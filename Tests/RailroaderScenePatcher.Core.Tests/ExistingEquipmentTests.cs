// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Whole scene patch file tests for the existing masts and bell of a crossing against a controlled crossing: a controller
// whose game-object culler lists the object above each mast, two masts with a gate below the light object, and the
// bell the controller creates. Placement, rotation and Enabled, culling, refusal, restoration, history, save and reload.
static class ExistingEquipmentTests {
    const string SceneName = "BushnellWhittier-MapManager", Crossing = "World/Large Scenery/Bryson/Grade Crossing - Bryson Everett";
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    static readonly double[] Identity = { 0, 0, 0, 1 };
    static string Mod(string id, string path, string component, string structure, double[] position = null, double[] rotation = null, bool? active = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"" + component + "\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + N(position) + "],\"rotation\":[" + N(rotation ?? Identity) + "]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    const string Mast1Path = Crossing + "/Signal 1/Signal 1", Mast2Path = Crossing + "/Signal 2/Signal 2", BellPath = Crossing + "/Crossing Bell";
    static string Mast(string id, string path = Mast1Path, double[] position = null, double[] rotation = null, bool? active = null) =>
        Mod(id, path, ExistingEquipmentScene.MastComponent, ExistingEquipmentScene.MastStructure, position, rotation, active);
    static string Bell(string id, double[] position = null, double[] rotation = null, bool? active = null) =>
        Mod(id, BellPath, ExistingEquipmentScene.BellComponent, ExistingEquipmentScene.BellStructure, position, rotation, active);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static TargetIdentity MastId(string path = Mast1Path) => new TargetIdentity(SceneName, path, ExistingEquipmentScene.MastComponent, ExistingEquipmentScene.MastStructure);
    static readonly TargetIdentity BellId = new TargetIdentity(SceneName, BellPath, ExistingEquipmentScene.BellComponent, ExistingEquipmentScene.BellStructure);
    static readonly double[] Near1 = { 1090, 10, 26 }, Far = { 1400, 10, 20 }, Quarter = Turn.Yaw(90);

    // The crossing stands at map 1100, 10, 20. Its culler has a 30 m sphere and lists the object above each mast.
    internal sealed class Fixture : IRelatedInspectionScene {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public FakeGroup Controller;
        public FakePart Outer1, Mast1, Gate1, Outer2, Mast2, Bell;
        public FakeCuller Culler;
        public readonly PartHolds Holds = new PartHolds();
        public readonly SceneCapabilities Scene;
        public readonly PatchEngine Engine;
        public Fixture() {
            Build();
            Scene = new SceneCapabilities(() => World.Maps, this);
            Scene.Add(ExistingEquipmentScene.Masts(World, Holds)); Scene.Add(ExistingEquipmentScene.Bells(World, Holds));
            Engine = new PatchEngine(Scene);
        }
        // The crossing as the game builds it when its scene loads.
        void Build() {
            Controller = new FakeGroup();
            Controller.Parts.Clear(); Controller.Cullers.Clear(); Controller.RootCulling = null;
            // As in the game, the object above a mast shows nothing of its own: every renderer is on the light object or below it.
            Outer1 = Controller.Add(new FakePart { Id = "Signal 1#1", Offset = new[] { -6.0, 0, 4 }, Empty = true });
            Mast1 = Controller.Add(new FakePart { Id = "Signal 1/Signal 1#2", Offset = new[] { -6.0, 0, 4 }, Parent = Outer1 });
            Gate1 = Controller.Add(new FakePart { Id = "Signal 1/Signal 1/Wishbone#3", Offset = new[] { -6.0, 3, 4 }, Parent = Mast1 });
            Outer2 = Controller.Add(new FakePart { Id = "Signal 2#4", Offset = new[] { 6.0, 0, -4 }, Empty = true });
            Mast2 = Controller.Add(new FakePart { Id = "Signal 2/Signal 2#5", Offset = new[] { 6.0, 0, -4 }, Parent = Outer2 });
            Bell = Controller.Add(new FakePart { Id = "Crossing Bell#6", Offset = new[] { 0.0, 0, 0 } });
            Culler = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Radius = 30, Authored = 30, Lists = { Outer1, Outer2 } };
            Controller.Cullers.Add(Culler);
            World.Objects[Crossing] = new List<object> { Controller };
            foreach (var (path, part) in new[] { (Mast1Path, Mast1), (Mast2Path, Mast2), (BellPath, Bell) }) World.PartsAt[path] = new List<object> { part };
            World.SetActive(Controller, true); World.Mutations = 0;
        }
        // The peer leaves and joins again: the map unloads, which destroys the crossing with its culling managers and the
        // spheres in them, and the game builds the crossing anew.
        public void Rejoin() {
            World.Native.Unloading = true; World.Culling.Unload(); World.Native.Registered.Clear();
            World.Objects.Clear(); World.PartsAt.Clear();
            World.Native.Unloading = false;
            Build();
        }
        public IEnumerable<string> MapIds => World.Maps;
        public IEnumerable<SceneObservation> Discover() => World.PartsAt.Select(pair => Of(pair.Value[0]));
        SceneObservation Of(object handle) {
            var part = (FakePart)handle; var state = World.ObservePart(part).Part; var path = World.PartsAt.Single(p => p.Value.Contains(part)).Key;
            var bell = part == Bell;
            return new SceneObservation(part, new TargetIdentity(SceneName, path, bell ? ExistingEquipmentScene.BellComponent : ExistingEquipmentScene.MastComponent, bell ? ExistingEquipmentScene.BellStructure : ExistingEquipmentScene.MastStructure),
                bell ? ExistingEquipmentScene.BellFamily : ExistingEquipmentScene.MastFamily, state.Active == true, state.Placement.Position, state.Placement.Rotation, new string[0], new string[0], new string[0], "equipment");
        }
        public SceneObservation Observe(object handle, TargetIdentity identity) => Of(handle);
        public IEnumerable<SceneObservation> ObserveRelated(object handle, TargetIdentity identity) => new[] { Of(handle) };
        public bool Present(object handle) => true;
        public Placement MapOf(FakePart part) => World.ObservePart(part).Part.Placement;
    }
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b, double tolerance = 0.001) => a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= tolerance);
    static bool SameTurn(IReadOnlyList<double> a, IReadOnlyList<double> b) => Math.Abs(Math.Abs(a.Zip(b, (x, y) => x * y).Sum()) - 1) < 1e-6;
    static string Where(Fixture f) => String.Join("; ", f.Controller.Parts.Select(p => p.Id + "=" + N(f.MapOf(p).Position.Select(v => Math.Round(v, 3)))));
    static (Store, NativeMapEditor, PatchEditingSession) Session(Fixture f, Store store = null) {
        store = store ?? new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        return (store, mapEditor, session);
    }

    public static void Run() {
        MastMovesAndTurnsWithItsGate();
        BellMovesOnItsOwn();
        EnabledIsItsOwnOperation();
        MovedMastKeepsItsOwnCulling();
        DisabledFarMastNeedsNoCulling();
        TrackFollowingPlacementIsRefused();
        ActivityThatACullerSwitchesIsRefused();
        GroupDrivenActivityAndTheExplicitDisable();
        RefusalsLeaveSceneAndFileUnchanged();
        FailureAfterMutationRestoresEquipmentAndCulling();
        IncompleteCleanupBlocksTheRetry();
        HistorySaveReloadAndRemoval();
        RepeatedApplicationAccumulatesNothing();
        RejoinAppliesOnceToTheRebuiltCrossing();
        PatternsNameWhyEquipmentDoesNotConform();
        Assert(ExistingEquipmentScene.MastStructure == "existing-crossing-mast-v1" && ExistingEquipmentScene.BellStructure == "existing-crossing-bell-v1", "The structure names that scene patch files hold");
        DetectionSpansNameTheirController();
        InspectorNamesWhatAppliesAndWhy();
        Console.WriteLine("PASS: existing crossing equipment: mast and bell placement, rotation and Enabled, own culling, refusal, restoration, history, save, reload and removal");
    }

    static void MastMovesAndTurnsWithItsGate() {
        var f = new Fixture();
        var outcome = f.Engine.Apply(Doc(Mast("move-mast", position: Near1, rotation: Quarter))).Outcomes["move-mast"];
        Assert(outcome.Applied, "A mast move applies: " + outcome.Error);
        Assert(Near(f.MapOf(f.Mast1).Position, Near1) && SameTurn(f.MapOf(f.Mast1).Rotation, Quarter), "The mast stands at the placement the patch sets: " + Where(f));
        // The gate stood 3 m above the light object and turns with it.
        Assert(Near(f.MapOf(f.Gate1).Position, new[] { 1090.0, 13, 26 }) && SameTurn(f.MapOf(f.Gate1).Rotation, Quarter), "The gate moves and turns with its light object as one: " + Where(f));
        Assert(Near(f.MapOf(f.Outer1).Position, new[] { 1094.0, 10, 24 }) && Near(f.MapOf(f.Mast2).Position, new[] { 1106.0, 10, 16 }) && Near(f.MapOf(f.Bell).Position, new[] { 1100.0, 10, 20 })
            && Near(f.World.Observe(f.Controller).Root.Placement.Position, new[] { 1100.0, 10, 20 }), "The controller, the object above the mast, the other mast and the bell stay: " + Where(f));
        Assert(f.Culler.Lists.SequenceEqual(new[] { f.Outer1, f.Outer2 }) && f.World.Native.Registered.Count == 0, "A move inside the culler's sphere leaves the culler's list and adds no sphere");
    }

    static void BellMovesOnItsOwn() {
        var f = new Fixture();
        var outcome = f.Engine.Apply(Doc(Bell("move-bell", new[] { 1103.0, 12, 21 }))).Outcomes["move-bell"];
        Assert(outcome.Applied && Near(f.MapOf(f.Bell).Position, new[] { 1103.0, 12, 21 }), "A bell move applies: " + outcome.Error);
        Assert(Near(f.MapOf(f.Mast1).Position, new[] { 1094.0, 10, 24 }) && Near(f.MapOf(f.Mast2).Position, new[] { 1106.0, 10, 16 }), "The masts stay: " + Where(f));
        // A bell far from its crossing needs no culling: no culler of the game switches it.
        Assert(f.Engine.Apply(Doc(Bell("move-bell", Far))).Outcomes["move-bell"].Applied && f.World.Native.Registered.Count == 0 && f.Culler.Lists.Count == 2, "A far bell gets no culling sphere");
    }

    static void EnabledIsItsOwnOperation() {
        var f = new Fixture();
        var outcome = f.Engine.Apply(Doc(Mast("mast-off", active: false), Bell("bell-off", active: false)));
        Assert(outcome.Outcomes.Values.All(o => o.Applied), "Disabling applies: " + String.Join("; ", outcome.Outcomes.Values.Select(o => o.Error)));
        Assert(!f.Mast1.Active && !f.Bell.Active && f.Gate1.Active && f.Outer1.Active && f.Mast2.Active && f.Controller.Active, "Only the mast's light object and the bell are switched off; the gate goes off with its light object");
        Assert(Near(f.MapOf(f.Mast1).Position, new[] { 1094.0, 10, 24 }) && Near(f.MapOf(f.Bell).Position, new[] { 1100.0, 10, 20 }), "An activity change leaves the placement: " + Where(f));
        var moved = f.Engine.Apply(Doc(Mast("mast-off", position: Near1, active: false))).Outcomes["mast-off"];
        Assert(moved.Applied && Near(f.MapOf(f.Mast1).Position, Near1) && !f.Mast1.Active, "A placement beside Enabled leaves the activity as the file sets it: " + moved.Error);
        var g = new Fixture();
        Assert(g.Engine.Apply(Doc(Mast("move", position: Near1))).Outcomes["move"].Applied && g.Mast1.Active && g.Gate1.Active, "A placement without Enabled leaves the activity to the scene");
    }

    // A mast moved out of its crossing culler's sphere gets a sphere of its own: the culler keeps its radius, the
    // object above the mast leaves the culler's list and follows the mast's sphere, and no sphere stays at the old place.
    static void MovedMastKeepsItsOwnCulling() {
        var f = new Fixture();
        var outcome = f.Engine.Apply(Doc(Mast("far-mast", position: Far))).Outcomes["far-mast"];
        Assert(outcome.Applied, "A far mast move applies: " + outcome.Error);
        Assert(f.Culler.Radius == 30 && f.Culler.Authored == 30, "The crossing's culler keeps its radius");
        var own = f.World.Native.Registered.SingleOrDefault(r => r.Owner == f.Mast1);
        Assert(own != null && Near(own.Centre, new[] { 400.0, 11.5, 20 }) && own.Radius < 5, "The moved mast has a culling sphere of its own at the mast: " + (own == null ? "none" : N(own.Centre) + " r" + own.Radius));
        Assert(!f.Culler.Lists.Contains(f.Outer1) && f.Culler.Lists.Contains(f.Outer2) && f.World.Culling.HandedOver(f.Culler).Contains(f.Outer1), "The object above the moved mast is handed over; the other mast stays the culler's");
        Assert(f.World.Native.Registered.Count == 1 && f.World.Culling.Regions(f.Controller).Select(r => r.Owner).SequenceEqual(new object[] { f.Mast1 }), "The mast's own sphere is the only one: none stays at the old place, and an inspection lists only that one");
        // The camera leaves the crossing and faces the mast: the mast stays on; then it leaves the mast: the mast goes off.
        f.Culler.Shows = false;
        Assert(f.Outer1.Active && f.Mast1.Active, "While its own sphere is visible the moved mast never vanishes, whatever the crossing's culler would do");
        f.World.Show(own, false);
        Assert(!f.Mast1.Active, "Out of view the moved mast is culled");
        f.World.Show(own, true);
        Assert(f.Mast1.Active && f.Outer1.Active, "Back in view it shows again");
        // Back inside the sphere, everything the own culling took returns to the game.
        var back = f.Engine.Apply(Doc(Mast("far-mast", position: Near1))).Outcomes["far-mast"];
        Assert(back.Applied && f.World.Native.Registered.Count == 0 && f.Culler.Lists.Contains(f.Outer1) && f.Culler.Lists.Count == 2, "Moved back, the mast's own sphere is released and the list is as before: " + back.Error);
        // Through the session: Undo, Redo and Remove modification keep at most the mast's own sphere.
        var g = new Fixture(); var (_, mapEditor, session) = Session(g);
        bool OnlyTheMast() => g.World.Native.Registered.Count == 1 && g.World.Native.Registered.Single().Owner == g.Mast1 && g.World.Culling.Regions(g.Controller).Count == 1;
        Assert(session.Edit(MastId(), new Placement(Far, Identity), null).Applied && OnlyTheMast(), "The session move holds exactly one sphere, the mast's");
        mapEditor.Undo();
        Assert(g.World.Native.Registered.Count == 0 && g.Culler.Lists.SequenceEqual(new[] { g.Outer1, g.Outer2 }), "Undo holds none, and the list is as before");
        mapEditor.Redo();
        Assert(OnlyTheMast() && !g.Culler.Lists.Contains(g.Outer1), "Redo holds exactly the mast's sphere again");
        Assert(session.Remove(MastId()).Applied && g.World.Native.Registered.Count == 0 && g.Culler.Lists.SequenceEqual(new[] { g.Outer1, g.Outer2 }), "Remove modification holds none");
    }

    // A disabled mast shows nowhere: it gets no sphere of its own and the crossing's culler keeps its list. Enabled
    // again at the same far placement, it gets its sphere.
    static void DisabledFarMastNeedsNoCulling() {
        var f = new Fixture(); var (_, _, session) = Session(f);
        Assert(session.Edit(MastId(), new Placement(Far, Identity), null).Applied && session.Edit(MastId(), null, false).Applied, "A far mast is disabled");
        Assert(!f.Mast1.Active && f.World.ObservePart(f.Mast1).Part.Active == false, "The mast is off and reads as disabled");
        Assert(f.World.Native.Registered.Count == 0 && f.Culler.Lists.SequenceEqual(new[] { f.Outer1, f.Outer2 }) && f.World.Culling.HandedOver(f.Culler).Count == 0, "A disabled mast holds no sphere, and the object above it is back in the culler's list");
        Assert(session.Edit(MastId(), null, true).Applied && f.Mast1.Active && f.World.Native.Registered.Any(r => r.Owner == f.Mast1) && !f.Culler.Lists.Contains(f.Outer1), "Enabled again, the far mast has its own sphere");
    }

    // Track-following placement does not apply to existing equipment: a file that asks for it is refused before mutation.
    sealed class AnyTrack : ITrackPoseScene { public TrackPose TrackPose(TrackAnchor anchor) => new TrackPose(new[] { 1200.0, 10, 20 }, new[] { 0.0, 0, 0, 1 }); }
    static void TrackFollowingPlacementIsRefused() {
        // The track resolves, so the refusal is the capability's and not a missing track graph.
        var f = new Fixture(); f.Scene.Tracks = new AnyTrack();
        string Follow(string id, string path, string component, string structure) =>
            "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"" + component + "\",\"structure\":\"" + structure + "\"}," +
            "\"placement\":{\"mode\":\"trackFollowing\",\"track\":{\"segment\":\"S1\",\"end\":\"a\",\"distance\":10},\"offset\":[2,0,0],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var result = f.Engine.Apply(Doc(Follow("mast", Mast1Path, ExistingEquipmentScene.MastComponent, ExistingEquipmentScene.MastStructure), Follow("bell", BellPath, ExistingEquipmentScene.BellComponent, ExistingEquipmentScene.BellStructure)));
        foreach (var id in new[] { "mast", "bell" })
            Assert(!result.Outcomes[id].Applied && result.Outcomes[id].Error.Contains("Track-following placement does not apply"), "A track-following placement of a " + id + " is refused with its reason: " + result.Outcomes[id].Error);
        Assert(f.World.Mutations == 0 && Near(f.MapOf(f.Mast1).Position, new[] { 1094.0, 10, 24 }) && Near(f.MapOf(f.Bell).Position, new[] { 1100.0, 10, 20 }), "Nothing moved");
    }

    static void ActivityThatACullerSwitchesIsRefused() {
        var f = new Fixture(); f.Culler.Lists.Add(f.Mast2);
        var outcome = f.Engine.Apply(Doc(Mast("off", Mast2Path, active: false))).Outcomes["off"];
        Assert(!outcome.Applied && outcome.Error.Contains("culler") && f.World.Mutations == 0 && f.Mast2.Active, "Enabled is refused before mutation for equipment a culler switches itself: " + outcome.Error);
        Assert(f.Engine.Apply(Doc(Mast("move", Mast2Path, position: new[] { 1108.0, 10, 16 }))).Outcomes["move"].Applied, "Its placement can still be edited");
    }

    // Equipment whose activity the game's switches drive. The mast pattern accepts a light object or bell that a
    // progression or a map feature names in its switch list, and one below an object that such a switch names: an explicit
    // disable holds through the switch's later writes, and without one the switch keeps switching the equipment. A light
    // object that a track-relative component switches with its track group does not conform: its disable is refused
    // before any change, and its group keeps switching it.
    static void GroupDrivenActivityAndTheExplicitDisable() {
        foreach (var kind in new[] { GameSwitch.Progression, GameSwitch.MapFeature }) {
            // The switch names the light object, or the bell, itself.
            var f = new Fixture(); f.Mast1.Constructed = f.Bell.Constructed = true; f.Mast1.Switch = f.Bell.Switch = kind;
            var off = f.Engine.Apply(Doc(Mast("mast-off", active: false), Bell("bell-off", active: false)));
            Assert(off.Outcomes.Values.All(o => o.Applied) && !f.Mast1.Active && !f.Bell.Active, kind + ": the explicit disable of a switched mast and bell applies: " + String.Join("; ", off.Outcomes.Values.Select(o => o.Error)));
            f.World.Controller(f.Mast1, true); f.World.Controller(f.Bell, true);
            Assert(!f.Mast1.Active && !f.Bell.Active && f.World.ObservePart(f.Mast1).Part.Active == false && f.World.Constructions.Held(f.Mast1), kind + ": the switch turning them on does not reach the disabled mast and bell");
            Assert(f.Engine.Apply(Doc(Mast("mast-off", active: false), Bell("bell-off", active: false))).Outcomes.Values.All(o => o.Applied) && !f.Mast1.Active && !f.Bell.Active, kind + ": a repeated application keeps the hold");
            var on = f.Engine.Apply(Doc(Mast("mast-on", active: true))).Outcomes["mast-on"];
            Assert(on.Applied && f.Mast1.Active && !f.World.Diverted(f.Mast1), kind + ": Enable returns the mast to the switch's current choice and back to the switch: " + on.Error);
            f.World.Controller(f.Mast1, false);
            Assert(!f.Mast1.Active, kind + ": the switch switches the enabled mast again");
            var g = new Fixture(); g.Mast1.Constructed = true; g.Mast1.Switch = kind;
            Assert(g.Engine.Apply(Doc(Mast("move", position: Near1))).Outcomes["move"].Applied, kind + ": a move without Enabled applies");
            g.World.Controller(g.Mast1, false);
            Assert(!g.Mast1.Active && !g.World.Diverted(g.Mast1), kind + ": without an explicit disable the switch turns the mast off");
            g.World.Controller(g.Mast1, true);
            Assert(g.Mast1.Active && Near(g.MapOf(g.Mast1).Position, Near1), kind + ": and on again, at its placement");
        }
        // A disable that could not hold, because the game's switch members are unavailable, is refused with the reason
        // before any change, for a mast and a bell, in a load and in scene patch editing. Its placement and Enable still apply.
        foreach (var bell in new[] { false, true }) {
            var u = new Fixture(); u.Mast1.Constructed = u.Bell.Constructed = true; u.Mast1.Switch = u.Bell.Switch = GameSwitch.Progression;
            u.World.DisableRefused = GameSwitchActivity.ProgressionMembersRefusal;
            var part = bell ? u.Bell : u.Mast1;
            var refusedOff = u.Engine.Apply(Doc(bell ? Bell("off", active: false) : Mast("off", active: false))).Outcomes["off"];
            Assert(!refusedOff.Applied && refusedOff.Failure == FailureKind.Refused && refusedOff.Error.Contains(GameSwitchActivity.ProgressionMembersRefusal) && u.World.Mutations == 0 && part.Active,
                (bell ? "Bell" : "Mast") + ": a disable that could not hold is refused with the reason before any change: " + refusedOff.Failure + " " + refusedOff.Error);
            var (_, uEditor, uSession) = Session(u);
            var uBefore = uSession.ScenePatchFileText;
            Assert(!uSession.Edit(bell ? BellId : MastId(), null, false).Applied && uSession.ScenePatchFileText == uBefore && !uEditor.CanUndo && u.World.Mutations == 0, (bell ? "Bell" : "Mast") + ": scene patch editing refuses it too and leaves the file and history unchanged");
            Assert(u.Engine.Apply(Doc(bell ? Bell("move", position: new[] { 1101.0, 10, 21 }) : Mast("move", position: Near1))).Outcomes["move"].Applied, (bell ? "Bell" : "Mast") + ": its placement still applies");
            Assert(u.Engine.Apply(Doc(bell ? Bell("on", active: true) : Mast("on", active: true))).Outcomes["on"].Applied, (bell ? "Bell" : "Mast") + ": Enable is no disable and still applies");
        }
        // The switch names the object above the mast: the mast's own activity holds the disable while the switch turns that
        // object off and on, and a load while the switch holds it off applies the disable as well.
        var above = new Fixture();
        Assert(above.Engine.Apply(Doc(Mast("mast-off", active: false))).Outcomes["mast-off"].Applied && !above.Mast1.Active, "The disable of a mast below a switched object applies");
        above.World.Controller(above.Outer1, false);
        Assert(above.Engine.Apply(Doc(Mast("mast-off", active: false))).Outcomes["mast-off"].Applied, "An application while the switch holds the object above off applies");
        above.World.Controller(above.Outer1, true);
        Assert(above.Outer1.Active && !above.Mast1.Active, "The switch turning the object above on does not turn the disabled mast on");
        var follows = new Fixture();
        follows.World.Controller(follows.Outer1, false);
        Assert(follows.Mast1.Active && !follows.Outer1.Active, "Without an explicit disable the mast keeps its own activity, so it shows with the object above as the switch sets it");
        follows.World.Controller(follows.Outer1, true);
        Assert(follows.Outer1.Active && follows.Mast1.Active, "The switch shows it again");
        // A track-group follower: the pattern refuses the mast before any change.
        var group = new Fixture(); var (_, editor, session) = Session(group);
        var problem = ExistingEquipmentPattern.MastProblem(new MastFacts { Lamps = { new LampFacts { Name = "Signal 1", Key = "lights" } }, FollowsTrackGroup = true }, new[] { "lights", "gates" });
        group.World.PartRefused = "This mast cannot be edited: " + problem;
        var refused = group.Engine.Apply(Doc(Mast("mast-off", active: false))).Outcomes["mast-off"];
        Assert(!refused.Applied && refused.Failure == FailureKind.Refused && refused.Error.Contains("track group") && group.World.Mutations == 0 && group.Mast1.Active, "The disable of a track-group follower is refused before any change: " + refused.Failure + " " + refused.Error);
        var before = session.ScenePatchFileText;
        Assert(!session.Edit(MastId(), null, false).Applied && session.ScenePatchFileText == before && !editor.CanUndo && group.World.Mutations == 0, "Scene patch editing refuses it too and leaves the file and history unchanged");
        group.World.Controller(group.Mast1, false);
        Assert(!group.Mast1.Active, "Its track group keeps switching it off");
        group.World.Controller(group.Mast1, true);
        Assert(group.Mast1.Active, "and on");
    }

    static void RefusalsLeaveSceneAndFileUnchanged() {
        // A target that does not resolve to one object, or names another component, fails before mutation.
        var f = new Fixture();
        f.World.PartsAt[Crossing + "/Twin/Twin"] = new List<object> { f.Controller.Add(new FakePart { Id = "Twin#8", Offset = new[] { 2.0, 0, 2 } }), f.Controller.Add(new FakePart { Id = "Twin#9", Offset = new[] { 2.0, 0, 3 } }) };
        var result = f.Engine.Apply(Doc(Mast("missing", Crossing + "/Signal 3/Signal 3", Near1), Mast("twin", Crossing + "/Twin/Twin", Near1),
            Mod("wrong-kind", Mast1Path, ExistingEquipmentScene.BellComponent, ExistingEquipmentScene.MastStructure, Near1), Mast("other", Mast2Path, new[] { 1108.0, 10, 16 })));
        Assert(!result.Outcomes["missing"].Applied && !result.Outcomes["twin"].Applied && !result.Outcomes["wrong-kind"].Applied, "A missing, ambiguous or mismatched target identity fails that modification");
        Assert(result.Outcomes["other"].Applied && f.World.Mutations == 1, "The other modification still applies and the failed ones mutate nothing: " + f.World.Mutations);
        // Equipment that does not conform to its pattern is refused before mutation; scene, file and history stay.
        var g = new Fixture(); var (_, editor, session) = Session(g); g.World.PartRefused = "its lamp observes key 'power', which the crossing controller does not publish";
        var before = session.ScenePatchFileText;
        var refused = session.Edit(MastId(), new Placement(Near1, Identity), null);
        Assert(!refused.Applied && refused.Error.Contains("does not publish") && session.ScenePatchFileText == before && g.World.Mutations == 0 && !editor.CanUndo, "A nonconforming mast is refused before mutation: " + refused.Error);
        Assert(!session.Edit(BellId, null, false).Applied && g.World.Mutations == 0, "So is a nonconforming bell's Enabled");
    }

    static void FailureAfterMutationRestoresEquipmentAndCulling() {
        // A disabled mast shows nowhere and needs no sphere, so the missing culling refresh is injected for an enabled one.
        var cases = new (Action<FakeGroupWorld> inject, string name, bool? active)[] { (w => w.ThrowAfterPartPlacement = true, "a throw after the placement", false), (w => w.MoveSibling = true, "another object moved", false), (w => w.SkipCullingRefresh = true, "a culling refresh that did not happen", null) };
        foreach (var (inject, name, active) in cases) {
            var f = new Fixture(); inject(f.World);
            var outcome = f.Engine.Apply(Doc(Mast("far-mast", position: Far, rotation: Quarter, active: active))).Outcomes["far-mast"];
            Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal), name + ": a failure after mutation is restored: " + outcome.Error);
            Assert(Near(f.MapOf(f.Mast1).Position, new[] { 1094.0, 10, 24 }) && SameTurn(f.MapOf(f.Mast1).Rotation, Identity) && f.Mast1.Active && Near(f.MapOf(f.Gate1).Position, new[] { 1094.0, 13, 24 }), name + ": placement and activity are as captured: " + Where(f));
            Assert(f.World.Native.Registered.Count == 0 && f.Culler.Lists.SequenceEqual(new[] { f.Outer1, f.Outer2 }) && f.Culler.Radius == 30, name + ": the culling membership and registration are as before");
        }
    }

    static void IncompleteCleanupBlocksTheRetry() {
        var f = new Fixture(); f.World.MoveSibling = f.World.KeepSiblingMoved = true;
        var outcome = f.Engine.Apply(Doc(Mast("move", position: Near1))).Outcomes["move"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        f.World.KeepSiblingMoved = false; var mutations = f.World.Mutations;
        var retry = f.Engine.Apply(Doc(Mast("again", position: new[] { 1092.0, 10, 26 }))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && f.World.Mutations == mutations, "A retry on the affected mast is blocked until cleanup is verified or the world reloads: " + retry.Error);
    }

    static void HistorySaveReloadAndRemoval() {
        var f = new Fixture(); var (store, mapEditor, session) = Session(f);
        Assert(session.Edit(MastId(), new Placement(Far, Quarter), null).Applied && session.Edit(MastId(), null, false).Applied && session.Edit(BellId, new Placement(new[] { 1103.0, 12, 21 }, Identity), null).Applied, "A mast move, a mast disable and a bell move apply");
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Persistence(true, all, "After three edits");
        mapEditor.Undo(); Assert(Near(f.MapOf(f.Bell).Position, new[] { 1100.0, 10, 20 }), "Undo returns the bell");
        mapEditor.Undo(); Assert(f.Mast1.Active && Near(f.MapOf(f.Mast1).Position, Far), "Undo re-enables the mast and leaves its placement");
        Assert(f.World.Native.Registered.Any(r => r.Owner == f.Mast1), "Enabled again, the far mast has its own culling sphere");
        mapEditor.Undo(); Assert(Near(f.MapOf(f.Mast1).Position, new[] { 1094.0, 10, 24 }) && f.World.Native.Registered.Count == 0 && f.Culler.Lists.SequenceEqual(new[] { f.Outer1, f.Outer2 }), "Undo of the move returns the mast and its culling membership");
        Persistence(false, new string[0], "After Undo back to the unedited crossing");
        mapEditor.Redo(); mapEditor.Redo(); mapEditor.Redo();
        Assert(Near(f.MapOf(f.Mast1).Position, Far) && SameTurn(f.MapOf(f.Mast1).Rotation, Quarter) && !f.Mast1.Active && Near(f.MapOf(f.Bell).Position, new[] { 1103.0, 12, 21 }), "Redo reapplies each operation: " + Where(f));
        Persistence(true, all, "After Redo");
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=" + ExistingEquipmentScene.RequiredVersion), "Save writes the file and the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        var item = (JObject)JObject.Parse(store.Patch)["modifications"].Children<JProperty>().Single(p => (string)p.Value["target"]["path"] == Mast1Path).Value;
        Assert((string)item["target"]["structure"] == ExistingEquipmentScene.MastStructure && (string)item["target"]["component"] == ExistingEquipmentScene.MastComponent && (string)item["placement"]["mode"] == "fixed" && (bool)item["active"] == false
            && item["equipment"] == null && item["coverage"] == null && item["trackMarker"] == null, "The file records fixed physical placement and activity, and no logical binding: " + item);
        // A world reload reproduces placement, rotation and activity from the file.
        var fresh = new Fixture();
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Values.All(o => o.Applied) && Near(fresh.MapOf(fresh.Mast1).Position, Far) && SameTurn(fresh.MapOf(fresh.Mast1).Rotation, Quarter) && !fresh.Mast1.Active && Near(fresh.MapOf(fresh.Bell).Position, new[] { 1103.0, 12, 21 })
            && fresh.Controller.Parts.Count == 6, "A reloaded world reproduces the edits without duplicated state: " + Where(fresh));
        // Removing the activity leaves the placement; removing the modification returns the mast to its place in the object above it.
        Assert(session.Edit(MastId(), null, true).Applied && f.Mast1.Active && Near(f.MapOf(f.Mast1).Position, Far), "Enabling again leaves the placement");
        var removed = session.Remove(MastId());
        Assert(removed.Applied && Near(f.MapOf(f.Mast1).Position, new[] { 1094.0, 10, 24 }) && SameTurn(f.MapOf(f.Mast1).Rotation, Identity) && f.Mast1.Active && Near(f.MapOf(f.Gate1).Position, new[] { 1094.0, 13, 24 }), "Remove modification returns the mast to the placement the scene gives it: " + removed.Error + " " + Where(f));
        Assert(f.World.Native.Registered.Count == 0 && f.Culler.Lists.SequenceEqual(new[] { f.Outer1, f.Outer2 }), "Removal returns the culling membership and registration");
        var bare = new Store { Patch = Doc(Mast("move", position: Near1)) }; var bareEditor = new NativeMapEditor();
        var without = new PatchEditingSession(new PatchEngine(new Scene { Maps = new[] { "map" } }), bareEditor, bare); bareEditor.Session = without;
        Assert(without.MinimumPatcherVersion == ExistingEquipmentScene.RequiredVersion, "A mast modification requires the capability's version whether or not this build offers it: " + without.MinimumPatcherVersion);
    }

    static void RepeatedApplicationAccumulatesNothing() {
        var f = new Fixture();
        var doc = Doc(Mast("far-mast", position: Far, rotation: Quarter), Bell("move-bell", new[] { 1103.0, 12, 21 }), Mast("off", Mast2Path, active: false));
        f.Engine.Apply(doc); var first = Where(f); var spheres = f.World.Native.Registered.Count; var listed = f.Culler.Lists.Count;
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && Where(f) == first && f.World.Native.Registered.Count == spheres && f.Culler.Lists.Count == listed && f.World.Culling.HandedOver(f.Culler).Count == 1,
            "Repeated application adds no offset and no second sphere or hand-over: " + first + " / " + Where(f));
        f.World.Origin = 1010;
        Assert(new PatchEngine(f.Scene).Apply(doc).Outcomes.Values.All(o => o.Applied) && Near(f.MapOf(f.Mast1).Position, Far), "A load at another origin reaches the same map placement: " + Where(f));
    }

    // A new load attempt over a kept world reconciles the moved mast, bell and disabled mast without another sphere or
    // hand-over. After a leave and a join the game has rebuilt the crossing: the old equipment is released without a
    // cleanup failure, and the rebuilt equipment gets each modification once.
    static void RejoinAppliesOnceToTheRebuiltCrossing() {
        var f = new Fixture();
        var doc = Doc(Mast("far-mast", position: Far, rotation: Quarter), Bell("move-bell", new[] { 1103.0, 12, 21 }), Mast("off", Mast2Path, active: false));
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied), "The equipment modifications apply");
        var first = Where(f); var spheres = f.World.Native.Registered.Count; var listed = f.Culler.Lists.Count;
        var kept = f.Engine.Reenter();
        Assert(kept.Retained == 3 && kept.Released == 0 && kept.Failures.Count == 0, "A kept crossing keeps its equipment through a new load attempt: retained " + kept.Retained);
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && Where(f) == first && f.World.Native.Registered.Count == spheres && f.Culler.Lists.Count == listed && f.World.Culling.HandedOver(f.Culler).Count == 1,
            "Reconciling kept equipment adds no offset, sphere or hand-over: " + Where(f));
        var old = f.Mast1;
        f.Rejoin();
        var rebuilt = f.Engine.Reenter();
        Assert(rebuilt.Released == 3 && rebuilt.Retained == 0 && rebuilt.Failures.Count == 0, "The equipment the game destroyed is released without a cleanup failure: " + String.Join("; ", rebuilt.Failures));
        Assert(!ReferenceEquals(f.Mast1, old) && f.World.Native.Registered.Count == 0 && Near(f.MapOf(f.Mast1).Position, new[] { 1094.0, 10, 24 }) && f.Mast2.Active, "The rebuilt crossing starts as the scene has it, with no hold or sphere left from the old one: " + Where(f));
        var again = f.Engine.Apply(doc);
        Assert(again.Outcomes.Values.All(o => o.Applied) && Where(f) == first && f.World.Native.Registered.Count == spheres && f.Culler.Lists.Count == listed && f.World.Culling.HandedOver(f.Culler).Count == 1 && !f.Mast2.Active,
            "The rebuilt equipment gets each modification once: " + String.Join("; ", again.Outcomes.Values.Select(o => o.Error)) + " " + Where(f));
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && Where(f) == first && f.World.Native.Registered.Count == spheres, "A repeated callback after the rejoin adds nothing");
    }

    // The mast pattern is judged from mechanisms: the lamps on the light object and the gates at and below it observe
    // keys their crossing controller publishes, and nothing in the mast would be left stale by a move or a switch.
    static void PatternsNameWhyEquipmentDoesNotConform() {
        var keys = new[] { "lights", "gates" };
        MastFacts Mast() => new MastFacts { Lamps = { new LampFacts { Name = "Signal 1", Key = "lights", Targets = 1, Materials = 1, RendererUsesMaterial = true } }, Gates = { new GateFacts { Name = "Wishbone", Key = "gates", Animator = true, Clip = true } } };
        Assert(ExistingEquipmentPattern.MastProblem(Mast(), keys) == null, "A light object with a gate on its child conforms");
        var lightOnly = Mast(); lightOnly.Gates.Clear();
        Assert(ExistingEquipmentPattern.MastProblem(lightOnly, keys) == null, "A mast without a gate conforms");
        var cases = new (Action<MastFacts> change, string says)[] {
            (m => m.Lamps.Clear(), "does not stand on a light object"), (m => m.Lamps[0].Key = "power", "'power', which the crossing controller does not publish"),
            (m => m.Gates[0].Key = "door", "gate Wishbone observes key 'door'"), (m => m.BelowController = false, "no crossing controller drives it"),
            (m => m.KeyValueObjects = 1, "its own key-value object"), (m => m.Cullers = 1, "its own culler"), (m => m.Spans = 1, "a detection span"), (m => m.Controllers = 1, "a crossing controller"),
            (m => m.Bells = 1, "a bell player"), (m => m.LampsBelow = 1, "another light object below it"), (m => m.MissingScript = true, "script in its hierarchy is missing"),
            (m => m.SharedPath = 3, "3 objects share this hierarchy path"), (m => m.AddedByPatcher = true, "the patcher added this equipment"), (m => m.FollowsTrackGroup = true, "track group"),
            (m => m.Lamps[0].OutsideTargets = 1, "outside the mast"), (m => m.Rigidbodies = 1, "rigidbody"),
            (m => m.GatesBeside = 1, "beside this light object") };
        foreach (var (change, says) in cases) {
            var mast = Mast(); change(mast);
            var problem = ExistingEquipmentPattern.MastProblem(mast, keys);
            Assert(problem != null && problem.Contains(says), "A nonconforming mast names its concrete reason (" + says + "): " + problem);
        }
        Assert(ExistingEquipmentPattern.BellProblem(new ExistingBellFacts()) == null, "The bell its crossing controller created conforms");
        Assert(ExistingEquipmentPattern.BellProblem(new ExistingBellFacts { ControllersOwn = false }).Contains("not the one its crossing controller created")
            && ExistingEquipmentPattern.BellProblem(new ExistingBellFacts { ControllerActive = false }).Contains("controller is disabled")
            && ExistingEquipmentPattern.BellProblem(new ExistingBellFacts { SharedPath = 2 }).Contains("2 objects share"), "A nonconforming bell names its concrete reason");
    }

    static SceneObservation Entry(string path, string component, string family, string operations = "x", string inspectionOnly = null, string holder = null, string displayName = null, string structure = null) =>
        new SceneObservation(new object(), new TargetIdentity(SceneName, path, component, structure), family, true, new double[] { 1, 2, 3 }, Identity, new string[0], new string[0], new string[0], operations,
            inspectionOnly: inspectionOnly, displayName: displayName, holderPath: holder);

    // A detection span is a span that a crossing controller holds in a circuit; it names that controller.
    static void DetectionSpansNameTheirController() {
        var controller = Entry(Crossing, CrossingCoverageScene.ComponentType, "Grade crossing controller", structure: CrossingCoverageScene.Structure);
        var mast = Entry(Mast1Path, ExistingEquipmentScene.MastComponent, ExistingEquipmentScene.MastFamily, CommandBand.MastOperations, displayName: "Signal 1 at Bryson Everett", structure: ExistingEquipmentScene.MastStructure);
        var bell = Entry(BellPath, ExistingEquipmentScene.BellComponent, ExistingEquipmentScene.BellFamily, CommandBand.BellOperations, structure: ExistingEquipmentScene.BellStructure);
        var island = Entry(Crossing + "/Track 1 Island", "Track.TrackSpan", "Track span", CommandBand.DetectionSpanOperations, holder: Crossing);
        var east = Entry(Crossing + "/Track 1 East", "Track.TrackSpan", "Track span", CommandBand.DetectionSpanOperations, holder: Crossing);
        // Whether a crossing controller holds the span decides: a detection span is a supported kind, any other span is not.
        Assert(CommandBand.SpanInspectionOnly(true) == null && CommandBand.SpanInspectionOnly(false) == CommandBand.InspectionOnly("Track span", false) && CommandBand.SpanInspectionOnly(false) != null, "The span's holder decides whether it is inspection only");
        var loose = Entry("World/Loose Span", "Track.TrackSpan", "Track span", "x", CommandBand.SpanInspectionOnly(false));
        var rows = new[] { island, mast, loose, controller, east, bell };
        var inventory = new SceneInventory(new Scene { Maps = new[] { "map" }, Observations = () => rows }); inventory.Refresh();
        Assert(ReferenceEquals(InventoryPresentationState.HolderOf(inventory, island), controller) && InventoryPresentationState.HolderOf(inventory, mast) == null && InventoryPresentationState.HeldBy(island) == "Detection span of: Grade Crossing - Bryson Everett" && InventoryPresentationState.HeldBy(mast) == null,
            "A detection span names its controller, which Select controller inspects");
    }

    // Every operation stays visible: one that applies offers itself, one that does not says why.
    static void InspectorNamesWhatAppliesAndWhy() {
        Assert(CommandBand.MastOperations.Contains("gate") && CommandBand.MastOperations.Contains("crossing controller") && CommandBand.BellOperations.Contains("bell"), "Supported edits name the mast with its gate and the bell");
        Assert(CommandBand.Refusal(CommandBand.DetectionSpanOperations) != null && CommandBand.DetectionSpanOperations.Contains("track locations") && CommandBand.DetectionSpanOperations.Contains("Connections"), "A detection span says that its position is its track locations and where coverage is edited: " + CommandBand.DetectionSpanOperations);
        Assert(CommandBand.EditReason(CommandBand.DetectionSpanOperations) == "Edit it on its crossing controller", "That reason is what its disabled placement shows: " + CommandBand.EditReason(CommandBand.DetectionSpanOperations));
        // Track-following placement is not offered for existing equipment; the other modes are.
        foreach (var mode in new[] { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode })
            Assert(SupportedEdits.ModeBlock(SupportedEdits.Of(EditEntries.Of(ExistingEquipmentScene.MastFamily, ExistingEquipmentScene.MastStructure, CommandBand.MastOperations)), mode, true) == null, "Mode " + mode + " applies to a mast");
        Assert(SupportedEdits.ModeBlock(SupportedEdits.Of(EditEntries.Of(ExistingEquipmentScene.MastFamily, ExistingEquipmentScene.MastStructure, CommandBand.MastOperations)), CommandBand.FollowMode, true) == "Uses fixed placement", "Follow says why it does not apply");
        Assert(CommandBand.Tooltip("Keep the object's placement relative to a chosen track or endpoint.", CommandBand.EquipmentNoFollow) == "Uses fixed placement.", "A disabled mode's tooltip gives the reason");
        NonconformingEquipmentHasItsOperationsDisabled();
        // A bell's sound has no direction: its rotation stays visible and disabled with that reason; a mast's applies.
        Assert(CommandBand.RotationBlock(ExistingEquipmentScene.BellStructure) == CommandBand.BellNoRotation && CommandBand.BellNoRotation.Contains("no direction") && CommandBand.RotationBlock(ExistingEquipmentScene.MastStructure) == null && CommandBand.RotationBlock(null) == null,
            "Rotation does not apply to a bell and applies to a mast");
        Assert(CommandBand.EquipmentGuide[1] == "moving-crossing-equipment.md#refusal-reasons" && CommandBand.EquipmentGuide[0].Length != 0, "The refusal links to the operation guidance");
        TrackBindingWithoutBindingShowsItsReason();
    }

    // Connections holds Track binding for every object, and an unsupported operation stays visible with its reason.
    // The section's decision runs on inventory entries as the inventory builds them: an object that holds no marker
    // and reports no current binding, such as a detection span, a crossing controller, a mast, a bell or a board,
    // shows its reason as one line instead of empty location fields and a current binding of None. An object of a
    // family that holds a marker keeps its fields, editable or disabled with its own reason, and so does any object
    // that reports a current binding. A detection span is told apart by its holder, not by a message text.
    static void TrackBindingWithoutBindingShowsItsReason() {
        SceneObservation Row(string path, string family, string holder = null, string operations = "x", params string[] bindings) =>
            new SceneObservation(new object(), new TargetIdentity(SceneName, path, "C", null), family, true, new double[] { 1, 2, 3 }, Identity, bindings, new string[0], new string[0], operations, holderPath: holder);
        var span = Row(Crossing + "/Track 1 East", "Track span", Crossing, "x", "Span ID: Pcyi", "Lower segment: S8rj", "Upper segment: Sx88");
        var looseSpan = Row("World/Loose Span", "Track span");
        var controller = Row(Crossing, "Grade crossing controller");
        var mast = Row(Mast1Path, ExistingEquipmentScene.MastFamily, operations: CommandBand.MastOperations);
        var bell = Row(BellPath, ExistingEquipmentScene.BellFamily, operations: CommandBand.BellOperations);
        var board = Row("World/Board", "Board");
        foreach (var row in new[] { span, looseSpan, controller, mast, bell, board })
            Assert(!SupportedEdits.HoldsBinding(row.Family) && CommandBand.CurrentBindings(row).Length == 0, "A " + row.Family + " without a marker has no Track binding section");
        // The span's own track locations are observed bindings, not a current binding.
        Assert(CommandBand.CurrentBindings(span).Length == 0, "A detection span's track locations stay out of Current binding");
        var refusedMarker = Row("World/Marker", "Generic track marker", operations: CommandBand.NotEditable("type Siding"));
        var refusedPad = Row("World/Pad", "Crossing pad marker", operations: CommandBand.NotEditable("refused"));
        var refusedMilepost = Row("World/Milepost", "Milepost", operations: CommandBand.NotEditable("refused"));
        foreach (var row in new[] { refusedMarker, refusedPad, refusedMilepost })
            Assert(SupportedEdits.HoldsBinding(row.Family) && SupportedEdits.ReasonOf(SupportedEdits.Of(row), SupportedEdits.Binding) == CommandBand.EditReason(row.Operations),
                "A refused " + row.Family + " keeps its binding fields, disabled with its own reason");
        var carrier = Row("World/Carrier", "Station", bindings: new[] { "Marker segment: Abc", "Other: x" });
        var named = Row("World/Named", "Station", bindings: new[] { CommandBand.ListedTrackLocation + " Abc a 1.0" });
        Assert(!SupportedEdits.HoldsBinding(carrier.Family) && CommandBand.CurrentBindings(carrier).Length == 1 && !SupportedEdits.HoldsBinding(named.Family),
            "Only a track marker, milepost or crossing pad has a Track binding section, and only marker lines are a current binding");
        Assert(CommandBand.NoBindingReason(span) == "Edit it on its crossing controller", "A detection span says where its tracks are set: " + CommandBand.NoBindingReason(span));
        // A span whose operations text differs still gets the span reason; a span no controller holds does not.
        Assert(CommandBand.NoBindingReason(Row(span.Identity.Path, "Track span", Crossing, "changed")) == CommandBand.DetectionSpanNoBinding, "The holder, not the message text, decides");
        foreach (var row in new[] { looseSpan, controller, mast, bell, board, null })
            Assert(CommandBand.NoBindingReason(row) == CommandBand.NoTrackBinding, "Another object without a binding names the families whose binding the patcher edits");
        Assert(CommandBand.NoTrackBinding == "Only markers, mileposts and pads have one", "The reason names the families with editable track bindings");
        Assert(CommandBand.Sentence(CommandBand.DetectionSpanNoBinding) == "Edit it on its crossing controller." && CommandBand.Sentence(CommandBand.NoTrackBinding) == "Only markers, mileposts and pads have one.", "Each reason forms a plain sentence");
        Assert(CommandBand.HoldsMarker(StarterPlacements.Family) && !CommandBand.HoldsMarker("Track span") && !CommandBand.HoldsMarker(null), "The marker families are one list");
    }

    // A mast or bell that does not conform to its pattern gets no structure and its refusal under Supported edits, so the
    // inspector cannot edit it: every placement mode, Numeric included, the rotation fields and Enabled are disabled with
    // the concrete reason, and the reason links to the operation guidance.
    static void NonconformingEquipmentHasItsOperationsDisabled() {
        var problem = ExistingEquipmentPattern.MastProblem(new MastFacts { Lamps = { new LampFacts { Name = "Signal 1", Key = "power", Targets = 1, Materials = 1, RendererUsesMaterial = true } } }, new[] { "lights", "gates" });
        var bellProblem = ExistingEquipmentPattern.BellProblem(new ExistingBellFacts { ControllerActive = false });
        var mast = Entry(Mast1Path, ExistingEquipmentScene.MastComponent, ExistingEquipmentScene.MastFamily, CommandBand.NotEditable(problem), displayName: "Signal 1 at Bryson Everett");
        var bell = Entry(BellPath, ExistingEquipmentScene.BellComponent, ExistingEquipmentScene.BellFamily, CommandBand.NotEditable(bellProblem));
        var conforming = Entry(Mast2Path, ExistingEquipmentScene.MastComponent, ExistingEquipmentScene.MastFamily, CommandBand.MastOperations, structure: ExistingEquipmentScene.MastStructure);
        foreach (var (row, reason) in new[] { (mast, problem), (bell, bellProblem) }) {
            var operations = row.Operations;
            Assert(!CommandBand.EditableStructure(row.Identity.Structure) && CommandBand.Refusal(operations) == reason, "The entry cannot be edited and states its refusal under Supported edits: " + operations);
            var shown = CommandBand.EditReason(operations);
            Assert(shown == (row == mast ? "Its structure isn't supported" : "Its crossing controller is disabled"), "The disabled controls give the short reason for the refusal: " + shown);
            var equipment = CommandBand.RefusedEquipment(row.Family, operations);
            Assert(equipment, "The entry is a refused mast or bell, which links to the guide's refusal reasons");
            var edits = SupportedEdits.Of(row);
            foreach (var mode in new[] { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode, CommandBand.FollowMode })
                Assert(SupportedEdits.ModeBlock(edits, mode, false) == shown, "Placement mode " + mode + " is disabled with the reason: " + SupportedEdits.ModeBlock(edits, mode, false));
            Assert(CommandBand.Tooltip("Enter a position in map coordinates and a rotation in degrees.", shown) == shown + ".", "Numeric's tooltip gives the reason: " + CommandBand.Tooltip("Enter a position in map coordinates and a rotation in degrees.", shown));
            // The rotation fields and Enabled take the inspector's edit block, which for an object that cannot be edited is this reason.
            Assert(SupportedEdits.ReasonOf(edits, SupportedEdits.Move) == shown && CommandBand.InspectorEditBlock(false, operations) == shown, "Rotation and Enabled are disabled with the same reason: " + CommandBand.InspectorEditBlock(false, operations));
        }
        Assert(!CommandBand.RefusedEquipment(conforming.Family, conforming.Operations) && SupportedEdits.ModeBlock(SupportedEdits.Of(conforming), CommandBand.NumericMode, true) == null && CommandBand.InspectorEditBlock(true, conforming.Operations) == null,
            "A conforming mast keeps Numeric, rotation and Enabled");
        var added = CommandBand.AddedEquipmentOperations("Added light at Bryson Everett");
        Assert(!CommandBand.RefusedEquipment(ExistingEquipmentScene.MastFamily, added), "Added equipment is no refused mast: its crossing's Connections tab edits it");
    }
}
