// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// The debug summary of the baked scenery rule's verdicts that one load attempt writes.
static class BakedSceneryCheckTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    internal static void Run() {
        SummaryPerLoad();
        Console.WriteLine("PASS: baked scenery checks");
    }

    static KeyValuePair<string, string> Check(string path, string verdict) => new KeyValuePair<string, string>(path, verdict);

    // Thousands of objects share a few verdicts: the summary gives one count per verdict, most frequent first, and names
    // the objects only of a verdict with few objects.
    static void SummaryPerLoad() {
        const string generated = "Not listed. Reason: Objects the game creates while running";
        const string crossing = "Listed as a scenery group. Category: Grade crossing";
        const string rig = "Not listed. Reason: Builder photo rig";
        var checks = Enumerable.Range(0, 2400).Select(i => Check("s/World/Track/Mask Container", generated))
            .Concat(Enumerable.Range(0, 3).Select(i => Check("s/World/Roads/Crossing " + (3 - i), crossing)))
            .Concat(new[] { Check("s/World/Builder Photo Setup/Camera", rig) })
            .ToArray();
        var lines = BakedSceneryChecks.Summary(checks, 3).ToArray();
        Assert(lines.SequenceEqual(new[] {
            "Baked scenery checks for 2404 candidate objects, by result:",
            "2400 objects: Not listed. Reason: Objects the game creates while running",
            "3 objects: Listed as a scenery group. Category: Grade crossing",
            "  s/World/Roads/Crossing 1",
            "  s/World/Roads/Crossing 2",
            "  s/World/Roads/Crossing 3",
            "1 object: Not listed. Reason: Builder photo rig",
            "  s/World/Builder Photo Setup/Camera",
        }), "The summary counts each verdict and names the objects of a verdict with few objects: " + String.Join(" | ", lines));
    }
}
