// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using RailroaderScenePatcher;

static class CompactRailTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;
    internal static void Run() {
        TheOutlinerShowsOnlyWholeRows();
        ScrollingRestsOnARowBoundaryInItsDirection();
        AShortContextKeepsTheRoomOfThreeLines();
        TheRailFitsItsHeightToTheDefaultRows();
        TheRailOpensAtTheRightEdgeBottomAligned();
        ATrackLocationReadsAsOneLine();
        FiltersKeepTheirFullWindowNames();
        Console.WriteLine("PASS: compact window rows, scrolling, context room, default place and one-line texts");
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

    static void TheRailFitsItsHeightToTheDefaultRows() {
        // Eighteen rows' space at scale 1 gives up six rows of 26 units.
        Check(Near(CompactRail.FittedHeight(800, 468, 1, 400, 1440), 644), "The rail gives up the rows above the default");
        Check(Near(CompactRail.FittedHeight(800, 468, 1.25f, 400, 1440), 605), "The rows are scaled to pixels");
        Check(Near(CompactRail.FittedHeight(800, 256, 1, 400, 1440), 856), "Too few rows make the rail higher");
        Check(Near(CompactRail.FittedHeight(500, 556, 1, 450, 1440), 450), "The rail keeps its least height");
        Check(Near(CompactRail.FittedHeight(1400, 0, 1, 400, 1440), 1440), "The rail stays within the screen");
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
}
