// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // What the outliner reads from the loaded world: where an object shows, what stands on and below it, and the
    // places the game names beside the listed objects. The rules that use these facts are in the core.
    internal sealed class OutlinerScene : IOutlinerScene {
        static bool Finite(double x, double z) => !Double.IsNaN(x) && !Double.IsNaN(z) && !Double.IsInfinity(x) && !Double.IsInfinity(z);

        // The loaded scenes and the instance of the game's operations data.
        public string WorldKey {
            get {
                var key = new System.Text.StringBuilder();
                for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    key.Append(scene.handle).Append(scene.isLoaded ? '+' : '-').Append(',');
                }
                var ops = Ops(null);
                return key.Append(ops == null ? "no ops" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ops).ToString()).ToString();
            }
        }
        static object Ops(List<string> notes) {
            try { return Native.Read(Native.Type("Model.Ops.OpsController"), "Shared"); }
            catch (Exception e) when (e is InvalidOperationException || e is MissingMemberException || e is System.Reflection.TargetInvocationException) {
                notes?.Add("OpsController.Shared could not be read: " + e.GetBaseException().Message); return null;
            }
        }

        public ObjectFacts Facts(SceneObservation entry) {
            var facts = new ObjectFacts();
            var go = entry?.Handle as GameObject;
            if (go == null) return facts;
            facts.Own = go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName).Distinct().ToArray();
            facts.AtOrBelow = go.GetComponentsInChildren<Component>(true).Where(c => c != null).Select(c => c.GetType().FullName).Distinct().ToArray();
            facts.NamesBelow = go.GetComponentsInChildren<Transform>(true).Where(t => t != null && t.gameObject != go).Select(t => t.name).ToArray();
            return facts;
        }

        public double[] Bounds(SceneObservation entry) => Bounds(entry?.Handle as GameObject);
        // The centre of the presence-giving renderers below an object. A renderer that is off or inactive counts by its
        // mesh's bounds, unless it is statically batched, whose mesh spans the whole batch; then by its transform.
        static double[] Bounds(GameObject go) {
            if (go == null) return null;
            var bounds = new Bounds(); var any = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true)) {
                if (renderer == null || !BakedSceneryRules.GivesPresence(renderer.GetType().FullName, true, false, false)) continue;
                Bounds own;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.bounds.size != Vector3.zero) own = renderer.bounds;
                else {
                    var filter = renderer.GetComponent<MeshFilter>();
                    var mesh = filter == null ? null : filter.sharedMesh;
                    if (renderer.isPartOfStaticBatch || mesh == null || mesh.name.StartsWith("Combined Mesh", StringComparison.Ordinal)) own = new Bounds(renderer.transform.position, Vector3.zero);
                    else {
                        var local = mesh.bounds; var extents = local.extents;
                        own = new Bounds(renderer.transform.TransformPoint(local.center), Vector3.zero);
                        for (var i = 0; i < 8; i++)
                            own.Encapsulate(renderer.transform.TransformPoint(local.center + new Vector3((i & 1) == 0 ? -extents.x : extents.x, (i & 2) == 0 ? -extents.y : extents.y, (i & 4) == 0 ? -extents.z : extents.z)));
                    }
                }
                if (any) bounds.Encapsulate(own); else { bounds = own; any = true; }
            }
            var map = NativeInventory.MapPosition(any ? bounds.center : go.transform.position, NativeInventory.MovesWithOrigin(go.transform));
            return !Finite(map.x, map.z) ? null : new double[] { map.x, map.z };
        }

        public IEnumerable<PlaceSource> PlaceSources(IReadOnlyList<SceneObservation> entries, List<string> notes) {
            var sources = new List<PlaceSource>();
            // The game's map data: the areas and passenger stops of the operations data.
            var ops = Ops(notes);
            if (ops == null) notes.Add("the game's operations data is not loaded");
            else { ReadOps(ops, "Areas", "Position", sources, notes); ReadOps(ops, "AllPassengerStops", "CenterPoint", sources, notes); }
            try { ReadUnlisted(entries, sources); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { notes.Add("the scenes could not be read: " + e.GetBaseException().Message); }
            return sources;
        }
        // One list of the operations data (areas or passenger stops): each item's name and position. One unreadable item
        // never drops the rest. Area.Position and TrackSpan.GetCenterPoint are map positions.
        static void ReadOps(object ops, string listMember, string positionMember, List<PlaceSource> sources, List<string> notes) {
            IEnumerable items;
            try { items = Native.Read(ops, listMember) as IEnumerable; }
            catch (Exception e) when (!(e is OutOfMemoryException)) { notes.Add(listMember + " could not be read: " + e.GetBaseException().Message); return; }
            foreach (var item in items ?? new object[0]) {
                string name = null;
                try {
                    name = Native.Read(item, "name") as string;
                    double[] at = null;
                    if (Native.Read(item, positionMember) is Vector3 p && Finite(p.x, p.z)) at = new double[] { p.x, p.z };
                    if (!String.IsNullOrWhiteSpace(name)) sources.Add(new PlaceSource(PlaceSourceKind.MapData, name.Trim(), at));
                } catch (Exception e) when (!(e is OutOfMemoryException)) { notes.Add(listMember + " " + (name ?? "?") + " could not be read: " + e.GetBaseException().Message); }
            }
        }
        // Every scene object outside kept-out roots and runtime objects that names a place and that the inventory does
        // not list: the folders of the map labels, and Loc: objects.
        static void ReadUnlisted(IReadOnlyList<SceneObservation> entries, List<PlaceSource> sources) {
            var listed = new HashSet<string>(entries.Select(e => e.Identity.Path), StringComparer.Ordinal);
            for (var s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++) {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) {
                    if (root == null || !BakedSceneryRules.ListedBelow(root.name, false)) continue;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
                        if (t == null || (t.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                        var label = t.parent != null && t.parent.name == BakedSceneryRules.MapLabels && t.name != BakedSceneryRules.StationIcons;
                        if (!label && !t.name.StartsWith(SceneClassification.LocPrefix, StringComparison.Ordinal)) continue;
                        var path = BoardScene.PathOf(t);
                        if (!listed.Contains(path)) sources.AddRange(ScenePlaces.SourcesOf(path, Bounds(t.gameObject)));
                    }
                }
            }
        }
    }
}
