// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Scene markers that hold a duplicate marker ID. The game removes a marker's graph registration by marker ID from every
// holder, so an activity, binding or track-following edit of one holder is refused before it changes anything.
static class DuplicateMarkerIdTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    const string Scene = "BushnellWhittier";
    const string SpeedPath = "World/Large Scenery/Signs/Speed - Fontana South Curve (3)", YardPath = "World/Large Scenery/Signs/Yard Limit";
    static readonly string[] WhistlePaths = { "World/Roads/Grade Crossing - A/Whistle Sign W", "World/Roads/Grade Crossing - B/Whistle Sign W", "World/Roads/Grade Crossing - C/Whistle Sign W" };
    static TargetIdentity Marker(string path) => new TargetIdentity(Scene, path, "Track.TrackMarker", TrackMarkerScene.Structure);

    // A pair: the active speed sign holds the registration of Mg6m; the inactive Yard Limit holds the same marker ID. A
    // group: three active whistle signs hold Mkft, each registered on its own segment. An unrelated marker has a marker ID
    // of its own.
    sealed class World {
        public FakeMarkerWorld Markers = new FakeMarkerWorld();
        public FakeMarker Speed, Yard, Unrelated;
        public FakeMarker[] Whistles;
        public TrackScene Tracks = new TrackScene();
        public Store Store = new Store();
        public NativeMapEditor MapEditor = new NativeMapEditor();
        public PatchEditingSession Session;
        public SceneCapabilities Capabilities;
        public World(FakeMarkerWorld markers = null) {
            Markers = markers ?? Markers;
            foreach (var segment in new[] { "Sa", "Sb", "Sc", "pi3pzp" }) { Markers.Segments[segment] = new List<double> { 50 }; Markers.Groups[segment] = null; }
            Speed = new FakeMarker { Id = "Mg6m", Segment = "Sjdl", End = "a", Distance = 10 };
            Yard = new FakeMarker { Id = "Mg6m", Segment = "S9sc", End = "b", Distance = 20, Active = false, Position = new double[] { 40, 1, 2 } };
            Unrelated = new FakeMarker { Id = "Mown", Segment = "Nnew", End = "a", Distance = 5, Position = new double[] { 60, 1, 2 } };
            Whistles = new[] { "Sa", "Sb", "Sc" }.Select((s, i) => new FakeMarker { Id = "Mkft", Segment = s, End = "a", Distance = 5 + i, Position = new double[] { 80 + i, 1, 2 } }).ToArray();
            Add(SpeedPath, Speed); Add(YardPath, Yard); Add("World/Large Scenery/Signs/Unrelated", Unrelated);
            for (var i = 0; i < Whistles.Length; i++) Add(WhistlePaths[i], Whistles[i]);
            Tracks.Poses["node:N1"] = new TrackPose(new[] { 1050.0, 1, 2 }, new[] { 0.0, 0, 0, 1 });
            Capabilities = new SceneCapabilities(() => Markers.MapIds) { Tracks = Tracks };
            Capabilities.Add(new TrackMarkerScene(Markers, Capability.Unchecked));
            Session = new PatchEditingSession(new PatchEngine(Capabilities), MapEditor, Store, "map"); MapEditor.Session = Session;
            Markers.Mutations = 0;
        }
        void Add(string path, FakeMarker m) { Markers.Objects[path] = new List<object> { m }; Markers.Load(m); Markers.Relative(m); }
        // Every holder's registration and state, compared before and after an attempt.
        public string Registrations() => String.Join(" ", new[] { Speed, Yard, Unrelated }.Concat(Whistles).Select(m =>
            m.Id + ":" + m.Active + ":" + m.Segment + ":" + String.Join(",", Markers.Registrations(m).Select(r => r.Bucket + "|" + r.Location.End + "|" + r.Location.Distance + (r.HoldsMarker ? "" : "|foreign")))));
    }

    public static void Run() {
        EnablingAnInactiveHolderIsRefusedWithTheExplanation();
        RebindingAHolderIsRefusedWithTheExplanation();
        GroupHoldersRefuseActivityBindingAndFollowing();
        AnotherHoldersRegistrationNeverCountsAsThisMarkers();
        PlacementOfAHolderKeepsEveryRegistration();
        InspectorNamesTheIdEveryHolderAndEachRegistrationOwner();
        LoadFailsChangingModificationsOfHoldersAndKeepsTheRest();
        PadHoldersRefuseActivityAndBindingAtEditAndLoad();
        RefusalNamesTheEditedAndActiveHoldersFirst();
        GroupFollowerHolderRefusesItsExplicitDisable();
        PadHolderRefusesEnableAndTrackFollowing();
        RegistrationOwnerOutsideTheLoadedMarkersIsAHolder();
        OneObjectWithTwoMarkersIsOneHolder();
        StaleDependentCacheOfAHolderIsRefusedBeforeMutation();
        WriteRefusesARestoreThatWouldReRegisterAHolder();
        Console.WriteLine("PASS: duplicate marker IDs are named and their unsafe edits are refused before mutation");
    }
    static void AssertRefused(World w, Outcome outcome, string before, string scenePatchFile, string scenario, params string[] holders) {
        Assert(!outcome.Applied, scenario + " is refused");
        Assert(holders.All(outcome.Error.Contains), scenario + ": the refusal names the marker ID and every holder: " + outcome.Error);
        AssertNoReloadAdvice(outcome.Error, scenario);
        Assert(w.Registrations() == before && w.Markers.Mutations == 0 && w.Session.ScenePatchFileText == scenePatchFile, scenario + ": the scene and the scene patch file are unchanged and every holder keeps its registration: " + w.Registrations());
    }
    // The refusal says that reloading does not help, and never advises it.
    static void AssertNoReloadAdvice(string error, string scenario) {
        Assert(error.Contains("Reloading or choosing another track doesn't change the duplicate marker ID.") && error.IndexOf("Reload before", StringComparison.OrdinalIgnoreCase) < 0
            && error.IndexOf("reload the", StringComparison.OrdinalIgnoreCase) < 0, scenario + ": the refusal explains that reloading does not help and gives no Reload advice: " + error);
        Assert(!error.Contains(": "), scenario + ": the refusal is plain sentences without a label of its own: " + error);
    }
    static void RebindingAHolderIsRefusedWithTheExplanation() {
        var w = new World();
        var before = w.Registrations(); var scenePatchFile = w.Session.ScenePatchFileText;
        AssertRefused(w, w.Session.Bind(Marker(YardPath), MarkerBinding.At(new TrackLocation("pi3pzp", "a", 3))), before, scenePatchFile, "A rebind of the Yard Limit to pi3pzp", "Mg6m", SpeedPath, YardPath);
        AssertRefused(w, w.Session.Bind(Marker(YardPath), MarkerBinding.Detached), before, scenePatchFile, "A detach of the Yard Limit", "Mg6m", SpeedPath, YardPath);
        AssertRefused(w, w.Session.Bind(Marker(SpeedPath), MarkerBinding.At(new TrackLocation("Sjdl", "a", 12))), before, scenePatchFile, "A rebind of the active speed sign", "Mg6m", SpeedPath, YardPath);
        AssertRefused(w, w.Session.Edit(Marker(SpeedPath), null, false), before, scenePatchFile, "A disable of the active speed sign", "Mg6m", SpeedPath, YardPath);
        Assert(!w.MapEditor.CanUndo, "No refused edit adds an Undo step");
        var unrelated = w.Session.Bind(Marker("World/Large Scenery/Signs/Unrelated"), MarkerBinding.At(new TrackLocation("Nnew", "b", 7)));
        Assert(unrelated.Applied && w.Unrelated.End == "b", "A marker with an id of its own still takes a binding: " + unrelated.Error);
    }
    static void GroupHoldersRefuseActivityBindingAndFollowing() {
        var w = new World();
        var before = w.Registrations(); var scenePatchFile = w.Session.ScenePatchFileText;
        var all = new[] { "Mkft" }.Concat(WhistlePaths).ToArray();
        AssertRefused(w, w.Session.Edit(Marker(WhistlePaths[1]), null, false), before, scenePatchFile, "A disable of one of three whistle signs", all);
        AssertRefused(w, w.Session.Bind(Marker(WhistlePaths[0]), MarkerBinding.At(new TrackLocation("Sb", "a", 30))), before, scenePatchFile, "A rebind of one whistle sign", all);
        AssertRefused(w, w.Session.Follow(Marker(WhistlePaths[2]), TrackAnchor.Endpoint("N1")), before, scenePatchFile, "Track following of one whistle sign", all);
        var refusal = w.Session.Edit(Marker(WhistlePaths[1]), null, false).Error;
        Assert(!refusal.Contains(SpeedPath) && !refusal.Contains("Mg6m"), "The refusal names only the holders of its own marker ID: " + refusal);
    }
    // The inactive Yard Limit holds no registration; the speed sign's registration under the same id is not one of the
    // Yard Limit's, so the Yard Limit's registration agrees with its activity and an edit that leaves it alone applies.
    static void AnotherHoldersRegistrationNeverCountsAsThisMarkers() {
        var w = new World();
        var registrations = w.Markers.Registrations(w.Yard);
        Assert(registrations.Count == 1 && !registrations[0].HoldsMarker && ReferenceEquals(registrations[0].Owner, w.Speed), "The graph entry under Mg6m is owned by the speed sign");
        var moved = w.Session.Edit(Marker(YardPath), new Placement(new[] { 1045.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null);
        Assert(moved.Applied, "The Yard Limit's own registration is coherent, so its placement edit is not refused as a registration error: " + moved.Error);
    }
    static void PlacementOfAHolderKeepsEveryRegistration() {
        var w = new World();
        var before = w.Registrations();
        var moved = w.Session.Edit(Marker(WhistlePaths[0]), new Placement(new[] { 1090.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null);
        Assert(moved.Applied && w.Whistles[0].Position[0] == 90 && w.Registrations() == before, "A fixed placement of a holder applies and every holder keeps its registration: " + moved.Error + " " + w.Registrations());
        w.MapEditor.Undo();
        Assert(w.Whistles[0].Position[0] == 80 && w.Registrations() == before, "Undo of the placement keeps every registration");
        // A placement that fails after its mutation is restored without removing any holder's registration, in scene
        // patch editing and at load.
        var failing = new FailingPlacementWorld();
        var failed = new World(failing);
        var prior = failed.Registrations();
        failing.FailPlacement = true;
        var placed = failed.Session.Edit(Marker(WhistlePaths[1]), new Placement(new[] { 1041.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null);
        Assert(!placed.Applied && placed.Error.Contains("The object is back as it was") && failed.Whistles[1].Position[0] == 81 && failed.Registrations() == prior, "A failed placement edit of a holder is restored with every registration: " + placed.Error + " " + failed.Registrations());
        failing.FailPlacement = true;
        var load = new PatchEngine(failed.Capabilities).Apply(Doc(Item("move-yard", YardPath, "\"placement\":{\"mode\":\"fixed\",\"position\":[1041,1,2],\"rotation\":[0,0,0,1]}")));
        Assert(!load.Outcomes["move-yard"].Applied && load.Outcomes["move-yard"].Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal) && failed.Yard.Position[0] == 40 && failed.Registrations() == prior, "A failed placement at load is restored with every registration: " + load.Outcomes["move-yard"].Error + " " + failed.Registrations());
    }
    // A world whose next placement write fails after it moved the object.
    sealed class FailingPlacementWorld : FakeMarkerWorld, ITrackMarkerWorld {
        public bool FailPlacement;
        public new void SetPlacement(object target, double[] position, double[] rotation) {
            base.SetPlacement(target, position, rotation);
            if (FailPlacement) { FailPlacement = false; throw new InvalidOperationException("placement injected"); }
        }
    }
    static IReadOnlyList<string> Lines(World w, FakeMarker m) => w.Markers.IdHolders(m).With(w.Markers.Registrations(m)).Dependencies(m);
    static void InspectorNamesTheIdEveryHolderAndEachRegistrationOwner() {
        var w = new World();
        var pair = Lines(w, w.Yard);
        Assert(pair.Count == 3 && pair[0].StartsWith("Duplicate marker ID Mg6m is held by 2 scene markers. ", StringComparison.Ordinal), "The dependency view names the duplicate marker ID first: " + String.Join(" | ", pair));
        Assert(pair[1] == "This marker is inactive and has no registration under Mg6m." && pair[2] == SpeedPath + " is active and has the registration under Mg6m at Sjdl / a / 10.",
            "The inspected Yard Limit comes first, inactive without a registration; the speed sign is active and owns the registration: " + String.Join(" | ", pair));
        var group = Lines(w, w.Whistles[2]);
        Assert(group.Count == 4 && group[0].StartsWith("Duplicate marker ID Mkft is held by 3 scene markers. ", StringComparison.Ordinal), "The group of three is named: " + String.Join(" | ", group));
        Assert(group[1] == "This marker is active and has the registration under Mkft at Sc / a / 7." && group[2] == WhistlePaths[0] + " is active and has the registration under Mkft at Sa / a / 5."
            && group[3] == WhistlePaths[1] + " is active and has the registration under Mkft at Sb / a / 6.", "Each whistle sign owns its own registration: " + String.Join(" | ", group));
        Assert(group.All(line => !line.Contains(": ")), "Every line is a plain sentence without a label: " + String.Join(" | ", group));
        Assert(Lines(w, w.Unrelated).Count == 0, "A marker ID of its own adds no dependency lines");
        // An entry that a non-marker object holds under the marker ID is named as such.
        w.Markers.Buckets["Nnew"]["Mkft"] = ("b", 1, new object());
        var foreign = Lines(w, w.Whistles[0]);
        Assert(foreign.Last() == "An object that is not a scene marker has the registration under Mkft at Nnew / b / 1.", "A registration of another object is named: " + String.Join(" | ", foreign));
    }
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + items + "}}";
    static string Item(string key, string path, string body) => "\"" + key + "\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"}," + body + ",\"dependsOn\":[]}";
    sealed class Messages : IPatchDiagnostics {
        public readonly List<PatchFailure> Rows = new List<PatchFailure>();
        public void Warning(PatchFailure failure) { Rows.Add(failure); }
    }
    static void LoadFailsChangingModificationsOfHoldersAndKeepsTheRest() {
        var w = new World();
        var engine = new PatchEngine(w.Capabilities);
        var messages = new Messages();
        var runtime = new PatchRuntime(engine, messages, "0.5.0", "game-build");
        var before = w.Registrations();
        var json = Doc(String.Join(",",
            Item("enable-yard", YardPath, "\"active\":true"),
            Item("rebind-yard", YardPath, "\"relationships\":{\"trackMarker\":{\"segment\":\"pi3pzp\",\"end\":\"a\",\"distance\":3}}"),
            Item("disable-whistle", WhistlePaths[0], "\"active\":false"),
            Item("follow-whistle", WhistlePaths[1], "\"placement\":{\"mode\":\"trackFollowing\",\"track\":{\"node\":\"N1\"},\"offset\":[0,0,0],\"rotation\":[0,0,0,1]}"),
            Item("same-activity", WhistlePaths[2], "\"active\":true"),
            Item("move-speed", SpeedPath, "\"placement\":{\"mode\":\"fixed\",\"position\":[1030,1,2],\"rotation\":[0,0,0,1]}"),
            Item("rebind-unrelated", "World/Large Scenery/Signs/Unrelated", "\"relationships\":{\"trackMarker\":{\"segment\":\"Nnew\",\"end\":\"b\",\"distance\":9}}")));
        var result = runtime.Apply(json, new PatchSource("example/mod", "Example Mod", "1.0"));
        var outcomes = result.Outcomes;
        foreach (var key in new[] { "rebind-yard", "disable-whistle", "follow-whistle" }) {
            var holders = key.EndsWith("yard", StringComparison.Ordinal) ? new[] { "Mg6m", SpeedPath, YardPath } : new[] { "Mkft" }.Concat(WhistlePaths).ToArray();
            Assert(!outcomes[key].Applied && holders.All(outcomes[key].Error.Contains) && outcomes[key].Cause == FailureCause.InvalidDefinition, key + " fails before mutation naming the marker ID and every holder: " + outcomes[key].Error);
            var row = messages.Rows.SingleOrDefault(r => r.Operation == key);
            Assert(row != null && holders.All(row.Message.Contains), key + " has a diagnostic naming the marker ID and every holder");
        }
        // Enabling the Yard Limit fails the same way, whichever of the Yard Limit's modifications comes first.
        Assert(!outcomes["enable-yard"].Applied && outcomes["enable-yard"].Error.Contains("Mg6m"), "Enabling the Yard Limit at load fails: " + outcomes["enable-yard"].Error);
        Assert(outcomes["same-activity"].Applied, "An activity equal to the holder's current one changes nothing and applies: " + outcomes["same-activity"].Error);
        Assert(outcomes["move-speed"].Applied && w.Speed.Position[0] == 30, "A fixed placement of a holder still applies: " + outcomes["move-speed"].Error);
        Assert(outcomes["rebind-unrelated"].Applied && w.Unrelated.End == "b", "The scene patch file's other modifications still apply: " + outcomes["rebind-unrelated"].Error);
        Assert(w.Registrations() == before.Replace("Mown:True:Nnew:Nnew|a|5", "Mown:True:Nnew:Nnew|b|9"), "Every holder keeps its registration: " + w.Registrations());
    }
    static void PadHoldersRefuseActivityAndBindingAtEditAndLoad() {
        var world = new FakePadWorld();
        world.Segments["Sj7c"] = new List<double> { 400 }; world.Segments["Sx"] = new List<double> { 100 };
        const string PadPath = "World/Roads/Crossing/Crossing Pad", OtherPath = "World/Roads/Crossing (1)/Crossing Pad";
        var pad = new FakeMarker { Id = "Mpad", Segment = "Sj7c", End = "a", Distance = 185 };
        var other = new FakeMarker { Id = "Mpad", Segment = "Sx", End = "a", Distance = 10, Position = new double[] { 45, 1, 2 } };
        world.Objects[PadPath] = new List<object> { pad }; world.Objects[OtherPath] = new List<object> { other };
        foreach (var m in new[] { pad, other }) { world.Load(m); world.Relative(m); world.Generated(m); }
        world.Mutations = 0;
        string Registered() => String.Join(" ", new[] { pad, other }.Select(m => m.Active + ":" + String.Join(",", world.Registrations(m).Select(r => r.Bucket + "|" + r.Location.Distance))));
        var before = Registered();
        string PadItem(string key, string body) => "\"" + key + "\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + PadPath + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"crossing-pad-v1\"}," + body + ",\"dependsOn\":[]}";
        var load = new PatchEngine(new CrossingPadScene(world, Capability.Unchecked)).Apply(Doc(String.Join(",",
            PadItem("disable-pad", "\"active\":false"), PadItem("rebind-pad", "\"relationships\":{\"trackMarker\":{\"segment\":\"Sj7c\",\"end\":\"a\",\"distance\":186}}"),
            PadItem("detach-pad", "\"relationships\":{\"trackMarker\":{\"detach\":true}}"))));
        foreach (var key in new[] { "disable-pad", "rebind-pad", "detach-pad" })
            Assert(!load.Outcomes[key].Applied && load.Outcomes[key].Error.Contains("Mpad") && load.Outcomes[key].Error.Contains(PadPath) && load.Outcomes[key].Error.Contains(OtherPath), key + " of a pad that holds a duplicate marker ID fails naming it and both pads: " + load.Outcomes[key].Error);
        Assert(world.Mutations == 0 && Registered() == before, "No pad changed and both keep their registration");
        var capabilities = new SceneCapabilities(() => world.MapIds);
        capabilities.Add(new CrossingPadScene(world, Capability.Unchecked));
        var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(capabilities), mapEditor, new Store(), "map"); mapEditor.Session = session;
        var identity = new TargetIdentity(Scene, PadPath, "Track.TrackMarker", CrossingPadScene.Structure);
        var disable = session.Edit(identity, null, false);
        var rebind = session.Bind(identity, MarkerBinding.At(new TrackLocation("Sj7c", "a", 186)));
        Assert(!disable.Applied && disable.Error.Contains("Mpad") && !rebind.Applied && rebind.Error.Contains(OtherPath), "Scene patch editing refuses the pad's activity and binding edits: " + disable.Error + " / " + rebind.Error);
        Assert(world.Mutations == 0 && Registered() == before && !mapEditor.CanUndo, "The refused pad edits change nothing");
    }

    static void EnablingAnInactiveHolderIsRefusedWithTheExplanation() {
        var w = new World();
        var before = w.Registrations(); var scenePatchFile = w.Session.ScenePatchFileText;
        var enable = w.Session.Edit(Marker(YardPath), null, true);
        Assert(!enable.Applied, "Enabling the inactive Yard Limit that holds Mg6m is refused");
        Assert(enable.Error.Contains("Mg6m") && enable.Error.Contains(SpeedPath) && enable.Error.Contains(YardPath), "The refusal names the marker ID and every holder: " + enable.Error);
        AssertNoReloadAdvice(enable.Error, "Enable");
        Assert(w.Registrations() == before && w.Markers.Mutations == 0 && w.Session.ScenePatchFileText == scenePatchFile && !w.MapEditor.CanUndo, "Nothing changed: " + w.Registrations());
    }

    // The edited holder comes first, then the active holders, whose registration is at stake, and the inactive ones last.
    static void RefusalNamesTheEditedAndActiveHoldersFirst() {
        var w = new World();
        var enable = w.Session.Edit(Marker(YardPath), null, true).Error;
        Assert(enable.StartsWith("Duplicate marker ID Mg6m is held by 2 scene markers, " + YardPath + " (inactive) and " + SpeedPath + " (active). ", StringComparison.Ordinal), "The edited Yard Limit comes first: " + enable);
        var disable = w.Session.Edit(Marker(SpeedPath), null, false).Error;
        Assert(disable.StartsWith("Duplicate marker ID Mg6m is held by 2 scene markers, " + SpeedPath + " (active) and " + YardPath + " (inactive). ", StringComparison.Ordinal), "The edited speed sign comes first: " + disable);
        var g = new World();
        g.Markers.SetActive(g.Whistles[0], false); g.Markers.Load(g.Whistles[1]); g.Markers.Load(g.Whistles[2]); g.Markers.Mutations = 0; // a load with the first sign inactive
        var group = g.Session.Bind(Marker(WhistlePaths[2]), MarkerBinding.At(new TrackLocation("Sc", "a", 9))).Error;
        Assert(group.StartsWith("Duplicate marker ID Mkft is held by 3 scene markers, " + WhistlePaths[2] + " (active), " + WhistlePaths[1] + " (active), and " + WhistlePaths[0] + " (inactive). ", StringComparison.Ordinal),
            "The edited sign first, the active sign next and the inactive sign last: " + group);
    }
    // A holder whose activity follows its track group: its explicit disable would install the hold and deactivate it,
    // which removes the registration of every holder.
    static void GroupFollowerHolderRefusesItsExplicitDisable() {
        var w = new World();
        w.Markers.Groups["Sb"] = "bryson"; w.Markers.EnabledGroups.Add("bryson");
        w.Markers.LoadFollower(w.Whistles[1]);
        var before = w.Registrations(); var scenePatchFile = w.Session.ScenePatchFileText;
        AssertRefused(w, w.Session.Edit(Marker(WhistlePaths[1]), null, false), before, scenePatchFile, "An explicit disable of a holder with group-driven activity", new[] { "Mkft" }.Concat(WhistlePaths).ToArray());
        Assert(!w.Whistles[1].Held && w.Whistles[1].Active && w.Whistles[1].Subscriptions == 1, "The hold is not installed and the group subscription stays");
    }
    static void PadHolderRefusesEnableAndTrackFollowing() {
        var world = new FakePadWorld();
        world.Segments["Sj7c"] = new List<double> { 400 }; world.Segments["Sx"] = new List<double> { 100 };
        const string PadPath = "World/Roads/Crossing/Crossing Pad", OtherPath = "World/Roads/Crossing (1)/Crossing Pad";
        var pad = new FakeMarker { Id = "Mpad", Segment = "Sj7c", End = "a", Distance = 185, Active = false };
        var other = new FakeMarker { Id = "Mpad", Segment = "Sx", End = "a", Distance = 10, Position = new double[] { 45, 1, 2 } };
        world.Objects[PadPath] = new List<object> { pad }; world.Objects[OtherPath] = new List<object> { other };
        foreach (var m in new[] { pad, other }) { world.Load(m); world.Relative(m); world.Generated(m); }
        world.Mutations = 0;
        string Registered() => String.Join(" ", new[] { pad, other }.Select(m => m.Active + ":" + String.Join(",", world.Registrations(m).Select(r => r.Bucket + "|" + r.Location.Distance))));
        var before = Registered();
        var tracks = new TrackScene();
        tracks.Poses["node:N1"] = new TrackPose(new[] { 1050.0, 1, 2 }, new[] { 0.0, 0, 0, 1 });
        var capabilities = new SceneCapabilities(() => world.MapIds) { Tracks = tracks };
        capabilities.Add(new CrossingPadScene(world, Capability.Unchecked));
        var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(capabilities), mapEditor, new Store(), "map"); mapEditor.Session = session;
        var enable = session.Edit(new TargetIdentity(Scene, PadPath, "Track.TrackMarker", CrossingPadScene.Structure), null, true);
        Assert(!enable.Applied && enable.Error.Contains("Mpad") && enable.Error.Contains(OtherPath) && !pad.Active, "Enabling an inactive pad that holds a duplicate marker ID is refused: " + enable.Error);
        var load = new PatchEngine(capabilities).Apply(Doc("\"follow-pad\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + PadPath + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"crossing-pad-v1\"},"
            + "\"placement\":{\"mode\":\"trackFollowing\",\"track\":{\"node\":\"N1\"},\"offset\":[0,0,0],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}"));
        Assert(!load.Outcomes["follow-pad"].Applied && load.Outcomes["follow-pad"].Error.Contains("Mpad") && load.Outcomes["follow-pad"].Cause == FailureCause.InvalidDefinition, "Track following of a pad holder fails at load: " + load.Outcomes["follow-pad"].Error);
        Assert(world.Mutations == 0 && Registered() == before && !mapEditor.CanUndo, "No pad changed and both keep their registration");
    }
    // A registration under the marker ID owned by a scene marker the world does not list, such as one in a scene that
    // is not loaded, makes the marker ID a duplicate marker ID: rewriting the registration would remove that one too.
    static void RegistrationOwnerOutsideTheLoadedMarkersIsAHolder() {
        var w = new World();
        var elsewhere = new FakeMarker { Id = "Mown", Segment = "Sjdl", End = "b", Distance = 30 };
        w.Markers.Unloaded[elsewhere] = "OtherScene/World/Signs/Elsewhere";
        w.Markers.Buckets["Sjdl"]["Mown"] = ("b", 30, elsewhere);
        var before = w.Registrations(); var scenePatchFile = w.Session.ScenePatchFileText;
        AssertRefused(w, w.Session.Bind(Marker("World/Large Scenery/Signs/Unrelated"), MarkerBinding.At(new TrackLocation("Nnew", "b", 7))), before, scenePatchFile,
            "A rebind of a marker whose registration another scene marker elsewhere holds", "Mown", "World/Large Scenery/Signs/Unrelated", "OtherScene/World/Signs/Elsewhere (active)");
        Assert(w.Markers.Buckets["Sjdl"]["Mown"].owner == elsewhere, "The other marker keeps its registration");
        var lines = Lines(w, w.Unrelated);
        Assert(lines.Contains("OtherScene/World/Signs/Elsewhere is active and has the registration under Mown at Sjdl / b / 30."), "The inspector lists it with the same holder set: " + String.Join(" | ", lines));
    }
    // A holder is an object: an object with two markers that hold the marker ID is one holder, as natively.
    static void OneObjectWithTwoMarkersIsOneHolder() {
        var handle = new object();
        var one = new MarkerIdHolders("Mtwo", new[] { new MarkerIdHolder(handle, "Scene/Sign", true), new MarkerIdHolder(handle, "Scene/Sign", true) });
        Assert(!one.Duplicate && one.Holders.Count == 1 && one.Dependencies(handle).Count == 0, "One object is one holder, so its marker ID is no duplicate");
        var two = new MarkerIdHolders("Mtwo", new[] { new MarkerIdHolder(handle, "Scene/Sign", true), new MarkerIdHolder(handle, "Scene/Sign", false), new MarkerIdHolder(new object(), "Scene/Other", false) });
        Assert(two.Duplicate && two.Holders.Count == 2 && two.Dependencies(handle).Count == 3, "Another object makes it a duplicate marker ID, counted by object");
    }
    // A map label cache that disagrees with a holder's location would be corrected by rewriting its registration, which
    // removes every holder's registration: the edit is refused before mutation, naming the cache and the holders.
    static void StaleDependentCacheOfAHolderIsRefusedBeforeMutation() {
        var w = new World();
        w.Whistles[0].CachedGroup = "stale";
        var before = w.Registrations(); var scenePatchFile = w.Session.ScenePatchFileText;
        var moved = w.Session.Edit(Marker(WhistlePaths[0]), new Placement(new[] { 1090.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null);
        Assert(!moved.Applied && moved.Error.Contains("the map label caches track group stale") && moved.Error.Contains("Mkft") && WhistlePaths.All(moved.Error.Contains), "The placement edit is refused with the cache and every holder: " + moved.Error);
        AssertNoReloadAdvice(moved.Error, "A stale cache");
        Assert(w.Registrations() == before && w.Markers.Mutations == 0 && w.Session.ScenePatchFileText == scenePatchFile && w.Whistles[0].Position[0] == 80, "Nothing changed: " + w.Registrations());
        var unrelated = new World();
        unrelated.Unrelated.CachedGroup = "stale";
        Assert(unrelated.Session.Edit(Marker("World/Large Scenery/Signs/Unrelated"), new Placement(new[] { 1061.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null).Applied && unrelated.Unrelated.CachedGroup == "extension",
            "A marker with a marker ID of its own still has its cache corrected by its edit");
    }
    // A placement that fails after it broke the holder's own registration or a dependent cache: restoring would rewrite
    // the registration by marker ID. The write refuses before it touches the graph, so cleanup is unverified and every
    // other holder keeps its registration.
    sealed class BreakingPlacementWorld : FakeMarkerWorld, ITrackMarkerWorld {
        public bool DropRegistration, StaleCache;
        public new void SetPlacement(object target, double[] position, double[] rotation) {
            base.SetPlacement(target, position, rotation);
            var m = (FakeMarker)target;
            if (DropRegistration) { DropRegistration = false; foreach (var bucket in Buckets.Values.Where(b => b.TryGetValue(m.Id, out var e) && ReferenceEquals(e.owner, m)).ToList()) bucket.Remove(m.Id); throw new InvalidOperationException("registration lost"); }
            if (StaleCache) { StaleCache = false; m.CachedGroup = "stale"; throw new InvalidOperationException("cache lost"); }
        }
    }
    static void WriteRefusesARestoreThatWouldReRegisterAHolder() {
        foreach (var registration in new[] { true, false }) {
            var world = new BreakingPlacementWorld();
            var w = new World(world);
            string Others() => String.Join(" ", w.Whistles.Skip(1).Select(m => String.Join(",", w.Markers.Registrations(m).Where(r => r.HoldsMarker).Select(r => r.Bucket))));
            var others = Others();
            if (registration) world.DropRegistration = true; else world.StaleCache = true;
            var edit = w.Session.Edit(Marker(WhistlePaths[0]), new Placement(new[] { 1090.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null);
            Assert(!edit.Applied && edit.Error.StartsWith("The edit failed. Cleanup could not be verified. Reason:", StringComparison.Ordinal) && Others() == others,
                (registration ? "A lost registration" : "A stale cache") + " after a failed edit is not rewritten by marker ID: " + edit.Error + " " + Others());
            var load = new World(world = new BreakingPlacementWorld());
            others = String.Join(" ", load.Whistles.Skip(1).Select(m => String.Join(",", load.Markers.Registrations(m).Where(r => r.HoldsMarker).Select(r => r.Bucket))));
            if (registration) world.DropRegistration = true; else world.StaleCache = true;
            var result = new PatchEngine(load.Capabilities).Apply(Doc(Item("move", WhistlePaths[0], "\"placement\":{\"mode\":\"fixed\",\"position\":[1090,1,2],\"rotation\":[0,0,0,1]}")));
            var after = String.Join(" ", load.Whistles.Skip(1).Select(m => String.Join(",", load.Markers.Registrations(m).Where(r => r.HoldsMarker).Select(r => r.Bucket))));
            Assert(!result.Outcomes["move"].Applied && result.Outcomes["move"].Error.StartsWith("The change failed. Cleanup could not be verified. Reason:", StringComparison.Ordinal) && after == others,
                (registration ? "A lost registration" : "A stale cache") + " after a failed modification is not rewritten by marker ID: " + result.Outcomes["move"].Error + " " + after);
        }
    }
}
