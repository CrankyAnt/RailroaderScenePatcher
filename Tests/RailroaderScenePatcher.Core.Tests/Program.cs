// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
class Scene : IScene, IInspectionScene {
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
    public Action BeforeCapture;
    public IReadOnlyList<object> Resolve(TargetIdentity identity) { ResolveCalls++; return Objects.TryGetValue(identity.Path, out var found) ? found : new List<object>(); }
    public void Preflight(object target) { }
    public bool FailRead { get; set; }
    public int FailNextApplies;
    public object Capture(object target) { BeforeCapture?.Invoke(); var box = (Box)target; return new object[] { (double[])box.Position.Clone(), (double[])box.Rotation.Clone(), box.Active }; }
    public Action BeforeRead;
    public ObjectState Read(object target) { var hook = BeforeRead; BeforeRead = null; hook?.Invoke(); if (FailRead) throw new Exception("injected read"); var box = (Box)target; return new ObjectState(new Placement(new[] { box.Position[0] + Origin, box.Position[1], box.Position[2] }, (double[])box.Rotation.Clone()), box.Active); }
    public void Apply(object target, ObjectState state) {
        MutationCalls++; var box = (Box)target;
        if (state.Placement != null) { box.Position = new[] { state.Placement.Position[0] - Origin, state.Placement.Position[1], state.Placement.Position[2] }; box.Rotation = (double[])state.Placement.Rotation.Clone(); }
        if (state.Active != null) box.Active = state.Active.Value;
        if (FailNextApplies > 0) { FailNextApplies--; throw new Exception("injected once"); }
        if (FailApply || ReferenceEquals(target, FailingTarget)) throw new Exception("injected");
    }
    public bool Restore(object target, object snapshot) { if (FailRestore) return false; var box = (Box)target; var pose = (object[])snapshot; box.Position = (double[])pose[0]; box.Rotation = (double[])pose[1]; box.Active = (bool)pose[2]; return true; }
}
class Box { public double[] Position = new double[3], Rotation = new double[4]; public bool Active = true; public double X { get => Position[0]; set => Position[0] = value; } }
class Program {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void AssertWriteProtected(string json) {
        var document = PatchDocument.Load(json);
        try { document.Write(); throw new Exception("Protected document was writable"); }
        catch (InvalidOperationException) { Assert(!document.CanWrite, "Protected document must expose its read failure"); }
    }
    static void AssertWholeDocumentRejected(string json, string scenario) {
        var scene = new Scene(); var box = new Box { Position = new[] { 7.0, 8.0, 9.0 }, Rotation = new[] { 1.0, 0.0, 0.0, 0.0 } };
        scene.Objects["p"] = new List<object> { box };
        var result = new PatchEngine(scene).Apply(json);
        Assert(!String.IsNullOrEmpty(result.Error) && result.Outcomes.Count == 0, scenario + ": whole document rejected without individual outcomes");
        Assert(scene.ResolveCalls == 0 && scene.MutationCalls == 0, scenario + ": no target resolution or mutation, including the independent valid entry");
        Assert(box.Position.SequenceEqual(new[] { 7.0, 8.0, 9.0 }) && box.Rotation.SequenceEqual(new[] { 1.0, 0.0, 0.0, 0.0 }), scenario + ": complete original pose retained");
    }
    static string Item(string id, string path, string deps = "[]", string extra = "") => "{\"id\":\"" + id + "\",\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":" + deps + extra + "}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":[" + items + "]}";
    static int Main() {
        var doc = PatchDocument.Load(Doc("")); Assert(doc.CanWrite, "Valid document must be writable");
        Assert(!PatchDocument.Load(Doc("{}")).CanWrite, "Unidentifiable modifications must protect the document");
        var scene = new Scene(); var box = new Box(); scene.Objects["p"] = new List<object> { box };
        var engine = new PatchEngine(scene); var result = engine.Apply(Doc(Item("a", "p")));
        Assert(result.Error == null && box.X == 12, "Fixed map placement uses scene origin conversion on an unchecked build");
        Assert(scene.ResolveCalls == 2 && scene.MutationCalls == 1, "A valid complete document revalidates resolution before mutation");
        Assert(ReferenceEquals(result.Outcomes["a"].ResolvedTarget, box), "Only a uniquely resolved target can support overlap attribution");
        Assert(box.Position[1] == 2 && box.Position[2] == 3 && box.Rotation[0] == 0 && box.Rotation[1] == 0 && box.Rotation[2] == 0 && box.Rotation[3] == 1, "Entire position and rotation are applied");
        scene.Origin = 110; result = engine.Apply(Doc(Item("a", "p"))); Assert(box.X == 2, "Changed origin yields the same map placement"); scene.Origin = 100;
        result = new PatchEngine(scene).Apply(Doc(Item("bad", "missing") + "," + Item("good", "p")));
        Assert(!result.Outcomes["bad"].Applied && result.Outcomes["good"].Applied, "Missing target is isolated from independent target");
        box.X = 7; var resolvesBeforeMismatch = scene.ResolveCalls; var mutationsBeforeMismatch = scene.MutationCalls;
        result = new PatchEngine(scene).Apply(Doc(Item("a", "p"), "other"));
        Assert(result.Error != null && result.Outcomes.Count == 0 && scene.ResolveCalls == resolvesBeforeMismatch && scene.MutationCalls == mutationsBeforeMismatch && box.X == 7, "Map mismatch rejects the whole document before target resolution or mutation");
        scene.Objects["ambiguous"] = new List<object> { new Box(), new Box() };
        result = new PatchEngine(scene).Apply(Doc(Item("a", "ambiguous")));
        Assert(!result.Outcomes["a"].Applied, "Ambiguous target is rejected");
        Assert(result.Outcomes["a"].ResolvedTarget == null, "Ambiguity cannot attribute overlap to an object");
        result = new PatchEngine(scene).Apply(Doc(Item("dependent", "p", "[\"invalid\"]") + "," + Item("invalid", "missing") + "," + Item("independent", "p")));
        Assert(!result.Outcomes["dependent"].Applied && result.Outcomes["independent"].Applied, "Failure propagates only to dependents");
        result = new PatchEngine(scene).Apply(Doc(Item("dependent", "p", "[\"base\"]") + "," + Item("base", "p")));
        Assert(result.Outcomes["dependent"].Applied && result.Outcomes["base"].Applied, "Dependencies run regardless of document order");
        engine = new PatchEngine(scene); box.Position = new[] { 4.0, 5.0, 6.0 }; box.Rotation = new[] { 1.0, 0.0, 0.0, 0.0 }; scene.FailApply = true;
        result = engine.Apply(Doc(Item("a", "p"))); Assert(!result.Outcomes["a"].Applied && box.X == 4 && box.Position[1] == 5 && box.Position[2] == 6 && box.Rotation[0] == 1 && box.Rotation[3] == 0, "Failed mutation restores the complete pose");
        scene.FailApply = false; result = engine.Apply(Doc(Item("a", "p"))); Assert(!result.Outcomes["a"].Applied && box.X == 4, "Failure is not retried during the load");
        scene.FailApply = true; scene.FailRestore = true; engine = new PatchEngine(scene);
        result = engine.Apply(Doc(Item("a", "p"))); scene.FailApply = false; scene.FailRestore = false;
        result = engine.Apply(Doc(Item("other", "p"))); Assert(!result.Outcomes["other"].Applied && result.Outcomes["other"].Error.Contains("cleanup"), "Unverified cleanup guards the target");
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
        Assert(!PatchDocument.Load("{\"formatVersion\":999999999999999999999999999,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[]}").CanWrite, "Huge version is protected");
        var zeroRotation = Item("zero", "p").Replace("[0,0,0,1]", "[0,0,0,0]");
        Assert(!new PatchEngine(scene).Apply(Doc(zeroRotation)).Outcomes["zero"].Applied, "Zero quaternion is rejected");
        var dates = PatchDocument.Load(Doc("").Replace("\"modifications\"", "\"unknown\":\"2020-01-01T00:00:00Z\",\"modifications\""));
        Assert(dates.Write().Contains("2020-01-01T00:00:00Z"), "Unknown date-like strings keep their meaning");
        var original = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"future\":{\"x\":3},\"modifications\":[]}";
        var roundtrip = PatchDocument.Load(original); Assert(roundtrip.CanWrite && roundtrip.Write().Contains("future"), "Unknown document content survives round trip");
        var preciseUnknown = Doc("").Replace("\"modifications\"", "\"futureNumber\":0.123456789012345678901234567890123456789,\"modifications\"");
        Assert(PatchDocument.Load(preciseUnknown).Write().Contains("0.123456789012345678901234567890123456789"), "Unknown numeric content retains its exact meaning");
        Assert(!PatchDocument.Load("{broken").CanWrite && !PatchDocument.Load("{\"formatVersion\":2,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[]}").CanWrite, "Unreadable and newer documents are protected");
        Assert(PatchDocument.Load("{\"formatVersion\":1}").Error.StartsWith("Invalid document"), "Malformed known documents remain distinguishable from unsupported format recovery");
        AssertWriteProtected("{broken");
        AssertWriteProtected(Doc("").Replace("\"formatVersion\":1", "\"formatVersion\":2"));
        AssertWriteProtected(Doc("").Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1"));
        var validIndependent = Item("independent", "p");
        AssertWholeDocumentRejected(Doc(validIndependent + ",{}"), "Missing modification identity after a valid entry");
        AssertWholeDocumentRejected(Doc(validIndependent + "," + Item("independent", "p")), "Duplicate modification identity after a valid entry");
        AssertWholeDocumentRejected(Doc(validIndependent + ",42"), "Non-object modification after a valid entry");
        AssertWholeDocumentRejected(Doc(validIndependent).Replace("\"mapId\":\"map\"", "\"mapId\":null"), "Invalid source map with a valid entry");
        AssertWholeDocumentRejected(Doc(validIndependent).Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1"), "Duplicate envelope property with a valid entry");
        AssertWholeDocumentRejected(Doc(validIndependent).Replace("\"formatVersion\":1", "\"formatVersion\":2"), "Unsupported document version with a valid entry");
        AssertWholeDocumentRejected(Doc(validIndependent).Substring(0, Doc(validIndependent).Length - 1), "Unreadable truncated document containing a valid entry");
        AssertWholeDocumentRejected(null, "Unreadable null input");
#if ISSUE39_OBSERVATION
        var traces = new List<Newtonsoft.Json.Linq.JObject>();
        JourneyObservation.Sink = value => traces.Add(Newtonsoft.Json.Linq.JObject.Parse(value));
        var observedEngine = new PatchEngine(scene);
        observedEngine.Apply(Doc(Item("missing-target", "missing") + "," + Item("independent", "p")), "fixture");
        observedEngine.Apply(Doc(Item("missing-target", "p") + "," + Item("independent", "p")), "fixture");
        Assert(traces.Count(t => (string)t["stage"] == "resolve" && (string)t["id"] == "missing-target") == 1, "Trace distinguishes the one initial failed resolution from a replay");
        Assert(traces.Count(t => (string)t["stage"] == "retry-blocked" && (string)t["id"] == "missing-target") == 1, "Trace demonstrates the same-load failed retry was blocked");
        Assert(!traces.Any(t => (string)t["stage"] == "mutation" && (string)t["id"] == "missing-target"), "Trace does not report mutation for a missing or previously failed identity");
        Assert(traces.Count(t => (string)t["stage"] == "mutation" && (string)t["id"] == "independent") == 2, "Independent valid operation remains observable on both dispatches");
        JourneyObservation.Sink = null;
#else
        Assert(typeof(PatchEngine).Assembly.GetType("RailroaderScenePatcher.Core.JourneyObservation") == null, "The normal product build exposes no journey observation sink");
#endif
        WorldRibbonGeometryTests.Run();
        WorldStrokeGeometryTests.Run();
        AuthoringTests.Run();
        SnapTests.Run();
        GizmoTests.Run();
        InspectionTests.Run();
        BridgeTests.Run();
        SelectionTests.Run();
        BoardTests.Run();
        CompositionTests.Run();
        BindingTests.Run();
        TrackFollowingTests.Run();
        GroupActivityTests.Run();
        Console.WriteLine("PASS: complete document behavior"); return 0;
    }
}
