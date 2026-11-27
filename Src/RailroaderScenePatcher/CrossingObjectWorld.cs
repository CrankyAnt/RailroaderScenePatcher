// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The crossing objects of the loaded scene (ICrossingObjectWorld): the object that holds the crossbucks and
    // crossing pads of a crossing without a crossing controller, such as Crossing Unprotected (1) at Alarka Jct. The
    // game gives it no component of its own, so it is known by what stands directly in it: a crossing pad. A mod
    // object this patcher built from one is a crossing object too.
    internal sealed class CrossingObjectWorld : ICrossingObjectWorld {
        public IEnumerable<string> MapIds => ModScenePatchFiles.MapIds();
        static IEnumerable<Component> PadMarkers(GameObject crossing) {
            var marker = Native.Type(TrackMarkerWorld.MarkerType);
            if (marker == null) yield break;
            for (var i = 0; i < crossing.transform.childCount; i++) {
                var child = crossing.transform.GetChild(i).gameObject;
                var held = child.GetComponent(marker);
                if (held != null && TrackMarkerWorld.Get(child, TrackMarkerWorld.PadBuilder) != null) yield return held;
            }
        }
        internal static bool IsCrossingObject(GameObject go) {
            if (go == null || !TrackMarkerWorld.Loaded(go)) return false;
            var controller = Native.Type(CrossingCoverageScene.ComponentType); var marker = Native.Type(TrackMarkerWorld.MarkerType);
            if ((controller != null && go.GetComponentInParent(controller) != null) || (marker != null && go.GetComponent(marker) != null)) return false;
            return ModObjectWorld.IsModObject(go) || PadMarkers(go).Any();
        }
        public IReadOnlyList<object> Find(string scene, string path) => BoardScene.LoadedAt(scene, path).Where(IsCrossingObject).Cast<object>().ToArray();
        public IReadOnlyList<string> PadMarkerIds(object crossing) =>
            crossing is GameObject go && go != null ? PadMarkers(go).Select(m => TrackMarkerWorld.Field(m, "id") as string).Where(id => !String.IsNullOrEmpty(id)).ToArray() : new string[0];
        public double[] Position(object crossing) {
            var go = (GameObject)crossing;
            var at = NativeInventory.MapPosition(go.transform.position, NativeInventory.MovesWithOrigin(go.transform));
            return new double[] { at.x, at.y, at.z };
        }
        public double[] Rotation(object crossing) { var r = ((GameObject)crossing).transform.rotation; return new double[] { r.x, r.y, r.z, r.w }; }
        public bool Active(object crossing) => ((GameObject)crossing).activeSelf;
    }
}
