// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // Reads serialized bindings and direct references, never binding/cache getters or TryApply.
    internal sealed class NativeInventory : IInspectionScene {
        const string Marker = "Track.TrackMarker", Span = "Track.TrackSpan", Relative = "Track.TrackRelativePosition";
        const string Crossing = "Track.Crossings.GradeCrossingController", Milepost = "Helpers.MilePost";
        const string ConstructionScenery = "Game.Mow.TrackConstructionScenery";
        internal static readonly string[] Coverage = {
            "Track markers, yard limits, station signs: typed marker and relative-position inspection; edits require the applicable adapter.",
            "Mileposts: marker, origin and display dependencies; model and mileage remain unchanged.",
            "Crossings: controllers, spans, pads, lights, gates and bells; relationships are explicit, never inferred from proximity.",
            "Independent boards: culler/mask patterns; only the Sylva board has the existing fixed-placement adapter.",
            "Source-inventory retained bridge: Ela renderer-culler root and its Game.Mow.TrackConstructionScenery controller, inspected separately.",
            "Construction scenery exposes segment and target relationships. Generated track-owned trestles remain owned by native track geometry.",
            "Passenger stops and map-object anchors: native operations/map-data editing; historical FUSE warning is outside current scope.",
            "Player-created signs: native sign editing; this does not establish inherited scene-sign editing.",
            "Signals, CTC and interlocking edits are outside the supported operation boundary.",
            "Dependencies show components, serialized relationships and direct loaded-scene references; indirect consumers can remain unknown."
        };
        public IEnumerable<string> MapIds => PackDocuments.MapIds();
        static bool Loaded(Component c) => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded;
        static string TypeName(Component c) => c == null ? "missing component" : c.GetType().FullName;
        static Component Get(GameObject go, string type) => go.GetComponents<Component>().FirstOrDefault(c => c != null && TypeName(c) == type);
        static Component Parent(Component c, string type) {
            for (var t = c.transform; t != null; t = t.parent) { var found = Get(t.gameObject, type); if (found != null) return found; }
            return null;
        }
        static string Label(Component c) => c == null ? "missing" : c.gameObject.scene.name + "/" + BoardScene.PathOf(c.transform) + " [" + TypeName(c) + "]";
        static IEnumerable<FieldInfo> Fields(Type type) {
            for (; type != null && type != typeof(MonoBehaviour) && type != typeof(Behaviour) && type != typeof(Component); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) yield return field;
        }
        static object Field(object owner, string name) {
            if (owner == null) throw new InvalidOperationException("Missing owner for " + name);
            var field = Fields(owner.GetType()).FirstOrDefault(f => f.Name == name);
            if (field == null) throw new MissingFieldException(owner.GetType().FullName, name);
            return field.GetValue(owner);
        }
        static string Text(object value) => Convert.ToString(value, CultureInfo.InvariantCulture);
        static IEnumerable<UnityEngine.Object> References(object value) {
            if (value is UnityEngine.Object obj) { if (obj != null) yield return obj; }
            else if (value is IList list) foreach (var item in list) if (item is UnityEngine.Object element && element != null) yield return element;
        }
        static string Family(Component c) {
            var type = TypeName(c); var go = c.gameObject;
            if (type == Milepost) return "Milepost";
            if (type == Marker) return Get(go, Milepost) != null ? null : Get(go, "Track.TrackCurveMeshBuilder") != null ? "Crossing pad marker" : "Track marker";
            if (type == Relative && Get(go, Marker) == null) return "Track-relative scene object";
            if (type == Span) return "Track span";
            if (type == Crossing) return "Crossing controller";
            if ((type == "Track.KeyValueLampMaterialController" || type == "RollingStock.Controls.KeyValueBoolAnimator" || type == "Audio.IntegerLoopingPlayer") && Parent(c, Crossing) != null)
                return type == "Track.KeyValueLampMaterialController" ? "Crossing light" : type == "Audio.IntegerLoopingPlayer" ? "Crossing bell" : "Crossing gate";
            if (type == BoardScene.CullerType && go.scene.name == BoardScene.BoardSceneName && BoardScene.PathOf(c.transform) == BoardScene.BoardPath) return "Independent board";
            if (type == BoardScene.CullerType && Get(go, BoardScene.MaskType) != null && Get(go, Marker) == null) return "Culler/mask scene object";
            if (type == BoardScene.CullerType && BoardScene.PathOf(c.transform) == "World/Large Scenery/Ela/Ela Bridge") return "Bridge";
            if (type == ConstructionScenery && BoardScene.PathOf(c.transform) == "World/Large Scenery/Ela/Ela Bridge Scenery") return "Bridge construction scenery";
            return null;
        }
        public IEnumerable<SceneObservation> Discover() {
            var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(Loaded).ToArray();
            var incoming = new Dictionary<UnityEngine.Object, List<string>>(); var scanErrors = 0;
            foreach (var owner in all) foreach (var field in Fields(owner.GetType())) {
                try {
                    foreach (var target in References(field.GetValue(owner))) {
                        if (!incoming.TryGetValue(target, out var refs)) incoming.Add(target, refs = new List<string>());
                        refs.Add("Referenced by " + Label(owner) + "." + field.Name);
                    }
                } catch { scanErrors++; }
            }
            var candidates = all.Select(c => new { Component = c, Family = Family(c) }).Where(c => c.Family != null).ToArray();
            HashSet<string> segmentIds = null;
            string graphError = null;
            try {
                var graph = Native.Read(Native.Type("Track.Graph"), "Shared");
                var lookup = Field(graph, "_segmentsLookup") as IDictionary;
                if (lookup == null) throw new InvalidOperationException("Track segment lookup unavailable");
                segmentIds = new HashSet<string>(lookup.Keys.Cast<object>().Select(Text), StringComparer.Ordinal);
            } catch (Exception e) { graphError = e.GetBaseException().Message; }
            foreach (var candidate in candidates) yield return Observe(candidate.Component, candidate.Family, incoming, scanErrors, segmentIds, graphError);
        }
        static SceneObservation Observe(Component c, string family, Dictionary<UnityEngine.Object, List<string>> incoming, int scanErrors, HashSet<string> segmentIds, string graphError) {
            var go = c.gameObject; var problems = new List<string>(); var bindings = new List<string>(); var dependencies = new List<string>();
            var components = go.GetComponentsInChildren<Component>(true);
            foreach (var child in components) {
                if (child == null) { problems.Add("Missing script in inspected hierarchy"); continue; }
                if (child is Transform) continue;
                dependencies.Add("Component " + Label(child) + (child is Behaviour b ? "; enabled=" + b.enabled : ""));
                if (incoming.TryGetValue(child, out var refs)) dependencies.AddRange(refs);
            }
            if (incoming.TryGetValue(go, out var rootRefs)) dependencies.AddRange(rootRefs);
            if (scanErrors != 0) problems.Add("Direct-reference scan incomplete: " + scanErrors + " unreadable fields");
            void Read(Action action) { try { action(); } catch (Exception e) { problems.Add("Inspection unavailable: " + e.GetBaseException().Message); } }
            var marker = TypeName(c) == Marker ? c : Get(go, Marker);
            if (marker != null) Read(() => {
                bindings.Add("Marker id=" + Text(Field(marker, "id")) + "; type=" + Text(Field(marker, "type")));
                Location(Field(marker, "_location"), "Marker", bindings, problems, segmentIds, graphError);
            });
            if (TypeName(c) == Span) Read(() => {
                bindings.Add("Span id=" + Text(Field(c, "id")));
                Location(Field(c, "_lower"), "Lower", bindings, problems, segmentIds, graphError); Location(Field(c, "_upper"), "Upper", bindings, problems, segmentIds, graphError);
                dependencies.Add("Track graph span registration and occupancy consumers");
            });
            var relative = TypeName(c) == Relative ? c : Get(go, Relative);
            if (relative != null) Read(() => {
                foreach (var name in new[] { "targetTransform", "targetOffset", "targetAngle", "snapToTerrain", "targetActiveFollowsTrackGroup", "_observedGroupId" })
                    bindings.Add("Track-relative " + name + "=" + Describe(Field(relative, name)));
                dependencies.Add("Track-relative placement and track-group subscription");
            });
            if (TypeName(c) == Milepost) Read(() => {
                foreach (var name in new[] { "prefix", "origin", "mileage" }) bindings.Add("Milepost " + name + "=" + Describe(Field(c, name)));
                dependencies.Add("Milepost origin cache and rendered display");
            });
            if (TypeName(c) == ConstructionScenery) Read(() => {
                var ids = Field(c, "segmentIds") as string[];
                if (ids == null) throw new InvalidOperationException("Construction scenery segment IDs unavailable");
                foreach (var id in ids) {
                    bindings.Add("Construction scenery segment=" + id);
                    if (String.IsNullOrWhiteSpace(id) || (segmentIds != null && !segmentIds.Contains(id))) problems.Add("Construction scenery has a missing segment binding: " + id);
                }
                if (segmentIds == null) problems.Add("Construction segment validity unknown: " + graphError);
                var entries = Field(c, "entries") as IEnumerable;
                if (entries == null) throw new InvalidOperationException("Construction scenery entries unavailable");
                foreach (var entry in entries) {
                    var targets = Field(entry, "targets") as GameObject[];
                    if (targets == null) throw new InvalidOperationException("Construction scenery targets unavailable");
                    var condition = "state=" + Text(Field(entry, "trackConstructionState")) + "; condition=" + Text(Field(entry, "condition"));
                    foreach (var target in targets) {
                        dependencies.Add("Construction target " + Describe(target) + "; " + condition);
                        if (target == null) problems.Add("Missing construction-scenery target");
                    }
                }
                dependencies.Add("Native construction and track-group visibility control; target placement is separate from controller placement");
            });
            var controller = Parent(c, Crossing);
            if (controller != null) Read(() => {
                dependencies.Add("Controller " + Label(controller));
                var sets = Field(controller, "circuitSets") as IEnumerable;
                if (sets == null) throw new InvalidOperationException("Crossing circuit sets unavailable");
                int index = 0;
                foreach (var set in sets) {
                    foreach (var role in new[] { "approachA", "island", "approachB" }) {
                        var span = Field(set, role) as Component;
                        bindings.Add("Circuit " + index + " " + role + "=" + Label(span));
                        if (span == null) problems.Add("Missing circuit " + index + " " + role);
                    }
                    index++;
                }
            });
            if (Get(go, BoardScene.CullerType) != null) dependencies.Add("Culling registration and origin-dependent position");
            if (Get(go, BoardScene.MaskType) != null) dependencies.Add("Terrain mask registration and generated terrain/vegetation output");
            if (family.StartsWith("Bridge", StringComparison.Ordinal)) dependencies.Add("Colliders and culling; separate construction scenery has its own placement");
            var position = go.transform.position;
            var coordinatesAvailable = false;
            Read(() => {
                position = (Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "WorldToGame", position);
                coordinatesAvailable = !Single.IsNaN(position.x) && !Single.IsNaN(position.y) && !Single.IsNaN(position.z) && !Single.IsInfinity(position.x) && !Single.IsInfinity(position.y) && !Single.IsInfinity(position.z);
                if (!coordinatesAvailable) throw new InvalidOperationException("Map coordinates unavailable");
            });
            var rotation = go.transform.rotation;
            var board = go.scene.name == BoardScene.BoardSceneName && BoardScene.PathOf(go.transform) == BoardScene.BoardPath && TypeName(c) == BoardScene.CullerType;
            if (board && (!go.activeInHierarchy || !(Get(go, BoardScene.CullerType) is Behaviour culler) || !culler.enabled || !(Get(go, BoardScene.MaskType) is Behaviour mask) || !mask.enabled))
                problems.Add("Fixed placement unavailable: board, culler and circle mask must be active");
            return new SceneObservation(go, new TargetIdentity(go.scene.name, BoardScene.PathOf(go.transform), TypeName(c), board ? BoardScene.Structure : null), family,
                go.activeInHierarchy, coordinatesAvailable ? new double[] { position.x, position.y, position.z } : Array.Empty<double>(), new double[] { rotation.x, rotation.y, rotation.z, rotation.w },
                bindings, dependencies.Distinct().OrderBy(x => x, StringComparer.Ordinal), problems, board ? "Fixed placement requires active board/culler/mask and successful preflight" : "Inspection only; no mutation adapter for this pattern");
        }
        static void Location(object location, string role, List<string> bindings, List<string> problems, HashSet<string> segmentIds, string graphError) {
            var segment = Text(Field(location, "segmentId"));
            bindings.Add(role + " segment=" + segment + "; end=" + Text(Field(location, "end")) + "; distance=" + Text(Field(location, "distance")));
            if (String.IsNullOrWhiteSpace(segment)) problems.Add(role + " has an empty segment binding");
            else if (segmentIds == null) problems.Add(role + " binding validity unknown: " + graphError);
            else if (!segmentIds.Contains(segment)) problems.Add(role + " broken binding: missing segment " + segment);
            // Existence is not proof that the binding names the intended track.
        }
        static string Describe(object value) => value is Component c ? Label(c) : value is GameObject go ? go.scene.name + "/" + BoardScene.PathOf(go.transform) : Text(value);
    }
}
