// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The ghost of a mod object while it is being placed: a visual stand-in of its source that stands on the pointer.
    // It holds only the source's meshes with their own materials, at the source's own arrangement: no component of the
    // source, no collider and no script, so the game neither finds nor runs it, and no raycast, such as the ground
    // raycast under the pointer, hits it. It belongs to no scene file and is gone when placing ends.
    internal sealed class PlacingGhost {
        GameObject root;
        GameObject source;

        // Shows the ghost of the source at the map point, with the rotation the placed object would get.
        internal void Show(GameObject from, double[] map, Quaternion rotation) {
            if (from == null || map == null || map.Length != 3) { Hide(); return; }
            if (root == null || !ReferenceEquals(source, from)) Build(from);
            if (root == null) return;
            var scene = NativeInventory.ScenePosition(new Vector3((float)map[0], (float)map[1], (float)map[2]), NativeInventory.MovesWithOrigin(from.transform));
            root.transform.SetPositionAndRotation(scene, rotation);
            if (!root.activeSelf) root.SetActive(true);
        }
        // Hides the ghost and keeps it for the next frame on the pointer.
        internal void Hide() { if (root != null && root.activeSelf) root.SetActive(false); }
        // Removes the ghost.
        internal void Clear() {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null; source = null;
        }

        // The ghost holds one plain mesh renderer for each mesh the source shows, placed relative to the source's own
        // position and rotation. Of a level-of-detail group only the nearest level is taken, so the levels do not
        // overlap.
        void Build(GameObject from) {
            Clear();
            source = from;
            root = new GameObject("CrankyAnt Scene Patcher placing ghost") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var frame = Matrix4x4.TRS(from.transform.position, from.transform.rotation, Vector3.one).inverse;
            var farther = new HashSet<Renderer>();
            foreach (var group in from.GetComponentsInChildren<LODGroup>(true))
                foreach (var level in group.GetLODs().Skip(1))
                    foreach (var renderer in level.renderers ?? new Renderer[0]) if (renderer != null) farther.Add(renderer);
            var count = 0;
            foreach (var renderer in from.GetComponentsInChildren<MeshRenderer>(true)) {
                if (renderer == null || farther.Contains(renderer)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter == null ? null : filter.sharedMesh;
                if (mesh == null) continue;
                var relative = frame * renderer.transform.localToWorldMatrix;
                var part = new GameObject(renderer.name) { hideFlags = HideFlags.HideAndDontSave };
                // The source's own layer, so the camera draws the ghost as it draws the source. Without a collider no
                // raycast hits it.
                part.layer = renderer.gameObject.layer;
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = relative.GetColumn(3);
                part.transform.localRotation = relative.rotation;
                part.transform.localScale = relative.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var shown = part.AddComponent<MeshRenderer>();
                shown.sharedMaterials = renderer.sharedMaterials;
                shown.shadowCastingMode = renderer.shadowCastingMode;
                shown.receiveShadows = renderer.receiveShadows;
                count++;
            }
            if (count == 0) PatcherLog.Debug("The placing ghost of " + from.name + " has no mesh to show; the pointer mark stands alone.");
        }
    }
}
