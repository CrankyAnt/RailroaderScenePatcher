// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// A mod object that is a scene marker: the placing click gives it a marker ID of its own, which no scene marker,
// registration or map object uses and which differs from its source's, so its source keeps its registration. A
// milepost or Generic track marker binds at placement to the track the game's lookup finds at the placed position,
// and stays unbound, a problem while it is enabled, where the lookup finds none. Set replacement marker ID changes
// the mod object's marker ID afterwards. The sources are the Speed Sign at Alarka and milepost T-47 of the base map
// fixture; a fake lookup stands for the game's.
static class ModObjectMarkerTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Mod object markers: " + message); }
    const string Scene = "BushnellWhittier", SignPath = "World/Large Scenery/Alarka Signs/Speed Sign", PostPath = "World/Mile Posts/T-47";
    const string MarkerComponent = "Track.TrackMarker", PostComponent = "Helpers.MilePost";
    // The ids the test generator draws: ten times the same letter, A first, then B, then C.
    const string FirstDraw = "AAAAAAAAAA", SecondDraw = "BBBBBBBBBB", ThirdDraw = "CCCCCCCCCC";
    static readonly double[] Upright = { 0, 0, 0, 1 };
    static readonly BaseMapFixture Map = new BaseMapFixture();
    static BaseMapObject Fixture(string path, string family) {
        var found = Map.Objects.Where(o => o.Listed && o.Path == path).ToArray();
        Assert(found.Length == 1 && found[0].Family == family, "The base map fixture lists " + path + " once as a " + family);
        return found[0];
    }
    // A draw sequence that yields FirstDraw, then SecondDraw, then ThirdDraw.
    static Func<int, int> Draws() { var draw = 0; return n => (draw++ / ReplacementMarkerIds.GeneratedLength) % n; }

    // The game's lookup from a map position to a track location: the location given for a position near enough, else none.
    sealed class TrackLookup : ITrackPointLookup {
        public readonly List<(double[] At, TrackLocation Location)> Tracks = new List<(double[], TrackLocation)>();
        public TrackLocation TrackAt(double[] position) =>
            Tracks.Where(t => Math.Abs(t.At[0] - position[0]) < 25 && Math.Abs(t.At[2] - position[2]) < 25).Select(t => t.Location).FirstOrDefault();
    }
    // A marker world that tells its mod objects apart, as the native world does by the objects it built.
    sealed class PostWorld : FakeMilepostWorld, IModObjectMarkers {
        public readonly HashSet<FakeMarker> Made = new HashSet<FakeMarker>();
        public bool IsModObject(object marker) => marker is FakeMarker m && Made.Contains(m);
    }
    // Builds a mod object from a source marker as the native world does: a copy below an inactive holder takes its
    // marker ID before it starts, and starting registers it under that id at the location it copied from its source.
    // Removing it disables it, which removes every registration under its id.
    // It knows the model and build of the markers, so a mod object whose original is missing is built from the first
    // identical one, in the order of Loaded.
    sealed class Builder : IModObjectScene, IModObjectSources {
        readonly FakeMarkerWorld world;
        public readonly List<string> BuiltWith = new List<string>();
        public readonly List<object> BuiltFrom = new List<object>();
        public readonly Dictionary<FakeMarker, string> Builds = new Dictionary<FakeMarker, string>();
        public readonly List<KeyValuePair<string, FakeMarker>> Loaded = new List<KeyValuePair<string, FakeMarker>>();
        // A host that copies the origin flag, which ModObjects.NotCopied forbids.
        public bool KeepsOrigin;
        public string BuildOf(object source) => source is FakeMarker m && Builds.TryGetValue(m, out var build) ? build : null;
        public IReadOnlyList<SourceInstance> Identical(TargetIdentity source, string build) =>
            Loaded.Where(p => BuildOf(p.Value) == build).Select(p => new SourceInstance(p.Key, p.Value)).ToArray();
        public Builder(FakeMarkerWorld world) { this.world = world; }
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) {
            if (world.Objects.TryGetValue(identity.Path, out var standing) && standing.Count != 0) throw new InvalidOperationException(ModObjects.Taken);
            var from = (FakeMarker)source;
            BuiltWith.Add(markerId); BuiltFrom.Add(source);
            var made = new FakeMarker { Id = markerId ?? from.Id, Segment = from.Segment, End = from.End, Distance = from.Distance,
                Position = new[] { placement.Position[0] - world.Origin, placement.Position[1], placement.Position[2] }, Rotation = (double[])placement.Rotation.Clone() };
            world.Objects[identity.Path] = new List<object> { made };
            if (world is PostWorld posts) {
                var post = posts.Posts[from];
                // The host resets what ModObjects.NotCopied names: the copy is never the origin of its prefix.
                var cleared = !KeepsOrigin && ModObjects.NotCopied.Any(f => f.Key == PostComponent && f.Value == "origin");
                posts.Posts[made] = new FakeMilepostWorld.Post { Prefix = post.Prefix, Origin = post.Origin && !cleared, Mileage = post.Mileage, Text = post.Text, Rendered = post.Rendered };
                posts.Made.Add(made);
                if (from.Follows) world.LoadFollower(made); else world.Load(made);
            } else world.Load(made);
            world.Relative(made);
            return made;
        }
        public void Remove(object modObject) {
            var made = (FakeMarker)modObject;
            world.SetActive(made, false);
            foreach (var pair in world.Objects.Where(p => p.Value.Contains(made)).ToArray()) world.Objects.Remove(pair.Key);
        }
    }
    // One loaded world: the source on its track, a marker that holds the first id the generator draws, the capability of
    // the source's family, the game's lookup and what builds mod objects.
    sealed class World {
        public readonly FakeMarkerWorld Markers;
        public readonly FakeMarker Source, Other;
        public readonly TrackLookup Lookup = new TrackLookup();
        public readonly Builder Builder;
        public readonly SceneCapabilities Scene;
        public readonly PatchEngine Engine;
        public readonly bool Milepost;
        // An identical marker of the same model and build stands elsewhere; without the original it is the only one.
        public const string TwinPath = "World/Large Scenery/Alarka Signs/Speed Sign (2)", Build = "v1:3:speed-sign";
        public readonly FakeMarker Twin;
        public World(bool milepost, bool original = true) {
            Milepost = milepost;
            Markers = milepost ? new PostWorld() : new FakeMarkerWorld();
            var fixture = milepost ? Fixture(PostPath, "Milepost") : Fixture(SignPath, "Generic track marker");
            Source = new FakeMarker { Id = SecondDraw, Segment = "Sjdl", End = "a", Distance = 10, Position = new[] { fixture.At[0] - Markers.Origin, 0, fixture.At[1] } };
            Other = new FakeMarker { Id = FirstDraw, Segment = "Nnew", End = "a", Distance = 5, Position = new double[] { 60, 1, 2 } };
            Twin = new FakeMarker { Id = "Mtwin", Segment = "Sjdl", End = "b", Distance = 30, Position = new double[] { 70, 1, 2 } };
            if (original) Markers.Objects[Path] = new List<object> { Source };
            Markers.Objects[TwinPath] = new List<object> { Twin };
            Markers.Objects["World/Large Scenery/Alarka Signs/Yard Limit"] = new List<object> { Other };
            if (Markers is PostWorld posts) foreach (var m in new[] { Source, Twin }) posts.Posts[m] = new FakeMilepostWorld.Post { Mileage = 47, Text = "47", Rendered = "47" };
            foreach (var m in (original ? new[] { Source, Other, Twin } : new[] { Other, Twin })) { Markers.Load(m); Markers.Relative(m); }
            Markers.MapObjects.Add("water-tower-1");
            Scene = new SceneCapabilities(() => Markers.MapIds) { ModObjectWorld = Builder = new Builder(Markers), TrackLookup = Lookup };
            Scene.Add(milepost ? (IScene)new MilepostScene((PostWorld)Markers, Capability.Unchecked) : new TrackMarkerScene(Markers, Capability.Unchecked));
            Engine = new PatchEngine(Scene);
            if (original) { Builder.Builds[Source] = Build; Builder.Loaded.Add(new KeyValuePair<string, FakeMarker>(Path, Source)); }
            Builder.Builds[Twin] = Build; Builder.Loaded.Add(new KeyValuePair<string, FakeMarker>(TwinPath, Twin));
        }
        // The mod object wherever it stands: beside its original, or beside the identical instance it was built from.
        public FakeMarker Standing(TargetIdentity made) =>
            Markers.Objects.Where(p => p.Key.EndsWith("/" + ModObjects.NameOf(made.Path), StringComparison.Ordinal)).SelectMany(p => p.Value).Cast<FakeMarker>().SingleOrDefault();
        public string Path => Milepost ? PostPath : SignPath;
        public TargetIdentity Id(string path) => new TargetIdentity(Scene0, path, Milepost ? PostComponent : MarkerComponent, Milepost ? MilepostScene.Structure : TrackMarkerScene.Structure);
        const string Scene0 = ModObjectMarkerTests.Scene;
        public FakeMarker Copy(TargetIdentity made) => Markers.Objects.TryGetValue(made.Path, out var found) && found.Count == 1 ? (FakeMarker)found[0] : null;
        // The registration a marker holds under its own id, as bucket|end|distance, or "none".
        public string Own(FakeMarker m) {
            var own = Markers.Registrations(m).Where(r => r.HoldsMarker).Select(r => r.Bucket + "|" + r.Location.End + "|" + r.Location.Distance).ToArray();
            return own.Length == 0 ? "none" : String.Join(",", own);
        }
        public PatchEditingSession Session(out Store store, out NativeMapEditor editor) {
            store = new Store(); editor = new NativeMapEditor();
            var session = new PatchEditingSession(Engine, editor, store, LoadedMod.PatcherId, "author/pack"); editor.Session = session;
            return session;
        }
        // A point beside the source where the lookup finds segment S9sc, and a point where it finds no track.
        public double[] BesideTrack => new[] { Source.Position[0] + Markers.Origin + 300, 0, Source.Position[2] };
        public double[] AwayFromTrack => new[] { Source.Position[0] + Markers.Origin + 900, 0, Source.Position[2] };
        public void LayTrack() => Lookup.Tracks.Add((BesideTrack, new TrackLocation("S9sc", "a", 20)));
    }
    static JObject Item(PatchEditingSession session, TargetIdentity made) =>
        ((JObject)JObject.Parse(session.ScenePatchFileText)["modifications"]).Properties().Select(p => (JObject)p.Value).Single(v => (string)v["target"]["path"] == made.Path);

    internal static void Run() {
        foreach (var milepost in new[] { false, true }) {
            APlacedMarkerHoldsAMarkerIdOfItsOwn(milepost);
            APlacedMarkerBindsToTheTrackTheGameFinds(milepost);
            AMarkerWithoutATrackStaysUnboundAndIsAProblem(milepost);
            SetReplacementMarkerIdChangesTheModObjectsId(milepost);
            AMarkerBuiltFromAnIdenticalInstanceKeepsItsOwnIdAndBinding(milepost);
        }
        AMarkerPartIsDuplicatedBesideItsGroup();
        ACopyOfAnOriginMilepostIsNoOrigin();
        AMarkerModObjectNeedsAFreeMarkerIdOfItsOwn();
        AMilepostOfTheMapStillTakesNoReplacementId();
        SupportedEditsOfferSetReplacementMarkerIdOnAModObject();
        Console.WriteLine("PASS: mod object markers");
    }
    static string Kind(bool milepost) => milepost ? "A milepost" : "A Generic track marker";

    // The placing click gives the mod object an id that differs from its source's and from every other scene marker's:
    // the generator's first draw is taken by another marker and its second by the source, so the third is the one.
    // The copy starts with that id, so the source keeps its own registration; a fresh load builds it with the same id.
    static void APlacedMarkerHoldsAMarkerIdOfItsOwn(bool milepost) {
        var world = new World(milepost); world.LayTrack();
        var session = world.Session(out var store, out _);
        var placed = session.PlaceModObject(world.Id(world.Path), new Placement(world.BesideTrack, Upright), null, null, Draws());
        Assert(placed.Applied, Kind(milepost) + " is placed: " + placed.Error);
        var item = Item(session, placed.Target);
        var copy = world.Copy(placed.Target);
        Assert((string)item[ReplacementMarkerIds.Member] == ThirdDraw && copy.Id == ThirdDraw, Kind(milepost) + " holds the first free id the generator draws: " + item.ToString(Newtonsoft.Json.Formatting.None));
        Assert(world.Builder.BuiltWith.SequenceEqual(new[] { ThirdDraw }), "The copy starts with its own id, never its source's: " + String.Join(",", world.Builder.BuiltWith));
        Assert(world.Source.Id == SecondDraw && world.Own(world.Source) == "Sjdl|a|10" && world.Other.Id == FirstDraw && world.Own(world.Other) == "Nnew|a|5",
            "The source and the other marker keep their ids and registrations: " + world.Own(world.Source) + " " + world.Own(world.Other));
        session.BeginSave(); session.CompleteNativeSave();
        var next = new World(milepost); next.LayTrack();
        var result = next.Engine.Apply(store.Patch, "author/pack");
        var key = result.Outcomes.Keys.Single();
        Assert(result.Outcomes[key].Applied && next.Copy(placed.Target)?.Id == ThirdDraw && next.Own(next.Source) == "Sjdl|a|10", "A fresh load builds the mod object with its own id: " + result.Outcomes[key].Error);
    }

    // A milepost or Generic track marker binds to the track the lookup finds at the placed position, under its own id;
    // the object stays where it was placed.
    static void APlacedMarkerBindsToTheTrackTheGameFinds(bool milepost) {
        var world = new World(milepost); world.LayTrack();
        var session = world.Session(out _, out var editor);
        var placed = session.PlaceModObject(world.Id(world.Path), new Placement(world.BesideTrack, Upright), null, null, Draws());
        var item = Item(session, placed.Target);
        var bound = item["relationships"]?["trackMarker"];
        Assert(bound != null && (string)bound["segment"] == "S9sc" && (string)bound["end"] == "a" && (double)bound["distance"] == 20, Kind(milepost) + " binds to the track found at its position: " + item.ToString(Newtonsoft.Json.Formatting.None));
        var copy = world.Copy(placed.Target);
        Assert(copy.Segment == "S9sc" && world.Own(copy) == "S9sc|a|20" && world.Markers.Position(copy)[0] == world.BesideTrack[0], "It is registered on that track and stands where it was placed: " + world.Own(copy));
        Assert(world.Own(world.Source) == "Sjdl|a|10", "The source keeps its binding and registration");
        editor.Undo();
        Assert(world.Copy(placed.Target) == null && world.Own(world.Source) == "Sjdl|a|10", "Undo takes the mod object away and leaves the source's registration");
    }

    // Where the lookup finds no track, the mod object is written as unbound: it holds no binding and no registration,
    // and an enabled one is a problem; a disabled one is not.
    static void AMarkerWithoutATrackStaysUnboundAndIsAProblem(bool milepost) {
        var world = new World(milepost); world.LayTrack();
        var session = world.Session(out _, out _);
        var placed = session.PlaceModObject(world.Id(world.Path), new Placement(world.AwayFromTrack, Upright), null, null, Draws());
        Assert(placed.Applied, Kind(milepost) + " away from the track is placed: " + placed.Error);
        var item = Item(session, placed.Target);
        Assert(item["relationships"]?["trackMarker"]?["detach"]?.Type == JTokenType.Boolean && (bool)item["relationships"]["trackMarker"]["detach"], "It is written as unbound: " + item.ToString(Newtonsoft.Json.Formatting.None));
        var copy = world.Copy(placed.Target);
        Assert(copy.Segment == null && world.Own(copy) == "none" && copy.Id == ThirdDraw && world.Own(world.Source) == "Sjdl|a|10", "It holds no binding and no registration: " + world.Own(copy));
        var need = BindingRules.NeedAt(BindingRules.Need("Generic", false, false), placed.Target.Path);
        Assert(BindingRules.Problem(need, true, "Marker", copy.Segment, null) == "Marker has an empty segment binding", "An enabled unbound mod object is a problem");
        Assert(BindingRules.Problem(need, false, "Marker", copy.Segment, null) == null, "A disabled one is not");
        var source = BindingRules.NeedAt(BindingRules.Need("Generic", false, false), world.Path);
        Assert(source == BindingNeed.Optional && BindingRules.Problem(source, true, "Marker", null, null) == null, "A marker of the map keeps its optional binding");
    }

    // Set replacement marker ID gives the mod object another id under the uniqueness rules: its registration moves to the
    // new id, an id another marker or its source holds is refused, and clearing is refused because a mod object keeps an
    // id of its own. Undo returns the id it had.
    static void SetReplacementMarkerIdChangesTheModObjectsId(bool milepost) {
        var world = new World(milepost); world.LayTrack();
        var session = world.Session(out var store, out var editor);
        var placed = session.PlaceModObject(world.Id(world.Path), new Placement(world.BesideTrack, Upright), null, null, Draws());
        var copy = world.Copy(placed.Target);
        Assert(session.MarkerIdRefusal(placed.Target, "Mnew1") == null, "The window offers the new id: " + session.MarkerIdRefusal(placed.Target, "Mnew1"));
        var set = session.SetMarkerId(placed.Target, "Mnew1", null);
        Assert(set.Applied && copy.Id == "Mnew1" && world.Own(copy) == "S9sc|a|20" && (string)Item(session, placed.Target)[ReplacementMarkerIds.Member] == "Mnew1",
            Kind(milepost) + " takes another id with its registration: " + set.Error);
        Assert(world.Own(world.Source) == "Sjdl|a|10" && world.Markers.Registrations(copy).Count == 1, "The source keeps its registration, and the old id has none left");
        foreach (var taken in new[] { FirstDraw, SecondDraw, "water-tower-1" }) {
            var refused = session.SetMarkerId(placed.Target, taken, null);
            Assert(!refused.Applied && refused.Error.Contains(taken) && copy.Id == "Mnew1", "An id in use is refused: " + taken + " " + refused.Error);
        }
        var cleared = session.SetMarkerId(placed.Target, null, null);
        Assert(!cleared.Applied && cleared.Error == ModObjects.KeepsMarkerId && copy.Id == "Mnew1", "Clearing is refused: " + cleared.Error);
        editor.Undo();
        Assert(copy.Id == ThirdDraw && world.Own(copy) == "S9sc|a|20" && (string)Item(session, placed.Target)[ReplacementMarkerIds.Member] == ThirdDraw, "Undo returns the id it had: " + copy.Id);
        editor.Redo();
        session.BeginSave(); session.CompleteNativeSave();
        var next = new World(milepost); next.LayTrack();
        var result = next.Engine.Apply(store.Patch, "author/pack");
        Assert(result.Outcomes.Values.Single().Applied && next.Copy(placed.Target).Id == "Mnew1", "A fresh load builds it with the id set afterwards");
    }

    // A scene patch file whose marker mod object holds no marker ID, its source's, or one another marker holds, fails
    // that mod object before anything is built; the source keeps its registration.
    static void AMarkerModObjectNeedsAFreeMarkerIdOfItsOwn() {
        string Doc(string id) {
            var made = ModObjects.Path(SignPath, "author/pack", "mod-object-sign");
            var target = "{\"scene\":\"" + Scene + "\",\"path\":\"PATH\",\"component\":\"" + MarkerComponent + "\",\"structure\":\"" + TrackMarkerScene.Structure + "\"}";
            return "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"mod-object-sign\":{\"target\":" + target.Replace("PATH", made) + ",\"source\":" + target.Replace("PATH", SignPath)
                + (id == null ? "" : ",\"markerId\":\"" + id + "\"") + ",\"placement\":{\"mode\":\"fixed\",\"position\":[1300,0,1177],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}";
        }
        foreach (var (id, reason) in new[] { ((string)null, ModObjects.NeedsMarkerId), (SecondDraw, ReplacementMarkerIds.UsedBy(SecondDraw, SignPath)), (FirstDraw, ReplacementMarkerIds.UsedBy(FirstDraw, "World/Large Scenery/Alarka Signs/Yard Limit")) }) {
            var world = new World(false);
            var outcome = world.Engine.Apply(Doc(id), "author/pack").Outcomes["mod-object-sign"];
            Assert(!outcome.Applied && outcome.Error == reason && world.Builder.BuiltWith.Count == 0, "A mod object with marker ID " + (id ?? "none") + " fails before it is built: " + outcome.Error);
            Assert(world.Own(world.Source) == "Sjdl|a|10", "The source keeps its registration");
        }
        var free = new World(false);
        var applied = free.Engine.Apply(Doc("Mfree"), "author/pack").Outcomes["mod-object-sign"];
        Assert(applied.Applied && free.Builder.BuiltWith.SequenceEqual(new[] { "Mfree" }), "A free id of its own builds it: " + applied.Error);
    }

    // A milepost of the map takes no replacement ID, as before: only a milepost that is a mod object holds one.
    static void AMilepostOfTheMapStillTakesNoReplacementId() {
        var world = new World(true);
        var session = world.Session(out _, out _);
        var refused = session.SetMarkerId(world.Id(PostPath), "Mnew1", null);
        Assert(!refused.Applied && refused.Error == ReplacementMarkerIds.NotSupported && world.Source.Id == SecondDraw, "A milepost of the map is refused: " + refused.Error);
    }

    // Supported edits offers Set replacement marker ID on a milepost that is a mod object, and keeps the reason of a
    // milepost of the map.
    static void SupportedEditsOfferSetReplacementMarkerIdOnAModObject() {
        SceneObservation Row(string path) => new SceneObservation(new object(), new TargetIdentity(Scene, path, PostComponent, MilepostScene.Structure), SceneFamilies.Milepost, true,
            new double[] { 0, 0, 0 }, new double[] { 0, 0, 0, 1 }, new[] { "Marker segment=Sjdl; end=a; distance=10" }, Array.Empty<string>(), Array.Empty<string>(), "binding");
        var made = Row(ModObjects.Path(PostPath, "author/pack", "mod-object-post"));
        var live = new SupportedEdits.Live { Present = true, MarkerIdReason = CommandBand.MarkerIdUnavailable(SceneFamilies.Milepost, true, ThirdDraw, 0, true) };
        Assert(SupportedEdits.ReasonOf(SupportedEdits.Of(made, live), SupportedEdits.MarkerId) == null, "A milepost mod object offers Set replacement marker ID");
        Assert(SupportedEdits.ReasonOf(SupportedEdits.Of(Row(PostPath), live), SupportedEdits.MarkerId) == EditReasons.Replacement, "A milepost of the map keeps its reason");
        Assert(CommandBand.TakesMarkerId(made.Identity) && !CommandBand.TakesMarkerId(Row(PostPath).Identity), "Only the mod object takes a replacement ID");
    }

    // A marker mod object whose original is missing is built from the first identical instance (#165) with the marker
    // ID and binding of its own modification: the instance keeps its ID and registration. A file that gives it the
    // instance's ID fails before anything is built.
    static void AMarkerBuiltFromAnIdenticalInstanceKeepsItsOwnIdAndBinding(bool milepost) {
        var world = new World(milepost); world.LayTrack();
        var session = world.Session(out var store, out _);
        var placed = session.PlaceModObject(world.Id(world.Path), new Placement(world.BesideTrack, Upright), null, null, Draws());
        var item = Item(session, placed.Target);
        Assert(placed.Applied && (string)item["source"]["build"] == World.Build, Kind(milepost) + " is placed with its source's build: " + placed.Error + " " + item.ToString(Newtonsoft.Json.Formatting.None));
        session.BeginSave(); session.CompleteNativeSave();
        var next = new World(milepost, original: false); next.LayTrack();
        var key = item.Parent is JProperty owner ? owner.Name : JObject.Parse(store.Patch)["modifications"].Cast<JProperty>().Single().Name;
        var outcome = next.Engine.Apply(store.Patch, "author/pack").Outcomes[key];
        var copy = next.Standing(placed.Target);
        Assert(outcome.Applied && next.Builder.BuiltFrom.SequenceEqual(new object[] { next.Twin }) && copy != null, "Without its original it is built from the identical instance: " + outcome.Error);
        Assert(copy.Id == ThirdDraw && next.Builder.BuiltWith.SequenceEqual(new[] { ThirdDraw }) && copy.Segment == "S9sc" && next.Own(copy) == "S9sc|a|20",
            "It keeps its own marker ID and binding: " + copy.Id + " " + next.Own(copy));
        Assert(next.Twin.Id == "Mtwin" && next.Own(next.Twin) == "Sjdl|b|30", "The identical instance keeps its ID and registration: " + next.Own(next.Twin));
        var taken = new World(milepost, original: false); taken.LayTrack();
        var refused = taken.Engine.Apply(store.Patch.Replace("\"" + ThirdDraw + "\"", "\"Mtwin\""), "author/pack").Outcomes[key];
        Assert(!refused.Applied && refused.Error == ReplacementMarkerIds.UsedBy("Mtwin", World.TwinPath) && taken.Builder.BuiltWith.Count == 0 && taken.Own(taken.Twin) == "Sjdl|b|30",
            "The instance's own ID is refused before anything is built: " + refused.Error);
    }

    // A Generic track marker that is a part of a group, a whistle sign of a crossing, is duplicated as an object of its
    // own: its copy stands beside the group, in the group's parent, with a marker ID of its own and the binding the
    // lookup finds; the whistle sign and its group stay as they are.
    static void AMarkerPartIsDuplicatedBesideItsGroup() {
        const string Group = "World/Large Scenery/Alarka Jct/Crossing Unprotected (1)", Whistle = Group + "/Whistle Sign E";
        var fixture = Fixture(Whistle, "Generic track marker");
        var world = new World(false); world.LayTrack();
        var sign = new FakeMarker { Id = "Mwhis", Segment = "Sjdl", End = "b", Distance = 60, Position = new[] { fixture.At[0] - world.Markers.Origin, 0, fixture.At[1] } };
        world.Markers.Objects[Whistle] = new List<object> { sign }; world.Markers.Load(sign); world.Markers.Relative(sign);
        var session = world.Session(out var store, out _);
        var placed = session.PlaceModObject(world.Id(Whistle), new Placement(world.BesideTrack, Upright), null, Group, Draws());
        Assert(placed.Applied, "The whistle sign is duplicated: " + placed.Error);
        Assert(ModObjects.ParentOf(placed.Target.Path) == "World/Large Scenery/Alarka Jct" && ModObjects.NameOf(placed.Target.Path).StartsWith("Whistle Sign E [mod object ", StringComparison.Ordinal),
            "Its copy stands beside the group as an object of its own: " + placed.Target.Path);
        var copy = world.Copy(placed.Target);
        var item = Item(session, placed.Target);
        Assert(copy != null && copy.Id == ThirdDraw && (string)item[ReplacementMarkerIds.Member] == ThirdDraw, "It holds a marker ID of its own: " + copy?.Id);
        Assert(copy.Segment == "S9sc" && world.Own(copy) == "S9sc|a|20" && (string)item["relationships"]["trackMarker"]["segment"] == "S9sc", "It binds to the track found at its position: " + world.Own(copy));
        Assert(sign.Id == "Mwhis" && world.Own(sign) == "Sjdl|b|60" && world.Markers.Objects[Whistle].Single() == sign, "The whistle sign keeps its ID, registration and place in its group");
        session.BeginSave(); session.CompleteNativeSave();
        var next = new World(false); next.LayTrack();
        var twin = new FakeMarker { Id = "Mwhis", Segment = "Sjdl", End = "b", Distance = 60 };
        next.Markers.Objects[Whistle] = new List<object> { twin }; next.Markers.Load(twin); next.Markers.Relative(twin);
        var result = next.Engine.Apply(store.Patch, "author/pack");
        Assert(result.Outcomes.Values.All(o => o.Applied) && next.Copy(placed.Target)?.Id == ThirdDraw && next.Own(twin) == "Sjdl|b|60", "A fresh load builds it beside the group with its own ID");
    }

    // A copy of an origin milepost is never the origin of its prefix: the host clears the flag, the source keeps it and
    // the game's origin cache keeps its entry. A copy that still holds the flag is refused and leaves nothing behind.
    static void ACopyOfAnOriginMilepostIsNoOrigin() {
        foreach (var keeps in new[] { false, true }) {
            var world = new World(true); world.LayTrack();
            var posts = (PostWorld)world.Markers;
            posts.Posts[world.Source].Origin = true;
            posts.OriginCache["T"] = world.Source;
            world.Builder.KeepsOrigin = keeps;
            var session = world.Session(out _, out _);
            var placed = session.PlaceModObject(world.Id(PostPath), new Placement(world.BesideTrack, Upright), null, null, Draws());
            Assert(posts.Posts[world.Source].Origin && posts.OriginCache["T"] == world.Source && world.Own(world.Source) == "Sjdl|a|10", "The source stays its prefix's origin, in the game's cache too");
            if (!keeps) {
                var copy = world.Copy(placed.Target);
                Assert(placed.Applied && copy != null && !posts.Posts[copy].Origin, "The copy of an origin milepost is no origin: " + placed.Error);
            } else {
                Assert(!placed.Applied && placed.Error == MilepostScene.ModObjectOrigin && world.Copy(placed.Target ?? world.Id(PostPath + " none")) == null && !posts.Made.Any(m => world.Markers.Objects.Values.Any(v => v.Contains(m))),
                    "A copy that holds the origin flag is refused and removed: " + placed.Error);
            }
        }
    }
}
