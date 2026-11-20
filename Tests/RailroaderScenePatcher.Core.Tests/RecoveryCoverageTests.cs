// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Interrupted construction of owned state: the retry reconciles the actual owned state with the persistent identities
// the scene patch file names before anything further is built.
static partial class CoverageTests {
    public static void RunRecovery() {
        InterruptedAdditionReconcilesBeforeConstruction();
        InterruptedWorldLoadAdditionNamesItsResidue();
    }
    static void InterruptedAdditionReconcilesBeforeConstruction() {
        var (f, session, mapEditor, store) = Session();
        Assert(session.AddDetection(Crossing, "siding", Extra()).Applied && OwnedAnywhere(f) == 3, "The first addition applies");
        var kept = f.Crossing.Circuits[2]; var keptSpans = kept.Spans.ToArray();
        // The construction of a second addition stops midway, and its spans cannot be retired.
        f.World.FailSetSpansOnce = true; f.World.StickyDestroy = true;
        // Through the panel's feedback route, as the Crossing coverage tab runs it. The observation lists the owned spans, as
        // the native inventory lists the crossing's spans, so leftover spans make the inspection stale.
        var panel = new Scene { Observations = () => new[] { new SceneObservation(f.Crossing, Crossing, "Crossing", true, new double[] { 5, 0, 7 }, new double[] { 0, 0, 0, 1 }, new string[0], new[] { "Owned spans " + OwnedAnywhere(f) }, new string[0], "Crossing coverage") } };
        var inventory = new SceneInventory(panel); inventory.Refresh();
        var feedback = new RailroaderScenePatcher.EditFeedback();
        Outcome failed = null;
        feedback.Run("Crossing coverage", RecoveryGuidance.DetectionAddition, null, inventory, inventory.Entries.Single(), _ => failed = session.AddDetection(Crossing, "spur", Extra("Sspur")), _ => null);
        Assert(feedback.Result.StartsWith("Detection addition failed; cleanup is incomplete. ", StringComparison.Ordinal) && feedback.Recovery.Guide == "adding-crossing-detection.md", "A failed addition from the coverage tab opens the detection guide: " + feedback.Result + " -> " + feedback.Recovery?.Guide);
        Assert(failed.Failure == FailureKind.IncompleteCleanup && OwnedAnywhere(f) == 6, "The interrupted addition leaves owned spans behind: " + failed.Error);
        Assert(failed.Remaining.Any(l => l.StartsWith("Owned spans still registered: ", StringComparison.Ordinal)), "The remaining owned spans are named: " + String.Join(" | ", failed.Remaining));
        var modifications = (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"];
        Assert(modifications.Count == 1 && ((JObject)modifications.Properties().Single().Value["relationships"]["crossingCoverage"]["additions"]).Properties().Select(p => p.Name).SequenceEqual(new[] { "siding" }), "The scene patch file keeps only the completed addition");
        f.World.StickyDestroy = false;
        var created = f.World.Created;
        Assert(session.AddDetection(Crossing, "spur", Extra("Sspur")).Failure == FailureKind.Blocked && f.World.Created == created, "Construction waits until the cleanup is verified");
        // The leftover spans changed the crossing's observation: an edit pinned to the old inspection would be stale.
        inventory.CheckFreshness();
        Assert(inventory.IsStale, "The leftover spans make the inspection stale");
        var inspected = inventory.Entries.Single();
        Outcome recheck = null;
        Assert(!feedback.Run("Modification", "Recheck", null, inventory, inspected, inspection => recheck = session.Recheck(Crossing)) && feedback.Result.Contains("stale") && recheck == null, "Pinned to the old inspection, Recheck would be refused as stale: " + feedback.Result);
        // The panel's Recheck finds and inspects the object again before it pins: its Refresh, then the same object. The
        // controlled coverage scene cannot check an inspection token itself, so the session runs without one here; the pin
        // is where the stale refusal came from.
        var presentation = new RailroaderScenePatcher.InventoryPresentationState();
        inventory.Refresh();
        inspected = presentation.CurrentSelection(inventory, inspected);
        Assert(feedback.Run("Modification", "Recheck", null, inventory, inspected, inspection => recheck = session.Recheck(Crossing)), "Recheck straight after the failure succeeds without a separate Refresh: " + feedback.Result);
        Assert(recheck.Applied && OwnedAnywhere(f) == 3 && f.Crossing.Circuits.Count == 3 && ReferenceEquals(f.Crossing.Circuits[2], kept) && kept.Spans.SequenceEqual(keptSpans), "Recheck retires the interrupted attempt's spans and keeps the addition the document names: " + recheck.Error);
        Assert(f.World.Created == created && DetectsBothWays(f, f.Crossing, "Snew"), "Recheck reuses the named addition instead of building it again");
        Assert(session.AddDetection(Crossing, "spur", Extra("Sspur")).Applied && OwnedAnywhere(f) == 6 && f.Crossing.Circuits.Count == 4, "After the recheck the retried addition is built once");
    }
#if SCENE_PATCHER_FAULTS
    // The injected interruption between the steps of an addition leaves its owned spans, and Recheck removes them.
    public static void RunInjectedInterruption() {
        var (f, session, mapEditor, store) = Session();
        FaultInjection.Arm(FaultInjection.BetweenAdditionSteps);
        var failed = session.AddDetection(Crossing, "siding", Extra());
        Assert(failed.Failure == FailureKind.IncompleteCleanup && OwnedAnywhere(f) == 3 && f.Crossing.Circuits.Count == 2, "The interrupted addition leaves its spans and no circuit: " + failed.Error);
        Assert(failed.Remaining.Any(l => l.StartsWith("Owned spans still registered: ", StringComparison.Ordinal)), "The leftover spans are named: " + String.Join(" | ", failed.Remaining));
        Assert(FaultInjection.Armed == null && session.ScenePatchFileText.Contains("\"modifications\": {}"), "The failure disarmed and the scene patch file holds no addition");
        var recheck = session.Recheck(Crossing);
        Assert(recheck.Applied && f.Native && OwnedAnywhere(f) == 0, "Recheck removes the interrupted attempt's spans: " + recheck.Error);
        Assert(session.AddDetection(Crossing, "siding", Extra()).Applied && OwnedAnywhere(f) == 3 && f.Crossing.Circuits.Count == 3, "The retried addition is built once");
        // The same interruption at world load.
        var g = new Fixture();
        FaultInjection.Arm(FaultInjection.BetweenAdditionSteps);
        var load = new PatchEngine(g.Scene).Apply(Doc(Add("add", Addition("siding")))).Outcomes["add"];
        Assert(load.Failure == FailureKind.IncompleteCleanup && OwnedAnywhere(g) == 3, "A world-load interruption leaves its spans and is guarded: " + load.Error);
        // An interruption inside Recheck, which restores nothing, ends with its attempt: a later, unrelated restoration runs.
        var (h, recheckSession, _, _) = Session(Doc(Add("add", Addition("siding"))));
        FaultInjection.Arm(FaultInjection.BetweenAdditionSteps);
        var interrupted = recheckSession.Recheck(Crossing);
        Assert(interrupted.Failure == FailureKind.IncompleteCleanup && OwnedAnywhere(h) == 3 && FaultInjection.Armed == null, "Recheck that builds an addition can be interrupted too: " + interrupted.Error);
        var other = new Scene { Origin = 0 }; var box = new Box { Position = new[] { 4.0, 2, 3 }, Rotation = new[] { 0.0, 0, 0, 1 } };
        other.Objects["p"] = new System.Collections.Generic.List<object> { box }; other.FailNextApplies = 1;
        var unrelated = new PatchEngine(other).Apply("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"s\",\"path\":\"p\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[90,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}", "pack").Outcomes["move"];
        Assert(unrelated.Failure == FailureKind.Restored && box.X == 4, "A later unrelated restoration is not failed by the earlier interruption: " + unrelated.Error);
        var retried = recheckSession.Recheck(Crossing);
        Assert(retried.Applied && OwnedAnywhere(h) == 3 && h.Crossing.Circuits.Count == 3, "The next Recheck reuses the spans and builds the addition once: " + retried.Error);
    }
#endif
    static void InterruptedWorldLoadAdditionNamesItsResidue() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        f.World.FailSetSpansOnce = true; f.World.StickyDestroy = true;
        var failed = engine.Apply(Doc(Add("add", Addition("siding")))).Outcomes["add"];
        Assert(failed.Failure == FailureKind.IncompleteCleanup && failed.Remaining.Any(l => l.StartsWith("Owned spans still registered: ", StringComparison.Ordinal)), "A world-load failure names the owned spans it left: " + String.Join(" | ", failed.Remaining));
    }
}
