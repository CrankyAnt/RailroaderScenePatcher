// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

static class EnteredEditValuesTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run() {
        PlacementPreservesDisplayedValues();
        TrackLocationAcceptsBothDrafts();
        ClickedTrackLocationReplacesDraft();
        ClickedTrackLocationUpdatesSameSegment();
        InvalidClickedTrackLocationPreservesDraft();
        ChangedRotationAndInvalidInputs();
        PreserveUnsubmittedTrackLocation();
    }
    static void PreserveUnsubmittedTrackLocation() {
        var observed = new[] { "S8it", "b", "20" };
        var entered = new[] { "S8it", "b", "100" };
        Check(EnteredEditValues.IsDraft(entered, observed, null),
            "A different edit does not reset the entered distance or the track and end it belongs to");
        Check(!EnteredEditValues.IsDraft(entered, observed, entered),
            "An edit that submitted this location can show the newly observed values");
    }
    static void ClickedTrackLocationReplacesDraft() {
        var fields = new[] { "S8it", "b", "106.626" };
        Check(EnteredEditValues.FillTrackLocation(fields, new TrackLocation("Sitz", "a", 5.25)) && fields.SequenceEqual(new[] { "Sitz", "a", "5.25" }), "A clicked track location replaces the entire draft");
    }
    static void ClickedTrackLocationUpdatesSameSegment() {
        var fields = new[] { "Sitz", "b", "106.626" };
        Check(EnteredEditValues.FillTrackLocation(fields, new TrackLocation("Sitz", "a", 5.25)) && fields.SequenceEqual(new[] { "Sitz", "a", "5.25" }), "A click on the same segment updates its end and distance");
    }
    static void InvalidClickedTrackLocationPreservesDraft() {
        foreach (var location in new[] { (TrackLocation)null, new TrackLocation(" ", "a", 5), new TrackLocation("Sitz", "c", 5), new TrackLocation("Sitz", "a", Double.NaN) }) {
            var fields = new[] { "S8it", "b", "106.626" };
            Check(!EnteredEditValues.FillTrackLocation(fields, location) && fields.SequenceEqual(new[] { "S8it", "b", "106.626" }), "Invalid clicks leave the draft untouched");
        }
        Check(!EnteredEditValues.FillTrackLocation(null, new TrackLocation("Sitz", "a", 5)) && !EnteredEditValues.FillTrackLocation(new[] { "old" }, new TrackLocation("Sitz", "a", 5)), "Missing or incomplete drafts are refused");
    }
    static void ChangedRotationAndInvalidInputs() {
        var fields = new[] { "4", "5", "6", "11", "20", "30" };
        var own = new[] { 1.0, 2.0, 3.0, 10.0, 20.0, 30.0 };
        var rotation = new[] { 0.0, 0.0, 0.0, 1.0 };
        foreach (var input in new[] { fields, (string[])fields.Clone() }) {
            var result = EnteredEditValues.Placement(input, own, rotation, (x, y, z) => x == 11 && y == 20 && z == 30 ? new[] { 0.5, 0.5, 0.5, 0.5 } : null);
            Check(result != null && result.Position.SequenceEqual(new[] { 4.0, 5.0, 6.0 }) && result.Rotation.SequenceEqual(new[] { 0.5, 0.5, 0.5, 0.5 }), "Changed rotation uses the conversion result for both drafts");
        }
        Check(EnteredEditValues.Placement(null, own, rotation, (x, y, z) => rotation) == null && EnteredEditValues.Placement(new[] { "1" }, own, rotation, (x, y, z) => rotation) == null, "Missing placement fields are refused");
        fields[0] = "NaN";
        Check(EnteredEditValues.Placement(fields, own, rotation, (x, y, z) => rotation) == null, "Nonfinite placement is refused");
        Check(EnteredEditValues.TrackLocation(null) == null && EnteredEditValues.TrackLocation(new[] { "s", "c", "1" }) == null && EnteredEditValues.TrackLocation(new[] { "s", "a", "Infinity" }) == null && EnteredEditValues.TrackLocation(new[] { " ", "a", "1" }) == null, "Invalid track locations are refused");
    }
    static void TrackLocationAcceptsBothDrafts() {
        var fields = new[] { " segment-42 ", " B ", "12.75" };
        foreach (var input in new[] { fields, (string[])fields.Clone() }) {
            var result = EnteredEditValues.TrackLocation(input);
            Check(result != null && result.Segment == "segment-42" && result.End == "b" && result.Distance == 12.75, "Inspector and guided drafts produce the expected track location");
        }
    }
    static void PlacementPreservesDisplayedValues() {
        var fields = new[] { "1.235", "-2", "3", "10", "20", "30" };
        var own = new[] { 1.23456, -2.0, 3.0, 10.0, 20.0, 30.0 };
        var rotation = new[] { 0.1, 0.2, 0.3, 0.9 };
        foreach (var input in new[] { fields, (string[])fields.Clone() }) {
            var result = EnteredEditValues.Placement(input, own, rotation, (x, y, z) => throw new Exception("Unchanged rotation must be preserved"));
            Check(result != null && result.Position.SequenceEqual(new[] { 1.23456, -2.0, 3.0 }) && result.Rotation.SequenceEqual(new[] { 0.1, 0.2, 0.3, 0.9 }), "Inspector and guided drafts preserve the exact displayed placement");
        }
    }
}
