// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// The keyed shape of format version 1: which documents are rejected as a whole and protected from overwrite, and which
// faults fail only the modification that holds them.
static class ShapeTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static readonly TargetIdentity P = new TargetIdentity("s", "p", "c"), Q = new TargetIdentity("s", "q", "c");
    const string Head = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":";
    static string Target(string path) => "\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"}";
    static string Place(string path, double x = 112) => "{" + Target(path) + ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
    static string Cover(string path, string coverage) => "{" + Target(path) + ",\"relationships\":{\"crossingCoverage\":" + coverage + "},\"dependsOn\":[]}";
    const string Ranges = "\"approachA\":{\"from\":{\"segment\":\"S\",\"end\":\"a\",\"distance\":1},\"to\":{\"segment\":\"S\",\"end\":\"a\",\"distance\":2}},\"island\":{\"from\":{\"segment\":\"S\",\"end\":\"a\",\"distance\":2},\"to\":{\"segment\":\"S\",\"end\":\"a\",\"distance\":3}},\"approachB\":{\"from\":{\"segment\":\"S\",\"end\":\"a\",\"distance\":3},\"to\":{\"segment\":\"S\",\"end\":\"a\",\"distance\":4}}";
    sealed class World {
        public Scene Scene = new Scene(); public Box Box = new Box { Position = new[] { 7.0, 8.0, 9.0 }, Rotation = new[] { 1.0, 0.0, 0.0, 0.0 } };
        public Box Other = new Box { Position = new[] { -50.0, 0.0, 0.0 }, Rotation = new[] { 0.0, 0.0, 0.0, 1.0 } };
        public World() { Scene.Objects["p"] = new List<object> { Box }; Scene.Objects["q"] = new List<object> { Other }; }
        public bool Untouched => Box.Position.SequenceEqual(new[] { 7.0, 8.0, 9.0 }) && Box.Rotation.SequenceEqual(new[] { 1.0, 0.0, 0.0, 0.0 });
    }
    public static void Run() {
        KeyedDocumentApplies();
        WholeDocumentIsRejectedAndProtected();
        ModificationFaultsFailOnlyTheirModification();
        Console.WriteLine("PASS: keyed shape, whole-document rejection with overwrite protection and faults isolated to their modification");
    }
    static void KeyedDocumentApplies() {
        var w = new World();
        var result = new PatchEngine(w.Scene).Apply(Head + "{\"move-p\":" + Place("p") + ",\"move-q\":" + Place("q", 160) + "}}");
        Assert(result.Error == null && result.Outcomes.Count == 2 && result.Outcomes["move-p"].Applied && result.Outcomes["move-q"].Applied && w.Box.X == 12 && w.Other.X == 60, "Modifications are read from an object keyed by modification ID: " + result.Error);
    }
    static void WholeDocumentIsRejectedAndProtected() {
        var valid = "\"valid\":" + Place("p");
        var cases = new[] {
            ("an array-form modifications of the earlier shape", Head + "[{\"id\":\"valid\"," + Place("p").Substring(1) + "]}"),
            ("an empty array-form modifications", Head + "[]}"),
            ("modifications that is a string", Head + "\"none\"}"),
            ("modifications that is null", Head + "null}"),
            ("a missing modifications", "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"}}"),
            ("an empty modification key", Head + "{" + valid + ",\"\":" + Place("q") + "}}"),
            ("a modification key of only whitespace", Head + "{" + valid + ",\" \\t\":" + Place("q") + "}}"),
            ("a modification value that is an array", Head + "{" + valid + ",\"list\":[]}}"),
            ("a modification value that is a number", Head + "{" + valid + ",\"number\":42}}"),
            ("a modification value that is null", Head + "{" + valid + ",\"nothing\":null}}"),
            ("a repeated modification key", Head + "{" + valid + ",\"valid\":" + Place("q") + "}}"),
            ("a repeated property name inside a modification", Head + "{" + valid + ",\"twice\":" + Place("q").Replace("\"mode\":\"fixed\"", "\"mode\":\"fixed\",\"mode\":\"fixed\"") + "}}"),
            ("a repeated property name deep inside unknown content", "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"future\":{\"list\":[{\"x\":{\"y\":1,\"y\":2}}]},\"modifications\":{" + valid + "}}"),
        };
        foreach (var (scenario, text) in cases) {
            var w = new World();
            var engine = new PatchEngine(w.Scene);
            var result = engine.Apply(text);
            Assert(!String.IsNullOrEmpty(result.Error) && result.MapEditorOnly && result.Outcomes.Count == 0, scenario + ": the whole document is rejected as structurally invalid without individual outcomes: " + result.Error);
            Assert(w.Scene.ResolveCalls == 0 && w.Scene.MutationCalls == 0 && w.Untouched, scenario + ": no operation runs, including the valid modification");
            var document = PatchDocument.Load(text);
            var wrote = true;
            try { document.Write(); } catch (InvalidOperationException) { wrote = false; }
            Assert(!document.CanWrite && document.Error != null && !wrote, scenario + ": the document is not writable");
            var store = new Store { Patch = text }; var editor = new NativeMapEditor();
            var session = new PatchEditingSession(engine, editor, store, "map"); editor.Session = session;
            Assert(session.Blocked != null && session.Blocked.Contains("protected from overwrite"), scenario + ": scene patch editing reports the protected document: " + session.Blocked);
            var edit = session.Edit(P, new Placement(new[] { 130.0, 2.0, 3.0 }, new[] { 0.0, 0.0, 0.0, 1.0 }), null);
            Assert(!edit.Applied && w.Untouched && editor.Save() == null && store.PatchWrites == 0 && ReferenceEquals(store.Patch, text), scenario + ": an edit is refused and a Save leaves the file's text as it is");
        }
        Assert(PatchDocument.Load(Head + "[]}").Error.Contains("keyed by modification ID"), "An array-form document is told what modifications must be: " + PatchDocument.Load(Head + "[]}").Error);
    }
    static void ModificationFaultsFailOnlyTheirModification() {
        var addition = "{\"additions\":{\"shared\":{" + Ranges + "}}}";
        var cases = new[] {
            ("a crossingCoverage that is an array", Cover("p", "[]")),
            ("a crossingCoverage that is a string", Cover("p", "\"all\"")),
            ("a crossingCoverage that is null", Cover("p", "null")),
            ("an empty crossingCoverage", Cover("p", "{}")),
            ("circuits that is an array", Cover("p", "{\"circuits\":[]}")),
            ("additions that is a number", Cover("p", "{\"additions\":7}")),
            ("an empty circuits", Cover("p", "{\"circuits\":{}}")),
            ("an empty additions", Cover("p", "{\"circuits\":{\"Pipr\":{\"detach\":true}},\"additions\":{}}")),
            ("an empty circuit key", Cover("p", "{\"circuits\":{\"\":{\"detach\":true}}}")),
            ("an addition key of only whitespace", Cover("p", "{\"additions\":{\" \":{" + Ranges + "}}}")),
            ("a circuit entry that is not an object", Cover("p", "{\"circuits\":{\"Pipr\":true}}")),
        };
        foreach (var (scenario, bad) in cases) {
            var w = new World();
            var text = Head + "{\"bad\":" + bad + ",\"good\":" + Place("q", 160) + "}}";
            var engine = new PatchEngine(w.Scene);
            var result = engine.Apply(text);
            Assert(result.Error == null && result.Outcomes.Count == 2, scenario + ": the document itself is read: " + result.Error);
            var outcome = result.Outcomes["bad"];
            Assert(!outcome.Applied && outcome.Cause == FailureCause.InvalidDefinition && !outcome.MapEditorOnly && outcome.Error.StartsWith("Invalid relationship", StringComparison.Ordinal) && w.Untouched,
                scenario + ": the modification fails as an invalid definition before any mutation: " + outcome.Error);
            Assert(result.Outcomes["good"].Applied && w.Other.X == 60, scenario + ": the other modification still applies");
            AssertPreserved(w, engine, text, scenario);
        }

        // An addition ID names one addition in its document: two modifications that use it both fail, the third applies.
        var shared = new World();
        var sharing = new PatchEngine(shared.Scene).Apply(Head + "{\"first\":" + Cover("p", addition) + ",\"second\":" + Cover("q", addition) + ",\"third\":" + Place("q", 160) + "}}");
        Assert(!sharing.Outcomes["first"].Applied && !sharing.Outcomes["second"].Applied && sharing.Outcomes["first"].Error.Contains("more than one modification") && sharing.Outcomes["second"].Error.Contains("more than one modification")
            && sharing.Outcomes["first"].Cause == FailureCause.InvalidDefinition && sharing.Outcomes["third"].Applied && shared.Untouched, "An addition ID that two modifications use fails both: " + sharing.Outcomes["first"].Error);

        // A blank addition key is no ID: two modifications that each hold one fail for the empty key, not for sharing an ID.
        var blank = new World();
        var blankAddition = "{\"additions\":{\" \":{" + Ranges + "}}}";
        var blanks = new PatchEngine(blank.Scene).Apply(Head + "{\"first\":" + Cover("p", blankAddition) + ",\"second\":" + Cover("q", blankAddition) + ",\"third\":" + Place("q", 160) + "}}");
        foreach (var key in new[] { "first", "second" })
            Assert(!blanks.Outcomes[key].Applied && blanks.Outcomes[key].Cause == FailureCause.InvalidDefinition && blanks.Outcomes[key].Error.Contains("a key of additions is empty") && !blanks.Outcomes[key].Error.Contains("more than one modification"),
                "A whitespace-only addition key in two modifications gets the empty-key diagnosis: " + blanks.Outcomes[key].Error);
        Assert(blanks.Outcomes["third"].Applied && blank.Untouched, "The modification beside them still applies");

        // A modification is named by its key. A leftover id member is content of the earlier shape: unsupported, diagnosed, preserved.
        var left = new World();
        var leftover = Head + "{\"old-style\":{\"id\":\"old-style\"," + Place("p").Substring(1) + ",\"good\":" + Place("q", 160) + "}}";
        var leftEngine = new PatchEngine(left.Scene);
        var applied = leftEngine.Apply(leftover);
        var failed = applied.Outcomes["old-style"];
        Assert(applied.Error == null && !failed.Applied && failed.MapEditorOnly && failed.Error.StartsWith("Unsupported modification content", StringComparison.Ordinal) && left.Untouched, "A modification that carries an id member fails as unsupported content: " + failed.Error);
        Assert(failed.Error.Contains("modification old-style carries an id member") && failed.Error.Contains("The ID of a modification is its key, old-style"), "The diagnosis names the key and says the ID is the key: " + failed.Error);
        Assert(applied.Outcomes["good"].Applied && left.Other.X == 60, "The modification beside a leftover id still applies");
        AssertPreserved(left, leftEngine, leftover, "a leftover id member");
    }
    // Scene patch editing opens the document, edits the valid modification and saves; the failed modification is written back with the content it had.
    static void AssertPreserved(World w, PatchEngine engine, string text, string scenario) {
        var store = new Store { Patch = text }; var editor = new NativeMapEditor();
        store.Info = store.Info.Replace("\"rr/game >=0.6.2\"", "\"rr/game >=0.6.2\",\"crankyant/scene-patcher >=0.5.0\"");
        var session = new PatchEditingSession(engine, editor, store, "map"); editor.Session = session;
        Assert(session.Blocked == null, scenario + ": the document opens in scene patch editing: " + session.Blocked);
        var key = JObject.Parse(text)["modifications"].Children<JProperty>().First().Name;
        var edit = session.Edit(Q, new Placement(new[] { 170.0, 2.0, 3.0 }, new[] { 0.0, 0.0, 0.0, 1.0 }), null);
        Assert(edit.Applied && w.Other.X == 70 && editor.Save() == null && store.PatchWrites == 1, scenario + ": the valid modification is edited and saved: " + edit.Error);
        var saved = JObject.Parse(store.Patch)["modifications"];
        Assert(JToken.DeepEquals(saved[key], JObject.Parse(text)["modifications"][key]) && (double)saved["good"]["placement"]["position"][0] == 170, scenario + ": the failed modification is preserved through the Save: " + saved[key]);
    }
}
