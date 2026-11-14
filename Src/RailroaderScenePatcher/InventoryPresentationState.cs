// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // Which inventory entries the outliner shows: every entry, those with a detected problem, or those the active
    // MapMod modifies.
    internal enum OutlinerFilter { All, Problems, Changed }
    // UI-independent state shared by the native adapter and controlled inventory replay.
    internal sealed class InventoryPresentationState {
        bool? stale;
        IReadOnlyList<SceneObservation> entries;
        SceneObservation selection;
        string search;
        OutlinerFilter mode;
        string changes;
        bool initialized;

        // changedStamp is any value that differs whenever the set of changed objects can differ.
        internal bool Observe(SceneInventory inventory, SceneObservation selected, string filter, OutlinerFilter filterMode = OutlinerFilter.All, string changedStamp = null) {
            // Entries is replaced by every successful scan, even when all displayed values match.
            var changed = !initialized || !ReferenceEquals(entries, inventory?.Entries) ||
                !ReferenceEquals(selection, selected) || search != filter || mode != filterMode || changes != changedStamp || stale != inventory?.IsStale;
            entries = inventory?.Entries; selection = selected; search = filter; mode = filterMode; changes = changedStamp; initialized = true;
            stale = inventory?.IsStale;
            return changed;
        }

        // The outliner rows in inventory order. The search text and the filter each narrow them; neither changes the
        // inventory. changed tells whether the active MapMod modifies an entry; without it nothing is changed.
        internal static IReadOnlyList<SceneObservation> Rows(SceneInventory inventory, string search, OutlinerFilter filter, Func<SceneObservation, bool> changed) =>
            inventory == null ? Array.Empty<SceneObservation>() : inventory.Search(search).Where(entry =>
                filter == OutlinerFilter.All || (filter == OutlinerFilter.Problems ? entry.Problems.Count != 0 : changed != null && changed(entry))).ToArray();

        internal static string Summary(SceneInventory inventory, string search, OutlinerFilter filter, Func<SceneObservation, bool> changed) =>
            Rows(inventory, search, filter, changed).Count + " of " + (inventory?.Entries.Count ?? 0) + " objects";

        // The object's own name: the last part of its hierarchy path.
        internal static string Name(SceneObservation entry) => entry.Identity.Path.Substring(entry.Identity.Path.LastIndexOf('/') + 1);

        // The rows under their family, families in name order, rows in the order given.
        internal static IReadOnlyList<KeyValuePair<string, IReadOnlyList<SceneObservation>>> Groups(IEnumerable<SceneObservation> rows) =>
            rows.GroupBy(row => row.Family ?? "").OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new KeyValuePair<string, IReadOnlyList<SceneObservation>>(group.Key, group.ToArray())).ToArray();

        // The outliner width that fits most names: the 95th percentile of the name widths, or the longest of fewer than
        // twenty, plus the row decoration. The column takes at most 40 percent of the window and leaves the inspector its
        // minimum beside the divider; it never drops below its own minimum.
        internal static float FitWidth(IEnumerable<float> nameWidths, float decoration, float windowWidth, float minimum, float inspectorMinimum, float divider) {
            var widths = (nameWidths ?? Array.Empty<float>()).OrderBy(width => width).ToArray();
            if (widths.Length == 0) return minimum;
            var name = widths.Length < 20 ? widths[widths.Length - 1] : widths[(int)Math.Ceiling(0.95 * widths.Length) - 1];
            var cap = Math.Min(0.4f * windowWidth, windowWidth - divider - inspectorMinimum);
            return (float)Math.Round(Math.Max(minimum, Math.Min(name + decoration, cap)));
        }

        // The inventory line of the panel. Edits are refused while the inventory is missing or stale, because pinning
        // refuses then; changed definitions alone refuse no edit.
        internal static string Notice(SceneInventory inventory, bool definitionsStale) {
            if (inventory != null && !inventory.IsStale && !definitionsStale) return "Inventory snapshot. Changes are checked while this panel is open. Refresh after native edits.";
            var refuses = inventory == null || inventory.IsStale;
            return "Inventory is stale: " + (inventory == null ? "inspection is unavailable" : inventory.IsStale ? inventory.StaleReason : "the supplied definitions changed or were not read") +
                ". Refresh before relying on these observations." + (refuses ? " Edits are refused until then." : "");
        }

        // A row of the current snapshot can be inspected, also while the snapshot is stale: only edits need a fresh
        // inventory, and pinning refuses those.
        internal bool CanSelect(SceneInventory inventory, SceneObservation row) =>
            inventory != null && row != null && inventory.Entries.Contains(row);

        internal SceneObservation CurrentSelection(SceneInventory inventory, SceneObservation previous) {
            if (previous == null) return null;
            var matches = inventory.Entries.Where(item => ReferenceEquals(item.Handle, previous.Handle) &&
                item.Identity.Scene == previous.Identity.Scene && item.Identity.Path == previous.Identity.Path &&
                item.Identity.Component == previous.Identity.Component && item.Identity.Structure == previous.Identity.Structure).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }
}
