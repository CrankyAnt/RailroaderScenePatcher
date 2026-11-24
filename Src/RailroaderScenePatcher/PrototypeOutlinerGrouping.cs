// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: throwaway outliner grouping for the question "how should the outliner group about 1,900 scene objects,
// by kind or by place?". Not production code: state in memory only. The rules that decide category, kind and place
// are in the core (SceneClassification, ScenePlaces) and the tree is in OutlineTree.cs; places are read in
// PrototypeOutlinerPlaces.cs and crossings are grouped in PrototypeOutlinerCrossings.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RailroaderScenePatcher.Core;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

namespace RailroaderScenePatcher {
    internal static partial class PrototypeOutlinerGrouping {
        // One tree across families; the top level by category or by place.
        internal static bool ByPlace;
        internal static bool LogPending = true;
        // Where warnings go; the offline harness replaces it, because the patcher's log needs the engine.
        internal static Action<string> Warn = message => PatcherLog.Warning(message);
        static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        // One warning per kind of failure, so a failure on each of 1,900 objects writes one line.
        internal static void WarnOnce(string kind, string message) {
            if (warned.Add(kind)) Warn("The outliner grouping could not read something: " + message);
        }

        // A scene object's identity in the hierarchy: its scene and path.
        readonly struct ScenePath : IEquatable<ScenePath> {
            internal readonly string Scene, Path;
            internal ScenePath(string scene, string path) { Scene = scene; Path = path; }
            public bool Equals(ScenePath other) => Scene == other.Scene && Path == other.Path;
            public override bool Equals(object other) => other is ScenePath path && Equals(path);
            public override int GetHashCode() => ((Scene?.GetHashCode() ?? 0) * 397) ^ (Path?.GetHashCode() ?? 0);
            public override string ToString() => Scene + "\n" + Path;
        }
        // One node the tree places: an object with its parts, spans or crossing members beneath it, or an unprotected
        // crossing (Row null, Members set).
        sealed class Unit {
            internal OutlineNode Node;
            internal SceneObservation Row;
            internal List<Unit> Members = new List<Unit>();
            internal string Category, Kind, Place, CrossingPath;
        }
        // A node without an inventory row that stands for a game-defined group: where Focus goes, in map coordinates.
        internal sealed class GroupNode { internal string Label; internal double[] Point; }
        internal sealed class Result {
            internal IReadOnlyList<OutlineNode> Tops;
            internal int Listed, Unplaced;
            internal List<string> Hidden = new List<string>();
            internal List<string> CatchAll = new List<string>();
            internal List<string> Violations = new List<string>();
            internal List<string> InspectionOnlyRows = new List<string>();
            internal List<string> Crossings = new List<string>();
            // The crossing nodes of the tree, by node key.
            internal Dictionary<string, GroupNode> GroupNodes = new Dictionary<string, GroupNode>(StringComparer.Ordinal);
            // Each row's category, kind and place, for its tooltip.
            internal Dictionary<SceneObservation, string[]> Shown = new Dictionary<SceneObservation, string[]>();
        }
        // The state of one Build.
        sealed class BuildContext {
            internal IReadOnlyList<SceneObservation> Rows, Entries;
            internal string Search;
            internal bool Narrowed, PlaceView, Diagnostics;
            internal Func<SceneObservation, double[]> TrackPoint;
            internal readonly Result Result = new Result();
            internal readonly List<Unit> Units = new List<Unit>();
            internal readonly Dictionary<ScenePath, Unit> ByPath = new Dictionary<ScenePath, Unit>();
            internal readonly Dictionary<ScenePath, SceneObservation> RootEntries = new Dictionary<ScenePath, SceneObservation>();
            internal readonly HashSet<ScenePath> ContainerObjects = new HashSet<ScenePath>();
            internal readonly HashSet<SceneObservation> Hidden = new HashSet<SceneObservation>();
            internal Unit Make(SceneObservation row, ScenePath at, bool context) {
                var label = row == null ? Last(at.Path) : OwnName(row);
                var unit = new Unit { Row = row, Node = new OutlineNode { Key = at + (row?.Identity.PadMarkerId == null ? "" : "\n" + row.Identity.PadMarkerId), Label = label, Row = row, Context = context } };
                Units.Add(unit);
                if (!ByPath.ContainsKey(at)) ByPath[at] = unit;
                return unit;
            }
        }
        static IReadOnlyList<OutlineNode> lastTops;
        static HashSet<string> kindCategories = new HashSet<string>(StringComparer.Ordinal);
        static IReadOnlyList<SceneObservation> kindsFrom;

        static string Last(string path) => path.Substring(path.LastIndexOf('/') + 1);
        // A row shows the game's own name of its object. A part is named with its group, and an object the patcher
        // added has no scene name of the game: both keep the name the inventory gives them.
        static string OwnName(SceneObservation row) =>
            row.GroupPath != null || CommandBand.IsAddedFamily(row.Family) || row.Identity.Path.StartsWith(CrossingCoverageWorld.RootName, StringComparison.Ordinal) ? InventoryPresentationState.Name(row) : Last(row.Identity.Path);
        static string Parent(string path) { var at = path.LastIndexOf('/'); return at < 0 ? null : path.Substring(0, at); }
        static string Round(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

        // A Sign group that holds only signs other families list gets no row (Bryson Signs, Ela Signs, Sylva Signs);
        // its signs are rows of their own.
        internal static bool IsContainer(SceneObservation r) => r.GroupPath == null && r.Category == BakedSceneryCategories.Sign && r.InspectionOnly != null;
        // A serialized track span that no crossing controller holds is a native track span and leaves the list.
        internal static bool IsNativeSpan(SceneObservation r) => r.Family == SpanFamily && r.HolderPath == null && !r.Identity.Path.StartsWith(CrossingCoverageWorld.RootName, StringComparison.Ordinal);
        internal static int HiddenCount(IReadOnlyList<SceneObservation> entries) => entries == null ? 0 : entries.Count(r => IsContainer(r) || IsNativeSpan(r));

        // ---- search covering place, kind and crossing ----
        static string GroupingSearchText(SceneObservation e, Func<SceneObservation, double[]> trackPoint) {
            if (searchTags.TryGetValue(e, out var cached)) return cached;
            var found = e.GroupPath == null && e.HolderPath == null ? ClassOf(e) : null;
            var place = e.GroupPath == null && e.HolderPath == null ? PlaceOf(e, trackPoint) : null;
            var crossing = CanJoinCrossing(e) ? Last(Parent(e.Identity.Path) ?? "") : null;
            var text = String.Join(" ", new[] { found?.Category, found?.Kind, crossing }.Concat(place?.Names ?? new string[0]).Where(s => !String.IsNullOrEmpty(s)));
            return searchTags[e] = text;
        }
        static bool MatchesGrouping(SceneObservation e, string search, Func<SceneObservation, double[]> trackPoint) =>
            !String.IsNullOrEmpty(search) && GroupingSearchText(e, trackPoint).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        // The outliner rows: the filter as today, and a search that also matches each object's place, kind and crossing.
        internal static IReadOnlyList<SceneObservation> Rows(SceneInventory inventory, string search, OutlinerFilter filter, Func<SceneObservation, bool> changed, Func<SceneObservation, double[]> trackPoint) {
            if (inventory == null) return Array.Empty<SceneObservation>();
            Prepare(inventory.Entries, trackPoint);
            var hasSearch = !String.IsNullOrEmpty(search);
            var matched = hasSearch ? new HashSet<SceneObservation>(inventory.Search(search)) : null;
            return inventory.Entries.Where(entry => {
                if (filter == OutlinerFilter.Problems && entry.Problems.Count == 0) return false;
                if (filter == OutlinerFilter.Disabled && entry.Enabled) return false;
                if (filter == OutlinerFilter.Changed && (changed == null || !changed(entry))) return false;
                return !hasSearch || matched.Contains(entry) || MatchesGrouping(entry, search, trackPoint);
            }).ToArray();
        }

        // ---- the tree ----
        internal static Result Build(IReadOnlyList<SceneObservation> rows, IReadOnlyList<SceneObservation> inventory, string search, bool narrowed, Func<SceneObservation, double[]> trackPoint) {
            // A failed build leaves no tree behind for Fit to measure.
            lastTops = null;
            var entries = inventory ?? rows ?? Array.Empty<SceneObservation>();
            Prepare(entries, trackPoint);
            // Which categories show kinds is decided on the whole inventory, so a search or filter changes no level.
            if (!ReferenceEquals(kindsFrom, entries) && entries.Count != 0) {
                var whole = Shape(new BuildContext { Rows = entries, Entries = entries, Search = "", TrackPoint = trackPoint });
                kindCategories = OutlineTree.KindCategories(whole.Units.Select(Entry));
                kindsFrom = entries;
            }
            var b = Shape(new BuildContext { Rows = rows ?? Array.Empty<SceneObservation>(), Entries = entries, Search = search, Narrowed = narrowed, PlaceView = ByPlace, Diagnostics = LogPending, TrackPoint = trackPoint });
            var shown = b.Units.Select(Entry).ToArray();
            var tops = b.PlaceView ? OutlineTree.ByPlace(shown, kindCategories, narrowed ? null : places.All.Select(p => p.Name)) : OutlineTree.ByCategory(shown, kindCategories);
            foreach (var u in b.Units) {
                Remember(b, u.Node, u);
                if (u.Row == null && u.CrossingPath != null) b.Result.GroupNodes[u.Node.Key] = new GroupNode { Label = u.Node.Label, Point = Centre(u) };
            }
            b.Result.Tops = lastTops = tops;
            b.Result.Listed = tops.Sum(t => t.Count);
            b.Result.Unplaced = tops.Where(t => t.Label == OutlineTree.NoPlace || t.Label == BakedSceneryCategories.Unclassified).Sum(t => t.Count);
            if (b.Diagnostics) {
                b.Result.CatchAll.AddRange(b.Units.Where(u => u.Place == null || u.Category == BakedSceneryCategories.Unclassified).Select(Describe));
                b.Result.Violations.AddRange(OutlineTree.Violations(tops, b.PlaceView, places.All.Select(p => p.Name)));
            }
            return b.Result;
        }
        static BuildContext Shape(BuildContext b) {
            HideContainersAndNativeSpans(b);
            BuildUnits(b);
            GroupProtectedCrossings(b);
            GroupUnprotectedCrossings(b);
            PlaceUnits(b);
            return b;
        }
        static OutlineEntry Entry(Unit u) => new OutlineEntry { Node = u.Node, Category = u.Category, Kind = u.Kind, Place = u.Place };
        static void Remember(BuildContext b, OutlineNode node, Unit unit) {
            if (node.Row != null) b.Result.Shown[node.Row] = new[] { unit.Category, unit.Kind, unit.Place };
            foreach (var child in node.Children) Remember(b, child, unit);
        }
        // The middle of a crossing's members, in map coordinates, where Focus goes.
        static double[] Centre(Unit u) {
            var at = u.Members.Select(m => m.Row).Where(r => r != null && HasPosition(r)).Select(r => r.Position).ToArray();
            return at.Length == 0 ? null : new[] { at.Average(p => p[0]), at.Average(p => p[1]), at.Average(p => p[2]) };
        }

        // Container objects and native track spans leave the list.
        static void HideContainersAndNativeSpans(BuildContext b) {
            foreach (var r in b.Rows.Where(r => IsContainer(r) || IsNativeSpan(r))) {
                b.Hidden.Add(r);
                if (b.Diagnostics) b.Result.Hidden.Add((IsContainer(r) ? "container object " : "native track span ") + r.Identity.Path);
            }
            foreach (var e in b.Entries) {
                var at = new ScenePath(e.Identity.Scene, e.Identity.Path);
                if (IsContainer(e)) b.ContainerObjects.Add(at);
                if (e.GroupPath == null && e.HolderPath == null && !b.RootEntries.ContainsKey(at)) b.RootEntries[at] = e;
            }
        }
        // Units: every object outside a group and outside a controller, with its parts or detection spans beneath it. A
        // part or span whose holder the search left out brings its holder, dimmed. A part of a container object stands
        // on its own.
        static void BuildUnits(BuildContext b) {
            bool InContainer(SceneObservation r) => r.GroupPath != null && b.ContainerObjects.Contains(new ScenePath(r.Identity.Scene, r.GroupPath));
            foreach (var row in b.Rows.Where(r => !b.Hidden.Contains(r) && (r.GroupPath == null && r.HolderPath == null || InContainer(r))))
                b.Make(row, new ScenePath(row.Identity.Scene, row.Identity.Path), false).Node.Count = 1;
            foreach (var row in b.Rows.Where(r => !b.Hidden.Contains(r) && (r.GroupPath != null && !InContainer(r) || r.HolderPath != null))) {
                var holderAt = new ScenePath(row.Identity.Scene, row.GroupPath ?? row.HolderPath);
                if (!b.ByPath.TryGetValue(holderAt, out var holder)) {
                    b.RootEntries.TryGetValue(holderAt, out var found);
                    holder = b.Make(found, holderAt, true);
                }
                holder.Node.Children.Add(new OutlineNode { Key = new ScenePath(row.Identity.Scene, row.Identity.Path).ToString(), Label = row.GroupPath != null ? InventoryPresentationState.PartLabel(row) : OwnName(row), Row = row, Count = 1 });
                holder.Node.Count++;
            }
            foreach (var u in b.Units) {
                // A group that holds matching parts but does not match itself is shown dimmed, only to hold them.
                if (!u.Node.Context && u.Row != null && u.Node.Children.Count != 0 && u.Row.GroupPath == null && u.Row.HolderPath == null && u.Row.Family == BakedSceneryRules.Family
                    && !u.Row.MatchesItself(b.Search) && !MatchesGrouping(u.Row, b.Search, b.TrackPoint)) { u.Node.Context = true; u.Node.Count--; }
                u.Node.Children.Sort((x, y) => { var by = OutlineTree.NaturalComparer.Instance.Compare(x.Label, y.Label); return by != 0 ? by : StringComparer.Ordinal.Compare(x.Row.Identity.Path, y.Row.Identity.Path); });
                var found = u.Row == null ? SceneClassification.Class(BakedSceneryCategories.Unclassified, null) : ClassOf(u.Row);
                u.Category = found.Category; u.Kind = found.Kind;
            }
        }
        // The place of each unit; an unprotected crossing stands where its crossing object is, at the mean of its
        // members. A crossing is one with signals when a crossing controller holds it, and one without otherwise.
        static void PlaceUnits(BuildContext b) {
            foreach (var u in b.Units) {
                if (u.Row != null) u.Place = PlaceOf(u.Row, b.TrackPoint)?.Name;
                else {
                    var positions = u.Members.Select(m => Measure(m.Row, b.TrackPoint)).Where(p => p != null).ToArray();
                    u.Place = places.Of(u.CrossingPath, positions.Length == 0 ? null : new[] { positions.Average(p => p[0]), positions.Average(p => p[1]) })?.Name;
                }
                if (u.Category == BakedSceneryCategories.GradeCrossing) u.Kind = u.Row?.Family == ControllerFamily ? SceneClassification.ProtectedCrossings : SceneClassification.UnprotectedCrossings;
                if (b.Diagnostics && u.Row?.InspectionOnly != null) b.Result.InspectionOnlyRows.Add(u.Node.Label + " (" + u.Row.Identity.Path + ") [" + u.Row.Family + "] under " + u.Category + ": " + u.Row.InspectionOnly);
            }
        }
        static string Describe(Unit u) => u.Node.Label + " [" + (u.Row?.Family ?? "no row") + "] " + (u.Row?.Identity.Path ?? u.Node.Key.Replace("\n", " "));

        // What a row's tooltip says: the object's name and hierarchy path, where the outliner lists it, and what the
        // row's markers mean. It names no internal family and no rule: only what the outliner itself shows.
        internal static string Tooltip(Result result, SceneObservation item, bool changed, FailureKind? failure) {
            var equipment = CommandBand.IsAddedFamily(item.Family);
            string[] shown = null;
            result?.Shown.TryGetValue(item, out shown);
            var lines = new List<string> { OwnName(item) };
            if (!equipment) lines.Add(item.Identity.Path);
            if (shown != null) {
                lines.Add(shown[0] + (shown[1] == null ? "" : " / " + shown[1]));
                if (shown[2] != null) lines.Add("Place: " + shown[2]);
            }
            if (!item.Active) lines.Add("Inactive");
            if (item.InspectionOnly != null) lines.Add(CommandBand.InspectionOnlyLine(item.InspectionOnly));
            if (failure != null) lines.Add(CommandBand.FailureTooltipLine(failure.Value));
            else if (changed) lines.Add(ChangedLine);
            if (item.Problems.Count != 0) {
                if (equipment) lines.Add("Detected problems: " + item.Problems.Count + ". The Problems section of the Object tab lists them.");
                else lines.AddRange(item.Problems.Select(problem => "Problem: " + problem));
            }
            return String.Join("\n", lines).Replace("<", "\\u003c").Replace(">", "\\u003e");
        }
        // Why a row is marked as changed, also when nothing was edited in this session.
        internal const string ChangedLine = "Changed: this mod's scene patch file (Data/scene-patches.json) changes this object. The Changes tab lists the changes.";
        internal static string FolderTooltip(OutlineNode node) =>
            (node.Label + "\n" + node.Count.ToString(CultureInfo.InvariantCulture) + (node.Count == 1 ? " object" : " objects")).Replace("<", "\\u003c").Replace(">", "\\u003e");

        // Widths of the visible prototype rows including their nesting indent, for Fit.
        const float IndentPerLevel = 16f;
        internal static float[] FitWidths(TMPro.TMP_Text measure) {
            if (measure == null || lastTops == null || lastTops.Count == 0) return null;
            var widths = new List<float>();
            void Walk(OutlineNode node, int depth) {
                var text = (node.Row == null ? "(" + node.Count.ToString(CultureInfo.InvariantCulture) + ") " : "") + (node.Label ?? "");
                // The row decoration already covers one folder level; each deeper level adds its indent.
                widths.Add(measure.GetPreferredValues(text).x + Math.Max(0, depth - 1) * IndentPerLevel);
                foreach (var child in node.Children) Walk(child, depth + 1);
            }
            foreach (var top in lastTops) Walk(top, 0);
            return widths.ToArray();
        }

        static bool detailLogged;
        // Debug lines, one per view switch: view, top-level nodes, objects listed, objects in a catch-all node, and
        // what breaks the rules of the tree. The detail lines follow with an empty search: places, crossings,
        // Inspection only, hidden objects.
        internal static void Log(Result result, int rowsShown, string search, string filter) {
            var tops = String.Join(", ", result.Tops.Select(t => t.Label + "=" + t.Count.ToString(CultureInfo.InvariantCulture)));
            PatcherLog.Debug("Outliner grouping: view=" + (ByPlace ? "Place" : "Category") + " tops=" + result.Tops.Count
                + " listed=" + result.Listed + " shown=" + rowsShown + " hidden=" + result.Hidden.Count + " catchAll=" + result.Unplaced + " dropped=" + (rowsShown - result.Hidden.Count - result.Listed)
                + " filter=" + filter + " search='" + search + "' kinds={" + String.Join(", ", kindCategories.OrderBy(k => k, StringComparer.Ordinal)) + "} [" + tops + "]");
            PatcherLog.Debug("Outliner grouping detail: catchAll=" + result.CatchAll.Count + " {" + String.Join(" | ", result.CatchAll) + "} violations=" + result.Violations.Count + " {" + String.Join(" | ", result.Violations) + "}");
            if (detailLogged && !String.IsNullOrEmpty(search)) return;
            detailLogged = true;
            PatcherLog.Debug("Outliner grouping places: " + placesLine);
            PatcherLog.Debug("Outliner grouping crossings: " + result.Crossings.Count + " {" + String.Join(" | ", result.Crossings) + "}");
            PatcherLog.Debug("Outliner grouping inspection only: " + result.InspectionOnlyRows.Count + " {" + String.Join(" | ", result.InspectionOnlyRows) + "}");
            PatcherLog.Debug("Outliner grouping hidden: " + result.Hidden.Count + " {" + String.Join(" | ", result.Hidden) + "}");
        }
    }
}
