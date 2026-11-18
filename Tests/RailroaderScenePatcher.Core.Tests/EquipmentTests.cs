// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// A loaded crossing device: the facts the light/gate pattern reads from its hierarchy. The production LightGatePattern
// judges them, so these tests exercise the eligibility rules the native world uses. The default is the Wishbone signal:
// one lamp keyed lights and one gate keyed gates.
class FakeDevice {
    public string Scene = "BushnellWhittier", Path, Model = "Crossing Signal Wishbone A";
    public DeviceFacts Facts = new DeviceFacts {
        Lamps = { new LampFacts { Name = "Signal 1", Key = "lights", Targets = 1, Materials = 2, RendererUsesMaterial = true } },
        Gates = { new GateFacts { Name = "Crossing Signal Wishbone A (1)", Key = "gates", Animator = true, Clip = true } }
    };
    public FakeEquipmentWorld.Added Owner;
}
// A bell definition of the content library: the facts the bell pattern reads from it. The production BellPattern judges
// them. The default is the game's crossing bell, whose audio splits at two indexes.
class FakeBell {
    public string Identifier = "rr/bell-uss-teardrop-crossing-rh", Name = "Crossing Bell";
    public BellFacts Facts = new BellFacts { Audio = "rr/bell-crossing", Indexes = new double[] { 0.4, 1.2 }, Volume = 0.8 };
    public double ClipSeconds = 3;
}
// Controlled equipment world: devices by scene and path, bell definitions by identifier, and additions the patcher owns
// beneath each crossing controller.
class FakeEquipmentWorld : ICrossingEquipmentWorld, ICrossingEquipmentVerificationDiagnostics {
    public static readonly string[] Keys = { "lights", "gates" };
    public sealed class Added {
        public FakeCrossing Controller; public FakeDevice Copy; public EquipmentAddition Record;
        public double[] Position, Rotation; public bool Destroyed, Culled, InCuller = true;
        // A bell: its definition, whether its audio is loaded or failed to load, and the observers it holds on its
        // controller's bell key.
        public FakeBell Bell; public bool AudioLoaded; public string LoadProblem; public int Observers;
    }
    public readonly List<FakeBell> Bells = new List<FakeBell>();
    // Loaded audio references the bells hold; each release lowers it. A bell that is built and loading holds none yet.
    public int AudioReferences, PendingLoads;
    // Loads complete later, as in the game, where the load continues on a later frame: FinishLoads stands for that frame.
    public bool LoadsPending = true, FailLoads;
    // The warnings a failed load raises, one per bell: its scope, ID, controller and reason.
    public readonly List<string> Warnings = new List<string>();
    public FakeBell Bell(string identifier = null, Action<FakeBell> setup = null) { var b = new FakeBell(); if (identifier != null) b.Identifier = identifier; setup?.Invoke(b); Bells.Add(b); return b; }
    // Completes every pending load: a bell whose load succeeds holds one audio reference.
    public void FinishLoads() {
        foreach (var a in All.Where(a => a.Bell != null && !a.Destroyed && !a.AudioLoaded && a.LoadProblem == null)) {
            if (FailLoads) a.LoadProblem = "the asset bundle failed";
            else { var clip = BellPattern.ClipProblem((int)(a.Bell.ClipSeconds * 44100), 44100, a.Bell.Facts.Indexes); if (clip != null) a.LoadProblem = clip; else { a.AudioLoaded = true; AudioReferences++; } }
            if (a.LoadProblem != null) Warnings.Add(a.Record.Scope + " " + a.Record.Id + " at " + "its crossing controller" + ": " + a.LoadProblem);
            PendingLoads--;
        }
    }
    public readonly List<FakeDevice> Devices = new List<FakeDevice>();
    public readonly List<Added> All = new List<Added>();
    public int Constructed, Mutations;
    public int FailConstructAfter = -1;
    public double ConstructOffset;
    public double[] ConstructPosition;
    public readonly List<string> VerificationFailures = new List<string>();
    public void VerificationFailed(object controller, string detail) => VerificationFailures.Add(detail);
    public bool StickyDestroy, FailSetPlacementOnce, CullerVisible = true;
    public FakeDevice Device(string path, Action<FakeDevice> setup = null) { var d = new FakeDevice { Path = path }; setup?.Invoke(d); Devices.Add(d); return d; }
    public IReadOnlyList<object> ResolveSource(EquipmentSource source) => source.IsDefinition ? Bells.Where(b => b.Identifier == source.Definition).Cast<object>().ToArray()
        : Devices.Where(d => d.Owner == null && d.Scene == source.Scene && d.Path == source.Path).Cast<object>().ToArray();
    public string SourceProblem(object source, object controller, string pattern) {
        if (source is FakeBell bell) return pattern != EquipmentAddition.BellPattern ? "the pattern " + pattern + " is not supported for a definition" : BellPattern.Problem(bell.Facts);
        if (pattern == EquipmentAddition.BellPattern) return "a loaded device is no bell definition";
        var d = (FakeDevice)source;
        if (pattern != EquipmentAddition.LightGatePattern) return "the pattern " + pattern + " is not supported";
        d.Facts.AddedByPatcher = d.Owner != null;
        return LightGatePattern.Problem(d.Facts, Keys);
    }
    public IReadOnlyList<SourceCandidate> Candidates(object controller) =>
        Devices.Where(d => d.Owner == null).Select(d => new SourceCandidate(new EquipmentSource(d.Scene, d.Path), d.Model, SourceProblem(d, controller, EquipmentAddition.LightGatePattern)))
            .Concat(Bells.Select(b => SourceCandidate.Bell(b.Identifier, b.Name, SourceProblem(b, controller, EquipmentAddition.BellPattern)))).ToArray();
    public IReadOnlyList<object> Owned(object controller) => All.Where(a => ReferenceEquals(a.Controller, controller) && !a.Destroyed).Cast<object>().ToArray();
    public EquipmentAddition Describe(object addition) => ((Added)addition).Record;
    public object Construct(object controller, object source, EquipmentAddition addition) {
        Mutations++;
        if (FailConstructAfter == 0) { FailConstructAfter = -1; throw new Exception("construction injected"); }
        if (FailConstructAfter > 0) FailConstructAfter--;
        if (source is FakeBell bell) {
            var player = new Added { Controller = (FakeCrossing)controller, Record = addition, Position = (double[])addition.Placement.Position.Clone(), Rotation = (double[])addition.Placement.Rotation.Clone(), Bell = bell, Observers = 1 };
            All.Add(player); Constructed++; PendingLoads++;
            if (!LoadsPending) FinishLoads();
            return player;
        }
        var d = (FakeDevice)source;
        var added = new Added { Controller = (FakeCrossing)controller, Record = addition, Position = (double[])(ConstructPosition ?? addition.Placement.Position).Clone(), Rotation = (double[])addition.Placement.Rotation.Clone(), Culled = !CullerVisible };
        added.Position[0] += ConstructOffset;
        added.Copy = new FakeDevice { Scene = d.Scene, Path = "owned/" + addition.Id, Model = d.Model, Owner = added };
        All.Add(added); Devices.Add(added.Copy); Constructed++;
        return added;
    }
    public void Destroy(object addition) {
        Mutations++; if (StickyDestroy) return; var a = (Added)addition; a.Destroyed = true; a.InCuller = false; Devices.Remove(a.Copy);
        // A bell releases its observer and its audio; a load still pending is cancelled.
        if (a.Bell != null) { a.Observers = 0; if (a.AudioLoaded) { a.AudioLoaded = false; AudioReferences--; } else if (a.LoadProblem == null) PendingLoads--; }
    }
    public bool Present(object addition) => !((Added)addition).Destroyed;
    public Placement Placement(object addition) { var a = (Added)addition; return new Placement((double[])a.Position.Clone(), (double[])a.Rotation.Clone()); }
    public void SetPlacement(object addition, Placement placement) {
        Mutations++; var a = (Added)addition;
        a.Position = (double[])placement.Position.Clone(); a.Rotation = (double[])placement.Rotation.Clone();
        if (FailSetPlacementOnce) { FailSetPlacementOnce = false; throw new Exception("placement injected"); }
    }
    public string Problem(object addition) {
        var a = (Added)addition;
        if (a.Bell != null) return a.LoadProblem != null ? "its audio failed to load: " + a.LoadProblem : a.Observers != 1 ? "it observes its controller's bell key " + a.Observers + " times" : null;
        return a.InCuller ? null : "the copy is not in the controller's culler";
    }
    public string Status(object addition) {
        var a = (Added)addition;
        if (a.Bell != null) return a.LoadProblem != null ? "audio failed to load" : !a.AudioLoaded ? "audio loading" : "audio loaded; observing the controller's bell key";
        return a.Culled ? "culled; it initializes when visible" : "lamp and gate observers registered";
    }
    // Whether the live additions of this crossing show its lights and gates: each one observes the controller's keys.
    public IEnumerable<Added> Live(FakeCrossing crossing) => All.Where(a => ReferenceEquals(a.Controller, crossing) && !a.Destroyed);
}
static class EquipmentTests {
    const string CrossingPath = "World/Crossings/Bryson Everett", OtherPath = "World/Crossings/Whittier", SignalPath = CrossingPath + "/Signal 1";
    static readonly TargetIdentity Crossing = new TargetIdentity("BushnellWhittier", CrossingPath, CrossingCoverageScene.ComponentType, CrossingCoverageScene.Structure);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Target(string path = CrossingPath) => "\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"" + CrossingCoverageScene.ComponentType + "\",\"structure\":\"" + CrossingCoverageScene.Structure + "\"}";
    static string N(double value) => value.ToString(CultureInfo.InvariantCulture);
    static string Addition(string id, string source = SignalPath, double x = 10, string pattern = EquipmentAddition.LightGatePattern, string extra = "", double[] position = null) =>
        "\"" + id + "\":{\"pattern\":\"" + pattern + "\",\"source\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + source + "\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", (position ?? new[] { x, 0, 7 }).Select(N)) + "],\"rotation\":[0,0,0,1]}" + extra + "}";
    static string Equip(string id, string additions, string path = CrossingPath, string deps = "[]") => EquipRaw(id, "{" + additions + "}", path, deps);
    // A modification whose crossingEquipment value is the given JSON text, keyed by equipment ID when it is valid.
    static string EquipRaw(string id, string equipment, string path = CrossingPath, string deps = "[]") =>
        "\"" + id + "\":{" + Target(path) + ",\"relationships\":{\"crossingEquipment\":" + equipment + "},\"dependsOn\":" + deps + "}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":{" + items + "}}";
    sealed class Fixture {
        public FakeCrossingWorld Coverage = new FakeCrossingWorld();
        public FakeEquipmentWorld World = new FakeEquipmentWorld();
        public FakeCrossing Crossing = new FakeCrossing(), Other = new FakeCrossing();
        public CrossingCoverageScene Scene;
        public Fixture() {
            Crossing.Circuits.Add(new FakeCircuit { Spans = new[] { Coverage.Span("Pcyi", "Sold", 308, 550), Coverage.Span("Pipr", "Sold", 292, 308), Coverage.Span("Pws9", "Sold", 292, 50) } });
            Other.Circuits.Add(new FakeCircuit { Spans = new[] { Coverage.Span("Qa", "Sother", 1, 5), Coverage.Span("Qi", "Sother", 5, 6), Coverage.Span("Qb", "Sother", 6, 9) } });
            Coverage.Objects[CrossingPath] = new List<object> { Crossing }; Coverage.Objects[OtherPath] = new List<object> { Other };
            World.Device(SignalPath); World.Device(CrossingPath + "/Signal 2");
            Scene = new CrossingCoverageScene(Coverage, Capability.Unchecked, World);
        }
        public List<FakeEquipmentWorld.Added> Live => World.Live(Crossing).ToList();
        public bool Unchanged => World.Live(Crossing).Count() == 0 && World.Live(Other).Count() == 0 && Crossing.Circuits.Count == 1;
    }
    public static void Run() {
        AdditionConstructsThroughTheController();
        EligibilityFollowsThePatternAcrossModels();
        UnresolvedAndNonconformingSourcesChangeNothing();
        InvalidEquipmentContentIsRefused();
        PreflightRefusalBuildsNothing();
        ConstructionFailureRestoresCapturedEquipment();
        IncompleteCleanupGuardsRetry();
        SupplyingModsKeepSeparateIdentities();
        MissingCapabilityRefusesEquipment();
        EditingPreviewAddMoveUndoRedoAndRemoval();
        EditingRefusalAndFailureRestore();
        SaveAndReloadReproduceEquipmentOnce();
        EquipmentIdsNameOneAdditionPerScenePatchFile();
        DetectionAdditionsAndEquipmentWorkTogether();
        FaultyEquipmentLeavesCoverageEditsAvailable();
        TheEditingProposesADistinctIdAndAPlacementAtTheCrossing();
        EditingKeepsAnotherModsEquipment();
        AFailedAdditionIsListedAndRemovable();
        SmallMovesReachTheSceneAndUntouchedValuesStay();
        UnknownSourceOrPlacementContentIsUnsupported();
        Console.WriteLine("PASS: crossing light and gate eligibility, construction, refusal, restoration and scene patch editing");
    }
    static void AdditionConstructsThroughTheController() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        var result = engine.Apply(Doc(Equip("signals", Addition("west"))), "author/pack");
        Assert(result.Error == null && result.Outcomes["signals"].Applied && FloatStoredPlacementsVerify(), "A conforming light/gate source verifies at its stored position, including far from the origin: " + result.Error + result.Outcomes["signals"].Error);
        Assert(f.Live.Count == 1 && ReferenceEquals(f.Live[0].Controller, f.Crossing) && f.Live[0].Position.SequenceEqual(new double[] { 10, 0, 7 }), "One addition is owned by the intended controller at the mod's placement");
        Assert(f.Live[0].Record.Scope == "author/pack" && f.Live[0].Record.Id == "west" && f.Live[0].Record.Source.Path == SignalPath, "The addition keeps its supplying-mod-scoped identity and source");
        engine.Apply(Doc(Equip("signals", Addition("west"))), "author/pack");
        Assert(f.Live.Count == 1 && f.World.Constructed == 1, "Repeating the application adds no duplicate");
        var fresh = new PatchEngine(new CrossingCoverageScene(f.Coverage, Capability.Unchecked, f.World));
        fresh.Apply(Doc(Equip("signals", Addition("west"))), "author/pack");
        Assert(f.Live.Count == 1 && f.World.Constructed == 1, "A new engine in the same world adopts the owned addition instead of duplicating it");
        var observed = f.Scene.Read(f.Crossing).Equipment;
        Assert(observed.Complete && observed.Additions.Count == 1 && observed.Additions[0].Id == "west", "The observed equipment lists every owned addition");
        engine.Apply(Doc(Equip("signals", Addition("west", x: 12))), "author/pack");
        Assert(f.Live.Count == 1 && f.World.Constructed == 1 && f.Live[0].Position[0] == 12, "A changed placement moves the owned addition instead of building another");
        var failed = new Fixture(); failed.World.ConstructOffset = 0.125;
        var refusal = new PatchEngine(failed.Scene).Apply(Doc(Equip("signals", Addition("west"))), "author/pack");
        Assert(f.Crossing.Position.SequenceEqual(new double[] { 5, 0, 7 }) && f.Crossing.Circuits.Count == 1
            && refusal.Outcomes["signals"].Error == "The change failed. The object is back as it was. Reason: Applied crossing equipment did not verify: equipment west differs from the mod's source or placement"
            && failed.World.VerificationFailures.Count == 1
            && failed.World.VerificationFailures[0] == "reason=\"mismatch\" identity=true source=true position=false rotation=true positionTolerance=0.0005-per-axis-or-equal-float32 rotationTolerance=normalized-absolute-dot>=0.99999999 desired={scope=\"author/pack\",id=\"west\",pattern=\"crossing-light-gate-v1\",sourceScene=\"BushnellWhittier\",sourcePath=\"" + SignalPath + "\",position=[10,0,7],rotation=[0,0,0,1]} observed={scope=\"author/pack\",id=\"west\",pattern=\"crossing-light-gate-v1\",sourceScene=\"BushnellWhittier\",sourcePath=\"" + SignalPath + "\",position=[10.125,0,7],rotation=[0,0,0,1]}", "Equipment leaves the crossing unchanged and a failed verification records the difference without changing the refusal");
    }
    static bool FloatStoredPlacementsVerify() {
        // Desired and observed values from the Allen journey, plus negative and larger map coordinates.
        var cases = new[] {
            new[] { new[] { 24631.32947220674, 620.50640869140625, -939.62025711108242 }, new[] { 24631.330078125, 620.50640869140625, -939.6201171875 } },
            new[] { new[] { 24637.317507956483, 620.18865966796875, -940.59328430260223 }, new[] { 24637.318359375, 620.18865966796875, -940.59326171875 } },
            new[] { new[] { -24631.32947220674, -620.50640869140625, 939.62025711108242 }, new[] { -24631.330078125, -620.50640869140625, 939.6201171875 } },
            new[] { new[] { 1000000.1239, 0, 7 }, new[] { 1000000.125, 0, 7 } }
        };
        // A neighbouring stored X coordinate is already a real misplacement, even before the larger offset.
        foreach (var values in cases) foreach (var offset in new[] { 0, Math.Abs(values[1][0]) >= 1000000 ? 0.0625 : 0.001953125, 0.125 }) {
            var f = new Fixture(); f.World.ConstructPosition = values[1]; f.World.ConstructOffset = offset;
            var engine = new PatchEngine(f.Scene);
            var file = Doc(Equip("signals", Addition("far", position: values[0])));
            var result = engine.Apply(file, "author/pack");
            if (offset == 0) {
                if (!result.Outcomes["signals"].Applied || f.Live.Count != 1 || !f.Live[0].Position.SequenceEqual(values[1])) return false;
                var mutations = f.World.Mutations;
                if (!engine.Apply(file, "author/pack").Outcomes["signals"].Applied || f.World.Mutations != mutations) return false;
            } else if (result.Outcomes["signals"].Applied || !f.Unchanged || f.World.VerificationFailures.Count != 1) return false;
        }
        return true;
    }
    // Devices with other model values qualify through the same lamp and gate mechanisms, judged by the production
    // LightGatePattern; each rule that fails names its concrete gap.
    static void EligibilityFollowsThePatternAcrossModels() {
        var f = new Fixture();
        // Other models, names, materials and key assignments, without a per-model list: two lamps and no gate, a gate keyed
        // lights, a gate alone, and a lamp keyed gates with three lamp materials.
        f.World.Device("World/Other/Cantilever Flasher", d => { d.Model = "Unexamined Cantilever"; d.Facts.Gates.Clear(); d.Facts.Lamps.Add(new LampFacts { Name = "Left Flasher", Key = "lights", Targets = 2, Materials = 1, RendererUsesMaterial = true }); });
        f.World.Device("World/Other/Barrier", d => { d.Model = "Unexamined Barrier"; d.Facts.Lamps.Clear(); d.Facts.Gates[0] = new GateFacts { Name = "Arm", Key = "lights", Animator = true, Clip = true }; });
        f.World.Device("World/Other/Gate Only", d => { d.Model = "Unexamined Gate"; d.Facts.Lamps.Clear(); });
        f.World.Device("World/Other/Gate Lamp", d => { d.Model = "Unexamined Gate Lamp"; d.Facts.Gates.Clear(); d.Facts.Lamps[0] = new LampFacts { Name = "Arm Lamp", Key = "gates", Targets = 1, Materials = 3, RendererUsesMaterial = true }; });
        var rejected = new Dictionary<string, KeyValuePair<Action<FakeDevice>, string>> {
            ["bell"] = R(d => d.Facts.Bells = 1, "it contains a bell player; add bells from a bell definition using the bell pattern"),
            ["owner"] = R(d => d.Facts.KeyValueObjects = 1, "it holds its own key-value object, which would intercept the crossing controller's keys"),
            ["controller"] = R(d => d.Facts.Controllers = 1, "it holds a crossing controller; a copy would be a second crossing"),
            ["span"] = R(d => d.Facts.Spans = 1, "it holds a detection span"),
            ["culler"] = R(d => d.Facts.Cullers = 1, "it holds its own culler"),
            ["script"] = R(d => d.Facts.MissingScript = true, "a script in its hierarchy is missing"),
            ["scene"] = R(d => d.Facts.OtherScene = true, "it is in a different Unity scene from the destination crossing"),
            ["contains"] = R(d => d.Facts.ContainsDestination = true, "it contains the destination crossing"),
            ["destination owner"] = R(d => d.Facts.DestinationKeyValueObjects = 0, "the destination crossing controller has no key-value objects"),
            ["destination cullers"] = R(d => d.Facts.DestinationCullers = 2, "the destination crossing controller has several cullers"),
            ["mode"] = R(d => d.Facts.Lamps[0].Key = "mode", "lamp Signal 1 observes key 'mode', which the crossing controller does not publish"),
            ["gate key"] = R(d => d.Facts.Gates[0].Key = "bell", "observes key 'bell', which the crossing controller does not publish"),
            ["no target"] = R(d => d.Facts.Lamps[0].Targets = 0, "lamp Signal 1 has no lamp target"),
            ["missing target"] = R(d => d.Facts.Lamps[0].MissingTargets = 1, "lamp Signal 1 has no lamp target"),
            ["outside"] = R(d => d.Facts.Lamps[0].OutsideTargets = 1, "targets an object outside the source, so a copy would drive the original"),
            ["material"] = R(d => d.Facts.Lamps[0].Materials = 0, "has no lamp material with a colour property"),
            ["property"] = R(d => d.Facts.Lamps[0].MaterialsWithoutProperty = 1, "has no lamp material with a colour property"),
            ["renderer"] = R(d => d.Facts.Lamps[0].RendererUsesMaterial = false, "has no renderer under its targets that uses its lamp material"),
            ["animator"] = R(d => d.Facts.Gates[0].Animator = false, "has no Animator"),
            ["clip"] = R(d => d.Facts.Gates[0].Clip = false, "has no animation clip"),
            ["empty"] = R(d => { d.Facts.Lamps.Clear(); d.Facts.Gates.Clear(); }, "it has no lamp or gate that observes the controller's lights or gates key"),
        };
        foreach (var r in rejected) f.World.Device("World/Rejected/" + r.Key, r.Value.Key);
        var candidates = f.Scene.EquipmentCandidates(f.Crossing);
        var eligible = new[] { CrossingPath + "/Signal 1", CrossingPath + "/Signal 2", "World/Other/Barrier", "World/Other/Cantilever Flasher", "World/Other/Gate Lamp", "World/Other/Gate Only" };
        Assert(candidates.Where(c => c.Eligible).Select(c => c.Source.Path).OrderBy(p => p).SequenceEqual(eligible.OrderBy(p => p)),
            "Every conforming device is offered, whatever its model: " + String.Join(", ", candidates.Where(c => c.Eligible).Select(c => c.Source.Path)));
        foreach (var r in rejected) {
            var candidate = candidates.Single(c => c.Source.Path == "World/Rejected/" + r.Key);
            Assert(!candidate.Eligible && candidate.Problem.Contains(r.Value.Value), "Rule " + r.Key + " names its concrete gap: " + candidate.Problem);
        }
        var result = new PatchEngine(f.Scene).Apply(Doc(Equip("mixed", Addition("flasher", "World/Other/Cantilever Flasher") + "," + Addition("barrier", "World/Other/Barrier", 20) + "," + Addition("gate", "World/Other/Gate Only", 30) + "," + Addition("lamp", "World/Other/Gate Lamp", 40))));
        Assert(result.Outcomes["mixed"].Applied && f.Live.Count == 4, "Unexamined models conforming to the pattern are built without a per-model list: " + result.Outcomes["mixed"].Error);
    }
    static KeyValuePair<Action<FakeDevice>, string> R(Action<FakeDevice> breaks, string reason) => new KeyValuePair<Action<FakeDevice>, string>(breaks, reason);
    static void UnresolvedAndNonconformingSourcesChangeNothing() {
        var cases = new Dictionary<string, Func<Fixture, string>> {
            ["missing"] = f => Addition("west", CrossingPath + "/Signal 9"),
            ["absent scene"] = f => Addition("west").Replace("\"scene\":\"BushnellWhittier\"", "\"scene\":\"UnloadedScene\""),
            ["ambiguous"] = f => { f.World.Device(SignalPath); return Addition("west"); },
            ["nonconforming"] = f => { f.World.Device("World/Rejected/bell", d => d.Facts.Bells = 1); return Addition("west", "World/Rejected/bell"); },
            ["added copy"] = f => { new PatchEngine(f.Scene).Apply(Doc(Equip("first", Addition("first")))); f.World.Mutations = 0; return Addition("west", "owned/first"); },
        };
        foreach (var c in cases) {
            var f = new Fixture();
            var addition = c.Value(f);
            var before = f.Live.Count;
            var result = new PatchEngine(f.Scene).Apply(Doc(Equip("bad", addition)));
            var error = result.Outcomes["bad"].Error ?? "";
            Assert(!result.Outcomes["bad"].Applied && f.World.Mutations == 0 && f.Live.Count == before, c.Key + " source is refused before any change: " + error);
            Assert(!f.World.Live(f.Crossing).Any(a => a.Record.Id == "west"), c.Key + " source is not replaced by another device");
            if (c.Key == "missing" || c.Key == "absent scene") Assert(error.Contains("not loaded in this map") && error.Contains("will not substitute another source or load another scene"), "A missing source neither substitutes nor loads a scene: " + error);
            if (c.Key == "ambiguous") Assert(error.Contains("ambiguous"), "An ambiguous source is named: " + error);
            if (c.Key == "nonconforming") Assert(error.Contains("does not conform to " + EquipmentAddition.LightGatePattern) && error.Contains("bell"), "A nonconforming source gives its reason: " + error);
        }
        var g = new Fixture();
        var future = new PatchEngine(g.Scene).Apply(Doc(Equip("future", Addition("west", pattern: "crossing-semaphore-v9"))));
        Assert(!future.Outcomes["future"].Applied && future.Outcomes["future"].Error.StartsWith("Unsupported", StringComparison.Ordinal) && g.World.Mutations == 0, "An unknown pattern is unsupported content, not substituted: " + future.Outcomes["future"].Error);
    }
    static void InvalidEquipmentContentIsRefused() {
        var entry = Addition("west").Substring("\"west\":".Length);
        foreach (var invalid in new[] { "{}", "null", "[]", "5", "[{" + Addition("west").Replace("\"west\":{", "\"id\":\"west\",") + "]", "{" + Addition("bad id") + "}", "{\" \":" + entry + "}",
                "{\"west\":5}", "{" + Addition("west").Replace("\"pattern\":\"" + EquipmentAddition.LightGatePattern + "\",", "") + "}",
                "{" + Addition("west").Replace("\"mode\":\"fixed\"", "\"mode\":\"trackFollowing\"") + "}", "{" + Addition("west").Replace("[0,0,0,1]", "[0,0,0,0]") + "}",
                "{" + Addition("west").Replace("\"path\":\"" + SignalPath + "\"", "\"path\":\"\"") + "}", "{" + Addition("west", extra: ",\"bell\":true") + "}",
                "{" + Addition("west").Replace("\"west\":{", "\"west\":{\"id\":\"west\",") + "}" }) {
            var f = new Fixture();
            var result = new PatchEngine(f.Scene).Apply(Doc(EquipRaw("bad", invalid)));
            Assert(result.Error == null && !result.Outcomes["bad"].Applied && f.World.Mutations == 0 && f.Unchanged, "Invalid equipment content changes nothing: " + invalid + " -> " + result.Error + result.Outcomes["bad"].Error);
        }
        var g = new Fixture();
        var repeated = new PatchEngine(g.Scene).Apply(Doc(Equip("bad", Addition("west") + "," + Addition("west", x: 20))));
        Assert(repeated.Error != null && g.World.Mutations == 0, "A repeated equipment key is a repeated property name and rejects the whole scene patch file: " + repeated.Error);
        var h = new Fixture();
        var legacy = new PatchEngine(h.Scene).Apply(Doc(EquipRaw("old", "{" + Addition("west").Replace("\"west\":{", "\"west\":{\"id\":\"west\",") + "}")));
        Assert(legacy.Outcomes["old"].Error.StartsWith("Unsupported", StringComparison.Ordinal) && legacy.Outcomes["old"].Error.Contains("its ID is its key"), "An entry with an id member is content of the older shape: " + legacy.Outcomes["old"].Error);
    }
    static void PreflightRefusalBuildsNothing() {
        var f = new Fixture();
        var result = new PatchEngine(f.Scene).Apply(Doc(Equip("pair", Addition("west") + "," + Addition("east", CrossingPath + "/Missing", 20))));
        Assert(!result.Outcomes["pair"].Applied && f.World.Mutations == 0 && f.Unchanged, "A coherent operation with one unresolved source builds none of its additions: " + result.Outcomes["pair"].Error);
        var dependent = new PatchEngine(f.Scene).Apply(Doc(Equip("pair", Addition("east", CrossingPath + "/Missing")) + "," + Equip("after", Addition("west"), deps: "[\"pair\"]") + "," + Equip("alone", Addition("solo", x: 30))));
        Assert(!dependent.Outcomes["after"].Applied && dependent.Outcomes["alone"].Applied && f.Live.Single().Record.Id == "solo", "A dependent operation fails with its prerequisite while independent work applies");
    }
    static void ConstructionFailureRestoresCapturedEquipment() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        engine.Apply(Doc(Equip("first", Addition("west"))), "author/pack");
        f.World.FailConstructAfter = 1;
        var result = engine.Apply(Doc(Equip("second", Addition("west", x: 15) + "," + Addition("east", CrossingPath + "/Signal 2", 20) + "," + Addition("north", x: 25))), "author/pack");
        Assert(!result.Outcomes["second"].Applied && result.Outcomes["second"].Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal), "A construction failure is restored: " + result.Outcomes["second"].Error);
        Assert(f.Live.Count == 1 && f.Live[0].Record.Id == "west" && f.Live[0].Position[0] == 10, "The captured additions and placements return; the partial addition is released");
        Assert(f.World.All.Where(a => a.Record.Id == "east").All(a => a.Destroyed && !a.InCuller), "The partially built addition leaves the controller and its culling");
    }
    static void IncompleteCleanupGuardsRetry() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        f.World.FailConstructAfter = 1; f.World.StickyDestroy = true;
        var result = engine.Apply(Doc(Equip("pair", Addition("west") + "," + Addition("east", CrossingPath + "/Signal 2", 20))), "author/pack");
        Assert(!result.Outcomes["pair"].Applied && result.Outcomes["pair"].Error.Contains("could not be verified"), "Incomplete cleanup is reported: " + result.Outcomes["pair"].Error);
        f.World.StickyDestroy = false;
        var retry = engine.Apply(Doc(Equip("other", Addition("north", x: 30))), "author/pack");
        Assert(!retry.Outcomes["other"].Applied && retry.Outcomes["other"].Error.Contains("could not be verified") && f.World.Live(f.Crossing).Count() == 1, "A retry on the uncertain controller is blocked: " + retry.Outcomes["other"].Error);
    }
    static void SupplyingModsKeepSeparateIdentities() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        engine.Apply(Doc(Equip("signals", Addition("west"))), "first/pack");
        engine.Apply(Doc(Equip("signals", Addition("west", x: 20))), "second/pack");
        Assert(f.Live.Count == 2 && f.Live.Select(a => a.Record.Scope).OrderBy(s => s).SequenceEqual(new[] { "first/pack", "second/pack" }), "The same ID from two supplying mods names two additions");
        engine.Apply(Doc(Equip("coverage-only", Addition("other", x: 30))), "third/pack");
        Assert(f.Live.Count == 3 && f.Live.Single(a => a.Record.Scope == "first/pack").Position[0] == 10, "A scene patch file never removes or moves another mod's additions");
    }
    static void MissingCapabilityRefusesEquipment() {
        var f = new Fixture();
        var scene = new CrossingCoverageScene(f.Coverage, Capability.Unchecked);
        var result = new PatchEngine(scene).Apply(Doc(Equip("signals", Addition("west"))));
        Assert(!result.Outcomes["signals"].Applied && result.Outcomes["signals"].Error.Contains("Crossing equipment editing is unavailable") && f.World.Mutations == 0, "Without the equipment capability the relationship is refused: " + result.Outcomes["signals"].Error);
        var board = new Scene(); board.Objects["p"] = new List<object> { new Box() };
        var other = new PatchEngine(board).Apply(Doc("\"x\":{\"target\":{\"scene\":\"s\",\"path\":\"p\",\"component\":\"c\"},\"relationships\":{\"crossingEquipment\":{" + Addition("west") + "}},\"dependsOn\":[]}"));
        Assert(!other.Outcomes["x"].Applied && other.Outcomes["x"].Error.Contains("no supported crossing-equipment relationship"), "An object without the relationship refuses it");
    }
    static (Fixture f, PatchEditingSession session, NativeMapEditor mapEditor, Store store) Session(string patch = null, Fixture fixture = null) {
        var f = fixture ?? new Fixture();
        var store = new Store { Patch = patch }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(f.Scene), mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        return (f, session, mapEditor, store);
    }
    static EquipmentAddition West(double x = 10, string path = SignalPath, string id = "west") =>
        new EquipmentAddition(id, EquipmentAddition.LightGatePattern, new EquipmentSource("BushnellWhittier", path), new Placement(new[] { x, 0, 7 }, new double[] { 0, 0, 0, 1 }));
    static JObject Only(PatchEditingSession session) => (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"].Single().First;
    static void EditingPreviewAddMoveUndoRedoAndRemoval() {
        var (f, session, mapEditor, store) = Session();
        Assert(session.EquipmentSources(Crossing).Count(c => c.Eligible) == 2, "Scene patch editing offers the loaded conforming sources");
        var preview = session.PreviewEquipment(Crossing, West(), null);
        Assert(preview.Error == null && preview.Lines.Single().StartsWith("Add author/pack west", StringComparison.Ordinal), "The preview names the addition: " + preview.Error + String.Join("|", preview.Lines));
        Assert(f.World.Mutations == 0 && mapEditor.UndoGroups.Count == 0 && session.ScenePatchFileText.Contains("\"modifications\": {}"), "A preview changes nothing");
        var added = session.AddEquipment(Crossing, West());
        Assert(added.Applied && f.Live.Single().Position[0] == 10, "A scene patch editing addition builds the equipment directly: " + added.Error);
        var item = Only(session);
        var entry = (JObject)item["relationships"]["crossingEquipment"]["west"];
        Assert(entry != null && entry["id"] == null && entry.Properties().Select(p => p.Name).SequenceEqual(new[] { "pattern", "source", "placement" }) && ((JObject)item["relationships"]["crossingEquipment"]).Count == 1 && (string)entry["pattern"] == EquipmentAddition.LightGatePattern && (string)entry["source"]["path"] == SignalPath && (string)entry["placement"]["mode"] == "fixed"
            && (string)item["target"]["path"] == CrossingPath, "The scene patch file records the source, the pattern and the controller relationship: " + item);
        Assert(session.MinimumPatcherVersion == "0.5.0", "Crossing equipment requires patcher 0.5.0");
        Assert(session.AddEquipment(Crossing, West(20, CrossingPath + "/Signal 2", "east")).Applied && f.Live.Count == 2, "A second addition joins the same modification");
        Assert(session.AddEquipment(Crossing, West(12)).Applied && f.Live.Count == 2 && f.World.Constructed == 2 && f.Live.Single(a => a.Record.Id == "west").Position[0] == 12, "Re-adding an ID moves its addition");
        var removePreview = session.PreviewEquipment(Crossing, null, "east");
        Assert(removePreview.Error == null && removePreview.Lines.Any(l => l.StartsWith("Remove author/pack east", StringComparison.Ordinal)), "Removal is previewed: " + String.Join("|", removePreview.Lines));
        Assert(session.RemoveEquipment(Crossing, "east").Applied && f.Live.Single().Record.Id == "west", "Removing one addition releases only it");
        Assert(!session.RemoveEquipment(Crossing, "east").Applied, "Removing an absent addition is refused");
        mapEditor.Undo(); Assert(f.Live.Count == 2 && f.Live.Single(a => a.Record.Id == "east").Position[0] == 20, "Undo of removal rebuilds the addition");
        mapEditor.Undo(); Assert(f.Live.Single(a => a.Record.Id == "west").Position[0] == 10, "Undo of the move restores the placement");
        mapEditor.Undo(); mapEditor.Undo();
        Assert(f.Live.Count == 0 && session.ScenePatchFileText.Contains("\"modifications\": {}"), "Undo of every addition releases all owned equipment");
        mapEditor.Redo(); Assert(f.Live.Single().Record.Id == "west", "Redo rebuilds the addition");
        var removed = session.Remove(Crossing);
        Assert(removed.Applied && f.Live.Count == 0 && f.World.All.All(a => a.Destroyed && !a.InCuller), "Removing the modification releases every owned addition from the controller and culling: " + removed.Error);
        mapEditor.Undo(); Assert(f.Live.Count == 1, "Undo of removal restores the effective equipment");
        Assert(f.World.Live(f.Other).Count() == 0, "No scene patch editing operation touched the other crossing");
    }
    static void EditingRefusalAndFailureRestore() {
        var (f, session, mapEditor, store) = Session();
        var refused = session.AddEquipment(Crossing, West(path: CrossingPath + "/Missing"));
        Assert(!refused.Applied && f.World.Mutations == 0 && mapEditor.UndoGroups.Count == 0 && refused.Error.Contains("not loaded"), "A missing source is refused without a change or history entry: " + refused.Error);
        Assert(session.PreviewEquipment(Crossing, West(path: CrossingPath + "/Missing"), null).Error.Contains("not loaded"), "The preview names the refusal");
        session.AddEquipment(Crossing, West());
        f.World.FailConstructAfter = 0;
        var failed = session.AddEquipment(Crossing, West(20, CrossingPath + "/Signal 2", "east"));
        Assert(!failed.Applied && failed.Error.StartsWith("The edit failed. The object is back as it was. Reason:", StringComparison.Ordinal) && f.Live.Single().Record.Id == "west" && ((JObject)Only(session)["relationships"]["crossingEquipment"]).Count == 1,
            "A failed construction restores the prior equipment and scene patch file: " + failed.Error);
        f.World.FailSetPlacementOnce = true; f.World.StickyDestroy = true;
        failed = session.AddEquipment(Crossing, West(30));
        Assert(!failed.Applied, "A failed move is reported: " + failed.Error);
        f.World.StickyDestroy = false;
        Assert(session.Recheck(Crossing).Applied && f.Live.Single().Position[0] == 10, "A recheck converges the controller on the scene patch file");
    }
    static void EquipmentIdsNameOneAdditionPerScenePatchFile() {
        var f = new Fixture();
        var result = new PatchEngine(f.Scene).Apply(Doc(Equip("here", Addition("west")) + "," + Equip("there", Addition("west", x: 20), OtherPath) + "," + Equip("alone", Addition("solo", x: 30))));
        Assert(!result.Outcomes["here"].Applied && !result.Outcomes["there"].Applied && result.Outcomes["here"].Error.Contains("more than one modification"), "Two modifications sharing an equipment ID both fail: " + result.Outcomes["here"].Error);
        Assert(result.Outcomes["alone"].Applied && f.Live.Single().Record.Id == "solo" && f.World.Live(f.Other).Count() == 0, "Independent work still applies");
        var (g, session, mapEditor, store) = Session();
        Assert(session.AddEquipment(Crossing, West()).Applied, "The first use of an ID is accepted");
        var other = new TargetIdentity("BushnellWhittier", OtherPath, CrossingCoverageScene.ComponentType, CrossingCoverageScene.Structure);
        var refused = session.AddEquipment(other, West(20));
        Assert(!refused.Applied && refused.Error.Contains("already used by another modification") && g.World.Live(g.Other).Count() == 0, "Scene patch editing refuses an equipment ID another modification uses: " + refused.Error);
    }
    static void DetectionAdditionsAndEquipmentWorkTogether() {
        var f = new Fixture();
        string Loc(double d) => "{\"segment\":\"Snew\",\"end\":\"a\",\"distance\":" + N(d) + "}";
        string Range(double a, double b) => "{\"from\":" + Loc(a) + ",\"to\":" + Loc(b) + "}";
        var detection = "\"west\":{\"approachA\":" + Range(308, 550) + ",\"island\":" + Range(292, 308) + ",\"approachB\":" + Range(292, 50) + "}";
        var both = "\"both\":{" + Target() + ",\"relationships\":{\"crossingCoverage\":{\"additions\":{" + detection + "}},\"crossingEquipment\":{" + Addition("west") + "}},\"dependsOn\":[]}";
        var engine = new PatchEngine(f.Scene);
        var result = engine.Apply(Doc(both), "author/pack");
        Assert(result.Outcomes["both"].Applied, "A detection addition and equipment with the same ID apply in one modification: " + result.Outcomes["both"].Error);
        Assert(f.Crossing.Circuits.Count == 2 && f.Live.Single().Record.Id == "west", "The crossing gains one added circuit and one added device");
        engine.Apply(Doc(both), "author/pack");
        Assert(f.Crossing.Circuits.Count == 2 && f.World.Constructed == 1, "Repeating the application duplicates neither");
        var (g, session, mapEditor, store) = Session(Doc(both));
        Assert(session.Blocked == null && g.Crossing.Circuits.Count == 1, "A fresh native map editor world starts without additions");
        Assert(session.Recheck(Crossing).Applied && g.Crossing.Circuits.Count == 2 && g.Live.Count == 1, "Applying the scene patch file builds both additions: " + session.Recheck(Crossing).Error);
        Assert(session.Remove(Crossing).Applied && g.Crossing.Circuits.Count == 1 && g.Live.Count == 0, "Removing the modification deletes the added circuit and releases the equipment");
        mapEditor.Undo(); Assert(g.Crossing.Circuits.Count == 2 && g.Live.Count == 1, "Undo restores both additions");
    }
    // An addition that fails its native check guards only equipment operations: coverage edits of the same crossing, in the
    // native map editor and from a scene patch file, stay available, and an equipment edit is refused before any change.
    static void FaultyEquipmentLeavesCoverageEditsAvailable() {
        var (f, session, mapEditor, store) = Session();
        Assert(session.AddEquipment(Crossing, West()).Applied, "The addition is built");
        f.Live[0].InCuller = false;
        SpanRange Span(double a, double b) => new SpanRange(new TrackLocation("Snew", "a", a), new TrackLocation("Snew", "a", b));
        var cover = session.Cover(Crossing, "Pipr", CircuitCoverage.Tracks(Span(308, 550), Span(292, 308), Span(292, 50)));
        Assert(cover.Applied && f.Crossing.Circuits[0].Spans.All(sp => sp.Owned), "A coverage edit of the crossing applies although its addition is faulty: " + cover.Error);
        Assert(session.Cover(Crossing, "Pipr", null).Applied, "Clearing the coverage applies as well");
        mapEditor.Undo(); mapEditor.Redo();
        Assert(session.IncompleteOperation == null, "Undo and Redo of coverage edits run beside the faulty addition: " + session.IncompleteOperation);
        var mutations = f.World.Mutations;
        var refused = session.AddEquipment(Crossing, West(12));
        Assert(!refused.Applied && refused.Error.Contains("Reload before editing the crossing's equipment") && f.World.Mutations == mutations && f.Live.Single().Position[0] == 10,
            "An equipment edit is refused before any change: " + refused.Error);
        string Loc(double d) => "{\"segment\":\"Snew\",\"end\":\"a\",\"distance\":" + N(d) + "}";
        string Range(double a, double b) => "{\"from\":" + Loc(a) + ",\"to\":" + Loc(b) + "}";
        var g = new Fixture();
        var engine = new PatchEngine(g.Scene);
        engine.Apply(Doc(Equip("equip", Addition("west"))), "author/pack");
        g.Live[0].InCuller = false;
        var coverage = "\"cover\":{" + Target() + ",\"relationships\":{\"crossingCoverage\":{\"circuits\":{\"Pipr\":{\"approachA\":" + Range(308, 550) + ",\"island\":" + Range(292, 308) + ",\"approachB\":" + Range(292, 50) + "}}}},\"dependsOn\":[]}";
        var result = engine.Apply(Doc(coverage + "," + Equip("more", Addition("east", CrossingPath + "/Signal 2", 20))), "other/pack");
        Assert(result.Outcomes["cover"].Applied, "A scene patch file's coverage modification applies beside a faulty addition: " + result.Outcomes["cover"].Error);
        Assert(!result.Outcomes["more"].Applied && result.Outcomes["more"].Error.Contains("Reload before editing the crossing's equipment"), "Its equipment modification is refused: " + result.Outcomes["more"].Error);
    }
    // Use names each device with its crossing, proposes an ID the scene patch file does not use yet, and places the copy at the
    // inspected crossing as its source stands at its own crossing, five metres aside.
    static void TheEditingProposesADistinctIdAndAPlacementAtTheCrossing() {
        var candidate = new SourceCandidate(new EquipmentSource("BushnellWhittier", "World/Roads/Grade Crossing - Allen/Signal 1"), "Crossing Signal Wishbone A (1)", null,
            new double[] { 110, 5, 200 }, new double[] { 0, 0, 0, 1 }, "Signal 1", "Allen", new double[] { 100, 5, 200 }, new double[] { 0, 0, 0, 1 });
        Assert(candidate.Label == "Signal 1 at Allen", "A candidate is labelled with its device and crossing: " + candidate.Label);
        Assert(new SourceCandidate(new EquipmentSource("s", "a/b"), "Model", null).Label == "Model", "Without a name and crossing the model labels it");
        // A crossing named after its place gives that place; a setup crossing takes the place from its enclosing objects.
        Assert(CrossingEquipment.Place("Grade Crossing - Bryson Everett", new[] { "Bryson", "Large Scenery", "World" }) == "Bryson Everett", "A named crossing gives its place");
        Assert(CrossingEquipment.Place("Grade Crossing Setup 2 Tracks", new[] { "Roads Andrews", "World" }) == "Andrews", "A setup crossing in Roads Andrews stands at Andrews");
        Assert(CrossingEquipment.Place("Grade Crossing Setup", new[] { "US19 Gov Island", "Roads Bryson", "World" }) == "US19 Gov Island", "The nearest enclosing place names a setup crossing");
        Assert(CrossingEquipment.Place("Lonely Crossing", new[] { "World" }) == "Lonely Crossing", "Without an enclosing place the crossing keeps its own name");
        // The source stands 10 m along X from its crossing. The destination crossing is turned 90 degrees about Y.
        var quarter = new[] { 0, Math.Sin(Math.PI / 4), 0, Math.Cos(Math.PI / 4) };
        var proposed = EquipmentPlacement.Propose(candidate.Position, candidate.Rotation, candidate.CrossingPosition, candidate.CrossingRotation, new double[] { 4000, 529, 5000 }, quarter);
        bool Near(double[] a, double[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d < 1e-6);
        // X turned by +90 degrees about Y points along -Z: the source offset lands at -10 Z, and the aside 5 m along -Z too.
        Assert(Near(proposed.Position, new double[] { 4000, 529, 4985 }), "The copy keeps its offset from the crossing, turned with the crossing, and stands aside: " + String.Join(", ", proposed.Position));
        Assert(Near(proposed.Rotation, quarter), "The copy turns with the destination crossing: " + String.Join(", ", proposed.Rotation));
        var same = EquipmentPlacement.Propose(candidate.Position, candidate.Rotation, candidate.CrossingPosition, candidate.CrossingRotation, candidate.CrossingPosition, candidate.CrossingRotation);
        Assert(Near(same.Position, new double[] { 115, 5, 200 }), "At its own crossing the copy stands five metres beside its source: " + String.Join(", ", same.Position));
        var unknown = EquipmentPlacement.Propose(candidate.Position, candidate.Rotation, null, null, new double[] { 4000, 529, 5000 }, quarter);
        Assert(Near(unknown.Position, new double[] { 4005, 529, 5000 }) && Near(unknown.Rotation, new double[] { 0, 0, 0, 1 }), "Without the source crossing's placement the copy starts at the destination crossing: " + String.Join(", ", unknown.Position));
        var (f, session, mapEditor, store) = Session();
        var first = session.AddEquipment(Crossing, West(id: "first"));
        var later = session.PreviewEquipment(Crossing, West(30, id: "second"), null);
        Assert(first.Applied && later.Lines.Count == 2 && later.Lines[0].StartsWith("Add author/pack second", StringComparison.Ordinal) && later.Lines[1].StartsWith("Keep author/pack first", StringComparison.Ordinal),
            "A preview lists what changes before what is kept: " + String.Join(" | ", later.Lines));
        Assert(session.NewEquipmentId("signal-1-added") == "signal-1-added", "An unused ID is proposed as it is");
        session.AddEquipment(Crossing, West(id: "signal-1-added"));
        Assert(session.NewEquipmentId("signal-1-added") == "signal-1-added-2", "A used ID gets a numeric suffix");
        session.AddEquipment(Crossing, West(20, CrossingPath + "/Signal 2", "signal-1-added-2"));
        Assert(session.NewEquipmentId("signal-1-added") == "signal-1-added-3" && session.NewEquipmentId("Signal 1 (copy)") == "Signal-1--copy", "Further uses count on, and other characters become dashes");
    }
    // Another supplying mod's additions stand on the same crossing. Editing this mod's equipment or coverage, removing
    // its modification and undoing never release them.
    static void EditingKeepsAnotherModsEquipment() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        var foreign = engine.Apply(Doc(Equip("signals", Addition("theirs", CrossingPath + "/Signal 2", 40))), "other/pack");
        Assert(foreign.Outcomes["signals"].Applied && f.Live.Single().Record.Scope == "other/pack", "The other mod's addition is built at load");
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        bool Theirs() => f.Live.Count(a => a.Record.Scope == "other/pack" && a.Record.Id == "theirs") == 1;
        Assert(session.AddEquipment(Crossing, West()).Applied && Theirs() && f.Live.Count == 2, "Adding this mod's equipment keeps the other mod's");
        SpanRange Span(double a, double b) => new SpanRange(new TrackLocation("Snew", "a", a), new TrackLocation("Snew", "a", b));
        Assert(session.Cover(Crossing, "Pipr", CircuitCoverage.Tracks(Span(308, 550), Span(292, 308), Span(292, 50))).Applied && Theirs(), "A coverage edit keeps the other mod's equipment");
        Assert(session.AddEquipment(Crossing, West(12)).Applied && Theirs(), "A move keeps it");
        Assert(session.RemoveEquipment(Crossing, "west").Applied && Theirs() && f.Live.Count == 1, "Removing this mod's addition keeps it");
        Assert(session.Remove(Crossing).Applied && Theirs(), "Removing this mod's modification keeps it");
        mapEditor.Undo(); mapEditor.Undo();
        Assert(Theirs() && f.Live.Count(a => a.Record.Scope == "author/pack") == 1, "Undo restores this mod's addition beside the other mod's");
        Assert(session.Recheck(Crossing).Applied && Theirs(), "A recheck keeps it");
    }
    // An addition whose source was missing at load has no copy. Scene patch editing still lists it from the scene patch file with its
    // reason, and it can be removed; afterwards edits of the crossing work again.
    static void AFailedAdditionIsListedAndRemovable() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("equip-crossing", Addition("lost", CrossingPath + "/Gone")));
        var load = engine.Apply(scenePatchFile, "author/pack");
        Assert(!load.Outcomes["equip-crossing"].Applied && f.Live.Count == 0, "The addition with a missing source fails at load");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var entries = session.EquipmentEntries(Crossing);
        Assert(entries.Count == 1 && entries[0].Built == null && entries[0].Addition.Id == "lost" && entries[0].Problem.Contains("not loaded"), "The failed addition is listed from the scene patch file with its reason: " + entries.FirstOrDefault()?.Problem);
        var removed = session.RemoveEquipment(Crossing, "lost");
        Assert(removed.Applied && session.EquipmentEntries(Crossing).Count == 0 && !session.ScenePatchFileText.Contains("lost"), "The failed addition is removed from the scene patch file: " + removed.Error);
        Assert(session.AddEquipment(Crossing, West()).Applied && f.Live.Count == 1, "Afterwards the crossing takes equipment again");
    }
    // A move as small as the native map editor stores reaches the scene, and a value the form did not change keeps the scene patch file's
    // literal, so an untouched apply is no change and a one-axis change rewrites only that axis.
    static void SmallMovesReachTheSceneAndUntouchedValuesStay() {
        var (f, session, mapEditor, store) = Session();
        Assert(session.AddEquipment(Crossing, West()).Applied, "The addition is built");
        Assert(session.AddEquipment(Crossing, West(10.003)).Applied && Math.Abs(f.Live.Single().Position[0] - 10.003) < 1e-9, "A 3 mm move reaches the scene: " + f.Live.Single().Position[0]);
        var turn = new EquipmentAddition("west", EquipmentAddition.LightGatePattern, new EquipmentSource("BushnellWhittier", SignalPath), new Placement(new[] { 10.003, 0, 7 }, new[] { 0, Math.Sin(0.05 * Math.PI / 360), 0, Math.Cos(0.05 * Math.PI / 360) }));
        Assert(session.AddEquipment(Crossing, turn).Applied && Math.Abs(f.Live.Single().Rotation[1] - turn.Placement.Rotation[1]) < 1e-12, "A turn of 0.05 degrees reaches the scene");
        var literal = Doc(Equip("equip-crossing", Addition("west").Replace("[10,0,7]", "[10.1234567,0.30000001,7]")));
        var (g, reopened, e2, s2) = Session(literal);
        new PatchEngine(g.Scene).Apply(literal, "author/pack");
        reopened.Recheck(Crossing);
        var asStored = new EquipmentAddition("west", EquipmentAddition.LightGatePattern, new EquipmentSource("BushnellWhittier", SignalPath), new Placement(new[] { (double)(float)10.1234567, (double)(float)0.30000001, 7 }, new double[] { 0, 0, 0, 1 }));
        var untouched = reopened.AddEquipment(Crossing, asStored);
        Assert(untouched.Applied && untouched.Unchanged && reopened.ScenePatchFileText.Contains("10.1234567") && reopened.ScenePatchFileText.Contains("0.30000001"), "An apply with the values as the game stores them is no change: " + reopened.ScenePatchFileText);
        var oneAxis = new EquipmentAddition("west", EquipmentAddition.LightGatePattern, asStored.Source, new Placement(new[] { (double)(float)10.1234567, (double)(float)0.30000001, 9 }, new double[] { 0, 0, 0, 1 }));
        Assert(reopened.AddEquipment(Crossing, oneAxis).Applied && reopened.ScenePatchFileText.Contains("10.1234567") && reopened.ScenePatchFileText.Contains("0.30000001"), "A change of one axis keeps the other axes' literals: " + reopened.ScenePatchFileText);
    }
    static void UnknownSourceOrPlacementContentIsUnsupported() {
        foreach (var unknown in new[] { Addition("west").Replace("\"path\":\"" + SignalPath + "\"", "\"path\":\"" + SignalPath + "\",\"hash\":1"), Addition("west").Replace("\"mode\":\"fixed\",", "\"mode\":\"fixed\",\"snap\":true,") }) {
            var f = new Fixture();
            var result = new PatchEngine(f.Scene).Apply(Doc(Equip("future", unknown)));
            Assert(!result.Outcomes["future"].Applied && result.Outcomes["future"].Error.StartsWith("Unsupported", StringComparison.Ordinal) && f.World.Mutations == 0, "Unknown content in a source or placement is unsupported: " + result.Outcomes["future"].Error);
        }
    }
    static void SaveAndReloadReproduceEquipmentOnce() {
        var (f, session, mapEditor, store) = Session();
        session.AddEquipment(Crossing, West());
        session.AddEquipment(Crossing, West(20, CrossingPath + "/Signal 2", "east"));
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0"), "Save raises the dependency to the crossing-equipment version: " + store.Info);
        var fresh = new Fixture();
        var engine = new PatchEngine(fresh.Scene);
        var reload = engine.Apply(store.Patch, "author/pack");
        Assert(reload.Outcomes.Values.All(o => o.Applied) && fresh.Live.Count == 2 && fresh.Live.All(a => a.Record.Scope == "author/pack"), "A reloaded world reproduces the saved equipment");
        engine.Apply(store.Patch, "author/pack");
        Assert(fresh.Live.Count == 2 && fresh.World.Constructed == 2, "A repeated world-load callback adds no duplicate");
        var (f2, reopened, e2, s2) = Session(store.Patch, fresh);
        Assert(reopened.Blocked == null && reopened.RemoveEquipment(Crossing, "east").Applied && fresh.Live.Single().Record.Id == "west", "A saved equipment scene patch file reopens for editing in the world it built");
        var withUnknown = JObject.Parse(store.Patch);
        withUnknown["futureTopLevel"] = new JObject { ["keep"] = true };
        var (f3, unknown, e3, s3) = Session(withUnknown.ToString());
        Assert(unknown.AddEquipment(Crossing, West(40, id: "north")).Applied && unknown.ScenePatchFileText.Contains("futureTopLevel"), "Unknown scene patch file content survives an equipment edit");
    }
}
