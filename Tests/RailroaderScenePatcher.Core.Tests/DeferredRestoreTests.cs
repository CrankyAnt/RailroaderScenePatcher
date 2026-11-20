// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// The game runs the restore-time callback of a scene object before the patcher's world-load apply. A callback waits for
// that apply only when an applicable scene patch file targets exactly that object and the callback would act on data the
// file changes; for a construction controller, a saved segment id the track does not hold while every id of the last
// setting that passes the engine's check is on the track.
static class DeferredRestoreTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Deferred restore: " + message); }
    const string SceneName = "BushnellWhittier-MapManager", Bryson = "World/Large Scenery/Bryson/Bridge 1", Ela = "World/Large Scenery/Ela/Ela Bridge Scenery";
    static readonly string[] Maps = { "map", "layer" };
    static readonly string[] Components = { BakedSceneryGroups.ComponentType, ConstructionSceneryScene.ComponentType };
    static string Target(string path, string scene = SceneName, string component = BakedSceneryGroups.ComponentType, string structure = BakedSceneryGroups.Structure) =>
        "\"target\":{\"scene\":\"" + scene + "\",\"path\":\"" + path + "\",\"component\":\"" + component + "\",\"structure\":\"" + structure + "\"}";
    static string Doc(string map, params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static string Segments(string id, string target, string relationship) => "\"" + id + "\":{" + target + ",\"relationships\":{\"constructionSegments\":" + relationship + "},\"dependsOn\":[]}";
    static string Repair(string id, string path, params string[] ids) => Segments(id, Target(path), "{\"segments\":[" + String.Join(",", ids.Select(s => "\"" + s + "\"")) + "]}");
    static LoadedMod Mod(string id, string json, bool applicable = true) => new LoadedMod(new PatchSource(id, id, "1.0.0"), json, applicable ? new[] { LoadedMod.PatcherId } : new string[0]);
    // The loaded track: S-bryson was split natively into S-bryson-a and S-bryson-b.
    static readonly HashSet<string> Track = new HashSet<string>(StringComparer.Ordinal) { "S-bryson-a", "S-bryson-b", "S-almond", "S-ela" };
    static bool OnTrack(string id) => Track.Contains(id);

    public static void Run() {
        DecisionTable();
        TargetsComeFromApplicableFiles();
        Console.WriteLine("PASS: deferred restore");
    }

    sealed class Row {
        internal string Name; internal LoadedMod[] Mods; internal string Path = Bryson; internal string Scene = SceneName; internal string[] Saved = { "S-bryson" }; internal bool Defer;
    }
    // One table over the decision rule: target match, missing saved ids and ids the file sets that are on the track.
    static void DecisionTable() {
        var repair = Doc("map", Repair("repair", Bryson, "S-bryson-a", "S-bryson-b"));
        var rows = new[] {
            new Row { Name = "A repair of the split segment on exactly this object waits", Mods = new[] { Mod("a", repair) }, Defer = true },
            new Row { Name = "A repair on Ela's own controller waits", Path = Ela, Saved = new[] { "S-ela-old" }, Mods = new[] { Mod("a", Doc("map", Segments("ela", Target(Ela, component: ConstructionSceneryScene.ComponentType, structure: ConstructionSceneryScene.Structure), "{\"segments\":[\"S-ela\"]}"))) }, Defer = true },
            new Row { Name = "Saved ids that are all on the track do not wait", Saved = new[] { "S-bryson-a" }, Mods = new[] { Mod("a", repair) }, Defer = false },
            new Row { Name = "A file id that is not on the track does not wait", Mods = new[] { Mod("a", Doc("map", Repair("stale", Bryson, "S-bryson-a", "S-gone"))) }, Defer = false },
            new Row { Name = "A file that sets the saved, missing set does not wait", Mods = new[] { Mod("a", Doc("map", Repair("same", Bryson, "S-bryson"))) }, Defer = false },
            new Row { Name = "A detach does not wait", Mods = new[] { Mod("a", Doc("map", Segments("detach", Target(Bryson), "{\"detach\":true}"))) }, Defer = false },
            new Row { Name = "Another object at another path does not wait", Path = "World/Large Scenery/Almond Bridge", Mods = new[] { Mod("a", repair) }, Defer = false },
            new Row { Name = "The same path in another scene does not wait", Scene = "OtherScene", Mods = new[] { Mod("a", repair) }, Defer = false },
            new Row { Name = "A file for another map does not wait", Mods = new[] { Mod("a", Doc("other-map", Repair("repair", Bryson, "S-bryson-a", "S-bryson-b"))) }, Defer = false },
            new Row { Name = "A mod that does not declare the patcher does not wait", Mods = new[] { Mod("a", repair, applicable: false) }, Defer = false },
            new Row { Name = "An unreadable file does not wait", Mods = new[] { Mod("a", "{ not json") }, Defer = false },
            new Row { Name = "A modification the patcher does not execute does not wait", Mods = new[] { Mod("a", Doc("map", "\"odd\":{" + Target(Bryson) + ",\"relationships\":{\"constructionSegments\":{\"segments\":[\"S-bryson-a\"]}},\"unknown\":1,\"dependsOn\":[]}")) }, Defer = false },
            new Row { Name = "A placement-only modification of the object does not wait", Mods = new[] { Mod("a", Doc("map", "\"move\":{" + Target(Bryson) + ",\"active\":false,\"dependsOn\":[]}")) }, Defer = false },
            new Row { Name = "No scene patch file does not wait", Mods = new LoadedMod[0], Defer = false },
            new Row { Name = "A stale later file is refused before mutation, so the earlier repair stays and waits", Mods = new[] { Mod("a", repair), Mod("b", Doc("map", Repair("stale", Bryson, "S-gone"))) }, Defer = true },
            new Row { Name = "A later detach is refused before mutation, so the earlier repair stays and waits", Mods = new[] { Mod("a", repair), Mod("b", Doc("map", Segments("detach", Target(Bryson), "{\"detach\":true}"))) }, Defer = true },
            new Row { Name = "A later file that sets the saved, missing set passes the check and wins, so nothing waits", Mods = new[] { Mod("a", repair), Mod("b", Doc("map", Repair("same", Bryson, "S-bryson"))) }, Defer = false },
            new Row { Name = "Only stale and detach settings leave nothing to wait for", Mods = new[] { Mod("a", Doc("map", Repair("stale", Bryson, "S-gone"))), Mod("b", Doc("map", Segments("detach", Target(Bryson), "{\"detach\":true}"))) }, Defer = false },
            new Row { Name = "A later repair after a stale file waits", Mods = new[] { Mod("b", Doc("map", Repair("stale", Bryson, "S-gone"))), Mod("a", repair) }, Defer = true },
            new Row { Name = "A later file without construction segments keeps the earlier repair", Mods = new[] { Mod("a", repair), Mod("b", Doc("map", "\"move\":{" + Target(Bryson) + ",\"active\":true,\"dependsOn\":[]}")) }, Defer = true },
        };
        foreach (var row in rows) {
            var targets = RestoreTargets.Read(row.Mods, Maps, new string[0]);
            var settings = targets.ConstructionSegments(row.Scene, row.Path, Components);
            var defer = DeferredRestoreRule.DeferConstruction(settings, row.Saved, OnTrack);
            Assert(defer == row.Defer, row.Name + ": expected " + (row.Defer ? "wait" : "run") + ", got " + (defer ? "wait" : "run"));
        }
        Assert(!DeferredRestoreRule.DeferConstruction(null, new[] { "S-bryson" }, OnTrack), "No settings does not wait");
        Assert(!DeferredRestoreRule.DeferConstruction(new[] { ConstructionSegments.Of(new[] { "S-bryson-a" }) }, null, OnTrack), "A controller without saved ids does not wait");
    }

    // The targets come only from scene patch files that would apply: applicable mods, readable files, the loaded map or one
    // of its layers, and not a map patch; a modification is matched through the engine's own parse.
    static void TargetsComeFromApplicableFiles() {
        var repair = Doc("map", Repair("repair", Bryson, "S-bryson-a", "S-bryson-b"));
        var layer = RestoreTargets.Read(new[] { Mod("a", Doc("layer", Repair("repair", Bryson, "S-bryson-a"))) }, Maps, new string[0]);
        Assert(layer.ConstructionSegments(SceneName, Bryson, Components).Count == 1, "A file for a map layer counts");
        var patch = RestoreTargets.Read(new[] { Mod("a", Doc("layer", Repair("repair", Bryson, "S-bryson-a"))) }, Maps, new[] { "layer" });
        Assert(patch.ConstructionSegments(SceneName, Bryson, Components).Count == 0, "A file that names a map patch does not count");
        var edited = RestoreTargets.Read(new[] { new LoadedMod(new PatchSource("e", "e", "1.0.0"), repair, new string[0], editTarget: true) }, Maps, new string[0]);
        Assert(edited.ConstructionSegments(SceneName, Bryson, Components).Count == 1, "The native map editor's edited mod counts before it declares the patcher");
        var otherComponent = RestoreTargets.Read(new[] { Mod("a", Doc("map", Segments("x", Target(Bryson, component: "Some.Other"), "{\"segments\":[\"S-bryson-a\"]}"))) }, Maps, new string[0]);
        Assert(otherComponent.ConstructionSegments(SceneName, Bryson, Components).Count == 0, "A target naming a component the object does not carry does not count");
        Assert(RestoreTargets.Empty.ConstructionSegments(SceneName, Bryson, Components).Count == 0, "The empty set holds no settings");
        var broken = RestoreTargets.Read(new LoadedMod[] { null, Mod("a", null), Mod("b", repair) }, Maps, new string[0]);
        Assert(broken.ConstructionSegments(SceneName, Bryson, Components).Count == 1, "A missing or unreadable mod does not hide the mods after it");
    }
}
