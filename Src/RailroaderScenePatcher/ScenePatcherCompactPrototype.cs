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
    // Keeps the active edit without changing tabs or dropping state: Refresh and status lamps, the four right-band
    // commands on one row, object name and state with a read-only mode label, a context section that follows the active
    // tab or guided task, a two-line scrolling choice and message area reusing the full footer's message priority, one
    // action row reusing the full footer's action pattern, and a collapsible outliner with a filter-status bar.
    public sealed partial class PatcherBehaviour {
        void DrawCompactPrototype(UIPanelBuilder builder) {
            typed.Clear();
            gizmoPlacement = null;
            gizmoDragMessage = null;
            builder.ConfigureLayout(4f);
            leftBandWidth = NativePresentation.PrototypeRailContent;
            DrawLeftBand(builder);
            DrawRightBand(builder);
            PrototypeHeading(builder);
            PrototypeContext(builder);
            PrototypeFooter(builder);
            PrototypeOutlinerSection(builder);
            builder.Spacer(CommandBand.GripClearance(GripHeight, RowPadding));
        }

        // Heading and state line with a read-only mode label instead of the live Inspector/Guided switch.
        void PrototypeHeading(UIPanelBuilder builder) {
            if (selected == null) {
                builder.HStack(row => {
                    Heading(row, "No Selection", null, null).EqualFlexibleWidth(0);
                    PrototypeModeLabel(row);
                }).ChildrenKeepHeight().Height(RowHeight);
                Line(builder, StateLine(out _), null, null, RowHeight);
                return;
            }
            var name = selected.DisplayName;
            var added = IsAdded(selected);
            var heading = Heading(builder, Literal(name), "Object", added ? name : selected.Identity.Path).Height(RowHeight);
            if (added) FitEquipmentText(heading.GetComponent<TMP_Text>(), selected.Family, name, value => "<style=H3>" + Literal(value) + "</style>");
            var state = StateLine(out var states);
            builder.HStack(row => {
                Line(row, state, "State", states, RowHeight).EqualFlexibleWidth(0);
                PrototypeModeLabel(row);
            }).ChildrenKeepHeight().Height(RowHeight);
        }

        void PrototypeModeLabel(UIPanelBuilder row) {
            var text = guided.Enabled ? "<color=" + Blue + "><b>Guided</b></color>" : "<alpha=#99>" + Literal(PrototypeTabTitle(inspectorTab.Value)) + "</alpha>";
            var title = guided.Enabled ? "Guided mode" : "Active tab";
            var tip = guided.Enabled
                ? "Guided mode is active. Switch to the full window to finish or leave the guided task."
                : "Compact mode follows the " + PrototypeTabTitle(inspectorTab.Value) + " tab. Switch to the full window to change tabs or modes.";
            var rect = row.AddLabel(text, label => label.alignment = TextAlignmentOptions.Right)
                .TextWrap(TextOverflowModes.Overflow, TextWrappingModes.NoWrap).Height(RowHeight).Tooltip(title, tip);
            var layout = rect.gameObject.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 0;
        }

        // Follows the active tab or guided task without mutating inspectorTab.
        void PrototypeContext(UIPanelBuilder builder) {
            if (guided.Enabled) { PrototypeGuidedContext(builder); return; }
            if (selected == null) {
                Line(builder, Literal("Select a row in the list or click Select in world."), null, null, RowHeight);
                return;
            }
            switch (inspectorTab.Value) {
                case PlacementTab: PrototypePlacementContext(builder); break;
                case ConnectionsTab: PrototypeConnectionsContext(builder); break;
                case ChangesTab: PrototypeChangesContext(builder); break;
                default: PrototypeObjectContext(builder); break;
            }
        }

        void PrototypePlacementContext(UIPanelBuilder builder) {
            var collapsed = !presentation.PrototypeListOpen;
            var minHeight = collapsed ? 140f : NativePresentation.PrototypePlacementBody;
            var body = presentation.Scroll(builder, minHeight, content => {
                content.ConfigureLayout(4f);
                DrawPlacement(content);
            });
            var fill = body.GetComponent<LayoutElement>();
            if (fill != null) {
                fill.minHeight = minHeight;
                fill.preferredHeight = NativePresentation.PrototypePlacementBody;
                fill.flexibleHeight = collapsed ? 10000 : 0;
            }
            var viewport = body.GetComponent<ScrollRect>().viewport;
            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null) viewport.gameObject.AddComponent<RectMask2D>();
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
            if (IsStarter) {
                PrototypeStarterLines(builder);
                return;
            }
            if (HoldsConstructionSegments) {
                var segments = String.IsNullOrWhiteSpace(segmentFields[0]) ? "None" : segmentFields[0];
                Line(builder, "<style=FieldLabel>SEGMENTS</style> " + Literal(segments),
                    "Construction segments", "Current construction segments: " + segments, LineHeight);
                PrototypePositionOnlyLine(builder);
                return;
            }
            if (selected.Identity.Structure == CrossingCoverageScene.Structure) {
                if (coverageCircuit != null) {
                    var label = coverageAddition == null ? "Circuit " + coverageCircuit : "Detection addition " + coverageAddition;
                    Line(builder, "<b>" + Literal(label) + "</b>", "Crossing coverage", "Editing " + label + ". Pick each role's segment in the world, then switch to the full window to preview and apply.", LineHeight);
                    var roles = new[] {
                        "A: " + (String.IsNullOrWhiteSpace(coverageFields[0]) ? "none" : coverageFields[0]),
                        "Island: " + (String.IsNullOrWhiteSpace(coverageFields[4]) ? "none" : coverageFields[4]),
                        "B: " + (String.IsNullOrWhiteSpace(coverageFields[8]) ? "none" : coverageFields[8])
                    };
                    var summary = String.Join("   ", roles);
                    Line(builder, Literal(summary), "Role segments", String.Join("\n", new[] {
                        "Approach A: " + (String.IsNullOrWhiteSpace(coverageFields[0]) ? "none" : coverageFields[0]),
                        "Island: " + (String.IsNullOrWhiteSpace(coverageFields[4]) ? "none" : coverageFields[4]),
                        "Approach B: " + (String.IsNullOrWhiteSpace(coverageFields[8]) ? "none" : coverageFields[8])
                    }), LineHeight);
                } else if (equipmentSource != null) {
                    Line(builder, "<b>Equipment form</b> " + Literal(equipmentLabel ?? EquipmentKind), "Equipment form", "Switch to the full window to preview or apply this equipment form.", LineHeight);
                    PrototypePositionOnlyLine(builder);
                } else {
                    PrototypePositionOnlyLine(builder);
                    Line(builder, "<style=Footnote>Switch to the full window to edit coverage or equipment.</style>", null, null, LineHeight);
                }
                return;
            }
            var bindingText = PrototypeBindingDisplay();
            Line(builder, "<style=FieldLabel>" + (IsPad ? "PAD BINDING" : "TRACK BINDING") + "</style> " + Literal(bindingText),
                IsPad ? "Crossing pad binding" : "Track binding",
                IsPad ? "Track location: " + bindingText + ". The pad's geometry is generated along this binding." : "Track location: " + bindingText + ". Changing a binding does not move the object.", LineHeight);
            if (HasPickedBinding) {
                var pickedText = PrototypeDraftBindingDisplay();
                Line(builder, "<style=FieldLabel>PICKED</style> " + Tint(Green, pickedText), "Picked track location",
                    "Picked track location ready for Set: " + pickedText + ".", LineHeight);
            } else if (IsPad) PrototypeEnabledLine(builder);
            else PrototypePositionOnlyLine(builder);
        }

        void PrototypeChangesContext(UIPanelBuilder builder) {
            var changed = IsChanged(selected);
            var shownFailure = FailureOf(selected);
            var badge = shownFailure != null ? CommandBand.Badges(changed, false, shownFailure)[0].Markup : changed ? ChangedBadge : "Unchanged";
            Line(builder, "<style=FieldLabel>THIS MOD</style> " + badge, "Changes", "Whether Data/scene-patches.json of the edited mod holds a modification of this object.", LineHeight);
            if (changed && attachment != null) {
                IReadOnlyList<string> lines = null;
                try { lines = attachment.Session.Changes(selected.Identity); } catch { }
                if (lines != null && lines.Count != 0) {
                    var full = String.Join("\n", lines);
                    Line(builder, Literal(lines[0]), "Changed settings", full, LineHeight);
                    if (lines.Count > 1) {
                        var second = lines.Count == 2 ? lines[1] : lines[1] + " (+" + (lines.Count - 2) + " more)";
                        Line(builder, Literal(second), "Changed settings", full, LineHeight);
                    }
                }
            } else PrototypeHonestPlacement(builder);
            if (!HasTerrain) return;
            var problem = (string)null;
            var output = attachment == null || !Editable ? DependentOutput.None : inspectedOutput.Of(attachment.Session, selected, out problem);
            var terrain = CommandBand.Terrain(problem, output.Error, output.Settled, Join(output.Completed), Join(output.Outstanding), output.Completed.Count != 0);
            Line(builder, problem != null ? Tint(Red, terrain) : Literal(terrain), "Terrain", terrain, LineHeight);
        }

        void PrototypeGuidedContext(UIPanelBuilder builder) {
            var summary = guided.Kind == null
                ? "Guided mode: select a task in the full window."
                : "Guided: " + GuidedName(guided.Kind.Value) + " (" + guided.Step + ")";
            Line(builder, "<b>" + Literal(summary) + "</b>", "Guided task", summary, LineHeight);
            if (selected != null) PrototypeHonestPlacement(builder);
            if (guided.Step == GuidedTaskStep.Values && guided.Kind == GuidedTaskKind.Snap) {
                var snapText = preparedSnap?.Identity.ToString() ?? "None chosen";
                Line(builder, "<style=FieldLabel>SNAP TARGET</style> " + Literal(snapText), "Snap target", snapText, LineHeight);
            } else if (guided.Step == GuidedTaskStep.Values && (guided.Kind == GuidedTaskKind.Binding || guided.Kind == GuidedTaskKind.StarterPlacement)) {
                var draft = guidedFields.Length >= 3 && !String.IsNullOrWhiteSpace(guidedFields[0])
                    ? guidedFields[0] + " / " + guidedFields[1] + " / " + guidedFields[2] + " m"
                    : "None entered";
                Line(builder, "<style=FieldLabel>DRAFT LOCATION</style> " + Literal(draft), "Draft track location", draft, LineHeight);
            }
            Line(builder, "<style=Footnote>Switch to the full window to finish this guided task.</style>", null, null, LineHeight);
        }

        // Honest placement lines with units: track point for starter markers, binding for crossing pads, and two-line
        // position and rotation with units for physical objects.
        void PrototypeHonestPlacement(UIPanelBuilder builder) {
            if (IsStarter) { PrototypeStarterLines(builder); return; }
            if (IsPad) {
                var bindingText = PrototypeBindingDisplay();
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
            var pos = CommandBand.RunningLine(CommandBand.Axes, values.Take(3).ToArray(), CommandBand.PlacementUnits[0]);
            var rot = CommandBand.RunningLine(CommandBand.Axes, values.Skip(3).Take(3).ToArray(), CommandBand.PlacementUnits[1]);
            Line(builder, "<style=FieldLabel>POSITION</style> " + Literal(pos), "Position", "Position in map coordinates: " + pos + ".", LineHeight);
            Line(builder, "<style=FieldLabel>ROTATION</style> " + Literal(rot), "Rotation", "Rotation in degrees: " + rot + ".", LineHeight);
            PrototypeEnabledLine(builder);
        }

        void PrototypePositionOnlyLine(UIPanelBuilder builder) {
            var values = CurrentPlacement();
            if (values == null || values.Length < 3) return;
            var pos = CommandBand.RunningLine(CommandBand.Axes, values.Take(3).ToArray(), CommandBand.PlacementUnits[0]);
            Line(builder, "<style=FieldLabel>POSITION</style> " + Literal(pos), "Position", "Position in map coordinates: " + pos + ".", LineHeight);
        }

        void PrototypeStarterLines(UIPanelBuilder builder) {
            var point = InspectedTrackPoint();
            var pointText = point == null
                ? "None (" + (FocusUnavailable() ?? StarterPlacements.NoTrackPoint) + ")"
                : CommandBand.RunningLine(CommandBand.Axes, point.Select(CommandBand.Number).ToArray(), CommandBand.PlacementUnits[0]);
            Line(builder, "<style=FieldLabel>TRACK POINT</style> " + Literal(pointText), "Track point",
                "Where the start setup places the front of this marker's car cut, in map coordinates: " + pointText + ". The marker's object position is not used.", LineHeight);
            var locationText = HasPickedBinding ? PrototypeDraftBindingDisplay() : PrototypeBindingDisplay();
            var locationLabel = HasPickedBinding ? "PICKED LOCATION" : "TRACK LOCATION";
            Line(builder, "<style=FieldLabel>" + locationLabel + "</style> " + (HasPickedBinding ? Tint(Green, locationText) : Literal(locationText)),
                HasPickedBinding ? "Picked track location" : "Track location",
                (HasPickedBinding ? "Picked track location ready for Set: " : "Registered track location: ") + locationText + ".", LineHeight);
        }

        string PrototypeDraftBindingDisplay() =>
            binding == null || binding.Length < 3 || String.IsNullOrWhiteSpace(binding[0]) ? "None"
                : binding[0] + " / " + (binding[1] ?? "").ToLowerInvariant() + " / " + binding[2] + " m";

        void PrototypeEnabledLine(UIPanelBuilder builder) {
            var own = OwnActivity(selected);
            var switchedOff = selected.Handle is GameObject shown && GroupWorld.OffThroughProgression(shown);
            var text = own ? (switchedOff ? CommandBand.SwitchedOffByGame : selected.Active ? "Yes" : "Yes; a parent is inactive") : "No";
            Line(builder, "<style=FieldLabel>ENABLED</style> " + Literal(text), "Enabled", "Whether this object is active in the scene: " + text + ".", LineHeight);
        }

        string PrototypeBindingDisplay() {
            var current = CurrentBinding();
            if (current == null || current.Length < 3 || String.IsNullOrWhiteSpace(current[0])) return "None";
            return current[0] + " / " + current[1].ToLowerInvariant() + " / " + current[2] + " m";
        }

        // Two-line scrolling choice and message area reusing DrawFooter's exact priority rules and live gizmo drag row,
        // followed by one action row reusing the full footer's action pattern.
        void PrototypeFooter(UIPanelBuilder builder) {
            builder.AddHRule();
            var pending = selection?.Pending;
            string source = null, state = "";
            if (pending == null) candidateDetails = false;
            var session = attachment?.Session;
            var confirmation = selection?.Confirmed;
            var confirmedForInspected = selected != null && confirmation?.Source != null && confirmation.Source.Equals(SelectionIdentity.SceneObject(selected.Identity));
            var primary = feedback.Primary(session, Requests.Place != null, pending != null, confirmedForInspected);
            if (primary == EditFeedback.PrimaryMessage.Working) { source = Requests.Place; state = "Working..."; }
            else if (primary == EditFeedback.PrimaryMessage.WorldChoice) {
                var candidate = selection.Find(pending.Candidate);
                source = "World choice";
                var purpose = choosingCoverageRole.HasValue ? pending.Purpose : choosingSnap ? "Choose the snap target" : pending.Kind == SelectionKind.SceneObject ? "Choose an object" : pending.Kind == SelectionKind.TrackSegment ? "Choose a track" : "Choose an endpoint";
                state = (pending.ListOpen ? Tint(Blue, pending.OfferCountText + " ") + Literal(pending.OfferListInstruction) : Literal(purpose + " in the world.")) +
                    (pending.InspectOnly ? "" : Literal(" Map Editor selection is paused. Click Cancel to resume it.")) +
                    (pending.Problem == null || pending.Problem == PendingSelection.SeveralOffers ? "" : " " + (PendingSelection.IsInstruction(pending.Problem) ? Literal(pending.Problem) : Tint(Red, pending.Problem))) +
                    (inputBlocker.WorldInputProblem == null ? "" : " " + Tint(Red, inputBlocker.WorldInputProblem)) +
                    (selectionProblem == null ? "" : " " + Tint(Red, selectionProblem)) +
                    (candidate == null ? "" : "\n" + Tint(Blue, CommandBand.ChoiceLine(pending.Kind == SelectionKind.SceneObject ? "Object" : "Candidate", pending.Number(candidate.Choice), pending.Offers.Count, candidate.Choice.Label, pending.Offers.Select(o => o.Label), candidate.Observation)) + Literal(choosingSnap ? guided.Enabled ? " Click Confirm to choose this target, then finish in the full window." : " Confirm chooses it. Apply moves the object." : pending.InspectOnly ? " Confirm inspects this object." : " Confirm chooses this target.")) +
                    (candidate == null || !candidateDetails ? "" : "\n" + Literal(candidate.Choice.Identity.ToString()) + (String.IsNullOrEmpty(candidate.Details) ? "" : "\n" + Literal(candidate.Details)));
            } else if (guided.Enabled && !String.IsNullOrEmpty(guided.Recovery)) { source = "Guided task"; state = Literal(guided.Recovery); }
            else if (primary == EditFeedback.PrimaryMessage.BackupDiagnosis) { source = RecoveryPlace; state = Literal(EditFeedback.BackupNotice(session)); }
            else if (selectionProblem != null) { source = "World choice"; state = Tint(Red, selectionProblem); }
            else if (primary == EditFeedback.PrimaryMessage.Reported) { state = null; }
            else if (primary == EditFeedback.PrimaryMessage.ConfirmedChoice) {
                source = "World choice"; state = Tint(Green, "Confirmed choice: " + (selection.ConfirmedName ?? confirmation.Identity.ToString()));
            } else if (!guided.Enabled && selected != null && EditBlock() is string block) { source = "Editing"; state = Tint(Amber, block); }
            else if (feedback.Recovery?.Kind != FailureKind.IncompleteCleanup && feedback.Recovery?.Kind != FailureKind.Blocked && CleanupState() is string cleanup) { source = "Editing"; state = Tint(Amber, cleanup); }
            else if (!guided.Enabled && HoldWarning is string warning) { source = ActivityPlace; state = Tint(Amber, warning); }

            var messages = feedback.Messages;
            var messageArea = presentation.Scroll(builder, NativePresentation.PrototypeMessageHeight, lines => {
                gizmoDragMessage = MessageSlot(lines, GizmoPlace, Literal(GizmoController.DraggingState), false).gameObject;
                ShowLiveGizmoDrag();
                var firstReport = 0;
                if (!String.IsNullOrEmpty(state)) MessageSlot(lines, source, state, false);
                else if (messages.Count != 0) { ReportSlot(0); firstReport = 1; }
                for (var i = firstReport; i < messages.Count; i++) ReportSlot(i);
                if ((primary == EditFeedback.PrimaryMessage.ConfirmedChoice || primary == EditFeedback.PrimaryMessage.Reported) && EditFeedback.HasBackupNotice(session))
                    MessageSlot(lines, RecoveryPlace, Literal(EditFeedback.BackupNotice(session)), true);

                void ReportSlot(int i) {
                    var message = messages[i]; var older = i > 0;
                    var tone = message.Recovery != null ? (CommandBand.RecoveryWarning(message.Recovery.Kind) ? Amber : Red) : ToneOf(message.Text);
                    var text = WithoutSource(message);
                    MessageSlot(lines, message.Source, PathBreaks(tone == null ? Literal(text) : Tint(tone + (older ? "80" : ""), text)), older);
                }
            }, false);
            var messageScroll = messageArea.GetComponent<ScrollRect>();
            if (messageScroll.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport)
                messageScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            if (messageScroll.horizontalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport)
                messageScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            var viewport = messageScroll.viewport;
            var scrollbar = messageScroll.verticalScrollbar == null ? null : messageScroll.verticalScrollbar.transform as RectTransform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(scrollbar == null ? 0 : -Mathf.Max(0, scrollbar.rect.width + messageScroll.verticalScrollbarSpacing), 0);
            var flexMessages = !presentation.PrototypeListOpen && !(inspectorTab.Value == PlacementTab && !guided.Enabled && selected != null);
            var area = messageArea.GetComponent<LayoutElement>();
            area.minHeight = area.preferredHeight = NativePresentation.PrototypeMessageHeight;
            area.flexibleHeight = flexMessages ? 10000 : 0;

            ActionRow(builder, PrototypeActs(), null);
        }

        // One shared action row: world-choice and gizmo-drag actions take it over (as in DrawFooter); otherwise it holds
        // the lightweight actions of the active tab or guided step.
        Act[] PrototypeActs() {
            if (selection?.Pending != null || gizmo?.Active == true) return FooterActs();
            if (guided.Enabled) {
                var wait = Requests.Pending ? "Wait for the scene check to finish." : null;
                if (guided.Step == GuidedTaskStep.Values && guided.Kind == GuidedTaskKind.Snap) return new[] {
                    Slot("Track", "Choose the track to snap to.", wait, () => BeginSnap(SelectionKind.TrackSegment)),
                    Slot("Node", "Choose the endpoint to snap to.", wait, () => BeginSnap(SelectionKind.TrackNode)) };
                if (guided.Step == GuidedTaskStep.Values && (guided.Kind == GuidedTaskKind.Binding || guided.Kind == GuidedTaskKind.StarterPlacement)) return new[] {
                    Slot("Segment", "Choose a track segment in the world.", wait, () => { inspectorTab.Value = ConnectionsTab; BeginSelection(SelectionKind.TrackSegment, selected, bindingSlot: guided.Kind == GuidedTaskKind.Binding); }),
                    Slot("Picked", "Fill the guided draft fields from the confirmed track location.", wait ?? (GuidedPickedTarget() != null ? null : "Choose and confirm a track segment first."), GuidedPicked) };
                return new Act[0];
            }
            if (selected == null) return new Act[0];
            switch (inspectorTab.Value) {
                case PlacementTab: return TabActs(PlacementTab);
                case ConnectionsTab: return PrototypeConnectionsActs();
                case ChangesTab: return PrototypeChangesActs();
                default: return PrototypeObjectActs();
            }
        }

        Act[] PrototypeObjectActs() {
            var acts = new List<Act>(TabActs(ObjectTab));
            var partOf = InventoryPresentationState.PartOf(selected);
            if (partOf != null) {
                var group = InventoryPresentationState.GroupOf(inventory, selected);
                acts.Add(Slot("Select group", "Inspect the group that this part belongs to.",
                    group == null ? "the object list does not include this part's group; click Refresh" : null, () => {
                        var entry = InventoryPresentationState.GroupOf(inventory, selected);
                        if (entry == null || !inventoryPresentation.CanSelect(inventory, entry)) return;
                        CancelGizmo(); ClearSnap(); coverageCircuit = coverageAddition = null; CloseEquipmentForm(); selected = inventoryPresentation.CurrentSelection(inventory, entry); ReadPlacement();
                    }));
            }
            var heldBy = InventoryPresentationState.HeldBy(selected);
            if (heldBy != null) {
                var holder = InventoryPresentationState.HolderOf(inventory, selected);
                acts.Add(Slot("Select controller", "Inspect the crossing controller that holds this detection span.",
                    holder == null ? "the object list does not include this detection span's crossing controller; click Refresh" : null, () => {
                        var entry = InventoryPresentationState.HolderOf(inventory, selected);
                        if (entry == null || !inventoryPresentation.CanSelect(inventory, entry)) return;
                        CancelGizmo(); ClearSnap(); coverageCircuit = coverageAddition = null; CloseEquipmentForm(); selected = inventoryPresentation.CurrentSelection(inventory, entry); ReadPlacement();
                    }));
            }
            return acts.ToArray();
        }

        Act[] PrototypeConnectionsActs() {
            var block = EditBlock();
            var busy = block ?? (selection == null ? "World selection is unavailable." : ChoiceBlock);
            if (IsStarter) {
                var confirmedTrack = ConfirmedForInspected?.Identity;
                var picked = confirmedTrack != null && confirmedTrack.Kind == SelectionKind.TrackSegment;
                return new[] {
                    Slot("Segment", "Click the track where the front of the car cut belongs and confirm the segment.", busy, () => BeginSelection(SelectionKind.TrackSegment, selected), "Place on a segment"),
                    Typed(Slot("Set", "Place the front of the car cut at the entered track location.", null, ApplyStarterLocation, "Set track location"),
                        () => CommandBand.FieldReason(block, binding, CurrentBinding(), CommandBand.SameLocation, CommandBand.BindingIds)),
                    HasPickedBinding ? Slot("Cancel", "Restore the current track location in the fields.", null, CancelPickedBinding)
                        : Slot("Picked", "Fill the segment field with the confirmed segment.", block ?? (picked ? null : "Choose a segment with Segment and confirm it first."), () => { binding[0] = confirmedTrack.Identifier; }, "Picked segment") };
            }
            if (HoldsConstructionSegments) {
                var current = CurrentSegments();
                var chosen = ConfirmedForInspected?.Identity;
                var pickedSegment = chosen != null && chosen.Kind == SelectionKind.TrackSegment;
                return new[] {
                    Slot("Segment", "Choose a track segment in the world and confirm its identity.", busy, () => BeginSelection(SelectionKind.TrackSegment, selected), "Replacement segment"),
                    Slot("Picked", "Add the confirmed segment to the segments field and remove segments that no longer exist.", block ?? (pickedSegment ? null : "Click Segment to choose a segment, then confirm it."),
                        () => { segmentFields[0] = CommandBand.AddSegment(segmentFields[0], chosen.Identifier, CommandBand.MissingSegments(selected?.Bindings)); FieldsChanged(); }, "Picked segment"),
                    Typed(Slot("Set", "Replace the construction segments with the segments in the field.", null, ApplySegments, "Set construction segments"),
                        () => CommandBand.SegmentsReason(block, segmentFields[0], current)) };
            }
            var coverage = selected.Identity.Structure == CrossingCoverageScene.Structure;
            if (coverage) {
                if (coverageCircuit != null) {
                    var pickBlock = ChoiceBlock ?? block ?? (selection == null ? "World selection is unavailable." : null);
                    var acts = new Act[3];
                    for (var role = 0; role < 3; role++) {
                        var roleIndex = role;
                        var roleName = CircuitCoverage.RoleCaption(CircuitCoverage.Roles[role]);
                        acts[role] = Slot(roleName, "Click a track in the world and confirm it to fill this role's segment.", pickBlock,
                            () => BeginSelection(SelectionKind.TrackSegment, selected, roleIndex), "Choose a segment for " + roleName);
                    }
                    return acts;
                }
                var replaceBlock = selection == null ? "World selection is unavailable." : ChoiceBlock;
                return new[] {
                    Slot("Object", "Choose a replacement object in the world and confirm its identity.", replaceBlock, () => BeginSelection(SelectionKind.SceneObject, selected), "Replacement object"),
                    Slot("Node", "Choose a replacement track node in the world and confirm its identity.", replaceBlock, () => BeginSelection(SelectionKind.TrackNode, selected), "Replacement node") };
            }
            var pad = selected.Identity.Structure == CrossingPadScene.Structure;
            var marker = pad || selected.Identity.Structure == TrackMarkerScene.Structure || selected.Identity.Structure == MilepostScene.Structure;
            var pickUnavailable = !marker ? "Replacement targets serve a track binding or a crossing's coverage." : selection == null ? "World selection is unavailable." : ChoiceBlock;
            var setUnavailable = marker ? block : BindingUnavailable();
            var list = new List<Act> {
                Slot("Object", "Choose a replacement object in the world and confirm its identity.", pickUnavailable, () => BeginSelection(SelectionKind.SceneObject, selected), "Replacement object"),
                Slot("Segment", "Choose a track segment in the world and confirm its identity.", pickUnavailable, () => BeginSelection(SelectionKind.TrackSegment, selected, bindingSlot: marker), "Replacement segment"),
                Slot("Node", "Choose a track node in the world and confirm its identity.", pickUnavailable, () => BeginSelection(SelectionKind.TrackNode, selected), "Replacement node"),
                Typed(Slot("Set", pad ? "Register the pad's marker at the entered track location and regenerate the pad there." : "Register the marker at the entered track location. The object does not move.", null, ApplyBinding, "Set binding"),
                    () => CommandBand.FieldReason(setUnavailable, binding, CurrentBinding(), CommandBand.SameBinding, CommandBand.BindingIds))
            };
            if (HasPickedBinding) list.Add(Slot("Cancel", "Restore the current binding in the fields.", null, CancelPickedBinding));
            return list.ToArray();
        }

        Act[] PrototypeChangesActs() {
            var acts = new List<Act>(TabActs(ChangesTab));
            var footer = FooterActs();
            // Keep Recheck and Guide from the full footer; destructive Remove modification stays in the full window.
            for (var i = 1; i < footer.Length; i++) acts.Add(footer[i]);
            return acts.ToArray();
        }

        // Filter-status bar with Reset filter and Hide/Show list toggle, above the collapsible outliner rows.
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
                ? summary + ". Active outliner filter: " + String.Join(", ", filterParts) + ". Click Reset filter to show all objects."
                : summary + ". Inactive loaded objects are included.";
            TMP_Text measure = null;
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                var summaryRect = Line(row, labelMarkup, "Object list", labelTip, RowHeight).EqualFlexibleWidth(0);
                measure = summaryRect.GetComponent<TMP_Text>() ?? summaryRect.GetComponentInChildren<TMP_Text>(true);
                Button(row, "Reset filter", () => { filter = ""; outlinerFilter = OutlinerFilter.All; }).Inactive(!filtered)
                    .Tooltip("Reset filter", CommandBand.Tooltip("Clear the outliner search text and filter to show all objects.", filtered ? null : "No outliner search or filter is active."));
                var listToggle = Button(row, presentation.PrototypeListOpen ? "Hide list" : "Show list", () => presentation.PrototypeToggleList());
                FitButton(listToggle, "Hide list", "Show list");
                listToggle.Tooltip("Object list", presentation.PrototypeListOpen ? "Collapse the object list to leave more of the world in view." : "Show the object list in this rail.");
            }).ChildrenKeepHeight().Height(RowHeight);
            if (!presentation.PrototypeListOpen) return;
            var outliner = builder.VStack(column => DrawOutliner(column, true, measure)).gameObject.AddComponent<LayoutElement>();
            outliner.flexibleHeight = 10000; outliner.flexibleWidth = 10000;
        }

        static string PrototypeFilterName(OutlinerFilter value) =>
            value == OutlinerFilter.Changed ? CommandBand.ChangedFilter
            : value == OutlinerFilter.Disabled ? CommandBand.DisabledFilter
            : value == OutlinerFilter.Problems ? CommandBand.ProblemsFilter
            : CommandBand.AllFilter;

        static string PrototypeTabTitle(string id) => id == PlacementTab ? "Placement" : id == ConnectionsTab ? "Connections" : id == ChangesTab ? "Changes" : "Object";

        void PrototypeLog(string state) {
            var pending = selection?.Pending;
            var contextName = guided.Enabled ? "Guided (" + (guided.Kind == null ? "Target" : GuidedName(guided.Kind.Value) + "/" + guided.Step) + ")" : PrototypeTabTitle(inspectorTab.Value);
            context?.Log.Info("Compact prototype: " + state + "; tab=" + contextName +
                "; inspected=" + (selected == null ? "none" : selected.DisplayName + " (" + selected.Identity.Path + ")") +
                "; pending world choice=" + (pending == null ? "no" : "yes (" + pending.Kind + ", candidate " + (selection.Find(pending.Candidate) == null ? "none" : "clicked") + ")"));
        }
    }
}
