// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// A controlled board world with the lifecycle the game's components have: enabling a board registers its
// culling sphere and circle-mask modifiers at the current pose, disabling releases them, moving the transform
// leaves both stale until the culling position is refreshed and the mask is rebuilt, and a mask change
// invalidates loaded terrain tiles at its old and new bounds, which then build asynchronously.
class FakeBoard {
    public string Scene = "BushnellWhittier-MapManager", Path = IndependentBoards.SylvaPath;
    public double[] Position = { 25692.9766, 633.4856, 441.8128 }, Rotation = { 0.0, 0.0, 0.0, 1.0 };
    public bool Active = true, ParentsActive = true, Culler = true, CullerEnabled = true, MaskEnabled = true, Flattens;
    public int Masks = 1;
    public string Unexpected;
    public object Parent = new object();
    public double[] Sphere;
    public readonly List<string> Keys = new List<string>();
}
class FakeBoardWorld : IBoardWorld {
    public const double Tile = 500;
    public string[] Maps = { "map", "layer" };
    public readonly List<FakeBoard> Boards = new List<FakeBoard>();
    public readonly Dictionary<string, double[]> MaskStore = new Dictionary<string, double[]>();
    public readonly HashSet<string> PendingTiles = new HashSet<string>(), UnloadedTiles = new HashSet<string>();
    public readonly List<string> Invalidated = new List<string>();
    public double Clock;
    public bool FailRebuild;
    public int FailRebuildCount, PoseWrites, Rebuilds, Finds;
    int nextKey;
    public IEnumerable<string> MapIds => Maps;
    public event Action<double[], double[]> TileBuilt;
    public FakeBoard Add(FakeBoard board = null) { board = board ?? new FakeBoard(); Boards.Add(board); if (board.Active && board.ParentsActive) Register(board); return board; }
    static string TileOf(double[] p) => Math.Floor(p[0] / Tile) + "," + Math.Floor(p[2] / Tile);
    bool Registered(FakeBoard b) => b.Active && b.ParentsActive;
    void Register(FakeBoard b) {
        if (b.Culler && b.CullerEnabled) b.Sphere = (double[])b.Position.Clone();
        if (b.MaskEnabled) for (var i = 0; i < b.Masks; i++) AddKey(b);
    }
    void AddKey(FakeBoard b) { var key = "k" + nextKey++; MaskStore[key] = (double[])b.Position.Clone(); b.Keys.Add(key); Invalidate(b.Position); }
    void RemoveKeys(FakeBoard b) { foreach (var key in b.Keys) { if (MaskStore.TryGetValue(key, out var at)) Invalidate(at); MaskStore.Remove(key); } b.Keys.Clear(); }
    void Invalidate(double[] at) { var tile = TileOf(at); if (!UnloadedTiles.Contains(tile)) PendingTiles.Add(tile); }
    public IReadOnlyList<object> Find(string scene, string path) { Finds++; return Find(Boards, scene, path); }
    static IReadOnlyList<object> Find(List<FakeBoard> Boards, string scene, string path) => Boards.Where(b => b.Scene == scene && b.Path == path && b.Culler).Cast<object>().ToList();
    public BoardFacts Facts(object board) {
        var b = (FakeBoard)board;
        return new BoardFacts(b.Scene, b.Path, true, b.ParentsActive, b.Culler, b.CullerEnabled, b.Masks, b.MaskEnabled, b.Flattens, b.Unexpected, b.Parent);
    }
    public double[] Position(object board) => (double[])((FakeBoard)board).Position.Clone();
    public double[] Rotation(object board) => (double[])((FakeBoard)board).Rotation.Clone();
    public bool Active(object board) => ((FakeBoard)board).Active;
    public void SetPose(object board, double[] position, double[] rotation) { var b = (FakeBoard)board; PoseWrites++; b.Position = (double[])position.Clone(); b.Rotation = (double[])rotation.Clone(); }
    public void SetActive(object board, bool active) {
        var b = (FakeBoard)board;
        if (b.Active == active) return;
        b.Active = active;
        if (!b.ParentsActive) return;
        if (active) Register(b); else { b.Sphere = null; RemoveKeys(b); }
    }
    public void RefreshCulling(object board) {
        var b = (FakeBoard)board;
        if (!Registered(b) || b.Sphere == null) throw new NullReferenceException("culling token");
        b.Sphere = (double[])b.Position.Clone();
    }
    public void RebuildMask(object board) {
        var b = (FakeBoard)board;
        if (FailRebuild || FailRebuildCount > 0) { FailRebuildCount--; throw new InvalidOperationException("mask injected"); }
        Rebuilds++;
        var count = b.Keys.Count; RemoveKeys(b);
        for (var i = 0; i < count; i++) AddKey(b);
    }
    public BoardRegistration Registration(object board) {
        var b = (FakeBoard)board;
        return new BoardRegistration(b.Sphere == null ? null : (double[])b.Sphere.Clone(), b.Keys.Where(MaskStore.ContainsKey).Select(k => (double[])MaskStore[k].Clone()), b.Keys.Count(k => !MaskStore.ContainsKey(k)));
    }
    public bool TileLoaded(double[] position) => !UnloadedTiles.Contains(TileOf(position));
    public void InvalidateTile(double[] position) { Invalidated.Add(TileOf(position)); Invalidate(position); }
    public bool TerrainIdle => PendingTiles.Count == 0;
    public double Now => Clock;
    public string Label(object board) => ((FakeBoard)board).Path;
    // The game builds each invalidated loaded tile and raises its bounds.
    public void BuildTiles(string only = null) {
        foreach (var tile in PendingTiles.Where(t => only == null || t == only).ToArray()) {
            var parts = tile.Split(',').Select(Double.Parse).ToArray();
            PendingTiles.Remove(tile);
            TileBuilt?.Invoke(new[] { parts[0] * Tile, 0, parts[1] * Tile }, new[] { (parts[0] + 1) * Tile, 0, (parts[1] + 1) * Tile });
        }
    }
    public int Registrations => MaskStore.Count;
}

static class BoardTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    const string Scene = "BushnellWhittier-MapManager";
    static readonly double[] Moved = { 25772.99, 633.4856, 393.795837 };
    // In the neighbouring terrain tile, so old and new positions need separate tile builds.
    static readonly double[] Far = { 26310.0, 640.0, 393.8 };
    static readonly double[] Turned = { 0.0, 0.13052619, 0.0, 0.99144486 };
    static string Num(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    static string Target(string path = IndependentBoards.SylvaPath, string structure = ",\"structure\":\"independent-board-v1\"") =>
        "\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + path + "\",\"component\":\"Helpers.Culling.RendererCuller\"" + structure + "}";
    static string Place(double[] p, double[] r = null) => "\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", p.Select(Num)) + "],\"rotation\":[" + String.Join(",", (r ?? Turned).Select(Num)) + "]}";
    static string Mod(string id, string body, string deps = "[]", string path = IndependentBoards.SylvaPath) => "{\"id\":\"" + id + "\"," + Target(path) + "," + body + ",\"dependsOn\":" + deps + "}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":[" + items + "]}";
    static bool Near(double[] a, double[] b, double tolerance = 0.001) => a != null && b != null && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= tolerance);
    // The registrations the native components hold agree with the board's own pose and activity.
    static bool Consistent(FakeBoardWorld world, FakeBoard board) {
        if (!board.Active) return board.Sphere == null && board.Keys.Count == 0;
        return Near(board.Sphere, board.Position) && board.Keys.Count == board.Masks && board.Keys.All(k => world.MaskStore.ContainsKey(k) && Near(world.MaskStore[k], board.Position));
    }
    static (FakeBoardWorld, FakeBoard, IndependentBoards, PatchEngine) Setup() {
        var world = new FakeBoardWorld(); var board = world.Add(); world.BuildTiles();
        var boards = new IndependentBoards(world); return (world, board, boards, new PatchEngine(boards));
    }
    internal static void Run() {
        PlacementMovesBoardAndItsRegistrations();
        ConformingBoardsQualifyWithoutTrackBinding();
        PreflightRefusalLeavesBoardUnchanged();
        TargetResolutionAndMapContext();
        RepeatAndOrderDoNotAccumulate();
        ActivityReleasesAndRestoresRegistrations();
        FailedMutationRestoresBoardAndRegistrations();
        UnverifiedRestorationGuardsRetry();
        TerrainOutputFollowsTheEdit();
        TerrainWriteFailureIsReportedAndRetryable();
        RestorationWaitsForTerrainOutput();
        AuthoredBoardFollowsHistoryAndRemoval();
        AuthoredTerrainFailureKeepsEditAndRetries();
        FailedAuthoredEditWaitsForRestoredOutput();
        SaveRaisesMinimumForPatternBoards();
        InspectedOutputResolvesOncePerInspection();
        ChangedObjectsAreReadFromTheDocument();
    }
    // The outliner marks every row this MapMod modifies. That reads the document alone and never searches the scene.
    static void ChangedObjectsAreReadFromTheDocument() {
        var (world, board, boards, session, editor, store) = Authoring();
        Assert(!session.IsChanged(Sylva), "An untouched object is not changed");
        Assert(session.Edit(Sylva, Pose(Moved), null).Applied, "The board is placed");
        var finds = world.Finds;
        for (var row = 0; row < 825; row++) Assert(session.IsChanged(Sylva) && !session.IsChanged(Whittier), "The placed board is changed, another board is not");
        Assert(session.IsChanged(new TargetIdentity(Scene, IndependentBoards.SylvaPath, IndependentBoards.CullerType)) && !session.IsChanged(new TargetIdentity(Scene, IndependentBoards.SylvaPath, "Other.Component")),
            "An observation without a structure matches the modification of the same object; another component does not");
        Assert(world.Finds == finds, "Marking rows does not search the scene: " + (world.Finds - finds));
        editor.Undo();
        Assert(!session.IsChanged(Sylva), "Undo of the only edit leaves the object unchanged");
        editor.Redo();
        Assert(session.IsChanged(Sylva) && session.Remove(Sylva).Applied && !session.IsChanged(Sylva), "Removing the modification leaves the object unchanged");
    }
    // The panel reads the terrain output of the inspected board every frame. The scene is searched once per
    // inspection; the output still follows the game's rebuild.
    static void InspectedOutputResolvesOncePerInspection() {
        var (world, board, boards, session, editor, store) = Authoring();
        Assert(session.Edit(Sylva, Pose(Far), null).Applied, "The board moves to another terrain tile");
        SceneObservation Observe() => new SceneObservation(board, Sylva, "Independent board", true, board.Position, board.Rotation, new string[0], new string[0], new string[0], "Fixed placement and activity");
        var inspected = Observe();
        var output = new RailroaderScenePatcher.InspectedOutput();
        var finds = world.Finds;
        for (var frame = 0; frame < 100; frame++) {
            var current = output.Of(session, inspected, out var problem);
            Assert(problem == null && current.Outstanding.Count == 2 && !current.Settled, "Every frame reads the outstanding output of the inspected board");
        }
        Assert(world.Finds == finds + 1, "One inspection searches the scene once, not once per frame: " + (world.Finds - finds));
        world.BuildTiles(); boards.Poll();
        Assert(output.Of(session, inspected, out _).Settled && world.Finds == finds + 1, "Completion is read without another search");
        var expected = session.Output(Sylva);
        var cached = output.Of(session, inspected, out _);
        Assert(cached.Completed.SequenceEqual(expected.Completed) && cached.Outstanding.SequenceEqual(expected.Outstanding) && cached.Error == expected.Error, "The cached read equals the resolving read");
        finds = world.Finds;
        var refreshed = Observe();
        Assert(output.Of(session, refreshed, out _).Settled && output.Of(session, refreshed, out _).Settled && world.Finds == finds + 1, "A new observation, as every refresh and edit produces, searches once again");
        var twin = world.Add(new FakeBoard());
        finds = world.Finds;
        var ambiguous = Observe();
        var none = output.Of(session, ambiguous, out var ambiguity);
        Assert(ambiguity == null && none.Completed.Count == 0 && none.Outstanding.Count == 0 && none.Error == null && session.Output(Sylva).Completed.Count == 0, "A target that does not resolve uniquely has no output, as before");
        output.Of(session, ambiguous, out _);
        Assert(world.Finds == finds + 2, "The failed resolution is remembered for that observation: " + (world.Finds - finds));
        world.Boards.Remove(twin);
    }
    static readonly TargetIdentity Sylva = new TargetIdentity(Scene, IndependentBoards.SylvaPath, IndependentBoards.CullerType, IndependentBoards.Structure);
    static readonly TargetIdentity Whittier = new TargetIdentity(Scene, OtherPath, IndependentBoards.CullerType, IndependentBoards.Structure);
    static Placement Pose(double[] p) => new Placement((double[])p.Clone(), (double[])Turned.Clone());
    static (FakeBoardWorld, FakeBoard, IndependentBoards, AuthoringSession, Editor, Store) Authoring(string patch = null) {
        var (world, board, boards, engine) = Setup();
        var store = new Store { Patch = patch }; var editor = new Editor();
        if (patch != null) engine.Apply(patch, "author/pack");
        var session = new AuthoringSession(engine, editor, store, "map"); editor.Session = session;
        return (world, board, boards, session, editor, store);
    }
    static void PlacementMovesBoardAndItsRegistrations() {
        var (world, board, boards, engine) = Setup();
        var result = engine.Apply(Doc(Mod("move", Place(Moved))), "author/pack");
        Assert(result.Error == null && result.Outcomes["move"].Applied, "Board placement applies: " + result.Error + result.Outcomes["move"].Error);
        Assert(Near(board.Position, Moved) && Near(board.Rotation, Turned), "Board receives the fixed map placement");
        Assert(Consistent(world, board) && world.Registrations == 1, "Culling sphere and mask registration follow the board without a duplicate registration");
    }
    const string OtherPath = "World/Signs/Loc: Whittier";
    static void ConformingBoardsQualifyWithoutTrackBinding() {
        var (world, board, boards, engine) = Setup();
        var other = world.Add(new FakeBoard { Path = OtherPath, Position = new[] { 100.0, 5.0, 100.0 } });
        var result = engine.Apply(Doc(Mod("other", Place(new[] { 110.0, 5.0, 90.0 }), path: OtherPath)), "author/pack");
        Assert(result.Outcomes["other"].Applied && Near(other.Position, new[] { 110.0, 5.0, 90.0 }) && Consistent(world, other), "Another board conforming to the pattern receives placement and refreshed registrations");
        Assert(Near(board.Position, new FakeBoard().Position), "Placing one board leaves another unchanged");
        Assert(boards.MinimumPatcherVersion(new TargetIdentity(Scene, OtherPath, IndependentBoards.CullerType, IndependentBoards.Structure)) == "0.5.0", "Boards beyond the Sylva board need the pattern-based patcher");
        Assert(boards.MinimumPatcherVersion(new TargetIdentity(Scene, IndependentBoards.SylvaPath, IndependentBoards.CullerType, IndependentBoards.Structure)) == null, "The original Sylva target needs no newer capability minimum");
    }
    static void PreflightRefusalLeavesBoardUnchanged() {
        foreach (var (name, change, reason) in new (string, Action<FakeBoard>, string)[] {
            ("track dependency", b => b.Unexpected = "Track.TrackMarker", "Track.TrackMarker"),
            ("height-flattening mask", b => b.Flattens = true, "flattens terrain height"),
            ("disabled culler", b => b.CullerEnabled = false, "renderer culler"),
            ("two masks", b => b.Masks = 2, "exactly one"),
            ("inactive parent", b => b.ParentsActive = false, "parents"),
        }) {
            var (world, board, boards, engine) = Setup();
            change(board);
            var before = (Position: (double[])board.Position.Clone(), Sphere: board.Sphere, Keys: board.Keys.ToArray(), Rebuilds: world.Rebuilds, Writes: world.PoseWrites);
            var result = engine.Apply(Doc(Mod("move", Place(Moved))), "author/pack");
            Assert(!result.Outcomes["move"].Applied && result.Outcomes["move"].Error.Contains(reason), name + ": preflight refuses with its reason: " + result.Outcomes["move"].Error);
            Assert(Near(board.Position, before.Position) && ReferenceEquals(board.Sphere, before.Sphere) && board.Keys.SequenceEqual(before.Keys) && world.Rebuilds == before.Rebuilds && world.PoseWrites == before.Writes, name + ": refusal leaves pose and registrations unchanged");
            Assert(boards.Output(board).Settled && boards.Output(board).Completed.Count == 0, name + ": refusal starts no terrain output");
        }
    }
    static void TargetResolutionAndMapContext() {
        var (world, board, boards, engine) = Setup();
        var result = engine.Apply(Doc(Mod("missing", Place(Moved), path: "World/Signs/Absent") + "," + Mod("move", Place(Moved))), "author/pack");
        Assert(!result.Outcomes["missing"].Applied && result.Outcomes["move"].Applied, "A missing board fails alone");
        (world, board, boards, engine) = Setup();
        world.Add(new FakeBoard());
        result = engine.Apply(Doc(Mod("move", Place(Moved))), "author/pack");
        Assert(!result.Outcomes["move"].Applied && !world.Boards.Any(b => Near(b.Position, Moved)), "An ambiguous board identity moves neither candidate");
        (world, board, boards, engine) = Setup();
        result = engine.Apply(Doc(Mod("move", Place(Moved)), "elsewhere"), "author/pack");
        Assert(result.Error != null && result.Outcomes.Count == 0 && !Near(board.Position, Moved) && world.PoseWrites == 0, "A map mismatch rejects the document before resolution");
        (world, board, boards, engine) = Setup();
        result = engine.Apply(Doc("{\"id\":\"wrong\",\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + IndependentBoards.SylvaPath + "\",\"component\":\"Helpers.Culling.RendererCuller\",\"structure\":\"bridge-v1\"}," + Place(Moved) + ",\"dependsOn\":[]}"), "author/pack");
        Assert(!result.Outcomes["wrong"].Applied && world.PoseWrites == 0, "Another structure identity does not resolve to a board");
    }
    static void RepeatAndOrderDoNotAccumulate() {
        var (world, board, boards, engine) = Setup();
        var other = world.Add(new FakeBoard { Path = OtherPath, Position = new[] { 100.0, 5.0, 100.0 } });
        var json = Doc(Mod("second", Place(new[] { 120.0, 5.0, 80.0 }), "[\"first\"]", OtherPath) + "," + Mod("first", Place(Moved)));
        var result = engine.Apply(json, "author/pack");
        Assert(result.Outcomes["first"].Applied && result.Outcomes["second"].Applied, "A dependent board placement listed first applies after its prerequisite");
        result = new PatchEngine(boards).Apply(json, "author/pack");
        result = new PatchEngine(boards).Apply(json, "author/pack");
        Assert(Near(board.Position, Moved) && Near(other.Position, new[] { 120.0, 5.0, 80.0 }), "Repeated application keeps the absolute placement without offset accumulation");
        Assert(Consistent(world, board) && Consistent(world, other) && world.Registrations == 2, "Repeated application leaves one registration per mask");
        var missing = Doc(Mod("second", Place(new[] { 130.0, 5.0, 70.0 }), "[\"absent\"]", OtherPath));
        result = new PatchEngine(boards).Apply(missing, "author/pack");
        Assert(!result.Outcomes["second"].Applied && Near(other.Position, new[] { 120.0, 5.0, 80.0 }), "A missing prerequisite isolates its dependent");
    }
    static void ActivityReleasesAndRestoresRegistrations() {
        var (world, board, boards, engine) = Setup();
        var result = engine.Apply(Doc(Mod("hide", "\"active\":false")), "author/pack");
        Assert(result.Outcomes["hide"].Applied && !board.Active && board.Sphere == null && board.Keys.Count == 0 && world.Registrations == 0, "Disabling releases the culling sphere and mask registration");
        result = new PatchEngine(boards).Apply(Doc(Mod("hide", "\"active\":false," + Place(Moved))), "author/pack");
        Assert(result.Outcomes["hide"].Applied && Near(board.Position, Moved) && world.Registrations == 0, "A disabled board moves without registering");
        result = new PatchEngine(boards).Apply(Doc(Mod("show", "\"active\":true")), "author/pack");
        Assert(result.Outcomes["show"].Applied && Consistent(world, board) && world.Registrations == 1, "Enabling registers at the current pose once");
    }
    static void FailedMutationRestoresBoardAndRegistrations() {
        var (world, board, boards, engine) = Setup();
        var original = (double[])board.Position.Clone();
        world.FailRebuildCount = 1;
        var result = engine.Apply(Doc(Mod("move", Place(Moved))), "author/pack");
        Assert(!result.Outcomes["move"].Applied && result.Outcomes["move"].Error.Contains("restored"), "A failure after the pose changed reports restoration: " + result.Outcomes["move"].Error);
        Assert(Near(board.Position, original) && Consistent(world, board) && world.Registrations == 1, "Restoration recovers the captured pose and its registrations");
    }
    static void UnverifiedRestorationGuardsRetry() {
        var (world, board, boards, engine) = Setup();
        world.FailRebuild = true;
        var result = engine.Apply(Doc(Mod("move", Place(Moved))), "author/pack");
        Assert(!result.Outcomes["move"].Applied && result.Outcomes["move"].Error.Contains("unverified"), "A restoration that cannot refresh the mask reports incomplete cleanup: " + result.Outcomes["move"].Error);
        world.FailRebuild = false;
        result = engine.Apply(Doc(Mod("again", Place(Moved))), "author/pack");
        Assert(!result.Outcomes["again"].Applied && result.Outcomes["again"].Error.Contains("cleanup"), "Retry on the same board is blocked until cleanup is verified");
    }
    static void TerrainOutputFollowsTheEdit() {
        var (world, board, boards, engine) = Setup();
        var original = (double[])board.Position.Clone();
        engine.Apply(Doc(Mod("move", Place(Far))), "author/pack");
        var output = boards.Output(board);
        Assert(output.Outstanding.Count == 2 && output.Error == null && !output.Settled, "Old and new terrain positions remain outstanding until the game rebuilds them: " + String.Join("; ", output.Outstanding));
        boards.Poll();
        Assert(!boards.Output(board).Settled, "Idle terrain alone does not settle writes whose tiles have not been rebuilt");
        world.BuildTiles("51,0"); boards.Poll();
        Assert(boards.Output(board).Completed.Count == 0, "A rebuilt tile does not complete while other terrain work is still pending");
        world.BuildTiles(); boards.Poll();
        output = boards.Output(board);
        Assert(output.Settled && output.Completed.Count == 2, "Rebuilt tiles and idle terrain complete both writes");
        (world, board, boards, engine) = Setup();
        world.UnloadedTiles.Add("52,0");
        engine.Apply(Doc(Mod("move", Place(Far))), "author/pack");
        Assert(boards.Output(board).Outstanding.Count == 1 && boards.Output(board).Completed.Count == 1, "A position whose tile is not built needs no rebuild; the tile builds from the current mask");
        Assert(Near(original, new FakeBoard().Position), "Fixture position unchanged");
    }
    static void TerrainWriteFailureIsReportedAndRetryable() {
        var (world, board, boards, engine) = Setup();
        engine.Apply(Doc(Mod("move", Place(Far))), "author/pack");
        // The old position's tile is rebuilt; the invalidation of the new one is lost before the game builds it.
        world.PendingTiles.Remove("52,0"); world.BuildTiles(); boards.Poll();
        world.Clock = IndependentBoards.OutputTimeout + 1; boards.Poll();
        var output = boards.Output(board);
        Assert(output.Error != null && output.Completed.Count == 1 && output.Outstanding.Count == 1 && !output.Settled, "A terrain write that does not complete is reported with its completed and outstanding writes");
        Assert(Near(board.Position, Far) && Consistent(world, board), "The board edit is kept when terrain output fails");
        Assert(boards.Failures.Count() == 1, "The failed terrain output is identifiable");
        boards.RetryOutput(board);
        Assert(boards.Output(board).Error == null && !boards.Output(board).Settled && world.Invalidated.SequenceEqual(new[] { "52,0" }), "Retry requests the outstanding writes again without reporting them complete");
        world.BuildTiles(); boards.Poll();
        Assert(boards.Output(board).Settled && boards.Output(board).Completed.Count == 2 && !boards.Failures.Any(), "The retried writes complete");
    }
    static void RestorationWaitsForTerrainOutput() {
        var (world, board, boards, engine) = Setup();
        world.FailRebuildCount = 1;
        var result = engine.Apply(Doc(Mod("move", Place(Moved))), "author/pack");
        Assert(result.Outcomes["move"].Error.Contains("restored"), "Restored pose and registrations verify immediately");
        result = engine.Apply(Doc(Mod("again", Place(Moved))), "author/pack");
        Assert(!result.Outcomes["again"].Applied && result.Outcomes["again"].Error.Contains("cleanup"), "Retry waits while restored terrain output is outstanding");
        world.BuildTiles(); boards.Poll();
        result = engine.Apply(Doc(Mod("later", Place(Moved))), "author/pack");
        Assert(result.Outcomes["later"].Applied && Near(board.Position, Moved), "Verified terrain output lifts the cleanup guard: " + result.Outcomes["later"].Error);
    }
    static void AuthoredBoardFollowsHistoryAndRemoval() {
        var (world, board, boards, session, editor, store) = Authoring();
        var original = (double[])board.Position.Clone();
        Assert(session.Edit(Sylva, Pose(Moved), null).Applied && Near(board.Position, Moved) && Consistent(world, board) && world.Registrations == 1, "An authored board placement moves the board and its registrations");
        var document = JObject.Parse(session.DocumentText);
        var modification = (JObject)((JArray)document["modifications"])[0];
        Assert((string)modification["target"]["structure"] == IndependentBoards.Structure && modification["placement"] != null && modification.Properties().All(p => p.Name != "binding"), "The document records the board placement without a track binding");
        editor.Undo();
        Assert(Near(board.Position, original) && Consistent(world, board) && world.Registrations == 1, "Undo restores the captured pose and registrations without an owned leftover");
        editor.Redo();
        Assert(Near(board.Position, Moved) && Consistent(world, board) && world.Registrations == 1, "Redo reapplies the placement and registrations");
        Assert(session.Edit(Sylva, null, false).Applied && world.Registrations == 0 && board.Sphere == null, "An authored disable releases the board's registrations");
        editor.Undo();
        Assert(board.Active && Consistent(world, board) && world.Registrations == 1, "Undo of the disable registers the board again once");
        Assert(session.Remove(Sylva).Applied && Near(board.Position, original) && Consistent(world, board) && world.Registrations == 1, "Removal returns the board and its registrations to the effective underlying state");
        editor.Undo();
        Assert(Near(board.Position, Moved) && Consistent(world, board), "Undo of the removal restores the authored placement");
        world.BuildTiles(); boards.Poll();
        Assert(session.Output(Sylva).Settled, "Terrain output of every history step completes once the game rebuilds the tiles");
    }
    static void AuthoredTerrainFailureKeepsEditAndRetries() {
        var (world, board, boards, session, editor, store) = Authoring();
        Assert(session.Edit(Sylva, Pose(Far), null).Applied, "The board edit applies");
        world.PendingTiles.Remove("52,0"); world.BuildTiles(); boards.Poll();
        world.Clock = IndependentBoards.OutputTimeout + 1; boards.Poll();
        var output = session.Output(Sylva);
        Assert(output.Error != null && output.Completed.Count == 1 && output.Outstanding.Count == 1, "A terrain-write failure names its completed and outstanding writes");
        Assert(Near(board.Position, Far) && session.DocumentText.Contains("26310"), "The in-memory edit survives the terrain-write failure");
        Assert(editor.Save() == null && !editor.IsDirty, "Save persists the document; Save does not write the generated terrain output");
        Assert(!session.Output(Sylva).Settled, "Save does not report the failed terrain output complete");
        var retry = session.RetryOutput(Sylva);
        Assert(retry.Applied && !session.Output(Sylva).Settled && session.Output(Sylva).Error == null, "Retry requests the outstanding write again without reporting it complete");
        world.BuildTiles(); boards.Poll();
        Assert(session.Output(Sylva).Settled && session.Output(Sylva).Completed.Count == 2, "The retried terrain write completes");
        Assert(!session.RetryOutput(Sylva).Applied, "Retry is refused when nothing is outstanding");
    }
    static void FailedAuthoredEditWaitsForRestoredOutput() {
        var (world, board, boards, session, editor, store) = Authoring();
        var original = (double[])board.Position.Clone();
        world.FailRebuildCount = 1;
        var failed = session.Edit(Sylva, Pose(Moved), null);
        Assert(!failed.Applied && failed.Error.Contains("restored") && Near(board.Position, original) && Consistent(world, board), "A failed authored edit restores the board and its registrations: " + failed.Error);
        Assert(JObject.Parse(session.DocumentText)["modifications"].Count() == 0 && editor.UndoGroups.Count == 0, "A failed edit leaves the document and history unchanged");
        var blocked = session.Edit(Sylva, Pose(Moved), null);
        Assert(!blocked.Applied && blocked.Error.Contains("cleanup"), "The board waits for the restored terrain output before the next edit");
        world.PendingTiles.Clear(); world.Clock = IndependentBoards.OutputTimeout + 1; boards.Poll();
        Assert(session.Output(Sylva).Error != null && !session.Edit(Sylva, Pose(Moved), null).Applied, "Restored terrain output that stopped keeps the board guarded");
        Assert(session.Recheck(Sylva).Applied && world.Invalidated.Count > 0 && !session.Edit(Sylva, Pose(Moved), null).Applied, "Recheck requests the restored output again and keeps the guard until it verifies");
        world.BuildTiles(); boards.Poll();
        Assert(session.Edit(Sylva, Pose(Moved), null).Applied && Near(board.Position, Moved), "Verified restored output permits the retry");
    }
    static void SaveRaisesMinimumForPatternBoards() {
        var (world, board, boards, session, editor, store) = Authoring();
        world.Add(new FakeBoard { Path = OtherPath, Position = new[] { 100.0, 5.0, 100.0 } });
        Assert(session.Edit(Sylva, Pose(Moved), null).Applied && session.MinimumPatcherVersion == "0.1.0", "The Sylva board keeps the fixed-placement minimum");
        Assert(session.Edit(Whittier, Pose(new[] { 110.0, 5.0, 90.0 }), null).Applied && session.MinimumPatcherVersion == "0.5.0", "Another conforming board needs the pattern-based patcher");
        Assert(editor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0"), "Save writes the raised dependency minimum");
    }
}
