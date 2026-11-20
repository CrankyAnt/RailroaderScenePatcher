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
        // without their namespace. Objects follow in the order they first appear. A reference line keeps its place
        // with the relative path; any other line, also one that names an object outside the hierarchy, stays as it is.
        // The lines that stay come first.
        internal static IReadOnlyList<string> ShortForm(string scene, string path, IEnumerable<string> lines) {
            var all = (lines ?? Enumerable.Empty<string>()).ToArray();
            if (String.IsNullOrEmpty(scene) || String.IsNullOrEmpty(path)) return all;
            var root = scene + "/" + path;
            var kept = new List<string>();
            var objects = new List<string>();
            var items = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var line in all) {
                var label = Label(line, root);
                if (label == null) { kept.Add(line); continue; }
                var (start, end, relative, type) = label.Value;
                var before = line.Substring(0, start); var after = line.Substring(end);
                var item = Item(before, after, ShortType(type));
                if (item == null) { kept.Add(before + (relative.Length == 0 ? "this object" : relative) + " [" + type + "]" + after); continue; }
                if (!items.TryGetValue(relative, out var list)) { items[relative] = list = new List<string>(); objects.Add(relative); }
                list.Add(item);
            }
            var ordered = objects.Where(o => o.Length == 0).Concat(objects.Where(o => o.Length != 0));
            return kept.Concat(ordered.Select(o => (o.Length == 0 ? "This object" : o) + ": " + String.Join(", ", items[o]))).ToArray();
        }
        // Where a line names a component of the hierarchy below root: the label's start and end, the object's path
        // relative to root, and the component type. Null when the line names none.
        static (int, int, string, string)? Label(string line, string root) {
            if (line == null) return null;
            for (var at = line.IndexOf(root, StringComparison.Ordinal); at >= 0; at = line.IndexOf(root, at + 1, StringComparison.Ordinal)) {
                var rest = at + root.Length;
                if (rest >= line.Length || (line[rest] != ' ' && line[rest] != '/')) continue;
                var open = line.IndexOf(" [", rest, StringComparison.Ordinal);
                var close = open < 0 ? -1 : line.IndexOf(']', open);
                if (close < 0) continue;
                var relative = line.Substring(rest, open - rest);
                if (relative.Length != 0 && relative[0] != '/') continue;
                return (at, close + 1, relative.TrimStart('/'), line.Substring(open + 2, close - open - 2));
            }
            return null;
        }
        static string ShortType(string type) { var dot = type.LastIndexOf('.'); return dot < 0 ? type : type.Substring(dot + 1); }
        // A component as its object's line names it, or null for a line of another kind.
        static string Item(string before, string after, string type) {
            if (before == "Component " && after.Length == 0) return type;
            if (before == "Component " && after == "; enabled=True") return type + " (enabled)";
            if (before == "Component " && after == "; enabled=False") return type + " (disabled)";
            if (before.Length == 0 && after == " enabled=True") return type + " enabled";
            if (before.Length == 0 && after == " enabled=False") return type + " disabled";
            if (after.Length == 0 && before == "Runtime object ") return type + " (runtime object)";
            if (after.Length == 0 && before == "Runtime component ") return type + " (runtime component)";
            if (after.Length == 0 && before == "Generated pad object ") return type + " (generated pad object)";
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
