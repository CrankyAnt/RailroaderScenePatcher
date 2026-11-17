// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    internal static class EnteredEditValues {
        internal static Placement Placement(string[] entered, double[] own, IReadOnlyList<double> ownRotation, Func<double, double, double, double[]> rotationFromEuler) {
            if (entered == null || entered.Length != 6 || own == null || own.Length != 6 || ownRotation == null || ownRotation.Count != 4 || rotationFromEuler == null) return null;
            var values = new double[6];
            for (var i = 0; i < 6; i++)
                if (!Finite(own[i]) || !CommandBand.Entered(entered[i], own[i], out values[i]) || !Finite(values[i])) return null;
            if (ownRotation.Any(v => !Finite(v))) return null;
            var turned = values[3] != own[3] || values[4] != own[4] || values[5] != own[5];
            var rotation = turned ? rotationFromEuler(values[3], values[4], values[5]) : ownRotation.ToArray();
            if (rotation == null || rotation.Length != 4 || rotation.Any(v => !Finite(v))) return null;
            return new Placement(new[] { values[0], values[1], values[2] }, rotation);
        }
        internal static TrackLocation TrackLocation(IReadOnlyList<string> fields) {
            if (fields == null || fields.Count != 3 || String.IsNullOrWhiteSpace(fields[0]) || fields[1] == null) return null;
            var end = fields[1].Trim().ToLowerInvariant();
            if ((end != "a" && end != "b") || !Double.TryParse(fields[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var distance) || !Finite(distance)) return null;
            return new TrackLocation(fields[0].Trim(), end, distance);
        }
        internal static bool FillTrackLocation(string[] fields, TrackLocation location) {
            if (fields == null || fields.Length != 3 || location == null || !Finite(location.Distance)) return false;
            var parsed = TrackLocation(new[] { location.Segment, location.End, CommandBand.Number(location.Distance) });
            if (parsed == null) return false;
            fields[0] = parsed.Segment;
            fields[1] = parsed.End;
            fields[2] = CommandBand.Number(parsed.Distance);
            return true;
        }
        static bool Finite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value);
    }
}
