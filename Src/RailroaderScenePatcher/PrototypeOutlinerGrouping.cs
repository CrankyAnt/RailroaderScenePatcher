// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: throwaway outliner grouping for the question "how should the outliner group about 1,900 scene objects,
// by kind or by place?". Not production code: no tests, no persistence, state in memory only. Places are in
// PrototypeOutlinerPlaces.cs, crossings in PrototypeOutlinerCrossings.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RailroaderScenePatcher.Core;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

namespace RailroaderScenePatcher {
    internal static partial class PrototypeOutlinerGrouping {
        // One tree across families; the top level by category or by place.
        internal static bool ByPlace;
        // A kind folder needs at least this many objects; fewer stand loose in the folder above.
        internal const int KindThreshold = 2;
        // A place folder needs at least this many objects of one kind in one place; fewer stand loose, with their place.
        internal const int PlaceThreshold = 3;
        // Place folders appear only in a list longer than this; a shorter list reads well with each object's place.
        internal const int PlaceListMinimum = 10;
        internal const string NotSorted = "Not sorted", NoPlace = "No place", InspectionOnly = InventoryPresentationState.InspectionOnlyGroup;
        const string TrackMarkerCategory = "Track marker", MilepostsKind = "Mileposts", LocationSignsKind = "Location signs", LocationMarkersKind = "Location markers";
        internal static bool LogPending = true;
        // Where warnings go; the offline harness replaces it, because Unity's log needs the engine.
        internal static Action<string> Warn = message => UnityEngine.Debug.LogWarning(message);
        static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        // One warning per kind of failure, so a failure on each of 1,900 objects writes one line.
        internal static void WarnOnce(string kind, string message) {
            if (warned.Add(kind)) Warn("Scene Patcher PROTOTYPE outliner: " + message);
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
        // One level of a unit's place in the tree. A category level always makes a folder; a kind level makes one only
        // from KindThreshold objects.
        sealed class Level {
            internal string Name; internal bool Forced;
            internal static Level Category(string name) => new Level { Name = name, Forced = true };
            internal static Level Kind(string name) => new Level { Name = name };
        }
        // One node the tree places: an object with its parts, spans or crossing members beneath it, or an unprotected
        // crossing (Row null, Members set).
        sealed class Unit {
            internal OutlineNode Node;
            internal SceneObservation Row;
            internal List<Unit> Members = new List<Unit>();
            internal List<Level> Levels = new List<Level>();
            internal string Place, Inside, CrossingPath;
            internal bool Conflict, Nearest;
        }
        internal sealed class Result {
            internal IReadOnlyList<OutlineNode> Tops;
            internal int Listed, Unplaced;
            internal List<string> Hidden = new List<string>();
            internal List<string> CatchAll = new List<string>();
            internal List<string> Conflicts = new List<string>();
            internal List<string> InspectionOnlyRows = new List<string>();
            internal List<string> PositionalSigns = new List<string>();
            internal List<string> Crossings = new List<string>();
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
                var label = row == null ? Last(at.Path) : row.GroupPath == null && (row.Category != null || row.Family == BakedSceneryRules.Family) ? row.DisplayName : InventoryPresentationState.Name(row);
                var unit = new Unit { Row = row, Node = new OutlineNode { Key = at + (row?.Identity.PadMarkerId == null ? "" : "\n" + row.Identity.PadMarkerId), Label = label, Row = row, Context = context } };
                Units.Add(unit);
                if (!ByPath.ContainsKey(at)) ByPath[at] = unit;
                return unit;
            }
        }
        static IReadOnlyList<OutlineNode> lastTops;

        static string Last(string path) => path.Substring(path.LastIndexOf('/') + 1);
        static string Parent(string path) { var at = path.LastIndexOf('/'); return at < 0 ? null : path.Substring(0, at); }
        static string Round(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

        // ---- kinds ----
        static readonly Regex SpeedSignWord = new Regex(@"\b(speed(\s*sign)?|speedlimit)\b", RegexOptions.IgnoreCase);
        static readonly Regex YardLimitWord = new Regex(@"\byard\s+limit\b", RegexOptions.IgnoreCase);
        static readonly Regex StationSignWord = new Regex(@"\b(station\s+sign|sign[-_\s]*station)\b", RegexOptions.IgnoreCase);
        static readonly Regex WhistleWord = new Regex(@"\bwhistle\b", RegexOptions.IgnoreCase);
        static readonly Regex CtcWord = new Regex(@"\bctc\b", RegexOptions.IgnoreCase);
        static readonly Regex CircuitWord = new Regex(@"\bcircuit\b", RegexOptions.IgnoreCase);
        static readonly Regex SignWord = new Regex(@"\bsigns?\b", RegexOptions.IgnoreCase);
        static readonly Regex Ordinal = new Regex(@"\s*\(\d+\)\z");
        internal static readonly string[] SignKinds = { "Station signs", LocationSignsKind, "Speed signs", "Yard limits", "Whistle signs", "CTC signs", MilepostsKind, "Other signs" };
        static readonly string[] Tops = { BakedSceneryCategories.StationBuilding, BakedSceneryCategories.Platform, BakedSceneryCategories.Lights, BakedSceneryCategories.Sign, BakedSceneryCategories.GradeCrossing,
            BakedSceneryCategories.Bridge, BakedSceneryCategories.IndustryBuilding, BakedSceneryCategories.StorageDisplay, BakedSceneryCategories.RailroadServiceBuilding,
            TrackMarkerCategory, SpawnPoints.Family, StarterPlacements.Family, StationMapIconRules.Family, BakedSceneryCategories.Other };
        // Kinds whose objects placed by position may still form place folders: long sign lists and boards. Bridges and
        // mileposts never do.
        static readonly HashSet<string> PositionalPlaceFolderKinds = new HashSet<string>(StringComparer.Ordinal) {
            "Station signs", LocationSignsKind, "Speed signs", "Yard limits", "Whistle signs", "CTC signs", "Other signs", "Independent board"
        };

        // The kind of sign an object's own name says, matched on word boundaries, or null when its name names no sign.
        internal static string SignKind(string name) {
            if (String.IsNullOrEmpty(name)) return null;
            if (SpeedSignWord.IsMatch(name)) return "Speed signs";
            if (YardLimitWord.IsMatch(name)) return "Yard limits";
            if (StationSignWord.IsMatch(name)) return "Station signs";
            if (WhistleWord.IsMatch(name)) return "Whistle signs";
            if (CtcWord.IsMatch(name) || CircuitWord.IsMatch(name) && SignWord.IsMatch(name)) return "CTC signs";
            if (SignWord.IsMatch(name)) return "Other signs";
            return null;
        }
        static string Stem(string label) => Ordinal.Replace(label ?? "", "");

        // An inspection-only Sign group that a container object of signs holds only signs: it gets no row (Bryson Signs,
        // Ela Signs, Sylva Signs); its signs are rows of their own.
        internal static bool IsContainer(SceneObservation r) => r.GroupPath == null && r.Category == BakedSceneryCategories.Sign && r.InspectionOnly != null;
        // A serialized track span that no crossing controller holds is a native track span and leaves the list.
        internal static bool IsNativeSpan(SceneObservation r) => r.Family == SpanFamily && r.HolderPath == null && !r.Identity.Path.StartsWith(CrossingCoverageWorld.RootName, StringComparison.Ordinal);
        internal static int HiddenCount(IReadOnlyList<SceneObservation> entries) => entries == null ? 0 : entries.Count(r => IsContainer(r) || IsNativeSpan(r));

        // A milepost's line prefix and mileage, read from its Milepost bindings; without them it sorts by name.
        static (string Prefix, double Mileage) MilepostKey(SceneObservation row) {
            string prefix = null; double mileage = Double.PositiveInfinity;
            foreach (var line in row?.Bindings ?? Array.Empty<string>()) {
                if (line.StartsWith("Milepost prefix=", StringComparison.Ordinal)) prefix = line.Substring(16).Trim();
                else if (line.StartsWith("Milepost mileage=", StringComparison.Ordinal) && Double.TryParse(line.Substring(17).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) mileage = parsed;
            }
            return (prefix ?? "", mileage);
        }

        static List<Level> LevelsOf(SceneObservation r, string label, IEnumerable<OutlineNode> children) {
            var levels = new List<Level>();
            if (r == null) { levels.Add(Level.Category(NotSorted)); levels.Add(Level.Category("Group root not in the object list")); return levels; }
            var name = Last(r.Identity.Path);
            if (r.Category != null || r.Family == BakedSceneryRules.Family || children != null && children.Any(c => c.Row?.GroupPath != null)) {
                var category = r.Category ?? BakedSceneryCategories.Other;
                levels.Add(Level.Category(category));
                if (category == BakedSceneryCategories.Bridge) levels.Add(Level.Kind(r.Subcategory ?? "Other bridges"));
                else if (category == BakedSceneryCategories.Sign)
                    levels.Add(Level.Kind(SignKind(name) ?? children?.Select(c => SignKind(Last(c.Row.Identity.Path))).FirstOrDefault(k => k != null) ?? "Other signs"));
                else if (category != BakedSceneryCategories.GradeCrossing) levels.Add(Level.Kind(Stem(label)));
                return levels;
            }
            // The bridge and construction controller of a rail bridge construction, should the inventory give no category.
            if (BakedSceneryCategories.OfFamily(r.Family) is IReadOnlyList<string> byFamily) {
                levels.Add(Level.Category(byFamily[0])); levels.Add(Level.Kind(byFamily[1])); return levels;
            }
            switch (r.Family) {
                case ControllerFamily: case PadFamily: case MastFamily: case BellFamily: case SpanFamily:
                case CommandBand.AddedEquipmentFamily: case CommandBand.AddedBellFamily:
                    levels.Add(Level.Category(BakedSceneryCategories.GradeCrossing)); return levels;
                case MilepostFamily: levels.Add(Level.Category(BakedSceneryCategories.Sign)); levels.Add(Level.Kind(MilepostsKind)); return levels;
                case TrackMarkerFamily:
                    if (IsLocMarker(r)) {
                        var visible = LocVisible(r);
                        levels.Add(Level.Category(visible ? BakedSceneryCategories.Sign : TrackMarkerCategory)); levels.Add(Level.Kind(visible ? LocationSignsKind : LocationMarkersKind));
                    } else if (SignKind(name) is string sign) { levels.Add(Level.Category(BakedSceneryCategories.Sign)); levels.Add(Level.Kind(sign)); }
                    else if (CrossingMarkerName.IsMatch(name)) levels.Add(Level.Category(BakedSceneryCategories.GradeCrossing));
                    else { levels.Add(Level.Category(TrackMarkerCategory)); levels.Add(Level.Kind(InventoryPresentationState.Subgroup(r) ?? "Other markers")); }
                    return levels;
                case "Track-relative scene object": case "Independent board":
                    levels.Add(Level.Category(BakedSceneryCategories.Other)); levels.Add(Level.Kind(r.Family)); return levels;
            }
            if (r.Family == SpawnPoints.Family || r.Family == StarterPlacements.Family || r.Family == StationMapIconRules.Family) {
                levels.Add(Level.Category(r.Family)); levels.Add(Level.Kind(Stem(label))); return levels;
            }
            levels.Add(Level.Category(r.InspectionOnly != null ? InspectionOnly : NotSorted)); levels.Add(Level.Category(String.IsNullOrEmpty(r.Family) ? "No family" : r.Family));
            return levels;
        }
        static int Rank(int depth, string name) {
            if (name == NotSorted || name == NoPlace || name == InspectionOnly) return 10000;
            if (depth == 0) { var i = Array.IndexOf(Tops, name); return i < 0 ? 5000 : i; }
            var s = Array.IndexOf(SignKinds, name);
            return s >= 0 ? 100 + s : 1000;
        }

        // ---- search covering place, kind and crossing ----
        static string GroupingSearchText(SceneObservation e, Func<SceneObservation, double[]> trackPoint) {
            if (searchTags.TryGetValue(e, out var cached)) return cached;
            var label = e.GroupPath == null && (e.Category != null || e.Family == BakedSceneryRules.Family) ? e.DisplayName : InventoryPresentationState.Name(e);
            var place = PlaceOf(e, trackPoint);
            var crossing = CanJoinCrossing(e) ? Last(Parent(e.Identity.Path) ?? "") : null;
            var text = String.Join(" ", LevelsOf(e, label, null).Select(l => l.Name).Concat(new[] { place.Named, place.Near, place.Inside, crossing }).Where(s => !String.IsNullOrEmpty(s)));
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
            var b = new BuildContext {
                Rows = rows ?? Array.Empty<SceneObservation>(), Entries = inventory ?? rows ?? Array.Empty<SceneObservation>(),
                Search = search, Narrowed = narrowed, PlaceView = ByPlace, Diagnostics = LogPending, TrackPoint = trackPoint
            };
            Prepare(b.Entries, trackPoint);
            HideContainersAndNativeSpans(b);
            BuildUnits(b);
            GroupProtectedCrossings(b);
            GroupUnprotectedCrossings(b);
            PlaceUnits(b);
            var tops = b.PlaceView ? PlaceTops(b) : CategoryTops(b);
            b.Result.Tops = lastTops = tops;
            b.Result.Listed = tops.Sum(t => t.Count);
            return b.Result;
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
                holder.Node.Children.Add(new OutlineNode { Key = new ScenePath(row.Identity.Scene, row.Identity.Path).ToString(), Label = row.GroupPath != null ? InventoryPresentationState.PartLabel(row) : InventoryPresentationState.Name(row), Row = row, Count = 1 });
                holder.Node.Count++;
            }
            foreach (var u in b.Units) {
                // A group that holds matching parts but does not match itself is shown dimmed, only to hold them.
                if (!u.Node.Context && u.Row != null && u.Node.Children.Count != 0 && u.Row.GroupPath == null && u.Row.HolderPath == null && u.Row.Family == BakedSceneryRules.Family
                    && !u.Row.MatchesItself(b.Search) && !MatchesGrouping(u.Row, b.Search, b.TrackPoint)) { u.Node.Context = true; u.Node.Count--; }
                u.Node.Children.Sort((x, y) => { var by = StringComparer.OrdinalIgnoreCase.Compare(x.Label, y.Label); return by != 0 ? by : StringComparer.Ordinal.Compare(x.Row.Identity.Path, y.Row.Identity.Path); });
                u.Levels = LevelsOf(u.Row, u.Node.Label, u.Node.Children);
            }
        }
        // The place of each unit; an unprotected crossing stands where its crossing object is, at the mean of its members.
        static void PlaceUnits(BuildContext b) {
            foreach (var u in b.Units) {
                PlaceInfo place;
                if (u.Row != null) place = PlaceOf(u.Row, b.TrackPoint);
                else {
                    var positions = u.Members.Select(m => Measure(m.Row, b.TrackPoint)).Where(p => p != null).ToArray();
                    place = PlaceAt(u.CrossingPath, positions.Length == 0 ? null : new[] { positions.Average(p => p[0]), positions.Average(p => p[1]) });
                }
                u.Place = place.Named ?? place.Near; u.Inside = place.Inside; u.Nearest = place.Positional; u.Conflict = IsConflict(place);
                if (b.Diagnostics) CollectPlaceDiagnostics(b, u, place);
            }
            if (!b.Diagnostics) return;
            foreach (var holder in b.Units.Where(u => u.Row == null || u.Row.Family == ControllerFamily))
                foreach (var member in holder.Node.Children.Where(c => c.Row?.InspectionOnly != null))
                    b.Result.InspectionOnlyRows.Add(member.Label + " (" + member.Row.Identity.Path + ") [" + member.Row.Family + "] under Grade crossing / " + holder.Node.Label + ": " + member.Row.InspectionOnly);
        }
        static void CollectPlaceDiagnostics(BuildContext b, Unit u, PlaceInfo place) {
            var path = u.Row?.Identity.Path ?? u.CrossingPath;
            if (u.Conflict) b.Result.Conflicts.Add(u.Node.Label + " (" + path + "): named " + place.Named + " at " + Round(place.NamedDistance) + " m (radius " + Round(places[place.Named].Radius) + "), inside " + place.Inside + " at " + Round(place.InsideDistance) + " m (radius " + Round(places[place.Inside].Radius) + ")");
            if (u.Row?.InspectionOnly != null) b.Result.InspectionOnlyRows.Add(u.Node.Label + " (" + path + ") [" + u.Row.Family + "] under " + String.Join(" / ", u.Levels.Select(l => l.Name)) + ": " + u.Row.InspectionOnly);
            if (u.Row == null || !u.Nearest || u.Levels.Count == 0 || u.Levels[0].Name != BakedSceneryCategories.Sign) return;
            var at = Measure(u.Row, b.TrackPoint);
            if (at != null) b.Result.PositionalSigns.Add(u.Node.Label + " (" + path + ") @" + Round(at[0]) + "," + Round(at[1]) + " -> " + u.Place + "; nearby: "
                + String.Join(", ", primaryPlaced.OrderBy(p => DistanceSquared(at, p.Centre)).Take(4).Select(p => p.Name + " " + Round(Distance(at, p.Centre)) + " m (r" + Round(p.Radius) + ")")));
        }

        static string Note(Unit u) => u.Conflict ? "(stands in " + u.Inside + ")" : u.Nearest ? "(nearest place)" : null;
        static string WithPlace(Unit u) => u.Place == null ? Note(u) : u.Place + (Note(u) == null ? "" : " " + Note(u));
        static IEnumerable<Unit> OrderLoose(IEnumerable<Unit> units, bool mileposts) => mileposts
            ? units.OrderBy(m => MilepostKey(m.Row).Prefix, StringComparer.OrdinalIgnoreCase).ThenBy(m => MilepostKey(m.Row).Mileage).ThenBy(m => m.Node.Label, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Node.Key, StringComparer.Ordinal)
            : units.OrderBy(m => m.Node.Label, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Place ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Node.Key, StringComparer.Ordinal);

        // One folder's contents: its kind folders, then its place folders, then its loose units with their places.
        static void Fill(BuildContext b, OutlineNode folder, List<Unit> members, int depth, bool placeFolders) {
            var loose = new List<Unit>();
            var levels = members.GroupBy(m => depth < m.Levels.Count ? m.Levels[depth].Name : null).ToList();
            var subfolders = new List<OutlineNode>();
            foreach (var group in levels.Where(g => g.Key != null).OrderBy(g => Rank(depth, g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                var list = group.ToList();
                if (!list.Any(m => m.Levels[depth].Forced) && list.Count < KindThreshold) { loose.AddRange(list); continue; }
                var sub = new OutlineNode { Key = folder.Key + "/" + group.Key, Label = group.Key };
                Fill(b, sub, list, depth + 1, placeFolders);
                subfolders.Add(sub);
            }
            loose.AddRange(levels.Where(g => g.Key == null).SelectMany(g => g));
            // A kind folder that would be the only thing in its folder only repeats it: its contents move up.
            if (subfolders.Count == 1 && loose.Count == 0 && !members.Any(m => depth < m.Levels.Count && m.Levels[depth].Forced)) {
                folder.Children.AddRange(subfolders[0].Children); folder.Count = subfolders[0].Count; return;
            }
            folder.Children.AddRange(subfolders);
            var mileposts = folder.Label == MilepostsKind || loose.Count > 0 && loose.All(m => m.Row?.Family == MilepostFamily);
            var positionalFolders = PositionalPlaceFolderKinds.Contains(folder.Label);
            var placeGroups = placeFolders && !mileposts && loose.Count > PlaceListMinimum
                ? loose.Where(m => m.Place != null && (!m.Nearest || positionalFolders)).GroupBy(m => m.Place, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= PlaceThreshold).ToList()
                : new List<IGrouping<string, Unit>>();
            // One place folder that would hold every loose object only repeats a place: the objects stand loose with it.
            if (placeGroups.Count == 1 && placeGroups[0].Count() == loose.Count && subfolders.Count == 0) placeGroups.Clear();
            var inFolders = new HashSet<Unit>(placeGroups.SelectMany(g => g));
            foreach (var g in placeGroups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                var placeFolder = new OutlineNode { Key = folder.Key + "/@" + g.Key, Label = g.Key };
                foreach (var m in OrderLoose(g, mileposts)) { m.Node.Where = Note(m); placeFolder.Children.Add(m.Node); }
                placeFolder.Count = placeFolder.Children.Sum(c => c.Count);
                folder.Children.Add(placeFolder);
            }
            foreach (var m in OrderLoose(loose.Where(m => !inFolders.Contains(m)), mileposts)) {
                m.Node.Where = b.PlaceView ? Note(m) : WithPlace(m);
                folder.Children.Add(m.Node);
            }
            folder.Count = folder.Children.Sum(c => c.Count);
        }
        static string Describe(Unit u) => u.Node.Label + " [" + (u.Row?.Family ?? "no row") + "] " + (u.Row?.Identity.Path ?? u.Node.Key.Replace("\n", " "));
        static List<OutlineNode> CategoryTops(BuildContext b) {
            var tops = new List<OutlineNode>();
            foreach (var group in b.Units.GroupBy(u => u.Levels[0].Name).OrderBy(g => Rank(0, g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                var top = new OutlineNode { Key = group.Key, Label = group.Key };
                Fill(b, top, group.ToList(), 1, true);
                tops.Add(top);
                if (group.Key != NotSorted) continue;
                b.Result.Unplaced += top.Count;
                if (b.Diagnostics) b.Result.CatchAll.AddRange(group.Select(Describe));
            }
            return tops;
        }
        // Every place the game defines appears when unfiltered; under a search or filter only places with matches.
        static List<OutlineNode> PlaceTops(BuildContext b) {
            var tops = new List<OutlineNode>();
            var byPlace = b.Units.GroupBy(u => u.Place ?? NoPlace, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var names = places.Keys.Where(n => !b.Narrowed || byPlace.ContainsKey(n)).Concat(byPlace.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names.OrderBy(n => n == NoPlace ? 1 : 0).ThenBy(n => n, StringComparer.OrdinalIgnoreCase)) {
                var top = new OutlineNode { Key = "@" + (places.TryGetValue(name, out var known) ? known.Name : name), Label = known?.Name ?? name };
                byPlace.TryGetValue(name, out var members);
                if (members != null) Fill(b, top, members, 0, false);
                tops.Add(top);
                if (name != NoPlace || members == null) continue;
                b.Result.Unplaced += top.Count;
                if (b.Diagnostics) b.Result.CatchAll.AddRange(members.Select(Describe));
            }
            return tops;
        }

        // Widths of the visible prototype rows including their nesting indent and place text, for Fit.
        const float IndentPerLevel = 16f;
        internal static float[] FitWidths(TMPro.TMP_Text measure) {
            if (measure == null || lastTops == null || lastTops.Count == 0) return null;
            var widths = new List<float>();
            void Walk(OutlineNode node, int depth) {
                var text = (node.Row == null ? "(" + node.Count.ToString(CultureInfo.InvariantCulture) + ") " : "") + (node.Label ?? "") + (String.IsNullOrEmpty(node.Where) ? "" : "  " + node.Where);
                // The row decoration already covers one folder level; each deeper level adds its indent.
                widths.Add(measure.GetPreferredValues(text).x + Math.Max(0, depth - 1) * IndentPerLevel);
                foreach (var child in node.Children) Walk(child, depth + 1);
            }
            foreach (var top in lastTops) Walk(top, 0);
            return widths.ToArray();
        }

        static bool detailLogged;
        // One line per view switch: view, top-level nodes, objects listed, objects not placed in any node. The detail
        // lines follow with an empty search: places, Loc objects, signs placed by position, crossings, Inspection only,
        // hidden objects.
        internal static void Log(Result result, int rowsShown, string search, string filter) {
            var tops = String.Join(", ", result.Tops.Select(t => t.Label + "=" + t.Count.ToString(CultureInfo.InvariantCulture)));
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE outliner: view=" + (ByPlace ? "Place" : "Category") + " tops=" + result.Tops.Count
                + " listed=" + result.Listed + " shown=" + rowsShown + " hidden=" + result.Hidden.Count + " notPlaced=" + result.Unplaced + " dropped=" + (rowsShown - result.Hidden.Count - result.Listed)
                + " filter=" + filter + " search='" + search + "' [" + tops + "]");
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE outliner detail: catchAll=" + result.CatchAll.Count + " {" + String.Join(" | ", result.CatchAll) + "} conflicts=" + result.Conflicts.Count + " {" + String.Join(" | ", result.Conflicts) + "}");
            if (detailLogged && !String.IsNullOrEmpty(search)) return;
            detailLogged = true;
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE places: " + placesLine);
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE loc objects: " + locLine);
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE signs by position: " + result.PositionalSigns.Count + " {" + String.Join(" | ", result.PositionalSigns) + "}");
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE crossings: " + result.Crossings.Count + " {" + String.Join(" | ", result.Crossings) + "}");
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE inspection only: " + result.InspectionOnlyRows.Count + " {" + String.Join(" | ", result.InspectionOnlyRows) + "}");
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE hidden: " + result.Hidden.Count + " {" + String.Join(" | ", result.Hidden) + "}");
        }
    }
}
