// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The reason the outliner area states instead of an empty list: on a map where the patcher lists no scene object
// because the game creates every scene object it checked while it runs. Any other empty or narrowed list keeps the
// count line and states no reason.
static class EmptyListReasonTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Empty list reason: " + message); }

    const string Runtime = BakedSceneryRules.RuntimeObject;
    // A scene that supplies its listed objects and, for every scene object it checked, null when it lists it or the
    // reason it leaves it out.
    sealed class Scene : IInspectionScene, ICheckedObjectsScene {
        public readonly List<SceneObservation> Objects = new List<SceneObservation>();
        public readonly List<string> Checks = new List<string>();
        public IEnumerable<string> MapIds => new[] { "map" };
        public IEnumerable<SceneObservation> Discover() => Objects;
        public IEnumerable<string> CheckedObjects() => Checks;
    }
    static SceneObservation Entry(string path) =>
        new SceneObservation(new object(), new TargetIdentity("scene", path, "UnityEngine.Transform"), BakedSceneryRules.Family, true, new double[] { 0, 0, 0 }, new double[] { 0, 0, 0, 1 },
            new string[0], new string[0], new string[0], "Placement", category: BakedSceneryCategories.Unclassified);
    static SceneInventory Inventory(Scene scene) { var inventory = new SceneInventory(scene); inventory.Refresh(); return inventory; }

    internal static void Run() {
        // Everett: every checked scene object is created by the game while it runs, and none is listed.
        var everett = new Scene();
        everett.Checks.AddRange(Enumerable.Repeat(Runtime, 465));
        var everettInventory = Inventory(everett);
        Check(everettInventory.CheckedCount == 465 && everettInventory.UnlistedReasons.Count == 465 && everettInventory.UnlistedReasons.All(r => r == Runtime),
            "the inventory carries how many scene objects were checked and why each unlisted one is left out");
        var reason = InventoryPresentationState.EmptyListReason(everettInventory);
        Check(reason == "This map has no objects the patcher can edit. While the map is running, the game creates all the scene objects the patcher checks for editing.",
            "an inventory whose checked objects are all created by the game while it runs states the reason (got " + (reason ?? "null") + ")");

        // One listed object: the list is not empty, so no reason.
        var oneListed = new Scene();
        oneListed.Objects.Add(Entry("World/Large Scenery/Town/Shed"));
        oneListed.Checks.Add(null);
        oneListed.Checks.AddRange(Enumerable.Repeat(Runtime, 20));
        Check(InventoryPresentationState.EmptyListReason(Inventory(oneListed)) == null, "an inventory with one listed object states no reason");

        // No listed object, and one checked object is left out for another reason: the current empty list stays.
        var otherReason = new Scene();
        otherReason.Checks.AddRange(Enumerable.Repeat(Runtime, 20));
        otherReason.Checks.Add(BakedSceneryRules.MapLabel);
        var otherInventory = Inventory(otherReason);
        Check(otherInventory.CheckedCount == 21 && otherInventory.UnlistedReasons.Count == 21, "the inventory counts the object left out for another reason");
        Check(InventoryPresentationState.EmptyListReason(otherInventory) == null, "an inventory whose unlisted objects have another reason states no reason");

        // A scene that checked nothing, or that does not report its checks, states no reason either.
        Check(InventoryPresentationState.EmptyListReason(Inventory(new Scene())) == null, "an inventory that checked no scene object states no reason");
        Check(InventoryPresentationState.EmptyListReason(null) == null, "no inventory states no reason");

        // A search or a filter that matches nothing on a map with listed objects keeps the count line "0 of N objects".
        var listed = new Scene();
        listed.Objects.Add(Entry("World/Large Scenery/Town/Shed"));
        listed.Objects.Add(Entry("World/Large Scenery/Town/Barn"));
        listed.Checks.AddRange(new string[] { null, null, Runtime });
        var listedInventory = Inventory(listed);
        OutlinerGrouping.InvalidatePlaces();
        OutlinerGrouping.LogPending = false;
        foreach (var (search, filter) in new[] { ("no such object", OutlinerFilter.All), ("", OutlinerFilter.Problems) }) {
            var rows = OutlinerGrouping.Rows(listedInventory, search, filter, null, null);
            var tree = OutlinerGrouping.Build(rows, listedInventory.Entries, search, true, null);
            Check(rows.Count == 0 && OutlinerGrouping.Summary(tree, listedInventory.Entries) == "0 of 2 objects", "an empty search or filter result keeps its count line (got " + OutlinerGrouping.Summary(tree, listedInventory.Entries) + ")");
            Check(InventoryPresentationState.EmptyListReason(listedInventory) == null, "an empty search or filter result states no reason");
        }
    }
}
