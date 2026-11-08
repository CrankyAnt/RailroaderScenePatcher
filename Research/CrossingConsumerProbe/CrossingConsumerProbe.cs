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

public sealed class CrossingConsumerProbeEntry : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    public void OnModLoad(IModContext context) { host = new GameObject("Crossing Consumer Probe"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<CrossingConsumerProbe>().Initialize(context.ModDirectory); }
    public void OnWorldLoad() { }
    public void OnWorldUnload() { }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}

public sealed class CrossingConsumerProbe : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Root = "World/Large Scenery/Bryson/Grade Crossing - Bryson Everett/";
    private string log;
    private string status = "Disposable world only. Never save.";
    private Rect window = new Rect(20, 200, 420, 130);
    public void Initialize(string directory) { log = Path.Combine(directory, "CrossingConsumerProbe.log"); }
    private void Write(string text) { File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine); }
    private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    private static object Field(object o, string name) { return o.GetType().GetField(name, Flags)?.GetValue(o) ?? throw new MissingFieldException(o.GetType().FullName, name); }
    private static int PiprCount(Type type) { return Resources.FindObjectsOfTypeAll<MonoBehaviour>().Count(c => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && c.GetType() == type && Convert.ToString(Field(c, "id")) == "Pipr"); }
    private sealed class CacheSnapshot
    {
        public Vector3[] Points;
        public string[] Segments;
        public float Length;
    }
    private CacheSnapshot Cache(Component span, string label)
    {
        var points = ((IEnumerable)Field(span, "_cachedPoints")).Cast<Vector3>().ToArray();
        var segments = ((IEnumerable)Field(span, "_cachedSegments")).Cast<object>().Select(s => Convert.ToString(Field(s, "id"))).ToArray();
        var length = Convert.ToSingle(Field(span, "_length"));
        Write(label + " points=" + points.Length + " segments=" + segments.Length + " length=" + length.ToString("R", CultureInfo.InvariantCulture));
        for (int i = 0; i < points.Length; i++) Write(label + " point[" + i + "]=" + points[i].x.ToString("R", CultureInfo.InvariantCulture) + "," + points[i].y.ToString("R", CultureInfo.InvariantCulture) + "," + points[i].z.ToString("R", CultureInfo.InvariantCulture));
        for (int i = 0; i < segments.Length; i++) Write(label + " segment[" + i + "]=" + segments[i]);
        return new CacheSnapshot { Points = points, Segments = segments, Length = length };
    }
    private static void CheckEndpoint(object serial, string end, float distance)
    {
        if (Convert.ToString(Field(serial, "segmentId")) != "Sz8t" || Convert.ToString(Field(serial, "end")) != end || Math.Abs(Convert.ToSingle(Field(serial, "distance")) - distance) > 0.0001f)
            throw new InvalidOperationException("Unexpected endpoint.");
    }
    private void Run()
    {
        Component span = null; object original = null, graph = null; PropertyInfo lower = null; MethodInfo lookup = null; CacheSnapshot baseline = null; bool changed = false, candidateChecked = false;
        try
        {
            var mapType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Game.ActiveMapDefinition", false)).First(t => t != null);
            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var identifier = mapType.GetProperty("Identifier", staticFlags)?.GetValue(null, null) ?? mapType.GetField("Identifier", staticFlags)?.GetValue(null);
            if (!string.Equals(Convert.ToString(identifier), "crankyant/scene-operations-map", StringComparison.Ordinal)) throw new InvalidOperationException("Wrong active map.");
            var matches = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && c.GetType().FullName == "Track.TrackSpan" && PathOf(c.transform).StartsWith(Root, StringComparison.Ordinal) && Convert.ToString(Field(c, "id")) == "Pipr").ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected one Pipr span.");
            span = matches[0]; var type = span.GetType();
            if (PiprCount(type) != 1) throw new InvalidOperationException("Pipr is not globally unique.");
            if (!(span is Behaviour behaviour) || !behaviour.enabled || !span.gameObject.activeInHierarchy) throw new InvalidOperationException("Span inactive.");
            var graphType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Track.Graph", false)).First(t => t != null);
            graph = graphType.GetProperty("Shared", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
            lookup = graphType.GetMethod("SpanForId", Flags, null, new[] { typeof(string) }, null);
            if (!ReferenceEquals(lookup.Invoke(graph, new object[] { "Pipr" }), span)) throw new InvalidOperationException("Registration mismatch.");
            lower = type.GetProperty("lower", Flags); var upper = type.GetProperty("upper", Flags);
            original = lower.GetValue(span, null);
            if (original == null || upper.GetValue(span, null) == null || !(bool)type.GetProperty("IsValid", Flags).GetValue(span, null)) throw new InvalidOperationException("Invalid span.");
            var serial = Field(span, "_lower"); var upperSerial = Field(span, "_upper");
            CheckEndpoint(serial, "A", 0.1567912f); CheckEndpoint(upperSerial, "B", 45.12718f);
            var serialType = serial.GetType(); var candidate = Activator.CreateInstance(serialType);
            foreach (var name in new[] { "segmentId", "end", "distance" }) serialType.GetField(name, Flags).SetValue(candidate, Field(serial, name));
            serialType.GetField("distance", Flags).SetValue(candidate, Convert.ToSingle(Field(serial, "distance")) + 0.25f);
            var args = new[] { candidate, (object)null };
            if (!(bool)graphType.GetMethod("TryResolveLocation").Invoke(graph, args) || args[1] == null || args[1].Equals(original)) throw new InvalidOperationException("Candidate unresolved.");
            var getSegments = type.GetMethod("GetSegments", Flags, null, Type.EmptyTypes, null);
            getSegments.Invoke(span, null); baseline = Cache(span, "baseline after GetSegments");
            if (baseline.Points.Length == 0 || baseline.Segments.Length == 0 || baseline.Length <= 0f) throw new InvalidOperationException("Baseline cache is empty.");
            changed = true; lower.SetValue(span, args[1], null); Cache(span, "candidate invalidated");
            getSegments.Invoke(span, null); Cache(span, "candidate after GetSegments"); candidateChecked = true;
            status = "Candidate checked. Restoring.";
        }
        catch (Exception e) { Write("ERROR " + e); status = "Probe failed. Abandon without saving."; }
        finally
        {
            if (changed) try
            {
                lower.SetValue(span, original, null); Cache(span, "restored invalidated");
                if (!original.Equals(lower.GetValue(span, null)) || PiprCount(span.GetType()) != 1 || !ReferenceEquals(lookup.Invoke(graph, new object[] { "Pipr" }), span)) throw new InvalidOperationException("Restore endpoint or registration mismatch.");
                CheckEndpoint(Field(span, "_lower"), "A", 0.1567912f); CheckEndpoint(Field(span, "_upper"), "B", 45.12718f);
                span.GetType().GetMethod("GetSegments", Flags, null, Type.EmptyTypes, null).Invoke(span, null); var restored = Cache(span, "restored after GetSegments");
                if (!baseline.Points.SequenceEqual(restored.Points) || !baseline.Segments.SequenceEqual(restored.Segments) || !baseline.Length.Equals(restored.Length)) throw new InvalidOperationException("Restored cache differs from baseline.");
                Write("restore complete; cache equals baseline; never save"); if (candidateChecked) status = "Restore and cache verified. Never save.";
            }
            catch (Exception e) { Write("RESTORE ERROR " + e); status = "Restore failed. Abandon without saving."; }
        }
    }
    private void OnGUI() { window = GUI.Window(0x43524350, window, id => { GUILayout.Label(status); if (GUILayout.Button("Test Pipr cache and restore")) Run(); GUI.DragWindow(); }, "Crossing Consumer Probe"); }
}
