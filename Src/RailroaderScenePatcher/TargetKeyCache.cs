// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Runtime.CompilerServices;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The key of a scene object as the game gave it (TargetKeys): its marker ID, where it holds a track marker, and its
    // map x/z. The key is read the first time the engine resolves the object or the inventory lists it. Both happen
    // before this mod changes the object: the engine resolves a target before it applies anything to it, and an edit
    // in the window starts from the listed object. A later move or replacement ID therefore leaves the key as it was,
    // so the target keeps naming the same object. The key of an object leaves with the object.
    internal static class TargetKeyCache {
        static readonly ConditionalWeakTable<GameObject, TargetKey> keys = new ConditionalWeakTable<GameObject, TargetKey>();
        // The key of a resolved object: a GameObject, or a component on one. Null when it cannot be read.
        internal static TargetKey Of(object handle) {
            var go = handle as GameObject ?? (handle as Component)?.gameObject;
            if (go == null) return null;
            if (keys.TryGetValue(go, out var known)) return known;
            TargetKey read;
            try { read = Read(go); }
            catch (Exception e) when (!(e is OutOfMemoryException)) {
                PatcherLog.Debug("Target key unavailable for " + go.scene.name + "/" + BoardScene.PathOf(go.transform) + ": " + e.GetBaseException().Message);
                return null;
            }
            if (read == null) return null;
            keys.Add(go, read);
            return read;
        }
        static TargetKey Read(GameObject go) {
            string markerId = null;
            var marker = TrackMarkerWorld.Get(go, TargetKeys.MarkerComponent);
            if (marker != null) markerId = TrackMarkerWorld.Field(marker, "id") as string;
            var map = NativeInventory.MapPosition(go.transform);
            if (Single.IsNaN(map.x) || Single.IsNaN(map.z) || Single.IsInfinity(map.x) || Single.IsInfinity(map.z)) return null;
            return new TargetKey(String.IsNullOrEmpty(markerId) ? null : markerId, new double[] { map.x, map.z });
        }
    }
}
