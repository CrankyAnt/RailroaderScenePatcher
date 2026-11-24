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
    // The compact window's content: one context-aware side rail. It keeps the active edit without changing tabs or
    // dropping state: Refresh and status lamps, the four band commands on one row, the object's name and state with a
    // read-only mode label, a context section that follows the active tab or guided task, the footer's message area and
    // an action row in the footer's pattern, and an outliner that can be hidden, with its summary line. The message area,
    // the bands, the actions and the outliner rows are the full window's own.
    public sealed partial class PatcherBehaviour {
        // The one field of construction segments, and the coverage form's fields per role, whose first is the segment.
        const int CompactSegmentIdsField = 0, CompactCoverageFieldsPerRole = 4;

        // What the rail shows for the active tab or guided task: its name, its context section and its idle actions.
        sealed class CompactView {
            internal string Title;
            internal Action<UIPanelBuilder> Context;
            internal Func<Act[]> Acts;
            // The Placement body fills the space it is given; every other context has the least lines below.
            internal bool Fills;
        }

        // The one place that maps the active tab or guided task to the rail's view.
        CompactView CompactCurrentView() {
            if (guided.Enabled) return new CompactView { Title = "Guided", Context = CompactGuidedContext, Acts = CompactGuidedActs };
            switch (inspectorTab.Value) {
                case PlacementTab: return new CompactView { Title = "Placement", Context = CompactPlacementContext, Acts = () => TabActs(PlacementTab), Fills = true };
                case ConnectionsTab: return new CompactView { Title = "Connections", Context = CompactConnectionsContext, Acts = CompactConnectionsActs };
                case ChangesTab: return new CompactView { Title = "Changes", Context = CompactChangesContext, Acts = CompactChangesActs };
                default: return new CompactView { Title = "Object", Context = CompactObjectContext, Acts = CompactObjectActs };
            }
        }

        void DrawCompact(UIPanelBuilder builder) {
            typed.Clear();
            gizmoPlacement = null;
            gizmoDragMessage = null;
            builder.ConfigureLayout(4f);
            var view = CompactCurrentView();
            CompactLeftBand(builder);
            DrawRightBand(builder);
            CompactHeading(builder, view);
            if (FillsRail(view)) view.Context(builder);
            else CompactContextBlock(builder, view);
            CompactFooter(builder, view);
            CompactOutlinerSection(builder);
            builder.Spacer(CompactRail.GripClearance);
        }

        // The left band at the rail's width. The full window's width stays as it was for its next build.
        void CompactLeftBand(UIPanelBuilder builder) {
            var fullWidth = leftBandWidth;
            leftBandWidth = CompactRail.Width;
            try { DrawLeftBand(builder); } finally { leftBandWidth = fullWidth; }
        }

        // The name with a read-only mode label at its right, instead of the live Inspector/Guided switch, then the state
        // line across the rail's whole width: a state line beside the label lost its family name to the ellipsis.
        void CompactHeading(UIPanelBuilder builder, CompactView view) {
            builder.HStack(row => {
                if (selected == null) Heading(row, "No Selection", null, null).EqualFlexibleWidth(0);
                else ObjectHeading(row).EqualFlexibleWidth(0);
                CompactModeLabel(row, view);
            }).ChildrenKeepHeight().Height(RowHeight);
            var state = StateLine(out var states);
            // Without an inspected object the state line is an instruction, which wraps so it shows in full at the
            // rail's width.
            if (selected == null) WrappedLine(builder, state);
            else StateLineRow(builder, state, states);
        }

        void CompactModeLabel(UIPanelBuilder row, CompactView view) {
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

        // A line that wraps at the rail's width and takes the height of its lines, for an instruction that has to show
        // in full.
        static RectTransform WrappedLine(UIPanelBuilder builder, string markup) =>
            builder.AddLabel(markup).TextWrap(TextOverflowModes.Overflow, TextWrappingModes.Normal);

        // ---- Context sections ----

        // A context with fewer lines than most views show keeps their height (CompactRail.ContextPadding), so inspecting
        // an object after a world choice or the hint does not move the message area, the actions, or the outliner.
        void CompactContextBlock(UIPanelBuilder builder, CompactView view) {
            var block = builder.VStack(column => {
                column.ConfigureLayout(4f);
                if (guided.Enabled || selected != null) view.Context(column);
                else WrappedLine(column, Literal("Select a row in the outliner or click Select in world."));
            });
            var padding = CompactRail.ContextPadding(block.childCount, LineHeight, 4f);
            if (padding > 0) builder.Spacer(padding);
        }

        void CompactPlacementContext(UIPanelBuilder builder) {
            var collapsed = !presentation.CompactOutlinerShown;
            var minHeight = collapsed ? CompactRail.PlacementCollapsedBody : CompactRail.PlacementBody;
            var body = presentation.Scroll(builder, minHeight, content => {
                content.ConfigureLayout(4f);
                DrawPlacement(content);
            });
            CompactWrapLines(body);
            var fill = body.GetComponent<LayoutElement>();
            if (fill != null) {
                fill.minHeight = minHeight;
                fill.preferredHeight = CompactRail.PlacementBody;
                fill.flexibleHeight = collapsed ? 10000 : 0;
            }
            var viewport = body.GetComponent<ScrollRect>().viewport;
            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null) viewport.gameObject.AddComponent<RectMask2D>();
        }

        // The Placement body's lines, such as the Snap and Follow hints, are one line high with an ellipsis, sized for the
        // full window's inspector. In the rail's narrower body they wrap instead and take the height they need; the body
        // scrolls. Only the rail's copy of the body changes.
        static void CompactWrapLines(RectTransform body) {
            foreach (var text in body.GetComponentsInChildren<TMP_Text>(true)) {
                if (text.overflowMode != TextOverflowModes.Ellipsis || text.textWrappingMode != TextWrappingModes.NoWrap) continue;
                if (text.GetComponentInParent<TMP_InputField>() != null) continue;
                text.overflowMode = TextOverflowModes.Overflow; text.textWrappingMode = TextWrappingModes.Normal;
                var layout = text.GetComponent<LayoutElement>();
                if (layout != null) { layout.minHeight = -1; layout.preferredHeight = -1; }
            }
        }

        void CompactObjectContext(UIPanelBuilder builder) {
            CompactHonestPlacement(builder);
            if (selected.Problems.Count == 0) return;
            var first = selected.Problems[0];
            var suffix = selected.Problems.Count > 1 ? " (+" + (selected.Problems.Count - 1) + " more)" : "";
            Line(builder, "<color=" + Red + "><b>Problem</b></color> " + Literal(first + suffix),
                "Detected problems", String.Join("\n", selected.Problems), LineHeight);
        }

        void CompactConnectionsContext(UIPanelBuilder builder) {
            if (IsStarter) CompactStarterLines(builder);
            else if (HoldsConstructionSegments) CompactSegmentsContext(builder);
            else if (selected.Identity.Structure == CrossingCoverageScene.Structure) CompactCoverageContext(builder);
            else CompactBindingContext(builder);
        }

        void CompactSegmentsContext(UIPanelBuilder builder) {
            var ids = segmentFields[CompactSegmentIdsField];
            var segments = String.IsNullOrWhiteSpace(ids) ? "None" : ids;
            Line(builder, "<style=FieldLabel>SEGMENTS</style> " + Literal(segments), "Construction segments", "Current construction segments: " + segments, LineHeight);
            CompactPositionOnlyLine(builder);
        }

        void CompactCoverageContext(UIPanelBuilder builder) {
            if (coverageCircuit != null) {
                var label = coverageAddition == null ? "Circuit " + coverageCircuit : "Detection addition " + coverageAddition;
                Line(builder, "<b>" + Literal(label) + "</b>", "Crossing coverage", "Editing " + label + ". Choose each role's segment in the world, then switch to the full window to preview and apply.", LineHeight);
                var roles = Enumerable.Range(0, CircuitCoverage.Roles.Length).Select(role => {
                    var segment = coverageFields[role * CompactCoverageFieldsPerRole];
                    return CircuitCoverage.RoleCaption(CircuitCoverage.Roles[role]) + ": " + (String.IsNullOrWhiteSpace(segment) ? "none" : segment);
                }).ToArray();
                // One line per role: the three on one line cut off the last role.
                foreach (var role in roles) Line(builder, Literal(role), "Role segments", String.Join("\n", roles), LineHeight);
            } else if (equipmentSource != null) {
                Line(builder, "<b>Equipment form</b> " + Literal(equipmentLabel ?? EquipmentKind), "Equipment form", "Switch to the full window to preview or apply this equipment form.", LineHeight);
                CompactPositionOnlyLine(builder);
            } else {
                CompactPositionOnlyLine(builder);
                Line(builder, "<style=Footnote>Switch to the full window to edit coverage or equipment.</style>", null, null, LineHeight);
            }
        }

        void CompactBindingContext(UIPanelBuilder builder) {
            CompactBindingLine(builder);
            if (HasPickedBinding) {
                var pickedText = CompactRail.LocationText(binding);
                Line(builder, "<style=FieldLabel>PICKED</style> " + Tint(Green, pickedText), "Picked track location", "Picked track location ready for Set: " + pickedText + ".", LineHeight);
            } else if (IsPad) CompactEnabledLine(builder);
            else CompactPositionOnlyLine(builder);
        }

        void CompactChangesContext(UIPanelBuilder builder) {
            var changed = IsChanged(selected);
            var shownFailure = FailureOf(selected);
            var badge = shownFailure != null ? CommandBand.Badges(changed, false, shownFailure)[0].Markup : changed ? ChangedBadge : "Unchanged";
            Line(builder, "<style=FieldLabel>THIS MOD</style> " + badge, "Changes", "Whether Data/scene-patches.json of the edited mod holds a modification of this object.", LineHeight);
            if (changed && attachment != null) CompactChangedSettings(builder);
            else CompactHonestPlacement(builder);
            if (!HasTerrain) return;
            var terrain = TerrainLine(out var failed);
            Line(builder, failed ? Tint(Red, terrain) : Literal(terrain), "Terrain", terrain, LineHeight);
        }

        // Up to two changed settings; the tooltip holds them all.
        void CompactChangedSettings(UIPanelBuilder builder) {
            IReadOnlyList<string> lines = null;
            try { lines = attachment.Session.Changes(selected.Identity); } catch { }
            if (lines == null || lines.Count == 0) return;
            var full = String.Join("\n", lines);
            Line(builder, Literal(lines[0]), "Changed settings", full, LineHeight);
            if (lines.Count == 1) return;
            var second = lines.Count == 2 ? lines[1] : lines[1] + " (+" + (lines.Count - 2) + " more)";
            Line(builder, Literal(second), "Changed settings", full, LineHeight);
        }

        void CompactGuidedContext(UIPanelBuilder builder) {
            // The name's row already says Guided, so the line names only the task and its step; the longest,
            // "Snap to track or endpoint (Review)", fits the rail's width.
            var summary = guided.Kind == null ? "No task chosen yet" : GuidedName(guided.Kind.Value) + " (" + guided.Step + ")";
            Line(builder, "<b>" + Literal(summary) + "</b>", "Guided task", summary, LineHeight);
            if (selected != null) CompactHonestPlacement(builder);
            if (GuidedSnapValues) {
                var snapText = preparedSnap?.Identity.ToString() ?? "None chosen";
                Line(builder, "<style=FieldLabel>SNAP TARGET</style> " + Literal(snapText), "Snap target", snapText, LineHeight);
            } else if (GuidedTrackValues) {
                var draft = CompactRail.LocationText(guidedFields, "None entered");
                Line(builder, "<style=FieldLabel>DRAFT LOCATION</style> " + Literal(draft), "Draft track location", draft, LineHeight);
            }
            Line(builder, "<style=Footnote>" + (guided.Kind == null ? "Choose a task in the full window." : "Switch to the full window to finish this guided task.") + "</style>", null, null, LineHeight);
        }

        // The current track binding on one line: a crossing pad's, which places the pad, or a marker's.
        void CompactBindingLine(UIPanelBuilder builder) {
            var bindingText = CompactRail.LocationText(CurrentBinding());
            Line(builder, "<style=FieldLabel>" + (IsPad ? "PAD BINDING" : "TRACK BINDING") + "</style> " + Literal(bindingText),
                IsPad ? "Crossing pad binding" : "Track binding",
                IsPad ? "Track location: " + bindingText + ". The pad is generated along this binding and moves by it, not by its object position."
                    : "Track location: " + bindingText + ". Changing a binding does not move the object.", LineHeight);
        }

        // ---- Placement readouts ----

        // Honest placement lines with units: the track point of a starter placement marker, the binding of a crossing
        // pad, and position and rotation on two lines for an object with a placement of its own.
        void CompactHonestPlacement(UIPanelBuilder builder) {
            if (IsStarter) { CompactStarterLines(builder); return; }
            if (IsPad) {
                CompactBindingLine(builder);
                CompactEnabledLine(builder);
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
                CompactEnabledLine(builder);
                return;
            }
            CompactPositionOnlyLine(builder);
            var rotation = CommandBand.RunningLine(CommandBand.Axes, values.Skip(3).Take(3).ToArray(), CommandBand.PlacementUnits[1]);
            Line(builder, "<style=FieldLabel>ROTATION</style> " + Literal(rotation), "Rotation", "Rotation in degrees: " + rotation + ".", LineHeight);
            CompactEnabledLine(builder);
        }

        void CompactPositionOnlyLine(UIPanelBuilder builder) {
            var values = CurrentPlacement();
            if (values == null || values.Length < 3) return;
            var position = CommandBand.RunningLine(CommandBand.Axes, values.Take(3).ToArray(), CommandBand.PlacementUnits[0]);
            Line(builder, "<style=FieldLabel>POSITION</style> " + Literal(position), "Position", "Position in map coordinates: " + position + ".", LineHeight);
        }

        void CompactStarterLines(UIPanelBuilder builder) {
            var point = InspectedTrackPoint();
            var pointText = point == null
                ? "None (" + (FocusUnavailable() ?? StarterPlacements.NoTrackPoint) + ")"
                : CommandBand.RunningLine(CommandBand.Axes, point.Select(CommandBand.Number).ToArray(), CommandBand.PlacementUnits[0]);
            Line(builder, "<style=FieldLabel>TRACK POINT</style> " + Literal(pointText), "Track point",
                "Where the start setup places the front of this marker's car cut, in map coordinates: " + pointText + ". The marker's object position is not used.", LineHeight);
            var picked = HasPickedBinding;
            var locationText = CompactRail.LocationText(picked ? binding : CurrentBinding());
            Line(builder, "<style=FieldLabel>" + (picked ? "PICKED LOCATION" : "TRACK LOCATION") + "</style> " + (picked ? Tint(Green, locationText) : Literal(locationText)),
                picked ? "Picked track location" : "Track location",
                (picked ? "Picked track location ready for Set: " : "Registered track location: ") + locationText + ".", LineHeight);
        }

        void CompactEnabledLine(UIPanelBuilder builder) {
            var text = EnabledText();
            Line(builder, "<style=FieldLabel>ENABLED</style> " + Literal(text), "Enabled", "Whether this object is active in the scene: " + text + ".", LineHeight);
        }

        // ---- Messages and actions ----

        // The footer's own message area, then one action row: a world choice or a gizmo drag takes it over, as in the
        // footer; otherwise it holds the active view's actions.
        // Whether the view's context, the Placement body of an inspected object, takes the rail's free height.
        bool FillsRail(CompactView view) => view.Fills && selected != null;

        void CompactFooter(UIPanelBuilder builder, CompactView view) {
            var area = DrawMessages(builder, CompactRail.MessageHeight);
            // The message area takes the height the rows above leave free, so a short context, such as the Object tab's
            // three lines, gives its room to the messages and never to the outliner. With the outliner hidden, the
            // Placement body takes that height instead.
            area.flexibleHeight = presentation.CompactOutlinerShown || !FillsRail(view) ? 10000 : 0;
            var acts = selection?.Pending != null || gizmo?.Active == true ? FooterActs() : selected == null && !guided.Enabled ? new Act[0] : view.Acts();
            ActionRow(builder, acts, null);
        }

        // The Values step's world choices; Segment keeps the active tab.
        Act[] CompactGuidedActs() => GuidedWorldActsInPlace(Requests.Pending ? "Wait for the scene check to finish." : null);

        // Enable or Disable and Clear activity from the Object tab, and Select group or Select controller.
        Act[] CompactObjectActs() {
            var acts = new List<Act>(TabActs(ObjectTab));
            if (InventoryPresentationState.PartOf(selected) != null) acts.Add(SelectGroupAct());
            if (InventoryPresentationState.HeldBy(selected) != null) acts.Add(SelectControllerAct());
            return acts.ToArray();
        }

        // The buttons that start a world choice, then Set and Picked or Cancel from the tab's track location row. An
        // open coverage form has its three role choices instead; a crossing's coverage and equipment rows stay in the
        // full window.
        Act[] CompactConnectionsActs() {
            var coverage = selected.Identity.Structure == CrossingCoverageScene.Structure;
            if (coverage && coverageCircuit != null)
                return Enumerable.Range(0, CircuitCoverage.Roles.Length).Select(role => CoverageRoleAct(role, CircuitCoverage.RoleCaption(CircuitCoverage.Roles[role]), CoveragePickBlock)).ToArray();
            if (coverage && attachment != null) return ReplacementActs();
            var row = TrackLocationActs(EditBlock());
            return ReplacementActs().Concat(new[] { row.Set, row.Pick }).ToArray();
        }

        // Retry from the Changes tab, then Recheck and Guide from the footer; Remove modification stays in the full window.
        Act[] CompactChangesActs() => TabActs(ChangesTab).Concat(new[] { RecheckAct(), GuideAct() }).ToArray();

        // ---- Outliner ----

        // The outliner's object count with Hide outliner or Show outliner at its right, above the outliner rows. The
        // filter cannot be changed or reset here: the compact window has no search field, filters or view switch, so
        // these mean switching to the full window. The rows follow the view chosen there, by category or by place.
        void CompactOutlinerSection(UIPanelBuilder builder) {
            builder.AddHRule();
            var filtered = !String.IsNullOrEmpty(filter) || outlinerFilter != OutlinerFilter.All;
            var filterParts = new List<string>();
            if (outlinerFilter != OutlinerFilter.All) filterParts.Add(CompactRail.FilterName(outlinerFilter));
            if (!String.IsNullOrEmpty(filter)) filterParts.Add("\"" + filter + "\"");
            Func<string, string> countMarkup = summary => filtered
                ? "<style=Footnote><color=" + Amber + ">" + Literal(summary) + "</color> (" + Literal(String.Join(", ", filterParts)) + ")</style>"
                : "<style=Footnote>" + Literal(summary) + "</style>";
            var labelTip = filtered
                ? "Active outliner filter: " + String.Join(", ", filterParts) + ". Switch to the full window to change or reset the filter."
                : "Inactive loaded objects are included.";
            var open = presentation.CompactOutlinerShown;
            // With the outliner shown, its rows set the count as they build the tree; hidden, the count is built here.
            var initial = open ? "" : countMarkup(OutlinerSummary(OutlinerTree(out _)));
            TMP_Text countLine = null;
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                // A long filter ends in an ellipsis beside the button; the tooltip names the whole filter.
                var countRect = Line(row, initial, "Objects", labelTip, RowHeight);
                countRect.EqualFlexibleWidth(0);
                countLine = countRect.GetComponent<TMP_Text>() ?? countRect.GetComponentInChildren<TMP_Text>(true);
                var toggle = Button(row, open ? "Hide outliner" : "Show outliner", () => presentation.ToggleCompactOutliner());
                FitButton(toggle, "Hide outliner", "Show outliner");
                toggle.Tooltip(open ? "Hide outliner" : "Show outliner", open ? "Hide the outliner to leave more of the world in view." : "Show the outliner in this rail.");
            }).ChildrenKeepHeight().Height(RowHeight);
            if (!open) return;
            // Exactly the presentation's whole rows: six at the default height, more when the user made the rail higher.
            var outliner = builder.VStack(column => DrawOutlinerRows(column, countLine, countMarkup)).gameObject.AddComponent<LayoutElement>();
            outliner.minHeight = outliner.preferredHeight = presentation.CompactOutlinerHeight;
            outliner.flexibleHeight = 0; outliner.flexibleWidth = 10000;
        }

        // One debug line per mode or outliner switch: the presentation's part, the active tab or guided task, the
        // inspected object, and a pending world choice.
        void LogCompactSwitch(string state) {
            var pending = selection?.Pending;
            var view = CompactCurrentView();
            var contextName = guided.Enabled ? view.Title + " (" + (guided.Kind == null ? "Target" : GuidedName(guided.Kind.Value) + "/" + guided.Step) + ")" : view.Title;
            PatcherLog.Debug("Compact window: " + state + "; tab=" + contextName +
                "; inspected=" + (selected == null ? "none" : selected.DisplayName + " (" + selected.Identity.Path + ")") +
                "; pending world choice=" + (pending == null ? "no" : "yes (" + pending.Kind + ", candidate " + (selection.Find(pending.Candidate) == null ? "none" : "clicked") + ")"));
        }
    }
}
