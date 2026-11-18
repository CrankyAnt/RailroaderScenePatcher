// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
namespace RailroaderScenePatcher {
    // The structural facts of a scene object that decide whether it is a Generic track marker of the supported pattern,
    // copied from the object before the consumer checks run.
    internal sealed class MarkerShape {
        internal MarkerShape(int markers, bool missingScript, bool milepost, bool padBuilder, string type) {
            Markers = markers; MissingScript = missingScript; Milepost = milepost; PadBuilder = padBuilder; Type = type;
        }
        // The number of track-marker components on the object.
        internal int Markers { get; }
        internal bool MissingScript { get; }
        internal bool Milepost { get; }
        // Whether the object holds a crossing-pad mesh builder.
        internal bool PadBuilder { get; }
        // The marker's type name, such as Generic or Signal.
        internal string Type { get; }
    }
    // The structural part of the Generic-marker pattern, before the consumer checks. UI-independent, shared with
    // controlled replay; the fixed-placement and track-following checks use the same rule as the binding check.
    // A Generic marker inside a grade-crossing setup, such as a whistle sign, qualifies: the crossing controller reads
    // its circuits and spans, and the auto engineer's crossing horn reads the Crossing-type pad marker, not this one.
    // The pad and the crossing's Crossing-type markers stay outside this pattern.
    internal static class MarkerPattern {
        internal static string Refusal(MarkerShape shape) {
            if (shape.Markers != 1) return shape.Markers == 0 ? "The object holds no track marker" : "The object holds several track markers";
            if (shape.MissingScript) return "Missing script on the marker object";
            if (shape.Milepost) return "Use the milepost's own settings to edit it";
            if (shape.PadBuilder) return "Edit this marker through its crossing pad";
            if (shape.Type != "Generic") return "Only Generic track markers are supported; this marker is " + shape.Type;
            return null;
        }
    }
}
