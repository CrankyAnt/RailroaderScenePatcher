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

        // The outliner rows in inventory order. The search text and the filter each narrow them; neither changes the
        // inventory. changed tells whether the edited mod modifies an entry; without it nothing is changed.
        internal static IReadOnlyList<SceneObservation> Rows(SceneInventory inventory, string search, OutlinerFilter filter, Func<SceneObservation, bool> changed) =>
            inventory == null ? Array.Empty<SceneObservation>() : inventory.Search(search).Where(entry =>
                filter == OutlinerFilter.All || (filter == OutlinerFilter.Problems ? entry.Problems.Count != 0 : filter == OutlinerFilter.Disabled ? !entry.Enabled : changed != null && changed(entry))).ToArray();

        internal static string Summary(SceneInventory inventory, string search, OutlinerFilter filter, Func<SceneObservation, bool> changed) =>
            Rows(inventory, search, filter, changed).Count + " of " + (inventory?.Entries.Count ?? 0) + " objects";

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
        // Baked scenery parts include their group; other objects use their observed visible name.
        internal static string Name(SceneObservation entry) => entry.GroupPath != null ? BakedSceneryRules.PartName(entry.GroupPath, entry.Identity.Path) : entry.DisplayName;

        static readonly Regex AddedSuffix = new Regex(@" \(added(?:, [0-9]+ of [0-9]+)?\)\z");
        internal static string NarrowName(string family, string name, float width, Func<string, float> measure) {
            if (!CommandBand.IsAddedFamily(family) || measure(name) <= width) return name;
            var suffix = AddedSuffix.Match(name);
            // Cut only the name, on a text-element boundary. Measure with the suffix and any caller-supplied markup.
            var elements = StringInfo.ParseCombiningCharacters(suffix.Success ? name.Substring(0, suffix.Index) : name);
            var shortened = "…" + suffix.Value;
            if (suffix.Success && measure(shortened) > width) return suffix.Value.Substring(1);
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

        internal static string OutlinerTooltip(SceneObservation item, bool changed, FailureKind? failure = null) {
            var equipment = CommandBand.IsAddedFamily(item.Family);
            return (Name(item) + "\n" + (equipment ? "" : item.Identity.Path + "\n") + item.Family
                + (item.Category == null ? "" : "\n" + item.Category + (item.Subcategory == null ? "" : " / " + item.Subcategory))
                + (item.Active ? "" : "\nInactive") + (item.InspectionOnly == null ? "" : "\n" + CommandBand.InspectionOnlyLine(item.InspectionOnly))
                + (failure != null ? "\n" + CommandBand.FailureTooltipLine(failure.Value) : changed ? "\nChanged by this mod" : "")
                + (item.Problems.Count == 0 ? "" : equipment ? "\nDetected problems: " + item.Problems.Count : "\n" + String.Join("\n", item.Problems)))
                .Replace("<", "\\u003c").Replace(">", "\\u003e");
        }

        // An added light, gate or bell by the source name stored with it, numbered among the additions of its crossing with the
        // same name and kind. The supplying scope and addition ID keep the order independent of the scene and outliner order.
        internal static string AddedEquipmentName((string Scope, string Id, string Name, bool IsBell) equipment,
            IEnumerable<(string Scope, string Id, string Name, bool IsBell)> crossingEquipment) {
            var matches = crossingEquipment.Where(item => item.IsBell == equipment.IsBell && item.Name == equipment.Name)
                .OrderBy(item => item.Scope, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
            var ordinal = Array.IndexOf(matches, equipment) + 1;
            return matches.Length > 1 && ordinal > 0
                ? FormattableString.Invariant($"{equipment.Name} (added, {ordinal} of {matches.Length})")
                : equipment.Name + " (added)";
        }

        // The group of the objects that are inspection only: outside the family groups, after them, and absent while
        // it holds no entry.
        internal const string InspectionOnlyGroup = "Inspection only";
        // The rows under their family, families in name order, rows in the order of their names and then their paths, so
        // two lists of one family compare row by row. The rows that are inspection only leave their families for the one
        // Inspection only group at the end, whatever their family, in the same order.
        // A baked scenery group that no supported operation covers yet keeps its family, where it stands under its
        // category: only an inspection-only object without a category goes to the Inspection only group.
        internal static IReadOnlyList<KeyValuePair<string, IReadOnlyList<SceneObservation>>> Groups(IEnumerable<SceneObservation> rows) {
            var listed = (rows ?? Array.Empty<SceneObservation>()).OrderBy(Name, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Identity.Path, StringComparer.Ordinal).ToArray();
            bool Only(SceneObservation row) => row.InspectionOnly != null && row.Category == null;
            // An object with a category stands in the Baked scenery group family, whatever family lists it: the bridge
            // and the construction controller of a rail bridge construction stand with the other rail bridges.
            // A detection span stands in the family of the crossing controller that holds it, under that controller.
            var groups = listed.Where(row => !Only(row)).GroupBy(row => row.Category != null || row.GroupPath != null ? BakedSceneryRules.Family : row.HolderPath != null ? HolderFamily : row.Family ?? "").OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new KeyValuePair<string, IReadOnlyList<SceneObservation>>(group.Key, group.ToArray())).ToList();
            var only = listed.Where(Only).ToArray();
            if (only.Length != 0) groups.Add(new KeyValuePair<string, IReadOnlyList<SceneObservation>>(InspectionOnlyGroup, only));
            return groups;
        }

        // One node of the Baked scenery group family in the outliner: a category or subgroup folder, a group, or a
        // part. Row is the inventory entry of a group or part and null for a folder. Context marks a group that the
        // search or filter left out and that is shown only to hold its parts. Count is the number of matching rows
        // at and below the node.
        internal sealed class OutlineNode {
            internal string Key, Label;
            // For a group, where it stands: the path between World/Large Scenery, or World, and its name.
            internal string Where;
            internal SceneObservation Row;
            internal bool Context;
            internal int Count;
            internal readonly List<OutlineNode> Children = new List<OutlineNode>();
        }
        // Where a group stands, to tell groups of one name apart, such as the Tall Post Shade of each town: the path
        // between World/Large Scenery, or World, and the group's name, or null directly below World.
        internal static string GroupWhere(string path) {
            if (path == null) return null;
            const string Scenery = "World/Large Scenery/", World = "World/";
            var from = path.StartsWith(Scenery, StringComparison.Ordinal) ? Scenery.Length : path.StartsWith(World, StringComparison.Ordinal) ? World.Length : 0;
            var to = path.LastIndexOf('/');
            return to <= from ? null : path.Substring(from, to - from).Replace("/", " / ");
        }
        // A part's name under its group: the path from the group root to the part.
        internal static string PartLabel(SceneObservation part) {
            var path = part.Identity.Path; var group = part.GroupPath;
            return group != null && path.StartsWith(group + "/", StringComparison.Ordinal) ? path.Substring(group.Length + 1).Replace("/", " / ") : part.DisplayName;
        }
        // The rows of the Baked scenery group family as the outliner nests them: the categories in the family's
        // order, Bridge under its subgroups, each group under its category and each part under its group. A part
        // never stands as a loose row: when the search or filter left its group out, the group is taken from the
        // whole inventory and shown to hold it. A category or subgroup without rows is not shown.
        // search is the outliner's search text. A group root names every component below it among its dependencies, so
        // a search for a part finds the group's row too: a group that holds matching parts and does not match by
        // what it says about itself (SceneObservation.MatchesItself) is shown to hold them, as one the search left out.
        internal static IReadOnlyList<OutlineNode> Categories(IReadOnlyList<SceneObservation> rows, IEnumerable<SceneObservation> inventory, string search) {
            rows = rows ?? Array.Empty<SceneObservation>();
            var all = (inventory ?? rows).Where(entry => entry.GroupPath == null && entry.Family == BakedSceneryRules.Family).ToArray();
            // The crossbucks of crossings that share one path differ by their crossing's pad marker ID.
            string GroupKey(string scene, string path, string padMarkerId = null) => scene + "\n" + path + (padMarkerId == null ? "" : "\n" + padMarkerId);
            var groups = new Dictionary<string, OutlineNode>(StringComparer.Ordinal);
            var order = new List<OutlineNode>();
            OutlineNode Group(SceneObservation row, string scene, string path, bool context) {
                var key = GroupKey(scene, path, row?.Identity.PadMarkerId);
                if (!groups.TryGetValue(key, out var node)) {
                    groups[key] = node = new OutlineNode { Key = key, Label = row?.DisplayName ?? path.Substring(path.LastIndexOf('/') + 1), Row = row, Context = context, Where = GroupWhere(path) };
                    order.Add(node);
                }
                return node;
            }
            foreach (var row in rows.Where(row => row.GroupPath == null)) Group(row, row.Identity.Scene, row.Identity.Path, false).Count = 1;
            foreach (var part in rows.Where(row => row.GroupPath != null)) {
                var key = GroupKey(part.Identity.Scene, part.GroupPath);
                if (!groups.TryGetValue(key, out var holder))
                    holder = Group(all.FirstOrDefault(entry => entry.Identity.Scene == part.Identity.Scene && entry.Identity.Path == part.GroupPath), part.Identity.Scene, part.GroupPath, true);
                holder.Children.Add(new OutlineNode { Key = GroupKey(part.Identity.Scene, part.Identity.Path), Label = PartLabel(part), Row = part, Count = 1 });
                holder.Count++;
            }
            foreach (var node in order)
                if (!node.Context && node.Row != null && node.Children.Count != 0 && !node.Row.MatchesItself(search)) { node.Context = true; node.Count--; }
            foreach (var node in order) node.Children.Sort((a, b) => { var by = StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label); return by != 0 ? by : StringComparer.Ordinal.Compare(a.Row.Identity.Path, b.Row.Identity.Path); });
            var sorted = order.OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Key, StringComparer.Ordinal).ToArray();
            var found = new List<OutlineNode>();
            foreach (var category in BakedSceneryCategories.All) {
                var folder = new OutlineNode { Key = category, Label = category };
                var members = sorted.Where(node => (node.Row?.Category ?? BakedSceneryCategories.Other) == category).ToArray();
                var subgroups = BakedSceneryCategories.Subcategories(category);
                foreach (var subgroup in subgroups) {
                    var inner = new OutlineNode { Key = category + "/" + subgroup, Label = subgroup };
                    inner.Children.AddRange(members.Where(node => node.Row?.Subcategory == subgroup));
                    inner.Count = inner.Children.Sum(node => node.Count);
                    if (inner.Children.Count != 0) folder.Children.Add(inner);
                }
                folder.Children.AddRange(members.Where(node => !subgroups.Contains(node.Row?.Subcategory)));
                folder.Count = folder.Children.Sum(node => node.Count);
                if (folder.Children.Count != 0) found.Add(folder);
            }
            return found;
        }
        // The family of the crossing controllers, which also lists each controller's detection spans.
        internal const string HolderFamily = "Crossing controller";
        // The rows of the Crossing controller family as the outliner nests them: each controller, in name order, with
        // its detection spans beneath it, by name. A detection span never stands as a loose row: when the search or filter left
        // its controller out, the controller is taken from the whole inventory and shown to hold it.
        internal static IReadOnlyList<OutlineNode> Held(IReadOnlyList<SceneObservation> rows, IEnumerable<SceneObservation> inventory) {
            rows = rows ?? Array.Empty<SceneObservation>();
            var all = (inventory ?? rows).Where(entry => entry.HolderPath == null && entry.Family == HolderFamily).ToArray();
            string Key(string scene, string path) => scene + "\n" + path;
            var holders = new Dictionary<string, OutlineNode>(StringComparer.Ordinal);
            var order = new List<OutlineNode>();
            OutlineNode Holder(SceneObservation row, string scene, string path, bool context) {
                var key = Key(scene, path);
                if (!holders.TryGetValue(key, out var node)) {
                    holders[key] = node = new OutlineNode { Key = key, Label = row == null ? path.Substring(path.LastIndexOf('/') + 1) : Name(row), Row = row, Context = context };
                    order.Add(node);
                }
                return node;
            }
            foreach (var row in rows.Where(row => row.HolderPath == null)) Holder(row, row.Identity.Scene, row.Identity.Path, false).Count = 1;
            foreach (var held in rows.Where(row => row.HolderPath != null)) {
                var key = Key(held.Identity.Scene, held.HolderPath);
                if (!holders.TryGetValue(key, out var holder))
                    holder = Holder(all.FirstOrDefault(entry => entry.Identity.Scene == held.Identity.Scene && entry.Identity.Path == held.HolderPath), held.Identity.Scene, held.HolderPath, true);
                holder.Children.Add(new OutlineNode { Key = Key(held.Identity.Scene, held.Identity.Path), Label = Name(held), Row = held, Count = 1 });
                holder.Count++;
            }
            foreach (var node in order) node.Children.Sort((a, b) => { var by = StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label); return by != 0 ? by : StringComparer.Ordinal.Compare(a.Row.Identity.Path, b.Row.Identity.Path); });
            return order.OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Key, StringComparer.Ordinal).ToArray();
        }
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

        // The inventory entry of the group a part belongs to, or null: for a group root, for an object outside any
        // group, and while the inventory does not hold exactly one entry for the group.
        internal static SceneObservation GroupOf(SceneInventory inventory, SceneObservation part) {
            if (inventory == null || part?.GroupPath == null) return null;
            var found = inventory.Entries.Where(entry => entry.GroupPath == null && entry.Family == BakedSceneryRules.Family && entry.Identity.Scene == part.Identity.Scene && entry.Identity.Path == part.GroupPath).Take(2).ToArray();
            return found.Length == 1 ? found[0] : null;
        }
        // The Object tab's line that names a part's group, or null for any object that is no part.
        internal static string PartOf(SceneObservation entry) =>
            entry?.GroupPath == null ? null : "Part of: " + entry.GroupPath.Substring(entry.GroupPath.LastIndexOf('/') + 1);

        // What the game itself tells apart inside a family, or null: for a track marker its marker type
        // (Track.TrackMarkerType), read from the observation's marker line. Never a name: a station sign and a whistle
        // sign are both Generic markers to the game. A starter marker has no subgroup here.
        internal static string Subgroup(SceneObservation entry) {
            if (entry == null || entry.Family != "Track marker") return null;
            foreach (var line in entry.Bindings) {
                if (!line.StartsWith(MarkerIdLine, StringComparison.Ordinal)) continue;
                var at = line.IndexOf("; type=", StringComparison.Ordinal);
                if (at < 0) return null;
                var type = line.Substring(at + 7); var end = type.IndexOf(';');
                type = (end < 0 ? type : type.Substring(0, end)).Trim();
                return type.Length == 0 ? null : "Type " + type;
            }
            return null;
        }
        // The rows of one family under their subgroups, subgroups in name order, rows in the order given. Empty when
        // the rows do not fall into at least two subgroups: a single subgroup would only repeat its family.
        internal static IReadOnlyList<KeyValuePair<string, IReadOnlyList<SceneObservation>>> Subgroups(IReadOnlyList<SceneObservation> rows) {
            var groups = (rows ?? Array.Empty<SceneObservation>()).GroupBy(row => Subgroup(row) ?? "").OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new KeyValuePair<string, IReadOnlyList<SceneObservation>>(group.Key.Length == 0 ? "Other" : group.Key, group.ToArray())).ToList();
            return groups.Count < 2 ? new List<KeyValuePair<string, IReadOnlyList<SceneObservation>>>() : groups;
        }

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
            if (inventory != null && !inventory.IsStale && !definitionsStale) return "The object list is up to date. You can inspect objects and start edits.";
            var refuses = inventory == null || inventory.IsStale;
            return (inventory == null ? "inspection is unavailable" : inventory.IsStale ? inventory.StaleReason : "the supplied definitions changed or were not read") +
                ". Click Refresh to update the object list." + (refuses ? " Edits are refused until then." : "");
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
