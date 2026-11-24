// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // Whether the supplied definitions, the scene patch files of the loaded mods, are still the ones the last refresh
    // read. Stale says that something other than this panel changed them: a scene patch file edited, added or removed outside
    // scene patch editing. A scene patch file counts as edited when its content differs under the Saved equality of the scene patch file,
    // so another member order, spacing or number literal is no change. The patcher's own Save is not such a change: what it wrote is what scene patch editing already holds, so the
    // written text becomes the text to compare against. UI-independent, shared with controlled replay.
    internal sealed class SuppliedDefinitions {
        // Per mod, the scene patch file text and the read error of the last refresh, with the patcher's own writes since.
        Dictionary<string, KeyValuePair<string, string>> baseline;
        // True until the first refresh, and from an outside change until the next refresh.
        internal bool Stale { get; private set; } = true;
        // True from the patcher's own write until what Session details shows of the supplied definitions is read again:
        // the write changed the file, not the lamp, so the shown lines follow the file without a refresh.
        internal bool ShownOutdated { get; private set; }
        // A new load attempt starts over: nothing is read yet.
        internal void Reset() { baseline = null; Stale = true; ShownOutdated = false; }
        // A refresh read these scene patch files: they are current.
        internal void Read(IEnumerable<LoadedMod> scenePatchFiles) { baseline = Copy(scenePatchFiles); Stale = false; ShownOutdated = false; }
        // The shown lines were read again after a write.
        internal void Shown() { ShownOutdated = false; }
        // The scene patch files as they are now. A difference from the baseline marks them stale; only a refresh clears that.
        internal void Compare(IEnumerable<LoadedMod> scenePatchFiles) {
            if (baseline == null) { Stale = true; return; }
            var current = Copy(scenePatchFiles);
            if (current.Count != baseline.Count) { Stale = true; return; }
            foreach (var mod in current) {
                if (!baseline.TryGetValue(mod.Key, out var known) || known.Value != mod.Value.Value || !ScenePatchFile.SameContent(known.Key, mod.Value.Key)) { Stale = true; return; }
                // Equal content in another form: later comparisons expect this text, so they need not parse it again.
                if (!ReferenceEquals(known.Key, mod.Value.Key)) baseline[mod.Key] = mod.Value;
            }
        }
        // The scene patch files could not be read: nothing vouches for them.
        internal void Unreadable() { Stale = true; }
        // The patcher wrote this mod's scene patch file itself. The written text is what later comparisons expect for that
        // mod; a scene patch file that was already stale stays stale, because the write does not say what else changed.
        internal void Written(string mod, string text) {
            if (baseline == null || String.IsNullOrEmpty(mod)) return;
            baseline[mod] = new KeyValuePair<string, string>(text, null);
            ShownOutdated = true;
        }
        static Dictionary<string, KeyValuePair<string, string>> Copy(IEnumerable<LoadedMod> scenePatchFiles) {
            var copy = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);
            foreach (var mod in scenePatchFiles) copy[mod.Source.Identifier] = new KeyValuePair<string, string>(mod.Json, mod.Error);
            return copy;
        }
    }
}
