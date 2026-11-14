// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

static class GizmoTests {
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static readonly TargetIdentity Target = new TargetIdentity("s", "p", "c");
    static Placement Pose(double x) => new Placement(new[] { x, 2.0, 3.0 }, new[] { 0.0, 0.6, 0.0, 0.8 });
    sealed class GizmoScene : Scene, IScene {
        // Native capability snapshots retain map coordinates and affected dependent state.
        public string Registration = "original registration";
        // The rotation a native read-back returns after restoration, when it differs slightly from the captured one.
        public double[] RestoredRotation;
        object IScene.Capture(object target) => new object[] { Read(target), Registration };
        void IScene.Apply(object target, ObjectState state) { Apply(target, state); Registration = "preview registration"; }
        bool IScene.Restore(object target, object snapshot) {
            var captured = (object[])snapshot;
            Apply(target, (ObjectState)captured[0]); Registration = (string)captured[1];
            if (RestoredRotation != null) ((Box)target).Rotation = (double[])RestoredRotation.Clone();
            return true;
        }
    }
    sealed class Fixture {
        internal readonly GizmoScene Scene = new GizmoScene();
        internal readonly Box Box = new Box { Position = new[] { 7.0, 2.0, 3.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        internal readonly Store Store = new Store();
        internal readonly Editor Editor = new Editor();
        internal readonly PatchEngine Engine;
        internal readonly AuthoringSession Session;
        internal readonly SceneInventory Inventory;
        internal Fixture(string patch = null) {
            Store.Patch = patch;
            Scene.Objects["p"] = new List<object> { Box };
            Scene.Observations = () => new[] { new SceneObservation(Box, Target, "board", Box.Active,
                Scene.Read(Box).Placement.Position, Box.Rotation, new[] { "unchanged-binding" },
                Array.Empty<string>(), Array.Empty<string>(), "placement") };
            Engine = new PatchEngine(Scene);
            Session = new AuthoringSession(Engine, Editor, Store, "map"); Editor.Session = Session;
            Inventory = new SceneInventory(Scene); Inventory.Refresh();
        }
        internal PlacementGesture Begin() => new PlacementGesture(Engine, Session, Target, Inventory.Pin(Inventory.Entries[0]));
    }
    // A scene that can observe one object, counting what a gesture reads.
    sealed class TargetedGizmoScene : Scene, IScene, IInspectionScene, ITargetedInspectionScene {
        public int Discoveries, Targeted;
        IEnumerable<SceneObservation> IInspectionScene.Discover() { Discoveries++; return Discover(); }
        public SceneObservation Observe(object handle, TargetIdentity identity) { Targeted++; return Discover().FirstOrDefault(o => ReferenceEquals(o.Handle, handle)); }
    }
    // Dragging previews many poses. Each preview reads its target and never the whole scene; the gesture still refuses
    // a target that changed under it.
    static void APreviewObservesOnlyTheGestureTarget() {
        var scene = new TargetedGizmoScene();
        var box = new Box { Position = new[] { 7.0, 2.0, 3.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        scene.Objects["p"] = new List<object> { box };
        scene.Observations = () => new[] { new SceneObservation(box, Target, "board", box.Active, scene.Read(box).Placement.Position, box.Rotation,
            new[] { "unchanged-binding" }, Array.Empty<string>(), Array.Empty<string>(), "placement") };
        var engine = new PatchEngine(scene); var editor = new Editor();
        var session = new AuthoringSession(engine, editor, new Store(), "map"); editor.Session = session;
        var inventory = new SceneInventory(scene); inventory.Refresh();
        var gesture = new PlacementGesture(engine, session, Target, inventory.PinCompared(inventory.Entries[0]));
        scene.Discoveries = 0; scene.Targeted = 0;
        Assert(gesture.Preview(Pose(112)).Applied && gesture.Preview(Pose(113)).Applied && gesture.Preview(Pose(114)).Applied, "Three previews of one drag apply");
        Assert(scene.Discoveries == 0 && scene.Targeted >= 3, "A preview observes its target and discovers nothing: " + scene.Discoveries + "/" + scene.Targeted);
        Assert(box.X == 14, "The last preview is the pose in the scene: " + box.X);
        var completed = gesture.Complete();
        Assert(completed.Applied && box.X == 14 && editor.UndoGroups.Count == 1 && scene.Discoveries == 0, "Completion persists the previewed pose without a discovery: " + completed.Error + " " + scene.Discoveries);
        // A change by another hand between two previews is refused and not overwritten.
        inventory.Refresh();
        gesture = new PlacementGesture(engine, session, Target, inventory.PinCompared(inventory.Entries[0]));
        Assert(gesture.Preview(Pose(120)).Applied, "A preview precedes an independent change");
        box.X = 55; box.Active = false;
        var mutations = scene.MutationCalls;
        var refused = gesture.Preview(Pose(121));
        Assert(!refused.Applied && box.X == 55 && !box.Active && scene.MutationCalls == mutations, "A preview after an independent change is refused and leaves that change: " + refused.Error);
    }
    internal static void Run() {
        CompletedGesturePersistsOneFixedOperation();
        SmallRotationsCompleteAsOperations();
        RestorationVerifiesWithinTheReadBackTolerance();
        CancelAndOriginShiftRestoreMapState();
        StaleAndFailedPreviewsStayGuarded();
        UnsupportedExistingContentRejectsPreview();
        ExternalChangesAreNotOverwrittenByCancellation();
        APreviewObservesOnlyTheGestureTarget();
        Console.WriteLine("Gizmo tests passed");
    }
    static void ExternalChangesAreNotOverwrittenByCancellation() {
        var f = new Fixture(); var gesture = f.Begin();
        Assert(gesture.Preview(Pose(112)).Applied, "A valid preview precedes an independent change");
        f.Box.X = 55; f.Box.Active = false;
        var mutations = f.Scene.MutationCalls;
        Assert(!gesture.Complete().Applied && f.Box.X == 55 && !f.Box.Active && f.Scene.MutationCalls == mutations, "Stale completion preserves an independently changed pose and activity rather than overwriting it during cleanup");
        Assert(f.Editor.UndoGroups.Count == 0, "A stale gesture cannot register history");
    }
    static void UnsupportedExistingContentRejectsPreview() {
        var patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"future\",\"target\":{\"scene\":\"s\",\"path\":\"p\",\"component\":\"c\"},\"futureOperation\":true,\"dependsOn\":[]}]}";
        var f = new Fixture(patch); var refused = false;
        try { f.Begin(); } catch (InvalidOperationException) { refused = true; }
        Assert(refused && f.Scene.MutationCalls == 0 && f.Store.Patch == patch && f.Editor.UndoGroups.Count == 0, "An unsupported existing contribution refuses the gesture before any preview mutation");
    }
    static void CancelAndOriginShiftRestoreMapState() {
        var f = new Fixture(); var gesture = f.Begin();
        Assert(gesture.Preview(Pose(112)).Applied, "Preview starts at a nonzero origin");
        f.Scene.Origin = 110; f.Box.X -= 10;
        Assert(gesture.Preview(Pose(114)).Applied && f.Box.X == 4, "A shifted origin changes local display position without changing map desired state");
        Assert(gesture.Cancel().Applied && f.Box.X == -3 && f.Box.Rotation[3] == 1 && f.Scene.Registration == "original registration", "Cancel restores the captured map state at the current origin");
        Assert(f.Editor.UndoGroups.Count == 0 && !f.Editor.IsDirty && f.Store.Patch == null, "Cancel creates no saved or undoable edit");
        f.Inventory.Refresh(); gesture = f.Begin();
        Assert(gesture.Preview(Pose(116)).Applied && gesture.Complete().Applied && f.Box.X == 6, "A subsequent completed gesture stores fixed placement at the new origin");
        f.Editor.Undo();
        Assert(f.Box.X == -3, "Undo restores prior map state using the new local origin");
    }
    static void StaleAndFailedPreviewsStayGuarded() {
        var f = new Fixture(); var gesture = f.Begin();
        var replacement = new Box { X = 52, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        f.Scene.Objects["p"] = new List<object> { replacement };
        Assert(!gesture.Preview(Pose(112)).Applied && f.Scene.MutationCalls == 0 && f.Box.X == 7 && replacement.X == 52, "A replaced target is rejected before the first preview mutation");
        f = new Fixture(); gesture = f.Begin();
        Assert(gesture.Preview(Pose(112)).Applied, "A valid preview precedes stale commit detection");
        f.Scene.Objects["p"] = new List<object> { f.Box, replacement };
        var mutations = f.Scene.MutationCalls;
        Assert(!gesture.Complete().Applied && f.Scene.MutationCalls == mutations && replacement.X == 52 && f.Editor.UndoGroups.Count == 0, "Ambiguous commit cannot mutate either target or register history");
        f = new Fixture(); gesture = f.Begin(); f.Scene.FailNextApplies = 1;
        Assert(!gesture.Preview(Pose(112)).Applied && f.Box.X == 7 && f.Box.Rotation[3] == 1 && f.Editor.UndoGroups.Count == 0, "A failed preview restores complete prior pose without history");
        f = new Fixture(); gesture = f.Begin(); f.Scene.FailApply = true;
        Assert(!gesture.Preview(Pose(112)).Applied, "Preview and restoration failure is reported");
        f.Scene.FailApply = false; f.Inventory.Refresh();
        var refused = false;
        try { f.Begin(); } catch (InvalidOperationException e) { refused = e.Message.Contains("cleanup"); }
        Assert(refused, "Unverified cleanup guards the next gesture until explicit recheck");
        Assert(f.Session.Recheck(Target).Applied, "Explicit recheck reconciles the canonical state before retry");
        f.Inventory.Refresh(); Assert(f.Begin().Cancel().Applied, "A verified recheck permits a later gesture");
    }
    static void CompletedGesturePersistsOneFixedOperation() {
        var f = new Fixture(); var gesture = f.Begin();
        Assert(gesture.Preview(Pose(112)).Applied && f.Box.X == 12, "Dragging previews the checked map pose in the world");
        Assert(gesture.Preview(Pose(114)).Applied && f.Box.X == 14, "Successive previews use absolute desired state without drift");
        Assert(f.Editor.UndoGroups.Count == 0 && !f.Editor.IsDirty && JObject.Parse(f.Session.DocumentText)["modifications"].Count() == 0, "Preview creates no canonical edit or history entry");
        Assert(gesture.Complete().Applied && f.Editor.UndoGroups.Count == 1, "One completed gesture registers one native operation");
        var placement = JObject.Parse(f.Session.DocumentText)["modifications"][0]["placement"];
        Assert((string)placement["mode"] == "fixed" && (double)placement["position"][0] == 114 && (double)placement["rotation"][1] == 0.6, "Canonical desired state holds map position and complete rotation");
        f.Editor.Undo();
        Assert(f.Box.X == 7 && f.Box.Rotation[3] == 1 && JObject.Parse(f.Session.DocumentText)["modifications"].Count() == 0, "Undo restores the prior effective world and document");
        f.Editor.Redo();
        Assert(f.Box.X == 14 && f.Editor.Save() == null && !f.Editor.IsDirty && f.Store.Info.Contains("crankyant/scene-patcher"), "Redo and Save persist patch and dependency with clean accounting");
        var loaded = new Fixture(); loaded.Scene.Origin = 110;
        var applied = loaded.Engine.Apply(f.Store.Patch);
        Assert(applied.Error == null && applied.Outcomes.Values.Single().Applied && loaded.Box.X == 4 && loaded.Box.Rotation[1] == 0.6, "The complete saved document reproduces the pose at a different origin");
        loaded.Engine.Apply(f.Store.Patch);
        Assert(loaded.Box.X == 4, "Repeated load reconciles desired state without accumulated movement");
    }
    // A rotation about the vertical axis by the given angle, at the fixture object's own map position.
    static Placement Turned(double degrees) { var half = degrees * Math.PI / 360; return new Placement(new[] { 107.0, 2.0, 3.0 }, new[] { 0.0, Math.Sin(half), 0.0, Math.Cos(half) }); }
    static JToken Modifications(Fixture f) => JObject.Parse(f.Session.DocumentText)["modifications"];
    // A completed gesture is one operation however small its rotation; only a gesture that ends where it began is none.
    static void SmallRotationsCompleteAsOperations() {
        foreach (var degrees in new[] { 0.4, 0.1, 0.02 }) {
            var f = new Fixture(); var gesture = f.Begin(); var pose = Turned(degrees);
            Assert(gesture.Preview(pose).Applied && f.Box.Rotation[1] == pose.Rotation[1], "The preview turns the object by " + degrees + " degrees");
            var completed = gesture.Complete();
            Assert(completed.Applied && f.Editor.UndoGroups.Count == 1 && Modifications(f).Count() == 1, "A rotation of " + degrees + " degrees completes as one operation: " + completed.Error + " history " + f.Editor.UndoGroups.Count);
            var rotation = Modifications(f)[0]["placement"]["rotation"];
            Assert((double)rotation[1] == pose.Rotation[1] && (double)rotation[3] == pose.Rotation[3] && f.Box.Rotation[1] == pose.Rotation[1] && f.Box.X == 7, "The document and the world hold the small rotation: " + rotation);
            f.Editor.Undo();
            Assert(f.Box.Rotation[1] == 0 && f.Box.Rotation[3] == 1 && Modifications(f).Count() == 0, "Undo restores the prior rotation");
            f.Editor.Redo();
            Assert(f.Box.Rotation[1] == pose.Rotation[1] && Modifications(f).Count() == 1, "Redo applies the small rotation again");
        }
        var back = new Fixture(); var returned = back.Begin();
        Assert(returned.Preview(Turned(0.4)).Applied && returned.Preview(Turned(0)).Applied && returned.Complete().Applied, "A gesture dragged back to its start completes");
        Assert(back.Editor.UndoGroups.Count == 0 && Modifications(back).Count() == 0 && back.Box.Rotation[3] == 1 && back.Scene.Registration == "original registration", "A gesture that ends where it began records no modification or history");
    }
    // A restored rotation that reads back slightly off the captured one, as a native transform can, still verifies:
    // the gesture ends without guarding the object, also when its own rotation was smaller than that difference.
    static void RestorationVerifiesWithinTheReadBackTolerance() {
        var f = new Fixture(); f.Scene.RestoredRotation = Turned(0.05).Rotation;
        var gesture = f.Begin();
        Assert(gesture.Preview(Turned(0.4)).Applied, "A preview precedes the cancellation");
        var cancelled = gesture.Cancel();
        Assert(cancelled.Applied && f.Editor.UndoGroups.Count == 0 && Modifications(f).Count() == 0, "Cancel verifies a restoration within the read-back tolerance: " + cancelled.Error);
        f.Inventory.Refresh(); gesture = f.Begin();
        Assert(gesture.Preview(Turned(0.02)).Applied, "The object accepts another gesture after that cancellation");
        var completed = gesture.Complete();
        Assert(completed.Applied && f.Editor.UndoGroups.Count == 1 && f.Box.Rotation[1] == Turned(0.02).Rotation[1], "A small rotation completes over such a restoration: " + completed.Error);
        // A restoration that reads back beyond the tolerance still fails and guards the object.
        var far = new Fixture(); far.Scene.RestoredRotation = Turned(2).Rotation;
        gesture = far.Begin();
        Assert(gesture.Preview(Turned(0.4)).Applied && !gesture.Cancel().Applied, "A restoration that reads back two degrees off is reported");
        far.Inventory.Refresh(); var refused = false;
        try { far.Begin(); } catch (InvalidOperationException e) { refused = e.Message.Contains("cleanup"); }
        Assert(refused, "The unverified restoration guards the next gesture");
    }
}
