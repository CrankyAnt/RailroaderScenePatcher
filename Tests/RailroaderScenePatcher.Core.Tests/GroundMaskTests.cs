// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// One scene ground mask of the controlled world: the object that holds it, the top scene object when On is null, the turn of
// its shape against its holder, and the keys of the registrations it holds in the terrain system.
class FakeMask {
    public string Id, Holder;
    public FakeGroup Group; public FakePart On;
    public bool Flattens, ComponentEnabled = true;
    public double Degrees;
    // The terrain modifiers the component makes when it registers.
    public int Modifiers = 1;
    // What the observation reports as that count, when the observer's model of the game disagrees with the game.
    public int? Reported;
    // A curve shape: its registration centre is the centre of its bounds, at this offset in its holder's own axes, so a
    // turn, a pitch or a roll of the holder moves it. Null for a shape centred on its holder.
    public double[] CurveOffset;
    // A curve that does not follow: each registration holds this height, wherever its holder stands.
    public double? StuckHeight;
    public readonly List<string> Keys = new List<string>();
    internal bool Registered;
}
// A controlled terrain system over the controlled group world, with the lifecycle the game's mask components have: a
// mask registers where its holder stands when it is enabled, releases when it is disabled, keeps a stale registration
// when only its holder's transform moves, and registers again at its holder on a rebuild. Every registration change
// invalidates the loaded terrain tile at its place, which then builds asynchronously.
class FakeMaskWorld : IGroundMaskWorld {
    public const double Tile = 500;
    readonly FakeGroupWorld groups;
    public readonly List<FakeMask> All = new List<FakeMask>();
    public readonly Dictionary<string, GroundMaskRegistration> Store = new Dictionary<string, GroundMaskRegistration>();
    public readonly HashSet<string> PendingTiles = new HashSet<string>(), UnloadedTiles = new HashSet<string>();
    public readonly List<string> Invalidated = new List<string>();
    public double Clock;
    // FailRebuildCount: the next rebuilds fail. FailAfterRebuilds: every rebuild fails once that many succeeded.
    // FailAttempt: the rebuild attempt with that number, counted from 1 over Attempts, fails.
    public int FailRebuildCount, Rebuilds, FailAfterRebuilds = -1, Attempts, FailAttempt = -1;
    int nextKey;
    public event Action<double[], double[]> TileBuilt;
    public FakeMaskWorld(FakeGroupWorld groups) { this.groups = groups; }
    public FakeMask Add(FakeMask mask) { All.Add(mask); Sync(); return mask; }
    static string TileOf(IReadOnlyList<double> p) => Math.Floor(p[0] / Tile) + "," + Math.Floor(p[2] / Tile);
    bool Active(FakeMask mask) {
        if (!mask.ComponentEnabled || !mask.Group.Active) return false;
        for (var part = mask.On; part != null; part = part.Parent) if (!part.Active) return false;
        return true;
    }
    Placement At(FakeMask mask) => mask.On == null ? groups.Observe(mask.Group).Root.Placement : groups.ObservePart(mask.On).Part.Placement;
    // The direction of the mask's shape on the ground: its holder's rotation, turned by the mask's own degrees.
    static double[] Axis(FakeMask mask, Placement at) {
        var forward = Turn.Rotate(Turn.Multiply(Turn.Yaw(mask.Degrees), at.Rotation), new[] { 0.0, 0, 1 });
        var length = Math.Sqrt(forward[0] * forward[0] + forward[2] * forward[2]);
        return new[] { forward[0] / length, 0, forward[2] / length };
    }
    void Invalidate(IReadOnlyList<double> at) { var tile = TileOf(at); if (!UnloadedTiles.Contains(tile)) PendingTiles.Add(tile); }
    void Register(FakeMask mask) {
        var at = At(mask);
        var centre = Shape(mask, at);
        if (mask.StuckHeight != null) centre[1] = mask.StuckHeight.Value;
        for (var i = 0; i < mask.Modifiers; i++) { var key = "k" + nextKey++; Store[key] = new GroundMaskRegistration(centre, Axis(mask, at)); mask.Keys.Add(key); Invalidate(at.Position); }
        mask.Registered = true;
    }
    // The centre a registration made at the holder's pose holds.
    static double[] Shape(FakeMask mask, Placement at) {
        if (mask.CurveOffset == null) return at.Position.ToArray();
        var turned = Turn.Rotate(at.Rotation, mask.CurveOffset);
        return new[] { at.Position[0] + turned[0], at.Position[1] + turned[1], at.Position[2] + turned[2] };
    }
    void Release(FakeMask mask) {
        foreach (var key in mask.Keys) { if (Store.TryGetValue(key, out var held)) Invalidate(held.Centre); Store.Remove(key); }
        mask.Keys.Clear(); mask.Registered = false;
    }
    // The components' own lifecycle: a mask that became active registers, and one that became inactive releases.
    void Sync() {
        foreach (var mask in All) {
            var active = Active(mask);
            if (active && !mask.Registered) Register(mask);
            else if (!active && mask.Registered) Release(mask);
        }
    }
    public IReadOnlyList<GroundMaskState> Masks(object target) {
        Sync();
        var found = All.Where(mask => target is FakeGroup group ? mask.Group == group : mask.On == target || groups.Below(mask.On, target));
        return found.Select(mask => {
            var at = At(mask);
            return new GroundMaskState(mask.Id, mask.Holder, Active(mask), mask.Flattens, at.Position, Axis(mask, at), mask.Reported ?? mask.Modifiers,
                mask.Keys.Where(Store.ContainsKey).Select(key => Store[key]), mask.Keys.Count(key => !Store.ContainsKey(key)), Shape(mask, at));
        }).ToArray();
    }
    public void Rebuild(object target, string id) {
        Sync();
        Attempts++;
        if (FailRebuildCount > 0 || (FailAfterRebuilds >= 0 && Rebuilds >= FailAfterRebuilds) || Attempts == FailAttempt) { FailRebuildCount--; throw new InvalidOperationException("mask injected"); }
        Rebuilds++;
        var mask = All.Single(m => m.Id == id);
        Release(mask); Register(mask);
    }
    public bool TileLoaded(double[] position) => !UnloadedTiles.Contains(TileOf(position));
    public void InvalidateTile(double[] position) { Invalidated.Add(TileOf(position)); Invalidate(position); }
    public bool TerrainIdle => PendingTiles.Count == 0;
    public double Now => Clock;
    // The game builds each invalidated loaded tile and raises its bounds.
    public void BuildTiles(string only = null) {
        foreach (var tile in PendingTiles.Where(t => only == null || t == only).ToArray()) {
            var parts = tile.Split(',').Select(Double.Parse).ToArray();
            PendingTiles.Remove(tile);
            TileBuilt?.Invoke(new[] { parts[0] * Tile, 0, parts[1] * Tile }, new[] { (parts[0] + 1) * Tile, 0, (parts[1] + 1) * Tile });
        }
    }
}

// One flattening scene ground mask held by an object that a capability moves as a single object, such as a bridge, a
// milepost, a crossing pad, a station map icon or a spawn point. The mask reads its holder through the capability's own
// Read, registers where the holder stands while it is active, keeps its registration when only the holder moves, and
// registers again at the holder on a rebuild. Each terrain tile is one metre wide, so the old and the new place of a
// move are separate writes that stay outstanding until BuildTiles.
sealed class HeldMaskTerrain : IGroundMaskWorld {
    // Set once the capability that reads the holder exists.
    internal object Holder;
    internal Func<ObjectState> Read;
    // The name of the object that holds the mask, and where it stands from the edited object, as a board below a track
    // marker does.
    internal string HolderName = "Mask";
    internal double[] Offset = { 0, 0, 0 };
    internal double[] Registered;
    internal int Rebuilds;
    internal bool FailRebuild, Built;
    public event Action<double[], double[]> TileBuilt;
    ObjectState Now => Read();
    double[] Place => Now.Placement.Position.Select((v, i) => v + Offset[i]).ToArray();
    bool Active => Now.Active != false;
    void Sync() { if (!Active) Registered = null; else if (Registered == null) Registered = Place; }
    static double[] Axis(Placement at) { var f = Turn.Rotate(at.Rotation, new[] { 0.0, 0, 1 }); var l = Math.Sqrt(f[0] * f[0] + f[2] * f[2]); return new[] { f[0] / l, 0, f[2] / l }; }
    double[] registeredAxis;
    public IReadOnlyList<GroundMaskState> Masks(object target) {
        if (!ReferenceEquals(target, Holder)) return new GroundMaskState[0];
        Sync();
        var at = Now.Placement;
        if (Registered != null && registeredAxis == null) registeredAxis = Axis(at);
        if (Registered == null) registeredAxis = null;
        var registrations = Registered == null ? new GroundMaskRegistration[0] : new[] { new GroundMaskRegistration(Registered, registeredAxis) };
        return new[] { new GroundMaskState("mask", HolderName, Active, true, Place, Axis(at), 1, registrations, 0, Place) };
    }
    public void Rebuild(object target, string id) {
        if (FailRebuild) throw new InvalidOperationException("mask injected");
        Rebuilds++; Registered = Place; registeredAxis = Axis(Now.Placement); Built = false;
    }
    public bool TileLoaded(double[] position) => true;
    public void InvalidateTile(double[] position) { Built = false; }
    public bool TerrainIdle => Built;
    double ITerrainWorld.Now => 0;
    internal void BuildTiles(IEnumerable<double[]> places) { Built = true; foreach (var p in places) TileBuilt?.Invoke(new[] { Math.Floor(p[0]), 0, Math.Floor(p[2]) }, new[] { Math.Floor(p[0]) + 1, 0, Math.Floor(p[2]) + 1 }); }
}

// Baked buildings with their own scene ground masks: the flag-stop stations and depots of the base-game map join the
// Scene object and Child object families, and a move of the object that holds a mask, or of its group, takes the mask's
// registration along, with the terrain output at the old and the new place tracked to completion.
static class GroundMaskTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Ground masks: " + message); }
    const string SceneName = "BushnellWhittier-MapManager", Station = "World/Large Scenery/Rhodo/Rhodo Station", Foundation = Station + "/Foundation", Bench = Station + "/Bench";
    static readonly double[] Upright = { 0, 0, 0, 1 }, Authored = { 1100, 10, 20 }, Near = { 1110, 10, 20 }, Far = { 1700, 12, 40 };
    // A station group with a shell, a bench and a foundation part. The mask stands on the root unless onPart.
    sealed class Stop {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeMaskWorld Terrain;
        public readonly GroundMasks Masks;
        public readonly FakeGroup Group = new FakeGroup { Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakePart Slab, Seat;
        public readonly FakeMask Mask;
        public readonly PatchEngine Engine;
        public Stop(bool onPart = false, bool flattens = false, double[] curveOffset = null) {
            Terrain = new FakeMaskWorld(World); Masks = new GroundMasks(Terrain);
            Slab = Group.Add(new FakePart { Id = "Foundation#1", Offset = new[] { 0.0, 0, 4 } });
            Seat = Group.Add(new FakePart { Id = "Bench#2", Offset = new[] { 3.0, 0, 0 } });
            World.Objects[Station] = new List<object> { Group };
            World.PartsAt[Foundation] = new List<object> { Slab }; World.PartsAt[Bench] = new List<object> { Seat };
            World.SetActive(Group, true); World.Mutations = 0;
            Mask = Terrain.Add(new FakeMask { Id = "mask", Holder = onPart ? "Foundation" : "Rhodo Station", Group = Group, On = onPart ? Slab : null, Degrees = 30, Flattens = flattens, CurveOffset = curveOffset });
            Terrain.BuildTiles();
            var holds = new PartHolds();
            var scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new SceneObjects(World, holds: holds, masks: Masks)); scene.Add(new ChildObjects(World, holds, masks: Masks));
            Engine = new PatchEngine(scene);
        }
        public double[] At(object handle) => (handle is FakeGroup group ? World.Observe(group).Root.Placement : World.ObservePart(handle).Part.Placement).Position.ToArray();
        public GroundMaskState State => Terrain.Masks(Group).Single();
        // The mask holds one registration in the terrain system, and it stands at the place with the direction.
        public bool RegisteredOnceAt(double[] centre, double yaw) =>
            Terrain.Store.Count == 1 && Mask.Keys.Count == 1 && Close(Terrain.Store[Mask.Keys[0]].Centre, centre) && Close(Terrain.Store[Mask.Keys[0]].Axis, new[] { Math.Sin(yaw * Math.PI / 180), 0, Math.Cos(yaw * Math.PI / 180) });
        public string Where() => "group " + N(At(Group)) + "; " + String.Join("; ", Terrain.Store.Values.Select(r => "registration " + N(r.Centre) + " axis " + N(r.Axis)));
    }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static bool Close(IReadOnlyList<double> a, IReadOnlyList<double> b) => a != null && b != null && a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= 0.001);
    static string Mod(string id, string path, string structure, double[] position = null, bool? active = null, double yaw = 0, double[] rotation = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + N(position) + "],\"rotation\":[" + String.Join(",", (rotation ?? Turn.Yaw(yaw)).Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string GroupMod(string id, double[] position = null, bool? active = null, double yaw = 0, double[] rotation = null) => Mod(id, Station, SceneObjects.Structure, position, active, yaw, rotation);
    static string PartMod(string id, string path, double[] position = null, bool? active = null, double yaw = 0) => Mod(id, path, ChildObjects.Structure, position, active, yaw);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static readonly TargetIdentity StationId = new TargetIdentity(SceneName, Station, SceneObjects.ComponentType, SceneObjects.Structure);
    static readonly TargetIdentity FoundationId = new TargetIdentity(SceneName, Foundation, ChildObjects.ComponentType, ChildObjects.Structure);

    // A map context change in one session, such as the editor's default map and then a mod's map: the earlier host is
    // released, the next host checks the game's ground mask members, and only then does Unity destroy the earlier host,
    // whose OnDestroy releases it again. The members stay available for the next world, so a group with a flattening
    // mask stays editable.
    static void TheNextWorldKeepsTheMaskMembersWhenTheEarlierHostIsDestroyed() {
        var availability = new WorldOwnedFlag();
        object earlier = new object(), next = new object();
        availability.Take(earlier);
        availability.Release(earlier);
        Assert(!availability.Held, "The released world gives up the members");
        availability.Take(next);
        availability.Release(earlier);
        Assert(availability.Held, "The earlier host's OnDestroy at the end of the frame leaves the next world's members available");
        availability.Release(null);
        Assert(availability.Held, "A host that never checked the members releases nothing");
        availability.Release(next);
        Assert(!availability.Held, "The owning world's teardown makes them unavailable");
    }
    // A terrain world whose tiles are all built, for the tracking of writes alone.
    sealed class BuiltTerrain : ITerrainWorld {
        public bool TileLoaded(double[] position) => true;
        public void InvalidateTile(double[] position) { }
        public bool TerrainIdle => true;
        public double Now => 0;
        public event Action<double[], double[]> TileBuilt { add { } remove { } }
    }
    // Writes within 0.05 m of an outstanding write, rounded to a tenth of a metre on each map axis, are one write; the
    // label shows the position in the shown number format.
    static void NearbyTerrainWritesMergeWithinFiveCentimetres() {
        using (var output = new TerrainOutput(new BuiltTerrain())) {
            var target = new object();
            output.Track(target, new[] { 100.0, 5, 200 });
            output.Track(target, new[] { 100.04, 5, 200.03 });
            Assert(output.Output(target).Outstanding.Count == 1, "A write within 0.05 m merges with the outstanding one: " + String.Join("; ", output.Output(target).Outstanding));
            output.Track(target, new[] { 100.06, 5, 200 });
            Assert(output.Output(target).Outstanding.Count == 2, "A write 0.06 m away is a write of its own: " + String.Join("; ", output.Output(target).Outstanding));
            Assert(output.Output(target).Outstanding.First() == "terrain output at map x=100, z=200" && output.Output(target).Outstanding.Last() == "terrain output at map x=100.06, z=200",
                "The labels show the shown number format: " + String.Join("; ", output.Output(target).Outstanding));
        }
    }
    public static void Run() {
        NearbyTerrainWritesMergeWithinFiveCentimetres();
        TheNextWorldKeepsTheMaskMembersWhenTheEarlierHostIsDestroyed();
        GroupMoveCarriesTheMaskOfItsRoot();
        PartMoveCarriesTheMaskItHolds();
        HeldMaskPartStaysWhenItsGroupMoves();
        MaskTheEditLeavesAloneIsNotJudged();
        SmallMovesDoNotLeaveTheMaskBehind();
        FailedSecondRebuildRestoresAndWaitsForTerrain();
        FlatteningMaskMovesWithItsHolder();
        HeightOnlyMoveRebuildsTheFlatteningMask();
        CurveMaskFollowsItsHolderPose();
        RefusedLoadMoveShowsAsRefused();
        ActivityReleasesAndRegistersTheMask();
        FailureAfterMutationRestoresTheMask(false);
        FailureAfterMutationRestoresTheMask(true);
        TerrainWriteFailureIsIdentifiableAndRetryable();
        EditingHistorySaveAndReload(false);
        EditingHistorySaveAndReload(true);
        InventoryListsTheStationsAndDepotsWithTheirParts();
        ObjectTabNamesTheMaskHolder();
        MarkerRootGainsTheMaskHandling();
        MarkerSceneBuiltBeforeTheMasksGainsThem();
        GenericMarkerMovesItsMask();
        BoardMaskStaysWithTheBoardFamily();
        BoardBelowMovedMarkerMovesItsMask();
        Console.WriteLine("PASS: ground masks");
    }

    // The mask on the top scene object follows a move and a turn of the group: one registration, at the new centre, with
    // the shape's direction turned with the group. The terrain at the old and the new place is outstanding until the
    // game rebuilds it.
    static void GroupMoveCarriesTheMaskOfItsRoot() {
        var f = new Stop();
        Assert(f.RegisteredOnceAt(Authored, 30), "The controlled mask is registered once where the station stands: " + f.Where());
        var doc = Doc(GroupMod("move-station", Far, yaw: 40));
        var result = f.Engine.Apply(doc);
        Assert(result.Outcomes["move-station"].Applied, "The station move applies: " + result.Outcomes["move-station"].Error);
        Assert(Close(f.At(f.Group), Far) && f.RegisteredOnceAt(Far, 70), "The mask's center and rotation follow the station, registered once: " + f.Where());
        var output = f.Masks.Output(f.Group);
        Assert(output.Outstanding.Count == 2 && !output.Settled && output.Error == null, "The terrain at the old and the new place is outstanding: " + String.Join("; ", output.Outstanding));
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(f.Masks.Output(f.Group).Settled && f.Masks.Output(f.Group).Completed.Count == 2, "Rebuilt tiles complete both writes");
        var rebuilds = f.Terrain.Rebuilds;
        Assert(f.Engine.Apply(doc).Outcomes["move-station"].Applied && f.RegisteredOnceAt(Far, 70) && f.Terrain.Rebuilds == rebuilds && f.Masks.Output(f.Group).Settled,
            "A repeated application leaves the one registration alone and starts no terrain output: " + f.Where());
        var shifted = new Stop(); shifted.World.Origin = 1010;
        Assert(shifted.Engine.Apply(doc).Outcomes["move-station"].Applied && shifted.RegisteredOnceAt(Far, 70), "A load at another origin reaches the same registration: " + shifted.Where());
    }

    // The part that holds the mask moves alone: the mask follows the part, and the group and its other parts stay. A
    // move of a part without a mask rebuilds nothing. A group move carries the part's mask too.
    static void PartMoveCarriesTheMaskItHolds() {
        var f = new Stop(onPart: true);
        Assert(f.RegisteredOnceAt(new[] { 1100.0, 10, 24 }, 30), "The controlled mask stands at its part: " + f.Where());
        var result = f.Engine.Apply(Doc(PartMod("move-foundation", Foundation, new[] { 1150.0, 10, 60 }, yaw: 90)));
        Assert(result.Outcomes["move-foundation"].Applied, "The part move applies: " + result.Outcomes["move-foundation"].Error);
        Assert(f.RegisteredOnceAt(new[] { 1150.0, 10, 60 }, 120) && Close(f.At(f.Group), Authored) && Close(f.At(f.Seat), new[] { 1103.0, 10, 20 }), "The mask follows its part; the group and the bench stay: " + f.Where());
        Assert(f.Masks.Output(f.Slab).Outstanding.Count == 2 && f.Masks.Output(f.Group).Outstanding.Count == 0, "The terrain output belongs to the edited part: " + String.Join("; ", f.Masks.Output(f.Slab).Outstanding));
        var other = new Stop(onPart: true); var rebuilds = other.Terrain.Rebuilds;
        Assert(other.Engine.Apply(Doc(PartMod("move-bench", Bench, new[] { 1120.0, 10, 30 }))).Outcomes["move-bench"].Applied && other.Terrain.Rebuilds == rebuilds && other.RegisteredOnceAt(new[] { 1100.0, 10, 24 }, 30),
            "A part without a mask moves without touching the mask: " + other.Where());
        var whole = new Stop(onPart: true);
        Assert(whole.Engine.Apply(Doc(GroupMod("move-station", Near))).Outcomes["move-station"].Applied && whole.RegisteredOnceAt(new[] { 1110.0, 10, 24 }, 30), "A group move carries the mask of its part: " + whole.Where());
    }

    // An edit that leaves a mask as it was neither rebuilds nor judges it: a disable or a move of another part applies
    // even where the observed registrations disagree with what the observer expects of the game.
    static void MaskTheEditLeavesAloneIsNotJudged() {
        var f = new Stop(onPart: true); f.Mask.Reported = 2;
        var rebuilds = f.Terrain.Rebuilds;
        var result = f.Engine.Apply(Doc(PartMod("move-bench", Bench, new[] { 1120.0, 10, 30 })));
        Assert(result.Outcomes["move-bench"].Applied && f.Terrain.Rebuilds == rebuilds, "A move of another part applies and rebuilds nothing: " + result.Outcomes["move-bench"].Error);
        result = new PatchEngine(Scene(f)).Apply(Doc(PartMod("hide-bench", Bench, active: false)));
        Assert(result.Outcomes["hide-bench"].Applied && !f.Seat.Active && f.Terrain.Rebuilds == rebuilds && f.RegisteredOnceAt(new[] { 1100.0, 10, 24 }, 30), "A disable of another part applies and leaves the mask alone: " + result.Outcomes["hide-bench"].Error);
        var moved = new PatchEngine(Scene(f)).Apply(Doc(PartMod("move-foundation", Foundation, new[] { 1150.0, 10, 60 }))).Outcomes["move-foundation"];
        Assert(!moved.Applied && moved.Error.Contains("registrations instead of 2"), "A move of the mask's own holder still judges the mask: " + moved.Error);
    }

    // A run of moves that are each smaller than the tolerance of the check adds up: the mask is rebuilt as soon as it
    // disagrees with its holder, although the holder moved little since the edit before, as on each step of a slow
    // gizmo drag.
    static void SmallMovesDoNotLeaveTheMaskBehind() {
        var f = new Stop();
        var first = new[] { 1100.06, 10, 20 }; var second = new[] { 1100.12, 10, 20 }; var third = new[] { 1100.18, 10, 20 };
        Assert(f.Engine.Apply(Doc(GroupMod("nudge", first))).Outcomes["nudge"].Applied, "The first small move applies");
        Assert(new PatchEngine(Scene(f)).Apply(Doc(GroupMod("nudge", second))).Outcomes["nudge"].Applied && f.RegisteredOnceAt(second, 30), "After the second small move the mask stands at its holder again: " + f.Where());
        Assert(new PatchEngine(Scene(f)).Apply(Doc(GroupMod("nudge", third))).Outcomes["nudge"].Applied && new PatchEngine(Scene(f)).Apply(Doc(GroupMod("nudge", new[] { 1100.24, 10, 20 }))).Outcomes["nudge"].Applied
            && f.RegisteredOnceAt(new[] { 1100.24, 10, 20 }, 30), "Further small moves keep the mask within the tolerance of its holder: " + f.Where());
    }

    // Two masks: the first follows the move, the rebuild of the second fails, and the restoration rebuilds the first
    // back. The placement and both registrations are as before, the terrain at the places the first mask left and
    // reached is outstanding, and the next edit waits until the game has rebuilt it.
    static void FailedSecondRebuildRestoresAndWaitsForTerrain() {
        var f = new Stop();
        var second = f.Terrain.Add(new FakeMask { Id = "second", Holder = "Foundation", Group = f.Group, On = f.Slab });
        f.Terrain.BuildTiles();
        f.Terrain.FailAttempt = f.Terrain.Attempts + 2;
        var outcome = f.Engine.Apply(Doc(GroupMod("move-station", Far))).Outcomes["move-station"];
        Assert(!outcome.Applied && outcome.Error.Contains("back as it was") && outcome.Error.Contains("mask injected"), "The failed attempt is restored: " + outcome.Error);
        Assert(Close(f.At(f.Group), Authored) && f.Terrain.Store.Count == 2 && Close(f.Terrain.Store[f.Mask.Keys.Single()].Centre, Authored) && Close(f.Terrain.Store[second.Keys.Single()].Centre, new[] { 1100.0, 10, 24 }),
            "The placement and both registrations are restored, each once: " + f.Where());
        var output = f.Masks.Output(f.Group);
        Assert(output.Outstanding.Count == 2 && output.Error == null && output.Outstanding.Any(l => l.Contains("x=1100,")) && output.Outstanding.Any(l => l.Contains("x=1700,")), "The terrain where the first mask stood and where it went is outstanding: " + String.Join("; ", output.Outstanding));
        var again = f.Engine.Apply(Doc(GroupMod("again", Near))).Outcomes["again"];
        Assert(!again.Applied && again.Failure == FailureKind.AwaitingOutput && Close(f.At(f.Group), Authored), "The next edit waits for the restored terrain output: " + again.Failure + " " + again.Error);
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(f.Masks.Output(f.Group).Settled && f.Masks.Output(f.Group).Completed.Count == 2, "The restored terrain output completes");
        Assert(f.Engine.Apply(Doc(GroupMod("later", Near))).Outcomes["later"].Applied && Close(f.At(f.Group), Near) && f.Terrain.Store.Count == 2, "Completed terrain output lifts the guard: " + f.Where());
    }

    // A move that the world load refused, such as a hand-written fixed move of a station that the group world refuses
    // to place, shows as refused in scene patch editing: not as a change, with the reason on the Changes tab, in
    // the outliner marker, the state badge and the row tooltip. The Problems filter stays for repairable defects of the
    // object. An edit that replaces the refused modification ends the mark.
    static void RefusedLoadMoveShowsAsRefused() {
        var f = new Stop(); f.World.PlacementRefused[f.Group] = "The station can't be placed in this test.";
        var text = Doc(GroupMod("move-station", Far));
        var load = f.Engine.Apply(text, "author/pack").Outcomes["move-station"];
        Assert(!load.Applied && load.Failure == FailureKind.Refused, "The world load refuses the move: " + load.Error);
        var store = new Store { Patch = text }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var refusal = session.Refusal(StationId);
        Assert(refusal != null && refusal.Contains("can't be placed in this test") && session.IsChanged(StationId), "The session knows the refused modification and its reason: " + refusal);
        Assert(session.Changes(StationId).SequenceEqual(new[] { "Refused. " + refusal.TrimEnd('.') + "." }), "The Changes tab marks it as refused: " + String.Join(" | ", session.Changes(StationId)));
        // What the outliner, state line and tooltip show: only an object this mod changes can show a failure.
        Assert(CommandBand.ShownFailure(true, FailureKind.Refused) == FailureKind.Refused && CommandBand.ShownFailure(false, FailureKind.Refused) == null && CommandBand.ShownFailure(true, null) == null, "Only a changed object shows a failure");
        Assert(CommandBand.RowMarkers(false, true, false, FailureKind.Refused) == "×" && CommandBand.RowMarkers(true, true, false, FailureKind.Restored) == "!×" && CommandBand.RowMarkers(false, true, false) == "*", "The outliner marks a modification that is not applied with its own marker, not *");
        var badges = CommandBand.Badges(true, false, FailureKind.Refused);
        Assert(badges.Count == 1 && ReferenceEquals(badges[0], CommandBand.Refused) && badges[0].Text == "× REFUSED" && badges[0].Color == "#FF8080", "The state line shows the refused badge instead of the changed one");
        Assert(ReferenceEquals(CommandBand.Badges(true, false, FailureKind.Restored).Single(), CommandBand.NotApplied) && CommandBand.NotApplied.Text == "× NOT APPLIED", "A failed and restored modification shows as not applied");
        Assert(ReferenceEquals(CommandBand.Badges(true, false).Single(), CommandBand.Changed), "A modification that applied stays changed");
        Assert(CommandBand.FailureTooltipLine(FailureKind.Refused) == "This mod's modification of this object is refused. The Changes tab gives the reason."
            && CommandBand.FailureTooltipLine(FailureKind.Restored) == "This mod's modification of this object is not applied. The Changes tab gives the reason.", "The row tooltip lines");
        Assert(CommandBand.FailureLabel(FailureKind.Refused) == "Refused" && CommandBand.FailureLabel(FailureKind.Restored) == "Failed and restored" && CommandBand.FailureLabel(FailureKind.DependencyFailed) == "Not applied because a dependency failed",
            "The Changes tab labels by failure kind");
        Assert(!CommandBand.ShowsAsNotApplied(FailureKind.IncompleteCleanup) && CommandBand.ShowsAsNotApplied(FailureKind.Restored) && CommandBand.ShowsAsNotApplied(FailureKind.Refused), "An incomplete cleanup keeps its own presentation");
        // A session whose own key differs, another mod's file, does not take that refusal.
        var other = new PatchEditingSession(f.Engine, new NativeMapEditor(), new Store { Patch = text }, scenePatchFileKey: "other/pack");
        Assert(other.Refusal(StationId) == null, "Another scene patch file key has no refusal");
        // Remove modification ends the mark; Undo brings the refused modification back, refused, without an incomplete step.
        Assert(session.Remove(StationId).Applied && session.Refusal(StationId) == null && !session.IsChanged(StationId), "After Remove modification nothing is refused or changed");
        mapEditor.Undo();
        Assert(session.IncompleteOperation == null && session.Refusal(StationId) != null && Close(f.At(f.Group), Authored), "Undo brings the refused move back as refused: " + session.IncompleteOperation);
    }

    // A part with a fixed placement keeps it when its group moves, and so does the mask it holds: nothing is rebuilt.
    static void HeldMaskPartStaysWhenItsGroupMoves() {
        var f = new Stop(onPart: true);
        var held = new[] { 1150.0, 10, 60 };
        var result = f.Engine.Apply(Doc(PartMod("hold-foundation", Foundation, held), GroupMod("move-station", Near)));
        Assert(result.Outcomes.Values.All(o => o.Applied), "The part hold and the group move apply: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        Assert(Close(f.At(f.Group), Near) && Close(f.At(f.Slab), held) && f.RegisteredOnceAt(held, 30), "The held part keeps its mask where it is: " + f.Where());
    }

    // A mask that flattens the terrain height moves with its holder like any other mask: a move of the group, of the
    // part that holds it or of a part above it applies, the mask's centre and rotation follow, and the terrain output
    // at the old and the new place is tracked under the edited object. Enabling and disabling stay as they were.
    static void FlatteningMaskMovesWithItsHolder() {
        var f = new Stop(flattens: true);
        var outcome = f.Engine.Apply(Doc(GroupMod("move-station", Far, yaw: 40))).Outcomes["move-station"];
        Assert(outcome.Applied && Close(f.At(f.Group), Far) && f.RegisteredOnceAt(Far, 70), "A group move carries the flattening mask, centre and rotation: " + outcome.Error + " " + f.Where());
        var output = f.Masks.Output(f.Group);
        Assert(output.Outstanding.Count == 2 && output.Outstanding.Any(l => l.Contains("x=1100,")) && output.Outstanding.Any(l => l.Contains("x=1700,")),
            "The terrain at the old and the new place is tracked: " + String.Join("; ", output.Outstanding));
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(f.Masks.Output(f.Group).Settled && f.Masks.Output(f.Group).Completed.Count == 2, "Both terrain writes complete");
        var part = new Stop(onPart: true, flattens: true);
        outcome = part.Engine.Apply(Doc(PartMod("move-foundation", Foundation, new[] { 1150.0, 10, 60 }, yaw: 90))).Outcomes["move-foundation"];
        Assert(outcome.Applied && part.RegisteredOnceAt(new[] { 1150.0, 10, 60 }, 120) && part.Masks.Output(part.Slab).Outstanding.Count == 2,
            "A move of the part that holds the flattening mask carries it: " + outcome.Error + " " + part.Where());
        var hide = new Stop(flattens: true);
        Assert(hide.Engine.Apply(Doc(GroupMod("hide-station", active: false))).Outcomes["hide-station"].Applied && hide.Terrain.Store.Count == 0, "Disabling the station releases the flattening mask");
    }

    // A flattening mask sets the terrain height to its holder's height, so a move that changes only the height rebuilds
    // the mask at the new height and tracks the terrain output at its place, as a move on the ground does. A mask that
    // does not flatten registers at the new height only on its next rebuild, since the height changes none of its output.
    static void HeightOnlyMoveRebuildsTheFlatteningMask() {
        var f = new Stop(flattens: true);
        var raised = new[] { 1100.0, 74, 20 };
        var rebuilds = f.Terrain.Rebuilds;
        var outcome = f.Engine.Apply(Doc(GroupMod("raise-station", raised))).Outcomes["raise-station"];
        Assert(outcome.Applied && Close(f.At(f.Group), raised), "The height-only move applies: " + outcome.Error);
        Assert(f.Terrain.Rebuilds == rebuilds + 1 && f.RegisteredOnceAt(raised, 30), "The flattening mask is rebuilt at the new height: " + f.Where());
        var output = f.Masks.Output(f.Group);
        Assert(output.Outstanding.Count == 1 && output.Outstanding.Single() == "terrain output at map x=1100, z=20", "The terrain output at the mask's place is tracked: " + String.Join("; ", output.Outstanding));
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(f.Masks.Output(f.Group).Settled && f.Masks.Output(f.Group).Completed.Count == 1, "The terrain write completes");
        var lowered = new[] { 1100.0, 9.95, 20 };
        Assert(new PatchEngine(Scene(f)).Apply(Doc(GroupMod("raise-station", lowered))).Outcomes["raise-station"].Applied && f.RegisteredOnceAt(lowered, 30) && f.Terrain.Rebuilds == rebuilds + 2,
            "A move back down rebuilds it again: " + f.Where());
        var plain = new Stop(); rebuilds = plain.Terrain.Rebuilds;
        Assert(plain.Engine.Apply(Doc(GroupMod("raise-station", raised))).Outcomes["raise-station"].Applied && plain.Terrain.Rebuilds == rebuilds && plain.Masks.Output(plain.Group).Outstanding.Count == 0,
            "A mask that does not flatten is not rebuilt for a height change: " + plain.Where());
    }

    // A flattening curve mask registers at the centre of its bounds, which its holder's pose places: a curve reaching
    // ahead and bending up holds that centre away from the holder's height, and a pitch or a roll moves it. Its
    // registration is compared with the one the holder's current pose would make, never with the holder's height, so a
    // move, a height change, a pitch and a run of small steps each follow, and a curve that does not follow is refused.
    static void CurveMaskFollowsItsHolderPose() {
        var offset = new[] { 0.0, 3, 10 };
        double[] Expected(double[] at, double[] rotation) { var t = Turn.Rotate(rotation, offset); return new[] { at[0] + t[0], at[1] + t[1], at[2] + t[2] }; }
        double[] Registered(Stop stop) => stop.Terrain.Store[stop.Mask.Keys.Single()].Centre.ToArray();
        var f = new Stop(flattens: true, curveOffset: offset);
        var outcome = f.Engine.Apply(Doc(GroupMod("move-station", Far, yaw: 40))).Outcomes["move-station"];
        Assert(outcome.Applied && Close(Registered(f), Expected(Far, Turn.Yaw(40))), "A move of a curve mask's holder applies and the curve follows: " + outcome.Error + " " + f.Where());
        f.Terrain.BuildTiles(); f.Masks.Poll();
        var raised = new[] { Far[0], Far[1] + 20, Far[2] };
        var rebuilds = f.Terrain.Rebuilds;
        outcome = new PatchEngine(Scene(f)).Apply(Doc(GroupMod("move-station", raised, yaw: 40))).Outcomes["move-station"];
        Assert(outcome.Applied && f.Terrain.Rebuilds == rebuilds + 1 && Close(Registered(f), Expected(raised, Turn.Yaw(40))), "A height-only move rebuilds the curve at its new height: " + outcome.Error + " " + f.Where());
        Assert(f.Masks.Output(f.Group).Outstanding.Count == 1, "The terrain output at its place is tracked: " + String.Join("; ", f.Masks.Output(f.Group).Outstanding));
        f.Terrain.BuildTiles(); f.Masks.Poll();
        // A pitch of the holder raises the curve's centre without raising the holder.
        var pitch = Turn.Multiply(Turn.Yaw(40), new[] { Math.Sin(5 * Math.PI / 180), 0, 0, Math.Cos(5 * Math.PI / 180) });
        rebuilds = f.Terrain.Rebuilds;
        outcome = new PatchEngine(Scene(f)).Apply(Doc(GroupMod("move-station", raised, rotation: pitch))).Outcomes["move-station"];
        Assert(outcome.Applied && f.Terrain.Rebuilds == rebuilds + 1 && Close(Registered(f), Expected(raised, pitch)), "A pitch of the holder applies and the curve follows: " + outcome.Error + " " + f.Where());
        f.Terrain.BuildTiles(); f.Masks.Poll();
        // Steps of 0.06 m each stay under the tolerance, but their sum does not: the curve is rebuilt once it lags.
        rebuilds = f.Terrain.Rebuilds;
        var at = raised.ToArray();
        for (var step = 1; step <= 4; step++) {
            at = new[] { at[0], at[1] + 0.06, at[2] };
            outcome = new PatchEngine(Scene(f)).Apply(Doc(GroupMod("move-station", at, rotation: pitch))).Outcomes["move-station"];
            Assert(outcome.Applied && Math.Abs(Registered(f)[1] - Expected(at, pitch)[1]) <= 0.1, "Step " + step + " keeps the curve within the tolerance of its holder's pose: " + outcome.Error + " " + f.Where());
            f.Terrain.BuildTiles(); f.Masks.Poll();
        }
        Assert(f.Terrain.Rebuilds > rebuilds, "The small steps rebuild the curve once they add up");
        Assert(new PatchEngine(Scene(f)).Apply(Doc(GroupMod("move-station", at, rotation: pitch))).Outcomes["move-station"].Applied && f.Masks.Output(f.Group).Settled, "A repeated application starts no terrain output");
        // A curve whose rebuild leaves its height behind does not follow: the edit is refused and restored.
        var stuck = new Stop(flattens: true, curveOffset: offset);
        stuck.Mask.StuckHeight = Registered(stuck)[1];
        var refused = stuck.Engine.Apply(Doc(GroupMod("raise-station", new[] { Authored[0], Authored[1] + 5, Authored[2] }))).Outcomes["raise-station"];
        Assert(!refused.Applied && refused.Error.Contains("The ground mask did not follow: wrong height for Rhodo Station") && Close(stuck.At(stuck.Group), Authored),
            "A curve that does not follow is refused and the station stays: " + refused.Error);
    }

    // Disabling the group releases the mask through the component's own lifecycle; enabling registers it once again.
    // The terrain at the mask's place is tracked both times.
    static void ActivityReleasesAndRegistersTheMask() {
        var f = new Stop();
        Assert(f.Engine.Apply(Doc(GroupMod("hide", active: false))).Outcomes["hide"].Applied && f.Terrain.Store.Count == 0 && f.Mask.Keys.Count == 0, "A disabled station holds no registration: " + f.Where());
        Assert(f.Masks.Output(f.Group).Outstanding.Count == 1, "The terrain where the mask was is outstanding");
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(new PatchEngine(Scene(f)).Apply(Doc(GroupMod("hide", Near, false))).Outcomes["hide"].Applied && f.Terrain.Store.Count == 0 && f.Masks.Output(f.Group).Settled, "A disabled station moves without registering and without terrain output");
        Assert(new PatchEngine(Scene(f)).Apply(Doc(GroupMod("show", active: true))).Outcomes["show"].Applied && f.RegisteredOnceAt(Near, 30), "Enabling registers the mask once where the station stands now: " + f.Where());
    }
    static SceneCapabilities Scene(Stop f) {
        var holds = new PartHolds(); var scene = new SceneCapabilities(() => f.World.Maps, null);
        scene.Add(new SceneObjects(f.World, holds: holds, masks: f.Masks)); scene.Add(new ChildObjects(f.World, holds, masks: f.Masks));
        return scene;
    }

    // A failure after the group moved restores the placement and the mask's one registration at its old place. A
    // rebuild that failed changed no registration, so no terrain output is outstanding and the next edit applies. A
    // restoration that cannot rebuild the mask is an incomplete cleanup, which blocks the retry.
    // flattens: the mask also flattens the terrain height, which takes the same route.
    static void FailureAfterMutationRestoresTheMask(bool flattens) {
        var f = new Stop(flattens: flattens);
        f.Terrain.FailRebuildCount = 1;
        var outcome = f.Engine.Apply(Doc(GroupMod("move-station", Far, yaw: 40))).Outcomes["move-station"];
        Assert(!outcome.Applied && outcome.Error.Contains("back as it was") && Close(f.At(f.Group), Authored) && f.RegisteredOnceAt(Authored, 30), "The placement and the registration are restored: " + outcome.Error + " " + f.Where());
        Assert(f.Masks.Output(f.Group).Settled && f.Terrain.Store.Count == 1, "The failed rebuild left no terrain output outstanding and no second registration");
        Assert(f.Engine.Apply(Doc(GroupMod("later", Far))).Outcomes["later"].Applied && f.RegisteredOnceAt(Far, 30), "The next edit applies: " + f.Where());
        // Two masks: the first follows the move, the second cannot be rebuilt, and neither can the first on the way back.
        var stuck = new Stop(flattens: flattens);
        stuck.Terrain.Add(new FakeMask { Id = "second", Holder = "Foundation", Group = stuck.Group, On = stuck.Slab });
        stuck.Terrain.FailAfterRebuilds = stuck.Terrain.Rebuilds + 1;
        outcome = stuck.Engine.Apply(Doc(GroupMod("move-station", Far))).Outcomes["move-station"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "A restoration that cannot rebuild a mask is reported as incomplete cleanup: " + outcome.Error);
        stuck.Terrain.FailAfterRebuilds = -1; var mutations = stuck.World.Mutations;
        var retry = stuck.Engine.Apply(Doc(GroupMod("again", Near))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && stuck.World.Mutations == mutations, "The retry is blocked until cleanup is verified: " + retry.Error);
        var part = new Stop(onPart: true, flattens: flattens); part.Terrain.FailRebuildCount = 1;
        outcome = part.Engine.Apply(Doc(PartMod("move-foundation", Foundation, Far))).Outcomes["move-foundation"];
        Assert(!outcome.Applied && outcome.Error.Contains("back as it was") && part.RegisteredOnceAt(new[] { 1100.0, 10, 24 }, 30), "A failed part move restores the part and its mask: " + outcome.Error + " " + part.Where());
    }

    // A terrain write that does not complete: the completed and the outstanding write stay identifiable, the edit is
    // kept, and a retry requests only the outstanding write again and is safe to repeat.
    static void TerrainWriteFailureIsIdentifiableAndRetryable() {
        var f = new Stop(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        Assert(session.Edit(StationId, new Placement(Far, Upright), null).Applied, "The station move applies");
        // The old place's tile is rebuilt; the invalidation of the new place's tile is lost before the game builds it.
        f.Terrain.PendingTiles.Remove("3,0"); f.Terrain.BuildTiles(); f.Masks.Poll();
        f.Terrain.Clock = TerrainOutput.Timeout + 1; f.Masks.Poll();
        var output = session.Output(StationId);
        Assert(output.Error != null && output.Completed.Count == 1 && output.Outstanding.Count == 1 && !output.Settled, "The failed write names its completed and outstanding writes: " + output.Error);
        Assert(output.Completed[0].Contains("x=1100,") && output.Outstanding[0].Contains("x=1700,"), "Each write names its place: " + output.Completed[0] + " | " + output.Outstanding[0]);
        Assert(f.Masks.Failures.Count() == 1 && Close(f.At(f.Group), Far) && f.RegisteredOnceAt(Far, 30), "The edit and its registration are kept, and the failure is identifiable");
        Assert(mapEditor.Save() == null && !session.Output(StationId).Settled, "Save does not report the terrain output complete");
        Assert(session.RetryOutput(StationId).Applied && session.RetryOutput(StationId).Applied && f.Terrain.Invalidated.SequenceEqual(new[] { "3,0", "3,0" }) && session.Output(StationId).Error == null && !session.Output(StationId).Settled && f.RegisteredOnceAt(Far, 30),
            "A retry requests only the outstanding write again, and repeating it changes nothing else: " + String.Join(",", f.Terrain.Invalidated));
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(session.Output(StationId).Settled && session.Output(StationId).Completed.Count == 2 && !f.Masks.Failures.Any() && !session.RetryOutput(StationId).Applied, "The retried write completes; nothing is left to retry");
    }

    // In scene patch editing: Undo and Redo of a station move with its mask and matching dirty state, Remove
    // modification, Save at format version 1 with the patcher dependency, and a reload that reproduces the move with
    // one registration.
    // flattens: the mask also flattens the terrain height, which takes the same route.
    static void EditingHistorySaveAndReload(bool flattens) {
        var f = new Stop(flattens: flattens); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Assert(session.Edit(StationId, new Placement(Far, Turn.Yaw(40)), null).Applied && f.RegisteredOnceAt(Far, 70), "The station move carries its mask: " + f.Where());
        Persistence(true, all, "After the move");
        mapEditor.Undo(); Assert(Close(f.At(f.Group), Authored) && f.RegisteredOnceAt(Authored, 30), "Undo returns the station and its mask: " + f.Where());
        Persistence(false, new string[0], "After Undo");
        mapEditor.Redo(); Assert(Close(f.At(f.Group), Far) && f.RegisteredOnceAt(Far, 70), "Redo reapplies the move and the mask: " + f.Where());
        Persistence(true, all, "After Redo");
        Assert(session.Output(StationId).Outstanding.Count == 2, "The history steps track the terrain at both places");
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(session.Output(StationId).Settled, "The terrain output of the history steps completes");
        var removed = session.Remove(StationId);
        Assert(removed.Applied && Close(f.At(f.Group), Authored) && f.RegisteredOnceAt(Authored, 30), "Remove modification returns the underlying placement and mask: " + removed.Error + " " + f.Where());
        mapEditor.Undo(); Assert(f.RegisteredOnceAt(Far, 70), "Undo of the removal moves the station and its mask again: " + f.Where());
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0") && (int)JObject.Parse(store.Patch)["formatVersion"] == 1, "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        var fresh = new Stop(flattens: flattens);
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Count == 1 && reload.Outcomes.Values.All(o => o.Applied) && fresh.RegisteredOnceAt(Far, 70), "A reloaded world reproduces the move with one registration: " + fresh.Where());
        Assert(fresh.Engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && fresh.RegisteredOnceAt(Far, 70), "Applying the saved file again adds no registration: " + fresh.Where());
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = new Stop(flattens: flattens); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(other.Engine, strictEditor, strict); strictEditor.Session = kept;
        Assert(kept.Edit(StationId, new Placement(Far, Upright), null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=0.5.0"), "A stricter existing constraint is retained: " + strict.Info);
    }

    // The recorded Railroader 0.6.3 scene (level5): a flag-stop station is an empty root with the station building as a
    // prop instance, a lamp, the Mask object with the rectangle mask, and two station signs.
    static Node SmallStation(string name) => new Node(name,
        new Node("Station").With("Helpers.PropInstance"),
        new Node("Spotlight Outdoor (3)").With("UnityEngine.Light", "Effects.NightLightController").Solid(),
        new Node("Mask").With(RectangleMask),
        new Node("Sign-Station Synchronized", new Node("Text").With("TMPro.TextMeshPro").Solid(), new Node("Sign-Station").Solid()).With("Helpers.TextSynchronizer"),
        new Node("Sign-Station Synchronized (1)", new Node("Text").With("TMPro.TextMeshPro").Solid(), new Node("Sign-Station").Solid()).With("Helpers.TextSynchronizer"));
    const string RectangleMask = "Map.Runtime.MaskComponents.RectangleMapMask";
    static Node Depot(string name) => new Node(name, new Node("Southern Railway Combination Depot Heath Springs, SC (1)", new Node("Wall").Solid()), new Node("Mask").With(RectangleMask));
    static readonly string[][] Stations = {
        new[] { "Alarka Jct", "Alarka Jct Station" }, new[] { "Cochran", "Cochran flagstopstation" }, new[] { "Ela", "flagstopstation" }, new[] { "Nantahala", "Nantahala Station" },
        new[] { "Rhodo", "Rhodo Station" }, new[] { "Topton", "Topton Station" }, new[] { "Wilmot", "Wilmot Station" } };
    static readonly string[][] Depots = { new[] { "Alarka", "Alarka Depot" }, new[] { "Almond", "Almond Depot" }, new[] { "Sylva", "Sylva Depot" }, new[] { "Whittier", "Southern Combination Depot" } };
    // The stations and depots are listed groups under Station building, with their parts; the object that holds the
    // mask is a part although it has no renderer, collider or light. Hemingway's track marker stands on its root, so
    // the object keeps its marker family.
    static void InventoryListsTheStationsAndDepotsWithTheirParts() {
        var hemingway = SmallStation("Hemingway flagstopstation").With("Track.TrackMarker", "Track.TrackRelativePosition").Family();
        var towns = Stations.Select(s => new Node(s[0], SmallStation(s[1]))).Concat(Depots.Select(d => new Node(d[0], Depot(d[1])))).Concat(new[] { new Node("Hemingway", hemingway) }).ToArray();
        var verdicts = SceneObjectRules.Classify(new Node("World", new Node("Large Scenery", towns)));
        var parts = SceneObjectRules.Parts(verdicts).ToDictionary(p => p.Path, p => p);
        foreach (var item in Stations.Concat(Depots)) {
            var path = "World/Large Scenery/" + item[0] + "/" + item[1];
            var verdict = verdicts.SingleOrDefault(v => v.Path == path);
            Assert(verdict?.Disposition == GroupDisposition.Group && verdict.Category == SceneObjectCategories.StationBuilding, path + " is a listed group under Station building: " + verdict?.Disposition + " " + verdict?.Reason);
            Assert(parts[path + "/Mask"].Disposition == PartDisposition.Part, "The object that holds the ground mask is a part of " + path);
        }
        var rhodo = "World/Large Scenery/Rhodo/Rhodo Station/";
        var listed = parts.Values.Where(p => p.Disposition == PartDisposition.Part && p.Path.StartsWith(rhodo, StringComparison.Ordinal)).Select(p => p.Path.Substring(rhodo.Length)).ToArray();
        Assert(listed.SequenceEqual(new[] { "Spotlight Outdoor (3)", "Mask", "Sign-Station Synchronized", "Sign-Station Synchronized/Text", "Sign-Station Synchronized/Sign-Station", "Sign-Station Synchronized (1)", "Sign-Station Synchronized (1)/Text", "Sign-Station Synchronized (1)/Sign-Station" }),
            "The parts of a flag-stop station: " + String.Join(", ", listed));
        Assert(parts[rhodo + "Station"].Disposition == PartDisposition.NotListed && parts[rhodo + "Station"].Reason == SceneObjectRules.NoPhysicalPresence, "The prop instance has no saved renderer and is no part");
        var marker = verdicts.SingleOrDefault(v => v.Path == "World/Large Scenery/Hemingway/Hemingway flagstopstation");
        Assert(marker?.Disposition == GroupDisposition.ExistingFamily, "Hemingway, whose track marker stands on its root, keeps its marker family: " + marker?.Disposition);
        Assert(!SceneObjectRules.Exclusions.Any(e => e.Component.StartsWith(SceneObjectRules.GroundMasks, StringComparison.Ordinal)), "No exclusion names a scene ground mask");
        Assert(SceneObjectRules.HoldsGroundMask(new[] { "UnityEngine.Transform", "Map.Runtime.MaskComponents.CircleMapMask" }) && !SceneObjectRules.HoldsGroundMask(new[] { "UnityEngine.Light" }) && !SceneObjectRules.HoldsGroundMask(null), "Every component in Map.Runtime.MaskComponents is a scene ground mask");
    }

    // The Object tab names the part that holds the ground mask and says that moving it moves the terrain shaping; the
    // top scene object and a part above the mask name the part that holds it. The Changes tab shows terrain output for them.
    static void ObjectTabNamesTheMaskHolder() {
        Assert(CommandBand.GroundMaskDependency(true, new[] { "Mask" }, false) == "Ground mask: this part holds the scene ground mask. Moving this part moves the mask and its effects on the terrain. The patcher asks the game to rebuild terrain and vegetation at both positions.",
            "The holder's line: " + CommandBand.GroundMaskDependency(true, new[] { "Mask" }, false));
        Assert(CommandBand.GroundMaskDependency(false, new[] { "Mask" }, false) == "Ground mask: the part Mask holds the scene ground mask. Moving this part moves that part, its mask, and the terrain effects. The patcher asks the game to rebuild terrain and vegetation at both positions.",
            "The line of an object above the holder: " + CommandBand.GroundMaskDependency(false, new[] { "Mask" }, false));
        Assert(CommandBand.GroundMaskDependency(true, new[] { "Mask" }, true) == "Ground mask: this part holds the scene ground mask. The mask also flattens the ground. Moving this part moves the mask and its effects on the terrain. The patcher asks the game to rebuild terrain and vegetation at both positions.",
            "The holder of a flattening mask: " + CommandBand.GroundMaskDependency(true, new[] { "Mask" }, true));
        Assert(CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true) == "Ground mask: the part Mask holds the scene ground mask. The mask also flattens the ground. Moving this part moves that part, its mask, and the terrain effects. The patcher asks the game to rebuild terrain and vegetation at both positions.",
            "An object above a flattening mask: " + CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true));
        Assert(CommandBand.HoldsGroundMask(new[] { "Culling", CommandBand.GroundMaskDependency(false, new[] { "Mask" }, false) }) && !CommandBand.HoldsGroundMask(new[] { "Culling" }) && !CommandBand.HoldsGroundMask(null),
            "Whether an object has terrain output travels as its dependency line");
        // Use in the game: the holder says what its mask does, and the group above it is not only decorative.
        var holder = new[] { CommandBand.GroundMaskDependency(true, new[] { "Mask" }, false) }; var above = new[] { CommandBand.GroundMaskDependency(false, new[] { "Mask" }, false) };
        Assert(CommandBand.GroundMaskUse(CommandBand.PartDecorative, true, holder) == "Ground mask: it shapes the ground and the vegetation where this part stands. Moving or disabling this part changes the terrain there.",
            "The holder's use: " + CommandBand.GroundMaskUse(CommandBand.PartDecorative, true, holder));
        Assert(CommandBand.GroundMaskUse(CommandBand.Decorative, false, above) == "The building is decorative. The ground mask of its part Mask shapes the ground and the vegetation where it stands, so moving or disabling this group changes the terrain there.",
            "The group's use: " + CommandBand.GroundMaskUse(CommandBand.Decorative, false, above));
        Assert(CommandBand.GroundMaskUse(CommandBand.PartDecorative, true, new[] { "Culling" }) == CommandBand.PartDecorative, "A part without a mask keeps its text");
        // The part that holds the mask shows the terrain result of its group's edit, with the group named.
        Assert(CommandBand.GroupTerrain("Wilmot Station", CommandBand.Terrain(null, null, true, "16525.9, 2300.8", "", true), false) == "By the edit of its group Wilmot Station: Terrain updated: 16525.9, 2300.8.",
            "The holder names its group's result: " + CommandBand.GroupTerrain("Wilmot Station", CommandBand.Terrain(null, null, true, "16525.9, 2300.8", "", true), false));
        Assert(CommandBand.GroupTerrain("Wilmot Station", CommandBand.Terrain(null, null, false, "none", "16525.9, 2300.8", false), true).EndsWith("Retry is on the group's Changes tab.", StringComparison.Ordinal),
            "A pending or stopped write says where Retry is");
        // A flattening mask moves like any other, so its use names moving and disabling.
        Assert(CommandBand.GroundMaskUse(CommandBand.PartDecorative, true, new[] { CommandBand.GroundMaskDependency(true, new[] { "Mask" }, true) }) == "Ground mask: it shapes the ground and the vegetation where this part stands. Moving or disabling this part changes the terrain there.",
            "The use of a flattening holder names the move");
        Assert(CommandBand.GroundMaskUse(CommandBand.Decorative, false, new[] { CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true) }) == "The building is decorative. The ground mask of its part Mask shapes the ground and the vegetation where it stands, so moving or disabling this group changes the terrain there.",
            "The use of a group above a flattening mask names its holder and the move, as Wilmot Station's");
        // Several holders are named together; the top scene object's own mask stays "Its ground mask".
        Assert(CommandBand.GroundMaskUse(CommandBand.Decorative, false, new[] { CommandBand.GroundMaskDependency(false, new[] { "Mask", "Pad" }, false) }) == "The building is decorative. The ground masks of its parts Mask and Pad shape the ground and the vegetation where they stand, so moving or disabling this group changes the terrain there.",
            "Several holders: " + CommandBand.GroundMaskUse(CommandBand.Decorative, false, new[] { CommandBand.GroundMaskDependency(false, new[] { "Mask", "Pad" }, false) }));
        Assert(CommandBand.GroundMaskUse(CommandBand.Decorative, false, new[] { CommandBand.GroundMaskDependency(true, new[] { "Root" }, false, group: true) }).StartsWith("The building is decorative. Its ground mask shapes", StringComparison.Ordinal),
            "A mask on the top scene object is the group's own");
        // Below a marker object the holder is no part of a family: the line calls it an object.
        Assert(CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true, parts: false) == "Ground mask: the object Mask holds the scene ground mask. The mask also flattens the ground. Moving this object moves that object, its mask, and the terrain effects. The patcher asks the game to rebuild terrain and vegetation at both positions.",
            "The line of a marker object above the holder: " + CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true, parts: false));
        Assert(CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true, group: true) == "Ground mask: the part Mask holds the scene ground mask. The mask also flattens the ground. Moving this group moves that part, its mask, and the terrain effects. The patcher asks the game to rebuild terrain and vegetation at both positions.",
            "A group above a mask holder calls the inspected object a group");
        Assert(CommandBand.GroundMaskDependency(false, new[] { "Mask", "Pad" }, true, group: true) == "Ground mask: the parts Mask and Pad hold scene ground masks. At least one mask also flattens the ground. Moving this group moves those parts, their masks, and the terrain effects. The patcher asks the game to rebuild terrain and vegetation at both positions.",
            "A group above several mask holders names the group and all the masks");
        // An object of another family, such as a milepost or a spawn point, that holds the mask itself is no part.
        Assert(CommandBand.GroundMaskDependency(true, new[] { "Spawn" }, true, parts: false) == "Ground mask: this object holds the scene ground mask. The mask also flattens the ground. Moving this object moves the mask and its effects on the terrain. The patcher asks the game to rebuild terrain and vegetation at both positions.",
            "The line of an object of another family that holds the mask: " + CommandBand.GroundMaskDependency(true, new[] { "Spawn" }, true, parts: false));
    }

    // A flattening ground mask under an object of another family follows its holder when the patcher moves it: the
    // move applies, the registration stands at the holder's new place, the capability reports the terrain output at the
    // old and the new place, and a failed rebuild restores the holder and the registration. setup makes a fresh world
    // and returns the holder with the family's capability built with the masks; move is a whole scene patch file that
    // moves the holder.
    // holderName and offset: the mask stands on an object below the edited one, such as a board below a track marker.
    internal static void HeldMaskFollowsItsHolder(string family, Func<GroundMasks, (object Holder, IScene Scene)> setup, string move, string holderName = "Mask", double[] offset = null) {
        (object Holder, IScene Scene, HeldMaskTerrain Terrain, GroundMasks Masks) Fresh() {
            var terrain = new HeldMaskTerrain { HolderName = holderName, Offset = offset ?? new double[3] }; var masks = new GroundMasks(terrain);
            var (holder, scene) = setup(masks);
            terrain.Holder = holder; terrain.Read = () => scene.Read(holder);
            terrain.Masks(holder);
            return (holder, scene, terrain, masks);
        }
        var f = Fresh();
        var authored = f.Terrain.Registered.ToArray();
        var outcome = new PatchEngine(f.Scene).Apply(move).Outcomes.Values.Single();
        var now = f.Scene.Read(f.Holder).Placement.Position.Select((v, i) => v + (offset?[i] ?? 0)).ToArray();
        Assert(outcome.Applied && f.Terrain.Rebuilds == 1 && Close(f.Terrain.Registered, now) && !Close(now, authored), family + ": the move carries the flattening mask to the holder's new place: " + outcome.Error);
        var output = ((IDependentOutputScene)f.Scene).Output(f.Holder);
        Assert(output.Outstanding.Count == 2 && output.Error == null, family + ": the terrain output at the old and the new place is reported: " + String.Join("; ", output.Outstanding));
        f.Terrain.BuildTiles(new[] { authored, now }); f.Masks.Poll();
        Assert(((IDependentOutputScene)f.Scene).Output(f.Holder).Settled, family + ": the terrain output completes");
        var g = Fresh();
        var place = g.Scene.Read(g.Holder).Placement.Position.ToArray(); var registered = g.Terrain.Registered.ToArray();
        g.Terrain.FailRebuild = true;
        var failed = new PatchEngine(g.Scene).Apply(move).Outcomes.Values.Single();
        Assert(!failed.Applied && failed.Error.Contains("back as it was") && Close(g.Scene.Read(g.Holder).Placement.Position, place) && Close(g.Terrain.Registered, registered),
            family + ": a failed rebuild restores the holder and the registration: " + failed.Error);
    }

    // Hemingway: the track marker stands on the station's root, so the object stays in the Generic track marker family, and
    // that family handles the mask below it: a move rebuilds the mask, a flattening one included.
    static void MarkerRootGainsTheMaskHandling() {
        const string path = "World/Large Scenery/Hemingway/Hemingway flagstopstation";
        var world = new FakeMarkerWorld(); var marker = new FakeMarker { Id = "Mo0j", Segment = "S9sc", End = "a", Distance = 46.52 };
        world.Objects[path] = new List<object> { marker }; world.Load(marker); world.Relative(marker);
        var terrain = new MarkerTerrain(world, marker); var masks = new GroundMasks(terrain);
        var engine = new PatchEngine(new TrackMarkerScene(world, Capability.Unchecked, masks));
        string Move(string id, double x) => "\"" + id + "\":{\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(CultureInfo.InvariantCulture) + ",1,2],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var moved = engine.Apply(Doc(Move("move", 1160))).Outcomes["move"];
        Assert(moved.Applied && terrain.Registered.Count == 1 && Close(terrain.Registered[0], new[] { 1160.0, 1, 2 }) && terrain.Rebuilds == 1, "A marker move rebuilds the mask below it once: " + moved.Error + " " + String.Join(";", terrain.Registered.Select(N)));
        Assert(masks.Output(marker).Completed.Count + masks.Output(marker).Outstanding.Count == 2, "The terrain at both places is tracked under the marker");
        terrain.Flattens = true;
        var again = engine.Apply(Doc(Move("again", 1220))).Outcomes["again"];
        Assert(again.Applied && terrain.Registered.Count == 1 && Close(terrain.Registered[0], new[] { 1220.0, 1, 2 }) && terrain.Rebuilds == 2, "A flattening mask follows the marker's move: " + again.Error);
    }
    // Any generic track marker object that holds a ground mask, not only Hemingway's, moves it with the marker.
    static void GenericMarkerMovesItsMask() {
        const string path = "World/Signs/Sign-Whistle Masked";
        HeldMaskFollowsItsHolder("Generic track marker", masks => {
            var world = new FakeMarkerWorld(); var marker = new FakeMarker { Id = "Mw1x", Segment = "S9sc", End = "a", Distance = 46.52 };
            world.Objects[path] = new List<object> { marker }; world.Load(marker); world.Relative(marker);
            return (marker, new TrackMarkerScene(world, Capability.Unchecked, masks));
        }, Doc("\"move\":{\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"},\"placement\":{\"mode\":\"fixed\",\"position\":[1160,1,2],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}"));
    }
    // A board below a moved track marker, such as a Loc: location sign, moves along with the marker, and the marker's
    // family moves its mask once: one rebuild, one registration at the board's new place, terrain output at both places,
    // and a failed rebuild restored. The board family rebuilds a board's mask only when the board itself is the edited
    // target, so an edit handles each mask once.
    static void BoardBelowMovedMarkerMovesItsMask() {
        const string path = "World/Signs/Sign-Whistle Masked";
        HeldMaskFollowsItsHolder("Board below a track marker", masks => {
            var world = new FakeMarkerWorld(); var marker = new FakeMarker { Id = "Mw1x", Segment = "S9sc", End = "a", Distance = 46.52 };
            world.Objects[path] = new List<object> { marker }; world.Load(marker); world.Relative(marker);
            return (marker, new TrackMarkerScene(world, Capability.Unchecked, masks));
        }, Doc("\"move\":{\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"},\"placement\":{\"mode\":\"fixed\",\"position\":[1160,1,2],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}"),
            "Loc: Sylva Int", new[] { 3.0, 0, 0 });
        var board = new[] { "UnityEngine.Transform", "Helpers.Culling.RendererCuller", "Map.Runtime.MaskComponents.CircleMapMask", "Helpers.TextSynchronizer" };
        Assert(GroundMasks.MovedByEdit(false, board), "The mask of a board below the edited object moves with that edit");
        Assert(!GroundMasks.MovedByEdit(true, board), "A board that is the edited object itself keeps its mask with the board family");
        Assert(GroundMasks.MovedByEdit(true, new[] { "UnityEngine.Transform", "Track.TrackMarker", "Map.Runtime.MaskComponents.RectangleMapMask" }) && GroundMasks.MovedByEdit(false, new[] { "Map.Runtime.MaskComponents.RectangleMapMask" }),
            "Any other mask moves with the edit of its holder or of an object above it");
    }
    // A mask whose holder is a location sign, a renderer culler with a circle mask such as a Loc: location sign:
    // the board family moves it when the board is the edited target.
    static void BoardMaskStaysWithTheBoardFamily() {
        Assert(GroundMasks.HeldByBoard(new[] { "UnityEngine.Transform", "Helpers.Culling.RendererCuller", "Map.Runtime.MaskComponents.CircleMapMask", "Helpers.TextSynchronizer" }),
            "A Loc: location sign holds a board's mask");
        Assert(!GroundMasks.HeldByBoard(new[] { "UnityEngine.Transform", "Track.TrackMarker", "Map.Runtime.MaskComponents.CircleMapMask" }) && !GroundMasks.HeldByBoard(new[] { "Map.Runtime.MaskComponents.RectangleMapMask" })
            && !GroundMasks.HeldByBoard(new[] { "Helpers.Culling.RendererCuller", "Map.Runtime.MaskComponents.RectangleMapMask" }) && !GroundMasks.HeldByBoard(null)
            && !GroundMasks.HeldByBoard(new[] { "Track.TrackMarker", "Helpers.Culling.RendererCuller", "Map.Runtime.MaskComponents.CircleMapMask" }),
            "A marker object, a plain mask holder, a culler with another mask, or an object the board family refuses holds no board's mask");
    }
    // A new game prepares the runtime before the world load builds the ground masks; the marker capability built then
    // gains them once they exist, and handles the mask below Hemingway as one built with them.
    static void MarkerSceneBuiltBeforeTheMasksGainsThem() {
        const string path = "World/Large Scenery/Hemingway/Hemingway flagstopstation";
        var world = new FakeMarkerWorld(); var marker = new FakeMarker { Id = "Mo0j", Segment = "S9sc", End = "a", Distance = 46.52 };
        world.Objects[path] = new List<object> { marker }; world.Load(marker); world.Relative(marker);
        var terrain = new MarkerTerrain(world, marker) { Flattens = true }; var masks = new GroundMasks(terrain);
        var scene = new TrackMarkerScene(world, Capability.Unchecked);
        scene.UseMasks(masks);
        var second = new MarkerTerrain(world, marker);
        scene.UseMasks(new GroundMasks(second));
        string Move(string id, double x) => "\"" + id + "\":{\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"},\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(CultureInfo.InvariantCulture) + ",1,2],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var moved = new PatchEngine(scene).Apply(Doc(Move("move", 1160))).Outcomes["move"];
        Assert(moved.Applied && terrain.Rebuilds == 1 && second.Rebuilds == 0, "The masks given later rebuild the flattening mask on a move; a second set does not replace them: " + moved.Error);
    }
    // The one mask below a controlled marker: registered where the marker stood at the last rebuild.
    sealed class MarkerTerrain : IGroundMaskWorld {
        readonly FakeMarkerWorld world; readonly FakeMarker marker;
        public readonly List<double[]> Registered = new List<double[]>();
        public bool Flattens; public int Rebuilds;
        public MarkerTerrain(FakeMarkerWorld world, FakeMarker marker) { this.world = world; this.marker = marker; Registered.Add(world.Position(marker)); }
        public IReadOnlyList<GroundMaskState> Masks(object target) => !ReferenceEquals(target, marker) ? new GroundMaskState[0] : new[] {
            new GroundMaskState("mask", "Mask", true, Flattens, world.Position(marker), new[] { 0.0, 0, 1 }, 1, Registered.Select(r => new GroundMaskRegistration(r, new[] { 0.0, 0, 1 })), 0, world.Position(marker)) };
        public void Rebuild(object target, string id) { Rebuilds++; Registered.Clear(); Registered.Add(world.Position(marker)); }
        public bool TileLoaded(double[] position) => true;
        public void InvalidateTile(double[] position) { }
        public bool TerrainIdle => true;
        public double Now => 0;
        public event Action<double[], double[]> TileBuilt { add { } remove { } }
    }
}
