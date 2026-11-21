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
        const string TrackMarkerCategory = "Track marker", MilepostsKind = "Mileposts";
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
            internal bool IsPrimary;
            internal string SubOf;
            internal readonly SortedSet<string> Sources = new SortedSet<string>(StringComparer.Ordinal);
            internal readonly SortedSet<string> Spellings = new SortedSet<string>(StringComparer.Ordinal);
            internal double[] Centre; internal double Radius; internal string CentreFrom;
            internal readonly List<double[]> Members = new List<double[]>();
            internal readonly Dictionary<string, double[]> Fallback = new Dictionary<string, double[]>();
        }
        sealed class PlaceInfo { internal string Named, Near, Inside; internal double NamedDistance, NearDistance, InsideDistance; internal bool Positional; }
        sealed class LocMarker { internal string Name, Path; internal int PresenceRenderers, ActiveRenderers; internal double[] At; internal bool Listed, Visible; }

        static string cachedSceneKey;
        static object cachedOps;
        static bool placesBuiltWithEntries;
        static Dictionary<string, Place> places;
        static Place[] primaryPlaced = Array.Empty<Place>();
        static Dictionary<string, string> folderPlace;
        static Dictionary<SceneObservation, PlaceInfo> placeCache;
        static Dictionary<SceneObservation, double[]> measured;
        static Dictionary<SceneObservation, string> searchTags;
        static List<LocMarker> locMarkers;
        static Dictionary<string, LocMarker> locByPath;
        static string placesLine, locLine;
        static IReadOnlyList<OutlineNode> lastTops;

        const string Scenery = "World/Large Scenery/";
        // Folders below Large Scenery named after a structure that stands in no single place.
        static readonly HashSet<string> NoPlaceFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Plate Girder Bridge", "Global Signs" };
        static readonly Regex StructureSuffix = new Regex(@"\s+(Bridge|Turntable)\z", RegexOptions.IgnoreCase);
        static readonly Regex PlaceSuffix = new Regex(@"\s+(Station(\s+Icon)?|Depot)\z", RegexOptions.IgnoreCase);
        static readonly Regex Whitespace = new Regex(@"\s+");
        static readonly Regex CrossingWord = new Regex(@"crossing", RegexOptions.IgnoreCase);
        static readonly Regex CrossingMarkerName = new Regex(@"\b(grade\s+crossing|crossing\s+pad)\b", RegexOptions.IgnoreCase);
        static readonly Regex SpeedSignWord = new Regex(@"\b(speed(\s*sign)?|speedlimit)\b", RegexOptions.IgnoreCase);
        static readonly Regex YardLimitWord = new Regex(@"\byard\s+limit\b", RegexOptions.IgnoreCase);
        static readonly Regex StationSignWord = new Regex(@"\b(station\s+sign|sign[-_\s]*station)\b", RegexOptions.IgnoreCase);
        static readonly Regex WhistleWord = new Regex(@"\bwhistle\b", RegexOptions.IgnoreCase);
        static readonly Regex CtcWord = new Regex(@"\bctc\b", RegexOptions.IgnoreCase);
        static readonly Regex CircuitWord = new Regex(@"\bcircuit\b", RegexOptions.IgnoreCase);
        static readonly Regex SignWord = new Regex(@"\bsigns?\b", RegexOptions.IgnoreCase);
        static readonly Regex Ordinal = new Regex(@"\s*\(\d+\)\z");
        static readonly Regex MileFallback = new Regex(@"^(.*?)(\d+(?:\.\d+)?)\b");

        internal static void InvalidatePlaces() {
            cachedSceneKey = null;
            cachedOps = null;
            placesBuiltWithEntries = false;
            places = null;
            primaryPlaced = Array.Empty<Place>();
            folderPlace = null;
            placeCache = null;
            measured = null;
            searchTags = null;
            locMarkers = null;
            locByPath = null;
        }

        static string Last(string path) => path.Substring(path.LastIndexOf('/') + 1);
        static string Parent(string path) { var at = path.LastIndexOf('/'); return at < 0 ? null : path.Substring(0, at); }
        static string SceneryFolder(string path) {
            if (path == null || !path.StartsWith(Scenery, StringComparison.Ordinal)) return null;
            var rest = path.Substring(Scenery.Length); var slash = rest.IndexOf('/');
            return slash < 0 ? rest : rest.Substring(0, slash);
        }
        // Only ancestor segments count as a sign container folder, never the object's own name:
        // World/Large Scenery/Whittier/Whittier Signs stays in Whittier; children of Alarka Signs or World/Signs do not.
        static bool InSignsFolder(string path) {
            var parent = Parent(path);
            return parent != null && parent.Split('/').Any(segment => segment.EndsWith("Signs", StringComparison.OrdinalIgnoreCase));
        }
        static bool HasPosition(SceneObservation e) => e.Position != null && e.Position.Count >= 3;
        static bool Finite(double x, double z) => !Double.IsNaN(x) && !Double.IsNaN(z) && !Double.IsInfinity(x) && !Double.IsInfinity(z);
        static double DistanceSquared(double[] a, double[] b) { var dx = a[0] - b[0]; var dz = a[1] - b[1]; return dx * dx + dz * dz; }
        static double Distance(double[] a, double[] b) => Math.Sqrt(DistanceSquared(a, b));
        static string Round(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

        static string NormalizePlaceName(string raw, bool stripStationSuffix) {
            if (String.IsNullOrWhiteSpace(raw)) return null;
            var text = Whitespace.Replace(raw, " ").Trim().TrimEnd('.');
            if (stripStationSuffix) text = PlaceSuffix.Replace(text, "").Trim().TrimEnd('.');
            return String.IsNullOrEmpty(text) ? null : text;
        }
        static bool ContainsWholeWord(string haystack, string needle) {
            if (String.IsNullOrEmpty(haystack) || String.IsNullOrEmpty(needle) || needle.Length >= haystack.Length) return false;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0) {
                var left = index == 0 || !Char.IsLetterOrDigit(haystack[index - 1]);
                var after = index + needle.Length;
                var right = after == haystack.Length || !Char.IsLetterOrDigit(haystack[after]);
                if (left && right) return true;
                index += needle.Length;
            }
            return false;
        }
        static bool StartsWithWholeWord(string longer, string shorter) =>
            longer != null && shorter != null && longer.Length > shorter.Length
            && longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase) && !Char.IsLetterOrDigit(longer[shorter.Length]);

        static string SceneSignature() {
            try {
                var sb = new System.Text.StringBuilder();
                for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    sb.Append(scene.handle).Append(scene.isLoaded ? '+' : '-').Append(',');
                }
                return sb.ToString();
            } catch { return "offline"; }
        }
        static object CurrentOps() {
            try { return Native.Read(Native.Type("Model.Ops.OpsController"), "Shared"); } catch { return null; }
        }

        // Where an object stands, as map x/z: a starter placement marker or track span uses its resolved track point;
        // any other object uses the centre of the presence-giving renderers in its hierarchy, else its pivot.
        static double[] Measure(SceneObservation e, Func<SceneObservation, double[]> trackPoint) {
            if (e == null) return null;
            if (measured.TryGetValue(e, out var known)) return known;
            double[] result = null;
            if (trackPoint != null && (e.Family == StarterPlacements.Family || e.Family == FocusTarget.Span)) {
                try {
                    var pt = trackPoint(e);
                    if (pt != null && pt.Length >= 2 && Finite(pt[0], pt[1])) result = new[] { pt[0], pt[1] };
                } catch { result = null; }
            }
            if (result == null) {
                try { result = MeasureBounds(e.Handle as UnityEngine.GameObject); } catch { result = null; }
            }
            if (result == null && HasPosition(e) && Finite(e.Position[0], e.Position[2])) result = new[] { e.Position[0], e.Position[2] };
            return measured[e] = result;
        }
        static double[] MeasureBounds(UnityEngine.GameObject go) {
            if (go == null) return null;
            var bounds = new UnityEngine.Bounds(); var any = false;
            foreach (var renderer in go.GetComponentsInChildren<UnityEngine.Renderer>(true)) {
                if (renderer == null) continue;
                if (!BakedSceneryRules.GivesPresence(renderer.GetType().FullName, true, false, false)) continue;
                UnityEngine.Bounds own;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.bounds.size != UnityEngine.Vector3.zero) own = renderer.bounds;
                else {
                    var filter = renderer.GetComponent<UnityEngine.MeshFilter>();
                    var mesh = filter == null ? null : filter.sharedMesh;
                    if (renderer.isPartOfStaticBatch || mesh == null || mesh.name.StartsWith("Combined Mesh", StringComparison.Ordinal)) own = new UnityEngine.Bounds(renderer.transform.position, UnityEngine.Vector3.zero);
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
            return !Finite(map.x, map.z) ? null : new double[] { map.x, map.z };
        }

        static Place PlaceNamed(string name, string source, string spelling = null) {
            name = NormalizePlaceName(name, false);
            if (String.IsNullOrEmpty(name)) return null;
            if (!places.TryGetValue(name, out var place)) places[name] = place = new Place { Name = name };
            place.Sources.Add(source); place.Spellings.Add(source + ": " + (spelling ?? name));
            return place;
        }
        // Merges an alternate spelling (such as "Bryson City" at Bryson) into an existing scene-folder place when it
        // lies inside that place's radius and shares its whole-word prefix, preventing duplicate places.
        static Place MergeOrCreatePlace(string name, string source, string spelling, double[] at) {
            name = NormalizePlaceName(name, false);
            if (String.IsNullOrEmpty(name)) return null;
            if (places.TryGetValue(name, out var existing)) {
                existing.Sources.Add(source); existing.Spellings.Add(source + ": " + (spelling ?? name));
                return existing;
            }
            if (at != null) {
                var target = places.Values.FirstOrDefault(p => p.Sources.Contains("scene folder") && p.Centre != null
                    && Distance(at, p.Centre) <= p.Radius && (StartsWithWholeWord(name, p.Name) || StartsWithWholeWord(p.Name, name)));
                if (target != null) {
                    target.Sources.Add(source); target.Spellings.Add(source + ": " + (spelling ?? name));
                    return target;
                }
            }
            return PlaceNamed(name, source, spelling);
        }

        // Whether a renderer's GameObject and every parent up to root are switched on, so a visible board that the
        // modder disabled at its root keeps its visibility verdict while an inactive child mesh does not count.
        static bool ActiveBelow(UnityEngine.Transform t, UnityEngine.Transform root) {
            for (var at = t; at != null && at != root; at = at.parent) if (!at.gameObject.activeSelf) return false;
            return true;
        }
        static bool PresenceRendererActive(UnityEngine.Renderer r, UnityEngine.Transform root) {
            if (r == null || !r.enabled) return false;
            if (!BakedSceneryRules.GivesPresence(r.GetType().FullName, true, false, false)) return false;
            if (!ActiveBelow(r.transform, root)) return false;
            if (r is UnityEngine.MeshRenderer) {
                var filter = r.GetComponent<UnityEngine.MeshFilter>();
                if (filter == null || filter.sharedMesh == null) return false;
            }
            return true;
        }

        // Every Loc: object in the loaded World hierarchy outside kept-out roots and runtime objects, with presence check.
        static void ScanLocMarkers(IReadOnlyList<SceneObservation> entries) {
            locMarkers = new List<LocMarker>(); locByPath = new Dictionary<string, LocMarker>(StringComparer.Ordinal);
            var listed = new HashSet<string>(entries.Select(e => e.Identity.Path), StringComparer.Ordinal);
            for (var s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++) {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) {
                    if (root == null || !BakedSceneryRules.ListedBelow(root.name, false)) continue;
                    foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true)) {
                        if (t == null || (t.gameObject.hideFlags & UnityEngine.HideFlags.DontSave) != 0) continue;
                        if (!t.name.StartsWith("Loc: ", StringComparison.Ordinal)) continue;
                        var path = BoardScene.PathOf(t);
                        var renderers = t.GetComponentsInChildren<UnityEngine.Renderer>(true);
                        var presence = renderers.Count(r => r != null && BakedSceneryRules.GivesPresence(r.GetType().FullName, true, false, false));
                        var active = renderers.Count(r => PresenceRendererActive(r, t));
                        var marker = new LocMarker {
                            Name = NormalizePlaceName(t.name.Substring(5), false), Path = path,
                            PresenceRenderers = presence, ActiveRenderers = active, Visible = active != 0,
                            At = MeasureBounds(t.gameObject), Listed = listed.Contains(path)
                        };
                        if (String.IsNullOrEmpty(marker.Name)) continue;
                        locMarkers.Add(marker); locByPath[path] = marker;
                    }
                }
            }
        }
        // A Loc: track marker is a visible location board only when it holds an active presence-giving renderer.
        static bool LocVisible(SceneObservation row) {
            if (locByPath != null && locByPath.TryGetValue(row.Identity.Path, out var marker)) return marker.Visible;
            try {
                if (row.Handle is UnityEngine.GameObject go && go != null)
                    return go.GetComponentsInChildren<UnityEngine.Renderer>(true).Any(r => PresenceRendererActive(r, go.transform));
            } catch { }
            return false;
        }

        static double Median(IEnumerable<double> values) {
            var s = values.OrderBy(v => v).ToArray();
            return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
        }

        // Places are cached per world load (scene signature and OpsController instance), not per inventory snapshot,
        // so single-object edits do not re-walk Unity scenes or re-measure all 1,900 objects.
        static void Prepare(IReadOnlyList<SceneObservation> entries, Func<SceneObservation, double[]> trackPoint) {
            var sceneKey = SceneSignature();
            var opsNow = CurrentOps();
            var hasEntries = entries != null && entries.Count > 0;
            if (places != null && cachedSceneKey == sceneKey && ReferenceEquals(cachedOps, opsNow) && (placesBuiltWithEntries || !hasEntries)) {
                // Keep per-observation caches bounded across many edits.
                if (measured != null && measured.Count > (entries?.Count ?? 0) * 2 + 500) {
                    var live = new HashSet<SceneObservation>(entries ?? Array.Empty<SceneObservation>());
                    foreach (var key in measured.Keys.Where(k => !live.Contains(k)).ToArray()) measured.Remove(key);
                    foreach (var key in placeCache.Keys.Where(k => !live.Contains(k)).ToArray()) placeCache.Remove(key);
                    foreach (var key in searchTags.Keys.Where(k => !live.Contains(k)).ToArray()) searchTags.Remove(key);
                }
                return;
            }
            cachedSceneKey = sceneKey; cachedOps = opsNow; placesBuiltWithEntries = hasEntries;
            placeCache = new Dictionary<SceneObservation, PlaceInfo>();
            measured = new Dictionary<SceneObservation, double[]>();
            searchTags = new Dictionary<SceneObservation, string>();
            places = new Dictionary<string, Place>(StringComparer.OrdinalIgnoreCase);
            var notes = new List<string>();
            // 1. Scene folders below Large Scenery. A folder whose name is a place followed by Bridge or Turntable
            // belongs to that place; sign folders and Plate Girder Bridge are no place.
            var folders = new HashSet<string>((entries ?? Array.Empty<SceneObservation>()).Select(e => SceneryFolder(e.Identity.Path)).Where(f => f != null), StringComparer.OrdinalIgnoreCase);
            folderPlace = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders) {
                if (NoPlaceFolders.Contains(folder) || folder.EndsWith("Signs", StringComparison.OrdinalIgnoreCase)) continue;
                var match = StructureSuffix.Match(folder);
                var name = match.Success ? folder.Substring(0, match.Index) : folder;
                var place = PlaceNamed(name, "scene folder", folder);
                if (place != null) folderPlace[folder] = place.Name;
            }
            // Compute scene-folder centres and radii first so station/ops names inside a town radius can merge cleanly.
            foreach (var e in entries ?? Array.Empty<SceneObservation>()) {
                if (e.GroupPath != null || e.HolderPath != null) continue;
                var folder = SceneryFolder(e.Identity.Path);
                if (folder == null || InSignsFolder(e.Identity.Path) || !folderPlace.TryGetValue(folder, out var name)) continue;
                var at = Measure(e, trackPoint); if (at != null) places[name].Members.Add(at);
            }
            foreach (var place in places.Values) {
                if (place.Members.Count == 0) continue;
                place.Centre = new[] { Median(place.Members.Select(v => v[0])), Median(place.Members.Select(v => v[1])) };
                var d = place.Members.Select(v => Distance(v, place.Centre)).OrderBy(v => v).ToArray();
                place.Radius = Math.Max(MinimumRadius, d[Math.Max(0, (int)Math.Ceiling(0.9 * d.Length) - 1)]) + RadiusMargin;
                place.CentreFrom = "scenery members (" + place.Members.Count + ")";
            }
            // 2. Station map icons: normalised visible text, else normalised object name.
            foreach (var icon in (entries ?? Array.Empty<SceneObservation>()).Where(e => e.Family == StationMapIconRules.Family)) {
                string text = null;
                try { if (icon.Handle is UnityEngine.GameObject go && go != null) text = go.GetComponentInChildren<TMPro.TMP_Text>(true)?.text; } catch { }
                var raw = String.IsNullOrWhiteSpace(text) ? Last(icon.Identity.Path) : text;
                var name = NormalizePlaceName(raw, true);
                var at = Measure(icon, trackPoint);
                var spelling = (String.IsNullOrWhiteSpace(text) ? "" : "text '" + Whitespace.Replace(text, " ").Trim() + "' of ") + Last(icon.Identity.Path);
                var place = MergeOrCreatePlace(name, "station map icon", spelling, at);
                if (place != null && at != null) place.Fallback["station map icon"] = at;
            }
            // 3. Map data: operations areas and passenger stops. Each item has its own guard so one unreadable entry
            // never drops the rest. Both Area.Position and TrackSpan.GetCenterPoint return map coordinates.
            if (opsNow == null) notes.Add("OpsController.Shared is null");
            else {
                try {
                    foreach (var area in (IEnumerable)Native.Read(opsNow, "Areas") ?? new object[0]) {
                        try {
                            var rawName = Native.Read(area, "name") as string;
                            var id = Native.Read(area, "identifier");
                            double[] at = null;
                            try {
                                if (Native.Read(area, "Position") is UnityEngine.Vector3 p && Finite(p.x, p.z)) at = new double[] { p.x, p.z };
                            } catch (Exception ex) { notes.Add("Area position failed (" + rawName + "): " + ex.GetBaseException().Message); }
                            var name = NormalizePlaceName(rawName, true);
                            var place = MergeOrCreatePlace(name, "ops area", (rawName ?? "") + " (" + id + ")", at);
                            if (place != null && at != null) place.Fallback["ops area"] = at;
                        } catch (Exception ex) { notes.Add("Ops area read failed: " + ex.GetBaseException().Message); }
                    }
                } catch (Exception ex) { notes.Add("Ops areas list failed: " + ex.GetBaseException().Message); }
                try {
                    foreach (var stop in (IEnumerable)Native.Read(opsNow, "AllPassengerStops") ?? new object[0]) {
                        try {
                            var rawName = Native.Read(stop, "name") as string;
                            var id = Native.Read(stop, "identifier");
                            double[] at = null;
                            try {
                                if (Native.Read(stop, "CenterPoint") is UnityEngine.Vector3 p && Finite(p.x, p.z)) at = new double[] { p.x, p.z };
                            } catch (Exception ex) { notes.Add("Stop CenterPoint failed (" + rawName + "): " + ex.GetBaseException().Message); }
                            var name = NormalizePlaceName(rawName, true);
                            var place = MergeOrCreatePlace(name, "passenger stop", (rawName ?? "") + " (" + id + ")", at);
                            if (place != null && at != null) place.Fallback["passenger stop"] = at;
                        } catch (Exception ex) { notes.Add("Passenger stop read failed: " + ex.GetBaseException().Message); }
                    }
                } catch (Exception ex) { notes.Add("Passenger stops list failed: " + ex.GetBaseException().Message); }
            }
            // 4. Loc: objects in the loaded scene outside kept-out roots and runtime objects.
            try { ScanLocMarkers(entries ?? Array.Empty<SceneObservation>()); }
            catch (Exception e) { notes.Add("Loc scan failed: " + e.GetBaseException().Message); locMarkers = new List<LocMarker>(); locByPath = new Dictionary<string, LocMarker>(); }
            foreach (var marker in locMarkers) {
                var place = PlaceNamed(marker.Name, "Loc marker");
                if (place != null && marker.At != null) place.Fallback["Loc marker"] = marker.At;
            }
            // Assign fallback centres for places without scenery members.
            foreach (var place in places.Values) {
                if (place.Centre != null) continue;
                foreach (var source in new[] { "passenger stop", "ops area", "station map icon", "Loc marker" })
                    if (place.Fallback.TryGetValue(source, out var at)) { place.Centre = at; place.Radius = MinimumRadius + RadiusMargin; place.CentreFrom = source; break; }
            }
            // Separate primary places from sub-locations. Scene folders, station map icons and passenger stops are always
            // primary. An ops-only or Loc-only place that qualifies an existing primary place by name or lies inside a
            // primary place's radius is a sub-location: it stays visible in Place view when unfiltered, but never steals
            // positional objects or raises "(stands in X)" conflicts.
            bool CoreSource(Place p) => p.Sources.Contains("scene folder") || p.Sources.Contains("station map icon") || p.Sources.Contains("passenger stop");
            var core = places.Values.Where(CoreSource).ToList();
            foreach (var p in core) p.IsPrimary = true;
            foreach (var p in places.Values.Where(x => !x.IsPrimary)) {
                var byName = core.Where(c => ContainsWholeWord(p.Name, c.Name)).OrderByDescending(c => c.Name.Length).FirstOrDefault();
                var bySpace = p.Centre == null ? null : core.Where(c => c.Centre != null && Distance(p.Centre, c.Centre) <= c.Radius).OrderBy(c => Distance(p.Centre, c.Centre)).FirstOrDefault();
                var parent = byName ?? bySpace;
                if (parent != null) { p.IsPrimary = false; p.SubOf = parent.Name; }
                else p.IsPrimary = true;
            }
            primaryPlaced = places.Values.Where(p => p.IsPrimary && p.Centre != null).ToArray();
            placesLine = String.Join(" | ", places.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p =>
                p.Name + (p.IsPrimary ? "" : " [sub of " + p.SubOf + "]") + " [" + String.Join("+", p.Sources) + "] {" + String.Join("; ", p.Spellings) + "} "
                + (p.Centre == null ? "no position" : "@" + Round(p.Centre[0]) + "," + Round(p.Centre[1]) + " r" + Round(p.Radius) + " from " + p.CentreFrom)
                + String.Concat(p.Fallback.Select(f => " " + f.Key + "@" + Round(f.Value[0]) + "," + Round(f.Value[1]))))) + (notes.Count == 0 ? "" : " NOTES: " + String.Join("; ", notes));
            locLine = String.Join(" | ", locMarkers.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(m =>
                m.Name + " (" + m.Path + ") presence=" + m.PresenceRenderers + " active=" + m.ActiveRenderers + " " + (m.Visible ? "visible board" : "invisible marker")
                + " listed=" + (m.Listed ? "yes" : "no") + (m.At == null ? "" : " @" + Round(m.At[0]) + "," + Round(m.At[1]))));
        }

        // The place of an object. An object in a real place folder keeps that folder's place, and is marked when it
        // stands outside that place's radius and inside another primary place's radius, nearer that place. Any other
        // object gets the nearest primary place by its own position (or track point for starter placements and spans).
        static PlaceInfo PlaceOf(SceneObservation e, Func<SceneObservation, double[]> trackPoint, string path = null, double[] overrideAt = null) {
            if (e == null && overrideAt == null && path == null) return new PlaceInfo();
            if (e != null && path == null && overrideAt == null && placeCache.TryGetValue(e, out var cached)) return cached;
            path = path ?? e?.Identity.Path;
            var info = new PlaceInfo();
            var folder = SceneryFolder(path);
            if (folder != null && !InSignsFolder(path) && folderPlace.TryGetValue(folder, out var byFolder)) info.Named = byFolder;
            var at = overrideAt ?? Measure(e, trackPoint);
            if (at != null && primaryPlaced.Length != 0) {
                Place nearest = null; var nearestSq = Double.PositiveInfinity;
                Place inside = null; var insideSq = Double.PositiveInfinity;
                for (var i = 0; i < primaryPlaced.Length; i++) {
                    var p = primaryPlaced[i];
                    var sq = DistanceSquared(at, p.Centre);
                    if (sq < nearestSq) { nearestSq = sq; nearest = p; }
                    if (info.Named != null && !p.Name.Equals(info.Named, StringComparison.OrdinalIgnoreCase) && sq <= p.Radius * p.Radius && sq < insideSq) {
                        insideSq = sq; inside = p;
                    }
                }
                if (nearest != null) { info.Near = nearest.Name; info.NearDistance = Math.Sqrt(nearestSq); }
                if (info.Named != null && places.TryGetValue(info.Named, out var namedPlace) && namedPlace.Centre != null) {
                    info.NamedDistance = Distance(at, namedPlace.Centre);
                    if (inside != null) { info.Inside = inside.Name; info.InsideDistance = Math.Sqrt(insideSq); }
                }
            }
            info.Positional = info.Named == null && info.Near != null;
            if (e != null && path == e.Identity.Path && overrideAt == null) placeCache[e] = info;
            return info;
        }
        static bool IsConflict(PlaceInfo info) => info.Named != null && info.Inside != null && places.TryGetValue(info.Named, out var named) && named.Centre != null
            && info.NamedDistance > named.Radius && info.InsideDistance < info.NamedDistance;

        // ---- kinds ----
        internal static readonly string[] SignKinds = { "Station signs", "Location signs", "Speed signs", "Yard limits", "Whistle signs", "CTC signs", MilepostsKind, "Other signs" };
        static readonly string[] Tops = { BakedSceneryCategories.StationBuilding, BakedSceneryCategories.Platform, BakedSceneryCategories.Lights, BakedSceneryCategories.Sign, BakedSceneryCategories.GradeCrossing,
            BakedSceneryCategories.Bridge, BakedSceneryCategories.IndustryBuilding, BakedSceneryCategories.StorageDisplay, BakedSceneryCategories.RailroadServiceBuilding,
            TrackMarkerCategory, SpawnPoints.Family, StarterPlacements.Family, StationMapIconRules.Family, BakedSceneryCategories.Other };
        static readonly HashSet<string> CrossingEquipmentFamilies = new HashSet<string>(StringComparer.Ordinal) {
            "Crossing mast", "Crossing bell", "Crossing pad marker", CommandBand.AddedEquipmentFamily, CommandBand.AddedBellFamily
        };
        static readonly HashSet<string> PositionalPlaceFolderKinds = new HashSet<string>(StringComparer.Ordinal) {
            "Station signs", "Location signs", "Speed signs", "Yard limits", "Whistle signs", "CTC signs", "Other signs", "Independent board"
        };

        // The kind of sign an object's own name says, matched on word boundaries, or null when its name names no sign.
        internal static string SignKind(string name) {
            if (String.IsNullOrEmpty(name)) return null;
            if (SpeedSignWord.IsMatch(name)) return "Speed signs";
            if (YardLimitWord.IsMatch(name)) return "Yard limits";
            if (StationSignWord.IsMatch(name)) return "Station signs";
            if (WhistleWord.IsMatch(name)) return "Whistle signs";
            if (CtcWord.IsMatch(name) || (CircuitWord.IsMatch(name) && SignWord.IsMatch(name))) return "CTC signs";
            if (SignWord.IsMatch(name)) return "Other signs";
            return null;
        }
        static string Stem(string label) => Ordinal.Replace(label ?? "", "");
        static Level L(string name, bool forced = false) => new Level { Name = name, Forced = forced };

        // A sign folder object that the inventory lists as an inspection-only Sign group holds only signs: it is a
        // container object and gets no row (Bryson Signs, Ela Signs, Sylva Signs).
        internal static bool IsContainer(SceneObservation r) => r.GroupPath == null && r.Category == BakedSceneryCategories.Sign && r.InspectionOnly != null;
        // A serialized track span that no crossing controller holds is a native track span and leaves the list.
        internal static bool IsNativeSpan(SceneObservation r) => r.Family == "Track span" && r.HolderPath == null && !r.Identity.Path.StartsWith(CrossingCoverageWorld.RootName, StringComparison.Ordinal);
        internal static int HiddenCount(IReadOnlyList<SceneObservation> entries) => entries == null ? 0 : entries.Count(r => IsContainer(r) || IsNativeSpan(r));

        static string MarkerIdOf(SceneObservation row) {
            if (row == null) return null;
            if (!String.IsNullOrEmpty(row.Identity.PadMarkerId)) return row.Identity.PadMarkerId;
            foreach (var line in row.Bindings) {
                if (!line.StartsWith("Marker id=", StringComparison.Ordinal)) continue;
                var rest = line.Substring(10);
                var end = rest.IndexOf(';');
                var id = (end < 0 ? rest : rest.Substring(0, end)).Trim();
                return id.Length == 0 ? null : id;
            }
            return null;
        }
        static (string Prefix, double Mileage) MilepostKey(SceneObservation row, string label) {
            string prefix = null; double? mileage = null;
            if (row != null) {
                foreach (var line in row.Bindings) {
                    if (line.StartsWith("Milepost prefix=", StringComparison.Ordinal)) prefix = line.Substring(16).Trim();
                    else if (line.StartsWith("Milepost mileage=", StringComparison.Ordinal)
                        && Double.TryParse(line.Substring(17).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) mileage = parsed;
                }
            }
            if (mileage == null && !String.IsNullOrEmpty(label)) {
                var match = MileFallback.Match(label);
                if (match.Success && Double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
                    prefix = prefix ?? match.Groups[1].Value.Trim();
                    mileage = parsed;
                }
            }
            return (prefix ?? "", mileage ?? Double.PositiveInfinity);
        }
        static int CrossingChildRank(SceneObservation row) {
            if (row == null) return 99;
            switch (row.Family) {
                case "Crossing mast": return 0;
                case CommandBand.AddedEquipmentFamily: return 1;
                case "Crossing bell": return 2;
                case CommandBand.AddedBellFamily: return 3;
                case "Crossing pad marker": return 4;
                case "Track span": return 6;
                default: return row.Category == BakedSceneryCategories.GradeCrossing ? 0 : 5;
            }
        }
        static string CrossingMemberLabel(SceneObservation row, string fallback) {
            if (row == null) return fallback;
            // Inside a crossing, crossbucks, masts and bells show their own game name without the flat-list " at ..." suffix.
            if (row.Family == "Crossing mast" || row.Family == "Crossing bell"
                || (row.Family == BakedSceneryRules.Family && row.Category == BakedSceneryCategories.GradeCrossing))
                return Last(row.Identity.Path);
            return fallback;
        }

        static List<Level> LevelsOf(SceneObservation r, string label, IEnumerable<OutlineNode> children, bool inCrossing) {
            var levels = new List<Level>();
            if (r == null) { levels.Add(L(NotSorted, true)); levels.Add(L("Group root not in the object list", true)); return levels; }
            var name = Last(r.Identity.Path);
            var baked = r.Category != null || r.Family == BakedSceneryRules.Family || (children != null && children.Any(c => c.Row?.GroupPath != null));
            if (baked) {
                var category = r.Category ?? BakedSceneryCategories.Other;
                levels.Add(L(category, true));
                if (category == BakedSceneryCategories.Bridge) levels.Add(L(r.Subcategory ?? "Other bridges"));
                else if (category == BakedSceneryCategories.Sign)
                    levels.Add(L(SignKind(name) ?? children?.Select(c => SignKind(Last(c.Row.Identity.Path))).FirstOrDefault(k => k != null) ?? "Other signs"));
                else if (category != BakedSceneryCategories.GradeCrossing) levels.Add(L(Stem(label)));
                return levels;
            }
            switch (r.Family) {
                case "Crossing controller": case "Crossing pad marker": case "Crossing mast": case "Crossing bell":
                case CommandBand.AddedEquipmentFamily: case CommandBand.AddedBellFamily: case "Track span":
                    levels.Add(L(BakedSceneryCategories.GradeCrossing, true)); return levels;
                case "Milepost": levels.Add(L(BakedSceneryCategories.Sign, true)); levels.Add(L(MilepostsKind)); return levels;
                case "Track marker": {
                    if (name.StartsWith("Loc: ", StringComparison.Ordinal)) {
                        if (LocVisible(r)) { levels.Add(L(BakedSceneryCategories.Sign, true)); levels.Add(L("Location signs")); }
                        else { levels.Add(L(TrackMarkerCategory, true)); levels.Add(L("Location markers")); }
                        return levels;
                    }
                    var sign = SignKind(name);
                    if (sign != null) { levels.Add(L(BakedSceneryCategories.Sign, true)); levels.Add(L(sign)); }
                    else if (inCrossing || CrossingMarkerName.IsMatch(name)) levels.Add(L(BakedSceneryCategories.GradeCrossing, true));
                    else { levels.Add(L(TrackMarkerCategory, true)); levels.Add(L(InventoryPresentationState.Subgroup(r) ?? "Other markers")); }
                    return levels;
                }
                case "Track-relative scene object": case "Independent board":
                    levels.Add(L(BakedSceneryCategories.Other, true)); levels.Add(L(r.Family)); return levels;
            }
            if (r.Family == SpawnPoints.Family || r.Family == StarterPlacements.Family || r.Family == StationMapIconRules.Family) {
                levels.Add(L(r.Family, true)); levels.Add(L(Stem(label))); return levels;
            }
            if (r.InspectionOnly != null) { levels.Add(L(InspectionOnly, true)); levels.Add(L(String.IsNullOrEmpty(r.Family) ? "No family" : r.Family, true)); return levels; }
            levels.Add(L(NotSorted, true)); levels.Add(L(String.IsNullOrEmpty(r.Family) ? "No family" : r.Family, true));
            return levels;
        }
        static void Classify(Unit u, bool inCrossing) => u.Levels = LevelsOf(u.Row, u.Node.Label, u.Node.Children, inCrossing);

        static int Rank(int depth, string name) {
            if (name == NotSorted || name == NoPlace || name == InspectionOnly) return 10000;
            if (depth == 0) { var i = Array.IndexOf(Tops, name); return i < 0 ? 5000 : i; }
            var s = Array.IndexOf(SignKinds, name); if (s >= 0) return 100 + s;
            return 1000;
        }

        // The outermost folder above path whose name says "crossing" below World and World/Large Scenery/<town>.
        static string OutermostCrossingFolder(string path) {
            string found = null;
            for (var at = Parent(path); at != null && at.Contains("/"); at = Parent(at)) {
                if (at == "World/Large Scenery" || at == "World") break;
                if (at.StartsWith(Scenery, StringComparison.Ordinal) && at.IndexOf('/', Scenery.Length) < 0) break;
                if (CrossingWord.IsMatch(Last(at))) found = at;
            }
            return found;
        }
        static bool IsCrossingParentEligible(string parentPath) {
            if (String.IsNullOrEmpty(parentPath) || parentPath == "World" || parentPath == "World/Large Scenery") return false;
            if (parentPath.StartsWith(Scenery, StringComparison.Ordinal) && parentPath.IndexOf('/', Scenery.Length) < 0) return false;
            return true;
        }
        static bool IsStructuralUnprotectedMember(SceneObservation r) =>
            r != null && (r.Family == "Crossing pad marker" || (r.Family == BakedSceneryRules.Family && r.Category == BakedSceneryCategories.GradeCrossing));

        // ---- search covering place and kind ----
        static string GroupingSearchText(SceneObservation e, Func<SceneObservation, double[]> trackPoint) {
            if (e == null) return "";
            if (searchTags != null && searchTags.TryGetValue(e, out var cached)) return cached;
            var label = e.GroupPath == null && (e.Category != null || e.Family == BakedSceneryRules.Family) ? e.DisplayName : InventoryPresentationState.Name(e);
            var levels = LevelsOf(e, label, null, false);
            var place = PlaceOf(e, trackPoint);
            var outer = OutermostCrossingFolder(e.Identity.Path);
            var text = String.Join(" ", levels.Select(l => l.Name).Concat(new[] { place.Named, place.Near, place.Inside, outer == null ? null : Last(outer) }).Where(s => !String.IsNullOrEmpty(s)));
            if (searchTags != null) searchTags[e] = text;
            return text;
        }
        static bool MatchesGrouping(SceneObservation e, string search, Func<SceneObservation, double[]> trackPoint) =>
            !String.IsNullOrEmpty(search) && GroupingSearchText(e, trackPoint).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        internal static IReadOnlyList<SceneObservation> Rows(SceneInventory inventory, string search, OutlinerFilter filter, Func<SceneObservation, bool> changed, Func<SceneObservation, double[]> trackPoint) {
            if (inventory == null) return Array.Empty<SceneObservation>();
            Prepare(inventory.Entries, trackPoint);
            var hasSearch = !String.IsNullOrEmpty(search);
            var matched = hasSearch ? new HashSet<SceneObservation>(inventory.Search(search)) : null;
            return inventory.Entries.Where(entry => {
                if (filter == OutlinerFilter.Problems && entry.Problems.Count == 0) return false;
                if (filter == OutlinerFilter.Disabled && entry.Enabled) return false;
                if (filter == OutlinerFilter.Changed && (changed == null || !changed(entry))) return false;
                if (!hasSearch) return true;
                return matched.Contains(entry) || MatchesGrouping(entry, search, trackPoint);
            }).ToArray();
        }

        // ---- the tree ----
        internal static Result Build(IReadOnlyList<SceneObservation> rows, IReadOnlyList<SceneObservation> inventory, string search, bool narrowed, Func<SceneObservation, double[]> trackPoint) {
            var placeView = ByPlace;
            rows = rows ?? Array.Empty<SceneObservation>();
            var entries = inventory ?? rows;
            Prepare(entries, trackPoint);
            var result = new Result();
            var collectDiagnostics = LogPending;

            // Container objects and native track spans leave the list.
            var hidden = new HashSet<SceneObservation>();
            foreach (var r in rows) {
                if (!IsContainer(r) && !IsNativeSpan(r)) continue;
                hidden.Add(r);
                if (collectDiagnostics) result.Hidden.Add((IsContainer(r) ? "container " : "native track span ") + r.Identity.Path);
            }
            string Key(string scene, string path) => scene + "\n" + path;
            var containerPaths = new HashSet<string>(StringComparer.Ordinal);
            var entryByPlain = new Dictionary<string, SceneObservation>(StringComparer.Ordinal);
            var controllers = new List<SceneObservation>();
            foreach (var e in entries) {
                if (IsContainer(e)) containerPaths.Add(Key(e.Identity.Scene, e.Identity.Path));
                if (e.GroupPath == null && e.HolderPath == null) {
                    var k = Key(e.Identity.Scene, e.Identity.Path);
                    if (!entryByPlain.ContainsKey(k)) entryByPlain[k] = e;
                    if (e.Family == "Crossing controller") controllers.Add(e);
                }
            }

            // Units: every object outside a group and outside a controller, with its parts or detection spans beneath it.
            var units = new List<Unit>(); var byKey = new Dictionary<string, Unit>(StringComparer.Ordinal);
            Unit Make(SceneObservation row, string scene, string path, bool context) {
                var label = row == null ? Last(path) : row.GroupPath == null && (row.Category != null || row.Family == BakedSceneryRules.Family) ? row.DisplayName : InventoryPresentationState.Name(row);
                var u = new Unit { Row = row, Node = new OutlineNode { Key = Key(scene, path) + (row?.Identity.PadMarkerId == null ? "" : "\n" + row.Identity.PadMarkerId), Label = label, Row = row, Context = context } };
                units.Add(u);
                var plain = Key(scene, path);
                if (!byKey.ContainsKey(plain)) byKey[plain] = u;
                return u;
            }
            foreach (var row in rows) {
                if (hidden.Contains(row)) continue;
                if (row.GroupPath == null && row.HolderPath == null || row.GroupPath != null && containerPaths.Contains(Key(row.Identity.Scene, row.GroupPath)))
                    Make(row, row.Identity.Scene, row.Identity.Path, false).Node.Count = 1;
            }
            foreach (var row in rows) {
                if (hidden.Contains(row)) continue;
                if (!(row.GroupPath != null && !containerPaths.Contains(Key(row.Identity.Scene, row.GroupPath)) || row.HolderPath != null)) continue;
                var holderPath = row.GroupPath ?? row.HolderPath;
                var holderKey = Key(row.Identity.Scene, holderPath);
                if (!byKey.TryGetValue(holderKey, out var holder)) {
                    entryByPlain.TryGetValue(holderKey, out var found);
                    holder = Make(found, row.Identity.Scene, holderPath, true);
                }
                holder.Node.Children.Add(new OutlineNode { Key = Key(row.Identity.Scene, row.Identity.Path), Label = row.GroupPath != null ? InventoryPresentationState.PartLabel(row) : InventoryPresentationState.Name(row), Row = row, Count = 1 });
                holder.Node.Count++;
            }
            foreach (var u in units) {
                if (!u.Node.Context && u.Row != null && u.Node.Children.Count != 0 && u.Row.GroupPath == null && u.Row.HolderPath == null && u.Row.Family == BakedSceneryRules.Family
                    && !u.Row.MatchesItself(search) && !MatchesGrouping(u.Row, search, trackPoint)) { u.Node.Context = true; u.Node.Count--; }
                u.Node.Children.Sort((a, b) => { var by = StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label); return by != 0 ? by : StringComparer.Ordinal.Compare(a.Row.Identity.Path, b.Row.Identity.Path); });
                Classify(u, false);
            }

            // ---- crossings ----
            // 1. Protected crossings: each Crossing controller is the clickable parent row, holding its detection spans,
            // masts, bells, pads, added equipment and non-sign crossing markers beneath it.
            var controllerEntries = controllers.Select(c => new { Row = c, c.Identity.Scene, c.Identity.Path, Folder = OutermostCrossingFolder(c.Identity.Path) }).ToArray();
            var folderCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var c in controllerEntries) if (c.Folder != null) { var fk = Key(c.Scene, c.Folder); folderCounts[fk] = folderCounts.TryGetValue(fk, out var n) ? n + 1 : 1; }
            SceneObservation ControllerFor(SceneObservation r) {
                if (r == null || r.Family == "Crossing controller") return null;
                if (r.Family == "Track marker" && SignKind(Last(r.Identity.Path)) != null) return null;
                if (!CrossingEquipmentFamilies.Contains(r.Family) && r.Family != "Track marker" && !(r.Family == BakedSceneryRules.Family && r.Category == BakedSceneryCategories.GradeCrossing)) return null;
                SceneObservation best = null; var bestLen = -1;
                for (var i = 0; i < controllerEntries.Length; i++) {
                    var c = controllerEntries[i];
                    if (c.Scene != r.Identity.Scene) continue;
                    var path = r.Identity.Path;
                    var matchByController = path.StartsWith(c.Path + "/", StringComparison.Ordinal);
                    var matchByFolder = !matchByController && c.Folder != null && folderCounts[Key(c.Scene, c.Folder)] == 1 && path.StartsWith(c.Folder + "/", StringComparison.Ordinal);
                    if (!matchByController && !matchByFolder) continue;
                    var len = matchByController ? c.Path.Length + 10000 : c.Folder.Length;
                    if (len > bestLen) { bestLen = len; best = c.Row; }
                }
                return best;
            }
            var absorbed = new HashSet<Unit>();
            var touchedControllers = new HashSet<Unit>();
            foreach (var u in units.ToArray()) {
                var owner = ControllerFor(u.Row);
                if (owner == null) continue;
                var ownerKey = Key(owner.Identity.Scene, owner.Identity.Path);
                if (!byKey.TryGetValue(ownerKey, out var controllerUnit)) {
                    controllerUnit = Make(owner, owner.Identity.Scene, owner.Identity.Path, true);
                    Classify(controllerUnit, false);
                }
                u.Node.Label = CrossingMemberLabel(u.Row, u.Node.Label);
                u.Node.Where = null;
                controllerUnit.Node.Children.Add(u.Node);
                controllerUnit.Node.Count += u.Node.Count;
                absorbed.Add(u);
                touchedControllers.Add(controllerUnit);
            }
            if (absorbed.Count != 0) units.RemoveAll(absorbed.Contains);
            foreach (var c in units.Where(u => u.Row?.Family == "Crossing controller" && u.Node.Children.Count > 0)) {
                touchedControllers.Add(c);
                c.Node.Children.Sort((a, b) => {
                    var byKind = CrossingChildRank(a.Row).CompareTo(CrossingChildRank(b.Row));
                    if (byKind != 0) return byKind;
                    var byName = StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label);
                    return byName != 0 ? byName : StringComparer.Ordinal.Compare(a.Row?.Identity.Path ?? a.Key, b.Row?.Identity.Path ?? b.Key);
                });
                var outer = OutermostCrossingFolder(c.Row.Identity.Path);
                if (outer != null && folderCounts.TryGetValue(Key(c.Row.Identity.Scene, outer), out var n) && n == 1) {
                    var own = Last(c.Row.Identity.Path);
                    if (own.Equals("Crossing", StringComparison.OrdinalIgnoreCase) || own.Equals("Grade Crossing", StringComparison.OrdinalIgnoreCase))
                        c.Node.Label = Last(outer);
                }
                if (collectDiagnostics) result.Crossings.Add("protected " + c.Node.Label + " (" + c.Row.Identity.Path + "): " + String.Join(", ", c.Node.Children.Select(m => m.Row.Family + " " + m.Label)));
            }

            // 2. Unprotected crossings: structural parents holding crossbucks and/or crossing pads, split by live parent
            // Transform or pad marker ID when crossings share one path (such as Old River Rd).
            var structuralParents = new HashSet<string>(StringComparer.Ordinal);
            var sharedPathPadIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var e in entries) {
                if (!IsStructuralUnprotectedMember(e)) continue;
                var p = Parent(e.Identity.Path);
                if (!IsCrossingParentEligible(p)) continue;
                var pk = Key(e.Identity.Scene, p);
                structuralParents.Add(pk);
                if (!String.IsNullOrEmpty(e.Identity.PadMarkerId)) {
                    if (!sharedPathPadIds.TryGetValue(pk, out var set)) sharedPathPadIds[pk] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(e.Identity.PadMarkerId);
                }
            }
            string UnprotectedGroupKey(Unit u, out string parentPath, out string padId) {
                parentPath = null; padId = null;
                var r = u.Row;
                if (r == null) return null;
                var eligible = IsStructuralUnprotectedMember(r) || (r.Family == "Track marker" && SignKind(Last(r.Identity.Path)) == null);
                if (!eligible) return null;
                var p = Parent(r.Identity.Path);
                if (!IsCrossingParentEligible(p)) return null;
                var pk = Key(r.Identity.Scene, p);
                if (!structuralParents.Contains(pk)) return null;
                parentPath = p;
                padId = MarkerIdOf(r);
                try {
                    if (r.Handle is UnityEngine.GameObject go && go != null && go.transform.parent != null)
                        return pk + "\n#" + go.transform.parent.GetInstanceID().ToString(CultureInfo.InvariantCulture);
                } catch { }
                if (sharedPathPadIds.TryGetValue(pk, out var ids) && ids.Count > 1 && padId != null)
                    return pk + "\n@" + padId;
                return pk;
            }
            var unprotectedGroups = new Dictionary<string, (string Scene, string ParentPath, List<Unit> Members, string PadId)>(StringComparer.Ordinal);
            var pathGroupCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var u in units) {
                var gk = UnprotectedGroupKey(u, out var pPath, out var padId);
                if (gk == null) continue;
                if (!unprotectedGroups.TryGetValue(gk, out var bucket)) {
                    unprotectedGroups[gk] = bucket = (u.Row.Identity.Scene, pPath, new List<Unit>(), padId);
                    var pk = Key(u.Row.Identity.Scene, pPath);
                    pathGroupCount[pk] = pathGroupCount.TryGetValue(pk, out var c) ? c + 1 : 1;
                } else if (bucket.PadId == null && padId != null) {
                    unprotectedGroups[gk] = (bucket.Scene, bucket.ParentPath, bucket.Members, padId);
                }
                bucket.Members.Add(u);
            }
            // Count how many distinct crossings sit below each outermost crossing folder so we never rename two siblings alike.
            var outerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in unprotectedGroups) {
                if (kv.Value.Members.Count < 2) continue;
                var outer = OutermostCrossingFolder(kv.Value.ParentPath + "/x");
                if (outer != null) { var ok = Key(kv.Value.Scene, outer); outerCounts[ok] = outerCounts.TryGetValue(ok, out var c) ? c + 1 : 1; }
            }
            var unprotectedAbsorbed = new HashSet<Unit>();
            var crossingUnits = new List<Unit>();
            foreach (var kv in unprotectedGroups) {
                var (scene, pPath, members, padId) = kv.Value;
                if (members.Count < 2) continue;
                var pk = Key(scene, pPath);
                var shared = pathGroupCount.TryGetValue(pk, out var pc) && pc > 1;
                var outer = !shared ? OutermostCrossingFolder(pPath + "/x") : null;
                var root = outer != null && outerCounts.TryGetValue(Key(scene, outer), out var oc) && oc == 1 ? outer : pPath;
                var label = Last(root) + (shared && !String.IsNullOrEmpty(padId) ? " (pad " + padId + ")" : "");
                var node = new OutlineNode { Key = "crossing\n" + kv.Key, Label = label };
                foreach (var m in members.OrderBy(m => CrossingChildRank(m.Row)).ThenBy(m => CrossingMemberLabel(m.Row, m.Node.Label), StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Node.Key, StringComparer.Ordinal)) {
                    m.Node.Label = CrossingMemberLabel(m.Row, m.Node.Label);
                    m.Node.Where = null;
                    node.Children.Add(m.Node);
                    unprotectedAbsorbed.Add(m);
                }
                node.Count = node.Children.Sum(c => c.Count);
                var crossing = new Unit { Node = node, Members = members, CrossingRoot = root };
                crossing.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true));
                crossingUnits.Add(crossing);
                if (collectDiagnostics) result.Crossings.Add("unprotected " + label + " (" + root + "): " + String.Join(", ", members.Select(m => m.Row.Family + " " + m.Node.Label)));
            }
            if (unprotectedAbsorbed.Count != 0) units.RemoveAll(unprotectedAbsorbed.Contains);
            units.AddRange(crossingUnits);

            // ---- places per unit ----
            foreach (var u in units) {
                PlaceInfo place;
                if (u.Row != null) place = PlaceOf(u.Row, trackPoint);
                else {
                    var positions = u.Members.Select(m => Measure(m.Row, trackPoint)).Where(p => p != null).ToArray();
                    double[] centre = positions.Length == 0 ? null : new[] { positions.Average(p => p[0]), positions.Average(p => p[1]) };
                    place = PlaceOf(u.Members.FirstOrDefault()?.Row, trackPoint, u.CrossingRoot + "/x", centre);
                }
                u.Place = place.Named ?? place.Near; u.Inside = place.Inside;
                u.Nearest = place.Positional;
                u.Conflict = IsConflict(place);
                if (!collectDiagnostics) continue;
                if (u.Conflict) result.Conflicts.Add(u.Node.Label + " (" + (u.Row?.Identity.Path ?? u.CrossingRoot) + "): named " + place.Named + " at " + Round(place.NamedDistance) + " m (radius " + Round(places[place.Named].Radius) + "), inside " + place.Inside + " at " + Round(place.InsideDistance) + " m (radius " + Round(places[place.Inside].Radius) + ")");
                if (u.Row?.InspectionOnly != null) result.InspectionOnlyRows.Add(u.Node.Label + " (" + u.Row.Identity.Path + ") [" + u.Row.Family + "] under " + String.Join(" / ", u.Levels.Select(l => l.Name)) + ": " + u.Row.InspectionOnly);
                if (u.Row != null && u.Nearest && u.Levels.Count > 0 && u.Levels[0].Name == BakedSceneryCategories.Sign) {
                    var at = Measure(u.Row, trackPoint);
                    if (at != null) result.PositionalSigns.Add(u.Node.Label + " (" + u.Row.Identity.Path + ") @" + Round(at[0]) + "," + Round(at[1]) + " -> " + u.Place + "; nearby: "
                        + String.Join(", ", primaryPlaced.OrderBy(p => DistanceSquared(at, p.Centre)).Take(4).Select(p => p.Name + " " + Round(Distance(at, p.Centre)) + " m (r" + Round(p.Radius) + ")")));
                }
            }
            if (collectDiagnostics) {
                foreach (var u in crossingUnits) foreach (var m in u.Members) if (m.Row.InspectionOnly != null) result.InspectionOnlyRows.Add(m.Node.Label + " (" + m.Row.Identity.Path + ") [" + m.Row.Family + "] under Grade crossing / " + u.Node.Label + ": " + m.Row.InspectionOnly);
                foreach (var c in touchedControllers) foreach (var child in c.Node.Children) if (child.Row?.InspectionOnly != null) result.InspectionOnlyRows.Add(child.Label + " (" + child.Row.Identity.Path + ") [" + child.Row.Family + "] under Grade crossing / " + c.Node.Label + ": " + child.Row.InspectionOnly);
            }

            string Note(Unit u) => u.Conflict ? "(stands in " + u.Inside + ")" : u.Nearest ? "(nearest place)" : null;
            string WithPlace(Unit u) => u.Place == null ? Note(u) : u.Place + (Note(u) == null ? "" : " " + Note(u));

            IEnumerable<Unit> OrderLoose(IEnumerable<Unit> items, bool mileposts) {
                if (mileposts) {
                    return items.OrderBy(m => MilepostKey(m.Row, m.Node.Label).Prefix, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(m => MilepostKey(m.Row, m.Node.Label).Mileage)
                        .ThenBy(m => m.Node.Label, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(m => m.Node.Key, StringComparer.Ordinal);
                }
                return items.OrderBy(m => m.Node.Label, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(m => m.Place ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(m => m.Node.Key, StringComparer.Ordinal);
            }

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
                var isMileposts = folder.Label == MilepostsKind || (loose.Count > 0 && loose.All(m => m.Row?.Family == "Milepost"));
                // Positional objects form place folders only in long sign and board lists, never under Bridge (so Plate
                // Girder Bridges never land in a Bryson folder) and never under Mileposts (keeping milepost order intact).
                var allowPositionalFolders = PositionalPlaceFolderKinds.Contains(folder.Label);
                var placeGroups = placeFolders && !isMileposts && loose.Count > PlaceListMinimum
                    ? loose.Where(m => m.Place != null && (!m.Nearest || allowPositionalFolders))
                        .GroupBy(m => m.Place, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() >= PlaceThreshold).ToList()
                    : new List<IGrouping<string, Unit>>();
                // One place folder that would hold every loose object only repeats a place: the objects stand loose with it.
                if (placeGroups.Count == 1 && placeGroups[0].Count() == loose.Count && subfolders.Count == 0) placeGroups.Clear();
                var inFolders = new HashSet<Unit>(placeGroups.SelectMany(g => g));
                foreach (var g in placeGroups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                    var pf = new OutlineNode { Key = prefix + "/@" + g.Key, Label = g.Key };
                    foreach (var m in OrderLoose(g, isMileposts)) { m.Node.Where = Note(m); pf.Children.Add(m.Node); }
                    pf.Count = pf.Children.Sum(c => c.Count);
                    folder.Children.Add(pf);
                }
                foreach (var m in OrderLoose(loose.Where(m => !inFolders.Contains(m)), isMileposts)) {
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
                    if (group.Key == NotSorted) { result.Unplaced += top.Count; if (collectDiagnostics) result.CatchAll.AddRange(group.Select(Describe)); }
                }
            } else {
                // Every place the game defines appears when unfiltered; under a search or filter only matching places show.
                var byPlace = units.GroupBy(u => u.Place ?? NoPlace, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                var names = places.Keys.Where(n => !narrowed || byPlace.ContainsKey(n)).Concat(byPlace.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var name in names.OrderBy(n => n == NoPlace ? 1 : 0).ThenBy(n => n, StringComparer.OrdinalIgnoreCase)) {
                    var label = places.TryGetValue(name, out var known) ? known.Name : name;
                    var top = new OutlineNode { Key = "@" + label, Label = label };
                    if (byPlace.TryGetValue(name, out var members)) Fill(top, members, 0, top.Key, false);
                    tops.Add(top);
                    if (name == NoPlace) { result.Unplaced += top.Count; if (collectDiagnostics) result.CatchAll.AddRange(members.Select(Describe)); }
                }
            }
            lastTops = tops;
            result.Tops = tops;
            result.Listed = tops.Sum(t => t.Count);
            return result;
        }

        // Widths of the visible prototype rows including their nesting indent and Where text, for FitOutliner.
        const float IndentPerLevel = 16f;
        internal static float[] FitWidths(TMPro.TMP_Text measure) {
            if (measure == null || lastTops == null || lastTops.Count == 0) return null;
            var widths = new List<float>();
            void Walk(OutlineNode node, int depth) {
                var text = (node.Row == null ? "(" + node.Count.ToString(CultureInfo.InvariantCulture) + ") " : "")
                    + (node.Label ?? "") + (String.IsNullOrEmpty(node.Where) ? "" : "  " + node.Where);
                // Baseline RowDecoration already accounts for one folder level; add indent for deeper levels.
                var extra = Math.Max(0, depth - 1) * IndentPerLevel;
                widths.Add(measure.GetPreferredValues(text).x + extra);
                foreach (var child in node.Children) Walk(child, depth + 1);
            }
            foreach (var top in lastTops) Walk(top, 0);
            return widths.ToArray();
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
