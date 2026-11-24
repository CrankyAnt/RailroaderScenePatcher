// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using TMPro;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // PROTOTYPE (throwaway): a single context-aware side rail for compact mode. Not production code.
    // Keeps the active edit without changing tabs or dropping state: Refresh and status lamps, the four band commands
    // on one row, the object's name and state with a read-only mode label, a context section that follows the active
    // tab or guided task, the footer's message area and an action row in the footer's pattern, and a collapsible
    // outliner with a filter-status bar. The message area, the buttons and the outliner rows are the full window's own.
    public sealed partial class PatcherBehaviour {
        // The fields of a track location (binding, guidedFields): segment, end, distance.
        const int PrototypeSegmentField = 0, PrototypeEndField = 1, PrototypeDistanceField = 2;
        // The one field of construction segments, and the coverage form's fields per role, whose first is the segment.
        const int PrototypeSegmentIdsField = 0, PrototypeCoverageFieldsPerRole = 4;
        // The production Connections actions the rail keeps: Set, and Picked or Cancel in the same slot. Detach and the
        // Clear actions stay in the full window.
        static readonly string[] PrototypeConnectionsKept = { "Set", "Picked", "Cancel" };

        // What the rail shows for the active tab or guided task: its name, its context section and its idle actions.
        sealed class PrototypeView {
            internal string Title;
            internal Action<UIPanelBuilder> Context;
            internal Func<Act[]> Acts;
            // The Placement body fills the space it is given; every other context has the least lines below.
            internal bool Fills;
        }

        // The one place that maps the active tab or guided task to the rail's view.
        PrototypeView PrototypeCurrentView() {
            if (guided.Enabled) return new PrototypeView { Title = "Guided", Context = PrototypeGuidedContext, Acts = PrototypeGuidedActs };
            switch (inspectorTab.Value) {
                case PlacementTab: return new PrototypeView { Title = "Placement", Context = PrototypePlacementContext, Acts = () => TabActs(PlacementTab), Fills = true };
                case ConnectionsTab: return new PrototypeView { Title = "Connections", Context = PrototypeConnectionsContext, Acts = PrototypeConnectionsActs };
                case ChangesTab: return new PrototypeView { Title = "Changes", Context = PrototypeChangesContext, Acts = PrototypeChangesActs };
                default: return new PrototypeView { Title = "Object", Context = PrototypeObjectContext, Acts = PrototypeObjectActs };
            }
        }

        bool PrototypeGuidedSnap => guided.Step == GuidedTaskStep.Values && guided.Kind == GuidedTaskKind.Snap;
        bool PrototypeGuidedTrack => guided.Step == GuidedTaskStep.Values && (guided.Kind == GuidedTaskKind.Binding || guided.Kind == GuidedTaskKind.StarterPlacement);

        void DrawCompactPrototype(UIPanelBuilder builder) {
            typed.Clear();
            gizmoPlacement = null;
            gizmoDragMessage = null;
            builder.ConfigureLayout(4f);
            var view = PrototypeCurrentView();
            PrototypeLeftBand(builder);
            DrawRightBand(builder);
            PrototypeHeading(builder, view);
            if (view.Fills && selected != null) view.Context(builder);
            else PrototypeContextBlock(builder, view);
            PrototypeFooter(builder, view);
            PrototypeOutlinerSection(builder);
            builder.Spacer(NativePresentation.PrototypeGripClearance);
        }

        // The left band at the rail's width. The full window's width stays as it was for its next build.
        void PrototypeLeftBand(UIPanelBuilder builder) {
            var fullWidth = leftBandWidth;
            leftBandWidth = NativePresentation.PrototypeRailContent;
            try { DrawLeftBand(builder); } finally { leftBandWidth = fullWidth; }
        }

        // The name with a read-only mode label at its right, instead of the live Inspector/Guided switch, then the state
        // line across the rail's whole width: a state line beside the label lost its family name to the ellipsis.
        void PrototypeHeading(UIPanelBuilder builder, PrototypeView view) {
            builder.HStack(row => {
                if (selected == null) Heading(row, "No Selection", null, null).EqualFlexibleWidth(0);
                else {
                    var name = selected.DisplayName;
                    var added = IsAdded(selected);
                    var heading = Heading(row, Literal(name), "Object", added ? name : selected.Identity.Path).EqualFlexibleWidth(0);
                    if (added) FitEquipmentText(heading.GetComponent<TMP_Text>(), selected.Family, name, value => "<style=H3>" + Literal(value) + "</style>");
                }
                PrototypeModeLabel(row, view);
            }).ChildrenKeepHeight().Height(RowHeight);
            var state = StateLine(out var states);
            if (selected == null) Line(builder, state, null, null, RowHeight);
            else Line(builder, state, "State", states, RowHeight);
        }

        void PrototypeModeLabel(UIPanelBuilder row, PrototypeView view) {
            var text = guided.Enabled ? "<color=" + Blue + "><b>Guided</b></color>" : "<alpha=#99>" + Literal(view.Title) + "<alpha=#FF>";
            var title = guided.Enabled ? "Guided mode" : "Active tab";
            var tip = guided.Enabled
                ? "Guided mode is active. Switch to the full window to finish or leave the guided task."
                : "Compact mode follows the " + view.Title + " tab. Switch to the full window to change tabs or modes.";
            var rect = row.AddLabel(text, label => label.alignment = TextAlignmentOptions.Right)
                .TextWrap(TextOverflowModes.Overflow, TextWrappingModes.NoWrap).Height(RowHeight).Tooltip(title, tip);
            var layout = rect.gameObject.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 0;
        }

        // ---- Context sections ----

        // The context lines most views show: position, rotation, and enabled for an object with a placement of its own.
        // A context with fewer lines keeps that height, so inspecting an object after a world choice or the hint does
        // not move the message area, the actions, or the outliner. A context with more lines, such as a problem, a
        // changed object's settings, or a guided task, still grows.
        const int PrototypeContextLines = 3;

        void PrototypeContextBlock(UIPanelBuilder builder, PrototypeView view) {
            var block = builder.VStack(column => {
                column.ConfigureLayout(4f);
                if (guided.Enabled || selected != null) view.Context(column);
                else Line(column, Literal("Select a row in the outliner or click Select in world."), null, null, LineHeight);
            });
            var lines = block.childCount;
            if (lines < PrototypeContextLines) builder.Spacer((PrototypeContextLines - lines) * (LineHeight + 4f) - 4f);
        }

        void PrototypePlacementContext(UIPanelBuilder builder) {
            var collapsed = !presentation.PrototypeOutlinerOpen;
            var minHeight = collapsed ? NativePresentation.PrototypePlacementCollapsedBody : NativePresentation.PrototypePlacementBody;
            var body = presentation.Scroll(builder, minHeight, content => {
                content.ConfigureLayout(4f);
                DrawPlacement(content);
            });
            PrototypeWrapLines(body);
            var fill = body.GetComponent<LayoutElement>();
            if (fill != null) {
                fill.minHeight = minHeight;
                fill.preferredHeight = NativePresentation.PrototypePlacementBody;
                fill.flexibleHeight = collapsed ? 10000 : 0;
            }
            var viewport = body.GetComponent<ScrollRect>().viewport;
            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null) viewport.gameObject.AddComponent<RectMask2D>();
        }

        // The Placement body's lines, such as the Snap and Follow hints, are one line high with an ellipsis, sized for the
        // full window's inspector. In the rail's narrower body they wrap instead and take the height they need; the body
        // scrolls. Only the rail's copy of the body changes.
        static void PrototypeWrapLines(RectTransform body) {
            foreach (var text in body.GetComponentsInChildren<TMP_Text>(true)) {
                if (text.overflowMode != TextOverflowModes.Ellipsis || text.textWrappingMode != TextWrappingModes.NoWrap) continue;
                if (text.GetComponentInParent<TMP_InputField>() != null) continue;
                text.overflowMode = TextOverflowModes.Overflow; text.textWrappingMode = TextWrappingModes.Normal;
                var layout = text.GetComponent<LayoutElement>();
                if (layout != null) { layout.minHeight = -1; layout.preferredHeight = -1; }
            }
        }

        void PrototypeObjectContext(UIPanelBuilder builder) {
            PrototypeHonestPlacement(builder);
            if (selected.Problems.Count == 0) return;
            var first = selected.Problems[0];
            var suffix = selected.Problems.Count > 1 ? " (+" + (selected.Problems.Count - 1) + " more)" : "";
            Line(builder, "<color=" + Red + "><b>Problem</b></color> " + Literal(first + suffix),
                "Detected problems", String.Join("\n", selected.Problems), LineHeight);
        }

        void PrototypeConnectionsContext(UIPanelBuilder builder) {
            if (IsStarter) PrototypeStarterLines(builder);
            else if (HoldsConstructionSegments) PrototypeSegmentsContext(builder);
            else if (selected.Identity.Structure == CrossingCoverageScene.Structure) PrototypeCoverageContext(builder);
            else PrototypeBindingContext(builder);
        }

        void PrototypeSegmentsContext(UIPanelBuilder builder) {
            var ids = segmentFields[PrototypeSegmentIdsField];
            var segments = String.IsNullOrWhiteSpace(ids) ? "None" : ids;
            Line(builder, "<style=FieldLabel>SEGMENTS</style> " + Literal(segments), "Construction segments", "Current construction segments: " + segments, LineHeight);
            PrototypePositionOnlyLine(builder);
        }

        void PrototypeCoverageContext(UIPanelBuilder builder) {
            if (coverageCircuit != null) {
                var label = coverageAddition == null ? "Circuit " + coverageCircuit : "Detection addition " + coverageAddition;
                Line(builder, "<b>" + Literal(label) + "</b>", "Crossing coverage", "Editing " + label + ". Choose each role's segment in the world, then switch to the full window to preview and apply.", LineHeight);
                var roles = Enumerable.Range(0, CircuitCoverage.Roles.Length).Select(role => {
                    var segment = coverageFields[role * PrototypeCoverageFieldsPerRole];
                    return CircuitCoverage.RoleCaption(CircuitCoverage.Roles[role]) + ": " + (String.IsNullOrWhiteSpace(segment) ? "none" : segment);
                }).ToArray();
                // One line per role: the three on one line cut off the last role.
                foreach (var role in roles) Line(builder, Literal(role), "Role segments", String.Join("\n", roles), LineHeight);
            } else if (equipmentSource != null) {
                Line(builder, "<b>Equipment form</b> " + Literal(equipmentLabel ?? EquipmentKind), "Equipment form", "Switch to the full window to preview or apply this equipment form.", LineHeight);
                PrototypePositionOnlyLine(builder);
            } else {
                PrototypePositionOnlyLine(builder);
                Line(builder, "<style=Footnote>Switch to the full window to edit coverage or equipment.</style>", null, null, LineHeight);
            }
        }

        void PrototypeBindingContext(UIPanelBuilder builder) {
            var bindingText = PrototypeLocationText(CurrentBinding());
            Line(builder, "<style=FieldLabel>" + (IsPad ? "PAD BINDING" : "TRACK BINDING") + "</style> " + Literal(bindingText),
                IsPad ? "Crossing pad binding" : "Track binding",
                IsPad ? "Track location: " + bindingText + ". The pad's geometry is generated along this binding." : "Track location: " + bindingText + ". Changing a binding does not move the object.", LineHeight);
            if (HasPickedBinding) {
                var pickedText = PrototypeLocationText(binding);
                Line(builder, "<style=FieldLabel>PICKED</style> " + Tint(Green, pickedText), "Picked track location", "Picked track location ready for Set: " + pickedText + ".", LineHeight);
            } else if (IsPad) PrototypeEnabledLine(builder);
            else PrototypePositionOnlyLine(builder);
        }

        void PrototypeChangesContext(UIPanelBuilder builder) {
            var changed = IsChanged(selected);
            var shownFailure = FailureOf(selected);
            var badge = shownFailure != null ? CommandBand.Badges(changed, false, shownFailure)[0].Markup : changed ? ChangedBadge : "Unchanged";
            Line(builder, "<style=FieldLabel>THIS MOD</style> " + badge, "Changes", "Whether Data/scene-patches.json of the edited mod holds a modification of this object.", LineHeight);
            if (changed && attachment != null) PrototypeChangedSettings(builder);
            else PrototypeHonestPlacement(builder);
            if (!HasTerrain) return;
            var problem = (string)null;
            var output = attachment == null || !Editable ? DependentOutput.None : inspectedOutput.Of(attachment.Session, selected, out problem);
            var terrain = CommandBand.Terrain(problem, output.Error, output.Settled, Join(output.Completed), Join(output.Outstanding), output.Completed.Count != 0);
            Line(builder, problem != null ? Tint(Red, terrain) : Literal(terrain), "Terrain", terrain, LineHeight);
        }

        // Up to two changed settings; the tooltip holds them all.
        void PrototypeChangedSettings(UIPanelBuilder builder) {
            IReadOnlyList<string> lines = null;
            try { lines = attachment.Session.Changes(selected.Identity); } catch { }
            if (lines == null || lines.Count == 0) return;
            var full = String.Join("\n", lines);
            Line(builder, Literal(lines[0]), "Changed settings", full, LineHeight);
            if (lines.Count == 1) return;
            var second = lines.Count == 2 ? lines[1] : lines[1] + " (+" + (lines.Count - 2) + " more)";
            Line(builder, Literal(second), "Changed settings", full, LineHeight);
        }

        void PrototypeGuidedContext(UIPanelBuilder builder) {
            // The name's row already says Guided, so the line names only the task and its step; the longest,
            // "Snap to track or endpoint (Review)", fits the rail's width.
            var summary = guided.Kind == null ? "No task chosen yet" : GuidedName(guided.Kind.Value) + " (" + guided.Step + ")";
            Line(builder, "<b>" + Literal(summary) + "</b>", "Guided task", summary, LineHeight);
            if (selected != null) PrototypeHonestPlacement(builder);
            if (PrototypeGuidedSnap) {
                var snapText = preparedSnap?.Identity.ToString() ?? "None chosen";
                Line(builder, "<style=FieldLabel>SNAP TARGET</style> " + Literal(snapText), "Snap target", snapText, LineHeight);
            } else if (PrototypeGuidedTrack) {
                var draft = PrototypeLocationText(guidedFields, "None entered");
                Line(builder, "<style=FieldLabel>DRAFT LOCATION</style> " + Literal(draft), "Draft track location", draft, LineHeight);
            }
            Line(builder, "<style=Footnote>" + (guided.Kind == null ? "Choose a task in the full window." : "Switch to the full window to finish this guided task.") + "</style>", null, null, LineHeight);
        }

        // ---- Placement readouts ----

        // Honest placement lines with units: the track point of a starter placement marker, the binding of a crossing
        // pad, and position and rotation on two lines for an object with a placement of its own.
        void PrototypeHonestPlacement(UIPanelBuilder builder) {
            if (IsStarter) { PrototypeStarterLines(builder); return; }
            if (IsPad) {
                var bindingText = PrototypeLocationText(CurrentBinding());
                Line(builder, "<style=FieldLabel>PAD BINDING</style> " + Literal(bindingText), "Crossing pad binding",
                    "Track location: " + bindingText + ". A crossing pad moves by its track binding, not by its object position.", LineHeight);
                PrototypeEnabledLine(builder);
                return;
            }
            if (IsController) {
                Line(builder, Literal("Construction controller: no editable placement or activity."), "Controller",
                    "This controller sets visibility from its construction segments on the Connections tab.", LineHeight);
                return;
            }
            var values = CurrentPlacement();
            if (values == null || values.Length < 6) {
                Line(builder, Literal("This object has no placement of its own."), "Placement", "This object has no placement of its own.", LineHeight);
                PrototypeEnabledLine(builder);
                return;
            }
            PrototypePositionOnlyLine(builder);
            var rotation = CommandBand.RunningLine(CommandBand.Axes, values.Skip(3).Take(3).ToArray(), CommandBand.PlacementUnits[1]);
            Line(builder, "<style=FieldLabel>ROTATION</style> " + Literal(rotation), "Rotation", "Rotation in degrees: " + rotation + ".", LineHeight);
            PrototypeEnabledLine(builder);
        }

        void PrototypePositionOnlyLine(UIPanelBuilder builder) {
            var values = CurrentPlacement();
            if (values == null || values.Length < 3) return;
            var position = CommandBand.RunningLine(CommandBand.Axes, values.Take(3).ToArray(), CommandBand.PlacementUnits[0]);
            Line(builder, "<style=FieldLabel>POSITION</style> " + Literal(position), "Position", "Position in map coordinates: " + position + ".", LineHeight);
        }

        void PrototypeStarterLines(UIPanelBuilder builder) {
            var point = InspectedTrackPoint();
            var pointText = point == null
                ? "None (" + (FocusUnavailable() ?? StarterPlacements.NoTrackPoint) + ")"
                : CommandBand.RunningLine(CommandBand.Axes, point.Select(CommandBand.Number).ToArray(), CommandBand.PlacementUnits[0]);
            Line(builder, "<style=FieldLabel>TRACK POINT</style> " + Literal(pointText), "Track point",
                "Where the start setup places the front of this marker's car cut, in map coordinates: " + pointText + ". The marker's object position is not used.", LineHeight);
            var picked = HasPickedBinding;
            var locationText = PrototypeLocationText(picked ? binding : CurrentBinding());
            Line(builder, "<style=FieldLabel>" + (picked ? "PICKED LOCATION" : "TRACK LOCATION") + "</style> " + (picked ? Tint(Green, locationText) : Literal(locationText)),
                picked ? "Picked track location" : "Track location",
                (picked ? "Picked track location ready for Set: " : "Registered track location: ") + locationText + ".", LineHeight);
        }

        void PrototypeEnabledLine(UIPanelBuilder builder) {
            var text = EnabledText();
            Line(builder, "<style=FieldLabel>ENABLED</style> " + Literal(text), "Enabled", "Whether this object is active in the scene: " + text + ".", LineHeight);
        }

        // A track location's fields as one line, such as "Sqo0 / a / 12.5 m"; empty when no segment is entered.
        static string PrototypeLocationText(string[] fields, string empty = "None") {
            if (fields == null || fields.Length <= PrototypeDistanceField || String.IsNullOrWhiteSpace(fields[PrototypeSegmentField])) return empty;
            return fields[PrototypeSegmentField] + " / " + (fields[PrototypeEndField] ?? "").ToLowerInvariant() + " / " + fields[PrototypeDistanceField] + " m";
        }

        // ---- Messages and actions ----

        // The footer's own message area, then one action row: a world choice or a gizmo drag takes it over, as in the
        // footer; otherwise it holds the active view's actions.
        void PrototypeFooter(UIPanelBuilder builder, PrototypeView view) {
            var area = DrawMessages(builder, NativePresentation.PrototypeMessageHeight);
            var placementFills = inspectorTab.Value == PlacementTab && !guided.Enabled && selected != null;
            area.flexibleHeight = !presentation.PrototypeOutlinerOpen && !placementFills ? 10000 : 0;
            var acts = selection?.Pending != null || gizmo?.Active == true ? FooterActs() : selected == null && !guided.Enabled ? new Act[0] : view.Acts();
            ActionRow(builder, acts, null);
        }

        // The Values step's world choices; Segment keeps the active tab.
        Act[] PrototypeGuidedActs() => GuidedWorldActs(Requests.Pending ? "Wait for the scene check to finish." : null, keepTab: true);

        // Enable or Disable and Clear activity from the Object tab, and Select group or Select controller.
        Act[] PrototypeObjectActs() {
            var acts = new List<Act>(TabActs(ObjectTab));
            if (InventoryPresentationState.PartOf(selected) != null) acts.Add(SelectGroupAct());
            if (InventoryPresentationState.HeldBy(selected) != null) acts.Add(SelectControllerAct());
            return acts.ToArray();
        }

        // The buttons that start a world choice, then Set and Picked or Cancel from the tab's own row. An open coverage
        // form has its three role choices instead.
        Act[] PrototypeConnectionsActs() {
            if (selected.Identity.Structure == CrossingCoverageScene.Structure && coverageCircuit != null)
                return Enumerable.Range(0, CircuitCoverage.Roles.Length).Select(role => CoverageRoleAct(role, CircuitCoverage.RoleCaption(CircuitCoverage.Roles[role]), CoveragePickBlock)).ToArray();
            var kept = TabActs(ConnectionsTab).Where(act => PrototypeConnectionsKept.Contains(act.Label));
            // An open equipment form's row is Preview, Remove and Apply: none of them is kept.
            return ReplacementActs().Concat(kept).ToArray();
        }

        // Retry from the Changes tab and the footer's actions except Remove modification, chosen by name.
        Act[] PrototypeChangesActs() =>
            TabActs(ChangesTab).Concat(FooterActs().Where(act => act.Label != null && act.Label != CommandBand.RemoveModification)).ToArray();

        // ---- Outliner ----

        // The outliner's summary line with Hide outliner or Show outliner at its right, above the outliner rows. The
        // filter cannot be changed or reset here: the compact window has no search field or filters, so filtering
        // means switching to the full window.
        void PrototypeOutlinerSection(UIPanelBuilder builder) {
            builder.AddHRule();
            var filtered = !String.IsNullOrEmpty(filter) || outlinerFilter != OutlinerFilter.All;
            var summary = inventory == null ? "Inspection is unavailable" : InventoryPresentationState.Summary(inventory, filter, outlinerFilter, IsChanged);
            var filterParts = new List<string>();
            if (outlinerFilter != OutlinerFilter.All) filterParts.Add(PrototypeFilterName(outlinerFilter));
            if (!String.IsNullOrEmpty(filter)) filterParts.Add("\"" + filter + "\"");
            var labelMarkup = filtered
                ? "<style=Footnote><color=" + Amber + ">" + Literal(summary) + "</color> (" + Literal(String.Join(", ", filterParts)) + ")</style>"
                : "<style=Footnote>" + Literal(summary) + "</style>";
            var labelTip = filtered
                ? summary + ". Active outliner filter: " + String.Join(", ", filterParts) + ". Switch to the full window to change or reset the filter."
                : summary + ". Inactive loaded objects are included.";
            TMP_Text measure = null;
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                // A long filter ends in an ellipsis beside the button; the tooltip holds the whole summary.
                var summaryRect = Line(row, labelMarkup, "Outliner", labelTip, RowHeight);
                summaryRect.EqualFlexibleWidth(0);
                measure = summaryRect.GetComponent<TMP_Text>() ?? summaryRect.GetComponentInChildren<TMP_Text>(true);
                var open = presentation.PrototypeOutlinerOpen;
                var toggle = Button(row, open ? "Hide outliner" : "Show outliner", () => presentation.PrototypeToggleOutliner());
                FitButton(toggle, "Hide outliner", "Show outliner");
                toggle.Tooltip(open ? "Hide outliner" : "Show outliner", open ? "Hide the outliner to leave more of the world in view." : "Show the outliner in this rail.");
            }).ChildrenKeepHeight().Height(RowHeight);
            if (!presentation.PrototypeOutlinerOpen) return;
            var outliner = builder.VStack(column => DrawOutlinerRows(column, measure)).gameObject.AddComponent<LayoutElement>();
            outliner.flexibleHeight = 10000; outliner.flexibleWidth = 10000;
        }

        static string PrototypeFilterName(OutlinerFilter value) =>
            value == OutlinerFilter.Changed ? CommandBand.ChangedFilter
            : value == OutlinerFilter.Disabled ? CommandBand.DisabledFilter
            : value == OutlinerFilter.Problems ? CommandBand.ProblemsFilter
            : CommandBand.AllFilter;

        void PrototypeLog(string state) {
            var pending = selection?.Pending;
            var view = PrototypeCurrentView();
            var contextName = guided.Enabled ? view.Title + " (" + (guided.Kind == null ? "Target" : GuidedName(guided.Kind.Value) + "/" + guided.Step) + ")" : view.Title;
            PatcherLog.Debug("Compact prototype: " + state + "; tab=" + contextName +
                "; inspected=" + (selected == null ? "none" : selected.DisplayName + " (" + selected.Identity.Path + ")") +
                "; pending world choice=" + (pending == null ? "no" : "yes (" + pending.Kind + ", candidate " + (selection.Find(pending.Candidate) == null ? "none" : "clicked") + ")"));
        }
    }
}
