// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // Text rules shared by every message.
    public static class Prose {
        // Names in running text: "a", "a and b", "a, b, and c", with the serial comma. Empty for no names.
        public static string Join(IEnumerable<string> items) {
            var names = (items ?? Enumerable.Empty<string>()).ToArray();
            if (names.Length < 2) return String.Join("", names);
            return String.Join(", ", names.Take(names.Length - 1)) + (names.Length == 2 ? " and " : ", and ") + names[names.Length - 1];
        }
    }
}
