// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RailroaderScenePatcher.Core;

// Crossing changes across a later join and a reconnect, through complete scene patch files: every peer reconstructs the
// detection and equipment of a supplying mod in its own world from the persistent identities in the scene patch file.
// One scene patch file reroutes a circuit, adds detection for a siding and adds a light or gate and a bell.
static class CrossingJoinTests {
    const string CrossingPath = "World/Crossings/Bryson Everett", OtherPath = "World/Crossings/Whittier", SignalPath = CrossingPath + "/Signal 1";
    const string CrossingBell = "rr/bell-uss-teardrop-crossing-rh", Pack = "author/pack";
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string N(double value) => value.ToString(CultureInfo.InvariantCulture);
    static string Target(string path = CrossingPath) => "\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"" + CrossingCoverageScene.ComponentType + "\",\"structure\":\"" + CrossingCoverageScene.Structure + "\"}";
    static string Loc(string segment, double distance) => "{\"segment\":\"" + segment + "\",\"end\":\"a\",\"distance\":" + N(distance) + "}";
    static string Range(string segment, double from, double to) => "{\"from\":" + Loc(segment, from) + ",\"to\":" + Loc(segment, to) + "}";
    // Approach A 308-550, island 292-308 and approach B 292-50 on one segment.
    static string Tracks(string key, string segment) => "\"" + key + "\":{\"approachA\":" + Range(segment, 308, 550) + ",\"island\":" + Range(segment, 292, 308) + ",\"approachB\":" + Range(segment, 292, 50) + "}";
    static string Placement(double x) => "\"placement\":{\"mode\":\"fixed\",\"position\":[" + N(x) + ",0,7],\"rotation\":[0,0,0,1]}";
    static string Light(string id, double x = 10, string path = SignalPath) => "\"" + id + "\":{\"pattern\":\"" + EquipmentAddition.LightGatePattern + "\",\"source\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\"}," + Placement(x) + "}";
    static string Bell(string id, double x = 20, string definition = CrossingBell) => "\"" + id + "\":{\"pattern\":\"" + EquipmentAddition.BellPattern + "\",\"source\":{\"definition\":\"" + definition + "\"}," + Placement(x) + "}";
    static string Modification(string id, string relationships, string path = CrossingPath) => "\"" + id + "\":{" + Target(path) + ",\"relationships\":{" + relationships + "},\"dependsOn\":[]}";
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + items + "}}";
    static readonly string Reroute = Modification("reroute", "\"crossingCoverage\":{\"circuits\":{" + Tracks("Pipr", "Snew") + "}}");
    static readonly string Siding = Modification("siding", "\"crossingCoverage\":{\"additions\":{" + Tracks("siding", "Sspur") + "}}");
    static readonly string Equip = Modification("equip", "\"crossingEquipment\":{" + Light("west") + "," + Bell("bell") + "}");
    static readonly string File = Doc(Reroute + "," + Siding + "," + Equip);
    static readonly string[] NativeSpanIds = { "Pcyi", "Pipr", "Pws9", "Pdlh", "P6id", "P1db", "Qa", "Qi", "Qb" };
    sealed class Rows : IPatchDiagnostics { public readonly List<PatchFailure> Failures = new List<PatchFailure>(); public void Warning(PatchFailure failure) => Failures.Add(failure); }

    // One peer's local world as its own game holds it: the crossings, the track graph, the loaded devices and bell
    // definitions, and this peer's patcher state. Nothing in it is shared with another peer.
    sealed class World {
        public readonly FakeCrossingWorld Coverage = new FakeCrossingWorld();
        public readonly FakeEquipmentWorld Equipment = new FakeEquipmentWorld();
        public FakeCrossing Crossing, Other;
        public readonly LocalPeer Local;
        public readonly Rows Diagnostics = new Rows();
        PatchRuntime runtime;
        public int Scenes;
        public World() {
            Build();
            Equipment.Device(SignalPath); Equipment.Bell();
            Local = new LocalPeer(() => { Scenes++; return new CrossingCoverageScene(Coverage, Capability.Unchecked, Equipment); });
        }
        // The crossings as the game builds them from map data: circuit Pipr on the old route and circuit P6id on another track.
        void Build() {
            Crossing = new FakeCrossing(); Other = new FakeCrossing();
            Crossing.Circuits.Add(new FakeCircuit { Spans = new[] { Coverage.Span("Pcyi", "Sold", 308, 550), Coverage.Span("Pipr", "Sold", 292, 308), Coverage.Span("Pws9", "Sold", 292, 50) } });
            Crossing.Circuits.Add(new FakeCircuit { Spans = new[] { Coverage.Span("Pdlh", "Sother", 200, 390), Coverage.Span("P6id", "Sother", 190, 200), Coverage.Span("P1db", "Sother", 190, 10) } });
            Other.Circuits.Add(new FakeCircuit { Spans = new[] { Coverage.Span("Qa", "Sother", 1, 5), Coverage.Span("Qi", "Sother", 5, 6), Coverage.Span("Qb", "Sother", 6, 9) } });
            Coverage.Objects[CrossingPath] = new List<object> { Crossing }; Coverage.Objects[OtherPath] = new List<object> { Other };
        }
        // The peer leaves the session and the game returns to its main menu: the map scenes unload, which destroys the
        // crossings with everything beneath them and every span, and the world-unload callback ends the load attempt.
        public void Leave() {
            Equipment.SceneUnloaded(Crossing); Equipment.SceneUnloaded(Other);
            Coverage.SceneUnloaded();
            Local.Unload();
        }
        // The peer joins: the game loads the map scenes, which builds new crossings and the map's own detection spans, and calls the
        // world-load callback once with the new track graph.
        public ScenePatchFileResult Join(string file = null) { Build(); return Callback(Graph = new object(), file); }
        public object Graph;
        // The game builds the crossings again while the objects of the earlier ones still stand: no route of the game is
        // known to do this, so it stands for a world the patcher has to clean up itself.
        public void RebuildInPlace() { Local.Unload(); Build(); }
        // A world-load callback: a new load attempt gets its own runtime, as the native entry point gives it.
        public ScenePatchFileResult Callback(object graph, string file = null) {
            if (Local.Enter("map", graph) != WorldEntry.Repeated || runtime == null) runtime = new PatchRuntime(Local.Engine, Diagnostics, "0.5.0", "0.6.3");
            var result = runtime.Apply(file ?? File, new PatchSource(Pack, "Pack", "1.0.0"));
            Equipment.FinishLoads();
            return result;
        }
        bool Both(string segment) => new[] { 500.0, 300, 100 }.All(d => Coverage.Detects(Crossing, segment, d));
        bool Nowhere(string segment) => !new[] { 500.0, 300, 100 }.Any(d => Coverage.Detects(Crossing, segment, d));
        public List<FakeSpan> OwnedSpans => Coverage.Graph.Values.Where(s => s.Owned).ToList();
        public string OwnedIds => String.Join(",", OwnedSpans.Select(s => s.Id).OrderBy(id => id, StringComparer.Ordinal));
        public List<FakeEquipmentWorld.Added> Live => Equipment.Live(Crossing).ToList();
        public int Observers => Equipment.All.Where(a => !a.Destroyed).Sum(a => a.Observers);
        // Detection covers the selected and added tracks in both passage directions, the untouched circuit keeps
        // detecting, and the old route no longer triggers the crossing.
        public string DetectionProblem => !Both("Snew") ? "the selected track does not detect" : !Both("Sspur") ? "the added track does not detect" : !new[] { 300.0, 195, 100 }.All(d => Coverage.Detects(Crossing, "Sother", d)) ? "the retained circuit does not detect"
            : !Nowhere("Sold") ? "the old route still detects" : null;
        // Exactly one set: three circuits on the crossing, six owned spans, one light or gate in the crossing's culler and one
        // bell with one observer and one loaded audio reference, and every detection span from the map registered.
        public string SetProblem {
            get {
                if (Crossing.Circuits.Count != 3 || Crossing.Circuits.Count(c => c.Addition != null) != 1) return "the crossing evaluates " + Crossing.Circuits.Count + " circuits";
                if (OwnedSpans.Count != 6 || OwnedSpans.Any(s => s.Destroyed)) return OwnedSpans.Count + " owned spans are registered: " + OwnedIds;
                if (OwnedSpans.Any(s => s.Id.EndsWith(".2", StringComparison.Ordinal))) return "an owned span has a suffixed ID: " + OwnedIds;
                if (!NativeSpanIds.All(id => Coverage.Graph.TryGetValue(id, out var s) && !s.Owned && !s.Destroyed)) return "a detection span from the map is not registered";
                if (Other.Circuits.Count != 1 || Equipment.Live(Other).Any()) return "the other crossing changed";
                var lights = Live.Where(a => a.Bell == null).ToList(); var bells = Live.Where(a => a.Bell != null).ToList();
                if (lights.Count != 1 || !lights[0].InCuller || lights[0].Record.Id != "west" || lights[0].Record.Scope != Pack) return lights.Count + " lights or gates stand at the crossing";
                if (bells.Count != 1 || bells[0].Observers != 1 || !bells[0].AudioLoaded || bells[0].Record.Id != "bell") return bells.Count + " bells stand at the crossing";
                if (Observers != 1 || Equipment.AudioReferences != 1 || Equipment.PendingLoads != 0) return Observers + " observers, " + Equipment.AudioReferences + " audio references and " + Equipment.PendingLoads + " pending loads remain";
                return null;
            }
        }
        public string Problem => DetectionProblem ?? SetProblem;
        // Nothing the patcher owns stands in this world.
        public string Leftover => OwnedSpans.Count != 0 ? "owned spans remain: " + OwnedIds : Equipment.All.Any(a => !a.Destroyed) ? "added equipment remains" : Observers != 0 || Equipment.AudioReferences != 0 || Equipment.PendingLoads != 0 ? "an observer, audio reference or load remains" : null;
        public string Counters => Coverage.Mutations + "/" + Coverage.Created + "/" + Equipment.Mutations + "/" + Equipment.Constructed + "/" + Scenes + "/" + Diagnostics.Failures.Count;
    }
    static string Errors(ScenePatchFileResult result) => result.Error ?? String.Join("; ", result.Outcomes.Where(o => !o.Value.Applied).Select(o => o.Key + ": " + o.Value.Error));
    static bool Applied(ScenePatchFileResult result) => result.Error == null && result.Outcomes.Count > 0 && result.Outcomes.Values.All(o => o.Applied);

    public static void Run() {
        ALaterJoinReconstructsTheCrossingOnTheJoiningPeerOnly();
        RepeatedCallbacksDuplicateNothing();
        AReconnectBuildsOneSetInTheRebuiltWorld();
        ARetainedWorldKeepsItsOneSet();
        EachSupplyingModIsReconstructedUnderItsOwnIdentities();
        AMissingPrerequisiteFailsOnThatPeerOnly();
        AFailedReconstructionStaysOnThatPeerAndIsRetriedAtTheNextJoin();
        AFailedCleanupIsReportedLocallyAndRetried();
        AnEarlyCheckKeepsTheEquipmentOfACrossingWhoseSceneIsNotLoaded();
        EndingTheStateReleasesEquipmentAndRestoresNativeCoverage();
        Console.WriteLine("PASS: crossing detection and equipment reconstructed per peer on later join and reconnect");
    }
    static void ALaterJoinReconstructsTheCrossingOnTheJoiningPeerOnly() {
        var host = new World(); var client = new World();
        var hosted = host.Join();
        Assert(Applied(hosted) && host.Problem == null, "The host builds the crossing changes in its own world: " + Errors(hosted) + " " + host.Problem);
        var before = host.Counters; var hostIds = host.OwnedIds;
        Assert(client.OwnedSpans.Count == 0 && client.Live.Count == 0 && client.Crossing.Circuits.Count == 2, "Nothing reaches the client's world before it joins");
        var joined = client.Join();
        Assert(Applied(joined) && client.Problem == null, "A later join builds detection and equipment in the client's world: " + Errors(joined) + " " + client.Problem);
        Assert(client.OwnedIds == hostIds && client.Live.All(a => ReferenceEquals(a.Controller, client.Crossing)), "The client's additions carry the same persistent identities and belong to its own crossing: " + client.OwnedIds);
        Assert(host.Counters == before && host.Problem == null && client.Diagnostics.Failures.Count == 0, "The join changes nothing in the host's world and reports nothing: " + host.Counters);
    }
    // Further world-load callbacks in one load attempt reconcile the same identities: no span, circuit, device, bell,
    // observer, audio reference or culler membership is added, and nothing is written.
    static void RepeatedCallbacksDuplicateNothing() {
        var peer = new World();
        Assert(Applied(peer.Join()), "The join applies");
        var before = peer.Counters; var circuits = peer.Crossing.Circuits.ToArray(); var live = peer.Live.ToArray();
        for (var i = 0; i < 3; i++) {
            var again = peer.Callback(peer.Graph);
            Assert(Applied(again) && peer.Problem == null, "A repeated callback reconciles: " + Errors(again) + " " + peer.Problem);
        }
        Assert(peer.Counters == before && peer.Crossing.Circuits.SequenceEqual(circuits) && peer.Live.SequenceEqual(live) && peer.Local.Reentries == 0, "Repeated callbacks write and build nothing: " + before + " -> " + peer.Counters);
    }
    // A reconnect goes through the main menu: the game destroyed the crossing with the patcher's additions. The patcher
    // forgets the old crossing without touching the new world and builds one set under the same identities.
    static void AReconnectBuildsOneSetInTheRebuiltWorld() {
        var host = new World(); var client = new World();
        host.Join(); client.Join();
        var ids = client.OwnedIds; var hostState = host.Counters; var old = client.Crossing;
        client.Leave();
        Assert(client.Leftover == null && host.Problem == null && host.Counters == hostState, "Leaving unloads the client's additions with its scenes and leaves the host as it was: " + client.Leftover);
        var rejoined = client.Join();
        var reentry = client.Local.LastReentry;
        Assert(client.Local.Reentries == 1 && reentry.Released == 1 && reentry.Retained == 0 && reentry.Failures.Count == 0 && client.Scenes == 1, "The new load attempt releases the crossing the game destroyed, without a cleanup failure: released " + reentry.Released + "; " + String.Join("; ", reentry.Failures));
        Assert(Applied(rejoined) && client.Problem == null && client.OwnedIds == ids, "The rebuilt crossing gets one set under the same identities: " + Errors(rejoined) + " " + client.Problem + " " + client.OwnedIds);
        Assert(!ReferenceEquals(client.Crossing, old) && client.Live.All(a => ReferenceEquals(a.Controller, client.Crossing)) && client.Equipment.All.Count(a => !a.Destroyed) == 2 && old.Circuits.Count == 3, "The equipment belongs to the rebuilt crossing, and the destroyed crossing is not written to");
        Assert(client.Diagnostics.Failures.Count == 0 && host.Counters == hostState, "The reconnect reports nothing and changes nothing on the host");
        client.Leave(); client.Join(); client.Leave();
        Assert(Applied(client.Join()) && client.Problem == null && client.OwnedIds == ids && client.Local.LastReentry.Failures.Count == 0, "Further reconnects keep one set: " + client.Problem);
    }
    // A new load attempt over a world the game kept, as a host that stops and starts hosting keeps its map: the crossing
    // still stands under its identity, so its additions are reconciled and nothing is built again.
    static void ARetainedWorldKeepsItsOneSet() {
        var peer = new World();
        peer.Join();
        var before = peer.Counters; var live = peer.Live.ToArray(); var ids = peer.OwnedIds;
        peer.Local.Unload();
        var again = peer.Callback(new object());
        Assert(peer.Local.LastReentry.Retained == 1 && peer.Local.LastReentry.Released == 0 && Applied(again) && peer.Problem == null, "The retained crossing reconciles: " + Errors(again) + " " + peer.Problem);
        Assert(peer.Counters == before && peer.Live.SequenceEqual(live) && peer.OwnedIds == ids, "Reconciling a retained crossing writes and builds nothing: " + before + " -> " + peer.Counters);
    }
    // Two supplying mods use the same addition IDs at one crossing. After a reconnect each mod's additions exist once, under
    // that mod's scope.
    static void EachSupplyingModIsReconstructedUnderItsOwnIdentities() {
        var peer = new World();
        peer.Coverage.Segments["Sloop"] = new List<double> { 600 };
        var other = Doc(Modification("siding", "\"crossingCoverage\":{\"additions\":{" + Tracks("siding", "Sloop") + "}}") + "," + Modification("equip", "\"crossingEquipment\":{" + Light("west", 30) + "}"));
        string Twice() {
            var second = peer.Local.Engine.Apply(other, "other/pack");
            if (!Applied(second)) return Errors(second);
            var lights = peer.Live.Where(a => a.Bell == null).Select(a => a.Record.Scope + " " + a.Record.Id + " " + N(a.Position[0])).OrderBy(s => s, StringComparer.Ordinal);
            if (!lights.SequenceEqual(new[] { Pack + " west 10", "other/pack west 30" })) return "lights: " + String.Join(", ", peer.Live.Where(a => a.Bell == null).Select(a => a.Record.Scope + " " + a.Record.Id));
            if (peer.OwnedSpans.Count != 9 || peer.Crossing.Circuits.Count != 4 || peer.OwnedSpans.Any(s => s.Id.EndsWith(".2", StringComparison.Ordinal))) return "owned spans: " + peer.OwnedIds;
            return new[] { 500.0, 300, 100 }.All(d => peer.Coverage.Detects(peer.Crossing, "Sloop", d)) ? peer.DetectionProblem : "the other mod's track does not detect";
        }
        Assert(Applied(peer.Join()) && Twice() == null, "Both mods apply at the first join: " + Twice());
        var ids = peer.OwnedIds;
        peer.Leave();
        Assert(Applied(peer.Join()) && Twice() == null && peer.OwnedIds == ids, "After a reconnect each mod's additions exist once under its own scope: " + Twice());
        Assert(Twice() == null && peer.Equipment.All.Count(a => !a.Destroyed) == 3, "A repeated application of the second mod adds nothing");
    }
    // A prerequisite that one peer lacks refuses that modification on that peer before any change. Its other modifications
    // apply, and the other peer has the whole set.
    static void AMissingPrerequisiteFailsOnThatPeerOnly() {
        var host = new World(); host.Join();
        var hostState = host.Counters;
        var noBell = new World(); noBell.Equipment.Bells.Clear();
        var result = noBell.Join();
        Assert(!result.Outcomes["equip"].Applied && result.Outcomes["equip"].Error.Contains("not available in the content library") && noBell.Equipment.Mutations == 0 && noBell.Live.Count == 0, "A bell definition this peer lacks refuses the equipment before any change, without a substitute: " + result.Outcomes["equip"].Error);
        Assert(result.Outcomes["reroute"].Applied && result.Outcomes["siding"].Applied && noBell.DetectionProblem == null, "Detection is reconstructed on that peer all the same: " + Errors(result) + " " + noBell.DetectionProblem);
        var noTrack = new World(); noTrack.Coverage.Segments.Remove("Sspur");
        result = noTrack.Join();
        Assert(!result.Outcomes["siding"].Applied && result.Outcomes["siding"].Error.Contains("track segment Sspur is missing") && result.Outcomes["reroute"].Applied && result.Outcomes["equip"].Applied && noTrack.OwnedSpans.Count == 3 && noTrack.Crossing.Circuits.Count == 2,
            "A track this peer lacks refuses the detection addition and builds no span for it: " + Errors(result));
        var twin = new World();
        twin.Local.Enter("map", new object());
        twin.Coverage.Objects[CrossingPath].Add(new FakeCrossing());
        result = twin.Local.Engine.Apply(File, Pack);
        Assert(result.Outcomes.Values.All(o => !o.Applied && o.Error.Contains("Target must resolve uniquely")) && twin.Coverage.Mutations == 0 && twin.Equipment.Mutations == 0, "A crossing identity that is ambiguous on this peer changes nothing: " + Errors(result));
        Assert(host.Counters == hostState && host.Problem == null && noBell.Diagnostics.Failures.Count == 1 && noBell.Diagnostics.Failures[0].Operation == "equip" && noBell.Diagnostics.Failures[0].ModIdentifier == Pack, "Each failure is reported once on its own peer, and the host keeps its set");
    }
    // The equipment fails while the client builds it. The client restores what the failed modification did, keeps its
    // detection, reports once and does not retry in that load attempt. Its next join retries. The host never notices.
    static void AFailedReconstructionStaysOnThatPeerAndIsRetriedAtTheNextJoin() {
        var host = new World(); var client = new World();
        host.Join();
        var hostState = host.Counters;
        client.Equipment.FailConstructAfter = 1;
        var joined = client.Join();
        var failed = joined.Outcomes["equip"];
        Assert(!failed.Applied && failed.Failure == FailureKind.Restored && client.Live.Count == 0 && client.Observers == 0 && client.Equipment.AudioReferences == 0 && client.Equipment.PendingLoads == 0, "The failed equipment is undone, with the light or gate it had already built: " + failed.Error);
        Assert(joined.Outcomes["reroute"].Applied && joined.Outcomes["siding"].Applied && client.DetectionProblem == null && client.OwnedSpans.Count == 6, "The client's detection applies beside the failure: " + Errors(joined));
        Assert(client.Diagnostics.Failures.Count == 1 && client.Diagnostics.Failures[0].Operation == "equip" && host.Diagnostics.Failures.Count == 0 && host.Counters == hostState && host.Problem == null, "The failure is one local warning; the host keeps its world and hears nothing");
        var built = client.Equipment.Constructed;
        var repeated = client.Callback(client.Graph);
        Assert(repeated.Outcomes["equip"].Failure == FailureKind.AlreadyFailed && client.Equipment.Constructed == built && client.Diagnostics.Failures.Count == 1 && client.Local.Engine != null, "A repeated callback neither retries nor reports again, and the client's state stays available");
        client.Leave();
        var rejoined = client.Join();
        Assert(Applied(rejoined) && client.Problem == null && client.Local.LastReentry.Failures.Count == 0, "The next join retries and builds the whole set: " + Errors(rejoined) + " " + client.Problem);
        Assert(host.Counters == hostState, "The host is still untouched");
    }
    // A crossing is rebuilt while its old additions still stand and cannot be destroyed. The failure names the crossing,
    // stays on this peer, leaves the patcher's state and the rebuilt crossing usable, and the next load attempt finishes it.
    static void AFailedCleanupIsReportedLocallyAndRetried() {
        var host = new World(); var peer = new World();
        host.Join(); peer.Join();
        var hostState = host.Counters;
        peer.Equipment.StickyDestroy = true;
        peer.RebuildInPlace();
        var result = peer.Callback(new object());
        var report = peer.Local.LastReentry;
        Assert(report.Released == 0 && report.Failures.Count == 1 && report.Failures[0].Contains(CrossingPath) && report.Failures[0].Contains("added equipment remains"), "Equipment that stays is reported for its crossing: " + String.Join("; ", report.Failures));
        Assert(peer.Local.ReentryError == null && peer.Scenes == 1 && host.Counters == hostState && host.Problem == null, "The cleanup failure keeps this peer's state and does not reach the host");
        Assert(result.Outcomes["reroute"].Applied && result.Outcomes["siding"].Applied && peer.DetectionProblem == null && peer.OwnedSpans.Count == 6, "The rebuilt crossing gets its detection under the original span IDs: " + Errors(result) + " " + peer.OwnedIds);
        peer.Equipment.StickyDestroy = false;
        peer.Local.Unload();
        result = peer.Callback(new object());
        report = peer.Local.LastReentry;
        Assert(report.Released == 1 && report.Retained == 1 && report.Failures.Count == 0 && Applied(result) && peer.Problem == null, "The next load attempt removes the old equipment and leaves one set: " + String.Join("; ", report.Failures) + Errors(result) + " " + peer.Problem);
    }
    // A native check opens the load attempt while the crossing's scene is not reported as loaded. The crossing exists, so
    // its equipment, observer and audio stay.
    static void AnEarlyCheckKeepsTheEquipmentOfACrossingWhoseSceneIsNotLoaded() {
        var peer = new World();
        peer.Join();
        var before = peer.Counters; var live = peer.Live.ToArray();
        peer.Local.Unload();
        peer.Crossing.SceneLoaded = false;
        peer.Local.Prepare("map");
        Assert(peer.Local.LastReentry.Retained == 1 && peer.Local.LastReentry.Released == 0 && peer.Local.LastReentry.Failures.Count == 0 && peer.SetProblem == null, "The early check keeps the crossing and its equipment: " + peer.SetProblem);
        peer.Crossing.SceneLoaded = true;
        var again = peer.Callback(new object());
        Assert(Applied(again) && peer.Problem == null && peer.Counters == before && peer.Live.SequenceEqual(live) && peer.Local.Reentries == 1, "The world-load callback joins that attempt and builds nothing: " + Errors(again) + " " + before + " -> " + peer.Counters);
    }
    // The patcher's state ends while the scene stays, as when the mod unloads: the equipment goes with its observer and
    // audio, the crossing evaluates its native circuits again, and a new state builds one set.
    static void EndingTheStateReleasesEquipmentAndRestoresNativeCoverage() {
        var peer = new World();
        peer.Join();
        var ids = peer.OwnedIds;
        peer.Local.Dispose();
        Assert(peer.Leftover == null && peer.Crossing.Circuits.Count == 2 && String.Join(",", peer.Crossing.Circuits.SelectMany(c => c.Spans).Select(s => s.Id)) == "Pcyi,Pipr,Pws9,Pdlh,P6id,P1db", "Ending the state leaves nothing of the patcher and returns the native circuits: " + peer.Leftover);
        Assert(new[] { 500.0, 300, 100 }.All(d => peer.Coverage.Detects(peer.Crossing, "Sold", d)), "The native route detects again");
        var again = peer.Callback(new object());
        Assert(peer.Scenes == 2 && Applied(again) && peer.Problem == null && peer.OwnedIds == ids, "A new state builds one set under the same identities: " + Errors(again) + " " + peer.Problem);
    }
}
