// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

static class ReloadDecisionTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static readonly TargetIdentity P = new TargetIdentity("s", "p", "c");
    static Placement PlacementAt(double x) => new Placement(new[] { x, 2.0, 3.0 }, new[] { 0.0, 0.0, 0.0, 1.0 });
    sealed class World {
        public Scene Scene = new Scene(); public Box Box = new Box { Position = new[] { -93.0, 8.0, 9.0 }, Rotation = new[] { 0.0, 1.0, 0.0, 0.0 } };
        public Box Other = new Box { Position = new[] { -50.0, 0.0, 0.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        public Store Store = new Store(); public NativeMapEditor MapEditor = new NativeMapEditor(); public PatchEditingSession Session;
        public bool AtUnderlying => Box.X == -93 && Box.Position[1] == 8 && Box.Position[2] == 9 && Box.Rotation.SequenceEqual(new[] { 0.0, 1.0, 0.0, 0.0 }) && Box.Active;
        public World(string patch = null, string info = null, IStorage files = null, string[] maps = null) {
            if (maps != null) Scene.Maps = maps;
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
        KeepEditorPreservesUnknownNumber();
        FileVersionUpdatesPlacement();
        ReloadPromptDescribesChoicesInOrder();
        ReloadPromptReportsWhenAllWorkIsSaved();
        ReloadRequiresAnExplicitDecision();
        ReloadSaveFailureKeepsOutstandingWork();
        ReloadFeedbackFollowsActualSessionState();
        ReloadPreservesSaveFailureDetails();
        PendingFileChoicePreventsSaveBeforeReload();
        Console.WriteLine("PASS: explicit reload decisions and partial-save protection");
    }
    static void FileVersionUpdatesPlacement() {
        const string initial = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"s\",\"path\":\"p\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}";
        var w = new World(initial);
        w.Store.Patch = initial.Replace("[112,2,3]", "[114,2,3]");
        w.Session.CheckFileVersion();
        Assert(w.Session.UseFileVersion().Applied && w.Box.X == 14, "Using the file version applies its placement to the scene");
    }
    static void KeepEditorPreservesUnknownNumber() {
        var initial = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{},\"issue59Retained\":{\"number\":0.10}}";
        var w = new World(initial);
        w.Store.Patch = initial.Replace("\"number\":0.10", "\"number\":0.10,\"outside\":true");
        w.Session.CheckFileVersion();
        Assert(w.Session.UseFileVersion().Applied, "Use file version applies the changed file");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        initial = w.Store.Patch;
        w.Store.Patch = initial.Replace("0.10", "0.20");
        w.Session.CheckFileVersion();
        Assert(w.Session.KeepEditorVersion().Applied && w.MapEditor.Save() == null && w.Store.Patch.Contains("\"number\": 0.10,"), "Keep editor version and Save preserve the unknown number literal: " + w.Store.Patch);
        w.Store.Patch = w.Store.Patch.Replace("0.10", "0.20");
        w.Session.CheckFileVersion();
        Assert(w.Session.Edit(P, PlacementAt(112), null).Applied && w.Session.KeepEditorVersion().Applied && w.MapEditor.Save() == null && w.Store.Patch.Contains("\"number\": 0.10,"), "An edit and Save preserve the unknown number literal: " + w.Store.Patch);
    }
    static void PendingFileChoicePreventsSaveBeforeReload() {
        var w = new World();
        w.Store.Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{},\"extra\":true}";
        w.Session.CheckFileVersion();
        var prompt = w.Session.ReloadPrompt(false);
        Assert(prompt.Contains("choose a version") && prompt.Contains("Resolve the scene patch file version choice before saving.")
            && !prompt.Contains("Save and Reload"), "The reload prompt explains the pending version choice when Save is unavailable");
        var saves = 0; var reloads = 0;
        var message = w.Session.Reload(ReloadChoice.Save, () => false, () => saves++, () => reloads++);
        Assert(message != null && saves == 0 && reloads == 0,
            "A pending version choice prevents invoking Save or reload even if a callback would report clean");
    }
    static void ReloadRequiresAnExplicitDecision() {
        var w = new World(); var reloads = 0;
        w.Session.Edit(P, PlacementAt(112), null); w.MapEditor.NativeEdit(7);
        Assert(w.Session.Reload(ReloadChoice.Cancel, () => w.MapEditor.NativeDirty, () => w.MapEditor.Save(), () => reloads++) == null
            && reloads == 0 && w.MapEditor.NativeWrites == 0 && w.MapEditor.IsDirty && w.Box.X == 12, "Cancel keeps native and patcher work, history and files");
        Assert(w.Session.Reload(ReloadChoice.Save, () => w.MapEditor.NativeDirty, () => w.MapEditor.Save(), () => reloads++) == null
            && reloads == 1 && !w.MapEditor.IsDirty && w.Saved["modifications"].Count() == 1 && w.MapEditor.NativeValue == 7, "Save reloads only after all participants are saved");
        w.Session.Edit(P, PlacementAt(130), null);
        var writes = w.Store.PatchWrites;
        Assert(w.Session.Reload(ReloadChoice.Discard, () => w.MapEditor.NativeDirty, () => w.MapEditor.Save(), () => reloads++) == null
            && reloads == 2 && w.Store.PatchWrites == writes && w.MapEditor.IsDirty, "Deliberate discard enters the reload route without saving");
    }
    static void ReloadPromptDescribesChoicesInOrder() {
        const string initial = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{}}";
        var w = new World(initial);
        w.Session.Edit(P, PlacementAt(112), null); w.MapEditor.NativeEdit(7);
        Assert(w.Session.ReloadPrompt(w.MapEditor.NativeDirty) ==
            "Reload the Map Editor? Reloading ends the Map Editor's Undo and Redo history. "
            + "Save and Reload saves changes to the map data and scene patch file before reloading. "
            + "Discard and Reload discards unsaved changes to the map data and scene patch file. "
            + "Cancel keeps the Map Editor open. Your working location is restored when available.",
            "The reload confirmation explains Save, Discard, and Cancel in order and names the same unsaved work for both reload choices.");
    }
    static void ReloadPromptReportsWhenAllWorkIsSaved() {
        var w = new World();
        var prompt = w.Session.ReloadPrompt(w.MapEditor.NativeDirty);
        Assert(prompt.Contains("All required work is saved.") && prompt.Contains("Save and Reload saves changes to the map data and scene patch file before reloading.")
            && !prompt.Contains("discards unsaved changes"), "The reload confirmation reports the saved state without warning about unsaved changes");
    }
    static void ReloadSaveFailureKeepsOutstandingWork() {
        var w = new World(); var reloads = 0;
        w.Session.Edit(P, PlacementAt(112), null); w.MapEditor.NativeEdit(7);
        w.MapEditor.FailNative = true;
        var message = w.Session.Reload(ReloadChoice.Save, () => w.MapEditor.NativeDirty, () => w.MapEditor.Save(), () => reloads++);
        Assert(message.Contains("Changes to the map data are still unsaved.") && message.Contains("native write injected") && reloads == 0
            && w.Store.PatchWrites == 1 && w.MapEditor.IsDirty && w.Box.X == 12 && w.MapEditor.NativeValue == 7,
            "A partial Save blocks reload and names completed and outstanding writes and the failure");
        Assert(w.Session.Reload(ReloadChoice.Cancel, () => w.MapEditor.NativeDirty, () => w.MapEditor.Save(), () => reloads++) == null
            && reloads == 0 && w.MapEditor.CanUndo, "Cancel after a partial Save preserves remaining work and history");
        w.MapEditor.FailNative = false;
        Assert(w.Session.Reload(ReloadChoice.Save, () => w.MapEditor.NativeDirty, () => w.MapEditor.Save(), () => reloads++) == null
            && reloads == 1 && w.Store.PatchWrites == 1 && !w.MapEditor.IsDirty, "Retry completes outstanding writes before reloading");
        w.MapEditor.NativeEdit(8); w.MapEditor.ThrowInNativeAction = true; w.MapEditor.Undo();
        message = w.Session.Reload(ReloadChoice.Save, () => w.MapEditor.NativeDirty, () => w.MapEditor.Save(), () => reloads++);
        Assert(message.Contains("Undo history could not be recovered") && reloads == 1, "Save cannot silently discard work after an unrecovered native history error");
        Assert(w.Session.Reload(ReloadChoice.Discard, () => w.MapEditor.NativeDirty, () => w.MapEditor.Save(), () => reloads++) == null
            && reloads == 2, "An explicit discard remains available after incomplete persistence");
    }
    static void ReloadFeedbackFollowsActualSessionState() {
        var w = new World();
        Assert(w.Session.ReloadRequirement == null, "A clean session needs no reload");
        w.Session.Edit(P, PlacementAt(112), false);
        Assert(w.Session.ReloadRequirement == null, "An effective live placement or activity update does not impose reload");
        w.MapEditor.Undo(); w.MapEditor.Redo(); w.Session.Remove(P);
        Assert(w.Session.ReloadRequirement == null, "Undo, Redo and removal retain effective live updates without a stale reload flag");
        w.MapEditor.NativeEdit(3); w.MapEditor.ThrowInNativeAction = true; w.MapEditor.Undo();
        Assert(w.Session.ReloadRequirement.Contains("Undo history could not be recovered"), "An unrecovered native history remains a concrete reload requirement");
        w.MapEditor.Save();
        Assert(w.Session.ReloadRequirement.Contains("Undo history could not be recovered"), "Save does not claim to repair native history");
    }
    static void ReloadPreservesSaveFailureDetails() {
        var w = new World(); var reloads = 0;
        var message = w.Session.Reload(ReloadChoice.Save, () => false,
            () => { throw new IOException("completed writes: scene patch file; outstanding: map data", new IOException("disk cause")); }, () => reloads++);
        Assert(reloads == 0 && message.Contains("completed writes: scene patch file") && message.Contains("outstanding: map data"),
            "A thrown native Save report keeps its completed and outstanding writes rather than only the inner cause");
    }
}
