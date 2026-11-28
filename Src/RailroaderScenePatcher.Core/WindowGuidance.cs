// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using System.Reflection;

namespace RailroaderScenePatcher.Core {
    // What the Scene Patcher window tells a map modder to do after a refusal or failure: a repair step, or the fact that
    // makes the step possible, such as a fallback control. The window shows a reason followed by its guidance; a log line
    // records only what happened, so PatcherLog leaves every text of this class out of the lines it writes. A message
    // composes its guidance from these constants, never from a copy, so the window and the log keep one wording.
    // Each text is one or more whole sentences, each ending with a period, and is appended after a space.
    public static class WindowGuidance {
        // Scene objects.
        public const string EditCrossingPadSeparately = "Edit its crossing pad in the Crossing pad marker family.";
        public const string EditSpawnPointsIndividually = "Edit its spawn points individually in the Spawn point family.";
        public const string RemovePadMarkerId = "Remove padMarkerId from this target.";
        public const string EditCulledParts = "Edit its parts directly.";
        public const string EditHeldGroups = "Edit the groups inside it.";
        public const string EditAsGroup = "Edit it as a group.";

        // Marker IDs and track bindings.
        public const string EnterUnusedId = "Enter an ID that no other scene marker or map object uses.";
        public const string GiveReplacementId = "Give one of them a replacement ID to repair it.";
        public const string RemoveSpace = "Remove the space.";
        public const string ChooseIdWithoutPrefix = "Choose an ID without that prefix.";
        public const string ChooseIdNoMarkerUses = "Choose an ID that no other scene marker uses.";
        public const string ChooseAnotherId = "Choose another ID.";
        public const string TryAfterMapLoaded = "Try again after the map has loaded.";
        public const string GenerateAgain = "Click Generate again, or enter an ID.";
        public const string ChangeDependentFirst = "Change or remove that modification first.";
        public const string ChangeDependentsFirst = "Change or remove those modifications first.";
        public const string ClearSettingsFirst = "Clear those settings first, or click Remove modification.";
        public const string ReloadMapEditorBeforeEditing = "Reload the Map Editor before editing it.";
        public const string OmitActive = "Omit active to keep group-driven activity.";

        // Captured state that no longer matches.
        public const string ReloadBeforeEditing = "Reload before editing it.";

        // Crossing coverage and detection additions.
        public const string KeepOneCircuit = "Keep or select coverage for at least one circuit.";
        public const string SelectSpanTracks = "Select those tracks explicitly.";
        public const string GiveAdditionId = "Give the addition an ID.";
        public const string ChooseCircuit = "Choose a circuit of the crossing.";
        public const string ClearCoverageForNative = "Clear the coverage set by this mod for the circuit to return it to the map's own detection spans.";
        public const string AddDetectionAsAddition = "Add detection for extra tracks as an addition.";
        public const string OmitDetachedAddition = "Omit it to delete it.";
        public const string SelectTracksThenPreview = "Select its tracks, then click Preview.";

        // Construction segments.
        public const string SelectBridgeTrackSegments = "Select the segments of the bridge's track instead, or clear this mod's value to keep the native segments.";
        public const string EnterSegmentId = "Enter at least one segment ID.";

        // Crossing equipment.
        public const string ControllerDrivesCopy = "The crossing controller drives it.";
        public const string ChooseMapDevice = "Choose a device already present in the map.";
        public const string AddBellsFromDefinition = "Add bells from a bell definition using the bell pattern.";
        public const string UseSupportedPattern = "Use a supported pattern or remove the addition.";
        public const string ChangeSourceOrRemove = "Change its source in your scene patch file or remove the addition.";
        public const string OnlyAvailableBells = "The patcher only uses bell definitions already available and will not choose a replacement.";
        public const string OnlyMapLights = "The patcher only copies lights and gates already in the map and will not choose a replacement.";
        public const string SelectAnotherBell = "Edit this addition on the crossing's Connections tab to select another bell definition, or remove the addition.";
        public const string SelectAnotherLight = "Edit this addition on the crossing's Connections tab to select another light or gate, or remove the addition.";
        public const string SelectEligibleSource = "Select an eligible source or remove the addition.";
        public const string GivePlacement = "Give it a position and rotation or remove the addition.";
        public const string RemoveOrReplaceBell = "Remove or replace that bell.";
        public const string ReloadBeforeEditingEquipment = "Reload before editing the crossing's equipment.";
        public const string CoverageStillEditable = "You can still edit crossing coverage.";
        public const string EditOnController = "Edit or remove it on its crossing controller's Connections tab.";
        public const string SetCoverageOnController = "Set coverage on its crossing controller.";
        public const string AddEquipmentOnController = "Add equipment on its crossing controller.";
        public const string ChooseAddition = "Choose an addition.";

        // Incomplete cleanup.
        public const string Recheck = "Click Recheck or reload the Map Editor.";
        public const string RecheckBeforeEditing = "Click Recheck or reload the Map Editor before editing it again.";
        public const string RecheckBeforeFollowing = "Click Recheck or reload the Map Editor before following the track again.";
        public const string RecheckBeforeImporting = "Click Recheck or reload the Map Editor before importing.";
        public const string RecheckBeforeFileVersion = "Click Recheck or reload the Map Editor before using the file version.";
        public const string RecheckEmptyObject = "Click Recheck to try removing it, or reload the Map Editor.";
        public const string ReloadMapEditor = "Reload the Map Editor.";

        // Scene patch file versions, import, backups and recovery.
        public const string ChooseVersionPerConflict = "Choose the editor version or the file version for each conflict first.";
        public const string ImportOrCancel = "Click Import or Cancel first.";
        public const string ChooseVersion = "Choose a version to continue.";
        public const string ReviewUpdatedChoice = "Review the updated choice before continuing.";
        public const string ReviewUpdatedDiagnosis = "Review the updated diagnosis before continuing.";
        public const string RefreshToCheck = "Refresh to check it again.";
        public const string DeleteBackup = "Delete it when you no longer need it.";
        public const string CorrectProtectedFile = "Correct it outside scene patch editing and read it again, or start fresh behind a backup of the original.";
        public const string CorrectInfoJson = "Correct Info.json outside scene patch editing and read it again.";
        public const string SaveAndReopen = "Save your map edits and reopen the Map Editor before trying to recover the scene patch file.";
        public const string ConfirmOutsideCorrection = "Confirm the outside correction before reading the scene patch file again.";
        public const string RestoreOrReopen = "Restore the file and read it again, or reopen the Map Editor to start a new one.";
        public const string ConfirmFreshStart = "Confirm that fresh start removes all content of the scene patch file.";
        public const string CorrectInfoJsonAndRead = "Correct Info.json and read it again.";
        public const string ReopenToStartNew = "Reopen the Map Editor to start a new one.";
        public const string ReviewBeforeSaving = "Review the new file version before saving.";
        public const string ReopenToRead = "Reopen the Map Editor to read the scene patch file again.";
        public const string SaveReplacesWithEmpty = "Save replaces it with an empty scene patch file.";
        public const string SeeDiagnostics = "See the diagnostics for the reasons.";

        // Scene patch editing and Save.
        public const string CorrectConstraint = "Correct that constraint and save again.";
        public const string CorrectThatFirst = "Correct that first.";
        public const string CombineModifications = "Combine them into one modification in the file.";
        public const string SaveStillAvailable = "Save is still available.";
        public const string SaveAfterHistoryError = "Save still writes the current changes. Reload the Map Editor to clear its unsaved status.";
        public const string CorrectAndSave = "Correct the cause and Save again.";
        public const string CheckPlayerLog = "Check the Player log for loading errors before trying again.";
        public const string WaitForReturn = "Wait for the return to finish or time out before reloading again.";
        public const string RequestReloadAgain = "Request reload again.";

        // Targets and inspection.
        public const string CompareTargetIdentity = "Inspect the intended object in the Scene Patcher. On the Object tab, compare its Identity fields with the target's scene, path, and component in the scene patch file. Correct the target or remove this modification from the file.";
        public const string UseSpecificTarget = "Use a more specific path or component so the target matches only one object.";
        public const string UseBottomLayer = "Use the bottom map layer.";
        public const string RemoveIdMember = "Remove the id member: the key of a modification is its ID.";
        public const string RefreshAndInspect = "Refresh and inspect again.";
        public const string RefreshInventoryAndInspect = "Refresh the inventory and inspect the object first.";
        public const string RefreshAndRepeat = "Click Refresh, inspect the object again, then repeat the action.";
        public const string RefreshThenInspect = "Click Refresh, then inspect the object again.";
        public const string ClickRefreshAndInspect = "Click Refresh and inspect it again.";
        public const string CheckOutlinerOrTracks = "Check scene objects in the outliner or tracks in the Map Editor.";

        // Failure recovery.
        public const string CorrectAndRetry = "Correct the cause and try again.";
        public const string WaitForTerrain = "Wait for the terrain to update before editing again, or click Retry on the Changes tab.";
        public const string CorrectDependency = "First correct the modification that this one depends on.";
        public const string CorrectProblem = "Correct the problem and try again.";

        // World choice, track following and snapping.
        public const string SelectOfferRow = "Select a row from the list next to the pointer.";
        public const string SelectListRow = "Select a row from that list.";
        public const string CancelAndStartAgain = "Cancel and start a new selection.";
        public const string CancelChoice = "Cancel; a choice for this object can't be confirmed.";
        public const string CancelAndInspect = "Cancel and inspect it again.";
        public const string PickUniquePath = "Pick an object whose path is unique.";
        public const string PickAgain = "Pick again.";
        public const string SelectFollowAnchor = "Select a track or endpoint to follow.";
        public const string SelectSnapTrack = "Select a track segment or endpoint first.";
        public const string InspectBeforeSnapping = "Inspect the object before snapping.";
        public const string SelectAgain = "Select it again.";
        public const string SelectUniqueIdentity = "Select a unique identity.";
        public const string PrepareSnap = "Prepare a snap for this scene patch editing session first.";
        public const string SelectTargetAgain = "Select the target again.";
        public const string SelectTrackAgain = "Select the track again.";
        public const string ChooseTrackLocation = "Choose a location on the track.";
        public const string SetStarterFields = "Set track, wreck or both in starterPlacement.";

        // Window controls and entered values.
        public const string ApplyPreview = "Click Apply to apply it.";
        public const string WidenWindow = "Widen the window for more.";
        public const string EnterTrackLocation = "Enter the segment ID, end a or b, and a distance. Use a point for decimals.";
        public const string EnterPlacement = "Enter a number in each of the six fields. Use a point for decimals.";
        public const string EnterByHand = "Enter end and distance by hand.";
        public const string ConfirmOrCancelChoice = "Confirm or cancel it first.";
        public const string CompleteOrCancelGizmo = "Complete or cancel it first.";
        public const string SelectActionAgain = "Select the action again.";
        public const string InspectAndFinishChoice = "Inspect a supported target and finish world selection first.";
        public const string ReleaseNativeGizmo = "Release the Map Editor gizmo first.";
        public const string InspectFirst = "Inspect an object first.";
        public const string DisablePad = "Disable it instead.";
        public const string ConfirmTrack = "Choose and confirm a track or endpoint first.";
        public const string SetSegmentsOnConnections = "Set its construction segments on the Connections tab.";
        public const string FinishGuidedTask = "Finish this task or return to Inspector before clearing the selection or selecting another object.";

        // Terrain and load results.
        public const string ReloadMap = "Reload the map.";
        public const string ReloadMapBeforeRetrying = "Reload the map before trying again.";
        public const string PlacementKeptUntilReload = "The object keeps its applied placement. Reloading the map regenerates the output.";
        public const string RetryOnChanges = "Click Retry on the Changes tab.";
        public const string RetriedAtNextLoad = "The patcher retries at the next load; reload the map to clear it.";

        // Every text above, longest first, so a text that contains a shorter one goes first.
        static readonly string[] all = typeof(WindowGuidance).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue())
            .OrderByDescending(t => t.Length).ToArray();
        public static System.Collections.Generic.IReadOnlyList<string> All => all;

        // The text without the guidance it holds. A guidance text counts where it starts the text or follows a space, and
        // ends the text or is followed by a period or other closing punctuation, which a caller adds when it ends a
        // reason with it; the space before it goes with it.
        public static string Without(string text) {
            if (String.IsNullOrEmpty(text)) return text;
            foreach (var guidance in all) {
                var core = guidance.TrimEnd('.');
                for (var at = text.IndexOf(core, StringComparison.Ordinal); at >= 0; at = text.IndexOf(core, at, StringComparison.Ordinal)) {
                    var end = at + core.Length;
                    var before = at == 0 || text[at - 1] == ' ';
                    var after = end == text.Length || ".;:,)".IndexOf(text[end]) >= 0;
                    if (!before || !after) { at = end; continue; }
                    if (end < text.Length && text[end] == '.') end++;
                    var start = at;
                    if (start > 0) start--;
                    else if (end < text.Length && text[end] == ' ') end++;
                    text = text.Remove(start, end - start);
                    at = start;
                }
            }
            return text;
        }
    }
}
