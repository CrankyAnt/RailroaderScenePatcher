// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    // The files of the edited mod. A read returns null when the file does not exist.
    public interface IStorage {
        string ReadScenePatchFile();
        void WriteScenePatchFile(string text);
        string ReadModInfo();
        void WriteModInfo(string text);
    }
    // The native map editor's shared undo history. Groups are listed oldest first.
    public interface INativeHistory {
        IReadOnlyList<object> UndoGroups { get; }
        IReadOnlyList<object> RedoGroups { get; }
        void Register(Action inverse, string name);
    }
    // The intended effect of a coverage edit on each circuit of a crossing, or why it would be refused.
    public sealed class CoveragePreview {
        internal CoveragePreview(IReadOnlyList<string> lines, string error) { Lines = lines; Error = error; }
        public IReadOnlyList<string> Lines { get; }
        public string Error { get; }
    }
    // The intended effect of an equipment edit on each addition of a crossing, or why it would be refused.
    public sealed class EquipmentPreview {
        internal EquipmentPreview(IReadOnlyList<string> lines, string error) { Lines = lines; Error = error; }
        public IReadOnlyList<string> Lines { get; }
        public string Error { get; }
    }
    public sealed class SaveReport {
        internal SaveReport(IEnumerable<string> completed, IEnumerable<string> outstanding, string error) {
            Completed = completed.ToArray(); Outstanding = outstanding.ToArray(); Error = error;
        }
        // Writes that finished during the attempt.
        public IReadOnlyList<string> Completed { get; }
        // Participants that still need a write after the attempt.
        public IReadOnlyList<string> Outstanding { get; }
        public string Error { get; }
    }
    // Identifies the state the undo history has reached, where the native change count cannot:
    // a state keeps its identity through Undo and Redo, a new edit or a pruned past gets a new one.
    internal sealed class HistoryIdentity {
        sealed class Token { internal long Value; }
        readonly ConditionalWeakTable<object, Token> tokens = new ConditionalWeakTable<object, Token>();
        readonly INativeHistory history;
        List<long> last = new List<long>();
        long next, floor, pendingUndo, pendingRedo;
        int undoCount, redoCount;
        bool executing;
        internal HistoryIdentity(INativeHistory history) { this.history = history; floor = ++next; Observe(); }
        long TokenFor(object group) { return tokens.GetValue(group, _ => new Token { Value = ++next }).Value; }
        static object Top(IReadOnlyList<object> groups) { return groups.Count == 0 ? null : groups[groups.Count - 1]; }
        internal long Current { get { Observe(); return last.Count == 0 ? floor : last[last.Count - 1]; } }
        internal void Observe() {
            if (executing) return;
            var now = history.UndoGroups.Select(TokenFor).ToList();
            if (last.Count > 0) {
                if (now.Count == 0) {
                    // Only an Undo of the single remaining group reaches the state below the stack.
                    if (last.Count != 1 || !history.RedoGroups.Any(g => TokenFor(g) == last[0])) floor = ++next;
                } else if (now[0] != last[0]) {
                    // The oldest groups were pruned: the state below the stack is now the newest pruned one.
                    var kept = last.IndexOf(now[0]);
                    floor = kept > 0 ? last[kept - 1] : ++next;
                }
            }
            last = now;
        }
        internal void Begin() {
            Observe();
            executing = true;
            undoCount = history.UndoGroups.Count; redoCount = history.RedoGroups.Count;
            pendingUndo = undoCount == 0 ? 0 : TokenFor(Top(history.UndoGroups));
            pendingRedo = redoCount == 0 ? 0 : TokenFor(Top(history.RedoGroups));
        }
        internal void End(bool failed) {
            if (!executing) return;
            executing = false;
            if (!failed) {
                var undo = history.UndoGroups; var redo = history.RedoGroups;
                if (undo.Count < undoCount && redo.Count > redoCount) Assign(Top(redo), pendingUndo);
                else if (redo.Count < redoCount && undo.Count > undoCount) Assign(Top(undo), pendingRedo);
            }
            Observe();
        }
        void Assign(object group, long value) { tokens.Remove(group); tokens.Add(group, new Token { Value = value }); }
    }
    internal static class DependencyMetadata {
        static readonly Regex Version = new Regex("^(\\d+)\\.(\\d+)\\.(\\d+)([a-z]?)$");
        static readonly string[] Operators = { ">=", "<=", "==", ">", "<", "~", "^" };
        internal static int Number(string version) {
            var match = Version.Match(version ?? "");
            int major, minor, patch;
            if (!match.Success || !Int32.TryParse(match.Groups[1].Value, out major) || !Int32.TryParse(match.Groups[2].Value, out minor) || !Int32.TryParse(match.Groups[3].Value, out patch) || major > 2146 || minor > 99 || patch > 99)
                throw new InvalidOperationException("'" + version + "' is not a mod version");
            return major * 1000000 + minor * 10000 + patch * 100 + (match.Groups[4].Value.Length == 0 ? 0 : match.Groups[4].Value[0] - 'a' + 1);
        }
        static bool Satisfies(int version, string op, int bound) {
            switch (op) {
                case ">=": return version >= bound;
                case ">": return version > bound;
                case "<=": return version <= bound;
                case "<": return version < bound;
                case "~": return version >= bound && version < (bound / 10000 + 1) * 10000;
                case "^": return version >= bound && version < (bound / 1000000 == 0 ? (bound / 10000 + 1) * 10000 : (bound / 1000000 + 1) * 1000000);
                default: return version == bound;
            }
        }
        // Returns the mod metadata with the required minimum, or null when it already requires at least that version.
        internal static string Raise(string info, string modId, string minimum) {
            if (info == null) throw new InvalidOperationException("The mod's Info.json is missing");
            PreservingJson json;
            try { json = PreservingJson.Parse(info); }
            catch (Exception e) when (e is JsonException || e is ArgumentException) { throw new InvalidOperationException("The mod's Info.json is unreadable: " + e.Message); }
            var root = json.Root as JObject;
            if (root == null || !json.Exact) throw new InvalidOperationException("The mod's Info.json cannot be rewritten without changing its content");
            if (root["dependencies"] == null) root["dependencies"] = new JObject();
            var dependencies = root["dependencies"] as JObject;
            if (dependencies == null) throw new InvalidOperationException("Info.json dependencies is not an object");
            if (dependencies["required"] == null) dependencies["required"] = new JArray();
            var required = dependencies["required"] as JArray;
            if (required == null || required.Any(x => x.Type != JTokenType.String)) throw new InvalidOperationException("Info.json required dependencies is not a list of strings");
            var entries = required.Where(x => ((string)x).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == modId).ToList();
            if (entries.Count > 1) throw new InvalidOperationException("Info.json lists " + modId + " more than once");
            var need = Number(minimum);
            var entry = modId + " >=" + minimum;
            if (entries.Count == 0) required.Add(entry);
            else {
                var parts = ((string)entries[0]).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var constraints = new List<KeyValuePair<string, string>>();
                for (var i = 1; i < parts.Length; i++) {
                    var text = parts[i];
                    if (Operators.Contains(text) && i + 1 < parts.Length) text += parts[++i];
                    var op = Operators.FirstOrDefault(o => text.StartsWith(o, StringComparison.Ordinal)) ?? "";
                    constraints.Add(new KeyValuePair<string, string>(op, text.Substring(op.Length).Trim()));
                }
                // An existing constraint that already excludes every lower version stays as it is.
                if (constraints.Any(c => c.Key != "<" && c.Key != "<=" && Number(c.Value) >= need)) return null;
                var kept = constraints.Where(c => c.Key != ">=" && c.Key != ">").ToList();
                var excluding = kept.FirstOrDefault(c => !Satisfies(need, c.Key, Number(c.Value)));
                if (excluding.Value != null) throw new InvalidOperationException("Info.json constrains " + modId + " to " + excluding.Key + excluding.Value + ", which excludes the required minimum " + minimum + ". Correct that constraint and save again");
                entries[0].Replace(String.Join(" ", new[] { entry }.Concat(kept.Select(c => (c.Key.Length == 0 ? "==" : c.Key) + c.Value))));
            }
            return json.Write();
        }
    }
    // One scene patch editing session for the edited mod: its scene patch file, the completed edits in the
    // shared native history, and the truth about which required writes are still outstanding.
    public sealed partial class PatchEditingSession {
        public const string NativeData = "native map data", PatchData = "scene patch file", Metadata = "dependency metadata";

        public static string DisplayItem(string item) => item == NativeData ? "map data" : item == Metadata ? "patcher dependency" : item;
        public static string OutstandingWriteList(IReadOnlyList<string> outstanding) {
            if (outstanding.Count < 2) return String.Join("", outstanding.Select(DisplayItem));
            var conjunction = outstanding.Count == 2 ? " and " : ", and ";
            return String.Join(", ", outstanding.Take(outstanding.Count - 1).Select(DisplayItem)) + conjunction + DisplayItem(outstanding[outstanding.Count - 1]);
        }
        readonly PatchEngine engine;
        readonly IScene scene;
        readonly INativeHistory history;
        readonly IStorage storage;
        readonly string modId;
        // The key the engine applied this scene patch file under at world load; it scopes the identities of the scene patch file's additions.
        readonly string scenePatchFileKey;
        // The key this session's scene patch file is applied under: the scope of its additions.
        public string ScenePatchFileKey => scenePatchFileKey;
        readonly HistoryIdentity identity;
        readonly HistoryIdentity nativeIdentity;
        bool historyExecuting, alignNativeCleanPending;
        readonly ConditionalWeakTable<Action, object> patchActions = new ConditionalWeakTable<Action, object>();
        long savedNativeIdentity;
        public bool HasNativeHistoryAccounting => nativeIdentity != null;
        void RegisterPatchAction(Action action, string name) { patchActions.Add(action, new object()); history.Register(action, name); }
        // Replaced only by a confirmed recovery of a protected scene patch file.
        ScenePatchFile scenePatchFile;
        readonly List<string> written = new List<string>();
        string diskText, info;
        // The content of the text last read or written, or null while no scene patch file was read or written.
        JToken saved;
        // The answer of the Saved comparison for the scene patch file version it was made for.
        int comparedVersion = -1;
        bool differs;
        long savedIdentity;
        bool uncertain;
        // Set when native Undo or Redo threw. The native manager then keeps an open partial group, so later
        // native edits reach neither its stack nor its change count. No Save in this session can prove a clean state.
        string unrecovered;
        // The scene target map a new scene patch file names: the lowest map layer the game composes for the edited map definition,
        // or that definition itself when it has no map layers. mapIds is the loaded map context as the game resolves it,
        // the edited map definition followed by all its map layers, layers of layers included, topmost first, so the last
        // entry is the bottom layer. An existing scene patch file keeps the scene target map it names; see ADR 0006.
        public static string SceneTargetMapFor(IEnumerable<string> mapIds) {
            var last = (mapIds ?? throw new ArgumentNullException(nameof(mapIds))).LastOrDefault();
            if (String.IsNullOrWhiteSpace(last)) throw new InvalidOperationException("Loaded map context unavailable");
            return last;
        }
        // The scene target map of the edited mod's scene patch file, or, while that scene patch file does not exist, the scene target map
        // scene patch editing would write for it. Null while the session cannot read the scene patch file. Copy ID names this map.
        public string SceneTargetMap => scenePatchFile == null ? null : (string)scenePatchFile.Root["sourceMap"]["mapId"];
        // Why Copy ID is unavailable in this session, or null: the session's own refusal while it cannot read the
        // edited mod's scene patch file, because that scene patch file names a scene target map the session does not know.
        public string CopyIdRefusal => scenePatchFile == null ? Blocked ?? "The scene patch file is unavailable" : null;
        // The inspected object's scene target map, target identity and placement as scene patch file JSON: what Copy ID copies.
        public string CopyId(SceneObservation entry) {
            var refusal = CopyIdRefusal;
            if (refusal != null) throw new InvalidOperationException(refusal);
            return CopyId(SceneTargetMap, entry);
        }
        // Copy ID without scene patch editing names the scene target map a new scene patch file would name for the loaded map context.
        public static string CopyId(IEnumerable<string> mapIds, SceneObservation entry) => CopyId(SceneTargetMapFor(mapIds), entry);
        static string CopyId(string sceneTargetMap, SceneObservation entry) {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            var target = new JObject { ["scene"] = entry.Identity.Scene, ["path"] = entry.Identity.Path, ["component"] = entry.Identity.Component };
            if (entry.Identity.Structure != null) target["structure"] = entry.Identity.Structure;
            return new JObject {
                ["sourceMap"] = new JObject { ["mapId"] = sceneTargetMap },
                ["target"] = target,
                ["placement"] = new JObject { ["mode"] = "fixed", ["position"] = new JArray(entry.Position), ["rotation"] = new JArray(entry.Rotation) }
            }.ToString();
        }
        public PatchEditingSession(PatchEngine engine, INativeHistory history, IStorage storage, string modId = "crankyant/scene-patcher", string scenePatchFileKey = "") {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.history = history ?? throw new ArgumentNullException(nameof(history));
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            this.modId = modId; this.scenePatchFileKey = scenePatchFileKey ?? ""; scene = engine.Scene;
            identity = new HistoryIdentity(history); savedIdentity = identity.Current;
            if (history is INativeHistoryActions) { nativeIdentity = new HistoryIdentity(new NativeOnlyHistory(history, action => patchActions.TryGetValue(action, out _))); savedNativeIdentity = nativeIdentity.Current; }
            string scenePatchFileError = null, metadataError = null;
            try { diskText = storage.ReadScenePatchFile(); }
            catch (System.Text.DecoderFallbackException e) {
                scenePatchFileError = InvalidUtf8Message;
                ScenePatchFileDiagnostics = new[] { InvalidUtf8Diagnostic(e) };
            } catch (Exception e) { scenePatchFileError = "The edited mod's scene patch file cannot be read. " + ScenePatchFile.Sentence(e.Message); }
            try { info = storage.ReadModInfo(); } catch (Exception e) { metadataError = e.Message; }
            // Unreadable mod metadata blocks the session whatever the scene patch file holds; starting the scene patch file fresh would not help.
            if (metadataError != null) { Blocked = "The edited mod's Info.json cannot be read. " + ScenePatchFile.Sentence(metadataError) + MetadataSuffix; return; }
            if (scenePatchFileError != null) Blocked = scenePatchFileError;
            else try {
                if (diskText == null) { scenePatchFile = ScenePatchFile.Create(SceneTargetMapFor(scene.MapIds)); ResetStartState(); return; }
                var loaded = ScenePatchFile.Load(diskText);
                Blocked = Refusal(loaded, diskText);
                if (Blocked == null) { scenePatchFile = loaded; saved = PreservingJson.Parse(diskText).Root; ResetStartState(); }
            } catch (Exception e) { Blocked = "The edited mod's scene patch file cannot be read. " + ScenePatchFile.Sentence(e.Message); }
            if (Blocked != null) { scenePatchFileProblem = true; Blocked = ScenePatchFile.Sentence(Blocked) + ProtectedSuffix; }
        }
        // Why scene patch editing and patch persistence are unavailable in this session, or null.
        public string Blocked { get; private set; }
        // A history or edit operation that did not complete, until a complete Save.
        public string IncompleteOperation { get; private set; }
        public SaveReport LastSave { get; private set; }
        public string ScenePatchFileText { get { return scenePatchFile == null ? diskText : scenePatchFile.Serialize(); } }
        // The lowest patcher version that supports every modification in the scene patch file, from its format content and its targets' capabilities.
        public string MinimumPatcherVersion {
            get {
                var minimum = "0.1.0";
                if (scenePatchFile == null) return minimum;
                foreach (var item in scenePatchFile.Items) {
                    if (item["active"] != null) minimum = Max(minimum, "0.2.0");
                    // Activity of a track-marker object may be the explicit disable of a group follower, which needs the group capability.
                    if (item["active"] != null && (item["target"] as JObject)?["structure"]?.Type == JTokenType.String && (string)item["target"]["structure"] == TrackMarkerScene.Structure) minimum = Max(minimum, TrackFollowingVersion);
                    // Track-following placement needs its capability whether or not it is available on this build.
                    if ((item["placement"] as JObject)?["mode"]?.Type == JTokenType.String && (string)item["placement"]["mode"] == PatchEngine.TrackFollowingMode) minimum = Max(minimum, TrackFollowingVersion);
                    // A relationship, and any edit of a track-marker object, needs the relationship capability whether or not it is available on this build.
                    if (item["relationships"] != null || ((item["target"] as JObject)?["structure"]?.Type == JTokenType.String && (string)item["target"]["structure"] == TrackMarkerScene.Structure) || ((item["target"] as JObject)?["component"]?.Type == JTokenType.String && (string)item["target"]["component"] == "Track.TrackMarker")) minimum = Max(minimum, TrackMarkerScene.RequiredVersion);
                    // Crossing coverage, and any edit of a crossing-coverage target, needs its capability whether or not it is available on this build.
                    if ((item["relationships"] as JObject)?["crossingCoverage"] != null || (item["relationships"] as JObject)?["crossingEquipment"] != null || ((item["target"] as JObject)?["structure"]?.Type == JTokenType.String && (string)item["target"]["structure"] == CrossingCoverageScene.Structure) || ((item["target"] as JObject)?["component"]?.Type == JTokenType.String && (string)item["target"]["component"] == CrossingCoverageScene.ComponentType)) minimum = Max(minimum, CrossingCoverageScene.RequiredVersion);
                    // Construction segments need their capability whether or not it is available on this build.
                    if ((item["relationships"] as JObject)?["constructionSegments"] != null) minimum = Max(minimum, ConstructionSegmentRule.RequiredVersion);
                    // Bridge placement needs its capability whether or not it is available on this build.
                    if ((item["target"] as JObject)?["structure"]?.Type == JTokenType.String && (string)item["target"]["structure"] == BridgeScene.Structure) minimum = Max(minimum, "0.5.0");
                    // A starter placement needs its capability whether or not it is available on this build.
                    if (item["starterPlacement"] != null || ((item["target"] as JObject)?["structure"]?.Type == JTokenType.String && (string)item["target"]["structure"] == StarterPlacementScene.Structure)) minimum = Max(minimum, StarterPlacementScene.RequiredVersion);
                    // Crossing-pad rebinding needs its capability whether or not it is available on this build.
                    if ((item["target"] as JObject)?["structure"]?.Type == JTokenType.String && (string)item["target"]["structure"] == CrossingPadScene.Structure) minimum = Max(minimum, CrossingPadScene.RequiredVersion);
                    // A milepost modification needs its capability whether or not it is available on this build.
                    if (((item["target"] as JObject)?["structure"]?.Type == JTokenType.String && (string)item["target"]["structure"] == MilepostScene.Structure) || ((item["target"] as JObject)?["component"]?.Type == JTokenType.String && (string)item["target"]["component"] == MilepostScene.Component)) minimum = Max(minimum, MilepostScene.RequiredVersion);
                    TargetIdentity target;
                    try { target = PatchEngine.ParseTarget(item["target"] as JObject); } catch (InvalidOperationException) { continue; }
                    // Board placement beyond the Sylva board needs its capability whether or not it is available on this build.
                    var board = IndependentBoards.RequiredVersion(target);
                    if (board != null) minimum = Max(minimum, board);
                    // A baked scenery group modification needs its capability whether or not it is available on this build.
                    if (target.Structure == BakedSceneryGroups.Structure) minimum = Max(minimum, BakedSceneryGroups.RequiredVersion);
                    // So does a modification of a part of a baked scenery group.
                    if (target.Structure == BakedSceneryParts.Structure) minimum = Max(minimum, BakedSceneryParts.RequiredVersion);
                    // So does a modification of a scene spawn point.
                    if (target.Structure == SpawnPoints.Structure || (target.Structure == null && target.Component == SpawnPoints.Component)) minimum = Max(minimum, SpawnPoints.RequiredVersion);
                    // So does a modification of a station map icon.
                    if (StationMapIconScene.Requires(target)) minimum = Max(minimum, StationMapIconRules.RequiredVersion);
                    // So does a modification of an existing crossing mast or bell.
                    if (ExistingEquipmentScene.Requires(target)) minimum = Max(minimum, ExistingEquipmentScene.RequiredVersion);
                    if (!(scene is IVersionedScene versioned)) continue;
                    var required = versioned.MinimumPatcherVersion(target);
                    if (required != null) minimum = Max(minimum, required);
                }
                return minimum;
            }
        }
        public const string TrackFollowingVersion = "0.5.0";
        static string Max(string a, string b) { return DependencyMetadata.Number(b) > DependencyMetadata.Number(a) ? b : a; }
        // Dependent native writes that follow the object's edits, such as terrain output regenerated from a board mask. Save does not write them.
        public DependentOutput Output(TargetIdentity target) { return OutputOf(Resolve(target)); }
        // Whether the active scene patch file's modification of this target sets its activity. Read from the scene patch file alone, as
        // IsChanged is. Clearing the activity value has something to clear only then.
        public bool HasActivityValue(TargetIdentity target) {
            if (scenePatchFile == null || target == null) return false;
            foreach (var item in scenePatchFile.Items) {
                var other = item["target"] as JObject;
                if (other == null || (string)other["scene"] != target.Scene || (string)other["path"] != target.Path || (string)other["component"] != target.Component) continue;
                var structure = other["structure"]?.Type == JTokenType.String ? (string)other["structure"] : null;
                if ((structure == null || target.Structure == null || structure == target.Structure) && item["active"] != null && item["active"].Type != JTokenType.Null) return true;
            }
            return false;
        }
        // Whether the active scene patch file holds a modification of this target. Read from the scene patch file alone, without
        // searching the scene, so a list can ask for every row. A target without a structure matches any structure.
        public bool IsChanged(TargetIdentity target) {
            if (scenePatchFile == null || target == null) return false;
            foreach (var item in scenePatchFile.Items) {
                var other = item["target"] as JObject;
                if (other == null || (string)other["scene"] != target.Scene || (string)other["path"] != target.Path || (string)other["component"] != target.Component) continue;
                var structure = other["structure"]?.Type == JTokenType.String ? (string)other["structure"] : null;
                if (structure == null || target.Structure == null || structure == target.Structure) return true;
            }
            return false;
        }
        // The object a target resolves to uniquely, or null. This searches the scene; a caller that reads output every
        // frame resolves once and reads OutputOf.
        public object Resolve(TargetIdentity target) {
            try { return engine.ResolveUnique(target); }
            catch (InvalidOperationException) { return null; }
        }
        // The dependent output of an object already resolved, without searching the scene.
        public DependentOutput OutputOf(object resolved) { return engine.Output(resolved); }
        // Requests outstanding dependent writes again. The edit and the scene patch file are unchanged.
        public Outcome RetryOutput(TargetIdentity target, InspectedTarget inspected = null) {
            var outcome = new Outcome();
            try {
                var resolved = engine.ResolveUnique(target);
                inspected?.Validate(scene, target, resolved);
                outcome.ResolvedTarget = resolved;
                if (!(scene is IDependentOutputScene dependent)) throw new InvalidOperationException("This object has no dependent output");
                if (engine.Output(resolved).Outstanding.Count == 0) throw new InvalidOperationException("No dependent output is outstanding");
                dependent.RetryOutput(resolved);
                var output = engine.Output(resolved);
                if (output.Error != null) throw new InvalidOperationException(output.Error);
                outcome.Applied = true;
            } catch (Exception e) { Fail(outcome, e); }
            return outcome;
        }

        public Outcome Edit(TargetIdentity target, Placement placement, bool? active) { return Edit(target, placement, active, null); }
        // An edit in scene patch editing passes the token of its inspected observation; the edit is refused when that observation no longer holds.
        public Outcome Edit(TargetIdentity target, Placement placement, bool? active, InspectedTarget inspected) {
            return Edit(target, placement, active, inspected, null);
        }
        // A one-time placement operation may depend on a selection in scene patch editing. Recheck it
        // after scene patch file/state callbacks, inside the same restoration boundary as the edit.
        public Outcome Edit(TargetIdentity target, Placement placement, bool? active, InspectedTarget inspected, Action validateBeforeMutation) {
            return Change("Edit scene object", target, inspected, existing => {
                if (placement == null && active == null) throw new InvalidOperationException("Nothing to change");
                if (placement != null) PatchEngine.CheckPlacement(placement.Position, placement.Rotation);
                if (existing != null) PatchEngine.Parse(existing);
                var value = new JObject { ["target"] = existing?["target"] ?? TargetJson(target) };
                // A placement edit of a track-following object keeps following the same track at the new relative placement.
                var following = existing == null ? null : PatchEngine.Parse(existing).State.Placement?.Following;
                if (placement != null && following != null) value["placement"] = PlacementJson(TrackFollowing.Relative(following.Anchor, TrackPose(following.Anchor), placement.Position, placement.Rotation));
                else if (placement != null) value["placement"] = FixedJson(placement);
                else if (existing?["placement"] != null) value["placement"] = existing["placement"].DeepClone();
                if (active != null) value["active"] = active.Value;
                else if (existing?["active"] != null) value["active"] = existing["active"].DeepClone();
                if (existing?["relationships"] != null) value["relationships"] = existing["relationships"].DeepClone();
                if (existing?["starterPlacement"] != null) value["starterPlacement"] = existing["starterPlacement"].DeepClone();
                value["dependsOn"] = existing?["dependsOn"]?.DeepClone() ?? new JArray();
                return value;
            }, validateBeforeMutation);
        }
        // Makes the object follow an explicitly selected track or endpoint from where it stands: the scene patch file records its
        // current placement relative to the anchor's current native pose. Activity and relationships keep their mod values.
        public Outcome Follow(TargetIdentity target, TrackAnchor anchor, InspectedTarget inspected = null, Action validateBeforeMutation = null) {
            return Change("Follow track", target, inspected, existing => {
                if (anchor == null) throw new InvalidOperationException("Explicitly select a track or endpoint to follow");
                if (existing != null) PatchEngine.Parse(existing);
                var current = scene.Read(engine.ResolveUnique(target)).Placement;
                PatchEngine.CheckPlacement(current?.Position, current?.Rotation);
                var following = TrackFollowing.Relative(anchor, TrackPose(anchor), current.Position, current.Rotation);
                return WithPlacement(target, existing, PlacementJson(following));
            }, validateBeforeMutation, "follow-");
        }
        // What the active scene patch file changes on the object: one short line per changed setting, each with the value the
        // object has without this modification. Empty when the scene patch file holds no modification of it.
        public IReadOnlyList<string> Changes(TargetIdentity target) {
            if (scenePatchFile == null || target == null) return new string[0];
            try {
                var resolved = engine.ResolveUnique(target);
                var own = Own(resolved);
                return own.Count == 1 ? ChangeLines(PatchEngine.Parse(own[0]).State, engine.Underlying(resolved)) : new string[0];
            } catch (InvalidOperationException) { return new string[0]; }
        }
        // The lines for one modification's state against the underlying state.
        public static IReadOnlyList<string> ChangeLines(ObjectState state, ObjectState before) {
            var lines = new List<string>();
            if (state == null) return lines;
            string Numbers(double[] values) => values == null ? "unknown" : String.Join(", ", values.Select(v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
            string Location(MarkerBinding binding) => binding == null ? "unknown" : binding.IsDetached ? "detached" : binding.Location.Display;
            var placement = state.Placement;
            if (placement != null) {
                var was = before?.Placement;
                if (placement.Following != null) lines.Add("Placement: follows track " + placement.Following.Anchor.Display + (was?.Position == null ? "" : "; was fixed at " + Numbers(was.Position)));
                else {
                    // A setting counts as changed beyond the tolerance of the equal-value rule: a rotation that
                    // went through the fields unchanged differs by rounding only.
                    var moved = placement.Position != null && !SamePosition(placement.Position, was?.Position);
                    var turned = placement.Rotation != null && !SameRotation(placement.Rotation, was?.Rotation);
                    if (moved) lines.Add("Position: from " + Numbers(was?.Position) + " to " + Numbers(placement.Position));
                    if (turned) lines.Add("Rotation: changed");
                    if (!moved && !turned) lines.Add("Placement: set to the placement the object has without this mod");
                }
            }
            // An underlying state that was read without an activity is one whose track group switches the object.
            if (state.Active.HasValue) lines.Add("Enabled: from " + (before == null ? "unknown" : before.Active == null ? "controlled by its track group" : before.Active.Value ? "Yes" : "No") + " to " + (state.Active.Value ? "Yes" : "No"));
            if (state.Marker != null) lines.Add("Track binding: from " + Location(before?.Marker) + " to " + Location(state.Marker));
            if (state.Starter?.Track != null) lines.Add("Track location: from " + Location(before?.Starter?.Track) + " to " + Location(state.Starter.Track));
            if (state.Starter?.Wreck != null) lines.Add("Start derailed: from " + (before?.Starter?.Wreck == null ? "unknown" : before.Starter.Wreck.Value ? "Yes" : "No") + " to " + (state.Starter.Wreck.Value ? "Yes" : "No"));
            if (state.Segments != null) lines.Add("Construction segments: from " + (before?.Segments?.Display ?? "unknown") + " to " + state.Segments.Display);
            if (state.Coverage != null) foreach (var circuit in state.Coverage.Circuits)
                lines.Add((DetectionAddition.Id(circuit.Key) == null ? "Circuit " + circuit.Key : "Detection addition " + DetectionAddition.Id(circuit.Key)) + ": " + circuit.Value);
            return lines;
        }
        // The track-following placement of the object in the active scene patch file, or null.
        public TrackFollowing FollowingOf(TargetIdentity target) {
            if (scenePatchFile == null) return null;
            try {
                var resolved = engine.ResolveUnique(target);
                var own = Own(resolved);
                return own.Count == 1 ? PatchEngine.Parse(own[0]).State.Placement?.Following : null;
            } catch (InvalidOperationException) { return null; }
        }
        // Whether any modification of the active scene patch file follows a track, read from the scene patch file alone.
        public bool HasTrackFollowing => scenePatchFile != null && Blocked == null && scenePatchFile.Items
            .Any(m => (m["placement"] as JObject)?["mode"]?.Type == JTokenType.String && (string)m["placement"]["mode"] == PatchEngine.TrackFollowingMode);
        // Ends track following: the object keeps its current placement as fixed placement.
        public Outcome StopFollowing(TargetIdentity target, InspectedTarget inspected = null) {
            return Change("Stop following track", target, inspected, existing => {
                if (existing == null || PatchEngine.Parse(existing).State.Placement?.Following == null) throw new InvalidOperationException("This object does not follow a track in the active scene patch file");
                var current = scene.Read(engine.ResolveUnique(target)).Placement;
                return WithPlacement(target, existing, FixedJson(PatchEngine.CheckPlacement(current.Position, current.Rotation)));
            });
        }
        JObject WithPlacement(TargetIdentity target, JObject existing, JObject placement) {
            var value = new JObject { ["target"] = existing?["target"] ?? TargetJson(target) };
            value["placement"] = placement;
            if (existing?["active"] != null) value["active"] = existing["active"].DeepClone();
            if (existing?["relationships"] != null) value["relationships"] = existing["relationships"].DeepClone();
            value["dependsOn"] = existing?["dependsOn"]?.DeepClone() ?? new JArray();
            return value;
        }
        TrackPose TrackPose(TrackAnchor anchor) {
            if (!(scene is ITrackPoseScene tracks)) throw new InvalidOperationException("Track-following placement is unavailable in this scene");
            return tracks.TrackPose(anchor);
        }
        static JObject FixedJson(Placement placement) => new JObject { ["mode"] = "fixed", ["position"] = new JArray(placement.Position.Cast<object>().ToArray()), ["rotation"] = new JArray(placement.Rotation.Cast<object>().ToArray()) };
        static JObject PlacementJson(TrackFollowing following) {
            PatchEngine.CheckPlacement(following.Offset, following.Rotation);
            var anchor = following.Anchor;
            var track = anchor.Node != null ? new JObject { ["node"] = anchor.Node }
                : new JObject { ["segment"] = anchor.Location.Segment, ["end"] = anchor.Location.End, ["distance"] = anchor.Location.Distance };
            return new JObject { ["mode"] = PatchEngine.TrackFollowingMode, ["track"] = track,
                ["offset"] = new JArray(following.Offset.Cast<object>().ToArray()), ["rotation"] = new JArray(following.Rotation.Cast<object>().ToArray()) };
        }
        // Moves every object of the active scene patch file that follows a track to its placement relative to the track's current
        // native pose. Neither the scene patch file nor the history changes. Each moved or failed object is reported by its
        // modification ID; a failure restores the object's prior state or guards it until a recheck.
        // Reconciliation runs several times a second, and it resolves the target of every modification in the scene patch file
        // to compute a followed object's desired state. Resolving searches the whole scene. A resolved target is therefore
        // kept while the scene confirms it is still that object, and resolved again every fortieth reconciliation, ten
        // seconds at four a second, so a second object of the same identity is still noticed. A scene without that
        // confirmation is searched every time. The guard before a mutation always searches afresh.
        readonly Dictionary<string, KeyValuePair<object, int>> reconciled = new Dictionary<string, KeyValuePair<object, int>>(StringComparer.Ordinal);
        const int ReconcileResolveInterval = 40;
        int reconciliations;
        object ReconcileTarget(TargetIdentity identity) {
            var key = identity.Scene + "|" + identity.Path + "|" + identity.Component + "|" + identity.Structure;
            if (scene is ITargetLiveness liveness && reconciled.TryGetValue(key, out var known) && reconciliations - known.Value < ReconcileResolveInterval && liveness.IsLive(known.Key, identity)) return known.Key;
            reconciled.Remove(key);
            var resolved = engine.ResolveFresh(identity);
            reconciled[key] = new KeyValuePair<object, int>(resolved, reconciliations);
            return resolved;
        }
        public IReadOnlyList<KeyValuePair<string, Outcome>> ReconcileTrackFollowing() {
            reconciliations++;
            engine.ResolveOverride = ReconcileTarget;
            try { return ReconcileFollowing(); }
            finally { engine.ResolveOverride = null; }
        }
        IReadOnlyList<KeyValuePair<string, Outcome>> ReconcileFollowing() {
            var results = new List<KeyValuePair<string, Outcome>>();
            if (scenePatchFile == null || Blocked != null) return results;
            var items = scenePatchFile.Items.ToList();
            foreach (var stale in heldTargets.Keys.Where(k => !items.Any(i => ReferenceEquals(i, heldTargets[k].Item))).ToList()) heldTargets.Remove(stale);
            foreach (var item in items) {
                PatchEngine.Modification parsed;
                try { parsed = PatchEngine.Parse(item); } catch (InvalidOperationException) { continue; }
                if (parsed.State.Placement?.Following == null) continue;
                if (Current(item, parsed, items)) continue;
                var held = HeldUpdate(item, parsed, items);
                if (held != null) { results.Add(new KeyValuePair<string, Outcome>(parsed.Id, held)); continue; }
                var outcome = new Outcome();
                object resolved;
                try { resolved = engine.ResolveUnique(parsed.Identity); } catch (InvalidOperationException) { heldTargets.Remove(parsed.Id); continue; }
                heldTargets[parsed.Id] = new Held { Item = item, Target = resolved };
                outcome.ResolvedTarget = resolved;
                var report = true;
                try {
                    if (engine.IsUnsafe(resolved)) throw engine.Guarded(resolved, "Cleanup for this object could not be verified. Select Recheck or reload the Map Editor before following the track again");
                    var mapContext = scene.MapIds.ToArray();
                    scene.Preflight(resolved);
                    var desired = engine.Desired(scenePatchFile.Modifications, resolved, scenePatchFileKey);
                    var prior = scene.Read(resolved);
                    // A placement that already agrees can still leave derived state, such as a pad's generated geometry, behind its track.
                    if (Same(prior, desired) && (!(scene is IDerivedStateScene derived) || derived.DerivedProblem(resolved, desired) == null)) { report = false; continue; }
                    engine.GuardMutation(parsed.Identity, resolved, mapContext, null);
                    try {
                        FollowUpdate(resolved, desired);
                        if (!Same(scene.Read(resolved), desired)) throw new InvalidOperationException("Applied state did not verify");
                    } catch (Exception e) {
                        var restored = false; string restoreError = null;
                        try { FollowUpdate(resolved, prior); restored = Same(scene.Read(resolved), prior); } catch (Exception r) { restoreError = r.Message; }
                        if (restored) throw new AttemptFailure(FailureKind.Restored, "Following the track failed. The object is back as it was. Reason: " + e.Message);
                        var left = Core.RemainingState.Describe(prior, () => scene.Read(resolved), restoreError);
                        engine.SetUnsafe(resolved, true, left);
                        throw new AttemptFailure(FailureKind.IncompleteCleanup, "Following the track failed. Cleanup could not be verified. Reason: " + e.Message, left);
                    }
                    outcome.Applied = true;
                } catch (Exception e) { Fail(outcome, e); }
                finally { if (report) results.Add(new KeyValuePair<string, Outcome>(parsed.Id, outcome)); }
            }
            return results;
        }
        // The target each following modification resolved to, while that modification object stays in the scene patch file.
        sealed class Held { internal JObject Item; internal object Target; }
        readonly Dictionary<string, Held> heldTargets = new Dictionary<string, Held>(StringComparer.Ordinal);
        // The check of a reconciliation pass that needs no scene-wide search, reference scan or rebuild: the held target is
        // still the object the scene identifies, it is the modification's only target in the scene patch file, and its state and
        // derived state already agree with the modification evaluated at the anchor's current pose. Nothing is mutated, so
        // no preflight is needed; any disagreement or doubt takes the full path, which resolves and preflights again.
        bool Current(JObject item, PatchEngine.Modification parsed, List<JObject> items) {
            if (!heldTargets.TryGetValue(parsed.Id, out var held) || !ReferenceEquals(held.Item, item)) return false;
            if (!(scene is ITargetConfirmation confirmation)) return false;
            try {
                if (!confirmation.StillIdentifies(held.Target, parsed.Identity) || engine.IsUnsafe(held.Target)) return false;
                var key = TargetKey(item);
                if (items.Count(i => !ReferenceEquals(i, item) && TargetKey(i) == key) != 0) return false;
                var underlying = engine.Underlying(held.Target);
                var desired = engine.Evaluate(new ObjectState(parsed.State.Placement, parsed.State.Active ?? underlying.Active, parsed.State.Marker ?? underlying.Marker, CrossingCoverage.Overlay(underlying.Coverage, parsed.State.Coverage), StarterSetting.Overlay(underlying.Starter, parsed.State.Starter)));
                if (!Same(scene.Read(held.Target), desired)) return false;
                return !(scene is IDerivedStateScene derived) || derived.DerivedProblem(held.Target, desired) == null;
            } catch (Exception) { return false; }
        }
        // A follow update of a held target that changes only its placement or derived state, such as during a track drag. The
        // scene confirms the held object, a placement preflight checks its own pattern and registration without scene-wide
        // scans, and the relationship, activity and coverage must already agree, so the update writes placement only.
        // Null when the update needs the full path.
        Outcome HeldUpdate(JObject item, PatchEngine.Modification parsed, List<JObject> items) {
            if (!heldTargets.TryGetValue(parsed.Id, out var held) || !ReferenceEquals(held.Item, item)) return null;
            if (!(scene is ITargetConfirmation confirmation) || !(scene is IPlacementPreflight placement)) return null;
            object target = held.Target; ObjectState desired, prior;
            try {
                if (!confirmation.StillIdentifies(target, parsed.Identity) || engine.IsUnsafe(target)) return null;
                var key = TargetKey(item);
                if (items.Count(i => !ReferenceEquals(i, item) && TargetKey(i) == key) != 0) return null;
                var underlying = engine.Underlying(target);
                desired = engine.Evaluate(new ObjectState(parsed.State.Placement, parsed.State.Active ?? underlying.Active, parsed.State.Marker ?? underlying.Marker, CrossingCoverage.Overlay(underlying.Coverage, parsed.State.Coverage), StarterSetting.Overlay(underlying.Starter, parsed.State.Starter)));
                prior = scene.Read(target);
                if (prior.Active != desired.Active || !MarkerBinding.Same(prior.Marker, desired.Marker) || !CrossingCoverage.Same(prior.Coverage, desired.Coverage) || !StarterSetting.Same(prior.Starter, desired.Starter)) return null;
                placement.PreflightPlacement(target);
            } catch (Exception) { return null; }
            var outcome = new Outcome { ResolvedTarget = target };
            try {
                FollowUpdate(target, desired);
                if (!Same(scene.Read(target), desired)) throw new InvalidOperationException("Applied state did not verify");
                outcome.Applied = true;
            } catch (Exception e) {
                var restored = false; string restoreError = null;
                try { FollowUpdate(target, prior); restored = Same(scene.Read(target), prior); } catch (Exception r) { restoreError = r.Message; }
                if (restored) Fail(outcome, new AttemptFailure(FailureKind.Restored, "Following the track failed. The object is back as it was. Reason: " + e.Message));
                else {
                    var left = Core.RemainingState.Describe(prior, () => scene.Read(target), restoreError);
                    engine.SetUnsafe(target, true, left);
                    Fail(outcome, new AttemptFailure(FailureKind.IncompleteCleanup, "Following the track failed. Cleanup could not be verified. Reason: " + e.Message, left));
                }
            }
            return outcome;
        }
        void FollowUpdate(object target, ObjectState state) { if (scene is IFollowUpdateScene follow) follow.ApplyFollowUpdate(target, state); else scene.Apply(target, state); }
        static string TargetKey(JObject item) {
            var target = item["target"] as JObject;
            return target == null ? null : (string)target["scene"] + "\n" + (string)target["path"] + "\n" + (string)target["component"];
        }
        // Sets, replaces or detaches this mod's track-marker relationship of the object. Placement and activity keep their mod values.
        // A null binding clears the mod's relationship: the object returns to its effective underlying relationship.
        public Outcome Bind(TargetIdentity target, MarkerBinding binding) { return Bind(target, binding, null); }
        public Outcome Bind(TargetIdentity target, MarkerBinding binding, InspectedTarget inspected) {
            return Change(binding == null ? "Clear track binding" : "Edit track binding", target, inspected, existing => {
                if (existing != null) PatchEngine.Parse(existing);
                if (binding == null) {
                    if (existing?["relationships"] == null) throw new InvalidOperationException("This object has no relationship set by this mod in the active scene patch file");
                    var rest = (JObject)existing.DeepClone();
                    rest.Remove("relationships");
                    return Empty(rest) ? null : rest;
                }
                var value = new JObject { ["target"] = existing?["target"] ?? TargetJson(target) };
                if (existing?["placement"] != null) value["placement"] = existing["placement"].DeepClone();
                if (existing?["active"] != null) value["active"] = existing["active"].DeepClone();
                if (existing?["starterPlacement"] != null) value["starterPlacement"] = existing["starterPlacement"].DeepClone();
                value["relationships"] = new JObject { ["trackMarker"] = binding.IsDetached ? new JObject { ["detach"] = true }
                    : new JObject { ["segment"] = binding.Location.Segment, ["end"] = binding.Location.End, ["distance"] = binding.Location.Distance } };
                value["dependsOn"] = existing?["dependsOn"]?.DeepClone() ?? new JArray();
                return value;
            }, null, "bind-");
        }
        // Sets or replaces this mod's construction segments of a rail bridge construction with exactly the selected
        // segments. Null ids clear the mod's value: the construction returns to its effective underlying segments. detach
        // writes an explicit detach, which every capability refuses with its reason. Placement and activity keep their mod
        // values.
        public Outcome SetConstructionSegments(TargetIdentity target, IReadOnlyList<string> ids, InspectedTarget inspected, bool detach = false) {
            return Change(ids == null && !detach ? "Clear construction segments" : "Edit construction segments", target, inspected, existing => {
                if (existing != null) PatchEngine.Parse(existing);
                var value = existing == null ? new JObject { ["target"] = TargetJson(target) } : (JObject)existing.DeepClone();
                var relationships = value["relationships"] as JObject;
                if (ids == null && !detach) {
                    if (relationships?["constructionSegments"] == null) throw new InvalidOperationException("This object has no construction segments set by this mod in the active scene patch file");
                    relationships.Remove("constructionSegments");
                    if (!relationships.HasValues) value.Remove("relationships");
                    return Empty(value) ? null : value;
                }
                if (relationships == null) value["relationships"] = relationships = new JObject();
                relationships["constructionSegments"] = detach ? new JObject { ["detach"] = true } : new JObject { ["segments"] = new JArray(ConstructionSegments.Of(ids).Sorted.Select(id => (JToken)id).ToArray()) };
                if (value["dependsOn"] == null) value["dependsOn"] = new JArray();
                return value;
            }, null, "segments-");
        }
        // Selects tracks for, or detaches, one circuit of this mod's crossing coverage. Other circuits keep their mod values or
        // effective coverage. A null coverage clears the entry set by this mod for the circuit: it returns to its effective underlying coverage.
        public Outcome Cover(TargetIdentity target, string circuit, CircuitCoverage coverage) { return Cover(target, circuit, coverage, null); }
        public Outcome Cover(TargetIdentity target, string circuit, CircuitCoverage coverage, InspectedTarget inspected) {
            return Change(coverage == null ? "Clear crossing coverage" : "Edit crossing coverage", target, inspected, existing => CoverageValue(target, existing, circuit, coverage, false), null, "cover-");
        }
        // Adds detection for explicitly selected tracks to the crossing as a detection addition with this ID, or replaces the
        // tracks of the addition that has it. The ID names one addition in the scene patch file. Native circuits keep their coverage.
        public Outcome AddDetection(TargetIdentity target, string id, CircuitCoverage tracks, InspectedTarget inspected = null) {
            return Change("Add crossing detection", target, inspected, existing => {
                if (tracks == null || tracks.Kind != CircuitKind.Added) throw new InvalidOperationException("Explicitly select the tracks of the approach A, island and approach B spans for the addition");
                return CoverageValue(target, existing, id, tracks, true);
            }, null, "cover-");
        }
        // Deletes the detection addition: its circuit leaves the crossing and its owned spans are removed. Other additions and
        // the coverage of the native circuits stay as they are.
        public Outcome DeleteDetection(TargetIdentity target, string id, InspectedTarget inspected = null) {
            return Change("Delete crossing detection", target, inspected, existing => CoverageValue(target, existing, id, null, true), null, "cover-");
        }
        // The effect AddDetection, or with null tracks DeleteDetection, would have on every circuit and addition, without
        // changing the scene, the scene patch file or the history.
        public CoveragePreview PreviewAddition(TargetIdentity target, string id, CircuitCoverage tracks, InspectedTarget inspected = null) {
            if (tracks != null && tracks.Kind != CircuitKind.Added) return new CoveragePreview(Array.Empty<string>(), "Explicitly select the tracks of the approach A, island and approach B spans for the addition");
            return Preview(target, id, tracks, inspected, true);
        }
        // The construction segments the object's construction controller names now, or null for an object without one.
        public ConstructionSegments ConstructionSegmentsOf(TargetIdentity target) {
            var resolved = engine.ResolveUnique(target);
            return scene.Read(resolved).Segments;
        }
        // Whether the active scene patch file sets construction segments for this object. Read from the scene patch file alone.
        public bool HasConstructionSegments(TargetIdentity target) {
            if (scenePatchFile == null || target == null) return false;
            foreach (var item in scenePatchFile.Items) {
                var other = item["target"] as JObject;
                if (other == null || (string)other["scene"] != target.Scene || (string)other["path"] != target.Path || (string)other["component"] != target.Component) continue;
                if (item["relationships"]?["constructionSegments"] != null) return true;
            }
            return false;
        }
        // The crossing's current coverage of every circuit, read from the scene.
        public CrossingCoverage Coverage(TargetIdentity target) {
            var resolved = engine.ResolveUnique(target);
            scene.Preflight(resolved);
            return scene.Read(resolved).Coverage ?? throw new InvalidOperationException("This object has no crossing coverage");
        }
        // The effect a Cover edit would have on every circuit, without changing the scene, the scene patch file or the history.
        public CoveragePreview PreviewCoverage(TargetIdentity target, string circuit, CircuitCoverage coverage, InspectedTarget inspected = null) => Preview(target, circuit, coverage, inspected, false);
        CoveragePreview Preview(TargetIdentity target, string circuit, CircuitCoverage coverage, InspectedTarget inspected, bool addition) {
            try {
                if (Blocked != null) throw new InvalidOperationException(Blocked);
                var resolved = engine.ResolveUnique(target);
                inspected?.Validate(scene, target, resolved);
                if (engine.IsUnsafe(resolved)) throw engine.Guarded(resolved, "Cleanup for this object could not be verified. Select Recheck or reload the Map Editor before editing it again");
                scene.Preflight(resolved);
                var own = Own(resolved);
                if (own.Count > 1) throw new InvalidOperationException("Several modifications in the scene patch file target this object; combine them outside scene patch editing first");
                var existing = own.FirstOrDefault();
                var value = CoverageValue(target, existing, circuit, coverage, addition);
                if (value != null) engine.CheckRelationships(resolved, PatchEngine.Parse(value, scenePatchFileKey).State);
                var key = existing == null ? NewId(target, "cover-") : ScenePatchFile.Key(existing);
                var candidate = (JObject)scenePatchFile.Modifications.DeepClone();
                if (value == null) candidate.Remove(key); else candidate[key] = value;
                var current = scene.Read(resolved).Coverage;
                var desired = engine.Desired(candidate, resolved, scenePatchFileKey).Coverage;
                CheckEffectiveCoverage(resolved, desired);
                return new CoveragePreview(CrossingCoverage.Describe(current, desired), null);
            } catch (Exception e) { return new CoveragePreview(Array.Empty<string>(), e.Message); }
        }
        // The effective coverage an edit converges on must itself be valid: removing an entry can leave a crossing without
        // detection although every remaining entry is valid on its own. Checked before mutation, so a refusal changes nothing.
        void CheckEffectiveCoverage(object target, CrossingCoverage desired) {
            if (desired != null) engine.CheckRelationships(target, new ObjectState(null, null, null, desired));
        }
        // The modification with one entry of its crossing coverage set, replaced or, for a null coverage, cleared. An entry
        // names a native circuit by its island span, or a detection addition by its ID.
        JObject CoverageValue(TargetIdentity target, JObject existing, string circuit, CircuitCoverage coverage, bool addition) {
            if (String.IsNullOrWhiteSpace(circuit)) throw new InvalidOperationException(addition ? "Give the addition an ID" : "Choose a circuit of the crossing");
            if (coverage != null && coverage.Kind == CircuitKind.Native) throw new InvalidOperationException("Clear the coverage set by this mod for the circuit to return it to its native spans");
            if (coverage != null && (coverage.Kind == CircuitKind.Added) != addition) throw new InvalidOperationException(addition ? "An addition needs its own selected tracks" : "Add detection for extra tracks as an addition; a native circuit takes selected tracks or a detach");
            if (existing != null) PatchEngine.Parse(existing);
            if (addition && coverage != null && scenePatchFile.Items.Any(m => !ReferenceEquals(m, existing) && PatchEngine.AdditionIds(m).Contains(circuit, StringComparer.Ordinal)))
                throw new InvalidOperationException("Addition ID " + circuit + " is already used by another modification of this scene patch file; an addition ID names one addition, so choose another ID");
            var name = addition ? "additions" : "circuits";
            var value = existing == null ? new JObject { ["target"] = TargetJson(target) } : (JObject)existing.DeepClone();
            var relationships = value["relationships"] as JObject ?? new JObject();
            var crossing = relationships["crossingCoverage"] as JObject ?? new JObject();
            var entries = crossing[name] as JObject ?? new JObject();
            if (coverage == null && entries.Property(circuit) == null) throw new InvalidOperationException(addition ? "Addition " + circuit + " is not set in the active scene patch file for this crossing" : "Circuit " + circuit + " has no coverage set by this mod in the active scene patch file");
            if (coverage == null) entries.Remove(circuit); else entries[circuit] = CircuitJson(coverage);
            // An object that lost its last entry leaves the scene patch file: circuits, additions and crossingCoverage are never written empty.
            if (entries.Count == 0) crossing.Remove(name); else if (entries.Parent == null) crossing[name] = entries;
            if (crossing.Count == 0) relationships.Remove("crossingCoverage"); else if (crossing.Parent == null) relationships["crossingCoverage"] = crossing;
            if (!relationships.HasValues) value.Remove("relationships"); else if (relationships.Parent == null) value["relationships"] = relationships;
            if (value["dependsOn"] == null) value["dependsOn"] = new JArray();
            return Empty(value) ? null : value;
        }
        // An entry of circuits or additions; its key names the circuit or the addition.
        static JObject CircuitJson(CircuitCoverage coverage) {
            var value = new JObject();
            if (coverage.Kind == CircuitKind.Detached) { value["detach"] = true; return value; }
            for (var i = 0; i < 3; i++) value[CircuitCoverage.Roles[i]] = new JObject { ["from"] = LocationJson(coverage.Ranges[i].From), ["to"] = LocationJson(coverage.Ranges[i].To) };
            return value;
        }
        static JObject LocationJson(TrackLocation location) => new JObject { ["segment"] = location.Segment, ["end"] = location.End, ["distance"] = location.Distance };
        // Adds this mod's equipment to a crossing controller, or replaces its namesake: an addition with the same ID is moved,
        // or rebuilt when its source or pattern changes. Other additions keep their mod values or effective state.
        public Outcome AddEquipment(TargetIdentity target, EquipmentAddition addition, InspectedTarget inspected = null) {
            if (addition == null) throw new ArgumentNullException(nameof(addition));
            return Change("Edit crossing equipment", target, inspected, existing => EquipmentValue(target, existing, addition, null), null, "equip-");
        }
        // Removes one of this mod's equipment additions: its copy is released from the controller and its culling.
        public Outcome RemoveEquipment(TargetIdentity target, string id, InspectedTarget inspected = null) {
            return Change("Remove crossing equipment", target, inspected, existing => EquipmentValue(target, existing, null, id), null, "equip-");
        }
        // The crossing's current equipment, read from the scene.
        public CrossingEquipment Equipment(TargetIdentity target) {
            var resolved = engine.ResolveUnique(target);
            scene.Preflight(resolved);
            return scene.Read(resolved).Equipment ?? throw new InvalidOperationException("This object has no crossing equipment");
        }
        // The loaded devices offered as sources for the crossing, eligible or with their concrete rejection reason.
        public IReadOnlyList<SourceCandidate> EquipmentSources(TargetIdentity target) {
            var resolved = engine.ResolveUnique(target);
            if (!(scene is ICrossingEquipmentScene equipment)) throw new InvalidOperationException("Crossing equipment editing is unavailable");
            return equipment.EquipmentCandidates(resolved);
        }
        // The initialization and culling state of one of this mod's additions, or null when it is not present.
        // An equipment ID from the stem that no modification of the scene patch file uses yet, with a numeric suffix when it is
        // taken, as a new modification key gets one. An invalid stem falls back to "equipment".
        // This mod's additions of the crossing as the scene patch file holds them, each with its built state in the scene or why it
        // was not built. The scene patch file's own order is the ordinal order of the IDs.
        public IReadOnlyList<EquipmentEntry> EquipmentEntries(TargetIdentity target) {
            var resolved = engine.ResolveUnique(target);
            var existing = Own(resolved).FirstOrDefault();
            if (existing == null) return Array.Empty<EquipmentEntry>();
            CrossingEquipment modEquipment;
            try { modEquipment = PatchEngine.Parse(existing, scenePatchFileKey).State.Equipment; } catch (InvalidOperationException) { return Array.Empty<EquipmentEntry>(); }
            if (modEquipment == null) return Array.Empty<EquipmentEntry>();
            CrossingEquipment observed = null;
            try { observed = scene.Read(resolved).Equipment; } catch (InvalidOperationException) { }
            var entries = new List<EquipmentEntry>();
            foreach (var addition in modEquipment.Additions) {
                var built = observed?.Find(addition.Key);
                string problem = null;
                if (built == null) {
                    try {
                        engine.CheckRelationships(resolved, new ObjectState(null, null, null, null, new CrossingEquipment(new[] { addition }, false)));
                        problem = "it is not built in this world; Recheck builds it";
                    } catch (InvalidOperationException e) { problem = e.Message; }
                }
                entries.Add(new EquipmentEntry(addition, built, problem));
            }
            return entries;
        }
        public string NewEquipmentId(string stem) {
            stem = new string((stem ?? "").Select(c => Char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' ? c : '-').ToArray()).Trim('-');
            if (stem.Length == 0 || stem.Length > 56) stem = "equipment";
            var used = new HashSet<string>(scenePatchFile == null ? Enumerable.Empty<string>() : scenePatchFile.Items.SelectMany(PatchEngine.AdditionIds).Where(a => a.StartsWith("equipment ", StringComparison.Ordinal)).Select(a => a.Substring("equipment ".Length)), StringComparer.Ordinal);
            var id = stem;
            for (var n = 2; used.Contains(id); n++) id = stem + "-" + n;
            return id;
        }
        public string EquipmentStatus(TargetIdentity target, string id) {
            var resolved = engine.ResolveUnique(target);
            return scene is ICrossingEquipmentScene equipment ? equipment.EquipmentStatus(resolved, scenePatchFileKey, id) : null;
        }
        // The effect of adding (addition) or removing (id) one addition, without changing the scene, the scene patch file or the history.
        public EquipmentPreview PreviewEquipment(TargetIdentity target, EquipmentAddition addition, string id, InspectedTarget inspected = null) =>
            PreviewEquipment(target, addition, id, inspected, null);
        public EquipmentPreview PreviewEquipment(TargetIdentity target, EquipmentAddition addition, string id, InspectedTarget inspected,
            Func<EquipmentAddition, string> displayName) {
            try {
                if (Blocked != null) throw new InvalidOperationException(Blocked);
                var resolved = engine.ResolveUnique(target);
                inspected?.Validate(scene, target, resolved);
                if (engine.IsUnsafe(resolved)) throw new InvalidOperationException("Cleanup for this object could not be verified. Select Recheck or reload the Map Editor before editing it again");
                scene.Preflight(resolved);
                var own = Own(resolved);
                if (own.Count > 1) throw new InvalidOperationException("Several modifications in the scene patch file target this object; combine them outside scene patch editing first");
                var existing = own.FirstOrDefault();
                var value = EquipmentValue(target, existing, addition, id);
                if (value != null) engine.CheckRelationships(resolved, PatchEngine.Parse(value, scenePatchFileKey).State);
                var key = existing == null ? NewId(target, "equip-") : ScenePatchFile.Key(existing);
                var candidate = (JObject)scenePatchFile.Modifications.DeepClone();
                if (value == null) candidate.Remove(key); else candidate[key] = value;
                var current = scene.Read(resolved).Equipment;
                var desired = engine.Desired(candidate, resolved, scenePatchFileKey).Equipment;
                return new EquipmentPreview(CrossingEquipment.Describe(current, desired, displayName), null);
            } catch (Exception e) { return new EquipmentPreview(Array.Empty<string>(), e.Message); }
        }
        // The modification with one entry of its crossing equipment set or, for a null addition, removed. An entry is keyed by
        // the equipment ID and holds no id member.
        JObject EquipmentValue(TargetIdentity target, JObject existing, EquipmentAddition addition, string id) {
            if (addition == null && String.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Choose an addition");
            if (existing != null) PatchEngine.Parse(existing);
            var key = addition?.Id ?? id;
            if (addition != null && scenePatchFile.Items.Any(m => !ReferenceEquals(m, existing) && PatchEngine.AdditionIds(m).Contains("equipment " + key, StringComparer.Ordinal)))
                throw new InvalidOperationException("Equipment ID " + key + " is already used by another modification of the scene patch file; choose another ID");
            var value = existing == null ? new JObject { ["target"] = TargetJson(target) } : (JObject)existing.DeepClone();
            var relationships = value["relationships"] as JObject ?? new JObject();
            var entries = relationships["crossingEquipment"] as JObject ?? new JObject();
            if (addition == null && entries.Property(key) == null) throw new InvalidOperationException("Equipment " + key + " is not an addition of the active scene patch file");
            if (addition == null) entries.Remove(key); else entries[key] = EquipmentJson(addition);
            // An object that lost its last entry leaves the scene patch file: crossingEquipment is never written empty.
            if (entries.Count == 0) relationships.Remove("crossingEquipment"); else if (entries.Parent == null) relationships["crossingEquipment"] = entries;
            if (!relationships.HasValues) value.Remove("relationships"); else if (relationships.Parent == null) value["relationships"] = relationships;
            if (value["dependsOn"] == null) value["dependsOn"] = new JArray();
            return value["placement"] == null && value["active"] == null && value["relationships"] == null ? null : value;
        }
        // An entry of crossingEquipment in canonical order: pattern, source, placement.
        static JObject EquipmentJson(EquipmentAddition addition) {
            var placement = PatchEngine.CheckPlacement(addition.Placement?.Position, addition.Placement?.Rotation);
            return new JObject { ["pattern"] = addition.Pattern, ["source"] = addition.Source.IsDefinition ? new JObject { ["definition"] = addition.Source.Definition } : new JObject { ["scene"] = addition.Source.Scene, ["path"] = addition.Source.Path }, ["placement"] = FixedJson(placement) };
        }
        // Clears this mod's activity value of the object: it returns to its effective underlying activity, which for an
        // object following a track group is group-driven again. Placement and relationships keep their mod values.
        public Outcome ClearActivity(TargetIdentity target, InspectedTarget inspected = null) {
            return Change("Clear activity", target, inspected, existing => {
                if (existing?["active"] == null) throw new InvalidOperationException("This object has no activity set by this mod in the active scene patch file");
                PatchEngine.Parse(existing);
                var rest = (JObject)existing.DeepClone();
                rest.Remove("active");
                return Empty(rest) ? null : rest;
            });
        }
        // A modification that sets nothing the patcher executes is no modification.
        static bool Empty(JObject value) => value["placement"] == null && value["active"] == null && value["relationships"] == null && value["starterPlacement"] == null;
        // Places a starter placement marker on the track: the scene patch file records the setup whose car placement reads the
        // marker and the new track location. The capability checks the location and the whole car cut before anything changes.
        public Outcome PlaceStarter(TargetIdentity target, TrackLocation location, InspectedTarget inspected = null) {
            return Change("Place starter marker", target, inspected, existing => {
                if (location == null) throw new InvalidOperationException("Choose a location on the track");
                if (existing != null) PatchEngine.Parse(existing);
                // The capability names why no single car placement reads the marker.
                var setup = StarterSetup(target);
                var value = existing == null ? new JObject { ["target"] = TargetJson(target) } : (JObject)existing.DeepClone();
                var starter = value["starterPlacement"] as JObject ?? new JObject();
                starter["setup"] = setup;
                starter["track"] = LocationJson(location);
                if (starter.Parent == null) value["starterPlacement"] = starter;
                if (value["dependsOn"] == null) value["dependsOn"] = new JArray();
                return value;
            }, null, "starter-");
        }
        // Clears this mod's track location of a starter placement marker: the marker returns to its effective underlying
        // track location. A modification left without a setting leaves the scene patch file.
        public Outcome ClearStarterTrack(TargetIdentity target, InspectedTarget inspected = null) {
            return Change("Clear starter track location", target, inspected, existing => {
                if (existing?["starterPlacement"]?["track"] == null) throw new InvalidOperationException("This mod does not set a track location for this marker, so there is nothing to clear");
                PatchEngine.Parse(existing);
                var rest = (JObject)existing.DeepClone();
                var starter = (JObject)rest["starterPlacement"];
                starter.Remove("track");
                if (!starter.Properties().Any(p => p.Name != "setup")) rest.Remove("starterPlacement");
                return Empty(rest) ? null : rest;
            });
        }
        // Sets the wreck setting of the car placement that reads a starter placement marker: the scene patch file records the setup
        // and the setting. A setting equal to the one without this mod leaves the modification, as any edit does.
        public Outcome SetStarterWreck(TargetIdentity target, bool wreck, InspectedTarget inspected = null) {
            return Change("Set starter wreck", target, inspected, existing => {
                if (existing != null) PatchEngine.Parse(existing);
                var setup = StarterSetup(target);
                var value = existing == null ? new JObject { ["target"] = TargetJson(target) } : (JObject)existing.DeepClone();
                var starter = value["starterPlacement"] as JObject ?? new JObject();
                starter["setup"] = setup;
                starter["wreck"] = wreck;
                if (starter.Parent == null) value["starterPlacement"] = starter;
                if (value["dependsOn"] == null) value["dependsOn"] = new JArray();
                return value;
            }, null, "starter-");
        }
        // The setup whose one car placement reads the marker; a marker that no car placement or several read is refused
        // with the reason the capability gives.
        string StarterSetup(TargetIdentity target) {
            var setup = scene.Read(engine.ResolveUnique(target)).Starter?.Setup;
            if (setup != null) return setup;
            engine.CheckRelationships(engine.ResolveUnique(target), new ObjectState(null, null, null, null, new StarterSetting("", null)));
            throw new InvalidOperationException(StarterPlacements.NoReader);
        }
        // The effective wreck setting of the car placement that reads the marker, or null when not exactly one does.
        public bool? StarterWreck(TargetIdentity target) {
            try { return scene.Read(engine.ResolveUnique(target)).Starter?.Wreck; } catch (Exception) { return null; }
        }
        // Whether the active scene patch file sets the track location of this starter placement marker. Read from the scene patch file alone.
        public bool HasStarterTrack(TargetIdentity target) => HasStarter(target, "track");
        // Whether the active scene patch file sets the wreck setting of this starter placement marker. Read from the scene patch file alone.
        public bool HasStarterWreck(TargetIdentity target) => HasStarter(target, "wreck");
        bool HasStarter(TargetIdentity target, string member) {
            if (scenePatchFile == null || target == null) return false;
            foreach (var item in scenePatchFile.Items) {
                var other = item["target"] as JObject;
                if (other == null || (string)other["scene"] != target.Scene || (string)other["path"] != target.Path || (string)other["component"] != target.Component) continue;
                if (item["starterPlacement"]?[member] != null) return true;
            }
            return false;
        }
        // The positions of the cut the marker's car placement would place at the location, or at its current track location
        // without one, front first: what the world shows.
        public IReadOnlyList<double[]> StarterCut(TargetIdentity target, TrackLocation location) {
            try {
                var resolved = engine.ResolveUnique(target);
                location = location ?? scene.Read(resolved).Starter?.Track?.Location;
                return scene is StarterPlacementScene starter ? starter.CutPath(resolved, location)
                    : scene is SceneCapabilities capabilities ? capabilities.StarterCut(resolved, location) : Array.Empty<double[]>();
            } catch (Exception) { return Array.Empty<double[]>(); }
        }
        // Removes this mod's saved or unsaved modification of the object; the object returns to its effective underlying state.
        public Outcome Remove(TargetIdentity target) { return Remove(target, null); }
        public Outcome Remove(TargetIdentity target, InspectedTarget inspected) {
            return Change("Remove scene modification", target, inspected, existing => {
                if (existing == null) throw new InvalidOperationException("This object has no modification in the active scene patch file");
                PatchEngine.Parse(existing);
                return null;
            });
        }
        // The prefix starts the key of a modification the edit adds; an existing modification keeps its key.
        Outcome Change(string name, TargetIdentity target, InspectedTarget inspected, Func<JObject, JObject> next, Action validateBeforeMutation = null, string prefix = "place-") {
            var outcome = new Outcome();
            try {
                if (Blocked != null) throw new InvalidOperationException(Blocked);
                var resolved = engine.ResolveUnique(target);
                inspected?.Validate(scene, target, resolved);
                outcome.ResolvedTarget = resolved;
                if (engine.IsUnsafe(resolved)) throw engine.Guarded(resolved, "Cleanup for this object could not be verified. Select Recheck or reload the Map Editor before editing it again");
                scene.Preflight(resolved);
                var own = Own(resolved);
                if (own.Count > 1) throw new InvalidOperationException("Several modifications in the scene patch file target this object; combine them outside scene patch editing first");
                var existing = own.FirstOrDefault();
                var ineffective = existing == null ? null : engine.IneffectiveReason(scenePatchFile.Modifications, ScenePatchFile.Key(existing), scenePatchFileKey);
                JObject value;
                try { value = next(existing); }
                catch (InvalidOperationException e) when (existing != null && e.Message.StartsWith("Unsupported", StringComparison.Ordinal)) {
                    throw new InvalidOperationException("The existing modification holds content this patcher version does not understand; it is preserved and cannot be edited or removed here");
                }
                // An edit in scene patch editing stores what it changes. A setting whose new value equals the object's value without
                // this mod leaves the modification: the edit is a revert of that setting. An edit that leaves the
                // scene patch file as it is changes nothing and adds no step to the native history, as a native field ignores
                // a value equal to the one it shows.
                var normal = Normalised(existing, value, engine.Underlying(resolved));
                outcome.Reverted = !ReferenceEquals(normal, value) || (value != null && normal != null && !PreservingJson.SameModification(normal, value));
                value = normal;
                // A modification that stays in the scene patch file but does not take effect would be reported as applied
                // without changing the object, so the edit is refused with the reason. Removing it remains possible.
                if (value != null && ineffective != null)
                    throw new InvalidOperationException("Modification " + ScenePatchFile.Key(existing) + " is not effective because " + ineffective + "; correct that first. Nothing was changed");
                // A modification another modification names in dependsOn cannot leave the scene patch file: the dependents would stop taking effect.
                if (value == null && existing != null) {
                    var dependents = Dependents(ScenePatchFile.Key(existing));
                    if (dependents.Count != 0)
                        throw new InvalidOperationException("Modification " + ScenePatchFile.Key(existing) + " cannot leave the scene patch file because " + String.Join(", ", dependents) + (dependents.Count == 1 ? " names" : " name") + " it in dependsOn; change or remove " + (dependents.Count == 1 ? "that modification" : "those modifications") + " first. Nothing was changed");
                }
                // An axis the edit leaves at the scene patch file's value as the game stores it keeps that value.
                if (value != null && existing != null) {
                    if (value.Parent != null) value = (JObject)value.DeepClone();
                    PreservingJson.KeepStoredPrecision(existing, value);
                }
                // Equal content is no change: a number written in another literal, or dependencies in another order, leave the scene patch file as it is.
                if (value == null ? existing == null : existing != null && PreservingJson.SameModification(existing, value)) { outcome.Reverted = false; outcome.Applied = true; outcome.Unchanged = true; return outcome; }
                var slot = existing == null ? NewId(target, prefix) : ScenePatchFile.Key(existing);
                if (value != null) {
                    if (value.Parent != null) value = (JObject)value.DeepClone();
                    // A number the edit leaves at its value keeps its source literal.
                    PreservingJson.KeepLiterals(existing, value);
                }
                Commit(target, resolved, inspected, slot, existing, value, validateBeforeMutation);
                var generation = versionGeneration; var keyGeneration = KeyGeneration(slot);
                stepKeys.Add(slot);
                RegisterPatchAction(() => Replay(name, slot, target, existing, generation, keyGeneration), name);
                outcome.Applied = true;
            } catch (Exception e) { Fail(outcome, e); }
            return outcome;
        }
        // Records a failure with its kind: a failure that names no mutation was refused before one started.
        static void Fail(Outcome outcome, Exception e) {
#if SCENE_PATCHER_FAULTS
            FaultInjection.EndAttempt();
#endif
            outcome.Error = e.Message;
            var attempt = e as AttemptFailure;
            outcome.Failure = attempt?.Kind ?? FailureKind.Refused;
            if (attempt != null) { outcome.Remaining = attempt.Remaining; outcome.WasGuarded = attempt.WasGuarded; }
        }
        // Whether an earlier failure left the object's cleanup unverified, and the remaining or uncertain state it named.
        // Read from the identities the engine resolved, without searching or mutating the scene, so the panel can ask
        // every frame. Restored dependent output that is still pending is not an unverified cleanup.
        public bool CleanupUnverified(TargetIdentity target) => engine.UnverifiedCleanup(target) != null;
        public IReadOnlyList<string> RemainingState(TargetIdentity target) => engine.UnverifiedCleanup(target) ?? Array.Empty<string>();
        // The refusal an edit of this object gets while an earlier failure guards it, as Blocked or AwaitingOutput, or null.
        // It is read without searching the scene, so the panel checks it before it compares the scene: the object an
        // incomplete cleanup left changed is refused for that cleanup, not as a stale inspection.
        public Outcome GuardOutcome(TargetIdentity target) {
            var guard = engine.GuardFor(target, "Cleanup is unverified");
            if (guard == null) return null;
            var outcome = new Outcome();
            Fail(outcome, guard);
            return outcome;
        }
        // The modification an edit stores: each setting the edit changed whose value equals the underlying value is
        // left out, and a modification that then sets nothing is no modification. Settings the edit did not touch stay
        // as they are, so an equal value written by hand in a scene patch file is kept. A position is equal within half a
        // millimetre and a rotation within about a hundredth of a degree: what the numeric fields can tell apart.
        static JObject Normalised(JObject existing, JObject value, ObjectState under) {
            if (value == null || under == null) return value;
            ObjectState state;
            try { state = PatchEngine.Parse(value).State; } catch (InvalidOperationException) { return value; }
            var result = (JObject)value.DeepClone();
            bool Changed(JToken was, JToken now) => now != null && !PreservingJson.SameValue(was, now);
            if (Changed(existing?["active"], value["active"]) && state.Active.HasValue && under.Active == state.Active) result.Remove("active");
            if (Changed(existing?["placement"], value["placement"]) && state.Placement != null && state.Placement.Following == null && SamePlacement(state.Placement, under.Placement)) result.Remove("placement");
            var relationships = result["relationships"] as JObject;
            if (relationships != null && Changed(existing?["relationships"]?["trackMarker"], relationships["trackMarker"]) && state.Marker != null && under.Marker != null && MarkerBinding.Same(state.Marker, under.Marker)) {
                relationships.Remove("trackMarker");
                if (!relationships.HasValues) result.Remove("relationships");
            }
            var starter = result["starterPlacement"] as JObject;
            if (starter != null && Changed(existing?["starterPlacement"]?["track"], starter["track"]) && state.Starter?.Track != null && under.Starter?.Track != null && MarkerBinding.Same(state.Starter.Track, under.Starter.Track))
                starter.Remove("track");
            if (starter != null && Changed(existing?["starterPlacement"]?["wreck"], starter["wreck"]) && state.Starter?.Wreck != null && under.Starter?.Wreck == state.Starter.Wreck)
                starter.Remove("wreck");
            if (starter != null && !starter.Properties().Any(p => p.Name != "setup")) result.Remove("starterPlacement");
            relationships = result["relationships"] as JObject;
            if (relationships != null && Changed(existing?["relationships"]?["constructionSegments"], relationships["constructionSegments"]) && state.Segments != null && under.Segments != null && ConstructionSegments.Same(state.Segments, under.Segments)) {
                relationships.Remove("constructionSegments");
                if (!relationships.HasValues) result.Remove("relationships");
            }
            if (PreservingJson.SameModification(result, value)) return value;
            return Empty(result) ? null : result;
        }
        static bool SamePlacement(Placement a, Placement b) {
            if (a == null || b == null || (a.Position == null && a.Rotation == null)) return false;
            return (a.Position == null || SamePosition(a.Position, b.Position)) && (a.Rotation == null || SameRotation(a.Rotation, b.Rotation));
        }
        // The tolerance within which two placements are the same setting, for the equal-value rule and for the lines of the
        // Changes tab alike: half a millimetre on each axis, and about a hundredth of a degree.
        public static bool SamePosition(IReadOnlyList<double> a, IReadOnlyList<double> b) {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++) if (Math.Abs(a[i] - b[i]) > .0005) return false;
            return true;
        }
        public static bool SameRotation(IReadOnlyList<double> a, IReadOnlyList<double> b) {
            if (a == null || b == null || a.Count != 4 || b.Count != 4) return false;
            double dot = 0, la = 0, lb = 0;
            for (var i = 0; i < 4; i++) { dot += a[i] * b[i]; la += a[i] * a[i]; lb += b[i] * b[i]; }
            return la > 0 && lb > 0 && Math.Abs(dot) / Math.Sqrt(la * lb) >= 1 - 1e-8;
        }
        // Runs inside native Undo or Redo. It must not throw: an exception there corrupts the native grouping.
        // Raised after Undo or Redo replayed one of this session's own edits on the target.
        public Action<TargetIdentity> Replayed;
        public Action<string> HistoryNotice;
        // Undo and Redo restore the modification under its key.
        // A step recorded before a file version replaced its entry, through Use file version or a file choice in Import, is a
        // no-op that stays in the native history.
        void Replay(string name, string slot, TargetIdentity target, JObject value, int generation, int keyGeneration) {
            if (generation != versionGeneration || keyGeneration != KeyGeneration(slot)) {
                RegisterPatchAction(() => Replay(name, slot, target, value, generation, keyGeneration), name);
                try { HistoryNotice?.Invoke("This discarded scene patch step no longer changes the scene patch file."); } catch { }
                return;
            }
            var current = scenePatchFile.Modifications[slot] as JObject;
            try {
                var resolved = engine.ResolveUnique(target);
                if (engine.IsUnsafe(resolved)) throw new InvalidOperationException("Cleanup for this object could not be verified");
                Commit(target, resolved, null, slot, current, value);
                // Undo and Redo of this patcher's own step changed this object; the owner observes it again.
                try { Replayed?.Invoke(target); } catch { }
            } catch (Exception e) {
                value = current;
                uncertain = true;
                IncompleteOperation = name + " could not be reversed or reapplied: " + e.Message + ". The scene patch file keeps the state shown; Save remains available.";
            }
            RegisterPatchAction(() => Replay(name, slot, target, current, generation, keyGeneration), name);
        }
        void Commit(TargetIdentity identity, object target, InspectedTarget inspected, string key, JObject current, JObject value, Action validateBeforeMutation = null) {
            var mapContext = scene.MapIds.ToArray();
            var prior = scene.Read(target);
            engine.Underlying(target);
            scenePatchFile.Replace(key, value);
            var mutating = false;
            try {
                var desired = engine.Desired(scenePatchFile.Modifications, target, scenePatchFileKey);
                // Only the mod's relationship needs a resolvable target; the underlying one may be the broken binding being repaired.
                // A starter placement is judged with the scene patch file's other starter placements, as at load.
                if (value != null) {
                    engine.Plan(scenePatchFile.Modifications, scenePatchFileKey);
                    try { engine.CheckRelationships(target, PatchEngine.Parse(value, scenePatchFileKey).State); }
                    finally { engine.Unplan(); }
                }
                CheckEffectiveCoverage(target, desired.Coverage);
                // Reading state and resolving the scene patch file may invoke native callbacks. Never redirect to a new match.
                engine.GuardMutation(identity, target, mapContext, inspected);
                validateBeforeMutation?.Invoke();
                // A prerequisite query can itself call native code. Keep its final check
                // from redirecting a mutation to an object replaced during that query.
                if (validateBeforeMutation != null) engine.GuardMutation(identity, target, mapContext, inspected);
                mutating = true;
                scene.Apply(target, desired);
#if SCENE_PATCHER_FAULTS
                FaultInjection.TripAfterMutation();
#endif
                if (!Same(scene.Read(target), desired)) throw new InvalidOperationException("Applied state did not verify");
            } catch (Exception e) {
                scenePatchFile.Replace(key, current);
                // A rejection before the mutation started changed nothing in the scene and needs no restoration.
                if (!mutating) throw;
                var restored = false; string restoreError = null;
                try {
#if SCENE_PATCHER_FAULTS
                    FaultInjection.TripRestoration();
#endif
                    scene.Apply(target, prior); restored = Same(scene.Read(target), prior);
                } catch (Exception r) { restoreError = r.Message; }
                if (!restored) {
                    var left = Core.RemainingState.Describe(prior, () => scene.Read(target), restoreError);
                    engine.SetUnsafe(target, true, left);
                    throw new AttemptFailure(FailureKind.IncompleteCleanup, "The edit failed. Cleanup could not be verified. Reason: " + e.Message, left);
                }
                if (engine.AwaitOutput(target)) throw new AttemptFailure(FailureKind.AwaitingOutput, "The edit failed and the object is back as it was. The game is still regenerating its dependent output. Editing can resume once that output is verified. Reason: " + e.Message);
                throw new AttemptFailure(FailureKind.Restored, "The edit failed. The object is back as it was. Reason: " + e.Message);
            }
        }
        // Explicit retry after unverified cleanup: rechecks the actual state against the scene patch file before unblocking. The
        // target is resolved, inspected and preflighted afresh. The desired state comes from the scene patch file, so the persistent
        // identities it names, such as addition IDs, decide what the object keeps: owned state that a failed or interrupted
        // attempt left behind and the scene patch file no longer names is removed, and owned state it names is reused, before
        // anything further is built.
        public Outcome Recheck(TargetIdentity target) { return Recheck(target, null); }
        public Outcome Recheck(TargetIdentity target, InspectedTarget inspected) {
            var outcome = new Outcome();
            try {
                if (Blocked != null) throw new InvalidOperationException(Blocked);
                var resolved = engine.ResolveUnique(target);
                inspected?.Validate(scene, target, resolved);
                outcome.ResolvedTarget = resolved;
                var mapContext = scene.MapIds.ToArray();
                scene.Preflight(resolved);
                var desired = engine.Desired(scenePatchFile.Modifications, resolved, scenePatchFileKey);
                if (!Same(scene.Read(resolved), desired)) {
                    engine.GuardMutation(target, resolved, mapContext, inspected);
                    try {
                        scene.Apply(resolved, desired);
                        if (!Same(scene.Read(resolved), desired)) throw new InvalidOperationException("Applied state did not verify");
                    } catch (Exception e) {
                        // The scene patch file is the reference here: what still differs from it remains.
                        var left = Core.RemainingState.Describe(desired, () => scene.Read(resolved), e.Message);
                        engine.SetUnsafe(resolved, true, left);
                        throw new AttemptFailure(FailureKind.IncompleteCleanup, "The object still does not match the scene patch file. Reason: " + e.Message, left);
                    }
                }
                engine.SetUnsafe(resolved, false);
                // Restored dependent output that stopped is requested again; the object stays guarded until it verifies.
                var output = engine.Output(resolved);
                if (output.Error != null && scene is IDependentOutputScene dependent) dependent.RetryOutput(resolved);
                engine.AwaitOutput(resolved);
                outcome.Applied = true;
            } catch (Exception e) { Fail(outcome, e); }
            return outcome;
        }
        // The keys of the other modifications whose dependsOn names the key, in ordinal order.
        List<string> Dependents(string key) {
            return scenePatchFile.Modifications.Properties()
                .Where(p => p.Name != key && (p.Value as JObject)?["dependsOn"] is JArray dependencies && dependencies.Any(d => d.Type == JTokenType.String && (string)d == key))
                .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }
        List<JObject> Own(object target) {
            var own = new List<JObject>();
            foreach (var item in scenePatchFile.Items) {
                try { if (ReferenceEquals(engine.ResolveUnique(PatchEngine.ParseTarget(item["target"] as JObject)), target)) own.Add(item); }
                catch (InvalidOperationException) { }
            }
            return own;
        }
        string NewId(TargetIdentity target, string prefix = "place-") {
            var name = new string(target.Path.Substring(target.Path.LastIndexOf('/') + 1).ToLowerInvariant().Select(c => Char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            // A name that already starts with the prefix, such as the starter placement marker starter-ewh-int2, keeps it once.
            var stem = name.Length == 0 ? prefix + "object" : name.StartsWith(prefix, StringComparison.Ordinal) ? name : prefix + name;
            var used = new HashSet<string>(scenePatchFile.Modifications.Properties().Select(m => m.Name), StringComparer.Ordinal);
            var id = stem;
            for (var n = 2; used.Contains(id); n++) id = stem + "-" + n;
            return id;
        }
        static JObject TargetJson(TargetIdentity target) {
            var value = new JObject { ["scene"] = target.Scene, ["path"] = target.Path, ["component"] = target.Component };
            if (target.Structure != null) value["structure"] = target.Structure;
            return value;
        }
        // The placement tolerances of the scene adapters' own verification. A wider angle here would count a following object
        // as current after its track turned by less than that angle, and the object would not follow.
        const double AngleToleranceDegrees = 0.01;
        static bool Same(ObjectState a, ObjectState b) {
            if (a == null || b == null || a.Active != b.Active || a.Placement == null || b.Placement == null || !MarkerBinding.Same(a.Marker, b.Marker) || !CrossingCoverage.Same(a.Coverage, b.Coverage) || !CrossingEquipment.Same(a.Equipment, b.Equipment) || !StarterSetting.Same(a.Starter, b.Starter) || !ConstructionSegments.Same(a.Segments, b.Segments)) return false;
            for (var i = 0; i < 3; i++) if (Math.Abs(a.Placement.Position[i] - b.Placement.Position[i]) > 0.01) return false;
            double dot = 0, la = 0, lb = 0;
            for (var i = 0; i < 4; i++) { dot += a.Placement.Rotation[i] * b.Placement.Rotation[i]; la += a.Placement.Rotation[i] * a.Placement.Rotation[i]; lb += b.Placement.Rotation[i] * b.Placement.Rotation[i]; }
            return la > 0 && lb > 0 && 2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(la * lb))) * 180 / Math.PI <= AngleToleranceDegrees;
        }

        // History notifications from the native map editor: around each native Undo or Redo, and after any history change.
        public void BeginHistoryExecution() { historyExecuting = true; identity.Begin(); nativeIdentity?.Begin(); }
        public void EndHistoryExecution(Exception error) {
            identity.End(error != null);
            nativeIdentity?.End(error != null);
            historyExecuting = false;
            if (alignNativeCleanPending && error == null && nativeIdentity != null) savedNativeIdentity = nativeIdentity.Current;
            alignNativeCleanPending = false;
            if (error == null) return;
            unrecovered = "Undo or Redo stopped on an error: " + error.Message + ". Undo history could not be recovered and may stop recording edits. Save still writes the current changes. Reload the Map Editor to clear its unsaved status.";
            IncompleteOperation = unrecovered;
        }
        // Raised after a history change outside native Undo and Redo was accounted, such as a patch-only edit, so the native
        // title and Save button can be rebuilt from IsDirty. Undo and Redo refresh once their history identity settled.
        public Action OutstandingMayHaveChanged;
        public void HistoryChanged() { identity.Observe(); nativeIdentity?.Observe(); if (!historyExecuting) OutstandingMayHaveChanged?.Invoke(); }
        // The native editor reached its pre-attachment clean state for the first time.
        public void AlignNativeCleanHistory() {
            if (historyExecuting) alignNativeCleanPending = true;
            else if (nativeIdentity != null) savedNativeIdentity = nativeIdentity.Current;
        }

        // The scene patch file is Saved exactly when its content equals the content of the text last read or written; member
        // order, spacing and the literal a number is written in do not count. Without such a text it is Saved while it has
        // no modification.
        bool PatchOutstanding {
            get {
                if (scenePatchFile == null) return false;
                // A fresh start stays outstanding until Save replaced the original, even without a modification.
                if (replacementBackup != null || acceptedVersion) return true;
                if (saved == null) return scenePatchFile.Modifications.Count != 0;
                if (comparedVersion != scenePatchFile.Version) { differs = !PreservingJson.SameContent(scenePatchFile.Root, saved); comparedVersion = scenePatchFile.Version; }
                return differs;
            }
        }
        // Whether the file differs from the text last read or written under the Saved equality. A reorder or other spacing
        // made outside scene patch editing is no change; another value, an unreadable file, a deleted file and a file that
        // appeared after a load that found none are.
        bool ChangedOutside(string current) {
            if (saved == null || current == null) return saved != null || current != null;
            try { return !PreservingJson.SameContent(PreservingJson.Parse(current).Root, saved); }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is InvalidOperationException || e is OverflowException) { return true; }
        }
        bool MetadataOutstanding {
            get {
                if (scenePatchFile == null || scenePatchFile.Modifications.Count == 0) return false;
                try { return DependencyMetadata.Raise(info, modId, MinimumPatcherVersion) != null; }
                catch (InvalidOperationException) { return true; }
            }
        }
        // The participants whose required write has not completed. nativeDirty includes the tracker's native work and unexplained changes.
        public IReadOnlyList<string> Outstanding(bool nativeDirty) {
            var result = new List<string>();
            if (nativeDirty || uncertain || unrecovered != null || (nativeIdentity == null ? identity.Current != savedIdentity : nativeIdentity.Current != savedNativeIdentity)) result.Add(NativeData);
            if (PatchOutstanding) result.Add(PatchData);
            if (MetadataOutstanding) result.Add(Metadata);
            return result;
        }
        public bool IsDirty(bool nativeDirty) { return Outstanding(nativeDirty).Count != 0; }
        // Work a failed Save leaves outstanding, or null when every write is complete.
        public string ExitWarning(bool nativeDirty) {
            var outstanding = Outstanding(nativeDirty);
            if (outstanding.Count == 0) return null;
            return "Changes to the " + OutstandingWriteList(outstanding) + " are still unsaved." + (IncompleteOperation == null ? "" : " " + IncompleteOperation);
        }

        // Why Save is refused now, or null: the file is checked again first, so a pending version choice, a file that needs
        // repair, an original or backup that changed after a fresh start or a version backup, and an Info.json the dependency
        // check cannot raise are all found before the native Save button writes terrain or map data.
        public string SaveRefusalBeforeWrite() {
            if (scenePatchFile == null) return null;
            if (Import != null) return ImportSaveRefusal;
            CheckFileVersion();
            if (SaveRefusal != null) return SaveRefusal;
            if (replacementBackup != null) {
                try { CheckReplacement(); }
                catch (Exception e) { var reason = e.Message; return reason.Length == 0 ? reason : Char.ToUpperInvariant(reason[0]) + reason.Substring(1); }
            }
            return DependencyRefusal();
        }
        // A dry run of the dependency write on a fresh read of Info.json: why it would fail, or null. Save reads and raises the
        // file again when it writes.
        string DependencyRefusal() {
            if (scenePatchFile.Modifications.Count == 0) return null;
            try { DependencyMetadata.Raise(storage.ReadModInfo(), modId, MinimumPatcherVersion); return null; }
            catch (Exception e) { return "The patcher dependency was not written: " + e.Message; }
        }
        // The Save route. BeginSave runs before the native map write and throws when a patcher write fails;
        // CompleteNativeSave follows the native write; FailSave follows any exception of the attempt.
        public void BeginSave() {
            written.Clear();
            var refusal = SaveRefusalBeforeWrite();
            if (refusal != null) throw new InvalidOperationException(refusal);
            if (scenePatchFile == null) return;
            if (scenePatchFile.Modifications.Count != 0) {
                try {
                    var fresh = storage.ReadModInfo();
                    var raised = DependencyMetadata.Raise(fresh, modId, MinimumPatcherVersion);
                    if (raised != null) { storage.WriteModInfo(raised); written.Add(Metadata); }
                    info = raised ?? fresh;
                } catch (Exception e) { throw new InvalidOperationException("The patcher dependency was not written: " + e.Message, e); }
            }
            if (!PatchOutstanding) return;
            try {
                if (replacementBackup != null) CheckReplacement();
                else { CheckFileVersion(); if (!CanSaveForReload) throw new InvalidOperationException(SaveRefusal); }
                var text = scenePatchFile.Serialize();
                storage.WriteScenePatchFile(text);
                diskText = text; saved = PreservingJson.Parse(text).Root; comparedVersion = -1; replacementBackup = null; versionBackup = false; backedVersionFingerprint = null; ClearVersionAcceptance(); VersionMessage = null; written.Add(PatchData);
            } catch (Exception e) { throw new InvalidOperationException("The scene patch file was not written: " + e.Message, e); }
        }
        public void CompleteNativeSave() {
            savedIdentity = identity.Current; if (nativeIdentity != null) savedNativeIdentity = nativeIdentity.Current; uncertain = false; IncompleteOperation = unrecovered;
            written.Add(NativeData);
            LastSave = new SaveReport(written, Outstanding(false), null);
        }
        public string FailSave(Exception error) {
            LastSave = new SaveReport(written, Outstanding(true), error.Message);
            return error.Message.TrimEnd('.') + ". Scene Patcher: completed writes: " + (written.Count == 0 ? "none" : String.Join(", ", written.Select(DisplayItem)))
                + "; outstanding: " + String.Join(", ", LastSave.Outstanding.Select(DisplayItem)) + ". Edits remain in memory. Correct the cause and Save again.";
        }
    }
}
