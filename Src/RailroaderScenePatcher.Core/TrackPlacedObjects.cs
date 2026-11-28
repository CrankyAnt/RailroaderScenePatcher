// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The objects whose place their track sets, such as a Generic track marker with a track-following placement, each with
    // the marker world that places it. A group that moves as a whole leaves each of them below it where it stands, as a
    // crossing pad stays when its setup moves. The marker capability records a marker when it applies a track-following
    // placement and forgets it when the applied state has a fixed placement or none; the group capability of the same scene
    // reads the record.
    public sealed class TrackPlacedObjects {
        sealed class ByReference : IEqualityComparer<object> {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        readonly Dictionary<object, ITrackMarkerWorld> held = new Dictionary<object, ITrackMarkerWorld>(new ByReference());
        public bool Contains(object target) => target != null && held.ContainsKey(target);
        internal void Set(object target, ITrackMarkerWorld world, bool placedByTrack) {
            if (target == null) return;
            if (placedByTrack && world != null) held[target] = world; else held.Remove(target);
        }
        // One recorded object below a group, with its map placement before the group moved.
        internal sealed class Kept {
            internal Kept(object target, ITrackMarkerWorld world) { Target = target; World = world; Position = world.Position(target); Rotation = world.Rotation(target); }
            internal object Target { get; }
            internal ITrackMarkerWorld World { get; }
            internal double[] Position { get; }
            internal double[] Rotation { get; }
        }
        // The recorded objects that stand below the group now, with where they stand. An object that no longer exists is
        // forgotten.
        internal IReadOnlyList<Kept> Below(IGroupWorld groups, object group) {
            foreach (var gone in held.Keys.Where(target => !groups.Alive(target)).ToList()) held.Remove(gone);
            return held.Where(pair => groups.Below(pair.Key, group)).Select(pair => new Kept(pair.Key, pair.Value)).ToArray();
        }
        // Puts each kept object back where it stood.
        internal static void PutBack(IReadOnlyList<Kept> kept) {
            foreach (var one in kept ?? Array.Empty<Kept>())
                if (PlacementChanged(one))
                    one.World.SetPlacement(one.Target, one.Position, one.Rotation);
        }
        // Why a kept object no longer stands where it stood, or null.
        internal static string Moved(IReadOnlyList<Kept> kept) =>
            (kept ?? Array.Empty<Kept>()).Any(PlacementChanged)
                ? "a track marker that follows its track left its track placement" : null;
        static bool PlacementChanged(Kept one) =>
            !GroupChecks.Near(one.World.Position(one.Target), one.Position, GroupChecks.PositionTolerance) ||
            !GroupChecks.SameRotation(one.World.Rotation(one.Target), one.Rotation);
    }
}
