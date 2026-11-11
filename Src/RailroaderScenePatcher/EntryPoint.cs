// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Railroader.Modding;
using RailroaderScenePatcher.Core;
using UnityEngine;
using UnityEngine.EventSystems;
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
        BoardScene scene;
        PatchEngine engine;
        SceneInventory inventory;
        InspectionInputBlocker inputBlocker;
        SpatialSelection selection;
        SceneObservation selected;
        string selectionProblem;
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
        Rect toggle;
        float nextTogglePlacement;
        Vector2 toggleScreen;
        readonly List<RaycastResult> chromeHits = new List<RaycastResult>();
        readonly Vector3[] chromeCorners = new Vector3[4];
        bool panelPlaced;
        Texture2D panelBacking, toggleBacking, toggleHover;
        GUIStyle panelStyle, toggleStyle;
        Vector2 scroll;
        Vector2 choiceScroll;
        internal const string Version = "0.2.0";
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
                if (editor) {
                    var inspection = new NativeInventory();
                    inventory = new SceneInventory(inspection);
                    selection = new SpatialSelection(inspection);
                }
                if (editor) {
                    try { inputBlocker = gameObject.AddComponent<InspectionInputBlocker>(); inputBlocker.Initialize(); }
                    catch (Exception e) {
                        inputBlocker?.Release(); inputBlocker = null;
                        Report("inspection-input", "Inspection UI unavailable: " + e.GetBaseException().Message);
                    }
                }
                scene = new BoardScene(); engine = new PatchEngine(scene);
                context.Log.Info("Scene Patcher " + Version + ": native board capability available; game " + Application.version + "; production integration unchecked");
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
            if (found == null || ReferenceEquals(found, refusedEditor) || (authoring != null && ReferenceEquals(authoring.Editor, found))) return;
            Detach();
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
            if (authoring == null) return;
            if (ReferenceEquals(Hooks.Current, authoring)) Hooks.Current = null;
            authoring.Dispose(); authoring = null;
        }
        void Update() {
            if (open && selection?.Pending != null && inputBlocker?.WorldClick is Vector2 pointer) {
                try { selection.Pick(pointer); selectionProblem = null; }
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
            if (scene == null) return;
            try { Attach(); }
            catch (Exception e) { authoringStatus = "Authoring unavailable: " + e.GetBaseException().Message; }
            try { scene.PollCompletion(); }
            catch (Exception e) { Report("terrain", "Dependent terrain state remains unverified. Reload before retry. Report to the patcher maintainer. " + e.GetBaseException().Message); }
        }
        internal void Release() {
            foreach (var texture in new[] { panelBacking, toggleBacking, toggleHover }) if (texture != null) Destroy(texture);
            panelBacking = toggleBacking = toggleHover = null; panelStyle = toggleStyle = null;
            ReleaseParts();
        }
        void ReleaseParts() { Detach(); inputBlocker?.Release(); inputBlocker = null; selection?.Cancel(); selection = null; scene?.Dispose(); scene = null; engine = null; inventory = null; selected = null; }
        void OnDestroy() { Release(); }
        void OnGUI() {
            if (!editor || inputBlocker == null || !inputBlocker.Available) return;
            Styles();
            var button = ToggleRect();
            inputBlocker.SetRegions(open, window, button);
            if (open && selection != null) {
                try { selection.Draw(selected); }
                catch (Exception e) { selectionProblem = "World markings unavailable: " + e.GetBaseException().Message; }
            }
            if (GUI.Button(button, open ? "Close Scene patches" : "Scene patches", toggleStyle)) {
                open = !open;
                if (open && !panelPlaced) { panelPlaced = true; PlacePanel(button); }
            }
            if (open) window = GUI.Window(0x43525350, window, DrawWindow, "Railroader Scene Patcher", panelStyle);
            // The pointer regions take the bounds that were drawn in this event, also while the panel is dragged.
            inputBlocker.SetRegions(open, window, button);
            inputBlocker.WorldPicking = open && selection?.Pending != null;
        }
        static Texture2D Solid(Color color) {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color); texture.Apply();
            return texture;
        }
        // The default IMGUI skin is translucent over the world. The toggle and panel get an opaque backing and light text.
        // These colours are provisional for readability; they are not taken from the native editor's assets.
        void Styles() {
            if (panelStyle != null && panelBacking != null) return;
            panelBacking = Solid(new Color(0.11f, 0.12f, 0.14f, 1f));
            toggleBacking = Solid(new Color(0.16f, 0.32f, 0.52f, 1f));
            toggleHover = Solid(new Color(0.22f, 0.42f, 0.66f, 1f));
            panelStyle = new GUIStyle(GUI.skin.window) { border = new RectOffset(0, 0, 0, 0) };
            foreach (var state in new[] { panelStyle.normal, panelStyle.onNormal, panelStyle.focused, panelStyle.onFocused, panelStyle.hover, panelStyle.onHover, panelStyle.active, panelStyle.onActive }) { state.background = panelBacking; state.textColor = Color.white; }
            toggleStyle = new GUIStyle(GUI.skin.button) { border = new RectOffset(0, 0, 0, 0) };
            foreach (var state in new[] { toggleStyle.normal, toggleStyle.onNormal, toggleStyle.focused, toggleStyle.onFocused }) { state.background = toggleBacking; state.textColor = Color.white; }
            foreach (var state in new[] { toggleStyle.hover, toggleStyle.onHover, toggleStyle.active, toggleStyle.onActive }) { state.background = toggleHover; state.textColor = Color.white; }
        }
        // The native Map Editor window in IMGUI screen coordinates (top-left origin), or null when it is not shown.
        static Rect? NativeEditorWindow() {
            try {
                var controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared");
                var editorWindow = controller as UnityEngine.Object == null ? null : Native.Read(controller, "_window");
                var native = editorWindow as UnityEngine.Object == null ? null : Native.Read(editorWindow, "Window") as Component;
                if (native == null || !native.gameObject.activeInHierarchy || !(native.transform is RectTransform bounds)) return null;
                // Native windows live on a screen-space overlay canvas: world corners are bottom-left screen pixels.
                var corners = new Vector3[4]; bounds.GetWorldCorners(corners);
                var rect = Rect.MinMaxRect(corners[0].x, Screen.height - corners[2].y, corners[2].x, Screen.height - corners[0].y);
                return rect.width > 0 && rect.height > 0 ? rect : (Rect?)null;
            } catch { return null; }
        }
        // Mod-owned windows stack on the right side of the viewport, leaving the centre to Focus and world objects.
        // The toggle takes the first slot on the right edge, from the top, that no native chrome occupies:
        // neither the native Map Editor window nor any native UI graphic that takes pointer input, such as
        // the status and icon strip. The search repeats twice a second, so the toggle follows moved chrome.
        Rect ToggleRect() {
            const float width = 180, height = 30, gap = 8, margin = 20;
            var size = new Vector2(Screen.width, Screen.height);
            if (Time.unscaledTime < nextTogglePlacement && size == toggleScreen) return toggle;
            nextTogglePlacement = Time.unscaledTime + 0.5f; toggleScreen = size;
            var right = Mathf.Max(0, Screen.width - width - margin);
            var native = NativeEditorWindow();
            for (var y = Mathf.Min(margin, Mathf.Max(0, Screen.height - height)); y + height + margin <= Screen.height; y += height + gap) {
                var candidate = new Rect(right, y, width, height);
                if (!Occupied(candidate, native, gap)) return toggle = candidate;
            }
            // No free slot was found: keep the bottom of the right edge rather than cover the top chrome.
            return toggle = new Rect(right, Mathf.Max(0, Screen.height - height - margin), width, height);
        }
        bool Occupied(Rect candidate, Rect? native, float gap) {
            if (native is Rect window && new Rect(window.x - gap, window.y - gap, window.width + 2 * gap, window.height + 2 * gap).Overlaps(candidate)) return true;
            var events = EventSystem.current;
            if (events == null) return false;
            for (var column = 0; column <= 4; column++) {
                for (var row = 0; row <= 2; row++) {
                    var point = new Vector2(candidate.x + candidate.width * column / 4f, Screen.height - (candidate.y + candidate.height * row / 2f));
                    chromeHits.Clear();
                    events.RaycastAll(new PointerEventData(events) { position = point }, chromeHits);
                    foreach (var hit in chromeHits) {
                        // Only UI graphics count, not world raycasts. The patcher's own regions and full-screen input surfaces are not chrome.
                        if (!(hit.module is GraphicRaycaster) || hit.gameObject == null || inputBlocker.Owns(hit.gameObject)) continue;
                        if (hit.gameObject.transform is RectTransform bounds) {
                            bounds.GetWorldCorners(chromeCorners);
                            if (Mathf.Abs(chromeCorners[2].x - chromeCorners[0].x) * Mathf.Abs(chromeCorners[2].y - chromeCorners[0].y) >= 0.5f * Screen.width * Screen.height) continue;
                        }
                        return true;
                    }
                }
            }
            return false;
        }
        // The initial panel layout: right-aligned below the toggle, sized to the actual viewport. Dragging is not constrained afterwards.
        void PlacePanel(Rect button) {
            const float gap = 8, margin = 20;
            window.width = Mathf.Min(700, Mathf.Max(360, Screen.width * 0.4f), Mathf.Max(1, Screen.width - 2 * margin));
            window.x = Mathf.Max(0, Screen.width - window.width - margin);
            window.y = Mathf.Clamp(button.yMax + gap, 0, Mathf.Max(0, Screen.height - 240 - margin));
            window.height = Mathf.Min(620, Mathf.Max(1, Screen.height - window.y - margin));
        }
        void DrawWindow(int id) {
            GUILayout.Label(status);
            DrawSelection();
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
                        selected = entry; inspected = DescribeInspection(entry); ReadPose();
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
            DrawAuthoring();
            foreach (var coverage in NativeInventory.Coverage) GUILayout.Label(coverage);
            GUILayout.Label("Binding validity does not establish correct placement or intended track. Inspect the world before editing.");
            if (GUILayout.Button("Inventory and dependency guidance")) Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/inspect-scene-objects.md");
            if (GUILayout.Button("World selection guidance")) Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/select-world-targets.md");
            GUILayout.Label("Track binding edits, the transform gizmo and snapping are not implemented.");
            if (GUILayout.Button("Fixed-placement guidance")) Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/authoring-fixed-placement.md");
            if (GUILayout.Button("Document v1 reference")) Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/scene-patch-document-v1.md");
            GUILayout.EndScrollView(); GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
        void DrawSelection() {
            if (selection == null) return;
            var pending = selection.Pending;
            var confirmed = selection.Confirmed;
            if (pending == null) {
                if (GUILayout.Button("Select scene object in world")) BeginSelection(SelectionKind.SceneObject, null);
                if (selected != null) {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Choose replacement object")) BeginSelection(SelectionKind.SceneObject, selected);
                    if (GUILayout.Button("Choose replacement segment")) BeginSelection(SelectionKind.TrackSegment, selected);
                    if (GUILayout.Button("Choose replacement node")) BeginSelection(SelectionKind.TrackNode, selected);
                    GUILayout.EndHorizontal();
                }
            } else {
                var candidate = selection.Find(pending.Candidate);
                GUILayout.Label(pending.Purpose + ". Click a marking or track in the world.");
                if (pending.Source != null) GUILayout.Label("Source: " + pending.Source.Label);
                if (pending.Problem != null) GUILayout.Label(pending.Problem);
                if (pending.Offers.Count > 1) {
                    choiceScroll = GUILayout.BeginScrollView(choiceScroll, GUILayout.Height(110));
                    foreach (var offer in pending.Offers) {
                        if (ReferenceEquals(offer, pending.Candidate)) continue;
                        if (GUILayout.Button("Choose " + offer.Label)) pending.Choose(offer);
                    }
                    GUILayout.EndScrollView();
                }
                if (candidate != null) {
                    GUILayout.Label("Candidate: " + candidate.Choice.Label);
                    GUILayout.Label(candidate.Choice.Identity.ToString());
                    GUILayout.Label(candidate.Details);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Focus candidate")) {
                        try { JumpTo(candidate.Position); }
                        catch (Exception e) { selectionProblem = "Camera focus unavailable: " + e.GetBaseException().Message; }
                    }
                    if (candidate.Observation != null && GUILayout.Button("Inspect candidate")) {
                        selected = candidate.Observation;
                        inspected = DescribeInspection(selected);
                        Inspect();
                        ReadPose();
                    }
                    if (GUILayout.Button("Confirm identity")) ConfirmSelection();
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Cancel selection")) { selection.Cancel(); selectionProblem = null; }
            }
            if (confirmed != null)
                GUILayout.Label("Confirmed: " + confirmed.Identity + " in " + confirmed.MapIds[0]);
            if (inputBlocker.WorldInputProblem != null && pending != null) GUILayout.Label(inputBlocker.WorldInputProblem);
            if (selectionProblem != null) GUILayout.Label(selectionProblem);
        }

        void BeginSelection(SelectionKind kind, SceneObservation origin) {
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
        // Editing needs a mutation adapter; today that is the board pattern the inventory marks with its structure.
        bool Editable => selected != null && selected.Identity.Structure == BoardScene.Structure && selected.Handle is GameObject;
        void ReadPose() {
            if (selected == null || selected.Position.Count != 3 || selected.Rotation.Count != 4) return;
            var angles = new Quaternion((float)selected.Rotation[0], (float)selected.Rotation[1], (float)selected.Rotation[2], (float)selected.Rotation[3]).eulerAngles;
            var values = new[] { selected.Position[0], selected.Position[1], selected.Position[2], angles.x, angles.y, angles.z };
            for (var i = 0; i < 6; i++) pose[i] = ((float)values[i]).ToString("R", CultureInfo.InvariantCulture);
        }
        // Every edit is bound to the inspected observation. A stale inventory refuses the edit instead of redirecting it.
        void Edit(string action, Func<AuthoringSession, InspectedTarget, Outcome> change) {
            if (selection?.Pending != null) return;
            Outcome outcome;
            try { outcome = change(authoring.Session, inventory.Pin(selected)); }
            catch (Exception e) { editResult = action + " refused: " + e.GetBaseException().Message; return; }
            editResult = outcome.Applied ? action + " completed. Undo and Redo use the editor's history; Save uses the editor's Save button." : action + " refused: " + outcome.Error;
            // A completed edit changed the observed values; refresh so the next edit binds to the new observation.
            if (outcome.Applied) { Inspect(); ReadPose(); }
            try { context?.Log.Info(editResult + " Outstanding: " + authoring.Outstanding()); } catch { }
        }
        void DrawAuthoring() {
            GUILayout.Label(authoringStatus);
            if (authoring == null) return;
            var session = authoring.Session;
            try {
                var outstanding = session.Outstanding(authoring.NativeDirty);
                GUILayout.Label(outstanding.Count == 0 ? "All required writes are saved." : "Unsaved: " + String.Join(", ", outstanding) + ". Use the editor's Save button.");
            } catch (Exception e) { GUILayout.Label("Outstanding work unknown: " + e.GetBaseException().Message); }
            if (session.LastSave != null && session.LastSave.Error != null) GUILayout.Label("Last Save failed: " + session.LastSave.Error + " Completed: " + (session.LastSave.Completed.Count == 0 ? "none" : String.Join(", ", session.LastSave.Completed)) + ". Outstanding: " + String.Join(", ", session.LastSave.Outstanding) + ". Edits remain in memory; correct the cause and Save again.");
            if (session.IncompleteOperation != null) GUILayout.Label("Incomplete operation: " + session.IncompleteOperation);
            if (authoring.Fault != null) GUILayout.Label(authoring.Fault);
            if (selection?.Pending != null) {
                GUILayout.Label("Confirm or cancel the world selection to edit placement or activity.");
                return;
            }
            if (!Editable) { GUILayout.Label("Inspect an object with a fixed-placement adapter to edit it. Other observations are inspection only."); return; }
            var target = (GameObject)selected.Handle;
            if (target == null) { GUILayout.Label("The inspected object no longer exists. Refresh the inventory."); return; }
            GUILayout.Label("Selected: " + selected.Identity.Path + (target.activeSelf ? "" : " (disabled)") + ". Position in map coordinates; rotation in degrees. After Undo, Redo or a native edit, refresh the inventory before the next edit.");
            GUILayout.BeginHorizontal();
            var labels = new[] { "X", "Y", "Z", "Rot X", "Rot Y", "Rot Z" };
            for (var i = 0; i < 6; i++) { GUILayout.Label(labels[i], GUILayout.ExpandWidth(false)); pose[i] = GUILayout.TextField(pose[i], GUILayout.MinWidth(70)); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            var identity = selected.Identity;
            if (GUILayout.Button("Apply placement")) {
                var values = new float[6]; var valid = true;
                for (var i = 0; i < 6; i++) valid &= Single.TryParse(pose[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
                if (!valid) editResult = "Placement refused: enter six numbers with a decimal point";
                else {
                    var rotation = Quaternion.Euler(values[3], values[4], values[5]);
                    var placement = new Placement(new double[] { values[0], values[1], values[2] }, new double[] { rotation.x, rotation.y, rotation.z, rotation.w });
                    Edit("Placement", (session, inspection) => session.Edit(identity, placement, null, inspection));
                }
            }
            if (GUILayout.Button("Read inspected values")) ReadPose();
            if (GUILayout.Button(target.activeSelf ? "Disable object" : "Enable object")) { var enable = !target.activeSelf; Edit("Activity change", (session, inspection) => session.Edit(identity, null, enable, inspection)); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove modification")) Edit("Removal", (session, inspection) => session.Remove(identity, inspection));
            if (GUILayout.Button("Recheck after failed edit")) Edit("Recheck", (session, inspection) => session.Recheck(identity, inspection));
            if (GUILayout.Button("Authoring guidance")) Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/editing-placement-in-the-editor.md");
            GUILayout.EndHorizontal();
            GUILayout.Label(editResult);
            GUILayout.Label("Removal returns the object to its state without this MapMod's modification. Save writes map data, Data/scene-patches.json and the patcher dependency in Info.json, and never reloads. Reloading the editor ends Undo/Redo history.");
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
