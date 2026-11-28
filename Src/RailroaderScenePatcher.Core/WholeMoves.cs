// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // A group world whose groups hold objects that stay where they are when the group moves as a whole: an object whose
    // place a track binding sets, such as a crossing pad or a whistle sign, and a detection span.
    public interface IWholeMoveWorld : IGroupWorld {
        // The objects below the group that keep their map placement when the group moves, each with everything below
        // it. Empty for a group without them.
        IReadOnlyList<object> Stays(object group);
        // Sets the root's placement as SetPlacement does and leaves each kept object, with everything below it, exactly
        // where it stands in the scene: no kept object takes a placement computed from the root's.
        void SetPlacementKeeping(object group, Placement placement, IReadOnlyList<object> kept);
    }
    // The whole move of a group that another capability than the scene object family edits, such as a grade-crossing
    // setup, moved through its crossing controller, or a crossing without signals, moved through its crossing object:
    // the root takes the placement, and every part that takes physical placement moves with it and keeps its placement
    // relative to the others, a part with its own placement included (PartHolds). Only the objects the world names
    // (IWholeMoveWorld.Stays) keep their map placement. The cullers follow, a moved part outside its culler's sphere
    // keeps a sphere of its own, and the ground masks at and below the root register again where their holders stand.
    // The own placements are the ones the part capabilities of the same group share, so one group has one culling plan.
    public sealed class WholeMove {
        readonly IWholeMoveWorld world;
        readonly PartHolds holds;
        readonly MaskFollowing masks;
        public WholeMove(IWholeMoveWorld world, PartHolds holds, GroundMasks masks = null) {
            this.world = world ?? throw new ArgumentNullException(nameof(world)); this.holds = holds ?? throw new ArgumentNullException(nameof(holds)); this.masks = new MaskFollowing(masks);
        }
        internal DependentOutput Output(object group) => masks.Output(group);
        internal void RetryOutput(object group) => masks.RetryOutput(group);
        // Why the group takes no placement now, or null. Checked before any change.
        internal string Refusal(object group) => world.Refusal(group) ?? world.PlacementRefusal(group);
        internal object Capture(object group) { holds.Remember(world, group); return world.Observe(group); }
        internal Placement PartInMap(object group, Placement recorded) => holds.InMap(world, group, recorded);
        internal Placement RecordPart(object group, Placement inMap) => holds.Recorded(world, group, inMap);
        // Whether the group stands at the placement already.
        internal bool At(object group, Placement placement) => GroupChecks.SamePlacement(world.Observe(group).Root.Placement, placement);
        // The objects that stay, topmost only: an object below another one that stays keeps its place in that object.
        IReadOnlyList<object> Staying(object group) {
            var named = (world.Stays(group) ?? Array.Empty<object>()).Where(handle => handle != null).ToList();
            return named.Where(handle => !named.Any(other => !ReferenceEquals(other, handle) && world.Below(handle, other))).ToArray();
        }
        // The root takes the placement around the objects that stay; every part with its own placement then takes its
        // place in its frame as the frame stands now.
        void Place(object group, Placement placement, IReadOnlyList<object> staying) {
            world.SetPlacementKeeping(group, placement, staying);
            holds.Reassert(world, group);
        }
        CullingPlan Refresh(object group) {
            var plan = GroupChecks.Plan(world, holds, group);
            world.RefreshCulling(group, plan);
            return plan;
        }
        // Only the objects that stay keep their map placement; every other part keeps its place relative to the root.
        static Func<object, bool> Framed(IReadOnlyList<object> staying) => handle => staying.Any(kept => ReferenceEquals(kept, handle));
        internal void Move(object group, Placement placement) {
            GroupChecks.CheckPlacement(placement);
            holds.Remember(world, group);
            var before = world.Observe(group);
            if (!GroupChecks.Finite(before.Root.Placement)) throw new InvalidOperationException("The group's map coordinates are unavailable");
            var staying = Staying(group);
            if (staying.Any(handle => !before.Parts.Any(part => ReferenceEquals(part.Handle, handle)))) throw new InvalidOperationException("An object that stays in place isn't listed below the group");
            masks.Write(group, () => {
                Place(group, placement, staying);
                var plan = Refresh(group);
                var after = world.Observe(group);
                if (!GroupChecks.SamePlacement(after.Root.Placement, placement)) throw new InvalidOperationException("Applied group placement did not verify");
                var difference = GroupChecks.Layout(before, after, Framed(staying)) ?? GroupChecks.Culling(after, after.Root.Active != false, plan);
                if (difference != null) throw new InvalidOperationException("The group did not move as one: " + difference);
            });
        }
        // Returns the group to the captured observation. A group that still stands as captured is left as it is. An
        // object that stays and no longer stands where it was captured, as after a move that stopped halfway, is put
        // back at its captured map placement.
        internal bool Restore(object group, object snapshot) {
            var captured = (GroupObservation)snapshot;
            try {
                var current = world.Observe(group);
                if (!ReferenceEquals(current.Parent, captured.Parent)) return false;
                var staying = Staying(group);
                if (GroupChecks.SamePlacement(current.Root.Placement, captured.Root.Placement) && GroupChecks.Layout(captured, current, Framed(staying)) == null) return true;
                return masks.Restore(group, () => {
                    Place(group, captured.Root.Placement, staying);
                    var placed = world.Observe(group);
                    foreach (var handle in staying) {
                        var was = captured.Parts.FirstOrDefault(part => ReferenceEquals(part.Handle, handle)); var now = placed.Parts.FirstOrDefault(part => ReferenceEquals(part.Handle, handle));
                        if (was == null || now == null) return false;
                        var (position, rotation) = GroupChecks.InMap(captured.Root.Placement, was); var (nowPosition, nowRotation) = GroupChecks.InMap(placed.Root.Placement, now);
                        if (!GroupChecks.Near(position, nowPosition, GroupChecks.PositionTolerance) || !GroupChecks.SameRotation(rotation, nowRotation)) world.SetPartPlacement(handle, new Placement(position, rotation));
                    }
                    var plan = Refresh(group);
                    var restored = world.Observe(group);
                    return GroupChecks.SamePlacement(restored.Root.Placement, captured.Root.Placement) && GroupChecks.Layout(captured, restored, Framed(staying)) == null
                        && GroupChecks.Culling(restored, restored.Root.Active != false, plan) == null;
                });
            } catch { return false; }
        }
    }
}
