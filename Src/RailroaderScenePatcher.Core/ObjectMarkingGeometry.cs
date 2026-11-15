// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RailroaderScenePatcher.Core {
    // What a renderer is to the object's outline. Only meshes outline it: an effect reaches past the object, a
    // shadows-only renderer is invisible and a lower level of detail repeats the first one.
    public enum ObjectMarkingPartKind { Mesh, Effect, ShadowsOnly, LowerLod }

    // One renderer of an object in world coordinates: its world-aligned box as centre and half extents, and, when known,
    // the eight corners of its own oriented box, which lies inside the world-aligned one.
    public sealed class ObjectMarkingPart {
        public ObjectMarkingPart(string name, WorldPoint center, WorldPoint extents, IReadOnlyList<WorldPoint> oriented = null,
            ObjectMarkingPartKind kind = ObjectMarkingPartKind.Mesh) {
            Name = name ?? ""; Center = center; Extents = extents; Oriented = oriented; Kind = kind;
        }
        public string Name { get; }
        public WorldPoint Center { get; }
        public WorldPoint Extents { get; }
        public IReadOnlyList<WorldPoint> Oriented { get; }
        public ObjectMarkingPartKind Kind { get; }
    }

    public sealed class ObjectMarkingFrame {
        internal ObjectMarkingFrame(ScreenRect? frame, double distance) { Frame = frame; Distance = distance; }
        // The frame in top-left screen pixels, or null when the object shows only its square marker.
        public ScreenRect? Frame { get; }
        // Metres from the camera to the bounds of the framed piece; NaN when no piece was found.
        public double Distance { get; }
    }

    // The frame a world marking draws around an object, which picking uses too. The same frame serves the gold frame of
    // the inspected object and the frames of candidates.
    public static class ObjectMarkingGeometry {
        // Beyond this distance from the camera to an object's bounds, in metres, the object shows only its square
        // marker: the largest distance at which every frame still surrounded only its own object in the game, 500 m, less
        // one 50 m measurement step. It stays within the native editor's default gizmo distance of 500 m.
        public const double FrameDistance = 450;
        // A part far larger than the part at the object's marker, such as a culling or proxy volume, does not outline
        // the object: larger than this factor and this margin in metres.
        public const double ExtentFactor = 4, ExtentMargin = 10;
        // Parts closer than this to the growing piece join it: a fraction of the marker part's size, at least a metre.
        public const double JoinFraction = .25, MinimumJoin = 1;
        // Pixels around a projected part.
        public const double Margin = 4;

        // What a renderer is to the outline, from its type, the types of the components on its object with their base
        // types, its shadow mode and its level of detail. Effects reach past the object they belong to: particles,
        // visual-effect graphs such as water spray, lines, trails, billboards and the beam geometry of a volumetric light.
        public static ObjectMarkingPartKind KindOf(string rendererType, IReadOnlyList<string> componentTypes, bool shadowsOnly, int lodLevel) {
            var type = rendererType ?? "";
            if (Array.IndexOf(EffectRenderers, type) >= 0) return ObjectMarkingPartKind.Effect;
            if (componentTypes != null)
                foreach (var component in componentTypes)
                    if (component != null && component.StartsWith("VLB.BeamGeometry", StringComparison.Ordinal)) return ObjectMarkingPartKind.Effect;
            if (shadowsOnly) return ObjectMarkingPartKind.ShadowsOnly;
            return lodLevel > 0 ? ObjectMarkingPartKind.LowerLod : ObjectMarkingPartKind.Mesh;
        }
        static readonly string[] EffectRenderers = {
            "UnityEngine.ParticleSystemRenderer", "UnityEngine.VFX.VFXRenderer", "UnityEngine.LineRenderer", "UnityEngine.TrailRenderer", "UnityEngine.BillboardRenderer"
        };

        // The anchor is the object's position and the camera the camera's position, both in world coordinates. The
        // projection maps a world point to clip coordinates (-w <= x, y, z <= w is visible). Decisions, when given,
        // receive one line per part.
        public static ObjectMarkingFrame Frame(WorldPoint anchor, WorldPoint camera, IReadOnlyList<ObjectMarkingPart> parts,
            Func<WorldPoint, ClipPoint> project, ScreenRect view, List<string> decisions) {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            if (project == null) throw new ArgumentNullException(nameof(project));
            var open = new List<ObjectMarkingPart>();
            foreach (var part in parts) {
                var reason = part.Kind == ObjectMarkingPartKind.Effect ? "effect"
                    : part.Kind == ObjectMarkingPartKind.ShadowsOnly ? "shadows only"
                    : part.Kind == ObjectMarkingPartKind.LowerLod ? "lower level of detail"
                    : !Valid(part.Center) || !Valid(part.Extents) || part.Extents.X < 0 || part.Extents.Y < 0 || part.Extents.Z < 0 ? "bounds not finite"
                    : null;
                if (reason == null) open.Add(part); else decisions?.Add(part.Name + ": excluded, " + reason);
            }
            if (open.Count == 0 || !Valid(anchor) || !Valid(camera)) {
                foreach (var part in open) decisions?.Add(part.Name + ": excluded, no anchor or camera");
                return new ObjectMarkingFrame(null, Double.NaN);
            }
            // The part the marker belongs to: under it, else the nearest; then the centre nearest it; then the smaller.
            var seed = open[0];
            foreach (var part in open) {
                double d = Distance(part, anchor), best = Distance(seed, anchor);
                if (d < best || (d == best && (Square(part.Center, anchor) < Square(seed.Center, anchor) ||
                    (Square(part.Center, anchor) == Square(seed.Center, anchor) && Size(part) < Size(seed))))) seed = part;
            }
            double limit = Math.Max(Size(seed) * ExtentFactor, Size(seed) + ExtentMargin), join = Math.Max(MinimumJoin, Size(seed) * JoinFraction);
            var piece = new List<ObjectMarkingPart> { seed };
            open.Remove(seed);
            for (int i = open.Count - 1; i >= 0; i--)
                if (Size(open[i]) > limit) { decisions?.Add(open[i].Name + ": excluded, far larger than the part at the marker"); open.RemoveAt(i); }
            double minX = seed.Center.X - seed.Extents.X, maxX = seed.Center.X + seed.Extents.X, minY = seed.Center.Y - seed.Extents.Y,
                maxY = seed.Center.Y + seed.Extents.Y, minZ = seed.Center.Z - seed.Extents.Z, maxZ = seed.Center.Z + seed.Extents.Z;
            for (bool joined = true; joined;) {
                joined = false;
                for (int i = open.Count - 1; i >= 0; i--) {
                    var p = open[i];
                    double gap = Math.Max(Gap(minX, maxX, p.Center.X, p.Extents.X), Math.Max(Gap(minY, maxY, p.Center.Y, p.Extents.Y), Gap(minZ, maxZ, p.Center.Z, p.Extents.Z)));
                    if (gap > join) continue;
                    minX = Math.Min(minX, p.Center.X - p.Extents.X); maxX = Math.Max(maxX, p.Center.X + p.Extents.X);
                    minY = Math.Min(minY, p.Center.Y - p.Extents.Y); maxY = Math.Max(maxY, p.Center.Y + p.Extents.Y);
                    minZ = Math.Min(minZ, p.Center.Z - p.Extents.Z); maxZ = Math.Max(maxZ, p.Center.Z + p.Extents.Z);
                    piece.Add(p); open.RemoveAt(i); joined = true;
                }
            }
            foreach (var part in open) decisions?.Add(part.Name + ": excluded, apart from the object's piece");
            double dx = Gap(minX, maxX, camera.X, 0), dy = Gap(minY, maxY, camera.Y, 0), dz = Gap(minZ, maxZ, camera.Z, 0);
            var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!(distance <= FrameDistance)) {
                foreach (var part in piece) decisions?.Add(part.Name + ": included, beyond the frame distance at " + distance.ToString("0.0", CultureInfo.InvariantCulture) + " m");
                return new ObjectMarkingFrame(null, distance);
            }
            // Clip each part to the view only now, so a part that crosses the near plane or a view edge still outlines a
            // near object, and a part outside the view, however near the camera's plane, adds nothing.
            ScreenRect? frame = null;
            foreach (var part in piece) {
                var aligned = Project(BoxCorners(part), project, view, out var finite);
                if (!aligned.HasValue) { decisions?.Add(part.Name + (finite ? ": included, outside the view" : ": included, projection not finite")); continue; }
                var rect = aligned.Value;
                var oriented = part.Oriented != null && part.Oriented.Count == 8 ? Project(part.Oriented, project, view, out _) : null;
                if (oriented.HasValue) {
                    double left = Math.Max(rect.X, oriented.Value.X), top = Math.Max(rect.Y, oriented.Value.Y);
                    double right = Math.Min(rect.Right, oriented.Value.Right), bottom = Math.Min(rect.Bottom, oriented.Value.Bottom);
                    if (right > left && bottom > top) rect = Rect(left, top, right, bottom);
                }
                decisions?.Add(part.Name + ": included");
                frame = frame.HasValue ? Rect(Math.Min(frame.Value.X, rect.X), Math.Min(frame.Value.Y, rect.Y), Math.Max(frame.Value.Right, rect.Right), Math.Max(frame.Value.Bottom, rect.Bottom)) : rect;
            }
            if (frame.HasValue) frame = Rect(frame.Value.X - Margin, frame.Value.Y - Margin, frame.Value.Right + Margin, frame.Value.Bottom + Margin);
            return new ObjectMarkingFrame(frame, distance);
        }

        // The screen rectangle of the part of a box inside the view: each of its six faces clipped at the six planes of
        // the view volume. Null when nothing of the box lies in the view, or, with finite false, when the projection
        // is not finite. A box far to the side with little depth projects far outside the view; unclipped, its corners
        // spread over more than the whole view.
        static ScreenRect? Project(IReadOnlyList<WorldPoint> corners, Func<WorldPoint, ClipPoint> project, ScreenRect view, out bool finite) {
            finite = false;
            var clip = new ClipPoint[8];
            for (int i = 0; i < 8; i++) {
                if (!Valid(corners[i])) return null;
                clip[i] = project(corners[i]);
                if (!Valid(clip[i])) return null;
            }
            finite = true;
            double left = Double.PositiveInfinity, top = Double.PositiveInfinity, right = Double.NegativeInfinity, bottom = Double.NegativeInfinity;
            foreach (var face in Faces) {
                var polygon = new List<ClipPoint> { clip[face[0]], clip[face[1]], clip[face[2]], clip[face[3]] };
                for (int plane = 0; plane < 6 && polygon.Count != 0; plane++) polygon = WorldRibbonGeometry.ClipPolygon(polygon, plane);
                foreach (var p in polygon) {
                    if (!(p.W > 0)) continue;
                    double x = view.X + (p.X / p.W + 1) * view.Width / 2, y = view.Y + (1 - p.Y / p.W) * view.Height / 2;
                    if (!Finite(x) || !Finite(y)) { finite = false; return null; }
                    left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
            }
            return left <= right ? Rect(left, top, right, bottom) : (ScreenRect?)null;
        }
        // The six faces of a box whose corners differ in one bit along an edge, each as four corners in order.
        static readonly int[][] Faces = {
            new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }
        };
        // The eight corners in the order Project expects: bit 0 for x, bit 1 for y, bit 2 for z, so corners that differ
        // in one bit share an edge.
        static WorldPoint[] BoxCorners(ObjectMarkingPart part) {
            var corners = new WorldPoint[8];
            for (int i = 0; i < 8; i++)
                corners[i] = new WorldPoint(part.Center.X + ((i & 1) == 0 ? -1 : 1) * part.Extents.X,
                    part.Center.Y + ((i & 2) == 0 ? -1 : 1) * part.Extents.Y, part.Center.Z + ((i & 4) == 0 ? -1 : 1) * part.Extents.Z);
            return corners;
        }
        static double Gap(double min, double max, double center, double extent) =>
            Math.Max(0, Math.Max(min - (center + extent), (center - extent) - max));
        static double Distance(ObjectMarkingPart part, WorldPoint p) {
            double dx = Math.Max(0, Math.Abs(p.X - part.Center.X) - part.Extents.X), dy = Math.Max(0, Math.Abs(p.Y - part.Center.Y) - part.Extents.Y),
                dz = Math.Max(0, Math.Abs(p.Z - part.Center.Z) - part.Extents.Z);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        static double Square(WorldPoint a, WorldPoint b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);
        // A part's size: its largest world dimension in metres.
        static double Size(ObjectMarkingPart part) => 2 * Math.Max(part.Extents.X, Math.Max(part.Extents.Y, part.Extents.Z));
        static ScreenRect Rect(double left, double top, double right, double bottom) =>
            ScreenRect.FromEdges((float)left, (float)top, (float)right, (float)bottom);
        static bool Finite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value);
        static bool Valid(WorldPoint p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
        static bool Valid(ClipPoint p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z) && Finite(p.W);
    }
}
