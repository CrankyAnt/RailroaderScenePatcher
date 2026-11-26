// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The tooltips of the outliner's rows wait as long as a button's. The game shows a tooltip after half a second, and
    // then, for half a second after the pointer leaves it, shows the next one at once: over a list every row under
    // the moving pointer would show its tooltip at once. Here a row's tooltip also waits while the game is in that
    // state, a little shorter than the game's own wait, so it appears while the game still shows tooltips at once.
    internal sealed class OutlineTooltipDelay : UnityEngine.MonoBehaviour {
        const float Wait = 0.45f, Gap = 0.25f;
        static UI.Tooltips.UITooltipProvider current;
        static string currentTitle;
        static float since, lastAsked;
        static System.Reflection.FieldInfo lastShown;
        static bool lastShownRead;
        readonly HashSet<UI.Tooltips.UITooltipProvider> wrapped = new HashSet<UI.Tooltips.UITooltipProvider>();
        float nextScan;
        // The list makes its row cells as it scrolls, so new cells are found as they appear.
        void LateUpdate() {
            if (UnityEngine.Time.unscaledTime < nextScan) return;
            nextScan = UnityEngine.Time.unscaledTime + 0.2f;
            foreach (var provider in GetComponentsInChildren<UI.Tooltips.UITooltipProvider>(true)) {
                if (provider == null || !wrapped.Add(provider)) continue;
                var row = provider;
                row.DynamicTooltipInfo = () => Ask(row);
            }
        }
        // Whether the game shows the next tooltip at once: it holds the tooltip it showed last.
        static bool ShowsAtOnce() {
            try {
                var controller = UI.Tooltips.UITooltipController.Shared;
                if (controller == null) return false;
                if (!lastShownRead) { lastShownRead = true; lastShown = typeof(UI.Tooltips.UITooltipController).GetField("_lastShownTooltip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); }
                return lastShown != null && lastShown.GetValue(controller) is TooltipInfo shown && !shown.IsEmpty;
            } catch (Exception) { return false; }
        }
        static TooltipInfo Ask(UI.Tooltips.UITooltipProvider row) {
            var title = row.Title;
            if (String.IsNullOrEmpty(title)) return TooltipInfo.Empty;
            var now = UnityEngine.Time.realtimeSinceStartup;
            // Another row, another text in the same reused row, or a pointer that came back starts the wait again.
            if (!ReferenceEquals(current, row) || currentTitle != title || now - lastAsked > Gap) { current = row; currentTitle = title; since = now; }
            lastAsked = now;
            return !ShowsAtOnce() || now - since >= Wait ? new TooltipInfo(title, String.Empty) : TooltipInfo.Empty;
        }
    }
    // Fits the added names of the outliner's rows to the width their cells have. The list makes and reuses its row cells
    // as it scrolls and the divider resizes it without a rebuild, so each cell is checked again: a cell that shows a
    // row's full text, or whose width changed, gets the name that fits (InventoryPresentationState.FittedRow).
    internal sealed class OutlineNameFit : UnityEngine.MonoBehaviour {
        struct Shown { internal InventoryPresentationState.FittedRow Row; internal float Width; internal string Text; }
        IReadOnlyDictionary<string, InventoryPresentationState.FittedRow> rows = new Dictionary<string, InventoryPresentationState.FittedRow>();
        readonly Dictionary<TMPro.TMP_Text, Shown> shown = new Dictionary<TMPro.TMP_Text, Shown>();
        float nextScan;
        internal void Use(IReadOnlyDictionary<string, InventoryPresentationState.FittedRow> fitted) { rows = fitted; shown.Clear(); nextScan = 0; }
        void LateUpdate() {
            if (UnityEngine.Time.unscaledTime < nextScan) return;
            nextScan = UnityEngine.Time.unscaledTime + 0.1f;
            foreach (var text in GetComponentsInChildren<TMPro.TMP_Text>(true)) {
                if (text == null) continue;
                var current = text.text;
                if (!shown.TryGetValue(text, out var state) || current != state.Text) {
                    if (current == null || !rows.TryGetValue(current, out var row)) { shown.Remove(text); continue; }
                    state = new Shown { Row = row, Width = -1, Text = current };
                }
                var width = text.rectTransform.rect.width - text.margin.x - text.margin.z - 1;
                if (width <= 0 || width == state.Width) { shown[text] = state; continue; }
                var fit = state.Row.Fit(width, value => text.GetPreferredValues(value).x);
                if (fit != current) text.text = fit;
                shown[text] = new Shown { Row = state.Row, Width = width, Text = fit };
            }
        }
    }
    public sealed partial class PatcherBehaviour {
        // The map x/z where a starter placement marker or track span acts, from the same track points Focus uses. The
        // outliner asks only for those families (OutlinerGrouping.UsesTrackPoint).
        double[] OutlinerTrackPoint(SceneObservation entry) {
            var point = FocusTarget.Point(entry.Family, null, TrackPoints(entry), out _);
            return point != null && point.Length >= 3 ? new[] { point[0], point[2] } : null;
        }
        // The folder of the game: a path below it shows from there (CommandBand.ShownPath).
        static string GameFolder => System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
        // Each level of the outliner's tree below the first indents a row by this much.
        const float OutlineIndent = 16f;

        // The crossing node the outliner has selected, while no object is inspected: a complex without a
        // row of its own. Focus goes to the middle of its members.
        string crossingNodeKey;
        OutlinerGrouping.GroupNode crossingNode;
        void SelectCrossingNode(string key, OutlinerGrouping.GroupNode node) {
            if (selected != null) ClearInspection();
            if (selected != null) return;
            crossingNodeKey = key; crossingNode = node;
        }
        void FocusCrossingNode() {
            var point = crossingNode?.Point;
            if (point == null) return;
            try { JumpTo(new UnityEngine.Vector3((float)point[0], (float)point[1], (float)point[2])); }
            catch (Exception e) { feedback.Report("Focus", "Focus refused: " + e.GetBaseException().Message); }
        }
    }
}
