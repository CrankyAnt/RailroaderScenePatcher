// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // Which scene objects are station map icons, and the texts of their family.
    public static class StationMapIconRules {
        public const string Family = "Station map icon", Component = "UI.Map.MapIcon", Structure = "station-map-icon-v1", RequiredVersion = "0.5.0";
        // The container object that holds the station map icons. Each icon in it is an entry of
        // the family, and the container object itself gets no row.
        public const string IconsPath = BakedSceneryRules.WorldRoot + "/" + BakedSceneryRules.MapLabels + "/" + BakedSceneryRules.StationIcons;
        // Whether a map icon at this scene path is a station map icon: one on a direct child of the Station Icons container object,
        // as BakedSceneryRules.Classify reads that container object. Map icons deeper in the container object, and elsewhere, such as those
        // the game creates for cars and players while it runs, are not.
        public static bool Listed(string path) =>
            path != null && path.Length > IconsPath.Length + 1 && path.StartsWith(IconsPath + "/", StringComparison.Ordinal) && path.IndexOf('/', IconsPath.Length + 1) < 0;
        public const string Operations = "Move, rotate, enable, or disable this station map icon. Edit the station's track span, house, and station agent in the Map Editor.";
        public const string Dependency = "Map window: the map shows the icon at its position. Enabling the icon registers it with the map.";
    }
    // A station map icon as the capability reads it: its own placement and activity in map coordinates, the object it
    // stands in, whether it is active with its parents, and whether the map has it registered. Registered is null while
    // the game has no map to register it with.
    public sealed class MapIconObservation {
        public MapIconObservation(ObjectState root, bool activeInHierarchy, object parent, bool? registered) {
            Root = root ?? throw new ArgumentNullException(nameof(root)); ActiveInHierarchy = activeInHierarchy; Parent = parent; Registered = registered;
        }
        public ObjectState Root { get; }
        public bool ActiveInHierarchy { get; }
        public object Parent { get; }
        public bool? Registered { get; }
    }
    // The native operations of the station map icon capability. Positions are map coordinates.
    // IDisableRefusal: why `active: false` cannot hold against a switch of the game that names the icon, or null.
    public interface IMapIconWorld : IDisableRefusal {
        IEnumerable<string> MapIds { get; }
        // The loaded objects at the scene path that carry a map icon.
        IReadOnlyList<object> Find(string scene, string path);
        // Why the icon cannot be edited, or null.
        string Refusal(object icon);
        MapIconObservation Observe(object icon);
        void SetPlacement(object icon, Placement placement);
        void SetActive(object icon, bool active);
    }
    // Station map icons: places and enables or disables one map icon below World/Map Labels/Station Icons. The map draws
    // the icon where its object stands; the icon registers with the map when it is enabled and leaves it when it is
    // disabled. Native map data, such as the station's track span, house and station agent, is not touched.
    public sealed class StationMapIconScene : IScene, IVersionedScene, IRelationshipScene, IDependentOutputScene {
        const double PositionTolerance = 0.01, AngleToleranceDegrees = 0.01;
        readonly IMapIconWorld world;
        // The scene ground masks at and below the icon object: they move with it, a mask that flattens the terrain height
        // included.
        readonly MaskFollowing masks;
        public StationMapIconScene(IMapIconWorld world, Capability capability = Capability.Unchecked, GroundMasks masks = null) {
            this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; this.masks = new MaskFollowing(masks);
        }
        public DependentOutput Output(object target) => masks.Output(target);
        public void RetryOutput(object target) => masks.RetryOutput(target);
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity == null || identity.Component != StationMapIconRules.Component || (identity.Structure != null && identity.Structure != StationMapIconRules.Structure)) return Array.Empty<object>();
            if (!StationMapIconRules.Listed(identity.Path)) return Array.Empty<object>();
            return world.Find(identity.Scene, identity.Path) ?? Array.Empty<object>();
        }
        public void Preflight(object target) {
            var refusal = world.Refusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            if (!Finite(world.Observe(target).Root.Placement)) throw new InvalidOperationException("The station map icon's map coordinates are unavailable.");
        }
        public void CheckRelationships(object target, ObjectState state) {
            if (state?.Marker != null) throw new InvalidOperationException("This object has no supported track-marker relationship.");
            if (state?.Coverage != null) throw new InvalidOperationException("This object has no supported crossing-coverage relationship.");
            if (state?.Equipment != null) throw new InvalidOperationException("This object has no supported crossing-equipment relationship.");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
            if (state?.Active == false && world.DisableRefusal(target) is string reason) throw new InvalidOperationException(reason);
        }
        public object Capture(object target) => world.Observe(target);
        public ObjectState Read(object target) => world.Observe(target).Root;
        public void Apply(object target, ObjectState state) => masks.Write(target, () => Change(target, state));
        void Change(object target, ObjectState state) {
            var before = world.Observe(target);
            if (state.Placement != null) CheckPlacement(state.Placement);
            var active = state.Active ?? before.Root.Active ?? true;
            Write(target, before, state.Placement, active);
            var difference = Verify(target, state.Placement ?? before.Root.Placement, active, before.Parent);
            if (difference != null) throw new InvalidOperationException("The station map icon did not verify: " + difference);
        }
        // Enabling comes first and disabling last, so the icon never registers with the map at a placement it leaves.
        void Write(object target, MapIconObservation before, Placement placement, bool active) {
            if (active && before.Root.Active != true) world.SetActive(target, true);
            if (placement != null) world.SetPlacement(target, placement);
            if (!active && before.Root.Active != false) world.SetActive(target, false);
        }
        // What differs from the requested state, or null: the placement, the activity, the object the icon stands in, and
        // the map's registration of an icon that is active with its parents.
        string Verify(object target, Placement placement, bool active, object parent) {
            var after = world.Observe(target);
            if (!SamePlacement(after.Root.Placement, placement)) return "its placement does not match the requested placement";
            if (after.Root.Active != active) return "its activity does not match the requested activity";
            if (!ReferenceEquals(after.Parent, parent)) return "it no longer stands in the same object";
            if (after.ActiveInHierarchy && after.Registered == false) return "the map does not have the enabled icon registered";
            return null;
        }
        public bool Restore(object target, object snapshot) => masks.Restore(target, () => Restored(target, snapshot));
        bool Restored(object target, object snapshot) {
            var captured = (MapIconObservation)snapshot;
            try {
                var current = world.Observe(target);
                if (!ReferenceEquals(current.Parent, captured.Parent)) return false;
                var root = captured.Root;
                Write(target, current, root.Placement, root.Active != false);
                return Verify(target, root.Placement, root.Active != false, captured.Parent) == null;
            } catch { return false; }
        }
        public string MinimumPatcherVersion(TargetIdentity target) => Requires(target) ? StationMapIconRules.RequiredVersion : null;
        // Whether a modification of this target needs the station map icon capability, whether or not it is available.
        public static bool Requires(TargetIdentity target) =>
            target != null && (target.Structure == StationMapIconRules.Structure || (target.Structure == null && target.Component == StationMapIconRules.Component));

        static void CheckPlacement(Placement placement) {
            if (placement.Position == null || placement.Rotation == null || placement.Position.Length != 3 || placement.Rotation.Length != 4) throw new InvalidOperationException("Invalid placement coordinates.");
            if (placement.Position.Concat(placement.Rotation).Any(v => Double.IsNaN(v) || Double.IsInfinity(v) || Math.Abs(v) > Single.MaxValue)) throw new InvalidOperationException("A coordinate exceeds the game's numeric precision.");
            if (Math.Abs(placement.Rotation.Sum(x => x * x) - 1) > 0.001) throw new InvalidOperationException("Rotation must be a unit quaternion.");
        }
        static bool Finite(Placement placement) => placement?.Position != null && placement.Position.Length == 3 && !placement.Position.Any(v => Double.IsNaN(v) || Double.IsInfinity(v));
        static bool SamePlacement(Placement a, Placement b) {
            if (a?.Position == null || b?.Position == null || a.Rotation == null || b.Rotation == null || a.Position.Length != 3 || b.Position.Length != 3 || a.Rotation.Length != 4 || b.Rotation.Length != 4) return false;
            for (var i = 0; i < 3; i++) if (Math.Abs(a.Position[i] - b.Position[i]) > PositionTolerance) return false;
            double dot = 0, la = 0, lb = 0;
            for (var i = 0; i < 4; i++) { dot += a.Rotation[i] * b.Rotation[i]; la += a.Rotation[i] * a.Rotation[i]; lb += b.Rotation[i] * b.Rotation[i]; }
            if (la <= 0 || lb <= 0) return false;
            return 2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(la * lb))) * 180 / Math.PI <= AngleToleranceDegrees;
        }
    }
}
