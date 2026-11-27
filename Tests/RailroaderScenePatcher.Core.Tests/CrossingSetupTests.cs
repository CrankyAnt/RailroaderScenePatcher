// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Whole scene patch file tests for the whole move of a grade-crossing setup through its crossing controller, against a
// controlled crossing: a controller whose game-object culler lists the object above each mast, two masts, one with a
// gate, the bell, two pads, three detection spans and a whistle sign. A placement of the controller moves the masts
// with their gates and the bell together, a mast with its own placement included; the pads, the detection spans and
// the whistle sign stay where they are. The same holds for a duplicated crossing.
static class CrossingSetupTests {
    const string SceneName = "BushnellWhittier", Crossing = "World/Large Scenery/Bryson/Grade Crossing - Bryson Everett";
    const string Mast1Path = Crossing + "/Signal 1/Signal 1";
    static void Assert(bool value, string message) { if (!value) throw new Exception("Crossing setups: " + message); }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static readonly double[] Identity = { 0, 0, 0, 1 }, Quarter = Turn.Yaw(90);
    // Where the setup stands in the scene, and where each test moves it.
    static readonly double[] Home = { 1100, 10, 20 }, Shifted = { 1110, 10, 25 };
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b, double tolerance = 0.001) => a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= tolerance);
    static bool SameTurn(IReadOnlyList<double> a, IReadOnlyList<double> b) => Math.Abs(Math.Abs(a.Zip(b, (x, y) => x * y).Sum()) - 1) < 1e-6;
    static TargetIdentity ControllerId(string path = Crossing) => new TargetIdentity(SceneName, path, CrossingCoverageScene.ComponentType, CrossingCoverageScene.Structure);
    static readonly TargetIdentity Mast1Id = new TargetIdentity(SceneName, Mast1Path, ExistingEquipmentScene.MastComponent, ExistingEquipmentScene.MastStructure);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static string Move(string id, double[] position, double[] rotation = null, string mode = "fixed", bool? active = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + Crossing + "\",\"component\":\"" + CrossingCoverageScene.ComponentType + "\",\"structure\":\"" + CrossingCoverageScene.Structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"" + mode + "\",\"position\":[" + String.Join(",", position.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[" + String.Join(",", (rotation ?? Identity).Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";

    // The group world of the crossings, which names what stays when a setup moves as a whole.
    sealed class Groups : FakeGroupWorld, IWholeMoveWorld {
        public readonly HashSet<object> Staying = new HashSet<object>();
        public IReadOnlyList<object> Stays(object group) => ((FakeGroup)group).Parts.Where(Staying.Contains).Cast<object>().ToArray();
        // What stands below a controller beside its parts moves with it, as the wrapper of added equipment does: told
        // the controller with its map placement before and after.
        public Action<object, Placement, Placement> Carried;
        public new void SetPlacement(object target, Placement placement) {
            var before = Observe(target).Root.Placement;
            base.SetPlacement(target, placement);
            Carried?.Invoke(target, before, Observe(target).Root.Placement);
        }
        public void SetPlacementKeeping(object group, Placement placement, IReadOnlyList<object> kept) {
            var standing = kept.Select(part => new KeyValuePair<object, Placement>(part, ObservePart(part).Part.Placement)).ToArray();
            SetPlacement(group, placement);
            foreach (var pair in standing) SetPartPlacement(pair.Key, pair.Value);
        }
    }
    // The equipment world over the same controllers. An addition stands below its controller, so it moves with it.
    sealed class Equipment : ICrossingEquipmentWorld {
        readonly Crossings crossings;
        public readonly FakeEquipmentWorld Inner = new FakeEquipmentWorld();
        public Equipment(Groups groups, Crossings crossings) {
            this.crossings = crossings;
            groups.Carried = (controller, before, after) => {
                var undo = Turn.Inverse(before.Rotation);
                foreach (var added in Inner.Live(crossings.Of[controller])) {
                    var offset = Turn.Rotate(undo, new[] { added.Position[0] - before.Position[0], added.Position[1] - before.Position[1], added.Position[2] - before.Position[2] });
                    var turned = Turn.Rotate(after.Rotation, offset);
                    added.Position = new[] { after.Position[0] + turned[0], after.Position[1] + turned[1], after.Position[2] + turned[2] };
                    added.Rotation = Turn.Multiply(after.Rotation, Turn.Multiply(undo, added.Rotation));
                }
            };
        }
        public IReadOnlyList<object> ResolveSource(EquipmentSource source) => Inner.ResolveSource(source);
        public string SourceProblem(object source, object controller, string pattern) => Inner.SourceProblem(source, crossings.Of[controller], pattern);
        public IReadOnlyList<SourceCandidate> Candidates(object controller) => Inner.Candidates(crossings.Of[controller]);
        public IReadOnlyList<object> Owned(object controller) => Inner.Owned(crossings.Of[controller]);
        public EquipmentAddition Describe(object addition) => Inner.Describe(addition);
        public object Construct(object controller, object source, EquipmentAddition addition) => Inner.Construct(crossings.Of[controller], source, addition);
        public void Destroy(object addition) => Inner.Destroy(addition);
        public bool Present(object addition) => Inner.Present(addition);
        public bool Built(object addition) => Inner.Built(addition);
        public Placement Placement(object addition) => Inner.Placement(addition);
        public void SetPlacement(object addition, Placement placement) => Inner.SetPlacement(addition, placement);
        public string Problem(object addition) => Inner.Problem(addition);
        public string Status(object addition) => Inner.Status(addition);
    }
    // The crossing world over the same controllers: the controller's object is the group, and its circuits and spans
    // are those of the controlled crossing world.
    sealed class Crossings : ICrossingCoverageWorld, IModObjectCrossings {
        readonly Groups groups;
        public readonly FakeCrossingWorld Inner = new FakeCrossingWorld();
        public readonly Dictionary<object, FakeCrossing> Of = new Dictionary<object, FakeCrossing>();
        public readonly HashSet<object> Made = new HashSet<object>();
        public Crossings(Groups groups) { this.groups = groups; }
        public bool IsModObject(object controller) => Made.Contains(controller);
        public IEnumerable<string> MapIds => groups.Maps;
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != CrossingCoverageScene.ComponentType || (identity.Structure != null && identity.Structure != CrossingCoverageScene.Structure)) return Array.Empty<object>();
            return groups.Objects.TryGetValue(identity.Path, out var found) ? found.Where(Of.ContainsKey).ToArray() : Array.Empty<object>();
        }
        public string PatternProblem(object controller) => Inner.PatternProblem(Of[controller]);
        public bool Unsettled(object controller) => Inner.Unsettled(Of[controller]);
        public IReadOnlyList<object> Circuits(object controller) => Inner.Circuits(Of[controller]);
        public IReadOnlyList<object> Spans(object circuit) => Inner.Spans(circuit);
        public string SpanId(object span) => Inner.SpanId(span);
        public SpanRange Range(object span) => Inner.Range(span);
        public IReadOnlyList<string> SegmentIds(object span) => Inner.SegmentIds(span);
        public bool Registered(object span) => Inner.Registered(span);
        public bool IdRegistered(string id) => Inner.IdRegistered(id);
        public bool Covers(object span) => Inner.Covers(span);
        public IReadOnlyList<double> SegmentLengths(string segment) => Inner.SegmentLengths(segment);
        public object CreateSpan(object controller, string id, SpanRange range) => Inner.CreateSpan(Of[controller], id, range);
        public void DestroySpan(object span) => Inner.DestroySpan(span);
        public object CreateCircuit(object controller, string key) => Inner.CreateCircuit(Of[controller], key);
        public string AdditionKey(object circuit) => Inner.AdditionKey(circuit);
        public void SetSpans(object circuit, object approachA, object island, object approachB) => Inner.SetSpans(circuit, approachA, island, approachB);
        public void SetCircuits(object controller, IReadOnlyList<object> circuits) => Inner.SetCircuits(Of[controller], circuits);
        public double[] Position(object controller) => groups.Observe(controller).Root.Placement.Position;
        public double[] Rotation(object controller) => groups.Observe(controller).Root.Placement.Rotation;
        public bool Active(object controller) => true;
    }
    // Builds a duplicated crossing as the native world does: a copy of the controller with everything below it, at the
    // placement, with a controller that evaluates no circuit.
    sealed class Builder : IModObjectScene {
        readonly Fixture f;
        public Builder(Fixture f) { this.f = f; }
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) {
            if (f.World.Objects.TryGetValue(identity.Path, out var there) && there.Count != 0) throw new InvalidOperationException(ModObjects.Taken);
            var from = (FakeGroup)source;
            var copy = new FakeGroup { Position = new[] { placement.Position[0] - f.World.Origin, placement.Position[1], placement.Position[2] }, Rotation = (double[])placement.Rotation.Clone(), RootCulling = null };
            copy.Parts.Clear(); copy.Cullers.Clear();
            var made = new Dictionary<FakePart, FakePart>();
            foreach (var part in from.Parts) {
                var twin = copy.Add(new FakePart { Id = part.Id, Offset = (double[])part.Offset.Clone(), Rotation = (double[])part.Rotation.Clone(), Parent = part.Parent == null ? null : made[part.Parent], Empty = part.Empty });
                made[part] = twin;
                if (f.World.Staying.Contains(part)) f.World.Staying.Add(twin);
            }
            foreach (var culler in from.Cullers) {
                var twin = new FakeCuller { Id = culler.Id, Switcher = culler.Switcher, Radius = culler.Radius, Authored = culler.Authored };
                twin.Lists.AddRange(culler.Lists.Select(part => made[part]));
                copy.Cullers.Add(twin);
            }
            f.World.Objects[identity.Path] = new List<object> { copy };
            f.Crossings.Of[copy] = new FakeCrossing();
            f.Crossings.Made.Add(copy);
            f.World.SetActive(copy, true);
            return copy;
        }
        public void Remove(object modObject) {
            foreach (var pair in f.World.Objects.Where(p => p.Value.Contains(modObject)).ToArray()) f.World.Objects.Remove(pair.Key);
            f.Crossings.Of.Remove(modObject); f.Crossings.Made.Remove(modObject);
        }
    }
    // The crossing stands at map 1100, 10, 20. Its culler has a 30 m sphere and lists the object above each mast. Its
    // one circuit covers segment Sold from 50 to 550.
    sealed class Fixture {
        public readonly Groups World = new Groups();
        public readonly Crossings Crossings;
        public readonly Equipment Equipment;
        public readonly FakeGroup Controller = new FakeGroup { RootCulling = null };
        public readonly FakeCrossing Crossing = new FakeCrossing();
        public readonly FakePart Outer1, Mast1, Gate1, Outer2, Mast2, Bell, Pad1, Pad2, East, Island, West, Sign;
        public readonly FakeCuller Culler;
        public readonly FakeSpan[] NativeSpans;
        public readonly PartHolds Holds = new PartHolds();
        public readonly PatchEngine Engine;
        public readonly SceneCapabilities Scene;
        public Fixture() {
            Crossings = new Crossings(World); Equipment = new Equipment(World, Crossings);
            Equipment.Inner.Device(Mast1Path);
            Controller.Parts.Clear(); Controller.Cullers.Clear();
            Outer1 = Controller.Add(new FakePart { Id = "Signal 1#1", Offset = new[] { -6.0, 0, 4 }, Empty = true });
            Mast1 = Controller.Add(new FakePart { Id = "Signal 1/Signal 1#2", Offset = new[] { -6.0, 0, 4 }, Parent = Outer1 });
            Gate1 = Controller.Add(new FakePart { Id = "Signal 1/Signal 1/Wishbone#3", Offset = new[] { -6.0, 3, 4 }, Parent = Mast1 });
            Outer2 = Controller.Add(new FakePart { Id = "Signal 2#4", Offset = new[] { 6.0, 0, -4 }, Empty = true });
            Mast2 = Controller.Add(new FakePart { Id = "Signal 2/Signal 2#5", Offset = new[] { 6.0, 0, -4 }, Parent = Outer2 });
            Bell = Controller.Add(new FakePart { Id = "Crossing Bell#6", Offset = new[] { 0.0, 0, 0 } });
            Pad1 = Controller.Add(new FakePart { Id = "Crossing Pad#7", Offset = new[] { 0.0, 0, -3 } });
            Pad2 = Controller.Add(new FakePart { Id = "Crossing Pad 2#8", Offset = new[] { 0.0, 0, 3 } });
            East = Controller.Add(new FakePart { Id = "Track 1 East#9", Offset = new[] { 0.0, 0, 0 }, Empty = true });
            Island = Controller.Add(new FakePart { Id = "Track 1 Island#10", Offset = new[] { 0.0, 0, 0 }, Empty = true });
            West = Controller.Add(new FakePart { Id = "Track 1 West#11", Offset = new[] { 0.0, 0, 0 }, Empty = true });
            Sign = Controller.Add(new FakePart { Id = "Whistle Sign E#12", Offset = new[] { 200.0, 0, 60 } });
            foreach (var kept in new[] { Pad1, Pad2, East, Island, West, Sign }) World.Staying.Add(kept);
            Culler = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Radius = 30, Authored = 30, Lists = { Outer1, Outer2 } };
            Controller.Cullers.Add(Culler);
            World.Objects[CrossingSetupTests.Crossing] = new List<object> { Controller };
            World.PartsAt[Mast1Path] = new List<object> { Mast1 };
            World.SetActive(Controller, true); World.Mutations = 0;
            NativeSpans = new[] { Crossings.Inner.Span("T1E", "Sold", 308, 550), Crossings.Inner.Span("T1I", "Sold", 292, 308), Crossings.Inner.Span("T1W", "Sold", 292, 50) };
            Crossing.Circuits.Add(new FakeCircuit { Spans = NativeSpans });
            Crossings.Of[Controller] = Crossing;
            Scene = new SceneCapabilities(() => World.Maps) { ModObjectWorld = new Builder(this) };
            Scene.Add(ExistingEquipmentScene.Masts(World, Holds));
            Scene.Add(new CrossingCoverageScene(Crossings, Capability.Unchecked, Equipment, new WholeMove(World, Holds)));
            Engine = new PatchEngine(Scene);
        }
        public double[] At(FakePart part) => World.ObservePart(part).Part.Placement.Position;
        public double[] Facing(FakePart part) => World.ObservePart(part).Part.Placement.Rotation;
        public double[] Root(FakeGroup group = null) => World.Observe(group ?? Controller).Root.Placement.Position;
        public string Where(FakeGroup group = null) => "root=" + N(Root(group)) + "; " + String.Join("; ", (group ?? Controller).Parts.Select(p => p.Id + "=" + N(At(p))));
        // Whether the masts, the gate and the bell stand around the root as the scene has them, turned by the yaw.
        public bool Together(double[] root, double degrees = 0, FakeGroup group = null) {
            var turn = Turn.Yaw(degrees); var parts = (group ?? Controller).Parts;
            bool Stands(int index, double[] offset) { var t = Turn.Rotate(turn, offset); return Near(At(parts[index]), new[] { root[0] + t[0], root[1] + t[1], root[2] + t[2] }) && SameTurn(Facing(parts[index]), turn); }
            return Stands(1, new[] { -6.0, 0, 4 }) && Stands(2, new[] { -6.0, 3, 4 }) && Stands(4, new[] { 6.0, 0, -4 }) && Stands(5, new[] { 0.0, 0, 0 });
        }
        // Whether the pads, the detection spans and the whistle sign stand where the scene has them around the place.
        public bool Kept(double[] place = null, FakeGroup group = null) {
            place = place ?? Home; var parts = (group ?? Controller).Parts;
            bool Stands(int index, double[] offset) => Near(At(parts[index]), new[] { place[0] + offset[0], place[1] + offset[1], place[2] + offset[2] }) && SameTurn(Facing(parts[index]), Identity);
            return Stands(6, new[] { 0.0, 0, -3 }) && Stands(7, new[] { 0.0, 0, 3 }) && Stands(8, new[] { 0.0, 0, 0 }) && Stands(9, new[] { 0.0, 0, 0 }) && Stands(10, new[] { 0.0, 0, 0 }) && Stands(11, new[] { 200.0, 0, 60 });
        }
        public bool Triggers(string segment, double distance, FakeCrossing crossing = null) => Crossings.Inner.Detects(crossing ?? Crossing, segment, distance);
        public PatchEditingSession Session(out Store store, out NativeMapEditor editor) {
            store = new Store(); editor = new NativeMapEditor();
            var session = new PatchEditingSession(Engine, editor, store, LoadedMod.PatcherId, "author/pack"); editor.Session = session;
            return session;
        }
        public FakeGroup Copy(TargetIdentity made) => World.Objects.TryGetValue(made.Path, out var found) && found.Count == 1 ? (FakeGroup)found[0] : null;
    }

    internal static void Run() {
        MovingTheSetupMovesMastsGatesAndBellTogether();
        RotatingTheSetupTurnsThemAboutTheSetup();
        PadsSpansAndDetectionStayAfterAWholeMove();
        AMastWithItsOwnPlacementMovesWithTheSetup();
        AddedEquipmentKeepsItsFixedPlacement();
        AMovedSetupKeepsItsCulling();
        RefusalsLeaveSceneAndFileUnchanged();
        AFailureAfterMutationRestoresTheSetup();
        HistorySaveReloadAndRemoval();
        ADuplicatedCrossingMovesAndRotatesAsAWhole();
        ThePlacementTabOffersTheWholeMove();
        Console.WriteLine("PASS: crossing setups");
    }

    static void MovingTheSetupMovesMastsGatesAndBellTogether() {
        var f = new Fixture();
        var outcome = f.Engine.Apply(Doc(Move("move-setup", Shifted))).Outcomes["move-setup"];
        Assert(outcome.Applied && Near(f.Root(), Shifted), "A move of the setup through its controller applies: " + outcome.Error + " " + f.Where());
        Assert(Near(f.At(f.Mast1), new[] { 1104.0, 10, 29 }) && Near(f.At(f.Gate1), new[] { 1104.0, 13, 29 }) && Near(f.At(f.Mast2), new[] { 1116.0, 10, 21 }) && Near(f.At(f.Bell), new[] { 1110.0, 10, 25 }),
            "The masts, the gate and the bell moved together and keep their placement relative to each other: " + f.Where());
        Assert(f.Together(Shifted), "Each keeps its direction");
    }

    static void RotatingTheSetupTurnsThemAboutTheSetup() {
        var f = new Fixture();
        var outcome = f.Engine.Apply(Doc(Move("turn-setup", Home, Quarter))).Outcomes["turn-setup"];
        Assert(outcome.Applied && Near(f.Root(), Home), "A rotation of the setup through its controller applies: " + outcome.Error);
        // A quarter turn about the vertical axis takes the mast at 6 m west and 4 m north to 4 m east and 6 m north.
        Assert(Near(f.At(f.Mast1), new[] { 1104.0, 10, 26 }) && Near(f.At(f.Gate1), new[] { 1104.0, 13, 26 }) && Near(f.At(f.Mast2), new[] { 1096.0, 10, 14 }) && Near(f.At(f.Bell), Home),
            "The masts, the gate and the bell turned together about the setup: " + f.Where());
        Assert(SameTurn(f.Facing(f.Mast1), Quarter) && SameTurn(f.Facing(f.Gate1), Quarter) && SameTurn(f.Facing(f.Mast2), Quarter), "Each turned with the setup");
        Assert(f.Kept(), "The pads, the detection spans and the whistle sign neither moved nor turned: " + f.Where());
    }

    static void PadsSpansAndDetectionStayAfterAWholeMove() {
        var f = new Fixture(); var session = f.Session(out _, out _);
        var before = session.Coverage(ControllerId()).Circuits.Single();
        Assert(f.Triggers("Sold", 300) && f.Triggers("Sold", 60) && f.Triggers("Sold", 540), "A train on the covered track triggers the crossing before the move");
        var moved = session.Edit(ControllerId(), new Placement(Shifted, Quarter), null);
        Assert(moved.Applied && f.Together(Shifted, 90), "The setup moved and turned as a whole: " + moved.Error + " " + f.Where());
        Assert(f.Kept(), "The pads, the detection spans and the whistle sign are where they were: " + f.Where());
        var after = session.Coverage(ControllerId()).Circuits.Single();
        Assert(after.Key == before.Key && after.Value.Kind == CircuitKind.Native && after.Value.SpanIds.SequenceEqual(new[] { "T1E", "T1I", "T1W" }), "The crossing keeps its circuit on the map's own detection spans: " + after.Value);
        Assert(f.Crossing.Circuits.Single().Spans.SequenceEqual(f.NativeSpans) && f.NativeSpans.All(s => f.Crossings.Inner.Registered(s)) && f.Crossings.Inner.Graph.Count == 3 && f.Crossings.Inner.Mutations == 0,
            "The move changed no circuit, span or registration");
        Assert(f.Triggers("Sold", 300) && f.Triggers("Sold", 60) && f.Triggers("Sold", 540) && !f.Triggers("Snew", 300), "Detection still works for the track it covered, and for no other");
        // Coverage edits and the whole move live in one modification and do not disturb each other.
        var added = session.AddDetection(ControllerId(), "siding", CircuitCoverage.Added(Range("Snew", 308, 550), Range("Snew", 292, 308), Range("Snew", 292, 50)));
        Assert(added.Applied && f.Triggers("Snew", 300) && f.Triggers("Sold", 300) && f.Together(Shifted, 90) && f.Kept(), "Detection added after the move applies and leaves the setup where it was moved to: " + added.Error + " " + f.Where());
        var again = session.Edit(ControllerId(), new Placement(Home, Identity), null);
        Assert(again.Applied && f.Together(Home) && f.Kept() && f.Triggers("Snew", 300) && f.Triggers("Sold", 300), "A move back keeps the added detection: " + again.Error + " " + f.Where());
    }
    sealed class AnyTrack : ITrackPoseScene { public TrackPose TrackPose(TrackAnchor anchor) => new TrackPose(new[] { 1200.0, 10, 20 }, new[] { 0.0, 0, 0, 1 }); }
    static SpanRange Range(string segment, double from, double to) => new SpanRange(new TrackLocation(segment, "a", from), new TrackLocation(segment, "a", to));

    // A part with its own placement keeps it relative to its setup and moves with it, as a part of any group does.
    static void AMastWithItsOwnPlacementMovesWithTheSetup() {
        var f = new Fixture(); var session = f.Session(out var store, out _);
        var held = new[] { 1090.0, 10, 26 };
        Assert(session.Edit(Mast1Id, new Placement(held, Identity), null).Applied, "A mast takes a placement of its own");
        var moved = session.Edit(ControllerId(), new Placement(Shifted, Quarter), null);
        // 10 m west and 6 m north of the setup turns to 6 m east and 10 m north of the moved setup.
        Assert(moved.Applied && Near(f.At(f.Mast1), new[] { 1116.0, 10, 35 }) && SameTurn(f.Facing(f.Mast1), Quarter) && Near(f.At(f.Gate1), new[] { 1116.0, 13, 35 }), "The mast with its own placement moves and turns with the setup, with its gate: " + moved.Error + " " + f.Where());
        var item = JObject.Parse(session.ScenePatchFileText)["modifications"].Children<JProperty>().Select(p => (JObject)p.Value).Single(m => (string)m["target"]["structure"] == ExistingEquipmentScene.MastStructure);
        Assert(Near(item["placement"]["position"].Select(v => (double)v).ToArray(), held), "The file keeps the mast's placement relative to the setup at its map placement: " + item["placement"]);
        // The other mast at 6 m east and 4 m south turns to 4 m west and 6 m south of the moved setup.
        Assert(Near(f.At(f.Mast2), new[] { 1106.0, 10, 19 }) && SameTurn(f.Facing(f.Mast2), Quarter) && Near(f.At(f.Bell), Shifted) && f.Kept(), "The other mast and the bell move with the setup: " + f.Where());
    }

    // A light or gate the patcher added stands at the fixed placement its addition records, in map coordinates: it
    // keeps that placement when the setup moves, in the scene and in the file, and a failed move returns it too.
    static void AddedEquipmentKeepsItsFixedPlacement() {
        var f = new Fixture(); var session = f.Session(out var store, out var editor);
        var at = new[] { 1108.0, 10, 30 };
        var added = session.AddEquipment(ControllerId(), new EquipmentAddition("extra", EquipmentAddition.LightGatePattern, new EquipmentSource(SceneName, Mast1Path), new Placement(at, Identity)));
        Assert(added.Applied && f.Equipment.Inner.Live(f.Crossing).Count() == 1, "A light is added to the crossing: " + added.Error);
        var extra = f.Equipment.Inner.Live(f.Crossing).Single();
        var moved = session.Edit(ControllerId(), new Placement(Shifted, Quarter), null);
        Assert(moved.Applied && f.Together(Shifted, 90) && f.Kept(), "The setup moves and turns as a whole: " + moved.Error + " " + f.Where());
        Assert(Near(extra.Position, at) && SameTurn(extra.Rotation, Identity) && f.Equipment.Inner.Live(f.Crossing).Single() == extra, "The added light keeps its fixed placement and is not rebuilt: " + N(extra.Position));
        var recorded = session.Equipment(ControllerId()).Additions.Single();
        Assert(Near(recorded.Placement.Position, at) && SameTurn(recorded.Placement.Rotation, Identity), "The file still records that placement: " + N(recorded.Placement.Position));
        editor.Undo();
        Assert(f.Together(Home) && Near(extra.Position, at), "Undo returns the setup and leaves the added light: " + f.Where() + " " + N(extra.Position));
        f.World.SkipCullingRefresh = true;
        var failed = session.Edit(ControllerId(), new Placement(new[] { 1500.0, 10, 20 }, Identity), null);
        Assert(!failed.Applied && f.Together(Home) && f.Kept() && Near(extra.Position, at), "A failed move returns the setup and the added light: " + failed.Error + " " + N(extra.Position));
        // Another mod moves the setup; the light this mod added keeps its placement in a fresh world, whichever applies first.
        Assert(editor.Save() == null, "Save writes the file");
        foreach (var first in new[] { "author/pack", "other/pack" }) {
            var fresh = new Fixture();
            foreach (var mod in first == "author/pack" ? new[] { "author/pack", "other/pack" } : new[] { "other/pack", "author/pack" })
                Assert(fresh.Engine.Apply(mod == "author/pack" ? store.Patch : Doc(Move("theirs", Shifted, Quarter)), mod).Outcomes.Values.All(o => o.Applied), mod + " applies in a fresh world");
            Assert(fresh.Together(Shifted, 90) && Near(fresh.Equipment.Inner.Live(fresh.Crossing).Single().Position, at), "With " + first + " first, the added light stands at its recorded placement beside the moved setup");
        }
    }

    // The crossing's culler follows the setup. A mast with its own placement moves with the setup: inside the culler's
    // sphere it needs no sphere of its own, and outside it its own sphere follows it.
    static void AMovedSetupKeepsItsCulling() {
        var f = new Fixture();
        var far = new[] { 1500.0, 10, 20 };
        Assert(f.Engine.Apply(Doc(Move("far", far))).Outcomes["far"].Applied, "A far move applies");
        Assert(Near(f.Culler.Registered, new[] { 500.0, 10, 20 }) && f.Culler.Radius == 30 && f.Culler.Lists.SequenceEqual(new[] { f.Outer1, f.Outer2 }) && f.World.Native.Registered.Count == 0,
            "The crossing's culler follows the setup with its radius and its list, and no part needs a sphere of its own");
        var g = new Fixture(); var session = g.Session(out _, out _);
        Assert(session.Edit(Mast1Id, new Placement(new[] { 1090.0, 10, 26 }, Identity), null).Applied && g.World.Native.Registered.Count == 0, "A mast held inside the culler's sphere needs no sphere of its own");
        Assert(session.Edit(ControllerId(), new Placement(far, Identity), null).Applied, "The setup moves with the held mast");
        Assert(Near(g.At(g.Mast1), new[] { 1490.0, 10, 26 }) && g.World.Native.Registered.Count == 0, "The held mast moved with the setup and still needs no sphere of its own: " + g.Where());
        var h = new Fixture(); var edits = h.Session(out _, out _);
        Assert(edits.Edit(Mast1Id, new Placement(new[] { 1150.0, 10, 20 }, Identity), null).Applied && h.World.Native.Registered.Count == 1 && h.World.Native.Registered[0].Owner == h.Mast1 && !h.Culler.Lists.Contains(h.Outer1) && h.Culler.Lists.Contains(h.Outer2),
            "A mast held outside the culler's sphere has a culling sphere of its own");
        Assert(edits.Edit(ControllerId(), new Placement(far, Identity), null).Applied && Near(h.At(h.Mast1), new[] { 1550.0, 10, 20 }) && h.World.Native.Registered.Count == 1 && h.World.Native.Registered[0].Owner == h.Mast1,
            "Its own sphere moves with it when the setup moves: " + h.Where());
    }

    static void RefusalsLeaveSceneAndFileUnchanged() {
        var f = new Fixture();
        var following = "\"follow\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + Crossing + "\",\"component\":\"" + CrossingCoverageScene.ComponentType + "\",\"structure\":\"" + CrossingCoverageScene.Structure + "\"}," +
            "\"placement\":{\"mode\":\"trackFollowing\",\"track\":{\"segment\":\"Sold\",\"end\":\"a\",\"distance\":10},\"offset\":[2,0,0],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        // The track resolves, so the refusal is the capability's and not a missing track graph.
        f.Scene.Tracks = new AnyTrack();
        var followed = f.Engine.Apply(Doc(following)).Outcomes["follow"];
        Assert(!followed.Applied && followed.Error.Contains(CrossingCoverageScene.NoFollowing) && f.World.Mutations == 0 && f.Together(Home), "A track-following placement of a setup is refused before mutation: " + followed.Error);
        var disabled = f.Engine.Apply(Doc(Move("off", null, active: false))).Outcomes["off"];
        Assert(!disabled.Applied && disabled.Error.Contains(CrossingCoverageScene.NoActivity) && f.World.Mutations == 0 && f.Controller.Active, "A crossing controller is still not disabled: " + disabled.Error);
        var g = new Fixture(); g.World.Refused = "The game's ground mask members are unavailable, so you can't move, enable, or disable this group or its parts.";
        var session = g.Session(out _, out var editor); var text = session.ScenePatchFileText;
        var refused = session.Edit(ControllerId(), new Placement(Shifted, Identity), null);
        Assert(!refused.Applied && refused.Error.Contains("ground mask members are unavailable") && g.World.Mutations == 0 && session.ScenePatchFileText == text && !editor.CanUndo, "A move the world refuses changes neither scene nor file: " + refused.Error);
    }

    static void AFailureAfterMutationRestoresTheSetup() {
        var cases = new (Action<FakeGroupWorld> inject, string name)[] { (w => w.ThrowAfterPlacement = true, "a throw after the placement"), (w => w.LeavePartBehind = true, "a part left behind"),
            (w => w.ThrowAfterPartPlacement = true, "a throw while a pad is put back"), (w => w.SkipCullingRefresh = true, "a culling refresh that did not happen") };
        foreach (var (inject, name) in cases) {
            var f = new Fixture(); inject(f.World);
            var outcome = f.Engine.Apply(Doc(Move("move-setup", new[] { 1500.0, 10, 20 }, Quarter))).Outcomes["move-setup"];
            Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal), name + ": a failure after mutation is restored: " + outcome.Error);
            Assert(Near(f.Root(), Home) && f.Together(Home) && f.Kept(), name + ": the setup stands as captured: " + f.Where());
            Assert(f.World.Native.Registered.Count == 0 && f.Culler.Lists.SequenceEqual(new[] { f.Outer1, f.Outer2 }) && Near(f.Culler.Registered, new[] { 100.0, 10, 20 }), name + ": the culling membership and registration are as before");
            Assert(f.Triggers("Sold", 300) && f.Crossings.Inner.Mutations == 0, name + ": the detection is untouched");
        }
        var kept = new Fixture(); kept.World.LeavePartBehind = kept.World.KeepPartBehind = true;
        var stuck = kept.Engine.Apply(Doc(Move("move-setup", Shifted))).Outcomes["move-setup"];
        Assert(!stuck.Applied && stuck.Error.Contains("could not be verified"), "An incomplete cleanup is reported explicitly: " + stuck.Error);
    }

    static void HistorySaveReloadAndRemoval() {
        var f = new Fixture(); var session = f.Session(out var store, out var editor);
        Assert(session.Edit(ControllerId(), new Placement(Shifted, Identity), null).Applied && session.Edit(ControllerId(), new Placement(Shifted, Quarter), null).Applied, "A move and a rotation apply");
        editor.Undo(); Assert(f.Together(Shifted) && f.Kept(), "Undo returns the rotation: " + f.Where());
        editor.Undo(); Assert(Near(f.Root(), Home) && f.Together(Home) && f.Kept(), "Undo returns the move: " + f.Where());
        editor.Redo(); editor.Redo(); Assert(f.Together(Shifted, 90) && f.Kept(), "Redo reapplies both: " + f.Where());
        Assert(editor.Save() == null, "Save writes the file");
        var item = (JObject)JObject.Parse(store.Patch)["modifications"].Children<JProperty>().Single().Value;
        Assert((string)item["target"]["structure"] == CrossingCoverageScene.Structure && (string)item["target"]["path"] == Crossing && (string)item["placement"]["mode"] == "fixed" && item["active"] == null && item["relationships"] == null
            && Near(item["placement"]["position"].Select(v => (double)v).ToArray(), Shifted), "The file records the controller's fixed placement and nothing else: " + item.ToString(Newtonsoft.Json.Formatting.None));
        var fresh = new Fixture();
        var reload = fresh.Engine.Apply(store.Patch, "author/pack");
        Assert(reload.Error == null && reload.Outcomes.Values.All(o => o.Applied) && fresh.Together(Shifted, 90) && fresh.Kept() && fresh.Triggers("Sold", 300), "A reloaded world reproduces the whole move: " + fresh.Where());
        var first = fresh.Where();
        Assert(fresh.Engine.Apply(store.Patch, "author/pack").Outcomes.Values.All(o => o.Applied) && fresh.Where() == first && fresh.Controller.Parts.Count == 12, "Repeated application adds no offset and no object: " + fresh.Where());
        fresh.World.Origin = 1010;
        Assert(new PatchEngine(Later(fresh)).Apply(store.Patch, "author/pack").Outcomes.Values.All(o => o.Applied) && Near(fresh.Root(), Shifted), "A load at another origin reaches the same map placement: " + fresh.Where());
        var removed = session.Remove(ControllerId());
        Assert(removed.Applied && Near(f.Root(), Home) && f.Together(Home) && f.Kept() && f.Triggers("Sold", 300), "Remove modification returns the setup to the placement the scene gives it: " + removed.Error + " " + f.Where());
    }
    // A second state over the same world, as a later load attempt builds it.
    static SceneCapabilities Later(Fixture f) {
        var scene = new SceneCapabilities(() => f.World.Maps);
        scene.Add(ExistingEquipmentScene.Masts(f.World, f.Holds));
        scene.Add(new CrossingCoverageScene(f.Crossings, Capability.Unchecked, f.Equipment, new WholeMove(f.World, f.Holds)));
        return scene;
    }

    // A duplicated crossing's setup moves and rotates as a whole in the same way: the copy's masts, gate and bell move
    // together, its pads stay where they stood, and the crossing it was made from is untouched.
    static void ADuplicatedCrossingMovesAndRotatesAsAWhole() {
        var f = new Fixture(); var session = f.Session(out var store, out var editor);
        var built = new[] { 5200.0, 14, 5900 }; var moved = new[] { 5230.0, 14, 5910 };
        var placed = session.PlaceModObject(ControllerId(), new Placement(built, Identity));
        Assert(placed.Applied && ModObjects.IsPath(placed.Target.Path), "The crossing is duplicated: " + placed.Error);
        var copy = f.Copy(placed.Target);
        Assert(copy != null && f.Together(built, 0, copy) && f.Kept(built, copy), "The duplicate stands where it was placed: " + f.Where(copy));
        var move = session.Edit(placed.Target, new Placement(moved, Identity), null);
        Assert(move.Applied && Near(f.Root(copy), moved) && f.Together(moved, 0, copy), "A move of the duplicate through its controller moves its masts, gate and bell together: " + move.Error + " " + f.Where(copy));
        Assert(f.Kept(built, copy), "The duplicate's pads stay where they were: " + f.Where(copy));
        var turn = session.Edit(placed.Target, new Placement(moved, Quarter), null);
        Assert(turn.Applied && f.Together(moved, 90, copy) && f.Kept(built, copy), "A rotation turns them together about the duplicate: " + turn.Error + " " + f.Where(copy));
        Assert(Near(f.Root(), Home) && f.Together(Home) && f.Kept() && f.Triggers("Sold", 300), "The crossing it was made from is untouched: " + f.Where());
        editor.Undo(); editor.Undo();
        Assert(f.Together(built, 0, copy) && f.Kept(built, copy), "Undo returns the duplicate to where it was placed: " + f.Where(copy));
        editor.Redo(); editor.Redo();
        var item = ((JObject)JObject.Parse(session.ScenePatchFileText)["modifications"]).Properties().Select(p => (JObject)p.Value).Single(v => (string)v["target"]["path"] == placed.Target.Path);
        Assert((string)item["source"]["path"] == Crossing && Near(item["placement"]["position"].Select(v => (double)v).ToArray(), moved), "The duplicate's modification holds its new placement: " + item.ToString(Newtonsoft.Json.Formatting.None));
        Assert(editor.Save() == null, "Save writes the file");
        var fresh = new Fixture();
        var reload = fresh.Engine.Apply(store.Patch, "author/pack");
        var loaded = fresh.Copy(placed.Target);
        Assert(reload.Error == null && reload.Outcomes.Values.All(o => o.Applied) && loaded != null && fresh.Together(moved, 90, loaded) && fresh.Together(Home), "A fresh load builds the duplicate with its masts, gate and bell at the moved placement: " + (loaded == null ? "none" : fresh.Where(loaded)));
    }

    // The Placement tab of a crossing controller offers Move and Rotate, by every placement mode but Follow; its
    // activity stays out, with its own reason.
    static void ThePlacementTabOffersTheWholeMove() {
        var entry = new SceneObservation(new object(), ControllerId(), SceneFamilies.CrossingController, true, Home, Identity, new string[0], new string[0], new string[0], CommandBand.CrossingControllerOperations);
        var edits = SupportedEdits.Of(entry);
        Assert(SupportedEdits.ReasonOf(edits, SupportedEdits.Move) == null && SupportedEdits.ReasonOf(edits, SupportedEdits.Rotate) == null, "Move and Rotate are available for a crossing controller");
        Assert(SupportedEdits.ReasonOf(edits, SupportedEdits.Follow) == EditReasons.Short(CommandBand.SetupNoFollow) && SupportedEdits.ReasonOf(edits, SupportedEdits.Activity) == EditReasons.Short(SupportedEdits.CrossingNoActivity),
            "Follow a track and the activity give their reasons: " + SupportedEdits.ReasonOf(edits, SupportedEdits.Follow) + " / " + SupportedEdits.ReasonOf(edits, SupportedEdits.Activity));
        foreach (var mode in new[] { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode })
            Assert(SupportedEdits.ModeBlock(edits, mode, true) == null, "Placement mode " + mode + " is open for a crossing controller");
        Assert(SupportedEdits.ModeBlock(edits, CommandBand.FollowMode, true) != null, "Follow stays closed");
        Assert(SupportedEdits.Lines(edits)[0] == "Available: Move, Rotate, Edit crossing coverage, Edit crossing equipment", "Supported edits lists the whole move: " + String.Join(" | ", SupportedEdits.Lines(edits)));
        Assert(GuidedTaskState.TargetBlock(GuidedTaskKind.Placement, ControllerId()) == null && GuidedTaskState.TargetBlock(GuidedTaskKind.Snap, ControllerId()) == null, "The guided placement tasks take a crossing controller");
        Assert(GuidedTaskState.TargetBlock(GuidedTaskKind.Activity, ControllerId()) == SupportedEdits.CrossingNoActivity, "The guided activity task gives the controller's reason");
        // The window reads from the capability whether it holds the whole move, and gives the reason where it does not.
        var f = new Fixture();
        Assert(new CrossingCoverageScene(f.Crossings, Capability.Unchecked, null, new WholeMove(f.World, f.Holds)).MovesSetup && !new CrossingCoverageScene(f.Crossings, Capability.Unchecked).MovesSetup, "The capability says whether it moves a setup");
        var without = SupportedEdits.Of(entry, new SupportedEdits.Live { WholeMoveReason = EditReasons.GameBuild });
        Assert(SupportedEdits.ReasonOf(without, SupportedEdits.Move) == EditReasons.GameBuild && SupportedEdits.ReasonOf(without, SupportedEdits.Rotate) == EditReasons.GameBuild, "Without the whole move, Move and Rotate give the game build reason");
        Assert(CommandBand.CrossingControllerOperations.StartsWith("Move or rotate the whole grade-crossing setup.", StringComparison.Ordinal), "The controller's supported operations start with the whole move: " + CommandBand.CrossingControllerOperations);
    }
}
