// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    internal sealed class NativeSnapScene : ISnapScene {
        readonly SpatialSelectionScene selection;
        internal NativeSnapScene(IInspectionScene inventory) { selection = new SpatialSelectionScene(inventory); }
        public IEnumerable<string> MapIds => selection.MapIds;
        public IEnumerable<SelectionCandidate> Discover(SelectionKind kind) => selection.Discover(kind);
        public IEnumerable<SnapGeometry> DiscoverSnapTargets(SelectionKind kind) {
            foreach (var item in selection.Scan(kind)) {
                var points = kind == SelectionKind.TrackSegment ? item.ControlPoints : item.Points;
                yield return new SnapGeometry(item.Choice, (points ?? new Vector3[0]).Select(p => new[] { (double)p.x, p.y, p.z }));
            }
        }
    }
}
