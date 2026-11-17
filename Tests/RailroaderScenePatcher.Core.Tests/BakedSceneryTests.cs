// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// A controlled scene hierarchy: names, the components on each object, and whether it renders, collides or lights.
class Node : IHierarchyNode {
    public string Name { get; set; }
    public List<string> Own = new List<string>();
    public List<Node> Kids = new List<Node>();
    public bool Physical { get; set; }
    public bool Serialized { get; set; } = true;
    public bool HasFamily { get; set; }
    public bool LodLevel { get; set; }
    public IReadOnlyList<string> Components => Own;
    public IReadOnlyList<IHierarchyNode> Children => Kids;
    public Node(string name, params Node[] children) { Name = name; Kids.AddRange(children); }
    public Node With(params string[] components) { Own.AddRange(components); return this; }
    public Node Solid() { Physical = true; return this; }
    public Node Family() { HasFamily = true; return this; }
    public Node Lod() { LodLevel = true; return this; }
}
// Quaternions in x, y, z, w order for the controlled worlds.
static class Turn {
    public static double[] Multiply(double[] a, double[] b) => new[] {
        a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1], a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
        a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3], a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2] };
    public static double[] Inverse(double[] q) => new[] { -q[0], -q[1], -q[2], q[3] };
    public static double[] Rotate(double[] q, double[] v) { var r = Multiply(Multiply(q, new[] { v[0], v[1], v[2], 0 }), Inverse(q)); return new[] { r[0], r[1], r[2] }; }
    public static double[] Yaw(double degrees) { var half = degrees * Math.PI / 360; return new[] { 0, Math.Sin(half), 0, Math.Cos(half) }; }
}
// One part below a group root: its placement relative to the root, its own activity, whether a culler switches it,
// and the part it stands below. Switched: a game-object culler switches it by camera distance.
class FakePart {
    public string Id; public double[] Offset; public double[] Rotation = { 0, 0, 0, 1 }; public bool Active = true; public bool Culled, Switched;
    // For a switched part: whether its culler shows it now. Off while its culler hides it, it counts as on.
    public bool Shown = true;
    public FakePart Parent; public FakeGroup Group;
}
// A culler in the group: where its culling sphere is registered, relative to the root, or null while it holds none.
// On names the part a renderer culler stands on. Radius is the sphere radius of the simple culler on the root.
// Switcher: a game-object culler, which culls the parts it Lists rather than the parts below it.
class FakeCuller { public string Id; public double[] Offset = { 0, 0, 0 }; public double[] Registered; public bool OnRoot, Switcher; public FakePart On; public double Radius = 10, Authored = 10; public List<FakePart> Lists = new List<FakePart>(); }
class FakeGroup {
    public double[] Position = { 100, 10, 20 }, Rotation = { 0, 0, 0, 1 };
    public bool Active = true;
    public object Parent = new object();
    public List<FakePart> Parts = new List<FakePart> {
        new FakePart { Id = "Platform#1", Offset = new[] { 0.0, 0, 5 } },
        new FakePart { Id = "Ramp#2", Offset = new[] { 4.0, 0, 0 } },
        new FakePart { Id = "Lamp#3", Offset = new[] { -3.0, 2, 0 }, Culled = true }
    };
    public List<FakeCuller> Cullers = new List<FakeCuller> { new FakeCuller { Id = "SimpleCuller#9", OnRoot = true } };
    // Whether the simple culler on the root switches the root; null without one.
    public bool? RootCulling = true;
    public FakeGroup() { foreach (var part in Parts) part.Group = this; }
    public FakePart Add(FakePart part) { part.Group = this; Parts.Add(part); return part; }
}
// Controlled world: map coordinates are scene coordinates plus Origin on X. A culling refresh moves every culler of an
// active group to where it stands; a disabled group holds no culling registration.
class FakeGroupWorld : IGroupWorld {
    public string[] Maps = { "map" };
    public IEnumerable<string> MapIds => Maps;
    public Dictionary<string, List<object>> Objects = new Dictionary<string, List<object>>();
    public Dictionary<string, List<object>> PartsAt = new Dictionary<string, List<object>>();
    public double Origin = 1000;
    public string Refused, PartRefused;
    public int Mutations;
    public bool ThrowAfterPlacement, SkipCullingRefresh, LeavePartBehind, KeepPartBehind, KeepRootCulling, ThrowAfterPartPlacement, MoveSibling, KeepSiblingMoved;
    // The cullers the last culling refresh of a group let enclose their renderers.
    public ICollection<string> Enclosed;
    double[] behind, sibling;
    public IReadOnlyList<object> Find(string scene, string path) => Objects.TryGetValue(path, out var found) ? found : new List<object>();
    public string Refusal(object group) => Refused;
    static double[] Add(double[] a, double[] b) => a.Zip(b, (x, y) => x + y).ToArray();
    static double[] Sub(double[] a, double[] b) => a.Zip(b, (x, y) => x - y).ToArray();
    double[] Map(double[] scene) => new[] { scene[0] + Origin, scene[1], scene[2] };
    double[] SceneOf(double[] map) => new[] { map[0] - Origin, map[1], map[2] };
    // A part's position in the scene: the root's position and rotation applied to its place relative to the root.
    static double[] ScenePosition(FakePart part) => Add(part.Group.Position, Turn.Rotate(part.Group.Rotation, part.Offset));
    // A part is on as the scene authored it while it and every part above it is on, or off only because its culler
    // hides it.
    static bool Enabled(FakePart part) { for (var p = part; p != null; p = p.Parent) if (!(p.Active || (p.Switched && !p.Shown))) return false; return true; }
    // The radius around the root, or around a part, that encloses the renderers of the enabled parts at or below it, each
    // a metre across.
    static double Required(FakeGroup group) => group.Parts.Where(Enabled).Select(p => Math.Sqrt(p.Offset.Sum(v => v * v)) + 1).DefaultIfEmpty(0).Max();
    static double Required(FakePart above) => above.Group.Parts.Where(p => (p == above || IsBelow(p, above)) && Enabled(p)).Select(p => Math.Sqrt(Sub(p.Offset, above.Offset).Sum(v => v * v)) + 1).DefaultIfEmpty(0).Max();
    static bool IsBelow(FakePart part, FakePart above) { for (var p = part.Parent; p != null; p = p.Parent) if (p == above) return true; return false; }
    // What a culler encloses: a game-object culler its listed parts and what stands below them, any other culler the
    // parts below its object.
    static double Required(FakeGroup group, FakeCuller culler) {
        if (!culler.Switcher) return culler.On != null ? Required(culler.On) : Required(group);
        var at = culler.On != null ? culler.On.Offset : culler.Offset;
        return group.Parts.Where(p => culler.Lists.Any(l => p == l || IsBelow(p, l)) && Enabled(p)).Select(p => Math.Sqrt(Sub(p.Offset, at).Sum(v => v * v)) + 1).DefaultIfEmpty(0).Max();
    }
    double[] CullerAt(FakeGroup group, FakeCuller culler) => culler.On != null ? ScenePosition(culler.On) : Add(group.Position, culler.Offset);
    public GroupObservation Observe(object target) {
        var group = (FakeGroup)target;
        return new GroupObservation(new ObjectState(new Placement(Map(group.Position), (double[])group.Rotation.Clone()), group.Active),
            group.Parts.Select(p => new GroupPart(p.Id, p.Offset, p.Rotation, p.Active, p.Culled, p, p.Parent)),
            group.Cullers.Select(c => new GroupCuller(c.Id, c.Registered == null ? null : Map(c.Registered), Map(CullerAt(group, c)), c.Radius, Required(group, c), (object)c.On ?? group, c.Lists, c.Switcher)), group.Parent, group.RootCulling);
    }
    public void SetActive(object target, bool active) {
        Mutations++; var group = (FakeGroup)target; group.Active = active;
        if (group.RootCulling != null && !KeepRootCulling) group.RootCulling = active;
        foreach (var culler in group.Cullers) culler.Registered = active && (culler.On == null || Enabled(culler.On)) ? CullerAt(group, culler) : null;
    }
    public void SetPlacement(object target, Placement placement) {
        Mutations++; var group = (FakeGroup)target;
        group.Position = SceneOf(placement.Position);
        group.Rotation = (double[])placement.Rotation.Clone();
        // A part left behind once follows the root again at the next placement, unless the injection keeps it behind.
        if (behind != null && !KeepPartBehind) { group.Parts[0].Offset = behind; behind = null; }
        else if (LeavePartBehind && behind == null) { behind = group.Parts[0].Offset; group.Parts[0].Offset = new[] { 9.0, 9, 9 }; LeavePartBehind = false; }
        if (ThrowAfterPlacement) { ThrowAfterPlacement = false; throw new Exception("injected after placement"); }
    }
    public void RefreshCulling(object target, ICollection<string> enclose) {
        var group = (FakeGroup)target;
        Enclosed = enclose;
        if (SkipCullingRefresh) { SkipCullingRefresh = false; return; }
        foreach (var culler in group.Cullers) {
            culler.Radius = enclose.Contains(culler.Id) ? Math.Max(culler.Authored, Required(group, culler)) : culler.Authored;
            if (group.Active && (culler.On == null || Enabled(culler.On))) culler.Registered = CullerAt(group, culler);
        }
    }
    public IReadOnlyList<object> FindParts(string scene, string path) => PartsAt.TryGetValue(path, out var found) ? found : new List<object>();
    public string PartRefusal(object part) => PartRefused;
    public string ActivityRefusal(object part) => ((FakePart)part).Switched ? "A culler of the game switches this part by camera distance" : null;
    public PartObservation ObservePart(object target) {
        var part = (FakePart)target; var group = part.Group;
        var parent = part.Parent == null ? new Placement(Map(group.Position), (double[])group.Rotation.Clone()) : new Placement(Map(ScenePosition(part.Parent)), Turn.Multiply(group.Rotation, part.Parent.Rotation));
        return new PartObservation(new ObjectState(new Placement(Map(ScenePosition(part)), Turn.Multiply(group.Rotation, part.Rotation)), part.Active || (part.Switched && !part.Shown)), group, Observe(group), (object)part.Parent ?? group, parent);
    }
    public void SetPartActive(object target, bool active) {
        Mutations++; var part = (FakePart)target; part.Active = active;
        foreach (var culler in part.Group.Cullers.Where(c => c.On != null && (c.On == part || Below(c.On, part)))) culler.Registered = active && part.Group.Active && Enabled(culler.On) ? CullerAt(part.Group, culler) : null;
    }
    // Everything below the part keeps its place relative to it.
    public void SetPartPlacement(object target, Placement placement) {
        Mutations++; var part = (FakePart)target; var group = part.Group;
        var inverse = Turn.Inverse(group.Rotation);
        var offset = Turn.Rotate(inverse, Sub(SceneOf(placement.Position), group.Position)); var rotation = Turn.Multiply(inverse, placement.Rotation);
        var undo = Turn.Inverse(part.Rotation);
        foreach (var child in group.Parts.Where(p => Below(p, part))) {
            var local = Turn.Rotate(undo, Sub(child.Offset, part.Offset));
            child.Offset = Add(offset, Turn.Rotate(rotation, local)); child.Rotation = Turn.Multiply(rotation, Turn.Multiply(undo, child.Rotation));
        }
        part.Offset = offset; part.Rotation = rotation;
        // A sibling moved once returns at the next part placement, unless the injection keeps it moved.
        var other = group.Parts.First(p => p != part && p.Parent == null && !Below(part, p));
        if (sibling != null && !KeepSiblingMoved) { other.Offset = sibling; sibling = null; }
        else if (MoveSibling && sibling == null) { sibling = other.Offset; other.Offset = new[] { 7.0, 7, 7 }; MoveSibling = false; }
        if (ThrowAfterPartPlacement) { ThrowAfterPartPlacement = false; throw new Exception("injected after part placement"); }
    }
    public bool Alive(object handle) => handle is FakeGroup group ? Objects.Values.Any(list => list.Contains(group)) : handle is FakePart part && part.Group.Parts.Contains(part);
    public bool Below(object target, object above) {
        if (!(target is FakePart part) || !part.Group.Parts.Contains(part)) return false;
        if (above is FakeGroup group) return part.Group == group;
        for (var p = part.Parent; p != null; p = p.Parent) if (p == above) return true;
        return false;
    }
}
static class BakedSceneryTests {
    const string Scene = "BushnellWhittier-MapManager", HousePath = "World/Large Scenery/Bryson/Bryson House Track";
    static string Group(string id, double x = 1110, string path = HousePath, string extra = "", string rotation = "[0,0.130526,0,0.991445]") =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"baked-scenery-group-v1\"}," +
        "\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",10,20],\"rotation\":" + rotation + "}" + extra + ",\"dependsOn\":[]}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":{" + items + "}}";
    static (FakeGroupWorld world, FakeGroup group, BakedSceneryGroups scene) Setup() {
        var world = new FakeGroupWorld(); var group = new FakeGroup();
        world.Objects[HousePath] = new List<object> { group };
        var scene = new BakedSceneryGroups(world);
        world.SetActive(group, true); world.Mutations = 0;
        return (world, group, scene);
    }
    static double[] Offsets(FakeGroup group) => group.Parts.SelectMany(p => p.Offset).ToArray();
    static bool[] Activity(FakeGroup group) => group.Parts.Select(p => p.Active).ToArray();
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static GroupRootVerdict At(IReadOnlyList<GroupRootVerdict> verdicts, string path) => verdicts.SingleOrDefault(v => v.Path == path);
    public static void Run() {
        ListsSimpleGroupRoots();
        LeavesOutGroupsThatLaterTicketsCover();
        CoverageRulesKeepObjectsOut();
        CrossingFoldersHoldTheirChildren();
        GroupMoveMovesTheWholeGroup();
        EnabledChangesOnlyTheRoot();
        UnmatchedAndAmbiguousPathsFailBeforeMutation();
        AnotherMapContextFailsBeforeMutation();
        RepeatedApplicationAccumulatesNoOffset();
        PreflightRefusalLeavesSceneAndScenePatchFileUnchanged();
        FailureAfterMutationRestoresTheGroup();
        IncompleteCleanupBlocksRetry();
        EditingHistoryRemovalAndSave();
        VersionHoldsWithoutTheCapability();
        SceneRootsAndBuiltSpansStayOut();
        EffectsGiveNoPresence();
        Console.WriteLine("PASS: baked scenery group rule, coverage rules, group placement and activity, refusal, restoration and scene patch editing");
    }
    // Every object below CTC and CTC Auxiliary is kept out, and a span the game builds from operations data is not
    // serialized; the patcher's own spans are listed.
    static void SceneRootsAndBuiltSpansStayOut() {
        foreach (var root in new[] { "CTC", "CTC Auxiliary", "Setups", "Ops", "MapFeatures", "Progressions", "MapManager", "Editor Directional Light" })
            Assert(!BakedSceneryRules.ListedBelow(root, false), "Objects below " + root + " are not listed");
        Assert(BakedSceneryRules.ListedBelow("World", false) && BakedSceneryRules.ListedBelow("Setups", true) && !BakedSceneryRules.ListedBelow("CTC", true),
            "Objects below World are listed, and starter placement markers in Setups keep their family; signal markers below CTC do not");
        Assert(!BakedSceneryRules.SpanListed(serialized: false, patcherOwned: false), "A span the game builds while it runs is not listed");
        Assert(BakedSceneryRules.SpanListed(serialized: true, patcherOwned: false) && BakedSceneryRules.SpanListed(serialized: false, patcherOwned: true), "A scene span and the patcher's own spans are listed");
    }
    // The components of World/Environment Effects/Fireflies * in the base-game map: a VFX Graph effect, whose VFXRenderer is a
    // renderer that nothing can run into. A particle system's renderer is the same; a mesh, collider or light is not.
    static void EffectsGiveNoPresence() {
        var fireflies = new[] { "UnityEngine.Transform", "UnityEngine.VFX.VisualEffect", "UnityEngine.VFX.VFXRenderer", "Effects.ClockDrivenVisualEffect" };
        Assert(!fireflies.Any(type => BakedSceneryRules.GivesPresence(type, renderer: type == "UnityEngine.VFX.VFXRenderer", collider: false, light: false)), "A VFX Graph effect gives no physical presence");
        Assert(!BakedSceneryRules.GivesPresence("UnityEngine.ParticleSystemRenderer", true, false, false), "A particle system's renderer gives no physical presence");
        Assert(BakedSceneryRules.GivesPresence("UnityEngine.MeshRenderer", true, false, false) && BakedSceneryRules.GivesPresence("UnityEngine.SkinnedMeshRenderer", true, false, false)
            && BakedSceneryRules.GivesPresence("UnityEngine.BoxCollider", false, true, false) && BakedSceneryRules.GivesPresence("UnityEngine.Light", false, false, true), "Meshes, colliders and lights give physical presence");
    }
    // A group modification needs the group capability whether or not this build offers it.
    static void VersionHoldsWithoutTheCapability() {
        var store = new Store { Patch = Doc(Group("move-house")) }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(new Scene { Maps = new[] { "map" } }), mapEditor, store); mapEditor.Session = session;
        Assert(session.MinimumPatcherVersion == BakedSceneryGroups.RequiredVersion, "A group scene patch file requires the group capability version: " + session.MinimumPatcherVersion);
    }
    static void AnotherMapContextFailsBeforeMutation() {
        var (world, group, scene) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Group("move-house"), "other-map"));
        Assert(result.Error != null && result.Outcomes.Count == 0 && world.Mutations == 0 && group.Position[0] == 100, "A scene patch file for another map context is rejected before mutation");
    }
    static void RepeatedApplicationAccumulatesNoOffset() {
        var (world, group, scene) = Setup();
        var offsets = Offsets(group); var engine = new PatchEngine(scene);
        engine.Apply(Doc(Group("move-house")));
        Assert(engine.Apply(Doc(Group("move-house"))).Outcomes["move-house"].Applied && group.Position.SequenceEqual(new[] { 110.0, 10, 20 }) && Offsets(group).SequenceEqual(offsets), "Repeated application keeps the absolute placement");
        world.Origin = 1010;
        Assert(new PatchEngine(scene).Apply(Doc(Group("move-house"))).Outcomes["move-house"].Applied && group.Position[0] == 100, "A load at another origin reaches the same map placement");
    }
    static void PreflightRefusalLeavesSceneAndScenePatchFileUnchanged() {
        var (world, group, scene) = Setup();
        world.Refused = "The group's scene is not loaded";
        var outcome = new PatchEngine(scene).Apply(Doc(Group("move-house"))).Outcomes["move-house"];
        Assert(!outcome.Applied && outcome.Error.Contains("not loaded") && world.Mutations == 0 && group.Position[0] == 100 && group.Active, "A refused group is not touched: " + outcome.Error);
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(scene), mapEditor, store); mapEditor.Session = session;
        var before = session.ScenePatchFileText;
        Assert(!session.Edit(Identity, new Placement(new[] { 1110.0, 10, 20 }, new[] { 0.0, 0, 0, 1 }), null).Applied && session.ScenePatchFileText == before && world.Mutations == 0 && !mapEditor.CanUndo,
            "A scene patch editing refusal leaves the scene, the scene patch file and the history unchanged");
    }
    static void FailureAfterMutationRestoresTheGroup() {
        var cases = new (Action<FakeGroupWorld> inject, string extra)[] { (w => w.ThrowAfterPlacement = true, ",\"active\":false"), (w => w.SkipCullingRefresh = true, ""), (w => w.LeavePartBehind = true, ",\"active\":false") };
        foreach (var (inject, extra) in cases) {
            var (world, group, scene) = Setup();
            var offsets = Offsets(group); inject(world);
            var engine = new PatchEngine(scene);
            var outcome = engine.Apply(Doc(Group("move-house", extra: extra))).Outcomes["move-house"];
            Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal), "A failure after mutation is restored: " + outcome.Error);
            Assert(group.Position.SequenceEqual(new[] { 100.0, 10, 20 }) && group.Rotation[3] == 1 && group.Active && Offsets(group).SequenceEqual(offsets) && group.Cullers.All(c => c.Registered.SequenceEqual(new[] { 100.0, 10, 20 })),
                "The captured placement, activity, parts and culling are restored");
        }
    }
    static void IncompleteCleanupBlocksRetry() {
        var (world, group, scene) = Setup();
        world.LeavePartBehind = world.KeepPartBehind = true;
        var engine = new PatchEngine(scene);
        var outcome = engine.Apply(Doc(Group("move-house"))).Outcomes["move-house"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        world.LeavePartBehind = world.KeepPartBehind = false;
        var mutations = world.Mutations;
        var retry = engine.Apply(Doc(Group("again", 1120))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && world.Mutations == mutations, "A retry on the affected group is blocked until cleanup is verified or the world reloads");
    }
    static readonly TargetIdentity Identity = new TargetIdentity(Scene, HousePath, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
    static void EditingHistoryRemovalAndSave() {
        var (world, group, scene) = Setup();
        var offsets = Offsets(group);
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(scene), mapEditor, store); mapEditor.Session = session;
        var moved = new Placement(new[] { 1110.0, 10, 20 }, new[] { 0, 0.130526, 0, 0.991445 });
        Assert(session.Edit(Identity, moved, null).Applied && group.Position[0] == 110 && Offsets(group).SequenceEqual(offsets), "A scene patch editing group move moves the whole group");
        Assert(session.Edit(Identity, null, false).Applied && !group.Active, "A scene patch editing Enabled off deactivates the group root");
        var item = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(session.ScenePatchFileText)["modifications"].First.First;
        Assert((string)item["target"]["structure"] == BakedSceneryGroups.Structure && (string)item["target"]["component"] == "UnityEngine.Transform" && item["placement"] != null && (bool)item["active"] == false,
            "The scene patch file records the group's placement and activity");
        // The native title indication and the Save button both read the native map editor's IsDirty, which the patcher answers from
        // the outstanding participants: every group edit, Undo and Redo must leave the two in agreement.
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Persistence(true, all, "After a group move and a disable");
        mapEditor.Undo(); Assert(group.Active && group.Position[0] == 110, "Undo re-enables the moved group");
        Persistence(true, all, "After Undo of the disable");
        mapEditor.Undo(); Assert(group.Position.SequenceEqual(new[] { 100.0, 10, 20 }) && Offsets(group).SequenceEqual(offsets), "Undo restores the prior placement");
        Persistence(false, new string[0], "After Undo back to the unedited group");
        mapEditor.Redo(); Assert(group.Position[0] == 110, "Redo reapplies the move");
        Persistence(true, all, "After Redo of the move");
        mapEditor.Redo(); Assert(!group.Active, "Redo reapplies the disable");
        Persistence(true, all, "After Redo of the disable");
        Assert(mapEditor.Save() == null && !mapEditor.IsDirty && store.Info.Contains("crankyant/scene-patcher >=0.5.0") && (int)Newtonsoft.Json.Linq.JObject.Parse(store.Patch)["formatVersion"] == 1,
            "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        mapEditor.Undo(); Assert(group.Active, "Undo after Save re-enables the group");
        Persistence(true, new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData }, "After Undo past the saved state");
        mapEditor.Redo(); Assert(!group.Active, "Redo returns to the saved state");
        Persistence(false, new string[0], "After Redo back to the saved state");
        var fresh = new FakeGroup(); var replay = new FakeGroupWorld(); replay.Objects[HousePath] = new List<object> { fresh };
        var reload = new PatchEngine(new BakedSceneryGroups(replay)).Apply(store.Patch);
        Assert(reload.Outcomes.Values.Single().Applied && fresh.Position[0] == 110 && !fresh.Active && fresh.Parts.Count == 3, "A reloaded world reproduces the group edits without duplicated state");
        Assert(session.Remove(Identity).Applied && group.Position[0] == 100 && group.Active && Offsets(group).SequenceEqual(offsets), "Remove modification returns the effective underlying state");
        mapEditor.Undo(); Assert(group.Position[0] == 110 && !group.Active, "Undo of the removal restores this mod's group edits");
        Assert(mapEditor.IsDirty == false, "Undo back to the saved scene patch file leaves nothing to save");
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var (_, other, otherScene) = Setup();
        var strictMapEditor = new NativeMapEditor(); var kept = new PatchEditingSession(new PatchEngine(otherScene), strictMapEditor, strict); strictMapEditor.Session = kept;
        Assert(kept.Edit(Identity, moved, null).Applied && strictMapEditor.Save() == null && strict.Info.Contains("crankyant/scene-patcher >=0.9.0") && !strict.Info.Contains(">=0.5.0"), "A stricter existing constraint is retained: " + strict.Info);
    }
    static void GroupMoveMovesTheWholeGroup() {
        var (world, group, scene) = Setup();
        var offsets = Offsets(group); var activity = Activity(group);
        var result = new PatchEngine(scene).Apply(Doc(Group("move-house")));
        Assert(result.Error == null && result.Outcomes["move-house"].Applied, "A group move applies: " + result.Error + result.Outcomes["move-house"].Error);
        Assert(group.Position.SequenceEqual(new[] { 110.0, 10, 20 }) && group.Rotation[1] == 0.130526, "The group root takes the map placement through the current origin");
        Assert(Offsets(group).SequenceEqual(offsets) && Activity(group).SequenceEqual(activity), "Every part keeps its place and activity relative to the root");
        Assert(group.Cullers.All(c => c.Registered.SequenceEqual(new[] { 110.0, 10, 20 })), "The culling sphere follows the group");
    }
    static void EnabledChangesOnlyTheRoot() {
        var (world, group, scene) = Setup();
        var activity = Activity(group);
        var outcome = new PatchEngine(scene).Apply(Doc(Group("disable-house", extra: ",\"active\":false"))).Outcomes["disable-house"];
        Assert(outcome.Applied && !group.Active && Activity(group).SequenceEqual(activity), "Disabling deactivates only the group root: " + outcome.Error);
        Assert(group.Cullers.All(c => c.Registered == null), "A disabled group holds no culling registration");
        outcome = new PatchEngine(scene).Apply(Doc(Group("enable-house", extra: ",\"active\":true"))).Outcomes["enable-house"];
        Assert(outcome.Applied && group.Active && Activity(group).SequenceEqual(activity) && group.Cullers.All(c => c.Registered != null), "Enabling activates the root and registers culling again: " + outcome.Error);
        // The simple culler on the root stops switching it while the group is disabled and resumes when it is enabled.
        new PatchEngine(scene).Apply(Doc(Group("off", extra: ",\"active\":false")));
        Assert(group.RootCulling == false, "A disabled group's root culler is paused");
        new PatchEngine(scene).Apply(Doc(Group("on", extra: ",\"active\":true")));
        Assert(group.RootCulling == true, "An enabled group's root culler resumes");
        // A root culler that keeps switching a disabled group fails the edit, which restores the captured state.
        world.KeepRootCulling = true;
        outcome = new PatchEngine(scene).Apply(Doc(Group("stuck", extra: ",\"active\":false"))).Outcomes["stuck"];
        Assert(!outcome.Applied && outcome.Error.Contains("root culler"), "A root culler that does not pause fails the disable: " + outcome.Error);
        Assert(group.Active && group.RootCulling == true, "The captured activity and root culler state are restored");
    }
    static void UnmatchedAndAmbiguousPathsFailBeforeMutation() {
        var (world, group, scene) = Setup();
        world.Objects["World/Large Scenery/Bryson/Twin"] = new List<object> { new FakeGroup(), new FakeGroup() };
        var result = new PatchEngine(scene).Apply(Doc(Group("missing", path: HousePath + "/Platform") + "," + Group("twin", path: "World/Large Scenery/Bryson/Twin") + "," + Group("move-house")));
        Assert(!result.Outcomes["missing"].Applied && !result.Outcomes["twin"].Applied && result.Outcomes["twin"].ResolvedTarget == null, "A path with no group or several groups fails that modification");
        Assert(result.Outcomes["move-house"].Applied && world.Mutations == 1 && group.Position[0] == 110, "The other modification still applies, and the failed ones mutate nothing");
        var other = Group("transform-elsewhere").Replace("baked-scenery-group-v1", "bridge-colliders-v1");
        Assert(!new PatchEngine(scene).Apply(Doc(other)).Outcomes["transform-elsewhere"].Applied, "Another structure does not resolve as a group");
    }
    static Node Pad() => new Node("Crossing Pad").With("Track.TrackMarker", "Track.TrackCurveMeshBuilder").Solid().Family();
    static Node Whistle(string side) => new Node("Whistle Sign " + side).With("Track.TrackMarker", "Track.TrackRelativePosition").Solid().Family();
    static Node Crossbuck(string name) => new Node(name, new Node("LOD0").Solid(), new Node("LOD1").Solid()).With("Helpers.Culling.RendererCuller");
    static Node Unprotected(string name) => new Node(name, Pad(), Whistle("E"), Whistle("W"), Crossbuck("Sign-Crossing0 (1)"), Crossbuck("Sign-Crossing0 (2)"));
    // A scene object whose children are crossings, or a crossing and a road bridge, is a folder; an unprotected crossing
    // is a folder too, and its children keep their families.
    static void CrossingFoldersHoldTheirChildren() {
        var setup = new Node("Grade Crossing Setup", new Node("Mast").Solid()).With("Track.Crossings.GradeCrossingController").Family();
        var world = new Node("World",
            new Node("Roads Bryson",
                new Node("US19 Gov Island", setup, new Node("Concrete Road Bridge - 60m").Solid()),
                new Node("Old River Rd", Unprotected("Crossing Unprotected"), Unprotected("Crossing Unprotected"), Unprotected("Crossing Unprotected")),
                Unprotected("Crossing Unprotected (1)")),
            new Node("Large Scenery", new Node("Bryson", new Node("Station Yard", Pad(), new Node("Shed").Solid()))));
        var verdicts = BakedSceneryRules.Classify(world);
        Assert(At(verdicts, "World/Roads Bryson/US19 Gov Island") == null, "A crossing and a road bridge make a folder, not a group");
        Assert(At(verdicts, "World/Roads Bryson/US19 Gov Island/Grade Crossing Setup")?.Disposition == GroupDisposition.ExistingFamily, "The crossing in the folder keeps its family");
        Assert(At(verdicts, "World/Roads Bryson/US19 Gov Island/Concrete Road Bridge - 60m")?.Ticket == "#97", "The road bridge in the folder is a group root for the road bridge ticket");
        var crossbucks = verdicts.Where(v => v.Path.StartsWith("World/Roads Bryson/Old River Rd/", StringComparison.Ordinal) && v.Path.EndsWith(")", StringComparison.Ordinal)).ToArray();
        Assert(At(verdicts, "World/Roads Bryson/Old River Rd") == null && crossbucks.Length == 6 && crossbucks.All(v => v.Disposition == GroupDisposition.InspectionOnly && v.Ticket == "#107"),
            "Old River Rd is a folder of three unprotected crossings, whose six crossbucks are inspection only for #107");
        var single = verdicts.Where(v => v.Path.StartsWith("World/Roads Bryson/Crossing Unprotected (1)/", StringComparison.Ordinal)).ToArray();
        Assert(single.Count(v => v.Disposition == GroupDisposition.ExistingFamily) == 3 && single.Count(v => v.Ticket == "#107") == 2 && At(verdicts, "World/Roads Bryson/Crossing Unprotected (1)") == null,
            "The pad and whistle signs of an unprotected crossing keep their families and its crossbucks wait for #107");
        Assert(At(verdicts, "World/Large Scenery/Bryson/Station Yard")?.Disposition == GroupDisposition.InspectionOnly, "A group that holds a pad among other parts is no crossing folder");
    }
    // Every candidate that is no group comes back with the coverage rule that keeps it out, so evidence can explain it.
    static void CoverageRulesKeepObjectsOut() {
        var runtime = new Node("Spawned Car").Solid(); runtime.Serialized = false;
        var world = new Node("World",
            new Node("Signs", new Node("Loc: Sylva Int").Solid().Family(), new Node("USGS Comment").With("Helpers.Comment")),
            new Node("Roads Bryson", runtime, new Node("br-depot").With("Game.MapObjectActivation").Solid()),
            new Node("Map Labels", new Node("Bryson").With("UI.Map.MapLabel").Solid(), new Node("Station Icons", new Node("Icon").With("UI.Map.MapIcon").Solid())),
            new Node("Builder Photo Setup", new Node("Backdrop").Solid()),
            new Node("Track", new Node("Segment Mesh").Solid()).With("Track.TrackObjectManager", "Game.MapLoader"),
            new Node("Telegraph Poles", new Node("Pole").Solid()).With("TelegraphPoles.TelegraphPoleManager"));
        var verdicts = BakedSceneryRules.Classify(world);
        void Expect(string path, GroupDisposition disposition, string rule) {
            var verdict = At(verdicts, path);
            Assert(verdict != null && verdict.Disposition == disposition && (rule == null || verdict.Reason == rule), path + " is " + disposition + " under " + rule + ": " + verdict?.Disposition + " " + verdict?.Reason);
        }
        Expect("World/Signs/Loc: Sylva Int", GroupDisposition.ExistingFamily, null);
        Expect("World/Signs/USGS Comment", GroupDisposition.NotListed, BakedSceneryRules.NoPhysicalPresence);
        Expect("World/Roads Bryson/Spawned Car", GroupDisposition.NotListed, BakedSceneryRules.RuntimeObject);
        Expect("World/Roads Bryson/br-depot", GroupDisposition.NotListed, BakedSceneryRules.NativeMapData);
        Expect("World/Map Labels/Bryson", GroupDisposition.NotListed, BakedSceneryRules.MapLabel);
        Expect("World/Map Labels/Station Icons", GroupDisposition.InspectionOnly, null);
        Expect("World/Builder Photo Setup/Backdrop", GroupDisposition.NotListed, BakedSceneryRules.BuilderPhotoRig);
        Expect("World/Track/Segment Mesh", GroupDisposition.NotListed, BakedSceneryRules.RuntimeObject);
        Expect("World/Telegraph Poles/Pole", GroupDisposition.NotListed, BakedSceneryRules.RuntimeObject);
        Assert(BakedSceneryRules.Classify(new Node("CTC", new Node("Panels", new Node("Panel").Solid()))).Count == 0, "No root outside World holds group roots");
    }
    // Each excluded kind anywhere below the root keeps the group out of the family and lists it as inspection only,
    // with the reason and the ticket that covers it.
    static void LeavesOutGroupsThatLaterTicketsCover() {
        var cases = new[] {
            new { Name = "Alarka Jct Station", Component = "Map.Runtime.MaskComponents.RectangleMapMask", Kind = "scene ground mask", Ticket = "#90" },
            new { Name = "Bryson Depot", Component = "Model.StationAgent", Kind = "station click spot", Ticket = "#89" },
            new { Name = "Coal Loader", Component = "RollingStock.CarContentSwapper", Kind = "car loader", Ticket = "#92" },
            new { Name = "Fontana Lake (1)", Component = "LakePolygon", Kind = "lake surface", Ticket = "#93" },
            new { Name = "Whittier Log Pond", Component = "Scenery.LogPond", Kind = "log pond", Ticket = "#93" },
            new { Name = "Piles", Component = "Scenery.Pile", Kind = "storage pile", Ticket = "#98" },
            new { Name = "Sylva Paperboard", Component = "Scenery.BulkIndustryStorageSynchronizer", Kind = "industry storage display", Ticket = "#98" },
            new { Name = "Trestle Scenery", Component = "Game.Mow.TrackConstructionScenery", Kind = "bridge construction scenery", Ticket = "#95" },
            new { Name = "Alarka", Component = "Character.SpawnPoint", Kind = "spawn point", Ticket = "#94" },
            new { Name = "Station Icons", Component = "UI.Map.MapIcon", Kind = "map icon", Ticket = "#89" },
            new { Name = "Roundhouse 3", Component = "RollingStock.Controls.KeyValuePickableToggle", Kind = "pickable toggle", Ticket = "#91" },
        };
        foreach (var item in cases) {
            // The excluded component sits on a part, not on the root, and the root has no renderer of its own.
            var world = new Node("World", new Node("Large Scenery", new Node("Town", new Node(item.Name, new Node("Part").With(item.Component), new Node("Shell").Solid()))));
            var verdict = At(BakedSceneryRules.Classify(world), "World/Large Scenery/Town/" + item.Name);
            Assert(verdict != null && verdict.Disposition == GroupDisposition.InspectionOnly, "A group that holds a " + item.Kind + " is inspection only");
            Assert(verdict.Reason.Contains(item.Kind) && verdict.Ticket == item.Ticket, "The reason names the " + item.Kind + " and " + item.Ticket + ": " + verdict.Reason + " " + verdict.Ticket);
        }
        // A group that holds track markers is left out because the markers keep their own family; no ticket covers it yet.
        var signs = At(BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Bryson", new Node("Bryson Signs", new Node("Yard Limit").With("Track.TrackMarker").Solid().Family()))))), "World/Large Scenery/Bryson/Bryson Signs");
        Assert(signs?.Disposition == GroupDisposition.InspectionOnly && signs.Ticket == null && signs.ReasonIsSentence &&
            signs.Reason == "the group holds track markers, which keep their own family, and moving such a group as a whole is not supported yet", "A group with track markers states why it is left out, without a ticket: " + signs?.Reason + " " + signs?.Ticket);
        ResearchHierarchy();
    }
    // The paths, components and physical presence recorded for the level4 scene of the base-game map
    // (Records/BakedScenery/groups_classified.csv, buckets X1 to X3).
    static void ResearchHierarchy() {
        var verdicts = BakedSceneryRules.Classify(new Node("World",
            new Node("Large Scenery",
                new Node("Almond Bridge", new Node("Abutments", new Node("Abutment").Solid()), new Node("Steel", new Node("Girder").Solid())),
                new Node("Plate Girder Bridge", new Node("Plate Girder Bridge").Solid(), new Node("Plate Girder Bridge (1)").Solid(), new Node("Plate Girder Bridge End", new Node("End").Solid())),
                new Node("Bryson", new Node("Bridge 1", new Node("Deck").Solid()).With("Helpers.Culling.RendererCuller", "Game.Mow.TrackConstructionScenery")),
                new Node("Almond", new Node("Almond Road Bridge", new Node("Deck").Solid()).With("Map.Runtime.MaskComponents.RectangleMapMask", "Helpers.Culling.RendererCuller"))),
            new Node("Roads Sylva", new Node("Sylva Road Bridge 20m", new Node("Deck").Solid()).With("Map.Runtime.MaskComponents.RectangleMapMask", "Helpers.Culling.RendererCuller")),
            new Node("POIs", new Node("Alarka").With("Character.SpawnPoint"), new Node("Bryson").With("Character.SpawnPoint")),
            new Node("Map Labels", new Node("Station Icons", new Node("Bryson").With("UI.Map.MapIcon", "UnityEngine.UI.Image", "UnityEngine.BoxCollider").Solid()))));
        foreach (var path in new[] { "World/Large Scenery/Almond Bridge/Abutments", "World/Large Scenery/Almond Bridge/Steel", "World/Large Scenery/Plate Girder Bridge/Plate Girder Bridge",
            "World/Large Scenery/Plate Girder Bridge/Plate Girder Bridge (1)", "World/Large Scenery/Plate Girder Bridge/Plate Girder Bridge End", "World/Large Scenery/Bryson/Bridge 1" }) {
            var verdict = At(verdicts, path);
            Assert(verdict?.Disposition == GroupDisposition.InspectionOnly && verdict.Ticket == "#95", path + " is a rail bridge part outside Ela for #95: " + verdict?.Disposition + " " + verdict?.Ticket);
        }
        foreach (var path in new[] { "World/Large Scenery/Almond/Almond Road Bridge", "World/Roads Sylva/Sylva Road Bridge 20m" })
            Assert(At(verdicts, path)?.Disposition == GroupDisposition.InspectionOnly && At(verdicts, path).Ticket == "#97", path + " is a road bridge for #97");
        // Physical presence comes first: a spawn point root has no renderer, collider or light.
        foreach (var path in new[] { "World/POIs/Alarka", "World/POIs/Bryson" })
            Assert(At(verdicts, path)?.Disposition == GroupDisposition.NotListed && At(verdicts, path).Reason == BakedSceneryRules.NoPhysicalPresence, path + " has no physical presence and is not listed");
        // The station icons carry colliders, so their group is physical and waits for the map icon ticket.
        Assert(At(verdicts, "World/Map Labels/Station Icons")?.Disposition == GroupDisposition.InspectionOnly && At(verdicts, "World/Map Labels/Station Icons").Ticket == "#89", "The station icons wait for #89");
    }
    // A group root is one level below a World category folder, and below World/Large Scenery one level below a town.
    static void ListsSimpleGroupRoots() {
        var world = new Node("World",
            new Node("Large Scenery", new Node("Bryson", new Node("Bryson House Track", new Node("Platform").Solid()))),
            new Node("Roads Barkers", new Node("1932 Chevy Truck LP").Solid()));
        var verdicts = BakedSceneryRules.Classify(world);
        var house = At(verdicts, "World/Large Scenery/Bryson/Bryson House Track");
        Assert(house != null && house.Disposition == GroupDisposition.Group, "A town child with a renderer below it is a group root");
        Assert(At(verdicts, "World/Roads Barkers/1932 Chevy Truck LP")?.Disposition == GroupDisposition.Group, "A child of another category is a group root");
        Assert(At(verdicts, "World/Large Scenery/Bryson") == null && At(verdicts, "World/Large Scenery/Bryson/Bryson House Track/Platform") == null && At(verdicts, "World/Large Scenery") == null,
            "Folders above group roots and parts below them are no group roots");
    }
}
