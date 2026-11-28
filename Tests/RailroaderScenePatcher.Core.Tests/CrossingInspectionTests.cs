// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The inspection of a crossing controller at the crossing coverage seam: what an inspection reads, what it offers and
// what it leaves unchanged.
static partial class CoverageTests {
    // The detection track of the controlled crossing world as it is now: every segment with one length.
    static FakeDetectionTrack DetectionTrack(FakeCrossingWorld world) {
        var track = new FakeDetectionTrack();
        foreach (var segment in world.Segments.Where(s => s.Value.Count == 1)) track.Segment(segment.Key, segment.Value[0]);
        return track;
    }
    // The spans of each circuit of a controller as the inspection reads them: index, circuit key and the stored ends.
    static IEnumerable<Tuple<int, string, DetectionSpan[]>> Circuits(FakeCrossingWorld world, FakeCrossing crossing) =>
        world.Circuits(crossing).Select((set, index) => {
            var spans = world.Spans(set);
            var key = CircuitSpanProblems.CircuitKey(world.SpanId(spans[1]), world.AdditionKey(set));
            return Tuple.Create(index, key, spans.Select(s => new DetectionSpan(world.SpanId(s), world.Range(s).From, world.Range(s).To)).ToArray());
        }).ToArray();
    static DetectionFindings[] Judge(FakeCrossingWorld world, FakeCrossing crossing, string structure, string coverageRefusal, IDetectionTrack track) {
        if (!CircuitSpanChecks.Judges(true, structure, coverageRefusal, null, out var refusal)) return Array.Empty<DetectionFindings>();
        var segments = new HashSet<string>(world.Segments.Keys, StringComparer.Ordinal);
        return Circuits(world, crossing).Select(c => CircuitSpanChecks.Findings(c.Item1, c.Item2, c.Item3, segments, track, refusal)).ToArray();
    }
    static string Lines(IEnumerable<DetectionFindings> findings) => String.Join(" | ", findings.SelectMany(f => f.Problems.Concat(f.Information).Concat(f.Notes)));

    // A crossing object that lacks its script fails the crossing pattern check. The inventory then gives it no crossing
    // coverage structure, so its Connections tab draws no Crossing coverage section and no Edit circuit or Edit detection
    // addition button, and the patch editing session refuses its coverage. Its spans are still read and judged: every
    // finding is information with the reason, none a problem.
    static void NonEditableControllerHasNoCoverageAndListsDetectionAsInformation() {
        const string reason = "Missing script on the crossing object";
        var (f, session, mapEditor, store) = Session();
        f.Crossing.Pattern = reason;
        // A native Move Node shortened Sother to 380 m: approach A Pdlh of circuit P6id ends at its stored 390 m.
        f.World.Segments["Sother"] = new List<double> { 380 };
        var track = DetectionTrack(f.World);
        // Island Pipr of circuit Pipr has no route between its ends.
        var pipr = f.Rerouted.Spans[1];
        track.NoRoutes.Add(FakeDetectionTrack.Key(f.World.Range(pipr).From, f.World.Range(pipr).To));
        // The inventory's identity for a controller whose pattern check fails carries no structure.
        var identity = new TargetIdentity(Crossing.Scene, Crossing.Path, Crossing.Component, null);

        Assert(!CommandBand.EditsCoverage(identity) && CommandBand.EditsCoverage(Crossing),
            "Only a controller with the crossing coverage structure gets the Crossing coverage section with Edit circuit and Edit detection addition");
        Assert(!CommandBand.EditableStructure(identity.Structure), "The controller is not edited in the inspector");
        foreach (var target in new[] { identity, Crossing }) {
            string coverageError = null;
            try { session.Coverage(target); } catch (Exception e) { coverageError = e.Message; }
            Assert(coverageError == reason, "The session reads no coverage of the controller and names the pattern check's reason: " + coverageError);
            var preview = session.PreviewCoverage(target, "Pipr", Selected());
            Assert(preview.Error != null && preview.Error.Contains(reason) && preview.Lines.Count == 0, "Edit circuit has nothing to preview: " + preview.Error);
            var addition = session.PreviewAddition(target, "siding", Extra());
            Assert(addition.Error != null && addition.Error.Contains(reason) && addition.Lines.Count == 0, "Edit detection addition has nothing to preview: " + addition.Error);
            Assert(!session.Cover(target, "Pipr", Selected()).Applied && !session.AddDetection(target, "siding", Extra()).Applied, "Neither coverage edit applies");
        }
        Assert(f.World.Mutations == 0 && f.Native && mapEditor.UndoGroups.Count == 0 && session.ScenePatchFileText.Contains("\"modifications\": {}"),
            "The refused edits change nothing");

        var findings = Judge(f.World, f.Crossing, identity.Structure, reason, track);
        Assert(findings.Length == 2 && findings.All(x => x.Problems.Count == 0 && x.Notes.Count == 0), "No finding of the controller is a problem: " + Lines(findings));
        var information = findings.SelectMany(x => x.Information).ToArray();
        Assert(information.Length == 2
            && information.Any(line => line.StartsWith("Circuit Pipr Island span Pipr has no track route between its two ends", StringComparison.Ordinal))
            && information.Any(line => line.StartsWith("Circuit P6id Approach A span Pdlh has its upper end outside its track segment: the end lies 390 m from end a of segment Sother, which is 380 m long.", StringComparison.Ordinal)),
            "The spans are read and both faults are listed as information: " + Lines(findings));
        Assert(information.All(line => line.EndsWith("The patcher cannot repair it, because crossing coverage editing is unavailable for this controller: " + reason + ".", StringComparison.Ordinal)
            && line.IndexOf("Edit circuit", StringComparison.Ordinal) < 0 && line.IndexOf("Edit detection addition", StringComparison.Ordinal) < 0 && line.IndexOf("Connections tab", StringComparison.Ordinal) < 0),
            "Each line names the reason and no repair route: " + Lines(findings));
    }

    // Inspecting a crossing controller, also one with a detection problem, reads its coverage, the tracks it marks and
    // its detection findings, twice, and changes no scene object, no patch data and no completed edit history.
    static void InspectionChangesNoObjectPatchDataOrHistory() {
        var (f, session, mapEditor, store) = Session();
        // Patch data and history to keep: a selection of circuit Pipr, and a detach of P6id that is undone, so both the
        // undo and the redo history hold an entry.
        Assert(session.Cover(Crossing, "Pipr", Selected()).Applied && session.Cover(Crossing, "P6id", CircuitCoverage.Detached).Applied, "The fixture edits apply");
        mapEditor.Undo();
        Assert(mapEditor.UndoGroups.Count == 1 && mapEditor.RedoGroups.Count == 1 && f.Crossing.Circuits.Count == 2, "The fixture holds one undo and one redo entry");
        // A native Move Node shortened Sother to 380 m, so approach A Pdlh of circuit P6id has a problem.
        f.World.Segments["Sother"] = new List<double> { 380 };

        string Scene() => String.Join(";", f.World.Objects.SelectMany(o => o.Value.Cast<FakeCrossing>().Select(c =>
            o.Key + "@" + String.Join(",", c.Position) + "/" + String.Join(",", c.Rotation) + ":" + String.Join("+", c.Circuits.Select(set =>
                (set.Addition ?? "") + "[" + String.Join(",", set.Spans.Select(s => s.Id + "=" + s.Range.From + ">" + s.Range.To + (s.Destroyed ? "!" : ""))) + "]")))))
            + "#" + String.Join(",", f.World.Graph.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + "=" + g.Value.Range.From + ">" + g.Value.Range.To))
            + "#" + String.Join(",", f.World.Segments.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => s.Key + "=" + String.Join("/", s.Value)));
        var scene = Scene();
        var circuits = f.Crossing.Circuits.ToArray();
        var spans = circuits.SelectMany(c => c.Spans).ToArray();
        var mutations = f.World.Mutations; var created = f.World.Created;
        var file = session.ScenePatchFileText; var saved = store.Patch; var info = store.Info;
        var undo = mapEditor.UndoGroups.ToArray(); var redo = mapEditor.RedoGroups.ToArray(); var changeCount = mapEditor.ChangeCount;
        var changes = session.Changes(Crossing).ToArray(); var incomplete = session.IncompleteOperation;

        string first = null;
        for (var pass = 0; pass < 2; pass++) {
            var coverage = session.Coverage(Crossing);
            Assert(coverage.Circuits.Any(c => c.Key == "Pipr" && c.Value.Kind == CircuitKind.Tracks) && coverage.Circuits.Any(c => c.Key == "P6id" && c.Value.Kind == CircuitKind.Native),
                "The inspection reads the crossing's coverage: " + String.Join(", ", coverage.Circuits.Select(c => c.Key + "=" + c.Value)));
            f.Scene.CoveredSegments(f.Crossing);
            new FakeCrossingInspection(f.World).Discover().ToArray();
            var findings = Judge(f.World, f.Crossing, CrossingCoverageScene.Structure, null, new DetectionRouteCache(DetectionTrack(f.World)));
            var lines = Lines(findings);
            Assert(findings.SelectMany(x => x.Problems).Count() == 1 && lines.Contains("Circuit P6id Approach A span Pdlh has its upper end outside its track segment"), "The inspection lists the problem: " + lines);
            if (first == null) first = lines; else Assert(lines == first, "A second inspection gives the same findings");
        }

        Assert(Scene() == scene && f.Crossing.Circuits.SequenceEqual(circuits) && f.Crossing.Circuits.SelectMany(c => c.Spans).SequenceEqual(spans)
            && f.World.Mutations == mutations && f.World.Created == created, "The inspection changes no scene object: no crossing, circuit, span, registration or track");
        Assert(session.ScenePatchFileText == file && store.Patch == saved && store.Info == info, "The inspection changes no patch data");
        Assert(mapEditor.UndoGroups.SequenceEqual(undo) && mapEditor.RedoGroups.SequenceEqual(redo) && mapEditor.ChangeCount == changeCount
            && session.Changes(Crossing).SequenceEqual(changes) && session.IncompleteOperation == incomplete, "The inspection changes no completed edit history");
        // The kept history still works: Redo reapplies the detach.
        mapEditor.Redo();
        Assert(f.Crossing.Circuits.Count == 1 && f.NewRouteDetectsBothWays, "Redo after the inspection reapplies the detach");
    }
}
