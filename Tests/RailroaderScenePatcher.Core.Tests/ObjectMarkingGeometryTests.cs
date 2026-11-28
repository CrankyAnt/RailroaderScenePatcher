// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// The frame a world marking draws around an object's renderers, and picks with. The same frame serves the gold frame of
// the inspected object and the frames of candidates; only the colour differs.
static class ObjectMarkingGeometryTests {
    static readonly ScreenRect View = new ScreenRect(0, 0, 2560, 1440);
    const double Near = .3, Far = 20000;
    static void Check(bool value, string message) { if (!value) throw new Exception("Object marking: " + message); }

    // A camera at the origin looking along +Z with a 90 degree vertical field, in the clip convention of Unity's
    // projection matrix: visible points satisfy -w <= x, y, z <= w.
    static ClipPoint Camera(WorldPoint p) {
        double depth = p.Z;
        return new ClipPoint(p.X * View.Height / View.Width, p.Y, (Far + Near) / (Far - Near) * depth - 2 * Far * Near / (Far - Near), depth);
    }
    static readonly WorldPoint Eye = new WorldPoint(0, 0, 0);
    // A camera at a position looking north (+Z), pitched down by an angle, with a vertical field of view, in Unity's
    // clip convention for the 2560 x 1440 view.
    static Func<WorldPoint, ClipPoint> Strategy(WorldPoint eye, double pitch, double fov) {
        double p = pitch * Math.PI / 180, t = Math.Tan(fov * Math.PI / 360), aspect = View.Width / View.Height;
        return q => {
            double dx = q.X - eye.X, dy = q.Y - eye.Y, dz = q.Z - eye.Z;
            double depth = -Math.Sin(p) * dy + Math.Cos(p) * dz, up = Math.Cos(p) * dy + Math.Sin(p) * dz;
            return new ClipPoint(dx / (t * aspect), up / t, (Far + Near) / (Far - Near) * depth - 2 * Far * Near / (Far - Near), depth);
        };
    }
    static ObjectMarkingBox Box(string name, double x, double y, double z, double ex, double ey, double ez, ObjectMarkingBoxKind kind = ObjectMarkingBoxKind.Mesh) =>
        new ObjectMarkingBox(name, new WorldPoint(x, y, z), new WorldPoint(ex, ey, ez), null, kind);
    static ObjectMarkingFrame Frame(WorldPoint anchor, params ObjectMarkingBox[] boxes) =>
        ObjectMarkingGeometry.Frame(anchor, Eye, boxes, Camera, View, new List<string>());
    static bool Inside(ScreenRect r, double x, double y) => x >= r.X && x <= r.Right && y >= r.Y && y <= r.Bottom;

    // The frame of one box with the given box and no ground, for comparing a cut frame with the box it should be.
    static ObjectMarkingFrame Unground(WorldPoint center, WorldPoint extents) => ObjectMarkingGeometry.Frame(new WorldPoint(center.X, center.Y, center.Z), Eye,
        new[] { new ObjectMarkingBox("box", center, extents, null, ObjectMarkingBoxKind.Mesh, null) }, Camera, View, new List<string>());
    // Two outlines are the same when each point of one lies within a hundredth of a pixel of a point of the other.
    static bool SameOutline(IReadOnlyList<ScreenPoint> a, IReadOnlyList<ScreenPoint> b) =>
        a != null && b != null && a.Count == b.Count && a.All(p => b.Any(q => Math.Abs(p.X - q.X) < .01 && Math.Abs(p.Y - q.Y) < .01));
    static ScreenPoint OnScreen(ClipPoint p) => new ScreenPoint(View.X + (p.X / p.W + 1) * View.Width / 2, View.Y + (1 - p.Y / p.W) * View.Height / 2);
    static bool Same(WorldPoint? a, WorldPoint? b) => a.HasValue && b.HasValue && Math.Abs(a.Value.X - b.Value.X) < 1e-9 && Math.Abs(a.Value.Y - b.Value.Y) < 1e-9 && Math.Abs(a.Value.Z - b.Value.Z) < 1e-9;

    // A camera at a position looking at a point, with a vertical field of view, in Unity's clip convention for the view:
    // a left-handed view with y up, as a camera turned around the object sees it.
    static Func<WorldPoint, ClipPoint> LookAt(WorldPoint eye, WorldPoint at, double fov) {
        double t = Math.Tan(fov * Math.PI / 360), aspect = View.Width / View.Height;
        double fx = at.X - eye.X, fy = at.Y - eye.Y, fz = at.Z - eye.Z, fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        fx /= fl; fy /= fl; fz /= fl;
        // right = up x forward, then up' = forward x right.
        double rx = fz, ry = 0, rz = -fx, rl = Math.Sqrt(rx * rx + rz * rz);
        rx /= rl; rz /= rl;
        double ux = fy * rz - fz * ry, uy = fz * rx - fx * rz, uz = fx * ry - fy * rx;
        return q => {
            double dx = q.X - eye.X, dy = q.Y - eye.Y, dz = q.Z - eye.Z;
            double depth = dx * fx + dy * fy + dz * fz, right = dx * rx + dy * ry + dz * rz, up = dx * ux + dy * uy + dz * uz;
            return new ClipPoint(right / (t * aspect), up / t, (Far + Near) / (Far - Near) * depth - 2 * Far * Near / (Far - Near), depth);
        };
    }
    // A square marker stands at a fixed world point of its object, the center of the box its frame outlines, so it stays
    // on the object as the camera zooms and turns; the center of the frame's rectangle on screen would wander. A group
    // such as Bryson Coal Dealer, framed whole, keeps the same point at 140 m and 200 m, seen from the front, from the
    // side and from far above, and the point lies inside the frame each time. Beyond the frame distance the marker still
    // stands on the object, and so does a group's square marker.
    static void MarkerStaysOnTheObject() {
        Func<double, double, double?> ground = (x, z) => 0;
        var boxes = new[] {
            new ObjectMarkingBox("shed", new WorldPoint(0, 4, 0), new WorldPoint(10, 4, 6), null, ObjectMarkingBoxKind.Mesh, ground),
            new ObjectMarkingBox("bin", new WorldPoint(18, 2, 4), new WorldPoint(4, 2, 4), null, ObjectMarkingBoxKind.Mesh, ground),
            new ObjectMarkingBox("lamp", new WorldPoint(-25, 3, 0), new WorldPoint(.3, 3, .3), null, ObjectMarkingBoxKind.Mesh, ground),
            // A pier footing runs 5 m into the ground: the cut box stands from the ground to the roof, the uncut box
            // reaches down to the footing.
            new ObjectMarkingBox("pier", new WorldPoint(5, -1, 0), new WorldPoint(1, 4, 1), null, ObjectMarkingBoxKind.Mesh, ground)
        };
        // The one box around the boxes, square to the world, runs from x -25.3 to 22, y -5 to 8 and z -6 to 8; cut at
        // the ground it runs from y 0 to 8.
        var cut = new WorldPoint(-1.65, 4, 1); var uncut = new WorldPoint(-1.65, 1.5, 1);
        bool At(WorldPoint? p, WorldPoint q) => p.HasValue && Math.Abs(p.Value.X - q.X) < 1e-6 && Math.Abs(p.Value.Y - q.Y) < 1e-6 && Math.Abs(p.Value.Z - q.Z) < 1e-6;
        var anchor = new WorldPoint(0, 0, 0);
        var cameras = new[] { new WorldPoint(0, 40, -134), new WorldPoint(0, 60, -190), new WorldPoint(-120, 50, -110), new WorldPoint(70, 180, -40) };
        WorldPoint? first = null;
        foreach (var eye in cameras) {
            var view = LookAt(eye, new WorldPoint(0, 4, 0), 60);
            var frame = ObjectMarkingGeometry.Frame(anchor, eye, boxes, view, View, null, ObjectMarkingGeometry.FrameDistance, null, true);
            Check(frame.Frame.HasValue && frame.Marker.HasValue, "a framed object has a marker point");
            if (first == null) first = frame.Marker; else Check(Same(first, frame.Marker), "the marker stands on the same world point from every camera position");
            Check(At(frame.Marker, cut), "the marker stands on the center of the box cut at the ground: " + frame.Marker);
            var screen = OnScreen(view(frame.Marker.Value));
            Check(ObjectMarkingGeometry.Contains(frame.Outline, screen.X, screen.Y), "the marker point lies inside the frame from " + eye.X + "/" + eye.Y + "/" + eye.Z);
        }
        var far = ObjectMarkingGeometry.Frame(anchor, new WorldPoint(0, 100, -900), boxes, Strategy(new WorldPoint(0, 100, -900), 5, 60), View, null, ObjectMarkingGeometry.FrameDistance, null, true);
        Check(!far.Frame.HasValue && At(far.Marker, uncut), "beyond the frame distance the marker stands on the center of the uncut box: " + far.Marker);
        // A group: its square marker stands on the center of its one box at 140 m and at 200 m.
        var frames = boxes.Select(box => ObjectMarkingGeometry.Frame(anchor, new WorldPoint(0, 40, -134), new[] { box }, Strategy(new WorldPoint(0, 40, -134), 15, 60), View, null)).ToArray();
        var anchors = boxes.Select(box => box.Center).ToArray();
        var near = ObjectMarkingGeometry.GroupFrame(new WorldPoint(0, 40, -150), anchors, frames, null, ground, Strategy(new WorldPoint(0, 40, -150), 15, 60), View);
        var farther = ObjectMarkingGeometry.GroupFrame(new WorldPoint(0, 60, -205), anchors, frames, null, ground, Strategy(new WorldPoint(0, 60, -205), 15, 60), View);
        Check(near.Shows == GroupShows.MarkerAndFrame && farther.Shows == GroupShows.MarkerAndFrame && Same(near.Marker, farther.Marker) && At(near.Marker, cut), "a group's square marker stands on the center of its cut box at 140 m and 200 m: " + near.Marker);
        var beyond = ObjectMarkingGeometry.GroupFrame(new WorldPoint(0, 60, -700), anchors, frames, null, ground, Strategy(new WorldPoint(0, 60, -700), 5, 60), View);
        Check(beyond.Shows == GroupShows.MarkerOnly && At(beyond.Marker, uncut), "beyond 500 m the group's square marker stands on the center of its uncut box: " + beyond.Marker);
    }

    // A marker stands on its world point whenever that point lies in front of the camera, also when the point lies
    // outside the frame's rectangle or the view, as for a frame larger than the view such as Ela Signs: the center of the
    // visible box of the frame would wander as the camera zooms. Only a point behind the camera falls back to the
    // rectangle's center.
    static void MarkerStaysOnItsPointOutsideTheView() {
        var rect = ScreenRect.FromEdges(0, 185, 2560, 1440);
        var inside = ObjectMarkingGeometry.MarkerScreen(true, 1280, 900, rect);
        Check(inside.X == 1280 && inside.Y == 900, "a marker point inside the frame is the marker");
        var below = ObjectMarkingGeometry.MarkerScreen(true, 1280, 1800, rect);
        Check(below.X == 1280 && below.Y == 1800, "a marker point in front of the camera but below the view stays the marker, off the view: " + below.X + "/" + below.Y);
        var behind = ObjectMarkingGeometry.MarkerScreen(false, 1280, 1800, rect);
        Check(Math.Abs(behind.X - 1280) < 1e-9 && Math.Abs(behind.Y - 812.5) < 1e-9, "a marker point behind the camera falls back to the center of the frame's rectangle");
    }

    // A crossing controller stands for its whole crossing: its frame encloses the grade-crossing setup it controls,
    // the masts with their gates, the bell and the pads below its root, and not only the mast nearest its marker. Its
    // whistle signs and its invisible detection spans stand below the same root but are no box of that frame. Every
    // other member of the setup keeps its own frame.
    static void CrossingControllerFramesItsSetup() {
        Check(ObjectMarkingGeometry.FramesWhole("Grade crossing controller") && !ObjectMarkingGeometry.FramesWhole("Crossing mast") && !ObjectMarkingGeometry.FramesWhole("Crossing bell") &&
            !ObjectMarkingGeometry.FramesWhole("Crossing pad marker") && !ObjectMarkingGeometry.FramesWhole("Generic track marker"),
            "a crossing controller is framed whole; the other members of its setup by the piece at their marker");
        // Gov Island: the controller's root holds two masts with their gates 12 m apart, a bell, a pad, a whistle sign
        // 300 m down the track and a detection span. Each renderer is listed with the families of the inventory objects
        // between it and the controller's root, nearest first.
        var renderers = new[] {
            new { Part = Box("mast 1", 0, 3, 100, .3, 3, .3), Families = new[] { "Crossing mast" } },
            new { Part = Box("gate 1", 3, 1, 100, 3, .2, .2), Families = new[] { "Crossing mast" } },
            new { Part = Box("mast 2", 12, 3, 100, .3, 3, .3), Families = new[] { "Crossing mast" } },
            new { Part = Box("bell", 0, 6, 100, .2, .2, .2), Families = new[] { "Crossing bell" } },
            new { Part = Box("pad", 6, 0, 100, 4, .1, 2), Families = new[] { "Crossing pad marker" } },
            new { Part = Box("pad mesh", 6, 0, 100, 4, .1, 2), Families = new string[0] },
            new { Part = Box("whistle sign", 300, 1.5, 100, .3, 1.5, .1), Families = new[] { "Generic track marker" } },
            new { Part = Box("detection span", -150, .1, 100, 150, .1, .1), Families = new[] { "Track span" } },
        };
        var counted = renderers.Where(r => ObjectMarkingGeometry.WholeCounts("Grade crossing controller", r.Families)).Select(r => r.Part).ToArray();
        Check(counted.Select(p => p.Name).SequenceEqual(new[] { "mast 1", "gate 1", "mast 2", "bell", "pad", "pad mesh" }),
            "the controller's frame counts its masts, gates, bell and pad, and leaves its whistle sign and detection span out: " + String.Join(", ", counted.Select(p => p.Name)));
        var whole = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, counted, Camera, View, null, ObjectMarkingGeometry.FrameDistance, null, ObjectMarkingGeometry.FramesWhole("Grade crossing controller"));
        Check(whole.Bounds.HasValue && whole.Bounds.Value.Min.X <= -.29 && whole.Bounds.Value.Max.X >= 12.29 && whole.Bounds.Value.Max.X < 20 && whole.Bounds.Value.Max.Y >= 6.19,
            "the controller's frame encloses both masts, the gate, the bell and the pad and stops short of the whistle sign: " + whole.Bounds);
        Check(whole.Marker.HasValue && Math.Abs(whole.Marker.Value.X - (whole.Bounds.Value.Min.X + whole.Bounds.Value.Max.X) / 2) < 1e-6,
            "the controller's square marker stands on the middle of its frame, as for any object");
        // Framed by the piece at its marker, as before, the frame stopped at the mast next to the marker.
        var piece = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, counted, Camera, View, null);
        Check(piece.Bounds.HasValue && piece.Bounds.Value.Max.X < 12, "framed by the piece at its marker, one mast is framed: " + piece.Bounds);
        // Nothing else framed whole leaves anything out: a top scene object keeps the track markers below it.
        Check(ObjectMarkingGeometry.WholeCounts("Scene object", new[] { "Generic track marker" }) && ObjectMarkingGeometry.WholeCounts("Scene object", new[] { "Track span" }),
            "a top scene object's frame keeps every object below it");
    }

    // The node of a crossing without signals stands for its crossing object: its frame encloses the crossbucks and the
    // pad, not only the pad next to its marker. At Alarka Jct the two crossbucks stand NW and SE of the pad.
    static void CrossingNodeFramesItsCrossbucksAndPad() {
        Check(ObjectMarkingGeometry.FramesWhole(CrossingObjectScene.Family), "a crossing without signals is framed whole");
        var boxes = new[] { Box("pad", 0, 0, 100, 5, .1, 2), Box("crossbuck NW", -8, 2, 108, .3, 2, .3), Box("crossbuck SE", 8, 2, 92, .3, 2, .3) };
        var whole = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, boxes, Camera, View, null, ObjectMarkingGeometry.FrameDistance, null, ObjectMarkingGeometry.FramesWhole(CrossingObjectScene.Family));
        Check(whole.Bounds.HasValue && whole.Bounds.Value.Min.X <= -8.29 && whole.Bounds.Value.Max.X >= 8.29 && whole.Bounds.Value.Min.Z <= 91.71 && whole.Bounds.Value.Max.Z >= 108.29,
            "the node's frame encloses both crossbucks and the pad: " + whole.Bounds);
        var piece = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, boxes, Camera, View, null);
        Check(piece.Bounds.HasValue && piece.Bounds.Value.Max.X < 6, "framed by the piece at its marker, as before, only the pad is framed: " + piece.Bounds);
        // At Old River Rd each crossing object also holds its whistle signs, Generic track markers up to 500 m down the
        // track. Counted, they set the boxes apart and the crossing had no frame at all; they stay out of its frame.
        var renderers = new[] {
            new { Part = Box("crossbuck (1)", 35, 2, 100, .3, 2, .3), Families = new[] { SceneFamilies.SceneObject } },
            new { Part = Box("crossbuck (2)", -14, 2, 100, .3, 2, .3), Families = new[] { SceneFamilies.SceneObject } },
            new { Part = Box("pad", 3, 0, 100, 14, .3, 7), Families = new[] { SceneFamilies.CrossingPad } },
            new { Part = Box("whistle sign E", 506, 1.3, 130, .2, 1.3, .3), Families = new[] { SceneFamilies.TrackMarker } },
            new { Part = Box("whistle sign W", 24, 1.3, 90, .2, 1.3, .2), Families = new[] { SceneFamilies.TrackMarker } },
        };
        var counted = renderers.Where(r => ObjectMarkingGeometry.WholeCounts(CrossingObjectScene.Family, r.Families)).Select(r => r.Part).ToArray();
        Check(counted.Select(p => p.Name).SequenceEqual(new[] { "crossbuck (1)", "crossbuck (2)", "pad" }), "a crossing object's frame counts its crossbucks and pad and leaves its whistle signs out: " + String.Join(", ", counted.Select(p => p.Name)));
        var oldRiver = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, counted, Camera, View, null, ObjectMarkingGeometry.FrameDistance, null, true);
        Check(oldRiver.Frame.HasValue && oldRiver.Bounds.Value.Min.X <= -14.29 && oldRiver.Bounds.Value.Max.X >= 35.29 && oldRiver.Bounds.Value.Max.X < 100, "the Old River Rd crossing gets a frame around its crossbucks and pad: " + oldRiver.Bounds);
        var spread = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, renderers.Select(r => r.Part).ToArray(), Camera, View, null, ObjectMarkingGeometry.FrameDistance, null, true);
        Check(!spread.Frame.HasValue, "with its whistle signs counted, its boxes stand apart and it gets no frame, as the game test saw");
    }

    // A renderer outlines its object whether or not the game's distance culling shows it now: on, or off only through
    // a culler that holds it, with every object above it on or off only through a culler. A renderer or object that the
    // scene or a modder switched off outlines nothing.
    static void RenderersOutlineWhateverTheCullingDoes() {
        // The box a renderer outlines with: its world box while it shows; while it is hidden, its own box from its mesh,
        // or, in a static batch, which keeps no mesh of its own, the box kept from when it last showed, carried along
        // with its object's current placement, so a patcher move takes the kept box with it; nothing when neither is
        // known.
        WorldPoint[] Cube(double x, double y, double z, double h) => Enumerable.Range(0, 8).Select(c => new WorldPoint(x + ((c & 1) == 0 ? -h : h), y + ((c & 2) == 0 ? -h : h), z + ((c & 4) == 0 ? -h : h))).ToArray();
        Func<WorldPoint, WorldPoint> Stay = p => p, Moved = p => new WorldPoint(p.X + 100, p.Y, p.Z);
        var seen = Cube(0, 5, 0, 5); var mesh = Cube(1, 1, 1, 1); var kept = Cube(0, 5, 0, 5);
        bool SameBox(IReadOnlyList<WorldPoint> a, IReadOnlyList<WorldPoint> b) => a != null && b != null && a.Count == 8 && b.Count == 8 && a.Zip(b, (p, q) => Math.Abs(p.X - q.X) + Math.Abs(p.Y - q.Y) + Math.Abs(p.Z - q.Z)).All(d => d < 1e-9);
        Check(SameBox(ObjectMarkingGeometry.RendererBox(true, seen, null, null, Stay), seen), "a renderer that shows outlines with its world box");
        Check(SameBox(ObjectMarkingGeometry.RendererBox(false, null, mesh, kept, Stay), mesh), "a hidden renderer outlines with its own box from its mesh");
        Check(SameBox(ObjectMarkingGeometry.RendererBox(false, null, null, kept, Stay), kept), "a hidden static-batched renderer outlines with the box kept from when it showed");
        Check(SameBox(ObjectMarkingGeometry.RendererBox(false, null, null, kept, Moved), Cube(100, 5, 0, 5)), "the kept box follows its object when the patcher moved it");
        Check(ObjectMarkingGeometry.RendererBox(false, null, null, null, Stay) == null, "a hidden static-batched renderer never seen showing outlines nothing");
        MarkingLink On = new MarkingLink(true, false), CullerOff = new MarkingLink(false, true), SceneOff = new MarkingLink(false, false);
        Check(ObjectMarkingGeometry.Outlines(true, false, new[] { On, On }), "a renderer that shows outlines its object");
        Check(ObjectMarkingGeometry.Outlines(false, true, new[] { On }), "a renderer that a renderer culler holds and hides outlines its object");
        Check(ObjectMarkingGeometry.Outlines(true, false, new[] { On, CullerOff, On }), "a renderer below an object that a culler switched off outlines its object");
        Check(ObjectMarkingGeometry.Outlines(false, true, new[] { CullerOff, On }), "a renderer that a culler hides, below an object a culler switched off, outlines its object");
        Check(!ObjectMarkingGeometry.Outlines(false, false, new[] { On }), "a renderer that the scene switched off outlines nothing");
        Check(!ObjectMarkingGeometry.Outlines(true, false, new[] { On, SceneOff }), "a renderer below an object that the scene or a modder switched off outlines nothing");
        // Whether an object that is off is off only through a culler: own culling holds it on, its simple culler or a
        // game-object culler that lists it, on any object above it as on a crossing's root, hides it now.
        Check(CullingActivity.OffThroughCuller(null, false, false, new KeyValuePair<bool, bool>[0]) == false, "an object without a culler is off through the scene");
        Check(CullingActivity.OffThroughCuller(null, true, false, new KeyValuePair<bool, bool>[0]), "an object its simple culler hides is off through the culler");
        Check(!CullingActivity.OffThroughCuller(null, true, true, new KeyValuePair<bool, bool>[0]), "an object off while its simple culler shows it was switched off by the scene or a modder");
        Check(CullingActivity.OffThroughCuller(null, false, false, new[] { new KeyValuePair<bool, bool>(false, false), new KeyValuePair<bool, bool>(true, false) }), "an object that a game-object culler above it lists and hides is off through the culler");
        Check(!CullingActivity.OffThroughCuller(null, false, false, new[] { new KeyValuePair<bool, bool>(true, true) }), "an object off while the game-object culler that lists it shows its objects was switched off");
        Check(CullingActivity.OffThroughCuller(true, false, false, null) && !CullingActivity.OffThroughCuller(false, true, false, null), "an object own culling switches is off through the culler exactly while the modder or the scene has it on");
    }

    public static void Run() {
        // A 10 m shed 100 m ahead: its front face, 95 m away, is 10/95 of the half view high, about 76 pixels, and a
        // margin of 4 pixels on each side.
        var shed = Frame(new WorldPoint(0, -5, 100), Box("shed", 0, 0, 100, 5, 5, 5));
        Check(shed.Frame.HasValue && shed.Frame.Value.Height > 80 && shed.Frame.Value.Height < 90 && Inside(shed.Frame.Value, 1280, 720),
            "a near object is framed around its own size");
        Check(Math.Abs(shed.Distance - 95) < 1e-9, "the distance is measured to the object's bounds");

        // Part exclusion, in world metres. A culling or proxy renderer 800 m wide beside the shed, and a box of the same
        // object 40 m away, such as a sign far from its post, stay out of the frame; a roof touching the walls joins.
        var decisions = new List<string>();
        var boxes = new[] {
            Box("walls", 0, 0, 100, 5, 5, 5), Box("roof", 0, 6, 100, 5.5f, 1, 5.5f),
            Box("proxy", 0, 0, 100, 400, 50, 400), Box("sign", 40, 0, 100, 1, 1, .1f),
            Box("smoke", 0, 20, 100, 30, 30, 30, ObjectMarkingBoxKind.Effect), Box("shadow", 0, 0, 100, 5, 5, 5, ObjectMarkingBoxKind.ShadowsOnly),
            Box("lod1", 0, 0, 100, 5, 5, 5, ObjectMarkingBoxKind.LowerLod)
        };
        var joined = ObjectMarkingGeometry.Frame(new WorldPoint(0, -5, 100), Eye, boxes, Camera, View, decisions);
        Check(joined.Frame.HasValue && joined.Frame.Value.Height > shed.Frame.Value.Height && joined.Frame.Value.Height < 2.5 * shed.Frame.Value.Height,
            "the roof joins the walls and the proxy does not stretch the frame");
        Check(joined.Frame.Value.Right < 1280 + 400, "the sign 40 m away stays outside the frame");
        foreach (var expected in new[] { "walls: Used for the frame.", "roof: Used for the frame.", "proxy: Not used for the frame because its box is much larger than the box at the marker.",
            "sign: Not used for the frame because its box is too far from the boxes at the marker.", "smoke: Not used for the frame because it is an effect.", "shadow: Not used for the frame because it draws only shadows.", "lod1: Not used for the frame because it is a lower level of detail." })
            Check(decisions.Contains(expected), "decision recorded: " + expected + " in " + String.Join("; ", decisions));
        Check(Frame(new WorldPoint(0, -5, 100), Box("walls", 0, 0, 100, 5, 5, 5), Box("porch", 0, -5.5, 106, 3, 1, 1.5f)).Frame.Value.Bottom >
            shed.Frame.Value.Bottom, "a box within the join distance joins the frame");
        // A large building is not excluded for its size when the marker box is large too.
        Check(Frame(new WorldPoint(0, -5, 100), Box("hall", 0, 0, 100, 30, 10, 30), Box("tower", 0, 20, 100, 5, 10, 5)).Frame.HasValue,
            "boxes of comparable size join");

        // Near-plane clipping. Close up, the camera stands inside the shed's porch: the porch is clipped at the near
        // plane instead of being skipped, so the frame covers more than the view, and it is kept.
        var inside = Frame(new WorldPoint(0, -2, 3), Box("porch", 0, 0, 3, 4, 3, 4));
        Check(inside.Frame.HasValue && inside.Frame.Value.X < 0 && inside.Frame.Value.Right > View.Width && inside.Frame.Value.Y < 0 && inside.Frame.Value.Bottom > View.Height,
            "a close-up frame larger than the view is kept");
        Check(inside.Distance == 0, "a camera inside the bounds is at distance zero");
        var behind = Frame(new WorldPoint(0, 0, -50), Box("behind", 0, 0, -50, 2, 2, 2));
        Check(!behind.Frame.HasValue, "an object wholly behind the camera has no frame");

        // A frame is never smaller than its marker with the margin: a small frame grows about its own centre.
        Check(ObjectMarkingGeometry.MarkerSize == 24 && ObjectMarkingGeometry.MarkerMargin == 4, "the marker is 24 pixels with a margin of 4");
        foreach (var row in new[] {
            new double[] { 100, 100, 110, 120, 89, 94, 121, 126 },   // 10 by 20: both grow to 32
            new double[] { 100, 100, 200, 110, 100, 89, 200, 121 },  // 100 by 10: only the height grows
            new double[] { 0, 0, 32, 32, 0, 0, 32, 32 },             // exactly the least size: kept
            new double[] { 10, 20, 310, 220, 10, 20, 310, 220 } }) { // larger: kept
            var grown = ObjectMarkingGeometry.VisibleFrame(ScreenRect.FromEdges((float)row[0], (float)row[1], (float)row[2], (float)row[3]));
            Check(grown.X == row[4] && grown.Y == row[5] && grown.Right == row[6] && grown.Bottom == row[7], "a frame of " + (row[2] - row[0]) + " by " + (row[3] - row[1]) + " is drawn as " + grown.Width + " by " + grown.Height + " at " + grown.X + "," + grown.Y);
        }
        // A label stands centred under its marker; labels of markers on one spot stack under it; without room below
        // the label stands above; it stays inside the screen.
        var screen = ScreenRect.FromEdges(0, 0, 1000, 800);
        var mark = ScreenRect.FromEdges(488, 388, 512, 412);
        var none = new ScreenRect[0];
        var under = ObjectMarkingGeometry.LabelUnder(mark, 100, 30, screen, none);
        Check(under.X == 450 && under.Right == 550 && under.Y == 416 && under.Bottom == 446, "a label is centred under its marker, 4 pixels below it");
        var second = ObjectMarkingGeometry.LabelUnder(mark, 140, 30, screen, new[] { under });
        Check(second.X == 430 && second.Y == 448, "the label of a second marker on the same spot stacks under the first, centred on its own marker");
        var third = ObjectMarkingGeometry.LabelUnder(ScreenRect.FromEdges(500, 392, 524, 416), 100, 30, screen, new[] { under, second });
        Check(third.X == 462 && third.Y == 480, "a third label goes under both, whatever the order they are listed in");
        var apart = ObjectMarkingGeometry.LabelUnder(ScreenRect.FromEdges(700, 388, 724, 412), 100, 30, screen, new[] { under, second });
        Check(apart.X == 662 && apart.Y == 416, "a label whose marker stands apart keeps its own place");
        var low = ObjectMarkingGeometry.LabelUnder(ScreenRect.FromEdges(488, 770, 512, 794), 100, 30, screen, none);
        Check(low.Y == 736 && low.Bottom == 766 && low.X == 450, "without room below, the label stands centred above its marker");
        var lowSecond = ObjectMarkingGeometry.LabelUnder(ScreenRect.FromEdges(488, 770, 512, 794), 100, 30, screen, new[] { low });
        Check(lowSecond.Bottom == 734, "labels above a marker stack upward");
        var edge = ObjectMarkingGeometry.LabelUnder(ScreenRect.FromEdges(0, 100, 24, 124), 100, 30, screen, none);
        Check(edge.X == 0 && edge.Y == 128, "a label at the screen's edge stays inside the screen");
        var right = ObjectMarkingGeometry.LabelUnder(ScreenRect.FromEdges(980, 100, 1004, 124), 100, 30, screen, none);
        Check(right.Right == 1000, "also at the right edge");

        // A label covers no native panel and no square marker of another object: it stands at the nearest place that
        // covers none.
        var panel = ScreenRect.FromEdges(400, 420, 560, 700);
        var clear = ObjectMarkingGeometry.LabelUnder(mark, 100, 30, screen, none, new[] { ScreenRect.FromEdges(0, 0, 50, 50) });
        Check(clear.X == under.X && clear.Y == under.Y, "a label that covers nothing blocked keeps its place under the marker");
        var beside = ObjectMarkingGeometry.LabelUnder(mark, 100, 30, screen, none, new[] { panel });
        Check(!(beside.X < panel.Right && panel.X < beside.Right && beside.Y < panel.Bottom && panel.Y < beside.Bottom), "a label that would be cut off by a native panel stands clear of it: " + beside.X + "," + beside.Y);
        Check(beside.Y == 354 && beside.X == 450, "the nearest clear place is centred above the marker here");
        var neighbour = ScreenRect.FromEdges(520, 414, 544, 438);
        var aside = ObjectMarkingGeometry.LabelUnder(mark, 100, 30, screen, none, new[] { neighbour });
        Check(!(aside.X < neighbour.Right && neighbour.X < aside.Right && aside.Y < neighbour.Bottom && neighbour.Y < aside.Bottom) && aside.Y == 416 && aside.Right == 516, "a label does not cover the square marker of a neighbour: it moves aside under its own marker (" + aside.X + "," + aside.Y + ")");
        var hemmed = ObjectMarkingGeometry.LabelUnder(mark, 100, 30, ScreenRect.FromEdges(440, 380, 560, 450), none, new[] { ScreenRect.FromEdges(440, 380, 560, 450) });
        Check(hemmed.Width == 100 && hemmed.Height == 30, "a label that fits nowhere keeps a place");

        // Every inventory object within the frame distance has a frame: one without a box to frame takes the least
        // frame about its marker.
        var bare = ObjectMarkingGeometry.MarkerOutline(300, 200);
        Check(bare.Count == 4 && bare.Min(q => q.X) == 284 && bare.Max(q => q.X) == 316 && bare.Min(q => q.Y) == 184 && bare.Max(q => q.Y) == 216, "an object without boxes is framed 32 by 32 about its marker");
        Check(ObjectMarkingGeometry.Contains(bare, 300, 200) && ObjectMarkingGeometry.Contains(bare, 315, 215) && !ObjectMarkingGeometry.Contains(bare, 317, 200), "that frame is picked like any other");
        Check(ObjectMarkingGeometry.FramesMarker(0) && ObjectMarkingGeometry.FramesMarker(500) && !ObjectMarkingGeometry.FramesMarker(500.1) && !ObjectMarkingGeometry.FramesMarker(Double.NaN), "it shows within the frame distance of 500 m and not beyond");
        Check(ReferenceEquals(ObjectMarkingGeometry.VisibleOutline(bare), bare), "it is the least frame already");

        // A neighbour's marker that overlaps the label's own marker in box still blocks the label: the label covers
        // no box of it. Here the neighbour stands lower right of the marker, where the label under the marker would
        // cover its lower half.
        var partly = ScreenRect.FromEdges(506, 404, 530, 428);
        var kept = ObjectMarkingGeometry.LabelUnder(mark, 100, 30, screen, none, new[] { partly });
        Check(!(kept.X < partly.Right && partly.X < kept.Right && kept.Y < partly.Bottom && partly.Y < kept.Bottom), "a label covers no box of a neighbouring marker that overlaps its own: " + kept.X + "," + kept.Y);
        // Only what lies near the label counts: hundreds of markers elsewhere in the view change nothing and are not
        // tested against every place.
        var crowd = new List<ScreenRect> { partly };
        for (int i = 0; i < 700; i++) crowd.Add(ScreenRect.FromEdges(10 + (i % 35) * 28, 500 + (i / 35) * 14, 34 + (i % 35) * 28, 524 + (i / 35) * 14));
        var crowded = ObjectMarkingGeometry.LabelUnder(mark, 100, 30, screen, none, crowd);
        Check(crowded.X == kept.X && crowded.Y == kept.Y, "markers far from the label do not move it");
        // The cost does not grow with the square of the markers in view. The bound is generous on purpose: a loaded
        // runner passes it, and the quadratic search it guards against, about a million overlap tests a label, does not.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) ObjectMarkingGeometry.LabelUnder(mark, 100, 30, screen, none, crowd);
        Check(clock.Elapsed.TotalSeconds < 20, "placing a label 200 times among 700 markers stays far under the quadratic cost: " + clock.Elapsed.TotalMilliseconds + " ms");
        // Every place that is tried is tested against everything that blocks: a place beside the first blocker that
        // a second blocker covers is not taken, and the search goes on beside that one.
        var chain = new[] { ScreenRect.FromEdges(506, 404, 530, 428), ScreenRect.FromEdges(380, 410, 400, 450), ScreenRect.FromEdges(430, 300, 580, 386) };
        var chained = ObjectMarkingGeometry.LabelUnder(mark, 100, 30, screen, none, chain);
        Check(chain.All(c => !(chained.X < c.Right && c.X < chained.Right && chained.Y < c.Bottom && c.Y < chained.Bottom)), "a label finds a place that none of a chain of blockers covers: " + chained.X + "," + chained.Y);
        Check(ObjectMarkingGeometry.LabelRounds == 4, "places beside newly found blockers are tried for four rounds");

        // The share of a pass for reads that are made once. Only the reads are timed; a pass starts anew; what was
        // read is kept by the caller, so the reads come to an end however slow the rest of the pass is.
        double now = 0;
        var budget = new ReadBudget(4, () => now);
        var known = new HashSet<int>();
        int Pass(int objects, double otherWork) {
            budget.Begin();
            var waiting = 0;
            for (int i = 0; i < objects; i++) {
                now += otherWork;                       // work on what is known already: not a read
                if (known.Contains(i)) continue;
                if (budget.Exhausted) { waiting++; continue; }
                budget.Read(() => { now += 1.5; known.Add(i); return i; });
            }
            return waiting;
        }
        Check(!budget.Exhausted && budget.Spent == 0, "a budget starts unused");
        var first = Pass(20, 0);
        Check(known.Count == 3 && first == 17 && budget.Exhausted && budget.Spent == 4.5, "a pass reads until its share of 4 ms is used up: three reads of 1.5 ms, the rest waits");
        var passes = 1;
        while (known.Count < 20 && passes < 50) { Pass(20, 0); passes++; }
        Check(known.Count == 20 && passes == 7, "every pass reads further, and the reads come to an end: " + passes + " passes of three reads for 20 reads");
        Check(Pass(20, 0) == 0 && budget.Spent == 0, "a pass with everything known reads nothing and waits for nothing");
        // Work that is no read does not use the share: a pass that is slow for another reason still reads.
        known.Clear();
        Check(Pass(20, 10) == 17 && known.Count == 3 && budget.Spent == 4.5, "200 ms of other work in the pass takes nothing from the share of the reads");
        // A value that needs no read, such as a renderer that is switched off and passed over, never waits.
        budget.Begin(); budget.Read(() => { now += 100; return 0; });
        var skipped = 0; for (int i = 0; i < 5; i++) { if (i % 2 == 0) { skipped++; continue; } if (budget.Exhausted) continue; }
        Check(budget.Exhausted && skipped == 3, "with the share used up, what needs no read is still passed over without waiting");
        budget.Begin();
        Check(!budget.Exhausted, "the next pass starts with its whole share, whatever the pass before took");
        Check(new ReadBudget(4, () => now).Read(() => "x") == "x", "a read returns its value");

        // The click rule. Each row: the pointer, the candidates (marker centre x, y, marker shown, pointer inside
        // its frame or ribbon), and the candidates the click selects, the nearest first.
        PickTarget T(double x, double y, bool marked = true, bool area = false) => new PickTarget(x, y, marked, area);
        var picks = new[] {
            new { Name = "a click on a marker selects its object", X = 100.0, Y = 100.0, Targets = new[] { T(104, 100), T(300, 300) }, Picked = "0" },
            new { Name = "a marker inside another object's frame wins over that frame", X = 100.0, Y = 100.0, Targets = new[] { T(400, 400, true, true), T(104, 100) }, Picked = "1" },
            new { Name = "of two markers under the pointer the nearest centre wins", X = 110.0, Y = 100.0, Targets = new[] { T(100, 100), T(118, 100) }, Picked = "1" },
            new { Name = "and the other one from its own side", X = 106.0, Y = 100.0, Targets = new[] { T(100, 100), T(118, 100) }, Picked = "0" },
            new { Name = "inside frames but on no marker, the nearest marker on screen wins", X = 200.0, Y = 200.0, Targets = new[] { T(100, 200, true, true), T(260, 200, true, true), T(230, 200, true, false) }, Picked = "1" },
            new { Name = "screen distance decides, whatever the order", X = 200.0, Y = 200.0, Targets = new[] { T(260, 200, true, true), T(100, 200, true, true) }, Picked = "0" },
            new { Name = "markers on one spot cannot be told apart", X = 100.0, Y = 100.0, Targets = new[] { T(100, 100), T(100, 100), T(300, 100) }, Picked = "0,1" },
            new { Name = "markers closer than half a marker are one spot, the nearest first", X = 108.0, Y = 100.0, Targets = new[] { T(100, 100), T(111, 100) }, Picked = "1,0" },
            new { Name = "markers half a marker apart are two spots", X = 108.0, Y = 100.0, Targets = new[] { T(100, 100), T(112, 100) }, Picked = "1" },
            new { Name = "a frame click also brings the markers on the winner's spot", X = 200.0, Y = 200.0, Targets = new[] { T(240, 200, true, true), T(244, 203, true, false), T(100, 200, true, true) }, Picked = "0,1" },
            new { Name = "a small object nested in a large frame wins near its own marker", X = 310.0, Y = 205.0, Targets = new[] { T(500, 300, true, true), T(300, 200, true, true) }, Picked = "1" },
            new { Name = "and the large object wins where its own marker is nearer", X = 480.0, Y = 300.0, Targets = new[] { T(500, 300, true, true), T(300, 200, true, false) }, Picked = "0" },
            new { Name = "three nested frames: only the nearest marker is picked, one at a time", X = 210.0, Y = 200.0, Targets = new[] { T(500, 300, true, true), T(200, 200, true, true), T(260, 200, true, true) }, Picked = "1" },
            new { Name = "a pad without a frame of its own is picked on its marker, inside other frames", X = 405.0, Y = 300.0, Targets = new[] { T(500, 300, true, true), T(400, 300, true, false), T(340, 300, true, true) }, Picked = "1" },
            new { Name = "and beside its marker the framed neighbour whose marker is nearest wins", X = 380.0, Y = 300.0, Targets = new[] { T(500, 300, true, true), T(400, 300, true, false), T(340, 300, true, true) }, Picked = "2" },
            new { Name = "a marker that is not shown takes no click", X = 100.0, Y = 100.0, Targets = new[] { T(100, 100, false, false), T(300, 300) }, Picked = "" },
            new { Name = "a track without a shown marker is picked by its ribbon", X = 100.0, Y = 100.0, Targets = new[] { T(180, 100, false, true), T(120, 100, false, true) }, Picked = "1" },
            new { Name = "a click on nothing selects nothing", X = 100.0, Y = 100.0, Targets = new[] { T(300, 300), T(500, 500) }, Picked = "" },
            new { Name = "the marker's edge still counts", X = 112.0, Y = 88.0, Targets = new[] { T(100, 100) }, Picked = "0" },
            new { Name = "just outside the marker does not", X = 113.0, Y = 100.0, Targets = new[] { T(100, 100) }, Picked = "" },
        };
        foreach (var pick in picks) {
            var picked = String.Join(",", WorldPick.Resolve(pick.X, pick.Y, pick.Targets));
            Check(picked == pick.Picked, "click rule: " + pick.Name + " (expected " + pick.Picked + ", got " + picked + ")");
        }
        Check(WorldPick.SameSpot == 12 && WorldPick.Resolve(0, 0, null).Count == 0, "one spot is half a marker; no candidates select nothing");
        // The list of one spot stands beside the click.
        var rowsBeside = WorldPick.ListBeside(500, 300, 200, new[] { 30.0, 30.0, 30.0 }, screen);
        Check(rowsBeside.Count == 3 && rowsBeside[0].X == 516 && rowsBeside[0].Y == 300 && rowsBeside[1].Y == 332 && rowsBeside[2].Bottom == 394 && rowsBeside[2].Right == 716, "the list opens right of the click, rows under one another");
        var rowsLeft = WorldPick.ListBeside(900, 300, 200, new[] { 30.0, 30.0 }, screen);
        Check(rowsLeft[0].Right == 884 && rowsLeft[0].X == 684, "without room on the right the list opens left of the click");
        var rowsUp = WorldPick.ListBeside(500, 790, 200, new[] { 30.0, 30.0 }, screen);
        Check(rowsUp[1].Bottom == 800 && rowsUp[0].Y == 738, "a list at the bottom moves up into the screen");

        // The frame is the outline of the object's own box on screen, not a rectangle around it. A slab 20 m long
        // and 2 m wide, lying flat and turned 45 degrees, seen from above at a slant: the rectangle around it is far
        // larger than the slab; the outline is not.
        var slant = Strategy(new WorldPoint(0, 40, -40), 45, 60);
        WorldPoint[] Turned(double cx, double cy, double cz, double halfLength, double halfWidth, double halfHeight) {
            var corners = new WorldPoint[8]; double c = Math.Sqrt(.5);
            for (int i = 0; i < 8; i++) {
                double a = ((i & 1) == 0 ? -1 : 1) * halfLength, h = ((i & 2) == 0 ? -1 : 1) * halfHeight, b = ((i & 4) == 0 ? -1 : 1) * halfWidth;
                corners[i] = new WorldPoint(cx + (a - b) * c, cy + h, cz + (a + b) * c);
            }
            return corners;
        }
        double Area(IReadOnlyList<ScreenPoint> polygon) { double sum = 0; for (int i = 0; i < polygon.Count; i++) { var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count]; sum += a.X * b.Y - b.X * a.Y; } return Math.Abs(sum) / 2; }
        var slab = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 0), new WorldPoint(0, 40, -40),
            new[] { new ObjectMarkingBox("slab", new WorldPoint(0, 0, 0), new WorldPoint(7.8, .1, 7.8), Turned(0, 0, 0, 10, 1, .1)) }, slant, View, new List<string>());
        Check(slab.Frame.HasValue && slab.Outline != null && slab.Outline.Count >= 4, "a framed object has an outline");
        var slabRect = slab.Frame.Value;
        Check(Area(slab.Outline) < .4 * slabRect.Width * slabRect.Height, "the outline of a slab that lies at an angle covers far less than the rectangle around it: " + Area(slab.Outline) + " of " + slabRect.Width * slabRect.Height);
        Check(slab.Outline.All(q => q.X >= slabRect.X - .01 && q.X <= slabRect.Right + .01 && q.Y >= slabRect.Y - .01 && q.Y <= slabRect.Bottom + .01), "the outline lies within its rectangle, which is the rectangle around it");
        double midX = (slabRect.X + slabRect.Right) / 2, midY = (slabRect.Y + slabRect.Bottom) / 2;
        Check(ObjectMarkingGeometry.Contains(slab.Outline, midX, midY), "the marker's place, the centre of the rectangle, lies inside the outline");
        Check(!ObjectMarkingGeometry.Contains(slab.Outline, slabRect.X + 6, slabRect.Y + 6) || !ObjectMarkingGeometry.Contains(slab.Outline, slabRect.Right - 6, slabRect.Y + 6), "a corner of the rectangle that the slab does not reach is outside the outline: a click there does not pick the slab");
        // Picking and drawing share the outline: the line lies inside it, along every edge.
        var stroke = ObjectMarkingGeometry.OutlineStroke(slab.Outline, 2);
        Check(stroke.Count == 2 * slab.Outline.Count && stroke.All(t => new[] { t.A, t.B, t.C }.All(q => q.X >= slabRect.X - .01 && q.X <= slabRect.Right + .01 && q.Y >= slabRect.Y - .01 && q.Y <= slabRect.Bottom + .01)), "the drawn line is two triangles an edge and stays inside the frame");
        // A square outline: points inside, on and outside it.
        var square = new[] { new ScreenPoint(0, 0), new ScreenPoint(10, 0), new ScreenPoint(10, 10), new ScreenPoint(0, 10) };
        foreach (var row in new[] { new[] { 5.0, 5, 1 }, new[] { 0.0, 0, 1 }, new[] { 10.0, 5, 1 }, new[] { 10.1, 5, 0 }, new[] { -1.0, -1, 0 }, new[] { 5.0, 11, 0 } })
            Check(ObjectMarkingGeometry.Contains(square, row[0], row[1]) == (row[2] == 1), "the point " + row[0] + "," + row[1] + " is " + (row[2] == 1 ? "inside" : "outside") + " the square");
        Check(!ObjectMarkingGeometry.Contains(null, 0, 0) && !ObjectMarkingGeometry.Contains(new[] { new ScreenPoint(0, 0), new ScreenPoint(1, 1) }, 0, 0), "no outline holds no point");
        // The least-frame rule holds for an outline: one smaller than the marker with its margin grows about its centre.
        var tiny = ObjectMarkingGeometry.VisibleOutline(new[] { new ScreenPoint(100, 100), new ScreenPoint(110, 100), new ScreenPoint(110, 120), new ScreenPoint(100, 120) });
        Check(tiny.Min(q => q.X) == 89 && tiny.Max(q => q.X) == 121 && tiny.Min(q => q.Y) == 94 && tiny.Max(q => q.Y) == 126, "an outline of 10 by 20 is drawn at least 32 by 32 about its centre");
        Check(ReferenceEquals(ObjectMarkingGeometry.VisibleOutline(square.Select(q => new ScreenPoint(q.X * 10, q.Y * 10)).ToArray()), null) == false && ObjectMarkingGeometry.VisibleOutline(slab.Outline).Count == slab.Outline.Count, "a larger outline is kept");
        // A post half under the ground: the buried half is not framed. The post stands 100 m ahead, from 5 m below
        // the camera's height to 5 m above; the ground lies at the camera's height.
        ObjectMarkingFrame Post(double? ground, double halfWidth = .5) => ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye,
            new[] { new ObjectMarkingBox("post", new WorldPoint(0, 0, 100), new WorldPoint(halfWidth, 5, halfWidth), null, ObjectMarkingBoxKind.Mesh, ground.HasValue ? (x, z) => ground : (Func<double, double, double?>)null) }, Camera, View, new List<string>());
        var whole = Post(null).Frame.Value; var cut = Post(0).Frame.Value;
        Check(Math.Abs((whole.Height - 2 * ObjectMarkingGeometry.Margin) - 2 * (cut.Height - 2 * ObjectMarkingGeometry.Margin)) < 1 && cut.Y == whole.Y && cut.Bottom < whole.Bottom - 20, "a post half under the ground is framed above the ground only: " + whole.Height + " and " + cut.Height);
        Check(Post(-20).Frame.Value.Height == whole.Height, "ground below the post cuts nothing");
        Check(Post(20).Frame.Value.Height == whole.Height, "a post wholly under the surface above it, as in a tunnel, is framed whole");
        // A wide box is cut as well, whatever its width: the 16 m slab half under level ground loses its lower half.
        var wideWhole = Post(null, 8).Frame.Value; var wideCut = Post(0, 8).Frame.Value;
        Check(wideCut.Y == wideWhole.Y && wideCut.Bottom < wideWhole.Bottom - 20, "a wide box is cut at the surface like a narrow one");
        // A platform 40 m long on a slope: the ground rises from 2 m under the camera's height at its left end to 2 m
        // above it at its right end. The platform reaches from 3 m under to 3 m above. Its frame is one box: the
        // platform's box with its lower face raised to the lowest ground under the pieces that stand above it, 1.5 m
        // under the camera's height under the leftmost of its four 10 m pieces. Both ends reach equally low.
        Func<double, double, double?> slope = (x, z) => x / 10;
        var platform = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye,
            new[] { new ObjectMarkingBox("platform", new WorldPoint(0, 0, 100), new WorldPoint(20, 3, 2), null, ObjectMarkingBoxKind.Mesh, slope) }, Camera, View, new List<string>());
        var level = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye,
            new[] { new ObjectMarkingBox("platform", new WorldPoint(0, 0, 100), new WorldPoint(20, 3, 2), null, ObjectMarkingBoxKind.Mesh, (x, z) => 0) }, Camera, View, new List<string>());
        double Lowest(ObjectMarkingFrame frame, bool leftHalf) { var mid = (frame.Frame.Value.X + frame.Frame.Value.Right) / 2; return frame.Outline.Where(q => leftHalf ? q.X < mid - 40 : q.X > mid + 40).Max(q => q.Y); }
        Check(Math.Abs(Lowest(platform, true) - Lowest(platform, false)) < 1, "a long platform on a slope is framed by one box: both ends reach equally low (" + Lowest(platform, true) + " and " + Lowest(platform, false) + ")");
        Check(Math.Abs(Lowest(level, true) - Lowest(level, false)) < 1, "on level ground both ends are cut at one height");
        Check(Lowest(platform, true) > Lowest(level, true) + 5, "the box keeps all that stands above the lowest ground, more than on level ground: " + Lowest(platform, true) + " " + Lowest(level, true));
        Check(SameOutline(platform.Outline, Unground(new WorldPoint(0, .75, 100), new WorldPoint(20, 2.25, 2)).Outline), "the platform's frame is the outline of its box raised to 1.5 m under the camera's height");
        // A heap 16 m wide, half buried in a slope that rises along both X and Z: the ground under its four pieces lies
        // at -1, 0, 0 and +1 m; it reaches from 2.5 m under to 0.5 m above. The piece on the high corner lies wholly under
        // the ground; the others stand above it. The frame is one box from the lowest of those grounds, -1 m, to the
        // heap's top, never a hull that follows the slope.
        Func<double, double, double?> tilted = (x, z) => (x + (z - 100)) / 8;
        var heap = ObjectMarkingGeometry.Frame(new WorldPoint(0, -1, 100), Eye,
            new[] { new ObjectMarkingBox("heap", new WorldPoint(0, -1, 100), new WorldPoint(8, 1.5, 8), null, ObjectMarkingBoxKind.Mesh, tilted) }, Camera, View, new List<string>());
        Check(SameOutline(heap.Outline, Unground(new WorldPoint(0, -.25, 100), new WorldPoint(8, .75, 8)).Outline), "a half-buried heap on a slope is framed by one box from the lowest ground under what stands above to its top");
        // A heap turned as Andrews' Coal & Oil / Heap (1) is: rotation X 270, Y 93.536, Z 0, so its local Z axis points
        // up. It is 12 m by 6 m and 2.4 m high along that axis, with its centre 0.4 m above level ground, so 0.8 m of it
        // lies under the ground. Its frame keeps the height from the ground to its top: the same frame as its box from
        // the ground up, and well higher on screen than a flat outline.
        WorldPoint[] HeapBox(double bottom, double top) {
            double yaw = 93.536 * Math.PI / 180, cosine = Math.Cos(yaw), sine = Math.Sin(yaw);
            var corners = new WorldPoint[8];
            for (int i = 0; i < 8; i++) {
                double lx = ((i & 1) == 0 ? -6 : 6), ly = ((i & 2) == 0 ? -3 : 3), lz = (i & 4) == 0 ? bottom : top;
                // X 270 turns local Z to world up and local Y to world -Z; the yaw then turns about world up.
                double x = lx, y = lz, z = -ly;
                corners[i] = new WorldPoint(x * cosine + z * sine, -1 + y, 100 - x * sine + z * cosine);
            }
            return corners;
        }
        ObjectMarkingFrame Turned270(Func<double, double, double?> ground, double bottom, double top) => ObjectMarkingGeometry.Frame(new WorldPoint(0, -1, 100), Eye,
            new[] { new ObjectMarkingBox("heap", new WorldPoint(0, -1 + (bottom + top) / 2, 100), new WorldPoint(6.2, (top - bottom) / 2, 6.2), HeapBox(bottom, top), ObjectMarkingBoxKind.Mesh, ground) }, Camera, View, new List<string>());
        // Level ground 1 m under the camera's height; the heap reaches from 0.8 m under it to 1.6 m above it.
        var heap270 = Turned270((x, z) => -1, -.8, 1.6); var heapAbove = Turned270(null, 0, 1.6);
        Check(SameOutline(heap270.Outline, heapAbove.Outline), "a heap turned X 270 and Y 93.536 is framed by its box from the ground to its top");
        Check(heap270.Frame.Value.Height - 2 * ObjectMarkingGeometry.Margin > 10, "the frame of the turned heap has its height on screen, not a flat outline: " + heap270.Frame.Value.Height);
        // A collider of an inventory object is never the ground, whatever its layer: the heap's own collider on the
        // terrain layer would otherwise raise the frame to the heap's top.
        Check(ObjectMarkingGeometry.HitKind(true, true) == ObjectMarkingGeometry.SurfaceHit.None && ObjectMarkingGeometry.HitKind(false, true) == ObjectMarkingGeometry.SurfaceHit.None
            && ObjectMarkingGeometry.HitKind(true, false) == ObjectMarkingGeometry.SurfaceHit.Terrain && ObjectMarkingGeometry.HitKind(false, false) == ObjectMarkingGeometry.SurfaceHit.Ballast, "only terrain and ballast outside the inventory are the ground");
        var onItself = Turned270((x, z) => .5, -.8, 1.6);
        Check(onItself.Frame.Value.Height < heap270.Frame.Value.Height - 10, "taken for the ground, the heap's own top would flatten its frame, which the hit rule prevents: " + onItself.Frame.Value.Height);
        // A grouped object on the same slope: one box around both boxes, cut the same way.
        var pair = ObjectMarkingGeometry.Frame(new WorldPoint(0, -1, 100), Eye, new[] {
            new ObjectMarkingBox("heap", new WorldPoint(-4, -1, 100), new WorldPoint(4, 1.5, 8), null, ObjectMarkingBoxKind.Mesh, tilted),
            new ObjectMarkingBox("heap (1)", new WorldPoint(4, -1, 100), new WorldPoint(4, 1.5, 8), null, ObjectMarkingBoxKind.Mesh, tilted) }, Camera, View, new List<string>());
        Check(SameOutline(pair.Outline, heap.Outline), "two boxes on a slope are framed by the same one box as the box that covers them both");
        // A bridge over a valley: the ground lies far under its middle and at deck height under its ends. Nothing of
        // the deck above the ground is lost, and the frame is as wide as the whole bridge.
        Func<double, double, double?> valley = (x, z) => Math.Abs(x) > 15 ? -1 : -30;
        ObjectMarkingFrame Bridge(Func<double, double, double?> ground) => ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye,
            new[] { new ObjectMarkingBox("deck", new WorldPoint(0, 0, 100), new WorldPoint(20, 1, 2), null, ObjectMarkingBoxKind.Mesh, ground) }, Camera, View, new List<string>());
        var open = Bridge(null).Frame.Value; var over = Bridge(valley).Frame.Value;
        Check(over.X == open.X && over.Right == open.Right && over.Y == open.Y && Math.Abs(over.Bottom - open.Bottom) < .01, "a bridge deck over a valley keeps its whole frame: the ground is under it everywhere");
        // A grouped object: its frame is the union of its cut boxes, one outline.
        var grouped = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, new[] {
            new ObjectMarkingBox("post", new WorldPoint(0, 0, 100), new WorldPoint(.5, 5, .5), null, ObjectMarkingBoxKind.Mesh, (x, z) => 0),
            new ObjectMarkingBox("sign", new WorldPoint(0, 4, 100), new WorldPoint(1.5, 1, .2), null, ObjectMarkingBoxKind.Mesh, (x, z) => 0) }, Camera, View, new List<string>());
        Check(grouped.Outline != null && grouped.Frame.Value.Width > cut.Width + 10 && Math.Abs(grouped.Frame.Value.Bottom - cut.Bottom) < .01 && grouped.Frame.Value.Y == cut.Y, "a grouped object has one outline around its cut boxes: as wide as the sign, ending at the ground like the post");
        // A piece wholly under the surface is buried while another piece stands above it.
        Func<double, double, double?> bank = (x, z) => x > 0 ? 50 : -50;
        var banked = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye,
            new[] { new ObjectMarkingBox("wall", new WorldPoint(0, 0, 100), new WorldPoint(20, 3, 2), null, ObjectMarkingBoxKind.Mesh, bank) }, Camera, View, new List<string>());
        var free = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye,
            new[] { new ObjectMarkingBox("wall", new WorldPoint(0, 0, 100), new WorldPoint(20, 3, 2), null, ObjectMarkingBoxKind.Mesh, null) }, Camera, View, new List<string>());
        Check(banked.Frame.Value.X == free.Frame.Value.X && banked.Frame.Value.Right < free.Frame.Value.Right - 50, "the half of a wall that lies inside a bank is not framed; the half that stands free is");
        Check(ObjectMarkingGeometry.GroundPiece == 10 && ObjectMarkingGeometry.GroundPieces == 5, "a box is read piece by piece, 10 m long, at most 5 along an axis");
        // The surface an object stands in, from what a ray from above meets: terrain, or the ballast of track that
        // lies on it. Track carried over the terrain, as on a bridge deck, is no such surface.
        foreach (var row in new[] {
            new object[] { 100.0, new double[0], 100.0, "terrain alone" },
            new object[] { 100.0, new[] { 100.5 }, 100.5, "ballast on the terrain" },
            new object[] { 100.0, new[] { 101.0 }, 101.0, "ballast at the most 1 m above the terrain" },
            new object[] { 100.0, new[] { 101.1 }, 100.0, "track carried more than 1 m over the terrain" },
            new object[] { 70.0, new[] { 101.0 }, 70.0, "track on a bridge deck over a valley" },
            new object[] { 100.0, new[] { 103.0 }, 100.0, "a collider on the track layer three metres over the ground, which is no ballast" },
            new object[] { 100.0, new[] { 100.4, 130.0 }, 100.4, "track on the ground under a bridge with track" },
            new object[] { 100.0, new[] { 99.0 }, 100.0, "track under the terrain, as in a tunnel" },
            new object[] { null, new[] { 101.0 }, null, "no terrain" } }) {
            var found = ObjectMarkingGeometry.Surface((double?)row[0], (double[])row[1]);
            Check(found == (double?)row[2], "the surface with " + row[3] + " is " + (row[2] ?? "none") + ", got " + (found.HasValue ? found.Value.ToString() : "none"));
        }
        Check(ObjectMarkingGeometry.BallastDepth == 1 && ObjectMarkingGeometry.Surface(0, null) == 0, "ballast lies at most 1 m above its terrain");
        // A rail bridge with track on its deck: a truss 40 m long from 2 m under the rail to 6 m above it. The terrain
        // lies at rail height under its two ends and 30 m lower under the span; the track lies on the deck all along.
        // A surface that took the track for the ground would cut the truss at rail level. The truss keeps its lower
        // chords over the valley: its frame is one box, and the lowest terrain under it, in the valley, raises nothing.
        Func<double, double, double?> underBridge = (x, z) => ObjectMarkingGeometry.Surface(Math.Abs(x) > 12 ? 0 : -30, new[] { 0.3 });
        Func<double, double, double?> trackAsGround = (x, z) => 0.3;
        ObjectMarkingFrame Truss(Func<double, double, double?> ground) => ObjectMarkingGeometry.Frame(new WorldPoint(0, 2, 100), Eye,
            new[] { new ObjectMarkingBox("truss", new WorldPoint(0, 2, 100), new WorldPoint(20, 4, 2), null, ObjectMarkingBoxKind.Mesh, ground) }, Camera, View, new List<string>());
        var trussWhole = Truss(null).Frame.Value; var truss = Truss(underBridge); var trussWrong = Truss(trackAsGround).Frame.Value;
        Check(Math.Abs(truss.Frame.Value.Bottom - trussWhole.Bottom) < .01 && truss.Frame.Value.X == trussWhole.X && truss.Frame.Value.Right == trussWhole.Right && truss.Frame.Value.Y == trussWhole.Y, "a truss with track on its deck keeps its lower chords over the valley");
        Check(trussWrong.Bottom < trussWhole.Bottom - 10, "the same truss cut at the track on its deck would lose them, which is what the surface rule prevents");
        Check(SameOutline(truss.Outline, Truss(null).Outline), "the truss is framed by its one whole box, not notched where it meets the terrain at its ends");
        // Burial is decided for the object. A sign on a post with a footing that lies wholly under the ground: the
        // footing is not framed, and the frame ends at the ground.
        Func<double, double, double?> flat = (x, z) => 0;
        ObjectMarkingBox UprightBox(string name, double y, double halfHeight, double halfWidth, Func<double, double, double?> ground) =>
            new ObjectMarkingBox(name, new WorldPoint(0, y, 100), new WorldPoint(halfWidth, halfHeight, halfWidth), null, ObjectMarkingBoxKind.Mesh, ground);
        var decided = new List<string>();
        var footed = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, new[] { UprightBox("post", 2, 3, .5, flat), UprightBox("footing", -3, 1, 1.5, flat) }, Camera, View, decided);
        var postOnly = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, new[] { UprightBox("post", 2, 3, .5, flat) }, Camera, View, new List<string>());
        Check(footed.Frame.Value.Bottom == postOnly.Frame.Value.Bottom && footed.Frame.Value.Width == postOnly.Frame.Value.Width && decided.Contains("footing: Selected as part of the object, but below the surface and not used for the frame."), "a wholly buried footing of an object that stands above the ground is not framed");
        // A wide pier of several pieces, wholly buried, under a deck that stands above the ground.
        var piered = ObjectMarkingGeometry.Frame(new WorldPoint(0, 2, 100), Eye, new[] { UprightBox("deck", 2, 1, 2, flat), UprightBox("pier", -4, 2, 18, flat) }, Camera, View, new List<string>());
        var deckOnly = ObjectMarkingGeometry.Frame(new WorldPoint(0, 2, 100), Eye, new[] { UprightBox("deck", 2, 1, 2, flat) }, Camera, View, new List<string>());
        Check(piered.Frame.Value.Bottom == deckOnly.Frame.Value.Bottom && piered.Frame.Value.Width == deckOnly.Frame.Value.Width, "a wide, wholly buried box of several pieces does not pull the frame under the ground");
        // An object all of whose boxes lie under the surface found from above keeps its whole frame.
        Func<double, double, double?> roof = (x, z) => 50;
        var tunnelled = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, new[] { UprightBox("post", 2, 3, .5, roof), UprightBox("footing", -3, 1, 1.5, roof) }, Camera, View, new List<string>());
        var uncut = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, new[] { UprightBox("post", 2, 3, .5, null), UprightBox("footing", -3, 1, 1.5, null) }, Camera, View, new List<string>());
        Check(tunnelled.Frame.Value.Bottom == uncut.Frame.Value.Bottom && tunnelled.Frame.Value.Y == uncut.Frame.Value.Y && tunnelled.Frame.Value.Width == uncut.Frame.Value.Width, "an object wholly under the surface above it, as in a tunnel, is framed whole with all its boxes");
        // A box turned 45 degrees and 40 m long is divided along its own length, read from its corners. On ground that
        // rises along that length, 1.5 m under the camera's height under its lowest piece, it stays one box turned with
        // it: its lower face rises to that lowest ground. Read along the world axes instead, its pieces would not
        // follow it.
        Func<double, double, double?> diagonal = (x, z) => (x + (z - 100)) / 14.142;
        ObjectMarkingFrame TurnedBox(Func<double, double, double?> ground) => ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye,
            new[] { new ObjectMarkingBox("turned", new WorldPoint(0, 0, 100), new WorldPoint(15.6, 3, 15.6), Turned(0, 0, 100, 20, 2, 3), ObjectMarkingBoxKind.Mesh, ground) }, Camera, View, new List<string>());
        var turnedSlope = TurnedBox(diagonal); var turnedLevel = TurnedBox(flat); var turnedWhole = TurnedBox(null);
        var turnedRaised = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye,
            new[] { new ObjectMarkingBox("turned", new WorldPoint(0, .75, 100), new WorldPoint(15.6, 2.25, 15.6), Turned(0, .75, 100, 20, 2, 2.25), ObjectMarkingBoxKind.Mesh, null) }, Camera, View, new List<string>());
        Check(SameOutline(turnedSlope.Outline, turnedRaised.Outline), "a turned box on a slope under its own length is framed by one box turned with it, raised to the lowest ground under it");
        Check(turnedLevel.Frame.Value.X == turnedWhole.Frame.Value.X && turnedLevel.Frame.Value.Right == turnedWhole.Frame.Value.Right && turnedLevel.Frame.Value.Y == turnedWhole.Frame.Value.Y && turnedLevel.Frame.Value.Bottom < turnedWhole.Frame.Value.Bottom, "cut on level ground, the turned box keeps its width and top and loses what lies under the ground");
        // Dividing a box changes nothing where nothing is cut: the outline of the pieces is the outline of the box.
        var far = TurnedBox((x, z) => -100);
        Check(Math.Abs(Area(far.Outline) - Area(turnedWhole.Outline)) < .01
            && Math.Abs(far.Frame.Value.X - turnedWhole.Frame.Value.X) + Math.Abs(far.Frame.Value.Bottom - turnedWhole.Frame.Value.Bottom) + Math.Abs(far.Frame.Value.Right - turnedWhole.Frame.Value.Right) + Math.Abs(far.Frame.Value.Y - turnedWhole.Frame.Value.Y) < .001, "the outline of a box read in pieces equals the outline of the whole box when the ground lies under it");

        // Beyond FrameDistance every frame becomes the square marker; the distance is to the bounds, not the centre.
        // Every frame still surrounded only its own object at the 550 m step with the pointer over each; less one 50 m
        // step. That is the native map editor's default gizmo distance.
        Check(ObjectMarkingGeometry.FrameDistance == 500, "the frame distance is 500 m");
        var atLimit = Frame(new WorldPoint(0, 0, 505), Box("limit", 0, 0, 505, 5, 5, 5));
        Check(atLimit.Frame.HasValue && atLimit.Distance == 500, "an object at exactly the frame distance keeps its frame");
        var beyond = Frame(new WorldPoint(0, 0, 506), Box("beyond", 0, 0, 506, 5, 5, 5));
        Check(!beyond.Frame.HasValue && beyond.Distance == 501, "an object beyond the frame distance keeps only its square marker");
        Check(Frame(new WorldPoint(0, 0, 540), Box("long", 0, 0, 540, 5, 5, 50)).Frame.HasValue, "a long object whose near end is within the frame distance is framed");

        // A platform 40 m long turned 45 degrees about the vertical: its world-aligned box is 29 m square, its own box a
        // narrow band inside it, which tightens the frame.
        double h = 20 / Math.Sqrt(2), w = 1 / Math.Sqrt(2);
        var corners = new List<WorldPoint>();
        foreach (var along in new[] { -1, 1 }) foreach (var across in new[] { -1, 1 }) foreach (var up in new[] { -1, 1 })
            corners.Add(new WorldPoint(along * h + across * w, up * .5, 100 + along * h - across * w));
        var turned = new ObjectMarkingBox("platform", new WorldPoint(0, 0, 100), new WorldPoint(h + w, .5, h + w), corners);
        var aligned = Frame(new WorldPoint(0, 0, 100), Box("platform", 0, 0, 100, h + w, .5, h + w));
        var oriented = Frame(new WorldPoint(0, 0, 100), turned);
        Check(oriented.Frame.HasValue && oriented.Frame.Value.Width < aligned.Frame.Value.Width, "an oriented box tightens the world-aligned frame");
        var wrong = new ObjectMarkingBox("platform", new WorldPoint(0, 0, 100), new WorldPoint(h + w, .5, h + w),
            corners.Select(c => new WorldPoint(c.X + 500, c.Y, c.Z)).ToArray());
        Check(Frame(new WorldPoint(0, 0, 100), wrong).Frame.Value.Width == aligned.Frame.Value.Width, "an oriented box outside the world-aligned box is not trusted");

        // Combined game session 2, Run 5: Whistle Sign W of the Bryson Everett crossing, a 0.228 x 2.55 x 0.556 m sign,
        // was logged at 298.001 m with a frame of 1.748 x 0.392 views. The strategy camera stood at map 4486.1 / 582.5 /
        // 5315.7, yaw 0, pitch 30, 100 m from its target; the sign at about 4192.8 / 528.3 / 5302.4 lies 293 m to the left
        // at a depth of only 16 m, far outside the view. Projected unclipped, its corners spread over more than a view.
        var signDecisions = new List<string>();
        var sign = ObjectMarkingGeometry.Frame(new WorldPoint(4192.8, 528.3, 5302.4), new WorldPoint(4486.1, 582.5, 5315.7),
            new[] { new ObjectMarkingBox("Sign-Whistle", new WorldPoint(4192.8, 529.575, 5302.4), new WorldPoint(.114, 1.275, .278)) },
            Strategy(new WorldPoint(4486.1, 582.5, 5315.7), 30, 60), View, signDecisions);
        Check(Math.Abs(sign.Distance - 298) < 1, "the sign is about 298 m from the camera: " + sign.Distance);
        Check(!sign.Frame.HasValue && signDecisions.Contains("Sign-Whistle: Selected for the frame, but outside the view."),
            "an object outside the view has no frame: " + String.Join("; ", signDecisions));
        // A box partly in view is cut at the view's edges: its frame reaches at most the margin past them.
        var wall = Frame(new WorldPoint(0, -5, 100), Box("wall", -300, 0, 100, 300, 5, 1));
        Check(wall.Frame.HasValue && wall.Frame.Value.X >= -ObjectMarkingGeometry.Margin - 1e-3 && wall.Frame.Value.Right < View.Width,
            "a box reaching out of the view is framed within the view");
        // Every projected frame lies within the view and its margin.
        foreach (var r in new[] { shed.Frame.Value, joined.Frame.Value, inside.Frame.Value, oriented.Frame.Value })
            Check(r.X >= -ObjectMarkingGeometry.Margin - 1e-3 && r.Y >= -ObjectMarkingGeometry.Margin - 1e-3 &&
                r.Right <= View.Width + ObjectMarkingGeometry.Margin + 1e-3 && r.Bottom <= View.Height + ObjectMarkingGeometry.Margin + 1e-3,
                "a frame stays within the view and its margin");

        // What a renderer is to the outline: effects, among them visual-effect graphs such as water spray and the beam
        // geometry of a volumetric light, never outline an object.
        Check(ObjectMarkingGeometry.KindOf("UnityEngine.MeshRenderer", new string[0], false, 0) == ObjectMarkingBoxKind.Mesh, "a mesh outlines its object");
        foreach (var effect in new[] { "UnityEngine.ParticleSystemRenderer", "UnityEngine.VFX.VFXRenderer", "UnityEngine.LineRenderer", "UnityEngine.TrailRenderer", "UnityEngine.BillboardRenderer" })
            Check(ObjectMarkingGeometry.KindOf(effect, new string[0], false, 0) == ObjectMarkingBoxKind.Effect, effect + " is an effect");
        Check(ObjectMarkingGeometry.KindOf("UnityEngine.MeshRenderer", new[] { "VLB.BeamGeometryHD", "VLB.BeamGeometryAbstractBase" }, false, 0) == ObjectMarkingBoxKind.Effect,
            "the beam geometry of a volumetric light is an effect");
        Check(ObjectMarkingGeometry.KindOf("UnityEngine.MeshRenderer", new string[0], true, 0) == ObjectMarkingBoxKind.ShadowsOnly, "a shadows-only renderer is invisible");
        Check(ObjectMarkingGeometry.KindOf("UnityEngine.MeshRenderer", new string[0], false, 1) == ObjectMarkingBoxKind.LowerLod, "a lower level of detail repeats the first");

        // Non-finite guards.
        Check(Frame(new WorldPoint(0, -5, 100), Box("nan", double.NaN, 0, 100, 5, 5, 5), Box("shed", 0, 0, 100, 5, 5, 5)).Frame.HasValue,
            "a box with non-finite bounds is left out");
        Check(!ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), new WorldPoint(double.NaN, 0, 0), new[] { Box("shed", 0, 0, 100, 5, 5, 5) }, Camera, View, null).Frame.HasValue,
            "a non-finite camera position gives the square marker");
        Check(!ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, new[] { Box("shed", 0, 0, 100, 5, 5, 5) }, p => new ClipPoint(double.NaN, 0, 0, 1), View, null).Frame.HasValue,
            "a non-finite projection gives the square marker");
        Check(!Frame(new WorldPoint(0, 0, 100)).Frame.HasValue, "an object without boxes has no frame");
        MarkerStaysOnTheObject();
        RenderersOutlineWhateverTheCullingDoes();
        MarkerStaysOnItsPointOutsideTheView();
        CrossingControllerFramesItsSetup();
        CrossingNodeFramesItsCrossbucksAndPad();
        Console.WriteLine("PASS: object marking frames identify their object at near and far zoom");
    }

    static double Shoelace(IReadOnlyList<ScreenPoint> outline) {
        double sum = 0;
        for (int i = 0; i < outline.Count; i++) { ScreenPoint p = outline[i], q = outline[(i + 1) % outline.Count]; sum += p.X * q.Y - q.X * p.Y; }
        return Math.Abs(sum) / 2;
    }
    static ScreenPoint OnScreen(Func<WorldPoint, ClipPoint> camera, double x, double y, double z) {
        var clip = camera(new WorldPoint(x, y, z));
        return new ScreenPoint(View.X + (clip.X / clip.W + 1) * View.Width / 2, View.Y + (1 - clip.Y / clip.W) * View.Height / 2);
    }
    // An object frame is one box: an object of several boxes is framed by one box around all its boxes, in the
    // object's orientation, and not by the outline of its boxes.
    public static void RunBoxes() {
        // A low wall 10 m wide and 2 m high with a tower 2 m wide and 8 m high on its left end, 100 m ahead.
        var wall = Box("wall", 0, 1, 100, 5, 1, 1); var tower = Box("tower", -4, 6, 100, 1, 4, 1);
        var anchor = new WorldPoint(0, 0, 100);
        var decisions = new List<string>();
        var both = ObjectMarkingGeometry.Frame(anchor, Eye, new[] { wall, tower }, Camera, View, decisions);
        var whole = Frame(anchor, Box("whole", 0, 5, 100, 5, 5, 1));
        Check(decisions.Contains("wall: Used for the frame.") && decisions.Contains("tower: Used for the frame."), "both boxes belong to the framed piece: " + String.Join("; ", decisions));
        Check(Math.Abs(both.Frame.Value.X - whole.Frame.Value.X) < 1e-6 && Math.Abs(both.Frame.Value.Right - whole.Frame.Value.Right) < 1e-6 && Math.Abs(both.Frame.Value.Y - whole.Frame.Value.Y) < 1e-6 && Math.Abs(both.Frame.Value.Bottom - whole.Frame.Value.Bottom) < 1e-6,
            "an object of several boxes is framed by one box around all its boxes");
        Check(Math.Abs(Shoelace(both.Outline) - Shoelace(whole.Outline)) < 1e-3, "the outline of that box is the outline of one box of its size");
        // The corner above the wall's right end, which neither box reaches, lies inside the frame.
        var corner = OnScreen(Camera, 4, 9, 99);
        Check(ObjectMarkingGeometry.Contains(both.Outline, corner.X, corner.Y), "the frame is a box: it holds the corner that no box reaches");
        Check(Math.Abs(Shoelace(both.Outline) - both.Frame.Value.Width * both.Frame.Value.Height) < 1e-4 * Shoelace(both.Outline), "seen head-on, the outline of the box is the rectangle around it");
        Check(Math.Abs(both.Distance - 99) < 1e-9 && both.Bounds.HasValue, "the distance still runs to the bounds of the boxes");

        // The box is cut at the surface the object stands in: with the ground 1 m up, the frame ends at it.
        Func<double, double, double?> ground = (x, z) => 1;
        var cut = ObjectMarkingGeometry.Frame(anchor, Eye, new[] {
            new ObjectMarkingBox("wall", new WorldPoint(0, 1, 100), new WorldPoint(5, 1, 1), null, ObjectMarkingBoxKind.Mesh, ground),
            new ObjectMarkingBox("tower", new WorldPoint(-4, 6, 100), new WorldPoint(1, 4, 1), null, ObjectMarkingBoxKind.Mesh, ground) }, Camera, View, null);
        // Seen from the height of the wall's foot, the far edge of the cut is the lowest on screen.
        var surface = OnScreen(Camera, 0, 1, 101);
        Check(Math.Abs(cut.Frame.Value.Y - both.Frame.Value.Y) < 1e-6 && Math.Abs(cut.Frame.Value.Bottom - (surface.Y + ObjectMarkingGeometry.Margin)) < 1e-3 && cut.Frame.Value.Bottom < both.Frame.Value.Bottom - 2,
            "the one box of several boxes is cut off at the ground");

        // The box stands in the object's orientation. Two slabs, each 10 m long and 1 m wide, lie end to end along
        // a diagonal, seen from above at a slant. Along the object's own directions the box is 20 m by 1 m; in the
        // world's directions it would be a square of about 15 m.
        double h = Math.Sqrt(.5);
        WorldPoint[] Slab(double cx, double cz) {
            var corners = new WorldPoint[8];
            for (int i = 0; i < 8; i++) {
                double along = (i & 1) == 0 ? -5 : 5, up = (i & 2) == 0 ? -.5 : .5, across = (i & 4) == 0 ? -.5 : .5;
                corners[i] = new WorldPoint(cx + along * h - across * h, up, cz + along * h + across * h);
            }
            return corners;
        }
        ObjectMarkingBox SlabBox(string name, double cx, double cz) => new ObjectMarkingBox(name, new WorldPoint(cx, 0, cz), new WorldPoint(5.5 * h, .5, 5.5 * h), Slab(cx, cz));
        var slabs = new[] { SlabBox("first", 0, 100), SlabBox("second", 10 * h, 100 + 10 * h) };
        var eye = new WorldPoint(0, 40, 60); var slant = Strategy(eye, 45, 60);
        var own = new WorldBasis(new WorldPoint(1, 0, 1), new WorldPoint(0, 1, 0), new WorldPoint(-1, 0, 1));
        var along = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), eye, slabs, slant, View, null, ObjectMarkingGeometry.FrameDistance, own);
        var world = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), eye, slabs, slant, View, null, ObjectMarkingGeometry.FrameDistance, WorldBasis.World);
        var unknown = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), eye, slabs, slant, View, null);
        // In the world's directions the box would be a square of about 15 m: as a single box of those corners.
        var square = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), eye, new[] { new ObjectMarkingBox("square", new WorldPoint(5 * h, 0, 100 + 5 * h), new WorldPoint(10.5 * h, .5, 10.5 * h)) }, slant, View, null);
        Check(along.Frame.HasValue && square.Frame.HasValue && Shoelace(along.Outline) < .35 * Shoelace(square.Outline), "the box of several boxes stands in the object's orientation: " + Shoelace(along.Outline) + " against " + Shoelace(square.Outline));
        // An object whose own axes are the world's while its boxes lie along the diagonal is framed along them
        // (FittedFrameTests).
        Check(world.Frame.HasValue && Math.Abs(Shoelace(world.Outline) - Shoelace(along.Outline)) < 1e-3 * Shoelace(along.Outline), "the box lies along the boxes, not along axes that do not fit them");
        Check(Math.Abs(Shoelace(unknown.Outline) - Shoelace(along.Outline)) < 1e-3, "without an orientation the box takes the directions of the box at the marker");
        // Both ends of both slabs lie inside the frame.
        foreach (var slab in slabs) foreach (var point in slab.Oriented) {
            var seen = OnScreen(slant, point.X, point.Y, point.Z);
            Check(ObjectMarkingGeometry.Contains(along.Outline, seen.X, seen.Y), "the box holds every corner of every box");
        }
        // A single box keeps its orientation.
        var single = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), eye, new[] { slabs[0] }, slant, View, null, ObjectMarkingGeometry.FrameDistance, WorldBasis.World);
        var alone = ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), eye, new[] { slabs[0] }, slant, View, null);
        Check(Math.Abs(Shoelace(single.Outline) - Shoelace(alone.Outline)) < 1e-9, "the frame uses the orientation of the single box");
        // A scene object with child objects is one inventory object for everything below its root. Andrews Depot: the root
        // stands at the east end of the building, where a lamp half a metre wide is the nearest box. The hall is
        // 40 m long and the platform 44 m: far larger than the lamp, and the far end of the platform is apart from
        // it. Framed as an object whose marker belongs to one of its boxes, only the lamp is framed.
        var root = new WorldPoint(20.5, 0, 100);
        ObjectMarkingBox[] Depot() => new[] {
            Box("lamp", 20.5, 1, 100, .25, 1, .25), Box("hall", 0, 3, 100, 20, 3, 5), Box("platform", 0, .25, 108, 22, .25, 2),
            Box("smoke", 0, 20, 100, 30, 30, 30, ObjectMarkingBoxKind.Effect), Box("far sign", -60, 2, 100, .5, 2, .1)
        };
        var partly = new List<string>();
        var lampOnly = ObjectMarkingGeometry.Frame(root, Eye, Depot(), Camera, View, partly);
        Check(lampOnly.Frame.HasValue && lampOnly.Frame.Value.Width < 40 && partly.Contains("hall: Not used for the frame because its box is much larger than the box at the marker."),
            "an object framed by the piece at its marker leaves a far larger box out: " + String.Join("; ", partly));
        // Framed whole, the box surrounds every box of the group, the far sign included; effects stay out.
        var all = new List<string>();
        var depot = ObjectMarkingGeometry.Frame(root, Eye, Depot(), Camera, View, all, ObjectMarkingGeometry.FrameDistance, null, true);
        var hull = Frame(root, Box("all", -19.25, 3, 102.5, 41.25, 3, 7.5));
        Check(depot.Frame.HasValue && Math.Abs(depot.Frame.Value.X - hull.Frame.Value.X) < 1e-6 && Math.Abs(depot.Frame.Value.Right - hull.Frame.Value.Right) < 1e-6 &&
            Math.Abs(depot.Frame.Value.Y - hull.Frame.Value.Y) < 1e-6 && Math.Abs(depot.Frame.Value.Bottom - hull.Frame.Value.Bottom) < 1e-6,
            "an object that stands for everything below it is framed by one box around all its boxes");
        foreach (var expected in new[] { "lamp: Used for the frame.", "hall: Used for the frame.", "platform: Used for the frame.", "far sign: Used for the frame.", "smoke: Not used for the frame because it is an effect." })
            Check(all.Contains(expected), "framed whole, decision recorded: " + expected + " in " + String.Join("; ", all));
        Check(Math.Abs(depot.Distance - 95) < 1e-9 && depot.Bounds.Value.Min.X == -60.5 && depot.Bounds.Value.Max.X == 22, "its distance runs to the bounds of all its boxes");
        Check(depot.Frame.Value.Width > 10 * lampOnly.Frame.Value.Width, "the whole frame is the building's, not the lamp's");
        // Which inventory objects stand for everything below them: a top scene object, and a crossing
        // controller, which stands for its grade-crossing setup (CrossingControllerFramesItsSetup).
        Check(ObjectMarkingGeometry.FramesWhole("Scene object") && ObjectMarkingGeometry.FramesWhole("Grade crossing controller") && !ObjectMarkingGeometry.FramesWhole("Generic track marker") && !ObjectMarkingGeometry.FramesWhole(null),
            "a scene object with child objects and a crossing controller are framed whole; an object of another family by the piece at its marker");
        Console.WriteLine("PASS: one enclosing frame in the object's orientation, cut at the ground");
    }
}
