// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Several supplying MapMods in one loaded world, through complete documents and pack metadata.
static class MultiplePackTests {
    sealed class Messages : IPatchDiagnostics {
        public readonly List<PatchFailure> Rows = new List<PatchFailure>();
        public void Warning(PatchFailure failure) { Rows.Add(failure); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static string Info(string name, params string[] required) =>
        "{\"name\":\"" + name + "\",\"version\":\"1.0\",\"dependencies\":{\"required\":[" + String.Join(",", required.Select(r => "\"" + r + "\"")) + "]}}";
    static string Move(string id, string path, double x, string deps = "[]") =>
        "{\"id\":\"" + id + "\",\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + x + ",0,0],\"rotation\":[0,0,0,1]},\"dependsOn\":" + deps + "}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":[" + items + "]}";
    const string Patcher = "crankyant/scene-patcher >=0.5.0";

    public static void Run() {
        DiscoveryConsidersOnlyDeclaringApplicablePacks();
        ApplicationFollowsNativeLoadOrder();
        PrerequisitesOrderOperationsIndependentOfArrayOrder();
        SameObjectFromSeveralPacksGivesOneInformationalNotice();
        FailuresStayInsideTheirDocumentOrOperation();
        Console.WriteLine("PASS: multiple supplying MapMods in native load order with overlap notices and isolated failures");
    }

    static void DiscoveryConsidersOnlyDeclaringApplicablePacks() {
        var scene = new Scene(); scene.Origin = 0;
        var declared = new Box(); var undeclared = new Box(); var otherMap = new Box(); var layer = new Box();
        scene.Objects["declared"] = new List<object> { declared }; scene.Objects["undeclared"] = new List<object> { undeclared };
        scene.Objects["other"] = new List<object> { otherMap }; scene.Objects["layer"] = new List<object> { layer };
        var messages = new Messages();
        var runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.6.0", "game");
        var packs = new[] {
            LoadedPack.Read("a/declared", Info("Declared", Patcher), Doc(Move("m", "declared", 5))),
            LoadedPack.Read("b/undeclared", Info("Undeclared", "other/mod >=1.0"), Doc(Move("m", "undeclared", 5))),
            LoadedPack.Read("c/other-map", Info("Other map", Patcher), Doc(Move("m", "other", 5), "elsewhere")),
            LoadedPack.Read("d/layer", Info("Layer", "crankyant/scene-patcher"), Doc(Move("m", "layer", 5), "layer")),
        };
        var documents = runtime.ApplyLoaded(packs, null);
        Check(declared.X == 5 && layer.X == 5, "A loaded pack that declares the patcher dependency and whose source map or map layer applies is applied");
        Check(undeclared.X == 0, "A loaded pack without the required patcher dependency is not considered");
        Check(otherMap.X == 0, "A pack whose source map context does not apply is not applied");
        Check(documents.Single(d => d.Pack.Source.Identifier == "b/undeclared").Result == null, "An undeclared pack is never handed to the engine");
        Check(messages.Rows.Count == 0, "Neither a missing dependency nor a map mismatch is a player-facing application failure");
        var editTarget = LoadedPack.Read("e/edited", Info("Edited"), Doc(Move("m", "undeclared", 7)), editTarget: true);
        runtime.ApplyLoaded(new[] { editTarget }, null);
        Check(undeclared.X == 7, "The editor's active document applies before Save has declared the dependency");
        var unreadable = LoadedPack.Read("f/broken", "{broken", Doc(Move("m", "declared", 9)));
        Check(unreadable.Error != null && !unreadable.Applicable, "Unreadable supplying metadata protects the pack as a whole");
    }

    static void ApplicationFollowsNativeLoadOrder() {
        foreach (var reversed in new[] { false, true }) {
            var scene = new Scene(); scene.Origin = 0;
            var sign = new Box(); var first = new Box(); var second = new Box();
            scene.Objects["sign"] = new List<object> { sign }; scene.Objects["first"] = new List<object> { first }; scene.Objects["second"] = new List<object> { second };
            var a = LoadedPack.Read("a/early", Info("Early", Patcher), Doc(Move("own", "first", 1) + "," + Move("shared", "sign", 10)));
            var b = LoadedPack.Read("b/late", Info("Late", Patcher), Doc(Move("own", "second", 2) + "," + Move("shared", "sign", 20)));
            var order = reversed ? new[] { b, a } : new[] { a, b };
            var documents = new PatchRuntime(new PatchEngine(scene), new Messages(), "0.6.0", "game").ApplyLoaded(order, null);
            Check(documents.Select(d => d.Pack.Source.Identifier).SequenceEqual(order.Select(p => p.Source.Identifier)), "Documents are processed in the supplied native load order");
            var firstIndex = scene.MutationLog.IndexOf(first); var secondIndex = scene.MutationLog.IndexOf(second);
            Check(reversed ? secondIndex < firstIndex : firstIndex < secondIndex, "Every operation of an earlier pack runs before those of a later pack");
            Check(sign.X == (reversed ? 10 : 20), "The later pack in load order writes the shared object last, without arbitration");
        }
    }

    sealed class Notices : IOverlapNotices {
        public readonly List<OverlapNotice> Rows = new List<OverlapNotice>();
        public Action<OverlapNotice> Hook;
        public void Notice(OverlapNotice notice) { Hook?.Invoke(notice); Rows.Add(notice); }
    }
    static void SameObjectFromSeveralPacksGivesOneInformationalNotice() {
        var scene = new Scene(); scene.Origin = 0;
        var sign = new Box(); var own = new Box(); var other = new Box();
        scene.Objects["sign"] = new List<object> { sign }; scene.Objects["own"] = new List<object> { own }; scene.Objects["other"] = new List<object> { other };
        scene.Objects["twins"] = new List<object> { new Box(), new Box() };
        var messages = new Messages(); var notices = new Notices();
        var runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.6.0", "game");
        var basePack = LoadedPack.Read("a/base", Info("Base Map", Patcher), Doc(Move("move", "sign", 10) + "," + Move("again", "sign", 11, "[\"move\"]") + "," + Move("twin", "twins", 1)));
        var extension = LoadedPack.Read("b/extension", Info("Extension", Patcher, "a/base >=1.0"), Doc(Move("move", "sign", 20) + "," + Move("twin", "twins", 2)));
        var unrelated = LoadedPack.Read("c/unrelated", Info("Unrelated", Patcher), Doc(Move("move", "other", 30)));
        var documents = runtime.ApplyLoaded(new[] { basePack, extension, unrelated }, notices);
        Check(notices.Rows.Count == 1, "Exactly one notice: same-pack repeats, ambiguous targets and different objects establish no overlap");
        var notice = notices.Rows.Single();
        Check(notice.Target.Path == "sign" && notice.Target.Scene == "s" && notice.Target.Component == "c", "The notice identifies the resolved existing object");
        Check(notice.Packs.Select(p => p.Identifier).SequenceEqual(new[] { "a/base", "b/extension" }), "The notice names the supplying MapMods in load order");
        Check(notice.Message.Contains("Base Map [a/base]") && notice.Message.Contains("Extension [b/extension]") && notice.Message.Contains("s/sign [c]"), "The message names each MapMod with its identifier and the object");
        Check(notice.Message.Contains("Extension [b/extension] declares a dependency on Base Map [a/base]"), "A declared dependency between the involved packs is attributed, not used as an exemption");
        Check(notice.Message.Contains("possible incompatibility") && notice.Message.Contains("not a confirmed conflict") && !notice.Message.Contains("Report to"), "The notice reports possible incompatibility, claims no conflict and assigns no fault");
        Check(notice.Source == "Railroader Scene Patcher", "The notice carries the patcher diagnostic source");
        Check(messages.Rows.Count == 2 && messages.Rows.All(r => r.Operation == "twin"), "Overlap is not an application failure; only the ambiguous targets fail");
        Check(documents.All(d => d.Result.Outcomes.Values.Where(o => o.ResolvedTarget != null).All(o => o.Applied)), "No resolved operation was stopped or undone by the overlap");
        Check(sign.X == 20 && other.X == 30, "Application continued in load order and chose no winner beyond that order");
        var late = LoadedPack.Read("d/late", Info("Late", Patcher), Doc(Move("move", "sign", 40)));
        runtime.ApplyLoaded(new[] { basePack, extension, unrelated, late }, notices);
        Check(notices.Rows.Count == 2 && notices.Rows[1].Key == notice.Key && notices.Rows[1].Packs.Count == 3, "A further MapMod on the same object updates the same notice row with every supplying pack");
        runtime.ApplyLoaded(new[] { basePack, extension, unrelated, late }, notices);
        Check(notices.Rows.Count == 2, "Repeated world-load application adds no duplicate notice within the load");
    }

    sealed class FailingMessages : IPatchDiagnostics {
        public int Calls;
        public void Warning(PatchFailure failure) { Calls++; throw new InvalidOperationException("injected warning delivery fault"); }
    }
    static void FailuresStayInsideTheirDocumentOrOperation() {
        var scene = new Scene(); scene.Origin = 0;
        var shared = new Box(); var earlier = new Box(); var independent = new Box(); var failing = new Box { X = 6 }; var dependent = new Box(); var later = new Box();
        foreach (var item in new Dictionary<string, Box> { ["shared"] = shared, ["earlier"] = earlier, ["independent"] = independent, ["failing"] = failing, ["dependent"] = dependent, ["later"] = later })
            scene.Objects[item.Key] = new List<object> { item.Value };
        scene.FailingTarget = failing;
        var messages = new Messages(); var notices = new Notices();
        var runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.6.0", "game");
        var packs = new[] {
            LoadedPack.Read("a/earlier", Info("Earlier", Patcher), Doc(Move("own", "earlier", 1) + "," + Move("shared", "shared", 1))),
            LoadedPack.Read("b/partial", Info("Partial", Patcher), Doc(Move("ok", "independent", 2) + "," + Move("fail", "failing", 2) + "," + Move("after", "dependent", 2, "[\"fail\"]"))),
            LoadedPack.Read("c/unreadable", Info("Unreadable", Patcher), "{broken"),
            LoadedPack.Read("d/no-metadata", "{broken", Doc(Move("own", "later", 99))),
            LoadedPack.Read("e/later", Info("Later", Patcher), Doc(Move("shared", "shared", 5) + "," + Move("own", "later", 5))),
        };
        var documents = runtime.ApplyLoaded(packs, notices);
        Check(earlier.X == 1, "A later failed document or operation leaves the earlier pack's successful work intact");
        var partial = documents[1].Result;
        Check(partial.Outcomes["ok"].Applied && independent.X == 2, "An independent operation of a partly failing document applies");
        Check(!partial.Outcomes["fail"].Applied && failing.X == 6, "A failed mutation is restored to its captured prior state");
        Check(!partial.Outcomes["after"].Applied && dependent.X == 0, "Only the dependent of the failed operation is withheld");
        Check(documents[2].Result.Error != null && documents[2].Result.Outcomes.Count == 0 && documents[3].Result == null && documents[3].Skipped != null, "An unreadable document or unreadable metadata is skipped as a whole");
        Check(later.X == 5 && shared.X == 5, "Independent applicable packs after the failures still process");
        Check(messages.Rows.Select(r => r.Operation).OrderBy(x => x).SequenceEqual(new[] { "after", "fail" }), "Only the failed operation and its dependent are reported as application failures");

        // A fault in the notice or warning presentation is isolated as well.
        shared = new Box(); failing = new Box(); later = new Box();
        scene = new Scene(); scene.Origin = 0; scene.Objects["shared"] = new List<object> { shared }; scene.Objects["failing"] = new List<object> { failing };
        scene.Objects["later"] = new List<object> { later }; scene.FailingTarget = failing;
        var faulty = new FailingMessages(); notices = new Notices { Hook = n => throw new InvalidOperationException("injected notice fault") };
        runtime = new PatchRuntime(new PatchEngine(scene), faulty, "0.6.0", "game");
        documents = runtime.ApplyLoaded(new[] {
            LoadedPack.Read("a/first", Info("First", Patcher), Doc(Move("shared", "shared", 1))),
            LoadedPack.Read("b/second", Info("Second", Patcher), Doc(Move("shared", "shared", 2) + "," + Move("fail", "failing", 2))),
            LoadedPack.Read("c/third", Info("Third", Patcher), Doc(Move("own", "later", 3))),
        }, notices);
        Check(faulty.Calls == 1 && later.X == 3 && shared.X == 2, "A presentation fault in one document does not stop the next applicable pack");
        Check(documents[1].PresentationError != null && documents[1].Result.Outcomes["shared"].Applied, "The presentation fault is recorded without undoing the document's applied work");
    }

    static void PrerequisitesOrderOperationsIndependentOfArrayOrder() {
        foreach (var dependentFirst in new[] { true, false }) {
            var scene = new Scene(); scene.Origin = 0;
            var prerequisite = new Box(); var dependent = new Box();
            scene.Objects["prerequisite"] = new List<object> { prerequisite }; scene.Objects["dependent"] = new List<object> { dependent };
            var built = Move("build", "prerequisite", 3); var bound = Move("bind", "dependent", 4, "[\"build\"]");
            var pack = LoadedPack.Read("a/pack", Info("Pack", Patcher), Doc(dependentFirst ? bound + "," + built : built + "," + bound));
            var result = new PatchRuntime(new PatchEngine(scene), new Messages(), "0.6.0", "game").ApplyLoaded(new[] { pack }, null).Single().Result;
            Check(result.Outcomes["build"].Applied && result.Outcomes["bind"].Applied, "Both operations apply in either array order");
            Check(scene.MutationLog.SequenceEqual(new object[] { prerequisite, dependent }), "The prerequisite is mutated before its dependent whatever the textual order");
        }
    }
}
