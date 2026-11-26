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
    // A complex that shows as one square marker, as the pointer meets it: where that marker stands and
    // whether the pointer is inside the group's frame, the objects the group holds, as indices into the targets, and
    // those of them that show their own marker and frame: a candidate and the inspected object.
    public sealed class PickGroup {
        public PickGroup(PickTarget target, IReadOnlyList<int> objects, IReadOnlyList<int> own = null, IReadOnlyList<int> unlisted = null, ComplexKind? kind = null) {
            Target = target; Objects = objects ?? throw new ArgumentNullException(nameof(objects)); Own = own ?? Array.Empty<int>(); Unlisted = unlisted ?? Array.Empty<int>(); Kind = kind;
        }
        public ComplexKind? Kind { get; }
        public PickTarget Target { get; }
        public IReadOnlyList<int> Objects { get; }
        public IReadOnlyList<int> Own { get; }
        // Objects of the group that its choice list leaves out, such as the detection spans of a grade-crossing setup.
        // They still take no part on their own unless they show on their own.
        public IReadOnlyList<int> Unlisted { get; }
    }
    // What a click selects among objects and groups: the objects, the winner's first, and the groups it picked, as
    // indices into the groups. The frame of a picked group is the one the pointer marks.
    public sealed class WorldPickResult {
        internal WorldPickResult(IReadOnlyList<int> objects, IReadOnlyList<int> groups, IReadOnlyList<int> hoveredObjects, bool isCrossingSetup) {
            Objects = objects; Groups = groups; HoveredObjects = hoveredObjects; IsCrossingSetup = isCrossingSetup;
        }
        public IReadOnlyList<int> Objects { get; }
        public IReadOnlyList<int> Groups { get; }
        // An offer included because it shares a spot can still be outside the pointer's own hit area.
        public IReadOnlyList<int> HoveredObjects { get; }
        // A single grade-crossing setup was picked, and every offered object is listed by that setup.
        public bool IsCrossingSetup { get; }
    }
    // Which candidate a click selects, and so which one the pointer marks before the click. One rule for every world
    // choice: Select in world, a replacement object, segment or node, and the snap and follow picks.
    public static class WorldPick {
        // Two markers whose centres are closer than this on screen cannot be told apart by a click: half a marker.
        // The marker is 24 pixels and opaque, and its cross is 10 pixels wide. Closer than 12 pixels the front marker
        // hides more than half of the one behind it and the two crosses touch or overlap, so they read as one "+";
        // from 12 pixels on, each marker shows a half of its own that a click can aim at.
        public const double SameSpot = ObjectMarkingGeometry.MarkerSize / 2;
        static bool OnMarker(PickTarget target, double x, double y) => target.Marked &&
            Math.Abs(target.X - x) <= SameSpot && Math.Abs(target.Y - y) <= SameSpot;
        // The candidates a click at the pointer selects, as indices into targets: none, one, or the candidates of
        // one spot, the nearest first, from which the user chooses in a list.
        //
        // A click on a shown "+" selects the object of that "+": of the markers under the pointer, the one whose
        // centre is nearest. A click on no "+" but inside frames or on a ribbon selects, of those candidates, the one
        // whose "+" is nearest on screen. Distances are screen distances, not world distances. Whatever was selected,
        // every candidate whose shown marker stands on the same spot comes with it: the click cannot tell them apart.
        public static IReadOnlyList<int> Resolve(double x, double y, IReadOnlyList<PickTarget> targets) {
            if (targets == null || targets.Count == 0) return Array.Empty<int>();
            double Distance(int i) { double dx = targets[i].X - x, dy = targets[i].Y - y; return dx * dx + dy * dy; }
            var all = Enumerable.Range(0, targets.Count).ToArray();
            var pool = all.Where(i => OnMarker(targets[i], x, y)).ToArray();
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
        // The click rule with complexes that show as one. A group's square marker and frame take part as
        // those of one object, in place of its objects other than those that show on their own. Where the rule picks
        // the group, its objects other than its unlisted ones are picked, in their order; objects and groups that
        // share the winner's spot come with it, and an object is listed once. Without groups this is the rule of one object each.
        public static WorldPickResult Resolve(double x, double y, IReadOnlyList<PickTarget> targets, IReadOnlyList<PickGroup> groups) {
            targets = targets ?? Array.Empty<PickTarget>(); groups = groups ?? Array.Empty<PickGroup>();
            var hidden = new HashSet<int>();
            foreach (var group in groups) foreach (var member in group.Objects) if (!group.Own.Contains(member)) hidden.Add(member);
            // What takes part: the objects that show on their own, then the groups; a group is a negative entry.
            var entries = new List<int>(); var shown = new List<PickTarget>();
            for (var i = 0; i < targets.Count; i++) if (!hidden.Contains(i)) { entries.Add(i); shown.Add(targets[i]); }
            for (var g = 0; g < groups.Count; g++) { entries.Add(-1 - g); shown.Add(groups[g].Target); }
            var objects = new List<int>(); var picked = new List<int>();
            foreach (var index in Resolve(x, y, shown)) {
                var entry = entries[index];
                if (entry >= 0) { if (!objects.Contains(entry)) objects.Add(entry); continue; }
                picked.Add(-1 - entry);
                var group = groups[-1 - entry];
                foreach (var member in group.Objects) if (member >= 0 && member < targets.Count && !group.Unlisted.Contains(member) && !objects.Contains(member)) objects.Add(member);
            }
            var hovered = objects.Where(i => !hidden.Contains(i) && (targets[i].Area || OnMarker(targets[i], x, y))).ToArray();
            var onlyGroup = picked.Count == 1 ? groups[picked[0]] : null;
            var isCrossingSetup = onlyGroup?.Kind == ComplexKind.GradeCrossingSetup && objects.Count > 0 &&
                objects.All(i => onlyGroup.Objects.Contains(i) && !onlyGroup.Unlisted.Contains(i));
            return new WorldPickResult(objects, picked, hovered, isCrossingSetup);
        }
        // The rows of the list a click on one spot opens, next to the pointer: to its right and from its height down,
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
