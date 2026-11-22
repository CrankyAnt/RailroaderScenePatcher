// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using UI.Windows;
using UnityEngine;

namespace RailroaderScenePatcher {
    // PROTOTYPE (throwaway): compact mode of the scene patch window. Not production code. A single context-aware
    // side rail at the right edge of the screen keeps the active edit, world choice and outliner accessible while
    // leaving the view's center free. Mode, outliner state and dragged placement live in memory only; the native window's
    // saved bounds are kept at the full window's while compact.
    internal sealed partial class NativePresentation {
        // Content dimensions in canvas units: 356 matches InspectorMinimumWidth (350) with padding so the four
        // right-band commands (335), every footer and tab action row (budget 340) and all five title-bar icons fit.
        // The collapsed content holds the tallest context section, the Placement body at its collapsed least height
        // of 140, with the bands, heading, messages, action row, outliner summary line and buttons, and grip clearance. The expanded
        // content adds the Placement body's full 196 and the outliner rows' least height of 120 with their spacing.
        // Each is the least content size the resize grip allows in its state, so a resize cannot crush a row.
        internal const float PrototypeRailContent = 356, PrototypePlacementBody = 196, PrototypePlacementCollapsedBody = 140, PrototypeMessageHeight = 56,
            PrototypeCollapsedContent = 444, PrototypeExpandedMinContent = PrototypeCollapsedContent + (PrototypePlacementBody - PrototypePlacementCollapsedBody) + 120 + 4;
        // Where the user left the compact rail, in screen pixels, for this session only.
        Rect prototypeCompactRect, prototypePlacedRect;
        float prototypeExpandedHeight, prototypeCollapsedHeight;
        internal bool PrototypeCompact { get; private set; }
        internal bool PrototypeOutlinerOpen { get; private set; } = true;
        // Called after each mode or outliner switch with the presentation's part of the log line.
        internal Action<string> PrototypeSwitched;
        RectTransform titleCompactPrototype;
        Sprite prototypeDownSprite, prototypeUpSprite;
        Texture2D prototypeDownTexture, prototypeUpTexture;
        UnityEngine.UI.Image prototypeCompactGlyph;
        // The full window's rectangle in screen pixels, and the saved-bounds record from before compact mode.
        Rect prototypeFullRect;
        string prototypeSavedRecord;
        Vector2 prototypeResizerMinimum;
        UI.Common.PanelResizer prototypeResizer;

        void AddPrototypeCompactIcon(WindowManager manager, Transform parent) {
            try {
                titleCompactPrototype = TitleIcon(manager, parent, "CrankyAnt Scene Patcher compact prototype", PrototypeArrow(true), PrototypeToggleCompact,
                    () => new TooltipInfo(PrototypeCompact ? "Full window" : "Compact window",
                        CommandBand.Tooltip(PrototypeCompact ? "Show the full window again." : "Switch to a compact side rail that leaves more of the world in view.", lockReason)), out prototypeCompactGlyph);
            } catch (Exception e) {
                if (titleCompactPrototype != null) UnityEngine.Object.Destroy(titleCompactPrototype.gameObject);
                titleCompactPrototype = null; prototypeCompactGlyph = null;
                TitleIconProblem = (TitleIconProblem == null ? "" : TitleIconProblem + "; ") + "Compact prototype: " + e.GetBaseException().Message;
            }
        }

        // A solid triangle as wide as the close cross, 13 units, and 11 units high: it points down to compact the
        // window and up to restore it. Rows count from the bottom; the drawing is made pointing down and mirrored for up.
        Sprite PrototypeArrow(bool down) {
            if (down && prototypeDownSprite != null) return prototypeDownSprite;
            if (!down && prototypeUpSprite != null) return prototypeUpSprite;
            const float baseY = 52, tipY = 4, halfWidth = 28, middle = 28;
            Func<Vector2, bool> covers = point => {
                float x = point.x, y = down ? point.y : 56 - point.y;
                if (y < tipY || y > baseY) return false;
                return Mathf.Abs(x - middle) <= halfWidth * (y - tipY) / (baseY - tipY);
            };
            if (down) return prototypeDownSprite = Glyph("CrankyAnt Scene Patcher prototype arrow down", covers, out prototypeDownTexture);
            return prototypeUpSprite = Glyph("CrankyAnt Scene Patcher prototype arrow up", covers, out prototypeUpTexture);
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
            if (prototypeCompactGlyph != null) prototypeCompactGlyph.sprite = PrototypeArrow(false);
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
            if (prototypeCompactGlyph != null) prototypeCompactGlyph.sprite = PrototypeArrow(true);
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
            if (!PrototypeCompact) return;
            // Closed by a route other than Close: the window gets its full rectangle back for the next open.
            if (!IsOpen) { PrototypeExitCompact(); PrototypeSwitched?.Invoke(PrototypeState() + "; reason=closed elsewhere"); return; }
            PrototypeRestoreRecord();
        }

        // The least window size in canvas units: the rail's content with the outliner shown or hidden, and the frame.
        Vector2 PrototypeMinimumSize() => new Vector2(PrototypeRailContent, PrototypeOutlinerOpen ? PrototypeExpandedMinContent : PrototypeCollapsedContent) + PrototypeFrame();

        Vector2 PrototypeFrame() {
            var native = window.NativeWindow;
            return ((RectTransform)native.transform).rect.size - native.GetContentSize();
        }

        float PrototypeDefaultExpandedHeight(float frameY) =>
            Mathf.Min(Screen.height, Mathf.Max(Mathf.Round(Screen.height * 0.65f), (PrototypeExpandedMinContent + frameY) * scale));

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
            foreach (var item in new UnityEngine.Object[] { prototypeDownSprite, prototypeUpSprite, prototypeDownTexture, prototypeUpTexture })
                if (item != null) UnityEngine.Object.Destroy(item);
        }
    }
}
