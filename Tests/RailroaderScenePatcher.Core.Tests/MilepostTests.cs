// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Controlled mileposts: Generic-marker graph behavior plus the milepost's own state, which no edit may change: its
// prefix, origin flag, mile value, displayed text and model, and the game's origin cache, which maps a prefix to the
// origin milepost object once something has looked it up.
class FakeMilepostWorld : FakeMarkerWorld, IMilepostWorld, ITargetConfirmation, IPlacementPattern {
    public sealed class Post { public string Prefix = "T"; public bool Origin; public double Mileage; public string Text, Model = "post-mesh"; }
    public readonly Dictionary<FakeMarker, Post> Posts = new Dictionary<FakeMarker, Post>();
    // The game's origin cache by prefix: absent, or the origin milepost it found, which may be none.
    public readonly Dictionary<string, FakeMarker> OriginCache = new Dictionary<string, FakeMarker>();
    public new IReadOnlyList<object> Resolve(TargetIdentity identity) {
        if (identity.Component != "Helpers.MilePost" || (identity.Structure != null && identity.Structure != MilepostScene.Structure)) return Array.Empty<object>();
        return Objects.TryGetValue(identity.Path, out var found) ? found.Where(o => Posts.ContainsKey((FakeMarker)o)).ToList() : new List<object>();
    }
    public bool StillIdentifies(object target, TargetIdentity identity) =>
        identity.Component == "Helpers.MilePost" && Objects.TryGetValue(identity.Path, out var found) && found.Contains(target) && Posts.ContainsKey((FakeMarker)target);
    public string PlacementProblem(object target) => ((FakeMarker)target).Pattern;
    // Failure injections: an edit step that changes what the patcher must preserve.
    public bool ChangeMileageOnLocation, DropCacheOnLocation;
    public new void SetLocation(object target, TrackLocation location) {
        base.SetLocation(target, location);
        var m = (FakeMarker)target;
        if (ChangeMileageOnLocation) { ChangeMileageOnLocation = false; Posts[m].Mileage += 1; }
        if (DropCacheOnLocation) { DropCacheOnLocation = false; OriginCache.Remove(Posts[m].Prefix); }
    }
    public MilepostState Milepost(object target) {
        var post = Posts[(FakeMarker)target];
        return new MilepostState(post.Prefix, post.Origin, post.Mileage, post.Text, post.Model,
            OriginCache.TryGetValue(post.Prefix, out var origin) ? MilepostState.CachedAs(origin == null ? null : NameOf(origin)) : MilepostState.NotCached);
    }
    string NameOf(FakeMarker m) => Objects.First(o => o.Value.Contains(m)).Key;
}
static class MilepostTests {
    const string T46 = "World/Mile Posts/T-46", T47 = "World/Mile Posts/T-47";
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Target(string path = T46) => "\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"Helpers.MilePost\",\"structure\":\"milepost-v1\"}";
    static string Item(string id, string body, string path = T46, string deps = "[]") => "\"" + id + "\":{" + Target(path) + body + ",\"dependsOn\":" + deps + "}";
    static string Bind(string marker) => ",\"relationships\":{\"trackMarker\":" + marker + "}";
    static string Place(double x, double qy = 0, double qw = 1) => ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",1,2],\"rotation\":[0," + qy.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",0," + qw.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]}";
    static string At(string segment, string end, double distance) => "{\"segment\":\"" + segment + "\",\"end\":\"" + end + "\",\"distance\":" + distance.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
    const string Detach = "{\"detach\":true}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":{" + items + "}}";
    // T-46 is the origin of prefix T on Stvw; T-47 stands on Stky0cg9c, as in the Boot02 milepost trial.
    static (FakeMilepostWorld world, FakeMarker t46, FakeMarker t47, MilepostScene scene) Setup() {
        var world = new FakeMilepostWorld();
        world.Segments["Stvw"] = new List<double> { 120 }; world.Segments["Stky0cg9c"] = new List<double> { 400 };
        world.Groups["Stvw"] = "sy-db"; world.Groups["Stky0cg9c"] = "sy-db"; world.EnabledGroups.Add("sy-db");
        var t46 = new FakeMarker { Id = "Mdow", Segment = "Stvw", End = "b", Distance = 51.20403 };
        var t47 = new FakeMarker { Id = "Mnau", Segment = "Stky0cg9c", End = "b", Distance = 266.9192, Position = new double[] { 60, 1, 2 } };
        world.Objects[T46] = new List<object> { t46 }; world.Objects[T47] = new List<object> { t47 };
        world.Posts[t46] = new FakeMilepostWorld.Post { Origin = true, Mileage = 46, Text = "46" };
        world.Posts[t47] = new FakeMilepostWorld.Post { Mileage = 47, Text = "47" };
        foreach (var m in new[] { t46, t47 }) { world.LoadFollower(m); world.Relative(m); }
        world.Mutations = 0;
        return (world, t46, t47, new MilepostScene(world, Capability.Unchecked));
    }
    static string Entry(FakeMarkerWorld world, FakeMarker marker) {
        var entries = world.Registrations(marker);
        if (entries.Count != 1) return entries.Count + " entries";
        var e = entries[0];
        return e.Bucket + "|" + e.Location.End + "|" + e.Location.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    static string Kept(FakeMilepostWorld world, FakeMarker m) { var p = world.Posts[m]; return p.Prefix + "|" + p.Origin + "|" + p.Mileage + "|" + p.Text + "|" + p.Model; }
    public static void Run() {
        PlacementAndBindingAreSeparateSettings();
        TargetsResolveExactlyOnce();
        ReplacementTracksResolveExactlyOnce();
        MapMismatchChangesNothing();
        OmittedSetAndDetachedValues();
        DependenciesResolveRegardlessOfOrder();
        RepeatedApplicationConverges();
        OriginCacheKeepsItsEntry();
        PreflightRefusalLeavesTheSceneUnchanged();
        PostMutationFailureRestoresOrGuardsRetry();
        EditingSetsPlacementAndBindingWithUndoRedo();
        RemovalRecomputesUnderlyingState();
        SaveAndReloadReproduceTheOperationOnce();
        VersionHoldsWithoutTheCapability();
        Console.WriteLine("PASS: milepost placement, activity and track binding");
    }
    static void PlacementAndBindingAreSeparateSettings() {
        var (world, t46, t47, scene) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Item("rebind-t46", Bind(At("Stky0cg9c", "b", 266.9192)))));
        Assert(result.Error == null && result.Outcomes["rebind-t46"].Applied, "A milepost binding applies: " + result.Error + result.Outcomes["rebind-t46"].Error);
        Assert(Entry(world, t46) == "Stky0cg9c|b|266.9192" && t46.Position[0] == 20, "The registration follows the binding and the post does not move: " + Entry(world, t46));
        Assert(Kept(world, t46) == "T|True|46|46|post-mesh", "The model and mile value stay");
        (world, t46, t47, scene) = Setup();
        result = new PatchEngine(scene).Apply(Doc(Item("move-t46", Place(1025, 1, 0))));
        Assert(result.Outcomes["move-t46"].Applied && t46.Position[0] == 25 && t46.Rotation[1] == 1, "A milepost placement applies: " + result.Outcomes["move-t46"].Error);
        Assert(Entry(world, t46) == "Stvw|b|51.20403" && Kept(world, t46) == "T|True|46|46|post-mesh", "An omitted binding keeps the registration, model and mile value");
    }
    static void TargetsResolveExactlyOnce() {
        var (world, t46, t47, scene) = Setup();
        var twin = new FakeMarker { Id = "Mtwn", Segment = "Stvw", End = "a", Distance = 3 };
        world.Objects["World/Mile Posts/T-48"] = new List<object> { twin, new FakeMarker { Id = "Mtw2", Segment = "Stvw", End = "a", Distance = 4 } };
        foreach (var m in world.Objects["World/Mile Posts/T-48"].Cast<FakeMarker>()) world.Posts[m] = new FakeMilepostWorld.Post { Mileage = 48, Text = "48" };
        var result = new PatchEngine(scene).Apply(Doc(Item("missing", Bind(Detach), "World/Mile Posts/T-99") + "," + Item("ambiguous", Bind(Detach), "World/Mile Posts/T-48") + "," + Item("keep", Place(1070), T47)));
        Assert(!result.Outcomes["missing"].Applied && result.Outcomes["missing"].Error.Contains("uniquely"), "A missing milepost fails its modification: " + result.Outcomes["missing"].Error);
        Assert(!result.Outcomes["ambiguous"].Applied && result.Outcomes["ambiguous"].Error.Contains("uniquely"), "An ambiguous milepost fails its modification: " + result.Outcomes["ambiguous"].Error);
        Assert(result.Outcomes["keep"].Applied && t47.Position[0] == 70, "An independent milepost edit still applies");
        Assert(Entry(world, t46) == "Stvw|b|51.20403" && twin.Segment == "Stvw", "No substitute target changes");
        // A track-marker target names no milepost: the two capabilities never claim the same object.
        var asMarker = new PatchEngine(scene).Apply(Doc("\"as-marker\":{\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + T46 + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"milepost-v1\"}" + Bind(Detach) + ",\"dependsOn\":[]}"));
        Assert(!asMarker.Outcomes["as-marker"].Applied && t46.Segment == "Stvw", "A milepost target names the milepost component: " + asMarker.Outcomes["as-marker"].Error);
    }
    static void ReplacementTracksResolveExactlyOnce() {
        foreach (var (marker, expected) in new[] { (At("Smissing", "a", 1), "missing"), (At("Sdup", "a", 1), "ambiguous"), (At("Stvw", "a", 500), "outside") }) {
            var (world, t46, t47, scene) = Setup();
            world.Segments["Sdup"] = new List<double> { 50, 50 };
            var result = new PatchEngine(scene).Apply(Doc(Item("rebind", Bind(marker)) + "," + Item("keep", Bind(At("Stvw", "a", 9)), T47)));
            Assert(!result.Outcomes["rebind"].Applied && result.Outcomes["rebind"].Error.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0, "An unresolved replacement track fails the milepost binding: " + result.Outcomes["rebind"].Error);
            Assert(Entry(world, t46) == "Stvw|b|51.20403" && Kept(world, t46) == "T|True|46|46|post-mesh", "The milepost keeps its binding: " + expected);
            Assert(result.Outcomes["keep"].Applied && Entry(world, t47) == "Stvw|a|9", "An independent milepost binding applies: " + expected);
        }
    }
    static void MapMismatchChangesNothing() {
        var (world, t46, t47, scene) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Item("rebind", Bind(Detach) + Place(1030)), "other-map"));
        Assert(result.Outcomes.Count == 0 || !result.Outcomes.Values.Any(o => o.Applied), "A scene patch file for another map applies nothing");
        Assert(world.Mutations == 0 && Entry(world, t46) == "Stvw|b|51.20403" && t46.Position[0] == 20, "The map mismatch leaves the milepost untouched");
    }
    static void OmittedSetAndDetachedValues() {
        var (world, t46, t47, scene) = Setup();
        var engine = new PatchEngine(scene);
        var detached = engine.Apply(Doc(Item("detach", Bind(Detach))));
        Assert(detached.Outcomes["detach"].Applied && t46.Segment == null && world.Registrations(t46).Count == 0 && t46.Position[0] == 20 && t46.Active, "An explicit detach clears binding and registration, and neither moves nor disables the post");
        Assert(Kept(world, t46) == "T|True|46|46|post-mesh", "Detach keeps the mile value");
        (world, t46, t47, scene) = Setup();
        var disabled = new PatchEngine(scene).Apply(Doc(Item("disable", ",\"active\":false")));
        Assert(disabled.Outcomes["disable"].Applied && !t46.Active && t46.Held && world.Registrations(t46).Count == 0 && t46.Segment == "Stvw", "An explicit disable holds the group-driven post inactive and keeps its binding: " + disabled.Outcomes["disable"].Error);
        world.SetGroupEnabled("sy-db", false); world.SetGroupEnabled("sy-db", true);
        Assert(!t46.Active, "A later group activation does not enable the disabled post");
        var enabled = new PatchEngine(scene).Apply(Doc(Item("enable", ",\"active\":true")));
        Assert(!enabled.Outcomes["enable"].Applied && enabled.Outcomes["enable"].Error.Contains("track group"), "An explicit enable of a group-driven post is refused: " + enabled.Outcomes["enable"].Error);
        (world, t46, t47, scene) = Setup();
        var both = new PatchEngine(scene).Apply(Doc(Item("both", Bind(At("Stky0cg9c", "a", 10)) + Place(1030))));
        Assert(both.Outcomes["both"].Applied && t46.Position[0] == 30 && Entry(world, t46) == "Stky0cg9c|a|10", "Placement and binding each take their own value");
    }
    static void DependenciesResolveRegardlessOfOrder() {
        var (world, t46, t47, scene) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Item("dependent", Bind(Detach), T47, "[\"base\"]") + "," + Item("base", Bind(At("Smissing", "a", 1)))));
        Assert(!result.Outcomes["dependent"].Applied && !result.Outcomes["base"].Applied && Entry(world, t47) == "Stky0cg9c|b|266.9192", "A failed milepost edit isolates its dependents");
        result = new PatchEngine(scene).Apply(Doc(Item("dependent", Bind(Detach), T47, "[\"base\"]") + "," + Item("base", Bind(At("Stky0cg9c", "b", 266.9192)))));
        Assert(result.Outcomes["dependent"].Applied && result.Outcomes["base"].Applied && world.Registrations(t47).Count == 0 && Entry(world, t46) == "Stky0cg9c|b|266.9192", "Dependencies run regardless of scene patch file order");
    }
    static void RepeatedApplicationConverges() {
        var (world, t46, t47, scene) = Setup();
        var patch = Doc(Item("both", Bind(At("Stky0cg9c", "b", 266.9192)) + Place(1025, 1, 0)));
        for (var i = 0; i < 3; i++) {
            var result = new PatchEngine(scene).Apply(patch);
            Assert(result.Outcomes["both"].Applied, "Application " + i + " applies: " + result.Outcomes["both"].Error);
        }
        Assert(t46.Position[0] == 25 && t46.Rotation[1] == 1 && Entry(world, t46) == "Stky0cg9c|b|266.9192", "Repeated application converges on the placement and binding without accumulated movement");
        Assert(world.Buckets.Values.Sum(b => b.Count(e => e.Key == "Mdow")) == 1 && t46.Subscriptions == 1 && t46.ObservedGroup == "sy-db", "One registration and one group subscription remain");
        Assert(Kept(world, t46) == "T|True|46|46|post-mesh", "Repeated application keeps the mile value");
    }
    // The Boot02 trial populated the T cache before rebinding T-46: the entry and T-47's origin stayed the same object.
    static void OriginCacheKeepsItsEntry() {
        var (world, t46, t47, scene) = Setup();
        world.OriginCache["T"] = t46;
        var engine = new PatchEngine(scene);
        Assert(engine.Apply(Doc(Item("rebind", Bind(At("Stky0cg9c", "b", 266.9192)) + Place(1025)))).Outcomes["rebind"].Applied, "A cached origin post is rebound and moved");
        Assert(ReferenceEquals(world.OriginCache["T"], t46) && Kept(world, t47) == "T|False|47|47|post-mesh", "The cache keeps the same origin object and T-47 keeps its displayed mile value");
        (world, t46, t47, scene) = Setup();
        Assert(new PatchEngine(scene).Apply(Doc(Item("rebind", Bind(Detach)))).Outcomes["rebind"].Applied && !world.OriginCache.ContainsKey("T"), "An absent cache entry stays absent: the edit populates nothing");
        (world, t46, t47, scene) = Setup();
        world.OriginCache["T"] = t46; world.DropCacheOnLocation = true;
        var outcome = new PatchEngine(scene).Apply(Doc(Item("rebind", Bind(At("Stky0cg9c", "b", 1))))).Outcomes["rebind"];
        Assert(!outcome.Applied && outcome.Error.Contains("origin cache"), "A write that loses the cache entry fails: " + outcome.Error);
    }
    static void PreflightRefusalLeavesTheSceneUnchanged() {
        var (world, t46, t47, scene) = Setup();
        t46.Pattern = "Another component consumes this marker's location changes; its dependent state is not supported";
        var result = new PatchEngine(scene).Apply(Doc(Item("refused", Bind(At("Stky0cg9c", "a", 1)) + Place(1030))));
        Assert(!result.Outcomes["refused"].Applied && world.Mutations == 0 && Entry(world, t46) == "Stvw|b|51.20403" && t46.Position[0] == 20, "A milepost outside the supported pattern is refused before mutation");
        (world, t46, t47, scene) = Setup();
        world.Buckets["Stky0cg9c"]["Mdow"] = ("a", 1, t47);
        result = new PatchEngine(scene).Apply(Doc(Item("foreign", Bind(Detach))));
        Assert(!result.Outcomes["foreign"].Applied && result.Outcomes["foreign"].Error.Contains("registration") && world.Mutations == 0, "An incoherent registration refuses before mutation: " + result.Outcomes["foreign"].Error);
    }
    static void PostMutationFailureRestoresOrGuardsRetry() {
        var injections = new Action<FakeMilepostWorld>[] { w => w.ThrowAfterLocation = true, w => w.SkipRegister = true, w => w.ChangeMileageOnLocation = true };
        for (var index = 0; index < injections.Length; index++) { var inject = injections[index];
            var (world, t46, t47, scene) = Setup();
            world.OriginCache["T"] = t46;
            var engine = new PatchEngine(scene);
            var keep = engine.Apply(Doc(Item("keep", Bind(Detach), T47))).Outcomes["keep"];
            inject(world); var injectedMileage = world.ChangeMileageOnLocation;
            var result = engine.Apply(Doc(Item("rebind", Bind(At("Stky0cg9c", "a", 5)) + Place(1030))));
            var outcome = result.Outcomes["rebind"];
            if (injectedMileage) {
                // The injected change of the mile value is not restored by the marker steps, so cleanup stays unverified.
                Assert(!outcome.Applied && outcome.Error.Contains("mile value") && outcome.Error.Contains("cleanup is unverified"), "A changed mile value fails and cannot be verified as restored: " + outcome.Error);
                var retry = engine.Apply(Doc(Item("rebind", Place(1040)))).Outcomes["rebind"];
                Assert(!retry.Applied && retry.Error.Contains("cleanup") && t46.Position[0] == 20, "Retry of the unverified milepost is guarded: " + retry.Error);
                continue;
            }
            Assert(!outcome.Applied && outcome.Error.StartsWith("Mutation failed and was restored", StringComparison.Ordinal), "A failure after mutation is restored: " + index + " " + outcome.Error);
            Assert(Entry(world, t46) == "Stvw|b|51.20403" && t46.Position[0] == 20 && ReferenceEquals(world.OriginCache["T"], t46) && Kept(world, t46) == "T|True|46|46|post-mesh", "The captured binding, registration, placement and cache return");
            Assert(keep.Applied && world.Registrations(t47).Count == 0 && t47.Segment == null, "Prior independent work survives");
        }
        var (w, p46, p47, s) = Setup();
        w.StickyRegistration = true;
        var e = new PatchEngine(s);
        var sticky = e.Apply(Doc(Item("rebind", Bind(At("Stky0cg9c", "a", 5))))).Outcomes["rebind"];
        Assert(!sticky.Applied && sticky.Error.Contains("cleanup is unverified"), "Incomplete cleanup is reported: " + sticky.Error);
        w.StickyRegistration = false; var mutations = w.Mutations;
        Assert(!e.Apply(Doc(Item("other", Place(1040)))).Outcomes["other"].Applied && w.Mutations == mutations, "Another operation on the unverified milepost is blocked");
    }
    static readonly TargetIdentity Post46 = new TargetIdentity("BushnellWhittier", T46, "Helpers.MilePost", MilepostScene.Structure);
    static readonly TargetIdentity Post47 = new TargetIdentity("BushnellWhittier", T47, "Helpers.MilePost", MilepostScene.Structure);
    static (FakeMilepostWorld world, FakeMarker t46, FakeMarker t47, PatchEditingSession session, NativeMapEditor mapEditor, Store store) Session(string patch = null) {
        var (world, t46, t47, scene) = Setup();
        var store = new Store { Patch = patch }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(scene), mapEditor, store); mapEditor.Session = session;
        return (world, t46, t47, session, mapEditor, store);
    }
    static Newtonsoft.Json.Linq.JObject Only(PatchEditingSession session) => (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(session.ScenePatchFileText)["modifications"].Single().First;
    static void EditingSetsPlacementAndBindingWithUndoRedo() {
        var (world, t46, t47, session, mapEditor, store) = Session();
        world.OriginCache["T"] = t46;
        var bound = session.Bind(Post46, MarkerBinding.At(new TrackLocation("Stky0cg9c", "b", 266.9192)));
        Assert(bound.Applied && Entry(world, t46) == "Stky0cg9c|b|266.9192" && t46.Position[0] == 20, "Scene patch editing rebinds the milepost without moving it: " + bound.Error);
        var item = Only(session);
        Assert((string)item["target"]["structure"] == "milepost-v1" && (string)item["target"]["component"] == "Helpers.MilePost" && (string)item["relationships"]["trackMarker"]["segment"] == "Stky0cg9c" && item["placement"] == null,
            "The scene patch file records the binding apart from the placement: " + item);
        Assert(session.MinimumPatcherVersion == "0.5.0", "A milepost edit requires patcher 0.5.0");
        var moved = session.Edit(Post46, new Placement(new[] { 1030.0, 1, 2 }, new[] { 0.0, 1, 0, 0 }), null);
        Assert(moved.Applied && t46.Position[0] == 30 && Entry(world, t46) == "Stky0cg9c|b|266.9192", "A placement edit keeps the binding: " + moved.Error);
        Assert(Only(session)["placement"] != null && Only(session)["relationships"] != null, "Placement and binding are separate settings in one modification");
        var disabled = session.Edit(Post46, null, false);
        Assert(disabled.Applied && !t46.Active && t46.Held && world.Registrations(t46).Count == 0, "An explicit disable holds the post inactive: " + disabled.Error);
        var detached = session.Bind(Post46, MarkerBinding.Detached);
        Assert(detached.Applied && t46.Segment == null && t46.Position[0] == 30, "Detach clears only the binding: " + detached.Error);
        Assert(ReferenceEquals(world.OriginCache["T"], t46) && Kept(world, t46) == "T|True|46|46|post-mesh" && Kept(world, t47) == "T|False|47|47|post-mesh", "No edit changes the cache, models or mile values");
        mapEditor.Undo(); Assert(t46.Segment == "Stky0cg9c" && !t46.Active && t46.Held, "Undo of the detach restores the binding and keeps the disable");
        mapEditor.Undo(); Assert(t46.Active && !t46.Held && Entry(world, t46) == "Stky0cg9c|b|266.9192", "Undo of the disable returns the post to its group");
        mapEditor.Undo(); Assert(t46.Position[0] == 20 && t46.Rotation[3] == 1 && Entry(world, t46) == "Stky0cg9c|b|266.9192", "Undo of the placement restores the prior placement and keeps the binding");
        mapEditor.Undo(); Assert(Entry(world, t46) == "Stvw|b|51.20403" && session.ScenePatchFileText.Contains("\"modifications\": {}"), "Undo of the binding restores the native binding");
        mapEditor.Redo(); mapEditor.Redo(); mapEditor.Redo(); mapEditor.Redo();
        Assert(t46.Segment == null && t46.Position[0] == 30 && !t46.Active && t46.Held, "Redo reapplies placement, disable and detach");
        Assert(ReferenceEquals(world.OriginCache["T"], t46) && Kept(world, t46) == "T|True|46|46|post-mesh", "Undo and Redo keep the cache and mile value");
        Assert(Entry(world, t47) == "Stky0cg9c|b|266.9192" && t47.Position[0] == 60, "The other milepost is untouched");
    }
    static void RemovalRecomputesUnderlyingState() {
        var (world, t46, t47, session, mapEditor, store) = Session();
        session.Bind(Post46, MarkerBinding.At(new TrackLocation("Stky0cg9c", "a", 7)));
        session.Edit(Post46, new Placement(new[] { 1030.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), false);
        Assert(session.Bind(Post47, MarkerBinding.Detached).Applied, "A second post's binding is a separate contribution");
        var cleared = session.Bind(Post46, null);
        Assert(cleared.Applied && t46.Segment == "Stvw" && !t46.Active && t46.Position[0] == 30, "Clearing the binding returns the underlying binding and keeps placement and disable: " + cleared.Error);
        var removed = session.Remove(Post46);
        Assert(removed.Applied && Entry(world, t46) == "Stvw|b|51.20403" && t46.Position[0] == 20 && t46.Active && !t46.Held, "Removal recomputes the underlying binding, placement and group activity: " + removed.Error);
        Assert(t47.Segment == null && world.Registrations(t47).Count == 0, "Removal keeps this mod's contribution to the other post");
        mapEditor.Undo(); Assert(t46.Segment == "Stvw" && t46.Position[0] == 30 && !t46.Active, "Undo of the removal restores this mod's values");
        Assert(Kept(world, t46) == "T|True|46|46|post-mesh", "Removal and its Undo keep the mile value");
    }
    // A milepost modification needs the milepost capability whether or not this build offers it.
    static void VersionHoldsWithoutTheCapability() {
        var store = new Store { Patch = Doc(Item("move-t46", Place(1030))) }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(new Scene { Maps = new[] { "map" } }), mapEditor, store); mapEditor.Session = session;
        Assert(session.MinimumPatcherVersion == MilepostScene.RequiredVersion, "A milepost scene patch file requires the milepost capability version: " + session.MinimumPatcherVersion);
    }
    static void SaveAndReloadReproduceTheOperationOnce() {
        var (world, t46, t47, session, mapEditor, store) = Session();
        session.Bind(Post46, MarkerBinding.At(new TrackLocation("Stky0cg9c", "b", 266.9192)));
        session.Edit(Post46, new Placement(new[] { 1025.0, 1, 2 }, new[] { 0.0, 1, 0, 0 }), null);
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0"), "Save raises the dependency to the milepost version: " + store.Info);
        var (fresh, f46, f47, scene) = Setup();
        var engine = new PatchEngine(scene);
        Assert(engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && Entry(fresh, f46) == "Stky0cg9c|b|266.9192" && f46.Position[0] == 25 && f46.Rotation[1] == 1, "A reloaded world reproduces the saved placement and binding");
        engine.Apply(store.Patch);
        Assert(fresh.Buckets.Values.Sum(b => b.Count(e => e.Key == "Mdow")) == 1 && f46.Subscriptions == 1 && f46.Position[0] == 25, "A repeated world-load callback leaves one registration and one subscription without accumulated movement");
        Assert(Kept(fresh, f46) == "T|True|46|46|post-mesh", "The reloaded post keeps its mile value");
        var (w2, r46, r47, reopened, e2, st2) = Session(store.Patch);
        Assert(reopened.Blocked == null && reopened.Bind(Post46, MarkerBinding.Detached).Applied && r46.Segment == null, "A saved milepost scene patch file reopens for editing");
    }
}
