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
    // The files of the active edited MapMod. A read returns null when the file does not exist.
    public interface IStorage {
        string ReadPatchDocument();
        void WritePatchDocument(string text);
        string ReadPackInfo();
        void WritePackInfo(string text);
    }
    // The editor's shared undo history. Groups are listed oldest first.
    public interface INativeHistory {
        IReadOnlyList<object> UndoGroups { get; }
        IReadOnlyList<object> RedoGroups { get; }
        void Register(Action inverse, string name);
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
        static int Number(string version) {
            var match = Version.Match(version ?? "");
            int major, minor, patch;
            if (!match.Success || !Int32.TryParse(match.Groups[1].Value, out major) || !Int32.TryParse(match.Groups[2].Value, out minor) || !Int32.TryParse(match.Groups[3].Value, out patch) || major > 2146 || minor > 99 || patch > 99)
                throw new InvalidOperationException("'" + version + "' is not a pack version");
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
        // Returns the pack metadata with the required minimum, or null when it already requires at least that version.
        internal static string Raise(string info, string modId, string minimum) {
            if (info == null) throw new InvalidOperationException("The MapMod's Info.json is missing");
            PreservingJson json;
            try { json = PreservingJson.Parse(info); }
            catch (Exception e) when (e is JsonException || e is ArgumentException) { throw new InvalidOperationException("The MapMod's Info.json is unreadable: " + e.Message); }
            var root = json.Root as JObject;
            if (root == null || !json.Exact) throw new InvalidOperationException("The MapMod's Info.json cannot be rewritten without changing its content");
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
    // One editor session for the active edited MapMod: its patch document, the completed edits in the
    // shared native history, and the truth about which required writes are still outstanding.
    public sealed class AuthoringSession {
        public const string NativeData = "native map data", PatchData = "patch document", Metadata = "dependency metadata";
        readonly PatchEngine engine;
        readonly IScene scene;
        readonly INativeHistory history;
        readonly IStorage storage;
        readonly string modId;
        readonly HistoryIdentity identity;
        readonly PatchDocument document;
        readonly List<string> written = new List<string>();
        string diskText, savedText, info;
        long savedIdentity;
        bool uncertain;
        // Set when native Undo or Redo threw. The native manager then keeps an open partial group, so later
        // native edits reach neither its stack nor its change count. No Save in this session can prove a clean state.
        string unrecovered;
        public AuthoringSession(PatchEngine engine, INativeHistory history, IStorage storage, string mapId, string modId = "crankyant/scene-patcher") {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.history = history ?? throw new ArgumentNullException(nameof(history));
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            this.modId = modId; scene = engine.Scene;
            identity = new HistoryIdentity(history); savedIdentity = identity.Current;
            try {
                diskText = storage.ReadPatchDocument();
                info = storage.ReadPackInfo();
                if (diskText == null) { document = PatchDocument.Create(mapId); return; }
                var loaded = PatchDocument.Load(diskText);
                if (!loaded.CanWrite) Blocked = loaded.Error;
                else if (loaded.EditError != null) Blocked = loaded.EditError;
                else if (!scene.MapIds.Contains((string)loaded.Root["sourceMap"]["mapId"], StringComparer.Ordinal)) Blocked = "Source map does not match loaded map context";
                else { document = loaded; savedText = loaded.Serialize(); }
            } catch (Exception e) { Blocked = "Cannot read the active MapMod's files: " + e.Message; }
            if (Blocked != null) Blocked += ". The patch document is protected from overwrite; correct it outside the editor and reload.";
        }
        // Why authoring and patch persistence are unavailable in this session, or null.
        public string Blocked { get; private set; }
        // A history or edit operation that did not complete, until a complete Save.
        public string IncompleteOperation { get; private set; }
        public SaveReport LastSave { get; private set; }
        public string DocumentText { get { return document == null ? diskText : document.Serialize(); } }
        // The oldest patcher that executes every authored capability of the document.
        public string MinimumPatcherVersion {
            get {
                if (document == null) return "0.1.0";
                var items = document.Modifications.OfType<JObject>().ToList();
                if (items.Any(m => (m["target"] as JObject)?["structure"]?.Type == JTokenType.String && (string)m["target"]["structure"] == BridgeScene.Structure)) return "0.3.0";
                return items.Any(m => m["active"] != null) ? "0.2.0" : "0.1.0";
            }
        }

        public Outcome Edit(TargetIdentity target, Placement placement, bool? active) { return Edit(target, placement, active, null); }
        // An editor edit passes the token of its inspected observation; the edit is refused when that observation no longer holds.
        public Outcome Edit(TargetIdentity target, Placement placement, bool? active, InspectedTarget inspected) {
            return Change("Edit scene object", target, inspected, existing => {
                if (placement == null && active == null) throw new InvalidOperationException("Nothing to change");
                if (placement != null) PatchEngine.CheckPlacement(placement.Position, placement.Rotation);
                if (existing != null) PatchEngine.Parse(existing);
                var value = new JObject { ["id"] = existing?["id"] ?? NewId(target) };
                value["target"] = existing?["target"] ?? TargetJson(target);
                if (placement != null) value["placement"] = new JObject { ["mode"] = "fixed", ["position"] = new JArray(placement.Position.Cast<object>().ToArray()), ["rotation"] = new JArray(placement.Rotation.Cast<object>().ToArray()) };
                else if (existing?["placement"] != null) value["placement"] = existing["placement"].DeepClone();
                if (active != null) value["active"] = active.Value;
                else if (existing?["active"] != null) value["active"] = existing["active"].DeepClone();
                value["dependsOn"] = existing?["dependsOn"]?.DeepClone() ?? new JArray();
                return value;
            });
        }
        // Removes this MapMod's saved or unsaved modification of the object; the object returns to its effective underlying state.
        public Outcome Remove(TargetIdentity target) { return Remove(target, null); }
        public Outcome Remove(TargetIdentity target, InspectedTarget inspected) {
            return Change("Remove scene modification", target, inspected, existing => {
                if (existing == null) throw new InvalidOperationException("This object has no modification in the active document");
                PatchEngine.Parse(existing);
                return null;
            });
        }
        Outcome Change(string name, TargetIdentity target, InspectedTarget inspected, Func<JObject, JObject> next) {
            var outcome = new Outcome();
            try {
                if (Blocked != null) throw new InvalidOperationException(Blocked);
                var resolved = engine.ResolveUnique(target);
                inspected?.Validate(scene, target, resolved);
                outcome.ResolvedTarget = resolved;
                if (engine.IsUnsafe(resolved)) throw new InvalidOperationException("Target cleanup is unverified. Recheck the object or reload before another edit");
                scene.Preflight(resolved);
                var own = Own(resolved);
                if (own.Count > 1) throw new InvalidOperationException("Several modifications in the document target this object; combine them outside the editor first");
                var existing = own.FirstOrDefault();
                JObject value;
                try { value = next(existing); }
                catch (InvalidOperationException e) when (existing != null && e.Message.StartsWith("Unsupported", StringComparison.Ordinal)) {
                    throw new InvalidOperationException("The existing modification holds content this patcher version does not understand; it is preserved and cannot be edited or removed here");
                }
                var index = Commit(target, resolved, inspected, existing, value, document.Modifications.Count);
                var slot = (string)(value ?? existing)["id"];
                history.Register(() => Replay(name, slot, target, existing, index), name);
                outcome.Applied = true;
            } catch (Exception e) { outcome.Error = e.Message; }
            return outcome;
        }
        // Runs inside native Undo or Redo. It must not throw: an exception there corrupts the native grouping.
        void Replay(string name, string slot, TargetIdentity target, JObject value, int index) {
            var current = document.Modifications.OfType<JObject>().FirstOrDefault(m => (string)m["id"] == slot);
            var position = index;
            try {
                var resolved = engine.ResolveUnique(target);
                if (engine.IsUnsafe(resolved)) throw new InvalidOperationException("Target cleanup is unverified");
                position = Commit(target, resolved, null, current, value, index);
            } catch (Exception e) {
                value = current;
                uncertain = true;
                IncompleteOperation = name + " could not be reversed or reapplied: " + e.Message + ". The document keeps the state shown; Save remains available.";
            }
            history.Register(() => Replay(name, slot, target, current, position), name);
        }
        int Commit(TargetIdentity identity, object target, InspectedTarget inspected, JObject current, JObject value, int index) {
            var mapContext = scene.MapIds.ToArray();
            var prior = scene.Read(target);
            engine.Underlying(target);
            index = document.Replace(current, value, index);
            var mutating = false;
            try {
                var desired = engine.Desired(document.Modifications, target);
                // Reading state and resolving the document may invoke native callbacks. Never redirect to a new match.
                engine.GuardMutation(identity, target, mapContext, inspected);
                mutating = true;
                scene.Apply(target, desired);
                if (!Same(scene.Read(target), desired)) throw new InvalidOperationException("Applied state did not verify");
            } catch (Exception e) {
                document.Replace(value, current, index);
                // A rejection before the mutation started changed nothing in the scene and needs no restoration.
                if (!mutating) throw;
                var restored = false;
                try { scene.Apply(target, prior); restored = Same(scene.Read(target), prior); } catch { }
                if (!restored) engine.SetUnsafe(target, true);
                throw new InvalidOperationException(restored ? "Edit failed and the prior state was restored: " + e.Message : "Edit failed; cleanup is unverified: " + e.Message);
            }
            return index;
        }
        // Explicit retry after unverified cleanup: rechecks the actual state against the document before unblocking.
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
                var desired = engine.Desired(document.Modifications, resolved);
                if (!Same(scene.Read(resolved), desired)) {
                    engine.GuardMutation(target, resolved, mapContext, inspected);
                    scene.Apply(resolved, desired);
                    if (!Same(scene.Read(resolved), desired)) throw new InvalidOperationException("The object still differs from the document; reload before another edit");
                }
                engine.SetUnsafe(resolved, false);
                outcome.Applied = true;
            } catch (Exception e) { outcome.Error = e.Message; }
            return outcome;
        }
        List<JObject> Own(object target) {
            var own = new List<JObject>();
            foreach (var item in document.Modifications.OfType<JObject>()) {
                try { if (ReferenceEquals(engine.ResolveUnique(PatchEngine.ParseTarget(item["target"] as JObject)), target)) own.Add(item); }
                catch (InvalidOperationException) { }
            }
            return own;
        }
        string NewId(TargetIdentity target) {
            var name = new string(target.Path.Substring(target.Path.LastIndexOf('/') + 1).ToLowerInvariant().Select(c => Char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            var stem = "place-" + (name.Length == 0 ? "object" : name);
            var used = new HashSet<string>(document.Modifications.OfType<JObject>().Select(m => (string)m["id"]), StringComparer.Ordinal);
            var id = stem;
            for (var n = 2; used.Contains(id); n++) id = stem + "-" + n;
            return id;
        }
        static JObject TargetJson(TargetIdentity target) {
            var value = new JObject { ["scene"] = target.Scene, ["path"] = target.Path, ["component"] = target.Component };
            if (target.Structure != null) value["structure"] = target.Structure;
            return value;
        }
        static bool Same(ObjectState a, ObjectState b) {
            if (a == null || b == null || a.Active != b.Active || a.Placement == null || b.Placement == null) return false;
            for (var i = 0; i < 3; i++) if (Math.Abs(a.Placement.Position[i] - b.Placement.Position[i]) > 0.01) return false;
            double dot = 0, la = 0, lb = 0;
            for (var i = 0; i < 4; i++) { dot += a.Placement.Rotation[i] * b.Placement.Rotation[i]; la += a.Placement.Rotation[i] * a.Placement.Rotation[i]; lb += b.Placement.Rotation[i] * b.Placement.Rotation[i]; }
            return la > 0 && lb > 0 && Math.Abs(dot) / Math.Sqrt(la * lb) > 0.99999;
        }

        // History notifications from the editor: around each native Undo or Redo, and after any history change.
        public void BeginHistoryExecution() { identity.Begin(); }
        public void EndHistoryExecution(Exception error) {
            identity.End(error != null);
            if (error == null) return;
            unrecovered = "Native Undo or Redo stopped on an error: " + error.Message + ". The native history is unrecovered and may no longer record edits. Save still writes the current state, but the editor stays unsaved until it is reloaded.";
            IncompleteOperation = unrecovered;
        }
        public void HistoryChanged() { identity.Observe(); }

        bool PatchOutstanding { get { return document != null && (savedText == null ? document.Modifications.Count != 0 : document.Serialize() != savedText); } }
        bool MetadataOutstanding {
            get {
                if (document == null || document.Modifications.Count == 0) return false;
                try { return DependencyMetadata.Raise(info, modId, MinimumPatcherVersion) != null; }
                catch (InvalidOperationException) { return true; }
            }
        }
        // The participants whose required write has not completed. nativeDirty is the editor's own change-count answer.
        public IReadOnlyList<string> Outstanding(bool nativeDirty) {
            var result = new List<string>();
            if (nativeDirty || uncertain || unrecovered != null || identity.Current != savedIdentity) result.Add(NativeData);
            if (PatchOutstanding) result.Add(PatchData);
            if (MetadataOutstanding) result.Add(Metadata);
            return result;
        }
        public bool IsDirty(bool nativeDirty) { return Outstanding(nativeDirty).Count != 0; }
        // Text for the exit confirmation, or null when nothing is outstanding.
        public string ExitWarning(bool nativeDirty) {
            var outstanding = Outstanding(nativeDirty);
            if (outstanding.Count == 0) return null;
            return "Unsaved work: " + String.Join(", ", outstanding) + "." + (IncompleteOperation == null ? "" : " " + IncompleteOperation);
        }

        // The Save route. BeginSave runs before the native map write and throws when a patcher write fails;
        // CompleteNativeSave follows the native write; FailSave follows any exception of the attempt.
        public void BeginSave() {
            written.Clear();
            if (document == null) return;
            if (document.Modifications.Count != 0) {
                try {
                    var fresh = storage.ReadPackInfo();
                    var raised = DependencyMetadata.Raise(fresh, modId, MinimumPatcherVersion);
                    if (raised != null) { storage.WritePackInfo(raised); written.Add(Metadata); }
                    info = raised ?? fresh;
                } catch (Exception e) { throw new InvalidOperationException("Dependency metadata was not written: " + e.Message, e); }
            }
            if (!PatchOutstanding) return;
            try {
                if (storage.ReadPatchDocument() != diskText) throw new InvalidOperationException("the file changed outside the editor after it was read. It is protected from overwrite; reload the editor to read it again");
                var text = document.Serialize();
                storage.WritePatchDocument(text);
                diskText = text; savedText = text; written.Add(PatchData);
            } catch (Exception e) { throw new InvalidOperationException("The patch document was not written: " + e.Message, e); }
        }
        public void CompleteNativeSave() {
            savedIdentity = identity.Current; uncertain = false; IncompleteOperation = unrecovered;
            written.Add(NativeData);
            LastSave = new SaveReport(written, Outstanding(false), null);
        }
        public string FailSave(Exception error) {
            LastSave = new SaveReport(written, Outstanding(true), error.Message);
            return error.Message.TrimEnd('.') + ". Scene Patcher: completed writes: " + (written.Count == 0 ? "none" : String.Join(", ", written))
                + "; outstanding: " + String.Join(", ", LastSave.Outstanding) + ". Edits remain in memory. Correct the cause and Save again.";
        }
    }
}
