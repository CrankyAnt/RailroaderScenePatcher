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
        SceneInventory inventory;
        InspectionInputBlocker inputBlocker;
        SceneObservation selected;
        string documentInspection = "";
        string documentStamp;
        bool definitionsStale = true;
        float nextInventoryCheck;
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
                if (editor) inventory = new SceneInventory(new NativeInventory());
                if (editor) {
                    try { inputBlocker = gameObject.AddComponent<InspectionInputBlocker>(); inputBlocker.Initialize(); }
                    catch (Exception e) {
                        inputBlocker?.Release(); inputBlocker = null;
                        Report("inspection-input", "Inspection UI unavailable: " + e.GetBaseException().Message);
                    }
                }
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
            if (editor && open && inventory != null && Time.unscaledTime >= nextInventoryCheck) {
                inventory.CheckFreshness(); nextInventoryCheck = Time.unscaledTime + 2f;
                try { if (documentStamp != DocumentStamp(PackDocuments.Discover())) definitionsStale = true; }
                catch { definitionsStale = true; }
            }
            if (scene == null) return;
            try { scene.PollCompletion(); }
            catch (Exception e) { Report("terrain", "Dependent terrain state remains unverified. Reload before retry. Report to the patcher maintainer. " + e.GetBaseException().Message); }
        }
        internal void Release() { inputBlocker?.Release(); inputBlocker = null; scene?.Dispose(); scene = null; engine = null; inventory = null; selected = null; }
        void OnDestroy() { Release(); }
        void OnGUI() {
            if (!editor || inputBlocker == null || !inputBlocker.Available) return;
            var button = new Rect(20, 20, 160, 30);
            inputBlocker.SetRegions(open, window, button);
            if (GUI.Button(button, "Scene patches")) open = !open;
            if (open) window = GUI.Window(0x43525350, window, DrawWindow, "Railroader Scene Patcher");
            inputBlocker.SetRegions(open, window, button);
        }
        void DrawWindow(int id) {
            GUILayout.Label(status);
            if (scene != null) GUILayout.Label(scene.Status);
            GUILayout.Label(inventory == null || inventory.IsStale || definitionsStale ? "Inventory is stale or not yet scanned. Refresh before relying on these observations." : "Inventory snapshot. Changes are checked while this panel is open. Refresh after native edits.");
            GUILayout.Label("Search names, paths, families, binding identifiers, dependencies or problems.");
            filter = GUILayout.TextField(filter);
            scroll = GUILayout.BeginScrollView(scroll);
            if (GUILayout.Button("Refresh inventory and verify stored target identities")) Inspect();
            if (inventory != null) {
                GUILayout.Label(inventory.Entries.Count + " observed entries. Active and inactive loaded objects are included.");
                foreach (var entry in inventory.Search(filter)) {
                    if (GUILayout.Button("Inspect " + entry.Family + ": " + entry.Identity.Path + (entry.Active ? "" : " [inactive]"))) {
                        selected = entry; inspected = DescribeInspection(entry);
                    }
                }
            }
            if (selected != null) {
                GUILayout.TextArea(inspected);
                if (GUILayout.Button("Focus inspected object")) Focus(selected);
                if (GUILayout.Button("Copy inspected identity")) GUIUtility.systemCopyBuffer = Identity(selected);
            }
            GUILayout.Label(documentInspection);
            foreach (var diagnostic in diagnostics) GUILayout.Label(diagnostic);
            foreach (var coverage in NativeInventory.Coverage) GUILayout.Label(coverage);
            GUILayout.Label("Binding validity does not establish correct placement or intended track. Inspect the world before editing.");
            if (GUILayout.Button("Inventory and dependency guidance")) Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/inspect-scene-objects.md");
            if (GUILayout.Button("Fixed-placement guidance")) Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/authoring-fixed-placement.md");
            if (GUILayout.Button("Document v1 reference")) Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/scene-patch-document-v1.md");
            GUILayout.EndScrollView(); GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
        void Inspect() {
            try {
                if (inventory == null) throw new InvalidOperationException("Map-editor inspection is unavailable");
                inventory.Refresh();
                if (selected != null) {
                    selected = inventory.Entries.FirstOrDefault(e => ReferenceEquals(e.Handle, selected.Handle) && e.Identity.Component == selected.Identity.Component);
                    inspected = selected == null ? "Inspected object no longer present" : DescribeInspection(selected);
                }
                var lines = new List<string>();
                var documents = PackDocuments.Discover().ToArray();
                foreach (var pack in documents) {
                    if (pack.Error != null) { lines.Add(pack.Identifier + ": " + pack.Error); continue; }
                    foreach (var item in inventory.InspectDocument(pack.Json))
                        lines.Add(pack.Identifier + "/" + (item.Id ?? "document") + ": " + item.MatchCount + " inspection matches; dependsOn=" + String.Join(", ", item.Dependencies) + "; " + String.Join("; ", item.Problems));
                }
                documentInspection = String.Join("\n", lines);
                documentStamp = DocumentStamp(documents); definitionsStale = false;
                status = "Refreshed scene observations and supplied definitions without applying patches.";
            } catch (Exception e) { EditorDiagnostic(e.GetBaseException().Message); }
        }
        static string DocumentStamp(IEnumerable<PackDocument> documents) => new JArray(documents.Select(p => new JArray(p.Identifier, p.Json, p.Error))).ToString(Newtonsoft.Json.Formatting.None);
        string Identity(SceneObservation entry) {
            var identity = new JObject {
                ["sourceMap"] = new JObject { ["mapId"] = PackDocuments.MapIds().First() },
                ["target"] = new JObject { ["scene"] = entry.Identity.Scene, ["path"] = entry.Identity.Path, ["component"] = entry.Identity.Component, ["structure"] = entry.Identity.Structure },
                ["placement"] = new JObject { ["mode"] = "fixed", ["position"] = new JArray(entry.Position), ["rotation"] = new JArray(entry.Rotation) }
            };
            if (entry.Identity.Structure == null) ((JObject)identity["target"]).Remove("structure");
            return identity.ToString();
        }
        string DescribeInspection(SceneObservation entry) => Identity(entry) + "\n" + entry.Family + "; active in hierarchy=" + entry.Active + "\n" + entry.Operations +
            "\nBindings:\n" + String.Join("\n", entry.Bindings) + "\nDependencies:\n" + String.Join("\n", entry.Dependencies) +
            "\nDetected problems:\n" + (entry.Problems.Count == 0 ? "None detected. Placement and intended binding require world inspection." : String.Join("\n", entry.Problems));
        void Focus(SceneObservation entry) {
            try {
                // Never focus a replacement with the same path. Refresh retains the selected handle.
                var current = new NativeInventory().Discover().Where(e => ReferenceEquals(e.Handle, entry.Handle) && e.Identity.Component == entry.Identity.Component).ToArray();
                if (current.Length != 1 || !entry.Matches(current[0])) throw new InvalidOperationException("Inspected object changed; refresh and inspect again");
                if (entry.Position.Count != 3) throw new InvalidOperationException("Map coordinates unavailable for this observation");
                var position = new Vector3((float)entry.Position[0], (float)entry.Position[1], (float)entry.Position[2]);
                var type = Native.Type("CameraSelector");
                var selector = Native.Read(type, "shared");
                var strategy = Enum.Parse(type.GetNestedType("CameraIdentifier", Native.Flags), "Strategy");
                Native.Call(selector, "JumpToPoint", position, Quaternion.identity, strategy);
            } catch (Exception e) { EditorDiagnostic("Camera focus unavailable: " + e.GetBaseException().Message); }
        }
    }
}
