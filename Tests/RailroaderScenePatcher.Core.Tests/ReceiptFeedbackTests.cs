// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

static class ReceiptFeedbackTests {
    const string Empty = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}";
    internal static void Run() {
        GizmoResultKeepsCopyId();
        DiscardedHistoryUsesFeedback();
        FileStateReplacesOldFeedback();
    }
    static void GizmoResultKeepsCopyId() {
        const string copied = "Copied the ID for Wood Platform 2 x 2 (4).";
        var feedback = new EditFeedback();
        feedback.GeneralPlaces.Add("Copy ID");
        feedback.Inspecting(new object());
        feedback.View("placement/Follow");
        feedback.Report("Copy ID", copied);
        feedback.View("placement/Gizmo");
        if (feedback.Result != copied) throw new Exception("Opening Gizmo keeps the Copy ID result");

        const string completed = "Gizmo placement completed.";
        feedback.Report(CommandBand.GizmoPlace, completed);
        if (feedback.Messages.Count != 2 || feedback.Messages[0].Text != completed || feedback.Messages[1].Text != copied)
            throw new Exception("Releasing the gizmo shows the placement result first and keeps Copy ID readable below it");

        const string cancelled = "Gizmo drag cancelled; the prior placement is restored.";
        feedback.Report(CommandBand.GizmoPlace, cancelled);
        if (feedback.Messages.Count != 2 || feedback.Messages[0].Text != cancelled || feedback.Messages[1].Text != copied)
            throw new Exception("Cancelling a later drag replaces the earlier gizmo result and keeps Copy ID");

        feedback.Inspecting(new object());
        if (feedback.Messages.Count != 0) throw new Exception("Inspecting another object clears both results");
    }
    static void FileStateReplacesOldFeedback() {
        var store = new Store { Patch = Empty };
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), store);
        var feedback = new EditFeedback();
        feedback.Report("Scene patch file", "File version applied.");
        store.Patch = Empty.Replace("\"modifications\"", "\"other\":true,\"modifications\"");
        feedback.CheckFileVersion(session, "Scene patch file");
        if (session.VersionState != FileVersionState.Changed || feedback.Messages.Count != 0) throw new Exception("A new choice replaces the previous file result");
        feedback.Report("Scene patch file", "Old choice result.");
        store.Patch = "{broken";
        session.CheckFileVersion();
        feedback.ObserveFileVersion(session, "Scene patch file");
        if (session.VersionState != FileVersionState.Repair || feedback.Messages.Count != 0) throw new Exception("A separately checked pending choice detects invalid JSON and replaces the old result");
        feedback.Report("Scene patch file", "Repair refused.");
        feedback.Follow("Undo/Redo", "Operation warning.");
        store.Patch = Empty;
        feedback.CheckFileVersion(session, "Scene patch file");
        if (session.VersionState != FileVersionState.None || feedback.Messages.Count != 1 || feedback.Result != "Operation warning.")
            throw new Exception("Restoring the original file clears the repair result and retains unrelated feedback");
    }
    internal static void DiscardedHistoryUsesFeedback() {
        var scene = new Scene();
        scene.Objects["p"] = new List<object> { new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } } };
        var history = new NativeMapEditor(); var store = new Store { Patch = Empty };
        var session = new PatchEditingSession(new PatchEngine(scene), history, store); history.Session = session;
        if (!session.Edit(new TargetIdentity("s", "p", "c"), null, false).Applied) throw new Exception("Fixture edit failed");
        store.Patch = Empty.Replace("\"modifications\"", "\"retained\":true,\"modifications\"");
        session.CheckFileVersion();
        if (!session.UseFileVersion().Applied) throw new Exception("Fixture version choice failed");
        session.BeginHistoryExecution(); session.EndHistoryExecution(new Exception("native history failure"));
        var warning = session.IncompleteOperation;
        var feedback = new EditFeedback();
        session.HistoryNotice = feedback.QueueHistoryNotice;
        history.Undo();
        feedback.HistoryMoved();
        feedback.FlushHistoryNotice();
        if (session.IncompleteOperation != warning || !feedback.Result.Contains("discarded scene patch step")
            || session.ExitWarning(false).Contains("discarded scene patch step"))
            throw new Exception("A discarded step reports footer feedback without replacing or contaminating the native failure warning");
        history.Redo();
        feedback.HistoryMoved();
        feedback.FlushHistoryNotice();
        if (!feedback.Result.Contains("discarded scene patch step")) throw new Exception("Redo reports the discarded step after history refresh");
        if (session.IncompleteOperation != warning || !history.CanUndo) throw new Exception("Redo retains the warning and inverse no-op");
    }
}

static class ReceiptPriorityTests {
    sealed class BackupStore : Store, IVersionBackupStorage {
        string backup;
        public string ReserveVersionBackup() => "Data/scene-patches.json.backup-test";
        public string RelativeVersionBackup(string path) => path;
        public string VersionFingerprint() => Patch;
        public string BackupScenePatchFile() => BackupScenePatchFile(ReserveVersionBackup());
        public string BackupScenePatchFile(string path) { backup = Patch; return path; }
        public void RemoveVersionBackupIfUnchanged(string path) { backup = null; }
        public bool MatchesPatchBackup(string path) => backup == Patch;
        public bool VersionBackupExists(string path) => backup != null;
    }
    internal static void Run() {
        if (EditFeedback.Primary(true, true, false) != EditFeedback.PrimaryMessage.Working)
            throw new Exception("Working feedback remains visible after a repair backup");
        if (EditFeedback.Primary(true, false, true) != EditFeedback.PrimaryMessage.WorldChoice)
            throw new Exception("A pending world choice remains visible after a repair backup");
        if (EditFeedback.Primary(true, false, false) != EditFeedback.PrimaryMessage.BackupDiagnosis)
            throw new Exception("Backup diagnosis remains visible while idle");
        var store = new BackupStore { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}" };
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), store);
        var feedback = new EditFeedback();
        feedback.ChoiceConfirmed(session);
        store.Patch = "{\n broken";
        session.CheckFileVersion();
        if (!session.KeepEditorVersion().Applied || EditFeedback.Primary(session, false, false) != EditFeedback.PrimaryMessage.BackupDiagnosis)
            throw new Exception("A repair backup reports its diagnosis until Save");
        if (feedback.Primary(session, false, false, true) != EditFeedback.PrimaryMessage.BackupDiagnosis)
            throw new Exception("A newer repair backup replaces the confirmation in the first footer slot");
        feedback.Report("Placement", "Previous result.");
        feedback.ChoiceConfirmed(session);
        if (feedback.Primary(session, false, false, true) != EditFeedback.PrimaryMessage.ConfirmedChoice)
            throw new Exception("A confirmed choice appears above an older result and repair backup");
        if (feedback.Primary(session, true, false, true) != EditFeedback.PrimaryMessage.Working)
            throw new Exception("A request in progress remains visible above a confirmation");
        feedback.Report("Placement", "New result.");
        if (feedback.Primary(session, false, false, true) != EditFeedback.PrimaryMessage.Reported)
            throw new Exception("A newer result replaces the confirmation in the first footer slot");
        if (feedback.Primary(session, false, false, false) != EditFeedback.PrimaryMessage.BackupDiagnosis)
            throw new Exception("A released choice leaves the backup available in the footer");
        var diagnosis = session.ScenePatchFileDiagnostics[0];
        var notice = EditFeedback.BackupNotice(session);
        if (notice != "The original file is backed up at Data/scene-patches.json.backup-test. Scene Patcher will not change the backup. Delete it when you no longer need it. It will be published with the mod while it remains there."
            || notice.Contains(diagnosis.Message))
            throw new Exception("The footer names the backup and its publication warning. Details retains the diagnosis: " + notice);
        session.BeginSave();
        if (EditFeedback.Primary(session, false, false) != EditFeedback.PrimaryMessage.None)
            throw new Exception("Successful Save releases the first footer slot for current feedback");
        if (session.LastBackup == null || session.ScenePatchFileDiagnostics.Count == 0 || session.ScenePatchFileDiagnostics[0] != diagnosis)
            throw new Exception("Details retains the backup and diagnosis after Save releases the footer slot");
    }
}

static class ReloadRequestTests {
    internal static void Run() {
        var owner = new ReloadReturn("reload-request-test/" + Guid.NewGuid());
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        try {
            var id = owner.Capture("map", 1, new[] { 1.0, 2.0, 3.0 }, now);
            string reason = null;
            if (owner.CanReload(now, text => reason = text) || reason == null || !reason.Contains("completes or expires"))
                throw new Exception("A blocked reload explains the pending working-location return");
            if (!owner.CanReload(now.AddMinutes(6), _ => throw new Exception("Expired return still blocks reload")))
                throw new Exception("Reload becomes available after the return expires");
            id = owner.Capture("map", 1, new[] { 1.0, 2.0, 3.0 }, now);
            owner.MarkRequested(id, "map", 2, now);
            if (!owner.Complete(id, "map", 2, new[] { 1.0, 2.0, 3.0 }, now) || !owner.CanReload(now, _ => { }))
                throw new Exception("Reload becomes available after return completion");
        } finally { owner.Cancel(); }
    }
}

static class ExitPromptTests {
    internal static void Run() {
        var store = new Store { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}" };
        var session = new PatchEditingSession(new PatchEngine(new Scene()), new NativeMapEditor(), store);
        store.Patch = store.Patch.Replace("\"modifications\"", "\"other\":true,\"modifications\"");
        session.CheckFileVersion();
        if (session.ExitWarning(false) != null) throw new Exception("Fixture has no unsaved work except the pending choice");
        var message = session.ExitPrompt(false);
        if (message != "Exit the Map Editor? A file version choice is still pending. Exit discards unsaved work.")
            throw new Exception("The exit prompt describes a pending version choice without repeating the diagnosis: " + message);
        store.Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{},\"extra\":\"unterminated";
        session.CheckFileVersion();
        message = session.ExitPrompt(false);
        if (message != "Exit the Map Editor? The scene patch file still needs repair. Exit discards unsaved work.")
            throw new Exception("The exit prompt describes repair without repeating the parser diagnostic: " + message);
    }
}
