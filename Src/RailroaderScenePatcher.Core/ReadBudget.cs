// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher.Core {
    // The share of one pass that reads which are made once and then kept may take, such as a renderer's kind or the
    // height of the ground at a place. Only the reads themselves are timed: work on what is known already costs
    // nothing here, so a pass that is slow for another reason still reads. A pass starts with Begin. Once the share is
    // used up, Exhausted says so and the caller leaves what is not read yet for a later pass; a value that was read
    // is kept by the caller and is never asked for again, so every pass reads further and the reads come to an end.
    public sealed class ReadBudget {
        readonly double limit;
        readonly Func<double> clock;
        double spent;
        // limit and the clock's value are milliseconds.
        public ReadBudget(double limit, Func<double> clock) {
            this.limit = limit; this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }
        public void Begin() { spent = 0; }
        public bool Exhausted => spent > limit;
        public double Spent => spent;
        public T Read<T>(Func<T> read) {
            var start = clock();
            try { return read(); }
            finally { spent += Math.Max(0, clock() - start); }
        }
    }
}
