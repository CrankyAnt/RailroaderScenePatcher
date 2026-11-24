// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
// PROTOTYPE: throwaway hooks of the outliner grouping prototype into the scene patch window.
using System;
using System.Collections.Generic;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The tooltips of the outliner's rows wait as long as a button's. The game shows a tooltip after half a second, and
    // then, for half a second after the pointer leaves it, shows the next one at once: over a list every row under
    // the moving pointer would show its tooltip at once. Here a row's tooltip also waits while the game is in that
    // state, a little shorter than the game's own wait, so it appears while the game still shows tooltips at once.
    internal sealed class PrototypeOutlineTooltips : UnityEngine.MonoBehaviour {
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
    public sealed partial class PatcherBehaviour {
        // The map x/z where a starter placement marker or track span acts, from the same track points Focus uses. The
        // grouping asks only for those families (PrototypeOutlinerGrouping.UsesTrackPoint).
        double[] PrototypeTrackPoint(SceneObservation entry) {
            var point = FocusTarget.Point(entry.Family, null, TrackPoints(entry), out _);
            return point != null && point.Length >= 3 ? new[] { point[0], point[2] } : null;
        }

        // The crossing node the outliner has selected, while no object is inspected: a game-defined group without a
        // row of its own. Focus goes to the middle of its members.
        string prototypeGroupKey;
        PrototypeOutlinerGrouping.GroupNode prototypeGroupNode;
        void SelectGroupNode(string key, PrototypeOutlinerGrouping.GroupNode node) {
            if (selected != null) ClearInspection();
            if (selected != null) return;
            prototypeGroupKey = key; prototypeGroupNode = node;
        }
        void FocusGroupNode() {
            var point = prototypeGroupNode?.Point;
            if (point == null) return;
            try { JumpTo(new UnityEngine.Vector3((float)point[0], (float)point[1], (float)point[2])); }
            catch (Exception e) { feedback.Report("Focus", "Focus refused: " + e.GetBaseException().Message); }
        }

        // The outliner rows, with the prototype's search over place, kind and crossing; the current rows when it fails.
        System.Collections.Generic.IReadOnlyList<SceneObservation> PrototypeRows() {
            try { return PrototypeOutlinerGrouping.Rows(inventory, filter, outlinerFilter, IsChanged, PrototypeTrackPoint); }
            catch (Exception e) when (!(e is OutOfMemoryException)) {
                PrototypeOutlinerGrouping.WarnOnce("rows", "the grouping search failed, so the search matches object fields only: " + e);
                return InventoryPresentationState.Rows(inventory, filter, outlinerFilter, IsChanged);
            }
        }
    }
}
