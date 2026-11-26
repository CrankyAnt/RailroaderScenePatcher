// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The sequence numbers of objects that share a hierarchy path and component, such as the two Loc: boards at the
    // ends of a place: "Loc: Andrews (1)", "(2)". They run west to east along the main line, from Andrews towards
    // Sylva, and on the Alarka branch from the Alarka end towards Alarka Jct. Only the shown name carries the number.
    //
    // Each line is a chain of the places the game's map data names, in order. An object belongs to the line whose
    // chain passes closest to it and stands at its distance along that chain; past either end of a chain the end
    // segment continues. The objects of one path are numbered along the Alarka branch when every one of them belongs
    // to it, else along the main line. A map without these places numbers them by map x, then z: west to east.
    public static class SharedPathSequence {
        public static readonly IReadOnlyList<string> MainLine = new[] {
            "Andrews", "Rhodo", "Topton", "Nantahala", "Hewitt", "Wesser", "Almond", "McClain", "Alarka Jct", "Hemingway", "Bryson", "Ela", "Whittier", "Wilmot", "Barker's Creek", "Dillsboro", "Sylva" };
        public static readonly IReadOnlyList<string> AlarkaBranch = new[] { "Alarka", "Cochran", "Alarka Jct" };

        // The number of each point, 1 for the first, in the order the points are given. Points are map x/z.
        public static IReadOnlyList<int> Numbers(IReadOnlyList<double[]> points, ScenePlaces places) {
            var main = Chain(MainLine, places);
            var branch = Chain(AlarkaBranch, places);
            Func<double[], double> along;
            if (main == null) along = null;
            else if (branch != null && points.All(p => Project(branch, p).Distance < Project(main, p).Distance)) along = p => Project(branch, p).Along;
            else along = p => Project(main, p).Along;
            var order = Enumerable.Range(0, points.Count)
                .OrderBy(i => along == null ? points[i][0] : along(points[i])).ThenBy(i => points[i][0]).ThenBy(i => points[i][1]).ToArray();
            var numbers = new int[points.Count];
            for (var rank = 0; rank < order.Length; rank++) numbers[order[rank]] = rank + 1;
            return numbers;
        }

        // The chain of a line's places, or null when the map names fewer than two of them.
        static List<double[]> Chain(IReadOnlyList<string> names, ScenePlaces places) {
            if (places == null) return null;
            var chain = new List<double[]>();
            foreach (var name in names) {
                var place = places.All.FirstOrDefault(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Points.Count > 0);
                if (place != null) chain.Add(place.Points[0]);
            }
            return chain.Count < 2 ? null : chain;
        }

        // How far a point stands from a chain, and its distance along it.
        static (double Distance, double Along) Project(List<double[]> chain, double[] p) {
            var best = (Distance: Double.MaxValue, Along: 0.0);
            var start = 0.0;
            for (var i = 0; i + 1 < chain.Count; i++) {
                var a = chain[i]; var b = chain[i + 1];
                double dx = b[0] - a[0], dz = b[1] - a[1];
                var length = Math.Sqrt(dx * dx + dz * dz);
                if (length == 0) continue;
                var t = ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / length;
                // The first and the last segment continue past the chain's ends.
                var clamped = Math.Max(i == 0 ? Double.NegativeInfinity : 0, Math.Min(i + 2 == chain.Count ? Double.PositiveInfinity : length, t));
                var near = Math.Max(0, Math.Min(length, t));
                double nx = a[0] + dx / length * near - p[0], nz = a[1] + dz / length * near - p[1];
                var distance = Math.Sqrt(nx * nx + nz * nz);
                if (distance < best.Distance) best = (distance, start + clamped);
                start += length;
            }
            return best;
        }
    }
}
