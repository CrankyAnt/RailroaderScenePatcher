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
        SteppedFreshness();
        ComponentsLeavingDuringAPass();
        EditFeedbackNamesEveryRefusal();
        StaleNoticePromisesOnlyWhatPinRefuses();
        Console.WriteLine("PASS: inventory classification and dependencies");
    }

    // A scene whose discovery pauses between bounded units: one preparation unit and one observation per object.
    sealed class Stepped : ISteppedInspectionScene {
        public string[] Maps = { "map" };
        public IEnumerable<string> MapIds => Maps;
        public readonly List<SceneObservation> Objects = new List<SceneObservation>();
        public int Starts, Units, Ended;
        public Action<int> BeforeObservation;
        public IEnumerable<SceneObservation> Discover() => DiscoverInSteps().Where(o => o != null);
        public IEnumerable<SceneObservation> DiscoverInSteps() {
            Starts++;
            try {
                for (var i = 0; i < Objects.Count; i++) {
                    Units++; yield return null;
                    BeforeObservation?.Invoke(i);
                    Units++; yield return Objects[i];
                }
            } finally { Ended++; }
        }
    }
    static SceneObservation Thing(object handle, string path, bool active = true) => new SceneObservation(handle, new TargetIdentity("scene", path, "Marker"), "Track marker", active,
        new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new string[0], new string[0], new string[0], "Inspection only");
    static SceneObservation Changed(SceneObservation o) => Thing(o.Handle, o.Identity.Path, !o.Active);
    static bool Refused(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }

    // A loaded component of a controlled scene. Using it after it left the scene fails, as a destroyed native component does.
    sealed class Part {
        public string Name, Family; public bool Gone, Active = true; public Part Refers; public readonly object Handle = new object();
    }
    sealed class PartScene : ISteppedInspectionScene {
        public IEnumerable<string> MapIds => new[] { "map" };
        public readonly List<Part> Parts = new List<Part>();
        public readonly List<string> Reads = new List<string>();
        public Part FailingRead;
        Part Use(Part part, string what) {
            if (part.Gone) throw new InvalidOperationException("A component that left the scene was used: " + part.Name);
            Reads.Add(what + " " + part.Name); return part;
        }
        public IEnumerable<SceneObservation> Discover() => DiscoverInSteps().Where(o => o != null);
        public IEnumerable<SceneObservation> DiscoverInSteps() {
            var referrers = new Dictionary<Part, List<string>>();
            return DiscoveryWalk.Steps(() => Parts.ToArray(), part => !part.Gone,
                part => {
                    if (ReferenceEquals(part, FailingRead)) return false;
                    var target = Use(part, "read").Refers;
                    if (target != null) { if (!referrers.TryGetValue(target, out var refs)) referrers.Add(target, refs = new List<string>()); refs.Add("Referenced by " + part.Name); }
                    return true;
                },
                () => Reads.Add("reads done"), part => Use(part, "classify").Family, () => Reads.Add("classified"),
                (part, family) => new SceneObservation(Use(part, "observe").Handle, new TargetIdentity("scene", part.Name, "Component"), family, part.Active, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 },
                    new string[0], referrers.TryGetValue(part, out var found) ? found.ToArray() : new string[0], new string[0], "Inspection only"));
        }
    }
    // A paused pass meets components that left the scene since its search. A component no observation depends on, such as
    // a widget of a rebuilt panel, changes no observation and must not mark the snapshot stale; a component an observation
    // depends on must.
    static void ComponentsLeavingDuringAPass() {
        PartScene Parts(out SceneInventory inventory, out Part widget, out Part sign, out Part holder, out Part board) {
            var scene = new PartScene();
            scene.Parts.Add(widget = new Part { Name = "Widget" });
            scene.Parts.Add(sign = new Part { Name = "Sign", Family = "Track marker" });
            scene.Parts.Add(holder = new Part { Name = "Holder", Refers = sign });
            scene.Parts.Add(board = new Part { Name = "Board", Family = "Independent board" });
            inventory = new SceneInventory(scene); inventory.Refresh();
            return scene;
        }
        var one = new Func<bool>(() => false);
        var parts = Parts(out var checkedInventory, out var widget, out var sign, out var holder, out var board);
        Check(checkedInventory.Entries.Count == 2 && checkedInventory.Entries[0].Dependencies.Single() == "Referenced by Holder", "The controlled walk observes candidates with their referrers");
        var whole = parts.Reads.ToArray(); parts.Reads.Clear();
        for (var i = 0; i < 100 && (i == 0 || checkedInventory.CheckInProgress); i++) checkedInventory.AdvanceFreshnessCheck(one);
        Check(!checkedInventory.IsStale && parts.Reads.SequenceEqual(whole), "A pass taken one unit at a time reads the scene in the order of one discovery: " + String.Join(", ", parts.Reads));
        Check(whole.SequenceEqual(new[] { "read Widget", "read Sign", "read Holder", "read Board", "reads done", "classify Widget", "classify Sign", "classify Holder", "classify Board", "classified", "observe Sign", "observe Board" }),
            "The discovery reads every owner, then classifies every component, then observes the candidates: " + String.Join(", ", whole));

        // The search and the loaded checks take five units. The widget leaves before its fields are read.
        parts = Parts(out checkedInventory, out widget, out sign, out holder, out board);
        for (var i = 0; i < 5; i++) checkedInventory.AdvanceFreshnessCheck(one);
        widget.Gone = true;
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(!checkedInventory.IsStale && !checkedInventory.CheckInProgress, "A component that no observation depends on leaves during a pass without marking the snapshot stale: " + checkedInventory.StaleReason);
        Check(checkedInventory.Pin(checkedInventory.Entries[0]) != null, "The snapshot can still be pinned after that pass");
        // The widget leaves after its fields were read and before it is classified.
        parts = Parts(out checkedInventory, out widget, out sign, out holder, out board);
        for (var i = 0; i < 9; i++) checkedInventory.AdvanceFreshnessCheck(one);
        widget.Gone = true;
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(!checkedInventory.IsStale, "A component that leaves between the reads and its classification does not mark the snapshot stale: " + checkedInventory.StaleReason);

        // An observed object leaves: the observations differ.
        parts = Parts(out checkedInventory, out widget, out sign, out holder, out board);
        for (var i = 0; i < 9; i++) checkedInventory.AdvanceFreshnessCheck(one);
        board.Gone = true;
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(checkedInventory.IsStale && checkedInventory.StaleReason.Contains("1 observations instead of 2"), "An observed object that leaves during a pass marks the snapshot stale with its reason: " + checkedInventory.StaleReason);
        // A referrer leaves before it is read: the dependencies of its target differ.
        parts = Parts(out checkedInventory, out widget, out sign, out holder, out board);
        for (var i = 0; i < 5; i++) checkedInventory.AdvanceFreshnessCheck(one);
        holder.Gone = true;
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(checkedInventory.IsStale && checkedInventory.StaleReason.Contains("scene/Sign"), "A referrer that leaves during a pass marks the snapshot stale and names the changed observation: " + checkedInventory.StaleReason);
        Check(Refused(() => checkedInventory.Pin(checkedInventory.Entries[1])), "A stale snapshot still refuses a pin");
        checkedInventory.Refresh();
        Check(!checkedInventory.IsStale && checkedInventory.StaleReason == null && checkedInventory.Entries[0].Dependencies.Count == 0, "Refresh observes the scene without the referrer and clears the reason");

        // A failing scan and a changed value keep their meaning and name it.
        parts = Parts(out checkedInventory, out widget, out sign, out holder, out board);
        sign.Active = false;
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(checkedInventory.IsStale && checkedInventory.StaleReason.Contains("scene/Sign"), "A changed value names its observation: " + checkedInventory.StaleReason);
        parts = Parts(out checkedInventory, out widget, out sign, out holder, out board);
        parts.Parts.Add(new Part { Name = "Late", Family = "Track marker", Gone = false });
        checkedInventory.CheckFreshness();
        Check(checkedInventory.IsStale && checkedInventory.StaleReason.Contains("3 observations instead of 2"), "The one-call check names its reason as well: " + checkedInventory.StaleReason);
        // A stopped reference walk reads no further owner; classification and observation continue.
        parts = Parts(out checkedInventory, out widget, out sign, out holder, out board);
        parts.Reads.Clear(); parts.FailingRead = sign;
        Check(parts.Discover().Count() == 2 && parts.Reads.SequenceEqual(new[] { "read Widget", "reads done", "classify Widget", "classify Sign", "classify Holder", "classify Board", "classified", "observe Sign", "observe Board" }),
            "A stopped owner walk reads no further owner and still observes the candidates: " + String.Join(", ", parts.Reads));
    }

    // The panel's inventory notice says that edits are refused only in the states in which a pin refuses them.
    static void StaleNoticePromisesOnlyWhatPinRefuses() {
        const string refused = "Edits are refused until then.";
        var scene = new Scene(); var box = new Box { X = 7 };
        scene.Objects["p"] = new List<object> { box };
        scene.Observations = () => new[] { new SceneObservation(box, new TargetIdentity("scene", "p", "Culler"), "Board", true, box.Position, box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var inventory = new SceneInventory(scene);
        var notice = RailroaderScenePatcher.InventoryPresentationState.Notice(inventory, true);
        Check(notice == "Inventory is stale: not yet scanned. Refresh before relying on these observations. " + refused && Refused(() => inventory.Pin(null)), "An unscanned inventory refuses edits and says so: " + notice);
        inventory.Refresh();
        notice = RailroaderScenePatcher.InventoryPresentationState.Notice(inventory, false);
        Check(notice == "Inventory snapshot. Changes are checked while this panel is open. Refresh after native edits.", "A current inventory with current definitions is a snapshot: " + notice);
        notice = RailroaderScenePatcher.InventoryPresentationState.Notice(inventory, true);
        Check(notice == "Inventory is stale: the supplied definitions changed or were not read. Refresh before relying on these observations." && inventory.Pin(inventory.Entries.Single()) != null,
            "Changed definitions alone are named as stale without a refusal the pin does not make: " + notice);
        box.X = 8; inventory.CheckFreshness();
        foreach (var definitionsStale in new[] { false, true }) {
            notice = RailroaderScenePatcher.InventoryPresentationState.Notice(inventory, definitionsStale);
            Check(notice == "Inventory is stale: scene/p [Culler] changed or is missing. Refresh before relying on these observations. " + refused && Refused(() => inventory.Pin(inventory.Entries.Single())),
                "A stale inventory names its own reason and the refusal, whatever the definitions: " + notice);
        }
        notice = RailroaderScenePatcher.InventoryPresentationState.Notice(null, false);
        Check(notice == "Inventory is stale: inspection is unavailable. Refresh before relying on these observations. " + refused, "A missing inventory refuses edits and says so: " + notice);
    }

    // Every edit that is refused or ignored names its reason for the panel and the log.
    static void EditFeedbackNamesEveryRefusal() {
        var scene = new Scene(); var box = new Box { X = 7 };
        scene.Objects["p"] = new List<object> { box };
        scene.Observations = () => new[] { new SceneObservation(box, new TargetIdentity("scene", "p", "Culler"), "Board", true, box.Position, box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var document = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"move\",\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}]}";
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var log = new List<string>(); var calls = 0;
        var feedback = new RailroaderScenePatcher.EditFeedback { Log = log.Add };
        Func<InspectedTarget, Outcome> move = inspection => { calls++; return new PatchEngine(scene).Apply(document, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inspection }).Outcomes["move"]; };

        box.X = 8;
        Check(!feedback.Run("placement", "Placement", null, inventory, inventory.Entries.Single(), move) && calls == 0 && box.X == 8, "A stale inventory refuses the edit before it runs");
        Check(feedback.Place == "placement" && feedback.Result == "Placement refused: Inventory is stale (scene/p [Culler] changed or is missing); refresh and inspect again" && log.Single() == feedback.Result, "The stale refusal names its reason for the panel and the log: " + feedback.Result);
        inventory.Refresh(); log.Clear();
        Check(!feedback.Run("binding", "Track binding", "a world selection is pending; confirm or cancel it first", inventory, inventory.Entries.Single(), move) && calls == 0,
            "An edit during a pending interaction does not run");
        Check(feedback.Place == "binding" && feedback.Result == "Track binding refused: a world selection is pending; confirm or cancel it first" && log.Single() == feedback.Result, "The ignored edit names the pending interaction: " + feedback.Result);
        log.Clear();
        Check(!feedback.Run("placement", "Placement", null, null, null, move) && feedback.Result.StartsWith("Placement refused: ", StringComparison.Ordinal) && log.Count == 1 && calls == 0, "An edit without an inspected object names that: " + feedback.Result);
        log.Clear();
        Check(!feedback.Run("placement", "Placement", null, inventory, inventory.Entries.Single(), inspection => throw new InvalidOperationException("injected edit failure")) &&
            feedback.Result == "Placement refused: injected edit failure" && log.Single() == feedback.Result, "A failing edit names its failure: " + feedback.Result);
        log.Clear();
        scene.FailApply = true;
        Check(!feedback.Run("placement", "Placement", null, inventory, inventory.Entries.Single(), move) && calls == 1 && feedback.Result.StartsWith("Placement refused: ", StringComparison.Ordinal) && feedback.Result.Length > "Placement refused: ".Length && log.Single() == feedback.Result,
            "A refused outcome names the engine's reason: " + feedback.Result);
        scene.FailApply = false; log.Clear(); inventory.Refresh();
        feedback.Refuse("binding", "Track binding", "enter a segment ID");
        Check(feedback.Place == "binding" && feedback.Result == "Track binding refused: enter a segment ID" && log.Single() == feedback.Result, "A refusal of entered values is logged like any other");
        log.Clear();
        Check(feedback.Run("placement", "Placement", null, inventory, inventory.Entries.Single(), move) && box.X == 12 && feedback.Result.StartsWith("Placement completed", StringComparison.Ordinal) && log.Single() == feedback.Result,
            "A completed edit reports completion in the same place: " + feedback.Result);
    }

    // The open panel compares the scene with its snapshot a few units per frame. A pass can only mark the snapshot
    // stale; it never replaces observations, and an unfinished pass authorizes nothing.
    static void SteppedFreshness() {
        Stepped Scene(out SceneInventory inventory) {
            var scene = new Stepped();
            foreach (var path in new[] { "A", "B", "C" }) scene.Objects.Add(Thing(new object(), path));
            inventory = new SceneInventory(scene); inventory.Refresh();
            scene.Starts = scene.Units = scene.Ended = 0;
            return scene;
        }
        var one = new Func<bool>(() => false);
        var stepped = Scene(out var checkedInventory);
        var snapshot = checkedInventory.Entries;
        checkedInventory.AdvanceFreshnessCheck(one);
        Check(stepped.Units == 1 && checkedInventory.CheckInProgress && !checkedInventory.IsStale, "One call without further budget performs one unit of the scan and leaves the pass unfinished");
        var budget = 2;
        checkedInventory.AdvanceFreshnessCheck(() => budget-- > 0);
        Check(stepped.Units == 4 && checkedInventory.CheckInProgress, "A call continues the same pass for as long as its budget allows");
        for (var i = 0; i < 20 && checkedInventory.CheckInProgress; i++) checkedInventory.AdvanceFreshnessCheck(one);
        Check(stepped.Starts == 1 && stepped.Ended == 1 && !checkedInventory.CheckInProgress && !checkedInventory.IsStale && ReferenceEquals(snapshot, checkedInventory.Entries),
            "A completed pass over an unchanged scene keeps the snapshot and its observations");
        checkedInventory.AdvanceFreshnessCheck(one);
        Check(stepped.Starts == 2 && checkedInventory.CheckInProgress, "The next call starts a new pass from the beginning");
        checkedInventory.CancelFreshnessCheck();
        Check(stepped.Ended == 2 && !checkedInventory.CheckInProgress && !checkedInventory.IsStale, "A canceled pass releases its scan and concludes nothing");

        // A change ahead of the scan is found when the pass reaches it; the snapshot keeps the inspected values.
        stepped = Scene(out checkedInventory);
        checkedInventory.AdvanceFreshnessCheck(one); checkedInventory.AdvanceFreshnessCheck(one);
        var inspected = checkedInventory.Entries[2];
        stepped.Objects[2] = Changed(stepped.Objects[2]);
        Check(!checkedInventory.IsStale, "An unfinished pass has not yet seen the change");
        for (var i = 0; i < 20 && checkedInventory.CheckInProgress; i++) checkedInventory.AdvanceFreshnessCheck(one);
        Check(checkedInventory.IsStale && !checkedInventory.CheckInProgress && stepped.Ended == 1 && ReferenceEquals(checkedInventory.Entries[2], inspected) && inspected.Active,
            "The pass marks the snapshot stale at the changed object without replacing its observation");
        var units = stepped.Units;
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(stepped.Units == units && checkedInventory.IsStale, "A stale snapshot stays stale without further scanning until explicit refresh");
        checkedInventory.Refresh();
        Check(!checkedInventory.IsStale && !checkedInventory.Entries[2].Active, "Explicit refresh replaces the observations and clears the stale state");

        // A change behind the scan is missed by this pass. The unfinished pass does not authorize a pin: pinning scans the
        // whole scene itself, and the following pass marks the snapshot.
        stepped = Scene(out checkedInventory);
        for (var i = 0; i < 4; i++) checkedInventory.AdvanceFreshnessCheck(one);
        stepped.Objects[0] = Changed(stepped.Objects[0]);
        var behind = checkedInventory.Entries[0]; var untouched = checkedInventory.Entries[2];
        Check(checkedInventory.CheckInProgress && Refused(() => checkedInventory.Pin(untouched)) && checkedInventory.IsStale,
            "Pinning during an unfinished pass still compares the whole scene and refuses a changed inventory");
        stepped = Scene(out checkedInventory);
        for (var i = 0; i < 4; i++) checkedInventory.AdvanceFreshnessCheck(one);
        stepped.Objects[0] = Changed(stepped.Objects[0]);
        for (var i = 0; i < 20 && checkedInventory.CheckInProgress; i++) checkedInventory.AdvanceFreshnessCheck(one);
        Check(!checkedInventory.IsStale, "A pass that already passed the changed object concludes nothing about it");
        for (var i = 0; i < 20 && !checkedInventory.IsStale; i++) checkedInventory.AdvanceFreshnessCheck(one);
        Check(checkedInventory.IsStale && stepped.Starts == 2, "The following pass finds the change");
        stepped = Scene(out checkedInventory);
        for (var i = 0; i < 4; i++) checkedInventory.AdvanceFreshnessCheck(one);
        var pinned = checkedInventory.Pin(checkedInventory.Entries[1]);
        Check(pinned != null && checkedInventory.CheckInProgress && !checkedInventory.IsStale, "An unchanged scene can be pinned while a pass is unfinished");

        // Added, removed and replaced objects.
        stepped = Scene(out checkedInventory);
        checkedInventory.AdvanceFreshnessCheck(one);
        stepped.Objects.Add(Thing(new object(), "D"));
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(checkedInventory.IsStale, "An object added during a pass marks the snapshot stale");
        stepped = Scene(out checkedInventory);
        checkedInventory.AdvanceFreshnessCheck(one);
        stepped.Objects.RemoveAt(2);
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(checkedInventory.IsStale, "An object removed during a pass marks the snapshot stale when the pass completes");
        stepped = Scene(out checkedInventory);
        stepped.Objects[1] = Thing(new object(), "B");
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(checkedInventory.IsStale, "A replacement with the same identity and values marks the snapshot stale");
        stepped = Scene(out checkedInventory);
        stepped.Objects.Add(stepped.Objects[0]);
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(checkedInventory.IsStale, "An observation that now appears twice marks the snapshot stale");

        // Map context and failures.
        stepped = Scene(out checkedInventory);
        stepped.Maps = new[] { "another-map" };
        checkedInventory.AdvanceFreshnessCheck(one);
        Check(checkedInventory.IsStale && stepped.Units == 0, "A changed map context marks the snapshot stale before the scan starts");
        stepped = Scene(out checkedInventory);
        checkedInventory.AdvanceFreshnessCheck(one);
        stepped.BeforeObservation = i => { if (i == 2) stepped.Maps = new[] { "another-map" }; };
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(checkedInventory.IsStale, "A map context that changes during a pass marks the snapshot stale");
        stepped = Scene(out checkedInventory);
        checkedInventory.AdvanceFreshnessCheck(one);
        stepped.BeforeObservation = i => { if (i == 1) throw new Exception("injected scan failure"); };
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(checkedInventory.IsStale && !checkedInventory.CheckInProgress && stepped.Ended == 1, "A scan failure during a pass marks the snapshot stale and releases the scan");
        stepped = Scene(out checkedInventory);
        checkedInventory.AdvanceFreshnessCheck(() => throw new Exception("injected budget failure"));
        Check(checkedInventory.IsStale && stepped.Ended == 1, "A failing budget marks the snapshot stale rather than leaving a pass undecided");

        // Explicit refresh ends the pass of the previous snapshot: its partial result is not carried over.
        stepped = Scene(out checkedInventory);
        for (var i = 0; i < 4; i++) checkedInventory.AdvanceFreshnessCheck(one);
        stepped.Objects.RemoveAt(2);
        checkedInventory.Refresh();
        Check(stepped.Ended == 2 && !checkedInventory.CheckInProgress && !checkedInventory.IsStale && checkedInventory.Entries.Count == 2, "Refresh ends the unfinished pass of the previous snapshot");
        var starts = stepped.Starts;
        for (var i = 0; i < 20; i++) { checkedInventory.AdvanceFreshnessCheck(one); if (!checkedInventory.CheckInProgress) break; }
        Check(stepped.Starts == starts + 1 && !checkedInventory.IsStale, "The pass after a refresh starts from the beginning against the new snapshot");
        stepped = Scene(out checkedInventory);
        checkedInventory.AdvanceFreshnessCheck(one);
        stepped.BeforeObservation = i => throw new Exception("injected refresh failure");
        Check(Refused(() => { try { checkedInventory.Refresh(); } catch (Exception e) { throw new InvalidOperationException(e.Message); } }) && !checkedInventory.CheckInProgress,
            "A failed refresh also ends the unfinished pass");

        // A scene without pause points is compared one observation per unit.
        var plain = new Fixture();
        plain.Objects.Add(Thing(new object(), "A")); plain.Objects.Add(Thing(new object(), "B"));
        var plainInventory = new SceneInventory(plain); plainInventory.Refresh();
        plainInventory.AdvanceFreshnessCheck(one);
        Check(plainInventory.CheckInProgress && !plainInventory.IsStale, "A scene without pause points is still checked in steps");
        plain.Objects[1] = Changed(plain.Objects[1]);
        plainInventory.AdvanceFreshnessCheck(() => true);
        Check(plainInventory.IsStale, "The stepped check of a plain scene finds a change");
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
