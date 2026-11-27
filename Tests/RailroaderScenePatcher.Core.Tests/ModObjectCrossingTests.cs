// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// A duplicated crossing: the mod object holds a controller of its own and its own pads, and is independent of the
// crossing it was made from. Its detection starts empty, so no train triggers it and its controller is a problem until
// detection is added on its Connections tab. Each of its pads holds a marker ID of its own and no track binding, a
// problem until Rebind sets it. An edit of either crossing leaves the other as it is. A single crossing pad is
// duplicated the same way. A crossing without signals is duplicated from its crossing object, as one mod object that
// holds its crossbucks and pads; where crossings share a path, the pad marker ID names the one. The sources are the
// Grade Crossing - Bryson Everett of the base map fixture, with its three pads, the Crossing Unprotected (1) at Alarka
// Jct and the three crossings below Old River Rd.
static class ModObjectCrossingTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Mod object crossings: " + message); }
    const string Scene = "BushnellWhittier", CrossingPath = "World/Large Scenery/Bryson/Grade Crossing - Bryson Everett";
    const string PadComponent = "Track.TrackMarker";
    const string AlarkaPath = "World/Large Scenery/Alarka Jct/Crossing Unprotected (1)", OldRiverPath = "World/Roads Bryson/Old River Rd/Crossing Unprotected";
    static readonly string[] PadNames = { "Crossing Pad", "Crossing Pad 2", "Crossing Pad 3" };
    // The ids the test generator draws: ten times the same letter, A first, then B, and so on.
    static string Draw(int n) => new string((char)('A' + n), ReplacementMarkerIds.GeneratedLength);
    static Func<int, int> Draws() { var draw = 0; return n => (draw++ / ReplacementMarkerIds.GeneratedLength) % n; }
    static readonly double[] Upright = { 0, 0, 0, 1 }, AtNewSiding = { 5200, 14, 5900 };
    static readonly BaseMapFixture Map = new BaseMapFixture();
    static void InFixture(string path, string family) =>
        Assert(Map.Objects.Count(o => o.Listed && o.Path == path && o.Family == family) == 1, "The base map fixture lists " + path + " once as a " + family);

    // The crossing world and the pad world tell their mod objects apart, as the native worlds do by the objects built.
    sealed class Crossings : FakeCrossingWorld, IModObjectCrossings {
        public readonly HashSet<object> Made = new HashSet<object>();
        public bool IsModObject(object controller) => Made.Contains(controller);
    }
    sealed class Pads : FakePadWorld, IModObjectMarkers {
        public readonly HashSet<object> Made = new HashSet<object>();
        public bool IsModObject(object marker) => Made.Contains(marker);
    }
    // A crossing without signals: the crossing object with the names of its crossbucks. Its pads stand in the pad world.
    sealed class FakeCrossingObject { public double[] Position = { 1620, 9, 1211 }, Rotation = { 0, 0, 0, 1 }; public List<string> Crossbucks = new List<string>(); }
    sealed class CrossingObjects : ICrossingObjectWorld {
        readonly World world;
        public CrossingObjects(World world) { this.world = world; }
        public readonly Dictionary<string, List<object>> Objects = new Dictionary<string, List<object>>();
        public IEnumerable<string> MapIds => world.Crossings.MapIds;
        public IReadOnlyList<object> Find(string scene, string path) => Objects.TryGetValue(path, out var found) ? found : new List<object>();
        public IReadOnlyList<string> PadMarkerIds(object crossing) => world.PadsOf.TryGetValue(crossing, out var pads) ? pads.Select(p => p.Value.Id).ToArray() : new string[0];
        public double[] Position(object crossing) => (double[])((FakeCrossingObject)crossing).Position.Clone();
        public double[] Rotation(object crossing) => (double[])((FakeCrossingObject)crossing).Rotation.Clone();
        public bool Active(object crossing) => true;
    }
    // Builds mod objects as the native world does. A crossing's copy holds a controller without circuits, so no span
    // of the source is evaluated or registered twice, and a copy of each pad of the source under the copy's own path,
    // with the marker ID given for it and no track location. A pad's copy holds its marker ID and no track location.
    sealed class Builder : IModObjectScene, IModObjectPartScene {
        readonly World world;
        public readonly List<object> BuiltFrom = new List<object>();
        public readonly List<string> PartIds = new List<string>();
        public Builder(World world) { this.world = world; }
        public IReadOnlyList<object> PartMarkers(object source) =>
            !(source is FakeMarker) && world.PadsOf.TryGetValue(source, out var pads) ? pads.Select(p => (object)p.Value).ToArray() : Array.Empty<object>();
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) => Build(identity, source, placement, markerId, null);
        FakeMarker Pad(string path, string id, double[] position) {
            var pad = new FakeMarker { Id = id, Segment = null, Position = new[] { position[0] - world.Pads.Origin, position[1], position[2] } };
            world.Pads.Objects[path] = new List<object> { pad };
            world.Pads.Made.Add(pad);
            world.Pads.Load(pad); world.Pads.Relative(pad);
            return pad;
        }
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId, IReadOnlyList<string> partMarkerIds) {
            BuiltFrom.Add(source);
            if (source is FakeMarker) {
                if (world.Pads.Objects.TryGetValue(identity.Path, out var there) && there.Count != 0) throw new InvalidOperationException(ModObjects.Taken);
                return Pad(identity.Path, markerId, placement.Position);
            }
            object from = source, made;
            if (source is FakeCrossingObject unprotected) {
                // The copy holds the crossbucks and pads of this crossing object alone, never those of a crossing beside it.
                if (world.Unprotected.Objects.TryGetValue(identity.Path, out var held) && held.Count != 0) throw new InvalidOperationException(ModObjects.Taken);
                made = new FakeCrossingObject { Position = (double[])placement.Position.Clone(), Rotation = (double[])placement.Rotation.Clone(), Crossbucks = unprotected.Crossbucks.ToList() };
                world.Unprotected.Objects[identity.Path] = new List<object> { made };
            } else {
                if (world.Crossings.Objects.TryGetValue(identity.Path, out var standing) && standing.Count != 0) throw new InvalidOperationException(ModObjects.Taken);
                made = new FakeCrossing { Position = (double[])placement.Position.Clone(), Rotation = (double[])placement.Rotation.Clone() };
                world.Crossings.Objects[identity.Path] = new List<object> { made };
                world.Crossings.Made.Add(made);
            }
            var pads = new List<KeyValuePair<string, FakeMarker>>();
            var sources = world.PadsOf[from];
            for (var i = 0; i < sources.Count; i++) {
                PartIds.Add(partMarkerIds[i]);
                pads.Add(new KeyValuePair<string, FakeMarker>(sources[i].Key, Pad(identity.Path + "/" + sources[i].Key, partMarkerIds[i], placement.Position)));
            }
            world.PadsOf[made] = pads;
            return made;
        }
        public void Remove(object modObject) {
            var pads = !(modObject is FakeMarker) && world.PadsOf.TryGetValue(modObject, out var held) ? held.Select(p => p.Value).ToList() : new List<FakeMarker>();
            if (modObject is FakeMarker pad) pads.Add(pad);
            foreach (var gone in pads) {
                world.Pads.SetActive(gone, false);
                foreach (var pair in world.Pads.Objects.Where(p => p.Value.Contains(gone)).ToArray()) world.Pads.Objects.Remove(pair.Key);
            }
            foreach (var pair in world.Crossings.Objects.Where(p => p.Value.Contains(modObject)).ToArray()) world.Crossings.Objects.Remove(pair.Key);
            foreach (var pair in world.Unprotected.Objects.Where(p => p.Value.Contains(modObject)).ToArray()) world.Unprotected.Objects.Remove(pair.Key);
        }
    }
    // One loaded world: the crossing with two native circuits, on segments Sold and Sother, and its three pads on Sjdl.
    sealed class World {
        public readonly Crossings Crossings = new Crossings();
        public readonly Pads Pads = new Pads();
        public readonly FakeCrossing Source = new FakeCrossing { Position = new double[] { 4387, 12, 5379 } };
        public readonly Dictionary<object, List<KeyValuePair<string, FakeMarker>>> PadsOf = new Dictionary<object, List<KeyValuePair<string, FakeMarker>>>();
        public readonly CrossingObjects Unprotected;
        public readonly FakeCrossingObject Alarka = new FakeCrossingObject { Crossbucks = { "Sign-Crossing0 (1)", "Sign-Crossing0 (2)" } };
        public readonly FakeCrossingObject[] OldRiver = Enumerable.Range(0, 3).Select(i => new FakeCrossingObject { Position = new double[] { 5350 + 700 * i, 10, 5747 }, Crossbucks = { "Sign-Crossing0 (1)", "Sign-Crossing0 (2)" } }).ToArray();
        // Gives a crossing object its one pad, bound and registered as the map has it.
        void Hold(string path, FakeCrossingObject crossing, string padId, double distance) {
            if (!Unprotected.Objects.TryGetValue(path, out var at)) Unprotected.Objects[path] = at = new List<object>();
            at.Add(crossing);
            var pad = new FakeMarker { Id = padId, Segment = "Nnew", End = "a", Distance = distance };
            if (!Pads.Objects.TryGetValue(path + "/Crossing Pad", out var pads)) Pads.Objects[path + "/Crossing Pad"] = pads = new List<object>();
            pads.Add(pad);
            Pads.Load(pad); Pads.Relative(pad); Pads.Generated(pad);
            PadsOf[crossing] = new List<KeyValuePair<string, FakeMarker>> { new KeyValuePair<string, FakeMarker>("Crossing Pad", pad) };
        }
        public readonly Builder Builder;
        // The lights, gates and bells added to a crossing, so equipment edits reach the duplicate as they reach a map crossing.
        public readonly FakeEquipmentWorld Equipment = new FakeEquipmentWorld();
        public readonly PatchEngine Engine;
        public readonly FakeSpan[] NativeSpans;
        public World() {
            NativeSpans = new[] { Crossings.Span("T1E", "Sold", 308, 550), Crossings.Span("T1I", "Sold", 292, 308), Crossings.Span("T1W", "Sold", 292, 50),
                Crossings.Span("T2E", "Sother", 208, 390), Crossings.Span("T2I", "Sother", 192, 208), Crossings.Span("T2W", "Sother", 192, 10) };
            Source.Circuits.Add(new FakeCircuit { Spans = NativeSpans.Take(3).ToArray() });
            Source.Circuits.Add(new FakeCircuit { Spans = NativeSpans.Skip(3).ToArray() });
            Crossings.Objects[CrossingPath] = new List<object> { Source };
            var pads = new List<KeyValuePair<string, FakeMarker>>();
            for (var i = 0; i < PadNames.Length; i++) {
                var pad = new FakeMarker { Id = "Mpad" + (i + 1), Segment = "Sjdl", End = "a", Distance = 10 + i };
                Pads.Objects[CrossingPath + "/" + PadNames[i]] = new List<object> { pad };
                Pads.Load(pad); Pads.Relative(pad); Pads.Generated(pad);
                pads.Add(new KeyValuePair<string, FakeMarker>(PadNames[i], pad));
            }
            PadsOf[Source] = pads;
            Unprotected = new CrossingObjects(this);
            Hold(AlarkaPath, Alarka, "Malk", 5);
            for (var i = 0; i < OldRiver.Length; i++) Hold(OldRiverPath, OldRiver[i], "Mold" + (i + 1), 20 + 10 * i);
            var scene = new SceneCapabilities(() => Crossings.MapIds) { ModObjectWorld = Builder = new Builder(this) };
            scene.Add(new CrossingObjectScene(Unprotected, Capability.Unchecked));
            scene.Add(new CrossingCoverageScene(Crossings, Capability.Unchecked, Equipment));
            scene.Add(new CrossingPadScene(Pads, Capability.Unchecked));
            Engine = new PatchEngine(scene);
        }
        public FakeCrossing Copy(TargetIdentity made) => Crossings.Objects.TryGetValue(made.Path, out var found) && found.Count == 1 ? (FakeCrossing)found[0] : null;
        public FakeMarker Pad(string path) => Pads.Objects.TryGetValue(path, out var found) && found.Count == 1 ? (FakeMarker)found[0] : null;
        public FakeMarker SourcePad(int i) => PadsOf[Source][i].Value;
        public FakeCrossingObject Group(TargetIdentity made) => Unprotected.Objects.TryGetValue(made.Path, out var found) && found.Count == 1 ? (FakeCrossingObject)found[0] : null;
        // The registration a pad holds under its own id, as bucket|end|distance, or "none".
        public string Own(FakeMarker m) {
            var own = Pads.Registrations(m).Where(r => r.HoldsMarker).Select(r => r.Bucket + "|" + r.Location.End + "|" + r.Location.Distance).ToArray();
            return own.Length == 0 ? "none" : String.Join(",", own);
        }
        public PatchEditingSession Session(out Store store, out NativeMapEditor editor) {
            store = new Store(); editor = new NativeMapEditor();
            var session = new PatchEditingSession(Engine, editor, store, LoadedMod.PatcherId, "author/pack"); editor.Session = session;
            return session;
        }
        // Whether a train on the segment at the distance triggers the crossing.
        public bool Triggers(FakeCrossing crossing, string segment, double distance) => Crossings.Detects(crossing, segment, distance);
    }
    static TargetIdentity CrossingId(string path) => new TargetIdentity(Scene, path, CrossingCoverageScene.ComponentType, CrossingCoverageScene.Structure);
    static TargetIdentity PadId(string path) => new TargetIdentity(Scene, path, PadComponent, CrossingPadScene.Structure);
    static JObject Item(PatchEditingSession session, TargetIdentity made) =>
        ((JObject)JObject.Parse(session.ScenePatchFileText)["modifications"]).Properties().Select(p => (JObject)p.Value).Single(v => (string)v["target"]["path"] == made.Path);
    static string KeyOf(PatchEditingSession session, TargetIdentity made) =>
        ((JObject)JObject.Parse(session.ScenePatchFileText)["modifications"]).Properties().Single(p => (string)p.Value["target"]["path"] == made.Path).Name;
    static SpanRange Range(string segment, double from, double to) => new SpanRange(new TrackLocation(segment, "a", from), new TrackLocation(segment, "a", to));
    static CircuitCoverage NewTrack(string segment) => CircuitCoverage.Added(Range(segment, 308, 550), Range(segment, 292, 308), Range(segment, 292, 50));
    static Outcome Place(World world, PatchEditingSession session) => session.PlaceModObject(CrossingId(CrossingPath), new Placement(AtNewSiding, Upright), null, null, Draws());

    internal static void Run() {
        InFixture(CrossingPath, SceneFamilies.CrossingController);
        foreach (var pad in PadNames) InFixture(CrossingPath + "/" + pad, SceneFamilies.CrossingPad);
        ADuplicatedCrossingStartsWithItsOwnEmptyDetection();
        DetectionOfTheDuplicateTriggersOnlyTheDuplicate();
        EditsOfTheDuplicatePreviewAsTheyApply();
        AnEditOfTheOriginalLeavesTheDuplicate();
        EachPadOfTheDuplicateStartsUnboundWithItsOwnMarkerId();
        ACrossingModObjectNeedsAMarkerIdForEachPad();
        ACrossingModObjectKeepsItsPlace();
        ASinglePadIsDuplicatedUnbound();
        ACrossingWithoutSignalsIsDuplicatedAsOneGroup();
        CrossingsAtOnePathAreToldApartByPadMarkerId();
        TheSuppliedDefinitionOfACopiedCrossingMatchesItsCopy();
        TheCrossingNodeNamesItsCrossingObject();
        ADuplicatedCrossingNodeIsNamedByItsPlace();
        DuplicateIsOfferedForACrossingAndAPad();
        ACopyHoldsAGameStateIdOfItsOwn();
        Console.WriteLine("PASS: mod object crossings");
    }

    // The placing click builds a crossing of its own at the placement: its controller evaluates no circuit and no span
    // is registered for it, so a train on the original's track triggers the original alone. The copy's coverage is
    // empty and its controller is a problem; the original has none. Remove modification takes the copy away.
    static void ADuplicatedCrossingStartsWithItsOwnEmptyDetection() {
        var world = new World();
        var session = world.Session(out _, out var editor);
        var placed = Place(world, session);
        Assert(placed.Applied, "The crossing is duplicated: " + placed.Error);
        var copy = world.Copy(placed.Target);
        Assert(copy != null && !ReferenceEquals(copy, world.Source) && placed.Target.Structure == CrossingCoverageScene.Structure && ModObjects.IsPath(placed.Target.Path), "The duplicate is a crossing of its own: " + placed.Target.Path);
        Assert(copy.Position.SequenceEqual(AtNewSiding), "It stands where it was placed");
        Assert(copy.Circuits.Count == 0 && session.Coverage(placed.Target).Circuits.Count == 0, "Its detection starts empty");
        Assert(world.Source.Circuits.Count == 2 && world.Source.Circuits.SelectMany(c => c.Spans).SequenceEqual(world.NativeSpans) && world.NativeSpans.All(s => world.Crossings.Registered(s)) && world.Crossings.Graph.Count == 6,
            "The original keeps its circuits and its registered spans, and no span is registered for the duplicate");
        Assert(world.Triggers(world.Source, "Sold", 300) && !world.Triggers(copy, "Sold", 300) && !world.Triggers(copy, "Sother", 200), "A train on the original's track triggers the original alone");
        Assert(CircuitSpanProblems.Undetected(session.Coverage(placed.Target)) == CircuitSpanProblems.NoDetection && CircuitSpanProblems.IsDetectionProblem(CircuitSpanProblems.NoDetection),
            "The duplicate's controller is a detection problem");
        Assert(CircuitSpanProblems.Undetected(session.Coverage(CrossingId(CrossingPath))) == null, "The original is none");
        var item = Item(session, placed.Target);
        Assert((string)item["source"]["path"] == CrossingPath && item["placement"] != null && item["relationships"] == null, "The scene patch file holds its source and placement, and no detection: " + item.ToString(Newtonsoft.Json.Formatting.None));
        editor.Undo();
        Assert(world.Copy(placed.Target) == null && PadNames.All(n => world.Pad(placed.Target.Path + "/" + n) == null) && world.Source.Circuits.Count == 2, "Undo takes the duplicate and its pads away");
        editor.Redo();
        var removed = session.Remove(placed.Target, null);
        Assert(removed.Applied && world.Copy(placed.Target) == null && world.Triggers(world.Source, "Sold", 300), "Remove modification removes the duplicate and leaves the original: " + removed.Error);
    }

    // Detection added to the duplicate on a new track triggers the duplicate alone, and the duplicate is no problem any
    // more; the original keeps its own circuits. A fresh load builds the same. Deleting the detection returns the
    // duplicate to its empty detection.
    static void DetectionOfTheDuplicateTriggersOnlyTheDuplicate() {
        var world = new World();
        var session = world.Session(out var store, out _);
        var placed = Place(world, session);
        var added = session.AddDetection(placed.Target, "siding", NewTrack("Snew"));
        Assert(added.Applied, "Detection is added to the duplicate: " + added.Error);
        var copy = world.Copy(placed.Target);
        Assert(world.Triggers(copy, "Snew", 300) && !world.Triggers(world.Source, "Snew", 300), "A train on the duplicate's detection triggers the duplicate alone");
        Assert(world.Triggers(world.Source, "Sold", 300) && !world.Triggers(copy, "Sold", 300), "A train on the original's detection triggers the original alone");
        Assert(CircuitSpanProblems.Undetected(session.Coverage(placed.Target)) == null, "With detection the duplicate is no problem");
        var original = session.Coverage(CrossingId(CrossingPath));
        Assert(original.Circuits.Count == 2 && original.Circuits.All(c => c.Value.Kind == CircuitKind.Native) && world.Source.Circuits.SelectMany(c => c.Spans).SequenceEqual(world.NativeSpans),
            "The edit of the duplicate leaves the original's circuits");
        session.BeginSave(); session.CompleteNativeSave();
        var next = new World();
        var result = next.Engine.Apply(store.Patch, "author/pack");
        var loaded = next.Copy(placed.Target);
        Assert(result.Error == null && result.Outcomes.Values.All(o => o.Applied) && loaded != null, "A fresh load builds the duplicate: " + (result.Error ?? String.Join("; ", result.Outcomes.Values.Select(o => o.Error))));
        Assert(next.Triggers(loaded, "Snew", 300) && !next.Triggers(next.Source, "Snew", 300) && next.Triggers(next.Source, "Sold", 300) && !next.Triggers(loaded, "Sold", 300), "After a fresh load each crossing answers its own detection only");
        var deleted = session.DeleteDetection(placed.Target, "siding");
        Assert(deleted.Applied && world.Copy(placed.Target) == copy && copy.Circuits.Count == 0 && CircuitSpanProblems.Undetected(session.Coverage(placed.Target)) == CircuitSpanProblems.NoDetection,
            "Without its detection the duplicate stays, as a problem again: " + deleted.Error);
        Assert(world.Crossings.Graph.Count == 6, "The spans of the deleted detection are gone");
    }

    // #38/#160: the previews of the duplicate's edits parse its modification under the scene patch file's key with the
    // modification's own key, so a detached copy of the value keeps the mark that matches. Adding detection (the
    // guide's Add detection steps), changing that detection, a coverage edit and a bell edit each preview without the
    // target shape refusal and change nothing, and the added detection and bell then apply.
    static void EditsOfTheDuplicatePreviewAsTheyApply() {
        var world = new World();
        var session = world.Session(out _, out _);
        var placed = Place(world, session);
        Assert(placed.Applied, "The crossing is duplicated: " + placed.Error);
        var copy = world.Copy(placed.Target);
        var addition = session.PreviewAddition(placed.Target, "siding", NewTrack("Snew"));
        Assert(addition.Error == null && copy.Circuits.Count == 0, "Adding detection to the duplicate previews and changes nothing: " + addition.Error);
        Assert(session.AddDetection(placed.Target, "siding", NewTrack("Snew")).Applied, "Detection is added to the duplicate");
        var changed = session.PreviewAddition(placed.Target, "siding", CircuitCoverage.Added(Range("Snew", 310, 560), Range("Snew", 290, 310), Range("Snew", 290, 40)));
        Assert(changed.Error == null && world.Triggers(copy, "Snew", 300) && !world.Triggers(copy, "Snew", 555), "A change of the duplicate's detection previews and changes nothing: " + changed.Error);
        var coverage = session.PreviewCoverage(placed.Target, "T1I", CircuitCoverage.Detached);
        Assert(coverage.Error == null || !coverage.Error.StartsWith("Invalid mod object", StringComparison.Ordinal), "A coverage edit of the duplicate previews without the target shape refusal: " + coverage.Error);
        world.Equipment.Bell(FakeEquipmentWorld.CrossingBell);
        var bell = new EquipmentAddition("bell", EquipmentAddition.BellPattern, EquipmentSource.ForDefinition(FakeEquipmentWorld.CrossingBell), new Placement(new[] { 10.0, 0, 7 }, Upright));
        var preview = session.PreviewEquipment(placed.Target, bell, null);
        Assert(preview.Error == null && world.Equipment.All.Count == 0, "A bell edit of the duplicate previews and changes nothing: " + preview.Error);
        var added = session.AddEquipment(placed.Target, bell);
        Assert(added.Applied && world.Equipment.All.Count(a => !a.Destroyed) == 1, "The bell is added to the duplicate: " + added.Error);
        var removal = session.PreviewEquipment(placed.Target, null, "bell");
        Assert(removal.Error == null, "The bell's removal previews: " + removal.Error);
    }

    // An edit of the original's coverage changes the original alone: the duplicate keeps its own detection.
    static void AnEditOfTheOriginalLeavesTheDuplicate() {
        var world = new World();
        var session = world.Session(out _, out _);
        var placed = Place(world, session);
        Assert(session.AddDetection(placed.Target, "siding", NewTrack("Snew")).Applied, "Detection is added to the duplicate");
        var copy = world.Copy(placed.Target);
        var moved = session.Cover(CrossingId(CrossingPath), "T1I", CircuitCoverage.Tracks(Range("Sspur", 308, 550), Range("Sspur", 292, 308), Range("Sspur", 292, 50)));
        Assert(moved.Applied && world.Triggers(world.Source, "Sspur", 300) && !world.Triggers(world.Source, "Sold", 300), "The original's circuit covers another track: " + moved.Error);
        Assert(!world.Triggers(copy, "Sspur", 300) && world.Triggers(copy, "Snew", 300) && copy.Circuits.Count == 1, "The duplicate keeps its own detection");
        var detached = session.Cover(CrossingId(CrossingPath), "T2I", CircuitCoverage.Detached);
        Assert(detached.Applied && world.Triggers(copy, "Snew", 300) && !world.Triggers(world.Source, "Sother", 200), "Detaching a circuit of the original leaves the duplicate: " + detached.Error);
    }

    // Each pad of the duplicate holds a marker ID of its own, which no pad of the original holds, and no track binding:
    // it has no registration and is a problem while it is enabled. The pads of the original keep their IDs, bindings and
    // registrations. Rebind binds a pad of the duplicate under its own ID, in a modification that depends on the
    // duplicate's; a fresh load builds it bound.
    static void EachPadOfTheDuplicateStartsUnboundWithItsOwnMarkerId() {
        var world = new World();
        var session = world.Session(out var store, out _);
        var placed = Place(world, session);
        var ids = (Item(session, placed.Target)[ModObjects.PartMarkerIdsMember] as JArray)?.Select(t => (string)t).ToArray();
        Assert(ids != null && ids.SequenceEqual(new[] { Draw(0), Draw(1), Draw(2) }) && world.Builder.PartIds.SequenceEqual(ids), "The scene patch file holds a generated marker ID for each pad: " + Item(session, placed.Target).ToString(Newtonsoft.Json.Formatting.None));
        var need = BindingRules.Need("Generic", true, false);
        for (var i = 0; i < PadNames.Length; i++) {
            var pad = world.Pad(placed.Target.Path + "/" + PadNames[i]);
            Assert(pad != null && pad.Id == ids[i] && pad.Segment == null && world.Own(pad) == "none", PadNames[i] + " of the duplicate holds its own ID and no binding: " + pad?.Id + " " + (pad == null ? "" : world.Own(pad)));
            Assert(BindingRules.Problem(need, true, "Marker", pad.Segment, null) == "Marker has an empty segment binding", PadNames[i] + " of the duplicate is a problem until it is bound");
            var source = world.SourcePad(i);
            Assert(source.Id == "Mpad" + (i + 1) && world.Own(source) == "Sjdl|a|" + (10 + i), PadNames[i] + " of the original keeps its ID and registration: " + world.Own(source));
        }
        var padId = PadId(placed.Target.Path + "/" + PadNames[0]);
        var bound = session.Bind(padId, MarkerBinding.At(new TrackLocation("S9sc", "a", 20)));
        var first = world.Pad(padId.Path);
        Assert(bound.Applied && first.Segment == "S9sc" && world.Own(first) == "S9sc|a|20" && BindingRules.Problem(need, true, "Marker", first.Segment, true) == null, "Rebind binds a pad of the duplicate under its own ID: " + bound.Error);
        Assert(world.Own(world.SourcePad(0)) == "Sjdl|a|10" && world.Pads.Geometry(first).Generated, "The original's pad keeps its registration, and the bound pad has its geometry");
        var dependsOn = (Item(session, padId)["dependsOn"] as JArray)?.Select(t => (string)t).ToArray();
        Assert(dependsOn != null && dependsOn.SequenceEqual(new[] { KeyOf(session, placed.Target) }), "The pad's modification depends on the duplicate's");
        session.BeginSave(); session.CompleteNativeSave();
        // The part marker IDs stand in a mod object, whose minimum is theirs: Save raises the dependency to it.
        Assert(store.Info.Contains(LoadedMod.PatcherId + " >=0.5.0"), "Save raises the patcher dependency to the mod object version: " + store.Info);
        var next = new World();
        var result = next.Engine.Apply(store.Patch, "author/pack");
        var loaded = next.Pad(padId.Path);
        Assert(result.Outcomes.Values.All(o => o.Applied) && loaded != null && loaded.Id == ids[0] && next.Own(loaded) == "S9sc|a|20" && next.Own(next.SourcePad(0)) == "Sjdl|a|10",
            "A fresh load builds the pad with its own ID and binding: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        Assert(next.Pad(placed.Target.Path + "/" + PadNames[1])?.Segment == null, "A pad that was not bound stays unbound after a fresh load");
    }

    // A scene patch file whose crossing mod object holds too few marker IDs for its pads, one twice, or one that a
    // scene marker already holds, fails that mod object before anything is built. Only a mod object holds part marker IDs.
    static void ACrossingModObjectNeedsAMarkerIdForEachPad() {
        var made = ModObjects.Path(CrossingPath, "author/pack", "mod-object-crossing");
        string Target(string path) => "{\"scene\":\"" + Scene + "\",\"path\":\"" + path + "\",\"component\":\"" + CrossingCoverageScene.ComponentType + "\",\"structure\":\"" + CrossingCoverageScene.Structure + "\"}";
        string Doc(string ids, bool modObject = true) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"mod-object-crossing\":{\"target\":" + Target(modObject ? made : CrossingPath)
            + (modObject ? ",\"source\":" + Target(CrossingPath) : "") + (ids == null ? "" : ",\"partMarkerIds\":" + ids)
            + (modObject ? ",\"placement\":{\"mode\":\"fixed\",\"position\":[5200,14,5900],\"rotation\":[0,0,0,1]}" : ",\"relationships\":{\"crossingCoverage\":{\"circuits\":{\"T2I\":{\"detach\":true}}}}") + ",\"dependsOn\":[]}}}";
        foreach (var (ids, reason) in new[] {
            ((string)null, ModObjects.PartMarkerIdCount(3, 0)), ("[\"Mnew1\",\"Mnew2\"]", ModObjects.PartMarkerIdCount(3, 2)),
            ("[\"Mnew1\",\"Mnew1\",\"Mnew3\"]", ModObjects.PartMarkerIdsShape), ("\"Mnew1\"", ModObjects.PartMarkerIdsShape), ("[\"Mnew1\",7,\"Mnew3\"]", ModObjects.PartMarkerIdsShape),
            ("[\"Mnew1\",\"Mpad2\",\"Mnew3\"]", ReplacementMarkerIds.UsedBy("Mpad2", CrossingPath + "/Crossing Pad 2")) }) {
            var world = new World();
            var outcome = world.Engine.Apply(Doc(ids), "author/pack").Outcomes["mod-object-crossing"];
            Assert(!outcome.Applied && outcome.Error == reason && world.Builder.BuiltFrom.Count == 0, "Part marker IDs " + (ids ?? "none") + " fail the mod object before it is built: " + outcome.Error);
            Assert(Enumerable.Range(0, 3).All(i => world.Own(world.SourcePad(i)) == "Sjdl|a|" + (10 + i)), "The original's pads keep their registrations");
        }
        var free = new World();
        var applied = free.Engine.Apply(Doc("[\"Mnew1\",\"Mnew2\",\"Mnew3\"]"), "author/pack").Outcomes["mod-object-crossing"];
        Assert(applied.Applied && free.Builder.PartIds.SequenceEqual(new[] { "Mnew1", "Mnew2", "Mnew3" }), "Free IDs build it: " + applied.Error);
        var plain = new World();
        var refused = plain.Engine.Apply(Doc("[\"Mnew1\"]", false), "author/pack").Outcomes["mod-object-crossing"];
        Assert(!refused.Applied && refused.Error == ModObjects.PartMarkerIdsOnlyModObject && plain.Source.Circuits.Count == 2, "A modification of a map object holds no part marker IDs: " + refused.Error);
    }

    // The crossing capability moves no crossing: the duplicate stands where its modification places it, and a
    // modification of a map crossing still takes no placement.
    static void ACrossingModObjectKeepsItsPlace() {
        var world = new World();
        var session = world.Session(out _, out _);
        var placed = Place(world, session);
        var moved = session.Edit(placed.Target, new Placement(new double[] { 6000, 14, 6000 }, Upright), null, null, null);
        Assert(!moved.Applied && world.Copy(placed.Target).Position.SequenceEqual(AtNewSiding), "A move of the duplicate's controller is refused: " + moved.Error);
        var original = session.Edit(CrossingId(CrossingPath), new Placement(new double[] { 6000, 14, 6000 }, Upright), null, null, null);
        Assert(!original.Applied && world.Source.Position[0] == 4387, "A move of the original's controller is refused as before: " + original.Error);
    }

    // A single crossing pad is duplicated as a mod object of its own: it stands beside the crossing it came from, not
    // in it, holds a generated marker ID and no binding, and Rebind and Set replacement marker ID apply to it. The pad
    // it was made from keeps its ID and registration, and a pad of the map still takes no replacement ID.
    static void ASinglePadIsDuplicatedUnbound() {
        var world = new World();
        var session = world.Session(out var store, out _);
        var source = PadId(CrossingPath + "/" + PadNames[0]);
        var placed = session.PlaceModObject(source, new Placement(AtNewSiding, Upright), null, null, Draws());
        Assert(placed.Applied && placed.Target.Structure == CrossingPadScene.Structure, "The pad is duplicated: " + placed.Error);
        Assert(ModObjects.ParentOf(placed.Target.Path) == ModObjects.ParentOf(CrossingPath) && ModObjects.NameOf(placed.Target.Path).StartsWith("Crossing Pad [mod object ", StringComparison.Ordinal),
            "Its copy stands beside the crossing, not in it: " + placed.Target.Path);
        var item = Item(session, placed.Target);
        var copy = world.Pad(placed.Target.Path);
        Assert(copy != null && copy.Id == Draw(0) && (string)item[ReplacementMarkerIds.Member] == Draw(0) && item["relationships"] == null, "It holds a generated marker ID and no binding: " + item.ToString(Newtonsoft.Json.Formatting.None));
        Assert(copy.Segment == null && world.Own(copy) == "none" && world.Own(world.SourcePad(0)) == "Sjdl|a|10" && world.SourcePad(0).Id == "Mpad1", "It starts unbound, and the original keeps its ID and registration");
        var bound = session.Bind(placed.Target, MarkerBinding.At(new TrackLocation("S9sc", "a", 20)));
        Assert(bound.Applied && world.Own(copy) == "S9sc|a|20" && world.Pads.Geometry(copy).Generated, "Rebind binds it under its own ID: " + bound.Error);
        var renamed = session.SetMarkerId(placed.Target, "Mnew1", null);
        Assert(renamed.Applied && copy.Id == "Mnew1" && world.Own(copy) == "S9sc|a|20", "Set replacement marker ID gives it another ID: " + renamed.Error);
        var taken = session.SetMarkerId(placed.Target, "Mpad2", null);
        Assert(!taken.Applied && copy.Id == "Mnew1", "An ID a pad of the map holds is refused: " + taken.Error);
        var map = session.SetMarkerId(source, "Mnew2", null);
        Assert(!map.Applied && map.Error == ReplacementMarkerIds.NotSupported && world.SourcePad(0).Id == "Mpad1", "A pad of the map still takes no replacement ID: " + map.Error);
        session.BeginSave(); session.CompleteNativeSave();
        var next = new World();
        var result = next.Engine.Apply(store.Patch, "author/pack");
        var loaded = next.Pad(placed.Target.Path);
        Assert(result.Outcomes.Values.All(o => o.Applied) && loaded?.Id == "Mnew1" && next.Own(loaded) == "S9sc|a|20" && next.Own(next.SourcePad(0)) == "Sjdl|a|10", "A fresh load builds it with its ID and binding");
    }

    // A crossing without signals is duplicated from its crossing object as one mod object: the copy holds its own
    // crossbucks and its own pad, with a marker ID of its own and no track binding, a problem until Rebind sets it. The
    // original keeps its crossbucks and its pad's registration. The crossing object itself is not moved or disabled.
    // Remove takes the whole copy away, and a fresh load builds it again.
    static void ACrossingWithoutSignalsIsDuplicatedAsOneGroup() {
        foreach (var name in new[] { "Crossing Pad", "Sign-Crossing0 (1)", "Sign-Crossing0 (2)" }) Assert(Map.Objects.Count(o => o.Listed && o.Path == AlarkaPath + "/" + name) == 1, "The base map fixture lists " + name + " of the crossing at Alarka Jct");
        var world = new World();
        var session = world.Session(out var store, out var editor);
        var source = CrossingObjectScene.Identity(Scene, AlarkaPath);
        var placed = session.PlaceModObject(source, new Placement(AtNewSiding, Upright), null, null, Draws());
        Assert(placed.Applied && placed.Target.Structure == CrossingObjectScene.Structure && ModObjects.IsPath(placed.Target.Path) && ModObjects.ParentOf(placed.Target.Path) == ModObjects.ParentOf(AlarkaPath),
            "The crossing is duplicated as one mod object beside its original: " + placed.Error);
        var copy = world.Group(placed.Target);
        Assert(copy != null && !ReferenceEquals(copy, world.Alarka) && copy.Position.SequenceEqual(AtNewSiding) && copy.Crossbucks.SequenceEqual(world.Alarka.Crossbucks), "The duplicate holds its own crossbucks where it was placed");
        var item = Item(session, placed.Target);
        var ids = (item[ModObjects.PartMarkerIdsMember] as JArray)?.Select(t => (string)t).ToArray();
        var pad = world.Pad(placed.Target.Path + "/Crossing Pad");
        Assert(ids != null && ids.SequenceEqual(new[] { Draw(0) }) && pad != null && pad.Id == Draw(0) && pad.Segment == null && world.Own(pad) == "none", "Its pad holds a marker ID of its own and no binding: " + item.ToString(Newtonsoft.Json.Formatting.None));
        Assert(BindingRules.Problem(BindingRules.Need("Generic", true, false), true, "Marker", pad.Segment, null) == "Marker has an empty segment binding", "Its pad is a problem until it is bound");
        var original = world.PadsOf[world.Alarka][0].Value;
        Assert(original.Id == "Malk" && world.Own(original) == "Nnew|a|5" && world.Alarka.Crossbucks.Count == 2 && world.Unprotected.Objects[AlarkaPath].Single() == world.Alarka, "The original keeps its crossbucks and its pad's ID and registration");
        Assert((string)item["source"]["path"] == AlarkaPath && (string)item["source"]["structure"] == CrossingObjectScene.Structure && item["source"]["padMarkerId"] == null, "The source names the crossing object, without a key where the path is its own");
        var bound = session.Bind(PadId(placed.Target.Path + "/Crossing Pad"), MarkerBinding.At(new TrackLocation("S9sc", "a", 20)));
        Assert(bound.Applied && world.Own(pad) == "S9sc|a|20" && world.Own(original) == "Nnew|a|5", "Rebind binds the duplicate's pad alone: " + bound.Error);
        var moved = session.Edit(placed.Target, new Placement(new double[] { 6000, 14, 6000 }, Upright), null, null, null);
        var disabled = session.Edit(placed.Target, null, false, null, null);
        Assert(!moved.Applied && !disabled.Applied && copy.Position.SequenceEqual(AtNewSiding), "The crossing object is not moved or disabled as one object: " + moved.Error);
        session.BeginSave(); session.CompleteNativeSave();
        var next = new World();
        var result = next.Engine.Apply(store.Patch, "author/pack");
        var loaded = next.Pad(placed.Target.Path + "/Crossing Pad");
        Assert(result.Outcomes.Values.All(o => o.Applied) && next.Group(placed.Target) != null && loaded?.Id == Draw(0) && next.Own(loaded) == "S9sc|a|20", "A fresh load builds the duplicate with its pad bound: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        var removed = session.Remove(placed.Target, null);
        Assert(removed.Applied && world.Group(placed.Target) == null && world.Pad(placed.Target.Path + "/Crossing Pad") == null && JObject.Parse(session.ScenePatchFileText)["modifications"].Count() == 0,
            "Remove takes the duplicate, its pad and the pad's binding away: " + removed.Error);
        Assert(world.Own(original) == "Nnew|a|5", "The original's pad keeps its registration");
        editor.Undo();
        Assert(world.Group(placed.Target) != null && world.Own(world.Pad(placed.Target.Path + "/Crossing Pad")) == "S9sc|a|20", "Undo puts the duplicate back with its pad bound");
    }

    // Three crossings stand at one path below Old River Rd. The marker ID of a crossing's pad names the crossing: the
    // duplicate is made from that crossing alone, and its source keeps the key, so each load builds it from the same
    // one. Without the key the source matches three objects and nothing is built.
    static void CrossingsAtOnePathAreToldApartByPadMarkerId() {
        var world = new World();
        var session = world.Session(out var store, out _);
        var placed = session.PlaceModObject(CrossingObjectScene.Identity(Scene, OldRiverPath, "Mold2"), new Placement(AtNewSiding, Upright), null, null, Draws());
        Assert(placed.Applied && world.Builder.BuiltFrom.SequenceEqual(new object[] { world.OldRiver[1] }) && placed.Target.PadMarkerId == null, "The duplicate is made from the crossing whose pad holds the ID: " + placed.Error);
        Assert((string)Item(session, placed.Target)["source"]["padMarkerId"] == "Mold2", "Its source keeps the pad marker ID");
        Assert(world.Unprotected.Objects[OldRiverPath].SequenceEqual(world.OldRiver) && Enumerable.Range(0, 3).All(i => world.Own(world.PadsOf[world.OldRiver[i]][0].Value) == "Nnew|a|" + (20 + 10 * i)), "The three crossings keep their place and their pads' registrations");
        session.BeginSave(); session.CompleteNativeSave();
        var next = new World();
        var result = next.Engine.Apply(store.Patch, "author/pack");
        Assert(result.Outcomes.Values.All(o => o.Applied) && next.Builder.BuiltFrom.SequenceEqual(new object[] { next.OldRiver[1] }), "A fresh load builds it from the same crossing");
        var unkeyed = new World();
        var refused = unkeyed.Session(out _, out _).PlaceModObject(CrossingObjectScene.Identity(Scene, OldRiverPath), new Placement(AtNewSiding, Upright), null, null, Draws());
        Assert(!refused.Applied && refused.Error.StartsWith(PatchEngine.TargetAmbiguous, StringComparison.Ordinal) && unkeyed.Builder.BuiltFrom.Count == 0, "Without the key nothing is built: " + refused.Error);
        var other = new World();
        var none = other.Session(out _, out _).PlaceModObject(CrossingObjectScene.Identity(Scene, OldRiverPath, "Mgone"), new Placement(AtNewSiding, Upright), null, null, Draws());
        Assert(!none.Applied && other.Builder.BuiltFrom.Count == 0, "A pad marker ID no crossing holds matches none: " + none.Error);
    }

    // The inventory as the native discovery lists it: a crossing object is no observation of its own, its crossbucks
    // and its pads are, and each pad names the object it stands in.
    sealed class Listed : IInspectionScene, IHolderScene {
        public IEnumerable<string> MapIds => new[] { "map" };
        public readonly List<SceneObservation> Objects = new List<SceneObservation>();
        public readonly Dictionary<object, object> Holders = new Dictionary<object, object>();
        public IEnumerable<SceneObservation> Discover() => Objects;
        public object HolderOf(object handle) => Holders.TryGetValue(handle, out var holder) ? holder : null;
    }
    // The loaded crossing objects as Resolve finds them: each with the marker IDs of the pads directly in it.
    sealed class Standing : ICrossingObjectWorld {
        public readonly Dictionary<string, List<object>> Objects = new Dictionary<string, List<object>>();
        public readonly Dictionary<object, string[]> Pads = new Dictionary<object, string[]>();
        public IEnumerable<string> MapIds => new[] { "map" };
        public IReadOnlyList<object> Find(string scene, string path) => Objects.TryGetValue(path, out var found) ? found : new List<object>();
        public IReadOnlyList<string> PadMarkerIds(object crossing) => Pads[crossing];
        public double[] Position(object crossing) => new double[] { 0, 0, 0 };
        public double[] Rotation(object crossing) => Upright;
        public bool Active(object crossing) => true;
    }
    static SceneObservation Crossbuck(string path, string padMarkerId = null) => new SceneObservation(new object(), new TargetIdentity(Scene, path, SceneObjects.ComponentType, SceneObjects.Structure, padMarkerId),
        SceneObjectRules.Family, true, new double[] { 0, 0, 0 }, Upright, new string[0], new string[0], new string[0], "Fixed placement", category: SceneObjectCategories.GradeCrossing);
    static SceneObservation PadRow(string path, string markerId) => new SceneObservation(new object(), PadId(path), SceneFamilies.CrossingPad, true,
        new double[] { 0, 0, 0 }, Upright, new[] { SceneObservation.MarkerIdLine + markerId + "; type=Generic" }, new string[0], new string[0], "Fixed placement");
    // Puts one crossing object at the path into the inventory and, unless inWorld is false, into the world Resolve
    // reads: its crossbucks, each with the pad marker ID the native rule gives it, and a pad in it for each marker ID.
    static void Stand(Listed listed, Standing world, string path, string[] padIds, int crossbucks = 2, string crossbuckPadId = null, bool inWorld = true) {
        var crossing = new object();
        if (inWorld) {
            if (!world.Objects.TryGetValue(path, out var at)) world.Objects[path] = at = new List<object>();
            at.Add(crossing); world.Pads[crossing] = padIds;
        }
        for (var i = 0; i < crossbucks; i++) listed.Objects.Add(Crossbuck(path + "/Sign-Crossing0 (" + (i + 1) + ")", crossbuckPadId));
        foreach (var id in padIds) { var pad = PadRow(path + "/Crossing Pad", id); listed.Objects.Add(pad); listed.Holders[pad.Handle] = crossing; }
    }
    static string ScenePatchFile(TargetIdentity target) {
        var json = new JObject { ["scene"] = target.Scene, ["path"] = target.Path, ["component"] = target.Component, ["structure"] = target.Structure };
        if (target.PadMarkerId != null) json[TargetIdentity.PadMarkerMember] = target.PadMarkerId;
        return "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"crossing\":{\"target\":" + json.ToString(Newtonsoft.Json.Formatting.None) + ",\"dependsOn\":[]}}}";
    }
    // Session details and the engine count the same crossing objects for the target.
    static void Agrees(Listed listed, Standing world, TargetIdentity target, int expected, string situation) {
        var inventory = new SceneInventory(listed); inventory.Refresh();
        var status = inventory.InspectScenePatchFile(ScenePatchFile(target)).Single();
        var resolved = new CrossingObjectScene(world, Capability.Unchecked).Resolve(target).Count;
        var problem = expected == 1 ? null : expected == 0 ? "Target is missing or has no inspection adapter" : "Target is ambiguous";
        Assert(status.MatchCount == expected && resolved == expected && (problem == null ? status.Problems.Count == 0 : status.Problems.Contains(problem)),
            situation + ": Session details counts " + status.MatchCount + " (" + String.Join(", ", status.Problems) + "), the engine " + resolved + ", expected " + expected);
    }

    // Right after Save, Session details lists the duplicate of a crossing without signals as its modification matching
    // one object, as the engine finds it: a crossing object is known by the pads directly in it, and a mod object also
    // without one. A copy built beside an identical instance stands at another parent with the same mark and matches as
    // well. Crossings that share a path are told apart by the objects their pads stand in, so a crossing with two pad
    // IDs or with crossbucks that carry no pad marker ID still counts as one of several, and a pad marker ID names the
    // crossing whose pads hold it, at a shared path or not. Crossbucks without a pad are no crossing object.
    static void TheSuppliedDefinitionOfACopiedCrossingMatchesItsCopy() {
        var world = new World();
        var session = world.Session(out var store, out _);
        var placed = session.PlaceModObject(CrossingObjectScene.Identity(Scene, AlarkaPath), new Placement(AtNewSiding, Upright), null, null, Draws());
        Assert(placed.Applied, "The crossing is duplicated: " + placed.Error);
        session.BeginSave(); session.CompleteNativeSave();
        var copy = placed.Target.Path;
        var listed = new Listed(); var standing = new Standing();
        Stand(listed, standing, AlarkaPath, new[] { "Malk" });
        Stand(listed, standing, copy, new[] { Draw(0) });
        var inventory = new SceneInventory(listed); inventory.Refresh();
        string Status(SceneInventory current) => String.Join("; ", current.InspectScenePatchFile(store.Patch).Select(d => d.Id + " matches " + d.MatchCount + " " + String.Join(", ", d.Problems)));
        var definition = inventory.InspectScenePatchFile(store.Patch).Single();
        Assert(definition.MatchCount == 1 && definition.Problems.Count == 0, "The saved duplicate matches its copy: " + Status(inventory));
        Agrees(listed, standing, placed.Target, 1, "The copy");
        Agrees(listed, standing, CrossingObjectScene.Identity(Scene, AlarkaPath, "Malk"), 1, "A pad marker ID at a path of its own");
        Agrees(listed, standing, CrossingObjectScene.Identity(Scene, AlarkaPath, "Mgone"), 0, "A pad marker ID no pad holds");
        var elsewhere = new Listed();
        var moved = "World/Roads Elsewhere/" + copy.Substring(copy.LastIndexOf('/') + 1);
        Stand(elsewhere, new Standing(), moved, new[] { Draw(0) });
        var built = new SceneInventory(elsewhere); built.Refresh();
        Assert(built.InspectScenePatchFile(store.Patch).Single().MatchCount == 1, "A copy that stands at another parent matches by its mark: " + Status(built));
        var padless = new Listed(); var padlessWorld = new Standing();
        Stand(padless, padlessWorld, copy, new string[0]);
        Agrees(padless, padlessWorld, placed.Target, 1, "A mod object without a pad");
        Agrees(padless, padlessWorld, CrossingObjectScene.Identity(Scene, copy, Draw(0)), 0, "A pad marker ID in a mod object without a pad");
        var gone = new SceneInventory(new Listed()); gone.Refresh();
        definition = gone.InspectScenePatchFile(store.Patch).Single();
        Assert(definition.MatchCount == 0 && definition.Problems.Contains("Target is missing or has no inspection adapter"), "Without the copy's rows it matches none: " + Status(gone));
        var noPad = new Listed(); var noPadWorld = new Standing();
        Stand(noPad, noPadWorld, AlarkaPath, new string[0], inWorld: false);
        Agrees(noPad, noPadWorld, CrossingObjectScene.Identity(Scene, AlarkaPath), 0, "Crossbucks without a pad outside a mod object");
        var shared = new Listed(); var sharedWorld = new Standing();
        Stand(shared, sharedWorld, OldRiverPath, new[] { "Mold1" }, crossbuckPadId: "Mold1");
        Stand(shared, sharedWorld, OldRiverPath, new[] { "Mold2", "Mold3" });
        Agrees(shared, sharedWorld, CrossingObjectScene.Identity(Scene, OldRiverPath), 2, "A shared path with a crossing of two pad IDs");
        Agrees(shared, sharedWorld, CrossingObjectScene.Identity(Scene, OldRiverPath, "Mold3"), 1, "The second pad ID of a crossing");
        Agrees(shared, sharedWorld, CrossingObjectScene.Identity(Scene, OldRiverPath, "Mold1"), 1, "The pad ID of a crossing at a shared path");
        var unnamed = new Listed(); var unnamedWorld = new Standing();
        Stand(unnamed, unnamedWorld, OldRiverPath, new[] { "Mold1" }, crossbucks: 1);
        Stand(unnamed, unnamedWorld, OldRiverPath, new[] { "Mold2" }, crossbucks: 0);
        Agrees(unnamed, unnamedWorld, CrossingObjectScene.Identity(Scene, OldRiverPath), 2, "A shared path whose crossbucks carry no pad marker ID");
        Agrees(unnamed, unnamedWorld, CrossingObjectScene.Identity(Scene, OldRiverPath, "Mold2"), 1, "A pad ID at a shared path whose crossbucks carry none");
        Assert(InventoryPresentationState.MarkerIdLine == SceneObservation.MarkerIdLine && PadRow("p", "Mx").MarkerId == "Mx", "A pad's marker ID is read from the binding line the inventory writes");
    }

    // The outliner's node of a crossing without signals names its crossing object: the scene, the path the members
    // stand in and, where crossings share that path, the pad marker ID of the crossing. Duplicate starts from it.
    static void TheCrossingNodeNamesItsCrossingObject() {
        var rows = Map.Observations(out _);
        var tree = OutlinerGrouping.Build(rows, rows, "", false, null);
        var nodes = tree.GroupNodes.Values.Where(n => n.Identity != null).ToArray();
        Assert(nodes.Length != 0 && nodes.All(n => n.Identity.Structure == CrossingObjectScene.Structure && n.Identity.Component == CrossingObjectScene.ComponentType && n.Member != null
            && ModObjects.ParentOf(n.Member.Identity.Path) == n.Identity.Path), "Every crossing node names the object its members stand in");
        var alarka = nodes.Where(n => n.Identity.Path == AlarkaPath).ToArray();
        Assert(alarka.Length == 1 && alarka[0].Identity.PadMarkerId == null, "A crossing with a path of its own needs no key");
        // The three crossings at one path, as the inventory lists them: each crossbuck names its crossing's pad marker
        // ID, and each pad shows its own.
        var listed = new List<SceneObservation>();
        foreach (var id in new[] { "Mold1", "Mold2", "Mold3" }) {
            foreach (var sign in new[] { "Sign-Crossing0 (1)", "Sign-Crossing0 (2)" })
                listed.Add(new SceneObservation(new object(), new TargetIdentity(Scene, OldRiverPath + "/" + sign, SceneObjects.ComponentType, SceneObjects.Structure, id), SceneFamilies.SceneObject, true,
                    new double[] { 5350, 10, 5747 }, new double[] { 0, 0, 0, 1 }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "placement", category: SceneObjectCategories.GradeCrossing));
            listed.Add(new SceneObservation(new object(), PadId(OldRiverPath + "/Crossing Pad"), SceneFamilies.CrossingPad, true, new double[] { 5350, 10, 5747 }, new double[] { 0, 0, 0, 1 },
                new[] { SceneObservation.MarkerIdBinding(id, "Generic"), "Marker segment=Nnew; end=a; distance=20" }, Array.Empty<string>(), Array.Empty<string>(), "binding"));
        }
        var logPending = OutlinerGrouping.LogPending;
        OutlinerGrouping.Result built;
        try { OutlinerGrouping.LogPending = true; built = OutlinerGrouping.Build(listed, listed, "", false, null); } finally { OutlinerGrouping.LogPending = logPending; }
        var shared = built.GroupNodes.Values.Where(n => n.Identity?.Path == OldRiverPath).Select(n => n.Identity.PadMarkerId).OrderBy(id => id).ToArray();
        Assert(shared.SequenceEqual(new[] { "Mold1", "Mold2", "Mold3" }), "The three crossings below Old River Rd are named by their pad marker IDs: " + String.Join(",", shared));
        // The frame of a selected crossing node encloses its own crossbucks and pad: at a shared path, not those of the
        // crossings beside it.
        foreach (var node in built.GroupNodes.Values.Where(n => n.Identity?.Path == OldRiverPath)) {
            var id = node.Identity.PadMarkerId;
            Assert(node.Members.Count >= 2 && node.Members.All(m => m.Identity.PadMarkerId == id || InventoryPresentationState.MarkerIdOf(m) == id)
                && node.Members.Any(m => m.Family == SceneFamilies.CrossingPad), "The node of crossing " + id + " lists only its own members: " + String.Join(", ", node.Members.Select(m => m.Identity.Path + "/" + m.Identity.PadMarkerId)));
        }
        // Each pad, with the marker ID line the inventory writes, stands in the node of its own crossing.
        var unprotected = built.Crossings.Where(line => line.StartsWith("unprotected ", StringComparison.Ordinal) && line.Contains(OldRiverPath)).ToArray();
        Assert(!built.Crossings.Any(line => line.StartsWith("unmatched", StringComparison.Ordinal)) && unprotected.Length == 3
            && new[] { "Mold1", "Mold2", "Mold3" }.All(id => unprotected.Count(line => line.Contains("pad marker " + id) && line.Contains(SceneFamilies.CrossingPad + " ")) == 1),
            "Each pad at the shared path joins its crossing's node: " + String.Join(" | ", built.Crossings));
        var crossing = new SceneObservation(new object(), alarka[0].Identity, CrossingObjectScene.Family, true, new double[] { 0, 0, 0 }, new double[] { 0, 0, 0, 1 }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null);
        Assert(DuplicateState.Reason(crossing, new[] { SceneFamilies.SceneObject, SceneFamilies.ChildObject, SceneFamilies.CrossingPad, SceneFamilies.TrackMarker }) == null, "A crossing without signals can be duplicated: " + DuplicateState.Reason(crossing, null));
        Assert(DuplicateState.Reason(crossing, new[] { SceneFamilies.SceneObject, SceneFamilies.Milepost }) == DuplicateState.HoldsOtherFamily, "One that holds a milepost is not copied");
    }

    // The node of a duplicated crossing without signals is named as a mod object's row is: its source's name at the
    // place where it stands, with a sequence number among the crossings without signals of that place. Almond has no
    // such crossing in the base map fixture, so the duplicate there is number 1 and a second one number 2; Nantahala
    // has one, so a duplicate there is number 2. The map's own crossings keep their names.
    static void ADuplicatedCrossingNodeIsNamedByItsPlace() {
        var entries = Map.Observations(out var facts);
        Assert(Map.Objects.Count(o => o.Listed && o.Name.StartsWith("Sign-Crossing0", StringComparison.Ordinal) && o.Expected != null && o.Expected.EndsWith("@ Nantahala", StringComparison.Ordinal)) == 2
            && !Map.Objects.Any(o => o.Listed && o.Name.StartsWith("Sign-Crossing0", StringComparison.Ordinal) && o.Expected != null && o.Expected.EndsWith("@ Almond", StringComparison.Ordinal)),
            "The base map fixture has one crossing without signals at Nantahala and none at Almond");
        double[] At(string path) { var found = Map.Objects.Single(o => o.Listed && o.Path == path); return new[] { found.At[0], 10, found.At[1] }; }
        // The rows of a duplicate as the inventory lists them: the crossbucks and pad of the crossing at Alarka Jct,
        // under the mod object's path, where the duplicate stands.
        SceneObservation[] Duplicate(string key, double[] at) {
            var made = ModObjects.Path(AlarkaPath, "author/pack", key);
            return new[] { "Sign-Crossing0 (1)", "Sign-Crossing0 (2)", "Crossing Pad" }.Select(name => {
                var from = entries.Single(e => e.Identity.Path == AlarkaPath + "/" + name);
                var row = new SceneObservation(new object(), new TargetIdentity(from.Identity.Scene, made + "/" + name, from.Identity.Component, from.Identity.Structure), from.Family, true, at, Upright,
                    from.Bindings, Array.Empty<string>(), Array.Empty<string>(), from.Operations, category: from.Category, subcategory: from.Subcategory);
                facts[row] = facts[from];
                return row;
            }).ToArray();
        }
        string Label(IReadOnlyList<SceneObservation> all, string path) {
            var scene = entries.First(e => e.Identity.Path == AlarkaPath + "/Crossing Pad").Identity.Scene;
            var tree = OutlinerGrouping.Build(all, all, "", false, null);
            return tree.GroupNodes.TryGetValue(OutlinerGrouping.CrossingNodeKey(scene, path, null), out var node) ? node.Label : null;
        }
        OutlinerGrouping.InvalidatePlaces();
        OutlinerGrouping.Scene = Map.Scene(facts);
        try {
            var almond = At("World/Large Scenery/Almond/Almond Road Bridge"); var nantahala = At("World/Large Scenery/Nantahala/Crossing Unprotected/Crossing Pad");
            var first = Duplicate("mod-object-a", almond); var second = Duplicate("mod-object-b", almond); var third = Duplicate("mod-object-c", nantahala);
            var all = entries.Concat(first).Concat(second).Concat(third).ToArray();
            string Made(string key) => ModObjects.Path(AlarkaPath, "author/pack", key);
            Assert(Label(all, Made("mod-object-a")) == "Almond Crossing Unprotected (1) (1)" && Label(all, Made("mod-object-b")) == "Almond Crossing Unprotected (1) (2)",
                "The duplicates at Almond are named by their place and numbered from 1: " + Label(all, Made("mod-object-a")) + ", " + Label(all, Made("mod-object-b")));
            Assert(Label(all, Made("mod-object-c")) == "Nantahala Crossing Unprotected (1) (2)", "The duplicate at Nantahala follows the map's crossing there: " + Label(all, Made("mod-object-c")));
            Assert(Label(all, AlarkaPath) == "Crossing Unprotected (1)" && Label(all, "World/Large Scenery/Nantahala/Crossing Unprotected") == "Crossing Unprotected", "The map's crossings keep their names");
            var moved = Duplicate("mod-object-a", nantahala);
            Assert(Label(entries.Concat(moved).ToArray(), Made("mod-object-a")) == "Nantahala Crossing Unprotected (1) (2)", "The name follows the duplicate to another place");
        } finally {
            OutlinerGrouping.Scene = null;
            OutlinerGrouping.InvalidatePlaces();
        }
    }

    // Duplicate is offered for a crossing controller with what a crossing holds and for a crossing pad the patcher can
    // edit. A mast alone and a pad at a shared path keep their reasons.
    static void DuplicateIsOfferedForACrossingAndAPad() {
        var rows = Map.Observations(out _);
        var controller = rows.Single(r => r.Identity.Path == CrossingPath && r.Family == SceneFamilies.CrossingController);
        var below = new[] { SceneFamilies.CrossingController, SceneFamilies.CrossingMast, SceneFamilies.CrossingBell, SceneFamilies.CrossingPad, SceneFamilies.TrackSpan, SceneFamilies.TrackMarker, SceneFamilies.AddedEquipment, SceneFamilies.AddedBell, SceneFamilies.ChildObject };
        Assert(DuplicateState.Reason(controller, below) == null, "A crossing can be duplicated: " + DuplicateState.Reason(controller, below));
        Assert(DuplicateState.Reason(controller, below.Concat(new[] { SceneFamilies.Milepost })) == DuplicateState.HoldsOtherFamily, "A crossing that holds a milepost is not copied");
        var pad = rows.Single(r => r.Identity.Path == CrossingPath + "/" + PadNames[0]);
        Assert(pad.Identity.Structure == CrossingPadScene.Structure && DuplicateState.Reason(pad, new[] { SceneFamilies.CrossingPad }) == null, "A crossing pad can be duplicated: " + DuplicateState.Reason(pad, null));
        var shared = rows.First(r => r.Family == SceneFamilies.CrossingPad && r.Identity.Structure == null);
        Assert(DuplicateState.Reason(shared, null) == CommandBand.EditReason(shared.Operations), "A pad the patcher cannot edit is not duplicated: " + DuplicateState.Reason(shared, null));
        var mast = rows.First(r => r.Family == SceneFamilies.CrossingMast);
        Assert(DuplicateState.Reason(mast, null) == DuplicateState.OtherFamily, "A mast alone is not duplicated: " + DuplicateState.Reason(mast, null));
    }

    // The game keeps the state of a crossing, its lights, gates and bell, under the global ID of its key-value object,
    // in the save and between the peers of a multiplayer game. A copy holds an ID of its own, made from its source's
    // and its own mark, so the two crossings never share that state; the ID is the same at every load and on every peer.
    static void ACopyHoldsAGameStateIdOfItsOwn() {
        var made = ModObjects.Path(CrossingPath, "author/pack", "mod-object-crossing");
        var own = ModObjects.OwnGlobalId("bryson-everett", made);
        Assert(own == "bryson-everett [mod object author:pack mod-object-crossing]", "The ID holds the source's ID and the mod object's mark: " + own);
        Assert(ModObjects.OwnGlobalId("bryson-everett", made + "/Signal 1") == own && ModObjects.OwnGlobalId("bryson-everett", ModObjects.Path(CrossingPath, "author/pack", "mod-object-other")) != own,
            "It is the same below the mod object and differs between mod objects");
        Assert(ModObjects.OwnGlobalId("", made) == "" && ModObjects.OwnGlobalId(null, made) == null && ModObjects.OwnGlobalId("bryson-everett", CrossingPath) == "bryson-everett", "An empty ID stays empty, and an object of the map keeps its ID");
    }
}
