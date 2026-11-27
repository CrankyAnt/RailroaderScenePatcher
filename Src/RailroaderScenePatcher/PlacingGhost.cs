// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The ghost of a mod object while it is being placed: a visual stand-in of its source that stands on the pointer.
    // It holds only the source's meshes with their own materials, at the source's own arrangement: no component of the
    // source, no collider and no script, so the game neither finds nor runs it, and no raycast, such as the ground
    // raycast under the pointer, hits it. It belongs to no scene file and is gone when placing ends.
    internal sealed class PlacingGhost {
        GameObject root;
        GameObject source;
        // The model references the ghost loaded for the source's prop instances; released when the ghost goes.
        readonly List<IDisposable> loaded = new List<IDisposable>();

        // Shows the ghost of the source at the map point, with the rotation the placed object would get.
        internal void Show(GameObject from, double[] map, Quaternion rotation) {
            if (from == null || map == null || map.Length != 3) { Hide(); return; }
            if (root == null || !ReferenceEquals(source, from)) Build(from);
            if (root == null) return;
            var scene = NativeInventory.ScenePosition(new Vector3((float)map[0], (float)map[1], (float)map[2]), NativeInventory.MovesWithOrigin(from.transform));
            root.transform.SetPositionAndRotation(scene, rotation);
            if (!root.activeSelf) root.SetActive(true);
        }
        // Hides the ghost and keeps it for the next frame on the pointer.
        internal void Hide() { if (root != null && root.activeSelf) root.SetActive(false); }
        // Removes the ghost.
        internal void Clear() {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null; source = null;
            foreach (var reference in loaded) try { reference.Dispose(); } catch { }
            loaded.Clear();
        }

        // The ghost holds one plain mesh renderer for each mesh the source shows, placed relative to the source's own
        // position and rotation. What the source's cullers switched off, such as the masts and gates of a crossing out
        // of view, counts as shown, as in the placed object. Of a level-of-detail group only the nearest level is
        // taken, so the levels do not overlap. A prop instance, such as a station building, holds its model only while
        // the game shows it near the camera (Railroader 0.6.3, Helpers.PropInstance.CullingSphereStateChanged); for one
        // without its model the ghost loads that model the way the game does and adds its meshes when the load
        // completes.
        void Build(GameObject from) {
            Clear();
            source = from;
            root = new GameObject("CrankyAnt Scene Patcher placing ghost") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var frame = Matrix4x4.TRS(from.transform.position, from.transform.rotation, Vector3.one).inverse;
            // What the placed object will not hold, such as the whistle signs of a crossing, is no part of the ghost.
            var left = ModObjectWorld.NotHeld(from).Where(go => go != null).Select(go => go.transform).ToArray();
            bool Kept(Transform t) => !left.Any(gone => t.IsChildOf(gone));
            var culled = Culled(from);
            var count = Add(from.transform, renderer => Kept(renderer.transform) && ShownWhenPlaced(renderer.transform, from.transform, culled), renderer => frame * renderer.transform.localToWorldMatrix);
            var loads = 0;
            foreach (var prop in Props(from).Where(p => Kept(p.transform) && Active(p, from.transform, culled) && Unloaded(p)))
                if (Load(prop, frame)) loads++;
            if (count == 0 && loads == 0) PatcherLog.Debug("The placing ghost of " + from.name + " has no mesh to show; the pointer mark stands alone.");
        }
        // Adds a ghost part for each mesh renderer at and below model that keep accepts, except the farther levels of a
        // level-of-detail group, placed by place relative to the source's frame. Returns how many parts it added.
        int Add(Transform model, Func<MeshRenderer, bool> keep, Func<MeshRenderer, Matrix4x4> place) {
            var farther = new HashSet<Renderer>();
            foreach (var group in model.GetComponentsInChildren<LODGroup>(true))
                foreach (var level in group.GetLODs().Skip(1))
                    foreach (var renderer in level.renderers ?? new Renderer[0]) if (renderer != null) farther.Add(renderer);
            var count = 0;
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true)) {
                if (renderer == null || farther.Contains(renderer) || !keep(renderer)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter == null ? null : filter.sharedMesh;
                if (mesh == null) continue;
                var relative = place(renderer);
                var part = new GameObject(renderer.name) { hideFlags = HideFlags.HideAndDontSave };
                // The source's own layer, so the camera draws the ghost as it draws the source. Without a collider no
                // raycast hits it.
                part.layer = renderer.gameObject.layer;
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = relative.GetColumn(3);
                part.transform.localRotation = relative.rotation;
                part.transform.localScale = relative.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var shown = part.AddComponent<MeshRenderer>();
                shown.sharedMaterials = renderer.sharedMaterials;
                shown.shadowCastingMode = renderer.shadowCastingMode;
                shown.receiveShadows = renderer.receiveShadows;
                count++;
            }
            return count;
        }

        const string PropInstanceType = "Helpers.PropInstance", PropManagerType = "Helpers.PropManager";
        static IEnumerable<Component> Props(GameObject from) {
            var type = Native.Type(PropInstanceType);
            return type == null ? Enumerable.Empty<Component>() : from.GetComponentsInChildren(type, true).Where(c => c != null);
        }
        // Whether the prop instance is active below the source, as the placed object will hold it: the component is
        // enabled and the placed object shows its object. A prop that the game or a patch switched off loads no model
        // there, so the ghost loads none either.
        static bool Active(Component prop, Transform source, ICollection<Transform> culled) => !(prop is Behaviour behaviour && !behaviour.enabled) && ShownWhenPlaced(prop.transform, source, culled);
        // Whether the placed object shows t (PlacingGhostRules.ShownWhenPlaced): every object from t up to the source
        // is active or switched by one of the source's cullers. The cullers of a crossing controller switch its masts
        // and gates off while the source is out of view, which is where it mostly stands while its copy is placed. A
        // renderer the game switched off by itself, such as a loaded prop's renderer away from the camera, still
        // counts: the placed object shows it where it stands, and the ghost stands for that.
        static bool ShownWhenPlaced(Transform t, Transform source, ICollection<Transform> culled) =>
            PlacingGhostRules.ShownWhenPlaced(t, source, at => at.parent, at => at.gameObject.activeSelf, culled);
        // The objects at and below the source that its cullers switch, which the copy switches on until its own cullers
        // decide (ModObjectWorld): the objects on a game-object culler's list and a simple culler's own object.
        static HashSet<Transform> Culled(GameObject from) {
            var culled = new HashSet<Transform>();
            var switcher = Native.Type(CullerTypes.GameObjectCuller);
            if (switcher != null) foreach (var culler in from.GetComponentsInChildren(switcher, true)) {
                if (culler == null || !(switcher.GetField("gameObjects", Native.Flags)?.GetValue(culler) is System.Collections.IEnumerable listed)) continue;
                foreach (var item in listed) if (item is GameObject go && go != null && go.transform.IsChildOf(from.transform)) culled.Add(go.transform);
            }
            var simple = Native.Type(CullerTypes.SimpleCuller);
            if (simple != null) foreach (var culler in from.GetComponentsInChildren(simple, true)) if (culler != null) culled.Add(culler.transform);
            return culled;
        }
        // Whether every object from t up to model is active: the loaded model of a prop shows such an object.
        static bool ActiveBelow(Transform t, Transform model) {
            for (; t != null && t != model; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }
        // Whether the prop instance holds no model now. A prop whose state can't be read counts as loaded: the ghost
        // then shows what stands below it, as before.
        static bool Unloaded(Component prop) {
            try { return Native.Read(prop, "_model") as GameObject == null; }
            catch (Exception e) { PatcherLog.Debug("The placing ghost can't read the model of " + prop.name + ": " + e.GetBaseException().Message); return false; }
        }
        // Whether a renderer of a loaded model shows: the game takes only the enabled renderers of the model it
        // instantiates (PropInstance.DidLoadModel), and an inactive object below the model root shows nothing.
        static bool Shows(MeshRenderer renderer, Transform model) => renderer.enabled && ActiveBelow(renderer.transform, model);
        // Starts loading the model of a prop instance through the game's own prop load (PropManager.LoadProp with the
        // definition's ModelIdentifier, as PropInstance.SetLoaded does). The loaded prefab is never instantiated: the ghost
        // copies its meshes as it copies the source's. Returns whether the load started.
        bool Load(Component prop, Matrix4x4 frame) {
            try {
                var identifier = Native.Read(prop, "identifier") as string;
                if (String.IsNullOrEmpty(identifier)) return false;
                var manager = Native.Read(Native.Type(PropManagerType) ?? throw new InvalidOperationException("The prop manager is unavailable"), "Shared");
                var definition = Native.Read(prop, "_definition");
                if (definition == null) {
                    var args = new object[] { identifier, prop, null };
                    if (!(bool)Native.Call(manager, "TryGetPropDefinition", args) || args[2] == null) return false;
                    definition = args[2];
                }
                if (SynchronizationContext.Current == null) throw new InvalidOperationException("The model must be loaded on the game's main thread");
                var task = (Task)Native.Call(manager, "LoadProp", Native.Read(definition, "ModelIdentifier"), identifier);
                var ghost = root;
                // The prop's place relative to the source does not change while the ghost exists: read it now.
                var at = frame * prop.transform.localToWorldMatrix;
                var name = prop.name;
                task.ContinueWith(t => Loaded(t, ghost, at, name), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
                return true;
            } catch (Exception e) {
                PatcherLog.Debug("The placing ghost can't load the model of " + prop.name + ": " + e.GetBaseException().Message);
                return false;
            }
        }
        // On the main thread after the load ends: the ghost it was started for, if it still stands, takes the model's
        // meshes. The game instantiates the model at the prop's position and rotation below it with the model's own
        // scale, which the prop's frame times the model root's scale gives. A late load releases its reference at once.
        void Loaded(Task task, GameObject ghost, Matrix4x4 at, string name) {
            var result = task.Status == TaskStatus.RanToCompletion ? Native.Read(task, "Result") as IDisposable : null;
            if (result == null) { PatcherLog.Debug("The placing ghost can't load the model of " + name + ": " + (task.Exception?.GetBaseException().Message ?? "no model was returned")); return; }
            if (ghost == null || !ReferenceEquals(ghost, root)) { try { result.Dispose(); } catch { } return; }
            loaded.Add(result);
            try {
                var model = Native.Read(result, "Asset") as GameObject;
                if (model == null) throw new InvalidOperationException("the loaded model is missing");
                var instance = at * Matrix4x4.Scale(model.transform.localScale) * model.transform.worldToLocalMatrix;
                var count = Add(model.transform, renderer => Shows(renderer, model.transform), renderer => instance * renderer.transform.localToWorldMatrix);
                if (count == 0) PatcherLog.Debug("The model of " + name + " has no mesh for the placing ghost.");
            } catch (Exception e) {
                PatcherLog.Debug("The placing ghost can't show the model of " + name + ": " + e.GetBaseException().Message);
            }
        }
    }
}
