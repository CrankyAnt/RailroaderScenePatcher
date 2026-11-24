// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using RailroaderScenePatcher;

// Baked scenery categories: which category the generic rules give each group root, which scene objects are container
// objects, which are kept out, and how the outliner nests groups under categories and parts under groups. All from
// supplied hierarchy and component facts.
static class BakedCategoryTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Baked scenery category: " + message); }

    // The Railroader 0.6.3 Large Scenery catalogue, as the ticket records it: scene object below World/Large Scenery,
    // group or scene object below it, kind, category. The rules reach each category without naming these objects.
    const string Table = @"
Alarka Jct | Alarka Jct Station | Group | Station building
Alarka Jct | Crossing Unprotected (1)/Sign-Crossing0 (1) | Group | Grade crossing
Alarka Jct | Crossing Unprotected (1)/Sign-Crossing0 (2) | Group | Grade crossing
Alarka Jct | Crossing Unprotected/Sign-Crossing0 (1) | Group | Grade crossing
Alarka Jct | Crossing Unprotected/Sign-Crossing0 (2) | Group | Grade crossing
Alarka Jct | Tall Post Shade | Group | Lights
Alarka Signs | Sign-Station | Group | Sign
Alarka | Alarka Depot | Group | Station building
Alarka | Platform | Group | Platform
Alarka | Tall Post Shade | Group | Lights
Alarka | Wood Platform 2 x 2 (6) | Group | Platform
Alarka | Wood Platform 2 x 2 (7) | Group | Platform
Alarka | Wood Platform 2 x 2 (8) | Group | Platform
Alarka | Wood Platform 2 x 2 (9) | Group | Platform
Alarka | Wood Platform Ramp 2 x 4 (1) | Group | Platform
Almond Bridge |  | Group | Bridge / Rail bridges
Almond | Almond Depot | Group | Station building
Almond | Almond Road Bridge | Group | Bridge / Road bridges
Almond | Tall Post Shade | Group | Lights
Andrews | Andrews Depot | Group | Station building
Andrews | Andrews Snowbird Paper | Group | Storage display
Andrews | Coal & Oil | Group | Industry building
Andrews | Grain Elevator | Group | Industry building
Andrews | Tall Post Shade | Group | Lights
Andrews | Tall Post Shade (1) | Group | Lights
Andrews | Tall Post Shade (2) | Group | Lights
Bryson | Bridge 1 | Group | Bridge / Rail bridges
Bryson | Bryson Coal Dealer | Group | Industry building
Bryson | Bryson Depot | Group | Station building
Bryson | Bryson House Track | Group | Platform
Bryson | Bryson Oil | Group | Industry building
Bryson | Bryson Oil Crossing/Crossing Unprotected/Sign-Crossing0 (1) | Group | Grade crossing
Bryson | Bryson Oil Crossing/Crossing Unprotected/Sign-Crossing0 (2) | Group | Grade crossing
Bryson | Bryson Section House | Group | Railroad service building
Bryson | Bryson Signs | Group | Sign
Bryson | Bryson Tower Chance Clayton | Group | Railroad service building
Bryson | Bryson Turntable | Group | Railroad service building
Bryson | Bryson Yard | Container | -
Bryson | Bryson Yard/Tall Post Shade | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (1) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (2) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (3) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (4) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (6) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (7) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (8) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (9) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (10) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (11) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (12) | Group | Lights
Bryson | Bryson Yard/Tall Post Shade (13) | Group | Lights
Cochran | Cochran flagstopstation | Group | Station building
Dillsboro Turntable | Roundhouse 3 | Group | Railroad service building
Ela | Crossing Unprotected - Bryson Everett/Sign-Crossing0 (1) | Group | Grade crossing
Ela | Crossing Unprotected - Bryson Everett/Sign-Crossing0 (2) | Group | Grade crossing
Ela | Signs | Group | Sign
Ela | flagstopstation | Group | Station building
Hewitt | Mine 1 | Group | Industry building
Nantahala | Crossing Unprotected/Sign-Crossing0 (1) | Group | Grade crossing
Nantahala | Crossing Unprotected/Sign-Crossing0 (2) | Group | Grade crossing
Nantahala | Nantahala Station | Group | Station building
Nantahala | Whittier Haywood Road Bridge | Group | Bridge / Road bridges
Plate Girder Bridge | Plate Girder Bridge | Group | Bridge / Rail bridges
Plate Girder Bridge | Plate Girder Bridge (1) | Group | Bridge / Rail bridges
Plate Girder Bridge | Plate Girder Bridge (2) | Group | Bridge / Rail bridges
Plate Girder Bridge | Plate Girder Bridge (3) | Group | Bridge / Rail bridges
Plate Girder Bridge | Plate Girder Bridge (4) | Group | Bridge / Rail bridges
Plate Girder Bridge | Plate Girder Bridge End | Group | Bridge / Rail bridges
Plate Girder Bridge | Plate Girder Bridge End (1) | Group | Bridge / Rail bridges
Rhodo | Rhodo Station | Group | Station building
Sylva | Sylva Culled | Group | Industry building
Sylva | Sylva Depot | Group | Station building
Sylva | Sylva Paperboard | Group | Storage display
Sylva | Sylva Signs | Group | Sign
Topton | Topton Station | Group | Station building
Whittier | Connelly | Group | Storage display
Whittier | Connelly Culled | Group | Industry building
Whittier | Southern Combination Depot | Group | Station building
Whittier | Spotlight Outdoor | Group | Lights
Whittier | Spotlight Outdoor (1) | Group | Lights
Whittier | Tall Post Shade | Group | Lights
Whittier | Tall Post Shade (1) | Group | Lights
Whittier | Whittier Log Pond | Group | Storage display
Whittier | Whittier Saw Mill Culled | Group | Industry building
Whittier | Whittier Signs | Group | Sign
Whittier | Whittier Stenzel | Group | Lights
Wilmot | Wilmot Station | Group | Station building";
    sealed class Row { public string Path; public bool Container; public string Category, Subcategory; }
    static readonly Row[] Rows = Table.Split('\n').Select(line => line.Trim()).Where(line => line.Length != 0).Select(line => {
        var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
        var category = cells[3].Split(new[] { " / " }, StringSplitOptions.None);
        return new Row { Path = ("World/Large Scenery/" + cells[0] + "/" + cells[1]).TrimEnd('/'), Container = cells[2] == "Container", Category = cells[2] == "Container" ? null : category[0], Subcategory = category.Length > 1 ? category[1] : null };
    }).ToArray();
    static readonly string[] Categories = { "Station building", "Platform", "Lights", "Sign", "Grade crossing", "Bridge", "Industry building", "Storage display", "Railroad service building", "Water", "Road scenery", "Unclassified" };

    public static void Run() {
        CategoryNames();
        EveryRootTakesItsTableCategory();
        ContainerObjectHasNoGroupEntry();
        VerdictsKeepObjectsOut();
        OutlinerNestsGroupsAndParts();
        BridgesOutsideTheTable();
        SelectGroupFindsTheGroup();
        Console.WriteLine("PASS: baked scenery categories");
    }

    static void CategoryNames() {
        Check(Rows.Where(r => !r.Container).All(r => Categories.Contains(r.Category)), "every category in the catalogue is one of the categories of the rules");
    }

    // The world of the catalogue: every row a scene object at its path. A crossbuck stands in an unprotected
    // crossing, a container object holds only its child groups, and every other root holds what it holds in the game
    // where the rules read that: a lamp post culls itself, a storage display holds piles or a log pond, a group named
    // after its place alone holds the objects that say what it is, and the Almond Bridge carries its culler and
    // construction controller. Every other root is a model with one part.
    static readonly Dictionary<string, Action<Node>> Contents = new Dictionary<string, Action<Node>> {
        ["Bryson House Track"] = n => n.Kids.Add(new Node("Culled", new Node("Wood Platform 2 x 2").Solid(), new Node("Wood Platform Ramp 2 x 4").Solid()).With("Helpers.SimpleCuller")),
        ["Whittier Stenzel"] = n => { n.Kids.Add(new Node("Spotlight Outdoor").Solid()); n.Kids.Add(new Node("Spotlight Outdoor (1)").Solid()); },
        ["Sylva Culled"] = n => { n.With("Helpers.SimpleCuller"); n.Kids.Add(new Node("Pavilion", new Node("Roof").Solid())); },
        ["Connelly Culled"] = n => { n.With("Helpers.SimpleCuller"); n.Kids.Add(new Node("Pavilion", new Node("Roof").Solid())); n.Kids.Add(new Node("Spotlight Outdoor").Solid()); },
        ["Connelly"] = n => n.Kids.Add(new Node("Log Pile L1a").With("Scenery.Pile")),
        ["Andrews Snowbird Paper"] = n => n.Kids.Add(new Node("Pulpwood Piles", new Node("P1").With("Scenery.Pile"))),
        ["Sylva Paperboard"] = n => n.Kids.Add(new Node("Pulpwood Piles", new Node("P1").With("Scenery.Pile"))),
        ["Whittier Log Pond"] = n => n.Kids.Add(new Node("LogPond").With("Scenery.LogPond")),
        ["Almond Bridge"] = n => { n.With("Helpers.SimpleCuller", "Game.Mow.TrackConstructionScenery"); n.Kids.Add(new Node("Steel").Solid()); },
    };
    static Node Find(Node parent, string name) { var found = parent.Kids.FirstOrDefault(k => k.Name == name); if (found == null) parent.Kids.Add(found = new Node(name)); return found; }
    static Node World() {
        var world = new Node("World");
        foreach (var row in Rows) {
            var names = row.Path.Split('/');
            var at = world;
            for (var i = 1; i < names.Length; i++) at = Find(at, names[i]);
            if (row.Container) continue;
            if (at.Name.StartsWith("Sign-Crossing0", StringComparison.Ordinal)) { at.With("Helpers.Culling.RendererCuller"); at.Kids.Add(new Node("LOD0").Solid()); }
            else if (Contents.TryGetValue(at.Name, out var fill)) fill(at);
            else {
                at.Kids.Add(new Node("Model").Solid());
                if (at.Name.StartsWith("Tall Post Shade", StringComparison.Ordinal)) at.With("Helpers.Culling.RendererCuller");
            }
        }
        // Each unprotected crossing has its pad, which an existing family lists.
        void Pads(Node node) {
            if (node.Kids.Any(k => k.Name.StartsWith("Sign-Crossing0", StringComparison.Ordinal))) node.Kids.Add(new Node("Pad").With("Track.TrackCurveMeshBuilder").Solid().Family());
            foreach (var kid in node.Kids) Pads(kid);
        }
        Pads(world);
        return world;
    }
    static GroupRootVerdict At(IEnumerable<GroupRootVerdict> verdicts, string path) => verdicts.SingleOrDefault(v => v.Path == path);

    static void EveryRootTakesItsTableCategory() {
        var verdicts = BakedSceneryRules.Classify(World());
        foreach (var row in Rows.Where(r => !r.Container)) {
            var verdict = At(verdicts, row.Path);
            Check(verdict != null && (verdict.Disposition == GroupDisposition.Group || verdict.Disposition == GroupDisposition.InspectionOnly), row.Path + " is a listed group root");
            Check(verdict.Category == row.Category && verdict.Subcategory == row.Subcategory, row.Path + " stands under " + row.Category + (row.Subcategory == null ? "" : " / " + row.Subcategory) + " (got " + verdict.Category + (verdict.Subcategory == null ? "" : " / " + verdict.Subcategory) + ")");
        }
        var listed = verdicts.Where(v => v.Disposition == GroupDisposition.Group || v.Disposition == GroupDisposition.InspectionOnly).ToArray();
        Check(listed.Length == Rows.Count(r => !r.Container), "the catalogue's groups are the listed group roots, no more (got " + listed.Length + ")");
        // Whittier Stenzel is a Lights group whose two spotlights stay parts nested under it.
        var stenzel = new Node("World", new Node("Large Scenery", new Node("Whittier", new Node("Whittier Stenzel", new Node("Spotlight Outdoor").Solid(), new Node("Spotlight Outdoor (1)").Solid()))));
        var lights = BakedSceneryRules.Classify(stenzel);
        Check(At(lights, "World/Large Scenery/Whittier/Whittier Stenzel")?.Category == "Lights" && BakedSceneryRules.Parts(lights).Count(p => p.Disposition == PartDisposition.Part && p.GroupPath == "World/Large Scenery/Whittier/Whittier Stenzel") == 2,
            "Whittier Stenzel is a Lights group and its two spotlights are its parts");
        // An industry building is its own group in Industry building.
        Check(Rows.Where(r => r.Category == "Industry building").All(r => At(verdicts, r.Path).Category == "Industry building"), "an industry building is a group root of its own in Industry building");
    }

    // A container object, an object without components or a telling name whose children all cull themselves, has no
    // group entry; its children are the group roots, each with its parts.
    static void ContainerObjectHasNoGroupEntry() {
        var posts = new[] { "", " (1)", " (2)", " (3)", " (4)", " (6)", " (7)", " (8)", " (9)", " (10)", " (11)", " (12)", " (13)" }.Select(n => "Tall Post Shade" + n).ToArray();
        var yard = new Node("Bryson Yard", posts.Select(name => new Node(name, new Node("Post").Solid(), new Node("Bulb").Solid()).With("Helpers.Culling.RendererCuller")).ToArray());
        var verdicts = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Bryson", yard, new Node("Bryson Depot", new Node("Roof").Solid())))));
        const string Yard = "World/Large Scenery/Bryson/Bryson Yard";
        Check(At(verdicts, Yard)?.Disposition == GroupDisposition.ContainerObject, "Bryson Yard is a container object: it has no group entry");
        var lamps = verdicts.Where(v => v.Path.StartsWith(Yard + "/", StringComparison.Ordinal)).ToArray();
        Check(lamps.Length == 13 && lamps.All(v => v.Disposition == GroupDisposition.Group && v.Category == "Lights") && posts.All(name => lamps.Any(v => v.Path == Yard + "/" + name)), "each of the 13 yard lamp posts is its own group in Lights (got " + lamps.Length + ")");
        var parts = BakedSceneryRules.Parts(verdicts).Where(p => p.Disposition == PartDisposition.Part).ToArray();
        Check(posts.All(name => parts.Count(p => p.GroupPath == Yard + "/" + name) == 2) && !parts.Any(p => p.GroupPath == Yard), "each lamp post lists its own parts, and none is a part of the container object");
        // The rule holds wherever the object stands and whatever it is called: a yard of lamp posts in another town is a
        // container object too.
        var elsewhere = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Sylva", new Node("East Yard", new Node("Tall Post Shade", new Node("Post").Solid()).With("Helpers.Culling.RendererCuller"))))));
        Check(At(elsewhere, "World/Large Scenery/Sylva/East Yard")?.Disposition == GroupDisposition.ContainerObject && At(elsewhere, "World/Large Scenery/Sylva/East Yard/Tall Post Shade")?.Category == "Lights",
            "a yard of lamp posts in another town is a container object and each post a Lights group");
        // An object whose children do not cull themselves stays a group, and so does one whose name says what it is.
        var other = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Sylva", new Node("Bryson Yard", new Node("Tall Post Shade", new Node("Post").Solid())),
            new Node("Sylva Oil", new Node("Tall Post Shade", new Node("Post").Solid()).With("Helpers.Culling.RendererCuller"))))));
        Check(At(other, "World/Large Scenery/Sylva/Bryson Yard")?.Disposition == GroupDisposition.Group && At(other, "World/Large Scenery/Sylva/Bryson Yard").Category == "Lights", "an object whose children do not cull themselves stays a group root, and is what it holds");
        Check(At(other, "World/Large Scenery/Sylva/Sylva Oil")?.Disposition == GroupDisposition.Group && At(other, "World/Large Scenery/Sylva/Sylva Oil").Category == "Industry building", "an object whose name says what it is stays a group root");
    }

    static void VerdictsKeepObjectsOut() {
        var world = new Node("World", new Node("Large Scenery",
            new Node("Almond", new Node("Cube").Solid()),
            new Node("Bryson", new Node("Bryson Appalachian Hardwoods", new Node("A").Solid(), new Node("B").Solid())),
            new Node("Sylva", new Node("New Shed", new Node("Wall").Solid()), new Node("Thing", new Node("Wall").Solid()), new Node("Masked Thing", new Node("Wall").Solid()).With("Character.SpawnPoint"))));
        var verdicts = BakedSceneryRules.Classify(world);
        foreach (var path in new[] { "World/Large Scenery/Almond/Cube", "World/Large Scenery/Bryson/Bryson Appalachian Hardwoods" }) {
            var verdict = At(verdicts, path);
            Check(verdict?.Disposition == GroupDisposition.NotListed && verdict.Reason == BakedSceneryRules.NoFunction && verdict.Category == null, path + " is not listed: it has no function in the game");
        }
        Check(!BakedSceneryRules.Parts(verdicts).Any(p => p.Path.Contains("Appalachian")), "an object that is not listed lists no parts");
        // A new group takes its category from the rules; a listed group no rule recognises stands under Unclassified,
        // and a group no supported operation covers and no rule recognises stays in the Inspection only group.
        Check(At(verdicts, "World/Large Scenery/Sylva/New Shed")?.Category == "Industry building", "a new shed is an industry building by its name");
        Check(At(verdicts, "World/Large Scenery/Sylva/Thing")?.Category == "Unclassified", "a listed group no rule recognises stands under Unclassified");
        var masked = At(verdicts, "World/Large Scenery/Sylva/Masked Thing");
        Check(masked?.Disposition == GroupDisposition.InspectionOnly && masked.Category == null, "an inspection-only group that no category covers has no category: it stays in the Inspection only group");
    }

    // The outliner rows of a small inventory: groups, parts, an inspection-only group with a category, and one without.
    static SceneObservation Entry(string path, string category = null, string subcategory = null, string groupPath = null, string only = null, string family = BakedSceneryRules.Family, string[] dependencies = null) =>
        new SceneObservation(new object(), new TargetIdentity("scene", path, "UnityEngine.Transform"), family, true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 },
            new string[0], dependencies ?? new string[0], new string[0], only ?? "Placement", inspectionOnly: only, groupPath: groupPath, category: category, subcategory: subcategory);
    sealed class Inventory : IInspectionScene {
        public List<SceneObservation> Objects = new List<SceneObservation>();
        public IEnumerable<string> MapIds => new[] { "map" };
        public IEnumerable<SceneObservation> Discover() => Objects;
    }
    static void OutlinerNestsGroupsAndParts() {
        const string Bryson = "World/Large Scenery/Bryson/", Dealer = Bryson + "Bryson Coal Dealer";
        var scene = new Inventory();
        scene.Objects.AddRange(new[] {
            Entry(Dealer + "/Bent/Beams", groupPath: Dealer),
            Entry(Bryson + "Bridge 1", "Bridge", "Rail bridges", only: "not yet supported"),
            // A group root names every component below it among its dependencies, as the native inventory does.
            Entry(Dealer, "Industry building", dependencies: new[] { "Component scene/" + Dealer + "/Heap [UnityEngine.MeshRenderer]", "Culling and colliders of every part follow the group root" }),
            Entry(Dealer + "/Heap", groupPath: Dealer),
            Entry("World/Large Scenery/Almond/Almond Road Bridge", "Bridge", "Road bridges"),
            Entry(Bryson + "Bryson Depot", "Station building"),
            Entry("World/Large Scenery/Sylva/New Pond", only: "not yet supported"),
            Entry("World/Sign", family: "Track marker"),
        });
        var inventory = new SceneInventory(scene); inventory.Refresh();
        // A search that matches only a part still shows the part under its group, never as a loose row.
        var found = InventoryPresentationState.Rows(inventory, "Heap", OutlinerFilter.All, null);
        // The group's own row is found too, through the dependency that names its part: it does not match itself.
        Check(found.Any(row => row.Identity.Path == Dealer) && !found.Single(row => row.Identity.Path == Dealer).MatchesItself("Heap") && found.Single(row => row.Identity.Path == Dealer).MatchesItself("coal dealer")
            && found.Single(row => row.Identity.Path == Dealer).MatchesItself("colliders"), "a group matches itself by its name and by what it says about itself, not by a dependency that names an object below it");
        // The category is searchable as shown.
        Check(InventoryPresentationState.Rows(inventory, "Rail bridges", OutlinerFilter.All, null).Select(r => r.Identity.Path).SequenceEqual(new[] { Bryson + "Bridge 1" }), "a search matches a group by its category");
    }

    // A bridge takes its category from the bridge rule wherever it stands; a rail bridge construction's
    // controller and bridge, which other families list, stand under Bridge / Rail bridges too.
    static void BridgesOutsideTheTable() {
        var world = new Node("World", new Node("Roads Bryson", new Node("Concrete Road Bridge - 20m", new Node("Deck").Solid()),
            new Node("US19 Gov Island", new Node("Grade Crossing Setup").With(BakedSceneryRules.CrossingController).Family(), new Node("Concrete Road Bridge - 60m", new Node("Deck").Solid()))),
            new Node("Large Scenery", new Node("Newtown", new Node("Plate Girder Bridge Span", new Node("Girder").Solid()))));
        var verdicts = BakedSceneryRules.Classify(world);
        foreach (var path in new[] { "World/Roads Bryson/Concrete Road Bridge - 20m", "World/Roads Bryson/US19 Gov Island/Concrete Road Bridge - 60m" }) {
            var verdict = At(verdicts, path);
            Check(verdict?.Disposition == GroupDisposition.Group && verdict.Category == "Bridge" && verdict.Subcategory == "Road bridges", path + " is a road bridge: it stands under Bridge / Road bridges (got " + verdict?.Category + ")");
        }
        var span = At(verdicts, "World/Large Scenery/Newtown/Plate Girder Bridge Span");
        Check(span?.Category == "Bridge" && span.Subcategory == "Rail bridges", "a rail bridge in a new town stands under Bridge / Rail bridges");
        string Of(string family, string path) => SceneClassification.Of(family, path, new string[0], new string[0], new string[0]).ToString();
        Check(Of("Bridge", "World/Large Scenery/Ela/Span") == "Bridge / Rail bridges" && Of("Bridge construction scenery", "World/Large Scenery/Ela/Scenery") == "Bridge / Rail bridges" && Of("Track marker", "World/Large Scenery/Ela/Marker") == "Track marker",
            "the bridge and the construction controller of a rail bridge construction stand under Bridge / Rail bridges by their family, whatever their names");
        BridgeEditsDoNotFollowTheCategory();
    }

    // Whether a group is a rail or a road bridge for its edits is a rule of its own (BakedSceneryRules.RailBridge,
    // RoadBridge), not the outliner's category: a group the outliner files under Bridge by another word, or by what it
    // holds, is edited as any group.
    static void BridgeEditsDoNotFollowTheCategory() {
        var world = new Node("World", new Node("Large Scenery", new Node("Newtown",
            new Node("Bridge 2", new Node("Steel").Solid()), new Node("Newtown Road Bridge", new Node("Deck").Solid()),
            new Node("Timber Trestle", new Node("Bent").Solid()), new Node("Newtown Culled", new Node("Road Bridge Abutment").Solid(), new Node("Road Bridge Side").Solid()).With("Helpers.SimpleCuller"),
            new Node("Newtown Depot", new Node("Roof").Solid()))));
        var verdicts = BakedSceneryRules.Classify(world);
        GroupRootVerdict V(string name) => At(verdicts, "World/Large Scenery/Newtown/" + name);
        Check(V("Bridge 2").Bridge == BridgeKind.Rail && V("Bridge 2").Subcategory == "Rail bridges" && V("Newtown Road Bridge").Bridge == BridgeKind.Road && V("Newtown Road Bridge").Subcategory == "Road bridges",
            "a rail bridge and a road bridge are bridges for their edits, and the outliner files them as such");
        Check(V("Timber Trestle").Category == "Bridge" && V("Timber Trestle").Subcategory == "Rail bridges" && V("Timber Trestle").Bridge == BridgeKind.None,
            "a trestle is filed under Bridge by the outliner's word and is no bridge for its edits (got " + V("Timber Trestle").Bridge + ")");
        Check(V("Newtown Culled").Category == "Bridge" && V("Newtown Culled").Bridge == BridgeKind.None, "a group filed under Bridge by what it holds is no bridge for its edits (got " + V("Newtown Culled").Category + " " + V("Newtown Culled").Bridge + ")");
        Check(V("Newtown Depot").Bridge == BridgeKind.None, "a depot is no bridge");
        Check(BakedSceneryRules.FollowingRefusal(V("Bridge 2").Bridge) == BakedSceneryRules.RailBridgeNoFollowing && BakedSceneryRules.FollowingRefusal(V("Newtown Road Bridge").Bridge) == BakedSceneryRules.RoadBridgeNoFollowing
            && BakedSceneryRules.FollowingRefusal(V("Timber Trestle").Bridge) == null && BakedSceneryRules.FollowingRefusal(V("Newtown Culled").Bridge) == null,
            "only a bridge by the edit rule refuses track following");
        Check(BakedSceneryRules.RailBridge(null, "Plate Girder Bridge End (1)") && BakedSceneryRules.RailBridge(null, "Almond Bridge") && !BakedSceneryRules.RailBridge(null, "Almond Road Bridge") && !BakedSceneryRules.RailBridge(null, "Bridgewater Mill") && !BakedSceneryRules.RailBridge(null, "Timber Trestle"),
            "the edit rule reads the word Bridge in the root's name, and a road bridge is no rail bridge");
    }

    static void SelectGroupFindsTheGroup() {
        const string Dealer = "World/Large Scenery/Bryson/Bryson Coal Dealer";
        var scene = new Inventory();
        var group = Entry(Dealer, "Industry building"); var part = Entry(Dealer + "/Heap", groupPath: Dealer); var sign = Entry("World/Sign", family: "Track marker");
        scene.Objects.AddRange(new[] { part, group, sign });
        var inventory = new SceneInventory(scene); inventory.Refresh();
        Check(ReferenceEquals(InventoryPresentationState.GroupOf(inventory, part), group), "a part's group is the group root's entry");
        Check(InventoryPresentationState.GroupOf(inventory, group) == null && InventoryPresentationState.GroupOf(inventory, sign) == null, "a group root and an object outside any group have no group to select");
        Check(InventoryPresentationState.PartOf(part) == "Part of: Bryson Coal Dealer" && InventoryPresentationState.PartOf(group) == null, "the line names the part's group, and only a part has it");
    }
}
