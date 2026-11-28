// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The window state of a track binding's fields: the segment length beside Distance, the segment whose ends the world
// marks, and where a crossing pad's distance gizmo stands.
static class PadBindingWindowTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Pad binding window: " + message); }
    internal static void Run() {
        TheLengthOfTheEnteredSegmentStandsBesideDistance();
        TheWorldMarksTheEndsWhileABindingFieldIsEdited();
        TheDistanceGizmoStandsOnlyOnAnEditablePadsConnectionsTab();
        TheReleaseFillsTheFieldsAndBindsThePadOnce();
        TheEndFieldSaysAPadIsStoredFromEndA();
        AnEmptyEndTakesEndAOnceTheFieldsAreUsed();
        TheCompletedPadBindingNamesTheStoredBinding();
        AReleaseWhereTheDragBeganBindsNothing();
        Console.WriteLine("PASS: segment length beside Distance, end marks while a binding field is edited, distance gizmo state, release fills the fields and binds once");
    }
    static void TheLengthOfTheEnteredSegmentStandsBesideDistance() {
        Check(CommandBand.SegmentLength("S0ys", 130.945) == "Segment length 130.945 m", "a loaded segment shows its length: " + CommandBand.SegmentLength("S0ys", 130.945));
        Check(CommandBand.SegmentLength("S0ys", 12.34567) == "Segment length 12.346 m", "the length reads with three decimals");
        Check(CommandBand.SegmentLength("Gone", null) == "Segment not found", "a segment that is not loaded says so");
        Check(CommandBand.SegmentLength(" ", null) == null && CommandBand.SegmentLength(null, null) == null, "no segment entered shows no length");
        Check(CommandBand.SegmentLengthTip.Contains("Distance") && CommandBand.SegmentLengthTip.Contains("End"), "the tooltip says where Distance counts from");
    }
    static void TheWorldMarksTheEndsWhileABindingFieldIsEdited() {
        Check(CommandBand.MarkedEnds(true, false, " S0ys ") == "S0ys", "a focused binding field marks the entered segment");
        Check(CommandBand.MarkedEnds(false, true, "S0ys") == "S0ys", "a distance gizmo drag edits the fields and marks the segment");
        Check(CommandBand.MarkedEnds(false, false, "S0ys") == null, "without an edit nothing is marked");
        Check(CommandBand.MarkedEnds(true, false, "") == null && CommandBand.MarkedEnds(true, false, null) == null, "no segment entered marks nothing");
        Check(CommandBand.EndMark("a", "S0ys") == "End A of S0ys" && CommandBand.EndMark("b", "S0ys") == "End B of S0ys", "the end marks name the end and the segment");
    }
    static void TheDistanceGizmoStandsOnlyOnAnEditablePadsConnectionsTab() {
        Check(CommandBand.ShowsPadGizmo(true, true, true, false, false), "an editable pad on Connections without a pending choice shows the gizmo");
        Check(!CommandBand.ShowsPadGizmo(false, true, true, false, false), "only a crossing pad");
        Check(!CommandBand.ShowsPadGizmo(true, false, true, false, false), "only on Connections");
        Check(!CommandBand.ShowsPadGizmo(true, true, false, false, false), "only while the pad can be edited");
        Check(!CommandBand.ShowsPadGizmo(true, true, true, true, false), "not while a world choice is pending");
        Check(!CommandBand.ShowsPadGizmo(true, true, true, false, true), "not during a guided task");
        Check(CommandBand.PadGizmoState(true) == CommandBand.PadGizmoDragging && CommandBand.PadGizmoState(false).Contains("gizmo"), "the state line says what the gizmo does");
    }
    // A drag from S1 a / 20 to a point 10 m before end b of the 130.945 m segment: the release fills the fields with
    // End a and the distance from it, as every pad is bound, then binds the pad there once, as Set binds the fields.
    static void TheReleaseFillsTheFieldsAndBindsThePadOnce() {
        var track = new FakeSegmentGeometry(); track.Line("S1", 100, 130.945);
        var start = new TrackLocation("S1", "a", 20);
        var released = PadDistance.At(track, "S1", new double[] { 220.945, 0, 0 });
        var fields = new[] { "S1", "a", "20" };
        var before = new[] { "S1", "a", "20" };
        var bound = new List<string[]>();
        var applied = CommandBand.ReleasePad(fields, before, start, released, () => bound.Add((string[])fields.Clone()));
        Check(applied, "the release binds");
        Check(bound.Count == 1, "the binding is applied once: " + bound.Count);
        Check(bound[0][0] == "S1" && bound[0][1] == "a" && bound[0][2] == "120.945", "the fields hold S1 / a / 120.945 when the binding is applied: " + String.Join(" / ", bound[0]));
        var fromA = new[] { "S1", "b", "100" };
        Check(CommandBand.ReleasePad(fromA, null, start, PadDistance.At(track, "S1", new double[] { 105.5, 0, 0 }), () => bound.Add((string[])fromA.Clone()))
            && bound.Count == 2 && bound[1][1] == "a" && bound[1][2] == "5.5", "a release near end a binds from end a: " + String.Join(" / ", bound[1]));
    }
    // Beside the End field: a pad's binding is stored from End a, and an entered End b shows the place it is stored as.
    static void TheEndFieldSaysAPadIsStoredFromEndA() {
        Check(CommandBand.PadEndNote(new[] { "S1", "a", "12" }, 130.945) == CommandBand.PadStoredFromEndA, "End a gives the short rule: " + CommandBand.PadEndNote(new[] { "S1", "a", "12" }, 130.945));
        Check(CommandBand.PadStoredFromEndA.Contains("End a") && CommandBand.PadStoredFromEndA.Length <= 40, "the rule is short: " + CommandBand.PadStoredFromEndA);
        var converted = CommandBand.PadEndNote(new[] { "S1", "b", "30" }, 130.945);
        Check(converted == "Same place, stored as End a, 100.945 m.", "End b shows the stored place: " + converted);
        Check(CommandBand.PadEndNote(new[] { "S1", "B", " 30 " }, 130.945) == converted, "upper case and spaces read alike");
        Check(CommandBand.PadEndNote(new[] { "S1", "b", "x" }, 130.945) == CommandBand.PadStoredFromEndA && CommandBand.PadEndNote(new[] { "Gone", "b", "30" }, null) == CommandBand.PadStoredFromEndA
            && CommandBand.PadEndNote(new[] { "S1", "b", "200" }, 130.945) == CommandBand.PadStoredFromEndA, "without a place on a loaded segment only the rule shows");
    }
    // Binding fields that start empty, as for a duplicate or an unbound marker or pad, get End a as soon as they are used:
    // a segment picked or typed. An End the modder entered stays, and nothing is filled while no segment is entered.
    static void AnEmptyEndTakesEndAOnceTheFieldsAreUsed() {
        var picked = new[] { "S0ys", "", "" };
        Check(CommandBand.DefaultEnd(picked) && picked[1] == "a" && picked[0] == "S0ys" && picked[2] == "", "a picked segment fills End with a");
        var typed = new[] { "S0", "  ", "12" };
        Check(CommandBand.DefaultEnd(typed) && typed[1] == "a", "a typed segment fills a blank End with a");
        var chosen = new[] { "S0ys", "b", "12" };
        Check(!CommandBand.DefaultEnd(chosen) && chosen[1] == "b", "an End the modder entered stays");
        var untouched = new[] { "", "", "" };
        Check(!CommandBand.DefaultEnd(untouched) && untouched[1] == "", "without a segment nothing is filled");
        Check(!CommandBand.DefaultEnd(null) && !CommandBand.DefaultEnd(new[] { "S1" }), "fields of another shape are left alone");
    }
    // The footer names the binding the edit stores: for a crossing pad an End b entry is stored from End a, so
    // `S0ys / b / 10` on the 151.936 m segment completes as `S0ys / a / 141.936`. Another track binding keeps its End.
    static void TheCompletedPadBindingNamesTheStoredBinding() {
        var entered = new TrackLocation("S0ys", "b", 10);
        var stored = CommandBand.StoredBinding(entered, true, 151.936);
        var action = CommandBand.BindingAction(stored.Segment, stored.End, stored.Distance);
        Check(action == "Track binding to S0ys / a / 141.936", "the pad's completion names the stored binding: " + action);
        Check(ReferenceEquals(CommandBand.StoredBinding(entered, false, 151.936), entered), "a marker's binding keeps End b");
        Check(ReferenceEquals(CommandBand.StoredBinding(entered, true, null), entered), "without a loaded segment the entry stays for the edit's own refusal");
        Check(CommandBand.StoredBinding(new TrackLocation("S0ys", "b", 200), true, 151.936).End == "b", "a distance outside the segment stays for its refusal");
        var fromA = new TrackLocation("S0ys", "a", 10);
        Check(ReferenceEquals(CommandBand.StoredBinding(fromA, true, 151.936), fromA), "End a stays as entered");
    }
    static void AReleaseWhereTheDragBeganBindsNothing() {
        var start = new TrackLocation("S1", "a", 20);
        var fields = new[] { "S1", "a", "20.0004" };
        var before = new[] { "S1", "a", "20" };
        var calls = 0;
        Check(!CommandBand.ReleasePad(fields, before, start, new TrackLocation("S1", "a", 20.0004), () => calls++) && calls == 0, "a release at the start binds nothing");
        Check(fields[2] == "20", "the fields return to their values from before the drag: " + fields[2]);
        Check(!CommandBand.ReleasePad(fields, null, start, null, () => calls++) && calls == 0 && fields[2] == "20", "a release without a move binds nothing and keeps the fields");
    }
}
