// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

namespace RailroaderScenePatcher {
    internal static class WorldMarkingDiagnostics {
        internal const string DrawFailed = "Couldn't show world markings.";

        internal static IEnumerable<string> RendererLines<T>(IReadOnlyList<T> renderers, Func<T, bool> isLive, Func<T, int, string> describe) {
            for (var index = 0; index < renderers.Count; index++) {
                var renderer = renderers[index];
                if (isLive(renderer)) yield return describe(renderer, index);
            }
        }
    }
}
