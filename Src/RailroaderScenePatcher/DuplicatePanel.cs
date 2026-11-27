// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    // Duplicate in the window (DuplicateState): the Duplicate button of the Object tab, the blank object window with
    // Place and Cancel, and the placing click. While the blank window is open the other controls of the window wait
    // for it, and the camera moves as always: only a left click on the world is taken, and only while placing.
    public sealed partial class PatcherBehaviour {
        const string DuplicatePlace = "Duplicate";
        readonly DuplicateState duplicate = new DuplicateState();
        readonly PlacingPointer placingPointer = new PlacingPointer();
        bool Duplicating => duplicate.Open;
        bool PlacingModObject => duplicate.Step == DuplicateStep.Placing;
        // Why the window's other controls are unavailable now, or null: the file panel's reason, or the open Duplicate.
        string WindowLockReason => FileLockReason ?? (Duplicating ? DuplicateState.LockReason : null);
        // The mod object the last placing click made: it is inspected once the inventory lists it.
        TargetIdentity placedModObject;

        // The pointer of the placing click: the world click the input blocker confirmed this frame, and the ground
        // under it.
        sealed class PlacingPointer : IPlacingPointer {
            internal Vector2? Click;
            public double[] WorldClick => Click is Vector2 point ? new double[] { point.x, point.y } : null;
            public double[] MapPoint(double[] click) {
                var camera = Camera.main;
                var terrain = LayerMask.NameToLayer("Terrain");
                if (camera == null || terrain < 0) return null;
                // The click is in GUI coordinates, which run down from the top of the screen.
                var ray = camera.ScreenPointToRay(new Vector3((float)click[0], Screen.height - (float)click[1], 0));
                if (!Physics.Raycast(ray, out var hit, 20000f, 1 << terrain)) return null;
                var map = NativeInventory.MapPosition(hit.point, true);
                return new double[] { map.x, map.y, map.z };
            }
        }
        // The families of the listed objects at or below the object, for the rule that decides whether a copy rebuilds it.
        static IEnumerable<string> FamiliesBelow(SceneObservation entry) {
            if (!(entry?.Handle is GameObject go) || go == null) return null;
            return go.GetComponentsInChildren<Component>(true).Where(c => c != null && !(c is Transform)).Select(NativeInventory.FamilyOf).Where(family => family != null).Distinct().ToArray();
        }
        // Why Duplicate is inactive for the inspected object, or null. The object's own reason comes first.
        string DuplicateReason() {
            if (selected == null) return "Inspect an object first.";
            string rule;
            try { rule = DuplicateState.Reason(selected, FamiliesBelow(selected)); }
            catch (Exception e) { rule = "The object couldn't be read: " + e.GetBaseException().Message.TrimEnd('.') + "."; }
            if (rule != null) return rule;
            if (attachment?.Session.Blocked != null) return attachment.Session.Blocked + " The recovery choices are in the session details.";
            return StarterBlock() ?? (Requests.Pending ? "Wait for the scene check to finish." : null);
        }
        Act DuplicateAct() => Slot(DuplicateState.Label, DuplicateState.Tip, DuplicateReason(), BeginDuplicate);
        void BeginDuplicate() {
            if (selected == null || Duplicating || DuplicateReason() != null) return;
            CancelGizmo(); ClearSnap(); CloseCoverageForm(); CloseEquipmentForm(); EndWorldEditAction();
            duplicate.Begin(selected);
            presentation?.RequestRefresh();
        }
        // Cancel: the inspected object is still the source, so the window shows it again.
        void CancelDuplicate() {
            duplicate.Cancel();
            presentation?.RequestRefresh();
        }
        // Closing the window, a map reload, the file panel or the end of editing: nothing of the mod object existed.
        void EndDuplicate() {
            if (!Duplicating) return;
            duplicate.End();
            presentation?.RequestRefresh();
        }
        // The blank object window: the title names the source, the Object tab alone shows what is known, and the
        // pinned row holds Place and Cancel.
        void DrawDuplicate(UIPanelBuilder builder) {
            var source = duplicate.Source;
            Heading(builder, Literal(DuplicateState.Title(source)), DuplicatePlace, source.Identity.Path).Height(RowHeight);
            Line(builder, Literal(CommandBand.ShownFamily(source)) + "   " + DuplicateState.NotPlaced, "State", null, RowHeight);
            // The one tab takes the place of the tab strip, at its height.
            Heading(builder, "Object", null, null).Height(30);
            var view = presentation.Scroll(builder, TabBodyMinimumHeight, content => {
                content.ConfigureLayout(4f);
                PlainLabel(content, duplicate.Instruction);
                if (duplicate.Message != null) PlainLabel(content, duplicate.Message);
                Field(content, "Source", source.DisplayName);
                Field(content, "Family", CommandBand.ShownFamily(source));
                Field(content, "Position", DuplicateState.NotPlaced);
            });
            var fill = view.GetComponent<LayoutElement>();
            if (fill != null) { fill.minHeight = fill.preferredHeight = TabBodyMinimumHeight; fill.flexibleHeight = 10000; }
            var viewport = view.GetComponent<ScrollRect>().viewport;
            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
            ActionRow(builder, new[] {
                Slot(DuplicateState.PlaceLabel, DuplicateState.PlaceTip, duplicate.PlaceReason, () => { duplicate.Place(); presentation?.RequestRefresh(); }),
                Slot(DuplicateState.CancelLabel, DuplicateState.CancelTip, null, CancelDuplicate, "Cancel Duplicate") }, null);
        }
        // One frame of Duplicate: ends it when its window is gone, and while placing gives the frame's world click to
        // the state. A placed mod object changed the scene: the inventory reads it again and then inspects the object.
        void TickDuplicate(bool open) {
            if (!Duplicating) return;
            var source = duplicate.Source?.Handle as GameObject;
            if (!open || attachment == null || FileLocked || presentation.Compact || source == null) { EndDuplicate(); return; }
            if (!PlacingModObject) return;
            placingPointer.Click = inputBlocker?.WorldClick;
            if (placingPointer.Click == null) return;
            var message = duplicate.Message;
            TargetIdentity placed = null;
            try { placed = duplicate.Tick(placingPointer, (from, placement) => attachment.Session.PlaceModObject(from, placement)); }
            catch (Exception e) { duplicate.End(); feedback.Report(DuplicatePlace, "The object couldn't be placed: " + e.GetBaseException().Message.TrimEnd('.') + "."); }
            if (placed != null) {
                placedModObject = placed;
                feedback.Report(DuplicatePlace, DuplicateState.Placed);
                ModObjectsChanged();
            }
            if (placed != null || !PlacingModObject || duplicate.Message != message) presentation?.RequestRefresh();
        }
        // A mod object was placed, removed or put back: the scene holds other objects than the snapshot, so the
        // inventory reads the scene again, after the frames the game takes to create or destroy them.
        void ModObjectsChanged() {
            OwnChange();
            fullRefresh = true; refreshTarget = null; refreshFrame = Time.frameCount + 2;
            inventory?.CancelFreshnessCheck();
        }
        // After the refresh that followed a placing click: the new mod object is inspected, with all four tabs.
        void InspectPlacedModObject() {
            var placed = placedModObject; placedModObject = null;
            if (placed == null || inventory == null) return;
            var entry = inventory.Entries.FirstOrDefault(e => TargetIdentity.SameObject(e.Identity, placed) && (placed.Structure == null || e.Identity.Structure == placed.Structure));
            if (entry == null) return;
            ClearSnap();
            selected = entry;
            inspectorTab.Value = ObjectTab;
        }
        // The pointer mark while placing: a cross at the pointer with the instruction beside it.
        GUIStyle placingLabel;
        void DrawPlacingMark() {
            if (!PlacingModObject || presentation == null || !presentation.IsOpen || Event.current.type != EventType.Repaint) return;
            var pointer = Event.current.mousePosition;
            if (presentation.PanelBounds.Contains(pointer)) return;
            if (placingLabel == null) placingLabel = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            var before = GUI.color;
            GUI.color = new Color(1f, .82f, .2f, 1f);
            var white = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(pointer.x - 14, pointer.y - 1, 10, 2), white); GUI.DrawTexture(new Rect(pointer.x + 4, pointer.y - 1, 10, 2), white);
            GUI.DrawTexture(new Rect(pointer.x - 1, pointer.y - 14, 2, 10), white); GUI.DrawTexture(new Rect(pointer.x - 1, pointer.y + 4, 2, 10), white);
            var size = placingLabel.CalcSize(new GUIContent(DuplicateState.PointerInstruction));
            var box = new Rect(pointer.x + 18, pointer.y + 10, size.x + 12, size.y + 4);
            GUI.color = new Color(.106f, .098f, .086f, .92f);
            GUI.DrawTexture(box, white);
            GUI.color = before;
            GUI.Label(new Rect(box.x + 6, box.y + 2, size.x, size.y), DuplicateState.PointerInstruction, placingLabel);
        }
    }
}
