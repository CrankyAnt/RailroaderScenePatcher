// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Crossing coverage across load attempts over a retained world: owned spans and circuits are reconciled by their persistent
// identities, and only the patcher's own state of a reconstructed crossing is removed.
static partial class CoverageTests {
    public static void RunRetained() {
        RetainedCrossingReconcilesWithoutDuplicates();
        ReconstructedCrossingReleasesOnlyItsOwnedSpans();
        IncompleteReleaseIsReportedLocallyAndRetried();
        EarlyCheckKeepsACrossingWhoseSceneIsNotLoaded();
        DiscardedStateRestoresNativeCoverageWhileTheSceneStays();
        FreshStateRefusesPatchedCircuitsAsNative();
        Console.WriteLine("PASS: retained crossing coverage reconciliation, owned cleanup, isolated cleanup failure, early checks over an unloaded scene and discarded state over a remaining scene");
    }
    static string RetainedDocument => Doc(Cover("reroute", Tracks("Pipr")) + "," + Cover("add", Addition("siding", "Sspur")));
    static readonly string[] NativeSpanIds = { "Pcyi", "Pipr", "Pws9", "Pdlh", "P6id", "P1db", "Qa", "Qi", "Qb" };
    static bool NativeSpansRegistered(Fixture f) => NativeSpanIds.All(id => f.World.Graph.TryGetValue(id, out var s) && !s.Owned && !s.Destroyed);
    static bool NoSuffixedIds(Fixture f) => f.World.Graph.Keys.All(id => !id.EndsWith(".2", StringComparison.Ordinal));
    // The crossing as the game rebuilds it from map data: a new controller whose circuits refer to the native spans.
    static FakeCrossing Rebuild(Fixture f) {
        var crossing = new FakeCrossing();
        crossing.Circuits.Add(new FakeCircuit { Spans = f.OldSpans });
        crossing.Circuits.Add(new FakeCircuit { Spans = f.Retained.Spans });
        f.World.Objects[CrossingPath] = new List<object> { crossing };
        return crossing;
    }
    static void RetainedCrossingReconcilesWithoutDuplicates() {
        var f = new Fixture();
        var peer = new LocalPeer(() => f.Scene);
        peer.Enter("map", "graph");
        var first = peer.Engine.Apply(RetainedDocument, "pack");
        Assert(first.Outcomes.Values.All(o => o.Applied) && f.World.Created == 6 && OwnedAnywhere(f) == 6, "The reroute and the addition apply once: " + String.Join("; ", first.Outcomes.Values.Select(o => o.Error)));
        var circuits = f.Crossing.Circuits.ToArray();
        Assert(peer.Enter("map", "rebuilt-graph") == WorldEntry.Reentered && peer.LastReentry.Retained == 1 && peer.LastReentry.Released == 0, "The retained crossing keeps its state through re-entry");
        var again = peer.Engine.Apply(RetainedDocument, "pack");
        Assert(again.Outcomes.Values.All(o => o.Applied), "The retained world reconciles both modifications again: " + String.Join("; ", again.Outcomes.Values.Select(o => o.Error)));
        Assert(f.World.Created == 6 && OwnedAnywhere(f) == 6 && NoSuffixedIds(f) && f.Crossing.Circuits.SequenceEqual(circuits), "Reconciliation reuses the owned spans and circuits instead of adding others");
        Assert(f.NewRouteDetectsBothWays && !f.OldRouteDetects && f.RetainedDetectsBothWays && DetectsBothWays(f, f.Crossing, "Sspur") && NativeSpansRegistered(f), "Detection stays on the selected and added tracks, and the native spans stay registered");
    }
    static void ReconstructedCrossingReleasesOnlyItsOwnedSpans() {
        var f = new Fixture();
        var peer = new LocalPeer(() => f.Scene);
        peer.Enter("map", "graph");
        peer.Engine.Apply(RetainedDocument, "pack");
        var owned = f.World.Graph.Values.Where(s => s.Owned).ToArray();
        var otherCircuits = f.Other.Circuits.ToArray();
        var rebuilt = Rebuild(f);
        peer.Unload();
        Assert(peer.Enter("map", "graph") == WorldEntry.Reentered && peer.LastReentry.Released == 1 && peer.LastReentry.Failures.Count == 0, "Re-entry releases the reconstructed crossing");
        Assert(owned.All(s => s.Destroyed && !f.World.Registered(s)) && OwnedAnywhere(f) == 0, "The owned spans of the reconstructed crossing are removed");
        Assert(NativeSpansRegistered(f) && f.Other.Circuits.SequenceEqual(otherCircuits) && rebuilt.Circuits.Count == 2, "Native spans, the rebuilt crossing and the unrelated crossing stay as they are");
        var applied = peer.Engine.Apply(RetainedDocument, "pack");
        Assert(applied.Outcomes.Values.All(o => o.Applied) && OwnedAnywhere(f) == 6 && NoSuffixedIds(f) && rebuilt.Circuits.Count == 3, "The rebuilt crossing gets one owned set under the original span IDs: " + String.Join("; ", applied.Outcomes.Values.Select(o => o.Error)));
        Assert(DetectsBothWays(f, rebuilt, "Snew") && DetectsBothWays(f, rebuilt, "Sspur") && !new[] { 500.0, 300, 100 }.Any(d => f.World.Detects(rebuilt, "Sold", d)), "The rebuilt crossing detects on the selected and added tracks only");
    }
    static void IncompleteReleaseIsReportedLocallyAndRetried() {
        var f = new Fixture();
        var peer = new LocalPeer(() => f.Scene);
        peer.Enter("map", "graph");
        peer.Engine.Apply(RetainedDocument, "pack");
        var owned = f.World.Graph.Values.Where(s => s.Owned).ToArray();
        Rebuild(f);
        f.World.StickyDestroy = true;
        peer.Unload(); peer.Enter("map", "graph");
        var report = peer.LastReentry;
        Assert(report.Released == 0 && report.Failures.Count == 1 && report.Failures[0].Contains(CrossingPath) && report.Failures[0].Contains("remains"), "A release that leaves owned spans registered is reported for its crossing: " + String.Join("; ", report.Failures));
        Assert(peer.ReentryError == null && peer.Engine != null, "A local cleanup failure keeps the world and its patcher state available");
        var otherTracks = "{\"circuit\":\"Qi\",\"approachA\":" + Range("Sother", 1, 5) + ",\"island\":" + Range("Sother", 5, 6) + ",\"approachB\":" + Range("Sother", 6, 9) + "}";
        var independent = peer.Engine.Apply(Doc(Cover("other", otherTracks, OtherPath)), "pack");
        Assert(independent.Outcomes["other"].Applied, "Unrelated work keeps applying after the cleanup failure: " + independent.Outcomes["other"].Error);
        f.World.StickyDestroy = false;
        peer.Unload(); peer.Enter("map", "graph");
        Assert(peer.LastReentry.Released == 1 && peer.LastReentry.Failures.Count == 0 && owned.All(s => !f.World.Registered(s)), "The next load attempt retries the release and removes the remaining owned spans");
        Assert(peer.Engine.Apply(RetainedDocument, "pack").Outcomes.Values.All(o => o.Applied) && f.OwnedRegistered == 6 && NoSuffixedIds(f), "After the completed release the rebuilt crossing gets one owned set");
    }

    // A native check opens the load attempt while the crossing's scene is not yet reported as loaded. The crossing still
    // exists, so its state is kept rather than released: detection stays on the selected tracks and re-application succeeds.
    static void EarlyCheckKeepsACrossingWhoseSceneIsNotLoaded() {
        var f = new Fixture();
        var peer = new LocalPeer(() => f.Scene);
        peer.Enter("map", "graph");
        var reroute = Doc(Cover("reroute", Tracks("Pipr")));
        Assert(peer.Engine.Apply(RetainedDocument, "pack").Outcomes.Values.All(o => o.Applied), "The reroute and the addition apply");
        var owned = f.World.Graph.Values.Where(s => s.Owned).ToArray();
        peer.Unload();
        f.Crossing.SceneLoaded = false;
        peer.Prepare("map");
        Assert(peer.LastReentry.Released == 0 && peer.LastReentry.Retained == 1 && peer.LastReentry.Failures.Count == 0, "The early check keeps the crossing whose scene is not loaded: released " + peer.LastReentry.Released + ", failures " + String.Join("; ", peer.LastReentry.Failures));
        f.Crossing.SceneLoaded = true;
        Assert(peer.Enter("map", "graph") == WorldEntry.Reentered && peer.Reentries == 1, "The world-load callback joins the attempt the early check opened");
        Assert(owned.All(s => !s.Destroyed && f.World.Registered(s)) && f.NewRouteDetectsBothWays && !f.OldRouteDetects && DetectsBothWays(f, f.Crossing, "Sspur"), "Detection stays on the selected and added tracks");
        var again = peer.Engine.Apply(RetainedDocument, "pack");
        Assert(again.Outcomes.Values.All(o => o.Applied) && OwnedAnywhere(f) == 6 && NoSuffixedIds(f) && f.World.Created == 6, "Re-application reuses the owned set: " + String.Join("; ", again.Outcomes.Values.Select(o => o.Error)));
        // A crossing whose scene is loaded but which no longer stands under its identity is still released.
        var rebuilt = Rebuild(f);
        peer.Unload(); peer.Prepare("map");
        Assert(peer.LastReentry.Released == 1 && OwnedAnywhere(f) == 0, "A reconstructed crossing is released by the early check");
        Assert(peer.Engine.Apply(reroute, "pack").Outcomes["reroute"].Applied && DetectsBothWays(f, rebuilt, "Snew"), "The rebuilt crossing takes the reroute");
    }
    // Discarding the patcher state while the scene stays, as a mod reload or unload without a scene unload would, returns every crossing to its
    // native circuits and removes the owned spans, so the next state records the native coverage and applies once.
    static void DiscardedStateRestoresNativeCoverageWhileTheSceneStays() {
        var f = new Fixture();
        var peer = new LocalPeer(() => new CrossingCoverageScene(f.World, Capability.Unchecked));
        peer.Enter("map", "graph");
        var doc = Doc(Cover("reroute", Tracks("Pipr") + "," + Detached("P6id")) + "," + Cover("add", Addition("siding", "Sspur")));
        var first = peer.Engine.Apply(doc, "pack");
        Assert(first.Outcomes.Values.All(o => o.Applied), "The reroute, the detach and the addition apply: " + String.Join("; ", first.Outcomes.Values.Select(o => o.Error)));
        peer.Unload();
        peer.Prepare("another-map");
        Assert(OwnedAnywhere(f) == 0 && f.World.Graph.Values.All(s => !s.Owned) && f.Native, "Discarding the state restores the native circuits and removes the owned spans: " + f.Spans(f.Rerouted) + " owned " + OwnedAnywhere(f));
        Assert(f.OldRouteDetects && f.RetainedDetectsBothWays && NativeSpansRegistered(f) && f.Scene.Read(f.Other).Coverage["Qi"].Kind == CircuitKind.Native, "Native detection returns and the unrelated crossing stays native");
        Assert(peer.Enter("map", "graph") == WorldEntry.Started, "The map starts a new state");
        var again = peer.Engine.Apply(doc, "pack");
        Assert(again.Outcomes.Values.All(o => o.Applied), "The new state applies the same document: " + String.Join("; ", again.Outcomes.Values.Select(o => o.Error)));
        Assert(OwnedAnywhere(f) == 6 && NoSuffixedIds(f) && f.NewRouteDetectsBothWays && !f.OldRouteDetects && !f.RetainedDetectsBothWays && DetectsBothWays(f, f.Crossing, "Sspur"), "One owned set under the original span IDs carries the reroute, the detach and the addition");
        peer.Dispose();
        Assert(OwnedAnywhere(f) == 0 && f.Native, "Releasing the binding also restores the native coverage");
    }
    // A state that starts over a crossing an earlier state patched and did not restore refuses to take the patched circuits for
    // native ones, and changes nothing.
    static void FreshStateRefusesPatchedCircuitsAsNative() {
        var f = new Fixture();
        Assert(new PatchEngine(f.Scene).Apply(Doc(Cover("reroute", Tracks("Pipr")))).Outcomes["reroute"].Applied, "The reroute applies");
        var mutations = f.World.Mutations;
        var fresh = new CrossingCoverageScene(f.World, Capability.Unchecked);
        var refused = new PatchEngine(fresh).Apply(Doc(Cover("reroute", Tracks("Pipr"))));
        Assert(!refused.Outcomes["reroute"].Applied && refused.Outcomes["reroute"].Error.Contains("Reload before editing it") && f.World.Mutations == mutations, "The fresh state refuses the patched circuit: " + refused.Outcomes["reroute"].Error);
        Assert(f.NewRouteDetectsBothWays && f.OwnedRegistered == 3, "The patched coverage stays as it was");
    }
}
