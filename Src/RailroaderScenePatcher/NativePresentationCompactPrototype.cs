// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using UI.Windows;
using UnityEngine;

namespace RailroaderScenePatcher {
    // PROTOTYPE (throwaway): compact mode of the scene patch window. Not production code. It answers what a compact
    // mode should look like and what the game's window allows. Mode and variant live in memory only; the native
    // window's saved bounds are kept at the full window's while compact.
    internal sealed partial class NativePresentation {
        internal enum PrototypeCompactVariant { OwnerLayout, SlimStrip, SideRail }
        internal static readonly string[] PrototypeVariantNames = { "A: Owner's layout", "B: Slim bottom strip", "C: Side rail" };
        internal bool PrototypeCompact { get; private set; }
        internal PrototypeCompactVariant PrototypeVariant { get; private set; }
        // Called after each mode or variant switch with the presentation's part of the log line.
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
                    () => new TooltipInfo(PrototypeCompact ? "Full window" : "Compact window", PrototypeCompact ? "Show the full window again." : "Shrink the window to the bottom of the screen."), out prototypeCompactGlyph);
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
            if (disposed || !IsOpen) return;
            if (PrototypeCompact) PrototypeExitCompact(); else PrototypeEnterCompact();
            PrototypeSwitched?.Invoke(PrototypeState());
        }

        internal void PrototypeCycleVariant(int step) {
            if (disposed || !PrototypeCompact) return;
            PrototypeVariant = (PrototypeCompactVariant)(((int)PrototypeVariant + step + 3) % 3);
            PrototypePlace();
            dirty = true;
            PrototypeSwitched?.Invoke(PrototypeState());
        }

        void PrototypeEnterCompact() {
            prototypeFullRect = PanelBounds;
            try { prototypeSavedRecord = Game.GlobalPreferences.GetString("window." + ScenePatchWindow.WindowIdentifier); } catch { prototypeSavedRecord = null; }
            // The native minimum size applies only to the user's resize drag (PanelResizer.minSize); Window.SetContentSize
            // and a direct size do not clamp to it. It is lowered while compact so the compact window can be resized.
            try {
                prototypeResizer = window.GetComponentInChildren<UI.Common.PanelResizer>(true);
                if (prototypeResizer != null) { prototypeResizerMinimum = prototypeResizer.minSize; prototypeResizer.minSize = new Vector2(200, 60); }
            } catch { prototypeResizer = null; }
            PrototypeCompact = true;
            CloseHelp();
            PrototypePlace();
            if (prototypeCompactGlyph != null) prototypeCompactGlyph.sprite = PrototypeArrow(false);
            dirty = true;
        }

        void PrototypeExitCompact() {
            PrototypeCompact = false;
            if (prototypeResizer != null) { try { prototypeResizer.minSize = prototypeResizerMinimum; } catch { } prototypeResizer = null; }
            if (prototypeFullRect.width > 0 && prototypeFullRect.height > 0) Place((RectTransform)window.transform, prototypeFullRect);
            PrototypeRestoreRecord();
            if (prototypeCompactGlyph != null) prototypeCompactGlyph.sprite = PrototypeArrow(true);
            boundsFrame = -1; dirty = true;
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

        // The compact window's rectangle in screen pixels for the current variant. The frame is what the native window
        // adds around its content: the title bar and the padding.
        void PrototypePlace() {
            var native = window.NativeWindow;
            var frame = ((RectTransform)native.transform).rect.size - native.GetContentSize();
            float width, height, x, y;
            switch (PrototypeVariant) {
                case PrototypeCompactVariant.SlimStrip:
                    width = prototypeFullRect.width;
                    height = (PrototypeSlimContent + frame.y) * scale;
                    x = (Screen.width - width) * 0.5f; y = Screen.height - height;
                    break;
                case PrototypeCompactVariant.SideRail:
                    width = (PrototypeRailContent + frame.x) * scale;
                    height = Mathf.Round(Screen.height * 0.6f);
                    x = Screen.width - width; y = Screen.height - height;
                    break;
                default:
                    width = prototypeFullRect.width;
                    height = (PrototypeOwnerContent + frame.y) * scale;
                    x = (Screen.width - width) * 0.5f; y = Screen.height - height;
                    break;
            }
            width = Mathf.Min(width, Screen.width); height = Mathf.Min(height, Screen.height);
            Place((RectTransform)window.transform, new Rect(Mathf.Max(0, x), Mathf.Max(0, y), width, height));
            boundsFrame = -1;
        }

        // Content heights and the rail's content width, in canvas units.
        internal const float PrototypeOwnerContent = 236, PrototypeSlimContent = 92, PrototypeRailContent = 300;

        string PrototypeState() {
            var bounds = PanelBounds;
            return "mode=" + (PrototypeCompact ? "compact" : "full") + "; variant=" + PrototypeVariantNames[(int)PrototypeVariant] +
                "; rect=(" + Mathf.RoundToInt(bounds.x) + ", " + Mathf.RoundToInt(bounds.y) + ", " + Mathf.RoundToInt(bounds.width) + " x " + Mathf.RoundToInt(bounds.height) + ") px; scale=" + scale;
        }

        void PrototypeDispose() {
            foreach (var item in new UnityEngine.Object[] { prototypeDownSprite, prototypeUpSprite, prototypeDownTexture, prototypeUpTexture })
                if (item != null) UnityEngine.Object.Destroy(item);
        }
    }
}
