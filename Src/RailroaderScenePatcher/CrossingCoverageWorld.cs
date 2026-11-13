// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RailroaderScenePatcher {
    // An existing grade crossing: a GradeCrossingController whose circuit sets each name an approach A, island and approach B
    // track span. The controller re-reads the sets and their span references on every pass of its loop, so changing them
    // changes detection from the next pass. A span registers in the track graph under its ID when it is enabled and removes
    // that ID when it is disabled. The map editor saves neither the controller's references nor runtime spans, so owned spans
    // exist only in the load that the patch document builds them in. A circuit set is a plain object the controller's list
    // refers to; a set the patcher adds for an authored addition is remembered by its own identity, so a later adapter over a
    // retained world still tells it from the native sets.
    internal sealed class CrossingCoverageWorld : ICrossingCoverageWorld {
        const string GraphType = "Track.Graph", SpanType = "Track.TrackSpan", SegmentType = "Track.TrackSegment", LocationType = "Track.SerializableLocation";
        internal const string RootName = "CrankyAnt Scene Patcher crossing coverage";
        readonly Type controller, span, graph, segment, end, location, circuitSet;
        // The addition key of every circuit set the patcher created in this process, held only while the set itself lives.
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, string> additions = new System.Runtime.CompilerServices.ConditionalWeakTable<object, string>();
        readonly FieldInfo circuitSets, spanId, lower, upper;
        readonly FieldInfo[] roles;
        readonly MethodInfo spanForId, invalidate;
        readonly PropertyInfo isValid, length;
        readonly ConstructorInfo newLocation;
        readonly Dictionary<Scene, GameObject> roots = new Dictionary<Scene, GameObject>();
        internal CrossingCoverageWorld() {
            controller = Native.Type(CrossingCoverageScene.ComponentType); span = Native.Type(SpanType); graph = Native.Type(GraphType); segment = Native.Type(SegmentType); location = Native.Type(LocationType);
            if (controller == null || span == null || graph == null || segment == null || location == null) throw new InvalidOperationException("Crossing-coverage prerequisites unavailable");
            end = segment.GetNestedType("End", Native.Flags) ?? throw new MissingMemberException(SegmentType, "End");
            circuitSets = controller.GetField("circuitSets", Native.Flags) ?? throw new MissingFieldException(CrossingCoverageScene.ComponentType, "circuitSets");
            if (!typeof(IList).IsAssignableFrom(circuitSets.FieldType) || !circuitSets.FieldType.IsGenericType) throw new InvalidOperationException("Crossing circuit sets are not a list");
            var set = circuitSet = circuitSets.FieldType.GetGenericArguments()[0];
            if (!set.IsClass || set.IsAbstract || set.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) == null) throw new InvalidOperationException("A crossing circuit set cannot be constructed");
            roles = CircuitCoverage.Roles.Select(r => set.GetField(r, Native.Flags) ?? throw new MissingFieldException(set.FullName, r)).ToArray();
            if (roles.Any(r => r.FieldType != span)) throw new InvalidOperationException("Crossing circuit roles do not hold track spans");
            spanId = span.GetField("id", Native.Flags) ?? throw new MissingFieldException(SpanType, "id");
            lower = span.GetField("_lower", Native.Flags) ?? throw new MissingFieldException(SpanType, "_lower");
            upper = span.GetField("_upper", Native.Flags) ?? throw new MissingFieldException(SpanType, "_upper");
            if (lower.FieldType != location || upper.FieldType != location) throw new InvalidOperationException("Track span endpoints are not serializable locations");
            isValid = span.GetProperty("IsValid", Native.Flags) ?? throw new MissingMemberException(SpanType, "IsValid");
            length = span.GetProperty("Length", Native.Flags) ?? throw new MissingMemberException(SpanType, "Length");
            invalidate = span.GetMethod("InvalidateCache", Native.Flags, null, Type.EmptyTypes, null) ?? throw new MissingMethodException(SpanType, "InvalidateCache");
            spanForId = graph.GetMethod("SpanForId", Native.Flags, null, new[] { typeof(string) }, null) ?? throw new MissingMethodException(GraphType, "SpanForId");
            foreach (var method in new[] { "AddSpan", "RemoveSpan" }) if (graph.GetMethod(method, Native.Flags, null, new[] { span }, null) == null) throw new MissingMethodException(GraphType, method);
            if (graph.GetMethods(Native.Flags).Count(m => m.Name == "GetSegment") != 1 || segment.GetMethod("GetLength", Native.Flags, null, Type.EmptyTypes, null) == null) throw new MissingMethodException(SegmentType, "GetSegment or GetLength");
            newLocation = location.GetConstructor(Native.Flags, null, new[] { typeof(string), typeof(float), end }, null) ?? throw new MissingMethodException(LocationType, ".ctor(string, float, End)");
            foreach (var field in new[] { "segmentId", "distance", "end" }) if (location.GetField(field, Native.Flags) == null) throw new MissingFieldException(LocationType, field);
        }
        public IEnumerable<string> MapIds => PackDocuments.MapIds();
        static bool Loaded(GameObject g) => g != null && g.scene.IsValid() && g.scene.isLoaded;
        static object Graph() => Native.Read(Native.Type(GraphType), "Shared") ?? throw new InvalidOperationException("The track graph is unavailable");
        Component ControllerOf(object target) => ((GameObject)target).GetComponent(controller) ?? throw new InvalidOperationException("The object no longer holds a crossing controller");
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != CrossingCoverageScene.ComponentType) return Array.Empty<object>();
            return Resources.FindObjectsOfTypeAll(controller).OfType<Component>().Where(c => c != null).Select(c => c.gameObject).Distinct()
                .Where(g => Loaded(g) && g.scene.name == identity.Scene && BoardScene.PathOf(g.transform) == identity.Path).Cast<object>().ToArray();
        }
        public string PatternProblem(object target) => Problem(target as GameObject);
        // Why the object does not conform to the supported crossing pattern, or null. Shared with the inventory.
        internal static string Problem(GameObject go) {
            if (!Loaded(go)) return "This object has no implemented crossing-coverage capability";
            var type = Native.Type(CrossingCoverageScene.ComponentType);
            if (type == null) return "The crossing controller type is unavailable";
            var controllers = go.GetComponents(type);
            if (controllers.Length != 1) return controllers.Length == 0 ? "The object holds no crossing controller" : "The object holds several crossing controllers";
            if (go.GetComponents<Component>().Any(c => c == null)) return "Missing script on the crossing object";
            if (!(type.GetField("circuitSets", Native.Flags)?.GetValue(controllers[0]) is IList sets)) return "The crossing controller holds no circuit list";
            if (sets.Count == 0) return "The crossing has no circuits";
            foreach (var set in sets) {
                if (set == null) return "The crossing holds an empty circuit";
                foreach (var role in CircuitCoverage.Roles) {
                    var value = set.GetType().GetField(role, Native.Flags)?.GetValue(set) as Component;
                    if (value == null) return "A circuit lacks its " + role + " span";
                    if (String.IsNullOrWhiteSpace(value.GetType().GetField("id", Native.Flags)?.GetValue(value) as string)) return "The " + role + " span of a circuit has no ID";
                }
            }
            return null;
        }
        IList Sets(object target) => circuitSets.GetValue(ControllerOf(target)) as IList ?? throw new InvalidOperationException("The crossing controller holds no circuit list");
        public IReadOnlyList<object> Circuits(object target) => Sets(target).Cast<object>().ToArray();
        public IReadOnlyList<object> Spans(object circuit) => roles.Select(r => { var value = r.GetValue(circuit) as Component; return value == null ? null : (object)value; }).ToArray();
        public string SpanId(object value) => spanId.GetValue(value) as string;
        public SpanRange Range(object value) => new SpanRange(Location(lower.GetValue(value)), Location(upper.GetValue(value)));
        static TrackLocation Location(object serialized) {
            var type = serialized.GetType();
            return new TrackLocation(type.GetField("segmentId", Native.Flags).GetValue(serialized) as string,
                Convert.ToString(type.GetField("end", Native.Flags).GetValue(serialized)) == "A" ? "a" : "b", (float)type.GetField("distance", Native.Flags).GetValue(serialized));
        }
        object Lookup(string id) { var found = spanForId.Invoke(Graph(), new object[] { id }) as UnityEngine.Object; return found == null ? null : found; }
        public bool Registered(object value) { var id = SpanId(value); return !String.IsNullOrEmpty(id) && ReferenceEquals(Lookup(id), value); }
        public bool IdRegistered(string id) => Lookup(id) != null;
        public bool Covers(object value) {
            if ((value as UnityEngine.Object) == null || !(bool)isValid.GetValue(value, null)) return false;
            var covered = (float)length.GetValue(value, null);
            return covered > 0 && !Single.IsNaN(covered) && !Single.IsInfinity(covered);
        }
        public IReadOnlyList<double> SegmentLengths(string id) {
            var found = Native.Call(Graph(), "GetSegment", id);
            return found == null ? Array.Empty<double>() : new double[] { (float)Native.Call(found, "GetLength") };
        }
        object Serializable(TrackLocation value) {
            if (value.End != "a" && value.End != "b") throw new InvalidOperationException("End must be a or b");
            var distance = (float)value.Distance;
            if (Single.IsNaN(distance) || Single.IsInfinity(distance)) throw new InvalidOperationException("Distance exceeds native precision");
            return newLocation.Invoke(new object[] { value.Segment, distance, Enum.Parse(end, value.End == "a" ? "A" : "B") });
        }
        // The owned spans live under one root per map scene, so they unload with the map. The game never saves the root.
        GameObject Root(Scene scene) {
            if (roots.TryGetValue(scene, out var root) && root != null) return root;
            root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            roots[scene] = root;
            return root;
        }
        // Builds the span disabled, as the native span loader does, and enables it once its ID and endpoints are set, so its own
        // lifecycle registers it.
        public object CreateSpan(object target, string id, SpanRange range) {
            var owner = (GameObject)target;
            if (IdRegistered(id)) throw new InvalidOperationException("Span ID " + id + " is already registered");
            var go = new GameObject(id) { hideFlags = HideFlags.DontSave };
            go.SetActive(false);
            try {
                go.transform.SetParent(Root(owner.scene).transform, false);
                var created = go.AddComponent(span);
                spanId.SetValue(created, id);
                lower.SetValue(created, Serializable(range.From));
                upper.SetValue(created, Serializable(range.To));
                invalidate.Invoke(created, null);
                go.SetActive(true);
                if (!ReferenceEquals(Lookup(id), created)) throw new InvalidOperationException("The owned span " + id + " did not register");
                return created;
            } catch {
                go.SetActive(false);
                UnityEngine.Object.Destroy(go);
                throw;
            }
        }
        // Disabling removes the ID from the graph; destruction follows at the end of the frame.
        public void DestroySpan(object value) {
            var component = (Component)value;
            if (component == null) return;
            var go = component.gameObject;
            if (Lookup(SpanId(value)) is object registered && !ReferenceEquals(registered, value)) throw new InvalidOperationException("Span ID " + SpanId(value) + " belongs to another span; it is not removed");
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
        }
        // A new set stays outside every controller until SetCircuits lists it.
        public object CreateCircuit(object target, string key) {
            ControllerOf(target);
            var set = Activator.CreateInstance(circuitSet, true);
            additions.Add(set, key);
            return set;
        }
        public string AdditionKey(object circuit) => circuit != null && additions.TryGetValue(circuit, out var key) ? key : null;
        public void SetSpans(object circuit, object approachA, object island, object approachB) {
            var values = new[] { approachA, island, approachB };
            if (values.Any(v => (v as UnityEngine.Object) == null)) throw new InvalidOperationException("A circuit needs three spans");
            for (var i = 0; i < 3; i++) roles[i].SetValue(circuit, values[i]);
        }
        // The controller keeps its own list instance, which its loop indexes on every pass.
        public void SetCircuits(object target, IReadOnlyList<object> circuits) {
            var sets = Sets(target);
            sets.Clear();
            foreach (var circuit in circuits) sets.Add(circuit);
        }
        public double[] Position(object target) {
            if (Native.Read(Native.Type("Helpers.WorldTransformer"), "Shared") == null) throw new InvalidOperationException("Floating origin unavailable");
            var p = (Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "WorldToGame", ((GameObject)target).transform.position);
            return new double[] { p.x, p.y, p.z };
        }
        public double[] Rotation(object target) { var r = ((GameObject)target).transform.rotation; return new double[] { r.x, r.y, r.z, r.w }; }
        public bool Active(object target) => ((GameObject)target).activeSelf;
    }
}
