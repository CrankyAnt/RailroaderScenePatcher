// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Invisible scene objects, light-only and collider-only, show a helper marker: a square marker at the pivot and a frame
// for the colliders' bounds, which follows the object's physical placement and is never written anywhere.
static class HelperMarkerTests {
    static readonly ScreenRect View = new ScreenRect(0, 0, 2560, 1440);
    const double Near = .3, Far = 20000;
    static void Check(bool value, string message) { if (!value) throw new Exception("Helper marker: " + message); }
    // A camera at the origin looking along +Z with a 90 degree vertical field, in Unity's clip convention.
    static ClipPoint Camera(WorldPoint p) =>
        new ClipPoint(p.X * View.Height / View.Width, p.Y, (Far + Near) / (Far - Near) * p.Z - 2 * Far * Near / (Far - Near), p.Z);
    static readonly WorldPoint Eye = new WorldPoint(0, 0, 0);
    static ObjectMarkingBox Box(string name, double x, double y, double z, double e, ObjectMarkingBoxKind kind = ObjectMarkingBoxKind.Mesh, Func<double, double, double?> ground = null) =>
        new ObjectMarkingBox(name, new WorldPoint(x, y, z), new WorldPoint(e, e, e), null, kind, ground);
    static bool Near1(double a, double b) => Math.Abs(a - b) < .01;

    internal static void Run() {
        ShowsOnlyWithoutAnOutliningRenderer();
        AHiddenRendererWithAnUnknownBoxOrAWaitingObjectShowsNoHelperMarker();
        TheFrameTakesSolidCollidersThatAreOnAndLeavesTerrainOut();
        TriggersFrameOnlyAnObjectWithoutSolidColliders();
        AFarLargerColliderDoesNotOutlineTheObject();
        ALightOnlyObjectHasItsMarkerAtThePivot();
        AColliderFramesItsBoundsWithTheMarkerAtThePivot();
        ACollidersOnlyObjectGetsItsColliderFrame();
        BeyondTheFrameDistanceOnlyTheMarkerShows();
        APivotBehindTheCameraShowsNothing();
        SelectInWorldPicksTheObjectByItsHelperMarker();
        TheMarkerFollowsPlacementPreviewAndUndoAndIsNeverWritten();
        TheMarkingCarriesNoColourOfItsOwn();
    }

    static void ShowsOnlyWithoutAnOutliningRenderer() {
        var anchor = new WorldPoint(0, 0, 50);
        ObjectMarkingFrame FrameOf(params ObjectMarkingBox[] boxes) => ObjectMarkingGeometry.Frame(anchor, Eye, boxes, Camera, View, null);
        Check(HelperMarkers.Shows(FrameOf(), false, 0), "an object without renderers shows a helper marker");
        Check(HelperMarkers.Shows(FrameOf(Box("beam", 0, 0, 50, 2, ObjectMarkingBoxKind.Effect)), false, 0), "a light whose only renderer is its beam effect shows a helper marker");
        Check(HelperMarkers.Shows(FrameOf(Box("shadow", 0, 0, 50, 2, ObjectMarkingBoxKind.ShadowsOnly)), false, 0), "an object with only a shadows-only renderer shows a helper marker");
        Check(!HelperMarkers.Shows(FrameOf(Box("mesh", 0, 0, 50, 2)), false, 0), "a visible part shows no helper marker");
        Check(!HelperMarkers.Shows(FrameOf(Box("mesh", 0, 0, 50, 2), Box("beam", 0, 0, 50, 2, ObjectMarkingBoxKind.Effect)), false, 0), "a lamp with a mesh shows its own frame, not a helper marker");
    }

    // The decision to show a helper marker: only when no renderer would outline the object. A hidden renderer whose box
    // is not known yet, such as a static-batched building part that has not shown since the load, would outline it once
    // seen; an object whose renderer kinds are still being read waits. Both keep the small frame about their marker.
    static void AHiddenRendererWithAnUnknownBoxOrAWaitingObjectShowsNoHelperMarker() {
        var none = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 50), Eye, Array.Empty<ObjectMarkingBox>(), Camera, View, null);
        Check(HelperMarkers.Shows(none, false, 0), "an object with nothing that would outline it shows a helper marker");
        Check(!HelperMarkers.Shows(none, false, 1), "a static-batched part not seen since the load is no invisible object");
        Check(!HelperMarkers.Shows(none, true, 0), "an object whose renderers are still being read shows no helper marker yet");
    }

    static HelperCollider Solid(params MarkingLink[] chain) => new HelperCollider(true, false, false, chain);
    static readonly MarkingLink On = new MarkingLink(true, false), Off = new MarkingLink(false, false), Culled = new MarkingLink(false, true);

    // Which colliders frame an invisible object: those switched on, on objects that are on or off only through a culler.
    static void TheFrameTakesSolidCollidersThatAreOnAndLeavesTerrainOut() {
        var colliders = new[] {
            Solid(On, On),                                  // 0: counts
            new HelperCollider(false, false, false, new[] { On }), // 1: the collider is switched off
            Solid(Off, On),                                 // 2: its object is switched off
            Solid(Culled, On),                              // 3: its object is off only through a culler: counts
            Solid(On, Off),                                 // 4: the root above is switched off
            new HelperCollider(true, false, true, new[] { On }),   // 5: a terrain collider
        };
        Check(HelperMarkers.Framing(colliders).SequenceEqual(new[] { 0, 3 }), "only colliders that are on, or off through a culler, and no terrain collider frame the object: " + String.Join(",", HelperMarkers.Framing(colliders)));
        Check(HelperMarkers.Framing(new[] { new HelperCollider(true, false, true, new[] { On }) }).Count == 0, "a terrain collider alone gives no frame");
    }

    static void TriggersFrameOnlyAnObjectWithoutSolidColliders() {
        var mixed = new[] { new HelperCollider(true, true, false, new[] { On }), Solid(On) };
        Check(HelperMarkers.Framing(mixed).SequenceEqual(new[] { 1 }), "a trigger zone is left out beside a solid collider");
        var triggers = new[] { new HelperCollider(true, true, false, new[] { On }), new HelperCollider(false, true, false, new[] { On }) };
        Check(HelperMarkers.Framing(triggers).SequenceEqual(new[] { 0 }), "an object with only triggers is framed by its triggers that are on");
    }

    // A 2 m click spot at the pivot inside a 200 m trigger zone and a 2 km terrain collider, 50 m ahead.
    static void AFarLargerColliderDoesNotOutlineTheObject() {
        var pivot = new WorldPoint(0, 0, 50);
        var spot = Box("spot", 0, 1, 50, 1);
        var alone = HelperMarkers.Mark(pivot, Eye, new[] { spot }, Camera, View);
        var zone = HelperMarkers.Mark(pivot, Eye, new[] { spot, Box("zone", 0, 1, 60, 100), Box("terrain", 0, -5, 50, 1000) }, Camera, View);
        Check(zone.Frame.HasValue && Near1(zone.Frame.Value.Width, alone.Frame.Value.Width) && Near1(zone.Frame.Value.Height, alone.Frame.Value.Height),
            "a collider far larger than the one at the pivot does not outline the object, as with renderers");
        var apart = HelperMarkers.Mark(pivot, Eye, new[] { spot, Box("apart", 30, 1, 50, 1) }, Camera, View);
        Check(apart.Frame.HasValue && Near1(apart.Frame.Value.Width, alone.Frame.Value.Width), "a collider apart from the one at the pivot does not outline the object");
    }

    // A light 2 m right and 1 m up, 50 m ahead: x = 1280 * (1 + 2 * 0.5625 / 50) = 1308.8, y = 720 * (1 - 1 / 50) = 705.6.
    static void ALightOnlyObjectHasItsMarkerAtThePivot() {
        var mark = HelperMarkers.Mark(new WorldPoint(2, 1, 50), Eye, Array.Empty<ObjectMarkingBox>(), Camera, View);
        Check(mark.Marker.HasValue && Near1(mark.Marker.Value.X, 1308.8) && Near1(mark.Marker.Value.Y, 705.6), "the square marker stands on the light's pivot");
        Check(!mark.ColliderFrame && mark.Frame.HasValue && Near1(mark.Frame.Value.Width, 32) && Near1(mark.Frame.Value.Height, 32), "without a collider the frame is the least frame about the marker: 24 px and 4 px on each side");
        Check(Near1(mark.Frame.Value.X + 16, 1308.8) && Near1(mark.Frame.Value.Y + 16, 705.6), "the least frame is centred on the marker");
        Check(Near1(mark.Distance, Math.Sqrt(4 + 1 + 2500)), "the distance runs to the pivot");
    }

    // A 2 m collider cube whose pivot is at its bottom face, 50 m ahead.
    static void AColliderFramesItsBoundsWithTheMarkerAtThePivot() {
        var pivot = new WorldPoint(0, 0, 50);
        var mark = HelperMarkers.Mark(pivot, Eye, new[] { Box("collider", 0, 1, 50, 1) }, Camera, View);
        Check(mark.ColliderFrame && mark.Frame.HasValue, "a collider gives a frame for its bounds");
        // The cube's near face spans y 0..2 at z 49: 720 * (1 - 2 / 49) = 690.6 at the top, 720 at the bottom; its far
        // face reaches x = +-1280 * 0.5625 / 49. With the 4 px margin the frame is 33.4 px high.
        Check(Near1(mark.Frame.Value.Y, 720 * (1 - 2.0 / 49) - 4) && Near1(mark.Frame.Value.Bottom, 724), "the frame surrounds the collider's bounds");
        Check(Near1(mark.Frame.Value.Width, 2 * 1280 * .5625 / 49 + 8), "the frame is as wide as the collider's bounds");
        Check(mark.Marker.HasValue && Near1(mark.Marker.Value.X, 1280) && Near1(mark.Marker.Value.Y, 720), "the marker stands on the pivot, not on the centre of the collider");
        // A collider is not a model that stands in the ground: its frame keeps its whole bounds over any surface.
        var buried = HelperMarkers.Mark(pivot, Eye, new[] { Box("collider", 0, 1, 50, 1, ground: (x, z) => 1.5) }, Camera, View);
        Check(buried.Frame.HasValue && Near1(buried.Frame.Value.Y, mark.Frame.Value.Y) && Near1(buried.Frame.Value.Bottom, mark.Frame.Value.Bottom), "the collider's frame is never cut off at the ground");
        // Two colliders side by side are framed as one box: x -2..2 on the near face at z 49.
        var two = HelperMarkers.Mark(pivot, Eye, new[] { Box("a", -1, 1, 50, 1), Box("b", 1, 1, 50, 1) }, Camera, View);
        Check(two.Frame.HasValue && Near1(two.Frame.Value.X, 1280 - 2 * 1280 * .5625 / 49 - 4) && Near1(two.Frame.Value.Right, 1280 + 2 * 1280 * .5625 / 49 + 4), "both colliders side by side are inside the frame");
    }

    // The collection for a collider-only object, from the facts of its colliders to its frame: no renderer outlines
    // it, so it shows a helper marker; the colliders that count give their bounds from their eight corners, and a
    // collider whose corners cannot be read is left out alone. Here a terrain collider, the 2 m click spot cube with
    // its pivot at its bottom face 50 m ahead, a collider that is switched off, and a solid collider whose corners throw.
    static void ACollidersOnlyObjectGetsItsColliderFrame() {
        var pivot = new WorldPoint(0, 0, 50);
        var renderers = ObjectMarkingGeometry.Frame(pivot, Eye, Array.Empty<ObjectMarkingBox>(), Camera, View, null);
        Check(HelperMarkers.Shows(renderers, false, 0), "a collider-only object shows a helper marker");
        var facts = new[] { new HelperCollider(true, false, true, new[] { On }), Solid(On), new HelperCollider(false, false, false, new[] { On }), Solid(On) };
        var cube = new List<WorldPoint>();
        foreach (var x in new[] { -1.0, 1 }) foreach (var y in new[] { 0.0, 2 }) foreach (var z in new[] { 49.0, 51 }) cube.Add(new WorldPoint(x, y, z));
        var read = new List<int>();
        IReadOnlyList<WorldPoint> Corners(int i) { read.Add(i); if (i == 3) throw new InvalidOperationException("collider destroyed"); return cube; }
        var boxes = HelperMarkers.ColliderBoxes(HelperMarkers.Framing(facts), Corners);
        Check(read.SequenceEqual(new[] { 1, 3 }), "only the colliders that count are read: " + String.Join(",", read));
        Check(boxes.Count == 1 && boxes[0].Name == "collider #1" && Near1(boxes[0].Center.Y, 1) && Near1(boxes[0].Center.Z, 50) && Near1(boxes[0].Extents.X, 1) && Near1(boxes[0].Extents.Y, 1) && boxes[0].Oriented.Count == 8,
            "the click spot gives one box with its bounds and corners: " + String.Join(" | ", boxes.Select(p => p.Name + " " + p.Center.X + "," + p.Center.Y + "," + p.Center.Z)));
        var mark = HelperMarkers.Mark(pivot, Eye, boxes, Camera, View);
        Check(mark.ColliderFrame && mark.Frame.HasValue && Near1(mark.Frame.Value.Y, 720 * (1 - 2.0 / 49) - 4) && Near1(mark.Frame.Value.Bottom, 724) && Near1(mark.Frame.Value.Width, 2 * 1280 * .5625 / 49 + 8),
            "the collider-only object gets its collider frame around the click spot's bounds");
        Check(mark.Marker.HasValue && Near1(mark.Marker.Value.X, 1280) && Near1(mark.Marker.Value.Y, 720), "its marker stands on the pivot");
        // A collider that gives fewer than eight corners is left out; without a collider that counts, the least frame shows.
        Check(HelperMarkers.ColliderBoxes(new[] { 0 }, i => cube.Take(7).ToArray()).Count == 0 && HelperMarkers.ColliderBoxes(new[] { 0 }, i => null).Count == 0, "a collider without eight corners gives no box");
        var terrainOnly = HelperMarkers.Mark(pivot, Eye, HelperMarkers.ColliderBoxes(HelperMarkers.Framing(new[] { facts[0] }), Corners), Camera, View);
        Check(!terrainOnly.ColliderFrame && terrainOnly.Frame.HasValue && Near1(terrainOnly.Frame.Value.Width, 32), "an object with only a terrain collider keeps the least frame about its marker");
    }

    static void BeyondTheFrameDistanceOnlyTheMarkerShows() {
        var far = HelperMarkers.Mark(new WorldPoint(0, 0, 600), Eye, Array.Empty<ObjectMarkingBox>(), Camera, View);
        Check(far.Marker.HasValue && !far.Frame.HasValue && far.Outline == null, "a light beyond 500 m shows only its square marker");
        var collider = HelperMarkers.Mark(new WorldPoint(0, 0, 600), Eye, new[] { Box("collider", 0, 1, 600, 1) }, Camera, View);
        Check(collider.Marker.HasValue && !collider.Frame.HasValue, "a collider beyond 500 m shows only its square marker");
        var inspected = HelperMarkers.Mark(new WorldPoint(0, 0, 600), Eye, new[] { Box("collider", 0, 1, 600, 1) }, Camera, View, Double.PositiveInfinity);
        Check(inspected.ColliderFrame && inspected.Frame.HasValue, "an object asked for without a limit, as the inspected one, keeps its collider frame at any distance");
    }

    static void APivotBehindTheCameraShowsNothing() {
        var behind = HelperMarkers.Mark(new WorldPoint(0, 0, -10), Eye, Array.Empty<ObjectMarkingBox>(), Camera, View);
        Check(!behind.Marker.HasValue && !behind.Frame.HasValue, "a pivot behind the camera has no marker and no frame");
    }

    // A collider-only click spot whose pivot lies at the edge of its collider, beside a visible part.
    static void SelectInWorldPicksTheObjectByItsHelperMarker() {
        var spot = HelperMarkers.Mark(new WorldPoint(0, 0, 50), Eye, new[] { Box("collider", 0, 2, 50, 2) }, Camera, View);
        var light = HelperMarkers.Mark(new WorldPoint(8, 0, 50), Eye, Array.Empty<ObjectMarkingBox>(), Camera, View);
        PickTarget Target(HelperMarking m, double x, double y) => new PickTarget(m.Marker.Value.X, m.Marker.Value.Y, true, ObjectMarkingGeometry.Contains(m.Outline, x, y));
        WorldPickResult Click(double x, double y) => WorldPick.Resolve(x, y, new[] { Target(spot, x, y), Target(light, x, y), new PickTarget(400, 400, true, false) }, new PickGroup[0]);
        var onSpot = Click(spot.Marker.Value.X, spot.Marker.Value.Y);
        Check(onSpot.Objects.Count == 1 && onSpot.Objects[0] == 0, "a click on the helper marker's + picks the collider-only object");
        var onLight = Click(light.Marker.Value.X + 3, light.Marker.Value.Y - 3);
        Check(onLight.Objects.Count == 1 && onLight.Objects[0] == 1, "a click on a light's helper marker picks the light");
        // Inside the collider frame, above the marker: 3 m up on the near face.
        var inFrame = Click(1280, 720 * (1 - 3.0 / 48));
        Check(inFrame.Objects.Count == 1 && inFrame.Objects[0] == 0, "a click inside the collider frame picks the object");
    }

    // The scene object's pivot is its transform position, read live: during a gizmo preview and after Undo the marker
    // stands where the object stands. The world is shifted 50 m ahead of the camera.
    static void TheMarkerFollowsPlacementPreviewAndUndoAndIsNeverWritten() {
        var target = new TargetIdentity("s", "p", "c");
        var scene = new Scene();
        var box = new Box { Position = new[] { 7.0, 2.0, 3.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        scene.Objects["p"] = new List<object> { box };
        scene.Observations = () => new[] { new SceneObservation(box, target, "board", box.Active, scene.Read(box).Placement.Position, box.Rotation,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "placement") };
        var store = new Store(); var history = new NativeMapEditor();
        var engine = new PatchEngine(scene); var session = new PatchEditingSession(engine, history, store); history.Session = session;
        var inventory = new SceneInventory(scene); inventory.Refresh();
        HelperMarking Mark() => HelperMarkers.Mark(new WorldPoint(box.Position[0], box.Position[1], box.Position[2] + 50), Eye, Array.Empty<ObjectMarkingBox>(), Camera, View);
        double ScreenX(double x) => 1280 * (1 + x * .5625 / 53);
        var start = Mark();
        Check(Near1(start.Marker.Value.X, ScreenX(7)), "the marker stands at the object's placement");
        var gesture = new PlacementGesture(engine, session, target, inventory.Pin(inventory.Entries[0]));
        Check(gesture.Preview(new Placement(new[] { 112.0, 2.0, 3.0 }, new[] { 0.0, 0.0, 0.0, 1.0 })).Applied, "the gizmo previews a move");
        Check(Near1(Mark().Marker.Value.X, ScreenX(12)), "the marker follows the gizmo preview");
        Check(gesture.Complete().Applied && Near1(Mark().Marker.Value.X, ScreenX(12)), "the marker stays at the completed placement");
        var modification = (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"].First.First;
        Check(modification.Properties().All(p => p.Name == "target" || p.Name == "placement" || p.Name == "dependsOn"), "the scene patch file holds the placement and no helper marker: " + modification);
        Check(session.ScenePatchFileText.IndexOf("helper", StringComparison.OrdinalIgnoreCase) < 0, "nothing of the helper marker enters the scene patch file");
        history.Undo();
        Check(Near1(Mark().Marker.Value.X, ScreenX(7)), "the marker returns with Undo");
        history.Redo();
        Check(Near1(Mark().Marker.Value.X, ScreenX(12)), "the marker moves again with Redo");
    }

    // The overlay colours the helper marker from the world colour table by the object's state, as any marking: the
    // helper marking itself has no colour to add.
    static void TheMarkingCarriesNoColourOfItsOwn() {
        var members = typeof(HelperMarking).GetProperties().Select(p => p.Name).ToArray();
        Check(!members.Any(name => name.IndexOf("Colo", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Tone", StringComparison.OrdinalIgnoreCase) >= 0), "a helper marking holds no colour");
    }
}
