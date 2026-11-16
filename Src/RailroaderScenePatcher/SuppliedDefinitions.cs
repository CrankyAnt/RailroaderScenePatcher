// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // Whether the supplied definitions, the patch documents of the loaded mods, are still the ones the last refresh
    // read. Stale says that something other than this panel changed them: a document edited, added or removed outside
    // scene patch editing. The patcher's own Save is not such a change: what it wrote is what scene patch editing already holds, so the
    // written text becomes the text to compare against. UI-independent, shared with controlled replay.
    internal sealed class SuppliedDefinitions {
        // Per mod, the document text and the read error of the last refresh, with the patcher's own writes since.
        Dictionary<string, KeyValuePair<string, string>> baseline;
        // True until the first refresh, and from an outside change until the next refresh.
        internal bool Stale { get; private set; } = true;
        // A new load attempt starts over: nothing is read yet.
        internal void Reset() { baseline = null; Stale = true; }
        // A refresh read these documents: they are current.
        internal void Read(IEnumerable<LoadedMod> documents) { baseline = Copy(documents); Stale = false; }
        // The documents as they are now. A difference from the baseline marks them stale; only a refresh clears that.
        internal void Compare(IEnumerable<LoadedMod> documents) {
            if (baseline == null) { Stale = true; return; }
            var current = Copy(documents);
            if (current.Count != baseline.Count) { Stale = true; return; }
            foreach (var mod in current)
                if (!baseline.TryGetValue(mod.Key, out var known) || known.Key != mod.Value.Key || known.Value != mod.Value.Value) { Stale = true; return; }
        }
        // The documents could not be read: nothing vouches for them.
        internal void Unreadable() { Stale = true; }
        // The patcher wrote this mod's document itself. The written text is what later comparisons expect for that
        // mod; a document that was already stale stays stale, because the write does not say what else changed.
        internal void Written(string mod, string text) {
            if (baseline == null || String.IsNullOrEmpty(mod)) return;
            baseline[mod] = new KeyValuePair<string, string>(text, null);
        }
        static Dictionary<string, KeyValuePair<string, string>> Copy(IEnumerable<LoadedMod> documents) {
            var copy = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);
            foreach (var mod in documents) copy[mod.Source.Identifier] = new KeyValuePair<string, string>(mod.Json, mod.Error);
            return copy;
        }
    }
}
