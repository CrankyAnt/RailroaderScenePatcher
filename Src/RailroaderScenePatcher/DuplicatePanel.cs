// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using TMPro;
using UI.Builder;
using UI.Common;
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
        readonly PlacingGhost placingGhost = new PlacingGhost();
        bool Duplicating => duplicate.Open;
        bool PlacingModObject => duplicate.Step == DuplicateStep.Placing;
        // Why the window's other controls are unavailable now, or null: the file panel's reason, or the open Duplicate.
        string WindowLockReason => FileLockReason ?? (Duplicating ? DuplicateState.LockReason : null);
        // The mod object the last placing click made: it is inspected once the inventory lists it.
        TargetIdentity placedModObject;

        // The pointer while placing: where it is over the world, the world click the input blocker confirmed this
        // frame, and the ground under a screen point. Both points are in GUI coordinates.
        sealed class PlacingPointer : IPlacingPointer {
            internal Vector2? At, Click;
            public double[] Pointer => At is Vector2 point ? new double[] { point.x, point.y } : null;
            public double[] WorldClick => Click is Vector2 point ? new double[] { point.x, point.y } : null;
            public double[] MapPoint(double[] click) {
                var camera = Camera.main;
                var terrain = LayerMask.NameToLayer("Terrain");
                if (camera == null || terrain < 0) return null;
                // The point is in GUI coordinates, which run down from the top of the screen.
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
            var subject = selected ?? CrossingNodeSource();
            if (subject == null) return "Inspect an object first.";
            string rule;
            try { rule = DuplicateState.Reason(subject, FamiliesBelow(subject)); }
            catch (Exception e) { rule = "The object couldn't be read: " + e.GetBaseException().Message.TrimEnd('.') + "."; }
            // The source's own existence counts, not the inspected row's: a crossing node has none (CrossingNodeSource).
            return DuplicateState.Inactive(rule, attachment?.Session.Blocked, EditingBlock(), Alive(subject), Requests.Pending);
        }
        // The crossing object of the crossing node the outliner has selected, as the source of Duplicate, or null. A
        // crossing without signals has no row of its own: its node stands for the object its crossbucks and pads stand in.
        SceneObservation CrossingNodeSource() {
            var node = selected == null ? crossingNode : null;
            if (node?.Identity == null || !(node.Member?.Handle is GameObject member) || member == null || member.transform.parent == null) return null;
            var crossing = member.transform.parent.gameObject;
            var at = NativeInventory.MapPosition(crossing.transform); var turn = crossing.transform.rotation;
            return new SceneObservation(crossing, node.Identity, CrossingObjectScene.Family, crossing.activeInHierarchy, new double[] { at.x, at.y, at.z }, new double[] { turn.x, turn.y, turn.z, turn.w },
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, displayName: node.Label);
        }
        // The selected crossing node as the world marks it while no row is inspected: its crossing object, framed around
        // the node's own crossbucks and pads in gold, as an inspected object is (#38's complex frame), with its marker at
        // the middle of those members, not at the crossing object that crossings at one path share. Its pads are framed
        // whole. Built once per selected node and asked for on every repaint; null without a node.
        SpatialCandidate crossingNodeFrame;
        OutlinerGrouping.GroupNode crossingNodeFramed;
        SpatialCandidate CrossingNodeFrame() {
            var node = selected == null ? crossingNode : null;
            if (node == null) return null;
            if (ReferenceEquals(node, crossingNodeFramed) && crossingNodeFrame?.Choice.Handle is GameObject held && held != null) return crossingNodeFrame;
            crossingNodeFramed = node; crossingNodeFrame = null;
            var crossing = CrossingNodeSource();
            if (crossing == null || node.Point == null || node.Point.Length != 3) return null;
            GameObject[] Handles(Func<SceneObservation, bool> which) => node.Members.Where(which).Select(m => m.Handle as GameObject).Where(go => go != null).ToArray();
            var observed = SpatialSelectionScene.Observe(crossing);
            return crossingNodeFrame = new SpatialCandidate(observed.Choice, new[] { new Vector3((float)node.Point[0], (float)node.Point[1], (float)node.Point[2]) }, observed.Details, crossing,
                parts: Handles(m => true), uncutParts: Handles(m => m.Family == SceneFamilies.CrossingPad));
        }
        // The label of the selected crossing node while no row is inspected, which the inspector heading shows, or null.
        string CrossingNodeLabel() => selected == null && crossingNode?.Identity != null ? crossingNode.Label : null;
        // The inspector heading while no row is inspected: the selected crossing node's label with its crossing object's
        // path in the tooltip, or No Selection.
        RectTransform EmptyHeading(UIPanelBuilder builder) {
            var label = CrossingNodeLabel();
            return Heading(builder, Literal(DuplicateState.EmptyHeading(label)), "Object", DuplicateState.EmptyHeadingTooltip(label, crossingNode?.Identity?.Path));
        }
        // The selected crossing node in the inspector: the crossing's name, Duplicate, and Remove modification for a
        // crossing this mod made.
        void DrawCrossingNode(UIPanelBuilder builder) {
            var node = crossingNode; var identity = node?.Identity;
            if (identity == null) return;
            Field(builder, DuplicateState.CrossingField, node.Label);
            var made = false;
            try { made = attachment != null && attachment.Session.IsModObject(identity); } catch { }
            var remove = !made ? DuplicateState.NotThisMods : attachment.Session.Blocked != null ? attachment.Session.Blocked + " The recovery choices are in the session details." : Requests.Pending ? "Wait for the scene check to finish." : null;
            builder.HStack(row => {
                row.ConfigureLayout(4f);
                ActButton(row, DuplicateAct());
                ActButton(row, Slot(CommandBand.RemoveModification, DuplicateState.RemoveTip, remove, () => RemoveCrossing(identity)));
            });
        }
        void RemoveCrossing(TargetIdentity identity) {
            string refused = null;
            try { var outcome = attachment.Session.Remove(identity, null); if (outcome == null || !outcome.Applied) refused = outcome?.Error ?? "The crossing could not be removed."; }
            catch (Exception e) { refused = PatcherLog.Reason(e); }
            if (refused != null) feedback.Report(DuplicatePlace, "Couldn't remove the crossing. " + refused.TrimEnd('.') + ".");
            else { crossingNodeKey = null; crossingNode = null; feedback.Report(DuplicatePlace, DuplicateState.Removed); ModObjectsChanged(); }
            presentation?.RequestRefresh();
        }
        Act DuplicateAct() => Slot(DuplicateState.Label, DuplicateState.Tip, DuplicateReason(), BeginDuplicate);
        void BeginDuplicate() {
            var subject = selected ?? CrossingNodeSource();
            if (subject == null || Duplicating || DuplicateReason() != null) return;
            CancelGizmo(); ClearSnap(); CloseCoverageForm(); CloseEquipmentForm(); EndWorldEditAction();
            duplicate.Begin(subject);
            presentation?.RequestRefresh();
        }
        // Cancel: the inspected object is still the source, so the window shows it again.
        void CancelDuplicate() {
            duplicate.Cancel();
            presentation?.RequestRefresh();
        }
        // A selection in the outliner while the mod object is unplaced: the game's own question asks whether to cancel
        // the duplicate. Keep duplicating, its Cancel and Escape keep the blank window; Cancel Duplicate ends it and
        // then runs the selection. Returns false when no duplicate is open, so the caller selects at once.
        bool AskBeforeOutlinerSelection(object target, string name, Action select) {
            if (!duplicate.AskBeforeSelecting(target)) return false;
            presentation?.RequestRefresh();
            var source = duplicate.Source;
            var answered = false;
            void Choose(bool cancel, Action dismiss) {
                if (answered) return;
                answered = true;
                dismiss();
                // A duplicate that ended meanwhile, by closing the window or a reload, has nothing left to keep.
                if (!ReferenceEquals(duplicate.Selecting, target)) return;
                if (!cancel) { duplicate.KeepDuplicate(); presentation?.RequestRefresh(); return; }
                duplicate.CancelForSelection();
                presentation?.RequestRefresh();
                select();
            }
            try {
                ModalAlertController.Present((builder, dismiss) => {
                    builder.ConfigureLayout(16f);
                    builder.AddLabel(DuplicateState.SelectTitle, text => { text.fontSize = 22f; text.horizontalAlignment = HorizontalAlignmentOptions.Center; });
                    builder.AddLabel(DuplicateState.SelectQuestion(source, name), text => { text.fontSize = 18f; text.horizontalAlignment = HorizontalAlignmentOptions.Center; });
                    builder.AlertButtons(buttons => {
                        buttons.AddButtonMedium(DuplicateState.KeepChoice, () => Choose(false, dismiss));
                        buttons.AddButtonMedium(DuplicateState.CancelChoice, () => Choose(true, dismiss));
                    });
                }, escapeDismisses: true, width: 600, onCancel: dismiss => Choose(false, dismiss));
            } catch (Exception e) {
                duplicate.KeepDuplicate();
                feedback.Report(DuplicatePlace, "Couldn't ask whether to cancel Duplicate. " + e.GetBaseException().Message.TrimEnd('.') + ". Click Cancel to discard the unplaced mod object.");
            }
            return true;
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
            // While placing, Release takes Place's slot: it takes the ghost off the pointer, as Esc does.
            ActionRow(builder, new[] {
                PlacingModObject
                    ? Slot(DuplicateState.ReleaseLabel, DuplicateState.ReleaseTip, null, () => { duplicate.Release(); presentation?.RequestRefresh(); })
                    : Slot(DuplicateState.PlaceLabel, DuplicateState.PlaceTip, null, () => { duplicate.Place(); presentation?.RequestRefresh(); }),
                Slot(DuplicateState.CancelLabel, DuplicateState.CancelTip, null, CancelDuplicate, "Cancel Duplicate") }, null);
        }
        // One frame of Duplicate: ends it when its window is gone, and while placing gives the frame's world click to
        // the state. A placed mod object changed the scene: the inventory reads it again and then inspects the object.
        // The ghost follows the pointer in every frame while placing; over the window, or where no ground is, it hides.
        void TickDuplicate(bool open) {
            if (!Duplicating) { placingGhost.Clear(); return; }
            var source = duplicate.Source?.Handle as GameObject;
            if (!open || attachment == null || FileLocked || presentation.Compact || source == null) { EndDuplicate(); placingGhost.Clear(); return; }
            if (!PlacingModObject || Requests.Busy) { placingGhost.Hide(); return; }
            var mouse = Input.mousePosition;
            var pointer = new Vector2(mouse.x, Screen.height - mouse.y);
            placingPointer.At = presentation.PanelBounds.Contains(pointer) ? (Vector2?)null : pointer;
            placingPointer.Click = inputBlocker?.WorldClick;
            var message = duplicate.Message;
            TargetIdentity placed = null;
            try {
                placed = duplicate.Tick(placingPointer, (from, placement) =>
                    TrackEditTerrain(() => attachment.Session.PlaceModObject(from.Identity, placement, null, from.GroupPath, markerIdRandom.Next)));
            }
            catch (Exception e) { duplicate.End(); feedback.Report(DuplicatePlace, "The object couldn't be placed: " + PatcherLog.Reason(e).TrimEnd('.') + "."); }
            if (placed != null) {
                placedModObject = placed;
                feedback.Report(DuplicatePlace, DuplicateState.Placed);
                ModObjectsChanged();
            }
            // A station map icon has no mesh in the world: its ghost is the map's icon, drawn with the pointer mark.
            if (PlacingModObject && duplicate.Ghost != null && !StationMapIconRules.ShowsMapIcon(duplicate.Source)) {
                var turn = duplicate.Source.Rotation;
                try { placingGhost.Show(source, duplicate.Ghost, new Quaternion((float)turn[0], (float)turn[1], (float)turn[2], (float)turn[3])); }
                catch (Exception e) { placingGhost.Clear(); PatcherLog.Debug("The placing ghost is unavailable: " + e.GetBaseException().Message); }
            }
            else placingGhost.Hide();
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
            // A crossing without signals has no row: the outliner selects the node of the new crossing.
            if (entry == null && placed.Structure == CrossingObjectScene.Structure) crossingNodeKey = OutlinerGrouping.CrossingNodeKey(placed.Scene, placed.Path, null);
            if (entry == null) return;
            ClearSnap();
            selected = entry;
            inspectorTab.Value = ObjectTab;
        }
        // The pointer mark while placing: a cross at the pointer with the instruction beside it.
        // The label style of the pointer instruction for each font size it was drawn or measured at: one line without
        // wrapping or padding, so the label rectangle is the measured text, rounded up, and no word moves to a second
        // line that the box would cut off. Each size has a style of its own, so measuring changes no style in use.
        readonly Dictionary<int, GUIStyle> placingLabels = new Dictionary<int, GUIStyle>();
        GUIStyle PlacingLabel(int fontSize) {
            if (!placingLabels.TryGetValue(fontSize, out var style))
                placingLabels[fontSize] = style = new GUIStyle(GUI.skin.label) {
                    fontSize = fontSize, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow,
                    padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), normal = { textColor = Color.white } };
            return style;
        }
        static Rect Of(double[] r) => new Rect((float)r[0], (float)r[1], (float)r[2], (float)r[3]);
        // The game's UI scale, by which its canvas and its pointer callout are drawn; 1 where it can't be read.
        static float UiScale() {
            try { return Game.Settings.CanvasSettingsApplicator.EffectiveCanvasScale; } catch (Exception) { return 1f; }
        }
        void DrawPlacingMark() {
            if (!PlacingModObject || presentation == null || !presentation.IsOpen || Event.current.type != EventType.Repaint) return;
            var pointer = Event.current.mousePosition;
            if (presentation.PanelBounds.Contains(pointer)) return;
            if (StationMapIconRules.ShowsMapIcon(duplicate.Source) && duplicate.Ghost is double[] ghost && duplicate.Source.Handle is GameObject icon && icon != null)
                WorldMapIcon.Draw(icon, NativeInventory.ScenePosition(new Vector3((float)ghost[0], (float)ghost[1], (float)ghost[2]), NativeInventory.MovesWithOrigin(icon.transform)), null);
            var content = new GUIContent(DuplicateState.PointerInstruction);
            var mark = DuplicateState.PointerMark(new double[] { pointer.x, pointer.y }, UiScale(), size => {
                var measured = PlacingLabel(size).CalcSize(content);
                return (measured.x, measured.y);
            }, Screen.width, Screen.height);
            var before = GUI.color;
            GUI.color = new Color(1f, .82f, .2f, 1f);
            var white = Texture2D.whiteTexture;
            float gap = (float)mark.CrossGap, arm = (float)mark.CrossArm, thick = (float)mark.CrossThickness;
            GUI.DrawTexture(new Rect(pointer.x - gap - arm, pointer.y - thick / 2, arm, thick), white); GUI.DrawTexture(new Rect(pointer.x + gap, pointer.y - thick / 2, arm, thick), white);
            GUI.DrawTexture(new Rect(pointer.x - thick / 2, pointer.y - gap - arm, thick, arm), white); GUI.DrawTexture(new Rect(pointer.x - thick / 2, pointer.y + gap, thick, arm), white);
            GUI.color = new Color(.106f, .098f, .086f, .92f);
            GUI.DrawTexture(Of(mark.Box), white);
            GUI.color = before;
            GUI.Label(Of(mark.Label), content, PlacingLabel(mark.FontSize));
        }
    }
}
