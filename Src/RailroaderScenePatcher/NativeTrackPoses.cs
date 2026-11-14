// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Globalization;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The loaded track graph's poses for track-following placement, in map coordinates. A segment location uses the
    // graph's position and rotation at that distance from the named end; its forward axis points away from that end
    // along the segment. An endpoint uses the node's own position and rotation. Segment and node IDs are keys of the
    // graph's lookups, so an anchor is unique or missing.
    internal sealed class NativeTrackPoses : ITrackPoseScene {
        const string GraphType = "Track.Graph", SegmentType = "Track.TrackSegment", LocationType = "Track.Location";
        readonly Type graph, location, end;
        internal NativeTrackPoses() {
            graph = Native.Type(GraphType); location = Native.Type(LocationType);
            var segment = Native.Type(SegmentType);
            if (graph == null || location == null || segment == null) throw new InvalidOperationException("Track-following prerequisites unavailable");
            end = segment.GetNestedType("End", Native.Flags) ?? throw new MissingMemberException(SegmentType, "End");
            if (location.GetConstructor(Native.Flags, null, new[] { segment, typeof(float), end }, null) == null) throw new MissingMethodException(LocationType, ".ctor");
            foreach (var method in new[] { "GetSegment", "GetNode", "GetPositionRotation" }) if (graph.GetMethod(method, Native.Flags) == null) throw new MissingMethodException(GraphType, method);
        }
        public TrackPose TrackPose(TrackAnchor anchor) {
            var shared = Native.Read(graph, "Shared") ?? throw new InvalidOperationException("The track graph is unavailable");
            if (anchor.Node != null) {
                var node = Native.Call(shared, "GetNode", anchor.Node) ?? throw new InvalidOperationException("Track anchor " + anchor + " does not resolve: the node is missing");
                return Pose((Vector3)Native.Read(node, "Position"), (Quaternion)Native.Read(node, "Rotation"), anchor);
            }
            var at = anchor.Location;
            var segment = Native.Call(shared, "GetSegment", at.Segment) ?? throw new InvalidOperationException("Track anchor " + anchor + " does not resolve: the segment is missing");
            var length = (float)Native.Call(segment, "GetLength");
            if (at.Distance > length + 0.001) throw new InvalidOperationException("Track anchor " + anchor + " does not resolve: distance " + at.Distance.ToString("R", CultureInfo.InvariantCulture) + " lies outside the segment");
            var native = Activator.CreateInstance(location, segment, (float)at.Distance, Enum.Parse(end, at.End == "a" ? "A" : "B"));
            var methods = Array.FindAll(graph.GetMethods(Native.Flags), m => m.Name == "GetPositionRotation");
            if (methods.Length != 1) throw new MissingMethodException(GraphType, "GetPositionRotation");
            var parameters = methods[0].GetParameters();
            var args = new object[parameters.Length];
            args[0] = native;
            for (var i = 1; i < args.Length; i++) args[i] = parameters[i].DefaultValue;
            var result = methods[0].Invoke(shared, args);
            return Pose((Vector3)Native.Read(result, "Position"), (Quaternion)Native.Read(result, "Rotation"), anchor);
        }
        // The location on the segment nearest to a world position, measured from end a, sampled every half metre.
        internal TrackLocation Nearest(string segmentId, Vector3 world) {
            var shared = Native.Read(graph, "Shared") ?? throw new InvalidOperationException("The track graph is unavailable");
            var segment = Native.Call(shared, "GetSegment", segmentId) ?? throw new InvalidOperationException("Segment " + segmentId + " is missing");
            var length = (float)Native.Call(segment, "GetLength");
            var target = (Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "WorldToGame", world);
            double best = 0, distance = Double.PositiveInfinity;
            var steps = Math.Max(1, Math.Min(4000, (int)Math.Ceiling(length / 0.5f)));
            for (var i = 0; i <= steps; i++) {
                var d = length * i / steps;
                var p = TrackPose(TrackAnchor.At(new TrackLocation(segmentId, "a", d))).Position;
                var squared = (p[0] - target.x) * (p[0] - target.x) + (p[1] - target.y) * (p[1] - target.y) + (p[2] - target.z) * (p[2] - target.z);
                if (squared < distance) { distance = squared; best = d; }
            }
            return new TrackLocation(segmentId, "a", Math.Round(best, 3));
        }
        static TrackPose Pose(Vector3 p, Quaternion q, TrackAnchor anchor) {
            if (Single.IsNaN(p.x + p.y + p.z + q.x + q.y + q.z + q.w) || Single.IsInfinity(p.x + p.y + p.z)) throw new InvalidOperationException("Track anchor " + anchor + " has no finite native pose");
            return new TrackPose(new double[] { p.x, p.y, p.z }, new double[] { q.x, q.y, q.z, q.w });
        }
    }
}
