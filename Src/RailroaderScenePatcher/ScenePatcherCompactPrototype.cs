// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System.Linq;
using RailroaderScenePatcher.Core;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // PROTOTYPE (throwaway): the content of the compact scene patch window. Not production code. Three structurally
    // different variants, switched in the game with the prototype pill at the top of the compact window.
    public sealed partial class PatcherBehaviour {
        // The pill's colour sets it apart from every control of the product.
        const string PrototypePill = "#E070E0";

        void DrawCompactPrototype(UIPanelBuilder builder) {
            typed.Clear();
            builder.ConfigureLayout(4f);
            PrototypeSwitcher(builder);
            switch (presentation.PrototypeVariant) {
                case NativePresentation.PrototypeCompactVariant.SlimStrip: PrototypeSlimStrip(builder); break;
                case NativePresentation.PrototypeCompactVariant.SideRail: PrototypeSideRail(builder); break;
                case NativePresentation.PrototypeCompactVariant.OwnerWithChoice: PrototypeOwnerLayout(builder, true); break;
                default: PrototypeOwnerLayout(builder, false); break;
            }
        }

        // The variant switcher, shown only in compact mode: left and right arrows around the variant's key and name.
        void PrototypeSwitcher(UIPanelBuilder builder) {
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                Button(row, "<", () => presentation.PrototypeCycleVariant(-1)).Tooltip("Previous variant", "Prototype: show the previous compact variant.");
                row.AddLabel("<color=" + PrototypePill + "><b>PROTOTYPE</b> " + Literal(NativePresentation.PrototypeVariantNames[(int)presentation.PrototypeVariant]) + " (" + ((int)presentation.PrototypeVariant + 1) + "/" + NativePresentation.PrototypeVariantCount + ")</color>")
                    .TextWrap(TMPro.TextOverflowModes.Ellipsis, TMPro.TextWrappingModes.NoWrap).Height(RowHeight).FlexibleWidth();
                Button(row, ">", () => presentation.PrototypeCycleVariant(1)).Tooltip("Next variant", "Prototype: show the next compact variant.");
            }).Height(RowHeight);
        }

        // A: the owner's description. The full window's two columns without the outliner's search, filters and
        // reminders, without tabs, tab bodies and footers: the left band over a short scrollable outliner, the right
        // band over the heading, the state line and the Inspector and Guided switch.
        // D is A with one line of a pending world choice under the state line.
        void PrototypeOwnerLayout(UIPanelBuilder builder, bool choiceLine) {
            var columns = builder.HStack(row => {
                var width = Mathf.Clamp(outlinerWidth, OutlinerMinimumWidth, OutlinerMaximum());
                leftBandWidth = width;
                var outlinerRect = row.VStack(column => DrawOutliner(column, true));
                var outliner = outlinerRect.gameObject.AddComponent<LayoutElement>();
                outliner.minWidth = width; outliner.preferredWidth = width; outliner.flexibleWidth = 0; outliner.flexibleHeight = 10000;
                row.AddVRule().Width(NativePresentation.DividerWidth);
                var inspectorRect = row.VStack(column => {
                    column.ConfigureLayout(4f);
                    DrawRightBand(column);
                    PrototypeHeading(column);
                    if (choiceLine) PrototypeChoiceLine(column);
                    column.AddExpandingVerticalSpacer();
                });
                var inspector = inspectorRect.gameObject.AddComponent<LayoutElement>();
                inspector.minWidth = 200; inspector.preferredWidth = 200; inspector.flexibleWidth = 10000; inspector.flexibleHeight = 10000;
            }).gameObject.AddComponent<LayoutElement>();
            columns.flexibleWidth = 10000; columns.flexibleHeight = 10000;
        }

        // B: a slim strip along the bottom: the two lamps beside the heading, then the state line with the switch. No
        // commands, no outliner rows.
        void PrototypeSlimStrip(UIPanelBuilder builder) {
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                var lamps = row.VStack(DrawLeftBand);
                var own = lamps.gameObject.AddComponent<LayoutElement>();
                own.minWidth = own.preferredWidth = OutlinerMinimumWidth; own.flexibleWidth = 0;
                var name = selected == null ? "No Selection" : selected.DisplayName;
                Heading(row, Literal(name), "Object", selected?.Identity.Path).EqualFlexibleWidth(0);
            }).ChildrenKeepHeight().Height(RowHeight);
            PrototypeStateRow(builder);
        }

        // C: a narrow rail at the right screen edge: the band's commands, the heading, the state line and the switch on
        // rows of their own, then the left band over the outliner, which takes the rest of the height.
        void PrototypeSideRail(UIPanelBuilder builder) {
            leftBandWidth = NativePresentation.PrototypeRailContent;
            PrototypeRailBand(builder);
            var name = selected == null ? "No Selection" : selected.DisplayName;
            Heading(builder, Literal(name), "Object", selected?.Identity.Path).Height(RowHeight);
            Line(builder, StateLine(out var states), "State", states, RowHeight);
            builder.HStack(row => { DrawModeSwitch(row); row.Spacer(); }).ChildrenKeepHeight().Height(RowHeight);
            var outliner = builder.VStack(column => DrawOutliner(column, true)).gameObject.AddComponent<LayoutElement>();
            outliner.flexibleHeight = 10000; outliner.flexibleWidth = 10000;
        }

        // The band's four commands on two rows, so the rail's width holds them: Select in world and Focus, then Clear
        // and Copy ID. The states and tooltips are the full band's (DrawRightBand).
        void PrototypeRailBand(UIPanelBuilder builder) {
            var pending = selection?.Pending != null;
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                var select = CommandBand.SelectInWorld(selection != null, pending);
                Button(row, "Select in world", () => BeginSelection(SelectionKind.SceneObject, null)).Inactive(select != null)
                    .Tooltip("Select in world", CommandBand.Tooltip("Click a marking in the world to choose the object to inspect.", select));
                var focus = CommandBand.Focus(selected != null, FocusUnavailable());
                var starter = selected != null && selected.Family == StarterPlacements.Family;
                Button(row, "Focus", () => { if (selected != null) Focus(selected); }).Inactive(focus != null)
                    .Tooltip("Focus", CommandBand.Tooltip(starter ? "Move the camera to the marker's track point, where the start setup places its cars." : "Move the camera to the inspected object.", focus));
                row.Spacer();
            }).Height(RowHeight);
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                var keepDraft = guided.Enabled && (guided.Step == GuidedTaskStep.Values || guided.Step == GuidedTaskStep.Review);
                var clear = keepDraft && !pending ? "No world choice is pending." : CommandBand.Clear(selected != null, pending);
                Button(row, "Clear", ClearInspection).Inactive(clear != null)
                    .Tooltip("Clear", CommandBand.Tooltip(keepDraft ? "Cancel the world choice and keep the object and draft." : "End the inspection and cancel a pending world choice, gizmo, or snap. Applied changes stay.", clear));
                var copy = CommandBand.CopyId(selected != null, attachment?.Session.CopyIdRefusal);
                Button(row, "Copy ID", CopyIdentity).Inactive(copy != null)
                    .Tooltip("Copy ID", CommandBand.Tooltip("Copy the object's ID, placement, and the map ID needed by the scene patch file.", copy));
                row.Spacer();
            }).Height(RowHeight);
        }

        // The heading row and the state row as the full window draws them.
        void PrototypeHeading(UIPanelBuilder builder) {
            if (selected == null) {
                builder.HStack(row => {
                    Heading(row, "No Selection", null, null).EqualFlexibleWidth(0);
                    DrawModeSwitch(row);
                }).ChildrenKeepHeight().Height(RowHeight);
                Heading(builder, StateLine(out _), null, null).Height(RowHeight);
                return;
            }
            Heading(builder, Literal(selected.DisplayName), "Object", IsAdded(selected) ? selected.DisplayName : selected.Identity.Path).Height(RowHeight);
            PrototypeStateRow(builder);
        }

        void PrototypeStateRow(UIPanelBuilder builder) {
            var state = StateLine(out var states);
            builder.HStack(row => {
                Line(row, state, "State", states, RowHeight).EqualFlexibleWidth(0);
                DrawModeSwitch(row);
            }).ChildrenKeepHeight().Height(RowHeight);
        }

        // One line of a pending world choice: what to click, then the clicked candidate. The full text, as the footer
        // words it, is in the tooltip. Without a pending choice the line stays empty and keeps its height.
        void PrototypeChoiceLine(UIPanelBuilder builder) {
            var pending = selection?.Pending;
            if (pending == null) { builder.Spacer(RowHeight); return; }
            var purpose = choosingCoverageRole.HasValue ? pending.Purpose : choosingSnap ? "Choose the snap target" : pending.Kind == SelectionKind.SceneObject ? "Choose an object" : pending.Kind == SelectionKind.TrackSegment ? "Choose a track" : "Choose an endpoint";
            var candidate = selection.Find(pending.Candidate);
            var what = candidate == null ? purpose + " in the world." : CommandBand.ChoiceLine(pending.Kind == SelectionKind.SceneObject ? "Object" : "Candidate", pending.Number(candidate.Choice), pending.Offers.Count, candidate.Choice.Label, pending.Offers.Select(o => o.Label), candidate.Observation);
            var full = purpose + " in the world." + (candidate == null ? " Nothing is clicked yet." : " " + what + " Open the full window to confirm.");
            Line(builder, "<b>World choice</b> " + Tint(Blue, what), "World choice", full, RowHeight);
        }

        void PrototypeLog(string state) {
            var pending = selection?.Pending;
            context?.Log.Info("Compact prototype: " + state + "; inspected=" + (selected == null ? "none" : selected.DisplayName + " (" + selected.Identity.Path + ")") +
                "; pending world choice=" + (pending == null ? "no" : "yes (" + pending.Kind + ", candidate " + (selection.Find(pending.Candidate) == null ? "none" : "clicked") + ")"));
        }
    }
}
