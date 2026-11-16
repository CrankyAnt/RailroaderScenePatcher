// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Correcting patch data without losing content: runtime reading of unknown content, the format version checked first,
// diagnostics, confirmed re-reading, backup-protected fresh start and the dependsOn refusals of scene patch editing.
static class RecoveryTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static readonly TargetIdentity P = new TargetIdentity("s", "p", "c"), Q = new TargetIdentity("s", "q", "c");
    static string Mod(string id, string path, string body, string deps = "[]") => "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"}," + body + ",\"dependsOn\":" + deps + "}";
    static string Fixed(double x) => "\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",2,3],\"rotation\":[0,0,0,1]}";
    static string Doc(string items, string extra = "", string version = "1") => "{\"formatVersion\":" + version + ",\"sourceMap\":{\"mapId\":\"map\"}," + extra + "\"modifications\":{" + items + "}}";
    static string Info(params string[] required) => "{\"name\":\"Mod\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[" + String.Join(",", required.Select(r => "\"" + r + "\"")) + "]}}";
    const string Patcher = "crankyant/scene-patcher >=0.5.0";
    static Placement At(double x) => new Placement(new[] { x, 2, 3 }, new[] { 0.0, 0, 0, 1 });
    sealed class Messages : IPatchDiagnostics { public readonly List<PatchFailure> Rows = new List<PatchFailure>(); public void Warning(PatchFailure failure) { Rows.Add(failure); } }

    sealed class World {
        public Scene Scene = new Scene(); public Box Box = new Box { Position = new[] { -93.0, 8, 9 }, Rotation = new[] { 0.0, 0, 0, 1 } };
        public Box Other = new Box { Position = new[] { -50.0, 0, 0 }, Rotation = new[] { 0.0, 0, 0, 1 } };
        public NativeMapEditor MapEditor = new NativeMapEditor(); public PatchEditingSession Session; public IStorage Storage;
        public World(IStorage storage) {
            Scene.Objects["p"] = new List<object> { Box }; Scene.Objects["q"] = new List<object> { Other };
            Storage = storage;
            var engine = new PatchEngine(Scene);
            string text = null;
            try { text = storage.ReadPatchDocument(); } catch (ArgumentException) { }
            if (text != null) engine.Apply(text, "author/mod");
            Session = new PatchEditingSession(engine, MapEditor, storage, documentKey: "author/mod");
            MapEditor.Session = Session;
        }
        public World(string patch) : this(new Store { Patch = patch, Info = Info("rr/game >=0.6.2", Patcher) }) { }
        public Store Store => (Store)Storage;
    }
    // A mod folder on disk with the edited mod's two files.
    sealed class Folder : IDisposable {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "scene-patcher-recovery-" + Guid.NewGuid().ToString("N"));
        public Folder(byte[] patch, string info = null) {
            Directory.CreateDirectory(Path.Combine(Root, "Data"));
            if (patch != null) File.WriteAllBytes(PatchPath, patch);
            File.WriteAllText(Path.Combine(Root, "Info.json"), info ?? Info("rr/game >=0.6.2", Patcher));
        }
        public string PatchPath => Path.Combine(Root, "Data", "scene-patches.json");
        public byte[] Bytes => File.ReadAllBytes(PatchPath);
        public string[] Backups => Directory.GetFiles(Path.Combine(Root, "Data"), "scene-patches.json.backup-*");
        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }
    static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    public static void Run() {
        RuntimeAppliesKnownOperationsBesideUnknownContent();
        FormatVersionIsCheckedBeforeAnyOtherContent();
        DiagnosticsLocateTheProblem();
        RereadAdoptsAnOutsideCorrection();
        FreshStartSecuresAVerifiedBackup();
        FreshStartRefusesWhatItCannotSecure();
        DependedModificationsCannotLeaveTheDocument();
        EditsOfIneffectiveModificationsAreRefused();
        Console.WriteLine("PASS: unknown runtime content, format version first, diagnostics, confirmed re-reading, backup-protected fresh start and dependsOn refusals");
    }

    // A current-format document with unknown fields at the root, inside a modification and inside known content: the known
    // operations apply, nothing executes for the unknown content and the file keeps its bytes through reading,
    // application, opening scene patch editing and a native Save.
    static void RuntimeAppliesKnownOperationsBesideUnknownContent() {
        var text = "{\n  \"formatVersion\": 1,\n  \"futureRoot\": {\"mode\": \"later\", \"ratio\": 1.50},\n  \"sourceMap\": {\"mapId\": \"map\", \"futureMap\": true},\n  \"modifications\": {\n    "
            + Mod("known", "p", Fixed(112)) + ",\n    " + Mod("future", "q", Fixed(170) + ",\"futureOperation\":{\"speed\":3}") + ",\n    "
            + Mod("nested", "q", Fixed(180).Replace("\"mode\":\"fixed\"", "\"mode\":\"fixed\",\"futureSnap\":true")) + "\n  }\n}\n";
        using (var folder = new Folder(Utf8(text))) {
            var before = folder.Bytes;
            var scene = new Scene(); var box = new Box(); var other = new Box { Position = new[] { -50.0, 0, 0 } };
            scene.Objects["p"] = new List<object> { box }; scene.Objects["q"] = new List<object> { other };
            var messages = new Messages();
            var storage = new ModStorage(folder.Root);
            var mod = LoadedMod.Read("author/mod", storage.ReadModInfo(), storage.ReadPatchDocument());
            var result = new PatchRuntime(new PatchEngine(scene), messages, "0.6.0", "game").ApplyLoaded(new[] { mod }, null).Single().Result;
            Assert(result.Error == null && result.Outcomes["known"].Applied && box.X == 12, "The known operation applies beside unknown content: " + result.Error);
            Assert(!result.Outcomes["future"].Applied && !result.Outcomes["nested"].Applied && other.X == -50, "Nothing executes for an unknown operation or an unknown field inside known content");
            Assert(folder.Bytes.SequenceEqual(before), "Runtime application leaves the document's bytes unchanged");
            var w = new World(storage);
            Assert(w.Session.Blocked == null && w.Session.DocumentDiagnostics.Count == 2 && w.Session.DocumentDiagnostics.All(d => d.Message.Contains("is not executed")),
                "Scene patch editing opens the document and names each modification it does not execute: " + String.Join(" | ", w.Session.DocumentDiagnostics.Select(d => d.Message)));
            w.MapEditor.NativeEdit(1);
            Assert(w.MapEditor.Save() == null && folder.Bytes.SequenceEqual(before), "A native Save without a patch edit leaves the document's bytes unchanged");
            Assert(w.Session.Edit(P, At(130), null).Applied && w.MapEditor.Save() == null, "A known modification beside unknown content is edited and saved");
            var saved = JObject.Parse(storage.ReadPatchDocument());
            Assert(JToken.DeepEquals(saved["futureRoot"], JObject.Parse(text)["futureRoot"]) && (bool)saved["sourceMap"]["futureMap"] && (int)saved["modifications"]["future"]["futureOperation"]["speed"] == 3
                && (bool)saved["modifications"]["nested"]["placement"]["futureSnap"] && storage.ReadPatchDocument().Contains("1.50"), "Save keeps every unknown field with its exact content");
        }
    }

    // A missing, unrecognized or newer declared version applies none of the document's operations, also when the rest
    // is valid for the current format or would not read as it; other applicable documents still apply, and scene patch
    // editing protects the document with the version as the reason.
    static void FormatVersionIsCheckedBeforeAnyOtherContent() {
        var valid = Mod("move", "p", Fixed(112));
        var cases = new[] {
            ("missing", "{\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + valid + "}}", "Missing format version"),
            ("a string", Doc(valid, version: "\"1\""), "Unrecognized format version"),
            ("a decimal", Doc(valid, version: "1.0"), "Unrecognized format version"),
            ("zero", Doc(valid, version: "0"), "Unrecognized format version"),
            ("negative", Doc(valid, version: "-1"), "Unrecognized format version"),
            ("an object", Doc(valid, version: "{\"major\":1}"), "Unrecognized format version"),
            ("newer", Doc(valid, version: "2"), "Newer format version 2"),
            ("much newer", Doc(valid, version: "10"), "Newer format version 10"),
            ("huge", Doc(valid, version: "999999999999999999999999999"), "Newer format version 999999999999999999999999999"),
            ("newer, declared last", "{\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + valid + "},\"formatVersion\":2}", "Newer format version 2"),
            ("newer with content of another shape", "{\"formatVersion\":2,\"sourceMap\":\"map\",\"modifications\":[{\"id\":\"a\",\"x\":1,\"x\":2}]}", "Newer format version 2"),
            ("newer, then unreadable", "{\"formatVersion\":3,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + valid + "}", "Newer format version 3"),
            ("newer beside a current declaration", "{\"formatVersion\":1,\"formatVersion\":2,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + valid + "}}", "Newer format version 2"),
            ("newer behind a comment", "/* later */ {\"formatVersion\":2, // why\n\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + valid + "}}", "Newer format version 2"),
            ("a leading zero", Doc(valid, version: "01"), "Unrecognized format version"),
            ("a hexadecimal number", Doc(valid, version: "0x1"), "Unrecognized format version"),
            ("a leading zero before a newer number", Doc(valid, version: "02"), "Unrecognized format version"),
            ("a leading zero on a later line", "{\r\n  \"sourceMap\": {\"mapId\": \"map\"},\r\n  \"formatVersion\": 01,\r\n  \"modifications\": {" + valid + "}\r\n}\r\n", "Unrecognized format version"),
        };
        foreach (var (name, text, reason) in cases) {
            var document = PatchDocument.Load(text);
            Assert(!document.CanWrite && document.Error.StartsWith(reason, StringComparison.Ordinal), name + ": the document is refused for its format version: " + document.Error);
            Assert(document.Diagnostics.Count == 1 && document.Diagnostics[0].Path == "formatVersion", name + ": one diagnostic names formatVersion");
            var scene = new Scene(); var box = new Box(); var other = new Box();
            scene.Objects["p"] = new List<object> { box }; scene.Objects["other"] = new List<object> { other };
            var messages = new Messages();
            var results = new PatchRuntime(new PatchEngine(scene), messages, "0.6.0", "game").ApplyLoaded(new[] {
                LoadedMod.Read("author/versioned", Info(Patcher), text),
                LoadedMod.Read("author/current", Info(Patcher), Doc(Mod("move", "other", Fixed(150)))) }, null);
            var versioned = results[0].Result;
            Assert(versioned.Error != null && versioned.Error.StartsWith(reason, StringComparison.Ordinal) && versioned.Outcomes.Count == 0 && box.X == 0 && scene.MutationCalls == 1,
                name + ": runtime applies none of the document's operations: " + versioned.Error);
            Assert(results[1].Result.Outcomes["move"].Applied && other.X == 50, name + ": another applicable document still applies");
            var line = results[0].LogLine("0.6.0", 1, new[] { "elsewhere" });
            Assert(line.Contains("document not applied: " + reason) && !line.Contains("skipped"), name + ": the log reports the format version, whatever map the document names: " + line);
            var w = new World(text);
            Assert(w.Session.Blocked != null && w.Session.Blocked.StartsWith(reason, StringComparison.Ordinal) && w.Session.Blocked.Contains("protected from overwrite")
                && !w.Session.Edit(P, At(112), null).Applied && w.Session.DocumentDiagnostics.Single().Path == "formatVersion", name + ": scene patch editing protects the document for its version: " + w.Session.Blocked);
            w.MapEditor.NativeEdit(1);
            Assert(w.MapEditor.Save() == null && w.Store.Patch == text && w.Store.PatchWrites == 0, name + ": Save does not overwrite the document");
        }
        Assert(PatchDocument.Load(Doc(valid)).CanWrite, "Format version 1 reads");
        Assert(PatchDocument.Load("{\"formatVersion\":1,\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}").Error.StartsWith("Unreadable", StringComparison.Ordinal), "A repeated current declaration is unreadable, not a version problem");
        Assert(PatchDocument.Load("[1]").Error.StartsWith("Invalid document envelope", StringComparison.Ordinal) && PatchDocument.Load("{broken").Error.StartsWith("Unreadable", StringComparison.Ordinal), "Text that is no object, or no JSON, is not a version problem");
    }

    static void DiagnosticsLocateTheProblem() {
        var text = "{\n  \"formatVersion\": 1,\n  \"sourceMap\": {\n    \"mapId\": \"\"\n  },\n  \"modifications\": {}\n}\n";
        var d = PatchDocument.Load(text).Diagnostics.Single();
        Assert(d.Path == "sourceMap.mapId" && d.StartLine == 4 && d.EndLine == 4 && d.Fragment == "    \"mapId\": \"\"" && d.Expected.Contains("mapId") && d.Message.Contains("sourceMap needs a mapId"),
            "A wrong value is located by path, line and fragment with the expected value: " + d.Path + " " + d.StartLine + " " + d.Fragment);
        Assert(d.Reference == DocumentDiagnostic.FormatReference && d.Reference.EndsWith("patch-document-v1.md", StringComparison.Ordinal) && d.Corrections.EndsWith("recover-patch-documents.md", StringComparison.Ordinal),
            "A diagnostic links the format reference and the corrections guide");
        var missing = PatchDocument.Load("{\"formatVersion\":1,\"modifications\":{}}").Diagnostics.Single();
        Assert(missing.Path == "sourceMap.mapId" && missing.StartLine == 0 && missing.Fragment.Contains("no line"), "A missing field has no line: " + missing.StartLine);
        var broken = PatchDocument.Load("{\n  \"formatVersion\": 1,\n  \"sourceMap\": {\"mapId\": \"map\"},\n  \"modifications\": {\n").Diagnostics.Single();
        Assert(!broken.Message.Contains("..") && !broken.Message.Contains(": "), "A parser message is one sentence without a second label or full stop: " + broken.Message);
        Assert(broken.Message.StartsWith("Unreadable", StringComparison.Ordinal) && broken.StartLine >= 4, "Unreadable JSON is located at the line where reading stopped: " + broken.StartLine);
        var block = "{\n  \"formatVersion\": 1,\n  \"sourceMap\": {\"mapId\": \"map\"},\n  \"modifications\": {\n    \"later\": {\n" + String.Join(",\n", Enumerable.Range(0, 20).Select(i => "      \"f" + i + "\": " + i)) + "\n    }\n  }\n}\n";
        var later = PatchDocument.Load(block);
        var unknown = later.Diagnostics.Single();
        Assert(later.CanWrite && unknown.Path == "modifications.later" && unknown.StartLine == 5 && unknown.EndLine == 25 && unknown.Truncated && unknown.Fragment.Split('\n').Length == DocumentDiagnostic.FragmentLines + 1 && unknown.Fragment.EndsWith("... 13 more lines, to line 25", StringComparison.Ordinal),
            "A modification that is not executed is located with a fragment of at most " + DocumentDiagnostic.FragmentLines + " lines: " + unknown.StartLine + "-" + unknown.EndLine);
        Assert(!d.Truncated, "A fragment that shows the whole problem is not marked as cut");
        var nan = "{\n  \"formatVersion\": 1,\n  \"sourceMap\": {\"mapId\": \"map\"},\n  \"modifications\": {\n    \"a\": {\"target\": {\"scene\": \"s\", \"path\": \"p\", \"component\": \"c\"}, \"dependsOn\": [],\n      \"placement\": {\"mode\": \"fixed\", \"position\": [1, 2, 3], \"rotation\": [0, 0, 0, 1]}},\n    \"b\": {\"target\": {\"scene\": \"s\", \"path\": \"q\", \"component\": \"c\"}, \"dependsOn\": [],\n      \"placement\": {\"mode\": \"fixed\", \"position\": [NaN, 2, 3], \"rotation\": [0, 0, 0, 1]}}\n  }\n}\n";
        var inexact = PatchDocument.Load(nan).Diagnostics.First(x => x.Message.StartsWith("Numeric content", StringComparison.Ordinal));
        Assert(inexact.Path == "modifications.b.placement.position[0]" && inexact.StartLine == 8 && inexact.Fragment.Contains("NaN"), "A number that cannot be kept exactly is located: " + inexact.Path + " line " + inexact.StartLine);
        var wide = Doc("").Replace("\"mapId\":\"map\"", "\"mapId\":\"\",\"pad\":\"" + new string('x', 5000) + "\"");
        Assert(PatchDocument.Load(wide).Diagnostics.Single().Fragment.Length == DocumentDiagnostic.FragmentCharacters + 3, "A fragment of one long line is bounded");
        var w = new World(Doc(Mod("move", "p", Fixed(112))).Replace("\"mapId\":\"map\"", "\"mapId\":\"elsewhere\""));
        Assert(w.Session.Blocked != null && w.Session.DocumentDiagnostics.Single().Path == "sourceMap.mapId" && w.Session.DocumentDiagnostics.Single().Expected.Contains("map, layer"),
            "A foreign target map is diagnosed with the maps this session knows");
    }

    // A protected document corrected outside scene patch editing is read again after confirmation and becomes the
    // session's document; the read writes nothing and changes nothing in the scene.
    static void RereadAdoptsAnOutsideCorrection() {
        var broken = Doc(Mod("move", "p", Fixed(112)), version: "2");
        using (var folder = new Folder(Utf8(broken))) {
            var w = new World(new ModStorage(folder.Root));
            Assert(w.Session.Blocked != null, "The newer document is protected");
            var unconfirmed = w.Session.Reread(false);
            Assert(!unconfirmed.Applied && unconfirmed.Error.Contains("Confirm") && w.Session.Blocked != null, "Reading again needs confirmation");
            var still = w.Session.Reread(true);
            Assert(!still.Applied && still.Error.Contains("Newer format version") && w.Session.Blocked != null && w.Session.DocumentDiagnostics.Single().Path == "formatVersion", "An uncorrected document stays protected with its diagnostics: " + still.Error);
            var corrected = Doc(Mod("move", "p", Fixed(112)) + "," + Mod("later", "q", "\"futureOperation\":true"), "\"futureRoot\":7,");
            File.WriteAllBytes(folder.PatchPath, Utf8(corrected));
            var reread = w.Session.Reread(true);
            Assert(reread.Applied && w.Session.Blocked == null && folder.Bytes.SequenceEqual(Utf8(corrected)) && folder.Backups.Length == 0,
                "The corrected document is adopted without a write: " + reread.Error);
            Assert(w.Box.X == 12 && w.Session.LastReread.Outcomes["move"].Applied && !w.Session.LastReread.Outcomes["later"].Applied && w.Other.X == -50,
                "The adopted document's modifications are applied at once, and the one it does not execute is not");
            Assert(w.Session.DocumentDiagnostics.Single().Path == "modifications.later" && !w.MapEditor.IsDirty, "The adopted document is Saved and still names the modification it does not execute");
            Assert(w.Session.Edit(P, At(130), null).Applied && w.MapEditor.Save() == null, "The adopted document is edited and saved: " + w.Session.LastSave?.Error);
            var saved = JObject.Parse(File.ReadAllText(folder.PatchPath));
            Assert((int)saved["futureRoot"] == 7 && (bool)saved["modifications"]["later"]["futureOperation"] && (double)saved["modifications"]["move"]["placement"]["position"][0] == 130, "Unknown content survives the recovered session's Save");
            Assert(!w.Session.Reread(true).Applied, "An editable session is not replaced by reading again");
        }
        // The edit after Read again starts from the object as the panel reads it, which is the adopted document's state:
        // changing Z alone keeps the document's X.
        using (var folder = new Folder(Utf8(Doc(Mod("move", "p", Fixed(112)), version: "2")))) {
            var w = new World(new ModStorage(folder.Root));
            File.WriteAllBytes(folder.PatchPath, Utf8(Doc(Mod("move", "p", Fixed(117.5)))));
            Assert(w.Session.Reread(true).Applied, "The corrected document is read again");
            var shown = w.Scene.Read(w.Box).Placement;
            Assert(shown.Position[0] == 117.5, "The panel reads the document's X after Read again: " + shown.Position[0]);
            Assert(w.Session.Edit(P, new Placement(new[] { shown.Position[0], shown.Position[1], 13.0 }, shown.Rotation), null).Applied && w.MapEditor.Save() == null, "Z alone is edited and saved");
            var saved = (JArray)JObject.Parse(File.ReadAllText(folder.PatchPath))["modifications"]["move"]["placement"]["position"];
            Assert((double)saved[0] == 117.5 && (double)saved[2] == 13, "Only the edited field changes in the document: " + saved.ToString(Newtonsoft.Json.Formatting.None));
        }
        var dirty = new World(Doc("", version: "2"));
        dirty.MapEditor.NativeEdit(4);
        dirty.Store.Patch = Doc("");
        Assert(!dirty.Session.Reread(true, dirty.MapEditor.NativeDirty).Applied && !dirty.Session.Reread(true).Applied && dirty.Session.Blocked != null, "Reading again refuses unsaved native work and a changed history");
        var gone = new World(Doc("", version: "2")); gone.Store.Patch = null;
        Assert(gone.Session.Reread(true).Error.Contains("no longer exists"), "A deleted document is not read again");
    }

    // Fresh start secures the original's exact bytes in a verified backup before Save replaces the original with an
    // empty current-format document; the backup stays.
    static void FreshStartSecuresAVerifiedBackup() {
        var original = new byte[] { 0x7b, 0x22, 0x66, 0xff, 0xfe, 0x22, 0x0a };
        using (var folder = new Folder(original)) {
            var w = new World(new ModStorage(folder.Root));
            Assert(w.Session.Blocked != null && w.Session.Blocked.StartsWith("The edited mod's patch document cannot be read", StringComparison.Ordinal) && w.Session.CanStartFresh, "A document that is not UTF-8 is protected: " + w.Session.Blocked);
            var unconfirmed = w.Session.FreshStart(false);
            Assert(!unconfirmed.Applied && folder.Backups.Length == 0 && w.Session.Blocked != null, "Fresh start needs confirmation and secures nothing without it");
            var fresh = w.Session.FreshStart(true);
            Assert(fresh.Applied && w.Session.Blocked == null && w.Session.FreshStartPending && folder.Backups.Length == 1 && w.Session.LastBackup == folder.Backups[0], "Fresh start secures one backup: " + fresh.Error);
            Assert(File.ReadAllBytes(w.Session.LastBackup).SequenceEqual(original) && folder.Bytes.SequenceEqual(original), "The backup holds the original bytes and the original is not yet replaced");
            Assert(w.MapEditor.IsDirty && w.Session.Outstanding(false).Contains(PatchEditingSession.PatchData), "The staged fresh document is outstanding until Save");
            Assert(w.MapEditor.Save() == null && !w.Session.FreshStartPending && !w.MapEditor.IsDirty, "Save replaces the original: " + w.Session.LastSave?.Error);
            var written = JObject.Parse(File.ReadAllText(folder.PatchPath));
            Assert((int)written["formatVersion"] == 1 && (string)written["sourceMap"]["mapId"] == "layer" && !((JObject)written["modifications"]).HasValues, "Save writes an empty current-format document naming the bottom map layer: " + written);
            Assert(File.ReadAllBytes(w.Session.LastBackup).SequenceEqual(original), "The backup stays after Save");
            Assert(w.Session.Edit(P, At(112), null).Applied && w.MapEditor.Save() == null && folder.Backups.Length == 1, "Editing continues on the fresh document without another backup");
        }
        // The original or its backup changed after fresh start: Save refuses to replace either.
        using (var folder = new Folder(Utf8("{broken"))) {
            var w = new World(new ModStorage(folder.Root));
            Assert(w.Session.FreshStart(true).Applied, "Fresh start of an unreadable document");
            File.WriteAllBytes(folder.PatchPath, Utf8("{changed"));
            var refusal = w.MapEditor.Save();
            Assert(refusal != null && refusal.Contains("changed after fresh start") && File.ReadAllText(folder.PatchPath) == "{changed" && w.Session.FreshStartPending, "A changed original is not replaced: " + refusal);
            File.WriteAllBytes(folder.PatchPath, Utf8("{broken")); File.WriteAllBytes(w.Session.LastBackup, Utf8("{tampered"));
            Assert(w.MapEditor.Save() != null && File.ReadAllText(folder.PatchPath) == "{broken", "A changed backup blocks the replacement");
        }
    }

    static void FreshStartRefusesWhatItCannotSecure() {
        var w = new World("{broken");
        var refused = w.Session.FreshStart(true);
        Assert(!refused.Applied && refused.Error.Contains("cannot secure a backup") && w.Session.Blocked != null, "A storage without backups leaves the document protected");
        using (var folder = new Folder(Utf8("{broken"))) {
            var files = new FaultyFiles { FailWrite = "scene-patches.json.backup-" };
            var f = new World(new ModStorage(folder.Root, files));
            var failed = f.Session.FreshStart(true);
            Assert(!failed.Applied && f.Session.Blocked != null && folder.Backups.Length == 0 && File.ReadAllText(folder.PatchPath) == "{broken", "A failed backup is removed and the original stays protected: " + failed.Error);
            f.MapEditor.NativeEdit(1);
            Assert(!f.Session.FreshStart(true, f.MapEditor.NativeDirty).Applied && folder.Backups.Length == 0, "Fresh start refuses unsaved native work");
        }
        var editable = new World(Doc(""));
        Assert(!editable.Session.FreshStart(true).Applied && !editable.Session.CanStartFresh, "Fresh start does not replace a readable document");
        // Unreadable mod metadata blocks the session, but the patch document is not what blocks it: Start fresh is not offered.
        using (var folder = new Folder(Utf8("{broken"))) {
            File.WriteAllBytes(Path.Combine(folder.Root, "Info.json"), new byte[] { 0x7b, 0xff, 0x7d });
            var m = new World(new ModStorage(folder.Root));
            var metadataRefusal = m.Session.FreshStart(true);
            Assert(m.Session.Blocked != null && m.Session.Blocked.StartsWith("The edited mod's Info.json cannot be read", StringComparison.Ordinal) && !m.Session.Blocked.Contains("start fresh")
                && !m.Session.CanStartFresh && !metadataRefusal.Applied && folder.Backups.Length == 0, "Unreadable Info.json does not offer or allow Start fresh: " + m.Session.Blocked + " / " + metadataRefusal.Error);
            File.WriteAllText(Path.Combine(folder.Root, "Info.json"), Info(Patcher));
            Assert(!m.Session.Reread(true).Applied && m.Session.CanStartFresh, "Once Info.json reads, the document itself blocks the session and Start fresh is offered");
        }
    }

    // Remove, Clear activity and other removals of a modification that another modification names in dependsOn are
    // refused with the dependents named; nothing changes.
    static void DependedModificationsCannotLeaveTheDocument() {
        var text = Doc(Mod("base", "p", "\"active\":false") + "," + Mod("follow", "q", Fixed(130), "[\"base\"]") + "," + Mod("also", "q", "\"active\":true", "[\"base\",\"follow\"]").Replace("\"path\":\"q\"", "\"path\":\"q2\""));
        var w = new World(text);
        w.Scene.Objects["q2"] = new List<object> { new Box { Rotation = new[] { 0.0, 0, 0, 1 } } };
        Assert(w.Session.Blocked == null && !w.Box.Active, "The document opens with base applied");
        foreach (var (name, attempt) in new (string, Func<Outcome>)[] { ("Remove", () => w.Session.Remove(P)), ("Clear activity", () => w.Session.ClearActivity(P)), ("an edit to the underlying value", () => w.Session.Edit(P, null, true)) }) {
            var groups = w.MapEditor.UndoGroups.Count; var mutations = w.Scene.MutationCalls;
            var outcome = attempt();
            Assert(!outcome.Applied && outcome.Error.Contains("Modification base cannot leave the document because also, follow name it in dependsOn") && outcome.Error.Contains("Nothing was changed"),
                name + " of a depended modification is refused with its dependents: " + outcome.Error);
            Assert(w.MapEditor.UndoGroups.Count == groups && w.Scene.MutationCalls == mutations && !w.Box.Active && JObject.Parse(w.Session.DocumentText)["modifications"]["base"] != null, name + " changes nothing");
        }
        var single = w.Session.Remove(Q);
        Assert(!single.Applied && single.Error.Contains("because also names it in dependsOn; change or remove that modification first"), "One dependent is named in the singular: " + single.Error);
        foreach (var target in new[] { new TargetIdentity("s", "q2", "c"), Q, P }) {
            var removed = w.Session.Remove(target);
            Assert(removed.Applied, "Once no modification names it, " + target.Path + " can leave the document: " + removed.Error);
        }
        Assert(w.Box.Active && w.Session.DocumentText.Contains("\"modifications\": {}"), "The removals return the objects to their underlying state");
    }

    // An edit of a modification that does not take effect because a dependency is missing, failed or cyclic is refused
    // with that reason instead of being reported as applied; removing it stays possible.
    static void EditsOfIneffectiveModificationsAreRefused() {
        var cases = new[] {
            ("missing", Mod("move", "p", Fixed(112), "[\"gone\"]"), "dependency gone is missing from the document"),
            ("failed", Mod("move", "p", Fixed(112), "[\"broken\"]") + "," + Mod("broken", "missing", Fixed(1)), "dependency broken does not take effect (Target must resolve uniquely)"),
            ("failed deeper", Mod("move", "p", Fixed(112), "[\"middle\"]") + "," + Mod("middle", "q", "\"active\":true", "[\"gone\"]"), "dependency gone is missing from the document"),
            ("cyclic", Mod("move", "p", Fixed(112), "[\"loop\"]") + "," + Mod("loop", "q", "\"active\":false", "[\"move\"]"), "its dependencies form the cycle move -> loop -> move"),
            ("self", Mod("move", "p", Fixed(112), "[\"move\"]"), "its dependencies form the cycle move -> move"),
        };
        foreach (var (name, items, reason) in cases) {
            var w = new World(Doc(items));
            var x = w.Box.X; var groups = w.MapEditor.UndoGroups.Count;
            var edit = w.Session.Edit(P, At(150), null);
            Assert(!edit.Applied && edit.Error == "Modification move is not effective because " + reason + "; correct that first. Nothing was changed", name + ": the edit is refused with the reason: " + edit.Error);
            Assert(w.Box.X == x && w.MapEditor.UndoGroups.Count == groups && (double)JObject.Parse(w.Session.DocumentText)["modifications"]["move"]["placement"]["position"][0] == 112, name + ": nothing changed");
            Assert(!w.Session.Edit(P, null, false).Applied, name + ": an activity edit is refused as well");
            var remove = w.Session.Remove(P);
            if (name != "cyclic") Assert(remove.Applied, name + ": the ineffective modification can be removed");
            else Assert(!remove.Applied && remove.Error.Contains("because loop names it in dependsOn") && JObject.Parse(w.Session.DocumentText)["modifications"]["move"] != null && w.MapEditor.UndoGroups.Count == groups,
                "In a cycle of two each member has a dependent, so neither can be removed; the cycle is broken in the file: " + remove.Error);
        }
        var runtime = new PatchEngine(new World(Doc("")).Scene).Apply(Doc(Mod("move", "p", Fixed(112), "[\"gone\"]")));
        Assert(runtime.Outcomes["move"].Error == "Dependency gone is missing from the document" && runtime.Outcomes["move"].Target?.Path == "p",
            "Runtime names the missing dependency and the object the modification is for: " + runtime.Outcomes["move"].Error);
        var effective = new World(Doc(Mod("move", "p", Fixed(112), "[\"base\"]") + "," + Mod("base", "q", "\"active\":false")));
        Assert(effective.Session.Edit(P, At(150), null).Applied && effective.Box.X == 50, "An edit of a modification whose dependencies take effect applies");
    }
}
