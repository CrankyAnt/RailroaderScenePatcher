// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using RailroaderScenePatcher;

// Baked scenery categories: which category the rule gives each group root, which scene objects are folders, which are
// kept out, and how the outliner nests groups under categories and parts under groups. All from supplied hierarchy
// and component facts.
static class BakedCategoryTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Baked scenery category: " + message); }

    // The category table of the Railroader 0.6.3 Large Scenery catalogue, as the ticket records it: folder below
    // World/Large Scenery, group or folder below it, kind, category.
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
Bryson | Bryson Yard | Folder | -
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
Robinson Gap Mine | Coal Loader | Group | Industry building
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
    sealed class Row { public string Path; public bool Folder; public string Category, Subcategory; }
    static readonly Row[] Rows = Table.Split('\n').Select(line => line.Trim()).Where(line => line.Length != 0).Select(line => {
        var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
        var category = cells[3].Split(new[] { " / " }, StringSplitOptions.None);
        return new Row { Path = ("World/Large Scenery/" + cells[0] + "/" + cells[1]).TrimEnd('/'), Folder = cells[2] == "Folder", Category = cells[2] == "Folder" ? null : category[0], Subcategory = category.Length > 1 ? category[1] : null };
    }).ToArray();
    static readonly string[] Categories = { "Station building", "Platform", "Lights", "Sign", "Grade crossing", "Bridge", "Industry building", "Storage display", "Railroad service building", "Other" };

    public static void Run() {
        CategoryNames();
        EveryRootTakesItsTableCategory();
        FolderHasNoGroupEntry();
        VerdictsKeepObjectsOut();
        OutlinerNestsGroupsAndParts();
        BridgesOutsideTheTable();
        SelectGroupFindsTheGroup();
        Console.WriteLine("PASS: baked scenery categories");
    }

    static void CategoryNames() {
        Check(BakedSceneryCategories.All.SequenceEqual(Categories), "the family has the ten categories of the ticket, in its order (got " + String.Join(", ", BakedSceneryCategories.All) + ")");
        Check(BakedSceneryCategories.Subcategories("Bridge").SequenceEqual(new[] { "Rail bridges", "Road bridges" }), "Bridge has the subgroups Rail bridges and Road bridges");
        Check(Categories.Where(c => c != "Bridge").All(c => BakedSceneryCategories.Subcategories(c).Count == 0), "no other category has subgroups");
        Check(Rows.Where(r => !r.Folder).All(r => Categories.Contains(r.Category)), "every category in the category table is one of the family's categories");
    }

    // The world of the table: every row a scene object at its path. A crossbuck stands in an unprotected crossing, a
    // folder holds only its child groups, and every other root is a model with one part.
    static Node Find(Node parent, string name) { var found = parent.Kids.FirstOrDefault(k => k.Name == name); if (found == null) parent.Kids.Add(found = new Node(name)); return found; }
    static Node World() {
        var world = new Node("World");
        foreach (var row in Rows) {
            var names = row.Path.Split('/');
            var at = world;
            for (var i = 1; i < names.Length; i++) at = Find(at, names[i]);
            if (row.Folder) continue;
            if (at.Name.StartsWith("Sign-Crossing0", StringComparison.Ordinal)) { at.With("Helpers.Culling.RendererCuller"); at.Kids.Add(new Node("LOD0").Solid()); }
            else at.Kids.Add(new Node("Model").Solid());
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
        foreach (var row in Rows.Where(r => !r.Folder)) {
            var verdict = At(verdicts, row.Path);
            Check(verdict != null && (verdict.Disposition == GroupDisposition.Group || verdict.Disposition == GroupDisposition.InspectionOnly), row.Path + " is a listed group root");
            Check(verdict.Category == row.Category && verdict.Subcategory == row.Subcategory, row.Path + " stands under " + row.Category + (row.Subcategory == null ? "" : " / " + row.Subcategory) + " (got " + verdict.Category + (verdict.Subcategory == null ? "" : " / " + verdict.Subcategory) + ")");
        }
        var listed = verdicts.Where(v => v.Disposition == GroupDisposition.Group || v.Disposition == GroupDisposition.InspectionOnly).ToArray();
        Check(listed.Length == Rows.Count(r => !r.Folder), "the table's groups are the listed group roots, no more (got " + listed.Length + ")");
        // Whittier Stenzel is a Lights group whose two spotlights stay parts nested under it.
        var stenzel = new Node("World", new Node("Large Scenery", new Node("Whittier", new Node("Whittier Stenzel", new Node("Spotlight Outdoor").Solid(), new Node("Spotlight Outdoor (1)").Solid()))));
        var lights = BakedSceneryRules.Classify(stenzel);
        Check(At(lights, "World/Large Scenery/Whittier/Whittier Stenzel")?.Category == "Lights" && BakedSceneryRules.Parts(lights).Count(p => p.Disposition == PartDisposition.Part && p.GroupPath == "World/Large Scenery/Whittier/Whittier Stenzel") == 2,
            "Whittier Stenzel is a Lights group and its two spotlights are its parts");
        // An industry building is its own group in Industry building.
        Check(Rows.Where(r => r.Category == "Industry building").All(r => At(verdicts, r.Path).Category == "Industry building"), "an industry building is a group root of its own in Industry building");
    }

    // A scene object in a group-root position that the table marks as a folder has no group entry; its children are the
    // group roots, each with its parts.
    static void FolderHasNoGroupEntry() {
        var posts = new[] { "", " (1)", " (2)", " (3)", " (4)", " (6)", " (7)", " (8)", " (9)", " (10)", " (11)", " (12)", " (13)" }.Select(n => "Tall Post Shade" + n).ToArray();
        var yard = new Node("Bryson Yard", posts.Select(name => new Node(name, new Node("Post").Solid(), new Node("Bulb").Solid()).With("Helpers.Culling.RendererCuller")).ToArray());
        var verdicts = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Bryson", yard, new Node("Bryson Depot", new Node("Roof").Solid())))));
        const string Yard = "World/Large Scenery/Bryson/Bryson Yard";
        Check(At(verdicts, Yard) == null, "Bryson Yard is a folder: it has no group entry");
        var lamps = verdicts.Where(v => v.Path.StartsWith(Yard + "/", StringComparison.Ordinal)).ToArray();
        Check(lamps.Length == 13 && lamps.All(v => v.Disposition == GroupDisposition.Group && v.Category == "Lights") && posts.All(name => lamps.Any(v => v.Path == Yard + "/" + name)), "each of the 13 yard lamp posts is its own group in Lights (got " + lamps.Length + ")");
        var parts = BakedSceneryRules.Parts(verdicts).Where(p => p.Disposition == PartDisposition.Part).ToArray();
        Check(posts.All(name => parts.Count(p => p.GroupPath == Yard + "/" + name) == 2) && !parts.Any(p => p.GroupPath == Yard), "each lamp post lists its own parts, and none is a part of the folder");
        // The same object anywhere else stays a group: only the table makes a folder.
        var other = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Sylva", new Node("Bryson Yard", new Node("Tall Post Shade", new Node("Post").Solid()))))));
        Check(At(other, "World/Large Scenery/Sylva/Bryson Yard")?.Disposition == GroupDisposition.Group, "an object the table does not mark as a folder stays a group root");
    }

    static void VerdictsKeepObjectsOut() {
        var world = new Node("World", new Node("Large Scenery",
            new Node("Almond", new Node("Cube").Solid()),
            new Node("Bryson", new Node("Bryson Appalachian Hardwoods", new Node("A").Solid(), new Node("B").Solid())),
            new Node("Sylva", new Node("New Shed", new Node("Wall").Solid()), new Node("New Masked Shed", new Node("Wall").Solid()).With("Map.Runtime.MaskComponents.RectangleMapMask"))));
        var verdicts = BakedSceneryRules.Classify(world);
        foreach (var path in new[] { "World/Large Scenery/Almond/Cube", "World/Large Scenery/Bryson/Bryson Appalachian Hardwoods" }) {
            var verdict = At(verdicts, path);
            Check(verdict?.Disposition == GroupDisposition.NotListed && verdict.Reason == BakedSceneryRules.NoFunction && verdict.Category == null, path + " is not listed: it has no function in the game");
        }
        Check(!BakedSceneryRules.Parts(verdicts).Any(p => p.Path.Contains("Appalachian")), "an object that is not listed lists no parts");
        // A listed group the table does not name stands under Other; a group no supported operation covers and no
        // category names stays in the Inspection only group.
        Check(At(verdicts, "World/Large Scenery/Sylva/New Shed")?.Category == "Other", "a listed group outside the table stands under Other");
        var masked = At(verdicts, "World/Large Scenery/Sylva/New Masked Shed");
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
    static string Shape(IEnumerable<InventoryPresentationState.OutlineNode> nodes) =>
        String.Join(" ", nodes.Select(n => (n.Row == null ? "[" + n.Label + "]" : n.Label + (n.Context ? "~" : "")) + (n.Children.Count == 0 ? "" : "{" + Shape(n.Children) + "}")));

    static void OutlinerNestsGroupsAndParts() {
        const string Bryson = "World/Large Scenery/Bryson/", Dealer = Bryson + "Bryson Coal Dealer";
        var scene = new Inventory();
        scene.Objects.AddRange(new[] {
            Entry(Dealer + "/Bent/Beams", groupPath: Dealer),
            Entry(Bryson + "Bridge 1", "Bridge", "Rail bridges", only: "not yet supported"),
            // A group root names every component below it among its dependencies, as the native inventory does.
            Entry(Dealer, "Industry building", dependencies: new[] { "Component scene/" + Dealer + "/Heap [UnityEngine.MeshRenderer]", "Culling and colliders of every part follow the group root" }),
            Entry(Dealer + "/Heap", groupPath: Dealer),
            Entry("World/Large Scenery/Almond/Almond Road Bridge", "Bridge", "Road bridges", only: "not yet supported"),
            Entry(Bryson + "Bryson Depot", "Station building"),
            Entry("World/Large Scenery/Sylva/New Pond", only: "not yet supported"),
            Entry("World/Sign", family: "Track marker"),
        });
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var all = InventoryPresentationState.Rows(inventory, "", OutlinerFilter.All, null);
        var groups = InventoryPresentationState.Groups(all);
        Check(groups.Select(g => g.Key + ":" + g.Value.Count).SequenceEqual(new[] { "Baked scenery group:6", "Track marker:1", "Inspection only:1" }),
            "a group that no supported operation covers stays in the family under its category; only one without a category stands in the Inspection only group (got " + String.Join(", ", groups.Select(g => g.Key + ":" + g.Value.Count)) + ")");
        var tree = InventoryPresentationState.Categories(groups[0].Value, inventory.Entries, "");
        Check(Shape(tree) == "[Station building]{Bryson Depot} [Bridge]{[Rail bridges]{Bridge 1} [Road bridges]{Almond Road Bridge}} [Industry building]{Bryson Coal Dealer{Bent / Beams Heap}}",
            "groups stand under their categories in the family's order, Bridge under its subgroups, and each group lists its parts beneath it (got " + Shape(tree) + ")");
        Check(tree.Select(n => n.Count).SequenceEqual(new[] { 1, 2, 3 }), "a category counts the rows beneath it");
        // A group row says where it stands, so groups of one name can be told apart.
        Check(tree[2].Children[0].Where == "Bryson" && tree[2].Children[0].Children.All(part => part.Where == null), "a group row names its folder below World/Large Scenery; a part row does not");
        Check(InventoryPresentationState.GroupWhere("World/Large Scenery/Alarka Jct/Crossing Unprotected (1)/Sign-Crossing0 (1)") == "Alarka Jct / Crossing Unprotected (1)"
            && InventoryPresentationState.GroupWhere("World/Roads Barkers/1932 Chevy Truck LP") == "Roads Barkers" && InventoryPresentationState.GroupWhere("World/Lone") == null,
            "a group's place is the path between World/Large Scenery, or World, and its name");
        // A search that matches only a part still shows the part under its group, never as a loose row.
        var found = InventoryPresentationState.Rows(inventory, "Heap", OutlinerFilter.All, null);
        // The group's own row is found too, through the dependency that names its part: it does not match itself.
        Check(found.Any(row => row.Identity.Path == Dealer) && !found.Single(row => row.Identity.Path == Dealer).MatchesItself("Heap") && found.Single(row => row.Identity.Path == Dealer).MatchesItself("coal dealer")
            && found.Single(row => row.Identity.Path == Dealer).MatchesItself("colliders"), "a group matches itself by its name and by what it says about itself, not by a dependency that names an object below it");
        var narrowed = InventoryPresentationState.Categories(InventoryPresentationState.Groups(found)[0].Value, inventory.Entries, "Heap");
        Check(Shape(InventoryPresentationState.Categories(InventoryPresentationState.Groups(InventoryPresentationState.Rows(inventory, "coal dealer", OutlinerFilter.All, null))[0].Value, inventory.Entries, "coal dealer")).StartsWith("[Industry building]{Bryson Coal Dealer{", StringComparison.Ordinal),
            "a group that matches by its own name is a match, with the parts that match through its name beneath it");
        Check(Shape(narrowed) == "[Industry building]{Bryson Coal Dealer~{Heap}}", "a part that matches alone stands under its group, which is shown to hold it (got " + Shape(narrowed) + ")");
        Check(narrowed[0].Count == 1, "a group shown only to hold a part is not counted");
        // The category is searchable as shown.
        Check(InventoryPresentationState.Rows(inventory, "Rail bridges", OutlinerFilter.All, null).Select(r => r.Identity.Path).SequenceEqual(new[] { Bryson + "Bridge 1" }), "a search matches a group by its category");
    }

    // A bridge outside the category table takes its category from the bridge rule; a rail bridge construction's
    // controller and bridge, which other families list, stand under Bridge / Rail bridges in the outliner too.
    static void BridgesOutsideTheTable() {
        var world = new Node("World", new Node("Roads Bryson", new Node("Concrete Road Bridge - 20m", new Node("Deck").Solid()),
            new Node("US19 Gov Island", new Node("Grade Crossing Setup").With(BakedSceneryRules.CrossingController).Family(), new Node("Concrete Road Bridge - 60m", new Node("Deck").Solid()))),
            new Node("Large Scenery", new Node("Newtown", new Node("Plate Girder Span", new Node("Girder").Solid()))));
        var verdicts = BakedSceneryRules.Classify(world);
        foreach (var path in new[] { "World/Roads Bryson/Concrete Road Bridge - 20m", "World/Roads Bryson/US19 Gov Island/Concrete Road Bridge - 60m" }) {
            var verdict = At(verdicts, path);
            Check(verdict?.Disposition == GroupDisposition.InspectionOnly && verdict.Category == "Bridge" && verdict.Subcategory == "Road bridges", path + " is a road bridge the table does not name: it stands under Bridge / Road bridges (got " + verdict?.Category + ")");
        }
        var span = At(verdicts, "World/Large Scenery/Newtown/Plate Girder Span");
        Check(span?.Category == "Bridge" && span.Subcategory == "Rail bridges", "a rail bridge construction the table does not name stands under Bridge / Rail bridges");
        Check(BakedSceneryCategories.OfFamily("Bridge").SequenceEqual(new[] { "Bridge", "Rail bridges" }) && BakedSceneryCategories.OfFamily("Bridge construction scenery").SequenceEqual(new[] { "Bridge", "Rail bridges" })
            && BakedSceneryCategories.OfFamily("Track marker") == null && BakedSceneryCategories.OfFamily(null) == null, "the bridge and the construction controller of a rail bridge construction stand under Bridge / Rail bridges; no other family has a category");
        // In the outliner both stand in the Baked scenery group family, under Bridge / Rail bridges, as groups of their own.
        var scene = new Inventory();
        scene.Objects.AddRange(new[] {
            Entry("World/Large Scenery/Ela/Ela Bridge", "Bridge", "Rail bridges", family: "Bridge"),
            Entry("World/Large Scenery/Ela/Ela Bridge Scenery", "Bridge", "Rail bridges", only: "not yet supported", family: "Bridge construction scenery"),
            Entry("World/Large Scenery/Bryson/Bridge 1", "Bridge", "Rail bridges", only: "not yet supported"),
            Entry("World/Sign", family: "Track marker"),
        });
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var groups = InventoryPresentationState.Groups(InventoryPresentationState.Rows(inventory, "", OutlinerFilter.All, null));
        Check(groups.Select(g => g.Key + ":" + g.Value.Count).SequenceEqual(new[] { "Baked scenery group:3", "Track marker:1" }), "an object with a category stands in the Baked scenery group family, whatever family lists it (got " + String.Join(", ", groups.Select(g => g.Key + ":" + g.Value.Count)) + ")");
        Check(Shape(InventoryPresentationState.Categories(groups[0].Value, inventory.Entries, "")) == "[Bridge]{[Rail bridges]{Bridge 1 Ela Bridge Ela Bridge Scenery}}", "the bridge and its construction controller stand under Bridge / Rail bridges (got " + Shape(InventoryPresentationState.Categories(groups[0].Value, inventory.Entries, "")) + ")");
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
