// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Controlled storage for the edited mod, with per-participant write failures.
class Store : IStorage {
    public string Patch, Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"rr/game >=0.6.2\"]}}";
    public bool FailPatch, FailInfo;
    public int PatchWrites, InfoWrites;
    public string ReadScenePatchFile() => Patch;
    public void WriteScenePatchFile(string text) { if (FailPatch) throw new IOException("patch write injected"); Patch = text; PatchWrites++; }
    public string ReadModInfo() => Info;
    public void WriteModInfo(string text) { if (FailInfo) throw new IOException("metadata write injected"); Info = text; InfoWrites++; }
}

// The real files of a mod folder, with a write that stops midway after part of its bytes reached the disk,
// or a failing replacement of a file by new content, for files whose name starts with the given text. With
// ReplaceFailures set, the replacement fails only that many times and then succeeds, as a file another process
// holds open for a moment does. With RefuseReplace set, the replacement is refused as Windows refuses to replace
// a read-only file.
class FaultyFiles : DiskFiles {
    public string FailWrite, FailReplace;
    public int? ReplaceFailures;
    public bool RefuseReplace;
    // Every replacement or move into place, by file name, in order; a pause recorder can add its pauses to the same log.
    public readonly List<string> Log = new List<string>();
    static bool Hit(string prefix, string path) => prefix != null && Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal);
    public override void Write(string path, byte[] bytes) {
        if (!Hit(FailWrite, path)) { base.Write(path, bytes); return; }
        File.WriteAllBytes(path, bytes.Take(bytes.Length / 2).ToArray());
        throw new IOException("write interrupted midway");
    }
    public override void Replace(string source, string destination) { Fail(destination); base.Replace(source, destination); }
    public override void Move(string source, string destination) { Fail(destination); base.Move(source, destination); }
    void Fail(string destination) {
        Log.Add("place " + Path.GetFileName(destination));
        if (!Hit(FailReplace, destination)) return;
        if (RefuseReplace) throw new UnauthorizedAccessException("access to the path is denied");
        if (ReplaceFailures == null || ReplaceFailures-- > 0) throw new IOException("replace failed");
    }
}

// Mirrors the grouping, stack, pruning and change-count rules of the game's undo manager,
// and the native map editor's count-based dirty flag, so history accounting meets the real collisions.
class NativeMapEditor : INativeHistory {
    sealed class Group { public string Name; public List<Action> Actions = new List<Action>(); }
    readonly List<Group> undo = new List<Group>(), redo = new List<Group>();
    Group current; bool undoing, redoing;
    public int ChangeCount, CleanCount, MaxLevels = 50, NativeValue, NativeWrites, TerrainWrites, Alerts;
    public bool FailNative, ThrowInNativeAction;
    public PatchEditingSession Session;
    public Action HistoryStarted { get; set; }
    public Action HistoryFinished { get; set; }
    public IReadOnlyList<object> UndoGroups => undo;
    public IReadOnlyList<object> RedoGroups => redo;
    public IReadOnlyList<Action> GroupActions(object group) => ((Group)group).Actions.ToArray();
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
        HistoryStarted?.Invoke();
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
        HistoryFinished?.Invoke();
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
    // The native 0.6.3 Save button, which stays enabled: the patcher's refusal runs before the click writes terrain or map
    // data and returns its reason; otherwise the click writes the edited terrain, then runs the Save route, and a failure
    // reaches the native Save Failed alert.
    public string ClickSave() {
        var refusal = Session.SaveRefusalBeforeWrite();
        if (refusal != null) return refusal;
        TerrainWrites++;
        var failure = Save();
        if (failure != null) Alerts++;
        return failure;
    }
}

static class PatchEditingTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static readonly TargetIdentity P = new TargetIdentity("s", "p", "c");
    static Placement PlacementAt(double x, double y = 2, double z = 3) => new Placement(new[] { x, y, z }, new[] { 0.0, 0.0, 0.0, 1.0 });
    static readonly TargetIdentity Q = new TargetIdentity("s", "q", "c");
    const string Tail = "; outstanding: ";
    static string Mod(string id, string path, string body) => "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"}," + body + ",\"dependsOn\":[]}";
    static string Fixed(double x) => "\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",2,3],\"rotation\":[0,0,0,1]}";
    static string Doc(string items, string extra = "") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"}," + extra + "\"modifications\":{" + items + "}}";
    sealed class World {
        public Scene Scene = new Scene(); public Box Box = new Box { Position = new[] { -93.0, 8.0, 9.0 }, Rotation = new[] { 0.0, 1.0, 0.0, 0.0 } };
        public Box Other = new Box { Position = new[] { -50.0, 0.0, 0.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        public Store Store = new Store(); public NativeMapEditor MapEditor = new NativeMapEditor(); public PatchEditingSession Session;
        public bool AtUnderlying => Box.X == -93 && Box.Position[1] == 8 && Box.Position[2] == 9 && Box.Rotation.SequenceEqual(new[] { 0.0, 1.0, 0.0, 0.0 }) && Box.Active;
        public World(string patch = null, string info = null, IStorage files = null, string[] maps = null, string[] mapPatches = null) {
            if (maps != null) Scene.Maps = maps;
            if (mapPatches != null) Scene.MapPatches = mapPatches;
            Scene.Objects["p"] = new List<object> { Box }; Scene.Objects["q"] = new List<object> { Other }; Store.Patch = patch;
            if (info != null) Store.Info = info;
            else if (patch != null) Store.Info = Store.Info.Replace("\"rr/game >=0.6.2\"", "\"rr/game >=0.6.2\",\"crankyant/scene-patcher >=0.5.0\"");
            var engine = new PatchEngine(Scene);
            if (patch != null) engine.Apply(patch, "author/pack");
            Session = new PatchEditingSession(engine, MapEditor, files ?? Store);
            MapEditor.Session = Session;
        }
        public JObject Saved => JObject.Parse(Store.Patch);
        public JObject ScenePatchFile => JObject.Parse(Session.ScenePatchFileText);
    }
    internal static void Run() {
        NumericEditReachesWorldAndScenePatchFile();
        ActivityFollowsTheSameRoute();
        UndoAndRedoAreOneCoherentChange();
        MixedHistoryKeepsSavedIdentity();
        RemovalRecomputesUnderlyingState();
        SaveWritesEveryParticipant();
        PartialWritesStayIdentifiedAndRetryable();
        InterruptedFileWritesKeepThePreviousFile();
        ReplacementIsRetriedOnlyWhileTheFileIsHeld();
        DependencyMinimumRisesOnlyWhenRequired();
        PruningAndHistoryErrorsStayTruthful();
        FailedEditsRestoreAndGuardRetry();
        UnknownContentAndProtectedScenePatchFilesSurvive();
        ExitIdentifiesOutstandingWork();
        InspectedEditsBindToTheirObservation();
        EqualValuesAreNotStored();
        NewScenePatchFilesNameTheBottomMapLayer();
        CopyIdNamesTheScenePatchFilesSceneTargetMap();
        ExistingSceneTargetMapsAreKept();
        AScenePatchFileNamingAMapPatchIsRefusedInTheEditor();
        AxesAtStoredPrecisionKeepTheirLiteral();
        RefusedSaveWritesNothing();
        DependencyRefusalsComeBeforeTheClickWrites();
        SaveWritesTheScenePatchFileOnlyWhenItsContentChanged();
        PatchOnlyEditsAnnounceTheirOutstandingWork();
        SeveralModificationsBlockEveryEditOfAnyObject();
    }
    // Any object, not only a crossing controller, that several modifications of the scene patch file target gets the same
    // reason ahead of time, which the panel shows on its edit controls and Remove modification. Each edit still refuses with
    // it and changes nothing; an object with one modification is not blocked.
    static void SeveralModificationsBlockEveryEditOfAnyObject() {
        var w = new World(Doc(Mod("move-p", "p", Fixed(112)) + "," + Mod("move-p-again", "p", Fixed(120)) + "," + Mod("move-q", "q", Fixed(5))));
        Assert(w.Session.SeveralModificationsBlock(P) == PatchEditingSession.SeveralModifications, "The object with two modifications is blocked: " + w.Session.SeveralModificationsBlock(P));
        Assert(w.Session.SeveralModificationsBlock(Q) == null, "The object with one modification is not blocked");
        var text = w.Store.Patch;
        foreach (var (control, outcome) in new[] { ("Remove modification", w.Session.Remove(P)), ("Placement", w.Session.Edit(P, PlacementAt(130), null)) })
            Assert(!outcome.Applied && outcome.Error.Contains(PatchEditingSession.SeveralModifications), control + " still refuses with the same reason: " + outcome.Error);
        Assert(w.Store.Patch == text, "The refusals leave the scene patch file as it was");
    }
    // The native title star and the Save button's unsaved tint are rebuilt from IsDirty on this notification. It follows a
    // patch-only edit once the edit is accounted, so the rebuilt chrome reads it as unsaved; Undo and Redo do not raise it
    // while they execute, because their own refresh follows once the history identity settled.
    static void PatchOnlyEditsAnnounceTheirOutstandingWork() {
        var w = new World(Doc(Mod("move-p", "p", Fixed(112))));
        var seen = new List<bool>();
        w.Session.OutstandingMayHaveChanged = () => seen.Add(w.MapEditor.IsDirty);
        Assert(w.Session.Edit(P, PlacementAt(130), null).Applied && seen.SequenceEqual(new[] { true }), "A patch-only edit announces itself as unsaved: " + String.Join(",", seen));
        w.MapEditor.Undo();
        Assert(seen.Count == 1 && !w.MapEditor.IsDirty, "Undo raises no notification while it executes");
        w.MapEditor.NativeEdit(5);
        Assert(seen.Count == 2 && seen[1], "A native edit announces itself too");
    }
    // A Save refusal is decided before the native Save button writes anything: no terrain, map, scene patch file or
    // dependency metadata, and no native Save Failed alert. Its reason names what to do.
    static void RefusedSaveWritesNothing() {
        var w = new World(Doc(Mod("move-p", "p", Fixed(112))));
        w.MapEditor.NativeEdit(1); w.Session.Edit(P, PlacementAt(120), null);
        var external = Doc(Mod("move-p", "p", Fixed(150))); w.Store.Patch = external;
        var refusal = w.MapEditor.ClickSave();
        Assert(refusal != null && refusal.Contains("Save won't overwrite it until you choose a version"), "A pending version choice refuses Save with its reason: " + refusal);
        Assert(w.MapEditor.TerrainWrites == 0 && w.MapEditor.NativeWrites == 0 && w.MapEditor.Alerts == 0 && w.Store.Patch == external && w.Store.InfoWrites == 0 && w.MapEditor.IsDirty,
            "A refused Save writes no terrain, map data, scene patch file or metadata and raises no Save Failed alert");
        w.Store.Patch = "{ not json";
        refusal = w.MapEditor.ClickSave();
        Assert(refusal != null && refusal.StartsWith("File needs repair: ") && w.MapEditor.TerrainWrites == 0 && w.MapEditor.NativeWrites == 0 && w.MapEditor.Alerts == 0 && w.Store.Patch == "{ not json", "A file that needs repair refuses Save before anything is written: " + refusal);
        w.Store.Patch = external; w.Session.CheckFileVersion();
        Assert(w.Session.KeepEditorVersion().Applied && w.MapEditor.ClickSave() == null && w.MapEditor.TerrainWrites == 1 && w.MapEditor.NativeWrites == 1 && !w.MapEditor.IsDirty
            && (double)w.Saved["modifications"]["move-p"]["placement"]["position"][0] == 120, "After the choice the same Save button writes every participant");
        var clean = new World(Doc(Mod("move-p", "p", Fixed(112))));
        Assert(clean.Session.SaveRefusalBeforeWrite() == null, "Without a refusal the Save runs");
        var none = new World(); none.MapEditor.NativeEdit(1);
        Assert(none.MapEditor.ClickSave() == null && none.MapEditor.NativeWrites == 1, "A map editor without scene patch work saves natively");
    }
    // An Info.json that the dependency check cannot raise refuses Save before the Save button writes terrain or map data: a
    // constraint that excludes the required minimum, an unreadable or inexact file, the patcher listed twice, and
    // dependencies that are not an object. A missing dependency that Save can add is no refusal.
    static void DependencyRefusalsComeBeforeTheClickWrites() {
        string Info(string dependencies) => "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":" + dependencies + "}";
        var cases = new[] {
            new { Info = Info("{\"required\":[\"rr/game >=0.6.2\",\"crankyant/scene-patcher <0.1.0\"]}"), Expect = "excludes the required minimum" },
            new { Info = "{ not json", Expect = "unreadable" },
            new { Info = Info("{\"required\":[\"crankyant/scene-patcher >=0.1.0\",\"crankyant/scene-patcher >=0.2.0\"]}"), Expect = "more than once" },
            new { Info = Info("[]"), Expect = "not an object" } };
        foreach (var c in cases) {
            var w = new World(Doc(Mod("move-p", "p", Fixed(112))), info: c.Info);
            w.MapEditor.NativeEdit(1); w.Session.Edit(P, PlacementAt(120), null);
            var refusal = w.MapEditor.ClickSave();
            Assert(refusal != null && refusal.Contains(c.Expect) && refusal.StartsWith("The patcher dependency was not written: ", StringComparison.Ordinal), "The dependency check refuses Save: " + refusal);
            Assert(w.MapEditor.TerrainWrites == 0 && w.MapEditor.NativeWrites == 0 && w.MapEditor.Alerts == 0 && w.Store.InfoWrites == 0 && w.Store.PatchWrites == 0 && w.Store.Info == c.Info && w.MapEditor.IsDirty,
                "The refusal comes before terrain, map, metadata or scene patch file are written: " + c.Expect);
        }
        var missing = new World(Doc(Mod("move-p", "p", Fixed(112))), info: Info("{\"required\":[\"rr/game >=0.6.2\"]}"));
        Assert(missing.Session.SaveRefusalBeforeWrite() == null && missing.MapEditor.ClickSave() == null && missing.Store.InfoWrites == 1, "A missing dependency is added by the Save, not refused");
        var none = new World(info: "{ not json"); none.MapEditor.NativeEdit(1);
        Assert(none.MapEditor.ClickSave() == null && none.MapEditor.NativeWrites == 1, "Without modifications Info.json is not read and does not refuse the Save");
    }
    // The Save button is enabled while nothing is outstanding too. A Save writes the scene patch file only when its content
    // differs from the text last read or written, whatever the native work and the file's modifications.
    static void SaveWritesTheScenePatchFileOnlyWhenItsContentChanged() {
        var text = Doc(Mod("move-p", "p", Fixed(112)) + "," + Mod("move-q", "q", Fixed(160)));
        var w = new World(text);
        Assert(!w.MapEditor.IsDirty && w.MapEditor.ClickSave() == null && w.Store.PatchWrites == 0 && w.Store.InfoWrites == 0 && w.Store.Patch == text,
            "A Save with nothing outstanding leaves a scene patch file with modifications unwritten");
        w.MapEditor.NativeEdit(1);
        Assert(w.MapEditor.ClickSave() == null && w.MapEditor.NativeWrites == 2 && w.Store.PatchWrites == 0, "A native-only Save leaves the scene patch file unwritten");
        w.Session.Edit(P, PlacementAt(130), null); w.MapEditor.Undo();
        Assert(!w.MapEditor.IsDirty && w.MapEditor.ClickSave() == null && w.Store.PatchWrites == 0, "An edit and its Undo leave equal content, so Save writes nothing");
        var spaced = JObject.Parse(text).ToString(Newtonsoft.Json.Formatting.Indented);
        var reordered = new World(spaced);
        Assert(reordered.MapEditor.ClickSave() == null && reordered.Store.PatchWrites == 0 && reordered.Store.Patch == spaced, "Other spacing in the file read is no change to write");
        w.Session.Edit(P, PlacementAt(140), null);
        Assert(w.MapEditor.IsDirty && w.MapEditor.ClickSave() == null && w.Store.PatchWrites == 1 && (double)w.Saved["modifications"]["move-p"]["placement"]["position"][0] == 140, "A changed content is written once");
        Assert(w.MapEditor.ClickSave() == null && w.Store.PatchWrites == 1, "A repeated Save writes nothing further");
    }
    // The loaded map context as the game resolves it: the edited map definition, then its map layers topmost first.
    // Source of the order: Railroader 0.6.2 build 26335745, Game.MapLayerResolver.Resolve sorts the layer graph with the
    // map before each of its layers and an earlier listed layer before a later one, removes the edited map, and
    // Game.MapLoader passes the matching ActiveMapDefinition.MapLayerDataPaths as baseLayerPathsTopmostFirst. Layers of
    // map layers are part of that one list, so its last entry is the lowest layer the game composes.
    static readonly string[] NoLayers = { "author/own-map" };
    static readonly string[] OneLayer = { "author/own-map", "rr/bushnell-whittier" };
    static readonly string[] TwoLayers = { "author/own-map", "other/upper-layer", "rr/bushnell-whittier" };
    // A scene patch file that scene patch editing creates names the bottom map layer, or the edited map definition
    // itself when that has no map layers, and Save writes that scene target map.
    static void NewScenePatchFilesNameTheBottomMapLayer() {
        foreach (var (maps, expected) in new[] { (NoLayers, "author/own-map"), (OneLayer, "rr/bushnell-whittier"), (TwoLayers, "rr/bushnell-whittier") }) {
            var w = new World(maps: maps);
            Assert(w.Session.Blocked == null && (string)w.ScenePatchFile["sourceMap"]["mapId"] == expected, "A new scene patch file for " + maps.Length + " resolved maps names " + expected + ": " + w.ScenePatchFile["sourceMap"]);
            Assert(w.Session.Edit(P, PlacementAt(112), null).Applied && w.MapEditor.Save() == null && (string)w.Saved["sourceMap"]["mapId"] == expected, "Save writes the scene target map " + expected);
            var reopened = new World(w.Store.Patch, w.Store.Info, maps: maps);
            Assert(reopened.Session.Blocked == null && reopened.Box.X == 12 && !reopened.MapEditor.IsDirty, "The saved scene patch file reopens and applies on the edited map: " + reopened.Session.Blocked);
        }
        var upper = new World(maps: new[] { "other/upper-layer", "rr/bushnell-whittier" });
        Assert((string)upper.ScenePatchFile["sourceMap"]["mapId"] == "rr/bushnell-whittier", "Editing a map layer itself also names its bottom layer");
    }
    // Copy ID names the scene target map of the edited mod's scene patch file, or the scene target map a new scene patch file would name.
    static void CopyIdNamesTheScenePatchFilesSceneTargetMap() {
        var entry = new SceneObservation(new Box(), new TargetIdentity("s", "p", "c"), "Board", true, new[] { 112.0, 2, 3 }, new[] { 0.0, 0, 0, 1 }, new string[0], new string[0], new string[0], "Fixed placement");
        foreach (var (maps, expected) in new[] { (NoLayers, "author/own-map"), (TwoLayers, "rr/bushnell-whittier") }) {
            var copied = JObject.Parse(new World(maps: maps).Session.CopyId(entry));
            Assert((string)copied["sourceMap"]["mapId"] == expected && (string)copied["target"]["path"] == "p" && (double)copied["placement"]["position"][0] == 112, "Copy ID without a scene patch file names " + expected + ": " + copied);
        }
        var existing = new World(Doc(Mod("move-p", "p", Fixed(112))).Replace("\"mapId\":\"map\"", "\"mapId\":\"author/own-map\""), maps: TwoLayers);
        Assert(existing.Session.Blocked == null && (string)JObject.Parse(existing.Session.CopyId(entry))["sourceMap"]["mapId"] == "author/own-map", "Copy ID names the existing scene patch file's scene target map");
        Assert((string)JObject.Parse(PatchEditingSession.CopyId(TwoLayers, entry))["sourceMap"]["mapId"] == "rr/bushnell-whittier", "Copy ID without scene patch editing names the scene target map a new scene patch file would name");
        // While the edited mod's scene patch file cannot be read, Copy ID is unavailable with the session's refusal: it names
        // no scene target map, because the scene patch file that exists names one this session cannot read.
        foreach (var text in new[] { "{broken", "{\"formatVersion\":1,\"sourceMap\":{},\"modifications\":{}}", "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":3},\"modifications\":{}}" }) {
            var blocked = new World(text, maps: TwoLayers);
            Assert(blocked.Session.Blocked != null && blocked.Session.CopyIdRefusal == blocked.Session.Blocked, "An unreadable scene patch file makes Copy ID unavailable with the session's refusal: " + text);
            try { blocked.Session.CopyId(entry); throw new Exception("Copy ID copied a scene target map for an unreadable scene patch file: " + text); }
            catch (InvalidOperationException e) { Assert(e.Message == blocked.Session.Blocked, "Copy ID refuses with the session's refusal: " + e.Message); }
        }
        Assert(new World(maps: TwoLayers).Session.CopyIdRefusal == null && existing.Session.CopyIdRefusal == null, "Copy ID is available with a new or a readable scene patch file");
    }
    // An edit that writes a position or rotation leaves an axis whose new value equals the scene patch file's value at the
    // single precision the game stores in its source literal, so an edit of one axis leaves the others' text alone.
    static void AxesAtStoredPrecisionKeepTheirLiteral() {
        const string initialPlacementJson = "\"placement\":{\"mode\":\"fixed\",\"position\":[25702.977,2,3],\"rotation\":[0,0.7071068,0,0.7071068]}";
        var w = new World(Doc(Mod("move-p", "p", initialPlacementJson)));
        var readBack = new Placement(new[] { (double)(float)25702.977, 2.0, 13.0 }, new[] { 0.0, (double)(float)0.7071068, 0.0, (double)(float)0.7071068 });
        Assert(readBack.Position[0] != 25702.977 && readBack.Rotation[1] != 0.7071068, "The read-back values differ from the scene patch file's literals in double precision");
        var edit = w.Session.Edit(P, readBack, null);
        Assert(edit.Applied && w.MapEditor.Save() == null, "An edit of Z alone completes and saves: " + edit.Error + " " + w.Session.Blocked);
        Assert(w.Store.Patch.Contains("25702.977") && !w.Store.Patch.Contains("25702.9765625") && w.Store.Patch.Split(new[] { "0.7071068" }, StringSplitOptions.None).Length == 3 && !w.Store.Patch.Contains("0.70710676"),
            "X and the rotation keep their literals; only Z is rewritten: " + w.Store.Patch);
        Assert((double)w.Saved["modifications"]["move-p"]["placement"]["position"][2] == 13, "The changed axis is written");
        var finer = new World(w.Store.Patch, w.Store.Info);
        Assert(finer.Session.Edit(P, new Placement(new[] { 25702.98, 2.0, 13.0 }, readBack.Rotation), null).Applied && finer.MapEditor.Save() == null
            && finer.Store.Patch.Contains("25702.98") && !finer.Store.Patch.Contains("25702.977"), "A value the game stores differently is written: " + finer.Store.Patch);
        var same = new World(w.Store.Patch, w.Store.Info);
        var outcome = same.Session.Edit(P, readBack, null);
        Assert(outcome.Applied && outcome.Unchanged && !same.MapEditor.CanUndo && !same.MapEditor.IsDirty, "An edit that differs only below the stored precision changes nothing");
    }
    // While the native map editor edits a map patch, the map patch is in the editor's map context, but in a save a map patch
    // is never the loaded map or one of its layers, so a scene patch file that names it would be skipped in play. The editor
    // refuses it with that reason, before any edit and in the engine's application. Scene patch editing in the map patch
    // itself keeps working: a new scene patch file, and one that names the bottom map layer, are accepted.
    static void AScenePatchFileNamingAMapPatchIsRefusedInTheEditor() {
        var maps = new[] { "author/station-patch", "rr/bushnell-whittier" };
        World InPatch(string patch) => new World(patch, maps: maps, mapPatches: new[] { "author/station-patch" });
        var naming = Doc(Mod("move-p", "p", Fixed(112))).Replace("\"mapId\":\"map\"", "\"mapId\":\"author/station-patch\"");
        var refused = InPatch(naming);
        Assert(refused.Session.Blocked != null && refused.Session.Blocked.Contains("author/station-patch is a map patch") && refused.Session.Blocked.Contains("rr/bushnell-whittier"),
            "A scene patch file naming the edited map patch is refused with why it would not apply in play: " + refused.Session.Blocked);
        Assert(refused.Box.X == -93 && !refused.Session.Edit(P, PlacementAt(130), null).Applied && refused.Store.PatchWrites == 0, "The refused file applies nothing and cannot be edited");
        var engine = new PatchEngine(refused.Scene).Apply(naming, "author/pack");
        Assert(engine.Error != null && engine.Error.Contains("is a map patch") && engine.MapEditorOnly, "The engine refuses it in the editor's application too: " + engine.Error);
        Assert(refused.Session.ScenePatchFileDiagnostics.Any(d => d.Message.Contains("is a map patch")), "The diagnosis points at sourceMap.mapId");
        var bottom = Doc(Mod("move-p", "p", Fixed(112))).Replace("\"mapId\":\"map\"", "\"mapId\":\"rr/bushnell-whittier\"");
        var accepted = InPatch(bottom);
        Assert(accepted.Session.Blocked == null && accepted.Box.X == 12, "A scene patch file naming the bottom map layer is accepted in the map patch: " + accepted.Session.Blocked);
        Assert(accepted.Session.Edit(P, PlacementAt(130), null).Applied && accepted.MapEditor.Save() == null && (string)accepted.Saved["sourceMap"]["mapId"] == "rr/bushnell-whittier", "Editing and Save keep the bottom map layer");
        var diagnosis = refused.Session.ScenePatchFileDiagnostics.Single(d => d.Path == "sourceMap.mapId");
        Assert(diagnosis.Expected == null && diagnosis.Message.EndsWith("Use the bottom map layer, rr/bushnell-whittier.", StringComparison.Ordinal), "The repair gives the bottom map layer once: " + diagnosis.Message);
        var other = InPatch(Doc(Mod("move-p", "p", Fixed(112))).Replace("\"mapId\":\"map\"", "\"mapId\":\"other/map\""));
        var otherHint = other.Session.ScenePatchFileDiagnostics.Single(d => d.Path == "sourceMap.mapId").Expected;
        Assert(other.Session.Blocked != null && other.Session.Blocked.StartsWith(SceneTargetMapCheck.Mismatch, StringComparison.Ordinal) && otherHint.Contains("rr/bushnell-whittier") && !otherHint.Contains("author/station-patch"),
            "A scene target map outside the map context lists only the maps that are no map patch: " + otherHint);

        // The host's SceneCapabilities carries the map patches of the native map editor's context to the check and the engine.
        var capabilities = new SceneCapabilities(() => maps) { MapPatches = () => new[] { "author/station-patch" } };
        Assert(capabilities.MapPatchIds.SequenceEqual(new[] { "author/station-patch" }), "SceneCapabilities lists the map patches its host supplies");
        Assert(SceneTargetMapCheck.Problem(capabilities, "author/station-patch")?.Contains("is a map patch") == true && SceneTargetMapCheck.Problem(capabilities, "rr/bushnell-whittier") == null,
            "Through SceneCapabilities the map patch is refused and the bottom map layer accepted");
        var throughEngine = new PatchEngine(capabilities).Apply(naming, "author/pack");
        Assert(throughEngine.Error != null && throughEngine.Error.Contains("is a map patch") && throughEngine.MapEditorOnly, "The engine over SceneCapabilities refuses it: " + throughEngine.Error);
        var withoutHost = new SceneCapabilities(() => maps);
        Assert(!withoutHost.MapPatchIds.Any() && SceneTargetMapCheck.Problem(withoutHost, "author/station-patch") == null, "Without a host list no map patch is known, as in a save");
        var fresh = InPatch(null);
        Assert(fresh.Session.Blocked == null && fresh.Session.Edit(P, PlacementAt(112), null).Applied && fresh.MapEditor.Save() == null && (string)fresh.Saved["sourceMap"]["mapId"] == "rr/bushnell-whittier",
            "A new scene patch file in the map patch names the bottom map layer");
    }
    // An existing scene patch file keeps the scene target map it names through opening, editing and Save; scene patch editing never
    // rewrites it to the bottom map layer, also when the modder set a layer or the edited map by hand.
    static void ExistingSceneTargetMapsAreKept() {
        foreach (var (maps, named) in new[] { (TwoLayers, "author/own-map"), (TwoLayers, "other/upper-layer"), (TwoLayers, "rr/bushnell-whittier"), (NoLayers, "author/own-map") }) {
            var text = Doc(Mod("move-p", "p", Fixed(112))).Replace("\"mapId\":\"map\"", "\"mapId\":\"" + named + "\"");
            var w = new World(text, maps: maps);
            Assert(w.Session.Blocked == null && (string)w.ScenePatchFile["sourceMap"]["mapId"] == named, "Opening keeps the scene target map " + named + ": " + w.Session.Blocked);
            Assert(w.Session.Edit(P, PlacementAt(130), null).Applied && (string)w.ScenePatchFile["sourceMap"]["mapId"] == named, "Editing keeps the scene target map " + named);
            Assert(w.MapEditor.Save() == null && (string)w.Saved["sourceMap"]["mapId"] == named, "Save keeps the scene target map " + named);
            var reread = new World(w.Store.Patch, w.Store.Info, maps: maps);
            Assert(reread.Session.Blocked == null && (string)reread.ScenePatchFile["sourceMap"]["mapId"] == named && reread.Box.X == 30, "The saved scene patch file reads back with the scene target map " + named);
        }
        // A Save round trip of a scene patch file whose scene target map is a layer id: read, edited, saved and read back, the
        // saved text is the text that was read.
        var written = new World(Doc(Mod("move-p", "p", Fixed(112))).Replace("\"mapId\":\"map\"", "\"mapId\":\"rr/bushnell-whittier\""), maps: OneLayer);
        written.Session.Edit(P, PlacementAt(130), null); written.MapEditor.Save();
        var before = written.Store.Patch;
        var layered = new World(before, written.Store.Info, maps: OneLayer);
        Assert(layered.Session.Blocked == null && layered.Session.Edit(P, PlacementAt(150), null).Applied && layered.MapEditor.Save() == null && (string)layered.Saved["sourceMap"]["mapId"] == "rr/bushnell-whittier", "A scene patch file naming a layer is edited and saved with that scene target map");
        Assert(layered.Session.Edit(P, PlacementAt(130), null).Applied && layered.MapEditor.Save() == null && layered.Store.Patch == before, "Edited back and saved, the scene patch file naming a layer is the text that was read: " + layered.Store.Patch);
        var readBack = new World(layered.Store.Patch, layered.Store.Info, maps: OneLayer);
        Assert(readBack.Session.Blocked == null && readBack.Session.ScenePatchFileText == before && readBack.Box.X == 30, "The scene patch file naming a layer reads back unchanged");
    }
    // An edit in scene patch editing stores only what it changes: a value equal to the object's own is no change, a value equal to
    // the value without this mod takes the setting out of the modification, and a hand-written equal value stays.
    static void EqualValuesAreNotStored() {
        var w = new World();
        var enable = w.Session.Edit(P, null, true);
        Assert(enable.Applied && enable.Unchanged && !enable.Reverted && w.ScenePatchFile["modifications"].Count() == 0 && !w.MapEditor.CanUndo && !w.Session.IsChanged(P), "Enabling an enabled object stores nothing and adds no Undo step");
        Assert(w.Session.Edit(P, null, false).Applied && !w.Box.Active && w.Session.IsChanged(P), "A disable is stored");
        var groups = w.MapEditor.UndoGroups.Count;
        var again = w.Session.Edit(P, null, false);
        Assert(again.Applied && again.Unchanged && w.MapEditor.UndoGroups.Count == groups, "The same value again changes nothing and adds no Undo step");
        var back = w.Session.Edit(P, null, true);
        Assert(back.Applied && back.Reverted && !back.Unchanged && w.Box.Active && w.ScenePatchFile["modifications"].Count() == 0 && !w.Session.IsChanged(P), "Enabling it again removes the modification: the object is not changed by this mod");
        Assert(w.MapEditor.UndoGroups.Count == groups + 1, "The revert is one Undo step, as setting a native value back is");
        w.MapEditor.Undo(); Assert(!w.Box.Active && w.Session.IsChanged(P), "Undo of the revert restores the disable");
        w.MapEditor.Redo(); Assert(w.Box.Active && !w.Session.IsChanged(P), "Redo reverts again");

        // A revert of one setting keeps the others.
        Assert(w.Session.Edit(P, PlacementAt(112), null).Applied && w.Session.Edit(P, null, false).Applied, "A placement and a disable");
        var kept = w.Session.Edit(P, null, true);
        var item = (JObject)w.ScenePatchFile["modifications"].First.First;
        Assert(kept.Reverted && item["active"] == null && item["placement"] != null && w.Box.Active && w.Box.X == 12, "Enabling removes the activity value and keeps the placement");
        // The underlying placement in map coordinates, as the numeric fields show it.
        var home = new Placement(new[] { 7.0002, 8, 9 }, new[] { 0.0, 1.0, 0.0, 0.0 });
        var returned = w.Session.Edit(P, home, null);
        Assert(returned.Applied && returned.Reverted && w.ScenePatchFile["modifications"].Count() == 0 && Math.Abs(w.Box.X + 93) < .001, "A placement equal to the unpatched placement within the fields' precision removes the modification: " + returned.Error + " x=" + w.Box.X);
        var moved = w.Session.Edit(P, new Placement(new[] { 7.002, 8, 9 }, new[] { 0.0, 1.0, 0.0, 0.0 }), null);
        Assert(moved.Applied && !moved.Reverted && w.ScenePatchFile["modifications"].Count() == 1, "Two millimetres away is a placement of its own");

        // The lines of a change use the same tolerance: a rotation that differs by rounding only is no change.
        var before = new ObjectState(new Placement(new[] { 1.0, 2, 3 }, new[] { 0.0, 0.7071068, 0, 0.7071068 }), true);
        string Lines(double x, double[] rotation) => String.Join(" | ", PatchEditingSession.ChangeLines(new ObjectState(new Placement(new[] { x, 2, 3 }, rotation), null), before));
        Assert(Lines(5, new[] { 0.0, 0.70710677, 0, 0.70710679 }) == "Position: from 1, 2, 3 to 5, 2, 3", "An X-only change with a rotation that drifted by rounding has no Rotation line: " + Lines(5, new[] { 0.0, 0.70710677, 0, 0.70710679 }));
        Assert(Lines(1.0003, new[] { 0.0, 1.0, 0, 0 }) == "Rotation: changed", "A position within half a millimetre has no Position line: " + Lines(1.0003, new[] { 0.0, 1.0, 0, 0 }));
        Assert(Lines(1, new[] { 0.0, 0.7071068, 0, 0.7071068 }) == "Placement: set to the placement the object has without this mod", "A stored placement equal to the object's own says so");
        Assert(PatchEditingSession.SameRotation(new[] { 0.0, 1, 0, 0 }, new[] { 0.0, -1, 0, 0 }) && !PatchEditingSession.SameRotation(new[] { 0.0, 1, 0, 0 }, new[] { 0.0, 0.9999, 0, 0.01414 }), "A rotation and its negated quaternion are the same; under a degree apart is not");
        Assert(PatchEditingSession.SamePosition(new[] { 1.0, 2, 3 }, new[] { 1.0005, 2, 3 }) && !PatchEditingSession.SamePosition(new[] { 1.0, 2, 3 }, new[] { 1.0006, 2, 3 }) && !PatchEditingSession.SamePosition(new[] { 1.0 }, null), "A position is the same within half a millimetre");
        // A scene patch file written by hand may hold a value equal to the object's own; an edit of another setting keeps it.
        var hand = new World(Doc(Mod("m", "p", "\"active\":true")));
        Assert(hand.Session.IsChanged(P) && hand.Box.Active, "A hand-written equal value is legal and counts as a modification");
        Assert(hand.Session.Edit(P, PlacementAt(112), null).Applied && (bool)hand.ScenePatchFile["modifications"].First.First["active"], "An edit of the placement keeps the hand-written activity value");
        var same = hand.Session.Edit(P, null, true);
        Assert(same.Applied && same.Unchanged && (bool)hand.ScenePatchFile["modifications"].First.First["active"], "Setting the hand-written value again changes nothing");
    }
    static string[] Required(World w) => JObject.Parse(w.Store.Info)["dependencies"]["required"].Select(x => (string)x).ToArray();
    static void ActivityFollowsTheSameRoute() {
        var w = new World();
        Assert(w.Session.Edit(P, null, false).Applied && !w.Box.Active && w.Box.X == -93, "An explicit disable takes effect without moving the object");
        var item = (JObject)w.ScenePatchFile["modifications"].First.First;
        Assert(item["active"].Type == JTokenType.Boolean && !(bool)item["active"] && item["placement"] == null, "The scene patch file represents the activity change alone");
        var replay = new Scene(); var fresh = new Box(); replay.Objects["p"] = new List<object> { fresh };
        var engine = new PatchEngine(replay);
        Assert(engine.Apply(w.Session.ScenePatchFileText).Outcomes.Values.Single().Applied && !fresh.Active, "The runtime engine applies the explicit disable");
        fresh.Active = true;
        Assert(engine.Apply(w.Session.ScenePatchFileText).Outcomes.Values.Single().Applied && !fresh.Active, "Repeated application keeps the explicit disable after a later activation");
        w.MapEditor.Undo(); Assert(w.Box.Active && w.ScenePatchFile["modifications"].Count() == 0, "Native Undo reverses the activity change");
        w.MapEditor.Redo(); Assert(!w.Box.Active, "Native Redo reapplies the activity change");
        Assert(w.Session.Edit(P, PlacementAt(112), null).Applied && !w.Box.Active && w.Box.X == 12 && w.ScenePatchFile["modifications"].Count() == 1, "Placement and activity group in one desired state per object");
        Assert(w.MapEditor.Save() == null && !w.MapEditor.IsDirty && !(bool)w.Saved["modifications"].First.First["active"], "The activity change persists through the same Save");
        // The target exists and is off, so only the activity check can keep a value that reads as true from applying.
        foreach (var value in new[] { "\"no\"", "1" }) {
            var target = new Scene(); var off = new Box { Active = false }; target.Objects["p"] = new List<object> { off };
            var rejected = new PatchEngine(target).Apply(Doc(Mod("a", "p", "\"active\":" + value))).Outcomes["a"];
            Assert(!rejected.Applied && rejected.Error == "Invalid activity" && rejected.Cause == FailureCause.InvalidDefinition && !off.Active && target.MutationCalls == 0, "A non-boolean activity value " + value + " is rejected before the object changes");
        }
    }
    static void UndoAndRedoAreOneCoherentChange() {
        var w = new World();
        w.Session.Edit(P, PlacementAt(112), false);
        Assert(w.MapEditor.UndoGroups.Count == 1 && w.MapEditor.IsDirty, "One completed edit is one native history group");
        w.MapEditor.Undo();
        Assert(w.AtUnderlying && w.ScenePatchFile["modifications"].Count() == 0 && !w.MapEditor.IsDirty, "Undo reverses world, scene patch file and dirty state together");
        w.MapEditor.Redo();
        Assert(w.Box.X == 12 && !w.Box.Active && w.ScenePatchFile["modifications"].Count() == 1 && w.MapEditor.IsDirty, "Redo reapplies the complete edit");
        w.Session.Edit(P, PlacementAt(140), null);
        w.MapEditor.Undo();
        Assert(w.Box.X == 12 && (double)w.ScenePatchFile["modifications"].First.First["placement"]["position"][0] == 112, "Undo of a second edit returns to the first desired state, not to the underlying state");
        w.Scene.Origin = 110; w.MapEditor.Redo(); Assert(w.Box.X == 30, "Redo applies the map placement through a changed origin"); w.Scene.Origin = 100;
    }
    static void MixedHistoryKeepsSavedIdentity() {
        var w = new World();
        w.MapEditor.NativeEdit(1); w.Session.Edit(P, PlacementAt(112), null); w.MapEditor.NativeEdit(2);
        Assert(w.MapEditor.Save() == null && !w.MapEditor.IsDirty, "Save of mixed native and patcher edits leaves a clean native map editor");
        w.MapEditor.Undo(); w.MapEditor.Undo();
        Assert(w.AtUnderlying && w.MapEditor.NativeValue == 1 && w.MapEditor.IsDirty, "Undo after Save crosses native and patcher edits and reports outstanding work");
        w.MapEditor.Redo(); w.MapEditor.Redo();
        Assert(w.Box.X == 12 && w.MapEditor.NativeValue == 2 && !w.MapEditor.IsDirty, "Redo back to the saved state is clean again");
        w.MapEditor.Undo(); w.MapEditor.NativeEdit(7);
        Assert(!w.MapEditor.NativeDirty && w.MapEditor.IsDirty && !w.MapEditor.CanRedo, "A branched history with the saved change count stays dirty, so Save stays available");
        Assert(w.Session.Outstanding(w.MapEditor.NativeDirty).SequenceEqual(new[] { PatchEditingSession.NativeData }), "The divergent native state is the outstanding participant");
        w.MapEditor.Undo(); w.Session.Edit(P, PlacementAt(150), null);
        Assert(!w.MapEditor.NativeDirty && w.MapEditor.IsDirty && w.Session.Outstanding(false).Contains(PatchEditingSession.PatchData), "A different patcher edit at the saved change count is outstanding patch work");
        Assert(w.MapEditor.Save() == null && !w.MapEditor.IsDirty && (double)w.Saved["modifications"].First.First["placement"]["position"][0] == 150, "Saving the branch records its own saved identity");
    }
    static void RemovalRecomputesUnderlyingState() {
        var w = new World(Doc(Mod("move-p", "p", Fixed(112)) + "," + Mod("move-q", "q", Fixed(160)), "\"future\":{\"keep\":true},"));
        Assert(w.Box.X == 12 && w.Other.X == 60 && !w.MapEditor.IsDirty, "The saved scene patch file is applied and clean at load");
        w.Session.Edit(Q, PlacementAt(170), null); w.Session.Edit(P, PlacementAt(130), null);
        Assert(w.Session.Remove(P).Applied && w.AtUnderlying, "Removal returns the object to the state the native context gives it, not to an earlier patched snapshot");
        Assert(w.Other.X == 70 && w.ScenePatchFile["modifications"].Count() == 1 && w.ScenePatchFile["modifications"]["move-q"] != null && (bool)w.ScenePatchFile["future"]["keep"], "Removal keeps this mod's remaining work and unknown content");
        w.MapEditor.Undo(); Assert(w.Box.X == 30 && w.ScenePatchFile["modifications"].Count() == 2 && (double)w.ScenePatchFile["modifications"]["move-p"]["placement"]["position"][0] == 130, "Undo of a removal restores the modification under its key");
        w.MapEditor.Redo(); Assert(w.AtUnderlying && w.MapEditor.Save() == null && w.Saved["modifications"].Count() == 1, "A saved removal leaves only the remaining modification on disk");
        Assert(!w.Session.Remove(P).Applied, "An object without a modification has nothing to remove");
        var two = new World(Doc(Mod("pose", "p", Fixed(112)) + "," + Mod("off", "p", "\"active\":false")));
        Assert(two.Box.X == 12 && !two.Box.Active && !two.Session.Remove(P).Applied && two.Box.X == 12, "Several manual modifications of one object are left for manual combination");
    }
    static void SaveWritesEveryParticipant() {
        var w = new World();
        w.MapEditor.NativeEdit(1); w.Session.Edit(P, PlacementAt(112), null);
        Assert(w.Session.Outstanding(w.MapEditor.NativeDirty).SequenceEqual(new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata }), "This mod's modification needs native data, scene patch file and dependency metadata");
        Assert(w.Session.ExitWarning(w.MapEditor.NativeDirty) == "Changes to the map data, scene patch file, and patcher dependency are still unsaved.", "The outstanding-work warning names all three writes.");
        Assert(w.Session.ExitPrompt(w.MapEditor.NativeDirty) == "Exit the Map Editor? Exit discards unsaved changes to the map data, scene patch file, and patcher dependency.",
            "The Exit prompt names all three outstanding writes in the discard sentence.");
        Assert(w.MapEditor.Save() == null && w.MapEditor.NativeWrites == 1 && w.Store.PatchWrites == 1 && w.Store.InfoWrites == 1, "One Save writes all three participants");
        Assert(Required(w).SequenceEqual(new[] { "rr/game >=0.6.2", "crankyant/scene-patcher >=0.1.0" }) && (string)JObject.Parse(w.Store.Info)["version"] == "1.0.0", "The dependency is added without disturbing other metadata");
        Assert(w.Session.LastSave.Outstanding.Count == 0 && w.Session.LastSave.Completed.Count == 3 && !w.MapEditor.IsDirty && w.Box.X == 12, "A complete Save reports clean and leaves the world as edited");
        w.MapEditor.NativeEdit(2);
        Assert(w.MapEditor.Save() == null && w.Store.PatchWrites == 1 && w.Store.InfoWrites == 1 && w.MapEditor.NativeWrites == 2, "Unchanged patcher participants are not rewritten");
        var empty = new World(); empty.MapEditor.NativeEdit(1);
        Assert(empty.MapEditor.Save() == null && empty.Store.Patch == null && empty.Store.InfoWrites == 0, "A mod without modifications gets no scene patch file or dependency");
    }
    static void PartialWritesStayIdentifiedAndRetryable() {
        var w = new World();
        w.Session.Edit(P, PlacementAt(112), null);
        w.Store.FailInfo = true; var message = w.MapEditor.Save();
        Assert(message.Contains("metadata write injected") && message.Contains("completed writes: none") && message.EndsWith(Tail + "map data, scene patch file, patcher dependency. Edits remain in memory. Correct the cause and Save again."), "A metadata failure names every outstanding participant: " + message);
        Assert(w.MapEditor.IsDirty && w.Box.X == 12 && w.ScenePatchFile["modifications"].Count() == 1 && w.Store.Patch == null && w.MapEditor.NativeWrites == 0, "The failed Save keeps the edit in memory and writes nothing further");
        w.Store.FailInfo = false; w.Store.FailPatch = true; message = w.MapEditor.Save();
        Assert(message.Contains("patch write injected") && message.Contains("completed writes: patcher dependency" + Tail + "map data, scene patch file."), "A patch failure identifies the completed metadata write: " + message);
        Assert(w.MapEditor.IsDirty && w.Session.LastSave.Completed.SequenceEqual(new[] { PatchEditingSession.Metadata }) && w.Store.InfoWrites == 1, "Completed writes are not reversed and the native map editor stays dirty");
        w.Store.FailPatch = false; w.MapEditor.FailNative = true; message = w.MapEditor.Save();
        Assert(message.Contains("native write injected") && message.Contains("completed writes: scene patch file" + Tail + "map data."), "A native failure after the patch write stays identified: " + message);
        Assert(w.MapEditor.IsDirty && w.Store.PatchWrites == 1 && w.Box.X == 12, "A partial Save never reports clean");
        w.MapEditor.FailNative = false;
        Assert(w.MapEditor.Save() == null && !w.MapEditor.IsDirty && w.Store.PatchWrites == 1 && w.Store.InfoWrites == 1 && w.MapEditor.NativeWrites == 1, "The retry writes only the outstanding participant");
        w.Session.Edit(P, PlacementAt(120), null); var external = w.Store.Patch.Replace("112", "999"); w.Store.Patch = external;
        message = w.MapEditor.Save();
        Assert(w.Session.VersionState == FileVersionState.Changed && message.Contains("The scene patch file changed outside scene patch editing") && w.Store.Patch == external && w.MapEditor.IsDirty, "The retry is guarded: an externally changed scene patch file is not overwritten");
        var first = new World(Doc(Mod("move-p", "p", Fixed(112))));
        first.MapEditor.NativeEdit(1); first.MapEditor.FailNative = true; first.MapEditor.Save(); first.MapEditor.FailNative = false;
        first.MapEditor.Undo(); first.MapEditor.NativeEdit(2);
        Assert(first.MapEditor.IsDirty, "A failed Save records no saved identity for a later equal change count");
    }
    // A mod folder on disk with the given files; null leaves a file absent.
    static string Folder(string patch, string info) {
        var root = Path.Combine(Path.GetTempPath(), "RailroaderScenePatcherTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        if (patch != null) File.WriteAllText(Path.Combine(root, "Data", "scene-patches.json"), patch);
        if (info != null) File.WriteAllText(Path.Combine(root, "Info.json"), info);
        return root;
    }
    static string Listing(string root) => String.Join(",", Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(f => f.Substring(root.Length + 1).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal));
    static void InterruptedFileWritesKeepThePreviousFile() {
        // An older dependency makes Save raise it, so Info.json is written; a satisfied one leaves Info.json unwritten.
        string Info(string minimum) => new Store().Info.Replace("\"rr/game >=0.6.2\"", "\"rr/game >=0.6.2\",\"crankyant/scene-patcher >=" + minimum + "\"");
        var saved = Doc(Mod("move-p", "p", Fixed(112)));
        // Each participant fails while its new content is written, and again while the new content replaces the file,
        // with an existing file and, for the scene patch file, at the first Save without one.
        var cases = new[] {
            new { Name = PatchEditingSession.PatchData, Patch = saved, Fault = "scene-patches.json", Replace = false },
            new { Name = PatchEditingSession.PatchData, Patch = saved, Fault = "scene-patches.json", Replace = true },
            new { Name = PatchEditingSession.PatchData, Patch = (string)null, Fault = "scene-patches.json", Replace = false },
            new { Name = PatchEditingSession.PatchData, Patch = (string)null, Fault = "scene-patches.json", Replace = true },
            new { Name = PatchEditingSession.Metadata, Patch = saved, Fault = "Info.json", Replace = false },
            new { Name = PatchEditingSession.Metadata, Patch = saved, Fault = "Info.json", Replace = true },
        };
        foreach (var c in cases) {
            var label = c.Name + (c.Replace ? " replacement" : " write") + (c.Patch == null ? " at the first Save" : "");
            var metadata = c.Name == PatchEditingSession.Metadata;
            var info = Info(metadata ? "0.0.1" : "0.5.0");
            var root = Folder(c.Patch, info);
            try {
                var files = new FaultyFiles(); var announced = new List<string>();
                var storage = new ModStorage(root, files, announced.Add);
                var w = new World(c.Patch, info, storage);
                var before = Listing(root);
                var patchBytes = c.Patch == null ? null : File.ReadAllBytes(storage.PatchPath);
                var infoBytes = File.ReadAllBytes(storage.InfoPath);
                w.Session.Edit(P, PlacementAt(130), null);
                if (c.Replace) files.FailReplace = c.Fault; else files.FailWrite = c.Fault;
                var message = w.MapEditor.Save();
                Assert(message != null && message.Contains(c.Replace ? "replace failed" : "write interrupted midway") && w.Session.LastSave.Outstanding.Contains(c.Name), label + ": the failure names the participant as outstanding: " + message);
                Assert(c.Patch == null ? !File.Exists(storage.PatchPath) : File.ReadAllBytes(storage.PatchPath).SequenceEqual(patchBytes), label + ": the previous scene patch file is intact");
                Assert(File.ReadAllBytes(storage.InfoPath).SequenceEqual(infoBytes), label + ": the previous Info.json is intact");
                Assert(Listing(root) == before, label + ": no partial file is left beside the mod's files: " + Listing(root));
                Assert(announced.Count == 0, label + ": a failed write is not announced as written");
                Assert(w.MapEditor.IsDirty && w.Box.X == 30 && (double)w.ScenePatchFile["modifications"].First.First["placement"]["position"][0] == 130, label + ": the edit stays in memory and the native map editor stays unsaved");
                files.FailWrite = files.FailReplace = null;
                Assert(w.MapEditor.Save() == null && !w.MapEditor.IsDirty, label + ": the guarded retry succeeds");
                var written = JObject.Parse(File.ReadAllText(storage.PatchPath));
                Assert((double)written["modifications"].First.First["placement"]["position"][0] == 130 && (metadata ? JObject.Parse(File.ReadAllText(storage.InfoPath))["dependencies"]["required"].Any(d => (string)d == "crankyant/scene-patcher >=0.1.0") : File.ReadAllBytes(storage.InfoPath).SequenceEqual(infoBytes)), label + ": the retry writes both files completely");
                Assert(announced.Count == 1 && announced[0] == File.ReadAllText(storage.PatchPath), label + ": the completed patch write is announced once with the text on disk");
                Assert(Listing(root) == "Data/scene-patches.json,Info.json", label + ": the retry leaves only the mod's own files: " + Listing(root));
            } finally { Directory.Delete(root, true); }
        }
        // Content left behind by an earlier interrupted Save is never published with the mod.
        var stale = Folder(saved, Info("0.0.1"));
        try {
            var storage = new ModStorage(stale);
            foreach (var leftover in new[] { storage.PatchPath, storage.InfoPath }) File.WriteAllText(leftover + ".saving", "{\"partial");
            var w = new World(saved, Info("0.0.1"), storage);
            w.Session.Edit(P, PlacementAt(140), null);
            Assert(w.MapEditor.Save() == null && Listing(stale) == "Data/scene-patches.json,Info.json" && (double)JObject.Parse(File.ReadAllText(storage.PatchPath))["modifications"].First.First["placement"]["position"][0] == 140, "A Save replaces leftovers of an interrupted Save and leaves only the mod's own files: " + Listing(stale));
        } finally { Directory.Delete(stale, true); }
    }
    // Records each pause in the file log and waits it out, so a real hold of a file during the test passes as it would in the native map editor.
    static Action<int> Waited(List<string> log) => milliseconds => { log.Add("pause " + milliseconds); System.Threading.Thread.Sleep(milliseconds); };
    // The attempts to put one file in place and the pauses between them. One file's attempts follow each other with only
    // their own pauses between them, so the other file's attempts, which the real disk can also delay, stay apart.
    static int[] Pauses(List<string> log, string file, out int attempts) {
        var first = log.IndexOf("place " + file); var last = log.LastIndexOf("place " + file);
        var span = first < 0 ? new List<string>() : log.GetRange(first, last - first + 1);
        attempts = span.Count(e => e == "place " + file);
        Assert(span.Count == 2 * attempts - 1 && span.Where((e, i) => i % 2 == 0).All(e => e == "place " + file), "Attempts for " + file + " alternate with their pauses: " + String.Join(", ", log));
        return span.Where(e => e.StartsWith("pause ", StringComparison.Ordinal)).Select(e => int.Parse(e.Substring(6))).ToArray();
    }
    static void ReplacementIsRetriedOnlyWhileTheFileIsHeld() {
        var saved = Doc(Mod("move-p", "p", Fixed(112)));
        var info = new Store().Info.Replace("\"rr/game >=0.6.2\"", "\"rr/game >=0.6.2\",\"crankyant/scene-patcher >=0.0.1\"");
        foreach (var target in new[] { "scene-patches.json", "Info.json" }) {
            // A file held open for a moment: the replacement fails twice, then succeeds within the same Save.
            var root = Folder(saved, info);
            try {
                var files = new FaultyFiles { FailReplace = target, ReplaceFailures = 2 };
                var storage = new ModStorage(root, files, null, Waited(files.Log));
                var w = new World(saved, info, storage);
                w.Session.Edit(P, PlacementAt(130), null);
                Assert(w.MapEditor.Save() == null && !w.MapEditor.IsDirty, target + ": a replacement that fails for a moment does not fail the Save");
                // The real disk can hold the file once more after the injected failures; each further attempt still follows a pause.
                var pauses = Pauses(files.Log, target, out var attempts);
                Assert(attempts >= 3 && pauses.Take(2).SequenceEqual(new[] { 50, 100 }), target + ": the replacement is tried again after a short pause until it succeeds");
                Assert((double)JObject.Parse(File.ReadAllText(storage.PatchPath))["modifications"].First.First["placement"]["position"][0] == 130 && JObject.Parse(File.ReadAllText(storage.InfoPath))["dependencies"]["required"].Any(d => (string)d == "crankyant/scene-patcher >=0.1.0"), target + ": both files hold their new content");
                Assert(Listing(root) == "Data/scene-patches.json,Info.json", target + ": no pending file is left: " + Listing(root));
            } finally { Directory.Delete(root, true); }
            // A file that stays held: the replacement is tried a bounded number of times, then the Save fails cleanly.
            root = Folder(saved, info);
            try {
                var files = new FaultyFiles { FailReplace = target };
                var storage = new ModStorage(root, files, null, Waited(files.Log));
                var w = new World(saved, info, storage);
                var patchBytes = File.ReadAllBytes(storage.PatchPath); var infoBytes = File.ReadAllBytes(storage.InfoPath);
                w.Session.Edit(P, PlacementAt(130), null);
                var message = w.MapEditor.Save();
                var participant = target == "Info.json" ? PatchEditingSession.Metadata : PatchEditingSession.PatchData;
                Assert(message != null && message.Contains("replace failed") && w.Session.LastSave.Outstanding.Contains(participant), target + ": a file that stays held fails the Save and names the participant: " + message);
                var held = Pauses(files.Log, target, out var tries);
                Assert(tries == ModStorage.ReplaceAttempts && held.SequenceEqual(new[] { 50, 100, 150, 200 }), target + ": the replacement is tried a bounded number of times");
                // Info.json is written before the scene patch file, so a held scene patch file leaves the completed metadata write.
                Assert(File.ReadAllBytes(storage.PatchPath).SequenceEqual(patchBytes) && (target == "scene-patches.json" || File.ReadAllBytes(storage.InfoPath).SequenceEqual(infoBytes)) && Listing(root) == "Data/scene-patches.json,Info.json", target + ": the held file keeps its previous content and no pending file is left");
                Assert(w.MapEditor.IsDirty && w.Box.X == 30 && (double)w.ScenePatchFile["modifications"].First.First["placement"]["position"][0] == 130, target + ": the edit stays in memory and the native map editor stays unsaved");
                files.FailReplace = null;
                Assert(w.MapEditor.Save() == null && !w.MapEditor.IsDirty, target + ": the retry after the file is released succeeds");
            } finally { Directory.Delete(root, true); }
        }
        // A refused replacement is no passing hold: it fails at once, without further attempts. The refusal is injected,
        // so the rule holds on every platform the tests run on.
        var refused = Folder(saved, info);
        try {
            var files = new FaultyFiles { FailReplace = "scene-patches.json", RefuseReplace = true };
            var storage = new ModStorage(refused, files, null, Waited(files.Log));
            var w = new World(saved, info, storage);
            w.Session.Edit(P, PlacementAt(130), null);
            var message = w.MapEditor.Save();
            Assert(message != null && Pauses(files.Log, "scene-patches.json", out var once).Length == 0 && once == 1 && w.MapEditor.IsDirty && Listing(refused) == "Data/scene-patches.json,Info.json", "A refused replacement of the scene patch file fails the Save at once and leaves no pending file: " + message);
            files.FailReplace = null;
            Assert(w.MapEditor.Save() == null && !w.MapEditor.IsDirty, "The retry after the refusal ends succeeds");
        } finally { Directory.Delete(refused, true); }
        // On Windows a read-only file refuses its replacement. Elsewhere the attribute only removes write permission from
        // the file, which a rename-based replacement does not need, so the check runs only where the refusal is real.
        if (!OperatingSystem.IsWindows()) return;
        // A read-only file is no passing hold: it fails at once, without further attempts.
        var locked = Folder(saved, info);
        try {
            var files = new FaultyFiles();
            var storage = new ModStorage(locked, files, null, Waited(files.Log));
            File.SetAttributes(storage.PatchPath, FileAttributes.ReadOnly);
            var w = new World(saved, info, storage);
            w.Session.Edit(P, PlacementAt(130), null);
            var message = w.MapEditor.Save();
            Assert(message != null && Pauses(files.Log, "scene-patches.json", out var once).Length == 0 && once == 1 && w.MapEditor.IsDirty && Listing(locked) == "Data/scene-patches.json,Info.json", "A read-only scene patch file fails the Save at once and leaves no pending file: " + message);
            File.SetAttributes(storage.PatchPath, FileAttributes.Normal);
            Assert(w.MapEditor.Save() == null && !w.MapEditor.IsDirty, "Clearing read-only lets the retry succeed");
        } finally { File.SetAttributes(Path.Combine(locked, "Data", "scene-patches.json"), FileAttributes.Normal); Directory.Delete(locked, true); }
    }
    static void DependencyMinimumRisesOnlyWhenRequired() {
        var legacy = new World();
        Assert(legacy.Session.MinimumPatcherVersion == "0.1.0", "A scene patch file without a newer capability keeps the fixed-placement baseline");
        legacy.Session.Edit(P, PlacementAt(112), null);
        Assert(legacy.Session.MinimumPatcherVersion == "0.1.0", "Runtime reporting does not raise a legacy placement scene patch file's minimum");
        legacy.Session.Edit(P, null, false);
        Assert(legacy.Session.MinimumPatcherVersion == "0.2.0", "Generic activity requires its own minimum rather than the newer capability version");
        Func<string, string> info = entry => "{\"version\":\"1.0.0\",\"build\":0.10,\"dependencies\":{\"required\":[" + entry + "]}}";
        var w = new World(null, info("\"crankyant/scene-patcher >=0.5.0\""));
        w.Session.Edit(P, PlacementAt(112), null);
        Assert(w.MapEditor.Save() == null && w.Store.InfoWrites == 0, "A satisfied minimum is not rewritten");
        w.Session.Edit(P, null, false);
        Assert(w.Session.MinimumPatcherVersion == "0.2.0" && !w.Session.Outstanding(false).Contains(PatchEditingSession.Metadata), "Generic activity retains its minimum while a higher existing constraint is preserved");
        Assert(w.MapEditor.Save() == null && Required(w).SequenceEqual(new[] { "crankyant/scene-patcher >=0.5.0" }) && w.Store.Info.Contains("0.10"), "Save preserves the satisfied delivery minimum and other metadata exactly");
        w.MapEditor.Undo();
        Assert(w.MapEditor.Save() == null && Required(w).Single() == "crankyant/scene-patcher >=0.5.0" && w.Store.InfoWrites == 0, "A satisfied delivery minimum is not rewritten after Undo");
        w = new World(null, info("\"crankyant/scene-patcher >= 0.5.0 <2.0.0\""));
        w.Session.Edit(P, PlacementAt(112), false);
        Assert(w.MapEditor.Save() == null && w.Store.InfoWrites == 0, "A stricter existing constraint is preserved");
        w = new World(null, info("\"crankyant/scene-patcher >=0.1.0 <1.0.0\""));
        w.Session.Edit(P, null, false);
        Assert(w.MapEditor.Save() == null && Required(w).Single() == "crankyant/scene-patcher >=0.2.0 <1.0.0", "An upper bound survives the raised minimum");
        w = new World(null, info("\"crankyant/scene-patcher ^0.1.0\""));
        w.Session.Edit(P, null, false); var message = w.MapEditor.Save();
        Assert(message.Contains("excludes the required minimum 0.2.0") && w.Store.InfoWrites == 0 && w.Store.Patch == null && w.MapEditor.IsDirty, "A constraint that excludes the required version blocks the write instead of being removed: " + message);
        w = new World(null, info("\"crankyant/scene-patcher\""));
        w.Session.Edit(P, PlacementAt(112), null);
        Assert(w.MapEditor.Save() == null && Required(w).Single() == "crankyant/scene-patcher >=0.1.0", "An unconstrained dependency receives the minimum without an upper bound");
        w = new World(null, info("\"crankyant/scene-patcher >=0.1.0\""));
        w.Session.Edit(P, PlacementAt(112), null);
        Assert(w.Session.MinimumPatcherVersion == "0.1.0" && !w.Session.Outstanding(false).Contains(PatchEditingSession.Metadata), "Fixed placement alone does not acquire a newer capability requirement");
        Assert(w.MapEditor.Save() == null && Required(w).Single() == "crankyant/scene-patcher >=0.1.0" && w.Store.InfoWrites == 0, "Save keeps a sufficient legacy dependency unchanged");
    }
    static void PruningAndHistoryErrorsStayTruthful() {
        var w = new World(); w.MapEditor.MaxLevels = 2;
        w.MapEditor.NativeEdit(1); w.Session.Edit(P, PlacementAt(112), null);
        Assert(w.MapEditor.Save() == null && !w.MapEditor.IsDirty, "Saved at the top of a full history");
        w.MapEditor.NativeEdit(2); w.MapEditor.NativeEdit(3);
        Assert(w.MapEditor.UndoGroups.Count == 2, "The oldest groups were pruned");
        w.MapEditor.Undo(); w.MapEditor.Undo();
        Assert(!w.MapEditor.CanUndo && !w.Session.IsDirty(false) && w.Box.X == 12, "Returning to the saved state below a pruned history is recognized as that state");
        var lost = new World(); lost.MapEditor.MaxLevels = 2;
        lost.MapEditor.NativeEdit(1); lost.MapEditor.NativeEdit(2); lost.MapEditor.NativeEdit(3); lost.MapEditor.Undo(); lost.MapEditor.Undo();
        Assert(!lost.MapEditor.CanUndo && lost.Session.IsDirty(false), "An emptied history whose pruned edit cannot be undone is not the saved initial state");
        var error = new World();
        error.MapEditor.NativeEdit(1); error.MapEditor.Save(); error.MapEditor.NativeEdit(2);
        error.MapEditor.ThrowInNativeAction = true; error.MapEditor.Undo(); error.MapEditor.ThrowInNativeAction = false;
        Assert(error.Session.IncompleteOperation.Contains("native action injected") && error.Session.IsDirty(false), "An exception inside native Undo is identified and never reads as clean");
        var writes = error.MapEditor.NativeWrites;
        Assert(error.MapEditor.Save() == null && error.MapEditor.NativeWrites == writes + 1, "Save still writes the current state after a native history error");
        Assert(!error.MapEditor.NativeDirty && error.MapEditor.IsDirty && error.Session.Outstanding(false).SequenceEqual(new[] { PatchEditingSession.NativeData }), "Save does not report clean while the native history is unrecovered");
        Assert(error.Session.IncompleteOperation.Contains("native action injected") && error.Session.LastSave.Outstanding.SequenceEqual(new[] { PatchEditingSession.NativeData }), "The incomplete operation stays identified through Save, in the diagnosis and the Save report");
        var count = error.MapEditor.ChangeCount; error.MapEditor.NativeEdit(5);
        Assert(error.MapEditor.NativeValue == 5 && error.MapEditor.ChangeCount == count && !error.MapEditor.NativeDirty && error.MapEditor.UndoGroups.Count == 1, "The open partial group swallows a later native edit: neither the change count nor the undo stack records it");
        Assert(error.MapEditor.IsDirty && error.Session.ExitWarning(error.MapEditor.NativeDirty).Contains("map data") && error.Session.ExitWarning(false).Contains("native action injected"), "The untracked edit after Save is still reported as unsaved, and exit names the unrecovered history");
        Assert(error.MapEditor.Save() == null && error.MapEditor.IsDirty, "A repeated Save cannot establish a clean state either");
        var own = new World();
        own.Session.Edit(P, PlacementAt(112), null); own.MapEditor.Save();
        own.Session.Edit(P, PlacementAt(130), null);
        own.Scene.FailApply = true; own.MapEditor.Undo(); own.Scene.FailApply = false;
        Assert(own.Session.IncompleteOperation.Contains("couldn't be reversed") && (double)own.ScenePatchFile["modifications"].First.First["placement"]["position"][0] == 130 && own.MapEditor.IsDirty, "A patcher edit that cannot be reversed keeps its scene patch file state and reports the incomplete operation");
        Assert(own.MapEditor.CanRedo && own.MapEditor.UndoGroups.Count == 1, "The failure leaves the native history grouping intact");
        own.MapEditor.Redo();
        Assert((double)own.ScenePatchFile["modifications"].First.First["placement"]["position"][0] == 130 && own.MapEditor.IsDirty, "History execution after the failure stays consistent with the scene patch file");
        Assert(own.MapEditor.Save() == null && !own.MapEditor.IsDirty && own.Session.IncompleteOperation == null && (double)own.Saved["modifications"].First.First["placement"]["position"][0] == 130, "A contained patcher failure leaves the native grouping intact, so a complete Save of the shown state is clean");
    }
    static void FailedEditsRestoreAndGuardRetry() {
        var w = new World();
        w.Scene.FailNextApplies = 1;
        var outcome = w.Session.Edit(P, PlacementAt(112), null);
        Assert(!outcome.Applied && outcome.Error.Contains("The object is back as it was") && w.AtUnderlying && w.ScenePatchFile["modifications"].Count() == 0 && w.MapEditor.UndoGroups.Count == 0 && !w.MapEditor.IsDirty, "A failed edit restores world and scene patch file and leaves no history or dirty state");
        Assert(w.Session.Edit(P, PlacementAt(112), null).Applied, "A verified restoration permits a new edit");
        w.Scene.FailApply = true; outcome = w.Session.Edit(P, PlacementAt(140), null); w.Scene.FailApply = false;
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified") && (double)w.ScenePatchFile["modifications"].First.First["placement"]["position"][0] == 112, "Unverified cleanup is reported and the scene patch file keeps the prior desired state");
        Assert(!w.Session.Edit(P, PlacementAt(150), null).Applied && !w.Session.Remove(P).Applied, "Further mutation of that object is blocked until it is rechecked");
        Assert(w.Session.Edit(Q, PlacementAt(160), null).Applied && w.Other.X == 60, "Independent objects remain editable");
        Assert(w.Session.Recheck(P).Applied && w.Box.X == 12 && w.Session.Edit(P, PlacementAt(150), null).Applied && w.Box.X == 50, "The guarded retry reconciles the actual state with the scene patch file before unblocking");
        Assert(!w.Session.Edit(new TargetIdentity("s", "missing", "c"), PlacementAt(1), null).Applied, "A missing target is refused before mutation");
        w.Scene.Objects["twins"] = new List<object> { new Box(), new Box() };
        Assert(!w.Session.Edit(new TargetIdentity("s", "twins", "c"), PlacementAt(1), null).Applied, "An ambiguous target is refused before mutation");
        Assert(!w.Session.Edit(P, new Placement(new[] { 1.0, 2.0, Double.NaN }, new[] { 0.0, 0.0, 0.0, 1.0 }), null).Applied && !w.Session.Edit(P, new Placement(new[] { 1.0, 2.0, 3.0 }, new[] { 0.0, 0.0, 0.0, 0.0 }), null).Applied && w.Box.X == 50, "Invalid numeric input is refused before mutation");
    }
    static void UnknownContentAndProtectedScenePatchFilesSurvive() {
        var future = "\"later\":{\"target\":{\"scene\":\"s\",\"path\":\"q\",\"component\":\"c\"},\"futureOperation\":{\"ratio\":1.50},\"dependsOn\":[]}";
        var w = new World(Doc(future + "," + Mod("move-p", "p", Fixed(112)), "\"futureNumber\":0.123456789012345678901234567890123456789,\"stamp\":\"2020-01-01T00:00:00Z\","));
        Assert(w.Other.X == -50 && w.Session.Blocked == null && !w.MapEditor.IsDirty, "Unknown content is not executed and does not make the session dirty");
        Assert(w.Session.Edit(P, PlacementAt(130), null).Applied && w.MapEditor.Save() == null, "A known modification beside unknown content can be edited and saved");
        Assert(w.Store.Patch.Contains("0.123456789012345678901234567890123456789") && w.Store.Patch.Contains("2020-01-01T00:00:00Z") && w.Store.Patch.Contains("1.50"), "Unknown numbers and strings keep their exact content through edit and Save");
        Assert(JToken.DeepEquals(w.Saved["modifications"]["later"], JObject.Parse("{" + future + "}")["later"]) && (double)w.Saved["modifications"]["move-p"]["placement"]["position"][0] == 130, "The unknown modification is written back unchanged under its key");
        var reread = new World(w.Store.Patch);
        Assert(reread.Session.Blocked == null && !reread.MapEditor.IsDirty && reread.Box.X == 30, "The saved scene patch file reads back clean and applies");
        Assert(!w.Session.Edit(Q, PlacementAt(1), null).Applied && !w.Session.Remove(Q).Applied && w.Other.X == -50, "A modification with unknown content is neither edited nor deleted");
        foreach (var text in new[] { "{broken", Doc("").Replace("\"formatVersion\":1", "\"formatVersion\":2"), Doc(Mod("a", "p", Fixed(112))).Replace("\"mapId\":\"map\"", "\"mapId\":\"elsewhere\"") }) {
            var guarded = new World(text);
            Assert(guarded.Session.Blocked != null && !guarded.Session.Edit(P, PlacementAt(112), null).Applied && guarded.AtUnderlying, "An unreadable, newer or foreign scene patch file blocks scene patch editing");
            guarded.MapEditor.NativeEdit(1);
            Assert(guarded.MapEditor.Save() == null && guarded.Store.Patch == text && guarded.Store.PatchWrites == 0 && !guarded.MapEditor.IsDirty, "Native Save proceeds and the protected scene patch file is not overwritten");
        }
    }
    static void InspectedEditsBindToTheirObservation() {
        var w = new World();
        w.Scene.Observations = () => new[] { new SceneObservation(w.Box, P, "Board", w.Box.Active, new[] { w.Box.Position[0] + w.Scene.Origin, w.Box.Position[1], w.Box.Position[2] },
            w.Box.Rotation, new string[0], new string[0], new string[0], "Fixed placement") };
        var inventory = new SceneInventory(w.Scene); inventory.Refresh();
        var inspected = inventory.Pin(inventory.Entries.Single());
        Assert(w.Session.Edit(P, PlacementAt(112), null, inspected).Applied && w.Box.X == 12, "An edit bound to the current inspection applies");
        var mutations = w.Scene.MutationCalls;
        var outcome = w.Session.Edit(P, PlacementAt(130), null, inspected);
        Assert(!outcome.Applied && outcome.Error.Contains("refresh and inspect again") && w.Box.X == 12 && w.Scene.MutationCalls == mutations && w.MapEditor.UndoGroups.Count == 1 && (double)w.ScenePatchFile["modifications"].First.First["placement"]["position"][0] == 112,
            "An edit bound to an outdated inspection is refused before mutation, scene patch file change or history: " + outcome.Error);
        Assert(!w.Session.Remove(P, inspected).Applied && !w.Session.Recheck(P, inspected).Applied && w.Box.X == 12, "Removal and recheck are bound to the inspection in the same way");
        try { inventory.Pin(inventory.Entries.Single()); throw new Exception("A stale inventory issued a token"); } catch (InvalidOperationException) { }
        inventory.Refresh(); inspected = inventory.Pin(inventory.Entries.Single());
        Assert(w.Session.Edit(P, PlacementAt(130), null, inspected).Applied && w.Box.X == 30, "A refreshed inspection permits the next edit");
        inventory.Refresh(); inspected = inventory.Pin(inventory.Entries.Single());
        var replacement = new Box(); mutations = w.Scene.MutationCalls;
        w.Scene.BeforeRead = () => w.Scene.Objects["p"] = new List<object> { replacement };
        outcome = w.Session.Edit(P, PlacementAt(150), null, inspected);
        Assert(!outcome.Applied && outcome.Error.Contains("Target changed before mutation") && w.Box.X == 30 && replacement.X == 0 && w.Scene.MutationCalls == mutations,
            "A target replaced while its state is read is rejected without moving either object or requesting restoration: " + outcome.Error);
        Assert((double)w.ScenePatchFile["modifications"].First.First["placement"]["position"][0] == 130 && w.MapEditor.UndoGroups.Count == 2, "The rejected edit leaves scene patch file and history as they were");
        w.Scene.Objects["p"] = new List<object> { w.Box };
        Assert(w.Session.Edit(P, PlacementAt(150), null, inspected).Applied, "The pre-mutation rejection does not block the object");
        w.Scene.BeforeRead = () => w.Scene.Maps = new[] { "elsewhere" };
        outcome = w.Session.Edit(P, PlacementAt(170), null);
        Assert(!outcome.Applied && outcome.Error.Contains("Map context changed") && w.Box.X == 50, "The immediate guard also holds for an edit without an inspection token: " + outcome.Error);
        w.Scene.Maps = new[] { "map", "layer" };
        w.MapEditor.Undo();
        Assert(w.Box.X == 30 && w.Session.IncompleteOperation == null, "History replay needs no inspection token and still passes the immediate guard");
        w.Scene.BeforeRead = () => w.Scene.Objects["p"] = new List<object> { replacement };
        w.MapEditor.Undo();
        Assert(w.Box.X == 30 && replacement.X == 0 && w.Session.IncompleteOperation.Contains("Target changed before mutation") && w.MapEditor.IsDirty, "A target replaced during Undo is not redirected; the operation is reported incomplete");
    }
    static void ExitIdentifiesOutstandingWork() {
        var w = new World();
        Assert(w.Session.ExitWarning(w.MapEditor.NativeDirty) == null, "A clean native map editor has no exit warning");
        Assert(w.Session.ExitPrompt(w.MapEditor.NativeDirty) == null, "A clean Map Editor has no Exit prompt");
        w.Session.Edit(P, PlacementAt(112), null); w.MapEditor.FailNative = true; w.MapEditor.Save();
        Assert(w.Session.ExitWarning(w.MapEditor.NativeDirty) == "Changes to the map data are still unsaved.", "After a partial Save the warning names the outstanding map data");
        Assert(w.Session.ExitPrompt(w.MapEditor.NativeDirty) == "Exit the Map Editor? Exit discards unsaved changes to the map data.",
            "The Exit prompt names the one outstanding write after a partial Save.");
        w.MapEditor.FailNative = false; w.MapEditor.Save(); w.Session.Edit(P, PlacementAt(120), null);
        Assert(w.Session.ExitWarning(w.MapEditor.NativeDirty) == "Changes to the map data and scene patch file are still unsaved.", "The warning names outstanding native and patcher work");
        Assert(w.Session.ExitPrompt(w.MapEditor.NativeDirty) == "Exit the Map Editor? Exit discards unsaved changes to the map data and scene patch file.",
            "The Exit prompt joins two outstanding writes with and.");
    }
    static void NumericEditReachesWorldAndScenePatchFile() {
        var w = new World();
        Assert(w.Session.Blocked == null && !w.MapEditor.IsDirty, "A mod without a scene patch file starts clean and editable");
        var outcome = w.Session.Edit(P, PlacementAt(112), null);
        Assert(outcome.Applied && outcome.Error == null, "A numeric placement edit completes");
        Assert(w.Box.X == 12 && w.Box.Position[1] == 2 && w.Box.Position[2] == 3 && w.Box.Rotation.SequenceEqual(new[] { 0.0, 0.0, 0.0, 1.0 }), "The edit moves the object to the fixed map placement through the current origin");
        var item = (JObject)w.ScenePatchFile["modifications"].First.First;
        Assert((int)w.ScenePatchFile["formatVersion"] == 1 && (string)w.ScenePatchFile["sourceMap"]["mapId"] == "layer", "A new scene patch file carries the format and names the bottom map layer of the loaded map context");
        Assert((string)item["target"]["path"] == "p" && (string)item["placement"]["mode"] == "fixed" && (double)item["placement"]["position"][0] == 112 && (double)item["placement"]["rotation"][3] == 1, "The canonical scene patch file holds the desired fixed placement");
        var replay = new Scene(); var fresh = new Box(); replay.Objects["p"] = new List<object> { fresh };
        Assert(new PatchEngine(replay).Apply(w.Session.ScenePatchFileText).Outcomes.Values.Single().Applied && fresh.X == 12, "The scene patch file applies through the runtime engine");
    }
}
