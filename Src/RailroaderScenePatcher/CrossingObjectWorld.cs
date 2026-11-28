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
        // Whether the inventory lists the object as the crossing object of a crossing without signals: it holds a crossing
        // pad directly and is no listed group of its own. A mod object built from one holds its own pads.
        internal static bool IsListedCrossing(GameObject go) => IsCrossingObject(go) && PadMarkers(go).Any() && GroupWorld.FamilyOf(go) == null;
        // Whether other crossing objects stand at the object's path, as the three below Old River Rd do.
        internal static bool SharesPath(GameObject go) => BoardScene.LoadedAt(go.scene.name, BoardScene.PathOf(go.transform)).Count(IsCrossingObject) > 1;
        // The marker ID of the crossing object's first pad, which names it where crossings share a path, or null.
        internal static string PadMarkerId(GameObject go) => PadMarkers(go).Select(m => TrackMarkerWorld.Field(m, "id") as string).FirstOrDefault(id => !String.IsNullOrEmpty(id));
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

    // The crossing object as the root of the whole move of its crossing without signals (WholeMove), in the group world:
    // everything below it moves with its transform, its crossbucks with their parts included, and each track marker
    // below it, a crossing pad, is put back where it stood (Stays). A crossing pad is generated along its marker's track
    // location, so it does not follow a move. A crossbuck with its own placement takes its place in the moved crossing
    // object through the own placements the groups share (PartHolds).
    internal sealed class CrossingObjectGroupWorld : IWholeMoveWorld, IFrameWorld {
        static readonly Type marker = Native.Type(TrackMarkerWorld.MarkerType);
        readonly GroupWorld groups;
        internal CrossingObjectGroupWorld(GroupWorld groups) { this.groups = groups ?? throw new ArgumentNullException(nameof(groups)); }
        static GameObject Crossing(object group) => group as GameObject ?? throw new InvalidOperationException("The crossing object no longer exists");
        public IEnumerable<string> MapIds => groups.MapIds;
        // The crossing capability resolves the crossing object and moves it through this world.
        public IReadOnlyList<object> Find(string scene, string path) => Array.Empty<object>();
        // Why the crossing cannot move now, or null: a ground mask below the crossing object that cannot be read and rebuilt.
        public string Refusal(object group) => GroupWorld.MaskProblem(group as GameObject);
        public GroupObservation Observe(object group) { var root = Crossing(group); return groups.Observe(root, root, null); }
        public void SetActive(object group, bool active) => throw new InvalidOperationException(CrossingObjectScene.NoActivity);
        // The crossing object, or a crossbuck that a held own placement puts back in it (PartHolds.Reassert).
        public void SetPlacement(object group, Placement placement) => groups.SetPlacement(group, placement);
        public IReadOnlyList<object> Stays(object group) {
            var root = Crossing(group);
            if (marker == null) return Array.Empty<object>();
            var kept = new List<object>();
            foreach (var held in root.GetComponentsInChildren(marker, true))
                if (held != null && held.gameObject != root && !kept.Contains(held.gameObject)) kept.Add(held.gameObject);
            return kept;
        }
        // Each kept object keeps the scene position and rotation it has, read before the root moves and written back after.
        public void SetPlacementKeeping(object group, Placement placement, IReadOnlyList<object> kept) {
            var root = Crossing(group);
            var standing = (kept ?? Array.Empty<object>()).OfType<GameObject>().Where(go => go != null).Select(go => (go.transform, go.transform.position, go.transform.rotation)).ToArray();
            groups.SetPlacement(root, placement);
            foreach (var (transform, position, rotation) in standing) transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();
        }
        public IReadOnlyList<object> FindParts(string scene, string path) => Array.Empty<object>();
        public string PartRefusal(object part) => groups.PartRefusal(part);
        public string ActivityRefusal(object part) => groups.ActivityRefusal(part);
        public string DisableRefusal(object target) => groups.DisableRefusal(target);
        public string FollowingRefusal(object target) => CrossingObjectScene.NoFollowing;
        public string PlacementRefusal(object target) => groups.PlacementRefusal(target);
        public PartObservation ObservePart(object part) => groups.ObservePart(part);
        public void SetPartActive(object part, bool active) => groups.SetPartActive(part, active);
        public void SetPartPlacement(object part, Placement placement) => groups.SetPartPlacement(part, placement);
        public void RefreshCulling(object group, CullingPlan plan) => groups.RefreshCulling(group, plan);
        public IReadOnlyList<IReadOnlyList<double>> Presence(object part, IReadOnlyList<object> without) => groups.Presence(part, without);
        public IReadOnlyList<double> Anchor(object part) => groups.Anchor(part);
        public bool Below(object part, object above) => groups.Below(part, above);
        public bool Alive(object handle) => groups.Alive(handle);
        public object FrameAbove(object handle) => groups.FrameAbove(handle);
        public object ModObjectOf(object handle) => groups.ModObjectOf(handle);
        public Placement PlacementOf(object handle) => groups.PlacementOf(handle);
    }
}
