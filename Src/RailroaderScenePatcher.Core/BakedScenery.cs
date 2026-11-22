// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // Where a track span comes from, which decides whether the inventory lists it (BakedSceneryRules.SpanListed).
    public enum SpanOrigin {
        // Built by the game from the map's operations data while it runs; the scene does not serialize it.
        Runtime,
        // Added by the patcher for a crossing.
        Owned,
        // Serialized in the scene and held by a crossing controller, now or in the scene as authored: a detection span.
        Held,
        // Serialized in the scene and held by no crossing controller, now or in the scene as authored: a native track span.
        Unheld,
    }
    public static class SpanOrigins {
        // The origin of a span. Only a serialized span the patcher did not add asks who holds it.
        public static SpanOrigin Of(bool serialized, bool patcherOwned, Func<bool> held) =>
            patcherOwned ? SpanOrigin.Owned : !serialized ? SpanOrigin.Runtime : held() ? SpanOrigin.Held : SpanOrigin.Unheld;
    }
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
        // The game turns or moves this object's transform while it runs, such as the clock hand a gauge needle turns.
        bool Animated { get; }
        // The id of the track marker on this object itself, or null without one. Read only for the crossing pads of an
        // unprotected crossing.
        string MarkerId { get; }
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
        // The category the outliner lists the group under, and its subgroup there, such as Bridge and Rail bridges.
        // A listed group the category table does not name stands under Other. Null for an inspection-only group the
        // table does not name, which stays in the Inspection only group, and for an object that is no group.
        public string Category { get; internal set; }
        public string Subcategory { get; internal set; }
        // A crossbuck of an unprotected crossing: a group of its own under Grade crossing, whose models are no parts.
        public bool Crossbuck { get; internal set; }
        // For a crossbuck, the marker id of its crossing's pad, or null when the crossing has no pad with one id.
        public string PadMarkerId { get; internal set; }
    }
    // The categories of the Baked scenery group family and the category table of the Railroader 0.6.3 Large Scenery
    // catalogue: which scene object in a group-root position is a group, under which category, and which is a folder.
    public static class BakedSceneryCategories {
        public const string StationBuilding = "Station building", Platform = "Platform", Lights = "Lights", Sign = "Sign", GradeCrossing = "Grade crossing", Bridge = "Bridge",
            IndustryBuilding = "Industry building", StorageDisplay = "Storage display", RailroadServiceBuilding = "Railroad service building", Other = "Other";
        public const string RailBridges = "Rail bridges", RoadBridges = "Road bridges";
        // The categories in the order the outliner shows them.
        public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[] { StationBuilding, Platform, Lights, Sign, GradeCrossing, Bridge, IndustryBuilding, StorageDisplay, RailroadServiceBuilding, Other });
        static readonly IReadOnlyList<string> BridgeSubgroups = Array.AsReadOnly(new[] { RailBridges, RoadBridges }), None = Array.AsReadOnly(new string[0]);
        // The subgroups of a category, in the order the outliner shows them: only Bridge has them.
        public static IReadOnlyList<string> Subcategories(string category) => category == Bridge ? BridgeSubgroups : None;
        public sealed class Row {
            internal Row(string path, string category, string subcategory) { Path = path; Category = category; Subcategory = subcategory; }
            // The hierarchy path of the scene object, from World.
            public string Path { get; }
            // A folder is not a group: its children are the group roots. It has no category.
            public bool Folder => Category == null;
            public string Category { get; }
            public string Subcategory { get; }
        }
        // A category is written as its letter in the table below.
        static readonly Dictionary<string, string[]> Letters = new Dictionary<string, string[]> {
            ["S"] = new[] { StationBuilding, null }, ["P"] = new[] { Platform, null }, ["L"] = new[] { Lights, null }, ["N"] = new[] { Sign, null }, ["G"] = new[] { GradeCrossing, null },
            ["BR"] = new[] { Bridge, RailBridges }, ["BW"] = new[] { Bridge, RoadBridges }, ["I"] = new[] { IndustryBuilding, null }, ["D"] = new[] { StorageDisplay, null }, ["R"] = new[] { RailroadServiceBuilding, null },
        };
        // The two crossbucks of the unprotected crossing at a path below its folder.
        static string Crossbucks(string crossing) => crossing + "/Sign-Crossing0 (1)=G;" + crossing + "/Sign-Crossing0 (2)=G";
        // One line per folder below World/Large Scenery: the folder, then its scene objects as path=category,
        // separated by semicolons. A path without a category is a folder; an empty path names the folder itself.
        static readonly string[] Source = {
            "Alarka Jct:Alarka Jct Station=S;" + Crossbucks("Crossing Unprotected (1)") + ";" + Crossbucks("Crossing Unprotected") + ";Tall Post Shade=L",
            "Alarka Signs:Sign-Station=N",
            "Alarka:Alarka Depot=S;Platform=P;Tall Post Shade=L;Wood Platform 2 x 2 (6)=P;Wood Platform 2 x 2 (7)=P;Wood Platform 2 x 2 (8)=P;Wood Platform 2 x 2 (9)=P;Wood Platform Ramp 2 x 4 (1)=P",
            // The Almond Bridge folder carries the construction controller and the culler of the bridge: it is the group
            // itself, and its abutments and steel are its parts. An item without a name is the folder's own row.
            "Almond Bridge:=BR",
            "Almond:Almond Depot=S;Almond Road Bridge=BW;Tall Post Shade=L",
            "Andrews:Andrews Depot=S;Andrews Snowbird Paper=D;Coal & Oil=I;Grain Elevator=I;Tall Post Shade=L;Tall Post Shade (1)=L;Tall Post Shade (2)=L",
            "Bryson:Bridge 1=BR;Bryson Coal Dealer=I;Bryson Depot=S;Bryson House Track=P;Bryson Oil=I;" + Crossbucks("Bryson Oil Crossing/Crossing Unprotected") +
                ";Bryson Section House=R;Bryson Signs=N;Bryson Tower Chance Clayton=R;Bryson Turntable=R;Bryson Yard;Bryson Yard/Tall Post Shade=L;Bryson Yard/Tall Post Shade (1)=L;Bryson Yard/Tall Post Shade (2)=L;" +
                "Bryson Yard/Tall Post Shade (3)=L;Bryson Yard/Tall Post Shade (4)=L;Bryson Yard/Tall Post Shade (6)=L;Bryson Yard/Tall Post Shade (7)=L;Bryson Yard/Tall Post Shade (8)=L;Bryson Yard/Tall Post Shade (9)=L;" +
                "Bryson Yard/Tall Post Shade (10)=L;Bryson Yard/Tall Post Shade (11)=L;Bryson Yard/Tall Post Shade (12)=L;Bryson Yard/Tall Post Shade (13)=L",
            "Cochran:Cochran flagstopstation=S",
            "Dillsboro Turntable:Roundhouse 3=R",
            "Ela:" + Crossbucks("Crossing Unprotected - Bryson Everett") + ";Signs=N;flagstopstation=S",
            "Hewitt:Mine 1=I",
            "Nantahala:" + Crossbucks("Crossing Unprotected") + ";Nantahala Station=S;Whittier Haywood Road Bridge=BW",
            "Plate Girder Bridge:Plate Girder Bridge=BR;Plate Girder Bridge (1)=BR;Plate Girder Bridge (2)=BR;Plate Girder Bridge (3)=BR;Plate Girder Bridge (4)=BR;Plate Girder Bridge End=BR;Plate Girder Bridge End (1)=BR",
            "Rhodo:Rhodo Station=S",
            "Sylva:Sylva Culled=I;Sylva Depot=S;Sylva Paperboard=D;Sylva Signs=N",
            "Topton:Topton Station=S",
            "Whittier:Connelly=D;Connelly Culled=I;Southern Combination Depot=S;Spotlight Outdoor=L;Spotlight Outdoor (1)=L;Tall Post Shade=L;Tall Post Shade (1)=L;Whittier Log Pond=D;Whittier Saw Mill Culled=I;Whittier Signs=N;Whittier Stenzel=L",
            "Wilmot:Wilmot Station=S",
        };
        static readonly Dictionary<string, Row> ByPath = Read();
        static Dictionary<string, Row> Read() {
            var rows = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (var line in Source) {
                var colon = line.IndexOf(':');
                var folder = "World/Large Scenery/" + line.Substring(0, colon) + "/";
                foreach (var item in line.Substring(colon + 1).Split(';')) {
                    var at = item.LastIndexOf('=');
                    var letters = at < 0 ? new string[] { null, null } : Letters[item.Substring(at + 1)];
                    var path = (folder + (at < 0 ? item : item.Substring(0, at))).TrimEnd('/');
                    rows.Add(path, new Row(path, letters[0], letters[1]));
                }
            }
            return rows;
        }
        public static IReadOnlyCollection<Row> Table => ByPath.Values;
        // The table's row for the scene object at a hierarchy path from World, or null.
        public static Row Find(string path) => path != null && ByPath.TryGetValue(path, out var row) ? row : null;
        public static bool IsFolder(string path) => Find(path)?.Folder == true;
        // The category and subgroup of an object that another family lists, or null: the bridge and the construction
        // controller of a rail bridge construction stand under Bridge / Rail bridges with the other rail bridges.
        public const string BridgeFamily = "Bridge", ConstructionFamily = "Bridge construction scenery";
        static readonly IReadOnlyList<string> RailBridge = Array.AsReadOnly(new[] { Bridge, RailBridges });
        public static IReadOnlyList<string> OfFamily(string family) => family == BridgeFamily || family == ConstructionFamily ? RailBridge : null;
        // The scene objects in a group-root position that have no model or function in the game: an inactive default
        // cube and five objects without a model. They are not supported and stay out of the inventory.
        public static readonly IReadOnlyList<string> WithoutFunction = Array.AsReadOnly(new[] { "World/Large Scenery/Almond/Cube", "World/Large Scenery/Bryson/Bryson Appalachian Hardwoods" });
    }
    // Which scene objects below World are the roots of baked scenery groups.
    public static class BakedSceneryRules {
        public const string Family = "Baked scenery group";
        public const string WorldRoot = "World", LargeScenery = "Large Scenery";
        public const string MapLabels = "Map Labels", StationIcons = "Station Icons", BuilderPhotoSetup = "Builder Photo Setup";
        // The coverage rules that keep a candidate out, by the name of their row in the coverage statement.
        public const string NoPhysicalPresence = "Objects without physical presence", RuntimeObject = "Objects the game creates while running",
            NativeMapData = "Track spans, passenger stops, industries, spawns, turntables, and other native map data", MapLabel = "Map labels",
            BuilderPhotoRig = "Builder photo rig", NoFunction = "Objects without a model or function in the game", CarLoaders = "Car loaders";
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
                    // The Station Icons folder holds the station map icons, each an entry of its own family: the folder is
                    // no group. A direct child with a map icon is a candidate, which keeps that family; any other child,
                    // such as a subgroup, is not listed, as the map labels beside the folder are not.
                    else if (category.Name == MapLabels)
                        foreach (var icon in root.Children) {
                            var at = path + "/" + icon.Name;
                            if (icon.Components.Contains(StationMapIconRules.Component)) Candidate(town, icon, at, verdicts);
                            else verdicts.Add(new GroupRootVerdict(icon, at, GroupDisposition.NotListed, MapLabel));
                        }
                    else Candidate(town, root, path, verdicts);
                }
            }
            // A group the category table names stands under its category, whatever supports it; a listed group the
            // table does not name stands under Other.
            foreach (var verdict in verdicts) {
                if (verdict.Disposition != GroupDisposition.Group && verdict.Disposition != GroupDisposition.InspectionOnly) continue;
                var row = BakedSceneryCategories.Find(verdict.Path);
                if (row != null && !row.Folder) { verdict.Category = row.Category; verdict.Subcategory = row.Subcategory; }
                else if (verdict.Category == null && verdict.Disposition == GroupDisposition.Group) verdict.Category = BakedSceneryCategories.Other;
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
        // A span the game builds from the map's operations data and a native track span are not listed; a detection span
        // and the spans the patcher adds for a crossing are.
        public static bool SpanListed(SpanOrigin origin) => origin == SpanOrigin.Owned || origin == SpanOrigin.Held;
        // The objects one level below a category folder, and below World/Large Scenery one level below a folder there, with
        // the name of that folder below World/Large Scenery.
        static IEnumerable<(string, IHierarchyNode, string)> Roots(IHierarchyNode category, string categoryPath) {
            if (category.Name != LargeScenery) { foreach (var root in category.Children) yield return (null, root, categoryPath + "/" + root.Name); yield break; }
            foreach (var town in category.Children) {
                // A folder the category table names as a group, such as the Almond Bridge, is itself the group root.
                if (BakedSceneryCategories.Find(categoryPath + "/" + town.Name)?.Folder == false) { yield return (town.Name, town, categoryPath + "/" + town.Name); continue; }
                foreach (var root in town.Children) yield return (town.Name, root, categoryPath + "/" + town.Name + "/" + root.Name);
            }
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
            new Exclusion("Track.TrackMarker", "track marker", null, reason: TrackMarkerReason),
            new Exclusion("Model.StationAgent", "station click spot", null, reason: StationAgentReason),
            new Exclusion(ConstructionController, "track construction controller", null, reason: ConstructionOutsideBridge),
            new Exclusion(SpawnPoints.Component, "spawn point", null, reason: SpawnPointReason),
            new Exclusion("UI.Map.MapIcon", "map icon", null, reason: MapIconReason),
        });
        // Why a group with a map icon is left out. Station map icons, in the Station Icons folder, have their own family;
        // a map icon elsewhere is in no family and has no edit. No ticket is needed.
        public const string MapIconReason = "This group contains a map icon, which the patcher can't move with a group.";
        // Why a group with a station click spot is left out: in Railroader 0.6.3 station click spots are native map data,
        // which the native map editor edits. No ticket is needed.
        public const string StationAgentReason = "This group contains a station click spot. It's native map data, which you edit in the Map Editor.";
        // Why a group with track markers is left out: it is not moved as a whole, and its markers are edited one by one in
        // their own family. No ticket is needed.
        public const string TrackMarkerReason = "You can't move this group as a whole. Edit its track markers individually in the Track marker family.";
        // A group with a car loader (RollingStock.CarContentSwapper) at any depth is not listed (CarLoaders). Railroader
        // 0.6.3 has one, the Robinson Gap Mine Coal Loader, and it loads nothing: the base map loads Robinson Gap coal by
        // teleport between the spans of its industry (a TeleportLoadingIndustry in Map.json), and no component references
        // the loader, so moving it would not move loading. No ticket.
        public const string CarLoader = "RollingStock.CarContentSwapper";
        // Why a modification that names a group with a car loader fails.
        public const string CarLoaderRefusal = "This group isn't listed because it contains a car loader. The game loads Robinson Gap coal by teleport between the industry's track spans. It doesn't use the loader, so moving the loader wouldn't change where loading happens.";
        // Why a group with a spawn point is left out: its spawn points are edited one by one in their own family.
        public const string SpawnPointReason = "You can't move this group as a whole. Edit its spawn points individually in the Spawn point family.";
        // The folders below World/Large Scenery that hold the parts of a rail bridge construction outside Ela.
        public static readonly IReadOnlyList<string> RailBridgeFolders = Array.AsReadOnly(new[] { "Almond Bridge", "Plate Girder Bridge" });
        // A road bridge construction, wherever it stands: a root whose name contains Road Bridge. Its road is native
        // map data; the bridge carries a renderer culler and its own scene ground mask.
        public static bool RoadBridge(string name) => name != null && name.IndexOf("Road Bridge", StringComparison.Ordinal) >= 0;
        // A rail bridge construction outside Ela: its root stands in a folder of its own below World/Large Scenery, or is
        // that folder, or is named as a Bryson or plate girder bridge. town is that folder's name, or null outside it.
        public static bool RailBridge(string town, string name) =>
            name != null && !RoadBridge(name) && (RailBridgeFolders.Contains(town) || name == "Bridge 1" || name.StartsWith("Plate Girder", StringComparison.Ordinal));
        // The construction controller of a rail bridge (Game.Mow.TrackConstructionScenery): it switches the objects of
        // its entries on and off by the construction state of the track segments it names, wherever it stands.
        public const string ConstructionController = "Game.Mow.TrackConstructionScenery";
        // Why a group that holds a construction controller but is no supported rail bridge construction is left out.
        public const string ConstructionOutsideBridge = "This group contains a track construction controller but isn't a supported rail bridge. You can't edit it as a group.";
        // Why a rail bridge construction or a part of one takes no track-following placement: its track is native map
        // data, and the construction is placed onto the moved track with fixed placement.
        public const string RailBridgeNoFollowing = "Rail bridges use fixed placement. They don't support track-following placement.";
        // Why a road bridge construction or a part of one takes no track-following placement: it stands where its road
        // goes, which is native map data, and track following follows track.
        public const string RoadBridgeNoFollowing = "Road bridges use fixed placement. They don't support track-following placement.";
        // Why a group of the subgroup, or a part of one, takes no track-following placement, or null: a bridge of
        // either subgroup is placed onto its moved track or road with fixed placement.
        public static string FollowingRefusal(string subcategory) =>
            subcategory == BakedSceneryCategories.RailBridges ? RailBridgeNoFollowing : subcategory == BakedSceneryCategories.RoadBridges ? RoadBridgeNoFollowing : null;
        // Why a rail bridge construction with track-owned components in its hierarchy cannot be edited: the track and
        // what the game builds for it belong to the native map data, which a construction move must leave in place.
        public static string TrackOwnedRefusal(IEnumerable<string> types) {
            var owned = (types ?? Enumerable.Empty<string>()).FirstOrDefault(type => type != null && (type.StartsWith("Track.", StringComparison.Ordinal) || type.StartsWith("AutoTrestle.", StringComparison.Ordinal)));
            return owned == null ? null : "This rail bridge contains the track-owned component " + owned + ", which belongs to the native track. The bridge can't be moved, enabled, or disabled.";
        }
        public const string CrossingController = "Track.Crossings.GradeCrossingController", PadBuilder = "Track.TrackCurveMeshBuilder", Marker = "Track.TrackMarker",
            RendererCuller = "Helpers.Culling.RendererCuller", FolderRule = "Folders above group roots";
        // Crossbucks at unprotected crossings: each crossbuck is a group of its own under Grade crossing, wherever it
        // stands below World. Its models, the LOD levels below it, belong to its entry and are no parts.
        public const string CrossbuckModels = "A crossbuck's models belong to its entry.";
        // The name of a crossbuck's entry: the crossbuck's own name at its crossing, as a crossing mast is named, so the
        // crossbucks of the base map read apart and a search finds them by crossing name. Where crossings at other paths
        // share the crossing's name, such as the two Crossing Unprotected - Robbinsville crossings, the name adds the
        // place the crossing stands at (CrossingEquipment.Place). A crossbuck that the pad marker id identifies names
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
        // The crossings that share one path, as below Old River Rd, are told apart by their pad marker id instead.
        public static ISet<string> SharedCrossingNames(IEnumerable<string> crossbuckPaths) {
            var crossings = (crossbuckPaths ?? Enumerable.Empty<string>()).Where(path => path != null && path.LastIndexOf('/') > 0)
                .Select(path => path.Substring(0, path.LastIndexOf('/'))).Distinct(StringComparer.Ordinal);
            return new HashSet<string>(crossings.GroupBy(crossing => crossing.Substring(crossing.LastIndexOf('/') + 1), StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.Ordinal);
        }
        // Why a target with a pad marker id cannot name a crossbuck, from the crossbucks listed at its path, or null. The
        // id identifies a crossbuck only where several crossbucks share the path: a crossbuck with a path of its own is
        // named by its path alone, so the identity holds one identifier, never two.
        public const string PadMarkerUnshared = "This crossbuck's path identifies it uniquely, so its target doesn't accept padMarkerId. Remove padMarkerId from this target.";
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
        // The marker id of the crossing's pad: the one id its pads hold, or null.
        static string PadMarkerId(IHierarchyNode crossing) {
            var ids = crossing.Children.Where(Pad).Select(pad => pad.MarkerId).Where(id => !String.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
            return ids.Length == 1 ? ids[0] : null;
        }
        static bool RoadBridge(IHierarchyNode node) => RoadBridge(node.Name);
        // One or more crossings, or crossings and road bridges.
        static bool CrossingFolder(IHierarchyNode node) =>
            !node.HasFamily && node.Children.Any(Crossing) && node.Children.All(child => Crossing(child) || RoadBridge(child));
        static void Candidate(string town, IHierarchyNode node, string path, List<GroupRootVerdict> verdicts) {
            if (!node.Serialized) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.NotListed, RuntimeObject)); return; }
            if (node.Components.Contains(MapObject)) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.NotListed, NativeMapData)); return; }
            if (node.HasFamily) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.ExistingFamily, null)); return; }
            if (BakedSceneryCategories.WithoutFunction.Contains(path)) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.NotListed, NoFunction)); return; }
            // A scene object the category table marks as a folder, such as Bryson Yard, is no group: its children are.
            if (BakedSceneryCategories.IsFolder(path) || CrossingFolder(node)) { foreach (var child in node.Children) Candidate(town, child, path + "/" + child.Name, verdicts); return; }
            if (Unprotected(node)) {
                var pad = PadMarkerId(node);
                foreach (var child in node.Children) {
                    var at = path + "/" + child.Name;
                    if (!child.Serialized) verdicts.Add(new GroupRootVerdict(child, at, GroupDisposition.NotListed, RuntimeObject));
                    else if (child.HasFamily) verdicts.Add(new GroupRootVerdict(child, at, GroupDisposition.ExistingFamily, null));
                    else if (MarkerObject(child)) verdicts.Add(new GroupRootVerdict(child, at, GroupDisposition.NotListed, FolderRule));
                    // Every other child is a crossbuck: a group of its own under Grade crossing, wherever the crossing stands.
                    else verdicts.Add(new GroupRootVerdict(child, at, GroupDisposition.Group, null) { Category = BakedSceneryCategories.GradeCrossing, Crossbuck = true, PadMarkerId = pad });
                }
                return;
            }
            // Physical presence comes first: an object without a renderer, collider or light is not listed, whatever it holds.
            if (!Physical(node)) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.NotListed, NoPhysicalPresence)); return; }
            if (Holds(node, CarLoader)) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.NotListed, CarLoaders)); return; }
            // A bridge takes its category from this rule wherever it stands, also outside the category table, as
            // the road bridges below the World/Roads folders do.
            var road = RoadBridge(node.Name);
            var rail = RailBridge(town, node.Name);
            // A supported rail bridge construction holds its own construction controller; any other group holding one is left out.
            var excluded = FirstExclusion(node, rail ? ConstructionController : null);
            if (road || rail) {
                var subgroup = road ? BakedSceneryCategories.RoadBridges : BakedSceneryCategories.RailBridges;
                verdicts.Add(excluded != null
                    ? new GroupRootVerdict(node, path, GroupDisposition.InspectionOnly, excluded.Reason ?? "baked scenery groups that hold a " + excluded.Kind, excluded.Ticket, excluded.Reason != null) { Category = BakedSceneryCategories.Bridge, Subcategory = subgroup }
                    : new GroupRootVerdict(node, path, GroupDisposition.Group, null) { Category = BakedSceneryCategories.Bridge, Subcategory = subgroup });
                return;
            }
            if (excluded != null) { verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.InspectionOnly, excluded.Reason ?? "baked scenery groups that hold a " + excluded.Kind, excluded.Ticket, excluded.Reason != null)); return; }
            verdicts.Add(new GroupRootVerdict(node, path, GroupDisposition.Group, null));
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
        // The coverage rules that keep an object below a listed group root out of the part entries.
        public const string AmbiguousParts = "Parts with the same name and parent share a path", LodLevels = "LOD levels belong to the object with the LOD group",
            AnimatedParts = "Parts the game moves while running", CullingContainers = "Culling containers";
        // The game's cullers: a simple culler switches its own object, a renderer culler the renderers below it and a
        // game-object culler the objects on its list. Only a simple culler makes a culling container: a renderer
        // culler sits on the root of a prefab that culls its own children, such as a lamp post (Tall Post Shade), a
        // crossbuck or a sign, and that root is the object a modder moves: a part where it stands below a group
        // root, and a group root where it stands in a group-root position, as the yard lamp posts of Bryson Yard do.
        public const string SimpleCuller = "Helpers.SimpleCuller", GameObjectCuller = "Helpers.Culling.GameObjectCuller";
        public static readonly IReadOnlyList<string> Cullers = Array.AsReadOnly(new[] { SimpleCuller, RendererCuller, GameObjectCuller });
        // A culling container, such as Bryson House Track/Culled, is a scene object below a group root that only holds
        // other objects for a simple culler: it carries a simple culler, has no renderer, collider or light of its own
        // and has children. It is no object a modder moves; its parts stay listed under their group. A group root that
        // carries the culler itself, such as Sylva Culled, stays the group root.
        public static bool CullingContainer(IHierarchyNode node) =>
            node != null && !Present(node) && node.Children.Count != 0 && node.Components.Contains(SimpleCuller);
        // Why a modification cannot edit the object at a path the part rule did not list, or null for a listed part.
        public const string CullingContainerRefusal = "This path names a culling container, which only groups parts for the game's culling; it is not an object. Edit its parts instead";
        // Whether a path resolves to the object the part rule decided this for: a listed part, and a culling container,
        // so that its modification fails with the container's reason instead of finding nothing.
        public static bool Resolves(PartVerdict verdict) => verdict?.Disposition == PartDisposition.Part || PartRefusal(verdict) != null;
        public static string PartRefusal(PartVerdict verdict) => verdict?.Reason == CullingContainers ? CullingContainerRefusal : null;
        // Why a modification cannot edit, as a group, the object at a path the group rule did not list, or null: the
        // category table marks the path as a folder, such as Bryson Yard.
        public const string FolderRefusal = "This path names a folder, which only holds groups; it is not a group. Edit its groups instead";
        public static string GroupRefusal(string path) => BakedSceneryCategories.IsFolder(path) ? FolderRefusal : null;
        // Why a modification cannot edit the object at a path as a group, from the group rule's verdict for it, or null:
        // a folder, which has no verdict, and a group with a car loader, which the Car loaders rule keeps out.
        public static string GroupRefusal(GroupRootVerdict verdict, string path) =>
            verdict == null ? GroupRefusal(path) : verdict.Reason == CarLoaders ? CarLoaderRefusal : null;
        // Whether a path resolves as a group target: a listed group root, a folder and a group with a car loader, so
        // that their modification fails with the reason instead of finding nothing.
        public static bool ResolvesGroup(GroupRootVerdict verdict, string path) => verdict?.Disposition == GroupDisposition.Group || GroupRefusal(verdict, path) != null;
        // Why a modification cannot edit, as a part, the object at a path: a listed group root, such as a yard lamp
        // post of Bryson Yard, is edited as a group; otherwise the part rule's own reason, or null for a listed part.
        public const string GroupRootRefusal = "This path names a group root, not a part. Edit it as a baked scenery group";
        public static string PartRefusal(PartVerdict verdict, GroupRootVerdict group) =>
            group?.Disposition == GroupDisposition.Group ? GroupRootRefusal : group?.Reason == CarLoaders ? CarLoaderRefusal : PartRefusal(verdict);
        public static bool Resolves(PartVerdict verdict, GroupRootVerdict group) => verdict?.Disposition == PartDisposition.Part || PartRefusal(verdict, group) != null;
        // The group verdict a part modification is judged by: the object's own, or else the verdict of a group with a car
        // loader above it, nearest first. An object below the loader then fails with the loader's reason, not as a path
        // that resolves to nothing.
        public static GroupRootVerdict GroupOf(GroupRootVerdict own, IEnumerable<GroupRootVerdict> above) =>
            own ?? above?.FirstOrDefault(verdict => verdict?.Reason == CarLoaders);
        // Every scene object below the roots of the listed groups, at any depth, with what the part rule decided for it.
        // A part is one scene object, not a renderer or a component. An inspection-only group has no part entries.
        public static IReadOnlyList<PartVerdict> Parts(IEnumerable<GroupRootVerdict> verdicts) {
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
                // A culling container is no part, but the objects in it are: they stay listed under their group.
                if (CullingContainer(child)) { found.Add(new PartVerdict(child, path, groupPath, PartDisposition.NotListed, CullingContainers)); Walk(child, path, groupPath, null, found); continue; }
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
        // The name of a part entry: the group root's name, then the path from the group root to the part.
        public static string PartName(string groupPath, string path) {
            if (groupPath == null || path == null || !path.StartsWith(groupPath + "/", StringComparison.Ordinal)) return path?.Substring(path.LastIndexOf('/') + 1);
            return groupPath.Substring(groupPath.LastIndexOf('/') + 1) + " / " + path.Substring(groupPath.Length + 1).Replace("/", " / ");
        }
        // What the inspector says about the culling of a part.
        public const string PartCullingDependency = "Culling and colliders move with the part. A part moved outside the sphere of a culler above it keeps its own culling, and that culler keeps its radius.";
        // The inventory note for a simple culler whose handler other code replaced while the patcher listened to it: the
        // patcher's handler stays in the chain and only passes the game's events on. Information, not a warning.
        public static string LeftoverWrapper(string culler) => "Own culling left a pass-through handler on " + culler + " because another handler was installed after it";
        // A scene ground mask is a component in this namespace (Map.Runtime.MaskComponents): it registers a shape in
        // the game's terrain system where its object stands, which shapes the ground and the vegetation there. A group
        // that holds one is a group of the family, and the object that holds it is a part.
        public const string GroundMasks = "Map.Runtime.MaskComponents.";
        public static bool HoldsGroundMask(IEnumerable<string> components) => components != null && components.Any(type => type != null && type.StartsWith(GroundMasks, StringComparison.Ordinal));
        // The marker objects whose scene ground mask the Track marker family handles: a station building whose track
        // marker stands on its root, which keeps that family. Railroader 0.6.3 has one, the Hemingway flag-stop station.
        // The masks of other marker objects, such as a sign's, stay as that family leaves them.
        public static readonly IReadOnlyList<string> MaskedMarkers = Array.AsReadOnly(new[] { "World/Large Scenery/Hemingway/Hemingway flagstopstation" });
        public static bool MaskedMarker(string path) => path != null && MaskedMarkers.Contains(path);
        // Whether a component of the type stands at or below the node.
        static bool Holds(IHierarchyNode node, string type) {
            var types = new HashSet<string>(StringComparer.Ordinal);
            Collect(node, types);
            return types.Contains(type);
        }
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

    // One scene object below a group root, observed with copied values.
    public sealed class GroupPart {
        public GroupPart(string id, IEnumerable<double> rootPosition, IEnumerable<double> rootRotation, bool activeSelf, bool cullerSwitched, object handle = null, object parent = null, bool animated = false) {
            Id = id ?? throw new ArgumentNullException(nameof(id)); RootPosition = rootPosition.ToArray(); RootRotation = rootRotation.ToArray();
            ActiveSelf = activeSelf; CullerSwitched = cullerSwitched; Handle = handle; Parent = parent; Animated = animated;
        }
        // The game animates this object or one above it below the root, so its placement is no part of the invariant.
        public bool Animated { get; }
        // Stable for the loaded object: hierarchy path below the root and instance.
        public string Id { get; }
        // Placement relative to the group root.
        public IReadOnlyList<double> RootPosition { get; }
        public IReadOnlyList<double> RootRotation { get; }
        // The part's own activity as the scene or the modder set it: for a part that own culling switches, the value
        // own culling holds for it, never what the camera made of it.
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
        public GroupCuller(string id, IEnumerable<double> registered, IEnumerable<double> expected, double? radius = null, double? authored = null, object handle = null, IEnumerable<object> listed = null,
            CullerKind kind = CullerKind.Simple, object culler = null, IEnumerable<ObservedRegion> regions = null) {
            Id = id ?? throw new ArgumentNullException(nameof(id)); Registered = registered?.ToArray(); Expected = expected.ToArray(); Radius = radius; Authored = authored;
            Handle = handle; Listed = (listed ?? Enumerable.Empty<object>()).ToArray(); Kind = kind; Culler = culler; Regions = (regions ?? Enumerable.Empty<ObservedRegion>()).ToArray();
        }
        // The object the culler stands on: the group root or a part. Listed holds the objects a game-object culler
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
        // For a rail bridge construction, a description of the separate track-owned scenery beside it, such as the
        // generated trestles of its track, which no edit may change; null for any other group.
        public string SeparateScenery { get; }
        // The colliders the scene saves at and below the root, for a group whose collider state every edit verifies: a
        // rail or road bridge construction. Empty for any other group. Their placement is the parts' placement.
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
    // IDisableRefusal: why `active: false` cannot hold against a switch of the game that names the group root or part, or
    // null.
    public interface IGroupWorld : IDisableRefusal {
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
        // Why the group root or part takes no track-following placement, or null: a rail or road bridge construction
        // and its parts use fixed placement. Refused before any mutation.
        string FollowingRefusal(object target);
        // Why the placement of the group root or part cannot be set, or null: a lake surface at or below it whose water
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
        // The group root of each held part, so a released part's group can let go of the part's own culling.
        readonly Dictionary<object, object> groups = new Dictionary<object, object>(new ByReference());
        // Where each held part stood in the object above it before it was held: the placement the scene gives it.
        readonly Dictionary<object, Placement> authored = new Dictionary<object, Placement>(new ByReference());
        internal Placement Of(object part) => held.TryGetValue(part, out var placement) ? placement : null;
        internal object GroupOf(object part) => groups.TryGetValue(part, out var group) ? group : null;
        internal Placement AuthoredOf(object part) => authored.TryGetValue(part, out var placement) ? placement : null;
        internal void Set(object part, Placement placement, object group = null, Placement scene = null) {
            if (placement == null) { held.Remove(part); groups.Remove(part); authored.Remove(part); return; }
            held[part] = new Placement((double[])placement.Position.Clone(), (double[])placement.Rotation.Clone());
            if (group != null) groups[part] = group;
            if (scene != null) authored[part] = scene; else authored.Remove(part);
        }
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
        static (double[], double[]) InMap(Placement root, GroupPart part) {
            var turn = Quaternion.Normalize(root.Rotation); var offset = Quaternion.Rotate(turn, part.RootPosition);
            return (new[] { root.Position[0] + offset[0], root.Position[1] + offset[1], root.Position[2] + offset[2] }, Quaternion.Multiply(turn, part.RootRotation));
        }
        static (double[], double[]) Relative(GroupPart frame, GroupPart part) {
            var inverse = Quaternion.Conjugate(Quaternion.Normalize(frame.RootRotation));
            return (Quaternion.Rotate(inverse, new[] { part.RootPosition[0] - frame.RootPosition[0], part.RootPosition[1] - frame.RootPosition[1], part.RootPosition[2] - frame.RootPosition[2] }), Quaternion.Multiply(inverse, part.RootRotation));
        }
        // A part's placement in the object directly above it, the group root or another part; null for an unknown part.
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
                    if (planned == null) return region.Id + " still holds a culling sphere of its own";
                    // A sphere that waits for the game's culler to start is registered at the next update.
                    if (region.Pending) continue;
                    if (region.Centre == null || !Near(region.Centre, planned.Centre, RegistrationTolerance) || region.Radius + PositionTolerance < planned.Radius) return "the own culling sphere " + region.Id + " does not stand at its part";
                }
                if (culler.Registered == null) continue;
                if (!active) return culler.Id + " still holds a culling sphere";
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
    // Baked scenery groups: places and enables or disables a group root with everything below it. A group move is
    // physical placement only: the activity of the parts stays theirs, and a part with its own fixed placement keeps it.
    // The root of a rail bridge construction that carries its construction controller, such as Bryson Bridge 1 and the
    // Almond Bridge, also holds the construction-segment relationship.
    // The crossbucks a world lists at a hierarchy path, with the marker id of each one's crossing pad, for the identity
    // of crossbucks whose crossings share one path.
    public interface ICrossbuckWorld {
        IReadOnlyList<KeyValuePair<object, string>> CrossbucksAt(string scene, string path);
    }
    public sealed class BakedSceneryGroups : IScene, IVersionedScene, IRelationshipScene, IConstructionSegmentScene, IDependentOutputScene, IPadMarkerIdentityScene, IDisposable {
        public const string Structure = "baked-scenery-group-v1", ComponentType = "UnityEngine.Transform", RequiredVersion = "0.5.0";
        readonly IGroupWorld world;
        readonly PartHolds holds;
        // The construction controllers on group roots; null in a world without them.
        IConstructionSegmentWorld Segments => world as IConstructionSegmentWorld;
        // The scene ground masks of the groups, or null in a world without them.
        readonly GroundMasks masks;
        public BakedSceneryGroups(IGroupWorld world, Capability capability = Capability.Unchecked, PartHolds holds = null, GroundMasks masks = null) {
            this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; this.holds = holds ?? new PartHolds(); this.masks = masks;
        }
        // The terrain output that follows a ground mask of the group, by the edited group root.
        public DependentOutput Output(object target) => masks?.Output(target) ?? DependentOutput.None;
        public void RetryOutput(object target) => masks?.RetryOutput(target);
        // The capability ends: the world gives back what its own culling of moved parts took over from the game.
        public void Dispose() => (world as IDisposable)?.Dispose();
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != ComponentType || (identity.Structure != null && identity.Structure != Structure)) return Array.Empty<object>();
            if (identity.PadMarkerId == null) return world.Find(identity.Scene, identity.Path) ?? Array.Empty<object>();
            // A pad marker id names the crossbucks at the shared path whose crossing's pad holds it.
            var crossbucks = Crossbucks(identity);
            return crossbucks.Count < 2 ? Array.Empty<object>() : crossbucks.Where(pair => pair.Value == identity.PadMarkerId).Select(pair => pair.Key).ToArray();
        }
        IReadOnlyList<KeyValuePair<object, string>> Crossbucks(TargetIdentity identity) =>
            (world as ICrossbuckWorld)?.CrossbucksAt(identity.Scene, identity.Path) ?? Array.Empty<KeyValuePair<object, string>>();
        public string PadMarkerRefusal(TargetIdentity identity) {
            if (identity.PadMarkerId == null) return null;
            if (identity.Component != ComponentType || (identity.Structure != null && identity.Structure != Structure)) return TargetIdentity.PadMarkerNotAccepted;
            return BakedSceneryRules.PadMarkerRefusal(Crossbucks(identity).Count);
        }
        public void Preflight(object target) {
            var refusal = world.Refusal(target);
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
            var refusal = (state?.Placement == null ? null : world.PlacementRefusal(target) ?? masks?.PlacementRefusal(target))
                ?? (state?.Active == false ? world.DisableRefusal(target) : null);
            if (refusal != null) throw new InvalidOperationException(refusal);
        }
        sealed class Snapshot { internal GroupObservation Observed; internal ConstructionSegments Segments; }
        public object Capture(object target) => new Snapshot { Observed = world.Observe(target), Segments = ConstructionSegmentRule.Read(Segments, target) };
        public ObjectState Read(object target) => world.Observe(target).Root.WithSegments(ConstructionSegmentRule.Read(Segments, target));
        // The group root's placement applies first; every part with a fixed placement below it is then put back there.
        void Place(object target, Placement placement) {
            world.SetPlacement(target, placement);
            holds.Reassert(world, target);
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
            ConstructionSegmentRule.Apply(Segments, target, state.Segments);
            var segments = ConstructionSegmentRule.Verify(Segments, target, state.Segments);
            if (segments != null) throw new InvalidOperationException("Applied construction segments did not verify: " + segments);
            var before = world.Observe(target);
            var masked = masks?.Observe(target);
            var active = state.Active ?? before.Root.Active ?? true;
            if (active && before.Root.Active != true) world.SetActive(target, true);
            if (state.Placement != null) Place(target, state.Placement);
            if (!active && before.Root.Active != false) world.SetActive(target, false);
            var plan = Refresh(target);
            var after = world.Observe(target);
            if (!GroupChecks.SamePlacement(after.Root.Placement, state.Placement ?? before.Root.Placement) || after.Root.Active != active) throw new InvalidOperationException("Applied group placement or activity did not verify");
            var difference = GroupChecks.Layout(before, after, holds.Contains) ?? GroupChecks.Colliders(before, after) ?? GroupChecks.Culling(after, active, plan) ?? GroupChecks.RootCuller(after, active);
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
                if (root.Active == true) world.SetActive(target, true);
                if (root.Placement != null) Place(target, root.Placement);
                if (root.Active == false) world.SetActive(target, false);
                var plan = Refresh(target);
                var current = world.Observe(target);
                masks?.Follow(target, target, masked);
                return GroupChecks.SamePlacement(current.Root.Placement, root.Placement) && current.Root.Active == root.Active && GroupChecks.Layout(captured, current, holds.Contains) == null
                    && GroupChecks.Colliders(captured, current) == null && GroupChecks.Culling(current, root.Active != false, plan) == null && current.RootCulling == captured.RootCulling;
            } catch { return false; }
        }
        public string MinimumPatcherVersion(TargetIdentity target) => target != null && (target.Structure == Structure || (target.Structure == null && target.Component == ComponentType)) ? RequiredVersion : null;
    }
    // Parts of baked scenery groups: places and enables or disables one scene object inside a listed group. A part edit
    // is an ordinary scene-object edit; the group root and the other parts keep their placement and activity. A fixed
    // placement holds the part in map coordinates when its group moves later; the placement the part has without any
    // modification stays relative to its group.
    public sealed class BakedSceneryParts : IScene, IVersionedScene, IFramedScene, IRelationshipScene, IRetainedScene, IDependentOutputScene {
        public const string Structure = "baked-scenery-part-v1", ComponentType = BakedSceneryGroups.ComponentType, RequiredVersion = "0.5.0";
        readonly IGroupWorld world;
        readonly PartHolds holds;
        // The scene ground masks of the groups, or null in a world without them.
        readonly GroundMasks masks;
        public BakedSceneryParts(IGroupWorld world, PartHolds holds, Capability capability = Capability.Unchecked, GroundMasks masks = null) {
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
            // A group root resolves as a part only so that a modification that asks for a part fails with the reason.
            // An identity without a structure names the group, which the group capability resolves.
            return identity.Structure != null ? found : found.Where(part => world.PartRefusal(part) != BakedSceneryRules.GroupRootRefusal).ToArray();
        }
        public void Preflight(object target) {
            var refusal = world.PartRefusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            var observed = world.ObservePart(target);
            if (!GroupChecks.Finite(observed.Part.Placement) || !GroupChecks.Finite(observed.GroupState.Root.Placement)) throw new InvalidOperationException("Map coordinates are unavailable for the part or its group root");
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
            if (state?.Marker != null) throw new InvalidOperationException("Baked scenery parts do not support track-marker relationships");
            if (state?.Coverage != null) throw new InvalidOperationException("Baked scenery parts do not support crossing-coverage relationships");
            if (state?.Equipment != null) throw new InvalidOperationException("Baked scenery parts do not support crossing-equipment relationships");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
            if (state?.Placement?.Following != null && world.FollowingRefusal(target) is string following) throw new InvalidOperationException(following);
            var refusal = (state?.Active == null ? null : world.ActivityRefusal(target)) ?? (state?.Placement == null ? null : world.PlacementRefusal(target) ?? masks?.PlacementRefusal(target))
                ?? (state?.Active == false ? world.DisableRefusal(target) : null);
            if (refusal != null) throw new InvalidOperationException(refusal);
        }
        sealed class Snapshot { internal PartObservation Observed; internal Placement Held, Scene; }
        public object Capture(object target) => new Snapshot { Observed = world.ObservePart(target), Held = holds.Of(target), Scene = holds.AuthoredOf(target) };
        // The part's placement applies, then every held part below it is put back at its fixed placement. scene is where
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
            // A placement read in its parent returns the part there; any other placement is a fixed placement that holds it.
            // A part that is not held yet stands where the scene has it in the object above it.
            if (desired != null) Place(target, before.Group, desired, desired.Frame == null ? desired : null, holds.Contains(target) ? holds.AuthoredOf(target) : GroupChecks.InParent(before.GroupState, target));
            if (!switched && !active && before.Part.Active != false) world.SetPartActive(target, false);
            Verify(target, before, desired ?? before.Part.Placement, switched ? (bool?)null : active, Refresh(before.Group));
            // The ground masks of the group register again where their holders stand now: the part's and those below it.
            masks?.Follow(target, before.Group, masked);
        }
        // active is null for a part whose activity a culler switches: its activity is not compared.
        void Verify(object target, PartObservation before, Placement placement, bool? active, CullingPlan plan) {
            var after = world.ObservePart(target);
            if (!GroupChecks.SamePlacement(after.Part.Placement, placement) || (active != null && after.Part.Active != active)) throw new InvalidOperationException("The part's placement or activity does not match the requested state");
            var group = before.GroupState.Root; var now = after.GroupState.Root;
            if (!ReferenceEquals(after.Group, before.Group) || !GroupChecks.SamePlacement(now.Placement, group.Placement) || now.Active != group.Active) throw new InvalidOperationException("Editing the part changed its group root");
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
        public string MinimumPatcherVersion(TargetIdentity target) => target != null && (target.Structure == Structure || (target.Structure == null && target.Component == ComponentType)) ? RequiredVersion : null;
    }
}
