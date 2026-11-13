// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// A detection span: the track between two locations, registered in the graph under its ID while it is enabled.
class FakeSpan {
    public string Id; public SpanRange Range; public bool Owned, Destroyed;
}
// One circuit set of a crossing controller: approach A, island and approach B.
class FakeCircuit { public FakeSpan[] Spans; }
class FakeCrossing {
    public List<FakeCircuit> Circuits = new List<FakeCircuit>();
    public double[] Position = { 5, 0, 7 }, Rotation = { 0, 0, 0, 1 };
    public string Pattern;
}
// Controlled crossing world: controllers by path, segments by ID and the graph's span lookup by ID, as the native graph keeps them.
// A train on a track location is detected by the crossing when one of its evaluated circuits holds a span covering that location.
class FakeCrossingWorld : ICrossingCoverageWorld {
    public string[] Maps = { "map" };
    public IEnumerable<string> MapIds => Maps;
    public Dictionary<string, List<object>> Objects = new Dictionary<string, List<object>>();
    public Dictionary<string, List<double>> Segments = new Dictionary<string, List<double>> {
        ["Sold"] = new List<double> { 600 }, ["Snew"] = new List<double> { 600 }, ["Sother"] = new List<double> { 400 }, ["Stwin"] = new List<double> { 100, 100 }
    };
    public readonly Dictionary<string, FakeSpan> Graph = new Dictionary<string, FakeSpan>();
    public int Mutations, Created;
    // Failure injections.
    public int FailCreateAfter = -1;
    public bool FailSetCircuits, StickyDestroy, FailSetSpansOnce, SkipRegisterOnCreate;
    public FakeSpan Span(string id, string segment, double from, double to) {
        var span = new FakeSpan { Id = id, Range = new SpanRange(new TrackLocation(segment, "a", from), new TrackLocation(segment, "a", to)) };
        Graph[id] = span;
        return span;
    }
    public IReadOnlyList<object> Resolve(TargetIdentity identity) {
        if (identity.Component != CrossingCoverageScene.ComponentType || (identity.Structure != null && identity.Structure != CrossingCoverageScene.Structure)) return Array.Empty<object>();
        return Objects.TryGetValue(identity.Path, out var found) ? found : new List<object>();
    }
    public string PatternProblem(object controller) => ((FakeCrossing)controller).Pattern;
    public IReadOnlyList<object> Circuits(object controller) => ((FakeCrossing)controller).Circuits.ToArray();
    public IReadOnlyList<object> Spans(object circuit) => ((FakeCircuit)circuit).Spans;
    public string SpanId(object span) => ((FakeSpan)span).Id;
    public SpanRange Range(object span) => ((FakeSpan)span).Range;
    public bool Registered(object span) => Graph.TryGetValue(((FakeSpan)span).Id, out var found) && ReferenceEquals(found, span);
    public bool IdRegistered(string id) => Graph.ContainsKey(id);
    public bool Covers(object span) { var s = (FakeSpan)span; return !s.Destroyed && Valid(s.Range.From) && Valid(s.Range.To); }
    bool Valid(TrackLocation l) => Segments.TryGetValue(l.Segment, out var lengths) && lengths.Count == 1 && l.Distance >= 0 && l.Distance <= lengths[0];
    public IReadOnlyList<double> SegmentLengths(string segment) => Segments.TryGetValue(segment, out var lengths) ? lengths : new List<double>();
    public object CreateSpan(object controller, string id, SpanRange range) {
        Mutations++;
        if (FailCreateAfter == 0) throw new Exception("span creation injected");
        if (FailCreateAfter > 0) FailCreateAfter--;
        var span = new FakeSpan { Id = id, Range = range, Owned = true };
        if (SkipRegisterOnCreate) { SkipRegisterOnCreate = false; Created++; return span; }
        if (Graph.ContainsKey(id)) throw new Exception("duplicate span id " + id);
        Graph[id] = span; Created++;
        return span;
    }
    public void DestroySpan(object span) {
        Mutations++;
        var s = (FakeSpan)span;
        if (StickyDestroy) return;
        if (Graph.TryGetValue(s.Id, out var found) && ReferenceEquals(found, s)) Graph.Remove(s.Id);
        s.Destroyed = true;
    }
    public void SetSpans(object circuit, object approachA, object island, object approachB) {
        Mutations++;
        ((FakeCircuit)circuit).Spans = new[] { (FakeSpan)approachA, (FakeSpan)island, (FakeSpan)approachB };
        if (FailSetSpansOnce) { FailSetSpansOnce = false; throw new Exception("span reference injected"); }
    }
    public void SetCircuits(object controller, IReadOnlyList<object> circuits) {
        Mutations++;
        if (FailSetCircuits) { FailSetCircuits = false; ((FakeCrossing)controller).Circuits.Clear(); throw new Exception("circuit list injected"); }
        ((FakeCrossing)controller).Circuits = circuits.Cast<FakeCircuit>().ToList();
    }
    public double[] Position(object controller) => (double[])((FakeCrossing)controller).Position.Clone();
    public double[] Rotation(object controller) => (double[])((FakeCrossing)controller).Rotation.Clone();
    public bool Active(object controller) => true;
    // Whether a car at the location occupies a registered span of an evaluated circuit, as the controller's loop finds it.
    public bool Detects(FakeCrossing crossing, string segment, double distance) =>
        crossing.Circuits.Any(c => c.Spans.Any(s => Registered(s) && Covers(s) && Inside(s.Range, segment, distance)));
    // Whether a car at the location occupies any registered span, whether or not a crossing evaluates it.
    public bool Occupies(string segment, double distance) => Graph.Values.Any(s => Covers(s) && Inside(s.Range, segment, distance));
    static bool Inside(SpanRange r, string segment, double distance) =>
        r.From.Segment == segment && r.To.Segment == segment && distance >= Math.Min(r.From.Distance, r.To.Distance) && distance <= Math.Max(r.From.Distance, r.To.Distance);
}
// Inspection of crossing controllers with copied values, as the native inventory observes them.
class FakeCrossingInspection : IInspectionScene {
    readonly FakeCrossingWorld world;
    public FakeCrossingInspection(FakeCrossingWorld world) { this.world = world; }
    public IEnumerable<string> MapIds => world.MapIds;
    public IEnumerable<SceneObservation> Discover() => world.Objects.SelectMany(o => o.Value.Select(target => {
        var c = (FakeCrossing)target;
        var bindings = c.Circuits.Select((circuit, i) => "Circuit " + i + " " + String.Join(",", circuit.Spans.Select(s => s.Id))).ToArray();
        return new SceneObservation(c, new TargetIdentity("BushnellWhittier", o.Key, CrossingCoverageScene.ComponentType, CrossingCoverageScene.Structure), "Crossing controller", true,
            world.Position(c), world.Rotation(c), bindings, Array.Empty<string>(), Array.Empty<string>(), "coverage");
    }));
}
static class CoverageTests {
    const string CrossingPath = "World/Crossings/Bryson Everett", OtherPath = "World/Crossings/Whittier";
    static readonly TargetIdentity Crossing = new TargetIdentity("BushnellWhittier", CrossingPath, CrossingCoverageScene.ComponentType, CrossingCoverageScene.Structure);
    static readonly TargetIdentity Other = new TargetIdentity("BushnellWhittier", OtherPath, CrossingCoverageScene.ComponentType, CrossingCoverageScene.Structure);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Target(string path = CrossingPath) => "\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"" + CrossingCoverageScene.ComponentType + "\",\"structure\":\"" + CrossingCoverageScene.Structure + "\"}";
    static string N(double value) => value.ToString(CultureInfo.InvariantCulture);
    static string Loc(string segment, double distance) => "{\"segment\":\"" + segment + "\",\"end\":\"a\",\"distance\":" + N(distance) + "}";
    static string Range(string segment, double from, double to) => "{\"from\":" + Loc(segment, from) + ",\"to\":" + Loc(segment, to) + "}";
    // The reroute fixture: approach A 308-550, island 292-308, approach B 292-50 on one segment.
    static string Tracks(string circuit, string segment = "Snew") => "{\"circuit\":\"" + circuit + "\",\"approachA\":" + Range(segment, 308, 550) + ",\"island\":" + Range(segment, 292, 308) + ",\"approachB\":" + Range(segment, 292, 50) + "}";
    static string Detached(string circuit) => "{\"circuit\":\"" + circuit + "\",\"detach\":true}";
    static string Cover(string id, string circuits, string path = CrossingPath, string deps = "[]", string extra = "") =>
        "{\"id\":\"" + id + "\"," + Target(path) + ",\"relationships\":{\"crossingCoverage\":[" + circuits + "]}" + extra + ",\"dependsOn\":" + deps + "}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":[" + items + "]}";
    static CircuitCoverage Selected(string segment = "Snew") => CircuitCoverage.Tracks(R(segment, 308, 550), R(segment, 292, 308), R(segment, 292, 50));
    static SpanRange R(string segment, double from, double to) => new SpanRange(new TrackLocation(segment, "a", from), new TrackLocation(segment, "a", to));
    sealed class Fixture {
        public FakeCrossingWorld World = new FakeCrossingWorld();
        public FakeCrossing Crossing = new FakeCrossing(), Other = new FakeCrossing();
        public FakeCircuit Rerouted, Retained;
        public FakeSpan[] OldSpans;
        public CrossingCoverageScene Scene;
        public Fixture() {
            // Circuit Pipr covers the old route; circuit P6id covers another track of the same crossing.
            OldSpans = new[] { World.Span("Pcyi", "Sold", 308, 550), World.Span("Pipr", "Sold", 292, 308), World.Span("Pws9", "Sold", 292, 50) };
            Rerouted = new FakeCircuit { Spans = OldSpans };
            Retained = new FakeCircuit { Spans = new[] { World.Span("Pdlh", "Sother", 200, 390), World.Span("P6id", "Sother", 190, 200), World.Span("P1db", "Sother", 190, 10) } };
            Crossing.Circuits.Add(Rerouted); Crossing.Circuits.Add(Retained);
            Other.Circuits.Add(new FakeCircuit { Spans = new[] { World.Span("Qa", "Sother", 1, 5), World.Span("Qi", "Sother", 5, 6), World.Span("Qb", "Sother", 6, 9) } });
            World.Objects[CrossingPath] = new List<object> { Crossing }; World.Objects[OtherPath] = new List<object> { Other };
            Scene = new CrossingCoverageScene(World, Capability.Unchecked);
        }
        public string Spans(FakeCircuit circuit) => String.Join(",", circuit.Spans.Select(s => s.Id));
        public bool Native => Crossing.Circuits.Count == 2 && ReferenceEquals(Crossing.Circuits[0], Rerouted) && ReferenceEquals(Crossing.Circuits[1], Retained)
            && Spans(Rerouted) == "Pcyi,Pipr,Pws9" && Spans(Retained) == "Pdlh,P6id,P1db" && OwnedRegistered == 0;
        // Owned spans registered for this crossing's circuits; the other crossing's circuit is Qi.
        public int OwnedRegistered => World.Graph.Values.Count(s => s.Owned && !s.Id.StartsWith("CrankyAnt.ScenePatcher.Qi.", StringComparison.Ordinal));
        // A passage over the old route, the new route and the retained track, each checked at both approaches and the island.
        public bool OldRouteDetects => new[] { 500.0, 300, 100 }.Any(d => World.Detects(Crossing, "Sold", d));
        public bool NewRouteDetectsBothWays => new[] { 500.0, 300, 100 }.All(d => World.Detects(Crossing, "Snew", d));
        public bool RetainedDetectsBothWays => new[] { 300.0, 195, 100 }.All(d => World.Detects(Crossing, "Sother", d));
        public bool OldRouteStillOccupied => new[] { 500.0, 300, 100 }.All(d => World.Occupies("Sold", d));
    }
    public static void Run() {
        DocumentDistinguishesOmittedSelectedAndDetached();
        ExplicitAlternativeTransfersDetectionAndRetainsCoverage();
        DetachStopsDetectionAndKeepsOthers();
        RepetitionAndReplacementKeepOneOwnedSet();
        UnresolvedAndObsoleteReferencesChangeNothing();
        MapMismatchRejectsWholeDocument();
        PrerequisitesResolveRegardlessOfOrder();
        UnsupportedSettingsAreRefusedBeforeMutation();
        PreflightRefusalLeavesCoverageUnchanged();
        FailureAfterMutationRestoresCoverage();
        IncompleteCleanupGuardsRetry();
        EditorPreviewSelectUndoRedoAndRemoval();
        EditorRefusalAndFailureRestore();
        SaveAndReloadReproduceCoverageOnce();
        Console.WriteLine("PASS: crossing coverage omission, explicit selection, detachment, retention, refusal, restoration and authoring");
    }
    static void DocumentDistinguishesOmittedSelectedAndDetached() {
        var f = new Fixture();
        var result = new PatchEngine(f.Scene).Apply(Doc(Cover("reroute", Tracks("Pipr") + "," + Detached("P6id"))));
        Assert(result.Error == null && result.Outcomes["reroute"].Applied, "Selected and detached circuits apply: " + result.Error + result.Outcomes["reroute"].Error);
        var observed = f.Scene.Read(f.Crossing).Coverage;
        Assert(observed["Pipr"].Kind == CircuitKind.Tracks && observed["P6id"].Kind == CircuitKind.Detached, "The document distinguishes explicit selection from explicit detachment");
        f = new Fixture();
        result = new PatchEngine(f.Scene).Apply(Doc(Cover("reroute", Tracks("Pipr"))));
        observed = f.Scene.Read(f.Crossing).Coverage;
        Assert(result.Outcomes["reroute"].Applied && observed["P6id"].Kind == CircuitKind.Native && f.Spans(f.Retained) == "Pdlh,P6id,P1db", "An omitted circuit keeps its native coverage");
        Assert(f.Scene.Read(f.Other).Coverage["Qi"].Kind == CircuitKind.Native, "An omitted crossing keeps its coverage");
        foreach (var invalid in new[] { "", Detached("Pipr").Replace("true", "false"), Detached("Pipr").Replace("}", ",\"island\":" + Range("Snew", 1, 2) + "}"),
                Tracks("Pipr").Replace(",\"approachB\":" + Range("Snew", 292, 50), ""), Tracks("Pipr") + "," + Detached("Pipr"), "{\"detach\":true}", "null", "{\"circuit\":\"Pipr\"}" }) {
            f = new Fixture();
            var refused = new PatchEngine(f.Scene).Apply(Doc(Cover("bad", invalid)));
            Assert(!refused.Outcomes["bad"].Applied && f.World.Mutations == 0 && f.Native, "An empty, partial, duplicated, false or mixed coverage is neither a selection nor a detach: " + invalid + " -> " + refused.Outcomes["bad"].Error);
        }
        f = new Fixture();
        var future = new PatchEngine(f.Scene).Apply(Doc(Cover("future", Tracks("Pipr").Replace("\"circuit\":\"Pipr\"", "\"circuit\":\"Pipr\",\"bell\":true"))));
        Assert(!future.Outcomes["future"].Applied && future.Outcomes["future"].Error.StartsWith("Unsupported", StringComparison.Ordinal) && f.Native, "Unknown circuit content is preserved as unsupported, not executed");
    }
    static void ExplicitAlternativeTransfersDetectionAndRetainsCoverage() {
        var f = new Fixture();
        Assert(f.OldRouteDetects && f.RetainedDetectsBothWays && !f.NewRouteDetectsBothWays, "Before the edit the valid old route triggers the crossing");
        var result = new PatchEngine(f.Scene).Apply(Doc(Cover("reroute", Tracks("Pipr"))));
        Assert(result.Outcomes["reroute"].Applied, "The explicit alternative applies: " + result.Outcomes["reroute"].Error);
        Assert(f.NewRouteDetectsBothWays, "The selected tracks trigger the crossing at both approaches and the island");
        Assert(!f.OldRouteDetects && f.OldRouteStillOccupied, "The obsolete route stops triggering although its spans stay valid and registered");
        Assert(f.OldSpans.All(s => f.World.Registered(s) && f.World.Covers(s)), "The native spans are not unregistered or changed");
        Assert(f.RetainedDetectsBothWays && f.Spans(f.Retained) == "Pdlh,P6id,P1db", "The untouched circuit keeps its coverage and function");
        Assert(ReferenceEquals(f.Crossing.Circuits[0], f.Rerouted) && f.Crossing.Circuits.Count == 2, "The circuit keeps its identity and order");
        Assert(f.OwnedRegistered == 3 && f.Rerouted.Spans.All(s => s.Owned && s.Id.StartsWith("CrankyAnt.ScenePatcher.Pipr.", StringComparison.Ordinal)), "Three owned spans cover the selected tracks: " + f.Spans(f.Rerouted));
        Assert(f.Crossing.Position.SequenceEqual(new double[] { 5, 0, 7 }), "Coverage never moves the crossing");
        Assert(f.Scene.Read(f.Crossing).Coverage["Pipr"].Ranges[2].To.Distance == 50, "The observed coverage reports the selected tracks");
    }
    static void DetachStopsDetectionAndKeepsOthers() {
        var f = new Fixture();
        var result = new PatchEngine(f.Scene).Apply(Doc(Cover("detach", Detached("Pipr"))));
        Assert(result.Outcomes["detach"].Applied && f.Crossing.Circuits.Count == 1 && ReferenceEquals(f.Crossing.Circuits[0], f.Retained), "A detached circuit leaves the crossing's evaluation: " + result.Outcomes["detach"].Error);
        Assert(!f.OldRouteDetects && f.OldRouteStillOccupied && f.RetainedDetectsBothWays, "The detached route stops triggering; its spans stay registered and the other circuit works");
        Assert(f.Spans(f.Rerouted) == "Pcyi,Pipr,Pws9" && f.OwnedRegistered == 0, "Detach creates nothing and keeps the native span references");
        f = new Fixture();
        var all = new PatchEngine(f.Scene).Apply(Doc(Cover("all", Detached("Pipr") + "," + Detached("P6id"))));
        Assert(!all.Outcomes["all"].Applied && all.Outcomes["all"].Error.Contains("without detection") && f.World.Mutations == 0 && f.Native, "Detaching every circuit is refused before mutation");
        // The rule holds for the effective coverage: a later modification may not detach the last circuit an earlier one left.
        f = new Fixture();
        var split = new PatchEngine(f.Scene).Apply(Doc(Cover("first", Detached("P6id")) + "," + Cover("second", Detached("Pipr"), deps: "[\"first\"]")));
        Assert(split.Outcomes["first"].Applied && !split.Outcomes["second"].Applied && split.Outcomes["second"].Error.Contains("without detection"), "Detaching the remaining circuit in a later modification is refused: " + split.Outcomes["second"].Error);
        Assert(f.Crossing.Circuits.Count == 1 && ReferenceEquals(f.Crossing.Circuits[0], f.Rerouted) && f.OldRouteDetects, "The refused modification leaves the remaining circuit covered");
        // Selected tracks for one circuit and a detach of the other keep detection, so they apply together.
        f = new Fixture();
        var mixed = new PatchEngine(f.Scene).Apply(Doc(Cover("mixed", Tracks("Pipr") + "," + Detached("P6id"))));
        Assert(mixed.Outcomes["mixed"].Applied && f.Crossing.Circuits.Count == 1 && f.NewRouteDetectsBothWays, "One covered circuit is enough");
    }
    static void RepetitionAndReplacementKeepOneOwnedSet() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        engine.Apply(Doc(Cover("reroute", Tracks("Pipr"))));
        var spans = f.Rerouted.Spans.ToArray();
        var again = engine.Apply(Doc(Cover("reroute", Tracks("Pipr"))));
        Assert(again.Outcomes["reroute"].Applied && f.Rerouted.Spans.SequenceEqual(spans) && f.OwnedRegistered == 3 && f.World.Created == 3, "Repeated application reuses the owned spans");
        var moved = new PatchEngine(f.Scene).Apply(Doc(Cover("reroute", "{\"circuit\":\"Pipr\",\"approachA\":" + Range("Sother", 350, 395) + ",\"island\":" + Range("Sother", 340, 350) + ",\"approachB\":" + Range("Sother", 340, 220) + "}")));
        Assert(moved.Outcomes["reroute"].Applied && f.OwnedRegistered == 3 && spans.All(s => s.Destroyed && !f.World.Registered(s)), "Other selected tracks replace the owned spans without leaving the earlier ones: " + moved.Outcomes["reroute"].Error);
        Assert(new[] { 380.0, 345, 250 }.All(d => f.World.Detects(f.Crossing, "Sother", d)) && !f.NewRouteDetectsBothWays, "The replacement coverage triggers; the earlier selection does not");
    }
    static void UnresolvedAndObsoleteReferencesChangeNothing() {
        foreach (var (circuits, reason) in new[] {
                (Tracks("Pipr", "Smissing"), "missing"), (Tracks("Pipr", "Stwin"), "ambiguous"),
                (Tracks("Pipr").Replace("550", "650"), "outside"), (Tracks("Pzzz"), "not a circuit"),
                (Tracks("Pipr").Replace(Range("Snew", 292, 308), Range("Snew", 300, 300)), "no length") }) {
            var f = new Fixture();
            var result = new PatchEngine(f.Scene).Apply(Doc(Cover("bad", circuits) + "," + Cover("other", Detached("Qi").Replace("\"detach\":true", "\"approachA\":" + Range("Sother", 1, 5) + ",\"island\":" + Range("Sother", 5, 6) + ",\"approachB\":" + Range("Sother", 6, 9)), OtherPath)));
            Assert(!result.Outcomes["bad"].Applied && result.Outcomes["bad"].Error.Contains(reason), "An unresolved or obsolete reference is refused (" + reason + "): " + result.Outcomes["bad"].Error);
            Assert(f.Native && f.OldRouteDetects, "The refused crossing keeps its coverage (" + reason + ")");
            Assert(result.Outcomes["other"].Applied, "An independent crossing still applies (" + reason + "): " + result.Outcomes["other"].Error);
        }
    }
    static void MapMismatchRejectsWholeDocument() {
        var f = new Fixture();
        var result = new PatchEngine(f.Scene).Apply(Doc(Cover("reroute", Tracks("Pipr")), "other-map"));
        Assert(result.Error != null && result.Outcomes.Count == 0 && f.World.Mutations == 0 && f.Native, "A document for another map changes no coverage");
    }
    static void PrerequisitesResolveRegardlessOfOrder() {
        var f = new Fixture();
        var result = new PatchEngine(f.Scene).Apply(Doc(Cover("dependent", Detached("P6id"), deps: "[\"base\"]") + "," + Cover("base", Tracks("Pipr"))));
        Assert(result.Outcomes["dependent"].Applied && result.Outcomes["base"].Applied, "A coverage edit listed before its prerequisite applies: " + result.Outcomes["dependent"].Error);
        Assert(f.NewRouteDetectsBothWays && f.Crossing.Circuits.Count == 1, "Both coverage edits take effect");
        f = new Fixture();
        result = new PatchEngine(f.Scene).Apply(Doc(Cover("dependent", Tracks("Pipr"), deps: "[\"base\"]") + "," + Cover("base", Tracks("Pzzz"), OtherPath)));
        Assert(!result.Outcomes["dependent"].Applied && result.Outcomes["dependent"].Error.Contains("Dependency failed") && f.Native, "A coverage edit whose prerequisite fails changes nothing");
    }
    static void UnsupportedSettingsAreRefusedBeforeMutation() {
        var f = new Fixture();
        var placement = ",\"placement\":{\"mode\":\"fixed\",\"position\":[1,2,3],\"rotation\":[0,0,0,1]}";
        var result = new PatchEngine(f.Scene).Apply(Doc(Cover("moved", Tracks("Pipr"), extra: placement)));
        Assert(!result.Outcomes["moved"].Applied && result.Outcomes["moved"].Error.Contains("placement") && f.World.Mutations == 0 && f.Native, "Placement of a crossing is refused before mutation: " + result.Outcomes["moved"].Error);
        result = new PatchEngine(f.Scene).Apply(Doc(Cover("marker", Tracks("Pipr")).Replace("\"crossingCoverage\"", "\"trackMarker\":{\"detach\":true},\"crossingCoverage\"")));
        Assert(!result.Outcomes["marker"].Applied && f.World.Mutations == 0 && f.Native, "A track-marker relationship on a crossing is refused");
        // Another capability refuses crossing coverage instead of ignoring it.
        var markers = new FakeMarkerWorld();
        var sign = new FakeMarker { Id = "Mi4f", Segment = "S9sc", Distance = 5 };
        markers.Objects["sign"] = new List<object> { sign }; markers.Load(sign);
        var capabilities = new SceneCapabilities(() => new[] { "map" });
        capabilities.Add(new TrackMarkerScene(markers, Capability.Unchecked)); capabilities.Add(f.Scene);
        var marker = "{\"id\":\"m\",\"target\":{\"scene\":\"s\",\"path\":\"sign\",\"component\":\"Track.TrackMarker\"},\"relationships\":{\"crossingCoverage\":[" + Detached("Pipr") + "]},\"dependsOn\":[]}";
        result = new PatchEngine(capabilities).Apply(Doc(marker));
        Assert(!result.Outcomes["m"].Applied && result.Outcomes["m"].Error.Contains("crossing-coverage") && markers.Mutations == 0, "A track marker refuses crossing coverage: " + result.Outcomes["m"].Error);
        result = new PatchEngine(new Scene { Objects = { ["p"] = new List<object> { new Box() } } }).Apply(Doc("{\"id\":\"b\",\"target\":{\"scene\":\"s\",\"path\":\"p\",\"component\":\"c\"},\"relationships\":{\"crossingCoverage\":[" + Detached("Pipr") + "]},\"dependsOn\":[]}"));
        Assert(!result.Outcomes["b"].Applied && result.Outcomes["b"].Error.Contains("crossing-coverage"), "An object without relationships refuses crossing coverage");
        Assert(f.Scene.MinimumPatcherVersion(Crossing) == "0.5.0", "The crossing capability names the version that introduced it");
    }
    static void PreflightRefusalLeavesCoverageUnchanged() {
        var f = new Fixture();
        f.Crossing.Pattern = "The crossing controller holds no circuit list";
        var result = new PatchEngine(f.Scene).Apply(Doc(Cover("reroute", Tracks("Pipr"))));
        Assert(!result.Outcomes["reroute"].Applied && f.World.Mutations == 0 && f.Native, "A pattern refusal changes nothing");
        f = new Fixture();
        f.Scene.Preflight(f.Crossing);
        // A native edit replaced a span after the patcher first observed the crossing.
        f.Retained.Spans = new[] { f.Retained.Spans[0], f.World.Span("Pforeign", "Sother", 190, 200), f.Retained.Spans[2] };
        f.World.Mutations = 0;
        result = new PatchEngine(f.Scene).Apply(Doc(Cover("reroute", Tracks("Pipr"))));
        Assert(!result.Outcomes["reroute"].Applied && result.Outcomes["reroute"].Error.Contains("Reload") && f.World.Mutations == 0 && f.Spans(f.Rerouted) == "Pcyi,Pipr,Pws9", "A crossing changed outside the patcher is refused before mutation: " + result.Outcomes["reroute"].Error);
        f = new Fixture();
        f.World.Graph.Remove("Pws9");
        result = new PatchEngine(f.Scene).Apply(Doc(Cover("reroute", Tracks("Pipr"))));
        Assert(!result.Outcomes["reroute"].Applied && f.World.Mutations == 0 && f.Spans(f.Rerouted) == "Pcyi,Pipr,Pws9", "A crossing with an unregistered native span is refused");
    }
    static void FailureAfterMutationRestoresCoverage() {
        foreach (var inject in new Action<FakeCrossingWorld>[] { w => w.FailSetCircuits = true, w => w.FailSetSpansOnce = true, w => w.FailCreateAfter = 2, w => w.SkipRegisterOnCreate = true }) {
            var f = new Fixture();
            inject(f.World);
            var result = new PatchEngine(f.Scene).Apply(Doc(Cover("reroute", Tracks("Pipr") + "," + Detached("P6id"))));
            Assert(!result.Outcomes["reroute"].Applied && result.Outcomes["reroute"].Error.StartsWith("Mutation failed and was restored", StringComparison.Ordinal), "A failure during mutation is restored: " + result.Outcomes["reroute"].Error);
            Assert(f.Native && f.OldRouteDetects && f.RetainedDetectsBothWays, "Restoration returns membership, native references and registrations; no owned span remains");
        }
        // A failure while the crossing already holds patched coverage restores that coverage, not the native one.
        var g = new Fixture();
        var engine = new PatchEngine(g.Scene);
        engine.Apply(Doc(Cover("reroute", Tracks("Pipr"))));
        var owned = g.Rerouted.Spans.ToArray();
        g.World.FailSetCircuits = true;
        var failed = new PatchEngine(g.Scene).Apply(Doc(Cover("more", Detached("P6id"))));
        Assert(!failed.Outcomes["more"].Applied && g.Rerouted.Spans.SequenceEqual(owned) && g.Crossing.Circuits.Count == 2 && g.NewRouteDetectsBothWays && g.RetainedDetectsBothWays, "Restoration returns the captured patched coverage: " + failed.Outcomes["more"].Error);
    }
    static void IncompleteCleanupGuardsRetry() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        f.World.FailSetSpansOnce = true; f.World.StickyDestroy = true;
        var result = engine.Apply(Doc(Cover("reroute", Tracks("Pipr"))));
        Assert(!result.Outcomes["reroute"].Applied && result.Outcomes["reroute"].Error.StartsWith("Mutation failed; cleanup is unverified", StringComparison.Ordinal), "Owned spans that stay registered make cleanup unverified: " + result.Outcomes["reroute"].Error);
        f.World.StickyDestroy = false; f.World.Mutations = 0;
        var retry = engine.Apply(Doc(Cover("retry", Tracks("Pipr"))));
        Assert(!retry.Outcomes["retry"].Applied && retry.Outcomes["retry"].Error.Contains("cleanup is unverified") && f.World.Mutations == 0, "Retry of the same crossing is guarded until cleanup verifies");
        var independent = engine.Apply(Doc(Cover("other", Detached("Qi").Replace("\"detach\":true", "\"approachA\":" + Range("Sother", 1, 5) + ",\"island\":" + Range("Sother", 5, 6) + ",\"approachB\":" + Range("Sother", 6, 9)), OtherPath)));
        Assert(independent.Outcomes["other"].Applied, "Another crossing stays editable");
    }
    static (Fixture f, AuthoringSession session, Editor editor, Store store) Session(string patch = null, Fixture fixture = null) {
        var f = fixture ?? new Fixture();
        var store = new Store { Patch = patch }; var editor = new Editor();
        var session = new AuthoringSession(new PatchEngine(f.Scene), editor, store, "map"); editor.Session = session;
        return (f, session, editor, store);
    }
    static JObject Only(AuthoringSession session) => (JObject)JObject.Parse(session.DocumentText)["modifications"].Single();
    static void EditorPreviewSelectUndoRedoAndRemoval() {
        var (f, session, editor, store) = Session();
        var preview = session.PreviewCoverage(Crossing, "Pipr", Selected());
        Assert(preview.Error == null && preview.Lines.Count == 2, "The preview reports every circuit: " + preview.Error);
        Assert(preview.Lines[0].Contains("Snew end a 308 to 550 m") && preview.Lines[0].Contains("Pcyi, Pipr, Pws9 stay registered and stop triggering"), "The preview shows the selected tracks and the obsolete spans: " + preview.Lines[0]);
        Assert(preview.Lines[1].StartsWith("Circuit P6id: retained", StringComparison.Ordinal), "The preview shows the retained circuit: " + preview.Lines[1]);
        Assert(f.World.Mutations == 0 && f.Native && session.DocumentText.Contains("\"modifications\": []") && editor.UndoGroups.Count == 0, "A preview changes nothing");
        Assert(session.PreviewCoverage(Crossing, "Pzzz", Selected()).Error.Contains("not a circuit"), "The preview refuses an obsolete circuit");
        var select = session.Cover(Crossing, "Pipr", Selected());
        Assert(select.Applied && f.NewRouteDetectsBothWays && !f.OldRouteDetects && f.RetainedDetectsBothWays, "An editor selection transfers detection: " + select.Error);
        var item = Only(session);
        Assert((string)item["relationships"]["crossingCoverage"][0]["circuit"] == "Pipr" && (double)item["relationships"]["crossingCoverage"][0]["approachB"]["to"]["distance"] == 50
            && item["placement"] == null && item["active"] == null && ((JArray)item["relationships"]["crossingCoverage"]).Count == 1, "The document records only the explicit circuit: " + item);
        Assert(session.MinimumPatcherVersion == "0.5.0", "Crossing coverage requires the patcher version that introduced it");
        Assert(session.Cover(Crossing, "P6id", CircuitCoverage.Detached).Applied && f.Crossing.Circuits.Count == 1 && f.NewRouteDetectsBothWays, "A second circuit can be detached in the same modification");
        Assert(((JArray)Only(session)["relationships"]["crossingCoverage"]).Count == 2, "Both circuits are recorded in one modification");
        var lastPreview = session.PreviewCoverage(Crossing, "Pipr", CircuitCoverage.Detached);
        Assert(lastPreview.Error != null && lastPreview.Error.Contains("without detection") && lastPreview.Lines.Count == 0, "The editor preview names why detaching the last covered circuit is refused: " + lastPreview.Error);
        Assert(CrossingCoverage.LastCovered(session.Coverage(Crossing), "Pipr") && !CrossingCoverage.LastCovered(session.Coverage(Crossing), "P6id"), "The editor can tell which circuit is the last covered one");
        var groups = editor.UndoGroups.Count;
        var last = session.Cover(Crossing, "Pipr", CircuitCoverage.Detached);
        Assert(!last.Applied && last.Error.Contains("without detection") && f.NewRouteDetectsBothWays && editor.UndoGroups.Count == groups, "Detaching the last covered circuit is refused without a history entry: " + last.Error);
        var owned = f.Rerouted.Spans.ToArray();
        editor.Undo(); Assert(f.Crossing.Circuits.Count == 2 && f.Rerouted.Spans.SequenceEqual(owned) && f.RetainedDetectsBothWays, "Undo of the detach restores the circuit and keeps the selected coverage");
        editor.Undo(); Assert(f.Native && f.OldRouteDetects && session.DocumentText.Contains("\"modifications\": []"), "Undo of the selection restores native coverage and registrations without owned spans");
        editor.Redo(); Assert(f.NewRouteDetectsBothWays && !f.OldRouteDetects && f.OwnedRegistered == 3, "Redo reapplies the selection with one owned set");
        editor.Redo(); Assert(f.Crossing.Circuits.Count == 1 && f.OwnedRegistered == 3, "Redo reapplies the detach");
        var cleared = session.Cover(Crossing, "P6id", null);
        Assert(cleared.Applied && f.Crossing.Circuits.Count == 2 && f.RetainedDetectsBothWays && f.NewRouteDetectsBothWays, "Clearing one circuit returns it to its underlying coverage and keeps the other: " + cleared.Error);
        Assert(!session.Cover(Crossing, "P6id", null).Applied, "Clearing an absent circuit is refused");
        var removed = session.Remove(Crossing);
        Assert(removed.Applied && f.Native && f.OldRouteDetects && f.OwnedRegistered == 0, "Removal returns native coverage and leaves no owned span: " + removed.Error);
        editor.Undo(); Assert(f.NewRouteDetectsBothWays && f.OwnedRegistered == 3, "Undo of removal restores the effective coverage");
        Assert(f.Scene.Read(f.Other).Coverage["Qi"].Kind == CircuitKind.Native, "No editor operation touched the other crossing");
    }
    static void EditorRefusalAndFailureRestore() {
        var (f, session, editor, store) = Session();
        Assert(!session.Edit(Crossing, new Placement(new[] { 1.0, 2, 3 }, new[] { 0.0, 0, 0, 1 }), null).Applied && f.World.Mutations == 0 && f.Native, "A placement edit of a crossing is refused before mutation");
        Assert(!session.Cover(Crossing, "Pipr", Selected("Smissing")).Applied && f.World.Mutations == 0 && f.Native && editor.UndoGroups.Count == 0, "A missing selected track is refused without a history entry");
        f.World.FailSetSpansOnce = true;
        var failed = session.Cover(Crossing, "Pipr", Selected());
        Assert(!failed.Applied && failed.Error.StartsWith("Edit failed and the prior state was restored", StringComparison.Ordinal) && f.Native && session.DocumentText.Contains("\"modifications\": []"), "A failed editor mutation restores coverage and the document: " + failed.Error);
        f.World.FailSetSpansOnce = true; f.World.StickyDestroy = true;
        failed = session.Cover(Crossing, "Pipr", Selected());
        Assert(!failed.Applied && failed.Error.Contains("cleanup is unverified"), "Incomplete cleanup is reported: " + failed.Error);
        f.World.StickyDestroy = false;
        Assert(!session.Cover(Crossing, "Pipr", Selected()).Applied, "A further edit is guarded until a recheck");
        // The stuck owned spans stay registered until a recheck converges on the document.
        var recheck = session.Recheck(Crossing);
        Assert(recheck.Applied && f.Native && f.OwnedRegistered == 0 && f.World.Graph.Values.Count(s => s.Owned) == 0, "Recheck converges on the document and clears the guard: " + recheck.Error);
        Assert(session.Cover(Crossing, "Pipr", Selected()).Applied && f.OwnedRegistered == 3, "After recheck the edit applies");
    }
    static void SaveAndReloadReproduceCoverageOnce() {
        var (f, session, editor, store) = Session();
        session.Cover(Crossing, "Pipr", Selected());
        session.Cover(Crossing, "P6id", CircuitCoverage.Detached);
        Assert(editor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0"), "Save raises the dependency to the crossing-coverage version: " + store.Info);
        var fresh = new Fixture();
        var engine = new PatchEngine(fresh.Scene);
        var reload = engine.Apply(store.Patch);
        Assert(reload.Outcomes.Values.All(o => o.Applied) && fresh.NewRouteDetectsBothWays && !fresh.OldRouteDetects && fresh.Crossing.Circuits.Count == 1, "A reloaded world reproduces the saved coverage");
        engine.Apply(store.Patch);
        Assert(fresh.OwnedRegistered == 3 && fresh.World.Created == 3, "A repeated world-load callback leaves exactly one owned set");
        var (f2, reopened, e2, s2) = Session(store.Patch);
        Assert(reopened.Blocked == null && reopened.Cover(Crossing, "P6id", null).Applied && f2.Crossing.Circuits.Count == 2, "A saved coverage document reopens for editing");
    }
}
