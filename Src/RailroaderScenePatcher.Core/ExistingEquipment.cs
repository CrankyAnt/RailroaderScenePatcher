// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // What the mast pattern reads from the light object of an existing crossing mast and from where it stands. The
    // native world fills it from the object's components; the rules that judge it are ExistingEquipmentPattern's.
    public sealed class MastFacts {
        // The lamps on the light object itself and the gates at and below it.
        public List<LampFacts> Lamps = new List<LampFacts>();
        public List<GateFacts> Gates = new List<GateFacts>();
        // Lamps on objects below the light object: a second light object inside this one.
        public int LampsBelow;
        // Gates that stand beside the light object: below the object directly above it, when that is not the crossing
        // controller, and not at or below the light object. A move of the light object would leave them behind.
        public int GatesBeside;
        public bool MissingScript, AddedByPatcher;
        // What the object holds at or below it besides lamps and gates.
        public int Controllers, KeyValueObjects, Spans, Cullers, Bells, Rigidbodies;
        // Whether the nearest key-value object above the light object is the one of a crossing controller.
        public bool BelowController = true;
        // A track-relative component at or above the light object, below the controller, that switches it with a track group.
        public bool FollowsTrackGroup;
        // Objects that share the light object's hierarchy path.
        public int SharedPath = 1;
    }
    // What the bell pattern reads from the bell player a crossing controller created.
    public sealed class ExistingBellFacts {
        // The player is the one its crossing controller created and drives.
        public bool ControllersOwn = true;
        // The controller is enabled, so its bell exists and it sets the bell's play state.
        public bool ControllerActive = true;
        public bool MissingScript;
        public int SharedPath = 1;
    }
    // The supported patterns of existing crossing equipment. existing-crossing-mast-v1: the light object of a mast, whose lamps
    // observe the lights key its crossing controller publishes, with its gates at or below it observing that
    // controller's keys, and nothing below it that a move or a switch of the light object would leave stale.
    // existing-crossing-bell-v1: the bell player a crossing controller creates beneath itself and drives directly.
    public static class ExistingEquipmentPattern {
        // Why the light object does not conform, or null. keys are the lights and gates keys the controller publishes.
        public static string MastProblem(MastFacts m, IReadOnlyList<string> keys) {
            if (m == null) throw new ArgumentNullException(nameof(m));
            if (keys == null || keys.Count == 0) throw new ArgumentException("The controller's keys are required", nameof(keys));
            if (m.AddedByPatcher) return "the patcher added this equipment; edit or remove it on its crossing controller's Connections tab";
            if (m.SharedPath > 1) return m.SharedPath + " objects share this hierarchy path, so a scene patch file cannot name this one";
            if (m.MissingScript) return "a script in its hierarchy is missing";
            if (!m.BelowController) return "its nearest key-value object does not belong to a crossing controller, so no crossing controller drives it";
            if (m.Lamps.Count == 0) return "this gate does not stand on a light object, so it is not part of a mast";
            if (m.LampsBelow > 0) return "it holds another light object below it; the mast pattern covers one light object with its gates";
            if (m.GatesBeside > 0) return "a gate stands beside this light object rather than on or below it; moving the light object would leave the gate behind";
            if (m.Controllers > 0) return "it holds a crossing controller";
            if (m.KeyValueObjects > 0) return "it holds its own key-value object, which intercepts the crossing controller's keys";
            if (m.Spans > 0) return "it holds a detection span";
            if (m.Cullers > 0) return "it holds its own culler";
            if (m.Bells > 0) return "it holds a bell player";
            if (m.Rigidbodies > 0) return "it holds a rigidbody; only static colliders are supported";
            if (m.FollowsTrackGroup) return "a track-relative component switches it with a track group, which the mast pattern does not cover";
            foreach (var l in m.Lamps) {
                if (!keys.Contains(l.Key)) return "lamp " + l.Name + " observes key '" + l.Key + "', which the crossing controller does not publish";
                if (l.OutsideTargets > 0) return "lamp " + l.Name + " targets an object outside the mast, which would not move with it";
            }
            foreach (var g in m.Gates)
                if (!keys.Contains(g.Key)) return "gate " + g.Name + " observes key '" + g.Key + "', which the crossing controller does not publish";
            return null;
        }
        // Why the bell player does not conform, or null.
        public static string BellProblem(ExistingBellFacts b) {
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (b.SharedPath > 1) return b.SharedPath + " objects share this hierarchy path, so a scene patch file cannot name this one";
            if (b.MissingScript) return "a script in its hierarchy is missing";
            if (!b.ControllersOwn) return "this bell player is not the one its crossing controller created, so the controller does not drive it";
            if (!b.ControllerActive) return "its crossing controller is disabled, so its bell cannot work";
            return null;
        }
    }

    // Existing crossing masts and bells: places and enables or disables the light object of a mast, with the gate on its
    // child, or the bell of a crossing controller. Both are physical placement and activity only: the equipment keeps
    // observing its controller, which keeps driving it. The crossing controller is the equipment's group for culling:
    // a mast moved out of the sphere of its crossing's culler keeps a culling sphere of its own, as a moved part of a
    // baked scenery group does, and the culler keeps its radius. A fixed placement holds the equipment in map
    // coordinates; without one it stands where the scene has it in the object above it.
    public sealed class ExistingEquipmentScene : IScene, IVersionedScene, IFramedScene, IRelationshipScene, IRetainedScene {
        public const string MastStructure = "existing-crossing-mast-v1", BellStructure = "existing-crossing-bell-v1";
        public const string MastComponent = "Track.KeyValueLampMaterialController", BellComponent = "Audio.IntegerLoopingPlayer", GateComponent = "RollingStock.Controls.KeyValueBoolAnimator";
        public const string MastFamily = "Crossing mast", BellFamily = "Crossing bell";
        public const string RequiredVersion = "0.5.0";
        // Why a track-following placement of an existing mast or bell is refused before any change.
        public const string NoFollowing = "Existing crossing masts and bells use fixed placement. Track-following placement does not apply to them";
        readonly IGroupWorld world;
        readonly PartHolds holds;
        readonly string component, structure, noun;
        ExistingEquipmentScene(IGroupWorld world, PartHolds holds, string component, string structure, string noun, Capability capability) {
            this.world = world ?? throw new ArgumentNullException(nameof(world)); this.holds = holds ?? throw new ArgumentNullException(nameof(holds));
            this.component = component; this.structure = structure; this.noun = noun; Capability = capability;
        }
        // world finds the light objects of masts, or the bells, as its parts and the crossing controller as their group.
        // The mast and bell capabilities of one scene share the fixed placements, so one crossing has one culling plan.
        public static ExistingEquipmentScene Masts(IGroupWorld world, PartHolds holds, Capability capability = Capability.Unchecked) => new ExistingEquipmentScene(world, holds, MastComponent, MastStructure, "mast", capability);
        public static ExistingEquipmentScene Bells(IGroupWorld world, PartHolds holds, Capability capability = Capability.Unchecked) => new ExistingEquipmentScene(world, holds, BellComponent, BellStructure, "bell", capability);
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != component || (identity.Structure != null && identity.Structure != structure)) return Array.Empty<object>();
            return world.FindParts(identity.Scene, identity.Path) ?? Array.Empty<object>();
        }
        public void Preflight(object target) {
            var refusal = world.PartRefusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            var observed = world.ObservePart(target);
            if (!GroupChecks.Finite(observed.Part.Placement) || !GroupChecks.Finite(observed.GroupState.Root.Placement)) throw new InvalidOperationException("Map coordinates are unavailable for the " + noun + " or its crossing controller");
        }
        // Without a fixed placement the equipment stands in the object above it: its placement says so, and applied back
        // it returns the equipment there.
        public ObjectState Read(object target) {
            var observed = world.ObservePart(target);
            var placement = observed.Part.Placement;
            if (placement != null && !holds.Contains(target) && observed.ParentPlacement != null) placement = new Placement(placement.Position, placement.Rotation) { Frame = observed.ParentPlacement };
            // Equipment that a culler of the game switches itself reads no activity: Enabled does not apply to it.
            return new ObjectState(placement, world.ActivityRefusal(target) != null ? null : observed.Part.Active);
        }
        public Placement FramePlacement(object target) => world.ObservePart(target).ParentPlacement;
        // Placement and activity leave the logical binding: the equipment takes no relationship of any kind.
        public void CheckRelationships(object target, ObjectState state) {
            if (state?.Marker != null) throw new InvalidOperationException("An existing crossing " + noun + " does not support a track-marker relationship");
            if (state?.Coverage != null) throw new InvalidOperationException("An existing crossing " + noun + " does not support a crossing-coverage relationship. Set coverage on its crossing controller");
            if (state?.Equipment != null) throw new InvalidOperationException("An existing crossing " + noun + " does not support a crossing-equipment relationship. Add equipment on its crossing controller");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
            // The capability covers fixed placement: nothing here re-evaluates a placement when its track changes.
            if (state?.Placement?.Following != null) throw new InvalidOperationException(NoFollowing);
            // A disable that could not hold against the game's progression or map feature is refused before any change.
            var refusal = (state?.Active == null ? null : world.ActivityRefusal(target)) ?? (state?.Active == false ? world.DisableRefusal(target) : null);
            if (refusal != null) throw new InvalidOperationException(refusal);
        }
        sealed class Snapshot { internal PartObservation Observed; internal Placement Held, Scene; }
        public object Capture(object target) => new Snapshot { Observed = world.ObservePart(target), Held = holds.Of(target), Scene = holds.AuthoredOf(target) };
        void Place(object target, object controller, Placement placement, Placement held, Placement scene) {
            holds.Set(target, held, controller, scene);
            world.SetPartPlacement(target, placement);
            holds.Reassert(world, target);
        }
        // The cullers of the crossing follow, and each moved mast outside the crossing culler's sphere keeps a sphere of its own.
        CullingPlan Refresh(object controller, object except = null) {
            var plan = GroupChecks.Plan(world, holds, controller, except);
            world.RefreshCulling(controller, plan);
            return plan;
        }
        public void Apply(object target, ObjectState state) {
            var before = world.ObservePart(target);
            var desired = state.Placement;
            if (desired != null) GroupChecks.CheckPlacement(desired);
            var switched = world.ActivityRefusal(target) != null;
            var active = state.Active ?? before.Part.Active ?? true;
            if (!switched && active && before.Part.Active != true) world.SetPartActive(target, true);
            // A placement read in the object above returns the equipment there; any other placement is fixed and holds it.
            if (desired != null) Place(target, before.Group, desired, desired.Frame == null ? desired : null, holds.Contains(target) ? holds.AuthoredOf(target) : GroupChecks.InParent(before.GroupState, target));
            if (!switched && !active && before.Part.Active != false) world.SetPartActive(target, false);
            Verify(target, before, desired ?? before.Part.Placement, switched ? (bool?)null : active, Refresh(before.Group));
        }
        void Verify(object target, PartObservation before, Placement placement, bool? active, CullingPlan plan) {
            var after = world.ObservePart(target);
            if (!GroupChecks.SamePlacement(after.Part.Placement, placement) || (active != null && after.Part.Active != active)) throw new InvalidOperationException("The " + noun + "'s placement or activity does not match the requested state");
            var controller = before.GroupState.Root; var now = after.GroupState.Root;
            if (!ReferenceEquals(after.Group, before.Group) || !GroupChecks.SamePlacement(now.Placement, controller.Placement) || now.Active != controller.Active) throw new InvalidOperationException("Editing the " + noun + " changed its crossing controller");
            var difference = GroupChecks.Layout(before.GroupState, after.GroupState, h => ReferenceEquals(h, target) || holds.Contains(h), target)
                ?? GroupChecks.Culling(after.GroupState, now.Active != false, plan);
            if (difference != null) throw new InvalidOperationException("Editing the " + noun + " changed more than the " + noun + " itself: " + difference);
        }
        public bool Restore(object target, object snapshot) {
            var captured = (Snapshot)snapshot;
            try {
                var current = world.ObservePart(target);
                if (!ReferenceEquals(current.Parent, captured.Observed.Parent) || !ReferenceEquals(current.Group, captured.Observed.Group)) return false;
                var part = captured.Observed.Part;
                var switched = world.ActivityRefusal(target) != null;
                if (!switched && part.Active == true) world.SetPartActive(target, true);
                if (part.Placement != null) Place(target, captured.Observed.Group, part.Placement, captured.Held, captured.Scene);
                if (!switched && part.Active == false) world.SetPartActive(target, false);
                Verify(target, captured.Observed, part.Placement, switched ? (bool?)null : part.Active != false, Refresh(captured.Observed.Group));
                return true;
            } catch { return false; }
        }
        // Equipment that no longer stands under its identity holds nothing any more, and its crossing lets go of its own culling.
        public void Release(object target) {
            var controller = holds.GroupOf(target);
            if (controller != null && world.Alive(controller)) Refresh(controller, target);
            holds.Set(target, null);
        }
        public bool Unsettled(object target) => false;
        public string MinimumPatcherVersion(TargetIdentity target) => Requires(target) ? RequiredVersion : null;
        // The requirement holds for the scene patch file whether or not this capability is available in the current load.
        public static bool Requires(TargetIdentity target) => target != null && (target.Structure == MastStructure || target.Structure == BellStructure);
    }
}
