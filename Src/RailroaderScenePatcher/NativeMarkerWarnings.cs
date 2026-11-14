// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // Warning rows emitted by the native check after a verified restoration of a broken underlying binding.
    internal sealed class NativeMarkerWarnings {
        readonly Dictionary<object, CurrentWarning> restored = new Dictionary<object, CurrentWarning>();
        internal void Verified(object target, bool locationValid, bool active, ICurrentWarnings warnings, Func<CurrentWarning> nativeCheck) {
            if (!locationValid) {
                if (active) { var row = nativeCheck(); if (row != null) restored[target] = row; }
                return;
            }
            if (!restored.TryGetValue(target, out var original)) return;
            var matches = warnings.Current.Where(r => r.Source == original.Source && r.Key == original.Key).ToArray();
            if (matches.Length != 1) return;
            var current = matches[0];
            if (!ReferenceEquals(current.Token, original.Token) || current.Originator != original.Originator || current.Description != original.Description) return;
            warnings.Remove(current);
            restored.Remove(target);
        }
    }
}
