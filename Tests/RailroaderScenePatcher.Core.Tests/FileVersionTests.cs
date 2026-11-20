// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RailroaderScenePatcher.Core;
using RailroaderScenePatcher;

static class FileVersionTests {
    sealed class MemoryFiles : IModFiles {
        public readonly Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();
        public Action<string> AfterWrite;
        public Action<string> BeforeRead;
        public int BackupReads;
        public bool Exists(string path) => Data.ContainsKey(path);
        public byte[] Read(string path) { BeforeRead?.Invoke(path); return Data[path].ToArray(); }
        public void Write(string path, byte[] bytes) { Data[path] = bytes.ToArray(); AfterWrite?.Invoke(path); }
        public void Replace(string source, string destination) { Data[destination] = Data[source]; Data.Remove(source); }
        public void Move(string source, string destination) => Replace(source, destination);
        public void Delete(string path) { Data.Remove(path); }
        public void CreateDirectory(string path) { }
    }
    sealed class AccountedHistory : NativeMapEditor, INativeHistoryActions { }
    sealed class BackupStore : IStorage, IVersionBackupStorage {
        public string Patch, Backup;
        public Action BeforeBackup;
        public string ReadScenePatchFile() => Patch;
        public void WriteScenePatchFile(string text) { Patch = text; }
        public string ReadModInfo() => "{\"dependencies\":{\"required\":[]}}";
        public void WriteModInfo(string text) { }
        public string ReserveVersionBackup() => "Data/scene-patches.json.backup-test";
        public string RelativeVersionBackup(string path) => path;
        public string VersionFingerprint() => Patch;
        public string BackupScenePatchFile() => BackupScenePatchFile(ReserveVersionBackup());
        public string BackupScenePatchFile(string path) { BeforeBackup?.Invoke(); Backup = Patch; return path; }
        public void RemoveVersionBackupIfUnchanged(string path) { Backup = null; }
        public bool MatchesPatchBackup(string path) => Backup != null && Backup == Patch;
        public bool VersionBackupExists(string path) => Backup != null;
    }
    public static void Run() {
        RestoredFileDropsTheRepairDiagnosis();
        PollDetectsInvalidUtf8AndDeletedBackup();
        DeletedFileCanBeRestored();
        FailedBackupCanBeRetried();
        RepairCanBecomeReadableOrAbsent();
        RemovalKeepsPendingChoice();
        RepairBackupPreservesBytes();
        ChangedBackupIsNotRemoved();
        ChangedBackupSurvivesWriteFailure();
        var store = new Store { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}" };
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), store);
        store.Patch = "{\"modifications\":{},\"sourceMap\":{\"mapId\":\"map\"},\"formatVersion\":1}";
        session.CheckFileVersion();
        if (session.VersionState != FileVersionState.None) throw new Exception("Reordering is not an external change");
        store.Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{},\"extra\":true}";
        session.CheckFileVersion();
        if (session.VersionState != FileVersionState.Changed || session.CanSaveForReload) throw new Exception("Changed content requires a choice before Save");
        if (!session.KeepEditorVersion().Applied) throw new Exception("Keep editor version should succeed");
        session.BeginSave();
        if (store.Patch.Contains("extra")) throw new Exception("Keeping an unedited editor version overwrites changed file content on Save");
        var scene = new Scene();
        var box = new Box { Position = new[] { 0.0, 0.0, 0.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        scene.Objects["p"] = new List<object> { box };
        var history = new AccountedHistory();
        store = new Store { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}" };
        session = new PatchEditingSession(new PatchEngine(scene), history, store); history.Session = session;
        var target = new TargetIdentity("s", "p", "c");
        if (!session.Edit(target, new Placement(new[] { 120.0, 0.0, 0.0 }, new[] { 0.0, 0.0, 0.0, 1.0 }), null).Applied) throw new Exception("Edit failed");
        store.Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{},\"extra\":true}";
        session.CheckFileVersion();
        if (!session.UseFileVersion().Applied || box.X != 0) throw new Exception("Using file restores removed editor modifications");
        if (session.Outstanding(false).Contains(PatchEditingSession.NativeData)) throw new Exception("Discarded patch work leaves native work saved");
        history.Undo();
        if (box.X != 0 || !history.CanRedo) throw new Exception("Discarded step remains a redoable no-op");
        if (session.Outstanding(false).Contains(PatchEditingSession.NativeData)) throw new Exception("Undo of discarded patch work leaves native work saved");
        history.Redo();
        if (box.X != 0 || !history.CanUndo) throw new Exception("Discarded step remains an undoable no-op");
        if (session.Outstanding(false).Contains(PatchEditingSession.NativeData)) throw new Exception("Redo of discarded patch work leaves native work saved");
        history.NativeEdit(1);
        if (!session.Outstanding(false).Contains(PatchEditingSession.NativeData)) throw new Exception("Native edits remain outstanding");
        session.CompleteNativeSave();
        history.NativeEdit(2); history.Undo();
        if (session.Outstanding(false).Contains(PatchEditingSession.NativeData)) throw new Exception("Undo of a native edit returns to the saved native state");
        history.Undo(); history.NativeEdit(3);
        if (!session.Outstanding(false).Contains(PatchEditingSession.NativeData)) throw new Exception("Divergent native work remains outstanding at the same history depth");
        var backup = new BackupStore { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{},\"future\":42}" };
        session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), backup);
        backup.Patch = "{\n\"formatVersion\": 99,\n\"modifications\": {}\n}";
        session.CheckFileVersion();
        if (session.VersionState != FileVersionState.Repair || session.ScenePatchFileDiagnostics[0].StartLine != 2 || session.BackupPreview == null) throw new Exception("Repair shows the diagnosis and backup path");
        var invalid = backup.Patch;
        if (!session.KeepEditorVersion().Applied || backup.Backup != invalid) throw new Exception("Keeping the editor version secures the exact changed file");
        backup.Backup = null;
        session.CheckFileVersion();
        if (session.VersionMessage != null) throw new Exception("Deleting the backup clears its feedback");
        if (session.VersionState != FileVersionState.None || session.ScenePatchFileDiagnostics[0].StartLine != 2) throw new Exception("Deleting the backup does not re-enter repair while the file is unchanged, and the diagnosis stays");
        session.BeginSave();
        if (!backup.Patch.Contains("\"future\": 42") && !backup.Patch.Contains("\"future\":42")) throw new Exception("Save retains the session's unknown content");
        if (session.VersionMessage != null || session.ScenePatchFileDiagnostics[0].StartLine != 2) throw new Exception("Save clears backup feedback but retains the diagnosis");
        var changedBackup = new BackupStore { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}" };
        var changedSession = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), changedBackup);
        changedBackup.Patch = "{\n\"formatVersion\":99}"; changedSession.CheckFileVersion();
        if (!changedSession.KeepEditorVersion().Applied) throw new Exception("The invalid version was not backed up");
        changedBackup.Backup = "changed backup"; changedSession.CheckFileVersion();
        if (changedSession.VersionState != FileVersionState.None) throw new Exception("Changing the backup does not re-enter repair");
        changedBackup.Patch = "{\n\"formatVersion\":98}"; changedSession.CheckFileVersion();
        if (changedSession.VersionState != FileVersionState.Repair || changedSession.CanSaveForReload) throw new Exception("Changing the original after backup requires a new choice");
        backup.Patch = "{\n\"formatVersion\":99}";
        session.CheckFileVersion();
        backup.BeforeBackup = () => backup.Patch = "{\n\n\"formatVersion\":98}";
        if (session.KeepEditorVersion().Applied || session.VersionState != FileVersionState.Repair) throw new Exception("A changed file cannot use the previous diagnosis for its backup");
        scene = new Scene(); box = new Box { Position = new[] { 0.0, 0.0, 0.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        scene.Objects["p"] = new List<object> { box };
        store = new Store { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}" };
        session = new PatchEditingSession(new PatchEngine(scene), new NativeMapEditor(), store);
        store.Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" +
            "\"missing\":{\"target\":{\"scene\":\"s\",\"path\":\"missing\",\"component\":\"c\"},\"active\":false,\"dependsOn\":[]}," +
            "\"valid\":{\"target\":{\"scene\":\"s\",\"path\":\"p\",\"component\":\"c\"},\"active\":false,\"dependsOn\":[]}}}";
        session.CheckFileVersion();
        var use = session.UseFileVersion();
        if (!use.Applied || box.Active || session.LastVersionApplication.Outcomes["missing"].Applied || !session.LastVersionApplication.Outcomes["valid"].Applied)
            throw new Exception("An unavailable target does not prevent a valid file entry from applying: " + use.Error + " / " + session.LastVersionApplication?.Outcomes["valid"].Error);
        CorrectedFailedEntryRetries();
    }
    // A file that needed repair and is restored outside the game to what the editor holds is no longer described by the
    // repair's diagnosis: the session details show what the file holds now, not the unreadable line.
    static void RestoredFileDropsTheRepairDiagnosis() {
        var original = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}";
        var store = new BackupStore { Patch = original };
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), store);
        store.Patch = "{ \"formatVersion\": 1,\n \"modifications\": { broken";
        session.CheckFileVersion();
        if (session.VersionState != FileVersionState.Repair || session.ScenePatchFileDiagnostics.Count == 0 || session.ScenePatchFileDiagnostics[0].StartLine != 2)
            throw new Exception("The unreadable file needs repair and names its line");
        store.Patch = original;
        session.CheckFileVersion();
        if (session.VersionState != FileVersionState.None || session.VersionMessage != null || session.ScenePatchFileDiagnostics.Count != 0)
            throw new Exception("The restored file leaves no stale diagnosis: " + String.Join(" | ", session.ScenePatchFileDiagnostics.Select(d => d.Message)));
    }
    static void PollDetectsInvalidUtf8AndDeletedBackup() {
        var files = new MemoryFiles(); var storage = new ModStorage("test", files);
        files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}");
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), storage);
        var feedback = new EditFeedback();
        files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{},\"extra\":true}");
        feedback.CheckFileVersion(session, "Scene patch file");
        if (!session.KeepEditorVersion().Applied) throw new Exception("Fixture choice failed");
        session.BeginSave();
        feedback.Report("Scene patch file", "There is no file to back up.");
        files.Data[storage.PatchPath] = new byte[] { 0xff };
        feedback.CheckFileVersion(session, "Scene patch file");
        if (session.VersionState != FileVersionState.Repair || feedback.Messages.Count != 0) throw new Exception("Polling after Keep and Save detects invalid UTF-8 and replaces the old result");
        var encoding = session.ScenePatchFileDiagnostics.Single();
        if (session.VersionMessage != "File needs repair: The scene patch file is not valid UTF-8 text." || encoding.StartLine != 0 || encoding.Fragment.Length != 0
            || String.IsNullOrEmpty(encoding.TechnicalDetail) || session.VersionMessage.Contains(encoding.TechnicalDetail))
            throw new Exception("A repair explains the encoding problem and keeps the decoder message in the details: " + session.VersionMessage);
        if (!session.KeepEditorVersion().Applied || session.LastBackup == null) throw new Exception("Fixture backup failed");
        feedback.ObserveFileVersion(session, "Scene patch file");
        feedback.Report("Scene patch file", "Backup retained.");
        files.Delete(session.LastBackup);
        feedback.CheckFileVersion(session, "Scene patch file");
        if (session.LastBackup != null || feedback.Messages.Count != 0) throw new Exception("Polling notices a deleted backup and removes its result");
    }
    static void ChangedBackupSurvivesWriteFailure() {
        var files = new MemoryFiles(); var storage = new ModStorage("test", files);
        files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}");
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), storage);
        files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{broken"); session.CheckFileVersion();
        var first = session.BackupPreview;
        files.AfterWrite = path => { if (path.Contains(".backup-")) { files.Data[path] = Encoding.UTF8.GetBytes("another writer"); throw new InvalidOperationException("write failed"); } };
        var failed = session.KeepEditorVersion();
        if (failed.Applied || !failed.Error.Contains(first.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString())) || !files.Data.Any(p => p.Key.Contains(".backup-") && Encoding.UTF8.GetString(p.Value) == "another writer") || session.BackupPreview == first)
            throw new Exception("Write failure retains another writer's backup and offers a fresh path");
    }
    static void RepairCanBecomeReadableOrAbsent() {
        var store = new BackupStore { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}" };
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), store);
        store.Patch = "{broken"; session.CheckFileVersion();
        store.Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{},\"extra\":true}"; session.CheckFileVersion();
        if (session.VersionState != FileVersionState.Changed || !session.VersionMessage.StartsWith("The file is readable again.")) throw new Exception("Correcting a repair to a different readable version offers the normal choice");
        store.Patch = null; session.CheckFileVersion();
        if (session.VersionState != FileVersionState.Repair || !session.KeepEditorVersion().Applied) throw new Exception("Deletion while a choice is pending requires and accepts a new choice");
    }
    static void RemovalKeepsPendingChoice() {
        var scene = new Scene(); var box = new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        scene.Objects["p"] = new List<object> { box };
        var store = new Store { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}" };
        var history = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(scene), history, store); history.Session = session;
        var target = new TargetIdentity("s", "p", "c");
        if (!session.Edit(target, null, false).Applied) throw new Exception("Fixture edit failed");
        store.Patch = store.Patch.Replace("\"modifications\"", "\"outside\":true,\"modifications\"");
        session.CheckFileVersion(); var outside = store.Patch;
        if (!session.Remove(target).Applied || !box.Active || session.VersionState != FileVersionState.Changed || session.CanSaveForReload || store.Patch != outside)
            throw new Exception("Removing a modification keeps the external choice accurate and the file protected");
        history.Undo(); history.Redo();
        if (!box.Active || session.VersionState != FileVersionState.Changed || session.CanSaveForReload || store.Patch != outside)
            throw new Exception("Undo and Redo of removal preserve the pending version choice");
    }
    static void RepairBackupPreservesBytes() {
        foreach (var bytes in new[] { new byte[] { 0xef, 0xbb, 0xbf, 0x7b, 0x78 }, new byte[] { 0xff, 0xfe, 0x7b } }) {
            var files = new MemoryFiles(); var storage = new ModStorage("test", files);
            files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}");
            var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), storage);
            files.Data[storage.PatchPath] = bytes; session.CheckFileVersion();
            var kept = session.KeepEditorVersion();
            if (!kept.Applied || !files.Read(session.LastBackup).SequenceEqual(bytes)) throw new Exception("Repair backup preserves the exact original bytes: " + kept.Error);
        }
    }
    static void ChangedBackupIsNotRemoved() {
        var files = new MemoryFiles(); var storage = new ModStorage("test", files);
        files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}");
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), storage);
        files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{broken"); session.CheckFileVersion();
        files.AfterWrite = path => { if (path.Contains(".backup-")) { files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{changed"); files.AfterWrite = null; } };
        // Simulate another writer changing the backup between its initial readback and later verification.
        files.BeforeRead = path => { if (path.Contains(".backup-") && files.BackupReads++ == 1) files.Data[path] = Encoding.UTF8.GetBytes("someone else's bytes"); };
        var failed = session.KeepEditorVersion();
        var retained = files.Data.Single(p => p.Key.Contains(".backup-") && Encoding.UTF8.GetString(p.Value) == "someone else's bytes").Key;
        if (failed.Applied || !failed.Error.Contains(retained)) throw new Exception("A changed backup is retained and its path is reported");
    }
    static void FailedBackupCanBeRetried() {
        var files = new MemoryFiles(); var storage = new ModStorage("test", files);
        files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}");
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), storage);
        files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{broken"); session.CheckFileVersion();
        var first = session.BackupPreview;
        files.AfterWrite = path => { if (path.Contains(".backup-")) { files.Data[storage.PatchPath] = Encoding.UTF8.GetBytes("{changed"); files.AfterWrite = null; } };
        var failed = session.KeepEditorVersion();
        if (failed.Applied || failed.Error.Contains(first.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString()))) throw new Exception("A changed original refuses the backup without reporting a removed copy");
        if (files.Data.Keys.Any(p => p.Contains(".backup-"))) throw new Exception("Failed backup copy must be removed");
        if (session.BackupPreview == first) throw new Exception("Retry must disclose a fresh backup path");
        session.CheckFileVersion();
        var retry = session.KeepEditorVersion();
        if (!retry.Applied || !files.Read(session.LastBackup).SequenceEqual(Encoding.UTF8.GetBytes("{changed"))) throw new Exception("Retry secures the current bytes: " + retry.Error);
    }
    static void DeletedFileCanBeRestored() {
        var store = new BackupStore { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}" };
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), store);
        store.Patch = null;
        session.CheckFileVersion();
        if (session.VersionState != FileVersionState.Repair) throw new Exception("Deletion requires a version choice");
        var kept = session.KeepEditorVersion();
        if (!kept.Applied || store.Backup != null || !session.CanSaveForReload || !session.VersionMessage.Contains("no file to back up") || !session.VersionMessage.Contains("next Save")) throw new Exception("Keeping a deleted file needs no backup and explains the next Save: " + kept.Error);
        session.BeginSave();
        if (store.Patch == null) throw new Exception("Save restores the deleted scene patch file");
    }
    static void CorrectedFailedEntryRetries() {
        string Doc(string path) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"same-id\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"active\":false,\"dependsOn\":[]}}}";
        var scene = new Scene();
        var box = new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        scene.Objects["p"] = new List<object> { box };
        var engine = new PatchEngine(scene);
        var store = new Store { Patch = Doc("missing") };
        if (engine.Apply(store.Patch, "author/mod").Outcomes["same-id"].Applied) throw new Exception("Missing original target must fail");
        if (engine.Apply(Doc("p"), "author/mod").Outcomes["same-id"].Applied) throw new Exception("Runtime application must not automatically retry a failed ID");
        var session = new PatchEditingSession(engine, new NativeMapEditor(), store, scenePatchFileKey: "author/mod");
        store.Patch = Doc("p"); session.CheckFileVersion();
        var chosen = session.UseFileVersion();
        if (!chosen.Applied || !session.LastVersionApplication.Outcomes["same-id"].Applied || box.Active)
            throw new Exception("The explicitly selected correction retries a previously failed entry: " + session.LastVersionApplication?.Outcomes["same-id"].Error);
        scene = new Scene { FailNextApplies = 1, FailRestore = true };
        var unsafeBox = new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        var other = new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        scene.Objects["p"] = new List<object> { unsafeBox }; scene.Objects["q"] = new List<object> { other };
        engine = new PatchEngine(scene);
        if (engine.Apply(Doc("p"), "author/mod").Outcomes["same-id"].Applied) throw new Exception("Injected failed cleanup must fail");
        scene.FailRestore = false;
        store = new Store { Patch = Doc("missing") };
        session = new PatchEditingSession(engine, new NativeMapEditor(), store, scenePatchFileKey: "author/mod");
        store.Patch = Doc("q"); session.CheckFileVersion(); chosen = session.UseFileVersion();
        if (!chosen.Applied || session.LastVersionApplication.Outcomes["same-id"].Applied || !other.Active)
            throw new Exception("An explicit retry remains blocked while the earlier target's cleanup is unverified");
    }
}
