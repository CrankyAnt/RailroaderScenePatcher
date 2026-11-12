// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

static class SnapTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static readonly TargetIdentity ObjectId = new TargetIdentity("s", "p", "c");
    sealed class BoundBox : Box {
        public object LogicalTrack = new object();
        public double DependentX = 4;
    }
    sealed class SnapObjects : Scene, IScene {
        public int FailAfterDependentUpdate;
        void IScene.Apply(object target, ObjectState state) {
            base.Apply(target, state);
            ((BoundBox)target).DependentX = ((BoundBox)target).X;
            if (FailAfterDependentUpdate > 0) { FailAfterDependentUpdate--; throw new InvalidOperationException("dependent update injected"); }
        }
        bool IScene.Restore(object target, object snapshot) {
            if (!base.Restore(target, snapshot)) return false;
            ((BoundBox)target).DependentX = ((BoundBox)target).X;
            return true;
        }
    }
    sealed class Tracks : ISnapScene {
        public IEnumerable<string> MapIds => new[] { "map", "layer" };
        public List<SnapGeometry> Items = new List<SnapGeometry>();
        public Action BeforeScan;
        public IEnumerable<SnapGeometry> DiscoverSnapTargets(SelectionKind kind) {
            BeforeScan?.Invoke();
            return Items.Where(g => g.Candidate.Identity.Kind == kind);
        }
        public IEnumerable<SelectionCandidate> Discover(SelectionKind kind) => DiscoverSnapTargets(kind).Select(g => g.Candidate);
    }
    sealed class World {
        public readonly SnapObjects Scene = new SnapObjects();
        public readonly BoundBox Box = new BoundBox { Position = new[] { 4.0, 8.0, 9.0 }, Rotation = new[] { 0.0, 1.0, 0.0, 0.0 } };
        public readonly Tracks Tracks = new Tracks();
        public readonly Store Store = new Store();
        public readonly Editor Editor = new Editor();
        public readonly PatchEngine Engine;
        public readonly AuthoringSession Session;
        public readonly SnapController Snap;
        public World() {
            Scene.Objects["p"] = new List<object> { Box };
            Scene.Observations = () => new[] { new SceneObservation(Box, ObjectId, "object", Box.Active,
                new[] { Box.X + Scene.Origin, Box.Position[1], Box.Position[2] }, Box.Rotation,
                new[] { "binding-to-other-track" }, Array.Empty<string>(), Array.Empty<string>(), "fixed placement") };
            Engine = new PatchEngine(Scene);
            Session = new AuthoringSession(Engine, Editor, Store, "map"); Editor.Session = Session;
            Snap = new SnapController(Session, Scene, Tracks);
        }
        public SnapRequest Prepare(SnapGeometry geometry) {
            var inventory = new SceneInventory(Scene); inventory.Refresh();
            var selection = new PendingSelection(Tracks, geometry.Candidate.Identity.Kind, "Snap physical position");
            selection.Offer(new[] { geometry.Candidate }); Assert(selection.Confirm(), "Snap target identity confirms");
            return Snap.Prepare(ObjectId, inventory.Pin(inventory.Entries.Single()), selection.Confirmed, geometry.Candidate);
        }
    }
    public static void Run() {
        EndpointPersistsAsFixedPlacement();
        SelectedCurveSuppliesTheClosestPoint();
        TrackChangeDuringObjectReadRefusesTheWholeEdit();
        ObjectReplacementDuringTrackCheckRefusesMutation();
        MissingAmbiguousAndChangedTargetsRefuseWithoutMutation();
        FailedSnapRestoresAndGuardsRetry();
        CurveEndsAndInvalidGeometry();
        SnapAndGizmoShareFixedHistoryWithoutPersistingPreviews();
        Console.WriteLine("PASS: explicit snap preflight, fixed documents, unchanged binding, persistence, repeat and restoration");
    }
    static void SnapAndGizmoShareFixedHistoryWithoutPersistingPreviews() {
        var w = new World(); var node = Node(); w.Tracks.Items.Add(node);
        var binding = w.Box.LogicalTrack;
        var request = w.Prepare(node); var document = w.Session.DocumentText;
        var inventory = new SceneInventory(w.Scene); inventory.Refresh();
        var gesture = new PlacementGesture(w.Engine, w.Session, ObjectId, inventory.Pin(inventory.Entries.Single()));
        Assert(gesture.Preview(new Placement(new[] { 116.0, 8.0, 9.0 }, w.Box.Rotation)).Applied, "A gizmo preview uses the shared placement adapter");
        var calls = w.Scene.MutationCalls;
        Assert(!w.Snap.Apply(request).Applied && w.Box.X == 16 && w.Scene.MutationCalls == calls && w.Session.DocumentText == document,
            "A snap prepared against the earlier inspection cannot overwrite a changed preview");
        Assert(gesture.Cancel().Applied && w.Box.X == 4 && w.Editor.UndoGroups.Count == 0 && w.Session.DocumentText == document,
            "Cancelling the preview preserves the original complete document and history");
        Assert(w.Snap.Apply(w.Prepare(node)).Applied && w.Box.X == 20, "A fresh snap follows the cancelled gesture");
        inventory.Refresh();
        gesture = new PlacementGesture(w.Engine, w.Session, ObjectId, inventory.Pin(inventory.Entries.Single()));
        Assert(gesture.Preview(new Placement(new[] { 135.0, 3.0, 5.0 }, w.Box.Rotation)).Applied && gesture.Complete().Applied,
            "A completed gizmo gesture follows the snapped fixed placement");
        Assert(w.Box.X == 35 && w.Editor.UndoGroups.Count == 2 && ReferenceEquals(binding, w.Box.LogicalTrack),
            "Snap and completed gesture each add one history operation and retain the logical binding");
        w.Editor.Undo(); Assert(w.Box.X == 20, "Undoing the gesture recovers the snapped pose");
        w.Editor.Undo(); Assert(w.Box.X == 4 && JObject.Parse(w.Session.DocumentText)["modifications"].Count() == 0, "Undoing the snap recovers underlying placement and an empty document");
        w.Editor.Redo(); w.Editor.Redo();
        Assert(w.Box.X == 35 && w.Editor.Save() == null, "Redo and shared Save persist the final gesture result");
        var fresh = new Scene(); var box = new Box(); fresh.Objects["p"] = new List<object> { box }; var engine = new PatchEngine(fresh);
        Assert(engine.Apply(w.Store.Patch).Outcomes.Values.Single().Applied && engine.Apply(w.Store.Patch).Outcomes.Values.Single().Applied && box.X == 35,
            "The combined history saves one absolute fixed result that replays without accumulated offsets");
    }
    static void ObjectReplacementDuringTrackCheckRefusesMutation() {
        var w = new World(); var node = Node(); w.Tracks.Items.Add(node); var request = w.Prepare(node);
        var scans = 0; var replacement = new BoundBox();
        w.Tracks.BeforeScan = () => { if (++scans == 2) w.Scene.Objects["p"] = new List<object> { replacement }; };
        var result = w.Snap.Apply(request);
        Assert(!result.Applied && w.Scene.MutationCalls == 0 && w.Box.X == 4 && replacement.X == 0,
            "A native callback during final track discovery cannot mutate the replaced object: " + result.Error);
    }
    static SnapGeometry Node(object handle = null, double x = 120) => new SnapGeometry(
        new SelectionCandidate(handle ?? new object(), SelectionIdentity.Graph(SelectionKind.TrackNode, "node-a"), "Node A"),
        new[] { new[] { x, 3.0, 5.0 } });
    static void MissingAmbiguousAndChangedTargetsRefuseWithoutMutation() {
        foreach (var change in new[] { "missing", "ambiguous", "replacement", "geometry", "map", "object" }) {
            var w = new World(); var node = Node(); w.Tracks.Items.Add(node);
            Assert(w.Session.Edit(ObjectId, new Placement(new[] { 106.0, 8.0, 9.0 }, w.Box.Rotation), null).Applied, "Independent prior edit succeeds");
            var request = w.Prepare(node); var document = w.Session.DocumentText; var calls = w.Scene.MutationCalls;
            switch (change) {
                case "missing": w.Tracks.Items.Clear(); break;
                case "ambiguous": w.Tracks.Items.Add(Node()); break;
                case "replacement": w.Tracks.Items[0] = Node(); break;
                case "geometry": w.Tracks.Items[0] = Node(node.Candidate.Handle, 130); break;
                case "map": w.Scene.Maps = new[] { "other" }; break;
                case "object": w.Scene.Objects["p"] = new List<object> { new BoundBox() }; break;
            }
            var result = w.Snap.Apply(request);
            Assert(!result.Applied && result.Error != null && w.Scene.MutationCalls == calls && w.Box.X == 6,
                change + " refuses snap without partial mutation or substitution: " + result.Error);
            Assert(w.Session.DocumentText == document && w.Editor.UndoGroups.Count == 1, change + " preserves earlier work and history");
            // A refused selection is not encoded into a fixed document. Replaying the retained
            // complete document reproduces prior authored work, without a runtime graph resolver.
            var fresh = new Scene(); var box = new Box(); fresh.Objects["p"] = new List<object> { box };
            Assert(new PatchEngine(fresh).Apply(document).Outcomes.Values.Single().Applied && box.X == 6,
                change + " leaves the complete document valid with prior authored placement");
        }
    }
    static void FailedSnapRestoresAndGuardsRetry() {
        var w = new World(); var node = Node(); w.Tracks.Items.Add(node);
        var request = w.Prepare(node); var original = w.Session.DocumentText; var binding = w.Box.LogicalTrack;
        w.Scene.FailAfterDependentUpdate = 1;
        var result = w.Snap.Apply(request);
        Assert(!result.Applied && result.Error.Contains("prior state was restored") && w.Box.X == 4 && w.Box.DependentX == 4 && ReferenceEquals(binding, w.Box.LogicalTrack),
            "Failed snap restores captured position and dependent state while retaining the logical reference: " + result.Error);
        Assert(w.Session.DocumentText == original && w.Editor.UndoGroups.Count == 0, "Failed snap preserves the complete prior document and history");
        w.Scene.FailAfterDependentUpdate = 2;
        result = w.Snap.Apply(request);
        Assert(!result.Applied && result.Error.Contains("cleanup is unverified"), "Incomplete restoration is reported");
        var calls = w.Scene.MutationCalls; result = w.Snap.Apply(w.Prepare(node));
        Assert(!result.Applied && result.Error.Contains("cleanup is unverified") && w.Scene.MutationCalls == calls, "Unverified cleanup guards another snap");
        Assert(w.Session.Recheck(ObjectId).Applied && w.Snap.Apply(w.Prepare(node)).Applied, "Explicit verified recovery permits a new snap");
        Assert(w.Box.DependentX == 20 && ReferenceEquals(binding, w.Box.LogicalTrack), "Successful snap updates dependent state without changing logical track");
        var document = w.Session.DocumentText;
        w.Scene.FailAfterDependentUpdate = 1;
        var outcome = new PatchEngine(w.Scene).Apply(document).Outcomes.Values.Single();
        Assert(!outcome.Applied && outcome.Error.Contains("restored") && w.Box.X == 20 && w.Box.DependentX == 20 && ReferenceEquals(binding, w.Box.LogicalTrack),
            "Complete-document failed application also restores dependent state and binding");
        w.Editor.Save();
        Assert(w.Store.Patch == document, "Save after recovery writes only the successful snap result");
    }
    static void CurveEndsAndInvalidGeometry() {
        foreach (var x in new[] { 90.0, 115.0, 104.0 }) {
            var w = new World(); w.Box.Position = new[] { x - 100, 5.0, 0.0 };
            var line = new SnapGeometry(new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, "line"), "Line"),
                new[] { new[] { 100.0, 0.0, 0.0 }, new[] { 103.0, 0.0, 0.0 }, new[] { 107.0, 0.0, 0.0 }, new[] { 110.0, 0.0, 0.0 } });
            w.Tracks.Items.Add(line); var result = w.Snap.Apply(w.Prepare(line));
            var expected = x < 100 ? 0 : x > 110 ? 10 : 4;
            Assert(result.Applied && Math.Abs(w.Box.X - expected) < 0.000001, "Closest point includes the selected segment's bounds");
        }
        var invalid = new World(); var bad = Node(x: Double.NaN); invalid.Tracks.Items.Add(bad);
        try { invalid.Prepare(bad); throw new Exception("Nonfinite geometry accepted"); }
        catch (InvalidOperationException e) { Assert(e.Message.Contains("finite"), "Invalid geometry reports a concrete refusal"); }
        Assert(invalid.Scene.MutationCalls == 0 && JObject.Parse(invalid.Session.DocumentText)["modifications"].Count() == 0, "Invalid geometry leaves the complete document and object untouched");
    }
    static void SelectedCurveSuppliesTheClosestPoint() {
        var w = new World(); w.Box.Position = new[] { 2.16, 5.0, 6.3 };
        var curve = new SnapGeometry(new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, "curve-a"), "Curve A"),
            new[] { new[] { 100.0, 0.0, 0.0 }, new[] { 100.0, 0.0, 10.0 }, new[] { 110.0, 0.0, 10.0 }, new[] { 110.0, 0.0, 0.0 } });
        w.Tracks.Items.Add(curve);
        var result = w.Snap.Apply(w.Prepare(curve));
        Assert(result.Applied && Math.Abs(w.Box.X - 2.16) < 0.000001 && Math.Abs(w.Box.Position[1]) < 0.000001 && Math.Abs(w.Box.Position[2] - 6.3) < 0.000001,
            "Selected cubic curve supplies its closest point rather than a sampled display vertex: " + result.Error);
        var document = w.Session.DocumentText;
        var other = new BoundBox(); w.Scene.Objects["p"] = new List<object> { other };
        var replay = new PatchEngine(w.Scene).Apply(document).Outcomes.Values.Single();
        Assert(replay.Applied && Math.Abs(other.Position[2] - 6.3) < 0.000001,
            "The complete document reproduces the curve snap independently of selection: " + replay.Error);
    }
    static void TrackChangeDuringObjectReadRefusesTheWholeEdit() {
        var w = new World();
        var node = new SnapGeometry(new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackNode, "node-a"), "Node A"),
            new[] { new[] { 120.0, 3.0, 5.0 } });
        w.Tracks.Items.Add(node); var request = w.Prepare(node); var document = w.Session.DocumentText;
        w.Scene.BeforeRead = () => w.Tracks.Items.Clear();
        var result = w.Snap.Apply(request);
        Assert(!result.Applied && result.Error.Contains("Snap target is missing") && w.Scene.MutationCalls == 0 && w.Box.X == 4,
            "The track is rechecked after object callbacks and before mutation: " + result.Error);
        Assert(w.Session.DocumentText == document && w.Editor.UndoGroups.Count == 0, "Pre-mutation refusal preserves the complete document and history");
    }
    static void EndpointPersistsAsFixedPlacement() {
        var w = new World();
        var node = new SnapGeometry(new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackNode, "node-a"), "Node A"),
            new[] { new[] { 120.0, 3.0, 5.0 } });
        w.Tracks.Items.Add(node);
        var binding = w.Box.LogicalTrack;
        var request = w.Prepare(node);
        Assert(w.Box.X == 4 && w.Editor.UndoGroups.Count == 0 && JObject.Parse(w.Session.DocumentText)["modifications"].Count() == 0,
            "Preparing and inspecting a snap has no mutation, persistence or history side effects");
        var result = w.Snap.Apply(request);
        Assert(result.Applied && w.Box.Position.SequenceEqual(new[] { 20.0, 3.0, 5.0 }), "Endpoint snap uses the selected node's map coordinates: " + result.Error);
        Assert(w.Box.Rotation.SequenceEqual(new[] { 0.0, 1.0, 0.0, 0.0 }), "Snap preserves the object's rotation");
        Assert(ReferenceEquals(binding, w.Box.LogicalTrack) && w.Box.DependentX == 20, "Endpoint snap retains the actual logical reference and updates dependent state");
        var item = JObject.Parse(w.Session.DocumentText)["modifications"].Single();
        Assert((string)item["placement"]["mode"] == "fixed" && item["binding"] == null, "The complete canonical document stores fixed placement without a track binding");
        Assert(w.Editor.UndoGroups.Count == 1, "One snap is one history operation");
        w.Editor.Undo(); Assert(w.Box.Position.SequenceEqual(new[] { 4.0, 8.0, 9.0 }), "Undo restores the original position");
        w.Editor.Redo(); Assert(w.Box.X == 20, "Redo restores the authored snap result");
        Assert(w.Editor.Save() == null && w.Store.Patch == w.Session.DocumentText, "Native Save participants persist the canonical snap document");
        w.Tracks.Items.Clear();
        var fresh = new Scene { Origin = 110 }; var box = new Box(); fresh.Objects["p"] = new List<object> { box };
        var engine = new PatchEngine(fresh);
        Assert(engine.Apply(w.Store.Patch).Outcomes.Values.Single().Applied && box.X == 10, "Reload needs no graph lookup for a fixed snap result and uses the current origin");
        Assert(engine.Apply(w.Store.Patch).Outcomes.Values.Single().Applied && box.X == 10, "Repeated whole-document application does not accumulate offsets");
    }
}
