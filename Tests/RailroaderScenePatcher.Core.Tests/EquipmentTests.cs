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
        // The wrapper stays while its copy or player is gone, as when something else destroys the copy object.
        public bool CopyLost;
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
        Mutations++; if (StickyDestroy) return; Gone((Added)addition);
    }
    void Gone(Added a) {
        // Destroying a destroyed object does nothing, as in the game.
        if (a.Destroyed) return;
        a.Destroyed = true; a.InCuller = false; Devices.Remove(a.Copy);
        // A bell releases its observer and its audio; a load still pending is cancelled.
        if (a.Bell != null) { a.Observers = 0; if (a.AudioLoaded) { a.AudioLoaded = false; AudioReferences--; } else if (a.LoadProblem == null) PendingLoads--; }
    }
    // The game unloads the scene of a crossing: every addition beneath it is destroyed with it, without a call from the
    // patcher. A bell gives up its observer and audio as it does when the patcher removes it.
    public void SceneUnloaded(FakeCrossing crossing) { foreach (var a in Live(crossing).ToList()) Gone(a); }
    public bool Present(object addition) => !((Added)addition).Destroyed;
    public bool Built(object addition) => !((Added)addition).CopyLost;
    public Placement Placement(object addition) { var a = (Added)addition; return new Placement((double[])a.Position.Clone(), (double[])a.Rotation.Clone()); }
    public void SetPlacement(object addition, Placement placement) {
        Mutations++; var a = (Added)addition;
        a.Position = (double[])placement.Position.Clone(); a.Rotation = (double[])placement.Rotation.Clone();
        if (FailSetPlacementOnce) { FailSetPlacementOnce = false; throw new Exception("placement injected"); }
    }
    public string Problem(object addition) {
        var a = (Added)addition;
        if (a.CopyLost) return "its copy is missing";
        if (a.Bell != null) return a.LoadProblem != null ? "its audio failed to load: " + a.LoadProblem : a.Observers != 1 ? "it observes its controller's bell key " + a.Observers + " times" : null;
        return a.InCuller ? null : "the copy is not in the controller's culler";
    }
    public string Status(object addition) {
        var a = (Added)addition;
        if (a.CopyLost) return "copy missing";
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
    static string BellAddition(string id, string definition = "rr/bell") =>
        "\"" + id + "\":{\"pattern\":\"" + EquipmentAddition.BellPattern + "\",\"source\":{\"definition\":\"" + definition + "\"},\"placement\":{\"mode\":\"fixed\",\"position\":[30,0,7],\"rotation\":[0,0,0,1]}}";
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
        EquipmentPreviewUsesDisplayNames();
        EditingRefusalAndFailureRestore();
        SaveAndReloadReproduceEquipmentOnce();
        EquipmentIdsNameOneAdditionPerScenePatchFile();
        DetectionAdditionsAndEquipmentWorkTogether();
        FaultyEquipmentLeavesCoverageEditsAvailable();
        TheEditingProposesADistinctIdAndAPlacementAtTheCrossing();
        EditingKeepsAnotherModsEquipment();
        AFailedAdditionIsListedAndRemovable();
        AdditionsInSeparateModificationsAreAllListed();
        AnAdditionWithoutItsCopyIsRebuiltFromItsSource();
        AnAdditionWithoutItsCopyIsRefusedWhileItsSourceIsNotLoaded();
        AnAdditionWithoutItsCopyCanBeRemoved();
        AnotherModsLostCopyNeitherRebuildsNorRefusesThisModsEdits();
        AFailedRebuildLeavesTheAdditionNotBuilt();
        AnImportRollbackAfterAVerifiedRebuildEndsCoherent();
        AnUnverifiedRebuildCleanupGuardsUntilRecheck();
        AStaleWrapperThatStaysAfterItsRebuildGuardsTheCrossing();
        RecheckRemovesAStaleWrapperItsFileNoLongerNames();
        AnotherModsLostCopyStaysThroughFileVersionImportAndHistory();
        SourceRefusalsExplainTheProblem();
        RecheckRefusesARefusedAdditionWithoutMutation();
        RecheckChecksOnlyTheModificationsThatTakeEffect();
        RecheckRefusalKeepsAnIndependentModificationOfTheObject();
        AValidAdditionNamesTheModificationsOtherRefusal();
        ARefusedAdditionCanBeRemovedWhileAnotherStaysRefused();
        RefusedAdditionsCanBeCorrectedOneAfterTheOther();
        AnEarlierRefusalDoesNotShapeALaterFailure();
        ACorrectionIsSavedOnlyWhileAnotherAdditionKeepsTheRefusal();
        UndoToARefusedStateRespectsDependentsAndReportsItsOwnFailure();
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
            if (c.Key == "missing" || c.Key == "absent scene") Assert(error.Contains("is not present in the loaded map") && error.Contains("only copies devices already in this map and will not choose a replacement"), "A missing source neither substitutes nor loads a scene: " + error);
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
    static void EquipmentPreviewUsesDisplayNames() {
        var (_, session, _, _) = Session();
        Assert(session.AddEquipment(Crossing, West()).Applied, "The crossing has a copy of Signal 1");
        const string replacementPath = CrossingPath + "/Signal 2";
        string DisplayName(EquipmentAddition addition) => addition.Source.Path == SignalPath ? "Signal 1 (added)" : "Signal 2";
        EquipmentPreview Preview(EquipmentAddition addition, string id = null) =>
            session.PreviewEquipment(Crossing, addition, id, null, DisplayName);
        void CheckLines(EquipmentPreview preview, params string[] expected) {
            Assert(preview.Error == null && preview.Lines.SequenceEqual(expected),
                "Expected [" + String.Join(" | ", expected) + "]. Got [" + (preview.Error ?? String.Join(" | ", preview.Lines)) + "].");
        }

        // Each line names the addition by its equipment ID, with the display name beside it: additions that share a
        // source read apart.
        CheckLines(Preview(West(path: replacementPath, id: "east")),
            "Add east (Signal 2); the crossing controller drives it.", "Keep west (Signal 1 (added)).");
        CheckLines(Preview(West(path: replacementPath)),
            "Rebuild west (Signal 2); the copy of Signal 1 (added) is removed.");
        CheckLines(Preview(West(12)), "Move west (Signal 1 (added)).");
        CheckLines(Preview(null, "west"), "Remove west (Signal 1 (added)).");
    }
    static void EditingRefusalAndFailureRestore() {
        var (f, session, mapEditor, store) = Session();
        var refused = session.AddEquipment(Crossing, West(path: CrossingPath + "/Missing"));
        Assert(!refused.Applied && f.World.Mutations == 0 && mapEditor.UndoGroups.Count == 0 && refused.Error.Contains("not present in the loaded map"), "A missing source is refused without a change or history entry: " + refused.Error);
        Assert(session.PreviewEquipment(Crossing, West(path: CrossingPath + "/Missing"), null).Error.Contains("not present in the loaded map"), "The preview names the refusal");
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
        Assert(entries.Count == 1 && entries[0].Built == null && entries[0].Addition.Id == "lost" && entries[0].Problem.Contains("not present in the loaded map"), "The failed addition is listed from the scene patch file with its reason: " + entries.FirstOrDefault()?.Problem);
        var removed = session.RemoveEquipment(Crossing, "lost");
        Assert(removed.Applied && session.EquipmentEntries(Crossing).Count == 0 && !session.ScenePatchFileText.Contains("lost"), "The failed addition is removed from the scene patch file: " + removed.Error);
        Assert(session.AddEquipment(Crossing, West()).Applied && f.Live.Count == 1, "Afterwards the crossing takes equipment again");
    }
    // A scene patch file may hold the crossing's coverage and each of its additions in a modification of its own, as the
    // CrossingReroute62 fixture does. The crossing lists every addition of this mod, not only those of its first modification.
    static void AdditionsInSeparateModificationsAreAllListed() {
        var f = new Fixture();
        f.World.Bell("rr/bell", b => b.Name = "Teardrop bell"); f.World.LoadsPending = false;
        var engine = new PatchEngine(f.Scene);
        string Loc(double d) => "{\"segment\":\"Snew\",\"end\":\"a\",\"distance\":" + N(d) + "}";
        string Range(double a, double b) => "{\"from\":" + Loc(a) + ",\"to\":" + Loc(b) + "}";
        var coverage = "\"cover\":{" + Target() + ",\"relationships\":{\"crossingCoverage\":{\"circuits\":{\"Pipr\":{\"approachA\":" + Range(308, 550) + ",\"island\":" + Range(292, 308) + ",\"approachB\":" + Range(292, 50) + "}}}},\"dependsOn\":[]}";
        var scenePatchFile = Doc(coverage + "," + Equip("equip-signal-copy", Addition("west")) + "," + Equip("equip-bell-added", BellAddition("ring")));
        var load = engine.Apply(scenePatchFile, "author/pack");
        Assert(load.Outcomes.Values.All(o => o.Applied) && f.Live.Count == 2, "The coverage and both additions apply at load: " + String.Join("; ", load.Outcomes.Where(o => !o.Value.Applied).Select(o => o.Key + " " + o.Value.Error)));
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var entries = session.EquipmentEntries(Crossing);
        Assert(entries.Select(e => e.Addition.Id).SequenceEqual(new[] { "ring", "west" }) && entries.All(e => e.Built != null && e.Problem == null),
            "Both additions are listed as built, in the ordinal order of their IDs: " + String.Join(", ", entries.Select(e => e.Addition.Id + " " + e.Problem)));
        // The crossing's edit controls are inactive ahead of time with the reason.
        Assert(session.SeveralModificationsBlock(Crossing) == PatchEditingSession.SeveralModifications, "The crossing's edits are blocked ahead of time: " + session.SeveralModificationsBlock(Crossing));
        // Every edit those controls start still refuses with the same reason before any change: Edit and Remove of an addition,
        // Use for a new light, gate or bell, the coverage and detection edits, and the removal of the modification.
        var mutations = f.World.Mutations;
        SpanRange Span(double a, double b) => new SpanRange(new TrackLocation("Snew", "a", a), new TrackLocation("Snew", "a", b));
        var tracks = CircuitCoverage.Tracks(Span(300, 540), Span(292, 300), Span(292, 60));
        var refusals = new (string Control, string Error)[] {
            ("Remove", session.RemoveEquipment(Crossing, "west").Error),
            ("Edit", session.AddEquipment(Crossing, West(12)).Error),
            ("Use", session.AddEquipment(Crossing, West(14, id: "new")).Error),
            ("Equipment preview", session.PreviewEquipment(Crossing, West(12), null).Error),
            ("Coverage preview", session.PreviewCoverage(Crossing, "Pipr", tracks).Error),
            ("Coverage", session.Cover(Crossing, "Pipr", tracks).Error),
            ("Add detection", session.AddDetection(Crossing, "extra", tracks).Error),
            ("Remove modification", session.Remove(Crossing).Error),
        };
        foreach (var refusal in refusals)
            Assert(refusal.Error != null && refusal.Error.Contains(PatchEditingSession.SeveralModifications), refusal.Control + " still refuses with the same reason: " + refusal.Error);
        Assert(f.World.Mutations == mutations && f.Live.Count == 2, "The refusals change nothing");
        var single = Session(Doc(Equip("equip-crossing", Addition("west"))));
        Assert(single.session.SeveralModificationsBlock(Crossing) == null, "One modification of the crossing blocks nothing");
    }
    // An added light, gate or bell whose copy is gone while its wrapper stays counts as not built: the editing lists it with
    // its reason, and Recheck rebuilds it from its source like a fresh construction, without a refusal or a reload.
    static void AnAdditionWithoutItsCopyIsRebuiltFromItsSource() {
        foreach (var bell in new[] { false, true }) {
            var f = new Fixture();
            if (bell) { f.World.Bell("rr/bell", b => b.Name = "Teardrop bell"); f.World.LoadsPending = false; }
            var engine = new PatchEngine(f.Scene);
            var scenePatchFile = Doc(Equip("equip-crossing", bell ? BellAddition("ring") : Addition("west")));
            Assert(engine.Apply(scenePatchFile, "author/pack").Outcomes["equip-crossing"].Applied && f.Live.Count == 1, "The addition is built at load");
            var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
            var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
            var lost = f.Live[0]; lost.CopyLost = true;
            var entries = session.EquipmentEntries(Crossing);
            Assert(entries.Count == 1 && entries[0].Built == null && entries[0].Problem == "it is not built in this world; Recheck builds it",
                "An addition without its copy is listed as not built with what to do: " + entries.FirstOrDefault()?.Problem);
            var recheck = session.Recheck(Crossing);
            Assert(recheck.Applied && lost.Destroyed && f.Live.Count == 1 && !f.Live[0].CopyLost && f.World.Constructed == 2,
                "Recheck replaces the wrapper without its copy by a fresh construction from its source: " + recheck.Error);
            Assert(session.EquipmentEntries(Crossing).Single().Built != null && !session.CleanupUnverified(Crossing), "Afterwards the addition is built and the crossing stays editable");
        }
        var next = new Fixture();
        var write = new PatchEngine(next.Scene);
        write.Apply(Doc(Equip("signals", Addition("west"))), "author/pack");
        next.Live[0].CopyLost = true;
        var moved = write.Apply(Doc(Equip("signals", Addition("west", x: 12))), "author/pack");
        Assert(moved.Outcomes["signals"].Applied && next.Live.Count == 1 && !next.Live[0].CopyLost && next.Live[0].Position[0] == 12 && next.World.Constructed == 2,
            "The next write rebuilds an addition without its copy at its placement: " + moved.Outcomes["signals"].Error);
    }
    // Without its source loaded, the addition cannot be rebuilt: Recheck refuses before any change and names the source.
    static void AnAdditionWithoutItsCopyIsRefusedWhileItsSourceIsNotLoaded() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("equip-crossing", Addition("west")));
        engine.Apply(scenePatchFile, "author/pack");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var lost = f.Live[0]; lost.CopyLost = true;
        f.World.Devices.RemoveAll(d => d.Path == SignalPath);
        var mutations = f.World.Mutations;
        var recheck = session.Recheck(Crossing);
        const string Refusal = "Equipment west: the added equipment is missing, and source device Signal 1 is not present in the loaded map, so the equipment cannot be rebuilt. The patcher only copies devices already in this map and will not choose a replacement. Select a device present in this map or remove the addition";
        Assert(!recheck.Applied && recheck.Failure == FailureKind.Refused && recheck.Error.Contains(Refusal),
            "Recheck refuses the rebuild and names the source by its name, with the standard tail: " + recheck.Failure + " " + recheck.Error);
        Assert(f.World.Mutations == mutations && !lost.Destroyed && !session.CleanupUnverified(Crossing), "The refusal changes nothing");
        var entry = session.EquipmentEntries(Crossing).Single();
        Assert(entry.Built == null && entry.Problem.Contains(Refusal), "The listed addition gives the refusal that names its source: " + entry.Problem);
        var b = new Fixture();
        b.World.Bell("rr/bell", d => d.Name = "Teardrop bell"); b.World.LoadsPending = false;
        var bellEngine = new PatchEngine(b.Scene);
        var bellFile = Doc(Equip("equip-crossing", BellAddition("ring")));
        bellEngine.Apply(bellFile, "author/pack");
        var bellStore = new Store { Patch = bellFile }; var bellEditor = new NativeMapEditor();
        var bellSession = new PatchEditingSession(bellEngine, bellEditor, bellStore, scenePatchFileKey: "author/pack"); bellEditor.Session = bellSession;
        b.Live.Single().CopyLost = true;
        b.World.Bells.Clear();
        var bellRecheck = bellSession.Recheck(Crossing);
        Assert(!bellRecheck.Applied && bellRecheck.Failure == FailureKind.Refused && bellRecheck.Error.Contains("Equipment ring: the added equipment is missing, and bell definition bell is unavailable in the content library, so the equipment cannot be rebuilt. The patcher only uses bell definitions already available and will not choose a replacement. Select an available bell definition or remove the addition"),
            "A lost bell whose definition is unavailable is refused with its recorded name and a next step: " + bellRecheck.Error);
    }
    static void AnAdditionWithoutItsCopyCanBeRemoved() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("equip-crossing", Addition("west") + "," + Addition("east", CrossingPath + "/Signal 2", 20)));
        engine.Apply(scenePatchFile, "author/pack");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var lost = f.Live.Single(a => a.Record.Id == "west"); lost.CopyLost = true;
        var removed = session.RemoveEquipment(Crossing, "west");
        Assert(removed.Applied && lost.Destroyed && f.Live.Count == 1 && f.Live[0].Record.Id == "east" && !session.ScenePatchFileText.Contains("\"west\""),
            "Removing an addition without its copy removes its wrapper and its entry: " + removed.Error);
    }
    // A lost copy belongs to its own mod: another mod's equipment write neither rebuilds it nor is refused because of it,
    // with its source loaded or not. Its own mod's Recheck rebuilds it, or refuses with its own error.
    static void AnotherModsLostCopyNeitherRebuildsNorRefusesThisModsEdits() {
        foreach (var loaded in new[] { true, false }) {
            var f = new Fixture();
            var engine = new PatchEngine(f.Scene);
            var theirsFile = Doc(Equip("signals", Addition("theirs", CrossingPath + "/Signal 2", 40)));
            Assert(engine.Apply(theirsFile, "other/pack").Outcomes["signals"].Applied, "The other mod's addition is built at load");
            var store = new Store(); var mapEditor = new NativeMapEditor();
            var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
            var theirs = f.Live.Single(); theirs.CopyLost = true;
            if (!loaded) f.World.Devices.RemoveAll(d => d.Path == CrossingPath + "/Signal 2");
            var constructed = f.World.Constructed;
            var added = session.AddEquipment(Crossing, West());
            Assert(added.Applied, "This mod's addition applies beside the other mod's lost copy (source loaded: " + loaded + "): " + added.Error);
            Assert(f.World.Constructed == constructed + 1 && !theirs.Destroyed && theirs.CopyLost, "This mod's write leaves the other mod's lost copy as it is");
            Assert(session.AddEquipment(Crossing, West(12)).Applied && !theirs.Destroyed && theirs.CopyLost, "A move of this mod's addition leaves it too");
            Assert(session.Recheck(Crossing).Applied && !theirs.Destroyed && theirs.CopyLost, "This mod's Recheck leaves it too");
            var otherStore = new Store { Patch = theirsFile }; var otherEditor = new NativeMapEditor();
            var other = new PatchEditingSession(engine, otherEditor, otherStore, scenePatchFileKey: "other/pack"); otherEditor.Session = other;
            var recheck = other.Recheck(Crossing);
            if (loaded) Assert(recheck.Applied && theirs.Destroyed && f.Live.Count(a => a.Record.Scope == "other/pack" && !a.CopyLost) == 1, "Its own mod's Recheck rebuilds it: " + recheck.Error);
            else Assert(!recheck.Applied && recheck.Failure == FailureKind.Refused && recheck.Error.Contains("Equipment theirs: the added equipment is missing, and source device Signal 2 is not present in the loaded map") && !theirs.Destroyed,
                "Its own mod's Recheck refuses with its own error: " + recheck.Error);
        }
    }
    // A rebuild that fails, in construction or in verification, is restored to "not built": the scene patch file keeps the
    // addition, its row offers Recheck, and nothing reports an incomplete cleanup.
    static void AFailedRebuildLeavesTheAdditionNotBuilt() {
        foreach (var verification in new[] { false, true }) {
            var f = new Fixture();
            var engine = new PatchEngine(f.Scene);
            var scenePatchFile = Doc(Equip("signals", Addition("west")));
            engine.Apply(scenePatchFile, "author/pack");
            var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
            var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
            f.Live.Single().CopyLost = true;
            if (verification) f.World.ConstructOffset = 0.125; else f.World.FailConstructAfter = 0;
            var moved = session.AddEquipment(Crossing, West(12));
            Assert(!moved.Applied && moved.Failure == FailureKind.Restored, "A failed rebuild is restored (verification: " + verification + "): " + moved.Failure + " " + moved.Error);
            Assert(!f.Live.Any(a => !a.CopyLost) && !session.CleanupUnverified(Crossing), "Nothing built stays and no cleanup is unverified");
            var entry = session.EquipmentEntries(Crossing).Single();
            Assert(entry.Built == null && entry.Problem == "it is not built in this world; Recheck builds it", "The addition stays in the scene patch file as not built: " + entry.Problem);
            f.World.ConstructOffset = 0;
            Assert(session.Recheck(Crossing).Applied && f.Live.Count(a => !a.CopyLost) == 1, "Recheck builds it afterwards");
        }
    }
    // A rebuild that verified is rolled back with the rest of an Import when a later target fails. The crossing ends with the
    // addition not built and coherent: no incomplete cleanup is reported and nothing is guarded.
    static void AnImportRollbackAfterAVerifiedRebuildEndsCoherent() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("a-crossing", Addition("west")));
        engine.Apply(scenePatchFile, "author/pack");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        f.Live.Single().CopyLost = true;
        store.Patch = Doc(Equip("a-crossing", Addition("west", x: 12)) + "," + Equip("b-other", Addition("north", x: 30), OtherPath));
        session.CheckFileVersion();
        Assert(session.OpenImport().Applied, "The Import overview opens");
        f.World.FailConstructAfter = 1;
        var import = session.ApplyImport();
        Assert(!import.Applied && import.Error.StartsWith("Scene patch editing is back as it was before Import.", StringComparison.Ordinal) && !import.Error.Contains("could not be verified"),
            "The Import is rolled back without an incomplete cleanup: " + import.Error);
        Assert(f.World.Constructed >= 2 && !session.CleanupUnverified(Crossing) && !session.CleanupUnverified(new TargetIdentity("BushnellWhittier", OtherPath, CrossingCoverageScene.ComponentType, CrossingCoverageScene.Structure)),
            "The rebuild happened and nothing is guarded after the rollback");
        Assert(!f.World.Live(f.Crossing).Any(a => !a.CopyLost) && !f.World.Live(f.Other).Any(), "The rebuilt copy and the failed addition are gone");
        var entry = session.EquipmentEntries(Crossing).Single();
        Assert(entry.Built == null && entry.Problem == "it is not built in this world; Recheck builds it", "The addition is listed as not built: " + entry.Problem);
    }
    // When cleanup after a failed rebuild cannot be verified, the crossing is guarded; Recheck then rebuilds the lost copy
    // and lifts the guard.
    static void AnUnverifiedRebuildCleanupGuardsUntilRecheck() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("signals", Addition("west")));
        engine.Apply(scenePatchFile, "author/pack");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var lost = f.Live.Single(); lost.CopyLost = true;
        f.World.ConstructOffset = 0.125; f.World.StickyDestroy = true;
        var failed = session.Recheck(Crossing);
        Assert(!failed.Applied && failed.Failure == FailureKind.IncompleteCleanup && session.CleanupUnverified(Crossing), "An unverified cleanup guards the crossing: " + failed.Failure + " " + failed.Error);
        Assert(!session.AddEquipment(Crossing, West(20, CrossingPath + "/Signal 2", "east")).Applied, "Another edit is blocked while it is guarded");
        f.World.ConstructOffset = 0; f.World.StickyDestroy = false;
        var recheck = session.Recheck(Crossing);
        Assert(recheck.Applied && !session.CleanupUnverified(Crossing) && lost.Destroyed && f.Live.Count == 1 && !f.Live[0].CopyLost && f.Live[0].Position[0] == 10,
            "Recheck rebuilds the lost copy at its placement and lifts the guard: " + recheck.Error);
    }
    // A cleared wrapper that a destroy leaves standing fails verification, so the rebuild guards the crossing instead of
    // verifying. Status reads the rebuilt addition, not the stale wrapper of the same ID.
    static void AStaleWrapperThatStaysAfterItsRebuildGuardsTheCrossing() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("signals", Addition("west")));
        engine.Apply(scenePatchFile, "author/pack");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var lost = f.Live.Single(); lost.CopyLost = true;
        f.World.StickyDestroy = true;
        var failed = session.Recheck(Crossing);
        Assert(!failed.Applied && failed.Failure == FailureKind.IncompleteCleanup && session.CleanupUnverified(Crossing) && failed.Error.Contains("equipment west left an empty object at this crossing that the patcher could not remove"),
            "A stale wrapper that stays after its rebuild is reported: " + failed.Failure + " " + failed.Error);
        Assert(f.Live.Count(a => !a.CopyLost) == 1 && f.Scene.EquipmentStatus(f.Crossing, "author/pack", "west") == "lamp and gate observers registered",
            "Status reads the rebuilt addition: " + f.Scene.EquipmentStatus(f.Crossing, "author/pack", "west"));
        var again = session.Recheck(Crossing);
        Assert(!again.Applied && again.Failure == FailureKind.IncompleteCleanup && session.CleanupUnverified(Crossing) && !lost.Destroyed && f.Live.Count(a => !a.CopyLost) == 1,
            "A second Recheck while the destroy is still stuck stays guarded and builds no extra copy: " + again.Failure + " " + again.Error);
        f.World.StickyDestroy = false;
        var recheck = session.Recheck(Crossing);
        Assert(recheck.Applied && lost.Destroyed && !session.CleanupUnverified(Crossing) && f.Live.Count == 1, "Recheck removes the stale wrapper and lifts the guard: " + recheck.Error);
    }
    // Recheck removes owned state its scene patch file no longer names, a stale wrapper included, although the observation
    // without it equals the desired state.
    static void RecheckRemovesAStaleWrapperItsFileNoLongerNames() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        engine.Apply(Doc(Equip("signals", Addition("west"))), "author/pack");
        var lost = f.Live.Single(); lost.CopyLost = true;
        var store = new Store { Patch = Doc(Equip("other", Addition("north", x: 30), OtherPath)) }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var recheck = session.Recheck(Crossing);
        Assert(recheck.Applied && lost.Destroyed && !f.World.Live(f.Crossing).Any(), "Recheck removes the stale wrapper the file no longer names: " + recheck.Error);
    }
    // Another mod's lost copy stays as it is through this mod's Use file version, Import, and native Undo and Redo.
    static void AnotherModsLostCopyStaysThroughFileVersionImportAndHistory() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        Assert(engine.Apply(Doc(Equip("signals", Addition("theirs", CrossingPath + "/Signal 2", 40))), "other/pack").Outcomes["signals"].Applied, "The other mod's addition is built at load");
        var store = new Store { Patch = Doc(Equip("equip-crossing", Addition("west"))) }; var mapEditor = new NativeMapEditor();
        engine.Apply(store.Patch, "author/pack");
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var theirs = f.Live.Single(a => a.Record.Scope == "other/pack"); theirs.CopyLost = true;
        bool Untouched() => !theirs.Destroyed && theirs.CopyLost && f.Live.Count(a => a.Record.Scope == "other/pack") == 1;
        store.Patch = Doc(Equip("equip-crossing", Addition("west", x: 12))); session.CheckFileVersion();
        var used = session.UseFileVersion();
        Assert(used.Applied && Untouched(), "Use file version leaves the other mod's lost copy: " + used.Error);
        store.Patch = Doc(Equip("equip-crossing", Addition("west", x: 14))); session.CheckFileVersion();
        Assert(session.OpenImport().Applied, "The Import overview opens");
        var imported = session.ApplyImport();
        Assert(imported.Applied && Untouched(), "Import leaves it: " + imported.Error);
        Assert(session.AddEquipment(Crossing, West(16)).Applied && Untouched(), "An edit leaves it");
        var constructed = f.World.Constructed; var mutations = f.World.Mutations;
        var recheck = session.Recheck(Crossing);
        Assert(recheck.Applied && Untouched() && f.World.Constructed == constructed && f.World.Mutations == mutations,
            "This mod's Recheck leaves it and applies nothing for it: " + recheck.Error);
        mapEditor.Undo();
        Assert(Untouched() && f.Live.Single(a => a.Record.Scope == "author/pack").Position[0] == 14, "Undo leaves it");
        mapEditor.Redo();
        Assert(Untouched() && f.Live.Single(a => a.Record.Scope == "author/pack").Position[0] == 16, "Redo leaves it");
    }
    // Source refusals explain the problem; content refusals also say what the modder can do.
    static void SourceRefusalsExplainTheProblem() {
        var f = new Fixture();
        f.World.Device(SignalPath + " twin"); f.World.Device(CrossingPath + "/Twin"); f.World.Device(CrossingPath + "/Twin");
        f.World.Device(CrossingPath + "/Bell", d => d.Facts.Bells = 1);
        var at = new Placement(new[] { 10.0, 0, 7 }, new[] { 0.0, 0, 0, 1 });
        string Refusal(EquipmentAddition addition) {
            try { f.Scene.CheckRelationships(f.Crossing, new ObjectState(null, null, null, null, new CrossingEquipment(new[] { addition }, false))); return null; }
            catch (InvalidOperationException e) { return e.Message; }
        }
        var device = new EquipmentSource("BushnellWhittier", SignalPath);
        var cases = new[] {
            (Refusal(new EquipmentAddition("x", "crossing-custom-v1", device, at, "author/pack")), "Equipment x: pattern crossing-custom-v1 is not supported. Use pattern crossing-light-gate-v1 or crossing-bell-v1. Otherwise remove the addition"),
            (Refusal(new EquipmentAddition("x", EquipmentAddition.LightGatePattern, EquipmentSource.ForDefinition("rr/bell"), at, "author/pack")), "Equipment x: a light or gate needs an object in the map as its source, not a definition"),
            (Refusal(new EquipmentAddition("x", EquipmentAddition.LightGatePattern, new EquipmentSource("BushnellWhittier", CrossingPath + "/Twin"), at, "author/pack")), "Equipment x: source Twin is ambiguous; 2 loaded objects share that source. Select another source or remove the addition"),
            (Refusal(new EquipmentAddition("x", EquipmentAddition.LightGatePattern, new EquipmentSource("BushnellWhittier", CrossingPath + "/Bell"), at, "author/pack")), ". Select an eligible source or remove the addition"),
            (Refusal(new EquipmentAddition("x", EquipmentAddition.LightGatePattern, device, null, "author/pack")), "Equipment x needs a fixed placement. Give it a position and rotation or remove the addition"),
            (Refusal(new EquipmentAddition("x", EquipmentAddition.BellPattern, device, at, "author/pack")), "Equipment x: a bell needs a bell definition as its source, not an object in the map"),
        };
        foreach (var (error, tail) in cases) Assert(error != null && error.EndsWith(tail, StringComparison.Ordinal), "The refusal gives its reason and any next step: " + error);
        Assert(cases[3].Item1.StartsWith("Equipment x: source Bell does not conform to " + EquipmentAddition.LightGatePattern, StringComparison.Ordinal), "A non-conforming source is named: " + cases[3].Item1);
    }
    // A modification whose additions include one with a nonconforming source is refused as one coherent operation, as the
    // world load refuses it. Recheck refuses it the same way before any change: nothing was mutated, so no cleanup is
    // incomplete and the crossing stays editable. The valid additions say why they are not built instead of offering Recheck.
    static void RecheckRefusesARefusedAdditionWithoutMutation() {
        var f = new Fixture();
        f.World.Device("World/Rejected/bell", d => d.Facts.Bells = 1);
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("equip-crossing", Addition("bell-as-light-a", "World/Rejected/bell", 5) + "," + Addition("west") + "," + Addition("east", CrossingPath + "/Signal 2", 20)));
        var load = engine.Apply(scenePatchFile, "author/pack");
        Assert(!load.Outcomes["equip-crossing"].Applied && f.World.Mutations == 0 && f.Live.Count == 0, "The world load refuses the modification before any change: " + load.Outcomes["equip-crossing"].Error);
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var entries = session.EquipmentEntries(Crossing);
        var rejected = entries.Single(e => e.Addition.Id == "bell-as-light-a");
        Assert(rejected.Built == null && rejected.Problem.Contains("does not conform to " + EquipmentAddition.LightGatePattern), "The rejected addition gives its own reason: " + rejected.Problem);
        foreach (var valid in entries.Where(e => e.Addition.Id != "bell-as-light-a"))
            Assert(valid.Built == null && !valid.Problem.Contains("Recheck builds it") && valid.Problem.Contains("bell-as-light-a"),
                "A valid addition that the refusal stops names the refused addition instead of offering Recheck: " + valid.Problem);
        var recheck = session.Recheck(Crossing);
        Assert(!recheck.Applied && recheck.Failure == FailureKind.Refused && recheck.Remaining.Count == 0 && recheck.Error.Contains("does not conform to " + EquipmentAddition.LightGatePattern),
            "Recheck refuses the modification with the rejected addition's reason: " + recheck.Failure + " " + recheck.Error);
        Assert(f.World.Mutations == 0 && f.Live.Count == 0 && !session.CleanupUnverified(Crossing), "Recheck changed nothing and leaves no unverified cleanup");
        var removed = session.RemoveEquipment(Crossing, "bell-as-light-a");
        Assert(removed.Applied && f.Live.Count == 2 && f.Live.All(a => a.Record.Id != "bell-as-light-a"), "Removing the rejected addition builds the valid ones: " + removed.Error);
    }
    // Recheck checks exactly the modifications that take effect on the object, as the desired state overlays them. An entry
    // that shares an equipment ID, one whose dependency is missing and one the patcher does not execute are left out, so
    // none of them refuses a Recheck that clears a real unverified cleanup.
    static void RecheckChecksOnlyTheModificationsThatTakeEffect() {
        var f = new Fixture();
        f.World.Device("World/Rejected/bell", d => d.Facts.Bells = 1);
        var engine = new PatchEngine(f.Scene);
        f.World.FailConstructAfter = 1; f.World.StickyDestroy = true;
        var scenePatchFile = Doc(Equip("equip-crossing", Addition("west") + "," + Addition("east", CrossingPath + "/Signal 2", 20))
            + "," + Equip("a-shared", Addition("twin", "World/Rejected/bell", 5)) + "," + Equip("b-shared", Addition("twin", x: 6), OtherPath)
            + "," + Equip("c-orphan", Addition("orphan", "World/Rejected/bell", 7), deps: "[\"absent\"]")
            + "," + EquipRaw("d-unsupported", "{" + Addition("odd", "World/Rejected/bell", 8, extra: ",\"colour\":\"red\"") + "}"));
        var load = engine.Apply(scenePatchFile, "author/pack");
        Assert(load.Outcomes["equip-crossing"].Failure == FailureKind.IncompleteCleanup, "The valid modification leaves an incomplete cleanup at load: " + load.Outcomes["equip-crossing"].Error);
        Assert(!load.Outcomes["a-shared"].Applied && !load.Outcomes["c-orphan"].Applied && !load.Outcomes["d-unsupported"].Applied, "The other entries are not applied at load");
        f.World.StickyDestroy = false;
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        Assert(session.CleanupUnverified(Crossing), "The crossing is guarded");
        var recheck = session.Recheck(Crossing);
        Assert(recheck.Applied && !session.CleanupUnverified(Crossing), "Recheck ignores the entries that take no effect and clears the guard: " + recheck.Failure + " " + recheck.Error);
        Assert(f.Live.Select(a => a.Record.Id).OrderBy(i => i, StringComparer.Ordinal).SequenceEqual(new[] { "east", "west" }), "Only the effective additions stand: " + String.Join(", ", f.Live.Select(a => a.Record.Id)));
    }
    // A refused modification beside an independent valid one on the same object: the world load applies the valid one, and
    // Recheck refuses with the refused one's reason before any change, so the valid one keeps its state.
    static void RecheckRefusalKeepsAnIndependentModificationOfTheObject() {
        var f = new Fixture();
        f.World.Device("World/Rejected/bell", d => d.Facts.Bells = 1);
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("a-refused", Addition("bad", "World/Rejected/bell", 5)) + "," + Equip("b-valid", Addition("west")));
        var load = engine.Apply(scenePatchFile, "author/pack");
        Assert(!load.Outcomes["a-refused"].Applied && load.Outcomes["b-valid"].Applied && f.Live.Single().Record.Id == "west", "The load applies the independent modification");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        f.World.Mutations = 0;
        var recheck = session.Recheck(Crossing);
        Assert(!recheck.Applied && recheck.Failure == FailureKind.Refused && recheck.Error.Contains("does not conform"), "Recheck refuses with the refused modification's reason: " + recheck.Failure + " " + recheck.Error);
        Assert(f.World.Mutations == 0 && f.Live.Single().Record.Id == "west" && !session.CleanupUnverified(Crossing), "The independent modification keeps its addition and nothing is guarded");
    }
    // A valid addition of a modification that is refused for another reason than an addition, such as its detection or a
    // missing dependency, names that reason instead of offering a Recheck that refuses the same way.
    static void AValidAdditionNamesTheModificationsOtherRefusal() {
        string Loc(double d) => "{\"segment\":\"Sgone\",\"end\":\"a\",\"distance\":" + N(d) + "}";
        string Range(double a, double b) => "{\"from\":" + Loc(a) + ",\"to\":" + Loc(b) + "}";
        var detection = "\"north\":{\"approachA\":" + Range(308, 550) + ",\"island\":" + Range(292, 308) + ",\"approachB\":" + Range(292, 50) + "}";
        var cases = new Dictionary<string, string> {
            ["detection"] = "\"both\":{" + Target() + ",\"relationships\":{\"crossingCoverage\":{\"additions\":{" + detection + "}},\"crossingEquipment\":{" + Addition("west") + "}},\"dependsOn\":[]}",
            ["dependency"] = Equip("both", Addition("west"), deps: "[\"absent\"]"),
        };
        foreach (var c in cases) {
            var f = new Fixture();
            var engine = new PatchEngine(f.Scene);
            var load = engine.Apply(Doc(c.Value), "author/pack");
            Assert(!load.Outcomes["both"].Applied && f.Live.Count == 0, c.Key + ": the load refuses the modification: " + load.Outcomes["both"].Error);
            var store = new Store { Patch = Doc(c.Value) }; var mapEditor = new NativeMapEditor();
            var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
            var entry = session.EquipmentEntries(Crossing).Single();
            Assert(entry.Built == null && entry.Problem != null && !entry.Problem.Contains("Recheck builds it"), c.Key + ": the valid addition names the refusal: " + entry.Problem);
            if (c.Key == "dependency") Assert(entry.Problem.Contains("absent"), "It names the missing dependency: " + entry.Problem);
        }
    }
    // Two refused additions beside two valid ones: removing one refused addition is previewed and applied while the other
    // keeps the modification refused, so nothing is built; removing the second builds the valid ones. Undo puts the
    // refused addition back, refused again, and the scene keeps what it holds.
    static void ARefusedAdditionCanBeRemovedWhileAnotherStaysRefused() {
        var f = new Fixture();
        f.World.Device("World/Rejected/bell", d => d.Facts.Bells = 1);
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("equip-crossing", Addition("bell-as-light-a", "World/Rejected/bell", 5) + "," + Addition("bell-as-light-b", "World/Rejected/bell", 6)
            + "," + Addition("west") + "," + Addition("east", CrossingPath + "/Signal 2", 20)));
        var load = engine.Apply(scenePatchFile, "author/pack");
        Assert(!load.Outcomes["equip-crossing"].Applied && f.Live.Count == 0, "The load refuses the modification");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        f.World.Mutations = 0;
        var preview = session.PreviewEquipment(Crossing, null, "bell-as-light-a");
        Assert(preview.Error == null && preview.Lines.Count == 1 && preview.Lines[0].Contains("bell-as-light-a") && preview.Lines[0].Contains("bell-as-light-b"),
            "The removal preview names the removed addition and the one that keeps the modification refused: " + preview.Error + String.Join("|", preview.Lines));
        // Both refused additions copy one source: the removal preview names the removed one by its equipment ID.
        var named = session.PreviewEquipment(Crossing, null, "bell-as-light-a", null, addition => "Crossing Bell at Rejected");
        Assert(named.Error == null && named.Lines.Single().StartsWith("Remove bell-as-light-a (Crossing Bell at Rejected) from the scene patch file.", StringComparison.Ordinal),
            "The removal preview names the addition by its equipment ID beside its display name: " + named.Error + String.Join("|", named.Lines));
        var first = session.RemoveEquipment(Crossing, "bell-as-light-a");
        Assert(first.Applied && f.World.Mutations == 0 && f.Live.Count == 0 && !session.ScenePatchFileText.Contains("bell-as-light-a") && session.ScenePatchFileText.Contains("bell-as-light-b"),
            "Removing one refused addition changes the scene patch file only: " + first.Error);
        Assert(session.Refusal(Crossing) != null && session.Refusal(Crossing).Contains("bell-as-light-b"), "The modification stays refused for the other addition: " + session.Refusal(Crossing));
        var entries = session.EquipmentEntries(Crossing);
        Assert(entries.Count == 3 && entries.Where(e => e.Addition.Id != "bell-as-light-b").All(e => e.Problem.Contains("bell-as-light-b")), "The valid additions name the remaining refused one");
        mapEditor.Undo();
        Assert(session.ScenePatchFileText.Contains("bell-as-light-a") && f.Live.Count == 0 && f.World.Mutations == 0, "Undo puts the removed addition back without building anything");
        Assert(session.IncompleteOperation == null && session.Refusal(Crossing) != null && session.Refusal(Crossing).Contains("bell-as-light-a"),
            "Undo leaves no incomplete operation, and the refusal names the restored addition: " + session.IncompleteOperation + " / " + session.Refusal(Crossing));
        mapEditor.Redo();
        Assert(!session.ScenePatchFileText.Contains("bell-as-light-a") && f.Live.Count == 0, "Redo removes it again");
        var second = session.RemoveEquipment(Crossing, "bell-as-light-b");
        Assert(second.Applied && f.Live.Select(a => a.Record.Id).OrderBy(i => i, StringComparer.Ordinal).SequenceEqual(new[] { "east", "west" }) && session.Refusal(Crossing) == null,
            "Removing the last refused addition builds the valid ones: " + second.Error);
        // Undo of the final removal returns the crossing to the refused modification: the built additions go again, as at load.
        mapEditor.Undo();
        Assert(session.IncompleteOperation == null && f.Live.Count == 0 && session.ScenePatchFileText.Contains("bell-as-light-b") && session.Refusal(Crossing) != null && session.Refusal(Crossing).Contains("bell-as-light-b"),
            "Undo of the final removal releases the built additions and keeps the modification refused: " + session.IncompleteOperation + " / " + session.Refusal(Crossing) + " / " + f.Live.Count);
        mapEditor.Redo();
        Assert(session.IncompleteOperation == null && f.Live.Count == 2 && session.Refusal(Crossing) == null, "Redo builds the valid additions again");
    }
    // Two refused additions corrected one after the other: the first correction is saved while the other keeps the
    // modification refused, so nothing is built; after the second correction every addition is built. A correction that
    // is itself refused is refused before any change.
    static void RefusedAdditionsCanBeCorrectedOneAfterTheOther() {
        var f = new Fixture();
        f.World.Device("World/Rejected/bell", d => d.Facts.Bells = 1);
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("equip-crossing", Addition("bell-as-light-a", "World/Rejected/bell", 5) + "," + Addition("bell-as-light-b", "World/Rejected/bell", 6)
            + "," + Addition("west") + "," + Addition("east", CrossingPath + "/Signal 2", 20)));
        engine.Apply(scenePatchFile, "author/pack");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        f.World.Mutations = 0;
        var stillBad = session.AddEquipment(Crossing, West(9, "World/Rejected/bell", "bell-as-light-a"));
        Assert(!stillBad.Applied && stillBad.Failure == FailureKind.Refused && stillBad.Error.Contains("bell-as-light-a") && !stillBad.Error.Contains("bell-as-light-b") && f.World.Mutations == 0
            && session.EquipmentEntries(Crossing).Single(e => e.Addition.Id == "bell-as-light-a").Addition.Placement.Position[0] == 5,
            "A correction that is itself refused is refused with its own reason and the scene patch file keeps the entry: " + stillBad.Error);
        var preview = session.PreviewEquipment(Crossing, West(5, SignalPath, "bell-as-light-a"), null);
        Assert(preview.Error == null && preview.Lines.Count == 1 && preview.Lines[0].Contains("bell-as-light-a") && preview.Lines[0].Contains("bell-as-light-b"),
            "The preview of the first correction names the addition and the one that keeps the modification refused: " + preview.Error + String.Join("|", preview.Lines));
        var named = session.PreviewEquipment(Crossing, West(5, SignalPath, "bell-as-light-a"), null, null, addition => "Signal 1");
        Assert(named.Error == null && named.Lines.Single().StartsWith("Save bell-as-light-a (Signal 1) in the scene patch file.", StringComparison.Ordinal),
            "The correction preview names the addition by its equipment ID beside its display name: " + named.Error + String.Join("|", named.Lines));
        var first = session.AddEquipment(Crossing, West(5, SignalPath, "bell-as-light-a"));
        Assert(first.Applied && f.World.Mutations == 0 && f.Live.Count == 0 && session.Refusal(Crossing) != null && session.Refusal(Crossing).Contains("bell-as-light-b"),
            "The first correction is saved and the modification stays refused for the other addition: " + first.Error + " / " + session.Refusal(Crossing));
        mapEditor.Undo();
        Assert(session.IncompleteOperation == null && f.Live.Count == 0 && session.EquipmentEntries(Crossing).Single(e => e.Addition.Id == "bell-as-light-a").Addition.Source.Path == "World/Rejected/bell",
            "Undo of the correction restores the refused source without building anything: " + session.IncompleteOperation);
        mapEditor.Redo();
        Assert(session.IncompleteOperation == null && f.Live.Count == 0 && session.EquipmentEntries(Crossing).Single(e => e.Addition.Id == "bell-as-light-a").Addition.Source.Path == SignalPath,
            "Redo saves the correction again: " + session.IncompleteOperation);
        var corrected = session.EquipmentEntries(Crossing).Single(e => e.Addition.Id == "bell-as-light-a");
        Assert(corrected.Addition.Source.Path == SignalPath && corrected.Problem.Contains("bell-as-light-b"), "The corrected addition holds its new source and names the remaining refusal: " + corrected.Problem);
        var second = session.AddEquipment(Crossing, West(6, CrossingPath + "/Signal 2", "bell-as-light-b"));
        Assert(second.Applied && session.Refusal(Crossing) == null && f.Live.Select(a => a.Record.Id).OrderBy(i => i, StringComparer.Ordinal).SequenceEqual(new[] { "bell-as-light-a", "bell-as-light-b", "east", "west" }),
            "After the second correction every addition is built: " + second.Error + " / " + String.Join(",", f.Live.Select(a => a.Record.Id)));
    }
    // A correction is saved without building only while another refused addition keeps the modification refused. When the
    // modification stays refused for another reason, such as its detection, the correction is refused before any change
    // with that reason. A source shared with a valid addition is no conflict: copies of one device may stand side by side.
    static void ACorrectionIsSavedOnlyWhileAnotherAdditionKeepsTheRefusal() {
        string Loc(double d) => "{\"segment\":\"Sgone\",\"end\":\"a\",\"distance\":" + N(d) + "}";
        string Range(double a, double b) => "{\"from\":" + Loc(a) + ",\"to\":" + Loc(b) + "}";
        var detection = "\"north\":{\"approachA\":" + Range(308, 550) + ",\"island\":" + Range(292, 308) + ",\"approachB\":" + Range(292, 50) + "}";
        var f = new Fixture();
        f.World.Device("World/Rejected/bell", d => d.Facts.Bells = 1);
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc("\"equip-crossing\":{" + Target() + ",\"relationships\":{\"crossingCoverage\":{\"additions\":{" + detection + "}},\"crossingEquipment\":{"
            + Addition("bell-as-light-a", "World/Rejected/bell", 5) + "," + Addition("west") + "}},\"dependsOn\":[]}");
        engine.Apply(scenePatchFile, "author/pack");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        f.World.Mutations = 0;
        var preview = session.PreviewEquipment(Crossing, West(5, SignalPath, "bell-as-light-a"), null);
        Assert(preview.Error != null && preview.Error.Contains("Sgone"), "The preview refuses a correction that leaves the modification refused for its detection: " + preview.Error + String.Join("|", preview.Lines));
        var corrected = session.AddEquipment(Crossing, West(5, SignalPath, "bell-as-light-a"));
        Assert(!corrected.Applied && corrected.Failure == FailureKind.Refused && corrected.Error.Contains("Sgone") && f.World.Mutations == 0 && session.ScenePatchFileText.Contains("World/Rejected/bell"),
            "The correction is refused with the detection's reason and changes nothing: " + corrected.Error);
        Assert(mapEditor.UndoGroups.Count == 0, "The refused correction adds no history step");
    }
    // Undo of the final removal withdraws the built additions only when nothing depends on the modification and the
    // withdrawal itself succeeds; otherwise the step is incomplete and names why.
    static void UndoToARefusedStateRespectsDependentsAndReportsItsOwnFailure() {
        var f = new Fixture();
        f.World.Device("World/Rejected/bell", d => d.Facts.Bells = 1);
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("equip-crossing", Addition("bell-as-light-b", "World/Rejected/bell", 6) + "," + Addition("west"))
            + "," + Equip("follower", Addition("solo", x: 30), OtherPath, "[\"equip-crossing\"]"));
        engine.Apply(scenePatchFile, "author/pack");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        Assert(session.RemoveEquipment(Crossing, "bell-as-light-b").Applied && f.Live.Single().Record.Id == "west", "The final removal builds the valid addition");
        mapEditor.Undo();
        Assert(session.IncompleteOperation != null && session.IncompleteOperation.Contains("follower") && f.Live.Single().Record.Id == "west",
            "A modification another one names in dependsOn keeps its effect, and the step names the dependent: " + session.IncompleteOperation);
        var g = new Fixture();
        g.World.Device("World/Rejected/bell", d => d.Facts.Bells = 1);
        var engine2 = new PatchEngine(g.Scene);
        var lone = Doc(Equip("equip-crossing", Addition("bell-as-light-b", "World/Rejected/bell", 6) + "," + Addition("west")));
        engine2.Apply(lone, "author/pack");
        var store2 = new Store { Patch = lone }; var mapEditor2 = new NativeMapEditor();
        var session2 = new PatchEditingSession(engine2, mapEditor2, store2, scenePatchFileKey: "author/pack"); mapEditor2.Session = session2;
        Assert(session2.RemoveEquipment(Crossing, "bell-as-light-b").Applied && g.Live.Single().Record.Id == "west", "The final removal builds the valid addition");
        g.World.StickyDestroy = true;
        mapEditor2.Undo();
        g.World.StickyDestroy = false;
        Assert(session2.IncompleteOperation != null && session2.IncompleteOperation.Contains("did not verify"), "A withdrawal that fails reports its own failure: " + session2.IncompleteOperation);
    }
    // A content refusal reported by an earlier step does not turn a later, unrelated failure of a removal into a removal
    // from the scene patch file: the removal fails and the scene patch file keeps the addition.
    static void AnEarlierRefusalDoesNotShapeALaterFailure() {
        var f = new Fixture();
        f.World.Device("World/Rejected/bell", d => d.Facts.Bells = 1);
        var engine = new PatchEngine(f.Scene);
        var scenePatchFile = Doc(Equip("equip-crossing", Addition("bell-as-light-a", "World/Rejected/bell", 5) + "," + Addition("bell-as-light-b", "World/Rejected/bell", 6) + "," + Addition("west")));
        engine.Apply(scenePatchFile, "author/pack");
        var store = new Store { Patch = scenePatchFile }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        Assert(session.RemoveEquipment(Crossing, "bell-as-light-a").Applied, "The first removal is saved");
        mapEditor.Undo();
        Assert(session.ScenePatchFileText.Contains("bell-as-light-a"), "Undo restores the refused addition, which records its refusal");
        f.Coverage.FailRead = true;
        var removed = session.RemoveEquipment(Crossing, "bell-as-light-a");
        f.Coverage.FailRead = false;
        Assert(!removed.Applied && session.ScenePatchFileText.Contains("bell-as-light-a"), "A removal that fails for another reason fails and keeps the scene patch file: " + removed.Applied + " " + removed.Error);
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
