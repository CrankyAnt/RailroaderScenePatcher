// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

// The outliner's rows from an inventory: an object with its parts or detection spans beneath it, each crossing as
// one node, what has no row, what a search and a filter show, the count line and the row tooltip.
static class OutlinerGroupingTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Outliner grouping: " + message); }

    const string SceneName = "scene", Baked = SceneObjectRules.Family, Bryson = "World/Large Scenery/Bryson/", Dealer = Bryson + "Bryson Coal Dealer", Signs = Bryson + "Bryson Signs",
        Allen = "World/Roads Sylva/Grade Crossing - Allen", Stenzel = "World/Roads Whittier/Crossing Unprotected - Stenzel";
    // An inspection-only entry carries its reason as its supported edits too, as NativeInventory builds it (operations = only).
    static SceneObservation Entry(string path, string family, double x, double z, string category = null, string groupPath = null, string holderPath = null, string only = null, string[] problems = null, string padMarkerId = null, string displayName = null, string subcategory = null) =>
        new SceneObservation(new object(), new TargetIdentity(SceneName, path, "UnityEngine.Transform", padMarkerId: padMarkerId), family, true, new[] { x, 10, z }, new double[] { 0, 0, 0, 1 },
            new string[0], new string[0], problems ?? new string[0], only ?? "Placement", inspectionOnly: only, displayName: displayName, groupPath: groupPath, category: category, subcategory: subcategory, holderPath: holderPath);
    sealed class Inventory : IInspectionScene {
        public List<SceneObservation> Objects = new List<SceneObservation>();
        public IEnumerable<string> MapIds => new[] { "map" };
        public IEnumerable<SceneObservation> Discover() => Objects;
    }
    // The world beside the inventory: the game's map data, and the components of the one object whose category they decide.
    sealed class World : IOutlinerScene {
        public string WorldKey => "world";
        public ObjectFacts Facts(SceneObservation entry) =>
            entry.Identity.Path.EndsWith("Loc: Connelly", StringComparison.Ordinal) ? new ObjectFacts { Own = new[] { SceneObjectRules.RendererCuller }, AtOrBelow = new[] { SceneObjectRules.RendererCuller } } : new ObjectFacts();
        public double[] Bounds(SceneObservation entry) => null;
        public IEnumerable<PlaceSource> PlaceSources(IReadOnlyList<SceneObservation> entries, List<string> notes) => new[] {
            new PlaceSource(PlaceSourceKind.MapData, "Bryson", new[] { 4400.0, 5400 }), new PlaceSource(PlaceSourceKind.MapData, "Sylva", new[] { 25000.0, -800 }),
            new PlaceSource(PlaceSourceKind.MapData, "Whittier", new[] { 12300.0, 5800 }), new PlaceSource(PlaceSourceKind.MapData, "Connelly Creek", new[] { 12500.0, 2500 }),
            new PlaceSource(PlaceSourceKind.MapLabel, "Log Camp", new[] { 7300.0, 4350 }),
        };
    }
    static string Shape(IEnumerable<OutlineNode> nodes) =>
        String.Join(" ", nodes.Select(n => (n.Row == null ? "[" + n.Label + "]" : n.Label + (n.Context ? "~" : "")) + (n.Children.Count == 0 ? "" : "{" + Shape(n.Children) + "}")));
    static OutlineNode Find(IEnumerable<OutlineNode> nodes, string label) {
        foreach (var node in nodes) { if (node.Label == label) return node; var below = Find(node.Children, label); if (below != null) return below; }
        return null;
    }

    // #110: an object that no supported operation covers stands under its category, kind and place in both views,
    // marked inspection only, with its reason in its tooltip and on its Object tab; an object that fits no category
    // stands under Unclassified. Fictional objects: the base-map fixture has no inspection-only object.
    static void InspectionOnlyRowsStandUnderTheirCategory() {
        var scene = new Inventory();
        string GroupReason = CommandBand.NotYetSupported("bridges with a moving span"), FamilyReason = CommandBand.InspectionOnly("Odd fixture", false);
        // A rail bridge no operation covers, beside an editable rail bridge and a road bridge: Bridge shows its kinds.
        var swing = Entry(Bryson + "Swing Bridge", Baked, 4450, 5450, "Bridge", only: GroupReason, subcategory: "Rail bridges");
        var girder = Entry(Bryson + "Bridge 9", Baked, 4460, 5460, "Bridge", subcategory: "Rail bridges");
        var road = Entry("World/Large Scenery/Sylva/Sylva Road Bridge", Baked, 25040, -840, "Bridge", subcategory: "Road bridges");
        // An object of a family no operation covers and no category names, and a listed group no rule recognises.
        var stand = Entry("World/Large Scenery/Sylva/Odd Fixture", "Odd fixture", 25050, -850, only: FamilyReason);
        var thing = Entry("World/Large Scenery/Sylva/Thing", Baked, 25060, -860, SceneObjectCategories.Unclassified);
        // An inspection-only group whose verdict has no category (SceneObjectCategoryTests: "Masked Thing").
        var masked = Entry("World/Large Scenery/Sylva/Masked Thing", Baked, 25070, -870, only: CommandBand.NotYetSupported("groups with a spawn point"));
        scene.Objects.AddRange(new[] { swing, girder, road, stand, thing, masked });
        var inventory = new SceneInventory(scene); inventory.Refresh();
        OutlinerGrouping.InvalidatePlaces();
        OutlinerGrouping.Scene = new World();
        OutlinerGrouping.LogPending = false;
        try {
            var all = OutlinerGrouping.Rows(inventory, "", OutlinerFilter.All, null, null);
            OutlinerGrouping.ByPlace = false;
            var tree = OutlinerGrouping.Build(all, inventory.Entries, "", false, null);
            Check(Shape(new[] { Find(tree.Tops, "Bridge") }) == "[Bridge]{[Rail bridges]{[Bryson]{Bridge 9 Swing Bridge}} [Road bridges]{[Sylva]{Sylva Road Bridge}}}",
                "by category, the inspection-only bridge stands under its category, kind and place beside the editable one (got " + Shape(tree.Tops) + ")");
            Check(Shape(new[] { Find(tree.Tops, SceneObjectCategories.Unclassified) }) == "[Unclassified]{[Sylva]{Masked Thing Odd Fixture Thing}}",
                "an object that fits no category stands under Unclassified, also when it is inspection only (got " + Shape(tree.Tops) + ")");
            Check(!tree.Tops.Any(t => t.Label.IndexOf("Inspection only", StringComparison.OrdinalIgnoreCase) >= 0), "there is no Inspection only group in the tree");
            OutlinerGrouping.ByPlace = true;
            var byPlace = OutlinerGrouping.Build(all, inventory.Entries, "", false, null);
            Check(Shape(new[] { Find(byPlace.Tops, "Bryson") }) == "[Bryson]{[Bridge]{[Rail bridges]{Bridge 9 Swing Bridge}}}",
                "by place, the inspection-only bridge stands under its place, category and kind (got " + Shape(new[] { Find(byPlace.Tops, "Bryson") }) + ")");
            Check(Shape(new[] { Find(Find(byPlace.Tops, "Sylva").Children, SceneObjectCategories.Unclassified) }) == "[Unclassified]{Masked Thing Odd Fixture Thing}",
                "by place, an object that fits no category stands under its place and Unclassified (got " + Shape(new[] { Find(byPlace.Tops, "Sylva") }) + ")");
            // The row is marked inspection only, and only an inspection-only object is.
            Check(InventoryPresentationState.RowMark(swing) == "inspection only" && InventoryPresentationState.RowMark(stand) == "inspection only" && InventoryPresentationState.RowMark(masked) == "inspection only"
                && InventoryPresentationState.RowMark(girder) == null && InventoryPresentationState.RowMark(thing) == null, "the row of an inspection-only object, and only that row, is marked inspection only");
            // The tooltip carries the reason, in both views, and says where the row stands.
            foreach (var (built, view) in new[] { (tree, "by category"), (byPlace, "by place") }) {
                var tip = OutlinerGrouping.Tooltip(built, swing, false, null);
                Check(tip == "Swing Bridge\n" + Bryson + "Swing Bridge\nGroup of scene objects, not a track group\nBridge / Rail bridges\nPlace: Bryson\n" + CommandBand.InspectionOnlyLine(GroupReason), view + ": the tooltip gives the place in the tree and the reason (got " + tip + ")");
                Check(OutlinerGrouping.Tooltip(built, stand, false, null).EndsWith("\n" + CommandBand.InspectionOnlyLine(FamilyReason), StringComparison.Ordinal)
                    && OutlinerGrouping.Tooltip(built, stand, false, null).Contains("\nUnclassified\nPlace: Sylva\n"), view + ": an unclassified inspection-only object's tooltip names Unclassified, its place and its reason");
                Check(!OutlinerGrouping.Tooltip(built, girder, false, null).Contains("Inspection only"), view + ": an editable object's tooltip gives no reason");
            }
            // The Object tab carries the same reason: Supported edits shows the entry's operations, which hold the reason,
            // and the state line's tooltip adds the same Inspection only line as the row's tooltip.
            Check(swing.Operations == GroupReason && stand.Operations == FamilyReason, "Supported edits on the Object tab gives the reason");
            Check(CommandBand.InspectionOnlyLine(GroupReason) == "Inspection only: Bridges with a moving span cannot be edited.",
                "the Inspection only line holds the reason as one sentence (got " + CommandBand.InspectionOnlyLine(GroupReason) + ")");
        } finally {
            OutlinerGrouping.Scene = null; OutlinerGrouping.ByPlace = false; OutlinerGrouping.LogPending = true; OutlinerGrouping.InvalidatePlaces();
        }
    }
    public static void Run() {
        var scene = new Inventory();
        var dealer = Entry(Dealer, Baked, 4410, 5410, "Industry building");
        var yardLimit = Entry(Signs + "/Yard Limit", "Track marker", 4300, 5300);
        var board = Entry("World/Signs/Loc: Connelly", "Location sign", 12040, 5565, problems: new[] { "The board's mask is missing" });
        var shed = Entry("World/Large Scenery/Sylva/Sylva Shed", Baked, 25020, -820, only: "This group contains a map icon, which the patcher can't move with the group.");
        var iconSigns = Entry("World/Large Scenery/Sylva/Depot Signs", Baked, 25030, -830, "Sign", only: "This group contains a map icon, which the patcher can't move with the group.");
        scene.Objects.AddRange(new[] {
            Entry(Dealer + "/Heap", Baked, 4411, 5411, groupPath: Dealer), dealer,
            Entry(Bryson + "Tall Post Shade (1)", Baked, 4420, 5420, "Lights"), Entry(Bryson + "Tall Post Shade", Baked, 4430, 5430, "Lights"),
            Entry("World/Large Scenery/Sylva/Tall Post Shade", Baked, 25010, -810, "Lights"),
            shed,
            // A lamp post of the same name in a folder of the same place stands beside the others in one folder.
            Entry(Bryson + "Bryson Yard/Tall Post Shade", Baked, 4440, 5440, "Lights"),
            // A sign group that has no edit for another reason, and holds no object of another family, keeps its row.
            iconSigns,
            // A sign container holds only signs another family lists: it has no row, and its sign stands on its own.
            Entry(Signs, Baked, 0, 0, "Sign", only: "You can't move this group as a whole."), yardLimit,
            // A crossing with a controller: the controller holds its mast, pad and detection span.
            Entry(Allen, "Grade crossing controller", 25100, -700), Entry(Allen + "/Track 1 East", "Track span", 25101, -701, holderPath: Allen),
            Entry(Allen + "/Signal 1/Signal 1", "Crossing mast", 25102, -702), Entry(Allen + "/Crossing Pad", "Crossing pad marker", 25103, -703),
            // A light and a bell the patcher added stand with the crossing's own equipment, under the names the inventory gives them.
            Entry(Allen + "/Signal 1 copy", SceneFamilies.AddedEquipment, 25104, -704, displayName: "Signal 1 (added)"), Entry(Allen + "/Brass bell", SceneFamilies.AddedBell, 25105, -705, displayName: "Brass bell (added)"),
            // A crossing without one: its crossing object is one node; its whistle sign is a sign.
            Entry(Stenzel + "/Sign-Crossing0 (2)", Baked, 12310, 5812, "Grade crossing"), Entry(Stenzel + "/Sign-Crossing0 (1)", Baked, 12310, 5810, "Grade crossing"),
            Entry(Stenzel + "/Crossing Pad", "Crossing pad marker", 12310, 5814), Entry(Stenzel + "/Whistle Sign E", "Track marker", 12400, 5900),
            board,
            Entry("World/Mile Posts/T-10", "Milepost", 4500, 5500), Entry("World/Mile Posts/T-2", "Milepost", 4510, 5510), Entry("World/Mile Posts/T-11", "Milepost", 25200, -600), Entry("World/Mile Posts/T-12", "Milepost", 25210, -610),
            // A native track span no controller holds has no row.
            Entry("Ops/OpsTrackSpans/Span-a", "Track span", 0, 0),
        });
        var inventory = new SceneInventory(scene); inventory.Refresh();
        OutlinerGrouping.InvalidatePlaces();
        OutlinerGrouping.Scene = new World();
        OutlinerGrouping.LogPending = false;
        try {
            OutlinerGrouping.ByPlace = false;
            var all = OutlinerGrouping.Rows(inventory, "", OutlinerFilter.All, null, null);
            var tree = OutlinerGrouping.Build(all, inventory.Entries, "", false, null);
            Check(tree.Tops.Select(t => t.Label).SequenceEqual(new[] { "Grade crossing", "Industry building", "Lights", "Mileposts", "Sign" }), "the categories stand in alphabetical order (got " + Shape(tree.Tops) + ")");
            Check(Shape(new[] { tree.Tops[1] }) == "[Industry building]{[Bryson]{Bryson Coal Dealer{Heap}} [Sylva]{Sylva Shed}}", "a group lists its parts beneath it, under its place (got " + Shape(new[] { tree.Tops[1] }) + ")");
            Check(Shape(new[] { tree.Tops[2] }) == "[Lights]{[Bryson]{Tall Post Shade Tall Post Shade Tall Post Shade (1)} [Sylva]{Tall Post Shade}}", "a category with one kind lists its objects by place, by name, and two objects of one name stand side by side (got " + Shape(new[] { tree.Tops[2] }) + ")");
            var twins = Find(tree.Tops[2].Children, "Bryson").Children.Where(n => n.Label == "Tall Post Shade").ToArray();
            Check(twins.Length == 2 && twins[0].Key != twins[1].Key && twins[0].Row.Identity.Path == Bryson + "Bryson Yard/Tall Post Shade" && OutlinerGrouping.Tooltip(tree, twins[0].Row, false, null).Contains(Bryson + "Bryson Yard/Tall Post Shade"),
                "rows of one name in one folder are two rows in the order of their paths, and each tooltip gives its own path");
            // An object without an edit stands under its category like any other; its tooltip gives the reason.
            var shedRow = Find(tree.Tops, "Sylva Shed");
            Check(ReferenceEquals(shedRow?.Row, shed) && shedRow.Row.InspectionOnly != null && OutlinerGrouping.Tooltip(tree, shed, false, null).Contains(CommandBand.InspectionOnlyLine(shed.InspectionOnly)), "an inspection-only group stands under its category, and its tooltip gives the reason");
            Check(ReferenceEquals(Find(tree.Tops, "Depot Signs")?.Row, iconSigns) && !OutlinerGrouping.SignContainers(inventory.Entries).Contains(iconSigns) && OutlinerGrouping.SignContainers(inventory.Entries).Single().Identity.Path == Signs,
                "a sign group that is inspection only and holds no object of another family keeps its row; only the one that holds another family's signs has none");
            // The sign container and the native track span have no row; the count line counts what the tree can list.
            Check(Find(tree.Tops, "Bryson Signs") == null && Find(tree.Tops, "Span-a") == null && OutlinerGrouping.HiddenCount(inventory.Entries) == 2, "a sign container and a native track span have no row");
            Check(tree.Listed == inventory.Entries.Count - 2 && OutlinerGrouping.Summary(tree, inventory.Entries) == tree.Listed + " of " + tree.Listed + " objects", "the count line counts the rows of the tree (got " + OutlinerGrouping.Summary(tree, inventory.Entries) + ")");
            Check(tree.Unplaced == 0, "no object stands in a catch-all node");
            // Mileposts are a category of their own, by place, in the order of their numbers; none stands under Sign.
            var mileposts = tree.Tops.Single(t => t.Label == "Mileposts");
            Check(Shape(new[] { mileposts }) == "[Mileposts]{[Bryson]{T-2 T-10} [Sylva]{T-11 T-12}}", "mileposts stand by place, in the order of their numbers (got " + Shape(new[] { mileposts }) + ")");
            var sign = tree.Tops.Single(t => t.Label == "Sign");
            Check(Find(sign.Children, "T-2") == null && Find(sign.Children, "Mileposts") == null, "no milepost stands under Sign (got " + Shape(sign.Children) + ")");
            // Without the mileposts no kind of Sign is shared, so Sign lists its signs by place alone.
            Check(sign.Children.All(c => c.Key.Contains("/" + OutlineTree.PlaceKey)), "Sign without mileposts lists its signs by place (got " + Shape(sign.Children) + ")");
            Check(Shape(new[] { Find(sign.Children, "Connelly Creek") }) == "[Connelly Creek]{Loc: Connelly}", "a Loc: board is a location sign at the place it names, under the game's name of that place");
            Check(Find(Find(sign.Children, "Bryson").Children, "Yard Limit") != null && Find(sign.Children, "Whistle Sign E") != null, "a sign in a sign container and a whistle sign at a crossing are signs of their own");
            // Crossings: the controller is the row that holds its equipment; a crossing without one is one node.
            var controller = Find(tree.Tops, "Grade Crossing - Allen");
            Check(controller?.Row != null && controller.Children.Select(c => c.Label).SequenceEqual(new[] { "Signal 1", "Signal 1 (added)", "Brass bell (added)", "Crossing Pad", "Track 1 East" }) && controller.Count == 6, "a crossing controller holds its mast, the added light and bell, its pad and its detection span (got " + (controller == null ? "none" : Shape(controller.Children)) + ")");
            var crossing = Find(tree.Tops, "Crossing Unprotected - Stenzel");
            Check(crossing != null && crossing.Row == null && crossing.Children.Select(c => c.Label).SequenceEqual(new[] { "Sign-Crossing0 (1)", "Sign-Crossing0 (2)", "Crossing Pad" }) && crossing.Count == 3,
                "a crossing without a controller is one node with its crossbucks and pad, under their game names (got " + (crossing == null ? "none" : Shape(crossing.Children)) + ")");
            Check(tree.GroupNodes.TryGetValue(crossing.Key, out var node) && node.Label == crossing.Label && Math.Abs(node.Point[0] - 12310) < 0.01 && Math.Abs(node.Point[2] - 5812) < 0.01, "Focus on a crossing node goes to the middle of its members");
            Check(Find(tree.Tops, "Whittier").Children.Contains(crossing), "the crossing stands at the place its Roads folder names");

            // The place view: every place the world names, also without objects, in alphabetical order.
            OutlinerGrouping.ByPlace = true;
            var byPlace = OutlinerGrouping.Build(all, inventory.Entries, "", false, null);
            Check(byPlace.Tops.Select(t => t.Label + ":" + t.Count).SequenceEqual(new[] { "Bryson:8", "Connelly Creek:1", "Log Camp:0", "Sylva:11", "Whittier:4" }), "every place is a top-level node, also without objects (got " + String.Join(", ", byPlace.Tops.Select(t => t.Label + ":" + t.Count)) + ")");
            Check(byPlace.Tops[0].Children.Select(c => c.Label).SequenceEqual(new[] { "Industry building", "Lights", "Mileposts", "Sign" }), "a place holds its categories in alphabetical order");
            Check(OutlineTree.Violations(byPlace.Tops, true, byPlace.Tops.Select(t => t.Label)).Count == 0 && OutlineTree.Violations(tree.Tops, false, byPlace.Tops.Select(t => t.Label)).Count == 0, "both views keep the rules of the tree");

            // A search matches what the outliner shows of an object: its kind, its place under any of its names.
            var kind = OutlinerGrouping.Rows(inventory, "Yard limits", OutlinerFilter.All, null, null);
            Check(kind.SequenceEqual(new[] { yardLimit }) && InventoryPresentationState.Rows(inventory, "Yard limits", OutlinerFilter.All, null).Count == 0, "a search finds an object by its kind, which the object does not say itself");
            var place = OutlinerGrouping.Rows(inventory, "Connelly Creek", OutlinerFilter.All, null, null);
            Check(place.SequenceEqual(new[] { board }), "a search finds an object by the game's name of its place");
            var narrowed = OutlinerGrouping.Build(place, inventory.Entries, "Connelly Creek", true, null);
            Check(narrowed.Tops.Select(t => t.Label).SequenceEqual(new[] { "Connelly Creek" }), "under a search only places with matches show");
            // A part that matches alone stands under its group, which is shown only to hold it and is not counted.
            OutlinerGrouping.ByPlace = false;
            var heap = OutlinerGrouping.Build(OutlinerGrouping.Rows(inventory, "Heap", OutlinerFilter.All, null, null), inventory.Entries, "Heap", true, null);
            Check(Shape(heap.Tops) == "[Industry building]{[Bryson]{Bryson Coal Dealer~{Heap}}}" && heap.Listed == 1, "a part that matches alone stands under its group, which is not counted (got " + Shape(heap.Tops) + ")");
            // The filters narrow the same tree.
            var problems = OutlinerGrouping.Rows(inventory, "", OutlinerFilter.Problems, null, null);
            var changed = OutlinerGrouping.Rows(inventory, "", OutlinerFilter.Changed, row => ReferenceEquals(row, dealer), null);
            Check(problems.SequenceEqual(new[] { board }) && changed.SequenceEqual(new[] { dealer }) && OutlinerGrouping.Rows(inventory, "", OutlinerFilter.Changed, null, null).Count == 0, "Problems and Changed show the objects with a problem and the objects this mod changes");
            Check(OutlinerGrouping.Rows(null, "", OutlinerFilter.All, null, null).Count == 0, "without an inventory there are no rows");

            // The tooltip of a row: its name, path, that it is a group of scene objects, where the outliner lists it, and
            // what its markers mean. No family.
            var tip = OutlinerGrouping.Tooltip(tree, dealer, changed: true, failure: null);
            Check(tip == "Bryson Coal Dealer\n" + Dealer + "\nGroup of scene objects\nIndustry building\nPlace: Bryson\n" + OutlinerGrouping.ChangedLine, "a changed group's tooltip says where it stands and why it is changed (got " + tip + ")");
            Check(!tip.Contains(Baked) && OutlinerGrouping.ChangedLine.Contains("scene patch file"), "the tooltip names no family, and Changed names the scene patch file");
            var boardTip = OutlinerGrouping.Tooltip(tree, board, false, null);
            Check(boardTip == "Loc: Connelly\nWorld/Signs/Loc: Connelly\nSign\nPlace: Connelly Creek\nProblem: The board's mask is missing", "a tooltip lists each problem on its own line (got " + boardTip + ")");
            Check(OutlinerGrouping.FolderTooltip(tree.Tops[2]) == "Lights\n4 objects" && OutlinerGrouping.FolderTooltip(Find(sign.Children, "Connelly Creek")) == "Connelly Creek\n1 object", "a folder's tooltip gives its name and its count");
            // Fit measures every row of the tree with its depth.
            var labels = OutlinerGrouping.FitLabels();
            Check(labels.Any(l => l.Key == "(1) Industry building" && l.Value == 0) && labels.Any(l => l.Key == "Heap" && l.Value == 3), "Fit takes each row's text and depth");
        } finally {
            OutlinerGrouping.Scene = null; OutlinerGrouping.ByPlace = false; OutlinerGrouping.LogPending = true; OutlinerGrouping.InvalidatePlaces();
        }
        InspectionOnlyRowsStandUnderTheirCategory();
        Console.WriteLine("PASS: outliner grouping");
    }
}
