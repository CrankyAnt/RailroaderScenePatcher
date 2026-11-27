// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// A duplicated spawn point: a teleport point under the name its modder gives it. The teleport name is a value of the
// copy's own modification and becomes the copy's object name in the game, by which the console's teleport command and
// the quick search find it; the target path, ending with the mark, stays its identity. The copy takes a priority below
// every map spawn point and keeps its source's radius; when its original is missing, the first loaded spawn point in
// the fixed order is its source, never another copy. The spawn points are those of the base map fixture.
static class SpawnPointCopyTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Spawn point copies: " + message); }
    const string Scene = "level4", Alarka = "World/POIs/Alarka", AlarkaJct = "World/POIs/Alarka Jct", Rhodo = "World/POIs/Rhodo", Key = "mod-object-alarka", Mod = "author/pack";
    static readonly string Made = ModObjects.Path(Alarka, Mod, Key);
    static readonly double[] Upright = { 0, 0, 0, 1 }, AtBryson = { 4493, 12, 5415 };
    static TargetIdentity Id(string path) => new TargetIdentity(Scene, path, SpawnPoints.Component, SpawnPoints.Structure);
    static readonly BaseMapFixture Map = new BaseMapFixture();
    // The spawn points of the base map fixture, in the fixed order: by hierarchy path in ordinal order.
    static readonly string[] MapSpawns = Map.Objects.Where(o => o.Listed && o.Family == SpawnPoints.Family).Select(o => o.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    // Priorities the fixture does not record: Alarka is the default start, Rhodo the lowest, the others 0.
    static int PriorityOf(string path) => path == Alarka ? 10 : path == Rhodo ? -5 : 0;

    // A scene spawn point with the fields of Character.SpawnPoint and its object name.
    sealed class Spawn : FakeSpawn { public int Priority; public double Radius = 3; public string Name; }
    // The loaded map with its spawn points, and the host that builds copies as the native world does: a copy keeps the
    // source's fields, as Object.Instantiate copies them, and stands under its target path, by which the scene finds it
    // whatever its object name.
    sealed class World : IModObjectScene, IModObjectSources, IModObjectNames, ISpawnPointPriorities {
        public readonly FakeSpawnWorld Spawns = new FakeSpawnWorld();
        public readonly List<KeyValuePair<string, Spawn>> Loaded = new List<KeyValuePair<string, Spawn>>();
        public readonly Dictionary<Spawn, string> Marks = new Dictionary<Spawn, string>();
        public readonly List<object> BuiltFrom = new List<object>();
        public readonly PatchEngine Engine;
        // A host that wrongly lists the copies it built first among the identical spawn points, under their object names.
        public bool ListsCopies;
        public World(IEnumerable<string> missing = null) {
            foreach (var path in MapSpawns.Except(missing ?? Enumerable.Empty<string>())) {
                var spawn = new Spawn { Priority = PriorityOf(path), Radius = path == Alarka ? 7.5 : 3, Name = ModObjects.NameOf(path) };
                Spawns.Objects[path] = new List<object> { spawn };
                Loaded.Add(new KeyValuePair<string, Spawn>(path, spawn));
            }
            var scene = new SceneCapabilities(() => Spawns.MapIds, null) { ModObjectWorld = this, SpawnPriorities = this };
            scene.Add(new SpawnPointScene(Spawns, Capability.Unchecked));
            Engine = new PatchEngine(scene);
        }
        public Spawn At(string path) => Spawns.Objects.TryGetValue(path, out var found) && found.Count == 1 ? (Spawn)found[0] : null;
        public Spawn Copy => Marks.Keys.SingleOrDefault();
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) {
            if (At(identity.Path) != null) throw new InvalidOperationException(ModObjects.Taken);
            var from = (Spawn)source;
            var made = new Spawn { Position = new[] { placement.Position[0] - Spawns.Origin, placement.Position[1], placement.Position[2] }, Rotation = (double[])placement.Rotation.Clone(),
                Priority = from.Priority, Radius = from.Radius, Name = ModObjects.NameOf(identity.Path) };
            Spawns.Objects[identity.Path] = new List<object> { made };
            Marks[made] = made.Name;
            BuiltFrom.Add(source);
            return made;
        }
        public void Remove(object modObject) {
            foreach (var pair in Spawns.Objects.Where(p => p.Value.Contains(modObject)).ToArray()) Spawns.Objects.Remove(pair.Key);
            Marks.Remove((Spawn)modObject);
        }
        public string BuildOf(object source) => source is Spawn ? "v1:1:spawn-point" : null;
        public IReadOnlyList<SourceInstance> Identical(TargetIdentity source, string build) =>
            (ListsCopies ? Marks.Keys.Select(c => new SourceInstance("World/POIs/" + c.Name, c)) : Enumerable.Empty<SourceInstance>())
                .Concat(Loaded.Where(p => At(p.Key) != null).Select(p => new SourceInstance(p.Key, p.Value))).ToArray();
        public void Name(object modObject, string name) { var copy = (Spawn)modObject; copy.Name = name ?? Marks[copy]; }
        public string NameOf(object modObject) => ((Spawn)modObject).Name;
        public IReadOnlyList<int> MapPriorities(string scene) => Loaded.Where(p => At(p.Key) != null).Select(p => p.Value.Priority).ToArray();
        public int Priority(object spawnPoint) => ((Spawn)spawnPoint).Priority;
        public void SetPriority(object spawnPoint, int priority) => ((Spawn)spawnPoint).Priority = priority;
    }
    static PatchEditingSession Session(World world, out Store store, out NativeMapEditor editor) {
        store = new Store(); editor = new NativeMapEditor();
        var session = new PatchEditingSession(world.Engine, editor, store, Mod, Mod); editor.Session = session;
        return session;
    }
    static JObject Modifications(PatchEditingSession session) => (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"];
    static string Target(string path) => "{\"scene\":\"" + Scene + "\",\"path\":\"" + path + "\",\"component\":\"" + SpawnPoints.Component + "\",\"structure\":\"" + SpawnPoints.Structure + "\"}";
    static string ModObject(string key, string source, string extra = "") =>
        "\"" + key + "\":{\"target\":" + Target(ModObjects.Path(source, Mod, key)) + ",\"source\":" + Target(source).TrimEnd('}') + ",\"build\":\"v1:1:spawn-point\"}"
        + ",\"placement\":{\"mode\":\"fixed\",\"position\":[4493,12,5415],\"rotation\":[0,0,0,1]}" + extra + ",\"dependsOn\":[]}";
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";

    internal static void Run() {
        DuplicateIsActiveOnASpawnPoint();
        PlacingLeavesTheTeleportNameEmpty();
        TheTeleportNameIsTheObjectName();
        ASlashIsRefusedWithItsReason();
        MovingAndRenamingKeepTheIdentity();
        TheTeleportNameHoldsThroughSaveAndReload();
        OnlyADuplicatedSpawnPointTakesATeleportName();
        TheCopyRanksBelowEveryMapSpawnPointWithItsSourceRadius();
        AMissingOriginalFallsBackToTheFirstMapSpawnPoint();
        NoCopyIsTheFallbackSourceOfAnother();
        TheObjectTabShowsTheFieldAndTheNote();
        TheOutlinerAndTheHeadingShowTheTeleportName();
        Console.WriteLine("PASS: spawn point copies");
    }

    // Duplicate is active on a spawn point of the base map.
    static void DuplicateIsActiveOnASpawnPoint() {
        var rows = Map.Observations(out _).Where(r => r.Family == SpawnPoints.Family).ToArray();
        Assert(rows.Length == MapSpawns.Length && rows.Length > 0, "The fixture lists its spawn points");
        foreach (var row in rows) Assert(DuplicateState.Reason(row, new[] { SpawnPoints.Family }) == null, "Duplicate is active on " + row.Identity.Path + ": " + DuplicateState.Reason(row, new[] { SpawnPoints.Family }));
    }

    // The placing click makes the copy with no teleport name: the file holds none and the copy's object name is the
    // name its target path gives it.
    static void PlacingLeavesTheTeleportNameEmpty() {
        var world = new World(); var session = Session(world, out _, out _);
        var placed = session.PlaceModObject(Id(Alarka), new Placement(AtBryson, Upright));
        Assert(placed.Applied && TargetIdentity.SameObject(placed.Target, Id(Made)), "The spawn point is duplicated: " + placed.Error);
        var item = (JObject)Modifications(session)[Key];
        Assert(item != null && item[ModObjects.TeleportNameMember] == null && session.TeleportNameOf(Id(Made)) == null, "The copy has no teleport name after placing: " + item);
        Assert(world.Copy.Name == ModObjects.NameOf(Made), "Its object name is the name its target path gives it: " + world.Copy.Name);
    }

    // Set teleport name writes the name into the copy's modification and gives the copy that object name; the target
    // path stays. Undo and Redo follow; an empty name clears it.
    static void TheTeleportNameIsTheObjectName() {
        var world = new World(); var session = Session(world, out _, out var editor);
        session.PlaceModObject(Id(Alarka), new Placement(AtBryson, Upright));
        var named = session.SetTeleportName(Id(Made), "  Bryson Yard ", null);
        var item = (JObject)Modifications(session)[Key];
        Assert(named.Applied && (string)item[ModObjects.TeleportNameMember] == "Bryson Yard" && session.TeleportNameOf(Id(Made)) == "Bryson Yard", "The teleport name is a value of the copy's modification: " + named.Error + " " + item);
        Assert(world.Copy.Name == "Bryson Yard", "The copy's object name in the game is the teleport name: " + world.Copy.Name);
        Assert((string)item["target"]["path"] == Made && Modifications(session).Count == 1, "The target path, ending with the mark, stays the copy's identity");
        editor.Undo();
        Assert(world.Copy.Name == ModObjects.NameOf(Made) && session.TeleportNameOf(Id(Made)) == null, "Undo returns the mark name: " + world.Copy.Name);
        editor.Redo();
        Assert(world.Copy.Name == "Bryson Yard" && session.TeleportNameOf(Id(Made)) == "Bryson Yard", "Redo gives the teleport name again: " + world.Copy.Name);
        var cleared = session.SetTeleportName(Id(Made), "", null);
        Assert(cleared.Applied && world.Copy.Name == ModObjects.NameOf(Made) && Modifications(session)[Key][ModObjects.TeleportNameMember] == null, "An empty field clears the teleport name: " + cleared.Error);
    }

    // A teleport name with a slash is refused with its reason and changes nothing; a scene patch file that holds one
    // fails that copy with the reason.
    static void ASlashIsRefusedWithItsReason() {
        var world = new World(); var session = Session(world, out _, out _);
        session.PlaceModObject(Id(Alarka), new Placement(AtBryson, Upright));
        session.SetTeleportName(Id(Made), "Bryson Yard", null);
        var before = session.ScenePatchFileText;
        var refused = session.SetTeleportName(Id(Made), "Bryson/Yard", null);
        Assert(!refused.Applied && refused.Error != null && refused.Error.Contains(ModObjects.TeleportNameSlash), "A slash is refused with its reason: " + refused.Error);
        Assert(session.ScenePatchFileText == before && world.Copy.Name == "Bryson Yard", "The refusal changes nothing");
        Assert(TeleportNameField.Refusal("Bryson/Yard") == ModObjects.TeleportNameSlash && TeleportNameField.Refusal(" Bryson Yard ") == null && TeleportNameField.Refusal("") == null, "The field names the same reason");
        var load = new World().Engine.Apply(Doc(ModObject(Key, Alarka, ",\"teleportName\":\"Bryson/Yard\"")), Mod).Outcomes[Key];
        Assert(!load.Applied && load.Error == ModObjects.TeleportNameSlash && load.Cause == FailureCause.InvalidDefinition, "A file with a slash in the teleport name fails that copy: " + load.Error);
    }

    // A move and a disable keep the teleport name; a rename leaves the target path and the modifications that name the
    // copy as they are.
    static void MovingAndRenamingKeepTheIdentity() {
        var world = new World(); var session = Session(world, out _, out var editor);
        session.PlaceModObject(Id(Alarka), new Placement(AtBryson, Upright));
        session.SetTeleportName(Id(Made), "Bryson Yard", null);
        var moved = new[] { 4520.0, 12, 5390 };
        var move = session.Edit(Id(Made), new Placement(moved, Upright), null);
        var disable = session.Edit(Id(Made), null, false);
        var item = (JObject)Modifications(session)[Key];
        Assert(move.Applied && disable.Applied && (string)item[ModObjects.TeleportNameMember] == "Bryson Yard" && world.Copy.Name == "Bryson Yard" && !world.Copy.Active,
            "Moving and disabling keep the teleport name: " + move.Error + disable.Error + " " + item);
        var target = item["target"].DeepClone(); var source = item["source"].DeepClone(); var placement = item["placement"].DeepClone();
        var renamed = session.SetTeleportName(Id(Made), "Whittier Siding", null);
        item = (JObject)Modifications(session)[Key];
        Assert(renamed.Applied && JToken.DeepEquals(target, item["target"]) && JToken.DeepEquals(source, item["source"]) && JToken.DeepEquals(placement, item["placement"]) && (bool)item["active"] == false,
            "A rename leaves the target path, the source, the placement and the activity: " + item);
        editor.Undo();
        Assert(world.Copy.Name == "Bryson Yard" && !world.Copy.Active, "Undo of the rename returns the earlier name only");
    }

    // After Save and a map reload the copy is built again under its mark with its teleport name.
    static void TheTeleportNameHoldsThroughSaveAndReload() {
        var world = new World(); var session = Session(world, out var store, out _);
        session.PlaceModObject(Id(Alarka), new Placement(AtBryson, Upright));
        session.SetTeleportName(Id(Made), "Bryson Yard", null);
        session.BeginSave(); session.CompleteNativeSave();
        Assert(store.Patch != null && store.Patch.Contains("\"teleportName\": \"Bryson Yard\""), "Save writes the teleport name: " + store.Patch);
        var next = new World();
        var result = next.Engine.Apply(store.Patch, Mod);
        Assert(result.Outcomes[Key].Applied && next.At(Made) != null && next.At(Made).Name == "Bryson Yard", "The reload builds the copy under its mark with its teleport name: " + result.Outcomes[Key].Error);
        var nextStore = new Store { Patch = store.Patch }; var nextEditor = new NativeMapEditor();
        var nextSession = new PatchEditingSession(next.Engine, nextEditor, nextStore, Mod, Mod); nextEditor.Session = nextSession;
        Assert(nextSession.TeleportNameOf(Id(Made)) == "Bryson Yard", "The reloaded file names the teleport name");
        var edit = nextSession.Edit(Id(Made), new Placement(new[] { 4500.0, 12, 5400 }, Upright), null);
        Assert(edit.Applied && next.At(Made).Name == "Bryson Yard", "The copy is edited by its mark after the reload: " + edit.Error);
    }

    // Only a duplicated spawn point takes a teleport name: a map spawn point and another family refuse it.
    static void OnlyADuplicatedSpawnPointTakesATeleportName() {
        var world = new World();
        var map = "\"move-alarka\":{\"target\":" + Target(Alarka) + ",\"teleportName\":\"Home\",\"placement\":{\"mode\":\"fixed\",\"position\":[5210,650,-3100],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var outcome = world.Engine.Apply(Doc(map), Mod).Outcomes["move-alarka"];
        Assert(!outcome.Applied && outcome.Error == ModObjects.TeleportNameOnlySpawnPoints, "A map spawn point's modification refuses a teleport name: " + outcome.Error);
        var session = Session(world, out _, out _);
        var refused = session.SetTeleportName(Id(Alarka), "Home", null);
        Assert(!refused.Applied && refused.Error.Contains(ModObjects.TeleportNameOnlySpawnPoints) && world.At(Alarka).Name == "Alarka", "Set teleport name refuses a map spawn point: " + refused.Error);
        Assert(ModObjects.TakesTeleportName(SpawnPoints.Structure) && !ModObjects.TakesTeleportName(SceneObjects.Structure), "Only the spawn point structure takes a teleport name");
    }

    // The copy ranks below every map spawn point, Rhodo's -5 included, so no new game starts there, and keeps its
    // source's radius. A copy of a copy is built from the map spawn point.
    static void TheCopyRanksBelowEveryMapSpawnPointWithItsSourceRadius() {
        var world = new World(); var session = Session(world, out _, out _);
        session.PlaceModObject(Id(Alarka), new Placement(AtBryson, Upright));
        var copy = world.Copy;
        Assert(copy.Priority == -6 && world.Loaded.All(p => p.Value.Priority > copy.Priority), "The copy has a lower priority than every map spawn point: " + copy.Priority);
        Assert(copy.Radius == 7.5, "The copy keeps its source's radius: " + copy.Radius);
        Assert(SpawnPointCopies.Priority(new[] { 3, 1 }, 9) == 0 && SpawnPointCopies.Priority(new int[0], 4) == 3 && SpawnPointCopies.Priority(new[] { Int32.MinValue }, 0) == Int32.MinValue, "The rule: one below the lowest");
        var again = session.PlaceModObject(Id(Made), new Placement(new[] { 4600.0, 12, 5400 }, Upright));
        var second = (JObject)Modifications(session).Properties().Single(p => p.Name != Key).Value;
        Assert(again.Applied && (string)second["source"]["path"] == Alarka && second[ModObjects.TeleportNameMember] == null, "A copy of a copy names the map spawn point as its source: " + again.Error + " " + second);
        Assert(world.Marks.Keys.All(c => c.Priority == -6), "Every copy ranks below the map spawn points");
        // The new-game route before the world load leaves copies to the world load.
        var early = new World().Engine.BeforeNewGameStart(Doc(ModObject(Key, Alarka)), Mod);
        Assert(!early.Outcomes.ContainsKey(Key) && early.EarlyRouteUnavailable == null, "No copy is built before a new game starts");
    }

    // With Alarka missing, the copy is built from the first loaded spawn point in the fixed order, Alarka Jct, not a
    // nearer one, with the fallback warning.
    static void AMissingOriginalFallsBackToTheFirstMapSpawnPoint() {
        var world = new World(new[] { Alarka });
        ScenePatchFileResult result = null;
        var log = RecordingLog.Capture(false, () => result = world.Engine.Apply(Doc(ModObject(Key, Alarka, ",\"teleportName\":\"Bryson Yard\"")), Mod));
        Assert(MapSpawns.First(p => p != Alarka) == AlarkaJct, "Alarka Jct is the first loaded spawn point in the fixed order");
        Assert(result.Outcomes[Key].Applied && ReferenceEquals(world.BuiltFrom.Single(), world.At(AlarkaJct)), "The copy is built from Alarka Jct: " + result.Outcomes[Key].Error);
        Assert(world.At(Made).Name == "Bryson Yard" && world.At(Made).Priority == -6 && world.At(Made).Radius == 3, "It takes its teleport name, ranks below the map, and keeps the radius of the spawn point it is built from");
        Assert(log.At(LogLevel.Warning).Single() == ModObjects.FallbackWarning(Mod, Key, Alarka, AlarkaJct), "The Player log names the fallback: " + String.Join(" | ", log.PlayerLines));
    }

    // A copy never stands in for another copy's original, even where the host lists copies first and the copy's
    // object name holds no mark.
    static void NoCopyIsTheFallbackSourceOfAnother() {
        var world = new World(new[] { Alarka }) { ListsCopies = true };
        var result = world.Engine.Apply(Doc(ModObject("mod-object-a", AlarkaJct, ",\"teleportName\":\"Aaa\""), ModObject("mod-object-b", Alarka)), Mod);
        Assert(result.Outcomes["mod-object-a"].Applied && result.Outcomes["mod-object-b"].Applied, "Both copies stand: " + result.Outcomes["mod-object-a"].Error + result.Outcomes["mod-object-b"].Error);
        Assert(world.BuiltFrom.Count == 2 && world.BuiltFrom.All(s => world.Loaded.Any(p => ReferenceEquals(p.Value, s))) && ReferenceEquals(world.BuiltFrom[1], world.At(AlarkaJct)),
            "The second copy is built from the map spawn point Alarka Jct, not from the first copy");
    }

    sealed class Listing : IInspectionScene {
        public IReadOnlyList<SceneObservation> Objects = new SceneObservation[0];
        public IEnumerable<string> MapIds => new[] { "map" };
        public IEnumerable<SceneObservation> Discover() => Objects;
    }
    static SceneObservation CopyRow(string name, int priority = -6) =>
        new SceneObservation(new object(), Id(Made), SpawnPoints.Family, true, AtBryson, Upright, new[] { SpawnPoints.NameBinding(name), SpawnPoints.PriorityBinding(priority) },
            new string[0], new string[0], SpawnPoints.Operations);

    // The Object tab of a duplicated spawn point shows the teleport name field, empty after placing with the note that
    // the copy is very hard to reach; with a name the field holds it and the note is gone. A map spawn point has no field.
    static void TheObjectTabShowsTheFieldAndTheNote() {
        var placed = CopyRow(ModObjects.NameOf(Made));
        Assert(TeleportNameField.Shows(placed) && TeleportNameField.Value(placed) == "" && TeleportNameField.Note(placed) == TeleportNameField.EmptyNote, "After placing the field is empty and the note shows");
        var named = CopyRow("Bryson Yard");
        Assert(TeleportNameField.Value(named) == "Bryson Yard" && TeleportNameField.Note(named) == null, "With a teleport name the field holds it and the note is gone");
        var map = Map.Observations(out _).First(r => r.Family == SpawnPoints.Family);
        Assert(!TeleportNameField.Shows(map) && TeleportNameField.Note(map) == null && SpawnPoints.TeleportName(map) == null, "A map spawn point has no teleport name field");
        Assert(TeleportNameField.EmptyNote.Contains("full object name") && TeleportNameField.EmptyNote.Contains("hard to enter"), "The note says the copy is reachable only by its full object name, which is hard to enter");
        Assert(TeleportNameField.Tip.Contains("quotation marks") && TeleportNameField.Tip.Contains("/tp"), "The tooltip names the console command and the quotation marks for a space");
    }

    // Once the teleport name is set, the outliner and the inspector heading show it, as they show a map spawn point's
    // own name; without one the copy is named like the map's spawn points at its place, with a number.
    static void TheOutlinerAndTheHeadingShowTheTeleportName() {
        var map = new BaseMapFixture();
        var entries = map.Observations(out var facts);
        var source = map.Objects.Single(o => o.Listed && o.Path == Alarka);
        string Label(SceneObservation entry, IReadOnlyList<SceneObservation> all) {
            var tree = OutlinerGrouping.Build(all, all, "", false, null);
            IEnumerable<InventoryPresentationState.OutlineNode> Nodes(IEnumerable<InventoryPresentationState.OutlineNode> nodes) => nodes.SelectMany(node => new[] { node }.Concat(Nodes(node.Children)));
            return Nodes(tree.Tops).Single(node => node.Row == entry).Label;
        }
        OutlinerGrouping.InvalidatePlaces();
        OutlinerGrouping.Scene = map.Scene(facts);
        try {
            var unnamed = CopyRow(ModObjects.NameOf(Made)); facts[unnamed] = source;
            var all = entries.Concat(new[] { unnamed }).ToArray();
            Assert(Label(unnamed, all) != ModObjects.NameOf(Made) && !Label(unnamed, all).Contains("[mod object"), "Without a teleport name the copy has a generated name: " + Label(unnamed, all));
            var named = CopyRow("Bryson Yard"); facts[named] = source;
            all = entries.Concat(new[] { named }).ToArray();
            Assert(Label(named, all) == "Bryson Yard", "The outliner shows the teleport name: " + Label(named, all));
            var inventory = new SceneInventory(new Listing { Objects = all }); inventory.Refresh();
            Assert(OutlinerGrouping.InspectorName(named, inventory, null) == "Bryson Yard", "The inspector heading shows the teleport name: " + OutlinerGrouping.InspectorName(named, inventory, null));
            var alarka = entries.Single(e => e.Identity.Path == Alarka);
            Assert(Label(alarka, all) == "Alarka", "The map spawn point keeps its own name");
        } finally { OutlinerGrouping.InvalidatePlaces(); OutlinerGrouping.Scene = null; }
    }
}
