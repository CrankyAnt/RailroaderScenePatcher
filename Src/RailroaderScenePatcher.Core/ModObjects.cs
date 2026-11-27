// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;

namespace RailroaderScenePatcher.Core {
    // A scene that builds mod objects: objects a mod created with Duplicate from a supported scene object. A mod
    // object stands beside its source in the hierarchy, under the path its modification's target names, so the
    // capability of its family resolves and edits it like any other object of that family.
    public interface IModObjectScene {
        // Builds the mod object of the identity from the source object, at the placement, and returns it. Afterwards
        // the identity resolves to that object alone. The source is read and never changed. A mod object that is a
        // scene marker holds markerId as its marker ID before it starts, so it never registers under its source's.
        // It throws, and leaves nothing behind, when the object cannot be built or another object already stands
        // under the identity.
        object Build(TargetIdentity identity, object source, Placement placement, string markerId);
        // Removes a mod object this scene built, with everything below it.
        void Remove(object modObject);
    }
    // A scene that knows the model and build of its objects, so a mod object whose original is missing can be built from
    // an identical instance (ADR 0008).
    public interface IModObjectSources {
        // The signature of the object's model and build, or null when the scene cannot compute it for this object.
        string BuildOf(object source);
        // The loaded objects in the source's scene with the source's component whose model and build equal the build, in
        // the scene's fixed order: by hierarchy path in ordinal order, then by sibling index. Never ordered by distance.
        // Mod objects are not listed.
        IReadOnlyList<SourceInstance> Identical(TargetIdentity source, string build);
    }
    // A loaded object that can stand in for a missing original, with the hierarchy path the log names it by.
    public sealed class SourceInstance {
        public SourceInstance(string path, object handle) { Path = path; Handle = handle; }
        public string Path { get; }
        public object Handle { get; }
    }
    // The texts and the hierarchy path of mod objects. A mod object's name ends with a mark that holds the supplying
    // mod and the modification's key, so its path is unique among the loaded objects and names no place: the place of
    // a mod object comes from where it stands.
    public static class ModObjects {
        public const string SourceMember = "source", BuildMember = "build", RequiredVersion = "0.5.0", KeyPrefix = "mod-object-";
        const string Open = " [mod object ", Close = "]";
        public const string Unavailable = "Mod objects are unavailable in this scene.";
        public const string NeedsPlacement = "Invalid mod object: a mod object needs a placement.";
        public const string TargetShape = "Invalid mod object: the target must name the source's scene, component and structure, and a path that ends with [mod object <ID>].";
        public const string SourceMissing = "The source of this mod object doesn't match any object in the loaded map.";
        public const string SourceAmbiguous = "The source of this mod object matches more than one object in the loaded map.";
        public const string InvalidBuild = "Invalid mod object: the build of the source must be a non-empty string.";
        // The Player log warning of a mod object built from an identical instance because its original is missing.
        public static string FallbackWarning(string scenePatchFile, string id, string original, string instance) =>
            "Mod object " + id + " in " + scenePatchFile + ": its source " + original + " isn't in the loaded map, so the patcher built it from the identical object " + instance + ".";
        public const string Taken = "Another object already stands at the path of this mod object.";
        public const string NeedsMarkerId = "Invalid mod object: a mod object that is a scene marker needs a marker ID of its own.";
        public const string KeepsMarkerId = "A mod object keeps a marker ID of its own.";
        // The fields a copy never takes from its source, with the value the copy holds instead: the origin flag of a
        // milepost. Helpers.MilePost.Origin caches, per prefix, the first loaded post with the flag that
        // FindObjectsByType returns, in no fixed order, so a copy with the flag could become its prefix's origin.
        public static readonly IReadOnlyList<KeyValuePair<string, string>> NotCopied = new[] { new KeyValuePair<string, string>("Helpers.MilePost", "origin") };
        // Whether a mod object of the structure is a scene marker: it holds a marker ID of its own.
        public static bool IsSceneMarker(string structure) => structure == TrackMarkerScene.Structure || structure == MilepostScene.Structure || structure == CrossingPadScene.Structure;
        // Whether a mod object of the structure binds at placement to the track the game finds at its position.
        public static bool BindsAtPlacement(string structure) => structure == TrackMarkerScene.Structure || structure == MilepostScene.Structure;
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
        // The mark of the mod object that the path names or lies in, such as "[mod object author:pack key]", or null.
        // The mark holds the mod and the modification key, so it names one mod object wherever it stands.
        public static string MarkOf(string path) {
            var root = RootOf(path);
            if (root == null) return null;
            var name = NameOf(root);
            return name.Substring(name.LastIndexOf(Open, StringComparison.Ordinal) + 1);
        }
        // The path by which identities compare: a path in a mod object is its mark and the rest below it, because a mod
        // object built beside an identical instance stands in another parent than its written target names. Any other
        // path is itself.
        public static string Canonical(string path) {
            var root = RootOf(path);
            return root == null ? path : MarkOf(root) + path.Substring(root.Length);
        }
        public static bool SamePath(string a, string b) => a == b || (a != null && b != null && Canonical(a) == Canonical(b));
        // The path where the object stands, for a path written below a mod object that now stands at another root.
        public static string Moved(string path, string standingRoot) {
            var root = RootOf(path);
            return root == null || standingRoot == null ? path : standingRoot + path.Substring(root.Length);
        }
        // The path without the marks of mod objects: the path of the source, by which the rules name its kind.
        public static string Plain(string path) {
            if (path == null) return null;
            var segments = path.Split('/');
            for (var i = 0; i < segments.Length; i++)
                while (IsName(segments[i])) segments[i] = segments[i].Substring(0, segments[i].LastIndexOf(Open, StringComparison.Ordinal));
            return String.Join("/", segments);
        }
        // The structure of a mod object made from a source of the structure: a part's copy is a scene object of its own.
        public static string StructureOf(string sourceStructure) => sourceStructure == ChildObjects.Structure ? SceneObjects.Structure : sourceStructure;
        // The path a mod object made from the source is named after and stands beside. A part's copy stands beside the
        // part's group, in the group's parent, under the part's own name; any other copy stands beside its source.
        // group is the path of the source's group, or null for an object that is no part.
        public static string Beside(string sourcePath, string group) {
            var source = Plain(sourcePath);
            var parent = group == null ? null : ParentOf(Plain(group));
            return group == null ? source : parent == null ? NameOf(source) : parent + "/" + NameOf(source);
        }
        // The parent a mod object stands in when it is built from the object at builtFrom: as many levels above that
        // object as its written target's parent stands above its written source. A copy stands beside its source, and a
        // part's copy beside the source part's group, also when an identical instance stands in for the original.
        // Without that relation it stands beside the object it is built from.
        public static string StandsIn(string target, string source, string builtFrom) {
            string written = ParentOf(target), above = ParentOf(source), parent = ParentOf(builtFrom);
            if (written == null || above == null || !(above + "/").StartsWith(written + "/", StringComparison.Ordinal)) return parent;
            for (var at = above; at != written && parent != null; at = ParentOf(at)) parent = ParentOf(parent);
            return parent;
        }
        // The name of a mod object in its parent: the last segment of its path.
        public static string NameOf(string path) => path.Substring(path.LastIndexOf('/') + 1);
        public static string ParentOf(string path) { var slash = path.LastIndexOf('/'); return slash < 0 ? null : path.Substring(0, slash); }
    }
}
