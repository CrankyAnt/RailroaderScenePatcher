// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

public sealed class EditorProbeEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    private IModContext context;
    public void OnModLoad(IModContext value) { context = value; context.Log.Info("EditorProbe mod load"); }
    public void OnWorldLoad()
    {
        context.Log.Info("EditorProbe world load");
        host = new GameObject("CrankyAnt EditorProbe");
        host.AddComponent<EditorProbeBehaviour>().Initialize(context.ModDirectory);
    }
    public void OnWorldUnload() { context.Log.Info("EditorProbe world unload"); Cleanup(); }
    public void OnModUnload() { context.Log.Info("EditorProbe mod unload"); Cleanup(); }
    private void Cleanup() { if (host != null) UnityEngine.Object.Destroy(host); host = null; }
}

public sealed class EditorProbeBehaviour : MonoBehaviour
{
    private string output;
    private string status = "Waiting for editor";
    private string lastController = "";
    private Vector2 scroll;
    private string snapshot = "";
    private int scanNumber;
    private Rect windowRect;
    private bool windowPositioned;
    private const int WindowId = 0x43524550;
    private readonly List<(object target, EventInfo info, Delegate handler)> subscriptions = new List<(object, EventInfo, Delegate)>();
    private static Type FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);
    private static object Read(object obj, string name)
    {
        if (obj == null) return null;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var type = obj as Type ?? obj.GetType();
        return type.GetProperty(name, flags)?.GetValue(obj is Type ? null : obj, null) ?? type.GetField(name, flags)?.GetValue(obj is Type ? null : obj);
    }
    public void Initialize(string directory) { output = Path.Combine(directory, "EditorProbe.log"); Log("behaviour created"); }
    private void Log(string message) { File.AppendAllText(output, DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine); }
    private void Update()
    {
        var controller = Read(FindType("Game.MapEditor.MapEditorController"), "Shared");
        var identity = controller == null ? "none" : controller.GetHashCode().ToString();
        if (identity != lastController)
        {
            Unsubscribe(); lastController = identity; status = "Controller: " + identity; Log(status);
            if (controller != null) Attach(controller);
        }
    }
    private void Attach(object controller)
    {
        var editor = Read(controller, "MapEditor");
        var graph = Read(editor, "GraphEditor");
        Subscribe(graph, "NodeDidChange");
        var undo = Read(editor, "UndoManager");
        Subscribe(undo, "DidChange");
        Log("attachment editor=" + (editor != null) + " graph=" + (graph != null) + " undo=" + (undo != null));
    }
    private void Subscribe(object target, string eventName)
    {
        if (target == null) return;
        try
        {
            var type = target as Type ?? target.GetType();
            var info = type.GetEvent(eventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (info == null) { Log("event absent " + type.FullName + "." + eventName); return; }
            var invoke = info.EventHandlerType.GetMethod("Invoke");
            if (invoke.ReturnType != typeof(void)) { Log("event return unsupported " + eventName); return; }
            var args = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            var call = Expression.Call(Expression.Constant(this), typeof(EditorProbeBehaviour).GetMethod(nameof(EventSeen), BindingFlags.Instance | BindingFlags.NonPublic), Expression.Constant(type.FullName + "." + eventName));
            var handler = Expression.Lambda(info.EventHandlerType, call, args).Compile();
            var receiver = target is Type ? null : target;
            info.AddEventHandler(receiver, handler); subscriptions.Add((receiver, info, handler));
            Log("subscribed " + type.FullName + "." + eventName);
        }
        catch (Exception e) { Log("subscription failed " + eventName + " " + e.GetType().Name + " " + e.Message); }
    }
    private void EventSeen(string eventName) { Log("event " + eventName); }
    private void Unsubscribe()
    {
        foreach (var item in subscriptions) try { item.info.RemoveEventHandler(item.target, item.handler); Log("unsubscribed " + item.info.Name); } catch (Exception e) { Log("unsubscribe failed " + e.Message); }
        subscriptions.Clear();
    }
    private void OnDestroy() { Unsubscribe(); Log("behaviour destroyed"); }
    private static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
    private static Vector3 ToGame(Vector3 world) => (Vector3)FindType("Helpers.WorldTransformer").GetMethod("WorldToGame").Invoke(null, new object[] { world });
    private static string Vec(object value) => value is Vector3 v ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:R},{1:R},{2:R})", v.x, v.y, v.z) : "unavailable";
    private void OpenPauseMenu()
    {
        try
        {
            var type = FindType("PauseMenu");
            if (type == null) { Log("pause unavailable: type missing"); return; }
            if ((bool)Read(type, "IsPaused")) { Log("pause already open"); return; }
            if (Read(FindType("Game.State.SaveManager"), "Shared") == null || Read(FindType("Game.State.StateManager"), "Shared") == null)
            { Log("pause unavailable: session managers missing"); return; }
            var menus = Resources.FindObjectsOfTypeAll(type).Cast<UnityEngine.Object>().OfType<Behaviour>()
                .Where(m => m.gameObject.scene.IsValid() && m.gameObject.scene.isLoaded && m.isActiveAndEnabled).ToArray();
            if (menus.Length != 1) { Log("pause unavailable: active menus=" + menus.Length); return; }
            var method = type.GetMethod("SetPaused", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
            if (method == null) { Log("pause unavailable: SetPaused missing"); return; }
            method.Invoke(menus[0], new object[] { true });
            Log("pause requested; IsPaused=" + Read(type, "IsPaused"));
        }
        catch (Exception e) { Log("pause failed " + e); }
    }
    private void Focus(string markerId, string boardName)
    {
        try
        {
            GameObject target = null;
            if (markerId != null)
                target = Resources.FindObjectsOfTypeAll(FindType("Track.TrackMarker")).Cast<UnityEngine.Object>().OfType<Component>()
                    .Where(m => m.gameObject.scene.IsValid() && m.gameObject.scene.isLoaded && (string)Read(m, "id") == markerId).Select(m => m.gameObject).SingleOrDefault();
            else target = Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.scene.IsValid() && g.scene.isLoaded && g.name == boardName).SingleOrDefault();
            if (target == null) { Log("focus target missing " + (markerId ?? boardName)); return; }
            var selectorType = FindType("CameraSelector");
            var selector = Read(selectorType, "shared");
            if (selector == null) { Log("focus camera unavailable"); return; }
            var cameraIdentifier = selectorType.GetNestedType("CameraIdentifier", BindingFlags.Public | BindingFlags.NonPublic);
            var strategy = Enum.Parse(cameraIdentifier, "Strategy");
            var gamePosition = ToGame(target.transform.position);
            selectorType.GetMethod("JumpToPoint").Invoke(selector, new object[] { gamePosition, Quaternion.identity, strategy });
            Log("focus requested " + Hierarchy(target.transform) + " game=" + Vec(gamePosition) + " native selection unchanged by probe");
        }
        catch (Exception e) { Log("focus failed " + e); }
    }
    private void FocusSegmentEnd(string segmentId, string end)
    {
        try
        {
            var graph = Read(FindType("Track.Graph"), "Shared");
            if (graph == null) { Log("focus segment unavailable: graph missing"); return; }
            var segment = graph.GetType().GetMethod("GetSegment").Invoke(graph, new object[] { segmentId });
            var node = Read(segment, end);
            var position = Read(node, "Position");
            if (!(position is Vector3 gamePosition)) { Log("focus segment unavailable " + segmentId + " end=" + end); return; }
            var selectorType = FindType("CameraSelector");
            var selector = Read(selectorType, "shared");
            if (selector == null) { Log("focus camera unavailable"); return; }
            var cameraIdentifier = selectorType.GetNestedType("CameraIdentifier", BindingFlags.Public | BindingFlags.NonPublic);
            var strategy = Enum.Parse(cameraIdentifier, "Strategy");
            selectorType.GetMethod("JumpToPoint").Invoke(selector, new object[] { gamePosition, Quaternion.identity, strategy });
            Log("focus requested segment=" + segmentId + " end=" + end + " node=" + Read(node, "id") + " game=" + Vec(gamePosition) + " native selection unchanged by probe");
        }
        catch (Exception e) { Log("focus segment failed " + segmentId + " " + e); }
    }
    private void Scan()
    {
        var markerType = FindType("Track.TrackMarker");
        if (markerType == null) { status = "TrackMarker type unavailable"; return; }
        var markers = Resources.FindObjectsOfTypeAll(markerType);
        var graph = Read(FindType("Track.Graph"), "Shared");
        var lines = new List<string>();
        foreach (var m in markers.Cast<UnityEngine.Object>().OfType<Component>().Where(m => m.gameObject.scene.IsValid() && m.gameObject.scene.isLoaded))
        {
            var id = (string)Read(m, "id");
            if (id != "Mjd8" && id != "M5pp" && id != "Mpg1") continue;
            var loc = Read(m, "_location");
            var segmentId = (string)Read(loc, "segmentId");
            var segment = graph?.GetType().GetMethod("GetSegment").Invoke(graph, new object[] { segmentId });
            var a = Read(segment, "a"); var b = Read(segment, "b");
            var detail = "marker=" + id + " path=" + Hierarchy(m.transform) + " instance=" + m.GetInstanceID() + " active=" + m.gameObject.activeInHierarchy + " world=" + Vec(m.transform.position) + " game=" + Vec(ToGame(m.transform.position)) + " segment=" + segmentId + " distance=" + Read(loc, "distance") + " end=" + Read(loc, "end") + " segmentExists=" + (segment != null) + " nodeA=" + Read(a, "id") + " positionA=" + Vec(Read(a, "Position")) + " nodeB=" + Read(b, "id") + " positionB=" + Vec(Read(b, "Position"));
            if (graph != null && loc != null)
            {
                var resolve = graph.GetType().GetMethod("TryResolveLocation");
                var args = new object[] { loc, null };
                var resolved = (bool)resolve.Invoke(graph, args);
                detail += " resolved=" + resolved;
                if (resolved) detail += " trackPoint=" + Vec(graph.GetType().GetMethod("GetPosition").Invoke(graph, new[] { args[1] }));
            }
            lines.Add(detail);
        }
        foreach (var board in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.scene.IsValid() && g.scene.isLoaded && g.name == "Loc: Sylva Int"))
            lines.Add("independent " + Hierarchy(board.transform) + " instance=" + board.GetInstanceID() + " active=" + board.activeInHierarchy + " world=" + Vec(board.transform.position) + " game=" + Vec(ToGame(board.transform.position)));
        snapshot = string.Join("\n", lines);
        status = "Scanned " + lines.Count + " objects";
        Log("scan=" + (++scanNumber) + " " + status + "\n" + snapshot);
    }
    private void OnGUI()
    {
        if (!windowPositioned)
        {
            windowRect = new Rect(Mathf.Max(10, Screen.width - 660), 320, 650, 500);
            windowPositioned = true;
        }
        windowRect.height = lastController == "none" || string.IsNullOrEmpty(lastController) ? 100 : 500;
        windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Editor diagnostic probe");
    }
    private void DrawWindow(int id)
    {
        GUILayout.Label(status);
        if (GUILayout.Button("Open native pause menu")) OpenPauseMenu();
        if (lastController == "none" || string.IsNullOrEmpty(lastController)) { GUI.DragWindow(new Rect(0, 0, 10000, 20)); return; }
        if (GUILayout.Button("Explicit rescan")) Scan();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Focus Mjd8")) Focus("Mjd8", null);
        if (GUILayout.Button("Focus M5pp")) Focus("M5pp", null);
        if (GUILayout.Button("Focus Sylva board")) Focus(null, "Loc: Sylva Int");
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Focus Shuo end B")) FocusSegmentEnd("Shuo", "b");
        if (GUILayout.Button("Focus replacement end A")) FocusSegmentEnd("SCrankyAntSceneProbe", "a");
        GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll); GUILayout.Label(snapshot); GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }
}
