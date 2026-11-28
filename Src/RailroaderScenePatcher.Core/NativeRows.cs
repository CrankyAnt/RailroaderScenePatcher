// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The game's Log window. It is ready once the game's message collector runs; before that the game writes a row only to
    // its file log, which is no row a player sees. The game writes every row it shows to the player log itself.
    public interface INativeRowOutput {
        bool Ready { get; }
        void Warning(string originator, string message, string key);
        void Notice(string originator, string message, string key);
    }

    // The patcher's Log window rows of one load attempt, each delivered once. The game writes a delivered row to the player
    // log, so the patcher writes no line beside it. A row for a patcher failure keeps the repair step of its reason, since
    // the Scene Patcher window is not open in play mode. A row keeps the identifiers of the load it was raised in. A row that can't be delivered is written once in player mode instead:
    // when the Log window fails, and when the load attempt or the world ends before the Log window is ready.
    public sealed class NativeRows {
        sealed class Row { internal string Key, Originator, Message, Load; internal bool Notice; }
        // The row's load identifiers in front of a line about the row, or nothing for a row raised outside a load.
        static string In(Row row) => row.Load == null ? "" : row.Load + ": ";
        readonly INativeRowOutput output;
        readonly List<Row> pending = new List<Row>();
        readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        public NativeRows(INativeRowOutput output) { this.output = output ?? throw new ArgumentNullException(nameof(output)); }
        // A failure warning row; a later row with the same key in this load attempt is left out.
        public void Warning(string key, string originator, string message) => Add(key, originator, message, false);
        // An informational row, such as an overlap notice; a later notice with the same key replaces a pending one.
        public void Notice(string key, string originator, string message) {
            var waiting = pending.FirstOrDefault(r => r.Notice && r.Key == key);
            if (waiting != null) { waiting.Originator = originator; waiting.Message = message; Flush(); return; }
            Add(key, originator, message, true, true);
        }
        void Add(string key, string originator, string message, bool notice, bool replace = false) {
            if (!replace && !seen.Add("w/" + key)) return;
            pending.Add(new Row { Key = key, Originator = originator, Message = message, Notice = notice, Load = LoadAttempt.Current });
            Flush();
        }
        public bool HasPending => pending.Count != 0;
        public void Flush() {
            bool ready;
            try { ready = output.Ready; } catch { ready = false; }
            if (!ready) return;
            while (pending.Count != 0) {
                var row = pending[0];
                try {
                    if (row.Notice) output.Notice(row.Originator, row.Message, row.Key);
                    else output.Warning(row.Originator, row.Message, row.Key);
                } catch (Exception e) {
                    PatcherLog.Debug(() => In(row) + "Log window message delivery failed for row " + row.Key + " from " + row.Originator + ": " + e);
                    Abandon();
                    return;
                }
                pending.RemoveAt(0);
            }
        }
        // Writes every row not yet delivered once in player mode and forgets it, as the load attempt or world ends.
        public void Abandon() {
            var rows = pending.ToArray();
            pending.Clear();
            foreach (var row in rows) PatcherLog.Warning(In(row) + "Log window message delivery failed or the Log window wasn't ready: " + row.Originator + ": " + row.Message + " Report delivery failures to the patcher maintainer.");
        }
    }
}
