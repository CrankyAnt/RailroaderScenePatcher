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
        // A level of a LOD group on an object above it: every renderer at and below this object is one the LOD group
        // switches by camera distance.
        bool LodLevel { get; }
        IReadOnlyList<IHierarchyNode> Children { get; }
    }
    public enum PartDisposition { Part, ExistingFamily, NotListed }
    // What the part rule decided for one scene object below a listed group root.
    public sealed class PartVerdict {
        internal PartVerdict(IHierarchyNode node, string path, string groupPath, PartDisposition disposition, string reason) {
            Node = node; Path = path; GroupPath = groupPath; Disposition = disposition; Reason = reason;
        }
        public IHierarchyNode Node { get; }
        public string Path { get; }
        // The path of the group root the part stands in.
        public string GroupPath { get; }
        public PartDisposition Disposition { get; }
        // Which coverage rule keeps the object out; null for a listed part and for an object an existing family lists.
        public string Reason { get; }
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
        // The coverage rules that keep an object below a listed group root out of the part entries.
        public const string AmbiguousParts = "Sibling parts that share one path", LodLevels = "Levels of a LOD group";
        // Every scene object below the roots of the listed groups, at any depth, with what the part rule decided for it.
        // A part is one scene object, not a renderer or a component. An inspection-only group has no part entries.
        public static IReadOnlyList<PartVerdict> Parts(IEnumerable<GroupRootVerdict> verdicts) {
            if (verdicts == null) throw new ArgumentNullException(nameof(verdicts));
            var found = new List<PartVerdict>();
            foreach (var group in verdicts.Where(v => v.Disposition == GroupDisposition.Group)) Walk(group.Node, group.Path, group.Path, null, found);
            return found;
        }
        // rule is set below an object that a sibling shares its path with: every object below it shares a path prefix
        // that names two objects, so it is no part either.
        static void Walk(IHierarchyNode parent, string parentPath, string groupPath, string rule, List<PartVerdict> found) {
            var shared = new HashSet<string>(parent.Children.GroupBy(c => c.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.Ordinal);
            foreach (var child in parent.Children) {
                var path = parentPath + "/" + child.Name;
                if (rule != null) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, rule)); Walk(child, path, groupPath, rule, found); continue; }
                if (!child.Serialized) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, RuntimeObject)); continue; }
                if (shared.Contains(child.Name)) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, AmbiguousParts)); Walk(child, path, groupPath, AmbiguousParts, found); continue; }
                // A level of a LOD group belongs to the entry of the object that holds the LOD group.
                if (child.LodLevel) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, LodLevels)); continue; }
                if (child.HasFamily) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.ExistingFamily, null)); continue; }
                if (!Physical(child)) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, NoPhysicalPresence)); continue; }
                found.Add(new PartVerdict(child, path, groupPath, PartDisposition.Part, null));
                Walk(child, path, groupPath, null, found);
            }
        }
        // The name of a part entry: the group root's name, then the path from the group root to the part.
        public static string PartName(string groupPath, string path) {
            if (groupPath == null || path == null || !path.StartsWith(groupPath + "/", StringComparison.Ordinal)) return path?.Substring(path.LastIndexOf('/') + 1);
            return groupPath.Substring(groupPath.LastIndexOf('/') + 1) + " / " + path.Substring(groupPath.Length + 1).Replace("/", " / ");
        }
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
        public GroupPart(string id, IEnumerable<double> rootPosition, IEnumerable<double> rootRotation, bool activeSelf, bool cullerSwitched, object handle = null, object parent = null) {
            Id = id ?? throw new ArgumentNullException(nameof(id)); RootPosition = rootPosition.ToArray(); RootRotation = rootRotation.ToArray();
            ActiveSelf = activeSelf; CullerSwitched = cullerSwitched; Handle = handle; Parent = parent;
        }
        // Stable for the loaded object: hierarchy path below the root and instance.
        public string Id { get; }
        // Placement relative to the group root.
        public IReadOnlyList<double> RootPosition { get; }
        public IReadOnlyList<double> RootRotation { get; }
        public bool ActiveSelf { get; }
        // A culler of the game switches this part by camera distance, so its own activity is no part of the invariant.
        public bool CullerSwitched { get; }
        // The object, as the part capability resolves it, and the handle of the part it stands below, or null directly
        // below the group root.
        public object Handle { get; }
        public object Parent { get; }
    }
    // One culler in a group: the culling sphere it holds now and where the culler stands, both in map coordinates.
    public sealed class GroupCuller {
        public GroupCuller(string id, IEnumerable<double> registered, IEnumerable<double> expected, double? radius = null, double? required = null, object handle = null, IEnumerable<object> listed = null) {
            Id = id ?? throw new ArgumentNullException(nameof(id)); Registered = registered?.ToArray(); Expected = expected.ToArray(); Radius = radius; Required = required;
            Handle = handle; Listed = (listed ?? Enumerable.Empty<object>()).ToArray();
        }
        // The object the culler stands on: the group root or a part. Listed holds the objects a game-object culler switches.
        public object Handle { get; }
        public IReadOnlyList<object> Listed { get; }
        public string Id { get; }
        // The centre of the culling sphere the game holds for it, or null while it holds none.
        public IReadOnlyList<double> Registered { get; }
        public IReadOnlyList<double> Expected { get; }
        // The radius of its culling sphere, and the radius around the culler that encloses the renderers it culls now:
        // for a simple culler or a renderer culler those below its object, for a game-object culler also those below
        // the objects it switches. Null when the culler holds no radius.
        public double? Radius { get; }
        public double? Required { get; }
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
    // One part with its group, observed with copied values.
    public sealed class PartObservation {
        public PartObservation(ObjectState part, object group, GroupObservation groupState, object parent, Placement parentPlacement) {
            Part = part ?? throw new ArgumentNullException(nameof(part)); Group = group ?? throw new ArgumentNullException(nameof(group));
            GroupState = groupState ?? throw new ArgumentNullException(nameof(groupState)); Parent = parent; ParentPlacement = parentPlacement;
        }
        // The map placement of the object the part stands directly below: its group root or the part above it. A part
        // without a fixed placement of its own keeps its place relative to this object.
        public Placement ParentPlacement { get; }
        // The part's placement in map coordinates and its own activity as the scene authored it.
        public ObjectState Part { get; }
        // The group root the part stands in, as the group capability resolves it.
        public object Group { get; }
        public GroupObservation GroupState { get; }
        // The object the part stands directly below.
        public object Parent { get; }
    }
    // The game-facing operations of the baked scenery group and part capabilities. Positions are map coordinates.
    public interface IGroupWorld {
        IEnumerable<string> MapIds { get; }
        // The loaded group roots of the Baked scenery group family at the scene and path.
        IReadOnlyList<object> Find(string scene, string path);
        // Why the group cannot be edited now, or null.
        string Refusal(object group);
        GroupObservation Observe(object group);
        // Sets the root's own activity; the game's cullers register and release through their own lifecycle.
        void SetActive(object group, bool active);
        // Sets the root's placement and synchronises the physics state; the cullers follow through RefreshCulling.
        void SetPlacement(object group, Placement placement);
        // The loaded parts of listed groups at the scene and path.
        IReadOnlyList<object> FindParts(string scene, string path);
        // Why the part cannot be edited now, or null.
        string PartRefusal(object part);
        // Why the part's activity cannot be set, or null: a game-object culler switches it by camera distance and would
        // override a stored setting. The patcher then never sets that part's activity.
        string ActivityRefusal(object part);
        PartObservation ObservePart(object part);
        // Sets the part's own activity; its cullers register and release through their own lifecycle.
        void SetPartActive(object part, bool active);
        // Sets the part's placement and synchronises the physics state; everything below the part moves with it.
        void SetPartPlacement(object part, Placement placement);
        // Moves the culling sphere of every culler in the group to its culler. The radius of each culler that enclose
        // names by id encloses the renderers it culls, and never drops below its authored radius; every other culler
        // takes its authored radius.
        void RefreshCulling(object group, ICollection<string> enclose);
        // Whether the part stands below the object, a group root or another part, and still exists.
        bool Below(object part, object above);
        // Whether the object, a group root or a part, still exists.
        bool Alive(object handle);
    }
    // The fixed placements that hold parts of baked scenery groups in place, by part. A part with a fixed placement keeps
    // its map placement when the group root or a part above it moves; without one it moves with them. The group and part
    // capabilities of one scene share one instance.
    public sealed class PartHolds {
        sealed class ByReference : IEqualityComparer<object> {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        readonly Dictionary<object, Placement> held = new Dictionary<object, Placement>(new ByReference());
        // The group root of each held part, so a released part's group can let go of its enlarged culling.
        readonly Dictionary<object, object> groups = new Dictionary<object, object>(new ByReference());
        internal Placement Of(object part) => held.TryGetValue(part, out var placement) ? placement : null;
        internal object GroupOf(object part) => groups.TryGetValue(part, out var group) ? group : null;
        internal void Set(object part, Placement placement, object group = null) {
            if (placement == null) { held.Remove(part); groups.Remove(part); return; }
            held[part] = new Placement((double[])placement.Position.Clone(), (double[])placement.Rotation.Clone());
            if (group != null) groups[part] = group;
        }
        internal bool Contains(object part) => part != null && held.ContainsKey(part);
        // Drops the holds of parts that no longer exist, such as those of a world that was unloaded.
        internal void Prune(IGroupWorld world) { foreach (var gone in held.Keys.Where(part => !world.Alive(part)).ToList()) Set(gone, null); }
        // The held parts below the object, each after every held part above it, so a part above sets its place first.
        internal IReadOnlyList<KeyValuePair<object, Placement>> Below(IGroupWorld world, object above) {
            Prune(world);
            var below = held.Where(pair => world.Below(pair.Key, above)).ToList();
            var ordered = new List<KeyValuePair<object, Placement>>();
            while (below.Count != 0) {
                var next = below.First(pair => !below.Any(other => !ReferenceEquals(other.Key, pair.Key) && world.Below(pair.Key, other.Key)));
                ordered.Add(next); below.Remove(next);
            }
            return ordered;
        }
        // Puts every held part below the object back at its fixed placement.
        internal void Reassert(IGroupWorld world, object above) { foreach (var pair in Below(world, above)) world.SetPartPlacement(pair.Key, pair.Value); }
    }
    // The checks that the group and part capabilities share.
    static class GroupChecks {
        internal const double PositionTolerance = 0.01, AngleToleranceDegrees = 0.01, RegistrationTolerance = 0.1;
        // Every part keeps its identity and its place in its frame, and, unless a culler switches it, its activity. The
        // frame of a part is the nearest part at or above it that framed names; a framed part keeps its map placement,
        // a part below one keeps its place relative to it, and every other part keeps its place relative to the root.
        // skip names a part that the edit changes itself.
        internal static string Layout(GroupObservation expected, GroupObservation current, Func<object, bool> framed, object skip = null) {
            if (!expected.Parts.Select(p => p.Id).SequenceEqual(current.Parts.Select(p => p.Id))) return "the parts of the group changed";
            var index = new Dictionary<object, int>(new Identity());
            for (var i = 0; i < current.Parts.Count; i++) if (current.Parts[i].Handle != null) index[current.Parts[i].Handle] = i;
            for (var i = 0; i < expected.Parts.Count; i++) {
                var a = expected.Parts[i]; var b = current.Parts[i];
                if (skip != null && ReferenceEquals(b.Handle, skip)) continue;
                var frame = -1;
                for (var at = i; at >= 0; at = current.Parts[at].Parent != null && index.TryGetValue(current.Parts[at].Parent, out var up) ? up : -1)
                    if (current.Parts[at].Handle != null && framed(current.Parts[at].Handle)) { frame = at; break; }
                double[] wasPosition, wasRotation, nowPosition, nowRotation;
                if (frame == i) {
                    (wasPosition, wasRotation) = InMap(expected.Root.Placement, a); (nowPosition, nowRotation) = InMap(current.Root.Placement, b);
                } else if (frame >= 0) {
                    (wasPosition, wasRotation) = Relative(expected.Parts[frame], a); (nowPosition, nowRotation) = Relative(current.Parts[frame], b);
                } else { wasPosition = a.RootPosition.ToArray(); wasRotation = a.RootRotation.ToArray(); nowPosition = b.RootPosition.ToArray(); nowRotation = b.RootRotation.ToArray(); }
                if (!Near(wasPosition, nowPosition, PositionTolerance) || !SameRotation(wasRotation, nowRotation))
                    return b.Id + (frame == i ? " left its fixed placement" : frame >= 0 ? " left its place below " + current.Parts[frame].Id : " stayed behind");
                if (!a.CullerSwitched && !b.CullerSwitched && a.ActiveSelf != b.ActiveSelf) return "the activity of " + b.Id + " changed";
            }
            return null;
        }
        sealed class Identity : IEqualityComparer<object> {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        static (double[], double[]) InMap(Placement root, GroupPart part) {
            var turn = Quaternion.Normalize(root.Rotation); var offset = Quaternion.Rotate(turn, part.RootPosition);
            return (new[] { root.Position[0] + offset[0], root.Position[1] + offset[1], root.Position[2] + offset[2] }, Quaternion.Multiply(turn, part.RootRotation));
        }
        static (double[], double[]) Relative(GroupPart frame, GroupPart part) {
            var inverse = Quaternion.Conjugate(Quaternion.Normalize(frame.RootRotation));
            return (Quaternion.Rotate(inverse, new[] { part.RootPosition[0] - frame.RootPosition[0], part.RootPosition[1] - frame.RootPosition[1], part.RootPosition[2] - frame.RootPosition[2] }), Quaternion.Multiply(inverse, part.RootRotation));
        }
        // The cullers whose radius encloses their renderers now: a culler with a held part below the object it stands
        // on, or below an object it switches. Every other culler keeps its authored radius.
        internal static HashSet<string> Enclosed(IGroupWorld world, PartHolds holds, object group) {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var held = holds.Below(world, group).Select(pair => pair.Key).ToList();
            if (held.Count == 0) return ids;
            foreach (var culler in world.Observe(group).Cullers) {
                if (culler.Radius == null || culler.Handle == null) continue;
                if (held.Any(part => ReferenceEquals(culler.Handle, group) || world.Below(part, culler.Handle) || culler.Listed.Any(listed => ReferenceEquals(part, listed) || world.Below(part, listed)))) ids.Add(culler.Id);
            }
            return ids;
        }
        // An enabled group's culling spheres stand at their cullers; a disabled group's cullers hold none. Each culler
        // that enclose names encloses its renderers.
        internal static string Culling(GroupObservation current, bool active, ICollection<string> enclose = null) {
            foreach (var culler in current.Cullers) {
                if (enclose != null && enclose.Contains(culler.Id) && active && culler.Required != null && (culler.Radius ?? 0) + PositionTolerance < culler.Required) return "the culling sphere of " + culler.Id + " does not enclose the renderers it culls";
                if (culler.Registered == null) continue;
                if (!active) return culler.Id + " still holds a culling sphere";
                if (!Near(culler.Registered, culler.Expected, RegistrationTolerance)) return "the culling sphere of " + culler.Id + " did not follow the group";
            }
            return null;
        }
        // A simple culler on the root switches an enabled group and pauses for a disabled one.
        internal static string RootCuller(GroupObservation current, bool active) =>
            current.RootCulling == null || current.RootCulling == active ? null : "the root culler " + (active ? "did not resume" : "still switches the disabled group");
        internal static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b, double tolerance) {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++) if (Math.Abs(a[i] - b[i]) > tolerance) return false;
            return true;
        }
        internal static bool SameRotation(IReadOnlyList<double> a, IReadOnlyList<double> b) {
            if (a == null || b == null || a.Count != 4 || b.Count != 4) return false;
            double dot = 0, la = 0, lb = 0;
            for (var i = 0; i < 4; i++) { dot += a[i] * b[i]; la += a[i] * a[i]; lb += b[i] * b[i]; }
            if (la <= 0 || lb <= 0) return false;
            return 2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(la * lb))) * 180 / Math.PI <= AngleToleranceDegrees;
        }
        internal static bool SamePlacement(Placement a, Placement b) => a != null && b != null && Near(a.Position, b.Position, PositionTolerance) && SameRotation(a.Rotation, b.Rotation);
        internal static void CheckPlacement(Placement placement) {
            var position = placement.Position; var rotation = placement.Rotation;
            if (position == null || rotation == null || position.Length != 3 || rotation.Length != 4) throw new InvalidOperationException("Invalid placement coordinates");
            if (position.Concat(rotation).Any(v => Double.IsNaN(v) || Double.IsInfinity(v) || Math.Abs(v) > Single.MaxValue)) throw new InvalidOperationException("Coordinate exceeds native precision");
            if (Math.Abs(rotation.Sum(x => x * x) - 1) > 0.001) throw new InvalidOperationException("Rotation must be a unit quaternion");
        }
        internal static bool Finite(Placement placement) => placement?.Position != null && !placement.Position.Any(v => Double.IsNaN(v) || Double.IsInfinity(v));
    }
    // Baked scenery groups: places and enables or disables a group root with everything below it. A group move is
    // physical placement only: the activity of the parts stays theirs, and a part with its own fixed placement keeps it.
    public sealed class BakedSceneryGroups : IScene, IVersionedScene {
        public const string Structure = "baked-scenery-group-v1", ComponentType = "UnityEngine.Transform", RequiredVersion = "0.5.0";
        readonly IGroupWorld world;
        readonly PartHolds holds;
        public BakedSceneryGroups(IGroupWorld world, Capability capability = Capability.Unchecked, PartHolds holds = null) {
            this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; this.holds = holds ?? new PartHolds();
        }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != ComponentType || (identity.Structure != null && identity.Structure != Structure)) return Array.Empty<object>();
            return world.Find(identity.Scene, identity.Path) ?? Array.Empty<object>();
        }
        public void Preflight(object target) {
            var refusal = world.Refusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            if (!GroupChecks.Finite(world.Observe(target).Root.Placement)) throw new InvalidOperationException("The group's map coordinates are unavailable");
        }
        public object Capture(object target) => world.Observe(target);
        public ObjectState Read(object target) => world.Observe(target).Root;
        // The group root's placement applies first; every part with a fixed placement below it is then put back there.
        void Place(object target, Placement placement) {
            world.SetPlacement(target, placement);
            holds.Reassert(world, target);
            world.RefreshCulling(target, Enclose(target));
        }
        // A culler encloses its renderers while a part below it keeps a fixed placement of its own.
        HashSet<string> Enclose(object target) => GroupChecks.Enclosed(world, holds, target);
        public void Apply(object target, ObjectState state) {
            var before = world.Observe(target);
            if (state.Placement != null) GroupChecks.CheckPlacement(state.Placement);
            var active = state.Active ?? before.Root.Active ?? true;
            if (active && before.Root.Active != true) world.SetActive(target, true);
            if (state.Placement != null) Place(target, state.Placement);
            if (!active && before.Root.Active != false) world.SetActive(target, false);
            var after = world.Observe(target);
            if (!GroupChecks.SamePlacement(after.Root.Placement, state.Placement ?? before.Root.Placement) || after.Root.Active != active) throw new InvalidOperationException("Applied group placement or activity did not verify");
            var difference = GroupChecks.Layout(before, after, holds.Contains) ?? GroupChecks.Culling(after, active, Enclose(target)) ?? GroupChecks.RootCuller(after, active);
            if (difference != null) throw new InvalidOperationException("The group did not move as one: " + difference);
        }
        public bool Restore(object target, object snapshot) {
            var captured = (GroupObservation)snapshot;
            try {
                if (!ReferenceEquals(world.Observe(target).Parent, captured.Parent)) return false;
                var root = captured.Root;
                if (root.Active == true) world.SetActive(target, true);
                if (root.Placement != null) Place(target, root.Placement);
                if (root.Active == false) world.SetActive(target, false);
                var current = world.Observe(target);
                return GroupChecks.SamePlacement(current.Root.Placement, root.Placement) && current.Root.Active == root.Active && GroupChecks.Layout(captured, current, holds.Contains) == null
                    && GroupChecks.Culling(current, root.Active != false, Enclose(target)) == null && current.RootCulling == captured.RootCulling;
            } catch { return false; }
        }
        public string MinimumPatcherVersion(TargetIdentity target) => target != null && (target.Structure == Structure || (target.Structure == null && target.Component == ComponentType)) ? RequiredVersion : null;
    }
    // Parts of baked scenery groups: places and enables or disables one scene object inside a listed group. A part edit
    // is an ordinary scene-object edit; the group root and the other parts keep their placement and activity. A fixed
    // placement holds the part in map coordinates when its group moves later; the placement the part has without any
    // modification stays relative to its group.
    public sealed class BakedSceneryParts : IScene, IVersionedScene, IFramedScene, IRelationshipScene, IRetainedScene {
        public const string Structure = "baked-scenery-part-v1", ComponentType = BakedSceneryGroups.ComponentType, RequiredVersion = "0.5.0";
        readonly IGroupWorld world;
        readonly PartHolds holds;
        public BakedSceneryParts(IGroupWorld world, PartHolds holds, Capability capability = Capability.Unchecked) {
            this.world = world ?? throw new ArgumentNullException(nameof(world)); this.holds = holds ?? throw new ArgumentNullException(nameof(holds)); Capability = capability;
        }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != ComponentType || (identity.Structure != null && identity.Structure != Structure)) return Array.Empty<object>();
            return world.FindParts(identity.Scene, identity.Path) ?? Array.Empty<object>();
        }
        public void Preflight(object target) {
            var refusal = world.PartRefusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            var observed = world.ObservePart(target);
            if (!GroupChecks.Finite(observed.Part.Placement) || !GroupChecks.Finite(observed.GroupState.Root.Placement)) throw new InvalidOperationException("The part's map coordinates are unavailable");
        }
        // Without a fixed placement of its own the part stands in its group root: its placement says so, and applied back
        // it returns the part to its place in the group.
        public ObjectState Read(object target) {
            var observed = world.ObservePart(target);
            var placement = observed.Part.Placement;
            if (placement != null && !holds.Contains(target) && observed.ParentPlacement != null) placement = new Placement(placement.Position, placement.Rotation) { Frame = observed.ParentPlacement };
            // The activity of a part a game-object culler switches is the culler's: the part reads none, so neither the
            // underlying state nor a desired state ever holds one for it.
            return new ObjectState(placement, world.ActivityRefusal(target) != null ? null : observed.Part.Active);
        }
        // A part stands in the object directly above it, so a part below a moved part keeps its place in that part.
        public Placement FramePlacement(object target) => world.ObservePart(target).ParentPlacement;
        public void CheckRelationships(object target, ObjectState state) {
            if (state?.Marker != null) throw new InvalidOperationException("This object has no supported track-marker relationship");
            if (state?.Coverage != null) throw new InvalidOperationException("This object has no supported crossing-coverage relationship");
            if (state?.Equipment != null) throw new InvalidOperationException("This object has no supported crossing-equipment relationship");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
            var refusal = state?.Active == null ? null : world.ActivityRefusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
        }
        sealed class Snapshot { internal PartObservation Observed; internal Placement Held; }
        public object Capture(object target) => new Snapshot { Observed = world.ObservePart(target), Held = holds.Of(target) };
        // The part's placement applies, then every held part below it is put back at its fixed placement, and the cullers
        // of the group follow.
        void Place(object target, object group, Placement placement, Placement held) {
            holds.Set(target, held, group);
            world.SetPartPlacement(target, placement);
            holds.Reassert(world, target);
            world.RefreshCulling(group, GroupChecks.Enclosed(world, holds, group));
        }
        public void Apply(object target, ObjectState state) {
            var before = world.ObservePart(target);
            var desired = state.Placement;
            if (desired != null) GroupChecks.CheckPlacement(desired);
            // A game-object culler switches this part by camera distance: its activity stays the culler's, whatever the
            // state says, so an edit never switches it on or off against the culler.
            var switched = world.ActivityRefusal(target) != null;
            var active = state.Active ?? before.Part.Active ?? true;
            if (!switched && active && before.Part.Active != true) world.SetPartActive(target, true);
            // A placement read in its parent returns the part there; any other placement is a fixed placement that holds it.
            if (desired != null) Place(target, before.Group, desired, desired.Frame == null ? desired : null);
            if (!switched && !active && before.Part.Active != false) world.SetPartActive(target, false);
            if (!switched && desired == null && active != before.Part.Active) world.RefreshCulling(before.Group, GroupChecks.Enclosed(world, holds, before.Group));
            Verify(target, before, desired ?? before.Part.Placement, switched ? (bool?)null : active);
        }
        // active is null for a part whose activity a culler switches: its activity is not compared.
        void Verify(object target, PartObservation before, Placement placement, bool? active) {
            var after = world.ObservePart(target);
            if (!GroupChecks.SamePlacement(after.Part.Placement, placement) || (active != null && after.Part.Active != active)) throw new InvalidOperationException("Applied part placement or activity did not verify");
            var group = before.GroupState.Root; var now = after.GroupState.Root;
            if (!ReferenceEquals(after.Group, before.Group) || !GroupChecks.SamePlacement(now.Placement, group.Placement) || now.Active != group.Active) throw new InvalidOperationException("The part edit changed its group root");
            var difference = GroupChecks.Layout(before.GroupState, after.GroupState, h => ReferenceEquals(h, target) || holds.Contains(h), target)
                ?? GroupChecks.Culling(after.GroupState, now.Active != false, GroupChecks.Enclosed(world, holds, after.Group));
            if (difference != null) throw new InvalidOperationException("The part edit changed more than the part: " + difference);
        }
        public bool Restore(object target, object snapshot) {
            var captured = (Snapshot)snapshot;
            try {
                var current = world.ObservePart(target);
                if (!ReferenceEquals(current.Parent, captured.Observed.Parent) || !ReferenceEquals(current.Group, captured.Observed.Group)) return false;
                var part = captured.Observed.Part;
                var switched = world.ActivityRefusal(target) != null;
                if (!switched && part.Active == true) world.SetPartActive(target, true);
                if (part.Placement != null) Place(target, captured.Observed.Group, part.Placement, captured.Held);
                if (!switched && part.Active == false) world.SetPartActive(target, false);
                world.RefreshCulling(captured.Observed.Group, GroupChecks.Enclosed(world, holds, captured.Observed.Group));
                Verify(target, captured.Observed, part.Placement, switched ? (bool?)null : part.Active != false);
                return true;
            } catch { return false; }
        }
        // A part that no longer stands under its identity holds nothing any more, and the cullers of its group, when
        // the group still stands, let go of a radius they enlarged for it.
        public void Release(object target) {
            var group = holds.GroupOf(target);
            holds.Set(target, null);
            if (group != null && world.Alive(group) && world.Refusal(group) == null) world.RefreshCulling(group, GroupChecks.Enclosed(world, holds, group));
        }
        public bool Unsettled(object target) => false;
        public string MinimumPatcherVersion(TargetIdentity target) => target != null && (target.Structure == Structure || (target.Structure == null && target.Component == ComponentType)) ? RequiredVersion : null;
    }
}
