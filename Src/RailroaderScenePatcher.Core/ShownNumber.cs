// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System.Globalization;

namespace RailroaderScenePatcher.Core {
    // Every number the window shows or fills a field with: metres and degrees to three decimals, without trailing
    // zeros, never in scientific notation, and no negative zero. A value below half a thousandth reads 0. Stored
    // values and key forms keep their full precision; only the shown text follows this rule.
    public static class ShownNumber {
        public static string Text(double value) {
            var text = value.ToString("0.###", CultureInfo.InvariantCulture);
            return text == "-0" ? "0" : text;
        }
    }
}
