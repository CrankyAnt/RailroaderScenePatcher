// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // UI-independent state shared by the native adapter and controlled inventory replay.
    internal sealed class InventoryPresentationState {
        bool? stale;
        IReadOnlyList<SceneObservation> entries;
        SceneObservation selection;
        string search;
        bool initialized;

        internal bool Observe(SceneInventory inventory, SceneObservation selected, string filter) {
            // Entries is replaced by every successful scan, even when all displayed values match.
            var changed = !initialized || !ReferenceEquals(entries, inventory?.Entries) ||
                !ReferenceEquals(selection, selected) || search != filter || stale != inventory?.IsStale;
            entries = inventory?.Entries; selection = selected; search = filter; initialized = true;
            stale = inventory?.IsStale;
            return changed;
        }

        internal bool CanSelect(SceneInventory inventory, SceneObservation row) =>
            inventory != null && !inventory.IsStale && row != null && inventory.Entries.Contains(row);

        internal SceneObservation CurrentSelection(SceneInventory inventory, SceneObservation previous) {
            if (previous == null) return null;
            var matches = inventory.Entries.Where(item => ReferenceEquals(item.Handle, previous.Handle) &&
                item.Identity.Scene == previous.Identity.Scene && item.Identity.Path == previous.Identity.Path &&
                item.Identity.Component == previous.Identity.Component && item.Identity.Structure == previous.Identity.Structure).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }
}
