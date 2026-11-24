// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

// The one tree of the outliner over every listed object of the base map, in both views: the same levels in every
// folder, every list in order, one node per place and no place below another.
static class OutlineTreeTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Outline tree: " + message); }

    static List<OutlineEntry> Entries(BaseMapFixture map) => map.Objects.Where(o => o.Listed && !o.BakedPart).Select((o, i) => {
        var found = map.ClassOf(o);
        // The outliner tells a crossing with signals from one without by its crossing controller.
        var kind = found.Category == BakedSceneryCategories.GradeCrossing ? (o.Path.Contains("/Grade Crossing") ? SceneClassification.ProtectedCrossings : SceneClassification.UnprotectedCrossings) : found.Kind;
        return new OutlineEntry { Node = new OutlineNode { Key = i + "\n" + o.Path, Label = o.Name, Count = 1 }, Category = found.Category, Kind = kind, Place = map.Places.Of(o.Path, o.At)?.Name };
    }).ToList();
    static IEnumerable<OutlineNode> All(IEnumerable<OutlineNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(All(n.Children)));
    static string Shape(IEnumerable<OutlineNode> nodes) => String.Join(" ", nodes.Select(n => n.Label + (n.Children.Count == 0 ? "" : "{" + Shape(n.Children) + "}")));

    public static void Run() {
        var map = new BaseMapFixture();
        var entries = Entries(map);
        var kinds = OutlineTree.KindCategories(entries);
        var places = map.Places.All.Select(p => p.Name).ToArray();
        var byCategory = OutlineTree.ByCategory(entries, kinds);
        var byPlace = OutlineTree.ByPlace(entries, kinds, places);
        if (Environment.GetEnvironmentVariable("RSP_FIXTURE_READOUT") == "1") {
            Console.WriteLine("KINDS " + String.Join(", ", kinds.OrderBy(k => k)));
            foreach (var top in byCategory) Console.WriteLine("CATEGORY (" + top.Count + ") " + top.Label + ": " + String.Join(", ", top.Children.Select(c => "(" + c.Count + ") " + c.Label)));
            foreach (var top in byPlace) Console.WriteLine("PLACEVIEW (" + top.Count + ") " + top.Label + ": " + String.Join(", ", top.Children.Select(c => "(" + c.Count + ") " + c.Label)));
        }
        foreach (var view in new[] { new { Name = "By category", Tops = byCategory, Place = false }, new { Name = "By place", Tops = byPlace, Place = true } }) {
            var broken = OutlineTree.Violations(view.Tops, view.Place, places);
            Check(broken.Count == 0, view.Name + ": " + broken.Count + " violations: " + String.Join(" | ", broken.Take(15)));
            Check(view.Tops.Sum(t => t.Count) == entries.Count, view.Name + " lists every object once (" + view.Tops.Sum(t => t.Count) + " of " + entries.Count + ")");
            Check(!All(view.Tops).Any(n => n.Label == OutlineTree.NoPlace || n.Label == BakedSceneryCategories.Unclassified), view.Name + " has no catch-all node");
        }
        // Lights: its kinds, and the places below each kind.
        var lights = byCategory.Single(t => t.Label == "Lights");
        Check(lights.Children.Select(c => c.Label).SequenceEqual(new[] { "Spotlight Outdoor", "Tall Post Shade" }), "Lights holds its kinds (got " + String.Join(", ", lights.Children.Select(c => c.Label)) + ")");
        Check(lights.Children[0].Children.Select(c => c.Label).SequenceEqual(new[] { "Whittier" }) && lights.Children[0].Count == 3, "Spotlight Outdoor holds its place, with the Whittier Stenzel lights in it (got " + Shape(lights.Children[0].Children) + ")");
        Check(lights.Children[1].Children.All(c => c.Key.Contains("/" + OutlineTree.PlaceKey)) && lights.Children[1].Children.Any(c => c.Label == "Andrews") && lights.Children[1].Children.Any(c => c.Label == "Bryson"),
            "Tall Post Shade holds only places (got " + String.Join(", ", lights.Children[1].Children.Select(c => c.Label)) + ")");
        // A category whose objects are each a kind of their own lists them by place alone.
        Check(!kinds.Contains("Industry building") && byCategory.Single(t => t.Label == "Industry building").Children.All(c => c.Key.Contains("/" + OutlineTree.PlaceKey)), "Industry building lists its buildings by place");
        // Every place the game names is a top-level node of the place view, also without objects; no alias has a node.
        Check(places.All(p => byPlace.Any(t => t.Label == p)), "every place is a top-level node");
        Check(byPlace.Single(t => t.Label == "Connelly Creek").Count >= 5 && !All(byPlace).Any(n => n.Label == "Connelly" && n.Row == null && n.Key.StartsWith(OutlineTree.PlaceKey)), "Connelly Creek holds the Connelly objects, and no node is named Connelly");
        var whittier = byPlace.Single(t => t.Label == "Whittier");
        Check(whittier.Children.Select(c => c.Label).SequenceEqual(whittier.Children.Select(c => c.Label).OrderBy(l => l, StringComparer.OrdinalIgnoreCase)) && whittier.Children[0].Label == "Bridge", "the categories of a place are in alphabetical order (got " + String.Join(", ", whittier.Children.Select(c => c.Label)) + ")");
        Check(OutlineTree.NaturalComparer.Instance.Compare("T-2", "T-10") < 0 && OutlineTree.NaturalComparer.Instance.Compare("lights", "Station building") < 0, "numbers sort by value and case does not count");
        // A search narrows the rows; the levels stay, and a place without matches is not shown.
        var narrowed = OutlineTree.ByPlace(entries.Where(e => e.Category == "Lights"), kinds, null);
        Check(narrowed.All(t => t.Count > 0) && OutlineTree.Violations(narrowed, true, places).Count == 0, "a narrowed place view shows only places with matches");
        Console.WriteLine("PASS: outline tree");
    }
}
