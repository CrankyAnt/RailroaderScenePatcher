// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: throwaway places of the outliner grouping prototype: where objects stand and which places the game defines.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    internal static partial class PrototypeOutlinerGrouping {
        // A place's radius: the 90th percentile of its members' distances from its centre, at least MinimumRadius
        // metres, plus RadiusMargin. A place without scenery members gets MinimumRadius plus RadiusMargin.
        internal const double MinimumRadius = 250, RadiusMargin = 150;
        const string SceneFolderSource = "scene folder", StationMapIconSource = "station map icon", OpsAreaSource = "ops area",
            PassengerStopSource = "passenger stop", LocMarkerSource = "Loc marker";
        // The order in which a place without scenery members takes its centre.
        static readonly string[] FallbackCentreSources = { PassengerStopSource, OpsAreaSource, StationMapIconSource, LocMarkerSource };
        // Sources whose places are always primary places.
        static readonly string[] PrimarySources = { SceneFolderSource, StationMapIconSource, PassengerStopSource };
        const string Scenery = "World/Large Scenery/", LocPrefix = "Loc: ";
        // Folders below Large Scenery named after a structure that stands in no single place.
        static readonly HashSet<string> NoPlaceFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Plate Girder Bridge", "Global Signs" };
        static readonly Regex StructureSuffix = new Regex(@"\s+(Bridge|Turntable)\z", RegexOptions.IgnoreCase);
        static readonly Regex PlaceSuffix = new Regex(@"\s+(Station(\s+Icon)?|Depot)\z", RegexOptions.IgnoreCase);
        static readonly Regex Whitespace = new Regex(@"\s+");

        sealed class Place {
            internal string Name;
            internal bool IsPrimary;
            internal string SubOf;
            internal readonly SortedSet<string> Sources = new SortedSet<string>(StringComparer.Ordinal);
            internal readonly SortedSet<string> Spellings = new SortedSet<string>(StringComparer.Ordinal);
            internal double[] Centre; internal double Radius; internal string CentreFrom;
            internal readonly List<double[]> Members = new List<double[]>();
            internal readonly Dictionary<string, double[]> Fallback = new Dictionary<string, double[]>(StringComparer.Ordinal);
        }
        sealed class PlaceInfo { internal string Named, Near, Inside; internal double NamedDistance, NearDistance, InsideDistance; internal bool Positional; }
        sealed class LocMarker { internal string Name, Path; internal int ShowingRenderers, EnabledRenderers; internal double[] At; internal bool Listed, Visible; }

        // Per world load: the scene signature and the OpsController instance the places were read from.
        static string cachedSceneKey;
        static object cachedOps;
        static bool placesBuiltWithEntries;
        static IReadOnlyList<SceneObservation> preparedEntries;
        static Dictionary<string, Place> places;
        static Place[] primaryPlaced = Array.Empty<Place>();
        static Dictionary<string, string> folderPlace;
        static List<LocMarker> locMarkers;
        static List<string> placeNotes;
        static string placesLine, locLine;
        // Per observation: an edit replaces the observation, so these never hold a verdict older than the object.
        static Dictionary<SceneObservation, PlaceInfo> placeCache;
        static Dictionary<SceneObservation, double[]> measured;
        static Dictionary<SceneObservation, bool> locVisibility;
        static Dictionary<SceneObservation, string> searchTags;

        internal static void InvalidatePlaces() {
            cachedSceneKey = null; cachedOps = null; placesBuiltWithEntries = false; preparedEntries = null;
            places = null; primaryPlaced = Array.Empty<Place>(); folderPlace = null; locMarkers = null;
            placeCache = null; measured = null; locVisibility = null; searchTags = null;
        }

        static string SceneryFolder(string path) {
            if (path == null || !path.StartsWith(Scenery, StringComparison.Ordinal)) return null;
            var rest = path.Substring(Scenery.Length); var slash = rest.IndexOf('/');
            return slash < 0 ? rest : rest.Substring(0, slash);
        }
        // Whether an object stands in a container object of signs (Alarka Signs, Bryson Signs, Ela/Signs, World/Signs).
        // Only the objects above it count, never its own name: World/Large Scenery/Whittier/Whittier Signs stays in Whittier.
        static bool InSignContainerObject(string path) {
            var parent = Parent(path);
            return parent != null && parent.Split('/').Any(segment => segment.EndsWith("Signs", StringComparison.OrdinalIgnoreCase));
        }
        // World, World/Large Scenery and a place's own object directly below it stop every upward walk for a crossing.
        static bool IsPlaceLevel(string path) =>
            String.IsNullOrEmpty(path) || path == "World" || path == "World/Large Scenery"
            || path.StartsWith(Scenery, StringComparison.Ordinal) && path.IndexOf('/', Scenery.Length) < 0;
        static bool HasPosition(SceneObservation e) => e.Position != null && e.Position.Count >= 3;
        static bool Finite(double x, double z) => !Double.IsNaN(x) && !Double.IsNaN(z) && !Double.IsInfinity(x) && !Double.IsInfinity(z);
        static double DistanceSquared(double[] a, double[] b) { var dx = a[0] - b[0]; var dz = a[1] - b[1]; return dx * dx + dz * dz; }
        static double Distance(double[] a, double[] b) => Math.Sqrt(DistanceSquared(a, b));

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
        static double Median(IEnumerable<double> values) {
            var s = values.OrderBy(v => v).ToArray();
            return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
        }

        // The engine calls stand in a method of their own, so a failure to reach the engine (outside the game) is caught here.
        static string SceneSignature() {
            try { return ReadSceneSignature(); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { WarnOnce("scene signature", "the loaded scenes could not be read: " + e.GetBaseException().Message); return "unreadable"; }
        }
        static string ReadSceneSignature() {
            var signature = new System.Text.StringBuilder();
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                signature.Append(scene.handle).Append(scene.isLoaded ? '+' : '-').Append(',');
            }
            return signature.ToString();
        }
        static object CurrentOps() {
            try { return Native.Read(Native.Type("Model.Ops.OpsController"), "Shared"); }
            catch (Exception e) when (e is InvalidOperationException || e is MissingMemberException || e is System.Reflection.TargetInvocationException) {
                WarnOnce("ops controller", "OpsController.Shared could not be read: " + e.GetBaseException().Message); return null;
            }
        }

        // ---- measuring ----
        // A starter placement marker and a track span stand at their resolved track point; their scene object's own
        // position is not where they act (SceneInventory: FocusTarget).
        static bool UsesTrackPoint(string family) => family == StarterPlacements.Family || family == FocusTarget.Span;
        // Where an object stands, as map x/z: its track point when it uses one, else the centre of the presence-giving
        // renderers in its hierarchy (a group with its parts), else its pivot.
        static double[] Measure(SceneObservation e, Func<SceneObservation, double[]> trackPoint) {
            if (e == null) return null;
            if (measured.TryGetValue(e, out var known)) return known;
            double[] result = null;
            if (trackPoint != null && UsesTrackPoint(e.Family)) {
                try {
                    var point = trackPoint(e);
                    if (point != null && point.Length >= 2 && Finite(point[0], point[1])) result = new[] { point[0], point[1] };
                } catch (Exception ex) when (!(ex is OutOfMemoryException)) { WarnOnce("track point", "a track point could not be resolved, so the object is measured at its bounds: " + ex.GetBaseException().Message); }
            }
            if (result == null) {
                // A destroyed scene object, or no engine at all (outside the game), leaves the observation's own position.
                try { result = MeasureBounds(e.Handle as UnityEngine.GameObject); }
                catch (Exception ex) when (!(ex is OutOfMemoryException)) { WarnOnce("bounds", "an object's renderers could not be read, so it is measured at its position: " + ex.GetBaseException().Message); }
            }
            if (result == null && HasPosition(e) && Finite(e.Position[0], e.Position[2])) result = new[] { e.Position[0], e.Position[2] };
            return measured[e] = result;
        }
        // The centre of the presence-giving renderers below an object. A renderer that is off or inactive counts by its
        // mesh's bounds, unless it is statically batched, whose mesh spans the whole batch; then by its transform.
        static double[] MeasureBounds(UnityEngine.GameObject go) {
            if (go == null) return null;
            var bounds = new UnityEngine.Bounds(); var any = false;
            foreach (var renderer in go.GetComponentsInChildren<UnityEngine.Renderer>(true)) {
                if (renderer == null || !BakedSceneryRules.GivesPresence(renderer.GetType().FullName, true, false, false)) continue;
                UnityEngine.Bounds own;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.bounds.size != UnityEngine.Vector3.zero) own = renderer.bounds;
                else {
                    var filter = renderer.GetComponent<UnityEngine.MeshFilter>();
                    var mesh = filter == null ? null : filter.sharedMesh;
                    if (renderer.isPartOfStaticBatch || mesh == null || mesh.name.StartsWith("Combined Mesh", StringComparison.Ordinal)) own = new UnityEngine.Bounds(renderer.transform.position, UnityEngine.Vector3.zero);
                    else {
                        var local = mesh.bounds; var extents = local.extents;
                        own = new UnityEngine.Bounds(renderer.transform.TransformPoint(local.center), UnityEngine.Vector3.zero);
                        for (var i = 0; i < 8; i++)
                            own.Encapsulate(renderer.transform.TransformPoint(local.center + new UnityEngine.Vector3((i & 1) == 0 ? -extents.x : extents.x, (i & 2) == 0 ? -extents.y : extents.y, (i & 4) == 0 ? -extents.z : extents.z)));
                    }
                }
                if (any) bounds.Encapsulate(own); else { bounds = own; any = true; }
            }
            return Map(any ? bounds.center : go.transform.position, go.transform);
        }
        static double[] Map(UnityEngine.Vector3 scene, UnityEngine.Transform transform) {
            var map = NativeInventory.MapPosition(scene, NativeInventory.MovesWithOrigin(transform));
            return !Finite(map.x, map.z) ? null : new double[] { map.x, map.z };
        }

        // ---- Loc: objects ----
        // A renderer that shows something: a presence-giving type, and for a mesh renderer, a mesh. Whether it is enabled
        // or active now is left out: a board's RendererCuller switches its renderers off with distance, so at any moment
        // most boards render nothing.
        static bool ShowsSomething(UnityEngine.Renderer r) {
            if (r == null || !BakedSceneryRules.GivesPresence(r.GetType().FullName, true, false, false)) return false;
            if (!(r is UnityEngine.MeshRenderer)) return true;
            var filter = r.GetComponent<UnityEngine.MeshFilter>();
            return filter != null && filter.sharedMesh != null;
        }
        static bool IsVisibleBoard(UnityEngine.GameObject go) => go != null && go.GetComponentsInChildren<UnityEngine.Renderer>(true).Any(ShowsSomething);
        // A listed Loc: object is a visible location board when a renderer below it shows something, else an invisible
        // marker. The verdict is cached per observation.
        static bool LocVisible(SceneObservation row) {
            if (locVisibility.TryGetValue(row, out var known)) return known;
            bool visible;
            try { visible = IsVisibleBoard(row.Handle as UnityEngine.GameObject); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { WarnOnce("loc visibility", "a Loc: object's renderers could not be read, so it counts as an invisible marker: " + e.GetBaseException().Message); visible = false; }
            return locVisibility[row] = visible;
        }
        // Every Loc: object in the loaded scenes outside kept-out roots and runtime objects, listed or not: the places
        // they name and the log table.
        static List<LocMarker> ScanLocMarkers(IReadOnlyList<SceneObservation> entries) {
            var found = new List<LocMarker>();
            var listed = new HashSet<string>(entries.Select(e => e.Identity.Path), StringComparer.Ordinal);
            for (var s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++) {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) {
                    if (root == null || !BakedSceneryRules.ListedBelow(root.name, false)) continue;
                    foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true)) {
                        if (t == null || (t.gameObject.hideFlags & UnityEngine.HideFlags.DontSave) != 0 || !t.name.StartsWith(LocPrefix, StringComparison.Ordinal)) continue;
                        var name = NormalizePlaceName(t.name.Substring(LocPrefix.Length), false);
                        if (String.IsNullOrEmpty(name)) continue;
                        var renderers = t.GetComponentsInChildren<UnityEngine.Renderer>(true);
                        var path = BoardScene.PathOf(t);
                        var showing = renderers.Count(ShowsSomething);
                        found.Add(new LocMarker {
                            Name = name, Path = path, Listed = listed.Contains(path), At = MeasureBounds(t.gameObject),
                            ShowingRenderers = showing, EnabledRenderers = renderers.Count(r => r != null && r.enabled && ShowsSomething(r)), Visible = showing != 0
                        });
                    }
                }
            }
            return found;
        }

        // ---- places ----
        static Place AddSource(Place place, string source, string spelling) {
            place.Sources.Add(source); place.Spellings.Add(source + ": " + spelling);
            return place;
        }
        static Place PlaceNamed(string name, string source, string spelling = null) {
            name = NormalizePlaceName(name, false);
            if (String.IsNullOrEmpty(name)) return null;
            if (!places.TryGetValue(name, out var place)) places[name] = place = new Place { Name = name };
            return AddSource(place, source, spelling ?? name);
        }
        // An alternate spelling, such as "Bryson City" at Bryson, joins a scene-folder place when it lies inside that
        // place's radius and one name starts the other as whole words.
        static Place MergeOrCreatePlace(string name, string source, string spelling, double[] at) {
            name = NormalizePlaceName(name, false);
            if (String.IsNullOrEmpty(name)) return null;
            if (places.TryGetValue(name, out var existing)) return AddSource(existing, source, spelling ?? name);
            var target = at == null ? null : places.Values.FirstOrDefault(p => p.Sources.Contains(SceneFolderSource) && p.Centre != null
                && Distance(at, p.Centre) <= p.Radius && (StartsWithWholeWord(name, p.Name) || StartsWithWholeWord(p.Name, name)));
            return target != null ? AddSource(target, source, spelling ?? name) : PlaceNamed(name, source, spelling);
        }

        // Places are read once per world load (scene signature and OpsController instance), not per inventory snapshot,
        // and at most once per outliner rebuild.
        static void Prepare(IReadOnlyList<SceneObservation> entries, Func<SceneObservation, double[]> trackPoint) {
            entries = entries ?? Array.Empty<SceneObservation>();
            if (places != null && ReferenceEquals(preparedEntries, entries)) return;
            var sceneKey = SceneSignature();
            var ops = CurrentOps();
            var hasEntries = entries.Count > 0;
            if (places != null && cachedSceneKey == sceneKey && ReferenceEquals(cachedOps, ops) && (placesBuiltWithEntries || !hasEntries)) {
                preparedEntries = entries;
                TrimObservationCaches(entries);
                return;
            }
            cachedSceneKey = sceneKey; cachedOps = ops; placesBuiltWithEntries = hasEntries; preparedEntries = entries;
            placeCache = new Dictionary<SceneObservation, PlaceInfo>(); measured = new Dictionary<SceneObservation, double[]>();
            locVisibility = new Dictionary<SceneObservation, bool>(); searchTags = new Dictionary<SceneObservation, string>();
            places = new Dictionary<string, Place>(StringComparer.OrdinalIgnoreCase);
            placeNotes = new List<string>();
            ReadSceneFolders(entries);
            MeasureSceneFolderPlaces(entries, trackPoint);
            ReadStationMapIcons(entries, trackPoint);
            if (ops == null) placeNotes.Add("OpsController.Shared is null");
            else {
                ReadOpsPlaces(ops, "Areas", "Position", OpsAreaSource);
                ReadOpsPlaces(ops, "AllPassengerStops", "CenterPoint", PassengerStopSource);
            }
            ReadLocMarkers(entries);
            AssignFallbackCentres();
            SeparateSubLocations();
            FormatPlaceLines();
        }
        // An edit replaces observations; drop the cached verdicts of the ones no longer listed.
        static void TrimObservationCaches(IReadOnlyList<SceneObservation> entries) {
            if (measured.Count <= entries.Count * 2 + 500) return;
            var live = new HashSet<SceneObservation>(entries);
            foreach (var cache in new IDictionary[] { measured, placeCache, locVisibility, searchTags }) {
                var stale = cache.Keys.Cast<SceneObservation>().Where(k => !live.Contains(k)).ToArray();
                foreach (var key in stale) cache.Remove(key);
            }
        }
        // 1. Folders below Large Scenery. A folder named after a place and Bridge or Turntable belongs to that place;
        // container objects of signs and Plate Girder Bridge are no place.
        static void ReadSceneFolders(IReadOnlyList<SceneObservation> entries) {
            folderPlace = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in entries.Select(e => SceneryFolder(e.Identity.Path)).Where(f => f != null).Distinct(StringComparer.OrdinalIgnoreCase)) {
                if (NoPlaceFolders.Contains(folder) || folder.EndsWith("Signs", StringComparison.OrdinalIgnoreCase)) continue;
                var suffix = StructureSuffix.Match(folder);
                var place = PlaceNamed(suffix.Success ? folder.Substring(0, suffix.Index) : folder, SceneFolderSource, folder);
                if (place != null) folderPlace[folder] = place.Name;
            }
        }
        // The centre and radius of each scene-folder place, from its members measured at their bounds (objects outside
        // any group or controller in its folders, container objects of signs left out). First, so other sources can
        // merge into a town by its radius.
        static void MeasureSceneFolderPlaces(IReadOnlyList<SceneObservation> entries, Func<SceneObservation, double[]> trackPoint) {
            foreach (var e in entries) {
                if (e.GroupPath != null || e.HolderPath != null || InSignContainerObject(e.Identity.Path)) continue;
                var folder = SceneryFolder(e.Identity.Path);
                if (folder == null || !folderPlace.TryGetValue(folder, out var name)) continue;
                var at = Measure(e, trackPoint);
                if (at != null) places[name].Members.Add(at);
            }
            foreach (var place in places.Values.Where(p => p.Members.Count != 0)) {
                place.Centre = new[] { Median(place.Members.Select(v => v[0])), Median(place.Members.Select(v => v[1])) };
                var distances = place.Members.Select(v => Distance(v, place.Centre)).OrderBy(v => v).ToArray();
                place.Radius = Math.Max(MinimumRadius, distances[Math.Max(0, (int)Math.Ceiling(0.9 * distances.Length) - 1)]) + RadiusMargin;
                place.CentreFrom = "scenery members (" + place.Members.Count + ")";
            }
        }
        // 2. Station map icons: the text the icon shows, else its object's name, less " Station", " Station Icon" or " Depot".
        static void ReadStationMapIcons(IReadOnlyList<SceneObservation> entries, Func<SceneObservation, double[]> trackPoint) {
            foreach (var icon in entries.Where(e => e.Family == StationMapIconRules.Family)) {
                string text = null;
                try { if (icon.Handle is UnityEngine.GameObject go && go != null) text = go.GetComponentInChildren<TMPro.TMP_Text>(true)?.text; }
                catch (Exception e) when (!(e is OutOfMemoryException)) { WarnOnce("map icon text", "a station map icon's text could not be read, so its object name is used: " + e.GetBaseException().Message); }
                var hasText = !String.IsNullOrWhiteSpace(text);
                var at = Measure(icon, trackPoint);
                var spelling = (hasText ? "text '" + Whitespace.Replace(text, " ").Trim() + "' of " : "") + Last(icon.Identity.Path);
                var place = MergeOrCreatePlace(NormalizePlaceName(hasText ? text : Last(icon.Identity.Path), true), StationMapIconSource, spelling, at);
                if (place != null && at != null) place.Fallback[StationMapIconSource] = at;
            }
        }
        // 3. The map data: one list of the OpsController (areas or passenger stops), each item's name, identifier and
        // position. One unreadable item never drops the rest. Area.Position and TrackSpan.GetCenterPoint are map positions.
        static void ReadOpsPlaces(object ops, string listMember, string positionMember, string source) {
            IEnumerable items;
            try { items = Native.Read(ops, listMember) as IEnumerable; }
            catch (Exception e) when (!(e is OutOfMemoryException)) { placeNotes.Add(source + " list failed: " + e.GetBaseException().Message); return; }
            foreach (var item in items ?? new object[0]) {
                string rawName = null;
                try {
                    rawName = Native.Read(item, "name") as string;
                    var id = Native.Read(item, "identifier");
                    double[] at = null;
                    if (Native.Read(item, positionMember) is UnityEngine.Vector3 p && Finite(p.x, p.z)) at = new double[] { p.x, p.z };
                    var place = MergeOrCreatePlace(NormalizePlaceName(rawName, true), source, (rawName ?? "") + " (" + id + ")", at);
                    if (place != null && at != null) place.Fallback[source] = at;
                } catch (Exception e) when (!(e is OutOfMemoryException)) { placeNotes.Add(source + " " + (rawName ?? "?") + " failed: " + e.GetBaseException().Message); }
            }
        }
        // 4. Loc: objects in the loaded scenes.
        static void ReadLocMarkers(IReadOnlyList<SceneObservation> entries) {
            try { locMarkers = ScanLocMarkers(entries); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { placeNotes.Add("Loc scan failed: " + e.GetBaseException().Message); locMarkers = new List<LocMarker>(); }
            foreach (var marker in locMarkers) {
                var place = PlaceNamed(marker.Name, LocMarkerSource);
                if (place != null && marker.At != null) place.Fallback[LocMarkerSource] = marker.At;
            }
        }
        static void AssignFallbackCentres() {
            foreach (var place in places.Values.Where(p => p.Centre == null))
                foreach (var source in FallbackCentreSources)
                    if (place.Fallback.TryGetValue(source, out var at)) { place.Centre = at; place.Radius = MinimumRadius + RadiusMargin; place.CentreFrom = source; break; }
        }
        // Scene folders, station map icons and passenger stops are primary places. A Loc-only or ops-only place is a
        // sub-location of the nearest primary place whose radius holds its centre: it stays visible in the Place view
        // when unfiltered, but never takes positional objects or raises "(stands in X)". A name match alone, such as
        // Alarka in "Alarka Jct" 4.9 km away, makes no sub-location; each such case is noted for the log.
        static void SeparateSubLocations() {
            var primary = places.Values.Where(p => PrimarySources.Any(p.Sources.Contains)).ToList();
            foreach (var p in places.Values) p.IsPrimary = primary.Contains(p);
            foreach (var p in places.Values.Where(x => !x.IsPrimary)) {
                var holder = p.Centre == null ? null : primary.Where(c => c.Centre != null && Distance(p.Centre, c.Centre) <= c.Radius).OrderBy(c => Distance(p.Centre, c.Centre)).FirstOrDefault();
                var byName = primary.Where(c => ContainsWholeWord(p.Name, c.Name)).OrderByDescending(c => c.Name.Length).FirstOrDefault();
                if (holder != null) { p.SubOf = holder.Name; continue; }
                p.IsPrimary = true;
                if (byName != null) placeNotes.Add("name match only, kept as a place: " + p.Name + " names " + byName.Name
                    + (p.Centre == null || byName.Centre == null ? " (no position)" : " at " + Round(Distance(p.Centre, byName.Centre)) + " m, radius " + Round(byName.Radius)));
            }
            primaryPlaced = places.Values.Where(p => p.IsPrimary && p.Centre != null).ToArray();
        }
        static void FormatPlaceLines() {
            placesLine = String.Join(" | ", places.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p =>
                p.Name + (p.IsPrimary ? "" : " [sub of " + p.SubOf + "]") + " [" + String.Join("+", p.Sources) + "] {" + String.Join("; ", p.Spellings) + "} "
                + (p.Centre == null ? "no position" : "@" + Round(p.Centre[0]) + "," + Round(p.Centre[1]) + " r" + Round(p.Radius) + " from " + p.CentreFrom)
                + String.Concat(p.Fallback.Select(f => " " + f.Key + "@" + Round(f.Value[0]) + "," + Round(f.Value[1])))))
                + (placeNotes.Count == 0 ? "" : " NOTES: " + String.Join("; ", placeNotes));
            locLine = String.Join(" | ", locMarkers.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(m =>
                m.Name + " (" + m.Path + ") renderers=" + m.ShowingRenderers + " enabledNow=" + m.EnabledRenderers + " " + (m.Visible ? "visible board" : "invisible marker")
                + " listed=" + (m.Listed ? "yes" : "no") + (m.At == null ? "" : " @" + Round(m.At[0]) + "," + Round(m.At[1]))));
        }

        // The place of an object at a hierarchy path. An object in a real place folder keeps that folder's place, and is
        // marked when it stands outside that place's radius and inside another primary place's radius, nearer that
        // place. Any other object, such as one in a container object, gets the nearest primary place by its position.
        static PlaceInfo PlaceAt(string path, double[] at) {
            var info = new PlaceInfo();
            var folder = SceneryFolder(path);
            if (folder != null && !InSignContainerObject(path) && folderPlace.TryGetValue(folder, out var byFolder)) info.Named = byFolder;
            if (at != null && primaryPlaced.Length != 0) {
                Place nearest = null, inside = null; double nearestSq = Double.PositiveInfinity, insideSq = Double.PositiveInfinity;
                foreach (var p in primaryPlaced) {
                    var sq = DistanceSquared(at, p.Centre);
                    if (sq < nearestSq) { nearestSq = sq; nearest = p; }
                    if (info.Named != null && !p.Name.Equals(info.Named, StringComparison.OrdinalIgnoreCase) && sq <= p.Radius * p.Radius && sq < insideSq) { insideSq = sq; inside = p; }
                }
                if (nearest != null) { info.Near = nearest.Name; info.NearDistance = Math.Sqrt(nearestSq); }
                if (info.Named != null && places.TryGetValue(info.Named, out var named) && named.Centre != null) {
                    info.NamedDistance = Distance(at, named.Centre);
                    if (inside != null) { info.Inside = inside.Name; info.InsideDistance = Math.Sqrt(insideSq); }
                }
            }
            info.Positional = info.Named == null && info.Near != null;
            return info;
        }
        static PlaceInfo PlaceOf(SceneObservation e, Func<SceneObservation, double[]> trackPoint) {
            if (e == null) return new PlaceInfo();
            if (placeCache.TryGetValue(e, out var cached)) return cached;
            return placeCache[e] = PlaceAt(e.Identity.Path, Measure(e, trackPoint));
        }
        static bool IsConflict(PlaceInfo info) => info.Named != null && info.Inside != null && places.TryGetValue(info.Named, out var named) && named.Centre != null
            && info.NamedDistance > named.Radius && info.InsideDistance < info.NamedDistance;
    }
}
