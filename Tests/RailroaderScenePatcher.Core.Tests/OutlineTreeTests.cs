// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

// The outliner over every listed object of the base map, built as the window builds it (OutlinerGrouping.Build), in
// both views: the same levels in every folder, every list in order, one node per place and no place below another,
// each group with its parts, each crossing as one node, and the sign containers without a row.
static class OutlineTreeTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Outline tree: " + message); }

    static IEnumerable<OutlineNode> All(IEnumerable<OutlineNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(All(n.Children)));
    static string Shape(IEnumerable<OutlineNode> nodes) => String.Join(" ", nodes.Select(n => n.Label + (n.Children.Count == 0 ? "" : "{" + Shape(n.Children) + "}")));
    static bool IsPlaceFolder(OutlineNode node) => node.Row == null && node.Key.Substring(node.Key.LastIndexOf('/') + 1).StartsWith(OutlineTree.PlaceKey, StringComparison.Ordinal);

    public static void Run() {
        Order();
        var map = new BaseMapFixture();
        var entries = map.Observations(out var source);
        OutlinerGrouping.InvalidatePlaces();
        OutlinerGrouping.Scene = map.Scene(source);
        OutlinerGrouping.LogPending = true;
        try {
            OutlinerGrouping.ByPlace = false;
            var byCategory = OutlinerGrouping.Build(entries, entries, "", false, null);
            OutlinerGrouping.LogPending = true;
            OutlinerGrouping.ByPlace = true;
            var byPlace = OutlinerGrouping.Build(entries, entries, "", false, null);
            var places = map.Places.All.Select(p => p.Name).ToArray();
            if (Environment.GetEnvironmentVariable("RSP_FIXTURE_READOUT") == "1") {
                foreach (var top in byCategory.Tops) Console.WriteLine("CATEGORY (" + top.Count + ") " + top.Label + ": " + String.Join(", ", top.Children.Select(c => "(" + c.Count + ") " + c.Label)));
                foreach (var top in byPlace.Tops) Console.WriteLine("PLACEVIEW (" + top.Count + ") " + top.Label + ": " + String.Join(", ", top.Children.Select(c => "(" + c.Count + ") " + c.Label)));
            }
            // What has no row, counted from the fixture alone: the three sign containers that hold only track markers,
            // and the track spans no crossing controller holds.
            var containers = new[] { "World/Large Scenery/Bryson/Bryson Signs", "World/Large Scenery/Ela/Signs", "World/Large Scenery/Sylva/Sylva Signs" };
            // Every listed track span of the base map is a detection span of a crossing controller: the spans the game
            // builds from its operations data are not in the inventory, so the outliner hides no span here.
            var nativeSpans = entries.Count(e => e.Family == SceneFamilies.TrackSpan && e.HolderPath == null);
            Check(nativeSpans == 0 && OutlinerGrouping.HiddenCount(entries) == containers.Length, "on the base map the sign containers are the only objects without a row (" + OutlinerGrouping.HiddenCount(entries) + ")");
            Check(byCategory.Hidden.OrderBy(h => h, StringComparer.Ordinal).SequenceEqual(containers.Select(c => "container object " + c)), "the hidden list names the objects of the inventory without a row: " + String.Join(" | ", byCategory.Hidden));
            Check(OutlinerGrouping.SignContainers(entries).Select(e => e.Identity.Path).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(containers), "the sign containers are Bryson Signs, Ela Signs and Sylva Signs (got " + String.Join(", ", OutlinerGrouping.SignContainers(entries).Select(e => e.Identity.Path)) + ")");
            foreach (var view in new[] { new { Name = "By category", Tree = byCategory, Place = false }, new { Name = "By place", Tree = byPlace, Place = true } }) {
                var tops = view.Tree.Tops;
                Check(view.Tree.Violations.Count == 0, view.Name + ": " + view.Tree.Violations.Count + " violations: " + String.Join(" | ", view.Tree.Violations.Take(15)));
                Check(view.Tree.Listed == entries.Count - containers.Length - nativeSpans && tops.Sum(t => t.Count) == view.Tree.Listed, view.Name + " lists every object but the sign containers and the native track spans once (" + view.Tree.Listed + " of " + entries.Count + ")");
                Check(view.Tree.Unplaced == 0 && view.Tree.CatchAll.Count == 0 && !All(tops).Any(n => n.Label == OutlineTree.NoPlace || n.Label == BakedSceneryCategories.Unclassified), view.Name + " has no catch-all node: " + String.Join(" | ", view.Tree.CatchAll.Take(5)));
                var rows = All(tops).Where(n => n.Row != null).Select(n => n.Row).ToArray();
                Check(rows.Length == rows.Distinct().Count() && !rows.Any(r => containers.Contains(r.Identity.Path)), view.Name + " shows each object once and no sign container");
                // Every part stands beneath its group, and every detection span beneath its controller.
                foreach (var node in All(tops).Where(n => n.Row != null))
                    foreach (var child in node.Children.Where(c => c.Row?.GroupPath != null)) Check(child.Row.GroupPath == node.Row.Identity.Path, child.Row.Identity.Path + " stands beneath its group");
                Check(All(tops).Where(IsPlaceFolder).SelectMany(f => f.Children).All(c => c.Row == null || (c.Row.GroupPath == null && c.Row.HolderPath == null)), view.Name + ": no part and no detection span is a loose row");
            }
            var tree = byCategory.Tops;
            Check(tree.Select(t => t.Label).SequenceEqual(new[] { "Bridge", "Grade crossing", "Industry building", "Lights", "Platform", "Railroad service building", "Road scenery", "Sign", "Spawn point", "Starter placement", "Station building", "Station map icon", "Storage display", "Track marker", "Water" }),
                "the categories of the base map, in alphabetical order (got " + String.Join(", ", tree.Select(t => t.Label)) + ")");
            // Lights: its kinds, and the places below each kind.
            var lights = tree.Single(t => t.Label == "Lights");
            Check(lights.Children.Select(c => c.Label).SequenceEqual(new[] { "Spotlight Outdoor", "Tall Post Shade" }), "Lights holds its kinds (got " + String.Join(", ", lights.Children.Select(c => c.Label)) + ")");
            Check(Shape(lights.Children[0].Children) == "Whittier{Spotlight Outdoor Spotlight Outdoor (1) Whittier Stenzel{Spotlight Outdoor Spotlight Outdoor (1)}}", "Spotlight Outdoor holds its place, with Whittier Stenzel and its two lights as parts (got " + Shape(lights.Children[0].Children) + ")");
            Check(lights.Children[1].Children.All(IsPlaceFolder) && lights.Children[1].Children.Select(c => c.Label).SequenceEqual(new[] { "Alarka", "Alarka Jct", "Almond", "Andrews", "Bryson", "Whittier" }), "Tall Post Shade holds only places (got " + String.Join(", ", lights.Children[1].Children.Select(c => c.Label)) + ")");
            // A category whose objects are each a kind of their own lists them by place alone.
            Check(tree.Single(t => t.Label == "Industry building").Children.All(IsPlaceFolder) && tree.Single(t => t.Label == "Railroad service building").Children.All(IsPlaceFolder), "Industry building and Railroad service building list their buildings by place");
            // Crossings, by the rule of the window: a crossing controller is a row that holds its equipment, and each
            // crossing without one is one node that holds its crossbucks and pads.
            var crossings = tree.Single(t => t.Label == "Grade crossing");
            Check(crossings.Children.Select(c => c.Label).SequenceEqual(new[] { SceneClassification.ProtectedCrossings, SceneClassification.UnprotectedCrossings }), "Grade crossing holds crossings with and without signals (got " + String.Join(", ", crossings.Children.Select(c => c.Label)) + ")");
            var controllers = All(new[] { crossings.Children[0] }).Where(n => n.Row?.Family == SceneFamilies.CrossingController).ToArray();
            Check(controllers.Length == 4 && controllers.All(c => c.Children.Count(m => m.Row.Family == SceneFamilies.CrossingMast) == 2 && c.Children.Count(m => m.Row.Family == SceneFamilies.CrossingBell) == 1 && c.Children.Any(m => m.Row.Family == SceneFamilies.CrossingPad) && c.Children.Any(m => m.Row.Family == SceneFamilies.TrackSpan)),
                "each of the four crossing controllers holds its two masts, its bell, its pads and its detection spans (got " + controllers.Length + ")");
            var nodes = All(new[] { crossings.Children[1] }).Where(n => n.Row == null && byCategory.GroupNodes.ContainsKey(n.Key)).ToArray();
            var crossbuckParents = entries.Where(e => e.Family == SceneFamilies.BakedScenery && e.GroupPath == null && e.Category == BakedSceneryCategories.GradeCrossing).Select(e => e.Identity.Path.Substring(0, e.Identity.Path.LastIndexOf('/'))).Distinct().Count();
            Check(nodes.Length == crossbuckParents && nodes.All(n => n.Children.Count(m => m.Row.Category == BakedSceneryCategories.GradeCrossing) >= 2 && n.Children.Any(m => m.Row.Family == SceneFamilies.CrossingPad) && byCategory.GroupNodes[n.Key].Point != null),
                "each crossing object without a controller is one node with its crossbucks and pads and a point for Focus (" + nodes.Length + " of " + crossbuckParents + ")");
            Check(!All(tree).Any(n => n.Row != null && n.Row.Family == SceneFamilies.CrossingPad && !All(new[] { crossings }).Contains(n)) && All(tree.Where(t => t.Label == "Sign")).Count(n => n.Label.StartsWith("Whistle Sign", StringComparison.Ordinal)) == 60, "every pad stands at its crossing, and the 60 whistle signs are signs");
            // Whittier Signs lists a part of its own, so it keeps its row; the signs of the containers are rows of their own.
            Check(All(tree).Any(n => n.Row?.Identity.Path == "World/Large Scenery/Whittier/Whittier Signs") && All(tree).Any(n => n.Row?.Identity.Path == "World/Large Scenery/Ela/Signs/Station Sign"), "Whittier Signs keeps its row, and a sign of a container is a row of its own");
            // The place view: every place the game names is a top-level node, also without objects; no other name of a place has a node.
            var placeTops = byPlace.Tops;
            Check(placeTops.Select(t => t.Label).SequenceEqual(places.OrderBy(p => p, OutlineTree.NaturalComparer.Instance)), "every place is a top-level node, in alphabetical order");
            Check(placeTops.Single(t => t.Label == "Log Camp").Count == 0 && placeTops.Count(t => t.Count == 0) == 1, "Log Camp is the one place without objects");
            Check(placeTops.Single(t => t.Label == "Connelly Creek").Count >= 5 && !All(placeTops).Any(n => n.Row == null && n.Label == "Connelly"), "Connelly Creek holds the Connelly objects, and no node is named Connelly");
            var whittier = placeTops.Single(t => t.Label == "Whittier");
            Check(whittier.Children.Select(c => c.Label).SequenceEqual(new[] { "Bridge", "Grade crossing", "Industry building", "Lights", "Sign", "Spawn point", "Starter placement", "Station building", "Station map icon", "Storage display", "Track marker" }),
                "the categories of a place are in alphabetical order (got " + String.Join(", ", whittier.Children.Select(c => c.Label)) + ")");
            // A search narrows the rows; the levels stay, and a place without matches is not shown.
            var lamps = entries.Where(e => e.Category == "Lights").ToArray();
            var narrowed = OutlinerGrouping.Build(lamps, entries, "", true, null);
            Check(narrowed.Tops.All(t => t.Count > 0) && narrowed.Tops.Select(t => t.Label).SequenceEqual(new[] { "Alarka", "Alarka Jct", "Almond", "Andrews", "Bryson", "Whittier" }) && OutlineTree.Violations(narrowed.Tops, true, places).Count == 0,
                "a narrowed place view shows only places with matches (got " + String.Join(", ", narrowed.Tops.Select(t => t.Label)) + ")");
        } finally {
            OutlinerGrouping.Scene = null; OutlinerGrouping.ByPlace = false; OutlinerGrouping.LogPending = true; OutlinerGrouping.InvalidatePlaces();
        }
        Console.WriteLine("PASS: outline tree");
    }

    // The order of every list, and which categories show kinds, on entries alone.
    static void Order() {
        Check(OutlineTree.NaturalComparer.Instance.Compare("T-2", "T-10") < 0 && OutlineTree.NaturalComparer.Instance.Compare("lights", "Station building") < 0 && OutlineTree.NaturalComparer.Instance.Compare("Tall Post Shade", "Tall Post Shade (1)") < 0,
            "numbers sort by value and case does not count");
        OutlineEntry Entry(string label, string category, string kind, string place) => new OutlineEntry { Node = new OutlineNode { Key = label, Label = label, Count = 1 }, Category = category, Kind = kind, Place = place };
        var entries = new[] { Entry("b", "Lights", "Lamp", "Y"), Entry("a", "Lights", "Lamp", "X"), Entry("c", "Lights", "Lantern", "X"), Entry("d", "Mill", "One", "X"), Entry("e", "Mill", "Two", "X") };
        var kinds = OutlineTree.KindCategories(entries);
        Check(kinds.SetEquals(new[] { "Lights" }), "a category shows kinds when it has two or more and at least half of its objects share one (got " + String.Join(", ", kinds) + ")");
        Check(Shape(OutlineTree.ByCategory(entries, kinds)) == "Lights{Lamp{X{a} Y{b}} Lantern{X{c}}} Mill{X{d e}}", "by category: category, kind where shown, place, objects (got " + Shape(OutlineTree.ByCategory(entries, kinds)) + ")");
        Check(Shape(OutlineTree.ByPlace(entries, kinds, new[] { "Z" })) == "X{Lights{Lamp{a} Lantern{c}} Mill{d e}} Y{Lights{Lamp{b}}} Z", "by place: place, category, kind where shown, objects, and a place without objects (got " + Shape(OutlineTree.ByPlace(entries, kinds, new[] { "Z" })) + ")");
        var mixed = new List<OutlineNode> { new OutlineNode { Key = OutlineTree.CategoryKey + "B", Label = "B" }, new OutlineNode { Key = OutlineTree.CategoryKey + "A", Label = "A" } };
        Check(OutlineTree.Violations(mixed, false, null).Any(v => v.StartsWith("order:", StringComparison.Ordinal)), "a list out of order breaks the rules of the tree");
    }
}
