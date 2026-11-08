// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Railroader.Modding;
using UnityEngine;

public sealed class MilepostProbeEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    public void OnModLoad(IModContext c) { host = new GameObject("CrankyAnt MilepostProbe25"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<MilepostProbe25>().Init(c.ModDirectory); }
    public void OnWorldLoad() { }
    public void OnWorldUnload() { if (host != null) host.GetComponent<MilepostProbe25>().WorldUnloading(); }
    public void OnModUnload() { if (host != null) { host.GetComponent<MilepostProbe25>().Abort(); UnityEngine.Object.Destroy(host); } }
}
public sealed class MilepostProbe25 : MonoBehaviour
{
    private const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private string modDirectory, log, status = "Load the disposable scene-operations-map.";
    private Rect window = new Rect(440, 400, 470, 200);
    private Component origin, dependent, marker, depMarker;
    private object graph, original, candidate, priorCache;
    private IDictionary cache;
    private PropertyInfo location;
    private bool cacheExisted, initialized, changed;
    private string stableBaseline, textBaseline, groupBaseline;
    private Vector3 worldOrigin;
    private int stage;
    private int frame, originSceneHandle, dependentSceneHandle;
    private void Write(string s) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + s + Environment.NewLine); }
    public void Init(string dir) { modDirectory = dir; log = Path.Combine(dir, "MilepostProbe25.log"); Write("loaded homedirArgumentCount=" + Environment.GetCommandLineArgs().Count(a => string.Equals(a, "-homedir", StringComparison.OrdinalIgnoreCase))); }
    private static Type TypeOf(string n) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(n, false)).First(t => t != null); }
    private static object Read(object o, string n) { var t = o as Type ?? o.GetType(); var obj = o is Type ? null : o; var f = t.GetField(n, F); if (f != null) return f.GetValue(obj); var p = t.GetProperty(n, F); if (p != null) return p.GetValue(obj, null); throw new MissingMemberException(t.FullName, n); }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static Vector3 WorldOrigin() { return (Vector3)TypeOf("Helpers.WorldTransformer").GetMethod("WorldToGame", F, null, new[] { typeof(Vector3) }, null).Invoke(null, new object[] { Vector3.zero }); }
    private static Component One(string path, string name) { var found = Resources.FindObjectsOfTypeAll(TypeOf(name)).OfType<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && PathOf(c.transform) == path).ToArray(); if (found.Length != 1) throw new InvalidOperationException("Expected one " + name + " at " + path + "; got " + found.Length); return found[0]; }
    private static string ObjectId(UnityEngine.Object o) { return o == null ? "null" : o.GetInstanceID().ToString(); }
    private static string Vec(Vector3 v) { return v.x.ToString("R") + "," + v.y.ToString("R") + "," + v.z.ToString("R"); }
    private static string Quat(Quaternion q) { return q.x.ToString("R") + "," + q.y.ToString("R") + "," + q.z.ToString("R") + "," + q.w.ToString("R"); }
    private static string Registration(Component m, object g)
    {
        var id = Convert.ToString(Read(m, "id")); var loc = Read(m, "Location");
        if (Resources.FindObjectsOfTypeAll(m.GetType()).OfType<Component>().Count(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && Convert.ToString(Read(c, "id")) == id) != 1) throw new InvalidOperationException("Marker ID is not unique: " + id);
        var buckets = Read(g, "_namedLocationsBySegmentId") as IDictionary ?? throw new InvalidOperationException("Missing graph buckets"); int count = 0; string result = "";
        foreach (DictionaryEntry b in buckets) { var entries = b.Value as IDictionary ?? throw new InvalidOperationException("Unsupported graph bucket"); foreach (DictionaryEntry e in entries) if (Convert.ToString(e.Key) == id) { count++; if (!ReferenceEquals(Read(e.Value, "ObjectValue"), m) || !loc.Equals(Read(e.Value, "Location"))) throw new InvalidOperationException("Registration mismatch: " + id); result = Convert.ToString(b.Key); } }
        if (count != 1) throw new InvalidOperationException("Expected one registration: " + id + " count=" + count);
        var serial = Read(m, "_location"); if (result != Convert.ToString(Read(serial, "segmentId"))) throw new InvalidOperationException("Registration bucket mismatch: " + id); return result;
    }
    private string Stable(Component post)
    {
        var sb = new StringBuilder(); var mark = (Component)Read(post, "TrackMarker"); var sync = (Component)Read(post, "_textSynchronizer");
        sb.Append("post=").Append(ObjectId(post)).Append(" path=").Append(PathOf(post.transform)).Append(" marker=").Append(ObjectId(mark)).Append(" markerId=").Append(Read(mark, "id"));
        sb.Append(" prefix=").Append(Read(post, "prefix")).Append(" mileage=").Append(Read(post, "mileage")).Append(" sync=").Append(ObjectId(sync));
        for (var t = post.transform; t != null; t = t.parent) { sb.Append(" ancestor=").Append(ObjectId(t)).Append("/").Append(t.name).Append(" active=").Append(t.gameObject.activeSelf).Append(" pos=").Append(Vec(t.position)).Append(" rot=").Append(Quat(t.rotation)).Append(" scale=").Append(Vec(t.localScale)); foreach (var c in t.GetComponents<Component>()) if (c != null && c.GetType().FullName.IndexOf("Group", StringComparison.OrdinalIgnoreCase) >= 0) sb.Append(" groupComponent=").Append(c.GetType().FullName).Append("#").Append(ObjectId(c)).Append(" enabled=").Append(c is Behaviour ? ((Behaviour)c).enabled.ToString() : "n/a"); }
        foreach (var t in post.GetComponentsInChildren<Transform>(true).OrderBy(x => PathOf(x)))
        {
            sb.Append(" child=").Append(ObjectId(t)).Append("/").Append(PathOf(t)).Append(" active=").Append(t.gameObject.activeSelf).Append(" pos=").Append(Vec(t.localPosition)).Append(" rot=").Append(Quat(t.localRotation)).Append(" scale=").Append(Vec(t.localScale));
            foreach (var c in t.GetComponents<Component>()) if (c != null && (c is Renderer || c.GetType().Name == "MeshFilter")) { sb.Append(" visual=").Append(c.GetType().FullName).Append("#").Append(ObjectId(c)); if (c is Renderer) sb.Append(" enabled=").Append(((Renderer)c).enabled); var mesh = c.GetType().GetProperty("sharedMesh", F); if (mesh != null) sb.Append(" mesh=").Append(ObjectId(mesh.GetValue(c, null) as UnityEngine.Object)); }
        }
        var textType = TypeOf("TMPro.TMP_Text"); var texts = sync.GetComponentsInChildren(textType, true).OfType<Component>().OrderBy(x => PathOf(x.transform)).ToArray(); if (texts.Length == 0) throw new InvalidOperationException("No TMP text components on " + PathOf(post.transform));
        foreach (var text in texts) sb.Append(" TMP=").Append(ObjectId(text)).Append("/").Append(PathOf(text.transform)).Append(" active=").Append(text.gameObject.activeInHierarchy).Append(" enabled=").Append(((Behaviour)text).enabled);
        return sb.ToString();
    }
    private string TextState(Component post)
    {
        var sync = (Component)Read(post, "_textSynchronizer"); var sb = new StringBuilder(); sb.Append("synchronizer=").Append(Read(sync, "text"));
        foreach (var text in sync.GetComponentsInChildren(TypeOf("TMPro.TMP_Text"), true).OfType<Component>().OrderBy(x => PathOf(x.transform))) sb.Append(" TMP#").Append(ObjectId(text)).Append("=").Append(Read(text, "text"));
        return sb.ToString();
    }
    private string GroupState(Component post)
    {
        var rel = post.GetComponents<Component>().Where(c => c != null && c.GetType().FullName == "Track.TrackRelativePosition").ToArray();
        if (rel.Length != 1) throw new InvalidOperationException("Expected one track-relative component on " + PathOf(post.transform));
        var enabled = Read(graph, "enabledGroupIds") as IList ?? throw new InvalidOperationException("Missing enabled groups");
        var id = Convert.ToString(Read(rel[0], "_observedGroupId"));
        return "relative=" + ObjectId(rel[0]) + " target=" + ObjectId(Read(rel[0], "targetTransform") as UnityEngine.Object) + " follows=" + Read(rel[0], "targetActiveFollowsTrackGroup") + " observing=" + Read(rel[0], "_observing") + " group=" + id + " enabled=" + enabled.Contains(id);
    }
    private static string FullPath(string value) { return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
    private bool CorrectHome()
    {
        try
        {
            const string home = @"C:\TMP\RailroaderMapResearch\RuntimeHome";
            var args = Environment.GetCommandLineArgs(); var matches = Enumerable.Range(0, args.Length).Where(i => string.Equals(args[i], "-homedir", StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length > 0 && matches[0] + 1 < args.Length
                && string.Equals(FullPath(args[matches[0] + 1]), FullPath(home), StringComparison.OrdinalIgnoreCase)
                && string.Equals(FullPath(modDirectory), FullPath(Path.Combine(home, "Mods", "MilepostProbe")), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    private bool SameWorld()
    {
        try
        {
            return initialized && CorrectHome() && Convert.ToString(Read(TypeOf("Game.ActiveMapDefinition"), "Identifier")) == "crankyant/scene-operations-map"
                && ReferenceEquals(Read(TypeOf("Track.Graph"), "Shared"), graph)
                && origin != null && dependent != null && marker != null && depMarker != null
                && origin.gameObject.scene.IsValid() && origin.gameObject.scene.isLoaded && origin.gameObject.scene.handle == originSceneHandle
                && dependent.gameObject.scene.IsValid() && dependent.gameObject.scene.isLoaded && dependent.gameObject.scene.handle == dependentSceneHandle
                && marker.gameObject.scene.IsValid() && marker.gameObject.scene.isLoaded
                && depMarker.gameObject.scene.IsValid() && depMarker.gameObject.scene.isLoaded
                && ReferenceEquals(Read(origin, "TrackMarker"), marker) && ReferenceEquals(Read(dependent, "TrackMarker"), depMarker)
                && ReferenceEquals(Read(TypeOf("Helpers.MilePost"), "_originMilePosts"), cache);
        }
        catch { return false; }
    }
    private void DropWorld(string reason)
    {
        Write("ABANDON " + reason + " dirty=" + changed + "; no binding or cache restoration attempted");
        initialized = false; changed = false; stage = -1; origin = dependent = marker = depMarker = null;
        graph = original = candidate = priorCache = null; cache = null; location = null;
        stableBaseline = textBaseline = groupBaseline = null;
        status = "World changed. Abandon the disposable session without saving.";
    }
    public void WorldUnloading() { if (initialized) DropWorld("world unloading"); }
    private void RequireWorld()
    {
        if (!SameWorld()) { DropWorld("world, graph or object lifetime changed"); throw new InvalidOperationException("Original world unavailable; do not restore stale state"); }
    }
    private void Snapshot(string phase)
    {
        RequireWorld();
        if ((WorldOrigin() - worldOrigin).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Floating origin drifted");
        if (!cache.Contains("T") || !ReferenceEquals(cache["T"], origin)) throw new InvalidOperationException("T cache entry changed");
        var resolved = (Component)Read(dependent, "Origin");
        Write(phase + " cache=" + ObjectId((Component)cache["T"]) + " dependentOrigin=" + ObjectId(resolved) + " sameOrigin=" + ReferenceEquals(resolved, origin));
        if (!ReferenceEquals(resolved, origin)) throw new InvalidOperationException("Dependent resolved a different origin");
        Write(phase + " originRegistration=" + Registration(marker, graph) + " dependentRegistration=" + Registration(depMarker, graph));
        var a = Stable(origin); var b = Stable(dependent); Write(phase + " " + a); Write(phase + " " + b);
        var text = TextState(origin) + " | " + TextState(dependent); var groups = GroupState(origin) + " | " + GroupState(dependent); Write(phase + " text " + text); Write(phase + " groups " + groups);
        if (stableBaseline == null) { stableBaseline = a + "\n" + b; textBaseline = text; groupBaseline = groups; }
        else if (phase == "restored" && (stableBaseline != a + "\n" + b || textBaseline != text || groupBaseline != groups)) throw new InvalidOperationException("Restored state differs from baseline");
        var s = Read(marker, "_location"); Write(phase + " binding segment=" + Read(s, "segmentId") + " distance=" + Read(s, "distance") + " end=" + Read(s, "end"));
    }
    private void FocusDependent()
    {
        if (stage != 0 || initialized) throw new InvalidOperationException("Focus is available only before capture");
        if (!CorrectHome() || Convert.ToString(Read(TypeOf("Game.ActiveMapDefinition"), "Identifier")) != "crankyant/scene-operations-map") throw new InvalidOperationException("Wrong test home or map");
        var post = One("World/Mile Posts/T-47", "Helpers.MilePost");
        var cameraType = TypeOf("CameraSelector"); var selector = Read(cameraType, "shared") ?? throw new InvalidOperationException("Camera selector unavailable");
        var identifier = cameraType.GetNestedType("CameraIdentifier", F) ?? throw new InvalidOperationException("Camera identifier unavailable");
        var strategy = Enum.Parse(identifier, "Strategy");
        var toGame = TypeOf("Helpers.WorldTransformer").GetMethod("WorldToGame", F, null, new[] { typeof(Vector3) }, null) ?? throw new MissingMethodException("WorldToGame(Vector3)");
        var position = (Vector3)toGame.Invoke(null, new object[] { post.transform.position });
        cameraType.GetMethod("JumpToPoint").Invoke(selector, new object[] { position, Quaternion.identity, strategy });
        Write("focus T-47 requested; allow floating origin to settle before capture");
        status = "Focus requested. Allow the origin to settle before capture.";
    }
    private void StartTrial()
    {
        if (stage != 0) throw new InvalidOperationException("Trial already active");
        if (!CorrectHome()) throw new InvalidOperationException("Wrong or ambiguous test home or mod directory");
        if (Convert.ToString(Read(TypeOf("Game.ActiveMapDefinition"), "Identifier")) != "crankyant/scene-operations-map") throw new InvalidOperationException("Wrong map");
        origin = One("World/Mile Posts/T-46 Origin", "Helpers.MilePost"); dependent = One("World/Mile Posts/T-47", "Helpers.MilePost");
        if (Convert.ToString(Read(origin, "prefix")) != "T" || !(bool)Read(origin, "origin") || Convert.ToString(Read(dependent, "prefix")) != "T" || (bool)Read(dependent, "origin")) throw new InvalidOperationException("Milepost identity mismatch");
        marker = (Component)Read(origin, "TrackMarker"); depMarker = (Component)Read(dependent, "TrackMarker");
        if (Convert.ToString(Read(marker, "id")) != "Mdow" || Convert.ToString(Read(depMarker, "id")) != "Mnau" || !((Behaviour)marker).enabled || !((Behaviour)depMarker).enabled || !marker.gameObject.activeInHierarchy || !depMarker.gameObject.activeInHierarchy) throw new InvalidOperationException("Markers are not expected active objects");
        graph = Read(TypeOf("Track.Graph"), "Shared") ?? throw new InvalidOperationException("Missing graph"); cache = Read(TypeOf("Helpers.MilePost"), "_originMilePosts") as IDictionary ?? throw new InvalidOperationException("Missing cache"); location = marker.GetType().GetProperty("Location", F);
        original = location.GetValue(marker, null) ?? throw new InvalidOperationException("Original location unresolved"); candidate = location.GetValue(depMarker, null) ?? throw new InvalidOperationException("Dependent location unresolved");
        if (original.Equals(candidate)) throw new InvalidOperationException("Alternative location equals original");
        var origSerial = Read(marker, "_location"); var destSerial = Read(depMarker, "_location"); if (Convert.ToString(Read(origSerial, "segmentId")) != "Stvw" || Convert.ToString(Read(destSerial, "segmentId")) != "Stky0cg9c") throw new InvalidOperationException("Unexpected fixture segments");
        Registration(marker, graph); Registration(depMarker, graph); worldOrigin = WorldOrigin(); cacheExisted = cache.Contains("T"); priorCache = cacheExisted ? cache["T"] : null;
        originSceneHandle = origin.gameObject.scene.handle; dependentSceneHandle = dependent.gameObject.scene.handle;
        initialized = true; RequireWorld();
        var resolved = Read(dependent, "Origin"); if (!ReferenceEquals(resolved, origin) || !cache.Contains("T") || !ReferenceEquals(cache["T"], origin)) throw new InvalidOperationException("Cannot populate expected original cache entry");
        Write("initial cacheExisted=" + cacheExisted + " prior=" + ObjectId(priorCache as UnityEngine.Object)); Snapshot("baseline"); stage = 1; frame = Time.frameCount; status = "Baseline captured. Inspect T-47 visually, then apply.";
    }
    private void Bind(object target)
    {
        RequireWorld();
        changed = true; ((Behaviour)marker).enabled = false; try { RequireWorld(); location.SetValue(marker, target, null); } finally { if (SameWorld()) ((Behaviour)marker).enabled = true; else if (initialized) DropWorld("world changed during binding"); }
        RequireWorld();
        if (!target.Equals(location.GetValue(marker, null))) throw new InvalidOperationException("Binding assignment failed");
    }
    private void Apply() { if (stage != 1) throw new InvalidOperationException("Wrong phase"); Bind(candidate); stage = 2; frame = Time.frameCount; status = "Rebound. Inspect T-47 visually, then capture after a frame."; }
    private void Observe() { if (Time.frameCount <= frame) throw new InvalidOperationException("Wait for another frame"); if (stage == 2) { Snapshot("rebound"); stage = 3; status = "Rebound captured. Repeat assignment."; } else if (stage == 4) { Snapshot("repeated"); stage = 5; status = "Repeat captured. Restore original binding."; } else if (stage == 6) { Snapshot("restored"); if (!original.Equals(location.GetValue(marker, null))) throw new InvalidOperationException("Original binding not restored"); Finish(); status = "Restored. Inspect log and exit without saving."; } else throw new InvalidOperationException("Wrong phase"); }
    private void Repeat() { if (stage != 3) throw new InvalidOperationException("Wrong phase"); Bind(candidate); stage = 4; frame = Time.frameCount; status = "Repeated. Inspect T-47 visually, then capture after a frame."; }
    private void Restore() { if (stage != 5) throw new InvalidOperationException("Wrong phase"); Bind(original); changed = false; stage = 6; frame = Time.frameCount; status = "Restored binding. Inspect T-47 visually, then capture after a frame."; }
    private void Finish() { RequireWorld(); if (cacheExisted) cache["T"] = priorCache; else cache.Remove("T"); if (cache.Contains("T") != cacheExisted || (cacheExisted && !ReferenceEquals(cache["T"], priorCache))) throw new InvalidOperationException("Prior cache restore failed"); Write("final prior cache presence and identity restored; binding and registrations checked"); initialized = false; stage = 0; stableBaseline = null; }
    public void Abort()
    {
        if (!initialized) return;
        if (!SameWorld()) { DropWorld("abort after world, graph or object lifetime changed"); return; }
        try { if (changed) { Bind(original); changed = false; } if (!original.Equals(location.GetValue(marker, null))) throw new InvalidOperationException("Original binding not restored"); Registration(marker, graph); Registration(depMarker, graph); RequireWorld(); if (cacheExisted) cache["T"] = priorCache; else cache.Remove("T"); if (cache.Contains("T") != cacheExisted || (cacheExisted && !ReferenceEquals(cache["T"], priorCache))) throw new InvalidOperationException("Prior cache restore failed"); Write("abort restored binding, registrations and prior cache"); initialized = false; stage = 0; stableBaseline = null; }
        catch (Exception e) { Write("RESTORE ERROR " + e); status = "Restore failed. Abandon disposable session without saving."; }
    }
    private void Do(Action action) { try { action(); } catch (Exception e) { Write("ERROR " + e); status = "Trial failed. Abandon disposable session without saving."; Abort(); } }
    private void OnGUI() { window = GUI.Window(0x43524d50, window, id => { GUILayout.Label(status); if (stage == 0 && GUILayout.Button("Focus T-47")) Do(FocusDependent); if (stage == 0 && GUILayout.Button("Capture baseline and populate cache")) Do(StartTrial); if (stage == 1 && GUILayout.Button("Apply alternative binding")) Do(Apply); if ((stage == 2 || stage == 4 || stage == 6) && GUILayout.Button("Capture observed phase")) Do(Observe); if (stage == 3 && GUILayout.Button("Repeat binding")) Do(Repeat); if (stage == 5 && GUILayout.Button("Restore binding")) Do(Restore); if (stage > 0 && GUILayout.Button("Abort and restore")) Abort(); GUI.DragWindow(); }, "Milepost Probe 25"); }
}
