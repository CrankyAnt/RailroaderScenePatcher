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
        // Where the compact rail stands, and its height with the outliner shown and hidden once the user changed it, in
        // screen pixels, for this session only.
        readonly RailPlacement railPlace = new RailPlacement();
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

        // The title-bar icon and the window help's button. The switch waits until the button is up and one tick has
        // passed (AfterRelease), so the window does not move away from under the release.
        internal void ToggleCompact() {
            if (disposed || !IsOpen || repairMode || afterRelease.Pending) return;
            Defer(() => {
                if (disposed || !IsOpen || repairMode) return;
                if (Compact) ExitCompact(); else EnterCompact();
                CompactSwitched?.Invoke(CompactState());
            });
        }

        // A window move asked for by a button, run by CompactTick once the button is up.
        Action deferredMove;
        readonly AfterRelease afterRelease = new AfterRelease();
        void Defer(Action move) { deferredMove = move; afterRelease.Request(); }

        // Hides or shows the outliner inside the compact rail, keeping the user's dragged X and width. The rail's height
        // changes by the outliner's rows, so the outliner's button stays under the pointer when the bottom edge is kept.
        internal void ToggleCompactOutliner() {
            if (disposed || !Compact || !IsOpen) return;
            var current = PanelBounds;
            if (current.height > 0) {
                if (CompactOutlinerShown) compactExpandedHeight = current.height;
                else compactCollapsedHeight = current.height;
            }
            CompactOutlinerShown = !CompactOutlinerShown;
            if (resizer != null) { try { resizer.minSize = CompactMinimumSize(); } catch { } }
            var frame = WindowFrame();
            var targetHeight = CompactOutlinerShown
                ? (compactExpandedHeight > 0 ? compactExpandedHeight : CompactDefaultHeight(true, frame.y))
                : (compactCollapsedHeight > 0 ? compactCollapsedHeight : CompactDefaultHeight(false, frame.y));
            targetHeight = Mathf.Clamp(targetHeight, CompactMinimumSize().y * scale, Screen.height);
            var width = current.width > 0 ? Mathf.Min(current.width, Screen.width) : Mathf.Min((CompactRail.Width + frame.x) * scale, Screen.width);
            var x = current.width > 0 ? Mathf.Clamp(current.x, 0, Mathf.Max(0, Screen.width - width)) : Mathf.Max(0, Screen.width - width);
            // Keep the bottom edge steady when docked low, or the top edge when dragged to the upper half.
            var anchorBottom = current.height <= 0 || current.yMax >= Screen.height * 0.5f;
            var y = anchorBottom ? current.yMax - targetHeight : current.y;
            y = Mathf.Clamp(y, 0, Mathf.Max(0, Screen.height - targetHeight));
            PlaceCompactRect(new Rect(x, y, width, targetHeight));
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

        // A rail the user dragged or resized keeps that rectangle for the session; the height it has with the outliner
        // shown or hidden is kept for the next switch of the outliner.
        void NoticeDrag() {
            var bounds = PanelBounds;
            if (!railPlace.Observe(Area(bounds), scale, Input.GetMouseButton(0))) return;
            if (CompactOutlinerShown) compactExpandedHeight = bounds.height;
            else compactCollapsedHeight = bounds.height;
        }

        void ExitCompact() {
            NoticeDrag();
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
            railPlace.Forget();
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
                var least = CompactMinimumSize() * scale;
                var kept = railPlace.AfterScaleChange(scale, least.x, least.y, Screen.width, Screen.height);
                if (kept is PanelPlacement.Area dragged) { compactScale = scale; PlaceCompactRect(new Rect(dragged.X, dragged.Y, dragged.Width, dragged.Height)); }
                else PlaceCompact();
                CompactSwitched?.Invoke(CompactState() + "; reason=ui scale");
                return;
            }
            var current = PanelBounds;
            if (current.width <= 0 || current.height <= 0) return;
            var within = PanelPlacement.Within(Area(current), Screen.width, Screen.height);
            if (within == null) { PlaceCompact(); return; }
            var inside = within.Value;
            if (Mathf.Abs(inside.X - current.x) <= 0.5f && Mathf.Abs(inside.Y - current.y) <= 0.5f &&
                Mathf.Abs(inside.Width - current.width) <= 0.5f && Mathf.Abs(inside.Height - current.height) <= 0.5f) return;
            PlaceCompactRect(new Rect(inside.X, inside.Y, inside.Width, inside.Height));
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
            var buttonDown = Input.GetMouseButton(0);
            if (afterRelease.Tick(buttonDown)) { var move = deferredMove; deferredMove = null; move?.Invoke(); }
            if (secondaryReset.Tick(buttonDown)) PlaceSecondaryWindowsBeside();
            if (!Compact) return;
            // Closed by a route other than Close: the window gets its full rectangle back for the next open.
            if (!IsOpen) { ExitCompact(); CompactSwitched?.Invoke(CompactState() + "; reason=closed elsewhere"); return; }
            RestoreFullRecord();
            if (compactScale == scale) NoticeDrag();
            FollowOutlinerRows();
            TrimOutlinerToWholeRows();
        }

        // ---- Help and session details ----

        readonly AfterRelease secondaryReset = new AfterRelease();

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

        // The rows the compact outliner shows: six at the default height, more when the user made the rail higher. The
        // rail gives the outliner exactly that many whole rows (OutlinerHeight), and the message area takes what is left.
        internal int CompactOutlinerRows { get; private set; } = CompactRail.OutlinerRows;
        internal float CompactOutlinerHeight => CompactOutlinerRows * CompactRail.OutlinerRow;

        // Follows a resize by the user: a change of whole rows rebuilds the rail with the new outliner height.
        void FollowOutlinerRows() {
            var bounds = PanelBounds;
            if (!CompactOutlinerShown || bounds.height <= 0) return;
            var rows = CompactRail.OutlinerRowsFor(bounds.height, CompactDefaultHeight(true, WindowFrame().y), scale);
            if (rows == CompactOutlinerRows) return;
            CompactOutlinerRows = rows;
            dirty = true;
        }

        ScrollRect trimmedOutlinerView;
        // The viewport's bottom and top offsets as the native view made them, and the scroll offset last snapped to.
        Vector2 outlinerViewportBase;
        float outlinerSnappedOffset;

        // The compact outliner shows whole rows: its height is whole rows already, and the viewport is cut to whole
        // rows in case the native view takes part of it, such as for a horizontal scroll bar. The scroll offset rests
        // on a row boundary, in the direction it moved. The native view's rows start at the content's top.
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
            var available = ((RectTransform)view.transform).rect.height + outlinerViewportBase.y - outlinerViewportBase.x;
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

        // The least window size in canvas units: the rail's content with the outliner shown or hidden, and the frame.
        Vector2 CompactMinimumSize() => new Vector2(CompactRail.Width, CompactOutlinerShown ? CompactRail.ExpandedLeastContent : CompactRail.CollapsedContent) + WindowFrame();

        // The native window's frame around its content, such as the title bar, in canvas units.
        Vector2 WindowFrame() {
            var native = window.NativeWindow;
            return ((RectTransform)native.transform).rect.size - native.GetContentSize();
        }

        // The rail's default height in pixels, with the outliner shown or hidden: its least content and the frame.
        float CompactDefaultHeight(bool outlinerShown, float frameY) =>
            Mathf.Min(Screen.height, ((outlinerShown ? CompactRail.ExpandedLeastContent : CompactRail.CollapsedContent) + frameY) * scale);

        // The compact rail's rectangle in screen pixels: at the right edge and bottom-aligned by default, or the
        // rectangle the user dragged it to in this session, inside the screen.
        void PlaceCompact() {
            compactScale = scale;
            if (railPlace.Dragged is PanelPlacement.Area dragged) {
                var width = Mathf.Min(dragged.Width, Screen.width);
                var height = Mathf.Min(dragged.Height, Screen.height);
                var x = Mathf.Clamp(dragged.X, 0, Mathf.Max(0, Screen.width - width));
                var y = Mathf.Clamp(dragged.Y, 0, Mathf.Max(0, Screen.height - height));
                PlaceCompactRect(new Rect(x, y, width, height));
                return;
            }
            var frame = WindowFrame();
            var place = CompactRail.DefaultPlace((CompactRail.Width + frame.x) * scale, CompactDefaultHeight(CompactOutlinerShown, frame.y), Screen.width, Screen.height);
            PlaceCompactRect(new Rect(place.X, place.Y, place.Width, place.Height));
        }

        void PlaceCompactRect(Rect rect) {
            railPlace.Placed(Area(rect), scale);
            Place((RectTransform)window.transform, rect);
            boundsFrame = -1;
        }

        static PanelPlacement.Area Area(Rect rect) => new PanelPlacement.Area { X = rect.x, Y = rect.y, Width = rect.width, Height = rect.height };

        string CompactState() {
            var bounds = PanelBounds;
            return "mode=" + (Compact ? "compact" : "full") + "; outliner=" + (CompactOutlinerShown ? "shown (" + CompactOutlinerRows + " rows)" : "hidden") +
                "; rect=(" + Mathf.RoundToInt(bounds.x) + ", " + Mathf.RoundToInt(bounds.y) + ", " + Mathf.RoundToInt(bounds.width) + " x " + Mathf.RoundToInt(bounds.height) + ") px; scale=" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        void DisposeCompact() {
            foreach (var item in new UnityEngine.Object[] { compactSprite, fullSprite, compactTexture, fullTexture })
                if (item != null) UnityEngine.Object.Destroy(item);
        }
    }
}
