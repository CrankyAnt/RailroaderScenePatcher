// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher.Core {
    // How a world-load callback relates to the state this peer already holds.
    public enum WorldEntry {
        // The peer had no state for this local world and created it.
        Started,
        // A new load attempt over the state retained for the same local world.
        Reentered,
        // Another callback in the current load attempt.
        Repeated
    }
    // Binds one peer's patcher state to its local world. Every host and client owns its own instance; nothing here is
    // shared with or sent to another peer. The state lives as long as the world key stays the same; each load attempt
    // over it starts with the reentry callback, which reconciles the retained state with the actual objects. The attempt
    // key, such as the track graph instance, separates load attempts within one world; a world unload ends the attempt.
    public sealed class LocalWorldBinding<T> : IDisposable where T : class {
        readonly Func<T> create;
        readonly Action<T> release, reenter;
        object world, attempt;
        bool bound, open, attemptBound, startedHere;
        public LocalWorldBinding(Func<T> create, Action<T> release, Action<T> reenter) {
            this.create = create ?? throw new ArgumentNullException(nameof(create));
            this.release = release ?? throw new ArgumentNullException(nameof(release));
            this.reenter = reenter ?? throw new ArgumentNullException(nameof(reenter));
        }
        public T State { get; private set; }
        // Set when the state for the current world could not be created.
        public string Error { get; private set; }
        // Set when reconciling the retained state failed; the binding then started a new state for the world.
        public string ReentryError { get; private set; }
        // The state for the world, opening a load attempt when none is open. A native check that runs before the
        // world-load callback uses this; the callback then joins the attempt.
        public T Prepare(object world) {
            if (world == null) throw new ArgumentNullException(nameof(world));
            Open(world);
            return State;
        }
        public WorldEntry Enter(object world, object attempt) {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (bound && open && attemptBound && Equals(this.world, world)) {
                if (Equals(this.attempt, attempt)) return WorldEntry.Repeated;
                // A changed attempt key within the same world: the open attempt ends here.
                open = false;
            }
            var joined = bound && open && !attemptBound && Equals(this.world, world);
            if (!joined) Open(world);
            this.attempt = attempt; attemptBound = true;
            return startedHere ? WorldEntry.Started : WorldEntry.Reentered;
        }
        // Ends the current load attempt and keeps the state for a later load attempt over the same world.
        public void EndAttempt() { open = false; attemptBound = false; attempt = null; }
        void Open(object world) {
            if (bound && Equals(this.world, world)) {
                if (open) return;
                open = true; startedHere = false; ReentryError = null;
                if (State == null) { Start(); return; }
                try { reenter(State); return; }
                catch (Exception e) { ReentryError = "Retained world reconciliation failed: " + e.GetBaseException().Message; }
                Discard();
                this.world = world; bound = true; open = true;
                Start();
                return;
            }
            Release();
            this.world = world; bound = true; open = true; ReentryError = null;
            Start();
        }
        void Start() {
            startedHere = true;
            try { State = create() ?? throw new InvalidOperationException("Local world state unavailable"); Error = null; }
            catch (Exception e) { State = null; Error = "Capability prerequisites unavailable: " + e.GetBaseException().Message; }
        }
        void Discard() {
            var state = State;
            State = null; Error = null; world = null; bound = false; open = false; attemptBound = false; attempt = null;
            if (state != null) release(state);
        }
        public void Release() { Discard(); ReentryError = null; }
        public void Dispose() { Release(); }
    }
}
