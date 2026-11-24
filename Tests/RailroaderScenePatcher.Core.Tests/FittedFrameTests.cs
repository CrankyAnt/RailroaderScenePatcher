// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// A frame follows its model: the one box around an object, a group or a mesh lies along what it holds, also where the
// scene object's own axes do not, as with a group root that stands square to the world while its building lies along
// the track, or a crossing pad whose mesh is built along a curve in axes of its own.
static class FittedFrameTests {
    static readonly ScreenRect View = new ScreenRect(0, 0, 2560, 1440);
    const double Near = .3, Far = 20000;
    static void Check(bool value, string message) { if (!value) throw new Exception("Fitted frame: " + message); }

    // A camera at a position looking north (+Z), pitched down by an angle, with a 60 degree vertical field.
    static Func<WorldPoint, ClipPoint> Camera(WorldPoint eye, double pitch) {
        double p = pitch * Math.PI / 180, t = Math.Tan(Math.PI / 6), aspect = View.Width / View.Height;
        return q => {
            double dx = q.X - eye.X, dy = q.Y - eye.Y, dz = q.Z - eye.Z;
            double depth = -Math.Sin(p) * dy + Math.Cos(p) * dz, up = Math.Cos(p) * dy + Math.Sin(p) * dz;
            return new ClipPoint(dx / (t * aspect), up / t, (Far + Near) / (Far - Near) * depth - 2 * Far * Near / (Far - Near), depth);
        };
    }
    static double Shoelace(IReadOnlyList<ScreenPoint> outline) {
        double sum = 0;
        for (int i = 0; i < outline.Count; i++) { var a = outline[i]; var b = outline[(i + 1) % outline.Count]; sum += a.X * b.Y - b.X * a.Y; }
        return Math.Abs(sum) / 2;
    }
    static double Edge(IReadOnlyList<WorldPoint> box, int bit) =>
        Math.Sqrt(Math.Pow(box[bit].X - box[0].X, 2) + Math.Pow(box[bit].Y - box[0].Y, 2) + Math.Pow(box[bit].Z - box[0].Z, 2));
    static IEnumerable<double> Edges(IReadOnlyList<WorldPoint> box) => new[] { Edge(box, 1), Edge(box, 2), Edge(box, 4) }.OrderBy(e => e);
    static bool Near3(IEnumerable<double> a, params double[] b) => a.Zip(b, (x, y) => Math.Abs(x - y) < 1e-6).All(x => x) && a.Count() == b.Length;

    // A box of a length, height and width turned by a yaw about the vertical, around a centre, as eight corners.
    static WorldPoint[] Turned(double cx, double cy, double cz, double length, double height, double width, double yaw) {
        double c = Math.Cos(yaw * Math.PI / 180), s = Math.Sin(yaw * Math.PI / 180);
        var corners = new WorldPoint[8];
        for (int i = 0; i < 8; i++) {
            double along = ((i & 1) == 0 ? -.5 : .5) * length, up = ((i & 2) == 0 ? -.5 : .5) * height, across = ((i & 4) == 0 ? -.5 : .5) * width;
            corners[i] = new WorldPoint(cx + along * c - across * s, cy + up, cz + along * s + across * c);
        }
        return corners;
    }
    static ObjectMarkingBox BoxOf(string name, WorldPoint[] corners) {
        double minX = corners.Min(p => p.X), maxX = corners.Max(p => p.X), minY = corners.Min(p => p.Y), maxY = corners.Max(p => p.Y), minZ = corners.Min(p => p.Z), maxZ = corners.Max(p => p.Z);
        return new ObjectMarkingBox(name, new WorldPoint((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2), new WorldPoint((maxX - minX) / 2, (maxY - minY) / 2, (maxZ - minZ) / 2), corners);
    }

    public static void Run() {
        MeshAlongACurve();
        GroupRootSquareToTheWorld();
        SmallGainKeepsTheObjectsAxes();
        GroupFrameFollowsItsObjects();
        Console.WriteLine("PASS: frames follow their models");
    }

    // A crossing pad 6 m long and 3 m wide lies along a track turned 25 degrees, while its mesh is built in axes
    // square to the world. The box around its points lies along the pad: 6 by 3 m, as tall as the pad, not the
    // larger square-to-the-world box.
    static void MeshAlongACurve() {
        var pad = Turned(0, .1, 0, 6, .2, 3, 25);
        var fitted = ObjectMarkingGeometry.Fit(pad, WorldBasis.World);
        Check(Near3(Edges(fitted), .2, 3, 6), "the box around a turned pad's points is the pad's own box: " + String.Join(", ", Edges(fitted)));
        foreach (var corner in pad) Check(fitted.Any(c => Math.Abs(c.X - corner.X) < 1e-6 && Math.Abs(c.Y - corner.Y) < 1e-6 && Math.Abs(c.Z - corner.Z) < 1e-6), "the fitted box has the pad's corners at its corners");
        // The points of a mesh need not be a box: the corners of a pad and some points along its middle give the same box.
        var more = pad.Concat(new[] { new WorldPoint(0, .2, 0), new WorldPoint(1, 0, .5) }).ToArray();
        Check(Near3(Edges(ObjectMarkingGeometry.Fit(more, WorldBasis.World)), .2, 3, 6), "points inside the pad change nothing");
        // Points that are no shape give no box.
        Check(ObjectMarkingGeometry.Fit(new WorldPoint[0], WorldBasis.World) == null, "no points give no box");
    }

    // Bryson Coal Dealer: its root stands nearly square to the world, while the trestle and the bins lie along the
    // track at 35 degrees. Framed whole, the one box around them lies along the track: about as small as the box
    // the trestle's own axes give, and much smaller than the box in the root's axes, which also covers ground beside
    // the trestle.
    static void GroupRootSquareToTheWorld() {
        var trestle = Turned(0, 4, 100, 60, 8, 6, 35); var bins = Turned(20 * Math.Cos(35 * Math.PI / 180) - 6 * Math.Sin(35 * Math.PI / 180), 2, 100 + 20 * Math.Sin(35 * Math.PI / 180) + 6 * Math.Cos(35 * Math.PI / 180), 12, 4, 6, 35);
        var boxes = new[] { BoxOf("trestle", trestle), BoxOf("bins", bins) };
        var eye = new WorldPoint(0, 120, 20); var view = Camera(eye, 55);
        var root = new WorldPoint(0, 0, 100);
        var square = new WorldBasis(new WorldPoint(Math.Cos(2.8 * Math.PI / 180), 0, -Math.Sin(2.8 * Math.PI / 180)), new WorldPoint(0, 1, 0), new WorldPoint(Math.Sin(2.8 * Math.PI / 180), 0, Math.Cos(2.8 * Math.PI / 180)));
        var along = new WorldBasis(new WorldPoint(Math.Cos(35 * Math.PI / 180), 0, Math.Sin(35 * Math.PI / 180)), new WorldPoint(0, 1, 0), new WorldPoint(-Math.Sin(35 * Math.PI / 180), 0, Math.Cos(35 * Math.PI / 180)));
        var framed = ObjectMarkingGeometry.Frame(root, eye, boxes, view, View, null, ObjectMarkingGeometry.FrameDistance, square, true);
        var trestleAxes = ObjectMarkingGeometry.Frame(root, eye, boxes, view, View, null, ObjectMarkingGeometry.FrameDistance, along, true);
        // The box in the root's axes, as a single box of those corners: the frame the root's axes would give.
        var rootBox = ObjectMarkingGeometry.Enclose(trestle.Concat(bins), square);
        var rootAxes = ObjectMarkingGeometry.Frame(root, eye, new[] { BoxOf("root axes", rootBox) }, view, View, null);
        Check(framed.Frame.HasValue && trestleAxes.Frame.HasValue && rootAxes.Frame.HasValue, "all three are framed");
        Check(Math.Abs(Shoelace(framed.Outline) - Shoelace(trestleAxes.Outline)) < .02 * Shoelace(trestleAxes.Outline),
            "a group framed whole lies along its objects, not along its root: " + Shoelace(framed.Outline) + " against " + Shoelace(trestleAxes.Outline));
        Check(Shoelace(framed.Outline) < .75 * Shoelace(rootAxes.Outline), "the box along the objects covers less ground than the box in the root's axes: " + Shoelace(framed.Outline) + " against " + Shoelace(rootAxes.Outline));
        foreach (var point in trestle.Concat(bins)) {
            var clip = view(point);
            double x = View.X + (clip.X / clip.W + 1) * View.Width / 2, y = View.Y + (1 - clip.Y / clip.W) * View.Height / 2;
            Check(ObjectMarkingGeometry.Contains(framed.Outline, x, y), "the box holds every corner of every box");
        }
        // The marker beyond the frame distance stands on the center of the same box.
        var far = new WorldPoint(0, 600, -400);
        var marker = ObjectMarkingGeometry.Frame(root, far, boxes, Camera(far, 55), View, null, ObjectMarkingGeometry.FrameDistance, square, true);
        Check(marker.Marker.HasValue && !marker.Frame.HasValue, "beyond the frame distance only the marker shows");
        var fitted = ObjectMarkingGeometry.Fit(trestle.Concat(bins), square);
        double mx = fitted.Average(p => p.X), my = fitted.Average(p => p.Y), mz = fitted.Average(p => p.Z);
        Check(Math.Abs(marker.Marker.Value.X - mx) < 1e-6 && Math.Abs(marker.Marker.Value.Y - my) < 1e-6 && Math.Abs(marker.Marker.Value.Z - mz) < 1e-6, "the far marker stands on the center of the fitted box");
    }

    // A box that fits only a little better does not replace the object's own axes: a building of two square halves,
    // with the object turned 3 degrees, keeps the object's axes, so a frame does not swing for a model that is
    // nearly square to its object or nearly round.
    static void SmallGainKeepsTheObjectsAxes() {
        var halves = Turned(-5, 5, 100, 10, 10, 20, 0).Concat(Turned(5, 5, 100, 10, 10, 20, 0)).ToArray();
        var turned = new WorldBasis(new WorldPoint(Math.Cos(3 * Math.PI / 180), 0, -Math.Sin(3 * Math.PI / 180)), new WorldPoint(0, 1, 0), new WorldPoint(Math.Sin(3 * Math.PI / 180), 0, Math.Cos(3 * Math.PI / 180)));
        var fitted = ObjectMarkingGeometry.Fit(halves, turned);
        var own = ObjectMarkingGeometry.Enclose(halves, turned);
        for (int i = 0; i < 8; i++) Check(Math.Abs(fitted[i].X - own[i].X) < 1e-9 && Math.Abs(fitted[i].Y - own[i].Y) < 1e-9 && Math.Abs(fitted[i].Z - own[i].Z) < 1e-9, "a small gain keeps the object's axes");
        // An L of two bars square to the world: the world's box is the smallest; the box along the L's diagonal edge is larger.
        var ell = Turned(5, 1, 1, 10, 2, 2, 0).Concat(Turned(1, 1, 5, 2, 2, 10, 0)).ToArray();
        Check(Near3(Edges(ObjectMarkingGeometry.Fit(ell, WorldBasis.World)), 2, 10, 10), "an L square to the world keeps the world's box");
    }

    // A group's one box lies along its objects too, and where its objects stand counts only for an object without a
    // box of its own: the pivot of a group root that stands apart from the root's building does not widen the box,
    // while a controller without a box of its own still counts.
    static void GroupFrameFollowsItsObjects() {
        var eye = new WorldPoint(0, 300, -100); var view = Camera(eye, 60);
        var trestle = Turned(0, 4, 100, 60, 8, 6, 35);
        var frame = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), eye, new[] { BoxOf("trestle", trestle) }, view, View, null, Double.PositiveInfinity);
        // The member's pivot stands 25 m from its building.
        var pivot = new WorldPoint(25, 0, 80);
        var square = WorldBasis.World;
        var group = ObjectMarkingGeometry.GroupFrame(eye, new[] { pivot }, new[] { frame }, square, null, view, View);
        var alone = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), eye, new[] { BoxOf("trestle", trestle) }, view, View, null, Double.PositiveInfinity);
        Check(group.Frame.HasValue && Math.Abs(Shoelace(group.Outline) - Shoelace(alone.Outline)) < .02 * Shoelace(alone.Outline),
            "the group's box lies along its object and leaves the pivot apart from it out: " + Shoelace(group.Outline) + " against " + Shoelace(alone.Outline));
        // An object without a box of its own counts with where it stands.
        var controller = new WorldPoint(0, 0, 130);
        var held = ObjectMarkingGeometry.GroupFrame(eye, new[] { pivot, controller }, new[] { frame, null }, square, null, view, View);
        var spot = view(controller);
        double sx = View.X + (spot.X / spot.W + 1) * View.Width / 2, sy = View.Y + (1 - spot.Y / spot.W) * View.Height / 2;
        Check(held.Frame.HasValue && ObjectMarkingGeometry.Contains(held.Outline, sx, sy), "an object without a box counts with where it stands");
    }
}
