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
        public double[] MapPoint(double[] click) => click[1] < Horizon ? null : new[] { 4000 + click[0], 12, 5000 + click[1] };
    }
    static readonly BaseMapFixture Map = new BaseMapFixture();
    static SceneObservation Row(string path, string family = null) {
        var rows = Map.Observations(out _);
        return rows.First(r => r.Identity.Path == path && (family == null || r.Family == family));
    }

    internal static void Run() {
        DuplicateIsInactiveWhereItDoesNotApply();
        DuplicateIsNoSupportedEdit();
        TheBlankWindowHoldsNothingOfTheModObject();
        OnlyALeftClickOnTheWorldPlaces();
        EscReturnsAndCancelEnds();
        ARefusedClickReturnsToTheBlankWindow();
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
        Assert(DuplicateState.Reason(starter, null) == "No start setup reads a duplicated marker.", "A starter placement marker shows its reason: " + DuplicateState.Reason(starter, null));
        var part = Row(ModObjectTests.Station + "/Mask");
        Assert(DuplicateState.Reason(part, null) == DuplicateState.Part, "A part is duplicated with its group: " + DuplicateState.Reason(part, null));
        var marker = Map.Observations(out _).First(r => r.Family == SceneFamilies.TrackMarker);
        Assert(DuplicateState.Reason(marker, null) == DuplicateState.OtherFamily, "A track marker needs more than a copy: " + DuplicateState.Reason(marker, null));
        Assert(DuplicateState.Reason(station, new[] { SceneFamilies.SceneObject, SceneFamilies.TrackMarker }) == DuplicateState.HoldsOtherFamily, "A group that holds a track marker is not copied");
        var bridge = Map.Observations(out _).FirstOrDefault(r => r.Bridge != BridgeKind.None);
        Assert(bridge != null && DuplicateState.Reason(bridge, null) == DuplicateState.Bridge, "A bridge is not duplicated");
        var only = Map.Observations(out _).FirstOrDefault(r => r.InspectionOnly != null);
        Assert(only == null || DuplicateState.Reason(only, null) == DuplicateState.InspectionOnly, "An inspection-only object is not duplicated");
        Assert(DuplicateState.Reason(null, null) == null, "Without an object there is no reason");
        foreach (var reason in new[] { DuplicateState.StarterMarker, DuplicateState.Part, DuplicateState.InspectionOnly, DuplicateState.Bridge, DuplicateState.OtherFamily, DuplicateState.HoldsOtherFamily })
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
            return State.Tick(Pointer, (source, placement) => { Clicks++; return Session.PlaceModObject(source, placement); });
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
        Assert(window.State.Instruction == DuplicateState.BlankInstruction && window.State.PlaceReason == null, "The blank window says what to do and offers Place");
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
        Assert(window.State.Step == DuplicateStep.Placing && window.State.Instruction == DuplicateState.PlacingInstruction && window.State.PlaceReason == DuplicateState.PlacingActive, "Place shows the left-click instruction");
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
        Assert(window.State.Release() && window.State.Step == DuplicateStep.Blank && window.State.PlaceReason == null, "Esc while placing returns to the blank window");
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
}
