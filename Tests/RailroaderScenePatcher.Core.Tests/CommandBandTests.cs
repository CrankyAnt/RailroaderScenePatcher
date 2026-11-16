// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

static class CommandBandTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run() {
        LampsShowOneWordAndColourPerState();
        LampWordsFitOrMoveIntoTheTooltip();
        CommandsAreDisabledOnlyForTheirOwnReason();
        MarkersStandBeforeTheName();
        BadgesAreTheNativeOnes();
        TheWindowOpensBelowTheMapEditorOrAtTheTopRight();
        OwnSaveKeepsTheDefinitionsCurrent();
        ADisabledControlKeepsItsDescription();
        ChoiceLabelsLeadWithWhatTellsThemApart();
        AClickOnTheInspectedRowClearsTheInspection();
        ClearActivityNeedsAnActivityValue();
        AClosedPanelCancelsTheWaitingRequest();
        SharedRulesHoldAtEveryCallSite();
        ReasonsReadAsOnePlainSentence();
        Console.WriteLine("PASS: status lamps, band commands, row markers, state badges and default window placement");
    }
    // A tooltip of a control that cannot be used carries one label, Unavailable:, and the reason after it as one plain
    // sentence: a reason's own label, such as Not editable: or Track binding unavailable:, is not repeated after it.
    static void ReasonsReadAsOnePlainSentence() {
        var starter = StarterPlacements.ObjectSettings + ".";
        var blocked = CommandBand.NotEditable(StarterPlacements.ObjectSettings);
        foreach (var row in new[] {
            new[] { blocked, starter },
            new[] { CommandBand.EditReason(blocked), starter },
            new[] { CommandBand.ModeBlock(CommandBand.GizmoMode, false, false, blocked), starter },
            new[] { StarterPlacements.NoDetach, StarterPlacements.NoDetach + "." },
            new[] { "Pad binding unavailable: the pad is part of a crossing", "The pad is part of a crossing." },
            new[] { CommandBand.NotYetSupported("the Milepost family"), "The patcher has no edit for the Milepost family yet." },
            new[] { CommandBand.ModeBlock(CommandBand.SnapMode, false, false, CommandBand.NotYetSupported("the Track span family")), "The patcher has no edit for the Track span family yet." },
            new[] { CommandBand.NotEditable(CommandBand.SharedPath(3)), "3 objects share this hierarchy path and component, so a patch cannot name one of them." },
            new[] { StarterPlacements.NoTrackPoint, "The marker's track location does not resolve to a track point." },
            new[] { "Inspect an object first.", "Inspect an object first." },
            new[] { CommandBand.PadFollowsTrack, CommandBand.PadFollowsTrack.TrimEnd('.') + "." },
        }) {
            var tooltip = CommandBand.Tooltip("Does something.", row[0]);
            var lines = tooltip.Split('\n');
            Check(lines.Length == 2 && lines[0] == "Does something." && lines[1] == "Unavailable: " + row[1], "The tooltip gives the reason as one plain sentence: " + tooltip);
            var reason = lines[1].Substring("Unavailable: ".Length);
            Check(new[] { "Not editable:", "Not yet supported:", "binding unavailable:", "Unavailable:" }.All(label => reason.IndexOf(label, StringComparison.OrdinalIgnoreCase) < 0)
                && (Char.IsUpper(reason[0]) || Char.IsDigit(reason[0])) && reason.EndsWith(".", StringComparison.Ordinal), "One label per tooltip, the reason a sentence: " + tooltip);
            Check(CommandBand.Tooltip(null, row[0]) == row[1], "Without a description the tooltip is the sentence alone: " + CommandBand.Tooltip(null, row[0]));
        }
    }
    // A lamp shows green for Current and Saved and amber for Stale and Unsaved, and reserves its longer word.
    static void LampsShowOneWordAndColourPerState() {
        var current = CommandBand.Inventory(false); var stale = CommandBand.Inventory(true);
        Check(current.Word == "Current" && current.Good && current.Color == "#7FB86B", "A current inventory reads Current in green");
        Check(stale.Word == "Stale" && !stale.Good && stale.Color == "#E8A33D", "A stale inventory reads Stale in amber");
        Check(current.Reserve == "Current" && stale.Reserve == "Current", "The inventory lamp reserves the width of Current in both states");
        var saved = CommandBand.Save(false); var unsaved = CommandBand.Save(true);
        Check(saved.Word == "Saved" && saved.Good && saved.Color == "#7FB86B", "Saved work reads Saved in green");
        Check(unsaved.Word == "Unsaved" && !unsaved.Good && unsaved.Color == "#E8A33D", "Unsaved work reads Unsaved in amber");
        Check(saved.Reserve == "Unsaved" && unsaved.Reserve == "Unsaved", "The save lamp reserves the width of Unsaved in both states");
        Check(current.Reserve.Length >= current.Word.Length && stale.Reserve.Length >= stale.Word.Length && saved.Reserve.Length >= saved.Word.Length, "The reserved word is the longer one");
    }
    // Refresh and both lamps with their words need 75 + (10 + 3 + 66) + (10 + 3 + 72) + 3 x 4 = 251 canvas units. The
    // decision uses fixed widths, so it is the same before and after the band's first layout.
    static void LampWordsFitOrMoveIntoTheTooltip() {
        Check(CommandBand.LeftBandWidth == 251 && CommandBand.InventoryLamp == 79 && CommandBand.SaveLamp == 85, "The left band needs 251 units: " + CommandBand.LeftBandWidth);
        Check(CommandBand.WordsFit(CommandBand.LeftBandWidth), "The words fit a band that is exactly as wide as they need");
        Check(CommandBand.WordsFit(252) && CommandBand.WordsFit(284), "The words fit the band at the outliner's minimum width and at any wider one");
        Check(!CommandBand.WordsFit(236) && !CommandBand.WordsFit(250), "A band that is too narrow keeps the lamps and drops the words");
        // A window saved at its minimum of 660 by 410 has a content of 636 by 364 inside a frame of 24 by 46.
        Check(CommandBand.RestoredContent(636, 660, 24) == 636 && CommandBand.RestoredContent(364, 410, 46) == 364, "A content size saved at the minimum window is restored as saved, not raised to the window's minimum");
        Check(CommandBand.RestoredContent(900, 660, 24) == 900 && CommandBand.RestoredContent(100, 660, 24) == 636, "A larger saved size stays, and a smaller one is raised to the minimum window's content");
        // A lamp without its word says it in the tooltip, first.
        Check(CommandBand.LampTooltip(CommandBand.Inventory(true), "Loc: Walker Branch changed (position)") == "Stale: Loc: Walker Branch changed (position)", "The tooltip text starts with the state word");
        Check(CommandBand.LampTooltip(CommandBand.Save(false), null) == "Saved." && CommandBand.LampTooltip(CommandBand.Save(true), "") == "Unsaved.", "Without a reason the tooltip is the word");
        Check(CommandBand.InventoryWord >= 60 && CommandBand.SaveWord >= CommandBand.InventoryWord, "The reserved widths hold the longer word of each lamp");
    }
    static void CommandsAreDisabledOnlyForTheirOwnReason() {
        Check(CommandBand.SelectInWorld(true, false) == null, "Select in world is enabled without a pending choice, whether or not an object is inspected");
        Check(CommandBand.SelectInWorld(true, true) == "Confirm or cancel the pending world choice first.", "Select in world is disabled while a world choice is pending, with that reason");
        Check(CommandBand.SelectInWorld(false, false) != null, "Select in world has nothing to start when world selection is unavailable");
        Check(CommandBand.Focus(true) == null && CommandBand.CopyId(true) == null, "Focus and Copy ID are enabled for an inspected object, also while a world choice is pending");
        Check(CommandBand.Clear(true, false) == null && CommandBand.Clear(false, true) == null && CommandBand.Clear(true, true) == null,
            "Clear is enabled for an inspected object and for a pending world choice without one");
        // Escape over the panel does what Clear does, in the states where Clear is enabled.
        Check(CommandBand.EscapeClears(true, true, false) && CommandBand.EscapeClears(true, false, true) && CommandBand.EscapeClears(true, true, true), "Escape over the panel clears an inspection and a pending choice");
        Check(!CommandBand.EscapeClears(true, false, false) && !CommandBand.EscapeClears(false, true, true) && !CommandBand.EscapeClears(false, true, false), "Escape clears nothing with nothing to clear, or with the pointer over the world");
        // One press of Escape does one thing. Columns: a field has the focus, the list beside the click is open, the
        // pointer is over the panel, an object is inspected, a world choice is pending, a gizmo drag is in progress.
        foreach (var row in new[] {
            new object[] { false, false, false, true, false, false, CommandBand.EscapeStep.None, "over the world with a gizmo that merely stands: the pause menu, as without the mod" },
            new object[] { false, false, false, false, false, false, CommandBand.EscapeStep.None, "nothing to end: the pause menu" },
            new object[] { false, false, true, true, false, false, CommandBand.EscapeStep.Clear, "over the panel with an inspected object: the inspection is cleared" },
            new object[] { false, false, true, false, true, false, CommandBand.EscapeStep.Clear, "over the panel with a pending choice: cleared as Clear does" },
            new object[] { false, false, true, false, false, false, CommandBand.EscapeStep.None, "over the panel with nothing to clear: the pause menu" },
            new object[] { false, false, false, true, true, false, CommandBand.EscapeStep.CancelChoice, "over the world with a pending choice: the choice is cancelled, as the native map editor disarms a tool" },
            new object[] { false, false, false, true, false, true, CommandBand.EscapeStep.CancelDrag, "during a gizmo drag: the placement is restored" },
            new object[] { false, true, false, true, true, false, CommandBand.EscapeStep.CloseList, "the list beside the click is open, pointer over the world: only the list closes" },
            new object[] { false, true, true, true, true, false, CommandBand.EscapeStep.CloseList, "the list is open, pointer over the panel: the list closes first, the inspection stays" },
            new object[] { true, true, true, true, true, true, CommandBand.EscapeStep.None, "a field has the focus: Escape is left alone, as in the native map editor" } })
            Check(CommandBand.Escape((bool)row[0], (bool)row[1], (bool)row[2], (bool)row[3], (bool)row[4], (bool)row[5]) == (CommandBand.EscapeStep)row[6], "Escape " + row[7]);
        // A field that had the focus when Escape went down has given it up by the time the panel is asked: the
        // focus counts for the frame it was last seen in and the two after it.
        foreach (var row in new[] { new[] { 100, 100, 1 }, new[] { 101, 100, 1 }, new[] { 102, 100, 1 }, new[] { 103, 100, 0 }, new[] { 100, -1, 0 }, new[] { 1, -1, 0 }, new[] { 500, 100, 0 } })
            Check(CommandBand.FieldHasFocus(row[0], row[1]) == (row[2] == 1), "In frame " + row[0] + " a field last focused in frame " + row[1] + " " + (row[2] == 1 ? "has" : "does not have") + " the keyboard");
        Check(CommandBand.FocusFrames == 2, "The focus counts for two frames after it was last seen");
        // With a field focused, Escape does nothing to the patcher in any state: the inspection, a pending choice, an
        // open list and a drag all stay.
        foreach (var state in new[] { new[] { false, true, true, false, false }, new[] { true, true, true, true, false }, new[] { false, false, true, true, true }, new[] { true, false, false, true, false } })
            Check(CommandBand.Escape(true, state[0], state[1], state[2], state[3], state[4]) == CommandBand.EscapeStep.None, "Escape with the caret in a field clears and cancels nothing");
        // The tooltip stays hidden through one pause, whatever the panel does meanwhile.
        foreach (var row in new[] {
            new object[] { true, true, false, true, "paused with the panel open: hidden" },
            new object[] { true, false, true, true, "the panel closed while still paused: stays hidden" },
            new object[] { true, false, false, false, "paused with the panel closed all along: the game's own tooltips are left alone" },
            new object[] { false, true, true, false, "the pause menu closed: shown again" },
            new object[] { false, false, true, false, "the pause menu closed after the panel closed: shown again" },
            new object[] { false, true, false, false, "not paused: never hidden" } })
            Check(CommandBand.HidesTooltip((bool)row[0], (bool)row[1], (bool)row[2]) == (bool)row[3], "The tooltip, " + row[4]);
        Check(CommandBand.Clear(false, false) == "Nothing is inspected and no world choice is pending.", "Clear is disabled only when nothing is inspected and no world choice is pending");
        Check(CommandBand.Tooltip("End the inspection and cancel a pending world choice, gizmo or snap. Changes that were made stay.", CommandBand.Clear(false, false))
            == "End the inspection and cancel a pending world choice, gizmo or snap. Changes that were made stay.\nUnavailable: Nothing is inspected and no world choice is pending.", "The disabled Clear says what it does first");
        Check(CommandBand.Focus(false) == "Inspect an object first." && CommandBand.CopyId(false) == "Inspect an object first.", "Focus and Copy ID are disabled when nothing is inspected, with that reason");
        Check(CommandBand.CopyId(true, "Invalid JSON. The patch document is protected from overwrite.") == "Invalid JSON. The patch document is protected from overwrite."
            && CommandBand.CopyId(false, "Invalid JSON.") == "Inspect an object first.", "Copy ID is disabled with the session's refusal while the patch document cannot be read, and asks for an inspection first");
    }
    // The native outline appends its markers; the panel puts them before the name so that a cut-off name keeps them.
    static void MarkersStandBeforeTheName() {
        Check(CommandBand.RowMarkers(false, false, false) == "" && CommandBand.RowText("", "Loc: Walker Branch") == "Loc: Walker Branch", "An unmarked row starts with its name");
        Check(CommandBand.RowText(CommandBand.RowMarkers(false, true, false), "Loc: Walker Branch") == "* Loc: Walker Branch", "A changed object has * before its name");
        Check(CommandBand.RowText(CommandBand.RowMarkers(false, false, true), "Sign") == "+ Sign", "An added object has + before its name");
        Check(CommandBand.RowText(CommandBand.RowMarkers(true, true, true), "Sign") == "!*+ Sign", "Problem, changed and added keep that order before the name");
        Check(!CommandBand.RowText(CommandBand.RowMarkers(false, true, true), "Sign").EndsWith("*") && !CommandBand.RowText(CommandBand.RowMarkers(false, true, true), "Sign").EndsWith("+"), "No marker follows the name");
    }
    static void BadgesAreTheNativeOnes() {
        Check(CommandBand.Badges(false, false).Count == 0, "An unchanged object has no badge");
        var changed = CommandBand.Badges(true, false).Single();
        Check(changed.Text == "* CHANGED" && changed.Color == "#E8A33D" && changed.Markup == "<color=#E8A33D><b><size=90%>* CHANGED</size></b></color>", "The changed badge is * CHANGED, amber, bold, at 90 percent: " + changed.Markup);
        var added = CommandBand.Badges(false, true).Single();
        Check(added.Text == "+ ADDED" && added.Color == "#7FB86B" && added.Markup == "<color=#7FB86B><b><size=90%>+ ADDED</size></b></color>", "The added badge is + ADDED, green, bold, at 90 percent: " + added.Markup);
        Check(CommandBand.Badges(true, true).Select(b => b.Text).SequenceEqual(new[] { "* CHANGED", "+ ADDED" }), "Both badges keep their order");
    }
    // The supplied definitions are stale when a patch document changed outside scene patch editing, not when the patcher saved
    // it itself, and a refresh makes them current again. The lamp follows: Current, Stale, Current.
    static void OwnSaveKeepsTheDefinitionsCurrent() {
        RailroaderScenePatcher.Core.LoadedMod Loaded(string id, string json, string error = null) =>
            new RailroaderScenePatcher.Core.LoadedMod(new RailroaderScenePatcher.Core.PatchSource(id, id, "1.0"), json, null, true, error);
        const string before = "{\"modifications\":{}}", written = "{\"modifications\":{\"board\":{\"active\":false}}}", outside = "{\"modifications\":{\"board\":{\"active\":true}}}";
        var definitions = new SuppliedDefinitions();
        Check(definitions.Stale && CommandBand.Inventory(definitions.Stale).Word == "Stale", "Before the first refresh the definitions are not read");
        definitions.Read(new[] { Loaded("pack", before) });
        definitions.Compare(new[] { Loaded("pack", before) });
        Check(!definitions.Stale && CommandBand.Inventory(definitions.Stale).Word == "Current", "After a refresh the unchanged document is current");
        // The patcher saves: the file now holds what it wrote.
        definitions.Written("pack", written);
        definitions.Compare(new[] { Loaded("pack", written) });
        Check(!definitions.Stale, "The patcher's own Save keeps the definitions current");
        definitions.Compare(new[] { Loaded("pack", written) });
        Check(!definitions.Stale, "and they stay current on every later comparison");
        // The file is changed outside scene patch editing.
        definitions.Compare(new[] { Loaded("pack", outside) });
        Check(definitions.Stale && CommandBand.Inventory(definitions.Stale).Word == "Stale", "A document changed outside scene patch editing marks the definitions stale");
        definitions.Compare(new[] { Loaded("pack", written) });
        Check(definitions.Stale, "Changing the file back does not clear it: only a refresh does");
        definitions.Read(new[] { Loaded("pack", outside) });
        Check(!definitions.Stale && CommandBand.Inventory(definitions.Stale).Word == "Current", "Refresh returns Current");
        // A document reordered or re-spaced outside scene patch editing has equal content: it is not reported as changed.
        const string keyed = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"a\":{\"active\":false,\"dependsOn\":[\"b\",\"c\"],\"x\":12},\"b\":{\"active\":true}}}";
        const string reordered = "{\n  \"modifications\": {\n    \"b\": { \"active\": true },\n    \"a\": { \"x\": 12.0, \"dependsOn\": [\"c\", \"b\"], \"active\": false }\n  },\n  \"sourceMap\": { \"mapId\": \"map\" },\n  \"formatVersion\": 1\n}\n";
        var form = new SuppliedDefinitions();
        form.Read(new[] { Loaded("pack", keyed) });
        form.Compare(new[] { Loaded("pack", reordered) });
        Check(!form.Stale && CommandBand.Inventory(form.Stale).Word == "Current", "A reordered and re-spaced document with another number literal is not reported as changed");
        form.Compare(new[] { Loaded("pack", keyed) });
        Check(!form.Stale, "and neither is the first form when it returns");
        form.Compare(new[] { Loaded("pack", reordered.Replace("12.0", "13")) });
        Check(form.Stale, "A changed value in the reordered document marks the definitions stale");
        form.Read(new[] { Loaded("pack", keyed) });
        form.Compare(new[] { Loaded("pack", keyed.Replace("[\"b\",\"c\"]", "[\"b\",\"b\",\"c\"]")) });
        Check(form.Stale, "A repeated dependency is other content");
        form.Read(new[] { Loaded("pack", "{broken") });
        form.Compare(new[] { Loaded("pack", "{broken") });
        Check(!form.Stale, "The same unparseable text is unchanged");
        form.Compare(new[] { Loaded("pack", "{ broken") });
        Check(form.Stale, "Another unparseable text marks them stale");
        // The first Save creates the document: no mod carried one at the refresh.
        var fresh = new SuppliedDefinitions();
        fresh.Read(new RailroaderScenePatcher.Core.LoadedMod[0]);
        fresh.Written("pack", written);
        fresh.Compare(new[] { Loaded("pack", written) });
        Check(!fresh.Stale, "A document the patcher's own Save created is current");
        fresh.Compare(new[] { Loaded("pack", written), Loaded("other", before) });
        Check(fresh.Stale, "A document that appears from elsewhere marks them stale");
        // A document that can no longer be read, and one removed outside scene patch editing.
        definitions.Compare(new[] { Loaded("pack", null, "Cannot read patch document") });
        Check(definitions.Stale, "An unreadable document marks them stale");
        definitions.Read(new[] { Loaded("pack", outside) });
        definitions.Compare(new RailroaderScenePatcher.Core.LoadedMod[0]);
        Check(definitions.Stale, "A removed document marks them stale");
        // A write while already stale does not vouch for what else changed.
        definitions.Read(new[] { Loaded("pack", before) });
        definitions.Compare(new[] { Loaded("pack", outside) });
        definitions.Written("pack", written);
        definitions.Compare(new[] { Loaded("pack", written) });
        Check(definitions.Stale, "A Save after an outside change leaves the definitions stale until Refresh");
        // A new load attempt starts over, as before the first refresh.
        definitions.Read(new[] { Loaded("pack", before) });
        definitions.Reset();
        Check(definitions.Stale, "After a reset the definitions are not read");
        definitions.Written("pack", written);
        definitions.Compare(new[] { Loaded("pack", written) });
        Check(definitions.Stale, "A write does not stand in for the refresh a new load attempt needs");
        var messages = new EditFeedback();
        messages.Report("Placement", "Placement completed."); messages.Follow("Terrain output", "Terrain output complete.");
        messages.Clear();
        Check(messages.Messages.Count == 0 && messages.Result == "", "A new load attempt starts without messages");
        // The inventory notice names the supplied definitions for this state.
        Check(InventoryPresentationState.Notice(null, true).Contains("inspection is unavailable"), "Without an inventory the notice names that first");
    }
    // A disabled control says what it would do and why it cannot now; an enabled one only what it does.
    static void ADisabledControlKeepsItsDescription() {
        Check(CommandBand.Tooltip("Move the object with the native gizmo.", null) == "Move the object with the native gizmo.", "An enabled control shows its description");
        Check(CommandBand.Tooltip("Move the object with the native gizmo.", "Inspect an object first.") == "Move the object with the native gizmo.\nUnavailable: Inspect an object first.",
            "A disabled control shows its description and then the reason");
        Check(CommandBand.Tooltip("Rotate the object with the native gizmo.", "Inspect an object first.") != CommandBand.Tooltip("Move the object with the native gizmo.", "Inspect an object first."),
            "Two controls disabled for one reason still differ");
        Check(CommandBand.Tooltip(null, "Inspect an object first.") == "Inspect an object first." && CommandBand.Tooltip("", null) == "", "Without a description the reason stands alone");
        Check(CommandBand.CandidateDetailsReason(0) == "Click a marking or track in the world first." && CommandBand.CandidateDetailsReason(1) == null && CommandBand.CandidateDetailsReason(3) == null,
            "The details of a world choice open only once something was clicked");
        Check(CommandBand.NotEditable("the marker is inside a crossing") == "Not editable: the marker is inside a crossing", "An object of a supported family that cannot be edited keeps its own reason");
        Check(!CommandBand.NotEditable("x").StartsWith(InventoryPresentationState.InspectionOnlyGroup, StringComparison.Ordinal), "The refusal's wording does not use the name of the Inspection only group");
        // A hierarchy path that more than one object holds: no edit, with the reason under Supported edits.
        Check(CommandBand.SharedPath(1) == null && CommandBand.SharedPath(0) == null, "A path one object holds can be edited");
        Check(CommandBand.SharedPath(2) == "2 objects share this hierarchy path and component, so a patch cannot name one of them"
            && CommandBand.Refusal(CommandBand.NotEditable(CommandBand.SharedPath(2))) == CommandBand.SharedPath(2), "A path two objects hold reads Not editable with its reason");
        // An object without edits is not told what Disable would do.
        Check(CommandBand.TrackGroup(false) == "Activity follows the track group." && CommandBand.TrackGroup(true).Contains("Disable holds it inactive"), "The track-group line names edits only where the object offers them");
        // The help of a tab closes when another tab opens; the outliner's and the window's stay.
        var tabs = new[] { "object", "placement", "connections", "changes" };
        Check(CommandBand.ClosesHelp("object", "placement", tabs) && !CommandBand.ClosesHelp("placement", "placement", tabs), "A tab's help closes on a switch to another tab");
        Check(!CommandBand.ClosesHelp("outliner", "placement", tabs) && !CommandBand.ClosesHelp("main", "changes", tabs) && !CommandBand.ClosesHelp(null, "object", tabs), "The outliner's and the window's help stay through a tab switch");
        // The footer's row clears the resize grip: the grip is 22 high and 10 units of padding lie below the row.
        Check(CommandBand.GripClearance(22, 10) == 12 && CommandBand.GripClearance(10, 22) == 0, "The row stands as much higher as the grip reaches into it");
        // A component the game adds while it runs is no dependency.
        Check(HierarchyDependencies.IsRuntimeComponent("Helpers.Animation.PlayableGraphAnimatorAdapter") && !HierarchyDependencies.IsRuntimeComponent("RollingStock.Controls.KeyValueBoolAnimator")
            && !HierarchyDependencies.IsRuntimeComponent("UnityEngine.Animator") && !HierarchyDependencies.IsRuntimeComponent(null), "The playable-graph adapter an animator adds while running is runtime state; the animator itself is authored");
        // The help window stands beside the panel and never over it.
        var right = PanelPlacement.Beside(0, 734, 300, 364, 800, 2560, 1440, 8);
        Check(right.X == 742 && right.Y == 300 && right.Width == 364 && right.Height == 800, "With room at the right the help stands right of the panel, at the height of its ?: " + right.X);
        var left = PanelPlacement.Beside(1186, 1920, 300, 364, 600, 1920, 1080, 8);
        Check(left.X == 1186 - 8 - 364 && left.X + left.Width <= 1186, "A panel at the right edge gets its help at its left, clear of the panel: " + left.X);
        var low = PanelPlacement.Beside(0, 734, 900, 364, 800, 1920, 1080, 8);
        Check(low.Y == 280 && low.X == 742, "A help that would leave the screen moves up, not over the panel: " + low.Y);
        var both = PanelPlacement.Beside(0, 734, 100, 364, 1101, 2560, 1440, 8);
        Check(both.X >= 734 && both.Y == 100, "The outliner's help at 2560 x 1440 stands right of the whole panel, not over the outliner");
        var cramped = PanelPlacement.Beside(200, 900, 0, 364, 400, 1000, 800, 8);
        Check(cramped.X == 0, "Without room on either side it takes the side with more room, at the screen's edge: " + cramped.X);
        Check(PanelPlacement.Beside(100, 950, 0, 364, 400, 1000, 800, 8).X == 0 && PanelPlacement.Beside(100, 900, 0, 364, 400, 1000, 800, 8).X == 636, "More room at the left takes the left edge; equal room takes the right edge");
        Check(CommandBand.NotYetSupported("the Milepost family") == "Not yet supported: the patcher has no edit for the Milepost family yet" && CommandBand.Refusal(CommandBand.NotYetSupported("the Milepost family")) == null,
            "A kind without an edit says that it is not supported yet, which is no refusal of a supported family");
        // The followed track element is the one the world marks green: the anchor's node, or its location's segment.
        Check(CommandBand.Followed(null) == null, "An object that follows nothing has no followed element");
        var onSegment = CommandBand.Followed(new TrackFollowing(TrackAnchor.At(new TrackLocation("S12", "a", 3.5)), new double[3], new double[] { 0, 0, 0, 1 }));
        Check(onSegment.Kind == SelectionKind.TrackSegment && onSegment.Identifier == "S12", "A location anchor marks its segment");
        var onNode = CommandBand.Followed(new TrackFollowing(TrackAnchor.Endpoint("N7"), new double[3], new double[] { 0, 0, 0, 1 }));
        Check(onNode.Kind == SelectionKind.TrackNode && onNode.Identifier == "N7", "An endpoint anchor marks its node");
        // A group of values: caption above, short labels in front of the boxes, the unit once.
        Check(CommandBand.PlacementGroups.SequenceEqual(new[] { "Position", "Rotation" }) && CommandBand.PlacementUnits.SequenceEqual(new[] { "m", "deg" }) && CommandBand.Axes.SequenceEqual(new[] { "X", "Y", "Z" }), "A placement is a position and a rotation, each with X, Y and Z");
        foreach (var row in new[] {
            new[] { "Position", "m", "edit", "Position (m)" }, new[] { "Position", "m", "read", "Position" },
            new[] { "Rotation", "deg", "edit", "Rotation (deg)" }, new[] { "Approach A", null, "edit", "Approach A" } })
            Check(CommandBand.GroupCaption(row[0], row[1], row[2] == "edit") == row[3], "The caption of " + row[0] + " carries its unit only while the row can be edited: " + row[3]);
        Check(CommandBand.RunningLine(CommandBand.Axes, new[] { "4361.091", "528.405", "5380.431" }, "m") == "X 4361.091  Y 528.405  Z 5380.431 (m)", "A position that cannot be edited is one running line with its unit once at the end");
        Check(CommandBand.RunningLine(CommandBand.Axes, new[] { "0", "70.041", "0" }, "deg") == "X 0  Y 70.041  Z 0 (deg)", "A rotation reads the same way, in degrees");
        Check(CommandBand.RunningLine(new[] { "Segment", "End", "Distance" }, new[] { "S377", " a ", "" }, "m") == "Segment S377  End a  Distance - (m)", "A binding reads the same way; a value that is not set is a dash");
        Check(CommandBand.RunningLine(new[] { "A", "B" }, new[] { "1" }, null) == "A 1  B -", "A line without a unit ends with its last value");
        // The reason an object cannot be edited is its own, wherever it is given, as one plain sentence.
        foreach (var row in new[] {
            new[] { CommandBand.NotYetSupported("the Milepost family"), "The patcher has no edit for the Milepost family yet." },
            new[] { CommandBand.NotEditable("the marker is inside a crossing"), "The marker is inside a crossing." },
            new[] { CommandBand.NotEditable(CommandBand.SharedPath(2)) + ".", "2 objects share this hierarchy path and component, so a patch cannot name one of them." },
            new[] { "Fixed placement and activity", "This object cannot be edited here." }, new[] { null, "This object cannot be edited here." }, new[] { " ", "This object cannot be edited here." } })
            Check(CommandBand.EditReason(row[0]) == row[1], "The editing message gives the object's own reason: " + row[1] + " / " + CommandBand.EditReason(row[0]));
        Check(!CommandBand.EditReason(CommandBand.NotYetSupported("x")).Contains("Object tab"), "The editing message does not send the user to another tab");
        // No placement mode moves a crossing pad: each is inactive for a pad, with the one reason.
        foreach (var mode in new[] { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode, CommandBand.FollowMode })
            Check(CommandBand.PadModeBlock(mode) == CommandBand.PadFollowsTrack, "Placement mode " + mode + " is inactive for a crossing pad");
        Check(CommandBand.PadFollowsTrack.Contains("follows its track") && CommandBand.PadFollowsTrack.Contains("under Connections"), "The reason says the pad follows its track and where to bind it");
        Check(CommandBand.PadOperations.StartsWith("Pad binding:", StringComparison.Ordinal) && !CommandBand.PadOperations.Contains("fixed placement") && CommandBand.PadOperations.Contains("no placement of its own"), "A pad's Supported edits promises no placement");
        // An edit whose fields hold the current values is disabled.
        foreach (var row in new[] {
            new object[] { new[] { "4387.212", "528.6", "0" }, new[] { "4387.212", "528.6", "0" }, true },
            new object[] { new[] { " 4387.2120 ", "528.60", "-0" }, new[] { "4387.212", "528.6", "0" }, true },
            new object[] { new[] { "4387.213", "528.6", "0" }, new[] { "4387.212", "528.6", "0" }, false },
            new object[] { new[] { "x", "a", "7.5" }, new[] { "X", "A", "7.50" }, true },
            new object[] { new[] { "S378", "A", "7.5" }, new[] { "S377", "A", "7.5" }, false },
            new object[] { new[] { "", "A", "" }, new[] { "", "A", "" }, true },
            new object[] { new[] { "1" }, new[] { "1", "2" }, false }, new object[] { new[] { "1" }, null, false } })
            Check(CommandBand.SameFields((string[])row[0], (string[])row[1]) == (bool)row[2], "Fields " + String.Join("/", (string[])row[0]) + " equal the current values: " + row[2]);
        // A button that applies fields follows the typing: each row is the state after one more keystroke.
        var shown = new[] { "4387.212", "528.6", "0" };
        foreach (var row in new[] {
            new object[] { null, new[] { "4387.212", "528.6", "0" }, CommandBand.SamePlacement },
            new object[] { null, new[] { "4387.2125", "528.6", "0" }, null },
            new object[] { null, new[] { "4387.212", "528.6", "0" }, CommandBand.SamePlacement },
            new object[] { null, new[] { "4387.21", "528.6", "0" }, null },
            new object[] { null, new[] { "4387.212", "528.6", "" }, null },
            new object[] { null, new[] { "4387.2120", "528.60", "0.0" }, CommandBand.SamePlacement },
            new object[] { "Inspect an object first.", new[] { "1", "2", "3" }, "Inspect an object first." },
            new object[] { "Inspect an object first.", new[] { "4387.212", "528.6", "0" }, "Inspect an object first." } })
            Check(CommandBand.FieldReason((string)row[0], (string[])row[1], shown, CommandBand.SamePlacement) == (string)row[2], "With the fields " + String.Join("/", (string[])row[1]) + " the button's reason is " + (row[2] ?? "none"));
        Check(CommandBand.FieldReason(null, new[] { "S378", "A", "7.5" }, new[] { "S377", "A", "7.5" }, CommandBand.SameBinding) == null && CommandBand.FieldReason(null, new[] { "S377", "a", "7.50" }, new[] { "S377", "A", "7.5" }, CommandBand.SameBinding, CommandBand.BindingIds) == CommandBand.SameBinding, "Set follows the binding fields the same way");
        // A segment ID is compared as it is written: the game finds a segment by its exact ID. The end is a or b in
        // either case.
        Check(!CommandBand.SameFields(new[] { "s377", "A", "7.5" }, new[] { "S377", "A", "7.5" }, CommandBand.BindingIds) && CommandBand.SameFields(new[] { "S377", "a", "7.5" }, new[] { "S377", "A", "7.5" }, CommandBand.BindingIds)
            && CommandBand.FieldReason(null, new[] { "s377", "A", "7.5" }, new[] { "S377", "A", "7.5" }, CommandBand.SameBinding, CommandBand.BindingIds) == null, "A segment ID in another letter case is another segment, so Set is available; the end's case does not count");
        Check(CommandBand.BindingIds.SequenceEqual(new[] { 0 }), "The segment ID is the first binding field");
        // One form for a track location everywhere: segment, end in lower case, distance to three decimals.
        Check(new TrackLocation("S9728", "A", 74.43612).Display == "S9728 / a / 74.436" && new TrackLocation("S9728", "a", 74).Display == "S9728 / a / 74", "A track location reads S9728 / a / 74.436");
        Check(TrackAnchor.At(new TrackLocation("S9728", "a", 74.436)).Display == "S9728 / a / 74.436" && TrackAnchor.Endpoint("N7").Display == "node N7", "A followed anchor reads as a binding does, a node by its ID");
        Check(CommandBand.BindingAction("S9728", "A", 74.43612) == "Track binding to S9728 / a / 74.436", "The binding's log name uses the same form");
        foreach (var row in new[] { new[] { "end", "A", "a" }, new[] { "end", "b", "b" }, new[] { "end", null, "" }, new[] { "segment", "S9728", "S9728" } })
            Check(CommandBand.Shown(row[0], row[1]) == row[2], "The value " + row[0] + "=" + row[1] + " shows as " + row[2]);
        // Several candidates on one spot are an instruction, not an error; a miss and a refusal are problems.
        Check(PendingSelection.IsInstruction(PendingSelection.SeveralOffers) && PendingSelection.IsInstruction(PendingSelection.NoOffer) && PendingSelection.NoOffer == "No candidate at this point", "Several candidates on one spot and a click that met none read as ordinary text: nothing is broken");
        Check(!PendingSelection.IsInstruction(null) && !PendingSelection.IsInstruction("The candidate changed or no longer resolves uniquely: it is no longer found. Pick again."), "A refusal stays a problem, in red");
        // The log line of a world choice names the candidates of the clicked spot, and the object the choice is for only when it is for one.
        var offered = new[] { "Crossing light: World/Roads/Everett/Signal 1/Signal 1", "Crossing gate: World/Roads/Everett/Signal 1/Signal 1/Wishbone" };
        var logged = CommandBand.ChoiceLog(null, PendingSelection.SeveralOffers, offered);
        Check(logged.StartsWith("[World choice] ", StringComparison.Ordinal) && logged.Contains(PendingSelection.SeveralOffers + " Candidates: 1 ") && logged.Contains("; 2 ") && logged.Contains("Wishbone") && !logged.Contains("World/Signs"), "A choice that only inspects logs the spot's candidates and no object path: " + logged);
        Check(CommandBand.ChoiceLog("World/Signs/Whistle Sign E", "No candidate at this point", new string[0]) == CommandBand.LogLine("World choice", "World/Signs/Whistle Sign E", "No candidate at this point"), "A choice for an object names that object; one candidate or none adds no list");
        // A shown marker takes its click before another candidate's label; markings on one spot are drawn neutral first.
        Check(CommandBand.MarkerBeforeLabel(true) && !CommandBand.MarkerBeforeLabel(false), "A click on a shown square marker reaches that marker");
        Check(CommandBand.DrawRank(false, false) < CommandBand.DrawRank(true, false) && CommandBand.DrawRank(true, false) < CommandBand.DrawRank(true, true) && CommandBand.DrawRank(false, true) == CommandBand.DrawRank(true, true), "A neutral marker is drawn first, a candidate over it, a pointed-at one last");
        Check(CommandBand.InactiveAlpha == .4f && CommandBand.RowFill > 0 && CommandBand.RowFill < 1, "An inactive label is dimmed to the native 0.4; the hovered list row is filled");
        // The result of an edit says what happened.
        foreach (var row in new[] {
            new object[] { false, false, null, "Enable completed." },
            new object[] { true, false, null, "Enable: nothing changed; the object has this value already." },
            new object[] { false, true, null, "Enable completed. The value equals the object's value without this MapMod, so the modification no longer sets it." },
            new object[] { false, false, "Enable did something else.", "Enable did something else." },
            new object[] { true, true, "x", "Enable: nothing changed; the object has this value already." } })
            Check(CommandBand.EditResult("Enable", (bool)row[0], (bool)row[1], (string)row[2]) == (string)row[3], "An edit's result reads: " + row[3]);
        // One rule for every shown number: three decimals, no trailing zeros, never scientific notation, no negative zero.
        foreach (var row in new[] { new object[] { 528.405334, "528.405" }, new object[] { 70.04144, "70.041" }, new object[] { 1.13392689E-05, "0" }, new object[] { -1.13392689E-05, "0" }, new object[] { 25692.9766, "25692.977" },
            new object[] { 0.0, "0" }, new object[] { 334.5, "334.5" }, new object[] { 1e21, "1000000000000000000000" }, new object[] { -0.0006, "-0.001" } })
            Check(CommandBand.Number((double)row[0]) == (string)row[1] && !CommandBand.Number((double)row[0]).Contains("E"), "A number reads " + row[1] + ", got " + CommandBand.Number((double)row[0]));
        // A field that still shows the object's value stands for the exact value; a changed field for what was entered.
        Check(CommandBand.Entered("528.405", 528.405334, out var kept) && kept == 528.405334, "An untouched field keeps the exact value");
        Check(CommandBand.Entered(" 528.41 ", 528.405334, out var typed) && typed == 528.41, "A changed field gives the entered number");
        Check(CommandBand.Entered("0", 1.13392689E-05, out var tiny) && tiny == 1.13392689E-05, "A field that shows 0 for a tiny value keeps the tiny value");
        Check(!CommandBand.Entered("abc", 1, out _) && !CommandBand.Entered("", 1, out _), "Text that is no number is refused");
        foreach (var row in new[] { new[] { "distance", "7.51166105", "7.512" }, new[] { "from", "1.5E-05", "0" }, new[] { "to", "12", "12" }, new[] { "id", "1E5", "1E5" }, new[] { "segment", "377", "377" }, new[] { "distance", "none", "none" } })
            Check(CommandBand.Shown(row[0], row[1]) == row[2], "A binding value " + row[0] + "=" + row[1] + " shows as " + row[2]);
        foreach (var row in new[] { new[] { "approachA", "Approach A" }, new[] { "island", "Island" }, new[] { "approachB", "Approach B" }, new[] { "other", "other" } })
            Check(CommandBand.RoleCaption(row[0]) == row[1], "The role " + row[0] + " reads " + row[1]);
        // Placement modes per object: a pad has none, an object that cannot be edited keeps Numeric only.
        var why = CommandBand.NotYetSupported("the Milepost family");
        foreach (var mode in new[] { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode, CommandBand.FollowMode }) {
            Check(CommandBand.ModeBlock(mode, false, true, "Fixed placement") == null, "Mode " + mode + " is open for an object that can be edited");
            Check(CommandBand.ModeBlock(mode, true, true, "x") == CommandBand.PadFollowsTrack, "Mode " + mode + " is inactive for a pad");
            Check(CommandBand.ModeBlock(mode, false, false, why) == (mode == CommandBand.NumericMode ? null : "The patcher has no edit for the Milepost family yet."), "Mode " + mode + " of an object that cannot be edited: only Numeric stays, the others give the object's reason");
        }
        Check(CommandBand.ShowsCurrentRelations(false) && !CommandBand.ShowsCurrentRelations(true), "A confirmed target that is not yet used is the one green relation");
        // A candidate among several offers carries one number everywhere.
        foreach (var row in new[] { new object[] { "Choice", 2, 3, "Choice 2" }, new object[] { "New", 1, 3, "New 1" }, new object[] { "Choose", 3, 3, "Choose 3" }, new object[] { "Candidate", 1, 1, "Candidate" }, new object[] { "Choice", 0, 3, "Choice" } })
            Check(CommandBand.ChoiceTag((string)row[0], (int)row[1], (int)row[2]) == (string)row[3], "A candidate is named " + row[3]);
        Check(new[] { CommandBand.ClearActivity, CommandBand.DiscardPrepared, CommandBand.RemoveModification }.Distinct().Count() == 3, "The three controls that take something back have three names");
    }
    // The labels of overlapping candidates differ at their end, which is the object's own name: the short label leads
    // with it, and a cut falls on the family.
    static void ChoiceLabelsLeadWithWhatTellsThemApart() {
        var offered = new[] {
            "Track marker: World/Large Scenery/Whittier/Whittier Signs/wh-e-water",
            "Track marker: World/Large Scenery/Whittier/Whittier Signs/wh-e-whistle [inactive]",
            "Independent board: World/Signs/Loc: Walker Branch" };
        Check(CommandBand.ChoiceLabel(offered[0], offered) == "wh-e-water (Track marker)", "The name comes first: " + CommandBand.ChoiceLabel(offered[0], offered));
        Check(CommandBand.ChoiceLabel(offered[1], offered) == "wh-e-whistle [inactive] (Track marker)", "An inactive object says so after its name: " + CommandBand.ChoiceLabel(offered[1], offered));
        Check(CommandBand.ChoiceLabel(offered[2], offered) == "Loc: Walker Branch (Independent board)", "A colon inside a name stays in the name: " + CommandBand.ChoiceLabel(offered[2], offered));
        var twins = new[] { "Crossing pad marker: World/Roads Wilmot/Crossing Unprotected/Crossing Pad", "Crossing pad marker: World/Roads Sylva/Crossing Unprotected (1)/Crossing Pad" };
        Check(CommandBand.ChoiceLabel(twins[0], twins) == "Crossing Unprotected/Crossing Pad (Cr..." && CommandBand.ChoiceLabel(twins[1], twins) == "Crossing Unprotected (1)/Crossing Pad...",
            "Equal names take their parents until they differ, and the cut falls at the end: " + CommandBand.ChoiceLabel(twins[0], twins) + " | " + CommandBand.ChoiceLabel(twins[1], twins));
        Check(CommandBand.ChoiceLabel(twins[0], twins) != CommandBand.ChoiceLabel(twins[1], twins), "The two labels differ");
        Check(CommandBand.ChoiceLabel("Track segment Sdb78", new[] { "Track segment Sdb78", "Track segment S9728" }) == "Track segment Sdb78", "A track's label is kept");
        Check(CommandBand.ChoiceLabel(offered[0], offered, 12) == "wh-e-wate...", "A label longer than its room is cut at the end");
        Check(CommandBand.ChoiceLabel(null, offered) == "", "No label gives none");
    }
    static void AClickOnTheInspectedRowClearsTheInspection() {
        object inspected = new object(), other = new object();
        Check(CommandBand.ClearsInspection(inspected, inspected), "A click on the inspected row clears the inspection");
        Check(!CommandBand.ClearsInspection(inspected, other), "A click on another row inspects that row");
        Check(!CommandBand.ClearsInspection(null, other) && !CommandBand.ClearsInspection(null, null), "With nothing inspected a click inspects");
    }
    static void ClearActivityNeedsAnActivityValue() {
        Check(CommandBand.ClearActivityReason(true) == null, "With an activity value Clear activity is enabled");
        Check(CommandBand.ClearActivityReason(false) == "This MapMod sets no activity for this object, so there is nothing to clear.", "Without one it is disabled with that reason");
    }
    // A request waits for the comparison of the scene. Closing the panel or leaving the native map editor cancels it: it never
    // runs, and a refusal says why.
    // The rules that every window, tab and row of the panel shares: one table per rule, over all call sites.
    static void SharedRulesHoldAtEveryCallSite() {
        // Button padding: every button of every row is as wide as its widest label plus the native padding on both
        // sides, and every row fits the inspector's minimum with its full labels.
        Check(CommandBand.ButtonPadding == 6 && CommandBand.ButtonWidth(62.15f) == 75, "A button is its label and 6 units of padding on each side, rounded up: " + CommandBand.ButtonWidth(62.15f));
        foreach (var row in CommandBand.ActionRows) {
            var widths = new System.Collections.Generic.List<float>();
            foreach (var button in row) {
                foreach (var label in button) Check(CommandBand.LabelWidths.ContainsKey(label), "Every label of an action row has a measured width: " + label);
                var width = CommandBand.ButtonWidth(button.Select(label => CommandBand.LabelWidths[label]).ToArray());
                foreach (var label in button) {
                    var free = width - CommandBand.LabelWidths[label];
                    Check(free >= 2 * CommandBand.ButtonPadding, "No label overflows its button: " + label + " in " + width);
                    if (button.Length == 1) Check(free < 2 * CommandBand.ButtonPadding + 1, "A button with one label has no more than its padding around it: " + label + " in " + width);
                }
                widths.Add(width);
            }
            Check(CommandBand.RowFits(widths), "Every action row fits the inspector's minimum with its full labels: " + String.Join(", ", row.Select(button => button[0])) + " = " + widths.Sum());
        }
        Check(CommandBand.ButtonWidth(CommandBand.LabelWidths["Disable"], CommandBand.LabelWidths["Enable"]) == CommandBand.ButtonWidth(CommandBand.LabelWidths["Disable"]), "A button whose label changes takes its widest label, so the change moves nothing");
        Check(!CommandBand.RowFits(new[] { 200f, 150f }) && CommandBand.RowFits(new[] { 200f, 136f }) && CommandBand.ShortLabel(CommandBand.RemoveModification) == "Remove" && CommandBand.ShortLabel("Recheck") == "Recheck",
            "A row that would not fit takes the shorter label where a button has one");
        // World colours: one table for every marking.
        float Max(float[] c) => Math.Max(c[0], Math.Max(c[1], c[2]));
        float Min(float[] c) => Math.Min(c[0], Math.Min(c[1], c[2]));
        float Saturation(float[] c) => (Max(c) - Min(c)) / Max(c);
        float Lightness(float[] c) => (Max(c) + Min(c)) / 2;
        var hover = CommandBand.WorldColor(CommandBand.WorldMark.CandidateHover); var chosen = CommandBand.WorldColor(CommandBand.WorldMark.CandidateSelected);
        var relation = CommandBand.WorldColor(CommandBand.WorldMark.Relation); var relationHover = CommandBand.WorldColor(CommandBand.WorldMark.RelationHover);
        Check(hover[2] == Max(hover) && chosen[2] == Max(chosen), "A candidate is blue, under the pointer and after a click");
        Check(Saturation(hover) > Saturation(chosen) && Lightness(chosen) > Lightness(hover), "The hovered candidate is more saturated and the clicked one lighter");
        Check(relation[1] == Max(relation) && relationHover[1] == Max(relationHover) && Saturation(relationHover) > Saturation(relation), "A current relation is green, more saturated under the pointer");
        var gold = CommandBand.WorldColor(CommandBand.WorldMark.Inspected);
        Check(gold[0] == 1 && gold[2] == 0 && CommandBand.WorldColor(CommandBand.WorldMark.Broken)[0] == 1, "The inspected object is gold and a broken item red");
        // Which state has which colour, at every kind of marking: a hovered candidate is blue, never green.
        Check(CommandBand.CandidateMark(false, true) == CommandBand.WorldMark.CandidateHover && CommandBand.CandidateMark(true, false) == CommandBand.WorldMark.CandidateSelected
            && CommandBand.CandidateMark(true, true) == CommandBand.WorldMark.CandidateSelected && CommandBand.CandidateMark(false, false) == CommandBand.WorldMark.Marker, "A candidate is blue on hover and after a click until Confirm, and a plain marker otherwise");
        Check(CommandBand.RelationMark(false) == CommandBand.WorldMark.Relation && CommandBand.RelationMark(true) == CommandBand.WorldMark.RelationHover, "A current relation is green, hovered or not");
        Check(CommandBand.WorldFill == .22f, "Every ribbon has one fill");
        // Markers in a pick mode: every candidate of the mode, without hover; tracks within a distance.
        Check(CommandBand.ShowsMarker(false, 5000, false) && CommandBand.ShowsMarker(true, 499, false) && !CommandBand.ShowsMarker(true, 501, false) && CommandBand.ShowsMarker(true, 5000, true),
            "Every candidate carries its marker without hover; a track's within 500 m, or when hovered or clicked");
        // The placement mode an object opens in.
        Check(CommandBand.PlacementMode(true) == CommandBand.FollowMode && CommandBand.PlacementMode(false) == CommandBand.NumericMode && CommandBand.FollowMode == 3 && CommandBand.GizmoMode == 1, "An object that follows a track opens in Follow, any other in Numeric");
        // No false success for a crossing pad's placement.
        var pad = CommandBand.PadPlacement("Gizmo placement");
        Check(pad.StartsWith("Gizmo placement changed the pad object's pose only.") && !pad.Contains("completed") && pad.Contains("did not move"), "A pad's placement result says that the pad did not move: " + pad);
        // Terrain output in plain words.
        Check(CommandBand.Terrain(null, null, true, "none", "none", false) == "Terrain unchanged." && CommandBand.Terrain(null, null, true, "tile 3", "none", true) == "Terrain updated: tile 3."
            && CommandBand.Terrain(null, null, false, "none", "tile 4", false).StartsWith("Terrain pending: ") && CommandBand.Terrain(null, "no tile", true, "a", "b", true).StartsWith("Terrain update stopped: no tile")
            && CommandBand.Terrain("unreadable", null, true, "", "", false) == "Terrain state unknown: unreadable", "Terrain output reads in plain words in every state");
        // Log lines name the action group, the object and the value.
        Check(CommandBand.LogLine("Track binding", "World/Signs/Yard Limit", "Track binding to S377 / a / 7.512 completed.") == "[Track binding] World/Signs/Yard Limit: Track binding to S377 / a / 7.512 completed."
            && CommandBand.LogLine("World choice", null, "No candidate at this point") == "[World choice] No candidate at this point" && CommandBand.LogLine(null, null, "x") == "x", "A log line names the action group and the object");
        Check(CommandBand.BindingAction("S377", "a", 7.5116) == "Track binding to S377 / a / 7.512", "A binding edit names the track location it sets");
    }
    static void AClosedPanelCancelsTheWaitingRequest() {
        var feedback = new EditFeedback(); var ran = 0;
        var queue = new RequestQueue(feedback);
        Check(!queue.Pending && queue.Place == null && !queue.Cancel("the panel was closed before the request ran"), "Nothing waits at first, and cancelling nothing reports nothing");
        Check(feedback.Messages.Count == 0, "No refusal without a request");
        queue.Enqueue("Activity", "Disable", () => ran++);
        Check(queue.Pending && queue.Place == "Activity", "A request waits under its action group");
        Check(queue.Cancel("the panel was closed before the request ran") && !queue.Pending && queue.Place == null, "Closing the panel cancels it");
        Check(ran == 0 && feedback.Result == "Disable refused. The panel was closed before the request ran." && feedback.Place == "Activity", "The cancelled request did not run and left its refusal: " + feedback.Result);
        queue.Run();
        Check(ran == 0, "Running after the cancellation runs nothing");
        // A request that is not cancelled runs once.
        queue.Enqueue("Crossing coverage", "Coverage preview", () => ran++);
        var refused = false;
        try { queue.Enqueue("Activity", "Enable", () => ran += 10); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "A second request is not taken while one waits");
        queue.Run(); queue.Run();
        Check(ran == 1 && !queue.Pending, "A waiting request runs once");
        // Leaving the native map editor.
        queue.Enqueue("Crossing coverage", "Coverage preview", () => ran++);
        queue.Cancel("the native map editor was left before the request ran");
        Check(ran == 1 && feedback.Result == "Coverage preview refused. The native map editor was left before the request ran.", "Leaving the native map editor cancels a preview too: " + feedback.Result);
        // Clearing the inspection drops the object's results and keeps the refusal of the request that waited for it.
        var board = new object(); var other = new object();
        feedback.Inspecting(board);
        feedback.Report("Activity", "Disable completed.");
        queue.Enqueue("Activity", "Enable", () => ran++);
        Check(queue.Release("the inspection was cleared before the request ran") && !queue.Pending, "Clearing the inspection cancels the waiting request");
        Check(ran == 1 && feedback.Messages.Count == 1 && feedback.Result == "Enable refused. The inspection was cleared before the request ran.",
            "Only the refusal is left after the inspection was cleared: " + String.Join(" / ", feedback.Messages.Select(m => m.Text)));
        feedback.Inspecting(null);
        Check(feedback.Messages.Count == 1, "The refusal stays while nothing is inspected");
        // Without a waiting request, clearing the inspection leaves nothing.
        feedback.Inspecting(board);
        Check(feedback.Messages.Count == 0, "The refusal goes when an object is inspected again");
        feedback.Report("Activity", "Disable completed.");
        Check(!queue.Release("the inspection was cleared before the request ran") && feedback.Messages.Count == 0, "A cleared inspection without a request leaves no message");
        feedback.Inspecting(other);
        feedback.Report("Activity", "Enable completed.");
        Check(feedback.Messages.Count == 1, "Messages work as before for the next object");
        // Clearing the inspection while a world choice is pending cancels the choice first, through its own Cancel,
        // then restores what was prepared, and the refusal of the waiting request is still the one line left.
        var steps = new System.Collections.Generic.List<string>();
        queue.Enqueue("Activity", "Disable", () => ran++);
        Check(queue.ReleaseInspection(true, () => steps.Add("cancel choice"), () => steps.Add("restore"), "the inspection was cleared before the request ran"), "A waiting request is reported as cancelled");
        Check(steps.SequenceEqual(new[] { "cancel choice", "restore" }), "A pending world choice is cancelled before the rest is restored: " + String.Join(", ", steps));
        Check(ran == 1 && feedback.Messages.Count == 1 && feedback.Result == "Disable refused. The inspection was cleared before the request ran.", "The refusal is reported after the choice was cancelled: " + feedback.Result);
        // The kept refusal stays through a tab switch and a history move, until an object is inspected.
        feedback.View("object/0"); feedback.View("placement/1"); feedback.HistoryMoved();
        Check(feedback.Messages.Count == 1 && feedback.Messages[0].Kept, "The refusal stays readable until the next inspection");
        feedback.Inspecting(board);
        Check(feedback.Messages.Count == 0, "The next inspection ends it");
        // Clear in the band with nothing inspected and a world choice pending cancels that choice and reports nothing.
        steps.Clear();
        Check(!queue.ReleaseInspection(true, () => steps.Add("cancel choice"), () => steps.Add("restore"), "unused") && steps.SequenceEqual(new[] { "cancel choice", "restore" }) && feedback.Messages.Count == 0,
            "Clear cancels a pending world choice also when nothing is inspected");
        steps.Clear();
        Check(!queue.ReleaseInspection(false, () => steps.Add("cancel choice"), () => steps.Add("restore"), "unused") && steps.SequenceEqual(new[] { "restore" }) && feedback.Messages.Count == 0,
            "Without a pending choice or a request, only the restore runs and nothing is reported");
    }
    static void TheWindowOpensBelowTheMapEditorOrAtTheTopRight() {
        // 2560 x 1440, scale 1: the Map Editor window ends at 666, so 774 units remain below it.
        var roomy = PanelPlacement.Default(2560, 1440, 1, 0, 604, 666, 60, 690, 660, 410);
        Check(!roomy.TopRight && roomy.X == 0 && roomy.Y == 666 && roomy.Width == 690 && roomy.Height == 774, "With 600 units or more below the Map Editor the window fills that space: " + roomy.Y + " " + roomy.Height);
        // 1920 x 1080, scale 1: 414 units remain below, fewer than 600.
        var small = PanelPlacement.Default(1920, 1080, 1, 0, 604, 666, 60, 690, 660, 410);
        Check(small.TopRight && small.X == 1920 - 690 && small.Y == 60 && small.Width == 690 && small.Height == 660, "With fewer than 600 units below, the window opens at the top right below the strip, with the same width: " + small.X + " " + small.Y + " " + small.Height);
        // 1920 x 1080, scale 1.25: one canvas unit is 1.25 pixels. The Map Editor ends at 832; 248 pixels remain.
        var scaled = PanelPlacement.Default(1920, 1080, 1.25f, 0, 755, 832, 75, 690, 660, 410);
        Check(scaled.TopRight && scaled.Width == 862.5f && scaled.X == 1920 - 862.5f && scaled.Y == 75 && scaled.Height == 825, "At the larger scale the sizes are canvas units times the scale: " + scaled.Width + " " + scaled.Height);
        // The strip leaves less than the default height: the window takes what remains, never less than its minimum.
        var low = PanelPlacement.Default(1280, 720, 1, 0, 604, 500, 60, 690, 660, 410);
        Check(low.TopRight && low.Y == 60 && low.Height == 660, "The remaining height below the strip bounds the window: " + low.Height);
        var tiny = PanelPlacement.Default(1280, 430, 1, 0, 604, 400, 60, 690, 660, 410);
        Check(tiny.TopRight && tiny.Height == 410 && tiny.Y == 20, "The window keeps its minimum height and stays on the screen: " + tiny.Y + " " + tiny.Height);
        // Exactly 600 units below stays below the Map Editor.
        var edge = PanelPlacement.Default(1920, 1266, 1, 0, 604, 666, 60, 690, 660, 410);
        Check(!edge.TopRight && edge.Height == 600, "600 units below is enough");
        // A Map Editor window wider than the default sets the width in both placements.
        var wide = PanelPlacement.Default(1920, 1080, 1, 0, 800, 666, 60, 690, 660, 410);
        Check(wide.TopRight && wide.Width == 800 && wide.X == 1120, "The window keeps the width it would have below the Map Editor");
        // Without a Map Editor window the default size stands at the left.
        var none = PanelPlacement.Default(1920, 1080, 1, 0, 0, 0, 60, 690, 660, 410);
        Check(!none.TopRight && none.X == 0 && none.Width == 690 && none.Height == 660 && none.Y == 420, "Without a Map Editor window the default size is used");
        // The corner's lower edge: the strip, the readout below it, not a window elsewhere or a rectangle further down.
        var corner = new[] {
            new PanelPlacement.Area { X = 1500, Y = 0, Width = 420, Height = 40 },
            new PanelPlacement.Area { X = 1700, Y = 40, Width = 220, Height = 24 },
            new PanelPlacement.Area { X = 0, Y = 0, Width = 604, Height = 666 },
            new PanelPlacement.Area { X = 1600, Y = 300, Width = 200, Height = 100 } };
        Check(PanelPlacement.OccupiedBelow(1230, 1920, corner, 400) == 64, "The edge is below the strip and the readout: " + PanelPlacement.OccupiedBelow(1230, 1920, corner, 400));
        Check(PanelPlacement.OccupiedBelow(1230, 1920, new PanelPlacement.Area[0], 400) == 0, "An empty corner starts at the top");
    }
}
