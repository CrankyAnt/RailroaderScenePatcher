// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
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
        // Several candidates under one click are numbered in the order they are offered; the panel can point at one.
        var several = new PendingSelection(candidates, SelectionKind.SceneObject, "Replace scene target");
        var twin = new SelectionCandidate(original, SelectionIdentity.SceneObject(new TargetIdentity("scene", "original", "Culler")), "Original board");
        several.Offer(new[] { candidate, twin });
        Check(several.Candidate == null && several.Problem == "Select a row from the list next to the pointer.", "A group or overlapping offers direct the choice to the list without claiming the objects share one spot");
        Check(several.OfferCountText + " " + several.OfferListInstruction == "2 objects. Select a row from the list next to the pointer.",
            "Overlapping object markers have a generic footer");
        Check(several.Number(candidate) == 1 && several.Number(twin) == 2 && several.Number(new SelectionCandidate(original, twin.Identity, "Other")) == 0, "Offers carry their numbers from 1; a candidate that is not offered has none");
        // The list of one spot closes without a choice, as a dropdown does on Escape: the choice stays pending.
        Check(several.ListOpen, "Several offers without a choice are an open list");
        several.CloseList();
        Check(several.IsPending && !several.ListOpen && several.Offers.Count == 0 && several.Candidate == null && several.Problem == null, "Closing the list offers nothing and keeps the choice pending");
        several.CloseList();
        Check(several.IsPending, "Closing a list that is not open changes nothing");
        several.Offer(new[] { candidate, twin });
        several.Highlight = twin;
        Check(several.Candidate == null && several.Problem == PendingSelection.SeveralOffers, "Pointing at an offer chooses nothing");
        several.Offer(new[] { candidate, twin });
        Check(several.Highlight == null, "Replacing an offer list releases the hover, even if the same object is offered again");
        several.Choose(twin);
        Check(ReferenceEquals(several.Candidate, twin) && several.Problem == null && several.Number(twin) == 2, "A chosen offer keeps its number");
        Check(!several.ListOpen, "With a candidate chosen no list is open");
        several.CloseList();
        Check(ReferenceEquals(several.Candidate, twin) && several.Offers.Count == 2, "Closing the list after a choice keeps the choice");
        Check(choice.Confirmed == null && choice.IsPending, "A spatial hit remains pending until its visible identity is explicitly confirmed");
        choice.Highlight = candidate;
        Check(choice.Confirm(), "One explicitly offered unique identity can be confirmed");
        Check(choice.Highlight == null && choice.Candidate == null, "Confirming releases the candidate and its transient hover");
        Check(choice.Confirmed.Identity.Target.Path == "replacement" && !choice.IsPending, "Confirmation records the explicitly chosen scene identity");
        var inspection = new SelectionSession(candidates);
        inspection.Begin(SelectionKind.SceneObject, "Select scene object", inspectOnly: true);
        inspection.Pending.Offer(new[] { candidate });
        Check(inspection.Confirm(), "Inspect the sign before clearing the inspection");
        inspection.Clear();
        Check(inspection.Confirmed == null && inspection.Pending == null, "Clear drops the previous confirmed inspection even with no pending choice");
        inspection.Begin(SelectionKind.SceneObject, "Select scene object", inspectOnly: true);
        Check(inspection.Pending.Candidate == null && inspection.Pending.Highlight == null, "A fresh inspection pick starts without a clicked or hovered candidate");
        ConfirmationEndsWhenInspectionChanges();
        var overlapping = new[] { new PickTarget(100, 100, true, false), new PickTarget(111, 100, true, false) };
        var overSign = WorldPick.Resolve(89, 100, overlapping, null);
        var offSign = WorldPick.Resolve(122, 100, overlapping, null);
        Check(overSign.Objects.Contains(0) && offSign.Objects.Contains(0), "Both markers still share the choice list when either is clicked");
        Check(overSign.HoveredObjects.SequenceEqual(new[] { 0 }) && offSign.HoveredObjects.SequenceEqual(new[] { 1 }),
            "An overlapping sign loses world hover when the pointer leaves its own marking");
        var scenePatchFile = JObject.Parse("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"scene\",\"path\":\"original\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}");
        var target = choice.Confirmed.Identity.Target;
        scenePatchFile["modifications"].First.First["target"] = new JObject { ["scene"] = target.Scene, ["path"] = target.Path, ["component"] = target.Component };
        var result = new PatchEngine(scene).Apply(scenePatchFile.ToString());
        Check(result.Outcomes["move"].Applied && ReferenceEquals(result.Outcomes["move"].ResolvedTarget, replacement) && original.X == 7 && replacement.X == 12,
            "Complete scene patch file application uses the confirmed replacement identity and preserves the previous target");
        Console.WriteLine("PASS: confirmed spatial scene-target identity and complete scene patch file application");
        // Each sub-test starts from its own applied replacement, so no candidate or scene object carries over.
        var unresolved = AppliedReplacement();
        UnresolvedSceneTargets(unresolved.Scene, unresolved.Json, unresolved.Candidates, unresolved.Candidate, unresolved.Original, unresolved.Replacement);
        var replacing = AppliedReplacement();
        ReplacementChoices(replacing.Scene, replacing.Json, replacing.Candidates, replacing.Candidate, replacing.Result, replacing.Original, replacing.Replacement);
        PatchEditingHistory();
        ConfirmationFindsOneIdentity();
        CurrentBindingIsNoCandidate();
    }
    // A binding slot's current binding cannot be picked as a candidate: a crossing pad's marker segment, like a coverage
    // role's segment, is left out of the slot's choice, and a click on it says why nothing is offered.
    static void CurrentBindingIsNoCandidate() {
        var pad = new[] { "Marker segment=Sjh7; end=a; distance=53.561", "Marker binding note: x", "Lower segment=S2" };
        Check(CommandBand.BindingSlotSegments(pad).SequenceEqual(new[] { "Sjh7" }), "A marker's binding slot holds its marker segment only: " + String.Join(",", CommandBand.BindingSlotSegments(pad)));
        Check(CommandBand.BindingSlotSegments(new[] { "Marker segment=; end=a; distance=" }).Count == 0, "A detached marker has no current segment to leave out");
        var candidates = new Candidates();
        var choice = new PendingSelection(candidates, SelectionKind.TrackSegment, "Select replacement track segment");
        var current = new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, "Sjh7"), "Sjh7");
        choice.Offer(Array.Empty<SelectionCandidate>(), currentBindings: new[] { current });
        Check(choice.Candidate == null && choice.Problem == "No candidate at this point. Track segment Sjh7 is the current binding, so it can't be picked.",
            "A click on the current binding names it as the reason: " + choice.Problem);
        Check(PendingSelection.IsInstruction(choice.Problem), "The current binding reason is ordinary text, not a defect");
        choice.Offer(Array.Empty<SelectionCandidate>());
        Check(choice.Problem == PendingSelection.NoOffer && PendingSelection.NoOffer.EndsWith(".", StringComparison.Ordinal) && PendingSelection.NoOffer.Length > "No candidate at this point.".Length,
            "A click that met nothing gives its reason and ends with a period: " + choice.Problem);
        var other = new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, "Sp3f"), "Sp3f");
        choice.Offer(new[] { other }, currentBindings: new[] { current });
        Check(ReferenceEquals(choice.Candidate, other) && choice.Problem == null, "Another segment is offered normally");
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
    static void ConfirmationEndsWhenInspectionChanges() {
        var scene = new Candidates();
        var boardId = SelectionIdentity.SceneObject(new TargetIdentity("scene", "Bryson House Track", "Culler"));
        var board = new SelectionCandidate(new object(), boardId, "Bryson House Track");
        var track = new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, "Szqa"), "Track Szqa");
        scene.Items.Add(board);
        scene.Items.Add(track);
        var session = new SelectionSession(scene);

        session.Begin(SelectionKind.SceneObject, "Select in world", inspectOnly: true);
        session.Pending.Offer(new[] { board });
        Check(session.Confirm(), "Confirm the object to inspect");
        var inspected = session.Confirmed;
        Check(!session.Inspect(board.Handle, boardId) && ReferenceEquals(inspected, session.Confirmed), "Confirming the inspection keeps the choice on that object");
        Check(session.Inspect(new object(), null) && session.Confirmed == null, "Inspecting another object ends an inspection-only confirmation");
        Check(!session.Inspect(board.Handle, boardId) && session.Confirmed == null, "Returning to the original object does not revive its choice");

        session.Begin(SelectionKind.TrackSegment, "Follow", board);
        session.Pending.Offer(new[] { track });
        Check(session.Confirm(), "Confirm a Follow target for the inspected object");
        var follow = session.Confirmed;
        Check(!session.Inspect(board.Handle, boardId) && ReferenceEquals(follow, session.Confirmed), "Keeping the same inspection retains its confirmed Follow target");
        Check(session.Inspect(new object(), null) && session.Confirmed == null, "A Follow target cannot carry to another live object");

        // One GameObject with two components gives two inventory entries with one handle. A pick confirmed for one
        // entry belongs to that entry only, while a refresh of the same entry keeps it.
        var signalId = SelectionIdentity.SceneObject(new TargetIdentity("scene", "Bryson House Track", "CrossingSignal"));
        session.Begin(SelectionKind.TrackSegment, "Picked segment", board);
        session.Pending.Offer(new[] { track });
        Check(session.Confirm(), "Confirm a segment for the first entry of the object");
        var picked = session.Confirmed;
        var refreshedId = SelectionIdentity.SceneObject(new TargetIdentity("scene", "Bryson House Track", "Culler"));
        Check(!session.Inspect(board.Handle, refreshedId) && ReferenceEquals(picked, session.Confirmed), "A refresh of the same entry keeps its confirmed segment");
        Check(picked.AppliesTo(board.Handle, refreshedId) && !picked.AppliesTo(board.Handle, signalId), "The confirmed segment applies to its own entry, not to the other entry of the same object");
        Check(session.Inspect(board.Handle, signalId) && session.Confirmed == null, "Switching to another entry of the same object ends the confirmation");
        Check(!session.Inspect(board.Handle, boardId) && session.Confirmed == null, "Returning to the first entry does not revive its confirmed segment");
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
        choice = new PendingSelection(scene, SelectionKind.SceneObject, "Select replacement scene object", segment);
        scene.Items.Add(segment);
        choice.Offer(new[] { source });
        Check(!choice.Confirm() && choice.Problem == "The candidate shares its identity scene/board [Culler] with another object, so a patch cannot target it. Pick an object whose path is unique.",
            "A replacement candidate that shares its identity is refused with what to pick instead: " + choice.Problem);
        // The same object chosen only to be inspected, as by Select in world: it is confirmed by itself, and its
        // supported edits then say why it cannot be edited.
        choice = new PendingSelection(scene, SelectionKind.SceneObject, "Select scene object") { InspectOnly = true };
        choice.Offer(new[] { source });
        Check(choice.Confirm() && choice.Problem == null, "An object that shares its identity can be chosen for inspection: " + choice.Problem);
        choice = new PendingSelection(scene, SelectionKind.SceneObject, "Replace scene target");
        choice.Offer(new[] { source });
        Check(!choice.Confirm(), "A choice a scene patch file will name refuses the shared identity also without a source object");
        // A choice a scene patch file must name offers no candidate with a shared identity; an inspecting choice offers all.
        var twin = new SelectionCandidate(new object(), source.Identity, "Board");
        Check(SelectionOffers.For(new[] { source, twin, segment }, false).SequenceEqual(new[] { segment }) && SelectionOffers.For(new[] { source, twin, segment }, true).Count == 3
            && SelectionOffers.For(new[] { source, segment }, false).Count == 2 && SelectionOffers.For(null, false).Count == 0, "A replacement choice offers only candidates a patch can name");
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

    static void PatchEditingHistory() {
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
        var mapEditor = new NativeMapEditor();
        var editing = new PatchEditingSession(new PatchEngine(scene), mapEditor, store);
        mapEditor.Session = editing;
        var candidates = new Candidates();
        var source = new SelectionCandidate(board, SelectionIdentity.SceneObject(target), "Board");
        var track = new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, "S-replacement"), "Track segment S-replacement");
        candidates.Items.AddRange(new[] { source, track });
        // A confirmation that an edit used is no longer confirmed.
        var used = new SelectionSession(candidates);
        used.Begin(SelectionKind.SceneObject, "Select scene object");
        used.Pending.Offer(new[] { source });
        Check(used.Confirm() && used.Confirmed != null, "A confirmed identity waits to be used");
        used.Consume();
        Check(used.Confirmed == null && used.Pending == null, "Once an edit used it, nothing is confirmed any more");
        var choices = new SelectionSession(candidates);
        choices.Begin(SelectionKind.SceneObject, "Select scene object");
        choices.Pending.Offer(new[] { source });
        Check(choices.Confirm(), "A scene object must be confirmed before using its identity for placement");
        var confirmed = choices.Confirmed;
        inventory.Refresh();
        var placement = new Placement(new[] { 112.0, 2.0, 3.0 }, new[] { 0.0, 0.0, 0.0, 1.0 });
        var edit = editing.Edit(confirmed.Identity.Target, placement, null, inventory.Pin(inventory.Entries.Single()));
        Check(edit.Applied && board.X == 12 && mapEditor.CanUndo, "Placement uses the confirmed identity, a fresh inspection and shared history");
        Check(mapEditor.Save() == null && !mapEditor.IsDirty, "Native Save persists this mod's placement");
        mapEditor.NativeEdit(1);
        mapEditor.Undo();
        var undo = mapEditor.UndoGroups.ToArray();
        var redo = mapEditor.RedoGroups.ToArray();
        var scenePatchFile = editing.ScenePatchFileText;
        var saved = store.Patch;
        var dirty = mapEditor.IsDirty;
        var writes = store.PatchWrites;
        choices.Begin(SelectionKind.TrackSegment, "Replace track segment", source);
        choices.Pending.Offer(new[] { track });
        choices.Cancel();
        Check(ReferenceEquals(confirmed, choices.Confirmed) && choices.Pending == null &&
            mapEditor.UndoGroups.SequenceEqual(undo) && mapEditor.RedoGroups.SequenceEqual(redo) &&
            undo.Length > 0 && redo.Length > 0 && mapEditor.IsDirty == dirty &&
            editing.ScenePatchFileText == scenePatchFile && store.Patch == saved && store.PatchWrites == writes && board.X == 12,
            "Cancel preserves the earlier confirmation, patch and saved scene patch files, placement, dirty state, and both history stacks");
        mapEditor.Undo();
        Check(board.X == 7, "The prior placement is still undoable after cancellation");
        mapEditor.Redo();
        Check(board.X == 12, "The prior placement is still redoable after cancellation");

        // The choice list of a game-defined group. Beyond 125 metres a grade-crossing setup shows one square marker;
        // the click rule picks the group there, and its objects are the offers of the list.
        var parts = new[] { "controller", "light", "gate" }.Select(name => new Box { X = 40 }).ToArray();
        var members = parts.Select((part, index) => new SelectionCandidate(part, SelectionIdentity.SceneObject(new TargetIdentity("s", "crossing/" + index, "c")), "Crossing part " + index)).ToArray();
        candidates.Items.AddRange(members);
        var offered = new[] { source }.Concat(members).ToArray();
        var marker = new PickTarget(300, 300, true, false);
        var crossing = new PickGroup(marker, new[] { 1, 2, 3 }, kind: GameDefinedGroupKind.GradeCrossingSetup);
        var picked = WorldPick.Resolve(301, 299, new[] { new PickTarget(900, 300, true, false), marker, marker, marker }, new[] { crossing });
        undo = mapEditor.UndoGroups.ToArray(); redo = mapEditor.RedoGroups.ToArray(); scenePatchFile = editing.ScenePatchFileText; saved = store.Patch; dirty = mapEditor.IsDirty; writes = store.PatchWrites;
        var mutations = scene.MutationCalls;
        bool Unchanged() => mapEditor.UndoGroups.SequenceEqual(undo) && mapEditor.RedoGroups.SequenceEqual(redo) && mapEditor.IsDirty == dirty && editing.ScenePatchFileText == scenePatchFile &&
            store.Patch == saved && store.PatchWrites == writes && scene.MutationCalls == mutations && board.X == 12 && parts.All(part => part.X == 40 && part.Active);
        choices.Begin(SelectionKind.SceneObject, "Select scene object", inspectOnly: true);
        choices.Pending.Offer(picked.Objects.Select(index => offered[index]), picked.IsCrossingSetup);
        Check(choices.Pending.ListOpen && choices.Pending.Offers.SequenceEqual(members) && choices.Pending.Candidate == null && Unchanged(),
            "A click that picks a group opens the choice list of the group's objects and changes nothing");
        Check(choices.Pending.OfferCountText + " " + choices.Pending.OfferListInstruction ==
            "3 objects in this crossing setup. Select a row from the list next to the pointer.", "The footer identifies the crossing setup and directs selection to its rows");
        choices.Pending.Highlight = members[2];
        choices.Pending.Choose(members[1]);
        Check(ReferenceEquals(choices.Pending.Candidate, members[1]) && choices.Pending.IsPending && Unchanged(), "Choosing an entry makes that object the candidate and changes nothing");
        choices.Cancel();
        Check(choices.Pending == null && ReferenceEquals(confirmed, choices.Confirmed) && Unchanged(), "Cancelling a group's choice list changes no scene object, patch data or completed edit history");
        choices.Begin(SelectionKind.SceneObject, "Select scene object", inspectOnly: true);
        choices.Pending.Offer(picked.Objects.Select(index => offered[index]));
        choices.Pending.CloseList();
        Check(choices.Pending != null && choices.Pending.Offers.Count == 0 && Unchanged(), "Closing a group's choice list keeps the choice pending and changes nothing");
        var mixed = WorldPick.Resolve(301, 299, new[] { marker, marker, marker, marker }, new[] { crossing });
        choices.Pending.Offer(mixed.Objects.Select(index => offered[index]), mixed.IsCrossingSetup);
        Check(choices.Pending.OfferCountText == "4 objects." && choices.Pending.OfferListInstruction == PendingSelection.SeveralOffers,
            "An overlapping marker can add an unrelated object to the crossing list, giving it a generic footer");
        var station = new PickGroup(marker, new[] { 1, 2, 3 }, kind: GameDefinedGroupKind.BakedSceneryGroup);
        var otherGroup = WorldPick.Resolve(301, 299, new[] { new PickTarget(900, 300, true, false), marker, marker, marker }, new[] { station });
        choices.Pending.Offer(otherGroup.Objects.Select(index => offered[index]), otherGroup.IsCrossingSetup);
        Check(choices.Pending.OfferCountText == "3 objects." && choices.Pending.OfferListInstruction == PendingSelection.SeveralOffers,
            "Other game-defined groups have a generic footer even when they are picked on their own");
        choices.Pending.Offer(picked.Objects.Select(index => offered[index]), picked.IsCrossingSetup);
        Check(!choices.Confirm() && choices.Pending.ListOpen, "A group's open choice list cannot be confirmed without a choice");
        choices.Pending.Choose(members[2]);
        Check(choices.Confirm() && choices.Confirmed.Identity.Equals(members[2].Identity) && Unchanged(), "Confirm makes the chosen object of the group the confirmed one, and no object of the group changed");
        Console.WriteLine("PASS: confirmed scene target scene patch editing and cancellation with shared history and saved placement");
    }

    static void UnresolvedSceneTargets(Scene scene, string json, Candidates candidates, SelectionCandidate candidate, Box original, Box replacement) {
        var saved = ScenePatchFile.Load(json);
        var session = new SelectionSession(candidates);
        var mutations = scene.MutationCalls;

        session.Begin(SelectionKind.SceneObject, "Replace scene target");
        session.Pending.Offer(Array.Empty<SelectionCandidate>());
        Check(!session.Confirm() && session.Pending.Candidate == null, "A world click without a hit leaves the scene patch file operation unresolved");
        session.Pending.Offer(new[] { candidate });
        session.Cancel();
        Check(session.Pending == null && session.Confirmed == null && saved.Write() == json && scene.MutationCalls == mutations &&
            original.X == 7 && replacement.X == 12, "Cancelling a pending complete scene patch file target choice preserves the scene patch file and world");

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
            "The complete scene patch file engine also refuses the ambiguous identity without moving either match");
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
        candidates.Items.Remove(other);
        Check(saved.Write() == json && scene.MutationCalls == mutations && original.X == 7 && replacement.X == 12,
            "Cancelling source-in-overlap choices preserves the whole scene patch file and both scene objects");
    }

    sealed class ReplacementFixture {
        public Scene Scene; public Candidates Candidates; public SelectionCandidate Candidate; public Box Original, Replacement;
        public string Json; public ScenePatchFileResult Result;
    }
    // A scene whose move modification targets the replacement board, applied, with the replacement as the one candidate.
    static ReplacementFixture AppliedReplacement() {
        var fixture = new ReplacementFixture { Scene = new Scene(), Candidates = new Candidates(), Original = new Box { X = 7 }, Replacement = new Box { X = 9 } };
        fixture.Scene.Objects["original"] = new List<object> { fixture.Original };
        fixture.Scene.Objects["replacement"] = new List<object> { fixture.Replacement };
        var target = new TargetIdentity("scene", "replacement", "Culler");
        fixture.Candidate = new SelectionCandidate(fixture.Replacement, SelectionIdentity.SceneObject(target), "Replacement board");
        fixture.Candidates.Items.Add(fixture.Candidate);
        var scenePatchFile = JObject.Parse("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"scene\",\"path\":\"original\",\"component\":\"Culler\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}");
        scenePatchFile["modifications"].First.First["target"] = new JObject { ["scene"] = target.Scene, ["path"] = target.Path, ["component"] = target.Component };
        fixture.Json = scenePatchFile.ToString();
        fixture.Result = new PatchEngine(fixture.Scene).Apply(fixture.Json);
        Check(fixture.Result.Outcomes["move"].Applied && fixture.Original.X == 7 && fixture.Replacement.X == 12, "The replacement fixture applies its move to the replacement board only");
        return fixture;
    }

    static void ReplacementChoices(Scene scene, string json, Candidates candidates, SelectionCandidate source, ScenePatchFileResult completed,
        Box original, Box replacement) {
        var scenePatchFile = ScenePatchFile.Load(json);
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
        Check(scenePatchFile.Write() == json && scene.MutationCalls == mutations && original.X == 7 && replacement.X == 12 &&
            completed.Outcomes["move"].Applied && ReferenceEquals(completed.Outcomes["move"].ResolvedTarget, replacement),
            "Replacement choices preserve the complete scene patch file, world state and previously applied edit");
        Console.WriteLine("PASS: explicit segment and node handoff, cancellation, ambiguity and revalidation");
    }
}
