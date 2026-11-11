// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // One collider in a bridge hierarchy, observed with copied values.
    public sealed class ColliderState {
        public ColliderState(string id, string shape, bool enabled, bool chainActive, bool activeInHierarchy,
            IEnumerable<double> rootPosition, IEnumerable<double> rootRotation, bool boundsFollow) {
            Id = id ?? throw new ArgumentNullException(nameof(id)); Shape = shape ?? ""; Enabled = enabled; ChainActive = chainActive;
            ActiveInHierarchy = activeInHierarchy; RootPosition = rootPosition.ToArray(); RootRotation = rootRotation.ToArray(); BoundsFollow = boundsFollow;
        }
        // Stable for the loaded object: hierarchy path below the bridge root, collider type and instance.
        public string Id { get; }
        // Type and shape properties that a pose or activity change must not alter.
        public string Shape { get; }
        public bool Enabled { get; }
        // Every object from the collider up to, but excluding, the bridge root is active.
        public bool ChainActive { get; }
        public bool ActiveInHierarchy { get; }
        // Pose relative to the bridge root.
        public IReadOnlyList<double> RootPosition { get; }
        public IReadOnlyList<double> RootRotation { get; }
        // The physics shape agrees with the collider's transform after synchronisation.
        public bool BoundsFollow { get; }
    }
    // A bridge root, its colliders and a description of the separate track-owned scenery beside it.
    public sealed class BridgeObservation {
        public BridgeObservation(ObjectState root, bool activeInHierarchy, IEnumerable<ColliderState> colliders, string separateScenery) {
            Root = root ?? throw new ArgumentNullException(nameof(root)); ActiveInHierarchy = activeInHierarchy;
            Colliders = colliders.ToArray(); SeparateScenery = separateScenery ?? "";
        }
        public ObjectState Root { get; }
        public bool ActiveInHierarchy { get; }
        public IReadOnlyList<ColliderState> Colliders { get; }
        public string SeparateScenery { get; }
    }
    // The game-facing operations of the bridge capability. Positions are map coordinates.
    public interface IBridgeWorld {
        IEnumerable<string> MapIds { get; }
        IReadOnlyList<object> Resolve(TargetIdentity identity);
        // Throws when the object does not conform to the supported bridge pattern.
        void CheckPattern(object bridge);
        BridgeObservation Observe(object bridge);
        void SetActive(object bridge, bool active);
        // Sets the root pose and refreshes the dependent culling and physics state.
        void SetPose(object bridge, Placement placement);
    }
    // Places and enables or disables a bridge root together with its colliders. Separate track-owned
    // scenery, such as generated trestles, is never part of the moved hierarchy and must stay unchanged.
    public sealed class BridgeScene : IScene {
        public const string Structure = "bridge-colliders-v1";
        const double PositionTolerance = 0.01, AngleToleranceDegrees = 0.01;
        readonly IBridgeWorld world;
        // The collider layout first observed for each bridge in this load: the invariant every application keeps.
        readonly Dictionary<object, BridgeObservation> reference = new Dictionary<object, BridgeObservation>();
        public BridgeScene(IBridgeWorld world, Capability capability) { this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Structure != null && identity.Structure != Structure) return Array.Empty<object>();
            return world.Resolve(identity) ?? Array.Empty<object>();
        }
        BridgeObservation Reference(object bridge) {
            if (!reference.TryGetValue(bridge, out var value)) reference.Add(bridge, value = world.Observe(bridge));
            return value;
        }
        public void Preflight(object target) {
            world.CheckPattern(target);
            var current = world.Observe(target);
            if (current.Colliders.Count == 0) throw new InvalidOperationException("The bridge has no colliders; this is not the supported bridge pattern");
            var difference = Layout(Reference(target), current);
            if (difference != null) throw new InvalidOperationException("The bridge's collider state differs from its first observation in this load: " + difference + ". Reload before editing it");
        }
        public object Capture(object target) { Reference(target); return world.Observe(target); }
        public ObjectState Read(object target) => world.Observe(target).Root;
        public void Apply(object target, ObjectState state) {
            var expected = Reference(target);
            var before = world.Observe(target);
            var active = state.Active ?? before.Root.Active ?? true;
            if (active && before.Root.Active != true) world.SetActive(target, true);
            if (state.Placement != null) world.SetPose(target, state.Placement);
            if (!active && before.Root.Active != false) world.SetActive(target, false);
            var after = world.Observe(target);
            var desired = new ObjectState(state.Placement ?? before.Root.Placement, active);
            if (!SamePose(after.Root, desired) || after.Root.Active != active) throw new InvalidOperationException("Applied bridge pose or activity did not verify");
            var difference = Layout(expected, after) ?? Activity(before, after) ?? (after.SeparateScenery == before.SeparateScenery ? null : "separate track-owned scenery changed");
            if (difference != null) throw new InvalidOperationException("Bridge collider state did not verify: " + difference);
        }
        public bool Restore(object target, object snapshot) {
            var captured = (BridgeObservation)snapshot;
            try {
                var expected = Reference(target);
                var root = captured.Root;
                if (root.Active == true) world.SetActive(target, true);
                if (root.Placement != null) world.SetPose(target, root.Placement);
                if (root.Active == false) world.SetActive(target, false);
                var current = world.Observe(target);
                return SamePose(current.Root, root) && current.Root.Active == root.Active && current.ActiveInHierarchy == captured.ActiveInHierarchy
                    && Layout(expected, current) == null && Activity(captured, current) == null && current.SeparateScenery == captured.SeparateScenery;
            } catch { return false; }
        }
        // Collider identities, shapes, enabled flags, root-relative poses and physics agreement.
        static string Layout(BridgeObservation expected, BridgeObservation current) {
            if (!expected.Colliders.Select(c => c.Id).SequenceEqual(current.Colliders.Select(c => c.Id))) return "collider identities changed";
            for (var i = 0; i < expected.Colliders.Count; i++) {
                var a = expected.Colliders[i]; var b = current.Colliders[i];
                if (a.Shape != b.Shape) return "shape of " + b.Id + " changed";
                if (a.Enabled != b.Enabled) return "enabled state of " + b.Id + " changed";
                if (!Near(a.RootPosition, b.RootPosition) || !SameRotation(a.RootRotation, b.RootRotation)) return b.Id + " no longer follows the bridge root";
                if (!b.BoundsFollow) return "physics shape of " + b.Id + " does not follow its transform";
            }
            return null;
        }
        // Collider activity follows the root; the activity below the root belongs to native construction control.
        static string Activity(BridgeObservation before, BridgeObservation after) {
            for (var i = 0; i < after.Colliders.Count && i < before.Colliders.Count; i++) {
                var a = before.Colliders[i]; var b = after.Colliders[i];
                if (a.ChainActive != b.ChainActive) return "activity below the bridge root changed for " + b.Id;
                if (b.ActiveInHierarchy != (after.ActiveInHierarchy && b.ChainActive)) return "activity of " + b.Id + " does not follow the bridge root";
            }
            return null;
        }
        static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++) if (Math.Abs(a[i] - b[i]) > PositionTolerance) return false;
            return true;
        }
        static bool SameRotation(IReadOnlyList<double> a, IReadOnlyList<double> b) {
            if (a == null || b == null || a.Count != 4 || b.Count != 4) return false;
            double dot = 0, la = 0, lb = 0;
            for (var i = 0; i < 4; i++) { dot += a[i] * b[i]; la += a[i] * a[i]; lb += b[i] * b[i]; }
            if (la <= 0 || lb <= 0) return false;
            var angle = 2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(la * lb))) * 180 / Math.PI;
            return angle <= AngleToleranceDegrees;
        }
        static bool SamePose(ObjectState a, ObjectState b) =>
            a.Placement != null && b.Placement != null && Near(a.Placement.Position, b.Placement.Position) && SameRotation(a.Placement.Rotation, b.Placement.Rotation);
    }
}
