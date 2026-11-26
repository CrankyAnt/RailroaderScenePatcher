// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Globalization;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The fields of the coverage form on a crossing controller's Connections tab: per role (approach A, island, approach B)
    // its From and To ends, each with a segment, a segment end and a distance. The game stores a detection span's two
    // ends separately and routes between them, so the two ends of a role may lie on different segments.
    internal sealed class CoverageFormFields {
        internal const int PerEnd = 3, PerRole = 2 * PerEnd, Count = 3 * PerRole;
        internal static readonly string[] EndLabels = { "Segment", "End", "Distance" };
        internal readonly string[] Values = new string[Count];
        // The ends the form was filled from: a distance the modder leaves as it shows keeps its exact value, so a Preview
        // and Apply without a change write the track the circuit covers now.
        readonly TrackLocation[] read = new TrackLocation[6];
        internal CoverageFormFields() { Clear(); }
        // The index of the first field of one end: its segment; the segment end and the distance follow.
        internal static int First(int role, bool to) => role * PerRole + (to ? PerEnd : 0);
        internal static string EndCaption(bool to) => to ? "To" : "From";
        // Empty fields, with end a, as a new addition starts.
        internal void Clear() {
            for (var i = 0; i < Count; i++) Values[i] = i % PerEnd == 1 ? "a" : "";
            Array.Clear(read, 0, read.Length);
        }
        // Fills every end from the track each role covers now: a circuit's selected tracks or an addition's tracks as the
        // scene patch file holds them, or the map's own spans of a native circuit. A detached circuit fills from the map's
        // own spans of that circuit, the track it covered before it was detached. A new addition starts empty.
        internal void Read(CircuitCoverage value) {
            Clear();
            var ranges = value?.CurrentRanges ?? (value?.Kind == CircuitKind.Detached ? value.SpanRanges : null);
            if (ranges == null) return;
            for (var role = 0; role < 3; role++) {
                Fill(role, false, ranges[role].From);
                Fill(role, true, ranges[role].To);
            }
        }
        // A world pick for one end: that end's segment, segment end and distance, and nothing else.
        internal void Pick(int role, bool to, TrackLocation at) {
            if (at == null || String.IsNullOrWhiteSpace(at.Segment)) return;
            Fill(role, to, at);
        }
        // A pick whose click gave no distance on the segment fills that end's segment only.
        internal void PickSegment(int role, bool to, string segment) {
            if (String.IsNullOrWhiteSpace(segment)) return;
            Values[First(role, to)] = segment;
        }
        void Fill(int role, bool to, TrackLocation at) {
            var first = First(role, to);
            Values[first] = at.Segment ?? "";
            Values[first + 1] = (at.End ?? "a").ToLowerInvariant();
            Values[first + 2] = CommandBand.Number(at.Distance);
            read[role * 2 + (to ? 1 : 0)] = at;
        }
        // The roles as entered, or null with the problem of the first end that is incomplete.
        internal CircuitCoverage Parse(bool addition, out string problem) {
            problem = null;
            var ranges = new SpanRange[3];
            for (var role = 0; role < 3; role++) {
                var from = Location(role, false); var to = Location(role, true);
                if (from == null || to == null) {
                    problem = "enter a segment ID, end a or b, and a distance with a decimal point for the " + EndCaption(from != null) + " end of " + CircuitCoverage.RoleCaption(CircuitCoverage.Roles[role]);
                    return null;
                }
                ranges[role] = new SpanRange(from, to);
            }
            return addition ? CircuitCoverage.Added(ranges[0], ranges[1], ranges[2]) : CircuitCoverage.Tracks(ranges[0], ranges[1], ranges[2]);
        }
        TrackLocation Location(int role, bool to) {
            var first = First(role, to);
            var segment = (Values[first] ?? "").Trim(); var end = (Values[first + 1] ?? "").Trim().ToLowerInvariant();
            if (segment.Length == 0 || (end != "a" && end != "b")) return null;
            var own = read[role * 2 + (to ? 1 : 0)];
            double distance;
            var ok = own != null ? CommandBand.Entered(Values[first + 2], own.Distance, out distance)
                : Double.TryParse(Values[first + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out distance);
            if (!ok || Double.IsNaN(distance) || Double.IsInfinity(distance)) return null;
            return new TrackLocation(segment, end, distance);
        }
    }
}
