// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Duplicate at the Scene Patcher window: the blank object window of a mod object that does not exist yet, with Place
// and Cancel, and the placing click. The pointer is recorded input: each frame holds the button that went down, the
// screen point and whether a window is under the pointer. The adapter fake gives the left clicks on the world and the
// map point under a screen point.
static class DuplicateTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Duplicate: " + message); }
    // One recorded frame: Button is 0 for left, 1 for right, 2 for middle and -1 for none.
    sealed class Frame { public int Button = -1; public double X, Y; public bool OverWindow; }
    // The adapter over recorded input: only a left click that is on no window is a world click, and only a screen point
    // below the horizon has ground under it.
    sealed class RecordedPointer : IPlacingPointer {
        public Frame Now = new Frame();
        public const double Horizon = 300;
        public double[] WorldClick => Now.Button == 0 && !Now.OverWindow ? new[] { Now.X, Now.Y } : null;
        public double[] Pointer => Now.OverWindow ? null : new[] { Now.X, Now.Y };
        public double[] MapPoint(double[] click) => click[1] < Horizon ? null : new[] { 4000 + click[0], 12, 5000 + click[1] };
    }
    static readonly BaseMapFixture Map = new BaseMapFixture();
    static SceneObservation Row(string path, string family = null) {
        var rows = Map.Observations(out _);
        return rows.First(r => r.Identity.Path == path && (family == null || r.Family == family));
    }

    // A crossing without signals has no row: its outliner node is the source of Duplicate while no row is inspected.
    // Duplicate judges whether that source still exists, not the inspected row, so it is active on the node, and the
    // reasons of the window still come in their order.
    static void TheCrossingNodeIsDuplicatedWithoutASelectedRow() {
        var crossing = DuplicateState.Inactive(null, null, null, sourceExists: true, checkPending: false);
        Assert(crossing == null, "Duplicate is active on a crossing node whose crossing exists: " + crossing);
        Assert(DuplicateState.Inactive(null, null, null, sourceExists: false, checkPending: false) == "The inspected object no longer exists. Refresh the inventory.", "A source that is gone keeps its reason");
        Assert(DuplicateState.Inactive("Own reason.", "Blocked.", "Locked.", false, true) == "Own reason.", "The object's own reason comes first");
        Assert(DuplicateState.Inactive(null, "Blocked.", "Locked.", false, true) == "Blocked. The recovery choices are in the session details.", "A blocked session comes next");
        Assert(DuplicateState.Inactive(null, null, "Locked.", false, true) == "Locked.", "Then what blocks editing");
        Assert(DuplicateState.Inactive(null, null, null, true, true) == "Wait for the scene check to finish.", "Then a pending scene check");
    }
    // While the node of a crossing without signals is selected and no row is inspected, the inspector heading names that
    // crossing by its node label with the crossing object's full path in its tooltip, as it names an inspected object,
    // and the select instruction is left out; without one the heading reads No Selection above the instruction.
    static void TheHeadingNamesTheSelectedCrossingNode() {
        Assert(DuplicateState.EmptyHeading("Crossing Unprotected") == "Crossing Unprotected", "The heading names the selected crossing node: " + DuplicateState.EmptyHeading("Crossing Unprotected"));
        Assert(DuplicateState.EmptyHeading(null) == "No Selection" && DuplicateState.EmptyHeading("") == "No Selection", "Without a crossing node the heading reads No Selection");
        const string path = "World/Large Scenery/Alarka Jct/Crossing Unprotected (1)";
        Assert(DuplicateState.EmptyHeadingTooltip("Crossing Unprotected", path) == path, "The heading's tooltip gives the crossing object's full path, as for an inspected object");
        Assert(DuplicateState.EmptyHeadingTooltip(null, path) == null && DuplicateState.EmptyHeadingTooltip("", null) == null, "No Selection has no tooltip");
        Assert(DuplicateState.EmptyInstruction("Crossing Unprotected") == null, "A selected crossing node is a selection: no select instruction below its heading");
        Assert(DuplicateState.EmptyInstruction(null) == CommandBand.SelectInstruction && DuplicateState.EmptyInstruction("") == CommandBand.SelectInstruction, "Without a selection the select instruction shows");
    }
    internal static void Run() {
        DuplicateIsInactiveWhereItDoesNotApply();
        DuplicateIsNoSupportedEdit();
        TheBlankWindowHoldsNothingOfTheModObject();
        OnlyALeftClickOnTheWorldPlaces();
        EscReturnsAndCancelEnds();
        ARefusedClickReturnsToTheBlankWindow();
        TheGhostFollowsThePointerWhilePlacing();
        ReleaseAndEscRemoveTheGhost();
        TheClickPlacesAtTheGhost();
        SelectingAnotherObjectAsksFirst();
        TheCrossingNodeIsDuplicatedWithoutASelectedRow();
        TheHeadingNamesTheSelectedCrossingNode();
        Console.WriteLine("PASS: duplicate");
    }

    // Duplicate is offered for a scene object and a station map icon. A starter placement marker shows its short
    // reason, and so does every object a plain copy would not rebuild soundly.
    static void DuplicateIsInactiveWhereItDoesNotApply() {
        var station = Row(ModObjectTests.Station);
        Assert(DuplicateState.Reason(station, new[] { SceneFamilies.SceneObject, SceneFamilies.ChildObject }) == null, "A station building can be duplicated");
        var icon = Map.Observations(out _).First(r => r.Family == SceneFamilies.StationMapIcon);
        Assert(DuplicateState.Reason(icon, new[] { SceneFamilies.StationMapIcon }) == null, "A station map icon can be duplicated");
        var starter = Map.Observations(out _).First(r => r.Family == SceneFamilies.StarterPlacement);
        Assert(DuplicateState.Reason(starter, null) == "No start setup uses the copy.", "A starter placement marker shows its reason: " + DuplicateState.Reason(starter, null));
        var part = Row(ModObjectTests.Station + "/Mask");
        Assert(part.GroupPath == ModObjectTests.Station && DuplicateState.Reason(part, null) == null, "A part can be duplicated: " + DuplicateState.Reason(part, null));
        // A Generic track marker and a milepost are duplicated: the placing click gives the copy a marker ID of its own.
        var marker = Map.Observations(out _).First(r => r.Family == SceneFamilies.TrackMarker && r.Identity.Structure == TrackMarkerScene.Structure);
        Assert(DuplicateState.Reason(marker, new[] { SceneFamilies.TrackMarker }) == null, "A Generic track marker can be duplicated: " + DuplicateState.Reason(marker, null));
        var post = Map.Observations(out _).First(r => r.Family == SceneFamilies.Milepost && r.Identity.Structure == MilepostScene.Structure);
        Assert(DuplicateState.Reason(post, new[] { SceneFamilies.Milepost }) == null, "A milepost can be duplicated: " + DuplicateState.Reason(post, new[] { SceneFamilies.Milepost }));
        // A crossing pad the patcher can edit is duplicated: its copy gets a marker ID of its own and no binding.
        var pad = Map.Observations(out _).First(r => r.Family == SceneFamilies.CrossingPad && r.Identity.Structure == CrossingPadScene.Structure);
        Assert(DuplicateState.Reason(pad, null) == null, "A crossing pad can be duplicated: " + DuplicateState.Reason(pad, null));
        var shared = Map.Observations(out _).FirstOrDefault(r => r.Family == SceneFamilies.TrackMarker && r.Identity.Structure == null && r.GroupPath == null);
        Assert(shared == null || DuplicateState.Reason(shared, null) == CommandBand.EditReason(shared.Operations), "A marker the patcher cannot edit is not duplicated: " + (shared == null ? "" : DuplicateState.Reason(shared, null)));
        Assert(DuplicateState.Reason(station, new[] { SceneFamilies.SceneObject, SceneFamilies.TrackMarker }) == DuplicateState.HoldsOtherFamily, "A group that holds a track marker is not copied");
        // A location sign, a road bridge and a rail bridge scene object can be duplicated; the controller beside the
        // Ela bridge is no bridge and stays with its bridge.
        var sign = Row("World/Signs/Loc: Alarka");
        Assert(sign.Family == SceneFamilies.LocationSign && DuplicateState.Reason(sign, new[] { SceneFamilies.LocationSign }) == null, "A location sign can be duplicated: " + DuplicateState.Reason(sign, new[] { SceneFamilies.LocationSign }));
        var road = Row("World/Large Scenery/Almond/Almond Road Bridge");
        Assert(road.Bridge == BridgeKind.Road && DuplicateState.Reason(road, new[] { SceneFamilies.SceneObject, SceneFamilies.ChildObject }) == null, "A road bridge can be duplicated: " + DuplicateState.Reason(road, null));
        foreach (var path in new[] { "World/Large Scenery/Almond Bridge", "World/Large Scenery/Bryson/Bridge 1" }) {
            var rail = Row(path);
            Assert(rail.Bridge == BridgeKind.Rail && DuplicateState.Reason(rail, new[] { SceneFamilies.SceneObject, SceneFamilies.ChildObject }) == null, "A rail bridge scene object can be duplicated: " + path + " " + DuplicateState.Reason(rail, null));
        }
        var ela = Row("World/Large Scenery/Ela/Ela Bridge");
        Assert(ela.Family == SceneFamilies.Bridge && DuplicateState.Reason(ela, new[] { SceneFamilies.Bridge }) == null, "The Ela bridge can be duplicated: " + DuplicateState.Reason(ela, new[] { SceneFamilies.Bridge }));
        var scenery = Row("World/Large Scenery/Ela/Ela Bridge Scenery");
        Assert(DuplicateState.Reason(scenery, null) == DuplicateState.OtherFamily, "The Ela bridge's construction controller is not duplicated: " + DuplicateState.Reason(scenery, null));
        // A milepost is duplicated with a marker ID of its own (#163), and a spawn point with a teleport name of its own.
        Assert(DuplicateState.Reason(Row("World/Mile Posts/T-45.1 Origin"), new[] { SceneFamilies.Milepost }) == null, "A milepost can be duplicated");
        Assert(DuplicateState.Reason(Row("World/POIs/Alarka"), new[] { SceneFamilies.SpawnPoint }) == null, "A spawn point can be duplicated: " + DuplicateState.Reason(Row("World/POIs/Alarka"), new[] { SceneFamilies.SpawnPoint }));
        var only = Map.Observations(out _).FirstOrDefault(r => r.InspectionOnly != null);
        Assert(only == null || DuplicateState.Reason(only, null) == DuplicateState.InspectionOnly, "An inspection-only object is not duplicated");
        Assert(DuplicateState.Reason(null, null) == null, "Without an object there is no reason");
        foreach (var reason in new[] { DuplicateState.StarterMarker, DuplicateState.InspectionOnly, DuplicateState.OtherFamily, DuplicateState.HoldsOtherFamily })
            Assert(reason.Length <= 45 && reason.EndsWith(".", StringComparison.Ordinal), "A reason is short and a sentence: " + reason);
    }

    // Supported edits lists the edits of an object; Duplicate makes a new object and is no entry of it.
    static void DuplicateIsNoSupportedEdit() {
        foreach (var row in Map.Observations(out _))
            foreach (var edit in SupportedEdits.Of(row))
                Assert(edit.Key.IndexOf(DuplicateState.Label, StringComparison.OrdinalIgnoreCase) < 0, "Supported edits of " + row.Identity.Path + " lists " + edit.Key);
        Assert(SupportedEdits.Lines(SupportedEdits.Of(Row(ModObjectTests.Station))).All(line => line.IndexOf(DuplicateState.Label, StringComparison.OrdinalIgnoreCase) < 0), "No Supported edits line names Duplicate");
    }

    sealed class Window {
        public readonly ModObjectTests.Loaded World = new ModObjectTests.Loaded();
        public readonly PatchEditingSession Session;
        public readonly DuplicateState State = new DuplicateState();
        public readonly RecordedPointer Pointer = new RecordedPointer();
        public readonly SceneObservation Source;
        public int Clicks;
        public Window() {
            Session = ModObjectTests.Session(World, out _, out _);
            Source = new SceneObservation(World.Building, ModObjectTests.Id(ModObjectTests.Station), SceneFamilies.SceneObject, true, new[] { 1733.0, 9, 1473 }, Turn.Yaw(90), new string[0], new string[0], new string[0], "Placement");
        }
        public int Modifications => ((JObject)JObject.Parse(Session.ScenePatchFileText)["modifications"]).Count;
        // One frame of the window with the recorded input.
        public TargetIdentity Play(Frame frame) {
            Pointer.Now = frame;
            return State.Tick(Pointer, (source, placement) => { Clicks++; return Session.PlaceModObject(source.Identity, placement, null, source.GroupPath); });
        }
    }

    // Duplicate opens the blank object window: its title names the source, it shows what is known, and nothing of the
    // mod object exists.
    static void TheBlankWindowHoldsNothingOfTheModObject() {
        var window = new Window();
        Assert(!window.State.Open && window.State.Instruction == null, "No blank window is open before Duplicate");
        window.State.Begin(window.Source);
        Assert(window.State.Step == DuplicateStep.Blank && ReferenceEquals(window.State.Source, window.Source), "Duplicate opens the blank object window for the source");
        Assert(DuplicateState.Title(window.State.Source) == "Duplicating Alarka Jct Station", "The title names the duplicated source: " + DuplicateState.Title(window.State.Source));
        Assert(window.State.Instruction == DuplicateState.BlankInstruction && window.State.Actions.SequenceEqual(new[] { DuplicateState.PlaceLabel, DuplicateState.CancelLabel }), "The blank window says what to do and offers Place");
        Assert(window.Modifications == 0 && window.World.World.Objects.Count == 1 && !window.Session.IsDirty(false), "The unplaced mod object has no object and no entry in the scene patch file");
        // A second Duplicate does not replace the open one.
        window.State.Begin(Row(ModObjectTests.Station + "/Mask"));
        Assert(ReferenceEquals(window.State.Source, window.Source), "The open blank window keeps its source");
    }

    // Place marks the pointer and waits. A right or middle click, a click on a window and a click before Place place
    // nothing; a left click where no ground is says so; the first left click on the ground places the mod object at
    // that point, with the source's rotation.
    static void OnlyALeftClickOnTheWorldPlaces() {
        var window = new Window();
        window.State.Begin(window.Source);
        Assert(window.Play(new Frame { Button = 0, X = 100, Y = 400 }) == null && window.Clicks == 0 && window.Modifications == 0, "A click before Place places nothing");
        window.State.Place();
        Assert(window.State.Step == DuplicateStep.Placing && window.State.Instruction == DuplicateState.PlacingInstruction && window.State.Actions[0] == DuplicateState.ReleaseLabel, "Place shows the left-click instruction and offers Release");
        foreach (var frame in new[] { new Frame(), new Frame { Button = 1, X = 100, Y = 400 }, new Frame { Button = 2, X = 100, Y = 400 }, new Frame { Button = 0, X = 100, Y = 400, OverWindow = true } })
            Assert(window.Play(frame) == null && window.Clicks == 0 && window.State.Step == DuplicateStep.Placing, "Only a left click on the world places: button " + frame.Button + (frame.OverWindow ? " over a window" : ""));
        Assert(window.Play(new Frame { Button = 0, X = 100, Y = 200 }) == null && window.Clicks == 0 && window.State.Step == DuplicateStep.Placing && window.State.Message == DuplicateState.NoGround, "A click where no ground is places nothing and says so");
        Assert(window.Modifications == 0 && window.World.ModObject == null, "Before the placing click nothing exists");
        var placed = window.Play(new Frame { Button = 0, X = 493, Y = 415 });
        Assert(placed != null && placed.Path == ModObjectTests.Made && window.Clicks == 1, "The first left click on the ground places the mod object");
        var made = window.World.ModObject;
        Assert(made != null && window.World.At(made).Zip(new[] { 4493.0, 12, 5415 }, (a, b) => Math.Abs(a - b)).All(d => d < 0.001), "It stands at the world point of the click");
        Assert(made.Rotation.Zip(Turn.Yaw(90), (a, b) => Math.Abs(a - b)).All(d => d < 0.000001), "It has the source's rotation");
        Assert(!window.State.Open && window.Modifications == 1, "The blank window ended and the scene patch file holds the modification");
        Assert(window.Play(new Frame { Button = 0, X = 600, Y = 500 }) == null && window.Clicks == 1 && window.Modifications == 1, "A later click places nothing more");
    }

    // Esc while placing returns to the blank window with Place. Cancel ends the duplicate and gives the source back.
    // Closing the window or a map reload ends it too.
    static void EscReturnsAndCancelEnds() {
        var window = new Window();
        window.State.Begin(window.Source);
        Assert(!window.State.Release(), "Esc in the blank window is not taken");
        window.State.Place();
        Assert(window.State.Release() && window.State.Step == DuplicateStep.Blank && window.State.Actions[0] == DuplicateState.PlaceLabel, "Esc while placing returns to the blank window");
        Assert(window.Play(new Frame { Button = 0, X = 493, Y = 415 }) == null && window.Modifications == 0, "After Esc a click places nothing");
        window.State.Place();
        var source = window.State.Cancel();
        Assert(ReferenceEquals(source, window.Source) && !window.State.Open && window.State.Source == null, "Cancel ends the duplicate and returns the source object");
        Assert(window.Modifications == 0 && window.World.ModObject == null && !window.Session.IsDirty(false), "Cancel adds nothing");
        window.State.Begin(window.Source); window.State.Place();
        window.State.End();
        Assert(!window.State.Open && window.Play(new Frame { Button = 0, X = 493, Y = 415 }) == null && window.Modifications == 0, "Closing the window or a map reload removes the unplaced mod object");
    }

    // A placing click that the edit refuses returns to the blank window with the reason; nothing was added.
    static void ARefusedClickReturnsToTheBlankWindow() {
        var window = new Window();
        window.World.World.ThrowAfterPlacement = true;
        window.State.Begin(window.Source); window.State.Place();
        Assert(window.Play(new Frame { Button = 0, X = 493, Y = 415 }) == null && window.Clicks == 1, "The refused click places nothing");
        Assert(window.State.Step == DuplicateStep.Blank && !String.IsNullOrEmpty(window.State.Message) && window.Modifications == 0 && window.World.ModObject == null, "The blank window shows the reason: " + window.State.Message);
    }

    static bool Near(double[] a, double[] b) => a != null && b != null && a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d < 0.001);

    // While Place is active, the ghost of the source stands at the map point under the pointer and follows it. Over a
    // window, or where no ground is, it is hidden. Before Place there is no ghost.
    static void TheGhostFollowsThePointerWhilePlacing() {
        var window = new Window();
        window.State.Begin(window.Source);
        window.Play(new Frame { X = 100, Y = 400 });
        Assert(window.State.Ghost == null, "The blank window shows no ghost");
        window.State.Place();
        window.Play(new Frame { X = 100, Y = 400 });
        Assert(Near(window.State.Ghost, new[] { 4100.0, 12, 5400 }), "The ghost stands under the pointer");
        window.Play(new Frame { X = 250, Y = 380 });
        Assert(Near(window.State.Ghost, new[] { 4250.0, 12, 5380 }), "The ghost follows the pointer");
        window.Play(new Frame { X = 250, Y = 380, OverWindow = true });
        Assert(window.State.Ghost == null, "Over a window the ghost is hidden");
        window.Play(new Frame { X = 250, Y = 200 });
        Assert(window.State.Ghost == null, "Where no ground is the ghost is hidden");
        window.Play(new Frame { Button = 1, X = 300, Y = 450 });
        Assert(Near(window.State.Ghost, new[] { 4300.0, 12, 5450 }) && window.State.Step == DuplicateStep.Placing, "A camera move keeps the ghost on the pointer");
        Assert(window.Modifications == 0 && window.World.ModObject == null, "The ghost is no mod object");
    }

    // Release, the control that takes Place's slot while placing, and Esc both remove the ghost and return to the blank
    // object window with Place and Cancel; neither creates a mod object.
    static void ReleaseAndEscRemoveTheGhost() {
        foreach (var esc in new[] { false, true }) {
            var window = new Window();
            window.State.Begin(window.Source);
            Assert(window.State.Actions.SequenceEqual(new[] { DuplicateState.PlaceLabel, DuplicateState.CancelLabel }), "The blank window offers Place and Cancel");
            window.State.Place();
            Assert(window.State.Actions.SequenceEqual(new[] { DuplicateState.ReleaseLabel, DuplicateState.CancelLabel }), "While placing the window offers Release and Cancel");
            window.Play(new Frame { X = 100, Y = 400 });
            Assert(window.State.Ghost != null, "The ghost stands on the pointer");
            Assert(window.State.Release(), (esc ? "Esc" : "Release") + " is taken while placing");
            Assert(window.State.Ghost == null && window.State.Step == DuplicateStep.Blank && window.State.Instruction == DuplicateState.BlankInstruction, (esc ? "Esc" : "Release") + " removes the ghost and returns to the blank window");
            Assert(window.State.Actions.SequenceEqual(new[] { DuplicateState.PlaceLabel, DuplicateState.CancelLabel }), "The blank window offers Place and Cancel again");
            window.Play(new Frame { X = 150, Y = 420 });
            Assert(window.State.Ghost == null && window.Play(new Frame { Button = 0, X = 150, Y = 420 }) == null, "After it no ghost follows and a click places nothing");
            Assert(window.Modifications == 0 && window.World.ModObject == null && !window.Session.IsDirty(false), "No mod object was created");
        }
        Assert(DuplicateState.ReleaseLabel == "Release" && DuplicateState.ReleaseTip.EndsWith(".", StringComparison.Ordinal), "Release has its label and tooltip");
    }

    // The ghost moves with the pointer frame by frame; one left click places the mod object at the ghost's position,
    // once, and the ghost goes with the placing.
    static void TheClickPlacesAtTheGhost() {
        var window = new Window();
        window.State.Begin(window.Source); window.State.Place();
        window.Play(new Frame { X = 100, Y = 400 });
        window.Play(new Frame { X = 320, Y = 470 });
        var ghost = window.State.Ghost;
        var placed = window.Play(new Frame { Button = 0, X = 320, Y = 470 });
        Assert(placed != null && window.Clicks == 1, "One left click places the mod object");
        Assert(Near(window.World.At(window.World.ModObject), ghost), "It stands where the ghost stood");
        Assert(window.State.Ghost == null && !window.State.Open, "The ghost goes with the placing");
        window.Play(new Frame { Button = 0, X = 330, Y = 480 });
        Assert(window.Clicks == 1 && window.Modifications == 1, "It is placed once");
    }

    // Selecting another object in the outliner while the mod object is unplaced asks before the duplicate is cancelled.
    // Declining keeps the duplicate as it was, in the blank window; confirming ends it and the selected object is
    // inspected. With no duplicate open, a selection asks nothing.
    static void SelectingAnotherObjectAsksFirst() {
        var other = Row(ModObjectTests.Station + "/Mask");
        var idle = new DuplicateState();
        Assert(!idle.AskBeforeSelecting(other) && idle.Selecting == null, "Without an open duplicate a selection asks nothing");
        foreach (var placing in new[] { false, true }) {
            var window = new Window();
            window.State.Begin(window.Source);
            if (placing) { window.State.Place(); window.Play(new Frame { X = 100, Y = 400 }); }
            Assert(window.State.AskBeforeSelecting(other), "Selecting another object asks first" + (placing ? " while placing" : ""));
            Assert(ReferenceEquals(window.State.Selecting, other) && window.State.Open && ReferenceEquals(window.State.Source, window.Source), "The duplicate stays open while the question is asked");
            Assert(window.State.Ghost == null && window.State.Step == DuplicateStep.Blank, "The ghost leaves the pointer while the question is asked");
            Assert(window.Play(new Frame { Button = 0, X = 100, Y = 400 }) == null && window.Clicks == 0, "A click while the question is asked places nothing");
            window.State.KeepDuplicate();
            Assert(window.State.Selecting == null && window.State.Open && window.State.Step == DuplicateStep.Blank && ReferenceEquals(window.State.Source, window.Source), "Declining keeps the duplicate");
            Assert(window.Modifications == 0, "Declining adds nothing");
            window.State.Place();
            window.Play(new Frame { X = 120, Y = 410 });
            Assert(window.State.Ghost != null, "After declining Place puts the ghost on the pointer again");
            Assert(window.State.AskBeforeSelecting(other), "The next selection asks again");
            var chosen = window.State.CancelForSelection();
            Assert(ReferenceEquals(chosen, other) && !window.State.Open && window.State.Selecting == null && window.State.Ghost == null, "Confirming cancels the duplicate and gives the selected object");
            Assert(window.Modifications == 0 && window.World.ModObject == null && !window.Session.IsDirty(false), "Confirming adds nothing");
        }
        Assert(DuplicateState.SelectQuestion(Row(ModObjectTests.Station), "Bryson Depot") == "To inspect Bryson Depot, discard the unplaced copy of Alarka Jct Station.", "The question names both objects: " + DuplicateState.SelectQuestion(Row(ModObjectTests.Station), "Bryson Depot"));
    }
}
