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
    // beside its source, in the source's parent, under the name its modification's target gives it: beside its original,
    // or beside the identical instance that stands in for a missing original. The inventory and the capability of its
    // family then find it at that path like any object of the map. The scene asset is never
    // changed: the copy lives until its scene unloads or its modification leaves, and each load builds it again.
    internal sealed class ModObjectWorld : IModObjectScene, IModObjectSources, IModObjectNames {
        // The mod objects that stand now. Unity reports a destroyed object as null, so an unloaded world leaves none.
        static readonly HashSet<GameObject> standing = new HashSet<GameObject>();
        // Whether this patcher built the object as a mod object.
        internal static bool IsModObject(GameObject go) => go != null && standing.Contains(go);
        // The mod objects with an object name of their own, with the name their target path gives them.
        static readonly Dictionary<GameObject, string> pathNames = new Dictionary<GameObject, string>();
        // The name of the object in a hierarchy path: for a mod object with an object name of its own, the name its target
        // path gives it; for any other object its name.
        internal static string PathName(Transform t) => pathNames.Count != 0 && pathNames.TryGetValue(t.gameObject, out var name) ? name : t.name;
        // Gives a mod object this patcher built its object name in the game, or with null its path name again. The game's
        // teleport command and quick search read the object name; the patcher keeps finding the object by its path.
        public void Name(object modObject, string name) {
            var made = modObject as GameObject;
            if (made == null || !standing.Contains(made)) throw new InvalidOperationException("This object is not a mod object of this patcher.");
            pathNames.Keys.Where(go => go == null).ToList().ForEach(go => pathNames.Remove(go));
            if (!pathNames.TryGetValue(made, out var pathName)) pathName = made.name;
            if (name == null || name == pathName) { pathNames.Remove(made); made.name = pathName; }
            else { pathNames[made] = pathName; made.name = name; }
            GroupWorld.Invalidate();
        }
        public string NameOf(object modObject) => (modObject as GameObject)?.name;
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

        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) {
            var from = source as GameObject;
            if (from == null) throw new InvalidOperationException("The source of this mod object is not a scene object.");
            if (placement?.Position == null || placement.Rotation == null || placement.Position.Length != 3 || placement.Rotation.Length != 4) throw new InvalidOperationException(ModObjects.NeedsPlacement);
            if (BoardScene.LoadedAt(identity.Scene, identity.Path).Count != 0) throw new InvalidOperationException(ModObjects.Taken);
            // The mod object stands in the parent its path names, which the engine gives: its source's parent, or for a
            // part's copy the parent of the part's group, an object above the source, also when the source is an
            // identical instance in another parent than the original's. Its path is that parent's path and its own name.
            var within = ModObjects.ParentOf(identity.Path);
            var parent = from.transform.parent;
            while (parent != null && BoardScene.PathOf(parent) != within) parent = parent.parent;
            var above = parent == null ? null : BoardScene.PathOf(parent);
            if (from.scene.name != identity.Scene || above != within)
                throw new InvalidOperationException("The target of this mod object doesn't stand beside its source. " + ModObjects.TargetShape);
            var name = above == null ? identity.Path : identity.Path.Substring(above.Length + 1);
            var map = new Vector3(Checked(placement.Position[0]), Checked(placement.Position[1]), Checked(placement.Position[2]));
            var rotation = new Quaternion(Checked(placement.Rotation[0]), Checked(placement.Rotation[1]), Checked(placement.Rotation[2]), Checked(placement.Rotation[3]));
            // Whether it moves with the world origin follows the parent it stands in; a root object follows its source.
            var scene = NativeInventory.ScenePosition(map, NativeInventory.MovesWithOrigin(parent != null ? parent : from.transform));
            GameObject made = null;
            try {
                made = UnityEngine.Object.Instantiate(from, Holder(), true);
                made.name = name;
                made.transform.SetPositionAndRotation(scene, rotation);
                // No switch of the game names a mod object, so nothing switches it: it starts enabled, whatever
                // progression or an explicit disable made of its source.
                if (!made.activeSelf) made.SetActive(true);
                Prepare(from, made);
                // A scene marker takes its own marker ID before it starts: under its source's ID its start would replace
                // the source's registration in the track graph, which holds one entry per ID and segment.
                if (markerId != null) {
                    var marker = made.GetComponent(Native.Type(TrackMarkerWorld.MarkerType)) ?? throw new InvalidOperationException("The source of this mod object holds no track marker for its marker ID.");
                    (marker.GetType().GetField("id", Native.Flags) ?? throw new MissingFieldException(TrackMarkerWorld.MarkerType, "id")).SetValue(marker, markerId);
                }
                // Entering its parent starts its components, at its placement.
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


        // The model and build of an object: its components and the components of its parts in hierarchy order, with the
        // meshes and materials they show, by asset name. Names of objects are left out: identical objects carry the
        // names of their places. Below a prop instance nothing is read: the game loads that model itself, named by the
        // prop instance's identifier. The signature starts with its version and the number of objects it read, so
        // Identical passes over every object of another size without reading it.
        public string BuildOf(object source) {
            var from = source as GameObject;
            if (from == null) return null;
            try {
                var text = new System.Text.StringBuilder();
                var count = Describe(from.transform, 0, text);
                using (var sha = System.Security.Cryptography.SHA256.Create()) {
                    var digest = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
                    return BuildVersion + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + BitConverter.ToString(digest, 0, 16).Replace("-", "").ToLowerInvariant();
                }
            } catch (Exception e) {
                PatcherLog.Debug("The model and build of a mod object's source could not be read: " + e.GetBaseException().Message);
                return null;
            }
        }
        const string BuildVersion = "v1:";
        static int Describe(Transform t, int depth, System.Text.StringBuilder text) {
            text.Append(depth).Append('|');
            var prop = false;
            foreach (var component in t.GetComponents<Component>()) {
                if (component == null) { text.Append("?;"); continue; }
                var type = component.GetType().FullName;
                text.Append(type);
                if (component is MeshFilter filter) text.Append('=').Append(AssetName(filter.sharedMesh));
                else if (component is SkinnedMeshRenderer skinned) text.Append('=').Append(AssetName(skinned.sharedMesh));
                if (component is Renderer renderer) { text.Append('='); foreach (var material in renderer.sharedMaterials) text.Append(AssetName(material)).Append(','); }
                if (type == PropInstanceType) { prop = true; text.Append('=').Append(Field(component, "identifier")?.GetValue(component) as string); }
                text.Append(';');
            }
            text.Append('\n');
            var count = 1;
            if (!prop) for (var i = 0; i < t.childCount; i++) count += Describe(t.GetChild(i), depth + 1, text);
            return count;
        }
        // The name of a mesh or material asset. A mesh the game makes while it runs, such as the mesh of a text, has none
        // that stays the same; a material the game copies keeps its asset's name with " (Instance)".
        static string AssetName(UnityEngine.Object asset) {
            if (asset == null) return "";
            if (asset is Mesh && (asset.hideFlags & HideFlags.DontSave) != 0) return "runtime";
            var name = asset.name ?? "";
            const string Instance = " (Instance)";
            while (name.EndsWith(Instance, StringComparison.Ordinal)) name = name.Substring(0, name.Length - Instance.Length);
            return name;
        }
        // The number of objects Describe reads below and at the object, without building the description.
        static int Size(Transform t, Dictionary<Transform, int> sizes) {
            var count = 1;
            var prop = false;
            foreach (var component in t.GetComponents<Component>()) if (component != null && component.GetType().FullName == PropInstanceType) { prop = true; break; }
            for (var i = 0; i < t.childCount; i++) { var below = Size(t.GetChild(i), sizes); if (!prop) count += below; }
            sizes[t] = count;
            return count;
        }
        // The loaded objects of the source's scene with its component and the build, in the fixed order: by hierarchy
        // path in ordinal order, then by sibling index. Mod objects and what stands below them are not listed.
        public IReadOnlyList<SourceInstance> Identical(TargetIdentity source, string build) {
            var found = new List<KeyValuePair<Transform, string>>();
            if (source == null || build == null || !build.StartsWith(BuildVersion, StringComparison.Ordinal)) return Array.Empty<SourceInstance>();
            var sizeText = build.Substring(BuildVersion.Length).Split(':')[0];
            if (!Int32.TryParse(sizeText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size)) return Array.Empty<SourceInstance>();
            var component = Native.Type(source.Component);
            if (component == null) return Array.Empty<SourceInstance>();
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
                var loaded = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!loaded.IsValid() || !loaded.isLoaded || loaded.name != source.Scene) continue;
                var sizes = new Dictionary<Transform, int>();
                foreach (var root in loaded.GetRootGameObjects()) if (root != null) Size(root.transform, sizes);
                foreach (var pair in sizes) {
                    var go = pair.Key.gameObject;
                    if (pair.Value != size || go.GetComponent(component) == null || IsModObject(go)) continue;
                    var path = BoardScene.PathOf(pair.Key);
                    if (ModObjects.Within(path) || BuildOf(go) != build) continue;
                    found.Add(new KeyValuePair<Transform, string>(pair.Key, path));
                }
            }
            return found.OrderBy(p => p.Value, StringComparer.Ordinal).ThenBy(p => p.Key.GetSiblingIndex()).Select(p => new SourceInstance(p.Value, p.Key.gameObject)).ToArray();
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
            // A copy takes no game role of its source: a milepost copy is never the origin of its prefix.
            Step("game roles", () => {
                foreach (var copy in copies) {
                    if (copy == null) continue;
                    foreach (var field in ModObjects.NotCopied.Where(f => f.Key == TypeOf(copy)))
                        if (Field(copy, field.Value) is FieldInfo info && info.FieldType == typeof(bool)) info.SetValue(copy, false);
                        else throw new MissingFieldException(field.Key, field.Value);
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
            // Unlike the steps above, this one must hold: a copy that a switch of the game still followed would not
            // be standalone, so a failure refuses the build.
            StandBuilt(from, made, source => paired && Array.IndexOf(sources, source.transform) is int at && at >= 0 ? ((Transform)copies[at]).gameObject : null);
        }

        // The switches of the game that set objects active or inactive (Railroader 0.6.3). A construction controller
        // (Game.Mow.TrackConstructionScenery.UpdateVisibility) sets the targets of its entries by the construction state of
        // its track segments, which base-game progression drives: a map feature enables their track groups
        // (MapFeatureManager.UpdateGraphGroups, Graph.SetGroupEnabled) and a construction milestone sets their state
        // (TrackConstructionMilestone). A map feature also sets gameObjectsEnableOnUnlock by whether it is unlocked
        // (MapFeatureManager.UpdateFeatureForUnlocked), a progression controller its two lists by its progression state
        // (ProgressionEntityObjectController), and a map object activation its own object by its unlock key.
        const string MapFeatureType = GroupWorld.MapFeatureType, ProgressionType = GroupWorld.ProgressionControllerType, MapObjectActivationType = SceneObjectRules.MapObject;
        static IEnumerable<object> Items(object list) => list as System.Collections.IEnumerable == null ? Enumerable.Empty<object>() : ((System.Collections.IEnumerable)list).Cast<object>();
        static object Member(object owner, string name) {
            var field = owner.GetType().GetField(name, Native.Flags) ?? throw new MissingFieldException(owner.GetType().FullName, name);
            return field.GetValue(owner);
        }
        // A mod object stands built and active from its placement, whatever the progression and the construction state,
        // and no switch of the game ever sets it: every object of the copy that a switch names takes the activity it has
        // with all track complete, every feature unlocked and every progression enabled, and the switches inside the
        // copy, whose lists Unity pointed at the copy's own objects, are removed. Switches elsewhere keep naming the source.
        // twin gives the copy's object for an object of the source, or null.
        static void StandBuilt(GameObject from, GameObject made, Func<GameObject, GameObject> twin) {
            GameObject Copied(object item, bool inside) {
                if (!(item is GameObject target) || target == null) return null;
                if (inside) return target.transform.IsChildOf(made.transform) ? target : null;
                return target.transform.IsChildOf(from.transform) ? twin(target) ?? throw new InvalidOperationException("The copy of " + target.name + " couldn't be found.") : null;
            }
            void Set(object list, bool inside, bool active) { foreach (var item in Items(list)) Copied(item, inside)?.SetActive(active); }
            IEnumerable<Component> Switches(string type, bool inside) {
                var found = Native.Type(type);
                if (found == null) return Enumerable.Empty<Component>();
                return inside ? made.GetComponentsInChildren(found, true)
                    : Resources.FindObjectsOfTypeAll(found).OfType<Component>().Where(c => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && !c.transform.IsChildOf(made.transform));
            }
            foreach (var inside in new[] { false, true }) {
                foreach (var controller in Switches(SceneObjectRules.ConstructionController, inside))
                    foreach (var entry in Items(Member(controller, "entries")))
                        Set(Member(entry, "targets"), inside, ConstructionSegmentRule.ShownWhenComplete(Member(entry, "condition")?.ToString(), Convert.ToInt32(Member(entry, "trackConstructionState"))));
                foreach (var feature in Switches(MapFeatureType, inside)) Set(Member(feature, "gameObjectsEnableOnUnlock"), inside, true);
                foreach (var controller in Switches(ProgressionType, inside)) {
                    Set(Member(controller, "activeOnProgressionEnabled"), inside, true);
                    Set(Member(controller, "inactiveOnProgressionEnabled"), inside, false);
                }
            }
            foreach (var type in new[] { SceneObjectRules.ConstructionController, MapFeatureType, ProgressionType, MapObjectActivationType })
                foreach (var own in Switches(type, true)) UnityEngine.Object.DestroyImmediate(own);
            // The copy itself starts active, whatever a switch did to its source.
            if (!made.activeSelf) made.SetActive(true);
        }

        public void Remove(object modObject) {
            var made = modObject as GameObject;
            standing.Remove(made);
            if (made != null) pathNames.Remove(made);
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
