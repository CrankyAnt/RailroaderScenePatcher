// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using TMPro;
using UI.Builder;

namespace RailroaderScenePatcher {
    // An inactive button looks inactive: besides refusing the click, its label is dimmed as the native segmented
    // control dims an inactive segment. Every button of the panel is switched through here.
    internal static class InactiveStyle {
        internal static IConfigurableElement Inactive(this IConfigurableElement element, bool inactive) {
            element.Disable(inactive);
            try { foreach (var text in element.RectTransform.GetComponentsInChildren<TMP_Text>(true)) text.alpha = inactive ? CommandBand.InactiveAlpha : 1f; } catch { }
            return element;
        }
    }
}
