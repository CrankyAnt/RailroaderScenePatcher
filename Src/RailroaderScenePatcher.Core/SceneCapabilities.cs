// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RailroaderScenePatcher.Core {
    // The scene of one load, assembled from independent capabilities. Each resolved object is operated on by
    // the capability that resolved it. A capability that is incompatible with the running game build fails
    // only the modifications that name its structure; unrelated supported work stays available.
    // A scene that can tell, without searching, whether an object resolved earlier is still the object of its identity.
    public interface ITargetLiveness {
        bool IsLive(object target, TargetIdentity identity);
    }
    public sealed class SceneCapabilities : IPadMarkerIdentityScene, IConstructionSegmentScene, IMarkerIdScene, IScene, IInspectionScene, IDependentOutputScene, IVersionedScene, IRelationshipScene, ITrackPoseScene, IDerivedStateScene, ITargetConfirmation, IPlacementPreflight, IFollowUpdateScene, IRetainedScene, ITargetLiveness, ITargetedInspectionScene, IPlannedStateScene, ICrossingEquipmentScene, IFramedScene, IMapPatchContext, IDisposable {
        // Supplied by the host: the map patches in the map context, which only the native map editor's context holds.
        public Func<IEnumerable<string>> MapPatches { get; set; }
        public IEnumerable<string> MapPatchIds => MapPatches?.Invoke() ?? Enumerable.Empty<string>();
        // The frame belongs to the adapter that resolved the object; an adapter without frames reports none.
        public Placement FramePlacement(object target) => target != null && owners.TryGetValue(target, out var owner) && owner is IFramedScene framed ? framed.FramePlacement(target) : null;
        // The observation of one object, when the inspection source can make it.
        public SceneObservation Observe(object handle, TargetIdentity identity) {
            return inspection is ITargetedInspectionScene targeted ? targeted.Observe(handle, identity) : inspection?.Discover().FirstOrDefault(o => ReferenceEquals(o.Handle, handle) && o.Identity.Component == identity.Component);
        }
        // Supplied by the host: whether the object still exists at the identity's scene and path.
        public Func<object, TargetIdentity, bool> Live { get; set; }
        public bool IsLive(object target, TargetIdentity identity) { return Live != null && target != null && Live(target, identity); }
        sealed class ByReference : IEqualityComparer<object> {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }
        sealed class Unavailable {
            internal string Structure, Reason;
        }
        readonly Func<IEnumerable<string>> mapIds;
        readonly IInspectionScene inspection;
        readonly List<IScene> capabilities = new List<IScene>();
        readonly Dictionary<string, Unavailable> unavailable = new Dictionary<string, Unavailable>(StringComparer.Ordinal);
        readonly Dictionary<object, IScene> owners = new Dictionary<object, IScene>(new ByReference());
        public SceneCapabilities(Func<IEnumerable<string>> mapIds, IInspectionScene inspection = null) {
            this.mapIds = mapIds ?? throw new ArgumentNullException(nameof(mapIds)); this.inspection = inspection;
        }
        // The native track graph, for track-following placement of any capability's objects. Null leaves the mode unavailable.
        public ITrackPoseScene Tracks { get; set; }
        public TrackPose TrackPose(TrackAnchor anchor) => (Tracks ?? throw new InvalidOperationException("Track-following placement is unavailable: the track graph is not available")).TrackPose(anchor);
        public void Add(IScene capability) { capabilities.Add(capability ?? throw new ArgumentNullException(nameof(capability))); }
        // Records a capability whose required game functions are missing. Targets naming its structure fail with the reason.
        public void AddUnavailable(string structure, string reason) { unavailable[structure] = new Unavailable { Structure = structure, Reason = reason }; }
        public IEnumerable<string> MapIds => mapIds();
        public Capability Capability {
            get {
                var available = capabilities.Where(c => c.Capability != Capability.Incompatible).ToList();
                if (available.Count == 0) return Capability.Incompatible;
                return available.All(c => c.Capability == Capability.Checked) ? Capability.Checked : Capability.Unchecked;
            }
        }
        public IEnumerable<SceneObservation> Discover() => inspection == null ? Enumerable.Empty<SceneObservation>() : inspection.Discover();
        // A pad marker ID is resolved only by a capability that accepts it, so no other capability matches the path alone.
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            var found = new List<object>();
            foreach (var capability in capabilities) {
                if (identity.PadMarkerId != null && !(capability is IPadMarkerIdentityScene)) continue;
                foreach (var item in capability.Resolve(identity) ?? Array.Empty<object>()) {
                    // An object claimed by two capabilities appears twice and stays ambiguous.
                    if (item != null) owners[item] = capability;
                    found.Add(item);
                }
            }
            if (identity.Structure != null && unavailable.TryGetValue(identity.Structure, out var missing)) found.Add(missing);
            return found;
        }
        // The structure picks the capability that judges a pad marker ID; without one, the first that accepts the identity.
        public string PadMarkerRefusal(TargetIdentity identity) {
            if (identity?.PadMarkerId == null) return null;
            var accepting = capabilities.OfType<IPadMarkerIdentityScene>().ToArray();
            if (accepting.Length == 0) return TargetIdentity.PadMarkerNotAccepted;
            var refusals = accepting.Select(c => c.PadMarkerRefusal(identity)).ToArray();
            return refusals.Any(r => r == null) ? null : refusals.FirstOrDefault(r => r != TargetIdentity.PadMarkerNotAccepted) ?? TargetIdentity.PadMarkerNotAccepted;
        }
        IScene Owner(object target) {
            if (target is Unavailable missing) throw new InvalidOperationException("The patcher cannot edit this object on this game build. Report this to the patcher maintainer. Details: " + missing.Reason);
            if (target == null || !owners.TryGetValue(target, out var owner)) throw new InvalidOperationException("The patcher could not find this object");
            if (owner.Capability == Capability.Incompatible) throw new InvalidOperationException("The patcher cannot edit this object on this game build. Report this to the patcher maintainer");
            return owner;
        }
        public void Preflight(object target) { Owner(target).Preflight(target); }
        // The adapter that resolved the object releases its own state for it; the object then has no owner.
        public void Release(object target) {
            if (target == null || !owners.TryGetValue(target, out var owner)) return;
            (owner as IRetainedScene)?.Release(target);
            owners.Remove(target);
        }
        // Ends every capability that holds state over the scene, such as owned crossing spans, when the patcher state ends.
        public void Dispose() {
            foreach (var capability in capabilities.OfType<IDisposable>().ToArray()) { try { capability.Dispose(); } catch { } }
            owners.Clear();
        }
        public bool Unsettled(object target) => target != null && owners.TryGetValue(target, out var owner) && owner is IRetainedScene retained && retained.Unsettled(target);
        // Throws the incompatibility of a capability whose game functions are missing, when the target is its placeholder.
        internal void CheckAvailable(object target) { if (target is Unavailable) Owner(target); }
        public object Capture(object target) => Owner(target).Capture(target);
        public ObjectState Read(object target) => Owner(target).Read(target);
        public IReadOnlyList<string> MarkerIdUsers(object target, string id) => Owner(target) is IMarkerIdScene ids ? ids.MarkerIdUsers(target, id) : Array.Empty<string>();
        public bool RewritesRegistration(object target, ObjectState state) => Owner(target) is IMarkerIdScene ids && ids.RewritesRegistration(target, state);
        public string MarkerIdRefusal(object target, string id) => Owner(target) is IMarkerIdScene ids ? ids.MarkerIdRefusal(target, id) : ReplacementMarkerIds.NotSupported;
        public void Apply(object target, ObjectState state) { Owner(target).Apply(target, state); }
        public bool Restore(object target, object snapshot) => Owner(target).Restore(target, snapshot);
        // Relationships belong to the adapter that resolved the object; an adapter without them refuses any relationship.
        public void CheckRelationships(object target, ObjectState state) {
            var owner = Owner(target);
            // Only a capability that holds the relationship accepts construction segments.
            if (state?.Segments != null && !(owner is IConstructionSegmentScene)) throw new InvalidPatchDefinitionException(ConstructionSegmentRule.NoRelationship);
            // Only the generic track markers take a replacement ID.
            if (state?.MarkerId != null && !(owner is IMarkerIdScene)) throw new InvalidPatchDefinitionException(ReplacementMarkerIds.NotSupported);
            if (owner is IRelationshipScene relationships) { relationships.CheckRelationships(target, state); return; }
            if (state?.Marker != null) throw new InvalidOperationException("This object has no supported track-marker relationship");
            if (state?.Coverage != null) throw new InvalidOperationException("This object has no supported crossing-coverage relationship");
            if (state?.Equipment != null) throw new InvalidOperationException("This object has no supported crossing-equipment relationship");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
        }
        // Equipment sources and additions belong to the adapter that resolved the object.
        public IReadOnlyList<SourceCandidate> EquipmentCandidates(object target) => Owner(target) is ICrossingEquipmentScene equipment ? equipment.EquipmentCandidates(target) : throw new InvalidOperationException("This object has no supported crossing-equipment relationship");
        public string EquipmentStatus(object target, string scope, string id) => Owner(target) is ICrossingEquipmentScene equipment ? equipment.EquipmentStatus(target, scope, id) : null;
        public bool EquipmentLeftover(object target, ObjectState desired) => Owner(target) is ICrossingEquipmentScene equipment && equipment.EquipmentLeftover(target, desired);
        // Only the adapter that resolved the object can confirm it; an object no adapter resolved is not confirmed.
        public bool StillIdentifies(object target, TargetIdentity identity) =>
            target != null && owners.TryGetValue(target, out var owner) && owner.Capability != Capability.Incompatible && owner is ITargetConfirmation confirmation && confirmation.StillIdentifies(target, identity);
        // An adapter without a follow update applies the state as an edit.
        public void ApplyFollowUpdate(object target, ObjectState state) { var owner = Owner(target); if (owner is IFollowUpdateScene follow) follow.ApplyFollowUpdate(target, state); else owner.Apply(target, state); }
        // An adapter without a placement preflight takes its full preflight.
        public void PreflightPlacement(object target) { var owner = Owner(target); if (owner is IPlacementPreflight placement) placement.PreflightPlacement(target); else owner.Preflight(target); }
        // Derived state belongs to the adapter that resolved the object; an adapter without it reports none.
        public string DerivedProblem(object target, ObjectState desired) => Owner(target) is IDerivedStateScene derived ? derived.DerivedProblem(target, desired) : null;
        // Dependent output belongs to the adapter that resolved the object; an adapter without it reports none.
        public DependentOutput Output(object target) {
            if (target == null || target is Unavailable || !owners.TryGetValue(target, out var owner) || !(owner is IDependentOutputScene dependent)) return DependentOutput.None;
            return dependent.Output(target) ?? DependentOutput.None;
        }
        public void RetryOutput(object target) { if (Owner(target) is IDependentOutputScene dependent) dependent.RetryOutput(target); }
        // Every adapter that judges a modification with its scene patch file receives the desired states of the objects it resolved.
        public void Plan(IReadOnlyDictionary<object, ObjectState> desired) {
            foreach (var adapter in capabilities.OfType<IPlannedStateScene>())
                adapter.Plan(desired?.Where(pair => pair.Key != null && owners.TryGetValue(pair.Key, out var owner) && ReferenceEquals(owner, adapter)).ToDictionary(pair => pair.Key, pair => pair.Value, new ByReference()));
        }
        // The cut the starter placement capability that resolved the object would place at the location; empty for any other object.
        public IReadOnlyList<double[]> StarterCut(object target, TrackLocation location) =>
            Owner(target) is StarterPlacementScene starter ? starter.CutPath(target, location) : Array.Empty<double[]>();
        // The highest version any adapter requires for the target.
        public string MinimumPatcherVersion(TargetIdentity target) {
            string minimum = null;
            foreach (var versioned in capabilities.OfType<IVersionedScene>()) {
                var required = versioned.MinimumPatcherVersion(target);
                if (required != null && (minimum == null || DependencyMetadata.Number(required) > DependencyMetadata.Number(minimum))) minimum = required;
            }
            return minimum;
        }
    }
}
