// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

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
        // Whether a component of a hierarchy belongs to runtime output and not to the authored scene: an object the
        // game created while running, or anything below the container a mesh builder generates into. The builder
        // destroys every child of its container when it generates, so a mesh object that was saved with the scene
        // there is replaced the first time the pad comes into view; it is output as well.
        internal static bool IsOutput(bool createdAtRuntime, bool belowOutputContainer) => createdAtRuntime || belowOutputContainer;
        // Whether a component is one the game adds to an authored object while it runs. An animator of crossing
        // equipment, such as a signal's or a gate's KeyValueBoolAnimator, gets a playable-graph adapter beside its
        // Animator the first time it animates, which is when the camera first comes near. Such a component was not
        // saved with the scene: it is runtime state, and its arrival changes no observation.
        internal static bool IsRuntimeComponent(string typeName) => typeName == "Helpers.Animation.PlayableGraphAnimatorAdapter";
        internal const string GeneratedPad = "Generated pad mesh and culling sphere, rebuilt by native generation from the marker location";
        // The short form of dependency or runtime lines, which the Object tab shows by default: every line that names a
        // component of the inspected hierarchy by its label (scene/path [type]) joins one line per object, which names
        // the object by its path relative to the inspected one, or "This object", followed by its component types
        // without their namespace. Objects follow in the order they first appear, the inspected object first. A
        // reference line keeps its place with the relative path; any other line, also one that names an object outside
        // the hierarchy, stays as it is. The lines that stay come first.
        internal static IReadOnlyList<string> ShortForm(string scene, string path, IEnumerable<string> lines) {
            var all = (lines ?? Enumerable.Empty<string>()).ToArray();
            if (String.IsNullOrEmpty(scene) || String.IsNullOrEmpty(path)) return all;
            var root = scene + "/" + path;
            var kept = new List<string>();
            var objects = new List<string>();
            var components = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var line in all) {
                var label = FindComponentLabel(line, root);
                if (label == null) { kept.Add(line); continue; }
                var found = label.Value;
                var before = line.Substring(0, found.Start); var after = line.Substring(found.End);
                var entry = ComponentEntry(before, after, ShortType(found.Type));
                if (entry == null) { kept.Add(before + (found.RelativePath.Length == 0 ? "this object" : found.RelativePath) + " [" + found.Type + "]" + after); continue; }
                if (!components.TryGetValue(found.RelativePath, out var list)) { components[found.RelativePath] = list = new List<string>(); objects.Add(found.RelativePath); }
                list.Add(entry);
            }
            var ordered = objects.Where(o => o.Length == 0).Concat(objects.Where(o => o.Length != 0));
            return kept.Concat(ordered.Select(o => (o.Length == 0 ? "This object" : o) + ": " + String.Join(", ", components[o]))).ToArray();
        }
        // A component label (scene/path [type]) of an object at or below the inspected one, found in a line: where the
        // label starts and ends in the line, the object's path relative to the inspected object ("" for that object
        // itself), and the component's full type name.
        struct ComponentLabel {
            internal int Start, End;
            internal string RelativePath, Type;
        }
        // The first label in the line that names a component of the hierarchy below root, or null when it names none.
        // A sibling whose name only begins with the inspected object's name, such as "Crossing (1)" beside "Crossing",
        // is outside the hierarchy.
        static ComponentLabel? FindComponentLabel(string line, string root) {
            if (line == null) return null;
            for (var at = line.IndexOf(root, StringComparison.Ordinal); at >= 0; at = line.IndexOf(root, at + 1, StringComparison.Ordinal)) {
                var rest = at + root.Length;
                if (rest >= line.Length || (line[rest] != ' ' && line[rest] != '/')) continue;
                var open = line.IndexOf(" [", rest, StringComparison.Ordinal);
                var close = open < 0 ? -1 : line.IndexOf(']', open);
                if (close < 0) continue;
                var relative = line.Substring(rest, open - rest);
                if (relative.Length != 0 && relative[0] != '/') continue;
                return new ComponentLabel { Start = at, End = close + 1, RelativePath = relative.TrimStart('/'), Type = line.Substring(open + 2, close - open - 2) };
            }
            return null;
        }
        static string ShortType(string type) { var dot = type.LastIndexOf('.'); return dot < 0 ? type : type.Substring(dot + 1); }
        // The component as its object's short line lists it, from the text before and after its label: a component
        // line (HierarchyDependencies.Describe), an enabled line of the runtime state, or a runtime or generated object.
        // An enabled state reads "Type enabled" or "Type disabled" in every kind of line. Null for a line of another
        // kind, such as a reference, which keeps a line of its own.
        static string ComponentEntry(string before, string after, string type) {
            var state = after == "; enabled=True" || after == " enabled=True" ? " enabled" : after == "; enabled=False" || after == " enabled=False" ? " disabled" : null;
            if (before == "Component " && (after.Length == 0 || (state != null && after[0] == ';'))) return type + (state ?? "");
            if (before.Length == 0 && state != null && after[0] == ' ') return type + state;
            if (after.Length != 0) return null;
            if (before == "Runtime object ") return type + " (runtime object)";
            if (before == "Runtime component ") return type + " (runtime component)";
            if (before == "Generated pad object ") return type + " (generated pad object)";
            return null;
        }
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
