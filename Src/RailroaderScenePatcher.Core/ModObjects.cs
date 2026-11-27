// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

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
    // A scene whose mod objects hold scene markers below them: the pads of a duplicated crossing. Each takes a marker
    // ID of its own before it starts, as a mod object that is a scene marker does, so no pad of the source loses its
    // registration.
    public interface IModObjectPartScene {
        // The scene markers below the source that a mod object made from it holds, in hierarchy order; never the
        // source itself. Empty for a source without such markers.
        IReadOnlyList<object> PartMarkers(object source);
        // As IModObjectScene.Build. partMarkerIds gives each scene marker below the mod object its marker ID, in the
        // order of PartMarkers; each starts without a track location.
        object Build(TargetIdentity identity, object source, Placement placement, string markerId, IReadOnlyList<string> partMarkerIds);
    }
    // A crossing world that tells the mod objects among its controllers apart: a duplicated crossing starts without a
    // circuit and stands where its modification places it.
    public interface IModObjectCrossings {
        bool IsModObject(object controller);
    }
    // A scene that gives a mod object an object name of its own in the game: a duplicated spawn point's teleport name,
    // by which the game's teleport command and quick search find it. The identity of the mod object stays its target
    // path, which ends with its mark: the scene keeps resolving the object, and the objects below it, by that path.
    public interface IModObjectNames {
        // Gives the mod object the object name, or with null the name its target path gives it.
        void Name(object modObject, string name);
        // The object name the mod object has in the game now.
        string NameOf(object modObject);
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
        public const string TargetShape = "Invalid mod object: the target must name the source's scene and component. Its structure must match the source's, except that a part's copy uses scene-object-v1. Its path must end with [mod object <ID>].";
        public const string SourceMissing = "The source of this mod object doesn't match any object in the loaded map.";
        public const string SourceAmbiguous = "The source of this mod object matches more than one object in the loaded map.";
        public const string InvalidBuild = "Invalid mod object: the source's build must be a non-empty string.";
        // The Player log warning of a mod object built from an identical instance because its original is missing.
        public static string FallbackWarning(string scenePatchFile, string id, string original, string instance) =>
            "Mod object " + id + " in " + scenePatchFile + ": its source " + original + " isn't in the loaded map, so the patcher built the mod object from the identical object " + instance + ".";
        public const string Taken = "Another object already has this mod object's path.";
        public const string NeedsMarkerId = "Invalid mod object: a mod object that is a scene marker needs a marker ID of its own.";
        // The member of a duplicated spawn point's modification that holds its teleport name, and its refusals.
        public const string TeleportNameMember = "teleportName";
        public const string TeleportNameNotAString = "Invalid teleport name: a teleport name must be a non-empty string.";
        public const string TeleportNameOnlySpawnPoints = "Invalid teleport name: only a duplicated spawn point can have a teleport name.";
        public const string TeleportNameSlash = "A teleport name can't contain a slash (/), because a slash splits the hierarchy path.";
        public const string NamesUnavailable = "Mod object names are unavailable in this scene.";
        // Why the text can't be a teleport name, or null. Null and empty text clear the teleport name and pass.
        public static string TeleportNameProblem(string name) =>
            name == null || name.Length == 0 ? null : name.Trim().Length == 0 ? TeleportNameNotAString : name.IndexOf('/') >= 0 ? TeleportNameSlash : null;
        // Whether a mod object of the structure takes a teleport name.
        public static bool TakesTeleportName(string structure) => structure == SpawnPoints.Structure;
        public const string KeepsMarkerId = "A mod object keeps a marker ID of its own.";
        // The marker IDs of the scene markers below a mod object, the pads of a duplicated crossing, in hierarchy order.
        public const string PartMarkerIdsMember = "partMarkerIds";
        public const string PartMarkerIdsShape = "Invalid mod object: partMarkerIds must be a list of marker IDs that differ from each other and from markerId.";
        public const string PartMarkerIdsOnlyModObject = "Invalid modification: only a mod object can have partMarkerIds.";
        public static string PartMarkerIdCount(int pads, int ids) =>
            "Invalid mod object: partMarkerIds must contain a marker ID for each crossing pad of the source. The source has " + pads + " pads, and partMarkerIds has " + ids + " IDs.";
        // The global ID of a key-value object in a mod object, from the ID its source holds: the game keeps the values
        // of such an object, such as the lights and gates of a crossing, under that ID in the save and between the
        // peers of a multiplayer game, so a copy under its source's ID would share them. The mod object's mark makes
        // the ID its own, the same at every load and on every peer. An empty ID stays empty: the game registers none.
        public static string OwnGlobalId(string sourceId, string path) {
            var mark = MarkOf(path);
            return String.IsNullOrEmpty(sourceId) || mark == null ? sourceId : sourceId + " " + mark;
        }
        // The fields a copy never takes from its source, with the value the copy holds instead: the origin flag of a
        // milepost. Helpers.MilePost.Origin caches, per prefix, the first loaded post with the flag that
        // FindObjectsByType returns, in no fixed order, so a copy with the flag could become its prefix's origin.
        public static readonly IReadOnlyList<KeyValuePair<string, string>> NotCopied = new[] { new KeyValuePair<string, string>("Helpers.MilePost", "origin") };
        // Whether a mod object of the structure is a scene marker: it holds a marker ID of its own.
        // Whether a mod object of the structure stands beside the object that holds its source: a crossing pad's copy
        // stands beside the crossing, not in it.
        public static bool StandsBesideHolder(string structure) => structure == CrossingPadScene.Structure;
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
        // Orders mod object paths in the order their mod objects were placed: by mod, then by the placement sequence of
        // the modification key (Sequence), then ordinally. Duplicate gives each new mod object a sequence above every
        // mod object key in the file (NextKey), so a placement never moves ahead of one placed before it, whatever its
        // source and after a removal. Ordinal order would put the key -10 before -2 and both before the key itself.
        public static readonly IComparer<string> PlacementOrder = Comparer<string>.Create((a, b) => {
            var x = PlacementKey(a); var y = PlacementKey(b);
            var order = StringComparer.Ordinal.Compare(x.Mod, y.Mod);
            if (order == 0) order = x.Sequence.CompareTo(y.Sequence);
            return order != 0 ? order : StringComparer.Ordinal.Compare(a, b);
        });
        static (string Mod, long Sequence) PlacementKey(string path) {
            var mark = MarkOf(path);
            if (mark == null) return (path ?? "", 0);
            mark = mark.Substring(Open.Length - 1).TrimEnd(']');
            var space = mark.LastIndexOf(' ');
            return space < 0 ? ("", Sequence(mark)) : (mark.Substring(0, space), Sequence(mark.Substring(space + 1)));
        }
        // The placement sequence of a modification key: the number after its last hyphen, or 1 for a key without one,
        // such as the first mod object of a file.
        public static long Sequence(string key) {
            var dash = key?.LastIndexOf('-') ?? -1;
            if (dash > 0 && dash < key.Length - 1 && key.Length - dash <= 10 && key[dash + 1] != '0' && key.Substring(dash + 1).All(Char.IsDigit))
                return Int64.Parse(key.Substring(dash + 1), System.Globalization.CultureInfo.InvariantCulture);
            return 1;
        }
        // The key of a new mod object from its stem, such as mod-object-alarka-jct-station: the stem itself for the first
        // mod object of the file, and otherwise the stem with a sequence above every mod object key the file holds, so
        // a removal frees no number for a later placement.
        public static string NextKey(string stem, ICollection<string> used) {
            var held = used.Where(key => key.StartsWith(KeyPrefix, StringComparison.Ordinal)).ToArray();
            if (held.Length == 0 && !used.Contains(stem)) return stem;
            var n = Math.Max(1, held.Length == 0 ? 1 : held.Max(Sequence)) + 1;
            while (used.Contains(stem + "-" + n)) n++;
            return stem + "-" + n;
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
