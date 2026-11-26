// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RailroaderScenePatcher.Core;

// The structure ids of the scene object, child object and location sign targets. The retired ids are read by no
// adapter: a modification whose target carries one matches no object and fails with the no-match row, the other
// modifications of the same scene patch file apply, and the runtime leaves the file as it was written.
static class StructureIdTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Structure ids: " + message); }
    const string SceneName = "BushnellWhittier-MapManager", Mill = "World/Large Scenery/Whittier/Whittier Saw Mill";
    static readonly string[] RetiredIds = { "baked-scenery-group-v1", "baked-scenery-part-v1", "independent-board-v1" };

    // An in-memory mod folder that counts every write.
    sealed class MemoryFiles : IModFiles {
        public readonly Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();
        public int Writes;
        public bool Exists(string path) => Data.ContainsKey(path);
        public byte[] Read(string path) => Data[path].ToArray();
        public void Write(string path, byte[] bytes) { Writes++; Data[path] = bytes.ToArray(); }
        public void Replace(string source, string destination) { Writes++; Data[destination] = Data[source]; Data.Remove(source); }
        public void Move(string source, string destination) => Replace(source, destination);
        public void Delete(string path) { Writes++; Data.Remove(path); }
        public void CreateDirectory(string path) { }
    }

    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    static string Mod(string id, string path, string component, string structure, double[] position) =>
        "\"" + id + "\": {\"target\": {\"scene\": \"" + SceneName + "\", \"path\": \"" + path + "\", \"component\": \"" + component + "\", \"structure\": \"" + structure + "\"},\n" +
        "    \"placement\": {\"mode\": \"fixed\", \"position\": [" + N(position) + "], \"rotation\": [0, 0, 0, 1.0]}, \"dependsOn\": []}";
    static string Transform(string id, string path, string structure, double[] position) => Mod(id, path, "UnityEngine.Transform", structure, position);
    static string Board(string id, string structure, double[] position) => Mod(id, LocationSigns.SylvaPath, LocationSigns.CullerType, structure, position);

    internal static void Run() {
        RetiredIdsMatchNoObjectInAWholeFile();
        Console.WriteLine("PASS: retired structure ids match no object, the other modifications apply and the file stays as written");
    }

    static void RetiredIdsMatchNoObjectInAWholeFile() {
        // One load's scene with the scene object, child object and location sign capabilities, as the entry point
        // composes it: the saw mill with its parts, and the Sylva location sign.
        var f = new ChildObjectTests.Fixture();
        var boards = new FakeBoardWorld(); var board = boards.Add(); boards.BuildTiles();
        f.Scene.Add(new LocationSigns(boards));
        var boardStart = board.Position.ToArray(); var groupStart = f.World.Observe(f.Group).Root.Placement.Position.ToArray();

        double[] groupMove = { 1110, 10, 20 }, partMove = { 1130, 10, 30 }, boardMove = { 25772.99, 633.4856, 393.795837 };
        // The written text has its own spacing and an order of keys that the canonical order would change.
        var text = "{\"formatVersion\": 1, \"sourceMap\": {\"mapId\": \"map\"}, \"modifications\": {\n" + String.Join(",\n", new[] {
            Board("sign-old", "independent-board-v1", boardMove),
            Transform("part-old", Mill + "/Jetty", "baked-scenery-part-v1", partMove),
            Transform("group-old", Mill, "baked-scenery-group-v1", groupMove),
            Board("sign", LocationSigns.Structure, boardMove),
            Transform("part", Mill + "/Ramp", ChildObjects.Structure, partMove),
            Transform("group", Mill, SceneObjects.Structure, groupMove),
        }) + "\n}}\n";
        var files = new MemoryFiles(); var storage = new ModStorage("C:/Mods/Pack", files);
        var written = ModStorage.Utf8.GetBytes(text); files.Data[storage.PatchPath] = written.ToArray();

        // A group modification that names the old id is not taken for the group by the new id: the old target is the
        // same object as the applied one.
        var result = f.Engine.Apply(storage.ReadScenePatchFile(), "author/pack");
        Check(result.Error == null, "the scene patch file is read as a whole: " + result.Error);
        foreach (var id in new[] { "group-old", "part-old", "sign-old" }) {
            var outcome = result.Outcomes[id];
            Check(!outcome.Applied && (outcome.Error ?? "").StartsWith(PatchEngine.TargetMissing, StringComparison.Ordinal),
                id + " fails with the row \"" + PatchEngine.TargetMissing + "\": " + outcome.Error);
        }
        foreach (var id in new[] { "group", "part", "sign" })
            Check(result.Outcomes[id].Applied, id + " with the current structure id applies: " + result.Outcomes[id].Error);
        Check(board.Position.Zip(boardMove, (a, b) => Math.Abs(a - b)).All(d => d < 0.001) && !board.Position.SequenceEqual(boardStart), "the location sign moved by its current id");
        Check(f.World.Observe(f.Group).Root.Placement.Position.Zip(groupMove, (a, b) => Math.Abs(a - b)).All(d => d < 0.001) && !groupMove.SequenceEqual(groupStart), "the saw mill moved by its current id");
        Check(files.Writes == 0 && files.Data[storage.PatchPath].SequenceEqual(written), "the runtime does not rewrite the scene patch file: its bytes stay as written");

        // No adapter claims a retired id, also for a target that names nothing else.
        foreach (var retired in RetiredIds)
            Check(f.Scene.Resolve(new TargetIdentity(SceneName, Mill, "UnityEngine.Transform", retired)).Count == 0, retired + " resolves to no object");
    }
}
