// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The base map of Railroader 0.6.3 as the inventory of patcher build 7962df1 listed it, read from
// Fixtures/BaseMapInventory.tsv: every listed object with its hierarchy path, the components on it, the family that
// lists it and where it stands, and the unlisted objects around them that the rules read. The fixture holds what the
// inventory dump recorded: objects without a script component that the inventory does not list are not in it.
sealed class BaseMapObject {
    public bool Listed;
    public string Path, Family, Structure, Today, Expected;
    public string[] Components;
    public double[] At;
    public Node Node;
    public string Name => Path.Substring(Path.LastIndexOf('/') + 1);
    public bool TopSceneObject => Listed && Family == SceneObjectRules.Family;
    public bool ChildObject => Listed && Family == SceneObjectRules.ChildFamily;
    public const string Part = "child-object-v1";
}
sealed class BaseMapFixture {
    public readonly List<BaseMapObject> Objects = new List<BaseMapObject>();
    public readonly List<PlaceSource> MapData = new List<PlaceSource>();
    public readonly Node World = new Node("World");
    public ScenePlaces Places;
    public IReadOnlyList<SceneObjectVerdict> Verdicts;
    readonly string[] lines; readonly string file;

    // An empty cell is written as a hyphen, so no line ends in white space.
    static string[] Cells(string line) => line.Split('\t').Select(cell => cell == "-" ? "" : cell).ToArray();
    public static string FilePath([CallerFilePath] string source = null) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source), "Fixtures", "BaseMapInventory.tsv");
    static bool GivesPresence(string type) =>
        type == "UnityEngine.Light" || type.EndsWith("Collider", StringComparison.Ordinal) || (type.EndsWith("Renderer", StringComparison.Ordinal) && !SceneObjectRules.EffectRenderers.Contains(type) && type != "UnityEngine.CanvasRenderer");

    public BaseMapFixture(IEnumerable<BaseMapObject> extra = null) {
        file = FilePath(); lines = File.ReadAllLines(file);
        var types = new Dictionary<string, string>();
        foreach (var line in lines) {
            var cells = Cells(line);
            if (cells[0] == "C") types[cells[1]] = cells[2];
            else if (cells[0] == "M") MapData.Add(new PlaceSource(cells[1] == "stop" ? PlaceSourceKind.MapDataPassengerStop : PlaceSourceKind.MapDataArea, cells[2], new[] { Double.Parse(cells[3], CultureInfo.InvariantCulture), Double.Parse(cells[4], CultureInfo.InvariantCulture) }));
            else if (cells[0] == "O") Objects.Add(new BaseMapObject {
                Listed = cells[1] == "L", Path = cells[2], Components = cells[3].Length == 0 ? new string[0] : cells[3].Split(',').Select(i => types[i]).ToArray(), Family = cells[4].Length == 0 ? null : cells[4],
                Structure = cells[5].Length == 0 ? null : cells[5], At = new[] { Double.Parse(cells[6], CultureInfo.InvariantCulture), Double.Parse(cells[7], CultureInfo.InvariantCulture) },
                Today = cells[8], Expected = cells.Length > 9 ? cells[9] : ""
            });
        }
        if (extra != null) Objects.AddRange(extra);
        // The hierarchy below World, from the paths. Objects that share a path stand side by side; what the dump
        // recorded below such a path hangs below the first of them.
        var first = new Dictionary<string, Node> { ["World"] = World };
        Node At(string path) {
            if (first.TryGetValue(path, out var found)) return found;
            var slash = path.LastIndexOf('/');
            var made = new Node(path.Substring(slash + 1)); made.Own.Add("UnityEngine.Transform");
            At(path.Substring(0, slash)).Kids.Add(made);
            return first[path] = made;
        }
        var taken = new HashSet<string>();
        foreach (var o in Objects.Where(o => o.Path.StartsWith("World/", StringComparison.Ordinal)).OrderBy(o => o.Path.Count(c => c == '/')).ThenBy(o => o.Path, StringComparer.Ordinal)) {
            Node node;
            if (taken.Add(o.Path)) node = At(o.Path);
            else { var slash = o.Path.LastIndexOf('/'); node = new Node(o.Name); node.Own.Add("UnityEngine.Transform"); At(o.Path.Substring(0, slash)).Kids.Add(node); }
            node.Own.AddRange(o.Components);
            node.HasFamily = o.Listed && o.Family != null && !SceneObjectRules.InFamilies(o.Family);
            // The dump does not record the renderers below an object that another family lists, such as a sign's
            // boards: such an object counts as present.
            node.Physical = node.HasFamily || o.Components.Any(GivesPresence);
            o.Node = node;
        }
        Places = ScenePlaces.From(MapData.Concat(Objects.Where(o => !o.ChildObject).SelectMany(o => ScenePlaces.SourcesOf(o.Path, o.At))));
        Verdicts = SceneObjectRules.Classify(World);
    }

    static void Below(IHierarchyNode node, HashSet<string> types) { foreach (var type in node.Components) types.Add(type); foreach (var child in node.Children) Below(child, types); }
    // The category and kind the generic rules give a listed object that is no part.
    public SceneClass ClassOf(BaseMapObject o) {
        if (o.Node == null) return SceneClassification.Of(o.Family, o.Path, o.Components, o.Components, null, Places.StripPlaces);
        if (o.TopSceneObject) return SceneObjectRules.ClassOf(o.Node, o.Path, Places.StripPlaces);
        var types = new HashSet<string>(); Below(o.Node, types);
        return SceneClassification.Of(o.Family, o.Path, o.Node.Components, types, SceneObjectRules.NamesBelow(o.Node), Places.StripPlaces);
    }
    public string Outcome(BaseMapObject o) => ClassOf(o) + " @ " + (Places.Of(o.Path, o.At)?.Name ?? "no place");

    // The inventory the outliner gets for this map: one observation per listed object, a part with its group, a
    // detection span with its controller, and a top scene object with the category and the reason the rules give it.
    public List<SceneObservation> Observations(out Dictionary<SceneObservation, BaseMapObject> source) {
        var roots = Verdicts.Where(v => v.Disposition == GroupDisposition.Group || v.Disposition == GroupDisposition.InspectionOnly).GroupBy(v => v.Path).ToDictionary(g => g.Key, g => g.First());
        var controllers = new HashSet<string>(Objects.Where(o => o.Listed && o.Family == SceneFamilies.CrossingController).Select(o => o.Path));
        string Above(string path, Func<string, bool> found) {
            for (var slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/')) { path = path.Substring(0, slash); if (found(path)) return path; }
            return null;
        }
        source = new Dictionary<SceneObservation, BaseMapObject>();
        var rows = new List<SceneObservation>();
        foreach (var o in Objects.Where(o => o.Listed)) {
            SceneObjectVerdict root = null;
            if (o.TopSceneObject) roots.TryGetValue(o.Path, out root);
            var row = new SceneObservation(new object(), new TargetIdentity("BushnellWhittier-MapManager", o.Path, "UnityEngine.Transform", o.Structure), o.Family, true, new[] { o.At[0], 0, o.At[1] }, new double[] { 0, 0, 0, 1 },
                new string[0], new string[0], new string[0], "Placement", inspectionOnly: root?.Disposition == GroupDisposition.InspectionOnly ? root.Reason : null,
                groupPath: o.ChildObject ? Above(o.Path, roots.ContainsKey) : null, category: root?.Category, subcategory: root?.Subcategory,
                holderPath: o.Family == SceneFamilies.TrackSpan ? Above(o.Path, controllers.Contains) : null, bridge: root?.Bridge ?? BridgeKind.None);
            source[row] = o; rows.Add(row);
        }
        return rows;
    }
    // The world the outliner reads beside that inventory: what the fixture recorded on and below each object, the
    // game's map data, and the places the unlisted objects name. No object has bounds: each stands at its position.
    public IOutlinerScene Scene(Dictionary<SceneObservation, BaseMapObject> source) => new FixtureScene { Map = this, Source = source };
    sealed class FixtureScene : IOutlinerScene {
        public BaseMapFixture Map; public Dictionary<SceneObservation, BaseMapObject> Source;
        public string WorldKey => "base map";
        public ObjectFacts Facts(SceneObservation entry) {
            var o = Source[entry];
            if (o.Node == null) return new ObjectFacts { Own = o.Components, AtOrBelow = o.Components };
            var types = new HashSet<string>(); Below(o.Node, types);
            return new ObjectFacts { Own = o.Node.Components.ToArray(), AtOrBelow = types.ToArray(), NamesBelow = SceneObjectRules.NamesBelow(o.Node).ToArray() };
        }
        public double[] Bounds(SceneObservation entry) => null;
        public double[] Displacement(SceneObservation entry) => null;
        public IEnumerable<PlaceSource> PlaceSources(IReadOnlyList<SceneObservation> entries, List<string> notes) =>
            Map.MapData.Concat(Map.Objects.Where(o => !o.Listed).SelectMany(o => ScenePlaces.SourcesOf(o.Path, o.At)));
    }

    // Writes the outcomes of the rules into the fixture's expected column, after they were reviewed.
    public void WriteExpected() {
        var byLine = new Dictionary<string, Queue<BaseMapObject>>();
        foreach (var o in Objects) { var key = o.Path + "\t" + o.Family + "\t" + o.Structure + "\t" + o.At[0].ToString(CultureInfo.InvariantCulture) + "\t" + o.At[1].ToString(CultureInfo.InvariantCulture); if (!byLine.TryGetValue(key, out var q)) byLine[key] = q = new Queue<BaseMapObject>(); q.Enqueue(o); }
        var written = lines.Select(line => {
            var cells = Cells(line);
            if (cells[0] != "O" || cells[1] != "L" || cells[5] == BaseMapObject.Part) return line;
            var o = byLine[cells[2] + "\t" + (cells[4].Length == 0 ? null : cells[4]) + "\t" + (cells[5].Length == 0 ? null : cells[5]) + "\t" + cells[6] + "\t" + cells[7]].Dequeue();
            return String.Join("\t", cells.Take(9).Concat(new[] { Outcome(o) }).Select(cell => cell.Length == 0 ? "-" : cell));
        });
        File.WriteAllText(file, String.Join("\n", written) + "\n");
    }
}
