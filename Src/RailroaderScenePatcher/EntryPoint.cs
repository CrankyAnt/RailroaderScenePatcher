// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Railroader.Modding;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    public sealed class EntryPoint : IModEntryPoint, IWorldLifecycle {
        GameObject host;
        IModContext context;
        public void OnModLoad(IModContext value) { context = value; }
        public void OnWorldLoad() {
            if (host != null) { host.GetComponent<PatcherBehaviour>().ApplyDocuments(); return; }
            host = new GameObject("CrankyAnt Scene Patcher");
            host.AddComponent<PatcherBehaviour>().Initialize(context);
        }
        public void OnWorldUnload() { Cleanup(); }
        public void OnModUnload() { Cleanup(); }
        void Cleanup() {
            if (host == null) return;
            host.GetComponent<PatcherBehaviour>().Release();
            UnityEngine.Object.Destroy(host); host = null;
        }
    }
    public sealed class PatcherBehaviour : MonoBehaviour {
        IModContext context;
        BoardScene scene;
        PatchEngine engine;
        bool editor, open;
        string status = "Initializing", inspected = "", filter = "";
        readonly HashSet<string> reported = new HashSet<string>();
        readonly Dictionary<object, HashSet<string>> contributions = new Dictionary<object, HashSet<string>>();
        readonly List<string> diagnostics = new List<string>();
        Rect window = new Rect(20, 60, 700, 620);
        Vector2 scroll;
        readonly string loadId = Guid.NewGuid().ToString("N");
        internal void Initialize(IModContext value) {
            context = value;
#if ISSUE39_OBSERVATION
            JourneyObservation.Sink = message => Debug.Log("[Issue39Attempt] " + message);
#endif
            try {
                editor = PackDocuments.IsEditor;
                scene = new BoardScene(); engine = new PatchEngine(scene);
                context.Log.Info("Scene Patcher 0.1.0: native board capability available; game " + Application.version + "; production integration unchecked");
                ApplyDocuments();
            } catch (Exception e) {
                status = "Independent board capability incompatible or unavailable: " + e.GetBaseException().Message;
                Report("capability", status);
            }
        }
        internal void ApplyDocuments() {
            if (engine == null) return;
            try {
                foreach (var pack in PackDocuments.Discover()) {
                    if (pack.Error != null) { Report(pack.Identifier, Describe(pack, pack.Error)); continue; }
                    var result = engine.Apply(pack.Json, pack.Identifier);
                    if (result.Error != null) {
                        if (result.Error == "Source map does not match loaded map context") { EditorDiagnostic(Describe(pack, result.Error)); continue; }
                        if (result.Error.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0) EditorDiagnostic(Describe(pack, result.Error));
                        else Report(pack.Identifier, Describe(pack, result.Error));
                        continue;
                    }
                    foreach (var item in result.Outcomes) {
                        if (!item.Value.Applied) {
                            var error = Describe(pack, item.Key + ": " + item.Value.Error);
                            if (item.Value.Error.StartsWith("Unsupported", StringComparison.Ordinal) || item.Value.Error.StartsWith("Invalid or unsupported", StringComparison.Ordinal)) EditorDiagnostic(error);
                            else Report(pack.Identifier + "/" + item.Key, error + (item.Value.Error.StartsWith("Mutation failed", StringComparison.Ordinal) ? ". Report to the patcher maintainer; affected state may need reload." : ". Report this supplied definition to its MapMod author."));
                        }
                    }
                    try { ObserveOverlap(pack, result); }
                    catch (Exception e) { Report(pack.Identifier + "/overlap", "Overlap inspection failed; remaining documents will still be processed. Report to the patcher maintainer. " + e.GetBaseException().Message); }
                    status = "Documents processed. " + scene.Status;
                }
            } catch (Exception e) { Report("discovery", "Loaded MapMod discovery failed. Report to the patcher maintainer. " + e.GetBaseException().Message); }
        }
        void ObserveOverlap(PackDocument pack, DocumentResult result) {
            foreach (var outcome in result.Outcomes.Values) {
                if (!(outcome.ResolvedTarget is GameObject target) || target == null) continue;
                if (!contributions.TryGetValue(target, out var packs)) contributions.Add(target, packs = new HashSet<string>());
                packs.Add(pack.Identifier);
                if (packs.Count > 1) {
                    var key = "overlap/" + target.GetInstanceID() + "/" + String.Join(",", packs.OrderBy(x => x));
                    if (reported.Add(key)) Native.Call(Native.Type("Game.Diagnostics.MessageLog"), "Info", "Railroader Scene Patcher", "Possible incompatibility: supplying MapMods attempt to modify the same object " + BoardScene.PathOf(target.transform) + ": " + String.Join(", ", packs), "Scene patches", loadId + key);
                }
            }
        }
        static string Describe(PackDocument pack, string message) => (pack.Name ?? pack.Identifier) + " [" + pack.Identifier + " " + pack.Version + "] " + message + ". Patcher 0.1.0. Author: " + (pack.Author ?? "not supplied") + (String.IsNullOrWhiteSpace(pack.Website) ? "" : "; website: " + pack.Website);
        void EditorDiagnostic(string message) { if (editor && !diagnostics.Contains(message)) diagnostics.Add(message); }
        void Report(string key, string message) {
            if (!reported.Add(key)) return;
            diagnostics.Add(message); status = message;
            context?.Log.Info(message);
            try { Native.Call(Native.Type("Game.Diagnostics.MessageLog"), "Warning", "Railroader Scene Patcher", message, "Scene patches", loadId + key); }
            catch (Exception e) { context?.Log.Info("Native warning presentation unavailable: " + e.GetBaseException().Message); }
        }
        void Update() {
            if (scene == null) return;
            try { scene.PollCompletion(); }
            catch (Exception e) { Report("terrain", "Dependent terrain state remains unverified. Reload before retry. Report to the patcher maintainer. " + e.GetBaseException().Message); }
        }
        internal void Release() { scene?.Dispose(); scene = null; engine = null; }
        void OnDestroy() { Release(); }
        void OnGUI() {
            if (!editor) return;
            if (GUI.Button(new Rect(20, 20, 160, 30), "Scene patches")) open = !open;
            if (open) window = GUI.Window(0x43525350, window, DrawWindow, "Railroader Scene Patcher");
        }
        void DrawWindow(int id) {
            GUILayout.Label(status);
            if (scene != null) GUILayout.Label(scene.Status);
            GUILayout.Label("Manual fixed placement. Inventory refresh does not apply patches. Reload after external JSON correction.");
            filter = GUILayout.TextField(filter);
            scroll = GUILayout.BeginScrollView(scroll);
            if (GUILayout.Button("Refresh inventory and verify stored target identities")) Inspect();
            if (scene != null) {
                foreach (var board in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.scene.IsValid() && g.scene.isLoaded && BoardScene.PathOf(g.transform) == BoardScene.BoardPath && g.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)) {
                    if (GUILayout.Button("Inspect " + board.name + " in " + board.scene.name)) inspected = Identity(board);
                    if (GUILayout.Button("Focus " + board.name)) Focus(board);
                }
            }
            inspected = GUILayout.TextArea(inspected);
            if (GUILayout.Button("Copy inspected identity")) GUIUtility.systemCopyBuffer = inspected;
            foreach (var diagnostic in diagnostics) GUILayout.Label(diagnostic);
            GUILayout.Label("Other object families, track binding, activity, authoring controls and Save/Undo are not implemented in this slice.");
            if (GUILayout.Button("Fixed-placement guidance")) Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/authoring-fixed-placement.md");
            if (GUILayout.Button("Document v1 reference")) Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/scene-patch-document-v1.md");
            GUILayout.EndScrollView(); GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
        void Inspect() {
            try {
                var lines = new List<string>();
                foreach (var pack in PackDocuments.Discover()) {
                    if (pack.Error != null) { lines.Add(pack.Error); continue; }
                    var document = PatchDocument.Load(pack.Json);
                    if (!document.CanWrite) { lines.Add(document.Error); continue; }
                    var root = JObject.Parse(document.Write());
                    if (!scene.MapIds.Contains((string)root["sourceMap"]["mapId"])) { lines.Add(pack.Identifier + ": map context mismatch before target resolution"); continue; }
                    foreach (var item in (JArray)root["modifications"]) {
                        try {
                            var identity = StoredIdentity((JObject)item["target"]);
                            var matches = scene.Resolve(identity);
                            string support = "";
                            if (matches.Count == 1) { try { scene.Preflight(matches[0]); support = "; prerequisites available"; } catch (Exception e) { support = "; " + e.GetBaseException().Message; } }
                            lines.Add(pack.Identifier + "/" + item["id"] + ": " + matches.Count + " exact matches" + support);
                        } catch (Exception e) { lines.Add(pack.Identifier + "/" + item["id"] + ": " + e.GetBaseException().Message); }
                    }
                }
                inspected = String.Join("\n", lines);
                status = "Refreshed from current scene and documents. Inspect again after native changes; inventory is a snapshot.";
            } catch (Exception e) { EditorDiagnostic(e.GetBaseException().Message); }
        }
        static TargetIdentity StoredIdentity(JObject value) => new TargetIdentity((string)value["scene"], (string)value["path"], (string)value["component"], (string)value["structure"]);
        string Identity(GameObject board) {
            var position = scene.ConvertPosition("WorldToGame", board.transform.position); var rotation = board.transform.rotation;
            return new JObject {
                ["sourceMap"] = new JObject { ["mapId"] = scene.MapIds.First() },
                ["target"] = new JObject { ["scene"] = board.scene.name, ["path"] = BoardScene.PathOf(board.transform), ["component"] = BoardScene.CullerType, ["structure"] = BoardScene.Structure },
                ["placement"] = new JObject { ["mode"] = "fixed", ["position"] = new JArray(position.x, position.y, position.z), ["rotation"] = new JArray(rotation.x, rotation.y, rotation.z, rotation.w) }
            }.ToString();
        }
        void Focus(GameObject board) {
            try {
                var type = Native.Type("CameraSelector");
                var selector = Native.Read(type, "shared");
                var strategy = Enum.Parse(type.GetNestedType("CameraIdentifier", Native.Flags), "Strategy");
                Native.Call(selector, "JumpToPoint", scene.ConvertPosition("WorldToGame", board.transform.position), Quaternion.identity, strategy);
            } catch (Exception e) { EditorDiagnostic("Camera focus unavailable: " + e.GetBaseException().Message); }
        }
    }
}
