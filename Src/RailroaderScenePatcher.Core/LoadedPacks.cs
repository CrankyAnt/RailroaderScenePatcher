// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    // One loaded asset pack that carries a patch document, with the supplying metadata of its Info.json.
    public sealed class LoadedPack {
        public const string PatcherId = "crankyant/scene-patcher";
        public LoadedPack(PatchSource source, string json, IEnumerable<string> dependencies, bool editTarget = false, string error = null) {
            Source = source ?? throw new ArgumentNullException(nameof(source)); Json = json; EditTarget = editTarget; Error = error;
            Dependencies = (dependencies ?? Enumerable.Empty<string>()).Where(d => !String.IsNullOrWhiteSpace(d)).Distinct(StringComparer.Ordinal).ToArray();
        }
        public PatchSource Source { get; }
        public string Json { get; }
        // Identifiers the pack lists under dependencies.required, without their version constraints.
        public IReadOnlyList<string> Dependencies { get; }
        // The editor's active edited MapMod, applied before Save has declared the patcher dependency.
        public bool EditTarget { get; }
        // Set when the document or supplying metadata cannot be read; the pack is skipped as a whole.
        public string Error { get; }
        public bool DeclaresPatcher => Dependencies.Contains(PatcherId, StringComparer.Ordinal);
        public bool Applicable => Error == null && (EditTarget || DeclaresPatcher);
        public static LoadedPack Read(string identifier, string info, string json, bool editTarget = false) {
            if (String.IsNullOrWhiteSpace(identifier)) throw new ArgumentException("A loaded pack needs its identifier", nameof(identifier));
            try {
                var metadata = JObject.Parse(info);
                var required = metadata["dependencies"]?["required"] as JArray;
                var dependencies = required?.Where(d => d.Type == JTokenType.String).Select(d => ((string)d).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()) ?? Enumerable.Empty<string>();
                var source = new PatchSource(identifier, Text(metadata, "name") ?? identifier, Text(metadata, "version"), Text(metadata, "author"), Text(metadata, "website"));
                return new LoadedPack(source, json, dependencies, editTarget, json == null ? "Cannot read patch document" : null);
            } catch (Exception e) when (!(e is OutOfMemoryException)) {
                return new LoadedPack(new PatchSource(identifier, identifier, null), json, null, editTarget, "Cannot read patch document or supplying metadata: " + e.GetType().Name);
            }
        }
        static string Text(JObject obj, string name) => obj[name]?.Type == JTokenType.String ? (string)obj[name] : null;
    }
    // What happened to one loaded pack during a load-order pass.
    public sealed class LoadedDocument {
        internal LoadedDocument(LoadedPack pack) { Pack = pack; }
        public LoadedPack Pack { get; }
        // Null when the pack was not handed to the engine: unreadable or not applicable.
        public DocumentResult Result { get; internal set; }
        public string Skipped { get; internal set; }
        // A failure report or overlap notice that could not be delivered; the document's applied work is unaffected.
        public string PresentationError { get; internal set; }
    }
    // Receives informational notices for the structured native collector. A notice with a known Key replaces that row.
    public interface IOverlapNotices { void Notice(OverlapNotice notice); }
    // Different supplying MapMods attempt to modify the same uniquely resolved existing object. It reports possible
    // incompatibility only: it is not a failure, chooses no winner and does not stop application.
    public sealed class OverlapNotice {
        internal OverlapNotice(string key, TargetIdentity target, IReadOnlyList<LoadedPack> packs) {
            Key = key; Target = target; Packs = packs.Select(p => p.Source).ToArray();
            var dependencies = new List<string>();
            foreach (var dependent in packs)
                foreach (var prerequisite in packs)
                    if (!ReferenceEquals(dependent, prerequisite) && dependent.Dependencies.Contains(prerequisite.Source.Identifier, StringComparer.Ordinal))
                        dependencies.Add(Name(dependent.Source) + " declares a dependency on " + Name(prerequisite.Source));
            Dependencies = dependencies;
            Message = "Supplying MapMods " + String.Join(", ", Packs.Select(Name)) + " each modify the existing object " + target.Scene + "/" + target.Path + " [" + target.Component + "]. " +
                "This indicates a possible incompatibility, not a confirmed conflict. " + (dependencies.Count == 0 ? "" : String.Join("; ", dependencies) + "; a dependency does not establish that the changes are compatible. ") +
                "Application continued in load order; no change was undone and no winner was chosen. Check the MapMods' descriptions if the result looks wrong.";
        }
        static string Name(PatchSource source) => (source.Name ?? source.Identifier) + " [" + source.Identifier + "]";
        public string Source => "Railroader Scene Patcher";
        public string Key { get; }
        public TargetIdentity Target { get; }
        // The supplying MapMods in load order.
        public IReadOnlyList<PatchSource> Packs { get; }
        // Declared dependencies among those MapMods, one description each.
        public IReadOnlyList<string> Dependencies { get; }
        public string Message { get; }
    }
}
