// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Detection additions: an added circuit with owned spans for explicitly selected extra tracks at an existing crossing.
static partial class CoverageTests {
    // The extra track of the fixture: approach A 308-550, island 292-308, approach B 292-50 on one segment.
    static string Addition(string id, string segment = "Snew") => "\"" + id + "\":{\"approachA\":" + Range(segment, 308, 550) + ",\"island\":" + Range(segment, 292, 308) + ",\"approachB\":" + Range(segment, 292, 50) + "}";
    static CircuitCoverage Extra(string segment = "Snew") => CircuitCoverage.Added(R(segment, 308, 550), R(segment, 292, 308), R(segment, 292, 50));
    static bool DetectsBothWays(Fixture f, FakeCrossing crossing, string segment) => new[] { 500.0, 300, 100 }.All(d => f.World.Detects(crossing, segment, d));
    static bool DetectsNowhere(Fixture f, FakeCrossing crossing, string segment) => !new[] { 500.0, 300, 100 }.Any(d => f.World.Detects(crossing, segment, d));
    static int OwnedAnywhere(Fixture f) => f.World.Graph.Values.Count(s => s.Owned);
    // The fixture crossing evaluates exactly its two native circuits on the map's own detection spans.
    static bool Untouched(Fixture f) => f.Crossing.Circuits.Count == 2 && ReferenceEquals(f.Crossing.Circuits[0], f.Rerouted) && ReferenceEquals(f.Crossing.Circuits[1], f.Retained)
        && f.Spans(f.Rerouted) == "Pcyi,Pipr,Pws9" && f.Spans(f.Retained) == "Pdlh,P6id,P1db";
    public static void RunAdditions() {
        ScenePatchFileAddsDetectionForExplicitTracks();
        InvalidAdditionsChangeNothing();
        UnresolvedTracksAndMapMismatchChangeNothing();
        AdditionIdentityIsScopedToItsScenePatchFile();
        AdditionDependenciesResolveRegardlessOfOrder();
        RepetitionReentryAndFreshWorldKeepOneOwnedSet();
        AdditionFailureRestoresAndPreservesIndependentWork();
        IncompleteAdditionCleanupGuardsRetry();
        EditingPreviewAddUndoRedoAndDelete();
        EditingKeepsDetectionAndRestoresAfterFailure();
        SaveAndReloadReproduceAdditionOnce();
        CoverageWritesLeaveNoEmptyObject();
        Console.WriteLine("PASS: detection additions with scoped identity, explicit tracks and area, convergence, deletion, refusal, restoration and scene patch editing");
    }
    static void ScenePatchFileAddsDetectionForExplicitTracks() {
        var f = new Fixture();
        f.World.Segments["Snear"] = new List<double> { 600 };
        Assert(DetectsNowhere(f, f.Crossing, "Snew"), "Before the addition the extra track does not trigger the crossing");
        var result = new PatchEngine(f.Scene).Apply(Doc(Add("add", Addition("siding"))));
        Assert(result.Error == null && result.Outcomes["add"].Applied, "An addition for explicit tracks applies: " + result.Error + result.Outcomes["add"].Error);
        Assert(f.Crossing.Circuits.Count == 3 && ReferenceEquals(f.Crossing.Circuits[0], f.Rerouted) && ReferenceEquals(f.Crossing.Circuits[1], f.Retained), "The controller evaluates its native circuits first and the added set after them");
        var added = f.Crossing.Circuits[2];
        Assert(OwnedAnywhere(f) == 3 && added.Spans.All(s => s.Owned && f.World.Registered(s) && f.World.Covers(s)), "The added set holds three owned spans registered in the graph");
        Assert(added.Spans.All(s => s.Id.StartsWith("CrankyAnt.ScenePatcher.added.siding.", StringComparison.Ordinal)), "Owned span IDs carry the addition's identity: " + f.Spans(added));
        Assert(DetectsBothWays(f, f.Crossing, "Snew"), "The selected extra track triggers the crossing at both approaches and the island");
        Assert(DetectsBothWays(f, f.Crossing, "Sold") && f.RetainedDetectsBothWays && f.Spans(f.Rerouted) == "Pcyi,Pipr,Pws9" && f.Spans(f.Retained) == "Pdlh,P6id,P1db", "The native circuits keep their spans and their function");
        Assert(DetectsNowhere(f, f.Crossing, "Snear"), "A track that was not selected gets no detection");
        var observed = f.Scene.Read(f.Crossing).Coverage;
        var key = DetectionAddition.Key("", "siding");
        Assert(observed.Circuits.Count == 3 && observed["Pipr"].Kind == CircuitKind.Native && observed["P6id"].Kind == CircuitKind.Native, "The observed coverage keeps the native circuits distinct from the addition");
        Assert(observed[key] != null && observed[key].Kind == CircuitKind.Added && observed[key].Ranges[0].To.Distance == 550 && observed[key].Ranges[1].From.Segment == "Snew", "The observed coverage reports the addition with its tracks and area");
        Assert(f.Other.Circuits.Count == 1 && f.Scene.Read(f.Other).Coverage.Circuits.Count == 1, "Another crossing gets no addition");
        Assert(f.Crossing.Position.SequenceEqual(new double[] { 5, 0, 7 }), "An addition never moves the crossing");
    }
    static void InvalidAdditionsChangeNothing() {
        var valid = Addition("siding");
        foreach (var invalid in new[] {
                "\"siding\":{\"detach\":true}", "\"siding\":{}", "\"siding\":7",
                valid.Replace(",\"approachB\":" + Range("Snew", 292, 50), ""),
                valid.Replace("\"island\":" + Range("Snew", 292, 308), "\"island\":{\"from\":" + Loc("Snew", 292) + "}") }) {
            var f = new Fixture();
            var refused = new PatchEngine(f.Scene).Apply(Doc(Add("bad", invalid)));
            Assert(!refused.Outcomes["bad"].Applied && f.World.Mutations == 0 && f.Native, "A detached, empty, non-object or partial addition is refused: " + invalid + " -> " + refused.Outcomes["bad"].Error);
        }
        var g = new Fixture();
        var future = new PatchEngine(g.Scene).Apply(Doc(Add("future", valid.Replace("\"siding\":{", "\"siding\":{\"bell\":true,"))));
        Assert(!future.Outcomes["future"].Applied && future.Outcomes["future"].Error.StartsWith("Unsupported", StringComparison.Ordinal) && g.Native, "Unknown addition content is preserved as unsupported, not executed");
        // An addition takes no placement or activity of the crossing with it.
        g = new Fixture();
        var moved = new PatchEngine(g.Scene).Apply(Doc(Add("moved", valid, extra: ",\"active\":false")));
        Assert(!moved.Outcomes["moved"].Applied && g.World.Mutations == 0 && g.Native, "Activity of the crossing is refused beside an addition");
    }
    static void UnresolvedTracksAndMapMismatchChangeNothing() {
        var independent = Add("other", Addition("spur", "Sspur"), OtherPath);
        foreach (var (circuits, reason) in new[] {
                (Addition("siding", "Smissing"), "missing"), (Addition("siding", "Stwin"), "ambiguous"),
                (Addition("siding").Replace("550", "650"), "outside"),
                (Addition("siding").Replace(Range("Snew", 292, 308), Range("Snew", 300, 300)), "no length") }) {
            var f = new Fixture();
            var result = new PatchEngine(f.Scene).Apply(Doc(Add("bad", circuits) + "," + independent));
            Assert(!result.Outcomes["bad"].Applied && result.Outcomes["bad"].Error.Contains(reason) && result.Outcomes["bad"].Error.Contains("Added detection siding"), "A missing, ambiguous or out-of-range track is refused (" + reason + "): " + result.Outcomes["bad"].Error);
            Assert(Untouched(f) && OwnedAnywhere(f) == 3, "The refused crossing gets no addition (" + reason + ")");
            Assert(result.Outcomes["other"].Applied && f.Other.Circuits.Count == 2, "An independent addition at another crossing still applies (" + reason + "): " + result.Outcomes["other"].Error);
        }
        var m = new Fixture();
        var mismatch = new PatchEngine(m.Scene).Apply(Doc(Add("add", Addition("siding")), "other-map"));
        Assert(mismatch.Error != null && mismatch.Outcomes.Count == 0 && m.World.Mutations == 0 && m.Native, "A scene patch file for another map adds nothing");
        var u = new Fixture();
        var unknown = new PatchEngine(u.Scene).Apply(Doc(Add("add", Addition("siding"), "World/Crossings/Gone")));
        Assert(!unknown.Outcomes["add"].Applied && unknown.Outcomes["add"].Error.Contains("resolve uniquely") && u.World.Mutations == 0, "An addition for a missing crossing is refused");
        u.World.Objects[CrossingPath].Add(u.Other);
        var ambiguous = new PatchEngine(u.Scene).Apply(Doc(Add("add", Addition("siding"))));
        Assert(!ambiguous.Outcomes["add"].Applied && ambiguous.Outcomes["add"].Error.Contains("resolve uniquely") && u.World.Mutations == 0, "An addition for an ambiguous crossing is refused");
    }
    static void AdditionIdentityIsScopedToItsScenePatchFile() {
        // One scene patch file uses each addition ID once: the ID names one addition of its mod, wherever the entries stand.
        foreach (var items in new[] { Add("a", Addition("siding")) + "," + Add("b", Addition("siding", "Sspur"), OtherPath) + "," + Add("c", Addition("spur", "Sspur"), OtherPath, "[]"),
                Add("c", Addition("spur", "Sspur"), OtherPath) + "," + Add("b", Addition("siding", "Sspur"), OtherPath) + "," + Add("a", Addition("siding")) }) {
            var f = new Fixture();
            var result = new PatchEngine(f.Scene).Apply(Doc(items));
            Assert(!result.Outcomes["a"].Applied && !result.Outcomes["b"].Applied && result.Outcomes["a"].Error.Contains("more than one modification"), "An addition ID used by two modifications fails both, in either order: " + result.Outcomes["a"].Error);
            Assert(Untouched(f) && OwnedAnywhere(f) == 3 && result.Outcomes["c"].Applied && f.Other.Circuits.Count == 2, "The modification with its own ID still applies: " + result.Outcomes["c"].Error);
        }
        // Two supplying mods may choose the same ID; their additions stay apart.
        var g = new Fixture();
        var engine = new PatchEngine(g.Scene);
        var first = engine.Apply(Doc(Add("add", Addition("siding"))), "pack/a");
        var second = engine.Apply(Doc(Add("add", Addition("siding", "Sspur"))), "pack/b");
        Assert(first.Outcomes["add"].Applied && second.Outcomes["add"].Applied && g.Crossing.Circuits.Count == 4 && OwnedAnywhere(g) == 6, "Additions of two mods with the same ID coexist: " + second.Outcomes["add"].Error);
        Assert(DetectsBothWays(g, g.Crossing, "Snew") && g.Crossing.Circuits[2].Spans.All(s => s.Id.StartsWith("CrankyAnt.ScenePatcher.added.pack/a.siding.", StringComparison.Ordinal))
            && g.Crossing.Circuits[3].Spans.All(s => s.Id.StartsWith("CrankyAnt.ScenePatcher.added.pack/b.siding.", StringComparison.Ordinal)), "Owned span IDs carry the supplying mod and the addition ID: " + g.Spans(g.Crossing.Circuits[2]));
        var observed = g.Scene.Read(g.Crossing).Coverage;
        Assert(observed[DetectionAddition.Key("pack/a", "siding")].Ranges[0].From.Segment == "Snew" && observed[DetectionAddition.Key("pack/b", "siding")].Ranges[0].From.Segment == "Sspur", "Each addition is observed under its own mod");
        engine.Apply(Doc(Add("add", Addition("siding"))), "pack/a");
        Assert(g.Crossing.Circuits.Count == 4 && g.World.Created == 6, "Repeating one mod's scene patch file leaves the other's addition alone");
        // The order of the added circuits follows their identity, never the order they were written or applied in.
        var h = new Fixture();
        new PatchEngine(h.Scene).Apply(Doc(Add("add", Addition("b", "Sspur") + "," + Addition("a"))));
        var i = new Fixture();
        new PatchEngine(i.Scene).Apply(Doc(Add("add", Addition("a") + "," + Addition("b", "Sspur"))));
        Assert(h.Crossing.Circuits.Count == 4 && h.Spans(h.Crossing.Circuits[2]) == i.Spans(i.Crossing.Circuits[2]) && h.Spans(h.Crossing.Circuits[2]).Contains("added.a."), "Added circuits have one order regardless of their textual order: " + h.Spans(h.Crossing.Circuits[2]));
    }
    static void AdditionDependenciesResolveRegardlessOfOrder() {
        var f = new Fixture();
        var result = new PatchEngine(f.Scene).Apply(Doc(Add("dependent", Addition("spur", "Sspur"), OtherPath, "[\"base\"]") + "," + Add("base", Addition("siding"))));
        Assert(result.Outcomes["dependent"].Applied && result.Outcomes["base"].Applied && f.Crossing.Circuits.Count == 3 && f.Other.Circuits.Count == 2, "An addition listed before its prerequisite applies: " + result.Outcomes["dependent"].Error);
        // A dependent addition fails with its prerequisite; an independent one still applies.
        f = new Fixture();
        result = new PatchEngine(f.Scene).Apply(Doc(Add("dependent", Addition("spur", "Sspur"), OtherPath, "[\"base\"]") + "," + Add("base", Addition("siding", "Smissing"))));
        Assert(!result.Outcomes["base"].Applied && !result.Outcomes["dependent"].Applied && result.Outcomes["dependent"].Error.Contains("A required modification failed") && f.Other.Circuits.Count == 1 && OwnedAnywhere(f) == 0, "An addition whose prerequisite fails adds nothing: " + result.Outcomes["dependent"].Error);
        f = new Fixture();
        result = new PatchEngine(f.Scene).Apply(Doc(Add("dependent", Addition("spur", "Sspur"), OtherPath, "[\"absent\"]")));
        Assert(!result.Outcomes["dependent"].Applied && f.World.Mutations == 0, "An addition with an unknown prerequisite adds nothing");
    }
    static void RepetitionReentryAndFreshWorldKeepOneOwnedSet() {
        var scenePatchFile = Doc(Add("add", Addition("siding")));
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        engine.Apply(scenePatchFile);
        var set = f.Crossing.Circuits[2]; var spans = set.Spans.ToArray();
        var again = engine.Apply(scenePatchFile);
        Assert(again.Outcomes.Values.All(o => o.Applied) && f.Crossing.Circuits.Count == 3 && ReferenceEquals(f.Crossing.Circuits[2], set) && set.Spans.SequenceEqual(spans) && f.World.Created == 3 && OwnedAnywhere(f) == 3, "A repeated world-load callback reuses the added circuit and its spans");
        // A later lifecycle re-entry builds a new engine and adapter over the world that still holds the addition.
        var reentered = new CrossingCoverageScene(f.World, Capability.Unchecked);
        var reentry = new PatchEngine(reentered).Apply(scenePatchFile);
        Assert(reentry.Outcomes.Values.All(o => o.Applied) && f.Crossing.Circuits.Count == 3 && ReferenceEquals(f.Crossing.Circuits[2], set) && set.Spans.SequenceEqual(spans) && f.World.Created == 3 && OwnedAnywhere(f) == 3, "Re-entry reconciles the retained addition instead of adding another: " + reentry.Outcomes["add"].Error);
        var observed = reentered.Read(f.Crossing).Coverage;
        Assert(observed.Circuits.Count == 3 && observed["Pipr"].Kind == CircuitKind.Native && observed["P6id"].Kind == CircuitKind.Native && observed[DetectionAddition.Key("", "siding")].Kind == CircuitKind.Added, "Re-entry tells the retained addition from the native circuits");
        Assert(DetectsBothWays(f, f.Crossing, "Snew") && DetectsBothWays(f, f.Crossing, "Sold"), "The addition and the native circuit keep detecting after re-entry");
        // Other tracks for the same addition replace its spans and keep its circuit.
        var movedTracks = "\"siding\":{\"approachA\":" + Range("Sother", 350, 395) + ",\"island\":" + Range("Sother", 340, 350) + ",\"approachB\":" + Range("Sother", 340, 220) + "}";
        var moved = new PatchEngine(reentered).Apply(Doc(Add("add", movedTracks)));
        Assert(moved.Outcomes["add"].Applied && ReferenceEquals(f.Crossing.Circuits[2], set) && OwnedAnywhere(f) == 3 && spans.All(s => s.Destroyed && !f.World.Registered(s)) && DetectsNowhere(f, f.Crossing, "Snew"), "Changed tracks replace the owned spans without leaving the earlier ones: " + moved.Outcomes["add"].Error);
        // A fresh world reconstructs the detection addition once.
        var fresh = new Fixture();
        var rebuilt = new PatchEngine(fresh.Scene).Apply(scenePatchFile);
        Assert(rebuilt.Outcomes.Values.All(o => o.Applied) && fresh.Crossing.Circuits.Count == 3 && fresh.World.Created == 3 && DetectsBothWays(fresh, fresh.Crossing, "Snew"), "A fresh world reconstructs the addition without duplicates");
    }
    static void AdditionFailureRestoresAndPreservesIndependentWork() {
        foreach (var inject in new Action<FakeCrossingWorld>[] { w => w.FailSetCircuits = true, w => w.FailSetSpansOnce = true, w => w.FailCreateAfter = 2, w => w.SkipRegisterOnCreate = true, w => w.FailCreateCircuit = true }) {
            var f = new Fixture();
            var engine = new PatchEngine(f.Scene);
            var prior = engine.Apply(Doc(Add("other", Addition("spur", "Sspur"), OtherPath)));
            Assert(prior.Outcomes["other"].Applied, "The independent addition applies first");
            inject(f.World);
            var result = engine.Apply(Doc(Add("add", Addition("siding"), circuits: Detached("P6id"))));
            Assert(!result.Outcomes["add"].Applied && result.Outcomes["add"].Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal), "A failure during construction is restored: " + result.Outcomes["add"].Error);
            Assert(Untouched(f) && DetectsBothWays(f, f.Crossing, "Sold") && f.RetainedDetectsBothWays && DetectsNowhere(f, f.Crossing, "Snew"), "Restoration returns membership and native references and removes the partial circuit and spans");
            Assert(f.Other.Circuits.Count == 2 && OwnedAnywhere(f) == 3 && DetectsBothWays(f, f.Other, "Sspur"), "The earlier independent addition survives the failure");
        }
        // A failure while the crossing already holds an addition restores that addition, not the native state.
        var g = new Fixture();
        new PatchEngine(g.Scene).Apply(Doc(Add("add", Addition("siding"))));
        var set = g.Crossing.Circuits[2]; var owned = set.Spans.ToArray();
        g.World.FailSetCircuits = true;
        var failed = new PatchEngine(g.Scene).Apply(Doc(Add("more", Addition("second", "Sspur"))));
        Assert(!failed.Outcomes["more"].Applied && g.Crossing.Circuits.Count == 3 && ReferenceEquals(g.Crossing.Circuits[2], set) && set.Spans.SequenceEqual(owned) && OwnedAnywhere(g) == 3 && DetectsBothWays(g, g.Crossing, "Snew"), "Restoration returns the captured addition and removes the failed one: " + failed.Outcomes["more"].Error);
    }
    static void IncompleteAdditionCleanupGuardsRetry() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        f.World.FailSetSpansOnce = true; f.World.StickyDestroy = true;
        var result = engine.Apply(Doc(Add("add", Addition("siding")) + "," + Add("dependent", Addition("spur", "Sspur"), OtherPath, "[\"add\"]")));
        Assert(!result.Outcomes["add"].Applied && result.Outcomes["add"].Error.StartsWith("The change failed. Cleanup could not be verified. Reason:", StringComparison.Ordinal), "Owned spans that stay registered make cleanup unverified: " + result.Outcomes["add"].Error);
        Assert(f.Crossing.Circuits.Count == 2 && f.Spans(f.Rerouted) == "Pcyi,Pipr,Pws9" && DetectsNowhere(f, f.Crossing, "Snew"), "The partial circuit left the crossing although its spans remain");
        Assert(!result.Outcomes["dependent"].Applied && f.Other.Circuits.Count == 1, "The dependent addition is not attempted");
        Assert(f.Scene.Read(f.Crossing).Coverage.Residue.Count == 3, "The remaining owned spans are reported as residue");
        f.World.StickyDestroy = false; f.World.Mutations = 0;
        var retry = engine.Apply(Doc(Add("retry", Addition("siding"))));
        Assert(!retry.Outcomes["retry"].Applied && retry.Outcomes["retry"].Error.Contains("could not be verified") && f.World.Mutations == 0, "Retry at the same crossing is guarded until cleanup verifies");
        var independent = engine.Apply(Doc(Add("other", Addition("spur", "Sspur"), OtherPath)));
        Assert(independent.Outcomes["other"].Applied && f.Other.Circuits.Count == 2, "Another crossing still takes an addition");
    }
    static JObject Coverage(PatchEditingSession session, string id) => (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"][id]["relationships"]["crossingCoverage"];
    // The circuits and additions the modification sets, counted together.
    static int Entries(PatchEditingSession session, string id) => Coverage(session, id).Properties().Sum(p => ((JObject)p.Value).Count);
    static void EditingPreviewAddUndoRedoAndDelete() {
        var (f, session, mapEditor, store) = Session();
        var preview = session.PreviewAddition(Crossing, "siding", Extra());
        Assert(preview.Error == null && preview.Lines.Count == 3, "The preview reports every circuit and the addition: " + preview.Error);
        Assert(preview.Lines[0].StartsWith("Circuit Pipr: retained", StringComparison.Ordinal) && preview.Lines[1].StartsWith("Circuit P6id: retained", StringComparison.Ordinal), "The preview shows the native circuits retained: " + preview.Lines[0]);
        Assert(preview.Lines[2].StartsWith("Added detection siding: new", StringComparison.Ordinal) && preview.Lines[2].Contains("Snew end a 308 to 550 m") && preview.Lines[2].Contains("Snew end a 292 to 308 m"), "The preview shows the addition's tracks and area: " + preview.Lines[2]);
        Assert(f.World.Mutations == 0 && f.Native && session.ScenePatchFileText.Contains("\"modifications\": {}") && mapEditor.UndoGroups.Count == 0, "A preview changes nothing");
        Assert(session.PreviewAddition(Crossing, "siding", Extra("Smissing")).Error.Contains("missing") && session.PreviewAddition(Crossing, " ", Extra()).Error != null, "The preview refuses a missing track and an unnamed addition");
        // Only an explicit addition creates membership: a track without one stays undetected.
        Assert(!session.AddDetection(Crossing, "siding", null).Applied && !session.AddDetection(Crossing, "siding", Selected()).Applied && f.World.Mutations == 0 && mapEditor.UndoGroups.Count == 0, "An addition without explicitly selected tracks is refused");
        var add = session.AddDetection(Crossing, "siding", Extra());
        Assert(add.Applied && f.Crossing.Circuits.Count == 3 && DetectsBothWays(f, f.Crossing, "Snew") && DetectsBothWays(f, f.Crossing, "Sold") && f.RetainedDetectsBothWays, "A scene patch editing addition detects on the selected tracks and keeps the native circuits: " + add.Error);
        var only = (JProperty)JObject.Parse(session.ScenePatchFileText)["modifications"].Single();
        var item = (JObject)only.Value;
        var entry = (JObject)item["relationships"]["crossingCoverage"]["additions"]["siding"];
        Assert(only.Name.StartsWith("cover-", StringComparison.Ordinal) && item["id"] == null && item["relationships"]["crossingCoverage"]["circuits"] == null && (double)entry["approachA"]["to"]["distance"] == 550
            && (string)entry["island"]["from"]["segment"] == "Snew" && item["placement"] == null && item["active"] == null, "The scene patch file records the addition's ID, tracks and area: " + item);
        Assert(session.MinimumPatcherVersion == "0.5.0", "An addition requires the patcher version of crossing coverage");
        var id = only.Name;
        var set = f.Crossing.Circuits[2]; var spans = set.Spans.ToArray();
        // The same modification still corrects native circuits beside the addition.
        Assert(session.Cover(Crossing, "P6id", CircuitCoverage.Detached).Applied && f.Crossing.Circuits.Count == 2 && ReferenceEquals(f.Crossing.Circuits[1], set) && Entries(session, id) == 2, "A native circuit can be detached beside the addition");
        // An addition ID names one addition in the scene patch file.
        var groups = mapEditor.UndoGroups.Count;
        var twice = session.AddDetection(Other, "siding", Extra("Sspur"));
        Assert(!twice.Applied && twice.Error.Contains("already used") && f.Other.Circuits.Count == 1 && mapEditor.UndoGroups.Count == groups, "The same ID at another crossing is refused without a history entry: " + twice.Error);
        Assert(session.AddDetection(Other, "spur", Extra("Sspur")).Applied && f.Other.Circuits.Count == 2 && DetectsBothWays(f, f.Other, "Sspur"), "Another ID at another crossing applies");
        // Other tracks for the addition replace its spans and keep its circuit.
        var changed = session.AddDetection(Crossing, "siding", CircuitCoverage.Added(R("Sother", 350, 395), R("Sother", 340, 350), R("Sother", 340, 220)));
        Assert(changed.Applied && ReferenceEquals(f.Crossing.Circuits[1], set) && spans.All(s => s.Destroyed) && DetectsNowhere(f, f.Crossing, "Snew") && f.World.Detects(f.Crossing, "Sother", 345) && Entries(session, id) == 2, "Changed tracks replace the addition's spans: " + changed.Error);
        mapEditor.Undo(); Assert(DetectsBothWays(f, f.Crossing, "Snew") && f.Crossing.Circuits.Count == 2 && OwnedAnywhere(f) == 6, "Undo returns the addition's earlier tracks");
        // Deleting the addition removes its circuit and spans; the detached circuit and the other crossing keep their state.
        var deletePreview = session.PreviewAddition(Crossing, "siding", null);
        Assert(deletePreview.Error == null && deletePreview.Lines.Any(l => l.StartsWith("Added detection siding:", StringComparison.Ordinal) && l.Contains("deleted")), "The preview shows the deletion: " + deletePreview.Error);
        var deleted = session.DeleteDetection(Crossing, "siding");
        Assert(deleted.Applied && f.Crossing.Circuits.Count == 1 && ReferenceEquals(f.Crossing.Circuits[0], f.Rerouted) && DetectsNowhere(f, f.Crossing, "Snew") && OwnedAnywhere(f) == 3, "Deleting the addition removes its circuit membership and its registered spans: " + deleted.Error);
        Assert(f.Spans(f.Rerouted) == "Pcyi,Pipr,Pws9" && DetectsBothWays(f, f.Crossing, "Sold") && f.Scene.Read(f.Crossing).Coverage["P6id"].Kind == CircuitKind.Detached && f.Scene.Read(f.Crossing).Coverage.Residue.Count == 0, "Deletion keeps the retained and this mod's coverage of the native circuits");
        Assert(f.Other.Circuits.Count == 2 && DetectsBothWays(f, f.Other, "Sspur") && Entries(session, id) == 1 && Coverage(session, id)["circuits"]["P6id"] != null && Coverage(session, id)["additions"] == null, "Deletion keeps the unrelated addition and the rest of the modification");
        Assert(!session.DeleteDetection(Crossing, "siding").Applied && !session.DeleteDetection(Crossing, "Pipr").Applied, "Deleting an absent addition, or a native circuit as an addition, is refused");
        mapEditor.Undo(); Assert(f.Crossing.Circuits.Count == 2 && DetectsBothWays(f, f.Crossing, "Snew") && OwnedAnywhere(f) == 6 && Entries(session, id) == 2, "Undo of the deletion restores the addition once");
        mapEditor.Redo(); Assert(f.Crossing.Circuits.Count == 1 && DetectsNowhere(f, f.Crossing, "Snew") && OwnedAnywhere(f) == 3, "Redo deletes it again and leaves no owned span of it");
        mapEditor.Undo();
        // Back through the whole history: every owned addition is gone and the native state returns.
        while (mapEditor.CanUndo) mapEditor.Undo();
        Assert(f.Native && OwnedAnywhere(f) == 0 && f.Other.Circuits.Count == 1 && session.ScenePatchFileText.Contains("\"modifications\": {}"), "Undoing every edit leaves no owned addition behind");
        while (mapEditor.CanRedo) mapEditor.Redo();
        Assert(f.Crossing.Circuits.Count == 1 && DetectsNowhere(f, f.Crossing, "Snew") && f.Other.Circuits.Count == 2 && OwnedAnywhere(f) == 3, "Redoing every edit, the deletion included, leaves one owned addition");
        mapEditor.Undo(); Assert(f.Crossing.Circuits.Count == 2 && DetectsBothWays(f, f.Crossing, "Snew") && OwnedAnywhere(f) == 6, "Undo of the redone deletion rebuilds the addition once");
        // Removing the modification returns the crossing to its state without this mod and keeps the other crossing's addition.
        var removed = session.Remove(Crossing);
        Assert(removed.Applied && Untouched(f) && DetectsNowhere(f, f.Crossing, "Snew") && OwnedAnywhere(f) == 3 && f.Other.Circuits.Count == 2, "Removal leaves no owned addition at the crossing: " + removed.Error);
        mapEditor.Undo(); Assert(f.Crossing.Circuits.Count == 2 && DetectsBothWays(f, f.Crossing, "Snew") && OwnedAnywhere(f) == 6, "Undo of removal restores the effective detection");
    }
    static void EditingKeepsDetectionAndRestoresAfterFailure() {
        var (f, session, mapEditor, store) = Session();
        session.AddDetection(Crossing, "siding", Extra());
        // The addition counts as detection, so both native circuits may be detached; deleting it would then leave none.
        Assert(session.Cover(Crossing, "Pipr", CircuitCoverage.Detached).Applied && session.Cover(Crossing, "P6id", CircuitCoverage.Detached).Applied && f.Crossing.Circuits.Count == 1 && DetectsBothWays(f, f.Crossing, "Snew"), "An addition keeps the crossing detecting when its native circuits are detached");
        var groups = mapEditor.UndoGroups.Count; f.World.Mutations = 0;
        var last = session.DeleteDetection(Crossing, "siding");
        Assert(!last.Applied && last.Error.Contains("without detection") && f.World.Mutations == 0 && mapEditor.UndoGroups.Count == groups && f.Crossing.Circuits.Count == 1, "Deleting the last detection is refused before mutation: " + last.Error);
        Assert(session.PreviewAddition(Crossing, "siding", null).Error.Contains("without detection"), "The preview names why the deletion is refused");
        // A failure during construction restores the scene and the scene patch file.
        (f, session, mapEditor, store) = Session();
        f.World.FailSetSpansOnce = true;
        var failed = session.AddDetection(Crossing, "siding", Extra());
        Assert(!failed.Applied && failed.Error.StartsWith("The edit failed. The object is back as it was. Reason:", StringComparison.Ordinal) && f.Native && OwnedAnywhere(f) == 0 && session.ScenePatchFileText.Contains("\"modifications\": {}") && mapEditor.UndoGroups.Count == 0, "A failed scene patch editing addition restores coverage and the scene patch file: " + failed.Error);
        f.World.FailSetSpansOnce = true; f.World.StickyDestroy = true;
        failed = session.AddDetection(Crossing, "siding", Extra());
        Assert(!failed.Applied && failed.Error.Contains("could not be verified") && OwnedAnywhere(f) == 3, "Incomplete cleanup of an addition is reported: " + failed.Error);
        f.World.StickyDestroy = false;
        Assert(!session.AddDetection(Crossing, "siding", Extra()).Applied, "A further edit is guarded until a recheck");
        var recheck = session.Recheck(Crossing);
        Assert(recheck.Applied && f.Native && OwnedAnywhere(f) == 0, "Recheck removes the remaining owned spans and clears the guard: " + recheck.Error);
        Assert(session.AddDetection(Crossing, "siding", Extra()).Applied && OwnedAnywhere(f) == 3 && f.Crossing.Circuits.Count == 3, "After recheck the addition applies once");
    }
    // Scene patch editing removes circuits or additions with its last entry and crossingCoverage with both; it never writes one of them empty.
    static void CoverageWritesLeaveNoEmptyObject() {
        var (f, session, mapEditor, store) = Session();
        JObject Relationships() => (JObject)((JObject)JObject.Parse(session.ScenePatchFileText)["modifications"]).PropertyValues().Single()["relationships"];
        void NoEmptyObject(string step) {
            var text = session.ScenePatchFileText;
            Assert(!text.Contains("\"circuits\": {}") && !text.Contains("\"additions\": {}") && !text.Contains("\"crossingCoverage\": {}") && !text.Contains("\"relationships\": {}"), step + ": no empty coverage object is written:\n" + text);
        }
        Assert(session.AddDetection(Crossing, "siding", Extra()).Applied, "An addition is set");
        NoEmptyObject("first addition");
        Assert(Relationships()["crossingCoverage"]["additions"]["siding"] != null && Relationships()["crossingCoverage"]["circuits"] == null, "A coverage with only an addition holds no circuits object");
        Assert(session.Cover(Crossing, "P6id", CircuitCoverage.Detached).Applied && session.Cover(Crossing, "Pipr", Selected()).Applied, "Two circuits are set beside it");
        NoEmptyObject("circuits beside the addition");
        Assert(session.Cover(Crossing, "P6id", null).Applied && ((JObject)Relationships()["crossingCoverage"]["circuits"]).Count == 1, "Clearing one of two circuits keeps the circuits object");
        Assert(session.Cover(Crossing, "Pipr", null).Applied, "The last circuit is cleared");
        NoEmptyObject("last circuit cleared");
        Assert(Relationships()["crossingCoverage"]["circuits"] == null && ((JObject)Relationships()["crossingCoverage"]).Count == 1 && Relationships()["crossingCoverage"]["additions"]["siding"] != null, "circuits leaves with its last entry; additions stays");
        Assert(session.Cover(Crossing, "P6id", CircuitCoverage.Detached).Applied && session.DeleteDetection(Crossing, "siding").Applied, "A circuit is set and the last addition deleted");
        NoEmptyObject("last addition deleted");
        Assert(Relationships()["crossingCoverage"]["additions"] == null && Relationships()["crossingCoverage"]["circuits"]["P6id"] != null, "additions leaves with its last entry; circuits stays");
        Assert(session.Cover(Crossing, "P6id", null).Applied && session.ScenePatchFileText.Contains("\"modifications\": {}"), "With both gone crossingCoverage leaves, and with it the modification that set nothing else");
        Assert(mapEditor.Save() == null && store.Patch == null, "Nothing is left to write");
        // Saved text: a coverage that ends with only circuits is written without an additions object.
        Assert(session.AddDetection(Crossing, "siding", Extra()).Applied && session.Cover(Crossing, "P6id", CircuitCoverage.Detached).Applied && session.DeleteDetection(Crossing, "siding").Applied && mapEditor.Save() == null, "A coverage is saved after its addition was deleted");
        Assert(!store.Patch.Contains("additions") && !store.Patch.Contains("{}") && JObject.Parse(store.Patch)["modifications"].Single().First["relationships"]["crossingCoverage"]["circuits"]["P6id"] != null, "The saved scene patch file holds the circuits object alone:\n" + store.Patch);
    }
    static void SaveAndReloadReproduceAdditionOnce() {
        var (f, session, mapEditor, store) = Session();
        session.AddDetection(Crossing, "siding", Extra());
        session.Cover(Crossing, "P6id", CircuitCoverage.Detached);
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0") && JObject.Parse(store.Patch)["modifications"].First.First["relationships"]["crossingCoverage"]["additions"]["siding"] != null, "Save writes the addition and the dependency: " + store.Info);
        // The world load applies the saved scene patch file under its mod's key; the scene patch editing session then edits the same addition.
        var fresh = new Fixture();
        var engine = new PatchEngine(fresh.Scene);
        var reload = engine.Apply(store.Patch, "author/pack");
        Assert(reload.Outcomes.Values.All(o => o.Applied) && fresh.Crossing.Circuits.Count == 2 && DetectsBothWays(fresh, fresh.Crossing, "Snew") && !fresh.RetainedDetectsBothWays, "A reloaded world reproduces the saved detection");
        engine.Apply(store.Patch, "author/pack");
        Assert(OwnedAnywhere(fresh) == 3 && fresh.World.Created == 3 && fresh.Crossing.Circuits.Count == 2, "A repeated world-load callback leaves exactly one owned addition");
        var reopenedMapEditor = new NativeMapEditor();
        var reopened = new PatchEditingSession(engine, reopenedMapEditor, new Store { Patch = store.Patch }, scenePatchFileKey: "author/pack"); reopenedMapEditor.Session = reopened;
        var set = fresh.Crossing.Circuits[1];
        Assert(reopened.Blocked == null && reopened.Cover(Crossing, "P6id", null).Applied && fresh.Crossing.Circuits.Count == 3 && ReferenceEquals(fresh.Crossing.Circuits[2], set) && fresh.World.Created == 3, "The reopened session edits beside the loaded addition without rebuilding it");
        Assert(reopened.DeleteDetection(Crossing, "siding").Applied && fresh.Native && OwnedAnywhere(fresh) == 0, "The reopened session deletes the loaded addition and leaves nothing owned");
    }
}
