// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // Remembers count changes that cannot be attributed to patch-only history.
    public sealed class NativeDirtyTracker {
        readonly INativeHistory history;
        object[] undo;
        int count;
        bool dirty;
        bool initialDirty, executing;
        readonly int cleanCount;
        public bool InitialDirtyPending => initialDirty;

        public NativeDirtyTracker(INativeHistory history, int count, bool initiallyDirty, int cleanCount) {
            this.history = history;
            if (!(history is INativeHistoryActions)) throw new ArgumentException("History action inspection unavailable", nameof(history));
            this.count = count;
            initialDirty = initiallyDirty;
            this.cleanCount = cleanCount;
            undo = history.UndoGroups.ToArray();
        }
        public void BeginHistoryExecution() { executing = true; }
        public void EndHistoryExecution() { executing = false; }
        public bool Observe(int currentCount, bool upgraded) {
            if (executing) return initialDirty || dirty || upgraded;
            var current = history.UndoGroups.ToArray();
            // Grouped work is tracked by the session's history identity. Any count
            // movement beyond its stack-depth change represents ungrouped work.
            var pruned = current.Length == 0 ? 0 : Array.IndexOf(undo, current[0]);
            if (pruned < 0) pruned = 0;
            if (currentCount - count != current.Length - undo.Length + pruned) dirty = true;
            if (currentCount == cleanCount) initialDirty = false;
            undo = current;
            count = currentCount;
            return initialDirty || dirty || upgraded;
        }
        public void Saved(int currentCount) { count = currentCount; undo = history.UndoGroups.ToArray(); dirty = false; initialDirty = false; }
    }
}
