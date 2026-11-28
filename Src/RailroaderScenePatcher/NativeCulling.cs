// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System.Reflection;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // Native members of the game's culling that several capabilities read.
    internal static class NativeCulling {
        // The culling spheres of a culling manager, looked up on each read; null when the game lacks them.
        internal static FieldInfo ManagerSpheres => Native.Type(CullerTypes.CullingManager)?.GetField("_spheres", Native.Flags);
    }
}
