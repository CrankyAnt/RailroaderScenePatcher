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
    // A part of the station building: its platform, listed as the station's Mask.
    internal const string Part = Station + "/Mask";
    internal static TargetIdentity PartId(string path) => new TargetIdentity(SceneName, path, Component, ChildObjects.Structure);
    internal static TargetIdentity Id(string path) => new TargetIdentity(SceneName, path, Component, SceneObjects.Structure);
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) => a != null && b != null && a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= 0.001);

    // What builds mod objects in the controlled group world: a new group with the source's parts, placed where asked.
    internal sealed class Builder : IModObjectScene {
        readonly FakeGroupWorld world;
        public int Builds, Removes;
        public Builder(FakeGroupWorld world) { this.world = world; }
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) {
            if (world.Objects.TryGetValue(identity.Path, out var standing) && standing.Count != 0) throw new InvalidOperationException(ModObjects.Taken);
            // A part's copy is a group of its own beside the part's group, with no parts and no culler of the group.
            var part = source as FakePart;
            var from = part?.Group ?? (FakeGroup)source;
            var made = new FakeGroup { Position = new[] { placement.Position[0] - world.Origin, placement.Position[1], placement.Position[2] }, Rotation = (double[])placement.Rotation.Clone(), Parent = from.Parent };
            if (part != null) { made.Parts.Clear(); made.Cullers.Clear(); made.RootCulling = null; }
            // The copy holds the source's parts. It stands fully built: a part a switch of the game sets is on, and no
            // switch sets the copy's parts.
            else {
                made.RailBridge = from.RailBridge;
                made.Parts.Clear();
                foreach (var p in from.Parts) made.Add(new FakePart { Id = p.Id, Offset = (double[])p.Offset.Clone(), Rotation = (double[])p.Rotation.Clone(), Culled = p.Culled, Active = p.Active || p.Constructed });
            }
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
            World.PartsAt[Part] = new List<object> { Building.Parts[0] };
            var holds = new PartHolds();
            var scene = new SceneCapabilities(() => World.MapIds, null) { ModObjectWorld = Builder = new Builder(World) };
            scene.Add(new SceneObjects(World, holds: holds)); scene.Add(new ChildObjects(World, holds));
            Engine = new PatchEngine(scene);
        }
        public FakeGroup At(string path) => World.Objects.TryGetValue(path, out var found) && found.Count == 1 ? found[0] as FakeGroup : null;
        public FakeGroup ModObject => World.Objects.TryGetValue(Made, out var found) && found.Count == 1 ? (FakeGroup)found[0] : null;
        public double[] At(FakeGroup group) => World.Observe(group).Root.Placement.Position.ToArray();
    }

    internal static void Run() {
        PathsOfModObjects();
        EachLoadBuildsTheModObjectFromItsSource();
        AModObjectWithoutItsSourceFailsAlone();
        AModObjectNeedsItsPlacementAndItsOwnTarget();
        AModObjectLeavesWithItsModification();
        EachModBuildsItsOwnModObjectUnderTheSameKey();
        PlacingAddsTheModificationAndTheObject();
        EditsOfTheFamilyApplyToAModObject();
        AModObjectCanBeDuplicatedAgain();
        APartIsDuplicatedAsAStandaloneModObject();
        ALocationSignCopyRebuildsItsGroundMaskWhereItStands();
        ABridgeCopyStandsFullyBuilt();
        TheElaBridgeIsDuplicated();
        ARoadBridgeIsDuplicated();
        RemoveTakesTheModObjectAndItsLaterEdits();
        AModObjectIsListedWhereItStands();
        StationNameFollowsItsPlace();
        PlacedModObjectsKeepTheirNumbers();
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

    // #38/#160: a mod object's identity is scoped to the mod that supplies its scene patch file, as an Equipment ID is.
    // Two mods whose files hold a mod object under the same modification key each build their own, with their own
    // mark; each leaves with its own file. A file whose mod object carries another mod's mark is refused with the
    // target shape and builds nothing, so one mod never takes another mod's mod object path.
    static void EachModBuildsItsOwnModObjectUnderTheSameKey() {
        const string first = "author/pack", second = "other/rail-extras";
        string Mark(string mod) => ModObjects.Path(Station, mod, Key);
        var world = new Loaded();
        var mine = world.Engine.Apply(Doc(ModObject(Key, Station, Mark(first), AtBryson)), first).Outcomes[Key];
        var theirs = world.Engine.Apply(Doc(ModObject(Key, Station, Mark(second), new[] { 4500.0, 12, 5420 })), second).Outcomes[Key];
        Assert(mine.Applied && theirs.Applied && world.Builder.Builds == 2, "Both mods build their mod object under the same key: " + mine.Error + " / " + theirs.Error);
        Assert(Mark(second) == Station + " [mod object other:rail-extras " + Key + "]" && world.World.Objects[Mark(first)].Count == 1 && world.World.Objects[Mark(second)].Count == 1
            && !ReferenceEquals(world.World.Objects[Mark(first)][0], world.World.Objects[Mark(second)][0]), "Each mod object stands under its own mod's mark");
        Assert(ModObjects.MadeBy(Mark(first), first) && !ModObjects.MadeBy(Mark(first), second) && ModObjects.MadeBy(Mark(second), second) && !ModObjects.MadeBy(Mark(second), first), "Each mod object is its own mod's");
        world.Engine.Reenter();
        var left = world.Engine.Apply(Doc(), second);
        Assert(left.Error == null && world.Builder.Removes == 1 && world.World.Objects.ContainsKey(Mark(first)) && !world.World.Objects.ContainsKey(Mark(second)), "The second mod's mod object leaves with its file; the first mod's stays");
        // A file of the second mod that writes the first mod's mark.
        var borrowed = new Loaded();
        var refused = borrowed.Engine.Apply(Doc(ModObject(Key, Station, Mark(first), AtBryson)), second).Outcomes[Key];
        Assert(!refused.Applied && refused.Error == ModObjects.TargetShape && refused.Cause == FailureCause.InvalidDefinition && borrowed.Builder.Builds == 0, "Another mod's mark is refused: " + refused.Error);
        // A mark whose modification ID differs from the key is refused too.
        var renamed = new Loaded();
        var other = renamed.Engine.Apply(Doc(ModObject(Key, Station, ModObjects.Path(Station, first, Key + "-2"), AtBryson)), first).Outcomes[Key];
        Assert(!other.Applied && other.Error == ModObjects.TargetShape && renamed.Builder.Builds == 0, "A mark with another modification ID is refused: " + other.Error);
        // The refusal holds whatever stands: with the first mod's mod object standing, the second mod's file that writes
        // its mark neither resolves to it nor moves it.
        var standing = new Loaded();
        Assert(standing.Engine.Apply(Doc(ModObject(Key, Station, Mark(first), AtBryson)), first).Outcomes[Key].Applied && standing.ModObject != null, "The first mod's mod object stands");
        var taken = standing.Engine.Apply(Doc(ModObject(Key, Station, Mark(first), new[] { 4500.0, 12, 5420 }, ",\"active\":false")), second).Outcomes[Key];
        Assert(!taken.Applied && taken.Error == ModObjects.TargetShape && taken.Cause == FailureCause.InvalidDefinition && standing.Builder.Builds == 1
            && Near(standing.At(standing.ModObject), AtBryson) && standing.ModObject.Active, "Another mod's mark is refused while that mod's mod object stands, which keeps its placement and activity: " + taken.Error);
        // In the same mod, a modification whose mark names the key of a standing mod object neither resolves to it nor moves it.
        var sibling = standing.Engine.Apply(Doc(ModObject(Key, Station, Mark(first), AtBryson), ModObject(Key + "-2", Station, Mark(first), new[] { 4500.0, 12, 5420 })), first).Outcomes;
        Assert(sibling[Key].Applied && !sibling[Key + "-2"].Applied && sibling[Key + "-2"].Error == ModObjects.TargetShape && standing.Builder.Builds == 1
            && Near(standing.At(standing.ModObject), AtBryson), "A mark with another modification ID is refused while that mod object stands, which keeps its placement: " + sibling[Key + "-2"].Error);
    }

    internal static PatchEditingSession Session(Loaded world, out Store store, out NativeMapEditor editor) {
        store = new Store(); editor = new NativeMapEditor();
        var session = new PatchEditingSession(world.Engine, editor, store, LoadedMod.PatcherId, "author/pack"); editor.Session = session;
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
        // In a session over the saved file, run under the patcher's ID with the mod's key as the game runs it, the mod
        // object's placement is edited, undone and edited again; the file keeps its source and mark.
        var nextEditor = new NativeMapEditor(); var nextSession = new PatchEditingSession(next.Engine, nextEditor, new Store { Patch = store.Patch }, LoadedMod.PatcherId, "author/pack"); nextEditor.Session = nextSession;
        var there = new[] { 4520.0, 12, 5390 };
        var moved = nextSession.Edit(Id(Made), new Placement(there, Upright), null);
        var movedItem = (JObject)Modifications(nextSession)[Key];
        Assert(moved.Applied && Near(next.At(next.ModObject), there) && (string)movedItem["target"]["path"] == Made && (string)movedItem["source"]["path"] == Station, "After a load, a session moves the mod object: " + moved.Error);
        nextEditor.Undo();
        Assert(Near(next.At(next.ModObject), AtBryson), "Undo returns the mod object to its saved placement");
        Assert(nextSession.Edit(Id(Made), new Placement(there, Upright), null).Applied && Near(next.At(next.ModObject), there), "The move applies again");
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

    // A part's copy is a standalone mod object: a top scene object beside the part's group, made from the part, and
    // never a part of that group. Its target names the scene object structure, its source the part. Each load builds
    // it again; its copy, and the copy of a part of a mod object, are made from the map's own part and stand beside it.
    static void APartIsDuplicatedAsAStandaloneModObject() {
        const string place = "World/Large Scenery/Alarka Jct/";
        var standalone = place + "Mask [mod object author:pack mod-object-mask]";
        var world = new Loaded(); var session = Session(world, out var store, out _);
        var parts = world.Building.Parts.Count;
        var placed = session.PlaceModObject(PartId(Part), new Placement(AtBryson, Upright), null, Station);
        Assert(placed.Applied && placed.Target?.Path == standalone && placed.Target.Structure == SceneObjects.Structure, "The part's copy stands beside the group as a scene object: " + (placed.Error ?? placed.Target?.Path + " " + placed.Target?.Structure));
        var item = (JObject)Modifications(session)["mod-object-mask"];
        Assert(item != null && (string)item["source"]["path"] == Part && (string)item["source"]["structure"] == ChildObjects.Structure && (string)item["target"]["structure"] == SceneObjects.Structure,
            "The modification is made from the part: " + Modifications(session).ToString(Newtonsoft.Json.Formatting.None));
        var made = world.At(standalone);
        Assert(made != null && ReferenceEquals(placed.ResolvedTarget, made) && made.Parts.Count == 0 && Near(world.At(made), AtBryson), "The copy is an object of its own at the point");
        Assert(world.Building.Parts.Count == parts && world.Building.Parts.All(p => p.Group == world.Building) && Near(world.At(world.Building), new[] { 1733.0, 9, 1473 }), "The group keeps its parts and its place");
        var moved = session.Edit(placed.Target, new Placement(new[] { 4500.0, 12, 5400 }, Upright), null);
        Assert(moved.Applied && Near(world.At(made), new[] { 4500.0, 12, 5400 }), "The copy moves as a scene object: " + moved.Error);
        // Duplicated again, and as a part of a mod object, the copy is made from the map's part and stands beside it.
        var again = session.PlaceModObject(placed.Target, new Placement(new[] { 2084.0, 12, -288 }, Upright));
        Assert(again.Applied && again.Target.Path == place + "Mask [mod object author:pack mod-object-mask-2]" && (string)Modifications(session)["mod-object-mask-2"]["source"]["path"] == Part, "Its copy has the part as source: " + (again.Error ?? again.Target.Path));
        var station = session.PlaceModObject(Id(Station), new Placement(new[] { 3000.0, 12, 100 }, Upright));
        world.World.PartsAt[station.Target.Path + "/Mask"] = new List<object> { world.At(station.Target.Path).Parts[0] };
        var inner = session.PlaceModObject(PartId(station.Target.Path + "/Mask"), new Placement(new[] { 3100.0, 12, 100 }, Upright), null, station.Target.Path);
        Assert(inner.Applied && inner.Target.Path == place + "Mask [mod object author:pack mod-object-mask-4]" && (string)Modifications(session)["mod-object-mask-4"]["source"]["path"] == Part && ((JArray)Modifications(session)["mod-object-mask-4"]["dependsOn"]).Count == 0,
            "A part of a mod object is copied from the map's part: " + (inner.Error ?? Modifications(session).ToString(Newtonsoft.Json.Formatting.None)));
        session.BeginSave(); session.CompleteNativeSave();
        var next = new Loaded();
        var result = next.Engine.Apply(store.Patch, "author/pack");
        Assert(result.Outcomes.Values.All(o => o.Applied) && next.At(standalone) != null && Near(next.At(next.At(standalone)), new[] { 4500.0, 12, 5400 }) && next.Building.Parts.Count == parts,
            "Each load builds the part's copy beside the group: " + String.Join("; ", result.Outcomes.Where(o => !o.Value.Applied).Select(o => o.Key + " " + o.Value.Error)));
    }

    // What builds a location sign's copy in the controlled board world: a board at the placement that enables there,
    // so its culling sphere and its ground mask register at the new place, as the game's components do.
    sealed class BoardBuilder : IModObjectScene {
        readonly FakeBoardWorld world;
        public BoardBuilder(FakeBoardWorld world) { this.world = world; }
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) {
            if (world.Find(identity.Scene, identity.Path).Count != 0) throw new InvalidOperationException(ModObjects.Taken);
            var from = (FakeBoard)source;
            return world.Add(new FakeBoard { Scene = identity.Scene, Path = identity.Path, Position = (double[])placement.Position.Clone(), Rotation = (double[])placement.Rotation.Clone(), Masks = from.Masks, Flattens = from.Flattens, Parent = from.Parent });
        }
        public void Remove(object modObject) { var board = (FakeBoard)modObject; world.SetActive(board, false); world.Boards.Remove(board); }
    }
    // A location sign's copy rebuilds its own ground mask at the new place and the terrain output there follows it to
    // completion. The source keeps its mask and its terrain; each load builds the copy and its mask again.
    static void ALocationSignCopyRebuildsItsGroundMaskWhereItStands() {
        const string sign = "World/Signs/Loc: Alarka";
        var at = new[] { 26310.0, 640, 393.8 };
        (FakeBoardWorld, FakeBoard, LocationSigns, PatchEngine) World() {
            var boards = new FakeBoardWorld();
            var board = boards.Add(new FakeBoard { Path = sign });
            boards.BuildTiles();
            var signs = new LocationSigns(boards);
            var scene = new SceneCapabilities(() => boards.MapIds, null) { ModObjectWorld = new BoardBuilder(boards) };
            scene.Add(signs);
            return (boards, board, signs, new PatchEngine(scene));
        }
        var (world, source, capability, engine) = World();
        var sourceKeys = source.Keys.ToArray();
        var editor = new NativeMapEditor(); var store = new Store();
        var session = new PatchEditingSession(engine, editor, store, LoadedMod.PatcherId, "author/pack"); editor.Session = session;
        var placed = session.PlaceModObject(new TargetIdentity(SceneName, sign, LocationSigns.CullerType, LocationSigns.Structure), new Placement(at, Upright));
        Assert(placed.Applied && placed.Target.Path == sign + " [mod object author:pack mod-object-loc--alarka]" && placed.Target.Structure == LocationSigns.Structure, "A location sign is duplicated: " + (placed.Error ?? placed.Target.Path));
        var copy = (FakeBoard)placed.ResolvedTarget;
        Assert(copy != null && copy != source && Near(copy.Position, at) && Near(copy.Sphere, at), "The copy stands at the point with its culling sphere there");
        Assert(copy.Keys.Count == 1 && copy.Keys.All(k => Near(world.MaskStore[k], at)) && world.Rebuilds == 1, "The copy rebuilt its own ground mask at the new place");
        Assert(source.Keys.SequenceEqual(sourceKeys) && Near(world.MaskStore[sourceKeys[0]], source.Position), "The source keeps its ground mask where it stands");
        var output = capability.Output(copy);
        Assert(output.Error == null && output.Outstanding.Count != 0 && !world.PendingTiles.Contains("51,0") && world.PendingTiles.Contains("52,0"), "The terrain output at the new place is outstanding, not at the source: " + String.Join("; ", world.PendingTiles));
        world.BuildTiles(); capability.Poll();
        Assert(capability.Output(copy).Settled, "The rebuilt terrain at the new place completes the copy's output");
        session.BeginSave(); session.CompleteNativeSave();
        var (next, nextSource, _, nextEngine) = World();
        var result = nextEngine.Apply(store.Patch, "author/pack");
        var rebuilt = next.Boards.SingleOrDefault(b => b.Path == placed.Target.Path);
        Assert(result.Outcomes.Values.All(o => o.Applied) && rebuilt != null && rebuilt.Keys.All(k => Near(next.MaskStore[k], at)) && nextSource.Keys.Count == 1, "Each load builds the copy with its mask at its place: " + String.Join("; ", result.Outcomes.Select(o => o.Key + " " + o.Value.Error)));
    }

    // A rail bridge scene object whose construction controller switches its parts: the copy stands fully built, holds no
    // construction segments, and the source's construction never switches it.
    static void ABridgeCopyStandsFullyBuilt() {
        const string bridge = "World/Large Scenery/Bryson/Bridge 1";
        var world = new Loaded();
        var source = new FakeGroup { Position = new[] { 900.0, 4, 300 }, RailBridge = true };
        foreach (var part in source.Parts) part.Constructed = true;
        // Base-game progression switches the bridge through its construction segments, a progression controller and a map feature.
        source.Parts[2].Switch = GameSwitch.Progression;
        source.Add(new FakePart { Id = "Deck#4", Offset = new[] { 0.0, 1, 0 }, Constructed = true, Switch = GameSwitch.MapFeature });
        world.World.Objects[bridge] = new List<object> { source };
        world.World.SegmentsOf[source] = new[] { "segment-a", "segment-b" };
        world.World.Controller(source.Parts[1], false);
        world.World.Controller(source.Parts[3], false);
        var session = Session(world, out _, out _);
        var placed = session.PlaceModObject(Id(bridge), new Placement(AtBryson, Upright));
        Assert(placed.Applied && placed.Target.Path == bridge + " [mod object author:pack mod-object-bridge-1]", "A rail bridge scene object is duplicated: " + (placed.Error ?? placed.Target.Path));
        var copy = world.At(placed.Target.Path);
        Assert(copy != null && copy.Parts.All(p => p.Active && !p.Constructed) && world.World.ConstructionSegments(copy) == null, "The copy stands fully built and holds no construction segments");
        Assert(copy.Parts.Count == source.Parts.Count, "The copy holds the parts that progression switched off at the source, built and active");
        foreach (var part in new[] { source.Parts[0], source.Parts[2] }) world.World.Controller(part, false);
        world.World.Controller(source.Parts[3], true);
        Assert(!source.Parts[0].Active && !source.Parts[2].Active && source.Parts[3].Active && copy.Parts.All(p => p.Active), "Construction and progression switch only the source");
        var disabled = session.Edit(placed.Target, null, false);
        Assert(disabled.Applied && !copy.Active && source.Active, "The copy's activity is its own: " + disabled.Error);
        Assert(Near(world.At(copy), AtBryson) && Near(world.At(source), new[] { 1900.0, 4, 300 }), "The copy stands at the point and the source where it stood");
        // The game's construction controller shows an entry for complete track when its condition says so.
        Assert(ConstructionSegmentRule.ShownWhenComplete("NoOverlap", 0x40) && ConstructionSegmentRule.ShownWhenComplete("Equal", 0)
            && !ConstructionSegmentRule.ShownWhenComplete("Equal", 0x40) && !ConstructionSegmentRule.ShownWhenComplete("Other", 0), "A fully built copy shows what complete track shows");
    }

    // What builds a copy of the Ela bridge in the controlled bridge world: a bridge with the source's colliders.
    sealed class BridgeBuilder : IModObjectScene {
        readonly FakeBridgeWorld world;
        public BridgeBuilder(FakeBridgeWorld world) { this.world = world; }
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) {
            if (world.Objects.ContainsKey(identity.Path)) throw new InvalidOperationException(ModObjects.Taken);
            var from = (FakeBridge)source;
            var made = new FakeBridge { Position = new[] { placement.Position[0] - world.Origin, placement.Position[1], placement.Position[2] }, Rotation = (double[])placement.Rotation.Clone(),
                Colliders = from.Colliders.Select(c => new FakeCollider { Id = c.Id, Offset = (double[])c.Offset.Clone(), Shape = c.Shape }).ToList() };
            world.Objects[identity.Path] = new List<object> { made };
            return made;
        }
        public void Remove(object modObject) { foreach (var pair in world.Objects.Where(p => p.Value.Contains(modObject)).ToArray()) world.Objects.Remove(pair.Key); }
    }
    // The Ela bridge, in the bridge family, is duplicated: the copy is a bridge of that family with its colliders at the
    // point, and edits of the family apply to it.
    static void TheElaBridgeIsDuplicated() {
        const string ela = "World/Large Scenery/Ela/Ela Bridge";
        var bridges = new FakeBridgeWorld(); var source = new FakeBridge();
        bridges.Objects[ela] = new List<object> { source };
        var scene = new SceneCapabilities(() => bridges.MapIds, null) { ModObjectWorld = new BridgeBuilder(bridges) };
        scene.Add(new BridgeScene(bridges, Capability.Unchecked));
        var editor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(scene), editor, new Store(), LoadedMod.PatcherId, "author/pack"); editor.Session = session;
        var identity = new TargetIdentity(SceneName, ela, "Helpers.Culling.RendererCuller", BridgeScene.Structure);
        var placed = session.PlaceModObject(identity, new Placement(AtBryson, Upright));
        Assert(placed.Applied && placed.Target.Path == ela + " [mod object author:pack mod-object-ela-bridge]" && placed.Target.Structure == BridgeScene.Structure, "The Ela bridge is duplicated: " + (placed.Error ?? placed.Target.Path));
        var copy = (FakeBridge)placed.ResolvedTarget;
        Assert(copy != source && copy.Active && copy.Colliders.Count == source.Colliders.Count && Near(new[] { copy.Position[0] + bridges.Origin, copy.Position[1], copy.Position[2] }, AtBryson), "The copy is a bridge with the source's colliders at the point");
        var disabled = session.Edit(placed.Target, null, false);
        Assert(disabled.Applied && !copy.Active && source.Active && Near(source.Position, new[] { 50.0, 5, 7 }), "The family's edits apply to the copy and leave the source: " + disabled.Error);
    }

    // A road bridge, a scene object, is duplicated: its copy stands at the point, its source stays.
    static void ARoadBridgeIsDuplicated() {
        const string road = "World/Large Scenery/Almond/Almond Road Bridge";
        var world = new Loaded();
        var source = new FakeGroup { Position = new[] { 600.0, 3, 200 } };
        world.World.Objects[road] = new List<object> { source };
        var session = Session(world, out _, out _);
        var placed = session.PlaceModObject(Id(road), new Placement(AtBryson, Upright));
        Assert(placed.Applied && placed.Target.Path == road + " [mod object author:pack mod-object-almond-road-bridge]", "A road bridge is duplicated: " + (placed.Error ?? placed.Target.Path));
        Assert(Near(world.At(world.At(placed.Target.Path)), AtBryson) && Near(world.At(source), new[] { 1600.0, 3, 200 }), "The copy stands at the point and the source where it stood");
    }

    // The plain scene of the engine tests with objects below a mod object: a build copies the source and what stands
    // below it.
    sealed class PlainScene : Scene, IModObjectScene {
        public int Removes;
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) {
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
        var editor = new NativeMapEditor(); var session = new PatchEditingSession(new PatchEngine(scene), editor, new Store(), LoadedMod.PatcherId, "author/pack"); editor.Session = session;
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

    // A placed mod object keeps its shown number when another of its kind is placed at its place, whatever the new one's
    // source and after a removal: mod objects are numbered in the order they were placed, which the sequence of their
    // modification key gives (ModObjects.NextKey above every mod object key of the file), not in the ordinal order of
    // their paths. The session makes those keys, also after a Remove modification.
    static void PlacedModObjectsKeepTheirNumbers() {
        var scene = new PlainScene { Maps = new[] { "map" } };
        foreach (var path in new[] { "Depot", "Shed" }) scene.Objects[path] = new List<object> { new Box { Position = new[] { 1.0, 2, 3 }, Rotation = new[] { 0.0, 0, 0, 1 } } };
        var editor = new NativeMapEditor(); var keySession = new PatchEditingSession(new PatchEngine(scene), editor, new Store(), LoadedMod.PatcherId, "author/pack"); editor.Session = keySession;
        TargetIdentity At(string path) => new TargetIdentity("s", path, "c");
        string PlaceKey(string path) {
            var outcome = keySession.PlaceModObject(At(path), new Placement(new[] { 400.0 + scene.Objects.Count, 2, 3 }, new[] { 0.0, 0, 0, 1 }));
            Assert(outcome.Applied, "The session places a mod object: " + outcome.Error);
            return outcome.Target.Path.Substring(outcome.Target.Path.LastIndexOf(' ') + 1).TrimEnd(']');
        }
        var keys = new[] { PlaceKey("Depot"), PlaceKey("Shed"), PlaceKey("Depot") };
        Assert(keys.SequenceEqual(new[] { "mod-object-depot", "mod-object-shed-2", "mod-object-depot-3" }), "Each placement takes a sequence above every mod object key: " + String.Join(", ", keys));
        Assert(keySession.Remove(At(ModObjects.Path("Shed", "author/pack", keys[1]))).Applied && PlaceKey("Shed") == "mod-object-shed-4", "A removal frees no sequence for the next placement");

        var map = new BaseMapFixture();
        var destination = map.Objects.Single(o => o.TopSceneObject && o.Name == "Nantahala Station");
        var alarka = map.Objects.Single(o => o.Listed && o.Path == Station);
        var nantahala = map.Objects.Single(o => o.Listed && o.Path == destination.Path);
        var entries = map.Observations(out var facts);
        string Label(SceneObservation entry, IReadOnlyList<SceneObservation> all) {
            var tree = OutlinerGrouping.Build(all, all, "", false, null);
            IEnumerable<InventoryPresentationState.OutlineNode> Nodes(IEnumerable<InventoryPresentationState.OutlineNode> nodes) =>
                nodes.SelectMany(node => new[] { node }.Concat(Nodes(node.Children)));
            return Nodes(tree.Tops).Single(node => node.Row == entry).Label;
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        SceneObservation Placed(BaseMapObject source, int n) {
            var stem = ModObjects.KeyPrefix + source.Name.ToLowerInvariant().Replace(' ', '-');
            var key = ModObjects.NextKey(stem, used); used.Add(key);
            var path = ModObjects.Path(source.Path, "author/pack", key);
            var entry = new SceneObservation(path, Id(path), source.Family, true, new[] { destination.At[0] + n, 12, destination.At[1] }, Upright,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "Placement");
            facts[entry] = source;
            return entry;
        }
        OutlinerGrouping.InvalidatePlaces();
        OutlinerGrouping.Scene = map.Scene(facts);
        try {
            var placed = new List<SceneObservation>();
            var shown = new Dictionary<SceneObservation, string>();
            void Place(BaseMapObject source) {
                placed.Add(Placed(source, placed.Count + 1));
                var all = entries.Concat(placed).ToArray();
                foreach (var earlier in placed.Take(placed.Count - 1))
                    Assert(Label(earlier, all) == shown[earlier], "A placed mod object keeps its shown name " + shown[earlier] + " after placement " + placed.Count + ", got " + Label(earlier, all) + " (" + earlier.Identity.Path + ")");
                foreach (var entry in placed) shown[entry] = Label(entry, all);
            }
            for (var n = 1; n <= 10; n++) Place(n % 3 == 0 ? alarka : nantahala);
            Assert(placed.Select(e => shown[e]).SequenceEqual(Enumerable.Range(2, 10).Select(n => "Nantahala Station (" + n + ")")), "The placed stations are numbered in placement order across sources: " + String.Join(", ", placed.Select(e => shown[e])));
            // A removal renumbers the later ones once; placements after it change no shown number.
            var gone = placed[3]; placed.Remove(gone); used.Remove(ModObjects.MarkOf(gone.Identity.Path).Split(' ').Last().TrimEnd(']'));
            var remaining = entries.Concat(placed).ToArray();
            foreach (var entry in placed) shown[entry] = Label(entry, remaining);
            Place(nantahala); Place(alarka);
            Assert(shown[placed[placed.Count - 1]] == "Nantahala Station (12)", "The last placement after the removal comes last: " + shown[placed[placed.Count - 1]]);
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
