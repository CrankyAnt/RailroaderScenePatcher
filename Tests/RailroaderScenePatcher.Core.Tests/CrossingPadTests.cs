// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Controlled crossing pads: Generic-marker graph behavior plus the native curve-mesh builder. Generation reads the
// marker location and the object's map position; a pad generated before the adapter observed it has no known source,
// as a pad the game generated at load. Generation from a missing or unresolvable location leaves no geometry.
class FakePadWorld : FakeMarkerWorld, ICrossingPadWorld, ITargetConfirmation, IPlacementPattern, ITrackMarkerDiagnostics {
    sealed class Mesh { public string Signature, Curves, Token; public TrackLocation Source; public double[] Origin; public bool CullingAligned = true; }
    readonly Dictionary<FakeMarker, Mesh> meshes = new Dictionary<FakeMarker, Mesh>();
    public int Generations;
    // Generations per pad.
    public readonly Dictionary<FakeMarker, int> PadGenerations = new Dictionary<FakeMarker, int>();
    // The shape of each segment's curve, changed by a track edit that may leave the anchor's pose unchanged.
    public readonly Dictionary<string, string> Shapes = new Dictionary<string, string>();
    // The native culling's current visibility of each pad's generated renderer.
    public readonly Dictionary<FakeMarker, bool> Visible = new Dictionary<FakeMarker, bool>();
    // Failure injections.
    public bool FailGenerate, EmptyGenerate, StaleCulling;
    // Scene-wide work, counted: resolutions, pattern checks with their reference scan, and curve builds; and the cheap checks.
    public int Resolutions, PatternChecks, CurveBuilds, CurveTokens, Confirmations;
    public new string PatternProblem(object target) { PatternChecks++; return base.PatternProblem(target); }
    // The placement-only pattern reads the object alone; the native warning reconciliation searches every marker.
    public int PlacementChecks, WarningReconciliations;
    public string PlacementProblem(object target) { PlacementChecks++; return ((FakeMarker)target).Pattern; }
    public void AfterVerifiedState(object target) => WarningReconciliations++;
    public bool StillIdentifies(object target, TargetIdentity identity) {
        Confirmations++;
        return identity.Component == "Track.TrackMarker" && Objects.TryGetValue(identity.Path, out var found) && found.Contains(target);
    }
    // A token that cannot be computed, as when the native adapter cannot read the track around the pad.
    public bool TokenUnavailable;
    public string CurveToken(object target) { CurveTokens++; return TokenUnavailable ? null : Curves((FakeMarker)target); }
    public new IReadOnlyList<object> Resolve(TargetIdentity identity) {
        Resolutions++;
        if (identity.Component != "Track.TrackMarker" || (identity.Structure != null && identity.Structure != CrossingPadScene.Structure)) return Array.Empty<object>();
        return Objects.TryGetValue(identity.Path, out var found) ? found : new List<object>();
    }
    // The vertex signature the native builder produces for this location and pad position.
    string Signature(FakeMarker m) {
        var location = Location(m);
        if (location == null || !SegmentLengths(location.Segment).Any() || location.Distance > SegmentLengths(location.Segment)[0]) return null;
        var shape = Shapes.TryGetValue(location.Segment, out var bent) ? "~" + bent : "";
        return location + "@" + Position(m)[0].ToString(System.Globalization.CultureInfo.InvariantCulture) + shape;
    }
    // The curves the builder reads for the current location; null when it does not resolve.
    public string SourceCurves(object target) { CurveBuilds++; return Curves((FakeMarker)target); }
    string Curves(FakeMarker m) => Signature(m) == null ? null : m.Segment + ":" + (Shapes.TryGetValue(m.Segment, out var bent) ? bent : "original");
    // The geometry the game generated at load, from the serialized location, with no recorded source.
    public void Generated(FakeMarker m) => meshes[m] = new Mesh { Signature = Signature(m) };
    public PadGeometry Geometry(object target) {
        var m = (FakeMarker)target;
        return meshes.TryGetValue(m, out var mesh) ? new PadGeometry(mesh.Signature, mesh.Source, mesh.Origin, mesh.CullingAligned, mesh.Curves, mesh.Token) : new PadGeometry(null, null, null, true);
    }
    public void Generate(object target) {
        Mutations++; Generations++;
        var m = (FakeMarker)target;
        PadGenerations[m] = PadGenerations.TryGetValue(m, out var n) ? n + 1 : 1;
        if (FailGenerate) { FailGenerate = false; throw new InvalidOperationException("generation injected"); }
        var signature = EmptyGenerate ? null : Signature(m);
        EmptyGenerate = false;
        meshes[m] = new Mesh { Signature = signature, Source = signature == null ? null : Location(m), Origin = signature == null ? null : Position(m), CullingAligned = !StaleCulling, Curves = signature == null ? null : Curves(m), Token = signature == null || TokenUnavailable ? null : Curves(m) };
    }
}
// Inspection of the pads with copied values, as the native inventory observes them: marker bindings, and hierarchy
// dependencies through the production description, where generated renderers carry their culling visibility.
class FakePadInspection : IInspectionScene {
    readonly FakePadWorld world;
    // False describes the pad like any other hierarchy, as a control.
    public bool DescribeAsPad = true;
    public FakePadInspection(FakePadWorld world) { this.world = world; }
    public IEnumerable<string> MapIds => world.MapIds;
    public IEnumerable<SceneObservation> Discover() => world.Objects.SelectMany(o => o.Value.Select(target => {
        var m = (FakeMarker)target;
        var parts = new List<RailroaderScenePatcher.HierarchyPart> {
            new RailroaderScenePatcher.HierarchyPart(o.Key + " [Track.TrackMarker]", true, false),
            new RailroaderScenePatcher.HierarchyPart(o.Key + " [Track.TrackCurveMeshBuilder]", true, false) };
        if (world.Geometry(m).Generated)
            parts.Add(new RailroaderScenePatcher.HierarchyPart(o.Key + "/Mesh/Curve Mesh [UnityEngine.MeshRenderer]", !world.Visible.TryGetValue(m, out var visible) || visible, true));
        return new SceneObservation(m, new TargetIdentity("BushnellWhittier", o.Key, "Track.TrackMarker", CrossingPadScene.Structure), "Crossing pad marker", m.Active,
            world.Position(m), world.Rotation(m), new[] { "Marker segment=" + m.Segment + "; end=" + m.End + "; distance=" + m.Distance },
            RailroaderScenePatcher.HierarchyDependencies.Describe(DescribeAsPad, parts), Array.Empty<string>(), "pad binding");
    }));
}
static class CrossingPadTests {
    const string PadPath = "World/Roads Wilmot/Crossing Unprotected/Crossing Pad", RearPath = "World/Roads Wilmot/Crossing Unprotected/Crossing Pad (1)";
    // Baseline pad, as the Boot05 trial recorded it: Sj7c end A at 185.9477; a one-metre rebind moves it along the same segment.
    const string Baseline = "Sj7c|a|185.9477@1020", Rebound = "Sj7c|a|186.9477@1020";
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Target(string path = PadPath) => "\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"crossing-pad-v1\"}";
    static string Binding(string id, string marker, string path = PadPath, string deps = "[]", string extra = "") =>
        "{\"id\":\"" + id + "\"," + Target(path) + ",\"relationships\":{\"trackMarker\":" + marker + "}" + extra + ",\"dependsOn\":" + deps + "}";
    static string At(string segment, string end, double distance) => "{\"segment\":\"" + segment + "\",\"end\":\"" + end + "\",\"distance\":" + distance.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":[" + items + "]}";
    static (FakePadWorld world, FakeMarker pad, FakeMarker rear, CrossingPadScene scene) Setup() {
        var world = new FakePadWorld();
        world.Segments["Sj7c"] = new List<double> { 400 };
        var pad = new FakeMarker { Id = "Mg74", Segment = "Sj7c", End = "a", Distance = 185.9477 };
        var rear = new FakeMarker { Id = "Mrr1", Segment = "Sj7c", End = "a", Distance = 210, Position = new double[] { 45, 1, 2 } };
        world.Objects[PadPath] = new List<object> { pad }; world.Objects[RearPath] = new List<object> { rear };
        foreach (var m in new[] { pad, rear }) { world.Load(m); world.Relative(m); world.Generated(m); }
        world.Mutations = 0;
        return (world, pad, rear, new CrossingPadScene(world, Capability.Unchecked));
    }
    static string Entry(FakeMarkerWorld world, FakeMarker marker) {
        var entries = world.Registrations(marker);
        if (entries.Count != 1) return entries.Count + " entries";
        var e = entries[0];
        return e.Bucket + "|" + e.Location.End + "|" + e.Location.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture) + (e.HoldsMarker ? "" : "|foreign");
    }
    static string Mesh(FakePadWorld world, FakeMarker pad) => world.Geometry(pad).Signature ?? "none";
    public static void Run() {
        RebindRegeneratesGeometry();
        RepetitionAndReplacementConverge();
        UnknownGeneratedSourceIsRegeneratedOnce();
        UnresolvedInputsChangeNothing();
        DetachIsRefusedBeforeMutation();
        PlacementAndActivityKeepTheBinding();
        PadsComposeWithGenericMarkers();
        PreflightRefusalChangesNothing();
        GenerationFailureRestoresCapturedState();
        IncompleteCleanupGuardsRetry();
        EditorRebindUndoRedoAndRemoval();
        SaveAndReloadReproduceThePadOnce();
        CullingTogglesKeepTheInspectedPadFresh();
        FollowingPadsRegenerateWhenTheirCurvesChange();
        InspectChooseAndFollowAPad();
        CurveChangesAndRemovalRegenerateFromTheCurrentTrack();
        PoseUpdatesOfAHeldPadDoNoSceneWideWork();
        Console.WriteLine("PASS: crossing-pad rebinding with regenerated geometry and culling");
    }
    static void RebindRegeneratesGeometry() {
        var (world, pad, rear, scene) = Setup();
        Assert(Mesh(world, pad) == Baseline, "The loaded pad holds its baseline geometry: " + Mesh(world, pad));
        var result = new PatchEngine(scene).Apply(Doc(Binding("rebind-pad", At("Sj7c", "a", 186.9477))));
        Assert(result.Error == null && result.Outcomes["rebind-pad"].Applied, "A pad rebinding applies: " + result.Error + result.Outcomes["rebind-pad"].Error);
        Assert(Entry(world, pad) == "Sj7c|a|186.9477", "The graph registers the pad marker at the authored location: " + Entry(world, pad));
        Assert(Mesh(world, pad) == Rebound, "The pad geometry is regenerated from the authored marker: " + Mesh(world, pad));
        Assert(world.Geometry(pad).CullingAligned, "Generation leaves the culling sphere on the regenerated bounds");
        Assert(pad.Position[0] == 20, "A rebinding does not move the pad object");
        Assert(Mesh(world, rear) == "Sj7c|a|210@1045" && Entry(world, rear) == "Sj7c|a|210", "The rear pad keeps its geometry and registration");
    }
    static void RepetitionAndReplacementConverge() {
        var (world, pad, rear, scene) = Setup();
        var engine = new PatchEngine(scene);
        engine.Apply(Doc(Binding("rebind", At("Sj7c", "a", 186.9477))));
        var generations = world.Generations;
        var again = engine.Apply(Doc(Binding("rebind", At("Sj7c", "a", 186.9477))));
        Assert(again.Outcomes["rebind"].Applied && world.Generations == generations && Mesh(world, pad) == Rebound && Entry(world, pad) == "Sj7c|a|186.9477", "Repeated application keeps one registration and does not regenerate current geometry");
        var replaced = new PatchEngine(scene).Apply(Doc(Binding("rebind", At("Nnew", "b", 5))));
        Assert(replaced.Outcomes["rebind"].Applied && Entry(world, pad) == "Nnew|b|5" && Mesh(world, pad) == "Nnew|b|5@1020", "A replacement on another segment registers and regenerates there: " + Mesh(world, pad));
        Assert(!world.Buckets["Sj7c"].ContainsKey("Mg74") && Entry(world, rear) == "Sj7c|a|210", "The old bucket releases the pad; the rear pad keeps its entry");
        // A broken pad binding without registration or geometry is repaired.
        (world, pad, rear, scene) = Setup();
        foreach (var b in world.Buckets.Values) b.Remove("Mg74");
        pad.Segment = "Smissing"; world.Generated(pad);
        Assert(Mesh(world, pad) == "none", "A pad with a broken binding has no geometry");
        var repaired = new PatchEngine(scene).Apply(Doc(Binding("repair", At("Sj7c", "a", 185.9477))));
        Assert(repaired.Outcomes["repair"].Applied && Entry(world, pad) == "Sj7c|a|185.9477" && Mesh(world, pad) == Baseline, "A broken pad binding is repaired with registration and geometry: " + repaired.Outcomes["repair"].Error);
    }
    // Geometry the game generated at load has no recorded source: the first application regenerates it once from the
    // authored location, and later applications converge without regeneration.
    static void UnknownGeneratedSourceIsRegeneratedOnce() {
        var (world, pad, rear, scene) = Setup();
        var engine = new PatchEngine(scene);
        Assert(engine.Apply(Doc(Binding("same", At("Sj7c", "a", 185.9477)))).Outcomes["same"].Applied && world.Generations == 1 && Mesh(world, pad) == Baseline, "An unchanged binding regenerates geometry of unknown source once, to the same geometry");
        Assert(engine.Apply(Doc(Binding("same", At("Sj7c", "a", 185.9477)))).Outcomes["same"].Applied && world.Generations == 1, "The repeated application leaves verified geometry alone");
        Assert(world.Geometry(rear).Source == null, "The untouched rear pad keeps its native geometry");
    }
    static void UnresolvedInputsChangeNothing() {
        var cases = new[] {
            (At("Smissing", "a", 1), "missing"), (At("Sdup", "a", 1), "ambiguous"), (At("Sj7c", "a", 500), "outside"),
            (At("Sj7c", "c", 1), "end"), (At("Sj7c", "a", -1), "negative"), ("{\"segment\":\"\",\"end\":\"a\",\"distance\":1}", "segment"),
            ("{\"segment\":\"Sj7c\",\"end\":\"a\",\"distance\":1,\"offset\":2}", "Unsupported")
        };
        foreach (var (marker, expected) in cases) {
            var (world, pad, rear, scene) = Setup();
            world.Segments["Sdup"] = new List<double> { 50, 50 };
            var result = new PatchEngine(scene).Apply(Doc(Binding("rebind", marker) + "," + Binding("keep", At("Sj7c", "a", 211), RearPath)));
            var outcome = result.Outcomes["rebind"];
            Assert(!outcome.Applied && outcome.Error.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0, "An unresolved pad input fails the operation: " + expected + " / " + outcome.Error);
            Assert(Entry(world, pad) == "Sj7c|a|185.9477" && Mesh(world, pad) == Baseline && world.Geometry(pad).Source == null, "No substitute, registration change or regeneration: " + expected);
            Assert(result.Outcomes["keep"].Applied && Mesh(world, rear) == "Sj7c|a|211@1045", "An independent pad rebinding still applies: " + expected);
        }
        foreach (var (path, name) in new[] { ("World/Roads Wilmot/Missing Pad", "missing target"), ("Ambiguous", "ambiguous target") }) {
            var (world, pad, rear, scene) = Setup();
            world.Objects["Ambiguous"] = new List<object> { pad, rear };
            var outcome = new PatchEngine(scene).Apply(Doc(Binding("rebind", At("Sj7c", "a", 186), path))).Outcomes["rebind"];
            Assert(!outcome.Applied && outcome.Error.Contains("resolve uniquely") && world.Mutations == 0 && Mesh(world, pad) == Baseline && Mesh(world, rear) == "Sj7c|a|210@1045", name + " changes nothing: " + outcome.Error);
        }
        {
            var (world, pad, rear, scene) = Setup();
            var mismatch = new PatchEngine(scene).Apply(Doc(Binding("rebind", At("Sj7c", "a", 186.9477)), "other"));
            Assert(mismatch.Error != null && mismatch.Outcomes.Count == 0 && world.Mutations == 0 && Mesh(world, pad) == Baseline, "A map mismatch rejects the whole pad document before mutation");
        }
        {
            var (world, pad, rear, scene) = Setup();
            var result = new PatchEngine(scene).Apply(Doc(Binding("dependent", At("Sj7c", "a", 212), RearPath, "[\"base\"]") + "," + Binding("base", At("Smissing", "a", 1))));
            Assert(!result.Outcomes["dependent"].Applied && Mesh(world, rear) == "Sj7c|a|210@1045", "A failed pad rebinding isolates its dependents");
            result = new PatchEngine(scene).Apply(Doc(Binding("dependent", At("Sj7c", "a", 212), RearPath, "[\"base\"]") + "," + Binding("base", At("Sj7c", "a", 186.9477))));
            Assert(result.Outcomes["dependent"].Applied && Mesh(world, rear) == "Sj7c|a|212@1045" && Mesh(world, pad) == Rebound, "Pad dependencies run regardless of document order");
        }
    }
    static void DetachIsRefusedBeforeMutation() {
        var (world, pad, rear, scene) = Setup();
        var outcome = new PatchEngine(scene).Apply(Doc(Binding("detach", "{\"detach\":true}"))).Outcomes["detach"];
        Assert(!outcome.Applied && outcome.Error.Contains("cannot be detached") && world.Mutations == 0, "A pad detach is refused before mutation: " + outcome.Error);
        Assert(Entry(world, pad) == "Sj7c|a|185.9477" && Mesh(world, pad) == Baseline && pad.Active, "The refused detach leaves binding, geometry and activity");
    }
    static string Placed(string id, string extra = "") =>
        "{\"id\":\"" + id + "\"," + Target() + ",\"placement\":{\"mode\":\"fixed\",\"position\":[1025,1,2],\"rotation\":[0,0,0,1]}" + extra + ",\"dependsOn\":[]}";
    static void PlacementAndActivityKeepTheBinding() {
        var (world, pad, rear, scene) = Setup();
        var moved = new PatchEngine(scene).Apply(Doc(Placed("move")));
        Assert(moved.Outcomes["move"].Applied && pad.Position[0] == 25 && Entry(world, pad) == "Sj7c|a|185.9477", "Pad placement keeps the binding: " + moved.Outcomes["move"].Error);
        Assert(Mesh(world, pad) == "Sj7c|a|185.9477@1025", "Geometry is regenerated for the new pad position, so it stays on the track: " + Mesh(world, pad));
        (world, pad, rear, scene) = Setup();
        var disabled = new PatchEngine(scene).Apply(Doc("{\"id\":\"off\"," + Target() + ",\"active\":false,\"dependsOn\":[]}"));
        Assert(disabled.Outcomes["off"].Applied && !pad.Active && world.Registrations(pad).Count == 0 && pad.Segment == "Sj7c", "Disabling the pad removes its registration and keeps the binding");
        var enabled = new PatchEngine(scene).Apply(Doc(Binding("on", At("Sj7c", "a", 186.9477), extra: ",\"active\":true")));
        Assert(enabled.Outcomes["on"].Applied && pad.Active && Entry(world, pad) == "Sj7c|a|186.9477" && Mesh(world, pad) == Rebound, "Enabling with a rebinding registers and regenerates at the authored location");
    }
    // Pads and Generic markers are separate capabilities over the same component; each structure resolves only its own objects.
    static void PadsComposeWithGenericMarkers() {
        var (world, pad, rear, pads) = Setup();
        var markerWorld = new FakeMarkerWorld();
        var sign = new FakeMarker { Id = "Mi4f", Segment = "S9sc", End = "a", Distance = 225.6681 };
        markerWorld.Objects["World/Large Scenery/Ela/Signs/Station Sign (1)"] = new List<object> { sign }; markerWorld.Load(sign); markerWorld.Relative(sign);
        var capabilities = new SceneCapabilities(() => new[] { "map" });
        capabilities.Add(new TrackMarkerScene(markerWorld, Capability.Unchecked)); capabilities.Add(pads);
        var signBinding = "{\"id\":\"sign\",\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"World/Large Scenery/Ela/Signs/Station Sign (1)\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"},\"relationships\":{\"trackMarker\":{\"segment\":\"Nnew\",\"end\":\"a\",\"distance\":2}},\"dependsOn\":[]}";
        var wrongStructure = Binding("as-marker", At("Sj7c", "a", 190), RearPath).Replace("crossing-pad-v1", "track-marker-v1");
        var result = new PatchEngine(capabilities).Apply(Doc(signBinding + "," + Binding("pad", At("Sj7c", "a", 186.9477)) + "," + wrongStructure));
        Assert(result.Outcomes["sign"].Applied && result.Outcomes["pad"].Applied && Mesh(world, pad) == Rebound && sign.Segment == "Nnew", "A pad and a Generic marker apply through their own capabilities");
        Assert(!result.Outcomes["as-marker"].Applied && Mesh(world, rear) == "Sj7c|a|210@1045", "A pad target named with the Generic-marker structure does not resolve to the pad");
        var incompatible = new SceneCapabilities(() => new[] { "map" });
        incompatible.Add(new TrackMarkerScene(markerWorld, Capability.Unchecked)); incompatible.AddUnavailable(CrossingPadScene.Structure, "pad builder missing");
        var isolated = new PatchEngine(incompatible).Apply(Doc(Binding("pad", At("Sj7c", "a", 186)) + "," + signBinding.Replace("\"distance\":2", "\"distance\":3")));
        Assert(!isolated.Outcomes["pad"].Applied && isolated.Outcomes["pad"].Error.Contains("crossing-pad-v1 capability is incompatible") && isolated.Outcomes["sign"].Applied, "An unavailable pad capability fails only pad modifications: " + isolated.Outcomes["pad"].Error);
    }
    static void PreflightRefusalChangesNothing() {
        var (world, pad, rear, scene) = Setup();
        pad.Pattern = "Another component consumes this marker's location changes";
        var outcome = new PatchEngine(scene).Apply(Doc(Binding("rebind", At("Sj7c", "a", 186.9477)))).Outcomes["rebind"];
        Assert(!outcome.Applied && outcome.Error == pad.Pattern && world.Mutations == 0 && Mesh(world, pad) == Baseline && Entry(world, pad) == "Sj7c|a|185.9477", "A pattern refusal leaves marker and geometry unchanged");
        (world, pad, rear, scene) = Setup();
        world.Buckets["Sj7c"]["Mg74"] = ("a", 1, rear);
        outcome = new PatchEngine(scene).Apply(Doc(Binding("rebind", At("Sj7c", "a", 186.9477)))).Outcomes["rebind"];
        Assert(!outcome.Applied && outcome.Error.Contains("registration") && world.Mutations == 0 && Mesh(world, pad) == Baseline, "An incoherent registration refuses before mutation: " + outcome.Error);
        (world, pad, rear, scene) = Setup();
        world.Owners.Add(new FakeOwner { Name = "Consumer", Fields = { new ReferenceField("marker", true, () => pad) } });
        outcome = new PatchEngine(scene).Apply(Doc(Binding("rebind", At("Sj7c", "a", 186.9477)))).Outcomes["rebind"];
        Assert(!outcome.Applied && outcome.Error.Contains("Consumer.marker holds this marker") && world.Mutations == 0 && Mesh(world, pad) == Baseline, "Another holder of the pad marker refuses before mutation: " + outcome.Error);
    }
    static void GenerationFailureRestoresCapturedState() {
        foreach (var (inject, name) in new (Action<FakePadWorld>, string)[] { (w => w.FailGenerate = true, "generation throws"), (w => w.EmptyGenerate = true, "generation yields no geometry"), (w => w.ThrowAfterLocation = true, "location write throws") }) {
            var (world, pad, rear, scene) = Setup();
            var engine = new PatchEngine(scene);
            engine.Apply(Doc(Binding("rear", At("Sj7c", "a", 212), RearPath)));
            inject(world);
            var outcome = engine.Apply(Doc(Binding("rebind", At("Sj7c", "a", 186.9477)))).Outcomes["rebind"];
            Assert(!outcome.Applied && outcome.Error.StartsWith("Mutation failed and was restored", StringComparison.Ordinal), name + ": the failure is restored: " + outcome.Error);
            Assert(Entry(world, pad) == "Sj7c|a|185.9477" && pad.Distance == 185.9477 && Mesh(world, pad) == Baseline && world.Geometry(pad).CullingAligned, name + ": the captured binding, registration, geometry and culling return: " + Mesh(world, pad));
            Assert(Mesh(world, rear) == "Sj7c|a|212@1045", name + ": prior independent pad work survives");
            var mutations = world.Mutations;
            Assert(!engine.Apply(Doc(Binding("rebind", At("Sj7c", "a", 186.9477)))).Outcomes["rebind"].Applied && world.Mutations == mutations, name + ": the failed operation is not retried in the same load");
        }
    }
    static void IncompleteCleanupGuardsRetry() {
        var (world, pad, rear, scene) = Setup();
        // Generation keeps leaving the culling sphere behind, so neither the rebinding nor its restoration verifies.
        world.StaleCulling = true;
        var engine = new PatchEngine(scene);
        var outcome = engine.Apply(Doc(Binding("rebind", At("Sj7c", "a", 186.9477)))).Outcomes["rebind"];
        Assert(!outcome.Applied && outcome.Error.Contains("cleanup is unverified") && outcome.Error.Contains("culling"), "Incomplete pad cleanup is reported: " + outcome.Error);
        world.StaleCulling = false; var mutations = world.Mutations;
        var retry = engine.Apply(Doc(Binding("another", At("Sj7c", "a", 187)))).Outcomes["another"];
        Assert(!retry.Applied && retry.Error.Contains("cleanup") && world.Mutations == mutations, "Another operation on the unverified pad is blocked");
        (world, pad, rear, scene) = Setup();
        world.FailRestoreLocation = true; world.Segments["Sjdl"] = new List<double> { 400 };
        pad.Segment = "Sjdl"; foreach (var b in world.Buckets.Values) b.Remove("Mg74"); world.Load(pad); world.Generated(pad);
        world.FailGenerate = true;
        outcome = new PatchEngine(scene).Apply(Doc(Binding("rebind", At("Sj7c", "a", 186.9477)))).Outcomes["rebind"];
        Assert(!outcome.Applied && outcome.Error.Contains("cleanup is unverified"), "A failed restoration of the binding is reported as unverified: " + outcome.Error);
    }
    static readonly TargetIdentity Pad = new TargetIdentity("BushnellWhittier", PadPath, "Track.TrackMarker", CrossingPadScene.Structure);
    static readonly TargetIdentity Rear = new TargetIdentity("BushnellWhittier", RearPath, "Track.TrackMarker", CrossingPadScene.Structure);
    static (FakePadWorld world, FakeMarker pad, FakeMarker rear, AuthoringSession session, Editor editor, Store store) Session(string patch = null) {
        var (world, pad, rear, scene) = Setup();
        var store = new Store { Patch = patch }; var editor = new Editor();
        var session = new AuthoringSession(new PatchEngine(scene), editor, store, "map"); editor.Session = session;
        return (world, pad, rear, session, editor, store);
    }
    static Newtonsoft.Json.Linq.JObject Only(AuthoringSession session) => (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(session.DocumentText)["modifications"].Single();
    static void EditorRebindUndoRedoAndRemoval() {
        var (world, pad, rear, session, editor, store) = Session();
        var before = session.DocumentText;
        var detach = session.Bind(Pad, MarkerBinding.Detached);
        Assert(!detach.Applied && detach.Error.Contains("cannot be detached") && editor.UndoGroups.Count == 0 && session.DocumentText == before && world.Mutations == 0, "An editor detach of a pad is refused without history: " + detach.Error);
        var set = session.Bind(Pad, MarkerBinding.At(new TrackLocation("Sj7c", "a", 186.9477)));
        Assert(set.Applied && Entry(world, pad) == "Sj7c|a|186.9477" && Mesh(world, pad) == Rebound && pad.Position[0] == 20, "An editor rebinding regenerates the pad geometry: " + set.Error);
        var item = Only(session);
        Assert((string)item["target"]["structure"] == "crossing-pad-v1" && (double)item["relationships"]["trackMarker"]["distance"] == 186.9477 && item["placement"] == null, "The document records the pad binding under its structure: " + item);
        Assert(session.MinimumPatcherVersion == "0.5.0", "A pad modification requires the patcher version that introduced it: " + session.MinimumPatcherVersion);
        Assert(session.Bind(Pad, MarkerBinding.At(new TrackLocation("Nnew", "a", 8))).Applied && Mesh(world, pad) == "Nnew|a|8@1020", "A replacement regenerates again");
        editor.Undo(); Assert(Entry(world, pad) == "Sj7c|a|186.9477" && Mesh(world, pad) == Rebound, "Undo of the replacement restores the prior binding and geometry: " + Mesh(world, pad));
        editor.Undo(); Assert(Entry(world, pad) == "Sj7c|a|185.9477" && Mesh(world, pad) == Baseline && session.DocumentText.Contains("\"modifications\": []"), "Undo of the first rebinding restores the native binding and geometry: " + Mesh(world, pad));
        editor.Redo(); editor.Redo(); Assert(Entry(world, pad) == "Nnew|a|8" && Mesh(world, pad) == "Nnew|a|8@1020", "Redo reapplies both rebindings with their geometry");
        Assert(session.Edit(Pad, new Placement(new[] { 1030.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null).Applied && Mesh(world, pad) == "Nnew|a|8@1030" && Entry(world, pad) == "Nnew|a|8", "A placement edit keeps the binding and regenerates on the track");
        Assert(session.Bind(Pad, null).Applied && Entry(world, pad) == "Sj7c|a|185.9477" && Mesh(world, pad) == "Sj7c|a|185.9477@1030", "Clearing the authored binding returns the effective native binding with its geometry");
        editor.Undo(); Assert(Mesh(world, pad) == "Nnew|a|8@1030", "Undo of the clearing restores the authored geometry");
        Assert(session.Remove(Pad).Applied && Entry(world, pad) == "Sj7c|a|185.9477" && pad.Position[0] == 20 && Mesh(world, pad) == Baseline, "Removal restores the underlying binding, pose and geometry: " + Mesh(world, pad));
        editor.Undo(); Assert(Entry(world, pad) == "Nnew|a|8" && Mesh(world, pad) == "Nnew|a|8@1030", "Undo of the removal restores the authored binding and geometry");
        Assert(Mesh(world, rear) == "Sj7c|a|210@1045" && Entry(world, rear) == "Sj7c|a|210", "No editor operation touched the rear pad");
        // A failed editor rebinding restores the captured geometry and records no history.
        (world, pad, rear, session, editor, store) = Session();
        before = session.DocumentText;
        world.FailGenerate = true;
        var failed = session.Bind(Pad, MarkerBinding.At(new TrackLocation("Sj7c", "a", 186.9477)));
        Assert(!failed.Applied && failed.Error.StartsWith("Edit failed and the prior state was restored", StringComparison.Ordinal) && Mesh(world, pad) == Baseline && Entry(world, pad) == "Sj7c|a|185.9477" && session.DocumentText == before && editor.UndoGroups.Count == 0, "A failed editor rebinding restores binding and geometry: " + failed.Error);
        world.StaleCulling = true;
        failed = session.Bind(Pad, MarkerBinding.At(new TrackLocation("Sj7c", "a", 186.9477)));
        Assert(!failed.Applied && failed.Error.Contains("cleanup is unverified"), "Incomplete editor cleanup is reported: " + failed.Error);
        world.StaleCulling = false;
        Assert(!session.Bind(Pad, MarkerBinding.At(new TrackLocation("Sj7c", "a", 187))).Applied, "The unverified pad refuses another edit");
        world.Generate(pad);
        Assert(session.Recheck(Pad).Applied && session.Bind(Pad, MarkerBinding.At(new TrackLocation("Sj7c", "a", 187))).Applied && Mesh(world, pad) == "Sj7c|a|187@1020", "Recheck after regeneration unblocks the pad");
    }
    static void SaveAndReloadReproduceThePadOnce() {
        var (world, pad, rear, session, editor, store) = Session();
        session.Bind(Pad, MarkerBinding.At(new TrackLocation("Sj7c", "a", 186.9477)));
        session.Bind(Rear, MarkerBinding.At(new TrackLocation("Nnew", "b", 3.5)));
        Assert(editor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0"), "Save raises the dependency to the pad version: " + store.Info);
        var (fresh, freshPad, freshRear, freshScene) = Setup();
        var engine = new PatchEngine(freshScene);
        var reload = engine.Apply(store.Patch);
        Assert(reload.Outcomes.Values.All(o => o.Applied) && Mesh(fresh, freshPad) == Rebound && Mesh(fresh, freshRear) == "Nnew|b|3.5@1045", "A reloaded world reproduces the saved pad bindings and geometry");
        var generations = fresh.Generations;
        engine.Apply(store.Patch);
        Assert(fresh.Generations == generations && Entry(fresh, freshPad) == "Sj7c|a|186.9477" && fresh.Buckets.Values.Sum(b => b.Count) == 2, "A repeated world-load callback leaves one registration per pad and no duplicate generation");
        var (w2, p2, r2, reopened, e2, st2) = Session(store.Patch);
        Assert(reopened.Blocked == null && reopened.Bind(Pad, MarkerBinding.At(new TrackLocation("Sj7c", "a", 185.9477))).Applied && Mesh(w2, p2) == Baseline, "A saved pad document reopens for editing");
    }
    // Culling toggles a generated renderer as the camera moves, and generation replaces it; neither changes the inspected
    // pad, so an edit bound to the inspection still applies. A marker change makes the inspection stale.
    static void CullingTogglesKeepTheInspectedPadFresh() {
        var (world, pad, rear, pads) = Setup();
        var inspection = new FakePadInspection(world);
        var capabilities = new SceneCapabilities(() => world.MapIds, inspection);
        capabilities.Add(pads);
        var editor = new Editor();
        var session = new AuthoringSession(new PatchEngine(capabilities), editor, new Store(), "map"); editor.Session = session;
        var inventory = new SceneInventory(capabilities);
        inventory.Refresh();
        var pinned = inventory.Pin(inventory.Entries.Single(e => ReferenceEquals(e.Handle, pad)));
        world.Visible[pad] = false; world.Visible[rear] = false;
        inventory.CheckFreshness();
        Assert(!inventory.IsStale, "A culling toggle of the generated renderers leaves the inventory fresh: " + inventory.StaleReason);
        world.Generate(rear);
        inventory.CheckFreshness();
        Assert(!inventory.IsStale, "Regenerating a pad at its current location leaves the inventory fresh: " + inventory.StaleReason);
        var edit = session.Bind(Pad, MarkerBinding.At(new TrackLocation("Sj7c", "a", 186.9477)), pinned);
        Assert(edit.Applied && Mesh(world, pad) == Rebound, "An edit bound to the inspection made before the culling toggle applies: " + edit.Error);
        inventory.CheckFreshness();
        Assert(inventory.IsStale, "The marker change makes the inspected pad stale");
        var stale = session.Bind(Pad, MarkerBinding.At(new TrackLocation("Sj7c", "a", 187)), pinned);
        Assert(!stale.Applied && Mesh(world, pad) == Rebound, "An edit bound to the stale inspection is refused: " + stale.Error);
        // Control: described like any other hierarchy, the same culling toggle would make the pad stale.
        inspection.DescribeAsPad = false;
        inventory.Refresh();
        world.Visible[pad] = true;
        inventory.CheckFreshness();
        Assert(inventory.IsStale, "Without the pad description, a culling toggle changes the observation");
    }
    // Native track poses for track-following placement: the anchor's pose stays where a track edit that only reshapes the
    // curve beyond the anchor leaves it.
    sealed class FixedTrackPoses : ITrackPoseScene {
        public Dictionary<string, TrackPose> Poses = new Dictionary<string, TrackPose>();
        public TrackPose TrackPose(TrackAnchor anchor) => Poses.TryGetValue(anchor.ToString(), out var pose) ? pose : throw new InvalidOperationException("Track anchor " + anchor + " does not resolve");
    }
    // A pad that follows a track is regenerated when the curves its builder reads change while its pose does not, as after
    // a longitudinal node move or its native Undo. A pad that is only rebound keeps the native behavior.
    static void FollowingPadsRegenerateWhenTheirCurvesChange() {
        var (world, pad, rear, pads) = Setup();
        var capabilities = new SceneCapabilities(() => world.MapIds);
        capabilities.Add(pads);
        var tracks = new FixedTrackPoses();
        tracks.Poses["Sj7c|a|185.9477"] = new TrackPose(new double[] { 1020, 1, 2 }, new double[] { 0, 0, 0, 1 });
        capabilities.Tracks = tracks;
        var editor = new Editor();
        var session = new AuthoringSession(new PatchEngine(capabilities), editor, new Store(), "map"); editor.Session = session;
        var follow = session.Follow(Pad, TrackAnchor.At(new TrackLocation("Sj7c", "a", 185.9477)));
        Assert(follow.Applied && pad.Position[0] == 20 && Mesh(world, pad) == Baseline, "The pad follows its track at an unchanged pose: " + follow.Error + " / " + Mesh(world, pad));
        Assert(session.Bind(Rear, MarkerBinding.At(new TrackLocation("Sj7c", "a", 211))).Applied && Mesh(world, rear) == "Sj7c|a|211@1045", "The rear pad is only rebound");
        Assert(!session.ReconcileTrackFollowing().Any(), "An unchanged track leaves the following pad alone");
        var padGenerations = world.PadGenerations[pad]; var rearGenerations = world.PadGenerations[rear];
        // A track edit reshapes Sj7c beyond the anchor; the anchor's pose and therefore the pad's pose stay the same. The first
        // check sees the change, the next one confirms it held still and regenerates.
        world.Shapes["Sj7c"] = "longer";
        Assert(!session.ReconcileTrackFollowing().Any() && world.PadGenerations[pad] == padGenerations, "A curve change waits one check to hold still");
        var moved = session.ReconcileTrackFollowing();
        Assert(moved.Count == 1 && moved[0].Value.Applied && world.PadGenerations[pad] == padGenerations + 1 && pad.Position[0] == 20, "A curve-only change regenerates the following pad once: " + String.Join(",", moved.Select(m => m.Key + "=" + m.Value.Error)));
        Assert(Mesh(world, pad) == Baseline + "~longer", "The regenerated mesh follows the changed curve: " + Mesh(world, pad));
        Assert(!session.ReconcileTrackFollowing().Any() && world.PadGenerations[pad] == padGenerations + 1, "The current curve is not regenerated again");
        Assert(world.PadGenerations[rear] == rearGenerations && Mesh(world, rear) == "Sj7c|a|211@1045", "The pad that is only rebound is untouched, as the game leaves it");
        // Native Undo of the track edit restores the curve; the pose still does not change.
        world.Shapes.Remove("Sj7c");
        Assert(!session.ReconcileTrackFollowing().Any(), "The undone curve waits one check to hold still");
        moved = session.ReconcileTrackFollowing();
        Assert(moved.Count == 1 && moved[0].Value.Applied && world.PadGenerations[pad] == padGenerations + 2 && Mesh(world, pad) == Baseline, "Undo of the curve change regenerates the original mesh: " + Mesh(world, pad));
        Assert(!session.ReconcileTrackFollowing().Any(), "The restored curve is then current");
        Assert(world.PadGenerations[rear] == rearGenerations, "The rebound-only pad is still untouched");
        // A native drag changes the curve on every check: the pad waits until the curve holds still and regenerates once.
        padGenerations = world.PadGenerations[pad];
        var lines = 0;
        foreach (var shape in new[] { "drag-1", "drag-2", "drag-3", "drag-3", "drag-3", "drag-3" }) {
            world.Shapes["Sj7c"] = shape;
            lines += session.ReconcileTrackFollowing().Count(r => r.Value.Applied);
        }
        Assert(lines == 1 && world.PadGenerations[pad] == padGenerations + 1 && Mesh(world, pad) == Baseline + "~drag-3", "One drag regenerates the pad once, on its final curve, with one follow line: " + lines);
        // A check of an unchanged pad does no resolution, pattern check, reference scan or curve build.
        int resolutions = world.Resolutions, patterns = world.PatternChecks, builds = world.CurveBuilds, tokens = world.CurveTokens, generations = world.Generations;
        for (var i = 0; i < 8; i++) Assert(!session.ReconcileTrackFollowing().Any(), "An unchanged pad is not followed again");
        Assert(world.Resolutions == resolutions && world.PatternChecks == patterns && world.CurveBuilds == builds && world.Generations == generations,
            "Checks with unchanged curves resolve nothing, scan nothing and build no curve: " + (world.Resolutions - resolutions) + " resolutions, " + (world.PatternChecks - patterns) + " pattern checks, " + (world.CurveBuilds - builds) + " curve builds");
        Assert(world.CurveTokens > tokens, "Each check compares the cheap curve token instead");
        // A held target that the scene no longer confirms takes the full path again.
        world.Objects[PadPath] = new List<object>();
        Assert(!session.ReconcileTrackFollowing().Any() && world.Resolutions > resolutions, "A target the scene no longer confirms is resolved again");
        world.Objects[PadPath] = new List<object> { pad };
    }
    // The editor route: refresh, inspect the pad, choose and confirm its track segment, then follow it through the edit
    // feedback with its inspected observation, while reconciliation holds the target. A pad capability that failed its
    // game checks refuses with that cause, not as a changed inspected context, and changes nothing.
    static void InspectChooseAndFollowAPad() {
        foreach (var available in new[] { true, false }) {
            var (world, pad, rear, pads) = Setup();
            var capabilities = new SceneCapabilities(() => world.MapIds, new FakePadInspection(world));
            if (available) capabilities.Add(pads); else capabilities.AddUnavailable(CrossingPadScene.Structure, "Object reference not set to an instance of an object");
            var tracks = new FixedTrackPoses();
            tracks.Poses["Sj7c|a|185.9477"] = new TrackPose(new double[] { 1020, 1, 2 }, new double[] { 0, 0, 0, 1 });
            capabilities.Tracks = tracks;
            var editor = new Editor();
            var session = new AuthoringSession(new PatchEngine(capabilities), editor, new Store(), "map"); editor.Session = session;
            var inventory = new SceneInventory(capabilities);
            inventory.Refresh();
            var selected = inventory.Entries.Single(e => ReferenceEquals(e.Handle, pad));
            var snapTracks = new FakeSnapTracks();
            var segment = new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, "Sj7c"), "Track segment Sj7c", "N385-N28g");
            snapTracks.Items.Add(new SnapGeometry(segment, new[] { new double[] { 1020, 1, 2 } }));
            var choice = new PendingSelection(snapTracks, SelectionKind.TrackSegment, "Follow track");
            choice.Offer(new[] { segment });
            Assert(choice.Confirm() && choice.Confirmed.Identity.Identifier == "Sj7c", "The track segment is confirmed");
            var feedback = new RailroaderScenePatcher.EditFeedback();
            var anchor = TrackAnchor.At(new TrackLocation(choice.Confirmed.Identity.Identifier, "a", 185.9477));
            var followed = feedback.Run("follow", "Track following", null, inventory, selected, inspection => session.Follow(selected.Identity, anchor, inspection));
            if (available) {
                Assert(followed && feedback.Result.StartsWith("Track following completed", StringComparison.Ordinal) && session.FollowingOf(Pad) != null && Mesh(world, pad) == Baseline, "The inspected pad follows its confirmed segment: " + feedback.Result);
                for (var i = 0; i < 3; i++) Assert(!session.ReconcileTrackFollowing().Any(), "The held pad is current");
                inventory.Refresh();
                var refreshed = inventory.Entries.Single(e => ReferenceEquals(e.Handle, pad));
                Assert(feedback.Run("follow", "Track following", null, inventory, refreshed, inspection => session.Follow(refreshed.Identity, anchor, inspection)),
                    "After a refresh, following again with the held target applies: " + feedback.Result);
            } else {
                Assert(!followed && feedback.Result.Contains("crossing-pad-v1 capability is incompatible") && !feedback.Result.Contains("Inspected target context changed"), "An unavailable pad capability names its cause: " + feedback.Result);
                Assert(world.Mutations == 0 && editor.UndoGroups.Count == 0 && Mesh(world, pad) == Baseline, "The refused follow changes nothing");
            }
        }
    }
    // The game-test sequence of a following pad: a longitudinal node move that changes only the curve, a lateral move that
    // also moves the anchor, Undo of each, then Remove after a track edit that reconciliation has not yet seen. Every
    // regeneration uses the current curves, also when the cheap track token cannot be computed.
    static void CurveChangesAndRemovalRegenerateFromTheCurrentTrack() {
        foreach (var tokenUnavailable in new[] { false, true }) {
            var mode = tokenUnavailable ? " (token unavailable)" : "";
            var (world, pad, rear, pads) = Setup();
            world.TokenUnavailable = tokenUnavailable;
            var capabilities = new SceneCapabilities(() => world.MapIds);
            capabilities.Add(pads);
            var tracks = new FixedTrackPoses();
            var anchor = "Sj7c|a|185.9477";
            tracks.Poses[anchor] = new TrackPose(new double[] { 1020, 1, 2 }, new double[] { 0, 0, 0, 1 });
            capabilities.Tracks = tracks;
            var editor = new Editor();
            var session = new AuthoringSession(new PatchEngine(capabilities), editor, new Store(), "map"); editor.Session = session;
            Assert(session.Follow(Pad, TrackAnchor.At(new TrackLocation("Sj7c", "a", 185.9477))).Applied && Mesh(world, pad) == Baseline, "The pad follows Sj7c" + mode);
            int Settle() { var lines = 0; for (var i = 0; i < 3; i++) lines += session.ReconcileTrackFollowing().Count(r => r.Value.Applied); return lines; }
            // Step 3: a longitudinal move of the far node changes the curve under the pad but not the anchor pose.
            world.Shapes["Sj7c"] = "longitudinal";
            var step3 = Settle();
            Assert(step3 == 1 && Mesh(world, pad) == Baseline + "~longitudinal", "A longitudinal-only curve change regenerates once" + mode + ": " + step3 + " / " + Mesh(world, pad));
            // Step 4: a lateral move also moves the anchor.
            world.Shapes["Sj7c"] = "lateral"; tracks.Poses[anchor] = new TrackPose(new double[] { 1021, 1, 2 }, new double[] { 0, 0, 0, 1 });
            Settle();
            Assert(pad.Position[0] == 21 && Mesh(world, pad) == "Sj7c|a|185.9477@1021~lateral", "The lateral move regenerates on the current curve" + mode + ": " + Mesh(world, pad));
            // Undo #1 returns to the longitudinal state: the mesh equals step 3.
            world.Shapes["Sj7c"] = "longitudinal"; tracks.Poses[anchor] = new TrackPose(new double[] { 1020, 1, 2 }, new double[] { 0, 0, 0, 1 });
            Settle();
            Assert(pad.Position[0] == 20 && Mesh(world, pad) == Baseline + "~longitudinal", "Undo of the lateral move equals the longitudinal state" + mode + ": " + Mesh(world, pad));
            // Undo #2 restores the original curve: the mesh equals the baseline.
            world.Shapes.Remove("Sj7c");
            var undo2 = Settle();
            Assert(undo2 == 1 && Mesh(world, pad) == Baseline, "Undo of the longitudinal move restores the baseline mesh once" + mode + ": " + undo2 + " / " + Mesh(world, pad));
            // Remove after a track edit and its Undo that reconciliation has not yet seen: removal regenerates from the restored track.
            world.Shapes["Sj7c"] = "longitudinal";
            Settle();
            Assert(Mesh(world, pad) == Baseline + "~longitudinal", "The pad follows the edited track again" + mode);
            world.Shapes.Remove("Sj7c");
            var removed = session.Remove(Pad);
            Assert(removed.Applied && session.FollowingOf(Pad) == null && Mesh(world, pad) == Baseline && pad.Position[0] == 20, "Remove regenerates the pad from the restored track" + mode + ": " + removed.Error + " / " + Mesh(world, pad));
            Assert(Mesh(world, rear) == "Sj7c|a|210@1045", "The untouched rear pad keeps its mesh" + mode);
        }
    }
    // During a track drag every follow update moves the pad: such an update resolves nothing, runs no consumer scan and
    // reconciles no native warning, because neither the binding nor the activity changes. Explicit binding edits, Undo and
    // Remove keep the warning reconciliation.
    static void PoseUpdatesOfAHeldPadDoNoSceneWideWork() {
        var (world, pad, rear, pads) = Setup();
        var capabilities = new SceneCapabilities(() => world.MapIds);
        capabilities.Add(pads);
        var tracks = new FixedTrackPoses();
        var anchor = "Sj7c|a|185.9477";
        tracks.Poses[anchor] = new TrackPose(new double[] { 1020, 1, 2 }, new double[] { 0, 0, 0, 1 });
        capabilities.Tracks = tracks;
        var editor = new Editor();
        var session = new AuthoringSession(new PatchEngine(capabilities), editor, new Store(), "map"); editor.Session = session;
        Assert(session.Follow(Pad, TrackAnchor.At(new TrackLocation("Sj7c", "a", 185.9477))).Applied, "The pad follows Sj7c");
        session.ReconcileTrackFollowing();
        int resolutions = world.Resolutions, patterns = world.PatternChecks, warnings = world.WarningReconciliations, placements = world.PlacementChecks;
        var lines = 0;
        for (var step = 1; step <= 6; step++) {
            world.Shapes["Sj7c"] = "drag-" + step;
            tracks.Poses[anchor] = new TrackPose(new double[] { 1020 + step * 0.1, 1, 2 }, new double[] { 0, 0, 0, 1 });
            lines += session.ReconcileTrackFollowing().Count(r => r.Value.Applied);
        }
        Assert(lines == 6 && Math.Abs(pad.Position[0] - 20.6) < 1e-9 && Mesh(world, pad) == "Sj7c|a|185.9477@1020.6~drag-6", "Every pose step of the drag moves and regenerates the pad: " + lines + " / " + Mesh(world, pad));
        Assert(world.Resolutions == resolutions && world.PatternChecks == patterns && world.WarningReconciliations == warnings,
            "Pose-driven follow updates resolve nothing, run no consumer scan and reconcile no warning: " + (world.Resolutions - resolutions) + " resolutions, " + (world.PatternChecks - patterns) + " pattern checks, " + (world.WarningReconciliations - warnings) + " warning reconciliations");
        Assert(world.PlacementChecks >= placements + 6, "Each update checks the pad's own pattern and registration instead");
        // A pad whose own pattern no longer holds takes the full path, which refuses it.
        pad.Pattern = "Missing script on the pad object";
        tracks.Poses[anchor] = new TrackPose(new double[] { 1021, 1, 2 }, new double[] { 0, 0, 0, 1 });
        var refused = session.ReconcileTrackFollowing();
        Assert(refused.Count == 1 && !refused[0].Value.Applied && refused[0].Value.Error.Contains("Missing script") && Math.Abs(pad.Position[0] - 20.6) < 1e-9, "A broken pattern refuses the update without moving the pad: " + String.Join(",", refused.Select(r => r.Value.Error)));
        pad.Pattern = null;
        Assert(session.ReconcileTrackFollowing().Count(r => r.Value.Applied) == 1 && pad.Position[0] == 21, "A restored pattern follows again");
        // Explicit binding edits, Undo and Remove keep the native warning reconciliation.
        warnings = world.WarningReconciliations;
        Assert(session.Bind(Pad, MarkerBinding.At(new TrackLocation("Sj7c", "a", 186))).Applied && world.WarningReconciliations > warnings, "A binding edit reconciles the native warning");
        warnings = world.WarningReconciliations;
        editor.Undo();
        Assert(world.WarningReconciliations > warnings && pad.Distance == 185.9477, "Undo of the binding reconciles the native warning");
        warnings = world.WarningReconciliations;
        Assert(session.Remove(Pad).Applied && world.WarningReconciliations > warnings, "Remove reconciles the native warning");
    }
}
