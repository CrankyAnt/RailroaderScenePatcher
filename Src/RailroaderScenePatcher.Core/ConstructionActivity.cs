// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The game-facing operations on the objects a rail bridge's construction controller switches
    // (Game.Mow.TrackConstructionScenery). The controller sets the activity of the objects its entries name whenever
    // the construction state of its track segments changes.
    public interface IConstructionHost {
        // Whether a loaded construction controller switches the object's activity.
        bool Switched(object node);
        // The object's own activity now.
        bool Active(object node);
        // Points every controller entry that names the object at a stand-in whose activity starts at choice: from then on
        // each write of the controller that changes the stand-in's activity reaches changed instead of the object.
        void Divert(object node, bool choice, Action<bool> changed);
        // Points those entries back at the object and drops the stand-in.
        void Restore(object node);
        void SetActive(object node, bool active);
        bool Alive(object node);
    }

    // The activity of the objects a construction controller switches, where the patcher shares it with the controller.
    // An explicit disable holds the object off against the controller, and Enable returns it to the controller's
    // current choice. For an object that own culling switches, the controller's current choice is what own culling
    // shows while its sphere is in view, so own culling never freezes the construction state. While neither applies,
    // the controller switches the object itself, as without the patcher.
    public sealed class ConstructionActivity {
        sealed class Entry { internal bool Choice, Held; }
        readonly Dictionary<object, Entry> diverted = new Dictionary<object, Entry>(ByIdentity.Instance);
        readonly IConstructionHost host;
        readonly PartCullingController culling;
        public ConstructionActivity(IConstructionHost host, PartCullingController culling) {
            this.host = host ?? throw new ArgumentNullException(nameof(host)); this.culling = culling ?? throw new ArgumentNullException(nameof(culling));
        }
        public bool Switched(object node) => node != null && host.Switched(node);
        // The object's activity as the patch sets it: false while an explicit disable holds it off. Null for an object
        // that no controller switches.
        public bool? Enabled(object node) => !Switched(node) ? (bool?)null : !(diverted.TryGetValue(node, out var entry) && entry.Held);
        // Whether an explicit disable holds the object off against its controller.
        public bool Held(object node) => node != null && diverted.TryGetValue(node, out var entry) && entry.Held;
        // The activity the object takes while nothing else switches it: the controller's current choice unless an
        // explicit disable holds it off. Null for an object whose writes are not diverted: its own activity says it.
        public bool? Effective(object node) => node != null && diverted.TryGetValue(node, out var entry) ? entry.Choice && !entry.Held : (bool?)null;
        // Sets the patch's activity of an object a controller switches: false holds it off, true returns it to the
        // controller's choice. False for any other object, which the caller switches itself.
        public bool Set(object node, bool active) {
            if (!Switched(node)) return false;
            var entry = Divert(node);
            entry.Held = !active;
            Write(node, entry);
            Settle(node);
            return true;
        }
        Entry Divert(object node) {
            if (diverted.TryGetValue(node, out var entry)) return entry;
            entry = new Entry();
            diverted[node] = entry;
            var current = host.Active(node);
            // An object own culling switches can be off only because its sphere is out of view: the controller's choice
            // is then the activity own culling holds for it, which its plan took from the object while it was the
            // controller's.
            entry.Choice = culling.Desired(node) ?? current;
            // The stand-in starts at that choice: the controller's later writes then reach the patcher exactly when they
            // change its choice, also when the object is off only because own culling hides it.
            host.Divert(node, entry.Choice, choice => Changed(node, choice));
            return entry;
        }
        void Changed(object node, bool choice) {
            if (!diverted.TryGetValue(node, out var entry)) return;
            entry.Choice = choice;
            if (host.Alive(node)) Write(node, entry);
        }
        void Write(object node, Entry entry) {
            var value = entry.Choice && !entry.Held;
            if (!culling.SetDesired(node, value)) host.SetActive(node, value);
        }
        // Brings an object a controller switches to who owns its activity, after own culling took it over or let it go:
        // an object own culling switches, or one an explicit disable holds, stays diverted; any other object goes back to
        // its controller with the controller's current choice.
        public void Settle(object node) {
            if (!Switched(node)) return;
            var owned = culling.Desired(node) != null;
            if (diverted.TryGetValue(node, out var entry)) {
                if (entry.Held || owned) return;
                diverted.Remove(node);
                host.Restore(node);
                if (host.Alive(node)) host.SetActive(node, entry.Choice);
            } else if (owned) Write(node, Divert(node));
        }
        public void Settle(IEnumerable<object> nodes) { foreach (var node in (nodes ?? Enumerable.Empty<object>()).ToArray()) Settle(node); }
        // The objects whose writes are diverted now.
        public IReadOnlyList<object> Diverted => diverted.Keys.ToArray();
        // The patcher's state ends while the scene stays: every object goes back to its controller with the
        // controller's current choice. An explicit disable no longer holds.
        public void ReleaseAll() {
            foreach (var pair in diverted.ToArray()) {
                host.Restore(pair.Key);
                if (host.Alive(pair.Key)) host.SetActive(pair.Key, pair.Value.Choice);
            }
            diverted.Clear();
        }
        // The map unloads: forgets everything without touching an object.
        public void Unload() => diverted.Clear();
    }
}
