// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Objects that share a hierarchy path and component: a target names one of them by a key, the game's marker ID where
// the object has one, else its map position within a small tolerance. A target whose path is unique holds no key.
static class SharedPathTargetTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run() {
        PositionKeyNamesOneObject();
        PositionKeyMatchesWithinToleranceAndNeverTheNearest();
        MarkerIdKeyNamesOneObject();
        KeyMatchingSeveralObjectsFails();
        UnkeyedTargetsKeepTheirOutcomes();
        KeyIsValidated();
        KeyedObjectMovedByItsModStillResolves();
        EditingWritesTheKeyOnlyForSharedPaths();
        BaseMapSharedPathsHaveKeysAndNumbers();
        OutlinerNumbersObjectsThatShareAPath();
        Console.WriteLine("PASS: targets of objects that share a hierarchy path");
    }

    // A scene whose adapter fake supplies each object's key as the game gave it: its marker ID, or null, and its map
    // x/z.
    sealed class KeyedScene : Scene, ITargetKeyScene {
        public readonly Dictionary<object, TargetKey> Keys = new Dictionary<object, TargetKey>();
        public TargetKey KeyOf(object target) => Keys.TryGetValue(target, out var key) ? key : null;
        public Box Add(string path, double x, double z, string markerId = null) {
            var box = new Box { Position = new[] { x - Origin, 5, z }, Rotation = new[] { 0.0, 0, 0, 1 } };
            if (!Objects.TryGetValue(path, out var at)) Objects[path] = at = new List<object>();
            at.Add(box);
            Keys[box] = new TargetKey(markerId, new[] { x, z });
            return box;
        }
    }
    const string Shared = "World/Signs/Speed - Alarka Branch", Unique = "World/Signs/Loc: Wilmot";
    static string N(double v) => v.ToString(CultureInfo.InvariantCulture);
    static string Item(string id, string path, string key = "", double to = 9000) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"" + key + "},\"placement\":{\"mode\":\"fixed\",\"position\":[" + N(to) + ",2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
    static string At(double x, double z) => ",\"mapPosition\":[" + N(x) + "," + N(z) + "]";
    static string Marker(string id) => ",\"markerId\":\"" + id + "\"";
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static KeyedScene Signs(out Box first, out Box second) {
        var scene = new KeyedScene();
        first = scene.Add(Shared, 2286.12, -1678.4);
        second = scene.Add(Shared, 1859.5, 775.25);
        return scene;
    }

    // Two signs at one path: the map position of one names that sign alone, and the other keeps its place.
    static void PositionKeyNamesOneObject() {
        var scene = Signs(out var first, out var second);
        var before = second.Position.ToArray();
        var result = new PatchEngine(scene).Apply(Doc(Item("one", Shared, At(1859.5, 775.25))));
        Check(result.Outcomes["one"].Applied && second.Position[0] == 9000 - scene.Origin && first.Position[0] == 2286.12 - scene.Origin,
            "the map position names the second sign alone: " + result.Outcomes["one"].Error);
        Check(!before.SequenceEqual(second.Position) && scene.MutationCalls == 1, "only that sign changed");
    }

    // A map position matches within the tolerance on x and on z; one just outside it matches no object, even with
    // a sign a few metres away, and the modification fails with the missing-target reason.
    static void PositionKeyMatchesWithinToleranceAndNeverTheNearest() {
        var tolerance = TargetIdentity.MapPositionTolerance;
        Check(tolerance > 0 && tolerance <= 0.1, "the tolerance is small: " + tolerance);
        var scene = Signs(out var first, out var second);
        var near = new PatchEngine(scene).Apply(Doc(Item("near", Shared, At(1859.5 + tolerance * 0.9, 775.25 - tolerance * 0.9)))).Outcomes["near"];
        Check(near.Applied && second.Position[0] == 9000 - scene.Origin, "a position within the tolerance matches: " + near.Error);
        scene = Signs(out first, out second);
        var mutations = scene.MutationCalls;
        var off = new PatchEngine(scene).Apply(Doc(Item("off", Shared, At(1859.5 + tolerance * 1.5, 775.25)), Item("far", Shared, At(1862, 779)))).Outcomes;
        Check(!off["off"].Applied && off["off"].Error.StartsWith(PatchEngine.TargetMissing, StringComparison.Ordinal) && off["off"].ResolvedTarget == null, "a position just outside the tolerance matches no object: " + off["off"].Error);
        Check(!off["far"].Applied && off["far"].Error.StartsWith(PatchEngine.TargetMissing, StringComparison.Ordinal), "a position near a sign is never resolved to the nearest sign: " + off["far"].Error);
        Check(scene.MutationCalls == mutations && second.Position[0] == 1859.5 - scene.Origin, "the failed keys change nothing");
    }

    // Two markers at one path, each with its own marker ID: the marker ID names one of them.
    static void MarkerIdKeyNamesOneObject() {
        var scene = new KeyedScene();
        var west = scene.Add(Shared, 2286, -1678, "Mab1");
        var east = scene.Add(Shared, 1859, 775, "Mab2");
        var result = new PatchEngine(scene).Apply(Doc(Item("west", Shared, Marker("Mab1")), Item("none", Shared, Marker("Mzzz"))));
        Check(result.Outcomes["west"].Applied && west.Position[0] == 9000 - scene.Origin && east.Position[0] == 1859 - scene.Origin, "the marker ID names the first marker alone: " + result.Outcomes["west"].Error);
        Check(!result.Outcomes["none"].Applied && result.Outcomes["none"].Error.StartsWith(PatchEngine.TargetMissing, StringComparison.Ordinal), "an unknown marker ID matches no object: " + result.Outcomes["none"].Error);
        // A marker ID never stands in for a position: the position of a marker is no key that its marker ID replaces.
        var byPosition = new PatchEngine(scene).Apply(Doc(Item("east", Shared, At(1859, 775)))).Outcomes["east"];
        Check(byPosition.Applied, "a map position still names a marker exactly: " + byPosition.Error);
    }

    // A key that two objects at the path share fails the modification with the reason, and the others still apply.
    static void KeyMatchingSeveralObjectsFails() {
        var scene = new KeyedScene();
        var a = scene.Add(Shared, 100, 100, "Mdup");
        var b = scene.Add(Shared, 900, 900, "Mdup");
        var c = scene.Add(Unique, 50, 50);
        var result = new PatchEngine(scene).Apply(Doc(Item("dup", Shared, Marker("Mdup")), Item("other", Unique)));
        Check(!result.Outcomes["dup"].Applied && result.Outcomes["dup"].Error.StartsWith(PatchEngine.TargetAmbiguous, StringComparison.Ordinal) && result.Outcomes["dup"].ResolvedTarget == null,
            "a marker ID that two objects hold fails with the ambiguous-target reason: " + result.Outcomes["dup"].Error);
        Check(a.Position[0] == 100 - scene.Origin && b.Position[0] == 900 - scene.Origin && result.Outcomes["other"].Applied && c.Position[0] == 9000 - scene.Origin,
            "the failed modification changes nothing and the other applies");
        var twin = new KeyedScene();
        twin.Add(Shared, 100, 100); twin.Add(Shared, 100.01, 100);
        var close = new PatchEngine(twin).Apply(Doc(Item("close", Shared, At(100, 100)))).Outcomes["close"];
        Check(!close.Applied && close.Error.StartsWith(PatchEngine.TargetAmbiguous, StringComparison.Ordinal), "a map position that two objects match within the tolerance fails: " + close.Error);
    }

    // A scene patch file written before keys existed: a target with a unique path applies as before, and a target
    // without a key at a shared path still fails as ambiguous. The text of the file stays as it was.
    static void UnkeyedTargetsKeepTheirOutcomes() {
        var scene = Signs(out var first, out var second);
        var wilmot = scene.Add(Unique, 16377, 2296);
        var text = Doc(Item("unique", Unique), Item("shared", Shared));
        var result = new PatchEngine(scene).Apply(text);
        Check(result.Outcomes["unique"].Applied && wilmot.Position[0] == 9000 - scene.Origin, "a unique path without a key applies: " + result.Outcomes["unique"].Error);
        Check(!result.Outcomes["shared"].Applied && result.Outcomes["shared"].Error.StartsWith(PatchEngine.TargetAmbiguous, StringComparison.Ordinal), "a shared path without a key stays ambiguous: " + result.Outcomes["shared"].Error);
        var plain = new Scene(); plain.Objects[Unique] = new List<object> { new Box { Position = new[] { 1.0, 2, 3 }, Rotation = new[] { 0.0, 0, 0, 1 } } };
        Check(new PatchEngine(plain).Apply(text).Outcomes["unique"].Applied, "a scene without keys applies an unkeyed target as before");
        var keyed = new PatchEngine(plain).Apply(Doc(Item("keyed", Unique, At(1, 3)))).Outcomes["keyed"];
        Check(!keyed.Applied && keyed.Error.StartsWith(PatchEngine.TargetMissing, StringComparison.Ordinal), "a scene that supplies no keys matches no keyed target: " + keyed.Error);
    }

    // A key is a non-empty marker ID or two finite numbers, never both, and never beside padMarkerId.
    static void KeyIsValidated() {
        var scene = Signs(out _, out _);
        foreach (var (name, key) in new[] {
            ("empty marker", ",\"markerId\":\"\""), ("number marker", ",\"markerId\":5"), ("one number", ",\"mapPosition\":[1]"), ("three numbers", ",\"mapPosition\":[1,2,3]"),
            ("text", ",\"mapPosition\":[\"1\",2]"), ("both", Marker("Mab1") + At(1, 2)), ("with pad", At(1, 2) + ",\"padMarkerId\":\"Mbkd\"") }) {
            var outcome = new PatchEngine(scene).Apply(Doc(Item("bad", Shared, key))).Outcomes["bad"];
            Check(!outcome.Applied && outcome.Error.Contains("Invalid or unsupported target"), name + " is an invalid target: " + outcome.Error);
        }
        Check(scene.MutationCalls == 0, "invalid keys change nothing");
    }

    // The key is the object's key as the game gave it, so a sign that its modification moved still resolves by the
    // map position it had before, for the next edit and for removal.
    static void KeyedObjectMovedByItsModStillResolves() {
        var scene = Signs(out var first, out var second);
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(scene), mapEditor, store); mapEditor.Session = session;
        var identity = new TargetIdentity("s", Shared, "c") { MapPosition = new[] { 1859.5, 775.25 } };
        Check(session.Edit(identity, new Placement(new[] { 5000.0, 2, 3 }, new[] { 0.0, 0, 0, 1 }), null).Applied && second.Position[0] == 5000 - scene.Origin, "a keyed edit moves the sign");
        Check(session.Edit(identity, new Placement(new[] { 6000.0, 2, 3 }, new[] { 0.0, 0, 0, 1 }), null).Applied && second.Position[0] == 6000 - scene.Origin, "a second edit finds the moved sign by its key");
        var target = (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"].Single().First["target"];
        Check(target.Properties().Select(p => p.Name).SequenceEqual(new[] { "scene", "path", "component", "mapPosition" }) && (double)target["mapPosition"][0] == 1859.5 && (double)target["mapPosition"][1] == 775.25,
            "the scene patch file names the sign by its path and map position: " + target);
        Check(session.Remove(identity).Applied && second.Position[0] == 1859.5 - scene.Origin && first.Position[0] == 2286.12 - scene.Origin, "Remove modification returns the sign and leaves the other");
    }

    // The key that editing writes for an object: none for a unique path, the marker ID where the object has one at a
    // shared path, else its map position, rounded to the centimetre.
    static void EditingWritesTheKeyOnlyForSharedPaths() {
        var unique = TargetKeys.For("s", Unique, "c", null, new[] { 16377.0, 2296.0 }, 1);
        Check(unique.MarkerId == null && unique.MapPosition == null, "a unique path holds no key");
        var marker = TargetKeys.For("s", Shared, "c", "Mab1", new[] { 2286.0, -1678.0 }, 2);
        Check(marker.MarkerId == "Mab1" && marker.MapPosition == null, "a shared path holds the marker ID where the game has one");
        var board = TargetKeys.For("s", "World/Signs/Loc: Andrews", "c", null, new[] { -30354.4567, -20284.0049 }, 2);
        Check(board.MarkerId == null && board.MapPosition.SequenceEqual(new[] { -30354.46, -20284.0 }), "a shared path without a marker ID holds the map position: " + String.Join(",", board.MapPosition));
    }

    // The 78 listed base-map objects that share a hierarchy path and component with another listed object: their key
    // kind and their sequence number. A row reads "path @ x,z" and its number; the numbers are a reviewed snapshot of
    // the rule's output, and the Alarka branch, Andrews and Sylva numbers are checked on their own below.
    static void BaseMapSharedPathsHaveKeysAndNumbers() {
        var map = new BaseMapFixture();
        var listed = map.Objects.Where(o => o.Listed).ToArray();
        var shared = listed.GroupBy(o => o.Path + "\n" + o.Family).Where(g => g.Count() > 1).ToArray();
        Check(shared.Sum(g => g.Count()) == 78, "78 listed objects share a path and component: " + shared.Sum(g => g.Count()));
        var kinds = shared.SelectMany(g => g).GroupBy(o => o.Family).ToDictionary(g => g.Key, g => g.Count());
        Check(kinds["Location sign"] == 47 && kinds["Generic track marker"] == 22 && kinds["Crossing pad marker"] == 3 && kinds[SceneObjectRules.Family] == 6, "47 location signs, 22 Generic track markers, 3 pads and 6 crossbucks");
        // The key: a marker ID for an object with a track marker, a pad marker ID for a crossbuck, else the map position.
        foreach (var o in shared.SelectMany(g => g)) {
            var key = TargetKeys.KindOf(o.Components, o.Family == SceneObjectRules.Family);
            var expected = o.Family == "Location sign" ? TargetKeyKind.MapPosition : o.Family == SceneObjectRules.Family ? TargetKeyKind.PadMarkerId : TargetKeyKind.MarkerId;
            Check(key == expected, o.Path + " is keyed by " + expected + ", not " + key);
        }
        var actual = new List<string>();
        foreach (var g in shared.OrderBy(g => g.Key, StringComparer.Ordinal)) {
            var objects = g.ToArray();
            var numbers = SharedPathSequence.Numbers(objects.Select(o => o.At).ToArray(), map.Places);
            for (var i = 0; i < objects.Length; i++) actual.Add(objects[i].Path + " @ " + N(objects[i].At[0]) + "," + N(objects[i].At[1]) + " = " + numbers[i]);
        }
        var wrong = actual.Except(ExpectedNumbers).Concat(ExpectedNumbers.Except(actual).Select(e => "missing " + e)).ToArray();
        Check(wrong.Length == 0, "sequence numbers differ:\n" + String.Join("\n", actual));
        string Number(string path, double x) => actual.Single(a => a.StartsWith(path + " @ " + N(x) + ",", StringComparison.Ordinal)).Split('=').Last().Trim();
        Check(Number(Shared, 2286) == "1" && Number(Shared, 1859) == "2", "the Alarka branch numbers run from the Alarka end to Alarka Jct");
        Check(Number("World/Signs/Loc: Cochran", 2250) == "1" && Number("World/Signs/Loc: Cochran", 1881) == "2", "Cochran's boards run towards Alarka Jct");
        Check(Number("World/Signs/Loc: Andrews", -30354) == "1" && Number("World/Signs/Loc: Andrews", -28881) == "2", "the main line runs west to east from Andrews");
        Check(Number("World/Signs/Loc: Sylva", 24344) == "1" && Number("World/Signs/Loc: E Sylva", 25418) == "2", "the main line runs towards Sylva in the east, past its end too");
    }
    // The outliner shows the game's name with the sequence number for each object that shares its path and component;
    // the numbers follow where the game placed the objects, also after one of them moved, and the object keeps its path.
    sealed class Listing : IInspectionScene {
        public List<SceneObservation> Objects = new List<SceneObservation>();
        public IEnumerable<string> MapIds => new[] { "map" };
        public IEnumerable<SceneObservation> Discover() => Objects;
    }
    sealed class Places : RailroaderScenePatcher.IOutlinerScene {
        public string WorldKey => "shared paths";
        public RailroaderScenePatcher.ObjectFacts Facts(SceneObservation entry) => new RailroaderScenePatcher.ObjectFacts();
        public double[] Bounds(SceneObservation entry) => null;
        public double[] Displacement(SceneObservation entry) => null;
        public IEnumerable<PlaceSource> PlaceSources(IReadOnlyList<SceneObservation> entries, List<string> notes) => new[] {
            new PlaceSource(PlaceSourceKind.MapDataArea, "Alarka", new[] { 4144.0, -3108 }), new PlaceSource(PlaceSourceKind.MapDataArea, "Cochran", new[] { 2084.0, -288 }),
            new PlaceSource(PlaceSourceKind.MapDataArea, "Alarka Jct", new[] { 1604.0, 1659 }), new PlaceSource(PlaceSourceKind.MapDataArea, "McClain", new[] { -6660.0, -176 }),
            new PlaceSource(PlaceSourceKind.MapDataArea, "Hemingway", new[] { 2833.0, 3075 }), new PlaceSource(PlaceSourceKind.MapDataArea, "Wilmot", new[] { 16507.0, 2281 }) };
    }
    static SceneObservation Marker(string path, double x, double z, double[] game = null, string markerId = null) =>
        new SceneObservation(new object(), new TargetIdentity("s", path, TargetKeys.MarkerComponent, TrackMarkerScene.Structure) { MarkerId = markerId }, "Generic track marker", true, new[] { x, 600, z }, new double[] { 0, 0, 0, 1 },
            new string[0], new string[0], new string[0], "Placement", gamePosition: game ?? new[] { x, z });
    static void OutlinerNumbersObjectsThatShareAPath() {
        var listing = new Listing();
        // The Alarka end sign moved by this mod far north: its number stays where the game placed it.
        var alarkaEnd = Marker(Shared, 1800, 9000, new[] { 2286.0, -1678 }, "Mab1");
        var junction = Marker(Shared, 1859, 775, markerId: "Mab2");
        var wilmot = new SceneObservation(new object(), new TargetIdentity("s", Unique, "Helpers.Culling.RendererCuller"), "Location sign", true, new[] { 16377.0, 600, 2296 }, new double[] { 0, 0, 0, 1 },
            new string[0], new string[0], new string[0], "Placement");
        listing.Objects.AddRange(new[] { junction, alarkaEnd, wilmot });
        var inventory = new SceneInventory(listing); inventory.Refresh();
        RailroaderScenePatcher.OutlinerGrouping.InvalidatePlaces();
        RailroaderScenePatcher.OutlinerGrouping.Scene = new Places();
        RailroaderScenePatcher.OutlinerGrouping.LogPending = false;
        try {
            RailroaderScenePatcher.OutlinerGrouping.ByPlace = false;
            var rows = RailroaderScenePatcher.OutlinerGrouping.Rows(inventory, "", RailroaderScenePatcher.OutlinerFilter.All, null, null);
            var tree = RailroaderScenePatcher.OutlinerGrouping.Build(rows, inventory.Entries, "", false, null);
            var labels = new Dictionary<SceneObservation, string>();
            void Collect(IEnumerable<RailroaderScenePatcher.InventoryPresentationState.OutlineNode> nodes) { foreach (var n in nodes) { if (n.Row != null) labels[n.Row] = n.Label; Collect(n.Children); } }
            Collect(tree.Tops);
            Check(labels[alarkaEnd] == "Speed - Alarka Branch (1)" && labels[junction] == "Speed - Alarka Branch (2)", "the Alarka branch signs are numbered from the Alarka end: " + labels[alarkaEnd] + " / " + labels[junction]);
            Check(labels[wilmot] == "Loc: Wilmot", "an object with a path of its own keeps the game's name: " + labels[wilmot]);
            Check(RailroaderScenePatcher.InventoryPresentationState.Name(alarkaEnd) == "Speed - Alarka Branch (1)", "the window names the object with its number too");
            Check(alarkaEnd.Identity.Path == Shared && alarkaEnd.DisplayName == "Speed - Alarka Branch", "the object keeps the game's name and path");
        } finally { RailroaderScenePatcher.OutlinerGrouping.Scene = null; RailroaderScenePatcher.OutlinerGrouping.InvalidatePlaces(); }
    }
    static readonly string[] ExpectedNumbers = {
        "World/Large Scenery/Sylva/Speed Sign Sylva Wye @ 25091,-911 = 1",
        "World/Large Scenery/Sylva/Speed Sign Sylva Wye @ 25303,-499 = 2",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Crossing Pad @ 6676,5474 = 2",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Crossing Pad @ 6924,5505 = 3",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Crossing Pad @ 5350,5747 = 1",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (1) @ 5370,5743 = 1",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (1) @ 6708,5473 = 2",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (1) @ 6938,5505 = 3",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (2) @ 5332,5749 = 1",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (2) @ 6659,5472 = 2",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Sign-Crossing0 (2) @ 6911,5505 = 3",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Whistle Sign E @ 6913,5504 = 2",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Whistle Sign E @ 7179,5582 = 3",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Whistle Sign E @ 5680,5737 = 1",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Whistle Sign W @ 6383,5651 = 2",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Whistle Sign W @ 6697,5463 = 3",
        "World/Roads Bryson/Old River Rd/Crossing Unprotected/Whistle Sign W @ 5156,5700 = 1",
        "World/Signs/Loc: Alarka Jct @ 1837,1060 = 2",
        "World/Signs/Loc: Alarka Jct @ 1765,1591 = 3",
        "World/Signs/Loc: Alarka Jct @ 1475,1196 = 1",
        "World/Signs/Loc: Almond @ -6440,-1058 = 2",
        "World/Signs/Loc: Almond @ -6270,-1433 = 1",
        "World/Signs/Loc: Andrews @ -30354,-20284 = 1",
        "World/Signs/Loc: Andrews @ -28881,-19781 = 2",
        "World/Signs/Loc: Barker's @ 18457,160 = 2",
        "World/Signs/Loc: Barker's @ 17794,1188 = 1",
        "World/Signs/Loc: Brooks @ -1252,864 = 2",
        "World/Signs/Loc: Brooks @ -1984,843 = 1",
        "World/Signs/Loc: Bryson @ 3874,4886 = 1",
        "World/Signs/Loc: Bryson @ 5115,5681 = 2",
        "World/Signs/Loc: Cochran @ 2250,-474 = 1",
        "World/Signs/Loc: Cochran @ 1881,-81 = 2",
        "World/Signs/Loc: Cowee @ 20710,-839 = 1",
        "World/Signs/Loc: Cowee @ 21011,-1353 = 2",
        "World/Signs/Loc: Dillsboro @ 21146,-1508 = 1",
        "World/Signs/Loc: Dillsboro @ 22527,-1302 = 2",
        "World/Signs/Loc: E Sylva @ 25136,-884 = 1",
        "World/Signs/Loc: E Sylva @ 25418,-81 = 2",
        "World/Signs/Loc: E Whittier @ 12365,4914 = 1",
        "World/Signs/Loc: E Whittier @ 13381,4280 = 2",
        "World/Signs/Loc: Ela @ 9617,7387 = 2",
        "World/Signs/Loc: Ela @ 9076,7584 = 1",
        "World/Signs/Loc: Gov Island @ 7501,5648 = 1",
        "World/Signs/Loc: Gov Island @ 8103,6021 = 2",
        "World/Signs/Loc: Hemingway @ 2511,2745 = 1",
        "World/Signs/Loc: Hemingway @ 2848,3225 = 2",
        "World/Signs/Loc: Hewitt @ -13394,-7875 = 2",
        "World/Signs/Loc: Hewitt @ -14162,-8501 = 1",
        "World/Signs/Loc: McClain @ -6374,228 = 2",
        "World/Signs/Loc: McClain @ -6372,-514 = 1",
        "World/Signs/Loc: Nantahala @ -15246,-10182 = 2",
        "World/Signs/Loc: Nantahala @ -16016,-10959 = 1",
        "World/Signs/Loc: Rhodo @ -22717,-17666 = 2",
        "World/Signs/Loc: Rhodo @ -23099,-18138 = 1",
        "World/Signs/Loc: Sylva @ 24756,-931 = 2",
        "World/Signs/Loc: Sylva @ 24344,-924 = 1",
        "World/Signs/Loc: Thomas Valley @ 15717,2327 = 2",
        "World/Signs/Loc: Thomas Valley @ 15210,2831 = 1",
        "World/Signs/Loc: Topton @ -19228,-15623 = 1",
        "World/Signs/Loc: Topton @ -18928,-15152 = 2",
        "World/Signs/Loc: Wesser @ -8840,-5592 = 1",
        "World/Signs/Loc: Wesser @ -8371,-5012 = 2",
        "World/Signs/Loc: Wilmot @ 16377,2296 = 1",
        "World/Signs/Loc: Wilmot @ 16664,2294 = 2",
        "World/Signs/Speed - Alarka Branch @ 2286,-1678 = 1",
        "World/Signs/Speed - Alarka Branch @ 1859,775 = 2",
        "World/Signs/Speed - Before Ela @ 10585,6549 = 2",
        "World/Signs/Speed - Before Ela @ 10130,6835 = 1",
        "World/Signs/Speed - Bryson Horseshoe @ 1861,3766 = 1",
        "World/Signs/Speed - Bryson Horseshoe @ 1679,3999 = 2",
        "World/Signs/Speed - Bryson Horseshoe Above @ 2357,3876 = 2",
        "World/Signs/Speed - Bryson Horseshoe Above @ 2005,3922 = 1",
        "World/Signs/Speed - Bryson Trestle @ 1994,4602 = 1",
        "World/Signs/Speed - Bryson Trestle @ 2350,4699 = 2",
        "World/Signs/Speed - Ela Bridge @ 8688,7432 = 2",
        "World/Signs/Speed - Ela Bridge @ 8685,6842 = 1",
        "World/Signs/Speed - Twin Coves West @ -3729,1398 = 1",
        "World/Signs/Speed - Twin Coves West @ -1982,851 = 2",
    };
}
