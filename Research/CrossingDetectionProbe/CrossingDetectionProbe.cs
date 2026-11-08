// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CrankyAnt.CrossingDetectionResearch;
using Railroader.Modding;
using UnityEngine;

public sealed class CrossingDetectionProbeEntry : IModEntryPoint, IWorldLifecycle
{
    GameObject host;
    public void OnModLoad(IModContext context) { host = new GameObject("Crossing Detection Probe"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<CrossingDetectionProbe>().Initialize(context.ModDirectory); }
    public void OnWorldLoad() { }
    public void OnWorldUnload() { host.GetComponent<CrossingDetectionProbe>().Leaving(); }
    public void OnModUnload() { if (host != null) { host.GetComponent<CrossingDetectionProbe>().Leaving(); UnityEngine.Object.Destroy(host); } }
}

public sealed class CrossingDetectionProbe : MonoBehaviour
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Map = "crankyant/crossing-detection-research";
    const string Root = "World/Large Scenery/Bryson/Grade Crossing - Bryson Everett";
    string directory, status = "Inspect first. Never save the research session.";
    Rect window = new Rect(20, 200, 410, 410);
    DetectionTrial trial; GateObserver gateObserver;
    float next; bool telemetry;
    public void Initialize(string path) { directory = path; Write("loaded assembly=" + typeof(CrossingDetectionProbe).Assembly.FullName + " moduleVersionId=" + typeof(CrossingDetectionProbe).Assembly.ManifestModule.ModuleVersionId); }
    void Write(string text) { File.AppendAllText(Path.Combine(directory, "CrossingDetectionProbe.log"), DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine); }
    static Type TypeNamed(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null); }
    static object Read(object target, string name) { var type = target as Type ?? target.GetType(); var obj = target is Type ? null : target; var p = type.GetProperty(name, F); if (p != null) return p.GetValue(obj, null); return (type.GetField(name, F) ?? throw new MissingMemberException(type.FullName, name)).GetValue(obj); }
    static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    static bool Loaded(Component c) { return c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded; }
    Component Guard()
    {
        if (Convert.ToString(Read(TypeNamed("Game.ActiveMapDefinition"), "Identifier")) != Map) throw new InvalidOperationException("Wrong map.");
        var matches = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => Loaded(c) && c.GetType().FullName == "Track.Crossings.GradeCrossingController" && PathOf(c.transform) == Root).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Expected exactly one target controller; found " + matches.Length);
        return matches[0];
    }
    void MutationGuard() { Guard(); HomeGuard(); if (!File.Exists(Path.Combine(directory, "ARM_CROSSING_DETECTION"))) throw new InvalidOperationException("Arm file absent."); if (gateObserver == null) throw new InvalidOperationException("Gate observer unavailable."); }
    void HomeGuard() { var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; var home = Path.GetFullPath(@"C:\TMP\RailroaderMapResearch\RuntimeHome").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; if (!full.StartsWith(home, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Probe is outside isolated runtime home."); var args = Environment.GetCommandLineArgs(); var index = Array.FindIndex(args, a => string.Equals(a, "-homedir", StringComparison.OrdinalIgnoreCase)); if (index < 0 || index + 1 >= args.Length || !string.Equals(Path.GetFullPath(args[index + 1]).TrimEnd(Path.DirectorySeparatorChar), home.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Process home argument is not the isolated runtime home."); }
    static string Endpoint(object e) { return Read(e, "segmentId") + "/" + Read(e, "end") + "/" + Read(e, "distance"); }
    void Inspect()
    {
        var controller = Guard(); var convert = TypeNamed("Helpers.WorldTransformer").GetMethod("WorldToGame", F, null, new[] { typeof(Vector3) }, null); var center = (Vector3)convert.Invoke(null, new object[] { controller.transform.position }); var origin = (Vector3)convert.Invoke(null, new object[] { Vector3.zero }); Write("BEGIN INSPECT controller=" + PathOf(controller.transform) + " controllerPosition=" + controller.transform.position.ToString("F4") + " worldToGameController=" + center.ToString("F4") + " worldToGameZero=" + origin.ToString("F4"));
        var sets = ((IEnumerable)Read(controller, "circuitSets")).Cast<object>().ToArray();
        for (int i = 0; i < sets.Length; i++) foreach (var n in new[] { "approachA", "island", "approachB" }) { var s = Read(sets[i], n); var points = ((IEnumerable)s.GetType().GetMethod("GetPoints", F, null, Type.EmptyTypes, null).Invoke(s, null)).Cast<Vector3>().ToArray(); var transform = TypeNamed("Helpers.WorldTransformer").GetMethod("WorldToGame", F, null, new[] { typeof(Vector3) }, null); Write("original set=" + i + " role=" + n + " id=" + Read(s, "id") + " lower=" + Endpoint(Read(s, "_lower")) + " upper=" + Endpoint(Read(s, "_upper")) + " getPoints=" + string.Join(";", points.Select(p => p.ToString("F4"))) + " worldToGameOfGetPoints=" + string.Join(";", points.Select(p => ((Vector3)transform.Invoke(null, new object[] { p })).ToString("F4")))); }
        foreach (var s in Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => Loaded(c) && c.GetType().FullName == "Track.TrackSpan"))
        {
            try { var points = ((IEnumerable)s.GetType().GetMethod("GetPoints", F, null, Type.EmptyTypes, null).Invoke(s, null)).Cast<Vector3>().ToArray(); if (points.Any(p => Vector3.Distance(p, center) < 250f)) Write("near span=" + Read(s, "id") + " lower=" + Endpoint(Read(s, "_lower")) + " upper=" + Endpoint(Read(s, "_upper")) + " points=" + string.Join(";", points.Select(p => p.ToString("F4")))); } catch (Exception e) { Write("span inspection error " + e.GetType().Name); }
        }
        foreach (var c in Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => Loaded(c) && c.GetType().FullName == "Track.Segment" && Vector3.Distance(c.transform.position, controller.transform.position) < 250f)) { try { Write("near segment id=" + Read(c, "id") + " path=" + PathOf(c.transform) + " position=" + c.transform.position.ToString("F4")); } catch (Exception e) { Write("segment inspection error " + e.GetType().Name); } }
        foreach (var c in Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => Loaded(c) && (c.GetType().Name.IndexOf("Locomotive", StringComparison.OrdinalIgnoreCase) >= 0 || c.GetType().Name == "Car"))) Write("train candidate type=" + c.GetType().FullName + " path=" + PathOf(c.transform) + " position=" + c.transform.position.ToString("F4") + " instance=" + c.GetInstanceID());
        Write("END INSPECT");
    }
    void Focus() { var c = Guard(); var v = (Vector3)TypeNamed("Helpers.WorldTransformer").GetMethod("WorldToGame", F, null, new[] { typeof(Vector3) }, null).Invoke(null, new object[] { c.transform.position }); var t = TypeNamed("CameraSelector"); t.GetMethod("JumpToPoint", F).Invoke(Read(t, "shared"), new object[] { v, Quaternion.identity, Enum.Parse(t.GetNestedType("CameraIdentifier", F), "Strategy") }); Write("focus " + v.ToString("F4")); }
    void Overview() {
        var c = Guard(); var convert = TypeNamed("Helpers.WorldTransformer").GetMethod("WorldToGame", F, null, new[] { typeof(Vector3) }, null);
        var center = (Vector3)convert.Invoke(null, new object[] { c.transform.position }); var selector = Read(TypeNamed("CameraSelector"), "shared");
        if (Convert.ToString(Read(selector, "_cameraIdentifier")) != "Strategy") throw new InvalidOperationException("Focus crossing first.");
        var camera = Read(selector, "strategyCamera"); var current = camera.GetType().GetMethod("CaptureState", F).Invoke(camera, null);
        var state = Activator.CreateInstance(TypeNamed("Cameras.StrategyCameraState"), new object[] { center, null, 80f, 70f, Convert.ToSingle(Read(current, "AngleY")) });
        if (!(bool)camera.GetType().GetMethod("Restore", F).Invoke(camera, new[] { state })) throw new InvalidOperationException("Camera restore refused.");
        Write("overview center=" + center.ToString("F4"));
    }
    void Pause() { var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; var home = Path.GetFullPath(@"C:\TMP\RailroaderMapResearch\RuntimeHome").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; if (!full.StartsWith(home, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Probe is outside isolated runtime home."); var args = Environment.GetCommandLineArgs(); var index = Array.FindIndex(args, a => string.Equals(a, "-homedir", StringComparison.OrdinalIgnoreCase)); if (index < 0 || index + 1 >= args.Length || !string.Equals(Path.GetFullPath(args[index + 1]).TrimEnd(Path.DirectorySeparatorChar), home.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Process home argument is not the isolated runtime home."); var p = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => Loaded(c) && c.GetType().Name == "PauseMenu").ToArray(); if (p.Length != 1) throw new InvalidOperationException("Pause menu count " + p.Length); p[0].GetType().GetMethod("SetPaused", F).Invoke(p[0], new object[] { true }); }
    SpanInput[] ReadInput()
    {
        var path = Path.Combine(directory, "spans.json"); var json = File.ReadAllText(path); Write("input path=" + path + " characters=" + json.Length);
        try { var old = JsonUtility.FromJson<TrialInput>(json); Write("input unity rootNull=" + (old == null) + " spansNull=" + (old == null || old.Spans == null) + " count=" + (old?.Spans == null ? -1 : old.Spans.Length) + " ids=" + (old?.Spans == null ? "<none>" : string.Join(",", old.Spans.Select(x => x == null ? "<null span>" : (x.Id ?? "<null id>") + "[lower=" + (x.Lower == null ? "null" : (x.Lower.Segment ?? "<null segment>") + "/" + (x.Lower.End ?? "<null end>") + "/" + x.Lower.Distance) + ",upper=" + (x.Upper == null ? "null" : (x.Upper.Segment ?? "<null segment>") + "/" + (x.Upper.End ?? "<null end>") + "/" + x.Upper.Distance) + "]")))); } catch (Exception e) { Write("input unity error=" + e.GetType().FullName + " " + e.Message); }
        var spans = InputReader.Parse(json); Write("input strict count=" + spans.Length + " ids=" + string.Join(",", spans.Select(x => x.Id)) + " endpoints=" + string.Join(";", spans.Select(x => x.Lower.Segment + "/" + x.Lower.End + "/" + x.Lower.Distance + "|" + x.Upper.Segment + "/" + x.Upper.End + "/" + x.Upper.Distance))); return spans;
    }
    void Run(string command)
    {
        try {
            switch (command.Trim().ToLowerInvariant()) {
                case "inspect": Inspect(); break;
                case "input": Guard(); ReadInput(); break;
                case "focus": Focus(); break;
                case "pause": Pause(); break;
                case "overview": Overview(); break;
                case "capture": Guard(); if (trial != null) throw new InvalidOperationException("Trial already captured."); trial = new DetectionTrial(Guard(), Guard, Write); telemetry = true; trial.Sample(); try { gateObserver = new GateObserver(Guard(), Guard, Write); } catch (Exception e) { gateObserver = null; Write("GATE OBSERVER INIT ERROR " + e); status = "Gate observation unavailable; see log."; } break;
                case "add": MutationGuard(); if (trial == null) throw new InvalidOperationException("Capture baseline first."); var input = ReadInput(); trial.Add(input); telemetry = true; trial.Sample(); break;
                case "sample": Guard(); if (trial == null) throw new InvalidOperationException("No trial."); trial.Sample(); break;
                case "remove": MutationGuard(); if (trial == null) throw new InvalidOperationException("No trial."); trial.Remove(); telemetry = true; break;
                case "verify": Guard(); if (trial == null) throw new InvalidOperationException("No trial."); trial.VerifyDestroyed(); trial.Sample(); break;
                default: throw new InvalidOperationException("Unknown command: " + command);
            }
            status = command + " completed; see log."; Write(status);
        } catch (Exception e) { status = command + " failed; see log. Do not save."; Write(status + " " + e); }
    }
    public void Leaving() { telemetry = false; if (trial != null) { try { MutationGuard(); trial.Remove(); trial = null; } catch (Exception e) { Write("LEAVING cleanup refused or failed; abandon without saving: " + e); } } gateObserver = null; }
    void Update() {
        if (Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + 0.5f;
        var path = Path.Combine(directory, "command.txt");
        if (File.Exists(path)) try { var cmd = File.ReadAllText(path); File.Delete(path); Run(cmd); } catch (Exception e) { Write("COMMAND ERROR " + e); }
        if (telemetry && trial != null) try { trial.Sample(); } catch (Exception e) { telemetry = false; status = "Telemetry stopped; inspect log. Do not save."; Write("TELEMETRY ERROR " + e); }
    }
    void LateUpdate() { if (gateObserver == null) return; try { gateObserver.Frame(); } catch (Exception e) { gateObserver = null; status = "Gate observation stopped; see log."; Write("GATE OBSERVER ERROR " + e); } }
    void OnGUI() { window = GUI.Window(0x43445052, window, id => { GUILayout.Label(status); foreach (var command in new[] { "inspect", "input", "focus", "overview", "pause", "capture", "add", "sample", "remove", "verify" }) if (GUILayout.Button(command)) Run(command); GUI.DragWindow(); }, "Crossing Detection Probe"); }
    [Serializable] public sealed class TrialInput { public SpanInput[] Spans; }
}
