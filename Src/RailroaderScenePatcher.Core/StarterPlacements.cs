// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // One new-game car placement of a loaded setup: the setup's identifier and whether it is active, the placement's
    // place among the setup's placements, the marker it holds, the cars of its cut and its wreck setting. The game takes
    // an active setup at a new game and reads each placement's marker for its track location.
    public sealed class StarterCarPlacement {
        public string Setup { get; }
        public bool SetupActive { get; }
        // Zero-based place of the placement in its setup, and the number of placements the setup has.
        public int Index { get; }
        public int Count { get; }
        public object Marker { get; }
        public IReadOnlyList<string> Cars { get; }
        public bool Wreck { get; }
        public StarterCarPlacement(string setup, bool setupActive, int index, int count, object marker, IEnumerable<string> cars, bool wreck) {
            Setup = setup; SetupActive = setupActive; Index = index; Count = count; Marker = marker;
            Cars = (cars ?? Enumerable.Empty<string>()).ToArray(); Wreck = wreck;
        }
    }
    // Whether the game reads a marker as a starter placement marker. Used: an active setup's car placement holds it.
    // Unused: only inactive setups hold it, so the game never reads it. None: no setup holds it, and it is no starter
    // placement marker.
    public enum StarterUse { None, Used, Unused }
    // The car placements of every loaded setup, as one discovery read them.
    public sealed class StarterSetups {
        readonly StarterCarPlacement[] placements;
        public StarterSetups(IEnumerable<StarterCarPlacement> placements) {
            this.placements = (placements ?? Enumerable.Empty<StarterCarPlacement>()).Where(p => p != null && p.Marker != null).ToArray();
        }
        // Setups that could not be read. No marker can be told to be a starter placement marker then: every marker keeps
        // its own family, and the inventory's reading of each marker carries a note that says why.
        public static StarterSetups Unreadable(string error) => new StarterSetups(null) { Error = String.IsNullOrEmpty(error) ? "unknown error" : error };
        // Why the setups could not be read, or null.
        public string Error { get; private set; }
        // What the inventory takes from the setups for one marker: what the game needs from its binding, the setups and
        // car placements that read it, and a note when the setups could not be read.
        public StarterReading Reading(object marker, string markerType, bool crossingPad) =>
            new StarterReading(BindingRules.Need(markerType, crossingPad, Use(marker) == StarterUse.Used), Readers(marker), Error == null ? null : StarterPlacements.Unreadable(Error));
        public StarterUse Use(object marker) {
            if (marker == null) return StarterUse.None;
            var holding = placements.Where(p => ReferenceEquals(p.Marker, marker)).ToArray();
            return holding.Length == 0 ? StarterUse.None : holding.Any(p => p.SetupActive) ? StarterUse.Used : StarterUse.Unused;
        }
        // The family a marker is listed in: Starter placement when it is used, none for an unused one, which stays out
        // of the inventory, and its own family when no setup holds it.
        public string Family(object marker, string otherwise) {
            switch (Use(marker)) {
                case StarterUse.Used: return StarterPlacements.Family;
                case StarterUse.Unused: return null;
                default: return otherwise;
            }
        }
        // Every car placement of an active setup that reads the marker, one line each, in the order they were read:
        // the setup, the placement's place in it, its cars and its wreck setting.
        public IReadOnlyList<string> Readers(object marker) =>
            marker == null ? Array.Empty<string>() : placements.Where(p => p.SetupActive && ReferenceEquals(p.Marker, marker)).Select(StarterPlacements.Reader).ToArray();
    }
    public sealed class StarterReading {
        public BindingNeed Need { get; }
        public IReadOnlyList<string> Readers { get; }
        // An inspection note (BindingRules.IsNote), or null.
        public string Note { get; }
        public StarterReading(BindingNeed need, IReadOnlyList<string> readers, string note) { Need = need; Readers = readers ?? Array.Empty<string>(); Note = note; }
    }
    public static class StarterPlacements {
        public const string Family = "Starter placement";
        // The inspection note of a marker, and the Refresh's report, when the setups could not be read.
        public static string Unreadable(string error) => "Inspection unavailable: the new-game setups could not be read, so starter placement markers cannot be told apart: " + error;
        // The start of a reader line: the inspector shows these lines under their own heading.
        public const string ReaderPrefix = "Read by setup ";
        // Why a starter placement marker has no Focus target: its track location does not resolve on the loaded track.
        public const string NoTrackPoint = "the marker's track location does not resolve to a track point";
        // What Supported edits says for a used marker. The track location and the wreck setting are the starter
        // placement workflow's own edits; the object's placement and activity do not reach the car placement.
        public const string Operations = "Not editable: a new-game car placement reads only this marker's track location, so the object's placement and activity do not change where its cars start; editing the track location and the wreck setting is not available in this version";
        // Why the Generic track-marker capability leaves a used marker alone: its track location is edited in the
        // starter placement workflow, together with the whole car cut.
        public const string MarkerCapabilityRefusal = "This is a starter placement marker: a new-game car placement reads its track location, which belongs to the starter placement workflow";
        public static string Reader(StarterCarPlacement placement) =>
            ReaderPrefix + placement.Setup + ", car placement " + (placement.Index + 1) + " of " + placement.Count + ": cars "
            + (placement.Cars.Count == 0 ? "none" : String.Join(", ", placement.Cars)) + "; wreck: " + (placement.Wreck ? "yes" : "no");
    }
}
