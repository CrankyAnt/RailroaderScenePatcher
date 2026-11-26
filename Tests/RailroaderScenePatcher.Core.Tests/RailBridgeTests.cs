// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Rail bridges outside Ela: Bryson Bridge 1, the plate girder bridges and the Almond Bridge join the Scene object
// family under Bridge / Rail bridges, each construction with its parts. Controlled hierarchies follow the Railroader 0.6.3
// scene (Records/InventoryLedger level4_mb_rows): Bryson Bridge 1 carries a renderer culler and its track construction
// controller on its root, above Abutments and Steel; the Almond Bridge top scene object below World/Large Scenery carries a simple
// culler and its construction controller above Abutments and Steel; each plate girder bridge is a model of its own
// without a controller.
static class RailBridgeTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Rail bridges: " + message); }
    const string Large = "World/Large Scenery/", Bryson = Large + "Bryson/Bridge 1", Almond = Large + "Almond Bridge", Girders = Large + "Plate Girder Bridge/";
    const string Controller = "Game.Mow.TrackConstructionScenery", RendererCuller = "Helpers.Culling.RendererCuller", SimpleCuller = "Helpers.SimpleCuller";
    static readonly string[] GirderNames = { "Plate Girder Bridge", "Plate Girder Bridge (1)", "Plate Girder Bridge (2)", "Plate Girder Bridge (3)", "Plate Girder Bridge (4)", "Plate Girder Bridge End", "Plate Girder Bridge End (1)" };
    internal static Node World() => new Node("World", new Node("Large Scenery",
        new Node("Almond Bridge", new Node("Abutments", new Node("Bridge Abutment").Solid(), new Node("Bridge Abutment End").Solid()),
            new Node("Steel", new Node("Girder Beam (1)").Solid(), new Node("Truss").Solid())).With(SimpleCuller, Controller),
        new Node("Bryson", new Node("Bridge 1", new Node("Abutments", new Node("Bridge Abutment").Solid(), new Node("Bridge Abutment (1)").Solid()).Solid(),
            new Node("Steel", new Node("Plate Girder W").Solid(), new Node("Plate Girder W").Solid(), new Node("Plate Girder E (4)").Solid()).Solid()).With(RendererCuller, Controller),
            new Node("Bryson Depot", new Node("Roof").Solid())),
        new Node("Plate Girder Bridge", GirderNames.Select(name => new Node(name).Solid()).ToArray()),
        new Node("Almond", new Node("Almond Road Bridge", new Node("Deck").Solid()).With("Map.Runtime.MaskComponents.RectangleMapMask", RendererCuller))));
    static SceneObjectVerdict At(IReadOnlyList<SceneObjectVerdict> verdicts, string path) => verdicts.SingleOrDefault(v => v.Path == path);

    public static void Run() {
        InventoryListsEachConstructionOnceWithItsParts();
        ConstructionMovesWithItsColliders();
        ExplicitDisableHoldsAgainstTheController();
        OwnCullingFollowsTheController();
        OwnCullingTakesOverAnObjectOutOfView();
        RailBridgesDoNotFollowTrack();
        SeparateTrackSceneryStays();
        FailuresRestoreColliderState();
        LoadFailuresShowAsNotApplied();
        ReadAgainShowsItsFailures();
        ImportClearsOnlyWhatItApplied();
        ObjectTabNamesTheConstructionSegments();
        MissingSegmentIsAProblemWithItsRepair();
        BridgeEditsInScenePatchEditing();
        Console.WriteLine("PASS: rail bridges");
    }

    // A construction's top scene object and parts carry one binding line per construction segment, which the Connections tab
    // lists; a segment that no longer exists keeps its id with a remark. The Object tab's Use in the game
    // text names the segments that decide the construction's visibility; a plate girder bridge without a controller is
    // decorative.
    static void ObjectTabNamesTheConstructionSegments() {
        Assert(CommandBand.SegmentBinding("seg-a", true) == "Construction segment=seg-a" && CommandBand.SegmentBinding("seg-a", null) == "Construction segment=seg-a",
            "An existing or unchecked segment is one binding line: " + CommandBand.SegmentBinding("seg-a", true));
        Assert(CommandBand.SegmentBinding("seg-gone", false) == "Construction segment=seg-gone; no longer exists", "A missing segment keeps its id with a remark: " + CommandBand.SegmentBinding("seg-gone", false));
        var bindings = new[] { "Storage piles=0", CommandBand.SegmentBinding("seg-a", true), CommandBand.SegmentBinding("seg-gone", false) };
        Assert(CommandBand.ConstructionSegments(bindings).SequenceEqual(new[] { "seg-a", "seg-gone" }), "The construction segments are read back from the bindings: " + String.Join(", ", CommandBand.ConstructionSegments(bindings)));
        var two = CommandBand.UseInGame(false, SceneObjectCategories.Bridge, null, bindings);
        Assert(two == "The construction state of track segments seg-a and seg-gone decides which parts of this bridge the game shows.", "The construction names its segments: " + two);
        var one = CommandBand.UseInGame(true, SceneObjectCategories.Bridge, null, new[] { CommandBand.SegmentBinding("seg-a", true) });
        Assert(one == "The construction state of track segment seg-a decides which parts of this bridge the game shows.", "A part names its construction's one segment: " + one);
        var three = CommandBand.UseInGame(false, SceneObjectCategories.Bridge, null, new[] { "a", "b", "c" }.Select(id => CommandBand.SegmentBinding(id, true)));
        Assert(three.StartsWith("The construction state of track segments a, b, and c decides", StringComparison.Ordinal), "Three segments: " + three);
        Assert(CommandBand.UseInGame(false, SceneObjectCategories.Bridge, null, new string[0]) == CommandBand.Decorative, "A plate girder bridge without a controller is decorative");
        Assert(CommandBand.ConstructionDependency == "Construction: the game shows or hides parts of this bridge by the construction state of its track segments.",
            "The dependency line: " + CommandBand.ConstructionDependency);
        // The inventory's lines for a controller's segments against the track graph's segment ids, or without a graph.
        var lines = CommandBand.ConstructionBindings(new[] { "seg-a", "seg-gone", " " }, new HashSet<string> { "seg-a" });
        Assert(lines.SequenceEqual(new[] { "Construction segment=seg-a", "Construction segment=seg-gone; no longer exists", "Construction segment= ; no longer exists" }), "The inventory lines mark a missing or empty segment: " + String.Join(" | ", lines));
        Assert(CommandBand.ConstructionBindings(new[] { "seg-a" }, null).SequenceEqual(new[] { "Construction segment=seg-a" }), "Without the track graph a segment is listed unchecked");
        Assert(CommandBand.ConstructionBindings(null, null).Count == 0, "No controller, no lines");
        Assert(CommandBand.ConstructionHoldWarning == "Your explicit disable keeps this object off in the game even when track construction progress switches it on. Click Enable or Clear activity to let construction progress control it again.",
            "The hold warning names the game feature and that the disable holds: " + CommandBand.ConstructionHoldWarning);
    }

    // A construction segment that no longer exists is a defect the modder repairs on the Connections tab (#96): the
    // top scene object of Bryson Bridge 1, the Almond Bridge or any other construction, and the Ela controller, report it as a
    // problem with the same wording, naming the segment and the repair. The problem leaves once the construction names
    // existing segments, and without the track graph there is one inspection note instead. The Object tab offers the
    // construction segment repair, not the track binding task, for such a problem.
    static void MissingSegmentIsAProblemWithItsRepair() {
        var known = new HashSet<string> { "seg-a", "seg-new-1", "seg-new-2" };
        var problems = CommandBand.ConstructionSegmentProblems(new[] { "seg-a", "cjwp8g" }, known, "no graph");
        Assert(problems.SequenceEqual(new[] { "Construction segment cjwp8g no longer exists. On the Connections tab, select the segments of the bridge's track and click Set." }),
            "A missing segment is one problem naming it and the repair: " + String.Join(" | ", problems));
        var empty = CommandBand.ConstructionSegmentProblems(new[] { " " }, known, "no graph");
        Assert(empty.SequenceEqual(new[] { "A construction segment has no ID. On the Connections tab, select the segments of the bridge's track and click Set." }),
            "An empty segment ID is a problem too: " + String.Join(" | ", empty));
        Assert(CommandBand.ConstructionSegmentProblems(new[] { "seg-new-1", "seg-new-2" }, known, "no graph").Count == 0, "After the repair the construction names existing segments and has no problem");
        var unknown = CommandBand.ConstructionSegmentProblems(new[] { "cjwp8g" }, null, "no graph");
        Assert(unknown.SequenceEqual(new[] { "Construction segment validity unknown: no graph" }) && unknown.All(BindingRules.IsNote), "Without the track graph there is one inspection note: " + String.Join(" | ", unknown));
        var unknownEmpty = CommandBand.ConstructionSegmentProblems(new[] { "cjwp8g", "" }, null, "no graph");
        Assert(unknownEmpty.SequenceEqual(new[] { empty[0], "Construction segment validity unknown: no graph" }), "Without the track graph an empty segment ID stays a problem: " + String.Join(" | ", unknownEmpty));
        Assert(CommandBand.ConstructionSegmentProblems(null, known, "no graph").Count == 0 && CommandBand.ConstructionSegmentProblems(null, null, "no graph").Count == 0, "No controller, no problem and no note");
        // A problem, not an inspection note: it gives the object the problem marker while the object is enabled, and a
        // disabled object keeps none.
        Assert(problems.All(line => !BindingRules.IsNote(line)) && !BindingRules.IsNote(empty[0]), "A missing construction segment is no inspection note");
        Assert(CommandBand.RowMarkers(BindingRules.Problems(true, problems, null).Count != 0, false, false) == "!", "An enabled construction is listed with the problem marker");
        Assert(CommandBand.RowMarkers(BindingRules.Problems(false, problems, null).Count != 0, false, false) == "", "A disabled construction has no problem marker: its controller does not run");
        // The same missing ID listed twice by one controller is one problem.
        var twice = CommandBand.ConstructionSegmentProblems(new[] { "cjwp8g", "cjwp8g", "", " " }, known, "no graph");
        Assert(twice.Count == 2 && twice.Distinct().Count() == 2, "Each problem line is distinct: " + String.Join(" | ", twice));
        // Where the patcher can repair the construction segments: the top scene object of an editable rail bridge group and the
        // editable Ela controller. A part, a refused top scene object or controller (no structure) and any other object show
        // the remark without a problem; this is the inventory's gate (NativeInventory passes the observation's structure).
        var ids = new[] { "cjwp8g" };
        Assert(CommandBand.RepairableConstructionProblems(SceneObjects.Structure, ids, known, "no graph").SequenceEqual(problems), "An editable top scene object reports the problem");
        Assert(CommandBand.RepairableConstructionProblems(ConstructionSceneryScene.Structure, ids, known, "no graph").SequenceEqual(problems), "The editable Ela controller reports the problem with the same wording");
        Assert(CommandBand.RepairableConstructionProblems(ChildObjects.Structure, ids, known, "no graph").Count == 0, "A part shows the remark without a problem");
        Assert(CommandBand.RepairableConstructionProblems(null, ids, known, "no graph").Count == 0, "A refused top scene object or controller, or one that shares its path, has no problem");
        Assert(CommandBand.RepairableConstructionProblems(SceneObjects.Structure, null, known, "no graph").Count == 0, "A group without a controller has no problem");
        Assert(BindingRules.Problems(false, problems, null).Count == 0, "A disabled construction keeps no problem");
        // The repair offered with the problem: the construction segment repair, and not the track binding task.
        Assert(CommandBand.RepairConstructionSegments == "Repair construction segments", "The repair button: " + CommandBand.RepairConstructionSegments);
        Assert(CommandBand.OffersConstructionRepair(problems) && !CommandBand.OffersBindingRepair(problems), "A construction problem offers the construction segment repair only");
        Assert(CommandBand.OffersConstructionRepair(empty) && !CommandBand.OffersBindingRepair(empty), "An empty construction segment offers the construction segment repair only");
        Assert(!CommandBand.OffersConstructionRepair(new[] { "Marker broken binding: missing segment S9" }) && CommandBand.OffersBindingRepair(new[] { "Marker broken binding: missing segment S9" }),
            "A track binding problem keeps the guided task");
        Assert(!CommandBand.OffersConstructionRepair(new[] { "Missing construction-scenery target" }) && !CommandBand.OffersBindingRepair(new string[0]), "Other problems offer neither");
        Assert(CommandBand.RepairConstructionSegmentsTip == "Open the Connections tab to select the segments of the bridge's track.", "The repair button's tooltip: " + CommandBand.RepairConstructionSegmentsTip);
    }

    // In scene patch editing: Undo and Redo of bridge edits with matching dirty state and Save availability, Remove
    // modification returning the effective state, Save at format version 1 with the patcher dependency, a stricter
    // constraint kept, and a reload that reproduces the moves without duplicated state.
    static void BridgeEditsInScenePatchEditing() {
        var f = new Bridges(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        const string Beam = Bryson + "/Steel/Plate Girder E (4)";
        Assert(session.Edit(PartId(Beam), new Placement(BeamTarget, Upright), null).Applied && session.Edit(GroupId(Bryson), new Placement(BridgeTarget, Upright), null).Applied
            && session.Edit(GroupId(Girder), new Placement(PlateTarget, Upright), false).Applied, "A girder move, a construction move and a plate girder bridge move with disable apply");
        Persistence(true, all, "After three edits");
        mapEditor.Undo(); Assert(Near(f.At(f.Plate), PlateAuthored) && f.Plate.Active, "Undo returns the plate girder bridge and enables it: " + f.Where());
        mapEditor.Undo(); Assert(Near(f.At(f.Bridge1), BridgeAuthored) && Near(f.Culls(), BridgeAuthored) && f.CollidersFollow() && Near(f.At(f.Beam), BeamTarget), "Undo returns the construction with its culling and colliders; the held girder stays: " + f.Where());
        mapEditor.Undo(); Assert(Near(f.At(f.Beam), BeamAuthored), "Undo returns the girder: " + f.Where()); Persistence(false, new string[0], "After Undo back to the unedited bridges");
        mapEditor.Redo(); mapEditor.Redo(); mapEditor.Redo();
        Assert(Near(f.At(f.Beam), BeamTarget) && Near(f.At(f.Bridge1), BridgeTarget) && Near(f.At(f.Plate), PlateTarget) && !f.Plate.Active && Near(f.Culls(), BridgeTarget) && f.CollidersFollow(), "Redo reapplies every edit: " + f.Where());
        Persistence(true, all, "After Redo");
        var removed = session.Remove(PartId(Beam));
        Assert(removed.Applied && Near(f.At(f.Beam), new[] { 3540.0, 539, 4429 }), "Remove modification returns the girder to its place in the moved construction: " + removed.Error + " " + f.Where());
        mapEditor.Undo(); Assert(Near(f.At(f.Beam), BeamTarget), "Undo of the removal holds the girder again");
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=" + SceneObjects.RequiredVersion) && (int)JObject.Parse(store.Patch)["formatVersion"] == 1,
            "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        var fresh = new Bridges();
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Count == 3 && reload.Outcomes.Values.All(o => o.Applied) && Near(fresh.At(fresh.Beam), BeamTarget) && Near(fresh.At(fresh.Bridge1), BridgeTarget) && !fresh.Plate.Active
            && Near(fresh.Culls(), BridgeTarget) && fresh.Bridge1.Parts.Count == 3 && fresh.Bridge1.Cullers.Count == 1 && fresh.Bridge1.Colliders.Count == 2, "A reloaded world reproduces the moves without duplicated state: " + fresh.Where());
        // A refused bridge edit leaves the scene patch file and the persistence state unchanged.
        var before = store.Patch;
        Assert(!session.Edit(GroupId(Bryson), new Placement(new[] { Double.NaN, 0, 0 }, Upright), null).Applied && store.Patch == before && !mapEditor.IsDirty, "A refused bridge edit changes neither the file nor the dirty state");
        var refused = new Bridges(); var refusedStore = new Store(); var refusedEditor = new NativeMapEditor(); var refusedSession = new PatchEditingSession(refused.Engine, refusedEditor, refusedStore); refusedEditor.Session = refusedSession;
        refused.World.Refused = "The group's scene is not loaded";
        Assert(!refusedSession.Edit(GroupId(Bryson), new Placement(BridgeTarget, Upright), null).Applied && refusedStore.Patch == null && !refusedEditor.IsDirty && refused.World.Mutations == 0,
            "A preflight refusal writes no scene patch file and leaves nothing outstanding");
        // Undo of a disable of a switched part gives the steel back to the controller's current choice.
        var held = new Bridges(); var heldEditor = new NativeMapEditor(); var heldSession = new PatchEditingSession(held.Engine, heldEditor, new Store()); heldEditor.Session = heldSession;
        Assert(heldSession.Edit(PartId(Bryson + "/Steel"), null, false).Applied && !held.Steel.Active, "Disable of the steel applies");
        held.World.Controller(held.Steel, true);
        Assert(!held.Steel.Active, "The disabled steel holds");
        heldEditor.Undo(); Assert(held.Steel.Active && !held.World.Diverted(held.Steel), "Undo gives the steel back to the controller's choice");
        heldEditor.Redo(); Assert(!held.Steel.Active, "Redo holds the steel off again");
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = new Bridges(); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(other.Engine, strictEditor, strict); strictEditor.Session = kept;
        Assert(kept.Edit(GroupId(Bryson), new Placement(BridgeTarget, Upright), null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=" + SceneObjects.RequiredVersion),
            "A stricter existing constraint is retained: " + strict.Info);
    }

    // Bryson Bridge 1 and a plate girder bridge as the group and part capabilities see them: Bryson Bridge 1 with its
    // renderer culler on the root, its abutments and steel, which its construction controller switches, a girder below
    // the steel and a collider on each of abutments and steel; a plate girder bridge without parts, controller or collider.
    const string SceneName = "BushnellWhittier-MapManager", Girder = Girders + "Plate Girder Bridge (1)";
    internal sealed class Bridges {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeGroup Bridge1 = new FakeGroup { Position = new[] { 2391.0, 532, 4315 }, Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakeGroup Plate = new FakeGroup { Position = new[] { 4098.0, 529, 5667 }, Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakePart Abutments, Steel, Beam;
        // The Almond Bridge: the top scene object carries a simple culler of 200 m and the construction controller, which
        // switches its abutments and steel; a bolt stands below the abutments and a truss below the steel.
        public readonly FakeGroup AlmondRoot = new FakeGroup { Position = new[] { -7206.0, 529, 608 }, Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = true, RailBridge = true };
        public readonly FakePart AlmondAbutments, Bolt, AlmondSteel, Truss;
        public readonly FakeCuller AlmondCuller = new FakeCuller { Id = "SimpleCuller#21", OnRoot = true, Radius = 200, Authored = 200 };
        public readonly SceneCapabilities Scene;
        public readonly FakeCuller BridgeCuller = new FakeCuller { Id = "RendererCuller#3", OnRoot = true, Simple = false, Radius = 120, Authored = 120 };
        public readonly PatchEngine Engine;
        public Bridges() {
            Abutments = Bridge1.Add(new FakePart { Id = "Abutments#1", Offset = new[] { 0.0, 0, 0 }, Constructed = true });
            Steel = Bridge1.Add(new FakePart { Id = "Steel#2", Offset = new[] { 0.0, 0, 0 }, Constructed = true });
            Beam = Bridge1.Add(new FakePart { Id = "Plate Girder E (4)#3", Offset = new[] { 40.0, -1, 29 }, Parent = Steel });
            Bridge1.Cullers.Add(BridgeCuller);
            Bridge1.Colliders.Add(new FakeGroupCollider { Id = "Abutments|BoxCollider#11", Shape = "box size=4,6,4" });
            Bridge1.Colliders.Add(new FakeGroupCollider { Id = "Steel|BoxCollider#12", Shape = "box size=3,1,90" });
            var verdicts = SceneObjectRules.Classify(RailBridgeTests.World());
            World.GroupVerdicts = verdicts.ToDictionary(v => v.Path, v => v);
            World.Verdicts = SceneObjectRules.Parts(verdicts).GroupBy(v => v.Path).ToDictionary(g => g.Key, g => g.First());
            World.Objects[Bryson] = new List<object> { Bridge1 }; World.Objects[Girder] = new List<object> { Plate };
            World.PartsAt[Bryson + "/Abutments"] = new List<object> { Abutments }; World.PartsAt[Bryson + "/Steel"] = new List<object> { Steel };
            World.PartsAt[Bryson + "/Steel/Plate Girder E (4)"] = new List<object> { Beam };
            Bridge1.RailBridge = Plate.RailBridge = true;
            Bridge1.Scenery = "trestle 7 generated=True beams=12";
            AlmondAbutments = AlmondRoot.Add(new FakePart { Id = "Abutments#31", Offset = new[] { 0.0, 0, 0 }, Constructed = true });
            Bolt = AlmondRoot.Add(new FakePart { Id = "Bridge Abutment#32", Offset = new[] { -50.0, 3, -110 }, Parent = AlmondAbutments });
            AlmondSteel = AlmondRoot.Add(new FakePart { Id = "Steel#33", Offset = new[] { 0.0, 0, 0 }, Constructed = true });
            Truss = AlmondRoot.Add(new FakePart { Id = "Truss#34", Offset = new[] { -28.0, -1, -61 }, Parent = AlmondSteel });
            AlmondRoot.Cullers.Add(AlmondCuller);
            World.Objects[Almond] = new List<object> { AlmondRoot };
            World.PartsAt[Almond + "/Abutments"] = new List<object> { AlmondAbutments }; World.PartsAt[Almond + "/Abutments/Bridge Abutment"] = new List<object> { Bolt };
            World.PartsAt[Almond + "/Steel"] = new List<object> { AlmondSteel }; World.PartsAt[Almond + "/Steel/Truss"] = new List<object> { Truss };
            World.SetActive(Bridge1, true); World.SetActive(Plate, true); World.SetActive(AlmondRoot, true); World.Mutations = 0;
            var holds = new PartHolds();
            var scene = Scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new SceneObjects(World, holds: holds)); scene.Add(new ChildObjects(World, holds));
            Engine = new PatchEngine(scene);
        }
        public double[] At(FakePart part) => World.ObservePart(part).Part.Placement.Position.ToArray();
        public double[] At(FakeGroup group) => World.Observe(group).Root.Placement.Position.ToArray();
        public double[] Culls() => BridgeCuller.Registered == null ? null : new[] { BridgeCuller.Registered[0] + World.Origin, BridgeCuller.Registered[1], BridgeCuller.Registered[2] };
        public bool CollidersFollow() => World.Observe(Bridge1).Colliders.All(c => c.BoundsFollow && c.Enabled);
        public string Where() => "bridge=" + N(At(Bridge1)) + "; steel=" + N(At(Steel)) + "; beam=" + N(At(Beam)) + "; plate=" + N(At(Plate)) + "; culls=" + (Culls() == null ? "none" : N(Culls())) + "; colliders follow=" + CollidersFollow();
    }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) => a != null && b != null && a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= 0.001);
    static string Mod(string id, string path, string structure, double[] position = null, bool? active = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", position.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[0,0,0,1]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string GroupMod(string id, string path, double[] position = null, bool? active = null) => Mod(id, path, SceneObjects.Structure, position, active);
    static string PartMod(string id, string path, double[] position = null, bool? active = null) => Mod(id, path, ChildObjects.Structure, position, active);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static TargetIdentity GroupId(string path) => new TargetIdentity(SceneName, path, SceneObjects.ComponentType, SceneObjects.Structure);
    static TargetIdentity PartId(string path) => new TargetIdentity(SceneName, path, ChildObjects.ComponentType, ChildObjects.Structure);
    static readonly double[] Upright = { 0, 0, 0, 1 }, BridgeTarget = { 3500, 540, 4400 }, PlateTarget = { 5200, 529, 5700 }, BeamTarget = { 3560, 539, 4440 };
    static readonly double[] BridgeAuthored = { 3391, 532, 4315 }, PlateAuthored = { 5098, 529, 5667 }, BeamAuthored = { 3431, 531, 4344 };

    // Bryson Bridge 1 moves with its parts, its culling sphere and its colliders, whose shape and enabled state stay
    // unchanged and whose physics bounds follow; a plate girder bridge moves on its own; a part moves within its
    // construction; a repeated application and a load at another origin reach the same placements.
    static void ConstructionMovesWithItsColliders() {
        var f = new Bridges();
        var observed = f.World.Observe(f.Bridge1).Colliders;
        Assert(observed.Select(c => c.Id).SequenceEqual(new[] { "Abutments|BoxCollider#11", "Steel|BoxCollider#12" }), "The group observation lists the bridge's colliders: " + String.Join(", ", observed.Select(c => c.Id)));
        var doc = Doc(GroupMod("move-bryson", Bryson, BridgeTarget), GroupMod("move-plate", Girder, PlateTarget), PartMod("move-beam", Bryson + "/Steel/Plate Girder E (4)", BeamTarget));
        var result = f.Engine.Apply(doc);
        Assert(result.Outcomes.Values.All(o => o.Applied), "The rail bridge modifications apply: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        Assert(Near(f.At(f.Bridge1), BridgeTarget) && Near(f.At(f.Steel), BridgeTarget) && Near(f.At(f.Beam), BeamTarget) && Near(f.At(f.Plate), PlateTarget),
            "The construction, its parts and the plate girder bridge stand at their targets: " + f.Where());
        Assert(Near(f.Culls(), BridgeTarget) && f.CollidersFollow(), "Culling and colliders follow the moved construction: " + f.Where());
        var first = f.Where();
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && f.Where() == first, "A repeated application accumulates no offset: " + f.Where());
        var shifted = new Bridges(); shifted.World.Origin = 1010;
        Assert(shifted.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && N(shifted.At(shifted.Bridge1)) == N(BridgeTarget) && N(shifted.At(shifted.Beam)) == N(BeamTarget),
            "A load at another origin reaches the same map placements: " + shifted.Where());
        var hidden = new Bridges();
        Assert(hidden.Engine.Apply(Doc(GroupMod("hide-bryson", Bryson, active: false))).Outcomes.Values.All(o => o.Applied) && !hidden.Bridge1.Active && hidden.Culls() == null,
            "Disabling the construction root applies, and the disabled bridge holds no culling sphere: " + hidden.Where());
    }

    // The construction controller switches the abutments and the steel by the state of its track segments. An explicit
    // disable holds such an object off against the controller, and Enable returns it to the controller's current
    // choice. Without a disable the controller switches the object itself, as without the patcher. While the game
    // switches an object during an edit, the edit still verifies.
    static void ExplicitDisableHoldsAgainstTheController() {
        var f = new Bridges();
        var outcome = f.Engine.Apply(Doc(PartMod("hide-steel", Bryson + "/Steel", active: false))).Outcomes["hide-steel"];
        Assert(outcome.Applied && !f.Steel.Active && f.World.Constructions.Held(f.Steel), "Disabling a switched part applies and holds it off: " + outcome.Error);
        f.World.Controller(f.Steel, true);
        Assert(!f.Steel.Active, "The controller showing the steel does not switch on the disabled steel");
        Assert(f.Engine.Apply(Doc(PartMod("hide-steel", Bryson + "/Steel", active: false))).Outcomes["hide-steel"].Applied && !f.Steel.Active, "A repeated application keeps the hold");
        outcome = f.Engine.Apply(Doc(PartMod("show-steel", Bryson + "/Steel", active: true))).Outcomes["show-steel"];
        Assert(outcome.Applied && f.Steel.Active && !f.World.Diverted(f.Steel), "Enable returns the steel to the controller's current choice and back to the controller: " + outcome.Error);
        f.World.Controller(f.Steel, false);
        Assert(!f.Steel.Active, "The controller switches the enabled steel again");
        // Enabled while the controller hides it: the object takes the controller's choice, and the edit verifies.
        var hidden = new Bridges();
        hidden.Engine.Apply(Doc(PartMod("hide-steel", Bryson + "/Steel", active: false)));
        hidden.World.Controller(hidden.Steel, false);
        outcome = hidden.Engine.Apply(Doc(PartMod("show-steel", Bryson + "/Steel", active: true))).Outcomes["show-steel"];
        Assert(outcome.Applied && !hidden.Steel.Active && hidden.World.ObservePart(hidden.Steel).Part.Active == true, "Enable while the controller hides the steel leaves it hidden and reads as enabled: " + outcome.Error);
        // Placement stays editable for a switched part.
        outcome = f.Engine.Apply(Doc(PartMod("move-steel", Bryson + "/Steel", BeamTarget))).Outcomes["move-steel"];
        Assert(outcome.Applied && Near(f.At(f.Steel), BeamTarget), "A switched part moves: " + outcome.Error);
        // A top scene object that a controller switches holds the same way.
        var group = new Bridges(); group.Bridge1.Constructed = true;
        outcome = group.Engine.Apply(Doc(GroupMod("hide-bryson", Bryson, active: false))).Outcomes["hide-bryson"];
        Assert(outcome.Applied && !group.Bridge1.Active, "Disabling a switched top scene object applies: " + outcome.Error);
        group.World.Controller(group.Bridge1, true);
        Assert(!group.Bridge1.Active && group.World.Observe(group.Bridge1).Root.Active == false, "The controller does not switch on the disabled root, which reads as disabled");
        var flips = new Bridges(); flips.World.ConstructionFlips = true;
        outcome = flips.Engine.Apply(Doc(GroupMod("move-bryson", Bryson, BridgeTarget))).Outcomes["move-bryson"];
        Assert(outcome.Applied && !flips.Steel.Active, "The construction switching a part during a move does not fail the move: " + outcome.Error);
        // A failure after mutation restores the hold as it was captured.
        var failed = new Bridges(); failed.World.ThrowAfterPartPlacement = true;
        outcome = failed.Engine.Apply(Doc(PartMod("hide-move-steel", Bryson + "/Steel", BeamTarget, false))).Outcomes["hide-move-steel"];
        failed.World.Controller(failed.Steel, true);
        Assert(!outcome.Applied && failed.Steel.Active && !failed.World.Constructions.Held(failed.Steel), "A failed disable is restored and the controller switches the steel again: " + outcome.Error);
    }

    // The Almond Bridge's steel moved outside the 200 m sphere of the top scene object's culler gets a culling sphere of its own.
    // Own culling then shows the controller's current choice while that sphere is in view: the controller hiding the
    // steel after the plan was made hides it at every visibility change, and the controller showing it again shows it
    // while its sphere is in view. The abutments, which stand above a moved bolt, follow the controller the same way.
    static void OwnCullingFollowsTheController() {
        var f = new Bridges();
        var far = new[] { -6206.0 + 600, 529, 608 + 400 };
        var bolt = new[] { -6206.0 - 450, 532, 608 - 500 };
        var result = f.Engine.Apply(Doc(PartMod("move-steel", Almond + "/Steel", far), PartMod("move-bolt", Almond + "/Abutments/Bridge Abutment", bolt)));
        Assert(result.Outcomes.Values.All(o => o.Applied), "The steel and the bolt move outside the top scene object's sphere: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        FakeRegistration Own(FakePart part) => f.World.Native.Registered.SingleOrDefault(r => r.Owner == part);
        Assert(Own(f.AlmondSteel) != null && Own(f.Bolt) != null && f.World.Culling.Desired(f.AlmondSteel) != null && f.World.Culling.Desired(f.AlmondAbutments) != null,
            "The moved steel and bolt have spheres of their own, and own culling switches the steel and the abutments above the bolt");
        Assert(f.World.Diverted(f.AlmondSteel) && f.World.Diverted(f.AlmondAbutments), "The controller's writes for the objects own culling switches reach own culling");
        f.World.Controller(f.AlmondSteel, false);
        Assert(!f.AlmondSteel.Active, "The controller hides the steel after the plan was made");
        f.World.Show(Own(f.AlmondSteel), false); f.World.Show(Own(f.AlmondSteel), true);
        Assert(!f.AlmondSteel.Active, "The steel stays hidden when its own sphere comes into view again: own culling does not freeze the construction state");
        f.World.Controller(f.AlmondSteel, true);
        Assert(f.AlmondSteel.Active, "The controller shows the steel while its sphere is in view");
        f.World.Show(Own(f.AlmondSteel), false);
        Assert(!f.AlmondSteel.Active, "Out of view, own culling hides the steel");
        f.World.Show(Own(f.AlmondSteel), true);
        Assert(f.AlmondSteel.Active, "In view again, the steel shows as the controller chose");
        f.World.Controller(f.AlmondAbutments, false);
        f.World.Show(Own(f.Bolt), false); f.World.Show(Own(f.Bolt), true);
        Assert(!f.AlmondAbutments.Active, "The abutments above the moved bolt stay as the controller chose");
        // An explicit disable holds against the controller also under own culling, and Enable gives the choice back.
        Assert(f.Engine.Apply(Doc(PartMod("hide-steel", Almond + "/Steel", far, false))).Outcomes["hide-steel"].Applied && !f.AlmondSteel.Active, "The culled steel disables");
        Assert(Own(f.AlmondSteel) == null, "A disabled part holds no culling sphere of its own");
        f.World.Controller(f.AlmondSteel, true);
        Assert(!f.AlmondSteel.Active, "The disabled steel stays off although the controller shows it");
        Assert(f.Engine.Apply(Doc(PartMod("show-steel", Almond + "/Steel", far, true))).Outcomes["show-steel"].Applied && f.AlmondSteel.Active, "Enable shows the steel as the controller chose");
        // Moved back inside the top scene object's sphere, the steel goes back to its controller.
        Assert(f.Engine.Apply(Doc(PartMod("back-steel", Almond + "/Steel", new[] { -6206.0, 529, 608 }))).Outcomes["back-steel"].Applied, "The steel moves back");
        Assert(f.World.Culling.Desired(f.AlmondSteel) == null && !f.World.Diverted(f.AlmondSteel), "The steel back in the top scene object's sphere has no own culling and goes back to its controller");
        f.World.Controller(f.AlmondSteel, false);
        Assert(!f.AlmondSteel.Active, "The controller switches the steel directly again");
    }

    // Own culling takes over the moved steel while its own sphere is out of view, as at a world load far from the
    // Almond Bridge: the steel is off then, although the controller shows it. The controller hiding the steel later
    // still reaches own culling, so the steel stays hidden when its sphere comes into view.
    static void OwnCullingTakesOverAnObjectOutOfView() {
        var f = new Bridges();
        var far = new[] { -6206.0 + 600, 529, 608 + 400 };
        f.World.HideSpheresOnApply = true;
        Assert(f.Engine.Apply(Doc(PartMod("move-steel", Almond + "/Steel", far))).Outcomes["move-steel"].Applied, "The steel moves outside the top scene object's sphere");
        var own = f.World.Native.Registered.Single(r => r.Owner == f.AlmondSteel);
        Assert(!f.AlmondSteel.Active && f.World.Diverted(f.AlmondSteel), "Out of view, own culling holds the steel off and takes the controller's writes");
        f.World.Controller(f.AlmondSteel, false);
        f.World.Show(own, true);
        Assert(!f.AlmondSteel.Active, "The controller's hide while the steel was out of view is not lost: the steel stays hidden in view");
        f.World.Controller(f.AlmondSteel, true);
        Assert(f.AlmondSteel.Active, "The controller showing the steel again shows it in view");
    }

    // A rail bridge does not follow its track: track-following placement of a construction or of a part is refused
    // before any mutation, and the Follow placement mode is unavailable with the reason.
    static void RailBridgesDoNotFollowTrack() {
        var f = new Bridges(); f.Scene.Tracks = new FixedTrack();
        string Follow(string id, string path, string structure) =>
            "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}," +
            "\"placement\":{\"mode\":\"trackFollowing\",\"track\":{\"segment\":\"S1\",\"end\":\"a\",\"distance\":10},\"offset\":[0,0,0],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var result = f.Engine.Apply(Doc(Follow("follow-bryson", Bryson, SceneObjects.Structure), Follow("follow-steel", Bryson + "/Steel", ChildObjects.Structure)));
        Assert(result.Outcomes.Values.All(o => !o.Applied && o.Failure == FailureKind.Refused && o.Error.Contains("fixed placement")) && f.World.Mutations == 0 && Near(f.At(f.Bridge1), BridgeAuthored),
            "Track-following placement of a rail bridge is refused before mutation: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Failure + " " + o.Error)));
        var railEdits = SupportedEdits.Of(EditEntries.Of(SceneObjectRules.Family, SceneObjects.Structure, CommandBand.GroupOperations, BridgeKind.Rail));
        Assert(SupportedEdits.ModeBlock(railEdits, CommandBand.FollowMode, true) == CommandBand.RailBridgeNoFollow && CommandBand.RailBridgeNoFollow.Contains("fixed placement")
            && SupportedEdits.ModeBlock(railEdits, CommandBand.NumericMode, true) == null, "Follow is unavailable for a rail bridge with the reason; Numeric stays");
    }
    sealed class FixedTrack : ITrackPoseScene { public TrackPose TrackPose(TrackAnchor anchor) => new TrackPose(new[] { 3000.0, 530, 4300 }, new[] { 0.0, 0, 0, 1 }); }

    // The separate track-owned scenery beside a rail bridge, such as its track's generated trestles, stays unchanged:
    // an edit that changes it fails and is restored.
    static void SeparateTrackSceneryStays() {
        var f = new Bridges(); f.World.DisturbScenery = true;
        var outcome = f.Engine.Apply(Doc(GroupMod("move-bryson", Bryson, BridgeTarget))).Outcomes["move-bryson"];
        Assert(!outcome.Applied && outcome.Error.Contains("track-owned scenery"), "Changed separate track scenery fails the move: " + outcome.Error);
    }

    // A preflight refusal leaves the bridge untouched; a collider that does not follow fails the move, which restores the
    // captured placement, activity, culling and collider state; incomplete cleanup is reported and blocks the retry.
    // A world load whose move of Bryson Bridge 1 fails after mutation and is restored, with a move of the plate girder
    // bridge that depends on it: in scene patch editing the first shows as failed and restored, the second as not applied
    // because its dependency failed. A second application in the same load reports AlreadyFailed and keeps the first
    // failure, so the object keeps showing the kind of that first failure.
    const string Key = "author/pack";
    static string Depending(string item, string on) => item.Replace("\"dependsOn\":[]", "\"dependsOn\":[\"" + on + "\"]");
    static void LoadFailuresShowAsNotApplied() {
        var f = new Bridges(); f.World.DetachCollider = true;
        var text = Doc(GroupMod("move-bryson", Bryson, BridgeTarget), Depending(GroupMod("move-plate", Girder, PlateTarget), "move-bryson"));
        var load = f.Engine.Apply(text, Key);
        Assert(load.Outcomes["move-bryson"].Failure == FailureKind.Restored && load.Outcomes["move-plate"].Failure == FailureKind.DependencyFailed,
            "The load fails the move after mutation and the dependent move by its dependency: " + load.Outcomes["move-bryson"].Failure + " " + load.Outcomes["move-plate"].Failure);
        var again = f.Engine.Apply(text, Key);
        Assert(again.Outcomes["move-bryson"].Failure == FailureKind.AlreadyFailed, "A second application in the same load reports AlreadyFailed: " + again.Outcomes["move-bryson"].Failure);
        var session = new PatchEditingSession(f.Engine, new NativeMapEditor(), new Store { Patch = text }, scenePatchFileKey: Key);
        var bryson = session.NotApplied(GroupId(Bryson)); var plate = session.NotApplied(GroupId(Girder));
        Assert(bryson?.Kind == FailureKind.Restored && session.Refusal(GroupId(Bryson)) == null, "The failed and restored move shows as not applied, not as refused: " + bryson?.Kind);
        Assert(plate?.Kind == FailureKind.DependencyFailed, "The dependent move shows as not applied because its dependency failed: " + plate?.Kind);
        Assert(session.Changes(GroupId(Bryson)).Single().StartsWith("Failed and restored. The change failed. The object is back as it was.", StringComparison.Ordinal)
            && session.Changes(GroupId(Girder)).Single().StartsWith("Not applied because a dependency failed. ", StringComparison.Ordinal),
            "The Changes tab names each failure: " + String.Join(" | ", session.Changes(GroupId(Bryson)).Concat(session.Changes(GroupId(Girder)))));
        Assert(ReferenceEquals(CommandBand.Badges(true, false, CommandBand.ShownFailure(true, bryson.Kind)).Single(), CommandBand.NotApplied)
            && CommandBand.RowMarkers(false, true, false, CommandBand.ShownFailure(true, plate.Kind)) == "\u00d7", "Both show the not applied badge and marker");
    }

    // Read again of a corrected scene patch file in a session that started protected: the failures of its application show.
    static void ReadAgainShowsItsFailures() {
        var f = new Bridges(); f.World.DetachCollider = true;
        var store = new Store { Patch = "{ not a scene patch file" };
        var session = new PatchEditingSession(f.Engine, new NativeMapEditor(), store, scenePatchFileKey: Key);
        Assert(session.Blocked != null, "The session starts protected");
        store.Patch = Doc(GroupMod("move-bryson", Bryson, BridgeTarget));
        var reread = session.Reread(true);
        Assert(reread.Applied && session.LastReread.Outcomes["move-bryson"].Failure == FailureKind.Restored, "Read again applies the file and the move fails: " + reread.Error);
        Assert(session.NotApplied(GroupId(Bryson))?.Kind == FailureKind.Restored, "The failure of Read again's application shows: " + session.NotApplied(GroupId(Bryson))?.Kind);
    }

    // Import applies a changed entry: its earlier failure no longer shows. A changed entry whose target does not resolve
    // is not applied, so it keeps its load refusal: here it changes away and back, and shows refused again.
    static void ImportClearsOnlyWhatItApplied() {
        const string Gone = Large + "Gone Bridge";
        var f = new Bridges(); f.World.DetachCollider = true;
        var text = Doc(GroupMod("move-bryson", Bryson, BridgeTarget), GroupMod("move-gone", Gone, BridgeTarget));
        f.Engine.Apply(text, Key);
        var store = new Store { Patch = text }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store, scenePatchFileKey: Key); mapEditor.Session = session;
        Assert(session.NotApplied(GroupId(Bryson))?.Kind == FailureKind.Restored && session.NotApplied(GroupId(Gone))?.Kind == FailureKind.Refused, "Both load failures show");
        f.World.DetachCollider = false;
        void Import(string patch, string step) {
            store.Patch = patch; session.CheckFileVersion();
            Assert(session.OpenImport().Applied, step + ": Import opens");
            var applied = session.ApplyImport();
            Assert(applied.Applied, step + ": Import applies: " + applied.Error);
        }
        var other = new[] { 3510.0, 540, 4400 };
        Import(Doc(GroupMod("move-bryson", Bryson, other), GroupMod("move-gone", Gone, other)), "First import");
        Assert(session.NotApplied(GroupId(Bryson)) == null && Near(f.At(f.Bridge1), other), "The applied entry no longer shows its failure: " + f.Where());
        Assert(session.LastImport.NotApplied.Any(line => line.StartsWith("move-gone", StringComparison.Ordinal)), "The unresolved entry is kept but not applied");
        Import(Doc(GroupMod("move-bryson", Bryson, other), GroupMod("move-gone", Gone, BridgeTarget)), "Second import");
        Assert(session.NotApplied(GroupId(Gone))?.Kind == FailureKind.Refused, "Back at its loaded entry, the unresolved modification shows its load refusal again");
    }

    static void FailuresRestoreColliderState() {
        var refused = new Bridges(); refused.World.Refused = "The group's scene is not loaded";
        var outcome = refused.Engine.Apply(Doc(GroupMod("move-bryson", Bryson, BridgeTarget))).Outcomes["move-bryson"];
        Assert(!outcome.Applied && refused.World.Mutations == 0 && Near(refused.At(refused.Bridge1), BridgeAuthored) && Near(refused.Culls(), BridgeAuthored), "A preflight refusal leaves the bridge untouched: " + outcome.Error);

        var failed = new Bridges(); failed.World.DetachCollider = true;
        outcome = failed.Engine.Apply(Doc(GroupMod("move-bryson", Bryson, BridgeTarget, false))).Outcomes["move-bryson"];
        Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal) && outcome.Error.Contains("collider")
            && Near(failed.At(failed.Bridge1), BridgeAuthored) && failed.Bridge1.Active && Near(failed.Culls(), BridgeAuthored) && failed.CollidersFollow(),
            "A collider that does not follow fails the move, which is restored: " + outcome.Error + " " + failed.Where());
        var shape = new Bridges(); shape.World.ChangeColliderShape = true;
        outcome = shape.Engine.Apply(Doc(PartMod("move-beam", Bryson + "/Steel/Plate Girder E (4)", BeamTarget))).Outcomes["move-beam"];
        Assert(!outcome.Applied && outcome.Error.Contains("collider") && Near(shape.At(shape.Beam), BeamAuthored), "A part edit that changes a collider's shape fails and is restored: " + outcome.Error + " " + shape.Where());
        var thrown = new Bridges(); thrown.World.ThrowAfterPlacement = true;
        outcome = thrown.Engine.Apply(Doc(GroupMod("move-bryson", Bryson, BridgeTarget, false))).Outcomes["move-bryson"];
        Assert(!outcome.Applied && Near(thrown.At(thrown.Bridge1), BridgeAuthored) && thrown.Bridge1.Active && thrown.CollidersFollow(), "A failure after mutation restores the bridge: " + outcome.Error);

        var stuck = new Bridges(); stuck.World.DetachCollider = stuck.World.KeepColliderDetached = true;
        outcome = stuck.Engine.Apply(Doc(GroupMod("move-bryson", Bryson, BridgeTarget))).Outcomes["move-bryson"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        stuck.World.KeepColliderDetached = false; var mutations = stuck.World.Mutations;
        var retry = stuck.Engine.Apply(Doc(GroupMod("again", Bryson, BridgeTarget))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && stuck.World.Mutations == mutations, "The retry on the affected bridge is blocked: " + retry.Error);
    }

    // Each rail bridge root outside Ela is listed once as a group of the family under Bridge / Rail bridges:
    // Bryson Bridge 1 and the Almond Bridge, whose top scene object carries its controller and culler, each with
    // its parts, and every plate girder bridge on its own. Road bridges stand under Road bridges.
    static void InventoryListsEachConstructionOnceWithItsParts() {
        var verdicts = SceneObjectRules.Classify(World());
        var roots = new[] { Bryson, Almond }.Concat(GirderNames.Select(name => Girders + name)).ToArray();
        foreach (var path in roots) {
            var found = verdicts.Where(v => v.Path == path).ToArray();
            Assert(found.Length == 1 && found[0].Disposition == GroupDisposition.Group && found[0].Category == SceneObjectCategories.Bridge && found[0].Subcategory == SceneObjectCategories.RailBridges,
                path + " is listed once as a group under Bridge / Rail bridges: " + String.Join(", ", found.Select(v => v.Disposition + " " + v.Category + "/" + v.Subcategory + " " + v.Reason)));
        }
        Assert(At(verdicts, Almond + "/Abutments") == null && At(verdicts, Almond + "/Steel") == null, "The Almond Bridge's abutments and steel are no top scene objects of their own");
        var parts = SceneObjectRules.Parts(verdicts).Where(p => p.Disposition == PartDisposition.Part).ToArray();
        foreach (var path in new[] { Almond + "/Abutments", Almond + "/Steel", Almond + "/Steel/Truss", Bryson + "/Abutments", Bryson + "/Steel", Bryson + "/Steel/Plate Girder E (4)" })
            Assert(parts.Count(p => p.Path == path) == 1 && parts.Single(p => p.Path == path).GroupPath == (path.StartsWith(Almond, StringComparison.Ordinal) ? Almond : Bryson), path + " is one part of its construction");
        Assert(!parts.Any(p => p.Path == Bryson + "/Steel/Plate Girder W"), "Two girders that share a path are no parts");
        Assert(!parts.Any(p => p.GroupPath.StartsWith(Girders, StringComparison.Ordinal)), "A plate girder bridge is one object without parts");
        Assert(SceneObjectRules.PartName(Almond, Almond + "/Steel") == "Almond Bridge / Steel", "A part is named with its construction");
        // A group that holds a construction controller but is no supported rail bridge stays inspection only.
        var other = At(SceneObjectRules.Classify(new Node("World", new Node("Large Scenery", new Node("Sylva", new Node("Tipple Scenery", new Node("Beam").Solid()).With(Controller))))), Large + "Sylva/Tipple Scenery");
        Assert(other?.Disposition == GroupDisposition.InspectionOnly && other.Reason == SceneObjectRules.ConstructionOutsideBridge && other.ReasonIsSentence,
            "A controller outside a supported rail bridge keeps its group inspection only: " + other?.Disposition + " " + other?.Reason);
        // A rail bridge that holds another excluded kind stays inspection only under its category.
        var masked = At(SceneObjectRules.Classify(new Node("World", new Node("Large Scenery", new Node("Plate Girder Bridge", new Node("Plate Girder Bridge", new Node("Deck").Solid()).With("Character.SpawnPoint"))))), Girders + "Plate Girder Bridge");
        Assert(masked?.Disposition == GroupDisposition.InspectionOnly && masked.Subcategory == SceneObjectCategories.RailBridges, "An excluded kind keeps a rail bridge inspection only under Rail bridges");
        // Track-owned components belong to the native track: a rail bridge holding one is refused.
        Assert(SceneObjectRules.TrackOwnedRefusal(new[] { "UnityEngine.MeshRenderer", Controller }) == null, "A construction controller is no track-owned component");
        Assert(SceneObjectRules.TrackOwnedRefusal(new[] { "UnityEngine.MeshRenderer", "AutoTrestle.AutoTrestle" }) == "This rail bridge contains the track-owned component AutoTrestle.AutoTrestle, which belongs to the native track. The bridge can't be moved, enabled, or disabled."
            && SceneObjectRules.TrackOwnedRefusal(new[] { "Track.TrackSegment" }) != null, "A generated trestle or a track component refuses the bridge");
        var road = At(verdicts, Large + "Almond/Almond Road Bridge");
        Assert(road?.Disposition == GroupDisposition.Group && road.Subcategory == SceneObjectCategories.RoadBridges, "A road bridge is no rail bridge: it stands under Road bridges");
    }
}
