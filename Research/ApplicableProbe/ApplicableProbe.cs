// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

public sealed class ApplicableProbeEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    public void OnModLoad(IModContext context)
    {
        host = new GameObject("CrankyAnt ApplicableProbe");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<ApplicableProbe>().Initialize(context.ModDirectory);
    }
    public void OnWorldLoad() { }
    public void OnWorldUnload() { }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}

public sealed class ApplicableProbe : MonoBehaviour
{
    private string log;
    private string status = "Load the disposable world, then run preflight.";
    private Rect window = new Rect(20, 200, 380, 390);
    private Transform bridge;
    private Transform bridgeParent;
    private Vector3 bridgePosition, bridgeOrigin;
    private Quaternion bridgeRotation;
    private bool bridgeActive, bridgeDirty;
    private Transform post, postParent;
    private Vector3 postPosition, postOrigin;
    private Quaternion postRotation;
    private bool postActive, postDirty;
    public void Initialize(string directory) { log = Path.Combine(directory, "ApplicableProbe.log"); Write("loaded"); }
    private void Write(string message) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine); }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static bool Loaded(Component c) { return c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded; }
    private static string Value(object value)
    {
        if (value == null) return "null";
        if (value is Component c) return "component:" + c.GetType().FullName + ":" + PathOf(c.transform);
        if (value is GameObject g) return "object:" + PathOf(g.transform);
        if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum) return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        if (value is System.Collections.ICollection collection) return "collection count=" + collection.Count;
        return "type:" + value.GetType().FullName;
    }
    private void NestedValue(string path, object value)
    {
        if (value == null) return;
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type.Namespace == null || !type.Namespace.StartsWith("Track") || value is UnityEngine.Object) return;
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            try { Write("nested " + path + "." + field.Name + "=" + Value(field.GetValue(value))); }
            catch (Exception e) { Write("nested unavailable " + path + "." + field.Name + " " + e.GetType().Name); }
    }
    private void Fields(Component component)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            foreach (var field in type.GetFields(flags))
                try { var value = field.GetValue(component); var name = type.FullName + "." + field.Name; Write("field " + name + "=" + Value(value)); NestedValue(name, value); }
                catch (Exception e) { Write("field unavailable " + field.Name + " " + e.GetType().Name); }
    }
    private void Hierarchy(string path)
    {
        var found = Resources.FindObjectsOfTypeAll<Transform>().Where(t => Loaded(t) && PathOf(t) == path).ToArray();
        Write("hierarchy path=" + path + " matches=" + found.Length);
        if (found.Length != 1) return;
        foreach (var t in found[0].GetComponentsInChildren<Transform>(true))
        {
            Write("node " + PathOf(t) + " activeSelf=" + t.gameObject.activeSelf + " activeHierarchy=" + t.gameObject.activeInHierarchy + " localPosition=" + t.localPosition.ToString("F4") + " localRotation=" + t.localRotation.ToString("F4"));
            foreach (var c in t.GetComponents<Component>()) if (c != null)
            {
                Write("component " + c.GetType().FullName + " enabled=" + (c is Behaviour b ? b.enabled.ToString() : "n/a"));
                if (c.GetType().FullName == "Track.TrackSpan" || c.GetType().FullName == "Track.Crossings.GradeCrossingController") Fields(c);
            }
        }
    }
    private void Scan()
    {
        try
        {
            Write("preflight begin");
            Hierarchy("World/Large Scenery/Ela/Ela Bridge");
            Hierarchy("World/Large Scenery/Ela/Ela Bridge Scenery");
            Hierarchy("World/Large Scenery/Bryson/Grade Crossing - Bryson Everett");
            var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(Loaded).ToArray();
            var posts = all.Where(c => c.GetType().FullName == "Helpers.MilePost").ToArray();
            Write("milepost count=" + posts.Length);
            foreach (var c in posts) { Write("milepost " + PathOf(c.transform)); Fields(c); }
            var relatives = all.Where(c => c.GetType().FullName == "Track.TrackRelativePosition").ToArray();
            var field = relatives.FirstOrDefault()?.GetType().GetField("targetActiveFollowsTrackGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Write("relative count=" + relatives.Length + " groupField=" + (field != null));
            if (field != null) foreach (var c in relatives) if (field.GetValue(c) is bool follows && follows) { Write("groupFollower " + PathOf(c.transform)); Fields(c); }
            Write("preflight complete"); status = "Preflight complete; inspect ApplicableProbe.log.";
        }
        catch (Exception e) { Write("preflight ERROR " + e); status = "Preflight failed; inspect log."; }
    }
    private static Vector3 Origin()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Helpers.WorldTransformer", false)).First(t => t != null);
        var method = type.GetMethod("WorldToGame", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);
        if (method == null) throw new MissingMethodException("WorldToGame(Vector3)");
        return (Vector3)method.Invoke(null, new object[] { Vector3.zero });
    }
    private void FocusPath(string path)
    {
        try
        {
            var found = Resources.FindObjectsOfTypeAll<Transform>().Where(t => Loaded(t) && PathOf(t) == path).ToArray();
            if (found.Length != 1) throw new InvalidOperationException("Focus match count=" + found.Length);
            var cameraType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("CameraSelector", false)).First(t => t != null);
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var selector = cameraType.GetProperty("shared", flags)?.GetValue(null, null) ?? cameraType.GetField("shared", flags)?.GetValue(null);
            if (selector == null) throw new InvalidOperationException("Camera selector unavailable.");
            var identifier = cameraType.GetNestedType("CameraIdentifier", BindingFlags.Public | BindingFlags.NonPublic);
            var strategy = Enum.Parse(identifier, "Strategy");
            var worldType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Helpers.WorldTransformer", false)).First(t => t != null);
            var toGame = worldType.GetMethod("WorldToGame", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);
            if (toGame == null) throw new MissingMethodException("WorldToGame(Vector3)");
            var gamePosition = (Vector3)toGame.Invoke(null, new object[] { found[0].position });
            cameraType.GetMethod("JumpToPoint").Invoke(selector, new object[] { gamePosition, Quaternion.identity, strategy });
            Write("focus path=" + path + " game=" + gamePosition.ToString("F4") + " native selection unchanged by probe");
            status = "Focus requested; capture a new baseline after origin settles.";
        }
        catch (Exception e) { Write("focus bridge ERROR " + e); status = "Bridge focus failed; inspect log."; }
    }
    private void BridgeSnapshot(string phase)
    {
        Write(phase + " origin=" + Origin().ToString("F4") + " root=" + PathOf(bridge) + " worldPosition=" + bridge.position.ToString("F4") + " localPosition=" + bridge.localPosition.ToString("F4") + " worldRotation=" + bridge.rotation.ToString("F4"));
        foreach (var t in bridge.GetComponentsInChildren<Transform>(true))
        {
            Write(phase + " node=" + PathOf(t) + " worldPosition=" + t.position.ToString("F4") + " worldRotation=" + t.rotation.ToString("F4") + " activeSelf=" + t.gameObject.activeSelf + " activeHierarchy=" + t.gameObject.activeInHierarchy);
            foreach (var c in t.GetComponents<Collider>()) Write(phase + " collider=" + c.GetType().FullName + " enabled=" + c.enabled + " bounds=" + c.bounds);
        }
    }
    private void CaptureBridge()
    {
        try
        {
            if (bridge != null) throw new InvalidOperationException("Restore captured bridge first.");
            var found = Resources.FindObjectsOfTypeAll<Transform>().Where(t => Loaded(t) && PathOf(t) == "World/Large Scenery/Ela/Ela Bridge").ToArray();
            if (found.Length != 1) throw new InvalidOperationException("Bridge match count=" + found.Length);
            var target = found[0];
            if (!target.gameObject.activeSelf || !target.gameObject.activeInHierarchy || target.parent == null || !target.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == "Helpers.Culling.RendererCuller")) throw new InvalidOperationException("Bridge baseline checks failed.");
            bridgeOrigin = Origin(); bridgePosition = target.position; bridgeRotation = target.rotation; bridgeActive = target.gameObject.activeSelf; bridgeParent = target.parent; bridge = target;
            BridgeSnapshot("bridge baseline"); status = "Bridge captured. Move or disable, then restore.";
        }
        catch (Exception e) { Write("bridge capture ERROR " + e); status = "Bridge capture failed."; }
    }
    private void CheckBridge()
    {
        if (bridge == null || bridge.parent != bridgeParent || (Origin() - bridgeOrigin).sqrMagnitude >= 0.0001f) throw new InvalidOperationException("Bridge, parent or origin changed; do not save.");
    }
    private void MoveBridge()
    {
        try { CheckBridge(); if (!bridge.gameObject.activeSelf) throw new InvalidOperationException("Restore activity first."); bridgeDirty = true; bridge.position = bridgePosition + new Vector3(5, 0, 0); bridge.rotation = Quaternion.Euler(0, 15, 0) * bridgeRotation; BridgeSnapshot("bridge moved"); status = "Bridge moved; restore before leaving."; }
        catch (Exception e) { Write("bridge move ERROR " + e); status = "Bridge move failed; inspect log and restore."; }
    }
    private void DisableBridge()
    {
        try { CheckBridge(); if ((bridge.position - bridgePosition).sqrMagnitude > 0.0001f || Quaternion.Angle(bridge.rotation, bridgeRotation) > 0.01f) throw new InvalidOperationException("Restore pose before activity test."); bridgeDirty = true; bridge.gameObject.SetActive(false); BridgeSnapshot("bridge disabled"); status = "Bridge disabled; restore before leaving."; }
        catch (Exception e) { Write("bridge disable ERROR " + e); status = "Bridge disable failed; inspect log and restore."; }
    }
    private void RestoreBridge()
    {
        try { CheckBridge(); bridge.position = bridgePosition; bridge.rotation = bridgeRotation; bridge.gameObject.SetActive(bridgeActive); if ((bridge.position - bridgePosition).sqrMagnitude > 0.0001f || Quaternion.Angle(bridge.rotation, bridgeRotation) > 0.01f || bridge.gameObject.activeSelf != bridgeActive) throw new InvalidOperationException("Restore postcondition failed."); BridgeSnapshot("bridge restored"); bridgeDirty = false; bridge = null; status = "Bridge root restored; inspect child and collider logs."; }
        catch (Exception e) { Write("bridge restore ERROR " + e); status = "Restore failed; abandon disposable session without saving."; }
    }
    private void PostSnapshot(string phase)
    {
        foreach (var path in new[] { "World/Mile Posts/T-46 Origin", "World/Mile Posts/T-47" })
        {
            var found = Resources.FindObjectsOfTypeAll<Transform>().Where(t => Loaded(t) && PathOf(t) == path).ToArray();
            if (found.Length != 1) throw new InvalidOperationException("Milepost count for " + path + "=" + found.Length);
            var t = found[0]; Write(phase + " post=" + path + " origin=" + Origin().ToString("F4") + " position=" + t.position.ToString("F4") + " rotation=" + t.rotation.ToString("F4") + " activeSelf=" + t.gameObject.activeSelf + " activeHierarchy=" + t.gameObject.activeInHierarchy);
            foreach (var c in t.GetComponents<Component>()) if (c != null && (c.GetType().FullName == "Helpers.MilePost" || c.GetType().FullName == "Track.TrackMarker" || c.GetType().FullName == "Track.TrackRelativePosition" || c.GetType().FullName == "Helpers.TextSynchronizer")) { Write(phase + " component=" + c.GetType().FullName + " enabled=" + (c is Behaviour b ? b.enabled.ToString() : "n/a")); Fields(c); if (c.GetType().FullName == "Track.TrackMarker") PostRegistrations(phase, c); }
        }
    }
    private void PostRegistrations(string phase, Component marker)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var idField = marker.GetType().GetField("id", flags) ?? throw new MissingFieldException("TrackMarker.id");
        var id = Convert.ToString(idField.GetValue(marker));
        var graphType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Track.Graph", false)).First(t => t != null);
        var graph = graphType.GetProperty("Shared", flags)?.GetValue(null, null) ?? throw new InvalidOperationException("Graph unavailable.");
        var buckets = graphType.GetField("_namedLocationsBySegmentId", flags)?.GetValue(graph) as System.Collections.IDictionary ?? throw new InvalidOperationException("Graph buckets unavailable.");
        var count = 0;
        foreach (System.Collections.DictionaryEntry bucket in buckets)
        {
            var entries = bucket.Value as System.Collections.IDictionary ?? throw new InvalidOperationException("Graph bucket shape unsupported.");
            foreach (System.Collections.DictionaryEntry entry in entries) if (Convert.ToString(entry.Key) == id)
            {
                count++; var item = entry.Value; var objectField = item?.GetType().GetField("ObjectValue", flags);
                var objectProperty = item?.GetType().GetProperty("ObjectValue", flags);
                var registeredObject = objectProperty != null ? objectProperty.GetValue(item, null) : objectField?.GetValue(item);
                Write(phase + " graph marker=" + id + " bucket=" + bucket.Key + " objectMatches=" + ReferenceEquals(registeredObject, marker) + " objectAccessor=" + (objectProperty != null || objectField != null));
            }
        }
        Write(phase + " graph marker=" + id + " count=" + count);
    }
    private void CapturePost()
    {
        try
        {
            if (post != null) throw new InvalidOperationException("Restore previous milepost capture first.");
            var found = Resources.FindObjectsOfTypeAll<Transform>().Where(t => Loaded(t) && PathOf(t) == "World/Mile Posts/T-46 Origin").ToArray();
            if (found.Length != 1 || found[0].parent == null || !found[0].gameObject.activeSelf || !found[0].gameObject.activeInHierarchy) throw new InvalidOperationException("Milepost baseline match or activity failed.");
            var target = found[0]; var mile = target.GetComponents<Component>().Single(c => c != null && c.GetType().FullName == "Helpers.MilePost");
            var originField = mile.GetType().GetField("origin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var prefixField = mile.GetType().GetField("prefix", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (!(originField.GetValue(mile) is bool isOrigin) || !isOrigin || Convert.ToString(prefixField.GetValue(mile)) != "T") throw new InvalidOperationException("Milepost identity fields differ.");
            postOrigin = Origin(); postPosition = target.position; postRotation = target.rotation; postActive = target.gameObject.activeSelf; postParent = target.parent; post = target; PostSnapshot("post baseline"); status = "Origin post captured; move or disable, then restore.";
        }
        catch (Exception e) { Write("post capture ERROR " + e); status = "Milepost capture failed."; }
    }
    private void CheckPost() { if (post == null || post.parent != postParent || (Origin() - postOrigin).sqrMagnitude >= 0.0001f) throw new InvalidOperationException("Post, parent or origin changed; do not save."); }
    private void MovePost()
    {
        try { CheckPost(); if (!post.gameObject.activeSelf) throw new InvalidOperationException("Restore activity first."); postDirty = true; post.position = postPosition + new Vector3(5, 0, 0); post.rotation = Quaternion.Euler(0, 15, 0) * postRotation; PostSnapshot("post moved"); status = "Post moved; restore before leaving."; }
        catch (Exception e) { Write("post move ERROR " + e); status = "Post move failed; restore."; }
    }
    private void DisablePost()
    {
        try { CheckPost(); if ((post.position - postPosition).sqrMagnitude > 0.0001f || Quaternion.Angle(post.rotation, postRotation) > 0.01f) throw new InvalidOperationException("Restore pose first."); postDirty = true; post.gameObject.SetActive(false); PostSnapshot("post disabled"); status = "Post disabled; restore before leaving."; }
        catch (Exception e) { Write("post disable ERROR " + e); status = "Post disable failed; restore."; }
    }
    private void RestorePost()
    {
        try { CheckPost(); post.position = postPosition; post.rotation = postRotation; post.gameObject.SetActive(postActive); if ((post.position - postPosition).sqrMagnitude > 0.0001f || Quaternion.Angle(post.rotation, postRotation) > 0.01f || post.gameObject.activeSelf != postActive) throw new InvalidOperationException("Restore postcondition failed."); PostSnapshot("post restored"); postDirty = false; post = null; status = "Post root restored; compare dependent fields."; }
        catch (Exception e) { Write("post restore ERROR " + e); status = "Post restoration failed; abandon disposable session."; }
    }
    private void OnGUI()
    {
        window = GUI.Window(0x43524150, window, id => { GUILayout.Label(status); if (GUILayout.Button("Run read-only preflight")) Scan(); if (GUILayout.Button("Focus bridge")) FocusPath("World/Large Scenery/Ela/Ela Bridge"); if (GUILayout.Button("Capture bridge")) CaptureBridge(); if (GUILayout.Button("Move bridge")) MoveBridge(); if (GUILayout.Button("Disable bridge")) DisableBridge(); if (GUILayout.Button("Restore bridge")) RestoreBridge(); if (GUILayout.Button("Focus T-46 origin")) FocusPath("World/Mile Posts/T-46 Origin"); if (GUILayout.Button("Capture T-46 origin")) CapturePost(); if (GUILayout.Button("Move T-46")) MovePost(); if (GUILayout.Button("Disable T-46")) DisablePost(); if (GUILayout.Button("Restore T-46")) RestorePost(); if (bridgeDirty || postDirty) GUILayout.Label("Restore before leaving; never save this test."); GUI.DragWindow(); }, "Applicable Probe");
    }
}
