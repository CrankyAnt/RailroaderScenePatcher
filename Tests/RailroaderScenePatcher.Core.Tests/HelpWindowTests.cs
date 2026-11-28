// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using RailroaderScenePatcher;

static class HelpWindowTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;
    internal static void Run() {
        TheWheelMovesAsTheGamesScrollViewsDo();
        TheWheelReachesTheEndOfTheContent();
        AHelpNeverCoversTheMapEditorsTitleBar();
        EveryHelpOpensWithTheSameWidthAndPosition();
        ObjectHelpExplainsSupportedEditsAndReasonGroups();
        ConnectionsHelpExplainsWhichEquipmentChoicesAppear();
        Console.WriteLine("PASS: help windows scroll at the game's wheel step, keep off the Map Editor's title bar and open with one width and position");
    }

    static void ObjectHelpExplainsSupportedEditsAndReasonGroups([CallerFilePath] string path = "") {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "Src", "RailroaderScenePatcher", "ScenePatcherLayout.cs"));
        var start = source.IndexOf("[ObjectTab] = new HelpText", StringComparison.Ordinal);
        var end = source.IndexOf("[PlacementTab] = new HelpText", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "The Object tab has context help");
        var words = WindowWordsTests.Literals(source.Substring(start, end - start));
        var headings = new[] { "Supported edits", "Placement limits", "Activity set by the game", "Edit elsewhere", "Not supported", "Blocked for now" };
        var previous = -1;
        foreach (var heading in headings) {
            var index = words.IndexOf(heading);
            Check(index > previous && words.Count(word => word == heading) == 1, "Object help names each reason group once in order: " + heading);
            previous = index;
        }
        Check(words[words.IndexOf("Supported edits") + 1].Contains("Hover over a disabled control to see the same reason."), "Supported edits explains the disabled control's reason");
        var activity = words[words.IndexOf("Activity set by the game") + 1];
        Check(activity.Contains("When available, Clear activity"), "Track group help does not offer Clear activity for every object whose activity follows a track group");
        var elsewhere = words[words.IndexOf("Edit elsewhere") + 1];
        Check(elsewhere.Contains("Edit a marker on its milepost or crossing pad.") && !elsewhere.Contains("when its reason names one"), "Help names both marker owners directly");
        Check(elsewhere.Contains("Edit construction segments on their group."), "Help directs construction segment edits to the group");
        Check(words[words.IndexOf("Blocked for now") + 1].Contains("related object"), "Blocked for now includes related object states as well as failed reads");
    }

    static void ConnectionsHelpExplainsWhichEquipmentChoicesAppear([CallerFilePath] string path = "") {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "Src", "RailroaderScenePatcher", "ScenePatcherLayout.cs"));
        var start = source.IndexOf("[ConnectionsTab] = new HelpText", StringComparison.Ordinal);
        var end = source.IndexOf("[ChangesTab] = new HelpText", start, StringComparison.Ordinal);
        var words = WindowWordsTests.Literals(source.Substring(start, end - start));
        foreach (var topic in new[] { "Lights and gates", "Bells" }) {
            var index = words.IndexOf(topic);
            Check(index >= 0 && words[index + 1].Contains("The list shows only usable choices."), topic + " help explains which choices appear");
            Check(words[index + 1].Contains(topic == "Bells" ? "it appears once." : "A source shared by several crossings appears once."), topic + " help explains that shared sources appear once");
        }
    }

    static void TheWheelMovesAsTheGamesScrollViewsDo() {
        Check(CommandBand.WheelStep(true, false) == 20, "On Windows a notch moves 20 units, about a text line");
        Check(CommandBand.WheelStep(false, true) == 2, "On macOS a notch moves 2 units");
        Check(CommandBand.WheelStep(false, false) == 1, "Elsewhere a notch moves 1 unit");
    }

    static void TheWheelReachesTheEndOfTheContent() {
        // The outliner's help at 1.25: 1100 units of content in a view of 880, so 220 units are hidden.
        var offset = 0f;
        for (var notch = 0; notch < 30; notch++) offset = CommandBand.WheelOffset(offset, -1, 20, 1100, 880);
        Check(Near(offset, 220), "Wheeling down reaches the content's end and stops there");
        Check(Near(CommandBand.WheelOffset(42, -1, 20, 1100, 880), 62), "One notch down moves one step");
        Check(Near(CommandBand.WheelOffset(10, 1, 20, 1100, 880), 0), "Wheeling up stops at the top");
        Check(Near(CommandBand.WheelOffset(0, -1, 20, 600, 880), 0), "Content that fits does not scroll");
    }

    static void AHelpNeverCoversTheMapEditorsTitleBar() {
        // UI scale 1.25 on a 2560 x 1440 screen: the panel stands right of the Map Editor, the help goes left of the
        // panel at the screen's full height, over the Map Editor's title bar (0, 0, 750 x 36).
        var bar = new PanelPlacement.Area { X = 0, Y = 0, Width = 750, Height = 36 };
        var help = PanelPlacement.Beside(995, 1900, 20, 780, 1440, 2560, 1440, 10, bar);
        Check(Near(help.X, 995 - 10 - 780), "The help stays left of the panel");
        Check(Near(help.Y, 46) && Near(help.Height, 1440 - 46), "The help starts below the title bar and is no higher than the screen leaves");
        var right = PanelPlacement.Beside(100, 900, 20, 400, 600, 2560, 1440, 10, bar);
        Check(Near(right.X, 910) && Near(right.Y, 20) && Near(right.Height, 600), "A help clear of the title bar keeps its place and height");
        var none = PanelPlacement.Beside(995, 1900, 20, 780, 1440, 2560, 1440, 10, null);
        Check(Near(none.Y, 0) && Near(none.Height, 1440), "Without a Map Editor window the help keeps its place");
        var below = PanelPlacement.Beside(995, 1900, 300, 780, 600, 2560, 1440, 10, bar);
        Check(Near(below.Y, 300) && Near(below.Height, 600), "A help that starts below the title bar is not moved");
    }

    // The Placement and Changes helps opened narrow and low beside their "?", the outliner's and the Connections help wide at
    // the top. Every help now opens with one width and at one place beside the panel, whichever "?" opens it; only its
    // height follows its content, within the screen, and a help too long for the screen scrolls.
    static void EveryHelpOpensWithTheSameWidthAndPosition() {
        var placement = PanelPlacement.HelpSize(width => 340f * 600 / width, 1440, 60, 120);
        var outliner = PanelPlacement.HelpSize(width => 340f * 3000 / width, 1440, 60, 120);
        Check(Near(placement.Width, outliner.Width) && Near(placement.Width, 610), "A short and a long help take the same width: " + placement.Width + " and " + outliner.Width);
        Check(Near(placement.Height, 340f * 600 / 610) && Near(outliner.Height, 1440 - 60 - 2 * PanelPlacement.HelpMargin), "Each help is as high as its content, within the screen: " + placement.Height + " and " + outliner.Height);
        var low = PanelPlacement.HelpPlace(0, 734, 634, placement.Height + 60, 2560, 1440, 8, null);
        var tall = PanelPlacement.HelpPlace(0, 734, 634, outliner.Height + 60, 2560, 1440, 8, null);
        Check(Near(low.X, tall.X) && Near(low.Y, tall.Y) && Near(low.X, 742) && Near(low.Y, 8), "A short and a tall help open at the same place right of the panel, at the top: " + low.X + "," + low.Y + " and " + tall.X + "," + tall.Y);
        var bar = new PanelPlacement.Area { X = 700, Y = 0, Width = 600, Height = 36 };
        var below = PanelPlacement.HelpPlace(0, 734, 634, placement.Height + 60, 2560, 1440, 8, bar);
        var belowTall = PanelPlacement.HelpPlace(0, 734, 634, outliner.Height + 60, 2560, 1440, 8, bar);
        Check(Near(below.Y, belowTall.Y) && Near(below.Y, 44), "Below the Map Editor's title bar every help starts at the same height: " + below.Y + " and " + belowTall.Y);
    }
}
