// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// A marker world whose transform reads back a rotation that is not bit-identical to the one it was given, as a native
// transform can: the read-back is within any placement tolerance, and each write moves it by one more unit in the last place.
class DriftingMarkerWorld : FakeMarkerWorld, ITrackMarkerWorld {
    public bool Drift;
    public new void SetPlacement(object target, double[] position, double[] rotation) {
        base.SetPlacement(target, position, rotation);
        if (Drift) { var m = (FakeMarker)target; m.Rotation[3] = Math.BitIncrement(m.Rotation[3]); }
    }
}
// Defects from the owner rerun O.01 (build c35e6b5): gizmo moves refused as a changed target after the gesture's own
// restoration, and a changed-target refusal that does not say what changed.
static class GizmoRefusalTests {
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    const string StationPath = "World/Large Scenery/Hemingway/Hemingway flagstopstation";
    static readonly TargetIdentity Station = new TargetIdentity("BushnellWhittier", StationPath, "Track.TrackMarker", TrackMarkerScene.Structure);
    sealed class Marker {
        internal readonly DriftingMarkerWorld World = new DriftingMarkerWorld();
        internal readonly FakeMarker Station = new FakeMarker { Id = "Mo0j", Segment = "S9sc", End = "a", Distance = 46.52 };
        internal readonly SceneCapabilities Capabilities;
        internal readonly PatchEngine Engine;
        internal readonly NativeMapEditor MapEditor = new NativeMapEditor();
        internal readonly PatchEditingSession Session;
        internal readonly SceneInventory Inventory;
        internal Marker() {
            World.Objects[StationPath] = new List<object> { Station };
            World.Load(Station); World.Relative(Station);
            Capabilities = new SceneCapabilities(() => World.MapIds, new FakeMarkerInspection(World));
            Capabilities.Add(new TrackMarkerScene(World, Capability.Unchecked));
            Engine = new PatchEngine(Capabilities);
            Session = new PatchEditingSession(Engine, MapEditor, new Store()); MapEditor.Session = Session;
            Inventory = new SceneInventory(Capabilities); Inventory.Refresh();
            World.Drift = true;
        }
        internal SceneObservation Entry => Inventory.Entries.Single(e => ReferenceEquals(e.Handle, Station));
        // The panel's pin: the snapshot was compared, so every validation observes the target now.
        internal PlacementGesture Begin() => new PlacementGesture(Engine, Session, GizmoRefusalTests.Station, Inventory.PinCompared(Entry));
        internal Placement Moved(double x) => new Placement(new[] { x, 1.0, 2.0 }, (double[])Station.Rotation.Clone());
    }
    internal static void Run() {
        // Each defect is reported on its own, so one failing case does not hide the state of the others.
        var failures = new List<string>();
        foreach (var test in new Action[] { ARestoreThatReadsBackOffInTheLastPlaceCompletes, AGestureAfterACancelCompletes, AChangedTargetRefusalNamesTheDifference }) {
            try { test(); } catch (Exception e) { failures.Add(test.Method.Name + ": " + e.Message); }
        }
        if (failures.Count != 0) throw new Exception("Gizmo refusal tests failed:\n" + String.Join("\n", failures));
        Console.WriteLine("Gizmo refusal tests passed");
    }
    // o01-7b: Complete restores the preview before it commits the final placement. The restoration verifies within the placement
    // tolerance while the inspection compares the rotation exactly, so the edit binds to the target as restored, not to
    // the inspection from before the gesture. Every attempt, also after Refresh, completes.
    static void ARestoreThatReadsBackOffInTheLastPlaceCompletes() {
        var f = new Marker();
        for (var attempt = 1; attempt <= 3; attempt++) {
            var gesture = f.Begin();
            Assert(gesture.Preview(f.Moved(1030 + attempt)).Applied, "Attempt " + attempt + ": the preview moves the station");
            var completed = gesture.Complete();
            Assert(completed.Applied, "Attempt " + attempt + ": a move whose restoration reads back one unit in the last place off completes: " + completed.Error);
            Assert(f.Station.Position[0] == 30 + attempt, "Attempt " + attempt + ": the station stands where the gizmo released it: " + f.Station.Position[0]);
            f.Inventory.Refresh();
        }
    }
    // o01-7c: a Cancel restores the captured placement with the same restoration; the panel observes the object again
    // afterwards (GizmoController.Stop invokes finished, and EntryPoint runs RefreshAfterEdit, which reobserves the
    // inspected entry). The next gesture captures that restored state and completes over its own restoration.
    static void AGestureAfterACancelCompletes() {
        var f = new Marker();
        var cancelled = f.Begin();
        Assert(cancelled.Preview(f.Moved(1025)).Applied && cancelled.Cancel().Applied, "A previewed gesture is cancelled and its restoration verifies");
        Assert(f.Inventory.Reobserve(f.Entry) != null, "The panel observes the cancelled target again, as it does after every gizmo end");
        var next = f.Begin();
        Assert(next.Preview(f.Moved(1026)).Applied, "The next gesture previews");
        var completed = next.Complete();
        Assert(completed.Applied && f.Station.Position[0] == 26, "The gesture after a Cancel completes: " + completed.Error);
    }
    // A target that did change is refused, and the refusal names what differs, as a stale reason does.
    static void AChangedTargetRefusalNamesTheDifference() {
        var f = new Marker(); f.World.Drift = false;
        var pinned = f.Inventory.PinCompared(f.Entry);
        f.Station.Position[0] += 1;
        var refused = f.Session.Edit(Station, f.Moved(1040), null, pinned);
        Assert(!refused.Applied && refused.Error != null && refused.Error.StartsWith("Inspected target changed", StringComparison.Ordinal), "A moved target is refused as changed: " + refused.Error);
        Assert(refused.Error.Contains("position"), "The refusal names the position as what differs: " + refused.Error);
    }
}
