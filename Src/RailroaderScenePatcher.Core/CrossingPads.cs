// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

namespace RailroaderScenePatcher.Core {
    // The generated geometry of a crossing pad as the native curve-mesh builder holds it.
    public sealed class PadGeometry {
        public PadGeometry(string signature, TrackLocation source, double[] origin, bool cullingAligned, string curves = null, string curveToken = null) {
            Signature = signature; Source = source; Origin = origin; CullingAligned = cullingAligned; Curves = curves; CurveToken = curveToken;
        }
        // A content signature of the generated mesh; null when the builder holds no generated geometry.
        public string Signature { get; }
        public bool Generated => Signature != null;
        // The marker location and the pad's map position the current geometry was generated from; null when unknown,
        // such as geometry the game generated before the adapter observed the pad.
        public TrackLocation Source { get; }
        public IReadOnlyList<double> Origin { get; }
        // False when the registered culling sphere does not enclose the generated bounds as native generation sets it.
        public bool CullingAligned { get; }
        // The fingerprint of the source curves the current geometry was generated from; null when unknown.
        public string Curves { get; }
        // The track token that last confirmed those curves; null when unknown.
        public string CurveToken { get; }
    }
    // The game-facing operations of the crossing-pad capability: the shared track-marker operations plus the
    // native curve-mesh builder that generates the pad's geometry from the marker location.
    public interface ICrossingPadWorld : ITrackMarkerWorld {
        PadGeometry Geometry(object pad);
        // Runs the native generation from the current marker location and pad position, then lets the native culling
        // recompute the visibility of the generated renderers.
        void Generate(object pad);
        // A fingerprint of the track curves the native builder reads for the current marker location: the segment curves
        // within half the pad width on either side. Null when the location does not resolve.
        string SourceCurves(object pad);
        // A cheap token of the track around the marker location, without building the builder's curves: the raw curves of the
        // segments within reach and the segments connected at their nodes. An unchanged token means unchanged source curves.
        string CurveToken(object pad);
    }
    // A scene whose objects derive state from their desired state beyond their pose, such as a track-following pad's
    // geometry generated from the track's curves. Track-following reconciliation applies the desired state when either differs.
    // A scene that can confirm, without a scene-wide search, that a previously resolved object is still the object its
    // identity names. Uniqueness is not checked again; a mutation still resolves and preflights first.
    public interface ITargetConfirmation {
        bool StillIdentifies(object target, TargetIdentity identity);
    }
    // A world that can check its pattern for an update that changes only the pose of an object, without the consumer
    // scans of the full check. Placement does not touch the relationship those scans guard.
    public interface IPlacementPattern {
        string PlacementProblem(object target);
    }
    // A scene that can preflight a pose-only update of a held object without scene-wide work.
    public interface IPlacementPreflight {
        void PreflightPlacement(object target);
    }
    // A scene that applies a track-following update, which changes the object's pose or derived state, without the
    // follow-up work an explicit edit needs, such as reconciling a native warning that only a binding or activity change affects.
    public interface IFollowUpdateScene {
        void ApplyFollowUpdate(object target, ObjectState state);
    }
    public interface IDerivedStateScene {
        // Why the object's derived state disagrees with the desired state, or null.
        string DerivedProblem(object target, ObjectState desired);
    }
    // Rebinds the track-marker location of a crossing pad with its graph registration and regenerates the pad's
    // geometry and culling from the authored location. A pad without a track location has no geometry, so a
    // relationship must name a location; disabling the pad is the way to hide it. Placement and activity stay
    // separate settings; the generated geometry follows the track, not the pad object.
    public sealed class CrossingPadScene : IScene, IRelationshipScene, IVersionedScene, IDerivedStateScene, ITargetConfirmation, IPlacementPreflight, IFollowUpdateScene {
        public const string Structure = "crossing-pad-v1", RequiredVersion = "0.5.0";
        readonly MarkerRelationship relationship;
        readonly ICrossingPadWorld world;
        // The changed source curves a reconciliation saw last for a pad, until they hold still for one more check.
        readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, string> unsettled = new System.Runtime.CompilerServices.ConditionalWeakTable<object, string>();
        public CrossingPadScene(ICrossingPadWorld world, Capability capability) {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            relationship = new MarkerRelationship(world, Structure, new GeneratedGeometry(world));
            Capability = capability;
        }
        public IEnumerable<string> MapIds => relationship.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) => relationship.Resolve(identity);
        public string MinimumPatcherVersion(TargetIdentity target) => target.Structure == Structure ? RequiredVersion : null;
        public void Preflight(object target) => relationship.Preflight(target);
        public void CheckRelationships(object target, ObjectState state) {
            if (state?.Marker != null && state.Marker.IsDetached)
                throw new InvalidPatchDefinitionException("A crossing pad cannot be detached: its geometry is generated from its track location. Bind it to a location, or disable the pad to hide it");
            relationship.CheckRelationships(target, state);
        }
        public object Capture(object target) => relationship.Capture(target);
        public ObjectState Read(object target) => relationship.Read(target);
        public void Apply(object target, ObjectState state) => relationship.Apply(target, state);
        public bool StillIdentifies(object target, TargetIdentity identity) => relationship.StillIdentifies(target, identity);
        public void PreflightPlacement(object target) => relationship.PreflightPlacement(target);
        // During a native track drag the curves change on every check. A curve-only change is reported once the changed
        // curves have held still between two checks, so one move regenerates the pad once. Any other difference is reported at once.
        public void ApplyFollowUpdate(object target, ObjectState state) => relationship.Apply(target, state, true);
        public string DerivedProblem(object target, ObjectState desired) {
            var problem = relationship.DerivedProblem(target, desired);
            if (problem != GeneratedGeometry.CurvesChanged) { unsettled.Remove(target); return problem; }
            var curves = world.SourceCurves(target);
            if (unsettled.TryGetValue(target, out var seen) && seen == curves) return problem;
            unsettled.Remove(target);
            unsettled.Add(target, curves);
            return null;
        }
        public bool Restore(object target, object snapshot) => relationship.Restore(target, snapshot);
        // The pad geometry follows the marker location and the pad position it was generated from.
        sealed class GeneratedGeometry : IMarkerConsumer {
            internal const string CurvesChanged = "the track curves the pad follows have changed since its geometry was generated";
            readonly ICrossingPadWorld world;
            internal GeneratedGeometry(ICrossingPadWorld world) { this.world = world; }
            public object Capture(object target) => world.Geometry(target);
            public void Refresh(object target, bool resolvable, bool following, bool explicitApply) { if (Problem(target, resolvable, following, explicitApply) != null) world.Generate(target); }
            // A pad that follows a track is also regenerated when the curves the builder reads have changed, such as after
            // a track edit or its native Undo that leaves the pad's pose unchanged. A pad that is only rebound keeps the
            // native behavior: the game does not regenerate it after a track edit.
            // An explicit application, such as an edit, Undo or Remove, also regenerates a pad whose recorded source curves
            // differ from the current track, so it never leaves a mesh built from an earlier track shape.
            public string Problem(object target, bool resolvable, bool following, bool explicitApply) {
                var geometry = world.Geometry(target) ?? throw new InvalidOperationException("The pad geometry is unavailable");
                if (!resolvable) return geometry.Generated ? "the pad holds generated geometry although its marker location does not resolve" : null;
                var location = world.Location(target);
                if (!geometry.Generated) return "the pad has no generated geometry for its marker location " + location;
                if (!TrackLocation.Same(geometry.Source, location)) return "the pad geometry was generated from " + (geometry.Source?.ToString() ?? "an unknown location") + " instead of " + location;
                if (!MarkerRelationship.Near(geometry.Origin, world.Position(target))) return "the pad geometry was generated at another pad position";
                if (!geometry.CullingAligned) return "the pad's culling sphere does not follow its generated geometry";
                var checkCurves = following || (explicitApply && geometry.Curves != null);
                if (checkCurves && (geometry.Curves == null || CurvesDiffer(target, geometry))) return CurvesChanged;
                return null;
            }
            // A known, equal token confirms the recorded curves without building them; an unknown token never confirms.
            bool CurvesDiffer(object target, PadGeometry geometry) {
                var token = world.CurveToken(target);
                if (token != null && geometry.CurveToken != null && token == geometry.CurveToken) return false;
                return geometry.Curves != world.SourceCurves(target);
            }
            public string Restored(object target, object captured) {
                var before = (PadGeometry)captured;
                var after = world.Geometry(target);
                if (before.Generated && after?.Signature != before.Signature) return "the regenerated pad geometry " + (after?.Signature ?? "none") + " differs from the captured geometry " + before.Signature;
                return null;
            }
        }
    }
}
