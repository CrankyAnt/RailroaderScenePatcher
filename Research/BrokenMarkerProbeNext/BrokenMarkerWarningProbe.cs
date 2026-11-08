// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

public sealed class BrokenMarkerWarningEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    public void OnModLoad(IModContext context) { host = new GameObject("CrankyAnt BrokenMarkerWarningProbe"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<BrokenMarkerWarningProbe>().Initialize(context.ModDirectory); }
    public void OnWorldLoad() { host.GetComponent<BrokenMarkerWarningProbe>().Begin(); }
    public void OnWorldUnload() { host.GetComponent<BrokenMarkerWarningProbe>().End(); }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}
public sealed class BrokenMarkerWarningProbe : MonoBehaviour
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string Map = "crankyant/broken-marker-test-map", Segment = "SCrankyAntSceneProbe", MarkerPath = "World/Roads Sylva/Crossing Unprotected - Allen (1)/Whistle Sign E";
    private string directory, log, status = "Waiting for world.";
    private bool pending;
    private float deadline;
    private float loadStarted, originStableSince;
    private Vector3 observedOrigin;
    private bool originObserved;
    private int world;
    private Component restoredMarker;
    private object originalSerial, repairedLocation, restoredGraph, controlMessage;
    private Vector3 restoreOrigin;
    private PoseState[] baseline;
    private bool restoreReady;
    private sealed class PoseState
    {
        public Transform Transform, Parent;
        public Vector3 Position, LocalPosition;
        public Quaternion Rotation, LocalRotation;
        public bool ActiveSelf, ActiveHierarchy;
    }
    public void Initialize(string path) { directory = path; log = Path.Combine(path, "BrokenMarkerWarningProbe.log"); Write("loaded"); }
    private void Write(string s) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + s + Environment.NewLine); }
    public void Begin() { world++; restoreReady = false; restoredMarker = null; controlMessage = null; pending = true; originObserved = false; loadStarted = Time.realtimeSinceStartup; deadline = loadStarted + 60f; Write("world=" + world + " load; awaiting graph, marker and stable origin"); }
    public void End() { pending = false; restoreReady = false; restoredMarker = null; controlMessage = null; Write("world=" + world + " unload; no automatic restoration attempted"); }
    private static Type TypeNamed(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null); }
    private static object Read(object target, string name) { var t = target as Type ?? target?.GetType() ?? throw new InvalidOperationException("null receiver " + name); var instance = target is Type ? null : target; var p = t.GetProperty(name, Flags); var f = t.GetField(name, Flags); return p != null ? p.GetValue(instance, null) : f != null ? f.GetValue(instance) : throw new MissingMemberException(t.FullName, name); }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static Vector3 Origin() { var t = TypeNamed("Helpers.WorldTransformer") ?? throw new InvalidOperationException("WorldTransformer unavailable"); return (Vector3)t.GetMethod("WorldToGame", Flags, null, new[] { typeof(Vector3) }, null).Invoke(null, new object[] { Vector3.zero }); }
    private void Update()
    {
        if (!pending) return;
        try
        {
            Wait(); if (!pending) return;
            var mapType = TypeNamed("Game.ActiveMapDefinition"); var graphType = TypeNamed("Track.Graph"); var markerType = TypeNamed("Track.TrackMarker");
            if (mapType == null || graphType == null || markerType == null) { Wait(); return; }
            var identifier = Convert.ToString(Read(mapType, "Identifier"));
            if (string.IsNullOrEmpty(identifier)) { Wait(); return; }
            if (identifier != Map) { pending = false; status = "Wrong map; no action."; Write("world=" + world + " wrong map=" + identifier); return; }
            var graph = Read(graphType, "Shared");
            var markers = Resources.FindObjectsOfTypeAll(markerType).OfType<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && Convert.ToString(Read(c, "id")) == "M5pp").ToArray();
            if (graph == null || markers.Length == 0) { originObserved = false; Wait(); return; }
            var now = Time.realtimeSinceStartup;
            var origin = Origin();
            if (float.IsNaN(origin.x) || float.IsNaN(origin.y) || float.IsNaN(origin.z) || float.IsInfinity(origin.x) || float.IsInfinity(origin.y) || float.IsInfinity(origin.z)) throw new InvalidOperationException("Origin is not finite");
            if (!originObserved || (origin - observedOrigin).sqrMagnitude >= 0.0001f)
            {
                observedOrigin = origin; originObserved = true; originStableSince = now;
                Write("world=" + world + " origin observed=" + origin.ToString("F4") + " secondsSinceLoad=" + (now - loadStarted).ToString("F2"));
            }
            if (now - loadStarted < 10f || now - originStableSince < 5f) { status = "Waiting for stable origin."; return; }
            Write("world=" + world + " origin settled=" + origin.ToString("F4") + " secondsSinceLoad=" + (now - loadStarted).ToString("F2") + " stableSeconds=" + (now - originStableSince).ToString("F2"));
            pending = false;
            CheckAndMaybeRepair(graphType, graph, markers);
        }
        catch (Exception e) { pending = false; status = "Failed closed; inspect log."; Write("world=" + world + " ERROR " + e); }
    }
    private void Wait() { if (Time.realtimeSinceStartup > deadline) { pending = false; status = "Timed out; inspect log."; Write("world=" + world + " readiness timeout"); } }
    private void CheckAndMaybeRepair(Type graphType, object graph, Component[] markers)
    {
        if (markers.Length != 1 || PathOf(markers[0].transform) != MarkerPath) throw new InvalidOperationException("Marker identity or path mismatch");
        var m = markers[0]; var behaviour = (Behaviour)m;
        if (!behaviour.enabled || !m.gameObject.activeSelf || !m.gameObject.activeInHierarchy) throw new InvalidOperationException("Marker activity mismatch");
        var serial = Read(m, "_location");
        if (Convert.ToString(Read(serial, "segmentId")) != "Shuo" || Math.Abs(Convert.ToSingle(Read(serial, "distance")) - 58.69453f) > 0.001f || Convert.ToString(Read(serial, "end")) != "A") throw new InvalidOperationException("Original serialized location mismatch");
        var getSegment = graphType.GetMethod("GetSegment");
        if (getSegment.Invoke(graph, new object[] { "Shuo" }) != null) throw new InvalidOperationException("Original Shuo still exists");
        var resolve = graphType.GetMethod("TryResolveLocation"); var originalArgs = new[] { serial, (object)null };
        if ((bool)resolve.Invoke(graph, originalArgs)) throw new InvalidOperationException("Original location unexpectedly resolves");
        var replacement = getSegment.Invoke(graph, new object[] { Segment }) ?? throw new InvalidOperationException("Replacement absent");
        var a = Read(replacement, "a"); var b = Read(replacement, "b");
        if (Convert.ToString(Read(a, "id")) != "NCrankyAntSceneProbeA" || Convert.ToString(Read(b, "id")) != "NCrankyAntSceneProbeB" || ((Vector3)Read(a, "Position") - new Vector3(25241.1465f, 622.7342f, -800f)).sqrMagnitude > 0.01f || ((Vector3)Read(b, "Position") - new Vector3(25241.1465f, 622.7342f, -700f)).sqrMagnitude > 0.01f) throw new InvalidOperationException("Replacement endpoints mismatch");
        var candidate = Activator.CreateInstance(serial.GetType());
        foreach (var name in new[] { "segmentId", "distance", "end" }) { var f = serial.GetType().GetField(name, Flags) ?? throw new MissingFieldException(name); f.SetValue(candidate, name == "segmentId" ? (object)Segment : f.GetValue(serial)); }
        var args = new[] { candidate, (object)null }; if (!(bool)resolve.Invoke(graph, args) || args[1] == null) throw new InvalidOperationException("Replacement resolution failed");
        var buckets = Read(graph, "_namedLocationsBySegmentId") as IDictionary ?? throw new InvalidOperationException("Graph buckets unavailable");
        var count = Count(buckets, m, null, false);
        var origin = Origin(); var t = m.transform; var parent = t.parent; var position = t.position; var rotation = t.rotation; var active = m.gameObject.activeSelf;
        Write("world=" + world + " preflight map=" + Map + " marker=" + m.GetInstanceID() + " path=" + PathOf(t) + " original=Shuo/58.69453/A resolves=false registrations=" + count + " replacement=" + Segment + " endpoints=A/B verified origin=" + origin.ToString("F4") + " position=" + position.ToString("F4") + " rotation=" + rotation.ToString("F4") + " active=" + active + " hierarchy=" + m.gameObject.activeInHierarchy + " enabled=" + behaviour.enabled);
        Write("world=" + world + " preflight cachedPositionPresent=" + (Read(m, "_cachedPosition") != null) + " warnedInvalid=" + Read(m, "_warnedInvalid"));
        Snapshot(m, "preflight");
        if (!File.Exists(Path.Combine(directory, "ARM_BROKEN_MARKER_WARNING_PROBE"))) { status = "Dry preflight recorded; no mutation."; return; }
        if (count != 0) throw new InvalidOperationException("Armed repair requires zero baseline registrations");
        if ((Origin() - origin).sqrMagnitude >= 0.0001f) throw new InvalidOperationException("Origin changed");
        var serialCopy = Activator.CreateInstance(serial.GetType());
        foreach (var name in new[] { "segmentId", "distance", "end" }) { var field = serial.GetType().GetField(name, Flags) ?? throw new MissingFieldException(name); field.SetValue(serialCopy, field.GetValue(serial)); }
        var poseCopy = CapturePose(m);
        behaviour.enabled = false;
        try { m.GetType().GetProperty("Location", Flags).SetValue(m, args[1], null); } finally { behaviour.enabled = true; }
        var actual = Read(m, "Location");
        var afterSerial = Read(m, "_location"); if (Convert.ToString(Read(afterSerial, "segmentId")) != Segment || Math.Abs(Convert.ToSingle(Read(afterSerial, "distance")) - 58.69453f) > 0.001f || Convert.ToString(Read(afterSerial, "end")) != "A") throw new InvalidOperationException("Serialized result mismatch");
        if (!actual.Equals(args[1]) || Count(buckets, m, actual, true) != 1 || (Origin() - origin).sqrMagnitude >= 0.0001f || t.parent != parent || (t.position - position).sqrMagnitude >= 0.0001f || Quaternion.Angle(t.rotation, rotation) > 0.01f || m.gameObject.activeSelf != active || !m.gameObject.activeInHierarchy || !behaviour.enabled) throw new InvalidOperationException("Postcondition mismatch; abandon session without saving");
        Write("world=" + world + " repaired registration=1 segment=" + Segment + " location=" + actual + " origin=" + Origin().ToString("F4") + " position=" + t.position.ToString("F4") + " rotation=" + t.rotation.ToString("F4") + " active=" + m.gameObject.activeSelf + " hierarchy=" + m.gameObject.activeInHierarchy + " enabled=" + behaviour.enabled);
        Snapshot(m, "repaired");
        bool removed = ClearWarning(Convert.ToSingle(Read(serial, "distance")));
        if (removed) { restoredMarker = m; originalSerial = serialCopy; repairedLocation = actual; restoredGraph = graph; restoreOrigin = origin; baseline = poseCopy; restoreReady = true; }
        status = removed ? "Repair verified. Restore is available; never save." : "Repair verified; warning not removed. Exit without saving.";
    }
    private bool ClearWarning(float originalDistance)
    {
        var collectorType = TypeNamed("Game.Diagnostics.MessageCollector") ?? throw new InvalidOperationException("MessageCollector type unavailable");
        var contract = TypeNamed("Game.Diagnostics.IMessageCollector") ?? throw new InvalidOperationException("IMessageCollector unavailable");
        var messageType = TypeNamed("Game.Diagnostics.DiagnosticMessage") ?? throw new InvalidOperationException("DiagnosticMessage unavailable");
        var collector = Read(collectorType, "Active") ?? throw new InvalidOperationException("Active collector unavailable");
        if (collector.GetType() != collectorType) throw new InvalidOperationException("Active collector is not the native message collector");
        var messagesProperty = contract.GetProperty("Messages") ?? throw new MissingMemberException("IMessageCollector.Messages");
        var remove = contract.GetMethod("RemoveMessage", new[] { messageType }) ?? throw new MissingMethodException("IMessageCollector.RemoveMessage");
        Func<object[]> snapshot = () => ((IEnumerable)messagesProperty.GetValue(collector, null)).Cast<object>().ToArray();
        var before = snapshot();
        var description = string.Format("Track marker {0} is not on the track at {1} + {2:F1}m and will have no effect.", "M5pp", "Shuo", originalDistance);
        var candidates = before.Where(item => item != null && item.GetType() == messageType && Convert.ToString(Read(item, "DeduplicationKey")) == "M5pp" && !(bool)Read(item, "IsRemoved")).ToArray();
        var matches = candidates.Where(item => Convert.ToString(Read(item, "Severity")) == "Warning" && Convert.ToString(Read(item, "Source")) == "System" && Convert.ToString(Read(item, "DeduplicationKey")) == "M5pp" && Convert.ToString(Read(item, "Originator")) == "Whistle Sign E" && Convert.ToString(Read(item, "Description")) == description && !(bool)Read(item, "IsRemoved")).ToArray();
        Write("world=" + world + " warning before total=" + before.Length + " candidates=" + candidates.Length + " exactMatches=" + matches.Length + " expectedDescription=" + description);
        if (candidates.Length != 1 || matches.Length != 1) { Write("world=" + world + " warning unchanged: expected exactly one candidate and exact match"); return false; }
        var selected = matches[0];
        object control = null;
        if (File.Exists(Path.Combine(directory, "INCLUDE_WARNING_CONTROL")))
        {
            const string controlKey = "CrankyAnt.BrokenMarkerWarningProbe.Control";
            const string controlOrigin = "CrankyAnt BrokenMarkerWarningProbe";
            const string controlSource = "CrankyAnt Probe";
            const string controlDescription = "CrankyAnt probe control warning; expected to remain";
            if (before.Any(item => item != null && Convert.ToString(Read(item, "DeduplicationKey")) == controlKey)) throw new InvalidOperationException("Control key already exists; no control added");
            var logType = TypeNamed("Game.Diagnostics.MessageLog") ?? throw new InvalidOperationException("MessageLog unavailable");
            var warning = logType.GetMethod("Warning", Flags, null, new[] { typeof(string), typeof(string), typeof(string), typeof(string) }, null) ?? throw new MissingMethodException("MessageLog.Warning");
            control = warning.Invoke(null, new object[] { controlOrigin, controlDescription, controlSource, controlKey }) ?? throw new InvalidOperationException("Control warning creation returned null");
            before = snapshot();
            if (before.Count(item => ReferenceEquals(item, control)) != 1 || Convert.ToString(Read(control, "Severity")) != "Warning" || Convert.ToString(Read(control, "Source")) != controlSource || Convert.ToString(Read(control, "DeduplicationKey")) != controlKey || Convert.ToString(Read(control, "Originator")) != controlOrigin || Convert.ToString(Read(control, "Description")) != controlDescription || (bool)Read(control, "IsRemoved")) throw new InvalidOperationException("Control warning verification failed");
            Write("world=" + world + " authored control added identity=" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(control) + " key=" + controlKey + " description=" + controlDescription);
        }
        if (before.Count(item => ReferenceEquals(item, selected)) != 1) throw new InvalidOperationException("Selected warning changed before removal");
        var unrelated = before.Where(item => !ReferenceEquals(item, selected)).ToArray();
        remove.Invoke(collector, new[] { selected });
        var after = snapshot();
        var retained = unrelated.Count(item => after.Any(other => ReferenceEquals(other, item)));
        var selectedRemaining = after.Count(item => ReferenceEquals(item, selected));
        Write("world=" + world + " warning after total=" + after.Length + " selectedRemaining=" + selectedRemaining + " unrelatedBefore=" + unrelated.Length + " unrelatedRetained=" + retained + " isRemoved=" + Read(selected, "IsRemoved") + " controlRetained=" + (control == null ? "not requested" : after.Any(item => ReferenceEquals(item, control)).ToString()));
        if (selectedRemaining != 0 || retained != unrelated.Length || !(bool)Read(selected, "IsRemoved")) throw new InvalidOperationException("Warning removal postcondition failed");
        controlMessage = control;
        return true;
    }
    private void Snapshot(Component marker, string phase)
    {
        foreach (var t in marker.GetComponentsInChildren<Transform>(true)) Write("world=" + world + " " + phase + " child=" + PathOf(t) + " position=" + t.position.ToString("F4") + " rotation=" + t.rotation.ToString("F4") + " activeSelf=" + t.gameObject.activeSelf + " activeHierarchy=" + t.gameObject.activeInHierarchy);
        var relative = marker.GetComponents<Component>().Single(c => c != null && c.GetType().FullName == "Track.TrackRelativePosition");
        var target = Read(relative, "targetTransform") as Transform ?? throw new InvalidOperationException("Track-relative target absent");
        Write("world=" + world + " " + phase + " target=" + PathOf(target) + " parent=" + (target.parent == null ? "none" : PathOf(target.parent)) + " position=" + target.position.ToString("F4") + " rotation=" + target.rotation.ToString("F4") + " activeSelf=" + target.gameObject.activeSelf + " activeHierarchy=" + target.gameObject.activeInHierarchy);
    }
    private static PoseState[] CapturePose(Component marker)
    {
        return marker.GetComponentsInChildren<Transform>(true).Select(t => new PoseState { Transform = t, Parent = t.parent, Position = t.position, LocalPosition = t.localPosition, Rotation = t.rotation, LocalRotation = t.localRotation, ActiveSelf = t.gameObject.activeSelf, ActiveHierarchy = t.gameObject.activeInHierarchy }).ToArray();
    }
    private static void VerifyPose(Component marker, PoseState[] expected)
    {
        var current = marker.GetComponentsInChildren<Transform>(true);
        if (current.Length != expected.Length) throw new InvalidOperationException("Hierarchy size changed");
        foreach (var p in expected)
            if (p.Transform == null || !current.Contains(p.Transform) || p.Transform.parent != p.Parent || (p.Transform.position - p.Position).sqrMagnitude >= 0.0001f || (p.Transform.localPosition - p.LocalPosition).sqrMagnitude >= 0.0001f || Quaternion.Angle(p.Transform.rotation, p.Rotation) > 0.01f || Quaternion.Angle(p.Transform.localRotation, p.LocalRotation) > 0.01f || p.Transform.gameObject.activeSelf != p.ActiveSelf || p.Transform.gameObject.activeInHierarchy != p.ActiveHierarchy) throw new InvalidOperationException("Hierarchy pose or activity changed");
    }
    private object[] Messages(out object collector)
    {
        var type = TypeNamed("Game.Diagnostics.MessageCollector") ?? throw new InvalidOperationException("MessageCollector unavailable");
        var contract = TypeNamed("Game.Diagnostics.IMessageCollector") ?? throw new InvalidOperationException("IMessageCollector unavailable");
        collector = Read(type, "Active");
        if (collector == null || collector.GetType() != type) throw new InvalidOperationException("Native collector unavailable");
        return ((IEnumerable)(contract.GetProperty("Messages") ?? throw new MissingMemberException("Messages")).GetValue(collector, null)).Cast<object>().ToArray();
    }
    private void Restore()
    {
        if (!restoreReady) return;
        restoreReady = false;
        try
        {
            var m = restoredMarker;
            var mapType = TypeNamed("Game.ActiveMapDefinition");
            var graphType = TypeNamed("Track.Graph");
            if (m == null || mapType == null || graphType == null || Convert.ToString(Read(mapType, "Identifier")) != Map || !ReferenceEquals(Read(graphType, "Shared"), restoredGraph)) throw new InvalidOperationException("World or graph changed");
            var markers = Resources.FindObjectsOfTypeAll(m.GetType()).OfType<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && Convert.ToString(Read(c, "id")) == "M5pp").ToArray();
            var currentOrigin = Origin();
            Write("world=" + world + " restore precheck markerCount=" + markers.Length + " sameMarker=" + (markers.Length == 1 && ReferenceEquals(markers[0], m)) + " path=" + PathOf(m.transform) + " enabled=" + ((Behaviour)m).enabled + " activeHierarchy=" + m.gameObject.activeInHierarchy + " capturedOrigin=" + restoreOrigin.ToString("F4") + " currentOrigin=" + currentOrigin.ToString("F4"));
            if (markers.Length != 1 || !ReferenceEquals(markers[0], m) || PathOf(m.transform) != MarkerPath || !((Behaviour)m).enabled || !m.gameObject.activeInHierarchy || (currentOrigin - restoreOrigin).sqrMagnitude >= 0.0001f) throw new InvalidOperationException("Marker or origin changed");
            VerifyPose(m, baseline);
            var currentSerial = Read(m, "_location");
            if (Convert.ToString(Read(currentSerial, "segmentId")) != Segment || Math.Abs(Convert.ToSingle(Read(currentSerial, "distance")) - 58.69453f) > 0.001f || Convert.ToString(Read(currentSerial, "end")) != "A" || !Read(m, "Location").Equals(repairedLocation)) throw new InvalidOperationException("Current binding changed");
            var buckets = Read(restoredGraph, "_namedLocationsBySegmentId") as IDictionary ?? throw new InvalidOperationException("Graph buckets unavailable");
            if (Count(buckets, m, repairedLocation, true) != 1 || graphType.GetMethod("GetSegment").Invoke(restoredGraph, new object[] { "Shuo" }) != null) throw new InvalidOperationException("Graph changed");
            var before = Messages(out var collector);
            if (before.Any(item => Convert.ToString(Read(item, "DeduplicationKey")) == "M5pp") || (controlMessage != null && !before.Any(item => ReferenceEquals(item, controlMessage)))) throw new InvalidOperationException("Warning baseline changed");
            var locationField = m.GetType().GetField("_location", Flags) ?? throw new MissingFieldException("TrackMarker._location");
            var invalidate = m.GetType().GetMethod("InvalidateCache", Flags, null, Type.EmptyTypes, null) ?? throw new MissingMethodException("TrackMarker.InvalidateCache");
            Write("world=" + world + " before restore cachedPositionPresent=" + (Read(m, "_cachedPosition") != null) + " warnedInvalid=" + Read(m, "_warnedInvalid"));
            var behaviour = (Behaviour)m;
            behaviour.enabled = false;
            try { locationField.SetValue(m, originalSerial); invalidate.Invoke(m, null); } finally { behaviour.enabled = true; }
            var serial = Read(m, "_location");
            var args = new[] { serial, (object)null };
            if (Convert.ToString(Read(serial, "segmentId")) != "Shuo" || !Read(serial, "distance").Equals(Read(originalSerial, "distance")) || !Read(serial, "end").Equals(Read(originalSerial, "end")) || (bool)graphType.GetMethod("TryResolveLocation").Invoke(restoredGraph, args) || Count(buckets, m, null, false) != 0 || (Origin() - restoreOrigin).sqrMagnitude >= 0.0001f || !behaviour.enabled) throw new InvalidOperationException("Restoration postcondition failed");
            VerifyPose(m, baseline);
            var after = Messages(out var collectorAfter);
            var description = string.Format("Track marker {0} is not on the track at {1} + {2:F1}m and will have no effect.", "M5pp", "Shuo", Convert.ToSingle(Read(originalSerial, "distance")));
            var warnings = after.Where(item => Convert.ToString(Read(item, "DeduplicationKey")) == "M5pp" && Convert.ToString(Read(item, "Severity")) == "Warning" && Convert.ToString(Read(item, "Source")) == "System" && Convert.ToString(Read(item, "Originator")) == "Whistle Sign E" && Convert.ToString(Read(item, "Description")) == description && !(bool)Read(item, "IsRemoved")).ToArray();
            var retained = before.Count(item => after.Any(other => ReferenceEquals(item, other)));
            Write("world=" + world + " restore original=Shuo/58.69453/A resolves=false registrations=0 pose=verified warnings=" + warnings.Length + " unrelatedBefore=" + before.Length + " unrelatedRetained=" + retained + " controlRetained=" + (controlMessage == null ? "not requested" : after.Any(item => ReferenceEquals(item, controlMessage)).ToString()));
            Write("world=" + world + " after restore cachedPositionPresent=" + (Read(m, "_cachedPosition") != null) + " warnedInvalid=" + Read(m, "_warnedInvalid"));
            if (!ReferenceEquals(collector, collectorAfter) || warnings.Length != 1 || after.Count(item => Convert.ToString(Read(item, "DeduplicationKey")) == "M5pp") != 1 || retained != before.Length) throw new InvalidOperationException("Warning restoration postcondition failed");
            Snapshot(m, "restored");
            status = "Original broken reference restored; never save.";
        }
        catch (Exception e) { Write("world=" + world + " restore ERROR " + e); status = "Restore failed; exit without saving."; }
    }
    private int Count(IDictionary buckets, Component marker, object expected, bool verify)
    {
        int count = 0;
        foreach (DictionaryEntry bucket in buckets) { var entries = bucket.Value as IDictionary ?? throw new InvalidOperationException("Unsupported graph bucket"); foreach (DictionaryEntry entry in entries) if (Convert.ToString(entry.Key) == "M5pp") { count++; var item = entry.Value; Write("world=" + world + " registration bucket=" + bucket.Key + " objectMatches=" + ReferenceEquals(Read(item, "ObjectValue"), marker)); if (!ReferenceEquals(Read(item, "ObjectValue"), marker) || Convert.ToString(Read(item, "Id")) != "M5pp" || (verify && (Convert.ToString(bucket.Key) != Segment || !expected.Equals(Read(item, "Location"))))) throw new InvalidOperationException("Registration mismatch"); } }
        if (count > 1) throw new InvalidOperationException("Duplicate registration"); return count;
    }
    private void OnGUI() { GUI.Label(new Rect(20, 100, 600, 30), "Broken marker probe: " + status); if (restoreReady && GUI.Button(new Rect(20, 135, 260, 30), "Restore original broken reference")) Restore(); }
}
