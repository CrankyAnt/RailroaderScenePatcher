// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using RailroaderScenePatcher.Core;
using Track;
using Track.Search;

namespace RailroaderScenePatcher {
    // The loaded track graph as the crossing detection checks read it. A track span routes between its two ends, each
    // clamped onto its segment, with the game's route search under the track span costs and iteration limit
    // (TrackSpan.UpdateCachedPointsIfNeeded, RouteSearchPoints.FindPoints). The game builds a span's points from each
    // pair of route steps, so a search with fewer than two steps leaves no points: the game then logs that the span
    // has no route. A span caches its route and drops it only when its lower or upper end is set, so after a native
    // Move Node the game keeps the old route until the span is edited or the world is loaded again. These checks never
    // read or build that cache: they search the graph as it is now. Nothing here changes the graph or a span.
    internal sealed class CrossingDetectionTrack : IDetectionTrack {
        const int SpanIterations = 200;
        readonly Graph graph;
        CrossingDetectionTrack(Graph graph) { this.graph = graph; }
        // The route results of the current graph, cached per pair of stored span ends. The cache follows the graph:
        // a new graph starts an empty one, and every changed node of the graph (Graph.NodeDidChange, which a native Move
        // Node, a new or removed segment and a diamond change raise) clears it, as Invalidate does on every discovery.
        static Graph subscribed;
        static DetectionRouteCache cache;
        internal static IDetectionTrack Current() {
            var shared = Graph.Shared ?? throw new InvalidOperationException("The track graph is unavailable");
            if (!ReferenceEquals(shared, subscribed) || cache == null) {
                if (subscribed != null) subscribed.NodeDidChange -= NodeChanged;
                shared.NodeDidChange += NodeChanged;
                subscribed = shared;
                cache = new DetectionRouteCache(new CrossingDetectionTrack(shared));
            }
            return cache;
        }
        internal static void Invalidate() => cache?.Clear();
        static void NodeChanged(TrackNode node) => cache?.Clear();
        public double? SegmentLength(string segment) {
            var found = graph.GetSegment(segment);
            return found == null ? (double?)null : found.GetLength();
        }
        public bool HasRoute(TrackLocation lower, TrackLocation upper) =>
            graph.FindRoute(Clamped(lower), Clamped(upper), HeuristicCosts.TrackSpan, maxIterations: SpanIterations).Count > 1;
        Location Clamped(TrackLocation at) {
            var segment = graph.GetSegment(at.Segment) ?? throw new InvalidOperationException("Segment " + at.Segment + " is missing");
            return new Location(segment, (float)at.Distance, at.End == "a" ? TrackSegment.End.A : TrackSegment.End.B).Clamped();
        }
    }
}
