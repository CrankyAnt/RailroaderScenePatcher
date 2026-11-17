// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

static class GuidedTaskTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static SceneObservation Target(string path) => new SceneObservation(new object(), new TargetIdentity("scene", path, "Marker"), "Track marker", true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "placement");
    internal static void Run() {
        CrossingControllerCannotMoveOrChangeActivity();
        StartingPinsTheTargetAndCopiesValues();
        SelectionCannotReplacePinnedDraft();
        FailureAndSuccessKeepTheirRecoveryPaths();
        DraftCannotBeChangedThroughAnArrayAlias();
        ReviewUpdatesARefusal();
        BackClearsRecovery();
        SharedHandleDoesNotIdentifyAComponent();
        InvalidStepActionsAreIgnored();
        SelectionAndBackOutsidePinnedSteps();
        ClearingPinnedTargetIsRefused();
        StartHandlesMissingValuesWithoutCorruptingState();
        GuidedResultSurvivesInspectorRestoration();
        RefreshClearsMessagesButKeepsContext();
        GuidedStepChangesClearEarlierRefusals();
    }
    static void CrossingControllerCannotMoveOrChangeActivity() {
        foreach (var crossing in new[] {
            new TargetIdentity("scene", "crossing", "other", CrossingCoverageScene.Structure),
            new TargetIdentity("scene", "crossing", CrossingCoverageScene.ComponentType, null)
        }) {
            foreach (var task in new[] { GuidedTaskKind.Placement, GuidedTaskKind.Snap, GuidedTaskKind.Activity })
                Check(GuidedTaskState.TargetBlock(task, crossing) == "You cannot move, rotate, enable or disable a crossing controller.", "A crossing controller cannot offer " + task);
            foreach (var task in new[] { GuidedTaskKind.Binding, GuidedTaskKind.DetachBinding, GuidedTaskKind.StarterPlacement })
                Check(GuidedTaskState.TargetBlock(task, crossing) == null, "Other task rules decide whether a crossing controller can offer " + task);
        }
        var marker = new TargetIdentity("scene", "marker", "Track.TrackMarker", TrackMarkerScene.Structure);
        foreach (GuidedTaskKind task in Enum.GetValues(typeof(GuidedTaskKind)))
            Check(GuidedTaskState.TargetBlock(task, marker) == null, "The crossing rule does not block a track marker's " + task);
        var differentStructure = new TargetIdentity("scene", "other", CrossingCoverageScene.ComponentType, TrackMarkerScene.Structure);
        Check(GuidedTaskState.TargetBlock(GuidedTaskKind.Placement, differentStructure) == null, "An explicit different structure takes precedence over the component fallback");
    }
    static void StartingPinsTheTargetAndCopiesValues() {
        var state = new GuidedTaskState(); var target = Target("first"); var values = new[] { "1", "2" };
        state.Start(GuidedTaskKind.Placement, target, values); values[0] = "changed";
        Check(state.Enabled && state.Step == GuidedTaskStep.Values && state.Kind == GuidedTaskKind.Placement && ReferenceEquals(state.Target, target) && state.Draft[0] == "1", "Starting a task pins its target and copies the inspected values");
    }
    static void SelectionCannotReplacePinnedDraft() {
        var state = new GuidedTaskState(); var first = Target("first"); var second = Target("second");
        state.Start(GuidedTaskKind.Placement, first, new[] { "old" }); state.SetValues(new[] { "draft" });
        Check(!state.Select(second) && ReferenceEquals(state.Target, first) && state.Draft[0] == "draft" && !String.IsNullOrEmpty(state.Message), "A different selection cannot replace a pinned draft");
        state.Review();
        Check(!state.Select(second) && ReferenceEquals(state.Target, first) && state.Draft[0] == "draft", "Review also keeps the pinned target and draft");
        state.Back();
        Check(state.Step == GuidedTaskStep.Values && state.Draft[0] == "draft", "Back from Review keeps the draft");
        state.Back();
        Check(state.Step == GuidedTaskStep.Target && state.Target == null && state.Draft.Length == 0 && state.Select(second), "Back from Values discards the draft and permits selection");
    }
    static void FailureAndSuccessKeepTheirRecoveryPaths() {
        var state = new GuidedTaskState(); var first = Target("first");
        state.Open(); Check(state.Enabled && state.Step == GuidedTaskStep.Target, "Open shows the task list");
        state.Start(GuidedTaskKind.Binding, first, new[] { "binding" }); state.Review("stale", "refresh");
        Check(state.Step == GuidedTaskStep.Review && state.Message == "stale" && state.Recovery == "refresh", "Review presents the refusal and recovery");
        state.Complete(false, "failed", "retry");
        Check(state.Step == GuidedTaskStep.Review && !state.Success && state.Recovery == "retry" && state.Draft[0] == "binding", "Failure stays in Review with its draft and recovery");
        state.Complete(true, "applied", null);
        Check(state.Step == GuidedTaskStep.Applied && state.Success && ReferenceEquals(state.BackToInspector(), first) && !state.Enabled && state.Draft.Length == 0, "Success returns to the same inspector and discards the task");
        state.Start(GuidedTaskKind.Activity, first, new[] { "active" }); state.Review(); state.Complete(true, "applied", null); state.AnotherTask();
        Check(state.Enabled && state.Step == GuidedTaskStep.Target && state.Target == null && state.Draft.Length == 0, "Another task returns to the task list");
        state.Start(GuidedTaskKind.Snap, first, new[] { "track" }); state.Cancel();
        Check(!state.Enabled && state.Target == null && state.Draft.Length == 0, "Cancel discards the pinned task");
    }
    static void DraftCannotBeChangedThroughAnArrayAlias() {
        var state = new GuidedTaskState(); state.Start(GuidedTaskKind.Placement, Target("first"), new[] { "old" });
        var edited = new[] { "draft" }; state.SetValues(edited); edited[0] = "outside";
        var read = state.Draft; read[0] = "outside again";
        Check(state.Draft[0] == "draft", "The draft owns its values after both input and output");
    }
    static void ReviewUpdatesARefusal() {
        var state = new GuidedTaskState(); state.Start(GuidedTaskKind.Placement, Target("first"), new[] { "draft" });
        state.Review(); state.Review("stale", "refresh");
        Check(state.Step == GuidedTaskStep.Review && state.Message == "stale" && state.Recovery == "refresh" && state.Draft[0] == "draft", "A later refusal updates Review without losing the draft");
    }
    static void BackClearsRecovery() {
        var state = new GuidedTaskState(); state.Start(GuidedTaskKind.Placement, Target("first"), new[] { "draft" });
        state.Review("stale", "refresh"); state.Back();
        Check(state.Step == GuidedTaskStep.Values && state.Message == null && state.Recovery == null && !state.Success && state.Draft[0] == "draft", "Back clears the Review outcome while preserving the draft");
    }
    static void SharedHandleDoesNotIdentifyAComponent() {
        var first = Target("first");
        var other = new SceneObservation(first.Handle, new TargetIdentity("scene", "first", "Other"), "Track marker", true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "placement");
        var state = new GuidedTaskState(); state.Start(GuidedTaskKind.Placement, first, new[] { "draft" });
        Check(!state.Select(other) && ReferenceEquals(state.Target, first) && state.Draft[0] == "draft", "A different component on the same handle cannot replace the pinned target");
    }
    static void InvalidStepActionsAreIgnored() {
        var state = new GuidedTaskState(); state.Open(); state.Review("ignored", "ignored"); state.Complete(true, "ignored", "ignored");
        Check(state.Step == GuidedTaskStep.Target && state.Message == null && state.Recovery == null && !state.Success, "Review and Complete do nothing on Target");
        var first = Target("first"); state.Start(GuidedTaskKind.Placement, first, new[] { "draft" }); state.Complete(true, "ignored", "ignored");
        Check(state.Step == GuidedTaskStep.Values && state.Message == null && !state.Success && state.Draft[0] == "draft", "Complete does nothing on Values");
        state.Review(); state.Complete(true, "applied", null); state.Review("ignored", "ignored"); state.Complete(false, "ignored", "ignored");
        Check(state.Step == GuidedTaskStep.Applied && state.Message == "applied" && state.Recovery == null && state.Success, "Review and Complete do nothing after application");
    }
    static void SelectionAndBackOutsidePinnedSteps() {
        var state = new GuidedTaskState(); var first = Target("first"); var second = Target("second"); state.Open();
        Check(state.Select(first) && ReferenceEquals(state.Target, first) && state.Select(second) && ReferenceEquals(state.Target, second), "Target permits a different selection");
        state.Start(GuidedTaskKind.Placement, second, new[] { "draft" }); state.Review(); state.Complete(true, "applied", null);
        Check(state.Select(first), "Applied permits selection"); state.Back();
        Check(state.Step == GuidedTaskStep.Applied && state.Success && ReferenceEquals(state.Target, second) && state.Draft[0] == "draft", "Back from Applied leaves the result and its target intact");
    }
    static void ClearingPinnedTargetIsRefused() {
        var state = new GuidedTaskState(); var first = Target("first"); state.Start(GuidedTaskKind.Placement, first, new[] { "draft" });
        Check(!state.Select(null) && ReferenceEquals(state.Target, first) && state.Draft[0] == "draft" && state.Message == "Finish this task or return to Inspector before clearing the selection or selecting another object.", "Clearing Values is refused with the selection feedback");
        state.Review();
        Check(!state.Select(null) && ReferenceEquals(state.Target, first) && state.Draft[0] == "draft" && state.Message == "Finish this task or return to Inspector before clearing the selection or selecting another object.", "Clearing Review is refused with the selection feedback");
    }
    static void StartHandlesMissingValuesWithoutCorruptingState() {
        var state = new GuidedTaskState(); var first = Target("first"); var second = Target("second");
        state.Start(GuidedTaskKind.DetachBinding, first, Array.Empty<string>());
        Check(state.Step == GuidedTaskStep.Values && state.Draft.Length == 0 && ReferenceEquals(state.Target, first), "A task can start without entered values");
        state.SetValues(new[] { "draft" }); state.Review("refused", "recover");
        try { state.Start(GuidedTaskKind.Placement, second, null); throw new Exception("Null values must be refused"); }
        catch (NullReferenceException) { }
        Check(state.Enabled && state.Step == GuidedTaskStep.Review && state.Kind == GuidedTaskKind.DetachBinding && ReferenceEquals(state.Target, first) && state.Draft[0] == "draft" && state.Message == "refused" && state.Recovery == "recover" && !state.Success, "A failed Start leaves the previous task untouched");
    }
    static void GuidedResultSurvivesInspectorRestoration() {
        var feedback = new EditFeedback(); var first = new object(); feedback.Inspecting(first); feedback.View("guided");
        feedback.Report("Placement", "Move completed"); feedback.View("inspector/placement", preserve: true);
        Check(feedback.Result == "Move completed", "Restoring the inspector retains the guided result");
        feedback.View("inspector/connections");
        Check(feedback.Result == "", "An ordinary tab change clears the retained result");
        feedback.View("guided"); feedback.Report("Placement", "Move completed"); feedback.View("inspector/placement", preserve: true); feedback.HistoryMoved();
        Check(feedback.Result == "", "History changes still clear a retained result");
        feedback.View("guided"); feedback.Report("Placement", "Move completed"); feedback.View("inspector/placement", preserve: true); feedback.Inspecting(new object());
        Check(feedback.Result == "", "Inspecting another object still clears a retained result");
        feedback.View("guided"); feedback.Report("Placement", "Move completed"); feedback.View("inspector/placement", preserve: false);
        Check(feedback.Result == "", "An explicit ordinary view change still clears tab results");
    }
    static void RefreshClearsMessagesButKeepsContext() {
        var feedback = new EditFeedback(); var first = new object(); feedback.Inspecting(first); feedback.View("guided/Values");
        feedback.Refuse("Placement", "Move", "stale"); feedback.Refreshed();
        Check(feedback.Messages.Count == 0, "Refreshing removes the earlier refusal");
        feedback.Report("Placement", "Move completed"); feedback.Inspecting(first); feedback.View("guided/Values");
        Check(feedback.Result == "Move completed", "The same object and view retain a new result after refresh");
        feedback.View("guided/Review");
        Check(feedback.Messages.Count == 0, "A later view change clears the new result");
        feedback.Report("Placement", "Move completed"); feedback.Inspecting(new object());
        Check(feedback.Messages.Count == 0, "Refresh retains the inspected identity, so another object clears the result");
    }
    static void GuidedStepChangesClearEarlierRefusals() {
        var feedback = new EditFeedback(); feedback.Inspecting(new object()); feedback.View("guided/Values");
        feedback.Refuse("Placement", "Move", "invalid"); feedback.View("guided/Review");
        Check(feedback.Messages.Count == 0, "Entering Review clears the Values refusal");
        feedback.Refuse("Placement", "Move", "stale"); feedback.View("guided/Target");
        Check(feedback.Messages.Count == 0, "Returning to Target clears the Review refusal");
        feedback.View("guided/Review"); feedback.Report("Placement", "Move completed"); feedback.View("guided/Applied", preserve: true);
        Check(feedback.Result == "Move completed", "Applied retains the new Apply result");
        feedback.View("inspector/placement", preserve: true);
        Check(feedback.Result == "Move completed", "Returning to Inspector retains the Apply result");
    }
}
