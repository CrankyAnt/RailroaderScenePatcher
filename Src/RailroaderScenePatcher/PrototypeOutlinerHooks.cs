// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: throwaway hooks of the outliner grouping prototype into the scene patch window.
using System;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    public sealed partial class PatcherBehaviour {
        // The map x/z where a starter placement marker or track span acts, from the same track points Focus uses. The
        // grouping asks only for those families (PrototypeOutlinerGrouping.UsesTrackPoint).
        double[] PrototypeTrackPoint(SceneObservation entry) {
            var point = FocusTarget.Point(entry.Family, null, TrackPoints(entry), out _);
            return point != null && point.Length >= 3 ? new[] { point[0], point[2] } : null;
        }

        // The outliner rows, with the prototype's search over place, kind and crossing; the current rows when it fails.
        System.Collections.Generic.IReadOnlyList<SceneObservation> PrototypeRows() {
            try { return PrototypeOutlinerGrouping.Rows(inventory, filter, outlinerFilter, IsChanged, PrototypeTrackPoint); }
            catch (Exception e) when (!(e is OutOfMemoryException)) {
                PrototypeOutlinerGrouping.WarnOnce("rows", "the grouping search failed, so the search matches object fields only: " + e);
                return InventoryPresentationState.Rows(inventory, filter, outlinerFilter, IsChanged);
            }
        }
    }
}
