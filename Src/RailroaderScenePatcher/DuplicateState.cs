// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The pointer while a mod object is being placed: the left click on the world of this frame, and the map point
    // under a click. A click on a window, and a right or middle click that moves the camera, is no click here.
    internal interface IPlacingPointer {
        // The left click on the world of this frame, in screen coordinates, or null.
        double[] WorldClick { get; }
        // The map point of the ground under the click, or null where no ground is.
        double[] MapPoint(double[] click);
    }
    internal enum DuplicateStep { None, Blank, Placing }
    // Duplicate on the Object tab: the blank object window of a mod object that does not exist yet. Blank shows what is
    // known, with Place and Cancel; Placing waits for the left click that places the object. Until that click nothing
    // of the mod object exists: no object, no outliner entry and no entry in the scene patch file.
    internal sealed class DuplicateState {
        internal const string Label = "Duplicate", Tip = "Make a new object from this one and place it in the world.";
        internal const string PlaceLabel = "Place", PlaceTip = "Mark the pointer, then left-click the ground to place the object.";
        internal const string CancelLabel = "Cancel", CancelTip = "End Duplicate and return to the source object. Nothing is added.";
        internal const string BlankInstruction = "Move the camera to where the object goes, then click Place.";
        internal const string PlacingInstruction = "Left-click the ground to place the object. Press Esc to stop placing.";
        internal const string PointerInstruction = "Left-click to place";
        internal const string PlacingActive = "Left-click the ground to place the object.";
        internal const string LockReason = "Place the object or click Cancel first.";
        internal const string NoGround = "No ground is under the pointer. Left-click the ground.";
        internal const string NotPlaced = "Not placed";
        internal const string Placed = "Mod object placed. Save when you're done.";
        // What Remove modification says for a mod object: the object itself goes, not only a change of it.
        internal const string RemoveTip = "Remove this mod object from the map. Your edits of its parts are removed with it.";
        // Why Duplicate is inactive for an object, or null: the short reasons of its tooltip.
        internal const string StarterMarker = "No start setup reads a duplicated marker.", Part = "A part is duplicated with its group.",
            InspectionOnly = "Inspection-only objects have no edits.", Bridge = "Bridges can't be duplicated.",
            OtherFamily = "This family can't be duplicated.", HoldsOtherFamily = "It holds objects that can't be duplicated.";
        // The families whose objects a plain copy rebuilds soundly: a scene object with what stands below it, and a
        // station map icon. An object with a marker ID, a track binding or detection needs more than a copy.
        static readonly string[] Copied = { SceneFamilies.SceneObject, SceneFamilies.ChildObject, SceneFamilies.StationMapIcon };
        // familiesBelow: the families of the listed objects at or below the object.
        internal static string Reason(SceneObservation selected, IEnumerable<string> familiesBelow) {
            if (selected == null) return null;
            if (selected.Family == SceneFamilies.StarterPlacement) return StarterMarker;
            if (selected.InspectionOnly != null) return InspectionOnly;
            if (selected.GroupPath != null || selected.Family == SceneFamilies.ChildObject) return Part;
            if (selected.Bridge != BridgeKind.None) return Bridge;
            if (selected.Family != SceneFamilies.SceneObject && selected.Family != SceneFamilies.StationMapIcon) return OtherFamily;
            return (familiesBelow ?? Enumerable.Empty<string>()).Any(family => family != null && !Copied.Contains(family, StringComparer.Ordinal)) ? HoldsOtherFamily : null;
        }
        // The title of the blank object window: it names the duplicated source.
        internal static string Title(SceneObservation source) => "Duplicating " + source.DisplayName;

        internal DuplicateStep Step { get; private set; }
        internal bool Open => Step != DuplicateStep.None;
        // The object being duplicated.
        internal SceneObservation Source { get; private set; }
        // Why the last placing click placed nothing, or null.
        internal string Message { get; private set; }
        internal string Instruction => Step == DuplicateStep.Placing ? PlacingInstruction : Step == DuplicateStep.Blank ? BlankInstruction : null;
        // Why Place is inactive, or null.
        internal string PlaceReason => Step == DuplicateStep.Placing ? PlacingActive : null;

        // Duplicate: opens the blank object window for a new object made from the source.
        internal void Begin(SceneObservation source) {
            if (source == null || Open) return;
            Source = source; Step = DuplicateStep.Blank; Message = null;
        }
        // Place: the pointer is marked and the next left click on the world places the object.
        internal void Place() { if (Step == DuplicateStep.Blank) { Step = DuplicateStep.Placing; Message = null; } }
        // Esc while placing: back to the blank window with Place.
        internal bool Release() {
            if (Step != DuplicateStep.Placing) return false;
            Step = DuplicateStep.Blank;
            return true;
        }
        // Cancel: ends the duplicate and returns the source object, which the window inspects again.
        internal SceneObservation Cancel() {
            var source = Source;
            End();
            return source;
        }
        // Closing the window, a map reload or the end of editing: the unplaced mod object is gone.
        internal void End() { Step = DuplicateStep.None; Source = null; Message = null; }
        // One frame while placing. Only a left click on the world places: the object goes to the map point under it,
        // with the source's rotation. Returns the target of the placed mod object, or null while none was placed. A
        // click that places nothing, on no ground or refused by the edit, returns to the blank window with its reason.
        internal TargetIdentity Tick(IPlacingPointer pointer, Func<TargetIdentity, Placement, Outcome> place) {
            if (Step != DuplicateStep.Placing || pointer == null || place == null) return null;
            var click = pointer.WorldClick;
            if (click == null) return null;
            var point = pointer.MapPoint(click);
            if (point == null) { Message = NoGround; return null; }
            var outcome = place(Source.Identity, new Placement(point, Source.Rotation.ToArray()));
            if (outcome == null || !outcome.Applied) {
                Step = DuplicateStep.Blank;
                Message = outcome?.Error ?? "The object could not be placed.";
                return null;
            }
            var placed = outcome.Target;
            End();
            return placed;
        }
    }
}
