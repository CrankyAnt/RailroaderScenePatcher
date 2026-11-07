// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

public sealed class SceneProbeEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    public void OnModLoad(IModContext context)
    {
        host = new GameObject("CrankyAnt SceneProbe");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<SceneProbe>().Initialize(context.ModDirectory);
    }
    public void OnWorldLoad() { }
    public void OnWorldUnload() { if (host != null) { host.GetComponent<SceneProbe>().MarkerRestore(); host.GetComponent<SceneProbe>().Restore(); } }
    public void OnModUnload() { if (host != null) { host.GetComponent<SceneProbe>().MarkerRestore(); host.GetComponent<SceneProbe>().Restore(); UnityEngine.Object.Destroy(host); } }
}

public sealed class SceneProbe : MonoBehaviour
{
    private const int WindowId = 0x43525350;
    private Rect windowRect = new Rect(670, 10, 380, 470);
    private string log;
    private GameObject board;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private bool originalActive;
    private Component marker;
    private object markerOriginalLocation;
    private Transform markerTarget;
    private Vector3 markerTargetPosition;
    private Quaternion markerTargetRotation;
    private Transform markerTargetParent;
    private string replacementSegment = "";
    private bool markerChanged;
    private bool componentChanged;
    private bool targetChanged;
    private bool rootChanged;
    private Vector3 boardOrigin;
    private Vector3 markerOrigin;
    private bool rebindVerified;
    private string status = "Resolve the board after loading a disposable world.";
    private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    public void Initialize(string directory) { log = Path.Combine(directory, "SceneProbe.log"); Write("probe loaded"); }
    private void Write(string value) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + value + Environment.NewLine); }
    private void Resolve()
    {
        if (board != null) { status = "Restore the current board before resolving again."; return; }
        var matches = Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.scene.IsValid() && g.scene.isLoaded && PathOf(g.transform) == "World/Signs/Loc: Sylva Int").ToArray();
        Write("resolve board matches=" + matches.Length);
        if (matches.Length != 1) { status = "Expected exactly one board, found " + matches.Length; return; }
        try { boardOrigin = Origin(); } catch (Exception e) { status = "Origin unavailable: " + e.Message; return; }
        board = matches[0]; originalPosition = board.transform.position; originalRotation = board.transform.rotation; originalActive = board.activeSelf;
        status = "Board resolved; baseline captured."; Snapshot("baseline");
    }
    private void Snapshot(string phase)
    {
        if (board == null) { status = "Resolve board first."; return; }
        Write(phase + " instance=" + board.GetInstanceID() + " path=" + PathOf(board.transform) + " worldPosition=" + board.transform.position.ToString("F4") + " localPosition=" + board.transform.localPosition.ToString("F4") + " worldRotation=" + board.transform.rotation.ToString("F4") + " localRotation=" + board.transform.localRotation.ToString("F4") + " activeSelf=" + board.activeSelf + " activeHierarchy=" + board.activeInHierarchy);
        foreach (var t in board.GetComponentsInChildren<Transform>(true))
            Write(phase + " child=" + PathOf(t) + " activeSelf=" + t.gameObject.activeSelf + " activeHierarchy=" + t.gameObject.activeInHierarchy + " components=" + string.Join(",", t.GetComponents<Component>().Select(c => c == null ? "missing" : c.GetType().FullName).ToArray()));
        status = phase + " recorded.";
    }
    private void Move()
    {
        if (board == null) { status = "Resolve board first."; return; }
        if (!OriginMatches(boardOrigin)) return;
        board.transform.position = originalPosition + new Vector3(10, 0, 0);
        board.transform.rotation = Quaternion.Euler(0, 15, 0) * originalRotation;
        Snapshot("move");
    }
    private void Hide()
    {
        if (board == null) { status = "Resolve board first."; return; }
        if (!OriginMatches(boardOrigin)) return;
        board.SetActive(false); Snapshot("hide");
    }
    private static object Read(object value, string name)
    {
        if (value == null) return null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = value.GetType();
        try { return type.GetProperty(name, flags)?.GetValue(value, null) ?? type.GetField(name, flags)?.GetValue(value); }
        catch { return null; }
    }
    private static object Strict(object value, string name)
    {
        if (value == null) throw new InvalidOperationException("Null receiver for " + name);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var p = value.GetType().GetProperty(name, flags);
        var f = value.GetType().GetField(name, flags);
        object result = p != null ? p.GetValue(value, null) : f != null ? f.GetValue(value) : throw new MissingMemberException(value.GetType().FullName, name);
        return result ?? throw new InvalidOperationException("Null " + name);
    }
    private static Vector3 Origin()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Helpers.WorldTransformer", false)).First(t => t != null);
        var method = type.GetMethod("WorldToGame", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);
        if (method == null) throw new MissingMethodException("WorldToGame(Vector3)");
        return (Vector3)method.Invoke(null, new object[] { Vector3.zero });
    }
    private bool OriginMatches(Vector3 original)
    {
        try { if ((Origin() - original).sqrMagnitude < 0.0001f) return true; status = "Origin drift: mutation/restore refused."; Write(status); }
        catch (Exception e) { status = "Origin unavailable: operation refused."; Write(status + " " + e); }
        return false;
    }
    private object VerifiedLocation(Component m, string expectedSegment)
    {
        if (m == null || Convert.ToString(Strict(m, "id")) != "M5pp") throw new InvalidOperationException("Marker ID mismatch");
        if (Resources.FindObjectsOfTypeAll(m.GetType()).OfType<Component>().Count(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && Convert.ToString(Strict(c, "id")) == "M5pp") != 1) throw new InvalidOperationException("Marker ID not unique");
        var graphType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Track.Graph", false)).First(t => t != null);
        var graph = graphType.GetProperty("Shared", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null) ?? throw new InvalidOperationException("Graph unavailable");
        var serial = Strict(m, "_location");
        var segment = Convert.ToString(Strict(serial, "segmentId"));
        var distance = Convert.ToSingle(Strict(serial, "distance"));
        var end = Strict(serial, "end");
        if (segment != expectedSegment || Math.Abs(distance - 58.69453f) > 0.001f || Convert.ToString(end) != "A") throw new InvalidOperationException("Serialized location differs from expected segment/distance/end");
        var args = new[] { serial, (object)null };
        if (!(bool)graphType.GetMethod("TryResolveLocation").Invoke(graph, args)) throw new InvalidOperationException("Marker location does not resolve");
        var location = Strict(m, "Location");
        if (location.GetType().FullName != "Track.Location" || args[1] == null || args[1].GetType() != location.GetType() || !location.Equals(args[1])) throw new InvalidOperationException("Location getter differs from resolved serialized location");
        var buckets = Strict(graph, "_namedLocationsBySegmentId") as IDictionary ?? throw new InvalidOperationException("Unsupported graph dictionary");
        int count = 0;
        foreach (DictionaryEntry bucket in buckets) { var entries = bucket.Value as IDictionary ?? throw new InvalidOperationException("Unsupported graph bucket"); foreach (DictionaryEntry entry in entries) if (Convert.ToString(entry.Key) == "M5pp") { count++; var item = entry.Value; var itemLocation = Strict(item, "Location"); Write("registration bucket=" + bucket.Key + " id=" + Strict(item, "Id") + " location=" + itemLocation + " objectMatches=" + ReferenceEquals(Strict(item, "ObjectValue"), m)); if (Convert.ToString(bucket.Key) != segment || Convert.ToString(Strict(item, "Id")) != "M5pp" || !location.Equals(itemLocation) || !ReferenceEquals(Strict(item, "ObjectValue"), m)) throw new InvalidOperationException("Registration contents mismatch"); } }
        if (count != 1) throw new InvalidOperationException("Expected one M5pp registration");
        Write("verified segment=" + segment + " distance=" + distance + " end=" + end);
        return location;
    }
    private static int Registrations(out string segment)
    {
        segment = null;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Track.Graph", false)).FirstOrDefault(t => t != null);
        var graph = type?.GetProperty("Shared", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null, null);
        var buckets = Read(graph, "_namedLocationsBySegmentId") as IDictionary;
        if (buckets == null) return -1;
        int count = 0;
        foreach (DictionaryEntry bucket in buckets) { var entries = bucket.Value as IDictionary; if (entries == null) return -1; foreach (DictionaryEntry entry in entries) if (Convert.ToString(entry.Key) == "M5pp") { count++; segment = Convert.ToString(bucket.Key); } }
        return count;
    }
    private void MarkerSnapshot()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Track.TrackMarker", false)).FirstOrDefault(t => t != null);
        if (type == null) { Write("marker type unavailable"); status = "Marker type unavailable."; return; }
        var matches = Resources.FindObjectsOfTypeAll(type).OfType<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && PathOf(c.transform) == "World/Roads Sylva/Crossing Unprotected - Allen (1)/Whistle Sign E").ToArray();
        Write("marker matches=" + matches.Length);
        if (matches.Length != 1) { status = "Expected one marker, found " + matches.Length; return; }
        var m = matches[0];
        if (marker == null && Convert.ToString(Read(m, "id")) == "M5pp" && ((Behaviour)m).enabled && m.gameObject.activeInHierarchy) { try { markerOrigin = Origin(); markerOriginalLocation = VerifiedLocation(m, "Shuo"); marker = m; var relative = m.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().FullName == "Track.TrackRelativePosition"); markerTarget = Read(relative, "targetTransform") as Transform; if (markerTarget != null) { markerTargetPosition = markerTarget.position; markerTargetRotation = markerTarget.rotation; markerTargetParent = markerTarget.parent; Write("marker target=" + PathOf(markerTarget) + " parent=" + (markerTargetParent == null ? "none" : PathOf(markerTargetParent)) + " position=" + markerTargetPosition.ToString("F4") + " rotation=" + markerTargetRotation.ToString("F4")); } else Write("marker target unavailable"); } catch (Exception e) { Write("marker capture refused " + e); status = "Marker preflight failed; inspect log."; return; } }
        Write("marker instance=" + m.GetInstanceID() + " path=" + PathOf(m.transform) + " id=" + (Read(m, "id") ?? "unavailable") + " location=" + (Read(m, "Location") ?? "unavailable") + " enabled=" + ((Behaviour)m).enabled + " activeSelf=" + m.gameObject.activeSelf + " activeHierarchy=" + m.gameObject.activeInHierarchy + " position=" + m.transform.position.ToString("F4") + " rotation=" + m.transform.rotation.ToString("F4"));
        var serial = Read(m, "_location");
        Write("marker serialized segment=" + Read(serial, "segmentId") + " distance=" + Read(serial, "distance") + " end=" + Read(serial, "end"));
        var graphType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Track.Graph", false)).FirstOrDefault(t => t != null);
        var graph = graphType?.GetProperty("Shared", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null, null);
        var buckets = Read(graph, "_namedLocationsBySegmentId") as IDictionary;
        if (buckets == null) Write("graph registrations unavailable");
        else
        {
            int count = 0;
            foreach (DictionaryEntry bucket in buckets)
            {
                var entries = bucket.Value as IDictionary;
                if (entries == null) { Write("graph bucket unsupported segment=" + bucket.Key); continue; }
                foreach (DictionaryEntry entry in entries)
                    if (Convert.ToString(entry.Key) == "M5pp") { count++; Write("graph marker=M5pp segment=" + bucket.Key + " entryType=" + (entry.Value?.GetType().FullName ?? "null")); }
            }
            Write("graph marker=M5pp count=" + count);
        }
        foreach (var t in m.GetComponentsInChildren<Transform>(true))
            Write("marker child=" + PathOf(t) + " activeSelf=" + t.gameObject.activeSelf + " activeHierarchy=" + t.gameObject.activeInHierarchy + " position=" + t.position.ToString("F4") + " rotation=" + t.rotation.ToString("F4") + " components=" + string.Join(",", t.GetComponents<Component>().Select(c => c == null ? "missing" : c.GetType().FullName).ToArray()));
        status = "Marker and graph snapshot recorded; inspect log for availability.";
    }
    private void MarkerRebind()
    {
        try
        {
            if (marker == null || markerOriginalLocation == null || replacementSegment.Length == 0 || !((Behaviour)marker).enabled || !marker.gameObject.activeInHierarchy || rootChanged || targetChanged) throw new InvalidOperationException("Capture an active enabled marker, restore target, and enter replacement segment first.");
            if (!OriginMatches(markerOrigin)) return;
            var graphType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Track.Graph", false)).First(t => t != null);
            var graph = graphType.GetProperty("Shared", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
            var buckets = Read(graph, "_namedLocationsBySegmentId") as IDictionary;
            if (buckets == null) throw new InvalidOperationException("Graph registration unavailable.");
            int registrationCount = 0; string registeredSegment = null;
            foreach (DictionaryEntry bucket in buckets) { var entries = bucket.Value as IDictionary; if (entries == null) throw new InvalidOperationException("Unsupported graph bucket."); foreach (DictionaryEntry entry in entries) if (Convert.ToString(entry.Key) == "M5pp") { registrationCount++; registeredSegment = Convert.ToString(bucket.Key); } }
            if (registrationCount != 1) throw new InvalidOperationException("Expected exactly one M5pp registration, found " + registrationCount);
            var duplicateCount = Resources.FindObjectsOfTypeAll(marker.GetType()).OfType<Component>().Count(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && Convert.ToString(Strict(c, "id")) == "M5pp");
            if (duplicateCount != 1) throw new InvalidOperationException("Expected exactly one M5pp marker, found " + duplicateCount);
            var serial = Read(marker, "_location");
            var currentSegment = Convert.ToString(Read(serial, "segmentId"));
            VerifiedLocation(marker, currentSegment);
            if (currentSegment != registeredSegment) throw new InvalidOperationException("Registration bucket differs from current location.");
            if (!markerChanged && (currentSegment != "Shuo" || replacementSegment == "Shuo")) throw new InvalidOperationException("Expected Shuo baseline and distinct replacement.");
            var serialType = serial.GetType();
            var candidate = Activator.CreateInstance(serialType);
            foreach (var field in new[] { "segmentId", "distance", "end" })
            {
                var f = serialType.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                f.SetValue(candidate, field == "segmentId" ? (object)replacementSegment : f.GetValue(serial));
            }
            var args = new[] { candidate, (object)null };
            var resolve = graphType.GetMethod("TryResolveLocation");
            if (!(bool)resolve.Invoke(graph, args)) throw new InvalidOperationException("Replacement location did not resolve with original distance and end.");
            var property = marker.GetType().GetProperty("Location");
            markerChanged = true;
            ((Behaviour)marker).enabled = false;
            try { property.SetValue(marker, args[1], null); } finally { ((Behaviour)marker).enabled = true; }
            if (Registrations(out var afterSegment) != 1 || afterSegment != replacementSegment) throw new InvalidOperationException("Rebind registration postcondition failed.");
            VerifiedLocation(marker, replacementSegment); rebindVerified = true;
            Write("marker rebind requested replacement=" + replacementSegment);
            MarkerSnapshot();
        }
        catch (Exception e) { Write("marker rebind ERROR " + e); status = "Rebind failed; restore and inspect log."; }
    }
    private void MarkerPlace()
    {
        status = "Placement refused: group subscription side effects cannot yet be restored."; Write(status + " verifiedRebind=" + rebindVerified);
    }
    private void MarkerDirectPose()
    {
        try
        {
            if (marker == null || markerTarget == null || markerTarget != marker.transform || markerTarget.parent != markerTargetParent || markerChanged || componentChanged || rootChanged || !((Behaviour)marker).enabled || !marker.gameObject.activeInHierarchy) throw new InvalidOperationException("Requires captured active baseline with track-relative target equal to marker root and unchanged parent.");
            if (!OriginMatches(markerOrigin)) return;
            var relative = marker.GetComponents<Component>().Single(c => c != null && c.GetType().FullName == "Track.TrackRelativePosition");
            if (!ReferenceEquals(Strict(relative, "targetTransform"), markerTarget)) throw new InvalidOperationException("Track-relative target changed.");
            VerifiedLocation(marker, "Shuo");
            targetChanged = true;
            markerTarget.position = markerTargetPosition + new Vector3(5, 0, 0);
            markerTarget.rotation = Quaternion.Euler(0, 15, 0) * markerTargetRotation;
            VerifiedLocation(marker, "Shuo");
            Write("direct target pose applied; TrackRelativePosition.TryApply not called"); MarkerSnapshot();
        }
        catch (Exception e) { Write("direct target pose ERROR " + e); status = "Direct pose failed; restore and inspect log."; }
    }
    private void MarkerDisable()
    {
        if (marker == null || targetChanged || markerChanged || rootChanged || !((Behaviour)marker).enabled) { status = "Capture an enabled marker and restore other operations first."; return; }
        try { if (!OriginMatches(markerOrigin)) return; VerifiedLocation(marker, "Shuo"); } catch (Exception e) { Write("marker disable preflight ERROR " + e); return; }
        var graphType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Track.Graph", false)).FirstOrDefault(t => t != null);
        var graph = graphType?.GetProperty("Shared", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null, null);
        var buckets = Read(graph, "_namedLocationsBySegmentId") as IDictionary;
        int count = 0; string registeredSegment = null;
        if (buckets == null) { status = "Graph unavailable; disable refused."; return; }
        foreach (DictionaryEntry bucket in buckets) { var entries = bucket.Value as IDictionary; if (entries == null) { status = "Graph bucket unsupported; disable refused."; return; } foreach (DictionaryEntry entry in entries) if (Convert.ToString(entry.Key) == "M5pp") { count++; registeredSegment = Convert.ToString(bucket.Key); } }
        var markers = Resources.FindObjectsOfTypeAll(marker.GetType()).OfType<Component>().Count(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && Convert.ToString(Strict(c, "id")) == "M5pp");
        if (count != 1 || markers != 1 || registeredSegment != "Shuo" || registeredSegment != Convert.ToString(Read(Read(marker, "_location"), "segmentId"))) { status = "Marker or Shuo registration is inconsistent; disable refused."; return; }
        componentChanged = true;
        ((Behaviour)marker).enabled = false;
        if (Registrations(out _) != 0) { Write("marker disable registration postcondition failed"); status = "Disable postcondition failed; restore."; return; }
        Write("marker component disabled"); MarkerSnapshot();
    }
    private void MarkerRootDisable()
    {
        try
        {
            if (marker == null || markerChanged || componentChanged || rootChanged || targetChanged || !((Behaviour)marker).enabled || !marker.gameObject.activeSelf || !marker.gameObject.activeInHierarchy) throw new InvalidOperationException("Capture an active baseline and restore other operations first.");
            if (!OriginMatches(markerOrigin)) return; VerifiedLocation(marker, "Shuo");
            var serialSegment = Convert.ToString(Read(Read(marker, "_location"), "segmentId"));
            var duplicates = Resources.FindObjectsOfTypeAll(marker.GetType()).OfType<Component>().Count(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && Convert.ToString(Strict(c, "id")) == "M5pp");
            if (duplicates != 1 || serialSegment != "Shuo" || Registrations(out var registered) != 1 || registered != "Shuo") throw new InvalidOperationException("Expected one marker and one Shuo registration.");
            rootChanged = true;
            marker.gameObject.SetActive(false);
            if (Registrations(out _) != 0) throw new InvalidOperationException("Root disable registration postcondition failed.");
            Write("marker root disabled"); MarkerSnapshot();
        }
        catch (Exception e) { Write("marker root disable ERROR " + e); status = "Root disable failed; restore and inspect log."; }
    }
    public void MarkerRestore()
    {
        if (marker == null) return;
        try
        {
            if (!OriginMatches(markerOrigin)) return;
            bool verifyRestored = markerChanged || componentChanged || rootChanged;
            if (rootChanged) { marker.gameObject.SetActive(true); if (Registrations(out var rootSegment) != 1 || rootSegment != "Shuo") throw new InvalidOperationException("Root restore registration postcondition failed."); }
            if (markerChanged) { ((Behaviour)marker).enabled = false; try { marker.GetType().GetProperty("Location").SetValue(marker, markerOriginalLocation, null); } finally { ((Behaviour)marker).enabled = true; } }
            if (componentChanged) ((Behaviour)marker).enabled = true;
            if (targetChanged) { if (markerTarget == null || markerTarget.parent != markerTargetParent) throw new InvalidOperationException("Target or parent changed; restoration refused."); markerTarget.position = markerTargetPosition; markerTarget.rotation = markerTargetRotation; if ((markerTarget.position - markerTargetPosition).sqrMagnitude > 0.0001f || Quaternion.Angle(markerTarget.rotation, markerTargetRotation) > 0.01f) throw new InvalidOperationException("Target restoration pose mismatch."); VerifiedLocation(marker, "Shuo"); }
            if (markerChanged && (Registrations(out var restoredSegment) != 1 || restoredSegment != "Shuo")) throw new InvalidOperationException("Restore registration postcondition failed.");
            if (verifyRestored) VerifiedLocation(marker, "Shuo");
            Write("marker restore verified locationChanged=" + markerChanged + " componentChanged=" + componentChanged + " rootChanged=" + rootChanged + " targetChanged=" + targetChanged); MarkerSnapshot(); marker = null; markerOriginalLocation = null; markerTarget = null; markerChanged = false; componentChanged = false; rootChanged = false; targetChanged = false;
        }
        catch (Exception e) { Write("marker restore ERROR " + e); status = "Marker restore failed; do not save."; }
    }
    public void Restore()
    {
        if (board == null) return;
        if (!OriginMatches(boardOrigin)) return;
        board.transform.position = originalPosition; board.transform.rotation = originalRotation; board.SetActive(originalActive);
        Snapshot("restore"); board = null; status = "Restored; resolve again after reload.";
    }
    private void OnGUI()
    {
        windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "Scene probe: disposable world only");
    }
    private void DrawWindow(int windowId)
    {
        GUILayout.Space(20);
        GUILayout.Label(status);
        if (GUILayout.Button("Resolve and capture baseline")) Resolve();
        if (GUILayout.Button("Capture current state")) Snapshot("manual");
        if (GUILayout.Button("Apply board position and rotation")) Move();
        if (GUILayout.Button("Disable board root")) Hide();
        if (GUILayout.Button("Restore baseline")) Restore();
        if (GUILayout.Button("Capture whistle marker")) MarkerSnapshot();
        GUILayout.Label("Replacement segment (original distance/end):"); replacementSegment = GUILayout.TextField(replacementSegment);
        if (GUILayout.Button("Rebind marker")) MarkerRebind();
        if (GUILayout.Button("Apply marker placement")) MarkerPlace();
        if (GUILayout.Button("Direct marker target pose")) MarkerDirectPose();
        if (GUILayout.Button("Disable marker component")) MarkerDisable();
        if (GUILayout.Button("Disable marker root")) MarkerRootDisable();
        if (GUILayout.Button("Restore marker")) MarkerRestore();
        GUI.DragWindow(new Rect(0, 0, windowRect.width, 20));
    }
}
