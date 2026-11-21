// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using UI.Windows;
using UnityEngine;

namespace RailroaderScenePatcher {
    // PROTOTYPE (throwaway): compact mode of the scene patch window. Not production code. A single context-aware
    // side rail at the right edge of the screen keeps the active edit, world choice and object list accessible while
    // leaving the view's center free. Mode, list state and dragged placement live in memory only; the native window's
    // saved bounds are kept at the full window's while compact.
    internal sealed partial class NativePresentation {
        // Content dimensions in canvas units: 356 matches InspectorMinimumWidth (350) with padding so the four
        // right-band commands (335), every footer and tab action row (budget 340) and all five title-bar icons fit.
        internal const float PrototypeRailContent = 356, PrototypePlacementBody = 196, PrototypeMessageHeight = 56,
            PrototypeExpandedMinContent = 560, PrototypeCollapsedContent = 420;
        const float PrototypeResizerMinWidth = PrototypeRailContent + 24, PrototypeResizerMinHeight = 300;
        // Where the user left the compact rail, in screen pixels, for this session only.
        Rect prototypeCompactRect, prototypePlacedRect;
        float prototypeExpandedHeight, prototypeCollapsedHeight;
        internal bool PrototypeCompact { get; private set; }
        internal bool PrototypeListOpen { get; private set; } = true;
        // Called after each mode or list switch with the presentation's part of the log line.
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

        // An arrow 13 units high: a shaft of 2 units and a head. down points to the bottom of the screen.
        Sprite PrototypeArrow(bool down) {
            if (down && prototypeDownSprite != null) return prototypeDownSprite;
            if (!down && prototypeUpSprite != null) return prototypeUpSprite;
            Func<Vector2, bool> covers = point => {
                // Rows count from the bottom; the drawing is made pointing down and mirrored for up.
                float x = point.x, y = down ? point.y : 56 - point.y;
                if (x >= 24 && x <= 32 && y >= 22 && y <= 56) return true;
                // The head: a triangle from (4, 30) and (52, 30) to the tip (28, 0).
                var half = (y / 30f) * 24f;
                return y >= 0 && y <= 30 && Mathf.Abs(x - 28) <= half;
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
        internal void PrototypeToggleList() {
            if (disposed || !PrototypeCompact || !IsOpen) return;
            var current = PanelBounds;
            PrototypeRememberRect();
            if (current.height > 0) {
                if (PrototypeListOpen) prototypeExpandedHeight = current.height;
                else prototypeCollapsedHeight = current.height;
            }
            PrototypeListOpen = !PrototypeListOpen;
            var frame = PrototypeFrame();
            var targetHeight = PrototypeListOpen
                ? (prototypeExpandedHeight > 0 ? prototypeExpandedHeight : PrototypeDefaultExpandedHeight(frame.y))
                : (prototypeCollapsedHeight > 0 ? prototypeCollapsedHeight : (PrototypeCollapsedContent + frame.y) * scale);
            targetHeight = Mathf.Clamp(targetHeight, PrototypeResizerMinHeight * scale, Screen.height);
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
            PrototypeSwitched?.Invoke(PrototypeState() + "; reason=" + (PrototypeListOpen ? "show list" : "hide list"));
        }

        void PrototypeEnterCompact() {
            ClearFocus();
            prototypeFullRect = PanelBounds;
            try { prototypeSavedRecord = Game.GlobalPreferences.GetString("window." + ScenePatchWindow.WindowIdentifier); } catch { prototypeSavedRecord = null; }
            // The native minimum size applies only to the user's resize drag (PanelResizer.minSize). Lower it to the
            // rail's own minimum so the rail can be resized without crushing the title bar or action rows.
            try {
                prototypeResizer = window.GetComponentInChildren<UI.Common.PanelResizer>(true);
                if (prototypeResizer != null) { prototypeResizerMinimum = prototypeResizer.minSize; prototypeResizer.minSize = new Vector2(PrototypeResizerMinWidth, PrototypeResizerMinHeight); }
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
                if (PrototypeListOpen) prototypeExpandedHeight = current.height;
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
            PrototypeListOpen = true;
        }

        // Keeps a compact window inside the screen after a resolution or scale change without writing saved full bounds.
        void PrototypeClampLayout() {
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

        Vector2 PrototypeFrame() {
            var native = window.NativeWindow;
            return ((RectTransform)native.transform).rect.size - native.GetContentSize();
        }

        float PrototypeDefaultExpandedHeight(float frameY) =>
            Mathf.Min(Screen.height, Mathf.Max(Mathf.Round(Screen.height * 0.65f), (PrototypeExpandedMinContent + frameY) * scale));

        // The compact rail's rectangle in screen pixels. Right-aligned and bottom-aligned by default; restores the
        // dragged rectangle for the session once moved or resized.
        void PrototypePlace() {
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
            var defaultHeight = PrototypeListOpen
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
            return "mode=" + (PrototypeCompact ? "compact" : "full") + "; layout=side rail; list=" + (PrototypeListOpen ? "open" : "collapsed") +
                "; rect=(" + Mathf.RoundToInt(bounds.x) + ", " + Mathf.RoundToInt(bounds.y) + ", " + Mathf.RoundToInt(bounds.width) + " x " + Mathf.RoundToInt(bounds.height) + ") px; scale=" + scale;
        }

        void PrototypeDispose() {
            foreach (var item in new UnityEngine.Object[] { prototypeDownSprite, prototypeUpSprite, prototypeDownTexture, prototypeUpTexture })
                if (item != null) UnityEngine.Object.Destroy(item);
        }
    }
}
