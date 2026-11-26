// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The family names of the base-map inventory after the families were named by the family rule: each family value and
// each label the window shows for it changes only by the mapping below, and no other family changes. The counts are
// those of the base-map fixture before the rename.
static class FamilyMappingTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Family mapping: " + message); }

    // The families that were renamed, from their former name to their name now. The former family of the scene objects
    // with child objects splits by structure id: its top scene objects, and its three sign holders without a structure
    // id, are Scene object; its child objects are Child object.
    const string FormerSceneObjects = "(former family of the scene objects with child objects)";
    // The former names stand as placeholders: the retired names appear nowhere in the code.
    const string LocationSignsBefore = "(former family of the location signs)", StarterPlacementsBefore = "(former family of the starter placement markers)",
        ControllersBefore = "(former family of the grade crossing controllers)";
    static readonly Dictionary<string, string> Renamed = new Dictionary<string, string> {
        [LocationSignsBefore] = "Location sign", [StarterPlacementsBefore] = "Starter placement marker", [ControllersBefore] = "Grade crossing controller",
    };
    // The families of the base-map fixture that keep their names, with their row counts.
    static readonly Dictionary<string, int> Kept = new Dictionary<string, int> {
        ["Track marker"] = 111, ["Milepost"] = 54, ["Crossing pad marker"] = 47, ["Spawn point"] = 22, ["Track span"] = 21, ["Station map icon"] = 15,
        ["Crossing mast"] = 8, ["Crossing bell"] = 4, ["Bridge"] = 1, ["Bridge construction scenery"] = 1,
    };

    internal static void Run() {
        CodeNamesFollowTheMapping();
        FixtureFamiliesChangeOnlyByTheMapping();
        ShownFamiliesChangeOnlyByTheMapping();
        ShownFamiliesAreInSentenceCase();
        Console.WriteLine("PASS: family values and labels change only by the family mapping");
    }

    // The former family of a family value now, for a row with its structure id.
    static string Former(string family, string structure) {
        if (family == SceneObjectRules.Family) return structure == null || structure == SceneObjects.Structure ? FormerSceneObjects : null;
        if (family == SceneObjectRules.ChildFamily) return structure == ChildObjects.Structure ? FormerSceneObjects : null;
        var former = Renamed.Where(pair => pair.Value == family).Select(pair => pair.Key).FirstOrDefault();
        return former ?? (Kept.ContainsKey(family ?? "") ? family : null);
    }
    // The label a former family value showed: a child object showed as Part, every other object as its family.
    static string FormerShown(string former, bool child) => child ? CommandBand.PartFamily : former;
    // The label the mapping gives for a former label and structure.
    static string Mapped(string formerShown, string structure) {
        if (formerShown == CommandBand.PartFamily) return CommandBand.PartFamily;
        // The window calls a top scene object, the top of a complex, a Group.
        if (formerShown == FormerSceneObjects) return structure == ChildObjects.Structure ? SceneObjectRules.ChildFamily : CommandBand.GroupFamily;
        return Renamed.TryGetValue(formerShown, out var now) ? now : formerShown;
    }

    static void CodeNamesFollowTheMapping() {
        Check(SceneObjectRules.Family == "Scene object" && SceneFamilies.SceneObject == "Scene object", "the top scene objects are in the Scene object family");
        Check(SceneObjectRules.ChildFamily == "Child object" && SceneFamilies.ChildObject == "Child object", "the child objects are in the Child object family");
        Check(SceneFamilies.LocationSign == "Location sign" && StarterPlacements.Family == "Starter placement marker" && SceneFamilies.StarterPlacement == "Starter placement marker"
            && SceneFamilies.CrossingController == "Grade crossing controller" && ObjectMarkingGeometry.ControllerFamily == "Grade crossing controller",
            "Location sign, Starter placement marker and Grade crossing controller are the renamed families");
        Check(SceneFamilies.CrossingMast == "Crossing mast" && SceneFamilies.CrossingBell == "Crossing bell" && SceneFamilies.Bridge == "Bridge"
            && SceneFamilies.BridgeConstruction == "Bridge construction scenery" && SceneFamilies.AddedEquipment == "Added light or gate",
            "the families with no game class of their own keep their names");
        Check(SceneFamilies.TrackMarker == "Track marker" && SceneFamilies.Milepost == "Milepost" && SceneFamilies.CrossingPad == "Crossing pad marker"
            && SceneFamilies.SpawnPoint == "Spawn point" && SceneFamilies.TrackSpan == "Track span" && SceneFamilies.StationMapIcon == "Station map icon",
            "the families already named by the family rule keep their names");
        Check(SceneClassification.StarterPlacement == "Starter placement", "the outliner category Starter placement keeps its name; only its family is renamed");
        Check(SceneObjects.Structure == "scene-object-v1" && ChildObjects.Structure == "child-object-v1" && LocationSigns.Structure == "location-sign-v1",
            "the structure ids are scene-object-v1, child-object-v1 and location-sign-v1");
    }

    static void FixtureFamiliesChangeOnlyByTheMapping() {
        var map = new BaseMapFixture();
        var listed = map.Objects.Where(o => o.Listed).ToArray();
        var unknown = listed.Where(o => Former(o.Family, o.Structure) == null).Select(o => o.Path + " (" + o.Family + ", " + (o.Structure ?? "no structure") + ")").ToArray();
        Check(unknown.Length == 0, "every listed row has a family value of the mapping or a kept family: " + String.Join("; ", unknown.Take(5)));
        var byFormer = listed.GroupBy(o => Former(o.Family, o.Structure)).ToDictionary(g => g.Key, g => g.ToArray());
        int Count(string former) => byFormer.TryGetValue(former, out var rows) ? rows.Length : 0;

        var former = byFormer[FormerSceneObjects];
        Check(former.Length == 1547, "the former family " + FormerSceneObjects + " holds 1547 rows, got " + former.Length);
        Check(former.Count(o => o.Structure == SceneObjects.Structure && o.Family == SceneObjectRules.Family) == 141, "its 141 scene-object-v1 rows are Scene object");
        Check(former.Count(o => o.Structure == ChildObjects.Structure && o.Family == SceneObjectRules.ChildFamily) == 1403, "its 1403 child-object-v1 rows are Child object");
        var unstructured = former.Where(o => o.Structure == null).ToArray();
        Check(unstructured.Length == 3 && unstructured.All(o => o.Family == SceneObjectRules.Family)
            && unstructured.Select(o => o.Path).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(new[] {
                "World/Large Scenery/Bryson/Bryson Signs", "World/Large Scenery/Ela/Signs", "World/Large Scenery/Sylva/Sylva Signs" }),
            "its three rows without a structure id, the sign holders of Bryson, Ela and Sylva, are Scene object: " + String.Join(", ", unstructured.Select(o => o.Path)));

        var signs = byFormer[LocationSignsBefore];
        Check(signs.Length == 55 && signs.All(o => o.Name.StartsWith("Loc:", StringComparison.Ordinal) && o.Family == "Location sign")
            && signs.Count(o => o.Structure == LocationSigns.Structure) == 8 && signs.Count(o => o.Structure == null) == 47,
            "the 55 Loc: rows, 8 with location-sign-v1 and 47 without a structure id, are Location sign by family value");
        Check(Count(StarterPlacementsBefore) == 8 && byFormer[StarterPlacementsBefore].All(o => o.Family == "Starter placement marker"), "the 8 starter placement rows are Starter placement marker");
        Check(Count(ControllersBefore) == 4 && byFormer[ControllersBefore].All(o => o.Family == "Grade crossing controller"), "the 4 crossing controller rows are Grade crossing controller");
        foreach (var pair in Kept) Check(Count(pair.Key) == pair.Value, "the kept family " + pair.Key + " holds " + pair.Value + " rows, got " + Count(pair.Key));
        Check(byFormer.Keys.All(k => k == FormerSceneObjects || Renamed.ContainsKey(k) || Kept.ContainsKey(k)), "no other family appears");
        Check(listed.Length == 1547 + 55 + 8 + 4 + Kept.Values.Sum(), "the fixture lists the same number of objects as before: " + listed.Length);
    }

    // Each family name the window shows for a listed object of the base map: sentence case with spaces, Milepost one
    // word, and neither "complex" nor "child object".
    static void ShownFamiliesAreInSentenceCase() {
        var map = new BaseMapFixture();
        var shown = map.Observations(out _).Select(CommandBand.ShownFamily).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var expected = new[] { "Bridge", "Bridge construction scenery", "Crossing bell", "Crossing mast", "Crossing pad marker", "Grade crossing controller", "Group", "Location sign", "Milepost", "Part",
            "Spawn point", "Starter placement marker", "Station map icon", "Track marker", "Track span" };
        Check(shown.SequenceEqual(expected), "the window shows these family names for the base map: " + String.Join(", ", shown));
        var sentence = new System.Text.RegularExpressions.Regex("^[A-Z][a-z]+( [a-z]+)*$");
        Check(shown.All(f => sentence.IsMatch(f)), "each family name shown is in sentence case with spaces: " + String.Join(", ", shown.Where(f => !sentence.IsMatch(f))));
        Check(!shown.Any(f => f.IndexOf("complex", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf("child object", StringComparison.OrdinalIgnoreCase) >= 0), "no family name shown says complex or child object");
    }

    static void ShownFamiliesChangeOnlyByTheMapping() {
        var map = new BaseMapFixture();
        var rows = map.Observations(out var source);
        var wrong = new List<string>();
        foreach (var row in rows) {
            var o = source[row];
            var former = Former(o.Family, o.Structure);
            var expected = Mapped(FormerShown(former, row.GroupPath != null), o.Structure);
            var shown = CommandBand.ShownFamily(row);
            if (shown != expected) wrong.Add(o.Path + ": shows " + shown + ", expected " + expected);
        }
        Check(wrong.Count == 0, wrong.Count + " rows show another family label than the mapping gives: " + String.Join("; ", wrong.Take(5)));
        Check(rows.Where(r => r.Family == SceneObjectRules.ChildFamily).All(r => r.GroupPath != null && CommandBand.ShownFamily(r) == CommandBand.PartFamily),
            "a child object still shows as Part, the window word for a child object");
        Check(rows.Where(r => r.Family == SceneObjectRules.Family).All(r => CommandBand.ShownFamily(r) == CommandBand.GroupFamily), "a top scene object shows as Group, the window word for a complex");
        var holders = OutlinerGrouping.SignContainers(rows).Select(r => r.Identity.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Check(holders.SequenceEqual(new[] { "World/Large Scenery/Bryson/Bryson Signs", "World/Large Scenery/Ela/Signs", "World/Large Scenery/Sylva/Sylva Signs" }),
            "the three sign holders in the Scene object family stay unlisted in the outliner: " + String.Join(", ", holders));
    }
}
