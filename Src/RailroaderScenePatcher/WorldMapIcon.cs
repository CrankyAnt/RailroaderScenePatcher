// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // The map's icon of a station map icon, drawn in the world: while the icon is selected at its place, and while a
    // duplicate of it is being placed as the ghost on the pointer. The map draws the icon's image on the map layer,
    // which the world camera does not show; this draws the same image over the world view at the icon's place
    // (StationMapIconRules.WorldIcon). It only reads the icon: its activity and its registration with the map stay
    // as they are.
    internal static class WorldMapIcon {
        // Draws the map's icon of the station map icon object icon at a scene position, unless it would cover a window.
        // Call during a repaint event.
        internal static void Draw(GameObject icon, Vector3 scene, IReadOnlyList<Rect> covered) {
            var camera = Camera.main;
            var image = Image(icon);
            if (camera == null || image == null) return;
            var sprite = image.sprite;
            var area = sprite.textureRect;
            if (area.width <= 0 || area.height <= 0 || sprite.texture == null) return;
            var clip = camera.projectionMatrix * camera.worldToCameraMatrix * new Vector4(scene.x, scene.y, scene.z, 1);
            var view = camera.pixelRect;
            var placed = StationMapIconRules.WorldIcon(new ClipPoint(clip.x, clip.y, clip.z, clip.w),
                ScreenRect.FromEdges(view.xMin, Screen.height - view.yMax, view.xMax, Screen.height - view.yMin), area.width / area.height);
            if (!placed.HasValue) return;
            var rect = Rect.MinMaxRect((float)placed.Value.X, (float)placed.Value.Y, (float)placed.Value.Right, (float)placed.Value.Bottom);
            if (covered != null && covered.Any(r => r.Overlaps(rect))) return;
            var texture = sprite.texture;
            var uv = new Rect(area.x / texture.width, area.y / texture.height, area.width / texture.width, area.height / texture.height);
            var before = GUI.color;
            GUI.color = image.color;
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
            GUI.color = before;
        }
        // The image the map shows for the icon: the first image in its hierarchy that has a sprite.
        static Image Image(GameObject icon) =>
            icon == null ? null : icon.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i != null && i.sprite != null);
    }
}
