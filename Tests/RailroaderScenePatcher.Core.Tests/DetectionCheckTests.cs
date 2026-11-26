// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Controlled track for the detection checks: segments by ID and length. The game's route search finds a route for
// every span unless the fixture lists the span's ends under NoRoutes.
class FakeDetectionTrack : IDetectionTrack {
    public readonly Dictionary<string, double> Segments = new Dictionary<string, double>(StringComparer.Ordinal);
    // The spans, by "lower|upper" key, between whose ends the game's route search finds no route.
    public readonly HashSet<string> NoRoutes = new HashSet<string>(StringComparer.Ordinal);
    public FakeDetectionTrack Segment(string id, double length) { Segments[id] = length; return this; }
    public double? SegmentLength(string segment) => Segments.TryGetValue(segment, out var length) ? length : (double?)null;
    public static string Key(TrackLocation from, TrackLocation to) => from + "|" + to;
    public bool HasRoute(TrackLocation lower, TrackLocation upper) => !NoRoutes.Contains(Key(lower, upper));
}
static class DetectionCheckTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static TrackLocation At(string segment, string end, double distance) => new TrackLocation(segment, end, distance);
    // One track: S1 100 m, S2 200 m and S3 100 m long.
    static FakeDetectionTrack Track() => new FakeDetectionTrack().Segment("S1", 100).Segment("S2", 200).Segment("S3", 100);
    static ISet<string> Ids(FakeDetectionTrack track) => new HashSet<string>(track.Segments.Keys, StringComparer.Ordinal);
    // A valid circuit on that track: approach A from S2 out over S1, the island on S2, approach B from S2 out over S3.
    static DetectionSpan[] Valid() => new[] {
        new DetectionSpan("PA", At("S2", "a", 50), At("S1", "a", 50)),
        new DetectionSpan("PI", At("S2", "a", 50), At("S2", "a", 70)),
        new DetectionSpan("PB", At("S2", "a", 70), At("S3", "a", 50)) };
    public static void Run() {
        EndOutsideItsSegmentIsAProblemWithARepairRoute();
        EndToleranceFollowsTheGame();
        MissingSegmentIsLeftToTheExistingCheck();
        NoRouteIsAProblemWithARepairRoute();
        AdditionNamesEditDetectionAddition();
        NonEditableControllerListsFindingsAsInformation();
        ValidCrossingListsNothingAndInspectionIsRepeatable();
        UnmeasurableSpanIsANote();
        InformationIsPartOfTheObservation();
        OnlyTheControllerItselfIsJudgedWithItsOwnRefusal();
        MissingRoleFollowsTheRepairRoute();
        UnreadableSpanIsANote();
        ExistingAndDetectionFindingsShareOneRecovery();
        RouteCacheSearchesEachSpanOnceUntilCleared();
        BrysonEverettAsTheBaseMapStoresItListsNothing();
        SetupTwoTracksAsTheBaseMapStoresItListsNothing();
        SegmentLengthsAreReadFreshAfterAMoveNode();
        Console.WriteLine("PASS: crossing detection checks");
    }
    static string Lines(DetectionFindings f) => String.Join(" | ", f.Problems.Concat(f.Information).Concat(f.Notes));
    static void EndOutsideItsSegmentIsAProblemWithARepairRoute() {
        var track = Track();
        var spans = Valid();
        // A native Move Node shortened S1 to 40 m; approach A's upper end keeps its stored 50 m.
        track.Segment("S1", 40);
        var findings = CircuitSpanChecks.Findings(0, "PI", spans, Ids(track), track, null);
        Assert(findings.Problems.Count == 1, "One end off its segment is one problem: " + String.Join(" | ", findings.Problems));
        var problem = findings.Problems[0];
        Assert(problem.StartsWith("Circuit PI Approach A span PA has its upper end outside its track segment: the end lies 50 m from end a of segment S1, which is 40 m long.", StringComparison.Ordinal),
            "The problem names the circuit, the span, the segment, the stored distance and the segment's length: " + problem);
        Assert(problem.EndsWith("On the controller's Connections tab, click Edit circuit PI and select tracks for this circuit or detach it.", StringComparison.Ordinal),
            "A native circuit's repair route is Edit circuit on the Connections tab: " + problem);
        Assert(!BindingRules.IsNote(problem) && findings.Information.Count == 0, "A repairable fault is a problem, not information or a note");
    }
    // An end is valid as the game's Location.IsValid judges it: from 0 to the segment's length, each within 0.0001 m.
    static void EndToleranceFollowsTheGame() {
        var track = Track();
        DetectionFindings With(double lower, double upper) =>
            CircuitSpanChecks.Findings(0, "PI", new[] { Valid()[0], new DetectionSpan("PI", At("S2", "a", lower), At("S2", "a", upper)), Valid()[2] }, Ids(track), track, null);
        Assert(With(-0.00005, 200.00005).Problems.Count == 0, "Ends within 0.0001 m of the segment are valid: " + Lines(With(-0.00005, 200.00005)));
        Assert(With(0, 200.001).Problems.Count == 1 && With(-0.001, 200).Problems.Count == 1, "An end 0.001 m outside its segment is a problem: " + Lines(With(0, 200.001)));
        Assert(With(Double.NaN, 70).Problems.Count == 1, "A distance that is not a number is outside its segment, as for the game");
    }
    // A span end on a segment that does not exist is reported once, by the existing check, and not as an end outside
    // its segment; the spans whose ends exist are still checked.
    static void MissingSegmentIsLeftToTheExistingCheck() {
        var track = Track();
        var spans = Valid();
        spans[0] = new DetectionSpan("PA", At("S2", "a", 50), At("Sgone", "a", 500));
        // Approach B is also off its segment, so the other spans are visibly still checked.
        spans[2] = new DetectionSpan("PB", At("S2", "a", 70), At("S3", "a", 150));
        var findings = CircuitSpanChecks.Findings(0, "PI", spans, Ids(track), track, null);
        var existing = CircuitSpanProblems.Unresolved("PI", "approachA", "PA", "S2", "Sgone", Ids(track));
        Assert(existing != null && findings.Problems.Count(p => p.Contains("Sgone")) == 1 && findings.Problems.Contains(existing),
            "The missing segment is reported once, in the existing check's words: " + Lines(findings));
        Assert(findings.Problems.Count == 2 && findings.Problems.Any(p => p.StartsWith("Circuit PI Approach B span PB has its upper end outside its track segment", StringComparison.Ordinal)),
            "The spans whose segments exist are still checked: " + Lines(findings));
        Assert(findings.Information.Count == 0 && findings.Notes.Count == 0, "Nothing else is listed: " + Lines(findings));
    }
    // The game's route search finds no route between approach B's two ends: the game logs that the span has no route
    // and gives it length zero. Whether a route exists decides, not its length.
    static void NoRouteIsAProblemWithARepairRoute() {
        var track = Track();
        var spans = Valid();
        track.NoRoutes.Add(FakeDetectionTrack.Key(spans[2].Lower, spans[2].Upper));
        var findings = CircuitSpanChecks.Findings(0, "PI", spans, Ids(track), track, null);
        Assert(findings.Problems.Count == 1 && findings.Problems[0].StartsWith("Circuit PI Approach B span PB has no track route between its two ends", StringComparison.Ordinal),
            "A span without a route is a problem of its span: " + Lines(findings));
        Assert(findings.Problems[0].EndsWith("click Edit circuit PI and select tracks for this circuit or detach it.", StringComparison.Ordinal), "It names the repair route: " + findings.Problems[0]);
        // The game routes a span also when an end lies off its segment, so both faults stand together.
        track.Segment("S3", 40);
        findings = CircuitSpanChecks.Findings(0, "PI", spans, Ids(track), track, null);
        Assert(findings.Problems.Count == 2 && findings.Problems.Any(p => p.Contains("outside its track segment")) && findings.Problems.Any(p => p.Contains("no track route")),
            "An end off its segment and a missing route are two problems: " + Lines(findings));
    }
    static void AdditionNamesEditDetectionAddition() {
        var track = Track();
        var spans = Valid();
        track.Segment("S1", 40);
        var key = DetectionAddition.Key("crankyant/maptest", "siding");
        var findings = CircuitSpanChecks.Findings(0, key, spans, Ids(track), track, null);
        Assert(findings.Problems.Count == 1 && findings.Problems[0].StartsWith("Detection addition siding Approach A span PA ", StringComparison.Ordinal)
            && findings.Problems[0].EndsWith("On the controller's Connections tab, click Edit detection addition siding and select other tracks for this addition or delete it.", StringComparison.Ordinal),
            "A detection addition's repair route is Edit detection addition: " + Lines(findings));
        track = Track();
        track.NoRoutes.Add(FakeDetectionTrack.Key(spans[1].Lower, spans[1].Upper));
        findings = CircuitSpanChecks.Findings(0, key, spans, Ids(track), track, null);
        Assert(findings.Problems.Count == 1 && findings.Problems[0].Contains("no track route") && findings.Problems[0].Contains("click Edit detection addition siding"),
            "A detection addition's missing route names Edit detection addition: " + Lines(findings));
    }
    // A crossing controller that fails the crossing pattern check, such as a crossing object that lacks a script, has
    // no repair route: its span findings are read and listed as information with the reason, never as problems.
    static void NonEditableControllerListsFindingsAsInformation() {
        var track = Track();
        var spans = Valid();
        track.Segment("S1", 40);
        track.NoRoutes.Add(FakeDetectionTrack.Key(spans[1].Lower, spans[1].Upper));
        spans[2] = new DetectionSpan("PB", At("S2", "a", 70), At("Sgone", "a", 5));
        const string reason = "Missing script on the crossing object";
        var findings = CircuitSpanChecks.Findings(0, "PI", spans, Ids(track), track, reason);
        Assert(findings.Problems.Count == 0, "A controller without a repair route lists no problem: " + Lines(findings));
        Assert(findings.Information.Count == 3, "Its findings are information: the end off its segment, the island without a route and the missing segment: " + Lines(findings));
        Assert(findings.Information.All(line => line.EndsWith("The patcher cannot repair it, because crossing coverage editing is unavailable for this controller: " + reason + ".", StringComparison.Ordinal)),
            "Each names the reason: " + Lines(findings));
        Assert(findings.Information.All(line => line.IndexOf("Edit circuit", StringComparison.Ordinal) < 0 && line.IndexOf("Connections tab", StringComparison.Ordinal) < 0),
            "No line offers a repair route: " + Lines(findings));
        Assert(findings.Information.All(line => !BindingRules.IsNote(line)), "The findings are information, not inspection notes");
    }
    // A crossing with valid ends and a route between them lists nothing, inspection after inspection, and reading it
    // changes nothing. Whether the island covers the road is not checked: the track alone decides.
    static void ValidCrossingListsNothingAndInspectionIsRepeatable() {
        var track = Track();
        var findings = CircuitSpanChecks.Findings(0, "PI", Valid(), Ids(track), track, null);
        Assert(findings.Problems.Count == 0 && findings.Information.Count == 0 && findings.Notes.Count == 0, "A valid crossing lists nothing: " + Lines(findings));
        var again = CircuitSpanChecks.Findings(0, "PI", Valid(), Ids(track), track, null);
        Assert(Lines(again) == Lines(findings), "A second inspection gives the same findings");
        var faulty = Valid(); track.Segment("S1", 40);
        var first = CircuitSpanChecks.Findings(0, "PI", faulty, Ids(track), track, null);
        var second = CircuitSpanChecks.Findings(0, "PI", faulty, Ids(track), track, null);
        Assert(first.Problems.Count == 1 && Lines(first) == Lines(second), "A second inspection of an unchanged faulty crossing gives the same findings: " + Lines(second));
        Assert(faulty[0].Upper.Distance == 50 && track.Segments["S1"] == 40, "The inspection changes no span and no track");
    }
    // When the track cannot be read for one span, that span has an inspection note and the others are still checked.
    static void UnmeasurableSpanIsANote() {
        var track = new ThrowingTrack(Track());
        var spans = Valid();
        track.Inner.Segment("S1", 40);
        var findings = CircuitSpanChecks.Findings(0, "PI", spans, Ids(track.Inner), track, null);
        Assert(findings.Notes.Count == 1 && BindingRules.IsNote(findings.Notes[0]) && findings.Notes[0].Contains("PB") && findings.Notes[0].Contains("route unavailable"),
            "A route search that fails is one inspection note naming the span: " + Lines(findings));
        Assert(findings.Problems.Count == 1 && findings.Problems[0].Contains("span PA"), "The other spans are still checked: " + Lines(findings));
    }
    // Information stands on the Object tab: a change in it marks an inspection stale, names itself in the stale reason,
    // and a search finds its text, as for problems and notes.
    static void InformationIsPartOfTheObservation() {
        var handle = new object();
        SceneObservation Entry(params string[] information) => new SceneObservation(handle, new TargetIdentity("s", "World/Crossing", CrossingCoverageScene.ComponentType),
            "Grade crossing controller", true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "none", information: information);
        var before = Entry("Circuit PI Approach A span PA has its upper end outside its track segment.");
        var after = Entry("Circuit PI Approach A span PA has no track route between its two ends.");
        Assert(before.Information.SequenceEqual(new[] { "Circuit PI Approach A span PA has its upper end outside its track segment." }) && before.Problems.Count == 0, "The observation keeps its information apart from its problems");
        Assert(before.Difference(after).StartsWith("information: ", StringComparison.Ordinal), "A changed finding names itself in the stale reason: " + before.Difference(after));
        Assert(before.Matches(Entry(before.Information.ToArray())) && !before.Matches(after), "A changed finding is part of what an inspection pins");
        Assert(before.MatchesItself("no track route") == false && after.MatchesItself("no track route"), "A search finds the information text");
    }
    // Only the inspected crossing controller itself judges its detection; a mast, gate, bell or span below it does not.
    // The controller's refusal comes from its crossing pattern check, or from a hierarchy path it shares with another
    // object; an editable controller has none.
    static void OnlyTheControllerItselfIsJudgedWithItsOwnRefusal() {
        Assert(!CircuitSpanChecks.Judges(false, null, null, null, out var refusal) && refusal == null, "A child of the controller judges nothing");
        Assert(!CircuitSpanChecks.Judges(false, CrossingCoverageScene.Structure, null, null, out refusal) && refusal == null, "Nor does a child of an editable controller");
        Assert(CircuitSpanChecks.Judges(true, CrossingCoverageScene.Structure, null, null, out refusal) && refusal == null, "An editable controller judges with a repair route");
        Assert(CircuitSpanChecks.Judges(true, null, "Missing script on the crossing object", null, out refusal) && refusal == "Missing script on the crossing object",
            "A controller that fails the pattern check judges with that reason: " + refusal);
        const string shared = "2 objects share this hierarchy path and component";
        Assert(CircuitSpanChecks.Judges(true, null, null, shared, out refusal) && refusal == shared, "A controller that shares its path judges with that reason: " + refusal);
    }
    // A circuit without one of its spans keeps its existing line, Missing circuit N role: a problem on an editable
    // controller, information with the reason on any other.
    static void MissingRoleFollowsTheRepairRoute() {
        var track = Track();
        var spans = Valid();
        spans[2] = null;
        var findings = CircuitSpanChecks.Findings(1, "PI", spans, Ids(track), track, null);
        Assert(findings.Problems.SequenceEqual(new[] { "Missing circuit 1 approachB" }) && findings.Information.Count == 0, "On an editable controller the missing role is a problem as before: " + Lines(findings));
        findings = CircuitSpanChecks.Findings(1, null, spans, Ids(track), track, null);
        Assert(findings.Problems.SequenceEqual(new[] { "Missing circuit 1 approachB" }), "A circuit without a key still lists its missing role: " + Lines(findings));
        findings = CircuitSpanChecks.Findings(1, "PI", spans, Ids(track), track, "A circuit lacks its approachB span");
        Assert(findings.Problems.Count == 0 && findings.Information.Count == 1 && findings.Information[0].StartsWith("Missing circuit 1 approachB. ", StringComparison.Ordinal)
            && findings.Information[0].EndsWith("crossing coverage editing is unavailable for this controller: A circuit lacks its approachB span.", StringComparison.Ordinal),
            "With a refusal the missing role is information with the reason: " + Lines(findings));
    }
    static void UnreadableSpanIsANote() {
        var track = Track();
        var spans = Valid();
        spans[1] = DetectionSpan.Unreadable("PI", "field _upper unavailable");
        var findings = CircuitSpanChecks.Findings(0, "PI", spans, Ids(track), track, "Missing script on the crossing object");
        Assert(findings.Notes.SequenceEqual(new[] { "Inspection unavailable: field _upper unavailable" }) && findings.Problems.Count == 0 && findings.Information.Count == 0,
            "A span that cannot be read is an inspection note, whatever the repair route, and is not checked: " + Lines(findings));
    }
    // The existing missing-segment check and the detection faults name the same repair route in the same words.
    static void ExistingAndDetectionFindingsShareOneRecovery() {
        var track = Track();
        track.Segment("S1", 40);
        var spans = Valid();
        spans[2] = new DetectionSpan("PB", At("S2", "a", 70), At("Sgone", "a", 5));
        var findings = CircuitSpanChecks.Findings(0, "PI", spans, Ids(track), track, null);
        Assert(findings.Problems.Count == 2 && findings.Problems.All(p => p.EndsWith(" On the controller's Connections tab, click Edit circuit PI and select tracks for this circuit or detach it.", StringComparison.Ordinal)),
            "Both kinds end in the one recovery: " + Lines(findings));
        var key = DetectionAddition.Key("crankyant/maptest", "siding");
        var unresolved = CircuitSpanProblems.Unresolved(key, "island", "PI", "Sgone", "S2", Ids(track));
        Assert(unresolved.EndsWith(" On the controller's Connections tab, click Edit detection addition siding and select other tracks for this addition or delete it.", StringComparison.Ordinal),
            "An addition's missing segment names Edit detection addition: " + unresolved);
    }
    // The route search runs once per pair of stored span ends until the cache is cleared, as a discovery or a changed
    // track node clears it; segment lengths are always read from the track.
    static void RouteCacheSearchesEachSpanOnceUntilCleared() {
        var inner = new CountingTrack(Track());
        var cache = new DetectionRouteCache(inner);
        var first = CircuitSpanChecks.Findings(0, "PI", Valid(), Ids(inner.Inner), cache, null);
        var routes = inner.Routes;
        Assert(routes == 3, "The first inspection searches each span's route once: " + routes);
        var second = CircuitSpanChecks.Findings(0, "PI", Valid(), Ids(inner.Inner), cache, null);
        Assert(inner.Routes == routes && Lines(first) == Lines(second), "A second inspection of the same span ends searches no route again");
        // A Move Node breaks the route of approach B: after the clear the routes are searched anew.
        inner.Inner.NoRoutes.Add(FakeDetectionTrack.Key(Valid()[2].Lower, Valid()[2].Upper));
        var stale = CircuitSpanChecks.Findings(0, "PI", Valid(), Ids(inner.Inner), cache, null);
        Assert(stale.Problems.Count == 0, "Before the clear the cached routes stand");
        cache.Clear();
        var fresh = CircuitSpanChecks.Findings(0, "PI", Valid(), Ids(inner.Inner), cache, null);
        Assert(inner.Routes > routes && fresh.Problems.Count == 1 && fresh.Problems[0].Contains("span PB has no track route"), "After the clear the problem follows the changed track: " + Lines(fresh));
        // Another span end is another key.
        var moved = Valid(); moved[2] = new DetectionSpan("PB", At("S2", "a", 70), At("S3", "a", 60));
        var before = inner.Routes;
        CircuitSpanChecks.Findings(0, "PI", moved, Ids(inner.Inner), cache, null);
        Assert(inner.Routes > before, "Changed stored ends are searched, not taken from the cache");
    }
    sealed class CountingTrack : IDetectionTrack {
        public readonly FakeDetectionTrack Inner; public int Routes;
        public CountingTrack(FakeDetectionTrack inner) { Inner = inner; }
        public double? SegmentLength(string segment) => Inner.SegmentLength(segment);
        public bool HasRoute(TrackLocation lower, TrackLocation upper) { Routes++; return Inner.HasRoute(lower, upper); }
    }
    sealed class ThrowingTrack : IDetectionTrack {
        public readonly FakeDetectionTrack Inner;
        public ThrowingTrack(FakeDetectionTrack inner) { Inner = inner; }
        public double? SegmentLength(string segment) => Inner.SegmentLength(segment);
        public bool HasRoute(TrackLocation lower, TrackLocation upper) {
            if (upper.Segment == "S3") throw new InvalidOperationException("route unavailable");
            return Inner.HasRoute(lower, upper);
        }
    }
    // The six spans of Bryson Everett as the unedited base map stores them (game item 79). Sccw is 59.26 m long, which
    // puts the island's upper end at Sccw / b / 50.8 inside it. The base map's crossings work in the game, so none of
    // these spans is listed; the approaches' gaps of up to 1.82 m to their island are not checked.
    static void BrysonEverettAsTheBaseMapStoresItListsNothing() {
        var track = new FakeDetectionTrack().Segment("S9xx", 149.9).Segment("Sz8t", 54.3).Segment("S8rj", 25.4).Segment("Sx88", 212.2)
            .Segment("Snp1", 158.1).Segment("Sccw", 59.255).Segment("S63c", 450.8).Segment("S3ddf", 91.0);
        var pipr = new[] {
            new DetectionSpan("Pcyi", At("S8rj", "b", 0.342), At("Sx88", "a", 2.823)),
            new DetectionSpan("Pipr", At("Sz8t", "a", 0.157), At("Sz8t", "b", 45.127)),
            new DetectionSpan("Pws9", At("Sz8t", "a", 9.086), At("S9xx", "b", 38.571)) };
        var p6id = new[] {
            new DetectionSpan("Pdlh", At("S63c", "b", 1.704), At("S3ddf", "a", 8.988)),
            new DetectionSpan("P6id", At("Sccw", "a", 0.117), At("Sccw", "b", 50.8)),
            new DetectionSpan("P1db", At("Sccw", "a", 9.715), At("Snp1", "b", 43.592)) };
        var first = CircuitSpanChecks.Findings(0, "Pipr", pipr, Ids(track), track, null);
        var second = CircuitSpanChecks.Findings(1, "P6id", p6id, Ids(track), track, null);
        Assert(first.Problems.Count == 0 && first.Information.Count == 0 && first.Notes.Count == 0, "Circuit Pipr lists nothing: " + Lines(first));
        Assert(second.Problems.Count == 0 && second.Information.Count == 0 && second.Notes.Count == 0, "Circuit P6id lists nothing, its island's upper end lies on Sccw: " + Lines(second));
    }
    // Circuit Pp1v of Grade Crossing Setup 2 Tracks as the unedited base map stores it (game item 81). Its island lies
    // turned round on St3r, 434.58 m long, and both approaches start about 9 m out from it. The game does not check
    // whether an approach reaches its island, and the crossing works, so nothing is listed: no problem and no information.
    static void SetupTwoTracksAsTheBaseMapStoresItListsNothing() {
        var track = new FakeDetectionTrack().Segment("S1nb", 200).Segment("St3r", 434.58).Segment("Ssay", 200);
        var pp1v = new[] {
            new DetectionSpan("Pqlb", At("St3r", "b", 99.279), At("S1nb", "a", 50.434)),
            new DetectionSpan("Pp1v", At("St3r", "b", 38.366), At("St3r", "a", 344.392)),
            new DetectionSpan("Pcau", At("St3r", "a", 404.916), At("Ssay", "a", 48.513)) };
        var findings = CircuitSpanChecks.Findings(0, "Pp1v", pp1v, Ids(track), track, null);
        Assert(findings.Problems.Count == 0 && findings.Information.Count == 0 && findings.Notes.Count == 0,
            "The working base-map crossing lists no problem and no information line: " + Lines(findings));
    }
    // A native Move Node changes segment lengths: they are read from the track at every inspection, cache or not, and
    // the cleared route cache follows the moved track.
    static void SegmentLengthsAreReadFreshAfterAMoveNode() {
        var inner = Track();
        var cache = new DetectionRouteCache(inner);
        var spans = Valid();
        Assert(CircuitSpanChecks.Findings(0, "PI", spans, Ids(inner), cache, null).Problems.Count == 0, "The crossing starts valid");
        // The node at S1's far end moves 60 m along the track, without a cache clear yet: S1 is now 40 m long.
        inner.Segment("S1", 40);
        var moved = CircuitSpanChecks.Findings(0, "PI", spans, Ids(inner), cache, null);
        Assert(moved.Problems.Count == 1 && moved.Problems[0].Contains("40 m long"), "The end now outside S1 is reported at once: " + Lines(moved));
        // Moved back, and the cache cleared as Graph.NodeDidChange clears it: nothing is reported.
        inner.Segment("S1", 100);
        cache.Clear();
        Assert(CircuitSpanChecks.Findings(0, "PI", spans, Ids(inner), cache, null).Problems.Count == 0, "The undone move leaves nothing behind");
    }
}
