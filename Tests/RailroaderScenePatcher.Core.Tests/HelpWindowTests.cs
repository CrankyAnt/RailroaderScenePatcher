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
        AHelpFollowsItsQuestionMarkThroughAScaleChange();
        ObjectHelpExplainsSupportedEditsAndReasonGroups();
        Console.WriteLine("PASS: help windows scroll at the game's wheel step, keep off the Map Editor's title bar and follow a UI scale change");
    }

    static void ObjectHelpExplainsSupportedEditsAndReasonGroups([CallerFilePath] string path = "") {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "Src", "RailroaderScenePatcher", "ScenePatcherLayout.cs"));
        var start = source.IndexOf("[ObjectTab] = new HelpText", StringComparison.Ordinal);
        var end = source.IndexOf("[PlacementTab] = new HelpText", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "The Object tab has context help");
        var words = WindowWordsTests.Literals(source.Substring(start, end - start));
        var headings = new[] { "Supported edits", "Placement fixed or set elsewhere", "The game controls its activity", "Edited elsewhere", "Not supported", "Blocked for now" };
        var previous = -1;
        foreach (var heading in headings) {
            var index = words.IndexOf(heading);
            Check(index > previous && words.Count(word => word == heading) == 1, "Object help names each reason group once in order: " + heading);
            previous = index;
        }
        Check(words[words.IndexOf("Supported edits") + 1].Contains("disabled control's tooltip"), "Supported edits explains the disabled control's reason");
        var activity = words[words.IndexOf("The game controls its activity") + 1];
        Check(activity.Contains("When available, Clear activity"), "Track group help does not offer Clear activity for every object whose activity follows a track group");
        var elsewhere = words[words.IndexOf("Edited elsewhere") + 1];
        Check(elsewhere.Contains("Edit a marker on its milepost or crossing pad.") && !elsewhere.Contains("when its reason names one"), "Help names both marker owners directly");
        Check(elsewhere.Contains("Edit construction segments on their group."), "Help directs construction segment edits to the group");
        Check(words[words.IndexOf("Blocked for now") + 1].Contains("related object"), "Blocked for now includes related object states as well as failed reads");
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

    static void AHelpFollowsItsQuestionMarkThroughAScaleChange() {
        // The "?" stands 40 pixels below the panel's top at scale 1.0; the panel keeps its top at 100.
        var offset = PanelPlacement.AnchorOffset(140, 100, 1);
        Check(Near(offset, 40), "The anchor is kept in canvas units below the panel's top");
        Check(Near(PanelPlacement.AnchorTop(offset, 100, 1.25f), 150), "At 1.25 the anchor moves with the scaled panel content");
        Check(Near(PanelPlacement.AnchorTop(PanelPlacement.AnchorOffset(150, 100, 1.25f), 100, 1), 140), "Back at 1.0 the anchor returns");
    }
}
