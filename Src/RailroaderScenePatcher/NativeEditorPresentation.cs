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
        // The identifier names the saved bounds. It changed with the default position, so bounds saved for the earlier
        // right-side default do not override the new one.
        internal const string WindowIdentifier = "crankyant/scene-patcher/inspector";
        public override string Identifier => WindowIdentifier;
        public override WindowConfig Config => WindowConfig.Resizable(new Vector2Int(NativeEditorPresentation.DefaultWidth, NativeEditorPresentation.DefaultHeight), new Vector2Int(NativeEditorPresentation.MinimumWidth, NativeEditorPresentation.MinimumHeight), Window.Position.UpperLeft);
        protected override string Title { get; set; } = "Scene patches";
        internal Window NativeWindow => Window;
        protected override void BuildContent(UIPanelBuilder builder) { Build?.Invoke(builder); }
        internal void Refresh() { RebuildPanel(); }
    }

    internal sealed class NativeEditorPresentation : IDisposable {
        // Canvas units. The default width holds the three outliner filters beside the four tab titles, which is wider
        // than the native Map Editor window. The minimum keeps the outliner, the inspector's minimum and the footer in
        // view, and the band's buttons and states at their own widths: at 620 they needed about 24 more and overlapped; its height fits below the Map Editor window on a 1080-high screen.
        internal const int DefaultWidth = 690, DefaultHeight = 660, MinimumWidth = 660, MinimumHeight = 410;
        // The larger side of a native strip icon's drawing covers this share of its 128-pixel texture: the median of the
        // nine native icons (94 to 128 pixels).
        const float NativeIconFill = 118f / 128f;
        // The space above the drawing, as a share of the frame: the native median of 2 of 128 texture pixels, plus one
        // of the button's 32 canvas units. The drawing therefore sits one unit below its neighbours' top line. As a
        // share of the frame it scales with the UI scale.
        // The space below the drawing, in the button's 32 canvas units: with it the drawing's bottom line lies on its
        // neighbours' bottom line, as measured in the game at UI scale 1.0 and 1.25. The top follows from the drawing's
        // height. As a share of the frame it scales with the UI scale.
        const float IconBottom = 1.4f / 32f;
        internal const float DividerWidth = 6;
        readonly ScenePatchWindow window;
        GameObject entry;
        UIPanel entryPanel;
        // The entry is an icon among the native icons of the top-right strip. When the strip cannot take it, a floating
        // entry button replaces it and EntryProblem names why.
        bool stripEntry;
        Sprite entrySprite;
        Texture2D entryTexture;
        bool hadSavedBounds;
        internal string EntryProblem { get; private set; }
        bool openOnTick;
        // The window's "?" in the title bar beside the close button. The owner supplies what it toggles and whether its
        // help is open; while it is, the button reads "x". TitleHelpProblem says why the title bar could not take it;
        // the owner then keeps a "?" in the command band.
        internal Action WindowHelp;
        internal Func<bool> WindowHelpOpen;
        internal string TitleHelpProblem { get; private set; }
        internal bool TitleHelp => titleHelp != null;
        RectTransform titleHelp, titleClose;
        TMP_Text titleHelpText;
        void AddTitleHelp(WindowManager manager) {
            try {
                var native = window.NativeWindow;
                var content = native.contentRectTransform;
                // The title bar's own button: the one button of the window that is not part of its content.
                var close = native.GetComponentsInChildren<Button>(true).FirstOrDefault(b => content == null || !b.transform.IsChildOf(content));
                if (close == null) { TitleHelpProblem = "the window has no close button to stand beside"; return; }
                var created = manager.builderAssets.CreateButton(UIBuilderAssets.ButtonStyle.Compact, close.transform.parent, () => { if (!disposed) { WindowHelp?.Invoke(); dirty = true; } });
                titleHelp = created.Item1; titleHelpText = created.Item2; titleClose = (RectTransform)close.transform;
                titleHelp.name = "CrankyAnt Scene Patcher window help";
                // The title bar may lay its children out; this button is placed beside the close button instead.
                var layout = titleHelp.GetComponent<LayoutElement>() ?? titleHelp.gameObject.AddComponent<LayoutElement>();
                layout.ignoreLayout = true;
                var fitter = titleHelp.GetComponent<ContentSizeFitter>();
                if (fitter != null) fitter.enabled = false;
                titleHelp.anchorMin = titleHelp.anchorMax = new Vector2(0.5f, 0.5f); titleHelp.pivot = new Vector2(1, 0.5f);
                if (titleHelpText != null) { titleHelpText.richText = false; titleHelpText.text = "?"; }
                var tooltip = titleHelp.GetComponent<UI.Tooltips.UITooltipProvider>() ?? titleHelp.gameObject.AddComponent<UI.Tooltips.UITooltipProvider>();
                tooltip.DynamicTooltipInfo = () => new TooltipInfo("Window help", WindowHelpOpen != null && WindowHelpOpen() ? "Close the help for the whole window." : "Explain the whole Scene patches window.");
                titleHelp.SetAsLastSibling();
                PlaceTitleHelp();
            } catch (Exception e) {
                if (titleHelp != null) UnityEngine.Object.Destroy(titleHelp.gameObject);
                titleHelp = null; titleHelpText = null;
                TitleHelpProblem = e.GetBaseException().Message;
            }
        }
        // Directly left of the close button, as high as it, in the close button's own parent.
        void PlaceTitleHelp() {
            if (titleHelp == null || titleClose == null) return;
            var parent = titleHelp.parent as RectTransform;
            if (parent == null) return;
            var corners = new Vector3[4];
            titleClose.GetWorldCorners(corners);
            Vector3 lowerLeft = parent.InverseTransformPoint(corners[0]), upperLeft = parent.InverseTransformPoint(corners[1]);
            var height = Mathf.Max(20, upperLeft.y - lowerLeft.y);
            titleHelp.sizeDelta = new Vector2(Mathf.Max(24, height), height);
            titleHelp.localPosition = new Vector3(lowerLeft.x - 4, (lowerLeft.y + upperLeft.y) * 0.5f, 0);
            if (titleHelpText != null) { var label = WindowHelpOpen != null && WindowHelpOpen() ? "x" : "?"; if (titleHelpText.text != label) titleHelpText.text = label; }
        }
        // Whether this is the window's first open: no bounds were saved for it.
        internal bool FirstOpen => !hadSavedBounds;
        // The width of the window's content, in canvas units.
        internal float ContentWidth => window == null ? DefaultWidth : Mathf.Max(0, ((RectTransform)window.transform).rect.width - 24);
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
        float? restoreScroll;
        string entryCaption = "Scene Patcher", entryReason = "";
        internal bool IsOpen => !disposed && window != null && window.NativeWindow.IsShown;
        internal Rect PanelBounds => IsOpen ? Bounds((RectTransform)window.transform) : Rect.zero;
        Rect EntryLayoutBounds => !disposed && entry != null && entry.activeInHierarchy ? Bounds((RectTransform)entry.transform) : Rect.zero;
        internal Rect EntryBounds {
            get {
                if (disposed || entry == null || !entry.activeInHierarchy) return Rect.zero;
                // The layout slot is wider than the compact button. Share only the actual
                // painted button/label bounds with world clipping and input exclusion.
                var result = Rect.zero;
                foreach (var graphic in entry.GetComponentsInChildren<Graphic>()) {
                    var rect = PaintBounds(graphic);
                    if (rect.width <= 0 || rect.height <= 0) continue;
                    result = result.width == 0 ? rect : Rect.MinMaxRect(Mathf.Min(result.xMin, rect.xMin),
                        Mathf.Min(result.yMin, rect.yMin), Mathf.Max(result.xMax, rect.xMax), Mathf.Max(result.yMax, rect.yMax));
                }
                return result;
            }
        }

        internal NativeEditorPresentation(Action<UIPanelBuilder> build, Action onClosed) {
            content = build; closed = onClosed;
            var manager = WindowManager.Shared;
            if (manager == null || manager.windowPrefab == null || manager.builderAssets == null ||
                manager.builderAssets.buttonCompact == null || manager.builderAssets.buttonSelected == null ||
                manager.builderAssets.inputField == null || manager.builderAssets.filterField == null ||
                manager.builderAssets.labelControl == null || manager.builderAssets.labelEmpty == null || manager.builderAssets.sectionHeader == null ||
                manager.builderAssets.fieldRow == null || manager.builderAssets.tabView == null || manager.builderAssets.segment.left == null ||
                manager.builderAssets.segmentSelected.left == null || manager.builderAssets.scrollRectVertical == null)
                throw new InvalidOperationException("Native editor window or control assets unavailable");
            try {
                // Read before the window exists: the native window saves its bounds as soon as it is shown.
                var saved = Game.GlobalPreferences.GetString("window." + ScenePatchWindow.WindowIdentifier);
                hadSavedBounds = !String.IsNullOrEmpty(saved);
                var savedShown = false;
                try { savedShown = hadSavedBounds && (bool?)Newtonsoft.Json.Linq.JObject.Parse(saved)["Shown"] == true; } catch { }
                window = manager.CreateWindowWithComponent<ScenePatchWindow>();
                RestoreSavedSize(saved);
                window.Build = BuildContent;
                window.NativeWindow.DelegateRequestClose = Close;
                window.NativeWindow.OnShownDidChange += ShownChanged;
                try { entry = StripEntry(manager); stripEntry = true; }
                catch (Exception e) {
                    EntryProblem = "The entry icon could not join the native top-right strip (" + e.GetBaseException().Message + "); a floating entry button replaces it.";
                    if (entrySprite != null) UnityEngine.Object.Destroy(entrySprite);
                    if (entryTexture != null) UnityEngine.Object.Destroy(entryTexture);
                    entrySprite = null; entryTexture = null;
                    entry = new GameObject("CrankyAnt Scene Patcher entry", typeof(RectTransform));
                    entry.layer = manager.windowPrefab.gameObject.layer;
                    entry.transform.SetParent(manager.transform, false);
                    entryPanel = UIPanel.Create((RectTransform)entry.transform, manager.builderAssets, b =>
                        b.AddButtonCompact(() => entryCaption, Toggle).Tooltip(() => new TooltipInfo(entryCaption, entryReason)));
                }
                AddTitleHelp(manager);
                Layout(true);
                // The native window saves whether it was shown; a panel that was open when the editor was left opens again.
                // The owner builds the content through this presentation, which it holds only after this constructor has
                // returned: the window opens on the first tick, not here.
                openOnTick = savedShown;
            } catch { Dispose(); throw; }
        }

        // The native window saves its content size and, when it restores, raises that size to the configured minimum,
        // which this window means for the whole window: a window saved at 660 by 410 came back 684 by 456. The saved
        // content size is applied again here, unraised. The window grows from the corner the native restore placed, so
        // its position stays.
        void RestoreSavedSize(string saved) {
            if (String.IsNullOrEmpty(saved)) return;
            try {
                var size = Newtonsoft.Json.Linq.JObject.Parse(saved)["Size"] as Newtonsoft.Json.Linq.JObject;
                if (size == null) return;
                float width = (float)size["x"], height = (float)size["y"];
                if (!(width > 0) || !(height > 0)) return;
                var native = window.NativeWindow;
                // The frame is what the window adds around its content: its own size less the content's.
                var frame = ((RectTransform)native.transform).rect.size - native.GetContentSize();
                native.SetContentSize(new Vector2(CommandBand.RestoredContent(width, MinimumWidth, frame.x), CommandBand.RestoredContent(height, MinimumHeight, frame.y)));
            } catch (Exception e) { SaveProblem = "the saved size could not be restored: " + e.GetBaseException().Message; }
        }
        // Components a clone of a native strip button may keep. Native scripts that observe game state or open native
        // windows are removed before the clone becomes active.
        static readonly HashSet<string> EntryComponents = new HashSet<string>(StringComparer.Ordinal) {
            "UnityEngine.Transform", "UnityEngine.RectTransform", "UnityEngine.CanvasRenderer", "UnityEngine.UI.Button", "UnityEngine.UI.Image",
            "UnityEngine.UI.LayoutElement", "UnityEngine.UI.Outline", "UnityEngine.UI.Shadow", "UI.Tooltips.UITooltipProvider" };
        // The icon ships beside the assembly, so another drawing replaces it without a rebuild.
        static string IconPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(NativeEditorPresentation).Assembly.Location) ?? "", "Icons", "SceneWrench.png");
        // The part of the icon texture the button shows: a square centred on the drawing, sized so the drawing fills the
        // button as the native icons fill theirs. The drawing itself is untouched; a PNG with uneven transparent margins
        // would otherwise sit off-centre and smaller than its neighbours.
        static Rect IconFrame(Texture2D texture) {
            int width = texture.width, height = texture.height, minX = width, minY = height, maxX = -1, maxY = -1;
            var pixels = texture.GetPixels32();
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++) {
                    if (pixels[y * width + x].a <= 8) continue;
                    if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            if (maxX < 0) return new Rect(0, 0, width, height);
            float drawing = Mathf.Max(maxX - minX + 1, maxY - minY + 1);
            float centerX = (minX + maxX + 1) * 0.5f, centerY = (minY + maxY + 1) * 0.5f;
            // No larger than the texture allows around the drawing's centre, and never smaller than the drawing.
            var side = Mathf.Min(drawing / NativeIconFill, 2 * Mathf.Min(Mathf.Min(centerX, width - centerX), Mathf.Min(centerY, height - centerY)));
            side = Mathf.Max(side, drawing);
            // Texture rows count from the bottom. The frame's top edge sits the top space above the drawing;
            // horizontally the drawing is centred.
            // Texture rows count from the bottom. The frame's bottom edge sits the bottom space below the drawing; when
            // the texture has too few rows above the drawing for a square frame, the frame is a little smaller.
            var bottom = Mathf.Max(0, minY - IconBottom * side);
            side = Mathf.Max(drawing, Mathf.Min(side, height - bottom));
            var frame = new Rect(Mathf.Clamp(centerX - side * 0.5f, 0, Mathf.Max(0, width - side)), bottom, Mathf.Min(side, width), Mathf.Min(side, height - bottom));
            return frame;
        }
        // An icon button among the native icons of the top-right strip: a clone of a native icon button with the native
        // tooltip, placed by the strip's own layout after the last native icon. It exists while a map editor is attached,
        // so it is absent in normal play.
        GameObject StripEntry(WindowManager manager) {
            var area = UnityEngine.Object.FindAnyObjectByType<UI.TopRightArea>();
            if (area == null) throw new InvalidOperationException("the strip was not found");
            var strip = area.GetComponentsInChildren<Button>(true).Where(b => b.transform.parent != null).GroupBy(b => b.transform.parent)
                .OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();
            if (strip == null || strip.GetComponent<LayoutGroup>() == null) throw new InvalidOperationException("the strip has no laid-out buttons");
            var children = Enumerable.Range(0, strip.childCount).Select(i => strip.GetChild(i)).ToArray();
            bool IsIcon(Transform child) => child.GetComponent<Button>() != null && child.GetComponentsInChildren<TMP_Text>(true).Length == 0;
            var template = children.FirstOrDefault(child => IsIcon(child) && child.gameObject.activeInHierarchy) ?? children.FirstOrDefault(IsIcon);
            if (template == null) throw new InvalidOperationException("the strip has no icon button to follow");
            var bytes = System.IO.File.ReadAllBytes(IconPath);
            entryTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "CrankyAnt Scene Patcher icon", hideFlags = HideFlags.DontSave };
            // The image module targets a newer framework surface than this assembly references; call it through reflection.
            var load = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
            if (load == null || !(bool)load.Invoke(null, new object[] { entryTexture, bytes, false })) throw new InvalidOperationException("the icon could not be read");
            entrySprite = Sprite.Create(entryTexture, IconFrame(entryTexture), new Vector2(0.5f, 0.5f), 100f);
            entrySprite.name = entryTexture.name; entrySprite.hideFlags = HideFlags.DontSave;
            // Instantiate under an inactive holder: no Awake or OnEnable of a native script runs on the clone.
            var holder = new GameObject("CrankyAnt Scene Patcher entry holder"); holder.SetActive(false); holder.transform.SetParent(manager.transform, false);
            GameObject clone = null;
            try {
                clone = UnityEngine.Object.Instantiate(template.gameObject, holder.transform, false);
                clone.name = "CrankyAnt Scene Patcher entry";
                for (var pass = 0; pass < 4; pass++) {
                    var extra = clone.GetComponentsInChildren<Component>(true).Where(c => c != null && !EntryComponents.Contains(c.GetType().FullName)).ToArray();
                    if (extra.Length == 0) break;
                    foreach (var component in extra) try { UnityEngine.Object.DestroyImmediate(component); } catch { }
                }
                var left = clone.GetComponentsInChildren<Component>(true).Where(c => c != null && !EntryComponents.Contains(c.GetType().FullName)).Select(c => c.GetType().Name).ToArray();
                if (left.Length != 0) throw new InvalidOperationException("native components could not be removed: " + String.Join(", ", left));
                var button = clone.GetComponent<Button>();
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(Toggle);
                button.interactable = true;
                var tooltip = clone.GetComponentInChildren<UI.Tooltips.UITooltipProvider>(true) ?? clone.AddComponent<UI.Tooltips.UITooltipProvider>();
                tooltip.DynamicTooltipInfo = () => new TooltipInfo(entryCaption, entryReason);
                var images = clone.GetComponentsInChildren<Image>(true);
                var icons = images.Where(image => image != button.targetGraphic).ToArray();
                foreach (var image in icons.Length == 0 ? images : icons) { image.sprite = entrySprite; image.color = Color.white; }
                // After the last icon button that precedes the first text button, such as the balance and the clock.
                var index = strip.childCount;
                var firstText = Array.FindIndex(children, child => child.GetComponent<Button>() != null && !IsIcon(child));
                var lastIcon = Array.FindLastIndex(children, firstText < 0 ? children.Length - 1 : firstText, child => IsIcon(child));
                if (lastIcon >= 0) index = lastIcon + 1; else if (firstText >= 0) index = firstText;
                clone.transform.SetParent(strip, false);
                clone.transform.SetSiblingIndex(Mathf.Min(index, strip.childCount - 1));
                clone.SetActive(true);
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)strip);
                return clone;
            } catch { if (clone != null) UnityEngine.Object.Destroy(clone); throw; }
            finally { UnityEngine.Object.Destroy(holder); }
        }

        // The window itself does not scroll: the layout owns its columns, and each scrolling region is built with Scroll.
        void BuildContent(UIPanelBuilder builder) { scroll = null; content(builder); }
        // The mod-built divider between two columns: dragging it sets the width of the column on its left. The native
        // builders have no splitter; this control is the recorded exception to native construction.
        ColumnDivider divider;
        Func<float> dividerWidth;
        internal void Divider(UIPanelBuilder builder, LayoutElement column, float minimum, Func<float> maximum, Func<float> desired, Action<float> changed) {
            var rule = builder.AddVRule().Width(DividerWidth);
            var image = rule.GetComponent<Image>();
            if (image != null) image.raycastTarget = true;
            var drag = rule.gameObject.AddComponent<ColumnDivider>();
            drag.Column = column; drag.Minimum = minimum; drag.Maximum = maximum; drag.Changed = changed;
            divider = drag; dividerWidth = desired;
            rule.Tooltip("Outliner width", "Drag to resize the outliner.");
        }
        // Forgets the saved bounds and places the window at its default position again.
        internal void ResetLayout() {
            try { Game.GlobalPreferences.SetString("window." + ScenePatchWindow.WindowIdentifier, ""); } catch { }
            // The request comes from a button press, and layout moves nothing while a button is down: the next layout
            // after the release places the window.
            hadSavedBounds = false; panelPlaced = false; nextLayout = 0;
        }
        // A native vertical scroll region that takes the remaining height and never less than minimumHeight. A content
        // refresh keeps the distance scrolled from the top of the region built last.
        internal RectTransform Scroll(UIPanelBuilder builder, float minimumHeight, Action<UIPanelBuilder> body, bool keepPosition = true) {
            var view = NativeScroll(builder, body);
            if (keepPosition) scroll = view.GetComponent<ScrollRect>();
            view.GetComponent<LayoutElement>().minHeight = minimumHeight;
            return view;
        }
        static RectTransform NativeScroll(UIPanelBuilder builder, Action<UIPanelBuilder> body) {
            var view = builder.VScrollView(body);
            // The game sets its wheel step on scroll views through this component.
            if (view.GetComponent<Game.Settings.ScrollRectSettingsApplicator>() == null) view.gameObject.AddComponent<Game.Settings.ScrollRectSettingsApplicator>();
            return view;
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
        void ShownChanged(bool shown) { boundsFrame = -1; if (!shown && !disposed) { ClearFocus(); closed(); } }
        internal void Tick() {
            if (disposed) return;
            if (openOnTick) { openOnTick = false; if (!IsOpen) Toggle(); }
            Layout(false);
            if (IsOpen) PlaceTitleHelp();
            // The column shows the user's width within what the window allows now. The content is built before the window
            // has its size, and the window can be resized afterwards, so the limit is applied every frame.
            if (IsOpen && divider != null && divider.Column != null && dividerWidth != null && !Input.GetMouseButton(0)) {
                var width = Mathf.Clamp(dividerWidth(), divider.Minimum, Mathf.Max(divider.Minimum, divider.Maximum == null ? divider.Minimum : divider.Maximum()));
                if (Mathf.Abs(divider.Column.preferredWidth - width) > 0.5f) { divider.Column.minWidth = width; divider.Column.preferredWidth = width; }
            }
            if (restoreScroll.HasValue && scroll != null) {
                Canvas.ForceUpdateCanvases();
                // A distance from the top survives a change of content height; a normalized position does not.
                var body = scroll.content; var view = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
                body.anchoredPosition = new Vector2(body.anchoredPosition.x, Mathf.Clamp(restoreScroll.Value, 0, Mathf.Max(0, body.rect.height - view.rect.height)));
                restoreScroll = null;
            }
            // A background freshness/history update must not destroy an active text edit or pointer drag. The input
            // fields are searched only when a refresh is due.
            if (dirty && IsOpen && !Input.GetMouseButton(0) && !window.GetComponentsInChildren<TMP_InputField>().Any(f => f.isFocused)) {
                restoreScroll = scroll == null ? (float?)null : scroll.content.anchoredPosition.y;
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
        // Collecting them walks every UI graphic, so one collection serves a frame. While a pointer button is down, when
        // windows move, and after this window was placed, the next use collects again; otherwise at most ten times a
        // second, which is how late world markings can follow native UI that opens or closes by itself.
        internal IReadOnlyList<Rect> WorldUiBounds() {
            var frame = Time.frameCount;
            if (frame == boundsFrame) return worldUiBounds;
            var pointerActive = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2) || Input.GetMouseButtonUp(0);
            if (boundsFrame >= 0 && !pointerActive && Time.unscaledTime < boundsUntil) return worldUiBounds;
            worldUiBounds.Clear();
            CollectVisibleUi(worldUiBounds, true);
            boundsFrame = frame; boundsUntil = Time.unscaledTime + 0.1f;
            return worldUiBounds;
        }
        int boundsFrame = -1;
        float boundsUntil;
        void CollectVisibleUi(List<Rect> bounds, bool includeOwned) {
            // This is the final screen-UI geometry for layout and world drawing/picking.
            // Offscreen decal/measurement canvases must not become phantom UI exclusions.
            if (includeOwned) {
                if (window != null && ScreenCanvas(window.GetComponentInParent<Canvas>())) AddBounds(bounds, PanelBounds);
                if (entry != null && ScreenCanvas(entry.GetComponentInParent<Canvas>())) AddBounds(bounds, EntryBounds);
            }
            var manager = WindowManager.Shared;
            if (manager != null) foreach (var native in manager.GetComponentsInChildren<Window>()) {
                if (!native.IsShown || !native.gameObject.activeInHierarchy || (!includeOwned && Owns(native.gameObject)) ||
                    !ScreenCanvas(native.GetComponentInParent<Canvas>()) ||
                    native.GetComponentsInParent<CanvasGroup>().Any(group => group.alpha == 0)) continue;
                AddBounds(bounds, Bounds((RectTransform)native.transform));
            }
            foreach (var graphic in UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsSortMode.InstanceID)) {
                // Window chrome already supplies its complete outer rectangle. Masked content must
                // never extend that rectangle into the world or another window.
                if (graphic.GetComponentInParent<Window>() != null) continue;
                var rootCanvas = graphic.canvas == null ? null : graphic.canvas.rootCanvas;
                if (!graphic.raycastTarget ||
                    (!includeOwned && Owns(graphic.gameObject)) || graphic.GetComponentInParent<InspectionInputBlocker>() != null ||
                    rootCanvas == null) continue;
                var rect = PaintBounds(graphic);
                if (rect.width <= 0 || rect.height <= 0) continue;
                // A tooltip's CanvasGroup does not block raycasts. Its visible background must not
                // move the very entry being hovered. Use Unity's actual raycast filters for layout.
                if (!includeOwned) {
                    var camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
                    var point = new Vector2(rect.center.x, Screen.height - rect.center.y);
                    if (!graphic.Raycast(point, camera)) continue;
                }
                // Full-screen input surfaces are not chrome; visible windows were included above.
                if (rect.width * rect.height < Screen.width * Screen.height * 0.5f) AddBounds(bounds, rect);
            }
        }
        static Rect PaintBounds(Graphic graphic) {
            if (!graphic.isActiveAndEnabled || graphic.color.a == 0 || !ScreenCanvas(graphic.canvas) ||
                graphic.canvasRenderer.cull || graphic.canvasRenderer.GetInheritedAlpha() == 0) return Rect.zero;
            var rect = Bounds(graphic.rectTransform);
            foreach (var mask in graphic.GetComponentsInParent<RectMask2D>())
                if (mask.isActiveAndEnabled) rect = Intersection(rect, Bounds(mask.rectTransform));
            foreach (var mask in graphic.GetComponentsInParent<Mask>())
                if (mask.isActiveAndEnabled) rect = Intersection(rect, Bounds(mask.rectTransform));
            return rect;
        }
        static bool ScreenCanvas(Canvas canvas) {
            if (canvas == null || !canvas.isActiveAndEnabled) return false;
            var root = canvas.rootCanvas;
            if (!root.isActiveAndEnabled || root.renderMode == RenderMode.WorldSpace) return false;
            if (root.renderMode == RenderMode.ScreenSpaceOverlay) return true;
            var camera = root.worldCamera;
            return camera != null && camera.isActiveAndEnabled && camera.targetTexture == null;
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
            if (canvas != null) canvas = canvas.rootCanvas;
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
            if (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)) return;
            nextLayout = Time.unscaledTime + 0.5f;
            screen = size; scale = currentScale;
            boundsFrame = -1;
            float margin = 12 * scale, gap = 8 * scale;
            if (!stripEntry) {
                chrome.Clear();
                CollectVisibleUi(chrome, false);
                float entryWidth = 180 * scale, entryHeight = 30 * scale;
                var previousEntry = EntryLayoutBounds;
                var entryRect = new Rect(Screen.width - entryWidth - margin, previousEntry.height > 0 ? previousEntry.y : margin, entryWidth, entryHeight);
                if (!Safe(entryRect, margin)) {
                    entryRect.y = margin;
                    while (entryRect.yMax < Screen.height - margin && chrome.Any(r => r.Overlaps(entryRect))) entryRect.y += entryHeight + gap;
                }
                bool entryFits = Safe(entryRect, margin);
                entry.SetActive(entryFits);
                if (!entryFits) { if (IsOpen) Close(); return; }
                Place((RectTransform)entry.transform, entryRect);
            }
            if (!IsOpen) return;
            var current = PanelBounds;
            bool onScreen = current.width > 0 && current.height > 0 && current.xMin >= 0 && current.yMin >= 0 && current.xMax <= Screen.width && current.yMax <= Screen.height;
            // A placed window stays where it is, as native windows do: native UI that opens over it, chrome rebuilds and
            // camera moves do not move or resize it. Bounds the native window restored from an earlier session win over
            // the default. Place it again only when it no longer lies within the screen.
            if (onScreen && (panelPlaced || hadSavedBounds)) { panelPlaced = true; return; }
            Place((RectTransform)window.transform, DefaultBounds());
            panelPlaced = true;
            SaveBounds();
        }
        // The native window saves its bounds when the user moves or resizes it and when it is shown or closed. It is
        // shown before this layout places it, so without a move by the user the saved position would stay the window's
        // initial one, the top-left corner, and the next editor session would restore that. Raising the window's own
        // moved event makes the native persistence save the placed bounds in its own format.
        void SaveBounds() {
            try {
                var native = window.NativeWindow;
                var moved = typeof(Window).GetField("OnDidPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(native) as Action<Vector2>;
                if (moved == null) { SaveProblem = "the native window exposes no moved event"; return; }
                moved(native.GetPosition());
                SaveProblem = null;
            } catch (Exception e) { SaveProblem = e.GetBaseException().Message; }
        }
        // Why the default placement could not be saved, or null. The window then opens at its initial position in the
        // next editor session until the user moves it.
        internal string SaveProblem { get; private set; }
        // Where the window opens while no bounds are saved: PanelPlacement decides between the space below the native Map
        // Editor window and the top right of the screen. The lower edge of what occupies that corner, the button strip
        // and the status readout below it, is read from the visible native UI.
        Rect DefaultBounds() {
            var native = NativeEditorBounds();
            var width = Mathf.Min(Mathf.Max(DefaultWidth * scale, native.width), Screen.width);
            chrome.Clear();
            CollectVisibleUi(chrome, false);
            // Only what hangs from the top of the screen in the window's column counts, not a window further down.
            var edge = PanelPlacement.OccupiedBelow(Screen.width - width, Screen.width, chrome.Select(r => new PanelPlacement.Area { X = r.xMin, Y = r.yMin, Width = r.width, Height = r.height }), Screen.height * 0.25f);
            var area = PanelPlacement.Default(Screen.width, Screen.height, scale, native.xMin, native.width, native.yMax, edge, DefaultWidth, DefaultHeight, MinimumHeight);
            DefaultAtTopRight = area.TopRight;
            return new Rect(area.X, area.Y, area.Width, area.Height);
        }
        // Whether the last default placement took the top right of the screen.
        internal bool DefaultAtTopRight { get; private set; }
        static Rect NativeEditorBounds() {
            try {
                var type = Native.Type("Game.MapEditor.MapEditorWindow");
                var editor = type == null ? null : UnityEngine.Object.FindAnyObjectByType(type) as Component;
                var native = editor == null ? null : editor.GetComponent<Window>();
                return native != null && native.IsShown ? Bounds((RectTransform)native.transform) : Rect.zero;
            } catch { return Rect.zero; }
        }
        static Rect Intersection(Rect a, Rect b) {
            var min = Vector2.Max(a.min, b.min); var max = Vector2.Min(a.max, b.max);
            return new Rect(min, Vector2.Max(Vector2.zero, max - min));
        }
        bool Safe(Rect rect, float margin) => rect.width > 0 && rect.height > 0 && rect.xMin >= margin && rect.yMin >= margin &&
            rect.xMax <= Screen.width - margin && rect.yMax <= Screen.height - margin && !chrome.Any(r => r.Overlaps(rect));
        static void Place(RectTransform transform, Rect pixels) {
            transform.anchorMin = transform.anchorMax = transform.pivot = new Vector2(0, 1);
            var s = transform.lossyScale;
            transform.sizeDelta = new Vector2(pixels.width / s.x, pixels.height / s.y);
            transform.position = new Vector3(pixels.x, Screen.height - pixels.y, 0);
        }
        // The game's tooltip is one callout that its controller fades out when the pointer leaves. When the editor is left
        // while a tooltip of this window shows, that controller stops and the callout stays as it was. Its alpha is set
        // to zero when the window goes; a running controller fades it in again for whatever the pointer rests on next.
        static void HideTooltip() {
            try {
                var controller = UI.Tooltips.UITooltipController.Shared;
                if (controller == null) return;
                var group = typeof(UI.Tooltips.UITooltipController).GetField("_calloutCanvasGroup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(controller) as CanvasGroup;
                if (group != null) group.alpha = 0;
            } catch { }
        }
        public void Dispose() {
            if (disposed) return;
            HideTooltip();
            ClearFocus(); disposed = true;
            entryPanel?.Dispose();
            if (entry != null) { entry.SetActive(false); UnityEngine.Object.Destroy(entry); }
            if (entrySprite != null) UnityEngine.Object.Destroy(entrySprite);
            if (entryTexture != null) UnityEngine.Object.Destroy(entryTexture);
            if (window != null) {
                window.Build = null;
                window.NativeWindow.DelegateRequestClose = null;
                window.NativeWindow.OnShownDidChange -= ShownChanged;
                window.gameObject.SetActive(false); UnityEngine.Object.Destroy(window.gameObject);
            }
        }
    }

    // Drags the divider between two columns. The column keeps the dragged width without a content rebuild; the owner
    // stores it.
    internal sealed class ColumnDivider : MonoBehaviour, IBeginDragHandler, IDragHandler {
        internal LayoutElement Column;
        internal float Minimum;
        internal Func<float> Maximum;
        internal Action<float> Changed;
        float start;
        public void OnBeginDrag(PointerEventData data) { start = Column == null ? 0 : Column.preferredWidth; }
        public void OnDrag(PointerEventData data) {
            if (Column == null) return;
            var canvas = GetComponentInParent<Canvas>();
            var scale = canvas == null || canvas.scaleFactor <= 0 ? 1 : canvas.scaleFactor;
            var width = Mathf.Clamp(start + (data.position.x - data.pressPosition.x) / scale, Minimum, Mathf.Max(Minimum, Maximum == null ? Minimum : Maximum()));
            Column.minWidth = width; Column.preferredWidth = width;
            Changed?.Invoke(width);
        }
    }
}
