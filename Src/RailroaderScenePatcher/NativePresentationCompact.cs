// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using UI.Tooltips;
using UI.Windows;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // Compact mode of the scene patch window: one context-aware side rail at the right edge of the screen keeps the
    // active edit, a world choice and the outliner at hand while it leaves the middle of the view free. The mode, the
    // outliner's state and the rail's place live in memory for the session only; while compact, the native window's
    // saved bounds stay the full window's.
    internal sealed partial class NativePresentation {
        internal bool Compact { get; private set; }
        internal bool CompactOutlinerShown { get; private set; } = true;
        // Called after each mode or outliner switch with the presentation's part of the log line.
        internal Action<string> CompactSwitched;
        // Where the user left the compact rail, in screen pixels, for this session only, and the rectangle last placed.
        Rect compactRect, compactPlacedRect;
        float compactExpandedHeight, compactCollapsedHeight;
        // The UI scale the rail was last placed at. Its rectangles are screen pixels, which a scale change rescales.
        float compactScale;
        RectTransform titleCompact;
        Sprite compactSprite, fullSprite;
        Texture2D compactTexture, fullTexture;
        Image compactGlyph;
        // The full window's rectangle in canvas units, so that a UI scale change while compact does not change its size,
        // the saved-bounds record from before compact mode, and the resize grip's minimum size from before compact mode.
        PanelPlacement.Area fullCanvas;
        readonly FullWindowRecord fullRecord = new FullWindowRecord();
        Vector2 fullResizerMinimum;
        UI.Common.PanelResizer resizer;

        void AddCompactIcon(WindowManager manager, Transform parent) {
            try {
                titleCompact = TitleIcon(manager, parent, "CrankyAnt Scene Patcher compact window", ModeGlyph(true), ToggleCompact,
                    () => new TooltipInfo(Compact ? "Full window" : "Compact window",
                        CommandBand.Tooltip(Compact ? "Show the full window again." : "Switch to a compact side rail that leaves more of the world in view.", lockReason)), out compactGlyph);
            } catch (Exception e) {
                if (titleCompact != null) UnityEngine.Object.Destroy(titleCompact.gameObject);
                titleCompact = null; compactGlyph = null;
                TitleIconProblem = (TitleIconProblem == null ? "" : TitleIconProblem + "; ") + "Compact window: " + e.GetBaseException().Message;
            }
        }

        // Corner brackets in the title-bar glyph's size and stroke, 13 units across with strokes of 2: four corners that
        // point inwards to compact the window, and four that point outwards to show the full window, as a video
        // player's leave and enter full screen. Neither says where the window goes, so they hold wherever the player
        // puts it. u and v are a point's distances to the nearest side and to the nearest top or bottom edge.
        Sprite ModeGlyph(bool toCompact) {
            if (toCompact && compactSprite != null) return compactSprite;
            if (!toCompact && fullSprite != null) return fullSprite;
            const float size = 56, stroke = 8, outwardArm = 22, inwardElbow = 24;
            Func<Vector2, bool> covers = point => {
                float u = Mathf.Min(point.x, size - point.x), v = Mathf.Min(point.y, size - point.y);
                if (!toCompact) return (u <= stroke && v <= outwardArm) || (v <= stroke && u <= outwardArm);
                return (u >= inwardElbow - stroke && u <= inwardElbow && v <= inwardElbow) || (v >= inwardElbow - stroke && v <= inwardElbow && u <= inwardElbow);
            };
            if (toCompact) return compactSprite = Glyph("CrankyAnt Scene Patcher compact glyph", covers, out compactTexture);
            return fullSprite = Glyph("CrankyAnt Scene Patcher full window glyph", covers, out fullTexture);
        }

        internal void ToggleCompact() {
            if (disposed || !IsOpen || repairMode) return;
            if (Compact) ExitCompact(); else EnterCompact();
            CompactSwitched?.Invoke(CompactState());
        }

        // Hides or shows the outliner inside the compact rail, keeping the user's dragged X and width.
        internal void ToggleCompactOutliner() {
            if (disposed || !Compact || !IsOpen) return;
            var current = PanelBounds;
            RememberCompactRect();
            if (current.height > 0) {
                if (CompactOutlinerShown) compactExpandedHeight = current.height;
                else compactCollapsedHeight = current.height;
            }
            CompactOutlinerShown = !CompactOutlinerShown;
            if (resizer != null) { try { resizer.minSize = CompactMinimumSize(); } catch { } }
            var frame = WindowFrame();
            if (CompactOutlinerShown && compactExpandedHeight <= 0 && compactRect.width <= 0) StartOutlinerFit();
            var targetHeight = CompactOutlinerShown
                ? (compactExpandedHeight > 0 ? compactExpandedHeight : CompactDefaultExpandedHeight(frame.y))
                : (compactCollapsedHeight > 0 ? compactCollapsedHeight : (CompactRail.CollapsedContent + frame.y) * scale);
            targetHeight = Mathf.Clamp(targetHeight, CompactMinimumSize().y * scale, Screen.height);
            var width = current.width > 0 ? Mathf.Min(current.width, Screen.width) : Mathf.Min((CompactRail.Width + frame.x) * scale, Screen.width);
            var x = current.width > 0 ? Mathf.Clamp(current.x, 0, Mathf.Max(0, Screen.width - width)) : Mathf.Max(0, Screen.width - width);
            // Keep the bottom edge steady when docked low, or the top edge when dragged to the upper half.
            var anchorBottom = current.height <= 0 || current.yMax >= Screen.height * 0.5f;
            var y = anchorBottom ? current.yMax - targetHeight : current.y;
            y = Mathf.Clamp(y, 0, Mathf.Max(0, Screen.height - targetHeight));
            PlaceCompactRect(new Rect(x, y, width, targetHeight));
            if (compactRect.width > 0) compactRect = compactPlacedRect;
            dirty = true;
            CompactSwitched?.Invoke(CompactState() + "; reason=" + (CompactOutlinerShown ? "show outliner" : "hide outliner"));
        }

        void EnterCompact() {
            ClearFocus();
            var full = PanelBounds;
            fullCanvas = CompactRail.ToCanvas(new PanelPlacement.Area { X = full.x, Y = full.y, Width = full.width, Height = full.height }, scale);
            string record;
            try { record = Game.GlobalPreferences.GetString("window." + ScenePatchWindow.WindowIdentifier); } catch { record = null; }
            fullRecord.Keep(record);
            // The native minimum size applies only to the user's resize drag (PanelResizer.minSize). Set it to the
            // rail's content in its outliner state and the window frame, so a resize never crushes a row.
            try {
                resizer = window.GetComponentInChildren<UI.Common.PanelResizer>(true);
                if (resizer != null) { fullResizerMinimum = resizer.minSize; resizer.minSize = CompactMinimumSize(); }
            } catch { resizer = null; }
            Compact = true;
            CloseHelp();
            PlaceCompact();
            if (compactGlyph != null) compactGlyph.sprite = ModeGlyph(false);
            dirty = true;
        }

        // A compact window the user dragged or resized keeps that rectangle for this session; it never snaps back.
        void RememberCompactRect() {
            var current = PanelBounds;
            if (current.width <= 0) return;
            if (Mathf.Abs(current.x - compactPlacedRect.x) > 1 || Mathf.Abs(current.y - compactPlacedRect.y) > 1 ||
                Mathf.Abs(current.width - compactPlacedRect.width) > 1 || Mathf.Abs(current.height - compactPlacedRect.height) > 1) {
                compactRect = current;
                if (CompactOutlinerShown) compactExpandedHeight = current.height;
                else compactCollapsedHeight = current.height;
            }
        }

        void ExitCompact() {
            RememberCompactRect();
            ClearFocus();
            Compact = false;
            if (resizer != null) { try { resizer.minSize = fullResizerMinimum; } catch { } resizer = null; }
            if (fullCanvas.Width > 0 && fullCanvas.Height > 0) {
                var place = CompactRail.RestoredFull(fullCanvas, scale, MinimumWidth, MinimumHeight, Screen.width, Screen.height);
                Place((RectTransform)window.transform, new Rect(place.X, place.Y, place.Width, place.Height));
            }
            RestoreFullRecord();
            ResetSecondaryWindows();
            if (compactGlyph != null) compactGlyph.sprite = ModeGlyph(true);
            boundsFrame = -1; dirty = true;
        }

        // Reset layout: the rail opens at its default place and height again, with the outliner shown.
        void ForgetCompactLayout() {
            compactRect = Rect.zero;
            compactExpandedHeight = 0;
            compactCollapsedHeight = 0;
            CompactOutlinerShown = true;
        }

        // Keeps a compact window inside the screen after a resolution change without writing saved full bounds. On a UI
        // scale change, a rail at its default place goes to the default place of the new scale, at the right screen
        // edge, bottom-aligned: the native window keeps its top-left corner while its size follows the scale, so it
        // drifted off the edge. A rail the user dragged keeps its top-left corner and takes the new scale's size,
        // inside the screen. The remembered heights follow the scale; the outliner stays shown or hidden.
        void ClampCompactLayout() {
            if (compactScale != scale) {
                var ratio = compactScale > 0 ? scale / compactScale : 1;
                compactExpandedHeight *= ratio; compactCollapsedHeight *= ratio;
                if (compactRect.width > 0) {
                    var least = CompactMinimumSize() * scale;
                    var kept = CompactRail.Rescaled(new PanelPlacement.Area { X = compactRect.x, Y = compactRect.y, Width = compactRect.width, Height = compactRect.height },
                        compactScale, scale, least.x, least.y, Screen.width, Screen.height);
                    compactRect = new Rect(kept.X, kept.Y, kept.Width, kept.Height);
                }
                PlaceCompact();
                CompactSwitched?.Invoke(CompactState() + "; reason=ui scale");
                return;
            }
            var current = PanelBounds;
            if (current.width <= 0 || current.height <= 0) return;
            var within = PanelPlacement.Within(new PanelPlacement.Area { X = current.x, Y = current.y, Width = current.width, Height = current.height }, Screen.width, Screen.height);
            if (within == null) { PlaceCompact(); return; }
            var inside = within.Value;
            if (Mathf.Abs(inside.X - current.x) <= 0.5f && Mathf.Abs(inside.Y - current.y) <= 0.5f &&
                Mathf.Abs(inside.Width - current.width) <= 0.5f && Mathf.Abs(inside.Height - current.height) <= 0.5f) return;
            PlaceCompactRect(new Rect(inside.X, inside.Y, inside.Width, inside.Height));
            if (compactRect.width > 0) compactRect = compactPlacedRect;
        }

        // The native persistence saves position and content size whenever the user drags or resizes the window. While
        // compact, the record from before compact mode is written back, so compact bounds never reach the next session.
        void RestoreFullRecord() {
            try {
                var key = "window." + ScenePatchWindow.WindowIdentifier;
                var rewrite = fullRecord.Rewrite(Game.GlobalPreferences.GetString(key));
                if (rewrite != null) Game.GlobalPreferences.SetString(key, rewrite);
            } catch { }
        }

        void CompactTick() {
            if (secondaryReset.Tick(Input.GetMouseButton(0))) PlaceSecondaryWindowsBeside();
            if (!Compact) return;
            // Closed by a route other than Close: the window gets its full rectangle back for the next open.
            if (!IsOpen) { ExitCompact(); CompactSwitched?.Invoke(CompactState() + "; reason=closed elsewhere"); return; }
            RestoreFullRecord();
            TrimOutlinerToWholeRows();
            if (outlinerFitFrames > 0 && --outlinerFitFrames == 0) FitOutlinerRows();
        }

        // ---- Help and session details ----

        readonly SecondaryWindowReset secondaryReset = new SecondaryWindowReset();

        // The help and the session details open beside the window again: their places are dropped on every exit from
        // compact mode, wherever they were placed, and on Reset layout. The full and compact window places are not
        // touched here.
        void ResetSecondaryWindows() {
            try { Game.GlobalPreferences.SetString("window." + SessionDetailsWindow.WindowIdentifier, ""); } catch { }
            detailsPlaced = false;
            secondaryReset.Request();
        }

        void PlaceSecondaryWindowsBeside() {
            if (disposed || !IsOpen) return;
            try { if (DetailsOpen) PlaceDetailsBeside(); } catch (Exception e) { DetailsProblem = e.GetBaseException().Message; }
            if (help != null && help.NativeWindow.IsShown) { helpBeside = PanelBounds; helpSizing = 1; }
            boundsFrame = -1;
        }

        // ---- Outliner rows ----

        ScrollRect trimmedOutlinerView;
        // The viewport's bottom and top offsets as the native view made them, and the scroll offset last snapped to.
        Vector2 outlinerViewportBase;
        float outlinerSnappedOffset;
        int outlinerFitFrames, outlinerFitTries;

        // The height the viewport would have without trimming, in canvas units.
        float OutlinerViewportAvailable(ScrollRect view) =>
            ((RectTransform)view.transform).rect.height + outlinerViewportBase.y - outlinerViewportBase.x;

        // The compact outliner shows whole rows: the viewport is cut down to a whole number of rows, and the scroll
        // offset rests on a row boundary, in the direction it moved. The native view's rows start at the content's top.
        void TrimOutlinerToWholeRows() {
            var view = OutlinerView;
            if (view == null || !CompactOutlinerShown) return;
            var viewport = view.viewport;
            if (viewport == null || viewport == view.transform || viewport.anchorMin.y != 0f || viewport.anchorMax.y != 1f) return;
            if (trimmedOutlinerView != view) {
                trimmedOutlinerView = view;
                outlinerViewportBase = new Vector2(viewport.offsetMin.y, viewport.offsetMax.y);
                outlinerSnappedOffset = view.content == null ? 0 : view.content.anchoredPosition.y;
            }
            var available = OutlinerViewportAvailable(view);
            var whole = CompactRail.WholeRows(available);
            var bottom = outlinerViewportBase.x + Mathf.Max(0, available - whole);
            if (Mathf.Abs(viewport.offsetMin.y - bottom) > 0.25f) viewport.offsetMin = new Vector2(viewport.offsetMin.x, bottom);
            var content = view.content;
            if (content == null) return;
            var y = content.anchoredPosition.y;
            if (Input.GetMouseButton(0) || view.velocity.sqrMagnitude > 1f) { outlinerSnappedOffset = y; return; }
            var offset = CompactRail.SnappedOffset(y, outlinerSnappedOffset, content.rect.height, whole);
            if (Mathf.Abs(offset - y) > 0.25f) content.anchoredPosition = new Vector2(content.anchoredPosition.x, offset);
            outlinerSnappedOffset = offset;
        }

        // Fits the rail to the outliner's default rows a few frames after the content is laid out.
        void StartOutlinerFit() { outlinerFitFrames = 3; outlinerFitTries = 2; }

        // At its default place the rail takes the height that gives the outliner its default rows exactly, keeping
        // its bottom edge. A rail the user resized or dragged keeps its size.
        void FitOutlinerRows() {
            if (!CompactOutlinerShown || compactRect.width > 0 || compactExpandedHeight > 0) return;
            var view = OutlinerView;
            if (view == null || trimmedOutlinerView != view) return;
            var current = PanelBounds;
            if (current.height <= 0) return;
            var height = CompactRail.FittedHeight(current.height, OutlinerViewportAvailable(view), scale, CompactMinimumSize().y * scale, Screen.height);
            if (Mathf.Abs(height - current.height) < 0.5f * scale) return;
            var y = Mathf.Clamp(current.yMax - height, 0, Mathf.Max(0, Screen.height - height));
            PlaceCompactRect(new Rect(current.x, y, current.width, height));
            // The layout follows in a later frame; one more look corrects what it changed.
            if (--outlinerFitTries > 0) outlinerFitFrames = 3;
        }

        // The least window size in canvas units: the rail's content with the outliner shown or hidden, and the frame.
        Vector2 CompactMinimumSize() => new Vector2(CompactRail.Width, CompactOutlinerShown ? CompactRail.ExpandedLeastContent : CompactRail.CollapsedContent) + WindowFrame();

        // The native window's frame around its content, such as the title bar, in canvas units.
        Vector2 WindowFrame() {
            var native = window.NativeWindow;
            return ((RectTransform)native.transform).rect.size - native.GetContentSize();
        }

        // A first guess with the outliner's default rows in place of its least height; FitOutlinerRows corrects it
        // once the content is laid out.
        float CompactDefaultExpandedHeight(float frameY) =>
            Mathf.Min(Screen.height, (CompactRail.ExpandedLeastContent - CommandBand.OutlinerLeastHeight + CompactRail.OutlinerRows * CompactRail.OutlinerRow + frameY) * scale);

        // The compact rail's rectangle in screen pixels: at the right edge and bottom-aligned by default, or the
        // rectangle the user dragged it to in this session.
        void PlaceCompact() {
            compactScale = scale;
            if (compactRect.width > 0 && compactRect.height > 0) {
                var width = Mathf.Min(compactRect.width, Screen.width);
                var height = Mathf.Min(compactRect.height, Screen.height);
                var x = Mathf.Clamp(compactRect.x, 0, Mathf.Max(0, Screen.width - width));
                var y = Mathf.Clamp(compactRect.y, 0, Mathf.Max(0, Screen.height - height));
                PlaceCompactRect(new Rect(x, y, width, height));
                return;
            }
            var frame = WindowFrame();
            var defaultHeight = CompactOutlinerShown ? CompactDefaultExpandedHeight(frame.y) : (CompactRail.CollapsedContent + frame.y) * scale;
            var place = CompactRail.DefaultPlace((CompactRail.Width + frame.x) * scale, defaultHeight, Screen.width, Screen.height);
            if (CompactOutlinerShown) StartOutlinerFit();
            PlaceCompactRect(new Rect(place.X, place.Y, place.Width, place.Height));
        }

        void PlaceCompactRect(Rect rect) {
            compactPlacedRect = rect;
            Place((RectTransform)window.transform, rect);
            boundsFrame = -1;
        }

        string CompactState() {
            var bounds = PanelBounds;
            return "mode=" + (Compact ? "compact" : "full") + "; outliner=" + (CompactOutlinerShown ? "shown" : "hidden") +
                "; rect=(" + Mathf.RoundToInt(bounds.x) + ", " + Mathf.RoundToInt(bounds.y) + ", " + Mathf.RoundToInt(bounds.width) + " x " + Mathf.RoundToInt(bounds.height) + ") px; scale=" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        void DisposeCompact() {
            foreach (var item in new UnityEngine.Object[] { compactSprite, fullSprite, compactTexture, fullTexture })
                if (item != null) UnityEngine.Object.Destroy(item);
        }
    }
}
