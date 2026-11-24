// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher {
    // The decisions of the compact window that need no game: its sizes, its default place and outliner height, the
    // whole rows the outliner shows, the room the context section keeps, where the full window and a dragged rail come
    // back after a UI scale change, and the rail's one-line texts. Sizes are canvas units unless a name says pixels.
    // UI-independent, shared with controlled replay.
    internal static class CompactRail {
        // The rail's content width: the inspector's minimum (350) with padding, so the four right-band commands (335),
        // every footer and tab action row (budget 340) and all five title-bar icons fit.
        internal const float Width = 356;
        // The window's row height, the layout's spacing between rows, and a rule with its margins. The rule's 14 units
        // are what the accepted least height of 476 leaves for the rail's two rules; they are not measured on their own.
        internal const float Row = 24, Spacing = 4, Rule = 14;
        // The message area of six lines, the Placement body at its full and its least height, and the clearance that
        // keeps the last row's right-aligned button off the native resize grip: the grip's 22 units less the 10 units
        // of padding below the last row.
        internal const float MessageHeight = 112, PlacementBody = 196, PlacementCollapsedBody = 140, GripClearance = 12;
        // The least content height with the outliner hidden: the two band rows, the name, the state line, the Placement
        // body at its least height, the message area, the action row, the outliner's summary line, two rules, the grip
        // clearance, and the spacing between those eleven rows. The tallest context is the Placement body's.
        internal const float CollapsedContent = 6 * Row + PlacementCollapsedBody + MessageHeight + 2 * Rule + GripClearance + 10 * Spacing;
        // With the outliner shown: the Placement body at its full height and the outliner rows at their least height.
        internal const float ExpandedLeastContent = CollapsedContent + (PlacementBody - PlacementCollapsedBody) + CommandBand.OutlinerLeastHeight + Spacing;
        // The native outline row's height.
        internal const float OutlinerRow = 26;
        // The rows the outliner shows at the default height: six, so the rail leaves most of the screen's height to
        // the world and the outliner still shows a group with a few of its parts.
        internal const int OutlinerRows = 6;
        // The context lines most views show: position, rotation, and enabled for an object with a placement of its own.
        internal const int ContextLines = 3;

        // The viewport height that shows only whole rows of the available height, and at least one row. The space left
        // over, less than a row, stays empty below the list.
        internal static float WholeRows(float available, float row = OutlinerRow) =>
            Math.Max(row, (float)Math.Floor((available + 0.5f) / row) * row);

        // The scroll offset that puts a row boundary at the viewport's top. It rounds in the direction the list moved
        // since the last snapped offset, so a single wheel notch moves a whole row either way, and it never scrolls past
        // the list's end. shown is the viewport's whole-row height.
        internal static float SnappedOffset(float offset, float lastSnapped, float contentHeight, float shown, float row = OutlinerRow) {
            var rows = offset / row;
            var target = offset > lastSnapped + 0.5f ? Math.Ceiling(rows - 0.01f)
                : offset < lastSnapped - 0.5f ? Math.Floor(rows + 0.01f)
                : Math.Round(rows);
            return Math.Min(Math.Max(0, (float)target * row), Math.Max(0, contentHeight - shown));
        }

        // The space below a context section of lines, so that a context with fewer than ContextLines keeps their
        // height: inspecting another object then moves neither the messages, the actions, nor the outliner. A context
        // with more lines grows.
        internal static float ContextPadding(int lines, float lineHeight, float spacing) =>
            lines >= ContextLines ? 0 : (ContextLines - lines) * (lineHeight + spacing) - spacing;

        // The window height in pixels that gives the outliner exactly its default rows, from the current height and the
        // outliner viewport's available height in canvas units, within the least height and the screen.
        internal static float FittedHeight(float currentHeight, float availableRows, float scale, float leastHeight, float screenHeight) {
            var height = currentHeight + (OutlinerRows * OutlinerRow - availableRows) * scale;
            return Math.Min(Math.Max(height, leastHeight), screenHeight);
        }

        // The default place in pixels: at the right screen edge, bottom-aligned, no larger than the screen.
        internal static PanelPlacement.Area DefaultPlace(float width, float height, float screenWidth, float screenHeight) {
            width = Math.Min(width, screenWidth); height = Math.Min(height, screenHeight);
            return new PanelPlacement.Area { X = Math.Max(0, screenWidth - width), Y = Math.Max(0, screenHeight - height), Width = width, Height = height };
        }

        // A pixel rectangle in canvas units at scale, so that it survives a UI scale change.
        internal static PanelPlacement.Area ToCanvas(PanelPlacement.Area pixels, float scale) {
            var s = scale > 0 ? scale : 1;
            return new PanelPlacement.Area { X = pixels.X / s, Y = pixels.Y / s, Width = pixels.Width / s, Height = pixels.Height / s };
        }

        // Where the full window comes back when compact mode ends, in pixels: the rectangle it had, kept in canvas units,
        // at the current scale, at least the window's minimum size, and inside the screen.
        internal static PanelPlacement.Area RestoredFull(PanelPlacement.Area canvas, float scale, float minimumWidth, float minimumHeight, float screenWidth, float screenHeight) {
            var width = Math.Min(Math.Max(canvas.Width, minimumWidth) * scale, screenWidth);
            var height = Math.Min(Math.Max(canvas.Height, minimumHeight) * scale, screenHeight);
            return new PanelPlacement.Area {
                X = Math.Min(Math.Max(0, canvas.X * scale), Math.Max(0, screenWidth - width)),
                Y = Math.Min(Math.Max(0, canvas.Y * scale), Math.Max(0, screenHeight - height)),
                Width = width, Height = height };
        }

        // A rail the user dragged keeps its place on a UI scale change: its top-left corner stays, its size follows the
        // scale with at least the least size (pixels at the new scale), and it stays inside the screen.
        internal static PanelPlacement.Area Rescaled(PanelPlacement.Area pixels, float oldScale, float newScale, float leastWidth, float leastHeight, float screenWidth, float screenHeight) {
            var ratio = oldScale > 0 ? newScale / oldScale : 1;
            var width = Math.Min(Math.Max(pixels.Width * ratio, leastWidth), screenWidth);
            var height = Math.Min(Math.Max(pixels.Height * ratio, leastHeight), screenHeight);
            return new PanelPlacement.Area {
                X = Math.Min(Math.Max(0, pixels.X), Math.Max(0, screenWidth - width)),
                Y = Math.Min(Math.Max(0, pixels.Y), Math.Max(0, screenHeight - height)),
                Width = width, Height = height };
        }

        // A track location's fields as one line, such as "Sqo0 / a / 12.5 m"; empty when no segment is entered. The
        // fields are segment, end, and distance.
        internal static string LocationText(string[] fields, string empty = "None") {
            if (fields == null || fields.Length < 3 || String.IsNullOrWhiteSpace(fields[0])) return empty;
            return fields[0] + " / " + (fields[1] ?? "").ToLowerInvariant() + " / " + fields[2] + " m";
        }

        // The name of an outliner filter as its control in the full window shows it.
        internal static string FilterName(OutlinerFilter value) =>
            value == OutlinerFilter.Changed ? CommandBand.ChangedFilter
            : value == OutlinerFilter.Disabled ? CommandBand.DisabledFilter
            : value == OutlinerFilter.Problems ? CommandBand.ProblemsFilter
            : CommandBand.AllFilter;
    }

    // The full window's saved-bounds record while compact. The native persistence saves position and size on every
    // drag and resize; while compact, the record from before compact mode is written back, so compact bounds never
    // reach the next session.
    internal sealed class FullWindowRecord {
        string saved;
        // Keeps the record read when compact mode starts; null when it could not be read.
        internal void Keep(string record) { saved = record; }
        // The record to write back over current, or null when nothing needs writing.
        internal string Rewrite(string current) => saved != null && current != saved ? saved : null;
    }

    // The help and the session details move beside the window again when compact mode ends and on Reset layout. They
    // move once the button that asked for it is up, since layout places nothing while it is down, and one tick later,
    // once the window has its new place.
    internal sealed class SecondaryWindowReset {
        const int Ticks = 2;
        int left;
        internal bool Pending => left > 0;
        internal void Request() { left = Ticks; }
        // Counts one tick; true on the tick that places the windows.
        internal bool Tick(bool buttonDown) => left > 0 && !buttonDown && --left == 0;
    }
}
