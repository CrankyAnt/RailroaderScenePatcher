// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The pointer while a mod object is being placed: where it is over the world, the left click on the world of this
    // frame, and the map point under a screen point. A click on a window, and a right or middle click that moves the
    // camera, is no click here.
    internal interface IPlacingPointer {
        // The pointer in screen coordinates while it is over the world, or null while it is over a window.
        double[] Pointer { get; }
        // The left click on the world of this frame, in screen coordinates, or null.
        double[] WorldClick { get; }
        // The map point of the ground under a screen point, or null where no ground is.
        double[] MapPoint(double[] screen);
    }
    internal enum DuplicateStep { None, Blank, Placing }
    // Duplicate on the Object tab: the blank object window of a mod object that does not exist yet. Blank shows what is
    // known, with Place and Cancel; Placing waits for the left click that places the object. Until that click nothing
    // of the mod object exists: no object, no outliner entry and no entry in the scene patch file.
    internal sealed class DuplicateState {
        internal const string Label = "Duplicate", Tip = "Make a copy of this object and place it in the world.";
        internal const string PlaceLabel = "Place", PlaceTip = "Show a ghost at the pointer. Left-click the ground to place the object there.";
        internal const string ReleaseLabel = "Release", ReleaseTip = "Hide the ghost and return to the Place button. Nothing is placed.";
        internal const string CancelLabel = "Cancel", CancelTip = "Discard the unplaced copy and return to the source object.";
        internal const string BlankInstruction = "Move the camera to where you want to put the copy, then click Place.";
        internal const string PlacingInstruction = "Left-click the ground to place the object. Click Release or press Esc to stop placing.";
        internal const string PointerInstruction = "Left-click to place";
        internal const string LockReason = "Place the object or click Cancel first.";
        internal const string NoGround = "There's no ground under the pointer. Move it over the ground and left-click.";
        internal const string NotPlaced = "Not placed";
        internal const string Placed = "Mod object placed. Click Save when you're done.";
        // What Remove modification says for a mod object: the object itself goes, not only a change of it.
        internal const string RemoveTip = "Remove this mod object and any edits you made to its parts.";
        // The crossing node of the outliner: what Duplicate and Remove say for a crossing without signals.
        // The mod's own crossing is removed there with Remove modification, as any mod object is.
        internal const string CrossingField = "Crossing", NotThisMods = "This mod doesn't change this object.", Removed = "Mod object removed. Click Save when you're done.";
        // Why Duplicate is inactive for an object, or null: the short reasons of its tooltip.
        internal const string StarterMarker = "No start setup uses the copy.",
            InspectionOnly = "You can only inspect this object.",
            OtherFamily = "This family can't be duplicated.", HoldsOtherFamily = "It contains objects that can't be duplicated.";
        // The families whose objects a copy rebuilds soundly: a scene object with what stands below it, a part, which
        // becomes an object of its own, a station map icon, a location sign, which registers its own ground mask at its
        // place, and the Ela bridge. A copy of a bridge stands fully built and no construction controller switches it.
        // A spawn point's copy takes a priority below every map spawn point and a teleport name of its own.
        static readonly string[] Copied = { SceneFamilies.SceneObject, SceneFamilies.ChildObject, SceneFamilies.StationMapIcon, SceneFamilies.LocationSign, SceneFamilies.Bridge, SceneFamilies.SpawnPoint };
        // The families of scene markers a mod object is made of: the placing click gives it a marker ID of its own and
        // binds it to the track the game finds at its position.
        // A crossing pad gets a marker ID of its own too and starts without a track binding.
        static readonly string[] Marked = { SceneFamilies.TrackMarker, SceneFamilies.Milepost, SceneFamilies.CrossingPad };
        // What a duplicated crossing is made from: its controller with what stands below it. Its copy holds its own
        // controller, masts, lights, gates, bell and pads. Its detection spans, its whistle signs and what a mod added
        // to it stay with the original: the copy's detection starts empty.
        static readonly string[] CrossingHolds = Copied.Concat(SceneFamilies.Crossing).Concat(new[] { SceneFamilies.TrackMarker }).ToArray();
        // The inspector heading while no row is inspected: the label of the selected crossing node, the node of a crossing
        // without signals that has no row of its own, or No Selection.
        internal static string EmptyHeading(string crossingNodeLabel) => String.IsNullOrEmpty(crossingNodeLabel) ? "No Selection" : crossingNodeLabel;
        // That heading's tooltip: the full path of the selected crossing node's crossing object, as an inspected object's
        // heading gives its path, or none for No Selection.
        internal static string EmptyHeadingTooltip(string crossingNodeLabel, string crossingPath) => String.IsNullOrEmpty(crossingNodeLabel) || String.IsNullOrEmpty(crossingPath) ? null : crossingPath;
        // The line below that heading: the select instruction, or none while a crossing node is selected, since the
        // crossing is the selection.
        internal static string EmptyInstruction(string crossingNodeLabel) => String.IsNullOrEmpty(crossingNodeLabel) ? CommandBand.SelectInstruction : null;
        internal const string Gone = "The inspected object no longer exists. Refresh the inventory.";
        // Why Duplicate is inactive in the window, or null, in this order: the object's own reason, a blocked session,
        // what blocks editing (no editing, a locked file, a pending world selection, an active gizmo), a source that
        // no longer exists and a pending scene check. sourceExists is about the source of Duplicate, which for a
        // crossing without signals is its outliner node's crossing object, not an inspected row.
        internal static string Inactive(string rule, string sessionBlocked, string editBlock, bool sourceExists, bool checkPending) =>
            rule ?? (sessionBlocked != null ? sessionBlocked + " The recovery choices are in the session details." : null) ?? editBlock
            ?? (!sourceExists ? Gone : null) ?? (checkPending ? "Wait for the scene check to finish." : null);
        // familiesBelow: the families of the listed objects at or below the object.
        internal static string Reason(SceneObservation selected, IEnumerable<string> familiesBelow) {
            if (selected == null) return null;
            if (selected.Family == SceneFamilies.StarterPlacement) return StarterMarker;
            if (selected.InspectionOnly != null) return InspectionOnly;
            // A crossing without signals is duplicated from its crossing object, which the outliner's crossing node stands for.
            var crossingObject = selected.Family == CrossingObjectScene.Family;
            if (selected.Family == SceneFamilies.CrossingController || crossingObject) {
                // A crossing the patcher cannot edit could not get its detection either.
                if (!crossingObject && !CommandBand.EditableStructure(selected.Identity.Structure)) return CommandBand.EditReason(selected.Operations);
                return (familiesBelow ?? Enumerable.Empty<string>()).Any(family => family != null && !CrossingHolds.Contains(family, StringComparer.Ordinal)) ? HoldsOtherFamily : null;
            }
            if (!Copied.Contains(selected.Family, StringComparer.Ordinal) && !Marked.Contains(selected.Family, StringComparer.Ordinal)) return OtherFamily;
            // A marker the patcher cannot edit takes no marker ID of its own, so it is not duplicated either.
            if (Marked.Contains(selected.Family, StringComparer.Ordinal) && !CommandBand.EditableStructure(selected.Identity.Structure)) return CommandBand.EditReason(selected.Operations);
            return (familiesBelow ?? Enumerable.Empty<string>()).Any(family => family != null && family != selected.Family && !Copied.Contains(family, StringComparer.Ordinal)) ? HoldsOtherFamily : null;
        }
        // The question that a selection in the outliner asks while the mod object is unplaced, with its title and choices.
        internal const string SelectTitle = "Cancel Duplicate?", KeepChoice = "Keep duplicating", CancelChoice = "Cancel Duplicate";
        internal static string SelectQuestion(SceneObservation source, string selectedName) =>
            "To inspect " + selectedName + ", discard the unplaced copy of " + source.DisplayName + ".";
        // The title of the blank object window: it names the duplicated source.
        internal static string Title(SceneObservation source) => "Duplicating " + source.DisplayName;

        internal DuplicateStep Step { get; private set; }
        internal bool Open => Step != DuplicateStep.None;
        // The object being duplicated.
        internal SceneObservation Source { get; private set; }
        // The map point where the ghost of the source stands while placing: the ground under the pointer. Null when
        // nothing is being placed, the pointer is over a window, or no ground is under it.
        internal double[] Ghost { get; private set; }
        // The object the modder selected in the outliner while the question whether to cancel the duplicate is open, or null.
        internal object Selecting { get; private set; }
        // Why the last placing click placed nothing, or null.
        internal string Message { get; private set; }
        internal string Instruction => Step == DuplicateStep.Placing ? PlacingInstruction : Step == DuplicateStep.Blank ? BlankInstruction : null;
        // The controls of the blank object window: Place and Cancel, and while placing Release in Place's slot.
        internal string[] Actions => Step == DuplicateStep.Placing ? new[] { ReleaseLabel, CancelLabel } : Step == DuplicateStep.Blank ? new[] { PlaceLabel, CancelLabel } : new string[0];

        // Duplicate: opens the blank object window for a new object made from the source.
        internal void Begin(SceneObservation source) {
            if (source == null || Open) return;
            Source = source; Step = DuplicateStep.Blank; Message = null;
        }
        // Place: the pointer is marked and the next left click on the world places the object.
        internal void Place() { if (Step == DuplicateStep.Blank && Selecting == null) { Step = DuplicateStep.Placing; Message = null; } }
        // Release or Esc while placing: the ghost leaves the pointer, back to the blank window with Place.
        internal bool Release() {
            if (Step != DuplicateStep.Placing) return false;
            Step = DuplicateStep.Blank; Ghost = null;
            return true;
        }
        // Cancel: ends the duplicate and returns the source object, which the window inspects again.
        internal SceneObservation Cancel() {
            var source = Source;
            End();
            return source;
        }
        // A selection in the outliner while the mod object is unplaced: true when the modder must first confirm that the
        // duplicate is cancelled. The ghost leaves the pointer while the question is open.
        internal bool AskBeforeSelecting(object selection) {
            if (!Open || selection == null) return false;
            Release();
            Selecting = selection;
            return true;
        }
        // The modder declined: the duplicate stays in the blank window.
        internal void KeepDuplicate() { Selecting = null; }
        // The modder confirmed: the duplicate ends and the object they selected is returned for inspection.
        internal object CancelForSelection() {
            var chosen = Selecting;
            End();
            return chosen;
        }
        // Closing the window, a map reload or the end of editing: the unplaced mod object is gone.
        internal void End() { Step = DuplicateStep.None; Source = null; Message = null; Ghost = null; Selecting = null; }
        // One frame while placing. Only a left click on the world places: the object goes to the map point under it,
        // with the source's rotation. Returns the target of the placed mod object, or null while none was placed. A
        // click that places nothing, on no ground or refused by the edit, returns to the blank window with its reason.
        internal TargetIdentity Tick(IPlacingPointer pointer, Func<SceneObservation, Placement, Outcome> place) {
            if (Step != DuplicateStep.Placing || pointer == null || place == null) return null;
            var at = pointer.Pointer;
            Ghost = at == null ? null : pointer.MapPoint(at);
            var click = pointer.WorldClick;
            if (click == null) return null;
            var point = pointer.MapPoint(click);
            if (point == null) { Message = NoGround; return null; }
            var outcome = place(Source, new Placement(point, Source.Rotation.ToArray()));
            if (outcome == null || !outcome.Applied) {
                Step = DuplicateStep.Blank; Ghost = null;
                Message = outcome?.Error ?? "The object could not be placed.";
                return null;
            }
            var placed = outcome.Target;
            End();
            return placed;
        }
    }
}
