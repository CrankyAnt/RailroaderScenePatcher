// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UI.Builder;
using UI.Windows;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // Uses the same prefab, builder assets and panel lifetime as native builder windows.
    // Internal game integration: construction and interaction require a native journey on each checked build.
    public sealed class ScenePatchWindow : BuilderWindowBase {
        internal Action<UIPanelBuilder> Build;
        public override string Identifier => "crankyant/scene-patcher/editor";
        public override WindowConfig Config => WindowConfig.Resizable(new Vector2Int(580, 620), new Vector2Int(300, 200), Window.Position.UpperRight);
        protected override string Title { get; set; } = "Scene patches";
        internal Window NativeWindow => Window;
        protected override void BuildContent(UIPanelBuilder builder) { Build?.Invoke(builder); }
        internal void Refresh() { RebuildPanel(); }
    }

    internal sealed class NativeEditorPresentation : IDisposable {
        readonly ScenePatchWindow window;
        readonly GameObject entry;
        readonly UIPanel entryPanel;
        readonly Action<UIPanelBuilder> content;
        readonly Action closed;
        readonly List<Rect> chrome = new List<Rect>();
        readonly List<Rect> worldUiBounds = new List<Rect>();
        ScrollRect scroll;
        bool dirty = true, disposed;
        Vector2 screen;
        float scale;
        float nextLayout;
        bool panelPlaced;
        Vector2? restoreScroll;
        string entryCaption = "Scene patches", entryReason = "Open scene-patch inspection and authoring.";
        internal bool IsOpen => !disposed && window != null && window.NativeWindow.IsShown;
        internal Rect PanelBounds => IsOpen ? Bounds((RectTransform)window.transform) : Rect.zero;
        internal Rect EntryBounds => !disposed && entry != null && entry.activeInHierarchy ? Bounds((RectTransform)entry.transform) : Rect.zero;

        internal NativeEditorPresentation(Action<UIPanelBuilder> build, Action onClosed) {
            content = build; closed = onClosed;
            var manager = WindowManager.Shared;
            if (manager == null || manager.windowPrefab == null || manager.builderAssets == null ||
                manager.builderAssets.buttonCompact == null || manager.builderAssets.buttonSelected == null ||
                manager.builderAssets.inputField == null || manager.builderAssets.filterField == null ||
                manager.builderAssets.labelControl == null || manager.builderAssets.sectionHeader == null ||
                manager.builderAssets.scrollRectVertical == null)
                throw new InvalidOperationException("Native editor window or control assets unavailable");
            try {
                window = manager.CreateWindowWithComponent<ScenePatchWindow>();
                window.Build = BuildContent;
                window.NativeWindow.DelegateRequestClose = Close;
                window.NativeWindow.OnShownDidChange += ShownChanged;
                entry = new GameObject("CrankyAnt Scene patches entry", typeof(RectTransform));
                entry.layer = manager.windowPrefab.gameObject.layer;
                entry.transform.SetParent(manager.transform, false);
                entryPanel = UIPanel.Create((RectTransform)entry.transform, manager.builderAssets, b =>
                    b.AddButtonCompact(() => entryCaption, Toggle).Tooltip(() => new TooltipInfo("Scene patches", entryReason)));
                Layout(true);
            } catch { Dispose(); throw; }
        }

        void BuildContent(UIPanelBuilder builder) {
            scroll = builder.VScrollView(content).GetComponent<ScrollRect>();
        }
        internal Action Action(Action action) => () => {
            if (disposed || !IsOpen) return;
            action(); dirty = true;
        };
        internal void RequestRefresh() { dirty = true; }
        // Feature-owned persistent feedback can update the entry without rebuilding an active input field.
        internal void SetEntryStatus(string caption, string reason) { entryCaption = caption; entryReason = reason; }
        void Toggle() {
            if (IsOpen) { Close(); return; }
            dirty = false;
            window.Show();
            Layout(true);
        }
        void Close() {
            if (disposed) return;
            ClearFocus();
            window.NativeWindow.CloseWindow();
            // Native close completes after the end-of-frame yield. Notify only from its transition event.
        }
        void ShownChanged(bool shown) { if (!shown && !disposed) { ClearFocus(); closed(); } }
        internal void Tick() {
            if (disposed) return;
            Layout(false);
            if (restoreScroll.HasValue && scroll != null) {
                Canvas.ForceUpdateCanvases();
                scroll.normalizedPosition = restoreScroll.Value; restoreScroll = null;
            }
            // A background freshness/history update must not destroy an active text edit or pointer drag.
            bool editing = window.GetComponentsInChildren<TMP_InputField>().Any(f => f.isFocused);
            if (dirty && IsOpen && !editing && !Input.GetMouseButton(0)) {
                restoreScroll = scroll == null ? (Vector2?)null : scroll.normalizedPosition;
                dirty = false; window.Refresh();
            }
        }
        void ClearFocus() {
            var events = EventSystem.current;
            if (events != null && Owns(events.currentSelectedGameObject)) events.SetSelectedGameObject(null);
        }
        internal bool Owns(GameObject target) => target != null &&
            ((entry != null && target.transform.IsChildOf(entry.transform)) ||
             (window != null && target.transform.IsChildOf(window.transform)));

        // The same visible native UI bounds drive layout, world clipping and pick exclusion.
        // Read transforms on every use so dragging does not leave a cached exclusion behind.
        internal IReadOnlyList<Rect> WorldUiBounds() {
            worldUiBounds.Clear();
            CollectVisibleUi(worldUiBounds, true);
            return worldUiBounds;
        }
        void CollectVisibleUi(List<Rect> bounds, bool includeOwned) {
            if (includeOwned) { AddBounds(bounds, PanelBounds); AddBounds(bounds, EntryBounds); }
            var manager = WindowManager.Shared;
            if (manager != null) foreach (var native in manager.GetComponentsInChildren<Window>()) {
                if (!native.IsShown || !native.gameObject.activeInHierarchy || (!includeOwned && Owns(native.gameObject)) ||
                    native.GetComponentsInParent<CanvasGroup>().Any(group => group.alpha == 0)) continue;
                AddBounds(bounds, Bounds((RectTransform)native.transform));
            }
            foreach (var graphic in UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsSortMode.InstanceID)) {
                if (!graphic.isActiveAndEnabled || !graphic.raycastTarget || graphic.color.a == 0 ||
                    (!includeOwned && Owns(graphic.gameObject)) || graphic.GetComponentInParent<InspectionInputBlocker>() != null ||
                    graphic.canvas == null || graphic.canvas.renderMode == RenderMode.WorldSpace || graphic.canvasRenderer.cull ||
                    graphic.canvasRenderer.GetInheritedAlpha() == 0) continue;
                var rect = Bounds(graphic.rectTransform);
                // A tooltip's CanvasGroup does not block raycasts. Its visible background must not
                // move the very entry being hovered. Use Unity's actual raycast filters for layout.
                if (!includeOwned) {
                    var camera = graphic.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : graphic.canvas.worldCamera;
                    var point = new Vector2(rect.center.x, Screen.height - rect.center.y);
                    if (!graphic.Raycast(point, camera)) continue;
                }
                // Full-screen input surfaces are not chrome; visible windows were included above.
                if (rect.width * rect.height < Screen.width * Screen.height * 0.5f) AddBounds(bounds, rect);
            }
        }
        static void AddBounds(List<Rect> bounds, Rect rect) {
            if (rect.width <= 0 || rect.height <= 0 || !rect.Overlaps(new Rect(0, 0, Screen.width, Screen.height))) return;
            bool Contains(Rect outer, Rect inner) => outer.xMin <= inner.xMin && outer.yMin <= inner.yMin &&
                outer.xMax >= inner.xMax && outer.yMax >= inner.yMax;
            if (bounds.Any(existing => Contains(existing, rect))) return;
            bounds.RemoveAll(existing => Contains(rect, existing)); bounds.Add(rect);
        }

        internal static Rect Bounds(RectTransform transform) {
            var corners = new Vector3[4]; transform.GetWorldCorners(corners);
            var canvas = transform.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var points = corners.Select(corner => RectTransformUtility.WorldToScreenPoint(camera, corner)).ToArray();
            return Rect.MinMaxRect(points.Min(p => p.x), Screen.height - points.Max(p => p.y),
                points.Max(p => p.x), Screen.height - points.Min(p => p.y));
        }
        void Layout(bool force) {
            var canvas = window.GetComponentInParent<Canvas>();
            var currentScale = canvas == null ? 1 : canvas.scaleFactor;
            var size = new Vector2(Screen.width, Screen.height);
            if (!force && size == screen && currentScale == scale && Time.unscaledTime < nextLayout) return;
            // Let native controls finish a press/drag before moving any hit rectangle.
            if (Input.GetMouseButton(0)) return;
            nextLayout = Time.unscaledTime + 0.5f;
            screen = size; scale = currentScale;
            chrome.Clear();
            CollectVisibleUi(chrome, false);
            float margin = 12 * scale, gap = 8 * scale;
            float entryWidth = 180 * scale, entryHeight = 30 * scale;
            var previousEntry = EntryBounds;
            var entryRect = new Rect(Screen.width - entryWidth - margin, previousEntry.height > 0 ? previousEntry.y : margin, entryWidth, entryHeight);
            if (!Safe(entryRect, margin)) {
                entryRect.y = margin;
                while (entryRect.yMax < Screen.height - margin && chrome.Any(r => r.Overlaps(entryRect))) entryRect.y += entryHeight + gap;
            }
            bool entryFits = Safe(entryRect, margin);
            entry.SetActive(entryFits);
            if (!entryFits) { if (IsOpen) Close(); return; }
            Place((RectTransform)entry.transform, entryRect);
            if (!IsOpen) return;
            var current = PanelBounds;
            // Chrome rebuilds and unrelated native drags do not invalidate a safe user placement.
            // Reflow only for an actual obstruction or an off-screen window, preserving its size.
            if (panelPlaced && Safe(current, 0) && !current.Overlaps(entryRect)) return;
            var width = panelPlaced ? Mathf.Min(current.width, Screen.width - 2 * margin) : Mathf.Min(700 * scale, Screen.width * 0.4f);
            var height = panelPlaced ? current.height : 660 * scale;
            var available = new Rect(Screen.width - width - margin, entryRect.yMax + gap, width, Screen.height - entryRect.yMax - gap - margin);
            foreach (var rect in chrome) {
                if (rect.xMax <= available.x || rect.xMin >= available.xMax || !rect.Overlaps(available)) continue;
                // Keep the largest continuous vertical slot on the right, clear of actual native chrome.
                var above = rect.yMin - gap - available.yMin;
                var below = available.yMax - rect.yMax - gap;
                if (above >= below) available.height = Mathf.Max(0, above);
                else { var bottom = available.yMax; available.y = rect.yMax + gap; available.height = Mathf.Max(0, bottom - available.y); }
            }
            if (available.height < 200 * scale) { Close(); return; }
            available.height = Mathf.Min(available.height, height);
            Place((RectTransform)window.transform, available);
            panelPlaced = true;
        }
        bool Safe(Rect rect, float margin) => rect.width > 0 && rect.height > 0 && rect.xMin >= margin && rect.yMin >= margin &&
            rect.xMax <= Screen.width - margin && rect.yMax <= Screen.height - margin && !chrome.Any(r => r.Overlaps(rect));
        static void Place(RectTransform transform, Rect pixels) {
            transform.anchorMin = transform.anchorMax = transform.pivot = new Vector2(0, 1);
            var s = transform.lossyScale;
            transform.sizeDelta = new Vector2(pixels.width / s.x, pixels.height / s.y);
            transform.position = new Vector3(pixels.x, Screen.height - pixels.y, 0);
        }
        public void Dispose() {
            if (disposed) return;
            ClearFocus(); disposed = true;
            entryPanel?.Dispose();
            if (entry != null) { entry.SetActive(false); UnityEngine.Object.Destroy(entry); }
            if (window != null) {
                window.Build = null;
                window.NativeWindow.DelegateRequestClose = null;
                window.NativeWindow.OnShownDidChange -= ShownChanged;
                window.gameObject.SetActive(false); UnityEngine.Object.Destroy(window.gameObject);
            }
        }
    }
}
