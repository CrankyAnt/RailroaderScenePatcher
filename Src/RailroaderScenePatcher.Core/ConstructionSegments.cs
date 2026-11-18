// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The construction-segment relationship of a rail bridge construction: the track segments whose construction state
    // its construction controller (Game.Mow.TrackConstructionScenery) reads to show or hide the construction's parts. A
    // set of segment ids, or an explicit detach, which every capability refuses because a construction without segments
    // has no meaning. The set has no order: the controller combines the states of all its segments.
    public sealed class ConstructionSegments {
        ConstructionSegments(IReadOnlyList<string> ids, bool detached) { Ids = ids; IsDetached = detached; }
        public static readonly ConstructionSegments Detached = new ConstructionSegments(Array.Empty<string>(), true);
        public static ConstructionSegments Of(IEnumerable<string> ids) => new ConstructionSegments((ids ?? throw new ArgumentNullException(nameof(ids))).ToArray(), false);
        public IReadOnlyList<string> Ids { get; }
        public bool IsDetached { get; }
        // The ids in ordinal order, the order a scene patch file writes them in.
        public IReadOnlyList<string> Sorted => Ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        public string Display => IsDetached ? "detached" : Ids.Count == 0 ? "none" : String.Join(", ", Sorted);
        // Two settings are the same when both are omitted, both detach, or both name the same set of segments.
        public static bool Same(ConstructionSegments a, ConstructionSegments b) {
            if (a == null || b == null) return a == b;
            if (a.IsDetached || b.IsDetached) return a.IsDetached == b.IsDetached;
            return new HashSet<string>(a.Ids, StringComparer.Ordinal).SetEquals(b.Ids);
        }
        // A modification's setting replaces the set below it; an omitted setting keeps it.
        public static ConstructionSegments Overlay(ConstructionSegments under, ConstructionSegments mod) => mod ?? under;
    }
    // A scene whose capabilities can hold the construction-segment relationship; every other scene refuses it.
    public interface IConstructionSegmentScene { }
    // The game-facing operations of the construction-segment relationship.
    public interface IConstructionSegmentWorld {
        // The segment ids the construction controller of the object names now, or null when the object holds no
        // construction controller.
        IReadOnlyList<string> ConstructionSegments(object target);
        // The segment ids the controller named when the world first saw it in this load: the native set, which the scene
        // and the native map editor's track give it.
        IReadOnlyList<string> NativeConstructionSegments(object target);
        // How many loaded track segments carry the id: one for a unique segment.
        int SegmentMatches(string id);
        // Replaces the controller's segment ids and the segments it caches from them. When every id names a loaded
        // segment, the controller then shows or hides the construction's parts by their construction state, as it does
        // when it is enabled; otherwise it leaves the parts as they are, as the game's controller does when a segment is
        // missing.
        void SetConstructionSegments(object target, IReadOnlyList<string> ids);
    }
    // The checks and the write of the construction-segment relationship, which the rail bridge construction root and the
    // separate construction controller at Ela share.
    public static class ConstructionSegmentRule {
        public const string RequiredVersion = "0.5.0";
        public const string NoDetach = "The construction segments of a rail bridge cannot be detached: a construction without segments has no meaning. Select the segments of the bridge's track instead, or clear this mod's value to keep the native segments";
        public const string NoRelationship = "This object has no supported construction-segment relationship";
        internal static string Missing(string id) => "Construction segment " + id + " is missing on the loaded track; no other segment is used";
        internal static string Ambiguous(string id) => "Construction segment " + id + " is ambiguous on the loaded track; no other segment is used";
        // Refuses, before any mutation, a setting the object does not support: a detach, and a reference with a segment that
        // is missing or ambiguous on the loaded track. The native set is accepted as it stands, also with a segment that
        // no longer exists: it is what the object has without this mod.
        public static void Check(IConstructionSegmentWorld world, object target, ConstructionSegments desired) {
            if (desired == null) return;
            if (world?.ConstructionSegments(target) == null) throw new InvalidPatchDefinitionException(NoRelationship);
            if (desired.IsDetached) throw new InvalidPatchDefinitionException(NoDetach);
            var native = world.NativeConstructionSegments(target);
            if (native != null && ConstructionSegments.Same(desired, ConstructionSegments.Of(native))) return;
            foreach (var id in desired.Sorted) {
                var matches = world.SegmentMatches(id);
                if (matches == 0) throw new InvalidPatchDefinitionException(Missing(id));
                if (matches > 1) throw new InvalidPatchDefinitionException(Ambiguous(id));
            }
        }
        // The object's construction segments now, or null when it holds no construction controller.
        public static ConstructionSegments Read(IConstructionSegmentWorld world, object target) {
            var ids = world?.ConstructionSegments(target);
            return ids == null ? null : ConstructionSegments.Of(ids);
        }
        // Writes the set when it differs from the current one; repeated application writes nothing.
        public static void Apply(IConstructionSegmentWorld world, object target, ConstructionSegments desired) {
            if (desired == null || world == null) return;
            if (desired.IsDetached) throw new InvalidOperationException(NoDetach);
            if (ConstructionSegments.Same(Read(world, target), desired)) return;
            world.SetConstructionSegments(target, desired.Sorted);
        }
        // Why the object's construction segments differ from the setting, or null.
        public static string Verify(IConstructionSegmentWorld world, object target, ConstructionSegments desired) {
            if (desired == null || world == null) return null;
            var now = Read(world, target);
            return ConstructionSegments.Same(now, desired) ? null : "the construction segments read " + (now?.Display ?? "none") + " instead of " + desired.Display;
        }
    }
    // The game-facing operations of a construction controller that stands on an object of its own, such as Ela Bridge
    // Scenery, which switches the steel and abutments of Ela Bridge.
    public interface IConstructionSceneryWorld : IConstructionSegmentWorld {
        IEnumerable<string> MapIds { get; }
        // The loaded objects at the scene and path that carry such a controller.
        IReadOnlyList<object> Find(string scene, string path);
        // Why the controller cannot be edited now, or null.
        string Refusal(object controller);
        // The controller object's placement in map coordinates and its own activity, which no edit changes.
        Placement Placement(object controller);
        bool Active(object controller);
    }
    // A construction controller on an object of its own: its construction segments are its only supported edit. Its
    // position has no effect in the game, and its activity runs the construction's visibility control, so a placement or
    // an Enabled setting that differs from the object's own is refused before any mutation.
    public sealed class ConstructionSceneryScene : IScene, IRelationshipScene, IVersionedScene, IConstructionSegmentScene {
        public const string Structure = "bridge-construction-scenery-v1", ComponentType = "Game.Mow.TrackConstructionScenery";
        public const string NoPlacement = "The position of a construction controller has no effect in the game: it switches the bridge's parts wherever it stands. Move the bridge or its parts instead";
        public const string NoActivity = "The construction's visibility control runs on this controller: disabling it would stop the game from switching the bridge's parts by construction progress. Disable the bridge or its parts instead";
        readonly IConstructionSceneryWorld world;
        public ConstructionSceneryScene(IConstructionSceneryWorld world, Capability capability = Capability.Unchecked) { this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != ComponentType || (identity.Structure != null && identity.Structure != Structure)) return Array.Empty<object>();
            return world.Find(identity.Scene, identity.Path) ?? Array.Empty<object>();
        }
        public void Preflight(object target) {
            var refusal = world.Refusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            if (world.ConstructionSegments(target) == null) throw new InvalidOperationException("The object no longer holds a construction controller");
        }
        public void CheckRelationships(object target, ObjectState state) {
            if (state?.Marker != null) throw new InvalidOperationException("This object has no supported track-marker relationship");
            if (state?.Coverage != null) throw new InvalidOperationException("This object has no supported crossing-coverage relationship");
            if (state?.Equipment != null) throw new InvalidOperationException("This object has no supported crossing-equipment relationship");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
            // The object's own placement and activity, which a complete desired state carries, change nothing.
            var placement = state?.Placement;
            if (placement != null && (placement.Following != null || !SameMap(placement, world.Placement(target)))) throw new InvalidPatchDefinitionException(NoPlacement);
            if (state?.Active != null && state.Active != world.Active(target)) throw new InvalidPatchDefinitionException(NoActivity);
            ConstructionSegmentRule.Check(world, target, state?.Segments);
        }
        static bool SameMap(Placement a, Placement b) => b?.Position != null && a.Position != null && a.Rotation != null && b.Rotation != null
            && a.Position.Zip(b.Position, (x, y) => Math.Abs(x - y)).All(d => d <= 0.01) && Math.Abs(Math.Abs(a.Rotation.Zip(b.Rotation, (x, y) => x * y).Sum()) - 1) <= 1e-6;
        public object Capture(object target) => ConstructionSegmentRule.Read(world, target);
        public ObjectState Read(object target) => new ObjectState(world.Placement(target), world.Active(target)).WithSegments(ConstructionSegmentRule.Read(world, target));
        public void Apply(object target, ObjectState state) {
            ConstructionSegmentRule.Apply(world, target, state?.Segments);
            var problem = ConstructionSegmentRule.Verify(world, target, state?.Segments);
            if (problem != null) throw new InvalidOperationException("Applied construction segments did not verify: " + problem);
        }
        public bool Restore(object target, object snapshot) {
            try {
                var captured = (ConstructionSegments)snapshot;
                ConstructionSegmentRule.Apply(world, target, captured);
                return ConstructionSegmentRule.Verify(world, target, captured) == null;
            } catch { return false; }
        }
        public string MinimumPatcherVersion(TargetIdentity target) => target != null && (target.Structure == Structure || (target.Structure == null && target.Component == ComponentType)) ? ConstructionSegmentRule.RequiredVersion : null;
    }
}
