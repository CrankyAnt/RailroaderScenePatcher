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
        // The wreck setting: read from the game's car placement now when the placement can read it, so a placement read
        // before a wreck edit shows the edited setting; otherwise as it was read.
        public bool Wreck => currentWreck != null ? currentWreck() : wreck;
        readonly bool wreck;
        readonly Func<bool> currentWreck;
        // The game's own car placement, whose wreck setting the starter placement capability writes; null when unknown.
        public object Handle { get; }
        public StarterCarPlacement(string setup, bool setupActive, int index, int count, object marker, IEnumerable<string> cars, bool wreck, object handle = null, Func<bool> currentWreck = null) {
            Setup = setup; SetupActive = setupActive; Index = index; Count = count; Marker = marker;
            Cars = (cars ?? Enumerable.Empty<string>()).ToArray(); this.wreck = wreck; Handle = handle; this.currentWreck = currentWreck;
        }
    }
    // Whether the game reads a marker as a starter placement marker. Used: an active setup's car placement holds it.
    // Unused: only inactive setups hold it, so the game never reads it. None: no setup holds it; below Setups it is a
    // starter placement marker that no setup uses, elsewhere no starter placement marker.
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
        // The family a marker is listed in: Starter placement marker when it is used, none for an unused one, which stays out
        // of the inventory, and its own family when no setup holds it. A marker below Setups (inSetups) that no setup holds
        // is a starter placement marker that no setup uses: it stays out of the inventory too. Setups that could not be
        // read leave every marker its own family.
        public string Family(object marker, string otherwise, bool inSetups = false) {
            switch (Use(marker)) {
                case StarterUse.Used: return StarterPlacements.Family;
                case StarterUse.Unused: return null;
                default: return inSetups && Error == null ? null : otherwise;
            }
        }
        // Every car placement of an active setup that reads the marker, one line each, in the order they were read:
        // the setup, the placement's place in it, its cars and its wreck setting.
        public IReadOnlyList<string> Readers(object marker) => Reading(marker).Select(StarterPlacements.Reader).ToArray();
        // Every car placement of an active setup that reads the marker, in the order they were read.
        public IReadOnlyList<StarterCarPlacement> Reading(object marker) =>
            marker == null ? Array.Empty<StarterCarPlacement>() : placements.Where(p => p.SetupActive && ReferenceEquals(p.Marker, marker)).ToArray();
        // The car placements of an active setup, in the order the game places them.
        public IReadOnlyList<StarterCarPlacement> Placements(string setup) =>
            placements.Where(p => p.SetupActive && p.Setup == setup).OrderBy(p => p.Index).ToArray();
        // The one car placement that reads the marker, or why there is not exactly one. A starter placement changes one
        // car placement, so a marker that no car placement or several read takes no starter placement.
        public StarterCarPlacement Reader(object marker, out string refusal) {
            refusal = null;
            if (Error != null) { refusal = StarterPlacements.Unreadable(Error); return null; }
            var readers = Reading(marker);
            if (readers.Count == 1) return readers[0];
            refusal = readers.Count == 0 ? StarterPlacements.NoReader
                : StarterPlacements.SeveralReaders(readers.Select(StarterPlacements.Place).ToArray());
            return null;
        }
    }
    // A starter placement of a starter placement marker: the setup whose new-game car placement reads the marker, the
    // marker's track location, the front of that placement's car cut, and the placement's wreck setting, whether the
    // game places the cut derailed. A null track or wreck leaves that setting untouched; a detached track is refused,
    // because the game reads a used marker's track location.
    public sealed class StarterSetting {
        public StarterSetting(string setup, MarkerBinding track) : this(setup, track, null) { }
        public StarterSetting(string setup, MarkerBinding track, bool? wreck) { Setup = setup; Track = track; Wreck = wreck; }
        public string Setup { get; }
        public MarkerBinding Track { get; }
        public bool? Wreck { get; }
        // The modification's values laid over the complete state below.
        internal static StarterSetting Overlay(StarterSetting under, StarterSetting mod) =>
            mod == null ? under : under == null ? mod : new StarterSetting(mod.Setup ?? under.Setup, mod.Track ?? under.Track, mod.Wreck ?? under.Wreck);
        // Two states agree when the track location and the wreck setting agree; the setup identifies the car placement
        // and is no state.
        internal static bool Same(StarterSetting a, StarterSetting b) => a == null ? b == null : b != null && MarkerBinding.Same(a.Track, b.Track) && a.Wreck == b.Wreck;
    }
    // Why the game could not move along the track or place a car: the game's own reason, such as "Not enough track."
    public sealed class StarterTrackException : Exception {
        public StarterTrackException(string reason) : base(reason) { }
    }
    // A scene that judges one modification together with the other modifications of its scene patch file: before a scene patch file's
    // modifications are checked, the engine and the session give it the desired state of every object the scene patch file's
    // other modifications set, and null afterwards.
    public interface IPlannedStateScene {
        void Plan(IReadOnlyDictionary<object, ObjectState> desired);
    }
    // The track as the game's car placement rule walks it. Positions are map coordinates.
    public interface IStarterTrack {
        // The location reached by moving the distance along the track from a location: a positive distance moves away from
        // the location's end, a negative one towards it and on through the node at that end. A switch lined against the
        // movement or the end of the track throws StarterTrackException with the game's reason.
        TrackLocation Move(TrackLocation from, double distance);
        // Why the game cannot place a car at the location, such as "Track not available.", or null.
        string Unplaceable(TrackLocation at);
        double[] Position(TrackLocation at);
    }
    // One car placement's cut as the game places it: its front at the marker's track location and its length, the length
    // of its cars with the gaps between them. A null front is a marker whose track location does not resolve.
    public sealed class StarterCut {
        public StarterCut(StarterCarPlacement placement, TrackLocation front, double length) { Placement = placement; Front = front; Length = length; }
        public StarterCarPlacement Placement { get; }
        public TrackLocation Front { get; }
        public double Length { get; }
    }
    // The game's placement rule for a new-game car cut (TrainController.CanPlaceAt): from the front, every 1.5 m back
    // over the cut's length and then at its back end, the location must be on available track and clear of every car
    // within 0.75 m. Moving back stops at the end of the track or a switch lined against the movement. The setup places
    // its cuts in order, so a cut must clear the cuts placed before it. A cut that fails stops the whole setup.
    public static class StarterCutRule {
        public const double Step = 1.5, CarRadius = 0.75, CarHalfWidth = 1.0, MaxVerticalSeparation = 4.0, BodyStep = 0.5;
        // The locations the game checks for a cut, front first. Throws StarterTrackException where moving back fails.
        // This is TrainController.CanPlaceAt(Location, float, ...) of Railroader 0.6.2 build 26335745 step by step, in
        // single precision as the game computes it: the remainder is the length less its whole 1.5 m steps; the loop
        // checks the location and moves 1.5 m back while the walked distance plus a step stays below the length; the
        // last location moves back by the remainder and is checked again. For a length of whole steps the remainder is 0,
        // so the game checks its last step twice and never the back end; this rule does the same.
        public static IReadOnlyList<TrackLocation> Checked(IStarterTrack track, TrackLocation front, double length) {
            var found = new List<TrackLocation>();
            var at = front;
            var distance = (float)length;
            var rest = distance - (float)Math.Floor(distance / 1.5f) * 1.5f;
            for (var done = 0f; done < distance; done += 1.5f) {
                found.Add(at);
                if (done + 1.5f < distance) at = track.Move(at, -1.5f);
            }
            found.Add(track.Move(at, -rest));
            return found;
        }
        // The positions along a cut from its front to its back, every half metre: where its cars stand once placed. A
        // path that cannot be walked to its end stops there, and the error says why; it is null for a whole path.
        public static IReadOnlyList<double[]> Body(IStarterTrack track, TrackLocation front, double length, out string error) {
            var points = new List<double[]>();
            error = null;
            if (front == null) { error = "the marker's track location does not resolve to a track point."; return points; }
            try {
                var at = front;
                points.Add(track.Position(at));
                for (var done = 0.0; done < length; done += BodyStep) {
                    at = track.Move(at, -Math.Min(BodyStep, length - done));
                    points.Add(track.Position(at));
                }
            } catch (StarterTrackException e) { error = PatcherLog.Reason(e); }
            return points;
        }
        public static IReadOnlyList<double[]> Body(IStarterTrack track, TrackLocation front, double length) => Body(track, front, length, out _);
        // Whether a car of a placed cut stands within the game's check radius of the point: within half a car's width
        // plus the radius beside the cut's path, or within the radius of its ends, and at most 4 m above or below.
        static bool Blocks(IReadOnlyList<double[]> body, double[] point) {
            for (var i = 0; i < body.Count; i++) {
                var a = body[i];
                if (Math.Abs(point[1] - a[1]) > MaxVerticalSeparation) continue;
                if (Horizontal(point, a) <= CarRadius) return true;
                if (i + 1 == body.Count) continue;
                var b = body[i + 1];
                double dx = b[0] - a[0], dz = b[2] - a[2], length = dx * dx + dz * dz;
                if (length <= 0) continue;
                var t = ((point[0] - a[0]) * dx + (point[2] - a[2]) * dz) / length;
                if (t < 0 || t > 1) continue;
                double px = a[0] + t * dx - point[0], pz = a[2] + t * dz - point[2];
                if (Math.Sqrt(px * px + pz * pz) <= CarHalfWidth + CarRadius) return true;
            }
            return false;
        }
        static double Horizontal(double[] a, double[] b) { double dx = a[0] - b[0], dz = a[2] - b[2]; return Math.Sqrt(dx * dx + dz * dz); }
        // Why the cut fails the rule, or null: the game's reason, or the earlier cut it runs into.
        static string Failure(IStarterTrack track, StarterCut cut, IReadOnlyList<KeyValuePair<StarterCut, IReadOnlyList<double[]>>> placed, out StarterCut blocker) {
            blocker = null;
            if (cut.Front == null) return "the marker's track location does not resolve to a track point.";
            try {
                foreach (var at in Checked(track, cut.Front, cut.Length)) {
                    var unplaceable = track.Unplaceable(at);
                    if (unplaceable != null) return unplaceable;
                    var point = track.Position(at);
                    foreach (var other in placed)
                        if (Blocks(other.Value, point)) { blocker = other.Key; return "Blocked by the cut of " + StarterPlacements.Place(other.Key.Placement) + "."; }
                }
            } catch (StarterTrackException e) { return PatcherLog.Reason(e); }
            return null;
        }
        // Why the setup's cuts fail the rule where the marker's cut takes part, or null: the marker's own cut fails, or
        // another cut runs into it. A failure of another cut that the marker's cut does not cause belongs to that cut's
        // own marker. Each cut is checked against the cuts before it that passed.
        public static string Problem(IStarterTrack track, IReadOnlyList<StarterCut> cuts, object marker) {
            var placed = new List<KeyValuePair<StarterCut, IReadOnlyList<double[]>>>();
            foreach (var cut in cuts) {
                var failure = Failure(track, cut, placed, out var blocker);
                if (failure == null) {
                    // A cut that passed has a body the later cuts must clear; one that cannot be followed cannot be judged.
                    var body = Body(track, cut.Front, cut.Length, out var unwalked);
                    if (unwalked == null) { placed.Add(new KeyValuePair<StarterCut, IReadOnlyList<double[]>>(cut, body)); continue; }
                    failure = "the track under the cut cannot be followed: " + unwalked;
                }
                if (ReferenceEquals(cut.Placement.Marker, marker))
                    return "The car cut of " + StarterPlacements.Place(cut.Placement) + " (" + Cars(cut) + ") does not fit with its front at " + cut.Front?.Display + ": " + failure;
                if (blocker != null && ReferenceEquals(blocker.Placement.Marker, marker))
                    return "This car cut blocks the cut of " + StarterPlacements.Place(cut.Placement) + " (" + Cars(cut) + "), which the setup places after it with its front at " + cut.Front?.Display + ": " + failure;
            }
            return null;
        }
        static string Cars(StarterCut cut) => (cut.Placement.Cars.Count == 0 ? "no cars" : String.Join(", ", cut.Placement.Cars)) + ", " + ShownNumber.Text(cut.Length) + " m";
    }
    public sealed class StarterReading {
        public BindingNeed Need { get; }
        public IReadOnlyList<string> Readers { get; }
        // An inspection note (BindingRules.IsNote), or null.
        public string Note { get; }
        public StarterReading(BindingNeed need, IReadOnlyList<string> readers, string note) { Need = need; Readers = readers ?? Array.Empty<string>(); Note = note; }
    }
    public static class StarterPlacements {
        public const string Family = "Starter placement marker";
        // The inspection note of a marker, and the Refresh's report, when the setups could not be read.
        public static string Unreadable(string error) => "Inspection unavailable: " + UnreadableReason(error);
        // The reason of Unreadable without its label, as a tooltip gives it after its own label.
        public static string UnreadableReason(string error) => "the new-game setups could not be read, so starter placement markers cannot be identified: " + error;
        // The start of a reader line: the inspector shows these lines under their own heading.
        public const string ReaderPrefix = "Read by setup ";
        // Why a starter placement marker has no Focus target: its track location does not resolve on the loaded track.
        public const string NoTrackPoint = "the marker's track location does not resolve to a track point";
        // What Supported edits says for a used marker. The track location is the starter placement workflow's own edit;
        // the object's placement and activity do not reach the car placement.
        public const string Operations = "Track location: place the marker on the track under Connections. Once the whole car cut passes the game's placement rule, the new-game setup places its front there. Start derailed: choose under Connections whether the cut starts derailed as a wreck. The object's placement and activity do not change where its cars start.";
        // Why the object's placement and activity cannot be edited: what the Placement tab's modes and the activity
        // buttons say, and what a starter placement modification with placement or active fails with.
        public const string ObjectSettings = "A new-game car placement reads only this marker's track location, so the object's placement and activity do not change where its cars start; place the marker on the track under Connections";
        public const string OtherRelationships = "A starter placement marker's track location is set through starterPlacement, not through relationships";
        // What a starter placement modification that sets neither a track location nor a wreck setting fails with.
        public const string NothingSet = "Invalid starter placement: starterPlacement contains neither track nor wreck. " + WindowGuidance.SetStarterFields;
        // Why the track location of a used marker cannot be detached: the game reads it to place the cut.
        public const string NoDetach = "The track location of a starter placement marker cannot be detached: the new-game car placement reads it to place its cars, and a missing location stops the whole setup";
        // Why the Generic track-marker capability leaves a used marker alone: its track location is edited in the
        // starter placement workflow, together with the whole car cut.
        public const string MarkerCapabilityRefusal = "This is a starter placement marker: a new-game car placement reads its track location, which belongs to the starter placement workflow";
        // A car placement as a refusal names it: its setup and its place in it.
        public static string Place(StarterCarPlacement placement) => "setup " + placement.Setup + ", car placement " + (placement.Index + 1) + " of " + placement.Count;
        public const string NoReader = "No new-game car placement in an active setup reads this marker, so it is not a starter placement marker";
        // Why a marker that several car placements read takes no starter placement, for a track location and a wreck
        // setting alike; places names each car placement as Place does.
        public static string SeveralReaders(IReadOnlyList<string> places) =>
            places.Count + " new-game car placements read this marker (" + String.Join("; ", places) + "); a starter placement can change only one car placement, so it cannot be used for this marker";
        // The car placement a reader line names, as Place names it: "setup ewh-steam, car placement 3 of 3".
        public static string PlaceOf(string readerLine) {
            if (readerLine == null || !readerLine.StartsWith(ReaderPrefix, StringComparison.Ordinal)) return readerLine;
            var rest = readerLine.Substring("Read by ".Length);
            var colon = rest.IndexOf(':');
            return colon < 0 ? rest : rest.Substring(0, colon);
        }
        // Why another capability's object takes no starter placement.
        public const string NotAStarter = "This object is no starter placement marker; a starter placement applies to a marker that a new-game car placement reads";
        public static string Reader(StarterCarPlacement placement) =>
            ReaderPrefix + placement.Setup + ", car placement " + (placement.Index + 1) + " of " + placement.Count + ": cars "
            + (placement.Cars.Count == 0 ? "none" : String.Join(", ", placement.Cars)) + "; wreck: " + (placement.Wreck ? "yes" : "no");
    }
}
