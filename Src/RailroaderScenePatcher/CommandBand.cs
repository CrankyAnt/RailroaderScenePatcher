// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The decisions of the command band, the row markers and the default window placement that need no game: which word
    // and colour a status lamp shows, when a command is disabled, which markers stand before a name, and where the
    // window opens. UI-independent, shared with controlled replay.
    internal static class CommandBand {
        internal const string Green = "#7FB86B", Amber = "#E8A33D", Red = "#FF8080";
        // A status lamp: a coloured dot and a word. It is information and has no action.
        internal sealed class Lamp {
            internal Lamp(string word, string reserve, bool good) { Word = word; Reserve = reserve; Good = good; }
            // The state word shown now.
            internal string Word { get; }
            // The longer of the lamp's two words: the lamp reserves its width, so the word changes in place.
            internal string Reserve { get; }
            internal bool Good { get; }
            // Dot and word have one colour: green when good, amber otherwise.
            internal string Color => Good ? Green : Amber;
        }
        internal static Lamp Inventory(bool stale, bool available = true) =>
            new Lamp(!available ? "Paused" : stale ? "Stale" : "Current", "Current", available && !stale);
        internal static Lamp Save(bool unsaved, bool available = true) =>
            new Lamp(unsaved ? "Unsaved" : available ? "Saved" : "Blocked", "Unsaved", available && !unsaved);

        internal static string SaveNotice(IReadOnlyList<string> outstanding, FileVersionState version, bool fileMissing) {
            var reason = version == FileVersionState.Changed ? "The scene patch file changed outside scene patch editing. Choose a file version before saving."
                : version != FileVersionState.Repair ? null
                : fileMissing ? "Restore the scene patch file or click Keep editor version before saving."
                : "The scene patch file needs repair. Resolve the issue before saving.";
            if (outstanding.Count == 0) return reason ?? "There are no changes to save.";
            var writes = PatchEditingSession.OutstandingWriteList(outstanding);
            return reason == null ? "Click Save to write changes to the " + writes + "."
                : "Changes to the " + writes + " still need saving. " + reason;
        }
        // The left band's fixed widths in canvas units: the Refresh button as the game lays it out (74.15 measured), the
        // spacing between the band's items, a lamp's dot and the gap to its word, and the width each lamp reserves for
        // its longer word. They are constants, not measurements taken while the band is built: a text measured before
        // its first layout gave widths under which the words never fitted.
        internal const float RefreshWidth = 75, Spacing = 4, Dot = 10, Gap = 3, InventoryWord = 66, SaveWord = 72;
        // A lamp with its word: the dot, the gap and the word's rectangle.
        internal const float InventoryLamp = Dot + Gap + InventoryWord, SaveLamp = Dot + Gap + SaveWord;
        // What Refresh and both lamps with their words need: the three items and the three spacings of the row, the last
        // one before the space that takes what is left over. The outliner is never narrower, so at the minimum width
        // every word shows and the items keep their spacing; one spacing short, the row pulled the lamps towards
        // Refresh.
        internal const float LeftBandWidth = RefreshWidth + InventoryLamp + SaveLamp + 3 * Spacing;
        // The saved bounds of the window hold its content size, and the native restore raises a content size to the
        // window's minimum, which is meant for the whole window. A saved content size is therefore restored as saved:
        // never smaller than the minimum window less its frame, never raised to the minimum window itself.
        internal static float RestoredContent(float saved, float minimumWindow, float frame) => Math.Max(saved, minimumWindow - frame);
        // Whether a left band of this width holds Refresh and both lamps with their words. When it does not, each lamp
        // keeps its dot and its word moves into the tooltip.
        internal static bool WordsFit(float band) => LeftBandWidth <= band + 0.5f;
        // The title also identifies a lamp when only its dot fits in the command band.
        internal static (string Title, string Body) LampTooltip(Lamp lamp, string reason) => (lamp.Word, Sentence(reason));
        // Why a command of the right band is disabled now, or null. A disabled command stays in place. Select in world
        // is disabled only while a world choice is pending, Focus and Copy ID only when nothing is inspected; a world
        // selection that could not be set up at all has nothing to start.
        internal static string SelectInWorld(bool available, bool pending) =>
            !available ? "World selection is unavailable." : pending ? "Confirm or cancel the pending world choice first." : null;
        // Focus is also disabled, with its reason, when the inspected object has no point to go to, such as a starter
        // placement marker whose track location does not resolve.
        internal static string Focus(bool inspected, string noTarget = null) => !inspected ? "Inspect an object first." : String.IsNullOrEmpty(noTarget) ? null : noTarget;
        // Copy ID is also disabled, with the editing session's refusal, while the edited mod's scene patch file cannot be read.
        internal static string CopyId(bool inspected, string refusal = null) => inspected ? refusal : "Inspect an object first.";
        // Clear ends the inspection and cancels a pending world choice: it is disabled only when there is neither.
        internal static string Clear(bool inspected, bool pending) => inspected || pending ? null : "Nothing is inspected and no world choice is pending.";
        // The tooltip of a control: what it does while it can be used, and otherwise only why it cannot now. The
        // description is left out of a disabled control's tooltip, since it applies only while the control can be used;
        // the tooltip's title still names the control. The reason follows the one label Unavailable: as a plain sentence
        // (Sentence), without a label of its own.
        internal static string Tooltip(string description, string reason) =>
            String.IsNullOrEmpty(reason) ? description : "Unavailable: " + Sentence(reason);
        // The labels a reason can carry where it is stored or shown on its own: under Supported edits, or in the
        // reason of a binding that cannot be set.
        static readonly string[] ReasonLabels = { "Not editable: ", "Not yet supported: ", "Not editable as a group: ", "Track binding unavailable: ", "Pad binding unavailable: " };
        // Whether a failed action's message is a warning, in amber, rather than a failure that left something to correct, in
        // red. The failure kind decides, not a word in the text: an edit that was undone and waits for its terrain is a warning.
        internal static bool RecoveryWarning(FailureKind kind) => kind == FailureKind.AwaitingOutput;
        // A reason as one plain sentence after a label such as "Unavailable:", without a label of its own, starting with a
        // capital and ending with a full stop.
        internal static string Sentence(string reason) {
            var text = (reason ?? "").Trim();
            for (var stripped = true; stripped;) {
                stripped = false;
                foreach (var label in ReasonLabels)
                    if (text.StartsWith(label, StringComparison.Ordinal)) { text = text.Substring(label.Length).TrimStart(); stripped = true; }
            }
            if (text.Length == 0) return text;
            text = Char.ToUpperInvariant(text[0]) + text.Substring(1);
            return text.EndsWith(".", StringComparison.Ordinal) || text.EndsWith("?", StringComparison.Ordinal) || text.EndsWith("!", StringComparison.Ordinal) ? text : text + ".";
        }
        // The footer report of a completed preview: the preview's own lines, as the form shows them, then the click that
        // applies it. A removal or correction that builds nothing says so here too, wherever the form stands in the tab.
        internal static string PreviewReport(string kind, IEnumerable<string> lines) {
            var shown = (lines ?? Enumerable.Empty<string>()).Where(line => !String.IsNullOrWhiteSpace(line)).Select(Sentence).ToArray();
            return kind + " preview completed. " + (shown.Length == 0 ? "" : String.Join(" ", shown) + " ") + "Click Apply to apply it.";
        }
        // The addition a Use corrects: the refused addition whose Edit form is open (editedId) takes the device as its
        // source and keeps its ID and placement. Null when Use starts a new addition: no form, a new addition's form, or
        // the form of an addition that is built or that the scene patch file no longer holds.
        internal static string CorrectedAddition(string editedId, IEnumerable<EquipmentEntry> entries) =>
            editedId == null ? null : (entries ?? Enumerable.Empty<EquipmentEntry>()).Any(e => e?.Addition?.Id == editedId && e.Problem != null) ? editedId : null;
        // The native alert for a refused Save while the Scene Patcher window is closed, or null while it is open, where the
        // message area shows the refusal. The Map Editor's Save button stays enabled, so a refusal must never look like an
        // ignored click.
        internal static (string Title, string Message)? SaveRefusalAlert(bool windowOpen, string reason, bool versionChoice = false) =>
            windowOpen ? ((string Title, string Message)?)null : ("Save refused", versionChoice
                ? "Nothing was saved. The scene patch file changed outside the Map Editor. Open Scene Patcher to choose which version to keep."
                : "Nothing was saved. " + Sentence(reason));
        // The three controls that take something back are named by what they take back. Object: this mod's activity
        // value. Placement, Snap: the snap that was prepared and not applied. Changes: the whole modification.
        internal const string ClearActivity = "Clear activity", DiscardPrepared = "Discard", RemoveModification = "Remove modification", ClearBinding = "Clear binding", ClearCircuit = "Clear circuit", ClearLocation = "Clear location", ClearSegments = "Clear segments";
        // Why Clear binding has nothing to clear: this mod sets no track binding for the marker, milepost or crossing pad.
        internal static string ClearBindingReason(bool hasValue) => hasValue ? null : "This mod sets no track binding for this object, so there is nothing to clear.";
        // Why Clear location has nothing to clear: this mod sets no track location for the starter placement marker.
        internal static string ClearLocationReason(bool hasValue) => hasValue ? null : "This mod sets no track location for this marker, so there is nothing to clear.";
        // The name of a wreck edit, after the checkbox's new state.
        internal static string WreckAction(bool wreck) => wreck ? "Start derailed: Yes" : "Start derailed: No";
        // Why the Wreck checkbox of a starter placement marker cannot be used, or null: the setups could not be read, or
        // not exactly one active car placement reads the marker. readers are the marker's reader lines. The reasons are
        // the ones the engine refuses a wreck setting with.
        internal static string WreckReason(string setupsError, IReadOnlyList<string> readers) {
            if (setupsError != null) return Sentence(StarterPlacements.UnreadableReason(setupsError));
            var count = readers?.Count ?? 0;
            return count == 1 ? null : count == 0 ? Sentence(StarterPlacements.NoReader)
                : Sentence(StarterPlacements.SeveralReaders(readers.Select(StarterPlacements.PlaceOf).ToArray()));
        }
        // Why the guided task that sets Start derailed cannot start, or null: it fits only a starter placement marker, and
        // there only where the inspector's checkbox can be used (WreckReason).
        internal static string WreckTaskReason(bool starter, string setupsError, IReadOnlyList<string> readers) =>
            starter ? WreckReason(setupsError, readers) : "This object is not a starter placement marker.";
        // The help line under the Start derailed choice in the guided task's Values, in the term of the inspector's
        // Starting cars section.
        internal const string WreckValuesHelp = "To remove this mod's value, choose the setting the starting cars have without this mod.";
        // Why the Start derailed value of the guided task cannot be reviewed: it is the car placement's current setting.
        internal const string SameWreck = "The car placement already has the selected Start derailed setting.";
        // Why Clear activity cannot run for an object this mod sets no activity for, or null.
        internal static string ClearActivityReason(bool hasValue) => hasValue ? null : "This mod sets no activity for this object, so there is nothing to clear.";
        // Why the footer's Details of a world choice cannot show anything, or null: it shows the clicked candidate.
        internal static string CandidateDetailsReason(int offered) => offered == 0 ? "Click a marking or track in the world first." : null;
        // The supported-edits text of an object the patcher cannot edit, with the reason that was found. It promises
        // no correction.
        internal static string NotEditable(string reason) => NotEditablePrefix + reason;
        // Why an object whose hierarchy path and component more than one loaded object holds cannot be edited, or null:
        // a patch names its target by them, so it cannot name one of the holders.
        internal static string SharedPath(int holders) => holders > 1 ? holders + " objects share this hierarchy path and component, so a patch cannot name one of them" : null;
        // The Track group line of the Object tab. An object without edits is told what its activity follows, not what
        // Disable and Clear activity would do.
        internal static string TrackGroup(bool editable) => editable
            ? "Activity follows the track group. Disable holds it inactive. Clear activity returns control to the track group."
            : "Activity follows the track group.";
        // Why the activity control cannot run, or null. The patcher refuses an explicit enable of an object whose activity
        // follows its track group, so Enable is inactive with that reason beforehand; Disable stays available.
        internal static string ActivityReason(bool enable, bool followsGroup) => enable && followsGroup ? GroupEnableReason : null;
        internal const string GroupEnableReason = "The track group controls this object's activity. Use Clear activity to let the object follow its group again.";
        // Whether the open help closes on a tab switch: the help of a tab that is no longer the open tab. The help of
        // the outliner or of the window stays.
        internal static bool ClosesHelp(string openRegion, string tab, IEnumerable<string> tabs) {
            if (openRegion == null || openRegion == tab) return false;
            foreach (var name in tabs) if (name == openRegion) return true;
            return false;
        }
        // One padding rule for every button of the panel: the band, a tab's row, the footer's row and the buttons inside
        // a tab. A button is as wide as its label and the native compact padding on both sides, 6 units each (measured
        // on the band: "Refresh" is 62.15 wide in a button of 74.15). A button whose label changes in place, such as
        // Disable and Enable or "?" and "x", takes the width of its widest label, so the change moves nothing. No
        // button has a width of its own beside this rule.
        internal const float ButtonPadding = 6;
        internal static float ButtonWidth(params float[] labelWidths) {
            float widest = 0;
            foreach (var width in labelWidths ?? new float[0]) if (width > widest) widest = width;
            return (float)Math.Ceiling(widest + 2 * ButtonPadding);
        }
        // An action row stays within this width, which the inspector has at its minimum; its buttons stand 4 apart.
        internal const float ActionRowBudget = 340, ActionRowSpacing = 4;
        internal static bool RowFits(IEnumerable<float> buttonWidths) {
            float total = 0; var count = 0;
            foreach (var width in buttonWidths) { total += width; count++; }
            return total + Math.Max(0, count - 1) * ActionRowSpacing <= ActionRowBudget;
        }
        // The shorter label a button takes when its row would not fit with the full ones, or the label itself.
        internal static string ShortLabel(string label) => label == RemoveModification ? "Remove" : label == FilePanelState.Copy ? "Copy" : label;
        // The pinned rows of the file panel that must fit the inspector's minimum, with full or short labels: Repair and Import.
        internal static readonly string[][] FilePanelRows = {
            new[] { "Keep editor version", "Details", "Copy to clipboard" },
            new[] { "Import", "Cancel" },
        };
        // Every action row of the panel, for the table test of the rule: each row a list of buttons, each button its
        // label or the labels it changes between.
        internal static readonly string[][][] ActionRows = {
            new[] { new[] { RemoveModification }, new[] { "Recheck" }, new[] { "Guide" } },
            new[] { new[] { "Confirm" }, new[] { "Cancel" }, new[] { "Focus" }, new[] { "Details" } },
            new[] { new[] { "Cancel" } },
            new[] { new[] { "Disable", "Enable" }, new[] { ClearActivity } },
            new[] { new[] { "Apply" }, new[] { "Read" } },
            new[] { new[] { "Track" }, new[] { "Node" }, new[] { "Apply" }, new[] { DiscardPrepared } },
            new[] { new[] { "Track" }, new[] { "Node" }, new[] { "Start" }, new[] { "Stop" } },
            new[] { new[] { "Set" }, new[] { "Detach" }, new[] { ClearBinding }, new[] { "Picked" } },
            new[] { new[] { "Set" }, new[] { "Detach" }, new[] { ClearLocation }, new[] { "Picked" } },
            new[] { new[] { "Set" }, new[] { "Detach" }, new[] { ClearSegments }, new[] { "Picked" } },
            new[] { new[] { "Preview" }, new[] { "Detach" }, new[] { ClearCircuit }, new[] { "Apply" } },
            new[] { new[] { "Preview" }, new[] { "Delete" }, new[] { "Apply" } },
            new[] { new[] { "Retry" } },
            new[] { new[] { "Select in world" }, new[] { "Focus" }, new[] { "Clear" }, new[] { "Copy ID" } },
            new[] { new[] { "Refresh" } },
            new[] { new[] { "?", "x" } },
            new[] { new[] { "Fit" } },
        };
        // The widths of those labels in the native compact button's text, Montserrat-Medium 16, as the UI probe measured
        // them in the game; Discard, Start, Guide and Clear location from the glyph advances fitted to the measured labels. The panel
        // measures its labels itself; this table lets the rule be checked without the game.
        internal static readonly Dictionary<string, float> LabelWidths = new Dictionary<string, float>(StringComparer.Ordinal) {
            { "Remove modification", 174.47f }, { "Remove", 66.76f }, { "Recheck", 70.02f }, { "Confirm", 65.75f }, { "Cancel", 55.19f }, { "Focus", 48.04f }, { "Details", 56.15f },
            { "Disable", 60.44f }, { "Enable", 56.36f }, { "Clear activity", 104.74f }, { "Apply", 46.95f }, { "Read", 41.67f }, { "Track", 43.46f }, { "Node", 43.86f },
            { "Discard", 61.5f }, { "Start", 39f }, { "Stop", 37.35f }, { "Set", 26.36f }, { "Detach", 59.13f }, { "Clear binding", 109.6f }, { "Clear location", 112.5f }, { "Picked", 55.25f },
            { "Preview", 64.68f }, { "Clear circuit", 96.93f }, { "Delete", 53.4f }, { "Retry", 43.8f }, { "Refresh", 62.15f }, { "Select in world", 119.96f }, { "Clear", 41.71f },
            { "Copy ID", 63.48f }, { "Fit", 21.26f }, { "?", 9.17f }, { "x", 8.84f }, { "Guide", 47f },
            // Estimated from the game capture of the Repair row (Copy to clipboard 143 units wide with its padding) and
            // the glyph advances of the measured labels.
            { "Keep editor version", 152f }, { "Copy to clipboard", 131f }, { "Copy", 41f }, { "Import", 52f },
            // Estimated from the glyph advances of Clear location and Clear circuit.
            { ClearSegments, 119f } };
        // One colour table for every world marking. Gold frames the inspected object. A candidate of a pick mode is
        // blue: more saturated while the pointer is over it, plain blue once it was clicked and until Confirm. A current
        // relation of the inspected object, native or set by a patch, is green, and more saturated under the pointer.
        // A candidate that is neither hovered nor clicked shows only its neutral "+" marker. Red marks a broken item.
        internal enum WorldMark { Inspected, Marker, CandidateHover, CandidateSelected, Relation, RelationHover, Broken }
        internal static float[] WorldColor(WorldMark mark) {
            switch (mark) {
                case WorldMark.Inspected: return new[] { 1f, .8f, 0f };
                case WorldMark.CandidateHover: return new[] { .1f, .6f, 1f };
                case WorldMark.CandidateSelected: return new[] { .3f, .8f, 1f };
                case WorldMark.Relation: return new[] { .498f, .722f, .42f };
                case WorldMark.RelationHover: return new[] { .3f, .78f, .2f };
                case WorldMark.Broken: return new[] { 1f, .502f, .502f };
                default: return new[] { .8156863f, .7950737f, .746353f };
            }
        }
        // The translucent fill of a track ribbon, in the marking's colour: one value for every ribbon.
        internal const float WorldFill = .22f;
        internal static WorldMark CandidateMark(bool clicked, bool hovered) => clicked ? WorldMark.CandidateSelected : hovered ? WorldMark.CandidateHover : WorldMark.Marker;
        internal static WorldMark RelationMark(bool hovered) => hovered ? WorldMark.RelationHover : WorldMark.Relation;
        // In a pick mode every candidate of that mode in view carries its "+" marker without the pointer over it. A
        // track's marker shows within this distance of the camera, so a wide view is not filled with them; a hovered or
        // clicked candidate always shows.
        internal const double TrackMarkerDistance = 500;
        internal static bool ShowsMarker(bool track, double distance, bool hoveredOrClicked) => hoveredOrClicked || !track || distance <= TrackMarkerDistance;
        // The placement mode an object opens in: Follow for an object that follows a track, Numeric otherwise.
        internal const int NumericMode = 0, GizmoMode = 1, SnapMode = 2, FollowMode = 3;
        internal static int PlacementMode(bool followsTrack) => followsTrack ? FollowMode : NumericMode;
        internal const string GizmoPlace = "Gizmo";
        // The queued Gizmo request opens the tool. The drag commits separately.
        internal static bool ClearsConnectionsPreviewOnQueue(string place) => place != GizmoPlace;
        // The result of a placement edit on a crossing pad. The pad's geometry is generated along its track binding, so
        // moving the pad object moves nothing the player sees: the result says so instead of "completed".
        internal static string PadPlacement(string action) => action + " changed the pad object's physical placement only. The pad's geometry is generated along its track binding and did not move; change the binding on Connections to move the pad.";
        // Terrain output in plain words, for an object that has terrain output at all.
        internal static string Terrain(string problem, string error, bool settled, string completed, string outstanding, bool anyCompleted) =>
            problem != null ? "Terrain state unknown: " + problem
            : error != null ? "Terrain update stopped: " + error + ". Updated: " + completed + ". Pending: " + outstanding + ". The edit is kept."
            : !settled ? "Terrain pending: the game is regenerating " + outstanding + ". Updated so far: " + completed + "."
            : anyCompleted ? "Terrain updated: " + completed + "." : "Terrain unchanged.";
        // A log line names the action group, the object and what happened.
        internal static string LogLine(string place, string objectPath, string message) =>
            (String.IsNullOrEmpty(place) ? "" : "[" + place + "] ") + (String.IsNullOrEmpty(objectPath) ? "" : objectPath + ": ") + message;
        // The name of a starter placement edit, with the track location it sets.
        internal static string StarterAction(string segment, string end, double distance) =>
            "Track location to " + segment + " / " + (end ?? "").ToLowerInvariant() + " / " + Number(distance);
        // The name of a binding edit, with the track location it sets.
        internal static string BindingAction(string segment, string end, double distance) =>
            "Track binding to " + segment + " / " + (end ?? "").ToLowerInvariant() + " / " + Number(distance);
        // Not "Inspection only": that names the outliner's group of objects no supported family covers.
        const string NotEditablePrefix = "Not editable: ";
        // The reason of such a text, or null for an object that can be edited.
        internal static string Refusal(string operations) => operations != null && operations.StartsWith(NotEditablePrefix, StringComparison.Ordinal) ? operations.Substring(NotEditablePrefix.Length) : null;
        // The outliner's four filters, All first and the rest by name. A native segment is as wide as its word plus 12
        // units of padding, and the segments stand side by side without spacing. In Montserrat-Medium 16 the words
        // measure 20.64 (All), 74.67 (Changed) and 77.88 (Problems) in the game; Disabled is 71.2 by the glyph advances
        // fitted to 168 measured labels, which agree with every measurement within 0.6. The four segments so need
        // 292.4 units. The outliner column is never narrower than that, so no word is cut.
        internal const string AllFilter = "All", ChangedFilter = "Changed", DisabledFilter = "Disabled", ProblemsFilter = "Problems";
        internal static readonly float[] FilterSegments = { 20.64f + 12, 74.67f + 12, 71.2f + 12, 77.88f + 12 };
        internal const float OutlinerWidth = 296;
        // Whether the tooltip callout is hidden now: the game is paused, and either the panel is open or the tooltip
        // was hidden already in this pause. It so stays hidden until the pause menu closes, whatever the panel does.
        internal static bool HidesTooltip(bool paused, bool panelOpen, bool hidden) => paused && (panelOpen || hidden);
        // Whether a text field of the panel has the keyboard for Escape's purpose: it had the focus in this frame or
        // in one of the two before it. The field itself takes Escape first and gives up its focus with it, so by the
        // time the panel is asked the field no longer reports the focus it had when the key went down.
        internal const int FocusFrames = 2;
        internal static bool FieldHasFocus(int frame, int lastFocused) => lastFocused >= 0 && frame - lastFocused <= FocusFrames;
        // What one press of Escape does, first that applies. The native map editor's Escape cancels its first armed
        // interaction, does nothing while a text field has the focus, and otherwise leaves Escape to the pause
        // menu; it never clears a selection. A list closes before an interaction is cancelled. The caller supplies
        // standing gizmo and prepared snap only while the pointer is over the panel.
        internal enum EscapeStep { None, CloseList, CancelDrag, CancelChoice, CancelGizmo, CancelSnap }
        internal static EscapeStep Escape(bool fieldFocused, bool listOpen, bool pending, bool dragging, bool gizmo, bool snap) =>
            fieldFocused ? EscapeStep.None
            : listOpen ? EscapeStep.CloseList
            : dragging ? EscapeStep.CancelDrag
            : pending ? EscapeStep.CancelChoice
            : gizmo ? EscapeStep.CancelGizmo
            : snap ? EscapeStep.CancelSnap
            : EscapeStep.None;
        // The supported edits of a group root of the Baked scenery group family.
        internal const string GroupOperations = "Move, rotate, enable, or disable the whole group. Its parts move with it.";
        internal const string Decorative = "Decorative. Moving or disabling this group changes no game function.";
        // The supported edits of a crossbuck of an unprotected crossing, a group of its own under Grade crossing, and
        // what the game uses it for: nothing reads it. The auto engineer's crossing horn reads the crossing's
        // Crossing-type pad marker, which keeps its own family.
        internal const string CrossbuckOperations = "Move, rotate, enable, or disable this crossbuck. The crossing pad, the whistle signs, and the other crossbuck stay in place.";
        internal const string CrossbuckUse = "Decorative. This crossbuck marks the crossing for road users, and the game doesn't read it. The auto engineer sounds the horn at the crossing's pad marker, which stays in place. Moving or disabling the crossbuck doesn't affect game functions.";
        // The supported edits of a part of a listed baked scenery group.
        internal const string PartOperations = "Move, rotate, enable, or disable this part. The group and its other parts stay unchanged. A fixed placement keeps this part at its map coordinates when the group moves.";
        internal const string PartDecorative = "Decorative. Moving or disabling this part does not affect game functions.";
        // What the Object tab says under Use in the game for a group root or part of the Baked scenery group family.
        // category is the category of the group, also for a part. storage names what a storage pile shows, null for
        // every object that is no pile. An industry loads and unloads on the track spans of the native map data, so
        // its decorative buildings and its storage display move without changing that; a pile shows its industry's
        // storage by the ids the game reads, wherever it stands.
        // holdsPiles: the group root or part holds storage piles below it.
        internal static string UseInGame(bool part, string category, string storage, bool holdsPiles) {
            if (storage != null) return "This pile shows the " + storage + ". Moving or disabling the pile doesn't change the storage. The industry keeps working on its native track spans.";
            // Only crossbucks stand under Grade crossing in the family.
            if (!part && category == BakedSceneryCategories.GradeCrossing) return CrossbuckUse;
            if (category != BakedSceneryCategories.IndustryBuilding && category != BakedSceneryCategories.StorageDisplay) return part ? PartDecorative : Decorative;
            return "Decorative. The industry loads and unloads on its native track spans, so moving or disabling this " + (part ? "part" : "group") + " doesn't affect loading or unloading." +
                (holdsPiles ? " Its piles keep showing the industry's storage." : "");
        }
        static object Member(object owner, string name) {
            if (owner == null) return null;
            for (var type = owner.GetType(); type != null; type = type.BaseType) {
                var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(owner);
            }
            throw new MissingFieldException(owner.GetType().FullName, name);
        }
        // The game logic of the buildings with pickable toggles (Railroader 0.6.3). A pickable toggle
        // (RollingStock.Controls.KeyValuePickableToggle) is clicked through a collider at or below its object, wherever
        // that stands, and flips one Boolean key of the nearest key-value object above it, which it looks up when it is
        // first used. A key-value object with a global id (RollingStock.Controls.GlobalKeyValueObject) registers under
        // that id while it is enabled, and the game save keeps its values by that id; disabled, it unregisters. What
        // reacts to the key, such as a lantern (Effects.LanternController) or the doors a KeyValueBoolAnimator opens,
        // stands on the toggle's object. Nothing of this reads where the objects stand.
        public const string PickableToggle = "RollingStock.Controls.KeyValuePickableToggle", LanternControl = "Effects.LanternController",
            KeyValueObject = "KeyValue.Runtime.KeyValueObject", GlobalKeyValueObject = "RollingStock.Controls.GlobalKeyValueObject", NameTextType = "TMPro.TextMeshPro";
        internal const string LanternSwitch = "Lantern switch", DoorToggle = "Door toggle", PlainToggle = "Toggle";
        // What a toggle switches, from the components on its own object.
        internal static string ToggleKind(IEnumerable<string> components) {
            var types = (components ?? Enumerable.Empty<string>()).ToArray();
            return types.Contains(LanternControl) ? LanternSwitch : BakedSceneryRules.Animates(types) ? DoorToggle : PlainToggle;
        }
        // One toggle on the object, by its kind and the key the game keeps its state by, as one binding line.
        const string TogglePrefix = "Toggle=", KeySeparator = ", key ";
        internal static string ToggleBinding(string kind, string key) => TogglePrefix + kind + (String.IsNullOrWhiteSpace(key) ? ", no key" : KeySeparator + key);
        // The key-value object on the object or below it outside other entries, by the global id the game saves its
        // values under, as one binding line.
        const string SavedStatesPrefix = "Saved states=", SavedIdPrefix = "id ", NoSavedId = "no id";
        internal static string KeyObjectBinding(string id) => SavedStatesPrefix + (String.IsNullOrWhiteSpace(id) ? NoSavedId : SavedIdPrefix + id);
        // How many key-value objects stand below the object in other entries, its parts: disabling the object disables
        // them too.
        const string SavedStatesBelowPrefix = "Saved states below=";
        internal static string KeyObjectsBelowBinding(int count) => SavedStatesBelowPrefix + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // How many toggles stand below the object in other entries, its parts.
        const string TogglesBelowPrefix = "Toggles below=";
        internal static string TogglesBelowBinding(int count) => TogglesBelowPrefix + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // The text of a name sign in a building with toggles, which the scene saves with its object.
        const string NameTextPrefix = "Name text=";
        internal static string NameTextBinding(string text) => NameTextPrefix + text;
        // The key a pickable toggle flips and the global id of a key-value object, as the game serializes them. A
        // component without the field fails the readout (MissingFieldException).
        internal static string ToggleKeyOf(object toggle) => Convert.ToString(Member(toggle ?? throw new ArgumentNullException(nameof(toggle)), "key"), System.Globalization.CultureInfo.InvariantCulture);
        internal static string GlobalIdOf(object globalObject) => Convert.ToString(Member(globalObject ?? throw new ArgumentNullException(nameof(globalObject)), "globalObjectId"), System.Globalization.CultureInfo.InvariantCulture);
        // The Use in the game text for the toggles, key-value object and name text an entry carries, or null when it
        // carries none of them. Each toggle is named by what it switches.
        static string Interactive(bool part, IReadOnlyList<string> lines) {
            var self = part ? "part" : "group";
            var said = new List<string>();
            foreach (var line in lines.Where(line => line.StartsWith(TogglePrefix, StringComparison.Ordinal))) {
                var body = line.Substring(TogglePrefix.Length);
                var at = body.IndexOf(KeySeparator, StringComparison.Ordinal);
                var kind = at < 0 ? body.Split(',')[0] : body.Substring(0, at);
                var key = at < 0 ? null : body.Substring(at + KeySeparator.Length);
                var (action, thing) = kind == LanternSwitch ? ("click the lantern in the game to switch it on or off", "the lantern")
                    : kind == DoorToggle ? ("click the doors in the game to open or close them", "the doors") : ("click it in the game to switch it", "the toggle");
                said.Add(kind + ": " + action + ". " + (key == null ? "It has no key, so the game doesn't keep its state." : "The game keeps its state by its key " + key + ", independently of its position. Moving this " + self + " preserves that state.") +
                    " While this " + self + " is disabled, you can't click " + thing + ".");
            }
            foreach (var line in lines.Where(line => line.StartsWith(SavedStatesPrefix, StringComparison.Ordinal))) {
                var value = line.Substring(SavedStatesPrefix.Length);
                said.Add(value.StartsWith(SavedIdPrefix, StringComparison.Ordinal)
                    ? "This " + self + " stores its toggles' states under the saved ID " + value.Substring(SavedIdPrefix.Length) + ". The game keeps them by this ID, independently of position. Saving the game while this " + self + " is disabled might lose those states."
                    : "This " + self + " holds its toggles' states. It has no saved ID, so the game doesn't save those states.");
            }
            if (lines.Any(line => line.StartsWith(SavedStatesBelowPrefix, StringComparison.Ordinal)))
                said.Add("Disabling this " + self + " also disables the object that holds its toggles' states. Saving the game while this " + self + " is disabled might lose those states.");
            foreach (var line in lines.Where(line => line.StartsWith(NameTextPrefix, StringComparison.Ordinal)))
                said.Add("The scene stores the name text \"" + line.Substring(NameTextPrefix.Length) + "\" with this " + self + ", independently of its position. The text moves with it.");
            var below = lines.Where(line => line.StartsWith(TogglesBelowPrefix, StringComparison.Ordinal)).Select(line => line.Substring(TogglesBelowPrefix.Length)).FirstOrDefault();
            if (below == "1") said.Add("Its toggle keeps its state by its key, independently of its position, and works at its new location.");
            else if (below != null) said.Add("Its " + below + " toggles keep their states by their keys, independently of their positions, and work at their new locations.");
            return said.Count == 0 ? null : String.Join(" ", said);
        }
        // The Use in the game text for an entry of the Baked scenery group family: for a part, its group's entry gives
        // the category; group is null for a group root or a part whose group is not in the inventory.
        internal static string UseInGame(SceneObservation selected, SceneObservation group) {
            var isPart = selected.GroupPath != null;
            var use = UseInGame(isPart, isPart ? group?.Category : selected.Category, selected.Storage, selected.Bindings);
            return GroundMaskUse(use, isPart, selected.Dependencies, (isPart ? group?.Subcategory : selected.Subcategory) == BakedSceneryCategories.RoadBridges);
        }
        // A road bridge construction is decorative, and its road is native map data: moving the bridge does not move
        // the road.
        internal const string RoadBridgeDecorative = "The bridge is decorative. Its road is native map data that the Map Editor edits.";
        // An object with a scene ground mask at or below it is not only decorative: the mask shapes the terrain where it
        // stands. The part that holds the mask says what the mask does; an object above it adds that to its own text.
        // An object whose mask flattens the terrain height cannot be moved, so its text names disabling alone.
        internal const string GroundMaskHolderUse = "Ground mask: it shapes the ground and the vegetation where this part stands. Moving or disabling this part changes the terrain there.";
        // roadBridge: the object is a road bridge construction or a part of one; the group root then names the bridge
        // and its native road.
        internal static string GroundMaskUse(string use, bool part, IEnumerable<string> dependencies, bool roadBridge = false) {
            var line = dependencies?.FirstOrDefault(dependency => dependency != null && dependency.StartsWith(GroundMaskPrefix, StringComparison.Ordinal));
            if (roadBridge && !part && use == Decorative) {
                if (line == null) return RoadBridgeDecorative + " Moving or disabling this group changes no game function.";
                return RoadBridgeDecorative + " The bridge's ground mask shapes the ground and the vegetation where it stands, so " + (line.EndsWith(HeightUnconfirmed, StringComparison.Ordinal) ? "disabling" : "moving or disabling") + " this group changes the terrain there.";
            }
            if (line == null) return use;
            var edits = line.EndsWith(HeightUnconfirmed, StringComparison.Ordinal) ? "disabling" : "moving or disabling";
            if (line.StartsWith(GroundMaskPrefix + "this part", StringComparison.Ordinal)) return edits == "disabling" ? GroundMaskHolderUse.Replace("Moving or disabling", "Disabling") : GroundMaskHolderUse;
            var shaping = "Its ground mask shapes the ground and the vegetation where it stands, so " + edits + " this " + (part ? "part" : "group") + " changes the terrain there.";
            return use == Decorative || use == PartDecorative ? "The building is decorative. " + shaping : use + " " + shaping;
        }
        // The ids by which a pile's or log pond's storage synchronizer reads the storage it shows, as one binding line.
        const string StoragePrefix = "Industry storage=";
        internal static string StorageBinding(string industryComponentId, string loadId) => StoragePrefix + industryComponentId + ", load " + loadId;
        // How many storage piles stand at or below a group root or part, as one binding line.
        const string PilesPrefix = "Storage piles=";
        internal static string PilesBinding(int count) => PilesPrefix + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        internal static bool HoldsPiles(IEnumerable<string> bindings) => bindings?.Any(line => line != null && line.StartsWith(PilesPrefix, StringComparison.Ordinal)) == true;
        // The binding lines of a log pond (Scenery.LogPond) and of a group root or part above one. self: the object is
        // the log pond; below: how many log ponds stand below it; storage: the log pond's storage binding, or null when
        // no loaded synchronizer names it. The log pond carries a fixed line of its own and, as a pile does, its storage
        // binding only while the storage is known; an object above it carries only the count. Every line stays the
        // same while the scene stays, so the Object tab's choice never makes the inventory stale.
        const string LogPondSelf = "Log pond=this object", LogPondsBelowPrefix = "Log ponds below=";
        internal static IReadOnlyList<string> LogPondBindings(bool self, int below, string storage) {
            var lines = new List<string>();
            if (self) { lines.Add(LogPondSelf); if (storage != null) lines.Add(storage); }
            else if (below > 0) lines.Add(LogPondsBelowPrefix + below.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return lines;
        }
        // What the Object tab says under Use in the game for a group root or part of the Baked scenery group family,
        // chosen from its bindings alone: the log pond itself, an object above a log pond, and otherwise the text of
        // UseInGame above, with piles as #98 has them. storage names what the pile or log pond shows, or null.
        internal static string UseInGame(bool part, string category, string storage, IEnumerable<string> bindings) {
            var lines = (bindings ?? Enumerable.Empty<string>()).Where(line => line != null).ToArray();
            var shown = storage ?? "industry's storage";
            var segments = ConstructionSegments(lines);
            if (segments.Count != 0)
                return "The construction state of track segment" + (segments.Count == 1 ? " " : "s ") + Prose.Join(segments) + " decides which parts of this bridge the game shows.";
            var interactive = Interactive(part, lines);
            if (interactive != null) return interactive;
            if (lines.Contains(LogPondSelf))
                return "This log pond shows the " + shown + ". Moving or disabling the log pond doesn't change the storage. The industry keeps working on its native track spans.";
            if (lines.Any(line => line.StartsWith(LogPondsBelowPrefix, StringComparison.Ordinal)))
                return "Decorative. Its log pond shows the " + shown + ". Moving or disabling this " + (part ? "part" : "group") + " doesn't change the storage. The industry keeps working on its native track spans.";
            return UseInGame(part, category, lines.Any(line => line.StartsWith(StoragePrefix, StringComparison.Ordinal)) ? storage : null, HoldsPiles(lines));
        }
        // A rail bridge's construction controller (Game.Mow.TrackConstructionScenery) shows or hides parts of the bridge by
        // the construction state of the track segments it names by id, wherever the bridge stands. Each segment is one
        // binding line of the group root and of every part below the controller, for the Connections tab. A segment that
        // no longer exists in the track graph keeps its id with a remark. Since #96 the Connections tab of the group root
        // repairs it, so the group root also reports it as a problem (ConstructionSegmentProblems), as the Ela controller
        // does. exists is null when the track graph could not be read.
        const string SegmentPrefix = "Construction segment=", MissingSegment = "; no longer exists";
        internal static string SegmentBinding(string id, bool? exists) => SegmentPrefix + (id ?? "") + (exists == false ? MissingSegment : "");
        internal static IReadOnlyList<string> ConstructionSegments(IEnumerable<string> bindings) =>
            (bindings ?? Enumerable.Empty<string>()).Where(line => line != null && line.StartsWith(SegmentPrefix, StringComparison.Ordinal))
                .Select(line => { var id = line.Substring(SegmentPrefix.Length); return id.EndsWith(MissingSegment, StringComparison.Ordinal) ? id.Substring(0, id.Length - MissingSegment.Length) : id; }).ToArray();
        // The track segments that an observation's binding lines name, each a current binding the world marks green while
        // the Connections tab is open: a marker's segment, a span's lower and upper, and each construction segment of a
        // rail bridge construction or of the Ela controller, its remark left off. A binding slot does not offer its own
        // current binding as a candidate (BindingSlotSegments, a coverage role's segment, a construction's segments).
        static readonly string[] BoundPrefixes = { "Marker segment=", "Lower segment=", "Upper segment=", SegmentPrefix, "Construction scenery segment=" };
        // The current binding of a marker's binding slot, a crossing pad's included: its marker segment, or nothing for
        // a detached marker. Segment on the slot does not offer it; a new distance on it is typed.
        internal static IReadOnlyList<string> BindingSlotSegments(IEnumerable<string> bindings) =>
            BoundSegments((bindings ?? Enumerable.Empty<string>()).Where(line => line != null && line.StartsWith(BoundPrefixes[0], StringComparison.Ordinal)))
                .Where(id => id.Length != 0).ToArray();
        internal static IReadOnlyList<string> BoundSegments(IEnumerable<string> bindings) {
            var found = new List<string>();
            foreach (var line in bindings ?? Enumerable.Empty<string>()) {
                var prefix = line == null ? null : BoundPrefixes.FirstOrDefault(p => line.StartsWith(p, StringComparison.Ordinal));
                if (prefix == null) continue;
                var value = line.Substring(prefix.Length);
                var end = value.IndexOf(';');
                found.Add((end < 0 ? value : value.Substring(0, end)).Trim());
            }
            return found;
        }
        // A construction segment that no longer exists, or an empty segment ID, is a defect the modder repairs on the
        // Connections tab of the construction (#96): one problem per such segment, worded alike for a rail bridge group
        // root and the Ela controller; a segment the controller lists twice is one problem. known is null when the track
        // graph could not be read: then the segments that could not be checked give one inspection note
        // (BindingRules.IsNote), and an empty segment ID stays a problem. ids is null without a controller.
        const string ConstructionRecovery = " On the Connections tab, select the segments of the bridge's track and click Set.";
        internal static IReadOnlyList<string> ConstructionSegmentProblems(IEnumerable<string> ids, ICollection<string> known, string graphError) {
            var all = (ids ?? Enumerable.Empty<string>()).ToArray();
            if (all.Length == 0) return Array.Empty<string>();
            var problems = all.Where(id => String.IsNullOrWhiteSpace(id) || (known != null && !known.Contains(id)))
                .Select(id => (String.IsNullOrWhiteSpace(id) ? "A construction segment has no ID." : "Construction segment " + id + " no longer exists.") + ConstructionRecovery);
            return (known == null ? problems.Concat(new[] { "Construction segment validity unknown: " + graphError }) : problems).Distinct(StringComparer.Ordinal).ToArray();
        }
        // The construction segment problems of an inventory entry, only where the patcher can repair them: the group root
        // of an editable rail bridge group and the editable Ela controller, by the structure the entry is observed with. A
        // part, a refused object or one that shares its path (no structure) shows the remark without a problem.
        internal static IReadOnlyList<string> RepairableConstructionProblems(string structure, IEnumerable<string> ids, ICollection<string> known, string graphError) =>
            structure == BakedSceneryGroups.Structure || structure == ConstructionSceneryScene.Structure ? ConstructionSegmentProblems(ids, known, graphError) : Array.Empty<string>();
        // The repair a problem offers on the Object tab: a construction segment problem opens the Connections tab of the
        // construction, where its segments are set; no guided task sets construction segments. Another problem that
        // concerns a segment, a binding or a track location offers the guided track binding task.
        internal const string RepairConstructionSegments = "Repair construction segments";
        internal const string RepairConstructionSegmentsTip = "Open the Connections tab to select the segments of the bridge's track.";
        static bool ConstructionProblem(string problem) => problem != null && problem.EndsWith(ConstructionRecovery, StringComparison.Ordinal);
        internal static bool OffersConstructionRepair(IEnumerable<string> problems) => (problems ?? Enumerable.Empty<string>()).Any(ConstructionProblem);
        internal static bool OffersBindingRepair(IEnumerable<string> problems) => (problems ?? Enumerable.Empty<string>()).Any(problem => problem != null && !ConstructionProblem(problem)
            && (problem.IndexOf("segment", StringComparison.OrdinalIgnoreCase) >= 0 || problem.IndexOf("binding", StringComparison.OrdinalIgnoreCase) >= 0 || problem.IndexOf("track location", StringComparison.OrdinalIgnoreCase) >= 0));
        internal const string ConstructionDependency = "Construction: the game shows or hides parts of this bridge by the construction state of its track segments.";
        // The binding lines of a construction controller's segment ids, checked against the segment ids the track graph
        // holds; known is null when the track graph could not be read. An empty id is a segment that does not exist.
        internal static IReadOnlyList<string> ConstructionBindings(IEnumerable<string> ids, ICollection<string> known) =>
            (ids ?? Enumerable.Empty<string>()).Select(id => SegmentBinding(id, known == null ? (bool?)null : !String.IsNullOrWhiteSpace(id) && known.Contains(id))).ToArray();
        // The construction segment row of the Connections tab: the segment ids of a rail bridge construction, typed or
        // picked in the world, as one field. Ids are separated by commas or spaces.
        internal static IReadOnlyList<string> SegmentIds(string text) => (text ?? "").Split(new[] { ',', ' ', '	' }, StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).Where(id => id.Length != 0).ToArray();
        // A set of segments as the field shows it, in ordinal order.
        internal static string SegmentText(IEnumerable<string> ids) => String.Join(", ", (ids ?? Enumerable.Empty<string>()).OrderBy(id => id, StringComparer.Ordinal));
        // The segments that the binding lines mark as no longer existing in the track graph, their remark left off.
        internal static IReadOnlyList<string> MissingSegments(IEnumerable<string> bindings) =>
            (bindings ?? Enumerable.Empty<string>()).Where(line => line != null && line.EndsWith(MissingSegment, StringComparison.Ordinal))
                .Select(line => { var prefix = BoundPrefixes.FirstOrDefault(p => line.StartsWith(p, StringComparison.Ordinal)); return prefix == null ? null : line.Substring(prefix.Length, line.Length - prefix.Length - MissingSegment.Length).Trim(); })
                .Where(id => !String.IsNullOrEmpty(id)).ToArray();
        // Picked adds the confirmed segment to the field once. A segment that no longer exists is the one the pick replaces:
        // it leaves the field, and the other segments stay.
        internal static string AddSegment(string text, string id, IEnumerable<string> missing = null) {
            var gone = new HashSet<string>(missing ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var ids = SegmentIds(text).Where(existing => !gone.Contains(existing)).ToList();
            if (!String.IsNullOrWhiteSpace(id) && !ids.Contains(id.Trim(), StringComparer.Ordinal)) ids.Add(id.Trim());
            return String.Join(", ", ids);
        }
        // Why Set is unavailable: the general reason first, then an empty field or the construction's current set.
        internal static string SegmentsReason(string block, string text, IReadOnlyList<string> current) {
            if (block != null) return block;
            var ids = SegmentIds(text);
            if (ids.Count == 0) return "Enter at least one segment ID.";
            return current != null && new HashSet<string>(ids, StringComparer.Ordinal).SetEquals(current) ? "The field already lists the construction's current segments." : null;
        }
        internal const string SegmentDetachReason = "The construction needs track segments. Select the segments of the bridge's track instead.";
        internal static string ClearSegmentsReason(bool hasValue) => hasValue ? null : "This mod sets no construction segments for this object, so there is nothing to clear.";
        // The Marker ID row of a track marker's Connections tab. Why the replacement ID is unavailable for the
        // inspected marker, or null when it is offered: only a generic track marker whose native id another scene marker
        // shares takes one, and a marker that already has one keeps the row to change or clear it.
        internal const string SetMarkerId = "Set ID", ClearMarkerId = "Clear ID", RepairDuplicateId = "Repair duplicate ID";
        // The Inspector route of the same repair, beside Repair duplicate ID: the Marker ID section of the Connections tab.
        internal const string SetIdOnConnections = "Set ID on Connections tab";
        internal const string SetIdOnConnectionsTip = "Open the Connections tab. In its Marker ID section, enter a replacement ID and click Set ID.";
        // Whether the Object tab offers Repair duplicate ID: for a generic track marker whose explanation names a
        // duplicate marker ID, active or not.
        internal static bool OffersDuplicateRepair(string structure, IEnumerable<string> dependencies) =>
            structure == TrackMarkerScene.Structure && dependencies?.Any(line => line != null && line.StartsWith("Duplicate marker ID ", StringComparison.Ordinal)) == true;
        // What the Detected problems section says when the object has no problem. Beside Repair duplicate ID the holder
        // is inactive, which is never a problem, so the section says so and what the duplicate marker ID still refuses.
        internal static string NoDetectedProblems(bool offersDuplicateRepair) => offersDuplicateRepair
            ? "None while this marker is inactive. The patcher refuses changes to its activity, binding, and track-following placement until its duplicate marker ID is repaired."
            : "None detected";
        // The tooltip of the disclosure triangle after the Dependencies and Runtime state headings.
        internal static string DisclosureTip(bool longForm) => longForm
            ? "Show the short form, with paths relative to this object and grouped per object."
            : "Show the full lines, with each component's scene and full path.";
        // The Detected problems section of a stale object list: its problems are those of the last refresh.
        internal const string StaleProblems = "The object list is stale, so these problems might be out of date. Click Refresh to check them again.";
        // A detected problem as the Object tab shows it. On a marker whose replacement ID repaired a duplicate marker ID, a
        // track binding problem that remains says that the duplicate is repaired and that this problem is another one,
        // with the two routes that repair it. repairedId is the duplicate marker ID the replacement ID repaired, or null.
        internal static string ProblemText(string problem, string repairedId) =>
            repairedId != null && problem != null && problem.StartsWith("Marker ", StringComparison.Ordinal) && (problem.IndexOf("missing segment", StringComparison.Ordinal) >= 0 || problem.IndexOf("empty segment binding", StringComparison.Ordinal) >= 0)
                ? problem + ". Duplicate marker ID " + repairedId + " is repaired. This is a different problem: the track binding " + (problem.IndexOf("missing segment", StringComparison.Ordinal) >= 0 ? "names a segment that doesn't exist" : "is empty")
                    + ". Click Repair with guided task, or set an existing track on the Connections tab."
                : problem;
        // What to enter in a Replacement ID field, on the Connections tab and in the guided task, shown as text beside the
        // field: a new ID that the modder makes up, which is neither an existing marker ID nor a segment ID.
        internal const string ReplacementIdInstruction = "Make up a new ID that nothing in the world uses yet. Don't enter another marker's ID or a segment ID. The ID can't be empty, have a space at either end, or begin with passengerStop: or flare:.";
        // The tooltip of a Replacement ID field: what the ID does, beside the instruction that stands below the field.
        internal const string ReplacementIdTip = "The game registers this marker's track location under this ID. A replacement ID repairs a duplicate marker ID. The patcher doesn't check Lua scripts or other mods that look up markers by ID.";
        internal static string MarkerIdUnavailable(string family, bool generic, string underlying, int sharers, bool hasValue) =>
            !generic ? ReplacementMarkerIds.NotSupportedFor(family)
            : hasValue ? null
            : underlying == null ? "The marker ID could not be read."
            : sharers == 0 ? "Only this marker holds marker ID " + underlying + ", so it doesn't need a replacement ID." : null;
        // Why Set ID is unavailable: the general reason first, then an empty field, a reserved prefix, a space at either end
        // or the marker's current ID.
        internal static string MarkerIdSetReason(string block, string text, string current) {
            if (block != null) return block;
            var problem = ReplacementMarkerIds.Problem(text);
            if (problem != null) return Sentence(problem);
            return text == current ? "The field already contains the marker's current ID." : null;
        }
        internal static string ClearMarkerIdReason(bool hasValue) => hasValue ? null : "This mod sets no replacement ID for this marker, so there is nothing to clear.";
        // Why Clear ID is unavailable: the general reason first, then nothing to clear.
        internal static string ClearMarkerIdReason(string block, bool hasValue) => block ?? ClearMarkerIdReason(hasValue);
        // The caption above the other holders of the marker's underlying ID: while the marker still holds that ID it
        // shares it with them; after a replacement it no longer does.
        internal static string SharersCaption(string underlying, string current) =>
            current == underlying ? "Also holding " + underlying : "Still holding " + underlying + ", the underlying ID";
        // A construction controller on an object of its own, Ela Bridge Scenery: its construction segments are its only edit.
        internal const string ConstructionSceneryFamily = "Bridge construction scenery";
        internal const string ConstructionSceneryOperations = "Set this controller's construction segments. Its position has no effect in the game. It stays enabled because it controls which parts of the bridge the game shows.";
        internal const string ControllerNoPlacement = ConstructionSceneryScene.NoPlacement, ControllerNoActivity = ConstructionSceneryScene.NoActivity;
        // The amber warning for an object that an explicit disable holds off against its construction controller, on the
        // Object tab and in the footer's message area.
        internal const string ConstructionHoldWarning = GameSwitchActivity.ConstructionHoldWarning;
        // The Enabled readout of an object that no explicit disable holds off while progression or a map feature
        // switches it off now.
        internal const string SwitchedOffByGame = "Yes, but currently switched off by the game";
        // The dependency an object with a scene ground mask names. self: the object holds the one mask itself; otherwise
        // holders names the parts at or below it that hold a mask. A mask that also flattens the terrain height keeps
        // the object in place.
        const string GroupRootHolds = "the root of this group holds the scene ground mask";
        const string GroundMaskPrefix = "Ground mask: ", HeightUnconfirmed = "can't be moved because the game might not restore the terrain height after a move.";
        // parts: the holders are parts of the object's family; below a marker object they are plain objects.
        // group: the object that holds the mask itself is a group root, as a road bridge is, not a part.
        internal static string GroundMaskDependency(bool self, IEnumerable<string> holders, bool flattens, bool parts = true, bool group = false) {
            var names = (holders ?? Enumerable.Empty<string>()).ToArray();
            var several = !self && names.Length > 1;
            var kind = parts ? "part" : "object";
            var who = self ? (group ? GroupRootHolds : "this part holds the scene ground mask") : several ? "the " + kind + "s " + Prose.Join(names) + " hold scene ground masks" : "the " + kind + " " + names.FirstOrDefault() + " holds the scene ground mask";
            if (flattens) return GroundMaskPrefix + who + (several ? ", and one or more also flatten the terrain height. " : ", which also flattens the terrain height. ") + (!self ? "This object " : group ? "The group " : "It ") + HeightUnconfirmed;
            return GroundMaskPrefix + who + ". Moving " + (!self ? "this object moves " + (several ? "those " + kind + "s" : "that " + kind) + " and the terrain shaping with " + (several ? "them" : "it") : group ? "the group moves the terrain shaping" : "it moves the terrain shaping") +
                ". The patcher asks the game to regenerate terrain and vegetation at the old and new positions.";
        }
        // Whether the Placement tab shows Numeric whatever mode was open: a pad, an object that cannot be edited, a
        // construction controller, and an object whose every move is refused have no use for the other modes.
        // The tooltip of a placement mode: its own block, or for Numeric, which stays open to show the placement, the block
        // of its fields; otherwise its description.
        internal static string ModeTip(int mode, string description, string modeBlock, string fieldBlock) => Tooltip(description, modeBlock ?? (mode == NumericMode ? fieldBlock : null));
        // The tooltip of a value row that stands as a running line because it cannot be edited.
        internal static string BlockedValueTip(string block) => Tooltip(null, block);
        // Why Apply and Read of Numeric are inactive: the block of the placement for both, then, for Apply, the fields.
        internal static (string Apply, string Read) NumericActionReasons(string block, string fieldReason) => (block ?? fieldReason, block);
        // Supported edits of an object whose move a ground mask blocks: only what still applies.
        internal const string MarkerOperations = "Set, replace, or detach the marker's track binding. Edit placement and activity separately.";
        internal static string OperationsWithoutMove(string operations) =>
            operations == GroupOperations ? "Enable or disable the whole group."
            : operations == PartOperations ? "Enable or disable this part. The group and its other parts stay unchanged."
            : operations == MarkerOperations ? "Set, replace, or detach the marker's track binding. Edit activity separately."
            : operations;
        internal static bool ForcesNumeric(bool pad, bool editable, bool controller, string moveBlock) => pad || !editable || controller || moveBlock != null;
        // Why the placement actions of the tab, Read and Apply included, are unavailable, or null: the first block that holds.
        // Whether an object observed with this structure is edited in the inspector: every capability's structure but a
        // starter placement marker's, which has its own workflow. The Ela construction controller counts: its construction
        // segments are its edit, and its placement and activity give their own reasons.
        static readonly HashSet<string> EditableStructures = new HashSet<string>(StringComparer.Ordinal) {
            IndependentBoards.Structure, BridgeScene.Structure, TrackMarkerScene.Structure, MilepostScene.Structure, CrossingCoverageScene.Structure, CrossingPadScene.Structure,
            BakedSceneryGroups.Structure, BakedSceneryParts.Structure, StationMapIconRules.Structure, SpawnPoints.Structure,
            ExistingEquipmentScene.MastStructure, ExistingEquipmentScene.BellStructure, ConstructionSceneryScene.Structure };
        internal static bool EditableStructure(string structure) => structure != null && EditableStructures.Contains(structure);
        internal static string PlacementBlock(string edit, string pad, string controller, string moveBlock) => edit ?? pad ?? controller ?? moveBlock;
        // Why every move of the object is refused, or null: its dependency line names a mask that flattens the terrain
        // height. The placement modes and fields are then unavailable with this reason.
        internal const string FlatteningMoveBlock = "This object has a ground mask that flattens the terrain height. The game might not restore that height after a move.";
        internal static string GroundMaskMoveBlock(IEnumerable<string> dependencies) =>
            dependencies?.Any(line => line != null && line.StartsWith(GroundMaskPrefix, StringComparison.Ordinal) && line.EndsWith(HeightUnconfirmed, StringComparison.Ordinal)) == true ? FlatteningMoveBlock : null;
        // Whether the object has terrain output of a scene ground mask, by its dependency line.
        internal static bool HoldsGroundMask(IEnumerable<string> dependencies) => dependencies?.Any(line => line != null && line.StartsWith(GroundMaskPrefix, StringComparison.Ordinal)) == true;
        // The dependency a lake surface's inspection names: what moves with it, what stays native, and whether its water
        // is part of a static batch, which the game would keep drawing at its old place.
        internal static string LakeSurfaceDependency(bool batched) => batched
            ? "Lake surface: the water surface is part of a static batch, so it can't be moved. Native lake masks and rivers shape and color the terrain. Edit them in the native map editor."
            : "Lake surface: the water surface moves with this object. Native lake masks and rivers shape and color the terrain. Edit them in the native map editor.";
        // The storage a pile shows: the industry's load storage by the names the game shows, the load in lower case
        // inside the sentence unless it is an acronym, or by the ids its storage synchronizer reads when the industry
        // or load is not known.
        internal static string StorageName(string industry, string load, string industryComponentId, string loadId) =>
            !String.IsNullOrWhiteSpace(industry) && !String.IsNullOrWhiteSpace(load) ? Lower(load) + " storage of " + industry : "storage of load " + loadId + " at industry component " + industryComponentId;
        static string Lower(string word) => word.Length > 1 && Char.IsUpper(word[1]) ? word : Char.ToLowerInvariant(word[0]) + word.Substring(1);
        // The reason an object is inspection only: its kind has no edit yet. kind names it, such as "the Milepost family".
        internal static string NotYetSupported(string kind) => NotYetSupportedPrefix + kind + " cannot be edited";
        // Why a baked scenery group root is inspection only, or null for a listed group. A group left out under a
        // settled rule, whose reason is a whole sentence, such as a group that holds track markers, is not editable as a
        // group and promises nothing later; a group whose reason names a kind waits for support.
        internal static string GroupInspectionOnly(GroupRootVerdict verdict, string family) {
            if (verdict?.Disposition == GroupDisposition.Group) return null;
            if (verdict?.ReasonIsSentence == true) return NotEditableAsGroupPrefix + verdict.Reason;
            return NotYetSupported(verdict?.Reason ?? "this " + family.ToLowerInvariant());
        }
        // The families of a light or gate and of a bell this patcher added: supported families whose edits live on their
        // crossing's Connections tab, not on the object itself.
        internal const string AddedEquipmentFamily = "Added light or gate", AddedBellFamily = "Added bell";
        // The guide link offered below a list of equipment sources: its label and the guide page with the anchor of its
        // eligibility reasons, or null while every source of the list is eligible. A list of bell definitions links to the bell
        // guide, a list of devices to the light and gate guide.
        internal static string[] RejectionGuide(IEnumerable<SourceCandidate> sources) {
            var rejected = (sources ?? Enumerable.Empty<SourceCandidate>()).Where(s => s != null && !s.Eligible).ToList();
            if (rejected.Count == 0) return null;
            return rejected.Any(s => s.Source.IsDefinition)
                ? new[] { "Why a bell definition isn't eligible", "adding-crossing-bells.md#ineligible-bell-definitions" }
                : new[] { "Why a device isn't eligible", "adding-crossing-equipment.md#ineligible-sources" };
        }
        internal static bool IsAddedFamily(string family) => family == AddedEquipmentFamily || family == AddedBellFamily;
        // Where added equipment is edited, one sentence for its supported edits and for why it cannot be edited as an
        // object of its own.
        internal const string AddedEquipmentReason = "To edit or remove this added equipment, use its crossing controller's Connections tab.";
        internal static string AddedEquipmentOperations(string name) => name + ". " + AddedEquipmentReason;
        // Why an object of the family is inspection only, or null when a supported operation covers it: a supported kind,
        // or an added light, gate or bell, which its crossing edits.
        internal static string InspectionOnly(string family, bool supportedKind) => supportedKind || IsAddedFamily(family) || family == ConstructionSceneryFamily ? null : NotYetSupported("Objects listed under " + family);
        // The track element an object follows, as the world marks it: the anchor's node, or the segment of its
        // location. Null without a following placement.
        internal static SelectionIdentity Followed(TrackFollowing following) =>
            following == null ? null
            : following.Anchor.Node != null ? SelectionIdentity.Graph(SelectionKind.TrackNode, following.Anchor.Node)
            : SelectionIdentity.Graph(SelectionKind.TrackSegment, following.Anchor.Location.Segment);
        // A group of named values, such as a position, as the native inspectors show one: the group's caption above
        // the row, each value's short label in front of its box. The unit stands once: in the caption while the row
        // can be edited, at the end of the running line while it cannot.
        internal static readonly string[] PlacementGroups = { "Position", "Rotation" }, PlacementUnits = { "m", "deg" }, Axes = { "X", "Y", "Z" };
        internal static string GroupCaption(string caption, string unit, bool editable) => editable && unit != null ? caption + " (" + unit + ")" : caption;
        // Whether each value of a group has a short label in front of its box: the one value of a group has only the
        // group's caption.
        internal static bool HasBoxLabels(IReadOnlyList<string> labels) => labels.Count > 1;
        // The values of a group that cannot be edited now, as one running line: "X 4361.091  Y 528.405  Z 5380.431 (m)".
        // A value that is not set reads as a dash.
        internal static string RunningLine(IReadOnlyList<string> labels, IReadOnlyList<string> values, string unit) =>
            String.Join("  ", labels.Select((label, i) => (HasBoxLabels(labels) ? label + " " : "") + (i < values.Count && !String.IsNullOrWhiteSpace(values[i]) ? values[i].Trim() : "-"))) + (unit == null ? "" : " (" + unit + ")");
        // Why the inspected object cannot be edited, as the yellow message and every "Unavailable:" tooltip give it:
        // the object's own reason, which the Object tab shows under Supported edits, and not a pointer to that tab. It is
        // one plain sentence: the message's source and the tooltip's Unavailable: are its only label.
        internal static string EditReason(string operations) {
            var own = operations?.Trim();
            if (own != null && own.EndsWith(". " + AddedEquipmentReason, StringComparison.Ordinal)) return AddedEquipmentReason;
            if (String.IsNullOrEmpty(own) || !(own.StartsWith(NotEditablePrefix, StringComparison.Ordinal) || own.StartsWith(NotYetSupportedPrefix, StringComparison.Ordinal) || own.StartsWith(NotEditableAsGroupPrefix, StringComparison.Ordinal))) return "This object cannot be edited here.";
            return Sentence(own);
        }
        const string NotYetSupportedPrefix = "Not yet supported: ", NotEditableAsGroupPrefix = "Not editable as a group: ";
        // The Inspection only line of a row tooltip and of the state line: one label, and the reason as one plain sentence.
        internal static string InspectionOnlyLine(string reason) => "Inspection only: " + Sentence(reason);
        // Every number the panel shows or fills a field with: metres and degrees to three decimals, without trailing
        // zeros, never in scientific notation, and no negative zero. A value below half a thousandth reads 0.
        internal static string Number(double value) {
            var text = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return text == "-0" ? "0" : text;
        }
        // The value an edit takes from a field: the object's own exact value while the field still shows it, so a
        // field that was not touched changes nothing by its rounding; the entered number otherwise.
        internal static bool Entered(string text, double own, out double value) {
            var entered = (text ?? "").Trim();
            if (entered == Number(own)) { value = own; return true; }
            return Double.TryParse(entered, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }
        // A number inside a binding line, such as a distance, as the panel shows it; other text stays as it is.
        // The end of a track location reads in lower case everywhere, as the scene patch file writes it: "a" or "b".
        internal static string Shown(string key, string value) =>
            key == "end" ? (value ?? "").ToLowerInvariant() :
            (key == "distance" || key == "from" || key == "to") && Double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? Number(number) : value;
        // A click on a shown square marker reaches that marker before the label of another candidate that lies over
        // it; a click on no marker goes to the label under the pointer.
        internal static bool MarkerBeforeLabel(bool onShownMarker) => onShownMarker;
        // The order in which world markings are drawn on one spot: a neutral marker first, a hovered, clicked or
        // related one over it, the one a row points at last.
        internal static int DrawRank(bool detailed, bool strong) => strong ? 2 : detailed ? 1 : 0;
        // The opacity of the fill under the hovered row of the list beside the click.
        internal const float RowFill = .45f;
        // The label opacity of an inactive button, as the native segmented control dims an inactive segment.
        internal const float InactiveAlpha = .4f;
        // The log line of a world choice's problem: the problem, and the candidates of the clicked spot when there
        // are several, each by what tells it apart. The object the line names is the one the choice is for, when it
        // is for one; a choice that only inspects names none.
        internal static string ChoiceLog(string forObject, string problem, IReadOnlyList<string> offers) =>
            LogLine("World choice", forObject, problem + (offers == null || offers.Count < 2 ? "" : " Candidates: " + String.Join("; ", offers.Select((label, i) => (i + 1) + " " + ChoiceLabel(label, offers, 80)))));
        // The caption of a coverage role, in words: the scene patch file's "approachA" reads "Approach A".
        internal static string RoleCaption(string role) => CircuitCoverage.RoleCaption(role);
        // Why a placement mode is inactive for the inspected object, or null. A crossing pad has its own reason for
        // every mode; a milepost has one for Follow. An object that cannot be edited keeps Numeric, which shows its
        // placement, and has every other mode inactive with the object's own reason.
        // An existing crossing mast or bell has one for Follow as well. One that does not conform to its pattern has
        // every mode inactive with its refusal, Numeric included: its placement fields stay visible under it.
        // moveBlock: why every move of the object is refused, such as a ground mask that flattens the terrain height;
        // Numeric then stays open to show the placement, with its fields unavailable for the same reason.
        internal static string ModeBlock(int mode, bool pad, bool editable, string operations, bool milepost = false, bool equipment = false, bool railBridge = false, bool controller = false, string moveBlock = null, bool roadBridge = false) =>
            controller ? (mode == NumericMode ? null : ControllerNoPlacement) : pad ? PadModeBlock(mode) : !editable && (mode != NumericMode || equipment) ? EditReason(operations) : moveBlock != null && mode != NumericMode ? moveBlock
            : milepost && mode == FollowMode ? MilepostNoFollow : equipment && mode == FollowMode ? EquipmentNoFollow
            : railBridge && mode == FollowMode ? RailBridgeNoFollow : roadBridge && mode == FollowMode ? RoadBridgeNoFollow : null;
        // A road bridge construction does not follow track: move the road natively, then the bridge onto it.
        internal const string RoadBridgeNoFollow = RailroaderScenePatcher.Core.BakedSceneryRules.RoadBridgeNoFollowing + " Move the road in the native map editor, then move the bridge onto it.";
        // A rail bridge construction does not follow its track: move the track natively, then the construction onto it.
        internal const string RailBridgeNoFollow = RailroaderScenePatcher.Core.BakedSceneryRules.RailBridgeNoFollowing + " Move the track in the native map editor, then move the bridge onto it.";
        // Why Follow is inactive for an existing crossing mast or bell: the capability covers fixed placement and activity.
        internal const string EquipmentNoFollow = ExistingEquipmentScene.NoFollowing + ".";
        // Why the rotation of an existing crossing bell cannot be set, or null for any other object: the bell is a
        // sound source at a point. Its position stays editable.
        internal const string BellNoRotation = "A bell's sound has no direction, so rotation does not apply to it. You can still set its position.";
        internal static string RotationBlock(string structure) => structure == ExistingEquipmentScene.BellStructure ? BellNoRotation : null;
        // Why a track span is inspection only, or null: a detection span, which a crossing controller holds in a
        // circuit, is a supported kind whose edit is crossing coverage on its controller; any other span has no edit.
        internal static string SpanInspectionOnly(bool controllerHolds) => InspectionOnly("Track span", controllerHolds);
        // What an existing crossing mast's Supported edits says: the light and its gate as one, and what stays unchanged.
        internal const string MastOperations = "Move, rotate, enable, or disable this mast. Its light and gate move, rotate, and are enabled or disabled together. They keep the same connection to the crossing controller, which continues to control them.";
        // What an existing crossing bell's Supported edits says.
        internal const string BellOperations = "Move, enable, or disable this bell. The crossing controller continues to control when it rings. A bell's sound has no direction, so rotation does not apply to it.";
        // What the Supported edits of a detection span says: no placement of its own, and where its one supported
        // edit, crossing coverage, is made.
        internal const string DetectionSpanOperations = NotEditablePrefix + "A span's track locations determine its position, so you cannot set its placement. To change which tracks the crossing detects, use its controller's Connections tab.";
        // The guide link offered for a mast or bell that does not conform to its pattern: its label and the guide page
        // with the anchor of the refusal reasons.
        internal static readonly string[] EquipmentGuide = { "Why this equipment cannot be edited", "moving-crossing-equipment.md#refusal-reasons" };
        // Whether an inventory entry is an existing crossing mast or bell that does not conform to its pattern: it is
        // listed in its family with its refusal under Supported edits. Added equipment, which its crossing edits, is not.
        internal static bool RefusedEquipment(string family, string operations) =>
            (family == ExistingEquipmentScene.MastFamily || family == ExistingEquipmentScene.BellFamily) && Refusal(operations) != null;
        // Why the inspector cannot edit the inspected object at all, or null: the object's own reason when no capability
        // edits it. The placement fields, the rotation fields and Enabled are then disabled with that reason.
        internal static string InspectorEditBlock(bool editable, string operations) => editable ? null : EditReason(operations);
        // Why Enabled cannot be set for equipment that a culler of the game switches itself.
        internal const string EquipmentSwitched = "The game's culler enables and disables this object based on camera distance. It would override a saved Enabled setting, so the patcher cannot keep that setting applied";
        // Why Follow is inactive for a milepost: the milepost capability covers fixed placement, activity and the binding.
        internal const string MilepostNoFollow = "Mileposts use fixed placement. Set the track binding under Connections.";
        // A target confirmed in the world and not yet used is the one green relation: the relations it would replace,
        // the followed and the bound track, are not drawn beside it, and return once it is used or withdrawn.
        internal static bool ShowsCurrentRelations(bool confirmedShown) => !confirmedShown;
        // Why a placement mode cannot move a crossing pad, or null for a mode that can. The pad's geometry is generated
        // along its track binding, so a mode moves the pad only when it changes that binding. None of the four does:
        // Numeric and Gizmo write the pad object's placement, Snap writes that placement once from a track, and Follow keeps
        // that placement relative to a track without binding the marker to it. The binding is set under Connections.
        internal const string PadFollowsTrack = "A crossing pad follows its track: its geometry is generated along its track binding. Bind it to another track under Connections.";
        internal static string PadModeBlock(int mode) => mode == NumericMode || mode == GizmoMode || mode == SnapMode || mode == FollowMode ? PadFollowsTrack : null;
        // What a milepost's Supported edits says: placement, activity and binding as separate settings, and what stays unchanged.
        internal const string MilepostOperations = "Move, rotate, enable, or disable the milepost. You can also set, replace, or detach its track binding. The model and displayed mile value stay the same.";
        // What a crossing pad's Supported edits says: its binding and its activity, and no placement of its own.
        internal const string PadOperations = "The pad moves only when you change its track location. The game rebuilds it along the track. Enable or disable it separately.";
        // Whether the fields of an edit hold the object's current values: the edit would change nothing, so its
        // control is disabled, as a native field ignores a value equal to the one it shows. Numbers compare as
        // numbers, other text without case or outer spaces.
        // exact names the fields whose text is an identifier the game compares as it is written, such as a track
        // segment ID: the game's track graph finds a segment by its exact ID, so "s377" is not "S377".
        internal static bool SameFields(IReadOnlyList<string> entered, IReadOnlyList<string> current, params int[] exact) {
            if (entered == null || current == null || entered.Count != current.Count) return false;
            for (var i = 0; i < entered.Count; i++) {
                string a = (entered[i] ?? "").Trim(), b = (current[i] ?? "").Trim();
                if (exact != null && Array.IndexOf(exact, i) >= 0) { if (!String.Equals(a, b, StringComparison.Ordinal)) return false; continue; }
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                if (Double.TryParse(a, System.Globalization.NumberStyles.Float, culture, out var x) && Double.TryParse(b, System.Globalization.NumberStyles.Float, culture, out var y)) { if (x != y) return false; }
                else if (!String.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        // The field of a binding that holds the segment ID.
        internal static readonly int[] BindingIds = { 0 };
        // Why a button that applies the values of fields is unavailable now: the general reason first, then that the
        // fields hold the current values. Read again on every keystroke, so the button is available as soon as a
        // typed value differs from the current one and unavailable again once it equals it, without Enter and
        // without leaving the field.
        internal static string FieldReason(string block, IReadOnlyList<string> entered, IReadOnlyList<string> current, string same, params int[] exact) =>
            block ?? (SameFields(entered, current, exact) ? same : null);

        internal static bool NumericEditBlocksWorld(bool focused, IReadOnlyList<string> entered, IReadOnlyList<string> current) =>
            focused || !SameFields(entered, current);
        internal const string SamePlacement = "The fields hold the object's current placement.", SameBinding = "The fields hold the marker's current binding.", AlreadyDetached = "The marker is detached already.",
            SameLocation = "The fields hold the marker's current track location.", SameActivity = "The object already has the selected enabled or disabled state.";
        // The result of an edit that applied. An edit that changed nothing says so; an edit whose value equals the
        // object's value without this mod says that the modification no longer sets it; completed replaces the
        // plain result of an edit that did something else than its name says.
        internal static string EditResult(string action, bool unchanged, bool reverted, string completed) =>
            unchanged ? action + ": the object has this value already."
            : reverted ? action + " completed. The value equals the object's value without this mod, so the modification no longer sets it."
            : completed ?? action + " completed.";
        // The name of a candidate among the offers of a world choice: its number when several are offered, the same
        // in the world label, the message and the panel's list.
        internal static string ChoiceTag(string word, int number, int offers) => offers > 1 && number > 0 ? word + " " + number : word;
        internal static string ChoiceLine(string word, int number, int offers, string label, IEnumerable<string> offered, SceneObservation observation = null) {
            var text = ChoiceTag(word, number, offers) + ": " + ChoiceLabel(label, offered, 60, observation).TrimEnd();
            return text.EndsWith(".", StringComparison.Ordinal) || text.EndsWith("!", StringComparison.Ordinal) || text.EndsWith("?", StringComparison.Ordinal) ? text : text + ".";
        }
        // Whether a click on an outliner row clears the inspection: a click on the row that is inspected already.
        internal static bool ClearsInspection(object inspected, object clicked) => inspected != null && ReferenceEquals(inspected, clicked);
        // A short label for one of several offered choices. A scene object's full label is "Family: path", whose start
        // is the same for neighbours, so the label leads with what tells them apart: the object's own name, with as
        // many parents as it needs to differ from the other offers, then "[inactive]" and the family. A label that is
        // too long is cut at its end, which is the family. Any other label, such as a track segment's, is kept.
        internal static string ChoiceLabel(string label, IEnumerable<string> offered, int length = 40, SceneObservation observation = null) {
            if (label == null) return "";
            var text = label;
            if (Split(label, out var family, out var segments, out var inactive)) {
                var observedPath = observation?.Identity.Path;
                var visibleName = observation?.DisplayName;
                if (IsAddedFamily(observation?.Family)) {
                    var state = inactive ? " [inactive]" : "";
                    var shortened = InventoryPresentationState.NarrowName(observation.Family, visibleName, length - state.Length, value => value.Length);
                    if (shortened != visibleName || visibleName.Length + state.Length <= length) return shortened + state;
                }
                // A presentation name is not another hierarchy segment. Prepending a wrapper parent for equal
                // added names would hide that name again when the line is shortened; list numbers distinguish them.
                if (observedPath != null && visibleName != observedPath.Substring(observedPath.LastIndexOf('/') + 1))
                    text = visibleName + (inactive ? " [inactive]" : "") + (IsAddedFamily(family) ? "" : " (" + family + ")");
                else {
                    var depth = 1;
                    var others = new List<string[]>();
                    foreach (var other in offered ?? new string[0])
                        if (other != null && other != label && Split(other, out _, out var path, out _)) others.Add(path);
                    while (depth < segments.Length && others.Exists(path => Tail(path, depth) == Tail(segments, depth))) depth++;
                    text = Tail(segments, depth) + (inactive ? " [inactive]" : "") + " (" + family + ")";
                }
            }
            return text.Length <= length ? text : text.Substring(0, Math.Max(1, length - 3)) + "...";
        }
        static bool Split(string label, out string family, out string[] segments, out bool inactive) {
            family = null; segments = null; inactive = false;
            var colon = label.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0) return false;
            family = label.Substring(0, colon);
            var path = label.Substring(colon + 2);
            const string suffix = " [inactive]";
            if (path.EndsWith(suffix, StringComparison.Ordinal)) { inactive = true; path = path.Substring(0, path.Length - suffix.Length); }
            segments = path.Split('/');
            return segments.Length != 0 && segments[segments.Length - 1].Length != 0;
        }
        static string Tail(string[] segments, int count) => String.Join("/", segments, Math.Max(0, segments.Length - count), Math.Min(count, segments.Length));
        // The markers of an outliner row, in their order before the name: "!" for a detected problem, "*" for a changed
        // object, "×" instead of "*" for an object whose modification in this mod is not applied (failure is the
        // shown failure, ShownFailure), and "+" for an added object. "*" never means unsaved. If a game font lacks the
        // multiplication sign, an "x" is the documented fallback.
        internal static string RowMarkers(bool problem, bool changed, bool added, FailureKind? failure = null) => (problem ? "!" : "") + (failure != null ? "×" : changed ? "*" : "") + (added ? "+" : "");
        // The failure an object shows: only an object that this mod changes can show that its modification is not applied.
        internal static FailureKind? ShownFailure(bool changed, FailureKind? failure) => changed ? failure : null;
        // The row tooltip's line for an object whose modification in this mod is refused or otherwise not applied.
        internal static string FailureTooltipLine(FailureKind failure) => "This mod's modification of this object is " + (failure == FailureKind.Refused ? "refused" : "not applied") + ". The Changes tab gives the reason.";
        // The label of the Changes tab line, and whether a failure kind shows as not applied.
        internal static string FailureLabel(FailureKind failure) => FailureLabels.Of(failure);
        internal static bool ShowsAsNotApplied(FailureKind failure) => FailureLabels.ShowsAsNotApplied(failure);
        // The row's text without colours: the markers, one space, the name. A row without a marker starts with its name.
        internal static string RowText(string markers, string name) => String.IsNullOrEmpty(markers) ? name : markers + " " + name;
        // A state-line badge as the native map editor's inspector writes it.
        internal sealed class Badge {
            internal Badge(string text, string color) { Text = text; Color = color; }
            internal string Text { get; }
            internal string Color { get; }
            // Bold, capitals, 90 percent of the line's size, in the badge's colour.
            internal string Markup => "<color=" + Color + "><b><size=90%>" + Text + "</size></b></color>";
        }
        internal static readonly Badge Changed = new Badge("* CHANGED", Amber), Added = new Badge("+ ADDED", Green), Refused = new Badge("× REFUSED", Red), NotApplied = new Badge("× NOT APPLIED", Red);
        // The order of the state line under the heading: the badges and the problem count first, then the family, its
        // inactivity and inspection only. The line ends in an ellipsis beside the Inspector and Guided switch, so what is
        // cut off is the family, which the tooltip and the outliner also give, never a badge. Absent parts are left out.
        internal static List<T> StateParts<T>(T family, T inactive, IEnumerable<T> badges, T inspectionOnly, T problems) where T : class {
            var parts = new List<T>(badges ?? Enumerable.Empty<T>());
            foreach (var part in new[] { problems, family, inactive, inspectionOnly }) if (part != null) parts.Add(part);
            return parts;
        }
        // The search field's tooltip: a fixed title and what the field does, like every other control.
        internal const string SearchTitle = "Search objects", SearchTip = "Show only the objects whose name, path, family, binding IDs, dependencies, or problems contain the text you type.";
        // A modification that is not applied changes nothing: its object shows the refused or the not applied badge
        // instead of the changed one.
        internal static IReadOnlyList<Badge> Badges(bool changed, bool added, FailureKind? failure = null) {
            var badges = new List<Badge>();
            if (failure != null) badges.Add(failure == FailureKind.Refused ? Refused : NotApplied);
            else if (changed) badges.Add(Changed);
            if (added) badges.Add(Added);
            return badges;
        }
    }
    // Where the window opens while no bounds are saved. All values are screen pixels with y measured down from the top
    // of the screen; scale is the number of pixels in one canvas unit.
    internal static class PanelPlacement {
        // Less height than this, in canvas units, below the Map Editor window sends the window to the top right: the
        // minimum window height plus about four field rows of tab body.
        internal const float FallbackBelow = 600;
        internal struct Area {
            internal float X, Y, Width, Height;
            // Whether the fallback placement at the top right was taken.
            internal bool TopRight;
        }
        // Below the Map Editor window, with its left edge, as wide as the default width or that window, down to the
        // bottom of the screen. When less than FallbackBelow remains there: at the top right, right-aligned below what
        // occupies that corner (topRightBottom: the lower edge of the button strip and the status readout), with the
        // same width. Its height is what remains below that edge, at most the window's default height or what remains
        // below the Map Editor window, whichever is more, and never less than the minimum height.
        // mapEditorWidth is 0 when the Map Editor window is not shown; the window then keeps its default size at the left.
        // Where a help window stands: beside the panel, never over it, so the region it explains stays in view. To the
        // right of the panel where the screen has room, else to its left; its top at the "?" that opened it, within
        // the screen. Only when neither side has room does it take the side with more of it, at the screen's edge.
        // All values are screen pixels with y counted from the top.
        internal static Area Beside(float panelLeft, float panelRight, float anchorTop, float width, float height, float screenWidth, float screenHeight, float gap) {
            float x;
            if (panelRight + gap + width <= screenWidth) x = panelRight + gap;
            else if (panelLeft - gap - width >= 0) x = panelLeft - gap - width;
            else x = screenWidth - panelRight >= panelLeft ? Math.Max(0, screenWidth - width) : 0;
            var y = Math.Max(0, Math.Min(anchorTop, screenHeight - height));
            return new Area { X = x, Y = y, Width = width, Height = height };
        }
        // Where a placed window goes once it no longer lies within the screen, such as after its corner was dragged past
        // the screen's bottom edge: moved back inside with its own size, as the native window keeps itself within its
        // parent (Window.ClampToParentBounds), and never larger than the screen. A window within the screen, up to half a
        // pixel of rounding past an edge, stays as it is. Null for a window without a size.
        internal static Area? Within(Area current, float screenWidth, float screenHeight) {
            if (current.Width <= 0 || current.Height <= 0) return null;
            const float slack = 0.5f;
            if (current.X >= -slack && current.Y >= -slack && current.X + current.Width <= screenWidth + slack && current.Y + current.Height <= screenHeight + slack) return current;
            float width = Math.Min(current.Width, screenWidth), height = Math.Min(current.Height, screenHeight);
            return new Area { X = Math.Max(0, Math.Min(current.X, screenWidth - width)), Y = Math.Max(0, Math.Min(current.Y, screenHeight - height)), Width = width, Height = height };
        }
        internal static Area Default(float screenWidth, float screenHeight, float scale, float mapEditorLeft, float mapEditorWidth, float mapEditorBottom, float topRightBottom,
            float defaultWidth, float defaultHeight, float minimumHeight) {
            float width = defaultWidth * scale, x = 0, top = screenHeight - defaultHeight * scale;
            if (mapEditorWidth > 0) { x = mapEditorLeft; width = Math.Max(width, mapEditorWidth); top = mapEditorBottom; }
            width = Math.Min(width, screenWidth);
            var below = screenHeight - top;
            var minimum = minimumHeight * scale;
            // Choose the default side using the Map Editor's height at UI scale 1. Keep the actual window below it
            // only when the scaled minimum fits there.
            var belowAtDefaultScale = screenHeight - mapEditorBottom / scale;
            if (mapEditorWidth > 0 && (belowAtDefaultScale < FallbackBelow || below < minimum)) {
                var remaining = screenHeight - topRightBottom;
                var height = Math.Max(minimum, Math.Min(remaining, Math.Max(defaultHeight * scale, below)));
                height = Math.Min(height, screenHeight);
                return new Area { X = screenWidth - width, Y = Math.Min(topRightBottom, screenHeight - height), Width = width, Height = height, TopRight = true };
            }
            if (below < minimum) { below = Math.Min(minimum, screenHeight); top = screenHeight - below; }
            return new Area { X = Math.Max(0, Math.Min(x, screenWidth - width)), Y = Math.Max(0, top), Width = width, Height = below };
        }
        // The lower edge of what occupies the top of a column of the screen: starting at the top, every rectangle that
        // reaches into the column and touches the edge found so far moves the edge to its own lower edge.
        internal static float OccupiedBelow(float columnLeft, float columnRight, IEnumerable<Area> occupied, float limit) {
            var rectangles = new List<Area>(occupied);
            var edge = 0f;
            for (var moved = true; moved;) {
                moved = false;
                foreach (var r in rectangles) {
                    if (r.Width <= 0 || r.Height <= 0 || r.X >= columnRight || r.X + r.Width <= columnLeft) continue;
                    if (r.Y > edge + 1 || r.Y + r.Height <= edge || r.Y + r.Height > limit) continue;
                    edge = r.Y + r.Height; moved = true;
                }
            }
            return edge;
        }
    }
}
