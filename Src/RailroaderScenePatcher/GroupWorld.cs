// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The native operations of the baked scenery group capability: the group root's transform and activity, the game's
    // cullers in the group and the physics state of its colliders. The rules live in Core's BakedSceneryRules and
    // BakedSceneryGroups.
    internal sealed class GroupWorld : IGroupWorld {
        internal const string SimpleCullerType = "Helpers.SimpleCuller", GameObjectCullerType = "Helpers.Culling.GameObjectCuller";
        static readonly Type simple = Native.Type(SimpleCullerType), renderer = Native.Type(BoardScene.CullerType), switcher = Native.Type(GameObjectCullerType);
        static readonly FieldInfo cullingGroup = simple?.GetField("_cullingGroup", Native.Flags), cullingSpheres = simple?.GetField("_cullingSpheres", Native.Flags),
            rendererToken = renderer?.GetField("_cullingToken", Native.Flags), switcherToken = switcher?.GetField("_cullingToken", Native.Flags),
            managerSpheres = Native.Type("Helpers.Culling.CullingManager")?.GetField("_spheres", Native.Flags);
        static readonly MethodInfo simpleMoved = simple?.GetMethod("OnTransformDidChange", Native.Flags, null, Type.EmptyTypes, null);
        internal GroupWorld() {
            if (simple == null || renderer == null || switcher == null) throw new InvalidOperationException("Culler types unavailable");
            if (cullingGroup?.FieldType != typeof(CullingGroup) || cullingSpheres?.FieldType != typeof(BoundingSphere[]) || simpleMoved == null) throw new MissingMemberException("Simple culler members unavailable");
            if (rendererToken == null || switcherToken == null || managerSpheres == null) throw new MissingMemberException("Culling registration members unavailable");
            if (renderer.GetMethod("RequestUpdateCullingPosition", Native.Flags) == null || switcher.GetMethod("RequestUpdateCullingPosition", Native.Flags) == null) throw new MissingMethodException("Culling refresh unavailable");
            var token = rendererToken.FieldType;
            if (token.GetProperty("Index") == null || token.GetField("Manager") == null || token.GetProperty("IsRemoved") == null) throw new MissingMemberException("Culling token members unavailable");
            if (Native.Type("Helpers.WorldTransformer") == null) throw new InvalidOperationException("Floating origin unavailable");
        }
        public IEnumerable<string> MapIds => ModDocuments.MapIds();

        // The rule's verdicts for every loaded World root. They are read again after Invalidate, which a whole discovery
        // calls, and when the loaded scenes, their World roots or the objects down to the group roots change; a change
        // deeper inside a group waits for the next whole discovery.
        static Dictionary<GameObject, GroupRootVerdict> verdicts;
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
            foreach (var world in worlds)
                foreach (var verdict in BakedSceneryRules.Classify(new HierarchyNode(world.transform)))
                    found[((HierarchyNode)verdict.Node).Transform.gameObject] = verdict;
            verdictKey = text;
            return verdicts = found;
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
        // One line per candidate of the rule, for evidence that compares the listed and unlisted group roots.
        internal static IEnumerable<string> Report() =>
            Verdicts().Select(p => p.Key.scene.name + "/" + p.Value.Path + " => " + p.Value.Disposition + (p.Value.Reason == null ? "" : "; " + p.Value.Reason) + (p.Value.Ticket == null ? "" : " (" + p.Value.Ticket + ")"))
                .OrderBy(line => line, StringComparer.Ordinal);
        internal static GroupRootVerdict VerdictOf(GameObject go) => go != null && Verdicts().TryGetValue(go, out var verdict) ? verdict : null;
        // The family an inspection-only candidate of the rule is listed under, or null when the rule lists none.
        internal static string FamilyOf(GameObject go) {
            var verdict = VerdictOf(go);
            if (verdict == null) return null;
            if (verdict.Disposition == GroupDisposition.Group) return BakedSceneryRules.Family;
            if (verdict.Disposition != GroupDisposition.InspectionOnly) return null;
            return verdict.Ticket == BakedSceneryRules.CrossbuckTicket ? "Crossbuck" : BakedSceneryRules.Family;
        }
        // The rule's view of one loaded object; components and children are read once.
        sealed class HierarchyNode : IHierarchyNode {
            internal readonly Transform Transform;
            string[] components; IHierarchyNode[] children; bool physical; bool? family;
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
        // The root's own activity as the scene authored it. A simple culler on the root switches the root off while the
        // camera is far away; off while its culling group says it is out of view, the root counts as on.
        internal static bool Authored(GameObject root) {
            var group = GroupOf(OwnSimple(root));
            return AuthoredActivity.Enabled(root.activeSelf, group != null && group.enabled, group != null && group.enabled && group.IsVisible(0));
        }
        public GroupObservation Observe(object target) {
            var root = Root(target);
            Physics.SyncTransforms();
            var t = root.transform;
            var position = NativeInventory.MapPosition(t); var rotation = t.rotation;
            var state = new ObjectState(new Placement(new double[] { position.x, position.y, position.z }, new double[] { rotation.x, rotation.y, rotation.z, rotation.w }), Authored(root));
            var parts = new List<GroupPart>();
            var listed = Listed(root);
            foreach (var part in root.GetComponentsInChildren<Transform>(true)) {
                if (part == t || Transient(part, t)) continue;
                var local = t.InverseTransformPoint(part.position); var turn = Quaternion.Inverse(t.rotation) * part.rotation;
                parts.Add(new GroupPart(Relative(t, part) + "#" + part.GetInstanceID(), new double[] { local.x, local.y, local.z }, new double[] { turn.x, turn.y, turn.z, turn.w }, part.gameObject.activeSelf, Switched(part, listed)));
            }
            var cullers = new List<GroupCuller>();
            foreach (var culler in root.GetComponentsInChildren(simple, true)) {
                var group = GroupOf(culler); var spheres = cullingSpheres.GetValue(culler) as BoundingSphere[];
                var holds = group != null && group.enabled && spheres != null && spheres.Length > 0 && (culler.transform == t || culler.transform.parent.gameObject.activeInHierarchy);
                cullers.Add(new GroupCuller(Relative(t, culler.transform) + "|" + SimpleCullerType, holds ? Map(spheres[0].position, culler.transform) : null, Map(culler.transform.position, culler.transform)));
            }
            foreach (var type in new[] { renderer, switcher })
                foreach (var culler in root.GetComponentsInChildren(type, true))
                    cullers.Add(new GroupCuller(Relative(t, culler.transform) + "|" + type.FullName, Registered(culler), Map(culler.transform.position, culler.transform)));
            var own = GroupOf(OwnSimple(root));
            return new GroupObservation(state, parts, cullers, t.parent, own == null ? (bool?)null : own.enabled);
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
        public void SetActive(object target, bool active) {
            var root = Root(target);
            // A simple culler on the root switches the root on when the camera comes near; while the group is disabled its
            // culling group sends no events, and it resumes when the group is enabled.
            var group = GroupOf(OwnSimple(root));
            // Enabled again, the root takes the visibility its culling group holds, so a far group does not show until the
            // camera comes near. A root whose culler has not started yet starts it now.
            if (active) { if (group != null) group.enabled = true; root.SetActive(group == null || group.IsVisible(0)); }
            else { if (group != null) group.enabled = false; root.SetActive(false); }
            Physics.SyncTransforms();
        }
        public void SetPlacement(object target, Placement placement) {
            var root = Root(target);
            var map = new Vector3(Checked(placement.Position[0]), Checked(placement.Position[1]), Checked(placement.Position[2]));
            var rotation = new Quaternion(Checked(placement.Rotation[0]), Checked(placement.Rotation[1]), Checked(placement.Rotation[2]), Checked(placement.Rotation[3]));
            var moves = NativeInventory.MovesWithOrigin(root.transform);
            var scene = NativeInventory.ScenePosition(map, moves);
            if ((NativeInventory.MapPosition(scene, moves) - map).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Map/origin conversion failed its round trip");
            root.transform.SetPositionAndRotation(scene, rotation);
            // A renderer or game-object culler keeps its sphere where it registered it; an inactive one holds none and
            // registers at the current placement when it is enabled.
            foreach (var type in new[] { renderer, switcher })
                foreach (var culler in root.GetComponentsInChildren(type, true))
                    if (culler is Behaviour behaviour && behaviour.isActiveAndEnabled && (culler.GetType() == switcher ? switcherToken : rendererToken).GetValue(culler) != null)
                        Native.Call(culler, "RequestUpdateCullingPosition");
            // A simple culler moves its sphere in its own Update, which does not run while the camera has culled it.
            foreach (var culler in root.GetComponentsInChildren(simple, true))
                if (cullingSpheres.GetValue(culler) != null) simpleMoved.Invoke(culler, null);
            // Physics reads moved static colliders at the next simulation step; synchronise now so queries and checks agree.
            Physics.SyncTransforms();
        }
        static float Checked(double value) { var f = (float)value; if (Single.IsNaN(f) || Single.IsInfinity(f)) throw new InvalidOperationException("Coordinate exceeds native precision"); return f; }
    }
}
