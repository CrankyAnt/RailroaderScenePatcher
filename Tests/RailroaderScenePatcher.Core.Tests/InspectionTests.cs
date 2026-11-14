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
        ProblemsFilterLimitsTheOutliner();
        RuntimeStateDoesNotMarkStale();
        RuntimeReferencesAreNotAuthoredState();
        AnEditScansTheSceneTwice();
        AnEditAfterASteppedPassBlocksForOneScan();
        OriginRoundingDoesNotMarkStale();
        TheGuardObservesOnlyTheTarget();
        AnObjectOutsideTheMovingOriginDoesNotMarkStale();
        CullerDrivenActivityDoesNotMarkStale();
        AnOwnEditTakesItsRelatedObservations();
        AComparedSnapshotPinsWithoutAScan();
        FollowUpsKeepTheActionResultVisible();
        AutoFitSizesTheOutlinerToMostNames();
        ResultsBelongToTheInspectedObject();
        MessagesKeepTheirSourceNewestFirst();
        RepeatedMessagesCollapse();
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
            Check(notice == "Inventory is stale: scene/p [Culler] changed (position). Refresh before relying on these observations. " + refused && Refused(() => inventory.Pin(inventory.Entries.Single())),
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
        Check(feedback.Place == "placement" && feedback.Result == "Placement refused: Inventory is stale (scene/p [Culler] changed (position)); refresh and inspect again" && log.Single() == feedback.Result, "The stale refusal names its reason for the panel and the log: " + feedback.Result);
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
        Check(inventory.IsStale && presentation.CanSelect(inventory, current), "A stale inventory still lets its rows be inspected; only edits are refused");
        refused = false;
        try { inventory.Pin(current); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Pin still refuses the replaced native target");
        inventory.Refresh();
        Check(presentation.CurrentSelection(inventory, current) == null, "Refresh does not transfer inspection to a replacement with the same identity");
        fixture.Objects[0] = original;
        inventory.Refresh();
    }

    // The outliner rows: the search text and the All, Problems and Changed filters narrow the inventory, and none changes it.
    static void ProblemsFilterLimitsTheOutliner() {
        SceneObservation Entry(string path, string family, params string[] problems) => new SceneObservation(new object(), new TargetIdentity("scene", path, "Component"), family, true,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new string[0], new string[0], problems, "Inspection only");
        var fixture = new Fixture();
        fixture.Objects.Add(Entry("World/Board", "Independent board"));
        fixture.Objects.Add(Entry("World/Sign", "Track marker", "Broken binding"));
        fixture.Objects.Add(Entry("World/Post", "Track marker", "Segment missing"));
        fixture.Objects.Add(Entry("World/Bridges/Ela", "Bridge"));
        var inventory = new SceneInventory(fixture); inventory.Refresh();
        Func<SceneObservation, bool> changed = entry => entry.Identity.Path == "World/Board" || entry.Identity.Path == "World/Post";
        string[] Paths(string search, RailroaderScenePatcher.OutlinerFilter filter) => RailroaderScenePatcher.InventoryPresentationState.Rows(inventory, search, filter, changed).Select(e => e.Identity.Path).ToArray();
        var all = RailroaderScenePatcher.OutlinerFilter.All; var problems = RailroaderScenePatcher.OutlinerFilter.Problems; var modified = RailroaderScenePatcher.OutlinerFilter.Changed;
        Check(Paths("", problems).SequenceEqual(new[] { "World/Sign", "World/Post" }), "The Problems filter limits the outliner to objects with detected problems");
        Check(Paths("", modified).SequenceEqual(new[] { "World/Board", "World/Post" }), "The Changed filter limits the outliner to objects this MapMod modifies");
        Check(Paths("", all).SequenceEqual(new[] { "World/Board", "World/Sign", "World/Post", "World/Bridges/Ela" }), "All shows every inventory entry");
        Check(Paths("post", problems).SequenceEqual(new[] { "World/Post" }) && Paths("board", problems).Length == 0 && Paths("sign", modified).Length == 0, "Search narrows within a filter");
        Check(Paths("marker", all).SequenceEqual(new[] { "World/Sign", "World/Post" }), "Search alone keeps its meaning");
        Check(inventory.Entries.Count == 4 && !inventory.IsStale, "Filtering changes neither the inventory nor its freshness");
        Check(RailroaderScenePatcher.InventoryPresentationState.Rows(null, "", problems, changed).Count == 0, "A missing inventory has no rows");
        Check(RailroaderScenePatcher.InventoryPresentationState.Rows(inventory, "", modified, null).Count == 0, "Without a document nothing is changed");
        Check(RailroaderScenePatcher.InventoryPresentationState.Summary(inventory, "", problems, changed) == "2 of 4 objects", "The count line names how many objects the filters show: " + RailroaderScenePatcher.InventoryPresentationState.Summary(inventory, "", problems, changed));
        Check(RailroaderScenePatcher.InventoryPresentationState.Summary(inventory, "", all, changed) == "4 of 4 objects", "The unfiltered outliner counts every entry");
        var groups = RailroaderScenePatcher.InventoryPresentationState.Groups(RailroaderScenePatcher.InventoryPresentationState.Rows(inventory, "", all, changed));
        Check(groups.Select(g => g.Key + ":" + String.Join(",", g.Value.Select(RailroaderScenePatcher.InventoryPresentationState.Name))).SequenceEqual(new[] { "Bridge:Ela", "Independent board:Board", "Track marker:Sign,Post" }),
            "Rows are grouped by family in name order, each row named by its object and kept in inventory order");
        var presentation = new RailroaderScenePatcher.InventoryPresentationState();
        Check(presentation.Observe(inventory, null, "", all, "1") && !presentation.Observe(inventory, null, "", all, "1"), "A settled outliner does not rebuild");
        Check(presentation.Observe(inventory, null, "", problems, "1"), "Switching a filter rebuilds the outliner");
        Check(!presentation.Observe(inventory, null, "", problems, "1") && presentation.Observe(inventory, null, "", all, "1"), "Returning to All rebuilds the outliner once");
        Check(presentation.Observe(inventory, null, "", all, "2") && !presentation.Observe(inventory, null, "", all, "2"), "A changed document rebuilds the outliner so its Changed markers follow Undo and Redo");
    }

    // State the game switches while it runs, such as a culled audio player's enabled flag, is shown but is no part of
    // what an inspection pins: its change leaves the snapshot fresh, and any other change still marks it stale.
    static void RuntimeStateDoesNotMarkStale() {
        var bell = new object();
        SceneObservation Bell(bool playerEnabled, string dependency = "Component Crossing Bell [Audio.IntegerLoopingPlayer]", bool active = true) => new SceneObservation(bell,
            new TargetIdentity("scene", "World/Crossing Bell", "Audio.IntegerLoopingPlayer"), "Crossing bell", active, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 },
            new string[0], new[] { dependency }, new string[0], "Inspection only", new[] { "Crossing Bell [Audio.IntegerLoopingPlayer] enabled=" + playerEnabled });
        var fixture = new Fixture();
        fixture.Objects.Add(Bell(true));
        var inventory = new SceneInventory(fixture); inventory.Refresh();
        var inspected = inventory.Entries.Single();
        Check(inspected.RuntimeState.Single() == "Crossing Bell [Audio.IntegerLoopingPlayer] enabled=True", "Runtime state is kept for display");
        fixture.Objects[0] = Bell(false);
        inventory.CheckFreshness();
        Check(!inventory.IsStale && inspected.Matches(fixture.Objects[0]) && inventory.Pin(inspected) != null, "A runtime toggle leaves the snapshot fresh and the inspection pinnable");
        inventory.AdvanceFreshnessCheck(() => true);
        Check(!inventory.IsStale, "The stepped check ignores a runtime toggle as well");
        fixture.Objects[0] = Bell(false, "Component Crossing Bell [Audio.OtherPlayer]");
        inventory.CheckFreshness();
        Check(inventory.IsStale, "A changed component still marks the snapshot stale");
        inventory.Refresh();
        fixture.Objects[0] = Bell(false, "Component Crossing Bell [Audio.OtherPlayer]", active: false);
        inventory.CheckFreshness();
        Check(inventory.IsStale, "The object's own activity still marks the snapshot stale");
    }

    // A reference held in a field Unity serializes is authored state. A reference in any other field, such as a
    // culling or audio manager's working list, comes and goes while the game runs: inspecting or focusing an object
    // changes it. Only authored references belong to what an inspection pins.
    sealed class Holder {
        public object Authored = null;
        [UnityEngine.SerializeField] object alsoAuthored = null;
        object working = null;
        [NonSerialized] public object Excluded = null;
        public readonly object Fixed = null;
        public static object Shared = null;
        internal object Use() => alsoAuthored ?? working;
    }
    static void RuntimeReferencesAreNotAuthoredState() {
        bool Authored(string name) => ReferenceScan.IsSerialized(typeof(Holder).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic));
        Check(Authored("Authored") && Authored("alsoAuthored"), "Public fields and fields marked for serialization hold authored references");
        Check(!Authored("working") && !Authored("Excluded") && !Authored("Fixed") && !Authored("Shared"), "Private, non-serialized, read-only and static fields hold runtime references");
        Check(new ReferenceField("f", true, () => null).Authored && !new ReferenceField("f", true, () => null, false).Authored, "A field descriptor carries whether its references are authored");
        // The inspect-driven change: a manager starts referencing the inspected object. It is runtime state.
        var bell = new object();
        SceneObservation Bell(params string[] runtime) => new SceneObservation(bell, new TargetIdentity("scene", "World/Bell", "Audio.Player"), "Crossing bell", true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 },
            new string[0], new[] { "Referenced by Crossing [Controller].bell" }, new string[0], "Inspection only", runtime);
        var fixture = new Fixture(); fixture.Objects.Add(Bell());
        var inventory = new SceneInventory(fixture); inventory.Refresh();
        fixture.Objects[0] = Bell("Referenced at runtime by Audio Manager [Audio.Culler].active");
        inventory.CheckFreshness();
        Check(!inventory.IsStale, "A runtime reference that appears after inspecting or focusing leaves the snapshot fresh");
        // The bell's audio sources come from a pool and are parented under it while it is audible.
        fixture.Objects[0] = Bell("Runtime object World/Bell/AS07 [UnityEngine.AudioSource]", "Runtime object World/Bell/AS07 [UnityEngine.AudioLowPassFilter]");
        inventory.CheckFreshness();
        Check(!inventory.IsStale, "A pooled child object that the game parents under the bell leaves the snapshot fresh");
        fixture.Objects[0] = new SceneObservation(bell, new TargetIdentity("scene", "World/Bell", "Audio.Player"), "Crossing bell", true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 },
            new string[0], new string[0], new string[0], "Inspection only");
        inventory.CheckFreshness();
        Check(inventory.IsStale, "A removed authored reference still marks the snapshot stale");
    }
    // An edit from the panel pins the inspection and edits in one call. The pin compares the whole scene, so the edit's
    // first validation reuses that comparison; the guard immediately before the mutation reads the scene again.
    static void AnEditScansTheSceneTwice() {
        var scene = new Scene(); var box = new Box { X = 7 }; var scans = 0;
        scene.Objects["p"] = new List<object> { box };
        scene.Observations = () => { scans++; return new[] { new SceneObservation(box, new TargetIdentity("scene", "p", "Culler"), "Board", true, box.Position, box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") }; };
        var document = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"move\",\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}]}";
        var inventory = new SceneInventory(scene); inventory.Refresh();
        scans = 0;
        var result = new PatchEngine(scene).Apply(document, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inventory.PinForEdit(inventory.Entries.Single()) });
        Check(result.Outcomes["move"].Applied && box.X == 12 && scans == 2, "An edit pinned for immediate use scans the scene for the pin and for the guard: " + scans);
        inventory.Refresh(); scans = 0;
        result = new PatchEngine(scene).Apply(document, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inventory.Pin(inventory.Entries.Single()) });
        Check(result.Outcomes["move"].Applied && scans == 3, "A pin kept for later validates at resolution as well: " + scans);
        // The guard still reads the scene: a change after the pin and before the mutation refuses the edit.
        inventory.Refresh(); box.X = 12; var mutations = scene.MutationCalls;
        var token = inventory.PinForEdit(inventory.Entries.Single());
        scene.BeforeCapture = () => box.Position[1] = 99;
        result = new PatchEngine(scene).Apply(document.Replace("112", "120"), "pack", new Dictionary<string, InspectedTarget> { ["move"] = token });
        scene.BeforeCapture = null;
        Check(!result.Outcomes["move"].Applied && scene.MutationCalls == mutations, "A change between the pin and the mutation is still refused by the guard: " + result.Outcomes["move"].Error);
    }

    // The panel spreads the whole-scene comparison of an edit over frames, as the background check does, so it does
    // not block. When that pass ends without a difference the edit runs at once; its guard still reads the scene in
    // one piece immediately before the mutation.
    static void AnEditAfterASteppedPassBlocksForOneScan() {
        var scene = new Scene(); var box = new Box { X = 7 }; var scans = 0;
        scene.Objects["p"] = new List<object> { box };
        scene.Observations = () => { scans++; return new[] { new SceneObservation(box, new TargetIdentity("scene", "p", "Culler"), "Board", true, box.Position, box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") }; };
        var document = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"move\",\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}]}";
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var passes = inventory.CompletedPasses;
        bool refused = false;
        try { inventory.PinAfterPass(inventory.Entries.Single(), passes); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Without a completed pass since the edit was requested there is no pin");
        while (inventory.CompletedPasses == passes && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        Check(inventory.CompletedPasses == passes + 1 && !inventory.IsStale, "A pass over an unchanged scene completes");
        scans = 0;
        var result = new PatchEngine(scene).Apply(document, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inventory.PinAfterPass(inventory.Entries.Single(), passes) });
        Check(result.Outcomes["move"].Applied && box.X == 12 && scans == 1, "After a completed pass the edit reads the scene once, in its guard: " + scans);
        // A difference found by the pass refuses the edit.
        inventory.Refresh(); passes = inventory.CompletedPasses; box.Position[2] = 50;
        while (inventory.CompletedPasses == passes && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        refused = false;
        try { inventory.PinAfterPass(inventory.Entries.Single(), passes); } catch (InvalidOperationException e) { refused = e.Message.Contains("stale"); }
        Check(inventory.IsStale && refused, "A pass that finds a difference marks the inventory stale and the edit is refused");
        // The guard still refuses a change made after the pass and before the mutation.
        inventory.Refresh(); passes = inventory.CompletedPasses; var mutations = scene.MutationCalls;
        while (inventory.CompletedPasses == passes && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        var token = inventory.PinAfterPass(inventory.Entries.Single(), passes);
        scene.BeforeCapture = () => box.Position[1] = 99;
        result = new PatchEngine(scene).Apply(document.Replace("112", "130"), "pack", new Dictionary<string, InspectedTarget> { ["move"] = token });
        scene.BeforeCapture = null;
        Check(!result.Outcomes["move"].Applied && scene.MutationCalls == mutations, "A change after the pass is still refused by the guard: " + result.Outcomes["move"].Error);
    }

    // The game keeps positions relative to a moving origin in single precision. After the origin moved, the same map
    // position reads a few millimetres differently: that is no change. A real move still is, and the stale reason
    // names what differs.
    static void OriginRoundingDoesNotMarkStale() {
        var post = new object();
        SceneObservation Post(double x, params string[] bindings) => new SceneObservation(post, new TargetIdentity("scene", "World/Mile Posts/T-60", "Helpers.MilePost"), "Milepost", true,
            new[] { x, 633.25, 441.5 }, new double[] { 0, 0, 0, 1 }, bindings.Length == 0 ? new[] { "Marker segment=S1; end=A; distance=10" } : bindings, new string[0], new string[0], "Inspection only");
        var fixture = new Fixture(); fixture.Objects.Add(Post(25692.9766));
        var inventory = new SceneInventory(fixture); inventory.Refresh();
        fixture.Objects[0] = Post(25692.9785);
        inventory.CheckFreshness();
        Check(!inventory.IsStale && inventory.Pin(inventory.Entries.Single()) != null, "A position that differs by the origin's rounding leaves the snapshot fresh and pinnable");
        inventory.AdvanceFreshnessCheck(() => true);
        Check(!inventory.IsStale, "The stepped check allows the same rounding");
        fixture.Objects[0] = Post(25693.0766);
        inventory.CheckFreshness();
        Check(inventory.IsStale && inventory.StaleReason == "scene/World/Mile Posts/T-60 [Helpers.MilePost] changed (position)", "A move of ten centimetres marks the snapshot stale and the reason names the position: " + inventory.StaleReason);
        inventory.Refresh();
        fixture.Objects[0] = Post(25693.0766, "Marker segment=S2; end=A; distance=10");
        inventory.AdvanceFreshnessCheck(() => true);
        Check(inventory.IsStale && inventory.StaleReason.Contains("changed (bindings: Marker segment=S") , "The stepped check names the binding that differs: " + inventory.StaleReason);
    }
    // A scene that can observe one object lets the guard read only the target, in the frame of the mutation, and lets
    // the inventory take the edited object's new observation without discovering the scene again.
    sealed class TargetedScene : IScene, ITargetedInspectionScene {
        public readonly Scene Inner = new Scene();
        public int Discoveries, Observations;
        public IEnumerable<string> MapIds => Inner.MapIds;
        public IEnumerable<SceneObservation> Discover() { Discoveries++; return Inner.Discover(); }
        public SceneObservation Observe(object handle, TargetIdentity identity) { Observations++; return Inner.Discover().FirstOrDefault(o => ReferenceEquals(o.Handle, handle)); }
        public Capability Capability => Inner.Capability;
        public IReadOnlyList<object> Resolve(TargetIdentity identity) => Inner.Resolve(identity);
        public void Preflight(object target) => Inner.Preflight(target);
        public object Capture(object target) => Inner.Capture(target);
        public ObjectState Read(object target) => Inner.Read(target);
        public void Apply(object target, ObjectState state) => Inner.Apply(target, state);
        public bool Restore(object target, object snapshot) => Inner.Restore(target, snapshot);
    }
    static void TheGuardObservesOnlyTheTarget() {
        var scene = new TargetedScene(); var box = new Box { X = 7 };
        scene.Inner.Objects["p"] = new List<object> { box };
        scene.Inner.Observations = () => new[] { new SceneObservation(box, new TargetIdentity("scene", "p", "Culler"), "Board", true, box.Position, box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var document = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"move\",\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}]}";
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var passes = inventory.CompletedPasses;
        while (inventory.CompletedPasses == passes && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        scene.Discoveries = 0; scene.Observations = 0;
        var result = new PatchEngine(scene).Apply(document, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inventory.PinAfterPass(inventory.Entries.Single(), passes) });
        Check(result.Outcomes["move"].Applied && box.X == 12 && scene.Discoveries == 0 && scene.Observations == 1, "The guard observes the target once and discovers nothing: " + scene.Discoveries + "/" + scene.Observations);
        var before = inventory.Entries.Single();
        var after = inventory.Reobserve(before);
        Check(after != null && !ReferenceEquals(after, before) && ReferenceEquals(inventory.Entries.Single(), after) && after.Position[0] == 12 && scene.Discoveries == 0 && !inventory.IsStale,
            "After the edit the inventory takes the object's new observation without a discovery");
        inventory.CheckFreshness();
        Check(!inventory.IsStale, "The inventory with the re-observed entry agrees with the scene");
        // A change between the pass and the mutation is refused by the targeted guard as well.
        passes = inventory.CompletedPasses; var mutations = scene.Inner.MutationCalls;
        while (inventory.CompletedPasses == passes && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        var token = inventory.PinAfterPass(inventory.Entries.Single(), passes);
        scene.Inner.BeforeCapture = () => box.Position[1] = 99;
        result = new PatchEngine(scene).Apply(document.Replace("112", "130"), "pack", new Dictionary<string, InspectedTarget> { ["move"] = token });
        scene.Inner.BeforeCapture = null;
        Check(!result.Outcomes["move"].Applied && scene.Inner.MutationCalls == mutations, "A change between the pass and the mutation is refused: " + result.Outcomes["move"].Error);
        Check(inventory.Reobserve(before) == null, "An observation that is no longer an entry is not re-observed");
    }

    // The game shifts the objects it registered for its moving origin and leaves the others where they are, such as the
    // markers of an equipment setup. Neither kind changes position when the origin moves; a move of either one does.
    static void AnObjectOutsideTheMovingOriginDoesNotMarkStale() {
        var offset = new double[3]; var moved = new double[] { 10, 0, 20 }; var kept = new double[] { 5, 0, 5 };
        object board = new object(), marker = new object();
        var scene = new Scene();
        SceneObservation At(object handle, string path, double[] position, bool movesWithOrigin) => new SceneObservation(handle, new TargetIdentity("scene", path, "Component"), "Family", true,
            MapOrigin.Position(position, offset, movesWithOrigin), new double[] { 0, 0, 0, 1 }, new string[0], new string[0], new string[0], "Inspection only");
        scene.Observations = () => new[] { At(board, "World/board", moved, true), At(marker, "Setups/marker", kept, false) };
        var inventory = new SceneInventory(scene); inventory.Refresh();
        Check(inventory.Entries[0].Position.SequenceEqual(new double[] { 10, 0, 20 }) && inventory.Entries[1].Position.SequenceEqual(new double[] { 5, 0, 5 }), "At the first origin both positions are the scene positions");
        // The origin moves three tiles one way and one the other: the game shifts what it registered, and its offset.
        moved[0] -= 1500; offset[0] -= 1500; moved[2] += 500; offset[2] += 500;
        inventory.CheckFreshness();
        Check(!inventory.IsStale, "An origin move marks nothing stale, also not the object the game does not shift: " + inventory.StaleReason);
        while (inventory.CompletedPasses == 0 && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        Check(!inventory.IsStale && inventory.CompletedPasses == 1, "A stepped pass after the origin move agrees: " + inventory.StaleReason);
        kept[0] += 1;
        inventory.CheckFreshness();
        Check(inventory.IsStale && inventory.StaleReason == "scene/Setups/marker [Component] changed (position)", "A move of the object the game does not shift is detected: " + inventory.StaleReason);
        inventory.Refresh(); moved[2] += 1;
        inventory.CheckFreshness();
        Check(inventory.IsStale && inventory.StaleReason == "scene/World/board [Component] changed (position)", "A move of a shifted object is detected after the origin moved: " + inventory.StaleReason);
        var refused = false;
        try { MapOrigin.Position(moved, new double[2], true); } catch (ArgumentException) { refused = true; }
        Check(refused, "An offset that does not fit the position is refused");
    }

    // A crossing signal sits under an object that a culler switches off when the camera is far away, as after a camera
    // jump. That switch marks nothing stale; switching off the signal itself, or an object above the culled one, does.
    static void CullerDrivenActivityDoesNotMarkStale() {
        // The chain from the signal upward: the signal, the object the culler switches, the crossing.
        var on = new[] { true, true, true }; var culled = new[] { false, true, false };
        object signal = new object();
        var scene = new Scene();
        scene.Observations = () => new[] { new SceneObservation(signal, new TargetIdentity("scene", "Crossing/Signal 1/Signal 1", "Lamp"), "Crossing light",
            AuthoredActivity.Of(on.Select((active, index) => new KeyValuePair<bool, bool>(active, culled[index]))), new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 },
            new string[0], new string[0], new string[0], "Inspection only", new[] { "Culled object active=" + on[1] }) };
        var inventory = new SceneInventory(scene); inventory.Refresh();
        Check(inventory.Entries[0].Active, "An object under a culled object that is switched on is active");
        on[1] = false;
        inventory.CheckFreshness();
        Check(!inventory.IsStale, "The culler switching its object off marks nothing stale: " + inventory.StaleReason);
        while (inventory.CompletedPasses == 0 && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        Check(!inventory.IsStale && inventory.CompletedPasses == 1, "A stepped pass agrees: " + inventory.StaleReason);
        inventory.Refresh();
        Check(inventory.Entries[0].Active && inventory.Entries[0].RuntimeState.Single() == "Culled object active=False", "A refresh while the object is culled still observes it as active, and shows the culled state");
        on[1] = true; on[0] = false;
        inventory.CheckFreshness();
        Check(inventory.IsStale && inventory.StaleReason == "scene/Crossing/Signal 1/Signal 1 [Lamp] changed (activity)", "Switching the signal itself off is detected: " + inventory.StaleReason);
        on[0] = true; inventory.Refresh(); on[2] = false;
        inventory.CheckFreshness();
        Check(inventory.IsStale && inventory.StaleReason.EndsWith("changed (activity)", StringComparison.Ordinal), "Switching off an object above the culled one is detected: " + inventory.StaleReason);
        Check(AuthoredActivity.Of(new KeyValuePair<bool, bool>[0]), "An empty chain is active");
    }

    // A scene that knows what belongs with an object: its observed parts and the objects the patcher keeps for it.
    sealed class RelatedScene : IRelatedInspectionScene {
        public List<SceneObservation> All = new List<SceneObservation>();
        public Func<object, IEnumerable<SceneObservation>> Related;
        public int Discoveries;
        public IEnumerable<string> MapIds => new[] { "map" };
        public IEnumerable<SceneObservation> Discover() { Discoveries++; return All.ToArray(); }
        public SceneObservation Observe(object handle, TargetIdentity identity) => All.FirstOrDefault(o => ReferenceEquals(o.Handle, handle));
        public IEnumerable<SceneObservation> ObserveRelated(object handle, TargetIdentity identity) => Related(handle);
        public bool Present(object handle) => All.Any(o => ReferenceEquals(o.Handle, handle));
    }
    // An edit of a crossing's coverage changes the crossing, changes the bell that reads its circuits and adds spans the
    // patcher owns; clearing it removes those spans again. The inventory follows without discovering the scene.
    static void AnOwnEditTakesItsRelatedObservations() {
        object crossing = new object(), bell = new object(), other = new object(), span = new object();
        SceneObservation Of(object handle, string path, string binding) => new SceneObservation(handle, new TargetIdentity("scene", path, "Component"), "Family", true,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new[] { binding }, new string[0], new string[0], "Inspection only");
        var scene = new RelatedScene();
        scene.All.AddRange(new[] { Of(crossing, "crossing", "native spans"), Of(bell, "crossing/bell", "native spans"), Of(other, "other", "none") });
        scene.Related = handle => scene.All.Where(o => !ReferenceEquals(o.Handle, other)).ToArray();
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var unrelated = inventory.Entries[2];
        scene.All[0] = Of(crossing, "crossing", "selected tracks"); scene.All[1] = Of(bell, "crossing/bell", "selected tracks"); scene.All.Add(Of(span, "owned/span", "span"));
        var discoveries = scene.Discoveries;
        var updated = inventory.ReobserveRelated(inventory.Entries[0]);
        Check(updated != null && updated.Bindings[0] == "selected tracks" && inventory.Entries.Count == 4 && inventory.Entries.Any(e => ReferenceEquals(e, unrelated)) && scene.Discoveries == discoveries,
            "The edited object, its changed part and the added object are taken without a discovery: " + inventory.Entries.Count);
        inventory.CheckFreshness();
        Check(!inventory.IsStale, "The inventory with the related observations agrees with the scene: " + inventory.StaleReason);
        while (inventory.CompletedPasses == 0 && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        Check(!inventory.IsStale, "A stepped pass agrees as well: " + inventory.StaleReason);
        // Clearing removes the owned span and returns the bindings.
        scene.All.RemoveAt(3); scene.All[0] = Of(crossing, "crossing", "native spans"); scene.All[1] = Of(bell, "crossing/bell", "native spans");
        updated = inventory.ReobserveRelated(updated);
        Check(updated != null && inventory.Entries.Count == 3 && inventory.Entries.Any(e => ReferenceEquals(e, unrelated)), "An object the change removed leaves the inventory: " + inventory.Entries.Count);
        inventory.CheckFreshness();
        Check(!inventory.IsStale, "The inventory without the removed object agrees with the scene: " + inventory.StaleReason);
        // A change of an unrelated object is still found.
        scene.All[2] = Of(other, "other", "changed");
        inventory.ReobserveRelated(updated);
        inventory.CheckFreshness();
        Check(inventory.IsStale && inventory.StaleReason.StartsWith("scene/other [Component] changed (bindings", StringComparison.Ordinal), "A change of an unrelated object still marks the inventory stale: " + inventory.StaleReason);
        Check(inventory.ReobserveRelated(Of(crossing, "crossing", "x")) == null, "An observation that is no entry is not re-observed");
        // A scene that does not know what belongs with an object re-observes the object alone.
        var targeted = new TargetedScene(); var box = new Box { X = 7 };
        targeted.Inner.Observations = () => new[] { new SceneObservation(box, new TargetIdentity("scene", "p", "Culler"), "Board", true, box.Position, box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var single = new SceneInventory(targeted); single.Refresh(); box.X = 9;
        Check(single.ReobserveRelated(single.Entries.Single())?.Position[0] == 9, "Without related observations the object itself is re-observed");
    }

    // A snapshot that was compared since the last foreign change pins an edit without another whole-scene comparison;
    // the guard still observes the target and refuses a change.
    static void AComparedSnapshotPinsWithoutAScan() {
        var scene = new TargetedScene(); var box = new Box { X = 7 };
        scene.Inner.Objects["p"] = new List<object> { box };
        scene.Inner.Observations = () => new[] { new SceneObservation(box, new TargetIdentity("scene", "p", "Culler"), "Board", true, box.Position, box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var document = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"move\",\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}]}";
        var inventory = new SceneInventory(scene); inventory.Refresh();
        scene.Discoveries = 0; scene.Observations = 0;
        var result = new PatchEngine(scene).Apply(document, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inventory.PinCompared(inventory.Entries.Single()) });
        Check(result.Outcomes["move"].Applied && box.X == 12 && scene.Discoveries == 0 && scene.Observations >= 1, "The edit runs without a discovery and observes the target: " + scene.Discoveries + "/" + scene.Observations);
        var entry = inventory.Reobserve(inventory.Entries.Single());
        var token = inventory.PinCompared(entry); var mutations = scene.Inner.MutationCalls;
        scene.Inner.BeforeCapture = () => box.Position[1] = 99;
        result = new PatchEngine(scene).Apply(document.Replace("112", "130"), "pack", new Dictionary<string, InspectedTarget> { ["move"] = token });
        scene.Inner.BeforeCapture = null;
        Check(!result.Outcomes["move"].Applied && scene.Inner.MutationCalls == mutations, "A change of the target before the mutation is refused: " + result.Outcomes["move"].Error);
        // A preview validates once: after a pass, that one validation observes the target when asked to.
        inventory.Refresh(); var passes = inventory.CompletedPasses;
        while (inventory.CompletedPasses == passes && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        scene.Observations = 0;
        inventory.PinAfterPass(inventory.Entries.Single(), passes, true).Validate(scene, inventory.Entries.Single().Identity, box);
        Check(scene.Observations == 1, "A pin that validates every time observes the target at its first validation: " + scene.Observations);
        box.Position[2] = 50; var refused = false;
        try { inventory.PinAfterPass(inventory.Entries.Single(), passes, true).Validate(scene, inventory.Entries.Single().Identity, box); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Its single validation refuses a changed target");
        inventory.CheckFreshness(); refused = false;
        try { inventory.PinCompared(inventory.Entries.Single()); } catch (InvalidOperationException e) { refused = e.Message.Contains("stale"); }
        Check(inventory.IsStale && refused, "A stale snapshot does not pin");
        // A scene that cannot observe one object compares the whole scene, as an ordinary pin does.
        var plain = new Scene(); var discoveries = 0; object handle = new object();
        plain.Observations = () => { discoveries++; return new[] { new SceneObservation(handle, new TargetIdentity("scene", "p", "Culler"), "Board", true, new double[3], new double[] { 0, 0, 0, 1 }, new string[0], new string[0], new string[0], "Fixed placement") }; };
        var whole = new SceneInventory(plain); whole.Refresh(); discoveries = 0;
        whole.PinCompared(whole.Entries.Single());
        Check(discoveries == 1, "Without single-object observation the pin compares the whole scene: " + discoveries);
    }

    // The newest message is first. Follow-ups such as terrain output replace one another, so the result they follow stays
    // among the messages shown.
    static void FollowUpsKeepTheActionResultVisible() {
        var log = new List<string>();
        var feedback = new RailroaderScenePatcher.EditFeedback { Log = log.Add };
        string Line(RailroaderScenePatcher.EditFeedback.Message m) => m.Source + ": " + m.Text;
        feedback.Follow("Terrain output", "Terrain output complete.");
        Check(feedback.Messages.Select(Line).SequenceEqual(new[] { "Terrain output: Terrain output complete." }), "A follow-up without a result is shown");
        feedback.Report("Placement", "Placement completed.");
        feedback.Report("Activity", "Enable completed.");
        feedback.Follow("Terrain output", "Terrain output outstanding.");
        feedback.Follow("Terrain output", "Terrain output complete.");
        Check(feedback.Messages.Select(Line).SequenceEqual(new[] { "Terrain output: Terrain output complete.", "Activity: Enable completed.", "Placement: Placement completed." }),
            "The newest message is first and the action result stays below the latest follow-up: " + String.Join(" | ", feedback.Messages.Select(Line)));
        Check(feedback.Messages.Count(m => m.FollowUp) == 1 && log.Count == 5 && feedback.Result == "Terrain output complete.", "Only the latest follow-up is kept, and every message is logged");
        feedback.Report("Activity", "Disable completed.");
        Check(feedback.Messages.Select(Line).SequenceEqual(new[] { "Activity: Disable completed.", "Terrain output: Terrain output complete.", "Activity: Enable completed." }), "A new result goes on top; older messages move down");
    }

    // Auto-fit sizes the outliner column to most names, relative to the window, within a cap.
    static void AutoFitSizesTheOutlinerToMostNames() {
        float Fit(IEnumerable<float> widths, float window) => RailroaderScenePatcher.InventoryPresentationState.FitWidth(widths, 40, window, 140, 320, 6);
        var few = new float[] { 60, 80, 100 };
        Check(Fit(few, 1000) == 140, "With few rows the longest name decides, and the column keeps its minimum: " + Fit(few, 1000));
        Check(Fit(new float[] { 60, 80, 170 }, 1000) == 210, "With few rows the longest name plus the row decoration is the width");
        var many = Enumerable.Range(1, 100).Select(i => (float)i).ToList(); many[99] = 900;
        Check(Fit(many, 1000) == 140, "One extreme name among many does not decide: the 95th percentile does");
        var wide = Enumerable.Range(1, 100).Select(i => 200f + i).ToList();
        Check(Fit(wide, 1000) == 335, "Most names fit: the 95th percentile of 201 to 300 is 295, plus decoration: " + Fit(wide, 1000));
        Check(Fit(wide, 600) == 240, "The column takes at most 40 percent of the window: " + Fit(wide, 600));
        Check(Fit(wide, 500) == 174, "The inspector keeps its minimum beside the divider: " + Fit(wide, 500));
        Check(Fit(wide, 400) == 140, "A window too narrow for both keeps the outliner minimum");
        Check(Fit(new float[0], 1000) == 140, "No rows keep the minimum");
    }

    // The same result twice in a row is one message.
    static void RepeatedMessagesCollapse() {
        var log = new List<string>();
        var feedback = new RailroaderScenePatcher.EditFeedback { Log = log.Add };
        feedback.Refuse("Placement", "Placement", "Inventory is stale");
        feedback.Refuse("Placement", "Placement", "Inventory is stale");
        feedback.Report("Enabled", "Activity change completed.");
        feedback.Refuse("Placement", "Placement", "Inventory is stale");
        Check(feedback.Messages.Select(m => m.Source).SequenceEqual(new[] { "Placement", "Enabled" }) && log.Count == 4, "An identical message is listed once, at the top, and every report is logged: " + String.Join(", ", feedback.Messages.Select(m => m.Source)));
        feedback.Refuse("Placement", "Placement", "enter six numbers");
        feedback.Report("Placement", "Placement completed.");
        Check(feedback.Messages.Select(m => m.Text).SequenceEqual(new[] { "Placement completed.", "Placement refused: enter six numbers", "Placement refused: Inventory is stale" }), "A success sits above the refusal it corrected: " + String.Join(" | ", feedback.Messages.Select(m => m.Text)));
        feedback.Withdraw("Placement", "Placement refused: enter six numbers");
        feedback.Withdraw("Enabled", "Placement completed.");
        Check(feedback.Messages.Select(m => m.Text).SequenceEqual(new[] { "Placement completed.", "Placement refused: Inventory is stale" }), "A withdrawn message leaves and the others stay: " + String.Join(" | ", feedback.Messages.Select(m => m.Text)));
    }

    // The message area shows the last results in fixed slots, newest first, each with the action group it came from.
    static void MessagesKeepTheirSourceNewestFirst() {
        var log = new List<string>();
        var feedback = new RailroaderScenePatcher.EditFeedback { Log = log.Add };
        Check(feedback.Messages.Count == 0, "No message before the first result");
        feedback.Refuse("Track binding", "Track binding", "enter a segment ID");
        feedback.Report("Placement", "Placement completed.");
        Check(feedback.Messages.Select(m => m.Source + ": " + m.Text).SequenceEqual(new[] { "Placement: Placement completed.", "Track binding: Track binding refused: enter a segment ID" }),
            "The newest message is first and each keeps its source: " + String.Join(" | ", feedback.Messages.Select(m => m.Source + ": " + m.Text)));
        Check(feedback.Place == "Placement" && feedback.Result == "Placement completed.", "The newest message is the last result");
        feedback.Report("Gizmo", "Gizmo placement completed.");
        feedback.Report("Window", "Identity copied.");
        Check(feedback.Messages.Count == 3 && feedback.Messages[0].Source == "Window" && feedback.Messages[2].Source == "Placement", "The area keeps the three newest messages");
        Check(log.Count == 4, "Every message is logged once");
        object first = new object(), second = new object();
        feedback.Inspecting(first);
        feedback.Report("Enabled", "Activity change completed.");
        feedback.Inspecting(second);
        Check(feedback.Messages.Count == 0 && feedback.Result == "", "Inspecting another object clears every message");
    }

    // A result or refusal is about the object that was inspected when it was reported.
    static void ResultsBelongToTheInspectedObject() {
        var log = new List<string>();
        var feedback = new RailroaderScenePatcher.EditFeedback { Log = log.Add };
        object first = new object(), second = new object();
        feedback.Inspecting(first);
        feedback.Refuse("placement", "Placement", "Target must resolve uniquely");
        feedback.Inspecting(first);
        Check(feedback.Place == "placement" && feedback.Result == "Placement refused: Target must resolve uniquely", "A result stays while its object stays inspected");
        feedback.Inspecting(null);
        Check(feedback.Result == "Placement refused: Target must resolve uniquely", "A result stays when the inspected object is no longer present");
        feedback.Inspecting(second);
        Check(feedback.Place == null && feedback.Result == "" && log.Count == 1, "Inspecting another object clears the previous object's result without a log line");
        feedback.Report("modification", "Recheck completed.");
        feedback.Inspecting(second);
        Check(feedback.Result == "Recheck completed.", "A result on the newly inspected object stays");
        feedback.Inspecting(first);
        Check(feedback.Result == "", "Returning to the first object does not bring a result back");
    }
}
