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
    // One scene ground mask (a component in Map.Runtime.MaskComponents) at or below a top scene object or part, observed with
    // copied values. Positions are map coordinates.
    public sealed class GroundMaskState {
        public GroundMaskState(string id, string holder, bool enabled, bool flattens, IEnumerable<double> centre, IEnumerable<double> axis, int modifiers, IEnumerable<GroundMaskRegistration> registrations, int missingKeys, IEnumerable<double> expected = null) {
            Expected = expected?.ToArray();
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
        // The centre a registration made now, at the holder's current pose, would hold, or null when it can't be read.
        // A rectangle or circle shape holds its holder's place; a curve holds the centre of its bounds, which its shape,
        // a turn, a pitch or a roll moves away from its holder.
        public IReadOnlyList<double> Expected { get; }
        // How many terrain modifiers the component makes when it registers.
        public int Modifiers { get; }
        public IReadOnlyList<GroundMaskRegistration> Registrations { get; }
        // Keys the component holds that the terrain system no longer stores.
        public int MissingKeys { get; }
    }
    // The scene ground masks of scene objects as the game holds them, and the terrain system they register in.
    public interface IGroundMaskWorld : ITerrainWorld {
        // The scene ground masks at and below a top scene object or part, in a stable order.
        IReadOnlyList<GroundMaskState> Masks(object target);
        // Makes the mask register again where its holder stands now, through the game's own rebuild.
        void Rebuild(object target, string id);
    }
    // Scene ground masks of scene objects with child objects. A mask registers its shape in the game's terrain system where its
    // holder stood when it was enabled; after a move of the holder, the mask is rebuilt so that its registration follows
    // (for a mask that flattens, a change of height alone too),
    // and the game regenerates terrain and vegetation at the old and the new place. A mask that flattens the terrain
    // height follows the same way: the game rebuilds a tile from its base heights and the masks registered there
    // (Railroader 0.6.3, MapMaskBase.ApplyModifiers, MaskComponentBase.OnDisable, TerrainBuilder.TerraformCore). That output is tracked by the
    // edited object. The group and part capabilities of one scene share one instance.
    public sealed class GroundMasks : IDisposable {
        const double CentreTolerance = 0.1, AxisToleranceDegrees = 0.1;
        readonly IGroundMaskWorld world;
        readonly TerrainOutput output;
        // Whether a holder with these component types is a location sign: a renderer culler and a circle mask on one
        // object without a track component, such as a Loc: location sign. The board family rebuilds that mask itself
        // when the board is the edited target.
        public static bool HeldByBoard(IEnumerable<string> components) {
            var types = components?.Where(type => type != null).ToArray();
            return types != null && types.Contains(LocationSigns.CullerType) && types.Contains(CircleMask) && !types.Any(type => type.StartsWith("Track.", StringComparison.Ordinal));
        }
        const string CircleMask = "Map.Runtime.MaskComponents.CircleMapMask";
        // Whether an edit of a family that moves its holders' masks moves this mask. heldByTarget: the edited object holds
        // the mask itself. A mask follows its holder whichever edit moves it, so a board carried along below a moved
        // object moves its mask with that edit; only a board that is the edited target leaves its mask to the board
        // family, so each mask is handled once.
        public static bool MovedByEdit(bool heldByTarget, IEnumerable<string> holderComponents) => !(heldByTarget && HeldByBoard(holderComponents));
        public GroundMasks(IGroundMaskWorld world) { this.world = world ?? throw new ArgumentNullException(nameof(world)); output = new TerrainOutput(world); }
        internal IReadOnlyList<GroundMaskState> Observe(object scope) => world.Masks(scope) ?? Array.Empty<GroundMaskState>();
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
                    if (mask.Modifiers != 0) output.Track(target, (mask.Expected ?? mask.Centre).ToArray());
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
            if (Distance(now.Flattens, was.Centre, now.Centre) > CentreTolerance || !SameAxis(was.Axis, now.Axis)) return false;
            for (var i = 0; i < now.Registrations.Count; i++)
                if (Distance(now.Flattens, was.Registrations[i].Centre, now.Registrations[i].Centre) > CentreTolerance || !SameAxis(was.Registrations[i].Axis, now.Registrations[i].Axis)) return false;
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
                // Like is compared with like: the registration with the one a registration made at the holder's
                // current pose would hold. Without that, the holder's place counts on the ground, and the height is
                // not judged.
                if (Flat(registration.Centre, mask.Expected ?? mask.Centre) > CentreTolerance) return "wrong center position for " + mask.Holder;
                if (mask.Flattens && mask.Expected != null && Math.Abs(registration.Centre[1] - mask.Expected[1]) > CentreTolerance) return "wrong height for " + mask.Holder;
                if (!SameAxis(registration.Axis, mask.Axis)) return "wrong rotation for " + mask.Holder;
            }
            return null;
        }
        // How far apart two centres of one mask are: on the ground, and with height when withHeight, for a mask that
        // flattens, since the game sets the terrain height to the height its registration holds
        // (RectangleMapMask.MakeMaskDescriptor passes transform.position, and MapMaskBase.ApplyModifiers gives that shape
        // to its HeightmapModifier). A mask that does not flatten makes no output that depends on its height.
        static double Distance(bool withHeight, IReadOnlyList<double> a, IReadOnlyList<double> b) =>
            withHeight ? Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2])) : Flat(a, b);
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
    // The scene ground masks at and below the objects of one capability whose holder the patcher moves as a single
    // object, such as a bridge, a milepost, a crossing pad, a station map icon, a spawn point or a marker object: every
    // write is followed by the masks' rebuild where the object stands now, and the terrain output is tracked under the
    // object. Without masks, as in a world without them, writes go through unchanged.
    internal sealed class MaskFollowing {
        GroundMasks masks;
        internal MaskFollowing(GroundMasks masks) { this.masks = masks; }
        // Gives a capability built before the world's ground masks existed, as for a new game's start, those masks. A
        // capability that already has masks keeps them.
        internal void Use(GroundMasks value) { if (masks == null) masks = value; }
        internal void Write(object target, Action change) { var before = masks?.Observe(target); change(); masks?.Follow(target, target, before); }
        // A restoration that verified its own state verifies the masks too: each stands at its holder again.
        internal bool Restore(object target, Func<bool> restore) {
            var before = masks?.Observe(target);
            if (!restore()) return false;
            try { masks?.Follow(target, target, before); return true; } catch { return false; }
        }
        internal DependentOutput Output(object target) => masks?.Output(target) ?? DependentOutput.None;
        internal void RetryOutput(object target) => masks?.RetryOutput(target);
    }
}
