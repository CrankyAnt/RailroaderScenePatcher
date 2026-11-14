// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher {
    // One handler on the current instance of a native event source, such as the map manager's terrain completion event.
    // Binding to another instance moves the handler; binding again to the same instance adds nothing. UI-independent,
    // shared with the controlled tests.
    internal sealed class InstanceSubscription : IDisposable {
        readonly Action<object> add, remove;
        internal InstanceSubscription(Action<object> add, Action<object> remove) {
            this.add = add ?? throw new ArgumentNullException(nameof(add));
            this.remove = remove ?? throw new ArgumentNullException(nameof(remove));
        }
        // The source that holds the handler, or null.
        internal object Instance { get; private set; }
        // True when the handler moved to the instance. A null instance leaves the subscription where it is. When adding
        // throws, nothing is bound and a later call retries.
        internal bool Bind(object current) {
            if (current == null || ReferenceEquals(current, Instance)) return false;
            if (Instance != null) { var previous = Instance; Instance = null; remove(previous); }
            add(current);
            Instance = current;
            return true;
        }
        public void Dispose() {
            var previous = Instance;
            Instance = null;
            if (previous != null) remove(previous);
        }
    }
}
