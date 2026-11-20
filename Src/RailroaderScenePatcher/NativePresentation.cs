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
        public override WindowConfig Config => WindowConfig.Resizable(new Vector2Int(NativePresentation.DefaultWidth, NativePresentation.DefaultHeight), new Vector2Int(NativePresentation.MinimumWidth, NativePresentation.MinimumHeight), Window.Position.UpperLeft);
        protected override string Title { get; set; } = "Scene patches";
        internal Window NativeWindow => Window;
        protected override void BuildContent(UIPanelBuilder builder) { Build?.Invoke(builder); }
        internal void Refresh() { RebuildPanel(); }
    }

    // The session details: a native builder window of its own, movable and resizable, with bounds the game saves
    // under its own key.
    public sealed class SessionDetailsWindow : BuilderWindowBase {
        internal Action<UIPanelBuilder> Build;
        internal const string WindowIdentifier = "crankyant/scene-patcher/session-details";
        internal const int DefaultWidth = 420, DefaultHeight = 420;
        // The least size at which the scene patch file section shows a diagnostic beside its buttons without much scrolling.
        internal const int MinimumWidth = 360, MinimumHeight = 320;
        public override string Identifier => WindowIdentifier;
        public override WindowConfig Config => WindowConfig.Resizable(new Vector2Int(DefaultWidth, DefaultHeight), new Vector2Int(MinimumWidth, MinimumHeight), Window.Position.UpperLeft);
        protected override string Title { get; set; } = "Session details";
        internal Window NativeWindow => Window;
        protected override void BuildContent(UIPanelBuilder builder) { Build?.Invoke(builder); }
        internal void Refresh() { RebuildPanel(); }
    }

    // The context help of one region: a native builder window of its own beside the "?" that opened it.
    public sealed class HelpWindow : BuilderWindowBase {
        internal Action<UIPanelBuilder> Build;
        internal const string WindowIdentifier = "crankyant/scene-patcher/help";
        // Width is the content's: the window's frame adds 24, so the window measures 364.
        internal const int Width = 340, DefaultHeight = 300, MinimumHeight = 120;
        public override string Identifier => WindowIdentifier;
        public override WindowConfig Config => WindowConfig.Resizable(new Vector2Int(Width, DefaultHeight), new Vector2Int(260, MinimumHeight), Window.Position.UpperLeft);
        protected override string Title { get; set; } = "Scene patches help";
        internal Window NativeWindow => Window;
        protected override void BuildContent(UIPanelBuilder builder) { Build?.Invoke(builder); }
        internal void Refresh() { RebuildPanel(); }
    }

    internal sealed class NativePresentation : IDisposable {
        // Canvas units. The default width holds the outliner filters beside the four tab titles, which is wider
        // than the native Map Editor window. The minimum keeps the outliner, the inspector's minimum and the footer in
        // view, and the band's buttons and states at their own widths: at 620 they needed about 24 more and overlapped; its height fits below the Map Editor window on a 1080-high screen.
        // The widths follow the outliner's: 296 for its four filters, the divider and row spacing of 32, the inspector's
        // 382 by default and 350 at the least, and the window's frame of 24.
        internal const int DefaultWidth = 734, DefaultHeight = 660, MinimumWidth = 704, MinimumHeight = 470;
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
        // The title bar holds, left of the close button and from right to left, the window's "?", the Reset layout icon
        // and the Session details icon: bare glyphs in the close glyph's colour, without a button face, each named in
        // its tooltip. TitleHelpProblem says why the title bar could
        // not take the "?"; the owner then keeps a "?" in the command band. TitleReset and TitleDetails say whether
        // each icon stands in the title bar; without one the owner keeps a button for it in the window help.
        internal Action ResetRequested;
        internal string TitleHelpProblem { get; private set; }
        // Why the Reset layout or the Session details icon could not join the title bar, or null.
        internal string TitleIconProblem { get; private set; }
        internal bool TitleHelp => titleHelp != null;
        internal bool TitleReset => titleReset != null;
        internal bool TitleDetails => titleDetails != null;
        RectTransform titleHelp, titleReset, titleDetails, titleClose;
        // Why the window's own controls are unavailable while the file panel is open, or null. The session details stay
        // available: the Repair panel's Details opens them. The window help stays available too, so the help is at hand
        // while a version choice, repair or import waits for a decision; a region's help closes with its region.
        string lockReason;
        bool repairMode => lockReason != null;
        internal void SetFileLock(string reason) {
            lockReason = reason;
            if (reason != null && HelpRegion != WindowHelpRegion) CloseHelp();
            if (divider != null) divider.enabled = reason == null;
            SetTitleRepairControls();
        }
        void SetTitleRepairControls() {
            foreach (var rect in new[] { titleReset, titleClose }) {
                var button = rect == null ? null : rect.GetComponent<Button>();
                if (button != null) button.interactable = !repairMode;
            }
            // The native close button has no tooltip of its own; it gets one only to say why it is unavailable.
            if (titleClose != null) {
                var tooltip = titleClose.GetComponent<UI.Tooltips.UITooltipProvider>() ?? titleClose.gameObject.AddComponent<UI.Tooltips.UITooltipProvider>();
                tooltip.DynamicTooltipInfo = () => new TooltipInfo("Close", CommandBand.Tooltip(null, lockReason));
                tooltip.enabled = repairMode;
            }
        }
        Sprite resetSprite, detailsSprite;
        Texture2D resetTexture, detailsTexture;
        // The region of the window's own help.
        internal const string WindowHelpRegion = "main";
        // The colour of the native close glyph, and the brighter one of a glyph whose window is open.
        static readonly Color TitleGlyph = new Color32(0xC0, 0xAE, 0x89, 0xFF), TitleGlyphOpen = new Color32(0xFF, 0xF2, 0xD6, 0xFF);
        // A compact native button without its face: it keeps the native click and tooltip, and shows only what stands
        // on it, like the close glyph beside it.
        RectTransform BareTitleButton(WindowManager manager, Transform parent, string name, Action click, out TMP_Text text) {
            var created = manager.builderAssets.CreateButton(UIBuilderAssets.ButtonStyle.Compact, parent, () => { if (!disposed) { click?.Invoke(); dirty = true; } });
            var rect = created.Item1; text = created.Item2;
            rect.name = name;
            // The title bar may lay its children out; this button is placed beside the close button instead.
            var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            var fitter = rect.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(1, 0.5f);
            // The native compact button draws its face, the dark rounded background, with an Image on a child object,
            // which also is the button's target graphic. Every image the button came with is switched off, wherever
            // it sits, so that nothing of the face is drawn in any state; a transparent image on the button itself
            // takes the pointer instead. The glyph is added after this and is not touched.
            foreach (var face in rect.GetComponentsInChildren<Image>(true)) { face.enabled = false; face.raycastTarget = false; }
            var hit = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            hit.sprite = null; hit.color = Color.clear; hit.raycastTarget = true; hit.enabled = true;
            var button = rect.GetComponent<Button>();
            if (button != null) { button.transition = Selectable.Transition.None; button.targetGraphic = hit; }
            return rect;
        }
        // Every title-bar glyph is one size and one stroke weight, those of the native close cross beside them, which
        // measures 12 to 13 pixels across with strokes of about 2 at UI scale 1: 13 canvas units across with strokes
        // of about 2 units. A glyph texture is 56 pixels: a drawing reaches the texture's edge within a unit and its
        // strokes are 8 pixels wide. The "?" is as high in the regular weight, whose stroke is the cross's; in bold
        // it was heavier than the others.
        internal const float TitleGlyphSize = 13, TitleGlyphFont = 18;
        // A title-bar icon: a bare button with a mod-drawn glyph of 14 x 14 in the close glyph's colour. The game's
        // assets hold no sprite for a layout reset or for a page of details (searched by name: of the icon sprites only
        // GearIcon and LocateIcon exist), so both glyphs are drawn here.
        RectTransform TitleIcon(WindowManager manager, Transform parent, string name, Sprite sprite, Action click, Func<TooltipInfo> tip, out Image glyphImage) {
            var rect = BareTitleButton(manager, parent, name, click, out var unused);
            if (unused != null) unused.text = "";
            var glyph = new GameObject("Glyph", typeof(RectTransform), typeof(Image)) { layer = rect.gameObject.layer };
            var glyphRect = (RectTransform)glyph.transform;
            glyphRect.SetParent(rect, false);
            glyphRect.anchorMin = glyphRect.anchorMax = glyphRect.pivot = new Vector2(0.5f, 0.5f);
            glyphRect.sizeDelta = new Vector2(TitleGlyphSize, TitleGlyphSize);
            glyphImage = glyph.GetComponent<Image>();
            glyphImage.sprite = sprite; glyphImage.color = TitleGlyph; glyphImage.raycastTarget = false; glyphImage.preserveAspect = true;
            // The compact button lays its children out inside its padding of 6 units on each side, which left a glyph
            // 8 units of width and squeezed it. The glyph keeps its own square instead.
            (glyph.GetComponent<LayoutElement>() ?? glyph.AddComponent<LayoutElement>()).ignoreLayout = true;
            var tooltip = rect.GetComponent<UI.Tooltips.UITooltipProvider>() ?? rect.gameObject.AddComponent<UI.Tooltips.UITooltipProvider>();
            tooltip.DynamicTooltipInfo = tip;
            rect.SetAsLastSibling();
            return rect;
        }
        Image detailsGlyph;
        void AddTitleHelp(WindowManager manager) {
            Button close;
            try {
                var native = window.NativeWindow;
                var content = native.contentRectTransform;
                // The title bar's own button: the one button of the window that is not part of its content.
                close = native.GetComponentsInChildren<Button>(true).FirstOrDefault(b => content == null || !b.transform.IsChildOf(content));
                if (close == null) { TitleHelpProblem = "the window has no close button to stand beside"; return; }
                titleClose = (RectTransform)close.transform;
                titleHelp = BareTitleButton(manager, close.transform.parent, "CrankyAnt Scene Patcher window help", () => ToggleHelp(WindowHelpRegion, titleHelp), out var label);
                if (label != null) {
                    label.richText = false; label.text = "?"; label.color = TitleGlyph;
                    label.fontStyle = FontStyles.Normal; label.enableAutoSizing = false;
                    label.fontSize = TitleGlyphFont; label.alignment = TextAlignmentOptions.CenterGeoAligned;
                    label.margin = Vector4.zero;
                    if (label.rectTransform.parent == titleHelp) {
                        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                    }
                }
                var tooltip = titleHelp.GetComponent<UI.Tooltips.UITooltipProvider>() ?? titleHelp.gameObject.AddComponent<UI.Tooltips.UITooltipProvider>();
                tooltip.DynamicTooltipInfo = () => new TooltipInfo("Window help", CommandBand.Tooltip(HelpRegion == WindowHelpRegion ? "Close the help for the whole window." : "Open Scene Patcher help.", null));
                titleHelp.SetAsLastSibling();
            } catch (Exception e) {
                if (titleHelp != null) UnityEngine.Object.Destroy(titleHelp.gameObject);
                titleHelp = null;
                TitleHelpProblem = e.GetBaseException().Message;
                return;
            }
            try {
                titleReset = TitleIcon(manager, close.transform.parent, "CrankyAnt Scene Patcher reset layout", ResetGlyph(), () => ResetRequested?.Invoke(),
                    () => new TooltipInfo("Reset layout", CommandBand.Tooltip("Put the window at its default place again and restore the outliner width.", lockReason)), out _);
            } catch (Exception e) {
                if (titleReset != null) UnityEngine.Object.Destroy(titleReset.gameObject);
                titleReset = null;
                TitleIconProblem = "Reset layout: " + e.GetBaseException().Message;
            }
            try {
                titleDetails = TitleIcon(manager, close.transform.parent, "CrankyAnt Scene Patcher session details", DetailsGlyph(), ToggleDetails,
                    () => new TooltipInfo("Session details", DetailsOpen ? "Close the session details." : "Open the session details in their own window: the edited mod's scene patch file, supplied definitions, diagnostics and the candidates of a world choice."), out detailsGlyph);
            } catch (Exception e) {
                if (titleDetails != null) UnityEngine.Object.Destroy(titleDetails.gameObject);
                titleDetails = null; detailsGlyph = null;
                TitleIconProblem = (TitleIconProblem == null ? "" : TitleIconProblem + "; ") + "Session details: " + e.GetBaseException().Message;
            }
            PlaceTitleHelp();
            SetTitleRepairControls();
        }
        // Draws a glyph of 56 x 56 pixels with a soft edge, white so that the image tints it. covers says whether a
        // point of the texture, rows counted from the bottom, lies in the drawing.
        static Sprite Glyph(string name, Func<Vector2, bool> covers, out Texture2D texture) {
            const int size = 56, samples = 3;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++) {
                    var covered = 0;
                    for (var sy = 0; sy < samples; sy++)
                        for (var sx = 0; sx < samples; sx++)
                            if (covers(new Vector2(x + (sx + 0.5f) / samples, y + (sy + 0.5f) / samples))) covered++;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * covered / (samples * samples)));
                }
            texture.SetPixels32(pixels); texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name; sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
        // A round arrow: three quarters of a ring with an arrowhead at its end. The ring is 13 units across with a
        // stroke of 2 units.
        Sprite ResetGlyph() {
            if (resetSprite != null) return resetSprite;
            const float radius = 22f, half = 4f, from = 70f, to = 330f;
            var centre = new Vector2(27, 28);
            var end = to * Mathf.Deg2Rad;
            Vector2 normal = new Vector2(Mathf.Cos(end), Mathf.Sin(end)), tangent = new Vector2(-normal.y, normal.x), at = centre + normal * radius;
            Vector2 tip = at + tangent * 15f, left = at + normal * 10f, right = at - normal * 12f;
            Func<Vector2, Vector2, Vector2, float> side = (p, q, r) => (p.x - r.x) * (q.y - r.y) - (q.x - r.x) * (p.y - r.y);
            return resetSprite = Glyph("CrankyAnt Scene Patcher reset glyph", point => {
                var offset = point - centre;
                var angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
                if (angle < 0) angle += 360f;
                if (Mathf.Abs(offset.magnitude - radius) <= half && angle >= from && angle <= to) return true;
                float d1 = side(point, tip, left), d2 = side(point, left, right), d3 = side(point, right, tip);
                return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
            }, out resetTexture);
        }
        // A page: an outline as wide as the close cross, the whole glyph box, with a stroke of 2 units and two lines of
        // text.
        Sprite DetailsGlyph() {
            if (detailsSprite != null) return detailsSprite;
            return detailsSprite = Glyph("CrankyAnt Scene Patcher details glyph", point => {
                float x = point.x, y = point.y;
                var inside = x >= 1 && x <= 55 && y >= 0 && y <= 56;
                var hollow = x >= 9 && x <= 47 && y >= 8 && y <= 48;
                if (inside && !hollow) return true;
                return x >= 16 && x <= 40 && ((y >= 16 && y <= 23) || (y >= 33 && y <= 40));
            }, out detailsTexture);
        }
        // Directly left of the close button, as high as it, in the close button's own parent: the "?" first, the
        // Reset layout icon left of that, the Session details icon left of that.
        void PlaceTitleHelp() {
            if (titleHelp == null || titleClose == null) return;
            var parent = titleHelp.parent as RectTransform;
            if (parent == null) return;
            var corners = new Vector3[4];
            titleClose.GetWorldCorners(corners);
            Vector3 lowerLeft = parent.InverseTransformPoint(corners[0]), upperLeft = parent.InverseTransformPoint(corners[1]);
            var upperRight = parent.InverseTransformPoint(corners[2]);
            var height = upperLeft.y - lowerLeft.y;
            var width = upperRight.x - upperLeft.x;
            if (height <= 0 || width <= 0) return;
            var middle = (lowerLeft.y + upperLeft.y) * 0.5f;
            var right = lowerLeft.x - 2;
            foreach (var icon in new[] { titleHelp, titleReset, titleDetails }) {
                if (icon == null) continue;
                icon.sizeDelta = new Vector2(width, height);
                icon.localPosition = new Vector3(right, middle, 0);
                right -= width + 2;
            }
            if (detailsGlyph != null) { var colour = DetailsOpen ? TitleGlyphOpen : TitleGlyph; if (detailsGlyph.color != colour) detailsGlyph.color = colour; }
        }
        // The context help of a region opens in one separate, movable window beside the "?" that opened it, sized to
        // its content. It replaces nothing in the panel. The "?" of the open region closes it again, as does its own
        // close button; another "?" shows its own region in the window and moves it beside itself. The owner supplies
        // the content of a region. HelpRegion is the region whose help is open, or null; HelpProblem says why the
        // window could not open.
        internal Action<UIPanelBuilder, string> Help;
        internal string HelpRegion { get; private set; }
        internal string HelpProblem { get; private set; }
        HelpWindow help;
        Rect helpBeside;
        int helpSizing;
        internal void ToggleHelp(string region, RectTransform beside) {
            if (disposed || region == null) return;
            try {
                if (HelpRegion == region && help != null && help.NativeWindow.IsShown) { help.NativeWindow.CloseWindow(); HelpRegion = null; dirty = true; return; }
                if (help == null) {
                    help = WindowManager.Shared.CreateWindowWithComponent<HelpWindow>();
                    help.Build = builder => { if (HelpRegion != null) Help?.Invoke(builder, HelpRegion); };
                    help.NativeWindow.OnShownDidChange += HelpShown;
                }
                HelpRegion = region;
                helpBeside = beside == null ? PanelBounds : Bounds(beside);
                if (help.NativeWindow.IsShown) help.Refresh(); else help.Show();
                // The content is laid out over the next frames; the window takes its size and place after that.
                helpSizing = 2;
                boundsFrame = -1; dirty = true;
                HelpProblem = null;
            } catch (Exception e) { HelpRegion = null; HelpProblem = e.GetBaseException().Message; }
        }
        void HelpShown(bool shown) {
            boundsFrame = -1;
            if (shown || disposed) return;
            // Closed by its own close button: the "?" of its region shows "?" again.
            HelpRegion = null; dirty = true;
            helpClosed = true;
        }
        // A closed native window stays in the UI tree. The help window is cheap to make again, so it is removed once
        // it has closed, on the next tick and not inside the window's own event.
        bool helpClosed;
        void RemoveClosedHelp() {
            if (!helpClosed) return;
            helpClosed = false;
            if (help == null || help.NativeWindow.IsShown) return;
            try { help.Build = null; help.NativeWindow.OnShownDidChange -= HelpShown; help.gameObject.SetActive(false); UnityEngine.Object.Destroy(help.gameObject); } catch { }
            help = null;
        }
        // Closes the help, whatever region it shows.
        internal void CloseHelp() {
            if (disposed || help == null) return;
            try { if (help.NativeWindow.IsShown) help.NativeWindow.CloseWindow(); } catch { }
            HelpRegion = null; dirty = true;
        }
        // The help window is as high as its content, within the screen, and stands beside the panel at the height of
        // the "?" that opened it: right of the panel where the screen has room, else left of it.
        void SizeHelp() {
            if (help == null || !help.NativeWindow.IsShown) return;
            try {
                Canvas.ForceUpdateCanvases();
                var native = help.NativeWindow;
                var frame = ((RectTransform)native.transform).rect.size - native.GetContentSize();
                var view = help.GetComponentInChildren<ScrollRect>(true);
                var wanted = view != null && view.content != null ? LayoutUtility.GetPreferredHeight(view.content) + 12 : native.GetContentSize().y;
                var most = Mathf.Max(HelpWindow.MinimumHeight, Screen.height / Mathf.Max(0.01f, scale) * 0.8f - frame.y);
                native.SetContentSize(new Vector2(HelpWindow.Width, Mathf.Clamp(wanted, HelpWindow.MinimumHeight, most)));
                Canvas.ForceUpdateCanvases();
                var size = Bounds((RectTransform)native.transform).size;
                // Beside the panel, never over it: the region the help explains stays in view.
                var panel = PanelBounds;
                var area = PanelPlacement.Beside(panel.xMin, panel.xMax, helpBeside.yMin, size.x, size.y, Screen.width, Screen.height, 8 * scale);
                Place((RectTransform)native.transform, new Rect(area.X, area.Y, area.Width, area.Height));
                boundsFrame = -1;
            } catch (Exception e) { HelpProblem = e.GetBaseException().Message; }
        }
        // The session details open in their own window, made when first asked for. The owner supplies its content. It
        // opens beside the panel the first time and afterwards where the user left it, and closes with the panel.
        // DetailsProblem says why it could not open.
        internal Action<UIPanelBuilder> Details;
        internal string DetailsProblem { get; private set; }
        SessionDetailsWindow details;
        bool detailsPlaced;
        internal bool DetailsOpen => !disposed && details != null && details.NativeWindow.IsShown;
        internal void ToggleDetails() {
            if (disposed) return;
            try {
                if (DetailsOpen) { details.NativeWindow.CloseWindow(); return; }
                if (details == null) {
                    // Read before the window exists: the native window saves its bounds as soon as it is shown.
                    detailsPlaced = !String.IsNullOrEmpty(Game.GlobalPreferences.GetString("window." + SessionDetailsWindow.WindowIdentifier));
                    details = WindowManager.Shared.CreateWindowWithComponent<SessionDetailsWindow>();
                    details.Build = builder => Details?.Invoke(builder);
                }
                details.Show();
                if (!detailsPlaced) {
                    // Beside the panel, at its top: to its right where the screen has room, else to its left.
                    var panel = PanelBounds;
                    float width = SessionDetailsWindow.DefaultWidth * scale, height = SessionDetailsWindow.DefaultHeight * scale, gap = 8 * scale;
                    var x = panel.xMax + gap + width <= Screen.width ? panel.xMax + gap : panel.xMin - gap - width >= 0 ? panel.xMin - gap - width : Mathf.Max(0, Screen.width - width);
                    Place((RectTransform)details.transform, new Rect(x, Mathf.Clamp(panel.yMin, 0, Mathf.Max(0, Screen.height - height)), width, height));
                    detailsPlaced = true;
                    SaveBounds(details.NativeWindow);
                } else {
                    // Bounds saved smaller than the window's least size, such as by an earlier version with a lower
                    // minimum, are raised to the default size where they fall short.
                    var bounds = (RectTransform)details.transform;
                    if (bounds.rect.width < SessionDetailsWindow.MinimumWidth || bounds.rect.height < SessionDetailsWindow.MinimumHeight) {
                        if (bounds.rect.width < SessionDetailsWindow.MinimumWidth) bounds.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, SessionDetailsWindow.DefaultWidth);
                        if (bounds.rect.height < SessionDetailsWindow.MinimumHeight) bounds.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, SessionDetailsWindow.DefaultHeight);
                        SaveBounds(details.NativeWindow);
                    }
                }
                boundsFrame = -1; dirty = true;
                DetailsProblem = null;
            } catch (Exception e) { DetailsProblem = e.GetBaseException().Message; }
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
        // The distance the session details were scrolled from their top before their last rebuild, restored in a later
        // frame, once the replaced content is gone.
        float? restoreDetailsScroll;
        int restoreDetailsFrame;
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

        internal NativePresentation(Action<UIPanelBuilder> build, Action onClosed) {
            content = build; closed = onClosed;
            var manager = WindowManager.Shared;
            if (manager == null || manager.windowPrefab == null || manager.builderAssets == null ||
                manager.builderAssets.buttonCompact == null || manager.builderAssets.buttonSelected == null ||
                manager.builderAssets.inputField == null || manager.builderAssets.filterField == null ||
                manager.builderAssets.labelControl == null || manager.builderAssets.labelEmpty == null || manager.builderAssets.sectionHeader == null ||
                manager.builderAssets.fieldRow == null || manager.builderAssets.tabView == null || manager.builderAssets.segment.left == null ||
                manager.builderAssets.segmentSelected.left == null || manager.builderAssets.scrollRectVertical == null)
                throw new InvalidOperationException("Native map editor window or control assets unavailable");
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
                // The native window saves whether it was shown; a panel that was open when the native map editor was left opens again.
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
        static string IconPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(NativePresentation).Assembly.Location) ?? "", "Icons", "SceneWrench.png");
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
        // tooltip, placed by the strip's own layout after the last native icon. It exists while a native map editor is attached,
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
            // The game shows the tooltip of the first object under the pointer that is on the UI layer; the builder's rule
            // is not, so the divider's tooltip never showed.
            var ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) rule.gameObject.layer = ui;
            var image = rule.GetComponent<Image>();
            if (image != null) image.raycastTarget = true;
            var drag = rule.gameObject.AddComponent<ColumnDivider>();
            drag.enabled = !repairMode;
            drag.Column = column; drag.Minimum = minimum; drag.Maximum = maximum; drag.Changed = changed;
            divider = drag; dividerWidth = desired;
            var tooltip = rule.GetComponent<UI.Tooltips.UITooltipProvider>() ?? rule.gameObject.AddComponent<UI.Tooltips.UITooltipProvider>();
            tooltip.DynamicTooltipInfo = () => new TooltipInfo("Outliner width", CommandBand.Tooltip("Drag to resize the outliner.", lockReason));
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
        // The last frame in which the primary pointer button was down or changed.
        int pointerFrame = -10;
        // While the pause menu is open no tooltip of the panel stays drawn behind it: the game's tooltip callout is
        // made invisible while the game is paused and the panel is open. The game fades the callout in only when its
        // own visible state changes, so on unpause a tooltip that is still due is made visible again here. The game
        // types are looked up once.
        static Type pauseType, tooltipType;
        bool tooltipHidden;
        internal void PauseTooltip() {
            if (!IsOpen && !tooltipHidden) return;
            try {
                if (pauseType == null) pauseType = Native.Type("PauseMenu");
                if (tooltipType == null) tooltipType = Native.Type("UI.Tooltips.UITooltipController");
                var paused = Native.Read(pauseType, "IsPaused") is bool flag && flag;
                // A tooltip is hidden when the game pauses with the panel open, and stays hidden until the pause
                // menu closes, also when the panel closes meanwhile.
                if (!CommandBand.HidesTooltip(paused, IsOpen, tooltipHidden) && !tooltipHidden) return;
                var controller = Native.Read(tooltipType, "Shared");
                var group = controller == null ? null : Native.Read(controller, "_calloutCanvasGroup") as CanvasGroup;
                if (group == null) { tooltipHidden = false; return; }
                if (CommandBand.HidesTooltip(paused, IsOpen, tooltipHidden)) { if (group.alpha != 0f) group.alpha = 0f; tooltipHidden = true; return; }
                // Unpaused: a callout the game still counts as visible shows again.
                tooltipHidden = false;
                if (Native.Read(controller, "_isVisible") is bool visible && visible) group.alpha = 1f;
            } catch { tooltipHidden = false; }
        }
        // Whether a field of the panel has the keyboard: Escape is then left alone, as in the native map editor. A text
        // field takes Escape itself, ends its edit and gives up the focus in the same press, so asking the fields
        // when Escape arrives finds none focused. The frame in which a field of the panel last had the focus is
        // noted every frame instead (CommandBand.FieldHasFocus), from the event system's selected object, which
        // costs no search.
        int fieldFocusFrame = -1;
        void NoteFieldFocus() {
            try {
                var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
                if (selected == null || window == null) return;
                var field = selected.GetComponent<TMP_InputField>();
                if (field != null && field.isFocused && (selected.transform.IsChildOf(window.transform) || (details != null && selected.transform.IsChildOf(details.transform)))) fieldFocusFrame = Time.frameCount;
            } catch { }
        }
        internal bool FieldFocused => IsOpen && CommandBand.FieldHasFocus(Time.frameCount, fieldFocusFrame);
        // One wheel tick moves a scroll view of the panel as far as it moves a native one. The game sets its step on
        // a scroll view through ScrollRectSettingsApplicator; the panel's views take the step of a native view that
        // has it, read from the game once, or the game's own rule where none is found.
        static float nativeStep;
        static float NativeStep() {
            if (nativeStep > 0) return nativeStep;
            try {
                foreach (var applicator in UnityEngine.Object.FindObjectsByType<Game.Settings.ScrollRectSettingsApplicator>(FindObjectsSortMode.None)) {
                    var rect = applicator == null ? null : applicator.GetComponent<ScrollRect>();
                    if (rect == null || rect.GetComponentInParent<ScenePatchWindow>() != null || rect.GetComponentInParent<SessionDetailsWindow>() != null || rect.GetComponentInParent<HelpWindow>() != null) continue;
                    if (rect.scrollSensitivity > 1) return nativeStep = rect.scrollSensitivity;
                }
            } catch { }
            // No native view to read from: the game's own rule, kept as well, so the scene is searched once.
            return nativeStep = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor ? 20 : Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor ? 2 : 1;
        }
        int scrollStepFrame = -1;
        // The panel, the session details and the help window alike.
        void ApplyScrollStep() {
            var step = NativeStep();
            foreach (var root in new Component[] { window, details, help }) {
                if (root == null) continue;
                foreach (var rect in root.GetComponentsInChildren<ScrollRect>(true)) if (rect != null && rect.scrollSensitivity != step) rect.scrollSensitivity = step;
            }
        }
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
        void ShownChanged(bool shown) {
            boundsFrame = -1;
            if (shown || disposed) return;
            ClearFocus();
            // The session details and the help belong to the open panel.
            try { if (DetailsOpen) details.NativeWindow.CloseWindow(); } catch { }
            try { if (help != null && help.NativeWindow.IsShown) help.NativeWindow.CloseWindow(); } catch { }
            HelpRegion = null;
            closed();
        }
        internal void Tick() {
            if (disposed) return;
            if (openOnTick) { openOnTick = false; if (!IsOpen) Toggle(); }
            Layout(false);
            if (IsOpen) PlaceTitleHelp();
            RemoveClosedHelp();
            if (helpSizing > 0 && --helpSizing == 0) SizeHelp();
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
            NoteFieldFocus();
            // After a rebuild, and now and then for the windows that open and rebuild on their own.
            if ((scrollStepFrame >= 0 && Time.frameCount >= scrollStepFrame) || (IsOpen && Time.frameCount % 30 == 0)) { scrollStepFrame = -1; ApplyScrollStep(); }
            // A click is a press and a release on one control: a rebuild between the two, or in the frame of the
            // release before the game's event system handled it, destroys the control and loses the click. The
            // panel is rebuilt only once the pointer has been up for a frame.
            if (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonDown(0)) pointerFrame = Time.frameCount;
            if (dirty && IsOpen && !Input.GetMouseButton(0) && Time.frameCount > pointerFrame + 1 && !window.GetComponentsInChildren<TMP_InputField>().Any(f => f.isFocused)) {
                restoreScroll = scroll == null ? (float?)null : scroll.content.anchoredPosition.y;
                dirty = false; window.Refresh();
                // The rebuilt scroll views take the native wheel step now and once more after their own start.
                ApplyScrollStep(); scrollStepFrame = Time.frameCount + 3;
                if (DetailsOpen) {
                    // The session details rebuild with the panel; they keep the distance scrolled from their top, so
                    // a button lower in them stays under the pointer.
                    var detailsScroll = details.GetComponentInChildren<ScrollRect>();
                    restoreDetailsScroll = detailsScroll == null ? (float?)null : detailsScroll.content.anchoredPosition.y;
                    restoreDetailsFrame = Time.frameCount;
                    details.Refresh();
                }
            }
            if (restoreDetailsScroll.HasValue && DetailsOpen && Time.frameCount > restoreDetailsFrame) {
                var detailsScroll = details.GetComponentInChildren<ScrollRect>();
                if (detailsScroll != null) {
                    Canvas.ForceUpdateCanvases();
                    var body = detailsScroll.content; var view = detailsScroll.viewport != null ? detailsScroll.viewport : (RectTransform)detailsScroll.transform;
                    body.anchoredPosition = new Vector2(body.anchoredPosition.x, Mathf.Clamp(restoreDetailsScroll.Value, 0, Mathf.Max(0, body.rect.height - view.rect.height)));
                }
                restoreDetailsScroll = null;
            }
        }
        void ClearFocus() {
            var events = EventSystem.current;
            if (events != null && Owns(events.currentSelectedGameObject)) events.SetSelectedGameObject(null);
        }
        internal void ClearFocusForEdit() => ClearFocus();
        internal bool Owns(GameObject target) => target != null &&
            ((entry != null && target.transform.IsChildOf(entry.transform)) ||
             (window != null && target.transform.IsChildOf(window.transform)) ||
             (details != null && target.transform.IsChildOf(details.transform)) ||
             (help != null && target.transform.IsChildOf(help.transform)));

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
            var within = PanelPlacement.Within(new PanelPlacement.Area { X = current.x, Y = current.y, Width = current.width, Height = current.height }, Screen.width, Screen.height);
            // A placed window stays where it is, as native windows do: native UI that opens over it, chrome rebuilds and
            // camera moves do not move or resize it. Bounds the native window restored from an earlier session win over
            // the default. A placed window that no longer lies within the screen, such as one whose corner was dragged
            // past the screen's bottom edge, moves back inside with its size, and does not return to the default layout.
            if (within != null && (panelPlaced || hadSavedBounds)) {
                var inside = within.Value;
                panelPlaced = true;
                if (inside.X == current.x && inside.Y == current.y && inside.Width == current.width && inside.Height == current.height) return;
                Place((RectTransform)window.transform, new Rect(inside.X, inside.Y, inside.Width, inside.Height));
                SaveBounds(window.NativeWindow);
                return;
            }
            Place((RectTransform)window.transform, DefaultBounds());
            panelPlaced = true;
            SaveBounds(window.NativeWindow);
        }
        // The native window saves its bounds when the user moves or resizes it and when it is shown or closed. It is
        // shown before this layout places it, so without a move by the user the saved position would stay the window's
        // initial one, the top-left corner, and the next native map editor session would restore that. Raising the window's own
        // moved event makes the native persistence save the placed bounds in its own format.
        void SaveBounds(Window native) {
            try {
                var moved = typeof(Window).GetField("OnDidPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(native) as Action<Vector2>;
                if (moved == null) { SaveProblem = "the native window exposes no moved event"; return; }
                moved(native.GetPosition());
                SaveProblem = null;
            } catch (Exception e) { SaveProblem = e.GetBaseException().Message; }
        }
        // Why the default placement could not be saved, or null. The window then opens at its initial position in the
        // next native map editor session until the user moves it.
        internal string SaveProblem { get; private set; }
        // Where the window opens while no bounds are saved: PanelPlacement decides between the space below the native Map
        // Editor window and the top right of the screen. The lower edge of what occupies that corner, the button strip
        // and the status readout below it, is read from the visible native UI.
        Rect DefaultBounds() {
            var native = MapEditorBounds();
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
        static Rect MapEditorBounds() {
            try {
                var type = Native.Type("Game.MapEditor.MapEditorWindow");
                var mapEditor = type == null ? null : UnityEngine.Object.FindAnyObjectByType(type) as Component;
                var native = mapEditor == null ? null : mapEditor.GetComponent<Window>();
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
        // The game's tooltip is one callout that its controller fades out when the pointer leaves. When the native map editor is left
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
            if (resetSprite != null) UnityEngine.Object.Destroy(resetSprite);
            if (resetTexture != null) UnityEngine.Object.Destroy(resetTexture);
            if (detailsSprite != null) UnityEngine.Object.Destroy(detailsSprite);
            if (detailsTexture != null) UnityEngine.Object.Destroy(detailsTexture);
            if (window != null) {
                window.Build = null;
                window.NativeWindow.DelegateRequestClose = null;
                window.NativeWindow.OnShownDidChange -= ShownChanged;
                window.gameObject.SetActive(false); UnityEngine.Object.Destroy(window.gameObject);
            }
            if (details != null) { details.Build = null; details.gameObject.SetActive(false); UnityEngine.Object.Destroy(details.gameObject); details = null; }
            if (help != null) { help.Build = null; help.NativeWindow.OnShownDidChange -= HelpShown; help.gameObject.SetActive(false); UnityEngine.Object.Destroy(help.gameObject); help = null; }
            HelpRegion = null;
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
