// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RailroaderScenePatcher.Core;

// Straight segments along the map x axis, each from its start: the point of a segment nearest to a map position is
// its start plus the position's x offset, clamped to the segment. A segment ID may be missing or loaded twice.
sealed class FakeSegmentGeometry : ISegmentGeometry {
    public readonly Dictionary<string, List<double>> Lengths = new Dictionary<string, List<double>>();
    public readonly Dictionary<string, double> Starts = new Dictionary<string, double>();
    public void Line(string segment, double start, double length) { Lengths[segment] = new List<double> { length }; Starts[segment] = start; }
    public IReadOnlyList<double> SegmentLengths(string segment) => Lengths.TryGetValue(segment, out var found) ? found : new List<double>();
    public double DistanceFromA(string segment, double[] position) => Math.Max(0, Math.Min(Lengths[segment][0], position[0] - Starts[segment]));
}

static class PadDistanceTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Pad distance: " + message); }
    static bool Is(TrackLocation location, string segment, string end, double distance) =>
        location != null && location.Segment == segment && location.End == end && Math.Abs(location.Distance - distance) < 1e-9;
    internal static void Run() {
        APointOnTheSegmentIsBoundFromEndA();
        APointBeyondTheSegmentStopsAtItsEnd();
        TheDistanceKeepsThreeDecimals();
        AMissingOrAmbiguousSegmentIsRefused();
        TheGizmoStartsAtTheEnteredLocationOrTheSegmentMiddle();
        AnEndBLocationIsStoredAsTheSamePlaceFromEndA();
        Console.WriteLine("PASS: pad distance from end a from a point on the segment, clamping, start location, end b stored from end a, and refusals");
    }
    static FakeSegmentGeometry Track() { var track = new FakeSegmentGeometry(); track.Line("S1", 100, 130.945); return track; }

    // S1 runs from x 100 (end a) to x 230.945 (end b). A crossing pad is always bound from end a: the game's pad builder
    // measures from end a, so a pad bound from end b would be built elsewhere.
    static void APointOnTheSegmentIsBoundFromEndA() {
        Check(Is(PadDistance.At(Track(), "S1", new double[] { 112.5, 4, -3 }), "S1", "a", 12.5), "a point 12.5 m from end a is a / 12.5");
        Check(Is(PadDistance.At(Track(), "S1", new double[] { 220.945, 0, 0 }), "S1", "a", 120.945), "a point 10 m before end b is a / 120.945");
    }
    static void APointBeyondTheSegmentStopsAtItsEnd() {
        Check(Is(PadDistance.At(Track(), "S1", new double[] { 50, 0, 0 }), "S1", "a", 0), "a point before end a is a / 0");
        Check(Is(PadDistance.At(Track(), "S1", new double[] { 400, 0, 0 }), "S1", "a", 130.945), "a point past end b is a / 130.945");
    }
    static void TheDistanceKeepsThreeDecimals() {
        Check(Is(PadDistance.At(Track(), "S1", new double[] { 112.34567, 0, 0 }), "S1", "a", 12.346), "a / 12.346");
    }
    static void AMissingOrAmbiguousSegmentIsRefused() {
        var track = Track();
        track.Lengths["Twice"] = new List<double> { 10, 12 }; track.Starts["Twice"] = 0;
        foreach (var segment in new[] { "Gone", "Twice" }) {
            try { PadDistance.At(track, segment, new double[] { 1, 0, 0 }); Check(false, segment + " was accepted"); }
            catch (InvalidOperationException e) { Check(e.Message.Contains(segment), segment + " refusal names the segment: " + e.Message); }
        }
        Check(PadDistance.Length(track, "S1") == 130.945 && PadDistance.Length(track, "Gone") == null && PadDistance.Length(track, "Twice") == null && PadDistance.Length(track, " ") == null,
            "only one loaded segment has a length");
    }
    // The gizmo starts where the fields bind the pad when that location lies on its segment, else at the middle of the
    // entered segment, and has no start without one loaded segment.
    static void TheGizmoStartsAtTheEnteredLocationOrTheSegmentMiddle() {
        var track = Track();
        Check(Is(PadDistance.Start(track, "S1", new TrackLocation("S1", "b", 20)), "S1", "b", 20), "a valid entered location is the start");
        Check(Is(PadDistance.Start(track, "S1", new TrackLocation("S1", "b", 200)), "S1", "a", 65.4725), "a distance beyond the segment starts at the middle");
        Check(Is(PadDistance.Start(track, "S1", null), "S1", "a", 65.4725), "an incomplete location starts at the segment's middle");
        Check(Is(PadDistance.Start(track, " S1 ", new TrackLocation("S1", "c", 2)), "S1", "a", 65.4725), "an invalid end starts at the middle; the segment is trimmed");
        Check(PadDistance.Start(track, "Gone", null) == null && PadDistance.Start(track, "", null) == null, "no start without a loaded segment");
    }
    // An end b location is the point that lies the same distance from end b: from end a it is the segment's length
    // minus that distance. An end a location stays.
    static void AnEndBLocationIsStoredAsTheSamePlaceFromEndA() {
        Check(Is(PadDistance.FromEndA(new TrackLocation("S1", "b", 30), 130.945), "S1", "a", 100.945), "b / 30 is a / 100.945");
        Check(Is(PadDistance.FromEndA(new TrackLocation("S1", "b", 0), 130.945), "S1", "a", 130.945) && Is(PadDistance.FromEndA(new TrackLocation("S1", "b", 130.945), 130.945), "S1", "a", 0), "the ends map onto each other");
        Check(Is(PadDistance.FromEndA(new TrackLocation("S1", "b", 12.34567), 130.945), "S1", "a", 118.599), "three decimals");
        var a = new TrackLocation("S1", "a", 7);
        Check(ReferenceEquals(PadDistance.FromEndA(a, 130.945), a), "an end a location stays as entered");
    }
}
