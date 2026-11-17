// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UI.Builder;
using UI.Common;
using UnityEngine;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // Reload is an explicit native world relaunch, separate from Save. This capability fails independently.
    internal static class MapEditorReload {
        const string Id = "crankyant.scene-patcher.reload";
        static Harmony harmony;
        static bool confirming, allowed, relaunching;
        static readonly ReloadReturn returns = new ReloadReturn();
        internal static string ActiveMapId => Native.Read(Native.Type("Game.ActiveMapDefinition"), "Identifier")?.ToString();
        internal static string Error { get; private set; }
        internal static void Install() {
            if (harmony != null || Error != null) return;
            var patcher = new Harmony(Id);
            try {
                var state = Native.Type("Game.State.StateManager");
                var relaunch = state?.GetMethod("RelaunchEditorWithMap", Native.Flags, null, new[] { typeof(string) }, null);
                if (relaunch == null || state.GetProperty("Shared", Native.Flags) == null) throw new MissingMethodException("Native Map Editor relaunch unavailable");
                Hooks.PresentMethod();
                if (typeof(ModalAlertController).GetMethod("Present", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Action<UIPanelBuilder, Action>), typeof(bool), typeof(int), typeof(Action<Action>) }, null) == null)
                    throw new MissingMethodException("Native reload confirmation layout unavailable");
                patcher.Patch(relaunch, prefix: new HarmonyMethod(typeof(MapEditorReload).GetMethod(nameof(BeforeRelaunch), Native.Flags)));
                harmony = patcher;
            } catch (Exception e) {
                patcher.UnpatchAll(Id);
                Error = "Map Editor reload unavailable on this build: " + e.GetBaseException().Message;
            }
        }
        internal static void Remove() {
            harmony?.UnpatchAll(Id); harmony = null;
            // The runtime owns the bounded scalar return handoff. Teardown must release our hooks and
            // local state without deleting the request that a replacement mod instance needs.
            confirming = allowed = relaunching = false;
        }
        static bool BeforeRelaunch(string mapIdentifier) {
            if (allowed || Hooks.Current == null) return true;
            Request(Hooks.Current, mapIdentifier);
            return false;
        }
        internal static void Request(MapEditorAttachment current, string mapIdentifier) {
            if (Error != null) { current.Fault = Error; return; }
            if (!returns.CanReload(DateTime.UtcNow, message => current.Fault = message)) return;
            if (confirming || relaunching) return;
            try {
                current.Session.CheckFileVersion();
                var warning = current.Session.ReloadWarning(current.NativeDirty);
                var canSave = current.Session.CanSaveForReload;
                var message = "Reload the Map Editor? Reloading ends the Map Editor's Undo and Redo history. "
                    + (warning ?? "All required work is saved.")
                    + (canSave ? " Save and Reload saves pending native map and patcher work before reloading." : " Resolve the scene patch file version choice before saving.")
                    + " Discard and Reload discards any remaining unsaved work. Cancel keeps the Map Editor open. Your working location is restored when available.";
                Action<int> choose = value => {
                    confirming = false;
                    if (value == 0 || !ReferenceEquals(Hooks.Current, current)) return;
                    var choice = value == 1 ? ReloadChoice.Save : value == 2 ? ReloadChoice.Discard : ReloadChoice.Cancel;
                    var problem = current.Session.Reload(choice, () => current.NativeDirty, () => Save(current), () => {
                        if (!ModScenePatchFiles.IsMapEditor || !ReferenceEquals(Hooks.Current, current)) throw new InvalidOperationException("The active Map Editor changed; request reload again");
                        CaptureLocation(current, mapIdentifier);
                        var state = Native.Read(Native.Type("Game.State.StateManager"), "Shared");
                        relaunching = returns.Active(DateTime.UtcNow);
                        try { allowed = true; Native.Call(state, "RelaunchEditorWithMap", mapIdentifier); }
                        catch (Exception e) { relaunching = false; returns.Cancel(); throw new InvalidOperationException(e.GetBaseException().Message); }
                        finally { allowed = false; }
                    });
                    if (problem != null) {
                        current.Fault = problem;
                        Hooks.PresentMethod().Invoke(null, new object[] { "Reload Cancelled", problem + " The Map Editor remains open. You can try again, discard the remaining unsaved work, or cancel.", new[] { (0, "OK") }, (Action<int>)(_ => { }) });
                    }
                };
                confirming = true;
                PresentChoices(message, choose, canSave);
            } catch (Exception e) { confirming = false; current.Fault = "Reload cancelled: " + e.GetBaseException().Message; }
        }
        static void PresentChoices(string message, Action<int> choose, bool canSave) {
            var dismissed = false;
            void Select(int value, Action dismiss) {
                if (dismissed) return;
                // Like the native generic factory, a failed callback leaves the prompt open.
                try { choose(value); }
                catch (Exception e) { Debug.LogException(e); return; }
                dismissed = true;
                dismiss();
            }
            // The generic factory fixes the modal at 400 units, which clips these three action labels.
            // Use its native builder overload with the same typography, buttons and Cancel ordering.
            ModalAlertController.Present((builder, dismiss) => {
                builder.ConfigureLayout(16f);
                builder.AddLabel("Reload Map Editor", text => { text.fontSize = 22f; text.horizontalAlignment = HorizontalAlignmentOptions.Center; });
                builder.AddLabel(message, text => { text.fontSize = 18f; text.horizontalAlignment = HorizontalAlignmentOptions.Center; });
                builder.AlertButtons(buttons => {
                    buttons.AddButtonMedium("Cancel", () => Select(0, dismiss));
                    if (canSave) buttons.AddButtonMedium("Save and Reload", () => Select(1, dismiss));
                    buttons.AddButtonMedium("Discard and Reload", () => Select(2, dismiss));
                });
            }, escapeDismisses: false, width: 600, onCancel: dismiss => Select(0, dismiss));
        }
        static void Save(MapEditorAttachment current) {
            // This is the native Save button's order. A terrain failure must stop the map and patch writes too.
            var controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared");
            if (!ReferenceEquals(Native.Read(controller, "MapEditor"), current.MapEditor)) throw new InvalidOperationException("The active Map Editor changed before Save");
            var terrain = Native.Read(controller, "TerrainPaintTool");
            try { if (terrain != null) Native.Call(terrain, "SaveEditedTiles"); }
            catch (Exception e) {
                throw new InvalidOperationException("Native terrain data was not saved: " + e.GetBaseException().Message
                    + ". Outstanding: terrain data, " + current.Outstanding() + ". No reload was started; edits remain in memory.");
            }
            try { Native.Call(current.MapEditor, "Save"); }
            catch (TargetInvocationException e) { throw new InvalidOperationException(e.InnerException?.Message ?? e.Message); }
            finally { current.RefreshChrome(); }
        }
        static void CaptureLocation(MapEditorAttachment current, string mapIdentifier) {
            returns.Cancel();
            if (mapIdentifier != ActiveMapId) return;
            try {
                var selector = Native.Read(Native.Type("CameraSelector"), "shared");
                var point = GroundPoint(selector);
                var id = returns.Capture(mapIdentifier, ControllerId(), Coordinates(point), DateTime.UtcNow);
                current.Log("Reload captured working map location " + point + " for " + mapIdentifier + "; return " + id + ".");
            } catch (Exception e) { returns.Cancel(); current.Log("Working-location capture unavailable; reload will use the native camera location: " + e.GetBaseException().Message); }
        }
        static int ControllerId() {
            var controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared") as UnityEngine.Object;
            if (controller == null) throw new InvalidOperationException("Native Map Editor controller unavailable");
            return controller.GetInstanceID();
        }
        static Vector3 GroundPoint(object selector) {
            if (selector as UnityEngine.Object == null || Native.Read(selector, "_currentCamera") == null)
                throw new InvalidOperationException("Native camera unavailable");
            return (Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "WorldToGame", (Vector3)Native.Read(selector, "CurrentCameraGroundPosition"));
        }
        static double[] Coordinates(Vector3 point) => new double[] { point.x, point.y, point.z };
        internal static void Poll(MapEditorAttachment current) {
            var now = DateTime.UtcNow;
            if (!returns.Active(now)) {
                if (relaunching && current != null) current.Fault = "The return to your working location did not complete within five minutes. Check the game's load diagnostics before trying again.";
                relaunching = false;
                return;
            }
            relaunching = true;
            try {
                var state = Native.Read(Native.Type("Game.State.StateManager"), "Shared");
                if (!(bool)Native.Read(state, "IsSessionReady")) return;
                // A different ready map invalidates the handoff even if scene patch editing is unavailable there.
                // Do not inspect a transient map definition while the old session is tearing down.
                if (!ModScenePatchFiles.IsMapEditor) { returns.Cancel(); relaunching = false; return; }
                var map = ActiveMapId;
                if (String.IsNullOrEmpty(map)) return;
                var controller = ControllerId();
                var pending = returns.Pending(map, controller, now);
                if (pending == null) { relaunching = returns.Active(now); return; }
                if (current == null) return;
                var selectorType = Native.Type("CameraSelector"); var selector = Native.Read(selectorType, "shared");
                if (selector as UnityEngine.Object == null) return;
                // Native camera initialization and priority terrain loading finish asynchronously.
                if (Native.Read(selector, "_initializationScope") != null || Native.Read(selector, "_pendingJump") != null) return;
                if (!pending.Requested) {
                    var point = new Vector3((float)pending.Point[0], (float)pending.Point[1], (float)pending.Point[2]);
                    var strategy = Enum.Parse(selectorType.GetNestedType("CameraIdentifier", Native.Flags), "Strategy");
                    if (!returns.MarkRequested(pending.RequestId, map, controller, now)) return;
                    Native.Call(selector, "JumpToPoint", point, Quaternion.identity, strategy);
                    current.Log("Reload requested return " + pending.RequestId + " to working map location " + point + "; arrival is pending.");
                    return;
                }
                var measured = GroundPoint(selector);
                if (returns.Complete(pending.RequestId, map, controller, Coordinates(measured), now)) {
                    relaunching = false;
                    current.Log("Reload checked return " + pending.RequestId + " at working map location " + measured + ".");
                }
            } catch (Exception e) {
                relaunching = false; returns.Cancel();
                if (current != null) current.Fault = "The Map Editor reloaded, but your working location could not be restored: " + e.GetBaseException().Message;
            }
        }
    }
}
