// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using UI.Tooltips;
using UI.Windows;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // PROTOTYPE (throwaway): compact mode of the scene patch window. Not production code. A single context-aware
    // side rail at the right edge of the screen keeps the active edit, world choice and outliner accessible while
    // leaving the view's center free. Mode, outliner state and dragged placement live in memory only; the native window's
    // saved bounds are kept at the full window's while compact.
    internal sealed partial class NativePresentation {
        // Content dimensions in canvas units: 356 matches InspectorMinimumWidth (350) with padding so the four
        // right-band commands (335), every footer and tab action row (budget 340) and all five title-bar icons fit.
        // The collapsed content holds the tallest context section, the Placement body at its collapsed least height
        // of 140, with the bands, heading, messages of six lines (112), action row, the outliner summary line with Hide
        // outliner at its right, and grip clearance. The expanded content adds the Placement body's full 196 and the
        // outliner rows' least height of 120 with their spacing. Each is the least content size the resize grip allows
        // in its state, so a resize cannot crush a row.
        internal const float PrototypeRailContent = 356, PrototypePlacementBody = 196, PrototypePlacementCollapsedBody = 140, PrototypeMessageHeight = 112,
            PrototypeCollapsedContent = 476, PrototypeExpandedMinContent = PrototypeCollapsedContent + (PrototypePlacementBody - PrototypePlacementCollapsedBody) + 120 + 4;
        // The native outline row's height, and the rows the compact outliner shows by default: the three rows down to
        // the inspected group and half of the eighteen below them that round 8's default showed.
        internal const float PrototypeOutlinerRow = 26; internal const int PrototypeOutlinerRows = 12;
        // The rail ends with the outliner or its summary line, whose Hide outliner button stands at the right: the rail
        // keeps clear of the native resize grip, 22 units high, less the 10 units of padding below the last row.
        internal const float PrototypeGripClearance = 12;
        // Where the user left the compact rail, in screen pixels, for this session only.
        Rect prototypeCompactRect, prototypePlacedRect;
        float prototypeExpandedHeight, prototypeCollapsedHeight;
        internal bool PrototypeCompact { get; private set; }
        internal bool PrototypeOutlinerOpen { get; private set; } = true;
        // Called after each mode or outliner switch with the presentation's part of the log line.
        internal Action<string> PrototypeSwitched;
        RectTransform titleCompactPrototype;
        Sprite prototypeCompactSprite, prototypeFullSprite;
        Texture2D prototypeCompactTexture, prototypeFullTexture;
        Image prototypeCompactGlyph;
        // The full window's rectangle in screen pixels, and the saved-bounds record from before compact mode.
        Rect prototypeFullRect;
        string prototypeSavedRecord;
        Vector2 prototypeResizerMinimum;
        UI.Common.PanelResizer prototypeResizer;

        void AddPrototypeCompactIcon(WindowManager manager, Transform parent) {
            try {
                titleCompactPrototype = TitleIcon(manager, parent, "CrankyAnt Scene Patcher compact prototype", PrototypeModeGlyph(true), PrototypeToggleCompact,
                    () => new TooltipInfo(PrototypeCompact ? "Full window" : "Compact window",
                        CommandBand.Tooltip(PrototypeCompact ? "Show the full window again." : "Switch to a compact side rail that leaves more of the world in view.", lockReason)), out prototypeCompactGlyph);
            } catch (Exception e) {
                if (titleCompactPrototype != null) UnityEngine.Object.Destroy(titleCompactPrototype.gameObject);
                titleCompactPrototype = null; prototypeCompactGlyph = null;
                TitleIconProblem = (TitleIconProblem == null ? "" : TitleIconProblem + "; ") + "Compact prototype: " + e.GetBaseException().Message;
            }
        }

        // Corner brackets in the title-bar glyph's size and stroke, 13 units across with strokes of 2: four corners that
        // point inwards to compact the window, and four that point outwards to show the full window, as a video
        // player's leave and enter full screen. Neither says where the window goes, so they hold wherever the player
        // puts it. u and v are a point's distances to the nearest side and to the nearest top or bottom edge.
        Sprite PrototypeModeGlyph(bool toCompact) {
            if (toCompact && prototypeCompactSprite != null) return prototypeCompactSprite;
            if (!toCompact && prototypeFullSprite != null) return prototypeFullSprite;
            const float size = 56, stroke = 8, outwardArm = 22, inwardElbow = 24;
            Func<Vector2, bool> covers = point => {
                float u = Mathf.Min(point.x, size - point.x), v = Mathf.Min(point.y, size - point.y);
                if (!toCompact) return (u <= stroke && v <= outwardArm) || (v <= stroke && u <= outwardArm);
                return (u >= inwardElbow - stroke && u <= inwardElbow && v <= inwardElbow) || (v >= inwardElbow - stroke && v <= inwardElbow && u <= inwardElbow);
            };
            if (toCompact) return prototypeCompactSprite = Glyph("CrankyAnt Scene Patcher prototype compact glyph", covers, out prototypeCompactTexture);
            return prototypeFullSprite = Glyph("CrankyAnt Scene Patcher prototype full glyph", covers, out prototypeFullTexture);
        }

        void PrototypeToggleCompact() {
            if (disposed || !IsOpen || repairMode) return;
            if (PrototypeCompact) PrototypeExitCompact(); else PrototypeEnterCompact();
            PrototypeSwitched?.Invoke(PrototypeState());
        }

        // Collapses or expands the outliner inside the compact rail, keeping the user's dragged X and width.
        internal void PrototypeToggleOutliner() {
            if (disposed || !PrototypeCompact || !IsOpen) return;
            var current = PanelBounds;
            PrototypeRememberRect();
            if (current.height > 0) {
                if (PrototypeOutlinerOpen) prototypeExpandedHeight = current.height;
                else prototypeCollapsedHeight = current.height;
            }
            PrototypeOutlinerOpen = !PrototypeOutlinerOpen;
            if (prototypeResizer != null) { try { prototypeResizer.minSize = PrototypeMinimumSize(); } catch { } }
            var frame = PrototypeFrame();
            if (PrototypeOutlinerOpen && prototypeExpandedHeight <= 0 && prototypeCompactRect.width <= 0) { prototypeFitFrames = 3; prototypeFitTries = 2; }
            var targetHeight = PrototypeOutlinerOpen
                ? (prototypeExpandedHeight > 0 ? prototypeExpandedHeight : PrototypeDefaultExpandedHeight(frame.y))
                : (prototypeCollapsedHeight > 0 ? prototypeCollapsedHeight : (PrototypeCollapsedContent + frame.y) * scale);
            targetHeight = Mathf.Clamp(targetHeight, PrototypeMinimumSize().y * scale, Screen.height);
            var width = current.width > 0 ? Mathf.Min(current.width, Screen.width) : Mathf.Min((PrototypeRailContent + frame.x) * scale, Screen.width);
            var x = current.width > 0 ? Mathf.Clamp(current.x, 0, Mathf.Max(0, Screen.width - width)) : Mathf.Max(0, Screen.width - width);
            // Keep the bottom edge steady when docked low, or the top edge when dragged to the upper half.
            var anchorBottom = current.height <= 0 || current.yMax >= Screen.height * 0.5f;
            var y = anchorBottom ? current.yMax - targetHeight : current.y;
            y = Mathf.Clamp(y, 0, Mathf.Max(0, Screen.height - targetHeight));
            prototypePlacedRect = new Rect(x, y, width, targetHeight);
            if (prototypeCompactRect.width > 0) prototypeCompactRect = prototypePlacedRect;
            Place((RectTransform)window.transform, prototypePlacedRect);
            boundsFrame = -1; dirty = true;
            PrototypeSwitched?.Invoke(PrototypeState() + "; reason=" + (PrototypeOutlinerOpen ? "show outliner" : "hide outliner"));
        }

        void PrototypeEnterCompact() {
            ClearFocus();
            prototypeFullRect = PanelBounds;
            try { prototypeSavedRecord = Game.GlobalPreferences.GetString("window." + ScenePatchWindow.WindowIdentifier); } catch { prototypeSavedRecord = null; }
            // The native minimum size applies only to the user's resize drag (PanelResizer.minSize). Set it to the
            // rail's content in its outliner state and the window frame, so a resize never crushes a row.
            try {
                prototypeResizer = window.GetComponentInChildren<UI.Common.PanelResizer>(true);
                if (prototypeResizer != null) { prototypeResizerMinimum = prototypeResizer.minSize; prototypeResizer.minSize = PrototypeMinimumSize(); }
            } catch { prototypeResizer = null; }
            PrototypeCompact = true;
            CloseHelp();
            PrototypePlace();
            if (prototypeCompactGlyph != null) prototypeCompactGlyph.sprite = PrototypeModeGlyph(false);
            dirty = true;
        }

        // A compact window the user dragged or resized keeps that rectangle for this session; it never snaps back.
        void PrototypeRememberRect() {
            var current = PanelBounds;
            if (current.width <= 0) return;
            if (Mathf.Abs(current.x - prototypePlacedRect.x) > 1 || Mathf.Abs(current.y - prototypePlacedRect.y) > 1 ||
                Mathf.Abs(current.width - prototypePlacedRect.width) > 1 || Mathf.Abs(current.height - prototypePlacedRect.height) > 1) {
                prototypeCompactRect = current;
                if (PrototypeOutlinerOpen) prototypeExpandedHeight = current.height;
                else prototypeCollapsedHeight = current.height;
            }
        }

        void PrototypeExitCompact() {
            PrototypeRememberRect();
            ClearFocus();
            PrototypeCompact = false;
            if (prototypeResizer != null) { try { prototypeResizer.minSize = prototypeResizerMinimum; } catch { } prototypeResizer = null; }
            if (prototypeFullRect.width > 0 && prototypeFullRect.height > 0) Place((RectTransform)window.transform, prototypeFullRect);
            PrototypeRestoreRecord();
            PrototypeResetExtraWindows();
            if (prototypeCompactGlyph != null) prototypeCompactGlyph.sprite = PrototypeModeGlyph(true);
            boundsFrame = -1; dirty = true;
        }

        void PrototypeForgetLayout() {
            prototypeCompactRect = Rect.zero;
            prototypeExpandedHeight = 0;
            prototypeCollapsedHeight = 0;
            PrototypeOutlinerOpen = true;
        }

        // The UI scale the rail was last placed at. Its rectangles are screen pixels, which a scale change makes wrong.
        float prototypeScale;

        // Keeps a compact window inside the screen after a resolution change without writing saved full bounds. A UI
        // scale change places the rail again at its default place, the right screen edge, bottom-aligned: the window
        // keeps its top-left corner while its size follows the scale, so it drifted off the edge. A dragged place and
        // the remembered heights are pixels of the old scale and are dropped; the outliner stays shown or hidden.
        void PrototypeClampLayout() {
            if (prototypeScale != scale) {
                prototypeCompactRect = Rect.zero; prototypeExpandedHeight = 0; prototypeCollapsedHeight = 0;
                PrototypePlace();
                PrototypeSwitched?.Invoke(PrototypeState() + "; reason=ui scale");
                return;
            }
            var current = PanelBounds;
            if (current.width <= 0 || current.height <= 0) return;
            var within = PanelPlacement.Within(new PanelPlacement.Area { X = current.x, Y = current.y, Width = current.width, Height = current.height }, Screen.width, Screen.height);
            if (within == null) { PrototypePlace(); return; }
            var inside = within.Value;
            if (Mathf.Abs(inside.X - current.x) <= 0.5f && Mathf.Abs(inside.Y - current.y) <= 0.5f &&
                Mathf.Abs(inside.Width - current.width) <= 0.5f && Mathf.Abs(inside.Height - current.height) <= 0.5f) return;
            prototypePlacedRect = new Rect(inside.X, inside.Y, inside.Width, inside.Height);
            if (prototypeCompactRect.width > 0) prototypeCompactRect = prototypePlacedRect;
            Place((RectTransform)window.transform, prototypePlacedRect);
            boundsFrame = -1;
        }

        // The native persistence saves position and content size whenever the user drags or resizes the window. While
        // compact, the record from before compact mode is written back, so compact bounds never reach the next session.
        void PrototypeRestoreRecord() {
            if (prototypeSavedRecord == null) return;
            try {
                var key = "window." + ScenePatchWindow.WindowIdentifier;
                if (Game.GlobalPreferences.GetString(key) != prototypeSavedRecord) Game.GlobalPreferences.SetString(key, prototypeSavedRecord);
            } catch { }
        }

        void PrototypeTick() {
            PrototypeDelayRowTooltips();
            // The extra windows move once the button that asked for it is up: layout places nothing while it is down.
            if (prototypeExtrasFrames > 0 && !Input.GetMouseButton(0) && --prototypeExtrasFrames == 0) PrototypePlaceExtrasBeside();
            if (!PrototypeCompact) return;
            // Closed by a route other than Close: the window gets its full rectangle back for the next open.
            if (!IsOpen) { PrototypeExitCompact(); PrototypeSwitched?.Invoke(PrototypeState() + "; reason=closed elsewhere"); return; }
            PrototypeRestoreRecord();
            PrototypeWholeRows();
            if (prototypeFitFrames > 0 && --prototypeFitFrames == 0) PrototypeFitRows();
        }

        // ---- Help and session details ----

        // Ticks until the extra windows are placed beside the window again.
        int prototypeExtrasFrames;

        // The help and the session details open beside the window again: their places from compact mode are dropped
        // when compact mode ends and on Reset layout. The full and compact window places are not touched here.
        void PrototypeResetExtraWindows() {
            try { Game.GlobalPreferences.SetString("window." + SessionDetailsWindow.WindowIdentifier, ""); } catch { }
            detailsPlaced = false;
            prototypeExtrasFrames = 2;
        }

        void PrototypePlaceExtrasBeside() {
            if (disposed || !IsOpen) return;
            try { if (DetailsOpen) PlaceDetailsBeside(); } catch (Exception e) { DetailsProblem = e.GetBaseException().Message; }
            if (help != null && help.NativeWindow.IsShown) { helpBeside = PanelBounds; helpSizing = 1; }
            boundsFrame = -1;
        }

        // ---- Outliner rows ----

        // The outliner's scroll view, set each time the outliner builds its rows, in the full and the compact window.
        internal ScrollRect PrototypeOutlinerView;
        ScrollRect prototypeTrimmedView;
        // The viewport's bottom and top offsets as the native view made them, and the scroll offset last snapped to.
        Vector2 prototypeViewportBase;
        float prototypeSnappedOffset;
        int prototypeFitFrames, prototypeFitTries;

        // The height the viewport would have without trimming, in canvas units.
        float PrototypeViewportAvailable(ScrollRect view) =>
            ((RectTransform)view.transform).rect.height + prototypeViewportBase.y - prototypeViewportBase.x;

        // The compact outliner shows whole rows: the viewport is cut down to a whole number of rows, and the scroll
        // offset rests on a row boundary, in the direction it moved. The space left over, less than a row, stays
        // below the list. The native view's rows are 26 units and start at the content's top.
        void PrototypeWholeRows() {
            var view = PrototypeOutlinerView;
            if (view == null || !PrototypeOutlinerOpen) return;
            var viewport = view.viewport;
            if (viewport == null || viewport == view.transform || viewport.anchorMin.y != 0f || viewport.anchorMax.y != 1f) return;
            if (prototypeTrimmedView != view) {
                prototypeTrimmedView = view;
                prototypeViewportBase = new Vector2(viewport.offsetMin.y, viewport.offsetMax.y);
                prototypeSnappedOffset = view.content == null ? 0 : view.content.anchoredPosition.y;
            }
            var available = PrototypeViewportAvailable(view);
            var whole = Mathf.Max(PrototypeOutlinerRow, Mathf.Floor((available + 0.5f) / PrototypeOutlinerRow) * PrototypeOutlinerRow);
            var bottom = prototypeViewportBase.x + Mathf.Max(0, available - whole);
            if (Mathf.Abs(viewport.offsetMin.y - bottom) > 0.25f) viewport.offsetMin = new Vector2(viewport.offsetMin.x, bottom);
            var content = view.content;
            if (content == null) return;
            var y = content.anchoredPosition.y;
            if (Input.GetMouseButton(0) || view.velocity.sqrMagnitude > 1f) { prototypeSnappedOffset = y; return; }
            var rows = y / PrototypeOutlinerRow;
            var target = y > prototypeSnappedOffset + 0.5f ? Mathf.Ceil(rows - 0.01f) : y < prototypeSnappedOffset - 0.5f ? Mathf.Floor(rows + 0.01f) : Mathf.Round(rows);
            var offset = Mathf.Clamp(target * PrototypeOutlinerRow, 0, Mathf.Max(0, content.rect.height - whole));
            if (Mathf.Abs(offset - y) > 0.25f) content.anchoredPosition = new Vector2(content.anchoredPosition.x, offset);
            prototypeSnappedOffset = offset;
        }

        // At its default place the rail takes the height that gives the outliner its default rows exactly, keeping
        // its bottom edge. A rail the user resized or dragged keeps its size.
        void PrototypeFitRows() {
            if (!PrototypeOutlinerOpen || prototypeCompactRect.width > 0 || prototypeExpandedHeight > 0) return;
            var view = PrototypeOutlinerView;
            if (view == null || prototypeTrimmedView != view) return;
            var delta = PrototypeOutlinerRows * PrototypeOutlinerRow - PrototypeViewportAvailable(view);
            var current = PanelBounds;
            if (Mathf.Abs(delta) < 0.5f || current.height <= 0) return;
            var height = Mathf.Clamp(current.height + delta * scale, PrototypeMinimumSize().y * scale, Screen.height);
            var y = Mathf.Clamp(current.yMax - height, 0, Mathf.Max(0, Screen.height - height));
            prototypePlacedRect = new Rect(current.x, y, current.width, height);
            Place((RectTransform)window.transform, prototypePlacedRect);
            boundsFrame = -1;
            // The layout follows in a later frame; one more look corrects what it changed.
            if (--prototypeFitTries > 0) prototypeFitFrames = 3;
        }

        // ---- Row tooltips ----

        // The game shows a tooltip after the pointer rests half a second, but once one shows, the next one follows at
        // once. In the outliner the rows touch, so scrolling or moving down the list showed a tooltip for every row.
        // A row's tooltip now waits until the pointer has rested on that row for nearly the game's half second, also
        // while another tooltip shows; the rest of the game's grace period keeps it from waiting twice. Outside that
        // mode the game's own half second applies. This holds in the full and the compact window.
        const float PrototypeRowTooltipDelay = 0.45f;
        static readonly System.Reflection.FieldInfo prototypeLastShown = typeof(UITooltipController).GetField("_lastShownTooltip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        static string prototypeDwellTitle;
        static float prototypeDwellSince;
        static int prototypeDwellFrame = -10;

        // Rows are pooled and made as the list scrolls, so each tick gives any new row its waiting tooltip.
        void PrototypeDelayRowTooltips() {
            var view = PrototypeOutlinerView;
            if (view == null || view.content == null) return;
            var content = view.content;
            for (var i = 0; i < content.childCount; i++) {
                var provider = content.GetChild(i).GetComponent<UITooltipProvider>();
                if (provider == null || provider.DynamicTooltipInfo != null) continue;
                var row = provider;
                row.DynamicTooltipInfo = () => PrototypeDwell(row.Title);
            }
        }

        static TooltipInfo PrototypeDwell(string title) {
            var now = Time.realtimeSinceStartup;
            if (title != prototypeDwellTitle || Time.frameCount - prototypeDwellFrame > 1) { prototypeDwellTitle = title; prototypeDwellSince = now; }
            prototypeDwellFrame = Time.frameCount;
            var info = new TooltipInfo(title, "");
            if (String.IsNullOrEmpty(title) || !PrototypeTooltipShowing()) return info;
            return now - prototypeDwellSince >= PrototypeRowTooltipDelay ? info : TooltipInfo.Empty;
        }

        static bool PrototypeTooltipShowing() {
            try {
                var controller = UITooltipController.Shared;
                return controller != null && prototypeLastShown != null && prototypeLastShown.GetValue(controller) is TooltipInfo shown && !shown.IsEmpty;
            } catch { return false; }
        }

        // The least window size in canvas units: the rail's content with the outliner shown or hidden, and the frame.
        Vector2 PrototypeMinimumSize() => new Vector2(PrototypeRailContent, PrototypeOutlinerOpen ? PrototypeExpandedMinContent : PrototypeCollapsedContent) + PrototypeFrame();

        Vector2 PrototypeFrame() {
            var native = window.NativeWindow;
            return ((RectTransform)native.transform).rect.size - native.GetContentSize();
        }

        // A first guess with the outliner's default rows in place of its least height; PrototypeFitRows corrects it
        // once the content is laid out.
        float PrototypeDefaultExpandedHeight(float frameY) =>
            Mathf.Min(Screen.height, (PrototypeExpandedMinContent - 120 + PrototypeOutlinerRows * PrototypeOutlinerRow + frameY) * scale);

        // The compact rail's rectangle in screen pixels. Right-aligned and bottom-aligned by default; restores the
        // dragged rectangle for the session once moved or resized.
        void PrototypePlace() {
            prototypeScale = scale;
            if (prototypeCompactRect.width > 0 && prototypeCompactRect.height > 0) {
                var width = Mathf.Min(prototypeCompactRect.width, Screen.width);
                var height = Mathf.Min(prototypeCompactRect.height, Screen.height);
                var x = Mathf.Clamp(prototypeCompactRect.x, 0, Mathf.Max(0, Screen.width - width));
                var y = Mathf.Clamp(prototypeCompactRect.y, 0, Mathf.Max(0, Screen.height - height));
                prototypePlacedRect = new Rect(x, y, width, height);
                Place((RectTransform)window.transform, prototypePlacedRect);
                boundsFrame = -1;
                return;
            }
            var frame = PrototypeFrame();
            var defaultWidth = Mathf.Min((PrototypeRailContent + frame.x) * scale, Screen.width);
            var defaultHeight = PrototypeOutlinerOpen
                ? PrototypeDefaultExpandedHeight(frame.y)
                : Mathf.Min((PrototypeCollapsedContent + frame.y) * scale, Screen.height);
            var defaultX = Mathf.Max(0, Screen.width - defaultWidth);
            if (PrototypeOutlinerOpen) { prototypeFitFrames = 3; prototypeFitTries = 2; }
            var defaultY = Mathf.Max(0, Screen.height - defaultHeight);
            prototypePlacedRect = new Rect(defaultX, defaultY, defaultWidth, defaultHeight);
            Place((RectTransform)window.transform, prototypePlacedRect);
            boundsFrame = -1;
        }

        string PrototypeState() {
            var bounds = PanelBounds;
            return "mode=" + (PrototypeCompact ? "compact" : "full") + "; layout=side rail; outliner=" + (PrototypeOutlinerOpen ? "shown" : "hidden") +
                "; rect=(" + Mathf.RoundToInt(bounds.x) + ", " + Mathf.RoundToInt(bounds.y) + ", " + Mathf.RoundToInt(bounds.width) + " x " + Mathf.RoundToInt(bounds.height) + ") px; scale=" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        void PrototypeDispose() {
            foreach (var item in new UnityEngine.Object[] { prototypeCompactSprite, prototypeFullSprite, prototypeCompactTexture, prototypeFullTexture })
                if (item != null) UnityEngine.Object.Destroy(item);
        }
    }
}
