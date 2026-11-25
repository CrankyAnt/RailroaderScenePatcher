// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RailroaderScenePatcher.Core;
using Fixture = BakedPartTests.Fixture;

// Own culling for parts of baked scenery groups that a move takes out of their culler's sphere, against the controlled
// saw mill group: the group root stands at map 1100, 10, 20 with a simple culler of radius 10; Platform, Ramp and Lamp
// stand below the root, the jetty at 1100, 10, 14 with a renderer culler of its own, and the post below the jetty at
// 1101, 10, 14. Every part fills a cube of one metre. The tests set what the camera sees: the game's sphere of a simple
// culler through See, and an own sphere through Show.
static class PartCullingTests {
    const string SceneName = "BushnellWhittier-MapManager", Mill = "World/Large Scenery/Whittier/Whittier Saw Mill";
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    static readonly double[] Identity = { 0, 0, 0, 1 };
    static string Mod(string id, string path, string structure, double[] position, bool? active) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + N(position) + "],\"rotation\":[0,0,0,1]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string Part(string id, string name, double[] position = null, bool? active = null) => Mod(id, Mill + "/" + name, BakedSceneryParts.Structure, position, active);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static TargetIdentity PartId(string name) => new TargetIdentity(SceneName, Mill + "/" + name, BakedSceneryParts.ComponentType, BakedSceneryParts.Structure);
    static readonly TargetIdentity GroupId = new TargetIdentity(SceneName, Mill, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b, double tolerance = 0.001) => a != null && a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= tolerance);
    static (Store, NativeMapEditor, PatchEditingSession) Session(Fixture f) {
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        return (store, mapEditor, session);
    }
    static FakeCuller Root(Fixture f) => f.Group.Cullers.Single(c => c.OnRoot);
    static FakeCuller OnJetty(Fixture f) => f.Group.Cullers.Single(c => c.On == f.Jetty);
    // The saw mill without the jetty's renderer culler, so the root's simple culler is the only culler.
    static Fixture Plain() { var f = new Fixture(); f.Group.Cullers.RemoveAll(c => c.On == f.Jetty); return f; }
    static bool Applies(Fixture f, params string[] items) { var result = f.Engine.Apply(Doc(items)); return result.Outcomes.Values.All(o => o.Applied); }
    static string Error(Fixture f, params string[] items) => String.Join("; ", f.Engine.Apply(Doc(items)).Outcomes.Values.Select(o => o.Error));
    static List<FakeRegistration> Spheres(Fixture f) => f.World.Native.Registered;
    static readonly double[] FarPost = { 1160, 10, 14 };
    // The saw mill with the post moved 59 m out of the root culler's sphere, and the post's own sphere.
    static (Fixture, FakeRegistration) WithFarPost() {
        var f = Plain();
        Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost)), "The far post move applies");
        return (f, Spheres(f).Single());
    }

    public static void Run() {
        PureRules();
        MovedPartOutsideItsCullerGetsItsOwnSphere();
        NoMoveChangesACullersRadius();
        PartInsideOrUneditedGetsNoSphere();
        WholeGroupMoveRecomputesOwnSpheres();
        NestedMovedPartsFollowTheFirstEnclosingSphere();
        PartWithItsOwnSimpleCullerKeepsIt();
        ActivityFollowsTheSpheresEachObjectNeeds();
        DisabledPartGetsNoSphereAndStaysOff();
        DisabledGroupPausesOwnSpheres();
        ObservedActivityIsTheModdersValue();
        FailureWithOwnSpheresRestores();
        HistoryRemovalReleaseAndReload();
        CancelledGestureReleasesEverything();
        RendererCullerOnTheMovedPartKeepsCulling();
        TwoCullerKindsKeepTwoSpheres();
        ListedObjectIsHandedOverAndReturns();
        ListedObjectStaysItsCullers();
        MovedPartBelowAListedObjectShows();
        MovedPartBelowAnEmptyListedObjectLeavesNoSphereBehind();
        PresenceMayBeEmptyAndTheAnchorStandsInForIt();
        EmptyListedObjectKeepsAStandInForAGameSphere();
        SeveralMovedPartsBelowAnEmptyListedObject();
        ListedObjectWithPresenceOfItsOwnKeepsItsStandIn();
        EmptyListedObjectThroughUndoRedoAndRemove();
        UnreadableBoundsCountAsPresence();
        NestedListedObjectsPlanTheSameInAnyOrder();
        MovedPartBelowAMovedListedObjectShows();
        PartInsideAListedSphereButOutsideTheSimpleSphereGetsItsOwn();
        GameObjectCullersStandOnGradeCrossings();
        NoListedObjectIsABakedSceneryPart();
        EndingThePatcherReturnsTheCullingToTheGame();
        InstallWaitsForTheGamesCuller();
        StaleCallbacksWriteNothing();
        UpdateFailureIsWrittenOncePerGroupAndFailure();
        UnloadReadsNoCullingManager();
        LeftoverWrapperIsReported();
        InspectorNamesOwnCulling();
        Console.WriteLine("PASS: own culling of moved parts: spheres, activity, renderer and listed hand-over, load order, liveness, unload and scene patch editing");
    }

    static void PureRules() {
        Assert(CullingActivity.Active(true, false, new[] { false, true }) && !CullingActivity.Active(true, false, new[] { false, false }), "An object is on while any sphere it needs is visible");
        Assert(!CullingActivity.Active(false, false, new[] { true }) && !CullingActivity.Active(false, true, new bool[0]), "An object that is not desired stays off, whatever is visible");
        Assert(CullingActivity.Active(true, true, new bool[0]) && !CullingActivity.Active(true, true, new bool[0], gate: false), "An object above every simple culler follows only the sphere that took it over");
        Assert(CullingActivity.Desired(false, true) && !CullingActivity.Desired(true, false) && CullingActivity.Desired(true, null) && !CullingActivity.Desired(false, null), "The desired activity is own culling's value for an object it switches, and the object's own activity otherwise");
        Assert(CullingActivity.Shows(CullerKind.Simple, false, true, 3, 0) && !CullingActivity.Shows(CullerKind.Simple, false, false, 0, 3), "A simple culler has no distance band");
        Assert(CullingActivity.Shows(CullerKind.Renderer, false, true, 1, 1) && !CullingActivity.Shows(CullerKind.Renderer, false, true, 2, 1) && !CullingActivity.Shows(CullerKind.Renderer, false, false, 0, 1), "A renderer culler shows what is visible within its band");
        Assert(CullingActivity.Shows(CullerKind.GameObject, true, false, 1, 1) && !CullingActivity.Shows(CullerKind.GameObject, true, true, 2, 1) && !CullingActivity.Shows(CullerKind.GameObject, false, false, 1, 1), "A game-object culler in distance-band mode ignores visibility");
        object a = new object(), b = new object(), c = new object();
        var union = ListedObjects.Union(new[] { a, null, b }, new[] { b, c });
        Assert(union.Count == 3 && union[0] == a && union[1] == b && union[2] == c, "The listed objects are those in the list and those taken out of it, each once");
        Assert(ListedObjects.Union(null, null).Count == 0, "An absent list holds nothing");
        var group = new object();
        var disabled = new GroupObservation(new ObjectState(new Placement(new double[3], Identity), false), new GroupPart[0], new GroupCuller[0], null);
        Assert(CullingPlan.Compute(group, disabled, new object[0], null).Suspended && CullingPlan.Empty.IsEmpty, "A disabled group plans no own culling");
    }

    // The jetty with the post below it fills 1 m by 1 m by 2 m: its bounds are centred half a metre along X from the
    // jetty, and the farthest corner is sqrt(1 + 0.25 + 0.25) = 1.2247 m from that centre.
    static void MovedPartOutsideItsCullerGetsItsOwnSphere() {
        foreach (var distance in new[] { 60.0, 5000, 50000 }) {
            var f = Plain(); var root = Root(f);
            Assert(Applies(f, Part("far-jetty", "Jetty", new[] { 1100 + distance, 10, 20 })), "A move of " + distance + " m applies");
            var own = f.World.Observe(f.Group).Cullers.Single(c => c.Id == root.Id).Regions.Single();
            Assert(ReferenceEquals(own.Owner, f.Jetty) && Near(own.Centre, new[] { 1100.5 + distance, 10, 20 }) && Math.Abs(own.Radius - 1.2247) < 0.001,
                "At " + distance + " m the part has a sphere of its own, centred on its bounds and enclosing them: " + N(own.Centre) + " r " + own.Radius);
            Assert(root.Radius == 10 && root.Authored == 10, "The culler keeps the radius the scene gives it: " + root.Radius);
        }
    }

    static void NoMoveChangesACullersRadius() {
        var f = new Fixture();
        Assert(Applies(f, Part("far-jetty", "Jetty", new[] { 6100.0, 10, 20 }), Part("far-ramp", "Ramp", new[] { 1100.0, 10, 5020 })), "Two far moves apply");
        Assert(f.Group.Cullers.All(c => c.Radius == 10), "No part move enlarges any culler's sphere: " + N(f.Group.Cullers.Select(c => c.Radius)));
        // A culler whose sphere no longer has the scene's radius fails the verification of the next edit.
        var g = new Fixture(); Root(g).Radius = 25;
        var error = Error(g, Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 }));
        Assert(error.Contains("left the radius the scene gives it"), "A changed culler radius fails verification: " + error);
    }

    static void PartInsideOrUneditedGetsNoSphere() {
        var f = Plain();
        Assert(Applies(f, Part("near-jetty", "Jetty", new[] { 1102.0, 10, 20 })) && Spheres(f).Count == 0 && f.World.Plan.IsEmpty && Root(f).Handler == null,
            "A part moved inside its culler's sphere gets no sphere of its own, and the culler stays the game's");
        // An unedited part whose bounds reach outside the sphere stays as the game culls it.
        var g = Plain(); g.Platform.Size = 40;
        Assert(Applies(g, Part("near-ramp", "Ramp", new[] { 1103.0, 10, 20 })) && Spheres(g).Count == 0, "An unedited part never gets a sphere of its own");
        // A part held at the placement the scene gives it is not moved; once it moves, its bounds decide.
        var h = Plain(); h.Jetty.Size = 40;
        Assert(Applies(h, Part("held-jetty", "Jetty", new[] { 1100.0, 10, 14 })) && Spheres(h).Count == 0, "A part held where the scene has it gets no sphere of its own");
        Assert(Applies(h, Part("held-jetty", "Jetty", new[] { 1101.0, 10, 14 })) && Spheres(h).Single().Owner == h.Jetty, "A moved part whose bounds reach outside the sphere gets one");
    }

    // A held part keeps its map placement when its group moves, so a group move changes which side of the culler's
    // sphere it stands on. The ramp is held at 1103, 10, 20, three metres from the root.
    static void WholeGroupMoveRecomputesOwnSpheres() {
        var f = Plain(); var root = Root(f); var (_, _, session) = Session(f);
        Assert(session.Edit(PartId("Ramp"), new Placement(new[] { 1103.0, 10, 20 }, Identity), null).Applied && Spheres(f).Count == 0, "A part held inside the sphere has no sphere of its own");
        Assert(session.Edit(GroupId, new Placement(new[] { 1200.0, 10, 20 }, Identity), null).Applied, "The group moves 100 m away");
        var own = Spheres(f).Single();
        Assert(own.Owner == f.Ramp && Near(f.World.Native.Sphere(own).Take(3).ToArray(), new[] { 1103.0, 10, 20 }), "A held part that the group move takes out of the culler's sphere gets its own sphere");
        Assert(Near(root.Registered, new[] { 200.0, 10, 20 }) && root.Radius == 10, "The culler's sphere follows the group at its radius");
        Assert(session.Edit(GroupId, new Placement(new[] { 1100.0, 10, 20 }, Identity), null).Applied && Spheres(f).Count == 0 && root.Handler == null, "A held part that the group move brings back inside loses its own sphere");
        // The other way round: the jetty is held 60 m out, and the group moves to it.
        Assert(session.Edit(PartId("Jetty"), new Placement(new[] { 1160.0, 10, 20 }, Identity), null).Applied && Spheres(f).Single().Owner == f.Jetty, "A part held outside has its own sphere");
        Assert(session.Edit(GroupId, new Placement(new[] { 1158.0, 10, 20 }, Identity), null).Applied && Spheres(f).Single().Owner == f.Ramp, "The group move brings the jetty inside and takes the ramp out");
        Assert(session.Edit(GroupId, new Placement(new[] { 1100.0, 10, 20 }, Identity), null).Applied && Spheres(f).Single().Owner == f.Jetty, "Moved back, the jetty has its own sphere again");
        var sphere = Spheres(f).Single();
        Assert(session.Edit(PartId("Jetty"), new Placement(new[] { 1170.0, 10, 20 }, Identity), null).Applied && Spheres(f).Single() == sphere && Near(f.World.Native.Sphere(sphere).Take(3).ToArray(), new[] { 1170.5, 10, 20 }),
            "The own sphere moves with its part");
    }

    static void NestedMovedPartsFollowTheFirstEnclosingSphere() {
        // The post moved inside the jetty's own sphere uses that sphere.
        var f = Plain();
        Assert(Applies(f, Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 }), Part("post", "Jetty/Post", new[] { 1160.0, 10, 20 })), "The jetty and the post move");
        Assert(Spheres(f).Single().Owner == f.Jetty, "A moved part inside the own sphere of the moved part above it uses that sphere");
        // The post back inside the root culler's sphere follows that sphere again, through the jetty.
        var g = Plain(); var root = Root(g);
        Assert(Applies(g, Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 }), Part("post", "Jetty/Post", new[] { 1101.0, 10, 20 })), "The jetty moves out and the post back in");
        var jetty = Spheres(g).Single();
        Assert(jetty.Owner == g.Jetty, "The post gets no sphere of its own");
        g.World.Show(jetty, false);
        Assert(g.Jetty.Active && g.Post.Active && g.Group.Active, "While the culler's sphere is visible the post shows, and the jetty above it stays on");
        g.World.See(g.Group, root, false); g.World.Show(jetty, true);
        Assert(g.Jetty.Active && !g.Post.Active && g.Group.Active, "While only the jetty's own sphere is visible the post is off and the jetty shows");
        g.World.Show(jetty, false);
        Assert(!g.Group.Active, "With both spheres hidden the group is off");
        // The post moved out of both spheres gets a sphere of its own.
        var h = Plain();
        Assert(Applies(h, Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 }), Part("post", "Jetty/Post", new[] { 1100.0, 10, 5020 })), "Both move out");
        Assert(Spheres(h).Count == 2 && Spheres(h).Any(s => s.Owner == h.Post) && Spheres(h).Any(s => s.Owner == h.Jetty), "A moved part outside every sphere above it gets its own");
    }

    static void PartWithItsOwnSimpleCullerKeepsIt() {
        var f = new Fixture(); var root = Root(f); var own = OnJetty(f); own.Simple = true;
        var (_, _, session) = Session(f);
        Assert(session.Edit(PartId("Jetty"), new Placement(new[] { 1160.0, 10, 20 }, Identity), null).Applied, "The part with a simple culler of its own moves out");
        Assert(Spheres(f).Count == 0 && own.Radius == 10, "Its own culler stays its sphere; nothing is registered for it");
        Assert(own.Handler != null && root.Handler != null, "Own culling listens to the part's culler and sets the group root");
        f.World.See(f.Group, own, false);
        Assert(!f.Jetty.Active && f.Group.Active, "The game's culler still switches the part itself");
        f.World.See(f.Group, root, false);
        Assert(!f.Group.Active, "With both spheres hidden the group is off");
        f.World.See(f.Group, own, true);
        Assert(f.Jetty.Active && f.Group.Active && !f.Platform.Active, "While the part's own culler is visible the group root is on for it, and the rest follows the root culler");
        f.World.See(f.Group, root, true);
        Assert(session.Remove(PartId("Jetty")).Applied && own.Handler == null && root.Handler == null && f.World.Culling.Leftovers.Count == 0, "Removal puts the game's handlers back");
    }

    // The rows of the activity rule: the group root is the container, the jetty the object between the culler and the
    // moved post, the rail a part of the jetty with no moved part below it, and the platform a part of the root.
    static void ActivityFollowsTheSpheresEachObjectNeeds() {
        var f = Plain(); var root = Root(f);
        var rail = f.Group.Add(new FakePart { Id = "Jetty/Rail#6", Offset = new[] { 0.0, 0, -7 }, Parent = f.Jetty });
        Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost)), "The post moves out");
        var own = Spheres(f).Single();
        string Now() => (f.Group.Active ? "root " : "") + (f.Jetty.Active ? "jetty " : "") + (rail.Active ? "rail " : "") + (f.Post.Active ? "post " : "") + (f.Platform.Active ? "platform" : "");
        f.World.See(f.Group, root, false); f.World.Show(own, false);
        Assert(!f.Group.Active, "Both spheres hidden: the container is off");
        f.World.Show(own, true);
        Assert(Now() == "root jetty post ", "Only the own sphere visible: the container, the object between and the moved part are on; the rest is off: " + Now());
        f.World.See(f.Group, root, true); f.World.Show(own, false);
        Assert(Now() == "root jetty rail platform", "Only the culler's sphere visible: everything but the moved part is on: " + Now());
        f.World.Show(own, true);
        Assert(Now() == "root jetty rail post platform", "Both visible: everything is on: " + Now());
    }

    static void DisabledPartGetsNoSphereAndStaysOff() {
        var f = Plain();
        Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost, active: false)) && Spheres(f).Count == 0 && !f.Post.Active, "A disabled part gets no sphere of its own");
        Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost, active: true)) && Spheres(f).Count == 1 && f.Post.Active, "Enabled, the moved part gets its sphere");
        var own = Spheres(f).Single();
        var (_, _, session) = Session(f);
        Assert(session.Edit(PartId("Jetty/Post"), null, false).Applied && !f.Post.Active && Spheres(f).Count == 0, "Disabling the part releases its sphere and switches it off");
        // While its sphere still stands, a part that is not desired stays off when the sphere becomes visible.
        var (g, sphere) = WithFarPost();
        Assert(g.World.Culling.SetDesired(g.Post, false), "The moved part is one that own culling switches");
        g.World.Show(sphere, false); g.World.Show(sphere, true);
        Assert(!g.Post.Active && g.Jetty.Active, "A disabled part stays off while its own sphere is visible");
    }

    static void DisabledGroupPausesOwnSpheres() {
        var (f, _) = WithFarPost(); var root = Root(f); var (_, _, session) = Session(f);
        Assert(session.Edit(GroupId, null, false).Applied, "The group disables");
        Assert(Spheres(f).Count == 0 && root.Handler == null && !f.Group.Active && f.Post.Active && f.Platform.Active, "A disabled group holds no own sphere, and its parts keep their own activity");
        // The camera leaves the mill while the group is disabled; the moved post stays in view.
        root.Visible = false;
        Assert(session.Edit(GroupId, null, true).Applied, "The group enables");
        Assert(Spheres(f).Count == 1 && f.Group.Active && f.Post.Active && f.Jetty.Active && !f.Platform.Active, "Enabled with the culler's sphere hidden and the own sphere visible, the moved part shows and the rest stays hidden");
    }

    static void ObservedActivityIsTheModdersValue() {
        var (f, own) = WithFarPost(); var root = Root(f); var (_, _, session) = Session(f);
        var inventory = new SceneInventory(f); inventory.Refresh();
        f.World.Show(own, false);
        Assert(!f.Post.Active, "The camera hides the moved part");
        Assert(f.Parts.Read(f.Post).Active == true && f.World.Observe(f.Group).Parts.Single(p => ReferenceEquals(p.Handle, f.Post)).ActiveSelf, "The observed activity of the part is the value the modder set");
        f.World.See(f.Group, root, false);
        Assert(!f.Group.Active && f.World.Observe(f.Group).Root.Active == true, "A group the camera hides still reads as enabled");
        inventory.CheckFreshness();
        Assert(!inventory.IsStale, "Activity that own culling sets does not make the inventory stale: " + inventory.StaleReason);
        var outcome = session.Edit(PartId("Ramp"), new Placement(new[] { 1103.0, 10, 20 }, Identity), null);
        Assert(outcome.Applied, "Verification reports no activity change for what the camera hides: " + outcome.Error);
        f.World.See(f.Group, root, true); f.World.Show(Spheres(f).Single(), true);
        inventory.CheckFreshness();
        Assert(f.Post.Active && f.Platform.Active, "The camera shows everything again");
    }

    static void FailureWithOwnSpheresRestores() {
        var (f, own) = WithFarPost();
        f.World.SkipCullingRefresh = true;
        var outcome = f.Engine.Apply(Doc(Part("far-post", "Jetty/Post", new[] { 1170.0, 10, 14 }))).Outcomes["far-post"];
        Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal) && outcome.Error.Contains("does not stand at its part"), "An own sphere that stays behind fails verification: " + outcome.Error);
        Assert(Near(f.MapOf(f.Post).Position, FarPost) && Spheres(f).Single() == own && Near(f.World.Native.Sphere(own).Take(3).ToArray(), FarPost), "The captured placement and its own sphere are restored");
    }

    static void HistoryRemovalReleaseAndReload() {
        var f = Plain(); var root = Root(f); var (store, mapEditor, session) = Session(f);
        Assert(session.Edit(PartId("Jetty/Post"), new Placement(FarPost, Identity), null).Applied && Spheres(f).Count == 1, "The edit creates the own sphere");
        mapEditor.Undo();
        Assert(Spheres(f).Count == 0 && root.Handler == null && Near(f.MapOf(f.Post).Position, new[] { 1101.0, 10, 14 }), "Undo removes the own sphere the edit created");
        mapEditor.Redo();
        Assert(Spheres(f).Count == 1 && Near(f.World.Native.Sphere(Spheres(f).Single()).Take(3).ToArray(), FarPost), "Redo restores it");
        Assert(mapEditor.Save() == null, "Save succeeds");
        var fresh = Plain();
        Assert(fresh.Engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && Spheres(fresh).Count == 1, "A reload reproduces the own sphere");
        var first = Spheres(fresh).Single();
        Assert(fresh.Engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && Spheres(fresh).Single() == first && fresh.World.Culling.RegionCount == 1, "Applying again leaves one sphere, not two");
        Assert(session.Remove(PartId("Jetty/Post")).Applied && Spheres(f).Count == 0 && root.Handler == null && Near(f.MapOf(f.Post).Position, new[] { 1101.0, 10, 14 }), "Remove modification removes the own sphere and returns the part");
        // A part that was destroyed is released with its sphere.
        var (g, _) = WithFarPost();
        g.Group.Parts.Remove(g.Post);
        g.Parts.Release(g.Post);
        Assert(Spheres(g).Count == 0 && g.World.Culling.RegionCount == 0 && Root(g).Handler == null, "Releasing a destroyed part removes its own sphere");
    }

    static void CancelledGestureReleasesEverything() {
        var f = Plain(); var root = Root(f); var (_, _, session) = Session(f);
        var inventory = new SceneInventory(f.Scene); inventory.Refresh();
        var gesture = new PlacementGesture(f.Engine, session, PartId("Jetty/Post"), inventory.Pin(inventory.Entries.Single(e => ReferenceEquals(e.Handle, f.Post))));
        foreach (var x in new[] { 1160.0, 1180.0 }) {
            var preview = gesture.Preview(new Placement(new[] { x, 10.0, 14 }, Identity));
            Assert(preview.Applied && Spheres(f).Count == 1 && root.Handler != null, "The preview gives the part its own sphere: " + preview.Error);
        }
        var end = gesture.Cancel();
        Assert(end.Applied && Spheres(f).Count == 0 && f.World.Culling.RegionCount == 0 && root.Handler == null && !f.World.Culling.Waiting, "A cancelled gesture releases every registration it created: " + end.Error);
        Assert(Near(f.MapOf(f.Post).Position, new[] { 1101.0, 10, 14 }) && f.Post.Active && f.Group.Active, "The part is back where it was");
    }

    // A renderer culler on the moved part moves with the part: it keeps culling the part's renderers at its radius.
    static void RendererCullerOnTheMovedPartKeepsCulling() {
        var f = new Fixture(); var own = OnJetty(f);
        Assert(Applies(f, Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 })), "The part with a renderer culler of its own moves out");
        Assert(own.Renders.Count == 2 && own.Radius == 10 && Near(own.Registered, new[] { 160.0, 10, 20 }), "Its renderer culler keeps its renderers and its radius, and its sphere stands at the part");
        Assert(Spheres(f).Single().Kind == CullerKind.Simple && !f.World.Plan.HandOvers.Any(), "Only the simple culler above the part gets a stand-in; nothing is handed over");
    }

    static void TwoCullerKindsKeepTwoSpheres() {
        var f = Plain();
        var container = new FakeCuller { Id = ".|Helpers.Culling.RendererCuller", Simple = false, Radius = 100, Authored = 100, Renders = { f.Platform.Renderer, f.Jetty.Renderer, f.Post.Renderer } };
        f.Group.Cullers.Add(container);
        Assert(Applies(f, Part("far-jetty", "Jetty", new[] { 6100.0, 10, 20 })), "The part leaves both the renderer culler's and the simple culler's sphere");
        var simple = Spheres(f).Single(s => s.Kind == CullerKind.Simple); var renderer = Spheres(f).Single(s => s.Kind == CullerKind.Renderer);
        Assert(container.Renders.Count == 1 && container.Renders[0] == f.Platform.Renderer && container.Radius == 100, "Only the moved part's renderers leave the container, which keeps its radius");
        bool Draws() => f.Group.Active && f.Jetty.Active && f.Jetty.Renderer.Enabled;
        f.World.Show(simple, false);
        Assert(!Draws(), "Hidden by the sphere that stands in for the simple culler, the part does not draw");
        f.World.Show(simple, true); f.World.Show(renderer, false);
        Assert(!Draws(), "Hidden by the sphere that stands in for the renderer culler, the part does not draw");
        f.World.Show(renderer, true);
        Assert(Draws(), "The part draws while both its own spheres allow it");
        // 60 m away the part is inside the renderer culler's sphere again and still outside the simple culler's.
        container.Shows = false;
        Assert(Applies(f, Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 })), "The part moves back inside the renderer culler's sphere");
        Assert(Spheres(f).Single() == simple && container.Renders.Count == 3, "Releasing one own sphere leaves the other in place, and the renderers return to the renderer culler above the part");
        Assert(!f.Jetty.Renderer.Enabled && !f.Post.Renderer.Enabled && f.Platform.Renderer.Enabled, "Returned, they take that culler's current state");
    }

    static void ListedObjectIsHandedOverAndReturns() {
        var f = Plain();
        var lamps = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { f.Lamp, f.Ramp }, Radius = 6, Authored = 6 }; f.Group.Cullers.Add(lamps);
        string Refusal() => Error(f, Part("hide-lamp", "Lamp", active: false));
        var before = Refusal();
        Assert(before.Contains("culler") && f.Parts.Read(f.Lamp).Active == null, "A listed part refuses the Enabled setting and reads none");
        Assert(Applies(f, Part("far-lamp", "Lamp", new[] { 1160.0, 12, 30 })), "The listed part moves out of its game-object culler's sphere");
        Assert(lamps.Lists.Count == 1 && lamps.Lists[0] == f.Ramp && lamps.Radius == 6, "The culler's list no longer holds the moved part; its other object and its radius are unchanged");
        var observed = f.World.Observe(f.Group);
        Assert(observed.Cullers.Single(c => c.Id == lamps.Id).Listed.SequenceEqual(new object[] { f.Ramp, f.Lamp }) && observed.Parts.Single(p => ReferenceEquals(p.Handle, f.Lamp)).CullerSwitched, "Readers still see the part as listed, and the culler still counts as switching it");
        Assert(Refusal().EndsWith(before, StringComparison.Ordinal) && f.Parts.Read(f.Lamp).Active == null, "The Enabled setting is refused and reads as before the move: " + Refusal());
        var sphere = Spheres(f).Single(s => s.Kind == CullerKind.GameObject);
        f.World.Show(sphere, false);
        Assert(!f.Lamp.Active && f.Ramp.Active, "The own sphere switches the handed-over part");
        f.World.Show(sphere, true);
        Assert(f.Lamp.Active, "The own sphere shows it again");
        // Other code, such as an added crossing light, joins the same list.
        lamps.Lists.Add(f.Platform);
        Assert(Applies(f, Part("far-lamp", "Lamp", new[] { 1170.0, 12, 30 })), "The part moves again");
        Assert(lamps.Lists.SequenceEqual(new[] { f.Ramp, f.Platform }) && f.World.Culling.HandedOver(lamps).SequenceEqual(new object[] { f.Lamp }), "Objects other code adds to the list are untouched and never taken over");
        // Back inside, the part takes the culler's current visibility at once and returns to the list once.
        lamps.Shows = false;
        Assert(Applies(f, Part("far-lamp", "Lamp", new[] { 1097.0, 12, 20 })), "The part moves back inside");
        Assert(!f.Lamp.Active && lamps.Lists.Count(l => l == f.Lamp) == 1 && lamps.Lists[0] == f.Lamp && lamps.Lists.Count == 3 && !Spheres(f).Any(s => s.Kind == CullerKind.GameObject), "The part returns to the list once, hidden as its culler is now");
        Assert(Applies(f, Part("far-lamp", "Lamp", new[] { 1097.0, 12, 20 })) && lamps.Lists.Count(l => l == f.Lamp) == 1, "Applying again adds no duplicate");
    }

    // An object a game-object culler lists and own culling did not take over stays that culler's to switch.
    static void ListedObjectStaysItsCullers() {
        var f = Plain(); var root = Root(f);
        var lamps = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { f.Ramp }, Radius = 6, Authored = 6 }; f.Group.Cullers.Add(lamps);
        Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost)), "A part elsewhere in the group moves out");
        // The ramp's culler hides it.
        lamps.Shows = false; f.Ramp.Active = false;
        f.World.See(f.Group, root, false); f.World.See(f.Group, root, true);
        Assert(!f.Ramp.Active && f.Platform.Active && lamps.Lists.Single() == f.Ramp, "Own culling never switches an object that a game-object culler lists and keeps");
        Assert(Applies(f, Part("far-post", "Jetty/Post", new[] { 1170.0, 10, 14 })) && !f.Ramp.Active && !f.World.Plan.Nodes.Any(n => n.Handle == f.Ramp), "A later edit leaves it to its culler as well");
    }

    // The jetty is listed by a game-object culler and does not move; the post below it moves out. The jetty is handed
    // over like a container: it is on while its culler's sphere or the post's own sphere shows.
    static void MovedPartBelowAListedObjectShows() {
        foreach (var simple in new[] { true, false }) {
            var f = Plain();
            if (!simple) { f.Group.Cullers.Clear(); f.Group.RootCulling = null; }
            var lister = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { f.Jetty }, Radius = 8, Authored = 8 }; f.Group.Cullers.Add(lister);
            Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost)), "The part below the listed object moves out");
            var standIn = Spheres(f).Single(s => s.Owner == f.Jetty); var own = Spheres(f).Single(s => s.Owner == f.Post);
            Assert(lister.Lists.Count == 0 && lister.Radius == 8 && f.World.Culling.HandedOver(lister).Single() == f.Jetty, "The listed object above the moved part is handed over");
            Assert(Near(f.World.Native.Sphere(standIn).ToArray(), new[] { 1100.0, 10, 20, 8 }), "Its stand-in sphere is the culler's own sphere: " + N(f.World.Native.Sphere(standIn)));
            f.World.Show(standIn, false);
            Assert(f.Jetty.Active && f.Post.Active, "With the culler's sphere hidden, the moved part shows while its own sphere is visible");
            f.World.Show(own, false);
            Assert(!f.Jetty.Active, "With both hidden the listed object is off");
            f.World.Show(standIn, true);
            Assert(f.Jetty.Active && !f.Post.Active, "With only the culler's sphere visible the listed object shows and the moved part does not");
            lister.Shows = false;
            Assert(Applies(f, Part("far-post", "Jetty/Post", new[] { 1101.0, 10, 14 })), "The part moves back");
            Assert(Spheres(f).Count == 0 && lister.Lists.Single() == f.Jetty && !f.Jetty.Active, "The listed object returns to its culler's list once, with the culler's current state");
        }
    }

    // A crossing mast: the culler lists the mast's outer object, which shows nothing of its own, and the light object
    // below it moves 59 m. The outer object follows the light object's own sphere: no sphere stays in the culler's place,
    // and the light object never vanishes while its own sphere is visible, whatever the culler's sphere shows.
    static void MovedPartBelowAnEmptyListedObjectLeavesNoSphereBehind() {
        foreach (var simple in new[] { true, false }) {
            var f = Plain(); f.Jetty.Empty = true;
            if (!simple) { f.Group.Cullers.Clear(); f.Group.RootCulling = null; }
            var lister = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { f.Jetty }, Radius = 8, Authored = 8 }; f.Group.Cullers.Add(lister);
            Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost)), "The part below the empty listed object moves out");
            var own = Spheres(f).Single(s => s.Owner == f.Post);
            Assert(Spheres(f).Count == 1 && !Spheres(f).Any(s => s.Owner == f.Jetty), "Only the moved part holds an own sphere; none stays in the culler's place: " + String.Join("; ", Spheres(f).Select(s => N(f.World.Native.Sphere(s)))));
            Assert(Near(f.World.Native.Sphere(own).ToArray(), new[] { 1160.0, 10, 14, Math.Sqrt(0.75) }), "The own sphere stands at the moved part: " + N(f.World.Native.Sphere(own)));
            Assert(lister.Lists.Count == 0 && lister.Radius == 8 && f.World.Culling.HandedOver(lister).Single() == f.Jetty, "The empty listed object is handed over");
            Assert(f.World.Culling.Regions(f.Group).Select(r => r.Owner).SequenceEqual(new object[] { f.Post }), "An inspection lists only the moved part's own sphere");
            lister.Shows = false;
            Assert(f.Jetty.Active && f.Post.Active, "With the culler's sphere hidden, the moved part shows while its own sphere is visible");
            f.World.Show(own, false);
            Assert(!f.Jetty.Active, "With its own sphere hidden the empty listed object is off");
            f.World.Show(own, true);
            Assert(f.Jetty.Active && f.Post.Active, "Its own sphere shows it again");
            Assert(Applies(f, Part("far-post", "Jetty/Post", new[] { 1101.0, 10, 14 })), "The part moves back");
            Assert(Spheres(f).Count == 0 && lister.Lists.Single() == f.Jetty && !f.Jetty.Active, "The listed object returns to its culler's list once, with the culler's current state");
        }
    }

    // The game-object culler of the empty-container tests: it lists the jetty, with its sphere at the root.
    static FakeCuller Lister(Fixture f, double radius = 8) {
        var lister = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { f.Jetty }, Radius = radius, Authored = radius };
        f.Group.Cullers.Add(lister); return lister;
    }
    // Another part below the jetty, at the offset from the group root, under the path Jetty/<name>.
    static FakePart BelowJetty(Fixture f, string name, double[] offset, FakePart parent = null) {
        var path = parent == null ? "Jetty/" + name : f.World.PartsAt.Single(p => p.Value.Contains(parent)).Key.Substring(Mill.Length + 1) + "/" + name;
        var part = f.Group.Add(new FakePart { Id = path + "#" + (f.Group.Parts.Count + 1), Offset = offset, Parent = parent ?? f.Jetty });
        f.World.PartsAt[Mill + "/" + path] = new List<object> { part };
        return part;
    }

    // A renderer or collider whose bounds cannot be read, such as a renderer without a mesh on an inactive object, counts
    // with its object's position: an object whose only content it is keeps the stand-in, as the game can switch that
    // content on while the plan stands.
    static void UnreadableBoundsCountAsPresence() {
        foreach (var kind in new[] { "renderer", "collider" }) {
            var f = Plain(); f.Jetty.Empty = true; f.Jetty.Unreadable = true;
            if (kind == "renderer") f.Jetty.Renders = true; else f.Jetty.Collides = true;
            var corners = f.World.Presence(f.Jetty, new object[] { f.Post });
            Assert(corners.Count == 1 && Near(corners[0], new[] { 1100.0, 10, 14 }), "A " + kind + " with unreadable bounds counts as one point at its object");
            Lister(f);
            Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost)), "The post moves out (" + kind + ")");
            var standIn = Spheres(f).SingleOrDefault(s => s.Owner == f.Jetty);
            Assert(standIn != null && Near(f.World.Native.Sphere(standIn).ToArray(), new[] { 1100.0, 10, 20, 8 }), "A jetty whose only " + kind + " has unreadable bounds keeps the stand-in");
        }
    }

    // Nested listed objects under two game-object cullers, without a simple culler: the jetty is listed by a wide culler
    // and the frame below it, empty, by a narrow one. The post moves far, so the jetty becomes a container with a
    // stand-in; the pin below the frame moves inside the wide sphere only, so it follows the jetty's stand-in. The frame
    // then keeps a stand-in of its own, whichever culler the plan reads first.
    static void NestedListedObjectsPlanTheSameInAnyOrder() {
        foreach (var wideFirst in new[] { true, false }) {
            var f = Plain(); f.Group.Cullers.Clear(); f.Group.RootCulling = null;
            var frame = BelowJetty(f, "Frame", new[] { 0.0, 0, -6 }); frame.Empty = true;
            var pin = BelowJetty(f, "Pin", new[] { 0.0, 0, -6 }, frame);
            var wide = new FakeCuller { Id = ".|Wide|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { f.Jetty }, Radius = 100, Authored = 100 };
            var narrow = new FakeCuller { Id = ".|Narrow|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { frame }, Radius = 2, Authored = 2 };
            if (wideFirst) { f.Group.Cullers.Add(wide); f.Group.Cullers.Add(narrow); } else { f.Group.Cullers.Add(narrow); f.Group.Cullers.Add(wide); }
            Assert(Applies(f, Part("far-post", "Jetty/Post", new[] { 1100.0, 10, 5020 }), Part("near-pin", "Jetty/Frame/Pin", new[] { 1100.0, 10, 40 })), "The post and the pin move (" + (wideFirst ? "wide" : "narrow") + " culler first)");
            var owners = Spheres(f).Select(s => s.Owner).ToList();
            Assert(owners.Count == 3 && owners.Contains(f.Post) && owners.Contains(f.Jetty) && owners.Contains(frame) && !owners.Contains(pin),
                "The post has its own sphere, and the jetty and the frame each keep a stand-in: " + String.Join(", ", Spheres(f).Select(s => N(f.World.Native.Sphere(s)))));
            Assert(Near(f.World.Native.Sphere(Spheres(f).Single(s => s.Owner == frame)).ToArray(), new[] { 1100.0, 10, 20, 2 }), "The frame's stand-in is its culler's sphere");
            Assert(wide.Lists.Count == 0 && narrow.Lists.Count == 0, "Both listed objects are handed over");
            var jetty = Spheres(f).Single(s => s.Owner == f.Jetty); var own = Spheres(f).Single(s => s.Owner == frame);
            f.World.Show(own, false);
            Assert(frame.Active && pin.Active, "With the frame's stand-in hidden, the pin shows by the jetty's stand-in");
            f.World.Show(jetty, false);
            Assert(!pin.Active, "With both hidden the pin is culled");
        }
    }

    // The contract of IGroupWorld.Presence: no corners for an object without a renderer, collider or light at or below
    // it, one point per light only where no renderer stands, and the anchor, the object's map position, stands in for
    // the presence of a moved part that has none: such a part keeps a sphere of its own at its place.
    static void PresenceMayBeEmptyAndTheAnchorStandsInForIt() {
        var f = Plain();
        f.Jetty.Empty = true; f.Post.Empty = true;
        Assert(f.World.Presence(f.Jetty, null).Count == 0 && f.World.Presence(f.Post, null).Count == 0, "An object without presence has no corners");
        f.Post.Lights = true;
        Assert(f.World.Presence(f.Jetty, null).Count == 1 && Near(f.World.Presence(f.Jetty, null)[0], new[] { 1101.0, 10, 14 }), "A light counts as one point where no renderer stands");
        f.Jetty.Renders = true;
        Assert(f.World.Presence(f.Jetty, null).Count == 8, "A renderer takes the place of the light");
        f.Jetty.Empty = true; f.Post.Empty = true; f.Post.Collides = true;
        Assert(f.World.Presence(f.Jetty, null).Count == 8 && f.World.Presence(f.Jetty, new object[] { f.Post }).Count == 0, "A collider fills its cube; a named part is left out");
        var g = Plain(); g.Post.Empty = true;
        Assert(Applies(g, Part("far-post", "Jetty/Post", FarPost)), "An empty part moves out");
        var own = Spheres(g).Single();
        Assert(own.Owner == g.Post && Near(g.World.Native.Sphere(own).ToArray(), new[] { 1160.0, 10, 14, 0 }), "It keeps a sphere of its own at its anchor: " + N(g.World.Native.Sphere(own)));
    }

    // The review's case: the empty jetty is listed with a sphere of 8 m, and the post moves to 1100, 10, 28.8: outside
    // the listed sphere, inside the root's simple sphere of 10 m. The post follows the game's sphere of the simple culler,
    // which is no own sphere, so the jetty keeps the stand-in and leaves the list.
    static void EmptyListedObjectKeepsAStandInForAGameSphere() {
        var f = Plain(); var root = Root(f); f.Jetty.Empty = true;
        var lister = Lister(f);
        Assert(Applies(f, Part("near-post", "Jetty/Post", new[] { 1100.0, 10, 28.8 })), "The post moves out of the listed sphere");
        var standIn = Spheres(f).SingleOrDefault(s => s.Owner == f.Jetty);
        Assert(standIn != null && Near(f.World.Native.Sphere(standIn).ToArray(), new[] { 1100.0, 10, 20, 8 }) && !Spheres(f).Any(s => s.Owner == f.Post), "The jetty keeps the stand-in; the post follows the simple culler's sphere");
        Assert(lister.Lists.Count == 0 && f.World.Culling.HandedOver(lister).Single() == f.Jetty, "The jetty leaves the list, so its culler cannot switch it");
        lister.Shows = false; f.World.Show(standIn, false);
        Assert(f.Jetty.Active && f.Post.Active, "With the listed sphere hidden and the simple sphere visible, the post shows");
    }

    // Two parts below the empty jetty move to different places. The jetty follows both own spheres and needs no
    // stand-in. When one part returns, the sphere that holds the jetty changes, and the hand-over reports the new one.
    static void SeveralMovedPartsBelowAnEmptyListedObject() {
        foreach (var returning in new[] { "Post", "Rail" }) {
            var f = Plain(); f.Jetty.Empty = true;
            var rail = BelowJetty(f, "Rail", new[] { -1.0, 0, -6 });
            var lister = Lister(f);
            Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost), Part("far-rail", "Jetty/Rail", new[] { 1100.0, 10, 5020 })), "Both parts move out");
            Assert(Spheres(f).Count == 2 && !Spheres(f).Any(s => s.Owner == f.Jetty) && lister.Lists.Count == 0, "Each part has its own sphere; none stays in the culler's place, and the jetty leaves the list");
            var post = Spheres(f).Single(s => s.Owner == f.Post); var far = Spheres(f).Single(s => s.Owner == rail);
            Assert(Near(f.World.Native.Sphere(far).Take(3).ToArray(), new[] { 1100.0, 10, 5020 }) && Near(f.World.Native.Sphere(post).Take(3).ToArray(), FarPost), "Each sphere stands at its part");
            lister.Shows = false;
            f.World.Show(post, false);
            Assert(f.Jetty.Active && rail.Active && !f.Post.Active, "With one sphere visible the jetty stays on for that part");
            f.World.Show(far, false);
            Assert(!f.Jetty.Active, "With both hidden the jetty is off");
            f.World.Show(post, true); f.World.Show(far, true);
            var stays = returning == "Post" ? rail : f.Post;
            Assert(Applies(f, Part("far-post", "Jetty/Post", returning == "Post" ? new[] { 1101.0, 10, 14 } : FarPost), Part("far-rail", "Jetty/Rail", returning == "Rail" ? new[] { 1099.0, 10, 14 } : new[] { 1100.0, 10, 5020 })), returning + " returns");
            // The returned part shows again below the jetty, so the jetty is no longer empty: a stand-in takes it over.
            var standIn = Spheres(f).SingleOrDefault(s => s.Owner == f.Jetty); var left = Spheres(f).SingleOrDefault(s => s.Owner == stays);
            Assert(Spheres(f).Count == 2 && standIn != null && left != null && lister.Lists.Count == 0 && f.World.Culling.HandedOver(lister).Single() == f.Jetty, "The other part keeps its sphere, and the jetty, with the returned part in it, is handed over to a stand-in");
            f.World.Show(left, false); f.World.Show(standIn, false);
            Assert(!f.Jetty.Active && f.World.Culling.HandedOver().Single().Item3 == false, "The hand-over reports the stand-in that holds the jetty now: hidden, the jetty is off");
            f.World.Show(standIn, true);
            Assert(f.Jetty.Active && f.World.Culling.HandedOver().Single().Item3, "Visible again, it reports the jetty shown");
        }
        // With an empty returned part the jetty stays empty: the remaining part's sphere takes it over.
        foreach (var returning in new[] { "Post", "Rail" }) {
            var f = Plain(); f.Jetty.Empty = true;
            var rail = BelowJetty(f, "Rail", new[] { -1.0, 0, -6 });
            (returning == "Post" ? f.Post : rail).Empty = true;
            var lister = Lister(f);
            Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost), Part("far-rail", "Jetty/Rail", new[] { 1100.0, 10, 5020 })), "Both parts move out");
            Assert(Spheres(f).Count == 2 && !Spheres(f).Any(s => s.Owner == f.Jetty), "No stand-in");
            var stays = returning == "Post" ? rail : f.Post;
            Assert(Applies(f, Part("far-post", "Jetty/Post", returning == "Post" ? new[] { 1101.0, 10, 14 } : FarPost), Part("far-rail", "Jetty/Rail", returning == "Rail" ? new[] { 1099.0, 10, 14 } : new[] { 1100.0, 10, 5020 })), "The empty " + returning + " returns");
            var left = Spheres(f).Single();
            Assert(left.Owner == stays && lister.Lists.Count == 0 && f.World.Culling.HandedOver(lister).Single() == f.Jetty, "The other part keeps its sphere, and the jetty stays handed over without a stand-in");
            f.World.Show(left, false);
            Assert(!f.Jetty.Active && f.World.Culling.HandedOver().Single().Item3 == false, "The hand-over reports the remaining sphere: hidden, the jetty is off");
            f.World.Show(left, true);
            Assert(f.Jetty.Active && f.World.Culling.HandedOver().Single().Item3, "Visible again, it reports the jetty shown");
        }
    }

    // A listed object with presence of its own keeps the stand-in: an unmoved child that shows, or only a light or a
    // collider on the object itself.
    static void ListedObjectWithPresenceOfItsOwnKeepsItsStandIn() {
        foreach (var kind in new[] { "child", "light", "collider" }) {
            var f = Plain(); f.Jetty.Empty = true;
            if (kind == "child") BelowJetty(f, "Rail", new[] { -1.0, 0, -6 });
            if (kind == "light") f.Jetty.Lights = true;
            if (kind == "collider") f.Jetty.Collides = true;
            Lister(f);
            Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost)), "The post moves out (" + kind + ")");
            var standIn = Spheres(f).SingleOrDefault(s => s.Owner == f.Jetty);
            Assert(standIn != null && Near(f.World.Native.Sphere(standIn).ToArray(), new[] { 1100.0, 10, 20, 8 }), "A jetty with a " + kind + " of its own keeps the stand-in in the culler's place");
        }
    }

    // The empty case through the scene patch editing session: Undo, Redo and Remove modification.
    static void EmptyListedObjectThroughUndoRedoAndRemove() {
        var f = Plain(); f.Jetty.Empty = true; var lister = Lister(f); var (_, mapEditor, session) = Session(f);
        Assert(session.Edit(PartId("Jetty/Post"), new Placement(FarPost, Identity), null).Applied, "The post moves out");
        Assert(Spheres(f).Count == 1 && Spheres(f).Single().Owner == f.Post && lister.Lists.Count == 0, "One own sphere at the post, the jetty handed over");
        mapEditor.Undo();
        Assert(Spheres(f).Count == 0 && lister.Lists.Single() == f.Jetty, "Undo releases the sphere and returns the jetty to the list");
        mapEditor.Redo();
        Assert(Spheres(f).Count == 1 && Spheres(f).Single().Owner == f.Post && lister.Lists.Count == 0 && f.World.Culling.Regions(f.Group).Single().Owner == f.Post, "Redo restores only the post's sphere");
        Assert(session.Remove(PartId("Jetty/Post")).Applied && Spheres(f).Count == 0 && lister.Lists.Single() == f.Jetty, "Remove modification returns everything");
    }

    // The listed jetty moves out of its game-object culler's sphere and gets a sphere of its own in the culler's place.
    // The post below it moves on, out of that sphere. The jetty is an object between a culler and a moved part: it
    // stays on while the post's own sphere shows, also while its own sphere is hidden.
    static void MovedPartBelowAMovedListedObjectShows() {
        foreach (var simple in new[] { true, false }) {
            var f = Plain();
            if (!simple) { f.Group.Cullers.Clear(); f.Group.RootCulling = null; }
            var lister = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { f.Jetty }, Radius = 8, Authored = 8 }; f.Group.Cullers.Add(lister);
            Assert(Applies(f, Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 }), Part("far-post", "Jetty/Post", new[] { 1100.0, 10, 5020 })), "The listed object and the part below it move out");
            var gate = Spheres(f).Single(s => s.Owner == f.Jetty && s.Kind == CullerKind.GameObject); var post = Spheres(f).Single(s => s.Owner == f.Post);
            f.World.Show(gate, false);
            Assert(f.Jetty.Active && f.Post.Active && f.Group.Active, (simple ? "With" : "Without") + " a simple culler above: with the listed object's own sphere hidden, it stays on for the moved part below it");
            f.World.Show(post, false);
            Assert(!f.Jetty.Active, "With the moved part's sphere hidden as well, the listed object is off");
            f.World.Show(gate, true);
            Assert(f.Jetty.Active && !f.Post.Active, "With only its own sphere visible the listed object shows and the moved part does not");
        }
    }

    // The jetty is listed by a game-object culler with a wide sphere. The post below it moves 59 m: inside that sphere,
    // outside the root's simple culler of radius 10. The simple culler would hide the whole group, so the post gets a
    // sphere of its own and the objects up to the root stay on for it.
    static void PartInsideAListedSphereButOutsideTheSimpleSphereGetsItsOwn() {
        var f = Plain(); var root = Root(f);
        var lister = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { f.Jetty }, Radius = 100, Authored = 100 }; f.Group.Cullers.Add(lister);
        Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost)), "The post moves out of the simple culler's sphere");
        var own = Spheres(f).SingleOrDefault(s => s.Owner == f.Post);
        Assert(own != null && own.Culler == root && own.Kind == CullerKind.Simple, "A part outside a simple culler's sphere further up gets its own sphere for that culler");
        f.World.See(f.Group, root, false);
        Assert(f.Group.Active && f.Jetty.Active && f.Post.Active && !f.Platform.Active, "With the simple culler's sphere hidden the post shows through the objects above it");
        f.World.Show(own, false);
        Assert(!f.Group.Active, "With both hidden the group is off");
    }

    // Where the game-object cullers of Railroader 0.6.3 stand, from the inventory ledger of the base-game map: each
    // row of such a culler with every component row at and below its object. Every one stands on a grade-crossing
    // controller, which the crossing families list, and in no listed baked scenery group. The ledger does not hold
    // what each culler lists; the native probe's culling readout records that.
    static void GameObjectCullersStandOnGradeCrossings() {
        const string culler = "Helpers.Culling.GameObjectCuller";
        var rows = CullerInventoryExtract.Rows;
        var cullers = rows.Where(r => r[0] == culler).Select(r => r[1]).ToArray();
        Assert(cullers.Length == 4, "Railroader 0.6.3 has four game-object cullers: " + cullers.Length);
        var world = new Node("World");
        Node At(string path) {
            var node = world;
            foreach (var name in path.Split('/').Skip(1)) { var next = node.Kids.FirstOrDefault(k => k.Name == name); if (next == null) node.Kids.Add(next = new Node(name)); node = next; }
            return node;
        }
        foreach (var row in rows) At(row[1]).With(row[0]);
        // A crossing controller's object belongs to the crossing families; everything in the extract renders or collides.
        void Mark(Node node) { node.Physical = true; node.HasFamily = node.Own.Contains(BakedSceneryRules.CrossingController); foreach (var kid in node.Kids) Mark(kid); }
        Mark(world);
        var verdicts = BakedSceneryRules.Classify(world);
        var parts = BakedSceneryRules.Parts(verdicts).Where(v => v.Disposition == PartDisposition.Part).ToArray();
        foreach (var path in cullers) {
            Assert(At(path).Own.Contains(BakedSceneryRules.CrossingController), path + " stands on a grade-crossing controller");
            Assert(!verdicts.Any(v => v.Disposition == GroupDisposition.Group && (path == v.Path || path.StartsWith(v.Path + "/", StringComparison.Ordinal))), path + " stands in no listed baked scenery group");
            Assert(!parts.Any(v => v.Path == path || v.Path.StartsWith(path + "/", StringComparison.Ordinal)), "No baked scenery part stands at or below " + path);
        }
    }

    // The paths among listed that are a baked scenery part, or stand below one: an object a game-object culler lists
    // there would be one that own culling could have to take over. The hierarchy is built from component rows and the
    // listed paths; a crossing controller's object belongs to the crossing families, and every object renders or collides.
    static string[] ListedBakedParts(IEnumerable<string[]> rows, IEnumerable<string> listed) {
        var world = new Node("World");
        Node At(string path) {
            var node = world;
            foreach (var name in path.Split('/').Skip(1)) { var next = node.Kids.FirstOrDefault(k => k.Name == name); if (next == null) node.Kids.Add(next = new Node(name)); node = next; }
            return node;
        }
        foreach (var row in rows) At(row[1]).With(row[0]);
        foreach (var path in listed) At(path);
        void Mark(Node node) { node.Physical = true; node.HasFamily = node.Own.Contains(BakedSceneryRules.CrossingController); foreach (var kid in node.Kids) Mark(kid); }
        Mark(world);
        var parts = BakedSceneryRules.Parts(BakedSceneryRules.Classify(world)).Where(v => v.Disposition == PartDisposition.Part).Select(v => v.Path).ToArray();
        return listed.Where(path => parts.Any(part => path == part || path.StartsWith(part + "/", StringComparison.Ordinal))).ToArray();
    }
    // What the four game-object cullers of Railroader 0.6.3 list: eight signals, each below a grade-crossing setup.
    // None is a baked scenery part, so the base-game map has no listed part for own culling to take over.
    static void NoListedObjectIsABakedSceneryPart() {
        var rows = CullerInventoryExtract.Rows; var listed = CullerInventoryExtract.Listed;
        var cullers = rows.Where(r => r[0] == "Helpers.Culling.GameObjectCuller").Select(r => r[1]).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert(listed.Length == 8 && listed.Select(l => l[0]).Distinct().OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(cullers), "The extract holds the eight listed objects of the four game-object cullers");
        Assert(listed.All(l => l[1].StartsWith(l[0] + "/", StringComparison.Ordinal)), "Every listed object stands below its culler's grade-crossing setup");
        var found = ListedBakedParts(rows, listed.Select(l => l[1]));
        Assert(found.Length == 0, "No object a game-object culler lists is a baked scenery part: " + String.Join(", ", found));
        // The check finds a listed baked scenery part where one exists: a lamp of a shed that is a listed group.
        const string lamp = "World/Large Scenery/Town/Shed/Lamp";
        var withLamp = ListedBakedParts(rows.Concat(new[] { new[] { "UnityEngine.MeshRenderer", lamp }, new[] { "Helpers.Culling.GameObjectCuller", "World/Large Scenery/Town/Shed" } }), listed.Select(l => l[1]).Concat(new[] { lamp }));
        Assert(withLamp.SequenceEqual(new[] { lamp }), "A listed part of a baked scenery group is found: " + String.Join(", ", withLamp));
    }

    // The patcher's state ends while the scene stays loaded: everything own culling took over goes back to the game.
    static void EndingThePatcherReturnsTheCullingToTheGame() {
        var f = Plain(); var root = Root(f);
        var lamps = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { f.Lamp, f.Ramp }, Radius = 6, Authored = 6 }; f.Group.Cullers.Add(lamps);
        var container = new FakeCuller { Id = ".|Helpers.Culling.RendererCuller", Simple = false, Radius = 100, Authored = 100, Renders = { f.Platform.Renderer, f.Jetty.Renderer, f.Post.Renderer } }; f.Group.Cullers.Add(container);
        Assert(Applies(f, Part("far-lamp", "Lamp", new[] { 1160.0, 12, 30 }), Part("far-jetty", "Jetty", new[] { 6100.0, 10, 20 })), "Two parts move out");
        Assert(lamps.Lists.Count == 1 && container.Renders.Count == 1 && root.Handler != null && Spheres(f).Count > 0, "Own culling holds the list, the renderers and the handler");
        f.World.See(f.Group, root, false);
        Assert(f.Group.Active, "While own culling runs, the moved parts keep the root on");
        // The entry point disposes the capabilities; the baked scenery group capability disposes its world.
        f.Scene.Dispose();
        Assert(Spheres(f).Count == 0 && f.World.Culling.RegionCount == 0 && root.Handler == null, "Disposing the capabilities releases every sphere and puts the game's handler back");
        Assert(lamps.Lists.Count == 2 && lamps.Lists.Count(l => l == f.Lamp) == 1 && container.Renders.Count == 3, "The list and the renderers are restored");
        Assert(!f.Group.Active, "The root takes the visibility of its own culler again");
        f.World.See(f.Group, root, true); f.World.See(f.Group, root, false);
        Assert(!f.Group.Active, "The game's handler hides the root again");
        // After a map unload nothing is touched, and a handler that stays passes the game's events on.
        var (g, _) = WithFarPost(); var other = Root(g);
        g.World.Culling.Unload();
        g.World.See(g.Group, other, false);
        Assert(!g.Group.Active, "A handler the controller no longer owns calls the game's handler");
    }

    static void InstallWaitsForTheGamesCuller() {
        // The patch applies before the root's simple culler has started.
        var f = Plain(); var root = Root(f); root.Started = false;
        Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost)), "A patch that applies before the culler starts applies");
        Assert(root.Handler == null && f.World.Culling.Waiting && f.Post.Active, "Before the culler starts nothing is installed on it");
        var idle = f.World.Native.ManagerCalls + f.World.Native.Writes;
        f.World.Culling.Tick(); f.World.Culling.Tick();
        Assert(root.Handler == null && f.World.Culling.Waiting && f.World.Native.ManagerCalls + f.World.Native.Writes == idle, "An update before the start installs nothing and does no culling work");
        f.World.Start(root); f.World.Culling.Tick();
        Assert(root.Handler != null && !f.World.Culling.Waiting, "The first update after the culler starts installs it");
        f.World.See(f.Group, root, false);
        Assert(f.Group.Active && !f.Platform.Active, "The container then culls by its own sphere and the moved part's");
        // The same for the wrapper on a moved part's own simple culler.
        var g = new Fixture(); var own = OnJetty(g); own.Simple = true; own.Started = false;
        Assert(Applies(g, Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 })) && own.Handler == null && Root(g).Handler != null, "A part's own culler that has not started gets no wrapper");
        g.World.Start(own); g.World.Culling.Tick();
        Assert(own.Handler != null && !g.World.Culling.Waiting, "The first update after it starts wraps it");
        // A renderer culler that has not woken has no renderer list to hand over yet. While the group waits, an
        // update does no culling work.
        var h = Plain();
        var asleep = new FakeCuller { Id = ".|Helpers.Culling.RendererCuller", Simple = false, Radius = 100, Authored = 100, Started = false, Renders = { h.Jetty.Renderer, h.Post.Renderer } }; h.Group.Cullers.Add(asleep);
        Assert(Applies(h, Part("far-jetty", "Jetty", new[] { 6100.0, 10, 20 })) && asleep.Renders.Count == 2 && !Spheres(h).Any(s => s.Kind == CullerKind.Renderer) && h.World.Culling.Waiting, "Before the renderer culler wakes its renderers stay");
        var calls = h.World.Native.ManagerCalls + h.World.Native.Writes;
        h.World.Culling.Tick(); h.World.Culling.Tick(); h.World.Culling.Tick();
        Assert(h.World.Native.ManagerCalls + h.World.Native.Writes == calls && h.World.Culling.Waiting, "Updates while the culler sleeps touch no sphere and no object");
        h.World.Start(asleep); h.World.Culling.Tick();
        Assert(asleep.Renders.Count == 0 && Spheres(h).Any(s => s.Kind == CullerKind.Renderer) && !h.World.Culling.Waiting, "After it wakes the renderers are handed over");
        // A culling manager that does not exist yet.
        var k = Plain(); k.World.Native.NoManager = true;
        Assert(Applies(k, Part("far-post", "Jetty/Post", FarPost)) && Spheres(k).Count == 0 && k.World.Culling.Waiting, "Without a culling manager the sphere waits");
        k.World.Native.NoManager = false; k.World.Culling.Tick();
        Assert(Spheres(k).Count == 1 && !k.World.Culling.Waiting, "The next update registers it");
    }

    static void StaleCallbacksWriteNothing() {
        var (f, own) = WithFarPost(); var (_, _, session) = Session(f);
        var writes = f.World.Native.Writes;
        f.World.Native.ManagersGone = true; f.World.Show(own, false); f.World.Native.ManagersGone = false;
        Assert(f.World.Native.Writes == writes && f.Post.Active, "A callback of a destroyed culling manager writes nothing");
        f.Group.Parts.Remove(f.Post); f.World.Show(own, false); f.Group.Parts.Add(f.Post);
        Assert(f.World.Native.Writes == writes, "A callback for a destroyed part writes nothing");
        Assert(session.Edit(PartId("Jetty/Post"), new Placement(FarPost, Identity), null).Applied && session.Remove(PartId("Jetty/Post")).Applied && own.Removed, "Removal releases the registration");
        writes = f.World.Native.Writes;
        f.World.Show(own, false);
        Assert(f.World.Native.Writes == writes && f.Post.Active, "A callback for a removed registration writes nothing");
        // A callback that fails is reported once, and the next update repairs the group.
        var (g, sphere) = WithFarPost(); var faults = new List<string>();
        g.World.Culling.Fault = faults.Add;
        g.World.Native.FailWrites = true;
        g.World.Show(sphere, false); g.World.Show(sphere, false);
        Assert(faults.Count == 1 && g.World.Culling.Waiting, "A failing callback does not throw and is reported once: " + faults.Count);
        g.World.Native.FailWrites = false; g.World.Culling.Tick();
        Assert(!g.Post.Active && !g.World.Culling.Waiting, "The next update brings the group to its spheres");
    }

    // The update runs every frame. A failure of one group is written once per group and failure, names the group, and
    // leaves the other groups updating.
    static void UpdateFailureIsWrittenOncePerGroupAndFailure() {
        const string Other = "World/Large Scenery/Whittier/Whittier Saw Mill Culled";
        var (f, _) = WithFarPost();
        var second = new FakeGroup();
        f.World.Objects[Other] = new List<object> { second };
        f.World.PartsAt[Other + "/Lamp"] = new List<object> { second.Parts[2] };
        Assert(Applies(f, Part("far-post", "Jetty/Post", FarPost), Mod("far-lamp", Other + "/Lamp", BakedSceneryParts.Structure, new[] { 1160.0, 10, 30 }, null)), "Both groups have a moved part out of their culler's sphere");
        Assert(Spheres(f).Count == 2, "Each group holds its own sphere");
        f.World.Culling.GroupName = group => ReferenceEquals(group, f.Group) ? Mill : ReferenceEquals(group, second) ? Other : null;
        var failure = "game function gone";
        f.World.Native.FailAlive = target => ReferenceEquals(target, f.Group) || ReferenceEquals(target, second) ? new InvalidOperationException(failure) : null;
        var log = RecordingLog.Capture(false, () => {
            for (var frame = 0; frame < 3; frame++) f.World.Culling.Tick();
            failure = "another failure";
            f.World.Culling.Tick(); f.World.Culling.Tick();
        });
        var warnings = log.At(LogLevel.Warning).ToArray();
        Assert(warnings.Length == 4, "Each group and failure is written once over five frames: " + String.Join(" | ", warnings));
        Assert(warnings.Count(w => w.Contains("of " + Mill + ". Report") && w.EndsWith(": game function gone", StringComparison.Ordinal)) == 1
            && warnings.Count(w => w.Contains("of " + Other + ". Report") && w.EndsWith(": game function gone", StringComparison.Ordinal)) == 1, "Two groups with the same failure each get one warning that names the group: " + String.Join(" | ", warnings));
        Assert(warnings.Count(w => w.EndsWith(": another failure", StringComparison.Ordinal)) == 2, "A new failure of the same group is written once more");
        Assert(warnings[0] == "The patcher couldn't update visibility for moved parts of " + Mill + ". Report this to the patcher maintainer: game function gone", "The warning names the failed visibility update and report recipient: " + warnings[0]);
        // One failing group leaves the other updating.
        f.World.Native.FailAlive = target => ReferenceEquals(target, f.Group) ? new InvalidOperationException("only the mill") : null;
        f.World.Objects.Remove(Other);
        var quiet = RecordingLog.Capture(false, () => f.World.Culling.Tick());
        Assert(quiet.At(LogLevel.Warning).Single().Contains("of " + Mill + ". Report") && f.World.Culling.RegionCount == 1, "The other group still updates and gives back its sphere when it goes, while the failing group keeps its own");
    }

    static void UnloadReadsNoCullingManager() {
        var (f, _) = WithFarPost();
        f.World.Native.Unloading = true;
        var calls = f.World.Native.ManagerCalls;
        f.World.RefreshCulling(f.Group, f.World.Plan);
        f.World.Observe(f.Group);
        f.World.Culling.Tick();
        f.World.Culling.Unload();
        Assert(f.World.Native.ManagerCalls == calls, "Nothing reads or writes a culling manager while the map unloads: " + (f.World.Native.ManagerCalls - calls));
        Assert(f.World.Culling.RegionCount == 0 && !f.World.Culling.Waiting, "The unload forgets every own sphere");
        // After the next load the same patch registers its sphere again.
        f.World.Native.Unloading = false; f.World.Native.Registered.Clear();
        Assert(new PatchEngine(f.Scene).Apply(Doc(Part("far-post", "Jetty/Post", FarPost))).Outcomes.Values.All(o => o.Applied) && Spheres(f).Count == 1, "The next load registers the own sphere again");
    }

    static void LeftoverWrapperIsReported() {
        var f = new Fixture(); var own = OnJetty(f); own.Simple = true; var (_, _, session) = Session(f);
        Assert(session.Edit(PartId("Jetty"), new Placement(new[] { 1160.0, 10, 20 }, Identity), null).Applied && own.Handler != null, "The part's own culler is wrapped");
        // Other code replaces the handler after the patcher wrapped it.
        Action other = () => { }; own.Handler = other;
        Assert(session.Remove(PartId("Jetty")).Applied, "Removal still applies");
        Assert(own.Handler == other && f.World.Culling.Leftovers.SequenceEqual(new[] { own.Id }), "A wrapper that could not be taken out is named, and the other handler stays");
        // The report is a debug line, once, and never a warning.
        var faults = new List<string>();
        f.World.Culling.Fault = faults.Add;
        // Reported during a load, the line carries that load's attempt number although culling knows nothing of the load.
        LoadAttempt.Reset();
        RecordingLog log;
        try { LoadAttempt.Begin("rr/bushnell-whittier"); log = RecordingLog.Capture(true, () => { f.World.Culling.Report(); f.World.Culling.Report(); }); }
        finally { LoadAttempt.Reset(); }
        Assert(log.Lines.Count == 1 && log.Lines[0].Key == LogLevel.Debug && faults.Count == 0, "The wrapper that stays is reported once as a debug line, not as a warning: " + log.Lines.Count + " / " + faults.Count);
        Assert(log.Lines[0].Value == "load attempt 1, map rr/bushnell-whittier: Released visibility rules for game culler " + own.Id + "; another component replaced its handler; replacement handler retained", "The line names the visibility controller and distinguishes released rules from conditional forwarding: " + log.Lines[0].Value);
        var playerMode = RecordingLog.Capture(false, () => new Fixture().World.Culling.Report());
        var quiet = RecordingLog.Capture(true, () => new Fixture().World.Culling.Report());
        Assert(quiet.Lines.Count == 0 && playerMode.Lines.Count == 0, "Without such a wrapper nothing is reported");
    }

    static void InspectorNamesOwnCulling() {
        var text = BakedSceneryRules.PartCullingDependency;
        Assert(text.Contains("its own culling") && text.Contains("keeps its radius") && !text.Contains("expand"), "The inspector says a moved part keeps its own culling and the group's culler its radius: " + text);
    }
}
