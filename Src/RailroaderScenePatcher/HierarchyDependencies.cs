// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System.Collections.Generic;

namespace RailroaderScenePatcher {
    // One component of an inspected hierarchy, with copied values: its label, its enabled state when it is a behaviour,
    // whether it belongs to a runtime-generated (DontSave) object, and the direct references to it.
    internal sealed class HierarchyPart {
        internal HierarchyPart(string label, bool? enabled, bool generated, IEnumerable<string> incoming = null) {
            Label = label; Enabled = enabled; Generated = generated; Incoming = incoming ?? new string[0];
        }
        internal string Label { get; }
        internal bool? Enabled { get; }
        internal bool Generated { get; }
        internal IEnumerable<string> Incoming { get; }
    }
    // The dependency lines of an inspected hierarchy, which enter the observation fingerprint. A crossing pad's
    // generated mesh objects are runtime output: generation replaces them and culling toggles their renderers, so they
    // are described by one line instead of per component, and neither changes the observation. UI-independent, shared
    // with controlled replay.
    internal static class HierarchyDependencies {
        internal const string GeneratedPad = "Generated pad mesh and culling sphere, rebuilt by native generation from the marker location";
        internal static IEnumerable<string> Describe(bool crossingPad, IEnumerable<HierarchyPart> parts) {
            if (crossingPad) yield return GeneratedPad;
            foreach (var part in parts) {
                if (crossingPad && part.Generated) continue;
                yield return "Component " + part.Label + (part.Enabled.HasValue ? "; enabled=" + (part.Enabled.Value ? "True" : "False") : "");
                foreach (var reference in part.Incoming) yield return reference;
            }
        }
    }
}
