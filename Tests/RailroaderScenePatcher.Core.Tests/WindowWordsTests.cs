// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

// The window's words for a complex and a child object: "group" and "part". No window text says "complex" or "child
// object", no window or Log text names the retired scenery terms or says "object list", and the retired Pavilion
// kind and Track-relative family are gone from the code. Where a modder could take a group for a track group of the
// native map editor, at a crossing, a rail bridge or a trackside station, its tooltip says that the group means scene
// objects.
static class WindowWordsTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Window words: " + message); }
    static string Root([CallerFilePath] string path = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", ".."));

    internal static void Run() {
        WindowLiteralsNameNoComplexOrChildObject();
        NoRetiredSceneryTerms();
        NoPavilionKindOrTrackRelativeFamily();
        GroupTooltipsSayTheyMeanSceneObjects();
        HelpTermsUseTheWindowsWords();
        ObjectOriginNamesTheMapOrMod();
        NoTrackMarkerFamily();
        Console.WriteLine("PASS: window words group and part");
    }

    static void ObjectOriginNamesTheMapOrMod() {
        var map = new BaseMapFixture();
        var entries = map.Observations(out _);
        Check(entries.All(entry => InventoryPresentationState.ObjectOrigin(entry) == ("Origin", "Map")),
            "every Object tab in the base map fixture has Origin Map");

        var root = ModObjectTests.Made;
        foreach (var path in new[] { root, root + "/Clock" }) {
            var observation = new SceneObservation(new object(), ModObjectTests.Id(path), SceneObjectRules.Family, true,
                new[] { 0.0, 0, 0 }, new[] { 0.0, 0, 0, 1 }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "Placement");
            Check(InventoryPresentationState.ObjectOrigin(observation) == ("Origin", "Mod"), path + " has Origin Mod on its Object tab");
        }
    }

    // The string literals of a C# source, without comments: regular, verbatim and interpolated strings.
    internal static List<string> Literals(string source) {
        var found = new List<string>();
        var i = 0;
        while (i < source.Length) {
            var c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/') { while (i < source.Length && source[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*') { var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal); i = end < 0 ? source.Length : end + 2; continue; }
            if (c == '\'') { i++; while (i < source.Length && source[i] != '\'') i += source[i] == '\\' ? 2 : 1; i++; continue; }
            var verbatim = false;
            if ((c == '@' || c == '$') && i + 1 < source.Length && (source[i + 1] == '"' || ((source[i + 1] == '@' || source[i + 1] == '$') && i + 2 < source.Length && source[i + 2] == '"'))) {
                verbatim = c == '@' || source[i + 1] == '@';
                i += source[i + 1] == '"' ? 1 : 2;
                c = source[i];
            }
            if (c != '"') { i++; continue; }
            var text = new StringBuilder();
            i++;
            while (i < source.Length) {
                if (verbatim && source[i] == '"' && i + 1 < source.Length && source[i + 1] == '"') { text.Append('"'); i += 2; continue; }
                if (source[i] == '"') { i++; break; }
                if (!verbatim && source[i] == '\\' && i + 1 < source.Length) { text.Append(source[i + 1] == 'n' ? '\n' : source[i + 1]); i += 2; continue; }
                text.Append(source[i]); i++;
            }
            found.Add(text.ToString());
        }
        return found;
    }
    static IEnumerable<string> Hits(string file, params string[] words) {
        var text = File.ReadAllText(Path.Combine(Root(), file));
        return Literals(text).Where(l => words.Any(w => l.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).Select(l => file + ": \"" + l + "\"");
    }

    // The help explains a state or a heading with the word the window shows for it: the empty-map state by the reason
    // the outliner states, and problems as problems, never with a second word for the same concept.
    static void HelpTermsUseTheWindowsWords() {
        Check(InventoryPresentationState.NoEditableObjects.IndexOf(InventoryPresentationState.NoEditableObjectsTerm, StringComparison.OrdinalIgnoreCase) >= 0,
            "the outliner's empty-map reason names its help term: " + InventoryPresentationState.NoEditableObjectsTerm);
        const string layout = "Src/RailroaderScenePatcher/ScenePatcherLayout.cs";
        var source = File.ReadAllText(Path.Combine(Root(), layout));
        Check(source.Contains("new[] { InventoryPresentationState.NoEditableObjectsTerm, "), "the outliner help takes the empty-map term from the window's reason");
        var hits = Hits(layout, "No objects to list", "defect").ToArray();
        Check(hits.Length == 0, "the help uses the window's terms only:\n" + String.Join("\n", hits));
    }

    static void WindowLiteralsNameNoComplexOrChildObject() {
        Check(Literals("var a = \"x\"; // \"y\"\nvar b = @\"a\"\"b\"; var c = $\"c{d}\"; char e = '\"';").SequenceEqual(new[] { "x", "a\"b", "c{d}" }), "the literal reader reads regular, verbatim and interpolated strings and skips comments and characters");
        var files = new[] { "Src/RailroaderScenePatcher/ScenePatcherLayout.cs", "Src/RailroaderScenePatcher/CommandBand.cs", "Src/RailroaderScenePatcher/WorldSelectionOverlay.cs" };
        var hits = files.SelectMany(f => Hits(f, "complex", "child object")).ToArray();
        Check(hits.Length == 0, "no window text says complex or child object:\n" + String.Join("\n", hits));
    }

    static void NoRetiredSceneryTerms() {
        var root = Root();
        string Relative(string file) => file.Substring(root.Length + 1).Replace('\\', '/');
        var files = new[] { "Src/RailroaderScenePatcher", "Src/RailroaderScenePatcher.Core" }.SelectMany(d => Directory.GetFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !Relative(f).Split('/').Any(p => p == "bin" || p == "obj")).Select(Relative).ToArray();
        Check(files.Any(f => f.EndsWith("/PatchEngine.cs", StringComparison.Ordinal)) && files.Any(f => f.EndsWith("/ScenePatcherLayout.cs", StringComparison.Ordinal)), "the check reads the window's and the core's sources");
        var hits = files.SelectMany(f => Hits(f, "baked scenery", "scenery group", "scenery part")).ToArray();
        Check(hits.Length == 0, "no window or Log text says baked scenery, scenery group or scenery part:\n" + String.Join("\n", hits));
        // The glossary's term to avoid for the outliner; the window says outliner, or inventory for the listed objects.
        var lists = files.SelectMany(f => Hits(f, "object list")).ToArray();
        Check(lists.Length == 0, "no window or Log text says object list:\n" + String.Join("\n", lists));
    }

    // The family of the Generic track markers that no more specific family claims is Generic track marker. No window
    // text, and neither the glossary nor a guide, names a family "Track marker": the bare name is the outliner category
    // only, which keeps it. A sentence about one marker, such as "Track marker M4 is not on the track", names no family.
    static void NoTrackMarkerFamily() {
        var root = Root();
        var bare = new System.Text.RegularExpressions.Regex(@"\bTrack marker\b(?! [A-Za-z0-9{]| $)");
        var code = Directory.GetFiles(Path.Combine(root, "Src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .SelectMany(f => Literals(File.ReadAllText(f)).Select(l => new { File = Path.GetFileName(f), Text = l })).ToArray();
        Check(code.Any(l => l.Text == SceneFamilies.TrackMarker), "the window names the family " + SceneFamilies.TrackMarker);
        var hits = code.Where(l => (bare.IsMatch(l.Text) || l.Text.IndexOf("Track marker family", StringComparison.Ordinal) >= 0)
            && !(l.File == "SceneClassification.cs" && l.Text == "Track marker")).Select(l => l.File + ": \"" + l.Text + "\"").ToArray();
        Check(hits.Length == 0, "no window text names a family Track marker; only the category constant says Track marker:\n" + String.Join("\n", hits));
        var family = new System.Text.RegularExpressions.Regex(@"(?<![Gg]eneric )(\*\*|`)?Track marker(\*\*|`)? family");
        var docs = new[] { Path.Combine(root, "GLOSSARY.md") }.Concat(Directory.GetFiles(Path.Combine(root, "docs", "guides"), "*.md", SearchOption.AllDirectories)).ToArray();
        var docHits = docs.SelectMany(f => File.ReadAllLines(f).Where(l => family.IsMatch(l)).Select(l => Path.GetFileName(f) + ": " + l)).ToArray();
        Check(docHits.Length == 0, "neither the glossary nor a guide names a family Track marker:\n" + String.Join("\n", docHits));
        var glossary = File.ReadAllText(Path.Combine(root, "GLOSSARY.md"));
        var entry = glossary.Substring(glossary.IndexOf("**Family**", StringComparison.Ordinal));
        entry = entry.Substring(0, entry.IndexOf("\n**", 2, StringComparison.Ordinal) < 0 ? entry.Length : entry.IndexOf("\n**", 2, StringComparison.Ordinal));
        Check(entry.Contains("Generic track marker"), "the glossary's Family entry names the family Generic track marker");
    }

    static void NoPavilionKindOrTrackRelativeFamily() {
        var root = Root();
        var literals = Directory.GetFiles(Path.Combine(root, "Src"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .SelectMany(f => Literals(File.ReadAllText(f))).ToArray();
        Check(!literals.Any(l => l.IndexOf("Track-relative scene object", StringComparison.OrdinalIgnoreCase) >= 0), "the code has no Track-relative scene object family");
        Check(!literals.Any(l => l == "Pavilion" || l == "Pavilions"), "the code has no Pavilion kind");
        var map = new BaseMapFixture();
        Check(!map.Objects.Any(o => (o.Family ?? "").IndexOf("Track-relative", StringComparison.OrdinalIgnoreCase) >= 0), "no inventory row of the base map carries the Track-relative scene object family");
        Check(!map.Objects.Where(o => o.Listed && !o.ChildObject).Any(o => map.ClassOf(o).Kind == "Pavilion"), "no inventory row of the base map carries the Pavilion kind");
    }

    static IEnumerable<OutlineNode> All(IEnumerable<OutlineNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(All(n.Children)));
    static void GroupTooltipsSayTheyMeanSceneObjects() {
        var map = new BaseMapFixture();
        var entries = map.Observations(out var source);
        OutlinerGrouping.InvalidatePlaces();
        OutlinerGrouping.Scene = map.Scene(source);
        try {
            var tips = new Dictionary<string, Dictionary<SceneObservation, string>>();
            foreach (var view in new[] { false, true }) {
                OutlinerGrouping.ByPlace = view;
                var tree = OutlinerGrouping.Build(entries, entries, "", false, null);
                tips[view ? "By place" : "By category"] = All(tree.Tops).Where(n => n.Row != null).ToDictionary(n => n.Row, n => OutlinerGrouping.Tooltip(tree, n.Row, false, null));
            }
            var byCategory = tips["By category"]; var byPlace = tips["By place"];
            Check(byCategory.Count == byPlace.Count && byCategory.All(t => byPlace.TryGetValue(t.Key, out var other) && other == t.Value), "both views give each row the same tooltip");
            string Tip(string path) => byCategory.First(t => t.Key.Identity.Path == path).Value;
            var trackSide = CommandBand.GroupLine(true);
            Check(trackSide == "Group of scene objects, not a track group" && CommandBand.GroupLine(false) == "Group of scene objects", "a group's tooltip line says it means scene objects (" + trackSide + ")");
            // A group at a crossing, a rail bridge and a trackside station: the base map's crossbuck, Almond Bridge and Bryson Depot.
            var crossbuck = entries.First(e => e.Family == SceneObjectRules.Family && e.GroupPath == null && e.Category == SceneObjectCategories.GradeCrossing).Identity.Path;
            var railBridge = entries.First(e => e.Family == SceneObjectRules.Family && e.GroupPath == null && e.Category == SceneObjectCategories.Bridge && map.ClassOf(map.Objects.Single(o => o.Path == e.Identity.Path)).Kind == SceneObjectCategories.RailBridges).Identity.Path;
            var station = entries.First(e => e.Family == SceneObjectRules.Family && e.GroupPath == null && e.Category == SceneObjectCategories.StationBuilding).Identity.Path;
            foreach (var path in new[] { crossbuck, railBridge, station })
                Check(Tip(path).Split('\n').Contains(trackSide), "the group row of " + path + " says the group means scene objects, not a track group (got " + Tip(path).Replace("\n", " | ") + ")");
            // Every group row of the base map at a crossing, a rail bridge or a station says so; the others say only that they are a group of scene objects.
            foreach (var row in byCategory.Keys.Where(r => r.Family == SceneObjectRules.Family && r.GroupPath == null)) {
                var lines = byCategory[row].Split('\n');
                Check(lines.Contains(trackSide) || lines.Contains(CommandBand.GroupLine(false)), row.Identity.Path + " is a group of scene objects in its tooltip");
            }
            var mill = entries.First(e => e.Family == SceneObjectRules.Family && e.GroupPath == null && e.Category == SceneObjectCategories.IndustryBuilding).Identity.Path;
            Check(Tip(mill).Split('\n').Contains(CommandBand.GroupLine(false)), "a group elsewhere is a group of scene objects without the track group (got " + Tip(mill).Replace("\n", " | ") + ")");
            // A part names its group in its tooltip, in both views alike.
            var part = byCategory.Keys.First(r => r.GroupPath != null);
            Check(byCategory[part].Split('\n').Contains("Part of: " + part.GroupPath.Substring(part.GroupPath.LastIndexOf('/') + 1)), "a part's tooltip names its group (got " + byCategory[part].Replace("\n", " | ") + ")");
            Check(byCategory.Values.All(t => t.IndexOf("complex", StringComparison.OrdinalIgnoreCase) < 0 && t.IndexOf("child object", StringComparison.OrdinalIgnoreCase) < 0), "no row tooltip says complex or child object");
        } finally {
            OutlinerGrouping.Scene = null; OutlinerGrouping.ByPlace = false; OutlinerGrouping.InvalidatePlaces();
        }
        // The Select group button's tooltip says the group means scene objects.
        Check(CommandBand.SelectGroupTip.Contains("group of scene objects"), "the Select group button says its group means scene objects (" + CommandBand.SelectGroupTip + ")");
        // The square marker of a group names the group as a group of scene objects; at a crossing, a rail bridge and
        // a station also that it is no track group.
        Check(CommandBand.GroupMarkerText("Almond Bridge", true) == "Almond Bridge\nGroup of scene objects, not a track group" && CommandBand.GroupMarkerText("Sylva Culled", false) == "Sylva Culled\nGroup of scene objects", "a square marker names its group and says it means scene objects");
        Check(CommandBand.TrackSide(ComplexKind.GradeCrossingSetup, null, BridgeKind.None) && CommandBand.TrackSide(ComplexKind.RailBridgeConstruction, null, BridgeKind.None)
            && CommandBand.TrackSide(ComplexKind.SceneObject, SceneObjectCategories.StationBuilding, BridgeKind.None) && CommandBand.TrackSide(ComplexKind.SceneObject, SceneObjectCategories.Bridge, BridgeKind.Rail)
            && !CommandBand.TrackSide(ComplexKind.SceneObject, SceneObjectCategories.Bridge, BridgeKind.Road) && !CommandBand.TrackSide(ComplexKind.IndustryStorageDisplay, null, BridgeKind.None)
            && !CommandBand.TrackSide(ComplexKind.SceneObject, SceneObjectCategories.IndustryBuilding, BridgeKind.None),
            "a group at a crossing, a rail bridge or a trackside station could be taken for a track group; one elsewhere not");
    }
}
