// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
#if SCENE_PATCHER_FAULTS
using System;

namespace RailroaderScenePatcher.Core {
    // Test-only fault injection. It exists only in a build with -p:SceneFaults=true, which defines SCENE_PATCHER_FAULTS;
    // the normal build has no type, no hook and no way to arm a failure. One armed failure trips at the next mutation that
    // reaches its point, whether that mutation is an edit, an Undo or Redo step or a world-load application, and then
    // disarms itself.
    public static class FaultInjection {
        // The points a failure can be armed at, by the names the test mod passes.
        public const string AfterMutation = "after-mutation", DuringRestoration = "during-restoration", BetweenAdditionSteps = "between-addition-steps";
        static readonly object gate = new object();
        static string armed;
        // Set when the armed failure tripped and the restoration that follows must fail as well.
        static bool failRestoration;
        // The armed point, or null.
        public static string Armed { get { lock (gate) return armed; } }
        // What the last tripped failure did, for the test mod's status line, or null.
        public static string LastTrip { get; private set; }
        // Arms one failure:
        // after-mutation: the next mutation throws after it changed the object; the restoration runs normally.
        // during-restoration: the next mutation throws after it changed the object, and its restoration throws before it
        //   writes anything, so the object keeps the failed attempt's state.
        // between-addition-steps: the next crossing-coverage write that builds owned spans for a new detection addition
        //   throws after those spans exist and before their circuit set is created; the write's own cleanup and the
        //   restoration do not run, as if the attempt had been interrupted.
        public static void Arm(string point) {
            if (point != AfterMutation && point != DuringRestoration && point != BetweenAdditionSteps) throw new ArgumentException("Unknown fault point: " + point);
            lock (gate) { armed = point; failRestoration = false; }
        }
        public static void Disarm() { lock (gate) { armed = null; failRestoration = false; } }
        internal static void TripAfterMutation() {
            lock (gate) {
                if (armed != AfterMutation && armed != DuringRestoration) return;
                failRestoration = armed == DuringRestoration;
                LastTrip = "Tripped " + armed + " at " + DateTime.UtcNow.ToString("O");
                armed = null;
            }
            throw new InjectedFault("Injected fault after mutation", false);
        }
        internal static void TripBetweenAdditionSteps() {
            lock (gate) {
                if (armed != BetweenAdditionSteps) return;
                failRestoration = true;
                LastTrip = "Tripped " + armed + " at " + DateTime.UtcNow.ToString("O");
                armed = null;
            }
            throw new InjectedFault("Injected interruption between the steps of a detection addition", true);
        }
        // Called just before a restoration writes. Throws once after a fault that requires the restoration to fail.
        internal static void TripRestoration() {
            lock (gate) {
                if (!failRestoration) return;
                failRestoration = false;
            }
            throw new InjectedFault("Injected fault during restoration", false);
        }
        // Ends the attempt an injected failure tripped in: a restoration the failure required but that never ran, such as
        // after a failure inside Recheck, which restores nothing, must not fail a later, unrelated restoration.
        internal static void EndAttempt() { lock (gate) failRestoration = false; }
        // An injected interruption skips the cleanup inside the write that it interrupted.
        internal static bool IsInterruption(Exception e) => e is InjectedFault fault && fault.Interruption;
        sealed class InjectedFault : Exception {
            internal InjectedFault(string message, bool interruption) : base(message) { Interruption = interruption; }
            internal bool Interruption { get; }
        }
    }
}
#endif
