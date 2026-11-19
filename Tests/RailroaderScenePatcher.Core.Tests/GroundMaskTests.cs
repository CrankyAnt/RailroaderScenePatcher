// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// One scene ground mask of the controlled world: the object that holds it, the group root when On is null, the turn of
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
        for (var i = 0; i < mask.Modifiers; i++) { var key = "k" + nextKey++; Store[key] = new GroundMaskRegistration(at.Position, Axis(mask, at)); mask.Keys.Add(key); Invalidate(at.Position); }
        mask.Registered = true;
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
                mask.Keys.Where(Store.ContainsKey).Select(key => Store[key]), mask.Keys.Count(key => !Store.ContainsKey(key)));
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

// Baked buildings with their own scene ground masks: the flag-stop stations and depots of the base-game map join the
// Baked scenery group family, and a move of the object that holds a mask, or of its group, takes the mask's
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
        public Stop(bool onPart = false, bool flattens = false) {
            Terrain = new FakeMaskWorld(World); Masks = new GroundMasks(Terrain);
            Slab = Group.Add(new FakePart { Id = "Foundation#1", Offset = new[] { 0.0, 0, 4 } });
            Seat = Group.Add(new FakePart { Id = "Bench#2", Offset = new[] { 3.0, 0, 0 } });
            World.Objects[Station] = new List<object> { Group };
            World.PartsAt[Foundation] = new List<object> { Slab }; World.PartsAt[Bench] = new List<object> { Seat };
            World.SetActive(Group, true); World.Mutations = 0;
            Mask = Terrain.Add(new FakeMask { Id = "mask", Holder = onPart ? "Foundation" : "Rhodo Station", Group = Group, On = onPart ? Slab : null, Degrees = 30, Flattens = flattens });
            Terrain.BuildTiles();
            var holds = new PartHolds();
            var scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new BakedSceneryGroups(World, holds: holds, masks: Masks)); scene.Add(new BakedSceneryParts(World, holds, masks: Masks));
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
    static string Mod(string id, string path, string structure, double[] position = null, bool? active = null, double yaw = 0) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + N(position) + "],\"rotation\":[" + String.Join(",", Turn.Yaw(yaw).Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string GroupMod(string id, double[] position = null, bool? active = null, double yaw = 0) => Mod(id, Station, BakedSceneryGroups.Structure, position, active, yaw);
    static string PartMod(string id, string path, double[] position = null, bool? active = null, double yaw = 0) => Mod(id, path, BakedSceneryParts.Structure, position, active, yaw);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static readonly TargetIdentity StationId = new TargetIdentity(SceneName, Station, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
    static readonly TargetIdentity FoundationId = new TargetIdentity(SceneName, Foundation, BakedSceneryParts.ComponentType, BakedSceneryParts.Structure);

    public static void Run() {
        GroupMoveCarriesTheMaskOfItsRoot();
        PartMoveCarriesTheMaskItHolds();
        HeldMaskPartStaysWhenItsGroupMoves();
        MaskTheEditLeavesAloneIsNotJudged();
        SmallMovesDoNotLeaveTheMaskBehind();
        FailedSecondRebuildRestoresAndWaitsForTerrain();
        PlacementModesAreUnavailableForAFlatteningMask();
        FlatteningMaskRefusesAMove();
        RefusedLoadMoveShowsAsRefused();
        BlockedMoveControlsShowTheirReason();
        ActivityReleasesAndRegistersTheMask();
        FailureAfterMutationRestoresTheMask();
        TerrainWriteFailureIsIdentifiableAndRetryable();
        EditingHistorySaveAndReload();
        InventoryListsTheStationsAndDepotsWithTheirParts();
        ObjectTabNamesTheMaskHolder();
        MarkerRootGainsTheMaskHandling();
        Console.WriteLine("PASS: ground masks");
    }

    // The mask on the group root follows a move and a turn of the group: one registration, at the new centre, with
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
        Assert(output.Outstanding.Count == 2 && output.Error == null && output.Outstanding.Any(l => l.Contains("x=1100.0")) && output.Outstanding.Any(l => l.Contains("x=1700.0")), "The terrain where the first mask stood and where it went is outstanding: " + String.Join("; ", output.Outstanding));
        var again = f.Engine.Apply(Doc(GroupMod("again", Near))).Outcomes["again"];
        Assert(!again.Applied && again.Failure == FailureKind.AwaitingOutput && Close(f.At(f.Group), Authored), "The next edit waits for the restored terrain output: " + again.Failure + " " + again.Error);
        f.Terrain.BuildTiles(); f.Masks.Poll();
        Assert(f.Masks.Output(f.Group).Settled && f.Masks.Output(f.Group).Completed.Count == 2, "The restored terrain output completes");
        Assert(f.Engine.Apply(Doc(GroupMod("later", Near))).Outcomes["later"].Applied && Close(f.At(f.Group), Near) && f.Terrain.Store.Count == 2, "Completed terrain output lifts the guard: " + f.Where());
    }

    // Every move of an object with a flattening mask at or below it is refused, so Gizmo, Snap and Follow are
    // unavailable with the reason and Numeric keeps showing the placement with its fields unavailable.
    static void PlacementModesAreUnavailableForAFlatteningMask() {
        var flat = new[] { "Culling", CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true) }; var free = new[] { CommandBand.GroundMaskDependency(false, new[] { "Mask" }, false) };
        var reason = CommandBand.GroundMaskMoveBlock(flat);
        Assert(reason == "A ground mask of this object flattens the terrain height, and it is not confirmed that the game restores the height after a move.", "The reason: " + reason);
        Assert(CommandBand.GroundMaskMoveBlock(free) == null && CommandBand.GroundMaskMoveBlock(new[] { "Culling" }) == null && CommandBand.GroundMaskMoveBlock(null) == null, "Only a flattening mask blocks a move");
        foreach (var mode in new[] { CommandBand.GizmoMode, CommandBand.SnapMode, CommandBand.FollowMode })
            Assert(CommandBand.ModeBlock(mode, false, true, CommandBand.GroupOperations, moveBlock: reason) == reason && CommandBand.Tooltip("Drag", CommandBand.ModeBlock(mode, false, true, CommandBand.GroupOperations, moveBlock: reason)).StartsWith("Unavailable: A ground mask", StringComparison.Ordinal),
                "Mode " + mode + " is unavailable with the reason");
        Assert(CommandBand.ModeBlock(CommandBand.NumericMode, false, true, CommandBand.GroupOperations, moveBlock: reason) == null, "Numeric stays open and shows the placement");
        Assert(CommandBand.ModeBlock(CommandBand.GizmoMode, false, true, CommandBand.GroupOperations) == null, "An object without the block keeps its modes");
        // A mode that was open before the object was selected closes: the tab shows Numeric, as for a pad.
        Assert(CommandBand.ForcesNumeric(false, true, false, reason) && CommandBand.ForcesNumeric(true, true, false, null) && CommandBand.ForcesNumeric(false, false, false, null) && CommandBand.ForcesNumeric(false, true, true, null)
            && !CommandBand.ForcesNumeric(false, true, false, null), "A blocked move forces Numeric, as a pad, an object that cannot be edited and a controller do");
        // The tab's actions, Read and Apply included, take the same reason.
        Assert(CommandBand.PlacementBlock(null, null, null, reason) == reason && CommandBand.PlacementBlock("edit", "pad", "controller", reason) == "edit" && CommandBand.PlacementBlock(null, "pad", null, reason) == "pad"
            && CommandBand.PlacementBlock(null, null, "controller", reason) == "controller" && CommandBand.PlacementBlock(null, null, null, null) == null, "The move block is the reason of the placement actions when no earlier block holds");
    }

    // A move that the world load refused, such as a hand-written fixed move of a station whose ground mask flattens the
    // terrain height, shows as refused in scene patch editing: not as a change, with the reason on the Changes tab, in
    // the outliner marker, the state badge and the row tooltip. The Problems filter stays for repairable defects of the
    // object. An edit that replaces the refused modification ends the mark.
    static void RefusedLoadMoveShowsAsRefused() {
        var f = new Stop(flattens: true);
        var text = Doc(GroupMod("move-station", Far));
        var load = f.Engine.Apply(text, "author/pack").Outcomes["move-station"];
        Assert(!load.Applied && load.Failure == FailureKind.Refused, "The world load refuses the move: " + load.Error);
        var store = new Store { Patch = text }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store, scenePatchFileKey: "author/pack"); mapEditor.Session = session;
        var refusal = session.Refusal(StationId);
        Assert(refusal != null && refusal.Contains("flattens the terrain height") && session.IsChanged(StationId), "The session knows the refused modification and its reason: " + refusal);
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

    // An object whose move a flattening ground mask blocks shows Read and Apply inactive with the reason, the Numeric mode
    // and the position fields with "Unavailable: <reason>", and Supported edits names only what still applies.
    static void BlockedMoveControlsShowTheirReason() {
        var reason = CommandBand.FlatteningMoveBlock;
        var acts = CommandBand.NumericActionReasons(reason, null);
        Assert(acts.Apply == reason && acts.Read == reason, "Read and Apply are inactive with the reason");
        var open = CommandBand.NumericActionReasons(null, "Enter a number");
        Assert(open.Apply == "Enter a number" && open.Read == null, "Without a block Apply follows the fields and Read is available");
        Assert(CommandBand.ModeTip(CommandBand.NumericMode, "Enter a position.", null, reason) == "Unavailable: " + reason.TrimEnd('.') + ".", "The Numeric mode gives the reason of its fields: " + CommandBand.ModeTip(CommandBand.NumericMode, "Enter a position.", null, reason));
        Assert(CommandBand.ModeTip(CommandBand.NumericMode, "Enter a position.", null, null) == "Enter a position." && CommandBand.ModeTip(CommandBand.GizmoMode, "Drag.", reason, reason).StartsWith("Unavailable: ", StringComparison.Ordinal),
            "An open Numeric mode keeps its description; another mode gives its own block");
        Assert(CommandBand.BlockedValueTip(reason) == "Unavailable: " + reason.TrimEnd('.') + ".", "A blocked value row's tooltip starts with Unavailable: " + CommandBand.BlockedValueTip(reason));
        Assert(CommandBand.OperationsWithoutMove(CommandBand.GroupOperations) == "Enable or disable the whole group.", "A blocked group: " + CommandBand.OperationsWithoutMove(CommandBand.GroupOperations));
        Assert(CommandBand.OperationsWithoutMove(CommandBand.PartOperations) == "Enable or disable this part. The group and its other parts stay unchanged.", "A blocked part: " + CommandBand.OperationsWithoutMove(CommandBand.PartOperations));
        Assert(CommandBand.OperationsWithoutMove(CommandBand.MarkerOperations) == "Set, replace or detach the marker's track binding. Edit activity separately.", "A blocked marker: " + CommandBand.OperationsWithoutMove(CommandBand.MarkerOperations));
        Assert(CommandBand.OperationsWithoutMove("Something else.") == "Something else." && CommandBand.OperationsWithoutMove(null) == null, "Other texts stay");
    }

    // A part with a fixed placement keeps it when its group moves, and so does the mask it holds: nothing is rebuilt.
    static void HeldMaskPartStaysWhenItsGroupMoves() {
        var f = new Stop(onPart: true);
        var held = new[] { 1150.0, 10, 60 };
        var result = f.Engine.Apply(Doc(PartMod("hold-foundation", Foundation, held), GroupMod("move-station", Near)));
        Assert(result.Outcomes.Values.All(o => o.Applied), "The part hold and the group move apply: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        Assert(Close(f.At(f.Group), Near) && Close(f.At(f.Slab), held) && f.RegisteredOnceAt(held, 30), "The held part keeps its mask where it is: " + f.Where());
    }

    // A mask that flattens the terrain height: a move of its holder, of a part above it or of its group is refused
    // before any mutation, with the reason. Enabling and disabling stay possible.
    static void FlatteningMaskRefusesAMove() {
        var reason = GroundMasks.FlatteningRefusal("Mask");
        Assert(reason == "The ground mask on Mask flattens the terrain height. It is not confirmed that the game restores the height after a move, so this object cannot be moved", "The refusal names the holder and why: " + reason);
        var f = new Stop(flattens: true); var rebuilds = f.Terrain.Rebuilds;
        var outcome = f.Engine.Apply(Doc(GroupMod("move-station", Far))).Outcomes["move-station"];
        Assert(!outcome.Applied && outcome.Failure == FailureKind.Refused && outcome.Error.Contains("flattens the terrain height") && f.World.Mutations == 0 && f.Terrain.Rebuilds == rebuilds && f.RegisteredOnceAt(Authored, 30),
            "A group move is refused before mutation: " + outcome.Failure + " " + outcome.Error + " " + f.Where());
        Assert(f.Masks.Output(f.Group).Settled && f.Masks.Output(f.Group).Completed.Count == 0, "A refusal starts no terrain output");
        var part = new Stop(onPart: true, flattens: true);
        outcome = part.Engine.Apply(Doc(PartMod("move-foundation", Foundation, Far))).Outcomes["move-foundation"];
        Assert(!outcome.Applied && outcome.Failure == FailureKind.Refused && part.World.Mutations == 0 && part.RegisteredOnceAt(new[] { 1100.0, 10, 24 }, 30), "A move of the part that holds the mask is refused before mutation: " + outcome.Error);
        Assert(part.Engine.Apply(Doc(PartMod("move-bench", Bench, new[] { 1120.0, 10, 30 }))).Outcomes["move-bench"].Applied, "A part without the mask still moves");
        var hide = f.Engine.Apply(Doc(GroupMod("hide-station", active: false))).Outcomes["hide-station"];
        Assert(hide.Applied && !f.Group.Active && f.Terrain.Store.Count == 0, "Disabling the station still applies and releases the mask: " + hide.Error);
        var s = new Stop(flattens: true); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(s.Engine, mapEditor, store); mapEditor.Session = session;
        var before = session.ScenePatchFileText;
        var edit = session.Edit(StationId, new Placement(Far, Upright), null);
        Assert(!edit.Applied && edit.Error.Contains("flattens the terrain height") && s.World.Mutations == 0 && session.ScenePatchFileText == before && !mapEditor.IsDirty && !mapEditor.CanUndo,
            "In scene patch editing the refusal leaves the scene, the scene patch file and the history unchanged: " + edit.Error);
        Assert(session.Edit(StationId, null, false).Applied && s.Terrain.Store.Count == 0, "A disable of the flattening station applies in scene patch editing");
        mapEditor.Undo(); Assert(s.Group.Active && s.RegisteredOnceAt(Authored, 30), "Undo enables it with its mask registered once: " + s.Where());
        mapEditor.Redo();
        var removed = session.Remove(StationId);
        Assert(removed.Applied && s.Group.Active && s.RegisteredOnceAt(Authored, 30), "Remove modification returns the enabled station and its mask: " + removed.Error + " " + s.Where());
    }

#if SCENE_PATCHER_FAULTS
    // The fault build can lift the refusal, so that a Railroader journey can compare the terrain height before, after
    // and restoration; Disarm ends it.
    internal static void RunFlatteningSwitch() {
        var f = new Stop(flattens: true);
        FaultInjection.AllowFlatteningMoves(true);
        try {
            var outcome = f.Engine.Apply(Doc(GroupMod("move-station", Far))).Outcomes["move-station"];
            Assert(outcome.Applied && f.RegisteredOnceAt(Far, 30), "With the switch on, a flattening mask moves with its station: " + outcome.Error + " " + f.Where());
        } finally { FaultInjection.Disarm(); }
        Assert(!FaultInjection.FlatteningMovesAllowed && !f.Engine.Apply(Doc(GroupMod("again", Near))).Outcomes["again"].Applied, "Disarm restores the refusal");
    }
#endif

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
        scene.Add(new BakedSceneryGroups(f.World, holds: holds, masks: f.Masks)); scene.Add(new BakedSceneryParts(f.World, holds, masks: f.Masks));
        return scene;
    }

    // A failure after the group moved restores the placement and the mask's one registration at its old place. A
    // rebuild that failed changed no registration, so no terrain output is outstanding and the next edit applies. A
    // restoration that cannot rebuild the mask is an incomplete cleanup, which blocks the retry.
    static void FailureAfterMutationRestoresTheMask() {
        var f = new Stop();
        f.Terrain.FailRebuildCount = 1;
        var outcome = f.Engine.Apply(Doc(GroupMod("move-station", Far, yaw: 40))).Outcomes["move-station"];
        Assert(!outcome.Applied && outcome.Error.Contains("back as it was") && Close(f.At(f.Group), Authored) && f.RegisteredOnceAt(Authored, 30), "The placement and the registration are restored: " + outcome.Error + " " + f.Where());
        Assert(f.Masks.Output(f.Group).Settled && f.Terrain.Store.Count == 1, "The failed rebuild left no terrain output outstanding and no second registration");
        Assert(f.Engine.Apply(Doc(GroupMod("later", Far))).Outcomes["later"].Applied && f.RegisteredOnceAt(Far, 30), "The next edit applies: " + f.Where());
        // Two masks: the first follows the move, the second cannot be rebuilt, and neither can the first on the way back.
        var stuck = new Stop();
        stuck.Terrain.Add(new FakeMask { Id = "second", Holder = "Foundation", Group = stuck.Group, On = stuck.Slab });
        stuck.Terrain.FailAfterRebuilds = stuck.Terrain.Rebuilds + 1;
        outcome = stuck.Engine.Apply(Doc(GroupMod("move-station", Far))).Outcomes["move-station"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "A restoration that cannot rebuild a mask is reported as incomplete cleanup: " + outcome.Error);
        stuck.Terrain.FailAfterRebuilds = -1; var mutations = stuck.World.Mutations;
        var retry = stuck.Engine.Apply(Doc(GroupMod("again", Near))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && stuck.World.Mutations == mutations, "The retry is blocked until cleanup is verified: " + retry.Error);
        var part = new Stop(onPart: true); part.Terrain.FailRebuildCount = 1;
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
        Assert(output.Completed[0].Contains("x=1100.0") && output.Outstanding[0].Contains("x=1700.0"), "Each write names its place: " + output.Completed[0] + " | " + output.Outstanding[0]);
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
    static void EditingHistorySaveAndReload() {
        var f = new Stop(); var store = new Store(); var mapEditor = new NativeMapEditor();
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
        var fresh = new Stop();
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Count == 1 && reload.Outcomes.Values.All(o => o.Applied) && fresh.RegisteredOnceAt(Far, 70), "A reloaded world reproduces the move with one registration: " + fresh.Where());
        Assert(fresh.Engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && fresh.RegisteredOnceAt(Far, 70), "Applying the saved file again adds no registration: " + fresh.Where());
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = new Stop(); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(other.Engine, strictEditor, strict); strictEditor.Session = kept;
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
        var verdicts = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", towns)));
        var parts = BakedSceneryRules.Parts(verdicts).ToDictionary(p => p.Path, p => p);
        foreach (var item in Stations.Concat(Depots)) {
            var path = "World/Large Scenery/" + item[0] + "/" + item[1];
            var verdict = verdicts.SingleOrDefault(v => v.Path == path);
            Assert(verdict?.Disposition == GroupDisposition.Group && verdict.Category == BakedSceneryCategories.StationBuilding && verdict.Ticket == null, path + " is a listed group under Station building: " + verdict?.Disposition + " " + verdict?.Reason);
            Assert(parts[path + "/Mask"].Disposition == PartDisposition.Part, "The object that holds the ground mask is a part of " + path);
        }
        var rhodo = "World/Large Scenery/Rhodo/Rhodo Station/";
        var listed = parts.Values.Where(p => p.Disposition == PartDisposition.Part && p.Path.StartsWith(rhodo, StringComparison.Ordinal)).Select(p => p.Path.Substring(rhodo.Length)).ToArray();
        Assert(listed.SequenceEqual(new[] { "Spotlight Outdoor (3)", "Mask", "Sign-Station Synchronized", "Sign-Station Synchronized/Text", "Sign-Station Synchronized/Sign-Station", "Sign-Station Synchronized (1)", "Sign-Station Synchronized (1)/Text", "Sign-Station Synchronized (1)/Sign-Station" }),
            "The parts of a flag-stop station: " + String.Join(", ", listed));
        Assert(parts[rhodo + "Station"].Disposition == PartDisposition.NotListed && parts[rhodo + "Station"].Reason == BakedSceneryRules.NoPhysicalPresence, "The prop instance has no saved renderer and is no part");
        var marker = verdicts.SingleOrDefault(v => v.Path == "World/Large Scenery/Hemingway/Hemingway flagstopstation");
        Assert(marker?.Disposition == GroupDisposition.ExistingFamily, "Hemingway, whose track marker stands on its root, keeps its marker family: " + marker?.Disposition);
        Assert(!BakedSceneryRules.Exclusions.Any(e => e.Component.StartsWith(BakedSceneryRules.GroundMasks, StringComparison.Ordinal)), "No exclusion names a scene ground mask");
        Assert(BakedSceneryRules.HoldsGroundMask(new[] { "UnityEngine.Transform", "Map.Runtime.MaskComponents.CircleMapMask" }) && !BakedSceneryRules.HoldsGroundMask(new[] { "UnityEngine.Light" }) && !BakedSceneryRules.HoldsGroundMask(null), "Every component in Map.Runtime.MaskComponents is a scene ground mask");
    }

    // The Object tab names the part that holds the ground mask and says that moving it moves the terrain shaping; the
    // group root and a part above the mask name the part that holds it. The Changes tab shows terrain output for them.
    static void ObjectTabNamesTheMaskHolder() {
        Assert(CommandBand.GroundMaskDependency(true, new[] { "Mask" }, false) == "Ground mask: this part holds the scene ground mask. Moving it moves the terrain shaping, and the patcher asks the game to regenerate terrain and vegetation at the old and the new place",
            "The holder's line: " + CommandBand.GroundMaskDependency(true, new[] { "Mask" }, false));
        Assert(CommandBand.GroundMaskDependency(false, new[] { "Mask" }, false) == "Ground mask: the part Mask holds the scene ground mask. Moving this object moves that part and the terrain shaping with it, and the patcher asks the game to regenerate terrain and vegetation at the old and the new place",
            "The line of an object above the holder: " + CommandBand.GroundMaskDependency(false, new[] { "Mask" }, false));
        Assert(CommandBand.GroundMaskDependency(true, new[] { "Mask" }, true) == "Ground mask: this part holds the scene ground mask, which also flattens the terrain height. It cannot be moved, because it is not confirmed that the game restores the height after a move",
            "The holder of a flattening mask: " + CommandBand.GroundMaskDependency(true, new[] { "Mask" }, true));
        Assert(CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true) == "Ground mask: the part Mask holds the scene ground mask, which also flattens the terrain height. This object cannot be moved, because it is not confirmed that the game restores the height after a move",
            "An object above a flattening mask: " + CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true));
        Assert(CommandBand.HoldsGroundMask(new[] { "Culling", CommandBand.GroundMaskDependency(false, new[] { "Mask" }, false) }) && !CommandBand.HoldsGroundMask(new[] { "Culling" }) && !CommandBand.HoldsGroundMask(null),
            "Whether an object has terrain output travels as its dependency line");
        // Use in the game: the holder says what its mask does, and the group above it is not only decorative.
        var holder = new[] { CommandBand.GroundMaskDependency(true, new[] { "Mask" }, false) }; var above = new[] { CommandBand.GroundMaskDependency(false, new[] { "Mask" }, false) };
        Assert(CommandBand.GroundMaskUse(CommandBand.PartDecorative, true, holder) == "Ground mask: it shapes the ground and the vegetation where this part stands. Moving or disabling this part changes the terrain there.",
            "The holder's use: " + CommandBand.GroundMaskUse(CommandBand.PartDecorative, true, holder));
        Assert(CommandBand.GroundMaskUse(CommandBand.Decorative, false, above) == "The building is decorative. Its ground mask shapes the ground and the vegetation where it stands, so moving or disabling this group changes the terrain there.",
            "The group's use: " + CommandBand.GroundMaskUse(CommandBand.Decorative, false, above));
        Assert(CommandBand.GroundMaskUse(CommandBand.PartDecorative, true, new[] { "Culling" }) == CommandBand.PartDecorative, "A part without a mask keeps its text");
        // A flattening mask cannot be moved, so its use names only what an edit can do: disabling.
        Assert(CommandBand.GroundMaskUse(CommandBand.PartDecorative, true, new[] { CommandBand.GroundMaskDependency(true, new[] { "Mask" }, true) }) == "Ground mask: it shapes the ground and the vegetation where this part stands. Disabling this part changes the terrain there.",
            "The use of a flattening holder names no move");
        Assert(CommandBand.GroundMaskUse(CommandBand.Decorative, false, new[] { CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true) }) == "The building is decorative. Its ground mask shapes the ground and the vegetation where it stands, so disabling this group changes the terrain there.",
            "The use of a group above a flattening mask names no move");
        // Below a marker object the holder is no part of a family: the line calls it an object.
        Assert(CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true, parts: false) == "Ground mask: the object Mask holds the scene ground mask, which also flattens the terrain height. This object cannot be moved, because it is not confirmed that the game restores the height after a move",
            "The line of a marker object above the holder: " + CommandBand.GroundMaskDependency(false, new[] { "Mask" }, true, parts: false));
        // Only the marker object of a station building gains the mask handling in the Track marker family.
        Assert(BakedSceneryRules.MaskedMarker("World/Large Scenery/Hemingway/Hemingway flagstopstation") && !BakedSceneryRules.MaskedMarker("World/Signs/Loc: Sylva Int") && !BakedSceneryRules.MaskedMarker(null),
            "Hemingway is the marker object whose mask the Track marker family handles");
    }

    // Hemingway: the track marker stands on the station's root, so the object stays in the Track marker family, and
    // that family handles the mask below it: a move rebuilds the mask, and a flattening mask refuses the move.
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
        var refused = engine.Apply(Doc(Move("again", 1220))).Outcomes["again"];
        Assert(!refused.Applied && refused.Error.Contains("flattens the terrain height") && Close(world.Position(marker), new[] { 1160.0, 1, 2 }) && terrain.Rebuilds == 1, "A flattening mask refuses the marker's move before mutation: " + refused.Error);
    }
    // The one mask below a controlled marker: registered where the marker stood at the last rebuild.
    sealed class MarkerTerrain : IGroundMaskWorld {
        readonly FakeMarkerWorld world; readonly FakeMarker marker;
        public readonly List<double[]> Registered = new List<double[]>();
        public bool Flattens; public int Rebuilds;
        public MarkerTerrain(FakeMarkerWorld world, FakeMarker marker) { this.world = world; this.marker = marker; Registered.Add(world.Position(marker)); }
        public IReadOnlyList<GroundMaskState> Masks(object target) => !ReferenceEquals(target, marker) ? new GroundMaskState[0] : new[] {
            new GroundMaskState("mask", "Mask", true, Flattens, world.Position(marker), new[] { 0.0, 0, 1 }, 1, Registered.Select(r => new GroundMaskRegistration(r, new[] { 0.0, 0, 1 })), 0) };
        public void Rebuild(object target, string id) { Rebuilds++; Registered.Clear(); Registered.Add(world.Position(marker)); }
        public bool TileLoaded(double[] position) => true;
        public void InvalidateTile(double[] position) { }
        public bool TerrainIdle => true;
        public double Now => 0;
        public event Action<double[], double[]> TileBuilt { add { } remove { } }
    }
}
