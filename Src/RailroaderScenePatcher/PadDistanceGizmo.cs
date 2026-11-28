// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using HarmonyLib;
using RailroaderScenePatcher.Core;
using UI.Gizmos;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RailroaderScenePatcher {
    // The distance gizmo of a crossing pad: the native move gizmo, standing on the pad's track location, that moves along
    // the entered segment. During a drag it stands on the point of the segment nearest to where it is dragged and reports
    // that place as a location from end a, as every pad is bound; nothing in the scene changes until the drag ends, when
    // its owner binds the pad there. Escape, Save, Undo, Redo and leaving the game window cancel a drag. Standing, it
    // blocks no native world input; its owner blocks it during a binding edit (Blocking). Only its move handles along the
    // track and in the ground plane take a drag.
    [DefaultExecutionOrder(-10000)]
    public sealed class PadDistanceGizmo : MonoBehaviour {
        const string HookId = "CrankyAnt.ScenePatcher.PadDistanceGizmo";
        static PadDistanceGizmo current;
        TransformGizmo visual, nativeVisual;
        Behaviour controller;
        MapEditorAttachment attachment;
        NativeTrackPoses track;
        TargetIdentity identity;
        Harmony hooks;
        bool stopping, worldHooks, nativeVisualWasEnabled, dragging;
        float activated;
        Vector3 anchor;
        TrackLocation start, live;
        Action<TrackLocation> moved;
        Action<TrackLocation, TrackLocation> released;
        Action cancelled;
        internal static bool IsShown => current?.Shown == true;
        internal static bool RestingNativeGizmo(TransformGizmo instance) => IsShown && ReferenceEquals(current.nativeVisual, instance);
        // A binding edit runs with the gizmo (CommandBand.PadGizmoBlocks): the native world clicks, hover and box select are
        // blocked. Set by the owner each frame; a gizmo that only stands blocks nothing.
        internal static bool Blocking { get; set; }
        // The pointer is on a handle, where a press starts a drag, or a drag runs.
        internal bool HandleUnderPointer => Shown && visual.IsInteracting;
        internal bool Shown => !stopping && visual != null;
        // A drag is in progress: the gizmo owns input and the pad's binding waits for the release.
        internal bool Active => Shown && dragging;
        internal bool Closed => stopping;
        // The segment the gizmo moves along and the location it stands at now: the dragged place during a drag.
        internal string Segment => start?.Segment;
        internal TrackLocation Location => live ?? start;

        // released gets the location the drag began at and the one it was released at, or null without a move.
        internal void Initialize(MapEditorAttachment attachment, NativeTrackPoses track, TargetIdentity identity, TrackLocation start, Action<TrackLocation> moved, Action<TrackLocation, TrackLocation> released, Action cancelled) {
            this.attachment = attachment; this.track = track; this.identity = identity; this.start = start; this.moved = moved; this.released = released; this.cancelled = cancelled;
            try {
                if (current != null || GizmoController.IsShown) throw new EditRefusal("Another scene-patch gizmo is shown");
                controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared") as Behaviour;
                if (controller == null || !ReferenceEquals(Native.Read(controller, "MapEditor"), attachment.MapEditor)) throw new EditRefusal("The Map Editor changed");
                var nativeGizmo = Native.Read(controller, "TransformGizmo");
                var native = nativeGizmo == null ? null : Native.Read(nativeGizmo, "_gizmo") as TransformGizmo;
                if (native == null) throw new InvalidOperationException("The Map Editor gizmo is unavailable");
                if (native.IsDragging) throw new EditRefusal("The Map Editor gizmo is being dragged. " + WindowGuidance.ReleaseNativeGizmo);
                var pose = Pose(start);
                hooks = new Harmony(HookId);
                // These hooks act only while a drag is in progress: Escape, Save, Undo and Redo then cancel the drag.
                NativeGizmoInput.PatchDragCancellation(hooks, controller, attachment, typeof(PadDistanceGizmo), nameof(BeforeEscape), nameof(BeforeSaveOrHistory));
                WorldInputHooks.Acquire(); worldHooks = true;
                nativeVisual = native;
                nativeVisualWasEnabled = native.enabled;
                native.enabled = false;
                current = this;
                visual = gameObject.AddComponent<TransformGizmo>();
                visual.Camera = Camera.main;
                if (visual.Camera == null) throw new InvalidOperationException("The Map Editor camera is unavailable");
                // Local space along the track: the z handle points along the segment, the xz plane lies in its ground.
                visual.Space = TransformGizmoSpace.Local; visual.Mode = TransformGizmoMode.Move; visual.Visible = true;
                visual.ConstraintFilter = constraint => constraint == GizmoConstraint.AxisZ || constraint == GizmoConstraint.PlaneXZ;
                visual.SetPose(pose.Key, pose.Value);
                activated = Time.unscaledTime;
                visual.CanStartDrag = CanDrag;
                visual.DragStarted += Begin; visual.DragUpdated += Preview; visual.DragEnded += Complete;
                Game.Events.EventBus.Subscribe<Game.Events.WorldDidMoveEvent>(this, change => { if (Shown) visual.ShiftOrigin(change.Offset); });
            } catch { Stop(); throw; }
        }
        // Sets the standing gizmo on another location of a segment, as when the fields change; not during a drag.
        internal void Retarget(TrackLocation location) {
            if (!Shown || dragging) return;
            var pose = Pose(location);
            start = location; live = null;
            visual.SetPose(pose.Key, pose.Value);
        }
        // A drag starts only when no native operation holds the pointer or an edit session, as for the placement gizmo.
        bool CanDrag() {
            if (!Shown || Time.unscaledTime - activated < 0.2f || attachment.Session.Blocked != null) return false;
            return NativeGizmoInput.CanStartDrag(controller);
        }
        static bool BeforeEscape(ref bool __result) {
            if (current == null || !current.Active) return true;
            current.Cancel(); __result = true; return false;
        }
        static void BeforeSaveOrHistory(object __instance) {
            var owner = current;
            if (owner == null || !owner.Active) return;
            if (ReferenceEquals(owner.attachment.MapEditor, __instance) || ReferenceEquals(owner.attachment.Manager, __instance)) owner.Cancel();
        }
        void Begin() {
            dragging = true; live = null;
            try { anchor = Map(start); } catch (Exception e) { Fail(e); }
        }
        // The dragged point, projected onto the segment: the gizmo stands there, along the track's direction from end a,
        // and the owner shows the location.
        void Preview(TransformGizmoDelta delta) {
            if (!Active || delta.Kind != GizmoTransformKind.Move) return;
            try {
                var dragged = anchor + delta.TotalTranslation;
                var location = PadDistance.At(track, start.Segment, new double[] { dragged.x, dragged.y, dragged.z });
                var pose = Pose(location);
                visual.SetPose(pose.Key, pose.Value);
                if (live == null || !PadDistance.Same(live, location)) { live = location; moved?.Invoke(location); }
            } catch (Exception e) { Fail(e); }
        }
        void Complete() {
            if (!Active) return;
            var location = live;
            dragging = false;
            Stop();
            released?.Invoke(start, location);
        }
        void Fail(Exception e) {
            dragging = false;
            Stop();
            cancelled?.Invoke();
            PatcherLog.Debug(() => "The distance gizmo stopped for " + identity.Label + ": " + PatcherLog.Reason(e));
        }
        // Cancels a drag in progress; the binding stays as it was and the owner restores its fields.
        internal void Cancel() {
            var wasDragging = dragging;
            dragging = false;
            Stop();
            if (wasDragging) cancelled?.Invoke();
        }
        // Takes the gizmo away: the tab, the inspection or the pad's segment ended. A drag in progress is cancelled.
        internal void Close() => Cancel();
        void Stop() {
            if (stopping) return;
            stopping = true;
            var cleanup = new List<string>();
            try { Game.Events.EventBus.Unsubscribe(this); } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); }
            if (visual != null) {
                visual.DragStarted -= Begin; visual.DragUpdated -= Preview; visual.DragEnded -= Complete;
                visual.enabled = false; visual.Visible = false; Destroy(visual); visual = null;
            }
            if (ReferenceEquals(current, this)) { current = null; Blocking = false; }
            if (nativeVisual != null) {
                try { nativeVisual.enabled = nativeVisualWasEnabled; } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); }
                nativeVisual = null;
            }
            if (worldHooks) {
                worldHooks = false;
                try { WorldInputHooks.Release(); } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); }
            }
            try { hooks?.UnpatchAll(HookId); } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); }
            hooks = null;
            if (cleanup.Count != 0) attachment?.Fault.Fail(EditingFailure.GizmoCleanup, String.Join("; ", cleanup), null);
            if (gameObject != null) Destroy(gameObject);
        }
        // The world pose of a location: its point on the track, turned along the segment as seen from end a, so the
        // gizmo does not turn round when the nearer end changes.
        KeyValuePair<Vector3, Quaternion> Pose(TrackLocation location) {
            var length = PadDistance.Length(track, location.Segment) ?? throw new InvalidOperationException("Segment " + location.Segment + " is missing");
            var fromA = location.End == "a" ? location.Distance : length - location.Distance;
            var pose = track.TrackPose(TrackAnchor.At(new TrackLocation(location.Segment, "a", Math.Max(0, Math.Min(length, fromA)))));
            var map = new Vector3((float)pose.Position[0], (float)pose.Position[1], (float)pose.Position[2]);
            return new KeyValuePair<Vector3, Quaternion>(NativeInventory.MarkPosition(map), new Quaternion((float)pose.Rotation[0], (float)pose.Rotation[1], (float)pose.Rotation[2], (float)pose.Rotation[3]));
        }
        Vector3 Map(TrackLocation location) {
            var pose = track.TrackPose(TrackAnchor.At(location));
            return new Vector3((float)pose.Position[0], (float)pose.Position[1], (float)pose.Position[2]);
        }
        void Update() {
            if (!Shown) return;
            if (controller == null || !controller.isActiveAndEnabled || !ReferenceEquals(Native.Read(controller, "MapEditor"), attachment.MapEditor) || Camera.main == null || Mouse.current == null) { Close(); return; }
            if (Active && Keyboard.current?.escapeKey.wasPressedThisFrame == true) Cancel();
        }
        void OnApplicationFocus(bool focused) { if (!focused && Active) Cancel(); }
        void OnDisable() { if (!stopping) Close(); }
        void OnDestroy() { if (!stopping) Close(); }
    }
}
