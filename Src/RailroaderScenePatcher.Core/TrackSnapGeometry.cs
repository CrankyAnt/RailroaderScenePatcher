// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    internal static class TrackSnapGeometry {
        internal static double[] Position(SnapGeometry geometry, double[] origin) {
            var points = geometry.Points;
            var count = geometry.Candidate.Identity.Kind == SelectionKind.TrackNode ? 1 : 4;
            if (points.Count != count || points.Any(p => p.Count != 3 || p.Any(v => Double.IsNaN(v) || Double.IsInfinity(v))))
                throw new InvalidOperationException("Snap target has no finite map-coordinate geometry");
            if (count == 1) return points[0].ToArray();
            if (points.Skip(1).All(p => p.SequenceEqual(points[0]))) throw new InvalidOperationException("Selected track curve has no length");

            // B(t) - origin in power form. Stationary squared distances are the real roots
            // of (B(t) - origin) dot B'(t), a polynomial of degree at most five.
            var coefficients = new double[3][];
            var derivative = new double[6];
            for (var axis = 0; axis < 3; axis++) {
                var a = points[0][axis]; var b = points[1][axis]; var c = points[2][axis]; var d = points[3][axis];
                var p = new[] { a - origin[axis], 3 * (b - a), 3 * (c - 2 * b + a), d - 3 * c + 3 * b - a };
                coefficients[axis] = p;
                for (var i = 0; i < 4; i++) for (var j = 1; j < 4; j++) derivative[i + j - 1] += p[i] * j * p[j];
            }
            if (derivative.Any(v => Double.IsInfinity(v) || Double.IsNaN(v))) throw new InvalidOperationException("Snap geometry exceeds the supported numeric range");
            var candidates = new List<double> { 0, 1 }; candidates.AddRange(Roots(derivative));
            double best = 0, distance = Double.PositiveInfinity;
            foreach (var t in candidates.OrderBy(v => v)) {
                var squared = coefficients.Sum(p => { var v = Evaluate(p, t); return v * v; });
                if (squared < distance) { best = t; distance = squared; }
            }
            if (Double.IsInfinity(distance)) throw new InvalidOperationException("Snap distance exceeds the supported numeric range");
            // Evaluate in Bernstein form so large map positions do not enter the power coefficients again.
            var s = 1 - best;
            return Enumerable.Range(0, 3).Select(i => s * s * s * points[0][i] + 3 * s * s * best * points[1][i] +
                3 * s * best * best * points[2][i] + best * best * best * points[3][i]).ToArray();
        }

        static double Evaluate(double[] p, double t) {
            double value = 0;
            for (var i = p.Length - 1; i >= 0; i--) value = value * t + p[i];
            return value;
        }
        // Derivative roots split [0,1] into monotonic intervals. Check the boundaries too,
        // so an even-multiplicity root and an endpoint cannot disappear in a sign test.
        static IEnumerable<double> Roots(double[] polynomial) {
            var scale = polynomial.Max(Math.Abs);
            if (scale == 0) return Array.Empty<double>();
            var p = polynomial.Select(v => v / scale).ToArray();
            while (p.Length > 1 && p[p.Length - 1] == 0) Array.Resize(ref p, p.Length - 1);
            if (p.Length == 1) return Array.Empty<double>();
            var boundaries = new List<double> { 0, 1 };
            if (p.Length > 2) boundaries.AddRange(Roots(p.Skip(1).Select((v, i) => v * (i + 1)).ToArray()));
            boundaries.Sort();
            var roots = new List<double>();
            foreach (var t in boundaries) if (Math.Abs(Evaluate(p, t)) < 1e-12) roots.Add(t);
            for (var i = 1; i < boundaries.Count; i++) {
                var lo = boundaries[i - 1]; var hi = boundaries[i]; var flo = Evaluate(p, lo); var fhi = Evaluate(p, hi);
                if (flo == 0 || fhi == 0 || Math.Sign(flo) == Math.Sign(fhi)) continue;
                for (var step = 0; step < 64; step++) {
                    var middle = (lo + hi) / 2; var value = Evaluate(p, middle);
                    if (value == 0) { lo = hi = middle; break; }
                    if (Math.Sign(value) == Math.Sign(flo)) { lo = middle; flo = value; } else hi = middle;
                }
                roots.Add((lo + hi) / 2);
            }
            return roots;
        }
    }
}
