// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RailroaderScenePatcher.Core;

static class NativeDirtyTests {
    sealed class History : NativeMapEditor, INativeHistoryActions { }
    public static void Run() {
        var history = new History();
        var session = new PatchEditingSession(new PatchEngine(new Scene()), history, new Store());
        history.Session = session;
        var dirty = new NativeDirtyTracker(history, 0, false, 0);
        history.ChangeCount++;
        if (!session.Outstanding(dirty.Observe(history.ChangeCount, false)).Contains(PatchEditingSession.NativeData))
            throw new Exception("An ungrouped native edit remains outstanding");
        history.ChangeCount--;
        if (!dirty.Observe(history.ChangeCount, false)) throw new Exception("Returning to the clean count does not clear an ungrouped edit");
        dirty.Saved(history.ChangeCount);
        if (dirty.Observe(history.ChangeCount, false)) throw new Exception("Native Save clears the ungrouped edit");
        history.NativeEdit(1);
        var attachedDirty = new NativeDirtyTracker(history, history.ChangeCount, true, 0);
        if (!attachedDirty.Observe(history.ChangeCount, false)) throw new Exception("Native work already unsaved at attachment remains outstanding");
        InitiallyDirtyClearsOnUndoToNativeCleanCount();
        InitiallyDirtyDoesNotClearUnexplainedChange();
        DeferredAlignmentFollowsSuccessfulHistory();
        FailedHistoryKeepsNativeWorkOutstanding();
        PatchOnlyHistoryDoesNotDirtyNativeData();
        DiscardedHistoryDuringNativeTransition();
        CoalescedNativeAndPatchChangesRemainDirty();
        GroupedNativeUndoReturnsToSavedState();
        PruningHistoryDoesNotLatchPatchOnlyWork();
    }
    static void DiscardedHistoryDuringNativeTransition() {
        var scene = new Scene();
        scene.Objects["p"] = new System.Collections.Generic.List<object> { new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } } };
        var history = new History(); var store = new Store { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}" };
        var session = new PatchEditingSession(new PatchEngine(scene), history, store); history.Session = session;
        var dirty = new NativeDirtyTracker(history, 0, false, 0);
        history.HistoryStarted = dirty.BeginHistoryExecution;
        session.HistoryNotice = _ => dirty.Observe(history.ChangeCount, false);
        history.HistoryFinished = dirty.EndHistoryExecution;

        if (!session.Edit(new TargetIdentity("s", "p", "c"), null, false).Applied) throw new Exception("Fixture edit failed");
        dirty.Observe(history.ChangeCount, false);
        store.Patch = store.Patch.Replace("\"modifications\"", "\"retained\":true,\"modifications\"");
        session.CheckFileVersion();
        if (!session.UseFileVersion().Applied) throw new Exception("Fixture version choice failed");
        history.Undo();
        if (session.IsDirty(dirty.Observe(history.ChangeCount, false))) throw new Exception("Discarded Undo does not dirty the title when the history notice reads outstanding work during replay");
        history.Redo();
        if (session.IsDirty(dirty.Observe(history.ChangeCount, false))) throw new Exception("Discarded Redo does not dirty the title when the history notice reads outstanding work during replay");
    }
    static void DeferredAlignmentFollowsSuccessfulHistory() {
        var history = new History();
        var session = new PatchEditingSession(new PatchEngine(new Scene()), history, new Store());
        history.Session = session;
        session.BeginHistoryExecution();
        history.NativeEdit(1);
        session.AlignNativeCleanHistory();
        session.EndHistoryExecution(null);
        if (session.Outstanding(false).Contains(PatchEditingSession.NativeData)) throw new Exception("Deferred alignment uses the settled native history");
        history.NativeEdit(2);
        if (!session.Outstanding(false).Contains(PatchEditingSession.NativeData)) throw new Exception("Native history divergence after alignment is outstanding");
    }
    static void FailedHistoryKeepsNativeWorkOutstanding() {
        var history = new History();
        var session = new PatchEditingSession(new PatchEngine(new Scene()), history, new Store());
        history.Session = session;
        session.BeginHistoryExecution();
        history.NativeEdit(1);
        session.AlignNativeCleanHistory();
        session.EndHistoryExecution(new Exception("history failure"));
        if (!session.Outstanding(false).Contains(PatchEditingSession.NativeData) || session.IncompleteOperation == null)
            throw new Exception("Failed history remains unrecovered despite pending alignment");
    }
    static void InitiallyDirtyClearsOnUndoToNativeCleanCount() {
        var history = new History();
        history.NativeEdit(1);
        var session = new PatchEditingSession(new PatchEngine(new Scene()), history, new Store());
        history.Session = session;
        var dirty = new NativeDirtyTracker(history, history.ChangeCount, true, 0);
        if (!session.Outstanding(Observe(session, dirty, history)).Contains(PatchEditingSession.NativeData)) throw new Exception("Pre-existing native edit is outstanding");
        history.Undo();
        if (session.Outstanding(Observe(session, dirty, history)).Contains(PatchEditingSession.NativeData)) throw new Exception("Undo to the native clean count clears pre-existing native work");
        history.Redo();
        if (!session.Outstanding(Observe(session, dirty, history)).Contains(PatchEditingSession.NativeData)) throw new Exception("Redo of pre-existing native work remains outstanding");
    }
    static bool Observe(PatchEditingSession session, NativeDirtyTracker dirty, History history) {
        var pending = dirty.InitialDirtyPending;
        var result = dirty.Observe(history.ChangeCount, false);
        if (pending && !dirty.InitialDirtyPending) session.AlignNativeCleanHistory();
        return result;
    }
    static void InitiallyDirtyDoesNotClearUnexplainedChange() {
        var history = new History();
        var session = new PatchEditingSession(new PatchEngine(new Scene()), history, new Store());
        history.Session = session;
        history.NativeEdit(1);
        var dirty = new NativeDirtyTracker(history, history.ChangeCount, true, 0);
        history.ChangeCount++;
        if (!dirty.Observe(history.ChangeCount, false)) throw new Exception("Unexplained native work is outstanding");
        history.ChangeCount--;
        dirty.Observe(history.ChangeCount, false);
        history.Undo();
        if (!dirty.Observe(history.ChangeCount, false)) throw new Exception("Returning to the clean count does not clear unexplained native work");
        dirty.Saved(history.ChangeCount);
        if (dirty.Observe(history.ChangeCount, false)) throw new Exception("Native Save clears unexplained work after initial dirty state");
    }
    static void PatchOnlyHistoryDoesNotDirtyNativeData() {
        var scene = new Scene();
        scene.Objects["p"] = new System.Collections.Generic.List<object> { new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } } };
        var history = new History();
        var session = new PatchEditingSession(new PatchEngine(scene), history, new Store());
        history.Session = session;
        var dirty = new NativeDirtyTracker(history, 0, false, 0);
        var target = new TargetIdentity("s", "p", "c");
        if (!session.Edit(target, new Placement(new[] { 120.0, 0.0, 0.0 }, new[] { 0.0, 0.0, 0.0, 1.0 }), null).Applied) throw new Exception("Patch edit failed");
        if (dirty.Observe(history.ChangeCount, false)) throw new Exception("A patch-only edit does not dirty native data");
        history.Undo();
        if (dirty.Observe(history.ChangeCount, false)) throw new Exception("Undo of a patch-only edit does not dirty native data");
        history.Redo();
        if (dirty.Observe(history.ChangeCount, false)) throw new Exception("Redo of a patch-only edit does not dirty native data");
        history.ChangeCount++;
        if (!session.Outstanding(dirty.Observe(history.ChangeCount, false)).Contains(PatchEditingSession.NativeData))
            throw new Exception("An ungrouped native edit after patch history remains outstanding");
        history.Undo();
        if (!dirty.Observe(history.ChangeCount, false)) throw new Exception("Undo of a patch edit does not clear the native latch");
        dirty.Saved(history.ChangeCount);
        if (dirty.Observe(history.ChangeCount, false)) throw new Exception("Native Save clears the latch after mixed work");
    }
    static void CoalescedNativeAndPatchChangesRemainDirty() {
        var scene = new Scene();
        scene.Objects["p"] = new System.Collections.Generic.List<object> { new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } } };
        var history = new History();
        var session = new PatchEditingSession(new PatchEngine(scene), history, new Store());
        history.Session = session;
        var dirty = new NativeDirtyTracker(history, 0, false, 0);
        var target = new TargetIdentity("s", "p", "c");
        history.ChangeCount++;
        if (!session.Edit(target, new Placement(new[] { 120.0, 0.0, 0.0 }, new[] { 0.0, 0.0, 0.0, 1.0 }), null).Applied) throw new Exception("Patch edit failed");
        if (!dirty.Observe(history.ChangeCount, false)) throw new Exception("Coalesced native and patch edits remain outstanding");
        dirty.Saved(history.ChangeCount);
        history.ChangeCount++;
        history.Undo();
        if (!dirty.Observe(history.ChangeCount, false)) throw new Exception("Native edit remains outstanding when patch Undo cancels its count change");
    }
    static void GroupedNativeUndoReturnsToSavedState() {
        var history = new History();
        var session = new PatchEditingSession(new PatchEngine(new Scene()), history, new Store());
        history.Session = session;
        var dirty = new NativeDirtyTracker(history, 0, false, 0);
        history.NativeEdit(1);
        if (!session.Outstanding(dirty.Observe(history.ChangeCount, false)).Contains(PatchEditingSession.NativeData)) throw new Exception("Grouped native edit is outstanding");
        history.Undo();
        if (session.Outstanding(dirty.Observe(history.ChangeCount, false)).Contains(PatchEditingSession.NativeData)) throw new Exception("Undo of grouped native edit returns to the saved state");
    }
    static void PruningHistoryDoesNotLatchPatchOnlyWork() {
        var scene = new Scene();
        scene.Objects["p"] = new System.Collections.Generic.List<object> { new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } } };
        var history = new History { MaxLevels = 2 };
        var session = new PatchEditingSession(new PatchEngine(scene), history, new Store());
        history.Session = session;
        history.NativeEdit(1); history.NativeEdit(2);
        session.CompleteNativeSave();
        var dirty = new NativeDirtyTracker(history, history.ChangeCount, false, history.ChangeCount);
        if (!session.Edit(new TargetIdentity("s", "p", "c"), new Placement(new[] { 120.0, 0.0, 0.0 }, new[] { 0.0, 0.0, 0.0, 1.0 }), null).Applied) throw new Exception("Patch edit failed");
        if (dirty.Observe(history.ChangeCount, false)) throw new Exception("Pruning old history for a patch-only edit does not dirty native data");
    }
}
