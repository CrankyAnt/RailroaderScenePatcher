// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RailroaderScenePatcher;

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
        Console.WriteLine("PASS: status lamps, band commands, row markers, state badges and default window placement");
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
        Check(CommandBand.Focus(false) == "Inspect an object first." && CommandBand.CopyId(false) == "Inspect an object first.", "Focus and Copy ID are disabled when nothing is inspected, with that reason");
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
    // The supplied definitions are stale when a patch document changed outside the editor, not when the patcher saved
    // it itself, and a refresh makes them current again. The lamp follows: Current, Stale, Current.
    static void OwnSaveKeepsTheDefinitionsCurrent() {
        RailroaderScenePatcher.Core.LoadedPack Pack(string id, string json, string error = null) =>
            new RailroaderScenePatcher.Core.LoadedPack(new RailroaderScenePatcher.Core.PatchSource(id, id, "1.0"), json, null, true, error);
        const string before = "{\"modifications\":[]}", written = "{\"modifications\":[{\"id\":\"board\",\"active\":false}]}", outside = "{\"modifications\":[{\"id\":\"board\",\"active\":true}]}";
        var definitions = new SuppliedDefinitions();
        Check(definitions.Stale && CommandBand.Inventory(definitions.Stale).Word == "Stale", "Before the first refresh the definitions are not read");
        definitions.Read(new[] { Pack("pack", before) });
        definitions.Compare(new[] { Pack("pack", before) });
        Check(!definitions.Stale && CommandBand.Inventory(definitions.Stale).Word == "Current", "After a refresh the unchanged document is current");
        // The patcher saves: the file now holds what it wrote.
        definitions.Written("pack", written);
        definitions.Compare(new[] { Pack("pack", written) });
        Check(!definitions.Stale, "The patcher's own Save keeps the definitions current");
        definitions.Compare(new[] { Pack("pack", written) });
        Check(!definitions.Stale, "and they stay current on every later comparison");
        // The file is changed outside the editor.
        definitions.Compare(new[] { Pack("pack", outside) });
        Check(definitions.Stale && CommandBand.Inventory(definitions.Stale).Word == "Stale", "A document changed outside the editor marks the definitions stale");
        definitions.Compare(new[] { Pack("pack", written) });
        Check(definitions.Stale, "Changing the file back does not clear it: only a refresh does");
        definitions.Read(new[] { Pack("pack", outside) });
        Check(!definitions.Stale && CommandBand.Inventory(definitions.Stale).Word == "Current", "Refresh returns Current");
        // The first Save creates the document: no pack carried one at the refresh.
        var fresh = new SuppliedDefinitions();
        fresh.Read(new RailroaderScenePatcher.Core.LoadedPack[0]);
        fresh.Written("pack", written);
        fresh.Compare(new[] { Pack("pack", written) });
        Check(!fresh.Stale, "A document the patcher's own Save created is current");
        fresh.Compare(new[] { Pack("pack", written), Pack("other", before) });
        Check(fresh.Stale, "A document that appears from elsewhere marks them stale");
        // A document that can no longer be read, and one removed outside the editor.
        definitions.Compare(new[] { Pack("pack", null, "Cannot read patch document") });
        Check(definitions.Stale, "An unreadable document marks them stale");
        definitions.Read(new[] { Pack("pack", outside) });
        definitions.Compare(new RailroaderScenePatcher.Core.LoadedPack[0]);
        Check(definitions.Stale, "A removed document marks them stale");
        // A write while already stale does not vouch for what else changed.
        definitions.Read(new[] { Pack("pack", before) });
        definitions.Compare(new[] { Pack("pack", outside) });
        definitions.Written("pack", written);
        definitions.Compare(new[] { Pack("pack", written) });
        Check(definitions.Stale, "A Save after an outside change leaves the definitions stale until Refresh");
        // A new load attempt starts over, as before the first refresh.
        definitions.Read(new[] { Pack("pack", before) });
        definitions.Reset();
        Check(definitions.Stale, "After a reset the definitions are not read");
        definitions.Written("pack", written);
        definitions.Compare(new[] { Pack("pack", written) });
        Check(definitions.Stale, "A write does not stand in for the refresh a new load attempt needs");
        var messages = new EditFeedback();
        messages.Report("Placement", "Placement completed."); messages.Follow("Terrain output", "Terrain output complete.");
        messages.Clear();
        Check(messages.Messages.Count == 0 && messages.Result == "", "A new load attempt starts without messages");
        // The inventory notice names the supplied definitions for this state.
        Check(InventoryPresentationState.Notice(null, true).Contains("inspection is unavailable"), "Without an inventory the notice names that first");
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
