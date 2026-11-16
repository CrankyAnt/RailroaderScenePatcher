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
    // native map DTOs. The gizmo is shown and waits; it owns input only during a drag. While it is merely shown the
    // native editor keeps its own input, its own gizmos and its Escape, as it does beside any native gizmo: the native
    // gizmo class already tells the game when the pointer is on a handle. Each completed drag is one operation; the
    // owner then shows the gizmo again on the new pose with a new inspection.
    [DefaultExecutionOrder(-10000)]
    public sealed class GizmoController : MonoBehaviour {
        const string HookId = "CrankyAnt.ScenePatcher.Gizmo";
        static GizmoController current;
        TransformGizmo visual;
        Behaviour controller, picker;
        Authoring authoring;
        PatchEngine engine;
        TargetIdentity identity;
        InspectedTarget inspected;
        PlacementGesture gesture;
        Action<Outcome, string> finished;
        Harmony hooks;
        bool stopping, pickerWasEnabled, pickerHooked, holding;
        IScene scene;
        object target;
        float activated;
        // A drag is in progress: the one state in which the gizmo owns input and blocks other edits.
        internal bool Active => !stopping && gesture?.Active == true;
        // The gizmo stands on the object and waits for a drag.
        internal bool Shown => !stopping && visual != null;
        // The controller has ended; its owner lets go of it.
        internal bool Closed => stopping;
        internal string State => Active ? "Dragging. Release to apply; Escape restores the prior placement." : holding ? "Reading the object again; the gizmo takes a drag in a moment." : "Drag a gizmo handle in the world.";
        // The object changed under the standing gizmo, as by a native Undo: the gizmo stays on the object, at its
        // pose now, and takes no drag until Rebind gives it the new inspection.
        internal bool Holding => holding && Shown;
        internal void Hold() {
            if (!Shown || Active) return;
            holding = true;
            try { shown = scene.Read(target).Placement; visual.SetPose(MapToWorld(shown), Rotation(shown)); } catch { }
        }
        // Binds the standing gizmo to a new inspection of the same object and lets it take drags again.
        internal void Rebind(InspectedTarget inspection) {
            if (!Shown || Active) throw new InvalidOperationException("The gizmo is not standing");
            var matches = scene.Resolve(identity);
            if (matches.Count != 1) throw new InvalidOperationException("Target must resolve uniquely");
            inspection.Validate(scene, identity, matches[0]); scene.Preflight(matches[0]);
            target = matches[0]; inspected = inspection;
            shown = scene.Read(target).Placement;
            visual.SetPose(MapToWorld(shown), Rotation(shown));
            holding = false;
        }
        // The pose the gizmo stands at now, in map coordinates and as a rotation: the preview pose during a drag.
        internal bool TryPose(out Vector3 map, out Quaternion rotation) {
            map = default; rotation = Quaternion.identity;
            var pose = live ?? shown;
            if (pose?.Position == null || pose.Rotation == null) return false;
            map = new Vector3((float)pose.Position[0], (float)pose.Position[1], (float)pose.Position[2]);
            rotation = Rotation(pose);
            return true;
        }
        Placement shown, live;

        internal void Initialize(PatchEngine engine, IScene scene, Authoring authoring, TargetIdentity identity, InspectedTarget inspected, TransformGizmoMode mode, Action<Outcome, string> finished) {
            this.engine = engine; this.scene = scene; this.authoring = authoring; this.identity = identity; this.inspected = inspected; this.finished = finished;
            try {
                if (current != null) throw new InvalidOperationException("Another scene-patch gizmo is shown");
                controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared") as Behaviour;
                if (controller == null || !ReferenceEquals(Native.Read(controller, "MapEditor"), authoring.Editor)) throw new InvalidOperationException("The map editor changed");
                var matches = scene.Resolve(identity);
                if (matches.Count != 1) throw new InvalidOperationException("Target must resolve uniquely");
                inspected.Validate(scene, identity, matches[0]); scene.Preflight(matches[0]);
                target = matches[0];
                shown = scene.Read(matches[0]).Placement;
                var position = MapToWorld(shown);
                hooks = new Harmony(HookId);
                // These hooks act only while a drag is in progress: Escape, Save, Undo and Redo then cancel the drag.
                Patch(controller.GetType(), "HandleEscapeCancel", nameof(BeforeEscape));
                Patch(authoring.Editor.GetType(), "Save", nameof(BeforeSaveOrHistory));
                Patch(authoring.Manager.GetType(), "Undo", nameof(BeforeSaveOrHistory));
                Patch(authoring.Manager.GetType(), "Redo", nameof(BeforeSaveOrHistory));
                current = this;
                // World click targets, such as a station's click spot, do not react while the gizmo is shown: a click
                // on a handle that lies over one would open its window. The game's picker is the one that finds
                // them; while the gizmo stands it finds none, so no target is clicked and none shows its hover
                // tooltip. The picker itself keeps running, so the native editor works as usual: a right-click on a
                // track still opens the native track menu, which the picker shows when it finds no target.
                try {
                    picker = Native.Read(Native.Type("ObjectPicker"), "Shared") as Behaviour;
                    var find = picker?.GetType().GetMethod("TryGetPickableUnderMouse", Native.Flags);
                    if (find != null) { hooks.Patch(find, prefix: new HarmonyMethod(typeof(GizmoController).GetMethod(nameof(NoPickable), Native.Flags)) { priority = Priority.First }); pickerHooked = true; }
                    // A build whose picker has no such method rests the whole picker, as before; the track menu
                    // then rests with it.
                    else if (picker != null) { pickerWasEnabled = picker.enabled; picker.enabled = false; }
                } catch { picker = null; }
                visual = gameObject.AddComponent<TransformGizmo>();
                visual.Camera = Camera.main;
                if (visual.Camera == null) throw new InvalidOperationException("The editor camera is unavailable");
                visual.Space = TransformGizmoSpace.World; visual.Mode = mode; visual.Visible = true;
                visual.SetPose(position, Rotation(shown));
                activated = Time.unscaledTime;
                visual.CanStartDrag = CanDrag;
                visual.DragStarted += Begin; visual.DragUpdated += Preview; visual.DragEnded += Complete;
                EventBus.Subscribe<WorldDidMoveEvent>(this, OriginChanged);
            } catch { Stop(false); throw; }
        }
        // A drag starts only when no native operation holds the pointer or an edit session: the gizmo does not take
        // input from the native editor, it waits for it.
        bool CanDrag() {
            if (!Shown || holding || Time.unscaledTime - activated < 0.2f || authoring.Session.Blocked != null) return false;
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
        void Patch(Type type, string name, string prefix) {
            var method = type.GetMethod(name, Native.Flags, null, Type.EmptyTypes, null);
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
            if (ReferenceEquals(owner.authoring.Editor, __instance) || ReferenceEquals(owner.authoring.Manager, __instance)) owner.Cancel();
        }
        void Begin() {
            try { gesture = new PlacementGesture(engine, authoring.Session, identity, inspected); live = gesture.Initial; }
            catch (Exception e) { Fail(e.Message); }
        }
        void Preview(TransformGizmoDelta delta) {
            if (!Active) return;
            try {
                var start = gesture.Initial;
                Placement pose;
                if (delta.Kind == GizmoTransformKind.Move) pose = new Placement(new[] {
                    start.Position[0] + delta.TotalTranslation.x, start.Position[1] + delta.TotalTranslation.y, start.Position[2] + delta.TotalTranslation.z }, start.Rotation);
                else if (delta.Kind == GizmoTransformKind.Rotate) {
                    var q = delta.TotalRotation * Rotation(start);
                    pose = new Placement(start.Position, new double[] { q.x, q.y, q.z, q.w });
                } else throw new InvalidOperationException("Scene-patch gizmos support movement and rotation only");
                var result = gesture.Preview(pose);
                if (!result.Applied) Fail(result.Error); else live = pose;
            } catch (Exception e) { Fail(e.Message); }
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
            // The hook on the picker leaves with the other hooks below; a picker that was rested returns.
            if (picker != null && !pickerHooked) { try { picker.enabled = pickerWasEnabled; } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); } }
            picker = null;
            try { hooks?.UnpatchAll(HookId); } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); }
            hooks = null;
            try {
                if (cleanup.Count != 0) {
                    var message = "Gizmo cleanup is unverified; reload the editor: " + String.Join("; ", cleanup);
                    authoring.Fault = message;
                    if (notify) finished?.Invoke(result, message);
                } else if (notify) finished?.Invoke(result, result?.Error == null ? "Gizmo drag cancelled; the prior placement is restored." : "Gizmo cancellation failed: " + result.Error);
            } finally { if (gameObject != null) Destroy(gameObject); }
        }
        // The gizmo stands at its object's marking (MapOrigin.MarkPosition) and shifts with every origin change, also for
        // an object the game does not move with the origin. Writes keep the object's own rule through its capability.
        void OriginChanged(WorldDidMoveEvent change) { if (visual != null && Shown) visual.ShiftOrigin(change.Offset); }
        // The picker's search for a world target under the pointer, while a gizmo stands: none. The out values are
        // the ones the picker sets itself when it finds nothing.
        static bool NoPickable(ref bool __result, ref float distanceToPickable, System.Reflection.MethodBase __originalMethod, object[] __args) {
            if (current == null || !current.Shown) return true;
            try {
                var parameters = __originalMethod.GetParameters();
                for (var i = 0; i < parameters.Length; i++) if (parameters[i].Name == "picked") __args[i] = null;
            } catch { }
            distanceToPickable = float.MaxValue;
            __result = false;
            return false;
        }
        // A picker that is rested as a whole leaves the tooltip of its last target; it is cleared while the gizmo stands.
        static void ClearPickTooltip() {
            try {
                var controller = Native.Read(Native.Type("UI.Tooltips.UITooltipController"), "Shared");
                if (controller == null) return;
                var property = controller.GetType().GetProperty("PickableTooltipInfo", Native.Flags);
                var empty = property?.PropertyType.GetProperty("Empty", Native.Flags | System.Reflection.BindingFlags.Static)?.GetValue(null)
                    ?? property?.PropertyType.GetField("Empty", Native.Flags | System.Reflection.BindingFlags.Static)?.GetValue(null);
                if (property != null && empty != null) property.SetValue(controller, empty);
            } catch { }
        }
        void LateUpdate() { if (Shown && picker != null && !pickerHooked) ClearPickTooltip(); }
        void Update() {
            if (Shown && picker != null && !pickerHooked) ClearPickTooltip();
            if (!Shown) return;
            if (controller == null || !controller.isActiveAndEnabled || !ReferenceEquals(Native.Read(controller, "MapEditor"), authoring.Editor) || Camera.main == null || Mouse.current == null) { Close(); return; }
            if (Active && Keyboard.current?.escapeKey.wasPressedThisFrame == true) Cancel();
        }
        void OnApplicationFocus(bool focused) { if (!focused && Active) Cancel(); }
        void OnDisable() { if (!stopping) Close(); }
        void OnDestroy() { if (!stopping) Stop(Active); }
        static Quaternion Rotation(Placement pose) => new Quaternion((float)pose.Rotation[0], (float)pose.Rotation[1], (float)pose.Rotation[2], (float)pose.Rotation[3]);
        static Vector3 MapToWorld(Placement pose) {
            if (Native.Read(Native.Type("Helpers.WorldTransformer"), "Shared") == null) throw new InvalidOperationException("Floating origin unavailable");
            var map = new Vector3((float)pose.Position[0], (float)pose.Position[1], (float)pose.Position[2]);
            var world = NativeInventory.MarkPosition(map);
            if ((NativeInventory.MapPosition(world, true) - map).sqrMagnitude > .0001f) throw new InvalidOperationException("Map/origin conversion did not round trip");
            return world;
        }
    }
}
