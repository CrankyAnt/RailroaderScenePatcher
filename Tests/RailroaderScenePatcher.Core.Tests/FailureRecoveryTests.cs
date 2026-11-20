// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Failure isolation, recovery and retry through complete scene patch files and the scene patch editing session: each failure
// is classified by what it did to the scene, an incomplete cleanup names what remains and blocks retry until a recheck
// verifies it or the world reloads, and prior successful independent work survives.
static class FailureRecoveryTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + items + "}}";
    static string Item(string id, string path, double x = 112, string dependsOn = "[]") => "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + x + ",2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":" + dependsOn + "}";
    static readonly TargetIdentity P = new TargetIdentity("s", "p", "c"), Q = new TargetIdentity("s", "q", "c");
    static Placement At(double x) => new Placement(new[] { x, 2.0, 3.0 }, new[] { 0.0, 0.0, 0.0, 1.0 });
    static Box NewBox(double x) => new Box { Position = new[] { x, 2.0, 3.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
    internal static void Run() {
        WholeScenePatchFileFailuresAreDistinguished();
        IncompleteCleanupNamesWhatRemainsAndBlocksRetry();
        ReloadEvaluatesAgainWithoutWithinLoadRetry();
        SessionDistinguishesRefusalRestoredAndIncompleteCleanup();
        RecheckRunsFreshChecksAndRefreshDoesNotMutate();
        RemainingStateIsHonestWhenItCannotCompare();
        GuidanceNamesCorrectionAndOperationGuide();
        PanelResultsDistinguishOutcomesAndKeepTheGuide();
        PendingRestoredOutputIsNoIncompleteCleanup();
        RetryAfterIncompleteCleanupIsBlockedNotStale();
        FailureColourFollowsTheFailureKind();
        PlayerMessagesDoNotSendPlayersToTheJson();
        CoverageTests.RunRecovery();
        Console.WriteLine("PASS: failure classification, remaining state, guarded retry, recheck reconciliation and recovery guidance");
    }
    static (Scene scene, Box p, Box q, Box r) World() {
        var scene = new Scene { Origin = 0 };
        var p = NewBox(4); var q = NewBox(5); var r = NewBox(6);
        scene.Objects["p"] = new List<object> { p }; scene.Objects["q"] = new List<object> { q }; scene.Objects["r"] = new List<object> { r };
        return (scene, p, q, r);
    }
    // Structural failure skips the whole scene patch file; an invalid modification fails alone, its dependent fails as a
    // dependency failure, a failure after mutation is restored, and independent work applies.
    static void WholeScenePatchFileFailuresAreDistinguished() {
        var (scene, p, q, r) = World();
        var structural = new PatchEngine(scene).Apply("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + Item("keep", "q") + ",\"keep\":{}}}");
        Assert(structural.Error != null && structural.Outcomes.Count == 0 && scene.MutationCalls == 0 && q.X == 5, "A structurally invalid scene patch file is skipped as a whole: " + structural.Error);
        scene.FailingTarget = p;
        var result = new PatchEngine(scene).Apply(Doc(Item("keep", "q", 50) + "," + Item("missing", "absent") + "," + Item("dependent", "r", 60, "[\"missing\"]") + "," + Item("fail", "p", 70)));
        Assert(result.Error == null, "Separately identifiable failures do not fail the scene patch file: " + result.Error);
        Assert(result.Outcomes["keep"].Applied && result.Outcomes["keep"].Failure == FailureKind.None && q.X == 50, "Independent work applies");
        Assert(result.Outcomes["missing"].Failure == FailureKind.Refused, "A missing target is refused before mutation: " + result.Outcomes["missing"].Failure);
        Assert(result.Outcomes["dependent"].Failure == FailureKind.DependencyFailed && r.X == 6, "A dependent of a failed modification is a dependency failure and does not mutate: " + result.Outcomes["dependent"].Failure);
        Assert(result.Outcomes["fail"].Failure == FailureKind.Restored && result.Outcomes["fail"].Remaining.Count == 0 && p.X == 4, "A failure after mutation restores the captured prior state: " + result.Outcomes["fail"].Error);
    }
    static void IncompleteCleanupNamesWhatRemainsAndBlocksRetry() {
        var (scene, p, q, r) = World();
        var engine = new PatchEngine(scene);
        Assert(engine.Apply(Doc(Item("first", "q", 40)), "pack").Outcomes["first"].Applied && q.X == 40, "Earlier work applies");
        scene.FailingTarget = p; scene.FailRestore = true;
        var failed = engine.Apply(Doc(Item("move", "p", 90)), "pack").Outcomes["move"];
        Assert(failed.Failure == FailureKind.IncompleteCleanup && failed.Error.Contains("could not be verified"), "An unverified restoration is an incomplete cleanup: " + failed.Error);
        Assert(failed.Remaining.Count == 1 && failed.Remaining[0].StartsWith("Position: now 90, 2, 3, before 4, 2, 3", StringComparison.Ordinal), "The incomplete cleanup names the remaining state: " + String.Join(" | ", failed.Remaining));
        scene.FailingTarget = null; scene.FailRestore = false;
        var calls = scene.MutationCalls;
        var other = engine.Apply(Doc(Item("other", "p", 20) + "," + Item("unrelated", "r", 30)), "other").Outcomes;
        Assert(other["other"].Failure == FailureKind.Blocked && other["other"].Remaining.SequenceEqual(failed.Remaining) && p.X == 90, "Another operation on the same target is blocked and repeats what remains: " + other["other"].Error);
        Assert(other["unrelated"].Applied && r.X == 30 && scene.MutationCalls == calls + 1, "Unrelated work stays available");
        Assert(q.X == 40, "The earlier successful work survives the failure");
    }
    // Ordinary play does not retry a failure within its load attempt; the next load attempt evaluates the definitions again.
    static void ReloadEvaluatesAgainWithoutWithinLoadRetry() {
        var (scene, p, q, r) = World();
        var engine = new PatchEngine(scene);
        var json = Doc(Item("move", "p", 90) + "," + Item("keep", "q", 50));
        scene.FailingTarget = p;
        var first = engine.Apply(json, "pack");
        scene.FailingTarget = null;
        Assert(first.Outcomes["move"].Failure == FailureKind.Restored && first.Outcomes["keep"].Applied, "The first attempt fails in isolation");
        var calls = scene.MutationCalls;
        var again = engine.Apply(json, "pack");
        Assert(again.Outcomes["move"].Failure == FailureKind.AlreadyFailed && p.X == 4 && again.Outcomes["keep"].Applied, "A later callback in the same load attempt does not retry the failure");
        Assert(scene.MutationCalls == calls + 1, "Only the successful modification is reconciled again");
        engine.Reenter();
        var reload = engine.Apply(json, "pack");
        Assert(reload.Outcomes["move"].Applied && p.X == 90, "The next load attempt evaluates the definition again");
        // An incomplete cleanup on a retained object stays guarded; a world that reloads its objects evaluates afresh.
        var g = World(); var guarded = new PatchEngine(g.scene);
        g.scene.FailingTarget = g.p; g.scene.FailRestore = true;
        guarded.Apply(Doc(Item("move", "p", 90)), "pack");
        g.scene.FailingTarget = null; g.scene.FailRestore = false;
        guarded.Reenter();
        Assert(guarded.Apply(Doc(Item("move", "p", 90)), "pack").Outcomes["move"].Failure == FailureKind.Blocked, "A retained object with unverified cleanup stays blocked across a new load attempt");
        g.scene.Objects["p"] = new List<object> { NewBox(4) };
        var released = guarded.Reenter();
        Assert(released.Released == 1 && guarded.Apply(Doc(Item("move", "p", 90)), "pack").Outcomes["move"].Applied, "A reloaded object is evaluated afresh");
    }
    sealed class EditWorld {
        public Scene Scene; public Box P, Q; public Store Store = new Store(); public NativeMapEditor MapEditor = new NativeMapEditor(); public PatchEditingSession Session;
        public SceneInventory Inventory;
        public EditWorld() {
            var w = World(); Scene = w.scene; P = w.p; Q = w.q;
            Scene.Observations = () => new[] { Observation(P, "p"), Observation(Q, "q") };
            Session = new PatchEditingSession(new PatchEngine(Scene), MapEditor, Store);
            MapEditor.Session = Session;
            Inventory = new SceneInventory(Scene); Inventory.Refresh();
        }
        static SceneObservation Observation(Box box, string path) => new SceneObservation(box, new TargetIdentity("s", path, "c"), "Board", box.Active,
            box.Position.ToArray(), box.Rotation.ToArray(), new string[0], new string[0], new string[0], "Fixed placement");
        public InspectedTarget Pin(TargetIdentity target) { Inventory.Refresh(); return Inventory.Pin(Inventory.Entries.Single(e => e.Identity.Path == target.Path)); }
        public JObject ScenePatchFile => JObject.Parse(Session.ScenePatchFileText);
    }
    static void SessionDistinguishesRefusalRestoredAndIncompleteCleanup() {
        var w = new EditWorld();
        Assert(w.Session.Edit(Q, At(40), null).Applied && w.Q.X == 40, "An independent edit applies first");
        var calls = w.Scene.MutationCalls; var groups = w.MapEditor.UndoGroups.Count;
        var refused = w.Session.Edit(P, null, null);
        Assert(refused.Failure == FailureKind.Refused && w.Scene.MutationCalls == calls, "An edit that names nothing is refused without mutation");
        w.Scene.FailNextApplies = 1;
        var restored = w.Session.Edit(P, At(70), null);
        Assert(restored.Failure == FailureKind.Restored && w.P.X == 4 && w.MapEditor.UndoGroups.Count == groups && w.ScenePatchFile["modifications"].Count() == 1, "A failed edit restores the object and the scene patch file: " + restored.Error);
        Assert(w.Session.Edit(P, At(71), null).Applied && w.P.X == 71, "A restored failure accepts a corrected retry at once");
        w.Scene.FailNextApplies = 1; w.Scene.RefuseState = s => s.Placement?.Position[0] != 80;
        var incomplete = w.Session.Edit(P, At(80), null);
        w.Scene.RefuseState = null;
        Assert(incomplete.Failure == FailureKind.IncompleteCleanup && incomplete.Remaining.Any(l => l.StartsWith("Position: now 80", StringComparison.Ordinal)), "An unverified restoration of an edit names what remains: " + String.Join(" | ", incomplete.Remaining));
        Assert(w.Session.CleanupUnverified(P) && w.Session.RemainingState(P).SequenceEqual(incomplete.Remaining), "The session exposes the unverified cleanup and its remaining state");
        Assert((double)w.ScenePatchFile["modifications"]["place-p"]["placement"]["position"][0] == 71, "The scene patch file keeps the last completed edit");
        calls = w.Scene.MutationCalls;
        var blocked = w.Session.Edit(P, At(72), null);
        Assert(blocked.Failure == FailureKind.Blocked && blocked.Remaining.SequenceEqual(incomplete.Remaining) && w.Scene.MutationCalls == calls, "A further edit is blocked until cleanup verifies");
        Assert(w.Session.Edit(Q, At(41), null).Applied && w.Q.X == 41, "Unrelated work stays available");
        Assert(new[] { refused, restored, incomplete, blocked }.Select(o => RecoveryGuidance.For(o, "Placement", "Placement", null).Heading).Distinct().Count() == 4, "Refusal, restored failure, incomplete cleanup and a blocked retry read differently");
    }
    static void RecheckRunsFreshChecksAndRefreshDoesNotMutate() {
        var w = new EditWorld();
        Assert(w.Session.Edit(P, At(30), null, w.Pin(P)).Applied, "The edit applies");
        w.Inventory.Refresh();
        w.Scene.FailNextApplies = 1; w.Scene.RefuseState = s => s.Placement?.Position[0] != 80;
        Assert(w.Session.Edit(P, At(80), null, w.Pin(P)).Failure == FailureKind.IncompleteCleanup, "The cleanup is incomplete");
        w.Scene.RefuseState = null;
        var calls = w.Scene.MutationCalls;
        w.Inventory.Refresh();
        Assert(w.Scene.MutationCalls == calls && w.P.X == 80 && w.Session.CleanupUnverified(P), "Refresh alone mutates nothing and does not lift the guard");
        // A recheck that cannot converge stays an incomplete cleanup against the scene patch file.
        w.Scene.FailApply = true;
        var stuck = w.Session.Recheck(P, w.Pin(P));
        w.Scene.FailApply = false;
        Assert(stuck.Failure == FailureKind.IncompleteCleanup && w.Session.CleanupUnverified(P), "A failing recheck keeps the guard: " + stuck.Error);
        w.Inventory.Refresh();
        var resolves = w.Scene.ResolveCalls;
        var recheck = w.Session.Recheck(P, w.Pin(P));
        Assert(recheck.Applied && w.P.X == 30 && !w.Session.CleanupUnverified(P) && w.Session.RemainingState(P).Count == 0 && w.Scene.ResolveCalls > resolves, "Recheck resolves afresh, applies the scene patch file and lifts the guard: " + recheck.Error);
        // A recheck with a stale inspection is refused without mutation.
        var stale = w.Pin(P); w.P.X = 31; calls = w.Scene.MutationCalls;
        Assert(w.Session.Recheck(P, stale).Failure == FailureKind.Refused && w.Scene.MutationCalls == calls, "A recheck of a changed observation is refused before mutation");
        w.P.X = 30; w.Inventory.Refresh();
        calls = w.Scene.MutationCalls;
        Assert(w.Session.Recheck(P, w.Pin(P)).Applied && w.Scene.MutationCalls == calls, "A recheck of an object that already agrees mutates nothing");
        Assert(w.Session.Edit(P, At(33), null, w.Pin(P)).Applied && w.P.X == 33, "After the recheck the object accepts edits again");
    }
    static void RemainingStateIsHonestWhenItCannotCompare() {
        var before = new ObjectState(At(4), true);
        var unread = RemainingState.Describe(before, () => throw new InvalidOperationException("injected read"));
        Assert(unread.Count == 1 && unread[0].Contains("could not be read") && unread[0].Contains("uncertain"), "A state that cannot be read is uncertain");
        var same = RemainingState.Describe(before, () => new ObjectState(At(4), true), "mask registration");
        Assert(same.Count == 1 && same[0].Contains("could not verify the related changes") && same[0].Contains("mask registration") && same[0].StartsWith("The settings match their values before the attempt.", StringComparison.Ordinal), "Agreeing settings with an unverified capability check stay uncertain");
        Assert(RemainingState.Describe(null, () => before)[0].Contains("uncertain"), "Without the prior state nothing can be compared");
        var disabled = RemainingState.Describe(before, () => new ObjectState(At(4), false));
        Assert(disabled.Single() == "Enabled: now No, before Yes", "A changed activity is named: " + disabled.Single());
    }
    static void GuidanceNamesCorrectionAndOperationGuide() {
        var w = new EditWorld();
        Assert(RecoveryGuidance.For(w.Session.Edit(Q, At(40), null), "Placement", "Placement", null) == null, "An applied outcome needs no guidance");
        w.Scene.FailNextApplies = 1;
        var restored = RecoveryGuidance.For(w.Session.Edit(P, At(70), null), "Placement", "Placement", IndependentBoards.Structure);
        Assert(restored.Kind == FailureKind.Restored && restored.Guide == "editing-placement-in-the-map-editor.md#set-a-numeric-placement" && restored.Line("Placement").Contains("was undone") && restored.Correction.Contains("Correct the cause and try again."), "A restored placement failure links the placement guide");
        var pin = w.Pin(P); w.P.X = 5;
        var stale = RecoveryGuidance.For(w.Session.Edit(P, At(71), null, pin), "Modification", "Removal", BridgeScene.Structure);
        w.Scene.FailNextApplies = 1; w.Scene.RefuseState = s => s.Placement?.Position[0] != 80;
        var incomplete = RecoveryGuidance.For(w.Session.Edit(P, At(80), null), "Track binding", "Set binding", CrossingPadScene.Structure);
        w.Scene.RefuseState = null;
        Assert(incomplete.Kind == FailureKind.IncompleteCleanup && incomplete.Guide == "rebinding-crossing-pads.md" && incomplete.Correction.Contains("Recheck") && incomplete.Line("Track binding").Contains("What remains: Position: now 80, 2, 3, before 5, 2, 3."), "An incomplete cleanup names what remains and Recheck: " + incomplete.Line("Track binding"));
        Assert(stale.Kind == FailureKind.Refused && stale.Guide == "placing-bridges.md" && stale.Correction.StartsWith("Nothing changed. Refresh", StringComparison.Ordinal), "A stale target asks for a refresh and links the capability guide");
        foreach (var guidance in new[] { restored, incomplete, stale })
            Assert(!guidance.Correction.Contains("JSON") && !guidance.Correction.Contains("scene-patches.json"), "Scene patch editing guidance corrects through the editor, not the file: " + guidance.Correction);
        // Each panel action opens the guide of the operation it ran: the place, the action and the target's capability decide.
        var routes = new[] {
            new[] { "Placement", "Placement", IndependentBoards.Structure, "editing-placement-in-the-map-editor.md#set-a-numeric-placement" },
            new[] { "Gizmo", "Gizmo drag", IndependentBoards.Structure, "using-transform-gizmos.md" },
            new[] { "Snap", "Physical snap", IndependentBoards.Structure, "snapping-to-track.md" },
            new[] { "Follow", "Track following", TrackMarkerScene.Structure, "following-track.md" },
            new[] { "Track binding", "Set binding", TrackMarkerScene.Structure, "repairing-track-bindings.md" },
            new[] { "Track binding", "Set binding", CrossingPadScene.Structure, "rebinding-crossing-pads.md" },
            new[] { "Track binding", "Place starter", StarterPlacementScene.Structure, "placing-starter-markers.md" },
            new[] { "Track binding", "Set binding", MilepostScene.Structure, "repositioning-mileposts.md" },
            new[] { "Start derailed", "Start derailed: Yes", StarterPlacementScene.Structure, "placing-starter-markers.md" },
            new[] { "Crossing coverage", "Crossing coverage", CrossingCoverageScene.Structure, "correcting-crossing-coverage.md" },
            new[] { "Crossing coverage", RecoveryGuidance.DetectionAddition, CrossingCoverageScene.Structure, "adding-crossing-detection.md" },
            new[] { "Lights and gates", "Adding a gate", CrossingCoverageScene.Structure, "adding-crossing-equipment.md" },
            new[] { "Bells", "Adding a bell", CrossingCoverageScene.Structure, "adding-crossing-bells.md" },
            new[] { "Modification", "Removal", ExistingEquipmentScene.MastStructure, "moving-crossing-equipment.md" },
            new[] { "Modification", "Removal", BakedSceneryParts.Structure, "moving-baked-scenery-groups.md" },
            new[] { "Modification", "Recheck", BridgeScene.Structure, "placing-bridges.md" },
            new[] { "Modification", "Removal", IndependentBoards.Structure, "moving-boards.md" },
            new[] { "Modification", "Recheck", null, RecoveryGuidance.EditingGuide } };
        foreach (var route in routes) {
            var guide = RecoveryGuidance.Guide(route[0], route[1], route[2]).Value;
            Assert(guide == route[3], route[0] + " / " + route[1] + " opens " + route[3] + ", not " + guide);
            Assert(System.IO.File.Exists(System.IO.Path.Combine(RepositoryRoot(), "docs", "guides", "scene-patches", guide.Split('#')[0])), "The guide " + guide + " exists");
        }
    }
    // The message area's result for each outcome, through the panel's own feedback route.
    static void PanelResultsDistinguishOutcomesAndKeepTheGuide() {
        var w = new EditWorld();
        var feedback = new RailroaderScenePatcher.EditFeedback();
        bool Run(Func<InspectedTarget, Outcome> change) { w.Inventory.Refresh(); return feedback.Run("Placement", "Placement", null, w.Inventory, w.Inventory.Entries.Single(e => e.Identity.Path == "p"), change); }
        Assert(!Run(inspection => w.Session.Edit(P, null, null, inspection)) && feedback.Result.StartsWith("Placement refused. Nothing to change. Nothing changed.", StringComparison.Ordinal) && feedback.Recovery.Kind == FailureKind.Refused, "A refusal says nothing changed: " + feedback.Result);
        w.Scene.FailNextApplies = 1;
        Assert(!Run(inspection => w.Session.Edit(P, At(70), null, inspection)) && feedback.Result.StartsWith("Placement failed and was undone. ", StringComparison.Ordinal) && feedback.Result.Contains("The scene patch file is unchanged.") && !feedback.Result.Contains("back as it was"), "A restored failure says it was undone once: " + feedback.Result);
        w.Scene.FailNextApplies = 1; w.Scene.RefuseState = s => s.Placement?.Position[0] != 80;
        Assert(!Run(inspection => w.Session.Edit(P, At(80), null, inspection)) && feedback.Result.StartsWith("Placement failed; cleanup is incomplete. ", StringComparison.Ordinal) && feedback.Result.Contains("What remains: Position: now 80") && feedback.Result.Contains("Recheck"), "An incomplete cleanup names what remains and Recheck: " + feedback.Result);
        w.Scene.RefuseState = null;
        Assert(feedback.Recovery.Guide == "editing-placement-in-the-map-editor.md#set-a-numeric-placement" && feedback.Recovery.GuideTitle == "Fixed placement", "The result keeps the operation's guide for the footer");
        Assert(!Run(inspection => w.Session.Edit(P, At(81), null, inspection)) && feedback.Result.StartsWith("Placement is blocked by incomplete cleanup. ", StringComparison.Ordinal), "A blocked retry says why: " + feedback.Result);
        Assert(Run(inspection => w.Session.Recheck(P, inspection)) && feedback.Recovery == null, "A completed action replaces the guidance with its result");
    }
    // A scene whose mutations leave dependent output, such as terrain, outstanding until the test settles it.
    sealed class OutputScene : Scene, IDependentOutputScene {
        public bool Pending;
        public DependentOutput Output(object target) => Pending ? new DependentOutput(new string[0], new[] { "terrain tile" }, null) : DependentOutput.None;
        public void RetryOutput(object target) { }
    }
    // A restored edit whose terrain is still being regenerated is waiting, not an incomplete cleanup: the next edit says
    // so, the panel asks for no Recheck, and the footer's cleanup line stays empty.
    static void PendingRestoredOutputIsNoIncompleteCleanup() {
        var scene = new OutputScene { Origin = 0 }; var p = NewBox(4);
        scene.Objects["p"] = new List<object> { p };
        var session = new PatchEditingSession(new PatchEngine(scene), new NativeMapEditor(), new Store());
        scene.FailNextApplies = 1; scene.Pending = true;
        var failed = session.Edit(P, At(70), null);
        Assert(failed.Failure == FailureKind.AwaitingOutput && p.X == 4, "A restored edit with pending output waits for it: " + failed.Error);
        var waiting = session.Edit(P, At(71), null);
        var guidance = RecoveryGuidance.For(waiting, "Placement", "Placement", IndependentBoards.Structure);
        Assert(waiting.Failure == FailureKind.AwaitingOutput && waiting.Remaining.Count == 0 && p.X == 4, "An edit while the restored output is pending is refused as waiting, not blocked: " + waiting.Failure + " " + waiting.Error);
        Assert(guidance.Heading == "is waiting for terrain" && guidance.Correction.Contains("click Retry on the Changes tab")
            && guidance.Line("Placement").Contains("The game is still regenerating terrain after an earlier edit.")
            && !guidance.Line("Placement").Contains("was undone"), "The second edit reports the pending terrain without claiming it was undone: " + guidance.Line("Placement"));
        Assert(!session.CleanupUnverified(P), "Pending output is not an unverified cleanup");
        var guard = RecoveryGuidance.For(session.GuardOutcome(P), "Placement", "Placement", IndependentBoards.Structure);
        Assert(guard?.Kind == FailureKind.AwaitingOutput && guard.Heading == "is waiting for terrain", "The guard the panel checks first also reports that the edit is waiting.");
        scene.Pending = false;
        Assert(session.Edit(P, At(72), null).Applied && p.X == 72, "Once the output completes the object accepts edits");
        // The engine reports the same at world load.
        var engineScene = new OutputScene { Origin = 0 }; var b = NewBox(4);
        engineScene.Objects["p"] = new List<object> { b };
        var engine = new PatchEngine(engineScene);
        engineScene.FailNextApplies = 1; engineScene.Pending = true;
        Assert(engine.Apply(Doc(Item("move", "p", 90)), "pack").Outcomes["move"].Failure == FailureKind.AwaitingOutput, "A restored world-load failure with pending output waits");
        Assert(engine.Apply(Doc(Item("other", "p", 91)), "other").Outcomes["other"].Failure == FailureKind.AwaitingOutput, "Another operation on that target waits as well");
    }
    // The retry after an incomplete cleanup that left the object moved: the moved object makes the inspection stale, and
    // the panel's guard, checked before the pin compares the scene, refuses the retry for the cleanup instead.
    static void RetryAfterIncompleteCleanupIsBlockedNotStale() {
        var w = new EditWorld();
        var entry = w.Inventory.Entries.Single(e => e.Identity.Path == "p");
        var feedback = new RailroaderScenePatcher.EditFeedback();
        w.Scene.FailNextApplies = 1; w.Scene.RefuseState = s => s.Placement?.Position[0] != 80;
        Assert(!feedback.Run("Placement", "Placement", null, w.Inventory, entry, inspection => w.Session.Edit(P, At(80), null, inspection)) && feedback.Recovery?.Kind == FailureKind.IncompleteCleanup && w.P.X == 80, "The cleanup is incomplete and the object stays moved: " + feedback.Result);
        w.Scene.RefuseState = null;
        w.Inventory.CheckFreshness();
        Assert(w.Inventory.IsStale, "The moved object makes the inspection stale");
        var calls = w.Scene.MutationCalls;
        Assert(!feedback.Run("Placement", "Placement", null, w.Inventory, entry, inspection => w.Session.Edit(P, At(81), null, inspection), guard: () => w.Session.GuardOutcome(P)), "The retry does not run");
        Assert(feedback.Result.StartsWith("Placement is blocked by incomplete cleanup. ", StringComparison.Ordinal) && feedback.Result.Contains("What remains: Position: now 80") && !feedback.Result.Contains("stale"), "The retry is refused for the incomplete cleanup, not as stale: " + feedback.Result);
        Assert(feedback.Recovery.Kind == FailureKind.Blocked && feedback.Recovery.Guide == "editing-placement-in-the-map-editor.md#set-a-numeric-placement" && w.Scene.MutationCalls == calls && w.P.X == 80, "Guide stays available and nothing changed");
        Assert(w.Session.GuardOutcome(Q) == null, "Another object has no guard");
    }
    static void FailureColourFollowsTheFailureKind() {
        Assert(RailroaderScenePatcher.CommandBand.RecoveryWarning(FailureKind.AwaitingOutput), "An edit that was undone and waits for its terrain is a warning");
        foreach (var kind in new[] { FailureKind.Refused, FailureKind.DependencyFailed, FailureKind.Restored, FailureKind.IncompleteCleanup, FailureKind.Blocked, FailureKind.AlreadyFailed })
            Assert(!RailroaderScenePatcher.CommandBand.RecoveryWarning(kind), kind + " is a failure colour");
        // The message of an undone edit that waits for its terrain carries that kind, whatever its words.
        var scene = new OutputScene { Origin = 0 }; var p = NewBox(4);
        scene.Objects["p"] = new List<object> { p };
        scene.Observations = () => new[] { new SceneObservation(p, P, "Board", p.Active, p.Position.ToArray(), p.Rotation.ToArray(), new string[0], new string[0], new string[0], "Fixed placement") };
        var session = new PatchEditingSession(new PatchEngine(scene), new NativeMapEditor(), new Store());
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var feedback = new RailroaderScenePatcher.EditFeedback();
        scene.FailNextApplies = 1; scene.Pending = true;
        feedback.Run("Placement", "Placement", null, inventory, inventory.Entries.Single(), inspection => session.Edit(P, At(70), null, inspection));
        Assert(feedback.Messages[0].Recovery?.Kind == FailureKind.AwaitingOutput && feedback.Result.Contains("was undone; waiting for terrain") && RailroaderScenePatcher.CommandBand.RecoveryWarning(feedback.Messages[0].Recovery.Kind), "The message reports the restoration and takes the warning colour by its kind: " + feedback.Result);
    }
    static string RepositoryRoot() {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        if (directory == null) {
            directory = new System.IO.DirectoryInfo(Environment.CurrentDirectory);
            while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
    sealed class Rows : IPatchDiagnostics { public readonly List<PatchFailure> Failures = new List<PatchFailure>(); public void Warning(PatchFailure failure) => Failures.Add(failure); }
    // A runtime failure goes to the player as a report request, never as a request to repair the distributed scene patch file.
    static void PlayerMessagesDoNotSendPlayersToTheJson() {
        var (scene, p, q, r) = World();
        scene.FailingTarget = p; scene.FailRestore = true;
        var rows = new Rows();
        var runtime = new PatchRuntime(new PatchEngine(scene), rows, "0.5.0", "0.6.2");
        var info = "{\"id\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.5.0\"]}}";
        var mod = LoadedMod.Read("author/pack", info, Doc(Item("move", "p", 90) + "," + Item("missing", "absent")));
        runtime.ApplyLoaded(new[] { mod }, null);
        Assert(rows.Failures.Count == 2, "Each failure becomes one player row: " + rows.Failures.Count);
        Assert(rows.Failures.Single(f => f.Operation == "move").Message.Contains("Remaining state: Position: now 90, 2, 3, before 4, 2, 3"), "The player row of an incomplete cleanup names what remains: " + rows.Failures.Single(f => f.Operation == "move").Message);
        foreach (var row in rows.Failures)
            Assert(row.Message.Contains("Report to the") && !row.Message.Contains("Recheck") && !row.Message.Contains("correct") && !row.Message.Contains("Correct"), "A player row asks for a report and gives no repair instruction: " + row.Message);
    }
}
