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
    internal sealed class NativeInventory : ISteppedInspectionScene {
        const string Marker = "Track.TrackMarker", Span = "Track.TrackSpan", Relative = "Track.TrackRelativePosition";
        const string Crossing = "Track.Crossings.GradeCrossingController", Milepost = "Helpers.MilePost";
        const string ConstructionScenery = "Game.Mow.TrackConstructionScenery";
        internal static readonly string[] Coverage = {
            "Generic track markers, such as yard limits and station signs: set, replace or detach the marker's track location with its graph registration, and place or enable the object. A marker whose activity follows its own track group keeps that subscription coherent through rebinding and accepts an explicit disable that holds through later group activation. Markers with other location consumers are inspection only.",
            "Crossing pads: set or replace the pad marker's track location with its graph registration; the game's pad generation then rebuilds the pad geometry and culling at the new location. Placement and activity stay separate settings; a pad cannot be detached.",
            "Mileposts: marker, origin and display dependencies; model and mileage remain unchanged.",
            "Crossings: controllers, spans, pads, lights, gates and bells; relationships are explicit, never inferred from proximity.",
            "Independent boards: renderer culler with one circle map mask and no track relationship; placement and activity refresh culling, mask registration and generated terrain output.",
            "Bridges: only the retained Ela bridge has the fixed-placement adapter; it moves the bridge root with its colliders and culling.",
            "Construction scenery exposes segment and target relationships and is inspected separately. Generated track-owned trestles remain owned by native track geometry and are never moved by bridge placement.",
            "Passenger stops and map-object anchors: native operations/map-data editing; historical FUSE warning is outside current scope.",
            "Player-created signs: native sign editing; this does not establish inherited scene-sign editing.",
            "Signals, CTC and interlocking edits are outside the supported operation boundary.",
            "Dependencies show components, serialized relationships and direct loaded-scene references; indirect consumers can remain unknown."
        };
        public IEnumerable<string> MapIds => PackDocuments.MapIds();
        // Why a board candidate cannot be edited, from the board capability; null when that capability is unavailable.
        readonly Func<GameObject, string> boardRefusal;
        internal NativeInventory(Func<GameObject, string> boardRefusal = null) { this.boardRefusal = boardRefusal; }
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
            if (type == BoardScene.CullerType && BridgeWorld.SupportedPaths.Contains(BoardScene.PathOf(c.transform))) return "Bridge";
            if (type == BoardScene.CullerType && Get(go, BoardScene.MaskType) != null && Get(go, Marker) == null) return "Independent board";
            if (type == ConstructionScenery && BoardScene.PathOf(c.transform) == "World/Large Scenery/Ela/Ela Bridge Scenery") return "Bridge construction scenery";
            return null;
        }
        // The discovery in one piece: the stepped walk without its pauses, so both read the scene in the same order.
        public IEnumerable<SceneObservation> Discover() => DiscoverInSteps().Where(observation => observation != null);
        public IEnumerable<SceneObservation> DiscoverInSteps() => Steps(boardRefusal);
        // The order and the pauses are DiscoveryWalk's: after the search for loaded components, after each component's
        // loaded check, after each owner's fields, after each component's classification and after the graph lookup. Every
        // observation is one further unit. A caller that pauses reads the scene at different times; a component destroyed
        // since the search, such as a widget of a rebuilt panel, is skipped instead of failing the walk.
        static IEnumerable<SceneObservation> Steps(Func<GameObject, string> boardRefusal) {
            var incoming = new Dictionary<UnityEngine.Object, List<string>>(); var scanErrors = 0;
            // One walk over the fields serves the dependency strings and the marker reference scan. Every field of every
            // owner is read once; a failed read or enumeration counts as one unreadable field for the dependencies, and
            // the marker scan applies its own relevance and fail-closed rules to the same reads.
            ReferenceScan markerReferrers = null;
            string referrerError = null;
            var walk = TrackMarkerWorld.ReferrerWalk((owner, field, value, error) => {
                if (error != null) { scanErrors++; return; }
                try {
                    foreach (var target in References(value)) {
                        if (!incoming.TryGetValue(target, out var refs)) incoming.Add(target, refs = new List<string>());
                        refs.Add("Referenced by " + Label((Component)owner) + "." + field.Name);
                    }
                } catch { scanErrors++; }
            });
            HolderIndex setups = null;
            HashSet<string> segmentIds = null;
            string graphError = null;
            var steps = DiscoveryWalk.Steps<MonoBehaviour>(() => Resources.FindObjectsOfTypeAll<MonoBehaviour>(), Loaded,
                owner => {
                    try { walk.Add(owner); return true; }
                    catch (Exception e) {
                        // The walk stopped early: the dependency strings are incomplete as well.
                        referrerError = e.GetBaseException().Message; scanErrors++; walk = null;
                        return false;
                    }
                },
                () => {
                    if (walk != null) markerReferrers = walk.Finish();
                    setups = TrackMarkerWorld.SetupHolders();
                },
                Family,
                () => {
                    try {
                        var graph = Native.Read(Native.Type("Track.Graph"), "Shared");
                        var lookup = Field(graph, "_segmentsLookup") as IDictionary;
                        if (lookup == null) throw new InvalidOperationException("Track segment lookup unavailable");
                        segmentIds = new HashSet<string>(lookup.Keys.Cast<object>().Select(Text), StringComparer.Ordinal);
                    } catch (Exception e) { graphError = e.GetBaseException().Message; }
                },
                (component, family) => Observe(component, family, incoming, scanErrors, segmentIds, graphError, boardRefusal, markerReferrers, referrerError, setups));
            foreach (var step in steps) yield return step;
        }
        static SceneObservation Observe(Component c, string family, Dictionary<UnityEngine.Object, List<string>> incoming, int scanErrors, HashSet<string> segmentIds, string graphError, Func<GameObject, string> boardRefusal,
            ReferenceScan markerReferrers, string referrerError, HolderIndex setups) {
            var go = c.gameObject; var problems = new List<string>(); var bindings = new List<string>(); var dependencies = new List<string>();
            var components = go.GetComponentsInChildren<Component>(true);
            var parts = new List<HierarchyPart>();
            foreach (var child in components) {
                if (child == null) { problems.Add("Missing script in inspected hierarchy"); continue; }
                if (child is Transform) continue;
                parts.Add(new HierarchyPart(Label(child), child is Behaviour b ? b.enabled : (bool?)null, (child.gameObject.hideFlags & HideFlags.DontSave) != 0,
                    incoming.TryGetValue(child, out var refs) ? refs : null));
            }
            dependencies.AddRange(HierarchyDependencies.Describe(family == "Crossing pad marker", parts));
            if (incoming.TryGetValue(go, out var rootRefs)) dependencies.AddRange(rootRefs);
            if (scanErrors != 0) problems.Add("Direct-reference scan incomplete: " + scanErrors + " unreadable fields");
            void Read(Action action) { try { action(); } catch (Exception e) { problems.Add("Inspection unavailable: " + e.GetBaseException().Message); } }
            var marker = TypeName(c) == Marker ? c : Get(go, Marker);
            if (marker != null) Read(() => {
                bindings.Add("Marker id=" + Text(Field(marker, "id")) + "; type=" + Text(Field(marker, "type")));
                Location(Field(marker, "_location"), "Marker", bindings, problems, segmentIds, graphError);
                Registrations(marker, bindings);
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
            var board = family == "Independent board";
            string refusal = board ? "the board capability is unavailable" : null;
            if (board && boardRefusal != null) Read(() => refusal = boardRefusal(go));
            if (refusal != null) problems.Add("Fixed placement unavailable: " + refusal);
            var bridge = family == "Bridge";
            if (bridge) Read(() => { var problem = BridgeWorld.PatternProblem(go); if (problem != null) problems.Add("Bridge placement unavailable: " + problem); });
            var marked = family == "Track marker";
            string markerRefusal = marked ? "the track-marker capability is unavailable" : null;
            if (marked) Read(() => markerRefusal = referrerError != null ? "the marker reference scan failed: " + referrerError
                : TrackMarkerWorld.Problem(go, markerReferrers, setups));
            if (markerRefusal != null) problems.Add("Track binding unavailable: " + markerRefusal);
            var markerEditable = marked && markerRefusal == null;
            var pad = family == "Crossing pad marker";
            string padRefusal = pad ? "the crossing-pad capability is unavailable" : null;
            if (pad) Read(() => padRefusal = referrerError != null ? "the marker reference scan failed: " + referrerError
                : CrossingPadWorld.PadProblem(go, markerReferrers, setups));
            if (padRefusal != null) problems.Add("Pad binding unavailable: " + padRefusal);
            var padEditable = pad && padRefusal == null;
            var crossing = family == "Crossing controller";
            string coverageRefusal = crossing ? "the crossing-coverage capability is unavailable" : null;
            if (crossing) Read(() => coverageRefusal = CrossingCoverageWorld.Problem(go));
            if (coverageRefusal != null) problems.Add("Crossing coverage unavailable: " + coverageRefusal);
            var coverageEditable = crossing && coverageRefusal == null;
            var structure = board ? BoardScene.Structure : bridge ? BridgeScene.Structure : markerEditable ? TrackMarkerScene.Structure : padEditable ? CrossingPadScene.Structure : coverageEditable ? CrossingCoverageScene.Structure : null;
            var operations = board ? (refusal == null ? "Fixed placement and activity; culling, mask registration and terrain output follow through native refresh" : "Inspection only until corrected: " + refusal)
                : bridge ? "Fixed placement and activity of the bridge root with its colliders; separate track-owned scenery stays unchanged. Requires successful preflight"
                : markerEditable ? "Track binding: set, replace or detach the marker location with its graph registration; fixed placement and activity of the object stay separate settings"
                : marked ? "Inspection only until corrected: " + markerRefusal
                : padEditable ? "Pad binding: set or replace the marker location with its graph registration and regenerate the pad geometry and culling; fixed placement and activity stay separate settings"
                : pad ? "Inspection only until corrected: " + padRefusal
                : coverageEditable ? "Crossing coverage: select tracks for, or detach, each circuit explicitly, or add detection for explicitly selected extra tracks; native spans stay registered and the crossing itself does not move"
                : crossing ? "Inspection only until corrected: " + coverageRefusal
                : "Inspection only; no mutation adapter for this pattern";
            return new SceneObservation(go, new TargetIdentity(go.scene.name, BoardScene.PathOf(go.transform), TypeName(c), structure), family,
                go.activeInHierarchy, coordinatesAvailable ? new double[] { position.x, position.y, position.z } : Array.Empty<double>(), new double[] { rotation.x, rotation.y, rotation.z, rotation.w },
                bindings, dependencies.Distinct().OrderBy(x => x, StringComparer.Ordinal), problems, operations);
        }
        static void Location(object location, string role, List<string> bindings, List<string> problems, HashSet<string> segmentIds, string graphError) {
            var segment = Text(Field(location, "segmentId"));
            bindings.Add(role + " segment=" + segment + "; end=" + Text(Field(location, "end")) + "; distance=" + Text(Field(location, "distance")));
            if (String.IsNullOrWhiteSpace(segment)) problems.Add(role + " has an empty segment binding");
            else if (segmentIds == null) problems.Add(role + " binding validity unknown: " + graphError);
            else if (!segmentIds.Contains(segment)) problems.Add(role + " broken binding: missing segment " + segment);
            // Existence is not proof that the binding names the intended track.
        }
        // The track graph's named-location entries under the marker's ID: the registration that ID lookups find.
        static void Registrations(Component marker, List<string> bindings) {
            var id = Text(Field(marker, "id"));
            var graph = Native.Read(Native.Type("Track.Graph"), "Shared") ?? throw new InvalidOperationException("Track graph unavailable");
            var buckets = Field(graph, "_namedLocationsBySegmentId") as IDictionary ?? throw new InvalidOperationException("Named-location registration unavailable");
            var found = 0;
            foreach (DictionaryEntry bucket in buckets) {
                if (!(bucket.Value is IDictionary entries) || !entries.Contains(id)) continue;
                var named = entries[id]; var at = Field(named, "Location"); var segment = Field(at, "segment");
                bindings.Add("Graph registration in bucket " + Text(bucket.Key) + ": segment=" + (segment == null ? "none" : Text(Field(segment, "id"))) + "; end=" + Text(Field(at, "end")) + "; distance=" + Text(Field(at, "distance")) + "; owner=" + (ReferenceEquals(Field(named, "ObjectValue"), marker) ? "this marker" : "another object"));
                found++;
            }
            if (found == 0) bindings.Add("Graph registration: none");
        }
        static string Describe(object value) => value is Component c ? Label(c) : value is GameObject go ? go.scene.name + "/" + BoardScene.PathOf(go.transform) : Text(value);
    }
}
