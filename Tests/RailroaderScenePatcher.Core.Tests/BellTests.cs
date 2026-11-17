// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// Controller-bound crossing bells through whole scene patch files: a bell is a separate player beneath the crossing
// controller, built from a bell definition of the content library, observing the controller's bell key and holding its own
// loaded audio. The production BellPattern judges every definition.
static class BellTests {
    const string CrossingPath = "World/Crossings/Bryson Everett", OtherPath = "World/Crossings/Whittier", SignalPath = CrossingPath + "/Signal 1";
    const string CrossingBell = "rr/bell-uss-teardrop-crossing-rh";
    static readonly TargetIdentity Crossing = new TargetIdentity("BushnellWhittier", CrossingPath, CrossingCoverageScene.ComponentType, CrossingCoverageScene.Structure);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Target(string path = CrossingPath) => "\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"" + CrossingCoverageScene.ComponentType + "\",\"structure\":\"" + CrossingCoverageScene.Structure + "\"}";
    static string N(double value) => value.ToString(CultureInfo.InvariantCulture);
    static string Bell(string id, string definition = CrossingBell, double x = 10, string pattern = EquipmentAddition.BellPattern, string source = null) =>
        "\"" + id + "\":{\"pattern\":\"" + pattern + "\",\"source\":" + (source ?? "{\"definition\":\"" + definition + "\"}") + ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + N(x) + ",0,7],\"rotation\":[0,0,0,1]}}";
    static string Light(string id, string path = SignalPath, double x = 20) =>
        "\"" + id + "\":{\"pattern\":\"" + EquipmentAddition.LightGatePattern + "\",\"source\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + N(x) + ",0,7],\"rotation\":[0,0,0,1]}}";
    static string Equip(string id, string additions, string path = CrossingPath) => "\"" + id + "\":{" + Target(path) + ",\"relationships\":{\"crossingEquipment\":{" + additions + "}},\"dependsOn\":[]}";
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + items + "}}";
    sealed class Fixture {
        public FakeCrossingWorld Coverage = new FakeCrossingWorld();
        public FakeEquipmentWorld World = new FakeEquipmentWorld();
        public FakeCrossing Crossing = new FakeCrossing(), Other = new FakeCrossing();
        public CrossingCoverageScene Scene;
        public Fixture() {
            Crossing.Circuits.Add(new FakeCircuit { Spans = new[] { Coverage.Span("Pcyi", "Sold", 308, 550), Coverage.Span("Pipr", "Sold", 292, 308), Coverage.Span("Pws9", "Sold", 292, 50) } });
            Other.Circuits.Add(new FakeCircuit { Spans = new[] { Coverage.Span("Qa", "Sother", 1, 5), Coverage.Span("Qi", "Sother", 5, 6), Coverage.Span("Qb", "Sother", 6, 9) } });
            Coverage.Objects[CrossingPath] = new List<object> { Crossing }; Coverage.Objects[OtherPath] = new List<object> { Other };
            World.Device(SignalPath); World.Bell();
            Scene = new CrossingCoverageScene(Coverage, Capability.Unchecked, World);
        }
        public List<FakeEquipmentWorld.Added> Bells => World.Live(Crossing).Where(a => a.Bell != null).ToList();
        public int Observers => World.All.Where(a => a.Bell != null && !a.Destroyed).Sum(a => a.Observers);
        public bool Unchanged => !World.Live(Crossing).Any() && !World.Live(Other).Any() && World.AudioReferences == 0 && World.PendingLoads == 0;
    }
    public static void Run() {
        ABellIsBuiltFromItsDefinitionAndBoundToTheController();
        EligibilityFollowsThePatternAcrossSounds();
        MissingAmbiguousAndNonconformingSourcesChangeNothing();
        InvalidBellContentIsRefused();
        PreflightRefusalBuildsNoBell();
        ConstructionAndLoadFailuresReleaseTheBell();
        IncompleteCleanupGuardsRetry();
        AudioLivesAsLongAsItsBell();
        EditingOffersBellsAndUndoRedoRemoveThem();
        SaveAndReloadReproduceOneBell();
        Console.WriteLine("PASS: controller-bound crossing bell eligibility, construction, audio lifetime, refusal, restoration and scene patch editing");
    }
    static void ABellIsBuiltFromItsDefinitionAndBoundToTheController() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        var result = engine.Apply(Doc(Equip("bells", Bell("bell"))), "author/pack");
        Assert(result.Error == null && result.Outcomes["bells"].Applied, "A conforming bell definition builds a bell: " + result.Error + result.Outcomes["bells"].Error);
        Assert(f.Scene.EquipmentStatus(f.Crossing, "author/pack", "bell") == "audio loading" && f.World.AudioReferences == 0, "The bell's audio loads after the apply");
        f.World.FinishLoads();
        var bell = f.Bells.Single();
        Assert(ReferenceEquals(bell.Controller, f.Crossing) && bell.Position.SequenceEqual(new double[] { 10, 0, 7 }) && bell.Bell.Identifier == CrossingBell, "One bell is owned by the intended controller at the mod's placement, from the named definition");
        Assert(bell.Record.Scope == "author/pack" && bell.Record.Id == "bell" && bell.Record.Pattern == EquipmentAddition.BellPattern && bell.Record.Source.Definition == CrossingBell, "The bell keeps its supplying-mod-scoped identity, pattern and source");
        Assert(bell.Observers == 1 && bell.AudioLoaded && f.World.AudioReferences == 1, "The bell observes its controller's bell key once and holds its own loaded audio");
        Assert(f.Scene.EquipmentStatus(f.Crossing, "author/pack", "bell") == "audio loaded; observing the controller's bell key", "The status names the audio and the binding");
        engine.Apply(Doc(Equip("bells", Bell("bell"))), "author/pack");
        Assert(f.Bells.Count == 1 && f.World.Constructed == 1 && f.Observers == 1 && f.World.AudioReferences == 1, "Repeating the application adds no player, observer or audio reference");
        var fresh = new PatchEngine(new CrossingCoverageScene(f.Coverage, Capability.Unchecked, f.World));
        fresh.Apply(Doc(Equip("bells", Bell("bell"))), "author/pack");
        Assert(f.Bells.Count == 1 && f.World.Constructed == 1 && f.Observers == 1, "A new engine in the same world adopts the bell instead of duplicating it");
        engine.Apply(Doc(Equip("bells", Bell("bell", x: 12))), "author/pack");
        Assert(f.Bells.Count == 1 && f.World.Constructed == 1 && f.Bells[0].Position[0] == 12 && f.World.AudioReferences == 1, "A changed placement moves the bell and keeps its audio");
        f.World.Bell("rr/bell-modded", b => b.Facts.Audio = "rr/other");
        engine.Apply(Doc(Equip("bells", Bell("bell", "rr/bell-modded", 12))), "author/pack");
        f.World.FinishLoads();
        Assert(f.Bells.Count == 1 && f.World.Constructed == 2 && f.Bells[0].Bell.Identifier == "rr/bell-modded" && f.World.AudioReferences == 1 && f.Observers == 1, "Another definition rebuilds the bell and releases the old audio and observer");
        Assert(f.Crossing.Circuits.Count == 1 && f.Crossing.Position.SequenceEqual(new double[] { 5, 0, 7 }), "A bell neither moves the crossing nor changes its coverage");
    }
    // Definitions with other sounds, index counts and volumes qualify through the same definition and loading mechanism; each
    // rule that fails names its concrete gap.
    static void EligibilityFollowsThePatternAcrossSounds() {
        var f = new Fixture();
        f.World.Bell("rr/bell-loco-brass", b => { b.Name = "Brass Locomotive Bell"; b.Facts.Audio = "rr/bell-brass"; b.Facts.Indexes = new double[] { 0.2, 0.9, 1.7 }; b.Facts.Volume = 0.3; });
        f.World.Bell("modder/bell-unexamined", b => { b.Name = null; b.Facts.Audio = "modder/clang"; b.Facts.Indexes = new double[0]; b.Facts.Volume = 1.4; });
        var rejected = new Dictionary<string, KeyValuePair<Action<FakeBell>, string>> {
            ["horn"] = R(b => b.Facts.Kind = "Horn", "it is a Horn definition, not a bell definition"),
            ["silent"] = R(b => b.Facts.AudioNamed = false, "its definition does not specify an audio asset"),
            ["unresolved"] = R(b => { b.Facts.Audio = "rr/missing-clip"; b.Facts.AudioResolves = false; }, "its audio asset rr/missing-clip cannot be found in the content library"),
            ["mute"] = R(b => b.Facts.Volume = 0, "its volume 0 must be a finite number greater than 0"),
            ["nan"] = R(b => b.Facts.Volume = Double.NaN, "must be a finite number greater than 0"),
            ["descending"] = R(b => b.Facts.Indexes = new double[] { 1, 0.5 }, "its audio indexes must be finite times greater than 0, in strictly increasing order"),
            ["negative"] = R(b => b.Facts.Indexes = new double[] { -0.1, 0.5 }, "its audio indexes must be finite times greater than 0, in strictly increasing order"),
            ["zero index"] = R(b => b.Facts.Indexes = new double[] { 0, 0.5 }, "its audio indexes must be finite times greater than 0, in strictly increasing order"),
            ["destination owner"] = R(b => b.Facts.DestinationKeyValueObjects = 0, "the destination crossing controller has no key-value objects, so it cannot address the bell"),
            ["destination owners"] = R(b => b.Facts.DestinationKeyValueObjects = 2, "has several key-value objects"),
        };
        foreach (var r in rejected) f.World.Bell("test/" + r.Key, r.Value.Key);
        var candidates = f.Scene.EquipmentCandidates(f.Crossing).Where(c => c.Source.IsDefinition).ToList();
        Assert(candidates.All(c => c.Pattern == EquipmentAddition.BellPattern) && candidates.Count == 3 + rejected.Count, "Every bell definition is offered with the bell pattern");
        Assert(candidates.Where(c => c.Eligible).Select(c => c.Source.Definition).OrderBy(d => d, StringComparer.Ordinal).SequenceEqual(new[] { "modder/bell-unexamined", "rr/bell-loco-brass", CrossingBell }),
            "Every conforming definition is eligible, whatever its sound: " + String.Join(", ", candidates.Where(c => c.Eligible).Select(c => c.Source.Definition)));
        foreach (var r in rejected) {
            var candidate = candidates.Single(c => c.Source.Definition == "test/" + r.Key);
            Assert(!candidate.Eligible && candidate.Problem.Contains(r.Value.Value), "Rule " + r.Key + " names its concrete gap: " + candidate.Problem);
        }
        Assert(candidates.Single(c => c.Source.Definition == CrossingBell).Label == "Crossing Bell (" + CrossingBell + ")" && candidates.Single(c => c.Source.Definition == "modder/bell-unexamined").Label == "modder/bell-unexamined",
            "A bell reads as its name and identifier, or its identifier alone");
        Assert(f.Scene.EquipmentCandidates(f.Crossing).Single(c => c.Source.Path == SignalPath).Pattern == EquipmentAddition.LightGatePattern, "A loaded device keeps the light/gate pattern");
        var result = new PatchEngine(f.Scene).Apply(Doc(Equip("bells", Bell("brass", "rr/bell-loco-brass") + "," + Bell("clang", "modder/bell-unexamined", 30) + "," + Bell("native", CrossingBell, 40))));
        f.World.FinishLoads();
        Assert(result.Outcomes["bells"].Applied && f.Bells.Count == 3 && f.World.AudioReferences == 3 && f.Observers == 3, "Unexamined definitions conforming to the pattern build bells without a per-sound list: " + result.Outcomes["bells"].Error);
    }
    static KeyValuePair<Action<FakeBell>, string> R(Action<FakeBell> breaks, string reason) => new KeyValuePair<Action<FakeBell>, string>(breaks, reason);
    static void MissingAmbiguousAndNonconformingSourcesChangeNothing() {
        var cases = new Dictionary<string, Func<Fixture, string>> {
            ["missing"] = f => Bell("bell", "rr/bell-not-installed"),
            ["ambiguous"] = f => { f.World.Bell(); return Bell("bell"); },
            ["nonconforming"] = f => { f.World.Bell("rr/horn-leslie", b => b.Facts.Kind = "Horn"); return Bell("bell", "rr/horn-leslie"); },
            ["unavailable audio"] = f => { f.World.Bell("rr/bell-unpacked", b => b.Facts.AudioResolves = false); return Bell("bell", "rr/bell-unpacked"); },
        };
        foreach (var c in cases) {
            var f = new Fixture();
            var addition = c.Value(f);
            var result = new PatchEngine(f.Scene).Apply(Doc(Equip("bad", addition)));
            var error = result.Outcomes["bad"].Error ?? "";
            Assert(!result.Outcomes["bad"].Applied && f.World.Mutations == 0 && f.Unchanged, c.Key + " bell source is refused before any change: " + error);
            if (c.Key == "missing") Assert(error.Contains("not available in the content library") && error.Contains("will not substitute another sound"), "A missing definition substitutes no other sound: " + error);
            if (c.Key == "ambiguous") Assert(error.Contains("ambiguous"), "An ambiguous definition is named: " + error);
            if (c.Key == "nonconforming") Assert(error.Contains("does not conform to " + EquipmentAddition.BellPattern) && error.Contains("not a bell definition"), "A nonconforming definition gives its reason: " + error);
            if (c.Key == "unavailable audio") Assert(error.Contains("cannot be found in the content library"), "Unavailable audio gives its reason: " + error);
        }
        // A bell whose source names a scene object is no definition: it is invalid, so no scene is resolved or loaded for it.
        var g = new Fixture();
        var scene = new PatchEngine(g.Scene).Apply(Doc(Equip("bad", Bell("bell", source: "{\"scene\":\"UnloadedScene\",\"path\":\"World/Bell\"}"))));
        Assert(!scene.Outcomes["bad"].Applied && scene.Outcomes["bad"].Error.Contains("identifier of a bell definition") && g.World.Mutations == 0 && g.Unchanged, "A bell from a scene path is refused without resolving a scene: " + scene.Outcomes["bad"].Error);
        var light = new PatchEngine(g.Scene).Apply(Doc(Equip("bad", Bell("lamp", pattern: EquipmentAddition.LightGatePattern))));
        Assert(!light.Outcomes["bad"].Applied && light.Outcomes["bad"].Error.Contains("needs a source with scene and path") && g.Unchanged, "A light or gate from a definition is refused: " + light.Outcomes["bad"].Error);
    }
    static void InvalidBellContentIsRefused() {
        foreach (var invalid in new[] { Bell("bell", source: "{}"), Bell("bell", source: "{\"definition\":\"\"}"), Bell("bell", source: "{\"definition\":5}"),
                Bell("bell", source: "{\"definition\":\"" + CrossingBell + "\",\"scene\":\"BushnellWhittier\"}"), Bell("bell", source: "\"" + CrossingBell + "\""),
                Bell("bell").Replace("\"mode\":\"fixed\"", "\"mode\":\"trackFollowing\"") }) {
            var f = new Fixture();
            var result = new PatchEngine(f.Scene).Apply(Doc(Equip("bad", invalid)));
            Assert(!result.Outcomes["bad"].Applied && result.Outcomes["bad"].Error.StartsWith("Invalid", StringComparison.Ordinal) && f.World.Mutations == 0 && f.Unchanged, "Invalid bell content changes nothing: " + invalid + " -> " + result.Outcomes["bad"].Error);
        }
        var g = new Fixture();
        var unknown = new PatchEngine(g.Scene).Apply(Doc(Equip("future", Bell("bell", source: "{\"definition\":\"" + CrossingBell + "\",\"pitch\":2}"))));
        Assert(!unknown.Outcomes["future"].Applied && unknown.Outcomes["future"].Error.StartsWith("Unsupported", StringComparison.Ordinal) && g.World.Mutations == 0, "Unknown source content is unsupported and preserved: " + unknown.Outcomes["future"].Error);
    }
    static void PreflightRefusalBuildsNoBell() {
        var f = new Fixture();
        var result = new PatchEngine(f.Scene).Apply(Doc(Equip("pair", Bell("bell") + "," + Light("lamp", CrossingPath + "/Missing"))));
        Assert(!result.Outcomes["pair"].Applied && f.World.Mutations == 0 && f.Unchanged, "A bell beside an unresolved light is not built: " + result.Outcomes["pair"].Error);
        var both = new PatchEngine(f.Scene).Apply(Doc(Equip("pair", Bell("bell") + "," + Light("lamp"))));
        Assert(both.Outcomes["pair"].Applied && f.World.Live(f.Crossing).Count() == 2 && f.Bells.Count == 1, "A bell and a light or gate apply together: " + both.Outcomes["pair"].Error);
    }
    // A construction failure after the bell restores the captured equipment, and nothing the failed bell held stays
    // referenced. A load fails later than the apply, as in the game: the apply succeeds, the bell stays silent, a warning
    // names it and its state says why, and an edit that keeps it is refused until it is removed or replaced.
    static void ConstructionAndLoadFailuresReleaseTheBell() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        engine.Apply(Doc(Equip("first", Bell("bell"))), "author/pack");
        f.World.FinishLoads();
        f.World.FailConstructAfter = 1;
        var result = engine.Apply(Doc(Equip("second", Bell("bell", x: 15) + "," + Bell("east", x: 20) + "," + Light("lamp", x: 25))), "author/pack");
        Assert(!result.Outcomes["second"].Applied && result.Outcomes["second"].Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal), "A construction failure is restored: " + result.Outcomes["second"].Error);
        Assert(f.Bells.Single().Record.Id == "bell" && f.Bells[0].Position[0] == 10 && f.World.AudioReferences == 1 && f.Observers == 1, "The captured bell returns; the partial bell releases its audio and observer");
        var g = new Fixture();
        var loading = new PatchEngine(g.Scene);
        var load = loading.Apply(Doc(Equip("bells", Bell("bell"))), "author/pack");
        Assert(load.Outcomes["bells"].Applied && g.World.Warnings.Count == 0, "The apply succeeds while the audio loads: " + load.Outcomes["bells"].Error);
        g.World.FailLoads = true; g.World.FinishLoads();
        Assert(g.Scene.EquipmentStatus(g.Crossing, "author/pack", "bell") == "audio failed to load" && g.World.AudioReferences == 0, "A failed load leaves a silent bell whose state says so");
        Assert(g.World.Warnings.Count == 1 && g.World.Warnings[0].StartsWith("author/pack bell", StringComparison.Ordinal) && g.World.Warnings[0].Contains("the asset bundle failed"), "One warning names the bell and the reason: " + String.Join("|", g.World.Warnings));
        var kept = loading.Apply(Doc(Equip("bells", Bell("bell", x: 14))), "author/pack");
        Assert(!kept.Outcomes["bells"].Applied && kept.Outcomes["bells"].Error.Contains("its audio failed to load") && kept.Outcomes["bells"].Error.Contains("Remove or replace that bell. You can still edit crossing coverage"),
            "The next edit that keeps the bell is refused with what to do: " + kept.Outcomes["bells"].Error);
        var h = new Fixture();
        h.World.Bell("rr/bell-short", b => b.ClipSeconds = 1);
        var shortClip = new PatchEngine(h.Scene).Apply(Doc(Equip("bells", Bell("bell", "rr/bell-short"))), "author/pack");
        h.World.FinishLoads();
        Assert(shortClip.Outcomes["bells"].Applied && h.World.Warnings.Single().Contains("does not split its clip") && h.World.AudioReferences == 0, "A clip its indexes do not fit is warned about and holds no audio: " + String.Join("|", h.World.Warnings));
    }
    static void IncompleteCleanupGuardsRetry() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        f.World.FailConstructAfter = 1; f.World.StickyDestroy = true;
        var result = engine.Apply(Doc(Equip("pair", Bell("bell") + "," + Bell("east", x: 20))), "author/pack");
        Assert(!result.Outcomes["pair"].Applied && result.Outcomes["pair"].Error.Contains("could not be verified"), "A bell that cannot be released reports incomplete cleanup: " + result.Outcomes["pair"].Error);
        f.World.StickyDestroy = false;
        var retry = engine.Apply(Doc(Equip("other", Bell("north", x: 30))), "author/pack");
        Assert(!retry.Outcomes["other"].Applied && retry.Outcomes["other"].Error.Contains("could not be verified") && f.Bells.Count == 1, "A retry on the uncertain controller is blocked: " + retry.Outcomes["other"].Error);
    }
    // A bell holds its loaded audio from the completed load until it is released, and a load still pending when it is
    // released is cancelled. Releasing the crossing's state releases every bell's audio.
    static void AudioLivesAsLongAsItsBell() {
        var f = new Fixture();
        var engine = new PatchEngine(f.Scene);
        var result = engine.Apply(Doc(Equip("bells", Bell("bell"))), "author/pack");
        Assert(result.Outcomes["bells"].Applied && f.World.PendingLoads == 1 && f.World.AudioReferences == 0, "A bell is built while its audio loads: " + result.Outcomes["bells"].Error);
        Assert(f.Scene.EquipmentStatus(f.Crossing, "author/pack", "bell") == "audio loading", "Its status says the audio loads");
        f.World.FinishLoads();
        Assert(f.World.AudioReferences == 1 && f.World.PendingLoads == 0, "The completed load holds one audio reference");
        engine.Apply(Doc(Equip("bells", Bell("bell") + "," + Bell("pending", x: 30))), "author/pack");
        Assert(f.World.PendingLoads == 1 && f.World.AudioReferences == 1 && f.Bells.Count == 2, "A second bell loads its own audio");
        f.Scene.Release(f.Crossing);
        Assert(f.World.AudioReferences == 0 && f.World.PendingLoads == 0 && f.Observers == 0 && !f.World.Live(f.Crossing).Any(), "Releasing the crossing releases every bell's audio and observer and cancels a pending load");
        var g = new Fixture();
        var later = new PatchEngine(g.Scene);
        later.Apply(Doc(Equip("bells", Bell("bell"))), "author/pack");
        g.World.FailLoads = true; g.World.FinishLoads();
        Assert(g.Scene.EquipmentStatus(g.Crossing, "author/pack", "bell") == "audio failed to load", "A load that fails later shows in the status");
        var refused = later.Apply(Doc(Equip("bells", Bell("bell", x: 14))), "author/pack");
        Assert(!refused.Outcomes["bells"].Applied && refused.Outcomes["bells"].Error.Contains("its audio failed to load") && refused.Outcomes["bells"].Error.Contains("Remove or replace that bell"), "Edits that keep a bell whose audio failed are refused: " + refused.Outcomes["bells"].Error);
        g.Scene.Dispose();
        Assert(g.World.AudioReferences == 0 && g.Observers == 0, "Ending the state releases the failed bell");
        // A bell whose audio fails after the apply, as a load in the game does, can still be removed or replaced in scene
        // patch editing; a light beside it waits until it is gone.
        var (h, session, mapEditor, store) = Session();
        Assert(session.AddEquipment(Crossing, NewBell()).Applied, "The bell is built while its audio loads");
        h.World.FailLoads = true; h.World.FinishLoads(); h.World.FailLoads = false;
        var light = session.AddEquipment(Crossing, new EquipmentAddition("lamp", EquipmentAddition.LightGatePattern, new EquipmentSource("BushnellWhittier", SignalPath), new Placement(new double[] { 20, 0, 7 }, new double[] { 0, 0, 0, 1 })));
        Assert(!light.Applied && light.Error.Contains("its audio failed to load"), "An edit that keeps the failed bell is refused: " + light.Error);
        var replaced = session.AddEquipment(Crossing, NewBell(definition: "rr/bell-replacement"));
        Assert(!replaced.Applied && replaced.Error.Contains("not available in the content library"), "A replacement needs an available definition: " + replaced.Error);
        h.World.Bell("rr/bell-replacement");
        replaced = session.AddEquipment(Crossing, NewBell(definition: "rr/bell-replacement"));
        h.World.FinishLoads();
        Assert(replaced.Applied && h.Bells.Single().Bell.Identifier == "rr/bell-replacement" && h.Bells[0].AudioLoaded, "A failed bell is replaced by another definition: " + replaced.Error);
        Assert(session.AddEquipment(Crossing, NewBell(30, id: "second")).Applied, "A second bell is built");
        h.World.FailLoads = true; h.World.FinishLoads();
        var removed = session.RemoveEquipment(Crossing, "second");
        Assert(removed.Applied && h.Bells.Single().Record.Id == "bell" && h.World.AudioReferences == 1, "A bell whose audio failed is removed: " + removed.Error);
    }
    static (Fixture f, PatchEditingSession session, NativeMapEditor mapEditor, Store store) Session(string patch = null, Fixture fixture = null) {
        var f = fixture ?? new Fixture();
        var store = new Store { Patch = patch }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(f.Scene), mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        return (f, session, mapEditor, store);
    }
    static EquipmentAddition NewBell(double x = 10, string definition = CrossingBell, string id = "bell") =>
        new EquipmentAddition(id, EquipmentAddition.BellPattern, EquipmentSource.ForDefinition(definition), new Placement(new[] { x, 0, 7 }, new double[] { 0, 0, 0, 1 }));
    static void EditingOffersBellsAndUndoRedoRemoveThem() {
        var (f, session, mapEditor, store) = Session();
        var offered = session.EquipmentSources(Crossing).Where(c => c.Pattern == EquipmentAddition.BellPattern).ToList();
        Assert(offered.Count == 1 && offered[0].Eligible && offered[0].Source.Definition == CrossingBell, "Scene patch editing offers the bell definitions");
        var preview = session.PreviewEquipment(Crossing, NewBell(), null);
        Assert(preview.Error == null && preview.Lines.Single().StartsWith("Add author/pack bell: " + EquipmentAddition.BellPattern + " from definition " + CrossingBell, StringComparison.Ordinal) && f.World.Mutations == 0, "The preview names the bell and changes nothing: " + preview.Error + String.Join("|", preview.Lines));
        var added = session.AddEquipment(Crossing, NewBell());
        f.World.FinishLoads();
        Assert(added.Applied && f.Bells.Count == 1 && f.World.AudioReferences == 1, "Scene patch editing builds the bell: " + added.Error);
        var item = (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"].Single().First;
        var entry = (JObject)item["relationships"]["crossingEquipment"]["bell"];
        Assert(entry.Properties().Select(p => p.Name).SequenceEqual(new[] { "pattern", "source", "placement" }) && (string)entry["pattern"] == EquipmentAddition.BellPattern
            && ((JObject)entry["source"]).Properties().Select(p => p.Name).SequenceEqual(new[] { "definition" }) && (string)entry["source"]["definition"] == CrossingBell && (string)item["target"]["path"] == CrossingPath
            && (string)item["target"]["component"] == CrossingCoverageScene.ComponentType, "The scene patch file records the bell definition, the pattern and the controller it is bound to: " + item);
        Assert(session.AddEquipment(Crossing, NewBell(14)).Applied && f.World.Constructed == 1 && f.Bells[0].Position[0] == 14, "Re-adding the ID moves the bell");
        // Undo and Redo of a bell edit name the crossing, so the window reads the crossing and its open bell form again.
        var replayed = new List<TargetIdentity>();
        session.Replayed = identity => replayed.Add(identity);
        mapEditor.Undo();
        var undone = session.EquipmentEntries(Crossing).Single(e => e.Addition.Id == "bell").Addition;
        Assert(replayed.Count == 1 && replayed[0].Path == CrossingPath && replayed[0].Component == CrossingCoverageScene.ComponentType && undone.Placement.Position[0] == 10 && f.Bells[0].Position[0] == 10,
            "Undo of the move names the crossing, and the scene patch file and scene hold the earlier placement");
        mapEditor.Redo();
        Assert(replayed.Count == 2 && replayed[1].Path == CrossingPath && session.EquipmentEntries(Crossing).Single().Addition.Placement.Position[0] == 14 && f.Bells[0].Position[0] == 14, "Redo names the crossing and moves the bell again");
        session.Replayed = null;
        Assert(session.RemoveEquipment(Crossing, "bell").Applied && f.Bells.Count == 0 && f.World.AudioReferences == 0 && f.Observers == 0, "Removing the bell releases its player, observer and audio");
        mapEditor.Undo(); f.World.FinishLoads(); Assert(f.Bells.Count == 1 && f.Bells[0].Position[0] == 14 && f.World.AudioReferences == 1 && f.Observers == 1, "Undo of the removal rebuilds the bell with its audio and binding");
        mapEditor.Undo(); Assert(f.Bells.Single().Position[0] == 10, "Undo of the move restores its placement");
        mapEditor.Undo(); Assert(f.Bells.Count == 0 && f.World.AudioReferences == 0 && f.Observers == 0, "Undo of the addition releases the bell");
        mapEditor.Redo(); f.World.FinishLoads(); Assert(f.Bells.Count == 1 && f.World.AudioReferences == 1 && f.Observers == 1, "Redo rebuilds the bell");
        Assert(session.Remove(Crossing).Applied && f.Bells.Count == 0 && f.World.AudioReferences == 0, "Removing the modification releases the bell");
        mapEditor.Undo(); Assert(f.Bells.Count == 1 && f.Observers == 1, "Undo of the modification's removal restores the bell");
        var refused = session.AddEquipment(Crossing, NewBell(definition: "rr/bell-not-installed", id: "other"));
        Assert(!refused.Applied && refused.Error.Contains("not available in the content library") && f.Bells.Count == 1, "A missing definition is refused without a change: " + refused.Error);
        Assert(!f.World.Live(f.Other).Any(), "No edit touched the other crossing");
    }
    static void SaveAndReloadReproduceOneBell() {
        var (f, session, mapEditor, store) = Session();
        session.AddEquipment(Crossing, NewBell());
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0"), "Save raises the dependency to the crossing-equipment version: " + store.Info);
        Assert(store.Patch.Contains("\"definition\": \"" + CrossingBell + "\""), "The saved scene patch file names the bell definition: " + store.Patch);
        var fresh = new Fixture();
        var engine = new PatchEngine(fresh.Scene);
        var reload = engine.Apply(store.Patch, "author/pack");
        fresh.World.FinishLoads();
        Assert(reload.Outcomes.Values.All(o => o.Applied) && fresh.Bells.Count == 1 && fresh.World.AudioReferences == 1 && fresh.Observers == 1, "A reloaded world reproduces the saved bell");
        engine.Apply(store.Patch, "author/pack");
        Assert(fresh.Bells.Count == 1 && fresh.World.Constructed == 1 && fresh.World.AudioReferences == 1 && fresh.Observers == 1, "A repeated world-load callback adds no player, observer or audio reference");
        var (f2, reopened, e2, s2) = Session(store.Patch, fresh);
        Assert(reopened.Blocked == null && reopened.RemoveEquipment(Crossing, "bell").Applied && fresh.Bells.Count == 0 && fresh.World.AudioReferences == 0, "A saved bell reopens for editing in the world it built");
    }
}
