// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

namespace RailroaderScenePatcher.Core {
    // The geometry of the loaded track segments that a crossing pad's distance gizmo moves along. Positions are map
    // coordinates.
    public interface ISegmentGeometry {
        // The lengths of every loaded segment with this ID; one entry for a unique segment.
        IReadOnlyList<double> SegmentLengths(string segment);
        // The distance from end a of the point on the one loaded segment with this ID that lies nearest to a position.
        double DistanceFromA(string segment, double[] position);
    }
    // The track location a crossing pad is bound at. The game's pad builder (Track.TrackCurveMeshBuilder) measures a
    // location's distance from end a, so a pad bound from end b is built elsewhere; a pad is therefore always bound from
    // end a. An end b location names the same place as end a at the segment's length minus its distance.
    public static class PadDistance {
        // Three decimals, as a location chosen in the world reads.
        const int Decimals = 3;
        // The length of the one loaded segment with this ID, or null when none or several are loaded.
        public static double? Length(ISegmentGeometry geometry, string segment) {
            if (String.IsNullOrWhiteSpace(segment)) return null;
            var lengths = geometry.SegmentLengths(segment.Trim()) ?? Array.Empty<double>();
            return lengths.Count == 1 ? lengths[0] : (double?)null;
        }
        // Whether two locations name the same place in the same way, within the distance tolerance.
        public static bool Same(TrackLocation a, TrackLocation b) => TrackLocation.Same(a, b);
        // The same place from end a: an end b location becomes end a at the length minus its distance; any other location
        // stays as it is.
        public static TrackLocation FromEndA(TrackLocation location, double length) =>
            location == null || location.End != "b" ? location : new TrackLocation(location.Segment, "a", Math.Round(length - location.Distance, Decimals));
        // The location from end a of the point at a distance from end a, clamped to the segment.
        public static TrackLocation Along(string segment, double fromA, double length) =>
            new TrackLocation(segment, "a", Math.Round(Math.Max(0, Math.Min(length, fromA)), Decimals));
        // The location on the segment nearest to a map position, from end a.
        public static TrackLocation At(ISegmentGeometry geometry, string segment, double[] position) {
            var length = Length(geometry, segment) ?? throw new InvalidOperationException(Missing(geometry, segment));
            return Along(segment.Trim(), geometry.DistanceFromA(segment.Trim(), position), length);
        }
        // Why the game's pad builder failed at a location, in window terms: a place the game still can't build, such as a
        // pad that runs into a short neighbouring segment.
        public static string CannotBuild(TrackLocation location, double? padLength) =>
            "The game can't build " + (padLength == null ? "the pad" : "this " + ShownNumber.Text(padLength.Value) + " m pad") + " at " + location.Display
            + ": its pad builder fails there, as where the pad runs into a short neighbouring segment. Move the pad along the track";
        // Where the gizmo starts: the entered location when it lies on the entered segment, else that segment's middle.
        // Null without one loaded segment with the entered ID.
        public static TrackLocation Start(ISegmentGeometry geometry, string segment, TrackLocation entered) {
            var length = Length(geometry, segment);
            if (length == null) return null;
            var id = segment.Trim();
            if (entered != null && entered.Segment == id && (entered.End == "a" || entered.End == "b") && !Double.IsNaN(entered.Distance)
                && entered.Distance >= 0 && entered.Distance <= length.Value + TrackLocation.DistanceTolerance) return entered;
            return new TrackLocation(id, "a", length.Value / 2);
        }
        static string Missing(ISegmentGeometry geometry, string segment) {
            var id = (segment ?? "").Trim();
            var count = id.Length == 0 ? 0 : (geometry.SegmentLengths(id) ?? Array.Empty<double>()).Count;
            return count == 0 ? "Segment " + id + " is missing" : "Segment " + id + " is ambiguous";
        }
    }
}
