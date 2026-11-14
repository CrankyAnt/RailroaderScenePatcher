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
        Console.WriteLine("PASS: retained crossing coverage reconciliation, owned cleanup and isolated cleanup failure");
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
}
