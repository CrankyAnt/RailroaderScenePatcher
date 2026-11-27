// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Industry scene objects and storage piles: the storage displays of Andrews Snowbird Paper, Sylva Paperboard and
// Connelly join the Scene object and Child object families with their piles as parts, and the Object tab says what moving a pile
// or a decorative industry building leaves unchanged. Controlled hierarchies follow the Railroader 0.6.3 scene.
static class IndustryObjectTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Industry objects: " + message); }
    const string Andrews = "World/Large Scenery/Andrews/Andrews Snowbird Paper", Sylva = "World/Large Scenery/Sylva/Sylva Paperboard",
        Connelly = "World/Large Scenery/Whittier/Connelly", LogPond = "World/Large Scenery/Whittier/Whittier Log Pond";
    const string Synchronizer = "Scenery.BulkIndustryStorageSynchronizer";
    // A storage pile as the scene saves it: a Scenery.Pile without a renderer or collider of its own. Near the camera
    // it builds a collider child the scene does not save.
    static Node Pile(string name, bool near = false) {
        var pile = new Node(name).With("Scenery.Pile");
        if (near) { var collider = new Node("Pile Collider").With("UnityEngine.MeshCollider").Solid(); collider.Serialized = false; pile.Kids.Add(collider); }
        return pile;
    }
    internal static Node World(bool near = false) => new Node("World", new Node("Large Scenery",
        new Node("Andrews", new Node("Andrews Snowbird Paper", new Node("Pulpwood Piles", Pile("Pile", near), Pile("Pile (1)", near)), new Node("Retaining Wall").Solid()).With(Synchronizer)),
        new Node("Sylva", new Node("Sylva Paperboard", new Node("Pulpwood Piles", Pile("Pile", near), Pile("Pile (1)", near))).With(Synchronizer)),
        new Node("Whittier",
            new Node("Connelly", Pile("Log Pile L1a", near), Pile("Log Pile L1b", near), Pile("Pulpwood Pile P1a", near)).With(Synchronizer, Synchronizer),
            new Node("Whittier Log Pond", new Node("Whittier Saw Mill Log Pond").With("LakePolygon", "UnityEngine.MeshRenderer").Solid(), new Node("LogPond").With("Scenery.LogPond")).With(Synchronizer),
            new Node("Whittier Saw Mill Culled", new Node("Saw Shed").Solid()))));
    static SceneObjectVerdict At(IReadOnlyList<SceneObjectVerdict> verdicts, string path) => verdicts.SingleOrDefault(v => v.Path == path);

    public static void Run() {
        InventoryListsPileGroupsWithTheirParts();
        ObjectTabNamesThePileStorage();
        ObjectTabStatesTheNativeTrackSpans();
        PileEditsApplyRepeatAndRestore();
        PileEditsInScenePatchEditing();
        Console.WriteLine("PASS: industry objects");
    }

    // The three pile groups are listed groups under Storage display, whatever the camera distance, and each pile is a
    // part named with its group; the Whittier Log Pond group, with its lake surface and log pond, is listed there too.
    static void InventoryListsPileGroupsWithTheirParts() {
        foreach (var near in new[] { false, true }) {
            var verdicts = SceneObjectRules.Classify(World(near));
            foreach (var path in new[] { Andrews, Sylva, Connelly }) {
                var group = At(verdicts, path);
                Assert(group?.Disposition == GroupDisposition.Group && group.Category == SceneObjectCategories.StorageDisplay,
                    path + " is a listed group under Storage display (near " + near + "): " + group?.Disposition + " " + group?.Reason + " " + group?.Category);
            }
            var pond = At(verdicts, LogPond);
            Assert(pond?.Disposition == GroupDisposition.Group && pond.Category == SceneObjectCategories.StorageDisplay, "The Whittier Log Pond group is listed under Storage display: " + pond?.Disposition + " " + pond?.Reason);
            var parts = SceneObjectRules.Parts(verdicts).Where(p => p.Disposition == PartDisposition.Part).Select(p => p.Path).ToArray();
            foreach (var path in new[] { Andrews + "/Pulpwood Piles", Andrews + "/Pulpwood Piles/Pile", Andrews + "/Pulpwood Piles/Pile (1)", Andrews + "/Retaining Wall",
                Sylva + "/Pulpwood Piles", Sylva + "/Pulpwood Piles/Pile (1)", Connelly + "/Log Pile L1a", Connelly + "/Log Pile L1b", Connelly + "/Pulpwood Pile P1a" })
                Assert(parts.Contains(path), path + " is a part (near " + near + "): " + String.Join(", ", parts));
            Assert(!parts.Any(p => p.EndsWith("Pile Collider", StringComparison.Ordinal)), "The collider a pile builds near the camera is no part");
        }
        Assert(SceneObjectRules.PartName(Connelly, Connelly + "/Log Pile L1a") == "Connelly / Log Pile L1a", "A pile is named with its group");
    }

    // A pile's inspection carries the storage it shows: its ids as a binding, which the fingerprint holds, and its
    // name as presentation metadata, which it does not, so names that resolve later never make the inventory stale.
    // The Object tab names that storage and says that moving the pile does not change it.
    static void ObjectTabNamesThePileStorage() {
        SceneObservation Pile(string storage) => new SceneObservation(new object(), new TargetIdentity("s", Connelly + "/Log Pile L1a", ChildObjects.ComponentType, ChildObjects.Structure),
            SceneObjectRules.ChildFamily, true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new[] { CommandBand.StorageBinding("connelly.logs", "logs") }, new string[0], new string[0], "part",
            groupPath: Connelly, storage: storage);
        var byIds = Pile(CommandBand.StorageName(null, null, "connelly.logs", "logs")); var byNames = Pile(CommandBand.StorageName("Connelly", "Logs", "connelly.logs", "logs"));
        var same = new SceneObservation(byIds.Handle, byIds.Identity, byIds.Family, byIds.Active, byIds.Position, byIds.Rotation, byIds.Bindings, byIds.Dependencies, byIds.Problems, byIds.Operations,
            groupPath: Connelly, storage: byNames.Storage);
        Assert(byIds.Matches(same), "A storage name that resolves later leaves the entry current");
        Assert(byIds.Bindings.Single() == "Industry storage=connelly.logs, load logs", "The binding holds the ids the game reads: " + byIds.Bindings.Single());
        Assert(byNames.Storage == "logs storage of Connelly", "Resolved names name the storage, the load in lower case inside the sentence: " + byNames.Storage);
        Assert(byIds.Storage == "storage of load logs at industry component connelly.logs", "Unresolved names fall back to ids: " + byIds.Storage);
        Assert(CommandBand.StorageName("Andrews Snowbird Paper", "LPG", "x.y", "lpg") == "LPG storage of Andrews Snowbird Paper", "An acronym keeps its capitals");
        var text = CommandBand.UseInGame(true, SceneObjectCategories.StorageDisplay, CommandBand.StorageName("Andrews Snowbird Paper", "Pulpwood", "a.b", "pulpwood"), false);
        Assert(text == "This pile shows the pulpwood storage of Andrews Snowbird Paper. Moving or disabling the pile doesn't change the storage. The industry keeps working on its native track spans.",
            "The Object tab names the pile's storage and says moving the pile does not change it: " + text);
    }
    // A decorative industry building, its parts, and a storage display say that the industry works on its native track
    // spans; only a group or part that holds piles says that its piles keep showing the storage. Other groups keep the
    // plain decorative text.
    static void ObjectTabStatesTheNativeTrackSpans() {
        const string Spans = "Decorative. The industry loads and unloads on its native track spans, so moving or disabling this ";
        Assert(CommandBand.UseInGame(false, SceneObjectCategories.IndustryBuilding, null, false) == Spans + "group doesn't affect loading or unloading.",
            "An industry building group states the native track spans: " + CommandBand.UseInGame(false, SceneObjectCategories.IndustryBuilding, null, false));
        Assert(CommandBand.UseInGame(true, SceneObjectCategories.IndustryBuilding, null, false) == Spans + "part doesn't affect loading or unloading.", "An industry building part states the native track spans");
        Assert(CommandBand.UseInGame(false, SceneObjectCategories.StorageDisplay, null, true) == Spans + "group doesn't affect loading or unloading. Its piles keep showing the industry's storage.",
            "A storage display group with piles also states its storage: " + CommandBand.UseInGame(false, SceneObjectCategories.StorageDisplay, null, true));
        Assert(CommandBand.UseInGame(true, SceneObjectCategories.StorageDisplay, null, true) == Spans + "part doesn't affect loading or unloading. Its piles keep showing the industry's storage.",
            "A part that holds piles, such as Pulpwood Piles, states its storage");
        Assert(CommandBand.UseInGame(true, SceneObjectCategories.StorageDisplay, null, false) == Spans + "part doesn't affect loading or unloading.",
            "A part of a storage display without piles, such as a retaining wall, does not mention piles");
        Assert(CommandBand.PilesBinding(2) == "Storage piles=2" && CommandBand.HoldsPiles(new[] { "Other=1", CommandBand.PilesBinding(2) }) && !CommandBand.HoldsPiles(new[] { "Other=1" }), "Whether an object holds piles travels as a binding");
        Assert(CommandBand.UseInGame(false, SceneObjectCategories.Platform, null, false) == CommandBand.Decorative && CommandBand.UseInGame(true, SceneObjectCategories.Platform, null, false) == CommandBand.PartDecorative
            && CommandBand.UseInGame(true, null, null, false) == CommandBand.PartDecorative, "Other groups and parts keep the plain decorative text");
    }

    // The Connelly storage display as the group and part capabilities see it: three piles below the root, no culler of
    // the game in the group, and the rule's verdicts deciding which paths resolve.
    const string SceneName = "BushnellWhittier-MapManager";
    sealed class Display {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeGroup Group = new FakeGroup { Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakePart L1a, L1b, P1a;
        public readonly PatchEngine Engine;
        public readonly SceneCapabilities Scene;
        public Display() {
            L1a = Group.Add(new FakePart { Id = "Log Pile L1a#1", Offset = new[] { 0.0, 0, 0 } });
            L1b = Group.Add(new FakePart { Id = "Log Pile L1b#2", Offset = new[] { 7.0, 0, -11 } });
            P1a = Group.Add(new FakePart { Id = "Pulpwood Pile P1a#3", Offset = new[] { 17.0, 1, 24 } });
            var verdicts = SceneObjectRules.Classify(IndustryObjectTests.World());
            World.GroupVerdicts = verdicts.ToDictionary(v => v.Path, v => v);
            World.Verdicts = SceneObjectRules.Parts(verdicts).ToDictionary(v => v.Path, v => v);
            World.Objects[Connelly] = new List<object> { Group };
            foreach (var (name, pile) in new[] { ("Log Pile L1a", L1a), ("Log Pile L1b", L1b), ("Pulpwood Pile P1a", P1a) }) World.PartsAt[Connelly + "/" + name] = new List<object> { pile };
            World.SetActive(Group, true); World.Mutations = 0;
            var holds = new PartHolds();
            var scene = Scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new SceneObjects(World, holds: holds)); scene.Add(new ChildObjects(World, holds));
            Engine = new PatchEngine(scene);
        }
        public double[] At(FakePart pile) => World.ObservePart(pile).Part.Placement.Position.ToArray();
        public string Where() => String.Join("; ", Group.Parts.Select(p => p.Id + "=" + N(At(p))));
    }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) => a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= 0.001);
    static string Mod(string id, string path, string structure, double[] position = null, bool? active = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", position.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[0,0,0,1]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string PileMod(string id, string name, double[] position = null, bool? active = null) => Mod(id, Connelly + "/" + name, ChildObjects.Structure, position, active);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static TargetIdentity PileId(string name) => new TargetIdentity(SceneName, Connelly + "/" + name, ChildObjects.ComponentType, ChildObjects.Structure);
    static readonly TargetIdentity DisplayId = new TargetIdentity(SceneName, Connelly, SceneObjects.ComponentType, SceneObjects.Structure);
    static readonly double[] Upright = { 0, 0, 0, 1 }, PileTarget = { 1080, 10, 60 }, DisplayTarget = { 1150, 10, 20 };
    static readonly double[] L1aAuthored = { 1100, 10, 20 }, L1bAuthored = { 1107, 10, 9 };
    // Where the pile with its own placement PileTarget stands once the display moved 50 m east to DisplayTarget.
    static readonly double[] PileWithDisplay = { PileTarget[0] + 50, PileTarget[1], PileTarget[2] };

    // A pile moves and disables through the scene patch file like any part, with only the edited pile changing; a
    // moved display carries its other piles; a repeated application and a load at another origin reach the same
    // placements; a refusal mutates nothing; a failure after mutation restores the pile; an incomplete cleanup blocks
    // the retry.
    static void PileEditsApplyRepeatAndRestore() {
        var f = new Display();
        var doc = Doc(PileMod("move-l1a", "Log Pile L1a", PileTarget), PileMod("hide-l1b", "Log Pile L1b", active: false), Mod("move-display", Connelly, SceneObjects.Structure, DisplayTarget));
        var result = f.Engine.Apply(doc);
        Assert(result.Outcomes.Values.All(o => o.Applied), "The pile and display modifications apply: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        // The display moves 50 m east: the moved pile keeps its own placement relative to the display and moves with it.
        Assert(Near(f.At(f.L1a), PileWithDisplay) && !f.L1b.Active && f.P1a.Active && Near(f.At(f.P1a), new[] { 1167.0, 11, 44 }), "The moved pile keeps its placement relative to its display, the disabled pile is off, the other pile moves with its display: " + f.Where());
        var first = f.Where();
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && f.Where() == first, "A repeated application accumulates no offset: " + f.Where());
        // A world loaded at another origin reaches the same map placements.
        // The scene stands 10 m further west in the scene's coordinates, so every map placement stays the same.
        var shifted = new Display(); shifted.World.Origin = 1010; shifted.Group.Position[0] -= 10;
        Assert(shifted.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && shifted.Where() == first, "A load at another origin reaches the same map placements: " + shifted.Where());
        // The display root and a pile resolve only as what the rule lists them as.
        var wrong = new Display().Engine.Apply(Doc(Mod("root-as-part", Connelly, ChildObjects.Structure, PileTarget), Mod("pile-as-group", Connelly + "/Log Pile L1a", SceneObjects.Structure, PileTarget)));
        Assert(wrong.Outcomes.Values.All(o => !o.Applied), "A display root does not resolve as a part, nor a pile as a group");

        var refused = new Display(); refused.World.PartRefused = "The part's scene is not loaded";
        var outcome = refused.Engine.Apply(Doc(PileMod("move-l1a", "Log Pile L1a", PileTarget))).Outcomes["move-l1a"];
        Assert(!outcome.Applied && refused.World.Mutations == 0 && Near(refused.At(refused.L1a), L1aAuthored), "A preflight refusal leaves the pile untouched: " + outcome.Error);

        var failed = new Display(); failed.World.ThrowAfterPartPlacement = true;
        outcome = failed.Engine.Apply(Doc(PileMod("move-l1a", "Log Pile L1a", PileTarget, false))).Outcomes["move-l1a"];
        Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal) && Near(failed.At(failed.L1a), L1aAuthored) && failed.L1a.Active,
            "A failure after mutation restores the pile's placement and activity: " + outcome.Error + " " + failed.Where());

        var stuck = new Display(); stuck.World.MoveSibling = stuck.World.KeepSiblingMoved = true;
        outcome = stuck.Engine.Apply(Doc(PileMod("move-l1b", "Log Pile L1b", PileTarget))).Outcomes["move-l1b"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        stuck.World.KeepSiblingMoved = false; var mutations = stuck.World.Mutations;
        var retry = stuck.Engine.Apply(Doc(PileMod("again", "Log Pile L1b", DisplayTarget))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && stuck.World.Mutations == mutations, "The retry on the affected pile is blocked: " + retry.Error);
    }

    // In scene patch editing: Undo and Redo of pile and display edits with matching dirty state and Save availability,
    // Remove modification returning the pile to its place in its display, Save at format version 1 with the patcher
    // dependency, a stricter constraint kept, and a reload that reproduces the edits without duplicated state.
    static void PileEditsInScenePatchEditing() {
        var f = new Display(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Assert(session.Edit(PileId("Log Pile L1a"), new Placement(PileTarget, Upright), null).Applied && session.Edit(PileId("Log Pile L1b"), null, false).Applied
            && session.Edit(DisplayId, new Placement(DisplayTarget, Upright), null).Applied, "A pile move, a pile disable and a display move apply");
        Persistence(true, all, "After three edits");
        mapEditor.Undo(); Assert(Near(f.At(f.L1b), L1bAuthored) && Near(f.At(f.L1a), PileTarget), "Undo returns the display, and the moved pile with it: " + f.Where());
        mapEditor.Undo(); Assert(f.L1b.Active, "Undo enables the pile");
        mapEditor.Undo(); Assert(Near(f.At(f.L1a), L1aAuthored), "Undo returns the pile: " + f.Where()); Persistence(false, new string[0], "After Undo back to the unedited display");
        mapEditor.Redo(); mapEditor.Redo(); mapEditor.Redo();
        Assert(Near(f.At(f.L1a), PileWithDisplay) && !f.L1b.Active && Near(f.At(f.P1a), new[] { 1167.0, 11, 44 }), "Redo reapplies every edit: " + f.Where()); Persistence(true, all, "After Redo");
        var removed = session.Remove(PileId("Log Pile L1a"));
        Assert(removed.Applied && Near(f.At(f.L1a), DisplayTarget), "Remove modification returns the pile to its place in the moved display: " + removed.Error + " " + f.Where());
        mapEditor.Undo(); Assert(Near(f.At(f.L1a), PileWithDisplay), "Undo of the removal holds the pile again: " + f.Where());
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0") && (int)JObject.Parse(store.Patch)["formatVersion"] == 1, "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        var fresh = new Display();
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Count == 3 && reload.Outcomes.Values.All(o => o.Applied) && Near(fresh.At(fresh.L1a), PileWithDisplay) && !fresh.L1b.Active && fresh.Group.Parts.Count == 3,
            "A reloaded world reproduces the edits without duplicated state: " + fresh.Where());
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = new Display(); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(other.Engine, strictEditor, strict); strictEditor.Session = kept;
        Assert(kept.Edit(PileId("Log Pile L1a"), new Placement(PileTarget, Upright), null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=0.5.0"), "A stricter existing constraint is retained: " + strict.Info);
    }
}
