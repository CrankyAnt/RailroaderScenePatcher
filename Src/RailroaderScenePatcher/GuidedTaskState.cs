// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    internal enum GuidedTaskKind { Placement, Snap, Activity, Binding, DetachBinding, StarterPlacement, Wreck, MarkerId }
    internal enum GuidedTaskStep { Target, Values, Review, Applied }
    internal sealed class GuidedTaskState {
        // The tasks the task list offers, in its order; a task that does not fit the inspected object stays visible and disabled.
        internal static readonly IReadOnlyList<GuidedTaskKind> Tasks = Array.AsReadOnly((GuidedTaskKind[])Enum.GetValues(typeof(GuidedTaskKind)));
        internal static string TargetBlock(GuidedTaskKind kind, TargetIdentity identity) =>
            identity != null && (identity.Structure == CrossingCoverageScene.Structure || (identity.Structure == null && identity.Component == CrossingCoverageScene.ComponentType))
                && (kind == GuidedTaskKind.Placement || kind == GuidedTaskKind.Snap || kind == GuidedTaskKind.Activity)
                ? SupportedEdits.CrossingNoEdit
            // The Ela construction controller: its position has no effect and its activity runs the visibility control.
            : identity?.Structure == ConstructionSceneryScene.Structure && (kind == GuidedTaskKind.Placement || kind == GuidedTaskKind.Snap) ? CommandBand.ControllerNoPlacement
            : identity?.Structure == ConstructionSceneryScene.Structure && kind == GuidedTaskKind.Activity ? CommandBand.ControllerNoActivity
            // Its only edit is its construction segments: the tasks for track markers and starter placements do not fit it.
            : identity?.Structure == ConstructionSceneryScene.Structure ? ControllerTaskBlock(kind) : null;
        static string ControllerTaskBlock(GuidedTaskKind kind) {
            switch (kind) {
                case GuidedTaskKind.Binding:
                case GuidedTaskKind.DetachBinding: return "The construction controller uses construction segments rather than a marker's track location. Set its construction segments on the Connections tab.";
                case GuidedTaskKind.StarterPlacement:
                case GuidedTaskKind.Wreck: return "The construction controller isn't a starter placement marker.";
                case GuidedTaskKind.MarkerId: return "The construction controller isn't a track marker, so you can't give it a Replacement ID.";
                default: return null;
            }
        }
        string[] draft = Array.Empty<string>();
        internal bool Enabled { get; private set; }
        internal GuidedTaskStep Step { get; private set; }
        internal GuidedTaskKind? Kind { get; private set; }
        internal SceneObservation Target { get; private set; }
        internal string[] Draft => (string[])draft.Clone();
        internal string Message { get; private set; }
        internal string Recovery { get; private set; }
        internal bool Success { get; private set; }
        internal void SetValues(string[] values) { if (Step == GuidedTaskStep.Values) draft = (string[])values.Clone(); }
        internal bool Select(SceneObservation target) {
            if ((Step == GuidedTaskStep.Values || Step == GuidedTaskStep.Review) && !(Target != null && target != null && ReferenceEquals(Target.Handle, target.Handle) && Target.Identity.Scene == target.Identity.Scene && Target.Identity.Path == target.Identity.Path && Target.Identity.Component == target.Identity.Component && Target.Identity.Structure == target.Identity.Structure)) {
                Message = "Finish this task or return to Inspector before clearing the selection or selecting another object."; return false;
            }
            if (Step == GuidedTaskStep.Target) Target = target;
            Message = null; return true;
        }
        internal void Review(string refusal = null, string recovery = null) { if (Step == GuidedTaskStep.Values || Step == GuidedTaskStep.Review) { Step = GuidedTaskStep.Review; Message = refusal; Recovery = recovery; reviewRefusal = null; } }
        // Why Review refuses the draft, which keeps Apply unavailable until Back or another Review.
        string reviewRefusal;
        internal string ApplyBlock => Step == GuidedTaskStep.Review ? reviewRefusal : null;
        // Checks the draft in Review as Apply would check it, without changing anything: a replacement ID that the
        // session refuses, such as one another scene marker uses. A refusal stays in Review with its recovery. Returns it.
        internal string CheckReview(Func<TargetIdentity, string, string> markerIdRefusal, string recovery) {
            if (Step != GuidedTaskStep.Review || Kind != GuidedTaskKind.MarkerId || Target == null || markerIdRefusal == null) return null;
            var refusal = markerIdRefusal(Target.Identity, draft.Length == 0 ? null : draft[0]);
            if (refusal != null) { Recovery = recovery; reviewRefusal = refusal; }
            return refusal;
        }
        internal void Back() {
            if (Step == GuidedTaskStep.Review) { Step = GuidedTaskStep.Values; Message = null; Recovery = null; Success = false; reviewRefusal = null; }
            else if (Step == GuidedTaskStep.Values) { Step = GuidedTaskStep.Target; Target = null; Kind = null; draft = Array.Empty<string>(); Message = null; Recovery = null; Success = false; }
        }
        internal void Open() { Cancel(); Enabled = true; }
        internal void Cancel() { appliedStep = null; reviewRefusal = null; Enabled = false; Step = GuidedTaskStep.Target; Kind = null; Target = null; draft = Array.Empty<string>(); Message = null; Recovery = null; Success = false; }
        internal void AnotherTask() { if (Step == GuidedTaskStep.Applied) Open(); }
        internal SceneObservation BackToInspector() { if (Step != GuidedTaskStep.Applied) return null; var target = Target; Cancel(); return target; }
        // The history state the applied edit reached, or null when it is not known.
        long? appliedStep;
        internal void Complete(bool applied, string message, string recovery, long? step = null) {
            if (Step != GuidedTaskStep.Review) return;
            Success = applied; Message = message; Recovery = recovery;
            if (applied) { Step = GuidedTaskStep.Applied; appliedStep = step; }
        }
        internal const string AppliedComplete = "The edit is complete. Save when you're done.", AppliedUndone = "Undo reversed this edit. Redo in the Map Editor applies it again.",
            AppliedDiscarded = "Undo reversed this edit, and a later edit removed it from Redo.";
        // What the Apply step says, given where the history holds the state the edit reached (PatchEditingSession.StepOf):
        // complete while it stands, reversed with Redo offered while Redo holds it, and reversed without Redo once a later
        // edit cleared it. Null outside the Apply step.
        internal string AppliedText(Func<long, HistoryStep> stepOf) {
            if (Step != GuidedTaskStep.Applied) return null;
            if (appliedStep == null || stepOf == null) return AppliedComplete;
            switch (stepOf(appliedStep.Value)) {
                case HistoryStep.Undone: return AppliedUndone;
                case HistoryStep.Discarded: return AppliedDiscarded;
                default: return AppliedComplete;
            }
        }
        internal void Start(GuidedTaskKind kind, SceneObservation target, string[] values) {
            var copiedValues = (string[])values.Clone();
            Enabled = true; Step = GuidedTaskStep.Values; Kind = kind; Target = target; Message = null; Recovery = null; Success = false; reviewRefusal = null;
            draft = copiedValues;
        }
    }
}
