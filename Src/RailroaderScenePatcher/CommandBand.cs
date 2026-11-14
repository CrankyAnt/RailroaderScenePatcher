// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

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
        internal static string Focus(bool inspected) => inspected ? null : "Inspect an object first.";
        internal static string CopyId(bool inspected) => inspected ? null : "Inspect an object first.";
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
        // editorWidth is 0 when the Map Editor window is not shown; the window then keeps its default size at the left.
        internal static Area Default(float screenWidth, float screenHeight, float scale, float editorLeft, float editorWidth, float editorBottom, float topRightBottom,
            float defaultWidth, float defaultHeight, float minimumHeight) {
            float width = defaultWidth * scale, x = 0, top = screenHeight - defaultHeight * scale;
            if (editorWidth > 0) { x = editorLeft; width = Math.Max(width, editorWidth); top = editorBottom; }
            width = Math.Min(width, screenWidth);
            var below = screenHeight - top;
            if (editorWidth > 0 && below < FallbackBelow * scale) {
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
