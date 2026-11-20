// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // One registration a scene ground mask holds in the game's terrain system, in map coordinates: the centre of the
    // mask's shape and the direction of its long axis on the ground, or no axis for a shape without a direction.
    public sealed class GroundMaskRegistration {
        public GroundMaskRegistration(IEnumerable<double> centre, IEnumerable<double> axis) { Centre = centre.ToArray(); Axis = axis?.ToArray(); }
        public IReadOnlyList<double> Centre { get; }
        public IReadOnlyList<double> Axis { get; }
    }
    // One scene ground mask (a component in Map.Runtime.MaskComponents) at or below a group root or part, observed with
    // copied values. Positions are map coordinates.
    public sealed class GroundMaskState {
        public GroundMaskState(string id, string holder, bool enabled, bool flattens, IEnumerable<double> centre, IEnumerable<double> axis, int modifiers, IEnumerable<GroundMaskRegistration> registrations, int missingKeys) {
            Id = id ?? throw new ArgumentNullException(nameof(id)); Holder = holder; Enabled = enabled; Flattens = flattens; Centre = centre.ToArray(); Axis = axis?.ToArray();
            Modifiers = modifiers; Registrations = (registrations ?? Enumerable.Empty<GroundMaskRegistration>()).ToArray(); MissingKeys = missingKeys;
        }
        // Stable for the loaded component.
        public string Id { get; }
        // The name of the scene object that holds the mask.
        public string Holder { get; }
        // The component is active and enabled, so the game holds its registrations; a disabled one holds none.
        public bool Enabled { get; }
        // The mask also sets the terrain height under it.
        public bool Flattens { get; }
        // Where the mask's holder stands now, and the direction its shape has there; no axis for a shape without one.
        public IReadOnlyList<double> Centre { get; }
        public IReadOnlyList<double> Axis { get; }
        // How many terrain modifiers the component makes when it registers.
        public int Modifiers { get; }
        public IReadOnlyList<GroundMaskRegistration> Registrations { get; }
        // Keys the component holds that the terrain system no longer stores.
        public int MissingKeys { get; }
    }
    // The scene ground masks of baked scenery as the game holds them, and the terrain system they register in.
    public interface IGroundMaskWorld : ITerrainWorld {
        // The scene ground masks at and below a group root or part, in a stable order.
        IReadOnlyList<GroundMaskState> Masks(object target);
        // Makes the mask register again where its holder stands now, through the game's own rebuild.
        void Rebuild(object target, string id);
    }
    // Scene ground masks of baked scenery groups. A mask registers its shape in the game's terrain system where its
    // holder stood when it was enabled; after a move of the holder, the mask is rebuilt so that its registration follows,
    // and the game regenerates terrain and vegetation at the old and the new place. That output is tracked by the
    // edited object. The group and part capabilities of one scene share one instance.
    public sealed class GroundMasks : IDisposable {
        const double CentreTolerance = 0.1, AxisToleranceDegrees = 0.1;
        readonly IGroundMaskWorld world;
        readonly TerrainOutput output;
        public GroundMasks(IGroundMaskWorld world) { this.world = world ?? throw new ArgumentNullException(nameof(world)); output = new TerrainOutput(world); }
        // Why the move of an object whose ground mask flattens the terrain height is refused.
        public static string FlatteningRefusal(string holder) =>
            "The ground mask on " + holder + " flattens the terrain height. The game might not restore that height after a move, so this object can't be moved.";
        internal IReadOnlyList<GroundMaskState> Observe(object scope) => world.Masks(scope) ?? Array.Empty<GroundMaskState>();
        // Why the placement of the group root or part cannot be set, or null: a ground mask at or below it flattens the
        // terrain height. Activity stays editable.
        internal string PlacementRefusal(object target) {
#if SCENE_PATCHER_FAULTS
            // Test only: the fault build can lift the refusal to gather the evidence that would end it.
            if (FaultInjection.FlatteningMovesAllowed) return null;
#endif
            var flattening = Observe(target).FirstOrDefault(mask => mask.Flattens);
            return flattening == null ? null : FlatteningRefusal(flattening.Holder);
        }
        // After an edit of target changed placement or activity inside the group at scope: every enabled mask whose
        // registration no longer stands at its holder is rebuilt there, and the terrain at each place a registration
        // left or reached is tracked under target. before holds the masks as they were when the edit started. A mask
        // the edit left as it was, such as the station's mask when a sign is disabled, is neither rebuilt nor judged.
        internal void Follow(object target, object scope, IReadOnlyList<GroundMaskState> before) {
            var changed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mask in Observe(scope)) {
                var was = before?.FirstOrDefault(b => b.Id == mask.Id);
                // A holder can also leave its mask behind in steps that are each within the tolerance, so a mask that
                // disagrees with its holder now, and did not before, counts as changed.
                if (was != null && Same(was, mask) && Stale(mask) == Stale(was)) continue;
                changed.Add(mask.Id);
                if (mask.Enabled && Stale(mask) != null) {
                    // A rebuild that fails changes no registration, so it starts no terrain output.
                    world.Rebuild(scope, mask.Id);
                    foreach (var registration in mask.Registrations) output.Track(target, registration.Centre.ToArray());
                    if (mask.Modifiers != 0) output.Track(target, mask.Centre.ToArray());
                }
                // The component's own lifecycle registered or released the mask with the activity.
                else if (was != null && (was.Registrations.Count == 0) != (mask.Registrations.Count == 0))
                    foreach (var registration in was.Registrations.Concat(mask.Registrations)) output.Track(target, registration.Centre.ToArray());
            }
            var difference = changed.Count == 0 ? null : Observe(scope).Where(mask => changed.Contains(mask.Id)).Select(Stale).FirstOrDefault(found => found != null);
            if (difference != null) throw new InvalidOperationException("The ground mask did not follow: " + difference);
        }
        // Whether the edit left the mask as it was: its activity, its holder's place and direction, and its registrations.
        static bool Same(GroundMaskState was, GroundMaskState now) {
            if (was.Enabled != now.Enabled || was.MissingKeys != now.MissingKeys || was.Registrations.Count != now.Registrations.Count) return false;
            if (Flat(was.Centre, now.Centre) > CentreTolerance || !SameAxis(was.Axis, now.Axis)) return false;
            for (var i = 0; i < now.Registrations.Count; i++)
                if (Flat(was.Registrations[i].Centre, now.Registrations[i].Centre) > CentreTolerance || !SameAxis(was.Registrations[i].Axis, now.Registrations[i].Axis)) return false;
            return true;
        }
        static bool SameAxis(IReadOnlyList<double> a, IReadOnlyList<double> b) => (a == null) == (b == null) && (a == null || Turn(a, b) <= AxisToleranceDegrees);
        // An enabled mask holds one registration per modifier, each at its holder with its direction; a disabled mask
        // holds none.
        static string Stale(GroundMaskState mask) {
            if (mask.MissingKeys != 0) return "the terrain system no longer stores a registration of the ground mask on " + mask.Holder;
            if (!mask.Enabled) return mask.Registrations.Count == 0 ? null : "the disabled ground mask on " + mask.Holder + " still holds a registration";
            if (mask.Registrations.Count != mask.Modifiers) return "the ground mask on " + mask.Holder + " holds " + mask.Registrations.Count + " registrations instead of " + mask.Modifiers;
            foreach (var registration in mask.Registrations) {
                if (Flat(registration.Centre, mask.Centre) > CentreTolerance) return "the center of the ground mask on " + mask.Holder + " did not follow it";
                if (!SameAxis(registration.Axis, mask.Axis)) return "the rotation of the ground mask on " + mask.Holder + " did not follow it";
            }
            return null;
        }
        static double Flat(IReadOnlyList<double> a, IReadOnlyList<double> b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[2] - b[2]) * (a[2] - b[2]));
        // The angle between two ground directions, in degrees; a shape's axis has no front, so opposite directions agree.
        static double Turn(IReadOnlyList<double> a, IReadOnlyList<double> b) {
            double dot = a[0] * b[0] + a[2] * b[2], la = Math.Sqrt(a[0] * a[0] + a[2] * a[2]), lb = Math.Sqrt(b[0] * b[0] + b[2] * b[2]);
            if (la <= 0 || lb <= 0) return la <= 0 && lb <= 0 ? 0 : 180;
            return Math.Acos(Math.Min(1, Math.Abs(dot) / (la * lb))) * 180 / Math.PI;
        }
        // Advances the completion of the tracked terrain output; called every frame.
        public void Poll() => output.Poll();
        public DependentOutput Output(object target) => output.Output(target);
        public void RetryOutput(object target) => output.RetryOutput(target);
        // The edited objects whose terrain output stopped without completing, with their reports.
        public IEnumerable<KeyValuePair<object, DependentOutput>> Failures => output.Failures;
        public void Dispose() => output.Dispose();
    }
}
