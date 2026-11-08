// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;
public sealed class MilepostProbeEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    public void OnModLoad(IModContext c) { host = new GameObject("CrankyAnt MilepostProbe"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<MilepostProbe>().Init(c.ModDirectory); }
    public void OnWorldLoad() { }
    public void OnWorldUnload() { }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}
public sealed class MilepostProbe : MonoBehaviour
{
    private const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private string log, status = "Load the disposable scene-operations-map.";
    private Rect window = new Rect(440, 500, 420, 90);
    private void Write(string s) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + s + Environment.NewLine); }
    public void Init(string dir) { log = Path.Combine(dir, "MilepostProbe.log"); Write("loaded"); }
    private static Type TypeOf(string n) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(n, false)).First(t => t != null); }
    private static object Read(object o, string n) { var t = o as Type ?? o.GetType(); var obj = o is Type ? null : o; var f = t.GetField(n, F); if (f != null) return f.GetValue(obj); var p = t.GetProperty(n, F); if (p != null) return p.GetValue(obj, null); throw new MissingMemberException(t.FullName, n); }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static Vector3 Origin() { return (Vector3)TypeOf("Helpers.WorldTransformer").GetMethod("WorldToGame", F, null, new[] { typeof(Vector3) }, null).Invoke(null, new object[] { Vector3.zero }); }
    private static Component One(string path, string name)
    {
        var found = Resources.FindObjectsOfTypeAll(TypeOf(name)).OfType<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && PathOf(c.transform) == path).ToArray();
        if (found.Length != 1) throw new InvalidOperationException("Expected one " + name + " at " + path + "; got " + found.Length);
        return found[0];
    }
    private static void VerifyRegistration(Component marker, object graph, string segment)
    {
        var id = Convert.ToString(Read(marker, "id")); var markerType = marker.GetType();
        if (Resources.FindObjectsOfTypeAll(markerType).OfType<Component>().Count(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && Convert.ToString(Read(c, "id")) == id) != 1) throw new InvalidOperationException("Marker ID is not unique");
        var buckets = Read(graph, "_namedLocationsBySegmentId") as IDictionary ?? throw new InvalidOperationException("Graph buckets unavailable"); var location = Read(marker, "Location"); int count = 0;
        foreach (DictionaryEntry bucket in buckets) { var entries = bucket.Value as IDictionary ?? throw new InvalidOperationException("Unsupported graph bucket"); foreach (DictionaryEntry entry in entries) if (Convert.ToString(entry.Key) == id) { count++; if (Convert.ToString(bucket.Key) != segment || !ReferenceEquals(Read(entry.Value, "ObjectValue"), marker) || !location.Equals(Read(entry.Value, "Location"))) throw new InvalidOperationException("Registration contents mismatch"); } }
        if (count != 1) throw new InvalidOperationException("Expected one matching registration");
    }
    private void Snapshot(string phase, Component origin, Component dependent, IDictionary cache, object graph)
    {
        bool existed = cache.Contains("T"); var prior = existed ? cache["T"] : null;
        try
        {
            var resolved = Read(dependent, "Origin") as Component;
            Write(phase + " originCacheExisted=" + existed + " priorMatchesT46=" + ReferenceEquals(prior, origin) + " resolvedMatchesT46=" + ReferenceEquals(resolved, origin));
            if (!ReferenceEquals(cache["T"], resolved)) throw new InvalidOperationException("Unexpected cache change");
        }
        finally
        {
            if (existed) cache["T"] = prior; else cache.Remove("T");
            if (cache.Contains("T") != existed || (existed && !ReferenceEquals(cache["T"], prior))) throw new InvalidOperationException("Origin cache restoration failed");
        }
        foreach (var post in new[] { origin, dependent })
        {
            var marker = Read(post, "TrackMarker") as Component ?? throw new InvalidOperationException("Missing marker"); var text = Read(post, "_textSynchronizer");
            var id = Convert.ToString(Read(marker, "id")); var serial = Read(marker, "_location"); var entries = Read(graph, "_namedLocationsBySegmentId") as IDictionary ?? throw new InvalidOperationException("Graph buckets unavailable"); int count = 0;
            foreach (DictionaryEntry bucket in entries) { var items = bucket.Value as IDictionary ?? throw new InvalidOperationException("Unsupported bucket"); foreach (DictionaryEntry item in items) if (Convert.ToString(item.Key) == id) { count++; Write(phase + " registration id=" + id + " bucket=" + bucket.Key + " objectMatches=" + ReferenceEquals(Read(item.Value, "ObjectValue"), marker)); } }
            Write(phase + " post=" + PathOf(post.transform) + " prefix=" + Read(post, "prefix") + " mileage=" + Read(post, "mileage") + " text=" + Read(text, "text") + " segment=" + Read(serial, "segmentId") + " distance=" + Read(serial, "distance") + " end=" + Read(serial, "end") + " registrations=" + count + " active=" + post.gameObject.activeSelf + " position=" + post.transform.position.ToString("F4"));
        }
    }
    private void Run()
    {
        Component marker = null; object old = null, graph = null; PropertyInfo property = null; bool changed = false, baselineActive = false, captured = false;
        try
        {
            if (Convert.ToString(Read(TypeOf("Game.ActiveMapDefinition"), "Identifier")) != "crankyant/scene-operations-map") throw new InvalidOperationException("Wrong map");
            var origin = One("World/Mile Posts/T-46 Origin", "Helpers.MilePost"); var dependent = One("World/Mile Posts/T-47", "Helpers.MilePost");
            if (Convert.ToString(Read(origin, "prefix")) != "T" || !(bool)Read(origin, "origin") || Convert.ToString(Read(dependent, "prefix")) != "T" || (bool)Read(dependent, "origin")) throw new InvalidOperationException("Milepost identity mismatch");
            marker = Read(origin, "TrackMarker") as Component; if (marker == null || Convert.ToString(Read(marker, "id")) != "Mdow" || !((Behaviour)marker).enabled || !marker.gameObject.activeInHierarchy) throw new InvalidOperationException("Origin marker mismatch");
            graph = Read(TypeOf("Track.Graph"), "Shared") ?? throw new InvalidOperationException("Missing graph"); var cache = Read(TypeOf("Helpers.MilePost"), "_originMilePosts") as IDictionary ?? throw new InvalidOperationException("Origin cache unavailable");
            var fixedOrigin = Origin(); property = marker.GetType().GetProperty("Location", F); old = property.GetValue(marker, null) ?? throw new InvalidOperationException("Original location unresolved"); VerifyRegistration(marker, graph, "Stvw"); baselineActive = marker.gameObject.activeSelf; captured = true;
            var serial = Read(marker, "_location"); if (Convert.ToString(Read(serial, "segmentId")) != "Stvw") throw new InvalidOperationException("Unexpected segment");
            var st = serial.GetType(); var copy = Activator.CreateInstance(st); foreach(var name in new[]{"segmentId","distance","end"}) { var f=st.GetField(name,F); f.SetValue(copy, name == "distance" ? (object)(Convert.ToSingle(f.GetValue(serial)) + 0.25f) : f.GetValue(serial)); }
            var args = new[] { copy, (object)null }; if (!(bool)graph.GetType().GetMethod("TryResolveLocation").Invoke(graph,args) || args[1] == null || args[1].Equals(old)) throw new InvalidOperationException("Candidate unresolved");
            Snapshot("baseline", origin, dependent, cache, graph); if ((Origin()-fixedOrigin).sqrMagnitude >= 0.0001f) throw new InvalidOperationException("Origin changed");
            changed = true; ((Behaviour)marker).enabled = false; try { property.SetValue(marker,args[1],null); } finally { ((Behaviour)marker).enabled = true; }
            VerifyRegistration(marker, graph, "Stvw"); Snapshot("rebound",origin,dependent,cache,graph);
            ((Behaviour)marker).enabled = false; try { property.SetValue(marker,old,null); } finally { ((Behaviour)marker).enabled = true; }
            if (!old.Equals(property.GetValue(marker,null)) || (Origin()-fixedOrigin).sqrMagnitude >= 0.0001f) throw new InvalidOperationException("Restore check failed");
            VerifyRegistration(marker, graph, "Stvw"); Snapshot("restored",origin,dependent,cache,graph); changed = false; status = "Recorded; inspect all fields and registrations.";
        }
        catch(Exception e) { Write("ERROR " + e); status = "Failed; abandon disposable session without saving."; }
        finally
        {
            if (captured) try
            {
                if (changed) { ((Behaviour)marker).enabled = false; try { property.SetValue(marker,old,null); } finally { ((Behaviour)marker).enabled = true; } }
                if (marker.gameObject.activeSelf != baselineActive || !((Behaviour)marker).enabled || !old.Equals(property.GetValue(marker,null))) throw new InvalidOperationException("Final activity or binding differs");
                VerifyRegistration(marker, graph, "Stvw"); Write("final activity binding registration verified");
            }
            catch(Exception e) { Write("RESTORE ERROR " + e); status = "Restore failed; abandon without saving."; }
        }
    }
    private void OnGUI() { window = GUI.Window(0x43524d50,window,id => { GUILayout.Label(status); if(GUILayout.Button("Inspect T-46 rebind and T-47 origin")) Run(); GUI.DragWindow(); },"Milepost Probe"); }
}
