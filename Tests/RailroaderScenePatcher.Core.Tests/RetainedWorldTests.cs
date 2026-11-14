// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Retained-world reconciliation: a later load attempt over the same local map keeps the patcher state of the objects that
// still stand under their identities and releases the state of objects that were reconstructed.
// Re-implements IScene so its Resolve replaces the base resolution.
class ThrowingScene : PeerScene, IScene {
    public bool ThrowOnResolve;
    public new IReadOnlyList<object> Resolve(TargetIdentity identity) => ThrowOnResolve ? throw new InvalidOperationException("scene not readable") : base.Resolve(identity);
}
static class RetainedWorldTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":[" + items + "]}";
    static string Item(string id, string path, double x = 112) => "{\"id\":\"" + id + "\",\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + x + ",2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
    static PeerScene World(out Box board) {
        board = new Box { Position = new[] { 1.0, 1.0, 1.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        var scene = new PeerScene { Origin = 0 };
        scene.Objects["board"] = new List<object> { board };
        return scene;
    }
    public static void Run() {
        ReconstructedGraphReentersTheRetainedWorld();
        EarlyChecksOpenOneLoadAttempt();
        RetainedTargetKeepsItsGuardAndAReconstructedTargetStartsFresh();
        FailuresAreRetriedInANewLoadAttemptOnly();
        RemovalAfterReentryReturnsToTheNativeState();
        AnUncheckableTargetKeepsItsStateAndIsReportedLocally();
        AFailedReconciliationRestartsOnlyThisPeersWorld();
        Console.WriteLine("PASS: retained-world reconciliation by world, graph and target identity");
    }
    static void ReconstructedGraphReentersTheRetainedWorld() {
        var scenes = new List<PeerScene>();
        var peer = new LocalPeer(() => { var s = World(out _); scenes.Add(s); return s; });
        var graph = new object(); var rebuilt = new object();
        Assert(peer.Enter("map", graph) == WorldEntry.Started && scenes.Count == 1 && peer.Reentries == 0, "The first world load starts the local world");
        Assert(peer.Enter("map", graph) == WorldEntry.Repeated && peer.Reentries == 0, "A repeated callback with the same graph stays in the same load attempt");
        Assert(peer.Enter("map", rebuilt) == WorldEntry.Reentered && scenes.Count == 1 && !scenes[0].Disposed && peer.Reentries == 1, "A reconstructed graph starts a new load attempt over the retained world without rebuilding its capabilities");
        Assert(peer.Enter("map", rebuilt) == WorldEntry.Repeated && peer.Reentries == 1, "Repeated callbacks after re-entry do not reconcile again");
        peer.Unload();
        Assert(peer.Enter("map", rebuilt) == WorldEntry.Reentered && scenes.Count == 1 && peer.Reentries == 2, "A world unload ends the load attempt; loading the same map again reconciles the retained state");
        Assert(peer.Enter("another-map", rebuilt) == WorldEntry.Started && scenes.Count == 2 && scenes[0].Disposed && !scenes[1].Disposed && peer.Reentries == 2, "Another map releases the retained world and starts its own");
    }
    static void EarlyChecksOpenOneLoadAttempt() {
        var scenes = new List<PeerScene>();
        var peer = new LocalPeer(() => { var s = World(out _); scenes.Add(s); return s; });
        var early = peer.Prepare("map");
        Assert(early != null && scenes.Count == 1 && peer.Reentries == 0, "A native check before the first world load prepares the local world");
        Assert(peer.Enter("map", "graph") == WorldEntry.Started && ReferenceEquals(peer.Engine, early.Engine) && scenes.Count == 1, "The world load binds the state that the early check prepared");
        peer.Unload();
        Assert(ReferenceEquals(peer.Prepare("map"), early) && peer.Reentries == 1, "An early check after an unload opens the next load attempt over the retained world");
        Assert(ReferenceEquals(peer.Prepare("map"), early) && peer.Reentries == 1, "Further early checks share that load attempt");
        Assert(peer.Enter("map", "graph") == WorldEntry.Reentered && peer.Reentries == 1, "The world load joins the load attempt that the early check opened");
        peer.Unload();
        var other = peer.Prepare("another-map");
        Assert(other != null && !ReferenceEquals(other, early) && scenes[0].Disposed && peer.Enter("another-map", "graph") == WorldEntry.Started, "An early check on another map releases the retained world");
    }
    static void RetainedTargetKeepsItsGuardAndAReconstructedTargetStartsFresh() {
        var scene = World(out var board);
        var peer = new LocalPeer(() => scene);
        peer.Enter("map", "graph");
        scene.FailApply = true; scene.FailRestore = true;
        Assert(!peer.Engine.Apply(Doc(Item("move-board", "board")), "pack").Outcomes["move-board"].Applied, "The mutation fails and its restoration cannot be confirmed");
        scene.FailApply = false; scene.FailRestore = false;
        Assert(peer.Enter("map", "rebuilt-graph") == WorldEntry.Reentered && peer.LastReentry.Retained == 1 && peer.LastReentry.Released == 0, "Re-entry keeps the target that still stands under its identity");
        var guarded = peer.Engine.Apply(Doc(Item("move-board", "board")), "pack");
        Assert(!guarded.Outcomes["move-board"].Applied && guarded.Outcomes["move-board"].Error.Contains("cleanup is unverified") && scene.MutationCalls == 1, "The unverified-cleanup guard of a retained target carries over into the new load attempt");
        var replacement = new Box { Position = new[] { 1.0, 1.0, 1.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        scene.Objects["board"] = new List<object> { replacement };
        peer.Unload();
        Assert(peer.Enter("map", "rebuilt-graph") == WorldEntry.Reentered && peer.LastReentry.Released == 1 && peer.LastReentry.Retained == 0, "Re-entry releases the state of a target that was reconstructed");
        var fresh = peer.Engine.Apply(Doc(Item("move-board", "board")), "pack");
        Assert(fresh.Outcomes["move-board"].Applied && replacement.X == 112 && ReferenceEquals(fresh.Outcomes["move-board"].ResolvedTarget, replacement), "The reconstructed target gets its own application: " + fresh.Outcomes["move-board"].Error);
    }
    // The retained object already carries the patched placement when the new load attempt starts. Its underlying state is
    // the native placement captured before the first application, so removing the modification returns it there.
    static void RemovalAfterReentryReturnsToTheNativeState() {
        var scene = World(out var board);
        var peer = new LocalPeer(() => scene);
        var json = Doc(Item("move-board", "board"));
        peer.Enter("map", "graph");
        peer.Engine.Apply(json, "pack");
        Assert(peer.Enter("map", "rebuilt-graph") == WorldEntry.Reentered && peer.Engine.Apply(json, "pack").Outcomes["move-board"].Applied && board.X == 112, "The retained board keeps its patched placement through re-entry");
        var editor = new Editor();
        var session = new AuthoringSession(peer.Engine, editor, new Store { Patch = json }, "map", documentKey: "pack"); editor.Session = session;
        var removed = session.Remove(new TargetIdentity("s", "board", "c"));
        Assert(removed.Applied && board.X == 1 && board.Position[1] == 1 && board.Position[2] == 1, "Removal after re-entry returns the board to its native placement, not to the patched one: " + removed.Error + " at " + board.X);
    }
    // A scene check that throws during reconciliation cannot show that the object is gone, so the engine keeps its state.
    static void AnUncheckableTargetKeepsItsStateAndIsReportedLocally() {
        var scene = new ThrowingScene { Origin = 0 };
        var board = new Box { Position = new[] { 1.0, 1.0, 1.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        scene.Objects["board"] = new List<object> { board };
        var peer = new LocalPeer(() => scene);
        peer.Enter("map", "graph");
        scene.FailApply = true; scene.FailRestore = true;
        peer.Engine.Apply(Doc(Item("move-board", "board")), "pack");
        scene.FailApply = false; scene.FailRestore = false;
        scene.ThrowOnResolve = true; peer.Unload(); peer.Enter("map", "graph"); scene.ThrowOnResolve = false;
        var report = peer.LastReentry;
        Assert(report.Retained == 1 && report.Released == 0 && report.Failures.Count == 1 && report.Failures[0].Contains("s/board") && report.Failures[0].Contains("could not be checked, so its state is kept"), "An object that cannot be checked is kept and reported for its target: " + String.Join("; ", report.Failures));
        Assert(peer.ReentryError == null && peer.Engine != null, "The world and its patcher state stay available");
        var guarded = peer.Engine.Apply(Doc(Item("move-board", "board")), "pack");
        Assert(!guarded.Outcomes["move-board"].Applied && guarded.Outcomes["move-board"].Error.Contains("cleanup is unverified"), "The kept state includes the unverified-cleanup guard");
        peer.Unload(); peer.Enter("map", "graph");
        Assert(peer.LastReentry.Failures.Count == 0 && peer.LastReentry.Retained == 1, "The next load attempt checks the object again");
    }
    // The reconciliation step itself throws: the binding records it, releases that state and starts a new one for the map.
    static void AFailedReconciliationRestartsOnlyThisPeersWorld() {
        var scenes = new List<PeerScene>(); var fail = false;
        var binding = new LocalWorldBinding<PeerWorld>(() => { var s = World(out _); scenes.Add(s); return new PeerWorld { Scene = s, Engine = new PatchEngine(s) }; }, w => w.Dispose(),
            w => { if (fail) throw new InvalidOperationException("injected reconciliation fault"); w.Engine.Reenter(); });
        var other = new LocalPeer(() => World(out _));
        other.Enter("map", "graph");
        Assert(binding.Enter("map", "graph") == WorldEntry.Started && binding.ReentryError == null, "The world starts without a reconciliation error");
        var first = binding.State;
        fail = true; binding.EndAttempt();
        var entry = binding.Enter("map", "graph");
        Assert(binding.ReentryError != null && binding.ReentryError.Contains("Retained world reconciliation failed") && binding.ReentryError.Contains("injected reconciliation fault"), "The failure is recorded for local reporting: " + binding.ReentryError);
        Assert(entry == WorldEntry.Started && scenes.Count == 2 && scenes[0].Disposed && binding.State != null && !ReferenceEquals(binding.State, first) && binding.Error == null, "The failed state is released and a new state starts for the same map");
        Assert(binding.State.Engine.Apply(Doc(Item("move-board", "board")), "pack").Outcomes["move-board"].Applied, "The new state applies the document");
        Assert(binding.Enter("map", "graph") == WorldEntry.Repeated && binding.ReentryError != null, "Repeated callbacks in that load attempt keep the report");
        fail = false; binding.EndAttempt();
        Assert(binding.Enter("map", "graph") == WorldEntry.Reentered && binding.ReentryError == null && scenes.Count == 2, "The next load attempt reconciles normally and clears the report");
        Assert(other.Scene != null && !((PeerScene)other.Scene).Disposed && other.Enter("map", "graph") == WorldEntry.Repeated, "Another peer's world is not affected");
    }
    static void FailuresAreRetriedInANewLoadAttemptOnly() {
        var scene = World(out var board);
        var peer = new LocalPeer(() => scene);
        var json = Doc(Item("move-board", "board") + "," + Item("later", "later"));
        peer.Enter("map", "graph");
        Assert(!peer.Engine.Apply(json, "pack").Outcomes["later"].Applied, "The missing target fails in the first load attempt");
        scene.Objects["later"] = new List<object> { new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } } };
        peer.Enter("map", "graph");
        Assert(peer.Engine.Apply(json, "pack").Outcomes["later"].Error.Contains("already failed"), "A repeated callback in the same load attempt does not retry the failure");
        peer.Unload(); peer.Enter("map", "graph");
        var again = peer.Engine.Apply(json, "pack");
        Assert(again.Outcomes["later"].Applied && again.Outcomes["move-board"].Applied && board.X == 112 && board.Position[1] == 2, "A new load attempt retries the failure and reapplies the absolute placement without accumulating an offset");
    }
}
