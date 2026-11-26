// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;

// Test-only mod: a small window that reads the built terrain height, so that a Railroader journey can compare the
// height at fixed places before and after an edit numerically rather than by eye. It reads the point under the pointer
// and two places A and B in map coordinates (the patcher's coordinates). The height is the built terrain tile's
// height at that place (Terrain.SampleHeight), in map coordinates. Log heights writes one Player log line per place.
// It changes nothing in the world.
public sealed class HeightReadoutEntry : IModEntryPoint {
    GameObject host;
    public void OnModLoad(IModContext context) { host = new GameObject("Terrain height readout"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<HeightReadout>(); }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}
public sealed class HeightReadout : MonoBehaviour {
    const string Prefix = "[Terrain height readout] ";
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    Rect window = new Rect(20, 420, 380, 0);
    bool visible = true;
    // The map x and z of places A and B as typed.
    readonly string[] fields = { "", "", "", "" };
    Vector3? pointer;
    MethodInfo toWorld, toMap;
    void Update() {
        if (Input.GetKeyDown(KeyCode.F10)) visible = !visible;
        if (visible) pointer = PointerOnTerrain();
    }
    void OnGUI() {
        if (!visible) return;
        window = GUILayout.Window(0x52FB, window, Draw, "Terrain height readout (test only, F10 hides)", GUILayout.Width(window.width));
    }
    void Draw(int id) {
        GUILayout.BeginVertical();
        try {
            GUILayout.Label("Pointer: " + (pointer is Vector3 p ? Describe(p.x, p.z) : "not over built terrain"));
            Place("A", 0); Place("B", 2);
            if (GUILayout.Button("Log heights")) {
                if (pointer is Vector3 at) Debug.Log(Prefix + "pointer " + Describe(at.x, at.z));
                foreach (var (name, index) in new[] { ("A", 0), ("B", 2) })
                    if (Read(index, out var x, out var z)) Debug.Log(Prefix + name + " " + Describe(x, z));
            }
        } catch (Exception e) { GUILayout.Label("Error: " + e.GetBaseException().Message); }
        GUILayout.EndVertical();
        GUI.DragWindow();
    }
    void Place(string name, int index) {
        GUILayout.BeginHorizontal();
        GUILayout.Label(name + " map x", GUILayout.Width(70)); fields[index] = GUILayout.TextField(fields[index], GUILayout.Width(90));
        GUILayout.Label("z", GUILayout.Width(12)); fields[index + 1] = GUILayout.TextField(fields[index + 1], GUILayout.Width(90));
        var enabled = GUI.enabled; GUI.enabled = enabled && pointer.HasValue;
        if (GUILayout.Button(new GUIContent("Pointer", pointer.HasValue ? "Set " + name + " to the point under the pointer" : "Unavailable: the pointer is not over built terrain")) && pointer is Vector3 p) {
            fields[index] = p.x.ToString("F2", Invariant); fields[index + 1] = p.z.ToString("F2", Invariant);
        }
        GUI.enabled = enabled;
        GUILayout.EndHorizontal();
        GUILayout.Label(name + ": " + (Read(index, out var x, out var z) ? Describe(x, z) : "type a map x and z"));
    }
    bool Read(int index, out float x, out float z) {
        z = 0;
        return Single.TryParse(fields[index], NumberStyles.Float, Invariant, out x) && Single.TryParse(fields[index + 1], NumberStyles.Float, Invariant, out z);
    }
    string Describe(float x, float z) {
        var height = Height(x, z);
        return "x=" + x.ToString("F2", Invariant) + " z=" + z.ToString("F2", Invariant) + " height=" + (height is float h ? h.ToString("F3", Invariant) : "no built terrain tile");
    }
    // The built terrain height at a map place, in map coordinates, or null where no terrain tile is built.
    float? Height(float x, float z) {
        var world = Convert("GameToWorld", new Vector3(x, 0, z));
        foreach (var terrain in Terrain.activeTerrains) {
            if (terrain == null || terrain.terrainData == null) continue;
            var origin = terrain.GetPosition(); var size = terrain.terrainData.size;
            if (world.x < origin.x || world.z < origin.z || world.x > origin.x + size.x || world.z > origin.z + size.z) continue;
            var height = terrain.SampleHeight(world) + origin.y;
            return Convert("WorldToGame", new Vector3(world.x, height, world.z)).y;
        }
        return null;
    }
    // The map place of the built terrain under the pointer, or null.
    Vector3? PointerOnTerrain() {
        var camera = Camera.main;
        if (camera == null) return null;
        var hits = Physics.RaycastAll(camera.ScreenPointToRay(Input.mousePosition), 20000f);
        var ground = hits.Where(h => h.collider != null && h.collider.GetComponent<Terrain>() != null).OrderBy(h => h.distance).ToArray();
        if (ground.Length == 0) return null;
        return Convert("WorldToGame", ground[0].point);
    }
    // Helpers.WorldTransformer converts between Unity's world, which moves with the floating origin, and map coordinates.
    Vector3 Convert(string direction, Vector3 value) {
        if (toWorld == null || toMap == null) {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => { try { return a.GetType("Helpers.WorldTransformer", false); } catch { return null; } }).FirstOrDefault(t => t != null)
                ?? throw new InvalidOperationException("Helpers.WorldTransformer is unavailable");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            toWorld = type.GetMethod("GameToWorld", flags, null, new[] { typeof(Vector3) }, null) ?? throw new MissingMethodException("GameToWorld");
            toMap = type.GetMethod("WorldToGame", flags, null, new[] { typeof(Vector3) }, null) ?? throw new MissingMethodException("WorldToGame");
        }
        return (Vector3)(direction == "GameToWorld" ? toWorld : toMap).Invoke(null, new object[] { value });
    }
}
