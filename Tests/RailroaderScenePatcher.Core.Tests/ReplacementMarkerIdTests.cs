// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// A replacement ID repairs a duplicate marker ID: one holder takes a new id that no other scene marker, map object
// or game registration uses, while every other holder of the old id keeps its registration. The id applies before the
// marker's binding, track-following placement and activity in the same modification.
static class ReplacementMarkerIdTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    const string Scene = "BushnellWhittier";
    const string SpeedPath = "World/Large Scenery/Signs/Speed - Fontana South Curve (3)", YardPath = "World/Large Scenery/Signs/Yard Limit", UnrelatedPath = "World/Large Scenery/Signs/Unrelated";
    static readonly string[] WhistlePaths = { "World/Roads/Grade Crossing - A/Whistle Sign W", "World/Roads/Grade Crossing - B/Whistle Sign W", "World/Roads/Grade Crossing - C/Whistle Sign W" };
    static TargetIdentity Marker(string path) => new TargetIdentity(Scene, path, "Track.TrackMarker", TrackMarkerScene.Structure);

    // A pair: the active speed sign holds the registration of Mg6m; the inactive Yard Limit holds the same marker ID. A
    // group: three active whistle signs hold Mkft, each registered on its own segment. An unrelated marker has an id of its
    // own, the map context holds a map object, and a passenger stop has a game registration of its own.
    sealed class World {
        public readonly FakeMarkerWorld Markers;
        public FakeMarker Speed, Yard, Unrelated;
        public FakeMarker[] Whistles;
        public Store Store;
        public NativeMapEditor MapEditor = new NativeMapEditor();
        public PatchEditingSession Session;
        public SceneCapabilities Capabilities;
        public PatchEngine Engine;
        public TrackScene Tracks = new TrackScene();
        public World(Store store = null, FakeMarkerWorld markers = null) {
            Markers = markers ?? new FakeMarkerWorld();
            Store = store ?? new Store();
            foreach (var segment in new[] { "Sa", "Sb", "Sc", "pi3pzp", "Sps" }) { Markers.Segments[segment] = new List<double> { 50 }; Markers.Groups[segment] = null; }
            Speed = new FakeMarker { Id = "Mg6m", Segment = "Sjdl", End = "a", Distance = 10 };
            Yard = new FakeMarker { Id = "Mg6m", Segment = "S9sc", End = "b", Distance = 20, Active = false, Position = new double[] { 40, 1, 2 } };
            Unrelated = new FakeMarker { Id = "Mown", Segment = "Nnew", End = "a", Distance = 5, Position = new double[] { 60, 1, 2 } };
            Whistles = new[] { "Sa", "Sb", "Sc" }.Select((s, i) => new FakeMarker { Id = "Mkft", Segment = s, End = "a", Distance = 5 + i, Position = new double[] { 80 + i, 1, 2 } }).ToArray();
            Add(SpeedPath, Speed); Add(YardPath, Yard); Add(UnrelatedPath, Unrelated);
            for (var i = 0; i < Whistles.Length; i++) Add(WhistlePaths[i], Whistles[i]);
            Markers.MapObjects.Add("water-tower-1");
            Markers.Buckets["Sps"] = new Dictionary<string, (string, double, object)> { ["passengerStop:ela:Sps"] = ("a", 1, new object()) };
            Tracks.Poses["node:N1"] = new TrackPose(new[] { 1050.0, 1, 2 }, new[] { 0.0, 0, 0, 1 });
            Capabilities = new SceneCapabilities(() => Markers.MapIds) { Tracks = Tracks };
            Capabilities.Add(new TrackMarkerScene(Markers, Capability.Unchecked));
            Engine = new PatchEngine(Capabilities);
            Session = new PatchEditingSession(new PatchEngine(Capabilities), MapEditor, Store); MapEditor.Session = Session;
            Markers.Mutations = 0;
        }
        void Add(string path, FakeMarker m) { Markers.Objects[path] = new List<object> { m }; Markers.Load(m); Markers.Relative(m); }
        // Every marker's id, activity and segment, with every registration under its id and its owner.
        public string Registrations() => String.Join(" ", new[] { Speed, Yard, Unrelated }.Concat(Whistles).Select(m =>
            m.Id + ":" + m.Active + ":" + m.Segment + ":" + String.Join(",", Markers.Registrations(m).Select(r => r.Bucket + "|" + r.Location.End + "|" + r.Location.Distance + (r.HoldsMarker ? "" : "|" + (r.OwnerName ?? "foreign"))))));
        // The registration a marker holds under its own id, as bucket|end|distance, or "none".
        public string Own(FakeMarker m) {
            var own = Markers.Registrations(m).Where(r => r.HoldsMarker).Select(r => r.Bucket + "|" + r.Location.End + "|" + r.Location.Distance).ToArray();
            return own.Length == 0 ? "none" : String.Join(",", own);
        }
    }

    public static void Run() {
        ParseRefusesEmptyAndReservedIdsBeforeMutation();
        UniquenessNamesTheMarkerOrMapObjectThatUsesTheId();
        YardLimitRepairAppliesBeforeEnableAndRebind();
        WhistleRepairLetsOneHolderBeDisabled();
        LoadOrderCollisionFailsTheLaterModification();
        TheSameIdInTwoModificationsFailsBoth();
        OnlyGenericTrackMarkersTakeAReplacementId();
        RepeatedApplicationAndReentryConverge();
        FailedAttemptRestoresTheIdAndEveryRegistration();
        IncompleteCleanupBlocksTheRetryUntilRecheck();
        AWorldReloadReleasesTheBlockedRetry();
        RefusalOffersTheReplacementIdForGenericMarkers();
        ActiveHolderShowsTheProblemAndInactiveHolderTheExplanation();
        ScenePatchEditingAppliesUndoesRemovesAndSaves();
        ScenePatchEditingRefusesBeforeMutation();
        MarkerIdRowStatesItsReasons();
        ReplacementIdAppliesBeforeTrackFollowing();
        DependentEditsLoadAfterTheReplacementId();
        SettingTheUnderlyingIdIsAClear();
        StaleDependencyLeavesWithTheSetting();
        OwnDependenciesStayWhenTheReplacementEntriesLeave();
        AHolderThatTakesItsOwnIdDropsTheDependency();
        AMapLabelCacheMakesAPlacementDependOnTheReplacementId();
        UnreadMapObjectsRefuseTheId();
        SpacesAtEitherEndAreRefused();
        Console.WriteLine("PASS: a replacement ID repairs a duplicate marker ID and every other holder keeps its registration");
    }
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + items + "}}";
    static string Item(string key, string path, string body) => "\"" + key + "\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"}," + body + ",\"dependsOn\":[]}";

    static void ParseRefusesEmptyAndReservedIdsBeforeMutation() {
        foreach (var (value, expected) in new[] {
            ("\"\"", ReplacementMarkerIds.Empty), ("\"   \"", ReplacementMarkerIds.Empty), ("7", ReplacementMarkerIds.NotAString),
            ("\"passengerStop:ela\"", "passengerStop:"), ("\"flare:12\"", "flare:") }) {
            var w = new World(); var before = w.Registrations();
            var outcome = w.Engine.Apply(Doc(Item("yard", YardPath, "\"markerId\":" + value + ",\"active\":true"))).Outcomes["yard"];
            Assert(!outcome.Applied && outcome.Failure == FailureKind.Refused && outcome.Cause == FailureCause.InvalidDefinition && outcome.Error.Contains(expected),
                "A replacement id " + value + " fails before mutation with the reason: " + outcome.Error);
            Assert(w.Registrations() == before && w.Markers.Mutations == 0, value + ": nothing changed: " + w.Registrations());
        }
        Assert(ReplacementMarkerIds.Problem("flare:12").Contains("begins with flare:"), "The refusal names the reserved prefix: " + ReplacementMarkerIds.Problem("flare:12"));
        Assert(ReplacementMarkerIds.Problem("Mg6m-yard") == null, "An ordinary id has no static problem");
    }
    static void UniquenessNamesTheMarkerOrMapObjectThatUsesTheId() {
        foreach (var (id, named) in new[] { ("Mown", UnrelatedPath), ("Mkft", WhistlePaths[0]), ("water-tower-1", "map object water-tower-1") }) {
            var w = new World(); var before = w.Registrations();
            var outcome = w.Engine.Apply(Doc(Item("yard", YardPath, "\"markerId\":\"" + id + "\""))).Outcomes["yard"];
            Assert(!outcome.Applied && outcome.Failure == FailureKind.Refused && outcome.Error.Contains(id) && outcome.Error.Contains(named),
                "Replacement id " + id + " that another holder already uses is refused naming it: " + outcome.Error);
            Assert(w.Registrations() == before && w.Markers.Mutations == 0 && w.Yard.Id == "Mg6m", id + ": the marker keeps its id: " + w.Registrations());
        }
    }
    // The Yard Limit takes a replacement id, Enable and a rebind to pi3pzp in the same modification; the speed sign keeps
    // its registration under Mg6m.
    static void YardLimitRepairAppliesBeforeEnableAndRebind() {
        var w = new World();
        var speedBefore = w.Own(w.Speed);
        var outcome = w.Engine.Apply(Doc(Item("yard", YardPath, "\"markerId\":\"Mg6m-yard\",\"active\":true,\"relationships\":{\"trackMarker\":{\"segment\":\"pi3pzp\",\"end\":\"a\",\"distance\":3}}"))).Outcomes["yard"];
        Assert(outcome.Applied, "The repair, Enable and rebind apply in one modification: " + outcome.Error);
        Assert(w.Yard.Id == "Mg6m-yard" && w.Yard.Active && w.Own(w.Yard) == "pi3pzp|a|3", "The Yard Limit carries the replacement id and is registered under it at pi3pzp: " + w.Registrations());
        Assert(w.Speed.Id == "Mg6m" && w.Own(w.Speed) == speedBefore && speedBefore == "Sjdl|a|10", "The speed sign stays registered under Mg6m: " + w.Registrations());
        // Without the replacement id the same Enable is refused, as before.
        var refused = new World();
        var enable = refused.Engine.Apply(Doc(Item("yard", YardPath, "\"active\":true"))).Outcomes["yard"];
        Assert(!enable.Applied && enable.Error.Contains("Mg6m"), "Enable alone stays refused: " + enable.Error);
    }
    static void WhistleRepairLetsOneHolderBeDisabled() {
        var w = new World();
        var others = new[] { w.Own(w.Whistles[0]), w.Own(w.Whistles[2]) };
        var outcome = w.Engine.Apply(Doc(Item("whistle", WhistlePaths[1], "\"markerId\":\"Mkft-b\",\"active\":false"))).Outcomes["whistle"];
        Assert(outcome.Applied && w.Whistles[1].Id == "Mkft-b" && !w.Whistles[1].Active && w.Own(w.Whistles[1]) == "none", "One whistle sign takes a replacement id and is disabled: " + outcome.Error + " " + w.Registrations());
        Assert(w.Own(w.Whistles[0]) == others[0] && w.Own(w.Whistles[2]) == others[1] && others.All(o => o != "none"), "Every other holder of Mkft keeps its registration: " + w.Registrations());
    }
    // A replacement id that an earlier scene patch file in load order applied is present in the world when a later one applies.
    static void LoadOrderCollisionFailsTheLaterModification() {
        var w = new World();
        var first = w.Engine.Apply(Doc(Item("yard", YardPath, "\"markerId\":\"Mnew\"")), "a/first").Outcomes["yard"];
        Assert(first.Applied && w.Yard.Id == "Mnew", "The earlier scene patch file applies: " + first.Error);
        var before = w.Registrations();
        var later = w.Engine.Apply(Doc(Item("unrelated", UnrelatedPath, "\"markerId\":\"Mnew\",\"active\":false")), "b/later").Outcomes["unrelated"];
        Assert(!later.Applied && later.Failure == FailureKind.Refused && later.Error.Contains(YardPath), "The later modification fails before mutation naming the Yard Limit: " + later.Error);
        Assert(w.Registrations() == before && w.Unrelated.Id == "Mown" && w.Unrelated.Active && w.Yard.Id == "Mnew", "The marker keeps its id and the earlier modification stays applied: " + w.Registrations());
    }
    static void TheSameIdInTwoModificationsFailsBoth() {
        var w = new World(); var before = w.Registrations();
        var result = w.Engine.Apply(Doc(Item("speed", SpeedPath, "\"markerId\":\"Mtwice\"") + "," + Item("yard", YardPath, "\"markerId\":\"Mtwice\"") + "," + Item("unrelated", UnrelatedPath, "\"active\":false")));
        foreach (var key in new[] { "speed", "yard" })
            Assert(!result.Outcomes[key].Applied && result.Outcomes[key].Cause == FailureCause.InvalidDefinition && result.Outcomes[key].Error.Contains("Mtwice") && result.Outcomes[key].Error.Contains("more than one modification"),
                key + " fails for the shared replacement id: " + result.Outcomes[key].Error);
        Assert(result.Outcomes["unrelated"].Applied && w.Speed.Id == "Mg6m" && w.Yard.Id == "Mg6m", "The other modifications still apply and neither holder changes its id: " + w.Registrations());
        Assert(w.Registrations() == before.Replace("Mown:True:Nnew:Nnew|a|5", "Mown:False:Nnew:"), "Only the unrelated marker changed: " + w.Registrations());
    }
    static void OnlyGenericTrackMarkersTakeAReplacementId() {
        var world = new FakePadWorld();
        world.Segments["Sj7c"] = new List<double> { 400 }; world.Segments["Sx"] = new List<double> { 100 };
        const string PadPath = "World/Roads/Crossing/Crossing Pad", OtherPath = "World/Roads/Crossing (1)/Crossing Pad";
        var pad = new FakeMarker { Id = "Mpad", Segment = "Sj7c", End = "a", Distance = 185 };
        var other = new FakeMarker { Id = "Mpad", Segment = "Sx", End = "a", Distance = 10, Position = new double[] { 45, 1, 2 } };
        world.Objects[PadPath] = new List<object> { pad }; world.Objects[OtherPath] = new List<object> { other };
        foreach (var m in new[] { pad, other }) { world.Load(m); world.Relative(m); world.Generated(m); }
        world.Mutations = 0;
        var engine = new PatchEngine(new CrossingPadScene(world, Capability.Unchecked));
        var json = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"pad\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + PadPath + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"" + CrossingPadScene.Structure + "\"},\"markerId\":\"Mpad-2\",\"dependsOn\":[]}}}";
        var outcome = engine.Apply(json).Outcomes["pad"];
        Assert(!outcome.Applied && outcome.Error == ReplacementMarkerIds.NotSupported && pad.Id == "Mpad" && world.Mutations == 0, "A crossing pad refuses a replacement id with the reason: " + outcome.Error);
        // An object without a track marker refuses it too.
        var board = new Scene(); board.Objects["b"] = new List<object> { new Box { Rotation = new[] { 0.0, 0, 0, 1 } } };
        var boxed = new PatchEngine(board).Apply("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"b\":{\"target\":{\"scene\":\"s\",\"path\":\"b\",\"component\":\"c\"},\"markerId\":\"Mx\",\"dependsOn\":[]}}}").Outcomes["b"];
        Assert(!boxed.Applied && boxed.Error == ReplacementMarkerIds.NotSupported && board.MutationCalls == 0, "An object without a marker refuses a replacement id: " + boxed.Error);
    }
    static void RepeatedApplicationAndReentryConverge() {
        var w = new World();
        var json = Doc(Item("yard", YardPath, "\"markerId\":\"Mg6m-yard\",\"active\":true"));
        Assert(w.Engine.Apply(json, "pack").Outcomes["yard"].Applied, "The first application applies");
        var after = w.Registrations();
        var again = w.Engine.Apply(json, "pack").Outcomes["yard"];
        Assert(again.Applied && w.Registrations() == after, "Repeated application converges: " + again.Error + " " + w.Registrations());
        var reentry = w.Engine.Reenter();
        Assert(reentry.Retained > 0 && reentry.Failures.Count == 0, "The retained Yard Limit stays recorded");
        var reentered = w.Engine.Apply(json, "pack").Outcomes["yard"];
        Assert(reentered.Applied && w.Registrations() == after, "Retained-world re-entry converges: " + reentered.Error + " " + w.Registrations());
        Assert(w.Markers.Buckets.Values.Count(b => b.ContainsKey("Mg6m-yard")) == 1 && w.Markers.Buckets.Values.Count(b => b.ContainsKey("Mg6m")) == 1,
            "One registration under the replacement id, and the speed sign's one under Mg6m: " + w.Registrations());
    }
    static void FailedAttemptRestoresTheIdAndEveryRegistration() {
        var w = new World(); var before = w.Registrations();
        w.Markers.ThrowAfterLocation = true;
        var outcome = w.Engine.Apply(Doc(Item("yard", YardPath, "\"markerId\":\"Mg6m-yard\",\"relationships\":{\"trackMarker\":{\"segment\":\"pi3pzp\",\"end\":\"a\",\"distance\":3}}"))).Outcomes["yard"];
        Assert(!outcome.Applied && outcome.Failure == FailureKind.Restored, "The failed attempt is restored: " + outcome.Error);
        Assert(w.Yard.Id == "Mg6m" && w.Registrations() == before, "Restoration returns the id and every registration: " + w.Registrations());
        // An active holder that failed after its id changed returns to Mg6m with its registration; the Yard Limit keeps none.
        var speed = new World(); var prior = speed.Registrations();
        speed.Markers.ThrowAfterLocation = true;
        var failed = speed.Engine.Apply(Doc(Item("speed", SpeedPath, "\"markerId\":\"Mg6m-speed\",\"relationships\":{\"trackMarker\":{\"segment\":\"Sjdl\",\"end\":\"a\",\"distance\":12}}"))).Outcomes["speed"];
        Assert(!failed.Applied && failed.Failure == FailureKind.Restored && speed.Speed.Id == "Mg6m" && speed.Registrations() == prior, "The speed sign returns to Mg6m and its registration: " + failed.Error + " " + speed.Registrations());
    }
    static void IncompleteCleanupBlocksTheRetryUntilRecheck() {
        var w = new World();
        w.Markers.ThrowAfterLocation = true; w.Markers.FailMarkerId = "Mg6m";
        var outcome = w.Engine.Apply(Doc(Item("yard", YardPath, "\"markerId\":\"Mg6m-yard\",\"relationships\":{\"trackMarker\":{\"segment\":\"pi3pzp\",\"end\":\"a\",\"distance\":3}}")), "pack").Outcomes["yard"];
        Assert(!outcome.Applied && outcome.Failure == FailureKind.IncompleteCleanup && outcome.Remaining.Any(l => l == "Marker ID: now Mg6m-yard, before Mg6m"),
            "The incomplete cleanup is reported with the remaining marker ID: " + outcome.Error + " | " + String.Join(" | ", outcome.Remaining));
        var retry = w.Engine.Apply(Doc(Item("yard", YardPath, "\"markerId\":\"Mg6m-yard\"")), "other").Outcomes["yard"];
        Assert(!retry.Applied && retry.Failure == FailureKind.Blocked, "A retry on the object is blocked: " + retry.Error);
        // In scene patch editing, the same guard holds until Recheck verifies the object against the scene patch file. The
        // removal fails as the id returns, and its restoration fails as the binding returns.
        var e = new World();
        var yard = Marker(YardPath);
        Assert(e.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied && e.Session.Bind(yard, MarkerBinding.At(new TrackLocation("Sjdl", "a", 30))).Applied, "The Yard Limit takes a replacement id and a rebind");
        e.Markers.FailMarkerId = "Mg6m"; e.Markers.FailRestoreLocation = true;
        var removed = e.Session.Remove(yard);
        Assert(!removed.Applied && removed.Failure == FailureKind.IncompleteCleanup && removed.Remaining.Contains("Track binding: now S9sc / b / 20, before Sjdl / a / 30"),
            "A removal whose restoration fails reports the incomplete cleanup: " + removed.Error + " | " + String.Join(" | ", removed.Remaining));
        var blocked = e.Session.SetMarkerId(yard, "Mg6m-other", null);
        Assert(!blocked.Applied && blocked.Failure == FailureKind.Blocked, "The next edit is blocked until cleanup is verified: " + blocked.Error);
        Assert(e.Own(e.Speed) == "Sjdl|a|10", "The speed sign keeps its registration through the failure: " + e.Registrations());
        e.Markers.FailMarkerId = null; e.Markers.FailRestoreLocation = false;
        var recheck = e.Session.Recheck(yard);
        Assert(recheck.Applied && e.Yard.Id == "Mg6m-yard" && e.Yard.Segment == "Sjdl", "Recheck verifies the object against the scene patch file: " + recheck.Error + " " + e.Registrations());
        Assert(e.Session.Remove(yard).Applied && e.Yard.Id == "Mg6m" && e.Yard.Segment == "S9sc" && e.Own(e.Speed) == "Sjdl|a|10", "After Recheck the removal returns the id and the binding: " + e.Registrations());
    }
    // A world reload rebuilds every marker from the scene as the game loads it: the next load attempt releases the
    // object whose cleanup stayed unverified and applies the replacement ID afresh. A new session starts with a new
    // engine over the same scene patch file and applies it as well.
    static void AWorldReloadReleasesTheBlockedRetry() {
        var w = new World();
        var json = Doc(Item("yard", YardPath, "\"markerId\":\"Mg6m-yard\",\"relationships\":{\"trackMarker\":{\"segment\":\"pi3pzp\",\"end\":\"a\",\"distance\":3}}"));
        w.Markers.ThrowAfterLocation = true; w.Markers.FailMarkerId = "Mg6m";
        Assert(w.Engine.Apply(json, "pack").Outcomes["yard"].Failure == FailureKind.IncompleteCleanup, "The attempt leaves an incomplete cleanup");
        w.Markers.FailMarkerId = null;
        w.Engine.Reenter();
        Assert(w.Engine.Apply(json, "pack").Outcomes["yard"].Failure == FailureKind.Blocked, "Over the retained world the retry stays blocked");
        // The reload: the game destroys the scene markers and their registrations and loads them from the scene again.
        var reloaded = new World();
        w.Markers.Objects.Clear(); w.Markers.Buckets.Clear(); w.Markers.Owners.Clear();
        foreach (var path in reloaded.Markers.Objects.Keys) w.Markers.Objects[path] = reloaded.Markers.Objects[path];
        foreach (var bucket in reloaded.Markers.Buckets) w.Markers.Buckets[bucket.Key] = bucket.Value;
        w.Markers.Owners.AddRange(reloaded.Markers.Owners);
        var release = w.Engine.Reenter();
        Assert(release.Released > 0 && release.Failures.Count == 0, "The next load attempt releases the reloaded marker: " + String.Join(" | ", release.Failures));
        var retry = w.Engine.Apply(json, "pack").Outcomes["yard"];
        Assert(retry.Applied && reloaded.Yard.Id == "Mg6m-yard" && reloaded.Own(reloaded.Yard) == "none" && reloaded.Yard.Segment == "pi3pzp" && reloaded.Own(reloaded.Speed) == "Sjdl|a|10",
            "After the reload the retry applies the replacement ID and the rebind: " + retry.Error + " " + reloaded.Registrations());
        // A new session over the same scene patch file starts with a new engine.
        var session = new World();
        session.Markers.ThrowAfterLocation = true; session.Markers.FailMarkerId = "Mg6m";
        Assert(session.Engine.Apply(json, "pack").Outcomes["yard"].Failure == FailureKind.IncompleteCleanup, "The first session leaves an incomplete cleanup");
        var next = new World();
        var applied = next.Engine.Apply(json, "pack").Outcomes["yard"];
        Assert(applied.Applied && next.Yard.Id == "Mg6m-yard" && next.Yard.Segment == "pi3pzp", "A new session applies the replacement ID: " + applied.Error + " " + next.Registrations());
    }
    static void RefusalOffersTheReplacementIdForGenericMarkers() {
        var w = new World();
        var refusal = w.Session.Edit(Marker(WhistlePaths[1]), null, false).Error;
        Assert(refusal.Contains(ReplacementMarkerIds.Offer), "The refusal of a generic marker offers the replacement id: " + refusal);
        Assert(!refusal.Contains(": "), "The refusal stays plain sentences: " + refusal);
        var pads = new FakePadWorld();
        pads.Segments["Sj7c"] = new List<double> { 400 }; pads.Segments["Sx"] = new List<double> { 100 };
        var pad = new FakeMarker { Id = "Mpad", Segment = "Sj7c", End = "a", Distance = 185 };
        var other = new FakeMarker { Id = "Mpad", Segment = "Sx", End = "a", Distance = 10, Position = new double[] { 45, 1, 2 } };
        pads.Objects["World/Roads/Crossing/Crossing Pad"] = new List<object> { pad }; pads.Objects["World/Roads/Crossing (1)/Crossing Pad"] = new List<object> { other };
        foreach (var m in new[] { pad, other }) { pads.Load(m); pads.Relative(m); pads.Generated(m); }
        var padRefusal = new PatchEngine(new CrossingPadScene(pads, Capability.Unchecked)).Apply("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"pad\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"World/Roads/Crossing/Crossing Pad\",\"component\":\"Track.TrackMarker\",\"structure\":\"" + CrossingPadScene.Structure + "\"},\"active\":false,\"dependsOn\":[]}}}").Outcomes["pad"].Error;
        Assert(padRefusal.Contains("Mpad") && !padRefusal.Contains(ReplacementMarkerIds.Offer), "A crossing pad's refusal does not offer the replacement id: " + padRefusal);
    }
    static void ActiveHolderShowsTheProblemAndInactiveHolderTheExplanation() {
        var w = new World();
        MarkerIdHolders Holders(FakeMarker m) => w.Markers.IdHolders(m).With(w.Markers.Registrations(m));
        var speed = Holders(w.Speed).Problem(w.Speed, true);
        Assert(speed != null && speed.Contains("Mg6m") && speed.Contains(ReplacementMarkerIds.Offer) && !speed.Contains(": "), "The active speed sign shows a duplicate-id problem that names the id and offers the replacement id: " + speed);
        Assert(Holders(w.Yard).Problem(w.Yard, true) == null && Holders(w.Yard).Dependencies(w.Yard, true).Count == 3, "The inactive Yard Limit shows the explanation without a problem");
        Assert(Holders(w.Yard).Dependencies(w.Yard, true)[0].EndsWith(ReplacementMarkerIds.Offer, StringComparison.Ordinal), "The explanation of a holder that can receive a replacement ID offers the repair, also when it is inactive");
        Assert(!Holders(w.Yard).Dependencies(w.Yard)[0].Contains(ReplacementMarkerIds.Offer), "The explanation of a holder that cannot receive one does not offer it");
        Assert(Holders(w.Speed).Problem(w.Speed, false) == null, "An active holder that cannot receive a replacement ID, such as a crossing pad, has no problem");
        Assert(Holders(w.Whistles[0]).Problem(w.Whistles[0], true).Contains("2 other scene markers"), "A group names how many others share the id: " + Holders(w.Whistles[0]).Problem(w.Whistles[0], true));
        Assert(Holders(w.Unrelated).Problem(w.Unrelated, true) == null, "An id of its own is no problem");
        // The Object tab's Detected problems section agrees with the repair it offers: an inactive generic holder has no
        // problem, so the section says why instead of None detected beside Repair duplicate ID.
        var yardLines = Holders(w.Yard).Dependencies(w.Yard, true);
        Assert(CommandBand.OffersDuplicateRepair(TrackMarkerScene.Structure, yardLines) && !CommandBand.OffersDuplicateRepair(null, yardLines)
            && !CommandBand.OffersDuplicateRepair(TrackMarkerScene.Structure, Holders(w.Unrelated).Dependencies(w.Unrelated, true)),
            "Repair duplicate ID is offered for a generic track marker whose explanation names a duplicate marker ID");
        Assert(CommandBand.NoDetectedProblems(true) == "None while this marker is inactive. The patcher refuses changes to its activity, binding, and track-following placement until its duplicate marker ID is repaired.",
            "Beside the repair, the section says why the duplicate marker ID is no problem now and what it still refuses: " + CommandBand.NoDetectedProblems(true));
        Assert(CommandBand.NoDetectedProblems(false) == "None detected", "Without the repair the section reads None detected");
    }

    // The Marker ID row of the Connections tab and its guided task: the replacement id stays visible and disabled with its
    // reason for any marker that does not take one, and Set id and Clear id state why they cannot be used.
    static void MarkerIdRowStatesItsReasons() {
        Assert(RailroaderScenePatcher.CommandBand.MarkerIdUnavailable(false, "Mpad", 1, false) == ReplacementMarkerIds.NotSupported, "A marker of another kind keeps the row disabled with its reason");
        Assert(RailroaderScenePatcher.CommandBand.MarkerIdUnavailable(true, "Mown", 0, false) == "Only this marker holds marker ID Mown, so it doesn't need a replacement ID.", "A marker ID of its own needs no replacement");
        Assert(RailroaderScenePatcher.CommandBand.MarkerIdUnavailable(true, "Mg6m", 1, false) == null && RailroaderScenePatcher.CommandBand.MarkerIdUnavailable(true, "Mg6m", 0, true) == null,
            "A shared native id offers the replacement id, and a marker that already has one keeps the row");
        Assert(RailroaderScenePatcher.CommandBand.MarkerIdSetReason(null, "", "Mg6m") == ReplacementMarkerIds.Empty, "An empty field states the reason");
        Assert(RailroaderScenePatcher.CommandBand.MarkerIdSetReason(null, "flare:1", "Mg6m").StartsWith("Replacement ID flare:1 begins with flare:", StringComparison.Ordinal), "A reserved prefix states the reason");
        Assert(RailroaderScenePatcher.CommandBand.MarkerIdSetReason(null, "Mg6m", "Mg6m") == "The field already contains the marker's current ID.", "The current id has nothing to set");
        Assert(RailroaderScenePatcher.CommandBand.MarkerIdSetReason("Editing is unavailable.", "Mx", "Mg6m") == "Editing is unavailable." && RailroaderScenePatcher.CommandBand.MarkerIdSetReason(null, "Mx", "Mg6m") == null, "The general reason comes first");
        Assert(RailroaderScenePatcher.CommandBand.ClearMarkerIdReason(false) == "This mod sets no replacement ID for this marker, so there is nothing to clear." && RailroaderScenePatcher.CommandBand.ClearMarkerIdReason(true) == null, "Clear id states when there is nothing to clear");
        Assert(RailroaderScenePatcher.GuidedTaskState.Tasks.Contains(RailroaderScenePatcher.GuidedTaskKind.MarkerId), "The guided task list offers the replacement ID");
        Assert(RailroaderScenePatcher.CommandBand.MarkerIdSetReason(null, " Mx", "Mg6m").Contains("begins or ends with a space"), "A space at either end states the reason");
        Assert(RailroaderScenePatcher.CommandBand.ClearMarkerIdReason("Editing is unavailable.", true) == "Editing is unavailable.", "Clear ID follows the general edit block first");
        Assert(RailroaderScenePatcher.CommandBand.SharersCaption("Mg6m", "Mg6m") == "Also holding Mg6m" && RailroaderScenePatcher.CommandBand.SharersCaption("Mg6m", "Mg6m-yard") == "Still holding Mg6m, the underlying ID",
            "After the repair the caption no longer says the marker holds the underlying ID");
    }

    // Criterion 13 for track following: the replacement ID applies before a track-following placement in the same
    // modification, which without it is refused for a holder of a duplicate marker ID.
    static void ReplacementIdAppliesBeforeTrackFollowing() {
        const string follow = "\"placement\":{\"mode\":\"trackFollowing\",\"track\":{\"node\":\"N1\"},\"offset\":[0,0,0],\"rotation\":[0,0,0,1]}";
        var refused = new World();
        var alone = refused.Engine.Apply(Doc(Item("yard", YardPath, follow))).Outcomes["yard"];
        Assert(!alone.Applied && alone.Error.Contains("Mg6m"), "Track following alone stays refused for the duplicate: " + alone.Error);
        var w = new World(); var speed = w.Own(w.Speed);
        var outcome = w.Engine.Apply(Doc(Item("yard", YardPath, "\"markerId\":\"Mg6m-yard\"," + follow))).Outcomes["yard"];
        Assert(outcome.Applied && w.Yard.Id == "Mg6m-yard" && w.Yard.Position[0] == 50, "The replacement ID applies first and the Yard Limit follows the node: " + outcome.Error + " " + String.Join(",", w.Yard.Position));
        Assert(w.Own(w.Speed) == speed && w.Speed.Id == "Mg6m", "The speed sign keeps its registration: " + w.Registrations());
    }
    // A holder's edit that is possible only because another holder took a replacement ID depends on that modification:
    // scene patch editing names it in dependsOn, so the file loads it first after Save, whatever the key order. The
    // replacement ID cannot then be removed or cleared while that edit needs it.
    static void DependentEditsLoadAfterTheReplacementId() {
        var w = new World();
        var yard = Marker(YardPath); var speed = Marker(SpeedPath);
        Assert(w.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied, "The Yard Limit takes the replacement ID");
        var bind = w.Session.Bind(speed, MarkerBinding.At(new TrackLocation("Sjdl", "a", 12)));
        Assert(bind.Applied && w.Own(w.Speed) == "Sjdl|a|12", "The speed sign, now the only holder of Mg6m, takes a rebind: " + bind.Error);
        var items = JObject.Parse(w.Session.ScenePatchFileText)["modifications"].Children<JProperty>().ToDictionary(p => (string)p.Value["target"]["path"], p => p);
        var yardKey = items[YardPath].Name;
        Assert(String.CompareOrdinal(items[SpeedPath].Name, yardKey) < 0 && items[SpeedPath].Value["dependsOn"].Select(d => (string)d).SequenceEqual(new[] { yardKey }),
            "The speed sign's modification sorts before the Yard Limit's and names it in dependsOn: " + w.Session.ScenePatchFileText);
        var removed = w.Session.Remove(yard);
        Assert(!removed.Applied && removed.Error.Contains(items[SpeedPath].Name) && w.Yard.Id == "Mg6m-yard", "Remove modification on the Yard Limit is refused while the speed sign depends on it: " + removed.Error);
        Assert(w.Session.Edit(yard, new Placement(new[] { 1041.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null).Applied, "The Yard Limit's modification takes a placement as well");
        var cleared = w.Session.SetMarkerId(yard, null, null);
        Assert(!cleared.Applied && cleared.Error.Contains(items[SpeedPath].Name) && cleared.Error.Contains("dependsOn") && w.Yard.Id == "Mg6m-yard", "Clear ID is refused while the speed sign depends on it: " + cleared.Error);
        Assert(w.MapEditor.Save() == null, "Save writes the file");
        var fresh = new World();
        var reload = fresh.Engine.Apply(w.Store.Patch);
        Assert(reload.Outcomes.Values.All(o => o.Applied) && fresh.Registrations() == w.Registrations(), "A reload applies the replacement ID before the rebind: " + String.Join(" | ", reload.Outcomes.Values.Select(o => o.Error)) + " " + fresh.Registrations());
        // The follow- case: the speed sign follows a node after the Yard Limit took its replacement ID.
        var f = new World();
        Assert(f.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied, "The Yard Limit takes the replacement ID");
        var follow = f.Session.Follow(speed, TrackAnchor.Endpoint("N1"));
        Assert(follow.Applied, "The speed sign follows the node: " + follow.Error);
        var followItems = JObject.Parse(f.Session.ScenePatchFileText)["modifications"].Children<JProperty>().ToDictionary(p => (string)p.Value["target"]["path"], p => p);
        Assert(String.CompareOrdinal(followItems[SpeedPath].Name, followItems[YardPath].Name) < 0 && followItems[SpeedPath].Value["dependsOn"].Select(d => (string)d).Contains(followItems[YardPath].Name),
            "The follow- modification names the replacement ID's modification in dependsOn: " + f.Session.ScenePatchFileText);
        Assert(f.MapEditor.Save() == null, "Save writes the file");
        var again = new World();
        var followed = again.Engine.Apply(f.Store.Patch);
        Assert(followed.Outcomes.Values.All(o => o.Applied) && again.Registrations() == f.Registrations(), "A reload applies the replacement ID before track following: " + String.Join(" | ", followed.Outcomes.Values.Select(o => o.Error)));
    }
    // Set ID with the underlying ID returns the marker to that ID, as Clear ID does, so it takes the same refusals: while
    // the modification itself still sets activity, a binding or track following, and while another holder depends on it.
    static void SettingTheUnderlyingIdIsAClear() {
        var w = new World();
        var yard = Marker(YardPath); var speed = Marker(SpeedPath);
        Assert(w.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied && w.Session.Edit(yard, null, true).Applied
            && w.Session.Bind(yard, MarkerBinding.At(new TrackLocation("pi3pzp", "a", 3))).Applied, "The Yard Limit takes a replacement ID, Enable and a rebind");
        var own = w.Session.SetMarkerId(yard, "Mg6m", null);
        Assert(!own.Applied && own.Error.Contains("need its replacement ID") && w.Yard.Id == "Mg6m-yard", "Set ID with the underlying ID is refused while the Yard Limit's own settings need the replacement ID: " + own.Error);
        Assert(w.Session.Bind(speed, MarkerBinding.At(new TrackLocation("Sjdl", "a", 12))).Applied, "The speed sign takes a dependent rebind");
        var before = w.Registrations(); var text = w.Session.ScenePatchFileText;
        var dependent = w.Session.SetMarkerId(yard, "Mg6m", null);
        Assert(!dependent.Applied && dependent.Failure == FailureKind.Refused && dependent.Error.Contains("dependsOn") && w.Registrations() == before && w.Session.ScenePatchFileText == text,
            "Set ID with the underlying ID is refused while the speed sign depends on the replacement ID: " + dependent.Error);
        // A Yard Limit whose modification sets only the replacement ID: Set ID with the underlying ID would remove the
        // modification, which the general dependsOn rule refuses.
        var only = new World();
        Assert(only.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied && only.Session.Bind(speed, MarkerBinding.At(new TrackLocation("Sjdl", "a", 12))).Applied, "Replacement ID and a dependent rebind");
        var leave = only.Session.SetMarkerId(yard, "Mg6m", null);
        Assert(!leave.Applied && leave.Error.Contains("dependsOn") && only.Yard.Id == "Mg6m-yard", "Set ID with the underlying ID cannot remove a modification another one depends on: " + leave.Error);
        // Without any dependency it clears, as Clear ID does.
        var free = new World();
        Assert(free.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied, "The Yard Limit takes a replacement ID");
        var back = free.Session.SetMarkerId(yard, "Mg6m", null);
        Assert(back.Applied && free.Yard.Id == "Mg6m" && !free.Session.HasMarkerId(yard), "Set ID with the underlying ID clears the mod's value: " + back.Error);
    }
    // A dependency on another holder's replacement ID leaves dependsOn when the holder's modification no longer sets
    // activity, a binding or track following, so the replacement ID can be removed again.
    static void StaleDependencyLeavesWithTheSetting() {
        var w = new World();
        var yard = Marker(YardPath); var speed = Marker(SpeedPath);
        Assert(w.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied, "The Yard Limit takes a replacement ID");
        Assert(w.Session.Edit(speed, new Placement(new[] { 1031.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null).Applied && w.Session.Bind(speed, MarkerBinding.At(new TrackLocation("Sjdl", "a", 12))).Applied,
            "The speed sign takes a placement and a dependent rebind");
        Assert(w.Session.Bind(speed, null).Applied, "Clearing the speed sign's binding keeps its placement");
        var item = JObject.Parse(w.Session.ScenePatchFileText)["modifications"].Children<JProperty>().Single(p => (string)p.Value["target"]["path"] == SpeedPath).Value;
        Assert(item["placement"] != null && !item["dependsOn"].Any(), "The speed sign's modification no longer names the Yard Limit's: " + item.ToString(Newtonsoft.Json.Formatting.None));
        Assert(w.Session.Remove(yard).Applied && w.Yard.Id == "Mg6m", "The Yard Limit's replacement ID can be removed again: " + w.Registrations());
    }
    // Dropping the entries of other holders' replacement IDs leaves the modification's own dependsOn entries alone.
    static void OwnDependenciesStayWhenTheReplacementEntriesLeave() {
        string Move(string key, string path, double x, string deps) => "\"" + key + "\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + x + ",1,2],\"rotation\":[0,0,0,1]},\"dependsOn\":" + deps + "}";
        var store = new Store { Patch = Doc(Move("a-unrelated", UnrelatedPath, 1061, "[]") + "," + Move("b-speed", SpeedPath, 1031, "[\"a-unrelated\"]")) };
        var w = new World(store);
        var yard = Marker(YardPath); var speed = Marker(SpeedPath);
        Assert(w.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied && w.Session.Bind(speed, MarkerBinding.At(new TrackLocation("Sjdl", "a", 12))).Applied, "Replacement ID and a dependent rebind");
        string[] Deps() => JObject.Parse(w.Session.ScenePatchFileText)["modifications"]["b-speed"]["dependsOn"].Select(d => (string)d).ToArray();
        var yardKey = JObject.Parse(w.Session.ScenePatchFileText)["modifications"].Children<JProperty>().Single(p => (string)p.Value["target"]["path"] == YardPath).Name;
        Assert(Deps().SequenceEqual(new[] { "a-unrelated", yardKey }), "The rebind adds the replacement ID's key next to the own entry: " + String.Join(",", Deps()));
        Assert(w.Session.Bind(speed, null).Applied && Deps().SequenceEqual(new[] { "a-unrelated" }), "Clearing the binding drops only the replacement ID's key: " + String.Join(",", Deps()));
    }
    // A holder that takes a replacement ID of its own no longer needs another holder's replacement ID first, so the
    // entry leaves and the other holder's replacement ID can be removed again.
    static void AHolderThatTakesItsOwnIdDropsTheDependency() {
        var w = new World();
        var yard = Marker(YardPath); var speed = Marker(SpeedPath);
        Assert(w.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied && w.Session.Bind(speed, MarkerBinding.At(new TrackLocation("Sjdl", "a", 12))).Applied, "Replacement ID and a dependent rebind");
        Assert(w.Session.SetMarkerId(speed, "Mg6m-speed", null).Applied, "The speed sign takes a replacement ID of its own");
        var item = JObject.Parse(w.Session.ScenePatchFileText)["modifications"].Children<JProperty>().Single(p => (string)p.Value["target"]["path"] == SpeedPath).Value;
        Assert(!item["dependsOn"].Any(), "The speed sign's modification no longer names the Yard Limit's: " + item.ToString(Newtonsoft.Json.Formatting.None));
        Assert(w.Session.Remove(yard).Applied && w.Yard.Id == "Mg6m" && w.Own(w.Speed) == "Sjdl|a|12", "The Yard Limit's replacement ID can be removed again: " + w.Registrations());
        Assert(w.MapEditor.Save() == null, "Save writes the file");
        var fresh = new World();
        Assert(fresh.Engine.Apply(w.Store.Patch).Outcomes.Values.All(o => o.Applied) && fresh.Registrations() == w.Registrations(), "A reload reproduces both markers: " + fresh.Registrations());
    }
    // A map label below a holder that caches another track group than its location makes even a fixed placement
    // rewrite the holder's registration, which a duplicate marker ID refuses: such a placement depends on the
    // replacement ID of the other holder as well.
    static void AMapLabelCacheMakesAPlacementDependOnTheReplacementId() {
        var w = new World(); w.Speed.CachedGroup = "stale";
        var yard = Marker(YardPath); var speed = Marker(SpeedPath);
        Assert(w.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied, "The Yard Limit takes a replacement ID");
        Assert(w.Session.Edit(speed, new Placement(new[] { 1031.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null).Applied, "The speed sign moves; its stale map label cache is corrected");
        var items = JObject.Parse(w.Session.ScenePatchFileText)["modifications"].Children<JProperty>().ToDictionary(p => (string)p.Value["target"]["path"], p => p);
        Assert(items[SpeedPath].Value["dependsOn"].Select(d => (string)d).SequenceEqual(new[] { items[YardPath].Name }), "The placement names the Yard Limit's modification in dependsOn: " + w.Session.ScenePatchFileText);
        // The edit corrected the cache in this world; a later placement still depends on the replacement ID, because a
        // reload starts from the stale cache again.
        Assert(w.Session.Edit(speed, new Placement(new[] { 1032.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null).Applied, "A second placement");
        var again = JObject.Parse(w.Session.ScenePatchFileText)["modifications"].Children<JProperty>().Single(p => (string)p.Value["target"]["path"] == SpeedPath).Value;
        Assert(again["dependsOn"].Select(d => (string)d).SequenceEqual(new[] { items[YardPath].Name }), "The second placement keeps the dependency: " + again.ToString(Newtonsoft.Json.Formatting.None));
        Assert(w.MapEditor.Save() == null, "Save writes the file");
        var fresh = new World(); fresh.Speed.CachedGroup = "stale";
        var reload = fresh.Engine.Apply(w.Store.Patch);
        Assert(reload.Outcomes.Values.All(o => o.Applied) && fresh.Speed.Position[0] == 32 && fresh.Yard.Id == "Mg6m-yard", "A reload applies the replacement ID before the placement: " + String.Join(" | ", reload.Outcomes.Values.Select(o => o.Error)));
        // Without the stale cache a placement needs no replacement ID, so it adds no entry.
        var plain = new World();
        Assert(plain.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied && plain.Session.Edit(speed, new Placement(new[] { 1031.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null).Applied, "A plain placement");
        var plainItem = JObject.Parse(plain.Session.ScenePatchFileText)["modifications"].Children<JProperty>().Single(p => (string)p.Value["target"]["path"] == SpeedPath).Value;
        Assert(!plainItem["dependsOn"].Any(), "A plain placement adds no dependency: " + plainItem.ToString(Newtonsoft.Json.Formatting.None));
    }
    // The map objects of the map context could not be read: the ID is refused rather than taken unchecked.
    static void UnreadMapObjectsRefuseTheId() {
        var w = new World(); w.Markers.MapObjects = null; var before = w.Registrations();
        var load = w.Engine.Apply(Doc(Item("yard", YardPath, "\"markerId\":\"Mg6m-yard\""))).Outcomes["yard"];
        Assert(!load.Applied && load.Error.Contains("couldn't be checked against them") && w.Registrations() == before, "At load the ID is refused with the reason: " + load.Error);
        var edit = w.Session.SetMarkerId(Marker(YardPath), "Mg6m-yard", null);
        Assert(!edit.Applied && edit.Failure == FailureKind.Refused && edit.Error.Contains("couldn't be checked against them") && w.Yard.Id == "Mg6m", "In scene patch editing the ID is refused with the reason: " + edit.Error);
    }
    static void SpacesAtEitherEndAreRefused() {
        var w = new World();
        foreach (var id in new[] { " Mg6m-yard", "Mg6m-yard " }) {
            var load = w.Engine.Apply(Doc(Item("yard", YardPath, "\"markerId\":\"" + id + "\""))).Outcomes["yard"];
            Assert(!load.Applied && load.Error.Contains("begins or ends with a space") && w.Yard.Id == "Mg6m", "Replacement ID \"" + id + "\" is refused: " + load.Error);
            var edit = w.Session.SetMarkerId(Marker(YardPath), id, null);
            Assert(!edit.Applied && edit.Error.Contains("begins or ends with a space"), "In scene patch editing too: " + edit.Error);
        }
    }

    // In scene patch editing: the repair, Enable and rebind of the Yard Limit, Undo and Redo with matching dirty state, Remove
    // modification back to the underlying id, Save in canonical order with the patcher dependency, a stricter constraint
    // kept, and a reload that reproduces the replacement id and every holder's registration.
    static void ScenePatchEditingAppliesUndoesRemovesAndSaves() {
        var w = new World();
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = w.Session.Outstanding(w.MapEditor.NativeDirty);
            Assert(w.MapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + w.MapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        var before = w.Registrations();
        var yard = Marker(YardPath);
        Assert(w.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied && w.Yard.Id == "Mg6m-yard", "The Yard Limit takes the replacement id");
        Assert(w.Session.MarkerIdOf(yard) == "Mg6m-yard" && w.Session.HasMarkerId(yard), "The session reads the id and that this mod sets it");
        Assert(w.Session.Changes(yard).Contains("Marker ID: from Mg6m to Mg6m-yard"), "The Changes tab names the replacement id: " + String.Join(" | ", w.Session.Changes(yard)));
        Persistence(true, all, "After the replacement id");
        var enable = w.Session.Edit(yard, null, true);
        Assert(enable.Applied && w.Yard.Active && w.Own(w.Yard) == "S9sc|b|20", "Enable now applies and registers the Yard Limit under its replacement id: " + enable.Error + " " + w.Registrations());
        var rebind = w.Session.Bind(yard, MarkerBinding.At(new TrackLocation("pi3pzp", "a", 3)));
        Assert(rebind.Applied && w.Own(w.Yard) == "pi3pzp|a|3", "The rebind to pi3pzp applies: " + rebind.Error);
        Assert(w.Own(w.Speed) == "Sjdl|a|10" && w.Speed.Id == "Mg6m", "The speed sign stays registered under Mg6m: " + w.Registrations());
        var repaired = w.Registrations();
        w.MapEditor.Undo(); w.MapEditor.Undo(); w.MapEditor.Undo();
        Assert(w.Registrations() == before, "Undo returns the underlying id and every registration: " + w.Registrations());
        Persistence(false, new string[0], "After Undo");
        w.MapEditor.Redo(); w.MapEditor.Redo(); w.MapEditor.Redo();
        Assert(w.Registrations() == repaired, "Redo reapplies the replacement id, Enable and rebind: " + w.Registrations());
        Persistence(true, all, "After Redo");
        var removed = w.Session.Remove(yard);
        Assert(removed.Applied && w.Registrations() == before, "Remove modification returns the underlying id and every registration: " + removed.Error + " " + w.Registrations());
        w.MapEditor.Undo();
        Assert(w.Registrations() == repaired, "Undo of the removal reapplies the repair");
        // The mod's value cleared alone: the id returns while Enable and the rebind stay refused for the duplicate.
        var cleared = w.Session.SetMarkerId(yard, null, null);
        Assert(!cleared.Applied && cleared.Error.Contains("Mg6m") && w.Registrations() == repaired, "Clearing the id while Enable and the rebind need it is refused before mutation: " + cleared.Error);
        // The speed sign takes an id of its own too; Save writes both.
        Assert(w.Session.SetMarkerId(Marker(SpeedPath), "Mg6m-speed", null).Applied && w.Own(w.Speed) == "Sjdl|a|10" && w.Speed.Id == "Mg6m-speed", "An active holder takes a replacement id and keeps its registration under it: " + w.Registrations());
        Assert(w.MapEditor.Save() == null && w.Store.Info.Contains("crankyant/scene-patcher >=" + ReplacementMarkerIds.RequiredVersion) && (int)JObject.Parse(w.Store.Patch)["formatVersion"] == 1,
            "Save keeps format version 1 and raises the minimum patcher dependency: " + w.Store.Info);
        var saved = JObject.Parse(w.Store.Patch)["modifications"].Children<JProperty>().Select(p => (JObject)p.Value).ToArray();
        var yardItem = saved.Single(i => (string)i["target"]["path"] == YardPath);
        Assert(yardItem.Properties().Select(p => p.Name).SequenceEqual(new[] { "target", "markerId", "relationships", "dependsOn", "active" }) && (string)yardItem["markerId"] == "Mg6m-yard",
            "Save writes the replacement id at its place in the canonical order: " + yardItem.ToString(Newtonsoft.Json.Formatting.None));
        Persistence(false, new string[0], "After Save");
        var fresh = new World();
        var reload = fresh.Engine.Apply(w.Store.Patch);
        Assert(reload.Outcomes.Values.All(o => o.Applied) && fresh.Registrations() == w.Registrations(), "A reloaded world reproduces the replacement ids and every holder's registration: " + String.Join(" | ", reload.Outcomes.Values.Select(o => o.Error)) + " " + fresh.Registrations());
        var strict = new World(new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" });
        Assert(strict.Session.SetMarkerId(yard, "Mg6m-yard", null).Applied && strict.MapEditor.Save() == null && strict.Store.Info.Contains(">=0.9.0") && !strict.Store.Info.Contains(">=" + ReplacementMarkerIds.RequiredVersion),
            "A stricter existing constraint is kept: " + strict.Store.Info);
        // Clearing the mod's value returns the underlying id, and setting the underlying id stores nothing.
        Assert(strict.Session.SetMarkerId(yard, null, null).Applied && strict.Yard.Id == "Mg6m" && !strict.Session.HasMarkerId(yard), "Clearing the mod's value returns the underlying id");
        var same = strict.Session.SetMarkerId(yard, "Mg6m", null);
        Assert(same.Applied && (same.Unchanged || same.Reverted) && !strict.Session.HasMarkerId(yard), "Setting the underlying id stores no modification: " + same.Error);
    }
    static void ScenePatchEditingRefusesBeforeMutation() {
        foreach (var (id, expected) in new[] { ("", ReplacementMarkerIds.Empty), ("Mown", UnrelatedPath), ("water-tower-1", "map object water-tower-1"), ("passengerStop:x", "passengerStop:"), ("Mkft", WhistlePaths[0]) }) {
            var w = new World(); var before = w.Registrations(); var text = w.Session.ScenePatchFileText;
            var outcome = w.Session.SetMarkerId(Marker(YardPath), id, null);
            Assert(!outcome.Applied && outcome.Failure == FailureKind.Refused && outcome.Error.Contains(expected), "Replacement id \"" + id + "\" is refused before mutation: " + outcome.Error);
            Assert(w.Registrations() == before && w.Markers.Mutations == 0 && w.Session.ScenePatchFileText == text && !w.MapEditor.IsDirty && !w.MapEditor.CanUndo, id + ": nothing changed");
        }
        // Any sharer can receive the replacement id: here the active speed sign, then one whistle sign of the group.
        var any = new World();
        Assert(any.Session.SetMarkerId(Marker(SpeedPath), "Mg6m-speed", null).Applied && any.Own(any.Speed) == "Sjdl|a|10", "The active speed sign can receive the replacement id");
        Assert(any.Session.Edit(Marker(YardPath), null, true).Applied && any.Own(any.Yard) == "S9sc|b|20" && any.Yard.Id == "Mg6m", "With the speed sign repaired, the Yard Limit keeps Mg6m and can be enabled: " + any.Registrations());
        var group = new World(); var others = new[] { group.Own(group.Whistles[0]), group.Own(group.Whistles[1]) };
        Assert(group.Session.SetMarkerId(Marker(WhistlePaths[2]), "Mkft-c", null).Applied && group.Session.Edit(Marker(WhistlePaths[2]), null, false).Applied, "A whistle sign takes a replacement id and is disabled");
        Assert(group.Own(group.Whistles[0]) == others[0] && group.Own(group.Whistles[1]) == others[1], "The other whistle signs keep their registrations: " + group.Registrations());
        // A sign that still shares Mkft with another holder stays refused.
        var still = group.Session.Edit(Marker(WhistlePaths[1]), null, false);
        Assert(!still.Applied && still.Error.Contains("Mkft"), "The remaining pair still refuses an activity edit: " + still.Error);
    }
}
