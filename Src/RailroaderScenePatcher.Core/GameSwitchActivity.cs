// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // What of the game switches a scene object on and off itself, by the list that names it.
    [Flags]
    public enum GameSwitch {
        None = 0,
        // A rail bridge's construction controller (Game.Mow.TrackConstructionScenery), by the construction state of its
        // track segments.
        Construction = 1,
        // A progression controller (Game.Progression.ProgressionEntityObjectController), by its industry's or station's
        // progression state, at load and whenever industries change.
        Progression = 2,
        // A map feature (Game.Progression.MapFeature, gameObjectsEnableOnUnlock), by whether it is unlocked.
        MapFeature = 4,
    }
    // The game-facing operations on the objects a switch of the game sets active or inactive. Each switch sets the
    // activity of the objects its lists name.
    public interface IGameSwitchHost {
        // Which loaded switches set the object's activity; None for an object that no switch names. A switch of a container object
        // above the object is no switch of the object.
        GameSwitch Switches(object node);
        // The object's own activity now.
        bool Active(object node);
        // Points every list entry of a switch that names the object at a stand-in whose activity starts at choice: from
        // then on each write of a switch that changes the stand-in's activity reaches changed instead of the object.
        void Divert(object node, bool choice, Action<bool> changed);
        // Points those entries back at the object and drops the stand-in.
        void Restore(object node);
        void SetActive(object node, bool active);
        bool Alive(object node);
    }

    // A world whose objects a switch of the game may set: why an explicit disable of the object cannot hold against that
    // switch, or null. The capability refuses `active: false` with the reason before changing anything, rather than
    // disabling the object until the switch sets it again.
    public interface IDisableRefusal {
        string DisableRefusal(object target);
    }

    // The list entries of the game's switches that name one object, pointed at a stand-in that receives the switches'
    // writes for it. A list is the switch's own array or list, so its entries are written in place.
    public static class SwitchSlots {
        // Points every entry of the lists that is the node at the stand-in, and returns those entries with the switches
        // they belong to. A failure part way puts back every entry already pointed at the stand-in, then throws, so no
        // entry stays diverted that no record names.
        public static IReadOnlyList<KeyValuePair<IList, int>> Divert(IEnumerable<KeyValuePair<IList, GameSwitch>> lists, object node, object standIn, out GameSwitch kinds) {
            var slots = new List<KeyValuePair<IList, int>>();
            kinds = GameSwitch.None;
            try {
                foreach (var list in (lists ?? Enumerable.Empty<KeyValuePair<IList, GameSwitch>>()).ToArray()) {
                    if (list.Key == null) continue;
                    for (var i = 0; i < list.Key.Count; i++)
                        if (ReferenceEquals(list.Key[i], node)) { list.Key[i] = standIn; slots.Add(new KeyValuePair<IList, int>(list.Key, i)); kinds |= list.Value; }
                }
            } catch {
                Restore(slots, node, standIn);
                throw;
            }
            return slots;
        }
        // Points the entries that still hold the stand-in back at the node. True when every entry was put back or no
        // longer holds the stand-in.
        public static bool Restore(IEnumerable<KeyValuePair<IList, int>> slots, object node, object standIn) {
            var complete = true;
            foreach (var slot in slots ?? Enumerable.Empty<KeyValuePair<IList, int>>()) {
                try { if (slot.Key != null && slot.Value < slot.Key.Count && ReferenceEquals(slot.Key[slot.Value], standIn)) slot.Key[slot.Value] = node; }
                catch { complete = false; }
            }
            return complete;
        }
    }

    // The activity of the objects a switch of the game sets, where the patcher shares it with that switch. An explicit
    // disable holds the object off against the switch, and Enable returns it to the switch's current choice. For an
    // object that own culling switches, the switch's current choice is what own culling shows while its sphere is in
    // view, so own culling never freezes the construction state. While neither applies, the switch sets the object
    // itself, as without the patcher.
    public sealed class GameSwitchActivity {
        sealed class Entry { internal bool Choice, Held; }
        readonly Dictionary<object, Entry> diverted = new Dictionary<object, Entry>(ByIdentity.Instance);
        readonly IGameSwitchHost host;
        readonly PartCullingController culling;
        public GameSwitchActivity(IGameSwitchHost host, PartCullingController culling) {
            this.host = host ?? throw new ArgumentNullException(nameof(host)); this.culling = culling ?? throw new ArgumentNullException(nameof(culling));
        }
        public GameSwitch Switches(object node) => node == null ? GameSwitch.None : host.Switches(node);
        public bool Switched(object node) => Switches(node) != GameSwitch.None;
        // The object's activity as the patch sets it: false while an explicit disable holds it off. Null for an object
        // that no switch sets.
        public bool? Enabled(object node) => !Switched(node) ? (bool?)null : !(diverted.TryGetValue(node, out var entry) && entry.Held);
        // The activity a capability reads for the object: the patch's activity for an object a switch sets, otherwise
        // the object's own activity.
        public bool Read(object node, bool own) => Enabled(node) ?? own;
        // The switches that set objects of every family, unlike a construction controller, which sets only the parts of
        // its own rail bridge.
        public const GameSwitch Progressions = GameSwitch.Progression | GameSwitch.MapFeature;
        // The patch's activity of an object a progression controller or a map feature sets; null for any other object.
        public bool? ProgressionEnabled(object node) => (Switches(node) & Progressions) != 0 ? Enabled(node) : null;
        // Sets the patch's activity of an object a progression controller or a map feature sets; false for any other
        // object, which the caller switches itself.
        public bool SetProgression(object node, bool active) => (Switches(node) & Progressions) != 0 && Set(node, active);
        // The activity a station map icon or track marker capability reads: a track-group follower reads its own activity,
        // which its group sets; any other object reads the patch's activity when progression or a map feature sets it.
        public bool ReadActivity(object node, bool own, bool follower) => follower ? own : ProgressionEnabled(node) ?? own;
        // Sets the activity of a station map icon or track marker: true when the shared activity took the value, false
        // when the caller switches the object itself. A track-group follower's disable is a hold (HoldFollower).
        public bool SetActivity(object node, bool active, bool follower) => !follower && SetProgression(node, active);
        // The hold of a track-group follower installed or released: a follower that progression or a map feature also
        // sets is held against them too, and released to their current choice.
        public void HoldFollower(object node, bool hold) => SetProgression(node, !hold);
        // Whether an explicit disable holds the object off against its switch.
        public bool Held(object node) => node != null && diverted.TryGetValue(node, out var entry) && entry.Held;
        // The switches an explicit disable holds the object off against, for the amber warning; None while no explicit
        // disable holds it or no switch sets it.
        public GameSwitch HeldAgainst(object node) => Held(node) ? Switches(node) : GameSwitch.None;
        // The activity the object takes while nothing else switches it: the switch's current choice unless an explicit
        // disable holds it off. Null for an object whose writes are not diverted: its own activity says it.
        public bool? Effective(object node) => node != null && diverted.TryGetValue(node, out var entry) ? entry.Choice && !entry.Held : (bool?)null;
        // Sets the patch's activity of an object a switch sets: false holds it off, true returns it to the switch's
        // choice. False for any other object, which the caller switches itself.
        public bool Set(object node, bool active) {
            if (!Switched(node)) return false;
            // Enabling an object whose writes are not diverted leaves it with its switch, which already set it; only own
            // culling may still take it over. No stand-in is made, so a restoration after a failed diversion cannot fail
            // the same way.
            if (active && !diverted.ContainsKey(node)) { Settle(node); return true; }
            var entry = Divert(node);
            entry.Held = !active;
            Write(node, entry);
            Settle(node);
            return true;
        }
        Entry Divert(object node) {
            if (diverted.TryGetValue(node, out var entry)) return entry;
            entry = new Entry();
            var current = host.Active(node);
            // An object own culling switches can be off only because its sphere is out of view: the switch's choice is
            // then the activity own culling holds for it, which its plan took from the object while it was the switch's.
            entry.Choice = culling.Desired(node) ?? current;
            // The stand-in starts at that choice: the switch's later writes then reach the patcher exactly when they
            // change its choice, also when the object is off only because own culling hides it. The entry counts only
            // once the host diverted the writes: a host that fails leaves nothing that Restore would have to find.
            host.Divert(node, entry.Choice, choice => Changed(node, choice));
            diverted[node] = entry;
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
        // Brings an object a switch sets to who owns its activity, after own culling took it over or let it go: an object
        // own culling switches, or one an explicit disable holds, stays diverted; any other object goes back to its
        // switch with the switch's current choice.
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
        // The patcher's state ends while the scene stays: every object goes back to its switch with the switch's current
        // choice. An explicit disable no longer holds.
        public void ReleaseAll() {
            foreach (var pair in diverted.ToArray()) {
                host.Restore(pair.Key);
                if (host.Alive(pair.Key)) host.SetActive(pair.Key, pair.Value.Choice);
            }
            diverted.Clear();
        }
        // The map unloads: forgets everything without touching an object.
        public void Unload() => diverted.Clear();

        // The amber warning for an object an explicit disable holds off against the switches named, on the Object tab and
        // in the footer's message area; null for None. A construction controller keeps the warning of its own.
        public const string ConstructionHoldWarning = "Your explicit disable keeps this object off in the game even when track construction progress switches it on. Click Enable or Clear activity to let construction progress control it again.";
        public const string ProgressionHoldWarning = "Your explicit disable keeps this object off in the game even when progression switches it on. Click Enable or Clear activity to let progression control it again.";
        public const string MapFeatureHoldWarning = "Your explicit disable keeps this object off in the game even when unlocking a map feature switches it on. Click Enable or Clear activity to let the map feature control it again.";
        public const string ProgressionOrMapFeatureHoldWarning = "Your explicit disable keeps this object off in the game even when progression or a map feature switches it on. Click Enable or Clear activity to let them control it again.";
        // clearOnly: Enable is refused for the object, as for a track-group follower, so the warning names Clear activity
        // alone.
        public static string HoldWarning(GameSwitch held, bool clearOnly = false) {
            string text;
            if ((held & GameSwitch.Construction) != 0) text = ConstructionHoldWarning;
            else {
                var progression = (held & GameSwitch.Progression) != 0; var feature = (held & GameSwitch.MapFeature) != 0;
                text = progression && feature ? ProgressionOrMapFeatureHoldWarning : progression ? ProgressionHoldWarning : feature ? MapFeatureHoldWarning : null;
            }
            return text != null && clearOnly ? text.Replace("Click Enable or Clear activity", "Click Clear activity") : text;
        }
        // Why an explicit disable of an object that progression or a map feature sets cannot hold, for IDisableRefusal.
        public const string UnsharedRefusal = "The patcher can't share control of this object's activity with the game's progression or map feature during this load. Disabling it wouldn't keep it off, so it can't be disabled.";
        public const string ProgressionMembersRefusal = "The required members of the game's progression controller are unavailable. Disabling this object wouldn't keep it off against progression, so it can't be disabled.";
        public const string MapFeatureMembersRefusal = "The required members of the game's map feature are unavailable. Disabling this object wouldn't keep it off against its map feature, so it can't be disabled.";
    }
}
