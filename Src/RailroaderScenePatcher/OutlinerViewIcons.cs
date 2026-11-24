// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RailroaderScenePatcher.Core;
using TMPro;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    public sealed partial class PatcherBehaviour {
        const string ByCategoryIcon = "CrankyAnt Scene Patcher by category", ByPlaceIcon = "CrankyAnt Scene Patcher by place";
        const float ViewIconButtonWidth = 28f, ViewIconGlyphSize = 16f;
        static IDisposable byCategoryRegistration, byPlaceRegistration;
        static Sprite byCategorySprite, byPlaceSprite;
        static bool spriteLibraryLogged;

        void SwitchOutlinerView(bool place) {
            if (OutlinerGrouping.ByPlace == place) return;
            OutlinerGrouping.ByPlace = place;
            OutlinerGrouping.LogPending = true;
            // Reveal the currently inspected object in the new view's hierarchy.
            revealed = null;
        }

        // Two compact native segments on the object count line: native segmentSelected / segment styling marks the
        // active view while both icons stay full white (never dimmed like a disabled button). Clicks on the active
        // view are ignored.
        void DrawViewSwitch(UIPanelBuilder row) {
            try {
                RegisterViewIcons();
                var items = new[] {
                    (ByCategoryIcon, byCategorySprite, false, "By category", "List objects by what they are: category, then kind, then place."),
                    (ByPlaceIcon, byPlaceSprite, true, "By place", "List objects by place, with the same categories and kinds under each place.")
                };
                row.AddSegmentedControlCompact(segments => {
                    foreach (var (iconName, sprite, place, title, body) in items) {
                        var active = OutlinerGrouping.ByPlace == place;
                        var action = active ? (Action)(() => { }) : presentation.Action(() => SwitchOutlinerView(place));
                        var segment = segments.AddSegment("", active, action);
                        segment.Tooltip(title, body + (active ? " Shown now." : ""));
                        AttachViewIcon(segment.RectTransform, sprite, iconName);
                    }
                });
            } catch (Exception e) {
                PatcherLog.Warning("The patcher couldn't show the outliner view icons. Use the By category or By place button beside the object count to switch views. Report this to the patcher maintainer and include the Player log.", e);
                Button(row, OutlinerGrouping.ByPlace ? "By category" : "By place", () => SwitchOutlinerView(!OutlinerGrouping.ByPlace));
            }
        }

        static void AttachViewIcon(RectTransform segment, Sprite fallback, string iconName) {
            // A native segment is a RailroaderButton. Its Awake reads the colour of the text it finds among active
            // objects, so a switched-off text object left it none: the first Show of a window built while hidden threw
            // in Awake for each segment. The text stays active and empty, and takes no room in the segment's layout.
            foreach (var label in segment.GetComponentsInChildren<TMP_Text>(true)) {
                label.text = "";
                if (label.gameObject != segment.gameObject)
                    (label.GetComponent<LayoutElement>() ?? label.gameObject.AddComponent<LayoutElement>()).ignoreLayout = true;
            }
            var hlg = segment.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) {
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = false;
                hlg.padding = new RectOffset(6, 6, 4, 4);
            }
            var layout = segment.GetComponent<LayoutElement>() ?? segment.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = ViewIconButtonWidth;
            layout.minHeight = layout.preferredHeight = RowHeight;
            layout.flexibleWidth = 0; layout.flexibleHeight = 0;

            Sprite sprite = fallback;
            if (UI.SpriteLibrary.TryGet(iconName, out var registered) && registered != null) sprite = registered;
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image)) { layer = segment.gameObject.layer };
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(segment, false);
            iconRect.sizeDelta = new Vector2(ViewIconGlyphSize, ViewIconGlyphSize);
            var image = iconObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var iconLayout = iconObject.AddComponent<LayoutElement>();
            iconLayout.minWidth = iconLayout.preferredWidth = ViewIconGlyphSize;
            iconLayout.minHeight = iconLayout.preferredHeight = ViewIconGlyphSize;
            iconLayout.flexibleWidth = 0; iconLayout.flexibleHeight = 0;
        }

        static void RegisterViewIcons() {
            if (!spriteLibraryLogged) {
                spriteLibraryLogged = true;
                try {
                    var names = UI.SpriteLibrary.Shared == null ? "the game has no shared icon library" : String.Join(", ", UI.SpriteLibrary.Shared.entries.Select(entry => entry.name));
                    PatcherLog.Debug("Icon names available to the outliner: " + names);
                } catch (Exception e) { PatcherLog.Debug("The patcher couldn't read the game's icon names for the outliner: " + e.GetBaseException().Message); }
            }
            if (byCategorySprite == null) byCategorySprite = Draw(ByCategoryIcon, ListPixel);
            if (byPlaceSprite == null) byPlaceSprite = Draw(ByPlaceIcon, PinPixel);
            if (byCategoryRegistration == null) byCategoryRegistration = UI.SpriteLibrary.Register(ByCategoryIcon, byCategorySprite);
            if (byPlaceRegistration == null) byPlaceRegistration = UI.SpriteLibrary.Register(ByPlaceIcon, byPlaceSprite);
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
