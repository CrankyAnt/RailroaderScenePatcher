// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The road bridges of the base-game map join the Scene object and Child object families under Bridge /
// Road bridges, each with its parts. A road bridge carries a renderer culler and its own scene ground mask on its root,
// so a move takes the mask's registration along, with the terrain output at the old and the new place tracked to
// completion. The roads themselves are native map data.
static class RoadBridgeTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Road bridges: " + message); }
    const string RectangleMask = "Map.Runtime.MaskComponents.RectangleMapMask", RendererCuller = "Helpers.Culling.RendererCuller";
    const string GovIsland = "World/Roads Bryson/US19 Gov Island", GovBridge = GovIsland + "/Concrete Road Bridge - 60m";
    // The road bridge roots of the Railroader 0.6.3 scene (level5).
    static readonly string[] Roots = {
        "World/Large Scenery/Almond/Almond Road Bridge", "World/Large Scenery/Nantahala/Whittier Haywood Road Bridge",
        "World/Roads Bryson/Concrete Road Bridge - 20m", GovBridge,
        "World/Roads Dillsboro/Dillsboro Haywood Road Bridge", "World/Roads Dillsboro/Dillsboro Road Bridge 1", "World/Roads Dillsboro/Dillsboro Road Bridge 2", "World/Roads Dillsboro/Old Home Town Road Bridge",
        "World/Roads Sylva/Sylva Road Bridge", "World/Roads Sylva/Sylva Road Bridge 20m", "World/Roads Sylva/Sylva Road Bridge 20m (1)",
        "World/Roads Whittier/Concrete Road Bridge - 20m", "World/Roads Whittier/Whittier Road Bridge", "World/Roads Wilmot/Wilmot Road Bridge" };
    // A road bridge as the scene has it: the mask and the renderer culler on the root, above the deck and the abutments.
    static Node Bridge(string name) => new Node(name, new Node("Deck").Solid(), new Node("Abutment N").Solid(), new Node("Abutment S").Solid()).With(RectangleMask, RendererCuller);
    internal static Node World() {
        var folders = new Dictionary<string, List<Node>>();
        var setup = new Node("Grade Crossing Setup", new Node("Mast").Solid()).With(SceneObjectRules.CrossingController).Family();
        var island = new List<Node> { setup };
        foreach (var path in Roots) {
            var parts = path.Split('/');
            var folder = path == GovBridge ? island : folders.TryGetValue(parts[parts.Length - 2], out var found) ? found : folders[parts[parts.Length - 2]] = new List<Node>();
            folder.Add(Bridge(parts[parts.Length - 1]));
        }
        folders["Roads Bryson"].Add(new Node("US19 Gov Island", island.ToArray()));
        var towns = new[] { "Almond", "Nantahala" }.Select(town => new Node(town, folders[town].ToArray())).ToArray();
        return new Node("World", new[] { new Node("Large Scenery", towns) }.Concat(folders.Where(f => f.Key.StartsWith("Roads ", StringComparison.Ordinal)).Select(f => new Node(f.Key, f.Value.ToArray()))).ToArray());
    }
    static SceneObjectVerdict At(IReadOnlyList<SceneObjectVerdict> verdicts, string path) => verdicts.SingleOrDefault(v => v.Path == path);

    public static void Run() {
        InventoryListsEachRoadBridgeOnceWithItsParts();
        MoveCarriesMaskCullingAndColliders();
        PartMoveLeavesTheMaskOfTheRoot();
        DisabledMaskStaysUnregistered();
        FlatteningMaskMovesWithTheBridge();
        ActivityReleasesAndRegistersTheMask();
        FailuresRestorePlacementMaskAndCulling();
        TerrainWriteFailureIsIdentifiableAndRetryable();
        EditingHistorySaveAndReload();
        ObjectTabNamesTheMaskAndTheNativeRoad();
        RoadBridgesDoNotFollowTrack();
        UseInGameReadsTheRoadBridgeSubgroup();
        Console.WriteLine("PASS: road bridges");
    }

    // A road bridge stands where its road goes, and Follow follows track: a track-following placement of a road bridge
    // or of one of its parts is refused before any mutation, and the Follow placement mode is unavailable with the reason.
    static void RoadBridgesDoNotFollowTrack() {
        Assert(SceneObjectRules.FollowingRefusal(BridgeKind.Road) == "Road bridges use fixed placement. They don't support track-following placement."
            && SceneObjectRules.FollowingRefusal(BridgeKind.Rail) == SceneObjectRules.RailBridgeNoFollowing && SceneObjectRules.FollowingRefusal(BridgeKind.None) == null,
            "Bridges of both kinds refuse track following, each with its reason; other groups do not: " + SceneObjectRules.FollowingRefusal(BridgeKind.Road));
        var f = new Span(); var scene = f.Scene(); scene.Tracks = new FixedTrack();
        string Follow(string id, string path, string structure) =>
            "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}," +
            "\"placement\":{\"mode\":\"trackFollowing\",\"track\":{\"segment\":\"S1\",\"end\":\"a\",\"distance\":10},\"offset\":[0,0,0],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var result = new PatchEngine(scene).Apply(Doc(Follow("follow-bridge", GovBridge, SceneObjects.Structure), Follow("follow-deck", GovBridge + "/Deck", ChildObjects.Structure)));
        Assert(result.Outcomes.Values.All(o => !o.Applied && o.Failure == FailureKind.Refused && o.Error.Contains("Road bridges use fixed placement")) && f.World.Mutations == 0 && f.RegisteredOnceAt(Authored, 90) && f.Terrain.Rebuilds == 0,
            "Track-following placement of a road bridge and of its part is refused before mutation: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Failure + " " + o.Error)));
        var roadEdits = SupportedEdits.Of(EditEntries.Of(SceneObjectRules.Family, SceneObjects.Structure, CommandBand.GroupOperations, BridgeKind.Road));
        Assert(SupportedEdits.ModeBlock(roadEdits, CommandBand.FollowMode, true) == "Uses fixed placement"
            && SupportedEdits.ModeBlock(roadEdits, CommandBand.NumericMode, true) == null && SupportedEdits.ModeBlock(roadEdits, CommandBand.GizmoMode, true) == null,
            "Follow is unavailable for a road bridge with the reason; Numeric and Gizmo stay: " + SupportedEdits.ModeBlock(roadEdits, CommandBand.FollowMode, true));
    }
    sealed class FixedTrack : ITrackPoseScene { public TrackPose TrackPose(TrackAnchor anchor) => new TrackPose(new[] { 3000.0, 530, 4300 }, new[] { 0.0, 0, 0, 1 }); }

    // The Object tab reads from the inventory that a group is a road bridge: the bridge's own entry, and for a part the
    // entry of its group. A bridge without a mask line, as one whose mask component is disabled, names no terrain.
    static void UseInGameReadsTheRoadBridgeSubgroup() {
        SceneObservation Entry(string path, string[] dependencies, string groupPath = null, string subcategory = null) =>
            new SceneObservation(new object(), new TargetIdentity(SceneName, path, "UnityEngine.Transform"), SceneObjectRules.Family, true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 },
                new string[0], dependencies, new string[0], CommandBand.GroupOperations, groupPath: groupPath, category: groupPath == null ? SceneObjectCategories.Bridge : null, subcategory: subcategory, bridge: subcategory == SceneObjectCategories.RoadBridges ? BridgeKind.Road : BridgeKind.None);
        var line = CommandBand.GroundMaskDependency(true, new[] { "Concrete Road Bridge - 60m" }, false, group: true);
        var bridge = Entry(GovBridge, new[] { "Culling", line }, subcategory: SceneObjectCategories.RoadBridges);
        Assert(CommandBand.UseInGame(bridge, null) == "The bridge is decorative. Its road is native map data that the Map Editor edits. The bridge's ground mask shapes the ground and the vegetation where it stands, so moving or disabling this group changes the terrain there.",
            "The road bridge's entry: " + CommandBand.UseInGame(bridge, null));
        Assert(CommandBand.UseInGame(Entry(GovBridge + "/Deck", new[] { "Culling" }, GovBridge), bridge) == CommandBand.PartDecorative, "A part of a road bridge keeps the part's text");
        // The text follows what the group is for its edits, not where the outliner files it.
        var filedOnly = new SceneObservation(new object(), new TargetIdentity(SceneName, GovBridge, "UnityEngine.Transform"), SceneObjectRules.Family, true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 },
            new string[0], new[] { "Culling", line }, new string[0], CommandBand.GroupOperations, category: SceneObjectCategories.Bridge, subcategory: SceneObjectCategories.RoadBridges);
        Assert(!CommandBand.UseInGame(filedOnly, null).Contains("Its road is native map data"), "A group the outliner files under Road bridges that is no road bridge for its edits has no road bridge text: " + CommandBand.UseInGame(filedOnly, null));
        var unmasked = Entry(Whittier, new[] { "Culling" }, subcategory: SceneObjectCategories.RoadBridges);
        Assert(CommandBand.UseInGame(unmasked, null) == "The bridge is decorative. Its road is native map data that the Map Editor edits. Moving or disabling this group changes no game function." && !CommandBand.HoldsGroundMask(unmasked.Dependencies),
            "A road bridge whose mask is disabled names no terrain and has no terrain output: " + CommandBand.UseInGame(unmasked, null));
        var rail = Entry("World/Large Scenery/Plate Girder Bridge/Plate Girder Bridge", new[] { "Culling" }, subcategory: SceneObjectCategories.RailBridges);
        Assert(CommandBand.UseInGame(rail, null) == CommandBand.Decorative, "A rail bridge without a controller keeps its text");
    }

#if SCENE_PATCHER_FAULTS
    // A failure after the bridge moved and its one mask was rebuilt at the new place: the restoration rebuilds the mask
    // back, the placement, culling and registration are as before, the terrain at both places is outstanding, and the
    // next edit waits until the game has rebuilt it.
    internal static void RunFailureAfterRebuild() {
        var f = new Span();
        FaultInjection.Arm(FaultInjection.AfterMutation);
        Outcome outcome;
        try { outcome = f.Engine.Apply(Doc(GroupMod("move-bridge", Far, yaw: 40))).Outcomes["move-bridge"]; } finally { FaultInjection.Disarm(); }
        Assert(!outcome.Applied && outcome.Error.Contains("back as it was") && f.Terrain.Rebuilds == 2, "The failed attempt is restored with a second rebuild: " + outcome.Failure + " " + outcome.Error + " rebuilds=" + f.Terrain.Rebuilds);
        Assert(Close(f.At(f.Group), Authored) && f.RegisteredOnceAt(Authored, 90) && Close(f.Culls(), Authored) && f.CollidersFollow(), "Placement, culling, colliders and the one registration are restored: " + f.Where());
        var output = f.Masks.Output(f.Group);
        Assert(!output.Settled && output.Error == null && output.Outstanding.Any(l => l.Contains("x=2100,")) && output.Outstanding.Any(l => l.Contains("x=2700,")), "The terrain at both places is outstanding: " + String.Join("; ", output.Outstanding));
        var again = f.Engine.Apply(Doc(GroupMod("again", Near))).Outcomes["again"];
        Assert(!again.Applied && again.Failure == FailureKind.AwaitingOutput && Close(f.At(f.Group), Authored), "The next edit waits for the restored terrain output: " + again.Failure + " " + again.Error);
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(f.Masks.Output(f.Group).Settled && f.Engine.Apply(Doc(GroupMod("later", Near))).Outcomes["later"].Applied && f.RegisteredOnceAt(Near, 90), "Completed terrain output lifts the guard: " + f.Where());
    }
#endif

    // The Object tab of a road bridge: its root holds the ground mask, so the dependency line names the group, not a
    // part; Use in the game says the bridge is decorative, that its road is native map data, and what the mask does.
    // A bridge whose mask flattens the terrain height names that, and moving it stays offered.
    static void ObjectTabNamesTheMaskAndTheNativeRoad() {
        var line = CommandBand.GroundMaskDependency(true, new[] { "Concrete Road Bridge - 60m" }, false, group: true);
        Assert(line == "Ground mask: the root of this group has the scene ground mask. Moving the group moves the mask and its effects on the terrain. The patcher asks the game to rebuild terrain and vegetation at both positions.",
            "The line of a group whose root holds the mask: " + line);
        var flat = CommandBand.GroundMaskDependency(true, new[] { "Old Home Town Road Bridge" }, true, group: true);
        Assert(flat == "Ground mask: the root of this group has the scene ground mask. The mask also flattens the ground. Moving the group moves the mask and its effects on the terrain. The patcher asks the game to rebuild terrain and vegetation at both positions.",
            "The line of a group whose root holds a flattening mask: " + flat);
        Assert(CommandBand.HoldsGroundMask(new[] { line }) && CommandBand.HoldsGroundMask(new[] { flat }), "Both lines carry terrain output");
        Assert(CommandBand.GroundMaskDependency(true, new[] { "Mask" }, false).StartsWith("Ground mask: this part has the scene ground mask.", StringComparison.Ordinal), "A part that holds the mask keeps its line");
        var use = CommandBand.GroundMaskUse(CommandBand.Decorative, false, new[] { "Culling", line }, roadBridge: true);
        Assert(use == "The bridge is decorative. Its road is native map data that the Map Editor edits. The bridge's ground mask shapes the ground and the vegetation where it stands, so moving or disabling this group changes the terrain there.",
            "The use of a road bridge: " + use);
        var held = CommandBand.GroundMaskUse(CommandBand.Decorative, false, new[] { flat }, roadBridge: true);
        Assert(held == "The bridge is decorative. Its road is native map data that the Map Editor edits. The bridge's ground mask shapes the ground and the vegetation where it stands, so moving or disabling this group changes the terrain there.",
            "The use of a road bridge with a flattening mask names the move: " + held);
        var bare = CommandBand.GroundMaskUse(CommandBand.Decorative, false, new[] { "Culling" }, roadBridge: true);
        Assert(bare == "The bridge is decorative. Its road is native map data that the Map Editor edits. Moving or disabling this group changes no game function.", "The use of a road bridge without a mask: " + bare);
        Assert(CommandBand.GroundMaskUse(CommandBand.PartDecorative, true, new[] { "Culling" }, roadBridge: true) == CommandBand.PartDecorative, "A part of a road bridge keeps the part's text");
        Assert(CommandBand.GroundMaskUse(CommandBand.Decorative, false, new[] { CommandBand.GroundMaskDependency(false, new[] { "Mask" }, false) }) == "The building is decorative. The ground mask of its part Mask shapes the ground and the vegetation where it stands, so moving or disabling this group changes the terrain there.",
            "A building keeps its text");
    }

    // Each road bridge root is listed once as a group of the family under Bridge / Road bridges, with its parts; the
    // 60-metre bridge inside US19 Gov Island is one of them, beside that folder's grade-crossing setup, which keeps
    // its own family.
    static void InventoryListsEachRoadBridgeOnceWithItsParts() {
        var verdicts = SceneObjectRules.Classify(World());
        var parts = SceneObjectRules.Parts(verdicts).Where(p => p.Disposition == PartDisposition.Part).ToArray();
        foreach (var path in Roots) {
            var found = verdicts.Where(v => v.Path == path).ToArray();
            Assert(found.Length == 1 && found[0].Disposition == GroupDisposition.Group && found[0].Category == SceneObjectCategories.Bridge && found[0].Subcategory == SceneObjectCategories.RoadBridges,
                path + " is listed once as a group under Bridge / Road bridges: " + String.Join(", ", found.Select(v => v.Disposition + " " + v.Category + "/" + v.Subcategory + " " + v.Reason)));
            Assert(parts.Where(p => p.GroupPath == path).Select(p => p.Path.Substring(path.Length + 1)).SequenceEqual(new[] { "Deck", "Abutment N", "Abutment S" }), path + " lists its parts");
        }
        Assert(At(verdicts, GovIsland) == null && At(verdicts, GovIsland + "/Grade Crossing Setup")?.Disposition == GroupDisposition.ExistingFamily, "US19 Gov Island stays a folder, and its grade-crossing setup keeps its family");
        Assert(!parts.Any(p => p.GroupPath == GovBridge && p.Path.Contains("Grade Crossing Setup")), "The grade-crossing setup is no part of the road bridge beside it");
        Assert(SceneObjectRules.PartName(GovBridge, GovBridge + "/Deck") == "Concrete Road Bridge - 60m / Deck", "A part is named with its bridge");
    }

    // The 60-metre bridge inside US19 Gov Island and, as the unedited control, the 20-metre bridge beside its folder, as
    // the group and part capabilities see them: each with a renderer culler and a rectangle mask on its root, above a
    // deck and two abutments, and a collider on the deck and on each abutment.
    const string SceneName = "BushnellWhittier-MapManager", Control = "World/Roads Bryson/Concrete Road Bridge - 20m", OldHomeTown = "World/Roads Dillsboro/Old Home Town Road Bridge", Whittier = "World/Roads Whittier/Whittier Road Bridge";
    static readonly double[] Upright = { 0, 0, 0, 1 }, Authored = { 2100, 520, 300 }, Near = { 2110, 520, 300 }, Far = { 2700, 522, 340 }, ControlAt = { 2300, 520, 900 };
    sealed class Span {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeMaskWorld Terrain;
        public readonly GroundMasks Masks;
        public readonly FakeGroup Group = Make(Authored), Other = Make(ControlAt);
        public readonly FakePart Deck;
        public readonly FakeMask Mask, OtherMask;
        public readonly FakeCuller Culler;
        public readonly PatchEngine Engine;
        static FakeGroup Make(double[] at) {
            var group = new FakeGroup { Position = new[] { at[0] - 1000, at[1], at[2] }, Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
            group.Add(new FakePart { Id = "Deck#1", Offset = new[] { 0.0, 4, 0 } });
            group.Add(new FakePart { Id = "Abutment N#2", Offset = new[] { 0.0, 0, 30 } });
            group.Add(new FakePart { Id = "Abutment S#3", Offset = new[] { 0.0, 0, -30 } });
            group.Cullers.Add(new FakeCuller { Id = "RendererCuller#5", OnRoot = true, Simple = false, Radius = 35, Authored = 35 });
            group.Colliders.Add(new FakeGroupCollider { Id = "Deck|BoxCollider#11", Shape = "box size=8,1,60" });
            group.Colliders.Add(new FakeGroupCollider { Id = "Abutment N|BoxCollider#12", Shape = "box size=8,6,2" });
            group.Colliders.Add(new FakeGroupCollider { Id = "Abutment S|BoxCollider#13", Shape = "box size=8,6,2" });
            return group;
        }
        public Span(string path = GovBridge, bool flattens = false, bool maskEnabled = true) {
            Terrain = new FakeMaskWorld(World); Masks = new GroundMasks(Terrain);
            Deck = Group.Parts[0]; Culler = Group.Cullers[0];
            var verdicts = SceneObjectRules.Classify(RoadBridgeTests.World());
            World.GroupVerdicts = verdicts.ToDictionary(v => v.Path, v => v);
            World.Verdicts = SceneObjectRules.Parts(verdicts).GroupBy(v => v.Path).ToDictionary(g => g.Key, g => g.First());
            World.Objects[path] = new List<object> { Group }; World.Objects[Control] = new List<object> { Other };
            World.PartsAt[path + "/Deck"] = new List<object> { Deck };
            World.SetActive(Group, true); World.SetActive(Other, true); World.Mutations = 0;
            Mask = Terrain.Add(new FakeMask { Id = "mask", Holder = path.Substring(path.LastIndexOf('/') + 1), Group = Group, Degrees = 90, Flattens = flattens, ComponentEnabled = maskEnabled });
            OtherMask = Terrain.Add(new FakeMask { Id = "control", Holder = "Concrete Road Bridge - 20m", Group = Other, Degrees = 90 });
            Terrain.BuildTiles();
            Engine = new PatchEngine(Scene());
        }
        public SceneCapabilities Scene() {
            var holds = new PartHolds(); var scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new SceneObjects(World, holds: holds, masks: Masks)); scene.Add(new ChildObjects(World, holds, masks: Masks));
            return scene;
        }
        public double[] At(object handle) => (handle is FakeGroup group ? World.Observe(group).Root.Placement : World.ObservePart(handle).Part.Placement).Position.ToArray();
        double[] Sphere(FakeGroup group) => group.Cullers[0].Registered == null ? null : new[] { group.Cullers[0].Registered[0] + World.Origin, group.Cullers[0].Registered[1], group.Cullers[0].Registered[2] };
        public double[] Culls() => Sphere(Group);
        public bool CollidersFollow() => World.Observe(Group).Colliders.Count == 3 && World.Observe(Group).Colliders.All(c => c.BoundsFollow && c.Enabled);
        // The bridge's mask holds one registration in the terrain system, at the place, with its long side turned yaw
        // degrees from north; the only other registration is the control's.
        public bool RegisteredOnceAt(double[] centre, double yaw) =>
            Mask.Keys.Count == 1 && Terrain.Store.Count == 1 + OtherMask.Keys.Count && Close(Terrain.Store[Mask.Keys[0]].Centre, centre) && Close(Terrain.Store[Mask.Keys[0]].Axis, new[] { Math.Sin(yaw * Math.PI / 180), 0, Math.Cos(yaw * Math.PI / 180) });
        // The control bridge, its culling sphere and its mask's one registration stand where the scene has them.
        public bool ControlUnchanged() => Close(At(Other), ControlAt) && Other.Active && OtherMask.Keys.Count == 1 && Close(Terrain.Store[OtherMask.Keys[0]].Centre, ControlAt) && Close(Sphere(Other), ControlAt);
        public string Where() => "bridge " + N(At(Group)) + "; deck " + N(At(Deck)) + "; culls " + (Culls() == null ? "none" : N(Culls())) + "; colliders follow=" + CollidersFollow() + "; " +
            String.Join("; ", Mask.Keys.Where(Terrain.Store.ContainsKey).Select(k => "registration " + N(Terrain.Store[k].Centre) + " axis " + N(Terrain.Store[k].Axis))) + "; control " + N(At(Other));
    }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static bool Close(IReadOnlyList<double> a, IReadOnlyList<double> b) => a != null && b != null && a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= 0.001);
    static string Mod(string id, string path, string structure, double[] position = null, bool? active = null, double yaw = 0) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + N(position) + "],\"rotation\":[" + String.Join(",", Turn.Yaw(yaw).Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string GroupMod(string id, double[] position = null, bool? active = null, double yaw = 0, string path = GovBridge) => Mod(id, path, SceneObjects.Structure, position, active, yaw);
    static string PartMod(string id, string path, double[] position = null, bool? active = null) => Mod(id, path, ChildObjects.Structure, position, active);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static readonly TargetIdentity BridgeId = new TargetIdentity(SceneName, GovBridge, SceneObjects.ComponentType, SceneObjects.Structure);
    static readonly TargetIdentity DeckId = new TargetIdentity(SceneName, GovBridge + "/Deck", ChildObjects.ComponentType, ChildObjects.Structure);

    // The US19 Gov Island bridge moves and turns as one: its parts, its culling sphere and its colliders follow, and the
    // mask on its root holds one registration at the new centre with the shape turned with the bridge. The terrain at
    // the old and the new place is outstanding until the game rebuilds it. The control bridge keeps its placement,
    // culling sphere and registration. A repeated application and a load at another origin reach the same state.
    static void MoveCarriesMaskCullingAndColliders() {
        var f = new Span();
        Assert(f.RegisteredOnceAt(Authored, 90) && Close(f.Culls(), Authored), "The controlled bridge culls and is registered once where it stands: " + f.Where());
        var doc = Doc(GroupMod("move-bridge", Far, yaw: 40));
        var outcome = f.Engine.Apply(doc).Outcomes["move-bridge"];
        Assert(outcome.Applied, "The bridge move applies: " + outcome.Error);
        Assert(Close(f.At(f.Group), Far) && f.RegisteredOnceAt(Far, 130), "The mask's center and rotation follow the bridge, registered once: " + f.Where());
        Assert(Close(f.Culls(), Far) && f.CollidersFollow() && Close(f.At(f.Deck), new[] { 2700.0, 526, 340 }), "The culling sphere, the colliders and the parts follow the bridge: " + f.Where());
        Assert(f.ControlUnchanged(), "The unedited control keeps its placement, culling and registration: " + f.Where());
        var output = f.Masks.Output(f.Group);
        Assert(output.Outstanding.Count == 2 && !output.Settled && output.Error == null && output.Outstanding.Any(l => l.Contains("x=2100,")) && output.Outstanding.Any(l => l.Contains("x=2700,")),
            "The terrain at the old and the new place is outstanding: " + String.Join("; ", output.Outstanding));
        Assert(f.Masks.Output(f.Other).Outstanding.Count == 0 && f.Masks.Output(f.Other).Completed.Count == 0, "The control has no terrain output");
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(f.Masks.Output(f.Group).Settled && f.Masks.Output(f.Group).Completed.Count == 2, "Rebuilt tiles complete both writes");
        var rebuilds = f.Terrain.Rebuilds;
        Assert(f.Engine.Apply(doc).Outcomes["move-bridge"].Applied && f.RegisteredOnceAt(Far, 130) && f.Terrain.Rebuilds == rebuilds && f.Masks.Output(f.Group).Settled,
            "A repeated application leaves the one registration alone and starts no terrain output: " + f.Where());
        var shifted = new Span(); shifted.World.Origin = 1010;
        Assert(shifted.Engine.Apply(doc).Outcomes["move-bridge"].Applied && shifted.RegisteredOnceAt(Far, 130) && Close(shifted.Culls(), Far), "A load at another origin reaches the same placement and registration: " + shifted.Where());
    }

    // The mask stands on the bridge's root: a move of the deck alone leaves the mask, the root and the other parts
    // where they are, rebuilds nothing and starts no terrain output.
    static void PartMoveLeavesTheMaskOfTheRoot() {
        var f = new Span();
        var outcome = f.Engine.Apply(Doc(PartMod("move-deck", GovBridge + "/Deck", new[] { 2120.0, 525, 310 }))).Outcomes["move-deck"];
        Assert(outcome.Applied && Close(f.At(f.Deck), new[] { 2120.0, 525, 310 }) && Close(f.At(f.Group), Authored), "The deck moves alone: " + outcome.Error + " " + f.Where());
        Assert(f.Terrain.Rebuilds == 0 && f.RegisteredOnceAt(Authored, 90) && f.Masks.Output(f.Deck).Outstanding.Count == 0 && f.Masks.Output(f.Group).Outstanding.Count == 0, "A part move leaves the root's mask and the terrain alone: " + f.Where());
    }

    // The mask of the Whittier Road Bridge is disabled in the scene: it holds no registration, and the bridge moves
    // without one and without terrain output.
    static void DisabledMaskStaysUnregistered() {
        var f = new Span(Whittier, maskEnabled: false);
        Assert(f.Mask.Keys.Count == 0 && f.Terrain.Store.Count == 1, "A disabled mask holds no registration");
        var outcome = f.Engine.Apply(Doc(GroupMod("move-bridge", Far, path: Whittier))).Outcomes["move-bridge"];
        Assert(outcome.Applied && Close(f.At(f.Group), Far) && Close(f.Culls(), Far) && f.Mask.Keys.Count == 0 && f.Terrain.Store.Count == 1 && f.Terrain.Rebuilds == 0 && f.Masks.Output(f.Group).Outstanding.Count == 0,
            "The bridge moves, and its disabled mask stays unregistered without terrain output: " + outcome.Error + " " + f.Where());
        // Disabling and enabling the bridge leaves the disabled mask unregistered too.
        var hidden = new PatchEngine(f.Scene()).Apply(Doc(GroupMod("hide", active: false, path: Whittier))).Outcomes["hide"];
        Assert(hidden.Applied && !f.Group.Active && f.Culls() == null && f.Mask.Keys.Count == 0 && f.Masks.Output(f.Group).Outstanding.Count == 0, "A disable applies without terrain output: " + hidden.Error);
        var shown = new PatchEngine(f.Scene()).Apply(Doc(GroupMod("show", active: true, path: Whittier))).Outcomes["show"];
        Assert(shown.Applied && f.Group.Active && Close(f.Culls(), Far) && f.Mask.Keys.Count == 0 && f.Terrain.Store.Count == 1 && f.Terrain.Rebuilds == 0 && f.Masks.Output(f.Group).Outstanding.Count == 0 && f.Masks.Output(f.Group).Completed.Count == 0,
            "An enable applies, and the disabled mask still holds no registration and starts no terrain output: " + shown.Error + " " + f.Where());
    }

    // The mask of the Old Home Town Road Bridge also flattens the terrain height: it moves with the bridge, the terrain
    // output at the old and the new place is tracked, and in scene patch editing Undo returns the bridge and its mask.
    // Enabling and disabling stay possible.
    static void FlatteningMaskMovesWithTheBridge() {
        var f = new Span(OldHomeTown, flattens: true);
        var outcome = f.Engine.Apply(Doc(GroupMod("move-bridge", Far, path: OldHomeTown))).Outcomes["move-bridge"];
        Assert(outcome.Applied && f.RegisteredOnceAt(Far, 90) && f.Masks.Output(f.Group).Outstanding.Count == 2,
            "The move carries the flattening mask and tracks both places: " + outcome.Error + " " + f.Where());
        var s = new Span(OldHomeTown, flattens: true); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(s.Engine, mapEditor, store); mapEditor.Session = session;
        var id = new TargetIdentity(SceneName, OldHomeTown, SceneObjects.ComponentType, SceneObjects.Structure);
        Assert(session.Edit(id, new Placement(Far, Upright), null).Applied && s.RegisteredOnceAt(Far, 90) && mapEditor.IsDirty && mapEditor.CanUndo, "In scene patch editing the move applies: " + s.Where());
        mapEditor.Undo(); Assert(s.RegisteredOnceAt(Authored, 90) && !mapEditor.IsDirty, "Undo returns the bridge and its mask: " + s.Where());
        Assert(session.Edit(id, null, false).Applied && !s.Group.Active && s.Mask.Keys.Count == 0, "A disable of the flattening bridge applies and releases its mask");
        mapEditor.Undo(); Assert(s.Group.Active && s.RegisteredOnceAt(Authored, 90), "Undo enables it with its mask registered once: " + s.Where());
    }

    // Disabling the bridge releases its culling sphere and, through the component's own lifecycle, its mask; enabling
    // registers both once again. The terrain at the mask's place is tracked both times.
    static void ActivityReleasesAndRegistersTheMask() {
        var f = new Span();
        Assert(f.Engine.Apply(Doc(GroupMod("hide", active: false))).Outcomes["hide"].Applied && !f.Group.Active && f.Mask.Keys.Count == 0 && f.Terrain.Store.Count == 1 && f.Culls() == null, "A disabled bridge holds no registration and no culling sphere: " + f.Where());
        Assert(f.Masks.Output(f.Group).Outstanding.Count == 1, "The terrain where the mask was is outstanding");
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(new PatchEngine(f.Scene()).Apply(Doc(GroupMod("hide", Near, false))).Outcomes["hide"].Applied && f.Mask.Keys.Count == 0 && f.Masks.Output(f.Group).Settled, "A disabled bridge moves without registering and without terrain output");
        Assert(new PatchEngine(f.Scene()).Apply(Doc(GroupMod("show", active: true))).Outcomes["show"].Applied && f.RegisteredOnceAt(Near, 90) && Close(f.Culls(), Near), "Enabling registers the mask and the culling sphere once where the bridge stands now: " + f.Where());
        Assert(f.ControlUnchanged(), "The control is unchanged: " + f.Where());
    }

    // A preflight refusal leaves the bridge and the scene patch file unchanged. A failure after the bridge moved
    // restores its placement, activity, culling, colliders and the mask's one registration; a rebuild that failed
    // changed no registration, so no terrain output is outstanding. A restoration that cannot be verified is incomplete
    // cleanup, which is reported and blocks the retry on that bridge while another bridge still takes an edit.
    static void FailuresRestorePlacementMaskAndCulling() {
        var refused = new Span(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(refused.Engine, mapEditor, store); mapEditor.Session = session;
        refused.World.Refused = "The group's scene is not loaded";
        var edit = session.Edit(BridgeId, new Placement(Far, Upright), null);
        Assert(!edit.Applied && refused.World.Mutations == 0 && store.Patch == null && !mapEditor.IsDirty && refused.RegisteredOnceAt(Authored, 90) && Close(refused.Culls(), Authored) && refused.Terrain.Rebuilds == 0,
            "A preflight refusal leaves the scene and the scene patch file unchanged: " + edit.Error + " " + refused.Where());

        var thrown = new Span(); thrown.World.ThrowAfterPlacement = true;
        var outcome = thrown.Engine.Apply(Doc(GroupMod("move-bridge", Far, false, 40))).Outcomes["move-bridge"];
        Assert(!outcome.Applied && outcome.Error.Contains("back as it was") && Close(thrown.At(thrown.Group), Authored) && thrown.Group.Active && Close(thrown.Culls(), Authored) && thrown.CollidersFollow() && thrown.RegisteredOnceAt(Authored, 90),
            "A failure after mutation restores placement, activity, culling, colliders and the registration: " + outcome.Error + " " + thrown.Where());

        var failed = new Span(); failed.Terrain.FailRebuildCount = 1;
        outcome = failed.Engine.Apply(Doc(GroupMod("move-bridge", Far, yaw: 40))).Outcomes["move-bridge"];
        Assert(!outcome.Applied && outcome.Error.Contains("back as it was") && outcome.Error.Contains("mask injected") && Close(failed.At(failed.Group), Authored) && failed.RegisteredOnceAt(Authored, 90) && Close(failed.Culls(), Authored),
            "A failed mask rebuild restores the bridge and its registration: " + outcome.Error + " " + failed.Where());
        Assert(failed.Masks.Output(failed.Group).Settled && failed.Terrain.Store.Count == 2, "The failed rebuild left no terrain output outstanding and no second registration");
        Assert(failed.Engine.Apply(Doc(GroupMod("later", Far))).Outcomes["later"].Applied && failed.RegisteredOnceAt(Far, 90), "The next edit applies: " + failed.Where());

        var detached = new Span(); detached.World.DetachCollider = true;
        outcome = detached.Engine.Apply(Doc(GroupMod("move-bridge", Far))).Outcomes["move-bridge"];
        Assert(!outcome.Applied && outcome.Error.Contains("collider") && Close(detached.At(detached.Group), Authored) && detached.CollidersFollow() && detached.RegisteredOnceAt(Authored, 90) && detached.Terrain.Rebuilds == 0,
            "A collider that does not follow fails the move before the mask is rebuilt, and the bridge is restored: " + outcome.Error + " " + detached.Where());

        var stuck = new Span(); stuck.World.DetachCollider = stuck.World.KeepColliderDetached = true;
        outcome = stuck.Engine.Apply(Doc(GroupMod("move-bridge", Far))).Outcomes["move-bridge"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        stuck.World.KeepColliderDetached = false; var mutations = stuck.World.Mutations;
        var retry = stuck.Engine.Apply(Doc(GroupMod("again", Near))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && stuck.World.Mutations == mutations, "The retry on the affected bridge is blocked until cleanup is verified: " + retry.Error);
        var beside = stuck.Engine.Apply(Doc(GroupMod("control", new[] { 2310.0, 520, 900 }, path: Control))).Outcomes["control"];
        Assert(beside.Applied, "Another bridge still takes an edit: " + beside.Error);

        // Two masks: the first follows the move, the second cannot be rebuilt, and neither can the first on the way back.
        var mask = new Span();
        mask.Terrain.Add(new FakeMask { Id = "second", Holder = "Deck", Group = mask.Group, On = mask.Deck });
        mask.Terrain.FailAfterRebuilds = mask.Terrain.Rebuilds + 1;
        outcome = mask.Engine.Apply(Doc(GroupMod("move-bridge", Far))).Outcomes["move-bridge"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "A restoration that cannot rebuild a mask is reported as incomplete cleanup: " + outcome.Error);
        mask.Terrain.FailAfterRebuilds = -1; mutations = mask.World.Mutations;
        retry = mask.Engine.Apply(Doc(GroupMod("again", Near))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && mask.World.Mutations == mutations, "That retry is blocked too: " + retry.Error);
    }

    // A terrain write that does not complete: the completed and the outstanding write stay identifiable, the edit is
    // kept, and a retry requests only the outstanding write again and is safe to repeat.
    static void TerrainWriteFailureIsIdentifiableAndRetryable() {
        var f = new Span(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        Assert(session.Edit(BridgeId, new Placement(Far, Upright), null).Applied, "The bridge move applies");
        // The old place's tile is rebuilt; the invalidation of the new place's tile is lost before the game builds it.
        f.Terrain.PendingTiles.Remove("5,0"); f.Terrain.BuildTiles(); f.Masks.Poll();
        f.Terrain.Clock = TerrainOutput.Timeout + 1; f.Masks.Poll();
        var output = session.Output(BridgeId);
        Assert(output.Error != null && output.Completed.Count == 1 && output.Outstanding.Count == 1 && !output.Settled, "The failed write names its completed and outstanding writes: " + output.Error);
        Assert(output.Completed[0].Contains("x=2100,") && output.Outstanding[0].Contains("x=2700,"), "Each write names its place: " + output.Completed[0] + " | " + output.Outstanding[0]);
        Assert(f.Masks.Failures.Count() == 1 && Close(f.At(f.Group), Far) && f.RegisteredOnceAt(Far, 90) && Close(f.Culls(), Far), "The edit, its culling and its registration are kept, and the failure is identifiable");
        Assert(mapEditor.Save() == null && !session.Output(BridgeId).Settled, "Save does not report the terrain output complete");
        Assert(session.RetryOutput(BridgeId).Applied && session.RetryOutput(BridgeId).Applied && f.Terrain.Invalidated.SequenceEqual(new[] { "5,0", "5,0" }) && session.Output(BridgeId).Error == null && !session.Output(BridgeId).Settled && f.RegisteredOnceAt(Far, 90),
            "A retry requests only the outstanding write again, and repeating it changes nothing else: " + String.Join(",", f.Terrain.Invalidated));
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(session.Output(BridgeId).Settled && session.Output(BridgeId).Completed.Count == 2 && !f.Masks.Failures.Any() && !session.RetryOutput(BridgeId).Applied, "The retried write completes; nothing is left to retry");
    }

    // In scene patch editing: Undo and Redo of a bridge move with its mask, culling and terrain output and matching
    // dirty state, Remove modification returning the state computed from the scene and the remaining modifications,
    // Save at format version 1 with the patcher dependency, and a reload that reproduces the move with one
    // registration and one culling sphere.
    static void EditingHistorySaveAndReload() {
        var f = new Span(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Assert(session.Edit(BridgeId, new Placement(Far, Turn.Yaw(40)), null).Applied && f.RegisteredOnceAt(Far, 130) && Close(f.Culls(), Far), "The bridge move carries its mask and culling: " + f.Where());
        Persistence(true, all, "After the move");
        mapEditor.Undo(); Assert(Close(f.At(f.Group), Authored) && f.RegisteredOnceAt(Authored, 90) && Close(f.Culls(), Authored) && f.CollidersFollow(), "Undo returns the bridge, its mask and its culling: " + f.Where());
        Persistence(false, new string[0], "After Undo");
        mapEditor.Redo(); Assert(Close(f.At(f.Group), Far) && f.RegisteredOnceAt(Far, 130) && Close(f.Culls(), Far), "Redo reapplies the move, the mask and the culling: " + f.Where());
        Persistence(true, all, "After Redo");
        Assert(session.Output(BridgeId).Outstanding.Count == 2, "The history steps track the terrain at both places: " + String.Join("; ", session.Output(BridgeId).Outstanding));
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(session.Output(BridgeId).Settled, "The terrain output of the history steps completes");
        // The deck gets a placement of its own; removing the bridge's modification then returns the bridge and its mask
        // to where the scene has them, and the deck keeps its own placement relative to the bridge, so it returns with it.
        var held = new[] { 2720.0, 527, 350 };
        Assert(session.Edit(DeckId, new Placement(held, Upright), null).Applied, "The deck takes a placement of its own");
        var removed = session.Remove(BridgeId);
        var inBridge = Turn.Rotate(Turn.Inverse(Turn.Yaw(40)), new[] { held[0] - Far[0], held[1] - Far[1], held[2] - Far[2] });
        var deckHome = new[] { Authored[0] + inBridge[0], Authored[1] + inBridge[1], Authored[2] + inBridge[2] };
        Assert(removed.Applied && Close(f.At(f.Group), Authored) && f.RegisteredOnceAt(Authored, 90) && Close(f.Culls(), Authored) && Close(f.At(f.Deck), deckHome),
            "Remove modification returns the bridge, its mask and its culling to the scene's state, and the deck keeps its own placement relative to the bridge: " + removed.Error + " " + f.Where());
        Assert(session.Output(BridgeId).Outstanding.Count == 2, "The removal tracks the terrain at both places");
        f.Terrain.BuildTiles(); f.Masks.Poll();
        mapEditor.Undo(); Assert(Close(f.At(f.Group), Far) && f.RegisteredOnceAt(Far, 130) && Close(f.At(f.Deck), held), "Undo of the removal moves the bridge and its mask again: " + f.Where());
        Persistence(true, all, "After Undo of the removal");
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=" + SceneObjects.RequiredVersion) && (int)JObject.Parse(store.Patch)["formatVersion"] == 1, "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        var fresh = new Span();
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Count == 2 && reload.Outcomes.Values.All(o => o.Applied) && fresh.RegisteredOnceAt(Far, 130) && Close(fresh.Culls(), Far) && Close(fresh.At(fresh.Deck), held) && fresh.Group.Cullers.Count == 1 && fresh.Group.Colliders.Count == 3,
            "A reloaded world reproduces the move with one registration and no duplicated state: " + String.Join("; ", reload.Outcomes.Values.Select(o => o.Error)) + " " + fresh.Where());
        Assert(fresh.Engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && fresh.RegisteredOnceAt(Far, 130), "Applying the saved file again adds no registration: " + fresh.Where());
        Assert(fresh.ControlUnchanged(), "The control is unchanged after the reload: " + fresh.Where());
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = new Span(); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(other.Engine, strictEditor, strict); strictEditor.Session = kept;
        Assert(kept.Edit(BridgeId, new Placement(Far, Upright), null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=" + SceneObjects.RequiredVersion), "A stricter existing constraint is retained: " + strict.Info);
    }
}
