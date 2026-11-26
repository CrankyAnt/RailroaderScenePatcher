// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Import of outside changes into a running session: classification against the session's start state and the text last
// read or written, conflict recognition, atomic application with restoration, Cancel, Save refusal and invalid files.
static class FileImportTests {
    sealed class History : NativeMapEditor, INativeHistoryActions { }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Doc(string items, string extra = "") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"}," + extra + "\"modifications\":{" + items + "}}";
    static string Item(string id, string path, double x, string more = "") =>
        "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + x + ",0,0],\"rotation\":[0,0,0,1]}" + more + ",\"dependsOn\":[]}";
    // Neither the native title nor the patch status shows unsaved work; dependency metadata is a separate participant.
    static bool Clean(PatchEditingSession session) { var outstanding = session.Outstanding(false); return !outstanding.Contains(PatchEditingSession.NativeData) && !outstanding.Contains(PatchEditingSession.PatchData); }
    static Placement At(double x) => new Placement(new[] { x, 0.0, 0.0 }, new[] { 0.0, 0.0, 0.0, 1.0 });
    static TargetIdentity Target(string path) => new TargetIdentity("s", path, "c");
    sealed class World {
        public readonly Scene Scene = new Scene();
        public readonly Dictionary<string, Box> Boxes = new Dictionary<string, Box>();
        public readonly History History = new History();
        public readonly Store Store = new Store();
        public PatchEditingSession Session;
        public readonly List<string> Notices = new List<string>();
        public World(string patch, params string[] paths) {
            foreach (var path in paths) { var box = new Box { Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } }; Boxes[path] = box; Scene.Objects[path] = new List<object> { box }; }
            Store.Patch = patch;
            var engine = new PatchEngine(Scene);
            if (patch != null) engine.Apply(patch);
            Session = new PatchEditingSession(engine, History, Store); History.Session = Session;
            Session.HistoryNotice = Notices.Add;
        }
        public double X(string path) => Boxes[path].X + Scene.Origin;
        public void Outside(string patch) { Store.Patch = patch; Session.CheckFileVersion(); }
    }
    public static void Run() {
        ReformattingTriggersNothing();
        ConflictFreeImportAppliesAsUnsavedWork();
        ImportEqualToFileIsSaved();
        EditedInBothNeedsAChoice();
        FileChoiceDiscardsSteps();
        SameObjectUnderAnotherKeyConflicts();
        NewKeyOnBothSidesConflicts();
        CancelKeepsTheChoicePending();
        SaveIsRefusedWhileImportIsOpen();
        FailedImportRestoresEverything();
        InvalidOrNewerFileOffersNoImport();
        FormatVersionIsCheckedFirst();
        UnknownContentSurvivesImportAndSave();
        SaveDuringSessionCreatesNoConflict();
        SavedEntryChangedOutsideIsAConflict();
        RemovedEntriesAreClassified();
        FilePanelFollowsTheSession();
        LockedControlsNameThemselves();
        UnavailableTargetIsIsolated();
        DependentsAreAppliedAgain();
        OutdatedOverviewCloses();
        FileChangedAgainRefusesApply();
        TargetMovedAwayFromEditedObjectConflicts();
        TopLevelChangesAreListed();
        ImportListNamesTheKindOnce();
        ImportListWordsEachSetting();
    }
    // The Import list leads with the kind in bold; the description does not repeat it as a verb.
    static void ImportListNamesTheKindOnce() {
        var world = new World(Doc(Item("a", "p", 110) + "," + Item("b", "r", 140)), "p", "r", "q");
        world.Outside(Doc(Item("a", "p", 117) + "," + Item("c", "q", 150)));
        Assert(world.Session.OpenImport().Applied, "Overview opens");
        var changes = world.Session.Import.Changes;
        Assert(changes.Single(c => c.Key == "a").Description == "Modification ID a for p. Setting: the placement.", "Changed: " + changes.Single(c => c.Key == "a").Description);
        Assert(changes.Single(c => c.Key == "b").Description == "Modification ID b for r. The object returns to its state without this modification.", "Removed: " + changes.Single(c => c.Key == "b").Description);
        Assert(changes.Single(c => c.Key == "c").Description == "Modification ID c for q. It sets the placement.", "New: " + changes.Single(c => c.Key == "c").Description);
        foreach (var change in changes) Assert(!change.Description.StartsWith("Changes ") && !change.Description.StartsWith("Adds ") && !change.Description.StartsWith("Removes "), "No list line repeats its kind: " + change.Description);
    }
    // A setting's own plurality decides the wording, and a new entry that sets nothing says only what it adds.
    static void ImportListWordsEachSetting() {
        var world = new World(Doc(Item("base", "r", 140) + "," + Item("a", "p", 110)), "p", "r", "q");
        world.Outside(Doc(Item("base", "r", 140) + "," + Item("a", "p", 110).Replace("\"dependsOn\":[]", "\"dependsOn\":[\"base\"]")
            + ",\"d\":{\"target\":{\"scene\":\"s\",\"path\":\"q\",\"component\":\"c\"},\"dependsOn\":[]}"));
        Assert(world.Session.OpenImport().Applied, "Overview opens");
        var changes = world.Session.Import.Changes;
        Assert(changes.Single(c => c.Key == "a").Description == "Modification ID a for p. Setting: the dependencies.", "A single plural setting: " + changes.Single(c => c.Key == "a").Description);
        Assert(changes.Single(c => c.Key == "d").Description == "Modification ID d for q.", "A new entry that sets nothing: " + changes.Single(c => c.Key == "d").Description);
    }
    static void FileChangedAgainRefusesApply() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        var session = world.Session;
        world.Outside(Doc(Item("a", "p", 117)));
        Assert(session.OpenImport().Applied, "Overview opens");
        var before = session.ScenePatchFileText;
        world.Store.Patch = Doc(Item("a", "p", 118));
        var refused = session.ApplyImport();
        Assert(!refused.Applied && refused.Error.Contains("The file changed after the version choice was shown") && session.Import == null, "A file changed again refuses Import and closes the overview: " + refused.Error);
        Assert(world.X("p") == 110 && session.ScenePatchFileText == before, "The refused Import changes nothing");
    }
    static void TargetMovedAwayFromEditedObjectConflicts() {
        var world = new World(Doc(""), "p", "r");
        var session = world.Session;
        Assert(session.Edit(Target("p"), At(125), null).Applied && session.HasSteps("place-p"), "Fixture edit adds place-p with steps");
        // A first Import keeps the editor version against the file's entry b for p, so b for p is the text last read.
        world.Outside(Doc(Item("b", "p", 150)));
        Assert(session.OpenImport().Applied, "First overview opens");
        session.ChooseImportVersion(1, false);
        Assert(session.ApplyImport().Applied && world.X("p") == 125, "First Import keeps the editor version");
        // The file then moves b away from p, the object the session edited.
        world.Outside(Doc(Item("b", "r", 150)));
        Assert(session.OpenImport().Applied, "Second overview opens");
        var moved = session.Import.Conflicts.SingleOrDefault();
        Assert(moved != null && moved.Case == ImportConflictCase.SameObject && moved.Keys.Contains("b") && moved.Keys.Contains("place-p"), "An entry whose target moves away from an object the session edited is a conflict");
    }
    static void TopLevelChangesAreListed() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        world.Outside(Doc(Item("a", "p", 110), "\"futureTop\":{\"x\":3},"));
        Assert(world.Session.OpenImport().Applied, "Overview opens");
        var change = world.Session.Import.Changes.Single();
        Assert(change.Description == "Content outside the modifications: futureTop." && change.Kind == ImportChangeKind.New, "A top-level change is listed: " + change.Description);
        Assert(world.Session.ApplyImport().Applied && world.Session.LastImport.Changes == 1, "The top-level change counts as applied");
        Assert(FilePanelState.Completed(new ImportResult(0, 0, 1), true).StartsWith("Import completed. No changes applied, 1 conflict kept at the editor version."), "A count of zero says No changes applied");
    }
    static void UnavailableTargetIsIsolated() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        world.Outside(Doc(Item("a", "p", 117) + "," + Item("gone", "missing", 140)));
        Assert(world.Session.OpenImport().Applied, "Overview opens");
        var applied = world.Session.ApplyImport();
        Assert(applied.Applied && world.X("p") == 117 && world.Session.LastImport.NotApplied.Single().StartsWith("gone"), "An unavailable target is an isolated failure: " + applied.Error);
        Assert(world.Session.ScenePatchFileText.Contains("\"gone\""), "The entry stays in the scene patch file");
    }
    static void DependentsAreAppliedAgain() {
        var world = new World(Doc(Item("base", "p", 110) + "," + Item("dependent", "r", 140).Replace("\"dependsOn\":[]", "\"dependsOn\":[\"base\"]")), "p", "r");
        Assert(world.X("r") == 140, "Fixture dependent applies");
        world.Outside(Doc(Item("dependent", "r", 140).Replace("\"dependsOn\":[]", "\"dependsOn\":[\"base\"]")));
        Assert(world.Session.OpenImport().Applied && world.Session.ApplyImport().Applied, "Import applies");
        Assert(world.Boxes["r"].X == 0, "An entry whose dependency left stops taking effect in the scene too");
    }
    static void OutdatedOverviewCloses() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        world.Outside(Doc(Item("a", "p", 117)));
        Assert(world.Session.OpenImport().Applied, "Overview opens");
        world.Outside(Doc(Item("a", "p", 118)));
        Assert(world.Session.Import == null && world.Session.ImportOutdated && world.Session.VersionState == FileVersionState.Changed, "A file changed again closes the outdated overview");
    }
    static void ReformattingTriggersNothing() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        world.Outside("{ \"modifications\": { \"a\": { \"dependsOn\": [], \"placement\": { \"rotation\": [0,0,0,1], \"position\": [110.0,0,0], \"mode\": \"fixed\" }, \"target\": { \"component\": \"c\", \"path\": \"p\", \"scene\": \"s\" } } },\n  \"sourceMap\": { \"mapId\": \"map\" }, \"formatVersion\": 1 }");
        Assert(world.Session.VersionState == FileVersionState.None, "Reformatting alone is no outside change");
        Assert(!world.Session.OpenImport().Applied, "Import needs a pending version choice");
    }
    static void ConflictFreeImportAppliesAsUnsavedWork() {
        var world = new World(Doc(Item("a", "p", 110)), "p", "q", "r");
        var session = world.Session;
        Assert(session.Edit(Target("q"), At(130), null).Applied, "Fixture edit of q");
        var undoBefore = world.History.UndoGroups.Count;
        var file = Doc(Item("a", "p", 115) + "," + Item("b", "r", 140));
        world.Outside(file);
        Assert(session.VersionState == FileVersionState.Changed, "A changed file needs a choice");
        var open = session.OpenImport();
        Assert(open.Applied && session.Import != null, "Import opens an overview: " + open.Error);
        var changes = session.Import.Changes;
        Assert(session.Import.Conflicts.Count == 0, "Entries without steps in this session are no conflict");
        Assert(changes.Count == 2 && changes.Single(c => c.Key == "a").Kind == ImportChangeKind.Changed && changes.Single(c => c.Key == "b").Kind == ImportChangeKind.New, "Changes are classified as new and changed");
        Assert(changes.Single(c => c.Key == "a").Description.Contains("placement"), "A change says what it does: " + changes.Single(c => c.Key == "a").Description);
        Assert(world.X("p") == 110 && world.Boxes["r"].X == 0, "Opening the overview changes nothing");
        var applied = session.ApplyImport();
        Assert(applied.Applied, "Import applies: " + applied.Error);
        Assert(world.X("p") == 115 && world.X("r") == 140 && world.X("q") == 130, "Imported changes apply and the session's own work keeps its value");
        Assert(world.Store.Patch == file && world.Store.PatchWrites == 0, "Import writes nothing; Save stays the only write");
        Assert(session.VersionState == FileVersionState.None && session.Import == null, "Import completes the version choice");
        Assert(session.Outstanding(false).Contains(PatchEditingSession.PatchData), "Work that differs from the file stays unsaved");
        Assert(world.History.UndoGroups.Count == undoBefore, "Import adds no native history step");
        world.History.Undo();
        Assert(world.X("q") != 130 && world.X("p") == 115 && world.X("r") == 140, "One Undo reverses the modder's own last action and leaves imported values");
        world.History.Redo();
        Assert(world.X("q") == 130, "Redo reapplies the modder's own action");
        Assert(session.SaveRefusalBeforeWrite() == null, "Save is available after Import");
        world.History.Save();
        Assert(world.Store.Patch.Contains("\"b\"") && world.Store.Patch.Contains("place-q"), "Save writes the imported and the session's own work");
        Assert(!session.Outstanding(false).Contains(PatchEditingSession.PatchData), "Save leaves no unsaved patch work");
    }
    static void ImportEqualToFileIsSaved() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        world.Outside(Doc(Item("a", "p", 118)));
        Assert(world.Session.OpenImport().Applied && world.Session.ApplyImport().Applied, "Fixture import");
        Assert(Clean(world.Session), "Patch content equal to the file on disk is Saved and leaves the native title clean");
    }
    static void EditedInBothNeedsAChoice() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        var session = world.Session;
        Assert(session.Edit(Target("p"), At(125), null).Applied && session.HasSteps("a"), "Fixture edit records a step on the entry");
        world.Outside(Doc(Item("a", "p", 119)));
        Assert(session.OpenImport().Applied, "Overview opens");
        var conflict = session.Import.Conflicts.Single();
        Assert(conflict.Case == ImportConflictCase.EditedInBoth && conflict.Keys.SequenceEqual(new[] { "a" }) && session.Import.Changes.Count == 0, "An entry with steps that changed in the file is a conflict");
        var refused = session.ApplyImport();
        Assert(!refused.Applied && refused.Error == session.Import.ApplyRefusal && world.X("p") == 125, "Each conflict needs a choice before Import applies");
        session.ChooseImportVersion(conflict.Number, false);
        Assert(session.ApplyImport().Applied, "Import applies with the editor version chosen");
        Assert(world.X("p") == 125 && session.HasSteps("a"), "The editor version keeps its value and its steps");
        Assert(session.Outstanding(false).Contains(PatchEditingSession.PatchData), "The kept editor version differs from the file and is unsaved");
        world.History.Undo();
        Assert(world.X("p") == 110, "A kept entry's step still undoes");
    }
    static void FileChoiceDiscardsSteps() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        var session = world.Session;
        Assert(session.Edit(Target("p"), At(125), null).Applied, "Fixture edit");
        world.Outside(Doc(Item("a", "p", 119)));
        Assert(session.OpenImport().Applied, "Overview opens");
        session.ChooseImportVersion(session.Import.Conflicts.Single().Number, true);
        Assert(session.ApplyImport().Applied, "Import applies with the file version chosen");
        Assert(world.X("p") == 119 && !session.HasSteps("a"), "The file version replaces the entry and discards its work");
        Assert(Clean(session), "After a file choice equal to the file, patch and native work are saved");
        var undo = world.History.UndoGroups.Count;
        world.History.Undo();
        Assert(world.X("p") == 119 && world.History.CanRedo && world.Notices.Count == 1, "Undo of a discarded step changes nothing, stays redoable and shows a message");
        Assert(Clean(session), "Undo of a discarded step leaves the native title clean");
        world.History.Redo();
        Assert(world.X("p") == 119 && world.History.UndoGroups.Count == undo && world.Notices.Count == 2, "Redo of a discarded step changes nothing, stays undoable and shows a message");
    }
    static void SameObjectUnderAnotherKeyConflicts() {
        var world = new World(Doc(""), "p");
        var session = world.Session;
        Assert(session.Edit(Target("p"), At(125), null).Applied && session.HasSteps("place-p"), "Fixture edit adds an entry with steps");
        world.Outside(Doc(Item("other", "p", 150)));
        Assert(session.OpenImport().Applied, "Overview opens");
        var conflict = session.Import.Conflicts.Single();
        Assert(conflict.Case == ImportConflictCase.SameObject && conflict.Keys.Contains("other") && conflict.Keys.Contains("place-p"), "One object changed under different keys on both sides is a conflict");
        session.ChooseImportVersion(conflict.Number, true);
        Assert(session.ApplyImport().Applied, "Import applies");
        Assert(world.X("p") == 150 && !session.ScenePatchFileText.Contains("place-p") && Clean(session), "The file version replaces the editor's entry for that object");
        world.History.Undo();
        Assert(world.X("p") == 150, "Undo of the discarded step never overwrites the imported value");
    }
    static void NewKeyOnBothSidesConflicts() {
        var world = new World(Doc(""), "p");
        var session = world.Session;
        Assert(session.Edit(Target("p"), At(125), null).Applied, "Fixture edit");
        world.Outside(Doc(Item("place-p", "p", 160)));
        Assert(session.OpenImport().Applied && session.Import.Conflicts.Count == 1 && session.Import.Conflicts[0].Case == ImportConflictCase.NewOnBothSides && session.Import.Changes.Count == 0, "A new key with different content on both sides is a conflict");
        session.ChooseImportVersion(1, false);
        Assert(session.ApplyImport().Applied && world.X("p") == 125, "The editor version keeps its content");
    }
    static void CancelKeepsTheChoicePending() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        var session = world.Session;
        Assert(session.Edit(Target("p"), At(125), null).Applied, "Fixture edit");
        var file = Doc(Item("a", "p", 119));
        world.Outside(file);
        Assert(session.OpenImport().Applied, "Overview opens");
        session.ChooseImportVersion(1, true);
        session.CancelImport();
        Assert(session.Import == null && session.VersionState == FileVersionState.Changed && world.X("p") == 125 && world.Store.Patch == file, "Cancel discards the choices, changes nothing and leaves the version choice pending");
        Assert(session.SaveRefusalBeforeWrite() == session.VersionMessage, "After Cancel, Save is refused for the pending choice");
        Assert(session.OpenImport().Applied && session.Import.Conflicts.Single().UseFile == null, "A new overview starts without the cancelled choices");
    }
    static void SaveIsRefusedWhileImportIsOpen() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        world.Outside(Doc(Item("a", "p", 119)));
        Assert(world.Session.OpenImport().Applied, "Overview opens");
        var patch = world.Store.Patch; var nativeWrites = world.History.NativeWrites;
        Assert(world.Session.SaveRefusalBeforeWrite() == "An import is open in Scene Patcher. Click Import or Cancel first.",
            "Save is refused with the visible actions while the Import panel is open");
        var result = world.History.Save();
        Assert(result != null && world.Store.Patch == patch && world.Store.PatchWrites == 0 && world.History.NativeWrites == nativeWrites, "The refused Save writes nothing");
    }
    static void FailedImportRestoresEverything() {
        var world = new World(Doc(Item("a", "p", 110)), "p", "r");
        var session = world.Session;
        var before = session.ScenePatchFileText;
        world.Outside(Doc(Item("a", "p", 117) + "," + Item("b", "r", 140)));
        Assert(session.OpenImport().Applied, "Overview opens");
        world.Scene.FailingTarget = world.Boxes["r"];
        var failed = session.ApplyImport();
        world.Scene.FailingTarget = null;
        Assert(!failed.Applied && failed.Error.StartsWith("Scene patch editing is back as it was before Import."), "A failed change says why: " + failed.Error);
        Assert(world.X("p") == 110 && world.Boxes["r"].X == 0 && session.ScenePatchFileText == before, "A failed Import restores every object and the scene patch file");
        Assert(session.Import != null && session.VersionState == FileVersionState.Changed, "The overview stays open after a failure");
    }
    static void InvalidOrNewerFileOffersNoImport() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        world.Outside("{broken");
        Assert(world.Session.VersionState == FileVersionState.Repair && !world.Session.OpenImport().Applied && world.Session.Import == null, "An invalid file needs repair and offers no Import");
        world.Outside(Doc(Item("a", "p", 119)).Replace("\"formatVersion\":1", "\"formatVersion\":2"));
        Assert(world.Session.VersionState == FileVersionState.Repair && !world.Session.OpenImport().Applied, "A newer format version needs repair and offers no Import");
        world.Outside(Doc(Item("a", "p", 119)));
        Assert(world.Session.OpenImport().Applied, "A readable file offers Import again");
        world.Outside("{broken");
        Assert(world.Session.Import == null, "A file that needs repair closes the open overview");
    }
    // Import checks the declared format version before any other content: a changed file whose version is missing,
    // unrecognized or newer, with other invalid content as well, needs repair for its version and offers no Import.
    static void FormatVersionIsCheckedFirst() {
        var cases = new[] {
            Tuple.Create("{\"sourceMap\":{\"mapId\":5},\"modifications\":[1]}", "Missing format version"),
            Tuple.Create("{\"formatVersion\":\"one\",\"sourceMap\":{\"mapId\":5},\"modifications\":[1]}", "Unrecognized format version"),
            Tuple.Create("{\"formatVersion\":2,\"sourceMap\":{\"mapId\":5},\"modifications\":[1", "Newer format version 2") };
        foreach (var item in cases) {
            var world = new World(Doc(Item("a", "p", 110)), "p");
            world.Outside(item.Item1);
            var session = world.Session;
            Assert(session.VersionState == FileVersionState.Repair && session.VersionMessage.StartsWith("File needs repair: " + item.Item2), "The version decides the repair: " + session.VersionMessage);
            Assert(session.ScenePatchFileDiagnostics.Count == 1 && session.ScenePatchFileDiagnostics[0].Path == "formatVersion" && session.ScenePatchFileDiagnostics[0].Message.StartsWith(item.Item2), "The diagnosis is the version's, not the other content's: " + String.Join(" | ", session.ScenePatchFileDiagnostics.Select(d => d.Path + ": " + d.Message)));
            Assert(!session.OpenImport().Applied && session.Import == null, "A file that needs repair for its version offers no Import");
        }
    }
    static void UnknownContentSurvivesImportAndSave() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        world.Outside(Doc(Item("a", "p", 110) + "," + Item("future", "p", 120, ",\"futureOperation\":true"), "\"futureTop\":{\"x\":3},"));
        Assert(world.Session.OpenImport().Applied, "Overview opens");
        Assert(world.Session.Import.Changes.Any(c => c.Key == "future" && c.Description.Contains("does not apply")), "Unknown content is named as not applied");
        var applied = world.Session.ApplyImport();
        Assert(applied.Applied && world.X("p") == 110, "Unknown content is kept without execution: " + applied.Error);
        world.Session.Edit(Target("p"), At(111), null);
        world.History.Save();
        Assert(world.Store.Patch.Contains("futureTop") && world.Store.Patch.Contains("futureOperation"), "Unknown content survives Import and a later Save");
    }
    static void SaveDuringSessionCreatesNoConflict() {
        var world = new World(Doc(Item("a", "p", 110)), "p", "r");
        var session = world.Session;
        Assert(session.Edit(Target("p"), At(125), null).Applied, "Fixture edit");
        world.History.Save();
        world.Outside(Doc(Item("a", "p", 125) + "," + Item("b", "r", 140)));
        Assert(session.VersionState == FileVersionState.Changed && session.OpenImport().Applied, "Overview opens: " + session.VersionMessage);
        Assert(session.Import.Conflicts.Count == 0 && session.Import.Changes.Single().Key == "b", "A Save during the session creates no conflict by itself");
    }
    // An outside change to an entry whose session edits were all saved is a conflict: the entry was edited in this session
    // and also changed in the file. The description says that, without suggesting unsaved edits.
    static void SavedEntryChangedOutsideIsAConflict() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        var session = world.Session;
        Assert(session.Edit(Target("p"), At(125), null).Applied, "Fixture edit");
        world.History.Save();
        Assert(!session.Outstanding(false).Contains(PatchEditingSession.PatchData), "Every session edit is saved");
        world.Outside(Doc(Item("a", "p", 126)));
        Assert(session.OpenImport().Applied, "Overview opens");
        var conflict = session.Import.Conflicts.Single();
        Assert(conflict.Case == ImportConflictCase.EditedInBoth && conflict.Description == "Modification ID a for p was edited in this session and also changed in the file.", "The saved entry's outside change is a conflict with a true description: " + conflict.Description);
        Assert(!conflict.Description.Contains("your edits"), "The description does not suggest unsaved edits");
    }
    static void RemovedEntriesAreClassified() {
        var world = new World(Doc(Item("a", "p", 110) + "," + Item("b", "r", 140)), "p", "r");
        world.Outside(Doc(Item("a", "p", 110)));
        Assert(world.Session.OpenImport().Applied, "Overview opens");
        var removed = world.Session.Import.Changes.Single();
        Assert(removed.Key == "b" && removed.Kind == ImportChangeKind.Removed, "A removal is a change");
        Assert(world.Session.ApplyImport().Applied && world.Boxes["r"].X == 0 && Clean(world.Session), "Import removes the entry and the object returns to its state without it");
    }
    // The file panel replaces the tabs while a choice is pending, the file needs repair or Import is open; the message area
    // holds no choice; every control outside the panel gives the panel's reason.
    static void FilePanelFollowsTheSession() {
        var world = new World(Doc(Item("a", "p", 110)), "p");
        var session = world.Session;
        Assert(FilePanelState.Of(session) == FilePanelState.Kind.None && FilePanelState.Reason(FilePanelState.Kind.None) == null, "No panel without a pending choice");
        world.Outside(Doc(Item("a", "p", 119)));
        Assert(FilePanelState.Of(session) == FilePanelState.Kind.VersionChoice && FilePanelState.Heading(FilePanelState.Kind.VersionChoice) == "Version choice", "A pending choice opens the Version choice panel");
        Assert(EditFeedback.Primary(session, false, false) == EditFeedback.PrimaryMessage.None, "The message area holds no version choice");
        Assert(FilePanelState.KeepEditorEffect == "The next Save replaces the file with the editor version.", "Keep editor version explains what the next Save writes");
        Assert(CommandBand.Tooltip("Inspect.", FilePanelState.Reason(FilePanelState.Of(session))) == "Choose a version in the Version choice panel first.", "A control outside the panel shows its reason");
        Assert(session.OpenImport().Applied && FilePanelState.Of(session) == FilePanelState.Kind.Import && FilePanelState.Heading(FilePanelState.Kind.Import) == "Import", "Import replaces the choice in the same panel");
        session.CancelImport();
        world.Outside("{broken");
        Assert(FilePanelState.Of(session) == FilePanelState.Kind.Repair && FilePanelState.Heading(FilePanelState.Kind.Repair) == "Repair", "A file that needs repair opens the Repair panel");
        Assert(FilePanelState.Completed(new ImportResult(2, 1, 0), true) == "Import completed. 3 changes applied. Click Save in the Map Editor to write the scene patch file.", "A conflict resolved with the file version counts as an applied change");
        Assert(FilePanelState.Completed(new ImportResult(0, 1, 0), false) == "Import completed. 1 change applied. Your scene patch edits match the file on disk.", "A single file choice is one applied change");
        Assert(FilePanelState.Completed(new ImportResult(1, 1, 2), true).StartsWith("Import completed. 2 changes applied, 2 conflicts kept at the editor version."), "Conflicts kept at the editor version are counted apart");
    }
    // While the file panel is open, an outliner row keeps its object's name as its tooltip title and gives only the
    // panel's reason; the search field has a fixed title; the state line puts the badges and the problem count before
    // the family, so the changed badge is never the part a narrow line cuts off.
    static void LockedControlsNameThemselves() {
        Assert(FilePanelState.RowTitle("Loc: Sylva Int\nWorld/Signs/Loc: Sylva Int\nSigns\nChanged by this mod", "* Loc: Sylva Int") == "Loc: Sylva Int", "A locked row's tooltip title is its object's name");
        Assert(FilePanelState.RowTitle(null, "12 Signs") == "12 Signs" && FilePanelState.RowTitle("", "12 Signs") == "12 Signs", "A folder row and its disclosure take the row's label as title");
        Assert(CommandBand.Tooltip(null, FilePanelState.Reason(FilePanelState.Kind.Repair)) == "The scene patch file needs repair. Use the Repair panel first.", "A locked row gives the Repair reason");
        Assert(CommandBand.Tooltip(null, FilePanelState.Reason(FilePanelState.Kind.VersionChoice)) == "Choose a version in the Version choice panel first.", "A locked row gives the Version choice reason");
        Assert(CommandBand.SearchTitle == "Search objects" && !String.IsNullOrEmpty(CommandBand.SearchTip), "The search field has a fixed title and a description");
        var parts = CommandBand.StateParts("Signs", "Inactive", new[] { "* CHANGED" }, "Inspection only", "1 problem");
        Assert(parts.SequenceEqual(new[] { "* CHANGED", "1 problem", "Signs", "Inactive", "Inspection only" }), "Badges and problems lead the state line: " + String.Join(" | ", parts));
        Assert(CommandBand.StateParts<string>("Signs", null, new string[0], null, null).SequenceEqual(new[] { "Signs" }), "Absent parts are left out");
    }
}
