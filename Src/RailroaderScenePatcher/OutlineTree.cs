// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: the one tree of the outliner grouping prototype, without the engine: the same levels in every folder.
using System;
using System.Collections.Generic;
using System.Linq;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

namespace RailroaderScenePatcher {
    // One row the tree places: an object with what stands beneath it (its parts, or the members of a crossing), its
    // category, its kind inside that category or null, and its place or null.
    internal sealed class OutlineEntry {
        internal OutlineNode Node;
        internal string Category, Kind, Place;
    }

    // The outliner as one tree. By category: category, then kind, then place, then the objects. By place: place, then
    // category, then kind, then the objects. Every folder holds either folders of one level or objects, never both,
    // and every list is in alphabetical order.
    internal static class OutlineTree {
        internal const string NoPlace = "No place";
        // Key prefixes that tell the level of a folder.
        internal const string CategoryKey = "c:", KindKey = "k:", PlaceKey = "p:";

        // Alphabetical, without regard to case, with numbers by their value: T-2 before T-10.
        internal sealed class NaturalComparer : IComparer<string> {
            internal static readonly NaturalComparer Instance = new NaturalComparer();
            public int Compare(string a, string b) {
                a = a ?? ""; b = b ?? "";
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length) {
                    if (Char.IsDigit(a[i]) && Char.IsDigit(b[j])) {
                        int si = i, sj = j;
                        while (i < a.Length && Char.IsDigit(a[i])) i++;
                        while (j < b.Length && Char.IsDigit(b[j])) j++;
                        var x = a.Substring(si, i - si).TrimStart('0'); var y = b.Substring(sj, j - sj).TrimStart('0');
                        if (x.Length != y.Length) return x.Length.CompareTo(y.Length);
                        var byDigits = String.CompareOrdinal(x, y);
                        if (byDigits != 0) return byDigits;
                        continue;
                    }
                    var byChar = Char.ToUpperInvariant(a[i]).CompareTo(Char.ToUpperInvariant(b[j]));
                    if (byChar != 0) return byChar;
                    i++; j++;
                }
                return (a.Length - i).CompareTo(b.Length - j);
            }
        }

        // The categories that show a kind level: those with two or more kinds, where at least half of the objects
        // share their kind with another object. A category whose objects are each a kind of their own, such as
        // Industry building, lists them by place alone. Decided on the whole inventory, so a search changes no level.
        internal static HashSet<string> KindCategories(IEnumerable<OutlineEntry> all) {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var category in (all ?? Enumerable.Empty<OutlineEntry>()).Where(e => e.Kind != null).GroupBy(e => e.Category)) {
                var kinds = category.GroupBy(e => e.Kind, StringComparer.OrdinalIgnoreCase).ToArray();
                if (kinds.Length >= 2 && kinds.Where(k => k.Count() >= 2).Sum(k => k.Count()) * 2 >= category.Count()) found.Add(category.Key);
            }
            return found;
        }

        static OutlineNode Folder(string key, string label, IEnumerable<OutlineNode> children) {
            var folder = new OutlineNode { Key = key, Label = label };
            folder.Children.AddRange(children);
            folder.Count = folder.Children.Sum(c => c.Count);
            return folder;
        }
        static IEnumerable<OutlineNode> Rows(IEnumerable<OutlineEntry> entries) =>
            entries.Select(e => e.Node).OrderBy(n => n.Label, NaturalComparer.Instance).ThenBy(n => n.Key, StringComparer.Ordinal);
        static IEnumerable<IGrouping<string, OutlineEntry>> Sorted(IEnumerable<IGrouping<string, OutlineEntry>> groups) => groups.OrderBy(g => g.Key, NaturalComparer.Instance);
        static IEnumerable<OutlineNode> Places(string key, IEnumerable<OutlineEntry> entries) =>
            Sorted(entries.GroupBy(e => e.Place ?? NoPlace, StringComparer.OrdinalIgnoreCase)).Select(place => Folder(key + "/" + PlaceKey + place.Key, place.First().Place ?? NoPlace, Rows(place)));
        // The kind folders of a category that shows kinds, else what make gives for all its entries.
        static IEnumerable<OutlineNode> Kinds(string key, string category, IEnumerable<OutlineEntry> entries, HashSet<string> kindCategories, Func<string, IEnumerable<OutlineEntry>, IEnumerable<OutlineNode>> make) =>
            !kindCategories.Contains(category) ? make(key, entries)
            : Sorted(entries.GroupBy(e => e.Kind ?? category, StringComparer.OrdinalIgnoreCase)).Select(kind => Folder(key + "/" + KindKey + kind.Key, kind.First().Kind ?? category, make(key + "/" + KindKey + kind.Key, kind)));

        internal static List<OutlineNode> ByCategory(IEnumerable<OutlineEntry> entries, HashSet<string> kindCategories) =>
            Sorted(entries.GroupBy(e => e.Category, StringComparer.Ordinal))
                .Select(category => Folder(CategoryKey + category.Key, category.Key, Kinds(CategoryKey + category.Key, category.Key, category, kindCategories, Places))).ToList();

        // places are the places to show also without objects, or null to show only those with objects.
        internal static List<OutlineNode> ByPlace(IEnumerable<OutlineEntry> entries, HashSet<string> kindCategories, IEnumerable<string> places) {
            var byPlace = entries.GroupBy(e => e.Place ?? NoPlace, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
            var names = (places ?? Enumerable.Empty<string>()).Concat(byPlace.Values.Select(g => g[0].Place ?? NoPlace)).Distinct(StringComparer.OrdinalIgnoreCase);
            return names.OrderBy(n => n == NoPlace ? 1 : 0).ThenBy(n => n, NaturalComparer.Instance).Select(name => {
                var key = PlaceKey + name;
                byPlace.TryGetValue(name, out var members);
                return Folder(key, name, Sorted((members ?? new OutlineEntry[0]).GroupBy(e => e.Category, StringComparer.Ordinal))
                    .Select(category => Folder(key + "/" + CategoryKey + category.Key, category.Key, Kinds(key + "/" + CategoryKey + category.Key, category.Key, category, kindCategories, (k, list) => Rows(list)))));
            }).ToList();
        }

        // What breaks the rules of the tree, one line each: a folder that mixes folders and objects or folders of two
        // levels, a list out of order, a folder that repeats the name of the folder it stands in, a place below the
        // top level of the place view, and text that tells how a place was chosen.
        internal static List<string> Violations(IReadOnlyList<OutlineNode> tops, bool byPlace, IEnumerable<string> placeNames) {
            var found = new List<string>();
            var places = new HashSet<string>(placeNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            string Level(OutlineNode n) { if (n.Row != null || n.Key == null) return "row"; var last = n.Key.Substring(n.Key.LastIndexOf('/') + 1); return last.Length > 1 && last[1] == ':' ? last.Substring(0, 2) : "row"; }
            void Ordered(IReadOnlyList<OutlineNode> list, string where) {
                for (var i = 1; i < list.Count; i++) if (NaturalComparer.Instance.Compare(list[i - 1].Label, list[i].Label) > 0 && list[i - 1].Label != NoPlace) found.Add("order: " + where + ": " + list[i - 1].Label + " before " + list[i].Label);
            }
            void Walk(OutlineNode node, string path, int depth) {
                var levels = node.Children.Select(Level).Distinct().ToArray();
                if (Level(node) != "row" && levels.Length > 1) found.Add("mixed: " + path + " holds " + String.Join(" and ", levels));
                if (Level(node) != "row") Ordered(node.Children, path);
                foreach (var child in node.Children) {
                    if (Level(child) != "row" && child.Label.StartsWith(node.Label, StringComparison.OrdinalIgnoreCase)) found.Add("repeat: " + path + " / " + child.Label);
                    if (byPlace && Level(child) != "row" && places.Contains(child.Label)) found.Add("nested place: " + path + " / " + child.Label);
                    if (((child.Label ?? "") + " " + (child.Where ?? "")).IndexOf("nearest", StringComparison.OrdinalIgnoreCase) >= 0) found.Add("method text: " + path + " / " + child.Label + " " + child.Where);
                    if (Level(node) != "row") Walk(child, path + " / " + child.Label, depth + 1);
                }
            }
            Ordered(tops, "top level");
            foreach (var top in tops) {
                if (Level(top) != (byPlace ? PlaceKey : CategoryKey)) found.Add("top level: " + top.Label + " is no " + (byPlace ? "place" : "category"));
                Walk(top, top.Label, 0);
            }
            return found;
        }
    }
}
