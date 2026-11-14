// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The Generic track-marker pattern: a scene object whose TrackMarker location names a segment, end and distance.
    // The serialized location is the relationship; the track graph's named-location bucket for that segment is its
    // registration, which the native marker adds when it is enabled and removes when it is disabled. Setting the
    // location does not change that registration, so this adapter performs the matching remove and add steps.
    // Crossing pads use CrossingPadWorld; mileposts and crossing markers keep their own later slices; signals are outside scope.
    // A colocated TrackRelativePosition whose activity follows the track group subscribes to the graph's group event for
    // the bound segment's group. This adapter observes the group again after a rebinding and holds an explicitly disabled
    // object inactive by replacing the relative position's activation callback, so a group activation cannot reach it.
    internal class TrackMarkerWorld : ITrackMarkerWorld, ITrackMarkerDiagnostics, ITargetConfirmation, IPlacementPattern {
        readonly NativeMarkerWarnings warningHistory = new NativeMarkerWarnings();
        readonly NativeCurrentWarnings currentWarnings = new NativeCurrentWarnings();
        internal const string MarkerType = "Track.TrackMarker";
        const string GraphType = "Track.Graph", SegmentType = "Track.TrackSegment", LocationType = "Track.Location";
        internal const string Relative = "Track.TrackRelativePosition", Milepost = "Helpers.MilePost";
        internal const string PadBuilder = "Track.TrackCurveMeshBuilder";
        const string Crossing = "Track.Crossings.GradeCrossingController";
        const string MapBuilderType = "UI.Map.MapBuilder", MapLabelType = "UI.Map.MapLabel", SetupType = "Game.Progression.SetupDescriptor";
        readonly Type marker, graph, segment, location, end, transformer, culler, mapBuilder, mapLabel;
        readonly PropertyInfo markerLocation;
        readonly MethodInfo addLabel, removeLabel, labelGroup, unsubscribe, observe;
        readonly FieldInfo groupEvent, setTargetActive;
        // The explicit-disable hold installed per relative position, so a hold can be recognised and released.
        readonly System.Runtime.CompilerServices.ConditionalWeakTable<Component, Action<bool>> holds = new System.Runtime.CompilerServices.ConditionalWeakTable<Component, Action<bool>>();
        // Direct references to marker components, scanned at most once per frame.
        ReferenceScan referrers;
        int referrersFrame = -1;
        GameObject enablingTarget;
        // Unity can invoke OnEnable before scene.isLoaded becomes true. Limit that allowance to the current native check.
        internal T DuringEnable<T>(GameObject target, Func<T> action) {
            var previous = enablingTarget; enablingTarget = target; referrersFrame = -1;
            try { return action(); }
            finally { enablingTarget = previous; referrersFrame = -1; }
        }
        bool VisibleForCheck(GameObject target) => Loaded(target) || enablingTarget != null && target != null && target.scene.IsValid();
        internal TrackMarkerWorld() {
            marker = Native.Type(MarkerType); graph = Native.Type(GraphType); segment = Native.Type(SegmentType); location = Native.Type(LocationType);
            transformer = Native.Type("Helpers.WorldTransformer"); culler = Native.Type(BoardScene.CullerType);
            if (marker == null || graph == null || segment == null || location == null || transformer == null || culler == null) throw new InvalidOperationException("Track-marker prerequisites unavailable");
            end = segment.GetNestedType("End", Native.Flags) ?? throw new MissingMemberException(SegmentType, "End");
            markerLocation = marker.GetProperty("Location", Native.Flags) ?? throw new MissingMemberException(MarkerType, "Location");
            if (markerLocation.GetSetMethod(true) == null) throw new MissingMethodException(MarkerType, "set_Location");
            foreach (var field in new[] { "id", "type", "_location", "OnLocationChanged" }) if (FieldOf(marker, field) == null) throw new MissingFieldException(MarkerType, field);
            var serializedLocation = FieldOf(marker, "_location").FieldType;
            if (FieldOf(serializedLocation, "segmentId")?.FieldType != typeof(string) || FieldOf(serializedLocation, "distance")?.FieldType != typeof(float) || FieldOf(serializedLocation, "end")?.FieldType != end)
                throw new MissingMemberException(MarkerType, "serialized location fields");
            if (marker.GetMethod("InvalidateCache", Native.Flags, null, Type.EmptyTypes, null)?.ReturnType != typeof(void)) throw new MissingMethodException(MarkerType, "InvalidateCache");
            if (marker.GetMethod("OnEnable", Native.Flags, null, Type.EmptyTypes, null)?.ReturnType != typeof(void)) throw new MissingMethodException(MarkerType, "OnEnable");
            if (FieldOf(graph, "_namedLocationsBySegmentId") == null) throw new MissingFieldException(GraphType, "_namedLocationsBySegmentId");
            foreach (var method in new[] { "GetSegment", "AddNamedLocation", "RemoveNamedLocation" }) if (graph.GetMethods(Native.Flags).Count(m => m.Name == method) != 1) throw new MissingMethodException(GraphType, method);
            if (segment.GetMethod("GetLength", Native.Flags, null, Type.EmptyTypes, null) == null || FieldOf(segment, "id") == null) throw new MissingMemberException(SegmentType, "GetLength or id");
            if (location.GetConstructor(Native.Flags, null, new[] { segment, typeof(float), end }, null) == null) throw new MissingMethodException(LocationType, ".ctor");
            foreach (var method in new[] { "WorldToGame", "GameToWorld" })
                if (transformer.GetMethods(Native.Flags).Count(m => m.Name == method && m.GetParameters().Length == 1) != 1) throw new MissingMethodException(method);
            if (culler.GetMethod("RequestUpdateCullingPosition", Native.Flags) == null) throw new MissingMethodException("Culling refresh unavailable");
            // Map labels below a marker are dimmed by the track group of that marker's location; the map builder caches that group when a label registers.
            mapBuilder = Native.Type(MapBuilderType); mapLabel = Native.Type(MapLabelType);
            if (mapBuilder == null || mapLabel == null) throw new InvalidOperationException("Map-label prerequisites unavailable");
            addLabel = mapBuilder.GetMethod("Add", Native.Flags, null, new[] { mapLabel }, null) ?? throw new MissingMethodException(MapBuilderType, "Add(MapLabel)");
            removeLabel = mapBuilder.GetMethod("Remove", Native.Flags, null, new[] { mapLabel }, null) ?? throw new MissingMethodException(MapBuilderType, "Remove(MapLabel)");
            labelGroup = mapBuilder.GetMethod("TrackGroupIdFor", Native.Flags, null, new[] { mapLabel }, null) ?? throw new MissingMethodException(MapBuilderType, "TrackGroupIdFor");
            if (FieldOf(mapBuilder, "_mapLabels") == null || mapBuilder.GetProperty("Shared", Native.Flags) == null) throw new MissingMemberException(MapBuilderType, "_mapLabels or Shared");
            // The group-following steps of the relative position and the graph's group event.
            var relative = Native.Type(Relative) ?? throw new InvalidOperationException("Track-relative position unavailable");
            foreach (var field in new[] { "targetTransform", "targetActiveFollowsTrackGroup", "_observing", "_observedGroupId" }) if (FieldOf(relative, field) == null) throw new MissingFieldException(Relative, field);
            setTargetActive = FieldOf(relative, "_setTargetActive");
            if (setTargetActive == null || setTargetActive.FieldType != typeof(Action<bool>)) throw new MissingFieldException(Relative, "_setTargetActive");
            unsubscribe = relative.GetMethod("UnsubscribeFromGroupEvents", Native.Flags, null, Type.EmptyTypes, null) ?? throw new MissingMethodException(Relative, "UnsubscribeFromGroupEvents");
            observe = relative.GetMethod("ObserveTrackGroup", Native.Flags, null, Type.EmptyTypes, null) ?? throw new MissingMethodException(Relative, "ObserveTrackGroup");
            if (relative.GetMethod("OnGraphGroupEnabledDidChange", Native.Flags) == null) throw new MissingMethodException(Relative, "OnGraphGroupEnabledDidChange");
            groupEvent = FieldOf(graph, "GroupEnabledDidChange");
            if (groupEvent == null || !typeof(Delegate).IsAssignableFrom(groupEvent.FieldType)) throw new MissingFieldException(GraphType, "GroupEnabledDidChange");
            if (FieldOf(graph, "enabledGroupIds") == null || FieldOf(segment, "groupId") == null) throw new MissingFieldException(GraphType, "enabledGroupIds or groupId");
            var setup = Native.Type(SetupType);
            if (setup == null || FieldOf(setup, "placements") == null) throw new MissingMemberException(SetupType, "placements");
        }
        public IEnumerable<string> MapIds => PackDocuments.MapIds();
        static FieldInfo FieldOf(Type type, string name) {
            for (var t = type; t != null; t = t.BaseType) { var f = t.GetField(name, Native.Flags | BindingFlags.DeclaredOnly); if (f != null) return f; }
            return null;
        }
        internal static object Field(object owner, string name) => (FieldOf(owner.GetType(), name) ?? throw new MissingFieldException(owner.GetType().FullName, name)).GetValue(owner);
        internal static bool Loaded(GameObject g) => g != null && g.scene.IsValid() && g.scene.isLoaded;
        internal static Component Get(GameObject go, string type) => go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().FullName == type);
        static object Graph() => Native.Read(Native.Type(GraphType), "Shared") ?? throw new InvalidOperationException("The track graph is unavailable");
        internal Component MarkerOf(object target) => ((GameObject)target).GetComponent(marker) ?? throw new InvalidOperationException("The object no longer holds a track marker");
        // The loaded objects at the path that hold a marker. Starting from the marker components avoids building the path of
        // every loaded object; an object with several markers is one match, as before.
        // Crossing pads belong to the crossing-pad capability, so the two capabilities never claim the same object.
        public IReadOnlyList<object> Resolve(TargetIdentity identity) => Markers(identity, Belongs);
        protected virtual bool Belongs(GameObject g) => Get(g, PadBuilder) == null;
        // The held object still exists in its loaded scene at the identity's path and still belongs to this capability.
        // This reads one object, never the scene; uniqueness is checked again by the next resolution.
        public bool StillIdentifies(object target, TargetIdentity identity) {
            var g = target as GameObject;
            return g != null && identity.Component == MarkerType && Loaded(g) && g.scene.name == identity.Scene && g.GetComponent(marker) != null && Belongs(g)
                && BoardScene.PathOf(g.transform) == identity.Path;
        }
        protected IReadOnlyList<object> Markers(TargetIdentity identity, Func<GameObject, bool> belongs) {
            if (identity.Component != MarkerType) return Array.Empty<object>();
            // The walk goes down the identity's path. While a marker is being enabled its scene is not loaded yet and the
            // scene manager does not list it: then every marker component is searched.
            if (enablingTarget == null) return BoardScene.LoadedAt(identity.Scene, identity.Path).Where(g => Loaded(g) && g.GetComponent(marker) != null && belongs(g)).Cast<object>().ToArray();
            return Resources.FindObjectsOfTypeAll(marker).OfType<Component>().Where(c => c != null).Select(c => c.gameObject).Distinct()
                .Where(g => VisibleForCheck(g) && g.scene.name == identity.Scene && BoardScene.PathOf(g.transform) == identity.Path && belongs(g)).Cast<object>().ToArray();
        }
        // The reference scan of a discovery the owner vouches for, or null. The check of a marker reads every field of
        // every loaded component, which is most of a discovery; the editor panel supplies the scan of the discovery its
        // snapshot was compared with instead. Runtime application and the check while a marker is enabled never use it.
        internal Func<ReferenceScan> KnownReferrers;
        public virtual string PatternProblem(object target) =>
            Problem(target as GameObject, CurrentReferrers(), HolderIndex.Lazy(() => SetupPlacementMarkers(VisibleForCheck)), ReferenceEquals(target, enablingTarget));
        // Direct references to marker components from the objects the current check may see: the scan the owner vouches
        // for, or one of the scene, made at most once per frame.
        protected ReferenceScan CurrentReferrers() {
            var known = enablingTarget == null ? KnownReferrers?.Invoke() : null;
            if (known != null) return known;
            if (referrersFrame != Time.frameCount) { referrers = Referrers(Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => c != null && VisibleForCheck(c.gameObject))); referrersFrame = Time.frameCount; }
            return referrers;
        }
        // Every direct field or list reference from a loaded component to a marker component. A relevant field that
        // cannot be read leaves the scan incomplete, which refuses every marker. An observer receives every field read of
        // the same pass, so the inventory's dependency scan needs no second walk over the fields.
        internal static ReferenceScan Referrers(IEnumerable<MonoBehaviour> owners, Action<object, ReferenceField, object, Exception> observe = null) {
            var walk = ReferrerWalk(observe);
            foreach (var owner in owners) walk.Add(owner);
            return walk.Finish();
        }
        // The same scan taken one owner at a time, for a discovery that pauses between owners.
        internal static ReferenceScan.Walk ReferrerWalk(Action<object, ReferenceField, object, Exception> observe = null) =>
            ReferenceScan.Begin(owner => Fields(owner), item => item is Component c && c != null && c.GetType().FullName == MarkerType, Describe, observe);
        // Field descriptors and their relevance per type, in declaration order from the most derived type. Type metadata
        // does not change during a load; every scan still reads each field of each owner afresh.
        static readonly Dictionary<Type, KeyValuePair<FieldInfo, bool>[]> descriptors = new Dictionary<Type, KeyValuePair<FieldInfo, bool>[]>();
        static KeyValuePair<FieldInfo, bool>[] Descriptors(Type owner) {
            if (descriptors.TryGetValue(owner, out var known)) return known;
            var found = new List<KeyValuePair<FieldInfo, bool>>();
            for (var type = owner; type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    found.Add(new KeyValuePair<FieldInfo, bool>(field, ReferenceScan.CanHold(field.FieldType, typeof(Component))));
            descriptors[owner] = known = found.ToArray();
            return known;
        }
        static IEnumerable<ReferenceField> Fields(object owner) {
            foreach (var descriptor in Descriptors(owner.GetType())) {
                var f = descriptor.Key;
                yield return new ReferenceField(f.Name, descriptor.Value, () => f.GetValue(owner), ReferenceScan.IsSerialized(f));
            }
        }
        // The markers that new-game setup car placements hold, walked in the order the per-marker check walked them: each
        // setup's placements, then each placement's marker. The walk runs once, at the first marker that reaches the check.
        internal static HolderIndex SetupHolders() => HolderIndex.Lazy(() => SetupPlacementMarkers(Loaded));
        static IEnumerable<object> SetupPlacementMarkers(Func<GameObject, bool> include) {
            foreach (var setup in Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => c != null && include(c.gameObject) && c.GetType().FullName == SetupType))
                if (Field(setup, "placements") is IEnumerable placements)
                    foreach (var placement in placements)
                        if (placement != null) yield return Field(placement, "marker");
        }
        static string Describe(object owner) => owner is Component c && c != null ? c.GetType().FullName + " on " + BoardScene.PathOf(c.transform) : System.Convert.ToString(owner);
        // Why the object does not conform to the supported Generic-marker pattern, or null. Shared with the inventory,
        // which supplies the reference scan and setup index of its discovery.
        // The pattern without the consumer scans, for an update that changes only the pose of a held object.
        public virtual string PlacementProblem(object target) => Problem(target as GameObject, null, null, ReferenceEquals(target, enablingTarget), true);
        internal static string Problem(GameObject go, ReferenceScan references, HolderIndex setups, bool enabling = false, bool placementOnly = false) {
            if (!Loaded(go) && !(enabling && go != null && go.scene.IsValid())) return "This object has no implemented track-marker capability";
            var markers = go.GetComponents<Component>().Where(c => c != null && c.GetType().FullName == MarkerType).ToArray();
            if (markers.Length != 1) return markers.Length == 0 ? "The object holds no track marker" : "The object holds several track markers";
            var m = markers[0];
            if (go.GetComponents<Component>().Any(c => c == null)) return "Missing script on the marker object";
            if (Get(go, Milepost) != null) return "Milepost markers keep their milepost-specific slice; this binding operation does not apply";
            if (Get(go, PadBuilder) != null) return "Crossing pad markers use the crossing-pad capability; this Generic-marker operation does not apply";
            for (var t = go.transform; t != null; t = t.parent) if (Get(t.gameObject, Crossing) != null) return "Markers inside a crossing keep the crossing-specific slice";
            var type = System.Convert.ToString(Field(m, "type"));
            if (type != "Generic") return "Only Generic track markers are supported; this marker is " + type;
            return Consumers(go, m, references, setups, (owner, field) => owner is Component c && c.gameObject == go && c.GetType().FullName == Relative && field == "_marker", placementOnly);
        }
        // The consumer checks shared by the Generic-marker and crossing-pad patterns: the marker's ID, location listeners,
        // group-following activity, new-game car placements and direct holders other than the allowed colocated ones.
        // A placement-only check reads this object alone: the setup placements and the direct-reference scan concern
        // consumers of the marker's location, which a pose update does not change.
        internal static string Consumers(GameObject go, Component m, ReferenceScan references, HolderIndex setups, Func<object, string, bool> allowed, bool placementOnly = false) {
            if (String.IsNullOrWhiteSpace(Field(m, "id") as string)) return "The marker has no ID; its graph registration cannot be identified";
            // A listener of location changes is a dependent consumer this adapter does not refresh.
            if (Field(m, "OnLocationChanged") is Delegate listeners && listeners.GetInvocationList().Length != 0) return "Another component consumes this marker's location changes; its dependent state is not supported";
            var relative = Get(go, Relative);
            if (relative != null && Field(relative, "targetActiveFollowsTrackGroup") is bool follows && follows) {
                // The adapter supports a follower whose relative position activates this object itself, through the native step or the hold.
                if (!ReferenceEquals(Field(relative, "targetTransform"), go.transform)) return "The object's activity follows a track group through another object; only a follower that activates itself is supported";
                if (Field(relative, "_setTargetActive") is Delegate callback && !(callback.Target is HoldTarget)) return "Another component replaced the track-group activation of this object; that consumer is not supported";
            }
            // A new-game setup car placement reads its marker's location when the setup runs; its timing relative to patch application is unverified.
            if (placementOnly) return null;
            if ((setups ?? SetupHolders()).Holds(m)) return "A new-game car placement reads this marker's location; that consumer is not supported";
            // The colocated relative position holds its marker; any other holder of the component may cache what it read.
            if (references == null) return "The direct-reference scan is unavailable; other holders of this marker are unknown";
            return references.Refusal(m, allowed, Describe);
        }
        // The native marker location (a boxed Track.Location), or null when it does not resolve.
        protected object ResolvedLocation(object target) => markerLocation.GetValue(MarkerOf(target));
        public TrackLocation Location(object target) {
            var serialized = Field(MarkerOf(target), "_location");
            var id = Field(serialized, "segmentId") as string;
            if (String.IsNullOrEmpty(id)) return null;
            return new TrackLocation(id, End(Field(serialized, "end")), (float)Field(serialized, "distance"));
        }
        static string End(object value) => System.Convert.ToString(value) == "A" ? "a" : "b";
        object Segment(string id) => Native.Call(Graph(), "GetSegment", id);
        // Segment IDs are the keys of the graph's segment lookup, so a loaded segment is either unique or missing.
        public IReadOnlyList<double> SegmentLengths(string id) {
            var found = Segment(id);
            return found == null ? Array.Empty<double>() : new double[] { (float)Native.Call(found, "GetLength") };
        }
        public IReadOnlyList<MarkerRegistration> Registrations(object target) {
            var m = MarkerOf(target);
            var id = (string)Field(m, "id");
            var result = new List<MarkerRegistration>();
            foreach (DictionaryEntry bucket in (IDictionary)Field(Graph(), "_namedLocationsBySegmentId")) {
                var entries = (IDictionary)bucket.Value;
                if (!entries.Contains(id)) continue;
                var named = entries[id];
                var at = Field(named, "Location");
                var owner = Field(named, "ObjectValue");
                var seg = Field(at, "segment");
                result.Add(new MarkerRegistration((string)bucket.Key, new TrackLocation(seg == null ? null : (string)Field(seg, "id"), End(Field(at, "end")), (float)Field(at, "distance")), ReferenceEquals(owner, m)));
            }
            return result;
        }
        public bool Registers(object target) { var m = MarkerOf(target); return ((Behaviour)m).isActiveAndEnabled; }
        public void SetLocation(object target, TrackLocation value) {
            // Preserve captured missing references; resolved locations use the native setter and its cache notifications.
            NativeMarkerLocation.Write(MarkerOf(target), value, value == null ? null : Segment(value.Segment), location, end);
        }
        // Crossing pads share this: a pad marker emits the same native warning for a binding that is not on the track.
        public void AfterVerifiedState(object target) {
            try {
                var m = MarkerOf(target);
                var id = (string)Field(m, "id");
                if (String.IsNullOrEmpty(id) || Resources.FindObjectsOfTypeAll(marker).OfType<Component>().Count(c => c != null && c.gameObject.scene.IsValid() && (string)Field(c, "id") == id) != 1) return;
                var resolved = markerLocation.GetValue(m);
                var valid = resolved != null && (bool)Native.Read(resolved, "IsValid");
                // A missing segment still has a serialized reference; an explicit detach does not.
                var serializedReference = Location(target);
                warningHistory.Verified(target, valid, Registers(target) && serializedReference != null, currentWarnings, () => {
                    var collector = Game.Diagnostics.MessageCollector.Shared;
                    if (collector == null) return null;
                    var serialized = Field(m, "_location");
                    var description = "Track marker " + Field(m, "id") + " is not on the track at " + Field(serialized, "segmentId") + " + " + ((float)Field(serialized, "distance")).ToString("F1", System.Globalization.CultureInfo.CurrentCulture) + "m and will have no effect.";
                    var emitted = new List<CurrentWarning>();
                    Action<Game.Diagnostics.DiagnosticMessage> observe = message => {
                        if (message.Severity == Game.Diagnostics.MessageSeverity.Warning && message.Source == "System" && message.DeduplicationKey == (string)Field(m, "id") && message.Originator == m.name && message.Description == description)
                            emitted.Add(NativeCurrentWarnings.Snapshot(message));
                    };
                    collector.OnMessageAdded += observe; collector.OnMessageUpdated += observe;
                    try { NativeRepairHooks.CheckRestoredBinding(m, () => Native.Call(m, "OnEnable")); }
                    finally { collector.OnMessageAdded -= observe; collector.OnMessageUpdated -= observe; }
                    return emitted.Count == 1 ? emitted[0] : null;
                });
            } catch (Exception e) { Debug.LogWarning("Scene Patcher could not reconcile the restored marker's native warning; current rows remain: " + e.GetBaseException().Message); }
        }
        object Builder() => Native.Read(mapBuilder, "Shared");
        // The registered map labels in this object's hierarchy; the map builder caches the track group of the nearest marker location above each.
        IEnumerable<Component> Labels(object target) {
            var builder = Builder() as UnityEngine.Object;
            if (builder == null) return Enumerable.Empty<Component>();
            var registered = (IDictionary)Field(builder, "_mapLabels");
            return ((GameObject)target).GetComponentsInChildren(mapLabel, true).Where(l => l != null && registered.Contains(l));
        }
        // Re-registers each label so the map builder recomputes its track group and dimming from the new location.
        public void RefreshDependents(object target) {
            var builder = Builder();
            foreach (var label in Labels(target).ToArray()) { removeLabel.Invoke(builder, new object[] { label }); addLabel.Invoke(builder, new object[] { label }); }
        }
        public string DependentProblem(object target) {
            var builder = Builder() as UnityEngine.Object;
            if (builder == null) return null;
            var registered = (IDictionary)Field(builder, "_mapLabels");
            foreach (var label in Labels(target)) {
                var cached = registered[label] as string;
                var current = labelGroup.Invoke(null, new object[] { label }) as string;
                if (cached != current) return "the map label " + BoardScene.PathOf(label.transform) + " is dimmed by track group " + (cached ?? "none") + " instead of " + (current ?? "none");
            }
            return null;
        }
        public void Unregister(object target) => Native.Call(Graph(), "RemoveNamedLocation", (string)Field(MarkerOf(target), "id"));
        // The registration step of the native marker's OnEnable, for a location the adapter has verified.
        public void Register(object target) {
            var m = MarkerOf(target);
            var current = markerLocation.GetValue(m) ?? throw new InvalidOperationException("The marker location does not resolve");
            Native.Call(Graph(), "AddNamedLocation", (string)Field(m, "id"), current, System.Convert.ToInt32(Field(m, "type")), m);
        }
        sealed class HoldTarget { internal GameObject Target; internal void Keep(bool requested) { if (Target != null && Target.activeSelf) Target.SetActive(false); } }
        Component Follower(object target) {
            var relative = Get((GameObject)target, Relative);
            return relative != null && Field(relative, "targetActiveFollowsTrackGroup") is bool follows && follows ? relative : null;
        }
        public GroupActivity GroupActivity(object target) {
            var relative = Follower(target);
            if (relative == null) return null;
            var held = setTargetActive.GetValue(relative) is Delegate callback && callback.Target is HoldTarget;
            var handlers = groupEvent.GetValue(Graph()) as Delegate;
            var subscriptions = handlers == null ? 0 : handlers.GetInvocationList().Count(d => ReferenceEquals(d.Target, relative) && d.Method.Name == "OnGraphGroupEnabledDidChange");
            var current = markerLocation.GetValue(MarkerOf(target));
            var seg = current == null ? null : Field(current, "segment");
            var group = seg == null ? null : Field(seg, "groupId") as string;
            var enabled = String.IsNullOrEmpty(group) || ((IList)Field(Graph(), "enabledGroupIds")).Contains(group);
            return new GroupActivity(held, (bool)Field(relative, "_observing"), Field(relative, "_observedGroupId") as string, subscriptions, seg != null, group, enabled);
        }
        // The native UnsubscribeFromGroupEvents and ObserveTrackGroup of the relative position.
        public void ObserveGroup(object target) {
            var relative = Follower(target) ?? throw new InvalidOperationException("The object's activity does not follow a track group");
            unsubscribe.Invoke(relative, null);
            observe.Invoke(relative, null);
        }
        public void HoldDisabled(object target, bool hold) {
            var relative = Follower(target) ?? throw new InvalidOperationException("The object's activity does not follow a track group");
            if (!hold) { setTargetActive.SetValue(relative, null); holds.Remove(relative); return; }
            if (!holds.TryGetValue(relative, out var callback)) { callback = new HoldTarget { Target = (GameObject)target }.Keep; holds.Add(relative, callback); }
            setTargetActive.SetValue(relative, callback);
        }
        public double[] Position(object target) { var p = Origin("WorldToGame", ((GameObject)target).transform.position); return new double[] { p.x, p.y, p.z }; }
        public double[] Rotation(object target) { var r = ((GameObject)target).transform.rotation; return new double[] { r.x, r.y, r.z, r.w }; }
        public bool Active(object target) => ((GameObject)target).activeSelf;
        public void SetPose(object target, double[] position, double[] rotation) {
            var go = (GameObject)target;
            var p = new Vector3(Checked(position[0]), Checked(position[1]), Checked(position[2]));
            var q = new Quaternion(Checked(rotation[0]), Checked(rotation[1]), Checked(rotation[2]), Checked(rotation[3]));
            if (Math.Abs(Quaternion.Dot(q, q) - 1f) > 0.001f) throw new InvalidOperationException("Rotation must be a unit quaternion");
            var local = Origin("GameToWorld", p);
            if ((Origin("WorldToGame", local) - p).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Map/origin conversion failed its round trip");
            go.transform.SetPositionAndRotation(local, q);
            foreach (var c in go.GetComponentsInChildren(culler, true).OfType<Behaviour>().Where(c => c.enabled && c.gameObject.activeInHierarchy))
                Native.Call(c, "RequestUpdateCullingPosition");
            Physics.SyncTransforms();
        }
        // The native marker lifecycle registers or removes the marker when the object's activity changes.
        public void SetActive(object target, bool active) => ((GameObject)target).SetActive(active);
        Vector3 Origin(string direction, Vector3 position) {
            if (Native.Read(transformer, "Shared") == null) throw new InvalidOperationException("Floating origin unavailable");
            var value = (Vector3)Native.Call(transformer, direction, position);
            if (Single.IsNaN(value.x) || Single.IsNaN(value.y) || Single.IsNaN(value.z) || Single.IsInfinity(value.x) || Single.IsInfinity(value.y) || Single.IsInfinity(value.z)) throw new InvalidOperationException("Invalid origin conversion");
            return value;
        }
        static float Checked(double value) { var f = (float)value; if (Single.IsNaN(f) || Single.IsInfinity(f)) throw new InvalidOperationException("Coordinate exceeds native precision"); return f; }
    }
}
