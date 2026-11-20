// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Several supplying mods in one loaded world, through complete scene patch files and mod metadata.
static class MultipleModTests {
    sealed class Messages : IPatchDiagnostics {
        public readonly List<PatchFailure> Rows = new List<PatchFailure>();
        public void Warning(PatchFailure failure) { Rows.Add(failure); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static string Info(string name, params string[] required) =>
        "{\"name\":\"" + name + "\",\"version\":\"1.0\",\"dependencies\":{\"required\":[" + String.Join(",", required.Select(r => "\"" + r + "\"")) + "]}}";
    static string Move(string id, string path, double x, string deps = "[]") =>
        "\"" + id + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + x + ",0,0],\"rotation\":[0,0,0,1]},\"dependsOn\":" + deps + "}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":{" + items + "}}";
    const string Patcher = "crankyant/scene-patcher >=0.5.0";

    public static void Run() {
        DiscoveryConsidersOnlyDeclaringApplicableMods();
        ApplicationFollowsNativeLoadOrder();
        PrerequisitesOrderOperationsIndependentOfArrayOrder();
        SameObjectFromSeveralModsGivesOneInformationalNotice();
        FailuresStayInsideTheirScenePatchFileOrOperation();
        Console.WriteLine("PASS: multiple supplying mods in native load order with overlap notices and isolated failures");
    }

    static void DiscoveryConsidersOnlyDeclaringApplicableMods() {
        var scene = new Scene(); scene.Origin = 0;
        var declared = new Box(); var undeclared = new Box(); var otherMap = new Box(); var layer = new Box();
        scene.Objects["declared"] = new List<object> { declared }; scene.Objects["undeclared"] = new List<object> { undeclared };
        scene.Objects["other"] = new List<object> { otherMap }; scene.Objects["layer"] = new List<object> { layer };
        var messages = new Messages();
        var runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.6.0", "game");
        var mods = new[] {
            LoadedMod.Read("a/declared", Info("Declared", Patcher), Doc(Move("m", "declared", 5))),
            LoadedMod.Read("b/undeclared", Info("Undeclared", "other/mod >=1.0"), Doc(Move("m", "undeclared", 5))),
            LoadedMod.Read("c/other-map", Info("Other map", Patcher), Doc(Move("m", "other", 5), "elsewhere")),
            LoadedMod.Read("d/layer", Info("Layer", "crankyant/scene-patcher"), Doc(Move("m", "layer", 5), "layer")),
        };
        var scenePatchFiles = runtime.ApplyLoaded(mods, null);
        Check(declared.X == 5 && layer.X == 5, "A loaded mod that declares the patcher dependency and whose scene target map or map layer applies is applied");
        Check(undeclared.X == 0, "A loaded mod without the required patcher dependency is not considered");
        Check(otherMap.X == 0, "A mod whose scene patch file names a scene target map outside the map context is not applied");
        Check(scenePatchFiles.Single(d => d.Mod.Source.Identifier == "b/undeclared").Result == null, "An undeclared mod is never handed to the engine");
        // A missing dependency means the mod is no candidate. A scene patch file for another map is a candidate that the
        // patcher does not apply, so it warns with the supplying mod and the reason; it is no fault, so it names no one to report to.
        var maps = scene.MapIds.ToArray();
        Check(messages.Rows.Count == 1 && messages.Rows[0].ModIdentifier == "c/other-map", "Only the scene patch file for another map gives a warning: " + String.Join(" | ", messages.Rows.Select(r => r.Message)));
        var skip = messages.Rows[0];
        // A map mismatch concerns the whole file, so its row has its own wording without an operation or object.
        Check(skip.Message == "Other map [c/other-map; version 1.0]. The scene patch file is for map elsewhere, which is neither the loaded map " + maps[0] + " nor one of its layers (" + String.Join(", ", maps.Skip(1)) + "). Its modifications were not applied. Patcher 0.6.0; game game."
            && skip.ReportTo == null && skip.Cause == FailureCause.NotApplicable, "The warning names the supplying mod, its version and the reason, without operation, object or report direction: " + skip.Message);
        runtime.ApplyLoaded(mods, null);
        Check(messages.Rows.Count == 1, "A repeated application in the same load gives no second warning");
        // Clicking Editor in the main menu loads the game's editor default map before a map is chosen. No mod supplies a
        // file for that transient map, so its load warns for none of them, applicable elsewhere or not.
        var editorStart = new Scene(); editorStart.Origin = 0; editorStart.Maps = new[] { SceneTargetMapCheck.EditorDefaultMap };
        var editorMessages = new Messages();
        new PatchRuntime(new PatchEngine(editorStart), editorMessages, "0.6.0", "game").ApplyLoaded(mods, null);
        Check(SceneTargetMapCheck.EditorDefaultMap == "rr/editor-default-map" && editorMessages.Rows.Count == 0, "The editor default map's load gives no map-applicability warning: " + String.Join(" | ", editorMessages.Rows.Select(r => r.Message)));
        // One log line per scene patch file per load attempt names the mod, the scene patch file's map and the outcome.
        var start = "Scene Patcher 0.6.0: load attempt 2, map " + maps[0] + " (layers " + String.Join(", ", maps.Skip(1)) + "): ";
        var lines = scenePatchFiles.ToDictionary(d => d.Mod.Source.Identifier, d => d.LogLine("0.6.0", 2, maps));
        Check(lines["a/declared"] == start + "Declared [a/declared] scene patch file for " + maps[0] + ": 1 of 1 modifications applied, 0 failed, 0 unchanged", "An applied scene patch file is logged with its counts: " + lines["a/declared"]);
        Check(lines["d/layer"] == start + "Layer [d/layer] scene patch file for layer: 1 of 1 modifications applied, 0 failed, 0 unchanged", "A scene patch file for a map layer is logged as applied: " + lines["d/layer"]);
        Check(lines["c/other-map"] == start + "Other map [c/other-map] scene patch file for elsewhere skipped: not the loaded map or one of its layers", "A scene patch file for another map is logged as skipped: " + lines["c/other-map"]);
        Check(lines["b/undeclared"] == start + "Undeclared [b/undeclared] scene patch file for " + maps[0] + " skipped: The mod does not declare a dependency on crankyant/scene-patcher", "A mod without the dependency is logged as skipped with its reason: " + lines["b/undeclared"]);
        var again = runtime.ApplyLoaded(mods.Take(1), null).Single().LogLine("0.6.0", 3, new[] { maps[0] });
        Check(again.StartsWith("Scene Patcher 0.6.0: load attempt 3, map " + maps[0] + " (layers none): Declared [a/declared] scene patch file for " + maps[0] + ": ", StringComparison.Ordinal), "A later attempt is logged with its number, and a map without layers says so: " + again);
        var editTarget = LoadedMod.Read("e/edited", Info("Edited"), Doc(Move("m", "undeclared", 7)), editTarget: true);
        runtime.ApplyLoaded(new[] { editTarget }, null);
        Check(undeclared.X == 7, "The native map editor's active scene patch file applies before Save has declared the dependency");
        var editedElsewhere = new Messages();
        new PatchRuntime(new PatchEngine(scene), editedElsewhere, "0.6.0", "game").ApplyLoaded(new[] { LoadedMod.Read("e/edited", Info("Edited"), Doc(Move("m", "undeclared", 8), "elsewhere"), editTarget: true) }, null);
        Check(editedElsewhere.Rows.Count == 0 && undeclared.X == 7, "In the native map editor a scene target map mismatch of the edited mod stays with scene patch editing's own diagnostic");
        var unreadable = LoadedMod.Read("f/broken", "{broken", Doc(Move("m", "declared", 9)));
        Check(unreadable.Error != null && !unreadable.Applicable, "Unreadable supplying metadata protects the mod as a whole");
    }

    static void ApplicationFollowsNativeLoadOrder() {
        foreach (var reversed in new[] { false, true }) {
            var scene = new Scene(); scene.Origin = 0;
            var sign = new Box(); var first = new Box(); var second = new Box();
            scene.Objects["sign"] = new List<object> { sign }; scene.Objects["first"] = new List<object> { first }; scene.Objects["second"] = new List<object> { second };
            var a = LoadedMod.Read("a/early", Info("Early", Patcher), Doc(Move("own", "first", 1) + "," + Move("shared", "sign", 10)));
            var b = LoadedMod.Read("b/late", Info("Late", Patcher), Doc(Move("own", "second", 2) + "," + Move("shared", "sign", 20)));
            var order = reversed ? new[] { b, a } : new[] { a, b };
            var scenePatchFiles = new PatchRuntime(new PatchEngine(scene), new Messages(), "0.6.0", "game").ApplyLoaded(order, null);
            Check(scenePatchFiles.Select(d => d.Mod.Source.Identifier).SequenceEqual(order.Select(p => p.Source.Identifier)), "Scene patch files are processed in the supplied native load order");
            var firstIndex = scene.MutationLog.IndexOf(first); var secondIndex = scene.MutationLog.IndexOf(second);
            Check(reversed ? secondIndex < firstIndex : firstIndex < secondIndex, "Every operation of an earlier mod runs before those of a later mod");
            Check(sign.X == (reversed ? 10 : 20), "The later mod in load order writes the shared object last, without arbitration");
        }
    }

    sealed class Notices : IOverlapNotices {
        public readonly List<OverlapNotice> Rows = new List<OverlapNotice>();
        public Action<OverlapNotice> Hook;
        public void Notice(OverlapNotice notice) { Hook?.Invoke(notice); Rows.Add(notice); }
    }
    static void SameObjectFromSeveralModsGivesOneInformationalNotice() {
        var scene = new Scene(); scene.Origin = 0;
        var sign = new Box(); var own = new Box(); var other = new Box();
        scene.Objects["sign"] = new List<object> { sign }; scene.Objects["own"] = new List<object> { own }; scene.Objects["other"] = new List<object> { other };
        scene.Objects["twins"] = new List<object> { new Box(), new Box() };
        var messages = new Messages(); var notices = new Notices();
        var runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.6.0", "game");
        var baseMod = LoadedMod.Read("a/base", Info("Layer Base", Patcher), Doc(Move("move", "sign", 10) + "," + Move("again", "sign", 11, "[\"move\"]") + "," + Move("twin", "twins", 1)));
        var extension = LoadedMod.Read("b/extension", Info("Extension", Patcher, "a/base >=1.0"), Doc(Move("move", "sign", 20) + "," + Move("twin", "twins", 2)));
        var unrelated = LoadedMod.Read("c/unrelated", Info("Unrelated", Patcher), Doc(Move("move", "other", 30)));
        var scenePatchFiles = runtime.ApplyLoaded(new[] { baseMod, extension, unrelated }, notices);
        Check(notices.Rows.Count == 1, "Exactly one notice: same-mod repeats, ambiguous targets and different objects establish no overlap");
        var notice = notices.Rows.Single();
        Check(notice.Target.Path == "sign" && notice.Target.Scene == "s" && notice.Target.Component == "c", "The notice identifies the resolved existing target");
        Check(notice.Mods.Select(p => p.Identifier).SequenceEqual(new[] { "a/base", "b/extension" }), "The notice names the supplying mods in load order");
        Check(notice.Message.Contains("Layer Base [a/base]") && notice.Message.Contains("Extension [b/extension]") && notice.Message.Contains("s/sign [c]"), "The message names each mod with its identifier and the object");
        Check(notice.Message.Contains("Extension [b/extension] declares a dependency on Layer Base [a/base]"), "A declared dependency between the involved mods is attributed, not used as an exemption");
        Check(notice.Message.Contains("possible incompatibility") && notice.Message.Contains("not a confirmed conflict") && !notice.Message.Contains("Report to"), "The notice reports possible incompatibility, claims no conflict and assigns no fault");
        Check(notice.Source == "Railroader Scene Patcher", "The notice carries the patcher diagnostic source");
        Check(messages.Rows.Count == 2 && messages.Rows.All(r => r.Operation == "twin"), "Overlap is not an application failure; only the ambiguous targets fail");
        Check(scenePatchFiles.All(d => d.Result.Outcomes.Values.Where(o => o.ResolvedTarget != null).All(o => o.Applied)), "No resolved operation was stopped or undone by the overlap");
        Check(sign.X == 20 && other.X == 30, "Application continued in load order and chose no winner beyond that order");
        var late = LoadedMod.Read("d/late", Info("Late", Patcher), Doc(Move("move", "sign", 40)));
        runtime.ApplyLoaded(new[] { baseMod, extension, unrelated, late }, notices);
        Check(notices.Rows.Count == 2 && notices.Rows[1].Key == notice.Key && notices.Rows[1].Mods.Count == 3, "A further mod on the same object updates the same notice row with every supplying mod");
        runtime.ApplyLoaded(new[] { baseMod, extension, unrelated, late }, notices);
        Check(notices.Rows.Count == 2, "Repeated world-load application adds no duplicate notice within the load");
    }

    sealed class FailingMessages : IPatchDiagnostics {
        public int Calls;
        public void Warning(PatchFailure failure) { Calls++; throw new InvalidOperationException("injected warning delivery fault"); }
    }
    static void FailuresStayInsideTheirScenePatchFileOrOperation() {
        var scene = new Scene(); scene.Origin = 0;
        var shared = new Box(); var earlier = new Box(); var independent = new Box(); var failing = new Box { X = 6 }; var dependent = new Box(); var later = new Box();
        foreach (var item in new Dictionary<string, Box> { ["shared"] = shared, ["earlier"] = earlier, ["independent"] = independent, ["failing"] = failing, ["dependent"] = dependent, ["later"] = later })
            scene.Objects[item.Key] = new List<object> { item.Value };
        scene.FailingTarget = failing;
        var messages = new Messages(); var notices = new Notices();
        var runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.6.0", "game");
        var mods = new[] {
            LoadedMod.Read("a/earlier", Info("Earlier", Patcher), Doc(Move("own", "earlier", 1) + "," + Move("shared", "shared", 1))),
            LoadedMod.Read("b/partial", Info("Partial", Patcher), Doc(Move("ok", "independent", 2) + "," + Move("fail", "failing", 2) + "," + Move("after", "dependent", 2, "[\"fail\"]"))),
            LoadedMod.Read("c/unreadable", Info("Unreadable", Patcher), "{broken"),
            LoadedMod.Read("d/no-metadata", "{broken", Doc(Move("own", "later", 99))),
            LoadedMod.Read("e/later", Info("Later", Patcher), Doc(Move("shared", "shared", 5) + "," + Move("own", "later", 5))),
        };
        var scenePatchFiles = runtime.ApplyLoaded(mods, notices);
        Check(earlier.X == 1, "A later failed scene patch file or operation leaves the earlier mod's successful work intact");
        var partial = scenePatchFiles[1].Result;
        Check(partial.Outcomes["ok"].Applied && independent.X == 2, "An independent operation of a partly failing scene patch file applies");
        Check(!partial.Outcomes["fail"].Applied && failing.X == 6, "A failed mutation is restored to its captured prior state");
        Check(!partial.Outcomes["after"].Applied && dependent.X == 0, "Only the dependent of the failed operation is withheld");
        Check(scenePatchFiles[2].Result.Error != null && scenePatchFiles[2].Result.Outcomes.Count == 0 && scenePatchFiles[3].Result == null && scenePatchFiles[3].Skipped != null, "An unreadable scene patch file or unreadable metadata is skipped as a whole");
        Check(later.X == 5 && shared.X == 5, "Independent applicable mods after the failures still process");
        Check(messages.Rows.Select(r => r.Operation).OrderBy(x => x).SequenceEqual(new[] { "after", "fail" }), "Only the failed operation and its dependent are reported as application failures");

        // A fault in the notice or warning presentation is isolated as well.
        shared = new Box(); failing = new Box(); later = new Box();
        scene = new Scene(); scene.Origin = 0; scene.Objects["shared"] = new List<object> { shared }; scene.Objects["failing"] = new List<object> { failing };
        scene.Objects["later"] = new List<object> { later }; scene.FailingTarget = failing;
        var faulty = new FailingMessages(); notices = new Notices { Hook = n => throw new InvalidOperationException("injected notice fault") };
        runtime = new PatchRuntime(new PatchEngine(scene), faulty, "0.6.0", "game");
        scenePatchFiles = runtime.ApplyLoaded(new[] {
            LoadedMod.Read("a/first", Info("First", Patcher), Doc(Move("shared", "shared", 1))),
            LoadedMod.Read("b/second", Info("Second", Patcher), Doc(Move("shared", "shared", 2) + "," + Move("fail", "failing", 2))),
            LoadedMod.Read("c/third", Info("Third", Patcher), Doc(Move("own", "later", 3))),
        }, notices);
        Check(faulty.Calls == 1 && later.X == 3 && shared.X == 2, "A presentation fault in one scene patch file does not stop the next applicable mod");
        Check(scenePatchFiles[1].PresentationError != null && scenePatchFiles[1].Result.Outcomes["shared"].Applied, "The presentation fault is recorded without undoing the scene patch file's applied work");
    }

    static void PrerequisitesOrderOperationsIndependentOfArrayOrder() {
        foreach (var dependentFirst in new[] { true, false }) {
            var scene = new Scene(); scene.Origin = 0;
            var prerequisite = new Box(); var dependent = new Box();
            scene.Objects["prerequisite"] = new List<object> { prerequisite }; scene.Objects["dependent"] = new List<object> { dependent };
            var built = Move("build", "prerequisite", 3); var bound = Move("bind", "dependent", 4, "[\"build\"]");
            var mod = LoadedMod.Read("a/pack", Info("Pack", Patcher), Doc(dependentFirst ? bound + "," + built : built + "," + bound));
            var result = new PatchRuntime(new PatchEngine(scene), new Messages(), "0.6.0", "game").ApplyLoaded(new[] { mod }, null).Single().Result;
            Check(result.Outcomes["build"].Applied && result.Outcomes["bind"].Applied, "Both operations apply in either array order");
            Check(scene.MutationLog.SequenceEqual(new object[] { prerequisite, dependent }), "The prerequisite is mutated before its dependent whatever the textual order");
        }
    }
}
