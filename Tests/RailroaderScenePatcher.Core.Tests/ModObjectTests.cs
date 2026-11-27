// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Mod objects: a modification with a source makes a new object from a supported scene object. The scene patch file
// holds the source, the mod object's own target and its placement under a key that is the mod object's identity in its
// mod; each load builds the object again from its source. The station building is the Alarka Jct Station of the base
// map fixture.
static class ModObjectTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Mod objects: " + message); }
    internal const string SceneName = "BushnellWhittier-MapManager", Station = "World/Large Scenery/Alarka Jct/Alarka Jct Station", Component = SceneObjects.ComponentType;
    const string Key = "mod-object-alarka-jct-station";
    internal static readonly string Made = ModObjects.Path(Station, "author/pack", Key);
    static readonly double[] Upright = { 0, 0, 0, 1 }, AtBryson = { 4493, 12, 5415 };
    static string Numbers(IEnumerable<double> values) => String.Join(",", values.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    static string Target(string path, string structure = SceneObjects.Structure) =>
        "{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"" + Component + "\"" + (structure == null ? "" : ",\"structure\":\"" + structure + "\"") + "}";
    static string ModObject(string key, string source, string made, double[] position, string extra = "") =>
        "\"" + key + "\":{\"target\":" + Target(made) + ",\"source\":" + Target(source) + (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + Numbers(position) + "],\"rotation\":[0,0,0,1]}") + extra + ",\"dependsOn\":[]}";
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    internal static TargetIdentity Id(string path) => new TargetIdentity(SceneName, path, Component, SceneObjects.Structure);
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) => a != null && b != null && a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= 0.001);

    // What builds mod objects in the controlled group world: a new group with the source's parts, placed where asked.
    internal sealed class Builder : IModObjectScene {
        readonly FakeGroupWorld world;
        public int Builds, Removes;
        public Builder(FakeGroupWorld world) { this.world = world; }
        public object Build(TargetIdentity identity, object source, Placement placement) {
            if (world.Objects.TryGetValue(identity.Path, out var standing) && standing.Count != 0) throw new InvalidOperationException(ModObjects.Taken);
            var from = (FakeGroup)source;
            var made = new FakeGroup { Position = new[] { placement.Position[0] - world.Origin, placement.Position[1], placement.Position[2] }, Rotation = (double[])placement.Rotation.Clone(), Parent = from.Parent };
            world.Objects[identity.Path] = new List<object> { made };
            Builds++;
            return made;
        }
        public void Remove(object modObject) { foreach (var pair in world.Objects.Where(p => p.Value.Contains(modObject)).ToArray()) world.Objects.Remove(pair.Key); Removes++; }
    }
    // One loaded world: the station building of the base map, the scene object capability and what builds mod objects.
    internal sealed class Loaded {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeGroup Building = new FakeGroup { Position = new[] { 733.0, 9, 1473 } };
        public readonly Builder Builder;
        public readonly PatchEngine Engine;
        public Loaded() {
            World.Objects[Station] = new List<object> { Building };
            var scene = new SceneCapabilities(() => World.MapIds, null) { ModObjectWorld = Builder = new Builder(World) };
            scene.Add(new SceneObjects(World));
            Engine = new PatchEngine(scene);
        }
        public FakeGroup ModObject => World.Objects.TryGetValue(Made, out var found) && found.Count == 1 ? (FakeGroup)found[0] : null;
        public double[] At(FakeGroup group) => World.Observe(group).Root.Placement.Position.ToArray();
    }

    internal static void Run() {
        PathsOfModObjects();
        EachLoadBuildsTheModObjectFromItsSource();
        AModObjectWithoutItsSourceFailsAlone();
        AModObjectNeedsItsPlacementAndItsOwnTarget();
        AModObjectLeavesWithItsModification();
        PlacingAddsTheModificationAndTheObject();
        EditsOfTheFamilyApplyToAModObject();
        AModObjectCanBeDuplicatedAgain();
        RemoveTakesTheModObjectAndItsLaterEdits();
        AModObjectIsListedWhereItStands();
        StationNameFollowsItsPlace();
        CanonicalSaveOrdersAModObject();
        Console.WriteLine("PASS: mod objects");
    }

    // The path of a mod object is its source's path with a mark that holds the mod and the modification key.
    static void PathsOfModObjects() {
        Assert(Made == Station + " [mod object author:pack " + Key + "]", "The path names the source, the mod and the key: " + Made);
        Assert(ModObjects.IsPath(Made) && !ModObjects.IsPath(Station) && !ModObjects.IsPath(Made + "/Mask"), "Only the mod object's own path is a mod object path");
        Assert(ModObjects.RootOf(Made + "/Sign/Text") == Made && ModObjects.RootOf(Station + "/Mask") == null && ModObjects.Within(Made), "An object below a mod object lies in it");
        Assert(ModObjects.Plain(Made + "/Mask") == Station + "/Mask", "Without its mark the path is the source's: " + ModObjects.Plain(Made + "/Mask"));
        Assert(ModObjects.Path(Made, "author/pack", "second") == Station + " [mod object author:pack second]", "A mod object's path never holds two marks");
    }

    // A load builds the mod object at its placement and leaves the source as it is; applying the file again builds
    // nothing more; the next load, a new world, builds it again.
    static void EachLoadBuildsTheModObjectFromItsSource() {
        var doc = Doc(ModObject(Key, Station, Made, AtBryson));
        for (var load = 1; load <= 2; load++) {
            var world = new Loaded(); var before = world.At(world.Building);
            var result = world.Engine.Apply(doc, "author/pack");
            Assert(result.Error == null && result.Outcomes[Key].Applied, "Load " + load + " applies the mod object: " + (result.Error ?? result.Outcomes[Key].Error));
            Assert(world.Builder.Builds == 1 && world.ModObject != null && Near(world.At(world.ModObject), AtBryson), "Load " + load + " builds the mod object at its placement");
            Assert(Near(world.At(world.Building), before) && world.Building.Active, "The source stays where it stands");
            Assert(TargetIdentity.SameObject(result.Outcomes[Key].Target, Id(Made)), "The outcome names the mod object's own target");
            var again = world.Engine.Apply(doc, "author/pack");
            Assert(again.Outcomes[Key].Applied && world.Builder.Builds == 1 && world.World.Objects[Made].Count == 1, "Applying the file again builds no second object");
        }
    }

    // A source that no object matches fails its mod object with the reason; the other modifications apply.
    static void AModObjectWithoutItsSourceFailsAlone() {
        var world = new Loaded();
        const string gone = "World/Large Scenery/Alarka Jct/Old Station";
        var result = world.Engine.Apply(Doc(ModObject("mod-object-old", gone, ModObjects.Path(gone, "author/pack", "mod-object-old"), AtBryson), ModObject(Key, Station, Made, AtBryson)), "author/pack");
        var failed = result.Outcomes["mod-object-old"];
        Assert(!failed.Applied && failed.Error == ModObjects.SourceMissing && failed.Cause == FailureCause.InvalidDefinition && failed.Failure == FailureKind.Refused, "The missing source refuses its mod object: " + failed.Error);
        Assert(result.Outcomes[Key].Applied && world.Builder.Builds == 1, "The other mod object is built");
        // A failure after the object was built leaves no object behind.
        var failing = new Loaded(); failing.World.ThrowAfterPlacement = true;
        var outcome = failing.Engine.Apply(Doc(ModObject(Key, Station, Made, AtBryson, ",\"active\":false")), "author/pack").Outcomes[Key];
        Assert(!outcome.Applied && failing.ModObject == null && failing.Builder.Removes == 1, "A mod object whose state fails to apply is removed again: " + outcome.Error);
    }

    // A mod object's modification holds a placement, and a target that names the source's scene, component and
    // structure with a mod object path.
    static void AModObjectNeedsItsPlacementAndItsOwnTarget() {
        void Refused(string item, string reason, string step) {
            var world = new Loaded();
            var outcome = world.Engine.Apply(Doc(item), "author/pack").Outcomes[Key];
            Assert(!outcome.Applied && outcome.Error == reason && world.Builder.Builds == 0, step + ": " + outcome.Error);
        }
        Refused(ModObject(Key, Station, Made, null, ",\"active\":true"), ModObjects.NeedsPlacement, "A mod object without a placement is refused");
        Refused(ModObject(Key, Station, Station + " copy", AtBryson), ModObjects.TargetShape, "A target without the mod object mark is refused");
        Refused("\"" + Key + "\":{\"target\":" + Target(Made, null) + ",\"source\":" + Target(Station) + ",\"placement\":{\"mode\":\"fixed\",\"position\":[1,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}",
            ModObjects.TargetShape, "A target of another structure than its source is refused");
        // The scene patch file reports such a modification when it is read, and keeps it.
        var file = ScenePatchFile.Load(Doc(ModObject(Key, Station, Made, null, ",\"active\":true")));
        Assert(file.CanWrite && file.Diagnostics.Count == 1 && file.Diagnostics[0].Message.Contains(ModObjects.NeedsPlacement.TrimEnd('.')), "Reading the file names the missing placement: " + String.Join("; ", file.Diagnostics.Select(d => d.Message)));
    }

    // In a world that stays loaded, a file that no longer holds the modification no longer has its mod object.
    static void AModObjectLeavesWithItsModification() {
        var world = new Loaded();
        world.Engine.Apply(Doc(ModObject(Key, Station, Made, AtBryson)), "author/pack");
        Assert(world.ModObject != null, "The mod object stands");
        world.Engine.Reenter();
        var result = world.Engine.Apply(Doc(), "author/pack");
        Assert(result.Error == null && world.ModObject == null && world.Builder.Removes == 1, "The mod object left with its modification");
        Assert(world.World.Objects[Station].Single() == world.Building, "The source stays");
    }

    internal static PatchEditingSession Session(Loaded world, out Store store, out NativeMapEditor editor) {
        store = new Store(); editor = new NativeMapEditor();
        var session = new PatchEditingSession(world.Engine, editor, store, "author/pack", "author/pack"); editor.Session = session;
        return session;
    }
    static JObject Modifications(PatchEditingSession session) => (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"];

    // Before the placing click the scene patch file and the scene hold nothing of the mod object. The click adds the
    // modification under a new key and builds the object at the point; Undo takes both away and Redo puts them back;
    // after Save, a new load builds the object at that point.
    static void PlacingAddsTheModificationAndTheObject() {
        var world = new Loaded(); var session = Session(world, out var store, out var editor);
        var changes = 0; session.ModObjectsChanged = () => changes++;
        Assert(Modifications(session).Count == 0 && world.World.Objects.Count == 1 && !session.IsDirty(false), "Nothing exists before the placing click");
        var placed = session.PlaceModObject(Id(Station), new Placement(AtBryson, Upright));
        Assert(placed.Applied && TargetIdentity.SameObject(placed.Target, Id(Made)) && ReferenceEquals(placed.ResolvedTarget, world.ModObject), "The placing click makes the mod object: " + placed.Error);
        var item = (JObject)Modifications(session)[Key];
        Assert(Modifications(session).Count == 1 && item != null, "The modification stands under the key the mod assigns: " + String.Join(", ", Modifications(session).Properties().Select(p => p.Name)));
        Assert((string)item["source"]["path"] == Station && (string)item["target"]["path"] == Made && (string)item["placement"]["mode"] == "fixed" && Near(item["placement"]["position"].Select(v => (double)v).ToArray(), AtBryson),
            "The modification holds its source, its own target and its placement: " + item.ToString(Newtonsoft.Json.Formatting.None));
        Assert(Near(world.At(world.ModObject), AtBryson) && session.IsModObject(Id(Made)) && !session.IsModObject(Id(Station)) && session.IsChanged(Id(Made)) && changes == 1, "The object stands at the point and is this mod's object");
        editor.Undo();
        Assert(Modifications(session).Count == 0 && world.ModObject == null && changes == 2 && session.IncompleteOperation == null, "Undo takes the modification and the object away: " + session.IncompleteOperation);
        editor.Redo();
        Assert(Modifications(session).Count == 1 && world.ModObject != null && Near(world.At(world.ModObject), AtBryson) && session.IncompleteOperation == null, "Redo puts both back: " + session.IncompleteOperation);
        session.BeginSave(); session.CompleteNativeSave();
        Assert(store.Patch != null && !session.IsDirty(false), "Save writes the scene patch file");
        var next = new Loaded();
        var result = next.Engine.Apply(store.Patch, "author/pack");
        Assert(result.Outcomes[Key].Applied && Near(next.At(next.ModObject), AtBryson), "After Save, the next load builds the mod object at that point: " + result.Outcomes[Key].Error);
        // A refused click leaves the file and the scene as they were.
        var refusing = new Loaded(); var refused = Session(refusing, out _, out _);
        refusing.World.ThrowAfterPlacement = true;
        var outcome = refused.PlaceModObject(Id(Station), new Placement(AtBryson, Upright));
        Assert(!outcome.Applied && Modifications(refused).Count == 0 && refusing.ModObject == null && !refused.IsDirty(false), "A refused placing click changes nothing: " + outcome.Error);
    }

    // A mod object takes the edits of its family: here a move and a disable, which stay in its own modification
    // with its source. A move back to where a load built it keeps the placement, which a mod object always has.
    static void EditsOfTheFamilyApplyToAModObject() {
        var world = new Loaded(); var session = Session(world, out _, out var editor);
        session.PlaceModObject(Id(Station), new Placement(AtBryson, Upright));
        var moved = new[] { 4500.0, 12, 5400 };
        var move = session.Edit(Id(Made), new Placement(moved, Upright), null);
        Assert(move.Applied && Near(world.At(world.ModObject), moved), "A mod object moves: " + move.Error);
        var disable = session.Edit(Id(Made), null, false);
        var item = (JObject)Modifications(session)[Key];
        Assert(disable.Applied && !world.ModObject.Active && Modifications(session).Count == 1 && (string)item["source"]["path"] == Station && (bool)item["active"] == false && Near(item["placement"]["position"].Select(v => (double)v).ToArray(), moved),
            "The edits stay in the mod object's own modification with its source: " + item.ToString(Newtonsoft.Json.Formatting.None));
        var back = session.Edit(Id(Made), new Placement(AtBryson, Upright), true);
        item = (JObject)Modifications(session)[Key];
        Assert(back.Applied && item["placement"] != null && item["source"] != null && item["active"] == null && world.ModObject.Active, "A mod object keeps its placement and its source: " + item.ToString(Newtonsoft.Json.Formatting.None));
        editor.Undo();
        Assert(!world.ModObject.Active && Near(world.At(world.ModObject), moved), "Undo returns the last edit");
        Assert(world.Building.Active && Near(world.At(world.Building), new[] { 1733.0, 9, 1473 }), "The source is unchanged");
    }

    // A mod object is duplicated from its own source, so it never depends on another mod object.
    static void AModObjectCanBeDuplicatedAgain() {
        var world = new Loaded(); var session = Session(world, out _, out _);
        session.PlaceModObject(Id(Station), new Placement(AtBryson, Upright));
        var second = session.PlaceModObject(Id(Made), new Placement(new[] { 2084.0, 12, -288 }, Upright));
        var item = (JObject)Modifications(session)[Key + "-2"];
        Assert(second.Applied && item != null && (string)item["source"]["path"] == Station && (string)item["target"]["path"] == ModObjects.Path(Station, "author/pack", Key + "-2") && ((JArray)item["dependsOn"]).Count == 0,
            "The second mod object has the first one's source and a key of its own: " + (second.Error ?? Modifications(session).ToString(Newtonsoft.Json.Formatting.None)));
        Assert(world.World.Objects.Count == 3 && world.Builder.Builds == 2, "Two mod objects stand beside the source");
    }

    // The plain scene of the engine tests with objects below a mod object: a build copies the source and what stands
    // below it.
    sealed class PlainScene : Scene, IModObjectScene {
        public int Removes;
        public object Build(TargetIdentity identity, object source, Placement placement) {
            var from = Objects.Single(p => p.Value.Contains(source)).Key;
            foreach (var pair in Objects.Where(p => p.Key == from || p.Key.StartsWith(from + "/", StringComparison.Ordinal)).ToArray()) {
                var box = (Box)pair.Value[0];
                Objects[identity.Path + pair.Key.Substring(from.Length)] = new List<object> { new Box { Position = (double[])box.Position.Clone(), Rotation = (double[])box.Rotation.Clone() } };
            }
            var made = (Box)Objects[identity.Path][0];
            made.Position = new[] { placement.Position[0] - Origin, placement.Position[1], placement.Position[2] }; made.Rotation = (double[])placement.Rotation.Clone();
            return made;
        }
        public void Remove(object modObject) {
            var path = Objects.Single(p => p.Value.Contains(modObject)).Key;
            foreach (var key in Objects.Keys.Where(k => k == path || k.StartsWith(path + "/", StringComparison.Ordinal)).ToArray()) Objects.Remove(key);
            Removes++;
        }
    }

    // Remove modification takes the mod object out of the scene, and its modification out of the scene patch file
    // together with the later edit of an object below it. Undo puts all of it back.
    static void RemoveTakesTheModObjectAndItsLaterEdits() {
        var scene = new PlainScene { Maps = new[] { "map" } };
        scene.Objects["Depot"] = new List<object> { new Box { Position = new[] { 1.0, 2, 3 }, Rotation = new[] { 0.0, 0, 0, 1 } } };
        scene.Objects["Depot/Clock"] = new List<object> { new Box { Position = new[] { 1.0, 5, 3 }, Rotation = new[] { 0.0, 0, 0, 1 } } };
        scene.Objects["Other"] = new List<object> { new Box { Position = new[] { 9.0, 2, 3 }, Rotation = new[] { 0.0, 0, 0, 1 } } };
        var editor = new NativeMapEditor(); var session = new PatchEditingSession(new PatchEngine(scene), editor, new Store(), "author/pack", "author/pack"); editor.Session = session;
        TargetIdentity At(string path) => new TargetIdentity("s", path, "c");
        var placed = session.PlaceModObject(At("Depot"), new Placement(new[] { 400.0, 2, 3 }, new[] { 0.0, 0, 0, 1 }));
        var made = placed.Target?.Path;
        Assert(placed.Applied && made == ModObjects.Path("Depot", "author/pack", "mod-object-depot"), "The depot is duplicated: " + placed.Error);
        Assert(session.Edit(At(made + "/Clock"), new Placement(new[] { 401.0, 6, 3 }, new[] { 0.0, 0, 0, 1 }), null).Applied && session.Edit(At("Other"), null, false).Applied, "A later edit of the object below it and an edit elsewhere apply");
        var clock = Modifications(session).Properties().Single(p => (string)p.Value["target"]["path"] == made + "/Clock");
        Assert(((JArray)clock.Value["dependsOn"]).Select(d => (string)d).SequenceEqual(new[] { "mod-object-depot" }), "The later edit applies after its mod object is built: " + clock.Value["dependsOn"].ToString(Newtonsoft.Json.Formatting.None));
        // A new load applies the later edit after the build, whatever the order of the keys.
        var reload = new PlainScene { Maps = new[] { "map" } };
        foreach (var path in new[] { "Depot", "Depot/Clock", "Other" }) reload.Objects[path] = new List<object> { new Box { Position = new[] { 1.0, 2, 3 }, Rotation = new[] { 0.0, 0, 0, 1 } } };
        var loaded = new PatchEngine(reload).Apply(session.ScenePatchFileText, "author/pack");
        Assert(loaded.Outcomes.Values.All(o => o.Applied) && ((Box)reload.Objects[made + "/Clock"][0]).Position[1] == 6, "A load builds the mod object and then applies the later edit: " + String.Join("; ", loaded.Outcomes.Where(o => !o.Value.Applied).Select(o => o.Key + " " + o.Value.Error)));
        var removed = session.Remove(At(made));
        Assert(removed.Applied && scene.Removes == 1 && !scene.Objects.ContainsKey(made) && !scene.Objects.ContainsKey(made + "/Clock"), "Remove modification removes the mod object: " + removed.Error);
        Assert(Modifications(session).Count == 1 && (string)Modifications(session).Properties().Single().Value["target"]["path"] == "Other", "Its modification and its later edit left the scene patch file; the edit elsewhere stays: " + Modifications(session).ToString(Newtonsoft.Json.Formatting.None));
        Assert(((Box)scene.Objects["Depot"][0]).Position[0] == 1 && ((Box)scene.Objects["Depot/Clock"][0]).Position[1] == 5, "The source and what stands below it are unchanged");
        editor.Undo();
        Assert(Modifications(session).Count == 3 && scene.Objects.ContainsKey(made) && ((Box)scene.Objects[made + "/Clock"][0]).Position[1] == 6 && session.IncompleteOperation == null, "Undo puts the mod object and its later edit back: " + session.IncompleteOperation);
        editor.Redo();
        Assert(Modifications(session).Count == 1 && !scene.Objects.ContainsKey(made), "Redo removes them again");
    }

    // The existing place rule lists a mod object where it stands: its path names its source's place, which does not
    // count, and it is no source of a place itself. Its kind is its source's kind.
    static void AModObjectIsListedWhereItStands() {
        var map = new BaseMapFixture();
        var station = map.Objects.Single(o => o.Path == Station && o.Listed);
        Assert(map.Places.Of(Station, station.At)?.Name == "Alarka Jct", "The station building of the fixture is listed under Alarka Jct");
        Assert(map.Places.Of(Made, new[] { AtBryson[0], AtBryson[2] })?.Name == "Bryson", "Placed at Bryson, the mod object is listed under Bryson: " + map.Places.Of(Made, new[] { AtBryson[0], AtBryson[2] })?.Name);
        Assert(map.Places.Of(Made + "/Mask", new[] { AtBryson[0], AtBryson[2] })?.Name == "Bryson", "So is an object below it");
        Assert(map.Places.Of(Made, station.At)?.Name == "Alarka Jct", "Beside its source it is listed under the source's place, by where it stands");
        var icon = ModObjects.Path(StationMapIconRules.IconsPath + "/Bryson", "author/pack", "mod-object-bryson");
        Assert(ScenePlaces.SourcesOf(StationMapIconRules.IconsPath + "/Bryson", new[] { 1.0, 2 }).Count() == 1 && !ScenePlaces.SourcesOf(icon, new[] { 1.0, 2 }).Any(), "A duplicated station map icon names no place");
        Assert(StationMapIconRules.Listed(icon), "A duplicated station map icon is a station map icon");
        var kind = SceneObjectRules.ClassOf(station.Node, Station, map.Places.StripPlaces).ToString();
        Assert(kind == "Station building / Stations" && SceneObjectRules.ClassOf(station.Node, Made, map.Places.StripPlaces).ToString() == kind, "The mod object has its source's kind: " + SceneObjectRules.ClassOf(station.Node, Made, map.Places.StripPlaces));
    }

    static void StationNameFollowsItsPlace() {
        var map = new BaseMapFixture();
        var destination = map.Objects.Single(o => o.TopSceneObject && o.Name == "Nantahala Station");
        var source = map.Objects.Single(o => o.Listed && o.Path == Station);
        var world = new Loaded();
        var session = Session(world, out var store, out _);
        var entries = map.Observations(out var facts);
        var mapStation = entries.Single(e => e.Identity.Path == Station);
        var otherStation = entries.Single(e => e.Identity.Path == destination.Path);
        string Label(SceneObservation entry, IReadOnlyList<SceneObservation> all) {
            var tree = OutlinerGrouping.Build(all, all, "", false, null);
            IEnumerable<InventoryPresentationState.OutlineNode> Nodes(IEnumerable<InventoryPresentationState.OutlineNode> nodes) =>
                nodes.SelectMany(node => new[] { node }.Concat(Nodes(node.Children)));
            return Nodes(tree.Tops).Single(node => node.Row == entry).Label;
        }
        SceneObservation ObserveModObject(Loaded loaded) {
            var entry = new SceneObservation(loaded.ModObject, Id(Made), source.Family, true, loaded.At(loaded.ModObject), Upright,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "Placement");
            facts[entry] = source;
            return entry;
        }
        OutlinerGrouping.InvalidatePlaces();
        OutlinerGrouping.Scene = map.Scene(facts);
        try {
            Assert(Label(mapStation, entries) == "Alarka Jct Station" && Label(otherStation, entries) == "Nantahala Station", "The map stations show their game names before placement");
            var placed = session.PlaceModObject(Id(Station), new Placement(new[] { destination.At[0], 12, destination.At[1] }, Upright));
            Assert(placed.Applied, "The station is placed: " + placed.Error);
            var modStation = ObserveModObject(world);
            var afterPlacement = entries.Concat(new[] { modStation }).ToArray();
            Assert(Label(modStation, afterPlacement) == "Nantahala Station (2)", "The placed station is named Nantahala Station (2), got " + Label(modStation, afterPlacement));
            Assert(Label(mapStation, afterPlacement) == "Alarka Jct Station" && Label(otherStation, afterPlacement) == "Nantahala Station", "Both game stations retain their original shown names");
            var identity = Modifications(session)[Key]["target"].DeepClone();
            var moved = session.Edit(placed.Target, new Placement(AtBryson, Upright), null);
            Assert(moved.Applied, "The station moves: " + moved.Error);
            var movedStation = ObserveModObject(world);
            var afterMove = entries.Concat(new[] { movedStation }).ToArray();
            Assert(Label(movedStation, afterMove) == "Bryson Station (1)", "The moved station is named Bryson Station (1), got " + Label(movedStation, afterMove));
            Assert(Modifications(session).Count == 1 && JToken.DeepEquals(identity, Modifications(session)[Key]["target"]), "Moving preserves the scene patch file's modification key and target");
            session.BeginSave();
            session.CompleteNativeSave();
            var reloaded = new Loaded();
            var rebuilt = reloaded.Engine.Apply(store.Patch, "author/pack");
            Assert(rebuilt.Outcomes[Key].Applied, "The saved scene patch file rebuilds the moved station");
            var rebuiltStation = ObserveModObject(reloaded);
            Assert(Label(rebuiltStation, entries.Concat(new[] { rebuiltStation }).ToArray()) == "Bryson Station (1)", "Loading preserves the move and yields the same shown name");
        } finally {
            OutlinerGrouping.Scene = null;
            OutlinerGrouping.InvalidatePlaces();
        }
    }

    // Save writes a mod object's source directly after its target, each in the order of a target.
    static void CanonicalSaveOrdersAModObject() {
        var world = new Loaded(); var session = Session(world, out var store, out _);
        session.PlaceModObject(Id(Station), new Placement(AtBryson, Upright));
        session.BeginSave(); session.CompleteNativeSave();
        var item = (JObject)JObject.Parse(store.Patch)["modifications"][Key];
        Assert(item.Properties().Select(p => p.Name).SequenceEqual(new[] { "target", "source", "placement", "dependsOn" }), "The members stand in the canonical order: " + String.Join(", ", item.Properties().Select(p => p.Name)));
        Assert(((JObject)item["source"]).Properties().Select(p => p.Name).SequenceEqual(new[] { "scene", "path", "component", "structure" }), "The source is written as a target is");
        Assert(session.MinimumPatcherVersion == ModObjects.RequiredVersion, "A mod object needs the patcher version that builds it: " + session.MinimumPatcherVersion);
    }
}
