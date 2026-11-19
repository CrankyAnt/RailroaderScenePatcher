// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    public enum GameDefinedGroupKind { GradeCrossingSetup, BakedSceneryGroup, RailBridgeConstruction, IndustryStorageDisplay }
    // What a group shows in the world: each of its objects on its own, or one square marker for the group, with or
    // without the group's object frame.
    public enum GroupShows { Objects, MarkerAndFrame, MarkerOnly }

    // One inventory object as the facts a group is decided from: the scene object it is on, the scene objects above
    // that one, nearest first, and what its components say it is.
    public sealed class GroupObject {
        public GroupObject(object node, IEnumerable<object> above, bool crossingController = false, bool trackMarker = false, bool trackSpan = false, bool bakedGroupRoot = false,
            bool bakedPart = false, object storage = null, bool constructionController = false, IEnumerable<object> targets = null, IEnumerable<IEnumerable<object>> targetsAbove = null) {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Above = above ?? Enumerable.Empty<object>();
            CrossingController = crossingController; TrackMarker = trackMarker; TrackSpan = trackSpan; BakedGroupRoot = bakedGroupRoot;
            BakedPart = bakedPart; Storage = storage; ConstructionController = constructionController; Targets = (targets ?? Enumerable.Empty<object>()).Where(target => target != null).ToArray();
            TargetsAbove = (targetsAbove ?? Enumerable.Empty<IEnumerable<object>>()).Select(above => (IReadOnlyList<object>)(above ?? Enumerable.Empty<object>()).ToArray()).ToArray();
        }
        public object Node { get; }
        public IEnumerable<object> Above { get; }
        // The object is the root of a baked scenery group: its scene object is the root of the group, and the
        // inventory objects below it are the group's other objects.
        public bool BakedGroupRoot { get; }
        // The object is a listed part of a baked scenery group.
        public bool BakedPart { get; }
        // The object is a storage pile or log pond that an industry's storage fills: the scene object of that
        // storage, which names every pile it fills. Null for every other object.
        public object Storage { get; }
        // The object is a construction controller of a rail bridge: Targets holds the scene objects whose
        // visibility it sets.
        public bool ConstructionController { get; }
        public IReadOnlyList<object> Targets { get; }
        // For each target, in the order of Targets, the scene objects above it, nearest first. A target that is no
        // inventory object itself stands for the nearest inventory object above it, as the steel and abutments of
        // the Ela bridge stand below the bridge object. Without this fact only objects on and below a target count.
        public IReadOnlyList<IReadOnlyList<object>> TargetsAbove { get; }
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
        internal GameDefinedGroup(GameDefinedGroupKind kind, object anchor, IReadOnlyList<int> members, IReadOnlyList<int> listed, int root) { Kind = kind; Anchor = anchor; Members = members; Listed = listed; Root = root; }
        public GameDefinedGroupKind Kind { get; }
        // The scene object of the game component that holds the group together.
        public object Anchor { get; }
        public IReadOnlyList<int> Members { get; }
        // The members that the group's choice list offers and its object frame surrounds: every member other than the
        // invisible detection spans of a grade-crossing setup, which its controller manages. The spans stay members:
        // they show as the group, not on their own, while the group shows as one.
        public IReadOnlyList<int> Listed { get; }
        // The member that is the root of a baked scenery group with parts, or -1. That root stands for the group: it
        // shows as the group's square marker beyond the group distance and not among the parts at that distance or
        // less. A group without parts, such as a sign group whose objects are track markers, has none.
        public int Root { get; }
    }

    public static class GameDefinedGroups {
        // The groups among inventory objects. A grade-crossing setup is a crossing controller with the inventory
        // objects on and below its root, the controller's scene object, other than its whistle signs. A baked scenery
        // group is a group root with the inventory objects below it: its parts, whatever culling container they
        // stand in, and the objects of other families there, as a station building group holds its signs. An object
        // belongs to the outermost group that holds it. Two groups go by what a game component names, wherever
        // its objects stand: a rail bridge construction is a construction controller with the inventory objects on
        // and below the scene objects whose visibility it sets, and an industry storage display is the storage piles
        // or log pond that one industry's storage fills. Such an object belongs to that group, whatever root it
        // stands below, so an industry building holds no pile, unless it is a part of a baked scenery group: such a
        // pile belongs to that group, as the storage displays of the Baked scenery group family hold their piles as
        // parts. Only these facts decide: objects that stand close
        // together form no group.
        public static IReadOnlyList<GameDefinedGroup> Of(IReadOnlyList<GroupObject> objects) {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            var crossings = new HashSet<object>(objects.Where(o => o != null && o.CrossingController).Select(o => o.Node));
            var baked = new HashSet<object>(objects.Where(o => o != null && o.BakedGroupRoot).Select(o => o.Node));
            // The scene object a construction controller switches, with that controller's scene object.
            var switched = new Dictionary<object, object>();
            foreach (var controller in objects.Where(o => o != null && o.ConstructionController))
                foreach (var target in controller.Targets) if (!switched.ContainsKey(target)) switched[target] = controller.Node;
            // The inventory object that holds a switched object which is no inventory object itself: the nearest one
            // above it. It joins the construction alone; what else stands below it does not.
            var nodes = new HashSet<object>(objects.Where(o => o != null).Select(o => o.Node));
            var holders = new Dictionary<object, object>();
            foreach (var controller in objects.Where(o => o != null && o.ConstructionController))
                for (var t = 0; t < controller.Targets.Count && t < controller.TargetsAbove.Count; t++) {
                    if (nodes.Contains(controller.Targets[t])) continue;
                    var holder = controller.TargetsAbove[t].FirstOrDefault(node => node != null && nodes.Contains(node));
                    if (holder != null && !holders.ContainsKey(holder)) holders[holder] = controller.Node;
                }
            var members = new Dictionary<KeyValuePair<GameDefinedGroupKind, object>, List<int>>();
            var order = new List<KeyValuePair<GameDefinedGroupKind, object>>();
            for (var index = 0; index < objects.Count; index++) {
                var item = objects[index];
                if (item == null) continue;
                KeyValuePair<GameDefinedGroupKind, object>? key = null;
                if (item.ConstructionController) key = new KeyValuePair<GameDefinedGroupKind, object>(GameDefinedGroupKind.RailBridgeConstruction, item.Node);
                else if (switched.Count != 0) {
                    // The outermost switched object at or above the item names its construction.
                    object controller = switched.TryGetValue(item.Node, out var own) ? own : null;
                    foreach (var node in item.Above) if (node != null && switched.TryGetValue(node, out var outer)) controller = outer;
                    if (controller == null && holders.TryGetValue(item.Node, out var held)) controller = held;
                    if (controller != null) key = new KeyValuePair<GameDefinedGroupKind, object>(GameDefinedGroupKind.RailBridgeConstruction, controller);
                }
                // A pile that is a part of a baked scenery group belongs to that group, its outermost one, as the
                // storage displays the Baked scenery group family lists are; any other filled object to its storage.
                if (key == null && item.Storage != null && !item.BakedPart) key = new KeyValuePair<GameDefinedGroupKind, object>(GameDefinedGroupKind.IndustryStorageDisplay, item.Storage);
                if (key == null) {
                    object root = crossings.Contains(item.Node) || baked.Contains(item.Node) ? item.Node : null;
                    var below = false;
                    // Above runs from the nearest object outwards: the last root met is the outermost.
                    foreach (var node in item.Above) if (node != null && (crossings.Contains(node) || baked.Contains(node))) { root = node; below = true; }
                    if (root == null || (below && item.TrackMarker && crossings.Contains(root))) continue;
                    key = new KeyValuePair<GameDefinedGroupKind, object>(crossings.Contains(root) ? GameDefinedGroupKind.GradeCrossingSetup : GameDefinedGroupKind.BakedSceneryGroup, root);
                }
                if (!members.TryGetValue(key.Value, out var list)) { members[key.Value] = list = new List<int>(); order.Add(key.Value); }
                list.Add(index);
            }
            // A group with one inventory object shows as that object: it is no group.
            return order.Where(key => members[key].Count > 1).Select(key => {
                var held = members[key];
                var root = key.Key == GameDefinedGroupKind.BakedSceneryGroup && held.Any(i => objects[i].BakedPart) ? held.Cast<int?>().FirstOrDefault(i => ReferenceEquals(objects[i.Value].Node, key.Value) || Equals(objects[i.Value].Node, key.Value)) ?? -1 : -1;
                return new GameDefinedGroup(key.Key, key.Value, held.AsReadOnly(), held.Where(i => !objects[i].TrackSpan).ToArray(), root);
            }).ToArray();
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
        // shows its objects, and of a group that shows as one only a candidate and the inspected object. root names
        // the root of a group with parts (GameDefinedGroup.Root): it stands for the group, so beyond the group
        // distance only the group shows and at that distance or less only its parts; the root itself shows only as a
        // candidate or as the inspected object.
        public static bool ShowsOwn(GroupShows group, bool candidate, bool inspected, bool root = false) => (!root && group == GroupShows.Objects) || candidate || inspected;
        // Whether the root of a group with parts stands for the group among the candidates of a choice: root is its
        // index there, or negative when the choice does not offer it, and members are the group's objects the
        // choice offers. A choice that offers the root and none of its parts shows the root as any object, so
        // that it can still be picked near the group.
        public static bool RootStands(int root, IReadOnlyList<int> members) => root >= 0 && members != null && members.Contains(root) && members.Any(m => m != root);
        // The frame distance of a candidate or inspected object in a group that shows as one. While the group shows
        // its frame, the object shows its own frame too, although its own bounds may lie beyond the frame distance:
        // the distance counts from the group's bounds, as for the group's frame. Otherwise it is any object's.
        public static double OwnFrameDistance(GroupShows group) =>
            group == GroupShows.MarkerAndFrame ? Double.PositiveInfinity : ObjectMarkingGeometry.FrameDistance;
    }

    // The groups that world marking or the family skips: each shows its objects on its own, and one developer
    // diagnostic line (GameDefinedGroups.SkippedLine) says so, once per group root and reason.
    public sealed class GroupSkips {
        readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);
        readonly Action<string> log;
        public GroupSkips(Action<string> log) { this.log = log ?? throw new ArgumentNullException(nameof(log)); }
        public void Skip(string root, string reason) {
            var line = GameDefinedGroups.SkippedLine(root, reason);
            if (logged.Add(line)) log(line);
        }
        // Reads one group of world marking. A group whose objects cannot be read is skipped: the result is null. root
        // names the group's root; a normal build computes it only for a skipped group, and a root that cannot be computed
        // skips that group as an unknown group root.
        public T Read<T>(Func<string> root, Func<T> read) where T : class {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (read == null) throw new ArgumentNullException(nameof(read));
            try {
#if SCENE_PATCHER_FAULTS
                FaultInjection.TripUnreadableGroup(root);
#endif
                return read();
            } catch (Exception e) { Skip(RootOrNull(root), "its objects could not be read: " + e.GetBaseException().Message); return null; }
        }
        // Reads the parts of one group of the family. A group whose parts cannot be read keeps its root entry without
        // parts and is skipped: the result is false. root is computed as for Read.
        public bool ReadParts(Func<string> root, Action read) {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (read == null) throw new ArgumentNullException(nameof(read));
            try {
#if SCENE_PATCHER_FAULTS
                FaultInjection.TripUnreadableParts(root);
#endif
                read();
                return true;
            } catch (Exception e) { Skip(RootOrNull(root), "its parts could not be read: " + e.GetBaseException().Message); return false; }
        }
        static string RootOrNull(Func<string> root) { try { return root(); } catch { return null; } }
    }
}
