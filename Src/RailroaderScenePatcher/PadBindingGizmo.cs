// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RailroaderScenePatcher.Core;
using TMPro;
using UnityEngine;

namespace RailroaderScenePatcher {
    // A crossing pad's distance gizmo on its Connections tab, and the marks of its binding fields: while a field of the
    // pad's binding is edited, the world marks the entered segment and its End A and End B.
    public sealed partial class PatcherBehaviour {
        PadDistanceGizmo padGizmo;
        string padGizmoKey;
        // The binding fields as they were when a drag began: Escape and a refused drag restore them.
        string[] padDragFields;
        // The input fields of the binding row as last built: a drag sets their text in place, because the panel does not
        // rebuild while a pointer button is down.
        readonly TMP_InputField[] bindingInputs = new TMP_InputField[3];
        TMP_Text segmentLengthText;
        bool PadGizmoDragging => padGizmo?.Active == true;
        // A binding field has the focus.
        bool BindingFieldEdited => bindingInputs.Any(input => input != null && input.isFocused);

        // The gizmo stands on the pad while its Connections tab is open and the entered segment is loaded; it stands at the
        // entered location, or at the segment's middle until the fields name a place on it. It goes with the tab, the
        // inspection or a world choice, and comes back after each drag once the pad was observed again.
        void ArmPadGizmo() {
            if (padGizmo != null && padGizmo.Closed) padGizmo = null;
            if (PadGizmoDragging) return;
            var wanted = CommandBand.ShowsPadGizmo(IsPad, inspectorTab.Value == ConnectionsTab, EditBlock() == null, selection?.Pending != null, guided.Enabled)
                && attachment != null && trackPoses != null && !Requests.Pending && refreshFrame == 0 && replayFrame == 0;
            TrackLocation start = null;
            if (wanted) {
                try { start = PadDistance.Start(trackPoses, binding[0], EnteredEditValues.TrackLocation(binding)); } catch { start = null; }
            }
            if (start == null) { CancelPadGizmo(); padGizmoKey = null; return; }
            var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(selected) + "|" + ChangeStamp() + "|" + start.Segment;
            if (key == padGizmoKey) {
                // A gizmo that was refused or taken away is not shown again until something changes.
                if (padGizmo != null && !PadDistance.Same(padGizmo.Location, start)) try { padGizmo.Retarget(start); } catch { CancelPadGizmo(); }
                return;
            }
            CancelPadGizmo();
            padGizmoKey = key;
            try {
                var root = new GameObject("CrankyAnt Scene Patcher pad distance gizmo");
                root.transform.SetParent(transform, false);
                padGizmo = root.AddComponent<PadDistanceGizmo>();
                padGizmo.Initialize(attachment, trackPoses, start, PadMoved, PadReleased, PadCancelled);
            } catch (Exception e) { padGizmo = null; GizmoUnavailable(e); }
        }
        // Takes the distance gizmo away; it is shown again once it is wanted.
        void CancelPadGizmo() {
            var standing = padGizmo; padGizmo = null; padGizmoKey = null;
            standing?.Close();
        }
        // The fields follow the dragged place: End a and the distance from it.
        void PadMoved(TrackLocation location) {
            if (padDragFields == null) padDragFields = (string[])binding.Clone();
            if (!EnteredEditValues.FillTrackLocation(binding, location)) return;
            ShowBindingFields();
        }
        // The release binds the pad at the dragged place, as Set does with the fields; a release where the drag began
        // binds nothing (CommandBand.ReleasePad).
        void PadReleased(TrackLocation start, TrackLocation location) {
            var before = padDragFields;
            padDragFields = null; padGizmoKey = null; gizmoEnded = Time.frameCount;
            CommandBand.ReleasePad(binding, before, start, location, ApplyBinding);
            ShowBindingFields();
            presentation?.RequestRefresh();
        }
        // Whether a binding edit of the inspected pad blocks the native world input now: the pointer on the gizmo's
        // handle, its drag, or a focused binding field on the pad's Connections tab.
        bool PadEditBlocks() => CommandBand.PadGizmoBlocks(padGizmo?.HandleUnderPointer == true, PadGizmoDragging, IsPad && inspectorTab.Value == ConnectionsTab && BindingFieldEdited);
        void PadCancelled() {
            if (padDragFields != null) { Array.Copy(padDragFields, binding, binding.Length); padDragFields = null; ShowBindingFields(); }
            padGizmoKey = null; gizmoEnded = Time.frameCount;
            presentation?.RequestRefresh();
        }
        // The binding an edit of the inspected object stores, for the name of its result (CommandBand.StoredBinding).
        TrackLocation StoredBinding(TrackLocation entered) {
            if (entered == null) return null;
            double? length = null;
            try { if (IsPad && trackPoses != null) length = PadDistance.Length(trackPoses, entered.Segment); } catch { length = null; }
            return CommandBand.StoredBinding(entered, IsPad, length);
        }
        // The binding fields are used, by a picked or typed segment: an empty End takes a (CommandBand.DefaultEnd).
        void UseBindingFields() { if (CommandBand.DefaultEnd(binding)) ShowBindingFields(); }
        void ShowBindingFields() {
            for (var i = 0; i < bindingInputs.Length; i++) if (bindingInputs[i] != null && bindingInputs[i].text != binding[i]) bindingInputs[i].SetTextWithoutNotify(binding[i]);
            ShowSegmentLength();
        }
        // The length of the entered segment beside Distance, kept current as the segment field changes.
        string SegmentLengthNow() {
            double? length = null;
            try { if (trackPoses != null) length = PadDistance.Length(trackPoses, binding[0]); } catch { length = null; }
            return CommandBand.SegmentLength(binding[0], length);
        }
        void ShowSegmentLength() {
            ShowPadEndNote();
            if (segmentLengthText == null) return;
            var text = "<style=FieldLabel>" + Literal(SegmentLengthNow() ?? "") + "</style>";
            if (segmentLengthText.text != text) segmentLengthText.text = text;
        }

        // The note beside End: a pad's binding is stored from End a, and an entered End b shows its stored place.
        TMP_Text padEndNote;
        string PadEndNoteNow() {
            double? length = null;
            try { if (trackPoses != null) length = PadDistance.Length(trackPoses, binding[0]); } catch { length = null; }
            return CommandBand.PadEndNote(binding, length);
        }
        void ShowPadEndNote() {
            if (padEndNote == null) return;
            var text = "<style=Footnote>" + Literal(PadEndNoteNow()) + "</style>";
            if (padEndNote.text != text) padEndNote.text = text;
        }
        // The entered segment and the map positions of its End A and End B while a binding field is edited, read once
        // per frame; null otherwise.
        int endsFrame = -1; BindingEnds ends;
        BindingEnds MarkedEnds() {
            if (endsFrame == Time.frameCount) return ends;
            endsFrame = Time.frameCount;
            var segment = IsPad && inspectorTab.Value == ConnectionsTab ? CommandBand.MarkedEnds(BindingFieldEdited, PadGizmoDragging, binding[0]) : null;
            if (segment == null || trackPoses == null) return ends = null;
            if (ends != null && ends.Segment == segment && Time.frameCount < ends.Read + 20) return ends;
            try {
                if (PadDistance.Length(trackPoses, segment) == null) return ends = null;
                ends = new BindingEnds(segment, trackPoses.TrackPose(TrackAnchor.At(new TrackLocation(segment, "a", 0))).Position,
                    trackPoses.TrackPose(TrackAnchor.At(new TrackLocation(segment, "b", 0))).Position, Time.frameCount);
            } catch { ends = null; }
            return ends;
        }
    }
    // A segment and the map positions of its two ends, as the world marks them while a binding field is edited.
    internal sealed class BindingEnds {
        internal BindingEnds(string segment, double[] a, double[] b, int read) { Segment = segment; A = a; B = b; Read = read; }
        internal string Segment { get; }
        internal double[] A { get; }
        internal double[] B { get; }
        internal int Read { get; }
    }
}
