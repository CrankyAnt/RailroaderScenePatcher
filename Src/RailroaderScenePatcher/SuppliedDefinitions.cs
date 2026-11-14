// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // Whether the supplied definitions, the patch documents of the loaded packs, are still the ones the last refresh
    // read. Stale says that something other than this panel changed them: a document edited, added or removed outside
    // the editor. The patcher's own Save is not such a change: what it wrote is what the editor already holds, so the
    // written text becomes the text to compare against. UI-independent, shared with controlled replay.
    internal sealed class SuppliedDefinitions {
        // Per pack, the document text and the read error of the last refresh, with the patcher's own writes since.
        Dictionary<string, KeyValuePair<string, string>> baseline;
        // True until the first refresh, and from an outside change until the next refresh.
        internal bool Stale { get; private set; } = true;
        // A new load attempt starts over: nothing is read yet.
        internal void Reset() { baseline = null; Stale = true; }
        // A refresh read these documents: they are current.
        internal void Read(IEnumerable<LoadedPack> documents) { baseline = Copy(documents); Stale = false; }
        // The documents as they are now. A difference from the baseline marks them stale; only a refresh clears that.
        internal void Compare(IEnumerable<LoadedPack> documents) {
            if (baseline == null) { Stale = true; return; }
            var current = Copy(documents);
            if (current.Count != baseline.Count) { Stale = true; return; }
            foreach (var pack in current)
                if (!baseline.TryGetValue(pack.Key, out var known) || known.Key != pack.Value.Key || known.Value != pack.Value.Value) { Stale = true; return; }
        }
        // The documents could not be read: nothing vouches for them.
        internal void Unreadable() { Stale = true; }
        // The patcher wrote this pack's document itself. The written text is what later comparisons expect for that
        // pack; a document that was already stale stays stale, because the write does not say what else changed.
        internal void Written(string pack, string text) {
            if (baseline == null || String.IsNullOrEmpty(pack)) return;
            baseline[pack] = new KeyValuePair<string, string>(text, null);
        }
        static Dictionary<string, KeyValuePair<string, string>> Copy(IEnumerable<LoadedPack> documents) {
            var copy = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);
            foreach (var pack in documents) copy[pack.Source.Identifier] = new KeyValuePair<string, string>(pack.Json, pack.Error);
            return copy;
        }
    }
}
