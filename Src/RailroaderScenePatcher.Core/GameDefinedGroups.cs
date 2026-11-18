// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    public enum GameDefinedGroupKind { GradeCrossingSetup, BakedSceneryGroup }
    // What a group shows in the world: each of its objects on its own, or one square marker for the group, with or
    // without the group's object frame.
    public enum GroupShows { Objects, MarkerAndFrame, MarkerOnly }

    // One inventory object as the facts a group is decided from: the scene object it is on, the scene objects above
    // that one, nearest first, and what its components say it is.
    public sealed class GroupObject {
        public GroupObject(object node, IEnumerable<object> above, bool crossingController = false, bool trackMarker = false, bool trackSpan = false, bool bakedGroupRoot = false) {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Above = above ?? Enumerable.Empty<object>();
            CrossingController = crossingController; TrackMarker = trackMarker; TrackSpan = trackSpan; BakedGroupRoot = bakedGroupRoot;
        }
        public object Node { get; }
        public IEnumerable<object> Above { get; }
        // The object is the root of a listed baked scenery group: its scene object is the root of the group, and the
        // listed parts below it are the group's other objects.
        public bool BakedGroupRoot { get; }
        // The object is a crossing controller: its scene object is the root of a grade-crossing setup.
        public bool CrossingController { get; }
        // The object is a track marker other than a crossing pad. The game has no component for a whistle sign: below
        // a crossing controller's root, such a marker is a whistle sign of that crossing.
        public bool TrackMarker { get; }
        // The object is a track span. Below a crossing controller's root, such a span is a detection span of that
        // crossing: a member of the setup that its controller manages.
        public bool TrackSpan { get; }
    }

    // Scene objects that a game component holds together. Members are indices into the objects the group was decided
    // from, in their order.
    public sealed class GameDefinedGroup {
        internal GameDefinedGroup(GameDefinedGroupKind kind, object anchor, IReadOnlyList<int> members, IReadOnlyList<int> listed) { Kind = kind; Anchor = anchor; Members = members; Listed = listed; }
        public GameDefinedGroupKind Kind { get; }
        // The scene object of the game component that holds the group together.
        public object Anchor { get; }
        public IReadOnlyList<int> Members { get; }
        // The members that the group's choice list offers and its object frame surrounds: every member other than the
        // invisible detection spans of a grade-crossing setup, which its controller manages. The spans stay members:
        // they show as the group, not on their own, while the group shows as one.
        public IReadOnlyList<int> Listed { get; }
    }

    public static class GameDefinedGroups {
        // The groups among inventory objects. A grade-crossing setup is a crossing controller with the inventory
        // objects on and below its root, the controller's scene object, other than its whistle signs. A baked scenery
        // group is a listed group root with the inventory objects below it: its parts, whatever culling container
        // they stand in. An object belongs to the outermost group that holds it. Only the hierarchy decides: objects
        // that stand close together form no group.
        public static IReadOnlyList<GameDefinedGroup> Of(IReadOnlyList<GroupObject> objects) {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            var crossings = new HashSet<object>(objects.Where(o => o != null && o.CrossingController).Select(o => o.Node));
            var baked = new HashSet<object>(objects.Where(o => o != null && o.BakedGroupRoot).Select(o => o.Node));
            var members = new Dictionary<object, List<int>>();
            var order = new List<object>();
            if (crossings.Count != 0 || baked.Count != 0)
                for (var index = 0; index < objects.Count; index++) {
                    var item = objects[index];
                    if (item == null) continue;
                    object root = crossings.Contains(item.Node) || baked.Contains(item.Node) ? item.Node : null;
                    var below = false;
                    // Above runs from the nearest object outwards: the last root met is the outermost.
                    foreach (var node in item.Above) if (node != null && (crossings.Contains(node) || baked.Contains(node))) { root = node; below = true; }
                    if (root == null || (below && item.TrackMarker && crossings.Contains(root))) continue;
                    if (!members.TryGetValue(root, out var list)) { members[root] = list = new List<int>(); order.Add(root); }
                    list.Add(index);
                }
            // A group with one inventory object shows as that object: it is no group.
            return order.Where(root => members[root].Count > 1)
                .Select(root => new GameDefinedGroup(crossings.Contains(root) ? GameDefinedGroupKind.GradeCrossingSetup : GameDefinedGroupKind.BakedSceneryGroup, root, members[root].AsReadOnly(),
                    members[root].Where(i => !objects[i].TrackSpan).ToArray())).ToArray();
        }
        // The developer diagnostic line for a group that world marking or the family skips: one line per group root
        // and reason, in the Player log.
        public static string SkippedLine(string root, string reason) => "Group skipped: " + (root ?? "unknown group root") + ": " + (reason ?? "no reason given");

        // Beyond this distance from the camera to a group's bounds, in metres, the group shows as one square marker
        // and one object frame; at this distance or less each of its objects shows on its own.
        public const double GroupDistance = 125;
        // What a group shows at a distance from the camera to its bounds. Like any object frame, the group's frame
        // shows only within the frame distance. A group whose distance is unknown shows its objects.
        public static GroupShows Shows(double distance) =>
            !(distance > GroupDistance) ? GroupShows.Objects : distance <= ObjectMarkingGeometry.FrameDistance ? GroupShows.MarkerAndFrame : GroupShows.MarkerOnly;
        // Whether an object of a group shows its own square marker and object frame: every object of a group that
        // shows its objects, and of a group that shows as one only a candidate and the inspected object.
        public static bool ShowsOwn(GroupShows group, bool candidate, bool inspected) => group == GroupShows.Objects || candidate || inspected;
        // The frame distance of a candidate or inspected object in a group that shows as one. While the group shows
        // its frame, the object shows its own frame too, although its own bounds may lie beyond the frame distance:
        // the distance counts from the group's bounds, as for the group's frame. Otherwise it is any object's.
        public static double OwnFrameDistance(GroupShows group) =>
            group == GroupShows.MarkerAndFrame ? Double.PositiveInfinity : ObjectMarkingGeometry.FrameDistance;
    }
}
