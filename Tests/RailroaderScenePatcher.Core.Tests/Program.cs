// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
namespace UnityEngine { sealed class SerializeField : Attribute { } }
class Scene : IScene, IInspectionScene, IMapPatchContext {
    // The map patches in the map context, as while the native map editor edits a map patch.
    public string[] MapPatches = new string[0];
    public IEnumerable<string> MapPatchIds => MapPatches;
    public Func<IEnumerable<SceneObservation>> Observations;
    public IEnumerable<SceneObservation> Discover() => Observations == null ? Array.Empty<SceneObservation>() : Observations();
    public IEnumerable<string> MapIds => Maps;
    public string[] Maps = { "map", "layer" };
    public Capability Capability { get; set; } = Capability.Unchecked;
    public Dictionary<string, List<object>> Objects = new Dictionary<string, List<object>>();
    public double Origin = 100;
    public bool FailApply { get; set; }
    public bool FailRestore { get; set; }
    public object FailingTarget { get; set; }
    public int ResolveCalls, MutationCalls;
    public readonly List<object> MutationLog = new List<object>();
    public Action BeforeCapture;
    public IReadOnlyList<object> Resolve(TargetIdentity identity) { ResolveCalls++; return Objects.TryGetValue(identity.Path, out var found) ? found : new List<object>(); }
    public void Preflight(object target) { }
    public bool FailRead { get; set; }
    public int FailNextApplies;
    // The next write throws this exception after it changed the object, as a reflected game call throws.
    public Exception NextApplyFailure;
    public object Capture(object target) { BeforeCapture?.Invoke(); var box = (Box)target; return new object[] { (double[])box.Position.Clone(), (double[])box.Rotation.Clone(), box.Active }; }
    public Action BeforeRead;
    public ObjectState Read(object target) { var hook = BeforeRead; BeforeRead = null; hook?.Invoke(); if (FailRead) throw new Exception("injected read"); var box = (Box)target; return new ObjectState(new Placement(new[] { box.Position[0] + Origin, box.Position[1], box.Position[2] }, (double[])box.Rotation.Clone()), box.Active); }
    // Refuses a write before it changes anything, such as a restoration that cannot start.
    public Func<ObjectState, bool> RefuseState;
    public void Apply(object target, ObjectState state) {
        MutationCalls++; MutationLog.Add(target); var box = (Box)target;
        if (RefuseState?.Invoke(state) == true) throw new Exception("injected refusal before the write");
        if (state.Placement != null) { box.Position = new[] { state.Placement.Position[0] - Origin, state.Placement.Position[1], state.Placement.Position[2] }; box.Rotation = (double[])state.Placement.Rotation.Clone(); }
        if (state.Active != null) box.Active = state.Active.Value;
        if (FailNextApplies > 0) { FailNextApplies--; throw new Exception("injected once"); }
        if (NextApplyFailure != null) { var failure = NextApplyFailure; NextApplyFailure = null; throw failure; }
        if (FailApply || ReferenceEquals(target, FailingTarget)) throw new Exception("injected");
    }
    public bool Restore(object target, object snapshot) { if (FailRestore) return false; var box = (Box)target; var placement = (object[])snapshot; box.Position = (double[])placement[0]; box.Rotation = (double[])placement[1]; box.Active = (bool)placement[2]; return true; }
}
class Box { public double[] Position = new double[3], Rotation = new double[4]; public bool Active = true; public double X { get => Position[0]; set => Position[0] = value; } }
// One peer's world state as the native entry point binds it: an adapter and its engine.
sealed class PeerWorld : IDisposable {
    public IScene Scene; public PatchEngine Engine;
    public void Dispose() { (Scene as IDisposable)?.Dispose(); }
}
sealed class LocalPeer : IDisposable {
    readonly LocalWorldBinding<PeerWorld> binding;
    public int Reentries;
    public ReentryResult LastReentry;
    public LocalPeer(Func<IScene> createScene) {
        binding = new LocalWorldBinding<PeerWorld>(() => { var scene = createScene() ?? throw new InvalidOperationException("Scene capability unavailable"); return new PeerWorld { Scene = scene, Engine = new PatchEngine(scene) }; }, w => w.Dispose(),
            w => { Reentries++; LastReentry = w.Engine.Reenter(); });
    }
    public IScene Scene => binding.State?.Scene;
    public PatchEngine Engine => binding.State?.Engine;
    public string Error => binding.Error;
    public string ReentryError => binding.ReentryError;
    // True when the callback starts a new load attempt; the graph instance is not part of these keys.
    public bool Enter(object world) => binding.Enter(world, null) != WorldEntry.Repeated;
    public WorldEntry Enter(object world, object graph) => binding.Enter(world, graph);
    public PeerWorld Prepare(object world) => binding.Prepare(world);
    public void Unload() { binding.EndAttempt(); }
    // Null when this world has no engine, so nothing resolves or mutates.
    public ScenePatchFileResult Apply(string json, string scenePatchFileKey) => binding.State?.Engine.Apply(json, scenePatchFileKey);
    public void Dispose() { binding.Dispose(); }
}
class PeerScene : Scene, IDisposable {
    public bool Disposed;
    public void Dispose() { Disposed = true; }
}
static class LocalWorldTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":{" + items + "}}";
    static string Item(string id, string path) => "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
    static PeerScene Peer(double origin, out Box board) {
        board = new Box { Position = new[] { 1.0, 1.0, 1.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        var scene = new PeerScene { Origin = origin };
        scene.Objects["board"] = new List<object> { board };
        return scene;
    }
    public static void Run() {
        HostAndClientEachApplyTheSameScenePatchFileToTheirOwnScene();
        EachPeerChecksItsOwnMapContextAndCapability();
        RetainedWorldReentryReconcilesWithoutRepeatingTheLoad();
        ChangedWorldStartsAFreshLoadAndReleasesThePrevious();
        UnavailablePrerequisitesIsolateOnlyThatWorld();
    }
    static void EachPeerChecksItsOwnMapContextAndCapability() {
        var json = Doc(Item("move-board", "board"));
        var hostScene = Peer(0, out var hostBoard); var clientScene = Peer(0, out var clientBoard);
        clientScene.Maps = new[] { "another-map" };
        var host = new LocalPeer(() => hostScene); var client = new LocalPeer(() => clientScene);
        host.Enter("host"); client.Enter("client");
        var clientResult = client.Apply(json, "pack");
        Assert(host.Apply(json, "pack").Outcomes["move-board"].Applied && hostBoard.X == 112, "Host applies in its matching map context");
        Assert(clientResult.Error != null && clientScene.ResolveCalls == 0 && clientBoard.X == 1, "Client map mismatch rejects before its own target resolution without affecting the host");
        var uncheckedScene = Peer(0, out var uncheckedBoard); uncheckedScene.Capability = Capability.Unchecked;
        var incompatibleScene = Peer(0, out var incompatibleBoard); incompatibleScene.Capability = Capability.Incompatible;
        var uncheckedPeer = new LocalPeer(() => uncheckedScene); var incompatiblePeer = new LocalPeer(() => incompatibleScene);
        uncheckedPeer.Enter("a"); incompatiblePeer.Enter("b");
        Assert(uncheckedPeer.Apply(json, "pack").Outcomes["move-board"].Applied && uncheckedBoard.X == 112, "An unchecked build alone does not refuse local application");
        Assert(incompatiblePeer.Apply(json, "pack").Error != null && incompatibleBoard.X == 1 && incompatibleScene.MutationCalls == 0, "Concrete incompatibility refuses only on the affected peer");
        var ambiguousScene = Peer(0, out _); ambiguousScene.Objects["board"].Add(new Box());
        var ambiguousPeer = new LocalPeer(() => ambiguousScene); ambiguousPeer.Enter("c");
        Assert(!ambiguousPeer.Apply(json, "pack").Outcomes["move-board"].Applied && ambiguousScene.MutationCalls == 0, "Each peer requires its own unique target identity before mutation");
    }
    static void RetainedWorldReentryReconcilesWithoutRepeatingTheLoad() {
        var created = 0;
        var scene = Peer(100, out var board); var missing = Doc(Item("move-board", "board") + "," + Item("absent", "later"));
        var peer = new LocalPeer(() => { created++; return scene; });
        peer.Enter("same-map"); var first = peer.Apply(missing, "pack");
        Assert(first.Outcomes["move-board"].Applied && !first.Outcomes["absent"].Applied, "Initial load applies available work and isolates a failure");
        scene.Origin = 140; scene.Objects["later"] = new List<object> { new Box() };
        Assert(!peer.Enter("same-map") && created == 1 && !scene.Disposed, "A repeated world-load callback for the retained world keeps its binding");
        var again = peer.Apply(missing, "pack");
        Assert(again.Outcomes["move-board"].Applied && board.X == -28 && board.Position[1] == 2, "Re-entry reconciles the absolute map placement under the current origin without accumulating an offset");
        Assert(!again.Outcomes["absent"].Applied && again.Outcomes["absent"].Error.Contains("already failed"), "A failure is not retried automatically within the retained load");
        var guarded = Peer(0, out var guardedBoard); guarded.FailApply = true; guarded.FailRestore = true;
        var guardedPeer = new LocalPeer(() => guarded); guardedPeer.Enter("w");
        guardedPeer.Apply(Doc(Item("move-board", "board")), "pack"); guarded.FailApply = false; guarded.FailRestore = false;
        guardedPeer.Enter("w");
        var retry = guardedPeer.Apply(Doc(Item("other", "board")), "pack");
        Assert(!retry.Outcomes["other"].Applied && retry.Outcomes["other"].Error.Contains("Cleanup"), "Unverified cleanup stays guarded through retained-world re-entry");
    }
    static void ChangedWorldStartsAFreshLoadAndReleasesThePrevious() {
        var scenes = new List<PeerScene>(); var boards = new List<Box>();
        var peer = new LocalPeer(() => { var s = Peer(0, out var b); boards.Add(b); if (scenes.Count == 0) s.Objects.Remove("board"); scenes.Add(s); return s; });
        peer.Enter("first-world");
        Assert(!peer.Apply(Doc(Item("move-board", "board")), "pack").Outcomes["move-board"].Applied, "The target is missing in the first world");
        Assert(peer.Enter("second-world") && scenes[0].Disposed && scenes.Count == 2, "A different world releases the previous world's owned state and binds a new scene");
        var result = peer.Apply(Doc(Item("move-board", "board")), "pack");
        Assert(result.Outcomes["move-board"].Applied && boards[1].X == 112 && scenes[1].MutationCalls == 1, "A new world is a new load attempt that applies to its own objects");
        peer.Dispose();
        Assert(scenes[1].Disposed && peer.Scene == null, "Releasing the peer releases its current world binding");
        Assert(peer.Enter("second-world") && scenes.Count == 3 && !scenes[2].Disposed, "After world unload releases the binding, loading the same key again starts a new load attempt");
    }
    static void UnavailablePrerequisitesIsolateOnlyThatWorld() {
        var attempts = 0; var scene = Peer(0, out var board);
        var peer = new LocalPeer(() => { if (attempts++ == 0) throw new MissingMethodException("WorldToGame"); return scene; });
        Assert(peer.Enter("early-world") && peer.Error != null && peer.Error.Contains("WorldToGame"), "Missing prerequisites are reported for this world");
        var refused = peer.Apply(Doc(Item("move-board", "board")), "pack");
        Assert(refused == null && scene.ResolveCalls == 0, "Without the capability nothing resolves or mutates");
        Assert(!peer.Enter("early-world") && attempts == 1, "The same retained world does not repeat capability initialization");
        Assert(peer.Enter("next-world") && peer.Error == null && peer.Apply(Doc(Item("move-board", "board")), "pack").Outcomes["move-board"].Applied && board.X == 112, "A later world rechecks prerequisites and applies");
    }
    static void HostAndClientEachApplyTheSameScenePatchFileToTheirOwnScene() {
        var hostScene = Peer(100, out var hostBoard); var clientScene = Peer(-250, out var clientBoard);
        var host = new LocalPeer(() => hostScene); var client = new LocalPeer(() => clientScene);
        Assert(host.Enter("host-world") && client.Enter("client-world"), "Each peer binds its own initial local world");
        var json = Doc(Item("move-board", "board"));
        var hostResult = host.Apply(json, "pack"); var clientResult = client.Apply(json, "pack");
        Assert(hostResult.Error == null && hostResult.Outcomes["move-board"].Applied, "Host applies the scene patch file locally");
        Assert(clientResult.Error == null && clientResult.Outcomes["move-board"].Applied, "Client applies the same scene patch file locally");
        Assert(ReferenceEquals(hostResult.Outcomes["move-board"].ResolvedTarget, hostBoard) && ReferenceEquals(clientResult.Outcomes["move-board"].ResolvedTarget, clientBoard), "Each peer resolves its own scene object");
        // Each peer's local coordinates differ by its own origin; both express map position 112.
        Assert(hostBoard.X == 12 && clientBoard.X == 362, "Each peer converts the same map placement through its own local origin");
        Assert(hostScene.MutationCalls == 1 && clientScene.MutationCalls == 1, "No peer mutates another peer's scene");
    }
}
class Program {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void AssertWriteProtected(string json) {
        var scenePatchFile = ScenePatchFile.Load(json);
        try { scenePatchFile.Write(); throw new Exception("Protected scene patch file was writable"); }
        catch (InvalidOperationException) { Assert(!scenePatchFile.CanWrite, "Protected scene patch file must expose its read failure"); }
    }
    static void AssertWholeScenePatchFileRejected(string json, string scenario) {
        var scene = new Scene(); var box = new Box { Position = new[] { 7.0, 8.0, 9.0 }, Rotation = new[] { 1.0, 0.0, 0.0, 0.0 } };
        scene.Objects["p"] = new List<object> { box };
        var result = new PatchEngine(scene).Apply(json);
        Assert(!String.IsNullOrEmpty(result.Error) && result.Outcomes.Count == 0, scenario + ": whole scene patch file rejected without individual outcomes");
        Assert(scene.ResolveCalls == 0 && scene.MutationCalls == 0, scenario + ": no target resolution or mutation, including the independent valid entry");
        Assert(box.Position.SequenceEqual(new[] { 7.0, 8.0, 9.0 }) && box.Rotation.SequenceEqual(new[] { 1.0, 0.0, 0.0, 0.0 }), scenario + ": complete original placement retained");
    }
    static string Item(string id, string path, string deps = "[]", string extra = "") => "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":" + deps + extra + "}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":{" + items + "}}";
    static int Main(string[] args) {
        try {
            return RunTests(args);
        } catch (Exception e) {
            var failure = e is System.Reflection.TargetInvocationException invocation ? invocation.InnerException ?? invocation : e;
            var test = args.Length == 2 && args[0] == "--suite" ? args[1] : "Core tests";
            Console.Error.WriteLine("FAIL: " + test + ": " + failure);
            return 1;
        }
    }
    static int RunTests(string[] args) {
        if (args.Length != 0) {
            if (args.Length != 2 || args[0] != "--suite" || !args[1].EndsWith("Tests", StringComparison.Ordinal)) return 2;
            var suite = typeof(Program).Assembly.GetType(args[1], false);
            var run = suite?.GetMethod("Run", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (run == null || run.GetParameters().Length != 0) return 2;
            run.Invoke(null, null);
            Console.WriteLine("PASS: " + args[1]);
            return 0;
        }
        var doc = ScenePatchFile.Load(Doc("")); Assert(doc.CanWrite, "Valid scene patch file must be writable");
        Assert(!ScenePatchFile.Load(Doc("\"\":{}")).CanWrite, "Unidentifiable modifications must protect the scene patch file");
        var scene = new Scene(); var box = new Box(); scene.Objects["p"] = new List<object> { box };
        var engine = new PatchEngine(scene); var result = engine.Apply(Doc(Item("a", "p")));
        Assert(result.Error == null && box.X == 12, "Fixed map placement uses scene origin conversion on an unchecked build");
        Assert(scene.ResolveCalls == 2 && scene.MutationCalls == 1, "A valid complete scene patch file revalidates resolution before mutation");
        Assert(ReferenceEquals(result.Outcomes["a"].ResolvedTarget, box), "Only a uniquely resolved target can support overlap attribution");
        Assert(box.Position[1] == 2 && box.Position[2] == 3 && box.Rotation[0] == 0 && box.Rotation[1] == 0 && box.Rotation[2] == 0 && box.Rotation[3] == 1, "Entire position and rotation are applied");
        scene.Origin = 110; result = engine.Apply(Doc(Item("a", "p"))); Assert(box.X == 2, "Changed origin yields the same map placement"); scene.Origin = 100;
        result = new PatchEngine(scene).Apply(Doc(Item("bad", "missing") + "," + Item("good", "p")));
        Assert(!result.Outcomes["bad"].Applied && result.Outcomes["good"].Applied, "Missing target is isolated from independent target");
        box.X = 7; var resolvesBeforeMismatch = scene.ResolveCalls; var mutationsBeforeMismatch = scene.MutationCalls;
        result = new PatchEngine(scene).Apply(Doc(Item("a", "p"), "other"));
        Assert(result.Error != null && result.Outcomes.Count == 0 && scene.ResolveCalls == resolvesBeforeMismatch && scene.MutationCalls == mutationsBeforeMismatch && box.X == 7, "Map mismatch rejects the whole scene patch file before target resolution or mutation");
        scene.Objects["ambiguous"] = new List<object> { new Box(), new Box() };
        result = new PatchEngine(scene).Apply(Doc(Item("a", "ambiguous")));
        Assert(!result.Outcomes["a"].Applied, "Ambiguous target is rejected");
        Assert(result.Outcomes["a"].ResolvedTarget == null, "Ambiguity cannot attribute overlap to an object");
        result = new PatchEngine(scene).Apply(Doc(Item("dependent", "p", "[\"invalid\"]") + "," + Item("invalid", "missing") + "," + Item("independent", "p")));
        Assert(!result.Outcomes["dependent"].Applied && result.Outcomes["independent"].Applied, "Failure propagates only to dependents");
        result = new PatchEngine(scene).Apply(Doc(Item("dependent", "p", "[\"base\"]") + "," + Item("base", "p")));
        Assert(result.Outcomes["dependent"].Applied && result.Outcomes["base"].Applied, "Dependencies run regardless of scene patch file order");
        engine = new PatchEngine(scene); box.Position = new[] { 4.0, 5.0, 6.0 }; box.Rotation = new[] { 1.0, 0.0, 0.0, 0.0 }; scene.FailApply = true;
        result = engine.Apply(Doc(Item("a", "p"))); Assert(!result.Outcomes["a"].Applied && box.X == 4 && box.Position[1] == 5 && box.Position[2] == 6 && box.Rotation[0] == 1 && box.Rotation[3] == 0, "Failed mutation restores the complete placement");
        scene.FailApply = false; result = engine.Apply(Doc(Item("a", "p"))); Assert(!result.Outcomes["a"].Applied && box.X == 4, "Failure is not retried during the load");
        scene.FailApply = true; scene.FailRestore = true; engine = new PatchEngine(scene);
        result = engine.Apply(Doc(Item("a", "p"))); scene.FailApply = false; scene.FailRestore = false;
        result = engine.Apply(Doc(Item("other", "p"))); Assert(!result.Outcomes["other"].Applied && result.Outcomes["other"].Error.Contains("Cleanup"), "Unverified cleanup guards the target");
        engine = new PatchEngine(scene); result = engine.Apply(Doc(Item("a", "p"))); result = engine.Apply(Doc(Item("a", "p")));
        Assert(result.Outcomes["a"].Applied && box.X == 12, "Repeated application reconciles absolute placement");
        result = new PatchEngine(scene).Apply(Doc(Item("future", "p", "[]", ",\"futureOperation\":true") + "," + Item("independent", "p"), "layer"));
        Assert(!result.Outcomes["future"].Applied && result.Outcomes["independent"].Applied, "Unknown operation is preserved without execution and layer context applies");
        scene.Capability = Capability.Incompatible; box.X = 4; result = new PatchEngine(scene).Apply(Doc(Item("a", "p")));
        Assert(result.Error != null && box.X == 4, "Concrete incompatibility blocks placement");
        scene.Capability = Capability.Unchecked;
        var independent = new Box { X = 41 }; scene.Objects["independent"] = new List<object> { independent };
        box.X = 9; scene.FailingTarget = box;
        result = new PatchEngine(scene).Apply(Doc(Item("keep", "independent") + "," + Item("fail", "p")));
        Assert(result.Outcomes["keep"].Applied && !result.Outcomes["fail"].Applied && independent.X == 12 && box.X == 9, "Restoring one failed mutation preserves prior independent work");
        scene.FailingTarget = null;
        Assert(!ScenePatchFile.Load("{\"formatVersion\":999999999999999999999999999,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}").CanWrite, "Huge version is protected");
        var zeroRotation = Item("zero", "p").Replace("[0,0,0,1]", "[0,0,0,0]");
        Assert(!new PatchEngine(scene).Apply(Doc(zeroRotation)).Outcomes["zero"].Applied, "Zero quaternion is rejected");
        var dates = ScenePatchFile.Load(Doc("").Replace("\"modifications\"", "\"unknown\":\"2020-01-01T00:00:00Z\",\"modifications\""));
        Assert(dates.Write().Contains("2020-01-01T00:00:00Z"), "Unknown date-like strings keep their meaning");
        var original = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"future\":{\"x\":3},\"modifications\":{}}";
        var roundtrip = ScenePatchFile.Load(original); Assert(roundtrip.CanWrite && roundtrip.Write().Contains("future"), "Unknown scene patch file content survives round trip");
        var preciseUnknown = Doc("").Replace("\"modifications\"", "\"futureNumber\":0.123456789012345678901234567890123456789,\"modifications\"");
        Assert(ScenePatchFile.Load(preciseUnknown).Write().Contains("0.123456789012345678901234567890123456789"), "Unknown numeric content retains its exact meaning");
        Assert(!ScenePatchFile.Load("{broken").CanWrite && !ScenePatchFile.Load("{\"formatVersion\":2,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}").CanWrite, "Unreadable and newer scene patch files are protected");
        const string missingMapId = "{\"formatVersion\":1}";
        var malformedCurrentFormat = ScenePatchFile.Load(missingMapId);
        Assert(!malformedCurrentFormat.CanWrite && ScenePatchFile.FormatProblem(missingMapId) == null
            && malformedCurrentFormat.Diagnostics.Single().Path == "sourceMap.mapId",
            "A current-format file with a missing map ID reports the missing value as its problem");
        AssertWriteProtected("{broken");
        AssertWriteProtected(Doc("").Replace("\"formatVersion\":1", "\"formatVersion\":2"));
        AssertWriteProtected(Doc("").Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1"));
        var validIndependent = Item("independent", "p");
        AssertWholeScenePatchFileRejected(Doc(validIndependent + ",\" \":{}"), "Missing modification identity after a valid entry");
        AssertWholeScenePatchFileRejected(Doc(validIndependent + "," + Item("independent", "p")), "Duplicate modification identity after a valid entry");
        AssertWholeScenePatchFileRejected(Doc(validIndependent + ",\"number\":42"), "Non-object modification after a valid entry");
        AssertWholeScenePatchFileRejected(Doc(validIndependent).Replace("\"mapId\":\"map\"", "\"mapId\":null"), "Invalid scene target map with a valid entry");
        AssertWholeScenePatchFileRejected(Doc(validIndependent).Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1"), "Duplicate envelope property with a valid entry");
        AssertWholeScenePatchFileRejected(Doc(validIndependent).Replace("\"formatVersion\":1", "\"formatVersion\":2"), "Unsupported scene patch file version with a valid entry");
        AssertWholeScenePatchFileRejected(Doc(validIndependent).Substring(0, Doc(validIndependent).Length - 1), "Unreadable truncated scene patch file containing a valid entry");
        AssertWholeScenePatchFileRejected(null, "Unreadable null input");
        // The attempt trace of each engine stage is debug output: with debug off nothing is built or written.
        var observedEngine = new PatchEngine(scene);
        var quiet = RecordingLog.Capture(false, () => observedEngine.Apply(Doc(Item("missing-target", "missing") + "," + Item("independent", "p")), "fixture"));
        Assert(quiet.Lines.Count == 0, "Debug off writes no attempt trace");
        observedEngine = new PatchEngine(scene);
        var traced = RecordingLog.Capture(true, () => {
            observedEngine.Apply(Doc(Item("missing-target", "missing") + "," + Item("independent", "p")), "fixture");
            observedEngine.Apply(Doc(Item("missing-target", "p") + "," + Item("independent", "p")), "fixture");
        });
        var traces = traced.At(LogLevel.Debug).Where(l => l.StartsWith(PatchEngine.AttemptTrace, StringComparison.Ordinal))
            .Select(l => Newtonsoft.Json.Linq.JObject.Parse(l.Substring(PatchEngine.AttemptTrace.Length))).ToList();
        Assert(traces.Count(t => (string)t["stage"] == "resolve" && (string)t["id"] == "missing-target") == 1, "Trace distinguishes the one initial failed resolution from a replay");
        Assert(traces.Count(t => (string)t["stage"] == "retry-blocked" && (string)t["id"] == "missing-target") == 1, "Trace demonstrates the same-load failed retry was blocked");
        Assert(!traces.Any(t => (string)t["stage"] == "mutation" && (string)t["id"] == "missing-target"), "Trace does not report mutation for a missing or previously failed identity");
        Assert(traces.Count(t => (string)t["stage"] == "mutation" && (string)t["id"] == "independent") == 2, "Independent valid operation remains observable on both dispatches");
        Assert(traces.All(t => t["engineRuntimeId"] != null), "Each trace names its engine for the attempt summary");
        LogModeTests.Run();
        LogSourceTests.Run();
        NativeButtonSourceTests.Run();
        WorldRibbonGeometryTests.Run();
        WorldStrokeGeometryTests.Run();
        ObjectMarkingGeometryTests.Run();
        ObjectMarkingGeometryTests.RunBoxes();
        MarkingCostTests.Run();
        FittedFrameTests.Run();
        HelperMarkerTests.Run();
        ShapeTests.Run();
        PatchEditingTests.Run();
        ReloadDecisionTests.Run();
        ReloadReturnTests.Run();
        FileVersionTests.Run();
        FileImportTests.Run();
        NativeDirtyTests.Run();
        ReceiptFeedbackTests.Run();
        ReceiptPriorityTests.Run();
        ReloadRequestTests.Run();
        ExitPromptTests.Run();
        RecoveryTests.Run();
        CanonicalSaveTests.Run();
        SnapTests.Run();
        GizmoTests.Run();
        GizmoRefusalTests.Run();
        InspectionTests.Run();
        StarterPlacementTests.Run();
        StarterTrackLocationTests.Run();
        StarterWreckTests.Run();
        GuidedTaskTests.Run();
        ProseTests.Run();
        CommandBandTests.Run();
        CompactRailTests.Run();
        HelpWindowTests.Run();
        EnteredEditValuesTests.Run();
        BridgeTests.Run();
        SceneObjectTests.Run();
        SceneObjectCheckTests.Run();
        SceneObjectCategoryTests.Run();
        ScenePlaceTests.Run();
        SceneClassificationTests.Run();
        FamilyMappingTests.Run();
        WindowWordsTests.Run();
        OutlineTreeTests.Run();
        OutlinerGroupingTests.Run();
        EmptyListReasonTests.Run();
        ChildObjectTests.Run();
        CrossbuckTests.Run();
        SharedPathTargetTests.Run();
        SupportedEditsTests.Run();
        IndustryObjectTests.Run();
        LakeSurfaceTests.Run();
        StationMapIconTests.Run();
        ModObjectTests.Run();
        ModObjectMarkerTests.Run();
        ModObjectCrossingTests.Run();
        ModObjectSourceTests.Run();
        DuplicateTests.Run();
        CoalLoaderTests.Run();
        GroundMaskTests.Run();
        SpawnPointTests.Run();
        SpawnPointCopyTests.Run();
        RailBridgeTests.Run();
        RoadBridgeTests.Run();
        ConstructionSegmentTests.Run();
        DeferredRestoreTests.Run();
        InteractiveBuildingTests.Run();
        PartCullingTests.Run();
        ExistingEquipmentTests.Run();
        CrossingSetupTests.Run();
        CrossingObjectMoveTests.Run();
        MarkerGroupTests.Run();
        SelectionTests.Run();
        ComplexTests.Run();
        BoardTests.Run();
        CompositionTests.Run();
        StructureIdTests.Run();
        BindingTests.Run();
        CrossingPadTests.Run();
        MilepostTests.Run();
        DuplicateMarkerIdTests.Run();
        ReplacementMarkerIdTests.Run();
        DiagnosticTests.Run();
        MultipleModTests.Run();
        TrackFollowingTests.Run();
        GroupActivityTests.Run();
        GameSwitchTests.Run();
        CoverageTests.Run();
        CoverageTests.RunAdditions();
        DetectionCheckTests.Run();
        LocalWorldTests.Run();
        RetainedWorldTests.Run();
        CoverageTests.RunRetained();
        SubscriptionTests.Run();
        EquipmentTests.Run();
        BellTests.Run();
        CrossingJoinTests.Run();
        FailureRecoveryTests.Run();
        FaultInjectionTests.Run();
        Console.WriteLine("PASS: complete scene patch file behavior"); return 0;
    }
}
