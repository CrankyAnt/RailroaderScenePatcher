// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // One loaded scene object as the baked scenery rule reads it: its name, the components on it, and its children.
    public interface IHierarchyNode {
        string Name { get; }
        // Full type names of the components on this object itself.
        IReadOnlyList<string> Components { get; }
        // A renderer, collider or light on this object itself.
        bool Physical { get; }
        // False for an object the game created while running, which the scene does not serialize.
        bool Serialized { get; }
        // An existing inventory family lists a component of this object.
        bool HasFamily { get; }
        IReadOnlyList<IHierarchyNode> Children { get; }
    }
    public enum GroupDisposition { Group, InspectionOnly, ExistingFamily, NotListed }
    // What the rule decided for one candidate: a group root of the Baked scenery group family, a group root that a later
    // ticket covers, an object an existing family lists, or an object a coverage rule keeps out.
    public sealed class GroupRootVerdict {
        internal GroupRootVerdict(IHierarchyNode node, string path, GroupDisposition disposition, string reason, string ticket = null, bool sentence = false) {
            Node = node; Path = path; Disposition = disposition; Reason = reason; Ticket = ticket; ReasonIsSentence = sentence;
        }
        // The reason of an inspection-only group is a whole clause, not the kind of group the patcher has no edit for.
        public bool ReasonIsSentence { get; }
        // The ticket that covers an inspection-only group, such as "#89"; null otherwise.
        public string Ticket { get; }
        public IHierarchyNode Node { get; }
        public string Path { get; }
        public GroupDisposition Disposition { get; }
        // Why the group is inspection only, or which coverage rule keeps it out; null for a listed group.
        public string Reason { get; }
    }
    // Which scene objects below World are the roots of baked scenery groups.
    public static class BakedSceneryRules {
        public const string Family = "Baked scenery group";
        public const string WorldRoot = "World", LargeScenery = "Large Scenery";
        public const string MapLabels = "Map Labels", StationIcons = "Station Icons", BuilderPhotoSetup = "Builder Photo Setup";
        // The coverage rules that keep a candidate out, by the name of their row in the coverage statement.
        public const string NoPhysicalPresence = "Objects without physical presence", RuntimeObject = "Objects the game creates while running",
            NativeMapData = "Track spans, passenger stops, industries, spawns, turntables and other native map data", MapLabel = "Map labels",
            BuilderPhotoRig = "Builder photo rig";
        // The renderers of visual effects, which draw particles that nothing can run into: a particle system and a VFX
        // Graph effect, such as the fireflies under World/Environment Effects.
        public static readonly IReadOnlyList<string> EffectRenderers = Array.AsReadOnly(new[] { "UnityEngine.ParticleSystemRenderer", "UnityEngine.VFX.VFXRenderer" });
        // Whether a component gives its object physical presence: a collider, a light, or a renderer that is no effect's.
        public static bool GivesPresence(string type, bool renderer, bool collider, bool light) => collider || light || (renderer && !EffectRenderers.Contains(type));
        // A component the game spawns from Map.json carries; the native map editor edits such an object.
        public const string MapObject = "Game.MapObjectActivation";
        // Components of a World category folder whose children the game builds while it runs: track geometry with its
        // trestles and pads, objects spawned from map data, and telegraph poles.
        public static readonly IReadOnlyList<string> Generators = Array.AsReadOnly(new[] { "Track.TrackObjectManager", "Game.MapLoader", "TelegraphPoles.TelegraphPoleManager" });
        // Every candidate below World with what the rule decided for it. A root that is not World holds no group roots.
        public static IReadOnlyList<GroupRootVerdict> Classify(IHierarchyNode world) {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var verdicts = new List<GroupRootVerdict>();
            if (world.Name != WorldRoot) return verdicts;
            foreach (var category in world.Children) {
                var categoryPath = WorldRoot + "/" + category.Name;
                string rule = !category.Serialized || category.Components.Any(Generators.Contains) ? RuntimeObject : category.Name == BuilderPhotoSetup ? BuilderPhotoRig : null;
                foreach (var (town, root, path) in Roots(category, categoryPath)) {
                    if (rule != null) verdicts.Add(new GroupRootVerdict(root, path, GroupDisposition.NotListed, rule));
                    else if (category.Name == MapLabels && root.Name != StationIcons) verdicts.Add(new GroupRootVerdict(root, path, GroupDisposition.NotListed, MapLabel));
                    else Candidate(town, root, path, verdicts);
                }
            }
            return verdicts;
        }
        // The scene roots whose objects are kept out whatever their family. CTC and CTC Auxiliary hold signals, CTC and
        // interlocking; the others hold game logic, native operations data or editor helpers.
        public static readonly IReadOnlyList<string> KeptOutRoots = Array.AsReadOnly(new[] { "CTC", "CTC Auxiliary", "Setups", "Ops", "MapFeatures", "Progressions", "MapManager", "Editor Directional Light" });
        public const string Setups = "Setups";
        // Whether an object below a scene root of this name can be listed at all. A track marker below Setups is a starter
        // placement marker and keeps its family.
        public static bool ListedBelow(string sceneRoot, bool trackMarker) => !KeptOutRoots.Contains(sceneRoot) || (trackMarker && sceneRoot == Setups);
        // A span the game builds from the map's operations data is not serialized and not listed; the spans the patcher
        // adds for a crossing are listed.
        public static bool SpanListed(bool serialized, bool patcherOwned) => serialized || patcherOwned;
        // The objects one level below a category folder, and below World/Large Scenery one level below a folder there, with
        // the name of that folder below World/Large Scenery.
        static IEnumerable<(string, IHierarchyNode, string)> Roots(IHierarchyNode category, string categoryPath) {
            if (category.Name != LargeScenery) { foreach (var root in category.Children) yield return (null, root, categoryPath + "/" + root.Name); yield break; }
            foreach (var town in category.Children)
                foreach (var root in town.Children) yield return (town.Name, root, categoryPath + "/" + town.Name + "/" + root.Name);
        }
        // A component anywhere in a group that a later ticket covers, with the kind it stands for and that ticket.
        public sealed class Exclusion {
            internal Exclusion(string component, string kind, string ticket, bool prefix = false, string reason = null) { Component = component; Kind = kind; Ticket = ticket; Prefix = prefix; Reason = reason; }
            // The whole reason, where the group is left out for another cause than a kind the patcher has no edit for.
            public string Reason { get; }
            public string Component { get; }
            public string Kind { get; }
            public string Ticket { get; }
            // The component names every type whose full name starts with it.
            public bool Prefix { get; }
            internal bool Matches(string type) => Prefix ? type.StartsWith(Component, StringComparison.Ordinal) : type == Component;
        }
        public static readonly IReadOnlyList<Exclusion> Exclusions = Array.AsReadOnly(new[] {
            new Exclusion("Map.Runtime.MaskComponents.", "scene ground mask", "#90", true),
            new Exclusion("Track.TrackMarker", "track marker", null, reason: TrackMarkerReason),
            new Exclusion("Model.StationAgent", "station click spot", "#89"),
            new Exclusion("RollingStock.CarContentSwapper", "car loader", "#92"),
            new Exclusion("LakePolygon", "lake surface", "#93"),
            new Exclusion("Scenery.LogPond", "log pond", "#93"),
            new Exclusion("Scenery.Pile", "storage pile", "#98"),
            new Exclusion("Scenery.BulkIndustryStorageSynchronizer", "industry storage display", "#98"),
            new Exclusion("Game.Mow.TrackConstructionScenery", "bridge construction scenery", "#95"),
            new Exclusion("Character.SpawnPoint", "spawn point", "#94"),
            new Exclusion("UI.Map.MapIcon", "map icon", "#89"),
            new Exclusion("RollingStock.Controls.KeyValuePickableToggle", "pickable toggle", "#91"),
        });
        // Why a group with track markers is left out: the markers keep their own family. No ticket covers it yet.
        public const string TrackMarkerReason = "the group holds track markers, which keep their own family, and moving such a group as a whole is not supported yet";
        // The folders below World/Large Scenery that hold the parts of a rail bridge construction outside Ela.
        public static readonly IReadOnlyList<string> RailBridgeFolders = Array.AsReadOnly(new[] { "Almond Bridge", "Plate Girder Bridge" });
        // The bridge roots that a later bridge ticket names: road bridges, and rail bridge constructions outside Ela, whose
        // parts stand in a folder of their own below World/Large Scenery. town is that folder's name, or null outside it.
        public static string BridgeTicket(string town, string name) {
            if (name == null) return null;
            if (name.IndexOf("Road Bridge", StringComparison.Ordinal) >= 0) return "#97";
            if (RailBridgeFolders.Contains(town) || name == "Bridge 1" || name.StartsWith("Plate Girder", StringComparison.Ordinal)) return "#95";
            return null;
        }
        public const string CrossingController = "Track.Crossings.GradeCrossingController", PadBuilder = "Track.TrackCurveMeshBuilder", Marker = "Track.TrackMarker",
            RendererCuller = "Helpers.Culling.RendererCuller", FolderRule = "Folders above group roots";
        public const string CrossbuckTicket = "#107";
        static bool Protected(IHierarchyNode node) => node.Components.Contains(CrossingController);
        static bool Pad(IHierarchyNode node) => node.Components.Contains(PadBuilder);
        static bool MarkerObject(IHierarchyNode node) => node.Components.Contains(Marker);
        // A crossbuck carries its own renderer culler and no component of an existing family.
        static bool Crossbuck(IHierarchyNode node) => !node.HasFamily && node.Components.Contains(RendererCuller);
        // An unprotected crossing: one or more crossing pads, and otherwise only whistle signs and crossbucks.
        static bool Unprotected(IHierarchyNode node) =>
            node.Children.Any(Pad) && node.Children.All(child => Pad(child) || MarkerObject(child) || Crossbuck(child));
        static bool Crossing(IHierarchyNode node) => Protected(node) || Unprotected(node);
        static bool RoadBridge(IHierarchyNode node) => BridgeTicket(null, node.Name) == "#97";
        // One or more crossings, or crossings and road bridges.
        static bool CrossingFolder(IHierarchyNode node) =>
            !node.HasFamily && node.Children.Any(Crossing) && node.Children.All(child => Crossing(child) || RoadBridge(child));
        static void Candidate(string town, IHierarchyNode node, string path, List<GroupRootVerdict> verdicts) {
            if (!node.Serialized) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.NotListed, RuntimeObject)); return; }
            if (node.Components.Contains(MapObject)) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.NotListed, NativeMapData)); return; }
            if (node.HasFamily) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.ExistingFamily, null)); return; }
            if (CrossingFolder(node)) { foreach (var child in node.Children) Candidate(town, child, path + "/" + child.Name, verdicts); return; }
            if (Unprotected(node)) {
                foreach (var child in node.Children) {
                    var at = path + "/" + child.Name;
                    if (!child.Serialized) verdicts.Add(new GroupRootVerdict(child, at, GroupDisposition.NotListed, RuntimeObject));
                    else if (child.HasFamily) verdicts.Add(new GroupRootVerdict(child, at, GroupDisposition.ExistingFamily, null));
                    else if (MarkerObject(child)) verdicts.Add(new GroupRootVerdict(child, at, GroupDisposition.NotListed, FolderRule));
                    else verdicts.Add(new GroupRootVerdict(child, at, GroupDisposition.InspectionOnly, "crossbucks at unprotected crossings", CrossbuckTicket));
                }
                return;
            }
            // Physical presence comes first: an object without a renderer, collider or light is not listed, whatever it holds.
            if (!Physical(node)) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.NotListed, NoPhysicalPresence)); return; }
            var bridge = BridgeTicket(town, node.Name);
            if (bridge != null) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.InspectionOnly, "baked scenery groups that are " + (bridge == "#97" ? "road bridges" : "rail bridge constructions outside Ela"), bridge)); return; }
            var excluded = FirstExclusion(node);
            if (excluded != null) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.InspectionOnly, excluded.Reason ?? "baked scenery groups that hold a " + excluded.Kind, excluded.Ticket, excluded.Reason != null)); return; }
            verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.Group, null));
        }
        // A renderer, collider or light at or below the node.
        static bool Physical(IHierarchyNode node) => node.Physical || node.Children.Any(Physical);
        // The first exclusion, in the order of the list, that a component at or below the node matches.
        static Exclusion FirstExclusion(IHierarchyNode node) {
            var types = new HashSet<string>(StringComparer.Ordinal);
            Collect(node, types);
            return Exclusions.FirstOrDefault(e => types.Any(e.Matches));
        }
        static void Collect(IHierarchyNode node, HashSet<string> types) {
            foreach (var type in node.Components) if (type != null) types.Add(type);
            foreach (var child in node.Children) Collect(child, types);
        }
    }

    // One scene object below a group root, observed with copied values.
    public sealed class GroupPart {
        public GroupPart(string id, IEnumerable<double> rootPosition, IEnumerable<double> rootRotation, bool activeSelf, bool cullerSwitched) {
            Id = id ?? throw new ArgumentNullException(nameof(id)); RootPosition = rootPosition.ToArray(); RootRotation = rootRotation.ToArray();
            ActiveSelf = activeSelf; CullerSwitched = cullerSwitched;
        }
        // Stable for the loaded object: hierarchy path below the root and instance.
        public string Id { get; }
        // Placement relative to the group root.
        public IReadOnlyList<double> RootPosition { get; }
        public IReadOnlyList<double> RootRotation { get; }
        public bool ActiveSelf { get; }
        // A culler of the game switches this part by camera distance, so its own activity is no part of the invariant.
        public bool CullerSwitched { get; }
    }
    // One culler in a group: the culling sphere it holds now and where the culler stands, both in map coordinates.
    public sealed class GroupCuller {
        public GroupCuller(string id, IEnumerable<double> registered, IEnumerable<double> expected) {
            Id = id ?? throw new ArgumentNullException(nameof(id)); Registered = registered?.ToArray(); Expected = expected.ToArray();
        }
        public string Id { get; }
        // The centre of the culling sphere the game holds for it, or null while it holds none.
        public IReadOnlyList<double> Registered { get; }
        public IReadOnlyList<double> Expected { get; }
    }
    public sealed class GroupObservation {
        public GroupObservation(ObjectState root, IEnumerable<GroupPart> parts, IEnumerable<GroupCuller> cullers, object parent, bool? rootCulling = null) {
            Root = root ?? throw new ArgumentNullException(nameof(root)); Parts = parts.ToArray(); Cullers = cullers.ToArray(); Parent = parent; RootCulling = rootCulling;
        }
        // Whether the simple culler on the root switches the root by camera distance; null without one. It pauses while
        // the group is disabled.
        public bool? RootCulling { get; }
        // The root's placement in map coordinates and its own activity as the scene authored it.
        public ObjectState Root { get; }
        public IReadOnlyList<GroupPart> Parts { get; }
        public IReadOnlyList<GroupCuller> Cullers { get; }
        public object Parent { get; }
    }
    // The game-facing operations of the baked scenery group capability. Positions are map coordinates.
    public interface IGroupWorld {
        IEnumerable<string> MapIds { get; }
        // The loaded group roots of the Baked scenery group family at the scene and path.
        IReadOnlyList<object> Find(string scene, string path);
        // Why the group cannot be edited now, or null.
        string Refusal(object group);
        GroupObservation Observe(object group);
        // Sets the root's own activity; the game's cullers register and release through their own lifecycle.
        void SetActive(object group, bool active);
        // Sets the root's placement and refreshes the culling spheres and physics state that follow it.
        void SetPlacement(object group, Placement placement);
    }
    // Baked scenery groups: places and enables or disables a group root with everything below it. A group move is
    // physical placement only; the activity of the parts stays theirs.
    public sealed class BakedSceneryGroups : IScene, IVersionedScene {
        public const string Structure = "baked-scenery-group-v1", ComponentType = "UnityEngine.Transform", RequiredVersion = "0.5.0";
        const double PositionTolerance = 0.01, AngleToleranceDegrees = 0.01, RegistrationTolerance = 0.1;
        readonly IGroupWorld world;
        public BakedSceneryGroups(IGroupWorld world, Capability capability = Capability.Unchecked) { this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != ComponentType || (identity.Structure != null && identity.Structure != Structure)) return Array.Empty<object>();
            return world.Find(identity.Scene, identity.Path) ?? Array.Empty<object>();
        }
        public void Preflight(object target) {
            var refusal = world.Refusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            var root = world.Observe(target).Root;
            if (root.Placement?.Position == null || root.Placement.Position.Any(v => Double.IsNaN(v) || Double.IsInfinity(v))) throw new InvalidOperationException("The group's map coordinates are unavailable");
        }
        public object Capture(object target) => world.Observe(target);
        public ObjectState Read(object target) => world.Observe(target).Root;
        public void Apply(object target, ObjectState state) {
            var before = world.Observe(target);
            if (state.Placement != null) CheckPlacement(state.Placement);
            var active = state.Active ?? before.Root.Active ?? true;
            if (active && before.Root.Active != true) world.SetActive(target, true);
            if (state.Placement != null) world.SetPlacement(target, state.Placement);
            if (!active && before.Root.Active != false) world.SetActive(target, false);
            var after = world.Observe(target);
            if (!SamePlacement(after.Root.Placement, state.Placement ?? before.Root.Placement) || after.Root.Active != active) throw new InvalidOperationException("Applied group placement or activity did not verify");
            var difference = Layout(before, after) ?? Culling(after, active) ?? RootCuller(after, active);
            if (difference != null) throw new InvalidOperationException("The group did not move as one: " + difference);
        }
        public bool Restore(object target, object snapshot) {
            var captured = (GroupObservation)snapshot;
            try {
                if (!ReferenceEquals(world.Observe(target).Parent, captured.Parent)) return false;
                var root = captured.Root;
                if (root.Active == true) world.SetActive(target, true);
                if (root.Placement != null) world.SetPlacement(target, root.Placement);
                if (root.Active == false) world.SetActive(target, false);
                var current = world.Observe(target);
                return SamePlacement(current.Root.Placement, root.Placement) && current.Root.Active == root.Active && Layout(captured, current) == null && Culling(current, root.Active != false) == null
                    && current.RootCulling == captured.RootCulling;
            } catch { return false; }
        }
        public string MinimumPatcherVersion(TargetIdentity target) => target != null && (target.Structure == Structure || (target.Structure == null && target.Component == ComponentType)) ? RequiredVersion : null;
        static void CheckPlacement(Placement placement) {
            var position = placement.Position; var rotation = placement.Rotation;
            if (position == null || rotation == null || position.Length != 3 || rotation.Length != 4) throw new InvalidOperationException("Invalid placement coordinates");
            if (position.Concat(rotation).Any(v => Double.IsNaN(v) || Double.IsInfinity(v) || Math.Abs(v) > Single.MaxValue)) throw new InvalidOperationException("Coordinate exceeds native precision");
            if (Math.Abs(rotation.Sum(x => x * x) - 1) > 0.001) throw new InvalidOperationException("Rotation must be a unit quaternion");
        }
        // Every part keeps its identity, its placement relative to the root and, unless a culler switches it, its activity.
        static string Layout(GroupObservation expected, GroupObservation current) {
            if (!expected.Parts.Select(p => p.Id).SequenceEqual(current.Parts.Select(p => p.Id))) return "the parts of the group changed";
            for (var i = 0; i < expected.Parts.Count; i++) {
                var a = expected.Parts[i]; var b = current.Parts[i];
                if (!Near(a.RootPosition, b.RootPosition, PositionTolerance) || !SameRotation(a.RootRotation, b.RootRotation)) return b.Id + " stayed behind";
                if (!a.CullerSwitched && !b.CullerSwitched && a.ActiveSelf != b.ActiveSelf) return "the activity of " + b.Id + " changed";
            }
            return null;
        }
        // An enabled group's culling spheres stand at their cullers; a disabled group's cullers hold none.
        static string Culling(GroupObservation current, bool active) {
            foreach (var culler in current.Cullers) {
                if (culler.Registered == null) continue;
                if (!active) return culler.Id + " still holds a culling sphere";
                if (!Near(culler.Registered, culler.Expected, RegistrationTolerance)) return "the culling sphere of " + culler.Id + " did not follow the group";
            }
            return null;
        }
        // A simple culler on the root switches an enabled group and pauses for a disabled one.
        static string RootCuller(GroupObservation current, bool active) =>
            current.RootCulling == null || current.RootCulling == active ? null : "the root culler " + (active ? "did not resume" : "still switches the disabled group");
        static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b, double tolerance) {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++) if (Math.Abs(a[i] - b[i]) > tolerance) return false;
            return true;
        }
        static bool SameRotation(IReadOnlyList<double> a, IReadOnlyList<double> b) {
            if (a == null || b == null || a.Count != 4 || b.Count != 4) return false;
            double dot = 0, la = 0, lb = 0;
            for (var i = 0; i < 4; i++) { dot += a[i] * b[i]; la += a[i] * a[i]; lb += b[i] * b[i]; }
            if (la <= 0 || lb <= 0) return false;
            return 2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(la * lb))) * 180 / Math.PI <= AngleToleranceDegrees;
        }
        static bool SamePlacement(Placement a, Placement b) => a != null && b != null && Near(a.Position, b.Position, PositionTolerance) && SameRotation(a.Rotation, b.Rotation);
    }
}
