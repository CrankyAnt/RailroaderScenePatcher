// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RailroaderScenePatcher.Core;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

namespace RailroaderScenePatcher {
    // What the scene says about one object: the components on it, those at or below it, and the names below it.
    internal sealed class ObjectFacts { internal string[] Own = new string[0], AtOrBelow = new string[0], NamesBelow = new string[0]; }
    // What the outliner reads from the loaded world. Without one, an object is classified by its name and family and
    // stands at its observed position.
    internal interface IOutlinerScene {
        // Differs whenever the loaded scenes or the game's map data are other ones: the places are read again then.
        string WorldKey { get; }
        ObjectFacts Facts(SceneObservation entry);
        // Where the object stands as map x/z, from what it shows in the world, or null.
        double[] Bounds(SceneObservation entry);
        // The places the world names beside the listed objects: the game's map data, map labels, and Loc: objects the
        // inventory does not list. notes takes what could not be read.
        IEnumerable<PlaceSource> PlaceSources(IReadOnlyList<SceneObservation> entries, List<string> notes);
    }

    // The outliner as one tree across families, by category or by place. The rules that decide category, kind and
    // place are in the core (SceneClassification, ScenePlaces) and the levels of the tree are in OutlineTree. Here
    // the inventory's rows become the tree's rows: each object outside a group with its parts or detection spans
    // beneath it, and each crossing as one node (OutlinerCrossings.cs).
    internal static partial class OutlinerGrouping {
        internal static bool ByPlace { get; set; }
        // The next build writes the debug lines of the tree once.
        internal static bool LogPending { get; set; } = true;
        internal static IOutlinerScene Scene { get; set; }
        static readonly LogOnce warnings = new LogOnce();
        // One warning per kind of failure, so a failure on each of 1,900 objects writes one line.
        internal static void WarnOnce(string kind, string message) => warnings.Warning(kind, "The outliner could not read something: " + message);

        // A scene object's identity in the hierarchy: its scene and path.
        readonly struct ScenePath : IEquatable<ScenePath> {
            internal readonly string Scene, Path;
            internal ScenePath(string scene, string path) { Scene = scene; Path = path; }
            public bool Equals(ScenePath other) => Scene == other.Scene && Path == other.Path;
            public override bool Equals(object other) => other is ScenePath path && Equals(path);
            public override int GetHashCode() => ((Scene?.GetHashCode() ?? 0) * 397) ^ (Path?.GetHashCode() ?? 0);
            public override string ToString() => Scene + "\n" + Path;
        }
        // One node the tree places: an object with its parts, spans or crossing members beneath it, or a crossing
        // without a controller (Row null, Members set).
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
            row.GroupPath != null || CommandBand.IsAddedFamily(row.Family) || row.Identity.Path.StartsWith(InventoryPresentationState.CoverageRoot, StringComparison.Ordinal) ? InventoryPresentationState.Name(row) : Last(row.Identity.Path);
        static string Parent(string path) { var at = path.LastIndexOf('/'); return at < 0 ? null : path.Substring(0, at); }
        static string Round(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

        // A container of signs: a Sign group that lists no part of its own and holds objects another family lists, as
        // Bryson Signs, Ela Signs and Sylva Signs hold track markers. It gets no row; the signs it holds are rows of
        // their own. Why such a group has no edit does not decide this: a Sign group without such objects keeps its row.
        static IReadOnlyList<SceneObservation> containersFrom;
        static HashSet<SceneObservation> containers = new HashSet<SceneObservation>();
        internal static ISet<SceneObservation> SignContainers(IReadOnlyList<SceneObservation> entries) {
            if (entries == null) return new HashSet<SceneObservation>();
            if (ReferenceEquals(containersFrom, entries)) return containers;
            var found = new HashSet<SceneObservation>();
            foreach (var group in entries.Where(e => e.Family == SceneFamilies.BakedScenery && e.GroupPath == null && e.Category == BakedSceneryCategories.Sign)) {
                var below = group.Identity.Path + "/";
                bool Inside(SceneObservation e) => e.Identity.Scene == group.Identity.Scene && e.Identity.Path.StartsWith(below, StringComparison.Ordinal);
                if (!entries.Any(e => e.GroupPath == group.Identity.Path && e.Identity.Scene == group.Identity.Scene) && entries.Any(e => e.Family != SceneFamilies.BakedScenery && Inside(e))) found.Add(group);
            }
            containersFrom = entries; containers = found;
            return found;
        }
        // A serialized track span that no crossing controller holds is a native track span and leaves the list.
        internal static bool IsNativeSpan(SceneObservation r) => r.Family == SpanFamily && r.HolderPath == null && !r.Identity.Path.StartsWith(InventoryPresentationState.CoverageRoot, StringComparison.Ordinal);
        internal static int HiddenCount(IReadOnlyList<SceneObservation> entries) => entries == null ? 0 : SignContainers(entries).Count + entries.Count(IsNativeSpan);
        // The outliner's count line: the rows the tree lists, of the objects it can list.
        internal static string Summary(Result result, IReadOnlyList<SceneObservation> entries) =>
            result.Listed.ToString(CultureInfo.InvariantCulture) + " of " + Math.Max(0, (entries?.Count ?? 0) - HiddenCount(entries)).ToString(CultureInfo.InvariantCulture) + " objects";

        // ---- places, positions and classes, read once per world and per observation ----
        static string cachedWorldKey;
        static bool placesBuiltWithEntries;
        static IReadOnlyList<SceneObservation> preparedEntries;
        static ScenePlaces places;
        static string placesLine;
        // Per observation: an edit replaces the observation, so these never hold a verdict older than the object.
        static Dictionary<SceneObservation, ScenePlace> placeCache;
        static Dictionary<SceneObservation, double[]> measured;
        static Dictionary<SceneObservation, SceneClass> classCache;
        static Dictionary<SceneObservation, string> searchTags;

        // Refresh reads the places again.
        internal static void InvalidatePlaces() {
            cachedWorldKey = null; placesBuiltWithEntries = false; preparedEntries = null;
            places = null; placeCache = null; measured = null; classCache = null; searchTags = null; kindsFrom = null; containersFrom = null;
        }
        static bool HasPosition(SceneObservation e) => e.Position != null && e.Position.Count >= 3;
        static bool Finite(double x, double z) => !Double.IsNaN(x) && !Double.IsNaN(z) && !Double.IsInfinity(x) && !Double.IsInfinity(z);
        // A starter placement marker and a track span stand at their resolved track point; their scene object's own
        // position is not where they act (SceneInventory: FocusTarget).
        static bool UsesTrackPoint(string family) => family == StarterPlacements.Family || family == FocusTarget.Span;
        // Where an object stands, as map x/z: its track point when it uses one, else where the world shows it (a
        // group with its parts), else its observed position.
        static double[] Measure(SceneObservation e, Func<SceneObservation, double[]> trackPoint) {
            if (e == null) return null;
            if (measured.TryGetValue(e, out var known)) return known;
            double[] result = null;
            if (trackPoint != null && UsesTrackPoint(e.Family)) {
                try {
                    var point = trackPoint(e);
                    if (point != null && point.Length >= 2 && Finite(point[0], point[1])) result = new[] { point[0], point[1] };
                } catch (Exception ex) when (!(ex is OutOfMemoryException)) { WarnOnce("track point", "a track point could not be resolved, so the object is placed by its own position: " + ex.GetBaseException().Message); }
            }
            if (result == null && Scene != null) {
                try { result = Scene.Bounds(e); }
                catch (Exception ex) when (!(ex is OutOfMemoryException)) { WarnOnce("bounds", "an object's renderers could not be read, so it is placed by its own position: " + ex.GetBaseException().Message); }
            }
            if (result == null && HasPosition(e) && Finite(e.Position[0], e.Position[2])) result = new[] { e.Position[0], e.Position[2] };
            return measured[e] = result;
        }
        static ObjectFacts FactsOf(SceneObservation e) {
            if (Scene == null) return new ObjectFacts();
            try { return Scene.Facts(e) ?? new ObjectFacts(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { WarnOnce("facts", "an object's components could not be read, so it is classified by its name and family: " + ex.GetBaseException().Message); return new ObjectFacts(); }
        }
        // The category and kind of a listed object that is no part, from the generic rules. A group root keeps the
        // category the inventory gave it by the same rules.
        static SceneClass ClassOf(SceneObservation e) {
            if (classCache.TryGetValue(e, out var known)) return known;
            var facts = FactsOf(e);
            var found = SceneClassification.Of(e.Family, e.Identity.Path, facts.Own, facts.AtOrBelow, facts.NamesBelow, places.StripPlaces);
            if (e.Category != null && e.Category != found.Category) found = SceneClassification.Class(e.Category, e.Subcategory);
            // The game tells track markers apart by their marker type.
            if (found.Category == SceneClassification.TrackMarker && found.Kind == null) found = SceneClassification.Class(found.Category, InventoryPresentationState.Subgroup(e));
            return classCache[e] = found;
        }
        static ScenePlace PlaceOf(SceneObservation e, Func<SceneObservation, double[]> trackPoint) {
            if (e == null) return null;
            if (placeCache.TryGetValue(e, out var cached)) return cached;
            return placeCache[e] = places.Of(e.Identity.Path, Measure(e, trackPoint));
        }
        // Places are read once per loaded world, not per inventory snapshot, and at most once per outliner rebuild.
        static void Prepare(IReadOnlyList<SceneObservation> entries, Func<SceneObservation, double[]> trackPoint) {
            entries = entries ?? Array.Empty<SceneObservation>();
            if (places != null && ReferenceEquals(preparedEntries, entries)) return;
            string worldKey = null;
            try { worldKey = Scene?.WorldKey; }
            catch (Exception e) when (!(e is OutOfMemoryException)) { WarnOnce("world", "the loaded world could not be read: " + e.GetBaseException().Message); }
            var hasEntries = entries.Count > 0;
            if (places != null && cachedWorldKey == worldKey && (placesBuiltWithEntries || !hasEntries)) {
                preparedEntries = entries;
                TrimObservationCaches(entries);
                return;
            }
            cachedWorldKey = worldKey; placesBuiltWithEntries = hasEntries; preparedEntries = entries;
            placeCache = new Dictionary<SceneObservation, ScenePlace>(); measured = new Dictionary<SceneObservation, double[]>();
            classCache = new Dictionary<SceneObservation, SceneClass>(); searchTags = new Dictionary<SceneObservation, string>();
            var notes = new List<string>();
            var sources = new List<PlaceSource>();
            // What the world names beside the listed objects, the game's map data first.
            if (Scene != null) {
                try { sources.AddRange(Scene.PlaceSources(entries, notes)); }
                catch (Exception e) when (!(e is OutOfMemoryException)) { notes.Add("the world's places could not be read: " + e.GetBaseException().Message); }
            }
            // What the listed objects name by their path and name: place folders, Roads folders, station map icons, Loc: boards.
            foreach (var e in entries.Where(e => e.GroupPath == null && e.HolderPath == null)) sources.AddRange(ScenePlaces.SourcesOf(e.Identity.Path, Measure(e, trackPoint)));
            places = ScenePlaces.From(sources);
            placesLine = String.Join(" | ", places.All.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p =>
                p.Name + " [" + p.From + "] {" + String.Join("; ", p.Names) + "} " + String.Join(" ", p.Points.Select(at => "@" + Round(at[0]) + "," + Round(at[1])))))
                + (notes.Count == 0 ? "" : " NOTES: " + String.Join("; ", notes));
        }
        // An edit replaces observations; drop the cached verdicts of the ones no longer listed.
        static void TrimObservationCaches(IReadOnlyList<SceneObservation> entries) {
            if (measured.Count <= entries.Count * 2 + 500) return;
            var live = new HashSet<SceneObservation>(entries);
            foreach (var cache in new IDictionary[] { measured, placeCache, classCache, searchTags }) {
                var stale = cache.Keys.Cast<SceneObservation>().Where(k => !live.Contains(k)).ToArray();
                foreach (var key in stale) cache.Remove(key);
            }
        }

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

        // The outliner rows in inventory order. The filter narrows them, and so does the search text, which matches
        // what an object says about itself and also its category, kind, place and crossing. Neither changes the inventory.
        internal static IReadOnlyList<SceneObservation> Rows(SceneInventory inventory, string search, OutlinerFilter filter, Func<SceneObservation, bool> changed, Func<SceneObservation, double[]> trackPoint) {
            if (inventory == null) return Array.Empty<SceneObservation>();
            Prepare(inventory.Entries, trackPoint);
            var hasSearch = !String.IsNullOrEmpty(search);
            var matched = hasSearch ? new HashSet<SceneObservation>(inventory.Search(search)) : null;
            return inventory.Entries.Where(entry => InventoryPresentationState.Passes(entry, filter, changed) && (!hasSearch || matched.Contains(entry) || MatchesGrouping(entry, search, trackPoint))).ToArray();
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
            // The kind counts only where the tree shows it.
            if (node.Row != null) b.Result.Shown[node.Row] = new[] { unit.Category, kindCategories.Contains(unit.Category) ? unit.Kind : null, unit.Place };
            foreach (var child in node.Children) Remember(b, child, unit);
        }
        // The middle of a crossing's members, in map coordinates, where Focus goes.
        static double[] Centre(Unit u) {
            var at = u.Members.Select(m => m.Row).Where(r => r != null && HasPosition(r)).Select(r => r.Position).ToArray();
            return at.Length == 0 ? null : new[] { at.Average(p => p[0]), at.Average(p => p[1]), at.Average(p => p[2]) };
        }

        // Container objects and native track spans leave the list.
        static void HideContainersAndNativeSpans(BuildContext b) {
            var signContainers = SignContainers(b.Entries);
            foreach (var r in b.Rows.Where(r => signContainers.Contains(r) || IsNativeSpan(r))) {
                b.Hidden.Add(r);
                if (b.Diagnostics) b.Result.Hidden.Add((signContainers.Contains(r) ? "container object " : "native track span ") + r.Identity.Path);
            }
            foreach (var e in b.Entries) {
                var at = new ScenePath(e.Identity.Scene, e.Identity.Path);
                if (e.GroupPath == null && e.HolderPath == null && !b.RootEntries.ContainsKey(at)) b.RootEntries[at] = e;
            }
        }
        // Units: every object outside a group and outside a controller, with its parts or detection spans beneath it. A
        // part or span whose holder the search left out brings its holder, dimmed.
        static void BuildUnits(BuildContext b) {
            foreach (var row in b.Rows.Where(r => !b.Hidden.Contains(r) && r.GroupPath == null && r.HolderPath == null))
                b.Make(row, new ScenePath(row.Identity.Scene, row.Identity.Path), false).Node.Count = 1;
            foreach (var row in b.Rows.Where(r => !b.Hidden.Contains(r) && (r.GroupPath != null || r.HolderPath != null))) {
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
        // The place of each unit; a crossing without a controller stands where its crossing object is, at the mean of
        // its members. A crossing is one with signals when a crossing controller holds it, and one without otherwise.
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
        // row's markers mean. It names no family and no rule: only what the outliner itself shows.
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

        // The text of every row of the last tree with its depth, for Fit: a folder with its count, a row with its name.
        internal static IReadOnlyList<KeyValuePair<string, int>> FitLabels() {
            var labels = new List<KeyValuePair<string, int>>();
            void Walk(OutlineNode node, int depth) {
                labels.Add(new KeyValuePair<string, int>((node.Row == null ? "(" + node.Count.ToString(CultureInfo.InvariantCulture) + ") " : "") + (node.Label ?? ""), depth));
                foreach (var child in node.Children) Walk(child, depth + 1);
            }
            foreach (var top in lastTops ?? new OutlineNode[0]) Walk(top, 0);
            return labels;
        }

        static bool detailLogged;
        // Debug lines, one per view switch: view, top-level nodes, objects listed, objects in a catch-all node, and
        // what breaks the rules of the tree. The detail lines follow with an empty search: places, crossings,
        // inspection-only rows, hidden objects.
        internal static void Log(Result result, int rowsShown, string search, string filter) {
            var tops = String.Join(", ", result.Tops.Select(t => t.Label + "=" + t.Count.ToString(CultureInfo.InvariantCulture)));
            PatcherLog.Debug("Outliner: view=" + (ByPlace ? "Place" : "Category") + " tops=" + result.Tops.Count
                + " listed=" + result.Listed + " shown=" + rowsShown + " hidden=" + result.Hidden.Count + " catchAll=" + result.Unplaced + " dropped=" + (rowsShown - result.Hidden.Count - result.Listed)
                + " filter=" + filter + " search='" + search + "' kinds={" + String.Join(", ", kindCategories.OrderBy(k => k, StringComparer.Ordinal)) + "} [" + tops + "]");
            PatcherLog.Debug("Outliner detail: catchAll=" + result.CatchAll.Count + " {" + String.Join(" | ", result.CatchAll) + "} violations=" + result.Violations.Count + " {" + String.Join(" | ", result.Violations) + "}");
            if (detailLogged && !String.IsNullOrEmpty(search)) return;
            detailLogged = true;
            PatcherLog.Debug("Outliner places: " + placesLine);
            PatcherLog.Debug("Outliner crossings: " + result.Crossings.Count + " {" + String.Join(" | ", result.Crossings) + "}");
            PatcherLog.Debug("Outliner inspection only: " + result.InspectionOnlyRows.Count + " {" + String.Join(" | ", result.InspectionOnlyRows) + "}");
            PatcherLog.Debug("Outliner hidden: " + result.Hidden.Count + " {" + String.Join(" | ", result.Hidden) + "}");
        }
    }
}
