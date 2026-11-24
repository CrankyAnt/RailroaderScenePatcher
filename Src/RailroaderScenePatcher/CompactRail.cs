// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher {
    // The decisions of the compact window that need no game: its default place and outliner height, the whole rows the
    // outliner shows, the room the context section keeps, and the rail's one-line texts. Sizes are canvas units unless a
    // name says pixels. UI-independent, shared with controlled replay.
    internal static class CompactRail {
        // The native outline row's height.
        internal const float OutlinerRow = 26;
        // The rows the outliner shows at the default height: half of the twelve it showed before, so the whole rail is
        // six rows lower.
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
}
