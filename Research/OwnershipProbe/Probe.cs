// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Reflection;
using Railroader.Modding;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class OwnershipProbe : IModEntryPoint, IWorldLifecycle
{
    private GameObject host;
    public void OnModLoad(IModContext context)
    {
        Debug.Log("[OwnershipProbe] OnModLoad");
        host = new GameObject("OwnershipProbe");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<Observer>();
    }
    public void OnModUnload() { Debug.Log("[OwnershipProbe] OnModUnload"); if (host != null) UnityEngine.Object.Destroy(host); }
    public void OnWorldLoad() { Debug.Log("[OwnershipProbe] OnWorldLoad"); Observer.Snapshot("OnWorldLoad"); }
    public void OnWorldUnload() { Debug.Log("[OwnershipProbe] OnWorldUnload"); Observer.Snapshot("OnWorldUnload"); }
}

public sealed class Observer : MonoBehaviour
{
    private sealed class ReadFailure
    {
        public readonly string Status;
        public ReadFailure(string status) { Status = status; }
    }
    private float next;
    private void OnEnable() { SceneManager.sceneLoaded += Loaded; SceneManager.sceneUnloaded += Unloaded; Snapshot("OnEnable"); }
    private void OnDisable() { SceneManager.sceneLoaded -= Loaded; SceneManager.sceneUnloaded -= Unloaded; }
    private void Loaded(Scene scene, LoadSceneMode mode) { Debug.Log("[OwnershipProbe] sceneLoaded " + scene.name); Snapshot("sceneLoaded"); }
    private void Unloaded(Scene scene) { Debug.Log("[OwnershipProbe] sceneUnloaded " + scene.name); Snapshot("sceneUnloaded"); }
    private void Update() { if (Time.realtimeSinceStartup >= next) { next = Time.realtimeSinceStartup + 10; Snapshot("periodic"); } }

    private static object Read(object instance, Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        try { var p = type.GetProperty(name, flags); if (p != null) return p.GetValue(instance, null); var f = type.GetField(name, flags); return f == null ? new ReadFailure("missing-member") : f.GetValue(instance); }
        catch (Exception e) { return new ReadFailure("access-error:" + e.GetType().Name); }
    }
    public static void Snapshot(string phase)
    {
        try
        {
            var asm = Assembly.Load("Railroader");
            var active = asm.GetType("Game.ActiveMapDefinition");
            var current = active == null ? new ReadFailure("missing-type") : Read(null, active, "Current");
            Debug.Log("[OwnershipProbe] " + phase + " active=" + (current is ReadFailure ? Format(current, false) : current == null ? "null" : "present"));
            if (active != null) foreach (var n in new[] { "Identifier", "MapDataAbsolutePath", "MapLayerIdentifiers", "MapLayerDataPaths", "IsEditTarget" })
                Debug.Log("[OwnershipProbe] " + phase + " " + n + "=" + Format(Read(null, active, n), n.Contains("Path")));
            var managerType = asm.GetType("Model.Database.AssetPackManager");
            var manager = managerType == null ? null : Read(null, managerType, "Shared");
            var packResult = manager == null ? null : manager is ReadFailure ? manager : Read(manager, managerType, "OrderedPacks");
            var packs = packResult is ReadFailure ? null : packResult as IEnumerable;
            if (packs == null) Debug.Log("[OwnershipProbe] " + phase + " packs=" + (packResult is ReadFailure ? Format(packResult, false) : "unavailable"));
            else foreach (var pack in packs)
            {
                var store = Read(pack, pack.GetType(), "Store");
                if (store == null || store is ReadFailure) { Debug.Log("[OwnershipProbe] " + phase + " store=" + Format(store, false)); continue; }
                var st = store.GetType();
                Debug.Log("[OwnershipProbe] " + phase + " pack=" + Format(Read(store, st, "Identifier"), false)
                    + " writable=" + Format(Read(store, st, "Writeable"), false)
                    + " root=" + Format(Read(store, st, "BasePath"), true));
            }
            var id = active == null ? null : Read(null, active, "Identifier");
            if (manager != null && !(manager is ReadFailure) && id != null && !(id is ReadFailure))
            {
                var method = managerType.GetMethod("GetPackBasePathForDefinition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Debug.Log("[OwnershipProbe] " + phase + " definitionOwner=" + Format(method == null ? new ReadFailure("missing-method") : method.Invoke(manager, new[] { id }), true));
            }
            foreach (var name in new[] { "Track.TrackMarker", "Track.TrackRelativePosition", "Track.TrackSpan" })
            {
                var type = asm.GetType(name);
                var count = type == null ? -1 : Resources.FindObjectsOfTypeAll(type).Length;
                Debug.Log("[OwnershipProbe] " + phase + " " + name + " count=" + count);
            }
        }
        catch (Exception e) { Debug.Log("[OwnershipProbe] " + phase + " ERROR " + e); }
    }
    private static string Format(object value, bool isPath)
    {
        if (value == null) return "null";
        if (value is ReadFailure failure) return failure.Status;
        if (value is string s) return isPath ? ClassifyPath(s) : s;
        if (value is IEnumerable sequence) { var result = "["; foreach (var item in sequence) result += Format(item, isPath) + ","; return result + "]"; }
        return value.ToString();
    }
    private static string ClassifyPath(string path)
    {
        string p = path.Replace('\\', '/').ToLowerInvariant();
        if (p.Contains("/ownershipresearcha/" ) || p.EndsWith("/ownershipresearcha")) return "fixture-A";
        if (p.Contains("/ownershipresearchb/" ) || p.EndsWith("/ownershipresearchb")) return "fixture-B";
        if (p.Contains("/ownershipresearchprobe/" ) || p.EndsWith("/ownershipresearchprobe")) return "probe";
        if (p.Contains("/assetpackscore/")) return "game-core";
        return "other-redacted";
    }
}
