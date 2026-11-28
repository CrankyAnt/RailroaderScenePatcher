// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Whole scene patch file tests for the whole move of a group that holds track markers, against the speed sign groups of
// the base map fixture: Bryson Signs holds the speed sign Speed Sign (2) and the yard limits Yard Limit and Yard Limit (1),
// each a Generic track marker. A placement of the group moves its parts and its markers together, each keeping its
// placement relative to the others; every marker keeps the track binding and the registration it had.
static class MarkerGroupTests {
    const string SceneName = "BushnellWhittier", Group = "World/Large Scenery/Bryson/Bryson Signs";
    const string SpeedPath = Group + "/Speed Sign (2)", YardPath = Group + "/Yard Limit", Yard1Path = Group + "/Yard Limit (1)";
    static readonly string[] SignGroups = { Group, "World/Large Scenery/Ela/Signs", "World/Large Scenery/Sylva/Sylva Signs" };
    static void Assert(bool value, string message) { if (!value) throw new Exception("Marker groups: " + message); }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static readonly double[] Identity = { 0, 0, 0, 1 }, Quarter = Turn.Yaw(90);
    // Where the group stands in the base map, and where each test moves it.
    static readonly double[] Home = { 4400, 10, 5345 }, Shifted = { 4412, 11, 5350 };
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b, double tolerance = 0.001) => a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= tolerance);
    static bool SameTurn(IReadOnlyList<double> a, IReadOnlyList<double> b) => Math.Abs(Math.Abs(a.Zip(b, (x, y) => x * y).Sum()) - 1) < 1e-6;
    static TargetIdentity GroupId() => new TargetIdentity(SceneName, Group, SceneObjects.ComponentType, SceneObjects.Structure);
    static TargetIdentity MarkerId(string path) => new TargetIdentity(SceneName, path, "Track.TrackMarker", TrackMarkerScene.Structure);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static string Move(string id, double[] position, double[] rotation = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + Group + "\",\"component\":\"" + SceneObjects.ComponentType + "\",\"structure\":\"" + SceneObjects.Structure + "\"}" +
        ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", position.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[" + String.Join(",", (rotation ?? Identity).Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]},\"dependsOn\":[]}";

    // The group world of the base map. A track marker below a group is a transform below its root, as in the game: a
    // placement of the root carries it, and nothing else about it changes. Its serialized location and its
    // registration are the marker world's.
    sealed class Groups : FakeGroupWorld, ITrackMarkerGroupWorld {
        public readonly Dictionary<FakeGroup, List<FakeMarker>> MarkersBelow = new Dictionary<FakeGroup, List<FakeMarker>>();
        public object MarkerGroup(object target) => MarkersBelow.FirstOrDefault(pair => pair.Value.Contains(target as FakeMarker)).Key;
        public Placement MarkerParentPlacement(object target) => PlacementOf(MarkerGroup(target));
        public object FrameAbove(object target) => null;
        public object ModObjectOf(object target) => null;
        public Placement PlacementOf(object target) => target is FakeMarker marker
            ? new Placement(new[] { marker.Position[0] + Origin, marker.Position[1], marker.Position[2] }, marker.Rotation)
            : Observe(target).Root.Placement;
        public new void SetPartPlacement(object target, Placement placement) {
            if (!(target is FakeMarker marker)) { base.SetPartPlacement(target, placement); return; }
            marker.Position = new[] { placement.Position[0] - Origin, placement.Position[1], placement.Position[2] };
            marker.Rotation = placement.Rotation;
        }
        // A marker stands below its group, as a transform below the root does.
        public new bool Below(object target, object above) => target is FakeMarker marker ? above is FakeGroup group && MarkersBelow.TryGetValue(group, out var markers) && markers.Contains(marker) : base.Below(target, above);
        public new bool Alive(object handle) => handle is FakeMarker marker ? MarkersBelow.Values.Any(markers => markers.Contains(marker)) : base.Alive(handle);
        public new void SetPlacement(object target, Placement placement) {
            var group = (FakeGroup)target;
            var before = Observe(group).Root.Placement;
            base.SetPlacement(target, placement);
            var after = Observe(group).Root.Placement;
            if (!MarkersBelow.TryGetValue(group, out var markers)) return;
            var undo = Turn.Inverse(before.Rotation);
            foreach (var marker in markers) {
                var offset = Turn.Rotate(undo, new[] { marker.Position[0] + Origin - before.Position[0], marker.Position[1] - before.Position[1], marker.Position[2] - before.Position[2] });
                var turned = Turn.Rotate(after.Rotation, offset);
                marker.Position = new[] { after.Position[0] + turned[0] - Origin, after.Position[1] + turned[1], after.Position[2] + turned[2] };
                marker.Rotation = Turn.Multiply(after.Rotation, Turn.Multiply(undo, marker.Rotation));
            }
        }
    }

    // Bryson Signs as the base map fixture records it, with its group verdict from the generic rules: the root, one part
    // of its own beside the markers, and the three markers below it, each bound to the track.
    sealed class Fixture {
        public readonly BaseMapFixture Map = new BaseMapFixture();
        public readonly Groups World = new Groups();
        public readonly FakeMarkerWorld Markers = new FakeMarkerWorld();
        public readonly FakeGroup Signs = new FakeGroup { RootCulling = null };
        public readonly FakePart Post;
        public readonly FakeMarker Speed, Yard, Yard1;
        public readonly PatchEngine Engine;
        public readonly SceneCapabilities Scene;
        // The objects whose place their track sets, which the marker and group capabilities share, and the track the
        // markers can follow.
        public readonly TrackPlacedObjects Placed = new TrackPlacedObjects();
        public readonly TrackScene Tracks = new TrackScene();
        public Fixture() {
            World.GroupVerdicts = Map.Verdicts.Where(v => v.Path != null).GroupBy(v => v.Path).ToDictionary(g => g.Key, g => g.First());
            Signs.Position = new[] { Home[0] - World.Origin, Home[1], Home[2] };
            Signs.Parts.Clear(); Signs.Cullers.Clear();
            Post = Signs.Add(new FakePart { Id = "Sign Post#1", Offset = new[] { 2.0, 0, -1 } });
            Speed = Marker(SpeedPath, "Mspd", "S9sc", "a", 120, new[] { 923.0, 0, 391 }, Turn.Yaw(30));
            Yard = Marker(YardPath, "Myrd", "Sjdl", "b", 38.6, new[] { 557.0, 0, 244 }, Identity);
            Yard1 = Marker(Yard1Path, "Myr1", "S9sc", "b", 10, new[] { -496.0, 0, -379 }, Turn.Yaw(-15));
            World.Objects[Group] = new List<object> { Signs };
            World.MarkersBelow[Signs] = new List<FakeMarker> { Speed, Yard, Yard1 };
            World.SetActive(Signs, true); World.Mutations = 0; Markers.Mutations = 0;
            Scene = Capabilities();
            Engine = new PatchEngine(Scene);
        }
        // A marker at its place relative to the group's root, bound and registered.
        FakeMarker Marker(string path, string id, string segment, string end, double distance, double[] offset, double[] rotation) {
            var marker = new FakeMarker { Id = id, Segment = segment, End = end, Distance = distance, Position = new[] { Signs.Position[0] + offset[0], Signs.Position[1] + offset[1], Signs.Position[2] + offset[2] }, Rotation = rotation };
            Markers.Objects[path] = new List<object> { marker }; Markers.Load(marker); Markers.Relative(marker);
            return marker;
        }
        // The capabilities of one load attempt over this world.
        public SceneCapabilities Capabilities() {
            var scene = new SceneCapabilities(() => World.Maps) { Tracks = Tracks };
            var holds = new PartHolds();
            scene.Add(new SceneObjects(World, holds: holds, placed: Placed));
            var markers = new TrackMarkerScene(Markers, Capability.Unchecked, placed: Placed);
            markers.UseGroups(World, holds);
            scene.Add(markers);
            return scene;
        }
        public double[] Root() => World.Observe(Signs).Root.Placement.Position;
        public double[] At(FakeMarker marker) => Markers.Position(marker);
        public double[] At(FakePart part) => World.ObservePart(part).Part.Placement.Position;
        // Each binding with its registration, as one comparable value.
        public string Bindings() => String.Join("; ", new[] { Speed, Yard, Yard1 }.Select(m => m.Id + "=" + m.Segment + "|" + m.End + "|" + m.Distance.ToString("R", CultureInfo.InvariantCulture)
            + " registered " + String.Join(",", Markers.Registrations(m).Select(r => r.Bucket + "|" + r.Location.End + "|" + r.Location.Distance.ToString("R", CultureInfo.InvariantCulture))) + " cache " + m.CachedGroup));
        public string Where() => "root=" + N(Root()) + "; post=" + N(At(Post)) + "; " + String.Join("; ", new[] { Speed, Yard, Yard1 }.Select(m => m.Id + "=" + N(At(m))));
        // Whether the part and the markers stand around the root as the scene has them, turned by the yaw; except names a
        // marker that is not compared.
        public bool Together(double[] root, double degrees = 0, FakeMarker except = null) {
            var turn = Turn.Yaw(degrees);
            bool Stands(IReadOnlyList<double> at, IReadOnlyList<double> facing, double[] offset, double[] rotation) {
                var t = Turn.Rotate(turn, offset);
                return Near(at, new[] { root[0] + t[0], root[1] + t[1], root[2] + t[2] }) && SameTurn(facing, Turn.Multiply(turn, rotation));
            }
            return Stands(At(Post), World.ObservePart(Post).Part.Placement.Rotation, new[] { 2.0, 0, -1 }, Identity)
                && (except == Speed || Stands(At(Speed), Speed.Rotation, new[] { 923.0, 0, 391 }, Turn.Yaw(30)))
                && (except == Yard || Stands(At(Yard), Yard.Rotation, new[] { 557.0, 0, 244 }, Identity))
                && (except == Yard1 || Stands(At(Yard1), Yard1.Rotation, new[] { -496.0, 0, -379 }, Turn.Yaw(-15)));
        }
        public PatchEditingSession Session(out Store store, out NativeMapEditor editor) {
            store = new Store(); editor = new NativeMapEditor();
            var session = new PatchEditingSession(Engine, editor, store, LoadedMod.PatcherId, "author/pack"); editor.Session = session;
            return session;
        }
    }
    const string Bound = "Mspd=S9sc|a|120 registered S9sc|a|120 cache ela; Myrd=Sjdl|b|38.6 registered Sjdl|b|38.6 cache alarka; Myr1=S9sc|b|10 registered S9sc|b|10 cache ela";

    internal static void Run() {
        SpeedSignGroupsAreGroupsOfTheFamily();
        MovingTheGroupMovesItsMarkersAndPartsTogether();
        EachMarkerKeepsItsTrackBinding();
        RotatingTheGroupTurnsItsPartsAboutTheGroup();
        HistorySaveReloadAndRemoval();
        FixedMarkerPlacementSurvivesSaveAndReload();
        AnInactiveMarkerKeepsItsOwnOrbit();
        AMarkerStaysEditableInItsOwnFamily();
        AMarkerOnARailBridgeIsNoTrackOwnedComponent();
        TheGroupsActivityStaysRefused();
        AMarkerThatFollowsItsTrackStaysOnItsTrackPoint();
        Console.WriteLine("PASS: marker groups");
    }

    // The base map's sign groups that hold track markers are groups of the Scene object family: no longer inspection
    // only, and each marker below them stays an object of its own family, never a part of the group.
    static void SpeedSignGroupsAreGroupsOfTheFamily() {
        var map = new BaseMapFixture();
        foreach (var path in SignGroups) {
            var verdict = map.Verdicts.FirstOrDefault(v => v.Path == path);
            Assert(verdict != null && verdict.Disposition == GroupDisposition.Group && verdict.Reason == null, path + " is a group of the family: " + verdict?.Disposition + " " + verdict?.Reason);
        }
        var parts = SceneObjectRules.Parts(map.Verdicts);
        foreach (var path in new[] { SpeedPath, YardPath, Yard1Path, "World/Large Scenery/Ela/Signs/Station Sign", "World/Large Scenery/Sylva/Sylva Signs/Yard Limit Trackside" }) {
            var part = parts.FirstOrDefault(p => p.Path == path);
            Assert(part != null && part.Disposition == PartDisposition.ExistingFamily, path + " stays in its own family: " + part?.Disposition);
        }
        Assert(!SceneObjectRules.Exclusions.Any(e => e.Component == "Track.TrackMarker"), "A track marker no longer keeps its group from moving as a whole");
        // No group that now moves as a whole holds a crossing pad, whose place its track binding sets.
        var moved = map.Verdicts.Where(v => v.Disposition == GroupDisposition.Group && !v.Crossbuck).ToArray();
        bool HoldsPad(IHierarchyNode node) => node.Components.Contains(SceneObjectRules.PadBuilder) || node.Children.Any(HoldsPad);
        Assert(!moved.Any(v => HoldsPad(v.Node)), "No base map group holds a crossing pad: " + String.Join(", ", moved.Where(v => HoldsPad(v.Node)).Select(v => v.Path)));
    }

    // A placement of the group moves the root, its part and its markers together, each keeping its placement relative to
    // the others.
    static void MovingTheGroupMovesItsMarkersAndPartsTogether() {
        var f = new Fixture();
        Assert(f.Together(Home), "The fixture stands at home: " + f.Where());
        var result = f.Engine.Apply(Doc(Move("move", Shifted))).Outcomes["move"];
        Assert(result.Applied, "The group takes the placement: " + result.Error);
        Assert(Near(f.Root(), Shifted) && f.Together(Shifted), "The part and the markers moved with the root and kept their placement relative to each other: " + f.Where());
        var again = f.Where();
        Assert(f.Engine.Apply(Doc(Move("move", Shifted))).Outcomes["move"].Applied && f.Where() == again, "Repeated application adds no offset: " + f.Where());
    }

    // After the move, each marker keeps the track binding it had: its serialized location, its registration in the
    // graph and the track group its map label caches. The group move writes nothing to the marker world.
    static void EachMarkerKeepsItsTrackBinding() {
        var f = new Fixture();
        Assert(f.Bindings() == Bound, "The fixture's markers are bound and registered: " + f.Bindings());
        Assert(f.Engine.Apply(Doc(Move("move", Shifted, Quarter))).Outcomes["move"].Applied, "The group moves and turns");
        Assert(f.Bindings() == Bound && f.Markers.Mutations == 0, "Every marker keeps its binding, registration and cache, and nothing wrote to them: " + f.Bindings() + " (" + f.Markers.Mutations + " writes)");
        foreach (var path in new[] { SpeedPath, YardPath, Yard1Path }) {
            var state = f.Scene.Read(f.Scene.Resolve(MarkerId(path)).Single());
            Assert(state.Marker != null && state.Marker.Location != null, path + " reads its binding after the move");
        }
    }

    // A rotation of the group turns its part and its markers together about the group's root.
    static void RotatingTheGroupTurnsItsPartsAboutTheGroup() {
        var f = new Fixture();
        var result = f.Engine.Apply(Doc(Move("turn", Home, Quarter))).Outcomes["turn"];
        Assert(result.Applied && Near(f.Root(), Home) && f.Together(Home, 90), "A quarter turn turns the part and the markers about the root: " + result.Error + " " + f.Where());
        Assert(f.Bindings() == Bound, "The turn keeps every binding: " + f.Bindings());
    }

    // The editing session's Undo, Redo, Save, a fresh load and Remove modification carry the whole move.
    static void HistorySaveReloadAndRemoval() {
        var f = new Fixture(); var session = f.Session(out var store, out var editor);
        Assert(session.Edit(GroupId(), new Placement(Shifted, Identity), null).Applied && session.Edit(GroupId(), new Placement(Shifted, Quarter), null).Applied, "A move and a rotation apply");
        editor.Undo(); Assert(f.Together(Shifted), "Undo returns the rotation: " + f.Where());
        editor.Undo(); Assert(Near(f.Root(), Home) && f.Together(Home), "Undo returns the move: " + f.Where());
        editor.Redo(); editor.Redo(); Assert(f.Together(Shifted, 90) && f.Bindings() == Bound, "Redo reapplies both, and the bindings stay: " + f.Where());
        Assert(editor.Save() == null, "Save writes the file");
        var item = (JObject)JObject.Parse(store.Patch)["modifications"].Children<JProperty>().Single().Value;
        Assert((string)item["target"]["structure"] == SceneObjects.Structure && (string)item["target"]["path"] == Group && (string)item["placement"]["mode"] == "fixed" && item["relationships"] == null
            && Near(item["placement"]["position"].Select(v => (double)v).ToArray(), Shifted), "The file records the group's fixed placement and no marker relationship: " + item.ToString(Newtonsoft.Json.Formatting.None));
        var fresh = new Fixture();
        var reload = fresh.Engine.Apply(store.Patch, "author/pack");
        Assert(reload.Error == null && reload.Outcomes.Values.All(o => o.Applied) && fresh.Together(Shifted, 90) && fresh.Bindings() == Bound, "A reloaded world reproduces the whole move with the bindings: " + fresh.Where());
        Assert(new PatchEngine(fresh.Capabilities()).Apply(store.Patch, "author/pack").Outcomes.Values.All(o => o.Applied) && fresh.Together(Shifted, 90), "A later load attempt keeps the same layout: " + fresh.Where());
        var removed = session.Remove(GroupId());
        Assert(removed.Applied && Near(f.Root(), Home) && f.Together(Home) && f.Bindings() == Bound, "Remove modification returns the group with its markers to the scene's placement: " + removed.Error + " " + f.Where());
    }

    static void FixedMarkerPlacementSurvivesSaveAndReload() {
        foreach (var partFirst in new[] { true, false }) {
            var f = new Fixture();
            var session = f.Session(out var store, out var editor);
            var marker = MarkerId(SpeedPath);
            var atHome = new Placement(new[] { 5325.0, 10, 5736 }, Turn.Yaw(30));
            var afterMove = new Placement(new[] { 4803.0, 11, 4425 }, Turn.Yaw(120));
            if (partFirst) Assert(session.Edit(marker, atHome, null).Applied, "The speed sign takes its own placement");
            Assert(session.Edit(GroupId(), new Placement(Shifted, Quarter), null).Applied, "The group moves and turns");
            if (!partFirst) Assert(session.Edit(marker, afterMove, null).Applied, "The speed sign can be placed in a group that has already moved");
            Assert(Near(f.At(f.Speed), afterMove.Position) && SameTurn(f.Speed.Rotation, afterMove.Rotation)
                && f.Together(Shifted, 90, f.Speed), "The speed sign and its siblings keep their respective placements: " + f.Where());
            Assert(editor.Save() == null, "Save writes both placements");
            var modifications = (JObject)JObject.Parse(store.Patch)["modifications"];
            var speed = modifications.Properties().Single(p => (string)p.Value["target"]["path"] == SpeedPath);
            var group = modifications.Properties().Single(p => (string)p.Value["target"]["path"] == Group);

            var fresh = new Fixture();
            var reload = fresh.Engine.Apply(store.Patch, "author/pack");
            Assert(reload.Error == null && reload.Outcomes.Values.All(o => o.Applied)
                && Near(fresh.At(fresh.Speed), afterMove.Position) && SameTurn(fresh.Speed.Rotation, afterMove.Rotation)
                && fresh.Together(Shifted, 90, fresh.Speed) && fresh.Bindings() == Bound,
                "A fresh load restores the fixed sign relative to its moved group: " + fresh.Where());
            Assert(Near(speed.Value["placement"]["position"].Values<double>().ToArray(), atHome.Position)
                && speed.Value["dependsOn"].Values<string>().SequenceEqual(new[] { group.Name }),
                "The file records the sign at the group's map placement and makes it depend on the group move");
            var unscheduledMarker = (JObject)speed.Value.DeepClone();
            unscheduledMarker["dependsOn"] = new JArray();
            var separate = new Fixture();
            var first = separate.Engine.Apply(Doc("\"marker\":" + unscheduledMarker), "author/sign");
            var second = separate.Engine.Apply(Doc("\"group\":" + group.Value), "author/group");
            Assert(first.Outcomes["marker"].Applied && second.Outcomes["group"].Applied
                && Near(separate.At(separate.Speed), afterMove.Position) && SameTurn(separate.Speed.Rotation, afterMove.Rotation),
                "Applying the sign before the group in separate files keeps the same relative placement: " + separate.Where());
            Assert(session.Remove(GroupId()).Applied && Near(f.At(f.Speed), atHome.Position), "Removing the group move does not remove the sign's own placement");
            editor.Undo();
            Assert(Near(f.At(f.Speed), afterMove.Position), "Undo restores the group move and carries the sign again");
        }
    }

    static void AnInactiveMarkerKeepsItsOwnOrbit() {
        Fixture WithInactiveYardLimit() {
            var fixture = new Fixture();
            fixture.Markers.SetActive(fixture.Yard, false);
            // X and Z from the inactive Yard Limit's transform below Bryson Signs in the base map.
            fixture.Yard.Position[0] = 4957.21923828125 - fixture.World.Origin;
            fixture.Yard.Position[2] = 5588.79345703125;
            return fixture;
        }
        var f = WithInactiveYardLimit();
        var session = f.Session(out var store, out var editor);
        Assert(session.Edit(MarkerId(SpeedPath), new Placement(new[] { 5325.0, 10, 5736 }, Turn.Yaw(30)), null).Applied,
            "The speed sign takes an independent two-metre offset");
        Assert(session.Edit(GroupId(), new Placement(new[] { 4405.0, 10, 5345 }, Identity), null).Applied
            && session.Edit(GroupId(), new Placement(new[] { 4405.0, 10, 5345 }, Quarter), null).Applied,
            "The group moves five metres and rotates by a quarter turn");
        var expected = new[] { 4648.79345703125, 10, 4787.78076171875 };
        Assert(!f.Yard.Active && Near(f.At(f.Yard), expected) && SameTurn(f.Yard.Rotation, Quarter),
            "The inactive yard limit rotates by its own offset: " + f.Where());
        Assert(editor.Save() == null, "Save writes the group and speed sign placements");
        var fresh = WithInactiveYardLimit();
        Assert(fresh.Engine.Apply(store.Patch, "author/pack").Outcomes.Values.All(outcome => outcome.Applied)
            && !fresh.Yard.Active && Near(fresh.At(fresh.Yard), expected) && SameTurn(fresh.Yard.Rotation, Quarter),
            "Reload retains the inactive yard limit's orbit: " + fresh.Where());
    }

    // After the group moved, a marker below it still takes its own family's edits: a rebinding changes only its binding,
    // and the group stays where it was moved.
    static void AMarkerStaysEditableInItsOwnFamily() {
        var f = new Fixture(); var session = f.Session(out _, out _);
        Assert(session.Edit(GroupId(), new Placement(Shifted, Identity), null).Applied, "The group moves");
        var at = f.At(f.Yard);
        var bind = session.Bind(MarkerId(YardPath), MarkerBinding.At(new TrackLocation("Sjdl", "a", 12)));
        Assert(bind.Applied && f.Yard.Segment == "Sjdl" && f.Yard.End == "a" && f.Yard.Distance == 12 && Near(f.At(f.Yard), at), "A rebinding of a marker in the moved group changes only its binding: " + bind.Error);
        Assert(Near(f.Root(), Shifted) && f.Speed.Distance == 120 && f.Yard1.Distance == 10, "The group and the other markers are unchanged: " + f.Where());
    }

    // A track marker on a rail bridge, such as a speed sign, moves with the bridge and keeps its binding: it is no
    // component of the native track, unlike a track segment or a generated trestle, which keep the bridge from moving.
    static void AMarkerOnARailBridgeIsNoTrackOwnedComponent() {
        var bridge = new Node("World", new Node("Large Scenery", new Node("Town", new Node("Bridge 9", new Node("Deck").Solid(), new Node("Speed Sign").With("Track.TrackMarker", "Track.TrackRelativePosition").Solid().Family()))));
        var verdict = SceneObjectRules.Classify(bridge).Single(v => v.Path == "World/Large Scenery/Town/Bridge 9");
        Assert(verdict.Disposition == GroupDisposition.Group && verdict.Bridge == BridgeKind.Rail, "A rail bridge with a speed sign is a group: " + verdict.Disposition + " " + verdict.Reason);
        Assert(SceneObjectRules.TrackOwnedRefusal(new[] { "UnityEngine.MeshRenderer", "Track.TrackMarker", "Track.TrackRelativePosition", "Helpers.MilePost" }) == null, "A track marker, its relative position and a milepost are no track-owned components");
        Assert(SceneObjectRules.TrackOwnedRefusal(new[] { "Track.TrackMarker", "Track.TrackSegment" }) != null && SceneObjectRules.TrackOwnedRefusal(new[] { "Track.TrackMarker", "Track.TrackCurveMeshBuilder" }) != null,
            "A track segment or a pad's mesh builder still refuses the bridge");
    }

    // A group that holds track markers moves and rotates as a whole, but its activity stays refused, as it was while the
    // group was inspection only: disabling it would take its markers off the track. A file's enable or disable and the
    // window's Disable are refused before anything changes; a part with a marker below it is held the same way.
    static void TheGroupsActivityStaysRefused() {
        var map = new BaseMapFixture();
        foreach (var path in SignGroups) Assert(map.Verdicts.First(v => v.Path == path).HoldsTrackMarkers, path + " holds track markers");
        Assert(!map.Verdicts.Any(v => v.Disposition == GroupDisposition.Group && v.HoldsTrackMarkers && !SignGroups.Contains(v.Path)) && map.Verdicts.Any(v => v.Disposition == GroupDisposition.Group && !v.HoldsTrackMarkers),
            "Only the sign groups hold track markers among the base map's groups: " + String.Join(", ", map.Verdicts.Where(v => v.Disposition == GroupDisposition.Group && v.HoldsTrackMarkers).Select(v => v.Path)));
        var part = new Node("Frame", new Node("Speed Sign").With("Track.TrackMarker").Solid().Family()).Solid();
        Assert(SceneObjectRules.HoldsTrackMarker(part) && !SceneObjectRules.HoldsTrackMarker(new Node("Post").Solid()), "A part with a marker below it holds a track marker; a plain part doesn't");
        foreach (var active in new[] { false, true }) {
            var f = new Fixture();
            var item = Move("activity", Shifted).Replace(",\"dependsOn\"", ",\"active\":" + (active ? "true" : "false") + ",\"dependsOn\"");
            var result = f.Engine.Apply(Doc(item)).Outcomes["activity"];
            Assert(!result.Applied && result.Error.Contains(SceneObjectRules.MarkerActivityRefusal), "An active value of " + active + " is refused with the reason: " + result.Error);
            Assert(Near(f.Root(), Home) && f.Together(Home) && f.Signs.Active && f.Bindings() == Bound && f.World.Mutations == 0 && f.Markers.Mutations == 0, "The refusal changes nothing: " + f.Where());
        }
        var g = new Fixture(); var session = g.Session(out var store, out _);
        var disable = session.Edit(GroupId(), null, false);
        Assert(!disable.Applied && g.Signs.Active && g.Bindings() == Bound && store.Patch == null, "The window's Disable is refused and writes nothing: " + disable.Error);
        Assert(session.Edit(GroupId(), new Placement(Shifted, Identity), null).Applied && g.Signs.Active, "A move without an activity still applies");
    }

    // A marker that follows its track stands where its track sets it, like a crossing pad: when its group moves or turns,
    // it stays on that track point and the rest of the group moves. This holds through Undo, Redo, a fresh load in either
    // key order, and a later move of the track, which carries the marker along that track.
    static void AMarkerThatFollowsItsTrackStaysOnItsTrackPoint() {
        var f = new Fixture(); var session = f.Session(out var store, out var editor);
        var anchor = TrackAnchor.At(new TrackLocation("Sitz", "a", 0));
        f.Tracks.Poses["Sitz|a|0"] = new TrackPose(new[] { 3920.16, 529, 5015.7 }, Identity);
        var followed = session.Follow(MarkerId(YardPath), anchor);
        Assert(followed.Applied && f.Placed.Contains(f.Yard), "The yard limit follows its track: " + followed.Error);
        var onTrack = f.At(f.Yard); var facing = (double[])f.Yard.Rotation.Clone();
        var moved = session.Edit(GroupId(), new Placement(Shifted, Quarter), null);
        Assert(moved.Applied && Near(f.Root(), Shifted) && f.Together(Shifted, 90, f.Yard), "The group moves and turns with its other markers: " + moved.Error + " " + f.Where());
        Assert(Near(f.At(f.Yard), onTrack) && SameTurn(f.Yard.Rotation, facing) && f.Bindings() == Bound, "The following yard limit stays on its track point with its binding: " + f.Where());
        editor.Undo(); Assert(Near(f.Root(), Home) && f.Together(Home, 0, f.Yard) && Near(f.At(f.Yard), onTrack), "Undo returns the group; the yard limit stays: " + f.Where());
        editor.Redo(); Assert(f.Together(Shifted, 90, f.Yard) && Near(f.At(f.Yard), onTrack), "Redo moves the group again around it: " + f.Where());
        Assert(editor.Save() == null, "Save writes the file");
        var modifications = JObject.Parse(store.Patch)["modifications"].Children<JProperty>().ToArray();
        foreach (var order in new[] { modifications, modifications.Reverse().ToArray() }) {
            var fresh = new Fixture(); fresh.Tracks.Poses["Sitz|a|0"] = f.Tracks.Poses["Sitz|a|0"];
            var reordered = new JObject(new JProperty("formatVersion", 1), new JProperty("sourceMap", new JObject(new JProperty("mapId", "map"))), new JProperty("modifications", new JObject(order.Select(p => new JProperty(p.Name, p.Value.DeepClone())))));
            var reload = fresh.Engine.Apply(reordered.ToString(), "author/pack");
            Assert(reload.Error == null && reload.Outcomes.Values.All(o => o.Applied) && fresh.Together(Shifted, 90, fresh.Yard) && Near(fresh.At(fresh.Yard), onTrack) && fresh.Placed.Contains(fresh.Yard),
                "A fresh load in key order " + String.Join(",", order.Select(p => p.Name)) + " keeps the yard limit on its track point: " + fresh.Where());
        }
        f.Tracks.Poses["Sitz|a|0"] = new TrackPose(new[] { 3930.16, 529, 5015.7 }, Identity);
        var carried = session.ReconcileTrackFollowing().Single().Value;
        Assert(carried.Applied && Near(f.At(f.Yard), new[] { onTrack[0] + 10, onTrack[1], onTrack[2] }) && f.Together(Shifted, 90, f.Yard), "A later track move carries the yard limit along its track; the group stays: " + carried.Error + " " + f.Where());
        var stopped = session.StopFollowing(MarkerId(YardPath));
        Assert(stopped.Applied && !f.Placed.Contains(f.Yard), "Stop stores a fixed placement and ends the track's hold: " + stopped.Error);
        var standing = f.At(f.Yard);
        Assert(session.Edit(GroupId(), new Placement(Home, Identity), null).Applied && !Near(f.At(f.Yard), standing), "A marker that no longer follows its track moves with its group again: " + f.Where());
        editor.Undo(); editor.Undo(); Assert(f.Placed.Contains(f.Yard) && Near(f.At(f.Yard), standing), "Undo of Stop makes it follow its track again: " + f.Where());
    }
}
