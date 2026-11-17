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
        Console.WriteLine("PASS: fault build arms each injected failure once and handles it as a real failure");
#else
        Assert(typeof(PatchEngine).Assembly.GetType("RailroaderScenePatcher.Core.FaultInjection") == null, "The normal build has no fault injection switch");
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
#endif
}
