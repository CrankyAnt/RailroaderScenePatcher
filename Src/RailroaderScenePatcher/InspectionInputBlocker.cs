// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // Native input has separate EventSystem and WindowManager hit-test gates.
    [DefaultExecutionOrder(-20000)]
    public sealed class InspectionInputBlocker : MonoBehaviour {
        GameObject canvasRoot;
        Component nativeWindow;
        RectTransform nativeWindowBounds;
        RectTransform toggleRegion, panelRegion, dragRegion;
        Image dragImage;
        // Bounds arrive from the drawn panel and toggle.
        Rect toggle, panel;
        bool panelOpen, captured, worldCaptured, worldHoverShield;
        internal bool WorldPicking { get; set; }
        internal Vector2? WorldClick { get; private set; }
        internal string WorldInputProblem { get; private set; }
        internal bool Available => isActiveAndEnabled && canvasRoot != null && canvasRoot.activeInHierarchy && nativeWindow != null && nativeWindow.gameObject.activeInHierarchy && EventSystem.current != null;

        internal void Initialize() {
            var input = Native.Type("UI.GameInput");
            if (input == null || input.GetMethod("IsMouseOverUI", Native.Flags) == null)
                throw new InvalidOperationException("Native UI pointer gate unavailable");
            var layer = Convert.ToInt32(Native.Read(Native.Type("Helpers.Layers"), "UI"));
            if (layer < 0 || layer > 31) throw new InvalidOperationException("Native UI layer unavailable");
            canvasRoot = new GameObject("CrankyAnt Scene Patcher pointer regions", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasRoot.layer = layer;
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            canvas.scaleFactor = 1;
            toggleRegion = Region("Scene patches button", layer);
            panelRegion = Region("Scene patches panel", layer);
            dragRegion = Region("Scene patches pointer capture", layer);
            dragImage = dragRegion.GetComponent<Image>();
            InitializeNativeWindow();
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
            Place(toggleRegion, toggle, true);
            Place(panelRegion, panel, panelOpen);
            Place(dragRegion, new Rect(0, 0, Screen.width, Screen.height), captured || worldCaptured || worldHoverShield);
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
            WorldClick = null;
            if (canvasRoot == null) return;
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
            worldHoverShield = false;
            if (WorldPicking && Available && !captured && !worldCaptured && !overButton && !overPanel &&
                !Input.GetMouseButton(1) && !Input.GetMouseButton(2)) {
                try {
                    // The native UI gate first checks the EventSystem's cached hover state.
                    // Arm the shield before the click, and hide it while checking for native UI underneath.
                    dragImage.raycastTarget = false;
                    var input = Native.Type("UI.GameInput");
                    bool overUi = (bool)Native.Call(input, "IsMouseOverUI", null, null);
                    bool overWorld = (bool)Native.Call(input, "IsMouseOverGameWindow", new object[] { null });
                    if (!overUi && overWorld) {
                        worldHoverShield = true;
                        if (Input.GetMouseButtonDown(0)) {
                            worldCaptured = true;
                            WorldClick = point;
                        }
                    }
                    WorldInputProblem = null;
                } catch (Exception e) {
                    WorldInputProblem = "World selection input unavailable: " + e.GetBaseException().Message;
                } finally { dragImage.raycastTarget = true; }
            }
            SetRegions(panelOpen, panel, toggle);
            // Only panel drags need the full native window shield for wheel input.
            SetNativeWindow(captured || overButton || overPanel, captured ? new Rect(0, 0, Screen.width, Screen.height) : overPanel ? panel : toggle);
        }
        void OnDisable() { captured = worldCaptured = worldHoverShield = false; WorldClick = null; SetNativeWindow(false, panel); if (canvasRoot != null) canvasRoot.SetActive(false); }
        void OnEnable() { if (canvasRoot != null) canvasRoot.SetActive(true); }
        internal void Release() {
            captured = worldCaptured = worldHoverShield = WorldPicking = false;
            WorldClick = null;
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
