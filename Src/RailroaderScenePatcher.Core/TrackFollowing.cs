// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The explicitly selected track of a track-following placement: a location on one segment, or one endpoint node.
    public sealed class TrackAnchor {
        TrackAnchor(TrackLocation location, string node) { Location = location; Node = node; }
        public static TrackAnchor At(TrackLocation location) => new TrackAnchor(location ?? throw new ArgumentNullException(nameof(location)), null);
        public static TrackAnchor Endpoint(string node) {
            if (String.IsNullOrWhiteSpace(node)) throw new ArgumentException("An endpoint anchor needs a node ID", nameof(node));
            return new TrackAnchor(null, node);
        }
        // Null for an endpoint anchor.
        public TrackLocation Location { get; }
        // Null for a segment-location anchor.
        public string Node { get; }
        internal static bool Same(TrackAnchor a, TrackAnchor b) =>
            a == null ? b == null : b != null && a.Node == b.Node && TrackLocation.Same(a.Location, b.Location);
        public override string ToString() => Node != null ? "node:" + Node : Location.ToString();
    }
    // The native pose of a track anchor in map coordinates: the position and the track direction as a quaternion.
    public sealed class TrackPose {
        public TrackPose(double[] position, double[] rotation) { Position = position; Rotation = rotation; }
        public double[] Position { get; }
        public double[] Rotation { get; }
    }
    // A placement relative to an explicitly selected track or endpoint. The offset is expressed in the track's frame;
    // the rotation is applied after the track's rotation. The absolute pose follows the anchor whenever it is evaluated.
    public sealed class TrackFollowing {
        public TrackFollowing(TrackAnchor anchor, double[] offset, double[] rotation) {
            Anchor = anchor ?? throw new ArgumentNullException(nameof(anchor)); Offset = offset; Rotation = rotation;
        }
        public TrackAnchor Anchor { get; }
        public double[] Offset { get; }
        public double[] Rotation { get; }
        // The absolute pose for the anchor's current native pose.
        public Placement Evaluate(TrackPose track) {
            if (track?.Position == null || track.Rotation == null || track.Position.Length != 3 || track.Rotation.Length != 4
                || track.Position.Concat(track.Rotation).Any(v => Double.IsNaN(v) || Double.IsInfinity(v)) || Quaternion.Length(track.Rotation) == 0)
                throw new InvalidOperationException("Track anchor " + Anchor + " has no finite native pose");
            var frame = Quaternion.Normalize(track.Rotation);
            var turned = Quaternion.Rotate(frame, Offset);
            var position = new[] { track.Position[0] + turned[0], track.Position[1] + turned[1], track.Position[2] + turned[2] };
            return new Placement(position, Quaternion.Normalize(Quaternion.Multiply(frame, Rotation))) { Following = this };
        }
        // The relative placement that keeps an absolute pose at the anchor's current native pose.
        public static TrackFollowing Relative(TrackAnchor anchor, TrackPose track, double[] position, double[] rotation) {
            if (track?.Position == null || track.Rotation == null || Quaternion.Length(track.Rotation) == 0) throw new InvalidOperationException("Track anchor " + anchor + " has no finite native pose");
            var inverse = Quaternion.Conjugate(Quaternion.Normalize(track.Rotation));
            var offset = Quaternion.Rotate(inverse, new[] { position[0] - track.Position[0], position[1] - track.Position[1], position[2] - track.Position[2] });
            return new TrackFollowing(anchor, offset, Quaternion.Normalize(Quaternion.Multiply(inverse, rotation)));
        }
        internal static bool Same(TrackFollowing a, TrackFollowing b) =>
            a == null ? b == null : b != null && TrackAnchor.Same(a.Anchor, b.Anchor) && a.Offset.SequenceEqual(b.Offset) && a.Rotation.SequenceEqual(b.Rotation);
    }
    // A scene that reports native track poses for track-following placement. Throws when the anchor does not resolve uniquely.
    public interface ITrackPoseScene {
        TrackPose TrackPose(TrackAnchor anchor);
    }
    // Quaternions in x, y, z, w order, as the game stores them.
    internal static class Quaternion {
        internal static double Length(IReadOnlyList<double> q) => Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
        internal static double[] Normalize(IReadOnlyList<double> q) { var l = Length(q); return new[] { q[0] / l, q[1] / l, q[2] / l, q[3] / l }; }
        internal static double[] Conjugate(IReadOnlyList<double> q) => new[] { -q[0], -q[1], -q[2], q[3] };
        internal static double[] Multiply(IReadOnlyList<double> a, IReadOnlyList<double> b) => new[] {
            a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
            a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
            a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
            a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2] };
        internal static double[] Rotate(IReadOnlyList<double> q, IReadOnlyList<double> v) {
            var r = Multiply(Multiply(q, new[] { v[0], v[1], v[2], 0 }), Conjugate(q));
            return new[] { r[0], r[1], r[2] };
        }
    }
}
