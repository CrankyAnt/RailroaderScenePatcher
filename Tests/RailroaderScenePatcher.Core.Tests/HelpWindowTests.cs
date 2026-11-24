// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using RailroaderScenePatcher;

static class HelpWindowTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;
    internal static void Run() {
        TheWheelMovesAsTheGamesScrollViewsDo();
        AHelpNeverCoversTheMapEditorsTitleBar();
        AHelpFollowsItsQuestionMarkThroughAScaleChange();
        Console.WriteLine("PASS: help windows scroll at the game's wheel step, keep off the Map Editor's title bar and follow a UI scale change");
    }

    static void TheWheelMovesAsTheGamesScrollViewsDo() {
        Check(CommandBand.WheelStep(true, false) == 20, "On Windows a notch moves 20 units, about a text line");
        Check(CommandBand.WheelStep(false, true) == 2, "On macOS a notch moves 2 units");
        Check(CommandBand.WheelStep(false, false) == 1, "Elsewhere a notch moves 1 unit");
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
