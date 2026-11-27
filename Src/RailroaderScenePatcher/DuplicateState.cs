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
        internal const string Label = "Duplicate", Tip = "Make a new object from this one and place it in the world.";
        internal const string PlaceLabel = "Place", PlaceTip = "Put a ghost of the object on the pointer, then left-click the ground to place it.";
        internal const string ReleaseLabel = "Release", ReleaseTip = "Take the ghost off the pointer. Nothing is placed.";
        internal const string CancelLabel = "Cancel", CancelTip = "End Duplicate and return to the source object. Nothing is added.";
        internal const string BlankInstruction = "Move the camera to where the object goes, then click Place.";
        internal const string PlacingInstruction = "Left-click the ground to place the object. Click Release or press Esc to stop placing.";
        internal const string PointerInstruction = "Left-click to place";
        internal const string LockReason = "Place the object or click Cancel first.";
        internal const string NoGround = "No ground is under the pointer. Left-click the ground.";
        internal const string NotPlaced = "Not placed";
        internal const string Placed = "Mod object placed. Save when you're done.";
        // What Remove modification says for a mod object: the object itself goes, not only a change of it.
        internal const string RemoveTip = "Remove this mod object from the map. Your edits of its parts are removed with it.";
        // Why Duplicate is inactive for an object, or null: the short reasons of its tooltip.
        internal const string StarterMarker = "No start setup reads a duplicated marker.",
            InspectionOnly = "Inspection-only objects have no edits.",
            OtherFamily = "This family can't be duplicated.", HoldsOtherFamily = "It holds objects that can't be duplicated.";
        // The families whose objects a copy rebuilds soundly: a scene object with what stands below it, a part, which
        // becomes an object of its own, a station map icon, a location sign, which registers its own ground mask at its
        // place, and the Ela bridge. A copy of a bridge stands fully built and no construction controller switches it.
        // An object with a marker ID, a track binding or detection needs more than a copy.
        static readonly string[] Copied = { SceneFamilies.SceneObject, SceneFamilies.ChildObject, SceneFamilies.StationMapIcon, SceneFamilies.LocationSign, SceneFamilies.Bridge };
        // The families of scene markers a mod object is made of: the placing click gives it a marker ID of its own and
        // binds it to the track the game finds at its position.
        static readonly string[] Marked = { SceneFamilies.TrackMarker, SceneFamilies.Milepost };
        // familiesBelow: the families of the listed objects at or below the object.
        internal static string Reason(SceneObservation selected, IEnumerable<string> familiesBelow) {
            if (selected == null) return null;
            if (selected.Family == SceneFamilies.StarterPlacement) return StarterMarker;
            if (selected.InspectionOnly != null) return InspectionOnly;
            if (!Copied.Contains(selected.Family, StringComparer.Ordinal) && !Marked.Contains(selected.Family, StringComparer.Ordinal)) return OtherFamily;
            // A marker the patcher cannot edit takes no marker ID of its own, so it is not duplicated either.
            if (Marked.Contains(selected.Family, StringComparer.Ordinal) && !CommandBand.EditableStructure(selected.Identity.Structure)) return CommandBand.EditReason(selected.Operations);
            return (familiesBelow ?? Enumerable.Empty<string>()).Any(family => family != null && family != selected.Family && !Copied.Contains(family, StringComparer.Ordinal)) ? HoldsOtherFamily : null;
        }
        // The question that a selection in the outliner asks while the mod object is unplaced, with its title and choices.
        internal const string SelectTitle = "Cancel Duplicate?", KeepChoice = "Keep duplicating", CancelChoice = "Cancel Duplicate";
        internal static string SelectQuestion(SceneObservation source, string selectedName) =>
            "Inspecting " + selectedName + " cancels the duplicate of " + source.DisplayName + ". Nothing is added.";
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
