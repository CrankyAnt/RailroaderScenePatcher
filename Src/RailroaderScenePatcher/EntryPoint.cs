// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Railroader.Modding;
using RailroaderScenePatcher.Core;
using UI.Builder;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    public sealed class EntryPoint : IModEntryPoint, IWorldLifecycle {
        GameObject host;
        IModContext context;
        public void OnModLoad(IModContext value) { context = value; Hooks.Install(); }
        public void OnWorldLoad() {
            if (host != null) { host.GetComponent<PatcherBehaviour>().ApplyDocuments(); return; }
            host = new GameObject("CrankyAnt Scene Patcher");
            host.AddComponent<PatcherBehaviour>().Initialize(context);
        }
        public void OnWorldUnload() { Cleanup(); }
        public void OnModUnload() { Cleanup(); Hooks.Remove(); }
        void Cleanup() {
            if (host == null) return;
            host.GetComponent<PatcherBehaviour>().Release();
            UnityEngine.Object.Destroy(host); host = null;
        }
    }
    public sealed class PatcherBehaviour : MonoBehaviour {
        IModContext context;
        BoardScene world;
        IndependentBoards scene;
        NativeInventory native;
        SceneCapabilities capabilities;
        PatchEngine engine;
        SceneInventory inventory;
        InspectionInputBlocker inputBlocker;
        SpatialSelection selection;
        GizmoController gizmo;
        SceneObservation selected;
        string selectionProblem;
        string documentInspection = "";
        string documentStamp;
        bool definitionsStale = true;
        float nextInventoryCheck;
        bool editor;
        string status = "Initializing", inspected = "", filter = "";
        readonly HashSet<string> reported = new HashSet<string>();
        readonly Dictionary<object, HashSet<string>> contributions = new Dictionary<object, HashSet<string>>();
        readonly List<string> diagnostics = new List<string>();
        NativeEditorPresentation presentation;
        Texture2D worldLabelBacking;
        object attachedEditor;
        string presentationSnapshot;
        readonly InventoryPresentationState inventoryPresentation = new InventoryPresentationState();
        readonly UIOutlineViewState inventoryOutline = new UIOutlineViewState();
        internal const string Version = "0.3.0";
        Authoring authoring;
        object refusedEditor;
        string authoringStatus = "", editResult = "";
        readonly string[] pose = { "", "", "", "", "", "" };
        readonly string loadId = Guid.NewGuid().ToString("N");
        internal void Initialize(IModContext value) {
            context = value;
#if ISSUE39_OBSERVATION
            JourneyObservation.Sink = message => Debug.Log("[Issue39Attempt] " + message);
#endif
            try {
                editor = PackDocuments.IsEditor;
                try {
                    world = new BoardScene();
                    scene = new IndependentBoards(world, () => native.Discover());
                } catch (Exception e) {
                    world?.Dispose(); world = null;
                    status = "Independent board capability incompatible or unavailable: " + e.GetBaseException().Message;
                    Report("capability", status);
                }
                native = new NativeInventory(scene == null ? (Func<GameObject, string>)null : g => scene.Refusal(g));
                if (editor) {
                    inventory = new SceneInventory(native);
                    selection = new SpatialSelection(native);
                }
                if (editor) {
                    try { inputBlocker = gameObject.AddComponent<InspectionInputBlocker>(); inputBlocker.Initialize(); inputBlocker.RefreshRegions = SyncInputRegions; }
                    catch (Exception e) {
                        inputBlocker?.Release(); inputBlocker = null;
                        Report("inspection-input", "Inspection UI unavailable: " + e.GetBaseException().Message);
                    }
                }
                // Each capability checks its own game functions; an incompatible one fails only the modifications naming its structure.
                capabilities = new SceneCapabilities(PackDocuments.MapIds, native);
                if (scene != null) capabilities.Add(scene);
                else capabilities.AddUnavailable(IndependentBoards.Structure, status);
                try { capabilities.Add(new BridgeScene(new BridgeWorld(), Capability.Unchecked)); }
                catch (Exception e) {
                    capabilities.AddUnavailable(BridgeScene.Structure, e.GetBaseException().Message);
                    context.Log.Info("Scene Patcher " + Version + ": bridge capability incompatible or unavailable: " + e.GetBaseException().Message);
                }
                engine = new PatchEngine(capabilities);
                context.Log.Info("Scene Patcher " + Version + ": native capabilities checked; game " + Application.version + "; production integration unchecked");
                ApplyDocuments();
            } catch (Exception e) {
                status = "Scene capabilities unavailable: " + e.GetBaseException().Message;
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
                            else Report(pack.Identifier + "/" + item.Key, error + (item.Value.Error.StartsWith("Mutation failed", StringComparison.Ordinal) || item.Value.Error.Contains("incompatible with this game build") ? ". Report to the patcher maintainer; affected state may need reload." : ". Report this supplied definition to its MapMod author."));
                        }
                    }
                    try { ObserveOverlap(pack, result); }
                    catch (Exception e) { Report(pack.Identifier + "/overlap", "Overlap inspection failed; remaining documents will still be processed. Report to the patcher maintainer. " + e.GetBaseException().Message); }
                    status = "Documents processed. " + (scene?.Status ?? "");
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
        static string Describe(PackDocument pack, string message) => (pack.Name ?? pack.Identifier) + " [" + pack.Identifier + " " + pack.Version + "] " + message + ". Patcher " + Version + ". Author: " + (pack.Author ?? "not supplied") + (String.IsNullOrWhiteSpace(pack.Website) ? "" : "; website: " + pack.Website);
        void EditorDiagnostic(string message) { if (editor && !diagnostics.Contains(message)) diagnostics.Add(message); }
        void Report(string key, string message) {
            if (!reported.Add(key)) return;
            diagnostics.Add(message); status = message;
            context?.Log.Info(message);
            try { Native.Call(Native.Type("Game.Diagnostics.MessageLog"), "Warning", "Railroader Scene Patcher", message, "Scene patches", loadId + key); }
            catch (Exception e) { context?.Log.Info("Native warning presentation unavailable: " + e.GetBaseException().Message); }
        }
        // The map editor appears after world load and is replaced by an editor reload.
        void Attach() {
            if (!editor || engine == null) return;
            var controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared");
            var found = controller as UnityEngine.Object == null ? null : Native.Read(controller, "MapEditor");
            if (ReferenceEquals(found, attachedEditor)) return;
            presentation?.Dispose(); presentation = null;
            selection?.Cancel(); selectionProblem = null;
            Detach(); attachedEditor = found; refusedEditor = null; presentationSnapshot = null;
            if (found == null) return;
            if (inputBlocker != null && inputBlocker.Available) {
                try { presentation = new NativeEditorPresentation(DrawWindow, () => { CancelGizmo(); selection?.Cancel(); selectionProblem = null; }); }
                catch (Exception e) { Report("inspection-presentation", "Inspection UI unavailable: " + e.GetBaseException().Message); }
            }
            if (Hooks.Error != null) { refusedEditor = found; authoringStatus = Hooks.Error; return; }
            try {
                authoring = new Authoring(engine, found); Hooks.Current = authoring;
                authoring.Log = message => context?.Log.Info(message);
                authoringStatus = authoring.Session.Blocked ?? "Authoring ready for " + authoring.PatchPath;
                // The editor built its title before this session could account for the document.
                authoring.RefreshChrome();
            } catch (Exception e) { refusedEditor = found; authoringStatus = "Authoring unavailable: " + e.GetBaseException().Message; }
        }
        void Detach() {
            CancelGizmo();
            if (authoring == null) return;
            if (ReferenceEquals(Hooks.Current, authoring)) Hooks.Current = null;
            authoring.Dispose(); authoring = null;
        }
        void SyncInputRegions() {
            if (inputBlocker == null) return;
            var open = presentation != null && presentation.IsOpen;
            inputBlocker.SetRegions(open, open ? presentation.PanelBounds : Rect.zero, presentation == null ? Rect.zero : presentation.EntryBounds);
            inputBlocker.WorldPicking = open && selection?.Pending != null;
        }
        void Update() {
            if (engine == null) return;
            try { Attach(); }
            catch (Exception e) { authoringStatus = "Authoring unavailable: " + e.GetBaseException().Message; }
            bool open = presentation != null && presentation.IsOpen;
            if (!open) CancelGizmo();
            SyncInputRegions();
            if (open && selection?.Pending != null && inputBlocker?.WorldClick is Vector2 pointer) {
                try { selection.Pick(pointer, presentation.WorldUiBounds()); selectionProblem = null; }
                catch (Exception e) {
                    selection.Pending.Offer(Array.Empty<SelectionCandidate>());
                    selectionProblem = "World selection unavailable: " + e.GetBaseException().Message;
                }
            }
            if (editor && open && inventory != null && Time.unscaledTime >= nextInventoryCheck) {
                inventory.CheckFreshness(); nextInventoryCheck = Time.unscaledTime + 2f;
                try { if (documentStamp != DocumentStamp(PackDocuments.Discover())) definitionsStale = true; }
                catch { definitionsStale = true; }
            }
            if (scene != null) try {
                scene.Poll();
                foreach (var failure in scene.Failures)
                    Report("terrain/" + world.Label(failure.Key) + "/" + failure.Value.Error, "Terrain output for " + world.Label(failure.Key) + " did not complete: " + failure.Value.Error + ". Completed: " + Join(failure.Value.Completed) + ". Outstanding: " + Join(failure.Value.Outstanding) + ". The board keeps its applied placement; reloading the map regenerates the output. Report to the patcher maintainer.");
            } catch (Exception e) { Report("terrain", "Dependent terrain state remains unverified. Reload before retry. Report to the patcher maintainer. " + e.GetBaseException().Message); }
            if (presentation != null) {
                var snapshot = PresentationSnapshot();
                var inventoryChanged = inventoryPresentation.Observe(inventory, selected, filter);
                if (snapshot != presentationSnapshot || inventoryChanged) { presentationSnapshot = snapshot; presentation.RequestRefresh(); }
                presentation.Tick(); SyncInputRegions();
            }
        }
        string PresentationSnapshot() {
            var pending = selection?.Pending;
            string outstanding = "";
            try {
                if (authoring != null) {
                    var session = authoring.Session;
                    outstanding = String.Join(", ", session.Outstanding(authoring.NativeDirty)) + "|" +
                        session.LastSave?.Error + "|" + (session.LastSave == null ? "" : String.Join(", ", session.LastSave.Completed)) + "|" +
                        (session.LastSave == null ? "" : String.Join(", ", session.LastSave.Outstanding)) + "|" +
                        session.IncompleteOperation + "|" + authoring.Fault;
                }
            } catch (Exception e) { outstanding = e.GetBaseException().Message; }
            return String.Join("|", status, scene?.Status, inventory?.IsStale, definitionsStale, authoringStatus, editResult,
                documentInspection, String.Join("\n", diagnostics), selectionProblem, inputBlocker?.WorldInputProblem,
                pending?.Purpose, pending?.Source?.Label, pending?.Problem, pending?.Candidate?.Label,
                pending == null ? "" : String.Join("\n", pending.Offers.Select(o => o.Label)),
                selection?.Confirmed?.Identity, gizmo?.State, outstanding, TerrainSnapshot());
        }
        string TerrainSnapshot() {
            if (authoring == null || selected == null || !Editable) return "";
            var output = OutputOf(authoring.Session, selected.Identity, out var problem);
            return problem ?? String.Join("|", String.Join(";", output.Completed), String.Join(";", output.Outstanding), output.Error);
        }
        internal void Release() { ReleaseParts(); }
        void ReleaseParts() {
            if (worldLabelBacking != null) Destroy(worldLabelBacking); worldLabelBacking = null;
            presentation?.Dispose(); presentation = null; attachedEditor = null;
            Detach(); inputBlocker?.Release(); inputBlocker = null; selection?.Release(); selection = null;
            scene?.Dispose(); scene = null; world?.Dispose(); world = null; capabilities = null; engine = null; inventory = null; native = null; selected = null;
        }
        static string Join(IReadOnlyList<string> items) => items.Count == 0 ? "none" : String.Join("; ", items);
        void OnDestroy() { Release(); }
        void OnGUI() {
            if (presentation == null || !presentation.IsOpen || selection == null || Event.current.type != EventType.Repaint) return;
            try {
                if (worldLabelBacking == null) {
                    worldLabelBacking = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    worldLabelBacking.SetPixel(0, 0, new Color(.11f, .12f, .14f, 1)); worldLabelBacking.Apply();
                }
                selection.Draw(selected, presentation.WorldUiBounds(), worldLabelBacking);
            }
            catch (Exception e) { selectionProblem = "World markings unavailable: " + e.GetBaseException().Message; }
        }
        IConfigurableElement Button(UIPanelBuilder builder, string label, Action action) {
            var button = builder.AddButtonCompact(label, presentation.Action(action));
            foreach (var text in button.RectTransform.GetComponentsInChildren<TMP_Text>(true)) text.richText = false;
            return button;
        }
        void PlainLabel(UIPanelBuilder builder, string value) => builder.AddLabel(value, text => text.richText = false);
        void DrawWindow(UIPanelBuilder builder) {
            PlainLabel(builder, status);
            DrawSelection(builder);
            if (scene != null) PlainLabel(builder, scene.Status);
            builder.AddSection("Inventory");
            PlainLabel(builder, inventory == null || inventory.IsStale || definitionsStale ? "Inventory is stale or not yet scanned. Refresh before relying on these observations." : "Inventory snapshot. Changes are checked while this panel is open. Refresh after native edits.");
            PlainLabel(builder, "Search names, paths, families, binding identifiers, dependencies or problems.");
            UIPanel results = null;
            builder.AddFilterField(filter, value => { filter = value; results?.SetNeedsRebuild(); }, null, true);
            Button(builder, "Refresh inventory and verify stored target identities", Inspect);
            if (inventory != null) {
                PlainLabel(builder, inventory.Entries.Count + " observed entries. Active and inactive loaded objects are included.");
                results = builder.AddRebuildableSection(items => {
                        var indices = inventory.Entries.Select((entry, index) => new { entry, index })
                            .ToDictionary(pair => pair.entry, pair => pair.index.ToString(CultureInfo.InvariantCulture));
                        var rows = new List<UIOutlineItem>();
                        string selectedKey = null;
                        foreach (var item in inventory.Search(filter)) {
                            var key = indices[item];
                            if (ReferenceEquals(selected, item)) selectedKey = key;
                            var targetIdentity = new JObject {
                                ["scene"] = item.Identity.Scene, ["path"] = item.Identity.Path,
                                ["component"] = item.Identity.Component, ["structure"] = item.Identity.Structure
                            };
                            var label = "Inspect " + item.Family + ": " + item.Identity.Path + (item.Active ? "" : " [inactive]");
                            rows.Add(new UIOutlineItem(key, label.Replace("<", "<noparse><</noparse>"), item) {
                                Tooltip = targetIdentity.ToString().Replace("<", "\\u003c").Replace(">", "\\u003e")
                            });
                        }
                        var byKey = rows.ToDictionary(row => row.Identifier);
                        var outline = items.AddOutlineView(rows, inventoryOutline, new UIState<string>(selectedKey), key => {
                            if (!byKey.TryGetValue(key, out var row)) return;
                            presentation.Action(() => {
                                var observation = (SceneObservation)row.Value;
                                if (!inventoryPresentation.CanSelect(inventory, observation)) return;
                                CancelGizmo(); selected = inventoryPresentation.CurrentSelection(inventory, observation); inspected = selected == null ? "Inspected object no longer present" : DescribeInspection(selected); ReadPose();
                            })();
                        });
                        var listLayout = outline.GetComponent<LayoutElement>();
                        listLayout.minHeight = 180; listLayout.preferredHeight = 180; listLayout.flexibleHeight = 0;
                });
            }
            if (selected != null) {
                PlainLabel(builder, inspected);
                Button(builder, "Focus inspected object", () => Focus(selected));
                Button(builder, "Copy inspected identity", () => GUIUtility.systemCopyBuffer = Identity(selected));
            }
            PlainLabel(builder, documentInspection);
            foreach (var diagnostic in diagnostics) PlainLabel(builder, diagnostic);
            DrawAuthoring(builder);
            builder.AddSection("Guidance");
            foreach (var coverage in NativeInventory.Coverage) PlainLabel(builder, coverage);
            PlainLabel(builder, "Binding validity does not establish correct placement or intended track. Inspect the world before editing.");
            Button(builder, "Inventory and dependency guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/inspect-scene-objects.md"));
            Button(builder, "World selection guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/select-world-targets.md"));
            PlainLabel(builder, "Track binding edits and snapping are not implemented.");
            Button(builder, "Transform gizmo guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/using-transform-gizmos.md"));
            Button(builder, "Fixed-placement guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/authoring-fixed-placement.md"));
            Button(builder, "Board and terrain guidance", () => Application.OpenURL(BoardGuide));
            Button(builder, "Document v1 reference", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/scene-patch-document-v1.md"));
        }
        const string BoardGuide = "https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/moving-boards.md";
        void DrawSelection(UIPanelBuilder builder) {
            if (selection == null) return;
            builder.AddSection("World selection");
            var pending = selection.Pending;
            var confirmed = selection.Confirmed;
            if (pending == null) {
                Button(builder, "Select scene object in world", () => BeginSelection(SelectionKind.SceneObject, null));
                if (selected != null) {
                    Button(builder, "Choose replacement object", () => BeginSelection(SelectionKind.SceneObject, selected));
                    Button(builder, "Choose replacement segment", () => BeginSelection(SelectionKind.TrackSegment, selected));
                    Button(builder, "Choose replacement node", () => BeginSelection(SelectionKind.TrackNode, selected));
                }
            } else {
                var candidate = selection.Find(pending.Candidate);
                PlainLabel(builder, pending.Purpose + ". Click a marking or track in the world.");
                if (pending.Source != null) PlainLabel(builder, "Source: " + pending.Source.Label);
                if (pending.Problem != null) PlainLabel(builder, pending.Problem);
                if (pending.Offers.Count > 1) {
                    foreach (var offer in pending.Offers) {
                        if (ReferenceEquals(offer, pending.Candidate)) continue;
                        var choice = offer;
                        Button(builder, "Choose " + choice.Label, () => pending.Choose(choice));
                    }
                }
                if (candidate != null) {
                    PlainLabel(builder, "Candidate: " + candidate.Choice.Label);
                    PlainLabel(builder, candidate.Choice.Identity.ToString());
                    PlainLabel(builder, candidate.Details);
                    Button(builder, "Focus candidate", () => {
                        try { JumpTo(candidate.Position); }
                        catch (Exception e) { selectionProblem = "Camera focus unavailable: " + e.GetBaseException().Message; }
                    });
                    if (candidate.Observation != null) Button(builder, "Inspect candidate", () => {
                        CancelGizmo(); selected = candidate.Observation; inspected = DescribeInspection(selected); Inspect(); ReadPose();
                    });
                    Button(builder, "Confirm identity", ConfirmSelection);
                }
                Button(builder, "Cancel selection", () => { selection.Cancel(); selectionProblem = null; });
            }
            if (confirmed != null) PlainLabel(builder, "Confirmed: " + confirmed.Identity + " in " + confirmed.MapIds[0]);
            if (inputBlocker.WorldInputProblem != null && pending != null) PlainLabel(builder, inputBlocker.WorldInputProblem);
            if (selectionProblem != null) PlainLabel(builder, selectionProblem);
        }
        void BeginSelection(SelectionKind kind, SceneObservation origin) {
            CancelGizmo();
            try { selection.Begin(kind, origin); selectionProblem = null; }
            catch (Exception e) { selectionProblem = "World selection unavailable: " + e.GetBaseException().Message; }
        }

        void ConfirmSelection() {
            try {
                var candidate = selection.Confirm();
                if (candidate == null) return;
                selectionProblem = null;
                if (candidate.Observation != null) {
                    selected = candidate.Observation;
                    inspected = DescribeInspection(selected);
                    Inspect();
                    ReadPose();
                }
            } catch (Exception e) {
                selection.Pending?.Offer(Array.Empty<SelectionCandidate>());
                selectionProblem = "Identity could not be confirmed: " + e.GetBaseException().Message;
            }
        }
        // Editing needs a mutation adapter; the inventory marks each supported pattern with its structure.
        bool Editable => selected != null && (selected.Identity.Structure == IndependentBoards.Structure || selected.Identity.Structure == BridgeScene.Structure) && selected.Handle is GameObject;
        void ReadPose() {
            if (selected == null || selected.Position.Count != 3 || selected.Rotation.Count != 4) return;
            var angles = new Quaternion((float)selected.Rotation[0], (float)selected.Rotation[1], (float)selected.Rotation[2], (float)selected.Rotation[3]).eulerAngles;
            var values = new[] { selected.Position[0], selected.Position[1], selected.Position[2], angles.x, angles.y, angles.z };
            for (var i = 0; i < 6; i++) pose[i] = ((float)values[i]).ToString("R", CultureInfo.InvariantCulture);
        }
        // Every edit is bound to the inspected observation. A stale inventory refuses the edit instead of redirecting it.
        void Edit(string action, Func<AuthoringSession, InspectedTarget, Outcome> change) {
            if (selection?.Pending != null || gizmo?.Active == true) return;
            Outcome outcome;
            try { outcome = change(authoring.Session, inventory.Pin(selected)); }
            catch (Exception e) { editResult = action + " refused: " + e.GetBaseException().Message; return; }
            editResult = outcome.Applied ? action + " completed. Undo and Redo use the editor's history; Save uses the editor's Save button." : action + " refused: " + outcome.Error;
            // A completed edit changed the observed values; refresh so the next edit binds to the new observation.
            if (outcome.Applied) { Inspect(); ReadPose(); }
            try { context?.Log.Info(editResult + " Outstanding: " + authoring.Outstanding()); } catch { }
        }
        void DrawAuthoring(UIPanelBuilder builder) {
            builder.AddSection("Authoring");
            PlainLabel(builder, authoringStatus);
            if (authoring == null) return;
            var session = authoring.Session;
            try {
                var outstanding = session.Outstanding(authoring.NativeDirty);
                PlainLabel(builder, outstanding.Count == 0 ? "All required writes are saved." : "Unsaved: " + String.Join(", ", outstanding) + ". Use the editor's Save button.");
            } catch (Exception e) { PlainLabel(builder, "Outstanding work unknown: " + e.GetBaseException().Message); }
            if (session.LastSave != null && session.LastSave.Error != null) PlainLabel(builder, "Last Save failed: " + session.LastSave.Error + " Completed: " + (session.LastSave.Completed.Count == 0 ? "none" : String.Join(", ", session.LastSave.Completed)) + ". Outstanding: " + String.Join(", ", session.LastSave.Outstanding) + ". Edits remain in memory; correct the cause and Save again.");
            if (session.IncompleteOperation != null) PlainLabel(builder, "Incomplete operation: " + session.IncompleteOperation);
            if (authoring.Fault != null) PlainLabel(builder, authoring.Fault);
            if (selection?.Pending != null) {
                PlainLabel(builder, "Confirm or cancel the world selection to edit placement or activity.");
                return;
            }
            if (!Editable) { PlainLabel(builder, "Inspect an object with a fixed-placement adapter to edit it. Other observations are inspection only."); return; }
            if (gizmo?.Active == true) {
                PlainLabel(builder, gizmo.State);
                Button(builder, "Cancel gizmo", CancelGizmo);
                return;
            }
            var target = (GameObject)selected.Handle;
            if (target == null) { PlainLabel(builder, "The inspected object no longer exists. Refresh the inventory."); return; }
            if (selected.Identity.Structure == BridgeScene.Structure) {
                PlainLabel(builder, "Bridge placement moves the bridge root with its colliders and culling. Generated track trestles, track geometry and construction scenery stay where they are; this is not a track binding.");
                Button(builder, "Bridge placement guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/placing-bridges.md"));
            }
            PlainLabel(builder, "Selected: " + selected.Identity.Path + (target.activeSelf ? "" : " (disabled)") + ". Position in map coordinates; rotation in degrees. After Undo, Redo or a native edit, refresh the inventory before the next edit.");
            Button(builder, "Move with gizmo", () => BeginGizmo(UI.Gizmos.TransformGizmoMode.Move));
            Button(builder, "Rotate with gizmo", () => BeginGizmo(UI.Gizmos.TransformGizmoMode.Rotate));
            var labels = new[] { "X", "Y", "Z", "Rot X", "Rot Y", "Rot Z" };
            for (var row = 0; row < 2; row++) {
                var first = row * 3;
                builder.HStack(fields => {
                    for (var column = 0; column < 3; column++) {
                        var index = first + column;
                        fields.VStack(field => {
                            field.AddLabel(labels[index]);
                            var input = field.AddInputField(pose[index], value => pose[index] = value);
                            input.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => pose[index] = value);
                            var layout = input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
                            layout.minWidth = 70; layout.flexibleWidth = 1;
                        }).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                    }
                });
            }
            var identity = selected.Identity;
            Button(builder, "Apply placement", () => {
                var values = new float[6]; var valid = true;
                for (var i = 0; i < 6; i++) valid &= Single.TryParse(pose[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
                if (!valid) editResult = "Placement refused: enter six numbers with a decimal point";
                else {
                    var rotation = Quaternion.Euler(values[3], values[4], values[5]);
                    var placement = new Placement(new double[] { values[0], values[1], values[2] }, new double[] { rotation.x, rotation.y, rotation.z, rotation.w });
                    Edit("Placement", (current, inspection) => current.Edit(identity, placement, null, inspection));
                }
            });
            Button(builder, "Read inspected values", ReadPose);
            Button(builder, target.activeSelf ? "Disable object" : "Enable object", () => { var enable = !target.activeSelf; Edit("Activity change", (current, inspection) => current.Edit(identity, null, enable, inspection)); });
            Button(builder, "Remove modification", () => Edit("Removal", (current, inspection) => current.Remove(identity, inspection)));
            Button(builder, "Recheck after failed edit", () => Edit("Recheck", (current, inspection) => current.Recheck(identity, inspection)));
            Button(builder, "Authoring guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/editing-placement-in-the-editor.md"));
            PlainLabel(builder, editResult);
            DrawOutput(builder, session, identity);
            PlainLabel(builder, "Removal returns the object to its state without this MapMod's modification. Save writes map data, Data/scene-patches.json and the patcher dependency in Info.json, and never reloads. Reloading the editor ends Undo/Redo history.");
        }
        // Terrain output follows a board edit asynchronously; the game regenerates it, and Save does not write it.
        void DrawOutput(UIPanelBuilder builder, AuthoringSession session, TargetIdentity identity) {
            var output = OutputOf(session, identity, out var problem);
            if (problem != null) { PlainLabel(builder, "Terrain output unknown: " + problem); return; }
            if (output.Completed.Count == 0 && output.Settled) return;
            PlainLabel(builder, output.Error != null ? "Terrain output stopped: " + output.Error + ". Completed: " + Join(output.Completed) + ". Outstanding: " + Join(output.Outstanding) + ". The board edit is kept."
                : output.Settled ? "Terrain output complete: " + Join(output.Completed) + "."
                : "Terrain output outstanding; the game is regenerating it: " + Join(output.Outstanding) + ". Completed: " + Join(output.Completed) + ".");
            PlainLabel(builder, "Save does not write the terrain output generated from the board's mask: it writes the board's placement, and the game regenerates that output from it on every load. Native edited terrain tiles are saved separately and are not changed by this edit.");
            Button(builder, "Retry terrain output", () => {
                var outcome = session.RetryOutput(identity);
                editResult = outcome.Applied ? "Terrain output requested again." : "Terrain output retry refused: " + outcome.Error;
            }).Disable(output.Outstanding.Count == 0);
            Button(builder, "Board and terrain guidance", () => Application.OpenURL(BoardGuide));
        }
        static DependentOutput OutputOf(AuthoringSession session, TargetIdentity identity, out string problem) {
            problem = null;
            try { return session.Output(identity); }
            catch (Exception e) { problem = e.GetBaseException().Message; return DependentOutput.None; }
        }
        void CancelGizmo() {
            var pending = gizmo; gizmo = null;
            if (pending != null) pending.Cancel();
        }
        void BeginGizmo(UI.Gizmos.TransformGizmoMode mode) {
            CancelGizmo();
            try {
                if (!Editable || selection?.Pending != null || authoring == null) throw new InvalidOperationException("Inspect a supported target and finish world selection first");
                var inspection = inventory.Pin(selected);
                var root = new GameObject("CrankyAnt Scene Patcher transform gizmo");
                root.transform.SetParent(transform, false);
                gizmo = root.AddComponent<GizmoController>();
                gizmo.Initialize(engine, capabilities, authoring, selected.Identity, inspection, mode, (outcome, message) => {
                    gizmo = null;
                    editResult = message ?? (outcome?.Applied == true ? "Gizmo placement completed. Use native Undo, Redo and Save." : "Gizmo placement refused: " + outcome?.Error);
                    Inspect(); ReadPose(); authoring?.RefreshChrome();
                    context?.Log.Info(editResult);
                });
            } catch (Exception e) { gizmo = null; editResult = "Gizmo unavailable: " + e.GetBaseException().Message; }
        }
        void Inspect() {
            try {
                if (inventory == null) throw new InvalidOperationException("Map-editor inspection is unavailable");
                inventory.Refresh();
                if (selected != null) {
                    selected = inventoryPresentation.CurrentSelection(inventory, selected);
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
                var current = native.Discover().Where(e => ReferenceEquals(e.Handle, entry.Handle) && e.Identity.Component == entry.Identity.Component).ToArray();
                if (current.Length != 1 || !entry.Matches(current[0])) throw new InvalidOperationException("Inspected object changed; refresh and inspect again");
                if (entry.Position.Count != 3) throw new InvalidOperationException("Map coordinates unavailable for this observation");
                var position = new Vector3((float)entry.Position[0], (float)entry.Position[1], (float)entry.Position[2]);
                JumpTo(position);
            } catch (Exception e) { EditorDiagnostic("Camera focus unavailable: " + e.GetBaseException().Message); }
        }
        static void JumpTo(Vector3 position) {
            var type = Native.Type("CameraSelector");
            var selector = Native.Read(type, "shared");
            var strategy = Enum.Parse(type.GetNestedType("CameraIdentifier", Native.Flags), "Strategy");
            Native.Call(selector, "JumpToPoint", position, Quaternion.identity, strategy);
        }
    }
}
