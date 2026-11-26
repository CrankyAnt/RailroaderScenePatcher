// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The game's terrain system as a mask change reaches it. Positions are map coordinates.
    public interface ITerrainWorld {
        // Whether the terrain tile containing the position is built; an unbuilt tile later builds from the current masks.
        bool TileLoaded(double[] position);
        void InvalidateTile(double[] position);
        bool TerrainIdle { get; }
        // Monotonic seconds.
        double Now { get; }
        // Bounds of a built terrain tile: minimum and maximum corner.
        event Action<double[], double[]> TileBuilt;
    }
    // The terrain output that follows mask changes, by the object whose edit changed the mask: the game regenerates the
    // terrain at each old and new mask position after the mutation returns, and this tracks those writes to completion.
    public sealed class TerrainOutput : IDisposable {
        public const double Timeout = 30;
        readonly ITerrainWorld world;
        readonly Dictionary<object, Work> work = new Dictionary<object, Work>();
        readonly Action<double[], double[]> built;
        sealed class Write { internal double[] Position; internal string Label; internal (long X, long Z) Key; internal bool Built; }
        sealed class Work {
            internal readonly List<Write> Outstanding = new List<Write>();
            internal readonly List<string> Completed = new List<string>();
            internal double Since;
            internal string Error;
            internal bool Settled => Outstanding.Count == 0 && Error == null;
        }
        public TerrainOutput(ITerrainWorld world) {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            built = TileBuilt;
            world.TileBuilt += built;
        }
        // A mask of the target changed at the position: the terrain there is outstanding until the game rebuilds its tile.
        public void Track(object target, double[] position) {
            if (!work.TryGetValue(target, out var item)) work.Add(target, item = new Work());
            else if (item.Settled) item.Completed.Clear();
            item.Error = null; item.Since = world.Now;
            var label = "terrain output at map x=" + Text(position[0]) + ", z=" + Text(position[2]);
            if (!world.TileLoaded(position)) { item.Completed.Add(label + " (tile not built; it builds from the current mask)"); return; }
            var key = KeyOf(position);
            if (item.Outstanding.Any(w => w.Key == key)) { foreach (var w in item.Outstanding.Where(w => w.Key == key)) w.Built = false; return; }
            item.Outstanding.Add(new Write { Position = (double[])position.Clone(), Label = label, Key = key });
        }
        void TileBuilt(double[] min, double[] max) {
            foreach (var item in work.Values)
                foreach (var write in item.Outstanding)
                    if (write.Position[0] >= min[0] && write.Position[0] <= max[0] && write.Position[2] >= min[2] && write.Position[2] <= max[2]) write.Built = true;
        }
        // Advances completion. The game reports a write complete when its tile was built afterwards and all terrain work is idle.
        public void Poll() {
            foreach (var item in work.Values) {
                if (item.Error != null || item.Outstanding.Count == 0) continue;
                foreach (var write in item.Outstanding.ToArray())
                    if (!world.TileLoaded(write.Position)) { item.Outstanding.Remove(write); item.Completed.Add(write.Label + " (tile unloaded; it builds from the current mask)"); }
                if (world.TerrainIdle) {
                    var done = item.Outstanding.Where(w => w.Built).ToArray();
                    foreach (var write in done) { item.Outstanding.Remove(write); item.Completed.Add(write.Label); }
                }
                if (item.Outstanding.Count != 0 && world.Now - item.Since > Timeout) item.Error = "Terrain output didn't complete within " + Timeout.ToString(CultureInfo.InvariantCulture) + " seconds";
            }
        }
        public DependentOutput Output(object target) {
            if (target == null || !work.TryGetValue(target, out var item)) return DependentOutput.None;
            return new DependentOutput(item.Completed, item.Outstanding.Select(w => w.Label), item.Error);
        }
        // Requests the outstanding writes again through the native mechanism.
        public void RetryOutput(object target) {
            if (target == null || !work.TryGetValue(target, out var item) || item.Outstanding.Count == 0) return;
            item.Error = null; item.Since = world.Now;
            foreach (var write in item.Outstanding.ToArray()) {
                if (!world.TileLoaded(write.Position)) { item.Outstanding.Remove(write); item.Completed.Add(write.Label + " (tile unloaded; it builds from the current mask)"); continue; }
                write.Built = false;
                try { world.InvalidateTile(write.Position); }
                catch (Exception e) { item.Error = "Couldn't request terrain output again: " + e.Message; return; }
            }
        }
        // The targets whose output stopped without completing, with their reports.
        public IEnumerable<KeyValuePair<object, DependentOutput>> Failures => work.Where(p => p.Value.Error != null).Select(p => new KeyValuePair<object, DependentOutput>(p.Key, Output(p.Key))).ToArray();
        // How many targets stopped without completing, and how many positions are outstanding.
        public int Stopped => work.Count(p => p.Value.Error != null);
        public int Outstanding => work.Values.Sum(w => w.Outstanding.Count);
        public void Dispose() { world.TileBuilt -= built; work.Clear(); }
        static string Text(double v) => ShownNumber.Text(v);
        // Writes at one place: positions that round to the same tenth of a metre on both map axes, so a write within
        // 0.05 m of an outstanding one requests that write again. The shown label keeps the shown number format.
        static (long X, long Z) KeyOf(double[] position) =>
            ((long)Math.Round(position[0] * 10, MidpointRounding.AwayFromZero), (long)Math.Round(position[2] * 10, MidpointRounding.AwayFromZero));
    }
}
