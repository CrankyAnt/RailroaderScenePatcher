// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Board and bridge capabilities assembled into one load's scene, as the entry point does.
static class CompositionTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    const string Scene = "BushnellWhittier-MapManager", BridgePath = "World/Large Scenery/Ela/Ela Bridge", OtherBoard = "World/Signs/Loc: Whittier";
    static readonly double[] Moved = { 25772.99, 633.4856, 393.795837 };
    static string Num(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    static string Board(string id, double[] p, string path = IndependentBoards.SylvaPath, string structure = ",\"structure\":\"independent-board-v1\"", string extra = "") =>
        "{\"id\":\"" + id + "\",\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + path + "\",\"component\":\"Helpers.Culling.RendererCuller\"" + structure + "}," +
        "\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", p.Select(Num)) + "],\"rotation\":[0,0,0,1]}" + extra + ",\"dependsOn\":[]}";
    static string Bridge(string id, double x = 1055, string deps = "[]") =>
        "{\"id\":\"" + id + "\",\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + BridgePath + "\",\"component\":\"Helpers.Culling.RendererCuller\",\"structure\":\"bridge-colliders-v1\"}," +
        "\"placement\":{\"mode\":\"fixed\",\"position\":[" + Num(x) + ",5,7],\"rotation\":[0,0,0,1]},\"dependsOn\":" + deps + "}";
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[" + items + "]}";
    sealed class World {
        public FakeBoardWorld Boards = new FakeBoardWorld(); public FakeBoard Board;
        public FakeBridgeWorld Bridges = new FakeBridgeWorld(); public FakeBridge Bridge = new FakeBridge();
        public IndependentBoards BoardCapability; public SceneCapabilities Scene; public PatchEngine Engine;
        public World(bool boardAvailable = true, bool bridgeAvailable = true) {
            Board = Boards.Add(); Boards.BuildTiles();
            // The bridge root carries a culler too, but no circle mask: it is not an independent board.
            Boards.Add(new FakeBoard { Path = BridgePath, Masks = 0, MaskEnabled = false, Position = new[] { 1050.0, 5.0, 7.0 } });
            Boards.BuildTiles();
            Bridges.Objects[BridgePath] = new List<object> { Bridge };
            Scene = new SceneCapabilities(() => Boards.Maps);
            if (boardAvailable) Scene.Add(BoardCapability = new IndependentBoards(Boards));
            else Scene.AddUnavailable(IndependentBoards.Structure, "board functions missing");
            if (bridgeAvailable) Scene.Add(new BridgeScene(Bridges, Capability.Unchecked));
            else Scene.AddUnavailable(BridgeScene.Structure, "bridge functions missing");
            Engine = new PatchEngine(Scene);
        }
        public bool BoardConsistent => Board.Active ? Board.Sphere != null && Board.Keys.Count == 1 && Boards.MaskStore.ContainsKey(Board.Keys[0]) : Board.Sphere == null && Board.Keys.Count == 0;
    }
    internal static void Run() {
        OneDocumentAppliesBoardAndBridge();
        DependentOutputReachesTheOwningCapability();
        UnavailableCapabilityLeavesTheOtherAvailable();
        VersionMinimumIsTheDocumentMaximum();
        Console.WriteLine("PASS: board and bridge capability composition");
    }
    static void OneDocumentAppliesBoardAndBridge() {
        var w = new World();
        var result = w.Engine.Apply(Doc(Bridge("bridge", 1055, "[\"board\"]") + "," + Board("board", Moved)), "author/pack");
        Assert(result.Error == null && result.Outcomes["board"].Applied && result.Outcomes["bridge"].Applied, "One document places the board and the bridge: " + result.Error + result.Outcomes["board"].Error + result.Outcomes["bridge"].Error);
        Assert(w.Board.Position.Zip(Moved, (a, b) => Math.Abs(a - b)).All(d => d < 0.001) && w.BoardConsistent && w.Boards.Registrations == 1, "The board moves with its culling and mask registration");
        Assert(Math.Abs(w.Bridge.Position[0] - 55) < 0.001 && w.Bridges.TrestlePosition[0] == 60, "The bridge moves and its separate trestle stays");
        result = new PatchEngine(w.Scene).Apply(Doc(Board("untyped", Moved, structure: "")), "author/pack");
        Assert(result.Outcomes["untyped"].Applied, "A board target without structure resolves only to the board: " + result.Outcomes["untyped"].Error);
        result = new PatchEngine(w.Scene).Apply(Doc("{\"id\":\"bridge-untyped\",\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + BridgePath + "\",\"component\":\"Helpers.Culling.RendererCuller\"},\"placement\":{\"mode\":\"fixed\",\"position\":[1060,5,7],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}"), "author/pack");
        Assert(result.Outcomes["bridge-untyped"].Applied && Math.Abs(w.Bridge.Position[0] - 60) < 0.001, "The bridge root, a culler without a circle mask, is not claimed by the board capability: " + result.Outcomes["bridge-untyped"].Error);
    }
    static void DependentOutputReachesTheOwningCapability() {
        var w = new World();
        w.Engine.Apply(Doc(Board("board", Moved) + "," + Bridge("bridge")), "author/pack");
        var output = w.Scene.Output(w.Board);
        Assert(output.Outstanding.Count == 2 && !output.Settled, "The composite reports the board's outstanding terrain output");
        Assert(w.Scene.Output(w.Bridge).Settled && w.Scene.Output(w.Bridge).Completed.Count == 0, "An adapter without dependent output reports none");
        w.Boards.PendingTiles.Clear(); w.Boards.Clock = IndependentBoards.OutputTimeout + 1; w.BoardCapability.Poll();
        Assert(w.Scene.Output(w.Board).Error != null, "A stopped terrain write is visible through the composite");
        w.Scene.RetryOutput(w.Board);
        Assert(w.Scene.Output(w.Board).Error == null && w.Boards.Invalidated.Count > 0, "Retry reaches the board capability");
        w.Boards.BuildTiles(); w.BoardCapability.Poll();
        Assert(w.Scene.Output(w.Board).Settled, "The retried output completes");
        // The engine's cleanup guard sees restored board output through the composite.
        w = new World();
        w.Boards.FailRebuildCount = 1;
        var result = w.Engine.Apply(Doc(Board("board", Moved)), "author/pack");
        Assert(result.Outcomes["board"].Error.Contains("restored") && w.BoardConsistent, "A failed board mutation restores through the composite: " + result.Outcomes["board"].Error);
        result = w.Engine.Apply(Doc(Board("again", Moved) + "," + Bridge("bridge")), "author/pack");
        Assert(!result.Outcomes["again"].Applied && result.Outcomes["again"].Error.Contains("cleanup") && result.Outcomes["bridge"].Applied, "The board waits for its restored output while the bridge still applies");
        w.Boards.BuildTiles(); w.BoardCapability.Poll();
        result = w.Engine.Apply(Doc(Board("later", Moved)), "author/pack");
        Assert(result.Outcomes["later"].Applied, "Verified restored output lifts the board's guard");
    }
    static void UnavailableCapabilityLeavesTheOtherAvailable() {
        var w = new World(boardAvailable: false);
        var result = w.Engine.Apply(Doc(Board("board", Moved) + "," + Bridge("bridge")), "author/pack");
        Assert(!result.Outcomes["board"].Applied && result.Outcomes["board"].Error.Contains("board functions missing") && result.Outcomes["bridge"].Applied, "An unavailable board capability fails only the board modification");
        Assert(w.Boards.PoseWrites == 0, "No board is moved without its capability");
        w = new World(bridgeAvailable: false);
        result = w.Engine.Apply(Doc(Board("board", Moved) + "," + Bridge("bridge")), "author/pack");
        Assert(result.Outcomes["board"].Applied && !result.Outcomes["bridge"].Applied && result.Outcomes["bridge"].Error.Contains("bridge functions missing"), "An unavailable bridge capability fails only the bridge modification");
        Assert(w.Bridges.Mutations == 0 && w.BoardConsistent, "The board applies with its registrations while the bridge is untouched");
    }
    static void VersionMinimumIsTheDocumentMaximum() {
        AuthoringSession Session(World w, Store store) { var editor = new Editor(); var s = new AuthoringSession(w.Engine, editor, store, "map"); editor.Session = s; return s; }
        var cases = new (string Name, string Items, bool BoardAvailable, bool BridgeAvailable, string Expected)[] {
            ("Sylva placement", Board("a", Moved), true, true, "0.1.0"),
            ("Sylva with activity", Board("a", Moved, extra: ",\"active\":false"), true, true, "0.2.0"),
            ("another board", Board("a", new[] { 110.0, 5.0, 90.0 }, OtherBoard), true, true, "0.3.0"),
            ("bridge with Sylva activity", Board("a", Moved, extra: ",\"active\":false") + "," + Bridge("b"), true, true, "0.3.0"),
            ("another board without its capability", Board("a", new[] { 110.0, 5.0, 90.0 }, OtherBoard), false, true, "0.3.0"),
            ("bridge without its capability", Bridge("b"), true, false, "0.3.0"),
        };
        foreach (var c in cases) {
            var w = new World(c.BoardAvailable, c.BridgeAvailable);
            var session = Session(w, new Store { Patch = Doc(c.Items) });
            Assert(session.Blocked == null, c.Name + ": the document is editable: " + session.Blocked);
            Assert(session.MinimumPatcherVersion == c.Expected, c.Name + ": the minimum is the highest requirement of the document, got " + session.MinimumPatcherVersion);
        }
        var stricter = new World();
        var store = new Store { Patch = Doc(Board("a", new[] { 110.0, 5.0, 90.0 }, OtherBoard)), Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.4.0\"]}}" };
        stricter.Boards.Add(new FakeBoard { Path = OtherBoard, Position = new[] { 100.0, 5.0, 100.0 } });
        var kept = Session(stricter, store);
        Assert(!kept.Outstanding(false).Contains(AuthoringSession.Metadata), "A stricter existing constraint is kept");
    }
}
