// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // A detection span of a circuit as the inspection reads it: its ID and its stored lower and upper ends. An end
    // keeps the segment, end and distance the scene stores; the segment is empty when none is assigned. A span whose
    // ends could not be read carries the reason instead (Unreadable).
    public sealed class DetectionSpan {
        public DetectionSpan(string id, TrackLocation lower, TrackLocation upper) {
            Id = id; Lower = lower ?? throw new ArgumentNullException(nameof(lower)); Upper = upper ?? throw new ArgumentNullException(nameof(upper));
        }
        DetectionSpan(string id, string error) { Id = id; Error = error; }
        public static DetectionSpan Unreadable(string id, string error) => new DetectionSpan(id, error ?? "the span could not be read");
        public string Id { get; }
        // Null when Error is set.
        public TrackLocation Lower { get; }
        public TrackLocation Upper { get; }
        // Why the span's ends could not be read, or null.
        public string Error { get; }
    }
    // The current track the detection checks read. Segment lengths and routes are read from the track as it is now,
    // never from a span's own cached route, which the game keeps after a native Move Node.
    public interface IDetectionTrack {
        // The length of the one segment with this ID, or null when there is none or more than one.
        double? SegmentLength(string segment);
        // Whether the game's route search for a span finds a route between its two ends, each moved onto its segment
        // as the game moves it. Without one the game logs that the span has no route and gives it length zero.
        bool HasRoute(TrackLocation lower, TrackLocation upper);
    }
    // The route results of a track, kept per pair of stored span ends until Clear. The owner clears it on every
    // discovery and whenever a track node changes, so a native Move Node during an inspection is read anew.
    // Segment lengths are always read from the track.
    public sealed class DetectionRouteCache : IDetectionTrack {
        readonly IDetectionTrack track;
        readonly Dictionary<string, bool> routes = new Dictionary<string, bool>(StringComparer.Ordinal);
        public DetectionRouteCache(IDetectionTrack track) { this.track = track ?? throw new ArgumentNullException(nameof(track)); }
        public void Clear() { routes.Clear(); }
        public double? SegmentLength(string segment) => track.SegmentLength(segment);
        public bool HasRoute(TrackLocation lower, TrackLocation upper) {
            var key = lower + "|" + upper;
            if (!routes.TryGetValue(key, out var found)) routes[key] = found = track.HasRoute(lower, upper);
            return found;
        }
    }
    // What the inspection found for one circuit of a crossing controller. A problem has a repair route on the
    // controller's Connections tab; information has none and stands on the Object tab; a note says what could not be checked.
    public sealed class DetectionFindings {
        internal DetectionFindings(IEnumerable<string> problems, IEnumerable<string> information, IEnumerable<string> notes) {
            Problems = problems.ToArray(); Information = information.ToArray(); Notes = notes.ToArray();
        }
        public IReadOnlyList<string> Problems { get; }
        public IReadOnlyList<string> Information { get; }
        public IReadOnlyList<string> Notes { get; }
    }
    // The checks of the detection spans of an inspected crossing controller's circuits, which give problems, information
    // and inspection notes. They check only what the game itself checks or reports for a span: whether each end lies
    // within its segment (TrackSpan.IsValid), and whether a route joins its two ends.
    public static class CircuitSpanChecks {
        // How far a stored distance may lie outside its segment and still be valid, as the game's Location.IsValid allows.
        const float EndTolerance = 0.0001f;

        // Whether an observed component judges its crossing's detection, and with which refusal. Only the crossing
        // controller itself does; a mast, gate, bell or span below it keeps its own lines. refusal is null for a
        // controller with crossing coverage editing (structure), whose findings have a repair route; otherwise it is
        // why the controller cannot be edited: its pattern check's reason, or the hierarchy path it shares.
        public static bool Judges(bool controllerItself, string structure, string coverageRefusal, string sharedPath, out string refusal) {
            refusal = null;
            if (!controllerItself) return false;
            if (structure != CrossingCoverageScene.Structure) refusal = coverageRefusal ?? sharedPath;
            return true;
        }

        // The findings of one circuit: a missing role (Missing circuit index role), a span that could not be read (a
        // note), the existing check for an end without an existing segment (CircuitSpanProblems.Unresolved), then the
        // detection checks: (a) an end whose stored distance lies outside its existing segment, and (b) a span with no
        // route between its two ends. index is the circuit's place in the controller's list; circuit is its key
        // (CircuitSpanProblems.CircuitKey), or null when it could not be named, which leaves only the missing roles; spans
        // holds its three spans in role order, a missing one as null; segments holds the IDs of the existing track
        // segments, or is null when the track graph could not be read, which leaves only the check for an end without a
        // segment. refusal is null when a repair route applies (Judges): then every finding is a problem with the
        // circuit's recovery. Otherwise every finding is information with the reason. The checks read only the current
        // scene, store nothing and change nothing.
        public static DetectionFindings Findings(int index, string circuit, IReadOnlyList<DetectionSpan> spans, ISet<string> segments, IDetectionTrack track, string refusal) {
            var missing = new List<string>();
            var existing = new List<string>();
            var faults = new List<string>();
            var notes = new List<string>();
            var named = !String.IsNullOrWhiteSpace(circuit);
            for (var i = 0; i < spans.Count && i < CircuitCoverage.Roles.Length; i++) {
                var span = spans[i];
                var role = CircuitCoverage.Roles[i];
                if (span == null) { missing.Add("Missing circuit " + index.ToString(CultureInfo.InvariantCulture) + " " + role); continue; }
                if (!named) continue;
                if (span.Error != null) { notes.Add("Inspection unavailable: " + span.Error); continue; }
                var unresolved = CircuitSpanProblems.UnresolvedFault(circuit, role, span.Id, span.Lower.Segment, span.Upper.Segment, segments);
                if (unresolved != null) existing.Add(unresolved);
                if (segments == null || track == null) continue;
                try { faults.AddRange(SpanFaults(circuit, role, span, segments, track)); }
                catch (Exception e) { notes.Add(Unavailable(CircuitSpanProblems.SpanName(circuit, role, span.Id), e)); }
            }
            if (refusal != null) {
                var reason = " The patcher cannot repair it, because crossing coverage editing is unavailable for this controller: " + refusal.TrimEnd('.') + ".";
                return new DetectionFindings(Enumerable.Empty<string>(), missing.Select(m => m + ".").Concat(existing).Concat(faults).Select(f => f + reason), notes);
            }
            var recovery = named ? " " + CircuitSpanProblems.Recovery(circuit) : "";
            return new DetectionFindings(missing.Concat(existing.Concat(faults).Select(f => f + recovery)), Enumerable.Empty<string>(), notes);
        }
        // Faults (a) and (b) of one span. The game routes a span whenever both its segments exist, also when an end lies
        // outside its segment, so both faults can stand together.
        static IEnumerable<string> SpanFaults(string circuit, string role, DetectionSpan span, ISet<string> segments, IDetectionTrack track) {
            var name = CircuitSpanProblems.SpanName(circuit, role, span.Id);
            var found = new List<string>();
            foreach (var end in new[] { new KeyValuePair<string, TrackLocation>("lower", span.Lower), new KeyValuePair<string, TrackLocation>("upper", span.Upper) }) {
                var at = end.Value;
                if (!Measurable(at, segments, track, out var length) || Valid(at.Distance, length)) continue;
                found.Add(name + " has its " + end.Key + " end outside its track segment: the end lies " + Metres(at.Distance) + " from end " + at.End + " of segment " + at.Segment
                    + ", which is " + Metres(length) + " long. The game detects no train on a span with an end outside its segment.");
            }
            if (!Measurable(span.Lower, segments, track, out _) || !Measurable(span.Upper, segments, track, out _)) return found;
            if (!track.HasRoute(span.Lower, span.Upper))
                found.Add(name + " has no track route between its two ends, so the game gives it length zero and detects no train on it.");
            return found;
        }
        static bool Measurable(TrackLocation at, ISet<string> segments, IDetectionTrack track, out double length) {
            length = 0;
            if (String.IsNullOrWhiteSpace(at.Segment) || (at.End != "a" && at.End != "b") || !segments.Contains(at.Segment)) return false;
            var found = track.SegmentLength(at.Segment);
            if (found == null) return false;
            length = found.Value;
            return true;
        }
        // The game's Location.IsValid, in its single precision: the distance lies from 0 to the segment's length, each
        // within EndTolerance. A distance that is not a number is not valid.
        static bool Valid(double distance, double length) {
            var at = (float)distance;
            return at >= -EndTolerance && at <= (float)length + EndTolerance;
        }
        static string Unavailable(string name, Exception e) => "Inspection unavailable: " + name + " could not be checked against the track: " + e.GetBaseException().Message;
        static string Metres(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + " m";
    }
}
