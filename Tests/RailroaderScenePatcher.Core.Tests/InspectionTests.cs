// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

static class InspectionTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class Fixture : IInspectionScene {
        public IEnumerable<string> MapIds => new[] { "map", "layer" };
        public readonly List<SceneObservation> Objects = new List<SceneObservation>();
        public IEnumerable<SceneObservation> Discover() => Objects;
    }
    internal static void Run() {
        var fixture = new Fixture();
        fixture.Objects.Add(new SceneObservation(new object(), new TargetIdentity("scene", "World/Board", "Culler"), "Independent board", false,
            new double[] { 12, 2, 3 }, new double[] { 0, 0, 0, 1 }, new string[0], new[] { "Culling registration", "Terrain mask" }, new string[0], "Inspection only"));
        fixture.Objects.Add(new SceneObservation(new object(), new TargetIdentity("scene", "World/Sign", "Marker"), "Track marker", true,
            new double[] { 14, 2, 3 }, new double[] { 0, 0, 0, 1 }, new[] { "Segment missing-segment: missing" }, new[] { "Track-relative placement" }, new[] { "Broken binding" }, "Inspection only"));
        var inventory = new SceneInventory(fixture);
        inventory.Refresh();
        var board = inventory.Search("board").Single();
        Check(!board.Active && board.Bindings.Count == 0 && board.Dependencies.Contains("Terrain mask"), "Inactive independent objects without patches retain their dependencies");
        Check(inventory.Search("missing-segment").Single().Problems.Contains("Broken binding"), "Search includes broken-binding identifiers and detected problems");
        Check(inventory.Entries.Count == 2 && !inventory.IsStale, "Refresh observes all supplied objects without making them active");
        var originalBoard = fixture.Objects[0];
        RefreshPresentation(fixture, inventory, originalBoard);
        fixture.Objects[0] = new SceneObservation(originalBoard.Handle, originalBoard.Identity, originalBoard.Family, true,
            originalBoard.Position, originalBoard.Rotation, originalBoard.Bindings, originalBoard.Dependencies, originalBoard.Problems, originalBoard.Operations);
        inventory.CheckFreshness();
        Check(inventory.IsStale && !board.Active, "Scene changes mark the old snapshot stale without silently replacing observations");
        var changedBoard = fixture.Objects[0];
        inventory.Refresh();
        var refreshedBoard = inventory.Search("board").Single();
        Check(!inventory.IsStale && refreshedBoard.Active && !board.Active && refreshedBoard.Position.SequenceEqual(new double[] { 12, 2, 3 }),
            "Explicit refresh replaces the inactive observation with the changed active value and clears stale state");
        Check(ReferenceEquals(fixture.Objects[0], changedBoard) && fixture.Objects[0].Active && fixture.Objects[0].Position.SequenceEqual(new double[] { 12, 2, 3 }) &&
            fixture.Objects[1].Active && fixture.Objects[1].Bindings.Single() == "Segment missing-segment: missing",
            "Refresh leaves source activity, placement and the independent marker binding unchanged");
        fixture.Objects[0] = originalBoard;
        inventory.CheckFreshness();
        fixture.Objects[0] = changedBoard;
        inventory.CheckFreshness();
        Check(inventory.IsStale, "Staleness remains visible until explicit refresh");
        inventory.Refresh();
        fixture.Objects.Add(new SceneObservation(new object(), originalBoard.Identity, originalBoard.Family, false,
            originalBoard.Position, originalBoard.Rotation, originalBoard.Bindings, originalBoard.Dependencies, originalBoard.Problems, originalBoard.Operations));
        inventory.CheckFreshness();
        Check(inventory.IsStale, "New or ambiguous targets invalidate the snapshot");
        var mutable = new Scene(); var oldTarget = new Box { X = 7 }; var replacement = new Box { X = 9 };
        mutable.Objects["p"] = new List<object> { oldTarget };
        mutable.BeforeCapture = () => mutable.Objects["p"] = new List<object> { replacement };
        var document = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"move\",\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}]}";
        var result = new PatchEngine(mutable).Apply(document);
        Check(!result.Outcomes["move"].Applied && mutable.MutationCalls == 0 && oldTarget.X == 7 && replacement.X == 9,
            "A replaced target between capture and mutation is rejected without moving either object");
        mutable.BeforeCapture = null; mutable.Objects["p"] = new List<object> { oldTarget };
        mutable.Observations = () => new[] { new SceneObservation(oldTarget, new TargetIdentity("scene", "p", "Culler"), "Board", true,
            oldTarget.Position, oldTarget.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var mutationInventory = new SceneInventory(mutable); mutationInventory.Refresh();
        var expected = mutationInventory.Pin(mutationInventory.Entries.Single());
        oldTarget.X = 8;
        result = new PatchEngine(mutable).Apply(document, "pack", new Dictionary<string, InspectedTarget> { ["move"] = expected });
        Check(!result.Outcomes["move"].Applied && mutable.MutationCalls == 0 && oldTarget.X == 8, "Changed placement since inspection rejects the complete-document edit");
        mutationInventory.Refresh(); expected = mutationInventory.Pin(mutationInventory.Entries.Single());
        result = new PatchEngine(mutable).Apply(document, "pack", new Dictionary<string, InspectedTarget> { ["move"] = expected });
        Check(result.Outcomes["move"].Applied && oldTarget.X == 12, "Fresh inspected targets can apply the intended complete document");
        mutationInventory.Refresh();
        var definition = mutationInventory.InspectDocument(document.Replace("\"dependsOn\":[]", "\"dependsOn\":[\"missing-edit\"]")).Single();
        Check(definition.MatchCount == 1 && definition.Problems.Any(p => p.Contains("missing-edit")), "Complete-document inspection reports missing dependencies without applying placement");
        definition = mutationInventory.InspectDocument(document.Replace("\"path\":\"p\"", "\"path\":\"missing-object\"")).Single();
        Check(definition.MatchCount == 0 && definition.Problems.Any(p => p.Contains("Target")), "A missing target leaves its referring definition inspectable");
        Check(mutationInventory.InspectDocument("{broken").Single().Problems.Any(p => p.Contains("Unreadable")) && mutable.MutationCalls == 1, "Invalid supplied documents remain visible without mutation or rewrite");
        var mutationCount = mutable.MutationCalls;
        foreach (var change in new Action[] {
            () => mutable.Objects["p"] = new List<object>(),
            () => mutable.Objects["p"] = new List<object> { oldTarget, replacement },
            () => mutable.Maps = new[] { "another-map" }
        }) {
            mutable.Maps = new[] { "map", "layer" }; mutable.Objects["p"] = new List<object> { oldTarget };
            mutable.BeforeCapture = change;
            result = new PatchEngine(mutable).Apply(document);
            Check(!result.Outcomes["move"].Applied && mutable.MutationCalls == mutationCount, "Missing, ambiguous and changed-map targets cannot mutate after capture");
        }
        mutable.BeforeCapture = null; mutable.Maps = new[] { "map", "layer" }; mutable.Objects["p"] = new List<object> { oldTarget };
        var bindings = new[] { "segment=original" };
        mutable.Observations = () => new[] { new SceneObservation(oldTarget, new TargetIdentity("scene", "p", "Culler"), "Board", true,
            oldTarget.Position, oldTarget.Rotation, bindings, new string[0], new string[0], "Fixed placement") };
        mutationInventory.Refresh(); expected = mutationInventory.Pin(mutationInventory.Entries.Single());
        bindings[0] = "segment=rebound";
        result = new PatchEngine(mutable).Apply(document, "pack", new Dictionary<string, InspectedTarget> { ["move"] = expected });
        Check(!result.Outcomes["move"].Applied && mutable.MutationCalls == mutationCount, "Changed binding after inspection cannot authorize a placement edit");
        Console.WriteLine("PASS: inventory classification and dependencies");
    }

    static void RefreshPresentation(Fixture fixture, SceneInventory inventory, SceneObservation original) {
        var presentation = new RailroaderScenePatcher.InventoryPresentationState();
        var selected = original;
        Check(presentation.Observe(inventory, selected, "board"), "Initial inventory presentation needs rows");
        Check(!presentation.Observe(inventory, selected, "board"), "Unchanged presentation does not repeatedly rebuild");
        fixture.Objects[0] = new SceneObservation(original.Handle, original.Identity, original.Family, original.Active,
            original.Position, original.Rotation, original.Bindings, original.Dependencies, original.Problems, original.Operations);
        inventory.Refresh();
        selected = presentation.CurrentSelection(inventory, selected);
        Check(presentation.Observe(inventory, selected, "board"),
            "Refresh with identical visible values must replace row callbacks and current selection");
        Check(ReferenceEquals(selected, inventory.Entries[0]), "Refreshed selection is owned by the current inventory");
        var refreshed = selected;
        Action oldRowClick = () => { if (presentation.CanSelect(inventory, original)) selected = original; };
        oldRowClick();
        Check(ReferenceEquals(selected, refreshed), "A deferred callback from the previous inventory cannot restore an old observation");
        Check(presentation.CanSelect(inventory, refreshed) && inventory.Pin(refreshed) != null,
            "A normal refreshed row can select and pin the current observation");
        bool refused = false;
        try { inventory.Pin(original); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Pin still refuses the previous inventory observation even with identical visible values");
        Check(presentation.Observe(inventory, null, "board"), "Clearing selection invalidates the native selected row");
        Check(presentation.Observe(inventory, null, "marker"), "Changed filter invalidates the cached outline");
        Check(!presentation.Observe(inventory, null, "marker"), "A settled filtered presentation does not repeatedly rebuild");
        inventory.Refresh();
        Check(presentation.Observe(inventory, null, "marker"), "Refresh invalidates rows even without an inspected selection");
        var current = inventory.Entries[0];
        fixture.Objects[0] = new SceneObservation(new object(), original.Identity, original.Family, original.Active,
            original.Position, original.Rotation, original.Bindings, original.Dependencies, original.Problems, original.Operations);
        inventory.CheckFreshness();
        Check(!presentation.CanSelect(inventory, current), "A replaced native target cannot be selected from stale rows");
        refused = false;
        try { inventory.Pin(current); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Pin still refuses the replaced native target");
        inventory.Refresh();
        Check(presentation.CurrentSelection(inventory, current) == null, "Refresh does not transfer inspection to a replacement with the same identity");
        fixture.Objects[0] = original;
        inventory.Refresh();
    }
}
