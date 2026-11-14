// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // Native writes that follow a mutation asynchronously, such as generated terrain output.
    // Completed and Outstanding name individual writes; Error is set when they stopped without completing.
    public sealed class DependentOutput {
        public static readonly DependentOutput None = new DependentOutput(Array.Empty<string>(), Array.Empty<string>(), null);
        public DependentOutput(IEnumerable<string> completed, IEnumerable<string> outstanding, string error) {
            Completed = Array.AsReadOnly(completed.ToArray()); Outstanding = Array.AsReadOnly(outstanding.ToArray()); Error = error;
        }
        public IReadOnlyList<string> Completed { get; }
        public IReadOnlyList<string> Outstanding { get; }
        public string Error { get; }
        public bool Settled => Outstanding.Count == 0 && Error == null;
    }
    // A scene whose mutations start dependent native writes that complete after the mutation returns.
    public interface IDependentOutputScene {
        DependentOutput Output(object target);
        // Requests the outstanding writes again through the native mechanism.
        void RetryOutput(object target);
    }
    // A scene that knows which patcher version first supports a target.
    public interface IVersionedScene {
        // Null when the target needs no version beyond the document's own requirements.
        string MinimumPatcherVersion(TargetIdentity target);
    }

    // Side-effect-free facts about a board candidate.
    public sealed class BoardFacts {
        public BoardFacts(string scene, string path, bool sceneLoaded, bool parentsActive, bool hasCuller, bool cullerEnabled, int masks, bool maskEnabled, bool flattensHeight, string unexpectedDependency, object parent) {
            Scene = scene; Path = path; SceneLoaded = sceneLoaded; ParentsActive = parentsActive; HasCuller = hasCuller; CullerEnabled = cullerEnabled;
            Masks = masks; MaskEnabled = maskEnabled; FlattensHeight = flattensHeight; UnexpectedDependency = unexpectedDependency; Parent = parent;
        }
        public string Scene { get; }
        public string Path { get; }
        public bool SceneLoaded { get; }
        public bool ParentsActive { get; }
        public bool HasCuller { get; }
        public bool CullerEnabled { get; }
        // Circle map masks on the board root.
        public int Masks { get; }
        public bool MaskEnabled { get; }
        public bool FlattensHeight { get; }
        // A component in the board hierarchy whose dependent state this capability does not refresh, or null.
        public string UnexpectedDependency { get; }
        public object Parent { get; }
    }
    // The registrations the board's components currently hold in the game's culling and terrain systems, in map coordinates.
    public sealed class BoardRegistration {
        public BoardRegistration(double[] cullingCenter, IEnumerable<double[]> maskCenters, int missingMaskKeys) {
            CullingCenter = cullingCenter; MaskCenters = Array.AsReadOnly(maskCenters.ToArray()); MissingMaskKeys = missingMaskKeys;
        }
        public double[] CullingCenter { get; }
        public IReadOnlyList<double[]> MaskCenters { get; }
        // Mask keys the component holds that the terrain system no longer stores.
        public int MissingMaskKeys { get; }
    }
    // The native operations of the independent-board capability. Positions are map coordinates.
    public interface IBoardWorld {
        IEnumerable<string> MapIds { get; }
        // Objects at the scene path that carry the board's renderer culler.
        IReadOnlyList<object> Find(string scene, string path);
        BoardFacts Facts(object board);
        string Label(object board);
        double[] Position(object board);
        double[] Rotation(object board);
        bool Active(object board);
        void SetPose(object board, double[] position, double[] rotation);
        void SetActive(object board, bool active);
        void RefreshCulling(object board);
        void RebuildMask(object board);
        BoardRegistration Registration(object board);
        // Whether the terrain tile containing the position is built; an unbuilt tile later builds from the current masks.
        bool TileLoaded(double[] position);
        void InvalidateTile(double[] position);
        bool TerrainIdle { get; }
        // Monotonic seconds.
        double Now { get; }
        // Bounds of a built terrain tile: minimum and maximum corner.
        event Action<double[], double[]> TileBuilt;
    }

    // Independent boards: scene objects with a renderer culler and a circle map mask and no track relationship.
    // Placement and activity refresh the culling sphere and the mask registration; the game's terrain system
    // then regenerates the output at the old and new positions, which this capability tracks to completion.
    public sealed class IndependentBoards : IScene, IInspectionScene, IDependentOutputScene, IVersionedScene, IDisposable {
        public const string Structure = "independent-board-v1";
        public const string CullerType = "Helpers.Culling.RendererCuller";
        public const string SylvaScene = "BushnellWhittier-MapManager", SylvaPath = "World/Signs/Loc: Sylva Int";
        // Pattern-based boards share the delivery minimum; the original Sylva target retains its legacy support.
        public const string PatternVersion = "0.5.0";
        public const double OutputTimeout = 30;
        const double PoseTolerance = 0.01, RegistrationTolerance = 0.1;
        readonly IBoardWorld world;
        readonly Func<IEnumerable<SceneObservation>> discover;
        readonly Dictionary<object, Work> work = new Dictionary<object, Work>();
        readonly Action<double[], double[]> built;
        sealed class Snapshot { internal double[] Position, Rotation; internal bool Active; internal object Parent; }
        sealed class Write { internal double[] Position; internal string Label; internal bool Built; }
        sealed class Work {
            internal readonly List<Write> Outstanding = new List<Write>();
            internal readonly List<string> Completed = new List<string>();
            internal double Since;
            internal string Error;
            internal bool Settled => Outstanding.Count == 0 && Error == null;
        }
        public IndependentBoards(IBoardWorld world, Func<IEnumerable<SceneObservation>> discover = null) {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.discover = discover ?? (() => Array.Empty<SceneObservation>());
            built = TileBuilt;
            world.TileBuilt += built;
        }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability => Capability.Unchecked;
        public IEnumerable<SceneObservation> Discover() => discover();

        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != CullerType || (identity.Structure != null && identity.Structure != Structure)) return Array.Empty<object>();
            // Other capabilities share the renderer culler; only an object that carries a circle mask is a board candidate.
            return (world.Find(identity.Scene, identity.Path) ?? Array.Empty<object>()).Where(o => world.Facts(o).Masks > 0).ToArray();
        }
        // Why the object does not conform to the independent-board pattern, or null.
        public string Refusal(object target) {
            var facts = world.Facts(target);
            if (!facts.SceneLoaded) return "The board's scene is not loaded";
            if (!facts.HasCuller || !facts.CullerEnabled) return "The board's renderer culler must be present and enabled";
            if (facts.Masks != 1 || !facts.MaskEnabled) return "The board must carry exactly one enabled circle map mask";
            if (!facts.ParentsActive) return "The board's parents must be active";
            if (facts.FlattensHeight) return "The board's circle map mask flattens terrain height, which this capability does not support";
            if (facts.UnexpectedDependency != null) return "The board hierarchy contains " + facts.UnexpectedDependency + ", whose dependent state this capability does not refresh";
            return null;
        }
        public void Preflight(object target) {
            var refusal = Refusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            Finite(world.Position(target));
        }
        public object Capture(object target) => new Snapshot { Position = world.Position(target), Rotation = world.Rotation(target), Active = world.Active(target), Parent = world.Facts(target).Parent };
        public ObjectState Read(object target) => new ObjectState(new Placement(world.Position(target), world.Rotation(target)), world.Active(target));

        public void Apply(object target, ObjectState state) {
            double[] position = null, rotation = null;
            if (state.Placement != null) {
                position = Finite(state.Placement.Position); rotation = Finite(state.Placement.Rotation);
                if (position.Length != 3 || rotation.Length != 4) throw new InvalidOperationException("Invalid placement coordinates");
                if (Math.Abs(rotation.Sum(x => x * x) - 1) > 0.001) throw new InvalidOperationException("Rotation must be a unit quaternion");
            }
            var active = state.Active ?? world.Active(target);
            // Enabling registers the culling sphere and mask at the current pose; the pose is refreshed afterwards.
            if (active && !world.Active(target)) { Track(target, world.Position(target)); world.SetActive(target, true); }
            if (position != null) {
                // An inactive board holds no culling sphere or mask; both register at the new pose when it is enabled.
                var registered = world.Active(target);
                if (registered) { Track(target, world.Position(target)); Track(target, position); }
                world.SetPose(target, position, rotation);
                if (registered) { world.RefreshCulling(target); world.RebuildMask(target); }
            }
            // Disabling releases the culling sphere and the mask through the components' own lifecycle.
            if (!active && world.Active(target)) { Track(target, world.Position(target)); world.SetActive(target, false); }
            Verify(target, position, rotation, active);
        }
        void Verify(object target, double[] position, double[] rotation, bool active) {
            var now = world.Position(target);
            if (position != null && (Distance(now, position) > PoseTolerance || Angle(world.Rotation(target), rotation) > 0.01)) throw new InvalidOperationException("Applied pose did not verify");
            if (world.Active(target) != active) throw new InvalidOperationException("Applied activity did not verify");
            var registration = world.Registration(target);
            if (registration.MissingMaskKeys != 0) throw new InvalidOperationException("The terrain system no longer stores the board's mask registration");
            if (!active) {
                if (registration.CullingCenter != null || registration.MaskCenters.Count != 0) throw new InvalidOperationException("A disabled board still holds culling or mask registrations");
                return;
            }
            if (registration.CullingCenter == null || Distance(registration.CullingCenter, now) > RegistrationTolerance) throw new InvalidOperationException("The culling sphere did not follow the board");
            if (registration.MaskCenters.Count == 0 || registration.MaskCenters.Any(c => Flat(c, now) > RegistrationTolerance)) throw new InvalidOperationException("The mask registration did not follow the board");
        }
        public bool Restore(object target, object snapshot) {
            var state = (Snapshot)snapshot;
            if (!ReferenceEquals(world.Facts(target).Parent, state.Parent)) return false;
            Apply(target, new ObjectState(new Placement(state.Position, state.Rotation), state.Active));
            return true;
        }

        // Dependent terrain output.
        void Track(object target, double[] position) {
            if (!work.TryGetValue(target, out var item)) work.Add(target, item = new Work());
            else if (item.Settled) item.Completed.Clear();
            item.Error = null; item.Since = world.Now;
            var label = "terrain output at map x=" + Text(position[0]) + ", z=" + Text(position[2]);
            if (!world.TileLoaded(position)) { item.Completed.Add(label + " (tile not built; it builds from the current mask)"); return; }
            if (item.Outstanding.Any(w => w.Label == label)) { foreach (var w in item.Outstanding.Where(w => w.Label == label)) w.Built = false; return; }
            item.Outstanding.Add(new Write { Position = (double[])position.Clone(), Label = label });
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
                if (item.Outstanding.Count != 0 && world.Now - item.Since > OutputTimeout) item.Error = "Terrain output did not complete within " + OutputTimeout.ToString(CultureInfo.InvariantCulture) + " seconds";
            }
        }
        public DependentOutput Output(object target) {
            if (target == null || !work.TryGetValue(target, out var item)) return DependentOutput.None;
            return new DependentOutput(item.Completed, item.Outstanding.Select(w => w.Label), item.Error);
        }
        public void RetryOutput(object target) {
            if (target == null || !work.TryGetValue(target, out var item) || item.Outstanding.Count == 0) return;
            item.Error = null; item.Since = world.Now;
            foreach (var write in item.Outstanding.ToArray()) {
                if (!world.TileLoaded(write.Position)) { item.Outstanding.Remove(write); item.Completed.Add(write.Label + " (tile unloaded; it builds from the current mask)"); continue; }
                write.Built = false;
                try { world.InvalidateTile(write.Position); }
                catch (Exception e) { item.Error = "Terrain output could not be requested again: " + e.Message; return; }
            }
        }
        // Boards whose dependent output stopped without completing, with their reports.
        public IEnumerable<KeyValuePair<object, DependentOutput>> Failures => work.Where(p => p.Value.Error != null).Select(p => new KeyValuePair<object, DependentOutput>(p.Key, Output(p.Key))).ToArray();
        public string Status {
            get {
                if (work.Values.Any(w => w.Error != null)) return "Terrain output stopped without completing for " + work.Count(p => p.Value.Error != null) + " board(s); retry it from the inspected board";
                var outstanding = work.Values.Sum(w => w.Outstanding.Count);
                return outstanding == 0 ? "No terrain output outstanding" : "Terrain output outstanding at " + outstanding + " position(s); the game is regenerating it";
            }
        }
        public string MinimumPatcherVersion(TargetIdentity target) => RequiredVersion(target);
        // The requirement holds for the document whether or not this capability is available in the current load.
        public static string RequiredVersion(TargetIdentity target) =>
            target != null && target.Component == CullerType && (target.Structure == null || target.Structure == Structure) && (target.Scene != SylvaScene || target.Path != SylvaPath) ? PatternVersion : null;
        public void Dispose() { world.TileBuilt -= built; work.Clear(); }

        static double[] Finite(double[] values) {
            if (values == null || values.Any(v => Double.IsNaN(v) || Double.IsInfinity(v) || Math.Abs(v) > Single.MaxValue)) throw new InvalidOperationException("Coordinate exceeds native precision");
            return values;
        }
        static double Distance(double[] a, double[] b) => Math.Sqrt(a.Zip(b, (x, y) => (x - y) * (x - y)).Sum());
        static double Flat(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[2] - b[2]) * (a[2] - b[2]));
        static double Angle(double[] a, double[] b) {
            double dot = 0, la = 0, lb = 0;
            for (var i = 0; i < 4; i++) { dot += a[i] * b[i]; la += a[i] * a[i]; lb += b[i] * b[i]; }
            return Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(la * lb))) * 2 * 180 / Math.PI;
        }
        static string Text(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
