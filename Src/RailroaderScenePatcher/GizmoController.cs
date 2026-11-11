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
    // Owns a single native gizmo gesture, without selecting or editing native map DTOs.
    [DefaultExecutionOrder(-10000)]
    public sealed class GizmoController : MonoBehaviour {
        const string HookId = "CrankyAnt.ScenePatcher.Gizmo";
        static GizmoController current;
        readonly List<TransformGizmo> suspended = new List<TransformGizmo>();
        TransformGizmo visual;
        Behaviour controller;
        Authoring authoring;
        PatchEngine engine;
        TargetIdentity identity;
        InspectedTarget inspected;
        PlacementGesture gesture;
        Action<Outcome, string> finished;
        Harmony hooks;
        bool ownsInput, stopping, priorPointer;
        float activated;
        internal bool Active => ownsInput;
        internal string State => gesture?.Active == true ? "Dragging. Release to apply; Escape or Cancel restores the prior placement." : "Drag a gizmo handle. Escape or Cancel ends placement.";

        internal void Initialize(PatchEngine engine, IScene scene, Authoring authoring, TargetIdentity identity, InspectedTarget inspected, TransformGizmoMode mode, Action<Outcome, string> finished) {
            this.engine = engine; this.authoring = authoring; this.identity = identity; this.inspected = inspected; this.finished = finished;
            try {
                if (current != null) throw new InvalidOperationException("Another scene-patch gizmo is active");
                controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared") as Behaviour;
                if (controller == null || !ReferenceEquals(Native.Read(controller, "MapEditor"), authoring.Editor)) throw new InvalidOperationException("The map editor changed");
                // Do not suspend another native operation while it owns a pointer or an edit session.
                foreach (var name in new[] { "_spanEndpointTool", "_builtinPlacementTool", "_terrainPaintTool", "_terrainTileTool" }) {
                    var tool = Native.Read(controller, name);
                    if (tool != null && (bool)Native.Read(tool, "OwnsPointer")) throw new InvalidOperationException("Finish or cancel the native pointer operation first");
                }
                var box = Native.Read(controller, "_boxSelectTool");
                if (box != null && (bool)Native.Read(box, "IsDragging")) throw new InvalidOperationException("Finish the native box selection first");
                if ((bool)Native.Read(Native.Type("Game.MapEditor.DtoFieldEditSession"), "AnySessionActive")) throw new InvalidOperationException("Finish the native field edit first");
                var matches = scene.Resolve(identity);
                if (matches.Count != 1) throw new InvalidOperationException("Target must resolve uniquely");
                inspected.Validate(scene, identity, matches[0]); scene.Preflight(matches[0]);
                var pose = scene.Read(matches[0]).Placement;
                var position = MapToWorld(pose);
                foreach (var native in controller.GetComponents<TransformGizmo>()) {
                    if (native.IsDragging) throw new InvalidOperationException("Finish the native transform gesture first");
                    if (native.enabled) suspended.Add(native);
                }
                hooks = new Harmony(HookId);
                Patch(controller.GetType(), "Update", nameof(BeforeNativeInput));
                Patch(controller.GetType(), "HandleEscapeCancel", nameof(BeforeEscape));
                Patch(authoring.Editor.GetType(), "Save", nameof(BeforeSaveOrHistory));
                Patch(authoring.Manager.GetType(), "Undo", nameof(BeforeSaveOrHistory));
                Patch(authoring.Manager.GetType(), "Redo", nameof(BeforeSaveOrHistory));
                priorPointer = (bool)Native.Read(Native.Type("ObjectPicker"), "ExternalPointerInteracting");
                foreach (var native in suspended) native.enabled = false;
                current = this; ownsInput = true;
                SetPointer(true);
                visual = gameObject.AddComponent<TransformGizmo>();
                visual.Camera = Camera.main;
                if (visual.Camera == null) throw new InvalidOperationException("The editor camera is unavailable");
                visual.Space = TransformGizmoSpace.World; visual.Mode = mode; visual.Visible = true;
                visual.SetPose(position, Rotation(pose));
                activated = Time.unscaledTime;
                visual.CanStartDrag = () => Active && Time.unscaledTime - activated >= 0.2f && authoring.Session.Blocked == null;
                visual.DragStarted += Begin; visual.DragUpdated += Preview; visual.DragEnded += Complete;
                EventBus.Subscribe<WorldDidMoveEvent>(this, OriginChanged);
            } catch { Stop(false); throw; }
        }
        void Patch(Type type, string name, string prefix) {
            var method = type.GetMethod(name, Native.Flags, null, Type.EmptyTypes, null);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            hooks.Patch(method, prefix: new HarmonyMethod(typeof(GizmoController).GetMethod(prefix, Native.Flags)) { priority = Priority.First });
        }
        static bool BeforeNativeInput(object __instance) => current == null || !current.Active || !ReferenceEquals(current.controller, __instance);
        static bool BeforeEscape(ref bool __result) {
            if (current == null || !current.Active) return true;
            current.Cancel(); __result = true; return false;
        }
        static void BeforeSaveOrHistory(object __instance) {
            var owner = current;
            if (owner == null || !owner.Active) return;
            if (ReferenceEquals(owner.authoring.Editor, __instance) || ReferenceEquals(owner.authoring.Manager, __instance)) owner.Cancel();
        }
        static void SetPointer(bool value) {
            var field = Native.Type("ObjectPicker")?.GetField("ExternalPointerInteracting", Native.Flags);
            if (field == null) throw new MissingMemberException("Native pointer ownership unavailable");
            field.SetValue(null, value);
        }
        void Begin() {
            try { gesture = new PlacementGesture(engine, authoring.Session, identity, inspected); }
            catch (Exception e) { Fail(e.Message); }
        }
        void Preview(TransformGizmoDelta delta) {
            if (!Active || gesture?.Active != true) return;
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
                if (!result.Applied) Fail(result.Error);
            } catch (Exception e) { Fail(e.Message); }
        }
        void Complete() {
            if (!Active) return;
            var result = gesture?.Complete();
            Stop(false);
            if (result != null) finished?.Invoke(result, null);
        }
        void Fail(string message) {
            var result = gesture?.Cancel();
            Stop(false);
            finished?.Invoke(null, "Gizmo refused: " + message + (result?.Error == null ? "" : "; " + result.Error));
        }
        internal void Cancel() { Stop(true); }
        void Stop(bool notify) {
            if (stopping) return;
            stopping = true;
            var result = gesture?.Cancel();
            var cleanup = new List<string>();
            try { EventBus.Unsubscribe(this); } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); }
            if (visual != null) {
                visual.DragStarted -= Begin; visual.DragUpdated -= Preview; visual.DragEnded -= Complete;
                visual.enabled = false; visual.Visible = false; Destroy(visual); visual = null;
            }
            if (ReferenceEquals(current, this)) current = null;
            if (ownsInput) {
                ownsInput = false;
                try { SetPointer(priorPointer); } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); }
                foreach (var native in suspended) if (native != null) {
                    try { native.enabled = true; } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); }
                }
            }
            try { hooks?.UnpatchAll(HookId); } catch (Exception e) { cleanup.Add(e.GetBaseException().Message); }
            hooks = null;
            try {
                if (cleanup.Count != 0) {
                    var message = "Gizmo input cleanup is unverified; reload the editor: " + String.Join("; ", cleanup);
                    authoring.Fault = message;
                    if (notify) finished?.Invoke(result, message);
                } else if (notify) finished?.Invoke(result, result?.Error == null ? "Gizmo cancelled; the prior placement is restored." : "Gizmo cancellation failed: " + result.Error);
            } finally { if (gameObject != null) Destroy(gameObject); }
        }
        void OriginChanged(WorldDidMoveEvent change) { if (visual != null && Active) visual.ShiftOrigin(change.Offset); }
        void Update() {
            if (!Active) return;
            if (controller == null || !controller.isActiveAndEnabled || !ReferenceEquals(Native.Read(controller, "MapEditor"), authoring.Editor) || Camera.main == null || Mouse.current == null) { Cancel(); return; }
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) Cancel();
        }
        void OnApplicationFocus(bool focused) { if (!focused && Active) Cancel(); }
        void OnDisable() { if (Active) Cancel(); }
        void OnDestroy() { if (!stopping) Stop(true); }
        static Quaternion Rotation(Placement pose) => new Quaternion((float)pose.Rotation[0], (float)pose.Rotation[1], (float)pose.Rotation[2], (float)pose.Rotation[3]);
        static Vector3 MapToWorld(Placement pose) {
            var transformer = Native.Type("Helpers.WorldTransformer");
            if (Native.Read(transformer, "Shared") == null) throw new InvalidOperationException("Floating origin unavailable");
            var map = new Vector3((float)pose.Position[0], (float)pose.Position[1], (float)pose.Position[2]);
            var world = (Vector3)Native.Call(transformer, "GameToWorld", map);
            if (((Vector3)Native.Call(transformer, "WorldToGame", world) - map).sqrMagnitude > .0001f) throw new InvalidOperationException("Map/origin conversion did not round trip");
            return world;
        }
    }
}
