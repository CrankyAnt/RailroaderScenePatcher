// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher.Core {
    // The load in progress, shared by every part that writes a line during it. A load attempt begins when the local world
    // binding opens one, before the reconciliation of a retained world, and ends when the world-load callback has finished
    // and handed back the deferred restorations. While a load is in progress, PatcherLog starts each line with its
    // identifiers, so every line of one load carries the same attempt number, also from parts that know nothing of the
    // load. A line outside a load, such as one of an edit, a save or the window state, names its scene object or file.
    // The game runs one local world per process, so one shared load serves every part.
    public static class LoadAttempt {
        public const string Prefix = "load attempt ";
        static readonly object gate = new object();
        static int number;
        static string map;
        static bool active;
        // The number of the latest load attempt, counted from 1 in this process.
        public static int Number { get { lock (gate) return number; } }
        public static bool Active { get { lock (gate) return active; } }
        // The identifiers of the latest load attempt: its number and the loaded map.
        public static string Label { get { lock (gate) return LoadedScenePatchFile.AttemptLabel(number, map == null ? null : new[] { map }); } }
        // The identifiers of the load in progress, or null outside a load.
        public static string Current { get { lock (gate) return active ? LoadedScenePatchFile.AttemptLabel(number, map == null ? null : new[] { map }) : null; } }
        public static void Begin(string mapId) { lock (gate) { number++; map = mapId; active = true; } }
        // Another world-load callback of the latest load attempt continues it.
        public static void Resume() { lock (gate) if (number > 0) active = true; }
        public static void End() { lock (gate) active = false; }
        // Starts over, for a test that begins its own loads.
        public static void Reset() { lock (gate) { number = 0; map = null; active = false; } }
        // The line with the identifiers of the load in progress in front. A line that already starts with load identifiers
        // keeps its own, and an attempt trace keeps the prefix that test tools parse.
        internal static string Prefixed(string text) {
            var current = Current;
            if (current == null || text.StartsWith(Prefix, StringComparison.Ordinal) || text.StartsWith(PatchEngine.AttemptTrace, StringComparison.Ordinal)) return text;
            return current + ": " + text;
        }
    }
}
