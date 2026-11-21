// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: throwaway view switch of the outliner grouping prototype: two icon buttons on the object count line.
using System;
using System.Linq;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    public sealed partial class PatcherBehaviour {
        const string ByCategoryIcon = "CrankyAnt Scene Patcher by category", ByPlaceIcon = "CrankyAnt Scene Patcher by place";
        static IDisposable byCategoryRegistration, byPlaceRegistration;
        static bool spriteLibraryLogged;

        // The game's icon buttons take a sprite from its sprite library, which a mod may add to. The two icons are drawn
        // here: a bulleted list for By category and a map pin for By place. The active view's icon is full white, the
        // other dimmed.
        void PrototypeViewIcons(UIPanelBuilder row) {
            try {
                RegisterViewIcons();
                foreach (var (icon, place, title, body) in new[] {
                    (ByCategoryIcon, false, "By category", "List objects by what they are: category, then kind, then place."),
                    (ByPlaceIcon, true, "By place", "List objects by place, with the same categories and kinds under each place.") }) {
                    var button = row.AddIconButton(icon, presentation.Action(() => { PrototypeOutlinerGrouping.ByPlace = place; PrototypeOutlinerGrouping.LogPending = true; }));
                    button.Tooltip(title, body + (PrototypeOutlinerGrouping.ByPlace == place ? " Shown now." : ""));
                    var image = button.RectTransform.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.gameObject.name == "Icon");
                    if (image != null) image.color = new Color(1, 1, 1, PrototypeOutlinerGrouping.ByPlace == place ? 1f : 0.4f);
                }
            } catch (Exception e) {
                Debug.LogWarning("Scene Patcher PROTOTYPE view icons unavailable, using text: " + e.GetBaseException().Message);
                Button(row, PrototypeOutlinerGrouping.ByPlace ? "By category" : "By place", () => { PrototypeOutlinerGrouping.ByPlace = !PrototypeOutlinerGrouping.ByPlace; PrototypeOutlinerGrouping.LogPending = true; });
            }
        }

        static void RegisterViewIcons() {
            if (!spriteLibraryLogged) {
                spriteLibraryLogged = true;
                try {
                    var names = UI.SpriteLibrary.Shared == null ? "no shared library" : String.Join(", ", UI.SpriteLibrary.Shared.entries.Select(entry => entry.name));
                    Debug.Log("Scene Patcher PROTOTYPE sprite library: " + names);
                } catch (Exception e) { Debug.Log("Scene Patcher PROTOTYPE sprite library unreadable: " + e.GetBaseException().Message); }
            }
            if (byCategoryRegistration == null) byCategoryRegistration = UI.SpriteLibrary.Register(ByCategoryIcon, Draw(ByCategoryIcon, ListPixel));
            if (byPlaceRegistration == null) byPlaceRegistration = UI.SpriteLibrary.Register(ByPlaceIcon, Draw(ByPlaceIcon, PinPixel));
        }

        const int IconSize = 32;
        static Sprite Draw(string name, Func<float, float, float> coverage) {
            var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[IconSize * IconSize];
            // Four samples per pixel for a soft edge; rows count from the bottom.
            for (var y = 0; y < IconSize; y++)
                for (var x = 0; x < IconSize; x++) {
                    var sum = 0f;
                    for (var sy = 0; sy < 2; sy++) for (var sx = 0; sx < 2; sx++) sum += coverage(x + 0.25f + 0.5f * sx, y + 0.25f + 0.5f * sy);
                    pixels[y * IconSize + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(sum / 4f)));
                }
            texture.SetPixels32(pixels); texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name; sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
        // Three rows of a square bullet and a bar.
        static float ListPixel(float x, float y) {
            foreach (var centre in new[] { 7f, 16f, 25f }) {
                if (Mathf.Abs(y - centre) > 2.5f) continue;
                if (x >= 3 && x <= 8) return 1;
                if (x >= 12 && x <= 29) return 1;
            }
            return 0;
        }
        // A map pin: a ring on top of a point.
        static float PinPixel(float x, float y) {
            float cx = 16, cy = 20, outer = 10, inner = 4;
            var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (d <= outer && d >= inner) return 1;
            // The point: a triangle from the circle's lower half down to (16, 2).
            if (y >= 2 && y <= cy) {
                var half = (y - 2) / (cy - 2) * outer * 0.87f;
                if (Mathf.Abs(x - cx) <= half && d > inner) return 1;
            }
            return 0;
        }
    }
}
