// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Railroader.Modding;
using UnityEngine;

public sealed class ObservationEntry : IModEntryPoint, IWorldLifecycle {
    GameObject host; Observation probe;
    public void OnModLoad(IModContext context) { host = new GameObject("Issue39 observation"); UnityEngine.Object.DontDestroyOnLoad(host); probe = host.AddComponent<Observation>(); probe.Init(context.ModDirectory); }
    public void OnWorldLoad() { probe.WorldLoad(); }
    public void OnWorldUnload() { probe.WorldUnload(); }
    public void OnModUnload() { if (host != null) UnityEngine.Object.Destroy(host); }
}
// How a buffered attempt trace is tied to a world load. It uses no Unity type, so a recorded observations.jsonl can be
// replayed through it outside the game.
public static class TraceLoad {
    // A trace captured outside a load names none. Its sourceLoadId can be a JSON null or a string value that is null,
    // which is what assigning a null string to a JObject member gives; both count as unassigned.
    public static bool Unassigned(JObject trace) => trace != null && String.IsNullOrEmpty((string)trace["sourceLoadId"]);
    // Gives an unassigned trace the load it was captured for, marked as assigned at the observer's world-load callback.
    public static void Assign(JObject trace, string loadId) {
        if (!Unassigned(trace) || String.IsNullOrEmpty(loadId)) return;
        trace["sourceLoadId"] = loadId; trace["sourceLifecycle"] = "active-world"; trace["assignedAtWorldLoad"] = true;
    }
    // The event a buffered trace is written as once the observer is ready for the load.
    public static string Event(JObject trace, string loadId) => (string)trace?["sourceLoadId"] == loadId ? "attempt-trace" : "unassigned-attempt-trace";
}
public sealed class Observation : MonoBehaviour {
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string Home = @"C:\TMP\RailroaderMapResearch\RuntimeHome";
    const string Mod = Home + @"\Mods\Issue39Observation";
    const string BoardPath = "World/Signs/Loc: Sylva Int";
    string directory, file, loadId, mapId, railroaderHash, observerHash, patcherHash, coreHash, referenceError; JObject referenceSource; string status = "Waiting for isolated fixture";
    bool loaded, ready, tracingSnapshot; Vector3? baselinePoint; long sequence; readonly List<JObject> buffered = new List<JObject>(); object terrain; EventInfo tileEvent; Action<Bounds> tileHandler;
    Rect window = new Rect(740, 30, 380, 290);
    public void Init(string path) { directory = path; Application.logMessageReceived += Log; }
    // The patcher applies its scene patch files in its own world-load callback, which the game calls before this
    // observer's, because the observer depends on the patcher. The traces buffered since the last world unload belong to
    // this load: they get its load ID, and assignedAtWorldLoad marks that they arrived before this callback.
    public void WorldLoad() {
        Unsubscribe(); loaded = true; ready = false; mapId = null; loadId = Guid.NewGuid().ToString("N"); baselinePoint = null; referenceSource = null; referenceError = null;
        foreach (var trace in buffered) TraceLoad.Assign(trace, loadId);
    }
    public void WorldUnload() { if (ready && GuardStillValid()) Emit("world-unload", new JObject()); loaded = false; ready = false; Unsubscribe(); buffered.Clear(); }
    void OnDestroy() { Application.logMessageReceived -= Log; Unsubscribe(); }
    // Attempt traces are recorded on any map in the isolated home; snapshots, tiles, focus and replay need the fixture map.
    const string FixtureMap = "crankyant/fixed-placement-fixture-map";
    static string ActiveMap() => Convert.ToString(Read(TypeNamed("Game.ActiveMapDefinition"), "Identifier"));
    void GuardHome() { if (!HomeValid()) throw new InvalidOperationException("Unexpected home or observer mod"); }
    void Guard() { GuardHome(); if (ActiveMap() != FixtureMap) throw new InvalidOperationException("Unexpected map"); }
    static int? EngineRuntimeId() { try { var patcher = Resources.FindObjectsOfTypeAll(TypeNamed("RailroaderScenePatcher.PatcherBehaviour")).OfType<Component>().SingleOrDefault(c => c != null); var engine = patcher == null ? null : Read(patcher, "engine"); return engine == null ? (int?)null : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(engine); } catch { return null; } }
    static string HashFile(string path) { using (var sha = System.Security.Cryptography.SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
    static bool IsPresent(object value) => value is UnityEngine.Object native ? native != null : value != null;
    void LoadReference() {
        var path = Path.Combine(directory, "Baseline.json"); if (!File.Exists(path)) return;
        try {
            var wrapper = JObject.Parse(File.ReadAllText(path)); var snapshot = wrapper["snapshot"] as JObject; var board = snapshot?["board"] as JObject; var pos = board?["mapPosition"] as JArray;
            if ((int?)wrapper["schemaVersion"] != 1 || (string)snapshot?["event"] != "snapshot" || (string)snapshot?["label"] != "baseline" || (string)snapshot?["mapId"] != "crankyant/fixed-placement-fixture-map" || (int?)board?["matches"] != 1 || (bool?)board?["eligible"] != true || (string)board?["scene"] != "BushnellWhittier-MapManager" || (string)board?["path"] != BoardPath || (string)board?["component"] != "Helpers.Culling.RendererCuller" || (string)board?["structure"] != "independent-board-v1" || pos == null || pos.Count != 3 || pos.Any(x => x.Type != JTokenType.Float && x.Type != JTokenType.Integer) || String.IsNullOrEmpty((string)snapshot?["loadId"]) || snapshot?["sequence"] == null || snapshot?["processId"] == null) throw new InvalidDataException("Baseline snapshot identity or coordinates are invalid");
            var point = new Vector3((float)pos[0], (float)pos[1], (float)pos[2]); if (Single.IsNaN(point.x) || Single.IsInfinity(point.x) || Single.IsNaN(point.y) || Single.IsInfinity(point.y) || Single.IsNaN(point.z) || Single.IsInfinity(point.z)) throw new InvalidDataException("Non-finite baseline position");
            baselinePoint = point; referenceSource = new JObject { ["loadId"] = snapshot["loadId"].DeepClone(), ["sequence"] = snapshot["sequence"].DeepClone(), ["processId"] = snapshot["processId"].DeepClone(), ["graphInstanceId"] = snapshot["graphInstanceId"]?.DeepClone(), ["boardSceneHandle"] = board["sceneHandle"]?.DeepClone(), ["boardInstanceId"] = board["instanceId"]?.DeepClone(), ["mapPosition"] = pos.DeepClone() };
        } catch (Exception e) { baselinePoint = null; referenceError = e.GetBaseException().Message; }
    }
    bool GuardStillValid() { try { GuardHome(); return true; } catch { return false; } }
    static bool Same(string a, string b) => String.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    void Update() {
        if (loaded && Input.GetKeyDown(KeyCode.F8) && HomeValid()) PointerReadout();
        if (!loaded || ready) return;
        try {
            GuardHome(); terrain = Read(TypeNamed("Map.Runtime.MapManager"), "Instance"); if (terrain == null) return; mapId = ActiveMap(); var fixture = mapId == FixtureMap;
            tileEvent = terrain.GetType().GetEvent("OnTileBuilt", F); if (tileEvent == null || tileEvent.EventHandlerType != typeof(Action<Bounds>)) throw new MissingMemberException("OnTileBuilt");
            if (railroaderHash == null) { var game = HashFile(TypeNamed("Game.ActiveMapDefinition").Assembly.Location); var observer = HashFile(Assembly.GetExecutingAssembly().Location); var patcher = HashFile(TypeNamed("RailroaderScenePatcher.PatcherBehaviour").Assembly.Location); railroaderHash = game; observerHash = observer; patcherHash = patcher; coreHash = HashFile(TypeNamed("RailroaderScenePatcher.Core.PatchEngine").Assembly.Location); } if (fixture) LoadReference(); file = Path.Combine(directory, "Evidence", loadId, "observations.jsonl"); Directory.CreateDirectory(Path.GetDirectoryName(file));
            if (fixture) { tileHandler = TileBuilt; tileEvent.AddEventHandler(terrain, tileHandler); } else tileEvent = null; ready = true;
            Emit("world-ready", new JObject { ["path"] = file, ["fixtureMap"] = fixture, ["engineRuntimeId"] = EngineRuntimeId(), ["gameVersion"] = Application.version, ["referenceSource"] = referenceSource == null ? null : referenceSource.DeepClone(), ["referenceError"] = referenceError }); foreach (var trace in buffered) Emit(TraceLoad.Event(trace, loadId), trace); buffered.Clear(); status = !fixture ? "Recording attempt traces on " + mapId + ". Snapshots need the fixture map." : referenceError == null ? "Ready. Record each phase explicitly." : "Invalid baseline reference: " + referenceError;
        } catch (Exception e) { status = "Waiting: " + e.GetBaseException().Message; }
    }
    void Unsubscribe() { if (tileEvent != null && terrain != null && tileHandler != null) tileEvent.RemoveEventHandler(terrain, tileHandler); tileEvent = null; tileHandler = null; terrain = null; }
    void TileBuilt(Bounds b) { try { Guard(); Emit("tile-built", new JObject { ["mapBounds"] = BoundsValue(b) }); } catch (Exception e) { status = e.GetBaseException().Message; } }
    // The patcher writes each engine stage as a debug line while its debug option is on: "Attempt trace: " and one JSON
    // object, inside the game's log line, which ends with the line's properties.
    const string AttemptTrace = "Attempt trace: ";
    static JObject TracePayload(string condition) { var at = condition.IndexOf(AttemptTrace, StringComparison.Ordinal); if (at < 0) return null; try { using (var reader = new JsonTextReader(new StringReader(condition.Substring(at + AttemptTrace.Length)))) return JObject.Load(reader); } catch { return null; } }
    void Log(string condition, string stack, LogType type) { if (condition == null || condition.IndexOf(AttemptTrace, StringComparison.Ordinal) < 0) return; var json = TracePayload(condition); var trace = new JObject { ["message"] = condition, ["trace"] = json?.DeepClone(), ["logType"] = type.ToString(), ["sourceFrame"] = Time.frameCount, ["sourceUtc"] = DateTime.UtcNow.ToString("O"), ["sourceLoadId"] = loaded ? (JToken)loadId : JValue.CreateNull(), ["sourceLifecycle"] = loaded ? "active-world" : "unassigned", ["engineRuntimeId"] = json?["engineRuntimeId"]?.DeepClone() }; if (!ready) { buffered.Add(trace); return; } try { GuardHome(); Emit("attempt-trace", trace); if (!tracingSnapshot && json != null && mapId == FixtureMap) { var stage = (string)json["stage"]; if (stage == "mutation" || stage == "applied" || stage == "restore-request" || stage == "restore-result") { tracingSnapshot = true; try { Snapshot("trace-" + stage); } finally { tracingSnapshot = false; } } } } catch (Exception e) { status = e.GetBaseException().Message; } }
    void Emit(string kind, JObject data) { data["schemaVersion"] = 1; data["evidenceKind"] = "native-observation"; data["railroaderSha256"] = railroaderHash; data["installedAssemblySha256"] = observerHash; data["patcherAssemblySha256"] = patcherHash; data["coreAssemblySha256"] = coreHash; data["event"] = kind; data["sequence"] = ++sequence; data["loadId"] = loadId; data["frame"] = Time.frameCount; data["processId"] = System.Diagnostics.Process.GetCurrentProcess().Id; data["mapId"] = mapId; data["utc"] = DateTime.UtcNow.ToString("O"); File.AppendAllText(file, data.ToString(Formatting.None) + "\n"); }
    static Type TypeNamed(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
    static object Read(object o, string name) { var t = o as Type ?? o.GetType(); var instance = o is Type ? null : o; for (; t != null; t = t.BaseType) { var p = t.GetProperty(name, F | BindingFlags.DeclaredOnly); if (p != null) return p.GetValue(instance, null); var f = t.GetField(name, F | BindingFlags.DeclaredOnly); if (f != null) return f.GetValue(instance); } throw new MissingMemberException(name); }
    static object Call(object o, string name, params object[] args) { var t = o as Type ?? o.GetType(); var methods = t.GetMethods(F).Where(m => m.Name == name && m.GetParameters().Length == args.Length).ToArray(); if (methods.Length != 1) throw new MissingMethodException(name); return methods[0].Invoke(o is Type ? null : o, args); }
    static Vector3 ConvertPosition(string method, Vector3 p) => (Vector3)Call(TypeNamed("Helpers.WorldTransformer"), method, p);
    static JArray V(Vector3 v) => new JArray(v.x, v.y, v.z);
    static JArray Q(Quaternion q) => new JArray(q.x, q.y, q.z, q.w);
    static JObject BoundsValue(Bounds b) => new JObject { ["center"] = V(b.center), ["size"] = V(b.size), ["min"] = V(b.min), ["max"] = V(b.max) };
    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    static Transform One(string path, string scene = null) { var a = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t != null && t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded && (scene == null || t.gameObject.scene.name == scene) && PathOf(t) == path).ToArray(); if (a.Length != 1) throw new InvalidOperationException(path + " match count=" + a.Length); return a[0]; }
    // The one loaded object at a path whose crossbuck target carries this pad marker ID, as the patcher gives it to a
    // crossbuck that shares its path with other crossbucks, such as the three below World/Roads Bryson/Old River Rd.
    static Transform OneCrossbuck(string path, string padMarkerId) {
        var world = TypeNamed("RailroaderScenePatcher.GroupWorld");
        var a = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t != null && t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded && PathOf(t) == path).ToArray();
        var b = a.Where(t => Convert.ToString(Call(world, "TargetPadMarkerId", t.gameObject)) == padMarkerId).ToArray();
        if (b.Length != 1) throw new InvalidOperationException(path + " with pad marker " + padMarkerId + " match count=" + b.Length + " of " + a.Length + " at the path");
        return b[0];
    }
    static JObject Placement(Transform t) => new JObject { ["instanceId"] = t.gameObject.GetInstanceID(), ["scene"] = t.gameObject.scene.name, ["sceneHandle"] = t.gameObject.scene.handle.ToString(), ["path"] = PathOf(t), ["parent"] = t.parent == null ? null : PathOf(t.parent), ["parentId"] = t.parent == null ? (int?)null : t.parent.gameObject.GetInstanceID(), ["worldPosition"] = V(t.position), ["worldRotation"] = Q(t.rotation), ["localPosition"] = V(t.localPosition), ["localRotation"] = Q(t.localRotation), ["localScale"] = V(t.localScale), ["lossyScale"] = V(t.lossyScale), ["mapPosition"] = V(ConvertPosition("WorldToGame", t.position)), ["activeSelf"] = t.gameObject.activeSelf, ["activeInHierarchy"] = t.gameObject.activeInHierarchy };
    static Component ComponentAt(Transform t, string type) => t.GetComponents<Component>().Single(c => c != null && c.GetType().FullName == type);
    JObject Marker() {
        var t = One("World/Mile Posts/T-47"); var post = ComponentAt(t, "Helpers.MilePost"); if (Convert.ToString(Read(post, "prefix")) != "T" || (bool)Read(post, "origin") || Convert.ToDouble(Read(post, "mileage")) != 47.0) throw new InvalidOperationException("Unexpected T-47 post identity"); var m = (Component)Read(post, "TrackMarker"); var id = Convert.ToString(Read(m, "id")); var markerCount = Resources.FindObjectsOfTypeAll(m.GetType()).OfType<Component>().Count(c => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && Convert.ToString(Read(c, "id")) == "Mnau"); if (id != "Mnau" || markerCount != 1) throw new InvalidOperationException("Unexpected marker identity or match count " + markerCount); var sync = (Component)Read(post, "_textSynchronizer");
        var location = Read(m, "Location"); var serial = Read(m, "_location"); var graph = Read(TypeNamed("Track.Graph"), "Shared"); var buckets = (IDictionary)Read(graph, "_namedLocationsBySegmentId"); var entries = new JArray();
        foreach (DictionaryEntry bucket in buckets) foreach (DictionaryEntry entry in (IDictionary)bucket.Value) if (Convert.ToString(entry.Key) == id) entries.Add(new JObject { ["bucket"] = Convert.ToString(bucket.Key), ["objectMatches"] = ReferenceEquals(Read(entry.Value, "ObjectValue"), m), ["locationMatches"] = Equals(Read(entry.Value, "Location"), location) });
        return new JObject { ["matches"] = markerCount, ["pose"] = Placement(m.transform), ["postPose"] = Placement(t), ["postInstanceId"] = post.GetInstanceID(), ["prefix"] = "T", ["origin"] = false, ["mileage"] = JToken.FromObject(Read(post, "mileage")), ["text"] = Convert.ToString(Read(sync, "text")), ["markerInstanceId"] = m.GetInstanceID(), ["id"] = id, ["enabled"] = ((Behaviour)m).enabled, ["segmentId"] = Convert.ToString(Read(serial, "segmentId")), ["distance"] = JToken.FromObject(Read(serial, "distance")), ["end"] = Convert.ToString(Read(serial, "end")), ["location"] = Convert.ToString(location), ["registrations"] = entries };
    }
    JObject Culler(Component c) {
        var token = Read(c, "_cullingToken"); var j = new JObject { ["enabled"] = ((Behaviour)c).enabled, ["radius"] = JToken.FromObject(Read(c, "radius")), ["tokenPresent"] = token != null };
        if (token == null) return j; var index = Convert.ToInt32(Read(token, "<Index>k__BackingField")); var removed = (bool)Read(token, "<IsRemoved>k__BackingField"); var manager = Read(token, "Manager"); var spheres = (BoundingSphere[])Read(manager, "_spheres"); var count = (int)Read(manager, "_sphereCount");
        j["index"] = index; j["removed"] = removed; j["managerInstanceId"] = manager is UnityEngine.Object u ? (int?)u.GetInstanceID() : null; j["sphereCount"] = count; j["member"] = !removed && index >= 0 && index < count && index < spheres.Length;
        if ((bool)j["member"]) j["sphere"] = new JObject { ["position"] = V(spheres[index].position), ["radius"] = spheres[index].radius }; return j;
    }
    JObject Mask(Component m) {
        var keys = ((IEnumerable)Read(m, "_modifierKeys")).Cast<object>().Select(Convert.ToString).ToArray(); var modifiers = (IDictionary)Read(Read(terrain, "_maskModifiers"), "_modifiers"); var offset = (Vector3)Read(terrain, "_gameToWorldOffset"); var registered = new JArray();
        foreach (var key in keys) { var j = new JObject { ["key"] = key, ["registered"] = modifiers.Contains(key) }; if (modifiers.Contains(key)) { var mod = modifiers[key]; var d = Read(mod, "Mask"); j["maskName"] = Convert.ToString(Read(mod, "MaskName")); j["value"] = JToken.FromObject(Read(mod, "Value")); j["descriptorType"] = d.GetType().FullName; var b = (Bounds)Read(d, "Bounds"); j["bounds"] = BoundsValue(b); j["worldCenter"] = V(b.center + offset); if (d.GetType().FullName == "Map.Runtime.CurveMaskDescriptor") { j["radius"] = JToken.FromObject(Read(d, "Radius")); j["falloff"] = JToken.FromObject(Read(d, "Falloff")); } } registered.Add(j); }
        var targets = new JArray(); foreach (var target in (IEnumerable)Read(m, "maskTargets")) { if (target == null) { targets.Add(JValue.CreateNull()); continue; } targets.Add(new JObject { ["kind"] = Convert.ToString(Read(target, "kind")), ["maskName"] = Convert.ToString(Read(target, "maskName")), ["value"] = JToken.FromObject(Read(target, "value")), ["useCustomStampOffset"] = JToken.FromObject(Read(target, "useCustomStampOffset")), ["stampOffset"] = Convert.ToString(Read(target, "stampOffset")), ["biomeCode"] = Convert.ToString(Read(target, "biomeCode")) }); }
        return new JObject { ["enabled"] = ((Behaviour)m).enabled, ["radius"] = JToken.FromObject(Read(m, "radius")), ["falloff"] = JToken.FromObject(Read(m, "falloff")), ["flattenHeightmap"] = JToken.FromObject(Read(m, "flattenHeightmap")), ["order"] = JToken.FromObject(Read(m, "order")), ["gameToWorldOffset"] = V(offset), ["modifiers"] = registered, ["targets"] = targets };
    }
    JArray LoadedMods() {
        var manager = Read(TypeNamed("Model.Database.AssetPackManager"), "Shared");
        var entries = (IEnumerable)Read(manager, "OrderedPacks");
        var result = new JArray();
        foreach (var entry in entries) {
            var store = Read(entry, "Store");
            result.Add(new JObject { ["identifier"] = Convert.ToString(Read(store, "Identifier")), ["basePath"] = Convert.ToString(Read(store, "BasePath")) });
        }
        return result;
    }
    JObject SaveContext() {
        var mapEditor = (bool)Read(TypeNamed("Game.ActiveMapDefinition"), "IsEditTarget");
        var manager = Read(TypeNamed("Game.State.SaveManager"), "Shared");
        var path = manager == null ? null : Read(manager, "RailroadPath") as string;
        if (!String.IsNullOrEmpty(path) && !Path.GetFullPath(path).StartsWith(Home + @"\Saves\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Save context points outside the isolated Saves directory");
        return new JObject { ["isEditor"] = mapEditor, ["railroadPath"] = path, ["loadedSaveSlug"] = manager == null ? null : Convert.ToString(Read(manager, "LoadedSaveSlug")) };
    }
    void Snapshot(string label) {
        try { Guard(); if (!ready || !ReferenceEquals(terrain, Read(TypeNamed("Map.Runtime.MapManager"), "Instance"))) throw new InvalidOperationException("Terrain context changed"); var board = One(BoardPath, "BushnellWhittier-MapManager"); var transformer = Read(TypeNamed("Helpers.WorldTransformer"), "Shared"); var p = ConvertPosition("WorldToGame", board.position); if (label == "baseline") { if (File.Exists(Path.Combine(directory, "Baseline.json"))) throw new InvalidOperationException("Baseline.json exists; baseline capture disabled"); baselinePoint = p; } var boardData = Placement(board); boardData["matches"] = 1; boardData["component"] = "Helpers.Culling.RendererCuller"; boardData["structure"] = "independent-board-v1"; boardData["rotation"] = Q(board.rotation); boardData["eligible"] = board.gameObject.activeInHierarchy && ((Behaviour)ComponentAt(board, "Helpers.Culling.RendererCuller")).enabled && ((Behaviour)ComponentAt(board, "Map.Runtime.MaskComponents.CircleMapMask")).enabled && !board.GetComponentsInChildren<Component>(true).Any(c => c == null || (c.GetType().FullName ?? "").StartsWith("Track.", StringComparison.Ordinal)); var graph = Read(TypeNamed("Track.Graph"), "Shared"); var patcherType = TypeNamed("RailroaderScenePatcher.PatcherBehaviour"); var patcher = Resources.FindObjectsOfTypeAll(patcherType).OfType<Component>().Single(); var engine = Read(patcher, "engine"); if (engine == null) throw new InvalidOperationException("No engine"); var maskData = Mask(ComponentAt(board, "Map.Runtime.MaskComponents.CircleMapMask")); var registeredMasks = ((JArray)maskData["modifiers"]).OfType<JObject>().Where(x => (bool)x["registered"]).ToArray(); boardData["eligible"] = (bool)boardData["eligible"] && registeredMasks.Length > 0 && registeredMasks.Length == ((JArray)maskData["modifiers"]).Count && registeredMasks.All(x => (string)x["descriptorType"] == "Map.Runtime.CurveMaskDescriptor"); var data = new JObject { ["label"] = label, ["graphInstanceId"] = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(graph), ["engineRuntimeId"] = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(engine), ["measurementErrors"] = referenceError == null ? new JArray() : new JArray("baseline reference: " + referenceError), ["referenceSource"] = referenceSource == null ? null : referenceSource.DeepClone(), ["origin"] = new JObject { ["offset"] = V((Vector3)Read(transformer, "_currentOffset")), ["gameZero"] = V(ConvertPosition("WorldToGame", Vector3.zero)), ["boardMapToWorldRoundtrip"] = V(ConvertPosition("GameToWorld", p)) }, ["board"] = boardData, ["marker"] = Marker(), ["culler"] = Culler(ComponentAt(board, "Helpers.Culling.RendererCuller")), ["mask"] = maskData, ["terrain"] = new JObject { ["pendingInvalidationCount"] = JToken.FromObject(Read(terrain, "PendingInvalidationCount")), ["idle"] = JToken.FromObject(Call(terrain, "IsTerrainWorkIdle")) } }; data["loadedPacks"] = LoadedMods(); data["saveContext"] = SaveContext(); try { data["generatedTrees"] = SampleTrees(); } catch (Exception ex) { ((JArray)data["measurementErrors"]).Add("generatedTrees: " + ex.GetBaseException().Message); } Emit("snapshot", data); status = label + " recorded"; }
        catch (Exception e) { if (ready && GuardStillValid()) Emit("snapshot-error", new JObject { ["label"] = label, ["error"] = e.GetBaseException().ToString() }); status = "Snapshot failed: " + e.GetBaseException().Message; }
    }
    JArray SampleTrees() {
        if (!baselinePoint.HasValue) throw new InvalidOperationException("Baseline point unavailable");
        var result = new JArray();
        foreach (var mapPoint in new[] { baselinePoint.Value, baselinePoint.Value + new Vector3(5, 0, 0) }) {
            var point = ConvertPosition("GameToWorld", mapPoint); var matches = Terrain.activeTerrains.Where(t => t != null && t.terrainData != null && point.x >= t.transform.position.x && point.z >= t.transform.position.z && point.x < t.transform.position.x + t.terrainData.size.x && point.z < t.transform.position.z + t.terrainData.size.z).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Terrain match count=" + matches.Length);
            var terrainObject = matches[0]; var mapTerrain = terrainObject.GetComponent(TypeNamed("Map.Runtime.MapTerrain")); var gpui = terrainObject.GetComponent(TypeNamed("Map.Runtime.RailroaderGPUITerrain"));
            if (mapTerrain == null || gpui == null || !ReferenceEquals(Read(mapTerrain, "terrain"), terrainObject)) throw new InvalidOperationException("Terrain association mismatch");
            var generated = (TreeInstance[])Read(mapTerrain, "TreeInstances"); var handoff = (TreeInstance[])Read(gpui, "TreeInstances"); var loadedTrees = (TreeInstance[])Read(gpui, "_treeInstances"); if (generated == null) throw new InvalidOperationException("TreeInstances missing");
            var rows = new List<string>(); foreach (var tree in generated) { var world = terrainObject.transform.position + Vector3.Scale(tree.position, terrainObject.terrainData.size); var delta = world - point; if (delta.x * delta.x + delta.z * delta.z > 400f) continue; var mp = ConvertPosition("WorldToGame", world); rows.Add(string.Join(",", new[] { mp.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), mp.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), mp.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture), tree.prototypeIndex.ToString(), tree.widthScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture), tree.heightScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture), tree.rotation.ToString("R", System.Globalization.CultureInfo.InvariantCulture) })); }
            rows.Sort(StringComparer.Ordinal); string hash; using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", rows)))).Replace("-", "");
            var relative = point - terrainObject.transform.position; var height = terrainObject.terrainData.GetInterpolatedHeight(relative.x / terrainObject.terrainData.size.x, relative.z / terrainObject.terrainData.size.z); var mapHeight = ConvertPosition("WorldToGame", new Vector3(point.x, terrainObject.transform.position.y + height, point.z)).y; result.Add(new JObject { ["interpolatedMapHeight"] = mapHeight, ["localRows"] = new JArray(rows), ["mapPoint"] = V(mapPoint), ["radius"] = 20, ["terrainInstanceId"] = terrainObject.GetInstanceID(), ["generatedCount"] = generated.Length, ["localCount"] = rows.Count, ["localSha256"] = hash, ["handoffSame"] = ReferenceEquals(generated, handoff), ["loadedSame"] = ReferenceEquals(handoff, loadedTrees), ["isLoaded"] = (bool)Read(gpui, "IsTreeInstancesLoaded"), ["managerPresent"] = IsPresent(Read(gpui, "TreeManager")) });
        }
        return result;
    }
    void Focus(string path) {
        try {
            Guard(); if (!ready) throw new InvalidOperationException("Not ready");
            var target = One(path, "BushnellWhittier-MapManager"); var cameraType = TypeNamed("CameraSelector");
            var selector = Read(cameraType, "shared");
            var identifier = cameraType.GetNestedType("CameraIdentifier", F);
            var point = ConvertPosition("WorldToGame", target.position);
            Call(selector, "JumpToPoint", point, Quaternion.identity, Enum.Parse(identifier, "Strategy"));
            Emit("camera-focus", new JObject { ["path"] = path, ["mapPosition"] = V(point) });
        } catch (Exception e) { if (ready && GuardStillValid()) Emit("focus-error", new JObject { ["error"] = e.GetBaseException().Message }); status = "Focus failed: " + e.GetBaseException().Message; }
    }
    void Replay() { try { Guard(); if (!ready) throw new InvalidOperationException("Not ready"); var type = TypeNamed("RailroaderScenePatcher.PatcherBehaviour"); var matches = Resources.FindObjectsOfTypeAll(type).OfType<Component>().Where(c => c != null).ToArray(); if (matches.Length != 1) throw new InvalidOperationException("Patcher count=" + matches.Length); var p = matches[0]; var engine = Read(p, "engine"); if (engine == null) throw new InvalidOperationException("No engine"); Emit("replay-start", new JObject { ["patcherInstanceId"] = p.GetInstanceID(), ["engineIdentity"] = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(engine) }); Call(p, "ApplyScenePatchFiles"); Emit("replay-end", new JObject { ["engineSame"] = ReferenceEquals(engine, Read(p, "engine")) }); status = "Replay callback completed"; } catch (Exception e) { if (ready && GuardStillValid()) Emit("replay-error", new JObject { ["error"] = e.GetBaseException().ToString() }); status = "Replay failed: " + e.GetBaseException().Message; } }
    // The culling readout serves any map in the isolated home: it needs the home and the observer's mod folder, not the
    // fixed-placement fixture. It writes Evidence/culling/readout.jsonl under the mod folder.
    string cullingPath = "World/Large Scenery/Whittier/Whittier Saw Mill", cullingPadMarkerId = "", cullingLabel = "", cullingStatus = "No readout yet"; long cullingSequence;
    Rect cullingWindow = new Rect(740, 330, 380, 240); long colliderSequence;
    bool HomeValid() {
        try {
            var args = Environment.GetCommandLineArgs(); var ix = Enumerable.Range(0, args.Length).Where(i => String.Equals(args[i], "-homedir", StringComparison.OrdinalIgnoreCase)).ToArray();
            return ix.Length == 1 && ix[0] + 1 < args.Length && Same(args[ix[0] + 1], Home) && Same(directory, Mod);
        } catch { return false; }
    }
    static object Field(object o, string name) { for (var t = o.GetType(); t != null; t = t.BaseType) { var f = t.GetField(name, F | BindingFlags.DeclaredOnly); if (f != null) return f.GetValue(o); } return null; }
    JObject Sphere(Vector3 position, float radius) => new JObject { ["scenePosition"] = V(position), ["mapPosition"] = V(ConvertPosition("WorldToGame", position)), ["radius"] = radius };
    // One game culler: the radius the scene gives it, the sphere it holds, and whether that sphere is visible.
    JObject GameCuller(Component c) {
        var type = c.GetType().FullName; var j = new JObject { ["path"] = PathOf(c.transform), ["type"] = type, ["authoredRadius"] = JToken.FromObject(Read(c, "radius")), ["activeSelf"] = c.gameObject.activeSelf, ["activeInHierarchy"] = c.gameObject.activeInHierarchy };
        if (type == "Helpers.SimpleCuller") {
            var group = Field(c, "_cullingGroup") as CullingGroup; var spheres = Field(c, "_cullingSpheres") as BoundingSphere[];
            j["started"] = group != null;
            if (group != null) { j["groupEnabled"] = group.enabled; j["visible"] = group.IsVisible(0); j["handler"] = group.onStateChanged == null ? null : group.onStateChanged.Method.DeclaringType.FullName + "." + group.onStateChanged.Method.Name; }
            if (spheres != null && spheres.Length > 0) j["sphere"] = Sphere(spheres[0].position, spheres[0].radius);
            return j;
        }
        j["state"] = Convert.ToString(Field(c, "_cullingState")); j["visibleDistanceBand"] = JToken.FromObject(Read(c, "visibleDistanceBand")); j["manager"] = Convert.ToString(Field(c, "cullingManagerName"));
        var token = Field(c, "_cullingToken"); j["tokenPresent"] = token != null;
        if (token != null && !(bool)Read(token, "IsRemoved")) { var index = Convert.ToInt32(Read(token, "Index")); var all = (BoundingSphere[])Read(Read(token, "Manager"), "_spheres"); if (all != null && index >= 0 && index < all.Length) j["sphere"] = Sphere(all[index].position, all[index].radius); }
        if (type == "Helpers.Culling.RendererCuller") { var list = Field(c, "_cullRenderers") as Renderer[]; j["renderers"] = list == null ? null : new JArray(list.Where(r => r != null).Select(r => new JObject { ["path"] = PathOf(r.transform), ["enabled"] = r.enabled })); }
        else { j["mode"] = Convert.ToString(Field(c, "cullingMode")); j["listed"] = new JArray(((IEnumerable)Read(c, "gameObjects")).Cast<GameObject>().Where(g => g != null).Select(g => new JObject { ["path"] = PathOf(g.transform), ["activeSelf"] = g.activeSelf })); }
        return j;
    }
    void CullingReadout() {
        try {
            if (!HomeValid()) throw new InvalidOperationException("Unexpected home or observer mod");
            var padMarkerId = cullingPadMarkerId.Trim();
            var root = padMarkerId.Length == 0 ? One(cullingPath) : OneCrossbuck(cullingPath, padMarkerId);
            var data = new JObject { ["evidenceKind"] = "culling-readout", ["schemaVersion"] = 1, ["label"] = cullingLabel, ["padMarkerId"] = padMarkerId.Length == 0 ? null : padMarkerId, ["sequence"] = ++cullingSequence, ["utc"] = DateTime.UtcNow.ToString("O"), ["frame"] = Time.frameCount, ["gameVersion"] = Application.version, ["group"] = Placement(root) };
            data["patcherAssemblySha256"] = HashFile(TypeNamed("RailroaderScenePatcher.PatcherBehaviour").Assembly.Location);
            var camera = Camera.main; if (camera != null) data["camera"] = new JObject { ["mapPosition"] = V(ConvertPosition("WorldToGame", camera.transform.position)), ["rotation"] = Q(camera.transform.rotation), ["distanceToGroup"] = (camera.transform.position - root.position).magnitude };
            data["cullers"] = new JArray(root.GetComponentsInChildren<Component>(true).Where(c => c != null && (c.GetType().FullName == "Helpers.SimpleCuller" || c.GetType().FullName == "Helpers.Culling.RendererCuller" || c.GetType().FullName == "Helpers.Culling.GameObjectCuller")).Select(GameCuller));
            // The own spheres of moved parts, as the patcher's own-culling controller reports them.
            var controller = TypeNamed("RailroaderScenePatcher.GroupWorld").GetField("culling", F)?.GetValue(null);
            var own = new JArray(); var owners = new List<Transform>();
            if (controller != null)
                foreach (var region in (IEnumerable)Call(controller, "Regions", root.gameObject)) {
                    var owner = Read(region, "Owner") as GameObject; var centre = Read(region, "Centre") as IEnumerable; if (owner != null) owners.Add(owner.transform);
                    own.Add(new JObject { ["id"] = Convert.ToString(Read(region, "Id")), ["owner"] = owner == null ? null : PathOf(owner.transform), ["mapCenter"] = centre == null ? null : new JArray(centre.Cast<double>()), ["radius"] = JToken.FromObject(Read(region, "Radius")), ["pending"] = JToken.FromObject(Read(region, "Pending")), ["shows"] = JToken.FromObject(Read(region, "Shows")),
                        ["ownerMapPosition"] = owner == null ? null : V(ConvertPosition("WorldToGame", owner.transform.position)), ["ownerActiveSelf"] = owner == null ? (bool?)null : owner.activeSelf, ["ownerActiveInHierarchy"] = owner == null ? (bool?)null : owner.activeInHierarchy,
                        ["ownerRenderers"] = owner == null ? null : new JArray(owner.GetComponentsInChildren<Renderer>(true).Select(r => new JObject { ["path"] = PathOf(r.transform), ["enabled"] = r.enabled })) });
                }
            data["ownSpheres"] = own; data["controllerPresent"] = controller != null;
            // Every game-object culler of the loaded scenes with the path of each object it lists, and the objects the
            // patcher took out of a list for a moved part, with the culler they belong to.
            data["gameObjectCullers"] = new JArray(Resources.FindObjectsOfTypeAll<Component>().Where(c => c != null && c.GetType().FullName == "Helpers.Culling.GameObjectCuller" && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded)
                .Select(c => new JObject { ["scene"] = c.gameObject.scene.name, ["path"] = PathOf(c.transform), ["listedPaths"] = new JArray(((IEnumerable)Read(c, "gameObjects")).Cast<GameObject>().Where(g => g != null).Select(g => PathOf(g.transform))) }));
            if (controller != null) data["handedOver"] = new JArray(((IEnumerable)Call(controller, "HandedOver")).Cast<object>().Select(item => new JObject { ["path"] = Read(item, "Item1") is GameObject g && g != null ? PathOf(g.transform) : null, ["culler"] = Read(item, "Item2") is Component k && k != null ? PathOf(k.transform) : null, ["shows"] = JToken.FromObject(Read(item, "Item3")) }));
            // Activity of the root and every object below it, with the value the patcher holds as desired, if any.
            var activity = new JArray();
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Take(600)) {
                var desired = controller == null ? null : Call(controller, "Desired", t.gameObject);
                var row = new JObject { ["path"] = PathOf(t), ["activeSelf"] = t.gameObject.activeSelf, ["activeInHierarchy"] = t.gameObject.activeInHierarchy, ["desired"] = desired == null ? null : JToken.FromObject(desired) };
                // The components directly on each object between the group root and a moved part with its own sphere.
                if (owners.Any(o => o != t && o.IsChildOf(t))) row["components"] = new JArray(t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => new JObject { ["type"] = c.GetType().FullName, ["enabled"] = c.GetType().GetProperty("enabled")?.GetValue(c, null) as bool? }));
                activity.Add(row);
            }
            data["activity"] = activity;
            var path = Path.Combine(directory, "Evidence", "culling", "readout.jsonl"); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, data.ToString(Formatting.None) + "\n");
            cullingStatus = "Readout " + cullingSequence + " written: " + ((JArray)data["cullers"]).Count + " cullers, " + own.Count + " own spheres";
        } catch (Exception e) { cullingStatus = "Readout failed: " + e.GetBaseException().Message; }
    }
    // Every Collider below the culling target, enabled or not, with its type, trigger flag, attached Rigidbody and world
    // bounds. It uses the same target identity as the culling readout (group path, optional pad marker ID) and writes one
    // row per collider to Evidence/culling/colliders.jsonl, or one no-colliders row when the target has none.
    void ColliderReadout() {
        try {
            if (!HomeValid()) throw new InvalidOperationException("Unexpected home or observer mod");
            var padMarkerId = cullingPadMarkerId.Trim();
            var root = padMarkerId.Length == 0 ? One(cullingPath) : OneCrossbuck(cullingPath, padMarkerId);
            var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => c != null).ToArray();
            var common = new JObject { ["evidenceKind"] = "collider-readout", ["schemaVersion"] = 1, ["label"] = cullingLabel, ["padMarkerId"] = padMarkerId.Length == 0 ? null : padMarkerId, ["sequence"] = ++colliderSequence, ["utc"] = DateTime.UtcNow.ToString("O"), ["frame"] = Time.frameCount, ["gameVersion"] = Application.version, ["group"] = Placement(root), ["count"] = colliders.Length };
            common["patcherAssemblySha256"] = HashFile(TypeNamed("RailroaderScenePatcher.PatcherBehaviour").Assembly.Location);
            var path = Path.Combine(directory, "Evidence", "culling", "colliders.jsonl"); Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string>();
            if (colliders.Length == 0) { var row = new JObject(common); row["event"] = "no-colliders"; lines.Add(row.ToString(Formatting.None)); }
            for (var i = 0; i < colliders.Length; i++) {
                var c = colliders[i]; var body = c.attachedRigidbody; var row = new JObject(common);
                row["event"] = "collider"; row["index"] = i; row["path"] = PathOf(c.transform); row["type"] = c.GetType().FullName; row["enabled"] = c.enabled; row["isTrigger"] = c.isTrigger;
                row["activeSelf"] = c.gameObject.activeSelf; row["activeInHierarchy"] = c.gameObject.activeInHierarchy;
                row["rigidbody"] = body == null ? null : new JObject { ["path"] = PathOf(body.transform), ["instanceId"] = body.GetInstanceID(), ["isKinematic"] = body.isKinematic };
                row["bounds"] = new JObject { ["center"] = V(c.bounds.center), ["size"] = V(c.bounds.size) };
                lines.Add(row.ToString(Formatting.None));
            }
            File.AppendAllLines(path, lines);
            cullingStatus = "Collider readout " + colliderSequence + " written: " + colliders.Length + " colliders";
        } catch (Exception e) { cullingStatus = "Collider readout failed: " + e.GetBaseException().Message; }
    }
    // Every UI object under the mouse pointer, top hit first, as the event system raycasts a click there: for each, its
    // selectables with their enabled and interactable state and the parent canvas groups that can block them. Bound to
    // F8 so the pointer can stay on the control. It writes Evidence/culling/pointer.jsonl under the mod folder.
    void PointerReadout() {
        try {
            var system = UnityEngine.EventSystems.EventSystem.current;
            if (system == null) throw new InvalidOperationException("No event system");
            var pointer = new UnityEngine.EventSystems.PointerEventData(system) { position = Input.mousePosition };
            var hits = new List<UnityEngine.EventSystems.RaycastResult>(); system.RaycastAll(pointer, hits);
            var data = new JObject { ["evidenceKind"] = "pointer-readout", ["schemaVersion"] = 1, ["label"] = cullingLabel, ["utc"] = DateTime.UtcNow.ToString("O"), ["frame"] = Time.frameCount,
                ["mouse"] = new JArray(Input.mousePosition.x, Input.mousePosition.y), ["screen"] = new JArray(Screen.width, Screen.height),
                ["hovered"] = system.currentSelectedGameObject == null ? null : PathOf(system.currentSelectedGameObject.transform) };
            data["hits"] = new JArray(hits.Where(h => h.gameObject != null).Select(h => {
                var rect = h.gameObject.transform as RectTransform;
                var clickHandler = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(h.gameObject);
                return new JObject { ["path"] = PathOf(h.gameObject.transform), ["layer"] = LayerMask.LayerToName(h.gameObject.layer), ["module"] = h.module == null ? null : h.module.GetType().FullName, ["depth"] = h.depth, ["sortingOrder"] = h.sortingOrder,
                    ["rect"] = rect == null ? null : new JArray(rect.rect.x, rect.rect.y, rect.rect.width, rect.rect.height),
                    ["clickHandler"] = clickHandler == null ? null : PathOf(clickHandler.transform) + " [" + String.Join(",", clickHandler.GetComponents<UnityEngine.EventSystems.IPointerClickHandler>().Select(c => c.GetType().FullName)) + "]",
                    ["selectables"] = new JArray(h.gameObject.GetComponentsInParent<UnityEngine.UI.Selectable>(true).Select(c => new JObject { ["path"] = PathOf(c.transform), ["type"] = c.GetType().FullName, ["enabled"] = c.enabled, ["activeAndEnabled"] = c.isActiveAndEnabled, ["interactable"] = c.interactable, ["interactableInHierarchy"] = c.IsInteractable() })),
                    ["canvasGroups"] = new JArray(h.gameObject.GetComponentsInParent<CanvasGroup>(true).Where(g => !g.interactable || !g.blocksRaycasts || g.ignoreParentGroups).Select(g => new JObject { ["path"] = PathOf(g.transform), ["interactable"] = g.interactable, ["blocksRaycasts"] = g.blocksRaycasts, ["ignoreParentGroups"] = g.ignoreParentGroups })) };
            }));
            var path = Path.Combine(directory, "Evidence", "culling", "pointer.jsonl"); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, data.ToString(Formatting.None) + "\n");
            cullingStatus = "Pointer readout written: " + hits.Count + " hits" + (hits.Count == 0 ? "" : ", top " + hits[0].gameObject.name);
        } catch (Exception e) { cullingStatus = "Pointer readout failed: " + e.GetBaseException().Message; }
    }
    void OnGUI() {
        if (loaded && HomeValid())
            cullingWindow = GUI.Window(0x4933394E, cullingWindow, id => {
                GUILayout.Label(cullingStatus);
                GUILayout.Label("Group path"); cullingPath = GUILayout.TextField(cullingPath);
                GUILayout.Label("Pad marker ID (crossbucks that share a path)"); cullingPadMarkerId = GUILayout.TextField(cullingPadMarkerId);
                GUILayout.Label("Label"); cullingLabel = GUILayout.TextField(cullingLabel);
                if (GUILayout.Button("Culling readout")) CullingReadout();
                if (GUILayout.Button("Collider readout")) ColliderReadout();
                GUI.DragWindow(new Rect(0, 0, 10000, 20));
            }, "Culling readout");
        if (!ready) return;
        window = GUI.Window(0x4933394F, window, id => {
            GUILayout.Label(status);
            if (GUILayout.Button("Focus board")) Focus(BoardPath);
            if (GUILayout.Button("Focus T-47")) Focus("World/Mile Posts/T-47");
            if (!File.Exists(Path.Combine(directory, "Baseline.json")) && GUILayout.Button("Snapshot baseline")) Snapshot("baseline");
            if (GUILayout.Button("Snapshot after")) Snapshot("after");
            if (GUILayout.Button("Snapshot restoration")) Snapshot("restoration");
            if (GUILayout.Button("Replay application callback once")) Replay();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }, "Issue 39 observation");
    }
}
