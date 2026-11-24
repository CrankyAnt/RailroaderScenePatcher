// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher.Core {
    // A process-wide availability that the state of one local world owns, such as whether the game's ground mask members
    // were checked for this load. When the map context changes, the state of the next world takes it over before Unity
    // destroys the state of the earlier world. The teardown of a world therefore clears the availability only while that
    // world still owns it, and leaves the next world's availability standing.
    public sealed class WorldOwnedFlag {
        object owner;
        public bool Held => owner != null;
        public void Take(object world) { owner = world ?? throw new ArgumentNullException(nameof(world)); }
        public void Release(object world) { if (world != null && ReferenceEquals(owner, world)) owner = null; }
    }
}
