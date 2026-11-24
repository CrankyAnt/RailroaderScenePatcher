// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using RailroaderScenePatcher;

static class CompactRailTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;
    internal static void Run() {
        TheRailSizesFollowFromItsRows();
        TheOutlinerShowsOnlyWholeRows();
        ScrollingRestsOnARowBoundaryInItsDirection();
        AShortContextKeepsTheRoomOfThreeLines();
        TheOutlinerKeepsSixRowsUntilTheUserAddsWholeRows();
        TheRailOpensAtTheRightEdgeBottomAligned();
        ATrackLocationReadsAsOneLine();
        FiltersKeepTheirFullWindowNames();
        TheFullWindowComesBackInCanvasUnits();
        ADraggedRailKeepsItsPlaceOnAScaleChange();
        TheFullRecordIsWrittenBackWhileCompact();
        MovesWaitUntilTheButtonIsUp();
        ADraggedRailIsNoticedBeforeAScaleChange();
        Console.WriteLine("PASS: compact window sizes, rows, scrolling, context room, places across scale changes, full record, moves after the release and one-line texts");
    }

    static void TheRailSizesFollowFromItsRows() {
        Check(Near(CompactRail.CollapsedContent, 476), "With the outliner hidden, the least content height is 476");
        Check(Near(CompactRail.ExpandedLeastContent, 692), "With the outliner shown, the default and least content height is 692: six rows above the hidden state's 476 with the full Placement body");
        Check(CompactRail.OutlinerRows == 6, "The default outliner shows six rows");
        Check(CompactRail.OutlinerRows * CompactRail.OutlinerRow > CommandBand.OutlinerLeastHeight, "The default rows stand above the outliner's least height");
    }

    static void TheOutlinerShowsOnlyWholeRows() {
        Check(Near(CompactRail.WholeRows(156), 156), "Six rows' height shows six rows");
        Check(Near(CompactRail.WholeRows(180), 156), "The space below the sixth row stays empty");
        Check(Near(CompactRail.WholeRows(155.6f), 156), "A height a fraction short of a row still shows that row");
        Check(Near(CompactRail.WholeRows(120), 104), "The least height shows four whole rows");
        Check(Near(CompactRail.WholeRows(10), 26), "At least one row shows");
    }

    static void ScrollingRestsOnARowBoundaryInItsDirection() {
        const float content = 26 * 20, shown = 26 * 6;
        Check(Near(CompactRail.SnappedOffset(3, 0, content, shown), 26), "A small move down goes one row down");
        Check(Near(CompactRail.SnappedOffset(50, 52, content, shown), 26), "A small move up goes one row up");
        Check(Near(CompactRail.SnappedOffset(26.3f, 26, content, shown), 26), "Without a move the offset rounds to the nearest row");
        Check(Near(CompactRail.SnappedOffset(400, 390, content, shown), content - shown), "The offset stops at the list's end");
        Check(Near(CompactRail.SnappedOffset(-5, 0, content, shown), 0), "The offset stops at the list's top");
        Check(Near(CompactRail.SnappedOffset(30, 0, 100, shown), 0), "A list shorter than the viewport does not scroll");
    }

    static void AShortContextKeepsTheRoomOfThreeLines() {
        Check(CompactRail.ContextLines == 3, "Most views show three context lines");
        Check(Near(CompactRail.ContextPadding(3, 20, 4), 0), "Three lines need no padding");
        Check(Near(CompactRail.ContextPadding(5, 20, 4), 0), "A longer context grows without padding");
        Check(Near(CompactRail.ContextPadding(2, 20, 4), 20), "Two lines keep the third line's height");
        Check(Near(CompactRail.ContextPadding(1, 20, 4), 44), "One line keeps two more lines with their spacing");
    }

    static void TheOutlinerKeepsSixRowsUntilTheUserAddsWholeRows() {
        // A default rail of 738 pixels at scale 1: the outliner shows six rows, and the message area takes what a
        // shorter context leaves free.
        Check(CompactRail.OutlinerRowsFor(738, 738, 1) == 6, "At the default height the outliner shows six rows");
        Check(CompactRail.OutlinerRowsFor(763, 738, 1) == 6, "Less than a row more keeps six rows");
        Check(CompactRail.OutlinerRowsFor(764, 738, 1) == 7, "A whole row more shows seven rows");
        Check(CompactRail.OutlinerRowsFor(738 + 26 * 4 * 1.25f, 738, 1.25f) == 10, "The rows are scaled to pixels");
        Check(CompactRail.OutlinerRowsFor(700, 738, 1) == 6, "A rail below the default keeps six rows");
    }

    static void TheRailOpensAtTheRightEdgeBottomAligned() {
        var place = CompactRail.DefaultPlace(400, 700, 2560, 1440);
        Check(Near(place.X, 2160) && Near(place.Y, 740) && Near(place.Width, 400) && Near(place.Height, 700), "The rail stands at the right edge, bottom-aligned");
        var large = CompactRail.DefaultPlace(3000, 2000, 1920, 1080);
        Check(Near(large.X, 0) && Near(large.Y, 0) && Near(large.Width, 1920) && Near(large.Height, 1080), "The rail is no larger than the screen");
    }

    static void ATrackLocationReadsAsOneLine() {
        Check(CompactRail.LocationText(new[] { "Sqo0", "A", "12.5" }) == "Sqo0 / a / 12.5 m", "Segment, end and distance read as one line");
        Check(CompactRail.LocationText(new[] { " ", "a", "1" }) == "None", "Without a segment the location is none");
        Check(CompactRail.LocationText(null, "None entered") == "None entered", "Missing fields show the given empty text");
        Check(CompactRail.LocationText(new[] { "Sqo0", "a" }) == "None", "Incomplete fields show the empty text");
    }

    static void FiltersKeepTheirFullWindowNames() {
        Check(CompactRail.FilterName(OutlinerFilter.All) == CommandBand.AllFilter, "All keeps its name");
        Check(CompactRail.FilterName(OutlinerFilter.Changed) == CommandBand.ChangedFilter, "Changed keeps its name");
        Check(CompactRail.FilterName(OutlinerFilter.Disabled) == CommandBand.DisabledFilter, "Disabled keeps its name");
        Check(CompactRail.FilterName(OutlinerFilter.Problems) == CommandBand.ProblemsFilter, "Problems keeps its name");
    }

    static void TheFullWindowComesBackInCanvasUnits() {
        // Entered at scale 1.0 with a window of 734 x 660 at (100, 50); left at scale 1.25.
        var pixels = new PanelPlacement.Area { X = 100, Y = 50, Width = 734, Height = 660 };
        var canvas = CompactRail.ToCanvas(pixels, 1);
        var back = CompactRail.RestoredFull(canvas, 1.25f, 704, 470, 2560, 1440);
        Check(Near(back.X, 125) && Near(back.Y, 62.5f) && Near(back.Width, 917.5f) && Near(back.Height, 825), "The full window keeps its canvas size and place at the new scale");
        var wide = new PanelPlacement.Area { X = 100, Y = 50, Width = 1000, Height = 700 };
        var same = CompactRail.RestoredFull(CompactRail.ToCanvas(wide, 1.25f), 1.25f, 704, 470, 2560, 1440);
        Check(Near(same.X, 100) && Near(same.Y, 50) && Near(same.Width, 1000) && Near(same.Height, 700), "Without a scale change the full window comes back as it was");
        var small = CompactRail.RestoredFull(new PanelPlacement.Area { X = 0, Y = 0, Width = 600, Height = 400 }, 1, 704, 470, 2560, 1440);
        Check(Near(small.Width, 704) && Near(small.Height, 470), "The full window comes back at least at its minimum size");
        var off = CompactRail.RestoredFull(new PanelPlacement.Area { X = 1800, Y = 900, Width = 734, Height = 660 }, 1, 704, 470, 1920, 1080);
        Check(Near(off.X, 1920 - 734) && Near(off.Y, 1080 - 660), "The full window comes back inside a smaller screen");
    }

    static void ADraggedRailKeepsItsPlaceOnAScaleChange() {
        var dragged = new PanelPlacement.Area { X = 800, Y = 300, Width = 400, Height = 700 };
        var larger = CompactRail.Rescaled(dragged, 1, 1.25f, 450, 600, 2560, 1440);
        Check(Near(larger.X, 800) && Near(larger.Y, 300) && Near(larger.Width, 500) && Near(larger.Height, 875), "The rail keeps its corner and takes the new scale's size");
        var edge = CompactRail.Rescaled(new PanelPlacement.Area { X = 2160, Y = 740, Width = 400, Height = 700 }, 1, 1.25f, 450, 600, 2560, 1440);
        Check(Near(edge.X, 2560 - 500) && Near(edge.Y, 1440 - 875), "A rail at the screen's edge stays inside the screen");
        var smaller = CompactRail.Rescaled(dragged, 1.25f, 1, 450, 600, 2560, 1440);
        Check(Near(smaller.Width, 450) && Near(smaller.Height, 600), "The rail keeps at least its least size");
    }

    static void TheFullRecordIsWrittenBackWhileCompact() {
        var record = new FullWindowRecord();
        Check(record.Rewrite("compact") == null, "Without a kept record nothing is written");
        record.Keep("full");
        Check(record.Rewrite("compact") == "full", "A record the native persistence changed is written back");
        Check(record.Rewrite("full") == null, "An unchanged record is not written again");
        record.Keep(null);
        Check(record.Rewrite("compact") == null, "A record that could not be read is not written");
    }

    static void MovesWaitUntilTheButtonIsUp() {
        var wait = new AfterRelease();
        Check(!wait.Tick(false) && !wait.Pending, "Without a request nothing moves");
        // The request comes from a button's click, which acts on the release frame.
        wait.Request();
        Check(!wait.Tick(false), "Nothing moves on the release frame, which the native map editor also reads");
        Check(wait.Tick(false) && !wait.Pending, "The move runs one tick later");
        Check(!wait.Tick(false), "It runs once per request");
        wait.Request();
        Check(!wait.Tick(true) && !wait.Tick(true) && wait.Pending, "Nothing moves while the button is down");
        Check(!wait.Tick(false) && wait.Tick(false), "The move follows the release by a tick");
        wait.Request(); wait.Cancel();
        Check(!wait.Tick(false) && !wait.Tick(false), "A cancelled move does not run");
    }

    static void ADraggedRailIsNoticedBeforeAScaleChange() {
        // At 1.25 the rail stands at its default place; the user drags it to the middle, then sets the scale to 1.0.
        var place = new RailPlacement();
        var fallback = new PanelPlacement.Area { X = 2085, Y = 518, Width = 475, Height = 922 };
        place.Placed(fallback, 1.25f);
        Check(place.AfterScaleChange(1, 380, 738, 2560, 1440) == null, "A rail at its default place goes to the default place");
        place.Placed(fallback, 1.25f);
        Check(!place.Observe(fallback, 1.25f, false), "The placed rectangle is no drag");
        var middle = new PanelPlacement.Area { X = 1000, Y = 300, Width = 475, Height = 922 };
        Check(!place.Observe(middle, 1.25f, true), "A drag in progress is not taken yet");
        Check(place.Observe(middle, 1.25f, false) && place.Dragged != null, "A tick after the drag notices it");
        var kept = place.AfterScaleChange(1, 380, 738, 2560, 1440);
        Check(kept != null && Near(kept.Value.X, 1000) && Near(kept.Value.Y, 300) && Near(kept.Value.Width, 380) && Near(kept.Value.Height, 738),
            "After the scale change the rail keeps its corner and takes the new scale's size");
        place.Placed(kept.Value, 1);
        var nativeResize = new PanelPlacement.Area { X = 1000, Y = 300, Width = 475, Height = 922 };
        Check(!place.Observe(nativeResize, 1.25f, false), "The native resize on a scale change is no drag");
        place.Forget();
        Check(place.Dragged == null && place.AfterScaleChange(1.25f, 380, 738, 2560, 1440) == null, "Reset layout forgets the drag");
    }
}
