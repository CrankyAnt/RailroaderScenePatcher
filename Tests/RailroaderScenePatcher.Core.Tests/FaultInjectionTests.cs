// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// The test-only fault injection route. A normal build has no switch; a build with -p:SceneFaults=true arms each point
// once, and the failure it produces takes the same failure handling as a real one.
static class FaultInjectionTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run() {
#if SCENE_PATCHER_FAULTS
        AfterMutationIsRestoredOnce();
        DuringRestorationLeavesAnIncompleteCleanup();
        CoverageTests.RunInjectedInterruption();
        UnreadableGroupIsSkippedOnce();
        Console.WriteLine("PASS: fault build arms each injected failure once and handles it as a real failure, and logs an unreadable group once");
#else
        Assert(typeof(PatchEngine).Assembly.GetType("RailroaderScenePatcher.Core.FaultInjection") == null, "The normal build has no fault injection switch");
        Assert(new GroupSkips(_ => { }).Read(() => "BushnellWhittier-MapManager/World/Large Scenery/Bryson/Bryson Coal Dealer", () => "read") == "read", "The normal build reads every group root");
        Console.WriteLine("PASS: the normal build has no fault injection switch");
#endif
    }
#if SCENE_PATCHER_FAULTS
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + items + "}}";
    static string Item(string id, string path, double x) => "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + x + ",2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
    static Box NewBox(double x) => new Box { Position = new[] { x, 2.0, 3.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
    static void AfterMutationIsRestoredOnce() {
        var scene = new Scene { Origin = 0 }; var p = NewBox(4); var q = NewBox(5);
        scene.Objects["p"] = new List<object> { p }; scene.Objects["q"] = new List<object> { q };
        var engine = new PatchEngine(scene);
        FaultInjection.Arm(FaultInjection.AfterMutation);
        var result = engine.Apply(Doc(Item("move", "p", 90) + "," + Item("next", "q", 50)), "pack");
        Assert(result.Outcomes["move"].Failure == FailureKind.Restored && p.X == 4 && result.Outcomes["move"].Error.Contains("Injected fault after mutation"), "The armed failure trips at the first mutation and is restored: " + result.Outcomes["move"].Error);
        Assert(result.Outcomes["next"].Applied && q.X == 50 && FaultInjection.Armed == null && FaultInjection.LastTrip.StartsWith("Tripped after-mutation", StringComparison.Ordinal), "The failure trips once and disarms");
        engine.Reenter();
        Assert(engine.Apply(Doc(Item("move", "p", 90)), "pack").Outcomes["move"].Applied && p.X == 90, "The next load attempt applies normally");
    }
    static void DuringRestorationLeavesAnIncompleteCleanup() {
        var scene = new Scene { Origin = 0 }; var p = NewBox(4);
        scene.Objects["p"] = new List<object> { p };
        var session = new PatchEditingSession(new PatchEngine(scene), new NativeMapEditor(), new Store());
        FaultInjection.Arm(FaultInjection.DuringRestoration);
        var failed = session.Edit(new TargetIdentity("s", "p", "c"), new Placement(new[] { 80.0, 2, 3 }, new[] { 0.0, 0, 0, 1 }), null);
        Assert(failed.Failure == FailureKind.IncompleteCleanup && p.X == 80 && failed.Remaining.Any(l => l.StartsWith("Position: now 80, 2, 3, before 4, 2, 3", StringComparison.Ordinal)), "A failed restoration leaves the attempt's state and names it: " + String.Join(" | ", failed.Remaining));
        Assert(FaultInjection.Armed == null, "The failure disarmed");
        var recheck = session.Recheck(new TargetIdentity("s", "p", "c"));
        Assert(recheck.Applied && p.X == 4 && !session.CleanupUnverified(new TargetIdentity("s", "p", "c")), "Recheck returns the object to the scene patch file and ends the guard: " + recheck.Error);
    }
    // The unreadable-group point makes world marking read one chosen group root as unreadable, each time it decides
    // its groups, until it is disarmed; the skipped group logs one line per root and reason.
    static void UnreadableGroupIsSkippedOnce() {
        const string root = "BushnellWhittier-MapManager/World/Large Scenery/Bryson/Bryson Coal Dealer", other = "s/World/Other";
        var lines = new List<string>();
        var skips = new GroupSkips(lines.Add);
        var computed = 0;
        Assert(skips.Read(() => { computed++; return root; }, () => "read") == "read" && computed == 0, "Before arming, the root of a group that is read is not computed");
        FaultInjection.ArmUnreadableGroup(root);
        Assert(FaultInjection.UnreadableGroup == root && FaultInjection.Armed == null, "The point is armed for its root beside the mutation points");
        // Arming a mutation point and tripping it leave the unreadable-group point armed.
        var scene = new Scene { Origin = 0 }; var p = NewBox(4);
        scene.Objects["p"] = new List<object> { p };
        FaultInjection.Arm(FaultInjection.AfterMutation);
        Assert(FaultInjection.UnreadableGroup == root, "Arm(point) leaves the unreadable-group point armed");
        Assert(new PatchEngine(scene).Apply(Doc(Item("move", "p", 90)), "pack").Outcomes["move"].Failure == FailureKind.Restored && FaultInjection.Armed == null, "The mutation point trips");
        Assert(FaultInjection.UnreadableGroup == root, "Tripping a mutation point leaves the unreadable-group point armed");
        for (var pass = 0; pass < 3; pass++) {
            Assert(skips.Read(() => root, () => "read") == null, "The armed root reads as unreadable on every pass");
            Assert(skips.Read(() => other, () => "read") == "read", "Another root reads normally");
            Assert(skips.Read(() => root + "/Part", () => "read") == "read", "Only the exact root reads as unreadable");
            Assert(skips.Read(() => " " + root, () => "read") == "read", "A root with a leading space is another root");
        }
        skips.Skip(root, "none of its objects has a usable position");
        skips.Skip(root, "none of its objects has a usable position");
        Assert(lines.SequenceEqual(new[] { "Group skipped: " + root + ": its objects could not be read: Injected unreadable group", "Group skipped: " + root + ": none of its objects has a usable position" }),
            "One line per group root and reason: " + String.Join(" | ", lines));
        Assert(FaultInjection.LastTrip.StartsWith("Tripped unreadable-group for " + root + " at ", StringComparison.Ordinal), "Last names the tripped root: " + FaultInjection.LastTrip);
        FaultInjection.Disarm();
        Assert(FaultInjection.UnreadableGroup == null && skips.Read(() => root, () => "read") == "read" && lines.Count == 2, "Disarm ends the point");
        foreach (var empty in new[] { null, "", " ", " \t " }) {
            var refused = false;
            try { FaultInjection.ArmUnreadableGroup(empty); } catch (ArgumentException) { refused = true; }
            Assert(refused && FaultInjection.UnreadableGroup == null, "The point needs a group root that is not only white space");
        }
        // The root is kept as typed: a scene object's name may begin or end with a space.
        const string spaced = "s/World/ Spaced root ";
        FaultInjection.ArmUnreadableGroup(spaced);
        Assert(FaultInjection.UnreadableGroup == spaced && skips.Read(() => spaced, () => "read") == null && skips.Read(() => spaced.Trim(), () => "read") == "read", "A root with surrounding spaces is armed as typed");
        FaultInjection.Disarm();
    }
#endif
}
