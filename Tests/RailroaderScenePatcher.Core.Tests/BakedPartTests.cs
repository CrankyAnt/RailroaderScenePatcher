// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Whole scene patch file tests for single parts of baked scenery groups against a controlled group with nested parts and
// cullers: part identity and ambiguity, part-only edits, the fixed placement of a part through a later group move and
// a group gizmo preview, ordering, removal, repeated application, refusal and restoration.
static class BakedPartTests {
    const string SceneName = "BushnellWhittier-MapManager", Mill = "World/Large Scenery/Whittier/Whittier Saw Mill";
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    static readonly double[] Identity = { 0, 0, 0, 1 };
    static string Mod(string id, string path, string structure, double[] position = null, double[] rotation = null, bool? active = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + N(position) + "],\"rotation\":[" + N(rotation ?? Identity) + "]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string Part(string id, string name, double[] position = null, double[] rotation = null, bool? active = null) => Mod(id, Mill + "/" + name, BakedSceneryParts.Structure, position, rotation, active);
    static string Group(string id, double[] position, double[] rotation) => Mod(id, Mill, BakedSceneryGroups.Structure, position, rotation);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static TargetIdentity PartId(string name) => new TargetIdentity(SceneName, Mill + "/" + name, BakedSceneryParts.ComponentType, BakedSceneryParts.Structure);
    static readonly TargetIdentity GroupId = new TargetIdentity(SceneName, Mill, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
    static readonly double[] JettyTarget = { 1130, 10, 30 }, GroupTarget = { 1110, 10, 20 };
    static readonly double[] JettyTurn = Turn.Yaw(30), GroupTurn = Turn.Yaw(15);

    // A saw mill group: a platform, a ramp, a culled lamp, and a jetty with a post below it; a renderer culler stands on
    // the jetty and a simple culler on the root.
    sealed class Fixture : IInspectionScene {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeGroup Group = new FakeGroup();
        public readonly FakePart Platform, Ramp, Lamp, Jetty, Post;
        public readonly PartHolds Holds = new PartHolds();
        public readonly SceneCapabilities Scene;
        public readonly BakedSceneryParts Parts;
        public readonly PatchEngine Engine;
        public Fixture() {
            Platform = Group.Parts[0]; Ramp = Group.Parts[1]; Lamp = Group.Parts[2];
            Jetty = Group.Add(new FakePart { Id = "Jetty#4", Offset = new[] { 0.0, 0, -6 } });
            Post = Group.Add(new FakePart { Id = "Jetty/Post#5", Offset = new[] { 1.0, 0, -6 }, Parent = Jetty });
            Group.Cullers.Add(new FakeCuller { Id = "Jetty|Helpers.Culling.RendererCuller", On = Jetty });
            World.Objects[Mill] = new List<object> { Group };
            foreach (var (name, part) in new[] { ("Platform", Platform), ("Ramp", Ramp), ("Lamp", Lamp), ("Jetty", Jetty), ("Jetty/Post", Post) }) World.PartsAt[Mill + "/" + name] = new List<object> { part };
            World.SetActive(Group, true); World.Mutations = 0;
            Scene = new SceneCapabilities(() => World.Maps, this);
            Scene.Add(new BakedSceneryGroups(World, holds: Holds)); Scene.Add(Parts = new BakedSceneryParts(World, Holds));
            Engine = new PatchEngine(Scene);
        }
        public IEnumerable<string> MapIds => World.Maps;
        public IEnumerable<SceneObservation> Discover() {
            var root = World.Observe(Group).Root;
            yield return new SceneObservation(Group, GroupId, BakedSceneryRules.Family, root.Active == true, root.Placement.Position, root.Placement.Rotation, new string[0], new string[0], new string[0], "group");
            foreach (var pair in World.PartsAt) {
                var part = (FakePart)pair.Value[0]; var state = World.ObservePart(part).Part;
                yield return new SceneObservation(part, new TargetIdentity(SceneName, pair.Key, BakedSceneryParts.ComponentType, BakedSceneryParts.Structure), BakedSceneryRules.Family,
                    state.Active == true, state.Placement.Position, state.Placement.Rotation, new string[0], new string[0], new string[0], "part", groupPath: Mill);
            }
        }
        public Placement MapOf(FakePart part) => World.ObservePart(part).Part.Placement;
    }
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b, double tolerance = 0.001) => a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= tolerance);
    static bool SameTurn(IReadOnlyList<double> a, IReadOnlyList<double> b) => Math.Abs(Math.Abs(a.Zip(b, (x, y) => x * y).Sum()) - 1) < 1e-6;
    static double[] Plus(double[] a, double[] b) => a.Zip(b, (x, y) => x + y).ToArray();
    // Where a part stands in map coordinates when it keeps its place relative to the root at rootPosition, rootTurn.
    static double[] InGroup(double[] rootPosition, double[] rootTurn, double[] offset) => Plus(rootPosition, Turn.Rotate(rootTurn, offset));
    static string Where(Fixture f) => String.Join("; ", f.Group.Parts.Select(p => p.Id + "=" + N(f.MapOf(p).Position.Select(v => Math.Round(v, 3)))));

    public static void Run() {
        PartRuleListsSceneObjectsOfListedGroups();
        PartEditChangesOnlyThatPart();
        FixedPartKeepsItsPlacementWhenTheGroupMoves();
        GroupAndPartInOneFileInEitherKeyOrder();
        GroupAndPartInTwoModsInEitherLoadOrder();
        FixedPartHoldsThroughAGroupGizmoPreview();
        RemovalReturnsThePartToItsPlaceInTheGroup();
        UnmatchedAndAmbiguousPartPathsFailBeforeMutation();
        RepeatedApplicationAccumulatesNoOffset();
        CullingFollowsAPartMove();
        PreflightRefusalLeavesSceneAndScenePatchFileUnchanged();
        FailureAfterMutationRestoresThePart();
        IncompleteCleanupBlocksTheRetry();
        ActivityOfACullerSwitchedPartIsRefused();
        EditingHistorySaveAndReload();
        PartsAreNamedWithTheirGroupAndDecorative();
        NestedPartKeepsItsPlaceInItsParent();
        CullerSwitchedPartKeepsTheCullersState();
        CullerAboveAHeldPartEnclosesIt();
        ReleaseAndDestroyedPartsLetTheCullingGo();
        HiddenSwitchedPartStillCountsForEnclosure();
        GameObjectCullerEnclosesOnlyWhatItSwitches();
        Console.WriteLine("PASS: baked scenery parts: part rule, part-only edits, fixed placement through group moves and previews, ordering, removal, culling, refusal, restoration and scene patch editing");
    }

    // A part is one scene object at any depth below a listed group root. Sibling objects that share a path, levels of a
    // LOD group, objects without physical presence, runtime objects and objects of another family are no parts, and an
    // inspection-only group has no part entries at all.
    static void PartRuleListsSceneObjectsOfListedGroups() {
        var spawned = new Node("Spawned Sawdust").Solid(); spawned.Serialized = false;
        var world = new Node("World", new Node("Large Scenery",
            new Node("Whittier",
                new Node("Whittier Saw Mill",
                    new Node("Jetty", new Node("Piece").Solid(), new Node("Piece (1)").Solid()),
                    new Node("Platform").Solid(),
                    new Node("Board", new Node("Nail").Solid()).Solid(), new Node("Board", new Node("Nail").Solid()).Solid(),
                    new Node("Lamp", new Node("LOD0").Solid().Lod(), new Node("LOD1").Solid().Lod()).With("UnityEngine.LODGroup"),
                    new Node("Anchor"), spawned, new Node("Sign").Solid().Family())),
            new Node("Bryson", new Node("Bryson Depot", new Node("Agent").With("Model.StationAgent"), new Node("Bench").Solid()))));
        var verdicts = BakedSceneryRules.Classify(world);
        var parts = BakedSceneryRules.Parts(verdicts);
        PartVerdict At(string name) => parts.SingleOrDefault(v => v.Path == Mill + "/" + name);
        var listed = parts.Where(v => v.Disposition == PartDisposition.Part).Select(v => v.Path.Substring(Mill.Length + 1)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert(listed.SequenceEqual(new[] { "Jetty", "Jetty/Piece", "Jetty/Piece (1)", "Lamp", "Platform" }), "Each scene object with physical presence at any depth is a part: " + String.Join(", ", listed));
        Assert(parts.All(v => v.GroupPath == Mill), "Every part names its group root");
        Assert(parts.Count(v => v.Path.StartsWith(Mill + "/Board", StringComparison.Ordinal)) == 4 && parts.Where(v => v.Path.StartsWith(Mill + "/Board", StringComparison.Ordinal)).All(v => v.Disposition == PartDisposition.NotListed && v.Reason == BakedSceneryRules.AmbiguousParts),
            "Siblings that share one path, and the objects below them, are no parts");
        Assert(At("Lamp/LOD0")?.Reason == BakedSceneryRules.LodLevels && At("Lamp/LOD1")?.Reason == BakedSceneryRules.LodLevels, "A level of a LOD group belongs to the object that holds the LOD group");
        Assert(At("Anchor")?.Reason == BakedSceneryRules.NoPhysicalPresence && At("Spawned Sawdust")?.Reason == BakedSceneryRules.RuntimeObject && At("Sign")?.Disposition == PartDisposition.ExistingFamily,
            "Objects without physical presence, runtime objects and objects of another family are no parts");
        Assert(!parts.Any(v => v.Path.StartsWith("World/Large Scenery/Bryson/", StringComparison.Ordinal)), "An inspection-only group has no part entries");
    }

    static void PartEditChangesOnlyThatPart() {
        var f = new Fixture();
        var root = f.World.Observe(f.Group).Root.Placement; var others = new[] { f.Platform, f.Ramp, f.Lamp }.Select(p => (double[])p.Offset.Clone()).ToArray();
        var outcome = f.Engine.Apply(Doc(Part("move-jetty", "Jetty", JettyTarget, JettyTurn))).Outcomes["move-jetty"];
        Assert(outcome.Applied, "A part move applies: " + outcome.Error);
        Assert(Near(f.MapOf(f.Jetty).Position, JettyTarget) && SameTurn(f.MapOf(f.Jetty).Rotation, JettyTurn), "The part takes its map placement: " + Where(f));
        Assert(Near(f.MapOf(f.Post).Position, Plus(JettyTarget, Turn.Rotate(JettyTurn, new[] { 1.0, 0, 0 }))), "The object below the part moves with it: " + Where(f));
        var now = f.World.Observe(f.Group).Root;
        Assert(Near(now.Placement.Position, root.Position) && now.Active == true && new[] { f.Platform, f.Ramp, f.Lamp }.Select(p => p.Offset).Zip(others, (a, b) => Near(a, b)).All(x => x), "The group root and the other parts keep their placement");
        outcome = f.Engine.Apply(Doc(Part("hide-platform", "Platform", active: false))).Outcomes["hide-platform"];
        Assert(outcome.Applied && !f.Platform.Active && f.Ramp.Active && f.Jetty.Active && f.Post.Active && f.Group.Active, "Disabling a part disables only that part: " + outcome.Error);
        Assert(Near(f.MapOf(f.Jetty).Position, JettyTarget), "An activity edit leaves the placement");
    }

    static void FixedPartKeepsItsPlacementWhenTheGroupMoves() {
        var f = new Fixture();
        f.Engine.Apply(Doc(Part("move-jetty", "Jetty", JettyTarget, JettyTurn)), "first");
        var post = f.MapOf(f.Post).Position;
        var outcome = f.Engine.Apply(Doc(Group("move-mill", GroupTarget, GroupTurn)), "second").Outcomes["move-mill"];
        Assert(outcome.Applied, "The group move applies: " + outcome.Error);
        Assert(Near(f.MapOf(f.Jetty).Position, JettyTarget) && SameTurn(f.MapOf(f.Jetty).Rotation, JettyTurn) && Near(f.MapOf(f.Post).Position, post), "The fixed part and what stands below it keep their placement: " + Where(f));
        Assert(Near(f.MapOf(f.Platform).Position, InGroup(GroupTarget, GroupTurn, new[] { 0.0, 0, 5 })), "A part without a fixed placement moves with the group: " + Where(f));
    }

    // The final placements of a group move and a part move, for comparing orders.
    static string Final(Fixture f) => Where(f) + " | root " + N(f.World.Observe(f.Group).Root.Placement.Position.Select(v => Math.Round(v, 3)));
    static void GroupAndPartInOneFileInEitherKeyOrder() {
        var first = new Fixture(); var second = new Fixture();
        var a = first.Engine.Apply(Doc(Group("a-mill", GroupTarget, GroupTurn), Part("b-jetty", "Jetty", JettyTarget, JettyTurn)));
        var b = second.Engine.Apply(Doc(Part("a-jetty", "Jetty", JettyTarget, JettyTurn), Group("b-mill", GroupTarget, GroupTurn)));
        Assert(a.Outcomes.Values.All(o => o.Applied) && b.Outcomes.Values.All(o => o.Applied), "Both orders apply: " + String.Join("; ", a.Outcomes.Values.Concat(b.Outcomes.Values).Select(o => o.Error)));
        Assert(Final(first) == Final(second), "A part and its group in one scene patch file reach the same placements in either key order: " + Final(first) + " / " + Final(second));
        Assert(Near(first.MapOf(first.Jetty).Position, JettyTarget), "The part keeps its fixed placement");
    }
    static void GroupAndPartInTwoModsInEitherLoadOrder() {
        var first = new Fixture(); var second = new Fixture();
        var parts = Doc(Part("move-jetty", "Jetty", JettyTarget, JettyTurn)); var groups = Doc(Group("move-mill", GroupTarget, GroupTurn));
        var runtime = new PatchRuntime(first.Engine, new Diagnostics(), "0.5.0", "test");
        runtime.ApplyLoaded(new[] { new LoadedMod(new PatchSource("author/parts", "Parts", "1.0.0"), parts, new[] { "crankyant/scene-patcher" }), new LoadedMod(new PatchSource("author/groups", "Groups", "1.0.0"), groups, new[] { "crankyant/scene-patcher" }) }, null);
        runtime = new PatchRuntime(second.Engine, new Diagnostics(), "0.5.0", "test");
        runtime.ApplyLoaded(new[] { new LoadedMod(new PatchSource("author/groups", "Groups", "1.0.0"), groups, new[] { "crankyant/scene-patcher" }), new LoadedMod(new PatchSource("author/parts", "Parts", "1.0.0"), parts, new[] { "crankyant/scene-patcher" }) }, null);
        Assert(Final(first) == Final(second) && Near(first.MapOf(first.Jetty).Position, JettyTarget), "A part and its group in two mods reach the same placements in either load order: " + Final(first) + " / " + Final(second));
    }
    sealed class Diagnostics : IPatchDiagnostics { public readonly List<PatchFailure> Failures = new List<PatchFailure>(); public void Warning(PatchFailure failure) => Failures.Add(failure); }

    static (Store, NativeMapEditor, PatchEditingSession) Session(Fixture f, Store store = null) {
        store = store ?? new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        return (store, mapEditor, session);
    }
    static void FixedPartHoldsThroughAGroupGizmoPreview() {
        foreach (var complete in new[] { true, false }) {
            var f = new Fixture(); var (_, _, session) = Session(f);
            Assert(session.Edit(PartId("Jetty"), new Placement(JettyTarget, JettyTurn), null).Applied, "The part edit applies");
            var inventory = new SceneInventory(f.Scene); inventory.Refresh();
            var gesture = new PlacementGesture(f.Engine, session, GroupId, inventory.Pin(inventory.Entries.Single(e => ReferenceEquals(e.Handle, f.Group))));
            foreach (var x in new[] { 1105.0, 1110.0 }) {
                var preview = gesture.Preview(new Placement(new[] { x, 10.0, 20 }, GroupTurn));
                Assert(preview.Applied, "The group preview applies: " + preview.Error);
                Assert(Near(f.MapOf(f.Jetty).Position, JettyTarget) && Near(f.MapOf(f.Platform).Position, InGroup(new[] { x, 10.0, 20 }, GroupTurn, new[] { 0.0, 0, 5 })), "During the preview the fixed part keeps its placement: " + Where(f));
            }
            var end = complete ? gesture.Complete() : gesture.Cancel();
            Assert(end.Applied, "The gesture ends: " + end.Error);
            Assert(Near(f.MapOf(f.Jetty).Position, JettyTarget) && SameTurn(f.MapOf(f.Jetty).Rotation, JettyTurn), "After the preview the fixed part keeps its placement: " + Where(f));
            Assert(Near(f.MapOf(f.Platform).Position, InGroup(complete ? GroupTarget : new[] { 1100.0, 10, 20 }, complete ? GroupTurn : Identity, new[] { 0.0, 0, 5 })), "The group ends where the gesture leaves it: " + Where(f));
        }
    }

    static void RemovalReturnsThePartToItsPlaceInTheGroup() {
        var f = new Fixture(); var (_, mapEditor, session) = Session(f);
        Assert(session.Edit(PartId("Jetty"), new Placement(JettyTarget, JettyTurn), null).Applied, "The part edit applies");
        Assert(session.Edit(GroupId, new Placement(GroupTarget, GroupTurn), null).Applied && Near(f.MapOf(f.Jetty).Position, JettyTarget), "The group move leaves the fixed part");
        var removed = session.Remove(PartId("Jetty"));
        Assert(removed.Applied, "Remove modification applies: " + removed.Error);
        Assert(Near(f.MapOf(f.Jetty).Position, InGroup(GroupTarget, GroupTurn, new[] { 0.0, 0, -6 })) && SameTurn(f.MapOf(f.Jetty).Rotation, GroupTurn), "The part returns to its unedited placement relative to its group: " + Where(f));
        Assert(Near(f.MapOf(f.Post).Position, InGroup(GroupTarget, GroupTurn, new[] { 1.0, 0, -6 })), "What stands below the part returns with it: " + Where(f));
        Assert(f.Group.Cullers.Single(c => c.OnRoot).Radius == 10, "Without a fixed part the root culler keeps its authored radius");
        Assert(session.Edit(GroupId, new Placement(new[] { 1120.0, 10, 20 }, GroupTurn), null).Applied && Near(f.MapOf(f.Jetty).Position, InGroup(new[] { 1120.0, 10, 20 }, GroupTurn, new[] { 0.0, 0, -6 })), "The returned part moves with its group again: " + Where(f));
        mapEditor.Undo(); mapEditor.Undo();
        Assert(Near(f.MapOf(f.Jetty).Position, JettyTarget), "Undo of the removal holds the part at its fixed placement again: " + Where(f));
    }

    static void UnmatchedAndAmbiguousPartPathsFailBeforeMutation() {
        var f = new Fixture();
        f.World.PartsAt[Mill + "/Twin"] = new List<object> { f.Group.Add(new FakePart { Id = "Twin#6", Offset = new[] { 2.0, 0, 2 } }), f.Group.Add(new FakePart { Id = "Twin#7", Offset = new[] { 2.0, 0, 3 } }) };
        var result = f.Engine.Apply(Doc(Part("missing", "Shed", JettyTarget), Part("twin", "Twin", JettyTarget), Part("move-jetty", "Jetty", JettyTarget, JettyTurn)));
        Assert(!result.Outcomes["missing"].Applied && !result.Outcomes["twin"].Applied && result.Outcomes["twin"].ResolvedTarget == null, "A part path that matches no part or several parts fails that modification");
        Assert(result.Outcomes["move-jetty"].Applied && f.World.Mutations == 1 && Near(f.MapOf(f.Jetty).Position, JettyTarget), "The other modification still applies, and the failed ones mutate nothing");
        Assert(!f.Engine.Apply(Doc(Part("group-path", "Jetty").Replace(BakedSceneryParts.Structure, BakedSceneryGroups.Structure).Replace("\"dependsOn\"", "\"active\":false,\"dependsOn\""))).Outcomes["group-path"].Applied, "A part path does not resolve as a group");
    }

    static void RepeatedApplicationAccumulatesNoOffset() {
        var f = new Fixture();
        var doc = Doc(Group("move-mill", GroupTarget, GroupTurn), Part("move-jetty", "Jetty", JettyTarget, JettyTurn));
        f.Engine.Apply(doc); var first = Final(f);
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && Final(f) == first, "Repeated application keeps the same placements: " + first + " / " + Final(f));
        f.World.Origin = 1010;
        Assert(new PatchEngine(f.Scene).Apply(doc).Outcomes.Values.All(o => o.Applied) && Near(f.MapOf(f.Jetty).Position, JettyTarget), "A load at another origin reaches the same map placements: " + Where(f));
    }

    static void CullingFollowsAPartMove() {
        var f = new Fixture();
        var far = new[] { 1160.0, 10, 20 };
        Assert(f.Engine.Apply(Doc(Part("far-jetty", "Jetty", far))).Outcomes["far-jetty"].Applied, "A far part move applies");
        var root = f.Group.Cullers.Single(c => c.OnRoot); var jetty = f.Group.Cullers.Single(c => c.On == f.Jetty);
        Assert(f.World.Enclosed.Contains(root.Id) && root.Radius > 60 && root.Radius >= f.World.Observe(f.Group).Cullers.Single(c => c.Id == root.Id).Required, "The simple culler on the root re-derives its sphere from the current renderers: " + root.Radius);
        Assert(Near(jetty.Registered, new[] { 160.0, 10, 20 }), "The renderer culler on the moved part is refreshed: " + N(jetty.Registered));
        // A culling refresh that does not happen fails the edit, which restores the part.
        var g = new Fixture(); g.World.SkipCullingRefresh = true;
        var outcome = g.Engine.Apply(Doc(Part("far-jetty", "Jetty", far))).Outcomes["far-jetty"];
        Assert(!outcome.Applied && outcome.Error.StartsWith("Mutation failed and was restored", StringComparison.Ordinal) && Near(g.MapOf(g.Jetty).Position, new[] { 1100.0, 10, 14 }), "A culler that did not follow fails and restores the part: " + outcome.Error);
        Assert(g.Group.Cullers.Single(c => c.OnRoot).Radius == 10 && Near(g.Group.Cullers.Single(c => c.On == g.Jetty).Registered, new[] { 100.0, 10, 14 }), "The restoration returns the culling as it was");
    }

    static void PreflightRefusalLeavesSceneAndScenePatchFileUnchanged() {
        var f = new Fixture(); f.World.PartRefused = "The part's scene is not loaded";
        var outcome = f.Engine.Apply(Doc(Part("move-jetty", "Jetty", JettyTarget))).Outcomes["move-jetty"];
        Assert(!outcome.Applied && outcome.Error.Contains("not loaded") && f.World.Mutations == 0, "A refused part is not touched: " + outcome.Error);
        var g = new Fixture(); var (_, editor, refused) = Session(g); g.World.PartRefused = "The part's scene is not loaded";
        var before = refused.ScenePatchFileText;
        Assert(!refused.Edit(PartId("Jetty"), new Placement(JettyTarget, Identity), null).Applied && refused.ScenePatchFileText == before && g.World.Mutations == 0 && !editor.CanUndo,
            "A scene patch editing refusal leaves the scene, the scene patch file and the history unchanged");
    }

    static void FailureAfterMutationRestoresThePart() {
        var cases = new (Action<FakeGroupWorld> inject, bool? active)[] { (w => w.ThrowAfterPartPlacement = true, false), (w => w.MoveSibling = true, null) };
        foreach (var (inject, active) in cases) {
            var f = new Fixture(); var platform = (double[])f.Platform.Offset.Clone(); inject(f.World);
            var outcome = f.Engine.Apply(Doc(Part("move-jetty", "Jetty", JettyTarget, JettyTurn, active))).Outcomes["move-jetty"];
            Assert(!outcome.Applied && outcome.Error.StartsWith("Mutation failed and was restored", StringComparison.Ordinal), "A failure after mutation is restored: " + outcome.Error);
            Assert(Near(f.MapOf(f.Jetty).Position, new[] { 1100.0, 10, 14 }) && SameTurn(f.MapOf(f.Jetty).Rotation, Identity) && f.Jetty.Active && Near(f.Platform.Offset, platform), "The captured placement and activity of the part are restored: " + Where(f));
            Assert(Near(f.Engine.Apply(Doc(Group("move-mill", GroupTarget, GroupTurn))).Outcomes.Values.Single().Applied ? f.MapOf(f.Jetty).Position : new double[3], InGroup(GroupTarget, GroupTurn, new[] { 0.0, 0, -6 })), "The restored part holds no fixed placement: " + Where(f));
        }
    }

    static void IncompleteCleanupBlocksTheRetry() {
        var f = new Fixture(); f.World.MoveSibling = f.World.KeepSiblingMoved = true;
        var outcome = f.Engine.Apply(Doc(Part("move-jetty", "Jetty", JettyTarget))).Outcomes["move-jetty"];
        Assert(!outcome.Applied && outcome.Error.Contains("cleanup is unverified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        f.World.KeepSiblingMoved = false; var mutations = f.World.Mutations;
        var retry = f.Engine.Apply(Doc(Part("again", "Jetty", new[] { 1120.0, 10, 30 }))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("cleanup") && f.World.Mutations == mutations, "A retry on the affected part is blocked until cleanup is verified or the world reloads: " + retry.Error);
    }

    static void ActivityOfACullerSwitchedPartIsRefused() {
        var f = new Fixture(); f.Lamp.Switched = true;
        var outcome = f.Engine.Apply(Doc(Part("hide-lamp", "Lamp", active: false))).Outcomes["hide-lamp"];
        Assert(!outcome.Applied && outcome.Error.Contains("culler") && f.World.Mutations == 0 && f.Lamp.Active, "A part a culler switches refuses an activity edit before mutation: " + outcome.Error);
        Assert(f.Engine.Apply(Doc(Part("move-lamp", "Lamp", new[] { 1095.0, 12, 20 }))).Outcomes["move-lamp"].Applied, "Its placement can still be edited");
    }

    static void EditingHistorySaveAndReload() {
        var f = new Fixture(); var (store, mapEditor, session) = Session(f);
        Assert(session.Edit(PartId("Jetty"), new Placement(JettyTarget, JettyTurn), null).Applied && session.Edit(PartId("Platform"), null, false).Applied, "Two part edits apply");
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Persistence(true, all, "After a part move and a part disable");
        mapEditor.Undo(); Assert(f.Platform.Active, "Undo re-enables the part"); Persistence(true, all, "After Undo of the disable");
        mapEditor.Undo(); Assert(Near(f.MapOf(f.Jetty).Position, new[] { 1100.0, 10, 14 }), "Undo returns the part"); Persistence(false, new string[0], "After Undo back to the unedited group");
        mapEditor.Redo(); Assert(Near(f.MapOf(f.Jetty).Position, JettyTarget), "Redo reapplies the move"); Persistence(true, all, "After Redo of the move");
        mapEditor.Redo(); Assert(!f.Platform.Active, "Redo reapplies the disable"); Persistence(true, all, "After Redo of the disable");
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0") && (int)JObject.Parse(store.Patch)["formatVersion"] == 1, "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        var item = (JObject)JObject.Parse(store.Patch)["modifications"].Children<JProperty>().Single(p => ((string)p.Value["target"]["path"]).EndsWith("/Jetty", StringComparison.Ordinal)).Value;
        Assert((string)item["target"]["structure"] == BakedSceneryParts.Structure && (string)item["target"]["component"] == "UnityEngine.Transform", "The scene patch file records the part target");
        var fresh = new Fixture();
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Values.All(o => o.Applied) && Near(fresh.MapOf(fresh.Jetty).Position, JettyTarget) && !fresh.Platform.Active && fresh.Group.Parts.Count == 5, "A reloaded world reproduces the part edits without duplicated state");
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var (_, strictEditor, kept) = Session(new Fixture(), strict);
        Assert(kept.Edit(PartId("Jetty"), new Placement(JettyTarget, JettyTurn), null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=0.5.0"), "A stricter existing constraint is retained: " + strict.Info);
        var bare = new Store { Patch = Doc(Part("move-jetty", "Jetty", JettyTarget)) }; var bareEditor = new NativeMapEditor();
        var without = new PatchEditingSession(new PatchEngine(new Scene { Maps = new[] { "map" } }), bareEditor, bare); bareEditor.Session = without;
        Assert(without.MinimumPatcherVersion == BakedSceneryParts.RequiredVersion, "A part scene patch file requires the part capability version whether or not this build offers it: " + without.MinimumPatcherVersion);
    }

    // A part without a fixed placement stands in the object directly above it: a part below a held part keeps its place
    // in that part through an activity edit, an Undo and a removal.
    static void NestedPartKeepsItsPlaceInItsParent() {
        double[] PostOnJetty(Fixture f) => Plus(f.MapOf(f.Jetty).Position, Turn.Rotate(f.MapOf(f.Jetty).Rotation, new[] { 1.0, 0, 0 }));
        foreach (var undo in new[] { false, true }) {
            var f = new Fixture(); var (_, mapEditor, session) = Session(f);
            Assert(session.Edit(PartId("Jetty"), new Placement(JettyTarget, JettyTurn), null).Applied && session.Edit(PartId("Jetty/Post"), null, false).Applied, "The jetty holds and the post is disabled");
            Assert(session.Edit(GroupId, new Placement(GroupTarget, GroupTurn), null).Applied, "The group moves");
            if (undo) { mapEditor.Undo(); mapEditor.Undo(); } else Assert(session.Edit(PartId("Jetty/Post"), null, true).Applied, "Enable applies");
            Assert(f.Post.Active && Near(f.MapOf(f.Post).Position, PostOnJetty(f)) && Near(f.MapOf(f.Jetty).Position, JettyTarget),
                "The post comes back on in its place on the held jetty after " + (undo ? "Undo of the disable" : "Enable") + ": " + Where(f));
        }
        var g = new Fixture(); var (_, _, edits) = Session(g);
        var postTarget = new[] { 1150.0, 10, 40 };
        Assert(edits.Edit(PartId("Jetty/Post"), new Placement(postTarget, Identity), null).Applied && edits.Edit(PartId("Jetty"), new Placement(JettyTarget, JettyTurn), null).Applied, "The post holds and the jetty moves");
        Assert(Near(g.MapOf(g.Post).Position, postTarget), "The held post stays when the jetty moves: " + Where(g));
        Assert(edits.Remove(PartId("Jetty/Post")).Applied && Near(g.MapOf(g.Post).Position, PostOnJetty(g)), "Removal returns the post to its place on the moved jetty: " + Where(g));
        Assert(edits.Remove(PartId("Jetty")).Applied && Near(g.MapOf(g.Jetty).Position, new[] { 1100.0, 10, 14 }) && Near(g.MapOf(g.Post).Position, new[] { 1101.0, 10, 14 }), "After both removals the post is back at its authored map placement: " + Where(g));
    }

    // A game-object culler switches a part by camera distance. Its activity read when the edit captured the part can
    // differ from the activity at a later placement edit; the edit leaves the culler's state.
    static void CullerSwitchedPartKeepsTheCullersState() {
        var f = new Fixture(); f.Lamp.Switched = true; var (_, _, session) = Session(f);
        Assert(session.Edit(PartId("Lamp"), new Placement(new[] { 1095.0, 12, 20 }, Identity), null).Applied, "A placement edit of the culled lamp applies");
        f.Lamp.Active = false; f.Lamp.Shown = false;
        var outcome = session.Edit(PartId("Lamp"), new Placement(new[] { 1094.0, 12, 20 }, Identity), null);
        Assert(outcome.Applied && !f.Lamp.Active, "A later placement edit leaves the lamp as its culler switched it: " + outcome.Error);
        f.Lamp.Active = true; f.Lamp.Shown = true;
        Assert(session.Remove(PartId("Lamp")).Applied && f.Lamp.Active && Near(f.MapOf(f.Lamp).Position, new[] { 1097.0, 12, 20 }), "Removal returns the placement and leaves the culler's state: " + Where(f));
        // The activity captured with the part reads off while the culler has not switched it on yet; the culler switches
        // it on afterwards, and a placement edit leaves it on.
        var g = new Fixture(); g.Lamp.Switched = true; g.Lamp.Active = false; var (store, editor, edits) = Session(g);
        Assert(edits.Edit(PartId("Lamp"), new Placement(new[] { 1095.0, 12, 20 }, Identity), null).Applied && !g.Lamp.Active, "A placement edit of the lamp applies and leaves it off");
        g.Lamp.Active = true;
        outcome = edits.Edit(PartId("Lamp"), new Placement(new[] { 1094.0, 12, 20 }, Identity), null);
        Assert(outcome.Applied && g.Lamp.Active, "A placement edit does not switch the lamp off against its culler: " + outcome.Error);
        // Save writes the placement and no activity, and a reload applies it without touching the culler's state.
        Assert(editor.Save() == null, "Save succeeds");
        var saved = JObject.Parse(store.Patch)["modifications"].Children<JProperty>().Single().Value;
        Assert(saved["placement"] != null && saved["active"] == null, "The written modification holds no active key: " + saved);
        var reloaded = new Fixture(); reloaded.Lamp.Switched = true; reloaded.Lamp.Active = false;
        Assert(reloaded.Engine.Apply(store.Patch).Outcomes.Values.Single().Applied && !reloaded.Lamp.Active && Near(reloaded.MapOf(reloaded.Lamp).Position, new[] { 1094.0, 12, 20 }), "A reload places the lamp and leaves it as its culler switched it: " + Where(reloaded));
    }

    // A culler on a part above a held part encloses it while it holds, and takes its authored radius again afterwards.
    static void CullerAboveAHeldPartEnclosesIt() {
        var f = new Fixture(); var (_, _, session) = Session(f);
        var jetty = f.Group.Cullers.Single(c => c.On == f.Jetty);
        Assert(session.Edit(PartId("Jetty/Post"), new Placement(new[] { 1160.0, 10, 14 }, Identity), null).Applied, "The post moves far from the jetty");
        Assert(jetty.Radius > 50 && f.World.Enclosed.Contains(jetty.Id), "The renderer culler on the jetty encloses the held post: " + jetty.Radius);
        Assert(session.Remove(PartId("Jetty/Post")).Applied && jetty.Radius == 10 && f.Group.Cullers.Single(c => c.OnRoot).Radius == 10, "Without a held part below it the culler takes its authored radius again");
    }

    // A released part, and a part that no longer exists, leave no enlarged culling behind.
    static void ReleaseAndDestroyedPartsLetTheCullingGo() {
        var f = new Fixture(); var root = f.Group.Cullers.Single(c => c.OnRoot);
        Assert(f.Engine.Apply(Doc(Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 }))).Outcomes.Values.Single().Applied && root.Radius > 60, "A far part enlarges the root culler");
        f.Parts.Release(f.Jetty);
        Assert(root.Radius == 10, "Releasing the part returns the root culler to its authored radius: " + root.Radius);
        var g = new Fixture(); var gone = g.Group.Cullers.Single(c => c.OnRoot);
        Assert(g.Engine.Apply(Doc(Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 }))).Outcomes.Values.Single().Applied && gone.Radius > 60, "A far part enlarges the root culler");
        g.Group.Parts.Remove(g.Post); g.Group.Parts.Remove(g.Jetty); g.Group.Cullers.RemoveAll(c => c.On == g.Jetty);
        Assert(g.Engine.Apply(Doc(Group("move-mill", GroupTarget, GroupTurn))).Outcomes.Values.Single().Applied && gone.Radius == 10, "A destroyed part holds nothing: the group moves and its root culler takes its authored radius: " + gone.Radius);
    }

    // A culler that hides a part because the camera is far away does not take it out of what the cullers above a held
    // part enclose: the held far part stays inside their spheres, whatever the camera does.
    static void HiddenSwitchedPartStillCountsForEnclosure() {
        foreach (var hideJetty in new[] { true, false }) {
            var f = new Fixture(); var hidden = hideJetty ? f.Jetty : f.Post;
            hidden.Switched = true; hidden.Active = false; hidden.Shown = false;
            var outcome = f.Engine.Apply(Doc(Part("far-post", "Jetty/Post", new[] { 1160.0, 10, 14 })));
            var jetty = f.Group.Cullers.Single(c => c.On == f.Jetty); var root = f.Group.Cullers.Single(c => c.OnRoot);
            Assert(outcome.Outcomes.Values.Single().Applied && jetty.Radius > 50 && root.Radius > 60,
                "With the " + (hideJetty ? "jetty" : "post") + " hidden by its culler, the jetty's and the root's cullers still enclose the held post: " + jetty.Radius + ", " + root.Radius);
        }
    }

    // A game-object culler culls the parts it switches, not the parts below it: it encloses only while a held part
    // separates it from a part it switches.
    static void GameObjectCullerEnclosesOnlyWhatItSwitches() {
        var f = new Fixture();
        var lamps = new FakeCuller { Id = ".|Helpers.Culling.GameObjectCuller", Switcher = true, Lists = { f.Lamp }, Radius = 2, Authored = 2 }; f.Group.Cullers.Add(lamps);
        Assert(f.Engine.Apply(Doc(Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 }))).Outcomes.Values.Single().Applied && lamps.Radius == 2, "A held part it does not switch leaves a root game-object culler at its authored radius: " + lamps.Radius);
        Assert(f.Engine.Apply(Doc(Part("far-lamp", "Lamp", new[] { 1160.0, 12, 30 }))).Outcomes.Values.Single().Applied && lamps.Radius > 50, "A held part it switches is enclosed: " + lamps.Radius);
        var g = new Fixture();
        var ramp = new FakeCuller { Id = "Jetty|Helpers.Culling.GameObjectCuller", Switcher = true, On = g.Jetty, Lists = { g.Ramp } }; g.Group.Cullers.Add(ramp);
        Assert(g.Engine.Apply(Doc(Part("far-jetty", "Jetty", new[] { 1160.0, 10, 20 }))).Outcomes.Values.Single().Applied && ramp.Radius > 50, "A game-object culler on a held part encloses the part it switches outside it: " + ramp.Radius);
    }

    static void PartsAreNamedWithTheirGroupAndDecorative() {
        var f = new Fixture();
        var post = f.Discover().Single(o => ReferenceEquals(o.Handle, f.Post));
        Assert(InventoryPresentationState.Name(post) == "Whittier Saw Mill / Jetty / Post", "A part entry is named with its group: " + InventoryPresentationState.Name(post));
        Assert(InventoryPresentationState.Name(f.Discover().First()) == "Whittier Saw Mill", "The group entry keeps its own name");
        Assert(CommandBand.PartDecorative == "Decorative. Moving or disabling this part does not affect game functions.", "The Object tab states that a part is decorative");
        Assert(CommandBand.PartOperations.Contains("this part") && CommandBand.PartOperations.Contains("group"), "Supported edits name the part and its group: " + CommandBand.PartOperations);
    }
}
