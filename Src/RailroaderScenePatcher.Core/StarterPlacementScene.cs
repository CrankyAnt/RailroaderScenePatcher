// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The game-facing operations of the starter placement capability: the marker operations of a track marker, the
    // new-game setups that read markers, the cars' lengths and the track as the game's car placement rule walks it.
    public interface IStarterPlacementWorld : ITrackMarkerWorld, IStarterTrack {
        // The car placements of every loaded setup, read now.
        StarterSetups Setups();
        // The marker a setup's car placement holds for the object.
        object Marker(object target);
        // The serialized track location of a marker a car placement holds, or null when it names no segment.
        TrackLocation PlacementLocation(object marker);
        // The length of the cut the game builds for these car identifiers, tenders included, with the gaps between the
        // cars. Throws when a car is unknown.
        double CutLength(IReadOnlyList<string> cars);
    }
    // Places a starter placement marker on the track: the marker's track location, which a new-game car placement reads
    // as the front of its car cut, together with its graph registration. The object's placement and activity play no
    // part in where the cars start, so a starter placement leaves them alone. A new location must resolve on the loaded
    // track and must let the whole cut, with the cuts of the setup's other placements, pass the game's placement rule.
    public sealed class StarterPlacementScene : IScene, IRelationshipScene, IVersionedScene, ITargetConfirmation, IPlannedStateScene {
        public const string Structure = "starter-placement-v1", RequiredVersion = "0.5.0";
        readonly IStarterPlacementWorld world;
        readonly MarkerRelationship relationship;
        public StarterPlacementScene(IStarterPlacementWorld world, Capability capability) {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            relationship = new MarkerRelationship(world, Structure, null); Capability = capability;
        }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        // The track locations the scene patch file being checked sets for other markers, by the marker a car placement holds. The
        // whole-cut check judges those markers' cuts there, so the result does not depend on which modification runs first.
        Dictionary<object, TrackLocation> planned = new Dictionary<object, TrackLocation>();
        public void Plan(IReadOnlyDictionary<object, ObjectState> desired) {
            var next = new Dictionary<object, TrackLocation>();
            if (desired != null)
                foreach (var pair in desired) {
                    var location = pair.Value?.Starter?.Track?.Location;
                    if (location == null) continue;
                    try { next[world.Marker(pair.Key)] = location; } catch (Exception) { }
                }
            planned = next;
        }
        // Only an identity that names this capability's structure: a starter placement marker is also a track marker.
        public IReadOnlyList<object> Resolve(TargetIdentity identity) => identity.Structure == Structure ? relationship.Resolve(identity) : Array.Empty<object>();
        public bool StillIdentifies(object target, TargetIdentity identity) => identity.Structure == Structure && relationship.StillIdentifies(target, identity);
        public string MinimumPatcherVersion(TargetIdentity target) => target.Structure == Structure ? RequiredVersion : null;
        public void Preflight(object target) => relationship.Preflight(target);
        public object Capture(object target) => relationship.Capture(target);
        public bool Restore(object target, object snapshot) => relationship.Restore(target, snapshot);
        public ObjectState Read(object target) {
            var setups = world.Setups();
            var reader = setups.Reader(world.Marker(target), out _);
            return new ObjectState(new Placement(world.Position(target), world.Rotation(target)), world.Active(target), null, null,
                new StarterSetting(reader?.Setup, MarkerBinding.Of(world.Location(target))));
        }
        // Writes the track location with its registration; the object keeps its placement and activity.
        public void Apply(object target, ObjectState state) {
            var track = state?.Starter?.Track;
            if (track != null && track.IsDetached) throw new InvalidPatchDefinitionException(StarterPlacements.NoDetach);
            relationship.Apply(target, new ObjectState(null, null, track ?? MarkerBinding.Of(world.Location(target))));
        }
        public void CheckRelationships(object target, ObjectState state) {
            if (state == null) return;
            if (state.Placement != null || state.Active != null) throw new InvalidPatchDefinitionException(StarterPlacements.ObjectSettings);
            if (state.Marker != null || state.Coverage != null) throw new InvalidPatchDefinitionException(StarterPlacements.OtherRelationships);
            var starter = state.Starter;
            if (starter == null) return;
            var setups = world.Setups();
            var marker = world.Marker(target);
            var reader = setups.Reader(marker, out var refusal);
            if (reader == null) throw new InvalidPatchDefinitionException(refusal);
            if (starter.Setup != reader.Setup)
                throw new InvalidPatchDefinitionException("The starter placement names setup " + starter.Setup + ", but the car placement that reads this marker is " + StarterPlacements.Place(reader));
            var track = starter.Track;
            if (track == null) return;
            if (track.IsDetached) throw new InvalidPatchDefinitionException(StarterPlacements.NoDetach);
            var unresolved = Unresolved(track.Location);
            if (unresolved != null) throw new InvalidPatchDefinitionException(unresolved);
            var problem = CutProblem(setups, reader, marker, track.Location);
            if (problem != null) throw new InvalidPatchDefinitionException(problem);
            // The marker rules before any mutation, such as the refusal for a holder of a duplicate marker id, whose
            // registration the game removes by marker id from every holder.
            relationship.CheckRelationships(target, new ObjectState(null, null, track));
        }
        // Why a track location cannot be the marker's, or null: its segment must exist exactly once and contain the distance.
        // No other segment is ever used instead.
        string Unresolved(TrackLocation location) {
            if (location.End != "a" && location.End != "b") return "The track location's end must be \"a\" or \"b\"";
            var lengths = world.SegmentLengths(location.Segment) ?? Array.Empty<double>();
            if (lengths.Count == 0) return "The track location's segment " + location.Segment + " is missing from the loaded track; no other segment is used";
            if (lengths.Count > 1) return "The track location's segment " + location.Segment + " is ambiguous on the loaded track; no other segment is used";
            if (Double.IsNaN(location.Distance) || location.Distance < 0 || location.Distance > lengths[0] + TrackLocation.DistanceTolerance)
                return "The track location's distance " + location.Distance.ToString("0.###", CultureInfo.InvariantCulture) + " m lies outside segment " + location.Segment + ", which is "
                    + lengths[0].ToString("0.###", CultureInfo.InvariantCulture) + " m long";
            return null;
        }
        // The whole cut at the new location, with the setup's other cuts at their markers' current locations, under the
        // game's placement rule.
        string CutProblem(StarterSetups setups, StarterCarPlacement reader, object marker, TrackLocation location) {
            var cuts = new List<StarterCut>();
            foreach (var placement in setups.Placements(reader.Setup)) {
                var own = ReferenceEquals(placement.Marker, marker);
                double length;
                try { length = world.CutLength(placement.Cars); }
                catch (Exception e) {
                    // The game stops the setup at a car it does not know; the cut of another placement then cannot be judged.
                    if (own) return "The whole car cut cannot be checked: the length of " + StarterPlacements.Place(placement) + " is unavailable: " + e.GetBaseException().Message;
                    continue;
                }
                TrackLocation front;
                try { front = own ? location : planned.TryGetValue(placement.Marker, out var set) ? set : world.PlacementLocation(placement.Marker); }
                catch (Exception) { front = null; }
                cuts.Add(new StarterCut(placement, front, length));
            }
            return StarterCutRule.Problem(world, cuts, marker);
        }
        // The positions of the cut that the marker's car placement places at the location, front first, every half
        // metre: what the world shows for the inspected marker. Empty when the cut's length or path is unavailable.
        public IReadOnlyList<double[]> CutPath(object target, TrackLocation location) {
            if (location == null) return Array.Empty<double[]>();
            var reader = world.Setups().Reader(world.Marker(target), out _);
            if (reader == null) return Array.Empty<double[]>();
            return StarterCutRule.Body(world, location, world.CutLength(reader.Cars));
        }
    }
}
