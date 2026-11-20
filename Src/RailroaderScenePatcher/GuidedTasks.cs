// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RailroaderScenePatcher.Core;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    public sealed partial class PatcherBehaviour {
        readonly GuidedTaskState guided = new GuidedTaskState();
        readonly UIState<string> guidedTab = new UIState<string>("Target");
        InspectedTarget guidedInspection;
        string[] guidedFields = Array.Empty<string>(), guidedBefore = Array.Empty<string>();
        double[] guidedPlacement;
        SnapRequest guidedSnap;
        string guidedInspectorTab;
        ConfirmedSelection choiceBeforeGuided;
        const string GuidedPlace = "Guided task";
        const string GuidedRecovery = "Click Target to discard the draft. Click Refresh, then inspect the object and start again.";
        const string GuidedValuesRecovery = "Click Back to correct the values or click Target to start again.";
        string InspectorFeedbackView() => inspectorTab.Value + "/" + (inspectorTab.Value == PlacementTab ? placementMode : 0);
        string GuidedFeedbackView() => GuidedPlace + "/" + guided.Step;

        bool GuidedSelect(SceneObservation observation) {
            if (guided.Select(observation)) return true;
            feedback.Report(GuidedPlace, guided.Message);
            presentation?.RequestRefresh();
            return false;
        }
        void OpenGuided() {
            if (guided.Enabled) return;
            guidedInspectorTab = inspectorTab.Value;
            choiceBeforeGuided = selection?.Confirmed;
            CancelGizmo(); ClearSnap();
            if (selection?.Pending != null) CancelSelection();
            CancelRequest("you switched to Guided before the action could run");
            guided.Open();
            feedback.View(GuidedFeedbackView());
        }
        void CancelGuided() {
            if (guidedInspectorTab != null) {
                if (!ReferenceEquals(selection?.Confirmed, choiceBeforeGuided)) ReleaseGuidedChoice();
                else if (selection?.Pending != null) CancelSelection();
                else ClearSnap();
                inspectorTab.Value = guidedInspectorTab;
                feedback.View(InspectorFeedbackView(), preserve: true);
            }
            guidedInspectorTab = null;
            choiceBeforeGuided = null;
            guided.Cancel(); guidedInspection = null; guidedSnap = null;
            guidedFields = guidedBefore = Array.Empty<string>(); guidedPlacement = null;
        }
        void LeaveGuided() {
            if (!guided.Enabled) return;
            CancelRequest("you cancelled the guided task before the action could run");
            CancelGuided(); ReadPlacement();
        }
        void ReleaseGuidedChoice() {
            if (selection?.Pending != null) CancelSelection();
            selection?.Consume();
            ClearSnap();
            pickedTrackLocation = null; pickedTrackSelection = null;
        }
        static string GuidedName(GuidedTaskKind kind) {
            switch (kind) {
                case GuidedTaskKind.Placement: return "Move or rotate";
                case GuidedTaskKind.Snap: return "Snap to track or endpoint";
                case GuidedTaskKind.Activity: return "Enable or disable";
                case GuidedTaskKind.Binding: return "Set track binding";
                case GuidedTaskKind.DetachBinding: return "Detach track binding";
                case GuidedTaskKind.MarkerId: return "Set replacement ID";
                // The inspector's name for the setting.
                case GuidedTaskKind.Wreck: return "Start derailed";
                default: return "Place starter on track";
            }
        }
        string GuidedBlock(GuidedTaskKind kind) {
            if (selected == null) return "Inspect an object first.";
            var block = (IsStarter ? StarterBlock() : EditBlock()) ?? GuidedTaskState.TargetBlock(kind, selected.Identity);
            if (block != null) return block;
            switch (kind) {
                case GuidedTaskKind.Placement: return PadBlock ?? (IsStarter ? StarterPlacements.ObjectSettings : null);
                case GuidedTaskKind.Snap: return PadBlock ?? (IsStarter ? StarterPlacements.ObjectSettings : snap == null ? "Snapping is unavailable." : null);
                case GuidedTaskKind.Activity: return IsStarter ? StarterPlacements.ObjectSettings : null;
                case GuidedTaskKind.StarterPlacement: return IsStarter ? null : "This object is not a starter placement marker.";
                case GuidedTaskKind.Wreck: return CommandBand.WreckTaskReason(IsStarter, NativeInventory.StartersError, selected.Bindings.Where(IsReader).ToArray());
                case GuidedTaskKind.MarkerId: return MarkerIdBlock();
                case GuidedTaskKind.DetachBinding:
                    if (IsPad) return "A crossing pad cannot be detached. Disable it instead.";
                    if (IsStarter) return StarterPlacements.NoDetach;
                    return selected.Identity.Structure != TrackMarkerScene.Structure ? BindingUnavailable() : null;
                default: return selected.Identity.Structure == TrackMarkerScene.Structure || IsPad ? null : BindingUnavailable();
            }
        }
        void StartGuided(GuidedTaskKind kind) {
            var reason = GuidedBlock(kind);
            if (reason != null) { feedback.Refuse(GuidedPlace, GuidedName(kind), reason); return; }
            OpenGuided();
            Queue(GuidedPlace, "Target", pin => {
                try {
                    guidedInspection = pin(selected);
                    // Consume the token's one-use comparison shortcut now; later steps must compare the pinned observation.
                    guidedInspection.Validate(capabilities, selected.Identity, selected.Handle);
                    guidedPlacement = PlacementValues();
                    if (guidedPlacement == null) throw new InvalidOperationException("This object has no placement.");
                    guidedBefore = kind == GuidedTaskKind.Placement || kind == GuidedTaskKind.Snap ? CurrentPlacement()
                        : kind == GuidedTaskKind.Activity ? new[] { OwnActivity(selected) ? "Yes" : "No" } : kind == GuidedTaskKind.MarkerId ? new[] { CurrentMarkerId() }
                        : kind == GuidedTaskKind.Wreck ? new[] { CurrentWreck() } : CurrentBinding();
                    guided.Start(kind, selected, guidedBefore);
                    feedback.View(GuidedFeedbackView());
                    guidedFields = guided.Draft;
                    guidedSnap = null;
                } catch (Exception e) { feedback.Refuse(GuidedPlace, "Target", e.GetBaseException().Message); }
                presentation?.RequestRefresh();
            });
        }
        // A problem offers only a matching supported task. The ordinary task list keeps unavailable operations visible.
        // A construction segment problem has no guided task: its repair opens the Connections tab of the construction.
        void DrawProblemTasks(UIPanelBuilder builder) {
            if (CommandBand.OffersConstructionRepair(selected.Problems))
                Button(builder, CommandBand.RepairConstructionSegments, () => inspectorTab.Value = ConnectionsTab)
                    .Tooltip(CommandBand.RepairConstructionSegments, CommandBand.RepairConstructionSegmentsTip);
            if (!CommandBand.OffersBindingRepair(selected.Problems)) return;
            var kind = IsStarter ? GuidedTaskKind.StarterPlacement : GuidedTaskKind.Binding;
            var reason = GuidedBlock(kind);
            Button(builder, "Repair with guided task", () => StartGuided(kind)).Inactive(reason != null)
                .Tooltip("Repair with guided task", CommandBand.Tooltip("Start a guided task to repair this track binding problem.", reason));
        }
        void DrawGuided(UIPanelBuilder builder) {
            guidedTab.Value = guided.Step == GuidedTaskStep.Applied ? "Apply" : guided.Step.ToString();
            builder.AddTabbedPanels(guidedTab, tabs => {
                foreach (var name in new[] { "Target", "Values", "Review", "Apply" }) {
                    var step = name;
                    tabs.AddTab(step, step, body => DrawGuidedBody(body));
                }
            });
        }
        void DrawGuidedBody(UIPanelBuilder builder) {
            var view = presentation.Scroll(builder, TabBodyMinimumHeight, body => {
                body.ConfigureLayout(4f);
                body.AddLabel("<style=Footnote>" + (guided.Kind == null ? "Choose one task for the inspected object." : Literal(GuidedName(guided.Kind.Value))) + "</style>")
                    .TextWrap(TMPro.TextOverflowModes.Ellipsis, TMPro.TextWrappingModes.Normal).Height(32);
                if (guided.Step == GuidedTaskStep.Target) {
                    foreach (var kind in GuidedTaskState.Tasks) {
                        var task = kind; var reason = GuidedBlock(task);
                        Button(body, GuidedName(task), () => StartGuided(task)).Inactive(reason != null)
                            .Tooltip(GuidedName(task), CommandBand.Tooltip("Start this task for the inspected object.", reason));
                    }
                } else if (guided.Step == GuidedTaskStep.Values) DrawGuidedValues(body);
                else if (guided.Step == GuidedTaskStep.Review) DrawGuidedReview(body);
                else PlainLabel(body, "The edit is complete. Save when you're done.");
            });
            var fill = view.GetComponent<LayoutElement>();
            if (fill != null) { fill.preferredHeight = TabBodyMinimumHeight; fill.flexibleHeight = 10000; }
            // Reserve the inspector tab action row, even though guided actions live in the footer.
            ActionRow(builder, Array.Empty<Act>(), null); builder.Spacer(TabGap);
        }
        void DrawGuidedValues(UIPanelBuilder builder) {
            Field(builder, "Target", guided.Target.Identity.Path);
            switch (guided.Kind) {
                case GuidedTaskKind.Placement:
                    for (var group = 0; group < 2; group++)
                        ValueRow(builder, CommandBand.PlacementGroups[group], CommandBand.PlacementUnits[group], CommandBand.Axes, guidedFields, group * 3, ChoiceBlock, group == 0 ? "Map coordinates" : "Euler angles", column => true);
                    break;
                case GuidedTaskKind.Snap:
                    Field(builder, "Snap target", preparedSnap?.Identity.ToString() ?? "Choose a track or endpoint in the world and confirm it.");
                    if (preparedSnap != null) Field(builder, "Position", String.Join(", ", preparedSnap.Placement.Position.Select(CommandBand.Number)));
                    break;
                case GuidedTaskKind.Activity:
                    builder.AddSegmentedControlCompact(values => {
                        values.AddSegment("Enabled", guidedFields[0] == "Yes", presentation.Action(() => guidedFields[0] = "Yes"));
                        values.AddSegment("Disabled", guidedFields[0] == "No", presentation.Action(() => guidedFields[0] = "No"));
                    }, true);
                    break;
                case GuidedTaskKind.Wreck:
                    // The inspector's Start derailed checkbox as two choices, starting from the effective setting.
                    FieldLabel(builder, "Start derailed");
                    builder.AddSegmentedControlCompact(values => {
                        values.AddSegment("Yes", guidedFields[0] == "Yes", presentation.Action(() => guidedFields[0] = "Yes"));
                        values.AddSegment("No", guidedFields[0] == "No", presentation.Action(() => guidedFields[0] = "No"));
                    }, true);
                    PlainLabel(builder, CommandBand.WreckValuesHelp);
                    break;
                case GuidedTaskKind.DetachBinding:
                    PlainLabel(builder, "Remove the logical track binding without changing the object's placement or activity.");
                    break;
                case GuidedTaskKind.MarkerId:
                    ValueRow(builder, "Replacement ID", null, new[] { "ID" }, guidedFields, 0, ChoiceBlock, "Enter an ID that no other scene marker or map object uses. It can't begin with passengerStop: or flare:.", column => false);
                    break;
                default:
                    ValueRow(builder, "Track location", "m", new[] { "Segment", "End", "Distance" }, guidedFields, 0, ChoiceBlock, "Enter the segment ID, end a or b, and distance in meters.", column => column == 2);
                    break;
            }
        }
        void GuidedPicked() {
            var choice = GuidedPickedTarget();
            if (choice == null) return;
            EnteredEditValues.FillTrackLocation(guidedFields, pickedTrackLocation);
        }
        ConfirmedSelection GuidedPickedTarget() {
            var choice = selection?.Confirmed;
            return choice?.Identity.Kind == SelectionKind.TrackSegment && ReferenceEquals(choice, pickedTrackSelection) && pickedTrackLocation != null && choice.Source != null && guided.Target != null &&
                choice.Source.Equals(SelectionIdentity.SceneObject(guided.Target.Identity)) ? choice : null;
        }
        string GuidedValuesBlock() {
            if (guided.Kind == GuidedTaskKind.Snap) {
                var request = guided.Step == GuidedTaskStep.Review ? guidedSnap : preparedSnap;
                return request == null ? "Choose and confirm a track or endpoint first." : null;
            }
            if (guided.Kind == GuidedTaskKind.Placement) return EnteredPlacement(guidedFields, guidedPlacement, guided.Target) == null ? "Enter a number in each of the six fields. Use a point for decimals."
                : CommandBand.FieldReason(null, guidedFields, guidedBefore, CommandBand.SamePlacement);
            if (guided.Kind == GuidedTaskKind.Binding || guided.Kind == GuidedTaskKind.StarterPlacement) {
                return EnteredEditValues.TrackLocation(guidedFields) == null ? "Enter the segment ID, end a or b, and a distance. Use a point for decimals."
                    : CommandBand.FieldReason(null, guidedFields, guidedBefore, guided.Kind == GuidedTaskKind.StarterPlacement ? CommandBand.SameLocation : CommandBand.SameBinding, CommandBand.BindingIds);
            }
            if (guided.Kind == GuidedTaskKind.DetachBinding) return String.IsNullOrWhiteSpace(guidedBefore[0]) ? CommandBand.AlreadyDetached : null;
            if (guided.Kind == GuidedTaskKind.Activity) return CommandBand.FieldReason(null, guidedFields, guidedBefore, CommandBand.SameActivity);
            if (guided.Kind == GuidedTaskKind.Wreck) return CommandBand.FieldReason(null, guidedFields, guidedBefore, CommandBand.SameWreck);
            if (guided.Kind == GuidedTaskKind.MarkerId) return CommandBand.MarkerIdSetReason(null, guidedFields[0], guidedBefore[0]);
            return null;
        }
        string GuidedUnchanged() {
            var follow = attachment?.Session.FollowingOf(guided.Target.Identity) != null;
            const string track = " The object keeps following the same track at the new offset.";
            switch (guided.Kind) {
                case GuidedTaskKind.Placement: return "Activity and logical bindings." + (follow ? track : "");
                case GuidedTaskKind.Snap: return "Rotation, activity and logical bindings." + (follow ? track : "");
                case GuidedTaskKind.Activity: return "Placement and logical bindings.";
                case GuidedTaskKind.StarterPlacement: return "The marker's position and activity, the wreck setting and other car placements.";
                case GuidedTaskKind.Wreck: return "The marker's position, activity and track location, and other car placements.";
                case GuidedTaskKind.MarkerId: return "Placement, activity and track binding. Every other scene marker that holds the old ID keeps its registration.";
                default: return IsPad ? "Activity. The pad follows its marker to the new location." : "Physical placement and activity.";
            }
        }
        void DrawGuidedReview(UIPanelBuilder builder) {
            Field(builder, "Target", guided.Target.Identity.Path);
            builder.HStack(row => {
                row.VStack(column => { Section(column, "Inspected state"); GuidedSummary(column, guidedBefore, false); }).FlexibleWidth();
                row.VStack(column => { Section(column, "Proposed changes"); GuidedSummary(column, guidedFields, true); }).FlexibleWidth();
            });
            Field(builder, "Unchanged", GuidedUnchanged());
        }
        void GuidedSummary(UIPanelBuilder builder, string[] values, bool intended) {
            if (guided.Kind == GuidedTaskKind.Activity) { Field(builder, "Enabled", values[0]); return; }
            if (guided.Kind == GuidedTaskKind.MarkerId) { Field(builder, "Marker ID", values[0]); return; }
            if (guided.Kind == GuidedTaskKind.Wreck) { Field(builder, "Start derailed", values[0]); return; }
            if (intended && guided.Kind == GuidedTaskKind.DetachBinding) { Field(builder, "Track binding", "Detached"); return; }
            if (guided.Kind == GuidedTaskKind.Placement || guided.Kind == GuidedTaskKind.Snap) {
                Field(builder, "Position X, Y, Z (m)", intended && guided.Kind == GuidedTaskKind.Snap
                    ? guidedSnap == null ? "No snap target" : String.Join(", ", guidedSnap.Placement.Position.Select(CommandBand.Number))
                    : String.Join(", ", values.Take(3)));
                Field(builder, "Rotation X, Y, Z (degrees)", String.Join(", ", values.Skip(3)));
                if (intended && guided.Kind == GuidedTaskKind.Snap) Field(builder, "Snap target", guidedSnap?.Identity.ToString() ?? "None");
                return;
            }
            Field(builder, "Segment", String.IsNullOrEmpty(values[0]) ? "Detached" : values[0]);
            Field(builder, "End", values[1]); Field(builder, "Distance (m)", values[2]);
        }
        void ReviewGuided() {
            var block = GuidedValuesBlock();
            if (block != null) { feedback.Refuse(GuidedPlace, "Review", block); return; }
            guided.SetValues(guidedFields); guidedSnap = preparedSnap;
            guided.Review();
            feedback.View(GuidedFeedbackView());
            // Refusals, including a queued comparison refusal, remain on Review with an explicit recovery route.
            Queue(GuidedPlace, "Review", pin => {
                try {
                    pin(selected).Validate(capabilities, guided.Target.Identity, guided.Target.Handle);
                    guidedInspection.Validate(capabilities, guided.Target.Identity, guided.Target.Handle);
                    guided.Review();
                } catch (Exception e) {
                    feedback.Refuse(GuidedPlace, "Review", e.GetBaseException().Message);
                    guided.Review(null, GuidedRecovery);
                }
                presentation?.RequestRefresh();
            }, () => guided.Review(null, GuidedRecovery));
        }
        void GuidedTarget() {
            CancelRequest("you released the task's target before the action could run");
            ReleaseGuidedChoice(); guided.Open(); guidedInspection = null; guidedSnap = null;
            feedback.View(GuidedFeedbackView());
            guidedFields = guidedBefore = Array.Empty<string>(); guidedPlacement = null;
        }
        Act[] GuidedActs() {
            var wait = Requests.Pending ? "Wait for the scene check to finish." : null;
            if (guided.Step == GuidedTaskStep.Applied) return new[] {
                Slot("Back to inspector", "Return to Inspector with the same object selected.", null, () => {
                    var target = guided.BackToInspector(); CancelGuided();
                    selected = inventoryPresentation.CurrentSelection(inventory, target); ReadPlacement();
                }), Slot("Another guided task", "Return to the task list.", null, GuidedTarget) };
            if (guided.Step == GuidedTaskStep.Target) return new[] { Slot("Inspector", "Return to Inspector.", wait, LeaveGuided) };
            if (guided.Step == GuidedTaskStep.Values) {
                var back = Slot("Back", "Discard the draft and return to the task list.", wait, GuidedTarget);
                var next = Typed(Slot("Next", "Review your changes without applying them.", wait, ReviewGuided), () => (Requests.Pending ? "Wait for the scene check to finish." : null) ?? GuidedValuesBlock());
                if (guided.Kind == GuidedTaskKind.Snap) return new[] { back,
                    Slot("Track", "Choose the track to snap to.", wait, () => BeginSnap(SelectionKind.TrackSegment)),
                    Slot("Node", "Choose the endpoint to snap to.", wait, () => BeginSnap(SelectionKind.TrackNode)), next };
                if (guided.Kind == GuidedTaskKind.Binding || guided.Kind == GuidedTaskKind.StarterPlacement) return new[] { back,
                    Slot("Segment", "Choose a track segment in the world.", wait, () => { inspectorTab.Value = ConnectionsTab; BeginSelection(SelectionKind.TrackSegment, selected, bindingSlot: guided.Kind == GuidedTaskKind.Binding); }),
                    Slot("Picked", "Fill the fields from the confirmed track location.", wait ?? (GuidedPickedTarget() != null ? null : "Choose and confirm a track segment first."), GuidedPicked), next };
                return new[] { back, next };
            }
            return new[] {
                Slot("Back", "Return to Values and keep the draft.", wait, () => { guided.Back(); feedback.View(GuidedFeedbackView()); guidedFields = guided.Draft; if (guided.Kind == GuidedTaskKind.Snap) preparedSnap = guidedSnap; }),
                Typed(Slot("Apply", "Apply the reviewed changes.", wait, ApplyGuided), () => (Requests.Pending ? "Wait for the scene check to finish." : null) ?? GuidedValuesBlock()),
                Slot("Target", "Discard the draft and return to the task list.", wait, GuidedTarget) };
        }
        Outcome GuidedOperation(PatchEditingSession session) {
            var identity = guided.Target.Identity;
            switch (guided.Kind) {
                case GuidedTaskKind.Snap: return snap.Apply(guidedSnap);
                case GuidedTaskKind.Activity: return session.Edit(identity, null, guidedFields[0] == "Yes", guidedInspection);
                case GuidedTaskKind.DetachBinding: return session.Bind(identity, MarkerBinding.Detached, guidedInspection);
                case GuidedTaskKind.MarkerId: return session.SetMarkerId(identity, guidedFields[0], guidedInspection);
                // The operation of the inspector's Start derailed checkbox.
                case GuidedTaskKind.Wreck: return session.SetStarterWreck(identity, guidedFields[0] == "Yes", guidedInspection);
                case GuidedTaskKind.Binding:
                case GuidedTaskKind.StarterPlacement:
                    var location = EnteredEditValues.TrackLocation(guidedFields);
                    if (location == null) throw new InvalidOperationException("Enter the segment ID, end a or b, and a distance. Use a point for decimals.");
                    return guided.Kind == GuidedTaskKind.StarterPlacement ? session.PlaceStarter(identity, location, guidedInspection) : session.Bind(identity, MarkerBinding.At(location), guidedInspection);
                default:
                    var placement = EnteredPlacement(guidedFields, guidedPlacement, guided.Target);
                    if (placement == null) throw new InvalidOperationException("Enter a number in each of the six fields. Use a point for decimals.");
                    return session.Edit(identity, placement, null, guidedInspection);
            }
        }
        void ApplyGuided() {
            string place, action; bool consumes;
            var location = EnteredEditValues.TrackLocation(guidedFields);
            switch (guided.Kind) {
                case GuidedTaskKind.Snap: place = SnapPlace; action = "Physical snap"; consumes = true; break;
                case GuidedTaskKind.Activity: place = ActivityPlace; action = guidedFields[0] == "Yes" ? "Enable" : "Disable"; consumes = false; break;
                case GuidedTaskKind.DetachBinding: place = BindingPlace; action = "Track detachment"; consumes = false; break;
                case GuidedTaskKind.MarkerId: place = MarkerIdPlace; action = "Set marker ID " + guidedFields[0]; consumes = false; break;
                case GuidedTaskKind.Wreck: place = WreckPlace; action = CommandBand.WreckAction(guidedFields[0] == "Yes"); consumes = false; break;
                case GuidedTaskKind.Binding: place = BindingPlace; action = location == null ? "Track binding" : CommandBand.BindingAction(location.Segment, location.End, location.Distance); consumes = true; break;
                case GuidedTaskKind.StarterPlacement: place = BindingPlace; action = location == null ? "Track location" : CommandBand.StarterAction(location.Segment, location.End, location.Distance); consumes = true; break;
                default: place = PlacementPlace; action = "Placement"; consumes = false; break;
            }
            var block = GuidedValuesBlock();
            if (block != null) { feedback.Refuse(place, action, block); guided.Complete(false, null, GuidedValuesRecovery); return; }
            guided.Complete(false, null, null);
            var checkedTarget = false;
            Edit(place, action, (session, inspection) => {
                guidedInspection.Validate(capabilities, guided.Target.Identity, guided.Target.Handle);
                checkedTarget = true;
                return GuidedOperation(session);
            }, consumes, applied => {
                var cleanup = !applied && guided.Target != null && attachment?.Session.CleanupUnverified(guided.Target.Identity) == true;
                guided.Complete(applied, null, applied ? null : cleanup ? "Return to Inspector and click Recheck before trying again." : checkedTarget ? GuidedValuesRecovery : GuidedRecovery);
                feedback.View(GuidedFeedbackView(), preserve: true);
                presentation?.RequestRefresh();
            });
        }
    }
}
