// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    internal enum GuidedTaskKind { Placement, Snap, Activity, Binding, DetachBinding, StarterPlacement }
    internal enum GuidedTaskStep { Target, Values, Review, Applied }
    internal sealed class GuidedTaskState {
        internal static string TargetBlock(GuidedTaskKind kind, TargetIdentity identity) =>
            identity != null && (identity.Structure == CrossingCoverageScene.Structure || (identity.Structure == null && identity.Component == CrossingCoverageScene.ComponentType))
                && (kind == GuidedTaskKind.Placement || kind == GuidedTaskKind.Snap || kind == GuidedTaskKind.Activity)
                ? "You cannot move, rotate, enable or disable a crossing controller." : null;
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
        internal void Review(string refusal = null, string recovery = null) { if (Step == GuidedTaskStep.Values || Step == GuidedTaskStep.Review) { Step = GuidedTaskStep.Review; Message = refusal; Recovery = recovery; } }
        internal void Back() {
            if (Step == GuidedTaskStep.Review) { Step = GuidedTaskStep.Values; Message = null; Recovery = null; Success = false; }
            else if (Step == GuidedTaskStep.Values) { Step = GuidedTaskStep.Target; Target = null; Kind = null; draft = Array.Empty<string>(); Message = null; Recovery = null; Success = false; }
        }
        internal void Open() { Cancel(); Enabled = true; }
        internal void Cancel() { Enabled = false; Step = GuidedTaskStep.Target; Kind = null; Target = null; draft = Array.Empty<string>(); Message = null; Recovery = null; Success = false; }
        internal void AnotherTask() { if (Step == GuidedTaskStep.Applied) Open(); }
        internal SceneObservation BackToInspector() { if (Step != GuidedTaskStep.Applied) return null; var target = Target; Cancel(); return target; }
        internal void Complete(bool applied, string message, string recovery) {
            if (Step != GuidedTaskStep.Review) return;
            Success = applied; Message = message; Recovery = recovery;
            if (applied) Step = GuidedTaskStep.Applied;
        }
        internal void Start(GuidedTaskKind kind, SceneObservation target, string[] values) {
            var copiedValues = (string[])values.Clone();
            Enabled = true; Step = GuidedTaskStep.Values; Kind = kind; Target = target; Message = null; Recovery = null; Success = false;
            draft = copiedValues;
        }
    }
}
