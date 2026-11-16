// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    public readonly struct ScreenPoint {
        public ScreenPoint(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }
    }

    public readonly struct ScreenRect {
        public ScreenRect(float x, float y, float width, float height)
            : this(new ScreenPoint(x, y), new ScreenPoint(x + width, y + height)) { }
        ScreenRect(ScreenPoint min, ScreenPoint max) { X = min.X; Y = min.Y; Right = max.X; Bottom = max.Y; }
        // Native screen edges are floats. Preserve their exact values while clipping in doubles,
        // so converting submitted vertices back to floats cannot round across a clipping edge.
        public static ScreenRect FromEdges(float left, float top, float right, float bottom) =>
            new ScreenRect(new ScreenPoint(left, top), new ScreenPoint(right, bottom));
        public double X { get; }
        public double Y { get; }
        public double Width => Right - X;
        public double Height => Bottom - Y;
        public double Right { get; }
        public double Bottom { get; }
    }

    public readonly struct ScreenTriangle {
        public ScreenTriangle(ScreenPoint a, ScreenPoint b, ScreenPoint c) { A = a; B = b; C = c; }
        public ScreenPoint A { get; }
        public ScreenPoint B { get; }
        public ScreenPoint C { get; }
    }

    // Produces the actual screen-space triangles submitted by the world marking renderer.
    // Visible zones are final UI-excluded rectangles; this module never changes hit geometry.
    public static class WorldStrokeGeometry {
        public static IReadOnlyList<ScreenTriangle> Triangulate(ScreenPoint a, ScreenPoint b, double radius,
            bool extend, IEnumerable<ScreenRect> visibleZones) {
            if (visibleZones == null) throw new ArgumentNullException(nameof(visibleZones));
            if (!Finite(a.X) || !Finite(a.Y) || !Finite(b.X) || !Finite(b.Y) || !Finite(radius) || radius < 0)
                throw new ArgumentOutOfRangeException(nameof(radius), "World stroke coordinates and radius must be finite; radius must be nonnegative");
            var triangles = new List<ScreenTriangle>();
            if (radius == 0) return triangles;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length == 0 && !extend) return triangles;
            double ux = length == 0 ? 1 : dx / length, uy = length == 0 ? 0 : dy / length;
            double end = extend ? radius : 0;
            var quad = new List<ScreenPoint> {
                new ScreenPoint(a.X - ux * end + uy * radius, a.Y - uy * end - ux * radius),
                new ScreenPoint(b.X + ux * end + uy * radius, b.Y + uy * end - ux * radius),
                new ScreenPoint(b.X + ux * end - uy * radius, b.Y + uy * end + ux * radius),
                new ScreenPoint(a.X - ux * end - uy * radius, a.Y - uy * end + ux * radius)
            };
            foreach (var zone in visibleZones) {
                if (!Finite(zone.X) || !Finite(zone.Y) || !Finite(zone.Right) || !Finite(zone.Bottom))
                    throw new ArgumentOutOfRangeException(nameof(visibleZones), "Visible screen edges must be finite");
                if (zone.Width <= 0 || zone.Height <= 0) continue;
                var polygon = Clip(quad, true, zone.X, true);
                polygon = Clip(polygon, true, zone.Right, false);
                polygon = Clip(polygon, false, zone.Y, true);
                polygon = Clip(polygon, false, zone.Bottom, false);
                if (polygon.Count < 3) continue;
                var first = Submitted(polygon[0]);
                for (int i = 1; i + 1 < polygon.Count; i++) {
                    var second = Submitted(polygon[i]);
                    var third = Submitted(polygon[i + 1]);
                    // The public output is the actual float geometry sent to GL. A collapsed edge
                    // after conversion contributes no drawable area, including boundary-only contact.
                    double area = (second.X - first.X) * (third.Y - first.Y) - (second.Y - first.Y) * (third.X - first.X);
                    if (area != 0) triangles.Add(new ScreenTriangle(first, second, third));
                }
            }
            return triangles;
        }

        public static IReadOnlyList<ScreenTriangle> ClipTriangles(IEnumerable<ScreenTriangle> source,
            IEnumerable<ScreenRect> visibleZones) {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (visibleZones == null) throw new ArgumentNullException(nameof(visibleZones));
            var zones = new List<ScreenRect>(visibleZones);
            var result = new List<ScreenTriangle>();
            foreach (var triangle in source) foreach (var zone in zones) {
                if (!Finite(zone.X) || !Finite(zone.Y) || !Finite(zone.Right) || !Finite(zone.Bottom))
                    throw new ArgumentOutOfRangeException(nameof(visibleZones));
                if (zone.Width <= 0 || zone.Height <= 0) continue;
                var polygon = new List<ScreenPoint> { triangle.A, triangle.B, triangle.C };
                polygon = Clip(polygon, true, zone.X, true);
                polygon = Clip(polygon, true, zone.Right, false);
                polygon = Clip(polygon, false, zone.Y, true);
                polygon = Clip(polygon, false, zone.Bottom, false);
                if (polygon.Count < 3) continue;
                var first = Submitted(polygon[0]);
                for (int i = 1; i + 1 < polygon.Count; i++) {
                    var second = Submitted(polygon[i]);
                    var third = Submitted(polygon[i + 1]);
                    double area = (second.X - first.X) * (third.Y - first.Y) - (second.Y - first.Y) * (third.X - first.X);
                    if (area != 0) result.Add(new ScreenTriangle(first, second, third));
                }
            }
            return result;
        }

        static bool Finite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value);
        static ScreenPoint Submitted(ScreenPoint point) => new ScreenPoint((float)point.X, (float)point.Y);

        // Clip the convex quad before triangulation. Intersections retain the exact clipping edge.
        static List<ScreenPoint> Clip(List<ScreenPoint> polygon, bool xAxis, double edge, bool greater) {
            var result = new List<ScreenPoint>();
            if (polygon.Count == 0) return result;
            var previous = polygon[polygon.Count - 1];
            double previousCoordinate = xAxis ? previous.X : previous.Y;
            bool previousInside = greater ? previousCoordinate >= edge : previousCoordinate <= edge;
            foreach (var current in polygon) {
                double coordinate = xAxis ? current.X : current.Y;
                bool inside = greater ? coordinate >= edge : coordinate <= edge;
                if (inside != previousInside) {
                    double t = (edge - previousCoordinate) / (coordinate - previousCoordinate);
                    result.Add(xAxis
                        ? new ScreenPoint(edge, previous.Y + t * (current.Y - previous.Y))
                        : new ScreenPoint(previous.X + t * (current.X - previous.X), edge));
                }
                if (inside) result.Add(current);
                previous = current;
                previousCoordinate = coordinate;
                previousInside = inside;
            }
            return result;
        }

        // The area a set of triangles covers, as horizontal spans one pixel high, each pixel once. The pieces of a
        // track ribbon overlap one another by construction; drawn one by one with a translucent colour, the overlaps
        // blend twice and show as darker patches that move with the camera. Drawn as these spans, the ribbon has one
        // tint whatever the angle. A span covers a row when the row's centre line crosses a triangle.
        public static IReadOnlyList<ScreenRect> Coverage(IEnumerable<ScreenTriangle> triangles) {
            var rows = new SortedDictionary<int, List<double[]>>();
            foreach (var t in triangles ?? Enumerable.Empty<ScreenTriangle>()) {
                double minY = Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), maxY = Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y));
                if (Double.IsNaN(minY) || Double.IsNaN(maxY) || Double.IsInfinity(minY) || Double.IsInfinity(maxY) || maxY - minY > 100000) continue;
                var edges = new[] { new[] { t.A, t.B }, new[] { t.B, t.C }, new[] { t.C, t.A } };
                for (int row = (int)Math.Floor(minY); row <= (int)Math.Floor(maxY); row++) {
                    double y = row + 0.5, left = Double.PositiveInfinity, right = Double.NegativeInfinity;
                    if (y < minY || y > maxY) continue;
                    foreach (var edge in edges) {
                        double y0 = edge[0].Y, y1 = edge[1].Y;
                        if ((y < y0 && y < y1) || (y > y0 && y > y1) || y0 == y1) continue;
                        var x = edge[0].X + (edge[1].X - edge[0].X) * (y - y0) / (y1 - y0);
                        if (x < left) left = x;
                        if (x > right) right = x;
                    }
                    if (!(right > left)) continue;
                    if (!rows.TryGetValue(row, out var spans)) rows.Add(row, spans = new List<double[]>());
                    spans.Add(new[] { left, right });
                }
            }
            var result = new List<ScreenRect>();
            foreach (var row in rows) {
                row.Value.Sort((a, b) => a[0].CompareTo(b[0]));
                double start = row.Value[0][0], end = row.Value[0][1];
                foreach (var span in row.Value) {
                    if (span[0] <= end) { if (span[1] > end) end = span[1]; continue; }
                    result.Add(ScreenRect.FromEdges((float)start, row.Key, (float)end, row.Key + 1));
                    start = span[0]; end = span[1];
                }
                result.Add(ScreenRect.FromEdges((float)start, row.Key, (float)end, row.Key + 1));
            }
            return result;
        }
    }
}
