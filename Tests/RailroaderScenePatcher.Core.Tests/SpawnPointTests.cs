// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// One scene spawn point under World/POIs: a transform without a renderer, collider or light.
class FakeSpawn {
    public double[] Position = { 4194.8, 645.5, -3139.2 }, Rotation = { 0, 0, 0, 1 };
    public bool Active = true;
}
// Controlled world: map coordinates are scene coordinates plus Origin on X, as the moving origin has them.
class FakeSpawnWorld : ISpawnPointWorld {
    public string[] Maps = { "map" };
    public IEnumerable<string> MapIds => Maps;
    public Dictionary<string, List<object>> Objects = new Dictionary<string, List<object>>();
    public double Origin = 1000;
    public string Refused;
    public int Mutations;
    // Failure injections: a throw after the placement is written, and a placement that does not stick, once or always.
    public bool ThrowAfterPlacement, DriftOnPlacement, KeepDrift;
    public IReadOnlyList<object> Find(string scene, string path) => scene == "level4" && Objects.TryGetValue(path, out var found) ? found : new List<object>();
    public string Refusal(object spawn) => Refused;
    public ObjectState Observe(object target) {
        var spawn = (FakeSpawn)target;
        return new ObjectState(new Placement(new[] { spawn.Position[0] + Origin, spawn.Position[1], spawn.Position[2] }, (double[])spawn.Rotation.Clone()), spawn.Active);
    }
    public void SetActive(object target, bool active) { Mutations++; ((FakeSpawn)target).Active = active; }
    public void SetPlacement(object target, Placement placement) {
        Mutations++;
        var spawn = (FakeSpawn)target;
        spawn.Position = new[] { placement.Position[0] - Origin, placement.Position[1], placement.Position[2] };
        spawn.Rotation = (double[])placement.Rotation.Clone();
        if (DriftOnPlacement) { spawn.Position[1] += 3; DriftOnPlacement = KeepDrift; }
        if (ThrowAfterPlacement) { ThrowAfterPlacement = false; throw new Exception("injected after placement"); }
    }
}
static class SpawnPointTests {
    const string Alarka = "World/POIs/Alarka";
    static readonly TargetIdentity AlarkaId = new TargetIdentity("level4", Alarka, SpawnPoints.Component, SpawnPoints.Structure);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Spawn(string id, double x = 5210, string path = Alarka, string extra = "", bool placement = true) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"level4\",\"path\":\"" + path + "\",\"component\":\"Character.SpawnPoint\",\"structure\":\"spawn-point-v1\"}" +
        (placement ? ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",650,-3100],\"rotation\":[0,0.707107,0,0.707107]}" : "") + extra + ",\"dependsOn\":[]}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":{" + items + "}}";
    static (FakeSpawnWorld world, FakeSpawn spawn, SpawnPointScene scene) Setup() {
        var world = new FakeSpawnWorld(); var spawn = new FakeSpawn();
        world.Objects[Alarka] = new List<object> { spawn };
        return (world, spawn, new SpawnPointScene(world, Capability.Unchecked));
    }
    static bool Unedited(FakeSpawn spawn) => spawn.Position.SequenceEqual(new[] { 4194.8, 645.5, -3139.2 }) && spawn.Rotation.SequenceEqual(new[] { 0.0, 0, 0, 1 }) && spawn.Active;
    static bool Moved(FakeSpawn spawn) => Math.Abs(spawn.Position[0] - 4210) < 1e-9 && spawn.Position[1] == 650 && spawn.Position[2] == -3100 && spawn.Rotation[1] == 0.707107;
    public static void Run() {
        GroundMaskTests.HeldMaskFollowsItsHolder("Spawn point", masks => { var (world, spawn, _) = Setup(); return (spawn, new SpawnPointScene(world, Capability.Unchecked, masks)); }, Doc(Spawn("move-spawn", 5400)));
        FixedPlacementThroughTheOrigin();
        TargetResolutionIsExact();
        ActivityAndRepeatedApplication();
        PreflightRefusalLeavesSceneAndFileUnchanged();
        FailureAfterMutationRestoresPlacementAndActivity();
        UnverifiedCleanupBlocksRetry();
        EditingHistoryRemovalSaveAndReload();
        ObjectTabNamesTheUses();
        InventoryListsEachSpawnPointOnceWithoutProblems();
        GroupsWithSpawnPointsPointToTheFamily();
        DisabledSpawnPointIsNotOffered();
        UsesStateConditionsAndCounts();
        RankCountsTheOtherActiveSpawnPoints();
        NewGameStartIsNamedOnlyWhereTheGameStartsThere();
        UnreadSetupsAreNamedAsUnread();
        RelationshipsAreRefusedBeforeMutation();
        MinimumHoldsWithoutTheCapability();
        SpawnPointRootIsNoGroupRow();
        NewGameStartPlacesSpawnPointsFirst();
        NewGameStartLeavesOtherWorkForTheWorldLoad();
    }
    // A new game reads the start setup's spawn point before the world-load apply; the spawn point modifications apply
    // first, through the same engine, and the world load then finds them in place.
    static void NewGameStartPlacesSpawnPointsFirst() {
        var (world, spawn, scene) = Setup();
        var engine = new PatchEngine(scene);
        var doc = Doc(Spawn("move-alarka", extra: ",\"active\":false"));
        var early = engine.BeforeNewGameStart(doc, "");
        Assert(early.Error == null && early.Outcomes["move-alarka"].Applied && Moved(spawn) && !spawn.Active, "The spawn point is moved and disabled before the new game reads it: " + early.Error);
        var load = engine.Apply(doc, "");
        Assert(load.Outcomes["move-alarka"].Applied && Moved(spawn) && !spawn.Active, "The world-load apply keeps the move: " + load.Outcomes["move-alarka"].Error);
        var store = new Store { Patch = doc }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store); mapEditor.Session = session;
        Assert(session.Remove(AlarkaId).Applied && Unedited(spawn), "Remove modification still returns the authored placement and activity, captured before the early apply");
    }
    // Only a spawn point modification without dependencies, with a fixed placement and a target ready now applies early;
    // anything else is left to the world-load apply, untouched and without a failure.
    static void NewGameStartLeavesOtherWorkForTheWorldLoad() {
        var (world, spawn, scene) = Setup();
        world.Objects["World/POIs/Bryson"] = new List<object> { new FakeSpawn() };
        world.Objects["World/POIs/Dillsboro"] = new List<object> { new FakeSpawn() };
        var dependent = Spawn("a-dependent", path: "World/POIs/Bryson").Replace("\"dependsOn\":[]", "\"dependsOn\":[\"move-alarka\"]");
        var following = "\"following\":{\"target\":{\"scene\":\"level4\",\"path\":\"World/POIs/Dillsboro\",\"component\":\"Character.SpawnPoint\",\"structure\":\"spawn-point-v1\"}," +
            "\"placement\":{\"mode\":\"trackFollowing\",\"track\":{\"segment\":\"S1\",\"end\":\"a\",\"distance\":10},\"offset\":[0,0,0],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var doc = Doc(Spawn("move-alarka") + "," + dependent + "," + following);
        var engine = new PatchEngine(scene);
        var early = engine.BeforeNewGameStart(doc, "");
        Assert(early.Outcomes["move-alarka"].Applied && !early.Outcomes.ContainsKey("a-dependent") && !early.Outcomes.ContainsKey("following") && world.Mutations == 1,
            "Only the fixed, dependency-free spawn point modification applies early: " + String.Join(", ", early.Outcomes.Keys));
        Assert(early.EarlyRouteUnavailable != null, "The skipped modifications are named as waiting for the world load");
        world.Refused = "The scene spawn point's scene isn't loaded.";
        var refused = new PatchEngine(scene).BeforeNewGameStart(Doc(Spawn("move-alarka", 5220)), "");
        Assert(!refused.Outcomes.Any() && world.Mutations == 1 && refused.EarlyRouteUnavailable.Contains("isn't loaded"), "A target that is not ready is left untouched without a failure: " + refused.EarlyRouteUnavailable);
        world.Refused = null;
        var later = engine.Apply(Doc(Spawn("move-alarka", 5220)), "");
        Assert(later.Outcomes["move-alarka"].Applied && Math.Abs(spawn.Position[0] - 4220) < 1e-9, "The world-load apply still runs every modification: " + later.Outcomes["move-alarka"].Error);
    }
    static void FixedPlacementThroughTheOrigin() {
        var (world, spawn, scene) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Spawn("move-alarka")));
        Assert(result.Error == null && result.Outcomes["move-alarka"].Applied, "Spawn point placement applies: " + result.Error + result.Outcomes["move-alarka"].Error);
        Assert(Moved(spawn) && spawn.Active, "The spawn point takes the map placement through the current origin");
    }
    static void TargetResolutionIsExact() {
        var (world, spawn, scene) = Setup();
        var engine = new PatchEngine(scene);
        Assert(!engine.Apply(Doc(Spawn("other-map"), "other")).Outcomes.Any() && world.Mutations == 0, "A scene patch file for another map touches nothing");
        Assert(!engine.Apply(Doc(Spawn("missing", path: "World/POIs/Nowhere"))).Outcomes["missing"].Applied && world.Mutations == 0, "A path without a spawn point resolves to nothing");
        world.Objects["World/POIs/Twin"] = new List<object> { new FakeSpawn(), new FakeSpawn() };
        Assert(!engine.Apply(Doc(Spawn("twin", path: "World/POIs/Twin"))).Outcomes["twin"].Applied && world.Mutations == 0, "An ambiguous spawn point identity fails");
        var group = Spawn("group").Replace("spawn-point-v1", SceneObjects.Structure).Replace("Character.SpawnPoint", "UnityEngine.Transform");
        Assert(!engine.Apply(Doc(group)).Outcomes["group"].Applied && world.Mutations == 0, "A scene object with child objects identity does not resolve as a spawn point");
        Assert(scene.Resolve(new TargetIdentity("level4", Alarka, SpawnPoints.Component, null)).Count == 1, "An identity without a structure names the spawn point by its component");
    }
    static void ActivityAndRepeatedApplication() {
        var (world, spawn, scene) = Setup();
        var engine = new PatchEngine(scene);
        var disable = Doc(Spawn("hide-alarka", extra: ",\"active\":false"));
        Assert(engine.Apply(disable).Outcomes["hide-alarka"].Applied && !spawn.Active && Moved(spawn), "An explicit disable deactivates the moved spawn point");
        spawn.Active = true;
        Assert(engine.Apply(disable).Outcomes["hide-alarka"].Applied && !spawn.Active && Moved(spawn), "Repeated application keeps the absolute placement and the disable");
        world.Origin = 1010;
        Assert(new PatchEngine(scene).Apply(disable).Outcomes["hide-alarka"].Applied && Math.Abs(spawn.Position[0] - 4200) < 1e-9, "A new load at another origin reaches the same map placement");
        var enable = Doc(Spawn("show-alarka", extra: ",\"active\":true", placement: false));
        Assert(new PatchEngine(scene).Apply(enable).Outcomes["show-alarka"].Applied && spawn.Active, "An explicit enable activates the spawn point and keeps its placement");
    }
    static void PreflightRefusalLeavesSceneAndFileUnchanged() {
        var (world, spawn, scene) = Setup();
        world.Refused = "The scene spawn point's scene isn't loaded.";
        var outcome = new PatchEngine(scene).Apply(Doc(Spawn("move-alarka"))).Outcomes["move-alarka"];
        Assert(!outcome.Applied && outcome.Error.Contains("isn't loaded") && world.Mutations == 0 && Unedited(spawn), "A preflight refusal leaves the spawn point untouched: " + outcome.Error);
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(scene), mapEditor, store); mapEditor.Session = session;
        var before = session.ScenePatchFileText;
        var edit = session.Edit(AlarkaId, new Placement(new[] { 5210.0, 650, -3100 }, new[] { 0, 0.707107, 0, 0.707107 }), null);
        Assert(!edit.Applied && session.ScenePatchFileText == before && world.Mutations == 0 && Unedited(spawn) && !mapEditor.IsDirty, "A refused edit leaves the scene and the scene patch file unchanged: " + edit.Error);
    }
    static void FailureAfterMutationRestoresPlacementAndActivity() {
        foreach (var inject in new Action<FakeSpawnWorld>[] { w => w.ThrowAfterPlacement = true, w => w.DriftOnPlacement = true }) {
            var (world, spawn, scene) = Setup();
            inject(world);
            var engine = new PatchEngine(scene);
            var outcome = engine.Apply(Doc(Spawn("move-alarka", extra: ",\"active\":false"))).Outcomes["move-alarka"];
            Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal), "A failure after mutation is restored: " + outcome.Error);
            Assert(Unedited(spawn), "The captured placement and activity are restored");
        }
    }
    static void UnverifiedCleanupBlocksRetry() {
        var (world, spawn, scene) = Setup();
        world.DriftOnPlacement = world.KeepDrift = true;
        var engine = new PatchEngine(scene);
        var outcome = engine.Apply(Doc(Spawn("move-alarka"))).Outcomes["move-alarka"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        world.DriftOnPlacement = world.KeepDrift = false;
        var mutations = world.Mutations;
        var retry = engine.Apply(Doc(Spawn("again", 5220))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && world.Mutations == mutations, "The retry on the affected spawn point is blocked: " + retry.Error);
        var reloaded = new PatchEngine(scene).Apply(Doc(Spawn("again", 5220))).Outcomes["again"];
        Assert(reloaded.Applied && Math.Abs(spawn.Position[0] - 4220) < 1e-9, "A world reload lifts the block: " + reloaded.Error);
    }
    static void EditingHistoryRemovalSaveAndReload() {
        var (world, spawn, scene) = Setup();
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(scene), mapEditor, store); mapEditor.Session = session;
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        var moved = new Placement(new[] { 5210.0, 650, -3100 }, new[] { 0, 0.707107, 0, 0.707107 });
        Assert(session.Edit(AlarkaId, moved, null).Applied && Moved(spawn), "A move in scene patch editing moves the spawn point");
        var item = (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"].First.First;
        Assert((string)item["target"]["structure"] == SpawnPoints.Structure && (string)item["target"]["component"] == SpawnPoints.Component && item["placement"] != null && item["active"] == null,
            "The scene patch file records the spawn point's placement under its own structure: " + item);
        Assert(session.MinimumPatcherVersion == SpawnPoints.RequiredVersion, "A spawn point placement requires the spawn point capability version: " + session.MinimumPatcherVersion);
        Assert(session.Edit(AlarkaId, null, false).Applied && !spawn.Active, "A disable in scene patch editing deactivates the spawn point");
        Persistence(true, all, "After two edits");
        mapEditor.Undo(); Assert(spawn.Active && Moved(spawn), "Undo enables the moved spawn point");
        mapEditor.Undo(); Assert(Unedited(spawn), "Undo returns the spawn point to its scene placement");
        Persistence(false, new string[0], "After Undo back to the unedited spawn point");
        mapEditor.Redo(); mapEditor.Redo(); Assert(Moved(spawn) && !spawn.Active, "Redo reapplies both edits");
        Persistence(true, all, "After Redo");
        Assert(session.Remove(AlarkaId).Applied && Unedited(spawn), "Remove modification returns the spawn point to its effective underlying state");
        mapEditor.Undo(); Assert(Moved(spawn) && !spawn.Active, "Undo of the removal restores this mod's edits");
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=" + SpawnPoints.RequiredVersion) && (int)JObject.Parse(store.Patch)["formatVersion"] == 1,
            "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        var fresh = new FakeSpawn(); var replay = new FakeSpawnWorld { Origin = 990 }; replay.Objects[Alarka] = new List<object> { fresh };
        var engine = new PatchEngine(new SpawnPointScene(replay, Capability.Unchecked));
        var reload = engine.Apply(store.Patch);
        Assert(((JObject)JObject.Parse(store.Patch)["modifications"]).Count == 1 && reload.Outcomes.Values.Single().Applied && Math.Abs(fresh.Position[0] - 4220) < 1e-9 && !fresh.Active,
            "A reloaded world reproduces the move from one modification");
        var once = String.Join(",", fresh.Position);
        Assert(engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && String.Join(",", fresh.Position) == once, "Applying the saved file again adds no offset");
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var (_, _, otherScene) = Setup(); var strictEditor = new NativeMapEditor();
        var kept = new PatchEditingSession(new PatchEngine(otherScene), strictEditor, strict); strictEditor.Session = kept;
        Assert(kept.Edit(AlarkaId, moved, null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=" + SpawnPoints.RequiredVersion + "\""), "A stricter existing constraint is retained: " + strict.Info);
    }
    static void ObjectTabNamesTheUses() {
        var bindings = new[] { SpawnPoints.NameBinding("Alarka"), SpawnPoints.PriorityBinding(0), SpawnPoints.SetupBinding("alarka-jct-start") };
        var uses = SpawnPoints.Uses(bindings, 0);
        foreach (var expected in new[] { "rescue", "/tp", "new game", "alarka-jct-start", "Alarka" })
            Assert(uses.Any(line => line.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0), "The uses name " + expected + ": " + String.Join(" | ", uses));
        // Quick search never opens in Railroader 0.6.3 (QuickSearchController.Show is empty), so it is no use of a spawn
        // point, and neither the uses nor the dependency name it.
        var teleport = uses.Single(line => line.Contains("/tp"));
        Assert(teleport == "Teleport: the /tp console command is expected to list this spawn point as Alarka and teleport you to its position.",
            "The teleport use names /tp alone: " + teleport);
        Assert(!uses.Any(line => line.IndexOf("quick search", StringComparison.OrdinalIgnoreCase) >= 0) && SpawnPoints.Dependency.IndexOf("quick search", StringComparison.OrdinalIgnoreCase) < 0,
            "No use or dependency names quick search: " + String.Join(" | ", uses) + " | " + SpawnPoints.Dependency);
        Assert(uses.Any(line => line.Contains("no native spawns")), "Without native spawns the default start says so: " + String.Join(" | ", uses));
        var native = SpawnPoints.Uses(new[] { SpawnPoints.NameBinding("Alarka"), SpawnPoints.PriorityBinding(0) }, 2);
        Assert(!native.Any(line => line.StartsWith("New game:", StringComparison.Ordinal)) && native.Any(line => line.Contains("No start setup")),
            "With native spawns a new game doesn't start at a scene spawn point, and a spawn point without a setup says so: " + String.Join(" | ", native));
        var unknown = SpawnPoints.Uses(new[] { SpawnPoints.NameBinding("Alarka"), SpawnPoints.PriorityBinding(0) }, null);
        Assert(unknown.Any(line => line.Contains("couldn't be read")), "Unknown native spawns are named as unread: " + String.Join(" | ", unknown));
        var setup = uses.Single(line => line.Contains("alarka-jct-start"));
        Assert(setup.Contains("expected") && setup.Contains("no cars") && setup.Contains("balance"),
            "The start setup use names its condition and stays expected behaviour: " + setup);
        Assert(SpawnPoints.Operations.StartsWith("Move, rotate, enable, or disable", StringComparison.Ordinal), "The supported edits name placement and Enabled: " + SpawnPoints.Operations);
    }
    static SceneObservation Row(string name, bool enabled, int? priority = null, string family = SpawnPoints.Family) =>
        new SceneObservation(name, new TargetIdentity("level4", "World/POIs/" + name, SpawnPoints.Component, SpawnPoints.Structure), family, enabled,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, priority == null ? new[] { SpawnPoints.NameBinding(name) } : new[] { SpawnPoints.NameBinding(name), SpawnPoints.PriorityBinding(priority.Value) }, new[] { SpawnPoints.Dependency }, BindingRules.Problems(enabled, new string[0], new string[0]),
            SpawnPoints.Operations, new string[0], enabled, new string[0], null);
    static void InventoryListsEachSpawnPointOnceWithoutProblems() {
        Assert(SpawnPoints.FamilyOf(SpawnPoints.Component, "World") == SpawnPoints.Family, "A spawn point below World is listed in the Spawn point family");
        Assert(SpawnPoints.FamilyOf("Helpers.MilePost", "World") == null, "Another component is not a spawn point");
        Assert(SpawnPoints.FamilyOf(SpawnPoints.Component, "Setups") == null, "An object below a kept-out root stays out");
        var rows = new[] { Row("Bryson", true), Row("Alarka", false), Row("Whittier", true) };
        Assert(rows.All(r => r.Problems.Count == 0 && r.InspectionOnly == null), "An inactive spawn point shows no problem and none is inspection only");
    }
    static void GroupsWithSpawnPointsPointToTheFamily() {
        var verdict = SceneObjectRules.Classify(new Node("World", new Node("Large Scenery", new Node("Town", new Node("Depot", new Node("Spawn").With(SpawnPoints.Component), new Node("Shell").Solid())))))
            .Single(v => v.Path == "World/Large Scenery/Town/Depot");
        Assert(verdict.Disposition == GroupDisposition.InspectionOnly && verdict.Reason == SceneObjectRules.SpawnPointReason && verdict.ReasonIsSentence,
            "A group that holds a spawn point names the Spawn point family: " + verdict.Reason);
    }
    // A disabled spawn point is not in SpawnPoint.All: rescue, teleport and the default start pass it by;
    // a start setup that names it still reads it.
    static void DisabledSpawnPointIsNotOffered() {
        var bindings = new[] { SpawnPoints.NameBinding("Alarka"), SpawnPoints.PriorityBinding(0), SpawnPoints.SetupBinding("alarka-jct-start") };
        var uses = SpawnPoints.Uses(bindings, 0, SpawnRank.Inactive);
        Assert(!uses.Any(line => line.Contains("list this spawn point as")), "A disabled spawn point is not said to be offered: " + String.Join(" | ", uses));
        Assert(!uses.Any(line => line.IndexOf("quick search", StringComparison.OrdinalIgnoreCase) >= 0), "A disabled spawn point's uses leave quick search out: " + String.Join(" | ", uses));
        foreach (var expected in new[] { "rescue", "/tp" })
            Assert(uses.Any(line => line.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0 && line.Contains("disabled")), "The disabled spawn point says " + expected + " passes it by: " + String.Join(" | ", uses));
        Assert(!uses.Any(line => line.StartsWith("New game:", StringComparison.Ordinal)), "A new game without a start setup passes a disabled spawn point by, so no default start is named: " + String.Join(" | ", uses));
        Assert(uses.Any(line => line.Contains("alarka-jct-start") && line.Contains("This also applies when this spawn point is disabled")), "A start setup still names it: " + String.Join(" | ", uses));
    }
    static void UsesStateConditionsAndCounts() {
        var bindings = new[] { SpawnPoints.NameBinding("Alarka"), SpawnPoints.PriorityBinding(0) };
        var one = SpawnPoints.Uses(bindings.Concat(new[] { SpawnPoints.SetupBinding("alarka-jct-start") }), 1);
        Assert(one.Any(line => line.Contains("declares 1 native spawn,")) && !one.Any(line => line.Contains("1 native spawns")), "One native spawn reads singular: " + String.Join(" | ", one));
        var tie = SpawnPoints.Uses(bindings, 0, new SpawnRank(true, 3, 0, 0));
        Assert(tie.Any(line => line.Contains("one of the 4 active spawn points with the highest priority (0), this one among them") && line.Contains("isn't fixed")),
            "Equal priorities count this spawn point among them and say the choice is not fixed: " + String.Join(" | ", tie));
        Assert(!SpawnPoints.Uses(bindings, 0).Any(line => line.Contains("isn't fixed")), "A unique priority names no tie");
        Assert(SpawnPoints.Priority(bindings) == 0 && SpawnPoints.Priority(new[] { SpawnPoints.NameBinding("x") }) == null, "The priority reads back from the bindings");
    }
    // The game offers only active spawn points, highest priority first, with no fixed order among equal priorities, so
    // the rank of a spawn point counts the other active ones that share, exceed or hide their priority.
    static void RankCountsTheOtherActiveSpawnPoints() {
        var alarka = Row("Alarka", true, 5);
        var rows = new[] { alarka, Row("Bryson", true, 5), Row("Cochran", true, 3), Row("Dillsboro", false, 9), Row("Ela", true),
            Row("Almond", true, 9, StarterPlacements.Family), Row("Alarka", true, 7) };
        var rank = SpawnPoints.Rank(alarka, rows);
        Assert(rank.Active && rank.SamePriority == 1 && rank.HigherPriority == 0 && rank.UnreadPriority == 1,
            "Alarka shares its priority with Bryson, has no active spawn point above it and one whose priority is unread; inactive points, other families and its own path don't count: "
            + rank.SamePriority + "/" + rank.HigherPriority + "/" + rank.UnreadPriority);
        var cochran = SpawnPoints.Rank(rows[2], rows);
        Assert(cochran.HigherPriority == 3 && cochran.SamePriority == 0, "Cochran has three active spawn points above it, both Alarka rows among them: " + cochran.HigherPriority);
        Assert(!SpawnPoints.Rank(rows[3], rows).Active, "A disabled spawn point ranks as inactive");
        var unread = SpawnPoints.Rank(rows[4], rows);
        Assert(unread.Active && unread.SamePriority == 0 && unread.HigherPriority == 0 && unread.UnreadPriority == 0, "A spawn point without a priority counts nothing it could compare");
        var alone = SpawnPoints.Rank(alarka, null);
        Assert(alone.Active && alone.SamePriority == 0 && alone.HigherPriority == 0 && alone.UnreadPriority == 0, "Without an inventory the rank counts no others");
    }
    // SpawnPoint.Default is the first of SpawnPoint.All: active spawn points only, highest priority first. A new game
    // without a start setup reads it only when the map declares no native spawns (InitializeDefaultSpawnIfNeeded). The
    // Object tab names that start only for the spawn point the game starts at.
    static void NewGameStartIsNamedOnlyWhereTheGameStartsThere() {
        var bindings = new[] { SpawnPoints.NameBinding("Alarka"), SpawnPoints.PriorityBinding(5) };
        string Start(IReadOnlyList<string> lines) => lines.SingleOrDefault(line => line.StartsWith("New game:", StringComparison.Ordinal));
        var first = Start(SpawnPoints.Uses(bindings, 0));
        Assert(first == "New game: the map declares no native spawns, so a new game without a start setup is expected to start here, at the active spawn point with the highest priority (5).",
            "The active spawn point with the highest priority names the default start here: " + first);
        Assert(Start(SpawnPoints.Uses(bindings, 0, new SpawnRank(true, 0, 1, 0))) == null, "A spawn point below another active one's priority names no default start");
        Assert(Start(SpawnPoints.Uses(bindings, 0, SpawnRank.Inactive)) == null, "A disabled spawn point names no default start");
        Assert(Start(SpawnPoints.Uses(bindings, 3)) == null, "With native spawns no scene spawn point names the default start");
        Assert(Start(SpawnPoints.Uses(new[] { SpawnPoints.NameBinding("Alarka") }, 0)) == null, "Without a priority the game's choice can't be told, so no default start is named");
        var tie = Start(SpawnPoints.Uses(bindings, 0, new SpawnRank(true, 2, 0, 0)));
        // In a tie the game starts at one of the tied spawn points, and which one isn't fixed: no tied point is named as the start.
        Assert(tie == "New game: the map declares no native spawns, so a new game without a start setup is expected to start at one of the 3 active spawn points with the highest priority (5), this one among them. Which one isn't fixed.",
            "A shared top priority gives one hedged line that claims no start here: " + tie);
        // An active spawn point whose priority couldn't be read might come first, so the start here is hedged.
        var hidden = Start(SpawnPoints.Uses(bindings, 0, new SpawnRank(true, 0, 0, 1)));
        Assert(hidden == "New game: the map declares no native spawns, so a new game without a start setup is expected to start here, at the active spawn point with the highest priority (5), unless the other active spawn point whose priority couldn't be read has a higher one.",
            "One unread priority hedges the start here: " + hidden);
        var hiddenTwo = Start(SpawnPoints.Uses(bindings, 0, new SpawnRank(true, 0, 0, 2)));
        Assert(hiddenTwo != null && hiddenTwo.EndsWith(", unless one of the 2 other active spawn points whose priority couldn't be read has a higher one.", StringComparison.Ordinal),
            "Several unread priorities hedge the start here: " + hiddenTwo);
        var tieHidden = Start(SpawnPoints.Uses(bindings, 0, new SpawnRank(true, 2, 0, 1)));
        Assert(tieHidden != null && tieHidden.Contains("one of the 3 active spawn points") && !tieHidden.Contains("start here")
            && tieHidden.EndsWith(" The priority of 1 other active spawn point couldn't be read, so the game might start there instead.", StringComparison.Ordinal),
            "A tie with an unread priority hedges both: " + tieHidden);
        var unread = Start(SpawnPoints.Uses(bindings, null));
        Assert(unread == "New game: the map's native spawns couldn't be read. If the map declares none, a new game without a start setup is expected to start here, at the active spawn point with the highest priority (5).",
            "Unread native spawns make the default start conditional: " + unread);
        Assert(Start(SpawnPoints.Uses(bindings, null, new SpawnRank(true, 0, 2, 0))) == null, "Unread native spawns name no start for a lower priority");
        // A start setup reads its spawn point whatever its activity or priority, but whether the map's native spawns
        // take its place is not settled, so with native spawns the setup line claims no start here.
        var withSetup = bindings.Concat(new[] { SpawnPoints.SetupBinding("ewh-steam") }).ToArray();
        var setup = SpawnPoints.Uses(withSetup, 0, SpawnRank.Inactive).Single(line => line.Contains("ewh-steam"));
        Assert(setup.StartsWith("Start setup: a new company game using ewh-steam is expected to start here", StringComparison.Ordinal), "Without native spawns the setup starts here: " + setup);
        var nativeSetup = SpawnPoints.Uses(withSetup, 2).Single(line => line.Contains("ewh-steam"));
        Assert(nativeSetup == "Start setup: named by ewh-steam. The map declares 2 native spawns, so it isn't known whether a new company game using this setup starts here or at a native spawn.",
            "With native spawns the setup line claims no start here: " + nativeSetup);
        var unreadSetup = SpawnPoints.Uses(withSetup, null).Single(line => line.Contains("ewh-steam"));
        Assert(unreadSetup.Contains("is expected to start here") && unreadSetup.Contains("native spawns couldn't be read"), "Unread native spawns make the setup start conditional: " + unreadSetup);
    }
    static void UnreadSetupsAreNamedAsUnread() {
        var uses = SpawnPoints.Uses(new[] { SpawnPoints.NameBinding("Alarka"), SpawnPoints.SetupsUnreadBinding("SetupDescriptor.spawnPoint is missing") }, 0);
        Assert(uses.Any(line => line.Contains("start setups couldn't be read") && line.Contains("spawnPoint is missing")) && !uses.Any(line => line.Contains("No start setup")),
            "Unread setups are named as unread, never as no setup: " + String.Join(" | ", uses));
        Assert(SpawnPoints.SetupBinding("  ") == null, "A setup without an identifier has no binding of its own; the adapter names it by its object");
    }
    static void RelationshipsAreRefusedBeforeMutation() {
        var (world, spawn, scene) = Setup();
        foreach (var (item, expected) in new[] {
            (Spawn("marker", extra: ",\"relationships\":{\"trackMarker\":{\"segment\":\"S1\",\"end\":\"a\",\"distance\":1}}"), "track-marker"),
            (Spawn("starter", placement: false, extra: ",\"starterPlacement\":{\"setup\":\"alarka-jct-start\",\"track\":{\"segment\":\"S1\",\"end\":\"a\",\"distance\":1}}"), StarterPlacements.NotAStarter) }) {
            var outcome = new PatchEngine(scene).Apply(Doc(item)).Outcomes.Values.Single();
            Assert(!outcome.Applied && outcome.Error.Contains(expected) && world.Mutations == 0 && Unedited(spawn), "A relationship on a spawn point is refused before mutation: " + outcome.Error);
        }
    }
    // The dependency minimum follows the content, whether or not this build can edit spawn points.
    static void MinimumHoldsWithoutTheCapability() {
        var capabilities = new SceneCapabilities(() => new[] { "map" });
        capabilities.Add(new Scene { Maps = new[] { "map" } });
        capabilities.AddUnavailable(SpawnPoints.Structure, "The scene spawn point type is unavailable.");
        var store = new Store { Patch = Doc(Spawn("move-alarka")) }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(capabilities), mapEditor, store); mapEditor.Session = session;
        Assert(session.MinimumPatcherVersion == SpawnPoints.RequiredVersion, "A spawn point placement needs its version without the capability: " + session.MinimumPatcherVersion);
        var bare = Doc(Spawn("by-component").Replace(",\"structure\":\"spawn-point-v1\"", ""));
        var other = new Store { Patch = bare }; var otherEditor = new NativeMapEditor();
        var byComponent = new PatchEditingSession(new PatchEngine(capabilities), otherEditor, other); otherEditor.Session = byComponent;
        Assert(byComponent.MinimumPatcherVersion == SpawnPoints.RequiredVersion, "A target named by its spawn point component alone needs it too: " + byComponent.MinimumPatcherVersion);
    }
    // A spawn point root carries a family of its own, so the scene object rule makes it no group, also when it has
    // something physical, and no part of a group either.
    static void SpawnPointRootIsNoGroupRow() {
        var verdicts = SceneObjectRules.Classify(new Node("World", new Node("POIs",
            new Node("Alarka").With(SpawnPoints.Component).Family(),
            new Node("Bryson").With(SpawnPoints.Component, "UnityEngine.BoxCollider").Solid().Family())));
        foreach (var path in new[] { "World/POIs/Alarka", "World/POIs/Bryson" })
            Assert(verdicts.Single(v => v.Path == path).Disposition == GroupDisposition.ExistingFamily, path + " is no group row: " + verdicts.Single(v => v.Path == path).Disposition);
        Assert(SpawnPoints.FamilyOf(SpawnPoints.Component, SceneObjectRules.WorldRoot) == SpawnPoints.Family, "The family rule the inventory calls lists it");
    }
}
