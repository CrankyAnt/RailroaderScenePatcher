// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Objects the game switches on and off itself, outside track-group activation: a progression controller sets a
// station's building group and station map icon from the station's progression state, and a map feature sets the
// objects it unlocks from whether it is unlocked. Controlled worlds follow the Railroader 0.6.3 scene: the Cochran
// controller on World/Large Scenery/Cochran lists the Cochran flagstopstation group and the Cochran station map icon,
// and the signals map feature lists the track markers Sign Begin CTC Andrews and Sign Begin CTC Sylva. A controlled
// switch writes the object's activity, or the stand-in that receives its writes while the patcher shares them.
static class GameSwitchTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Game switches: " + message); }
    const string SceneName = "level5", Large = "World/Large Scenery/";
    const string Cochran = Large + "Cochran/Cochran flagstopstation", Alarka = Large + "Alarka/Alarka Depot", Almond = Large + "Almond/Almond Depot";
    const string Icons = StationMapIconRules.IconsPath + "/", CochranIcon = Icons + "Cochran Station", AlarkaIcon = Icons + "Alarka Station";
    const string Sylva = "World/Signs/Sign Begin CTC Sylva", Andrews = "World/Signs/Sign Begin CTC Andrews";

    public static void Run() {
        HoldWarningNamesTheSwitch();
        DisableHoldsThroughProgression();
        WithoutDisableTheGroupFollowsProgression();
        RemovingTheDisableReturnsToTheCurrentSwitch();
        UndoRedoSaveAndReloadOfAStationGroup();
        StationMapIconHoldsThroughProgression();
        TrackMarkerHoldsThroughTheMapFeature();
        FollowerHoldsAgainstGroupAndMapFeature();
        DisableThatCannotHoldIsRefused();
        FailedDiversionLeavesNothingBehind();
        FailureRestoresTheDiversion();
        Console.WriteLine("PASS: game switches");
    }

    // The warning names the switch the disable holds against; a construction controller keeps its own warning, and an
    // object no switch sets or no explicit disable holds gets none.
    static void HoldWarningNamesTheSwitch() {
        Assert(GameSwitchActivity.HoldWarning(GameSwitch.None) == null, "No switch, no warning");
        Assert(GameSwitchActivity.HoldWarning(GameSwitch.Construction) == GameSwitchActivity.ConstructionHoldWarning, "A construction controller keeps its warning");
        Assert(GameSwitchActivity.HoldWarning(GameSwitch.Progression) == "Your explicit disable keeps this object off in the game even when progression switches it on. Click Enable or Clear activity to let progression control it again.",
            "The progression warning: " + GameSwitchActivity.HoldWarning(GameSwitch.Progression));
        Assert(GameSwitchActivity.HoldWarning(GameSwitch.MapFeature) == "Your explicit disable keeps this object off in the game even when unlocking a map feature switches it on. Click Enable or Clear activity to let the map feature control it again.",
            "The map feature warning: " + GameSwitchActivity.HoldWarning(GameSwitch.MapFeature));
        Assert(GameSwitchActivity.HoldWarning(GameSwitch.Progression | GameSwitch.MapFeature) == GameSwitchActivity.ProgressionOrMapFeatureHoldWarning
            && GameSwitchActivity.ProgressionOrMapFeatureHoldWarning.Contains("progression or a map feature"), "Both switches: " + GameSwitchActivity.ProgressionOrMapFeatureHoldWarning);
        Assert(RailroaderScenePatcher.CommandBand.ConstructionHoldWarning == GameSwitchActivity.ConstructionHoldWarning, "The footer and Object tab read the same construction warning");
    }

    // Three station building groups: Cochran and Alarka, which their progression controllers switch, and Almond, which
    // no switch sets.
    sealed class Stations {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeGroup Cochran = Station(new[] { 4200.0, 600, 1500 }, true), Alarka = Station(new[] { 4800.0, 610, 1700 }, true), Almond = Station(new[] { -7000.0, 530, 600 }, false);
        public readonly PatchEngine Engine;
        static FakeGroup Station(double[] position, bool switched) =>
            new FakeGroup { Position = position, Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null, Constructed = switched, Switch = GameSwitch.Progression };
        public Stations() {
            World.Objects[GameSwitchTests.Cochran] = new List<object> { Cochran }; World.Objects[GameSwitchTests.Alarka] = new List<object> { Alarka }; World.Objects[GameSwitchTests.Almond] = new List<object> { Almond };
            // A roof part below the Cochran group root, as a station building has parts.
            var roof = Cochran.Add(new FakePart { Id = "Roof#1", Offset = new[] { 0.0, 4, 0 } });
            World.PartsAt[GameSwitchTests.Cochran + "/Roof"] = new List<object> { roof };
            World.Mutations = 0;
            var holds = new PartHolds();
            var scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new BakedSceneryGroups(World, holds: holds)); scene.Add(new BakedSceneryParts(World, holds));
            Engine = new PatchEngine(scene);
        }
        // The progression controller of the station sets its group, as an IndustriesDidChange event makes it do.
        public void Progression(FakeGroup station, bool enabled) => World.Controller(station, enabled);
        public GameSwitch Warned(FakeGroup station) => World.Constructions.HeldAgainst(station);
        public bool Reads(FakeGroup station) => World.Observe(station).Root.Active == true;
    }
    static string Mod(string id, string path, bool? active, string component = BakedSceneryGroups.ComponentType, string structure = BakedSceneryGroups.Structure) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"" + component + "\",\"structure\":\"" + structure + "\"}" +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static TargetIdentity GroupId(string path) => new TargetIdentity(SceneName, path, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);

    // An explicitly disabled station group stays inactive when its progression controller switches it on, also after a
    // repeated application; the warning names progression. An object no switch sets holds its disable natively and gets
    // no warning.
    static void DisableHoldsThroughProgression() {
        var f = new Stations();
        var doc = Doc(Mod("hide-cochran", Cochran, false), Mod("hide-almond", Almond, false));
        var result = f.Engine.Apply(doc);
        Assert(result.Outcomes.Values.All(o => o.Applied), "The disables apply: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        Assert(!f.Cochran.Active && !f.Reads(f.Cochran) && f.Warned(f.Cochran) == GameSwitch.Progression, "The disabled station group is off, reads as disabled and is held against progression");
        Assert(GameSwitchActivity.HoldWarning(f.Warned(f.Cochran)) == GameSwitchActivity.ProgressionHoldWarning, "Its warning names progression");
        f.Progression(f.Cochran, true);
        Assert(!f.Cochran.Active, "Progression switching the station on does not switch on the disabled group");
        f.Progression(f.Cochran, false); f.Progression(f.Cochran, true);
        Assert(!f.Cochran.Active, "Repeated progression switches do not switch it on");
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && !f.Cochran.Active && f.Warned(f.Cochran) == GameSwitch.Progression, "A repeated application keeps the hold");
        f.Progression(f.Cochran, true);
        Assert(!f.Cochran.Active, "Progression after the repeat still does not switch it on");
        Assert(!f.Almond.Active && f.Warned(f.Almond) == GameSwitch.None, "An object no switch sets is disabled without a warning");
        Assert(f.Warned(f.Alarka) == GameSwitch.None, "An unedited switched object has no warning");
    }

    // Without an explicit disable, the game's switch sets the group on and off as native, and the patcher does not
    // divert its writes.
    static void WithoutDisableTheGroupFollowsProgression() {
        var f = new Stations();
        Assert(f.Engine.Apply(Doc(Mod("hide-cochran", Cochran, false))).Outcomes["hide-cochran"].Applied, "The disable of another station applies");
        f.Progression(f.Alarka, false);
        Assert(!f.Alarka.Active && !f.World.Diverted(f.Alarka), "The unedited station follows progression off");
        f.Progression(f.Alarka, true);
        Assert(f.Alarka.Active && f.Warned(f.Alarka) == GameSwitch.None, "The unedited station follows progression on, without a warning");
        // A move of a switched station leaves its activity to the switch.
        var moved = f.Engine.Apply(Doc(Mod("hide-cochran", Cochran, false), "\"move-alarka\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + Alarka + "\",\"component\":\"" + BakedSceneryGroups.ComponentType + "\",\"structure\":\"" + BakedSceneryGroups.Structure + "\"},\"placement\":{\"mode\":\"fixed\",\"position\":[5800,610,1700],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}"));
        Assert(moved.Outcomes.Values.All(o => o.Applied), "A move of the switched station applies: " + String.Join("; ", moved.Outcomes.Values.Select(o => o.Error)));
        f.Progression(f.Alarka, false);
        Assert(!f.Alarka.Active && !f.World.Diverted(f.Alarka), "The moved station still follows progression off");
        f.Progression(f.Alarka, true);
        Assert(f.Alarka.Active, "The moved station still follows progression on");
    }

    // Enable, Clear activity and Remove modification return the object to what the switch gives it now: on when
    // progression shows the station, off when it hides it; either way it reads as enabled and its writes go back to the
    // switch.
    static void RemovingTheDisableReturnsToTheCurrentSwitch() {
        foreach (var shows in new[] { true, false }) {
            foreach (var way in new[] { "enable", "clear", "remove" }) {
                var f = new Stations(); var editor = new NativeMapEditor(); var session = new PatchEditingSession(f.Engine, editor, new Store()); editor.Session = session;
                Assert(session.Edit(GroupId(Cochran), null, false).Applied && !f.Cochran.Active, way + ": the disable applies");
                f.Progression(f.Cochran, shows);
                var outcome = way == "enable" ? session.Edit(GroupId(Cochran), null, true) : way == "clear" ? session.ClearActivity(GroupId(Cochran)) : session.Remove(GroupId(Cochran));
                Assert(outcome.Applied, way + " applies: " + outcome.Error);
                Assert(f.Cochran.Active == shows && f.Reads(f.Cochran) && !f.World.Diverted(f.Cochran) && f.Warned(f.Cochran) == GameSwitch.None,
                    way + " returns the station to the switch's current choice (" + shows + "): active=" + f.Cochran.Active + " diverted=" + f.World.Diverted(f.Cochran));
                f.Progression(f.Cochran, !shows);
                Assert(f.Cochran.Active == !shows, way + ": the switch sets the station again");
            }
        }
    }

    // Native Undo and Redo of the disable keep the object and its warning consistent with the switch's current choice.
    // Save writes the disable; a reloaded world holds it again through progression.
    static void UndoRedoSaveAndReloadOfAStationGroup() {
        var f = new Stations(); var store = new Store(); var editor = new NativeMapEditor(); var session = new PatchEditingSession(f.Engine, editor, store); editor.Session = session;
        Assert(session.Edit(GroupId(Cochran), null, false).Applied && !f.Cochran.Active && f.Warned(f.Cochran) == GameSwitch.Progression, "The disable applies with its warning");
        f.Progression(f.Cochran, false);
        editor.Undo();
        Assert(!f.Cochran.Active && f.Reads(f.Cochran) && f.Warned(f.Cochran) == GameSwitch.None && !f.World.Diverted(f.Cochran), "Undo while progression hides the station leaves it off, enabled and without a warning");
        f.Progression(f.Cochran, true);
        Assert(f.Cochran.Active, "After Undo progression switches the station on");
        editor.Redo();
        Assert(!f.Cochran.Active && f.Warned(f.Cochran) == GameSwitch.Progression, "Redo holds it off again with the warning");
        f.Progression(f.Cochran, false); f.Progression(f.Cochran, true);
        Assert(!f.Cochran.Active, "After Redo progression does not switch it on");
        editor.Undo();
        Assert(f.Cochran.Active && f.Warned(f.Cochran) == GameSwitch.None, "Undo while progression shows the station switches it on without a warning");
        editor.Redo();
        Assert(editor.Save() == null, "Save succeeds");
        var saved = JObject.Parse(store.Patch);
        Assert((bool?)saved["modifications"].Children<JProperty>().Single().Value["active"] == false, "The scene patch file holds the explicit disable: " + store.Patch);
        var fresh = new Stations();
        fresh.Progression(fresh.Cochran, false);
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Values.All(o => o.Applied) && !fresh.Cochran.Active && fresh.Warned(fresh.Cochran) == GameSwitch.Progression, "The reloaded world applies the disable with its warning");
        fresh.Progression(fresh.Cochran, true);
        Assert(!fresh.Cochran.Active, "Progression after the reload does not switch on the disabled station");
        fresh.Progression(fresh.Alarka, false); fresh.Progression(fresh.Alarka, true);
        Assert(fresh.Alarka.Active, "The unedited control station follows progression after the reload");
    }

    // A controlled set of switches over objects of another capability: each switch's own object list, a progression
    // controller's List or a map feature's array, which the switch walks to set every object it names, as
    // UpdateObjectStates and UpdateFeatureForUnlocked do. While the patcher shares an object's activity, SwitchSlots
    // points the list entries at a stand-in, which reports each change of its activity.
    sealed class StandIn { public bool Active; public Action<bool> Changed; }
    sealed class SwitchHost : IGameSwitchHost {
        public readonly List<KeyValuePair<IList, GameSwitch>> Lists = new List<KeyValuePair<IList, GameSwitch>>();
        sealed class Diversion { internal StandIn StandIn; internal IReadOnlyList<KeyValuePair<IList, int>> Slots; internal GameSwitch Kinds; }
        readonly Dictionary<object, Diversion> diversions = new Dictionary<object, Diversion>();
        readonly Func<object, bool> active; readonly Action<object, bool> set;
        public readonly GameSwitchActivity Activity;
        public SwitchHost(Func<object, bool> active, Action<object, bool> set) { this.active = active; this.set = set; Activity = new GameSwitchActivity(this, new FakeGroupWorld().Culling); }
        // A progression controller's list or a map feature's array that names the objects.
        public IList Name(GameSwitch kind, params object[] nodes) {
            IList list = kind == GameSwitch.MapFeature ? (IList)nodes.ToArray() : new List<object>(nodes);
            Lists.Add(new KeyValuePair<IList, GameSwitch>(list, kind));
            return list;
        }
        public void Name(object node, GameSwitch kind) => Name(kind, node);
        public GameSwitch Switches(object node) {
            var kinds = diversions.TryGetValue(node, out var diversion) ? diversion.Kinds : GameSwitch.None;
            foreach (var list in Lists) if (list.Key.Cast<object>().Any(item => ReferenceEquals(item, node))) kinds |= list.Value;
            return kinds;
        }
        public bool Active(object node) => active(node);
        public void Divert(object node, bool choice, Action<bool> report) {
            var standIn = new StandIn { Active = choice };
            var slots = SwitchSlots.Divert(Lists, node, standIn, out var kinds);
            standIn.Changed = report;
            diversions[node] = new Diversion { StandIn = standIn, Slots = slots, Kinds = kinds };
        }
        public void Restore(object node) {
            if (!diversions.TryGetValue(node, out var diversion)) return;
            diversions.Remove(node);
            SwitchSlots.Restore(diversion.Slots, node, diversion.StandIn);
        }
        public void SetActive(object node, bool value) => set(node, value);
        public bool Alive(object node) => node != null;
        public bool Diverted(object node) => diversions.ContainsKey(node);
        // Whether a list entry still names the object itself.
        public bool Listed(object node) => Lists.Any(list => list.Key.Cast<object>().Any(item => ReferenceEquals(item, node)));
        // A switch sets every object of its list: a stand-in takes the value and reports a change, any other object is set.
        public void Write(IList list, bool value) {
            foreach (var item in list.Cast<object>().ToArray()) {
                if (item is StandIn standIn) { if (standIn.Active != value) { standIn.Active = value; standIn.Changed(value); } }
                else set(item, value);
            }
        }
        // Each switch that names the object, or its stand-in, sets its list.
        public void Write(object node, bool value) {
            var standIn = diversions.TryGetValue(node, out var diversion) ? diversion.StandIn : null;
            foreach (var list in Lists.Where(l => l.Key.Cast<object>().Any(item => ReferenceEquals(item, node) || (standIn != null && ReferenceEquals(item, standIn)))).ToArray()) Write(list.Key, value);
        }
    }

    // A station map icon as the capability reads it, with its activity shared with the progression controller that
    // lists it, routed as MapIconWorld routes it: the icon registers with the map while it is active and leaves it while
    // it is not. DisableRefused gives the reason MapIconWorld refuses a disable with.
    sealed class SwitchedIcon { public double[] Position = { 0, 0, 0 }; public bool Active = true; }
    sealed class IconWorld : IMapIconWorld, IDisableRefusal {
        public readonly Dictionary<string, List<object>> Objects = new Dictionary<string, List<object>>();
        public readonly HashSet<object> Registered = new HashSet<object>();
        public readonly SwitchHost Switches;
        public string DisableRefused;
        public int Mutations;
        public IconWorld() { Switches = new SwitchHost(node => ((SwitchedIcon)node).Active, Own); }
        void Own(object node, bool active) { var icon = (SwitchedIcon)node; icon.Active = active; if (active) Registered.Add(icon); else Registered.Remove(icon); }
        public IEnumerable<string> MapIds => new[] { "map" };
        public IReadOnlyList<object> Find(string scene, string path) => Objects.TryGetValue(path, out var found) ? found : new List<object>();
        public string Refusal(object icon) => null;
        public string DisableRefusal(object icon) => DisableRefused;
        public MapIconObservation Observe(object target) {
            var icon = (SwitchedIcon)target;
            return new MapIconObservation(new ObjectState(new Placement((double[])icon.Position.Clone(), new double[] { 0, 0, 0, 1 }), Switches.Activity.ReadActivity(icon, icon.Active, false)), icon.Active, "Station Icons", Registered.Contains(icon));
        }
        public void SetPlacement(object target, Placement placement) { Mutations++; ((SwitchedIcon)target).Position = (double[])placement.Position.Clone(); }
        public void SetActive(object target, bool active) { Mutations++; if (!Switches.Activity.SetActivity(target, active, false)) Own(target, active); }
    }
    static TargetIdentity IconId(string path) => new TargetIdentity(SceneName, path, StationMapIconRules.Component, StationMapIconRules.Structure);
    static IconWorld IconFixture(out SwitchedIcon cochran, out SwitchedIcon alarka, out PatchEngine engine) {
        var world = new IconWorld();
        cochran = new SwitchedIcon { Position = new[] { 4200.0, 0, 1500 } }; alarka = new SwitchedIcon { Position = new[] { 4800.0, 0, 1700 } };
        world.Objects[CochranIcon] = new List<object> { cochran }; world.Objects[AlarkaIcon] = new List<object> { alarka };
        world.Registered.Add(cochran); world.Registered.Add(alarka);
        // The Cochran controller lists its icon in a List; the Alarka controller too.
        world.Switches.Name(GameSwitch.Progression, cochran); world.Switches.Name(GameSwitch.Progression, alarka);
        var scene = new SceneCapabilities(() => world.MapIds, null); scene.Add(new StationMapIconScene(world));
        engine = new PatchEngine(scene);
        return world;
    }

    // The Cochran station map icon, explicitly disabled, stays off the map through progression, Undo and Redo keep it
    // consistent with progression's current choice, and a reloaded world holds it again; the unedited Alarka icon
    // follows progression. While the disable holds, the controller's list names the stand-in, and it names the icon
    // again once the disable ends.
    static void StationMapIconHoldsThroughProgression() {
        var w = IconFixture(out var icon, out var control, out var e);
        var store = new Store(); var editor = new NativeMapEditor(); var session = new PatchEditingSession(e, editor, store); editor.Session = session;
        var outcome = session.Edit(IconId(CochranIcon), null, false);
        Assert(outcome.Applied && !icon.Active && !w.Registered.Contains(icon) && w.Switches.Activity.HeldAgainst(icon) == GameSwitch.Progression, "The icon's disable applies with its warning: " + outcome.Error);
        Assert(!w.Switches.Listed(icon) && w.Switches.Diverted(icon), "While the disable holds, the controller's list names the stand-in instead of the icon");
        w.Switches.Write(icon, true);
        Assert(!icon.Active && !w.Registered.Contains(icon), "Progression does not put the disabled icon back on the map");
        w.Switches.Write(control, false);
        Assert(!control.Active && !w.Registered.Contains(control), "The unedited icon follows progression off the map");
        w.Switches.Write(control, true);
        Assert(control.Active && w.Registered.Contains(control) && w.Switches.Activity.HeldAgainst(control) == GameSwitch.None, "The unedited icon follows progression onto the map, without a warning");
        w.Switches.Write(icon, false);
        editor.Undo();
        Assert(!icon.Active && w.Switches.Activity.HeldAgainst(icon) == GameSwitch.None && !w.Switches.Diverted(icon) && w.Switches.Listed(icon), "Undo while progression hides the icon leaves it off, without a warning, and the list names the icon again");
        w.Switches.Write(icon, true);
        Assert(icon.Active && w.Registered.Contains(icon), "After Undo progression puts the icon on the map");
        editor.Redo();
        Assert(!icon.Active && !w.Registered.Contains(icon) && w.Switches.Activity.HeldAgainst(icon) == GameSwitch.Progression, "Redo holds the icon off the map with the warning");
        Assert(editor.Save() == null, "Save succeeds");
        var fresh = IconFixture(out var reloaded, out var freshControl, out var freshEngine);
        Assert(freshEngine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && !reloaded.Active, "The reloaded world applies the icon's disable");
        fresh.Switches.Write(reloaded, false); fresh.Switches.Write(reloaded, true);
        Assert(!reloaded.Active && !fresh.Registered.Contains(reloaded), "Progression after the reload does not put the disabled icon on the map");
        outcome = new PatchEditingSession(freshEngine, new NativeMapEditor(), new Store { Patch = store.Patch, Info = store.Info }).Remove(IconId(CochranIcon));
        Assert(outcome.Applied && reloaded.Active && fresh.Registered.Contains(reloaded) && !fresh.Switches.Diverted(reloaded) && fresh.Switches.Listed(reloaded), "Remove modification returns the icon to progression's current choice: " + outcome.Error);
    }

    // A marker world whose markers a map feature may switch, routed as TrackMarkerWorld routes them: a marker that
    // follows no track group reads and sets the shared activity; a follower reads its own activity, and its hold also
    // holds it against the switch. DisableRefused gives the reason TrackMarkerWorld refuses a disable with.
    sealed class SwitchedMarkerWorld : FakeMarkerWorld, ITrackMarkerWorld {
        public readonly SwitchHost Switches;
        public SwitchedMarkerWorld() { Switches = new SwitchHost(node => base.Active(node), (node, value) => base.SetActive(node, value)); }
        bool Follower(object target) => GroupActivity(target) != null;
        public new bool Active(object target) => Switches.Activity.ReadActivity(target, base.Active(target), Follower(target));
        public new void SetActive(object target, bool active) { if (!Switches.Activity.SetActivity(target, active, Follower(target))) base.SetActive(target, active); }
        public new void HoldDisabled(object target, bool hold) { base.HoldDisabled(target, hold); Switches.Activity.HoldFollower(target, hold); }
    }
    static TargetIdentity MarkerId(string path) => new TargetIdentity(SceneName, path, "Track.TrackMarker", TrackMarkerScene.Structure);
    static SwitchedMarkerWorld MarkerFixture(out FakeMarker sylva, out FakeMarker andrews, out IList signals, out PatchEngine engine, bool followers = false) {
        var world = new SwitchedMarkerWorld();
        // Before the unlock the feature keeps both signs off.
        sylva = new FakeMarker { Id = "Mctcs", Segment = "S9sc", End = "a", Distance = 40, Active = false };
        andrews = new FakeMarker { Id = "Mctca", Segment = "Sjdl", End = "b", Distance = 20, Active = false };
        world.Objects[Sylva] = new List<object> { sylva }; world.Objects[Andrews] = new List<object> { andrews };
        world.Relative(sylva); world.Relative(andrews);
        if (followers) { world.LoadFollower(sylva); world.LoadFollower(andrews); world.SetActive(sylva, false); world.SetActive(andrews, false); world.Mutations = 0; }
        else { world.Load(sylva); world.Load(andrews); }
        // The signals feature names both signs in its array.
        signals = world.Switches.Name(GameSwitch.MapFeature, sylva, andrews);
        var scene = new SceneCapabilities(() => world.MapIds, null); scene.Add(new TrackMarkerScene(world, Capability.Unchecked));
        engine = new PatchEngine(scene);
        return world;
    }

    // Sign Begin CTC Sylva, explicitly disabled, stays inactive and unregistered through the unlock of the signals map
    // feature; the unedited Sign Begin CTC Andrews follows the unlock. Undo, Redo and a reload keep the hold consistent
    // with the feature's current state.
    static void TrackMarkerHoldsThroughTheMapFeature() {
        var w = MarkerFixture(out var sign, out var control, out var signals, out var e);
        var store = new Store(); var editor = new NativeMapEditor(); var session = new PatchEditingSession(e, editor, store); editor.Session = session;
        var outcome = session.Edit(MarkerId(Sylva), null, false);
        Assert(outcome.Applied && !sign.Active && w.Switches.Activity.HeldAgainst(sign) == GameSwitch.MapFeature, "The sign's disable applies with the map feature warning: " + outcome.Error);
        Assert(GameSwitchActivity.HoldWarning(w.Switches.Activity.HeldAgainst(sign)) == GameSwitchActivity.MapFeatureHoldWarning, "Its warning names the map feature");
        Assert(ReferenceEquals(signals[1], control) && signals[0] is StandIn, "The feature's array names the stand-in for the disabled sign and the control itself");
        // The signals feature unlocks.
        w.Switches.Write(signals, true);
        Assert(!sign.Active && w.Registrations(sign).Count == 0, "The unlock does not switch on or register the disabled sign");
        Assert(control.Active && w.Registrations(control).Count == 1 && w.Switches.Activity.HeldAgainst(control) == GameSwitch.None, "The unedited sign follows the unlock and registers, without a warning");
        editor.Undo();
        Assert(sign.Active && w.Registrations(sign).Count == 1 && w.Switches.Activity.HeldAgainst(sign) == GameSwitch.None && ReferenceEquals(signals[0], sign), "Undo after the unlock switches the sign on as the feature sets it, without a warning");
        editor.Redo();
        Assert(!sign.Active && w.Registrations(sign).Count == 0 && w.Switches.Activity.HeldAgainst(sign) == GameSwitch.MapFeature, "Redo holds the sign off with the warning");
        w.Switches.Write(signals, false); w.Switches.Write(signals, true);
        Assert(!sign.Active, "A later unlock does not switch it on");
        Assert(editor.Save() == null, "Save succeeds");
        var fresh = MarkerFixture(out var reloaded, out var freshControl, out var freshSignals, out var freshEngine);
        Assert(freshEngine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && !reloaded.Active, "The reloaded world applies the sign's disable");
        fresh.Switches.Write(freshSignals, true);
        Assert(!reloaded.Active && fresh.Registrations(reloaded).Count == 0 && freshControl.Active, "After the reload the unlock leaves the disabled sign off and switches on the control");
        var cleared = new PatchEditingSession(freshEngine, new NativeMapEditor(), new Store { Patch = store.Patch, Info = store.Info }).ClearActivity(MarkerId(Sylva));
        Assert(cleared.Applied && reloaded.Active && fresh.Registrations(reloaded).Count == 1 && !fresh.Switches.Diverted(reloaded), "Clear activity returns the sign to the unlocked feature: " + cleared.Error);
    }

    // A sign whose activity follows its track group and which the signals feature also switches: its disable installs
    // the group hold and holds it against the feature, so neither the group nor the unlock switches it on. Its warning
    // names Clear activity alone, since Enable is refused for a follower. Clear activity releases both: the sign follows
    // the group and the feature again.
    static void FollowerHoldsAgainstGroupAndMapFeature() {
        var w = MarkerFixture(out var sign, out var control, out var signals, out var e, followers: true);
        w.Switches.Write(signals, true);
        Assert(sign.Active && control.Active, "Before the edit the unlocked feature and the enabled group show both followers");
        var editor = new NativeMapEditor(); var session = new PatchEditingSession(e, editor, new Store()); editor.Session = session;
        var outcome = session.Edit(MarkerId(Sylva), null, false);
        Assert(outcome.Applied && !sign.Active && sign.Held && w.Switches.Activity.HeldAgainst(sign) == GameSwitch.MapFeature, "The follower's disable installs the group hold and holds against the feature: " + outcome.Error);
        Assert(GameSwitchActivity.HoldWarning(w.Switches.Activity.HeldAgainst(sign), clearOnly: true) == GameSwitchActivity.MapFeatureHoldWarning.Replace("Click Enable or Clear activity", "Click Clear activity")
            && !GameSwitchActivity.HoldWarning(GameSwitch.MapFeature, true).Contains("Enable"), "A follower's warning names only Clear activity: " + GameSwitchActivity.HoldWarning(GameSwitch.MapFeature, true));
        w.Switches.Write(signals, false); w.Switches.Write(signals, true);
        Assert(!sign.Active && w.Registrations(sign).Count == 0, "The unlock does not switch on the held follower");
        w.SetGroupEnabled("ela", false); w.SetGroupEnabled("ela", true);
        Assert(!sign.Active, "Its track group does not switch it on either");
        var enabled = session.Edit(MarkerId(Sylva), null, true);
        Assert(!enabled.Applied && !sign.Active, "Enable of the follower stays refused: " + enabled.Error);
        var cleared = session.ClearActivity(MarkerId(Sylva));
        Assert(cleared.Applied && !sign.Held && !w.Switches.Diverted(sign) && sign.Active && w.Registrations(sign).Count == 1 && ReferenceEquals(signals[0], sign), "Clear activity releases both holds: " + cleared.Error);
        w.Switches.Write(signals, false);
        Assert(!sign.Active, "The feature switches the released follower off again");
        editor.Undo();
        Assert(!sign.Active && sign.Held && w.Switches.Diverted(sign), "Undo of Clear activity holds the follower again");
        editor.Redo();
        Assert(!sign.Held && !w.Switches.Diverted(sign), "Redo releases it again");
    }

    // Every capability world that can disable an object the game switches carries the disable refusal, so the production
    // adapters (TrackMarkerWorld and its milepost, crossing pad and starter subclasses, MapIconWorld, GroupWorld) must
    // implement it: these conversions compile only while the world interfaces extend IDisableRefusal.
    static IDisableRefusal Refusing(ITrackMarkerWorld world) => world;
    static IDisableRefusal Refusing(IMapIconWorld world) => world;
    static IDisableRefusal Refusing(IGroupWorld world) => world;

    // A disable that cannot hold is refused with the world's reason before anything changes; Enable stays possible. A
    // marker world refuses through the world interface itself, without declaring the refusal on its own: a plain
    // FakeMarkerWorld, as the production TrackMarkerWorld, takes the check in the track-marker relationship.
    static void DisableThatCannotHoldIsRefused() {
        Assert(Refusing((ITrackMarkerWorld)new FakeMarkerWorld { DisableRefused = "r" }).DisableRefusal(null) == "r", "A track marker world refuses through its interface");
        var plain = new FakeMarkerWorld();
        var plainSign = new FakeMarker { Id = "Mctcs", Segment = "S9sc", End = "a", Distance = 40 };
        plain.Objects[Sylva] = new List<object> { plainSign }; plain.Load(plainSign); plain.Relative(plainSign);
        plain.DisableRefused = GameSwitchActivity.UnsharedRefusal;
        var plainScene = new SceneCapabilities(() => plain.MapIds, null); plainScene.Add(new TrackMarkerScene(plain, Capability.Unchecked));
        var plainOutcome = new PatchEngine(plainScene).Apply(Doc(Mod("hide-sign", Sylva, false, "Track.TrackMarker", TrackMarkerScene.Structure))).Outcomes["hide-sign"];
        Assert(!plainOutcome.Applied && plainOutcome.Error.Contains(GameSwitchActivity.UnsharedRefusal) && plain.Mutations == 0 && plainSign.Active && plain.Registrations(plainSign).Count == 1,
            "A plain marker world's refusal stops the disable before any change: " + plainOutcome.Error);
        var icons = IconFixture(out var icon, out _, out var iconEngine);
        icons.DisableRefused = GameSwitchActivity.UnsharedRefusal;
        var outcome = iconEngine.Apply(Doc(Mod("hide-icon", CochranIcon, false, StationMapIconRules.Component, StationMapIconRules.Structure))).Outcomes["hide-icon"];
        Assert(!outcome.Applied && outcome.Error.Contains(GameSwitchActivity.UnsharedRefusal) && icons.Mutations == 0 && icon.Active && !icons.Switches.Diverted(icon), "The icon's disable is refused before mutation: " + outcome.Error);
        Assert(iconEngine.Apply(Doc(Mod("show-icon", CochranIcon, true, StationMapIconRules.Component, StationMapIconRules.Structure))).Outcomes["show-icon"].Applied, "Enable of the icon still applies");

        var markers = MarkerFixture(out var sign, out _, out _, out var markerEngine);
        markers.DisableRefused = GameSwitchActivity.MapFeatureMembersRefusal;
        outcome = markerEngine.Apply(Doc(Mod("hide-sign", Sylva, false, "Track.TrackMarker", TrackMarkerScene.Structure))).Outcomes["hide-sign"];
        Assert(!outcome.Applied && outcome.Error.Contains(GameSwitchActivity.MapFeatureMembersRefusal) && markers.Mutations == 0 && !markers.Switches.Diverted(sign), "The sign's disable is refused before mutation: " + outcome.Error);

        var f = new Stations(); f.World.DisableRefused = GameSwitchActivity.ProgressionMembersRefusal;
        outcome = f.Engine.Apply(Doc(Mod("hide-cochran", Cochran, false))).Outcomes["hide-cochran"];
        Assert(!outcome.Applied && outcome.Error.Contains(GameSwitchActivity.ProgressionMembersRefusal) && f.World.Mutations == 0 && f.Cochran.Active && !f.World.Diverted(f.Cochran), "The station group's disable is refused before mutation: " + outcome.Error);
        var editor = new NativeMapEditor(); var store = new Store(); var session = new PatchEditingSession(f.Engine, editor, store); editor.Session = session;
        Assert(!session.Edit(GroupId(Cochran), null, false).Applied && store.Patch == null && !editor.IsDirty, "In scene patch editing the refused disable writes nothing");
    }

    // A list that fails to take an entry at one index, as a native list can.
    sealed class FailingList : ArrayList {
        public int FailAt = -1;
        public FailingList(params object[] items) : base(items) { }
        public override object this[int index] { get => base[index]; set { if (index == FailAt) throw new InvalidOperationException("list write injected"); base[index] = value; } }
    }

    // A diversion that fails part way puts back the entries it already changed and leaves no entry the patcher would
    // have to find; the object stays with its switches, and a later disable diverts it whole.
    static void FailedDiversionLeavesNothingBehind() {
        var node = new object(); var standIn = new object();
        var list = new List<object> { node, "other" }; var array = new object[] { "x", node }; var failing = new FailingList(node) { FailAt = 0 };
        var lists = new[] { new KeyValuePair<IList, GameSwitch>(list, GameSwitch.Progression), new KeyValuePair<IList, GameSwitch>(array, GameSwitch.MapFeature), new KeyValuePair<IList, GameSwitch>(failing, GameSwitch.MapFeature) };
        var thrown = false;
        try { SwitchSlots.Divert(lists, node, standIn, out _); } catch (InvalidOperationException) { thrown = true; }
        Assert(thrown && ReferenceEquals(list[0], node) && ReferenceEquals(array[1], node) && ReferenceEquals(failing[0], node), "A failed diversion puts every entry back");
        failing.FailAt = -1;
        var slots = SwitchSlots.Divert(lists, node, standIn, out var kinds);
        Assert(slots.Count == 3 && kinds == (GameSwitch.Progression | GameSwitch.MapFeature) && ReferenceEquals(list[0], standIn) && ReferenceEquals(array[1], standIn), "A diversion points the List and array entries at the stand-in");
        Assert(SwitchSlots.Restore(slots, node, standIn) && ReferenceEquals(list[0], node) && ReferenceEquals(array[1], node) && ReferenceEquals(failing[0], node), "Restore puts every entry back");

        // Through the shared activity: the failed disable leaves no entry diverted, no hold and the icon with its switch.
        var w = IconFixture(out var icon, out _, out var e);
        var broken = new FailingList(icon) { FailAt = 0 };
        w.Switches.Lists.Add(new KeyValuePair<IList, GameSwitch>(broken, GameSwitch.MapFeature));
        var outcome = e.Apply(Doc(Mod("hide-icon", CochranIcon, false, StationMapIconRules.Component, StationMapIconRules.Structure))).Outcomes["hide-icon"];
        Assert(!outcome.Applied && !w.Switches.Diverted(icon) && !w.Switches.Activity.Held(icon) && w.Switches.Activity.Diverted.Count == 0 && w.Switches.Listed(icon) && icon.Active,
            "A diversion that fails leaves no hold and no diverted entry: " + outcome.Error);
        w.Switches.Write(icon, false);
        Assert(!icon.Active, "The switch still sets the icon");
        broken.FailAt = -1;
        // The engine keeps a failed operation failed for this load; a new load's engine applies it again.
        var next = new SceneCapabilities(() => w.MapIds, null); next.Add(new StationMapIconScene(w));
        var later = new PatchEngine(next).Apply(Doc(Mod("hide-icon", CochranIcon, false, StationMapIconRules.Component, StationMapIconRules.Structure))).Outcomes["hide-icon"];
        Assert(later.Applied && w.Switches.Diverted(icon) && !w.Switches.Listed(icon), "A later disable diverts every entry: " + later.Error);
    }

    // A failure after mutation on a station group that progression switches restores the diversion as captured: the
    // group goes back to its switch. An incomplete cleanup is reported and blocks the retry without a mutation.
    static void FailureRestoresTheDiversion() {
        var f = new Stations(); f.World.ThrowAfterPlacement = true;
        var outcome = f.Engine.Apply(Doc(MoveAndHide("hide-move", Cochran))).Outcomes["hide-move"];
        Assert(!outcome.Applied && f.Cochran.Active && !f.World.Diverted(f.Cochran) && f.Warned(f.Cochran) == GameSwitch.None, "A failed disable is restored and the group is back with its switch: " + outcome.Error);
        f.Progression(f.Cochran, false);
        Assert(!f.Cochran.Active, "Progression sets the restored group again");

        var held = new Stations(); held.Engine.Apply(Doc(Mod("hide-cochran", Cochran, false)));
        held.World.ThrowAfterPlacement = true;
        outcome = held.Engine.Apply(Doc(MoveAndShow("show-move", Cochran))).Outcomes["show-move"];
        held.Progression(held.Cochran, true);
        Assert(!outcome.Applied && !held.Cochran.Active && held.World.Diverted(held.Cochran) && held.Warned(held.Cochran) == GameSwitch.Progression, "A failed enable is restored with the hold: " + outcome.Error);

        var stuck = new Stations(); stuck.World.LeavePartBehind = stuck.World.KeepPartBehind = true;
        outcome = stuck.Engine.Apply(Doc(MoveAndHide("hide-move", Cochran))).Outcomes["hide-move"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        Assert(stuck.Cochran.Active == stuck.Reads(stuck.Cochran) && stuck.Warned(stuck.Cochran) == GameSwitch.None, "The captured activity came back: the group is not held off");
        stuck.World.LeavePartBehind = stuck.World.KeepPartBehind = false; var mutations = stuck.World.Mutations;
        var retry = stuck.Engine.Apply(Doc(Mod("again", Cochran, false))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && stuck.World.Mutations == mutations && !stuck.World.Diverted(stuck.Cochran), "The retry on the affected group is blocked without a mutation: " + retry.Error);
    }
    static string MoveAndHide(string id, string path) => "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"" + BakedSceneryGroups.ComponentType + "\",\"structure\":\"" + BakedSceneryGroups.Structure + "\"},\"placement\":{\"mode\":\"fixed\",\"position\":[4300,600,1500],\"rotation\":[0,0,0,1]},\"active\":false,\"dependsOn\":[]}";
    static string MoveAndShow(string id, string path) => MoveAndHide(id, path).Replace("\"active\":false", "\"active\":true");
}
