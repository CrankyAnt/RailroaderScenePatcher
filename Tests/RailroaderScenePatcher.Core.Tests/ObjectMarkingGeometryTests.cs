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
    static ObjectMarkingPart Box(string name, double x, double y, double z, double ex, double ey, double ez, ObjectMarkingPartKind kind = ObjectMarkingPartKind.Mesh) =>
        new ObjectMarkingPart(name, new WorldPoint(x, y, z), new WorldPoint(ex, ey, ez), null, kind);
    static ObjectMarkingFrame Frame(WorldPoint anchor, params ObjectMarkingPart[] parts) =>
        ObjectMarkingGeometry.Frame(anchor, Eye, parts, Camera, View, new List<string>());
    static bool Inside(ScreenRect r, double x, double y) => x >= r.X && x <= r.Right && y >= r.Y && y <= r.Bottom;

    public static void Run() {
        // A 10 m shed 100 m ahead: its front face, 95 m away, is 10/95 of the half view high, about 76 pixels, and a
        // margin of 4 pixels on each side.
        var shed = Frame(new WorldPoint(0, -5, 100), Box("shed", 0, 0, 100, 5, 5, 5));
        Check(shed.Frame.HasValue && shed.Frame.Value.Height > 80 && shed.Frame.Value.Height < 90 && Inside(shed.Frame.Value, 1280, 720),
            "a near object is framed around its own size");
        Check(Math.Abs(shed.Distance - 95) < 1e-9, "the distance is measured to the object's bounds");

        // Part exclusion, in world metres. A culling or proxy renderer 800 m wide beside the shed, and a part of the same
        // object 40 m away, such as a sign far from its post, stay out of the frame; a roof touching the walls joins.
        var decisions = new List<string>();
        var parts = new[] {
            Box("walls", 0, 0, 100, 5, 5, 5), Box("roof", 0, 6, 100, 5.5f, 1, 5.5f),
            Box("proxy", 0, 0, 100, 400, 50, 400), Box("sign", 40, 0, 100, 1, 1, .1f),
            Box("smoke", 0, 20, 100, 30, 30, 30, ObjectMarkingPartKind.Effect), Box("shadow", 0, 0, 100, 5, 5, 5, ObjectMarkingPartKind.ShadowsOnly),
            Box("lod1", 0, 0, 100, 5, 5, 5, ObjectMarkingPartKind.LowerLod)
        };
        var joined = ObjectMarkingGeometry.Frame(new WorldPoint(0, -5, 100), Eye, parts, Camera, View, decisions);
        Check(joined.Frame.HasValue && joined.Frame.Value.Height > shed.Frame.Value.Height && joined.Frame.Value.Height < 2.5 * shed.Frame.Value.Height,
            "the roof joins the walls and the proxy does not stretch the frame");
        Check(joined.Frame.Value.Right < 1280 + 400, "the sign 40 m away stays outside the frame");
        foreach (var expected in new[] { "walls: included", "roof: included", "proxy: excluded, far larger than the part at the marker",
            "sign: excluded, apart from the object's piece", "smoke: excluded, effect", "shadow: excluded, shadows only", "lod1: excluded, lower level of detail" })
            Check(decisions.Contains(expected), "decision recorded: " + expected + " in " + String.Join("; ", decisions));
        Check(Frame(new WorldPoint(0, -5, 100), Box("walls", 0, 0, 100, 5, 5, 5), Box("porch", 0, -5.5, 106, 3, 1, 1.5f)).Frame.Value.Bottom >
            shed.Frame.Value.Bottom, "a part within the join distance joins the frame");
        // A large building is not excluded for its size when the marker part is large too.
        Check(Frame(new WorldPoint(0, -5, 100), Box("hall", 0, 0, 100, 30, 10, 30), Box("tower", 0, 20, 100, 5, 10, 5)).Frame.HasValue,
            "parts of comparable size join");

        // Near-plane clipping. Close up, the camera stands inside the shed's porch: the porch is clipped at the near
        // plane instead of being skipped, so the frame covers more than the view, and it is kept.
        var inside = Frame(new WorldPoint(0, -2, 3), Box("porch", 0, 0, 3, 4, 3, 4));
        Check(inside.Frame.HasValue && inside.Frame.Value.X < 0 && inside.Frame.Value.Right > View.Width && inside.Frame.Value.Y < 0 && inside.Frame.Value.Bottom > View.Height,
            "a close-up frame larger than the view is kept");
        Check(inside.Distance == 0, "a camera inside the bounds is at distance zero");
        var behind = Frame(new WorldPoint(0, 0, -50), Box("behind", 0, 0, -50, 2, 2, 2));
        Check(!behind.Frame.HasValue, "an object wholly behind the camera has no frame");

        // Beyond FrameDistance every frame becomes the square marker; the distance is to the bounds, not the centre.
        // Every frame still surrounded only its own object at the 550 m step with the pointer over each; less one 50 m
        // step. That is the native editor's default gizmo distance.
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
        var turned = new ObjectMarkingPart("platform", new WorldPoint(0, 0, 100), new WorldPoint(h + w, .5, h + w), corners);
        var aligned = Frame(new WorldPoint(0, 0, 100), Box("platform", 0, 0, 100, h + w, .5, h + w));
        var oriented = Frame(new WorldPoint(0, 0, 100), turned);
        Check(oriented.Frame.HasValue && oriented.Frame.Value.Width < aligned.Frame.Value.Width, "an oriented box tightens the world-aligned frame");
        var wrong = new ObjectMarkingPart("platform", new WorldPoint(0, 0, 100), new WorldPoint(h + w, .5, h + w),
            corners.Select(c => new WorldPoint(c.X + 500, c.Y, c.Z)).ToArray());
        Check(Frame(new WorldPoint(0, 0, 100), wrong).Frame.Value.Width == aligned.Frame.Value.Width, "an oriented box outside the world-aligned box is not trusted");

        // Combined game session 2, Run 5: Whistle Sign W of the Bryson Everett crossing, a 0.228 x 2.55 x 0.556 m sign,
        // was logged at 298.001 m with a frame of 1.748 x 0.392 views. The strategy camera stood at map 4486.1 / 582.5 /
        // 5315.7, yaw 0, pitch 30, 100 m from its target; the sign at about 4192.8 / 528.3 / 5302.4 lies 293 m to the left
        // at a depth of only 16 m, far outside the view. Projected unclipped, its corners spread over more than a view.
        var signDecisions = new List<string>();
        var sign = ObjectMarkingGeometry.Frame(new WorldPoint(4192.8, 528.3, 5302.4), new WorldPoint(4486.1, 582.5, 5315.7),
            new[] { new ObjectMarkingPart("Sign-Whistle", new WorldPoint(4192.8, 529.575, 5302.4), new WorldPoint(.114, 1.275, .278)) },
            Strategy(new WorldPoint(4486.1, 582.5, 5315.7), 30, 60), View, signDecisions);
        Check(Math.Abs(sign.Distance - 298) < 1, "the sign is about 298 m from the camera: " + sign.Distance);
        Check(!sign.Frame.HasValue && signDecisions.Contains("Sign-Whistle: included, outside the view"),
            "an object outside the view has no frame: " + String.Join("; ", signDecisions));
        // A part partly in view is cut at the view's edges: its frame reaches at most the margin past them.
        var wall = Frame(new WorldPoint(0, -5, 100), Box("wall", -300, 0, 100, 300, 5, 1));
        Check(wall.Frame.HasValue && wall.Frame.Value.X >= -ObjectMarkingGeometry.Margin - 1e-3 && wall.Frame.Value.Right < View.Width,
            "a part reaching out of the view is framed within the view");
        // Every projected frame lies within the view and its margin.
        foreach (var r in new[] { shed.Frame.Value, joined.Frame.Value, inside.Frame.Value, oriented.Frame.Value })
            Check(r.X >= -ObjectMarkingGeometry.Margin - 1e-3 && r.Y >= -ObjectMarkingGeometry.Margin - 1e-3 &&
                r.Right <= View.Width + ObjectMarkingGeometry.Margin + 1e-3 && r.Bottom <= View.Height + ObjectMarkingGeometry.Margin + 1e-3,
                "a frame stays within the view and its margin");

        // What a renderer is to the outline: effects, among them visual-effect graphs such as water spray and the beam
        // geometry of a volumetric light, never outline an object.
        Check(ObjectMarkingGeometry.KindOf("UnityEngine.MeshRenderer", new string[0], false, 0) == ObjectMarkingPartKind.Mesh, "a mesh outlines its object");
        foreach (var effect in new[] { "UnityEngine.ParticleSystemRenderer", "UnityEngine.VFX.VFXRenderer", "UnityEngine.LineRenderer", "UnityEngine.TrailRenderer", "UnityEngine.BillboardRenderer" })
            Check(ObjectMarkingGeometry.KindOf(effect, new string[0], false, 0) == ObjectMarkingPartKind.Effect, effect + " is an effect");
        Check(ObjectMarkingGeometry.KindOf("UnityEngine.MeshRenderer", new[] { "VLB.BeamGeometryHD", "VLB.BeamGeometryAbstractBase" }, false, 0) == ObjectMarkingPartKind.Effect,
            "the beam geometry of a volumetric light is an effect");
        Check(ObjectMarkingGeometry.KindOf("UnityEngine.MeshRenderer", new string[0], true, 0) == ObjectMarkingPartKind.ShadowsOnly, "a shadows-only renderer is invisible");
        Check(ObjectMarkingGeometry.KindOf("UnityEngine.MeshRenderer", new string[0], false, 1) == ObjectMarkingPartKind.LowerLod, "a lower level of detail repeats the first");

        // Non-finite guards.
        Check(Frame(new WorldPoint(0, -5, 100), Box("nan", double.NaN, 0, 100, 5, 5, 5), Box("shed", 0, 0, 100, 5, 5, 5)).Frame.HasValue,
            "a part with non-finite bounds is left out");
        Check(!ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), new WorldPoint(double.NaN, 0, 0), new[] { Box("shed", 0, 0, 100, 5, 5, 5) }, Camera, View, null).Frame.HasValue,
            "a non-finite camera position gives the square marker");
        Check(!ObjectMarkingGeometry.Frame(new WorldPoint(0, 0, 100), Eye, new[] { Box("shed", 0, 0, 100, 5, 5, 5) }, p => new ClipPoint(double.NaN, 0, 0, 1), View, null).Frame.HasValue,
            "a non-finite projection gives the square marker");
        Check(!Frame(new WorldPoint(0, 0, 100)).Frame.HasValue, "an object without parts has no frame");
        Console.WriteLine("PASS: object marking frames identify their object at near and far zoom");
    }
}
