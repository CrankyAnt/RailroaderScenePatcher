// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // Builds mod objects in the loaded scene (IModObjectScene). A mod object is a runtime copy of its source that stands
    // beside the source, in the same parent, under the name its modification's target path gives it. The inventory and
    // the capability of its family then find it by that path like any object of the map. The scene asset is never
    // changed: the copy lives until its scene unloads or its modification leaves, and each load builds it again.
    internal sealed class ModObjectWorld : IModObjectScene {
        // The mod objects that stand now. Unity reports a destroyed object as null, so an unloaded world leaves none.
        static readonly HashSet<GameObject> standing = new HashSet<GameObject>();
        // Whether this patcher built the object as a mod object.
        internal static bool IsModObject(GameObject go) => go != null && standing.Contains(go);
        // A copy is made below an inactive holder, so none of its components starts before the copy has its name and
        // stands at its placement.
        static GameObject holder;
        static Transform Holder() {
            if (holder == null) {
                holder = new GameObject("CrankyAnt Scene Patcher mod object build") { hideFlags = HideFlags.HideAndDontSave };
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
            }
            return holder.transform;
        }
        static float Checked(double value) => Math.Abs(value) <= Single.MaxValue && !Double.IsNaN(value) ? (float)value : throw new InvalidOperationException("A coordinate exceeds the game's numeric precision.");

        public object Build(TargetIdentity identity, object source, Placement placement) {
            var from = source as GameObject;
            if (from == null) throw new InvalidOperationException("The source of this mod object is not a scene object.");
            if (placement?.Position == null || placement.Rotation == null || placement.Position.Length != 3 || placement.Rotation.Length != 4) throw new InvalidOperationException(ModObjects.NeedsPlacement);
            if (BoardScene.LoadedAt(identity.Scene, identity.Path).Count != 0) throw new InvalidOperationException(ModObjects.Taken);
            // The mod object stands in its source's parent: its target path is that parent's path and its own name.
            var parent = from.transform.parent;
            var above = parent == null ? null : BoardScene.PathOf(parent);
            if (from.scene.name != identity.Scene || (above != null && !identity.Path.StartsWith(above + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("The target of this mod object doesn't stand beside its source. " + ModObjects.TargetShape);
            var name = above == null ? identity.Path : identity.Path.Substring(above.Length + 1);
            var map = new Vector3(Checked(placement.Position[0]), Checked(placement.Position[1]), Checked(placement.Position[2]));
            var rotation = new Quaternion(Checked(placement.Rotation[0]), Checked(placement.Rotation[1]), Checked(placement.Rotation[2]), Checked(placement.Rotation[3]));
            var scene = NativeInventory.ScenePosition(map, NativeInventory.MovesWithOrigin(from.transform));
            GameObject made = null;
            try {
                made = UnityEngine.Object.Instantiate(from, Holder(), true);
                made.name = name;
                made.transform.SetPositionAndRotation(scene, rotation);
                // No switch of the game names a mod object, so nothing switches it: it starts enabled, whatever
                // progression or an explicit disable made of its source.
                if (!made.activeSelf) made.SetActive(true);
                Prepare(from, made);
                // Entering the source's parent starts its components, at its placement.
                made.transform.SetParent(parent, true);
                if (parent == null) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(made, from.scene);
                Physics.SyncTransforms();
                if (BoardScene.PathOf(made.transform) != identity.Path) throw new InvalidOperationException("The mod object did not get its target path.");
                standing.RemoveWhere(go => go == null);
                standing.Add(made);
                // The rules read the hierarchy again, so the mod object gets its verdict and its key.
                GroupWorld.Invalidate();
                return made;
            } catch {
                if (made != null) { made.SetActive(false); UnityEngine.Object.Destroy(made); }
                throw;
            }
        }

        const string PropInstanceType = "Helpers.PropInstance", NightLightType = "Effects.NightLightController", OriginTargetType = "Helpers.WorldTransformerTarget";
        static FieldInfo Field(Component component, string name) => component.GetType().GetField(name, Native.Flags);
        // Brings the copy, still inactive below the holder, to the state a scene object has before the game starts it.
        // A copy takes what the game made of its source while it ran: renderers and objects a culler hid because the
        // camera is far from the source, a light the night light controller dimmed, and the model a prop instance
        // loaded. The copy's own components start from that state, so what the game hid at the source would stay
        // hidden at the copy. Each step is independent: one that fails is logged and the others still run.
        static void Prepare(GameObject from, GameObject made) {
            var sources = from.GetComponentsInChildren<Component>(true);
            var copies = made.GetComponentsInChildren<Component>(true);
            // The copy holds the source's components in the same order, so a component of the source has its twin.
            var paired = sources.Length == copies.Length;
            for (var i = 0; paired && i < sources.Length; i++) paired = (sources[i] == null) == (copies[i] == null) && (sources[i] == null || sources[i].GetType() == copies[i].GetType());
            Component Twin(Component source) { var at = paired && source != null ? Array.IndexOf(sources, source) : -1; return at < 0 ? null : copies[at]; }
            void Step(string what, Action run) {
                try { run(); } catch (Exception e) { PatcherLog.Debug("A mod object's copy could not be prepared (" + what + "): " + e.GetBaseException().Message); }
            }
            string TypeOf(Component component) => component == null ? null : component.GetType().FullName;
            // A renderer culler keeps only the renderers that are enabled when it starts: the ones its source culler
            // switched off are switched on again in the copy.
            Step("renderer cullers", () => {
                for (var i = 0; i < copies.Length; i++) {
                    if (TypeOf(copies[i]) != CullerTypes.RendererCuller) continue;
                    var held = paired ? Field(sources[i], "_cullRenderers")?.GetValue(sources[i]) as Renderer[] : null;
                    if (held != null) { foreach (var renderer in held) if (Twin(renderer) is Renderer twin) twin.enabled = true; }
                    else if (!paired) foreach (var renderer in copies[i].GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                }
            });
            // A game-object culler switches the objects on its list, and a simple culler its own object: each is on
            // until its culler in the copy decides.
            Step("object cullers", () => {
                foreach (var culler in copies) {
                    if (TypeOf(culler) == CullerTypes.GameObjectCuller && Field(culler, "gameObjects")?.GetValue(culler) is System.Collections.IEnumerable listed)
                        foreach (var item in listed) if (item is GameObject switched && switched != null && switched.transform.IsChildOf(made.transform)) switched.SetActive(true);
                    if (TypeOf(culler) == CullerTypes.SimpleCuller) culler.gameObject.SetActive(true);
                }
            });
            // The night light controller takes the light's intensity at its start as the full intensity.
            Step("night lights", () => {
                for (var i = 0; paired && i < copies.Length; i++) {
                    if (TypeOf(copies[i]) != NightLightType) continue;
                    if (Field(copies[i], "light")?.GetValue(copies[i]) is Light light && light != null && Field(sources[i], "_lightIntensityMultiplier")?.GetValue(sources[i]) is float full) light.intensity = full;
                }
            });
            // The copy moves with the origin through its parent; a registration of its own would shift it twice.
            Step("origin targets", () => { foreach (var target in copies) if (TypeOf(target) == OriginTargetType) UnityEngine.Object.DestroyImmediate(target); });
            // A prop instance loads its model itself: the copied model goes, and the copy keeps the source's styles.
            Step("prop instances", () => {
                for (var i = 0; i < copies.Length; i++) {
                    if (TypeOf(copies[i]) != PropInstanceType) continue;
                    var styles = copies[i].GetType().GetProperty("PlacementStyles", Native.Flags);
                    if (paired && styles != null && styles.CanWrite) styles.SetValue(copies[i], styles.GetValue(sources[i]));
                    var prop = copies[i].transform;
                    for (var child = prop.childCount - 1; child >= 0; child--) UnityEngine.Object.DestroyImmediate(prop.GetChild(child).gameObject);
                }
            });
        }

        public void Remove(object modObject) {
            var made = modObject as GameObject;
            standing.Remove(made);
            standing.RemoveWhere(go => go == null);
            if (made == null) return;
            // Disabling first lets its components leave what they registered with, such as the map and the cullers;
            // the game destroys the object at the end of the frame.
            // Until then it stands under no path of the map, so its target path is free again at once.
            made.SetActive(false);
            made.name = "CrankyAnt Scene Patcher removed mod object";
            made.transform.SetParent(Holder(), true);
            UnityEngine.Object.Destroy(made);
            GroupWorld.Invalidate();
        }
    }
}
