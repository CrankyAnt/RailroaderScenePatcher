// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using RailroaderScenePatcher.Core;

// The one rule for names in running text: "a", "a and b", "a, b, and c", with the serial comma.
static class ProseTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run() {
        foreach (var (items, expected) in new[] {
            (new string[0], ""),
            (new[] { "a" }, "a"),
            (new[] { "a", "b" }, "a and b"),
            (new[] { "a", "b", "c" }, "a, b, and c"),
            (new[] { "a", "b", "c", "d" }, "a, b, c, and d") }) {
            var joined = Prose.Join(items);
            Assert(joined == expected, items.Length + " items join as \"" + expected + "\": \"" + joined + "\"");
        }
        Assert(PatchEditingSession.OutstandingWriteList(new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata }) == "map data, scene patch file, and patcher dependency",
            "The outstanding writes follow the rule");
        Console.WriteLine("PASS: names in running text join with the serial comma");
    }
}
