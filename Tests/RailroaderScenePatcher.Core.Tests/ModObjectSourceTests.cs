// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// The source of a mod object: its original when the original is loaded; when the original is missing, the first loaded
// instance with the same model and build, in the fixed order the scene gives, with a warning in the Player log; never
// an instance chosen by its distance. Without an identical instance the mod object is refused with its reason. Targets
// keep the exactly-one rule with no fallback. The station building is the Alarka Jct Station of the base map fixture.
static class ModObjectSourceTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Mod object sources: " + message); }
    const string SceneName = ModObjectTests.SceneName, Station = ModObjectTests.Station, Key = "mod-object-alarka-jct-station", Build = "v1:4:station-build";
    static readonly string Made = ModObjectTests.Made;
    // Where the mod object stands when it is built from the Dillsboro instance: in that instance's parent, under its own name.
    static readonly string BesideDillsboro = "World/Large Scenery/Dillsboro/" + ModObjects.NameOf(ModObjectTests.Made);
    static readonly double[] AtBryson = { 4493, 12, 5415 };
    // Two identical station buildings elsewhere in the map; the fixed order lists Dillsboro first, though Bryson's
    // building stands nearer to the mod object's placement.
    const string Dillsboro = "World/Large Scenery/Dillsboro/Dillsboro Station", Bryson = "World/Large Scenery/Bryson/Bryson Station", Shed = "World/Large Scenery/Bryson/Freight Shed";
    static string Target(string path) => "{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"" + SceneObjects.ComponentType + "\",\"structure\":\"" + SceneObjects.Structure + "\"}";
    static string Source(string path, string build) => build == null ? Target(path) : Target(path).TrimEnd('}') + ",\"build\":\"" + build + "\"}";
    static string ModObject(string key, string source, string made, string build = Build) =>
        "\"" + key + "\":{\"target\":" + Target(made) + ",\"source\":" + Source(source, build) + ",\"placement\":{\"mode\":\"fixed\",\"position\":[4493,12,5415],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
    static string Edit(string key, string path) => "\"" + key + "\":{\"target\":" + Target(path) + ",\"active\":false,\"dependsOn\":[]}";
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";

    // What builds mod objects in the controlled group world and knows the model and build of its objects. The identical
    // instances come in the order of the list, which is the scene's fixed order.
    sealed class Builder : IModObjectScene, IModObjectSources {
        readonly FakeGroupWorld world;
        public readonly List<KeyValuePair<string, FakeGroup>> Loaded = new List<KeyValuePair<string, FakeGroup>>();
        public readonly Dictionary<FakeGroup, string> Builds = new Dictionary<FakeGroup, string>();
        public readonly List<object> BuiltFrom = new List<object>();
        public Builder(FakeGroupWorld world) { this.world = world; }
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) {
            if (world.Objects.TryGetValue(identity.Path, out var standing) && standing.Count != 0) throw new InvalidOperationException(ModObjects.Taken);
            var made = new FakeGroup { Position = new[] { placement.Position[0] - world.Origin, placement.Position[1], placement.Position[2] }, Rotation = (double[])placement.Rotation.Clone() };
            world.Objects[identity.Path] = new List<object> { made };
            BuiltFrom.Add(source);
            return made;
        }
        public void Remove(object modObject) { foreach (var pair in world.Objects.Where(p => p.Value.Contains(modObject)).ToArray()) world.Objects.Remove(pair.Key); }
        public string BuildOf(object source) => source is FakeGroup group && Builds.TryGetValue(group, out var build) ? build : null;
        public IReadOnlyList<SourceInstance> Identical(TargetIdentity source, string build) =>
            Loaded.Where(p => p.Value != null && BuildOf(p.Value) == build).Select(p => new SourceInstance(p.Key, p.Value)).ToArray();
    }
    sealed class World {
        public readonly FakeGroupWorld Groups = new FakeGroupWorld();
        public readonly Builder Builder;
        public readonly PatchEngine Engine;
        public readonly Dictionary<string, FakeGroup> At = new Dictionary<string, FakeGroup>();
        // The loaded map: the named buildings, in the order given, each with its model and build.
        public World(params (string Path, double X, string Build)[] loaded) {
            var scene = new SceneCapabilities(() => Groups.MapIds, null);
            Builder = new Builder(Groups); scene.ModObjectWorld = Builder;
            foreach (var (path, x, build) in loaded) {
                var group = new FakeGroup { Position = new[] { x, 12, 5415 } };
                Groups.Objects[path] = new List<object> { group };
                At[path] = group; Builder.Builds[group] = build;
                Builder.Loaded.Add(new KeyValuePair<string, FakeGroup>(path, group));
            }
            scene.Add(new SceneObjects(Groups));
            Engine = new PatchEngine(scene);
        }
        public bool Stands(string path) => Groups.Objects.TryGetValue(path, out var found) && found.Count == 1;
    }

    internal static void Run() {
        TheOriginalIsTheSourceWhenItIsLoaded();
        AMissingOriginalFallsBackToTheFirstIdenticalInstance();
        WithoutAnIdenticalInstanceTheModObjectIsRefused();
        AFileWithoutTheBuildHasNoFallback();
        TargetsHaveNoFallback();
        PlacingWritesTheBuildOfTheSource();
        CanonicalSaveOrdersTheBuild();
        AFallbackModObjectIsFoundByItsMarkWithItsParts();
        APartCopyStandsBesideTheGroupOfItsSource();
        Console.WriteLine("PASS: mod object sources");
    }

    // A part's copy stands beside the group of the part it is built from: beside its original's group, or, with the
    // original part missing, beside the group of the identical part, never inside that group. A copy so built is as
    // standalone as one built from its original.
    static void APartCopyStandsBesideTheGroupOfItsSource() {
        const string part = Station + "/Mask", other = Dillsboro + "/Mask";
        var made = "World/Large Scenery/Alarka Jct/" + "Mask [mod object author:pack mod-object-mask]";
        string Item() => "\"mod-object-mask\":{\"target\":" + Target(made) + ",\"source\":" + Source(part, Build).Replace(SceneObjects.Structure, ChildObjects.Structure)
            + ",\"placement\":{\"mode\":\"fixed\",\"position\":[4493,12,5415],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var world = new World((other, 1000, Build));
        var result = world.Engine.Apply(Doc(Item()), "author/pack");
        var beside = "World/Large Scenery/Dillsboro/" + ModObjects.NameOf(made);
        Assert(result.Outcomes["mod-object-mask"].Applied && world.Stands(beside) && !world.Stands(Dillsboro + "/" + ModObjects.NameOf(made)),
            "The part's copy stands beside the identical part's group: " + result.Outcomes["mod-object-mask"].Error + " " + String.Join(", ", world.Groups.Objects.Keys));
        Assert(ReferenceEquals(world.Builder.BuiltFrom.Single(), world.At[other]), "It is built from the identical part");
        Assert(ModObjects.StandsIn(made, part, part) == "World/Large Scenery/Alarka Jct" && ModObjects.StandsIn(made, part, other) == "World/Large Scenery/Dillsboro"
            && ModObjects.StandsIn(Made, Station, Dillsboro) == "World/Large Scenery/Dillsboro", "A copy keeps its distance above its source");
    }

    // With its original loaded, the mod object is built from the original, even where an identical instance comes
    // first in the fixed order; the Player log holds no warning.
    static void TheOriginalIsTheSourceWhenItIsLoaded() {
        var world = new World((Dillsboro, 1000, Build), (Station, 733, Build));
        ScenePatchFileResult result = null;
        var log = RecordingLog.Capture(false, () => result = world.Engine.Apply(Doc(ModObject(Key, Station, Made)), "author/pack"));
        Assert(result.Outcomes[Key].Applied && world.Stands(Made), "The mod object stands: " + result.Outcomes[Key].Error);
        Assert(world.Builder.BuiltFrom.Count == 1 && ReferenceEquals(world.Builder.BuiltFrom[0], world.At[Station]), "The mod object is built from its original");
        Assert(!log.PlayerLines.Any(), "The Player log holds nothing: " + String.Join(" | ", log.PlayerLines));
    }

    // With its original missing, the mod object is built from the first identical instance in the fixed order, not
    // from a nearer one, and not from an instance of another build listed before it. The Player log holds one warning
    // that names the mod object, the missing original and the instance it was built from.
    static void AMissingOriginalFallsBackToTheFirstIdenticalInstance() {
        var world = new World((Shed, 4490, "v1:2:shed-build"), (Dillsboro, 1000, Build), (Bryson, 4480, Build));
        ScenePatchFileResult result = null;
        var log = RecordingLog.Capture(false, () => result = world.Engine.Apply(Doc(ModObject(Key, Station, Made)), "author/pack"));
        Assert(result.Outcomes[Key].Applied && world.Stands(BesideDillsboro) && !world.Stands(Made), "The mod object stands beside the identical instance, in its parent: " + result.Outcomes[Key].Error);
        Assert(world.Builder.BuiltFrom.Count == 1 && ReferenceEquals(world.Builder.BuiltFrom[0], world.At[Dillsboro]), "The mod object is built from the first identical instance in the fixed order, not the nearer one at Bryson");
        var warnings = log.At(LogLevel.Warning).ToArray();
        Assert(warnings.Length == 1 && log.PlayerLines.Count() == 1, "The Player log holds one warning: " + String.Join(" | ", log.PlayerLines));
        Assert(warnings[0].Contains(Key) && warnings[0].Contains(Station) && warnings[0].Contains(Dillsboro) && warnings[0].Contains("author/pack"),
            "The warning names the mod object, its scene patch file, the missing original and the instance: " + warnings[0]);
        Assert(warnings[0] == ModObjects.FallbackWarning("author/pack", Key, Station, Dillsboro), "The warning has its wording: " + warnings[0]);
        // Applying the file again builds nothing more and warns no more.
        var again = RecordingLog.Capture(false, () => result = world.Engine.Apply(Doc(ModObject(Key, Station, Made)), "author/pack"));
        Assert(result.Outcomes[Key].Applied && world.Builder.BuiltFrom.Count == 1 && !again.PlayerLines.Any(), "Applying again builds nothing and warns nothing: " + String.Join(" | ", again.PlayerLines));
    }

    // With no identical instance loaded, the mod object is refused with its reason and nothing is built; the other
    // modifications of the scene patch file still apply.
    static void WithoutAnIdenticalInstanceTheModObjectIsRefused() {
        var world = new World((Shed, 4490, "v1:2:shed-build"), (Bryson, 4480, "v1:4:other-station"));
        ScenePatchFileResult result = null;
        var log = RecordingLog.Capture(false, () => result = world.Engine.Apply(Doc(ModObject(Key, Station, Made), Edit("shed", Shed)), "author/pack"));
        var refused = result.Outcomes[Key];
        Assert(!refused.Applied && refused.Error == ModObjects.SourceMissing && refused.Cause == FailureCause.InvalidDefinition && refused.Failure == FailureKind.Refused, "The mod object is refused with its reason: " + refused.Error);
        Assert(world.Builder.BuiltFrom.Count == 0 && !world.Stands(Made), "Nothing is built");
        Assert(result.Outcomes["shed"].Applied && !world.At[Shed].Active, "The other modification applies: " + result.Outcomes["shed"].Error);
        Assert(!log.At(LogLevel.Warning).Any(w => w.Contains("identical")), "No fallback warning is written");
    }

    // A source written without its model and build, such as one placed before the patcher wrote it, has no fallback.
    static void AFileWithoutTheBuildHasNoFallback() {
        var world = new World((Dillsboro, 1000, Build));
        var outcome = world.Engine.Apply(Doc(ModObject(Key, Station, Made, build: null)), "author/pack").Outcomes[Key];
        Assert(!outcome.Applied && outcome.Error == ModObjects.SourceMissing && world.Builder.BuiltFrom.Count == 0, "Without a build the missing original refuses the mod object: " + outcome.Error);
        // A build that is not a non-empty string is an invalid source.
        foreach (var bad in new[] { "\"\"", "3" }) {
            var item = "\"" + Key + "\":{\"target\":" + Target(Made) + ",\"source\":" + Target(Station).TrimEnd('}') + ",\"build\":" + bad + "},\"placement\":{\"mode\":\"fixed\",\"position\":[1,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
            var invalid = new World((Station, 733, Build)).Engine.Apply(Doc(item), "author/pack").Outcomes[Key];
            Assert(!invalid.Applied && invalid.Error == ModObjects.InvalidBuild, "The build " + bad + " is refused: " + invalid.Error);
        }
    }

    // A modification of an existing target whose object is missing is refused as before, though an identical instance
    // is loaded.
    static void TargetsHaveNoFallback() {
        var world = new World((Dillsboro, 1000, Build));
        var outcome = world.Engine.Apply(Doc(Edit("station", Station)), "author/pack").Outcomes["station"];
        Assert(!outcome.Applied && outcome.Error != null && outcome.Error.StartsWith(PatchEngine.TargetMissing, StringComparison.Ordinal) && world.At[Dillsboro].Active, "The missing target is refused and the identical instance is untouched: " + outcome.Error);
    }

    static JObject Modifications(PatchEditingSession session) => (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"];
    static PatchEditingSession Session(World world, string patch = null) {
        var editor = new NativeMapEditor();
        var session = new PatchEditingSession(world.Engine, editor, new Store { Patch = patch }, LoadedMod.PatcherId, "author/pack"); editor.Session = session;
        return session;
    }

    // The placing click writes the model and build of the source beside it. A mod object duplicated again keeps the
    // build of its own source, also where that source is now an identical instance.
    static void PlacingWritesTheBuildOfTheSource() {
        var world = new World((Station, 733, Build));
        var session = Session(world);
        var placed = session.PlaceModObject(ModObjectTests.Id(Station), new Placement(AtBryson, new[] { 0.0, 0, 0, 1 }));
        var item = (JObject)Modifications(session)[Key];
        Assert(placed.Applied && (string)item["source"]["build"] == Build, "The source holds its build: " + (placed.Error ?? item.ToString(Newtonsoft.Json.Formatting.None)));
        var fallback = new World((Dillsboro, 1000, Build));
        var later = Session(fallback, Doc(ModObject(Key, Station, Made)));
        fallback.Engine.Apply(Doc(ModObject(Key, Station, Made)), "author/pack");
        var again = later.PlaceModObject(ModObjectTests.Id(Made), new Placement(new[] { 2084.0, 12, -288 }, new[] { 0.0, 0, 0, 1 }));
        var second = (JObject)Modifications(later)[Key + "-2"];
        Assert(again.Applied && second != null && (string)second["source"]["path"] == Station && (string)second["source"]["build"] == Build,
            "A mod object duplicated again keeps its source and build: " + (again.Error ?? Modifications(later).ToString(Newtonsoft.Json.Formatting.None)));
        Assert(ReferenceEquals(fallback.Builder.BuiltFrom.Last(), fallback.At[Dillsboro]), "Its object is built from the identical instance");
        // Without a build of the source, the placing click writes none.
        var unknown = new World((Station, 733, null));
        var plain = Session(unknown);
        plain.PlaceModObject(ModObjectTests.Id(Station), new Placement(AtBryson, new[] { 0.0, 0, 0, 1 }));
        Assert(Modifications(plain)[Key]?["source"]?["build"] == null, "A source whose build is unknown gets no build member");
    }

    // Save writes the build last in the source, after the members of a target, whatever order the file had.
    static void CanonicalSaveOrdersTheBuild() {
        var text = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"" + Key + "\":{\"dependsOn\":[],\"source\":{\"build\":\"" + Build + "\",\"structure\":\"" + SceneObjects.Structure + "\",\"path\":\"" + Station
            + "\",\"scene\":\"" + SceneName + "\",\"component\":\"" + SceneObjects.ComponentType + "\"},\"placement\":{\"mode\":\"fixed\",\"position\":[4493,12,5415],\"rotation\":[0,0,0,1]},\"target\":" + Target(Made) + "}}}";
        var world = new World((Station, 733, Build));
        Assert(world.Engine.Apply(text, "author/pack").Outcomes[Key].Applied, "The file applies");
        var store = new Store { Patch = text };
        var editor = new NativeMapEditor();
        var session = new PatchEditingSession(world.Engine, editor, store, LoadedMod.PatcherId, "author/pack"); editor.Session = session;
        var disable = session.Edit(ModObjectTests.Id(Made), null, false);
        session.BeginSave(); session.CompleteNativeSave();
        var source = (JObject)JObject.Parse(store.Patch)["modifications"][Key]["source"];
        Assert(disable.Applied && source.Properties().Select(p => p.Name).SequenceEqual(new[] { "scene", "path", "component", "structure", "build" }),
            "The build stands last: " + (disable.Error ?? String.Join(", ", source.Properties().Select(p => p.Name))));
    }

    // The plain scene of the engine tests with objects below a station: a build copies the source and what stands below
    // it to the path the engine gives. Each station has a build; the identical instances come in the order of the list.
    sealed class PartScene : Scene, IModObjectScene, IModObjectSources {
        public readonly List<string> Order = new List<string>();
        public readonly Dictionary<string, string> Builds = new Dictionary<string, string>(StringComparer.Ordinal);
        public int Made, Removes;
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) {
            if (Objects.ContainsKey(identity.Path)) throw new InvalidOperationException(ModObjects.Taken);
            var from = Objects.Single(p => p.Value.Contains(source)).Key;
            foreach (var pair in Objects.Where(p => p.Key == from || p.Key.StartsWith(from + "/", StringComparison.Ordinal)).ToArray()) {
                var box = (Box)pair.Value[0];
                Objects[identity.Path + pair.Key.Substring(from.Length)] = new List<object> { new Box { Position = (double[])box.Position.Clone(), Rotation = (double[])box.Rotation.Clone() } };
            }
            var made = (Box)Objects[identity.Path][0];
            made.Position = new[] { placement.Position[0] - Origin, placement.Position[1], placement.Position[2] }; made.Rotation = (double[])placement.Rotation.Clone();
            Made++;
            return made;
        }
        public void Remove(object modObject) {
            var path = Objects.Single(p => p.Value.Contains(modObject)).Key;
            foreach (var key in Objects.Keys.Where(k => k == path || k.StartsWith(path + "/", StringComparison.Ordinal)).ToArray()) Objects.Remove(key);
            Removes++;
        }
        public string BuildOf(object source) { var path = Objects.FirstOrDefault(p => p.Value.Contains(source)).Key; return path != null && Builds.TryGetValue(path, out var build) ? build : null; }
        public IReadOnlyList<SourceInstance> Identical(TargetIdentity source, string build) =>
            Order.Where(p => Objects.ContainsKey(p) && Builds.TryGetValue(p, out var b) && b == build).Select(p => new SourceInstance(p, Objects[p][0])).ToArray();
        // A loaded map with the Dillsboro station and its clock, without Alarka Jct, where the original stood.
        public static PartScene WithoutOriginal() {
            var scene = new PartScene { Maps = new[] { "map" } };
            scene.Objects["Dillsboro/Station"] = new List<object> { new Box { Position = new[] { 1.0, 2, 3 }, Rotation = new[] { 0.0, 0, 0, 1 } } };
            scene.Objects["Dillsboro/Station/Clock"] = new List<object> { new Box { Position = new[] { 1.0, 5, 3 }, Rotation = new[] { 0.0, 0, 0, 1 } } };
            scene.Builds["Dillsboro/Station"] = ModObjectSourceTests.Build; scene.Order.Add("Dillsboro/Station");
            return scene;
        }
    }

    // With its original missing, the mod object stands beside the identical instance, in that instance's parent; its
    // written target stays as it is. It is found by its mark: an edit of the mod object and of a part below it applies,
    // whether named by the written path or by the path where it stands. Applying again builds nothing more; Remove
    // modification removes it with its later edits; a second load builds and finds it again.
    static void AFallbackModObjectIsFoundByItsMarkWithItsParts() {
        TargetIdentity At(string path) => new TargetIdentity("s", path, "c");
        const string key = "mod-object-station";
        var written = ModObjects.Path("Alarka Jct/Station", "author/pack", key);
        var standing = "Dillsboro/" + ModObjects.NameOf(written);
        string Item(string k, string path, string body) => "\"" + k + "\":{\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"}," + body + "}";
        var doc = Doc(
            Item(key, written, "\"source\":{\"scene\":\"s\",\"path\":\"Alarka Jct/Station\",\"component\":\"c\",\"build\":\"" + Build + "\"},\"placement\":{\"mode\":\"fixed\",\"position\":[400,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]"),
            Item("clock", written + "/Clock", "\"placement\":{\"mode\":\"fixed\",\"position\":[401,6,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[\"" + key + "\"]"));
        var scene = PartScene.WithoutOriginal();
        var engine = new PatchEngine(scene);
        var result = engine.Apply(doc, "author/pack");
        Assert(result.Outcomes.Values.All(o => o.Applied), "The mod object and the edit of its part apply: " + String.Join("; ", result.Outcomes.Where(o => !o.Value.Applied).Select(o => o.Key + " " + o.Value.Error)));
        Assert(scene.Objects.ContainsKey(standing) && !scene.Objects.Keys.Any(k => k.StartsWith("Alarka Jct", StringComparison.Ordinal)), "It stands beside the identical instance and no parent is created: " + String.Join(", ", scene.Objects.Keys));
        Assert(((Box)scene.Objects[standing + "/Clock"][0]).Position[1] == 6 && ((Box)scene.Objects["Dillsboro/Station/Clock"][0]).Position[1] == 5, "The edit of its part reaches the part where it stands; the instance's own part is unchanged");
        var again = engine.Apply(doc, "author/pack");
        Assert(again.Outcomes.Values.All(o => o.Applied) && scene.Made == 1, "Applying again finds it by its mark and builds nothing more: " + String.Join("; ", again.Outcomes.Where(o => !o.Value.Applied).Select(o => o.Key + " " + o.Value.Error)));

        var editor = new NativeMapEditor(); var store = new Store { Patch = doc };
        var session = new PatchEditingSession(engine, editor, store, LoadedMod.PatcherId, "author/pack"); editor.Session = session;
        Assert(session.IsModObject(At(written)) && session.IsModObject(At(standing)), "Both paths name the mod object");
        var disable = session.Edit(At(standing), null, false);
        var moveClock = session.Edit(At(standing + "/Clock"), new Placement(new[] { 402.0, 7, 3 }, new[] { 0.0, 0, 0, 1 }), null);
        var mods = Modifications(session);
        Assert(disable.Applied && moveClock.Applied && !((Box)scene.Objects[standing][0]).Active && ((Box)scene.Objects[standing + "/Clock"][0]).Position[1] == 7, "Edits by the path where it stands apply: " + (disable.Error ?? moveClock.Error));
        Assert(mods.Count == 2 && (string)mods[key]["target"]["path"] == written && (string)mods["clock"]["target"]["path"] == written + "/Clock" && (bool)mods[key]["active"] == false,
            "The edits stay in the existing modifications, whose targets stay as written: " + mods.ToString(Newtonsoft.Json.Formatting.None));
        var enable = session.Edit(At(written), null, true);
        Assert(enable.Applied && ((Box)scene.Objects[standing][0]).Active, "An edit by the written path applies: " + enable.Error);
        session.BeginSave(); session.CompleteNativeSave();
        var saved = store.Patch;

        var removed = session.Remove(At(standing));
        Assert(removed.Applied && scene.Removes == 1 && !scene.Objects.Keys.Any(k => k.StartsWith(standing, StringComparison.Ordinal)) && Modifications(session).Count == 0,
            "Remove modification removes it with its later edit: " + (removed.Error ?? Modifications(session).ToString(Newtonsoft.Json.Formatting.None)));
        Assert(scene.Objects.ContainsKey("Dillsboro/Station") && ((Box)scene.Objects["Dillsboro/Station/Clock"][0]).Position[1] == 5, "The identical instance stays as it is");
        editor.Undo();
        Assert(scene.Objects.ContainsKey(standing) && Modifications(session).Count == 2 && ((Box)scene.Objects[standing + "/Clock"][0]).Position[1] == 7, "Undo puts it back where it stood: " + session.IncompleteOperation);

        // A new edit of a part, named by the path where it stands, applies after its mod object is built.
        var only = Doc(Item(key, written, "\"source\":{\"scene\":\"s\",\"path\":\"Alarka Jct/Station\",\"component\":\"c\",\"build\":\"" + Build + "\"},\"placement\":{\"mode\":\"fixed\",\"position\":[400,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]"));
        var fresh = PartScene.WithoutOriginal(); var freshEngine = new PatchEngine(fresh);
        freshEngine.Apply(only, "author/pack");
        var freshEditor = new NativeMapEditor();
        var freshSession = new PatchEditingSession(freshEngine, freshEditor, new Store { Patch = only }, LoadedMod.PatcherId, "author/pack"); freshEditor.Session = freshSession;
        var partEdit = freshSession.Edit(At(standing + "/Clock"), new Placement(new[] { 403.0, 8, 3 }, new[] { 0.0, 0, 0, 1 }), null);
        var part = Modifications(freshSession).Properties().SingleOrDefault(p => p.Name != key)?.Value;
        Assert(partEdit.Applied && part != null && ((JArray)part["dependsOn"]).Select(d => (string)d).SequenceEqual(new[] { key }),
            "A new edit of its part depends on the mod object: " + (partEdit.Error ?? Modifications(freshSession).ToString(Newtonsoft.Json.Formatting.None)));

        var next = PartScene.WithoutOriginal();
        var load = new PatchEngine(next).Apply(saved, "author/pack");
        Assert(load.Outcomes.Values.All(o => o.Applied) && next.Objects.ContainsKey(standing) && ((Box)next.Objects[standing + "/Clock"][0]).Position[1] == 7,
            "A second load builds it beside the identical instance and finds its part: " + String.Join("; ", load.Outcomes.Where(o => !o.Value.Applied).Select(o => o.Key + " " + o.Value.Error)));
    }
}
