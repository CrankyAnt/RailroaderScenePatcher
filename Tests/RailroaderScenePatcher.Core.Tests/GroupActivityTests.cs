// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// A Generic marker object whose TrackRelativePosition makes its activity follow the bound segment's track group.
static class GroupActivityTests {
    const string SignPath = "World/Large Scenery/Alarka Signs/Speed Sign", OtherPath = "World/Large Scenery/Alarka Signs/Yard Limit";
    static readonly TargetIdentity Sign = new TargetIdentity("BushnellWhittier", SignPath, "Track.TrackMarker", TrackMarkerScene.Structure);
    static readonly TargetIdentity Other = new TargetIdentity("BushnellWhittier", OtherPath, "Track.TrackMarker", TrackMarkerScene.Structure);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Target(string path = SignPath) => "\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"}";
    static string Item(string id, string content, string path = SignPath) => "{\"id\":\"" + id + "\"," + Target(path) + "," + content + ",\"dependsOn\":[]}";
    const string ToExtension = "\"relationships\":{\"trackMarker\":{\"segment\":\"Nnew\",\"end\":\"a\",\"distance\":5}}";
    const string Moved = "\"placement\":{\"mode\":\"fixed\",\"position\":[1030,1,2],\"rotation\":[0,0,0,1]}";
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[" + items + "]}";
    static (FakeMarkerWorld world, FakeMarker sign, FakeMarker other, TrackMarkerScene scene) Setup() {
        var world = new FakeMarkerWorld();
        var sign = new FakeMarker { Id = "Mwp1", Segment = "S9sc", End = "a", Distance = 120 };
        var other = new FakeMarker { Id = "Mkwd", Segment = "Sjdl", End = "b", Distance = 38.6, Position = new double[] { 70, 3, 4 } };
        world.Objects[SignPath] = new List<object> { sign }; world.Objects[OtherPath] = new List<object> { other };
        world.Relative(sign); world.Relative(other);
        world.Load(other); world.LoadFollower(sign);
        return (world, sign, other, new TrackMarkerScene(world, Capability.Unchecked));
    }
    static string Registered(FakeMarkerWorld world, FakeMarker m) {
        var entries = world.Registrations(m);
        return entries.Count == 0 ? "none" : entries.Count == 1 ? entries[0].Bucket : entries.Count + " entries";
    }
    // The observed group, the handler count and the hold, as one comparable value.
    static string Subscription(FakeMarker m) => (m.Observing ? m.ObservedGroup ?? "-" : "unobserved") + "|" + m.Subscriptions + (m.Held ? "|held" : "");
    public static void Run() {
        RebindingMovesTheGroupSubscription();
        ExplicitDisableHoldsThroughGroupActivation();
        WithoutDisableGroupActivityContinues();
        ExplicitEnableOfAFollowerIsRefused();
        RemovingTheDisableReturnsActivityToTheGroup();
        FailedApplicationRestoresSubscriptionAndHold();
        TrackFollowingKeepsGroupActivity();
        SaveAndReloadReproduceTheHoldOnce();
        Console.WriteLine("PASS: group subscriptions, explicit-disable precedence, native group activity and restoration");
    }
    static void RebindingMovesTheGroupSubscription() {
        var (world, sign, other, scene) = Setup();
        Assert(Subscription(sign) == "ela|1" && sign.Active && Registered(world, sign) == "S9sc", "The follower observes its bound group at load");
        var engine = new PatchEngine(scene);
        var applied = engine.Apply(Doc(Item("rebind", ToExtension))).Outcomes["rebind"];
        Assert(applied.Applied && Subscription(sign) == "extension|1" && sign.Active && Registered(world, sign) == "Nnew", "Rebinding moves the single subscription to the new group: " + applied.Error + " " + Subscription(sign));
        for (var i = 0; i < 3; i++) Assert(engine.Apply(Doc(Item("rebind", ToExtension))).Outcomes["rebind"].Applied, "Repeated application succeeds");
        Assert(Subscription(sign) == "extension|1" && Registered(world, sign) == "Nnew", "Repeated application keeps one subscription and one registration");
        world.SetGroupEnabled("ela", false);
        Assert(sign.Active && Registered(world, sign) == "Nnew", "The old group no longer controls the object");
        world.SetGroupEnabled("ela", true); world.SetGroupEnabled("extension", false);
        Assert(!sign.Active && Registered(world, sign) == "none", "The new group deactivates it and the native lifecycle removes the registration");
        world.SetGroupEnabled("extension", true);
        Assert(sign.Active && Registered(world, sign) == "Nnew", "The new group activates it again");
        Assert(Subscription(other) == "unobserved|0" && Registered(world, other) == "Sjdl", "The unrelated marker is untouched");
        // Detaching ends the subscription: the native observation needs a location.
        Assert(new PatchEngine(scene).Apply(Doc(Item("detach", "\"relationships\":{\"trackMarker\":{\"detach\":true}}"))).Outcomes["detach"].Applied && Subscription(sign) == "unobserved|0", "A detached follower observes no group");
    }
    static void ExplicitDisableHoldsThroughGroupActivation() {
        var (world, sign, other, scene) = Setup();
        var engine = new PatchEngine(scene);
        var doc = Doc(Item("disable", ToExtension + ",\"active\":false"));
        var applied = engine.Apply(doc).Outcomes["disable"];
        Assert(applied.Applied && !sign.Active && sign.Held && Subscription(sign) == "extension|1|held" && Registered(world, sign) == "none", "An explicit disable holds the follower inactive under its new group: " + applied.Error + " " + Subscription(sign));
        Assert(sign.Activations == 0, "Applying the disable never activated the object");
        for (var i = 0; i < 3; i++) Assert(engine.Apply(doc).Outcomes["disable"].Applied, "Repeated application succeeds");
        Assert(sign.Activations == 0 && !sign.Active && Subscription(sign) == "extension|1|held", "Repeated application while disabled never activates the object, even briefly");
        world.SetGroupEnabled("extension", false); world.SetGroupEnabled("extension", true);
        Assert(!sign.Active && sign.Activations == 0 && Registered(world, sign) == "none", "Later group activation does not reactivate the disabled object");
        Assert(engine.Apply(doc).Outcomes["disable"].Applied && sign.Activations == 0, "A repeat after that group activation still never activates it");
        world.SetGroupEnabled("extension", false); world.SetGroupEnabled("extension", true);
        Assert(!sign.Active && sign.Activations == 0, "Group activation after the repeat still does not reactivate it");
    }
    static void WithoutDisableGroupActivityContinues() {
        var (world, sign, other, scene) = Setup();
        var applied = new PatchEngine(scene).Apply(Doc(Item("move", Moved))).Outcomes["move"];
        Assert(applied.Applied && sign.Position[0] == 30 && Subscription(sign) == "ela|1" && sign.Active, "A placement leaves the subscription and activity to the group: " + applied.Error);
        world.SetGroupEnabled("ela", false);
        Assert(!sign.Active, "The group still deactivates the placed object");
        Assert(new PatchEngine(scene).Apply(Doc(Item("move", Moved))).Outcomes["move"].Applied && !sign.Active, "Repeated placement does not override the disabled group");
        world.SetGroupEnabled("ela", true);
        Assert(sign.Active && Registered(world, sign) == "S9sc", "The group activates the placed object again");
    }
    static void ExplicitEnableOfAFollowerIsRefused() {
        var (world, sign, other, scene) = Setup();
        world.SetGroupEnabled("ela", false); world.Mutations = 0;
        var refused = new PatchEngine(scene).Apply(Doc(Item("enable", Moved + ",\"active\":true") + "," + Item("independent", "\"active\":false", OtherPath))).Outcomes;
        Assert(!refused["enable"].Applied && refused["enable"].Error.Contains("follows track group ela") && !sign.Active && sign.Position[0] == 20, "An explicit enable of a group follower is refused before mutation: " + refused["enable"].Error);
        Assert(refused["independent"].Applied && !other.Active, "Independent work still applies");
    }
    static (FakeMarkerWorld world, FakeMarker sign, FakeMarker other, AuthoringSession session, Editor editor, Store store) Session(string patch = null, TrackScene tracks = null) {
        var (world, sign, other, markers) = Setup();
        var capabilities = new SceneCapabilities(() => world.MapIds) { Tracks = tracks };
        capabilities.Add(markers);
        var store = new Store { Patch = patch }; var editor = new Editor();
        var session = new AuthoringSession(new PatchEngine(capabilities), editor, store, "map"); editor.Session = session;
        return (world, sign, other, session, editor, store);
    }
    static JObject Only(AuthoringSession session) => (JObject)JObject.Parse(session.DocumentText)["modifications"].Single();
    static void RemovingTheDisableReturnsActivityToTheGroup() {
        var (world, sign, other, session, editor, store) = Session();
        Assert(!session.Edit(Sign, null, true).Applied && editor.UndoGroups.Count == 0, "The editor refuses an explicit enable of a follower");
        Assert(session.Edit(Sign, null, false).Applied && !sign.Active && sign.Held && sign.Activations == 0, "The editor disables the follower with a hold");
        Assert(session.Bind(Sign, MarkerBinding.At(new TrackLocation("Nnew", "a", 5))).Applied && Subscription(sign) == "extension|1|held" && !sign.Active && sign.Activations == 0, "Rebinding a disabled follower moves the subscription and keeps the hold");
        world.SetGroupEnabled("extension", false);
        var cleared = session.ClearActivity(Sign);
        Assert(cleared.Applied && !sign.Held && !sign.Active && Subscription(sign) == "extension|1", "Clearing the disable returns activity to the disabled group: " + cleared.Error);
        Assert(Only(session)["active"] == null && Only(session)["relationships"] != null, "The modification keeps its binding without an activity value");
        world.SetGroupEnabled("extension", true);
        Assert(sign.Active && Registered(world, sign) == "Nnew", "The group then activates the object");
        editor.Undo();
        Assert(!sign.Active && sign.Held, "Undo of the clearing restores the hold");
        world.SetGroupEnabled("extension", false); world.SetGroupEnabled("extension", true);
        Assert(!sign.Active, "The restored hold keeps precedence over group activation");
        Assert(!session.ClearActivity(Other).Applied, "Clearing an absent activity value is refused");
        Assert(session.Remove(Sign).Applied && !sign.Held && sign.Active && Subscription(sign) == "ela|1" && Registered(world, sign) == "S9sc", "Removal returns the native binding, subscription and group-driven activity");
        world.SetGroupEnabled("ela", false);
        Assert(!sign.Active, "After removal the native group drives activity");
        world.SetGroupEnabled("ela", true);
        editor.Undo();
        Assert(sign.Held && !sign.Active && Subscription(sign) == "extension|1|held", "Undo of the removal restores the disable and the new subscription");
    }
    static void FailedApplicationRestoresSubscriptionAndHold() {
        var (world, sign, other, scene) = Setup();
        world.SkipObserve = true;
        var failed = new PatchEngine(scene).Apply(Doc(Item("rebind", ToExtension))).Outcomes["rebind"];
        Assert(!failed.Applied && failed.Error.StartsWith("Mutation failed and was restored", StringComparison.Ordinal) && failed.Error.Contains("group"), "A lost subscription fails the rebinding: " + failed.Error);
        Assert(Subscription(sign) == "ela|1" && sign.Active && Registered(world, sign) == "S9sc" && sign.Segment == "S9sc", "Restoration returns the location, registration and subscription: " + Subscription(sign));
        var (w2, s2, o2, session, editor, store) = Session();
        Assert(session.Edit(Sign, null, false).Applied && s2.Held, "The follower is disabled");
        w2.FailHold = true; w2.SkipObserve = true;
        var unverified = session.ClearActivity(Sign);
        Assert(!unverified.Applied && unverified.Error.Contains("cleanup is unverified") && Only(session)["active"] != null, "An unverifiable release of the hold is reported and the document keeps the disable: " + unverified.Error);
        Assert(!session.Edit(Sign, null, false).Applied, "The unverified follower refuses another edit");
        w2.FailHold = null;
        Assert(session.Recheck(Sign).Applied && s2.Held && !s2.Active, "Recheck reconciles the hold with the document before unblocking");
        Assert(session.ClearActivity(Sign).Applied && !s2.Held && s2.Active, "The verified follower accepts edits again");
    }
    static void TrackFollowingKeepsGroupActivity() {
        var tracks = new TrackScene();
        tracks.Poses["node:N1"] = new TrackPose(new[] { 1050.0, 1, 2 }, new[] { 0.0, 0, 0, 1 });
        var (world, sign, other, session, editor, store) = Session(tracks: tracks);
        Assert(session.Follow(Sign, TrackAnchor.Endpoint("N1")).Applied && Subscription(sign) == "ela|1" && sign.Active, "Following a track leaves the bound group's subscription and activity");
        Assert(session.Edit(Sign, null, false).Applied && !sign.Active && sign.Activations == 0, "The following follower is disabled");
        tracks.Poses["node:N1"] = new TrackPose(new[] { 1060.0, 1, 2 }, new[] { 0.0, 0, 0, 1 });
        var moved = session.ReconcileTrackFollowing().Single().Value;
        Assert(moved.Applied && sign.Position[0] == 30 && !sign.Active && sign.Activations == 0 && Subscription(sign) == "ela|1|held", "The moved track carries the disabled follower without activating it: " + moved.Error);
        world.SetGroupEnabled("ela", false); world.SetGroupEnabled("ela", true);
        Assert(!sign.Active && sign.Activations == 0, "Group activation after the track move keeps the disable");
        Assert(session.ClearActivity(Sign).Applied && sign.Active && Subscription(sign) == "ela|1", "Without the disable the group drives activity again");
        tracks.Poses["node:N1"] = new TrackPose(new[] { 1070.0, 1, 2 }, new[] { 0.0, 0, 0, 1 });
        world.SetGroupEnabled("ela", false);
        Assert(session.ReconcileTrackFollowing().Single().Value.Applied && sign.Position[0] == 40 && !sign.Active, "Following the track does not override the disabled group");
    }
    static void SaveAndReloadReproduceTheHoldOnce() {
        var (world, sign, other, session, editor, store) = Session();
        session.Bind(Sign, MarkerBinding.At(new TrackLocation("Nnew", "a", 5)));
        session.Edit(Sign, null, false);
        Assert(session.MinimumPatcherVersion == "0.5.0", "An activity value on a track-marker target needs 0.5.0");
        Assert(editor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0"), "Save raises the dependency: " + store.Info);
        var (fresh, freshSign, freshOther, freshScene) = Setup();
        var engine = new PatchEngine(freshScene);
        Assert(engine.Apply(store.Patch).Outcomes.Values.Single().Applied && !freshSign.Active && Subscription(freshSign) == "extension|1|held" && freshSign.Activations == 0, "A reload reproduces the binding, subscription and hold without activating the object");
        engine.Apply(store.Patch);
        Assert(Subscription(freshSign) == "extension|1|held" && freshSign.Activations == 0 && fresh.Buckets.Values.Sum(b => b.Count) == 1, "A repeated world-load callback leaves one subscription and only the unrelated registration");
        var unloaded = Session(store.Patch.Replace(SignPath, "World/Absent"));
        Assert(unloaded.session.MinimumPatcherVersion == "0.5.0", "The version follows the document even when the target is not loaded");
    }
}
