// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: throwaway outliner grouping for the question "how should the outliner group about 1,900 scene objects,
// by kind or by place?". Not production code: no tests, no persistence, state in memory only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RailroaderScenePatcher.Core;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

namespace RailroaderScenePatcher {
    internal static class PrototypeOutlinerGrouping {
        // One tree across families; the top level by category or by place.
        internal static bool ByPlace;
        // A kind folder needs at least this many objects; fewer stand loose in the folder above.
        internal const int KindThreshold = 2;
        // A place folder needs at least this many objects of one kind in one place; fewer stand loose, with their place.
        internal const int PlaceThreshold = 3;
        // Place folders appear only in a list longer than this; a shorter list reads well with each object's place.
        internal const int PlaceListMinimum = 10;
        // A place's radius: the 90th percentile of its members' distances from its centre, at least MinimumRadius
        // metres, plus RadiusMargin. A place without scenery members gets MinimumRadius plus RadiusMargin.
        internal const double MinimumRadius = 250, RadiusMargin = 150;
        internal const string NotSorted = "Not sorted", NoPlace = "No place", InspectionOnly = InventoryPresentationState.InspectionOnlyGroup;
        const string TrackMarkerCategory = "Track marker";
        internal static bool LogPending = true;

        sealed class Level { internal string Name; internal bool Forced; }
        sealed class Unit {
            internal OutlineNode Node;
            internal SceneObservation Row;
            // The rows a crossing node holds; empty for any other unit.
            internal List<Unit> Members = new List<Unit>();
            internal List<Level> Levels = new List<Level>();
            internal string Place, Inside, CrossingRoot;
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

        // ---- places ----
        sealed class Place {
            internal string Name;
            internal readonly SortedSet<string> Sources = new SortedSet<string>(StringComparer.Ordinal);
            internal readonly SortedSet<string> Spellings = new SortedSet<string>(StringComparer.Ordinal);
            internal double[] Centre; internal double Radius; internal string CentreFrom;
            internal readonly List<double[]> Members = new List<double[]>();
            internal readonly Dictionary<string, double[]> Fallback = new Dictionary<string, double[]>();
        }
        sealed class PlaceInfo { internal string Named, Near, Inside; internal double NamedDistance, NearDistance, InsideDistance; internal bool Positional; }
        sealed class LocMarker { internal string Name, Path; internal int Renderers, Active; internal double[] At; internal bool Listed; }
        static IReadOnlyList<SceneObservation> placesFor;
        static Dictionary<string, Place> places;
        static Dictionary<string, string> folderPlace;
        static Dictionary<SceneObservation, PlaceInfo> placeCache;
        static Dictionary<SceneObservation, double[]> measured;
        static List<LocMarker> locMarkers;
        static Dictionary<string, LocMarker> locByPath;
        static string placesLine, locLine;
        const string Scenery = "World/Large Scenery/";
        // Folders below Large Scenery named after a structure that stands in no single place.
        static readonly HashSet<string> NoPlaceFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Plate Girder Bridge", "Global Signs" };
        static readonly Regex StructureSuffix = new Regex(@"\s+(Signs|Bridge|Turntable)\z", RegexOptions.IgnoreCase);
        static readonly Regex CrossingWord = new Regex(@"crossing", RegexOptions.IgnoreCase);

        static string Last(string path) => path.Substring(path.LastIndexOf('/') + 1);
        static string Parent(string path) { var at = path.LastIndexOf('/'); return at < 0 ? null : path.Substring(0, at); }
        static string SceneryFolder(string path) {
            if (path == null || !path.StartsWith(Scenery, StringComparison.Ordinal)) return null;
            var rest = path.Substring(Scenery.Length); var slash = rest.IndexOf('/');
            return slash < 0 ? rest : rest.Substring(0, slash);
        }
        // A sign folder is a container object: Alarka Signs, Bryson Signs, Ela/Signs, World/Signs.
        static bool InSignsFolder(string path) => path.Split('/').Any(segment => segment.EndsWith("Signs", StringComparison.Ordinal));
        static bool HasPosition(SceneObservation e) => e.Position != null && e.Position.Count >= 3;
        static double Distance(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));
        static string Round(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

        // Where an object stands, as map x/z: the centre of the bounds of the renderers in its hierarchy (a group with
        // its parts), not its pivot. A renderer that is off or inactive counts by its mesh's bounds, unless the mesh is a
        // static batch, which spans more than the object; then by its transform. Without renderers: the pivot.
        static double[] Measure(SceneObservation e) {
            if (e == null) return null;
            if (measured.TryGetValue(e, out var known)) return known;
            double[] result = null;
            try { result = MeasureBounds(e.Handle as UnityEngine.GameObject); } catch { result = null; }
            if (result == null && HasPosition(e)) result = new[] { e.Position[0], e.Position[2] };
            return measured[e] = result;
        }
        static double[] MeasureBounds(UnityEngine.GameObject go) {
            if (go == null) return null;
            var bounds = new UnityEngine.Bounds(); var any = false;
            foreach (var renderer in go.GetComponentsInChildren<UnityEngine.Renderer>(true)) {
                if (renderer == null) continue;
                UnityEngine.Bounds own;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.bounds.size != UnityEngine.Vector3.zero) own = renderer.bounds;
                else {
                    var filter = renderer.GetComponent<UnityEngine.MeshFilter>();
                    var mesh = filter == null ? null : filter.sharedMesh;
                    if (mesh == null || mesh.name.StartsWith("Combined Mesh", StringComparison.Ordinal)) own = new UnityEngine.Bounds(renderer.transform.position, UnityEngine.Vector3.zero);
                    else {
                        var local = mesh.bounds; var ext = local.extents;
                        own = new UnityEngine.Bounds(renderer.transform.TransformPoint(local.center), UnityEngine.Vector3.zero);
                        for (var i = 0; i < 8; i++)
                            own.Encapsulate(renderer.transform.TransformPoint(local.center + new UnityEngine.Vector3((i & 1) == 0 ? -ext.x : ext.x, (i & 2) == 0 ? -ext.y : ext.y, (i & 4) == 0 ? -ext.z : ext.z)));
                    }
                }
                if (any) bounds.Encapsulate(own); else { bounds = own; any = true; }
            }
            if (!any) return Map(go.transform.position, go.transform);
            return Map(bounds.center, go.transform);
        }
        static double[] Map(UnityEngine.Vector3 scene, UnityEngine.Transform transform) {
            var map = NativeInventory.MapPosition(scene, NativeInventory.MovesWithOrigin(transform));
            return Single.IsNaN(map.x) || Single.IsNaN(map.z) ? null : new double[] { map.x, map.z };
        }

        static Place PlaceNamed(string name, string source, string spelling = null) {
            name = name?.Trim();
            if (String.IsNullOrEmpty(name)) return null;
            if (!places.TryGetValue(name, out var place)) places[name] = place = new Place { Name = name };
            place.Sources.Add(source); place.Spellings.Add(source + ": " + (spelling ?? name));
            return place;
        }

        // Every Loc: object in the loaded World hierarchy, listed or not, with its renderers.
        static void ScanLocMarkers(IReadOnlyList<SceneObservation> entries) {
            locMarkers = new List<LocMarker>(); locByPath = new Dictionary<string, LocMarker>(StringComparer.Ordinal);
            var listed = new HashSet<string>(entries.Select(e => e.Identity.Path), StringComparer.Ordinal);
            for (var s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++) {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true)) {
                        if (!t.name.StartsWith("Loc: ", StringComparison.Ordinal)) continue;
                        var path = BoardScene.PathOf(t);
                        var renderers = t.GetComponentsInChildren<UnityEngine.Renderer>(true);
                        var marker = new LocMarker {
                            Name = t.name.Substring(5).Trim(), Path = path, Renderers = renderers.Length,
                            Active = renderers.Count(r => r != null && r.enabled && r.gameObject.activeInHierarchy), At = MeasureBounds(t.gameObject), Listed = listed.Contains(path)
                        };
                        locMarkers.Add(marker); locByPath[path] = marker;
                    }
            }
        }
        // A Loc: object is a visible board when its hierarchy holds a renderer; otherwise it is an invisible marker.
        static bool LocVisible(SceneObservation row) {
            if (locByPath != null && locByPath.TryGetValue(row.Identity.Path, out var marker)) return marker.Renderers != 0;
            try { return row.Handle is UnityEngine.GameObject go && go != null && go.GetComponentsInChildren<UnityEngine.Renderer>(true).Length != 0; } catch { return false; }
        }

        static void Prepare(IReadOnlyList<SceneObservation> entries) {
            if (ReferenceEquals(placesFor, entries) && placeCache != null) return;
            placesFor = entries; placeCache = new Dictionary<SceneObservation, PlaceInfo>(); measured = new Dictionary<SceneObservation, double[]>();
            places = new Dictionary<string, Place>(StringComparer.OrdinalIgnoreCase);
            var notes = new List<string>();
            // 1. Scene folders below Large Scenery. A folder whose name is a place followed by a structure word belongs
            // to that place (Almond Bridge, Dillsboro Turntable); a sign folder is a container object and no place.
            var folders = new HashSet<string>(entries.Select(e => SceneryFolder(e.Identity.Path)).Where(f => f != null), StringComparer.OrdinalIgnoreCase);
            folderPlace = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders) {
                if (NoPlaceFolders.Contains(folder) || folder.EndsWith("Signs", StringComparison.Ordinal)) continue;
                var match = StructureSuffix.Match(folder);
                var name = match.Success ? folder.Substring(0, match.Index) : folder;
                var place = PlaceNamed(name, "scene folder", folder);
                folderPlace[folder] = place.Name;
            }
            // 2. Loc: objects in the scene.
            try { ScanLocMarkers(entries); } catch (Exception e) { notes.Add("Loc scan failed: " + e.GetBaseException().Message); locMarkers = new List<LocMarker>(); locByPath = new Dictionary<string, LocMarker>(); }
            foreach (var marker in locMarkers) { var place = PlaceNamed(marker.Name, "Loc marker"); if (marker.At != null) place.Fallback["Loc marker"] = marker.At; }
            // 3. Station map icons: the text the icon shows, else its object's name less " Station" or " Station Icon".
            foreach (var icon in entries.Where(e => e.Family == StationMapIconRules.Family)) {
                string text = null;
                try { if (icon.Handle is UnityEngine.GameObject go && go != null) text = go.GetComponentInChildren<TMPro.TMP_Text>(true)?.text; } catch { }
                var name = String.IsNullOrWhiteSpace(text) ? Regex.Replace(Last(icon.Identity.Path), @"\s+Station(\s+Icon)?\z", "") : text;
                var place = PlaceNamed(name, "station map icon", (text == null ? "" : "text '" + text + "' of ") + Last(icon.Identity.Path));
                var at = Measure(icon); if (place != null && at != null) place.Fallback["station map icon"] = at;
            }
            // 4. The map data: operations areas and passenger stops.
            try {
                var ops = Native.Read(Native.Type("Model.Ops.OpsController"), "Shared");
                if (ops == null) notes.Add("OpsController.Shared is null");
                else {
                    foreach (var area in (IEnumerable)Native.Read(ops, "Areas") ?? new object[0]) {
                        var place = PlaceNamed(Native.Read(area, "name") as string, "ops area", (Native.Read(area, "name") as string) + " (" + Native.Read(area, "identifier") + ")");
                        if (place != null && Native.Read(area, "Position") is UnityEngine.Vector3 p) place.Fallback["ops area"] = new double[] { p.x, p.z };
                    }
                    foreach (var stop in (IEnumerable)Native.Read(ops, "AllPassengerStops") ?? new object[0]) {
                        var place = PlaceNamed(Native.Read(stop, "name") as string, "passenger stop", (Native.Read(stop, "name") as string) + " (" + Native.Read(stop, "identifier") + ")");
                        try { if (place != null && Native.Read(stop, "CenterPoint") is UnityEngine.Vector3 p) place.Fallback["passenger stop"] = new double[] { p.x, p.z }; } catch { }
                    }
                }
            } catch (Exception e) { notes.Add("Map data read failed: " + e.GetBaseException().Message); }
            // Centres: the median of the place's scenery members, measured at their bounds (objects outside any group
            // or controller in its folders, sign folders left out); else a passenger stop, an ops area, a station map
            // icon or a Loc marker, in that order.
            foreach (var e in entries) {
                if (e.GroupPath != null || e.HolderPath != null) continue;
                var folder = SceneryFolder(e.Identity.Path);
                if (folder == null || InSignsFolder(e.Identity.Path) || !folderPlace.TryGetValue(folder, out var name)) continue;
                var at = Measure(e); if (at != null) places[name].Members.Add(at);
            }
            double Median(IEnumerable<double> values) { var s = values.OrderBy(v => v).ToArray(); return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2; }
            foreach (var place in places.Values) {
                if (place.Members.Count != 0) {
                    place.Centre = new[] { Median(place.Members.Select(v => v[0])), Median(place.Members.Select(v => v[1])) };
                    var d = place.Members.Select(v => Distance(v, place.Centre)).OrderBy(v => v).ToArray();
                    place.Radius = Math.Max(MinimumRadius, d[Math.Max(0, (int)Math.Ceiling(0.9 * d.Length) - 1)]) + RadiusMargin;
                    place.CentreFrom = "scenery members (" + place.Members.Count + ")";
                } else foreach (var source in new[] { "passenger stop", "ops area", "station map icon", "Loc marker" })
                    if (place.Fallback.TryGetValue(source, out var at)) { place.Centre = at; place.Radius = MinimumRadius + RadiusMargin; place.CentreFrom = source; break; }
            }
            placesLine = String.Join(" | ", places.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => p.Name + " [" + String.Join("+", p.Sources) + "] {" + String.Join("; ", p.Spellings) + "} "
                + (p.Centre == null ? "no position" : "@" + Round(p.Centre[0]) + "," + Round(p.Centre[1]) + " r" + Round(p.Radius) + " from " + p.CentreFrom)
                + String.Concat(p.Fallback.Select(f => " " + f.Key + "@" + Round(f.Value[0]) + "," + Round(f.Value[1]))))) + (notes.Count == 0 ? "" : " NOTES: " + String.Join("; ", notes));
            locLine = String.Join(" | ", locMarkers.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(m => m.Name + " (" + m.Path + ") renderers=" + m.Renderers + " active=" + m.Active + " " + (m.Renderers != 0 ? "visible board" : "invisible marker")
                + " listed=" + (m.Listed ? "yes" : "no") + (m.At == null ? "" : " @" + Round(m.At[0]) + "," + Round(m.At[1]))));
        }

        static IEnumerable<Place> Placed => places.Values.Where(p => p.Centre != null);

        // The place of an object. An object in a real place folder keeps that folder's place, and is marked when it
        // stands outside that place's radius and inside another place's radius, nearer that place. Any other object,
        // such as one in a container object (a sign folder, World/Signs, Mile Posts), gets the nearest place by its
        // own position.
        static PlaceInfo PlaceOf(SceneObservation e, string path = null) {
            if (e == null) return new PlaceInfo();
            if (path == null && placeCache.TryGetValue(e, out var cached)) return cached;
            path = path ?? e.Identity.Path;
            var info = new PlaceInfo();
            var folder = SceneryFolder(path);
            if (folder != null && !InSignsFolder(path) && folderPlace.TryGetValue(folder, out var byFolder)) info.Named = byFolder;
            var at = Measure(e);
            if (at != null && Placed.Any()) {
                var nearest = Placed.OrderBy(p => Distance(at, p.Centre)).First();
                info.Near = nearest.Name; info.NearDistance = Distance(at, nearest.Centre);
                if (info.Named != null && places[info.Named].Centre != null) {
                    info.NamedDistance = Distance(at, places[info.Named].Centre);
                    var inside = Placed.Where(p => !p.Name.Equals(info.Named, StringComparison.OrdinalIgnoreCase) && Distance(at, p.Centre) <= p.Radius).OrderBy(p => Distance(at, p.Centre)).FirstOrDefault();
                    if (inside != null) { info.Inside = inside.Name; info.InsideDistance = Distance(at, inside.Centre); }
                }
            }
            info.Positional = info.Named == null && info.Near != null;
            return path == e.Identity.Path ? placeCache[e] = info : info;
        }
        static bool IsConflict(PlaceInfo info) => info.Named != null && info.Inside != null && places[info.Named].Centre != null
            && info.NamedDistance > places[info.Named].Radius && info.InsideDistance < info.NamedDistance;

        // ---- kinds ----
        internal static readonly string[] SignKinds = { "Station signs", "Location signs", "Speed signs", "Yard limits", "Whistle signs", "CTC signs", "Mileposts", "Other signs" };
        static readonly string[] Tops = { BakedSceneryCategories.StationBuilding, BakedSceneryCategories.Platform, BakedSceneryCategories.Lights, BakedSceneryCategories.Sign, BakedSceneryCategories.GradeCrossing,
            BakedSceneryCategories.Bridge, BakedSceneryCategories.IndustryBuilding, BakedSceneryCategories.StorageDisplay, BakedSceneryCategories.RailroadServiceBuilding,
            TrackMarkerCategory, SpawnPoints.Family, StarterPlacements.Family, StationMapIconRules.Family, BakedSceneryCategories.Other };
        static readonly HashSet<string> CrossingFamilies = new HashSet<string>(StringComparer.Ordinal) { "Crossing controller", "Crossing pad marker", "Crossing mast", "Crossing bell" };

        // The kind of sign an object's own name says, or null when its name names no sign.
        internal static string SignKind(string name) {
            var n = name.ToLowerInvariant();
            if (n.StartsWith("speed", StringComparison.Ordinal) || n.Contains("speed sign")) return "Speed signs";
            if (n.Contains("yard limit")) return "Yard limits";
            if (n.Contains("station sign") || n.Contains("sign-station")) return "Station signs";
            if (n.Contains("whistle")) return "Whistle signs";
            if (n.Contains("ctc") || n.Contains("circuit")) return "CTC signs";
            if (n.Contains("sign")) return "Other signs";
            return null;
        }
        static readonly Regex Ordinal = new Regex(@"\s*\(\d+\)\z");
        static string Stem(string label) => Ordinal.Replace(label ?? "", "");
        static Level L(string name, bool forced = false) => new Level { Name = name, Forced = forced };

        // A sign folder object that the inventory lists as an inspection-only Sign group holds only signs: it is a
        // container object and gets no row (Bryson Signs, Ela Signs, Sylva Signs).
        static bool IsContainer(SceneObservation r) => r.GroupPath == null && r.Category == BakedSceneryCategories.Sign && r.InspectionOnly != null;
        // A serialized track span that no crossing controller holds is a native track span and leaves the list.
        static bool IsNativeSpan(SceneObservation r) => r.Family == "Track span" && r.HolderPath == null && !r.Identity.Path.StartsWith(CrossingCoverageWorld.RootName, StringComparison.Ordinal);

        static void Classify(Unit u) {
            var r = u.Row;
            if (r == null) { u.Levels.Add(L(NotSorted, true)); u.Levels.Add(L("Group root not in the object list", true)); return; }
            var name = Last(r.Identity.Path);
            if (name.StartsWith("Loc: ", StringComparison.Ordinal)) {
                if (LocVisible(r)) { u.Levels.Add(L(BakedSceneryCategories.Sign, true)); u.Levels.Add(L("Location signs")); }
                else { u.Levels.Add(L(TrackMarkerCategory, true)); u.Levels.Add(L("Location markers")); }
                return;
            }
            var baked = r.Category != null || r.Family == BakedSceneryRules.Family || u.Node.Children.Any(c => c.Row?.GroupPath != null);
            if (baked) {
                var category = r.Category ?? BakedSceneryCategories.Other;
                u.Levels.Add(L(category, true));
                if (category == BakedSceneryCategories.Bridge) u.Levels.Add(L(r.Subcategory ?? "Other bridges"));
                else if (category == BakedSceneryCategories.Sign) u.Levels.Add(L(SignKind(name) ?? u.Node.Children.Select(c => SignKind(Last(c.Row.Identity.Path))).FirstOrDefault(k => k != null) ?? "Other signs"));
                else if (category != BakedSceneryCategories.GradeCrossing) u.Levels.Add(L(Stem(u.Node.Label)));
                return;
            }
            switch (r.Family) {
                case "Bridge": case "Bridge construction scenery":
                    u.Levels.Add(L(BakedSceneryCategories.Bridge, true)); u.Levels.Add(L(BakedSceneryCategories.RailBridges)); return;
                case "Crossing controller": case "Crossing pad marker": case "Crossing mast": case "Crossing bell":
                case CommandBand.AddedEquipmentFamily: case CommandBand.AddedBellFamily: case "Track span":
                    u.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true)); return;
                case "Milepost": u.Levels.Add(L(BakedSceneryCategories.Sign, true)); u.Levels.Add(L("Mileposts")); return;
                case "Track marker": {
                    var sign = SignKind(name);
                    if (sign != null) { u.Levels.Add(L(BakedSceneryCategories.Sign, true)); u.Levels.Add(L(sign)); }
                    // A marker named after a crossing (Crossing Pad (1), Grade Crossing - Allen) belongs to its crossing.
                    else if (CrossingWord.IsMatch(name)) u.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true));
                    else { u.Levels.Add(L(TrackMarkerCategory, true)); u.Levels.Add(L(InventoryPresentationState.Subgroup(r) ?? "Other markers")); }
                    return;
                }
                case "Track-relative scene object": case "Independent board":
                    u.Levels.Add(L(BakedSceneryCategories.Other, true)); u.Levels.Add(L(r.Family)); return;
            }
            if (r.Family == SpawnPoints.Family || r.Family == StarterPlacements.Family || r.Family == StationMapIconRules.Family) { u.Levels.Add(L(r.Family, true)); u.Levels.Add(L(Stem(u.Node.Label))); return; }
            if (r.InspectionOnly != null) { u.Levels.Add(L(InspectionOnly, true)); u.Levels.Add(L(String.IsNullOrEmpty(r.Family) ? "No family" : r.Family, true)); return; }
            u.Levels.Add(L(NotSorted, true)); u.Levels.Add(L(String.IsNullOrEmpty(r.Family) ? "No family" : r.Family, true));
        }

        static int Rank(int depth, string name) {
            if (name == NotSorted || name == NoPlace || name == InspectionOnly) return 10000;
            if (depth == 0) { var i = Array.IndexOf(Tops, name); return i < 0 ? 5000 : i; }
            var s = Array.IndexOf(SignKinds, name); if (s >= 0) return 100 + s;
            return 1000;
        }

        // The crossing an object belongs to, as a hierarchy path: the nearest folder above it whose name says
        // "crossing", or the object itself for a crossing controller with no such folder above it. Null for none.
        static string CrossingFolder(string path) {
            for (var at = Parent(path); at != null && at.Contains("/"); at = Parent(at)) {
                if (at == "World/Large Scenery" || at == "World") return null;
                if (CrossingWord.IsMatch(Last(at))) return at;
            }
            return null;
        }

        // ---- the tree ----
        internal static Result Build(IReadOnlyList<SceneObservation> rows, IReadOnlyList<SceneObservation> inventory, string search) {
            var placeView = ByPlace;
            rows = rows ?? Array.Empty<SceneObservation>();
            var entries = inventory ?? rows;
            Prepare(entries);
            var result = new Result();
            // Container objects and native track spans leave the list.
            var hidden = new HashSet<SceneObservation>(rows.Where(r => IsContainer(r) || IsNativeSpan(r)));
            foreach (var r in hidden) result.Hidden.Add((IsContainer(r) ? "container " : "native track span ") + r.Identity.Path);
            var containerPaths = new HashSet<string>(entries.Where(IsContainer).Select(e => e.Identity.Scene + "\n" + e.Identity.Path), StringComparer.Ordinal);
            // Units: every object outside a group and outside a controller, with its parts or spans beneath it. A part or
            // span whose holder the search left out brings its holder, dimmed. A part of a container stands on its own.
            var units = new List<Unit>(); var byKey = new Dictionary<string, Unit>(StringComparer.Ordinal);
            string Key(string scene, string path) => scene + "\n" + path;
            Unit Make(SceneObservation row, string scene, string path, bool context) {
                var label = row == null ? Last(path) : row.GroupPath == null && (row.Category != null || row.Family == BakedSceneryRules.Family) ? row.DisplayName : InventoryPresentationState.Name(row);
                var u = new Unit { Row = row, Node = new OutlineNode { Key = Key(scene, path) + (row?.Identity.PadMarkerId == null ? "" : "\n" + row.Identity.PadMarkerId), Label = label, Row = row, Context = context } };
                units.Add(u);
                var plain = Key(scene, path);
                if (!byKey.ContainsKey(plain)) byKey[plain] = u;
                return u;
            }
            foreach (var row in rows.Where(r => !hidden.Contains(r) && (r.GroupPath == null && r.HolderPath == null || r.GroupPath != null && containerPaths.Contains(Key(r.Identity.Scene, r.GroupPath)))))
                Make(row, row.Identity.Scene, row.Identity.Path, false).Node.Count = 1;
            foreach (var row in rows.Where(r => !hidden.Contains(r) && (r.GroupPath != null && !containerPaths.Contains(Key(r.Identity.Scene, r.GroupPath)) || r.HolderPath != null))) {
                var holderPath = row.GroupPath ?? row.HolderPath;
                if (!byKey.TryGetValue(Key(row.Identity.Scene, holderPath), out var holder)) {
                    var found = entries.FirstOrDefault(e => e.GroupPath == null && e.HolderPath == null && e.Identity.Scene == row.Identity.Scene && e.Identity.Path == holderPath);
                    holder = Make(found, row.Identity.Scene, holderPath, true);
                }
                holder.Node.Children.Add(new OutlineNode { Key = Key(row.Identity.Scene, row.Identity.Path), Label = row.GroupPath != null ? InventoryPresentationState.PartLabel(row) : InventoryPresentationState.Name(row), Row = row, Count = 1 });
                holder.Node.Count++;
            }
            foreach (var u in units) {
                if (!u.Node.Context && u.Row != null && u.Node.Children.Count != 0 && u.Row.GroupPath == null && u.Row.HolderPath == null && u.Row.Family == BakedSceneryRules.Family && !u.Row.MatchesItself(search)) { u.Node.Context = true; u.Node.Count--; }
                u.Node.Children.Sort((a, b) => { var by = StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label); return by != 0 ? by : StringComparer.Ordinal.Compare(a.Row.Identity.Path, b.Row.Identity.Path); });
                Classify(u);
            }

            // Crossings: one node per crossing under Grade crossing, named after the crossing folder or its controller,
            // holding its controller (with its detection spans), masts, bell, pads and crossbucks. Whistle signs stay signs.
            var allEntries = entries.Where(e => e.Family == "Crossing controller" && e.HolderPath == null).ToArray();
            var controllerRoots = allEntries.Select(c => new { c.Identity.Scene, c.Identity.Path, Root = CrossingFolder(c.Identity.Path) ?? c.Identity.Path }).ToArray();
            string RootOf(SceneObservation r) {
                var under = controllerRoots.Where(c => c.Scene == r.Identity.Scene && (r.Identity.Path == c.Path || r.Identity.Path.StartsWith(c.Path + "/", StringComparison.Ordinal)
                    || r.Identity.Path == c.Root || r.Identity.Path.StartsWith(c.Root + "/", StringComparison.Ordinal))).OrderByDescending(c => c.Root.Length).FirstOrDefault();
                return under?.Root ?? CrossingFolder(r.Identity.Path);
            }
            var crossingMembers = units.Where(u => u.Row != null && u.Levels.Count > 0 && u.Levels[0].Name == BakedSceneryCategories.GradeCrossing
                && (CrossingFamilies.Contains(u.Row.Family) || u.Row.Family == "Track marker" || u.Row.Category == BakedSceneryCategories.GradeCrossing)).ToList();
            foreach (var u in crossingMembers) u.CrossingRoot = RootOf(u.Row);
            var crossingUnits = new List<Unit>();
            foreach (var group in crossingMembers.Where(u => u.CrossingRoot != null).GroupBy(u => u.Row.Identity.Scene + "\n" + u.CrossingRoot)) {
                var members = group.ToList();
                if (members.Count < 2) continue;
                var root = members[0].CrossingRoot;
                var node = new OutlineNode { Key = "crossing\n" + group.Key, Label = Last(root) };
                foreach (var m in members.OrderBy(m => m.Row.Family == "Crossing controller" ? 0 : 1).ThenBy(m => m.Node.Label, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Node.Key, StringComparer.Ordinal)) { m.Node.Where = null; node.Children.Add(m.Node); }
                node.Count = node.Children.Sum(c => c.Count);
                var crossing = new Unit { Node = node, Members = members, CrossingRoot = root };
                crossing.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true));
                crossingUnits.Add(crossing);
                result.Crossings.Add(Last(root) + " (" + root + "): " + String.Join(", ", members.Select(m => m.Row.Family + " " + m.Node.Label)));
                foreach (var m in members) units.Remove(m);
            }
            units.AddRange(crossingUnits);

            foreach (var u in units) {
                var row = u.Row ?? u.Members.Select(m => m.Row).FirstOrDefault(m => m.Family == "Crossing controller") ?? u.Members.FirstOrDefault()?.Row;
                var place = u.Row != null ? PlaceOf(u.Row) : PlaceOf(row, u.CrossingRoot + "/x");
                u.Place = place.Named ?? place.Near; u.Inside = place.Inside;
                u.Nearest = place.Positional;
                u.Conflict = IsConflict(place);
                if (u.Conflict) result.Conflicts.Add(u.Node.Label + " (" + (u.Row?.Identity.Path ?? u.CrossingRoot) + "): named " + place.Named + " at " + Round(place.NamedDistance) + " m (radius " + Round(places[place.Named].Radius) + "), inside " + place.Inside + " at " + Round(place.InsideDistance) + " m (radius " + Round(places[place.Inside].Radius) + ")");
                if (u.Row?.InspectionOnly != null) result.InspectionOnlyRows.Add(u.Node.Label + " (" + u.Row.Identity.Path + ") [" + u.Row.Family + "] under " + String.Join(" / ", u.Levels.Select(l => l.Name)) + ": " + u.Row.InspectionOnly);
                // Each sign placed by its own position, with its distance to the nearby places.
                if (u.Row != null && u.Nearest && u.Levels.Count > 0 && u.Levels[0].Name == BakedSceneryCategories.Sign) {
                    var at = Measure(u.Row);
                    if (at != null) result.PositionalSigns.Add(u.Node.Label + " (" + u.Row.Identity.Path + ") @" + Round(at[0]) + "," + Round(at[1]) + " -> " + u.Place + "; nearby: "
                        + String.Join(", ", Placed.OrderBy(p => Distance(at, p.Centre)).Take(4).Select(p => p.Name + " " + Round(Distance(at, p.Centre)) + " m (r" + Round(p.Radius) + ")")));
                }
            }
            foreach (var u in crossingUnits) foreach (var m in u.Members) if (m.Row.InspectionOnly != null) result.InspectionOnlyRows.Add(m.Node.Label + " (" + m.Row.Identity.Path + ") [" + m.Row.Family + "] under Grade crossing / " + u.Node.Label + ": " + m.Row.InspectionOnly);

            string Note(Unit u) => u.Conflict ? "(stands in " + u.Inside + ")" : u.Nearest ? "(nearest place)" : null;
            string WithPlace(Unit u) => u.Place == null ? Note(u) : u.Place + (Note(u) == null ? "" : " " + Note(u));

            void Fill(OutlineNode folder, List<Unit> members, int depth, string prefix, bool placeFolders) {
                var loose = new List<Unit>();
                var levels = members.GroupBy(m => depth < m.Levels.Count ? m.Levels[depth].Name : null).ToList();
                var subfolders = new List<OutlineNode>();
                foreach (var group in levels.Where(g => g.Key != null).OrderBy(g => Rank(depth, g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                    var list = group.ToList();
                    if (!list.Any(m => m.Levels[depth].Forced) && list.Count < KindThreshold) { loose.AddRange(list); continue; }
                    var sub = new OutlineNode { Key = prefix + "/" + group.Key, Label = group.Key };
                    Fill(sub, list, depth + 1, sub.Key, placeFolders);
                    subfolders.Add(sub);
                }
                loose.AddRange(levels.Where(g => g.Key == null).SelectMany(g => g));
                // A kind folder that would be the only thing in its folder only repeats it: its contents move up.
                if (subfolders.Count == 1 && loose.Count == 0 && !members.Any(m => depth < m.Levels.Count && m.Levels[depth].Forced)) {
                    folder.Children.AddRange(subfolders[0].Children); folder.Count = subfolders[0].Count; return;
                }
                folder.Children.AddRange(subfolders);
                var placeGroups = placeFolders && loose.Count > PlaceListMinimum ? loose.Where(m => m.Place != null).GroupBy(m => m.Place, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= PlaceThreshold).ToList() : new List<IGrouping<string, Unit>>();
                // An object placed by its position may stand in a place folder; its row keeps "(nearest place)".
                // One place folder that would hold every loose object only repeats a place: the objects stand loose with it.
                if (placeGroups.Count == 1 && placeGroups[0].Count() == loose.Count && subfolders.Count == 0) placeGroups.Clear();
                var inFolders = new HashSet<Unit>(placeGroups.SelectMany(g => g));
                foreach (var g in placeGroups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                    var pf = new OutlineNode { Key = prefix + "/@" + g.Key, Label = g.Key };
                    foreach (var m in g.OrderBy(m => m.Node.Label, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Node.Key, StringComparer.Ordinal)) { m.Node.Where = Note(m); pf.Children.Add(m.Node); }
                    pf.Count = pf.Children.Sum(c => c.Count);
                    folder.Children.Add(pf);
                }
                foreach (var m in loose.Where(m => !inFolders.Contains(m)).OrderBy(m => m.Node.Label, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Place ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Node.Key, StringComparer.Ordinal)) {
                    m.Node.Where = placeView ? Note(m) : WithPlace(m);
                    folder.Children.Add(m.Node);
                }
                folder.Count = folder.Children.Sum(c => c.Count);
            }

            var tops = new List<OutlineNode>();
            string Describe(Unit u) => u.Node.Label + " [" + (u.Row?.Family ?? "no row") + "] " + (u.Row?.Identity.Path ?? u.Node.Key.Replace("\n", " "));
            if (!placeView) {
                foreach (var group in units.GroupBy(u => u.Levels[0].Name).OrderBy(g => Rank(0, g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                    var top = new OutlineNode { Key = group.Key, Label = group.Key };
                    Fill(top, group.ToList(), 1, top.Key, true);
                    tops.Add(top);
                    if (group.Key == NotSorted) { result.Unplaced += top.Count; result.CatchAll.AddRange(group.Select(Describe)); }
                }
            } else {
                // Every place the game defines is a node, also one that holds no listed object.
                var byPlace = units.GroupBy(u => u.Place ?? NoPlace, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                var names = places.Keys.Where(n => String.IsNullOrEmpty(search) || byPlace.ContainsKey(n)).Concat(byPlace.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var name in names.OrderBy(n => n == NoPlace ? 1 : 0).ThenBy(n => n, StringComparer.OrdinalIgnoreCase)) {
                    var label = places.TryGetValue(name, out var known) ? known.Name : name;
                    var top = new OutlineNode { Key = "@" + label, Label = label };
                    if (byPlace.TryGetValue(name, out var members)) Fill(top, members, 0, top.Key, false);
                    tops.Add(top);
                    if (name == NoPlace) { result.Unplaced += top.Count; result.CatchAll.AddRange(members.Select(Describe)); }
                }
            }
            result.Tops = tops;
            result.Listed = tops.Sum(t => t.Count);
            return result;
        }

        static bool detailLogged;
        // One line per view switch: view, top-level nodes, objects listed, objects not placed in any node; detail lines
        // follow once per inventory: places, Loc objects, signs placed by position, crossings, Inspection only, hidden.
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
