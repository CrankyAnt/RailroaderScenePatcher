// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // Which inventory entries the outliner shows: every entry, those with a detected problem, or those the active
    // mod modifies.
    // Problems: objects with a detected problem. Changed: objects the edited mod modifies. Disabled: objects whose
    // own active flag is off.
    internal enum OutlinerFilter { All, Problems, Changed, Disabled }
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

        // Whether the filter lets an entry through. changed tells whether the edited mod modifies an entry; without
        // it nothing is changed.
        internal static bool Passes(SceneObservation entry, OutlinerFilter filter, Func<SceneObservation, bool> changed) =>
            filter == OutlinerFilter.All || (filter == OutlinerFilter.Problems ? entry.Problems.Count != 0 : filter == OutlinerFilter.Disabled ? !entry.Enabled : changed != null && changed(entry));
        // The inventory's entries in inventory order that the search text and the filter let through, by what each
        // object says about itself. Neither changes the inventory. The outliner adds what it shows of an object: its
        // category, kind, place and crossing (OutlinerGrouping.Rows).
        internal static IReadOnlyList<SceneObservation> Rows(SceneInventory inventory, string search, OutlinerFilter filter, Func<SceneObservation, bool> changed) =>
            inventory == null ? Array.Empty<SceneObservation>() : inventory.Search(search).Where(entry => Passes(entry, filter, changed)).ToArray();
        // What the outliner area states instead of an empty list on a map where the patcher lists no scene object because
        // the game creates every scene object it checked while it runs.
        // The outliner help's term for this state: the reason's own words.
        internal const string NoEditableObjectsTerm = "No objects the patcher can edit";
        internal const string NoEditableObjects = "This map has no objects the patcher can edit. While the map is running, the game creates all the scene objects the patcher checks for editing.";
        // The reason the outliner area states instead of its list, or null: the inventory lists no object, it checked at
        // least one scene object, and it left out every one because the game creates it while it runs. A search or a
        // filter that matches nothing on a map with listed objects states no reason; its count line stays.
        internal static string EmptyListReason(SceneInventory inventory) {
            if (inventory == null || inventory.CheckedCount == 0 || inventory.UnlistedReasons.Count != inventory.CheckedCount) return null;
            if (inventory.Entries.Count - OutlinerGrouping.HiddenCount(inventory.Entries) > 0) return null;
            return inventory.UnlistedReasons.All(reason => reason == SceneObjectRules.RuntimeObject) ? NoEditableObjects : null;
        }
        // The scene root of the detection spans the patcher adds for a crossing.
        internal const string CoverageRoot = "CrankyAnt Scene Patcher crossing coverage";

        const string MarkerIdKey = "ID";
        internal const string MarkerIdLine = "Marker " + MarkerIdKey + "=";

        // The named values of a binding line of the form "Marker ID=Mlm1; type=Generic": the words before the first key
        // name the group ("Marker", "Track-relative", "Circuit 0"), and each "key=value" part follows in order. A part
        // without a key continues the value before it. Null for a line without a key, such as "Graph registration: none".
        internal static (string Group, IReadOnlyList<KeyValuePair<string, string>> Values)? BindingValues(string line) {
            var first = line?.IndexOf('=') ?? -1;
            if (first < 0) return null;
            var space = line.LastIndexOf(' ', first);
            var group = space < 0 ? "" : line.Substring(0, space);
            var values = new List<KeyValuePair<string, string>>();
            string key = null, value = null;
            foreach (var part in line.Substring(space + 1).Split(new[] { "; " }, StringSplitOptions.None)) {
                var equals = part.IndexOf('=');
                if (equals <= 0) { if (key != null) value += "; " + part; continue; }
                if (key != null) values.Add(new KeyValuePair<string, string>(key, value));
                key = part.Substring(0, equals); value = part.Substring(equals + 1);
            }
            if (key != null) values.Add(new KeyValuePair<string, string>(key, value));
            return (group, values);
        }
        // The marker ID an entry was observed with: the ID value of its Marker binding line (BindingValues), or null for
        // an entry without one.
        internal static string MarkerIdOf(SceneObservation entry) {
            foreach (var line in entry?.Bindings ?? Enumerable.Empty<string>()) {
                var values = BindingValues(line);
                if (values?.Group != "Marker") continue;
                var id = values.Value.Values.FirstOrDefault(pair => pair.Key == MarkerIdKey).Value?.Trim();
                if (!String.IsNullOrEmpty(id)) return id;
            }
            return null;
        }
        // The other entries that hold the marker ID an object had before an edit, its Undo or its Redo, or the one it has
        // after it: their duplicate marker ID problem and explanation change with the edit, so they are observed again.
        internal static IReadOnlyList<SceneObservation> OtherMarkerIdHolders(IEnumerable<SceneObservation> entries, SceneObservation before, SceneObservation after) {
            var ids = new HashSet<string>(new[] { MarkerIdOf(before), MarkerIdOf(after) }.Where(id => id != null), StringComparer.Ordinal);
            var self = after?.Handle ?? before?.Handle;
            if (ids.Count == 0) return Array.Empty<SceneObservation>();
            return (entries ?? Enumerable.Empty<SceneObservation>()).Where(e => e != null && !ReferenceEquals(e.Handle, self) && MarkerIdOf(e) is string id && ids.Contains(id)).ToArray();
        }
        // Child objects include their group; other objects use their observed visible name.
        internal static string Name(SceneObservation entry) => entry.GroupPath != null ? SceneObjectRules.PartName(entry.GroupPath, entry.Identity.Path)
            : entry.DisplayName == Last(entry.Identity.Path) && SharedPathNames.TryGetValue(entry, out var numbered) ? numbered : entry.DisplayName;
        // The shown names of the objects that share their hierarchy path and component with another listed object, with
        // their sequence numbers, as the outliner last numbered them (OutlinerGrouping). Only the shown name changes.
        internal static IReadOnlyDictionary<SceneObservation, string> SharedPathNames = new Dictionary<SceneObservation, string>();
        internal static string Numbered(string name, int number) => name + " (" + number + ")";
        static string Last(string path) => path.Substring(path.LastIndexOf('/') + 1);

        // The suffix of an added name (AddedEquipmentName); an Equipment ID holds no parenthesis.
        static readonly Regex AddedSuffix = new Regex(@" \(added(?:: [^()]+)?\)\z");
        internal static string NarrowName(string family, string name, float width, Func<string, float> measure) {
            if (!CommandBand.IsAddedFamily(family) || measure(name) <= width) return name;
            var suffix = AddedSuffix.Match(name);
            // Cut only the name, on a text-element boundary. Measure with the suffix and any caller-supplied markup.
            var elements = StringInfo.ParseCombiningCharacters(suffix.Success ? name.Substring(0, suffix.Index) : name);
            var shortened = "…" + suffix.Value;
            // Too narrow even for the ellipsis and the suffix: the ellipsis still marks the cut source name, and the
            // outliner cuts the rest at the end of the row.
            if (suffix.Success && measure(shortened) > width) return shortened;
            var lower = 0;
            var upper = elements.Length - 1;
            while (lower <= upper) {
                var middle = lower + (upper - lower) / 2;
                var candidate = name.Substring(0, elements[middle]).TrimEnd() + "…" + suffix.Value;
                if (measure(candidate) <= width) {
                    shortened = candidate;
                    lower = middle + 1;
                } else upper = middle - 1;
            }
            return shortened;
        }

        // An outliner row fitted to the width its cell has. The cell first gets the full row; when an added name doesn't
        // fit, its source name is shortened and "(added: <Equipment ID>)" stays (NarrowName). Other rows keep the full
        // text, which the outliner cuts at the end. Markup adds the row's markers and marks around the name.
        internal sealed class FittedRow {
            readonly string family, name;
            readonly Func<string, string> markup;
            internal FittedRow(string family, string name, Func<string, string> markup) { this.family = family; this.name = name; this.markup = markup; }
            internal string Full => markup(name);
            internal string Fit(float width, Func<string, float> measure) => markup(NarrowName(family, name, width, value => measure(markup(value))));
        }

        internal static string EquipmentKind(EquipmentSource source) => source.IsDefinition ? "Bell" : "Light or gate";

        // A source by its visible name, or by the stored-name rule from its recorded source when it has none.
        internal static string EquipmentSourceName(SourceCandidate candidate) {
            var name = String.IsNullOrWhiteSpace(candidate.Name) ? candidate.Source.StoredName() : candidate.Name;
            return String.IsNullOrEmpty(candidate.Crossing) ? name : name + " at " + candidate.Crossing;
        }

        internal static string EquipmentSourceName(EquipmentSource source, IEnumerable<SourceCandidate> candidates) {
            var match = candidates?.FirstOrDefault(candidate => candidate.Source.Scene == source.Scene && candidate.Source.Path == source.Path
                && candidate.Source.Definition == source.Definition);
            return match == null ? source.StoredName() : EquipmentSourceName(match);
        }

        internal static string UnbuiltEquipmentName(EquipmentAddition item, IEnumerable<SourceCandidate> candidates) =>
            item.Id + ": " + EquipmentSourceName(item.Source, candidates);

        // An added light, gate or bell by the source name stored with it and the Equipment ID the modder chose, such as
        // "Signal 1 (added: north-signal)". The ID tells additions from one source apart, so no number is needed.
        internal static string AddedEquipmentName(string sourceName, string id) => sourceName + " (added: " + id + ")";

        // One node of the outliner: a folder (a category, kind, place or crossing), an object, or what stands beneath an
        // object (a part of a group, a detection span of a crossing controller, a member of a crossing). Row is the
        // inventory entry and null for a folder. Context marks an object that the search or filter left out and that
        // is shown only to hold what stands beneath it. Count is the number of matching rows at and below the node.
        internal sealed class OutlineNode {
            internal string Key, Label;
            internal SceneObservation Row;
            internal bool Context;
            internal int Count;
            internal readonly List<OutlineNode> Children = new List<OutlineNode>();
        }
        // A part's name under its group: the path from the top scene object to the part.
        internal static string PartLabel(SceneObservation part) {
            var path = part.Identity.Path; var group = part.GroupPath;
            return group != null && path.StartsWith(group + "/", StringComparison.Ordinal) ? path.Substring(group.Length + 1).Replace("/", " / ") : part.DisplayName;
        }
        // The family of the crossing controllers, which also lists each controller's detection spans.
        internal const string HolderFamily = SceneFamilies.CrossingController;
        // The inventory entry of the controller that holds a detection span, or null: for an object no controller holds, and
        // while the inventory does not hold exactly one entry for the controller.
        internal static SceneObservation HolderOf(SceneInventory inventory, SceneObservation held) {
            if (inventory == null || held?.HolderPath == null) return null;
            var found = inventory.Entries.Where(entry => entry.HolderPath == null && entry.Family == HolderFamily && entry.Identity.Scene == held.Identity.Scene && entry.Identity.Path == held.HolderPath).Take(2).ToArray();
            return found.Length == 1 ? found[0] : null;
        }
        // The Object tab's line that names the controller of a detection span, or null for any other object.
        internal static string HeldBy(SceneObservation entry) =>
            entry?.HolderPath == null ? null : "Detection span of: " + entry.HolderPath.Substring(entry.HolderPath.LastIndexOf('/') + 1);

        // The inventory entry of the group a part belongs to, or null: for a top scene object, for an object outside any
        // group, and while the inventory does not hold exactly one entry for the group.
        internal static SceneObservation GroupOf(SceneInventory inventory, SceneObservation part) {
            if (inventory == null || part?.GroupPath == null) return null;
            var found = inventory.Entries.Where(entry => entry.GroupPath == null && SceneObjectRules.InFamilies(entry.Family) && entry.Identity.Scene == part.Identity.Scene && entry.Identity.Path == part.GroupPath).Take(2).ToArray();
            return found.Length == 1 ? found[0] : null;
        }
        // The mark after an outliner row's name for an object that no supported operation covers; null otherwise.
        internal const string InspectionOnlyMark = "inspection only";
        internal static string RowMark(SceneObservation entry) => entry?.InspectionOnly == null ? null : InspectionOnlyMark;
        // The Object tab's line that names a part's group, or null for any object that is no part.
        internal static string PartOf(SceneObservation entry) =>
            entry?.GroupPath == null ? null : "Part of: " + entry.GroupPath.Substring(entry.GroupPath.LastIndexOf('/') + 1);

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
            if (inventory != null && !inventory.IsStale && !definitionsStale) return "The inventory is up to date. You can inspect and edit objects.";
            var refuses = inventory == null || inventory.IsStale;
            return (inventory == null ? "inspection is unavailable" : inventory.IsStale ? inventory.StaleReason : "the supplied definitions changed or were not read") +
                ". Click Refresh to update the inventory." + (refuses ? " Edits are refused until then." : "");
        }

        // A row of the current snapshot can be inspected, also while the snapshot is stale: only edits need a fresh
        // inventory, and pinning refuses those.
        internal bool CanSelect(SceneInventory inventory, SceneObservation row) =>
            inventory != null && row != null && inventory.Entries.Contains(row);

        internal SceneObservation CurrentSelection(SceneInventory inventory, SceneObservation previous) {
            if (previous == null) return null;
            var matches = inventory.Entries.Where(item => ReferenceEquals(item.Handle, previous.Handle) &&
                TargetIdentity.SameObject(item.Identity, previous.Identity) && item.Identity.Structure == previous.Identity.Structure).Take(2).ToArray();
            // Two observations can share handle and identity: one object with two components of a type, as some CTC
            // objects have. The inspected one is then the entry itself, or the entry that reads the same.
            if (matches.Length <= 1) return matches.Length == 1 ? matches[0] : null;
            return inventory.Entries.FirstOrDefault(item => ReferenceEquals(item, previous)) ?? inventory.Entries.FirstOrDefault(item => item.Matches(previous));
        }
    }
}
