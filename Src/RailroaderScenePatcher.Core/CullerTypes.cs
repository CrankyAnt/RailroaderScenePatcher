// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT

namespace RailroaderScenePatcher.Core {
    // The full type names of the game's cullers and of its culling manager.
    public static class CullerTypes {
        public const string SimpleCuller = "Helpers.SimpleCuller", RendererCuller = "Helpers.Culling.RendererCuller",
            GameObjectCuller = "Helpers.Culling.GameObjectCuller", CullingManager = "Helpers.Culling.CullingManager";
    }
}
