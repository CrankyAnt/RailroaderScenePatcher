// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Controlled storage for the active edited MapMod, with per-participant write failures.
class Store : IStorage {
    public string Patch, Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"rr/game >=0.6.2\"]}}";
    public bool FailPatch, FailInfo;
    public int PatchWrites, InfoWrites;
    public string ReadPatchDocument() => Patch;
    public void WritePatchDocument(string text) { if (FailPatch) throw new IOException("patch write injected"); Patch = text; PatchWrites++; }
    public string ReadPackInfo() => Info;
    public void WritePackInfo(string text) { if (FailInfo) throw new IOException("metadata write injected"); Info = text; InfoWrites++; }
}

// Mirrors the grouping, stack, pruning and change-count rules of the game's undo manager,
// and the editor's count-based dirty flag, so history accounting meets the real collisions.
class Editor : INativeHistory {
    sealed class Group { public string Name; public List<Action> Actions = new List<Action>(); }
    readonly List<Group> undo = new List<Group>(), redo = new List<Group>();
    Group current; bool undoing, redoing;
    public int ChangeCount, CleanCount, MaxLevels = 50, NativeValue, NativeWrites;
    public bool FailNative, ThrowInNativeAction;
    public AuthoringSession Session;
    public IReadOnlyList<object> UndoGroups => undo;
    public IReadOnlyList<object> RedoGroups => redo;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Register(Action inverse, string name) {
        var own = current == null;
        if (own) current = new Group { Name = name };
        current.Actions.Add(inverse);
        if (own) End();
    }
    void End() {
        if (current.Actions.Count > 0) {
            if (undoing) redo.Add(current);
            else if (redoing) undo.Add(current);
            else { undo.Add(current); redo.Clear(); ChangeCount++; if (undo.Count > MaxLevels) undo.RemoveAt(0); }
            Session?.HistoryChanged();
        }
        current = null;
    }
    void Execute(List<Group> source, ref bool flag, int delta) {
        if (source.Count == 0) return;
        Session.BeginHistoryExecution();
        Exception error = null;
        flag = true;
        try {
            var group = source[source.Count - 1]; source.RemoveAt(source.Count - 1);
            current = new Group { Name = group.Name };
            for (var i = group.Actions.Count - 1; i >= 0; i--) group.Actions[i]();
            ChangeCount += delta;
            End();
        } catch (Exception e) { error = e; }
        finally { flag = false; }
        Session.EndHistoryExecution(error);
    }
    public void Undo() { Execute(undo, ref undoing, -1); }
    public void Redo() { Execute(redo, ref redoing, 1); }
    public void NativeEdit(int value) {
        var old = NativeValue; NativeValue = value;
        Register(() => { if (ThrowInNativeAction) throw new Exception("native action injected"); NativeEdit(old); }, "Native edit");
    }
    public bool NativeDirty => ChangeCount != CleanCount;
    public bool IsDirty => Session.IsDirty(NativeDirty);
    // The native Save route: patcher participants first, then the native map write and clean mark.
    public string Save() {
        try {
            Session.BeginSave();
            if (FailNative) throw new IOException("native write injected");
            NativeWrites++; CleanCount = ChangeCount;
            Session.CompleteNativeSave();
            return null;
        } catch (Exception e) { return Session.FailSave(e); }
    }
}

static class AuthoringTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static readonly TargetIdentity P = new TargetIdentity("s", "p", "c");
    static Placement Pose(double x, double y = 2, double z = 3) => new Placement(new[] { x, y, z }, new[] { 0.0, 0.0, 0.0, 1.0 });
    static readonly TargetIdentity Q = new TargetIdentity("s", "q", "c");
    const string Tail = "; outstanding: ";
    static string Mod(string id, string path, string body) => "{\"id\":\"" + id + "\",\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"}," + body + ",\"dependsOn\":[]}";
    static string Fixed(double x) => "\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",2,3],\"rotation\":[0,0,0,1]}";
    static string Doc(string items, string extra = "") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"}," + extra + "\"modifications\":[" + items + "]}";
    sealed class World {
        public Scene Scene = new Scene(); public Box Box = new Box { Position = new[] { -93.0, 8.0, 9.0 }, Rotation = new[] { 0.0, 1.0, 0.0, 0.0 } };
        public Box Other = new Box { Position = new[] { -50.0, 0.0, 0.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        public Store Store = new Store(); public Editor Editor = new Editor(); public AuthoringSession Session;
        public bool AtUnderlying => Box.X == -93 && Box.Position[1] == 8 && Box.Position[2] == 9 && Box.Rotation.SequenceEqual(new[] { 0.0, 1.0, 0.0, 0.0 }) && Box.Active;
        public World(string patch = null, string info = null) {
            Scene.Objects["p"] = new List<object> { Box }; Scene.Objects["q"] = new List<object> { Other }; Store.Patch = patch;
            if (info != null) Store.Info = info;
            else if (patch != null) Store.Info = Store.Info.Replace("\"rr/game >=0.6.2\"", "\"rr/game >=0.6.2\",\"crankyant/scene-patcher >=0.1.0\"");
            var engine = new PatchEngine(Scene);
            if (patch != null) engine.Apply(patch, "author/pack");
            Session = new AuthoringSession(engine, Editor, Store, "map");
            Editor.Session = Session;
        }
        public JObject Saved => JObject.Parse(Store.Patch);
        public JObject Document => JObject.Parse(Session.DocumentText);
    }
    internal static void Run() {
        NumericEditReachesWorldAndDocument();
        ActivityFollowsTheSameRoute();
        UndoAndRedoAreOneCoherentChange();
        MixedHistoryKeepsSavedIdentity();
        RemovalRecomputesUnderlyingState();
        SaveWritesEveryParticipant();
        PartialWritesStayIdentifiedAndRetryable();
        DependencyMinimumRisesOnlyWhenRequired();
        PruningAndHistoryErrorsStayTruthful();
        FailedEditsRestoreAndGuardRetry();
        UnknownContentAndProtectedDocumentsSurvive();
        ExitIdentifiesOutstandingWork();
        InspectedEditsBindToTheirObservation();
    }
    static string[] Required(World w) => JObject.Parse(w.Store.Info)["dependencies"]["required"].Select(x => (string)x).ToArray();
    static void ActivityFollowsTheSameRoute() {
        var w = new World();
        Assert(w.Session.Edit(P, null, false).Applied && !w.Box.Active && w.Box.X == -93, "An explicit disable takes effect without moving the object");
        var item = (JObject)w.Document["modifications"][0];
        Assert(item["active"].Type == JTokenType.Boolean && !(bool)item["active"] && item["placement"] == null, "The document represents the activity change alone");
        var replay = new Scene(); var fresh = new Box(); replay.Objects["p"] = new List<object> { fresh };
        var engine = new PatchEngine(replay);
        Assert(engine.Apply(w.Session.DocumentText).Outcomes.Values.Single().Applied && !fresh.Active, "The runtime engine applies the explicit disable");
        fresh.Active = true;
        Assert(engine.Apply(w.Session.DocumentText).Outcomes.Values.Single().Applied && !fresh.Active, "Repeated application keeps the explicit disable after a later activation");
        w.Editor.Undo(); Assert(w.Box.Active && w.Document["modifications"].Count() == 0, "Native Undo reverses the activity change");
        w.Editor.Redo(); Assert(!w.Box.Active, "Native Redo reapplies the activity change");
        Assert(w.Session.Edit(P, Pose(112), null).Applied && !w.Box.Active && w.Box.X == 12 && w.Document["modifications"].Count() == 1, "Placement and activity group in one desired state per object");
        Assert(w.Editor.Save() == null && !w.Editor.IsDirty && !(bool)w.Saved["modifications"][0]["active"], "The activity change persists through the same Save");
        Assert(!new PatchEngine(new Scene()).Apply(Doc(Mod("a", "p", "\"active\":\"no\""))).Outcomes["a"].Applied, "A non-boolean activity value is rejected");
    }
    static void UndoAndRedoAreOneCoherentChange() {
        var w = new World();
        w.Session.Edit(P, Pose(112), false);
        Assert(w.Editor.UndoGroups.Count == 1 && w.Editor.IsDirty, "One completed edit is one native history group");
        w.Editor.Undo();
        Assert(w.AtUnderlying && w.Document["modifications"].Count() == 0 && !w.Editor.IsDirty, "Undo reverses world, document and dirty state together");
        w.Editor.Redo();
        Assert(w.Box.X == 12 && !w.Box.Active && w.Document["modifications"].Count() == 1 && w.Editor.IsDirty, "Redo reapplies the complete edit");
        w.Session.Edit(P, Pose(140), null);
        w.Editor.Undo();
        Assert(w.Box.X == 12 && (double)w.Document["modifications"][0]["placement"]["position"][0] == 112, "Undo of a second edit returns to the first desired state, not to the underlying state");
        w.Scene.Origin = 110; w.Editor.Redo(); Assert(w.Box.X == 30, "Redo applies the map placement through a changed origin"); w.Scene.Origin = 100;
    }
    static void MixedHistoryKeepsSavedIdentity() {
        var w = new World();
        w.Editor.NativeEdit(1); w.Session.Edit(P, Pose(112), null); w.Editor.NativeEdit(2);
        Assert(w.Editor.Save() == null && !w.Editor.IsDirty, "Save of mixed native and patcher edits leaves a clean editor");
        w.Editor.Undo(); w.Editor.Undo();
        Assert(w.AtUnderlying && w.Editor.NativeValue == 1 && w.Editor.IsDirty, "Undo after Save crosses native and patcher edits and reports outstanding work");
        w.Editor.Redo(); w.Editor.Redo();
        Assert(w.Box.X == 12 && w.Editor.NativeValue == 2 && !w.Editor.IsDirty, "Redo back to the saved state is clean again");
        w.Editor.Undo(); w.Editor.NativeEdit(7);
        Assert(!w.Editor.NativeDirty && w.Editor.IsDirty && !w.Editor.CanRedo, "A branched history with the saved change count stays dirty, so Save stays available");
        Assert(w.Session.Outstanding(w.Editor.NativeDirty).SequenceEqual(new[] { AuthoringSession.NativeData }), "The divergent native state is the outstanding participant");
        w.Editor.Undo(); w.Session.Edit(P, Pose(150), null);
        Assert(!w.Editor.NativeDirty && w.Editor.IsDirty && w.Session.Outstanding(false).Contains(AuthoringSession.PatchData), "A different patcher edit at the saved change count is outstanding patch work");
        Assert(w.Editor.Save() == null && !w.Editor.IsDirty && (double)w.Saved["modifications"][0]["placement"]["position"][0] == 150, "Saving the branch records its own saved identity");
    }
    static void RemovalRecomputesUnderlyingState() {
        var w = new World(Doc(Mod("move-p", "p", Fixed(112)) + "," + Mod("move-q", "q", Fixed(160)), "\"future\":{\"keep\":true},"));
        Assert(w.Box.X == 12 && w.Other.X == 60 && !w.Editor.IsDirty, "The saved document is applied and clean at load");
        w.Session.Edit(Q, Pose(170), null); w.Session.Edit(P, Pose(130), null);
        Assert(w.Session.Remove(P).Applied && w.AtUnderlying, "Removal returns the object to the state the native context gives it, not to an earlier patched snapshot");
        Assert(w.Other.X == 70 && w.Document["modifications"].Count() == 1 && (string)w.Document["modifications"][0]["id"] == "move-q" && (bool)w.Document["future"]["keep"], "Removal keeps the remaining authored work and unknown content");
        w.Editor.Undo(); Assert(w.Box.X == 30 && (string)w.Document["modifications"][0]["id"] == "move-p", "Undo of a removal restores the modification at its place in the document");
        w.Editor.Redo(); Assert(w.AtUnderlying && w.Editor.Save() == null && w.Saved["modifications"].Count() == 1, "A saved removal leaves only the remaining modification on disk");
        Assert(!w.Session.Remove(P).Applied, "An object without a modification has nothing to remove");
        var two = new World(Doc(Mod("pose", "p", Fixed(112)) + "," + Mod("off", "p", "\"active\":false")));
        Assert(two.Box.X == 12 && !two.Box.Active && !two.Session.Remove(P).Applied && two.Box.X == 12, "Several manual modifications of one object are left for manual combination");
    }
    static void SaveWritesEveryParticipant() {
        var w = new World();
        w.Editor.NativeEdit(1); w.Session.Edit(P, Pose(112), null);
        Assert(w.Session.Outstanding(w.Editor.NativeDirty).SequenceEqual(new[] { AuthoringSession.NativeData, AuthoringSession.PatchData, AuthoringSession.Metadata }), "An authored modification needs native data, patch document and dependency metadata");
        Assert(w.Editor.Save() == null && w.Editor.NativeWrites == 1 && w.Store.PatchWrites == 1 && w.Store.InfoWrites == 1, "One Save writes all three participants");
        Assert(Required(w).SequenceEqual(new[] { "rr/game >=0.6.2", "crankyant/scene-patcher >=0.1.0" }) && (string)JObject.Parse(w.Store.Info)["version"] == "1.0.0", "The dependency is added without disturbing other metadata");
        Assert(w.Session.LastSave.Outstanding.Count == 0 && w.Session.LastSave.Completed.Count == 3 && !w.Editor.IsDirty && w.Box.X == 12, "A complete Save reports clean and leaves the world as edited");
        w.Editor.NativeEdit(2);
        Assert(w.Editor.Save() == null && w.Store.PatchWrites == 1 && w.Store.InfoWrites == 1 && w.Editor.NativeWrites == 2, "Unchanged patcher participants are not rewritten");
        var empty = new World(); empty.Editor.NativeEdit(1);
        Assert(empty.Editor.Save() == null && empty.Store.Patch == null && empty.Store.InfoWrites == 0, "A MapMod without authored modifications gets no patch document or dependency");
    }
    static void PartialWritesStayIdentifiedAndRetryable() {
        var w = new World();
        w.Session.Edit(P, Pose(112), null);
        w.Store.FailInfo = true; var message = w.Editor.Save();
        Assert(message.Contains("metadata write injected") && message.Contains("completed writes: none") && message.EndsWith(Tail + "native map data, patch document, dependency metadata. Edits remain in memory. Correct the cause and Save again."), "A metadata failure names every outstanding participant: " + message);
        Assert(w.Editor.IsDirty && w.Box.X == 12 && w.Document["modifications"].Count() == 1 && w.Store.Patch == null && w.Editor.NativeWrites == 0, "The failed Save keeps the edit in memory and writes nothing further");
        w.Store.FailInfo = false; w.Store.FailPatch = true; message = w.Editor.Save();
        Assert(message.Contains("patch write injected") && message.Contains("completed writes: dependency metadata" + Tail + "native map data, patch document."), "A patch failure identifies the completed metadata write: " + message);
        Assert(w.Editor.IsDirty && w.Session.LastSave.Completed.SequenceEqual(new[] { AuthoringSession.Metadata }) && w.Store.InfoWrites == 1, "Completed writes are not reversed and the editor stays dirty");
        w.Store.FailPatch = false; w.Editor.FailNative = true; message = w.Editor.Save();
        Assert(message.Contains("native write injected") && message.Contains("completed writes: patch document" + Tail + "native map data."), "A native failure after the patch write stays identified: " + message);
        Assert(w.Editor.IsDirty && w.Store.PatchWrites == 1 && w.Box.X == 12, "A partial Save never reports clean");
        w.Editor.FailNative = false;
        Assert(w.Editor.Save() == null && !w.Editor.IsDirty && w.Store.PatchWrites == 1 && w.Store.InfoWrites == 1 && w.Editor.NativeWrites == 1, "The retry writes only the outstanding participant");
        w.Session.Edit(P, Pose(120), null); var external = w.Store.Patch.Replace("112", "999"); w.Store.Patch = external;
        message = w.Editor.Save();
        Assert(message.Contains("changed outside the editor") && w.Store.Patch == external && w.Editor.IsDirty, "The retry is guarded: an externally changed document is not overwritten");
        var first = new World(Doc(Mod("move-p", "p", Fixed(112))));
        first.Editor.NativeEdit(1); first.Editor.FailNative = true; first.Editor.Save(); first.Editor.FailNative = false;
        first.Editor.Undo(); first.Editor.NativeEdit(2);
        Assert(first.Editor.IsDirty, "A failed Save records no saved identity for a later equal change count");
    }
    static void DependencyMinimumRisesOnlyWhenRequired() {
        Func<string, string> info = entry => "{\"version\":\"1.0.0\",\"build\":0.10,\"dependencies\":{\"required\":[" + entry + "]}}";
        var w = new World(null, info("\"crankyant/scene-patcher >=0.1.0\""));
        w.Session.Edit(P, Pose(112), null);
        Assert(w.Editor.Save() == null && w.Store.InfoWrites == 0, "A satisfied minimum is not rewritten");
        w.Session.Edit(P, null, false);
        Assert(w.Session.MinimumPatcherVersion == "0.2.0" && w.Session.Outstanding(false).Contains(AuthoringSession.Metadata), "An activity change requires the patcher version that introduced it");
        Assert(w.Editor.Save() == null && Required(w).SequenceEqual(new[] { "crankyant/scene-patcher >=0.2.0" }) && w.Store.Info.Contains("0.10"), "Save raises the minimum and preserves other metadata exactly");
        w.Editor.Undo();
        Assert(w.Editor.Save() == null && Required(w).Single() == "crankyant/scene-patcher >=0.2.0" && w.Store.InfoWrites == 1, "A raised minimum is not lowered again");
        w = new World(null, info("\"crankyant/scene-patcher >= 0.5.0 <2.0.0\""));
        w.Session.Edit(P, Pose(112), false);
        Assert(w.Editor.Save() == null && w.Store.InfoWrites == 0, "A stricter existing constraint is preserved");
        w = new World(null, info("\"crankyant/scene-patcher >=0.1.0 <1.0.0\""));
        w.Session.Edit(P, null, false);
        Assert(w.Editor.Save() == null && Required(w).Single() == "crankyant/scene-patcher >=0.2.0 <1.0.0", "An upper bound survives the raised minimum");
        w = new World(null, info("\"crankyant/scene-patcher ^0.1.0\""));
        w.Session.Edit(P, null, false); var message = w.Editor.Save();
        Assert(message.Contains("excludes the required minimum 0.2.0") && w.Store.InfoWrites == 0 && w.Store.Patch == null && w.Editor.IsDirty, "A constraint that excludes the required version blocks the write instead of being removed: " + message);
        w = new World(null, info("\"crankyant/scene-patcher\""));
        w.Session.Edit(P, Pose(112), null);
        Assert(w.Editor.Save() == null && Required(w).Single() == "crankyant/scene-patcher >=0.1.0", "An unconstrained dependency receives the minimum without an upper bound");
    }
    static void PruningAndHistoryErrorsStayTruthful() {
        var w = new World(); w.Editor.MaxLevels = 2;
        w.Editor.NativeEdit(1); w.Session.Edit(P, Pose(112), null);
        Assert(w.Editor.Save() == null && !w.Editor.IsDirty, "Saved at the top of a full history");
        w.Editor.NativeEdit(2); w.Editor.NativeEdit(3);
        Assert(w.Editor.UndoGroups.Count == 2, "The oldest groups were pruned");
        w.Editor.Undo(); w.Editor.Undo();
        Assert(!w.Editor.CanUndo && !w.Session.IsDirty(false) && w.Box.X == 12, "Returning to the saved state below a pruned history is recognized as that state");
        var lost = new World(); lost.Editor.MaxLevels = 2;
        lost.Editor.NativeEdit(1); lost.Editor.NativeEdit(2); lost.Editor.NativeEdit(3); lost.Editor.Undo(); lost.Editor.Undo();
        Assert(!lost.Editor.CanUndo && lost.Session.IsDirty(false), "An emptied history whose pruned edit cannot be undone is not the saved initial state");
        var error = new World();
        error.Editor.NativeEdit(1); error.Editor.Save(); error.Editor.NativeEdit(2);
        error.Editor.ThrowInNativeAction = true; error.Editor.Undo(); error.Editor.ThrowInNativeAction = false;
        Assert(error.Session.IncompleteOperation.Contains("native action injected") && error.Session.IsDirty(false), "An exception inside native Undo is identified and never reads as clean");
        var writes = error.Editor.NativeWrites;
        Assert(error.Editor.Save() == null && error.Editor.NativeWrites == writes + 1, "Save still writes the current state after a native history error");
        Assert(!error.Editor.NativeDirty && error.Editor.IsDirty && error.Session.Outstanding(false).SequenceEqual(new[] { AuthoringSession.NativeData }), "Save does not report clean while the native history is unrecovered");
        Assert(error.Session.IncompleteOperation.Contains("native action injected") && error.Session.LastSave.Outstanding.SequenceEqual(new[] { AuthoringSession.NativeData }), "The incomplete operation stays identified through Save, in the diagnosis and the Save report");
        var count = error.Editor.ChangeCount; error.Editor.NativeEdit(5);
        Assert(error.Editor.NativeValue == 5 && error.Editor.ChangeCount == count && !error.Editor.NativeDirty && error.Editor.UndoGroups.Count == 1, "The open partial group swallows a later native edit: neither the change count nor the undo stack records it");
        Assert(error.Editor.IsDirty && error.Session.ExitWarning(error.Editor.NativeDirty).Contains("native map data") && error.Session.ExitWarning(false).Contains("native action injected"), "The untracked edit after Save is still reported as unsaved, and exit names the unrecovered history");
        Assert(error.Editor.Save() == null && error.Editor.IsDirty, "A repeated Save cannot establish a clean state either");
        var own = new World();
        own.Session.Edit(P, Pose(112), null); own.Editor.Save();
        own.Session.Edit(P, Pose(130), null);
        own.Scene.FailApply = true; own.Editor.Undo(); own.Scene.FailApply = false;
        Assert(own.Session.IncompleteOperation.Contains("could not be reversed") && (double)own.Document["modifications"][0]["placement"]["position"][0] == 130 && own.Editor.IsDirty, "A patcher edit that cannot be reversed keeps its document state and reports the incomplete operation");
        Assert(own.Editor.CanRedo && own.Editor.UndoGroups.Count == 1, "The failure leaves the native history grouping intact");
        own.Editor.Redo();
        Assert((double)own.Document["modifications"][0]["placement"]["position"][0] == 130 && own.Editor.IsDirty, "History execution after the failure stays consistent with the document");
        Assert(own.Editor.Save() == null && !own.Editor.IsDirty && own.Session.IncompleteOperation == null && (double)own.Saved["modifications"][0]["placement"]["position"][0] == 130, "A contained patcher failure leaves the native grouping intact, so a complete Save of the shown state is clean");
    }
    static void FailedEditsRestoreAndGuardRetry() {
        var w = new World();
        w.Scene.FailNextApplies = 1;
        var outcome = w.Session.Edit(P, Pose(112), null);
        Assert(!outcome.Applied && outcome.Error.Contains("prior state was restored") && w.AtUnderlying && w.Document["modifications"].Count() == 0 && w.Editor.UndoGroups.Count == 0 && !w.Editor.IsDirty, "A failed edit restores world and document and leaves no history or dirty state");
        Assert(w.Session.Edit(P, Pose(112), null).Applied, "A verified restoration permits a new edit");
        w.Scene.FailApply = true; outcome = w.Session.Edit(P, Pose(140), null); w.Scene.FailApply = false;
        Assert(!outcome.Applied && outcome.Error.Contains("cleanup is unverified") && (double)w.Document["modifications"][0]["placement"]["position"][0] == 112, "Unverified cleanup is reported and the document keeps the prior desired state");
        Assert(!w.Session.Edit(P, Pose(150), null).Applied && !w.Session.Remove(P).Applied, "Further mutation of that object is blocked until it is rechecked");
        Assert(w.Session.Edit(Q, Pose(160), null).Applied && w.Other.X == 60, "Independent objects remain editable");
        Assert(w.Session.Recheck(P).Applied && w.Box.X == 12 && w.Session.Edit(P, Pose(150), null).Applied && w.Box.X == 50, "The guarded retry reconciles the actual state with the document before unblocking");
        Assert(!w.Session.Edit(new TargetIdentity("s", "missing", "c"), Pose(1), null).Applied, "A missing target is refused before mutation");
        w.Scene.Objects["twins"] = new List<object> { new Box(), new Box() };
        Assert(!w.Session.Edit(new TargetIdentity("s", "twins", "c"), Pose(1), null).Applied, "An ambiguous target is refused before mutation");
        Assert(!w.Session.Edit(P, new Placement(new[] { 1.0, 2.0, Double.NaN }, new[] { 0.0, 0.0, 0.0, 1.0 }), null).Applied && !w.Session.Edit(P, new Placement(new[] { 1.0, 2.0, 3.0 }, new[] { 0.0, 0.0, 0.0, 0.0 }), null).Applied && w.Box.X == 50, "Invalid numeric input is refused before mutation");
    }
    static void UnknownContentAndProtectedDocumentsSurvive() {
        var future = "{\"id\":\"later\",\"target\":{\"scene\":\"s\",\"path\":\"q\",\"component\":\"c\"},\"futureOperation\":{\"ratio\":1.50},\"dependsOn\":[]}";
        var w = new World(Doc(future + "," + Mod("move-p", "p", Fixed(112)), "\"futureNumber\":0.123456789012345678901234567890123456789,\"stamp\":\"2020-01-01T00:00:00Z\","));
        Assert(w.Other.X == -50 && w.Session.Blocked == null && !w.Editor.IsDirty, "Unknown content is not executed and does not make the session dirty");
        Assert(w.Session.Edit(P, Pose(130), null).Applied && w.Editor.Save() == null, "A known modification beside unknown content can be edited and saved");
        Assert(w.Store.Patch.Contains("0.123456789012345678901234567890123456789") && w.Store.Patch.Contains("2020-01-01T00:00:00Z") && w.Store.Patch.Contains("1.50"), "Unknown numbers and strings keep their exact content through edit and Save");
        Assert(JToken.DeepEquals(w.Saved["modifications"][0], JObject.Parse(future)) && (double)w.Saved["modifications"][1]["placement"]["position"][0] == 130, "The unknown modification is written back unchanged, in place");
        var reread = new World(w.Store.Patch);
        Assert(reread.Session.Blocked == null && !reread.Editor.IsDirty && reread.Box.X == 30, "The saved document reads back clean and applies");
        Assert(!w.Session.Edit(Q, Pose(1), null).Applied && !w.Session.Remove(Q).Applied && w.Other.X == -50, "A modification with unknown content is neither edited nor deleted");
        foreach (var text in new[] { "{broken", Doc("").Replace("\"formatVersion\":1", "\"formatVersion\":2"), Doc(Mod("a", "p", Fixed(112))).Replace("\"mapId\":\"map\"", "\"mapId\":\"elsewhere\"") }) {
            var guarded = new World(text);
            Assert(guarded.Session.Blocked != null && !guarded.Session.Edit(P, Pose(112), null).Applied && guarded.AtUnderlying, "An unreadable, newer or foreign document blocks authoring");
            guarded.Editor.NativeEdit(1);
            Assert(guarded.Editor.Save() == null && guarded.Store.Patch == text && guarded.Store.PatchWrites == 0 && !guarded.Editor.IsDirty, "Native Save proceeds and the protected document is not overwritten");
        }
    }
    static void InspectedEditsBindToTheirObservation() {
        var w = new World();
        w.Scene.Observations = () => new[] { new SceneObservation(w.Box, P, "Board", w.Box.Active, new[] { w.Box.Position[0] + w.Scene.Origin, w.Box.Position[1], w.Box.Position[2] },
            w.Box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var inventory = new SceneInventory(w.Scene); inventory.Refresh();
        var inspected = inventory.Pin(inventory.Entries.Single());
        Assert(w.Session.Edit(P, Pose(112), null, inspected).Applied && w.Box.X == 12, "An edit bound to the current inspection applies");
        var mutations = w.Scene.MutationCalls;
        var outcome = w.Session.Edit(P, Pose(130), null, inspected);
        Assert(!outcome.Applied && outcome.Error.Contains("refresh and inspect again") && w.Box.X == 12 && w.Scene.MutationCalls == mutations && w.Editor.UndoGroups.Count == 1 && (double)w.Document["modifications"][0]["placement"]["position"][0] == 112,
            "An edit bound to an outdated inspection is refused before mutation, document change or history: " + outcome.Error);
        Assert(!w.Session.Remove(P, inspected).Applied && !w.Session.Recheck(P, inspected).Applied && w.Box.X == 12, "Removal and recheck are bound to the inspection in the same way");
        try { inventory.Pin(inventory.Entries.Single()); throw new Exception("A stale inventory issued a token"); } catch (InvalidOperationException) { }
        inventory.Refresh(); inspected = inventory.Pin(inventory.Entries.Single());
        Assert(w.Session.Edit(P, Pose(130), null, inspected).Applied && w.Box.X == 30, "A refreshed inspection permits the next edit");
        inventory.Refresh(); inspected = inventory.Pin(inventory.Entries.Single());
        var replacement = new Box(); mutations = w.Scene.MutationCalls;
        w.Scene.BeforeRead = () => w.Scene.Objects["p"] = new List<object> { replacement };
        outcome = w.Session.Edit(P, Pose(150), null, inspected);
        Assert(!outcome.Applied && outcome.Error.Contains("Target changed before mutation") && w.Box.X == 30 && replacement.X == 0 && w.Scene.MutationCalls == mutations,
            "A target replaced while its state is read is rejected without moving either object or requesting restoration: " + outcome.Error);
        Assert((double)w.Document["modifications"][0]["placement"]["position"][0] == 130 && w.Editor.UndoGroups.Count == 2, "The rejected edit leaves document and history as they were");
        w.Scene.Objects["p"] = new List<object> { w.Box };
        Assert(w.Session.Edit(P, Pose(150), null, inspected).Applied, "The pre-mutation rejection does not block the object");
        w.Scene.BeforeRead = () => w.Scene.Maps = new[] { "elsewhere" };
        outcome = w.Session.Edit(P, Pose(170), null);
        Assert(!outcome.Applied && outcome.Error.Contains("Map context changed") && w.Box.X == 50, "The immediate guard also holds for an edit without an inspection token: " + outcome.Error);
        w.Scene.Maps = new[] { "map", "layer" };
        w.Editor.Undo();
        Assert(w.Box.X == 30 && w.Session.IncompleteOperation == null, "History replay needs no inspection token and still passes the immediate guard");
        w.Scene.BeforeRead = () => w.Scene.Objects["p"] = new List<object> { replacement };
        w.Editor.Undo();
        Assert(w.Box.X == 30 && replacement.X == 0 && w.Session.IncompleteOperation.Contains("Target changed before mutation") && w.Editor.IsDirty, "A target replaced during Undo is not redirected; the operation is reported incomplete");
    }
    static void ExitIdentifiesOutstandingWork() {
        var w = new World();
        Assert(w.Session.ExitWarning(w.Editor.NativeDirty) == null, "A clean editor has no exit warning");
        w.Session.Edit(P, Pose(112), null); w.Editor.FailNative = true; w.Editor.Save();
        Assert(w.Session.ExitWarning(w.Editor.NativeDirty) == "Unsaved work: native map data.", "After a partial Save the exit warning names the outstanding native work");
        w.Editor.FailNative = false; w.Editor.Save(); w.Session.Edit(P, Pose(120), null);
        Assert(w.Session.ExitWarning(w.Editor.NativeDirty) == "Unsaved work: native map data, patch document.", "The exit warning names outstanding native and patcher work");
    }
    static void NumericEditReachesWorldAndDocument() {
        var w = new World();
        Assert(w.Session.Blocked == null && !w.Editor.IsDirty, "A MapMod without a patch document starts clean and editable");
        var outcome = w.Session.Edit(P, Pose(112), null);
        Assert(outcome.Applied && outcome.Error == null, "A numeric placement edit completes");
        Assert(w.Box.X == 12 && w.Box.Position[1] == 2 && w.Box.Position[2] == 3 && w.Box.Rotation.SequenceEqual(new[] { 0.0, 0.0, 0.0, 1.0 }), "The edit moves the object to the fixed map placement through the current origin");
        var item = (JObject)w.Document["modifications"][0];
        Assert((int)w.Document["formatVersion"] == 1 && (string)w.Document["sourceMap"]["mapId"] == "map", "A new document carries the format and the active map context");
        Assert((string)item["target"]["path"] == "p" && (string)item["placement"]["mode"] == "fixed" && (double)item["placement"]["position"][0] == 112 && (double)item["placement"]["rotation"][3] == 1, "The canonical document holds the desired fixed placement");
        var replay = new Scene(); var fresh = new Box(); replay.Objects["p"] = new List<object> { fresh };
        Assert(new PatchEngine(replay).Apply(w.Session.DocumentText).Outcomes.Values.Single().Applied && fresh.X == 12, "The authored document applies through the runtime engine");
    }
}
