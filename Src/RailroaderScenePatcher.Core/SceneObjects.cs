// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RailroaderScenePatcher.Core {
    // One loaded scene object as the scene object rule reads it: its name, the components on it, and its children.
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
        // The game turns or moves this object's transform while it runs, such as the clock hand a gauge needle turns.
        bool Animated { get; }
        // The id of the track marker on this object itself, or null without one. Read only for the crossing pads of an
        // unprotected crossing.
        string MarkerId { get; }
        IReadOnlyList<IHierarchyNode> Children { get; }
    }
    public enum PartDisposition { Part, ExistingFamily, NotListed }
    // What the part rule decided for one scene object below a listed top scene object.
    public sealed class PartVerdict {
        internal PartVerdict(IHierarchyNode node, string path, string groupPath, PartDisposition disposition, string reason) {
            Node = node; Path = path; GroupPath = groupPath; Disposition = disposition; Reason = reason;
        }
        public IHierarchyNode Node { get; }
        public string Path { get; }
        // The path of the top scene object the part stands in.
        public string GroupPath { get; }
        public PartDisposition Disposition { get; }
        // Which coverage rule keeps the object out; null for a listed part and for an object an existing family lists.
        public string Reason { get; }
    }
    // The bridge a top scene object is for its edits: a rail bridge stands on native track and a road bridge on a native
    // road, so each takes fixed placement only, and a rail bridge may hold its own construction controller. This is
    // the group's technical kind. The outliner's category does not decide it.
    public enum BridgeKind { None, Rail, Road }
    public enum GroupDisposition { Group, InspectionOnly, ExistingFamily, NotListed, GroupHolder }
    // What the rule decided for one candidate: a top scene object of the Scene object family, a top scene object that is
    // listed for inspection only, an object an existing family lists, or an object a coverage rule keeps out.
    public sealed class SceneObjectVerdict {
        internal SceneObjectVerdict(IHierarchyNode node, string path, GroupDisposition disposition, string reason, bool sentence = false) {
            Node = node; Path = path; Disposition = disposition; Reason = reason; ReasonIsSentence = sentence;
        }
        // The reason of an inspection-only group is a whole clause, not the kind of group the patcher has no edit for.
        public bool ReasonIsSentence { get; }
        public IHierarchyNode Node { get; }
        public string Path { get; }
        public GroupDisposition Disposition { get; }
        // Why the group is inspection only, or which coverage rule keeps it out; null for a listed group.
        public string Reason { get; }
        // The category the outliner lists the group under, and its subgroup there, such as Bridge and Rail bridges.
        // A listed group no rule recognises stands under Unclassified. Null for an inspection-only group no rule
        // recognises, and for an object that is no group.
        public string Category { get; internal set; }
        public string Subcategory { get; internal set; }
        // Whether the group is a rail bridge or a road bridge for its edits (SceneObjectRules.RailBridge, RoadBridge).
        public BridgeKind Bridge { get; internal set; }
        // A crossbuck of an unprotected crossing: a group of its own under Grade crossing, whose models are no parts.
        public bool Crossbuck { get; internal set; }
        // For a crossbuck, the marker ID of its crossing's pad, or null when the crossing has no pad with one id.
        public string PadMarkerId { get; internal set; }
        // A track marker stands at or below the group (SceneObjectRules.HoldsTrackMarker): the group moves as a whole,
        // but its activity is refused (SceneObjectRules.MarkerActivityRefusal).
        public bool HoldsTrackMarkers { get; internal set; }
    }
    // The categories the outliner lists objects under. Which category an object takes is decided by generic rules
    // from what the object provides (SceneClassification); no table names objects.
    public static class SceneObjectCategories {
        public const string StationBuilding = "Station building", Platform = "Platform", Lights = "Lights", Sign = "Sign", GradeCrossing = "Grade crossing", Bridge = "Bridge",
            IndustryBuilding = "Industry building", StorageDisplay = "Storage display", RailroadServiceBuilding = "Railroad service building", Water = "Water", RoadScenery = "Road scenery",
            // The category of last resort, for a listed group that no rule recognises.
            Unclassified = "Unclassified";
        public const string RailBridges = "Rail bridges", RoadBridges = "Road bridges";
        // The families of the bridge and of the construction controller of the Ela rail bridge construction.
        public const string BridgeFamily = "Bridge", ConstructionFamily = "Bridge construction scenery";
        // The scene objects in a top scene object's position that have no model or function in the game: an inactive default
        // cube and a folder of lights without a model. They are not supported and stay out of the inventory.
        public static readonly IReadOnlyList<string> WithoutFunction = Array.AsReadOnly(new[] { "World/Large Scenery/Almond/Cube", "World/Large Scenery/Bryson/Bryson Appalachian Hardwoods" });
    }
    // Which scene objects below World are the top scene objects.
    public static class SceneObjectRules {
        // The families of a top scene object and of its child objects, split by structure id.
        public const string Family = "Scene object", ChildFamily = "Child object";
        public static bool InFamilies(string family) => family == Family || family == ChildFamily;
        public const string WorldRoot = "World", LargeScenery = "Large Scenery";
        public const string MapLabels = "Map Labels", StationIcons = "Station Icons", BuilderPhotoSetup = "Builder Photo Setup";
        // The coverage rules that keep a candidate out, by the name of their row in the coverage statement.
        public const string NoPhysicalPresence = "Objects without physical presence", RuntimeObject = "Objects the game creates while running",
            NativeMapData = "Native track spans, passenger stops, industries, native spawns, turntables, and other native map data", MapLabel = "Map labels",
            BuilderPhotoRig = "Builder photo rig", NoFunction = "Objects without a model or function in the game";
        // The renderers of visual effects, which draw particles that nothing can run into: a particle system and a VFX
        // Graph effect, such as the fireflies under World/Environment Effects.
        public static readonly IReadOnlyList<string> EffectRenderers = Array.AsReadOnly(new[] { "UnityEngine.ParticleSystemRenderer", "UnityEngine.VFX.VFXRenderer" });
        // Whether a component gives its object physical presence: a collider, a light, or a renderer that is no effect's.
        public static bool GivesPresence(string type, bool renderer, bool collider, bool light) => collider || light || (renderer && !EffectRenderers.Contains(type));
        // A component the game spawns from Map.json carries; the native map editor edits such an object.
        public const string MapObject = "Game.MapObjectActivation";
        // Components of a World category holder whose children the game builds while it runs: track geometry with its
        // trestles and pads, objects spawned from map data, and telegraph poles.
        public static readonly IReadOnlyList<string> Generators = Array.AsReadOnly(new[] { "Track.TrackObjectManager", "Game.MapLoader", "TelegraphPoles.TelegraphPoleManager" });
        // Every candidate below World with what the rule decided for it. A root that is not World holds no top scene objects.
        public static IReadOnlyList<SceneObjectVerdict> Classify(IHierarchyNode world) {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var verdicts = new List<SceneObjectVerdict>();
            if (world.Name != WorldRoot) return verdicts;
            foreach (var category in world.Children) {
                var categoryPath = WorldRoot + "/" + category.Name;
                string rule = !category.Serialized || category.Components.Any(Generators.Contains) ? RuntimeObject : category.Name == BuilderPhotoSetup ? BuilderPhotoRig : null;
                foreach (var (town, root, path) in Roots(category, categoryPath)) {
                    if (rule != null) verdicts.Add(new SceneObjectVerdict(root, path, GroupDisposition.NotListed, rule));
                    else if (category.Name == MapLabels && root.Name != StationIcons) verdicts.Add(new SceneObjectVerdict(root, path, GroupDisposition.NotListed, MapLabel));
                    // The Station Icons holder holds the station map icons. Each gets an entry in its own family.
                    // A direct child with a map icon is a candidate in that family. Other children,
                    // including subgroups, and the map labels beside Station Icons stay out of the inventory.
                    else if (category.Name == MapLabels)
                        foreach (var icon in root.Children) {
                            var at = path + "/" + icon.Name;
                            if (icon.Components.Contains(StationMapIconRules.Component)) Candidate(town, icon, at, verdicts);
                            else verdicts.Add(new SceneObjectVerdict(icon, at, GroupDisposition.NotListed, MapLabel));
                        }
                    else Candidate(town, root, path, verdicts);
                }
            }
            // A group stands under the category the generic rules give it, whatever supports it. A listed group that
            // no rule recognises stands under the category of last resort; an inspection-only group that no rule
            // recognises has no category.
            foreach (var verdict in verdicts) {
                if (verdict.Category != null || (verdict.Disposition != GroupDisposition.Group && verdict.Disposition != GroupDisposition.InspectionOnly)) continue;
                var found = ClassOf(verdict.Node, verdict.Path);
                if (found.Recognised || verdict.Disposition == GroupDisposition.Group) { verdict.Category = found.Category; verdict.Subcategory = found.Category == SceneObjectCategories.Bridge ? found.Kind : null; }
            }
            return verdicts;
        }
        // The category and kind of a top scene object, from its components, its name and what stands below it.
        public static SceneClass ClassOf(IHierarchyNode node, string path, Func<string, string> stripPlaces = null) {
            var types = new HashSet<string>(StringComparer.Ordinal);
            Collect(node, types);
            return SceneClassification.OfGroup(path, node.Components, types, NamesBelow(node), stripPlaces);
        }
        public static IEnumerable<string> NamesBelow(IHierarchyNode node) {
            foreach (var child in node.Children) {
                if (!child.Serialized) continue;
                yield return child.Name;
                foreach (var name in NamesBelow(child)) yield return name;
            }
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
        // The objects one level below a category holder, and below World/Large Scenery one level below a scene object there, with
        // the name of that scene object below World/Large Scenery.
        static IEnumerable<(string, IHierarchyNode, string)> Roots(IHierarchyNode category, string categoryPath) {
            if (category.Name != LargeScenery) { foreach (var root in category.Children) yield return (null, root, categoryPath + "/" + root.Name); yield break; }
            foreach (var town in category.Children) {
                // A scene object there that is itself an object of the game, such as Almond Bridge, is the top scene object,
                // and so is a mod object there, such as a part copied from Almond Bridge: it is never a folder of a place.
                if (GroupItself(town) || ModObjects.IsPath(town.Name)) { yield return (town.Name, town, categoryPath + "/" + town.Name); continue; }
                foreach (var root in town.Children) yield return (town.Name, root, categoryPath + "/" + town.Name + "/" + root.Name);
            }
        }
        // A scene object directly below World/Large Scenery that is an object of the game itself and no folder of a
        // place: it carries a culler or a construction controller, or has a renderer, collider or light of its own, as
        // the Almond Bridge object carries the culler and the construction controller of its bridge.
        static bool GroupItself(IHierarchyNode town) =>
            !town.HasFamily && (Present(town) || town.Components.Any(type => Cullers.Contains(type) || type == ConstructionController));
        const string TransformType = "UnityEngine.Transform";
        // A holder: an object without components of its own and without a name that says what it is,
        // whose children are all prefab roots that cull themselves (a renderer culler on each), such as the yard lamp
        // posts of Bryson Yard. It is no group: each child is a top scene object of its own.
        public static bool GroupHolder(IHierarchyNode node) =>
            node != null && !node.HasFamily && node.Children.Count != 0 && node.Components.All(type => type == TransformType)
            && node.Children.All(child => !child.HasFamily && child.Components.Contains(RendererCuller)) && SceneClassification.ByName(node.Name) == null;
        // A component anywhere in a group that keeps the group from being edited as a whole, with the kind it stands for
        // and the whole reason the group is listed for inspection only.
        public sealed class Exclusion {
            internal Exclusion(string component, string kind, string reason, bool prefix = false) { Component = component; Kind = kind; Reason = reason; Prefix = prefix; }
            public string Reason { get; }
            public string Component { get; }
            public string Kind { get; }
            // The component names every type whose full name starts with it.
            public bool Prefix { get; }
            internal bool Matches(string type) => Prefix ? type.StartsWith(Component, StringComparison.Ordinal) : type == Component;
        }
        public static readonly IReadOnlyList<Exclusion> Exclusions = Array.AsReadOnly(new[] {
            new Exclusion(PadBuilder, "crossing pad", CrossingPadReason),
            new Exclusion("Model.StationAgent", "station click spot", StationAgentReason),
            new Exclusion(ConstructionController, "construction controller", ConstructionOutsideBridge),
            new Exclusion(SpawnPoints.Component, "spawn point", SpawnPointReason),
            new Exclusion("UI.Map.MapIcon", "map icon", MapIconReason),
        });
        // Why a group with a map icon is left out. Station map icons, in the Station Icons holder, have their own family;
        // a map icon elsewhere is in no family and has no edit.
        public const string MapIconReason = "This group contains a map icon, which the patcher can't move with the group.";
        // Why a group with a station click spot is left out: in Railroader 0.6.3 station click spots are native map data,
        // which the native map editor edits. The group's Placement tab gives this reason instead of its controls.
        public const string StationAgentReason = "This group can't be moved as a whole because it contains a station click spot, which is native map data. Edit station click spots in the Map Editor.";
        // Why a group with a crossing pad outside a crossing is left out: its track binding places the pad, so the pad
        // can't move with the group. A track marker without a pad moves with its group and keeps its binding.
        public const string CrossingPadReason = "This group can't be moved as a whole because it contains a crossing pad, which its track binding places. " + WindowGuidance.EditCrossingPadSeparately;
        // Why a group with a spawn point is left out: its spawn points are edited one by one in their own family.
        public const string SpawnPointReason = "This group can't be moved as a whole because it contains spawn points. " + WindowGuidance.EditSpawnPointsIndividually;
        // Why a group or part that holds track markers takes no activity: disabling it would deactivate its markers, which
        // then leave the track's registrations, so its markers stop working. It moves and rotates as a whole.
        public const string MarkerActivityRefusal = "This object contains track markers, so you can't enable or disable it. Disabling it would take its markers off the track.";
        // Whether a track marker stands at or below the node, on an object the scene serializes.
        public static bool HoldsTrackMarker(IHierarchyNode node) {
            if (node == null) return false;
            var types = new HashSet<string>(StringComparer.Ordinal);
            Collect(node, types);
            return types.Contains(Marker);
        }
        // A road bridge, wherever it stands: a root whose name contains Road Bridge. Its road is native
        // map data; the bridge carries a renderer culler and its own scene ground mask.
        public static bool RoadBridge(string name) => name != null && name.IndexOf("Road Bridge", StringComparison.Ordinal) >= 0;
        // A rail bridge outside Ela, wherever it stands: a root whose name holds the word Bridge and that is no road
        // bridge, such as Bridge 1, Almond Bridge and Plate Girder Bridge End. The rule is this one's own: the words
        // the outliner files objects by (SceneClassification) do not decide what a group's edits may do. town is the
        // name of the scene object below World/Large Scenery it stands in; it decides nothing.
        static readonly Regex BridgeWord = new Regex(@"\bBridge\b", RegexOptions.CultureInvariant);
        public static bool RailBridge(string town, string name) => name != null && !RoadBridge(name) && BridgeWord.IsMatch(name);
        // The construction controller of a rail bridge (Game.Mow.TrackConstructionScenery): it switches the objects of
        // its entries on and off by the construction state of the track segments it names, wherever it stands.
        public const string ConstructionController = "Game.Mow.TrackConstructionScenery";
        // Why a group that holds a construction controller but is no supported rail bridge construction is left out. The
        // group's Placement tab gives this reason instead of its controls.
        public const string ConstructionOutsideBridge = "This group can't be moved as a whole because it contains a construction controller and isn't a supported rail bridge.";
        // Why a rail bridge or a part of one takes no track-following placement: its track is native map
        // data, and the bridge is placed onto the moved track with fixed placement.
        public const string RailBridgeNoFollowing = "Rail bridges use fixed placement. They don't support track-following placement.";
        // Why a road bridge or a part of one takes no track-following placement: it stands where its road
        // goes, which is native map data, and track following follows track.
        public const string RoadBridgeNoFollowing = "Road bridges use fixed placement. They don't support track-following placement.";
        // Why a bridge, or a part of one, takes no track-following placement, or null: a bridge of either kind is
        // placed onto its moved track or road with fixed placement.
        public static string FollowingRefusal(BridgeKind bridge) =>
            bridge == BridgeKind.Rail ? RailBridgeNoFollowing : bridge == BridgeKind.Road ? RoadBridgeNoFollowing : null;
        // Why a rail bridge with track-owned components in its hierarchy cannot be edited: the track and
        // what the game builds for it belong to the native map data, which a bridge move must leave in place. A track
        // marker and its relative position are no part of the track: a marker moves with its group and keeps its binding.
        static readonly string[] MarkerComponents = { "Track.TrackMarker", "Track.TrackRelativePosition" };
        public static string TrackOwnedRefusal(IEnumerable<string> types) {
            var owned = (types ?? Enumerable.Empty<string>()).FirstOrDefault(type => type != null && !MarkerComponents.Contains(type) && (type.StartsWith("Track.", StringComparison.Ordinal) || type.StartsWith("AutoTrestle.", StringComparison.Ordinal)));
            return owned == null ? null : "This rail bridge contains the track-owned component " + owned + ", which belongs to the native track. The bridge can't be moved, enabled, or disabled.";
        }
        public const string CrossingController = "Track.Crossings.GradeCrossingController", PadBuilder = "Track.TrackCurveMeshBuilder", Marker = "Track.TrackMarker",
            RendererCuller = CullerTypes.RendererCuller, GroupHolderRule = "Objects that only hold other scene objects";
        // Crossbucks at unprotected crossings: each crossbuck is a group of its own under Grade crossing, wherever it
        // stands below World. Its models, the LOD levels below it, belong to its entry and are no parts.
        public const string CrossbuckModels = "A crossbuck's models belong to its entry.";
        // The name of a crossbuck's entry: the crossbuck's own name at its crossing, as a crossing mast is named, so the
        // crossbucks of the base map read apart and a search finds them by crossing name. Where crossings at other paths
        // share the crossing's name, such as the two Crossing Unprotected - Robbinsville crossings, the name adds the
        // place the crossing stands at (CrossingEquipment.Place). A crossbuck that the pad marker ID identifies names
        // that id. The full path stays in the tooltip.
        public static string CrossbuckName(string path, string padMarkerId = null, ISet<string> sharedCrossingNames = null) {
            if (path == null) return null;
            var last = path.LastIndexOf('/');
            if (last <= 0) return path;
            var crossing = path.LastIndexOf('/', last - 1);
            var name = path.Substring(crossing + 1, last - crossing - 1);
            var place = sharedCrossingNames != null && sharedCrossingNames.Contains(name) && crossing > 0
                ? " in " + CrossingEquipment.Place(null, path.Substring(0, crossing).Split('/').Reverse().ToArray()) : "";
            return path.Substring(last + 1) + " at " + name + place + (padMarkerId == null ? "" : " (pad marker " + padMarkerId + ")");
        }
        // The crossing names that crossings at two or more hierarchy paths hold, from the paths of the listed crossbucks.
        // The crossings that share one path, as below Old River Rd, are told apart by their pad marker ID instead.
        public static ISet<string> SharedCrossingNames(IEnumerable<string> crossbuckPaths) {
            var crossings = (crossbuckPaths ?? Enumerable.Empty<string>()).Where(path => path != null && path.LastIndexOf('/') > 0)
                .Select(path => path.Substring(0, path.LastIndexOf('/'))).Distinct(StringComparer.Ordinal);
            return new HashSet<string>(crossings.GroupBy(crossing => crossing.Substring(crossing.LastIndexOf('/') + 1), StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.Ordinal);
        }
        // Why a target with a pad marker ID cannot name a crossbuck, from the crossbucks listed at its path, or null. The
        // id identifies a crossbuck only where several crossbucks share the path: a crossbuck with a path of its own is
        // named by its path alone, so the identity holds one identifier, never two.
        public const string PadMarkerUnshared = "This crossbuck's path identifies it uniquely, so its target doesn't accept padMarkerId. " + WindowGuidance.RemovePadMarkerId;
        public static string PadMarkerRefusal(int crossbucksAtPath) =>
            crossbucksAtPath == 0 ? TargetIdentity.PadMarkerNotAccepted : crossbucksAtPath == 1 ? PadMarkerUnshared : null;
        static bool Protected(IHierarchyNode node) => node.Components.Contains(CrossingController);
        static bool Pad(IHierarchyNode node) => node.Components.Contains(PadBuilder);
        static bool MarkerObject(IHierarchyNode node) => node.Components.Contains(Marker);
        // A crossbuck carries its own renderer culler and no component of an existing family.
        static bool Crossbuck(IHierarchyNode node) => !node.HasFamily && node.Components.Contains(RendererCuller);
        // An unprotected crossing: one or more crossing pads, and otherwise only whistle signs and crossbucks.
        static bool Unprotected(IHierarchyNode node) =>
            node.Children.Any(Pad) && node.Children.All(child => Pad(child) || MarkerObject(child) || Crossbuck(child));
        static bool Crossing(IHierarchyNode node) => Protected(node) || Unprotected(node);
        // The marker ID of the crossing's pad: the one id its pads hold, or null.
        static string PadMarkerId(IHierarchyNode crossing) {
            var ids = crossing.Children.Where(Pad).Select(pad => pad.MarkerId).Where(id => !String.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
            return ids.Length == 1 ? ids[0] : null;
        }
        static bool RoadBridge(IHierarchyNode node) => RoadBridge(node.Name);
        // One or more crossings, or crossings and road bridges.
        static bool CrossingGroupHolder(IHierarchyNode node) =>
            !node.HasFamily && node.Children.Any(Crossing) && node.Children.All(child => Crossing(child) || RoadBridge(child));
        static void Candidate(string town, IHierarchyNode node, string path, List<SceneObjectVerdict> verdicts) {
            if (!node.Serialized) { verdicts.Add(new SceneObjectVerdict(node, path, GroupDisposition.NotListed, RuntimeObject)); return; }
            if (node.Components.Contains(MapObject)) { verdicts.Add(new SceneObjectVerdict(node, path, GroupDisposition.NotListed, NativeMapData)); return; }
            if (node.HasFamily) { verdicts.Add(new SceneObjectVerdict(node, path, GroupDisposition.ExistingFamily, null)); return; }
            if (SceneObjectCategories.WithoutFunction.Contains(path)) { verdicts.Add(new SceneObjectVerdict(node, path, GroupDisposition.NotListed, NoFunction)); return; }
            // A holder, such as Bryson Yard, is no group: its children are. It keeps a verdict of its own, so
            // a modification that names it fails with its reason.
            var container = GroupHolder(node);
            if (container) verdicts.Add(new SceneObjectVerdict(node, path, GroupDisposition.GroupHolder, GroupHolderRule));
            if (container || CrossingGroupHolder(node)) { foreach (var child in node.Children) Candidate(town, child, path + "/" + child.Name, verdicts); return; }
            if (Unprotected(node)) {
                var pad = PadMarkerId(node);
                foreach (var child in node.Children) {
                    var at = path + "/" + child.Name;
                    if (!child.Serialized) verdicts.Add(new SceneObjectVerdict(child, at, GroupDisposition.NotListed, RuntimeObject));
                    else if (child.HasFamily) verdicts.Add(new SceneObjectVerdict(child, at, GroupDisposition.ExistingFamily, null));
                    else if (MarkerObject(child)) verdicts.Add(new SceneObjectVerdict(child, at, GroupDisposition.NotListed, GroupHolderRule));
                    // Every other child is a crossbuck: a group of its own under Grade crossing, wherever the crossing stands.
                    else verdicts.Add(new SceneObjectVerdict(child, at, GroupDisposition.Group, null) { Category = SceneObjectCategories.GradeCrossing, Crossbuck = true, PadMarkerId = pad });
                }
                return;
            }
            // Physical presence comes first: an object without a renderer, collider or light is not listed, whatever it holds.
            if (!Physical(node)) { verdicts.Add(new SceneObjectVerdict(node, path, GroupDisposition.NotListed, NoPhysicalPresence)); return; }
            // A bridge is a bridge by this rule wherever it stands, as the road bridges below the World/Roads
            // holders are. Its category in the outliner follows from it, not the other way round.
            var road = RoadBridge(node.Name);
            var rail = RailBridge(town, node.Name);
            // A supported rail bridge construction holds its own construction controller; any other group holding one is left out.
            var excluded = FirstExclusion(node, rail ? ConstructionController : null);
            var verdict = excluded != null ? new SceneObjectVerdict(node, path, GroupDisposition.InspectionOnly, excluded.Reason, true) : new SceneObjectVerdict(node, path, GroupDisposition.Group, null);
            verdict.HoldsTrackMarkers = HoldsTrackMarker(node);
            if (road || rail) { verdict.Category = SceneObjectCategories.Bridge; verdict.Subcategory = road ? SceneObjectCategories.RoadBridges : SceneObjectCategories.RailBridges; verdict.Bridge = road ? BridgeKind.Road : BridgeKind.Rail; }
            verdicts.Add(verdict);
        }
        // A renderer, collider or light at or below the node, or a storage pile, on an object the scene serializes. What
        // the game builds while it runs does not count: a storage pile builds its collider only while the camera is
        // near, so counting that collider would list its group by camera distance. The pile itself counts.
        static bool Physical(IHierarchyNode node) => node.Serialized && (Present(node) || node.Children.Any(Physical));
        // A storage pile of an industry (Scenery.Pile) draws its logs through the game's instancer and builds its
        // collider only near the camera: the scene gives it no renderer or collider, but a track change runs into it.
        public const string StoragePile = "Scenery.Pile";
        // The kind of a bulk scenery object (Scenery.BulkScenery) for refusal and failure texts, by its type's full name.
        public static string BulkKind(string type) => type == LogPond ? "log pond" : "storage pile";
        // Why a group with storage piles or a log pond cannot be edited while the game's bulk scenery members that the
        // refresh uses are unavailable, naming the kinds the group holds.
        public static string BulkMembersRefusal(bool piles, bool ponds) =>
            piles && ponds ? "The game's storage pile and log pond members are unavailable. This group, its piles, and its log pond can't be moved, enabled, or disabled."
            : ponds ? "The game's log pond members are unavailable. This group and its log pond can't be moved, enabled, or disabled."
            : "The game's storage pile members are unavailable. This group and its piles can't be moved, enabled, or disabled.";
        // The box a log pond lays its logs out in, in its own coordinates, as centre and half extents: its width across
        // (X), its length plus width along (Z, the capsule's straight section and its two round ends), and one log's
        // radius up and down from the water line where the logs float (LogPond.ComputeLayout places them at height 0).
        public static Tuple<double[], double[]> LogPondBox(double width, double length, double radius) =>
            Tuple.Create(new[] { 0.0, 0, 0 }, new[] { width / 2, radius, (Math.Max(length, 0) + width) / 2 });
        // Why the move of a lake surface whose water mesh is part of a static batch is refused: the game draws the
        // batched mesh where the scene had it, whatever the transform says.
        public static string StaticBatchRefusal(string water) =>
            "The water surface " + water + " is part of a static batch. The game would keep drawing it at its old position, so this lake surface can't be moved.";
        // The logs of a log pond (Scenery.LogPond) are drawn the same way, without a collider: the scene gives the
        // object no renderer, but it shows the logs of the sawmill's storage inside the pond's water surface.
        public const string LogPond = "Scenery.LogPond";
        // A lake surface (LakePolygon) is a water mesh the scene saves with its object: native lake masks and rivers
        // shape and color the terrain, and the water surface is this scene object.
        public const string LakeSurface = "LakePolygon";
        // The object itself has physical presence: a renderer, collider or light on it, or it is a storage pile or the
        // logs of a log pond.
        public static bool Present(IHierarchyNode node) => node.Physical || node.Components.Contains(StoragePile) || node.Components.Contains(LogPond);
        // The coverage rules that keep an object below a listed top scene object out of the part entries.
        public const string AmbiguousParts = "Child objects with the same name and parent share a path", LodLevels = "LOD levels belong to the object with the LOD group",
            AnimatedParts = "Child objects the game moves while running", CullerOnlyChildren = "Child objects that only carry a simple culler";
        // The game's cullers: a simple culler switches its own object, a renderer culler the renderers below it and a
        // game-object culler the objects on its list. Only a simple culler makes a child object one that only carries a simple culler: a renderer
        // culler sits on the root of a prefab that culls its own children, such as a lamp post (Tall Post Shade), a
        // crossbuck or a sign, and that root is the object a modder moves: a child object where it stands below a top
        // scene object, and a top scene object where it stands in a top scene object's position, as the yard lamp posts of Bryson Yard do.
        public const string SimpleCuller = CullerTypes.SimpleCuller, GameObjectCuller = CullerTypes.GameObjectCuller;
        public static readonly IReadOnlyList<string> Cullers = Array.AsReadOnly(new[] { SimpleCuller, RendererCuller, GameObjectCuller });
        // A child object that only carries a simple culler, such as Bryson House Track/Culled, is a scene object below a top scene object that only holds
        // other objects for a simple culler: it carries a simple culler, has no renderer, collider or light of its own
        // and has children. It is no object a modder moves; its parts stay listed under their group. A top scene object that
        // carries the culler itself, such as Sylva Culled, stays the top scene object.
        public static bool CullerOnlyChild(IHierarchyNode node) =>
            node != null && !Present(node) && node.Children.Count != 0 && node.Components.Contains(SimpleCuller);
        // Why a modification cannot edit the object at a path the part rule did not list, or null for a listed part.
        public const string CullerOnlyRefusal = "This path names an object that only carries a simple culler, which isn't an editable part. " + WindowGuidance.EditCulledParts;
        // Whether a path resolves to the object the part rule decided this for: a listed part, and a child object that only carries a simple culler,
        // so that its modification fails with the container's reason instead of finding nothing.
        public static bool Resolves(PartVerdict verdict) => verdict?.Disposition == PartDisposition.Part || PartRefusal(verdict) != null;
        public static string PartRefusal(PartVerdict verdict) => verdict?.Reason == CullerOnlyChildren ? CullerOnlyRefusal : null;
        // Why a modification cannot edit, as a group, the object the group rule did not list, or null: the object is
        // a holder, such as Bryson Yard.
        public const string GroupHolderRefusal = "This path names an object that only contains other groups, which isn't a group itself. " + WindowGuidance.EditHeldGroups;
        public static string GroupRefusal(SceneObjectVerdict verdict) => verdict?.Disposition == GroupDisposition.GroupHolder ? GroupHolderRefusal : null;
        // Whether an object resolves as a group target: a listed top scene object and a holder, so that a
        // holder's modification fails with its reason instead of finding nothing.
        public static bool ResolvesGroup(SceneObjectVerdict verdict) => verdict?.Disposition == GroupDisposition.Group || GroupRefusal(verdict) != null;
        // Why a modification cannot edit, as a part, the object at a path: a listed top scene object, such as a yard lamp
        // post of Bryson Yard, is edited as a group; otherwise the part rule's own reason, or null for a listed part.
        public const string TopObjectRefusal = "This path names a group, not a part. " + WindowGuidance.EditAsGroup;
        public static string PartRefusal(PartVerdict verdict, SceneObjectVerdict group) =>
            group?.Disposition == GroupDisposition.Group ? TopObjectRefusal : PartRefusal(verdict);
        public static bool Resolves(PartVerdict verdict, SceneObjectVerdict group) => verdict?.Disposition == PartDisposition.Part || PartRefusal(verdict, group) != null;
        // Every scene object below the roots of the listed groups, at any depth, with what the part rule decided for it.
        // A part is one scene object, not a renderer or a component. An inspection-only group has no part entries.
        public static IReadOnlyList<PartVerdict> Parts(IEnumerable<SceneObjectVerdict> verdicts) {
            if (verdicts == null) throw new ArgumentNullException(nameof(verdicts));
            var found = new List<PartVerdict>();
            // A crossbuck is one entry: its models are no parts.
            foreach (var group in verdicts.Where(v => v.Disposition == GroupDisposition.Group)) Walk(group.Node, group.Path, group.Path, group.Crossbuck ? CrossbuckModels : null, found);
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
                // A transform the game animates, such as a clock hand, changes while nothing edits it, and so does
                // everything below it: as a part it would make the inventory stale by itself. It moves with its group.
                if (child.Animated || Animates(child.Components)) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, AnimatedParts)); Walk(child, path, groupPath, AnimatedParts, found); continue; }
                if (shared.Contains(child.Name)) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, AmbiguousParts)); Walk(child, path, groupPath, AmbiguousParts, found); continue; }
                // A level of a LOD group belongs to the entry of the object that holds the LOD group.
                if (child.LodLevel) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, LodLevels)); continue; }
                if (child.HasFamily) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.ExistingFamily, null)); continue; }
                // A child object that only carries a simple culler is no part, but the objects in it are: they stay listed under their group.
                if (CullerOnlyChild(child)) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, CullerOnlyChildren)); Walk(child, path, groupPath, null, found); continue; }
                // The object that holds a scene ground mask is a part without a renderer, collider or light: moving it
                // moves the terrain shaping.
                if (!Physical(child) && !HoldsGroundMask(child.Components)) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, NoPhysicalPresence)); continue; }
                found.Add(new PartVerdict(child, path, groupPath, PartDisposition.Part, null));
                Walk(child, path, groupPath, null, found);
            }
        }
        // The components that animate the object they stand on, and everything below it, while the game runs. Railroader
        // 0.6.3: a KeyValueBoolAnimator plays its clip on its own Animator whenever its key changes, as the doors of a
        // roundhouse stall do when they open. The game's adapter adds these objects to
        // the transforms it treats as animated (GroupWorld.AddDriven), so the part rule and the group observation agree.
        public static readonly IReadOnlyList<string> Animators = Array.AsReadOnly(new[] { "RollingStock.Controls.KeyValueBoolAnimator" });
        public static bool Animates(IEnumerable<string> components) => components != null && components.Any(type => type != null && Animators.Contains(type));
        // A component field that names a transform the game turns or moves every frame, wherever it stands relative to the
        // component: a gauge's needle (the clock hand at Bryson depot), and the filament renderer that a lantern's
        // LanternController turns to the camera and scales (Railroader 0.6.3, Effects.LanternController.Update). The
        // field holds the transform itself or a component on it. The game's adapter (GroupWorld.AddDriven) marks these
        // transforms animated, so they and everything below them are no parts.
        public sealed class DrivenField {
            internal DrivenField(string component, string field) { Component = component; Field = field; }
            public string Component { get; }
            public string Field { get; }
        }
        public static readonly IReadOnlyList<DrivenField> DrivenFields = Array.AsReadOnly(new[] {
            new DrivenField("RollingStock.GaugeNeedle", "needle"), new DrivenField("Effects.LanternController", "filamentRenderer") });
        // The name of a part entry: the top scene object's name, then the path from the top scene object to the part.
        public static string PartName(string groupPath, string path) {
            if (groupPath == null || path == null || !path.StartsWith(groupPath + "/", StringComparison.Ordinal)) return path?.Substring(path.LastIndexOf('/') + 1);
            return groupPath.Substring(groupPath.LastIndexOf('/') + 1) + " / " + path.Substring(groupPath.Length + 1).Replace("/", " / ");
        }
        // What the inspector says about the culling of a part.
        public const string PartCullingDependency = "Culling and colliders move with the part. A part moved outside the sphere of a culler above it keeps its own culling, and that culler keeps its radius.";
        // The inventory note for a simple culler whose handler other code replaced while the patcher listened to it: the
        // patcher's handler stays in the chain and only passes the game's events on. Information, not a warning.
        // A failed update of own culling, for the group it names, or for moved parts in general when no group is known.
        public static string CullingUpdateFailure(string group) =>
            "The patcher couldn't update visibility for " + (String.IsNullOrEmpty(group) ? "moved parts" : "moved parts of " + group) + ". Report this to the patcher maintainer";
        public static string LeftoverWrapper(string culler) => "Released visibility rules for game culler " + culler + "; another component replaced its handler; replacement handler retained";
        // A scene ground mask is a component in this namespace (Map.Runtime.MaskComponents): it registers a shape in
        // the game's terrain system where its object stands, which shapes the ground and the vegetation there. A group
        // that holds one is a group of the family, and the object that holds it is a part.
        public const string GroundMasks = "Map.Runtime.MaskComponents.";
        public static bool HoldsGroundMask(IEnumerable<string> components) => components != null && components.Any(type => type != null && type.StartsWith(GroundMasks, StringComparison.Ordinal));
        // The first exclusion, in the order of the list, that a component at or below the node matches.
        static Exclusion FirstExclusion(IHierarchyNode node, string allowed = null) {
            var types = new HashSet<string>(StringComparer.Ordinal);
            Collect(node, types);
            return Exclusions.FirstOrDefault(e => e.Component != allowed && types.Any(e.Matches));
        }
        // Only the scene's own objects count, so a group's reason does not change with what the game builds while it runs.
        static void Collect(IHierarchyNode node, HashSet<string> types) {
            if (!node.Serialized) return;
            foreach (var type in node.Components) if (type != null) types.Add(type);
            foreach (var child in node.Children) Collect(child, types);
        }
    }

    // One scene object below a top scene object, observed with copied values.
    public sealed class GroupPart {
        public GroupPart(string id, IEnumerable<double> rootPosition, IEnumerable<double> rootRotation, bool activeSelf, bool cullerSwitched, object handle = null, object parent = null, bool animated = false) {
            Id = id ?? throw new ArgumentNullException(nameof(id)); RootPosition = rootPosition.ToArray(); RootRotation = rootRotation.ToArray();
            ActiveSelf = activeSelf; CullerSwitched = cullerSwitched; Handle = handle; Parent = parent; Animated = animated;
        }
        // The game animates this object or one above it below the root, so its placement is no part of the invariant.
        public bool Animated { get; }
        // Stable for the loaded object: hierarchy path below the root and instance.
        public string Id { get; }
        // Placement relative to the top scene object.
        public IReadOnlyList<double> RootPosition { get; }
        public IReadOnlyList<double> RootRotation { get; }
        // The part's own activity as the scene or the modder set it: for a part that own culling switches, the value
        // own culling holds for it, never what the camera made of it.
        public bool ActiveSelf { get; }
        // A culler of the game switches this part by camera distance, so its own activity is no part of the invariant.
        public bool CullerSwitched { get; }
        // The object, as the part capability resolves it, and the handle of the part it stands below, or null directly
        // below the top scene object.
        public object Handle { get; }
        public object Parent { get; }
    }
    // One culler in a group: the culling sphere it holds now and where the culler stands, both in map coordinates.
    public sealed class GroupCuller {
        public GroupCuller(string id, IEnumerable<double> registered, IEnumerable<double> expected, double? radius = null, double? authored = null, object handle = null, IEnumerable<object> listed = null,
            CullerKind kind = CullerKind.Simple, object culler = null, IEnumerable<ObservedRegion> regions = null) {
            Id = id ?? throw new ArgumentNullException(nameof(id)); Registered = registered?.ToArray(); Expected = expected.ToArray(); Radius = radius; Authored = authored;
            Handle = handle; Listed = (listed ?? Enumerable.Empty<object>()).ToArray(); Kind = kind; Culler = culler; Regions = (regions ?? Enumerable.Empty<ObservedRegion>()).ToArray();
        }
        // The object the culler stands on: the top scene object or a part. Listed holds the objects a game-object culler
        // switches: those in its list and those own culling took out of it.
        public object Handle { get; }
        public IReadOnlyList<object> Listed { get; }
        public CullerKind Kind { get; }
        // The culler itself, as own culling addresses it.
        public object Culler { get; }
        // The own spheres of moved parts that left this culler's sphere.
        public IReadOnlyList<ObservedRegion> Regions { get; }
        public string Id { get; }
        // The centre of the culling sphere the game holds for it, or null while it holds none.
        public IReadOnlyList<double> Registered { get; }
        public IReadOnlyList<double> Expected { get; }
        // The radius of its culling sphere now, and the radius the scene gives the culler. They are equal: no edit
        // changes a culler's radius. Null when the culler holds no radius.
        public double? Radius { get; }
        public double? Authored { get; }
    }
    public sealed class GroupObservation {
        public GroupObservation(ObjectState root, IEnumerable<GroupPart> parts, IEnumerable<GroupCuller> cullers, object parent, bool? rootCulling = null, IEnumerable<ColliderState> colliders = null, string separateScenery = null) {
            Root = root ?? throw new ArgumentNullException(nameof(root)); Parts = parts.ToArray(); Cullers = cullers.ToArray(); Parent = parent; RootCulling = rootCulling;
            Colliders = (colliders ?? Enumerable.Empty<ColliderState>()).ToArray(); SeparateScenery = separateScenery;
        }
        // For a rail bridge, a description of the separate track-owned scenery beside it, such as the
        // generated trestles of its track, which no edit may change; null for any other group.
        public string SeparateScenery { get; }
        // The colliders the scene saves at and below the root, for a group whose collider state every edit verifies: a
        // rail or road bridge. Empty for any other group. Their placement is the parts' placement.
        public IReadOnlyList<ColliderState> Colliders { get; }
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
        // The map placement of the object the part stands directly below: its top scene object or the part above it. A part
        // without a placement of its own keeps its place relative to this object.
        public Placement ParentPlacement { get; }
        // The part's placement in map coordinates and its own activity as the scene authored it.
        public ObjectState Part { get; }
        // The top scene object the part stands in, as the group capability resolves it.
        public object Group { get; }
        public GroupObservation GroupState { get; }
        // The object the part stands directly below.
        public object Parent { get; }
    }
    // The game-facing operations of the scene object with child objects and part capabilities. Positions are map coordinates.
    // IDisableRefusal: why `active: false` cannot hold against a switch of the game that names the top scene object or part, or
    // null.
    public interface IGroupWorld : IDisableRefusal {
        IEnumerable<string> MapIds { get; }
        // The loaded top scene objects of the Scene object family at the scene and path.
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
        // Why the top scene object or part takes no track-following placement, or null: a rail or road bridge
        // and its parts use fixed placement. Refused before any mutation.
        string FollowingRefusal(object target);
        // Why the placement of the top scene object or part cannot be set, or null: a lake surface at or below it whose water
        // mesh is part of a static batch would stay drawn at its old place while every transform check passes. A
        // placement is then refused before any mutation; activity stays editable.
        string PlacementRefusal(object target);
        PartObservation ObservePart(object part);
        // Sets the part's own activity; its cullers register and release through their own lifecycle.
        void SetPartActive(object part, bool active);
        // Sets the part's placement and synchronises the physics state; everything below the part moves with it.
        void SetPartPlacement(object part, Placement placement);
        // Moves the culling sphere of every culler in the group to its culler, each at the radius the scene gives it,
        // and brings the own culling of moved parts to the plan.
        void RefreshCulling(object group, CullingPlan plan);
        // The corners, in map coordinates, of the renderer, collider, light and storage pile bounds at and below a part,
        // leaving out the named parts and what stands below them. A renderer or collider whose bounds cannot be read, such
        // as a renderer without a mesh on an inactive object, counts as one point at its object. Empty when none of these
        // is there: the object has no presence of its own, which own culling reads from the empty list.
        IReadOnlyList<IReadOnlyList<double>> Presence(object part, IReadOnlyList<object> without);
        // The part's position in map coordinates: own culling places the sphere of a moved part without presence there.
        IReadOnlyList<double> Anchor(object part);
        // Whether the part stands below the object, a top scene object or another part, and still exists.
        bool Below(object part, object above);
        // Whether the object, a top scene object or a part, still exists.
        bool Alive(object handle);
    }
    // A group world whose objects can stand in another frame than their top scene object, or in a mod object. Without
    // it every top scene object stands in no frame and outside every mod object, and an object's placement is read from
    // its observation.
    public interface IFrameWorld {
        // The object above a top scene object that it moves with, such as the crossing object above a crossbuck, or null.
        object FrameAbove(object handle);
        // The mod object at or above the object, or null.
        object ModObjectOf(object handle);
        // The object's placement in map coordinates.
        Placement PlacementOf(object handle);
    }
    // The own placements of parts, by part: a part that a modification gives a placement of its own. The rule that every
    // capability with own placements shares: the recorded placement says where the part stands while the object above it,
    // its frame, stands at its map placement. So a part with its own placement moves with its frame and keeps its
    // placement relative to it, whatever moves the frame and in whatever order the scene patch files and their
    // modifications apply. The frame is the top scene object of a part, the controller of crossing equipment, or the
    // crossing object of a crossbuck. A frame's map placement is where it stood before the patcher moved it; a frame in
    // a mod object counts from that mod object, which stands at the origin. Without an own placement a part moves with
    // the object directly above it. The capabilities of one scene that move a frame or its parts share one instance;
    // each captures the frame's reference (Remember) before it moves the frame.
    public sealed class PartHolds {
        sealed class ByReference : IEqualityComparer<object> {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        // The recorded placement of each held part.
        readonly Dictionary<object, Placement> held = new Dictionary<object, Placement>(new ByReference());
        // The frame of each held part, so a released part's group can let go of the part's own culling.
        readonly Dictionary<object, object> groups = new Dictionary<object, object>(new ByReference());
        // Where each held part stood in the object above it before it was held: the placement the scene gives it.
        readonly Dictionary<object, Placement> authored = new Dictionary<object, Placement>(new ByReference());
        // The held objects that are top scene objects, such as a crossbuck in its crossing object: placed as a group root.
        readonly HashSet<object> tops = new HashSet<object>(new ByReference());
        // The held parts that follow a track: their placement is the map placement the track gives them, which the
        // reconciliation of track following evaluates, so they keep it when their frame moves.
        readonly HashSet<object> followers = new HashSet<object>(new ByReference());
        // The reference placement of each frame, captured before the patcher moved it.
        readonly Dictionary<object, Placement> references = new Dictionary<object, Placement>(new ByReference());
        internal Placement Of(object part) => held.TryGetValue(part, out var placement) ? placement : null;
        internal object GroupOf(object part) => groups.TryGetValue(part, out var group) ? group : null;
        internal Placement AuthoredOf(object part) => authored.TryGetValue(part, out var placement) ? placement : null;
        internal bool Follows(object part) => part != null && followers.Contains(part);
        // placement is the recorded placement in the frame group, or for a part that follows a track its map placement;
        // top marks a top scene object held in its frame.
        internal void Set(object part, Placement placement, object group = null, Placement scene = null, bool top = false) {
            if (placement == null) { held.Remove(part); groups.Remove(part); authored.Remove(part); tops.Remove(part); followers.Remove(part); return; }
            held[part] = new Placement((double[])placement.Position.Clone(), (double[])placement.Rotation.Clone());
            if (group != null) groups[part] = group;
            if (scene != null) authored[part] = scene; else authored.Remove(part);
            if (top) tops.Add(part); else tops.Remove(part);
            if (placement.Following != null) followers.Add(part); else followers.Remove(part);
        }
        static readonly double[] Still = { 0, 0, 0, 1 };
        static Placement Placed(IGroupWorld world, object handle) => (world as IFrameWorld)?.PlacementOf(handle) ?? world.Observe(handle).Root.Placement;
        // Captures the frame's reference placement, once, before the frame moves.
        internal void Remember(IGroupWorld world, object frame) { if (frame != null) Reference(world, frame); }
        // The placement the frame counts from: its map placement before the patcher moved it. A mod object stands at the
        // origin, and a frame in another frame or in a mod object takes its place there as it stood at its first reading.
        internal Placement Reference(IGroupWorld world, object frame) {
            if (references.TryGetValue(frame, out var known)) return known;
            var frames = world as IFrameWorld;
            var modObject = frames?.ModObjectOf(frame);
            Placement reference;
            if (modObject != null && ReferenceEquals(modObject, frame)) reference = new Placement(new double[3], (double[])Still.Clone());
            else {
                var above = frames?.FrameAbove(frame) ?? modObject;
                var now = Placed(world, frame);
                reference = above == null ? now : GroupChecks.Carried(Placed(world, above), Reference(world, above), now);
            }
            return references[frame] = new Placement((double[])reference.Position.Clone(), (double[])reference.Rotation.Clone());
        }
        // The map placement now of a placement recorded in the frame.
        internal Placement InMap(IGroupWorld world, object frame, Placement recorded) => GroupChecks.Carried(Reference(world, frame), Placed(world, frame), recorded);
        // The recorded placement in the frame of a map placement now.
        internal Placement Recorded(IGroupWorld world, object frame, Placement inMap) => GroupChecks.Carried(Placed(world, frame), Reference(world, frame), inMap);
        // The held parts of the group whose placement differs from the placement the scene gives them: only these can
        // get culling of their own. A held part whose scene placement is not known counts as moved.
        internal IReadOnlyList<object> Moved(IGroupWorld world, object group, GroupObservation observed, object except = null) {
            var moved = new List<object>();
            foreach (var pair in Below(world, group)) {
                if (ReferenceEquals(pair.Key, except)) continue;
                var scene = AuthoredOf(pair.Key); var now = GroupChecks.InParent(observed, pair.Key);
                if (scene == null || now == null || !GroupChecks.SamePlacement(scene, now)) moved.Add(pair.Key);
            }
            return moved;
        }
        internal bool Contains(object part) => part != null && held.ContainsKey(part);
        // Drops the holds of parts and the references of frames that no longer exist, such as those of a world that was unloaded.
        internal void Prune(IGroupWorld world) {
            foreach (var gone in held.Keys.Where(part => !world.Alive(part)).ToList()) Set(gone, null);
            foreach (var gone in references.Keys.Where(frame => !world.Alive(frame)).ToList()) references.Remove(gone);
        }
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
        // Puts every held part below the object at its own placement in its frame as the frame stands now: outer parts first.
        internal void Reassert(IGroupWorld world, object above) {
            foreach (var pair in Below(world, above)) {
                var placement = followers.Contains(pair.Key) ? pair.Value : InMap(world, groups.TryGetValue(pair.Key, out var frame) ? frame : above, pair.Value);
                if (tops.Contains(pair.Key)) world.SetPlacement(pair.Key, placement); else world.SetPartPlacement(pair.Key, placement);
            }
        }
    }
    // The own-placement rule of PartHolds as the part capabilities read and apply a part's placement: the child objects of
    // a group and the existing equipment of a crossing.
    static class OwnPlacement {
        // A held part reads its own placement as recorded in its frame; any other part its map placement in the object
        // directly above it (parent), so a removal returns it to its place there.
        internal static Placement Read(IGroupWorld world, PartHolds holds, object target, object frame, Placement inMap, Placement parent) {
            if (inMap == null) return null;
            if (holds.Follows(target)) return inMap;
            if (holds.Contains(target)) return holds.Recorded(world, holds.GroupOf(target) ?? frame, inMap);
            return parent == null ? inMap : new Placement(inMap.Position, inMap.Rotation) { Frame = parent };
        }
        // The map placement a desired placement puts the part at now: a recorded one through its frame as the frame stands
        // now; a placement read in the parent is one already.
        internal static Placement InMap(IGroupWorld world, PartHolds holds, object frame, Placement desired) =>
            desired == null || desired.Frame != null || desired.Following != null ? desired : holds.InMap(world, frame, desired);
    }
    // The checks that the group and part capabilities share.
    static class GroupChecks {
        internal const double PositionTolerance = 0.01, AngleToleranceDegrees = 0.01, RegistrationTolerance = 0.1;
        // Every part keeps its identity and its place in its frame, and, unless a culler switches it, its activity; a part the
        // game animates keeps only its identity and activity. The frame of a part is the nearest part at or above it that
        // framed names; a framed part keeps its map placement, a part below one keeps its place relative to it, and every
        // other part keeps its place relative to the root. skip names a part that the edit changes itself.
        internal static string Layout(GroupObservation expected, GroupObservation current, Func<object, bool> framed, object skip = null) {
            if (!expected.Parts.Select(p => p.Id).SequenceEqual(current.Parts.Select(p => p.Id))) return "the parts of the group changed";
            var index = new Dictionary<object, int>(new Identity());
            for (var i = 0; i < current.Parts.Count; i++) if (current.Parts[i].Handle != null) index[current.Parts[i].Handle] = i;
            for (var i = 0; i < expected.Parts.Count; i++) {
                var a = expected.Parts[i]; var b = current.Parts[i];
                if (skip != null && ReferenceEquals(b.Handle, skip)) continue;
                var frame = -1;
                if (a.Animated || b.Animated) {
                    if (!a.CullerSwitched && !b.CullerSwitched && a.ActiveSelf != b.ActiveSelf) return "the activity of " + b.Id + " changed";
                    continue;
                }
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
        // The placement that stands in the frame now as the placement stood in the frame at from: to ∘ from⁻¹ ∘ placement.
        internal static Placement Carried(Placement from, Placement to, Placement placement) {
            var inverse = Quaternion.Conjugate(Quaternion.Normalize(from.Rotation));
            var local = Quaternion.Rotate(inverse, new[] { placement.Position[0] - from.Position[0], placement.Position[1] - from.Position[1], placement.Position[2] - from.Position[2] });
            var turn = Quaternion.Normalize(to.Rotation); var moved = Quaternion.Rotate(turn, local);
            return new Placement(new[] { to.Position[0] + moved[0], to.Position[1] + moved[1], to.Position[2] + moved[2] }, Quaternion.Normalize(Quaternion.Multiply(turn, Quaternion.Multiply(inverse, placement.Rotation))));
        }
        internal static (double[], double[]) InMap(Placement root, GroupPart part) {
            var turn = Quaternion.Normalize(root.Rotation); var offset = Quaternion.Rotate(turn, part.RootPosition);
            return (new[] { root.Position[0] + offset[0], root.Position[1] + offset[1], root.Position[2] + offset[2] }, Quaternion.Multiply(turn, part.RootRotation));
        }
        static (double[], double[]) Relative(GroupPart frame, GroupPart part) {
            var inverse = Quaternion.Conjugate(Quaternion.Normalize(frame.RootRotation));
            return (Quaternion.Rotate(inverse, new[] { part.RootPosition[0] - frame.RootPosition[0], part.RootPosition[1] - frame.RootPosition[1], part.RootPosition[2] - frame.RootPosition[2] }), Quaternion.Multiply(inverse, part.RootRotation));
        }
        // A part's placement in the object directly above it, the top scene object or another part; null for an unknown part.
        internal static Placement InParent(GroupObservation observed, object handle) {
            var part = observed.Parts.FirstOrDefault(p => ReferenceEquals(p.Handle, handle));
            if (part == null) return null;
            var above = part.Parent == null ? null : observed.Parts.FirstOrDefault(p => ReferenceEquals(p.Handle, part.Parent));
            if (above == null) return new Placement(part.RootPosition.ToArray(), part.RootRotation.ToArray());
            var (position, rotation) = Relative(above, part);
            return new Placement(position, rotation);
        }
        // The own culling the group needs now: a sphere of its own for each moved part that left a culler's sphere.
        // except leaves out a hold that is being released.
        internal static CullingPlan Plan(IGroupWorld world, PartHolds holds, object group, object except = null) {
            var observed = world.Observe(group);
            return CullingPlan.Compute(group, observed, holds.Moved(world, group, observed, except), world.Presence, world.Anchor);
        }
        // An enabled group's culling spheres stand at their cullers, each at the radius the scene gives it, and every
        // moved part the plan names has its own sphere at the part, enclosing it. A disabled group holds no sphere.
        internal static string Culling(GroupObservation current, bool active, CullingPlan plan) {
            foreach (var culler in current.Cullers) {
                if (culler.Radius != null && culler.Authored != null && Math.Abs(culler.Radius.Value - culler.Authored.Value) > PositionTolerance) return "the culling sphere of " + culler.Id + " left the radius the scene gives it";
                foreach (var region in culler.Regions) {
                    var planned = !active ? null : plan?.Regions.FirstOrDefault(r => r.Id == region.Id);
                    if (planned == null) return region.Id + " still has a culling sphere of its own";
                    // A sphere that waits for the game's culler to start is registered at the next update.
                    if (region.Pending) continue;
                    if (region.Centre == null || !Near(region.Centre, planned.Centre, RegistrationTolerance) || region.Radius + PositionTolerance < planned.Radius) return "the own culling sphere " + region.Id + " does not stand at its part";
                }
                if (culler.Registered == null) continue;
                if (!active) return culler.Id + " still has a culling sphere";
                if (!Near(culler.Registered, culler.Expected, RegistrationTolerance)) return "the culling sphere of " + culler.Id + " did not follow the group";
            }
            if (active && plan != null)
                foreach (var planned in plan.Regions)
                    if (!current.Cullers.Any(c => c.Regions.Any(r => r.Id == planned.Id))) return "the moved part has no culling sphere of its own: " + planned.Id;
            return null;
        }
        // Every collider keeps its identity, shape and enabled state, and its physics bounds follow its transform. The
        // separate track-owned scenery beside a rail bridge stays unchanged.
        internal static string Colliders(GroupObservation expected, GroupObservation current) {
            if (expected.SeparateScenery != current.SeparateScenery) return "the separate track-owned scenery beside the bridge changed";
            if (!expected.Colliders.Select(c => c.Id).SequenceEqual(current.Colliders.Select(c => c.Id))) return "the colliders of the group changed";
            for (var i = 0; i < current.Colliders.Count; i++) {
                var a = expected.Colliders[i]; var b = current.Colliders[i];
                if (a.Shape != b.Shape) return "the shape of the collider " + b.Id + " changed";
                if (a.Enabled != b.Enabled) return "the enabled state of the collider " + b.Id + " changed";
                if (!b.BoundsFollow) return "the physics shape of the collider " + b.Id + " does not follow its transform";
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
    // Scene objects with child objects: places and enables or disables a top scene object with everything below it. A group move is
    // physical placement only: the activity of the parts stays theirs, and a part with its own placement keeps it relative
    // to the group (PartHolds).
    // The root of a rail bridge construction that carries its construction controller, such as Bryson Bridge 1 and the
    // Almond Bridge, also holds the construction-segment relationship.
    // The crossbucks a world lists at a hierarchy path, with the marker ID of each one's crossing pad, for the identity
    // of crossbucks whose crossings share one path.
    public interface ICrossbuckWorld {
        IReadOnlyList<KeyValuePair<object, string>> CrossbucksAt(string scene, string path);
    }
    // Why a group takes no activity at all, enabled or disabled, or null: a group that holds track markers
    // (SceneObjectRules.MarkerActivityRefusal). Checked before any mutation.
    public interface IGroupActivityRefusal {
        string GroupActivityRefusal(object group);
    }
    // A top scene object that stands in a frame of its own, a crossbuck in its crossing object (IFrameWorld.FrameAbove),
    // follows the own-placement rule of PartHolds: with a placement of its own it moves with its crossing object and keeps
    // its placement relative to it; without one it keeps its place in the crossing object.
    public sealed class SceneObjects : IScene, IVersionedScene, IRelationshipScene, IConstructionSegmentScene, IDependentOutputScene, IPadMarkerIdentityScene, IFramedScene, IRecordedPlacementScene, IRetainedScene, IDisposable {
        public const string Structure = "scene-object-v1", ComponentType = "UnityEngine.Transform", RequiredVersion = "0.5.0";
        readonly IGroupWorld world;
        readonly PartHolds holds;
        // The construction controllers on top scene objects; null in a world without them.
        IConstructionSegmentWorld Segments => world as IConstructionSegmentWorld;
        // The scene ground masks of the groups, or null in a world without them.
        readonly GroundMasks masks;
        // The markers below a group whose track sets their place: a group move leaves them where they stand.
        readonly TrackPlacedObjects placed;
        public SceneObjects(IGroupWorld world, Capability capability = Capability.Unchecked, PartHolds holds = null, GroundMasks masks = null, TrackPlacedObjects placed = null) {
            this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; this.holds = holds ?? new PartHolds(); this.masks = masks; this.placed = placed;
        }
        // The terrain output that follows a ground mask of the group, by the edited top scene object.
        public DependentOutput Output(object target) => masks?.Output(target) ?? DependentOutput.None;
        public void RetryOutput(object target) => masks?.RetryOutput(target);
        // The capability ends: the world gives back what its own culling of moved parts took over from the game.
        public void Dispose() => (world as IDisposable)?.Dispose();
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != ComponentType || (identity.Structure != null && identity.Structure != Structure)) return Array.Empty<object>();
            if (identity.PadMarkerId == null) return world.Find(identity.Scene, identity.Path) ?? Array.Empty<object>();
            // A pad marker ID names the crossbucks at the shared path whose crossing's pad holds it.
            var crossbucks = Crossbucks(identity);
            return crossbucks.Count < 2 ? Array.Empty<object>() : crossbucks.Where(pair => pair.Value == identity.PadMarkerId).Select(pair => pair.Key).ToArray();
        }
        IReadOnlyList<KeyValuePair<object, string>> Crossbucks(TargetIdentity identity) =>
            (world as ICrossbuckWorld)?.CrossbucksAt(identity.Scene, identity.Path) ?? Array.Empty<KeyValuePair<object, string>>();
        public string PadMarkerRefusal(TargetIdentity identity) {
            if (identity.PadMarkerId == null) return null;
            if (identity.Component != ComponentType || (identity.Structure != null && identity.Structure != Structure)) return TargetIdentity.PadMarkerNotAccepted;
            return SceneObjectRules.PadMarkerRefusal(Crossbucks(identity).Count);
        }
        public void Preflight(object target) {
            var refusal = world.Refusal(target);
            if (refusal == SceneObjectRules.GroupHolderRefusal) throw new InvalidPatchDefinitionException(refusal);
            if (refusal != null) throw new InvalidOperationException(refusal);
            if (!GroupChecks.Finite(world.Observe(target).Root.Placement)) throw new InvalidOperationException("The group's map coordinates are unavailable");
        }
        // A group has no relationships but the construction segments of a construction controller on its root; a
        // placement the world refuses, such as the move of a lake surface whose water is part of a static batch, is
        // refused here, before any mutation.
        public void CheckRelationships(object target, ObjectState state) {
            ConstructionSegmentRule.Check(Segments, target, state?.Segments);
            if (state?.Marker != null) throw new InvalidOperationException("This object has no supported track-marker relationship.");
            if (state?.Coverage != null) throw new InvalidOperationException("This object has no supported crossing-coverage relationship.");
            if (state?.Equipment != null) throw new InvalidOperationException("This object has no supported crossing-equipment relationship.");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
            if (state?.Placement?.Following != null && world.FollowingRefusal(target) is string following) throw new InvalidOperationException(following);
            var refusal = (state?.Active == null ? null : (world as IGroupActivityRefusal)?.GroupActivityRefusal(target))
                ?? (state?.Placement == null ? null : world.PlacementRefusal(target))
                ?? (state?.Active == false ? world.DisableRefusal(target) : null);
            if (refusal != null) throw new InvalidOperationException(refusal);
        }
        sealed class Snapshot { internal GroupObservation Observed; internal ConstructionSegments Segments; internal Placement Held, Scene; }
        // The frame the top scene object stands in, such as a crossbuck's crossing object, or null.
        object FrameOf(object target) => (world as IFrameWorld)?.FrameAbove(target);
        static Placement PlacementOf(IGroupWorld world, object handle) => (world as IFrameWorld)?.PlacementOf(handle) ?? world.Observe(handle).Root.Placement;
        public object Capture(object target) {
            holds.Remember(world, target);
            return new Snapshot { Observed = world.Observe(target), Segments = ConstructionSegmentRule.Read(Segments, target), Held = holds.Of(target), Scene = holds.AuthoredOf(target) };
        }
        // A top scene object in a frame reads its own placement as recorded in that frame, and without one its map
        // placement in that frame, so a removal returns it to its place in the frame wherever the frame stands.
        public ObjectState Read(object target) {
            holds.Remember(world, target);
            var root = world.Observe(target).Root;
            var frame = FrameOf(target);
            var placement = root.Placement;
            if (frame != null && placement != null)
                placement = holds.Follows(target) ? placement : holds.Contains(target) ? holds.Recorded(world, frame, placement) : new Placement(placement.Position, placement.Rotation) { Frame = PlacementOf(world, frame) };
            return new ObjectState(placement, root.Active).WithSegments(ConstructionSegmentRule.Read(Segments, target));
        }
        public Placement FramePlacement(object target) { var frame = FrameOf(target); return frame == null ? null : PlacementOf(world, frame); }
        public Placement Recorded(object target, Placement inMap) { var frame = FrameOf(target); return frame == null || inMap == null ? inMap : holds.Recorded(world, frame, inMap); }
        public Placement InMap(object target, Placement recorded) { var frame = FrameOf(target); return frame == null || recorded == null || recorded.Frame != null || holds.Follows(target) ? recorded : holds.InMap(world, frame, recorded); }
        // A top scene object in a frame that no longer stands under its identity holds nothing any more.
        public void Release(object target) => holds.Set(target, null);
        public bool Unsettled(object target) => false;
        // The map placement a desired placement puts the top scene object at. In a frame, a recorded placement holds the
        // object in its frame and a placement read in the frame lets it go.
        Placement Hold(object target, Placement desired) {
            var frame = desired == null ? null : FrameOf(target);
            if (frame == null) return desired;
            if (desired.Frame != null) { holds.Set(target, null); return desired; }
            var scene = holds.Contains(target) ? holds.AuthoredOf(target) : GroupChecks.Carried(PlacementOf(world, frame), Origin, PlacementOf(world, target));
            var inMap = desired.Following != null ? desired : holds.InMap(world, frame, desired);
            holds.Set(target, desired, frame, scene, true);
            return inMap;
        }
        static readonly Placement Origin = new Placement(new double[3], new double[] { 0, 0, 0, 1 });
        // The top scene object's placement applies first; every part with its own placement below it then takes its place in
        // it, and every marker below it whose track sets its place is put back where it stood. Returns those markers with
        // where they stood.
        IReadOnlyList<TrackPlacedObjects.Kept> Place(object target, Placement placement) {
            var kept = placed?.Below(world, target) ?? Array.Empty<TrackPlacedObjects.Kept>();
            world.SetPlacement(target, placement);
            holds.Reassert(world, target);
            TrackPlacedObjects.PutBack(kept);
            return kept;
        }
        // The cullers follow the group, and each moved part outside its culler's sphere keeps a sphere of its own.
        CullingPlan Refresh(object target) {
            var plan = GroupChecks.Plan(world, holds, target);
            world.RefreshCulling(target, plan);
            return plan;
        }
        // The construction segments apply first: the controller then shows the construction's parts by their state, and
        // the layout that every later check compares with is the one after that.
        public void Apply(object target, ObjectState state) {
            if (state.Placement != null) GroupChecks.CheckPlacement(state.Placement);
            holds.Remember(world, target);
            ConstructionSegmentRule.Apply(Segments, target, state.Segments);
            var segments = ConstructionSegmentRule.Verify(Segments, target, state.Segments);
            if (segments != null) throw new InvalidOperationException("Applied construction segments did not verify: " + segments);
            var before = world.Observe(target);
            var masked = masks?.Observe(target);
            var active = state.Active ?? before.Root.Active ?? true;
            var placement = Hold(target, state.Placement);
            if (active && before.Root.Active != true) world.SetActive(target, true);
            var kept = placement != null ? Place(target, placement) : null;
            if (!active && before.Root.Active != false) world.SetActive(target, false);
            var plan = Refresh(target);
            var after = world.Observe(target);
            if (!GroupChecks.SamePlacement(after.Root.Placement, placement ?? before.Root.Placement) || after.Root.Active != active) throw new InvalidOperationException("Applied group placement or activity did not verify");
            // Every part, one with its own placement included, keeps its place relative to the root; only a marker whose
            // track sets its place keeps its map placement.
            var difference = GroupChecks.Layout(before, after, Fixed(kept)) ?? GroupChecks.Colliders(before, after) ?? GroupChecks.Culling(after, active, plan) ?? GroupChecks.RootCuller(after, active) ?? TrackPlacedObjects.Moved(kept);
            if (difference != null) throw new InvalidOperationException("The group did not move as one: " + difference);
            // The ground masks at and below the root register again where their holders stand now.
            masks?.Follow(target, target, masked);
        }
        public bool Restore(object target, object snapshot) {
            var taken = (Snapshot)snapshot; var captured = taken.Observed;
            try {
                if (!ReferenceEquals(world.Observe(target).Parent, captured.Parent)) return false;
                ConstructionSegmentRule.Apply(Segments, target, taken.Segments);
                if (ConstructionSegmentRule.Verify(Segments, target, taken.Segments) != null) return false;
                var masked = masks?.Observe(target);
                var root = captured.Root;
                var frame = FrameOf(target);
                if (frame != null) holds.Set(target, taken.Held, frame, taken.Scene, true);
                if (root.Active == true) world.SetActive(target, true);
                var kept = root.Placement != null ? Place(target, root.Placement) : null;
                if (root.Active == false) world.SetActive(target, false);
                var plan = Refresh(target);
                var current = world.Observe(target);
                masks?.Follow(target, target, masked);
                return TrackPlacedObjects.Moved(kept) == null && GroupChecks.SamePlacement(current.Root.Placement, root.Placement) && current.Root.Active == root.Active && GroupChecks.Layout(captured, current, Fixed(kept)) == null
                    && GroupChecks.Colliders(captured, current) == null && GroupChecks.Culling(current, root.Active != false, plan) == null && current.RootCulling == captured.RootCulling;
            } catch { return false; }
        }
        // The parts that keep their map placement when the top scene object moves: only the markers whose track sets their
        // place. A part with its own placement moves with it too.
        static Func<object, bool> Fixed(IReadOnlyList<TrackPlacedObjects.Kept> kept) => part => kept != null && kept.Any(one => ReferenceEquals(one.Target, part));
        public string MinimumPatcherVersion(TargetIdentity target) => VersionRequirement.For(target, Structure, ComponentType, RequiredVersion);
    }
    // Parts of scene objects with child objects: places and enables or disables one scene object inside a listed group. A part edit
    // is an ordinary scene-object edit; the top scene object and the other parts keep their placement and activity. The
    // part's own placement is recorded relative to its group and moves with the group when the group moves later
    // (PartHolds); the placement the part has without any modification stays relative to the object above it.
    public sealed class ChildObjects : IScene, IVersionedScene, IFramedScene, IRecordedPlacementScene, IRelationshipScene, IRetainedScene, IDependentOutputScene {
        public const string Structure = "child-object-v1", ComponentType = SceneObjects.ComponentType, RequiredVersion = "0.5.0";
        readonly IGroupWorld world;
        readonly PartHolds holds;
        // The scene ground masks of the groups, or null in a world without them.
        readonly GroundMasks masks;
        public ChildObjects(IGroupWorld world, PartHolds holds, Capability capability = Capability.Unchecked, GroundMasks masks = null) {
            this.world = world ?? throw new ArgumentNullException(nameof(world)); this.holds = holds ?? throw new ArgumentNullException(nameof(holds)); Capability = capability; this.masks = masks;
        }
        // The terrain output that follows a ground mask at or below the part, by the edited part.
        public DependentOutput Output(object target) => masks?.Output(target) ?? DependentOutput.None;
        public void RetryOutput(object target) => masks?.RetryOutput(target);
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != ComponentType || (identity.Structure != null && identity.Structure != Structure)) return Array.Empty<object>();
            var found = world.FindParts(identity.Scene, identity.Path) ?? Array.Empty<object>();
            // A top scene object resolves as a part only so that a modification that asks for a part fails with the reason.
            // An identity without a structure names the group, which the group capability resolves.
            return identity.Structure != null ? found : found.Where(part => world.PartRefusal(part) != SceneObjectRules.TopObjectRefusal).ToArray();
        }
        public void Preflight(object target) {
            var refusal = world.PartRefusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            var observed = world.ObservePart(target);
            if (!GroupChecks.Finite(observed.Part.Placement) || !GroupChecks.Finite(observed.GroupState.Root.Placement)) throw new InvalidOperationException("Map coordinates are unavailable for the part or its group");
        }
        // A part with its own placement reads it as recorded in its top scene object (PartHolds). Without one the part
        // stands in the object above it: its placement says so, and applied back it returns the part to its place there.
        public ObjectState Read(object target) {
            var observed = world.ObservePart(target);
            var placement = OwnPlacement.Read(world, holds, target, observed.Group, observed.Part.Placement, observed.ParentPlacement);
            // The activity of a part a game-object culler switches is the culler's: the part reads none, so neither the
            // underlying state nor a desired state ever holds one for it.
            return new ObjectState(placement, world.ActivityRefusal(target) != null ? null : observed.Part.Active);
        }
        // A part stands in the object directly above it, so a part below a moved part keeps its place in that part.
        public Placement FramePlacement(object target) => world.ObservePart(target).ParentPlacement;
        public Placement Recorded(object target, Placement inMap) => inMap == null ? null : holds.Recorded(world, world.ObservePart(target).Group, inMap);
        public Placement InMap(object target, Placement recorded) => recorded == null || recorded.Frame != null || holds.Follows(target) ? recorded : holds.InMap(world, world.ObservePart(target).Group, recorded);
        public void CheckRelationships(object target, ObjectState state) {
            if (state?.Marker != null) throw new InvalidOperationException("Parts do not support track-marker relationships");
            if (state?.Coverage != null) throw new InvalidOperationException("Parts do not support crossing-coverage relationships");
            if (state?.Equipment != null) throw new InvalidOperationException("Parts do not support crossing-equipment relationships");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
            if (state?.Placement?.Following != null && world.FollowingRefusal(target) is string following) throw new InvalidOperationException(following);
            var refusal = (state?.Active == null ? null : world.ActivityRefusal(target)) ?? (state?.Placement == null ? null : world.PlacementRefusal(target))
                ?? (state?.Active == false ? world.DisableRefusal(target) : null);
            if (refusal != null) throw new InvalidOperationException(refusal);
        }
        sealed class Snapshot { internal PartObservation Observed; internal Placement Held, Scene; }
        public object Capture(object target) => new Snapshot { Observed = world.ObservePart(target), Held = holds.Of(target), Scene = holds.AuthoredOf(target) };
        // The part's placement applies, then every held part below it takes its own placement again. scene is where
        // the part stood in the object above it before any hold.
        void Place(object target, object group, Placement placement, Placement held, Placement scene) {
            holds.Set(target, held, group, scene);
            world.SetPartPlacement(target, placement);
            holds.Reassert(world, target);
        }
        // The cullers of the group follow, and each moved part outside its culler's sphere keeps a sphere of its own.
        CullingPlan Refresh(object group, object except = null) {
            var plan = GroupChecks.Plan(world, holds, group, except);
            world.RefreshCulling(group, plan);
            return plan;
        }
        public void Apply(object target, ObjectState state) {
            var before = world.ObservePart(target);
            var desired = state.Placement;
            if (desired != null) GroupChecks.CheckPlacement(desired);
            var masked = masks?.Observe(before.Group);
            // A game-object culler switches this part by camera distance: its activity stays the culler's, whatever the
            // state says, so an edit never switches it on or off against the culler.
            var switched = world.ActivityRefusal(target) != null;
            var active = state.Active ?? before.Part.Active ?? true;
            if (!switched && active && before.Part.Active != true) world.SetPartActive(target, true);
            // A placement read in its parent returns the part there; any other placement is the part's own placement, recorded
            // in its top scene object, which holds it there. A part that is not held yet stands where the scene has it in the
            // object above it.
            var inMap = OwnPlacement.InMap(world, holds, before.Group, desired);
            if (desired != null) Place(target, before.Group, inMap, desired.Frame == null ? desired : null, holds.Contains(target) ? holds.AuthoredOf(target) : GroupChecks.InParent(before.GroupState, target));
            if (!switched && !active && before.Part.Active != false) world.SetPartActive(target, false);
            Verify(target, before, inMap ?? before.Part.Placement, switched ? (bool?)null : active, Refresh(before.Group));
            // The ground masks of the group register again where their holders stand now: the part's and those below it.
            masks?.Follow(target, before.Group, masked);
        }
        // active is null for a part whose activity a culler switches: its activity is not compared.
        void Verify(object target, PartObservation before, Placement placement, bool? active, CullingPlan plan) {
            var after = world.ObservePart(target);
            if (!GroupChecks.SamePlacement(after.Part.Placement, placement) || (active != null && after.Part.Active != active)) throw new InvalidOperationException("The part's placement or activity does not match the requested state");
            var group = before.GroupState.Root; var now = after.GroupState.Root;
            if (!ReferenceEquals(after.Group, before.Group) || !GroupChecks.SamePlacement(now.Placement, group.Placement) || now.Active != group.Active) throw new InvalidOperationException("Editing the part changed its group");
            var difference = GroupChecks.Layout(before.GroupState, after.GroupState, h => ReferenceEquals(h, target) || holds.Contains(h), target)
                ?? GroupChecks.Colliders(before.GroupState, after.GroupState) ?? GroupChecks.Culling(after.GroupState, now.Active != false, plan);
            if (difference != null) throw new InvalidOperationException("Editing the part changed more than the part itself: " + difference);
        }
        public bool Restore(object target, object snapshot) {
            var captured = (Snapshot)snapshot;
            try {
                var current = world.ObservePart(target);
                if (!ReferenceEquals(current.Parent, captured.Observed.Parent) || !ReferenceEquals(current.Group, captured.Observed.Group)) return false;
                var part = captured.Observed.Part;
                var masked = masks?.Observe(captured.Observed.Group);
                var switched = world.ActivityRefusal(target) != null;
                if (!switched && part.Active == true) world.SetPartActive(target, true);
                if (part.Placement != null) Place(target, captured.Observed.Group, part.Placement, captured.Held, captured.Scene);
                if (!switched && part.Active == false) world.SetPartActive(target, false);
                Verify(target, captured.Observed, part.Placement, switched ? (bool?)null : part.Active != false, Refresh(captured.Observed.Group));
                masks?.Follow(target, captured.Observed.Group, masked);
                return true;
            } catch { return false; }
        }
        // A part that no longer stands under its identity holds nothing any more, and its group, when the group still
        // stands, lets go of the part's own culling.
        // The culling refresh comes first: when it fails the hold stays, and the next load attempt releases it again.
        public void Release(object target) {
            var group = holds.GroupOf(target);
            if (group != null && world.Alive(group) && world.Refusal(group) == null) Refresh(group, target);
            holds.Set(target, null);
        }
        public bool Unsettled(object target) => false;
        public string MinimumPatcherVersion(TargetIdentity target) => VersionRequirement.For(target, Structure, ComponentType, RequiredVersion);
    }

    // The debug summary of the scene object rule's verdicts for one load attempt. The objects that share a verdict are
    // counted, and only a verdict with few objects names them, so a player's debug log stays readable.
    public static class SceneObjectChecks {
        public const int ListLimit = 30;
        // checks holds each candidate object's path with its verdict text.
        public static IEnumerable<string> Summary(IEnumerable<KeyValuePair<string, string>> checks, int listLimit = ListLimit) {
            var all = (checks ?? Enumerable.Empty<KeyValuePair<string, string>>()).ToArray();
            yield return "Checked " + all.Length + " candidate scene objects. Counts by result:";
            foreach (var verdict in all.GroupBy(c => c.Value, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)) {
                var count = verdict.Count();
                yield return count + (count == 1 ? " object: " : " objects: ") + verdict.Key;
                if (count > listLimit) continue;
                foreach (var path in verdict.Select(c => c.Key).OrderBy(p => p, StringComparer.Ordinal)) yield return "  " + path;
            }
        }
    }
}
