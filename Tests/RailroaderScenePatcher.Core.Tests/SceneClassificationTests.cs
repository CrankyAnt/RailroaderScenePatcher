// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// The generic rules that decide category, kind and place, checked against every listed object of the base map and
// against objects the base map does not have.
static class SceneClassificationTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Scene classification: " + message); }

    public static void Run() {
        var map = new BaseMapFixture();
        // Set to write the reviewed outcomes into the fixture, or to print the readout of every object.
        if (Environment.GetEnvironmentVariable("RSP_FIXTURE_WRITE") == "1") { map.WriteExpected(); map = new BaseMapFixture(); }
        if (Environment.GetEnvironmentVariable("RSP_FIXTURE_READOUT") == "1") Readout(map);
        TopObjectsAreTheInventorys(map);
        EveryListedObjectHasItsExpectedOutcome(map);
        NothingLandsInACatchAll(map);
        ChangesAreTheKnownOnes(map);
        PlaceChangesAreTheKnownOnes(map);
        OutcomesMatchWhatTheNamesSay(map);
        PlacesFallTogether(map);
        FictionalObjects();
        Console.WriteLine("PASS: scene classification");
    }

    static void Readout(BaseMapFixture map) {
        foreach (var place in map.Places.All.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine("PLACE " + place.Name + " [" + place.From + "] names: " + String.Join("; ", place.Names) + " points (" + place.PointsFrom + "): " + String.Join(" ", place.Points.Select(p => Math.Round(p[0]) + "," + Math.Round(p[1]))));
        foreach (var o in map.Objects.Where(o => o.Listed && !o.ChildObject)) {
            var named = map.Places.NamedInPath(o.Path).FirstOrDefault(); var nearest = map.Places.Nearest(o.At);
            Console.WriteLine("ROW " + o.Path + " | " + o.Family + " | today " + o.Today + " | " + map.Outcome(o) + " | named " + (named?.Name ?? "-") + " " + Math.Round(map.Places.Distance(named, o.At)) + " nearest " + nearest?.Name + " " + Math.Round(map.Places.Distance(nearest, o.At)));
        }
    }

    // The rules list the same top scene objects as the inventory of 7962df1 did with its category table: no group lost, none
    // added, and no part that is a top scene object.
    static void TopObjectsAreTheInventorys(BaseMapFixture map) {
        var expected = map.Objects.Where(o => o.TopSceneObject).Select(o => o.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var found = map.Verdicts.Where(v => v.Disposition == GroupDisposition.Group || v.Disposition == GroupDisposition.InspectionOnly).Select(v => v.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var missing = expected.Except(found).ToArray(); var added = found.Except(expected).ToArray();
        Check(missing.Length == 0 && added.Length == 0 && expected.Length == found.Length,
            "the rules list the top scene objects of the inventory (" + expected.Length + "), got " + found.Length + "; missing: " + String.Join(", ", missing) + "; added: " + String.Join(", ", added));
        var roots = new HashSet<string>(found);
        foreach (var part in map.Objects.Where(o => o.ChildObject)) {
            var above = part.Path; var held = false;
            while (!held && above.LastIndexOf('/') > 0) { above = above.Substring(0, above.LastIndexOf('/')); held = roots.Contains(above); }
            Check(held && !roots.Contains(part.Path), part.Path + " is a part below a top scene object");
        }
        var containers = map.Verdicts.Where(v => v.Disposition == GroupDisposition.GroupHolder).Select(v => v.Path).ToArray();
        Check(containers.SequenceEqual(new[] { "World/Large Scenery/Bryson/Bryson Yard" }), "Bryson Yard is the one scene object that only holds other groups of lamp posts (got " + String.Join(", ", containers) + ")");
        foreach (var o in map.Objects.Where(o => o.TopSceneObject)) {
            var verdict = map.Verdicts.First(v => v.Path == o.Path);
            Check(verdict.Category == null || verdict.Category == map.ClassOf(o).Category, o.Path + " has one category in the inventory and in the outliner (" + verdict.Category + ", " + map.ClassOf(o).Category + ")");
        }
    }

    static void EveryListedObjectHasItsExpectedOutcome(BaseMapFixture map) {
        var wrong = map.Objects.Where(o => o.Listed && !o.ChildObject && map.Outcome(o) != o.Expected).Select(o => o.Path + ": expected " + o.Expected + ", got " + map.Outcome(o)).ToArray();
        Check(wrong.Length == 0, wrong.Length + " listed objects do not have their expected category, kind and place: " + String.Join(" | ", wrong.Take(12)));
    }

    static void NothingLandsInACatchAll(BaseMapFixture map) {
        var lost = map.Objects.Where(o => o.Listed && !o.ChildObject && (!map.ClassOf(o).Recognised || map.Places.Of(o.Path, o.At) == null)).Select(o => o.Path).ToArray();
        Check(lost.Length == 0, "no listed object is left without a category or a place: " + String.Join(", ", lost));
    }

    // Every listed object whose category differs from the one it had before the generic rules, by the rule that
    // changes it. Before, a top scene object took its category from the category table of the 7962df1 inventory, and
    // every other object from its family and name.
    static readonly Dictionary<string, string[]> KnownChanges = new Dictionary<string, string[]> {
        // A loader that fills cars (RollingStock.CarContentSwapper) is an industry building.
        ["Other -> Industry building"] = new[] { "World/Large Scenery/Robinson Gap Mine/Coal Loader" },
        // A lake surface (LakePolygon) is water.
        ["Other -> Water"] = new[] { "World/Rivers/Fontana Lake (1)", "World/Rivers/Fontana Lake (2)" },
        // What stands along a road and is nothing else is road scenery.
        ["Other -> Road scenery"] = new[] { "World/Roads Barkers/1932 Chevy Truck LP" },
        // A track marker on a station building is that station.
        ["Track marker -> Station building"] = new[] { "World/Large Scenery/Hemingway/Hemingway flagstopstation" },
    };
    // Mileposts left Sign for a category of their own: every row of the Milepost family, by the fixture's family column.
    static Dictionary<string, string[]> Known(BaseMapFixture map) => KnownChanges.Concat(new[] { new KeyValuePair<string, string[]>("Sign -> Mileposts",
        map.Objects.Where(o => o.Listed && o.Family == "Milepost").Select(o => o.Path).ToArray()) }).ToDictionary(p => p.Key, p => p.Value);
    static void ChangesAreTheKnownOnes(BaseMapFixture map) {
        var changes = map.Objects.Where(o => o.Listed && !o.ChildObject).Select(o => new { o.Path, From = o.Today.Split(new[] { " / " }, StringSplitOptions.None)[0], To = map.ClassOf(o).Category })
            .Where(c => c.From != c.To).GroupBy(c => c.From + " -> " + c.To).ToDictionary(g => g.Key, g => g.Select(c => c.Path).ToArray());
        if (Environment.GetEnvironmentVariable("RSP_FIXTURE_READOUT") == "1") foreach (var change in changes) Console.WriteLine("CHANGE " + change.Key + " (" + change.Value.Length + "): " + String.Join(", ", change.Value));
        var known = Known(map);
        var unknown = changes.Where(c => !known.TryGetValue(c.Key, out var paths) || !paths.OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(c.Value.OrderBy(p => p, StringComparer.Ordinal))).Select(c => c.Key + " (" + c.Value.Length + "): " + String.Join(", ", c.Value.Take(5))).ToArray();
        Check(unknown.Length == 0 && changes.Count == known.Count, "the categories that change are the known ones; unexpected: " + String.Join(" | ", unknown));
    }

    // Every listed object whose place differs from the one it had when a place stood at the points of all its
    // sources, now that it stands where its most reliable source puts it (ScenePlaces.PositionOrder). Each change is
    // named by path and map position, since objects can share a path, with the place before and after and why.
    // Before, each of these objects stood nearest a point of its place that came from a less reliable source, such as
    // a Loc: board, a station map icon or the objects of a folder; the distances are to the nearest point of the
    // place before and after. A path's place counts within 3000 metres (ScenePlaces.NameReach).
    static readonly (string Why, string From, string To, (string Path, double X, double Z)[] Objects)[] KnownPlaceChanges = {
        // 54 to 338 metres from a point of Whittier before; now 501 to 803 metres from the EWH Interchange map label,
        // which is nearer than the Whittier map label.
        ("Whittier stands at its map label, farther from these objects than EWH Interchange", "Whittier", "EWH Interchange", new[] {
            ("Setups/Starter Equipment Setup/starter-ewh-wreck", 12582.0, 4885.0), ("Setups/Starter Equipment Setup/starter-wh-l", 12499.0, 4929.0),
            ("World/Mile Posts/T-56", 13718.0, 4249.0), ("World/Mile Posts/T-57", 12329.0, 4954.0) }),
        // 715 metres from a point of Alarka Jct before; now 1018 metres from the Robinson Branch Loc: board. Its path names no place.
        ("Alarka Jct stands at its map label, farther than the Robinson Branch Loc: board", "Alarka Jct", "Robinson Branch", new[] { ("World/Large Scenery/Robinson Gap Mine/Coal Loader", 1318.0, 2189.0) }),
        // The sign holder at the map origin, which has no row in the outliner, stands 24 km from Sylva, which its path
        // names: 1521 metres from a point of Brooks before; now 1688 metres from the Robinson Branch Loc: board.
        ("Brooks stands at its map label, farther than the Robinson Branch Loc: board", "Brooks", "Robinson Branch", new[] { ("World/Large Scenery/Sylva/Sylva Signs", 0.0, 0.0) }),
        // 918 metres from a point of Dillsboro before; now 1153 metres from a Sylva map label.
        ("Dillsboro stands at its map label, farther than Sylva", "Dillsboro", "Sylva", new[] { ("World/Mile Posts/T-47", 23406.0, -1036.0) }),
        // 1164 metres from a point of Barker's Creek before; now 1556 metres from the Cowee map label.
        ("Barker's Creek stands at its map label, farther than Cowee", "Barker's Creek", "Cowee", new[] { ("World/Mile Posts/T-50", 19455.0, -439.0) }),
        // 568 metres from the Walker Branch Loc: board before; Walker has no map label and now stands at its area. Now
        // 1325 metres from the Bryson map label.
        ("Walker stands at its area, farther than Bryson", "Walker", "Bryson", new[] { ("World/Mile Posts/T-63", 5873.0, 5706.0) }),
        // The milepost stood 809 metres from a point of Bryson; now 1293 metres from the Hemingway map label. The
        // Horseshoe speed signs between Bryson and Hemingway stood 2103 to 2367 metres from a point of Bryson, within
        // the reach of the name in their path; now 3017 to 3261 metres from the Bryson map label, beyond it, and 1284 to
        // 1583 metres from the Hemingway map label. The other Horseshoe Above sign stands 2752 metres from Bryson's
        // label and stays at Bryson.
        ("Bryson stands at its map label, farther than Hemingway", "Bryson", "Hemingway", new[] {
            ("World/Mile Posts/T-65", 3338.0, 4280.0), ("World/Signs/Speed - Bryson Horseshoe", 1861.0, 3766.0), ("World/Signs/Speed - Bryson Horseshoe", 1679.0, 3999.0),
            ("World/Signs/Speed - Bryson Horseshoe Above", 2005.0, 3922.0) }),
        // 2700 metres from a point of Andrews before; now 3459 metres from the Rhodo map label.
        ("Andrews stands at its map label, farther than Rhodo", "Andrews", "Rhodo", new[] { ("World/Mile Posts/T-96", -26223.0, -19308.0) }),
        // 2259 metres from a point of Wesser before; now 2390 metres from the Almond map label. Its path names no place.
        ("Wesser stands at its map label, farther than Almond", "Wesser", "Almond", new[] { ("World/Signs/Speed - Fontana South Curve", -6496.0, -3752.0) }),
    };
    // The place an object had when a place stood at the points of all its sources, by the same choice by path and
    // distance: a check of the snapshot, independent of ScenePlaces' position order.
    static string PreviousPlace(BaseMapFixture map, BaseMapObject o) {
        var points = new Dictionary<ScenePlace, List<double[]>>();
        var sources = map.MapData.Concat(map.Objects.Where(x => !x.ChildObject).SelectMany(x => ScenePlaces.SourcesOf(x.Path, x.At)));
        foreach (var g in sources.GroupBy(s => new { Tier = ScenePlaces.NameTier(s.Kind), Loc = s.Kind == PlaceSourceKind.LocBoard, s.Kind, Key = String.Join(" ", ScenePlaces.Tokens(s.Name)) })) {
            var place = map.Places.All.FirstOrDefault(p => p.Names.Any(n => String.Join(" ", ScenePlaces.Tokens(n)) == g.Key.Key));
            if (place == null) continue;
            if (!points.TryGetValue(place, out var list)) points[place] = list = new List<double[]>();
            // Before, the areas and passenger stops of one name were one source with one point at their middle.
            var at = (ScenePlaces.NameTier(g.Key.Kind) == 0 ? sources.Where(s => ScenePlaces.NameTier(s.Kind) == 0 && String.Join(" ", ScenePlaces.Tokens(s.Name)) == g.Key.Key) : g).Select(s => s.At).ToArray();
            if (g.Key.Loc) list.AddRange(at);
            else { var xs = at.Select(p => p[0]).OrderBy(v => v).ToArray(); var zs = at.Select(p => p[1]).OrderBy(v => v).ToArray(); double Mid(double[] v) => v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2; var point = new[] { Mid(xs), Mid(zs) }; if (!list.Any(p => p[0] == point[0] && p[1] == point[1])) list.Add(point); }
        }
        double Distance(ScenePlace p) => p == null || !points.TryGetValue(p, out var list) || list.Count == 0 ? Double.PositiveInfinity : list.Min(q => Math.Sqrt((q[0] - o.At[0]) * (q[0] - o.At[0]) + (q[1] - o.At[1]) * (q[1] - o.At[1])));
        var named = map.Places.NamedInPath(o.Path);
        var near = named.FirstOrDefault(p => Distance(p) <= ScenePlaces.Close) ?? named.Where(p => Distance(p) <= ScenePlaces.NameReach).OrderBy(Distance).FirstOrDefault();
        return (near ?? map.Places.All.OrderBy(Distance).First()).Name;
    }
    static void PlaceChangesAreTheKnownOnes(BaseMapFixture map) {
        var changed = map.Objects.Where(o => o.Listed && !o.ChildObject).Select(o => new { o, From = PreviousPlace(map, o), To = map.Places.Of(o.Path, o.At)?.Name }).Where(c => c.From != c.To)
            .Select(c => c.From + " -> " + c.To + ": " + c.o.Path + " @ " + c.o.At[0] + "," + c.o.At[1]).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var known = KnownPlaceChanges.SelectMany(k => k.Objects.Select(x => k.From + " -> " + k.To + ": " + x.Path + " @ " + x.X + "," + x.Z)).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Check(changed.SequenceEqual(known), "the places that change are the known ones; unexpected: " + String.Join(" | ", changed.Except(known)) + "; missing: " + String.Join(" | ", known.Except(changed)));
        // The snapshot names each changed place: the expected column holds the new place of each of these objects.
        foreach (var k in KnownPlaceChanges) foreach (var x in k.Objects) {
            var row = map.Objects.Single(o => o.Listed && o.Path == x.Path && o.At[0] == x.X && o.At[1] == x.Z);
            Check(row.Expected.EndsWith(" @ " + k.To, StringComparison.Ordinal), x.Path + " @ " + x.X + "," + x.Z + " is listed at " + k.To + " in the snapshot (" + k.Why + "), got " + row.Expected);
        }
    }

    // Expectations that do not come from the rules' own output: the kind of every bridge by its name, the kind of
    // every sign by the words of its name, the Loc: boards and the mileposts by their family, and the place of every
    // object that stands in a place folder and no farther than the rules' own reach from that place.
    static void OutcomesMatchWhatTheNamesSay(BaseMapFixture map) {
        var wrong = new List<string>();
        foreach (var o in map.Objects.Where(o => o.Listed && !o.ChildObject)) {
            var found = map.ClassOf(o); var name = o.Name;
            string kind = null;
            if (o.TopSceneObject && o.Today == "Bridge") kind = name.Contains("Road Bridge") ? "Road bridges" : "Rail bridges";
            else if (name.StartsWith("Loc: ", StringComparison.Ordinal)) kind = "Location signs";
            else if (o.Family == "Milepost") { if (found.Category != "Mileposts" || found.Kind != null) wrong.Add(o.Path + ": " + found + ", expected Mileposts without a kind"); continue; }
            else if (name.StartsWith("Whistle Sign", StringComparison.Ordinal)) kind = "Whistle signs";
            else if (name.StartsWith("Speed", StringComparison.Ordinal)) kind = "Speed signs";
            else if (name.StartsWith("Yard Limit", StringComparison.Ordinal)) kind = "Yard limits";
            if (kind != null && found.Kind != kind) wrong.Add(o.Path + ": kind " + found.Kind + ", expected " + kind);
            if (kind != null && found.Category != (kind.EndsWith("bridges", StringComparison.Ordinal) ? "Bridge" : "Sign")) wrong.Add(o.Path + ": category " + found.Category);
        }
        // The pavilions of Sylva and Connelly are industry buildings without a kind: there is no Pavilion kind.
        foreach (var path in new[] { "World/Large Scenery/Sylva/Sylva Culled", "World/Large Scenery/Whittier/Connelly Culled" }) {
            var found = map.ClassOf(map.Objects.Single(o => o.Path == path));
            if (found.Category != SceneObjectCategories.IndustryBuilding || found.Kind != null) wrong.Add(path + ": " + found + ", expected Industry building without a kind");
        }
        Check(!map.Objects.Where(o => o.Listed && !o.ChildObject).Any(o => map.ClassOf(o).Kind == "Pavilion"), "no listed object has the Pavilion kind");
        Check(wrong.Count == 0, "bridges and signs take the kind their names say: " + String.Join(" | ", wrong.Take(10)));
        // The towns of the map data: an object in a town's folder stands at that town, unless its own name or a folder
        // between names another place, or it stands far from the town.
        var towns = new HashSet<string>(map.MapData.Select(p => p.Name), StringComparer.Ordinal);
        var misplaced = map.Objects.Where(o => o.Listed && !o.ChildObject && o.Path.StartsWith("World/Large Scenery/", StringComparison.Ordinal)).Select(o => new { o, Parts = o.Path.Split('/') })
            .Where(x => x.Parts.Length == 4 && towns.Contains(x.Parts[2]) && !towns.Any(t => t != x.Parts[2] && x.Parts[3].Contains(t.Split(' ')[0])) && map.Places.Distance(map.Places.Named(x.Parts[2]), x.o.At) <= ScenePlaces.Close)
            .Where(x => map.Places.Of(x.o.Path, x.o.At)?.Name != x.Parts[2]).Select(x => x.o.Path + " @ " + map.Places.Of(x.o.Path, x.o.At)?.Name).ToArray();
        Check(misplaced.Length == 0, "an object directly in a town's folder that stands in that town is listed at that town: " + String.Join(" | ", misplaced.Take(10)));
    }

    static void PlacesFallTogether(BaseMapFixture map) {
        string Name(string text) => map.Places.Named(text)?.Name;
        Check(Name("Connelly") == "Connelly Creek" && Name("Loc: Connelly") == "Connelly Creek", "Connelly is Connelly Creek, the name of the game's map data (got " + Name("Connelly") + ")");
        Check(Name("Barkers") == "Barker's Creek" && Name("Barker's") == "Barker's Creek", "Barkers and Barker's are Barker's Creek (got " + Name("Barkers") + ")");
        Check(Name("Sylva Int") == "Sylva" && Name("E Sylva") == "Sylva", "Sylva Int and E Sylva are Sylva");
        Check(Name("E Whittier") == "Whittier" && Name("Whittier E") == "Whittier" && Name("Whittier W") == "Whittier", "E Whittier, Whittier E and Whittier W are Whittier");
        Check(Name("Alarka Jct") == "Alarka Jct" && Name("Alarka") == "Alarka", "Alarka and Alarka Jct, both in the map data, stay two places");
        var names = map.Places.All.Select(p => p.Name).ToArray();
        Check(names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length, "every place has one node");
        foreach (var a in map.Places.All) foreach (var b in map.Places.All) {
            if (ReferenceEquals(a, b) || ScenePlaces.NameTier(a.From) == ScenePlaces.NameTier(b.From)) continue;
            var at = ScenePlaces.Tokens(a.Name); var bt = ScenePlaces.Tokens(b.Name);
            Check(!(at.All(bt.Contains) && ScenePlaces.NameTier(a.From) < ScenePlaces.NameTier(b.From)) || map.Places.All.Count(p => ScenePlaces.NameTier(p.From) < ScenePlaces.NameTier(b.From) && ScenePlaces.Tokens(p.Name).All(bt.Contains)) > 1, b.Name + " holds the words of " + a.Name + " and is a place of its own only when two places fit");
        }
    }

    // Objects the base map does not have, built the way the game and Unity build such objects: the same path forms,
    // components and names. Each takes its category, kind and place from the rules, with no rule written for it.
    static BaseMapObject Fiction(string path, string family, double x, double z, string expected, params string[] components) =>
        new BaseMapObject { Listed = true, Path = path, Family = family, Structure = family == SceneObjectRules.Family ? "scene-object-v1" : null, Components = components, At = new[] { x, z }, Today = "", Expected = expected };
    static BaseMapObject Below(string path, params string[] components) =>
        new BaseMapObject { Listed = true, Path = path, Family = SceneObjectRules.ChildFamily, Structure = BaseMapObject.Part, Components = components, At = new[] { 0.0, 0 }, Today = "", Expected = "" };
    const string Baked = SceneObjectRules.Family, Culler = "Helpers.Culling.RendererCuller", Mesh = "UnityEngine.MeshRenderer", Marker = "Track.TrackMarker", Relative = "Track.TrackRelativePosition";
    static void FictionalObjects() {
        var fiction = Fictions();
        var map = new BaseMapFixture(fiction);
        var listed = fiction.Where(o => !o.ChildObject).ToArray();
        Check(listed.Length >= 10 && listed.Length <= 30, "the fictional objects are a handful (" + listed.Length + ")");
        CheckFictions(map, listed);
    }
    // The fictional objects, which the Supported edits test lists as well.
    internal static BaseMapObject[] Fictions() => new[] {
            // A new station building in an existing town.
            Fiction("World/Large Scenery/Hewitt/Hewitt Depot", Baked, -13800, -8190, "Station building / Depots @ Hewitt"),
            Below("World/Large Scenery/Hewitt/Hewitt Depot/Roof", Mesh),
            // A flag stop station in a town that has no folder yet: the folder is the place.
            Fiction("World/Large Scenery/Judson/Judson flagstopstation", Baked, -9000, -3000, "Station building / Flag stop stations @ Judson"),
            Below("World/Large Scenery/Judson/Judson flagstopstation/Station", Mesh),
            // A new lamp post and a lamp of a new make.
            Fiction("World/Large Scenery/Wilmot/Tall Post Shade (4)", Baked, 16520, 2310, "Lights / Tall Post Shade @ Wilmot", Culler),
            Below("World/Large Scenery/Wilmot/Tall Post Shade (4)/Bulb", "UnityEngine.Light"),
            Fiction("World/Large Scenery/Ela/Gooseneck Lamp", Baked, 9440, 7450, "Lights / Gooseneck Lamp @ Ela", Culler),
            Below("World/Large Scenery/Ela/Gooseneck Lamp/Bulb", "UnityEngine.Light"),
            // A new kind of sign in the game's sign container, a sign named after a place, and a location board for a new place.
            Fiction("World/Signs/Whistle Post - Judson", "Generic track marker", -9050, -3020, "Sign / Whistle signs @ Judson", Marker, Relative, Culler),
            Fiction("World/Signs/Derail Sign Hewitt", "Generic track marker", -13790, -8200, "Sign / Other signs @ Hewitt", Marker, Relative, Culler),
            Fiction("World/Signs/Loc: Judson", "Location sign", -9100, -3100, "Sign / Location signs @ Judson", Culler, "Map.Runtime.MaskComponents.CircleMapMask"),
            Fiction("World/Signs/Speed - Tunnel Curve", "Generic track marker", 16400, 2250, "Sign / Speed signs @ Wilmot", Marker, Relative, Culler),
            // A milepost of a new line.
            Fiction("World/Mile Posts/M-12", "Milepost", 2000, -200, "Mileposts @ Cochran", Culler, Marker, Relative, "Helpers.MilePost"),
            // A new unprotected crossing in a Roads folder of a new place, and a protected one.
            Fiction("World/Roads Judson/Crossing Unprotected - Mill Road/Sign-Crossing0 (1)", Baked, -9010, -3010, "Grade crossing @ Judson", Culler),
            Fiction("World/Roads Judson/Crossing Unprotected - Mill Road/Crossing Pad", "Crossing pad marker", -9010, -3012, "Grade crossing @ Judson", Marker, Relative, "Track.TrackCurveMeshBuilder"),
            Fiction("World/Roads Hewitt/Grade Crossing - Main St", "Grade crossing controller", -13810, -8180, "Grade crossing @ Hewitt", "Track.Crossings.GradeCrossingController"),
            Fiction("World/Roads Hewitt/Grade Crossing - Main St/Signal 1", "Crossing mast", -13811, -8181, "Grade crossing @ Hewitt", "Track.KeyValueLampMaterialController"),
            // A new storage pile at a new place, and an industry building there.
            Fiction("World/Large Scenery/Judson/Judson Pulpwood", Baked, -9020, -2990, "Storage display / Storage piles @ Judson"),
            Below("World/Large Scenery/Judson/Judson Pulpwood/Pulpwood Pile P1", "Scenery.Pile"),
            Fiction("World/Large Scenery/Judson/Judson Lumber Mill", Baked, -9030, -2980, "Industry building / Lumber Mill @ Judson"),
            Below("World/Large Scenery/Judson/Judson Lumber Mill/Shed", Mesh),
            // A building named after its place alone is what it holds.
            Fiction("World/Large Scenery/Judson/Judson Culled", Baked, -9040, -2970, "Industry building @ Judson", "Helpers.SimpleCuller"),
            Below("World/Large Scenery/Judson/Judson Culled/Pavilion", Mesh), Below("World/Large Scenery/Judson/Judson Culled/Pavilion (1)", Mesh),
            // A road bridge, a rail bridge and a railroad service building of a new make.
            Fiction("World/Roads Hewitt/Hewitt Road Bridge 30m", Baked, -13700, -8100, "Bridge / Road bridges @ Hewitt", Culler, "Map.Runtime.MaskComponents.RectangleMapMask"),
            Below("World/Roads Hewitt/Hewitt Road Bridge 30m/Deck 1", Mesh),
            Fiction("World/Large Scenery/Judson/Timber Trestle", Baked, -9200, -3200, "Bridge / Rail bridges @ Judson", Mesh),
            Fiction("World/Large Scenery/Hewitt/Hewitt Water Tank", Baked, -13820, -8200, "Railroad service building / Water Tank @ Hewitt", Mesh),
            // A lake of another name, a vehicle along a road, a spawn point and a station map icon.
            Fiction("World/Rivers/Nantahala Lake", Baked, -15000, -10000, "Water / Lake surfaces @ Nantahala", "LakePolygon", Mesh),
            Fiction("World/Roads Hewitt/1940 Ford Pickup", Baked, -13750, -8150, "Road scenery / 1940 Ford Pickup @ Hewitt"),
            Below("World/Roads Hewitt/1940 Ford Pickup/Body", Mesh),
            Fiction("World/POIs/Judson", "Spawn point", -9000, -3000, "Spawn point @ Judson", "Character.SpawnPoint"),
            Fiction("World/Map Labels/Station Icons/Judson Station", "Station map icon", -9005, -3005, "Station map icon @ Judson", "UI.Map.MapIcon"),
        };
    static void CheckFictions(BaseMapFixture map, BaseMapObject[] listed) {
        var wrong = listed.Where(o => map.Outcome(o) != o.Expected).Select(o => o.Path + ": expected " + o.Expected + ", got " + map.Outcome(o)).ToArray();
        Check(wrong.Length == 0, "a fictional object built the game's way takes its category, kind and place from the rules: " + String.Join(" | ", wrong));
        // Each fictional top scene object is a top scene object for the inventory too, with the same category.
        foreach (var o in listed.Where(o => o.TopSceneObject)) {
            var verdict = map.Verdicts.FirstOrDefault(v => v.Path == o.Path);
            Check(verdict != null && verdict.Disposition == GroupDisposition.Group && verdict.Category == map.ClassOf(o).Category, o.Path + " is a listed top scene object under " + map.ClassOf(o).Category + " (got " + verdict?.Disposition + " " + verdict?.Category + ")");
        }
        if (Environment.GetEnvironmentVariable("RSP_FIXTURE_READOUT") == "1") foreach (var o in listed) Console.WriteLine("FICTION " + o.Path + " | " + map.Outcome(o));
    }
}
