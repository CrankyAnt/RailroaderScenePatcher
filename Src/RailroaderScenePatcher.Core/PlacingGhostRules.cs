// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

namespace RailroaderScenePatcher.Core {
    // What the placing ghost of a mod object shows of its source: what the placed object will show.
    public static class PlacingGhostRules {
        // Whether the placed object shows what stands at node: every object from node up to the source, the source
        // itself left out, is active or is on the list of one of the source's cullers. A game-object culler switches
        // the objects on its list off while its sphere is out of view and a simple culler its own object (Railroader
        // 0.6.3, GameObjectCuller.CullingSphereStateChanged), and the copy switches them on until its own culler
        // decides (ModObjectWorld), so a source out of view still has its masts and gates in the ghost. An object
        // switched off otherwise stays out. The source's own activity does not count: the ghost stands for the placed
        // object, not for the source where it stands. A node outside the source shows nothing.
        public static bool ShownWhenPlaced<T>(T node, T source, Func<T, T> parent, Func<T, bool> active, ICollection<T> culled) where T : class {
            if (node == null || source == null || parent == null || active == null) return false;
            for (var at = node; at != null; at = parent(at)) {
                if (ReferenceEquals(at, source)) return true;
                if (!active(at) && (culled == null || !culled.Contains(at))) return false;
            }
            return false;
        }
    }
}
