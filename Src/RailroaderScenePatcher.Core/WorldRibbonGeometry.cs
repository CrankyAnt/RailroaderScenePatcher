// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

namespace RailroaderScenePatcher.Core {
    public readonly struct WorldPoint {
        public WorldPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    public readonly struct ClipPoint {
        public ClipPoint(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double W { get; }
    }

    public sealed class ProjectedWorldRibbon {
        internal ProjectedWorldRibbon(List<ScreenTriangle> ribbon, List<ScreenTriangle> centerline) {
            Ribbon = ribbon; Centerline = centerline;
        }
        public IReadOnlyList<ScreenTriangle> Ribbon { get; }
        public IReadOnlyList<ScreenTriangle> Centerline { get; }
        public bool Contains(ScreenPoint point) {
            foreach (var t in Ribbon) {
                double a = Cross(t.A, t.B, point), b = Cross(t.B, t.C, point), c = Cross(t.C, t.A, point);
                if ((a >= 0 && b >= 0 && c >= 0) || (a <= 0 && b <= 0 && c <= 0)) return true;
            }
            return false;
        }
        static double Cross(ScreenPoint a, ScreenPoint b, ScreenPoint c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    public static class WorldRibbonGeometry {
        public static ProjectedWorldRibbon ProjectCurve(IReadOnlyList<WorldPoint> controls, double worldRadius,
            Func<WorldPoint, ClipPoint> project, ScreenRect viewport, double minimumRadius = 10) {
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (controls.Count != 4) throw new ArgumentException("A cubic requires four controls", nameof(controls));
            if (!Finite(worldRadius) || worldRadius < 0 || !Finite(minimumRadius) || minimumRadius < 0)
                throw new ArgumentOutOfRangeException(nameof(worldRadius));
            if (!Finite(viewport.X) || !Finite(viewport.Y) || !Finite(viewport.Right) || !Finite(viewport.Bottom) ||
                viewport.Width <= 0 || viewport.Height <= 0) throw new ArgumentOutOfRangeException(nameof(viewport));
            foreach (var p in controls) if (!Valid(p)) throw new ArgumentOutOfRangeException(nameof(controls));
            var ribbon = new List<ScreenTriangle>();
            var centerline = new List<ScreenTriangle>();
            Subdivide(controls[0], controls[1], controls[2], controls[3], 0, worldRadius,
                minimumRadius, project, viewport, ribbon, centerline);
            return new ProjectedWorldRibbon(ribbon, centerline);
        }

        static void Subdivide(WorldPoint a, WorldPoint b, WorldPoint c, WorldPoint d, int depth,
            double radius, double minimum, Func<WorldPoint, ClipPoint> project, ScreenRect viewport,
            List<ScreenTriangle> ribbon, List<ScreenTriangle> centerline) {
            if (OutsideBounds(a, b, c, d, radius, project)) return;
            var ab = Mid(a, b); var bc = Mid(b, c); var cd = Mid(c, d);
            var abc = Mid(ab, bc); var bcd = Mid(bc, cd); var mid = Mid(abc, bcd);
            if (depth < 10 && !Flat(a, b, c, d, project, viewport)) {
                Subdivide(a, ab, abc, mid, depth + 1, radius, minimum, project, viewport, ribbon, centerline);
                Subdivide(mid, bcd, cd, d, depth + 1, radius, minimum, project, viewport, ribbon, centerline);
                return;
            }
            AddLeaf(a, d, radius, minimum, project, viewport, ribbon, centerline);
        }

        static bool OutsideBounds(WorldPoint a, WorldPoint b, WorldPoint c, WorldPoint d, double radius,
            Func<WorldPoint, ClipPoint> project) {
            // Both square end extensions and side offsets fit inside this expansion.
            double e = radius * 2;
            double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) - e;
            double maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + e;
            double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) - e;
            double maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) + e;
            double minZ = Math.Min(Math.Min(a.Z, b.Z), Math.Min(c.Z, d.Z)) - e;
            double maxZ = Math.Max(Math.Max(a.Z, b.Z), Math.Max(c.Z, d.Z)) + e;
            var corners = new List<ClipPoint>();
            for (int i = 0; i < 8; i++) {
                var p = project(new WorldPoint((i & 1) == 0 ? minX : maxX, (i & 2) == 0 ? minY : maxY,
                    (i & 4) == 0 ? minZ : maxZ));
                if (!Valid(p)) return false;
                corners.Add(p);
            }
            for (int plane = 0; plane < 6; plane++) {
                bool outside = true;
                foreach (var p in corners) if (Distance(p, plane) >= 0) { outside = false; break; }
                if (outside) return true;
            }
            return false;
        }

        static bool Flat(WorldPoint a, WorldPoint b, WorldPoint c, WorldPoint d,
            Func<WorldPoint, ClipPoint> project, ScreenRect viewport) {
            var pa = project(a); var pb = project(b); var pc = project(c); var pd = project(d);
            if (!Valid(pa) || !Valid(pb) || !Valid(pc) || !Valid(pd) ||
                pa.W <= 0 || pb.W <= 0 || pc.W <= 0 || pd.W <= 0) return false;
            var sa = Screen(pa, viewport); var sb = Screen(pb, viewport);
            var sc = Screen(pc, viewport); var sd = Screen(pd, viewport);
            return DistanceToSegment(sb, sa, sd) <= 1 && DistanceToSegment(sc, sa, sd) <= 1;
        }

        static double DistanceToSegment(ScreenPoint p, ScreenPoint a, ScreenPoint b) {
            double dx = b.X - a.X, dy = b.Y - a.Y, length2 = dx * dx + dy * dy;
            double t = length2 == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length2));
            double x = p.X - (a.X + t * dx), y = p.Y - (a.Y + t * dy);
            return Math.Sqrt(x * x + y * y);
        }

        static void AddLeaf(WorldPoint a, WorldPoint d, double radius, double minimum,
            Func<WorldPoint, ClipPoint> project, ScreenRect viewport, List<ScreenTriangle> ribbon,
            List<ScreenTriangle> centerline) {
            double dx = d.X - a.X, dy = d.Y - a.Y, dz = d.Z - a.Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double ux = length == 0 ? 1 : dx / length, uy = length == 0 ? 0 : dy / length, uz = length == 0 ? 0 : dz / length;
            double sx = uz, sz = -ux;
            var polygon = new List<ClipPoint> {
                project(Offset(a, -ux * radius + sx * radius, -uy * radius, -uz * radius + sz * radius)),
                project(Offset(d, ux * radius + sx * radius, uy * radius, uz * radius + sz * radius)),
                project(Offset(d, ux * radius - sx * radius, uy * radius, uz * radius - sz * radius)),
                project(Offset(a, -ux * radius - sx * radius, -uy * radius, -uz * radius - sz * radius))
            };
            var points = new List<ScreenPoint>();
            bool valid = true;
            foreach (var p in polygon) if (!Valid(p)) valid = false;
            if (valid) {
                for (int plane = 0; plane < 6; plane++) polygon = ClipPolygon(polygon, plane);
                foreach (var p in polygon) if (p.W > 0) points.Add(Screen(p, viewport));
            }
            var line = new List<ClipPoint> { project(a), project(d) };
            if (Valid(line[0]) && Valid(line[1]) && ClipLine(line) && line[0].W > 0 && line[1].W > 0) {
                var start = Screen(line[0], viewport); var end = Screen(line[1], viewport);
                var zones = new[] { viewport };
                foreach (var t in WorldStrokeGeometry.Triangulate(start, end, minimum, true, zones)) {
                    points.Add(t.A); points.Add(t.B); points.Add(t.C);
                }
                centerline.AddRange(WorldStrokeGeometry.Triangulate(start, end, 1, false, zones));
            }
            var hull = Hull(points);
            if (hull.Count < 3) return;
            var triangles = new List<ScreenTriangle>();
            for (int i = 1; i + 1 < hull.Count; i++) triangles.Add(new ScreenTriangle(hull[0], hull[i], hull[i + 1]));
            ribbon.AddRange(WorldStrokeGeometry.ClipTriangles(triangles, new[] { viewport }));
        }

        static List<ScreenPoint> Hull(List<ScreenPoint> points) {
            points.Sort((a, b) => a.X == b.X ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            if (points.Count < 3) return points;
            var hull = new List<ScreenPoint>();
            foreach (var p in points) {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lower = hull.Count;
            for (int i = points.Count - 2; i >= 0; i--) {
                var p = points[i];
                while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }
        static double Cross(ScreenPoint a, ScreenPoint b, ScreenPoint c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        static List<ClipPoint> ClipPolygon(List<ClipPoint> polygon, int plane) {
            var output = new List<ClipPoint>();
            if (polygon.Count == 0) return output;
            var previous = polygon[polygon.Count - 1]; double pd = Distance(previous, plane);
            foreach (var current in polygon) {
                double cd = Distance(current, plane);
                if ((pd >= 0) != (cd >= 0)) output.Add(Lerp(previous, current, pd / (pd - cd)));
                if (cd >= 0) output.Add(current);
                previous = current; pd = cd;
            }
            return output;
        }
        static bool ClipLine(List<ClipPoint> line) {
            for (int plane = 0; plane < 6; plane++) {
                double a = Distance(line[0], plane), b = Distance(line[1], plane);
                if (a < 0 && b < 0) return false;
                if (a < 0) line[0] = Lerp(line[0], line[1], a / (a - b));
                else if (b < 0) line[1] = Lerp(line[0], line[1], a / (a - b));
            }
            return true;
        }
        static double Distance(ClipPoint p, int plane) {
            switch (plane) { case 0: return p.W + p.X; case 1: return p.W - p.X;
                case 2: return p.W + p.Y; case 3: return p.W - p.Y;
                case 4: return p.W + p.Z; default: return p.W - p.Z; }
        }
        static ClipPoint Lerp(ClipPoint a, ClipPoint b, double t) => new ClipPoint(
            a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t);
        static ScreenPoint Screen(ClipPoint p, ScreenRect v) => new ScreenPoint(
            v.X + (p.X / p.W + 1) * v.Width / 2, v.Y + (1 - p.Y / p.W) * v.Height / 2);
        static WorldPoint Mid(WorldPoint a, WorldPoint b) => new WorldPoint(a.X / 2 + b.X / 2, a.Y / 2 + b.Y / 2, a.Z / 2 + b.Z / 2);
        static WorldPoint Offset(WorldPoint p, double x, double y, double z) => new WorldPoint(p.X + x, p.Y + y, p.Z + z);
        static bool Finite(double x) => !Double.IsNaN(x) && !Double.IsInfinity(x);
        static bool Valid(WorldPoint p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
        static bool Valid(ClipPoint p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z) && Finite(p.W);
    }
}
