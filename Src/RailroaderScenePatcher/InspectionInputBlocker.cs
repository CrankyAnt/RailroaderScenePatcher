// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // Native input has separate EventSystem and WindowManager hit-test gates. A world click follows the native picker's
    // gates too: a press on a popup blocker, such as the one behind an open context menu, is no world click, and neither
    // is a press the native map editor claims for itself. The claim is made after this component's Update, so a candidate
    // press is confirmed in LateUpdate and published as WorldClick in the next frame.
    [DefaultExecutionOrder(-20000)]
    public sealed class InspectionInputBlocker : MonoBehaviour {
        GameObject canvasRoot;
        Component nativeWindow;
        RectTransform nativeWindowBounds;
        RectTransform toggleRegion, panelRegion, dragRegion;
        Image dragImage;
        // Bounds arrive from the drawn panel and toggle.
        Rect toggle, panel;
        bool panelOpen, captured, worldCaptured, worldHoverShield, navigating, worldHooks;
        internal bool WorldPicking { get; set; }
        internal bool WorldInspecting { get; set; }
        internal bool WorldEditing {
            get => WorldInputHooks.EditOpen;
            set { if (worldHooks) WorldInputHooks.EditOpen = value; }
        }
        internal bool GizmoShown { get; set; }
        internal Action RefreshRegions { get; set; }
        internal Func<Vector2, bool> OverWorldLabel { get; set; }
        internal Vector2? WorldClick { get; private set; }
        Vector2? candidate, confirmed;
        bool candidateInspectOnly;
        MethodInfo overUiPoint;
        FieldInfo pressInput, primaryState, pressClaimed, pressStarted;
        // The UI gate's problem, kept until the gate answers again, and the press claim's, kept until the claim reads again.
        string gateProblem, claimProblem;
        internal string WorldInputProblem => gateProblem ?? claimProblem;
        internal bool Available => isActiveAndEnabled && canvasRoot != null && canvasRoot.activeInHierarchy && nativeWindow != null && nativeWindow.gameObject.activeInHierarchy && EventSystem.current != null;

        internal void Initialize() {
            var input = Native.Type("UI.GameInput");
            if (input == null || input.GetMethod("IsMouseOverUI", Native.Flags) == null)
                throw new InvalidOperationException("Native UI pointer gate unavailable");
            overUiPoint = input.GetMethod("IsScreenPointOverUI", Native.Flags, null, new[] { typeof(Vector2), typeof(bool) }, null);
            pressInput = input.GetField("pressInput", Native.Flags);
            primaryState = pressInput?.FieldType.GetField("_primaryState", Native.Flags);
            pressClaimed = primaryState?.FieldType.GetField("_pressClaimed", Native.Flags);
            pressStarted = primaryState?.FieldType.GetField("_startedThisFrame", Native.Flags);
            if (overUiPoint == null || !overUiPoint.IsStatic || overUiPoint.ReturnType != typeof(bool) || input.GetProperty("Shared", Native.Flags) == null
                || pressClaimed == null || pressClaimed.FieldType != typeof(bool) || pressStarted == null || pressStarted.FieldType != typeof(bool))
                throw new InvalidOperationException("Native popup blocker and press claim gates unavailable");
            var layer = Convert.ToInt32(Native.Read(Native.Type("Helpers.Layers"), "UI"));
            if (layer < 0 || layer > 31) throw new InvalidOperationException("Native UI layer unavailable");
            canvasRoot = new GameObject("CrankyAnt Scene Patcher pointer regions", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasRoot.layer = layer;
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            canvas.scaleFactor = 1;
            toggleRegion = Region("Scene Patcher button", layer);
            panelRegion = Region("Scene Patcher panel", layer);
            dragRegion = Region("Scene Patcher pointer capture", layer);
            dragImage = dragRegion.GetComponent<Image>();
            InitializeNativeWindow();
            WorldInputHooks.Acquire(); worldHooks = true;
            SetRegions(false, panel, toggle);
        }
        void InitializeNativeWindow() {
            // Strategy wheel input uses WindowManager.HitTest, separately from UI raycasts.
            // A bare Window is invalid: Awake requires the native prefab's serialized wiring.
            var manager = Native.Read(Native.Type("UI.Windows.WindowManager"), "Shared") as Component;
            var windowType = Native.Type("UI.Windows.Window");
            if (manager == null || windowType == null || windowType.GetMethod("ShowWindow", Native.Flags) == null || windowType.GetMethod("CloseWindow", Native.Flags) == null || windowType.GetProperty("IsShown", Native.Flags) == null)
                throw new InvalidOperationException("Native window hit testing unavailable");
            var prefab = Native.Read(manager, "windowPrefab");
            var prefabRoot = prefab is Component component ? component.gameObject : prefab as GameObject;
            var template = prefabRoot == null ? null : prefabRoot.GetComponent(windowType);
            if (template == null || Native.Read(template, "contentRectTransform") == null || Native.Read(template, "draggablePanel") == null || Native.Read(template, "resizer") == null)
                throw new InvalidOperationException("Native window prefab wiring unavailable");
            var root = Instantiate(prefabRoot, manager.transform, false);
            nativeWindow = root.GetComponent(windowType);
            nativeWindowBounds = (RectTransform)root.transform;
            root.name = "CrankyAnt Scene Patcher native pointer boundary";
            var group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) { graphic.raycastTarget = false; graphic.enabled = false; }
            Native.Call(nativeWindow, "CloseWindow");
        }
        RectTransform Region(string name, int layer) {
            var region = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            region.layer = layer;
            region.transform.SetParent(canvasRoot.transform, false);
            var image = region.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            region.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
            return (RectTransform)region.transform;
        }
        internal bool Owns(GameObject target) => canvasRoot != null && target != null && target.transform.IsChildOf(canvasRoot.transform);
        internal void SetRegions(bool open, Rect window, Rect button) {
            panelOpen = open; panel = window; toggle = button;
            if (canvasRoot == null) return;
            // Visible native controls now own their own raycasts and pointer capture.
            // An overlay over them would consume the click before their callbacks receive it.
            Place(toggleRegion, toggle, false);
            Place(panelRegion, panel, false);
            Place(dragRegion, new Rect(0, 0, Screen.width, Screen.height), !navigating && (worldCaptured || worldHoverShield));
        }
        void SetNativeWindow(bool shown, Rect bounds) {
            if (nativeWindow == null || !nativeWindow.gameObject.activeInHierarchy) return;
            var wasShown = (bool)Native.Read(nativeWindow, "IsShown");
            if (wasShown != shown) Native.Call(nativeWindow, shown ? "ShowWindow" : "CloseWindow");
            if (!shown) return;
            // HitTest transforms bottom-left screen pixels with InverseTransformPoint.
            // Position in that same space and account for the native canvas's scale.
            nativeWindowBounds.anchorMin = nativeWindowBounds.anchorMax = nativeWindowBounds.pivot = Vector2.zero;
            nativeWindowBounds.rotation = Quaternion.identity;
            nativeWindowBounds.localScale = Vector3.one;
            var scale = nativeWindowBounds.lossyScale;
            nativeWindowBounds.sizeDelta = new Vector2(bounds.width / scale.x, bounds.height / scale.y);
            nativeWindowBounds.position = new Vector3(bounds.x, Screen.height - bounds.y - bounds.height, 0);
        }
        static void Place(RectTransform region, Rect bounds, bool active) {
            region.anchorMin = region.anchorMax = region.pivot = new Vector2(0, 1);
            region.anchoredPosition = new Vector2(bounds.x, -bounds.y);
            region.sizeDelta = new Vector2(bounds.width, bounds.height);
            region.gameObject.SetActive(active);
        }
        void Update() {
            WorldClick = confirmed; confirmed = null; candidate = null; candidateInspectOnly = false;
            if (canvasRoot == null) return;
            WorldInputHooks.OverlayShielded = false;
            RefreshRegions?.Invoke();
            var position = Input.mousePosition;
            var point = new Vector2(position.x, Screen.height - position.y);
            bool pressed = false, held = false, released = false;
            for (var button = 0; button < 3; button++) {
                pressed |= Input.GetMouseButtonDown(button);
                held |= Input.GetMouseButton(button);
                released |= Input.GetMouseButtonUp(button);
            }
            // Retain the raycast shield through the release frame, including outside-window drags.
            if (!held && !released) captured = false;
            var overPanel = panelOpen && panel.Contains(point);
            var overButton = toggle.Contains(point);
            if (pressed && (overButton || overPanel)) captured = true;
            if (!Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) worldCaptured = false;
            navigating = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            worldHoverShield = false;
            var primaryPressed = Input.GetMouseButtonDown(0);
            var overWorldLabel = false;
            if (Available && !captured && !worldCaptured && !overButton && !overPanel) {
                try {
                    overWorldLabel = WorldInspecting && !navigating && OverWorldLabel?.Invoke(point) == true;
                    var needsWorldShield = !navigating && (WorldPicking || (WorldEditing && !GizmoShown) || overWorldLabel);
                    var inspectClick = WorldInspecting && primaryPressed;
                    if (needsWorldShield || inspectClick) {
                        // The native UI gate first checks the EventSystem's cached hover state.
                        // Arm the shield before the click, and hide it while checking for native UI underneath.
                        dragImage.raycastTarget = false;
                        var input = Native.Type("UI.GameInput");
                        bool overUi = (bool)Native.Call(input, "IsMouseOverUI", null, null)
                            || (bool)overUiPoint.Invoke(null, new object[] { (Vector2)position, true });
                        bool overWorld = (bool)Native.Call(input, "IsMouseOverGameWindow", new object[] { null });
                        if (!overUi && overWorld) {
                            worldHoverShield = needsWorldShield;
                            if (inspectClick || (WorldPicking && needsWorldShield && primaryPressed)) {
                                worldCaptured = needsWorldShield;
                                candidate = point;
                                candidateInspectOnly = inspectClick;
                            }
                        }
                        gateProblem = null;
                    }
                } catch (Exception e) {
                    gateProblem = "Cannot select in the world: " + e.GetBaseException().Message;
                } finally { dragImage.raycastTarget = true; }
            }
            WorldInputHooks.OverlayShielded = !navigating && WorldInspecting && (worldCaptured || (overWorldLabel && worldHoverShield));
            SetRegions(panelOpen, panel, toggle);
            // Only panel drags need the full native window shield for wheel input.
            SetNativeWindow(captured || overButton || overPanel, captured ? new Rect(0, 0, Screen.width, Screen.height) : overPanel ? panel : toggle);
        }
        // The native map editor and picker claim the primary press during their Update, which runs after this component's. A
        // claim counts only for a press the native input started this frame: the claim flag is reset only when a press starts,
        // so without a started press, as while a text field has focus, the flag still holds an earlier press's claim.
        void LateUpdate() {
            if (candidate == null) return;
            var point = candidate; candidate = null;
            if (candidateInspectOnly) { candidateInspectOnly = false; confirmed = point; claimProblem = null; return; }
            try {
                var shared = Native.Read(Native.Type("UI.GameInput"), "Shared");
                var press = shared == null ? null : pressInput.GetValue(shared);
                var state = press == null ? null : primaryState.GetValue(press);
                if (state == null) throw new InvalidOperationException("Native primary press state unavailable");
                var claimed = (bool)pressStarted.GetValue(state) && (bool)pressClaimed.GetValue(state);
                claimProblem = null;
                if (!claimed) confirmed = point;
            } catch (Exception e) {
                claimProblem = "Cannot select in the world: " + e.GetBaseException().Message;
            }
        }
        void OnDisable() {
            captured = worldCaptured = worldHoverShield = WorldPicking = WorldInspecting = WorldEditing = GizmoShown = false;
            if (worldHooks) WorldInputHooks.OverlayShielded = false;
            WorldClick = candidate = confirmed = null; candidateInspectOnly = false;
            SetNativeWindow(false, panel); if (canvasRoot != null) canvasRoot.SetActive(false);
        }
        void OnEnable() { if (canvasRoot != null) canvasRoot.SetActive(true); }
        internal void Release() {
            RefreshRegions = null;
            OverWorldLabel = null;
            captured = worldCaptured = worldHoverShield = WorldPicking = WorldInspecting = WorldEditing = GizmoShown = false;
            if (worldHooks) {
                WorldInputHooks.OverlayShielded = false;
                worldHooks = false;
                WorldInputHooks.Release();
            }
            WorldClick = candidate = confirmed = null; candidateInspectOnly = false;
            if (nativeWindow != null) {
                var root = nativeWindow.gameObject;
                if (root.activeInHierarchy) Native.Call(nativeWindow, "CloseWindow");
                root.transform.SetParent(null, false);
                root.SetActive(false); Destroy(root); nativeWindow = null; nativeWindowBounds = null;
            }
            if (canvasRoot != null) { canvasRoot.SetActive(false); Destroy(canvasRoot); canvasRoot = null; }
        }
        void OnDestroy() { Release(); }
    }
}
