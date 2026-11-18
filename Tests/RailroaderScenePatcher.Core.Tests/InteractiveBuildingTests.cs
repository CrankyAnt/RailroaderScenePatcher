// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The buildings with game logic: Bryson Section House, the Tower (Bryson Tower Chance Clayton), Bryson Turntable and
// Roundhouse 3 join the Baked scenery group family with their parts. The controlled hierarchy is the recorded Railroader
// 0.6.3 scene (Records/InventoryLedger/level4_mb_rows.json, Records/InventoryCoverage/renderer_coverage.csv): each
// building holds a key-value object with its global id (RollingStock.Controls.GlobalKeyValueObject), a lantern carries
// its pickable toggle with its LanternController, and each roundhouse stall carries its pickable toggle on the object
// whose KeyValueBoolAnimator opens its doors.
static class InteractiveBuildingTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Interactive buildings: " + message); }
    internal const string Bryson = "World/Large Scenery/Bryson/", SectionHouse = Bryson + "Bryson Section House", Tower = Bryson + "Bryson Tower Chance Clayton",
        Turntable = Bryson + "Bryson Turntable", Roundhouse3 = "World/Large Scenery/Dillsboro Turntable/Roundhouse 3";
    const string Toggle = "RollingStock.Controls.KeyValuePickableToggle", Lantern = "Effects.LanternController", Animator = "RollingStock.Controls.KeyValueBoolAnimator",
        KeyObject = "KeyValue.Runtime.KeyValueObject", GlobalKeyObject = "RollingStock.Controls.GlobalKeyValueObject", Text = "TMPro.TextMeshPro";
    static Node Solid(string name) => new Node(name).Solid();
    static Node TrainmanLantern() => new Node("TrainmanLantern",
        new Node("Light").With("UnityEngine.Light", "UnityEngine.Rendering.Universal.UniversalAdditionalLightData").Solid(), Solid("Flame").Driven(),
        new Node("Lantern", Solid("Body"), Solid("Globe"), Solid("Handle"), Solid("Torus"), Solid("Torus.001"), Solid("Vertical Wires"))).With(Toggle, Lantern);
    static Node Stall(string name) => new Node(name, Solid("Roundhouse Modular A Floor"),
        new Node("Roundhouse Modular A Stall", Solid("Roundhouse Engine Door Frame"), Solid("Roundhouse Frame Middle"), Solid("Roundhouse Roof Top"), Solid("Roundhouse Wall Rear"),
            new Node("Roundhouse Stall Doors", new Node("Bone.002", new Node("Bone", Solid("Roundhouse Engine Door Left")), new Node("Bone.001", Solid("Roundhouse Engine Door Right")))))
            .With("UnityEngine.Animator", Animator, Toggle));
    static Node Side(string name) => new Node(name, Solid("Roundhouse Wall"), new Node("Roundhouse Side Doors", new Node("Bone", Solid("Roundhouse Side Door"))));
    static Node Spot(string name) => new Node(name).With("UnityEngine.Light", "Effects.NightLightController", "UnityEngine.Rendering.Universal.UniversalAdditionalLightData").Solid();
    // The part rule finds the stall's animator itself, as the game's adapter does. Flame is the lantern's filament
    // renderer, which its LanternController turns to the camera and scales every frame: the adapter finds it through
    // BakedSceneryRules.DrivenFields and marks it animated, as the fixture does.
    internal static Node World() => new Node("World", new Node("Large Scenery",
        new Node("Bryson",
            new Node("Bryson Section House", new Node("Common Standard Tool House", Solid("Cylinder"), Solid("Plane"), Solid("Plane.001")), TrainmanLantern()).With(KeyObject, GlobalKeyObject),
            new Node("Bryson Tower Chance Clayton", new Node("Culling Container",
                new Node("Elevated_Crossing_Tower", Solid("Cube"), Solid("Cube.001")),
                new Node("Sign-Station", Solid("Sign-Station"), new Node("Text").With(Text).Solid()),
                TrainmanLantern()).With(BakedSceneryRules.SimpleCuller, KeyObject, GlobalKeyObject)),
            new Node("Bryson Turntable", new Node("Roundhouse", new[] { Solid("Roundhouse Modular A Floor"), Solid("Roundhouse Modular A Between"), Solid("Roundhouse Modular A Between (1)"),
                Side("Roundhouse Modular A Side"), Side("Roundhouse Modular A Side (1)"), Stall("Stall"), Stall("Stall (1)"), Stall("Stall (2)"), Stall("Stall (3)"), Stall("Stall (4)"),
                Spot("Spot Light"), Spot("Spot Light (1)") }).With(BakedSceneryRules.RendererCuller, KeyObject, GlobalKeyObject))),
        new Node("Dillsboro Turntable",
            new Node("Roundhouse 3", Solid("Roundhouse Modular A Between"), Side("Roundhouse Modular A Side"), Stall("Stall"), Stall("Stall (1)"), Stall("Stall (2)"), Spot("Spot Light (1)"))
                .With(BakedSceneryRules.RendererCuller, KeyObject, GlobalKeyObject))));
    static GroupRootVerdict At(IReadOnlyList<GroupRootVerdict> verdicts, string path) => verdicts.SingleOrDefault(v => v.Path == path);

    public static void Run() {
        InventoryListsTheBuildingsWithTheirParts();
        ObjectTabNamesTheInteractiveParts();
        BindingsReadWhatTheGameReads();
        BuildingEditsApplyRepeatAndRestore();
        BuildingEditsInScenePatchEditing();
        Console.WriteLine("PASS: interactive buildings");
    }

    // The four buildings are listed groups under Railroad service building. The lantern is a part with its toggle; a
    // stall is a part, and the object that holds its toggle and animates its doors moves with the stall without a row
    // of its own; the Tower's culling container is no part, but its lantern and name text are.
    static void InventoryListsTheBuildingsWithTheirParts() {
        var verdicts = BakedSceneryRules.Classify(World());
        foreach (var path in new[] { SectionHouse, Tower, Turntable, Roundhouse3 }) {
            var group = At(verdicts, path);
            Assert(group?.Disposition == GroupDisposition.Group && group.Category == BakedSceneryCategories.RailroadServiceBuilding && group.Ticket == null,
                path + " is a listed group under Railroad service building: " + group?.Disposition + " " + group?.Reason + " " + group?.Category);
        }
        Assert(!BakedSceneryRules.Exclusions.Any(e => e.Component == Toggle || e.Ticket == "#91"), "No exclusion names a pickable toggle");
        var all = BakedSceneryRules.Parts(verdicts);
        var parts = all.ToDictionary(p => p.Path, p => p);
        bool Part(string path) => parts.TryGetValue(path, out var verdict) && verdict.Disposition == PartDisposition.Part;
        string Reason(string path) => parts.TryGetValue(path, out var verdict) ? verdict.Reason : "missing";
        foreach (var path in new[] { SectionHouse + "/TrainmanLantern", SectionHouse + "/TrainmanLantern/Light", SectionHouse + "/TrainmanLantern/Lantern/Globe", SectionHouse + "/Common Standard Tool House",
            Tower + "/Culling Container/TrainmanLantern", Tower + "/Culling Container/Sign-Station", Tower + "/Culling Container/Sign-Station/Text", Tower + "/Culling Container/Elevated_Crossing_Tower",
            Turntable + "/Roundhouse", Turntable + "/Roundhouse/Stall (2)", Turntable + "/Roundhouse/Stall (2)/Roundhouse Modular A Floor", Turntable + "/Roundhouse/Spot Light (1)",
            Turntable + "/Roundhouse/Roundhouse Modular A Side/Roundhouse Side Doors/Bone/Roundhouse Side Door", Roundhouse3 + "/Stall (1)", Roundhouse3 + "/Spot Light (1)" })
            Assert(Part(path), path + " is a part: " + Reason(path));
        foreach (var lantern in new[] { SectionHouse + "/TrainmanLantern", Tower + "/Culling Container/TrainmanLantern" })
            Assert(Reason(lantern + "/Flame") == BakedSceneryRules.AnimatedParts, "The lantern's filament, which the game turns to the camera, is no part: " + Reason(lantern + "/Flame"));
        var filament = BakedSceneryRules.DrivenFields.SingleOrDefault(d => d.Component == Lantern);
        Assert(filament != null && filament.Field == "filamentRenderer" && BakedSceneryRules.DrivenFields.Any(d => d.Component == "RollingStock.GaugeNeedle" && d.Field == "needle"),
            "The adapter reads the lantern's filament renderer and the gauge needle as transforms the game drives");
        Assert(Reason(Tower + "/Culling Container") == BakedSceneryRules.CullingContainers, "The Tower's culling container is no part: " + Reason(Tower + "/Culling Container"));
        foreach (var stall in new[] { Turntable + "/Roundhouse/Stall (2)", Roundhouse3 + "/Stall" }) {
            Assert(Reason(stall + "/Roundhouse Modular A Stall") == BakedSceneryRules.AnimatedParts, "The animated stall object is no part: " + Reason(stall + "/Roundhouse Modular A Stall"));
            Assert(Reason(stall + "/Roundhouse Modular A Stall/Roundhouse Stall Doors/Bone.002/Bone/Roundhouse Engine Door Left") == BakedSceneryRules.AnimatedParts, "A stall door moves with its stall");
        }
        var stalls = all.Count(p => p.Disposition == PartDisposition.Part && p.Path.EndsWith("/Roundhouse Modular A Floor", StringComparison.Ordinal) && p.Path.Contains("/Stall"));
        Assert(stalls == 8, "Each of the eight stalls lists its floor: " + stalls);
    }

    // The Object tab names each interactive part by what its toggle switches and says that the game keeps its state by
    // its key, not by where it stands; the object that holds the key-value object names the id the game saves the
    // states under; a group or part above toggles says that they keep their states; the Tower's name text says that the
    // scene keeps it with its object. A part of the group without any of these reads the decorative text.
    static void ObjectTabNamesTheInteractiveParts() {
        var lantern = CommandBand.ToggleBinding(CommandBand.ToggleKind(new[] { Toggle, Lantern }), "lantern.0");
        var door = CommandBand.ToggleBinding(CommandBand.ToggleKind(new[] { "UnityEngine.Animator", Animator, Toggle }), "doors");
        Assert(lantern == "Toggle=Lantern switch, key lantern.0" && door == "Toggle=Door toggle, key doors", "A toggle binding names its kind and key: " + lantern + " | " + door);
        Assert(CommandBand.ToggleKind(new[] { Toggle }) == "Toggle" && CommandBand.ToggleBinding("Toggle", null) == "Toggle=Toggle, no key", "A toggle that switches nothing named reads Toggle");
        var holder = CommandBand.KeyObjectBinding("bryson-section-house");
        var holderBelow = CommandBand.KeyObjectsBelowBinding(1);
        Assert(holder == "Saved states=id bryson-section-house" && CommandBand.KeyObjectBinding("") == "Saved states=no id", "The key-value object binding names its id: " + holder);
        Assert(CommandBand.TogglesBelowBinding(5) == "Toggles below=5" && CommandBand.NameTextBinding("Chance Clayton") == "Name text=Chance Clayton" && holderBelow == "Saved states below=1",
            "The counts and the name text are bindings");

        string Use(bool part, params string[] bindings) => CommandBand.UseInGame(part, BakedSceneryCategories.RailroadServiceBuilding, null, bindings);
        Assert(Use(true, lantern) == "Lantern switch: click the lantern in the game to switch it on or off. The game keeps its state by its key lantern.0, not by its position, so the state stays when this part moves. While this part is disabled, the lantern cannot be clicked.",
            "The lantern part names its switch: " + Use(true, lantern));
        Assert(Use(true, door, CommandBand.KeyObjectBinding(null)).StartsWith("Door toggle: click the doors in the game to open or close them. The game keeps its state by its key doors, not by its position, so the state stays when this part moves. While this part is disabled, the doors cannot be clicked.", StringComparison.Ordinal),
            "A stall names its door toggle: " + Use(true, door, CommandBand.KeyObjectBinding(null)));
        Assert(Use(false, holder, CommandBand.TogglesBelowBinding(1)) == "This group holds the states of its toggles under the saved ID bryson-section-house. The game keeps them by this ID, not by position. A game save made while this group is disabled might not keep them. " +
            "Its toggle keeps its state by its key, not by its position, and works at its new place.", "The Section House names the id and its toggle: " + Use(false, holder, CommandBand.TogglesBelowBinding(1)));
        Assert(Use(false, CommandBand.KeyObjectBinding(null)) == "This group holds the states of its toggles. It has no saved ID, so the game does not save them.", "A key-value object without an id says the states are not saved");
        // Disabling a group or part above the object that holds the states disables that object too: it warns as well.
        Assert(Use(false, holderBelow, CommandBand.TogglesBelowBinding(5)) == "Disabling this group also disables the object that holds the states of its toggles, so a game save made while this group is disabled might not keep them. " +
            "Its 5 toggles keep their states by their keys, not by their positions, and work at their new place.", "The Bryson Turntable root warns about the holder below it: " + Use(false, holderBelow, CommandBand.TogglesBelowBinding(5)));
        Assert(Use(true, CommandBand.TogglesBelowBinding(5)) == "Its 5 toggles keep their states by their keys, not by their positions, and work at their new place.", "A part above toggles counts them: " + Use(true, CommandBand.TogglesBelowBinding(5)));
        Assert(Use(true, CommandBand.NameTextBinding("Chance Clayton")) == "Name text \"Chance Clayton\": the scene keeps the text with this part, not by its position, so the text moves with it.",
            "The name text names its text: " + Use(true, CommandBand.NameTextBinding("Chance Clayton")));
        Assert(Use(true) == CommandBand.PartDecorative && Use(false) == CommandBand.Decorative, "A part or group without toggles reads the decorative text");
        // The Object tab passes the selected entry's bindings; the group's bindings do not change a part's text.
        SceneObservation Entry(string path, string groupPath, params string[] bindings) =>
            new SceneObservation(new object(), new TargetIdentity("s", path, BakedSceneryGroups.ComponentType, groupPath == null ? BakedSceneryGroups.Structure : BakedSceneryParts.Structure),
                BakedSceneryRules.Family, true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, bindings, new string[0], new string[0], "edit", groupPath: groupPath,
                category: groupPath == null ? BakedSceneryCategories.RailroadServiceBuilding : null);
        var root = Entry(SectionHouse, null, holder, CommandBand.TogglesBelowBinding(1));
        var shed = Entry(SectionHouse + "/Common Standard Tool House", SectionHouse);
        Assert(CommandBand.UseInGame(Entry(SectionHouse + "/TrainmanLantern", SectionHouse, lantern), root) == Use(true, lantern) && CommandBand.UseInGame(shed, root) == CommandBand.PartDecorative,
            "The inspector reads the selected entry's own bindings: " + CommandBand.UseInGame(shed, root));
    }

    // The bindings come from the fields the game serializes: the toggle's key and the key-value object's global id.
    sealed class PickToggle { public string displayTitle = "Toggle"; public string key; }
    sealed class GlobalObject { public string globalObjectId; }
    sealed class Unrelated { public string name = "x"; }
    static void BindingsReadWhatTheGameReads() {
        Assert(CommandBand.ToggleKeyOf(new PickToggle { key = "lantern.0" }) == "lantern.0" && CommandBand.GlobalIdOf(new GlobalObject { globalObjectId = "roundhouse-3" }) == "roundhouse-3",
            "The toggle key and the global id are read from their serialized fields");
        var missing = 0;
        try { CommandBand.ToggleKeyOf(new Unrelated()); } catch (MissingFieldException) { missing++; }
        try { CommandBand.GlobalIdOf(new Unrelated()); } catch (MissingFieldException) { missing++; }
        Assert(missing == 2, "A component without the field fails the readout instead of reading no key");
    }

    // Roundhouse 3 as the group and part capabilities see it: the group root carries the renderer culler, a stall part
    // stands below it beside the other parts, and the animated stall object below the stall is observed with the group,
    // never resolved. Bryson Turntable holds the same stalls one level deeper, below its Roundhouse part.
    const string SceneName = "BushnellWhittier-MapManager";
    static readonly double[] Upright = { 0, 0, 0, 1 }, Authored = { 1600.026, 529.0, 1307.409 }, Moved = { 1640.0, 529.0, 1280.0 }, StallMoved = { 1660.0, 530.0, 1300.0 };
    sealed class Yard {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeGroup Group = new FakeGroup { Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakePart Between, Stall, Doors, Spot;
        public readonly PatchEngine Engine;
        public Yard() {
            Group.Position = new[] { 600.026, 529.0, 1307.409 };
            Between = Group.Add(new FakePart { Id = "Roundhouse Modular A Between#1", Offset = new[] { 0.0, 0, -8 } });
            Stall = Group.Add(new FakePart { Id = "Stall (1)#2", Offset = new[] { 10.0, -0.33, 5 } });
            // The stall object that holds the toggle and animates the doors: observed with its group, never resolved.
            Doors = Group.Add(new FakePart { Id = "Roundhouse Modular A Stall#3", Offset = new[] { 10.0, -0.33, 9 }, Parent = Stall, Animated = true });
            Spot = Group.Add(new FakePart { Id = "Spot Light (1)#4", Offset = new[] { -6.0, 5, 0 } });
            Group.Cullers.Add(new FakeCuller { Id = "Roundhouse 3|RendererCuller", Simple = false, Radius = 40, Authored = 40 });
            var verdicts = BakedSceneryRules.Classify(InteractiveBuildingTests.World());
            World.GroupVerdicts = verdicts.ToDictionary(v => v.Path, v => v);
            World.Verdicts = BakedSceneryRules.Parts(verdicts).ToDictionary(v => v.Path, v => v);
            World.Objects[Roundhouse3] = new List<object> { Group };
            World.PartsAt[Roundhouse3 + "/Roundhouse Modular A Between"] = new List<object> { Between };
            World.PartsAt[Roundhouse3 + "/Stall (1)"] = new List<object> { Stall };
            World.PartsAt[Roundhouse3 + "/Spot Light (1)"] = new List<object> { Spot };
            World.SetActive(Group, true); World.Mutations = 0;
            var holds = new PartHolds();
            var scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new BakedSceneryGroups(World, holds: holds)); scene.Add(new BakedSceneryParts(World, holds));
            Engine = new PatchEngine(scene);
        }
        public double[] Root => World.Observe(Group).Root.Placement.Position.ToArray();
        public double[] At(FakePart part) => World.ObservePart(part).Part.Placement.Position.ToArray();
        public string Where() => "root " + N(Root) + "; " + String.Join("; ", Group.Parts.Select(p => p.Id + "=" + N(At(p)) + (p.Active ? "" : " off")));
    }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) => a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= 0.001);
    static string Mod(string id, string path, string structure, double[] position = null, bool? active = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", position.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[0,0,0,1]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string GroupMod(string id, double[] position = null, bool? active = null) => Mod(id, Roundhouse3, BakedSceneryGroups.Structure, position, active);
    static string StallMod(string id, double[] position = null, bool? active = null) => Mod(id, Roundhouse3 + "/Stall (1)", BakedSceneryParts.Structure, position, active);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static readonly TargetIdentity GroupId = new TargetIdentity(SceneName, Roundhouse3, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
    static readonly TargetIdentity StallId = new TargetIdentity(SceneName, Roundhouse3 + "/Stall (1)", BakedSceneryParts.ComponentType, BakedSceneryParts.Structure);

    // A group move carries the stalls and their doors; a stall move carries its doors and leaves the
    // rest in place; a repeated application and a load at another origin reach the same placements; a refusal mutates
    // nothing; a failure after mutation restores the placement and activity; an incomplete cleanup blocks the retry.
    static void BuildingEditsApplyRepeatAndRestore() {
        var f = new Yard();
        Assert(Near(f.Root, Authored), "The controlled turntable stands at its authored place: " + f.Where());
        var doc = Doc(GroupMod("move-turntable", Moved));
        var result = f.Engine.Apply(doc);
        Assert(result.Outcomes.Values.All(o => o.Applied), "The group move applies: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        Assert(Near(f.Root, Moved) && Near(f.At(f.Stall), new[] { 1650.0, 528.67, 1285.0 }) && Near(f.At(f.Doors), new[] { 1650.0, 528.67, 1289.0 }),
            "The stall and its doors move with the group: " + f.Where());
        var first = f.Where();
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && f.Where() == first, "A repeated application accumulates no offset: " + f.Where());
        var shifted = new Yard(); shifted.World.Origin = 1010;
        Assert(shifted.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && shifted.Where() == first, "A load at another origin reaches the same map placements: " + shifted.Where());

        var stall = new Yard();
        var moved = stall.Engine.Apply(Doc(StallMod("move-stall", StallMoved)));
        Assert(moved.Outcomes.Values.All(o => o.Applied) && Near(stall.At(stall.Stall), StallMoved) && Near(stall.At(stall.Doors), new[] { 1660.0, 530.0, 1304.0 })
            && Near(stall.Root, Authored) && Near(stall.At(stall.Between), new[] { 1600.026, 529.0, 1299.409 }),
            "A stall move carries its doors and leaves the other parts in place: " + stall.Where() + " " + String.Join("; ", moved.Outcomes.Values.Select(o => o.Error)));
        var unresolved = new Yard();
        var wrong = unresolved.Engine.Apply(Doc(Mod("doors", Roundhouse3 + "/Stall (1)/Roundhouse Modular A Stall", BakedSceneryParts.Structure, StallMoved),
            Mod("door", Roundhouse3 + "/Stall (1)/Roundhouse Modular A Stall/Roundhouse Stall Doors/Bone.002/Bone/Roundhouse Engine Door Left", BakedSceneryParts.Structure, StallMoved),
            Mod("container", Tower + "/Culling Container", BakedSceneryParts.Structure, StallMoved)));
        Assert(wrong.Outcomes.Values.All(o => !o.Applied) && unresolved.World.Mutations == 0 && unresolved.Where() == new Yard().Where(),
            "The animated stall object, its door and the Tower's culling container fail before any change: " + unresolved.World.Mutations + " mutations; " +
            String.Join("; ", wrong.Outcomes.Select(o => o.Key + " " + o.Value.Applied)));

        var refused = new Yard(); refused.World.Refused = "The group's scene is not loaded";
        var outcome = refused.Engine.Apply(Doc(GroupMod("move-turntable", Moved))).Outcomes["move-turntable"];
        Assert(!outcome.Applied && refused.World.Mutations == 0 && Near(refused.Root, Authored) && refused.Group.Active, "A preflight refusal leaves the building untouched: " + outcome.Error);

        foreach (var inject in new Action<FakeGroupWorld>[] { w => w.ThrowAfterPlacement = true, w => w.LeavePartBehind = true }) {
            var failed = new Yard(); inject(failed.World);
            outcome = failed.Engine.Apply(Doc(GroupMod("move-turntable", Moved, false))).Outcomes["move-turntable"];
            Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal) && Near(failed.Root, Authored) && failed.Group.Active
                && Near(failed.At(failed.Stall), new[] { 1610.026, 528.67, 1312.409 }), "A failure after mutation restores the building's placement and activity: " + outcome.Error + " " + failed.Where());
        }
        var part = new Yard(); part.World.ThrowAfterPartPlacement = true;
        outcome = part.Engine.Apply(Doc(StallMod("move-stall", StallMoved, false))).Outcomes["move-stall"];
        Assert(!outcome.Applied && Near(part.At(part.Stall), new[] { 1610.026, 528.67, 1312.409 }) && part.Stall.Active && Near(part.At(part.Doors), new[] { 1610.026, 528.67, 1316.409 }),
            "A failure after a stall move restores the stall and its doors: " + outcome.Error + " " + part.Where());

        var stuck = new Yard(); stuck.World.LeavePartBehind = stuck.World.KeepPartBehind = true;
        outcome = stuck.Engine.Apply(Doc(GroupMod("move-turntable", Moved))).Outcomes["move-turntable"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        stuck.World.LeavePartBehind = stuck.World.KeepPartBehind = false; var mutations = stuck.World.Mutations;
        var retry = stuck.Engine.Apply(Doc(GroupMod("again", StallMoved))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && stuck.World.Mutations == mutations, "The retry on the affected building is blocked: " + retry.Error);
    }

    // In scene patch editing: Undo and Redo of a group move and a stall edit with matching dirty state and Save
    // availability, Remove modification returning the building to its authored place, Save at format version 1 with the
    // patcher dependency, a stricter constraint kept, and a reload that reproduces the edits without duplicated state.
    static void BuildingEditsInScenePatchEditing() {
        var f = new Yard(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Assert(session.Edit(GroupId, new Placement(Moved, Upright), null).Applied && session.Edit(StallId, new Placement(StallMoved, Upright), false).Applied, "A group move and a stall move and disable apply");
        Persistence(true, all, "After two edits");
        mapEditor.Undo(); Assert(f.Stall.Active && Near(f.At(f.Stall), new[] { 1650.0, 528.67, 1285.0 }) && Near(f.Root, Moved), "Undo returns the stall; the group stays moved: " + f.Where());
        mapEditor.Undo(); Assert(Near(f.Root, Authored) && Near(f.At(f.Doors), new[] { 1610.026, 528.67, 1316.409 }), "Undo returns the building and its doors: " + f.Where());
        Persistence(false, new string[0], "After Undo back to the unedited building");
        mapEditor.Redo(); mapEditor.Redo();
        Assert(Near(f.Root, Moved) && !f.Stall.Active && Near(f.At(f.Stall), StallMoved), "Redo reapplies every edit: " + f.Where()); Persistence(true, all, "After Redo");
        var removed = session.Remove(GroupId);
        Assert(removed.Applied && Near(f.Root, Authored) && !f.Stall.Active && Near(f.At(f.Stall), StallMoved), "Remove modification returns the group and keeps the fixed stall edit: " + removed.Error + " " + f.Where());
        mapEditor.Undo(); Assert(Near(f.Root, Moved), "Undo of the removal moves the building again: " + f.Where());
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0") && (int)JObject.Parse(store.Patch)["formatVersion"] == 1, "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        var fresh = new Yard();
        var reload = fresh.Engine.Apply(store.Patch);
        var saved = (JObject)JObject.Parse(store.Patch)["modifications"];
        Assert(saved.Count == 2 && reload.Outcomes.Count == 2 && reload.Outcomes.Values.All(o => o.Applied) && Near(fresh.Root, Moved) && !fresh.Stall.Active && Near(fresh.At(fresh.Stall), StallMoved),
            "A reloaded world reproduces the edits from one modification per target: " + fresh.Where() + " " + saved);
        var once = fresh.Where();
        Assert(fresh.Engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && fresh.Where() == once, "Applying the saved file again adds no offset: " + fresh.Where());
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = new Yard(); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(other.Engine, strictEditor, strict); strictEditor.Session = kept;
        Assert(kept.Edit(GroupId, new Placement(Moved, Upright), null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=0.5.0"), "A stricter existing constraint is retained: " + strict.Info);
    }
}
