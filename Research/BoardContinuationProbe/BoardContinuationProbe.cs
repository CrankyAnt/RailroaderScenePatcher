// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

public sealed class BoardContinuationProbeEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host; private BoardContinuationProbe probe;
    public void OnModLoad(IModContext context) { host = new GameObject("Board Continuation Probe"); UnityEngine.Object.DontDestroyOnLoad(host); probe = host.AddComponent<BoardContinuationProbe>(); probe.Initialize(context.ModDirectory); }
    public void OnWorldLoad() { if (probe != null) probe.WorldLoad(); }
    public void OnWorldUnload() { if (probe != null) probe.WorldUnload(); }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}
public sealed class BoardContinuationProbe : MonoBehaviour
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string PathName = "World/Signs/Loc: Sylva Int";
    private string directory, log, status = "Disposable fixture only. Never save.";
    private Rect window = new Rect(20, 200, 440, 340);
    private HashSet<string> previousMaskKeys = new HashSet<string>(); private EventInfo originEvent; private Delegate originHandler; private object originSource; private bool worldLoaded, autoAttempted; private Transform root, parent; private Component culler, mask; private Vector3 origin; private Quaternion baselineRotation; private bool dirty; private bool baselineActive; private Vector3 gamePosition;
    public void Initialize(string directory) { this.directory = directory; log = Path.Combine(directory, "BoardContinuationProbe.log"); Write("loaded assembly=" + Assembly.GetExecutingAssembly().FullName + " source=issue18-board-continuation-v2"); }
    public void WorldLoad() { if (originEvent != null && originHandler != null) originEvent.RemoveEventHandler(originSource, originHandler); originEvent = null; originHandler = null; originSource = null; worldLoaded = true; Write("OnWorldLoad"); try { var transformer = TypeNamed("Helpers.WorldTransformer"); originSource = transformer.GetProperty("Shared", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null); if (originSource == null) throw new InvalidOperationException("WorldTransformer unavailable"); originEvent = transformer.GetEvent("OnDidMove", Instance); if (originEvent == null) throw new MissingMemberException("OnDidMove"); originHandler = Delegate.CreateDelegate(originEvent.EventHandlerType, this, GetType().GetMethod("OriginMoved", Instance)); originEvent.AddEventHandler(originSource, originHandler); AutoOnWorldLoad(); } catch (Exception e) { originHandler = null; originEvent = null; originSource = null; Write("origin subscribe ERROR " + e); status = "Origin notification unavailable; do not capture."; } }
    public void WorldUnload() { Write("OnWorldUnload dirty=" + dirty + " captureCancelled=true"); if (originEvent != null && originHandler != null) originEvent.RemoveEventHandler(originSource, originHandler); originEvent = null; originHandler = null; originSource = null; root = null; parent = null; culler = null; mask = null; dirty = false; worldLoaded = false; autoAttempted = false; status = "World unloaded; capture cancelled."; }
    private void AutoOnWorldLoad() { if (autoAttempted) { Write("auto already attempted for world"); return; } autoAttempted = true; if (!File.Exists(Path.Combine(directory, "ARM_AUTO_BOARD"))) { Write("auto unarmed"); return; } try { var args = Environment.GetCommandLineArgs(); var indices = Enumerable.Range(0, args.Length).Where(i => string.Equals(args[i], "-homedir", StringComparison.OrdinalIgnoreCase)).ToArray(); Write("auto homedirArgumentCount=" + indices.Length); if (indices.Length == 0 || indices[0] + 1 >= args.Length || !string.Equals(Path.GetFullPath(args[indices[0] + 1]).TrimEnd('\\', '/'), @"C:\TMP\RailroaderMapResearch\RuntimeHome", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("First homedir is not the isolated home."); if (!string.Equals(Path.GetFullPath(directory).TrimEnd('\\', '/'), @"C:\TMP\RailroaderMapResearch\RuntimeHome\Mods\BoardContinuationProbe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Probe context is not in the isolated home."); Write("auto preflight armed, first homedir and probe context matched"); Capture(); if (root == null) throw new InvalidOperationException("Capture preflight failed."); Move(); Write("auto move attempted dirty=" + dirty + " status=" + status); } catch (Exception e) { Write("auto preflight ERROR " + e); status = "Auto preflight failed; inspect log."; } }
    private void OnDestroy() { if (originEvent != null && originHandler != null) originEvent.RemoveEventHandler(originSource, originHandler); originEvent = null; originHandler = null; originSource = null; Write("OnDestroy"); }
    private void OriginMoved(Vector3 delta) { Write("OnDidMove delta=" + V(delta) + " dirty=" + dirty); }
    private static Type TypeNamed(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null); }
    private static Vector3 ConvertPosition(string method, Vector3 value) { return (Vector3)TypeNamed("Helpers.WorldTransformer").GetMethod(method, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null).Invoke(null, new object[] { value }); }
    private void Focus() { try { if (Convert.ToString(TypeNamed("Game.ActiveMapDefinition").GetProperty("Identifier", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null)) != "crankyant/scene-operations-map") throw new InvalidOperationException("Wrong map."); if (!worldLoaded || originHandler == null) throw new InvalidOperationException("World or origin event unavailable."); var found = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t != null && t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded && PathOf(t) == PathName).ToArray(); if (found.Length != 1) throw new InvalidOperationException("Board match count=" + found.Length); var type = TypeNamed("CameraSelector"); var selector = type.GetProperty("shared", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null); var strategy = Enum.Parse(type.GetNestedType("CameraIdentifier", BindingFlags.Public | BindingFlags.NonPublic), "Strategy"); var point = ConvertPosition("WorldToGame", found[0].position); type.GetMethod("JumpToPoint").Invoke(selector, new object[] { point, Quaternion.identity, strategy }); Write("focus game=" + V(point)); status = "Focus requested. Wait for origin to settle, then capture."; } catch (Exception e) { Write("focus ERROR " + e); status = "Focus failed; inspect log."; } }
    private void Pause() { try { var matches = Resources.FindObjectsOfTypeAll(TypeNamed("PauseMenu")).OfType<Component>().Where(c => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && c.gameObject.activeInHierarchy).ToArray(); if (matches.Length != 1) throw new InvalidOperationException("Active pause menu count=" + matches.Length); var method = matches[0].GetType().GetMethod("SetPaused", Instance, null, new[] { typeof(bool) }, null); if (method == null) throw new MissingMethodException("PauseMenu.SetPaused(bool)"); method.Invoke(matches[0], new object[] { true }); Write("pause requested"); status = "Pause requested."; } catch (Exception e) { Write("pause ERROR " + e); status = "Pause failed; inspect log."; } }
    private void FocusFar() { try { CheckCapture(); var type = TypeNamed("CameraSelector"); var selector = type.GetProperty("shared", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null); var strategy = Enum.Parse(type.GetNestedType("CameraIdentifier", BindingFlags.Public | BindingFlags.NonPublic), "Strategy"); var point = gamePosition + new Vector3(-10000, 0, 0); type.GetMethod("JumpToPoint").Invoke(selector, new object[] { point, Quaternion.identity, strategy }); Write("focus far game=" + V(point)); status = "Far focus requested; wait for origin notification, then snapshot."; } catch (Exception e) { Write("focus far ERROR " + e); status = "Far focus failed; restore and inspect log."; } }
    private static void InvokeNoArgs(object target, string name) { for (var t = target.GetType(); t != null; t = t.BaseType) { var m = t.GetMethod(name, Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null); if (m != null) { m.Invoke(target, null); return; } } throw new MissingMethodException(target.GetType().FullName, name); }
    private void Refresh() { InvokeNoArgs(culler, "RequestUpdateCullingPosition"); InvokeNoArgs(mask, "Rebuild"); Write("refresh RequestUpdateCullingPosition + Rebuild"); }
    private void Write(string s) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + s + Environment.NewLine); }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static object Field(object o, string name) { for (var t = o.GetType(); t != null; t = t.BaseType) { var f = t.GetField(name, Instance | BindingFlags.DeclaredOnly); if (f != null) return f.GetValue(o); } throw new MissingFieldException(o.GetType().FullName, name); }
    private static object Property(object o, string name) { for (var t = o.GetType(); t != null; t = t.BaseType) { var p = t.GetProperty(name, Instance | BindingFlags.DeclaredOnly); if (p != null) return p.GetValue(o, null); } throw new MissingMemberException(o.GetType().FullName, name); }
    private static string F(float n) { return n.ToString("R", CultureInfo.InvariantCulture); }
    private static string V(Vector3 v) { return F(v.x) + "," + F(v.y) + "," + F(v.z); }
    private static string Q(Quaternion q) { return F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w); }
    private static Vector3 Origin() { var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Helpers.WorldTransformer", false)).First(x => x != null); return (Vector3)t.GetMethod("WorldToGame", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null).Invoke(null, new object[] { Vector3.zero }); }
    private void Snapshot(string label, Transform root, Component culler, Component mask)
    {
        Write(label + " origin=" + V(Origin()));
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
        {
            Write(label + " node=" + PathOf(node) + " localPosition=" + V(node.localPosition) + " localRotation=" + Q(node.localRotation) + " worldPosition=" + V(node.position) + " worldRotation=" + Q(node.rotation) + " activeSelf=" + node.gameObject.activeSelf + " activeHierarchy=" + node.gameObject.activeInHierarchy);
            foreach (var component in node.GetComponents<Component>()) if (component != null) Write(label + " component=" + PathOf(node) + " type=" + component.GetType().FullName + " enabled=" + (component is Behaviour b ? b.enabled.ToString() : component is Renderer r ? r.enabled.ToString() : "n/a"));
        }
        foreach (string name in new[] { "radius", "falloff", "flattenHeightmap", "order" }) Write(label + " mask." + name + "=" + Convert.ToString(Field(mask, name), CultureInfo.InvariantCulture));
        var keys = new HashSet<string>(((IEnumerable)Field(mask, "_modifierKeys")).Cast<object>().Select(Convert.ToString));
        var managerType = TypeNamed("Map.Runtime.MapManager"); var mapManager = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null); if (mapManager == null) throw new InvalidOperationException("MapManager unavailable.");
        var modifiers = (IDictionary)Field(Field(mapManager, "_maskModifiers"), "_modifiers"); var offset = (Vector3)Field(mapManager, "_gameToWorldOffset");
        Write(label + " map.gameToWorldOffset=" + V(offset) + " root.game=" + V(ConvertPosition("WorldToGame", root.position)) + " pendingInvalidationCount=" + Property(mapManager, "PendingInvalidationCount"));
        if (label == "baseline") previousMaskKeys.Clear();
        foreach (var oldKey in previousMaskKeys) Write(label + " previousKey=" + oldKey + " registered=" + modifiers.Contains(oldKey) + " current=" + keys.Contains(oldKey));
        foreach (var key in keys) { Write(label + " mask.modifierKey=" + key + " registered=" + modifiers.Contains(key)); if (!modifiers.Contains(key)) continue; var modifier = modifiers[key]; var descriptor = Property(modifier, "Mask"); Write(label + " modifier maskName=" + Field(modifier, "MaskName") + " value=" + Field(modifier, "Value") + " descriptor=" + descriptor.GetType().FullName); if (descriptor.GetType().FullName == "Map.Runtime.CurveMaskDescriptor") { var bounds = (Bounds)Property(descriptor, "Bounds"); Write(label + " descriptor.boundsCenter=" + V(bounds.center) + " descriptor.worldCenter=" + V(bounds.center + offset) + " descriptor.boundsSize=" + V(bounds.size) + " radius=" + Property(descriptor, "Radius") + " falloff=" + Property(descriptor, "Falloff") + " curve=" + Field(descriptor, "Curve")); } }
        previousMaskKeys = keys;
        var targets = (ICollection)Field(mask, "maskTargets"); Write(label + " mask.maskTargets.count=" + (targets == null ? "null" : targets.Count.ToString()));
        if (targets != null) foreach (var target in targets) { if (target == null) { Write(label + " mask.target=null"); continue; } foreach (string name in new[] { "kind", "maskName", "value", "useCustomStampOffset", "stampOffset", "biomeCode" }) Write(label + " mask.target." + name + "=" + Convert.ToString(Field(target, name), CultureInfo.InvariantCulture)); }
        foreach (string name in new[] { "_cullingState", "cullingManagerName", "visibleDistanceBand", "radius" }) Write(label + " culler." + name + "=" + Convert.ToString(Field(culler, name), CultureInfo.InvariantCulture));
        var renderers = (Renderer[])Field(culler, "_cullRenderers"); Write(label + " culler.renderers=" + (renderers == null ? "null" : renderers.Length.ToString()));
        if (renderers != null) foreach (var renderer in renderers) Write(label + " culler.renderer=" + (renderer == null ? "null" : PathOf(renderer.transform) + " enabled=" + renderer.enabled));
        var token = Field(culler, "_cullingToken");
        if (token == null) { Write(label + " token=null active=" + root.gameObject.activeInHierarchy); if (root.gameObject.activeInHierarchy) throw new InvalidOperationException("Active culler token missing."); return; }
        var index = (int)Field(token, "<Index>k__BackingField"); var removed = (bool)Field(token, "<IsRemoved>k__BackingField"); var manager = Field(token, "Manager");
        if (removed && !root.gameObject.activeInHierarchy) { Write(label + " token.removed=true inactive=true"); return; }
        var spheres = (BoundingSphere[])Field(manager, "_spheres"); var count = (int)Field(manager, "_sphereCount");
        if (index < 0 || index >= count || index >= spheres.Length || removed) throw new InvalidOperationException("Culler token is not live.");
        Write(label + " token.index=" + index + " removed=" + removed + " sphere.position=" + V(spheres[index].position) + " sphere.radius=" + F(spheres[index].radius));
    }
    private void CheckCapture()
    {
        if (root == null || root.parent != parent) throw new InvalidOperationException("Captured root or parent changed.");
        if (!worldLoaded || originHandler == null) throw new InvalidOperationException("World or origin event unavailable.");
    }
    private void Capture()
    {
        try
        {
            if (dirty) throw new InvalidOperationException("Restore before recapture.");
            if (!worldLoaded || originHandler == null) throw new InvalidOperationException("World or origin event unavailable.");
            var map = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Game.ActiveMapDefinition", false)).First(t => t != null);
            var id = map.GetProperty("Identifier", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null) ?? map.GetField("Identifier", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
            if (Convert.ToString(id) != "crankyant/scene-operations-map") throw new InvalidOperationException("Wrong map.");
            var matches = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t != null && t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded && PathOf(t) == PathName).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Board match count=" + matches.Length);
            var candidate = matches[0];
            if (candidate.parent == null || !candidate.gameObject.activeSelf || !candidate.gameObject.activeInHierarchy) throw new InvalidOperationException("Board baseline inactive or parent missing.");
            var nodes = candidate.GetComponentsInChildren<Transform>(true); if (nodes.Length != 3 || nodes.Count(n => PathOf(n) == PathName + "/Sign-Location") != 1 || nodes.Count(n => PathOf(n) == PathName + "/Text") != 1) throw new InvalidOperationException("Unexpected hierarchy.");
            if (nodes.SelectMany(n => n.GetComponents<Component>()).Any(c => c != null && c.GetType().FullName == "Track.TrackMarker")) throw new InvalidOperationException("Unexpected marker.");
            var components = candidate.GetComponents<Component>(); var c = components.Single(x => x != null && x.GetType().FullName == "Helpers.Culling.RendererCuller"); var m = components.Single(x => x != null && x.GetType().FullName == "Map.Runtime.MaskComponents.CircleMapMask");
            var capturedOrigin = Origin(); Snapshot("baseline", candidate, c, m);
            root = candidate; parent = candidate.parent; culler = c; mask = m; baselineRotation = root.rotation; origin = capturedOrigin; gamePosition = ConvertPosition("WorldToGame", root.position); baselineActive = root.gameObject.activeSelf; if (!((Behaviour)culler).enabled || !((Behaviour)mask).enabled) throw new InvalidOperationException("Consumers must be enabled.");
            CheckCapture(); status = "Captured. Move, wait, snapshot, then restore.";
        }
        catch (Exception e) { if (!dirty) { root = null; parent = null; culler = null; mask = null; } Write("capture ERROR " + e); status = "Capture failed; inspect log."; }
    }
    private void Move()
    {
        try { CheckCapture(); if (!root.gameObject.activeInHierarchy) throw new InvalidOperationException("Board inactive."); dirty = true; root.position = ConvertPosition("GameToWorld", gamePosition + new Vector3(5, 0, 0)); root.rotation = Quaternion.Euler(0, 15, 0) * baselineRotation; Refresh(); Snapshot("moved immediate", root, culler, mask); status = "Moved. Wait, snapshot and restore; never save."; }
        catch (Exception e) { Write("move ERROR " + e); status = "Move failed. Restore, then abandon without saving."; }
    }
    private void Current()
    {
        try { CheckCapture(); Snapshot("current", root, culler, mask); status = dirty ? "Current snapshot recorded. Restore before leaving." : "Current snapshot recorded."; }
        catch (Exception e) { Write("snapshot ERROR " + e); status = "Snapshot failed. Restore, then abandon without saving."; }
    }
    private void Restore()
    {
        try
        {
            if (root == null || root.parent != parent) throw new InvalidOperationException("Cannot restore original parent.");
            var originChanged = (Origin() - origin).sqrMagnitude > 0.0001f;
            if (root.gameObject.activeSelf != baselineActive) root.gameObject.SetActive(baselineActive); ((Behaviour)culler).enabled = true; ((Behaviour)mask).enabled = true; root.position = ConvertPosition("GameToWorld", gamePosition); root.rotation = baselineRotation; Refresh();
            if ((ConvertPosition("WorldToGame", root.position) - gamePosition).sqrMagnitude > 0.0001f || Quaternion.Angle(root.rotation, baselineRotation) > 0.01f) throw new InvalidOperationException("Game pose restore mismatch.");
            Snapshot("restored", root, culler, mask); Write("restore complete originChanged=" + originChanged); dirty = false; root = null;
            status = originChanged ? "Game pose restored after origin change. Check log; never save." : "Game pose restored. Never save.";
        }
        catch (Exception e) { Write("restore ERROR " + e); status = "Restore failed. Abandon without saving."; }
    }
    private void Toggle() { try { CheckCapture(); dirty = true; root.gameObject.SetActive(!root.gameObject.activeSelf); Write("toggle activeSelf=" + root.gameObject.activeSelf); Snapshot("toggle", root, culler, mask); status = "Activity changed. Toggle again or restore; never save."; } catch (Exception e) { Write("toggle ERROR " + e); status = "Toggle failed; restore and abandon without saving."; } }
    private void OnGUI() { window = GUI.Window(0x42445032, window, id => { GUILayout.Label(status); if (GUILayout.Button("Open pause menu")) Pause(); if (GUILayout.Button("Focus board")) Focus(); if (GUILayout.Button("Capture baseline")) Capture(); if (GUILayout.Button("Focus 10 km west (origin test)")) FocusFar(); if (GUILayout.Button("Move or repeat baseline +5m/+15 degrees")) Move(); if (GUILayout.Button("Snapshot current")) Current(); if (GUILayout.Button("Toggle board activity")) Toggle(); if (GUILayout.Button("Restore captured game pose")) Restore(); if (dirty) GUILayout.Label("Restore before leaving. Never save."); GUI.DragWindow(new Rect(0, 0, 10000, 20)); }, "Board Continuation Probe"); }
}