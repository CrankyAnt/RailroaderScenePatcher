// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: throwaway outliner grouping for the question "how should the outliner group about 1,700 scene objects,
// by kind or by place?". Not production code: no tests, no persistence, state in memory only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RailroaderScenePatcher.Core;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

namespace RailroaderScenePatcher {
    internal static class PrototypeOutlinerGrouping {
        // 0: the current outliner. A: kinds and places inside today's families. B: one tree across families.
        internal static int Variant = 1;
        internal static bool ByPlace;
        internal static readonly string[] VariantKeys = { "0", "A", "B" };
        internal static readonly string[] VariantNames = { "Current outliner", "Kinds and places within families", "One tree across families" };
        internal static readonly string[] VariantShort = { "Current", "Families", "One tree" };
        // A kind folder needs at least this many objects; fewer stand loose in the folder above.
        internal const int KindThreshold = 2;
        // A place folder needs at least this many objects of one kind in one place; fewer stand loose, with their place.
        internal const int PlaceThreshold = 3;
        // Place folders appear only in a list longer than this; a shorter list reads well with each object's place.
        internal const int PlaceListMinimum = 10;
        // A place's radius: the 90th percentile of its members' distances from its centre, at least MinimumRadius
        // metres, plus RadiusMargin.
        internal const double MinimumRadius = 250, RadiusMargin = 150;
        internal const string NotSorted = "Not sorted", NoPlace = "No place", InspectionOnly = InventoryPresentationState.InspectionOnlyGroup;
        internal static bool LogPending = true;

        sealed class Level { internal string Name; internal bool Forced; }
        sealed class Unit {
            internal OutlineNode Node;
            internal SceneObservation Row;
            internal List<Level> Levels = new List<Level>();
            internal string Place, Near, Inside;
            internal bool Conflict, Nearest;
        }
        internal sealed class Result {
            internal IReadOnlyList<OutlineNode> Tops;
            internal int Listed, Unplaced;
            internal List<string> CatchAll = new List<string>();
            internal List<string> Conflicts = new List<string>();
        }

        // ---- places ----
        // Named: the place the object's folder or name gives. Near: the place whose centre is nearest. Inside: the
        // nearest other place whose radius holds the object.
        sealed class PlaceInfo { internal string Named, Near, Inside; internal double NamedDistance, NearDistance, InsideDistance; }
        static IReadOnlyList<SceneObservation> placesFor;
        static Dictionary<string, string> aliases;
        static List<KeyValuePair<string, Regex>> aliasPatterns;
        static Dictionary<string, double[]> centres;
        static Dictionary<string, double> radii;
        static Dictionary<SceneObservation, PlaceInfo> placeCache;
        static Dictionary<SceneObservation, double[]> measured;
        const string Scenery = "World/Large Scenery/";
        // Folders below Large Scenery named after a structure that stands in no single place.
        static readonly HashSet<string> NoPlaceFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Plate Girder Bridge", "Global Signs" };
        static readonly Regex StructureSuffix = new Regex(@"\s+(Signs|Bridge|Turntable)\z", RegexOptions.IgnoreCase);

        static string Last(string path) => path.Substring(path.LastIndexOf('/') + 1);
        static string SceneryFolder(string path) {
            if (path == null || !path.StartsWith(Scenery, StringComparison.Ordinal)) return null;
            var rest = path.Substring(Scenery.Length); var slash = rest.IndexOf('/');
            return slash < 0 ? rest : rest.Substring(0, slash);
        }
        static bool InSignsFolder(string path) => path.Split('/').Any(segment => segment.EndsWith("Signs", StringComparison.Ordinal) || segment == "Signs");
        static bool HasPosition(SceneObservation e) => e.Position != null && e.Position.Count >= 3;

        // Where an object stands, as map x/z: the centre of the bounds of the renderers in its hierarchy (a group with
        // its parts), not its pivot. A renderer that is off or inactive counts by its mesh's bounds, unless the mesh is a
        // static batch, which spans more than the object; then by its transform. Without renderers: the pivot.
        static double[] Measure(SceneObservation e) {
            if (e == null) return null;
            if (measured.TryGetValue(e, out var known)) return known;
            double[] result = null;
            try { result = MeasureBounds(e.Handle); } catch { result = null; }
            if (result == null && HasPosition(e)) result = new[] { e.Position[0], e.Position[2] };
            return measured[e] = result;
        }
        static double[] MeasureBounds(object handle) {
            if (!(handle is UnityEngine.GameObject go) || go == null) return null;
            var bounds = new UnityEngine.Bounds(); var any = false;
            foreach (var renderer in go.GetComponentsInChildren<UnityEngine.Renderer>(true)) {
                if (renderer == null) continue;
                UnityEngine.Bounds own;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.bounds.size != UnityEngine.Vector3.zero) own = renderer.bounds;
                else {
                    var filter = renderer.GetComponent<UnityEngine.MeshFilter>();
                    var mesh = filter == null ? null : filter.sharedMesh;
                    if (mesh == null || mesh.name.StartsWith("Combined Mesh", StringComparison.Ordinal)) own = new UnityEngine.Bounds(renderer.transform.position, UnityEngine.Vector3.zero);
                    else {
                        var local = mesh.bounds; var ext = local.extents;
                        own = new UnityEngine.Bounds(renderer.transform.TransformPoint(local.center), UnityEngine.Vector3.zero);
                        for (var i = 0; i < 8; i++)
                            own.Encapsulate(renderer.transform.TransformPoint(local.center + new UnityEngine.Vector3((i & 1) == 0 ? -ext.x : ext.x, (i & 2) == 0 ? -ext.y : ext.y, (i & 4) == 0 ? -ext.z : ext.z)));
                    }
                }
                if (any) bounds.Encapsulate(own); else { bounds = own; any = true; }
            }
            if (!any) return null;
            var map = NativeInventory.MapPosition(bounds.center, NativeInventory.MovesWithOrigin(go.transform));
            return Single.IsNaN(map.x) || Single.IsNaN(map.z) ? null : new double[] { map.x, map.z };
        }
        static double Distance(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));

        static void Prepare(IReadOnlyList<SceneObservation> entries) {
            if (ReferenceEquals(placesFor, entries) && placeCache != null) return;
            placesFor = entries; placeCache = new Dictionary<SceneObservation, PlaceInfo>(); measured = new Dictionary<SceneObservation, double[]>();
            var folders = new HashSet<string>(entries.Select(e => SceneryFolder(e.Identity.Path)).Where(f => f != null), StringComparer.OrdinalIgnoreCase);
            // A folder is its own place (Alarka, Alarka Jct). A folder whose name is a place followed by a structure word
            // belongs to that place (Alarka Signs, Almond Bridge, Dillsboro Turntable). Plate Girder Bridge and Global
            // Signs are no place.
            string Normalize(string folder) {
                if (NoPlaceFolders.Contains(folder)) return null;
                var match = StructureSuffix.Match(folder);
                if (!match.Success) return folder;
                var rest = folder.Substring(0, match.Index);
                return folders.FirstOrDefault(f => f.Equals(rest, StringComparison.OrdinalIgnoreCase)) ?? rest;
            }
            aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in folders) { var p = Normalize(f); if (p != null) { aliases[f] = p; aliases[p] = p; } }
            aliasPatterns = aliases.Keys.OrderByDescending(a => a.Length)
                .Select(a => new KeyValuePair<string, Regex>(a, new Regex(@"(?<![\w])" + Regex.Escape(a) + @"(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))).ToList();
            // A place's centre: the median x and z of its members, measured at their bounds. Members are the objects
            // outside any group or controller in the place's folders; sign folders are left out, because their signs
            // spread along the line. Its radius: the 90th percentile of the members' distances from the centre, at least
            // MinimumRadius, plus RadiusMargin.
            var points = new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries) {
                if (e.GroupPath != null || e.HolderPath != null) continue;
                var folder = SceneryFolder(e.Identity.Path);
                if (folder == null || InSignsFolder(e.Identity.Path) || !aliases.TryGetValue(folder, out var place)) continue;
                var at = Measure(e); if (at == null) continue;
                if (!points.TryGetValue(place, out var list)) points[place] = list = new List<double[]>();
                list.Add(at);
            }
            double Median(IEnumerable<double> values) { var s = values.OrderBy(v => v).ToArray(); return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2; }
            centres = points.ToDictionary(p => p.Key, p => new[] { Median(p.Value.Select(v => v[0])), Median(p.Value.Select(v => v[1])) }, StringComparer.OrdinalIgnoreCase);
            radii = points.ToDictionary(p => p.Key, p => {
                var d = p.Value.Select(v => Distance(v, centres[p.Key])).OrderBy(v => v).ToArray();
                return Math.Max(MinimumRadius, d[Math.Max(0, (int)Math.Ceiling(0.9 * d.Length) - 1)]) + RadiusMargin;
            }, StringComparer.OrdinalIgnoreCase);
        }

        static PlaceInfo PlaceOf(SceneObservation e) {
            if (e == null) return new PlaceInfo();
            if (placeCache.TryGetValue(e, out var cached)) return cached;
            var info = new PlaceInfo();
            var folder = SceneryFolder(e.Identity.Path);
            if (folder != null && aliases.TryGetValue(folder, out var byFolder)) info.Named = byFolder;
            if (info.Named == null) {
                var name = Last(e.Identity.Path);
                foreach (var alias in aliasPatterns) if (alias.Value.IsMatch(name)) { info.Named = aliases[alias.Key]; break; }
            }
            var at = Measure(e);
            if (at != null && centres.Count != 0) {
                var nearest = centres.OrderBy(c => Distance(at, c.Value)).First();
                info.Near = nearest.Key; info.NearDistance = Distance(at, nearest.Value);
                if (info.Named != null && centres.TryGetValue(info.Named, out var own)) {
                    info.NamedDistance = Distance(at, own);
                    var inside = centres.Where(c => !c.Key.Equals(info.Named, StringComparison.OrdinalIgnoreCase) && Distance(at, c.Value) <= radii[c.Key]).OrderBy(c => Distance(at, c.Value)).FirstOrDefault();
                    if (inside.Key != null) { info.Inside = inside.Key; info.InsideDistance = Distance(at, inside.Value); }
                }
            }
            return placeCache[e] = info;
        }
        // The conflict rule: the object stands outside its named place's radius and inside the radius of another place,
        // nearer that place's centre than its own.
        static bool IsConflict(PlaceInfo info) => info.Named != null && info.Inside != null && centres.ContainsKey(info.Named)
            && info.NamedDistance > radii[info.Named] && info.InsideDistance < info.NamedDistance;

        // ---- kinds ----
        internal static readonly string[] SignKinds = { "Town sign groups", "Station signs", "Location signs", "Speed signs", "Yard limits", "Whistle signs", "CTC signs", "Mileposts", "Other signs" };
        static readonly string[] TopsB = { BakedSceneryCategories.StationBuilding, BakedSceneryCategories.Platform, BakedSceneryCategories.Lights, BakedSceneryCategories.Sign, BakedSceneryCategories.GradeCrossing,
            BakedSceneryCategories.Bridge, BakedSceneryCategories.IndustryBuilding, BakedSceneryCategories.StorageDisplay, BakedSceneryCategories.RailroadServiceBuilding,
            "Track marker", SpawnPoints.Family, StarterPlacements.Family, StationMapIconRules.Family, BakedSceneryCategories.Other };
        static readonly string[] CrossingKinds = { "Crossbucks", "Crossing controllers", "Crossing pads", "Masts and gates", "Bells", "Added lights and gates", "Added bells", "Detection spans" };

        // The kind of sign an object's own name says, or null when its name names no sign.
        internal static string SignKind(string name) {
            var n = name.ToLowerInvariant();
            if (n.StartsWith("speed", StringComparison.Ordinal) || n.Contains("speed sign")) return "Speed signs";
            if (n.Contains("yard limit")) return "Yard limits";
            if (n.StartsWith("loc:", StringComparison.Ordinal)) return "Location signs";
            if (n.Contains("station sign") || n.Contains("sign-station")) return "Station signs";
            if (n.Contains("whistle")) return "Whistle signs";
            if (n.Contains("ctc") || n.Contains("circuit")) return "CTC signs";
            if (n.Contains("sign")) return "Other signs";
            return null;
        }
        static readonly Regex Ordinal = new Regex(@"\s*\(\d+\)\z");
        static string Stem(string label) => Ordinal.Replace(label ?? "", "");

        // A sign group takes the name of the folder it is stored in: Alarka Signs, Ela Signs.
        static string SignGroupLabel(string path) {
            var segments = path.Split('/');
            for (var i = segments.Length - 1; i >= 0; i--) {
                if (segments[i] == "Signs" && i > 0) return segments[i - 1] + " Signs";
                if (segments[i].EndsWith(" Signs", StringComparison.Ordinal)) return segments[i];
            }
            return null;
        }

        static Level L(string name, bool forced = false) => new Level { Name = name, Forced = forced };

        static void Classify(Unit u, int variant) {
            var r = u.Row;
            if (r == null) { u.Levels.Add(L(NotSorted, true)); u.Levels.Add(L("Group not in the object list", true)); return; }
            var name = Last(r.Identity.Path);
            var baked = r.Category != null || r.Family == BakedSceneryRules.Family || u.Node.Children.Any(c => c.Row?.GroupPath != null);
            var category = r.Category ?? (baked ? BakedSceneryCategories.Other : null);
            string BakedKind() => category == BakedSceneryCategories.Bridge ? r.Subcategory ?? "Other bridges"
                : category == BakedSceneryCategories.Sign ? "Town sign groups"
                : category == BakedSceneryCategories.GradeCrossing ? "Crossbucks" : Stem(u.Node.Label);
            if (variant == 1) {
                if (r.InspectionOnly != null && r.Category == null) { u.Levels.Add(L(InspectionOnly, true)); u.Levels.Add(L(r.Family ?? "No family")); return; }
                if (baked) { u.Levels.Add(L(BakedSceneryRules.Family, true)); u.Levels.Add(L(category, true)); u.Levels.Add(L(BakedKind())); return; }
                if (String.IsNullOrEmpty(r.Family)) { u.Levels.Add(L(NotSorted, true)); return; }
                u.Levels.Add(L(r.Family, true));
                if (r.Family == "Track marker") u.Levels.Add(L(SignKind(name) ?? InventoryPresentationState.Subgroup(r) ?? "Other markers"));
                else if (r.Family != InventoryPresentationState.HolderFamily) u.Levels.Add(L(Stem(u.Node.Label)));
                return;
            }
            // Variant B: one tree; the category says what the object is, whatever family lists it.
            if (baked) { u.Levels.Add(L(category, true)); u.Levels.Add(L(BakedKind())); return; }
            switch (r.Family) {
                case "Bridge": case "Bridge construction scenery":
                    u.Levels.Add(L(BakedSceneryCategories.Bridge, true)); u.Levels.Add(L(BakedSceneryCategories.RailBridges)); return;
                case "Crossing controller": u.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true)); u.Levels.Add(L("Crossing controllers")); return;
                case "Crossing pad marker": u.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true)); u.Levels.Add(L("Crossing pads")); return;
                case "Crossing mast": u.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true)); u.Levels.Add(L("Masts and gates")); return;
                case "Crossing bell": u.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true)); u.Levels.Add(L("Bells")); return;
                case CommandBand.AddedEquipmentFamily: u.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true)); u.Levels.Add(L("Added lights and gates")); return;
                case CommandBand.AddedBellFamily: u.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true)); u.Levels.Add(L("Added bells")); return;
                case "Track span": u.Levels.Add(L(BakedSceneryCategories.GradeCrossing, true)); u.Levels.Add(L("Detection spans")); return;
                case "Milepost": u.Levels.Add(L(BakedSceneryCategories.Sign, true)); u.Levels.Add(L("Mileposts")); return;
                case "Track marker": {
                    var sign = SignKind(name);
                    if (sign != null) { u.Levels.Add(L(BakedSceneryCategories.Sign, true)); u.Levels.Add(L(sign)); }
                    else { u.Levels.Add(L("Track marker", true)); u.Levels.Add(L(InventoryPresentationState.Subgroup(r) ?? "Other markers")); }
                    return;
                }
                case "Track-relative scene object": case "Independent board":
                    u.Levels.Add(L(BakedSceneryCategories.Other, true)); u.Levels.Add(L(r.Family)); return;
            }
            if (r.Family == SpawnPoints.Family || r.Family == StarterPlacements.Family || r.Family == StationMapIconRules.Family) { u.Levels.Add(L(r.Family, true)); u.Levels.Add(L(Stem(u.Node.Label))); return; }
            u.Levels.Add(L(NotSorted, true)); u.Levels.Add(L(String.IsNullOrEmpty(r.Family) ? "No family" : r.Family, true));
        }

        static int Rank(int variant, int depth, string name, bool placeView) {
            var top = depth == 0;
            if (name == NotSorted || name == NoPlace || name == InspectionOnly) return 10000;
            if (top && variant == 2) { var i = Array.IndexOf(TopsB, name); return i < 0 ? 5000 : i; }
            var c = BakedSceneryCategories.All.ToList().IndexOf(name); if (c >= 0) return c;
            var s = Array.IndexOf(SignKinds, name); if (s >= 0) return 100 + s;
            var k = Array.IndexOf(CrossingKinds, name); if (k >= 0) return 200 + k;
            return 1000;
        }

        // ---- the tree ----
        internal static Result Build(IReadOnlyList<SceneObservation> rows, IReadOnlyList<SceneObservation> inventory, string search) {
            var variant = Variant; var placeView = ByPlace;
            rows = rows ?? Array.Empty<SceneObservation>();
            var entries = inventory ?? rows;
            Prepare(entries);
            // Units: every object outside a group and outside a controller, with its parts or spans beneath it. A part or
            // span whose holder the search left out brings its holder, dimmed.
            var units = new List<Unit>(); var byKey = new Dictionary<string, Unit>(StringComparer.Ordinal);
            string Key(string scene, string path) => scene + "\n" + path;
            Unit Make(SceneObservation row, string scene, string path, bool context) {
                var label = row == null ? Last(path) : row.Category == BakedSceneryCategories.Sign ? SignGroupLabel(row.Identity.Path) ?? row.DisplayName
                    : row.GroupPath == null && (row.Category != null || row.Family == BakedSceneryRules.Family) ? row.DisplayName : InventoryPresentationState.Name(row);
                var u = new Unit { Row = row, Node = new OutlineNode { Key = Key(scene, path) + (row?.Identity.PadMarkerId == null ? "" : "\n" + row.Identity.PadMarkerId), Label = label, Row = row, Context = context } };
                units.Add(u);
                var plain = Key(scene, path);
                if (!byKey.ContainsKey(plain)) byKey[plain] = u;
                return u;
            }
            foreach (var row in rows.Where(r => r.GroupPath == null && r.HolderPath == null)) Make(row, row.Identity.Scene, row.Identity.Path, false).Node.Count = 1;
            foreach (var row in rows.Where(r => r.GroupPath != null || r.HolderPath != null)) {
                var holderPath = row.GroupPath ?? row.HolderPath;
                if (!byKey.TryGetValue(Key(row.Identity.Scene, holderPath), out var holder)) {
                    var found = entries.FirstOrDefault(e => e.GroupPath == null && e.HolderPath == null && e.Identity.Scene == row.Identity.Scene && e.Identity.Path == holderPath);
                    holder = Make(found, row.Identity.Scene, holderPath, true);
                }
                holder.Node.Children.Add(new OutlineNode { Key = Key(row.Identity.Scene, row.Identity.Path), Label = row.GroupPath != null ? InventoryPresentationState.PartLabel(row) : InventoryPresentationState.Name(row), Row = row, Count = 1 });
                holder.Node.Count++;
            }
            foreach (var u in units) {
                if (!u.Node.Context && u.Row != null && u.Node.Children.Count != 0 && u.Row.GroupPath == null && u.Row.HolderPath == null && u.Row.Family == BakedSceneryRules.Family && !u.Row.MatchesItself(search)) { u.Node.Context = true; u.Node.Count--; }
                u.Node.Children.Sort((a, b) => { var by = StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label); return by != 0 ? by : StringComparer.Ordinal.Compare(a.Row.Identity.Path, b.Row.Identity.Path); });
                Classify(u, variant);
                var place = PlaceOf(u.Row);
                u.Place = place.Named ?? place.Near; u.Near = place.Near; u.Inside = place.Inside;
                u.Nearest = place.Named == null && place.Near != null;
                u.Conflict = IsConflict(place);
            }
            var result = new Result();
            string Note(Unit u) => u.Conflict ? "(stands in " + u.Inside + ")" : u.Nearest ? "(nearest place)" : null;
            string WithPlace(Unit u) => u.Place == null ? Note(u) : u.Place + (Note(u) == null ? "" : " " + Note(u));

            void Fill(OutlineNode folder, List<Unit> members, int depth, string prefix, bool placeFolders) {
                var loose = new List<Unit>();
                var levels = members.GroupBy(m => depth < m.Levels.Count ? m.Levels[depth].Name : null).ToList();
                var subfolders = new List<OutlineNode>();
                foreach (var group in levels.Where(g => g.Key != null).OrderBy(g => Rank(variant, depth, g.Key, placeView)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                    var list = group.ToList();
                    if (!list.Any(m => m.Levels[depth].Forced) && list.Count < KindThreshold) { loose.AddRange(list); continue; }
                    var sub = new OutlineNode { Key = prefix + "/" + group.Key, Label = group.Key };
                    Fill(sub, list, depth + 1, sub.Key, placeFolders);
                    subfolders.Add(sub);
                }
                loose.AddRange(levels.Where(g => g.Key == null).SelectMany(g => g));
                // A kind folder that would be the only thing in its folder only repeats it: its contents move up.
                if (subfolders.Count == 1 && loose.Count == 0 && !members.Any(m => depth < m.Levels.Count && m.Levels[depth].Forced)) {
                    folder.Children.AddRange(subfolders[0].Children); folder.Count = subfolders[0].Count; return;
                }
                folder.Children.AddRange(subfolders);
                var placeGroups = placeFolders && loose.Count > PlaceListMinimum ? loose.Where(m => m.Place != null && !m.Nearest).GroupBy(m => m.Place, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= PlaceThreshold).ToList() : new List<IGrouping<string, Unit>>();
                // One place folder that would hold every loose object only repeats a place: the objects stand loose with it.
                if (placeGroups.Count == 1 && placeGroups[0].Count() == loose.Count && subfolders.Count == 0) placeGroups.Clear();
                var inFolders = new HashSet<Unit>(placeGroups.SelectMany(g => g));
                foreach (var g in placeGroups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                    var pf = new OutlineNode { Key = prefix + "/@" + g.Key, Label = g.Key };
                    foreach (var m in g.OrderBy(m => m.Node.Label, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Node.Key, StringComparer.Ordinal)) { m.Node.Where = Note(m); pf.Children.Add(m.Node); }
                    pf.Count = pf.Children.Sum(c => c.Count);
                    folder.Children.Add(pf);
                }
                foreach (var m in loose.Where(m => !inFolders.Contains(m)).OrderBy(m => m.Node.Label, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Place ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Node.Key, StringComparer.Ordinal)) {
                    m.Node.Where = placeView ? Note(m) : WithPlace(m);
                    folder.Children.Add(m.Node);
                }
                folder.Count = folder.Children.Sum(c => c.Count);
            }

            var tops = new List<OutlineNode>();
            if (!placeView) {
                foreach (var group in units.GroupBy(u => u.Levels[0].Name).OrderBy(g => Rank(variant, 0, g.Key, false)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                    var top = new OutlineNode { Key = group.Key, Label = group.Key };
                    Fill(top, group.ToList(), 1, top.Key, true);
                    tops.Add(top);
                    if (group.Key == NotSorted) { result.Unplaced += top.Count; result.CatchAll.AddRange(group.Select(u => u.Node.Label + " [" + (u.Row?.Family ?? "no row") + "] " + (u.Row?.Identity.Path ?? u.Node.Key.Replace("\n", " "))));}
                }
            } else {
                foreach (var group in units.GroupBy(u => u.Place ?? NoPlace, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key == NoPlace ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                    var top = new OutlineNode { Key = "@" + group.Key, Label = group.Key };
                    Fill(top, group.ToList(), 0, top.Key, false);
                    tops.Add(top);
                    if (group.Key == NoPlace) { result.Unplaced += top.Count; result.CatchAll.AddRange(group.Select(u => u.Node.Label + " [" + (u.Row?.Family ?? "no row") + "] " + (u.Row?.Identity.Path ?? u.Node.Key.Replace("\n", " ")))); }
                }
            }
            foreach (var u in units.Where(u => u.Conflict)) {
                var info = PlaceOf(u.Row);
                result.Conflicts.Add(u.Node.Label + " (" + u.Row.Identity.Path + "): named " + info.Named + " at " + Math.Round(info.NamedDistance).ToString(CultureInfo.InvariantCulture) + " m (radius " + Math.Round(radii[info.Named]).ToString(CultureInfo.InvariantCulture) + "), inside " + info.Inside + " at " + Math.Round(info.InsideDistance).ToString(CultureInfo.InvariantCulture) + " m (radius " + Math.Round(radii[info.Inside]).ToString(CultureInfo.InvariantCulture) + ")");
            }
            result.Tops = tops;
            result.Listed = tops.Sum(t => t.Count);
            return result;
        }

        // One line per view or variant switch: view, variant, top-level nodes, objects listed, objects not placed in
        // any node; the catch-all members and place conflicts follow on a detail line.
        internal static void Log(Result result, int rowsShown, string search, string filter) {
            var tops = String.Join(", ", result.Tops.Select(t => t.Label + "=" + t.Count.ToString(CultureInfo.InvariantCulture)));
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE outliner: view=" + (ByPlace ? "Place" : "Category") + " variant=" + VariantKeys[Variant] + " (" + VariantNames[Variant] + ") tops=" + result.Tops.Count
                + " listed=" + result.Listed + " shown=" + rowsShown + " notPlaced=" + result.Unplaced + " dropped=" + (rowsShown - result.Listed) + " filter=" + filter + " search='" + search + "' [" + tops + "]");
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE outliner detail: catchAll=" + result.CatchAll.Count + " {" + String.Join(" | ", result.CatchAll) + "} conflicts=" + result.Conflicts.Count + " {" + String.Join(" | ", result.Conflicts) + "} places={"
                + String.Join(", ", (centres ?? new Dictionary<string, double[]>()).OrderBy(c => c.Key).Select(c => c.Key + "@" + Math.Round(c.Value[0]) + "," + Math.Round(c.Value[1]) + " r" + Math.Round(radii[c.Key]))) + "}");
        }
        internal static void LogCurrent(int tops, int rowsShown) =>
            UnityEngine.Debug.Log("Scene Patcher PROTOTYPE outliner: view=Category variant=0 (Current outliner) tops=" + tops + " listed=" + rowsShown + " notPlaced=0");
    }
}
