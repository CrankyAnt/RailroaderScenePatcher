// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // What a failed operation did to the scene, and so what the user can do next.
    public enum FailureKind {
        // The operation applied, or nothing was attempted.
        None,
        // Refused before any mutation: an invalid value, a missing or ambiguous target, a failed prerequisite or a changed
        // inspected observation. The scene and the scene patch file are as they were.
        Refused,
        // Not attempted because a modification it depends on failed. Nothing was mutated for it.
        DependencyFailed,
        // The mutation failed and the captured prior state was restored and verified.
        Restored,
        // The mutation failed and the prior state was restored, but dependent output, such as regenerated terrain, has not
        // verified yet; or a later operation was refused because that output is still outstanding. The target waits until
        // the output completes. This is no incomplete cleanup: it needs no Recheck.
        AwaitingOutput,
        // The mutation failed and the restoration could not be verified. Outcome.Remaining names the remaining or uncertain
        // state. The target accepts no operation until its cleanup is verified or the world reloads.
        IncompleteCleanup,
        // Refused because an earlier failure left this target's cleanup unverified.
        Blocked,
        // A world-load application that already failed in this load attempt; it is not retried until the next load attempt.
        AlreadyFailed
    }
    // A failure after a mutation started, with what it left behind.
    internal sealed class AttemptFailure : InvalidOperationException {
        internal AttemptFailure(FailureKind kind, string message, IReadOnlyList<string> remaining = null, bool wasGuarded = false) : base(message) {
            Kind = kind; Remaining = remaining ?? Array.Empty<string>(); WasGuarded = wasGuarded;
        }
        internal bool WasGuarded { get; }
        internal FailureKind Kind { get; }
        internal IReadOnlyList<string> Remaining { get; }
    }
    // The state a failed attempt may have left behind, compared with the state before it.
    public static class RemainingState {
        // One line per setting whose current value differs from the value before the attempt, or one line that says the
        // state could not be read. When every read setting agrees, one line says that the capability's own checks, such as
        // registrations or generated output, did not verify, so that state is uncertain. before may be null when it could not be read.
        public static IReadOnlyList<string> Describe(ObjectState before, Func<ObjectState> readNow, string reason = null) {
            ObjectState now;
            try { now = readNow(); }
            catch (Exception e) { return new[] { "The object's current state could not be read. Every setting is uncertain. Reason: " + e.Message }; }
            if (before == null) return new[] { "The state before the attempt could not be read. No comparison is possible, so every setting is uncertain" };
            var lines = new List<string>();
            string Numbers(IReadOnlyList<double> values) => values == null ? "unknown" : String.Join(", ", values.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));
            var was = before.Placement; var placement = now?.Placement;
            if (was?.Position != null && !PatchEditingSession.SamePosition(was.Position, placement?.Position)) lines.Add("Position: now " + Numbers(placement?.Position) + ", before " + Numbers(was.Position));
            if (was?.Rotation != null && !PatchEditingSession.SameRotation(was.Rotation, placement?.Rotation)) lines.Add("Rotation: does not match its value before the attempt");
            if (before.Active != now?.Active) lines.Add("Enabled: now " + Activity(now?.Active) + ", before " + Activity(before.Active));
            if (!MarkerBinding.Same(before.Marker, now?.Marker)) lines.Add("Track binding: now " + Binding(now?.Marker) + ", before " + Binding(before.Marker));
            if (before.MarkerId != null && before.MarkerId != now?.MarkerId) lines.Add("Marker ID: now " + (now?.MarkerId ?? "unknown") + ", before " + before.MarkerId);
            if (!CrossingCoverage.Same(before.Coverage, now?.Coverage)) lines.Add("Crossing coverage: does not match its value before the attempt");
            if (before.Segments != null && !ConstructionSegments.Same(before.Segments, now?.Segments)) lines.Add("Construction segments: now " + (now?.Segments?.Display ?? "none") + ", before " + before.Segments.Display);
            if (now?.Coverage?.Residue?.Count > 0) lines.Add("Owned spans still registered: " + String.Join(", ", now.Coverage.Residue));
            if (lines.Count == 0) lines.Add("The settings match their values before the attempt. The patcher could not verify the related changes" + (String.IsNullOrEmpty(reason) ? "" : ". Reason: " + reason));
            return lines;
        }
        static string Activity(bool? active) => active == null ? "group-driven" : active.Value ? "Yes" : "No";
        static string Binding(MarkerBinding binding) => binding == null ? "none" : binding.IsDetached ? "detached" : binding.Location.Display;
    }
    // The correction guidance for one failed scene patch editing action: what went wrong, what to do, and the guide of the operation.
    public sealed class Recovery {
        readonly bool wasGuarded;
        internal Recovery(FailureKind kind, string problem, string correction, string guide, string guideTitle, IReadOnlyList<string> remaining, bool wasGuarded) {
            Kind = kind; Problem = problem; Correction = correction; Guide = guide; GuideTitle = guideTitle; Remaining = remaining; this.wasGuarded = wasGuarded;
        }
        public FailureKind Kind { get; }
        // The reason the action reported.
        public string Problem { get; }
        // The applicable correction, in scene patch editing terms.
        public string Correction { get; }
        // The file name of the operation guide under docs/guides/scene-patches/, and its title.
        public string Guide { get; }
        public string GuideTitle { get; }
        public IReadOnlyList<string> Remaining { get; }
        // A short heading that tells the three outcomes apart: nothing changed, undone, or not confirmed.
        public string Heading {
            get {
                switch (Kind) {
                    case FailureKind.Restored: return "failed and was undone";
                    case FailureKind.AwaitingOutput: return wasGuarded ? "is waiting for terrain" : "was undone; waiting for terrain";
                    case FailureKind.IncompleteCleanup: return "failed; cleanup is incomplete";
                    case FailureKind.Blocked: return "is blocked by incomplete cleanup";
                    default: return "refused";
                }
            }
        }
        // One line for the message area, read as one label followed by plain sentences, as every refusal in the message
        // area reads: the action with its heading, the problem, what remains and the correction.
        public string Line(string action) {
            var detail = Kind == FailureKind.Blocked ? "" : Problem;
            if (Kind == FailureKind.Restored || Kind == FailureKind.AwaitingOutput || Kind == FailureKind.IncompleteCleanup) {
                const string reason = " Reason: ";
                var start = detail.IndexOf(reason, StringComparison.Ordinal);
                if (start >= 0) detail = detail.Substring(start + reason.Length);
            }
            return action + " " + Heading + "." + (String.IsNullOrEmpty(detail) ? "" : " " + Sentence(detail)) +
                (Remaining.Count == 0 ? "" : " What remains: " + String.Join("; ", Remaining.Select(line => line.TrimEnd('.'))) + ".") +
                (String.IsNullOrEmpty(Correction) ? "" : " " + Correction);
        }
        static string Sentence(string text) {
            text = (text ?? "").Trim();
            if (text.Length == 0) return text;
            text = Char.ToUpperInvariant(text[0]) + text.Substring(1);
            return text.EndsWith(".", StringComparison.Ordinal) ? text : text + ".";
        }
    }
    public static class RecoveryGuidance {
        public const string EditingGuide = "editing-placement-in-the-map-editor.md";
        // The guide of an operation: the action that ran, then the action group it ran in, and otherwise the capability of
        // the inspected object. A detection addition runs in the Crossing coverage group but has its own guide.
        public static KeyValuePair<string, string> Guide(string place, string action, string structure) {
            if (action == DetectionAddition) return Pair("Crossing detection", "adding-crossing-detection.md");
            switch (place) {
                case "Placement": return Pair("Fixed placement", "writing-fixed-placement.md");
                case "Gizmo": return Pair("Transform gizmos", "using-transform-gizmos.md");
                case "Snap": return Pair("Snapping", "snapping-to-track.md");
                case "Follow": return Pair("Following a track", "following-track.md");
                case "Track binding": return structure == CrossingPadScene.Structure ? Pair("Crossing pads", "rebinding-crossing-pads.md") : Pair("Track bindings", "repairing-track-bindings.md");
                case "Crossing coverage": return Pair("Crossing coverage", "correcting-crossing-coverage.md");
                case "Construction segments": return Pair("Construction segments", "repairing-construction-segments.md");
                case "Marker ID": return Pair("Marker IDs", "repairing-duplicate-marker-ids.md");
            }
            if (structure == ConstructionSceneryScene.Structure) return Pair("Construction segments", "repairing-construction-segments.md");
            if (structure == IndependentBoards.Structure) return Pair("Boards", "moving-boards.md");
            if (structure == BridgeScene.Structure) return Pair("Bridges", "placing-bridges.md");
            if (structure == StationMapIconRules.Structure) return Pair("Station map icons", "moving-station-map-icons.md");
            if (structure == TrackMarkerScene.Structure) return Pair("Track bindings", "repairing-track-bindings.md");
            if (structure == CrossingPadScene.Structure) return Pair("Crossing pads", "rebinding-crossing-pads.md");
            if (structure == CrossingCoverageScene.Structure) return Pair("Crossing coverage", "correcting-crossing-coverage.md");
            if (structure == SpawnPoints.Structure) return Pair("Spawn points", "moving-spawn-points.md");
            return Pair("Editing in the Map Editor", EditingGuide);
        }
        static KeyValuePair<string, string> Pair(string title, string file) => new KeyValuePair<string, string>(title, file);
        // The action name of an added or deleted detection addition.
        public const string DetectionAddition = "Detection addition";
        // The guidance for a failed outcome, or null for one that applied.
        public static Recovery For(Outcome outcome, string place, string action, string structure) {
            if (outcome == null || outcome.Applied) return null;
            var guide = Guide(place, action, structure);
            var problem = String.IsNullOrWhiteSpace(outcome.Error) ? "no reason was reported for the failure" : outcome.Error;
            return new Recovery(outcome.Failure == FailureKind.None ? FailureKind.Refused : outcome.Failure, problem, Correction(outcome.Failure, problem), guide.Value, guide.Key, outcome.Remaining, outcome.WasGuarded);
        }
        static string Correction(FailureKind kind, string problem) {
            switch (kind) {
                case FailureKind.Restored: return "The scene patch file is unchanged. Correct the cause and try again.";
                case FailureKind.AwaitingOutput: return "Wait for the terrain to update before editing again, or click Retry on the Changes tab.";
                case FailureKind.IncompleteCleanup:
                case FailureKind.Blocked: return "Click Recheck or reload the Map Editor.";
                case FailureKind.DependencyFailed: return "First correct the modification that this one depends on.";
            }
            if (Mentions(problem, "Target changed", "inspected", "refresh", "Inspect")) return "Nothing changed. Refresh the object list, inspect the object again and repeat the action.";
            if (Mentions(problem, "resolve uniquely")) return "Nothing changed. The object is missing from the loaded scene, or more than one object matches. Refresh the object list and inspect it again.";
            if (Mentions(problem, "protected from overwrite")) return "Nothing changed. This scene patch file cannot be overwritten.";
            if (Mentions(problem, "Reload before", "Reload the Map Editor before")) return "";
            return "Nothing changed. Correct the problem and try again.";
        }
        static bool Mentions(string text, params string[] words) => words.Any(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
