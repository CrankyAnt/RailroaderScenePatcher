// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

static class SelectionTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class Candidates : ISelectionScene {
        public string[] Maps = { "map", "layer" };
        public IEnumerable<string> MapIds => Maps;
        public readonly List<SelectionCandidate> Items = new List<SelectionCandidate>();
        public IEnumerable<SelectionCandidate> Discover(SelectionKind kind) => Items.Where(c => c.Identity.Kind == kind);
    }
    internal static void Run() {
        var scene = new Scene();
        var original = new Box { X = 7 }; var replacement = new Box { X = 9 };
        scene.Objects["original"] = new List<object> { original };
        scene.Objects["replacement"] = new List<object> { replacement };
        var candidates = new Candidates();
        var candidate = new SelectionCandidate(replacement, SelectionIdentity.SceneObject(new TargetIdentity("scene", "replacement", "Culler")), "Replacement board");
        candidates.Items.Add(candidate);
        var choice = new PendingSelection(candidates, SelectionKind.SceneObject, "Replace scene target");
        choice.Offer(new[] { candidate });
        Check(choice.Confirmed == null && choice.IsPending, "A spatial hit remains pending until its visible identity is explicitly confirmed");
        Check(choice.Confirm(), "One explicitly offered unique identity can be confirmed");
        Check(choice.Confirmed.Identity.Target.Path == "replacement" && !choice.IsPending, "Confirmation records the explicitly chosen scene identity");
        var document = JObject.Parse("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"move\",\"target\":{\"scene\":\"scene\",\"path\":\"original\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}]}");
        var target = choice.Confirmed.Identity.Target;
        document["modifications"][0]["target"] = new JObject { ["scene"] = target.Scene, ["path"] = target.Path, ["component"] = target.Component };
        var result = new PatchEngine(scene).Apply(document.ToString());
        Check(result.Outcomes["move"].Applied && ReferenceEquals(result.Outcomes["move"].ResolvedTarget, replacement) && original.X == 7 && replacement.X == 12,
            "Complete-document application uses the confirmed replacement identity and preserves the previous target");
        Console.WriteLine("PASS: confirmed spatial scene-target identity and complete-document application");
        UnresolvedSceneTargets(scene, document.ToString(), candidates, candidate, original, replacement);
        ReplacementChoices(scene, document.ToString(), candidates, candidate, result, original, replacement);
        AuthoringHistory();
        ConfirmationFindsOneIdentity();
    }
    // A scene that can find one identity confirms without discovering every candidate, and still refuses a candidate
    // that changed, disappeared or became ambiguous.
    sealed class FindingCandidates : ITargetedSelectionScene {
        public IEnumerable<string> MapIds => new[] { "map" };
        public readonly List<SelectionCandidate> Items = new List<SelectionCandidate>();
        public int Discoveries, Finds;
        public IEnumerable<SelectionCandidate> Discover(SelectionKind kind) { Discoveries++; return Items.Where(c => c.Identity.Kind == kind); }
        // As the native scene does: every candidate at the identity's place, whatever its classification reads now.
        public IEnumerable<SelectionCandidate> Find(SelectionIdentity identity) {
            Finds++;
            return Items.Where(c => c.Identity.Kind == identity.Kind && (identity.Target == null ? c.Identity.Identifier == identity.Identifier
                : c.Identity.Target.Scene == identity.Target.Scene && c.Identity.Target.Path == identity.Target.Path && c.Identity.Target.Component == identity.Target.Component)).ToArray();
        }
    }
    static void ConfirmationFindsOneIdentity() {
        var scene = new FindingCandidates();
        object board = new object(), track = new object();
        var source = new SelectionCandidate(board, SelectionIdentity.SceneObject(new TargetIdentity("scene", "board", "Culler")), "Board");
        var segment = new SelectionCandidate(track, SelectionIdentity.Graph(SelectionKind.TrackSegment, "S1"), "S1", "geometry A");
        scene.Items.Add(source); scene.Items.Add(segment);
        var choice = new PendingSelection(scene, SelectionKind.TrackSegment, "Select replacement track segment", source);
        choice.Offer(new[] { segment });
        Check(choice.Confirm() && scene.Discoveries == 0 && scene.Finds == 2, "Confirmation finds the source and the candidate and discovers nothing: " + scene.Discoveries + "/" + scene.Finds);
        // The source object is gone.
        choice = new PendingSelection(scene, SelectionKind.TrackSegment, "Select replacement track segment", source);
        choice.Offer(new[] { segment }); scene.Items.Remove(source);
        Check(!choice.Confirm() && choice.Problem == "The source object changed or no longer resolves uniquely: it is no longer found. Cancel and inspect it again.", "A source that no longer resolves refuses the confirmation and says so: " + choice.Problem);
        scene.Items.Add(source);
        // The candidate's geometry changed.
        choice = new PendingSelection(scene, SelectionKind.TrackSegment, "Select replacement track segment", source);
        choice.Offer(new[] { segment });
        scene.Items.Remove(segment); scene.Items.Add(new SelectionCandidate(track, segment.Identity, "S1", "geometry B"));
        Check(!choice.Confirm() && choice.Problem == "The candidate changed or no longer resolves uniquely: its observed geometry changed. Pick again.", "A changed candidate refuses the confirmation and names the change: " + choice.Problem);
        // A second object at the source's identity.
        scene.Items.Clear(); scene.Items.Add(source); scene.Items.Add(segment);
        choice = new PendingSelection(scene, SelectionKind.TrackSegment, "Select replacement track segment", source);
        choice.Offer(new[] { segment }); scene.Items.Add(new SelectionCandidate(new object(), source.Identity, "Board"));
        Check(!choice.Confirm() && scene.Discoveries == 0 && choice.Problem == "The inspected object shares its identity scene/board [Culler] with another object, so a patch cannot target it. Cancel; a choice for this object cannot be confirmed.",
            "A source that shares its identity refuses the confirmation, names the identity and does not ask to inspect again: " + choice.Problem);
        // A candidate that shares its identity with another object.
        scene.Items.Clear(); scene.Items.Add(source); scene.Items.Add(new SelectionCandidate(new object(), source.Identity, "Board"));
        choice = new PendingSelection(scene, SelectionKind.SceneObject, "Select scene object");
        choice.Offer(new[] { source });
        Check(!choice.Confirm() && choice.Problem == "The candidate shares its identity scene/board [Culler] with another object, so a patch cannot target it. Pick an object whose path is unique.",
            "A candidate that shares its identity is refused with what to pick instead: " + choice.Problem);
        // The same object under another classification, as when its supported edits changed.
        scene.Items.Clear(); scene.Items.Add(new SelectionCandidate(board, SelectionIdentity.SceneObject(new TargetIdentity("scene", "board", "Culler", "structure")), "Board")); scene.Items.Add(segment);
        choice = new PendingSelection(scene, SelectionKind.TrackSegment, "Select replacement track segment", source);
        choice.Offer(new[] { segment });
        Check(!choice.Confirm() && choice.Problem.Contains("its identity or its supported edits changed"), "A source whose classification changed is named as such: " + choice.Problem);
        // An object chosen without a source object, as by Select in world: one find, for the candidate.
        scene.Items.Clear(); scene.Items.Add(source); scene.Finds = 0; scene.Discoveries = 0;
        choice = new PendingSelection(scene, SelectionKind.SceneObject, "Select scene object");
        choice.Offer(new[] { source });
        Check(choice.Confirm() && scene.Finds == 1 && scene.Discoveries == 0, "Confirming an object without a source finds the candidate once: " + scene.Finds + "/" + scene.Discoveries);
        Console.WriteLine("PASS: confirmation by finding one identity");
    }

    static void AuthoringHistory() {
        var scene = new Scene();
        var board = new Box { X = 7, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        var target = new TargetIdentity("s", "board", "c");
        scene.Objects[target.Path] = new List<object> { board };
        scene.Observations = () => new[] {
            new SceneObservation(board, target, "Board", board.Active, board.Position, board.Rotation,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "Fixed placement")
        };
        var inventory = new SceneInventory(scene);
        var store = new Store();
        var editor = new Editor();
        var authoring = new AuthoringSession(new PatchEngine(scene), editor, store, "map");
        editor.Session = authoring;
        var candidates = new Candidates();
        var source = new SelectionCandidate(board, SelectionIdentity.SceneObject(target), "Board");
        var track = new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, "S-replacement"), "Track segment S-replacement");
        candidates.Items.AddRange(new[] { source, track });
        var choices = new SelectionSession(candidates);
        choices.Begin(SelectionKind.SceneObject, "Select scene object");
        choices.Pending.Offer(new[] { source });
        Check(choices.Confirm(), "A scene object must be confirmed before using its identity for placement");
        var confirmed = choices.Confirmed;
        inventory.Refresh();
        var placement = new Placement(new[] { 112.0, 2.0, 3.0 }, new[] { 0.0, 0.0, 0.0, 1.0 });
        var edit = authoring.Edit(confirmed.Identity.Target, placement, null, inventory.Pin(inventory.Entries.Single()));
        Check(edit.Applied && board.X == 12 && editor.CanUndo, "Placement uses the confirmed identity, a fresh inspection and shared history");
        Check(editor.Save() == null && !editor.IsDirty, "Native Save persists the authored placement");
        editor.NativeEdit(1);
        editor.Undo();
        var undo = editor.UndoGroups.ToArray();
        var redo = editor.RedoGroups.ToArray();
        var document = authoring.DocumentText;
        var saved = store.Patch;
        var dirty = editor.IsDirty;
        var writes = store.PatchWrites;
        choices.Begin(SelectionKind.TrackSegment, "Replace track segment", source);
        choices.Pending.Offer(new[] { track });
        choices.Cancel();
        Check(ReferenceEquals(confirmed, choices.Confirmed) && choices.Pending == null &&
            editor.UndoGroups.SequenceEqual(undo) && editor.RedoGroups.SequenceEqual(redo) &&
            undo.Length > 0 && redo.Length > 0 && editor.IsDirty == dirty &&
            authoring.DocumentText == document && store.Patch == saved && store.PatchWrites == writes && board.X == 12,
            "Cancel preserves the earlier confirmation, authored and saved documents, pose, dirty state, and both history stacks");
        editor.Undo();
        Check(board.X == 7, "The prior placement is still undoable after cancellation");
        editor.Redo();
        Check(board.X == 12, "The prior placement is still redoable after cancellation");
        Console.WriteLine("PASS: confirmed scene target authoring and cancellation with shared history and saved placement");
    }

    static void UnresolvedSceneTargets(Scene scene, string json, Candidates candidates, SelectionCandidate candidate, Box original, Box replacement) {
        var saved = PatchDocument.Load(json);
        var session = new SelectionSession(candidates);
        var mutations = scene.MutationCalls;

        session.Begin(SelectionKind.SceneObject, "Replace scene target");
        session.Pending.Offer(Array.Empty<SelectionCandidate>());
        Check(!session.Confirm() && session.Pending.Candidate == null, "A world click without a hit leaves the document operation unresolved");
        session.Pending.Offer(new[] { candidate });
        session.Cancel();
        Check(session.Pending == null && session.Confirmed == null && saved.Write() == json && scene.MutationCalls == mutations &&
            original.X == 7 && replacement.X == 12, "Cancelling a pending complete-document target choice preserves the document and world");

        var duplicate = new Box { X = 25 };
        var duplicateCandidate = new SelectionCandidate(duplicate, candidate.Identity, "Duplicate board");
        candidates.Items.Add(duplicateCandidate);
        scene.Objects["replacement"].Add(duplicate);
        session.Begin(SelectionKind.SceneObject, "Replace scene target");
        session.Pending.Offer(new[] { candidate });
        Check(!session.Confirm() && session.Confirmed == null && session.Pending.Candidate == null,
            "A hit does not authorize an identity with a duplicate elsewhere in the world");
        var rejected = new PatchEngine(scene).Apply(saved.Write());
        Check(!rejected.Outcomes["move"].Applied && scene.MutationCalls == mutations && original.X == 7 && replacement.X == 12 && duplicate.X == 25,
            "The complete-document engine also refuses the ambiguous identity without moving either match");
        candidates.Items.Remove(duplicateCandidate);
        scene.Objects["replacement"].Remove(duplicate);
        session.Cancel();
        var other = new SelectionCandidate(original, SelectionIdentity.SceneObject(new TargetIdentity("scene", "original", "Culler")), "Original board");
        candidates.Items.Add(other);
        session.Begin(SelectionKind.SceneObject, "Replace scene target", candidate);
        session.Pending.Offer(new[] { candidate, other });
        Check(session.Pending.Source == candidate && session.Pending.Candidate == null && !session.Confirm(),
            "An offered source remains an unchosen choice while scene-object hits overlap");
        session.Pending.Choose(other);
        Check(session.Pending.Candidate == other && session.Pending.Source == candidate && session.Confirmed == null,
            "Choosing another object leaves the source unchosen and requires confirmation");
        session.Pending.Choose(candidate);
        Check(session.Pending.Candidate == candidate && session.Pending.Source == candidate && session.Confirmed == null,
            "An explicit choice can make the source the candidate without confirming it");
        session.Cancel();
        Check(saved.Write() == json && scene.MutationCalls == mutations && original.X == 7 && replacement.X == 12,
            "Cancelling source-in-overlap choices preserves the whole document and both scene objects");
    }

    static void ReplacementChoices(Scene scene, string json, Candidates candidates, SelectionCandidate source, DocumentResult completed,
        Box original, Box replacement) {
        var document = PatchDocument.Load(json);
        var mutations = scene.MutationCalls;
        var first = new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, "S-original"), "Track segment S-original", "endpoints A/B");
        var intended = new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, "S-verified"), "Track segment S-verified", "endpoints B/C");
        var node = new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackNode, "S-verified"), "Track node S-verified", "position B");
        candidates.Items.AddRange(new[] { first, intended, node });
        var session = new SelectionSession(candidates);

        session.Begin(SelectionKind.TrackSegment, "Replace track segment", source);
        session.Pending.Offer(new[] { first, intended });
        Check(!session.Confirm() && session.Pending.Candidate == null && session.Pending.Offers.Count == 2,
            "Overlapping segment markings require an explicit choice, regardless of candidate order");
        session.Pending.Choose(intended);
        Check(session.Pending.Source == source && session.Confirmed == null, "Choosing a candidate keeps the source pending until explicit confirmation");
        Check(session.Confirm() && session.Pending == null && session.Confirmed.Identity.Kind == SelectionKind.TrackSegment &&
            session.Confirmed.Identity.Identifier == "S-verified" && session.Confirmed.Source.Equals(source.Identity) &&
            session.Confirmed.MapIds.SequenceEqual(new[] { "map", "layer" }),
            "The production selection session records the explicitly chosen segment, source and map context");
        var previous = session.Confirmed;

        session.Begin(SelectionKind.TrackNode, "Replace track node", source);
        session.Pending.Offer(new[] { node });
        session.Cancel();
        Check(session.Pending == null && ReferenceEquals(session.Confirmed, previous),
            "Cancelling a separate node choice keeps the completed segment handoff");

        session.Begin(SelectionKind.TrackNode, "Replace track node", source);
        session.Pending.Offer(new[] { node });
        candidates.Items.Remove(node);
        Check(!session.Confirm() && session.Pending.Candidate == null && ReferenceEquals(session.Confirmed, previous),
            "A node that disappears while pending stays unresolved and does not overwrite a confirmed choice");
        candidates.Items.Add(node);
        session.Pending.Offer(new[] { node });
        candidates.Maps = new[] { "map", "changed-layer" };
        Check(!session.Confirm() && ReferenceEquals(session.Confirmed, previous), "Changing the loaded layers invalidates confirmation");
        candidates.Maps = new[] { "map", "layer" };
        session.Cancel();

        session.Begin(SelectionKind.TrackNode, "Replace track node", source);
        session.Pending.Offer(new[] { node });
        candidates.Items.Remove(node);
        var moved = new SelectionCandidate(node.Handle, node.Identity, node.Label, "position C");
        candidates.Items.Add(moved);
        Check(!session.Confirm() && ReferenceEquals(session.Confirmed, previous), "Changed node geometry requires another explicit pick");
        session.Pending.Offer(new[] { moved });
        Check(session.Confirm() && session.Confirmed.Identity.Kind == SelectionKind.TrackNode && session.Confirmed.Identity.Identifier == "S-verified",
            "Confirmation records the node as a node even when its identifier text matches a segment");
        Check(document.Write() == json && scene.MutationCalls == mutations && original.X == 7 && replacement.X == 12 &&
            completed.Outcomes["move"].Applied && ReferenceEquals(completed.Outcomes["move"].ResolvedTarget, replacement),
            "Replacement choices preserve the complete document, world state and previously applied edit");
        Console.WriteLine("PASS: explicit segment and node handoff, cancellation, ambiguity and revalidation");
    }
}
