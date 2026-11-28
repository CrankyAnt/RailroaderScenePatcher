// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Game.Events;
using HarmonyLib;
using RailroaderScenePatcher.Core;
using UI.Gizmos;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RailroaderScenePatcher {
    // Shows the native gizmo on the inspected object for as long as the Gizmo mode is open, without selecting or editing
    // native map DTOs. While shown, it blocks native world clicks but keeps the native gizmo's own handle input.
    // Each completed drag is one operation; the
    // owner then shows the gizmo again on the new placement with a new inspection.
    [DefaultExecutionOrder(-10000)]
    public sealed class GizmoController : MonoBehaviour {
        const string HookId = "CrankyAnt.ScenePatcher.Gizmo";
        static GizmoController current;
        TransformGizmo visual, nativeVisual;
        Behaviour controller;
        MapEditorAttachment attachment;
        PatchEngine engine;
        TargetIdentity identity;
        InspectedTarget inspected;
        PlacementGesture gesture;
        Action<Outcome, string> finished;
        Harmony hooks;
        bool stopping, holding, worldHooks, nativeVisualWasEnabled;
        IScene scene;
        object target;
        float activated;
        // A drag is in progress: the one state in which the gizmo owns input and blocks other edits.
        internal bool Active => !stopping && gesture?.Active == true;
        // The gizmo stands on the object and waits for a drag.
        internal bool Shown => !stopping && visual != null;
        internal static bool IsShown => current?.Shown == true;
        internal static bool RestingNativeGizmo(TransformGizmo instance) => IsShown && ReferenceEquals(current.nativeVisual, instance);
        // The controller has ended; its owner lets go of it.
        internal bool Closed => stopping;
        internal const string DraggingState = "Dragging. Release to apply; Escape restores the prior placement.";
        internal string State => Active ? DraggingState : holding ? "Reading the object again; the gizmo takes a drag in a moment." : "Drag a gizmo handle in the world.";
        // The object changed under the standing gizmo, as by a native Undo: the gizmo stays on the object, at its
        // placement now, and takes no drag until Rebind gives it the new inspection.
        internal bool Holding => holding && Shown;
        internal void Hold() {
            if (!Shown || Active) return;
            holding = true;
            try { shown = ShownPlacement(); Pose("hold"); } catch { }
        }
        // Binds the standing gizmo to a new inspection of the same object and lets it take drags again.
        internal void Rebind(InspectedTarget inspection) {
            if (!Shown || Active) throw new EditRefusal("The gizmo is not standing");
            target = Checked(inspection); inspected = inspection;
            shown = ShownPlacement();
            Pose("rebind");
            holding = false;
        }
        // The placement the gizmo stands at now, in map coordinates and as a rotation: the preview placement during a drag.
        internal bool TryPlacement(out Vector3 map, out Quaternion rotation) {
            map = default; rotation = Quaternion.identity;
            var placement = live ?? shown;
            if (placement?.Position == null || placement.Rotation == null) return false;
            map = new Vector3((float)placement.Position[0], (float)placement.Position[1], (float)placement.Position[2]);
            rotation = Rotation(placement);
            return true;
        }
        Placement shown, live;
        DragFault dragFault;
        // The one target the identity resolves to, checked against the inspection and the capability: every failure of
        // these checks refuses the gizmo.
        object Checked(InspectedTarget inspection) => EditRefusal.Check(() => {
            var matches = scene.Resolve(identity);
            if (matches.Count != 1) throw new InvalidOperationException(matches.Count == 0 ? PatchEngine.TargetMissing : PatchEngine.TargetAmbiguous);
            inspection.Validate(scene, identity, matches[0]); scene.Preflight(matches[0]);
            return matches[0];
        });

        // Where the target stands in map coordinates: an object with its own placement in a frame reads it as recorded there.
        Placement ShownPlacement() { var read = scene.Read(target).Placement; return scene is IRecordedPlacementScene recorded ? recorded.InMap(target, read) : read; }
        Action beginEdit;
        Func<bool> inputBlocked;
        internal void Initialize(PatchEngine engine, IScene scene, MapEditorAttachment attachment, TargetIdentity identity, InspectedTarget inspected, TransformGizmoMode mode, Func<bool> inputBlocked, Action beginEdit, Action<Outcome, string> finished) {
            this.engine = engine; this.scene = scene; this.attachment = attachment; this.identity = identity; this.inspected = inspected; this.finished = finished;
            this.inputBlocked = inputBlocked; this.beginEdit = beginEdit;
            dragFault = new DragFault(attachment.Fault);
            try {
                if (current != null) throw new EditRefusal("Another scene-patch gizmo is shown");
                controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared") as Behaviour;
                if (controller == null || !ReferenceEquals(Native.Read(controller, "MapEditor"), attachment.MapEditor)) throw new EditRefusal("The Map Editor changed");
                target = Checked(inspected);
                shown = ShownPlacement();
                var position = MapToWorld(shown);
                var nativeGizmo = Native.Read(controller, "TransformGizmo");
                var native = nativeGizmo == null ? null : Native.Read(nativeGizmo, "_gizmo") as TransformGizmo;
                if (native == null) throw new InvalidOperationException("The Map Editor gizmo is unavailable");
                if (native.IsDragging) throw new EditRefusal("The Map Editor gizmo is being dragged. " + WindowGuidance.ReleaseNativeGizmo);
                var box = Native.Read(controller, "BoxSelect");
                if (box != null && (bool)Native.Read(box, "IsDragging")) {
                    Native.Call(box, "Cancel");
                    Native.Call(controller, "RefreshWindowSelection");
                }
                hooks = new Harmony(HookId);
                // These hooks act only while a drag is in progress: Escape, Save, Undo and Redo then cancel the drag.
                Patch(controller.GetType(), "HandleEscapeCancel", nameof(BeforeEscape));
                Patch(attachment.MapEditor.GetType(), "Save", nameof(BeforeSaveOrHistory));
                Patch(attachment.Manager.GetType(), "Undo", nameof(BeforeSaveOrHistory));
                Patch(attachment.Manager.GetType(), "Redo", nameof(BeforeSaveOrHistory));
                WorldInputHooks.Acquire(); worldHooks = true;
                nativeVisual = native;
                nativeVisualWasEnabled = native.enabled;
                native.enabled = false;
                current = this;
                visual = gameObject.AddComponent<TransformGizmo>();
                visual.Camera = Camera.main;
                if (visual.Camera == null) throw new InvalidOperationException("The Map Editor camera is unavailable");
                // Local space, as the native map editor's gizmo: the handles follow the object's own rotation.
                visual.Space = TransformGizmoSpace.Local; visual.Mode = mode; visual.Visible = true;
                visual.SetPose(position, Rotation(shown));
                activated = Time.unscaledTime;
                visual.CanStartDrag = CanDrag;
                visual.DragStarted += Begin; visual.DragUpdated += Preview; visual.DragEnded += Complete;
                EventBus.Subscribe<WorldDidMoveEvent>(this, OriginChanged);
            } catch { Stop(false); throw; }
        }
        // A drag starts only when no native operation holds the pointer or an edit session: the gizmo does not take
        // input from the native map editor, it waits for it.
        bool CanDrag() {
            if (!Shown || holding || inputBlocked() || Time.unscaledTime - activated < 0.2f || attachment.Session.Blocked != null) return false;
            try {
                foreach (var name in new[] { "_spanEndpointTool", "_builtinPlacementTool", "_terrainPaintTool", "_terrainTileTool" }) {
                    var tool = Native.Read(controller, name);
                    if (tool != null && (bool)Native.Read(tool, "OwnsPointer")) return false;
                }
                var box = Native.Read(controller, "_boxSelectTool");
                if (box != null && (bool)Native.Read(box, "IsDragging")) return false;
                if ((bool)Native.Read(Native.Type("Game.MapEditor.DtoFieldEditSession"), "AnySessionActive")) return false;
                foreach (var native in controller.GetComponents<TransformGizmo>()) if (native != null && native.IsDragging) return false;
            } catch { return false; }
            return true;
        }
        // Switches between Move and Rotate while the gizmo stands; not during a drag.
        internal void SetMode(TransformGizmoMode mode) { if (Shown && !Active) visual.Mode = mode; }
        void Patch(Type type, string name, string prefix, params Type[] parameters) {
            var method = type.GetMethod(name, Native.Flags, null, parameters, null);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            hooks.Patch(method, prefix: new HarmonyMethod(typeof(GizmoController).GetMethod(prefix, Native.Flags)) { priority = Priority.First });
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
            dragFault.Begin();
            try { beginEdit(); gesture = new PlacementGesture(engine, attachment.Session, identity, inspected); live = gesture.Initial; }
            catch (Exception e) { Fail(PatcherLog.Reason(e)); }
        }
        void Preview(TransformGizmoDelta delta) {
            if (!Active) return;
            // Scaling is no placement edit: a refusal, not a failure.
            if (delta.Kind != GizmoTransformKind.Move && delta.Kind != GizmoTransformKind.Rotate) { Fail("Scene-patch gizmos support movement and rotation only"); return; }
            try {
                var start = gesture.Initial;
                var placement = delta.Kind == GizmoTransformKind.Move
                    ? GizmoDrag.Moved(start, new double[] { delta.TotalTranslation.x, delta.TotalTranslation.y, delta.TotalTranslation.z })
                    : GizmoDrag.Turned(start, new double[] { delta.TotalRotation.x, delta.TotalRotation.y, delta.TotalRotation.z, delta.TotalRotation.w });
                var result = gesture.Preview(placement);
                if (!result.Applied) Fail(result.Error); else live = placement;
            } catch (Exception e) {
                // An exception outside the gesture's own checks is a patcher failure, written as a player error.
                dragFault.Fail(EditingFailure.GizmoPreview, e.GetBaseException().Message, identity?.Path);
                Fail(e.Message);
            }
        }
        void Complete() {
            if (stopping || gesture == null) return;
            var result = gesture.Complete();
            Stop(false);
            if (result != null) finished?.Invoke(result, null);
        }
        void Fail(string message) {
            var result = gesture?.Cancel();
            Stop(false);
            finished?.Invoke(null, "Gizmo refused: " + message + (result?.Error == null ? "" : "; " + result.Error));
        }
        // Cancels a drag in progress and reports it; the prior placement is restored.
        internal void Cancel() { Stop(true); }
        // Takes the gizmo away without a result: the mode, the tab or the inspection ended. A drag in progress is
        // cancelled and reported.
        internal void Close() { Stop(Active); }
        void Stop(bool notify) {
            if (stopping) return;
            var dragging = gesture?.Active == true;
            stopping = true;
            var result = dragging ? gesture.Cancel() : null;
            var cleanup = new List<string>();
            try { EventBus.Unsubscribe(this); } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); }
            if (visual != null) {
                visual.DragStarted -= Begin; visual.DragUpdated -= Preview; visual.DragEnded -= Complete;
                visual.enabled = false; visual.Visible = false; Destroy(visual); visual = null;
            }
            if (ReferenceEquals(current, this)) current = null;
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
            try {
                // A restoration that did not verify leaves the object guarded: a patcher failure, written as a player error.
                if (gesture?.CleanupFailure != null) dragFault?.Fail(EditingFailure.GizmoRestore, gesture.CleanupFailure, identity?.Path);
                if (cleanup.Count != 0) {
                    var detail = String.Join("; ", cleanup);
                    var message = EditingFault.Describe(EditingFailure.GizmoCleanup, detail);
                    dragFault?.Fail(EditingFailure.GizmoCleanup, detail, identity?.Path);
                    if (notify) finished?.Invoke(result, message);
                } else if (notify) finished?.Invoke(result, result?.Error == null ? "Gizmo drag cancelled; the prior placement is restored." : "Gizmo cancellation failed: " + result.Error);
            } finally { if (gameObject != null) Destroy(gameObject); }
        }
        // The gizmo stands at its object's marking (MapOrigin.MarkPosition) and shifts with every origin change, also for
        // an object the game does not move with the origin, as the native gizmo shifts with it. Writes keep the object's
        // own rule through its capability.
        void OriginChanged(WorldDidMoveEvent change) {
            if (visual == null || !Shown) return;
            visual.ShiftOrigin(change.Offset);
        }
        // Sets the standing gizmo on the object's placement now. Only a drag moves the gizmo with its object. A hold
        // follows a change of the object itself, such as a native Undo or Redo, and moves the gizmo with it.
        void Pose(string reason) {
            visual.SetPose(MapToWorld(shown), Rotation(shown));
        }
        void Update() {
            if (!Shown) return;
            if (controller == null || !controller.isActiveAndEnabled || !ReferenceEquals(Native.Read(controller, "MapEditor"), attachment.MapEditor) || Camera.main == null || Mouse.current == null) { Close(); return; }
            if (Active && Keyboard.current?.escapeKey.wasPressedThisFrame == true) Cancel();
        }
        void OnApplicationFocus(bool focused) { if (!focused && Active) Cancel(); }
        void OnDisable() { if (!stopping) Close(); }
        void OnDestroy() { if (!stopping) Stop(Active); }
        static Quaternion Rotation(Placement placement) => new Quaternion((float)placement.Rotation[0], (float)placement.Rotation[1], (float)placement.Rotation[2], (float)placement.Rotation[3]);
        static Vector3 MapToWorld(Placement placement) {
            if (Native.Read(Native.Type("Helpers.WorldTransformer"), "Shared") == null) throw new InvalidOperationException("Floating origin unavailable");
            var map = new Vector3((float)placement.Position[0], (float)placement.Position[1], (float)placement.Position[2]);
            var world = NativeInventory.MarkPosition(map);
            if ((NativeInventory.MapPosition(world, true) - map).sqrMagnitude > .0001f) throw new InvalidOperationException("Map/origin conversion did not round trip");
            return world;
        }
    }
}
