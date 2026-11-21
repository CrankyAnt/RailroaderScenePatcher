// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System.Linq;
using RailroaderScenePatcher.Core;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // PROTOTYPE (throwaway): the content of the compact scene patch window. Not production code. Compact mode keeps the
    // edit in progress while the world needs the screen: the inspected object, its state, and the interactive elements a
    // modder needs with an active selection. Each variant has its own purpose:
    // A, selection toolbar: choose and confirm in the world.
    // B, placement strip: place the inspected object while watching it.
    // C, side rail: browse the list and choose, with the view's centre free.
    // Every variant ends in the world choice bar, so a pending choice can be confirmed in compact mode.
    public sealed partial class PatcherBehaviour {
        // The pill's colour sets it apart from every control of the product.
        const string PrototypePill = "#E070E0";
        // The placement mode body's height: Numeric's two groups of fields.
        const float PrototypePlacementBody = NativePresentation.PrototypePlacementBody;

        void DrawCompactPrototype(UIPanelBuilder builder) {
            typed.Clear();
            builder.ConfigureLayout(4f);
            PrototypeSwitcher(builder);
            switch (presentation.PrototypeVariant) {
                case NativePresentation.PrototypeCompactVariant.PlacementStrip: PrototypePlacementStrip(builder); break;
                case NativePresentation.PrototypeCompactVariant.SideRail: PrototypeSideRail(builder); break;
                default: PrototypeSelectionToolbar(builder); break;
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

        // A: a toolbar along the bottom for choosing in the world. Refresh and the lamps beside Select in world, Focus,
        // Clear and Copy ID; the heading; the state line with the switch; the position line; the world choice bar.
        void PrototypeSelectionToolbar(UIPanelBuilder builder) {
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                leftBandWidth = OutlinerMinimumWidth;
                var lamps = row.VStack(DrawLeftBand).gameObject.AddComponent<LayoutElement>();
                lamps.minWidth = lamps.preferredWidth = OutlinerMinimumWidth; lamps.flexibleWidth = 0;
                var commands = row.VStack(column => DrawRightBand(column)).gameObject.AddComponent<LayoutElement>();
                commands.flexibleWidth = 10000;
            }).ChildrenKeepHeight().Height(RowHeight);
            PrototypeHeading(builder);
            PrototypePositionLine(builder);
            PrototypeChoiceBar(builder);
        }

        // B: a strip along the bottom for placing the inspected object: the heading with the switch, the state line, the
        // Placement tab's mode switch and the open mode's fields or state, the mode's own action row and the world choice
        // bar, which Snap and Follow need. The strip opens the Placement tab, so the gizmo stands in Gizmo mode.
        void PrototypePlacementStrip(UIPanelBuilder builder) {
            var pending = selection?.Pending != null;
            if (!guided.Enabled && !pending && inspectorTab.Value != PlacementTab) inspectorTab.Value = PlacementTab;
            PrototypeHeading(builder);
            var body = presentation.Scroll(builder, PrototypePlacementBody, content => {
                content.ConfigureLayout(4f);
                if (guided.Enabled) Line(content, Literal("A guided task is open. Its steps are in the full window."), null, null);
                else if (selected == null) Line(content, Literal("Inspect an object first. The selection toolbar and the side rail choose one."), null, null);
                else if (inspectorTab.Value != PlacementTab) Line(content, Literal("The world choice belongs to the " + PrototypeTabTitle(inspectorTab.Value) + " tab. Confirm or cancel it to place the object here."), null, null);
                else DrawPlacement(content);
            });
            var fill = body.GetComponent<LayoutElement>();
            if (fill != null) { fill.minHeight = fill.preferredHeight = PrototypePlacementBody; fill.flexibleHeight = 10000; }
            var viewport = body.GetComponent<ScrollRect>().viewport;
            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null) viewport.gameObject.AddComponent<RectMask2D>();
            var tabActs = guided.Enabled || inspectorTab.Value != PlacementTab ? new Act[0] : TabActs(PlacementTab);
            ActionRow(builder, tabActs, ChoiceBlock);
            PrototypeChoiceBar(builder);
        }

        // C: a narrow rail at the right screen edge for browsing and choosing: the band's commands on two rows, the
        // heading, the state line, the switch, the position line, the world choice bar, then Refresh with the lamps over
        // the outliner, which takes the rest of the height.
        void PrototypeSideRail(UIPanelBuilder builder) {
            leftBandWidth = NativePresentation.PrototypeRailContent;
            PrototypeRailBand(builder);
            var name = selected == null ? "No Selection" : selected.DisplayName;
            Heading(builder, Literal(name), "Object", selected?.Identity.Path).Height(RowHeight);
            Line(builder, StateLine(out var states), "State", states, RowHeight);
            builder.HStack(row => { DrawModeSwitch(row); row.Spacer(); }).ChildrenKeepHeight().Height(RowHeight);
            PrototypePositionLine(builder);
            PrototypeChoiceBar(builder);
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
            var state = StateLine(out var states);
            builder.HStack(row => {
                Line(row, state, "State", states, RowHeight).EqualFlexibleWidth(0);
                DrawModeSwitch(row);
            }).ChildrenKeepHeight().Height(RowHeight);
        }

        // The inspected object's position and rotation as last observed, and whether it is enabled: where it stands now.
        void PrototypePositionLine(UIPanelBuilder builder) {
            var values = selected == null ? null : CurrentPlacement();
            string markup, full;
            if (selected == null) { markup = full = "Position and rotation appear here when an object is inspected."; markup = Literal(markup); }
            else if (values == null || values.Length < 6) { full = "This object has no placement of its own."; markup = Literal(full); }
            else {
                var position = values[0] + ", " + values[1] + ", " + values[2];
                var rotation = values[3] + ", " + values[4] + ", " + values[5];
                var enabled = OwnActivity(selected) ? "Enabled" : "Disabled";
                markup = "<style=FieldLabel>POSITION</style> " + Literal(position) + "   <style=FieldLabel>ROTATION</style> " + Literal(rotation) + "   " + Literal(enabled);
                full = "Position in map coordinates, in meters: " + position + ". Rotation in degrees: " + rotation + ". " + enabled + ". As last observed; a completed edit observes the object again.";
            }
            Line(builder, markup, "Placement", full, RowHeight);
        }

        // The world choice bar: one line, then the footer's Confirm, Cancel, Focus and Details. The line shows the pending
        // choice and its clicked candidate; without a choice it shows the confirmed target or the newest result. Without a
        // choice the four buttons stay in place, inactive with their reason. During a gizmo drag the row holds its Cancel.
        void PrototypeChoiceBar(UIPanelBuilder builder) {
            var pending = selection?.Pending;
            string markup, title, full;
            if (pending != null) {
                var purpose = choosingCoverageRole.HasValue ? pending.Purpose : choosingSnap ? "Choose the snap target" : pending.Kind == SelectionKind.SceneObject ? "Choose an object" : pending.Kind == SelectionKind.TrackSegment ? "Choose a track" : "Choose an endpoint";
                var candidate = selection.Find(pending.Candidate);
                title = "World choice";
                if (candidate == null) {
                    markup = "<b>World choice</b> " + Tint(Blue, purpose + " in the world.");
                    full = purpose + " in the world." + (pending.InspectOnly ? "" : " Map Editor selection is paused. Click Cancel to resume it.");
                } else {
                    var line = CommandBand.ChoiceLine(pending.Kind == SelectionKind.SceneObject ? "Object" : "Candidate", pending.Number(candidate.Choice), pending.Offers.Count, candidate.Choice.Label, pending.Offers.Select(o => o.Label), candidate.Observation);
                    var next = choosingSnap ? " Confirm chooses it. Apply moves the object." : pending.InspectOnly ? " Confirm inspects this object." : " Confirm chooses this target.";
                    // Details shows the candidate's identity in the line itself, as it does in the footer's message area.
                    markup = "<b>World choice</b> " + Tint(Blue, candidateDetails ? candidate.Choice.Identity.ToString() : line);
                    full = line + next + "\n" + candidate.Choice.Identity + (string.IsNullOrEmpty(candidate.Details) ? "" : "\n" + candidate.Details);
                }
                if (selectionProblem != null) { markup += " " + Tint(Red, selectionProblem); full += "\n" + selectionProblem; }
            } else if (ConfirmedForInspected is ConfirmedSelection confirmed) {
                title = "World choice";
                var name = selection.ConfirmedName ?? confirmed.Identity.ToString();
                markup = "<b>World choice</b> " + Tint(Green, "Confirmed choice: " + name);
                full = "Confirmed choice: " + name + ". Open the full window to use it.";
            } else if (feedback.Messages.Count != 0) {
                var message = feedback.Messages[0];
                var text = WithoutSource(message);
                var tone = message.Recovery != null ? (CommandBand.RecoveryWarning(message.Recovery.Kind) ? Amber : Red) : ToneOf(message.Text);
                title = message.Source;
                markup = (string.IsNullOrEmpty(message.Source) ? "" : "<b>" + Literal(message.Source) + "</b> ") + (tone == null ? Literal(text) : Tint(tone, text));
                full = text;
            } else {
                title = "World choice";
                full = "No world choice is pending.";
                markup = Literal(full);
            }
            Line(builder, markup, title, full, RowHeight);
            const string none = "No world choice is pending.";
            var acts = pending != null || gizmo?.Active == true ? FooterActs() : new[] {
                Slot("Confirm", "Confirm the candidate's identity. Confirming changes nothing.", none, () => { }),
                Slot("Cancel", "Cancel the world choice.", none, () => { }),
                Slot("Focus", "Move the camera to the candidate.", none, () => { }),
                Slot("Details", "Show or hide the clicked candidate's identity in the line above.", none, () => { }, "Candidate details") };
            ActionRow(builder, acts, null);
        }

        static string PrototypeTabTitle(string id) => id == PlacementTab ? "Placement" : id == ConnectionsTab ? "Connections" : id == ChangesTab ? "Changes" : "Object";

        void PrototypeLog(string state) {
            var pending = selection?.Pending;
            context?.Log.Info("Compact prototype: " + state + "; inspected=" + (selected == null ? "none" : selected.DisplayName + " (" + selected.Identity.Path + ")") +
                "; pending world choice=" + (pending == null ? "no" : "yes (" + pending.Kind + ", candidate " + (selection.Find(pending.Candidate) == null ? "none" : "clicked") + ")"));
        }
    }
}
