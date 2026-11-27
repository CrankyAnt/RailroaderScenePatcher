// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;

namespace RailroaderScenePatcher.Core {
    // A scene that builds mod objects: objects a mod created with Duplicate from a supported scene object. A mod
    // object stands beside its source in the hierarchy, under the path its modification's target names, so the
    // capability of its family resolves and edits it like any other object of that family.
    public interface IModObjectScene {
        // Builds the mod object of the identity from the source object, at the placement, and returns it. Afterwards
        // the identity resolves to that object alone. The source is read and never changed. It throws, and leaves
        // nothing behind, when the object cannot be built or another object already stands under the identity.
        object Build(TargetIdentity identity, object source, Placement placement);
        // Removes a mod object this scene built, with everything below it.
        void Remove(object modObject);
    }
    // The texts and the hierarchy path of mod objects. A mod object's name ends with a mark that holds the supplying
    // mod and the modification's key, so its path is unique among the loaded objects and names no place: the place of
    // a mod object comes from where it stands.
    public static class ModObjects {
        public const string SourceMember = "source", RequiredVersion = "0.5.0", KeyPrefix = "mod-object-";
        const string Open = " [mod object ", Close = "]";
        public const string Unavailable = "Mod objects are unavailable in this scene.";
        public const string NeedsPlacement = "Invalid mod object: a mod object needs a placement.";
        public const string TargetShape = "Invalid mod object: the target must name the source's scene, component and structure, and a path that ends with [mod object <ID>].";
        public const string SourceMissing = "The source of this mod object doesn't match any object in the loaded map.";
        public const string SourceAmbiguous = "The source of this mod object matches more than one object in the loaded map.";
        public const string Taken = "Another object already stands at the path of this mod object.";
        // A slash would split the name into two path segments.
        static string Safe(string text) => (text ?? "").Replace('/', ':');
        // The path of the mod object that the mod makes from the source under the modification key.
        public static string Path(string sourcePath, string modId, string key) => Plain(sourcePath) + Open + Safe(modId) + " " + Safe(key) + Close;
        static bool IsName(string name) => name.EndsWith(Close, StringComparison.Ordinal) && name.IndexOf(Open, StringComparison.Ordinal) > 0;
        // Whether the path is the path of a mod object itself.
        public static bool IsPath(string path) => path != null && IsName(path.Substring(path.LastIndexOf('/') + 1));
        // The path of the mod object that the path names or lies in, or null for any other path.
        public static string RootOf(string path) {
            if (path == null) return null;
            for (var start = 0; start <= path.Length;) {
                var end = path.IndexOf('/', start);
                if (end < 0) end = path.Length;
                if (IsName(path.Substring(start, end - start))) return path.Substring(0, end);
                start = end + 1;
            }
            return null;
        }
        public static bool Within(string path) => RootOf(path) != null;
        // The path without the marks of mod objects: the path of the source, by which the rules name its kind.
        public static string Plain(string path) {
            if (path == null) return null;
            var segments = path.Split('/');
            for (var i = 0; i < segments.Length; i++)
                while (IsName(segments[i])) segments[i] = segments[i].Substring(0, segments[i].LastIndexOf(Open, StringComparison.Ordinal));
            return String.Join("/", segments);
        }
        // The name of a mod object in its parent: the last segment of its path.
        public static string NameOf(string path) => path.Substring(path.LastIndexOf('/') + 1);
        public static string ParentOf(string path) { var slash = path.LastIndexOf('/'); return slash < 0 ? null : path.Substring(0, slash); }
    }
}
