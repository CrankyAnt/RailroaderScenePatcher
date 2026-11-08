// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

public sealed class BoardDependencyProbeEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    public void OnModLoad(IModContext context) { host = new GameObject("Board Dependency Probe"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<BoardDependencyProbe>().Initialize(context.ModDirectory); }
    public void OnWorldLoad() { }
    public void OnWorldUnload() { }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}
public sealed class BoardDependencyProbe : MonoBehaviour
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string PathName = "World/Signs/Loc: Sylva Int";
    private string log, status = "Disposable fixture only. Never save.";
    private Rect window = new Rect(20, 200, 440, 200);
    private Transform root, parent; private Component culler, mask; private Vector3 localPosition, baselinePosition, origin; private Quaternion localRotation, baselineRotation; private bool dirty;
    public void Initialize(string directory) { log = Path.Combine(directory, "BoardDependencyProbe.log"); }
    private void Write(string s) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + s + Environment.NewLine); }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static object Field(object o, string name) { for (var t = o.GetType(); t != null; t = t.BaseType) { var f = t.GetField(name, Instance | BindingFlags.DeclaredOnly); if (f != null) return f.GetValue(o); } throw new MissingFieldException(o.GetType().FullName, name); }
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
        var keys = (IEnumerable)Field(mask, "_modifierKeys"); foreach (var key in keys) Write(label + " mask.modifierKey=" + key);
        var targets = (ICollection)Field(mask, "maskTargets"); Write(label + " mask.maskTargets.count=" + (targets == null ? "null" : targets.Count.ToString()));
        if (targets != null) foreach (var target in targets) { if (target == null) { Write(label + " mask.target=null"); continue; } foreach (string name in new[] { "kind", "maskName", "value", "useCustomStampOffset", "stampOffset", "biomeCode" }) Write(label + " mask.target." + name + "=" + Convert.ToString(Field(target, name), CultureInfo.InvariantCulture)); }
        foreach (string name in new[] { "_cullingState", "cullingManagerName", "visibleDistanceBand", "radius" }) Write(label + " culler." + name + "=" + Convert.ToString(Field(culler, name), CultureInfo.InvariantCulture));
        var renderers = (Renderer[])Field(culler, "_cullRenderers"); Write(label + " culler.renderers=" + (renderers == null ? "null" : renderers.Length.ToString()));
        if (renderers != null) foreach (var renderer in renderers) Write(label + " culler.renderer=" + (renderer == null ? "null" : PathOf(renderer.transform) + " enabled=" + renderer.enabled));
        var token = Field(culler, "_cullingToken");
        if (token == null) { Write(label + " token=null"); return; }
        var index = (int)Field(token, "<Index>k__BackingField"); var removed = (bool)Field(token, "<IsRemoved>k__BackingField"); var manager = Field(token, "Manager");
        var spheres = (BoundingSphere[])Field(manager, "_spheres"); var count = (int)Field(manager, "_sphereCount");
        if (index < 0 || index >= count || index >= spheres.Length || removed) throw new InvalidOperationException("Culler token is not live.");
        Write(label + " token.index=" + index + " removed=" + removed + " sphere.position=" + V(spheres[index].position) + " sphere.radius=" + F(spheres[index].radius));
    }
    private void CheckCapture()
    {
        if (root == null || root.parent != parent) throw new InvalidOperationException("Captured root or parent changed.");
        if ((Origin() - origin).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Origin changed after capture. Restore and abandon without saving.");
    }
    private void Capture()
    {
        try
        {
            if (dirty) throw new InvalidOperationException("Restore before recapture.");
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
            root = candidate; parent = candidate.parent; culler = c; mask = m; localPosition = root.localPosition; localRotation = root.localRotation; baselinePosition = root.position; baselineRotation = root.rotation; origin = capturedOrigin;
            CheckCapture(); status = "Captured. Move, wait, snapshot, then restore.";
        }
        catch (Exception e) { Write("capture ERROR " + e); status = "Capture failed; inspect log."; }
    }
    private void Move()
    {
        try { CheckCapture(); if (!root.gameObject.activeInHierarchy) throw new InvalidOperationException("Board inactive."); dirty = true; root.position = baselinePosition + new Vector3(5, 0, 0); root.rotation = Quaternion.Euler(0, 15, 0) * baselineRotation; Snapshot("moved immediate", root, culler, mask); status = "Moved. Wait, snapshot and restore; never save."; }
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
            root.localPosition = localPosition; root.localRotation = localRotation;
            if (!root.localPosition.Equals(localPosition) || !root.localRotation.Equals(localRotation)) throw new InvalidOperationException("Local pose restore mismatch.");
            Snapshot("restored", root, culler, mask); Write("restore complete originChanged=" + originChanged); dirty = false; root = null;
            status = originChanged ? "Local pose restored; origin changed. Abandon without saving." : "Local pose restored. Never save.";
        }
        catch (Exception e) { Write("restore ERROR " + e); status = "Restore failed. Abandon without saving."; }
    }
    private void OnGUI() { window = GUI.Window(0x42445031, window, id => { GUILayout.Label(status); if (GUILayout.Button("Capture baseline")) Capture(); if (GUILayout.Button("Move or repeat baseline +5m/+15 degrees")) Move(); if (GUILayout.Button("Snapshot current")) Current(); if (GUILayout.Button("Restore captured local pose")) Restore(); if (dirty) GUILayout.Label("Restore before leaving. Never save."); GUI.DragWindow(); }, "Board Dependency Probe"); }
}