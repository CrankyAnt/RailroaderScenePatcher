// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher.Core {
    // Binds one peer's load-attempt state to a caller-supplied world key. Every host and
    // client owns its own instance; nothing here is shared with or sent to another peer.
    public sealed class LocalWorldBinding<T> : IDisposable where T : class {
        readonly Func<T> create;
        readonly Action<T> release;
        object world;
        bool bound;
        public LocalWorldBinding(Func<T> create, Action<T> release) {
            this.create = create ?? throw new ArgumentNullException(nameof(create));
            this.release = release ?? throw new ArgumentNullException(nameof(release));
        }
        public T State { get; private set; }
        public string Error { get; private set; }
        // Returns true when the key differs from the bound one, which releases the previous
        // state and starts a new load attempt. An equal key keeps the existing state.
        public bool Enter(object world) {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (bound && Equals(this.world, world)) return false;
            Release();
            this.world = world; bound = true;
            try { State = create() ?? throw new InvalidOperationException("Local world state unavailable"); }
            catch (Exception e) { State = null; Error = "Capability prerequisites unavailable: " + e.GetBaseException().Message; }
            return true;
        }
        public void Release() {
            var state = State;
            State = null; Error = null; world = null; bound = false;
            if (state != null) release(state);
        }
        public void Dispose() { Release(); }
    }
}
