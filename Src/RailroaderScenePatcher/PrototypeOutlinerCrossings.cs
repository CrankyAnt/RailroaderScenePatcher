// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: throwaway crossings of the outliner grouping prototype: one node per crossing under Grade crossing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RailroaderScenePatcher.Core;
using OutlineNode = RailroaderScenePatcher.InventoryPresentationState.OutlineNode;

namespace RailroaderScenePatcher {
    internal static partial class PrototypeOutlinerGrouping {
        const string ControllerFamily = InventoryPresentationState.HolderFamily, MastFamily = "Crossing mast", BellFamily = "Crossing bell",
            PadFamily = "Crossing pad marker", SpanFamily = FocusTarget.Span, TrackMarkerFamily = "Track marker", MilepostFamily = "Milepost";
        static readonly Regex CrossingWord = new Regex(@"crossing", RegexOptions.IgnoreCase);
        static readonly Regex CrossingMarkerName = new Regex(@"\b(grade\s+crossing|crossing\s+pad)\b", RegexOptions.IgnoreCase);
        static readonly HashSet<string> CrossingEquipmentFamilies = new HashSet<string>(StringComparer.Ordinal) {
            MastFamily, BellFamily, PadFamily, CommandBand.AddedEquipmentFamily, CommandBand.AddedBellFamily
        };

        // An unprotected crossing: the crossing object that holds its crossbucks and pads, and, where crossings share
        // that object's path (Old River Rd), the pad marker ID that tells them apart.
        sealed class UnprotectedCrossing {
            internal ScenePath Crossing;
            internal string PadMarkerId;
            internal readonly List<Unit> Members = new List<Unit>();
        }

        // A Loc: object, whatever family lists it: a track marker, or an independent board (a culler and a mask, no
        // track marker), as the base map's Loc: boards are.
        static bool IsLocObject(SceneObservation r) => Last(r.Identity.Path).StartsWith(LocPrefix, StringComparison.Ordinal);
        static bool IsCrossbuck(SceneObservation r) => r.Family == BakedSceneryRules.Family && r.GroupPath == null && r.Category == BakedSceneryCategories.GradeCrossing;
        // A track marker of a crossing, such as Crossing Pad (1): no sign and no Loc: object.
        static bool IsCrossingTrackMarker(SceneObservation r) => r.Family == TrackMarkerFamily && !IsLocObject(r) && SignKind(Last(r.Identity.Path)) == null;
        static bool CanJoinCrossing(SceneObservation r) =>
            r != null && (CrossingEquipmentFamilies.Contains(r.Family) || IsCrossbuck(r) || IsCrossingTrackMarker(r));

        // The marker ID of a crossing pad or track marker, or the pad marker ID that names a crossbuck.
        static string MarkerIdOf(SceneObservation row) {
            if (!String.IsNullOrEmpty(row.Identity.PadMarkerId)) return row.Identity.PadMarkerId;
            foreach (var line in row.Bindings) {
                if (!line.StartsWith("Marker id=", StringComparison.Ordinal)) continue;
                var rest = line.Substring(10); var end = rest.IndexOf(';');
                var id = (end < 0 ? rest : rest.Substring(0, end)).Trim();
                return id.Length == 0 ? null : id;
            }
            return null;
        }
        static int CrossingChildRank(SceneObservation row) {
            switch (row?.Family) {
                case null: return 99;
                case MastFamily: return 0;
                case CommandBand.AddedEquipmentFamily: return 1;
                case BellFamily: return 2;
                case CommandBand.AddedBellFamily: return 3;
                case PadFamily: return 4;
                case SpanFamily: return 6;
                default: return IsCrossbuck(row) ? 0 : 5;
            }
        }
        // Inside a crossing, crossbucks, masts and bells show their own game name, without the " at <crossing>" the flat
        // list adds to tell them apart.
        static string CrossingMemberLabel(SceneObservation row, string label) =>
            row != null && (row.Family == MastFamily || row.Family == BellFamily || IsCrossbuck(row)) ? Last(row.Identity.Path) : label;
        static void SortCrossingChildren(OutlineNode node) => node.Children.Sort((a, b) => {
            var byKind = CrossingChildRank(a.Row).CompareTo(CrossingChildRank(b.Row));
            if (byKind != 0) return byKind;
            var byName = StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label);
            return byName != 0 ? byName : StringComparer.Ordinal.Compare(a.Row?.Identity.Path ?? a.Key, b.Row?.Identity.Path ?? b.Key);
        });
        static void Join(Unit holder, Unit member) {
            member.Node.Label = CrossingMemberLabel(member.Row, member.Node.Label);
            member.Node.Where = null;
            holder.Node.Children.Add(member.Node);
            holder.Node.Count += member.Node.Count;
        }

        // A protected crossing: the crossing controller is the clickable parent row. It holds its detection spans and the
        // members below it: masts, bells, pads, added lights, gates and bells, and crossing track markers. Its own
        // crossing folder, the folder directly above it when that folder says "crossing" and holds no other controller,
        // adds the members that stand directly in that folder. No folder further up absorbs anything.
        static void GroupProtectedCrossings(BuildContext b) {
            var controllers = b.Entries.Where(e => e.Family == ControllerFamily && e.HolderPath == null).ToArray();
            var controllersPerFolder = controllers.GroupBy(c => new ScenePath(c.Identity.Scene, Parent(c.Identity.Path))).ToDictionary(g => g.Key, g => g.Count());
            string OwnFolder(SceneObservation c) {
                var folder = Parent(c.Identity.Path);
                return !IsPlaceLevel(folder) && CrossingWord.IsMatch(Last(folder)) && controllersPerFolder[new ScenePath(c.Identity.Scene, folder)] == 1 ? folder : null;
            }
            var ownFolders = controllers.ToDictionary(c => c, OwnFolder);
            SceneObservation ControllerOf(SceneObservation r) {
                if (!CanJoinCrossing(r)) return null;
                var path = r.Identity.Path;
                // A controller above the object first, the nearest one; else the controller whose own folder holds it.
                return controllers.Where(c => c.Identity.Scene == r.Identity.Scene && path.StartsWith(c.Identity.Path + "/", StringComparison.Ordinal)).OrderByDescending(c => c.Identity.Path.Length).FirstOrDefault()
                    ?? controllers.FirstOrDefault(c => c.Identity.Scene == r.Identity.Scene && ownFolders[c] != null && Parent(path) == ownFolders[c]);
            }
            var joined = new HashSet<Unit>();
            foreach (var u in b.Units.ToArray()) {
                var controller = u.Row == null ? null : ControllerOf(u.Row);
                if (controller == null) continue;
                var key = new ScenePath(controller.Identity.Scene, controller.Identity.Path);
                if (!b.ByPath.TryGetValue(key, out var holder)) holder = b.Make(controller, key, true);
                Join(holder, u);
                joined.Add(u);
            }
            b.Units.RemoveAll(joined.Contains);
            foreach (var c in b.Units.Where(u => u.Row?.Family == ControllerFamily && u.Node.Children.Count > 0)) {
                SortCrossingChildren(c.Node);
                // A controller named only "Crossing" or "Grade Crossing" shows the name of its own crossing folder.
                var own = Last(c.Row.Identity.Path);
                if (ownFolders.TryGetValue(c.Row, out var folder) && folder != null && (own.Equals("Crossing", StringComparison.OrdinalIgnoreCase) || own.Equals("Grade Crossing", StringComparison.OrdinalIgnoreCase)))
                    c.Node.Label = Last(folder);
                if (b.Diagnostics) b.Result.Crossings.Add("protected " + c.Node.Label + " (" + c.Row.Identity.Path + "): " + String.Join(", ", c.Node.Children.Select(m => m.Row.Family + " " + m.Label)));
            }
        }

        // An unprotected crossing: the crossing object that holds crossbucks or pads becomes a node named with its game
        // name, holding its crossbucks, pads and crossing track markers. Crossings that share one crossing path are told
        // apart by pad marker ID: each crossbuck names its pad's marker ID, and each pad and track marker carries its own.
        // A member in a shared crossing whose ID matches no crossbuck stays in a node of the shared path alone, logged.
        static void GroupUnprotectedCrossings(BuildContext b) {
            var crossingObjects = new HashSet<ScenePath>();
            var sharedPadIds = new Dictionary<ScenePath, HashSet<string>>();
            foreach (var e in b.Entries.Where(e => IsCrossbuck(e) || e.Family == PadFamily)) {
                var parent = Parent(e.Identity.Path);
                if (IsPlaceLevel(parent)) continue;
                var crossing = new ScenePath(e.Identity.Scene, parent);
                crossingObjects.Add(crossing);
                if (IsCrossbuck(e) && !String.IsNullOrEmpty(e.Identity.PadMarkerId)) {
                    if (!sharedPadIds.TryGetValue(crossing, out var ids)) sharedPadIds[crossing] = ids = new HashSet<string>(StringComparer.Ordinal);
                    ids.Add(e.Identity.PadMarkerId);
                }
            }
            var crossings = new Dictionary<(ScenePath, string), UnprotectedCrossing>();
            foreach (var u in b.Units) {
                var r = u.Row;
                if (r == null || !(IsCrossbuck(r) || r.Family == PadFamily || IsCrossingTrackMarker(r))) continue;
                var crossing = new ScenePath(r.Identity.Scene, Parent(r.Identity.Path));
                if (!crossingObjects.Contains(crossing)) continue;
                string padMarkerId = null;
                if (sharedPadIds.TryGetValue(crossing, out var ids)) {
                    var id = MarkerIdOf(r);
                    if (id != null && ids.Contains(id)) padMarkerId = id;
                    else if (b.Diagnostics) b.Result.Crossings.Add("unmatched in shared crossing " + crossing.Path + ": " + r.Family + " " + Last(r.Identity.Path) + " (marker id " + (id ?? "none") + ")");
                }
                if (!crossings.TryGetValue((crossing, padMarkerId), out var found)) crossings[(crossing, padMarkerId)] = found = new UnprotectedCrossing { Crossing = crossing, PadMarkerId = padMarkerId };
                found.Members.Add(u);
            }
            var joined = new HashSet<Unit>();
            foreach (var crossing in crossings.Values.Where(c => c.Members.Count >= 2)) {
                var node = new OutlineNode { Key = "crossing\n" + crossing.Crossing.Scene + "\n" + crossing.Crossing.Path + "\n" + crossing.PadMarkerId, Label = Last(crossing.Crossing.Path) };
                var unit = new Unit { Node = node, Members = crossing.Members, CrossingPath = crossing.Crossing.Path };
                foreach (var m in crossing.Members) { Join(unit, m); joined.Add(m); }
                SortCrossingChildren(node);
                unit.Levels.Add(Level.Category(BakedSceneryCategories.GradeCrossing));
                b.Units.Add(unit);
                if (b.Diagnostics) b.Result.Crossings.Add("unprotected " + node.Label + " (" + crossing.Crossing.Path + (crossing.PadMarkerId == null ? "" : ", pad marker " + crossing.PadMarkerId) + "): "
                    + String.Join(", ", crossing.Members.Select(m => m.Row.Family + " " + m.Node.Label)));
            }
            b.Units.RemoveAll(joined.Contains);
        }
    }
}
