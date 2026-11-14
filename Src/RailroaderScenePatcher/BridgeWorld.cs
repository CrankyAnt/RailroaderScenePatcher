// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The retained Ela bridge pattern: a renderer-culler root whose descendants hold the bridge colliders.
    // Generated trestles belong to track segments and the construction-scenery controller sits beside the
    // bridge; both are separate scenery that a bridge placement must leave unchanged.
    internal sealed class BridgeWorld : IBridgeWorld {
        internal static readonly string[] SupportedPaths = { "World/Large Scenery/Ela/Ela Bridge" };
        internal const string TrestleType = "AutoTrestle.AutoTrestle", SceneryType = "Game.Mow.TrackConstructionScenery";
        const float TrestleRadius = 250f;
        readonly Type transformer, culler, trestle, scenery;
        // Trestles selected at the first observation of each bridge, so a moved bridge keeps comparing the same controls.
        readonly Dictionary<GameObject, Component[]> trestles = new Dictionary<GameObject, Component[]>();
        internal BridgeWorld() {
            transformer = Native.Type("Helpers.WorldTransformer"); culler = Native.Type(BoardScene.CullerType);
            trestle = Native.Type(TrestleType); scenery = Native.Type(SceneryType);
            if (transformer == null || culler == null || trestle == null || scenery == null) throw new InvalidOperationException("Bridge prerequisites unavailable");
            foreach (var method in new[] { "WorldToGame", "GameToWorld" })
                if (transformer.GetMethods(Native.Flags).Count(m => m.Name == method && m.GetParameters().Length == 1) != 1) throw new MissingMethodException(method);
            if (culler.GetMethod("RequestUpdateCullingPosition", Native.Flags) == null) throw new MissingMethodException("Culling refresh unavailable");
            foreach (var field in new[] { "controlPoints", "_beamMatrices", "_hasGenerated" }) if (FieldOf(trestle, field) == null) throw new MissingFieldException(TrestleType, field);
            foreach (var field in new[] { "segmentIds", "entries" }) if (FieldOf(scenery, field) == null) throw new MissingFieldException(SceneryType, field);
        }
        public IEnumerable<string> MapIds => PackDocuments.MapIds();
        static FieldInfo FieldOf(Type type, string name) {
            for (var t = type; t != null; t = t.BaseType) { var f = t.GetField(name, Native.Flags | BindingFlags.DeclaredOnly); if (f != null) return f; }
            return null;
        }
        static object Field(object owner, string name) {
            var field = FieldOf(owner.GetType(), name) ?? throw new MissingFieldException(owner.GetType().FullName, name);
            return field.GetValue(owner);
        }
        static bool Loaded(GameObject g) => g != null && g.scene.IsValid() && g.scene.isLoaded;
        internal static bool IsSupported(GameObject g) => Loaded(g) && SupportedPaths.Contains(BoardScene.PathOf(g.transform));
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != BoardScene.CullerType || !SupportedPaths.Contains(identity.Path)) return Array.Empty<object>();
            return BoardScene.LoadedAt(identity.Scene, identity.Path).Where(g => Loaded(g) && g.GetComponent(culler) != null).Cast<object>().ToArray();
        }
        public void CheckPattern(object target) {
            var problem = PatternProblem(target as GameObject);
            if (problem != null) throw new InvalidOperationException(problem);
            Convert("WorldToGame", ((GameObject)target).transform.position);
        }
        // Why the object does not conform to the supported bridge pattern, or null. Shared with the inventory.
        internal static string PatternProblem(GameObject bridge) {
            if (bridge == null || !IsSupported(bridge)) return "This object has no implemented bridge-placement capability";
            var cullerType = Native.Type(BoardScene.CullerType);
            // The root itself may be inactive through an explicit disable; its parents and root culler may not.
            if (bridge.transform.parent == null || !bridge.transform.parent.gameObject.activeInHierarchy) return "The bridge's parents must be active";
            if (cullerType == null || !(bridge.GetComponent(cullerType) is Behaviour rootCuller) || !rootCuller.enabled) return "The bridge root's renderer culler must be enabled";
            var components = bridge.GetComponentsInChildren<Component>(true);
            if (components.Any(c => c == null)) return "Missing script in the bridge hierarchy";
            var owned = components.FirstOrDefault(c => { var n = c.GetType().FullName ?? ""; return n.StartsWith("Track.", StringComparison.Ordinal) || n.StartsWith("AutoTrestle.", StringComparison.Ordinal) || n == SceneryType; });
            if (owned != null) return "Unexpected track-owned component " + owned.GetType().FullName + " in the bridge hierarchy";
            if (components.Any(c => c is Rigidbody)) return "The bridge hierarchy holds a rigidbody; only static bridge colliders are supported";
            if (!components.Any(c => c is Collider)) return "The bridge has no colliders";
            // Native construction control may switch bridge parts, never the root that an explicit disable owns.
            foreach (var controller in Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => c != null && Loaded(c.gameObject) && c.GetType().FullName == SceneryType)) {
                if (!(Field(controller, "entries") is IEnumerable entries)) return "Construction scenery entries unavailable";
                foreach (var entry in entries) {
                    if (entry == null || !(Field(entry, "targets") is GameObject[] targets)) return "Construction scenery targets unavailable";
                    if (targets.Any(t => t != null && bridge.transform.IsChildOf(t.transform))) return "Construction scenery controls the bridge root's activity";
                }
            }
            return null;
        }
        public BridgeObservation Observe(object target) {
            var bridge = (GameObject)target;
            Physics.SyncTransforms();
            var root = bridge.transform;
            var position = Convert("WorldToGame", root.position); var rotation = root.rotation;
            var state = new ObjectState(new Placement(new double[] { position.x, position.y, position.z }, new double[] { rotation.x, rotation.y, rotation.z, rotation.w }), bridge.activeSelf);
            var colliders = new List<ColliderState>();
            foreach (var c in bridge.GetComponentsInChildren<Collider>(true)) {
                var t = c.transform; var chain = true;
                for (var node = t; node != null && node != root; node = node.parent) chain &= node.gameObject.activeSelf;
                var local = root.InverseTransformPoint(t.position); var turn = Quaternion.Inverse(root.rotation) * t.rotation;
                colliders.Add(new ColliderState(Relative(root, t) + "|" + c.GetType().FullName + "|" + c.GetInstanceID(), Shape(c), c.enabled, chain, c.gameObject.activeInHierarchy,
                    new double[] { local.x, local.y, local.z }, new double[] { turn.x, turn.y, turn.z, turn.w }, BoundsFollow(c)));
            }
            return new BridgeObservation(state, bridge.activeInHierarchy, colliders, SeparateScenery(bridge));
        }
        static string Relative(Transform root, Transform t) => t == root ? "." : Relative(root, t.parent) + "/" + t.name;
        static string F(float n) => n.ToString("R", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
        static string Q(Quaternion q) => F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);
        static string Shape(Collider c) {
            var shape = c.GetType().FullName + " trigger=" + c.isTrigger + " layer=" + c.gameObject.layer + " contactOffset=" + F(c.contactOffset);
            if (c is BoxCollider box) shape += " center=" + V(box.center) + " size=" + V(box.size);
            if (c is SphereCollider sphere) shape += " center=" + V(sphere.center) + " radius=" + F(sphere.radius);
            if (c is CapsuleCollider capsule) shape += " center=" + V(capsule.center) + " radius=" + F(capsule.radius) + " height=" + F(capsule.height) + " direction=" + capsule.direction;
            if (c is MeshCollider mesh) shape += " mesh=" + (mesh.sharedMesh == null ? 0 : mesh.sharedMesh.GetInstanceID()) + " vertices=" + (mesh.sharedMesh == null ? 0 : mesh.sharedMesh.vertexCount) + " convex=" + mesh.convex + " cooking=" + mesh.cookingOptions;
            return shape;
        }
        // After synchronisation, the world bounds of an active primitive collider are centred on its transformed centre.
        // Mesh collider bounds are not centred on a fixed point; their agreement is left to the native contact check.
        static bool BoundsFollow(Collider c) {
            if (!c.enabled || !c.gameObject.activeInHierarchy) return true;
            Vector3 center;
            if (c is BoxCollider box) center = box.center;
            else if (c is SphereCollider sphere) center = sphere.center;
            else if (c is CapsuleCollider capsule) center = capsule.center;
            else return true;
            return (c.bounds.center - c.transform.TransformPoint(center)).magnitude <= 0.01f;
        }
        string SeparateScenery(GameObject bridge) {
            if (!trestles.TryGetValue(bridge, out var selected)) trestles.Add(bridge, selected = NearbyTrestles(bridge.transform.position));
            var text = new StringBuilder();
            foreach (var c in selected) {
                if (c == null) { text.AppendLine("trestle destroyed"); continue; }
                var t = c.transform;
                text.Append("trestle ").Append(c.GetInstanceID()).Append(' ').Append(BoardScene.PathOf(t)).Append(" parent=").Append(t.parent == null ? 0 : t.parent.GetInstanceID())
                    .Append(" local=").Append(V(t.localPosition)).Append(' ').Append(Q(t.localRotation)).Append(' ').Append(V(t.localScale))
                    .Append(" active=").Append(c.gameObject.activeSelf).Append('/').Append(c.gameObject.activeInHierarchy).Append(" enabled=").Append(((Behaviour)c).enabled)
                    .Append(" generated=").Append(Field(c, "_hasGenerated"));
                if (Field(c, "controlPoints") is IEnumerable points) foreach (var point in points) text.Append(" point=").Append(point == null ? "null" : V((Vector3)Field(point, "position")) + "/" + Q((Quaternion)Field(point, "rotation")));
                var matrices = new StringBuilder(); var count = 0;
                if (Field(c, "_beamMatrices") is IEnumerable beams) foreach (Matrix4x4 m in beams) { for (var i = 0; i < 16; i++) matrices.Append(F(m[i])).Append(','); matrices.Append(';'); count++; }
                using (var sha = SHA256.Create()) text.Append(" beams=").Append(count).Append(' ').Append(BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(matrices.ToString()))).Replace("-", ""));
                text.AppendLine();
            }
            // Construction-scenery controllers that reference the bridge: their placement and configuration.
            foreach (var controller in Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => c != null && Loaded(c.gameObject) && c.GetType().FullName == SceneryType).OrderBy(c => c.GetInstanceID())) {
                var description = new StringBuilder(); var references = false;
                foreach (var entry in (IEnumerable)Field(controller, "entries")) {
                    var targets = (GameObject[])Field(entry, "targets");
                    description.Append(" entry=").Append(Field(entry, "trackConstructionState")).Append('/').Append(Field(entry, "condition")).Append(':')
                        .Append(String.Join(",", targets.Select(g => g == null ? "null" : g.GetInstanceID().ToString(CultureInfo.InvariantCulture))));
                    references |= targets.Any(g => g != null && g.transform.IsChildOf(bridge.transform));
                }
                if (!references) continue;
                var t = controller.transform;
                text.Append("construction ").Append(controller.GetInstanceID()).Append(' ').Append(BoardScene.PathOf(t)).Append(" local=").Append(V(t.localPosition)).Append(' ').Append(Q(t.localRotation))
                    .Append(" active=").Append(controller.gameObject.activeSelf).Append(" enabled=").Append(controller.enabled)
                    .Append(" segments=").Append(String.Join(",", (string[])Field(controller, "segmentIds") ?? new string[0])).Append(description).AppendLine();
            }
            return text.ToString();
        }
        Component[] NearbyTrestles(Vector3 center) =>
            Resources.FindObjectsOfTypeAll(trestle).OfType<Component>().Where(c => c != null && Loaded(c.gameObject) && Near(c, center)).OrderBy(c => c.GetInstanceID()).ToArray();
        // Distances in world space equal distances in map space under the floating origin.
        static bool Near(Component c, Vector3 center) {
            if ((c.transform.position - center).magnitude <= TrestleRadius) return true;
            if (!(Field(c, "controlPoints") is IEnumerable points)) return false;
            foreach (var point in points) if (point != null && (c.transform.TransformPoint((Vector3)Field(point, "position")) - center).magnitude <= TrestleRadius) return true;
            return false;
        }
        public void SetActive(object target, bool active) {
            var bridge = (GameObject)target;
            bridge.SetActive(active);
            Physics.SyncTransforms();
        }
        public void SetPose(object target, Placement placement) {
            var bridge = (GameObject)target;
            var position = new Vector3(Checked(placement.Position[0]), Checked(placement.Position[1]), Checked(placement.Position[2]));
            var rotation = new Quaternion(Checked(placement.Rotation[0]), Checked(placement.Rotation[1]), Checked(placement.Rotation[2]), Checked(placement.Rotation[3]));
            if (Math.Abs(Quaternion.Dot(rotation, rotation) - 1f) > 0.001f) throw new InvalidOperationException("Rotation must be a unit quaternion");
            var local = Convert("GameToWorld", position);
            if ((Convert("WorldToGame", local) - position).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Map/origin conversion failed its round trip");
            bridge.transform.SetPositionAndRotation(local, rotation);
            // An inactive culler holds no culling sphere; it registers at the current pose when it is enabled.
            foreach (var c in bridge.GetComponentsInChildren(culler, true).OfType<Behaviour>().Where(c => c.enabled && c.gameObject.activeInHierarchy))
                Native.Call(c, "RequestUpdateCullingPosition");
            // Physics reads moved static colliders at the next simulation step; synchronise now so queries and checks agree.
            Physics.SyncTransforms();
        }
        Vector3 Convert(string direction, Vector3 position) {
            if (Native.Read(transformer, "Shared") == null) throw new InvalidOperationException("Floating origin unavailable");
            var value = (Vector3)Native.Call(transformer, direction, position);
            if (Single.IsNaN(value.x) || Single.IsNaN(value.y) || Single.IsNaN(value.z) || Single.IsInfinity(value.x) || Single.IsInfinity(value.y) || Single.IsInfinity(value.z)) throw new InvalidOperationException("Invalid origin conversion");
            return value;
        }
        static float Checked(double value) { var f = (float)value; if (Single.IsNaN(f) || Single.IsInfinity(f)) throw new InvalidOperationException("Coordinate exceeds native precision"); return f; }
    }
}
