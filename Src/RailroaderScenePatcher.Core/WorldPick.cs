// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // One candidate of a world choice as the pointer meets it: the centre of its "+" marker on screen, whether that
    // marker is shown, and whether the pointer is inside its frame or on its track ribbon.
    public readonly struct PickTarget {
        public PickTarget(double x, double y, bool marked, bool area) { X = x; Y = y; Marked = marked; Area = area; }
        public double X { get; }
        public double Y { get; }
        public bool Marked { get; }
        public bool Area { get; }
    }
    // Which candidate a click selects, and so which one the pointer marks before the click. One rule for every world
    // choice: Select in world, a replacement object, segment or node, and the snap and follow picks.
    public static class WorldPick {
        // Two markers whose centres are closer than this on screen cannot be told apart by a click: half a marker.
        // The marker is 24 pixels and opaque, and its cross is 10 pixels wide. Closer than 12 pixels the front marker
        // hides more than half of the one behind it and the two crosses touch or overlap, so they read as one "+";
        // from 12 pixels on, each marker shows a half of its own that a click can aim at.
        public const double SameSpot = ObjectMarkingGeometry.MarkerSize / 2;
        // The candidates a click at the pointer selects, as indices into targets: none, one, or the candidates of
        // one spot, the nearest first, from which the user chooses in a list.
        //
        // A click on a shown "+" selects the object of that "+": of the markers under the pointer, the one whose
        // centre is nearest. A click on no "+" but inside frames or on a ribbon selects, of those candidates, the one
        // whose "+" is nearest on screen. Distances are screen distances, not world distances. Whatever was selected,
        // every candidate whose shown marker stands on the same spot comes with it: the click cannot tell them apart.
        public static IReadOnlyList<int> Resolve(double x, double y, IReadOnlyList<PickTarget> targets) {
            if (targets == null || targets.Count == 0) return Array.Empty<int>();
            const double half = ObjectMarkingGeometry.MarkerSize / 2;
            double Distance(int i) { double dx = targets[i].X - x, dy = targets[i].Y - y; return dx * dx + dy * dy; }
            var all = Enumerable.Range(0, targets.Count).ToArray();
            var pool = all.Where(i => targets[i].Marked && Math.Abs(targets[i].X - x) <= half && Math.Abs(targets[i].Y - y) <= half).ToArray();
            if (pool.Length == 0) pool = all.Where(i => targets[i].Area).ToArray();
            if (pool.Length == 0) return Array.Empty<int>();
            var winner = pool.OrderBy(Distance).ThenBy(i => i).First();
            bool Same(int i) {
                if (i == winner) return true;
                if (!targets[i].Marked || !targets[winner].Marked) return false;
                double dx = targets[i].X - targets[winner].X, dy = targets[i].Y - targets[winner].Y;
                return dx * dx + dy * dy < SameSpot * SameSpot;
            }
            return all.Where(Same).OrderBy(i => i == winner ? 0 : 1).ThenBy(Distance).ThenBy(i => i).ToArray();
        }
        // The rows of the list a click on one spot opens, beside the pointer: to its right and from its height down,
        // or to its left without room on the right, moved up when the list would leave the screen at the bottom. The
        // rows are as wide as the widest and stand directly under one another.
        public static IReadOnlyList<ScreenRect> ListBeside(double x, double y, double width, IReadOnlyList<double> heights, ScreenRect screen) {
            const double gap = 16;
            var total = heights.Sum() + ObjectMarkingGeometry.LabelStack * Math.Max(0, heights.Count - 1);
            var left = x + gap + width <= screen.Right ? x + gap : x - gap - width;
            left = Math.Max(screen.X, Math.Min(left, screen.Right - width));
            var top = Math.Max(screen.Y, Math.Min(y, screen.Bottom - total));
            var rows = new List<ScreenRect>();
            foreach (var height in heights) {
                rows.Add(ScreenRect.FromEdges((float)left, (float)top, (float)(left + width), (float)(top + height)));
                top += height + ObjectMarkingGeometry.LabelStack;
            }
            return rows;
        }
    }
}
