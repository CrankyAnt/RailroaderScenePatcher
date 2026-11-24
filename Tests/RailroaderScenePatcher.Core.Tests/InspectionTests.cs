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
    // Edit results are debug lines, so these checks read them with the debug option on.
    internal static void Run() {
        var debug = PatcherLog.DebugEnabled; PatcherLog.DebugEnabled = true;
        try { RunWithDebug(); } finally { PatcherLog.DebugEnabled = debug; }
    }
    static void RunWithDebug() {
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
        var scenePatchFile = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}";
        var result = new PatchEngine(mutable).Apply(scenePatchFile);
        Check(!result.Outcomes["move"].Applied && mutable.MutationCalls == 0 && oldTarget.X == 7 && replacement.X == 9,
            "A replaced target between capture and mutation is rejected without moving either object");
        mutable.BeforeCapture = null; mutable.Objects["p"] = new List<object> { oldTarget };
        mutable.Observations = () => new[] { new SceneObservation(oldTarget, new TargetIdentity("scene", "p", "Culler"), "Board", true,
            oldTarget.Position, oldTarget.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var mutationInventory = new SceneInventory(mutable); mutationInventory.Refresh();
        var expected = mutationInventory.Pin(mutationInventory.Entries.Single());
        oldTarget.X = 8;
        result = new PatchEngine(mutable).Apply(scenePatchFile, "pack", new Dictionary<string, InspectedTarget> { ["move"] = expected });
        Check(!result.Outcomes["move"].Applied && mutable.MutationCalls == 0 && oldTarget.X == 8, "Changed placement since inspection rejects the complete scene patch file edit");
        mutationInventory.Refresh(); expected = mutationInventory.Pin(mutationInventory.Entries.Single());
        result = new PatchEngine(mutable).Apply(scenePatchFile, "pack", new Dictionary<string, InspectedTarget> { ["move"] = expected });
        Check(result.Outcomes["move"].Applied && oldTarget.X == 12, "Fresh inspected targets can apply the intended complete scene patch file");
        mutationInventory.Refresh();
        var definition = mutationInventory.InspectScenePatchFile(scenePatchFile.Replace("\"dependsOn\":[]", "\"dependsOn\":[\"missing-edit\"]")).Single();
        Check(definition.MatchCount == 1 && definition.Problems.Any(p => p.Contains("missing-edit")), "Complete scene patch file inspection reports missing dependencies without applying placement");
        definition = mutationInventory.InspectScenePatchFile(scenePatchFile.Replace("\"path\":\"p\"", "\"path\":\"missing-object\"")).Single();
        Check(definition.MatchCount == 0 && definition.Problems.Any(p => p.Contains("Target")), "A missing target leaves its referring definition inspectable");
        Check(mutationInventory.InspectScenePatchFile("{broken").Single().Problems.Any(p => p.Contains("Unreadable")) && mutable.MutationCalls == 1, "Invalid supplied scene patch files remain visible without mutation or rewrite");
        var mutationCount = mutable.MutationCalls;
        foreach (var change in new Action[] {
            () => mutable.Objects["p"] = new List<object>(),
            () => mutable.Objects["p"] = new List<object> { oldTarget, replacement },
            () => mutable.Maps = new[] { "another-map" }
        }) {
            mutable.Maps = new[] { "map", "layer" }; mutable.Objects["p"] = new List<object> { oldTarget };
            mutable.BeforeCapture = change;
            result = new PatchEngine(mutable).Apply(scenePatchFile);
            Check(!result.Outcomes["move"].Applied && mutable.MutationCalls == mutationCount, "Missing, ambiguous and changed-map targets cannot mutate after capture");
        }
        mutable.BeforeCapture = null; mutable.Maps = new[] { "map", "layer" }; mutable.Objects["p"] = new List<object> { oldTarget };
        var bindings = new[] { "segment=original" };
        mutable.Observations = () => new[] { new SceneObservation(oldTarget, new TargetIdentity("scene", "p", "Culler"), "Board", true,
            oldTarget.Position, oldTarget.Rotation, bindings, new string[0], new string[0], "Fixed placement") };
        mutationInventory.Refresh(); expected = mutationInventory.Pin(mutationInventory.Entries.Single());
        bindings[0] = "segment=rebound";
        result = new PatchEngine(mutable).Apply(scenePatchFile, "pack", new Dictionary<string, InspectedTarget> { ["move"] = expected });
        Check(!result.Outcomes["move"].Applied && mutable.MutationCalls == mutationCount, "Changed binding after inspection cannot authorize a placement edit");
        SteppedFreshness();
        ComponentsLeavingDuringAPass();
        OwnedRuntimeObjectsDoNotMakeTheSnapshotStale();
        AnEquipmentEditAddsNoInventoryEntries();
        EditFeedbackNamesEveryRefusal();
        StaleNoticePromisesOnlyWhatPinRefuses();
        ProblemsFilterLimitsTheOutliner();
        BindingProblemsFollowTheGame();
        InspectionOnlyObjectsHaveTheirOwnGroup();
        ChangesReadOneLinePerSetting();
        SubgroupsFollowTheGame();
        RuntimeStateDoesNotMarkStale();
        RuntimeReferencesAreNotAuthoredSceneState();
        AnEditScansTheSceneTwice();
        AnEditAfterASteppedPassBlocksForOneScan();
        OriginRoundingDoesNotMarkStale();
        TheGuardObservesOnlyTheTarget();
        AnObjectOutsideTheMovingOriginDoesNotMarkStale();
        APlacementOutsideTheMovingOriginKeepsItsMapPosition();
        FocusLandsOnTheObject();
        CullerDrivenActivityDoesNotMarkStale();
        AnOwnEditTakesItsRelatedObservations();
        AComparedSnapshotPinsWithoutAScan();
        FollowUpsKeepTheActionResultVisible();
        MessagesFollowTheirScope();
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
        public string Name, Family; public bool Gone, Active = true, Owned; public Part Refers; public readonly object Handle = new object();
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
                    new string[0], referrers.TryGetValue(part, out var found) ? found.ToArray() : new string[0], new string[0], "Inspection only"),
                part => part.Owned);
        }
    }
    // A paused pass meets components that left the scene since its search. A component no observation depends on, such as
    // a widget of a rebuilt panel, changes no observation and must not mark the snapshot stale; a component an observation
    // depends on must.
    // Crossing equipment the patcher added toggles with culling, initializes later, and appears, moves and disappears with
    // native Undo and Redo. Those are the patcher's own changes: they must not make the snapshot stale, while a change of a
    // map object still must.
    static void OwnedRuntimeObjectsDoNotMakeTheSnapshotStale() {
        var scene = new PartScene();
        Part native, copy, copyOwner;
        scene.Parts.Add(native = new Part { Name = "Grade Crossing/Signal 1/Gate", Family = "Crossing gate" });
        scene.Parts.Add(copy = new Part { Name = "Grade Crossing/CrankyAnt equipment/Signal 1/Gate", Family = "Crossing gate", Owned = true });
        scene.Parts.Add(copyOwner = new Part { Name = "Grade Crossing/CrankyAnt equipment", Owned = true, Refers = native });
        var inventory = new SceneInventory(scene); inventory.Refresh();
        Check(inventory.Entries.Count == 1 && inventory.Entries[0].Identity.Path == native.Name && inventory.Entries[0].Dependencies.Count == 0,
            "Owned objects are neither observed nor read as referrers: " + String.Join(", ", inventory.Entries.Select(e => e.Identity.Path)));
        copy.Active = false; inventory.CheckFreshness();
        Check(!inventory.IsStale, "Culling the added copy keeps the snapshot fresh: " + inventory.StaleReason);
        copy.Active = true; copy.Gone = true; inventory.CheckFreshness();
        Check(!inventory.IsStale, "Removing the added copy, as an Undo does, keeps the snapshot fresh: " + inventory.StaleReason);
        scene.Parts.Add(new Part { Name = "Grade Crossing/CrankyAnt equipment 2/Signal 1/Gate", Family = "Crossing gate", Owned = true }); inventory.CheckFreshness();
        Check(!inventory.IsStale, "Rebuilding an added copy, as a Redo does, keeps the snapshot fresh: " + inventory.StaleReason);
        native.Active = false; inventory.CheckFreshness();
        Check(inventory.IsStale && inventory.StaleReason.StartsWith("Gate changed", StringComparison.Ordinal), "A change of a map object still marks the snapshot stale: " + inventory.StaleReason);
    }
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
        Check(checkedInventory.IsStale && checkedInventory.StaleReason.Contains("Sign"), "A referrer that leaves during a pass marks the snapshot stale and names the changed observation: " + checkedInventory.StaleReason);
        Check(Refused(() => checkedInventory.Pin(checkedInventory.Entries[1])), "A stale snapshot still refuses a pin");
        checkedInventory.Refresh();
        Check(!checkedInventory.IsStale && checkedInventory.StaleReason == null && checkedInventory.Entries[0].Dependencies.Count == 0, "Refresh observes the scene without the referrer and clears the reason");

        // A failing scan and a changed value keep their meaning and name it.
        parts = Parts(out checkedInventory, out widget, out sign, out holder, out board);
        sign.Active = false;
        checkedInventory.AdvanceFreshnessCheck(() => true);
        Check(checkedInventory.IsStale && checkedInventory.StaleReason.Contains("Sign"), "A changed value names its observation: " + checkedInventory.StaleReason);
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
        Check(notice == "not yet scanned. Click Refresh to update the object list. " + refused && Refused(() => inventory.Pin(null)), "An unscanned inventory refuses edits and says so: " + notice);
        inventory.Refresh();
        notice = RailroaderScenePatcher.InventoryPresentationState.Notice(inventory, false);
        Check(notice == "The object list is up to date. You can inspect objects and start edits.", "A current inventory with current definitions is a snapshot: " + notice);
        notice = RailroaderScenePatcher.InventoryPresentationState.Notice(inventory, true);
        Check(notice == "the supplied definitions changed or were not read. Click Refresh to update the object list." && inventory.Pin(inventory.Entries.Single()) != null,
            "Changed definitions alone are named as stale without a refusal the pin does not make: " + notice);
        box.X = 8; inventory.CheckFreshness();
        foreach (var definitionsStale in new[] { false, true }) {
            notice = RailroaderScenePatcher.InventoryPresentationState.Notice(inventory, definitionsStale);
            Check(notice == "p changed (position). Click Refresh to update the object list. " + refused && Refused(() => inventory.Pin(inventory.Entries.Single())),
                "A stale inventory names its own reason and the refusal, whatever the definitions: " + notice);
        }
        notice = RailroaderScenePatcher.InventoryPresentationState.Notice(null, false);
        Check(notice == "inspection is unavailable. Click Refresh to update the object list. " + refused, "A missing inventory refuses edits and says so: " + notice);
    }

    // Every edit that is refused or ignored names its reason for the panel and the log.
    static void EditFeedbackNamesEveryRefusal() {
        var scene = new Scene(); var box = new Box { X = 7 };
        scene.Objects["p"] = new List<object> { box };
        scene.Observations = () => new[] { new SceneObservation(box, new TargetIdentity("scene", "p", "Culler"), "Board", true, box.Position, box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var scenePatchFile = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}";
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var log = new List<string>(); var calls = 0;
        var feedback = new RailroaderScenePatcher.EditFeedback { Describe = text => { log.Add(text); return text; } };
        Func<InspectedTarget, Outcome> move = inspection => { calls++; return new PatchEngine(scene).Apply(scenePatchFile, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inspection }).Outcomes["move"]; };

        box.X = 8;
        Check(!feedback.Run("placement", "Placement", null, inventory, inventory.Entries.Single(), move) && calls == 0 && box.X == 8, "A stale inventory refuses the edit before it runs");
        Check(feedback.Place == "placement" && feedback.Result == "Placement refused. The object list is stale (p changed (position)); refresh and inspect again." && log.Single() == feedback.Result, "The stale refusal names its reason for the panel and the log: " + feedback.Result);
        inventory.Refresh(); log.Clear();
        Check(!feedback.Run("binding", "Track binding", "a world selection is pending; confirm or cancel it first", inventory, inventory.Entries.Single(), move) && calls == 0,
            "An edit during a pending interaction does not run");
        Check(feedback.Place == "binding" && feedback.Result == "Track binding refused. A world selection is pending; confirm or cancel it first." && log.Single() == feedback.Result, "The ignored edit names the pending interaction: " + feedback.Result);
        log.Clear();
        Check(!feedback.Run("placement", "Placement", null, null, null, move) && feedback.Result.StartsWith("Placement refused. ", StringComparison.Ordinal) && log.Count == 1 && calls == 0, "An edit without an inspected object names that: " + feedback.Result);
        log.Clear();
        Check(!feedback.Run("placement", "Placement", null, inventory, inventory.Entries.Single(), inspection => throw new InvalidOperationException("injected edit failure")) &&
            feedback.Result == "Placement refused. Injected edit failure." && log.Single() == feedback.Result, "A failing edit names its failure: " + feedback.Result);
        log.Clear();
        scene.FailApply = true;
        Check(!feedback.Run("placement", "Placement", null, inventory, inventory.Entries.Single(), move) && calls == 1 && feedback.Result.StartsWith("Placement failed and was undone. Injected.", StringComparison.Ordinal) && feedback.Recovery?.Kind == FailureKind.Restored && log.Single() == feedback.Result,
            "A failure after mutation is reported as undone, not as a refusal, with the engine's reason: " + feedback.Result);
        scene.FailApply = false; log.Clear(); inventory.Refresh();
        feedback.Refuse("binding", "Track binding", "enter a segment ID");
        Check(feedback.Place == "binding" && feedback.Result == "Track binding refused. Enter a segment ID." && log.Single() == feedback.Result, "A refusal of entered values is logged like any other");
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
        SceneObservation Entry(string path, string family, string displayName = null, params string[] problems) => new SceneObservation(new object(), new TargetIdentity("scene", path, "Component"), family, true,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new string[0], new string[0], problems, "Inspection only", displayName: displayName);
        var fixture = new Fixture();
        fixture.Objects.Add(Entry("World/Board", "Independent board"));
        fixture.Objects.Add(Entry("World/Sign", "Track marker", "Added Alpha", "Broken binding"));
        fixture.Objects.Add(Entry("World/Post", "Track marker", "Added Zulu", "Segment missing"));
        fixture.Objects.Add(Entry("World/Bridges/Ela", "Bridge"));
        var inventory = new SceneInventory(fixture); inventory.Refresh();
        Func<SceneObservation, bool> changed = entry => entry.Identity.Path == "World/Board" || entry.Identity.Path == "World/Post";
        string[] Paths(string search, RailroaderScenePatcher.OutlinerFilter filter) => RailroaderScenePatcher.InventoryPresentationState.Rows(inventory, search, filter, changed).Select(e => e.Identity.Path).ToArray();
        var all = RailroaderScenePatcher.OutlinerFilter.All; var problems = RailroaderScenePatcher.OutlinerFilter.Problems; var modified = RailroaderScenePatcher.OutlinerFilter.Changed;
        Check(Paths("", problems).SequenceEqual(new[] { "World/Sign", "World/Post" }), "The Problems filter limits the outliner to objects with detected problems");
        Check(Paths("", modified).SequenceEqual(new[] { "World/Board", "World/Post" }), "The Changed filter limits the outliner to objects this mod modifies");
        Check(Paths("", all).SequenceEqual(new[] { "World/Board", "World/Sign", "World/Post", "World/Bridges/Ela" }), "All shows every inventory entry");
        Check(Paths("post", problems).SequenceEqual(new[] { "World/Post" }) && Paths("board", problems).Length == 0 && Paths("sign", modified).Length == 0, "Search narrows within a filter");
        Check(Paths("marker", all).SequenceEqual(new[] { "World/Sign", "World/Post" }) && Paths("Added Alpha", all).SequenceEqual(new[] { "World/Sign" })
            && Paths("World/Post", all).SequenceEqual(new[] { "World/Post" }), "Search finds visible names while keeping the original identity path searchable");
        Check(inventory.Entries.Count == 4 && !inventory.IsStale, "Filtering changes neither the inventory nor its freshness");
        Check(RailroaderScenePatcher.InventoryPresentationState.Rows(null, "", problems, changed).Count == 0, "A missing inventory has no rows");
        // The Disabled filter lists the objects whose own active flag is off: not one that is inactive through a parent.
        var switched = new Fixture();
        SceneObservation Flagged(string path, bool active, bool? enabled) => new SceneObservation(new object(), new TargetIdentity("scene", path, "Component"), "Track marker", active,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new string[0], new string[0], new string[0], "Inspection only", null, enabled);
        switched.Objects.Add(Flagged("World/On", true, null));
        switched.Objects.Add(Flagged("World/Off", false, null));
        switched.Objects.Add(Flagged("World/Parent off", false, true));
        switched.Objects.Add(Flagged("World/Own off", false, false));
        var flags = new SceneInventory(switched); flags.Refresh();
        Check(RailroaderScenePatcher.InventoryPresentationState.Rows(flags, "", RailroaderScenePatcher.OutlinerFilter.Disabled, null).Select(e => e.Identity.Path).SequenceEqual(new[] { "World/Off", "World/Own off" }),
            "The Disabled filter lists objects that are switched off themselves");
        Check(flags.Entries[0].Enabled && !flags.Entries[1].Enabled && flags.Entries[2].Enabled && !flags.Entries[2].Active, "Without a value the own flag is the activity; an object can be enabled under an inactive parent");
        Check(RailroaderScenePatcher.InventoryPresentationState.Summary(flags, "", RailroaderScenePatcher.OutlinerFilter.Disabled, null) == "2 of 4 objects", "The count line counts the disabled objects");
        Check(Flagged("World/X", false, true).Difference(Flagged("World/X", false, false)) == "identity",
            "The own flag is no part of what an inspection pins: for a culler-listed object its reading follows the camera");
        Check(RailroaderScenePatcher.CommandBand.AllFilter == "All" && RailroaderScenePatcher.CommandBand.ChangedFilter == "Changed" && RailroaderScenePatcher.CommandBand.DisabledFilter == "Disabled"
            && RailroaderScenePatcher.CommandBand.ProblemsFilter == "Problems", "The four filters are All, Changed, Disabled and Problems");
        var names = new[] { RailroaderScenePatcher.CommandBand.ChangedFilter, RailroaderScenePatcher.CommandBand.DisabledFilter, RailroaderScenePatcher.CommandBand.ProblemsFilter };
        Check(names.SequenceEqual(names.OrderBy(name => name, StringComparer.Ordinal)), "After All the filters stand by name");
        // Four native segments, each its word and 12 units of padding, need 292.4 units; the outliner column holds them
        // and its band at its default and least width.
        var segments = RailroaderScenePatcher.CommandBand.FilterSegments;
        Check(segments.Length == 4 && Math.Abs(segments.Sum() - 292.39f) < 0.01f, "The four filter segments need 292.4 units: " + segments.Sum());
        Check(segments.Sum() <= RailroaderScenePatcher.CommandBand.OutlinerWidth && RailroaderScenePatcher.CommandBand.OutlinerWidth == 296, "The outliner column holds the four filters without cutting a word");
        Check(segments.Sum() > 252 && segments.Sum() - (77.88f + 12) + (49.56f + 12) > 252, "At the earlier width of 252 neither Problems nor the shorter Issues fits beside the other three");
        Check(RailroaderScenePatcher.CommandBand.WordsFit(RailroaderScenePatcher.CommandBand.OutlinerWidth), "The lamps' words fit the band at that width");
        // A disabled object triggers no problem of any kind, and stays reachable through the Disabled filter.
        var every = new[] { "Marker broken binding: missing segment Sgone", "Missing script in inspected hierarchy", "Lower broken binding: missing segment S9", "Inspection unavailable: no graph" };
        Check(BindingRules.Problems(true, every, new string[0]).SequenceEqual(every), "An enabled object keeps every problem");
        Check(BindingRules.Problems(false, every, new string[0]).Count == 0 && BindingRules.Problems(false, every, null).Count == 0, "A disabled object triggers no problem of any kind");
        Check(BindingRules.Problems(false, every, new[] { every[0] }).SequenceEqual(new[] { every[0] }), "What the game reads whatever the activity stays listed for a disabled object");
        Check(BindingRules.Problems(true, null, null).Count == 0, "No problems give an empty list");
        // An object on a culler's list: off while its culler shows its objects is disabled; off while the culler hides
        // them is the culler's doing.
        Check(AuthoredActivity.Enabled(true, false, false) && !AuthoredActivity.Enabled(false, false, false), "Without a culler the own flag is what it says");
        Check(AuthoredActivity.Enabled(true, true, true) && AuthoredActivity.Enabled(true, true, false), "An object that is on is enabled, whatever its culler does");
        Check(AuthoredActivity.Enabled(false, true, false), "Off while its culler hides its objects: camera culling does not disable an object");
        Check(!AuthoredActivity.Enabled(false, true, true), "Off while its culler shows its objects: authored off or disabled by a patch");
        Check(RailroaderScenePatcher.InventoryPresentationState.Rows(inventory, "", modified, null).Count == 0, "Without a scene patch file nothing is changed");
        Check(RailroaderScenePatcher.InventoryPresentationState.Summary(inventory, "", problems, changed) == "2 of 4 objects", "The count line names how many objects the filters show: " + RailroaderScenePatcher.InventoryPresentationState.Summary(inventory, "", problems, changed));
        Check(RailroaderScenePatcher.InventoryPresentationState.Summary(inventory, "", all, changed) == "4 of 4 objects", "The unfiltered outliner counts every entry");
        var groups = RailroaderScenePatcher.InventoryPresentationState.Groups(RailroaderScenePatcher.InventoryPresentationState.Rows(inventory, "", all, changed));
        Check(groups.Select(g => g.Key + ":" + String.Join(",", g.Value.Select(RailroaderScenePatcher.InventoryPresentationState.Name))).SequenceEqual(new[] { "Bridge:Ela", "Independent board:Board", "Track marker:Added Alpha,Added Zulu" })
            && groups.Last().Value.Select(row => row.Identity.Path).SequenceEqual(new[] { "World/Sign", "World/Post" }),
            "Rows sort by their visible names within each family while retaining their original identity paths");
        var presentation = new RailroaderScenePatcher.InventoryPresentationState();
        Check(presentation.Observe(inventory, null, "", all, "1") && !presentation.Observe(inventory, null, "", all, "1"), "A settled outliner does not rebuild");
        Check(presentation.Observe(inventory, null, "", problems, "1"), "Switching a filter rebuilds the outliner");
        Check(!presentation.Observe(inventory, null, "", problems, "1") && presentation.Observe(inventory, null, "", all, "1"), "Returning to All rebuilds the outliner once");
        Check(presentation.Observe(inventory, null, "", all, "2") && !presentation.Observe(inventory, null, "", all, "2"), "A changed scene patch file rebuilds the outliner so its Changed markers follow Undo and Redo");
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

    // A reference held in a field Unity serializes is authored scene state. A reference in any other field, such as a
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
    static void RuntimeReferencesAreNotAuthoredSceneState() {
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
        var scenePatchFile = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}";
        var inventory = new SceneInventory(scene); inventory.Refresh();
        scans = 0;
        var result = new PatchEngine(scene).Apply(scenePatchFile, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inventory.PinForEdit(inventory.Entries.Single()) });
        Check(result.Outcomes["move"].Applied && box.X == 12 && scans == 2, "An edit pinned for immediate use scans the scene for the pin and for the guard: " + scans);
        inventory.Refresh(); scans = 0;
        result = new PatchEngine(scene).Apply(scenePatchFile, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inventory.Pin(inventory.Entries.Single()) });
        Check(result.Outcomes["move"].Applied && scans == 3, "A pin kept for later validates at resolution as well: " + scans);
        // The guard still reads the scene: a change after the pin and before the mutation refuses the edit.
        inventory.Refresh(); box.X = 12; var mutations = scene.MutationCalls;
        var token = inventory.PinForEdit(inventory.Entries.Single());
        scene.BeforeCapture = () => box.Position[1] = 99;
        result = new PatchEngine(scene).Apply(scenePatchFile.Replace("112", "120"), "pack", new Dictionary<string, InspectedTarget> { ["move"] = token });
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
        var scenePatchFile = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}";
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var passes = inventory.CompletedPasses;
        bool refused = false;
        try { inventory.PinAfterPass(inventory.Entries.Single(), passes); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Without a completed pass since the edit was requested there is no pin");
        while (inventory.CompletedPasses == passes && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        Check(inventory.CompletedPasses == passes + 1 && !inventory.IsStale, "A pass over an unchanged scene completes");
        scans = 0;
        var result = new PatchEngine(scene).Apply(scenePatchFile, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inventory.PinAfterPass(inventory.Entries.Single(), passes) });
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
        result = new PatchEngine(scene).Apply(scenePatchFile.Replace("112", "130"), "pack", new Dictionary<string, InspectedTarget> { ["move"] = token });
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
        Check(inventory.IsStale && inventory.StaleReason == "T-60 changed (position)", "A move of ten centimetres marks the snapshot stale and the reason names the position: " + inventory.StaleReason);
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
        var scenePatchFile = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}";
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var passes = inventory.CompletedPasses;
        while (inventory.CompletedPasses == passes && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        scene.Discoveries = 0; scene.Observations = 0;
        var result = new PatchEngine(scene).Apply(scenePatchFile, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inventory.PinAfterPass(inventory.Entries.Single(), passes) });
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
        result = new PatchEngine(scene).Apply(scenePatchFile.Replace("112", "130"), "pack", new Dictionary<string, InspectedTarget> { ["move"] = token });
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
        Check(inventory.IsStale && inventory.StaleReason == "marker changed (position)", "A move of the object the game does not shift is detected: " + inventory.StaleReason);
        inventory.Refresh(); moved[2] += 1;
        inventory.CheckFreshness();
        Check(inventory.IsStale && inventory.StaleReason == "board changed (position)", "A move of a shifted object is detected after the origin moved: " + inventory.StaleReason);
        var refused = false;
        try { MapOrigin.Position(moved, new double[2], true); } catch (ArgumentException) { refused = true; }
        Check(refused, "An offset that does not fit the position is refused");
    }

    // Focus, the displayed placement and a placement write use one map position. For an object the game does not shift
    // with its origin, such as a starter marker under Setups, that is its scene position at every origin: converting it
    // like a shifted object put the camera off the map and would write the origin offset into the scene.
    static void APlacementOutsideTheMovingOriginKeepsItsMapPosition() {
        // starter-br-coaling: serialized and scene position 4174.06 / 528.62 / 5327.77, origin offset at the review -4500 / 0 / -5500.
        var scene = new double[] { 4174.06, 528.62, 5327.77 }; var offset = new double[] { -4500, 0, -5500 };
        Check(MapOrigin.Position(scene, offset, false).SequenceEqual(scene), "An object outside the moving origin is at its scene position on the map");
        Check(MapOrigin.ScenePosition(scene, offset, false).SequenceEqual(scene), "A map placement outside the moving origin is written as that scene position");
        Check(!MapOrigin.ScenePosition(scene, offset, false).SequenceEqual(new double[] { -325.94, 528.62, -172.23 }), "No origin offset enters the scene position of an object the game does not shift");
        // A board under World at map 1000 / 500 / 2000: the game has shifted it by the offset.
        var map = new double[] { 1000, 500, 2000 };
        Check(MapOrigin.ScenePosition(map, offset, true).SequenceEqual(new double[] { -3500, 500, -3500 }), "A shifted object's map placement is written at the scene position of the current origin");
        Check(MapOrigin.Position(MapOrigin.ScenePosition(map, offset, true), offset, true).SequenceEqual(map), "A shifted object's written placement reads back as its map position");
        var refused = false;
        try { MapOrigin.ScenePosition(map, new double[2], true); } catch (ArgumentException) { refused = true; }
        Check(refused, "A write with an offset that does not fit the position is refused");
        // The gizmo stands at the object's marking: its map position at the current origin, as the world marking and
        // Focus place it, whether or not the game moves the object with the origin. Three tiles of origin change shift
        // the gizmo by the full change, so it stays at the marking; writes still keep the object's own rule.
        var change = new double[] { -1500, 0, 500 };
        var moved = offset.Select((v, i) => v + change[i]).ToArray();
        var before = MapOrigin.MarkPosition(scene, offset);
        Check(before.Select((v, i) => Math.Abs(v - new double[] { -325.94, 528.62, -172.23 }[i])).Max() < 1e-9,
            "The gizmo of an object outside the moving origin stands at its marking, not at its scene position");
        var shifted = before.Select((v, i) => v + change[i]).ToArray();
        Check(shifted.Select((v, i) => Math.Abs(v - MapOrigin.MarkPosition(scene, moved)[i])).Max() < 1e-9,
            "After an origin change the gizmo of an object outside the moving origin stands at its marking");
        Check(MapOrigin.ScenePosition(scene, moved, false).SequenceEqual(scene),
            "After an origin change the object outside the moving origin is still written at its scene position");
        // An object inside the moving world: its marking is where the game keeps it, so its gizmo stands on it as before.
        Check(MapOrigin.MarkPosition(map, offset).SequenceEqual(MapOrigin.ScenePosition(map, offset, true)) &&
            MapOrigin.MarkPosition(map, moved).SequenceEqual(MapOrigin.ScenePosition(map, moved, true)),
            "The gizmo of a shifted object stands on the object before and after an origin change");
    }

    // Focus moves the camera to where the object is on the map. A track span is its track locations, not its transform:
    // Ops/OpsTrackSpans/Span-wh-e-engine.span-(1) stands at the scene origin, and Focus put the camera at x = 0, z = 0.
    static void FocusLandsOnTheObject() {
        var origin = new double[] { 0, 0, 0 };
        var lower = new double[] { 12700, 560, 4600 }; var upper = new double[] { 12900, 564, 4700 };
        Check(FocusTarget.Point("Track span", origin, new[] { lower, upper }, out var refusal).SequenceEqual(new double[] { 12800, 562, 4650 }) && refusal == null,
            "Focus on a track span lands midway between its track locations");
        Check(FocusTarget.Point("Track span", origin, new[] { upper }, out refusal).SequenceEqual(upper) && refusal == null,
            "Focus on a track span with one resolved location lands on it");
        Check(FocusTarget.Point("Track span", origin, new double[0][], out refusal) == null && refusal == "the span's track locations do not resolve",
            "Focus on a track span without a resolved location is refused with its reason");
        var board = new double[] { 5306.8, 529.95, 5737.6 };
        Check(FocusTarget.Point("Independent board", board, new double[0][], out refusal).SequenceEqual(board) && refusal == null,
            "Focus on any other object lands on its map position");
        Check(FocusTarget.Point("Track span", origin, new[] { new double[] { double.NaN, 0, 0 } }, out refusal) == null && refusal != null,
            "a track location without a finite position does not count");
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
        Check(inventory.IsStale && inventory.StaleReason == "Signal 1 changed (activity)", "Switching the signal itself off is detected: " + inventory.StaleReason);
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
    // A scene that also holds objects the patcher created at runtime.
    sealed class OwningScene : IRelatedInspectionScene, IOwnedObjectScene {
        public readonly RelatedScene Inner = new RelatedScene();
        public readonly HashSet<object> Owned = new HashSet<object>();
        public IEnumerable<string> MapIds => Inner.MapIds;
        public IEnumerable<SceneObservation> Discover() => Inner.Discover();
        public SceneObservation Observe(object handle, TargetIdentity identity) => Inner.Observe(handle, identity);
        public IEnumerable<SceneObservation> ObserveRelated(object handle, TargetIdentity identity) => Inner.ObserveRelated(handle, identity);
        public bool Present(object handle) => Inner.Present(handle);
        public bool IsOwned(object handle) => Owned.Contains(handle);
    }
    // Applying a light or gate copies a lamp and a gate beneath the crossing. The re-observation of the crossing's
    // hierarchy after the edit meets them, and so would any discovery that does not leave them out; they are the
    // patcher's own state. The snapshot keeps its count, and the next check agrees with the scene.
    static void AnEquipmentEditAddsNoInventoryEntries() {
        object crossing = new object(), lamp = new object(), copyLamp = new object(), copyGate = new object();
        SceneObservation Of(object handle, string path, string family) => new SceneObservation(handle, new TargetIdentity("scene", path, "Component"), family, true,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new string[0], new string[0], new string[0], "Inspection only");
        var scene = new OwningScene();
        scene.Inner.All.AddRange(new[] { Of(crossing, "crossing", "Crossing controller"), Of(lamp, "crossing/Signal 1", "Crossing light") });
        scene.Inner.Related = handle => scene.Inner.All.ToArray();
        var inventory = new SceneInventory(scene); inventory.Refresh();
        Check(inventory.Entries.Count == 2, "The snapshot holds the crossing and its native light");
        scene.Owned.Add(copyLamp); scene.Owned.Add(copyGate);
        scene.Inner.All.Add(Of(copyLamp, "crossing/CrankyAnt equipment/Signal 1", "Crossing light"));
        scene.Inner.All.Add(Of(copyGate, "crossing/CrankyAnt equipment/Signal 1/Gate", "Crossing gate"));
        var updated = inventory.ReobserveRelated(inventory.Entries[0]);
        Check(updated != null && inventory.Entries.Count == 2 && !inventory.Entries.Any(e => scene.Owned.Contains(e.Handle)), "The edit's re-observation adds no entry for the copy: " + inventory.Entries.Count);
        inventory.CheckFreshness();
        Check(!inventory.IsStale, "The next check agrees with the scene: " + inventory.StaleReason);
        while (inventory.CompletedPasses == 0 && !inventory.IsStale) inventory.AdvanceFreshnessCheck(() => false);
        Check(!inventory.IsStale, "A stepped pass agrees as well: " + inventory.StaleReason);
        inventory.Refresh();
        Check(inventory.Entries.Count == 2, "A refresh does not count the copy either: " + inventory.Entries.Count);
        // The addition itself is an added object: its own entry is taken by the edit's re-observation and by a refresh
        // alike, so the count stays consistent; only the copy's parts stay out.
        object wrapper = new object();
        scene.Inner.All.Add(Of(wrapper, "crossing/CrankyAnt equipment", "Added light or gate"));
        updated = inventory.ReobserveRelated(inventory.Entries[0]);
        Check(updated != null && inventory.Entries.Count == 3 && inventory.Entries.Any(e => ReferenceEquals(e.Handle, wrapper)), "The added object enters with the edit: " + inventory.Entries.Count);
        inventory.CheckFreshness();
        Check(!inventory.IsStale, "The next check agrees: " + inventory.StaleReason);
        inventory.Refresh();
        Check(inventory.Entries.Count == 3, "A refresh counts the added object once and its parts not at all: " + inventory.Entries.Count);
    }
    // An edit of a crossing's coverage changes the crossing, changes the bell that reads its circuits and adds spans the
    // patcher owns; clearing it removes those spans again. The inventory follows without discovering the scene.
    static void AnOwnEditTakesItsRelatedObservations() {
        object crossing = new object(), bell = new object(), other = new object(), span = new object();
        SceneObservation Of(object handle, string path, string binding) => new SceneObservation(handle, new TargetIdentity("scene", path, "Component"), "Family", true,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new[] { binding }, new string[0], new string[0], "Inspection only");
        var scene = new RelatedScene();
        scene.All.AddRange(new[] { Of(crossing, "crossing", "detection spans"), Of(bell, "crossing/bell", "detection spans"), Of(other, "other", "none") });
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
        scene.All.RemoveAt(3); scene.All[0] = Of(crossing, "crossing", "detection spans"); scene.All[1] = Of(bell, "crossing/bell", "detection spans");
        updated = inventory.ReobserveRelated(updated);
        Check(updated != null && inventory.Entries.Count == 3 && inventory.Entries.Any(e => ReferenceEquals(e, unrelated)), "An object the change removed leaves the inventory: " + inventory.Entries.Count);
        inventory.CheckFreshness();
        Check(!inventory.IsStale, "The inventory without the removed object agrees with the scene: " + inventory.StaleReason);
        // A change of an unrelated object is still found.
        scene.All[2] = Of(other, "other", "changed");
        inventory.ReobserveRelated(updated);
        inventory.CheckFreshness();
        Check(inventory.IsStale && inventory.StaleReason.StartsWith("other changed (bindings", StringComparison.Ordinal), "A change of an unrelated object still marks the inventory stale: " + inventory.StaleReason);
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
        var scenePatchFile = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"scene\",\"path\":\"p\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}";
        var inventory = new SceneInventory(scene); inventory.Refresh();
        scene.Discoveries = 0; scene.Observations = 0;
        var result = new PatchEngine(scene).Apply(scenePatchFile, "pack", new Dictionary<string, InspectedTarget> { ["move"] = inventory.PinCompared(inventory.Entries.Single()) });
        Check(result.Outcomes["move"].Applied && box.X == 12 && scene.Discoveries == 0 && scene.Observations >= 1, "The edit runs without a discovery and observes the target: " + scene.Discoveries + "/" + scene.Observations);
        var entry = inventory.Reobserve(inventory.Entries.Single());
        var token = inventory.PinCompared(entry); var mutations = scene.Inner.MutationCalls;
        scene.Inner.BeforeCapture = () => box.Position[1] = 99;
        result = new PatchEngine(scene).Apply(scenePatchFile.Replace("112", "130"), "pack", new Dictionary<string, InspectedTarget> { ["move"] = token });
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

    // The message area shows the result of the last action and the latest follow-up of that action above it.
    static void FollowUpsKeepTheActionResultVisible() {
        var log = new List<string>();
        var feedback = new RailroaderScenePatcher.EditFeedback { Describe = text => { log.Add(text); return text; } };
        string Line(RailroaderScenePatcher.EditFeedback.Message m) => m.Source + ": " + m.Text;
        feedback.Follow("Terrain output", "Terrain output complete.");
        Check(feedback.Messages.Select(Line).SequenceEqual(new[] { "Terrain output: Terrain output complete." }), "A follow-up without a result is shown");
        feedback.Report("Placement", "Placement completed.");
        Check(feedback.Messages.Select(Line).SequenceEqual(new[] { "Placement: Placement completed." }), "A result takes the place of what was shown before it");
        feedback.Report("Activity", "Enable completed.");
        feedback.Follow("Terrain output", "Terrain output outstanding.");
        feedback.Follow("Terrain output", "Terrain output complete.");
        Check(feedback.Messages.Select(Line).SequenceEqual(new[] { "Terrain output: Terrain output complete.", "Activity: Enable completed." }),
            "The latest follow-up stands above the result it follows; the result of the action before is gone: " + String.Join(" | ", feedback.Messages.Select(Line)));
        Check(feedback.Messages.Count(m => m.FollowUp) == 1 && log.Count == 5 && feedback.Result == "Terrain output complete.", "Only the latest follow-up is kept, and every message is logged");
        feedback.Report("Activity", "Disable completed.");
        Check(feedback.Messages.Select(Line).SequenceEqual(new[] { "Activity: Disable completed." }), "A new result also removes the follow-up of the action before");
    }

    // A result of a tab's own action leaves the message area with its tab or mode; a result of a general action, such as
    // the footer's Remove modification and Recheck, stays through a tab switch. Both go with their object, on a new
    // action and on a history move.
    static void MessagesFollowTheirScope() {
        var feedback = new RailroaderScenePatcher.EditFeedback();
        feedback.GeneralPlaces.Add("Modification");
        object board = new object(), sign = new object();
        feedback.Inspecting(board);
        feedback.View("object/0");
        feedback.Report("Activity", "Disable completed.");
        Check(!feedback.Messages[0].General, "A tab's action is tab-scoped");
        feedback.View("changes/0");
        Check(feedback.Messages.Count == 0, "A tab's result leaves with its tab");
        feedback.Report("Modification", "Removal completed.");
        Check(feedback.Messages[0].General, "The footer's action is general");
        feedback.View("placement/0"); feedback.View("placement/2"); feedback.View("object/0");
        Check(feedback.Result == "Removal completed.", "A general result stays through tab and mode changes");
        // A follow-up of a tab-scoped place leaves with the tab, and the general result below it stays.
        feedback.Follow("Terrain output", "Terrain output complete.");
        feedback.View("connections/0");
        Check(feedback.Messages.Count == 1 && feedback.Result == "Removal completed.", "A tab-scoped follow-up leaves and the general result stays: " + feedback.Messages.Count);
        feedback.Report("Activity", "Enable completed.");
        Check(feedback.Messages.Count == 1 && feedback.Result == "Enable completed.", "A new action replaces a general result too");
        feedback.Report("Modification", "Recheck completed.");
        feedback.HistoryMoved();
        Check(feedback.Messages.Count == 0, "A history move removes a general result");
        feedback.Report("Modification", "Recheck completed.");
        feedback.Inspecting(sign);
        Check(feedback.Messages.Count == 0, "Another object removes a general result");
    }

    // Whether a track binding is a problem follows what the game needs from the kind of object: the game warns only for
    // an enabled marker, and reads a pad's and a referenced starter marker's binding.
    static void BindingProblemsFollowTheGame() {
        // The kinds.
        Check(BindingRules.Need("Generic", true, false) == BindingNeed.Required && BindingRules.Need("Crossing", false, false) == BindingNeed.Required, "A crossing pad needs its binding");
        Check(BindingRules.Need("Generic", false, true) == BindingNeed.ReadAnyway && BindingRules.Need("Crossing", true, true) == BindingNeed.ReadAnyway, "A starter marker of an active setup is read whatever its activity");
        Check(BindingRules.Need("Generic", false, false) == BindingNeed.Optional && BindingRules.Need("PassengerStop", false, false) == BindingNeed.Optional, "A sign, a milepost and an unreferenced starter marker need no binding");
        Check(BindingRules.Need("Flare", false, false) == BindingNeed.Never, "A flare marker has no binding problem");
        Check(!BindingRules.Listed("Signal", false, false) && !BindingRules.Listed("Generic", true, false) && !BindingRules.Listed("Generic", false, true) && BindingRules.Listed("Generic", false, false) && BindingRules.Listed("Crossing", false, false),
            "Signals, native map objects and the builder photo marker are not listed");
        string Problem(BindingNeed need, bool enabled, string segment, bool? resolves) => BindingRules.Problem(need, enabled, "Marker", segment, resolves);
        // An inactive object is never a problem.
        foreach (var need in new[] { BindingNeed.Never, BindingNeed.Optional, BindingNeed.Required })
            Check(Problem(need, false, "", false) == null && Problem(need, false, "Sgone", false) == null && BindingRules.Unknown(need, false, "Marker", "S1", null, "no graph") == null,
                "A disabled object has no binding problem: " + need);
        // A required binding: a problem when enabled and empty or dangling.
        Check(Problem(BindingNeed.Required, true, "", false) == "Marker has an empty segment binding" && Problem(BindingNeed.Required, true, " ", false) == "Marker has an empty segment binding", "An enabled pad without a binding is a problem");
        Check(Problem(BindingNeed.Required, true, "Sgone", false) == "Marker broken binding: missing segment Sgone", "An enabled pad whose segment is gone is a problem");
        Check(Problem(BindingNeed.Required, true, "S1", true) == null, "A resolving binding is no problem");
        // A referenced starter marker: a problem whatever its own activity.
        foreach (var enabled in new[] { true, false }) {
            Check(Problem(BindingNeed.ReadAnyway, enabled, "", false) == "Marker has an empty segment binding; a new-game setup reads this marker whatever its activity", "A referenced starter marker without a binding is a problem, enabled " + enabled);
            Check(Problem(BindingNeed.ReadAnyway, enabled, "Sgone", false) == "Marker broken binding: missing segment Sgone; a new-game setup reads this marker whatever its activity", "A referenced starter marker with a missing segment is a problem, enabled " + enabled);
            Check(Problem(BindingNeed.ReadAnyway, enabled, "S1", true) == null, "A referenced starter marker with a resolving binding is no problem");
        }
        // An optional binding: empty is information, dangling is a problem when enabled.
        Check(Problem(BindingNeed.Optional, true, "", false) == null, "An enabled sign without a binding is no problem");
        Check(Problem(BindingNeed.Optional, true, "Sgone", false) == "Marker broken binding: missing segment Sgone", "An enabled sign whose stored segment is gone is a problem");
        Check(Problem(BindingNeed.Never, true, "", false) == null && Problem(BindingNeed.Never, true, "Sgone", false) == null, "A kind without a binding has no binding problem");
        // An unreadable graph is reported only where a missing segment would be a problem.
        Check(BindingRules.Unknown(BindingNeed.Optional, true, "Marker", "S1", null, "no graph") == "Marker binding validity unknown: no graph" && Problem(BindingNeed.Optional, true, "S1", null) == null, "An unreadable graph is an inspection failure, not a broken binding");
        Check(BindingRules.Unknown(BindingNeed.Optional, true, "Marker", "", false, "no graph") == null && BindingRules.Unknown(BindingNeed.Never, true, "Marker", "S1", null, "no graph") == null
            && BindingRules.Unknown(BindingNeed.ReadAnyway, false, "Marker", "S1", null, "no graph") != null, "The failure is reported where the binding is read");
        // What is no problem still says what it is.
        Check(BindingRules.Note(BindingNeed.Optional, true, "", false, false) == "Empty; this kind needs no track binding", "An empty optional binding is information");
        Check(BindingRules.Note(BindingNeed.Optional, false, "", false, true) == "Empty; this kind needs no track binding, but it does not follow a track group while unbound", "The note names the one effect an unbound sign loses");
        Check(BindingRules.Note(BindingNeed.Required, false, "", false, false) == "Empty; a problem once the object is enabled" && BindingRules.Note(BindingNeed.Optional, false, "Sgone", false, false) == "Names missing segment Sgone; a problem once the object is enabled",
            "A disabled object says what enabling it would make a problem");
        Check(BindingRules.Note(BindingNeed.Required, true, "", false, false) == null && BindingRules.Note(BindingNeed.Optional, true, "Sgone", false, false) == null && BindingRules.Note(BindingNeed.Optional, true, "S1", true, true) == null
            && BindingRules.Note(BindingNeed.Never, true, "", false, false) == null && BindingRules.Note(BindingNeed.ReadAnyway, false, "", false, false) == null, "A problem and a sound binding have no note");
        // The reason an object is inspection only is no problem; the layout reads it from the supported-edits text.
        Check(RailroaderScenePatcher.CommandBand.Refusal("Not editable: the marker is inside a crossing") == "the marker is inside a crossing" && RailroaderScenePatcher.CommandBand.Refusal("Inspection only: x") == null && RailroaderScenePatcher.CommandBand.Refusal("Fixed placement and activity") == null
            && RailroaderScenePatcher.CommandBand.Refusal(null) == null, "The refusal is read from the supported-edits text");
    }

    // An object of a kind no supported family covers is listed in one Inspection only group after the family groups,
    // without a problem marker. Problems are only defects the modder can repair with the patcher: what the inspection
    // could not read or check is a note.
    static void InspectionOnlyObjectsHaveTheirOwnGroup() {
        SceneObservation Entry(string path, string family, string[] problems, string only = null, bool active = true, string[] notes = null) => new SceneObservation(new object(), new TargetIdentity("scene", path, "Component"), family, active,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new string[0], new string[0], problems, only ?? "Track binding", null, null, notes, only);
        var none = new string[0];
        const string reason = "Not yet supported: the patcher has no edit for the Milepost family yet";
        var fixture = new Fixture();
        fixture.Objects.Add(Entry("World/Sign", "Track marker", new[] { "Marker broken binding: missing segment Sgone" }));
        fixture.Objects.Add(Entry("World/Post 1", "Milepost", new[] { "Marker broken binding: missing segment S9" }, reason));
        fixture.Objects.Add(Entry("World/Board", "Independent board", none, null, true, new[] { "Direct-reference scan incomplete: 2 unreadable fields" }));
        fixture.Objects.Add(Entry("World/Span", "Track span", none, "Not yet supported: the patcher has no edit for the Track span family yet", false));
        fixture.Objects.Add(Entry("World/Refused", "Track marker", none));
        var inventory = new SceneInventory(fixture); inventory.Refresh();
        string[] Paths(RailroaderScenePatcher.OutlinerFilter filter, string search = "") => RailroaderScenePatcher.InventoryPresentationState.Rows(inventory, search, filter, null).Select(e => e.Identity.Path).ToArray();
        string Shape(RailroaderScenePatcher.OutlinerFilter filter, string search = "") => String.Join(" | ", RailroaderScenePatcher.InventoryPresentationState.Groups(RailroaderScenePatcher.InventoryPresentationState.Rows(inventory, search, filter, null))
            .Select(g => g.Key + ":" + String.Join(",", g.Value.Select(RailroaderScenePatcher.InventoryPresentationState.Name))));
        var all = RailroaderScenePatcher.OutlinerFilter.All;
        // One group, after the family groups, whatever the families of its entries.
        Check(Shape(all) == "Independent board:Board | Track marker:Refused,Sign | Inspection only:Post 1,Span", "Inspection only entries leave their families for one group after the family groups, rows in name order: " + Shape(all));
        Check(RailroaderScenePatcher.InventoryPresentationState.InspectionOnlyGroup == "Inspection only", "The group is named Inspection only");
        // The group is not shown while it holds no entry.
        Check(Shape(all, "sign") == "Track marker:Sign" && Shape(RailroaderScenePatcher.OutlinerFilter.Problems) == "Track marker:Sign", "The group is absent while it holds no entry: " + Shape(all, "sign"));
        var empty = new Fixture(); empty.Objects.Add(Entry("World/Sign", "Track marker", none));
        var plain = new SceneInventory(empty); plain.Refresh();
        Check(RailroaderScenePatcher.InventoryPresentationState.Groups(RailroaderScenePatcher.InventoryPresentationState.Rows(plain, "", all, null)).Select(g => g.Key).SequenceEqual(new[] { "Track marker" }), "An inventory without such objects has no Inspection only group");
        // Search and the All and Disabled filters apply to the group.
        Check(Shape(all, "span") == "Inspection only:Span" && Shape(all, "not yet supported") == "Inspection only:Post 1,Span", "Search finds the group's entries, also by their reason: " + Shape(all, "not yet supported"));
        Check(Paths(RailroaderScenePatcher.OutlinerFilter.Disabled).SequenceEqual(new[] { "World/Span" }) && Shape(RailroaderScenePatcher.OutlinerFilter.Disabled) == "Inspection only:Span", "The Disabled filter lists a disabled Inspection only object, in its group");
        Check(Paths(all).Length == 5, "All lists the group's entries with the rest");
        // No problem and no problem marker on an Inspection only entry: what would be a problem is a note.
        var post = inventory.Entries.Single(e => e.Identity.Path == "World/Post 1");
        Check(post.InspectionOnly == reason && post.Problems.Count == 0 && post.Notes.SequenceEqual(new[] { "Marker broken binding: missing segment S9" }), "An Inspection only entry has no problem; what was found is kept as a note");
        Check(RailroaderScenePatcher.CommandBand.RowMarkers(post.Problems.Count != 0, false, false) == "", "Its row has no problem marker");
        // The Problems filter excludes the group and excludes inspection notes.
        Check(Paths(RailroaderScenePatcher.OutlinerFilter.Problems).SequenceEqual(new[] { "World/Sign" }), "The Problems filter lists only the repairable defect: " + String.Join(",", Paths(RailroaderScenePatcher.OutlinerFilter.Problems)));
        var board = inventory.Entries.Single(e => e.Identity.Path == "World/Board");
        Check(board.Problems.Count == 0 && board.Notes.Count == 1 && RailroaderScenePatcher.CommandBand.RowMarkers(board.Problems.Count != 0, false, false) == "", "An inspection note is no problem and gives no marker");
        // Which lines are notes: what the inspection could not read or check.
        foreach (var line in new[] { "Direct-reference scan incomplete: 3 unreadable fields", "Inspection unavailable: no graph", "Marker binding validity unknown: no graph", "Lower binding validity unknown: no graph", "Construction segment validity unknown: no graph", "Missing script in inspected hierarchy" })
            Check(BindingRules.IsNote(line), "An inspection failure is a note: " + line);
        foreach (var line in new[] { "Marker broken binding: missing segment Sgone", "Marker has an empty segment binding", "Lower broken binding: missing segment S9", "Missing circuit 0 island", "Construction segment S4 no longer exists. On the Connections tab, select the segments of the bridge's track and click Set.", "Missing construction-scenery target" })
            Check(!BindingRules.IsNote(line), "A defect of the object is a problem: " + line);
        Check(!BindingRules.IsNote(null), "No line is no note");
        // A refused object of a supported family stays in its family, with its reason and without a problem.
        var refused = inventory.Entries.Single(e => e.Identity.Path == "World/Refused");
        Check(refused.InspectionOnly == null && refused.Problems.Count == 0, "A refused object of a supported family is not inspection only and has no problem");
        // Two observations of one object with the same identity, as an object with two components of one type gives:
        // each row stays inspectable, and the inspected one is found again after a refresh.
        var twin = new object();
        SceneObservation Twin(string binding) => new SceneObservation(twin, new TargetIdentity("scene", "CTC/Module/BC-E/BC-E", "Track.TrackSpan"), "Track span", true,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new[] { binding }, none, none, reason, null, null, null, reason);
        var twins = new Fixture(); twins.Objects.Add(Twin("Span ID=A")); twins.Objects.Add(Twin("Span ID=B"));
        var doubled = new SceneInventory(twins); doubled.Refresh();
        var state = new RailroaderScenePatcher.InventoryPresentationState();
        var first = doubled.Entries[0]; var second = doubled.Entries[1];
        Check(state.CanSelect(doubled, first) && state.CanSelect(doubled, second), "Both rows of a shared identity can be selected");
        Check(ReferenceEquals(state.CurrentSelection(doubled, first), first) && ReferenceEquals(state.CurrentSelection(doubled, second), second), "A row of a shared identity selects itself, not nothing");
        twins.Objects[0] = Twin("Span ID=A"); twins.Objects[1] = Twin("Span ID=B");
        doubled.Refresh();
        Check(ReferenceEquals(state.CurrentSelection(doubled, second), doubled.Entries[1]) && ReferenceEquals(state.CurrentSelection(doubled, first), doubled.Entries[0]), "After a refresh the inspected one of the two is found again by what it reads");
        // The state is part of what an inspection pins and of the stale reason.
        Check(Entry("World/X", "Milepost", none, reason).Difference(Entry("World/X", "Milepost", none, "Not yet supported: other")) == "supported edits"
            && Entry("World/X", "Track marker", none, null, true, new[] { "a" }).Difference(Entry("World/X", "Track marker", none, null, true, new[] { "b" })).StartsWith("notes: ", StringComparison.Ordinal), "A changed note names itself in the stale reason");
    }

    // The Changes tab lists what a modification changes, one line per setting, with the value without it.
    // A family shows subgroups only by what the game tells apart: a track marker by its marker type, never by a name.
    static void SubgroupsFollowTheGame() {
        SceneObservation Row(string family, string path, params string[] bindings) => new SceneObservation(new object(), new TargetIdentity("scene", path, "Marker"), family, true,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, bindings, new string[0], new string[0], "Track binding");
        var station = Row("Track marker", "World/Bryson Station Sign", "Marker ID=M1; type=Generic", "Marker segment=S1; end=a; distance=2");
        var whistle = Row("Track marker", "World/Whistle Sign E", "Marker ID=M2; type=Generic");
        var stop = Row("Track marker", "World/Stop", "Marker ID=M3; type=PassengerStop");
        var starter = Row("Track marker", "World/Setups/starter-ewh-coaling", "Marker ID=M4; type=Generic");
        var board = Row("Board", "World/Board", "Culler");
        var bare = Row("Track marker", "World/Bare");
        foreach (var row in new[] { new object[] { station, "Type Generic" }, new object[] { whistle, "Type Generic" }, new object[] { starter, "Type Generic" }, new object[] { stop, "Type PassengerStop" }, new object[] { board, null }, new object[] { bare, null }, new object[] { null, null } })
            Check(RailroaderScenePatcher.InventoryPresentationState.Subgroup((SceneObservation)row[0]) == (string)row[1], "The subgroup of a row is its marker type or none: " + row[1]);
        Check(RailroaderScenePatcher.InventoryPresentationState.Subgroups(new[] { station, whistle, starter }).Count == 0, "Markers of one type form no subgroups: a station sign, a whistle sign and a starter marker are one kind to the game");
        var two = RailroaderScenePatcher.InventoryPresentationState.Subgroups(new[] { stop, station, whistle });
        Check(two.Count == 2 && two[0].Key == "Type Generic" && two[0].Value.SequenceEqual(new[] { station, whistle }) && two[1].Key == "Type PassengerStop" && two[1].Value.Single() == stop, "Markers of two types stand under their types, in name order, rows in the order given");
        Check(RailroaderScenePatcher.InventoryPresentationState.Subgroups(new[] { board, Row("Board", "World/Other") }).Count == 0, "A family the game does not divide has no subgroups");
    }
    static void ChangesReadOneLinePerSetting() {
        var before = new ObjectState(new Placement(new[] { 10.0, 2, 3 }, new[] { 0.0, 0, 0, 1 }), true, MarkerBinding.At(new TrackLocation("S377", "a", 7.5116)));
        string Lines(ObjectState state, ObjectState was) => String.Join(" | ", PatchEditingSession.ChangeLines(state, was));
        Check(Lines(new ObjectState(new Placement(new[] { 12.5, 2, 3 }, new[] { 0.0, 0, 0, 1 }), null), before) == "Position: from 10, 2, 3 to 12.5, 2, 3", "A moved object reads its position from and to: " + Lines(new ObjectState(new Placement(new[] { 12.5, 2, 3 }, new[] { 0.0, 0, 0, 1 }), null), before));
        Check(Lines(new ObjectState(new Placement(new[] { 10.0, 2, 3 }, new[] { 0.0, 0.7071, 0, 0.7071 }), null), before) == "Rotation: changed", "A turned object names its rotation alone: its position did not change");
        Check(Lines(new ObjectState(null, false), before) == "Enabled: from Yes to No", "A disabled object reads its activity from and to");
        Check(Lines(new ObjectState(null, null, MarkerBinding.At(new TrackLocation("Srrh", "b", 3))), before) == "Track binding: from S377 / a / 7.512 to Srrh / b / 3", "A rebound marker reads its binding from and to");
        Check(Lines(new ObjectState(null, null, MarkerBinding.Detached), before) == "Track binding: from S377 / a / 7.512 to detached", "A detached marker says so");
        Check(Lines(new ObjectState(null, true, MarkerBinding.At(new TrackLocation("Srrh", "b", 3))), before) == "Enabled: from Yes to Yes | Track binding: from S377 / a / 7.512 to Srrh / b / 3", "Every changed setting has its own line");
        var following = new Placement(new[] { 1.0, 2, 3 }, new[] { 0.0, 0, 0, 1 }) { };
        Check(Lines(new ObjectState(null, null), before) == "" && PatchEditingSession.ChangeLines(null, before).Count == 0, "No setting gives no line");
        // A target refusal names what differed.
        var sign = new object();
        SceneObservation Marker(double x, bool active, object handle = null) => new SceneObservation(handle ?? sign, new TargetIdentity("scene", "World/Sign", "Marker"), "Track marker", active,
            new double[] { x, 2, 3 }, new double[] { 0, 0, 0, 1 }, new string[0], new string[0], new string[0], "Track binding");
        Check(InspectedTarget.Changed(Marker(1, true), Marker(5, true)) == "Inspected target changed (position); refresh and inspect again" && InspectedTarget.Changed(Marker(1, true), Marker(1, false)) == "Inspected target changed (activity); refresh and inspect again"
            && InspectedTarget.Changed(Marker(1, true), null) == "Inspected target changed (missing); refresh and inspect again"
            && InspectedTarget.Changed(Marker(1, true), Marker(1, true, new object())) == "Inspected target changed (another object); refresh and inspect again", "The refusal of a changed target names what differed");
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

    // A result describes the present. It goes when a new action reports, when the view changes, and when the native map editor's
    // history moves: after a native Undo, "Enable completed" would be untrue.
    static void RepeatedMessagesCollapse() {
        var log = new List<string>();
        var feedback = new RailroaderScenePatcher.EditFeedback { Describe = text => { log.Add(text); return text; } };
        feedback.Refuse("Placement", "Placement", "The object list is stale");
        feedback.Refuse("Placement", "Placement", "The object list is stale");
        Check(feedback.Messages.Count == 1 && log.Count == 2, "The same refusal again is one message, and both are logged");
        feedback.Refuse("Placement", "Placement", "enter six numbers");
        feedback.Report("Placement", "Placement completed.");
        Check(feedback.Messages.Select(m => m.Text).SequenceEqual(new[] { "Placement completed." }), "The success of the corrected action replaces the refusal: " + String.Join(" | ", feedback.Messages.Select(m => m.Text)));
        // The view: the open tab and the placement mode.
        feedback.View("placement/0");
        feedback.Report("Placement", "Placement completed.");
        feedback.View("placement/0");
        Check(feedback.Messages.Count == 1, "The same view keeps its result");
        feedback.View("placement/2");
        Check(feedback.Messages.Count == 0 && feedback.Result == "", "Another placement mode clears the result of the mode before");
        feedback.Report("Snap", "Physical snap completed.");
        feedback.View("object");
        Check(feedback.Messages.Count == 0, "Another tab clears it as well");
        // Undo or Redo.
        feedback.Report("Activity", "Enable completed.");
        feedback.Follow("Terrain output", "Terrain output complete.");
        feedback.HistoryMoved();
        Check(feedback.Messages.Count == 0 && feedback.Result == "", "After Undo or Redo the result and its follow-up are gone");
        // A world choice that starts is a new action: the refusal of the action before it goes.
        feedback.Refuse("Track binding", "Track binding to SE2E / a / 7.5", "Relationship target segment SE2E is missing");
        Check(feedback.Messages.Count == 1 && feedback.Result.Contains("refused"), "A refused Set stands in the message area");
        feedback.ChoiceStarted();
        Check(feedback.Messages.Count == 0, "The refusal of an earlier Set does not stand beside a new world choice");
        feedback.Released(); feedback.Refuse("Activity", "Enable", "the panel was closed before the request ran"); feedback.ChoiceStarted();
        Check(feedback.Messages.Count == 1, "A refusal kept for the next inspection stays when a choice starts");
        feedback.Clear();
        // An announcement that is withdrawn.
        feedback.Report("Crossing coverage", "Detection addition a7 is open at the top of this tab: select its tracks, then Preview.");
        feedback.Withdraw("Crossing coverage", "Detection addition a7 is open at the top of this tab: select its tracks, then Preview.");
        Check(feedback.Messages.Count == 0, "A withdrawn announcement leaves");
    }

    // Each message keeps the action group it came from, and inspecting another object clears the messages.
    static void MessagesKeepTheirSourceNewestFirst() {
        var log = new List<string>();
        var feedback = new RailroaderScenePatcher.EditFeedback { Describe = text => { log.Add(text); return text; } };
        Check(feedback.Messages.Count == 0, "No message before the first result");
        feedback.Refuse("Track binding", "Track binding", "enter a segment ID");
        Check(feedback.Messages.Select(m => m.Source + ": " + m.Text).SequenceEqual(new[] { "Track binding: Track binding refused. Enter a segment ID." }), "A message keeps its source");
        feedback.Report("Placement", "Placement completed.");
        Check(feedback.Place == "Placement" && feedback.Result == "Placement completed." && feedback.Messages.Count == 1, "The newest result is the one shown");
        Check(log.Count == 2, "Every message is logged once");
        object first = new object(), second = new object();
        feedback.Inspecting(first);
        feedback.Report("Enabled", "Activity change completed.");
        feedback.Inspecting(second);
        Check(feedback.Messages.Count == 0 && feedback.Result == "", "Inspecting another object clears every message");
    }

    // A result or refusal is about the object that was inspected when it was reported.
    static void ResultsBelongToTheInspectedObject() {
        var log = new List<string>();
        var feedback = new RailroaderScenePatcher.EditFeedback { Describe = text => { log.Add(text); return text; } };
        object first = new object(), second = new object();
        feedback.Inspecting(first);
        feedback.Refuse("placement", "Placement", "Target must resolve uniquely");
        feedback.Inspecting(first);
        Check(feedback.Place == "placement" && feedback.Result == "Placement refused. Target must resolve uniquely.", "A result stays while its object stays inspected");
        feedback.Inspecting(null);
        Check(feedback.Result == "Placement refused. Target must resolve uniquely.", "A result stays when the inspected object is no longer present");
        feedback.Inspecting(second);
        Check(feedback.Place == null && feedback.Result == "" && log.Count == 1, "Inspecting another object clears the previous object's result without a log line");
        feedback.Report("modification", "Recheck completed.");
        feedback.Inspecting(second);
        Check(feedback.Result == "Recheck completed.", "A result on the newly inspected object stays");
        feedback.Inspecting(first);
        Check(feedback.Result == "", "Returning to the first object does not bring a result back");
    }
}
