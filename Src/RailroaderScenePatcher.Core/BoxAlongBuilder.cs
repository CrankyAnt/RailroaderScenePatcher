// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

namespace RailroaderScenePatcher.Core {
    // The one box around points that follows what they make up, built from the points part by part, so that a large
    // mesh is read over several frames and no single read takes more than a frame's share.
    //
    // For an object that stands upright (ObjectMarkingGeometry.Stands), the box stands upright too and turns about
    // the vertical to lie along the points: of the boxes with a side along an edge of the points' outline seen from
    // above, the one with the least ground area. The box of the object's own axes stays unless that box is clearly
    // smaller, at most ObjectMarkingGeometry.FitShare of its size, so a model that lies nearly along its object's axes
    // or is nearly round keeps them. A group root that stands square to the world while its building lies along the
    // track, and a mesh built along a curve in axes of its own, such as a crossing pad, are framed along what they show.
    // A tilted object keeps the box of its own axes, which turns with it.
    public sealed class BoxAlongBuilder {
        readonly WorldPoint[] axes;
        readonly bool stands;
        readonly double[] min = { Double.PositiveInfinity, Double.PositiveInfinity, Double.PositiveInfinity };
        readonly double[] max = { Double.NegativeInfinity, Double.NegativeInfinity, Double.NegativeInfinity };
        // The outline seen from above of the points so far, and their lowest and highest point: all an upright box
        // along them needs.
        List<ScreenPoint> outline = new List<ScreenPoint>();
        double low = Double.PositiveInfinity, high = Double.NegativeInfinity;

        public BoxAlongBuilder(WorldBasis objectAxes) {
            axes = ObjectMarkingGeometry.Unit(objectAxes);
            stands = ObjectMarkingGeometry.Stands(objectAxes);
        }
        public void Add(IEnumerable<WorldPoint> points) {
            if (points == null) return;
            var flat = stands ? new List<ScreenPoint>(outline) : null;
            foreach (var p in points) {
                if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z)) continue;
                ObjectMarkingGeometry.Extend(axes, p, min, max);
                if (!stands) continue;
                flat.Add(new ScreenPoint(p.X, p.Z));
                low = Math.Min(low, p.Y); high = Math.Max(high, p.Y);
            }
            if (stands) outline = WorldRibbonGeometry.Hull(flat);
        }
        // The box of the points so far; null without points.
        public WorldPoint[] Box {
            get {
                if (min[0] > max[0]) return null;
                var axisBox = ObjectMarkingGeometry.Corners(axes, min, max);
                if (!stands || outline.Count < 2) return axisBox;
                double bestArea = Double.PositiveInfinity;
                double[] bestMin = null, bestMax = null; WorldPoint[] bestAxes = null;
                for (int i = 0; i < outline.Count; i++) {
                    ScreenPoint a = outline[i], b = outline[(i + 1) % outline.Count];
                    double ex = b.X - a.X, ez = b.Y - a.Y, length = Math.Sqrt(ex * ex + ez * ez);
                    if (!(length > 0)) continue;
                    ex /= length; ez /= length;
                    double minU = Double.PositiveInfinity, maxU = Double.NegativeInfinity, minV = Double.PositiveInfinity, maxV = Double.NegativeInfinity;
                    foreach (var q in outline) {
                        double u = q.X * ex + q.Y * ez, v = -q.X * ez + q.Y * ex;
                        minU = Math.Min(minU, u); maxU = Math.Max(maxU, u); minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
                    }
                    var area = (maxU - minU + ObjectMarkingGeometry.FitSlack) * (maxV - minV + ObjectMarkingGeometry.FitSlack);
                    if (area < bestArea * (1 - 1e-9)) {
                        bestArea = area;
                        bestAxes = new[] { new WorldPoint(ex, 0, ez), new WorldPoint(0, 1, 0), new WorldPoint(-ez, 0, ex) };
                        bestMin = new[] { minU, low, minV }; bestMax = new[] { maxU, high, maxV };
                    }
                }
                if (bestAxes == null) return axisBox;
                var alongBox = ObjectMarkingGeometry.Corners(bestAxes, bestMin, bestMax);
                return ObjectMarkingGeometry.Measure(alongBox) <= ObjectMarkingGeometry.FitShare * ObjectMarkingGeometry.Measure(axisBox) ? alongBox : axisBox;
            }
        }
        static bool Finite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value);
    }
}
