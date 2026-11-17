// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The native operations of the baked scenery group and part capabilities: the transform and activity of a group root
    // or a part, the game's cullers in the group and the physics state of its colliders. The rules live in Core's
    // BakedSceneryRules, BakedSceneryGroups and BakedSceneryParts.
    internal sealed class GroupWorld : IGroupWorld {
        internal const string SimpleCullerType = "Helpers.SimpleCuller", GameObjectCullerType = "Helpers.Culling.GameObjectCuller";
        static readonly Type simple = Native.Type(SimpleCullerType), renderer = Native.Type(BoardScene.CullerType), switcher = Native.Type(GameObjectCullerType);
        static readonly FieldInfo cullingGroup = simple?.GetField("_cullingGroup", Native.Flags), cullingSpheres = simple?.GetField("_cullingSpheres", Native.Flags),
            rendererToken = renderer?.GetField("_cullingToken", Native.Flags), switcherToken = switcher?.GetField("_cullingToken", Native.Flags),
            managerSpheres = Native.Type("Helpers.Culling.CullingManager")?.GetField("_spheres", Native.Flags);
        static readonly MethodInfo simpleMoved = simple?.GetMethod("OnTransformDidChange", Native.Flags, null, Type.EmptyTypes, null);
        static readonly FieldInfo simpleRadius = simple?.GetField("radius", Native.Flags), rendererRadius = renderer?.GetField("radius", Native.Flags), switcherRadius = switcher?.GetField("radius", Native.Flags),
            switcherState = switcher?.GetField("_cullingState", Native.Flags);
        // Token.UpdatePosition(Transform, float?): moves a renderer or game-object culler's sphere and, with a radius, sets it.
        static readonly MethodInfo tokenUpdate = renderer?.GetField("_cullingToken", Native.Flags)?.FieldType.GetMethod("UpdatePosition", new[] { typeof(Transform), typeof(float?) });
        internal GroupWorld() {
            if (simple == null || renderer == null || switcher == null) throw new InvalidOperationException("Culler types unavailable");
            if (cullingGroup?.FieldType != typeof(CullingGroup) || cullingSpheres?.FieldType != typeof(BoundingSphere[]) || simpleMoved == null || simpleRadius?.FieldType != typeof(float)) throw new MissingMemberException("Simple culler members unavailable");
            if (rendererToken == null || switcherToken == null || managerSpheres == null) throw new MissingMemberException("Culling registration members unavailable");
            if (renderer.GetMethod("RequestUpdateCullingPosition", Native.Flags) == null || switcher.GetMethod("RequestUpdateCullingPosition", Native.Flags) == null) throw new MissingMethodException("Culling refresh unavailable");
            if (rendererRadius?.FieldType != typeof(float) || switcherRadius?.FieldType != typeof(float) || switcherState == null || tokenUpdate == null) throw new MissingMemberException("Culling radius members unavailable");
            var token = rendererToken.FieldType;
            if (token.GetProperty("Index") == null || token.GetField("Manager") == null || token.GetProperty("IsRemoved") == null) throw new MissingMemberException("Culling token members unavailable");
            if (Native.Type("Helpers.WorldTransformer") == null) throw new InvalidOperationException("Floating origin unavailable");
        }
        public IEnumerable<string> MapIds => ModScenePatchFiles.MapIds();

        // The rule's verdicts for every loaded World root. They are read again after Invalidate, which a whole discovery
        // calls, and when the loaded scenes, their World roots or the objects down to the group roots change; a change
        // deeper inside a group waits for the next whole discovery.
        static Dictionary<GameObject, GroupRootVerdict> verdicts;
        // The part rule's verdicts for the scene objects below the listed group roots, read with the group verdicts.
        static Dictionary<GameObject, PartVerdict> partVerdicts = new Dictionary<GameObject, PartVerdict>();
        static string verdictKey;
        static int verdictFrame = -1;
        internal static void Invalidate() { verdicts = null; verdictKey = null; }
        internal static IReadOnlyDictionary<GameObject, GroupRootVerdict> Verdicts() {
            // One check of the hierarchy per frame serves every question asked in it.
            if (verdicts != null && verdictFrame == Time.frameCount) return verdicts;
            verdictFrame = Time.frameCount;
            var worlds = new List<GameObject>();
            var key = new System.Text.StringBuilder();
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                key.Append(scene.handle).Append(':');
                foreach (var root in scene.GetRootGameObjects())
                    if (root != null && root.name == BakedSceneryRules.WorldRoot) { worlds.Add(root); Signature(root.transform, 0, key); }
                key.Append(';');
            }
            var text = key.ToString();
            if (verdicts != null && verdictKey == text) return verdicts;
            var found = new Dictionary<GameObject, GroupRootVerdict>();
            var parts = new Dictionary<GameObject, PartVerdict>();
            foreach (var world in worlds) {
                var classified = BakedSceneryRules.Classify(new HierarchyNode(world.transform));
                foreach (var verdict in classified) found[((HierarchyNode)verdict.Node).Transform.gameObject] = verdict;
                foreach (var part in BakedSceneryRules.Parts(classified)) parts[((HierarchyNode)part.Node).Transform.gameObject] = part;
            }
            verdictKey = text; partVerdicts = parts;
            return verdicts = found;
        }
        internal static PartVerdict PartVerdictOf(GameObject go) { if (go == null) return null; Verdicts(); return partVerdicts.TryGetValue(go, out var verdict) ? verdict : null; }
        internal static bool IsPart(GameObject go) => PartVerdictOf(go)?.Disposition == PartDisposition.Part;
        // The listed parts of the listed group roots, for the inventory.
        internal static IEnumerable<GameObject> Parts() { Verdicts(); return partVerdicts.Where(pair => pair.Key != null && pair.Value.Disposition == PartDisposition.Part).Select(pair => pair.Key).ToArray(); }
        // The listed group root a part stands in, or null.
        internal static GameObject GroupRootOf(GameObject part) {
            if (part == null) return null;
            for (var t = part.transform.parent; t != null; t = t.parent) if (VerdictOf(t.gameObject)?.Disposition == GroupDisposition.Group) return t.gameObject;
            return null;
        }
        // The identity of the objects from World down to the group roots: World, its folders, the town folders below
        // World/Large Scenery and the candidates below them.
        // Objects below the candidates, such as the children of a crossing folder, are read again at the next whole discovery.
        static void Signature(Transform t, int depth, System.Text.StringBuilder key, int limit = 2) {
            key.Append(t.GetInstanceID()).Append(',');
            if (depth == 1 && t.name == BakedSceneryRules.LargeScenery) limit = 3;
            if (depth >= limit) return;
            for (var i = 0; i < t.childCount; i++) Signature(t.GetChild(i), depth + 1, key, limit);
            key.Append('/');
        }
        // One line per candidate of the rule and per scene object below a listed group root, for evidence that compares
        // the listed and unlisted group roots and parts.
        internal static IEnumerable<string> Report() =>
            Verdicts().Select(p => p.Key.scene.name + "/" + p.Value.Path + " => " + p.Value.Disposition + (p.Value.Reason == null ? "" : "; " + p.Value.Reason) + (p.Value.Ticket == null ? "" : " (" + p.Value.Ticket + ")"))
                .Concat(partVerdicts.Where(p => p.Key != null).Select(p => p.Key.scene.name + "/" + p.Value.Path + " => " + (p.Value.Disposition == PartDisposition.Part ? "Part" : "Part " + p.Value.Disposition) + (p.Value.Reason == null ? "" : "; " + p.Value.Reason)))
                .OrderBy(line => line, StringComparer.Ordinal);
        internal static GroupRootVerdict VerdictOf(GameObject go) => go != null && Verdicts().TryGetValue(go, out var verdict) ? verdict : null;
        // The family an inspection-only candidate of the rule is listed under, or null when the rule lists none. A listed
        // part stands in the family of its group.
        internal static string FamilyOf(GameObject go) {
            var verdict = VerdictOf(go);
            if (verdict == null) return IsPart(go) ? BakedSceneryRules.Family : null;
            if (verdict.Disposition == GroupDisposition.Group) return BakedSceneryRules.Family;
            if (verdict.Disposition != GroupDisposition.InspectionOnly) return null;
            return verdict.Ticket == BakedSceneryRules.CrossbuckTicket ? "Crossbuck" : BakedSceneryRules.Family;
        }
        // The rule's view of one loaded object; components and children are read once.
        sealed class HierarchyNode : IHierarchyNode {
            internal readonly Transform Transform;
            string[] components; IHierarchyNode[] children; bool physical; bool? family, lod;
            // Read only for the objects below a listed group root.
            public bool LodLevel => lod ?? (lod = IsLodLevel(Transform)).Value;
            // Every renderer at and below the object is one a LOD group on an object above it switches.
            static bool IsLodLevel(Transform t) {
                LODGroup holder = null;
                for (var up = t.parent; up != null && holder == null; up = up.parent) holder = up.GetComponent<LODGroup>();
                if (holder == null) return false;
                var levels = new HashSet<Renderer>(holder.GetLODs().SelectMany(level => level.renderers ?? new Renderer[0]).Where(r => r != null));
                var own = t.GetComponentsInChildren<Renderer>(true).Where(r => r != null && BakedSceneryRules.GivesPresence(r.GetType().FullName, true, false, false)).ToArray();
                return own.Length != 0 && own.All(levels.Contains);
            }
            internal HierarchyNode(Transform transform) { Transform = transform; }
            public string Name => Transform.name;
            void Read() {
                if (components != null) return;
                var own = Transform.GetComponents<Component>();
                components = own.Where(c => c != null).Select(c => c.GetType().FullName).ToArray();
                // A visual effect, such as the fireflies under World/Environment Effects, has a renderer but nothing a
                // track change can run into: it gives no physical presence.
                physical = own.Any(c => c != null && BakedSceneryRules.GivesPresence(c.GetType().FullName, c is Renderer, c is Collider, c is Light));
            }
            public IReadOnlyList<string> Components { get { Read(); return components; } }
            public bool Physical { get { Read(); return physical; } }
            // Read only where the rule asks: for candidates and the children of crossings.
            public bool HasFamily => family ?? (family = Transform.GetComponents<Component>().Any(c => c != null && !(c is Transform) && NativeInventory.FamilyOf(c) != null)).Value;
            public bool Serialized => (Transform.gameObject.hideFlags & HideFlags.DontSave) == 0;
            public IReadOnlyList<IHierarchyNode> Children {
                get {
                    if (children == null) { children = new IHierarchyNode[Transform.childCount]; for (var i = 0; i < children.Length; i++) children[i] = new HierarchyNode(Transform.GetChild(i)); }
                    return children;
                }
            }
        }

        static GameObject Root(object group) => group as GameObject ?? throw new InvalidOperationException("The group no longer exists");
        static GameObject PartObject(object part) => part as GameObject ?? throw new InvalidOperationException("The part no longer exists");
        static bool Loaded(GameObject g) => g != null && g.scene.IsValid() && g.scene.isLoaded;
        public IReadOnlyList<object> Find(string scene, string path) =>
            BoardScene.LoadedAt(scene, path).Where(g => Loaded(g) && VerdictOf(g)?.Disposition == GroupDisposition.Group).Cast<object>().ToArray();
        public string Refusal(object group) => Problem(group as GameObject);
        // Why a group root cannot be edited, or null. Shared with the inventory.
        internal static string Problem(GameObject root) {
            if (!Loaded(root)) return "The group's scene is not loaded";
            if (VerdictOf(root)?.Disposition != GroupDisposition.Group) return "This object is not a group root of the Baked scenery group family";
            var components = root.GetComponentsInChildren<Component>(true);
            if (components.Any(c => c == null)) return "Missing script in the group hierarchy";
            if (components.Any(c => c is Rigidbody)) return "The group holds a rigidbody; only static colliders are supported";
            return null;
        }
        public IReadOnlyList<object> FindParts(string scene, string path) =>
            BoardScene.LoadedAt(scene, path).Where(g => Loaded(g) && IsPart(g)).Cast<object>().ToArray();
        public string PartRefusal(object part) => PartProblem(part as GameObject);
        // Why a part cannot be edited, or null: what refuses its group refuses the part. Shared with the inventory.
        internal static string PartProblem(GameObject part) {
            if (!Loaded(part)) return "The part's scene is not loaded";
            if (!IsPart(part)) return "This object is not listed as a baked scenery part";
            var root = GroupRootOf(part);
            return root == null ? "The group root for this part is no longer listed" : Problem(root);
        }
        public string ActivityRefusal(object part) => SwitchedPart(part as GameObject);
        // Why the part's Enabled setting cannot be set, or null: a game-object culler switches the part on and off by
        // camera distance and would override a stored setting, so the patcher cannot hold it.
        internal static string SwitchedPart(GameObject part) {
            var root = GroupRootOf(part);
            return root != null && ListingCuller(part, root) != null ? "The game's culler enables and disables this part based on camera distance. It would override a saved Enabled setting, so the patcher cannot keep that setting applied" : null;
        }
        // The game-object culler in the part's group that lists the object, or null.
        static Component ListingCuller(GameObject go, GameObject root) {
            if (switcher == null || switchedObjects == null || root == null) return null;
            foreach (var culler in root.GetComponentsInChildren(switcher, true))
                if (switchedObjects.GetValue(culler) is System.Collections.IEnumerable objects)
                    foreach (var item in objects) if (item is GameObject listed && listed == go) return culler;
            return null;
        }
        // Whether a simple culler on the object switches the object itself by camera distance now.
        internal static bool SwitchesItself(GameObject go) { if (simple == null || cullingGroup == null) return false; var group = GroupOf(go.GetComponent(simple)); return group != null && group.enabled; }
        // A part's own activity is switched by a simple culler on it, or by a game-object culler that lists it.
        static bool Switched(Transform t, HashSet<GameObject> listed) => (simple != null && t.GetComponent(simple) != null) || listed.Contains(t.gameObject);
        static readonly FieldInfo switchedObjects = switcher?.GetField("gameObjects", Native.Flags);
        static HashSet<GameObject> Listed(GameObject root) {
            var listed = new HashSet<GameObject>();
            if (switcher == null || switchedObjects == null) return listed;
            foreach (var culler in root.GetComponentsInChildren(switcher, true))
                if (switchedObjects.GetValue(culler) is System.Collections.IEnumerable objects)
                    foreach (var item in objects) if (item is GameObject go && go != null) listed.Add(go);
            return listed;
        }
        static Component OwnSimple(GameObject root) => simple == null ? null : root.GetComponent(simple);
        static CullingGroup GroupOf(Component culler) => culler == null ? null : cullingGroup.GetValue(culler) as CullingGroup;
        // The object's own activity as the scene authored it. A simple culler on the object, or a game-object culler in
        // its group that lists it, switches it off while the camera is far away; off while that culler hides it, the
        // object counts as on, as AuthoredActivity.Enabled reads it for the inventory.
        internal static bool Authored(GameObject go) {
            if (go.activeSelf) return true;
            var group = GroupOf(OwnSimple(go));
            if (AuthoredActivity.Enabled(false, group != null && group.enabled, group != null && group.enabled && group.IsVisible(0))) return true;
            var culler = ListingCuller(go, GroupRootOf(go) ?? go);
            return culler != null && AuthoredActivity.Enabled(false, true, Convert.ToString(switcherState.GetValue(culler)) == "Visible");
        }
        static double[] Coordinates(Vector3 v) => new double[] { v.x, v.y, v.z };
        static double[] Coordinates(Quaternion q) => new double[] { q.x, q.y, q.z, q.w };
        public GroupObservation Observe(object target) {
            var root = Root(target);
            Physics.SyncTransforms();
            var t = root.transform;
            var position = NativeInventory.MapPosition(t); var rotation = t.rotation;
            var state = new ObjectState(new Placement(Coordinates(position), Coordinates(rotation)), Authored(root));
            var parts = new List<GroupPart>();
            var listed = Listed(root);
            foreach (var part in root.GetComponentsInChildren<Transform>(true)) {
                if (part == t || Transient(part, t)) continue;
                var local = t.InverseTransformPoint(part.position); var turn = Quaternion.Inverse(t.rotation) * part.rotation;
                parts.Add(new GroupPart(Relative(t, part) + "#" + part.GetInstanceID(), Coordinates(local), Coordinates(turn), part.gameObject.activeSelf, Switched(part, listed), part.gameObject, part.parent == t ? null : part.parent.gameObject));
            }
            var cullers = new List<GroupCuller>();
            foreach (var culler in root.GetComponentsInChildren(simple, true)) {
                var group = GroupOf(culler); var spheres = cullingSpheres.GetValue(culler) as BoundingSphere[];
                var holds = group != null && group.enabled && spheres != null && spheres.Length > 0 && (culler.transform == t || culler.transform.parent.gameObject.activeInHierarchy);
                // Each culler reports its radius and the radius that encloses the renderers it culls.
                var radius = spheres != null && spheres.Length > 0 ? spheres[0].radius : (float)simpleRadius.GetValue(culler);
                cullers.Add(new GroupCuller(CullerId(t, culler), holds ? Map(spheres[0].position, culler.transform) : null, Map(culler.transform.position, culler.transform), radius, Enclosing(culler.transform, t), culler.gameObject));
            }
            foreach (var type in new[] { renderer, switcher })
                foreach (var culler in root.GetComponentsInChildren(type, true)) {
                    var switches = type == switcher ? Switches(culler) : new GameObject[0];
                    cullers.Add(new GroupCuller(CullerId(t, culler), Registered(culler), Map(culler.transform.position, culler.transform), SphereRadius(culler), Enclosing(culler.transform, t, switches, type != switcher), culler.gameObject, switches, type == switcher));
                }
            var own = GroupOf(OwnSimple(root));
            return new GroupObservation(state, parts, cullers, t.parent, own == null ? (bool?)null : own.enabled);
        }
        public PartObservation ObservePart(object target) {
            var part = PartObject(target);
            var root = GroupRootOf(part) ?? throw new InvalidOperationException("The group root for this part is no longer listed");
            var group = Observe(root);
            var position = NativeInventory.MapPosition(part.transform);
            // A part without a fixed placement keeps its place in the object directly above it.
            var parent = part.transform.parent;
            var parentPlacement = parent == null ? null : new Placement(Coordinates(NativeInventory.MapPosition(parent)), Coordinates(parent.rotation));
            return new PartObservation(new ObjectState(new Placement(Coordinates(position), Coordinates(part.transform.rotation)), Authored(part)), root, group, parent == null ? null : parent.gameObject, parentPlacement);
        }
        static string CullerId(Transform root, Component culler) => Relative(root, culler.transform) + "|" + culler.GetType().FullName;
        static GameObject[] Switches(Component culler) =>
            switchedObjects.GetValue(culler) is System.Collections.IEnumerable objects ? objects.OfType<GameObject>().Where(go => go != null).ToArray() : new GameObject[0];
        // The radius a renderer or game-object culler's sphere holds in its culling manager, or its authored radius while
        // it holds none.
        static double SphereRadius(Component culler) {
            var token = (culler.GetType() == switcher ? switcherToken : rendererToken).GetValue(culler);
            if (token != null && !(bool)Native.Read(token, "IsRemoved")) {
                var index = (int)Native.Read(token, "Index");
                var all = (BoundingSphere[])managerSpheres.GetValue(Native.Read(token, "Manager"));
                if (index >= 0 && index < all.Length) return all[index].radius;
            }
            return (float)(culler.GetType() == switcher ? switcherRadius : rendererRadius).GetValue(culler);
        }
        // The radius around the culler that encloses the renderers it culls: those below its object (own), and those below
        // the objects it switches. A renderer counts while every object from it up to the group root is on as the scene
        // authored it, so an object that a culler hides only because the camera is far away still counts. The bounds
        // come from the meshes, which an object that is switched off still has.
        static float Enclosing(Transform centre, Transform root, IEnumerable<GameObject> listed = null, bool own = true) {
            var radius = 0f;
            var shapes = (own ? centre.GetComponentsInChildren<Renderer>(true) : new Renderer[0]).Concat((listed ?? Enumerable.Empty<GameObject>()).SelectMany(go => go.GetComponentsInChildren<Renderer>(true))).Distinct();
            foreach (var shape in shapes) {
                if (shape == null || !BakedSceneryRules.GivesPresence(shape.GetType().FullName, true, false, false) || !AuthoredOnBelow(shape.transform, root)) continue;
                foreach (var corner in Corners(shape)) radius = Math.Max(radius, (corner - centre.position).magnitude);
            }
            return radius;
        }
        static bool AuthoredOnBelow(Transform t, Transform root) {
            for (; t != null && t != root; t = t.parent) if (!Authored(t.gameObject)) return false;
            return t == root;
        }
        static IEnumerable<Vector3> Corners(Renderer shape) {
            Bounds local; Matrix4x4 matrix;
            if (shape is SkinnedMeshRenderer skinned) { local = skinned.localBounds; matrix = (skinned.rootBone != null ? skinned.rootBone : skinned.transform).localToWorldMatrix; }
            else {
                var filter = shape.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) { local = filter.sharedMesh.bounds; matrix = shape.transform.localToWorldMatrix; }
                else if (shape.gameObject.activeInHierarchy) { local = shape.bounds; matrix = Matrix4x4.identity; }
                else yield break;
            }
            for (var i = 0; i < 8; i++)
                yield return matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
        }
        static bool Transient(Transform child, Transform root) {
            for (var t = child; t != null && t != root; t = t.parent) if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) return true;
            return false;
        }
        static string Relative(Transform root, Transform t) => t == root ? "." : Relative(root, t.parent) + "/" + t.name;
        static double[] Map(Vector3 scene, Transform owner) { var p = NativeInventory.MapPosition(scene, NativeInventory.MovesWithOrigin(owner)); return new double[] { p.x, p.y, p.z }; }
        // The centre of the culling sphere a renderer or game-object culler holds in its culling manager, or null.
        static double[] Registered(Component culler) {
            var token = (culler.GetType() == switcher ? switcherToken : rendererToken).GetValue(culler);
            if (token == null || (bool)Native.Read(token, "IsRemoved")) return null;
            var index = (int)Native.Read(token, "Index");
            var all = (BoundingSphere[])managerSpheres.GetValue(Native.Read(token, "Manager"));
            if (index < 0 || index >= all.Length) throw new InvalidOperationException("Culling sphere index out of range");
            return Map(all[index].position, culler.transform);
        }
        public void SetActive(object target, bool active) => Switch(Root(target), active);
        public void SetPartActive(object target, bool active) => Switch(PartObject(target), active);
        // A simple culler on the object switches it on when the camera comes near; while the object is disabled its
        // culling group sends no events, and it resumes when the object is enabled.
        static void Switch(GameObject go, bool active) {
            var group = GroupOf(OwnSimple(go));
            // Enabled again, the object takes the visibility its culling group holds, so a far object does not show until
            // the camera comes near. An object whose culler has not started yet starts it now.
            if (active) { if (group != null) group.enabled = true; go.SetActive(group == null || group.IsVisible(0)); }
            else { if (group != null) group.enabled = false; go.SetActive(false); }
            Physics.SyncTransforms();
        }
        public void SetPlacement(object target, Placement placement) => Place(Root(target), placement);
        public void SetPartPlacement(object target, Placement placement) => Place(PartObject(target), placement);
        static void Place(GameObject go, Placement placement) {
            var map = new Vector3(Checked(placement.Position[0]), Checked(placement.Position[1]), Checked(placement.Position[2]));
            var rotation = new Quaternion(Checked(placement.Rotation[0]), Checked(placement.Rotation[1]), Checked(placement.Rotation[2]), Checked(placement.Rotation[3]));
            var moves = NativeInventory.MovesWithOrigin(go.transform);
            var scene = NativeInventory.ScenePosition(map, moves);
            if ((NativeInventory.MapPosition(scene, moves) - map).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Map/origin conversion failed its round trip");
            go.transform.SetPositionAndRotation(scene, rotation);
            // Physics reads moved static colliders at the next simulation step; synchronise now so queries and checks agree.
            Physics.SyncTransforms();
        }
        // The authored radius of each culler whose radius a part edit enlarged.
        static readonly Dictionary<Component, float> authoredRadius = new Dictionary<Component, float>();
        public void RefreshCulling(object target, ICollection<string> enclose) {
            var root = Root(target);
            // A culler of an unloaded world holds nothing any more.
            foreach (var gone in authoredRadius.Keys.Where(c => c == null).ToList()) authoredRadius.Remove(gone);
            // A renderer or game-object culler keeps its sphere where it registered it; an inactive one holds none and
            // registers at the current placement when it is enabled.
            foreach (var type in new[] { renderer, switcher })
                foreach (var culler in root.GetComponentsInChildren(type, true))
                    if (culler is Behaviour behaviour && behaviour.isActiveAndEnabled && (culler.GetType() == switcher ? switcherToken : rendererToken).GetValue(culler) != null)
                        Native.Call(culler, "RequestUpdateCullingPosition");
            // A simple culler moves its sphere in its own Update, which does not run while the camera has culled it.
            foreach (var culler in root.GetComponentsInChildren(simple, true))
                if (cullingSpheres.GetValue(culler) != null) simpleMoved.Invoke(culler, null);
            // The game sets the radius of a culler once, from its authored value. While a part below it keeps a fixed
            // placement of its own a culler encloses the renderers it culls, and never shrinks below that value.
            var t = root.transform;
            foreach (var culler in root.GetComponentsInChildren(simple, true)) {
                var radius = Radius(culler, simpleRadius, enclose.Contains(CullerId(t, culler)), () => Enclosing(culler.transform, t));
                if (cullingSpheres.GetValue(culler) is BoundingSphere[] spheres && spheres.Length > 0) spheres[0].radius = radius;
            }
            foreach (var type in new[] { renderer, switcher })
                foreach (var culler in root.GetComponentsInChildren(type, true)) {
                    var radius = Radius(culler, type == switcher ? switcherRadius : rendererRadius, enclose.Contains(CullerId(t, culler)), () => Enclosing(culler.transform, t, type == switcher ? Switches(culler) : null, type != switcher));
                    // A culler that registers again when it is enabled takes the field; a registered one takes the radius now.
                    var token = (type == switcher ? switcherToken : rendererToken).GetValue(culler);
                    if (token != null && !(bool)Native.Read(token, "IsRemoved")) tokenUpdate.Invoke(token, new object[] { culler.transform, (float?)radius });
                }
            Physics.SyncTransforms();
        }
        // Sets the culler's radius field: enclosing its renderers, never below the authored radius, or the authored radius.
        static float Radius(Component culler, FieldInfo field, bool enclose, Func<float> enclosing) {
            if (!authoredRadius.TryGetValue(culler, out var authored)) authored = (float)field.GetValue(culler);
            var radius = enclose ? Math.Max(authored, enclosing()) : authored;
            if (enclose) authoredRadius[culler] = authored; else authoredRadius.Remove(culler);
            field.SetValue(culler, radius);
            return radius;
        }
        public bool Below(object target, object above) =>
            target is GameObject part && part != null && above is GameObject over && over != null && part != over && part.transform.IsChildOf(over.transform);
        public bool Alive(object handle) => handle is GameObject go && go != null;
        static float Checked(double value) { var f = (float)value; if (Single.IsNaN(f) || Single.IsInfinity(f)) throw new InvalidOperationException("Coordinate exceeds native precision"); return f; }
    }
}
