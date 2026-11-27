// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The game-facing operations of the crossing-object capability. A crossing object is the object that holds the
    // crossbucks and crossing pads of a crossing without a crossing controller.
    public interface ICrossingObjectWorld {
        IEnumerable<string> MapIds { get; }
        // The loaded crossing objects at the hierarchy path. Several crossings can share one path, as the three
        // crossings below World/Roads Bryson/Old River Rd do.
        IReadOnlyList<object> Find(string scene, string path);
        // The marker IDs of the crossing pads the crossing object holds.
        IReadOnlyList<string> PadMarkerIds(object crossing);
        double[] Position(object crossing);
        double[] Rotation(object crossing);
        bool Active(object crossing);
    }
    // A crossing without signals as one object: the crossing object with its crossbucks and crossing pads. The
    // capability exists so that such a crossing is duplicated as one mod object and that mod object is removed as one.
    // It changes nothing: a crossbuck is moved and a pad is rebound by the capability of its own family, and the
    // crossing object stays where it stands. Where crossings share a path, the marker ID of a crossing's pad names
    // the crossing, as it names the crossbucks of that crossing.
    public sealed class CrossingObjectScene : IScene, IVersionedScene, IRelationshipScene, IPadMarkerIdentityScene {
        public const string Structure = "crossing-object-v1", ComponentType = "UnityEngine.Transform", RequiredVersion = "0.5.0";
        // The family the window names a crossing without signals by.
        public const string Family = "Crossing without signals";
        public const string NoEdits = "You can't edit this crossing as a whole. Edit its crossbucks and crossing pads separately.";
        readonly ICrossingObjectWorld world;
        public CrossingObjectScene(ICrossingObjectWorld world, Capability capability) { this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; }
        // The identity of the crossing object at the path. padMarkerId names the crossing where crossings share the path.
        public static TargetIdentity Identity(string scene, string path, string padMarkerId = null) => new TargetIdentity(scene, path, ComponentType, Structure, String.IsNullOrEmpty(padMarkerId) ? null : padMarkerId);
        // How many crossing objects an inventory shows for a target, by the rule Resolve uses. A crossing object is no
        // observation of its own: the crossing pads directly in it are, and a crossing object is known by them. A path in
        // a mod object compares by its mark, so a copy that stands at another parent counts as well. holderOf names the
        // object a pad stands in, which tells crossings that share a path apart; without it every pad at a path counts
        // for one crossing object. A target without a pad marker ID counts every crossing object at its path; with one,
        // the crossing objects whose pads hold it. A mod object is a crossing object without a pad as well: it counts
        // while an observation stands directly in it.
        public static int Observed(IEnumerable<SceneObservation> entries, TargetIdentity target, Func<object, object> holderOf = null) {
            if (entries == null || target == null || target.Component != ComponentType || target.Structure != Structure) return 0;
            var below = ModObjects.Canonical(target.Path) + "/";
            var members = entries.Where(e => e.Identity.Scene == target.Scene && ModObjects.Canonical(e.Identity.Path) is string path
                && path.StartsWith(below, StringComparison.Ordinal) && path.IndexOf('/', below.Length) < 0).ToArray();
            var crossings = new Dictionary<object, List<string>>();
            foreach (var pad in members.Where(e => e.Family == SceneFamilies.CrossingPad)) {
                var holder = holderOf == null ? below : holderOf(pad.Handle);
                if (holder == null) continue;
                if (!crossings.TryGetValue(holder, out var ids)) crossings[holder] = ids = new List<string>();
                var id = pad.MarkerId;
                if (!String.IsNullOrEmpty(id)) ids.Add(id);
            }
            if (crossings.Count == 0 && ModObjects.IsPath(target.Path) && members.Length != 0) crossings[below] = new List<string>();
            return target.PadMarkerId == null ? crossings.Count : crossings.Values.Count(ids => ids.Contains(target.PadMarkerId, StringComparer.Ordinal));
        }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        // Only a target that names the structure: the component alone also names every scene object.
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != ComponentType || identity.Structure != Structure) return Array.Empty<object>();
            var found = world.Find(identity.Scene, identity.Path) ?? Array.Empty<object>();
            return identity.PadMarkerId == null ? found : found.Where(crossing => (world.PadMarkerIds(crossing) ?? Array.Empty<string>()).Contains(identity.PadMarkerId, StringComparer.Ordinal)).ToArray();
        }
        public string PadMarkerRefusal(TargetIdentity identity) =>
            identity.PadMarkerId == null || (identity.Component == ComponentType && identity.Structure == Structure) ? null : TargetIdentity.PadMarkerNotAccepted;
        public string MinimumPatcherVersion(TargetIdentity target) => VersionRequirement.For(target, Structure, null, RequiredVersion);
        public void Preflight(object target) { }
        Placement Placed(object target) => new Placement(world.Position(target), world.Rotation(target));
        // The one state a modification holds for a crossing object is the placement its mod object was built at.
        public void CheckRelationships(object target, ObjectState state) {
            if (state == null) return;
            if (state.Marker != null || state.Coverage != null || state.Equipment != null || state.Starter != null || state.Segments != null || state.MarkerId != null || state.Active != null) throw new InvalidPatchDefinitionException(NoEdits);
            if (state.Placement != null && (state.Placement.Following != null || !Placements.Same(state.Placement, Placed(target)))) throw new InvalidPatchDefinitionException(NoEdits);
        }
        public object Capture(object target) => null;
        public ObjectState Read(object target) => new ObjectState(Placed(target), world.Active(target), null, null);
        public void Apply(object target, ObjectState state) {
            if (state?.Placement != null && !Placements.Same(state.Placement, Placed(target))) throw new InvalidOperationException(NoEdits);
            if (state?.Active != null && state.Active.Value != world.Active(target)) throw new InvalidOperationException(NoEdits);
        }
        // Apply changes nothing, so there is nothing to put back.
        public bool Restore(object target, object snapshot) => true;
    }
}
