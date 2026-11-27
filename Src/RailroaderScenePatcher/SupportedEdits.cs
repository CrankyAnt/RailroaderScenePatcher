// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The edits that the Placement, Object and Connections controls offer for an inventory entry, for every family, and
    // for each edit that is not available the reason that its control's tooltip gives. One rule set serves both: the
    // Object tab lists Supported edits from it, and each control takes its object reason from it. What holds an edit back
    // only now, such as an open world choice or a blocked editing session, is not part of it. UI-independent.
    internal static class SupportedEdits {
        // The edits, in the order Supported edits names them. Activity is named by its button: Disable or Enable.
        internal const string Move = "Move", Rotate = "Rotate", Follow = "Follow a track", Activity = "Activity", Binding = "Set track binding",
            MarkerId = "Set replacement marker ID", Coverage = "Edit crossing coverage", Equipment = "Edit crossing equipment", Segments = "Set construction segments", TrackLocation = "Set track location", Wreck = "Set Start derailed";
        internal sealed class Edit {
            internal Edit(string key, string name, string reason) { Key = key; Name = name; Reason = EditReasons.Short(reason); }
            // The edit's constant above.
            internal string Key { get; }
            // The edit as Supported edits names it.
            internal string Name { get; }
            // Why the edit is not available, or null: the short reason its control's tooltip gives as a sentence.
            internal string Reason { get; }
        }
        // What the loaded world adds to the entry at the time the Object tab is drawn. Every value left null takes what
        // the entry itself says.
        internal sealed class Live {
            // Whether the inspected object still stands in the loaded scene; the patcher edits only a live object.
            internal bool? Present { get; set; }
            // The object's own active flag, which decides between Disable and Enable.
            internal bool? Enabled { get; set; }
            // Why the game switches the object's activity itself, such as a culler or a rail bridge's construction controller.
            internal string ActivitySwitched { get; set; }
            // Why the game would not keep the object disabled, such as a progression that cannot be read.
            internal string DisableProblem { get; set; }
            // Why the crossing's lights, gates and bells cannot be read now, so that none can be added, edited or removed.
            internal string EquipmentReason { get; set; }
            // Why a crossing controller cannot move its grade-crossing setup as a whole, such as a game build without the
            // support for crossing masts that the whole move runs on.
            internal string WholeMoveReason { get; set; }
            // Why a generic track marker takes no replacement ID now, as its Replacement marker ID section reads it.
            internal string MarkerIdReason { get; set; }
            // Why the setups that read a starter placement marker could not be read.
            internal string StartersError { get; set; }
            // Whether the object holds the construction-segment relationship, read from its controller.
            internal bool? HoldsConstructionSegments { get; set; }
            // The bridge kind of the object's group, for a part whose top scene object names it.
            internal BridgeKind? Bridge { get; set; }
        }
        // The reason that holds the activity edit of a crossing controller: its capability takes its placement, which
        // moves the grade-crossing setup as a whole, its crossing coverage and its added equipment.
        internal const string CrossingNoActivity = "Its activity can't be edited";
        static readonly HashSet<string> MarkerStructures = new HashSet<string>(StringComparer.Ordinal) { CrossingPadScene.Structure, TrackMarkerScene.Structure, MilepostScene.Structure };
        // Whether the object's track binding is edited through its track marker: a crossing pad, a generic track marker or a milepost.
        internal static bool EditsBinding(string structure) => structure != null && MarkerStructures.Contains(structure);
        // Whether the object holds a track binding: a track marker or a milepost, whose binding is optional, or a crossing
        // pad, whose binding is required. Only these have the track binding edit and the Track binding section, also
        // when the patcher refuses them; every other object has neither.
        internal static bool HoldsBinding(string family) => family == SceneFamilies.TrackMarker || family == "Milepost" || family == "Crossing pad marker";
        // Whether the object's activity follows its track group, by its binding lines.
        internal static bool FollowsGroup(SceneObservation entry) => entry != null && entry.Bindings.Any(b => b.IndexOf("follows track group", StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf("targetActiveFollowsTrackGroup=True", StringComparison.OrdinalIgnoreCase) >= 0);
        // Whether the object holds the construction-segment relationship: the Ela construction controller, or a rail
        // bridge top scene object that names construction segments.
        internal static bool HoldsSegments(SceneObservation entry, Live live = null) =>
            entry != null && (entry.Identity.Structure == ConstructionSceneryScene.Structure
                || entry.Identity.Structure == SceneObjects.Structure && (live?.HoldsConstructionSegments ?? CommandBand.ConstructionSegments(entry.Bindings).Count != 0));

        internal static IReadOnlyList<Edit> Of(SceneObservation entry, Live live = null) {
            if (entry == null) return Array.Empty<Edit>();
            var structure = entry.Identity.Structure;
            var starter = structure == StarterPlacementScene.Structure;
            var editable = CommandBand.EditableStructure(structure) && live?.Present != false;
            var coverage = CommandBand.EditsCoverage(entry.Identity);
            var controller = structure == ConstructionSceneryScene.Structure;
            var equipment = structure == ExistingEquipmentScene.MastStructure || structure == ExistingEquipmentScene.BellStructure;
            var bridge = live?.Bridge ?? entry.Bridge;
            // The object's own reason, which every placement and activity edit meets.
            var own = live?.Present == false ? EditReasons.Scene : starter ? CommandBand.EditReason(CommandBand.NotEditable(StarterPlacements.ObjectSettings)) : !editable ? CommandBand.EditReason(entry.Operations) : null;
            var move = own ?? (structure == CrossingPadScene.Structure ? CommandBand.PadFollowsTrack : controller ? CommandBand.ControllerNoPlacement : coverage ? live?.WholeMoveReason : null);
            var edits = new List<Edit> {
                new Edit(Move, Move, move),
                new Edit(Rotate, Rotate, move ?? CommandBand.RotationBlock(structure)),
                new Edit(Follow, Follow, move ?? (structure == MilepostScene.Structure ? CommandBand.MilepostNoFollow : equipment ? CommandBand.EquipmentNoFollow : coverage ? CommandBand.SetupNoFollow
                    : bridge == BridgeKind.Rail ? CommandBand.RailBridgeNoFollow : bridge == BridgeKind.Road ? CommandBand.RoadBridgeNoFollow : null)),
            };
            var enabled = live?.Enabled ?? entry.Enabled;
            edits.Add(new Edit(Activity, enabled ? "Disable" : "Enable", own ?? (controller ? CommandBand.ControllerNoActivity : coverage ? CrossingNoActivity : null) ?? live?.ActivitySwitched
                ?? (enabled ? live?.DisableProblem : null) ?? CommandBand.ActivityReason(!enabled, FollowsGroup(entry))));
            if (starter) {
                edits.Add(new Edit(TrackLocation, TrackLocation, null));
                edits.Add(new Edit(MarkerId, MarkerId, EditReasons.Replacement));
                edits.Add(new Edit(Wreck, Wreck, CommandBand.WreckReason(live?.StartersError, entry.Bindings.Where(b => b.StartsWith(StarterPlacements.ReaderPrefix, StringComparison.Ordinal)).ToArray())));
                return InScene(edits, live);
            }
            if (HoldsSegments(entry, live)) { edits.Add(new Edit(Segments, Segments, null)); return InScene(edits, live); }
            // A crossing controller's Placement tab moves its setup as a whole; its Connections tab edits its coverage and,
            // as a separate edit, its added lights, gates and bells.
            if (coverage) { edits.Add(new Edit(Coverage, Coverage, null)); edits.Add(new Edit(Equipment, Equipment, live?.EquipmentReason == null ? null : EditReasons.EquipmentUnread)); }
            // An object that holds a track binding: its own reason when the patcher does not edit it, as for every other edit.
            if (HoldsBinding(entry.Family)) edits.Add(new Edit(Binding, Binding, editable && EditsBinding(structure) ? null : own ?? CommandBand.EditReason(entry.Operations)));
            if (EditsBinding(structure))
                edits.Add(new Edit(MarkerId, MarkerId, own ?? (CommandBand.TakesMarkerId(entry.Identity) ? live?.MarkerIdReason : EditReasons.Replacement)));
            return InScene(edits, live);
        }
        static IReadOnlyList<Edit> InScene(IReadOnlyList<Edit> edits, Live live) => live?.Present == false
            ? edits.Select(edit => new Edit(edit.Key, edit.Name, EditReasons.Scene)).ToArray() : edits;
        // The reason of one edit, or null when it is available or not offered.
        internal static string ReasonOf(IEnumerable<Edit> edits, string key) => edits?.FirstOrDefault(edit => edit.Key == key)?.Reason;
        // Why a placement mode is not available: Numeric, Gizmo and Snap move and rotate, Follow follows a track.
        internal static string ModeReason(IEnumerable<Edit> edits, int mode) => ReasonOf(edits, mode == CommandBand.FollowMode ? Follow : Move);
        // Why a placement mode's button is inactive, or null. Numeric shows the placement and so stays open unless
        // keepsNumeric is false, as for a crossing pad or crossing equipment that cannot be edited; its fields then
        // carry the reason of Move.
        internal static string ModeBlock(IEnumerable<Edit> edits, int mode, bool keepsNumeric) => mode == CommandBand.NumericMode && keepsNumeric ? null : ModeReason(edits, mode);
        static readonly int[] AllModes = { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode, CommandBand.FollowMode };
        // Why the group can't be moved as a whole, as the whole sentence its Placement tab shows, or null: a group that a
        // settled rule leaves out of its family, such as one that holds a station click spot or a construction controller
        // outside a rail bridge. Such a group has no placement edit at all, so the tab names the reason and shows its
        // placement as text, without placement modes or actions that would do nothing.
        internal static string WholeMoveRefusal(SceneObservation entry) => CommandBand.NotEditableAsGroup(entry?.InspectionOnly);
        // The placement modes the Placement tab offers as buttons, each active or inactive with its reason (ModeBlock),
        // and the actions of the open mode with them: none for a group that can't be moved as a whole.
        internal static IReadOnlyList<int> PlacementModes(SceneObservation entry) => WholeMoveRefusal(entry) != null ? Array.Empty<int>() : AllModes;
        // Supported edits as the Object tab shows it: one line with the available edits, then one line for each reason
        // with the edits it holds back, the reason in the words of their controls' tooltips.
        internal static IReadOnlyList<string> Lines(IReadOnlyList<Edit> edits) {
            var lines = new List<string>();
            var available = edits.Where(edit => edit.Reason == null).Select(edit => edit.Name).ToArray();
            if (available.Length != 0) lines.Add("Available: " + String.Join(", ", available));
            var unavailable = edits.Where(edit => edit.Reason != null).GroupBy(edit => edit.Reason.TrimEnd('.')).ToArray();
            if (unavailable.Length != 0) lines.Add("Unavailable:");
            foreach (var group in unavailable)
                lines.Add("• " + group.Key + ": " + String.Join(", ", group.Select(edit => edit.Name)));
            return lines;
        }
    }
}
