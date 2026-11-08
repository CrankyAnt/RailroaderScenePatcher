// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

public sealed class MarkerLifecycleContinuationEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    private MarkerLifecycleContinuationProbe probe;
    public void OnModLoad(IModContext context) { host = new GameObject("CrankyAnt Marker Lifecycle Trace"); UnityEngine.Object.DontDestroyOnLoad(host); probe = host.AddComponent<MarkerLifecycleContinuationProbe>(); probe.Initialize(context.ModDirectory); }
    public void OnWorldLoad() { if (probe != null) probe.Trace("OnWorldLoad"); }
    public void OnWorldUnload() { if (probe != null) probe.Trace("OnWorldUnload"); }
    public void OnModUnload() { if (probe != null) probe.Stop(); if (host != null) UnityEngine.Object.Destroy(host); }
}
public sealed class MarkerLifecycleContinuationProbe : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private readonly List<IDisposable> subscriptions = new List<IDisposable>();
    private string log, status = "Read-only lifecycle trace. See log.";
    private Rect window = new Rect(20, 550, 440, 80);
    private int warningCount;
    private static Type TypeNamed(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null); }
    private static object Read(object target, string name) { var t = target as Type ?? target.GetType(); var instance = target is Type ? null : target; var p = t.GetProperty(name, Flags); var f = t.GetField(name, Flags); return p != null ? p.GetValue(instance, null) : f != null ? f.GetValue(instance) : throw new MissingMemberException(t.FullName, name); }
    private void Write(string message) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine); }
    public void Initialize(string directory)
    {
        log = Path.Combine(directory, "MarkerLifecycleContinuationProbe.log");
        Write("OnModLoad; read-only trace; warning count begins now");
        Application.logMessageReceived += LogMessage;
        Subscribe("Game.Events.MapWillLoadEvent"); Subscribe("Game.Events.MapDidLoadEvent");
        Trace("OnModLoad");
    }
    private void Subscribe(string name)
    {
        try
        {
            var eventType = TypeNamed(name) ?? throw new InvalidOperationException("Event type unavailable: " + name);
            var bus = TypeNamed("Game.Events.EventBus") ?? throw new InvalidOperationException("EventBus unavailable");
            var method = bus.GetMethods(Flags).Single(m => m.Name == "Subscribe" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsGenericType && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(Action<>));
            var handler = GetType().GetMethod("Handle", Flags).MakeGenericMethod(eventType);
            var action = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(eventType), this, handler);
            var subscription = method.MakeGenericMethod(eventType).Invoke(null, new object[] { action }) as IDisposable ?? throw new InvalidOperationException("Subscription is not disposable");
            subscriptions.Add(subscription); Write("subscribed " + name);
        }
        catch (Exception e) { Write("subscribe ERROR " + name + " " + e); status = "Subscription failed; inspect log."; }
    }
    private void Handle<T>(T value) { Trace(typeof(T).FullName); }
    private void LogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Warning && type != LogType.Error && type != LogType.Exception) return;
        if (condition.IndexOf("marker", StringComparison.OrdinalIgnoreCase) < 0 && condition.IndexOf("location", StringComparison.OrdinalIgnoreCase) < 0) return;
        warningCount++; Write("matching diagnostic type=" + type + " message=" + condition.Replace("\n", " ").Replace("\r", " "));
    }
    public void Trace(string phase)
    {
        try
        {
            var mapType = TypeNamed("Game.ActiveMapDefinition"); var graphType = TypeNamed("Track.Graph"); var markerType = TypeNamed("Track.TrackMarker");
            var map = mapType == null ? null : Read(mapType, "Identifier"); var graph = graphType == null ? null : Read(graphType, "Shared");
            var markers = markerType == null ? new Component[0] : Resources.FindObjectsOfTypeAll(markerType).OfType<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && (Convert.ToString(Read(c, "id")) == "Mdow" || Convert.ToString(Read(c, "id")) == "M5pp")).ToArray();
            string origin;
            try { var t = TypeNamed("Helpers.WorldTransformer"); origin = ((Vector3)t.GetMethod("WorldToGame", Flags, null, new[] { typeof(Vector3) }, null).Invoke(null, new object[] { Vector3.zero })).ToString("F4"); } catch (Exception e) { origin = "unavailable(" + e.GetType().Name + ")"; }
            Write(phase + " map=" + map + " graph=" + (graph == null ? "null" : graph.GetHashCode().ToString()) + " markerCount=" + markers.Length + " origin=" + origin + " matchingDiagnosticsSinceModLoad=" + warningCount);
            if (graph == null) Write(phase + " destination segment=SCrankyAntSceneProbe exists=unavailable graph=null");
            else
            {
                try { var segment = graph.GetType().GetMethod("GetSegment").Invoke(graph, new object[] { "SCrankyAntSceneProbe" }); Write(phase + " destination segment=SCrankyAntSceneProbe exists=" + (segment != null)); }
                catch (Exception e) { Write(phase + " destination segment check ERROR " + e); }
            }
            if (markers.Length == 0) Write(phase + " marker details and location resolution unavailable: no matching loaded target");
            foreach (var marker in markers) TraceMarker(phase, marker, graph);
        }
        catch (Exception e) { Write(phase + " trace ERROR " + e); }
    }
    private void TraceMarker(string phase, Component marker, object graph)
    {
        var id = Convert.ToString(Read(marker, "id"));
        try
        {
            var serial = Read(marker, "_location");
            var type = serial.GetType();
            var cache = marker.GetType().GetField("_cachedPosition", Flags);
            var warned = marker.GetType().GetField("_warnedInvalid", Flags);
            Write(phase + " marker id=" + id + " path=" + PathOf(marker.transform) + " enabled=" + ((Behaviour)marker).enabled + " active=" + marker.gameObject.activeInHierarchy + " segment=" + Read(serial, "segmentId") + " distance=" + Read(serial, "distance") + " end=" + Read(serial, "end") + " cachedPositionPresent=" + (cache == null ? "unavailable" : (cache.GetValue(marker) != null).ToString()) + " warnedInvalid=" + (warned == null ? "unavailable" : Convert.ToString(warned.GetValue(marker))));
            if (graph == null) { Write(phase + " marker id=" + id + " currentResolves=unavailable destinationResolves=unavailable graph=null"); return; }
            var resolve = graph.GetType().GetMethod("TryResolveLocation");
            var originalArgs = new[] { serial, (object)null };
            bool current = (bool)resolve.Invoke(graph, originalArgs);
            var candidate = Activator.CreateInstance(type);
            foreach (var name in new[] { "segmentId", "distance", "end" })
            {
                var field = type.GetField(name, Flags) ?? throw new MissingFieldException(type.FullName, name);
                object value = field.GetValue(serial);
                if (name == "segmentId") value = "SCrankyAntSceneProbe";
                if (name == "distance") value = 50f;
                if (name == "end") value = Enum.Parse(field.FieldType, "A");
                field.SetValue(candidate, value);
            }
            var destinationArgs = new[] { candidate, (object)null };
            bool destination = (bool)resolve.Invoke(graph, destinationArgs);
            Write(phase + " marker id=" + id + " currentResolves=" + current + " currentResultPresent=" + (originalArgs[1] != null) + " destination=SCrankyAntSceneProbe/A/50 destinationResolves=" + destination + " destinationResultPresent=" + (destinationArgs[1] != null));
        }
        catch (Exception e) { Write(phase + " marker id=" + id + " resolution ERROR " + e); }
    }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    public void Stop() { Application.logMessageReceived -= LogMessage; foreach (var subscription in subscriptions) subscription.Dispose(); subscriptions.Clear(); Write("subscriptions disposed"); }
    private void OnGUI() { window = GUI.Window(0x43414c54, window, id => { GUILayout.Label(status); if (GUILayout.Button("Record current state")) Trace("manual"); GUI.DragWindow(new Rect(0, 0, 10000, 20)); }, "Marker Lifecycle Trace"); }
}
