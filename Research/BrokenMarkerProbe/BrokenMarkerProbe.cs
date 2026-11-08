// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

public sealed class BrokenMarkerEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    public void OnModLoad(IModContext context) { host = new GameObject("CrankyAnt BrokenMarkerProbe"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<BrokenMarkerProbe>().Initialize(context.ModDirectory); }
    public void OnWorldLoad() { host.GetComponent<BrokenMarkerProbe>().Begin(); }
    public void OnWorldUnload() { host.GetComponent<BrokenMarkerProbe>().End(); }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}
public sealed class BrokenMarkerProbe : MonoBehaviour
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string Map = "crankyant/broken-marker-test-map", Segment = "SCrankyAntSceneProbe", MarkerPath = "World/Roads Sylva/Crossing Unprotected - Allen (1)/Whistle Sign E";
    private string directory, log, status = "Waiting for world.";
    private bool pending;
    private float deadline;
    private int world;
    public void Initialize(string path) { directory = path; log = Path.Combine(path, "BrokenMarkerProbe.log"); Write("loaded"); }
    private void Write(string s) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + s + Environment.NewLine); }
    public void Begin() { world++; pending = true; deadline = Time.realtimeSinceStartup + 60f; Write("world=" + world + " load; awaiting graph and marker"); }
    public void End() { pending = false; Write("world=" + world + " unload; no restoration attempted"); }
    private static Type TypeNamed(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null); }
    private static object Read(object target, string name) { var t = target as Type ?? target?.GetType() ?? throw new InvalidOperationException("null receiver " + name); var instance = target is Type ? null : target; var p = t.GetProperty(name, Flags); var f = t.GetField(name, Flags); return p != null ? p.GetValue(instance, null) : f != null ? f.GetValue(instance) : throw new MissingMemberException(t.FullName, name); }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static Vector3 Origin() { var t = TypeNamed("Helpers.WorldTransformer") ?? throw new InvalidOperationException("WorldTransformer unavailable"); return (Vector3)t.GetMethod("WorldToGame", Flags, null, new[] { typeof(Vector3) }, null).Invoke(null, new object[] { Vector3.zero }); }
    private void Update()
    {
        if (!pending) return;
        try
        {
            var mapType = TypeNamed("Game.ActiveMapDefinition"); var graphType = TypeNamed("Track.Graph"); var markerType = TypeNamed("Track.TrackMarker");
            if (mapType == null || graphType == null || markerType == null) { Wait(); return; }
            var identifier = Convert.ToString(Read(mapType, "Identifier"));
            if (string.IsNullOrEmpty(identifier)) { Wait(); return; }
            if (identifier != Map) { pending = false; status = "Wrong map; no action."; Write("world=" + world + " wrong map=" + identifier); return; }
            var graph = Read(graphType, "Shared");
            var markers = Resources.FindObjectsOfTypeAll(markerType).OfType<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && Convert.ToString(Read(c, "id")) == "M5pp").ToArray();
            if (graph == null || markers.Length == 0) { Wait(); return; }
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
        Snapshot(m, "preflight");
        if (!File.Exists(Path.Combine(directory, "ARM_BROKEN_MARKER_PROBE"))) { status = "Dry preflight recorded; no mutation."; return; }
        if (count != 0) throw new InvalidOperationException("Armed repair requires zero baseline registrations");
        if ((Origin() - origin).sqrMagnitude >= 0.0001f) throw new InvalidOperationException("Origin changed");
        behaviour.enabled = false;
        try { m.GetType().GetProperty("Location", Flags).SetValue(m, args[1], null); } finally { behaviour.enabled = true; }
        var actual = Read(m, "Location");
        var afterSerial = Read(m, "_location"); if (Convert.ToString(Read(afterSerial, "segmentId")) != Segment || Math.Abs(Convert.ToSingle(Read(afterSerial, "distance")) - 58.69453f) > 0.001f || Convert.ToString(Read(afterSerial, "end")) != "A") throw new InvalidOperationException("Serialized result mismatch");
        if (!actual.Equals(args[1]) || Count(buckets, m, actual, true) != 1 || (Origin() - origin).sqrMagnitude >= 0.0001f || t.parent != parent || (t.position - position).sqrMagnitude >= 0.0001f || Quaternion.Angle(t.rotation, rotation) > 0.01f || m.gameObject.activeSelf != active || !m.gameObject.activeInHierarchy || !behaviour.enabled) throw new InvalidOperationException("Postcondition mismatch; abandon session without saving");
        Write("world=" + world + " repaired registration=1 segment=" + Segment + " location=" + actual + " origin=" + Origin().ToString("F4") + " position=" + t.position.ToString("F4") + " rotation=" + t.rotation.ToString("F4") + " active=" + m.gameObject.activeSelf + " hierarchy=" + m.gameObject.activeInHierarchy + " enabled=" + behaviour.enabled);
        Snapshot(m, "repaired");
        status = "Repair verified; exit without saving.";
    }
    private void Snapshot(Component marker, string phase)
    {
        foreach (var t in marker.GetComponentsInChildren<Transform>(true)) Write("world=" + world + " " + phase + " child=" + PathOf(t) + " position=" + t.position.ToString("F4") + " rotation=" + t.rotation.ToString("F4") + " activeSelf=" + t.gameObject.activeSelf + " activeHierarchy=" + t.gameObject.activeInHierarchy);
        var relative = marker.GetComponents<Component>().Single(c => c != null && c.GetType().FullName == "Track.TrackRelativePosition");
        var target = Read(relative, "targetTransform") as Transform ?? throw new InvalidOperationException("Track-relative target absent");
        Write("world=" + world + " " + phase + " target=" + PathOf(target) + " parent=" + (target.parent == null ? "none" : PathOf(target.parent)) + " position=" + target.position.ToString("F4") + " rotation=" + target.rotation.ToString("F4") + " activeSelf=" + target.gameObject.activeSelf + " activeHierarchy=" + target.gameObject.activeInHierarchy);
    }
    private int Count(IDictionary buckets, Component marker, object expected, bool verify)
    {
        int count = 0;
        foreach (DictionaryEntry bucket in buckets) { var entries = bucket.Value as IDictionary ?? throw new InvalidOperationException("Unsupported graph bucket"); foreach (DictionaryEntry entry in entries) if (Convert.ToString(entry.Key) == "M5pp") { count++; var item = entry.Value; Write("world=" + world + " registration bucket=" + bucket.Key + " objectMatches=" + ReferenceEquals(Read(item, "ObjectValue"), marker)); if (!ReferenceEquals(Read(item, "ObjectValue"), marker) || Convert.ToString(Read(item, "Id")) != "M5pp" || (verify && (Convert.ToString(bucket.Key) != Segment || !expected.Equals(Read(item, "Location"))))) throw new InvalidOperationException("Registration mismatch"); } }
        if (count > 1) throw new InvalidOperationException("Duplicate registration"); return count;
    }
    private void OnGUI() { GUI.Label(new Rect(20, 100, 600, 30), "Broken marker probe: " + status); }
}
