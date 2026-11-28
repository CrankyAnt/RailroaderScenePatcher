// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Select in world keeps its per-frame work proportional to what changes: an object's piece is kept between camera
// moves, an object outside the view or beyond the frame distance reads no ground, a still camera reuses its pass, and
// the frame diagnostics run once per camera rest. Every kept or culled result gives the markers and frames a fresh
// pass gives.
static class MarkingCostTests {
    static readonly ScreenRect View = new ScreenRect(0, 0, 2560, 1440);
    const double Near = .3, Far = 20000;
    static void Check(bool value, string message) { if (!value) throw new Exception("Marking cost: " + message); }

    // A camera at a position looking at a point, in Unity's clip convention for the view.
    static Func<WorldPoint, ClipPoint> LookAt(WorldPoint eye, WorldPoint at, double fov = 60) {
        double t = Math.Tan(fov * Math.PI / 360), aspect = View.Width / View.Height;
        double fx = at.X - eye.X, fy = at.Y - eye.Y, fz = at.Z - eye.Z, fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        fx /= fl; fy /= fl; fz /= fl;
        double rx = fz, rz = -fx, rl = Math.Sqrt(rx * rx + rz * rz);
        rx /= rl; rz /= rl;
        double ux = fy * rz, uy = fz * rx - fx * rz, uz = -fy * rx;
        return q => {
            double dx = q.X - eye.X, dy = q.Y - eye.Y, dz = q.Z - eye.Z;
            double depth = dx * fx + dy * fy + dz * fz, right = dx * rx + dz * rz, up = dx * ux + dy * uy + dz * uz;
            return new ClipPoint(right / (t * aspect), up / t, (Far + Near) / (Far - Near) * depth - 2 * Far * Near / (Far - Near), depth);
        };
    }
    static ObjectMarkingBox Box(string name, double x, double y, double z, double ex, double ey, double ez, Func<double, double, double?> ground = null, IReadOnlyList<WorldPoint> oriented = null) =>
        new ObjectMarkingBox(name, new WorldPoint(x, y, z), new WorldPoint(ex, ey, ez), oriented, ObjectMarkingBoxKind.Mesh, ground);
    // The eight corners of a box turned about the vertical by an angle, in the order Frame expects.
    static WorldPoint[] Turned(double x, double y, double z, double ex, double ey, double ez, double degrees) {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var corners = new WorldPoint[8];
        for (var i = 0; i < 8; i++) {
            double lx = ((i & 1) == 0 ? -1 : 1) * ex, ly = ((i & 2) == 0 ? -1 : 1) * ey, lz = ((i & 4) == 0 ? -1 : 1) * ez;
            corners[i] = new WorldPoint(x + c * lx + s * lz, y + ly, z - s * lx + c * lz);
        }
        return corners;
    }
    static bool Near1(double a, double b) => (Double.IsNaN(a) && Double.IsNaN(b)) || Math.Abs(a - b) < 1e-6;
    static bool SamePoint(WorldPoint? a, WorldPoint? b) => a.HasValue == b.HasValue && (!a.HasValue || (Near1(a.Value.X, b.Value.X) && Near1(a.Value.Y, b.Value.Y) && Near1(a.Value.Z, b.Value.Z)));
    static bool SameRect(ScreenRect? a, ScreenRect? b) => a.HasValue == b.HasValue && (!a.HasValue || (Near1(a.Value.X, b.Value.X) && Near1(a.Value.Y, b.Value.Y) && Near1(a.Value.Width, b.Value.Width) && Near1(a.Value.Height, b.Value.Height)));
    static bool SameOutline(IReadOnlyList<ScreenPoint> a, IReadOnlyList<ScreenPoint> b) =>
        (a == null && b == null) || (a != null && b != null && a.Count == b.Count && a.Select((p, i) => Near1(p.X, b[i].X) && Near1(p.Y, b[i].Y)).All(x => x));
    static bool SameBounds(WorldBox? a, WorldBox? b) => a.HasValue == b.HasValue && (!a.HasValue || (SamePoint(a.Value.Min, b.Value.Min) && SamePoint(a.Value.Max, b.Value.Max)));
    // Two frames give the same marking: the same rectangle, outline, marker, distance, bounds and boxes for a group.
    static void Same(ObjectMarkingFrame kept, ObjectMarkingFrame fresh, string what) {
        Check(SameRect(kept.Frame, fresh.Frame), what + ": the frame rectangle differs");
        Check(SameOutline(kept.Outline, fresh.Outline), what + ": the outline differs");
        Check(SamePoint(kept.Marker, fresh.Marker), what + ": the marker differs");
        Check(Near1(kept.Distance, fresh.Distance), what + ": the distance differs");
        Check(SameBounds(kept.Bounds, fresh.Bounds), what + ": the bounds differ");
        var a = Lent(kept); var b = Lent(fresh);
        Check(SamePoint(a.Center, b.Center) && Near1(a.Distance, b.Distance), what + ": the boxes a group takes differ");
    }

    // The group of one object, seen from far away: its center and distance come from the boxes the object lends it.
    static GroupMarking Lent(ObjectMarkingFrame frame) => ObjectMarkingGeometry.GroupFrame(new WorldPoint(5000, 50, 5000), new[] { new WorldPoint(-9999, 0, -9999) },
        new[] { frame }, null, null, q => new ClipPoint(0, 0, 0, 1), View);

    public static void Run() {
        KeptPieceGivesTheFreshFrame();
        OutsideTheViewReadsNoGround();
        BeyondTheFrameDistanceReadsNoGround();
        StillCameraReusesItsPass();
        DiagnosticsOncePerCameraRest();
        RepeatedObservationKeepsOneEntry();
        KeptGroupPieceGivesTheFreshGroupFrame();
        GroupPieceHoldsWhileItsObjectsStay();
    }

    static bool SameGroup(GroupMarking a, GroupMarking b) =>
        Near1(a.Distance, b.Distance) && SamePoint(a.Center, b.Center) && SamePoint(a.Marker, b.Marker) && SameRect(a.Frame, b.Frame) && SameOutline(a.Outline, b.Outline) && a.Spread == b.Spread && a.Shows == b.Shows;
    // A station complex of a hall, a shed and a lamp without boxes, with its detection span left out: from each camera
    // around it, near, at the group distance, within and beyond the frame distance, the group marking from the kept
    // group piece is the marking the objects give.
    static void KeptGroupPieceGivesTheFreshGroupFrame() {
        Func<double, double, double?> slope = (x, z) => x * .05;
        var eye0 = new WorldPoint(0, 50, -300);
        var frames = new[] {
            ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 0), eye0, new[] { Box("hall", 0, 4, 0, 10, 4, 6, slope) }, LookAt(eye0, new WorldPoint(0, 0, 0)), View, null, Double.PositiveInfinity),
            ObjectMarkingGeometry.Frame(new WorldPoint(30, 0, 10), eye0, new[] { Box("shed", 30, 2, 10, 4, 2, 4, slope) }, LookAt(eye0, new WorldPoint(0, 0, 0)), View, null, Double.PositiveInfinity),
            null,
            ObjectMarkingGeometry.Frame(new WorldPoint(-40, 0, 0), eye0, new[] { Box("span", -40, 0, 0, 30, .1, 2) }, LookAt(eye0, new WorldPoint(0, 0, 0)), View, null, Double.PositiveInfinity),
        };
        var anchors = new[] { new WorldPoint(0, 0, 0), new WorldPoint(30, 0, 10), new WorldPoint(12, 3, -8), new WorldPoint(-40, 0, 0) };
        var listed = new[] { true, true, true, false };
        WorldBasis? axes = new WorldBasis(new WorldPoint(.966, 0, -.259), new WorldPoint(0, 1, 0), new WorldPoint(.259, 0, .966));
        var piece = ObjectMarkingGeometry.GroupPieceOf(anchors, frames, axes, listed);
        var compared = 0; var shows = new HashSet<GroupShows>();
        foreach (var distance in new[] { 40.0, 110, 140, 300, 480, 520, 1500 })
            for (var angle = 0; angle < 360; angle += 45) {
                var a = angle * Math.PI / 180;
                var eye = new WorldPoint(Math.Cos(a) * distance, 10 + distance / 6, Math.Sin(a) * distance);
                foreach (var at in new[] { new WorldPoint(0, 0, 0), new WorldPoint(0, 0, 400) }) {
                    var project = LookAt(eye, at);
                    var kept = ObjectMarkingGeometry.GroupFrame(piece, eye, slope, project, View);
                    var fresh = ObjectMarkingGeometry.GroupFrame(eye, anchors, frames, axes, slope, project, View, listed);
                    Check(SameGroup(kept, fresh), "the kept group piece gives the fresh group marking from " + eye.X.ToString("0") + "," + eye.Z.ToString("0"));
                    shows.Add(fresh.Shows); compared++;
                }
            }
        Check(compared == 112 && shows.Count == 3, "the comparison covers objects, marker and frame, and marker only: " + string.Join(",", shows));
    }

    // A kept group piece holds while every listed object lends the same boxes and an object without boxes stands where it
    // stood; a moved object, new boxes or a turned anchor make it again.
    static void GroupPieceHoldsWhileItsObjectsStay() {
        var eye = new WorldPoint(0, 50, -300); var project = LookAt(eye, new WorldPoint(0, 0, 0));
        var hall = ObjectMarkingGeometry.PieceOf(new WorldPoint(0, 0, 0), new[] { Box("hall", 0, 4, 0, 10, 4, 6) });
        var frames = new[] { ObjectMarkingGeometry.Frame(hall, eye, project, View, null), null };
        var anchors = new[] { new WorldPoint(0, 0, 0), new WorldPoint(12, 3, -8) };
        var piece = ObjectMarkingGeometry.GroupPieceOf(anchors, frames, null, null);
        var otherCamera = new WorldPoint(100, 20, 0);
        var again = new[] { ObjectMarkingGeometry.Frame(hall, otherCamera, LookAt(otherCamera, new WorldPoint(0, 0, 0)), View, null), null };
        Check(piece.Holds(new[] { new WorldPoint(0, 0, 0), new WorldPoint(12, 3, -8) }, again, null, null), "the same boxes seen from another camera hold the piece");
        Check(!piece.Holds(new[] { new WorldPoint(0, 0, 0), new WorldPoint(12, 3, -7) }, again, null, null), "an object without boxes that moved makes the piece again");
        var moved = ObjectMarkingGeometry.PieceOf(new WorldPoint(0, 0, 0), new[] { Box("hall", 0, 4, 1, 10, 4, 6) });
        Check(!piece.Holds(anchors, new[] { ObjectMarkingGeometry.Frame(moved, eye, project, View, null), null }, null, null), "an object measured again makes the piece again");
        Check(!piece.Holds(anchors, frames, new WorldBasis(new WorldPoint(0, 0, 1), new WorldPoint(0, 1, 0), new WorldPoint(-1, 0, 0)), null), "a turned anchor makes the piece again");
        Check(!piece.Holds(anchors, frames, null, new[] { true, false }), "another list of counted objects makes the piece again");
    }

    // An object observed again, as in every repaint of the inspected object and in the new candidates of each click,
    // finds the entry kept for it: the store stays one entry per object, its parts and family. Other parts or another
    // family are another entry; past the limit the store starts anew.
    static void RepeatedObservationKeepsOneEntry() {
        var kept = new KeptGeometry<int>(3);
        object building = new object(), lamp = new object(), crossing = new object(), pad = new object(), buck = new object();
        for (var repaint = 0; repaint < 1000; repaint++) {
            var key = new MarkingKey(building, null, null, "Scene object");
            if (!kept.TryGet(key, out _)) kept.Keep(key, repaint);
            var part = new MarkingKey(crossing, new List<object> { pad, buck }, new List<object> { pad }, "Grade crossing");
            if (!kept.TryGet(part, out _)) kept.Keep(part, repaint);
        }
        Check(kept.Count == 2, "an object observed again a thousand times keeps one entry: " + kept.Count);
        Check(kept.TryGet(new MarkingKey(building, null, null, "Scene object"), out var first) && first == 0, "the entry kept first is found again");
        Check(!kept.TryGet(new MarkingKey(crossing, new List<object> { pad }, null, "Grade crossing"), out _), "other parts of the same object are another entry");
        Check(!kept.TryGet(new MarkingKey(building, null, null, "Child object"), out _), "another family is another entry");
        kept.Keep(new MarkingKey(lamp, null, null, "Scene object"), 1);
        Check(kept.Count == 3, "a third object adds an entry");
        kept.Keep(new MarkingKey(new object(), null, null, "Scene object"), 1);
        Check(kept.Count == 1, "past the limit the store starts anew: " + kept.Count);
        kept.RemoveWhere(handle => true);
        Check(kept.Count == 0, "entries of destroyed objects are let go");
    }

    // A station building of three boxes on sloping ground, a turned shed, and a crossing framed whole: from each camera
    // around and above them, near and far, inside and outside the view, the frame from the kept piece without
    // diagnostics is the frame the boxes give with diagnostics, which culls nothing.
    static void KeptPieceGivesTheFreshFrame() {
        Func<double, double, double?> slope = (x, z) => x * .05;
        var objects = new[] {
            Tuple.Create("station", new WorldPoint(0, 0, 0), new[] { Box("hall", 0, 4, 0, 10, 4, 6, slope), Box("wing", 14, 3, 0, 4, 3, 4, slope), Box("chimney", 3, 10, 2, .5f, 2, .5f, slope) }, (WorldBasis?)null, false),
            Tuple.Create("shed", new WorldPoint(40, 0, 30), new[] { Box("shed", 40, 2, 30, 4, 2, 4, slope, Turned(40, 2, 30, 3, 2, 2, 30)) }, (WorldBasis?)new WorldBasis(new WorldPoint(.866, 0, -.5), new WorldPoint(0, 1, 0), new WorldPoint(.5, 0, .866)), false),
            Tuple.Create("crossing", new WorldPoint(-30, 0, 10), new[] { Box("gate", -30, 2, 10, 1, 2, 1), Box("pad", -34, .1, 10, 3, .1, 2), Box("sign", -26, 1.5, 12, .3, 1.5, .1) }, (WorldBasis?)null, true),
        };
        var eyes = new List<WorldPoint>();
        foreach (var distance in new[] { 15.0, 60, 240, 480, 520, 900 })
            for (var angle = 0; angle < 360; angle += 30) {
                var a = angle * Math.PI / 180;
                eyes.Add(new WorldPoint(Math.Cos(a) * distance, 3 + distance / 8, Math.Sin(a) * distance));
            }
        var targets = new[] { new WorldPoint(0, 0, 0), new WorldPoint(300, 0, 0), new WorldPoint(0, 0, -300), new WorldPoint(0, 400, 0) };
        var compared = 0; var culled = 0;
        foreach (var o in objects) {
            var piece = ObjectMarkingGeometry.PieceOf(o.Item2, o.Item3, o.Item4, o.Item5);
            foreach (var eye in eyes)
                foreach (var at in targets) {
                    var project = LookAt(eye, at);
                    foreach (var frameDistance in new[] { ObjectMarkingGeometry.FrameDistance, Double.PositiveInfinity }) {
                        var kept = ObjectMarkingGeometry.Frame(piece, eye, project, View, null, frameDistance);
                        var fresh = ObjectMarkingGeometry.Frame(o.Item2, eye, o.Item3, project, View, new List<string>(), frameDistance, o.Item4, o.Item5);
                        Same(kept, fresh, o.Item1 + " from " + eye.X.ToString("0") + "," + eye.Z.ToString("0"));
                        compared++;
                        if (!fresh.Frame.HasValue && fresh.Distance <= frameDistance) culled++;
                    }
                }
        }
        Check(compared > 1000 && culled > 100, "the comparison covers objects outside the view within the frame distance: " + culled + " of " + compared);
    }

    // A shed 100 m behind the camera, and one 100 m to its side outside a narrow view: neither has a frame or a frame
    // marker, both are 96 m away, and neither reads the ground. The same shed in front reads it.
    static void OutsideTheViewReadsNoGround() {
        var reads = 0;
        Func<double, double, double?> ground = (x, z) => { reads++; return 0; };
        var eye = new WorldPoint(0, 2, 0);
        var ahead = LookAt(eye, new WorldPoint(0, 2, 100), 40);
        foreach (var place in new[] { new WorldPoint(0, 2, -100), new WorldPoint(100, 2, 0) }) {
            var piece = ObjectMarkingGeometry.PieceOf(place, new[] { Box("shed", place.X, place.Y, place.Z, 4, 2, 4, ground) });
            var frame = ObjectMarkingGeometry.Frame(piece, eye, ahead, View, null);
            Check(!frame.Frame.HasValue && !frame.Marker.HasValue, "a shed outside the view has no frame and no frame marker");
            Check(Math.Abs(frame.Distance - 96) < 1e-9, "the distance of a shed outside the view still runs to its bounds: " + frame.Distance);
            Check(frame.Bounds.HasValue && SamePoint(Lent(frame).Center, place), "a shed outside the view still lends its box to its group");
        }
        Check(reads == 0, "a shed outside the view reads no ground: " + reads);
        var front = ObjectMarkingGeometry.PieceOf(new WorldPoint(0, 2, 100), new[] { Box("shed", 0, 2, 100, 4, 2, 4, ground) });
        Check(ObjectMarkingGeometry.Frame(front, eye, ahead, View, null).Frame.HasValue && reads > 0, "a shed in the view is framed and cut at the ground");
    }

    // A shed 800 m ahead shows only its marker, on the centre of its box, and reads no ground.
    static void BeyondTheFrameDistanceReadsNoGround() {
        var reads = 0;
        Func<double, double, double?> ground = (x, z) => { reads++; return 0; };
        var eye = new WorldPoint(0, 2, 0);
        var piece = ObjectMarkingGeometry.PieceOf(new WorldPoint(0, 0, 800), new[] { Box("shed", 0, 2, 800, 4, 2, 4, ground) });
        var frame = ObjectMarkingGeometry.Frame(piece, eye, LookAt(eye, new WorldPoint(0, 2, 100)), View, null);
        Check(!frame.Frame.HasValue && SamePoint(frame.Marker, new WorldPoint(0, 2, 800)) && Math.Abs(frame.Distance - 796) < 1e-9 && reads == 0,
            "a far shed shows its marker on the centre of its box and reads no ground");
    }

    // A pass is reused while the camera's numbers and the pass's inputs stay the same, for at most the kept age, and
    // only when it was complete.
    static void StillCameraReusesItsPass() {
        var reuse = new MarkingReuse(30, 4);
        var candidates = new object(); var chosen = new object();
        var view = new double[] { 1, 2, 3, 0, 0, 0, 1 };
        Check(!reuse.Reuses(view, new[] { candidates, chosen }, 10), "nothing is reused before a pass was kept");
        reuse.Keep(view, new[] { candidates, chosen }, 10, true);
        Check(reuse.Reuses(new double[] { 1, 2, 3, 0, 0, 0, 1 }, new[] { candidates, chosen }, 11), "a still camera with the same inputs reuses the pass");
        Check(reuse.Reuses(view, new[] { candidates, chosen }, 39), "the pass is reused within its age");
        Check(!reuse.Reuses(view, new[] { candidates, chosen }, 40), "a pass is made again once it is the kept age old");
        Check(!reuse.Reuses(new double[] { 1, 2, 3.01, 0, 0, 0, 1 }, new[] { candidates, chosen }, 11), "a moved camera makes the pass again");
        Check(!reuse.Reuses(view, new[] { candidates, new object() }, 11), "another choice makes the pass again");
        Check(!reuse.Reuses(view, new object[] { candidates, null }, 11), "a choice that ends makes the pass again");
        Check(reuse.Reuses(new double[] { 1, 2, 3.000001, 0, 0, 1e-9, 1 }, new[] { candidates, chosen }, 11), "a camera that only jitters within a millionth stands still");
        reuse.Keep(view, new[] { candidates, chosen }, 12, false);
        Check(reuse.Reuses(view, new[] { candidates, chosen }, 15) && !reuse.Reuses(view, new[] { candidates, chosen }, 16),
            "an incomplete pass, with reads left for a later frame, is used again only for its shorter age");
        reuse.Keep(view, new[] { candidates, chosen }, 12, true);
        reuse.Forget();
        Check(!reuse.Reuses(view, new[] { candidates, chosen }, 13), "a forgotten pass, after the scene may have changed, is made again");
    }

    // The frame diagnostics run on the first pass of a camera that stands still, once, and again after it moved and
    // stood still again, or for another set of candidates.
    static void DiagnosticsOncePerCameraRest() {
        var pace = new ObservationPace();
        var set = new object();
        var a = new double[] { 1, 2, 3 }; var b = new double[] { 4, 5, 6 };
        Check(!pace.Due(a, set), "the first pass of a camera is no rest yet");
        Check(pace.Due(a, set), "the second pass from the same place is a rest: the diagnostics run");
        Check(!pace.Due(a, set), "a camera that stays at rest runs them once");
        Check(!pace.Due(b, set), "a moving camera runs none");
        Check(pace.Due(b, set), "a camera that rests again runs them again");
        Check(pace.Due(b, new object()), "another set of candidates at rest runs them again");
    }
}
