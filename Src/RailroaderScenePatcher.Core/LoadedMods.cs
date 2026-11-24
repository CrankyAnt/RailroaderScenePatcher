// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    // One loaded mod that carries a scene patch file, with the supplying metadata of its Info.json.
    public sealed class LoadedMod {
        public const string PatcherId = "crankyant/scene-patcher";
        public LoadedMod(PatchSource source, string json, IEnumerable<string> dependencies, bool editTarget = false, string error = null) {
            Source = source ?? throw new ArgumentNullException(nameof(source)); Json = json; EditTarget = editTarget; Error = error;
            Dependencies = (dependencies ?? Enumerable.Empty<string>()).Where(d => !String.IsNullOrWhiteSpace(d)).Distinct(StringComparer.Ordinal).ToArray();
        }
        public PatchSource Source { get; }
        public string Json { get; }
        // Identifiers the mod lists under dependencies.required, without their version constraints.
        public IReadOnlyList<string> Dependencies { get; }
        // The native map editor's edited mod, applied before Save has declared the patcher dependency.
        public bool EditTarget { get; }
        // Set when the scene patch file or supplying metadata cannot be read; the mod is skipped as a whole.
        public string Error { get; }
        public bool DeclaresPatcher => Dependencies.Contains(PatcherId, StringComparer.Ordinal);
        public bool Applicable => Error == null && (EditTarget || DeclaresPatcher);
        public static LoadedMod Read(string identifier, string info, string json, bool editTarget = false) {
            if (String.IsNullOrWhiteSpace(identifier)) throw new ArgumentException("A loaded mod needs its identifier", nameof(identifier));
            try {
                var metadata = JObject.Parse(info);
                var required = metadata["dependencies"]?["required"] as JArray;
                var dependencies = required?.Where(d => d.Type == JTokenType.String).Select(d => ((string)d).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()) ?? Enumerable.Empty<string>();
                var source = new PatchSource(identifier, Text(metadata, "name") ?? identifier, Text(metadata, "version"), Text(metadata, "author"), Text(metadata, "website"));
                return new LoadedMod(source, json, dependencies, editTarget, json == null ? "Cannot read scene patch file" : null);
            } catch (Exception e) when (!(e is OutOfMemoryException)) {
                return new LoadedMod(new PatchSource(identifier, identifier, null), json, null, editTarget, "Cannot read scene patch file or supplying metadata: " + e.GetType().Name);
            }
        }
        static string Text(JObject obj, string name) => obj[name]?.Type == JTokenType.String ? (string)obj[name] : null;
    }
    // What happened to one loaded mod during a load-order pass.
    public sealed class LoadedScenePatchFile {
        internal LoadedScenePatchFile(LoadedMod mod) { Mod = mod; }
        public LoadedMod Mod { get; }
        // Null when the mod was not handed to the engine: unreadable or not applicable.
        public ScenePatchFileResult Result { get; internal set; }
        public string Skipped { get; internal set; }
        // A failure report or overlap notice that could not be delivered; the scene patch file's applied work is unaffected.
        public string PresentationError { get; internal set; }
        // The map this scene patch file names as its source, or null when the scene patch file cannot be read that far. A scene patch file of
        // another format version names no map this patcher can read, so its log line reports the version, not a map.
        public string MapId {
            get {
                try { return Mod.Json == null || ScenePatchFile.FormatProblem(Mod.Json) != null ? null : (string)JObject.Parse(Mod.Json)["sourceMap"]?["mapId"]; }
                catch { return null; }
            }
        }
        // Writes what happened to this scene patch file in one load attempt. A file-level failure that gives no Log window row
        // is one player-mode warning; the outcome of every file is a debug line.
        public void WriteLog(int attempt, IReadOnlyList<string> mapIds) {
            var warning = Warning(mapIds);
            if (warning != null) PatcherLog.Warning(warning);
            PatcherLog.Debug(() => DebugLine(attempt, mapIds));
        }
        bool ForOtherMap(IReadOnlyList<string> maps) {
            var map = MapId;
            return map != null && maps.Count != 0 && !maps.Contains(map, StringComparer.Ordinal);
        }
        // The player-mode warning of this file, or null. A file that applied, a skip for another map and a failure that the
        // Log window shows get none: the Log window row is the player's line, written by the game.
        public string Warning(IReadOnlyList<string> mapIds) {
            var maps = mapIds ?? Array.Empty<string>();
            var name = (Mod.Source.Name ?? Mod.Source.Identifier) + " [" + Mod.Source.Identifier + "]";
            if (Result == null) {
                if (Mod.Error != null) return name + ": the scene patch file wasn't applied: " + Sentence(Mod.Error);
                if (!Mod.Applicable) return name + ": the scene patch file wasn't applied because the mod's Info.json doesn't list " + LoadedMod.PatcherId + " under dependencies.required.";
                return null;
            }
            if (Result.Error != null) {
                if (!Result.MapEditorOnly || Result.NotApplicable != null || ForOtherMap(maps)) return null;
                return name + ": the scene patch file wasn't applied: " + Sentence(Result.Error);
            }
            var unsupported = (Result.Outcomes ?? new Dictionary<string, Outcome>()).Where(o => !o.Value.Applied && o.Value.MapEditorOnly).ToArray();
            if (unsupported.Length == 0) return null;
            return name + ": " + (unsupported.Length == 1 ? "modification " + unsupported[0].Key + " wasn't applied" : "modifications " + String.Join(", ", unsupported.Select(o => o.Key)) + " weren't applied") +
                " because the game can't apply " + (unsupported.Length == 1 ? "it" : "them") + " in play: " + Sentence(String.Join("; ", unsupported.Select(o => o.Key + ": " + (o.Value.Error ?? "").TrimEnd('.'))));
        }
        static string Sentence(string text) { text = (text ?? "").Trim(); return text.EndsWith(".", StringComparison.Ordinal) ? text : text + "."; }
        // What happened to this scene patch file in one load attempt, as one debug line: the load attempt, the mod, the map
        // the scene patch file is for, and the outcome. mapIds is the loaded map followed by its layers.
        public string DebugLine(int attempt, IReadOnlyList<string> mapIds) {
            var maps = mapIds ?? Array.Empty<string>();
            var source = Mod.Source;
            var start = "load attempt " + attempt + ", map " + (maps.Count == 0 ? "unknown" : maps[0]) +
                " (layers " + (maps.Count < 2 ? "none" : String.Join(", ", maps.Skip(1))) + "): " + (source.Name ?? source.Identifier) + " [" + source.Identifier + "] scene patch file";
            var map = MapId;
            var named = start + (map == null ? "" : " for " + map);
            if (Result == null) return named + " skipped: " + (Skipped ?? "no application result or reason for skipping the file was recorded");
            if (ForOtherMap(maps)) return named + " skipped: not the loaded map or one of its layers";
            if (Result.Error != null) return named + " not applied: " + Result.Error;
            var outcomes = Result.Outcomes?.Values.ToArray() ?? Array.Empty<Outcome>();
            int failed = outcomes.Count(o => !o.Applied), unchanged = outcomes.Count(o => o.Applied && o.Unchanged);
            return named + ": " + (outcomes.Length - failed - unchanged) + " of " + outcomes.Length + " modifications applied, " + failed + " failed, " + unchanged + " unchanged";
        }
    }
    // Receives informational notices for the structured native collector. A notice with a known Key replaces that row.
    public interface IOverlapNotices { void Notice(OverlapNotice notice); }
    // Different supplying mods attempt to modify the same uniquely resolved existing target. It reports possible
    // incompatibility only: it is not a failure, chooses no winner and does not stop application.
    public sealed class OverlapNotice {
        internal OverlapNotice(string key, TargetIdentity target, IReadOnlyList<LoadedMod> mods) {
            Key = key; Target = target; Mods = mods.Select(p => p.Source).ToArray();
            var dependencies = new List<string>();
            foreach (var dependent in mods)
                foreach (var prerequisite in mods)
                    if (!ReferenceEquals(dependent, prerequisite) && dependent.Dependencies.Contains(prerequisite.Source.Identifier, StringComparer.Ordinal))
                        dependencies.Add(Name(dependent.Source) + " declares a dependency on " + Name(prerequisite.Source));
            Dependencies = dependencies;
            Message = "Supplying mods " + String.Join(", ", Mods.Select(Name)) + " each modify the existing target " + target.Label + ". " +
                "This indicates a possible incompatibility, not a confirmed conflict. " + (dependencies.Count == 0 ? "" : String.Join("; ", dependencies) + "; a dependency does not establish that the changes are compatible. ") +
                "Application continued in load order; no change was undone and no winner was chosen. Check the mods' descriptions if the result looks wrong.";
        }
        static string Name(PatchSource source) => (source.Name ?? source.Identifier) + " [" + source.Identifier + "]";
        public string Source => "Railroader Scene Patcher";
        public string Key { get; }
        public TargetIdentity Target { get; }
        // The supplying mods in load order.
        public IReadOnlyList<PatchSource> Mods { get; }
        // Declared dependencies among those mods, one description each.
        public IReadOnlyList<string> Dependencies { get; }
        public string Message { get; }
    }
}
