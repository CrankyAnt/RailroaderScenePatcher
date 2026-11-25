// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The coverage form of a crossing controller's Connections tab: each role's From and To ends, filled from the track the
// circuit covers now, and written back by Preview and Apply, also when the two ends lie on different segments.
static partial class CoverageTests {
    static void RunForm() {
        NativeCircuitFillsBothEndsOfEveryRole();
        UnchangedNativeCircuitPreviewsAndAppliesNothing();
        ChangedEndOfANativeSpanWritesBothEndsOnTheirSegments();
        StoredCircuitAndAdditionFillTheForm();
        WorldPickFillsOneEndOnly();
        AdditionWithEndsOnTwoSegmentsIsWritten();
        IncompleteEndIsNamed();
    }
    // As the game stores it: a single-precision distance.
    static double Single(double value) => (float)value;
    // The fixture's retained circuit P6id with approach A Pdlh from Sother, measured from end b, to Snew, as base-map
    // approaches over two segments are stored.
    static Fixture TwoSegmentFixture() {
        var f = new Fixture();
        f.Retained.Spans[0].Range = new SpanRange(new TrackLocation("Sother", "b", Single(210.123456)), new TrackLocation("Snew", "a", Single(12.5)));
        return f;
    }
    static string[] EndValues(CoverageFormFields form, int role, bool to) => form.Values.Skip(CoverageFormFields.First(role, to)).Take(CoverageFormFields.PerEnd).ToArray();
    static bool Shows(CoverageFormFields form, int role, bool to, string segment, string end, string distance) =>
        EndValues(form, role, to).SequenceEqual(new[] { segment, end, distance });
    static void NativeCircuitFillsBothEndsOfEveryRole() {
        var (f, session, _, _) = Session(fixture: TwoSegmentFixture());
        var native = session.Coverage(Crossing)["P6id"];
        Assert(native.Kind == CircuitKind.Native && native.SpanRanges != null, "A native circuit read from the scene carries the track its own spans cover");
        var form = new CoverageFormFields();
        form.Read(native);
        Assert(Shows(form, 0, false, "Sother", "b", "210.123") && Shows(form, 0, true, "Snew", "a", "12.5"), "Approach A shows its From and To ends on their own segments: " + String.Join(",", form.Values));
        Assert(Shows(form, 1, false, "Sother", "a", "190") && Shows(form, 1, true, "Sother", "a", "200") && Shows(form, 2, false, "Sother", "a", "190") && Shows(form, 2, true, "Sother", "a", "10"),
            "The island and approach B show their current ends: " + String.Join(",", form.Values));
        form.Read(CircuitCoverage.Detached);
        Assert(form.Values.All(v => v == "" || v == "a"), "A detached circuit starts empty");
        form.Read(null);
        Assert(form.Values.All(v => v == "" || v == "a"), "A new addition starts empty");
        Assert(f.World.Mutations == 0, "Reading the form changes nothing");
    }
    static void UnchangedNativeCircuitPreviewsAndAppliesNothing() {
        var (f, session, mapEditor, _) = Session(fixture: TwoSegmentFixture());
        var form = new CoverageFormFields();
        form.Read(session.Coverage(Crossing)["P6id"]);
        var value = form.Parse(false, out var problem);
        Assert(value != null && problem == null, "The filled form parses: " + problem);
        Assert(value.Ranges[0].From.Segment == "Sother" && value.Ranges[0].From.End == "b" && value.Ranges[0].From.Distance == Single(210.123456),
            "A distance left as it shows keeps its exact stored value");
        var preview = session.PreviewCoverage(Crossing, "P6id", value);
        Assert(preview.Error == null && preview.Lines.All(l => l.Contains(": retained,")), "The preview of an unchanged native circuit retains every circuit: " + preview.Error + String.Join(" | ", preview.Lines));
        var outcome = session.Cover(Crossing, "P6id", value);
        Assert(outcome.Applied && outcome.Unchanged && f.World.Mutations == 0 && f.Native && mapEditor.UndoGroups.Count == 0 && session.ScenePatchFileText.Contains("\"modifications\": {}"),
            "Apply without a change leaves the crossing's coverage and the scene patch file unchanged: " + outcome.Error);
    }
    static void ChangedEndOfANativeSpanWritesBothEndsOnTheirSegments() {
        var (f, session, _, _) = Session(fixture: TwoSegmentFixture());
        var form = new CoverageFormFields();
        form.Read(session.Coverage(Crossing)["P6id"]);
        form.Values[CoverageFormFields.First(0, true) + 2] = "40";
        var value = form.Parse(false, out var problem);
        var preview = session.PreviewCoverage(Crossing, "P6id", value);
        Assert(preview.Error == null && preview.Lines.Any(l => l.StartsWith("Circuit P6id", StringComparison.Ordinal) && l.Contains("Sother end b " + Single(210.123456).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " m to Snew end a 40 m")), "The preview shows the changed span over two segments: " + preview.Error + String.Join(" | ", preview.Lines));
        var outcome = session.Cover(Crossing, "P6id", value);
        Assert(outcome.Applied && !outcome.Unchanged, "The changed end applies: " + outcome.Error);
        var approach = Only(session)["relationships"]["crossingCoverage"]["circuits"]["P6id"]["approachA"];
        Assert((string)approach["from"]["segment"] == "Sother" && (string)approach["from"]["end"] == "b" && (double)approach["from"]["distance"] == Single(210.123456)
            && (string)approach["to"]["segment"] == "Snew" && (double)approach["to"]["distance"] == 40, "The role is written with its From and To ends on their own segments: " + approach);
        var applied = f.Retained.Spans[0];
        Assert(applied.Owned && applied.Range.From.Segment == "Sother" && applied.Range.To.Segment == "Snew", "The owned span runs between the two segments");
        var stored = new CoverageFormFields();
        stored.Read(session.Coverage(Crossing)["P6id"]);
        Assert(Shows(stored, 0, false, "Sother", "b", "210.123") && Shows(stored, 0, true, "Snew", "a", "40"), "Edit circuit reopens the stored circuit with its two ends: " + String.Join(",", stored.Values));
        Assert(session.Cover(Crossing, "P6id", stored.Parse(false, out _)).Unchanged, "Applying the stored circuit unchanged changes nothing");
    }
    static void StoredCircuitAndAdditionFillTheForm() {
        var (f, session, _, _) = Session();
        Assert(session.Cover(Crossing, "Pipr", Selected()).Applied && session.AddDetection(Crossing, "siding", Extra("Sspur")).Applied, "A circuit and an addition are stored");
        var coverage = session.Coverage(Crossing);
        var form = new CoverageFormFields();
        form.Read(coverage["Pipr"]);
        Assert(Shows(form, 0, false, "Snew", "a", "308") && Shows(form, 0, true, "Snew", "a", "550") && Shows(form, 2, true, "Snew", "a", "50"), "Edit circuit fills the stored circuit: " + String.Join(",", form.Values));
        form.Read(coverage.Circuits.Single(c => c.Value.Kind == CircuitKind.Added).Value);
        Assert(Shows(form, 1, false, "Sspur", "a", "292") && Shows(form, 1, true, "Sspur", "a", "308"), "Edit detection addition fills the stored addition: " + String.Join(",", form.Values));
    }
    static void WorldPickFillsOneEndOnly() {
        var (_, session, _, _) = Session(fixture: TwoSegmentFixture());
        var form = new CoverageFormFields();
        form.Read(session.Coverage(Crossing)["P6id"]);
        var before = form.Values.ToArray();
        form.Pick(1, true, new TrackLocation("Sspur", "a", 77.25));
        var changed = Enumerable.Range(0, CoverageFormFields.Count).Where(i => form.Values[i] != before[i]).ToArray();
        Assert(Shows(form, 1, true, "Sspur", "a", "77.25") && changed.All(i => i >= CoverageFormFields.First(1, true) && i < CoverageFormFields.First(1, true) + CoverageFormFields.PerEnd),
            "A world pick fills only the segment, segment end, and distance of its own end: " + String.Join(",", form.Values));
        var picked = form.Parse(false, out _);
        Assert(picked.Ranges[1].From.Segment == "Sother" && picked.Ranges[1].To.Segment == "Sspur" && picked.Ranges[1].To.Distance == 77.25, "The picked end parses on its own segment");
        before = form.Values.ToArray();
        form.PickSegment(2, false, "Snew");
        Assert(form.Values[CoverageFormFields.First(2, false)] == "Snew" && Enumerable.Range(0, CoverageFormFields.Count).Count(i => form.Values[i] != before[i]) == 1, "A pick without a distance fills that end's segment only");
    }
    static void AdditionWithEndsOnTwoSegmentsIsWritten() {
        var (f, session, _, _) = Session();
        var form = new CoverageFormFields();
        form.Read(null);
        void End(int role, bool to, string segment, string distance) { var i = CoverageFormFields.First(role, to); form.Values[i] = segment; form.Values[i + 1] = "a"; form.Values[i + 2] = distance; }
        End(0, false, "Snew", "308"); End(0, true, "Sspur", "100");
        End(1, false, "Snew", "292"); End(1, true, "Snew", "308");
        End(2, false, "Snew", "292"); End(2, true, "Snew", "50");
        var value = form.Parse(true, out var problem);
        Assert(value != null && value.Kind == CircuitKind.Added, "The addition parses: " + problem);
        Assert(session.PreviewAddition(Crossing, "branch", value).Error == null, "The addition previews");
        var outcome = session.AddDetection(Crossing, "branch", value);
        Assert(outcome.Applied, "Apply writes an addition role whose ends lie on two segments: " + outcome.Error);
        var approach = Only(session)["relationships"]["crossingCoverage"]["additions"]["branch"]["approachA"];
        Assert((string)approach["from"]["segment"] == "Snew" && (string)approach["to"]["segment"] == "Sspur" && (double)approach["to"]["distance"] == 100, "The scene patch file holds both ends: " + approach);
    }
    static void IncompleteEndIsNamed() {
        var form = new CoverageFormFields();
        form.Read(CircuitCoverage.Tracks(R("Snew", 308, 550), R("Snew", 292, 308), R("Snew", 292, 50)));
        form.Values[CoverageFormFields.First(2, true)] = "";
        Assert(form.Parse(false, out var problem) == null && problem == "enter a segment ID, end a or b, and a distance with a decimal point for the To end of Approach B", "The refusal names the incomplete end: " + problem);
        form.Values[CoverageFormFields.First(0, false) + 1] = "c";
        Assert(form.Parse(false, out problem) == null && problem.EndsWith("the From end of Approach A", StringComparison.Ordinal), "An end other than a or b is refused: " + problem);
    }
}
