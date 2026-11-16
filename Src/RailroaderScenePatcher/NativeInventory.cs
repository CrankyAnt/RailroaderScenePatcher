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
    internal sealed class NativeInventory : ISteppedInspectionScene, IRelatedInspectionScene {
        // Observes one component with what the last discovery learned about the rest of the scene: who references what,
        // the graph's segments and the setup holders. A discovery sets it when that knowledge is complete.
        static Func<Component, string, SceneObservation> observer;
        // The marker reference scan of the last discovery whose walk over the fields completed, or null.
        internal static ReferenceScan LastReferrers { get; private set; }
        // The object itself is read now: its components, placement, activity, bindings and problems. Without a discovery so
        // far, or when the object or its component is gone, there is no observation.
        public SceneObservation Observe(object handle, TargetIdentity identity) {
            var target = handle as GameObject;
            if (target == null || observer == null || identity == null) return null;
            var component = target.GetComponents<Component>().FirstOrDefault(c => c != null && TypeName(c) == identity.Component);
            var family = component == null ? null : Family(component);
            return family == null ? null : observer(component, family);
        }
        public bool Present(object handle) => handle is GameObject target && target != null && target.scene.IsValid() && target.scene.isLoaded;
        // The observed components of the object's hierarchy, which holds the lights, gates and bells of a crossing, and
        // for a crossing the spans this patcher owns in its scene: a coverage edit creates and removes those.
        public IEnumerable<SceneObservation> ObserveRelated(object handle, TargetIdentity identity) {
            var target = handle as GameObject;
            if (target == null || observer == null) return null;
            var roots = new List<GameObject> { target };
            if (Get(target, Crossing) != null)
                roots.AddRange(target.scene.GetRootGameObjects().Where(root => root != null && root.name == CrossingCoverageWorld.RootName));
            var found = new List<SceneObservation>();
            foreach (var root in roots)
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) {
                    if (!Loaded(component)) continue;
                    var family = Family(component);
                    if (family != null) found.Add(observer(component, family));
                }
            return found;
        }
        // Whether the game shifts the object with its moving origin: it shifts the transforms registered as origin
        // targets, with everything below them. A holder of data outside them, such as an equipment setup with its
        // markers, stays where it is in the scene. When no target is registered at all the registration cannot be read,
        // and every object counts as shifted, as the game's own conversion assumes.
        internal static bool MovesWithOrigin(Transform transform) {
            var targets = Helpers.WorldTransformerTargetList.Targets;
            if (targets == null || targets.Count == 0) return true;
            for (var t = transform; t != null; t = t.parent) if (targets.Contains(t)) return true;
            return false;
        }
        // The object's map position (MapOrigin.Position): the inventory shows it and Focus moves the camera to it.
        internal static Vector3 MapPosition(Transform transform) => MapPosition(transform.position, MovesWithOrigin(transform));
        // The MapOrigin rule between scene and map positions, for an object that does or does not move with the origin.
        // Focus, the inventory, the marker capability and the gizmo use it.
        internal static Vector3 MapPosition(Vector3 scene, bool movesWithOrigin) =>
            Vector(MapOrigin.Position(Doubles(scene), movesWithOrigin ? OriginOffset() : null, movesWithOrigin));
        internal static Vector3 ScenePosition(Vector3 map, bool movesWithOrigin) =>
            Vector(MapOrigin.ScenePosition(Doubles(map), movesWithOrigin ? OriginOffset() : null, movesWithOrigin));
        internal static Vector3 MarkPosition(Vector3 map) => Vector(MapOrigin.MarkPosition(Doubles(map), OriginOffset()));
        static double[] OriginOffset() => Doubles((Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "GameToWorld", Vector3.zero));
        static double[] Doubles(Vector3 v) => new double[] { v.x, v.y, v.z };
        static Vector3 Vector(double[] v) => new Vector3((float)v[0], (float)v[1], (float)v[2]);
        const string GameObjectCuller = "Helpers.Culling.GameObjectCuller";
        // The equipment of a crossing whose activity the game switches by camera distance through a culler.
        static readonly HashSet<string> CulledFamilies = new HashSet<string>(StringComparer.Ordinal) { "Crossing light", "Crossing gate", "Crossing bell" };
        const string Marker = "Track.TrackMarker", Span = "Track.TrackSpan", Relative = "Track.TrackRelativePosition";
        const string Crossing = "Track.Crossings.GradeCrossingController", Milepost = "Helpers.MilePost";
        const string ConstructionScenery = "Game.Mow.TrackConstructionScenery", Pile = "Scenery.Pile";
        public IEnumerable<string> MapIds => ModDocuments.MapIds();
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
        // A map object the game spawns carries this component; its track anchor is edited in the native Map Editor.
        const string MapObject = "Game.MapObjectActivation", PhotoController = "RollingStock.BuilderPhotoController";
        static string MarkerKind(Component marker) { try { return Text(Field(marker, "type")); } catch { return null; } }
        // The markers a builder photo rig holds, read once per discovery.
        static HashSet<object> photoMarkers;
        static HashSet<object> PhotoMarkers(bool again = false) {
            if (photoMarkers != null && !again) return photoMarkers;
            var found = new HashSet<object>();
            try {
                var type = Native.Type(PhotoController);
                if (type != null) foreach (var rig in Resources.FindObjectsOfTypeAll(type)) {
                    var held = rig == null ? null : Field(rig, "marker") as Component;
                    if (held != null) found.Add(held);
                }
            } catch { }
            return photoMarkers = found;
        }
        // The car placements of the loaded setups as the last discovery read them. A single object observed later is
        // classified by them, as it takes the reference scan of that discovery. Setups that could not be read classify
        // no marker as a starter placement marker; every marker then carries a note that says so.
        static StarterSetups starters = new StarterSetups(null);
        // Why the last discovery could not read the setups, or null: the Refresh reports it.
        internal static string StartersError => starters.Error;
        static void ReadStarters() {
            try { starters = TrackMarkerWorld.StarterSetups(); }
            catch (Exception e) { starters = StarterSetups.Unreadable(e.GetBaseException().Message); }
        }
        static string Family(Component c) {
            var type = TypeName(c); var go = c.gameObject;
            if (type == Milepost) return "Milepost";
            if (type == Marker) {
                // Kinds the inventory does not list: signals, map objects whose track anchor the native map editor owns, and
                // the builder photo marker. A milepost's marker is part of the Milepost row.
                if (!BindingRules.Listed(MarkerKind(c), Get(go, MapObject) != null, PhotoMarkers().Contains(c)) || Get(go, Milepost) != null) return null;
                // A marker an active setup's car placement holds is a starter placement marker; one only inactive
                // setups hold is never read and stays out of the inventory. A marker no setup holds keeps its kind.
                return starters.Family(c, Get(go, "Track.TrackCurveMeshBuilder") != null ? "Crossing pad marker" : "Track marker");
            }
            if (type == Relative && Get(go, Marker) == null) return "Track-relative scene object";
            if (type == Span) return "Track span";
            // A storage pile of an industry is a scene object the patcher will place; until then it is listed without
            // an edit, in the Inspection only group, instead of being left out.
            if (type == Pile) return "Storage pile";
            if (type == Crossing) return "Crossing controller";
            if ((type == "Track.KeyValueLampMaterialController" || type == "RollingStock.Controls.KeyValueBoolAnimator" || type == "Audio.IntegerLoopingPlayer") && Parent(c, Crossing) != null)
                return type == "Track.KeyValueLampMaterialController" ? "Crossing light" : type == "Audio.IntegerLoopingPlayer" ? "Crossing bell" : "Crossing gate";
            if (type == BoardScene.CullerType && BridgeWorld.SupportedPaths.Contains(BoardScene.PathOf(c.transform))) return "Bridge";
            if (type == BoardScene.CullerType && Get(go, BoardScene.MaskType) != null && Get(go, Marker) == null) return "Independent board";
            if (type == ConstructionScenery && BoardScene.PathOf(c.transform) == "World/Large Scenery/Ela/Ela Bridge Scenery") return "Bridge construction scenery";
            return null;
        }
        // The discovery in one piece: the stepped walk without its pauses, so both read the scene in the same order.
        public IEnumerable<SceneObservation> Discover() => Steps(boardRefusal, true).Where(observation => observation != null);
        public IEnumerable<SceneObservation> DiscoverInSteps() => Steps(boardRefusal, false);
        // The order and the pauses are DiscoveryWalk's: after the search for loaded components, after each component's
        // loaded check, after each owner's fields, after each component's classification and after the graph lookup. Every
        // observation is one further unit. A caller that pauses reads the scene at different times; a component destroyed
        // since the search, such as a widget of a rebuilt panel, is skipped instead of failing the walk.
        // whole is a discovery in one piece, which a snapshot is read from: single-object observation always follows it.
        // A stepped pass reads the fields over many frames, during which components come and go; when that leaves its
        // reference scan incomplete, every marker would read as unsupported. Such a pass still marks the snapshot stale
        // by its own observations, and single-object observation keeps the last complete scan, so an object observed
        // alone meanwhile is not classified by the incomplete one.
        static IEnumerable<SceneObservation> Steps(Func<GameObject, string> boardRefusal, bool whole) {
            var incoming = new Dictionary<UnityEngine.Object, List<string>>(); var scanErrors = 0;
            // A reference in a field Unity does not serialize is working state of a running manager, such as a culling or
            // audio list: it appears and disappears when the camera moves or an object is inspected. It is runtime state.
            var working = new Dictionary<UnityEngine.Object, List<string>>();
            // One walk over the fields serves the dependency strings and the marker reference scan. Every field of every
            // owner is read once; a failed read or enumeration counts as one unreadable field for the dependencies, and
            // the marker scan applies its own relevance and fail-closed rules to the same reads.
            ReferenceScan markerReferrers = null;
            string referrerError = null;
            var culled = new Dictionary<GameObject, string>();
            // The listed objects of cullers that hide their objects now, or whose state is not known yet.
            var hidden = new HashSet<GameObject>();
            var walk = TrackMarkerWorld.ReferrerWalk((owner, field, value, error) => {
                if (error != null) { scanErrors++; return; }
                // What this patcher's own components hold, such as the object the panel revealed last, is working state
                // of the patcher and says nothing about the scene.
                if (owner.GetType().Assembly == typeof(NativeInventory).Assembly) return;
                // The objects a culler switches on and off by camera distance, read from the same walk over the fields.
                if (field.Name == "gameObjects" && owner.GetType().FullName == GameObjectCuller && value is IList switched)
                {
                    var shows = false;
                    try { shows = Text(Field(owner, "_cullingState")) == "Visible"; } catch { }
                    foreach (var item in switched) if (item is GameObject listed && listed != null) { culled[listed] = Label((Component)owner); if (!shows) hidden.Add(listed); }
                }
                try {
                    foreach (var target in References(value)) {
                        var held = field.Authored ? incoming : working;
                        if (!held.TryGetValue(target, out var refs)) held.Add(target, refs = new List<string>());
                        refs.Add((field.Authored ? "Referenced by " : "Referenced at runtime by ") + Label((Component)owner) + "." + field.Name);
                    }
                } catch { scanErrors++; }
            });
            HolderIndex setups = null;
            HashSet<string> segmentIds = null;
            PhotoMarkers(true);
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
                    // The setups first, so the classification below never uses those of an earlier discovery.
                    ReadStarters();
                    LastReferrers = referrerError == null ? markerReferrers : null;
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
                    if (whole || observer == null || (referrerError == null && scanErrors == 0)) observer = (component, family) => Observe(component, family, incoming, working, scanErrors, segmentIds, graphError, boardRefusal, markerReferrers, referrerError, setups, culled, hidden);
                },
                (component, family) => Observe(component, family, incoming, working, scanErrors, segmentIds, graphError, boardRefusal, markerReferrers, referrerError, setups, culled, hidden));
            foreach (var step in steps) yield return step;
        }
        static bool Transient(Transform child, Transform root) {
            for (var t = child; t != null && t != root; t = t.parent) if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) return true;
            return false;
        }
        static SceneObservation Observe(Component c, string family, Dictionary<UnityEngine.Object, List<string>> incoming, Dictionary<UnityEngine.Object, List<string>> working, int scanErrors, HashSet<string> segmentIds, string graphError, Func<GameObject, string> boardRefusal,
            ReferenceScan markerReferrers, string referrerError, HolderIndex setups, Dictionary<GameObject, string> culled, HashSet<GameObject> hidden) {
            var go = c.gameObject; var problems = new List<string>(); var bindings = new List<string>(); var dependencies = new List<string>();
            // The object's own active flag. What a culler switches off by camera distance is no disabling; an object
            // that is off while its culler shows its objects was switched off by the scene or by a patch.
            var enabled = AuthoredActivity.Enabled(go.activeSelf, culled.ContainsKey(go), !hidden.Contains(go));
            // What the game reads whatever the object's activity: listed as a problem also for a disabled object.
            var anyway = new List<string>();
            // The game enables and disables child behaviours while it runs, for example a crossing bell's audio player
            // by distance. Which components exist is a dependency; whether each is enabled is runtime state.
            var runtime = new List<string>();
            var components = go.GetComponentsInChildren<Component>(true);
            var parts = new List<HierarchyPart>();
            // A crossing pad's builder generates its mesh into a container and destroys whatever is there first.
            Transform container = null;
            if (family == "Crossing pad marker") try { container = Field(Get(go, "Track.TrackCurveMeshBuilder"), "meshContainer") as Transform; } catch { }
            foreach (var child in components) {
                if (child == null) { problems.Add("Missing script in inspected hierarchy"); continue; }
                if (child is Transform) continue;
                // An object the game created while running is not saved with the scene: the audio pool parents its
                // sources under a crossing bell while it is audible and takes them back when it is not, and pad
                // generation replaces a crossing pad's mesh objects. Such a child and everything on it is runtime state.
                var below = container != null && child.transform != container && child.transform.IsChildOf(container);
                if (HierarchyDependencies.IsOutput(Transient(child.transform, go.transform), below)) {
                    runtime.Add((below ? "Generated pad object " : "Runtime object ") + Label(child));
                    continue;
                }
                if (HierarchyDependencies.IsRuntimeComponent(TypeName(child))) { runtime.Add("Runtime component " + Label(child)); continue; }
                // Which components exist, and who references them from an authored field, is a dependency; whether a
                // behaviour is enabled is runtime state, so the part carries no enabled value.
                parts.Add(new HierarchyPart(Label(child), null, false, incoming.TryGetValue(child, out var refs) ? refs : null));
                if (child is Behaviour behaviour) runtime.Add(Label(child) + " enabled=" + behaviour.enabled);
                if (working.TryGetValue(child, out var held)) runtime.AddRange(held);
            }
            dependencies.AddRange(HierarchyDependencies.Describe(family == "Crossing pad marker", parts));
            if (incoming.TryGetValue(go, out var rootRefs)) dependencies.AddRange(rootRefs);
            if (working.TryGetValue(go, out var rootHeld)) runtime.AddRange(rootHeld);
            if (scanErrors != 0) problems.Add("Direct-reference scan incomplete: " + scanErrors + " unreadable fields");
            void Read(Action action) { try { action(); } catch (Exception e) { problems.Add("Inspection unavailable: " + e.GetBaseException().Message); } }
            var marker = TypeName(c) == Marker ? c : Get(go, Marker);
            if (marker != null) Read(() => {
                var kind = Text(Field(marker, "type"));
                bindings.Add("Marker id=" + Text(Field(marker, "id")) + "; type=" + kind);
                // What the game needs from this kind's binding decides whether its state is a problem; the setups say
                // whether a new-game car placement reads it whatever its activity, and which ones do.
                var reading = starters.Reading(marker, kind, family == "Crossing pad marker");
                if (reading.Note != null) problems.Add(reading.Note);
                var follower = Get(go, Relative);
                var follows = follower != null && Field(follower, "targetActiveFollowsTrackGroup") is bool flag && flag;
                var before = problems.Count;
                MarkerLocation(Field(marker, "_location"), reading.Need, enabled, follows, bindings, problems, segmentIds, graphError);
                if (reading.Need == BindingNeed.ReadAnyway) anyway.AddRange(problems.Skip(before));
                // Every setup and car placement that reads a starter placement marker, with its cars and wreck setting.
                bindings.AddRange(reading.Readers);
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
            // Crossing equipment sits under objects that a culler switches off when the camera is far away, so whether it
            // is active in the game changes with every camera jump. Its activity is what the scene authored: every
            // object from it upward that no culler switches is switched on. What the cullers do now is runtime state.
            var active = go.activeInHierarchy;
            if (CulledFamilies.Contains(family)) {
                var chain = new List<KeyValuePair<bool, bool>>();
                for (var t = go.transform; t != null; t = t.parent) {
                    var switched = culled.TryGetValue(t.gameObject, out var by);
                    chain.Add(new KeyValuePair<bool, bool>(t.gameObject.activeSelf, switched));
                    if (switched) runtime.Add("Culled by " + by + ": " + BoardScene.PathOf(t) + " active=" + t.gameObject.activeSelf);
                }
                active = AuthoredActivity.Of(chain);
            }
            var position = go.transform.position;
            var coordinatesAvailable = false;
            Read(() => {
                // The map position is the scene position less the origin's offset. For an object the game does not shift
                // with its origin that difference changes with every origin move although nothing moved the object: its
                // position is the scene position, which is its map position at the first origin.
                position = MapPosition(go.transform);
                if (!MovesWithOrigin(go.transform)) runtime.Add("Position does not move with the world origin; it is the position in the scene");
                coordinatesAvailable = !Single.IsNaN(position.x) && !Single.IsNaN(position.y) && !Single.IsNaN(position.z) && !Single.IsInfinity(position.x) && !Single.IsInfinity(position.y) && !Single.IsInfinity(position.z);
                if (!coordinatesAvailable) throw new InvalidOperationException("Map coordinates unavailable");
            });
            var rotation = go.transform.rotation;
            var board = family == "Independent board";
            string refusal = board ? "the board capability is unavailable" : null;
            if (board && boardRefusal != null) Read(() => refusal = boardRefusal(go));
            // Why an object of a supported family cannot be edited is no detected problem: it stands under Supported
            // edits.
            var bridge = family == "Bridge";
            string bridgeRefusal = null;
            if (bridge) Read(() => bridgeRefusal = BridgeWorld.PatternProblem(go));
            var marked = family == "Track marker";
            string markerRefusal = marked ? "the track-marker capability is unavailable" : null;
            if (marked) Read(() => markerRefusal = referrerError != null ? "the marker reference scan failed: " + referrerError
                : TrackMarkerWorld.Problem(go, markerReferrers, setups));
            var markerEditable = marked && markerRefusal == null;
            var pad = family == "Crossing pad marker";
            string padRefusal = pad ? "the crossing-pad capability is unavailable" : null;
            if (pad) Read(() => padRefusal = referrerError != null ? "the marker reference scan failed: " + referrerError
                : CrossingPadWorld.PadProblem(go, markerReferrers, setups));
            var padEditable = pad && padRefusal == null;
            var crossing = family == "Crossing controller";
            string coverageRefusal = crossing ? "the crossing-coverage capability is unavailable" : null;
            if (crossing) Read(() => coverageRefusal = CrossingCoverageWorld.Problem(go));
            var coverageEditable = crossing && coverageRefusal == null;
            // A kind no supported family covers is inspection only, which is another state than an object of a supported
            // family that cannot be edited: a marker of another type than Generic, and every family without an edit.
            // A starter placement marker is a supported kind with its own workflow.
            var starter = family == StarterPlacements.Family;
            string only = null;
            if (marked) Read(() => {
                var type = MarkerKind(marker);
                if (type != "Generic") only = CommandBand.NotYetSupported("track markers of type " + type);
            });
            else if (!board && !bridge && !pad && !crossing && !starter) only = CommandBand.NotYetSupported("the " + family + " family");
            if (only != null) markerEditable = false;
            var structure = only != null ? null : board ? BoardScene.Structure : bridge ? BridgeScene.Structure : markerEditable ? TrackMarkerScene.Structure : padEditable ? CrossingPadScene.Structure : coverageEditable ? CrossingCoverageScene.Structure : null;
            var operations = only != null ? only : starter ? StarterPlacements.Operations : board ? (refusal == null ? "Fixed placement and activity; culling, mask registration and terrain output follow through native refresh" : CommandBand.NotEditable(refusal))
                : bridge ? (bridgeRefusal == null ? "Fixed placement and activity of the bridge root with its colliders; separate track-owned scenery stays unchanged. Requires successful preflight" : CommandBand.NotEditable(bridgeRefusal))
                : markerEditable ? "Track binding: set, replace or detach the marker location with its graph registration; fixed placement and activity of the object stay separate settings"
                : marked ? CommandBand.NotEditable(markerRefusal)
                : padEditable ? CommandBand.PadOperations
                : pad ? CommandBand.NotEditable(padRefusal)
                : coverageEditable ? "Crossing coverage: select tracks for, or detach, each circuit explicitly, or add detection for explicitly selected extra tracks; native spans stay registered and the crossing itself does not move"
                : CommandBand.NotEditable(coverageRefusal);
            // A patch names its target by scene, path and component. Where more than one loaded object holds those, an
            // edit would be refused as not resolving uniquely: the object says so under Supported edits and offers none.
            if (only == null && structure != null) Read(() => {
                var holders = BoardScene.LoadedAt(go.scene.name, BoardScene.PathOf(go.transform)).Count(held => held != null && Get(held, TypeName(c)) != null);
                var shared = CommandBand.SharedPath(holders);
                if (shared != null) { structure = null; operations = CommandBand.NotEditable(shared); }
            });
            // What the inspection could not read or check is a note, whatever the object's activity. Of the rest, a
            // disabled object keeps only what the game reads anyway.
            var notes = problems.Where(BindingRules.IsNote).ToArray();
            var defects = BindingRules.Problems(enabled, problems.Where(line => !BindingRules.IsNote(line)), anyway.Where(line => !BindingRules.IsNote(line)));
            return new SceneObservation(go, new TargetIdentity(go.scene.name, BoardScene.PathOf(go.transform), TypeName(c), structure), family,
                active, coordinatesAvailable ? new double[] { position.x, position.y, position.z } : Array.Empty<double>(), new double[] { rotation.x, rotation.y, rotation.z, rotation.w },
                bindings, dependencies.Distinct().OrderBy(x => x, StringComparer.Ordinal), defects, operations, runtime.Distinct().OrderBy(x => x, StringComparer.Ordinal), enabled, notes, only);
        }
        // A marker's binding, judged by what the game needs from its kind. A span's two locations keep Location below.
        static void MarkerLocation(object location, BindingNeed need, bool enabled, bool followsGroup, List<string> bindings, List<string> problems, HashSet<string> segmentIds, string graphError) {
            var segment = Text(Field(location, "segmentId"));
            bindings.Add("Marker segment=" + segment + "; end=" + Text(Field(location, "end")) + "; distance=" + Text(Field(location, "distance")));
            var resolves = String.IsNullOrWhiteSpace(segment) ? false : segmentIds == null ? (bool?)null : segmentIds.Contains(segment);
            var problem = BindingRules.Problem(need, enabled, "Marker", segment, resolves) ?? BindingRules.Unknown(need, enabled, "Marker", segment, resolves, graphError);
            if (problem != null) problems.Add(problem);
            var note = BindingRules.Note(need, enabled, segment, resolves, followsGroup);
            if (note != null) bindings.Add("Marker binding note: " + note);
            // Existence is not proof that the binding names the intended track.
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
