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
        internal const string Green = "#7FB86B", Amber = "#E8A33D";
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
        internal static Lamp Inventory(bool stale) => new Lamp(stale ? "Stale" : "Current", "Current", !stale);
        internal static Lamp Save(bool unsaved) => new Lamp(unsaved ? "Unsaved" : "Saved", "Unsaved", !unsaved);
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
        // The tooltip of a lamp: its title is the state word, and its text starts with that word, so a lamp without its
        // word still says it.
        internal static string LampTooltip(Lamp lamp, string reason) => String.IsNullOrEmpty(reason) ? lamp.Word + "." : lamp.Word + ": " + reason;
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
        // The tooltip of a control: what it does, and when it cannot be used now, also why. A disabled control keeps its
        // description, so two disabled controls with one reason still say what each would do.
        // The reason follows the one label Unavailable: as a plain sentence (Sentence), without a label of its own.
        internal static string Tooltip(string description, string reason) =>
            String.IsNullOrEmpty(reason) ? description : String.IsNullOrEmpty(description) ? Sentence(reason) : description + "\nUnavailable: " + Sentence(reason);
        // The labels a reason can carry where it is stored or shown on its own: under Supported edits, or in the
        // reason of a binding that cannot be set.
        static readonly string[] ReasonLabels = { "Not editable: ", "Not yet supported: ", "Track binding unavailable: ", "Pad binding unavailable: " };
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
        // The three controls that take something back are named by what they take back. Object: this mod's activity
        // value. Placement, Snap: the snap that was prepared and not applied. Changes: the whole modification.
        internal const string ClearActivity = "Clear activity", DiscardPrepared = "Discard", RemoveModification = "Remove modification", ClearBinding = "Clear binding", ClearCircuit = "Clear circuit", ClearLocation = "Clear location";
        // Why Clear location has nothing to clear: this mod sets no track location for the starter placement marker.
        internal static string ClearLocationReason(bool hasValue) => hasValue ? null : "This mod sets no track location for this marker, so there is nothing to clear.";
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
            ? "Activity follows the track group. Disable holds it inactive; Clear activity returns it to the group; an explicit enable is refused."
            : "Activity follows the track group.";
        // Whether the open help closes on a tab switch: the help of a tab that is no longer the open tab. The help of
        // the outliner or of the window stays.
        internal static bool ClosesHelp(string openRegion, string tab, IEnumerable<string> tabs) {
            if (openRegion == null || openRegion == tab) return false;
            foreach (var name in tabs) if (name == openRegion) return true;
            return false;
        }
        // How far the footer's row stands above the window's lower edge beyond its own padding, so that it clears the
        // native resize grip in the lower right corner: the grip's height less the padding below the row.
        internal static float GripClearance(float gripHeight, float paddingBelowRow) => Math.Max(0, gripHeight - paddingBelowRow);
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
        internal static string ShortLabel(string label) => label == RemoveModification ? "Remove" : label;
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
            { "Copy ID", 63.48f }, { "Fit", 21.26f }, { "?", 9.17f }, { "x", 8.84f }, { "Guide", 47f } };
        // One colour table for every world marking. Gold frames the inspected object. A candidate of a pick mode is
        // blue: saturated while the pointer is over it, lighter once it was clicked and until Confirm. A current
        // relation of the inspected object, native or set by a patch, is green, and more saturated under the pointer.
        // A candidate that is neither hovered nor clicked shows only its neutral "+" marker. Red marks a broken item.
        internal enum WorldMark { Inspected, Marker, CandidateHover, CandidateSelected, Relation, RelationHover, Broken }
        internal static float[] WorldColor(WorldMark mark) {
            switch (mark) {
                case WorldMark.Inspected: return new[] { 1f, .8f, 0f };
                case WorldMark.CandidateHover: return new[] { .1f, .6f, 1f };
                case WorldMark.CandidateSelected: return new[] { .45f, .85f, 1f };
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
        // Whether Escape does what Clear does: with the pointer over the open panel, while an object is inspected or a
        // world choice is pending. Elsewhere Escape cancels a pending choice or the gizmo, or opens the pause menu.
        internal static bool EscapeClears(bool overPanel, bool inspected, bool pending) => overPanel && (inspected || pending);
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
        // menu; it never clears a selection. Here: while a field of the panel has the focus Escape is left alone;
        // the list beside the click closes alone, as a native dropdown does, and the choice stays pending; over the
        // panel Escape does what Clear does; a pending world choice or a gizmo drag is an armed interaction and is
        // cancelled; anything else, such as a gizmo that merely stands, leaves Escape to the pause menu.
        internal enum EscapeStep { None, CloseList, Clear, CancelChoice, CancelDrag }
        internal static EscapeStep Escape(bool fieldFocused, bool listOpen, bool overPanel, bool inspected, bool pending, bool dragging) =>
            fieldFocused ? EscapeStep.None
            : listOpen ? EscapeStep.CloseList
            : EscapeClears(overPanel, inspected, pending) ? EscapeStep.Clear
            : pending ? EscapeStep.CancelChoice
            : dragging ? EscapeStep.CancelDrag
            : EscapeStep.None;
        // The supported edits of a group root of the Baked scenery group family.
        internal const string GroupOperations = "Fixed placement and activity of the whole group; culling and colliders follow the group. Decorative: moving or disabling it changes no game function";
        internal const string Decorative = "Decorative. Moving or disabling this group changes no game function.";
        // The reason an object is inspection only: its kind has no edit yet. kind names it, such as "the Milepost family".
        internal static string NotYetSupported(string kind) => NotYetSupportedPrefix + "the patcher has no edit for " + kind + " yet";
        // The reason an object is inspection only, given as a whole clause.
        internal static string NotYetSupportedBecause(string reason) => NotYetSupportedPrefix + reason;
        // The families of a light or gate and of a bell this patcher added: supported families whose edits live on their
        // crossing's Connections tab, not on the object itself.
        internal const string AddedEquipmentFamily = "Added light or gate", AddedBellFamily = "Added bell";
        internal static bool IsAddedFamily(string family) => family == AddedEquipmentFamily || family == AddedBellFamily;
        internal static string AddedEquipmentOperations(string id, string scope, bool bell = false) =>
            (bell ? "Added bell " : "Added light or gate ") + id + " of " + scope + ". Use its crossing controller's Connections tab to edit or remove it.";
        // Why an object of the family is inspection only, or null when a supported operation covers it: a supported kind,
        // or an added light, gate or bell, which its crossing edits.
        internal static string InspectionOnly(string family, bool supportedKind) => supportedKind || IsAddedFamily(family) ? null : NotYetSupported("the " + family + " family");
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
        // The values of a group that cannot be edited now, as one running line: "X 4361.091  Y 528.405  Z 5380.431 (m)".
        // A value that is not set reads as a dash.
        internal static string RunningLine(IReadOnlyList<string> labels, IReadOnlyList<string> values, string unit) =>
            String.Join("  ", labels.Select((label, i) => label + " " + (i < values.Count && !String.IsNullOrWhiteSpace(values[i]) ? values[i].Trim() : "-"))) + (unit == null ? "" : " (" + unit + ")");
        // Why the inspected object cannot be edited, as the yellow message and every "Unavailable:" tooltip give it:
        // the object's own reason, which the Object tab shows under Supported edits, and not a pointer to that tab. It is
        // one plain sentence: the message's source and the tooltip's Unavailable: are its only label.
        internal static string EditReason(string operations) {
            var own = operations?.Trim();
            if (String.IsNullOrEmpty(own) || !(own.StartsWith(NotEditablePrefix, StringComparison.Ordinal) || own.StartsWith(NotYetSupportedPrefix, StringComparison.Ordinal))) return "This object cannot be edited here.";
            return Sentence(own);
        }
        const string NotYetSupportedPrefix = "Not yet supported: ";
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
        // every mode. An object that cannot be edited keeps Numeric, which shows its placement, and has every other mode
        // inactive with the object's own reason.
        internal static string ModeBlock(int mode, bool pad, bool editable, string operations) =>
            pad ? PadModeBlock(mode) : !editable && mode != NumericMode ? EditReason(operations) : null;
        // A target confirmed in the world and not yet used is the one green relation: the relations it would replace,
        // the followed and the bound track, are not drawn beside it, and return once it is used or withdrawn.
        internal static bool ShowsCurrentRelations(bool confirmedShown) => !confirmedShown;
        // Why a placement mode cannot move a crossing pad, or null for a mode that can. The pad's geometry is generated
        // along its track binding, so a mode moves the pad only when it changes that binding. None of the four does:
        // Numeric and Gizmo write the pad object's placement, Snap writes that placement once from a track, and Follow keeps
        // that placement relative to a track without binding the marker to it. The binding is set under Connections.
        internal const string PadFollowsTrack = "A crossing pad follows its track: its geometry is generated along its track binding. Bind it to another track under Connections.";
        internal static string PadModeBlock(int mode) => mode == NumericMode || mode == GizmoMode || mode == SnapMode || mode == FollowMode ? PadFollowsTrack : null;
        // What a crossing pad's Supported edits says: its binding and its activity, and no placement of its own.
        internal const string PadOperations = "Pad binding: set or replace the marker location with its graph registration and regenerate the pad geometry and culling; activity is a separate setting. The pad follows its track, so it has no placement of its own";
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
        internal const string SamePlacement = "The fields hold the object's current placement.", SameBinding = "The fields hold the marker's current binding.", AlreadyDetached = "The marker is detached already.",
            SameLocation = "The fields hold the marker's current track location.";
        // The result of an edit that applied. An edit that changed nothing says so; an edit whose value equals the
        // object's value without this mod says that the modification no longer sets it; completed replaces the
        // plain result of an edit that did something else than its name says.
        internal static string EditResult(string action, bool unchanged, bool reverted, string completed) =>
            unchanged ? action + ": nothing changed; the object has this value already."
            : reverted ? action + " completed. The value equals the object's value without this mod, so the modification no longer sets it."
            : completed ?? action + " completed.";
        // The name of a candidate among the offers of a world choice: its number when several are offered, the same
        // in the world label, the message and the panel's list.
        internal static string ChoiceTag(string word, int number, int offers) => offers > 1 && number > 0 ? word + " " + number : word;
        // Whether a click on an outliner row clears the inspection: a click on the row that is inspected already.
        internal static bool ClearsInspection(object inspected, object clicked) => inspected != null && ReferenceEquals(inspected, clicked);
        // A short label for one of several offered choices. A scene object's full label is "Family: path", whose start
        // is the same for neighbours, so the label leads with what tells them apart: the object's own name, with as
        // many parents as it needs to differ from the other offers, then "[inactive]" and the family. A label that is
        // too long is cut at its end, which is the family. Any other label, such as a track segment's, is kept.
        internal static string ChoiceLabel(string label, IEnumerable<string> offered, int length = 40) {
            if (label == null) return "";
            var text = label;
            if (Split(label, out var family, out var segments, out var inactive)) {
                var depth = 1;
                var others = new List<string[]>();
                foreach (var other in offered ?? new string[0])
                    if (other != null && other != label && Split(other, out _, out var path, out _)) others.Add(path);
                while (depth < segments.Length && others.Exists(path => Tail(path, depth) == Tail(segments, depth))) depth++;
                text = Tail(segments, depth) + (inactive ? " [inactive]" : "") + " (" + family + ")";
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
        // object and "+" for an added object. "*" never means unsaved.
        internal static string RowMarkers(bool problem, bool changed, bool added) => (problem ? "!" : "") + (changed ? "*" : "") + (added ? "+" : "");
        // The row's text without colours: the markers, one space, the name. A row without a marker starts with its name.
        internal static string RowText(string markers, string name) => String.IsNullOrEmpty(markers) ? name : markers + " " + name;
        // A state-line badge as the native Map Editor's inspector writes it.
        internal sealed class Badge {
            internal Badge(string text, string color) { Text = text; Color = color; }
            internal string Text { get; }
            internal string Color { get; }
            // Bold, capitals, 90 percent of the line's size, in the badge's colour.
            internal string Markup => "<color=" + Color + "><b><size=90%>" + Text + "</size></b></color>";
        }
        internal static readonly Badge Changed = new Badge("* CHANGED", Amber), Added = new Badge("+ ADDED", Green);
        internal static IReadOnlyList<Badge> Badges(bool changed, bool added) {
            var badges = new List<Badge>();
            if (changed) badges.Add(Changed);
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
        internal static Area Default(float screenWidth, float screenHeight, float scale, float mapEditorLeft, float mapEditorWidth, float mapEditorBottom, float topRightBottom,
            float defaultWidth, float defaultHeight, float minimumHeight) {
            float width = defaultWidth * scale, x = 0, top = screenHeight - defaultHeight * scale;
            if (mapEditorWidth > 0) { x = mapEditorLeft; width = Math.Max(width, mapEditorWidth); top = mapEditorBottom; }
            width = Math.Min(width, screenWidth);
            var below = screenHeight - top;
            if (mapEditorWidth > 0 && below < FallbackBelow * scale) {
                var remaining = screenHeight - topRightBottom;
                var height = Math.Max(minimumHeight * scale, Math.Min(remaining, Math.Max(defaultHeight * scale, below)));
                height = Math.Min(height, screenHeight);
                return new Area { X = screenWidth - width, Y = Math.Min(topRightBottom, screenHeight - height), Width = width, Height = height, TopRight = true };
            }
            var minimum = minimumHeight * scale;
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
