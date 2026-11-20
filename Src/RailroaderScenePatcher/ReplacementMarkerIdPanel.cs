// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using UI.Builder;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The replacement ID of a track marker, which repairs a duplicate marker ID: the Marker ID section of the
    // Connections tab, the other scene markers that share the ID, and the repair offered with the duplicate explanation
    // on the Object tab.
    public sealed partial class PatcherBehaviour {
        const string MarkerIdPlace = "Marker ID";
        // The replacement ID field of the inspected marker.
        readonly string[] markerIdFields = { "" };
        bool IsGenericMarker => selected?.Identity.Structure == TrackMarkerScene.Structure;
        // The ID the inspected marker holds now: read from the scene for a generic track marker, otherwise from the
        // inventory's binding line of the marker, so a marker that takes no replacement ID still shows its ID. Empty for
        // any other object.
        const string MarkerIdLine = "Marker id=";
        string CurrentMarkerId() {
            if (selected == null) return "";
            try { var read = attachment?.Session.MarkerIdOf(selected.Identity); if (read != null) return read; } catch { }
            var line = selected.Bindings.FirstOrDefault(b => b.StartsWith(MarkerIdLine, StringComparison.Ordinal));
            if (line == null) return "";
            var value = line.Substring(MarkerIdLine.Length);
            var end = value.IndexOf(';');
            return (end < 0 ? value : value.Substring(0, end)).Trim();
        }
        // The underlying ID of the inspected generic marker and the other scene markers that use it, read once per frame:
        // the users are a scan of every loaded marker and of the track graph, and one panel build asks several times.
        sealed class SharerReading { internal object Handle; internal int Frame; internal string Underlying; internal IReadOnlyList<string> Sharers; }
        SharerReading sharerReading;
        SharerReading ReadSharers() {
            var handle = selected?.Handle;
            if (sharerReading != null && ReferenceEquals(sharerReading.Handle, handle) && sharerReading.Frame == Time.frameCount) return sharerReading;
            var reading = new SharerReading { Handle = handle, Frame = Time.frameCount, Sharers = Array.Empty<string>() };
            if (IsGenericMarker && attachment != null) {
                try {
                    reading.Underlying = attachment.Session.UnderlyingMarkerId(selected.Identity);
                    if (reading.Underlying != null) reading.Sharers = attachment.Session.MarkerIdUsers(selected.Identity, reading.Underlying);
                } catch { }
            }
            return sharerReading = reading;
        }
        bool HasMarkerIdValue(TargetIdentity identity) {
            try { return attachment != null && identity != null && attachment.Session.HasMarkerId(identity); } catch { return false; }
        }
        // Why the replacement ID is unavailable for the inspected object now, or null when it is offered.
        string MarkerIdBlock() {
            var block = IsStarter ? StarterBlock() : EditBlock();
            if (block != null) return block;
            var reading = ReadSharers();
            return CommandBand.MarkerIdUnavailable(IsGenericMarker, reading.Underlying, reading.Sharers.Count, HasMarkerIdValue(selected?.Identity));
        }
        // The Marker ID section: the field with Set ID and Clear ID, then the scene markers that hold the underlying ID.
        // The section stays visible for every marker; for a marker that takes no replacement ID it is disabled with the reason.
        void DrawMarkerId(UIPanelBuilder builder) {
            var identity = selected.Identity;
            var reading = ReadSharers();
            var block = MarkerIdBlock();
            var current = CurrentMarkerId();
            Section(builder, "Marker ID");
            ValueRow(builder, "Replacement ID", null, new[] { "ID" }, markerIdFields, 0, block,
                "The ID the game registers this marker's track location under. A replacement ID repairs a duplicate marker ID: it must not be empty, have a space at either end, begin with passengerStop: or flare:, or match another scene marker or map object. Lua scripts and other mods that look up a marker by ID are not checked.", column => false, freeText: true);
            builder.HStack(row => {
                var set = Button(row, CommandBand.SetMarkerId, ApplyMarkerId);
                Func<string> setReason = () => CommandBand.MarkerIdSetReason(block, markerIdFields[0], current);
                set.Inactive(setReason() != null);
                var provider = set.RectTransform.GetComponent<UI.Tooltips.UITooltipProvider>() ?? set.RectTransform.gameObject.AddComponent<UI.Tooltips.UITooltipProvider>();
                provider.DynamicTooltipInfo = () => new TooltipInfo("Set marker ID", CommandBand.Tooltip("Give this marker the ID in the field. Its registration moves to the new ID; every other holder of the old ID keeps its registration.", setReason()));
                typed.Add(() => { if (set.RectTransform != null) set.Inactive(setReason() != null); });
                var clear = CommandBand.ClearMarkerIdReason(IsStarter ? StarterBlock() : EditBlock(), HasMarkerIdValue(identity));
                Button(row, CommandBand.ClearMarkerId, () => Edit(MarkerIdPlace, "Clearing the marker ID value", (edit, inspection) => edit.SetMarkerId(identity, null, inspection)))
                    .Inactive(clear != null).Tooltip("Clear marker ID", CommandBand.Tooltip("Clear this mod's replacement ID; the marker returns to its underlying ID.", clear));
                row.Spacer();
            }).ChildrenKeepHeight().Height(RowHeight);
            if (reading.Underlying != null && reading.Sharers.Count != 0) {
                FieldLabel(builder, CommandBand.SharersCaption(reading.Underlying, current));
                DrawMarkerIdSharers(builder, reading.Sharers);
            }
        }
        // One line per other scene marker that holds the ID, with Select, which inspects it so that it can receive the
        // replacement ID instead. A registration that no listed object owns has no Select.
        void DrawMarkerIdSharers(UIPanelBuilder builder, IReadOnlyList<string> sharers) {
            foreach (var name in sharers) {
                var entry = SharerEntry(name);
                builder.HStack(row => {
                    // The name gives way, ending in an ellipsis, so that Select stays clear of the scroll bar.
                    Line(row, Literal(name), "Scene marker", name, RowHeight).EqualFlexibleWidth(0);
                    Button(row, "Select", () => SelectSharer(name)).Inactive(entry == null)
                        .Tooltip("Select scene marker", CommandBand.Tooltip("Inspect this scene marker. Its Connections tab can give it the replacement ID instead.",
                            entry != null ? null : name == ReplacementMarkerIds.ForeignOwner ? "this registration belongs to an object that is not a scene marker." : "the object list does not include this scene marker; click Refresh."));
                }).ChildrenKeepHeight().Height(RowHeight);
            }
        }
        // The object-list entry of a scene marker named by scene and path, or null.
        SceneObservation SharerEntry(string name) =>
            inventory?.Entries.FirstOrDefault(e => e.Identity.Component == TrackMarkerWorld.MarkerType && e.Identity.Scene + "/" + e.Identity.Path == name);
        // Inspects another holder as Select group and Select controller inspect their object: what the inspection was
        // doing ends first.
        void SelectSharer(string name) {
            var entry = SharerEntry(name);
            if (entry == null || !inventoryPresentation.CanSelect(inventory, entry)) return;
            CancelGizmo(); ClearSnap(); coverageCircuit = coverageAddition = null; CloseEquipmentForm();
            selected = inventoryPresentation.CurrentSelection(inventory, entry); ReadPlacement();
            inspectorTab.Value = ConnectionsTab;
        }
        void ApplyMarkerId() {
            var identity = selected.Identity;
            var id = markerIdFields[0];
            var reason = CommandBand.MarkerIdSetReason(null, id, CurrentMarkerId());
            if (reason != null) { feedback.Refuse(MarkerIdPlace, "Set marker ID", reason); return; }
            Edit(MarkerIdPlace, "Set marker ID " + id, (edit, inspection) => edit.SetMarkerId(identity, id, inspection));
        }
        // The duplicate explanation of a holder that can receive a replacement ID offers the repair, active or not: for
        // the inspected marker through its guided task, and for each other scene marker that shares the ID through Select.
        void DrawDuplicateIdTasks(UIPanelBuilder builder) {
            if (!CommandBand.OffersDuplicateRepair(selected?.Identity.Structure, selected?.Dependencies)) return;
            var reason = GuidedBlock(GuidedTaskKind.MarkerId);
            Button(builder, CommandBand.RepairDuplicateId, () => StartGuided(GuidedTaskKind.MarkerId)).Inactive(reason != null)
                .Tooltip("Repair duplicate marker ID", CommandBand.Tooltip("Start a guided task that gives this marker a replacement ID.", reason));
            var sharers = ReadSharers().Sharers;
            if (sharers.Count == 0) return;
            FieldLabel(builder, "Or give the replacement ID to");
            DrawMarkerIdSharers(builder, sharers);
        }
    }
}
