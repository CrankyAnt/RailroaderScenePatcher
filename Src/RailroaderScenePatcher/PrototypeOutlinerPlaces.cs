// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: throwaway adapter of the outliner grouping prototype: where objects stand, what the game's scene says
// about them, and the places the game names. The rules themselves are in the core (ScenePlaces, SceneClassification).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    internal static partial class PrototypeOutlinerGrouping {
        // What the scene says about one object: the components on it, those at or below it, and the names below it.
        sealed class Facts { internal string[] Own = new string[0], AtOrBelow = new string[0], NamesBelow = new string[0]; }

        // Per world load: the scene signature and the OpsController instance the places were read from.
        static string cachedSceneKey;
        static object cachedOps;
        static bool placesBuiltWithEntries;
        static IReadOnlyList<SceneObservation> preparedEntries;
        static ScenePlaces places;
        static List<string> placeNotes;
        static string placesLine;
        // Per observation: an edit replaces the observation, so these never hold a verdict older than the object.
        static Dictionary<SceneObservation, ScenePlace> placeCache;
        static Dictionary<SceneObservation, double[]> measured;
        static Dictionary<SceneObservation, SceneClass> classCache;
        static Dictionary<SceneObservation, string> searchTags;

        internal static void InvalidatePlaces() {
            cachedSceneKey = null; cachedOps = null; placesBuiltWithEntries = false; preparedEntries = null;
            places = null; placeCache = null; measured = null; classCache = null; searchTags = null;
        }

        static bool HasPosition(SceneObservation e) => e.Position != null && e.Position.Count >= 3;
        static bool Finite(double x, double z) => !Double.IsNaN(x) && !Double.IsNaN(z) && !Double.IsInfinity(x) && !Double.IsInfinity(z);

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

        // ---- what the scene says about an object ----
        static Facts FactsOf(SceneObservation e) {
            try { return ReadFacts(e.Handle as UnityEngine.GameObject); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { WarnOnce("facts", "an object's components could not be read, so it is classified by its name and family: " + ex.GetBaseException().Message); return new Facts(); }
        }
        static Facts ReadFacts(UnityEngine.GameObject go) {
            var facts = new Facts();
            if (go == null) return facts;
            facts.Own = go.GetComponents<UnityEngine.Component>().Where(c => c != null).Select(c => c.GetType().FullName).Distinct().ToArray();
            facts.AtOrBelow = go.GetComponentsInChildren<UnityEngine.Component>(true).Where(c => c != null).Select(c => c.GetType().FullName).Distinct().ToArray();
            facts.NamesBelow = go.GetComponentsInChildren<UnityEngine.Transform>(true).Where(t => t != null && t.gameObject != go).Select(t => t.name).ToArray();
            return facts;
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

        // ---- places ----
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
            placeCache = new Dictionary<SceneObservation, ScenePlace>(); measured = new Dictionary<SceneObservation, double[]>();
            classCache = new Dictionary<SceneObservation, SceneClass>(); searchTags = new Dictionary<SceneObservation, string>();
            placeNotes = new List<string>();
            var sources = new List<PlaceSource>();
            // 1. The game's map data: the areas and passenger stops of the operations data.
            if (ops == null) placeNotes.Add("OpsController.Shared is null");
            else { ReadOpsPlaces(ops, "Areas", "Position", sources); ReadOpsPlaces(ops, "AllPassengerStops", "CenterPoint", sources); }
            // 2. What the listed objects name by their path and name: place folders, Roads folders, station map icons, Loc: boards.
            foreach (var e in entries.Where(e => e.GroupPath == null && e.HolderPath == null)) sources.AddRange(ScenePlaces.SourcesOf(e.Identity.Path, Measure(e, trackPoint)));
            // 3. What the scene names without a listed object: map labels, and Loc: objects the inventory does not list.
            try { ReadUnlistedSources(entries, sources); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { placeNotes.Add("scene scan failed: " + e.GetBaseException().Message); }
            places = ScenePlaces.From(sources);
            placesLine = String.Join(" | ", places.All.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p =>
                p.Name + " [" + p.From + "] {" + String.Join("; ", p.Names) + "} " + String.Join(" ", p.Points.Select(at => "@" + Round(at[0]) + "," + Round(at[1])))))
                + (placeNotes.Count == 0 ? "" : " NOTES: " + String.Join("; ", placeNotes));
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
        // One list of the OpsController (areas or passenger stops): each item's name and position. One unreadable item
        // never drops the rest. Area.Position and TrackSpan.GetCenterPoint are map positions.
        static void ReadOpsPlaces(object ops, string listMember, string positionMember, List<PlaceSource> sources) {
            IEnumerable items;
            try { items = Native.Read(ops, listMember) as IEnumerable; }
            catch (Exception e) when (!(e is OutOfMemoryException)) { placeNotes.Add(listMember + " list failed: " + e.GetBaseException().Message); return; }
            foreach (var item in items ?? new object[0]) {
                string name = null;
                try {
                    name = Native.Read(item, "name") as string;
                    double[] at = null;
                    if (Native.Read(item, positionMember) is UnityEngine.Vector3 p && Finite(p.x, p.z)) at = new double[] { p.x, p.z };
                    if (!String.IsNullOrWhiteSpace(name)) sources.Add(new PlaceSource(PlaceSourceKind.MapData, name.Trim(), at));
                } catch (Exception e) when (!(e is OutOfMemoryException)) { placeNotes.Add(listMember + " " + (name ?? "?") + " failed: " + e.GetBaseException().Message); }
            }
        }
        // Every scene object outside kept-out roots and runtime objects that names a place and that the inventory does
        // not list: the folders of the map labels, and Loc: objects.
        static void ReadUnlistedSources(IReadOnlyList<SceneObservation> entries, List<PlaceSource> sources) {
            var listed = new HashSet<string>(entries.Select(e => e.Identity.Path), StringComparer.Ordinal);
            for (var s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++) {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) {
                    if (root == null || !BakedSceneryRules.ListedBelow(root.name, false)) continue;
                    foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true)) {
                        if (t == null || (t.gameObject.hideFlags & UnityEngine.HideFlags.DontSave) != 0) continue;
                        var label = t.parent != null && t.parent.name == BakedSceneryRules.MapLabels && t.name != BakedSceneryRules.StationIcons;
                        if (!label && !t.name.StartsWith(SceneClassification.LocPrefix, StringComparison.Ordinal)) continue;
                        var path = BoardScene.PathOf(t);
                        if (!listed.Contains(path)) sources.AddRange(ScenePlaces.SourcesOf(path, MeasureBounds(t.gameObject)));
                    }
                }
            }
        }

        static ScenePlace PlaceOf(SceneObservation e, Func<SceneObservation, double[]> trackPoint) {
            if (e == null) return null;
            if (placeCache.TryGetValue(e, out var cached)) return cached;
            return placeCache[e] = places.Of(e.Identity.Path, Measure(e, trackPoint));
        }
    }
}
