// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    public enum ImportChangeKind { New, Changed, Removed }
    public enum ImportConflictCase { EditedInBoth, SameObject, NewOnBothSides }
    // One change the file made to an entry since scene patch editing last read or wrote it, which Import applies.
    public sealed class ImportChange {
        internal ImportChange(string key, ImportChangeKind kind, string target, string description, bool outside = false) { Key = key; Kind = kind; Target = target; Description = description; OutsideModifications = outside; }
        // The modification ID, or for content outside the modifications, its member name.
        public string Key { get; }
        public bool OutsideModifications { get; }
        public ImportChangeKind Kind { get; }
        // The name of the object the entry targets.
        public string Target { get; }
        // What the change does, as one or more plain sentences.
        public string Description { get; }
    }
    // Entries that both the file and this session's scene patch editing steps changed. The modder chooses one version for all of them.
    public sealed class ImportConflict {
        internal ImportConflict(int number, ImportConflictCase kind, IReadOnlyList<string> keys, string target, string description, string editorSide, string fileSide) {
            Number = number; Case = kind; Keys = keys; Target = target; Description = description; EditorSide = editorSide; FileSide = fileSide;
        }
        public int Number { get; }
        public ImportConflictCase Case { get; }
        public IReadOnlyList<string> Keys { get; }
        public string Target { get; }
        public string Description { get; }
        // What each version does to the entries.
        public string EditorSide { get; }
        public string FileSide { get; }
        // Null until the modder chose: true for the file version, false for the editor version.
        public bool? UseFile { get; internal set; }
    }
    // The overview Import shows: the changes that apply without conflict, then each conflict with its choice.
    public sealed class ImportOverview {
        internal ImportOverview(IReadOnlyList<ImportChange> changes, IReadOnlyList<ImportConflict> conflicts, string fingerprint, string text) {
            Changes = changes; Conflicts = conflicts; Fingerprint = fingerprint; Text = text;
        }
        public IReadOnlyList<ImportChange> Changes { get; }
        public IReadOnlyList<ImportConflict> Conflicts { get; }
        internal string Fingerprint { get; }
        internal string Text { get; }
        public bool Ready => Conflicts.All(c => c.UseFile.HasValue);
        // Why Import cannot apply yet, or null.
        public string ApplyRefusal => Ready ? null : "Choose the editor version or the file version for each conflict first.";
    }
    // What the last completed Import applied.
    public sealed class ImportResult {
        public ImportResult(int changes, int fileChoices, int editorChoices, IReadOnlyList<string> notApplied = null) { Changes = changes; FileChoices = fileChoices; EditorChoices = editorChoices; NotApplied = notApplied ?? Array.Empty<string>(); }
        // Entries kept in the scene patch file whose target is unavailable in this session, each with its reason: an isolated
        // application failure, as at load.
        public IReadOnlyList<string> NotApplied { get; }
        public int Changes { get; }
        public int FileChoices { get; }
        public int EditorChoices { get; }
    }
    // Import brings the changes made to the scene patch file outside scene patch editing into the running session. The file
    // side is compared with the text scene patch editing last read or wrote, the side in scene patch editing with the session's
    // start state. A change to an entry without scene patch editing steps in this session applies as unsaved work; an entry
    // with such steps is a conflict for the modder to decide. Import is outside native Undo and Redo and writes nothing.
    public sealed partial class PatchEditingSession {
        public const string ImportSaveRefusal = "Complete or cancel Import before saving.";
        // The scene patch file's content when the session started, or after Use file version replaced it; kept apart from the
        // text last read or written.
        JToken startContent;
        // The keys of the entries this session's scene patch editing steps changed. A step a file choice discarded no longer counts.
        readonly HashSet<string> stepKeys = new HashSet<string>(StringComparer.Ordinal);
        // Per key, how often a file version replaced the entry: a step recorded for an older count is a no-op.
        readonly Dictionary<string, int> keyGenerations = new Dictionary<string, int>(StringComparer.Ordinal);
        int KeyGeneration(string key) => keyGenerations.TryGetValue(key, out var value) ? value : 0;
        // The open import overview, or null.
        public ImportOverview Import { get; private set; }
        // Set when a file check closed the open overview because the file changed again; the owner reads it once.
        public bool ImportOutdated { get; set; }
        public ImportResult LastImport { get; private set; }
        // Whether this session's scene patch editing steps changed the entry.
        public bool HasSteps(string key) => stepKeys.Contains(key);
        void ResetStartState() { startContent = scenePatchFile?.Root.DeepClone(); }
        static JObject ModificationsOf(JToken root) => (root as JObject)?["modifications"] as JObject ?? new JObject();
        static JObject Entry(JObject modifications, string key) => modifications[key] as JObject;
        static bool SameEntry(JObject a, JObject b) => a == null ? b == null : b != null && PreservingJson.SameModification(a, b);

        public Outcome OpenImport() {
            var outcome = new Outcome();
            try {
                if (Blocked != null) throw new InvalidOperationException(Blocked);
                if (scenePatchFile == null) throw new InvalidOperationException("The scene patch file is unavailable");
                var shown = observedVersion;
                CheckFileVersion();
                if (shown != observedVersion) throw new InvalidOperationException("The file changed again. Review the updated choice before continuing");
                if (VersionState != FileVersionState.Changed) throw new InvalidOperationException("There is no readable file version to import");
                var text = storage.ReadScenePatchFile();
                if ((storage is IVersionBackupStorage versionStorage ? versionStorage.VersionFingerprint() : text) != shown) throw new InvalidOperationException("The file changed again. Review the updated choice before continuing");
                if (text == null) throw new InvalidOperationException("The file no longer exists");
                // The declared format version decides how the rest is read, so it is checked before any other content.
                var format = ScenePatchFile.FormatProblem(text);
                if (format != null) throw new InvalidOperationException(format);
                var loaded = ScenePatchFile.Load(text);
                var refusal = Refusal(loaded, text);
                if (refusal != null) throw new InvalidOperationException(refusal);
                Import = Classify(loaded, text, shown);
                outcome.Applied = true;
            } catch (Exception e) { outcome.Error = e.Message; }
            return outcome;
        }
        // Discards the overview and its conflict choices. The version choice stays pending.
        public void CancelImport() { Import = null; }
        public void ChooseImportVersion(int conflict, bool useFile) {
            var found = Import?.Conflicts.FirstOrDefault(c => c.Number == conflict);
            if (found == null) throw new InvalidOperationException("There is no such conflict in the open Import");
            found.UseFile = useFile;
        }

        ImportOverview Classify(ScenePatchFile loaded, string text, string fingerprint) {
            var file = loaded.Modifications; var read = ModificationsOf(saved); var start = ModificationsOf(startContent); var editor = scenePatchFile.Modifications;
            var keys = file.Properties().Select(p => p.Name).Concat(read.Properties().Select(p => p.Name)).Distinct(StringComparer.Ordinal).ToList();
            var changes = new List<ImportChange>();
            var groups = new List<List<string>>();
            var cases = new List<ImportConflictCase>();
            foreach (var key in keys) {
                var after = Entry(file, key); var before = Entry(read, key); var mine = Entry(editor, key);
                // Unchanged in the file since the last read or write, or already as the file has it: nothing to bring in.
                if (SameEntry(before, after) || SameEntry(mine, after)) continue;
                ImportConflictCase? kind = null;
                var involved = new List<string> { key };
                if (before == null && Entry(start, key) == null && mine != null) kind = ImportConflictCase.NewOnBothSides;
                else if (HasSteps(key)) kind = ImportConflictCase.EditedInBoth;
                else {
                    // The object the entry targets in the file and the one it targeted before: an entry that moves away from an
                    // object the session edited changes that object too.
                    var targets = new[] { after?["target"] as JObject, before?["target"] as JObject };
                    var others = stepKeys.Where(other => other != key && targets.Any(target => SameObject(target, (Entry(editor, other) ?? Entry(start, other) ?? Entry(read, other))?["target"] as JObject))).OrderBy(k => k, StringComparer.Ordinal).ToList();
                    if (others.Count != 0) { kind = ImportConflictCase.SameObject; involved.AddRange(others); }
                }
                if (kind == null) { changes.Add(Change(key, before, after)); continue; }
                // Conflicts that share an entry are decided together.
                var joined = groups.Select((g, i) => i).Where(i => groups[i].Intersect(involved).Any()).ToList();
                if (joined.Count == 0) { groups.Add(involved); cases.Add(kind.Value); continue; }
                var into = joined[0];
                foreach (var i in joined.Skip(1).Reverse()) { groups[into].AddRange(groups[i]); groups.RemoveAt(i); cases.RemoveAt(i); }
                groups[into] = groups[into].Concat(involved).Distinct(StringComparer.Ordinal).ToList();
            }
            var conflicts = groups.Select((g, i) => Conflict(i + 1, cases[i], g, file, read, start, editor)).ToList();
            // A change to an entry a conflict holds is decided by that conflict.
            changes.RemoveAll(c => conflicts.Any(conflict => conflict.Keys.Contains(c.Key)));
            // Content outside the modifications comes from the file; scene patch editing never edits it.
            // Without a text read or written, the session's own content outside the modifications is the reference.
            var fileRoot = loaded.Root; var readRoot = saved as JObject ?? scenePatchFile.Root;
            foreach (var member in fileRoot.Properties().Select(p => p.Name).Concat(readRoot?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal)) {
                if (member == "modifications") continue;
                var mine = fileRoot[member]; var theirs = readRoot?[member];
                if (mine != null && theirs != null && PreservingJson.SameContent(mine, theirs)) continue;
                changes.Add(new ImportChange(member, theirs == null ? ImportChangeKind.New : mine == null ? ImportChangeKind.Removed : ImportChangeKind.Changed, null,
                    "Content outside the modifications: " + member + ".", true));
            }
            return new ImportOverview(changes, conflicts, fingerprint, text);
        }
        bool SameObject(JObject a, JObject b) {
            if (a == null || b == null) return false;
            if (PreservingJson.SameContent(a, b)) return true;
            try { return ReferenceEquals(engine.ResolveUnique(PatchEngine.ParseTarget(a)), engine.ResolveUnique(PatchEngine.ParseTarget(b))); }
            catch (InvalidOperationException) { return false; }
        }
        static string TargetName(params JObject[] entries) {
            foreach (var entry in entries) {
                var path = (entry?["target"] as JObject)?["path"];
                if (path?.Type != JTokenType.String) continue;
                var text = (string)path;
                return text.Substring(text.LastIndexOf('/') + 1);
            }
            return null;
        }
        static ImportChangeKind KindOf(JObject before, JObject after) => before == null ? ImportChangeKind.New : after == null ? ImportChangeKind.Removed : ImportChangeKind.Changed;
        static ImportChange Change(string key, JObject before, JObject after) {
            var name = TargetName(after, before);
            return new ImportChange(key, KindOf(before, after), name, ListLine(key, name, before, after));
        }
        // The line of a change in the Import list. The list names the kind in front of it, so the line does not repeat it as
        // a verb: the entry first, then what the change does.
        static string ListLine(string key, string name, JObject before, JObject after) {
            var entry = "Modification ID " + key + (name == null ? "" : " for " + name) + ".";
            if (after == null) return entry + " The object returns to its state without this modification.";
            if (before == null) {
                // A new entry that sets nothing beyond its target says only what it adds.
                var sets = Settings(after, null);
                return sets.Count == 0 ? entry : entry + " It sets " + List(sets) + ".";
            }
            var changed = Settings(after, before);
            if (changed.Count == 0) return entry + " Its content changes.";
            return entry + (changed.Count == 1 ? " Setting: " : " Settings: ") + List(changed) + ".";
        }
        // What replacing one value of an entry by another does, as plain sentences.
        static string Effect(string key, string name, JObject before, JObject after) {
            var subject = "modification " + key + (name == null ? "" : " for " + name);
            if (before == null && after == null) return "Leaves " + subject + " absent.";
            if (after == null) return "Removes " + subject + ". The object returns to its state without this modification.";
            if (before == null) return "Adds " + subject + ". It sets " + List(Settings(after, null)) + ".";
            var changed = Settings(after, before);
            return "Changes " + (changed.Count == 0 ? "the content" : List(changed)) + " in " + subject + ".";
        }
        // The settings an entry holds, or, against an earlier value, the settings whose values differ.
        static List<string> Settings(JObject entry, JObject against) {
            var names = new List<string>();
            void Add(string name) { if (!names.Contains(name)) names.Add(name); }
            bool Differs(JToken a, JToken b) => against == null ? a != null : !(a == null ? b == null : b != null && PreservingJson.SameContent(a, b));
            foreach (var member in entry.Properties().Select(p => p.Name).Concat(against?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal)) {
                if (member == "relationships") {
                    var mine = entry["relationships"] as JObject; var theirs = against?["relationships"] as JObject;
                    foreach (var relation in (mine?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>()).Concat(theirs?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal))
                        if (Differs(mine?[relation], theirs?[relation])) Add(SettingName(relation));
                    continue;
                }
                // A new entry sets what it changes: its target names the object, and an empty dependsOn sets nothing.
                if (against == null && (member == "target" || (member == "dependsOn" && entry[member] is JArray none && none.Count == 0))) continue;
                if (Differs(entry[member], against?[member])) Add(SettingName(member));
            }
            return names;
        }
        static string SettingName(string member) {
            switch (member) {
                case "target": return "the target object";
                case "placement": return "the placement";
                case "active": return "the activity";
                case "dependsOn": return "the dependencies";
                case "starterPlacement": return "the starter placement";
                case "trackMarker": return "the track binding";
                case "crossingCoverage": return "the crossing coverage";
                case "crossingEquipment": return "the added lights, gates and bells";
                default: return "content this patcher version does not apply (" + member + ")";
            }
        }
        static string List(IReadOnlyList<string> items) {
            if (items.Count == 0) return "nothing";
            if (items.Count == 1) return items[0];
            return String.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];
        }
        ImportConflict Conflict(int number, ImportConflictCase kind, List<string> keys, JObject file, JObject read, JObject start, JObject editor) {
            var name = keys.Select(k => TargetName(Entry(file, k), Entry(editor, k), Entry(read, k), Entry(start, k))).FirstOrDefault(n => n != null);
            var subject = (keys.Count == 1 ? "Modification ID " + keys[0] : "Modification IDs " + List(keys)) + (name == null ? "" : " for " + name);
            string description;
            switch (kind) {
                case ImportConflictCase.SameObject: description = subject + (keys.Count == 1 ? " refers" : " refer") + " to the same object. Your edits and the file change it under different modification IDs."; break;
                case ImportConflictCase.NewOnBothSides: description = subject + (keys.Count == 1 ? " is new" : " are new") + " in both your edits and the file, with different content."; break;
                default: description = subject + (keys.Count == 1 ? " has" : " have") + " changes in both your edits and the file."; break;
            }
            var editorSide = String.Join(" ", keys.Where(k => !SameEntry(Entry(start, k), Entry(editor, k))).Select(k => Effect(k, name, Entry(start, k), Entry(editor, k))));
            var fileSide = String.Join(" ", keys.Where(k => !SameEntry(Entry(editor, k), Entry(file, k))).Select(k => Effect(k, name, Entry(editor, k), Entry(file, k))));
            return new ImportConflict(number, kind, keys, name,
                description,
                ("Editor version: keeps your edits. " + editorSide).TrimEnd(),
                ("File version: discards your edits for " + (keys.Count == 1 ? "this entry" : "these entries") + ". Their steps remain in Undo and Redo but no longer change anything. " + fileSide).TrimEnd());
        }

        // Applies every change of the overview and every conflict choice at once. When one fails, scene patch editing returns to
        // its state before Import and the overview stays open.
        public Outcome ApplyImport() {
            var outcome = new Outcome();
            var overview = Import;
            if (overview == null) { outcome.Error = "There is no open Import"; return outcome; }
            if (!overview.Ready) { outcome.Error = overview.ApplyRefusal; return outcome; }
            var original = scenePatchFile;
            var snapshots = new List<Tuple<object, object>>();
            var mutationStarted = false;
            try {
                if (Blocked != null) throw new InvalidOperationException(Blocked);
                var text = storage.ReadScenePatchFile();
                if ((storage is IVersionBackupStorage versionStorage ? versionStorage.VersionFingerprint() : text) != overview.Fingerprint) {
                    Import = null;
                    throw new InvalidOperationException("The file changed again. Review the updated choice before continuing");
                }
                var merged = ScenePatchFile.Load(text);
                var refusal = Refusal(merged, text);
                if (refusal != null) { Import = null; throw new InvalidOperationException(refusal); }
                var editor = original.Modifications;
                // The entries that take the file's value: every change, and the entries of each conflict decided for the file.
                var fileKeys = new HashSet<string>(overview.Changes.Where(c => !c.OutsideModifications).Select(c => c.Key), StringComparer.Ordinal);
                foreach (var conflict in overview.Conflicts.Where(c => c.UseFile == true)) fileKeys.UnionWith(conflict.Keys);
                // Every other entry keeps its value in scene patch editing.
                foreach (var key in editor.Properties().Select(p => p.Name).Concat(merged.Modifications.Properties().Select(p => p.Name)).Distinct(StringComparer.Ordinal).ToList()) {
                    if (fileKeys.Contains(key)) continue;
                    var mine = Entry(editor, key);
                    if (!SameEntry(mine, Entry(merged.Modifications, key))) merged.Replace(key, (JObject)mine?.DeepClone());
                }
                var changed = editor.Properties().Select(p => p.Name).Concat(merged.Modifications.Properties().Select(p => p.Name)).Distinct(StringComparer.Ordinal)
                    .Where(key => !SameEntry(Entry(editor, key), Entry(merged.Modifications, key))).ToList();
                var context = scene.MapIds.ToArray();
                var targets = new List<Tuple<TargetIdentity, object>>();
                var notApplied = new List<string>();
                // An entry that names a changed entry in dependsOn may change its effect with it, so its object is applied again.
                var affected = new HashSet<string>(changed, StringComparer.Ordinal);
                for (var grown = true; grown;) {
                    grown = false;
                    foreach (var source in new[] { editor, merged.Modifications })
                        foreach (var property in source.Properties())
                            if (!affected.Contains(property.Name) && (property.Value as JObject)?["dependsOn"] is JArray dependencies
                                && dependencies.Any(d => d.Type == JTokenType.String && affected.Contains((string)d))) { affected.Add(property.Name); grown = true; }
                }
                foreach (var key in affected) {
                    foreach (var pair in new[] { Tuple.Create(Entry(editor, key), false), Tuple.Create(Entry(merged.Modifications, key), true) }) {
                        var item = pair.Item1;
                        if (item == null) continue;
                        var executable = true;
                        try { PatchEngine.Parse(item, scenePatchFileKey); } catch (InvalidOperationException) { executable = false; }
                        TargetIdentity identity; object resolved;
                        try { identity = PatchEngine.ParseTarget(item["target"] as JObject); resolved = engine.ResolveUnique(identity); }
                        catch (InvalidOperationException e) {
                            // An entry the patcher does not execute is kept without execution, and an entry whose target is
                            // unavailable is an isolated application failure, as at load.
                            if (pair.Item2 && executable && changed.Contains(key)) notApplied.Add(key + ": " + ScenePatchFile.Sentence(e.Message));
                            continue;
                        }
                        if (targets.Any(t => ReferenceEquals(t.Item2, resolved))) continue;
                        if (engine.IsUnsafe(resolved)) throw new InvalidOperationException("Cleanup for the object of modification " + key + " could not be verified. Select Recheck or reload the Map Editor before importing");
                        scene.Preflight(resolved);
                        targets.Add(Tuple.Create(identity, resolved));
                    }
                }
                foreach (var target in targets) { engine.Underlying(target.Item2); snapshots.Add(Tuple.Create(target.Item2, scene.Capture(target.Item2))); }
                var modifications = merged.Modifications;
                engine.Plan(modifications, scenePatchFileKey);
                try {
                    foreach (var key in changed) {
                        var item = Entry(modifications, key);
                        if (item == null) continue;
                        ObjectState state;
                        try { state = PatchEngine.Parse(item, scenePatchFileKey).State; } catch (InvalidOperationException) { continue; }
                        object owner;
                        try { owner = engine.ResolveUnique(PatchEngine.ParseTarget(item["target"] as JObject)); } catch (InvalidOperationException) { continue; }
                        try { engine.CheckRelationships(owner, state); }
                        catch (InvalidOperationException e) { throw new InvalidOperationException("Modification " + key + " cannot be applied: " + ScenePatchFile.Sentence(e.Message)); }
                    }
                } finally { engine.Unplan(); }
                scenePatchFile = merged;
                foreach (var target in targets) {
                    var desired = engine.Desired(modifications, target.Item2, scenePatchFileKey);
                    CheckEffectiveCoverage(target.Item2, desired.Coverage);
                    engine.GuardMutation(target.Item1, target.Item2, context, null);
                    mutationStarted = true;
                    scene.Apply(target.Item2, desired);
#if SCENE_PATCHER_FAULTS
                    FaultInjection.TripAfterMutation();
#endif
                    if (!Same(scene.Read(target.Item2), desired)) throw new InvalidOperationException("The applied state of " + target.Item1.Path + " did not verify");
                }
                // The file has now been read: later changes are compared with this text.
                diskText = text; saved = PreservingJson.Parse(text).Root; comparedVersion = -1;
                var fileChoices = overview.Conflicts.Where(c => c.UseFile == true).ToList();
                foreach (var key in fileKeys) keyGenerations[key] = KeyGeneration(key) + 1;
                foreach (var conflict in fileChoices) stepKeys.ExceptWith(conflict.Keys);
                ClearVersionAcceptance(); VersionState = FileVersionState.None; VersionMessage = null; Import = null;
                LastImport = new ImportResult(overview.Changes.Count, fileChoices.Count, overview.Conflicts.Count - fileChoices.Count, notApplied);
                outcome.Applied = true;
            } catch (Exception e) {
                scenePatchFile = original;
                var unverified = false;
                foreach (var snapshot in mutationStarted ? snapshots : Enumerable.Empty<Tuple<object, object>>()) {
                    var restored = false;
                    try { restored = scene.Restore(snapshot.Item1, snapshot.Item2); } catch { }
                    if (!restored) { engine.SetUnsafe(snapshot.Item1, true); unverified = true; }
                }
                outcome.Error = "Scene patch editing is back as it was before Import. " + ScenePatchFile.Sentence(e.Message) + "."
                    + (unverified ? " Cleanup could not be verified; the affected objects stay guarded until Recheck or a Map Editor reload." : "");
            }
            if (outcome.Applied) { try { OutstandingMayHaveChanged?.Invoke(); } catch { } }
            return outcome;
        }
    }
}
