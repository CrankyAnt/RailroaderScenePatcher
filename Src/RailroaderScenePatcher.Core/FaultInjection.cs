// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
#if SCENE_PATCHER_FAULTS
using System;

namespace RailroaderScenePatcher.Core {
    // Test-only fault injection. It exists only in a build with -p:SceneFaults=true, which defines SCENE_PATCHER_FAULTS;
    // the normal build has no type, no hook and no way to arm a failure. One armed failure trips at the next mutation that
    // reaches its point, whether that mutation is an edit, an Undo or Redo step or a world-load application, and then
    // disarms itself. The unreadable-group and unreadable-parts points are no mutation failures: each stays armed for one
    // group root.
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
        public static void Disarm() { lock (gate) { armed = null; failRestoration = false; unreadableGroup = null; unreadableParts = null; } }
        // The group root that world marking reads as unreadable, as the Group skipped line names it
        // (scene name/path), or null.
        static string unreadableGroup;
        public static string UnreadableGroup { get { lock (gate) return unreadableGroup; } }
        // Arms the unreadable-group point, beside any armed failure: every read of the group with this root throws
        // until Disarm or another root is armed, so world marking skips that group each time it decides its groups.
        // The root is kept as typed, since a scene object's name may begin or end with a space.
        public static void ArmUnreadableGroup(string root) {
            if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("A group root is required", nameof(root));
            lock (gate) unreadableGroup = root;
        }
        // The group root whose parts the family reads as unreadable, named as for the unreadable-group point, or null.
        static string unreadableParts;
        public static string UnreadableParts { get { lock (gate) return unreadableParts; } }
        // Arms the unreadable-parts point, beside any other point: every read of the parts of the group with this root
        // throws until Disarm or another root is armed, so the family skips that group each time it decides its groups.
        // The root is kept as typed.
        public static void ArmUnreadableParts(string root) {
            if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("A group root is required", nameof(root));
            lock (gate) unreadableParts = root;
        }
        // root is computed only while the point is armed.
        internal static void TripUnreadableGroup(Func<string> root) {
            if (TripsFor(root, () => unreadableGroup, "unreadable-group")) throw new InjectedFault("Injected unreadable group", false);
        }
        internal static void TripUnreadableParts(Func<string> root) {
            if (TripsFor(root, () => unreadableParts, "unreadable-parts")) throw new InjectedFault("Injected unreadable parts", false);
        }
        // Whether the root read now is the root armed at the point; records the trip.
        static bool TripsFor(Func<string> root, Func<string> armedAt, string point) {
            string armedRoot;
            lock (gate) armedRoot = armedAt();
            if (armedRoot == null) return false;
            var read = root();
            lock (gate) {
                if (read == null || !String.Equals(read, armedAt(), StringComparison.Ordinal)) return false;
                LastTrip = "Tripped " + point + " for " + read + " at " + DateTime.UtcNow.ToString("O");
            }
            return true;
        }
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
