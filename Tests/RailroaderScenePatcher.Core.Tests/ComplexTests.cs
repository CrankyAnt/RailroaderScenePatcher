// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Complexes: which inventory objects a group holds, from supplied hierarchy and component facts, and how a
// group and its objects show and are picked at each distance, from supplied positions and distances to bounds.
static class ComplexTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Complex: " + message); }
    // One inventory object: the scene object it is on and the scene objects above that one, nearest first.
    static GroupObject Object(string at, string above, bool controller = false, bool marker = false, bool span = false, bool baked = false) =>
        new GroupObject(at, above.Length == 0 ? new string[0] : above.Split('/').Reverse().ToArray(), controller, marker, span, baked);
    static string Members(IReadOnlyList<Complex> groups) =>
        String.Join(" | ", groups.Select(g => g.Kind + ":" + String.Join(",", g.Members)));

    public static void Run() {
        Membership();
        SceneObjectMembership();
        CrossingWithoutControllerMembership();
        CategoryGroupMembership();
        RoadBridgeGroup();
        SkippedGroupLine();
        SkippedGroupOnce();
        SkippedFamilyOnce();
        Distances();
        Marking();
        Picking();
        Console.WriteLine("PASS: complexes");
    }

    // A crossing without a crossing controller is a group too: the crossbucks and pads its crossing object holds,
    // without its whistle signs. Crossings that share one crossing object are told apart by the marker ID of their
    // pad. Below a crossing controller, a pad belongs to the controller's setup.
    static void CrossingWithoutControllerMembership() {
        const string Road = "World/Roads Whittier", Crossing = Road + "/Crossing Unprotected", Shared = "World/Roads Bryson/Old River Rd/Crossing Unprotected";
        GroupObject Up(string at, string above, object crossing, bool baked = false, bool marker = false) =>
            new GroupObject(at, above.Split('/').Reverse().ToArray(), trackMarker: marker, topSceneObject: baked, crossing: crossing);
        var groups = Complexes.Of(new[] {
            Up("Crossbuck 1", Crossing, Crossing, baked: true),                       // 0
            Up("Crossbuck 2", Crossing, Crossing, baked: true),                       // 1
            Up("Pad", Crossing, Crossing),                                            // 2
            Up("Whistle Sign E", Crossing, null, marker: true),                       // 3: a whistle sign shows on its own
            Up("A1", Shared, Tuple.Create(Shared, "Mgdu"), baked: true),              // 4
            Up("A2", Shared, Tuple.Create(Shared, "Mgdu"), baked: true),              // 5
            Up("B1", Shared, Tuple.Create(Shared, "Mbkd"), baked: true),              // 6
            Up("B pad", Shared, Tuple.Create(Shared, "Mbkd")),                        // 7
            Up("Lone crossbuck", "World/Roads Sylva/Crossing Unprotected Wye", "World/Roads Sylva/Crossing Unprotected Wye", baked: true), // 8: one object is no group
        });
        Check(Members(groups) == "GradeCrossingSetup:0,1,2 | GradeCrossingSetup:4,5 | GradeCrossingSetup:6,7",
            "a crossing without a controller is its crossbucks and pads; shared crossing objects split by pad marker ID (got " + Members(groups) + ")");
        var held = Complexes.Of(new[] {
            new GroupObject("Controller", new[] { "Roads Sylva", "World" }, crossingController: true),
            new GroupObject("Controller pad", new object[] { "Controller", "Roads Sylva", "World" }, crossing: "Controller"),
        });
        Check(Members(held) == "GradeCrossingSetup:0,1" && Equals(held[0].Anchor, "Controller"), "below a crossing controller a pad belongs to the controller's setup (got " + Members(held) + ")");
    }

    // A scene object with child objects whose parts share one top scene object, such as Bryson Coal Dealer: the listed top scene object and the
    // inventory objects below it, also those in a child object that only carries a simple culler, are one group. Only the hierarchy decides:
    // objects that stand close together, but under no common listed root, form no group, and a listed root without
    // parts shows as that one object.
    static void SceneObjectMembership() {
        const string Town = "World/Large Scenery/Bryson";
        var coal = new[] {
            Object("Bryson Coal Dealer", Town, baked: true),                              // 0
            Object("Coal Bin", Town + "/Bryson Coal Dealer"),                             // 1
            Object("Shed", Town + "/Bryson Coal Dealer"),                                 // 2
            Object("Shed Door", Town + "/Bryson Coal Dealer/Shed"),                       // 3
            Object("Platform", Town + "/Bryson House Track/Culled"),                      // 4: in a child object that only carries a simple culler of another group
            Object("Bryson House Track", Town, baked: true),                              // 5
            Object("Lamp", Town),                                                         // 6: beside the coal dealer, under no root
            Object("Bryson Depot", Town, baked: true),                                    // 7: a listed root without parts
            Object("Sign", Town + "/Bryson Coal Dealer", marker: true),                   // 8: a track marker in a baked group
        };
        var groups = Complexes.Of(coal);
        Check(Members(groups) == "SceneObject:0,1,2,3,8 | SceneObject:4,5", "a scene object with child objects holds its listed root and the inventory objects below it, at any depth and in its child objects that only carry a simple culler, track markers included: only a crossing leaves its whistle signs out (got " + Members(groups) + ")");
        Check(ReferenceEquals(groups[0].Anchor, coal[0].Node) && groups[0].Listed.SequenceEqual(groups[0].Members), "the group is anchored by its root and lists and frames every object");
        Check(!groups.Any(g => g.Members.Contains(6) || g.Members.Contains(7)), "proximity alone merges no objects into a group, and a root without parts is no group");
        // A crossing inside a listed top scene object is held by the outermost group.
        var nested = new[] { Object("Yard", Town, baked: true), Object("Setup", Town + "/Yard", controller: true), Object("Bell", Town + "/Yard/Setup") };
        Check(Members(Complexes.Of(nested)) == "SceneObject:0,1,2", "an object belongs to the outermost group that holds it (got " + Members(Complexes.Of(nested)) + ")");
    }
    static string[] Up(string above) => above.Length == 0 ? new string[0] : above.Split('/').Reverse().ToArray();

    // The groups of the scene objects categories, from supplied hierarchy and component facts.
    static void CategoryGroupMembership() {
        const string Town = "World/Large Scenery/Bryson";
        // A station building group holds its inventory objects: the building, the sign markers and the parts below it.
        var station = new[] {
            Object("Bryson Depot", Town, baked: true),                                    // 0
            Object("Sign E", Town + "/Bryson Depot", marker: true),                       // 1
            new GroupObject("Clock", Up(Town + "/Bryson Depot"), childObject: true),         // 2
            Object("Milepost", Town),                                                     // 3: beside the depot
        };
        var depot = Complexes.Of(station);
        Check(Members(depot) == "SceneObject:0,1,2", "a station building group holds its inventory objects (got " + Members(depot) + ")");
        Check(depot[0].Root == 0, "the root of a group with parts stands for the group");

        // A rail bridge construction holds its construction controller and the inventory objects whose visibility
        // the controller sets, wherever they stand: on a target, or below one.
        var bridge = new[] {
            new GroupObject("Ela Bridge Scenery", Up("World/Large Scenery/Ela"), constructionController: true, targets: new[] { "Steel", "Abutments" }), // 0
            Object("Steel", "World/Large Scenery/Ela/Ela Bridge"),                         // 1: a target itself
            Object("Abutment W", "World/Large Scenery/Ela/Ela Bridge/Abutments"),          // 2: below a target
            Object("Ela Bridge", "World/Large Scenery/Ela"),                               // 3: the bridge beside the targets, which names none of them
            Object("Board", "World/Large Scenery/Ela"),                                    // 4
        };
        var construction = Complexes.Of(bridge);
        Check(Members(construction) == "RailBridgeConstruction:0,1,2", "a rail bridge construction holds its controller and the objects whose visibility it sets, and nothing else (got " + Members(construction) + ")");
        // Ela as the game holds it in Railroader 0.6.3: the controller switches Ela Bridge/Steel and Ela Bridge/Abutments,
        // which are no inventory objects themselves. The inventory object that holds them, the Ela Bridge, is the object
        // whose visibility the controller sets; a board below the bridge that no target holds is not.
        var ela = new[] {
            new GroupObject("Ela Bridge Scenery", Up("World/Large Scenery/Ela"), constructionController: true, targets: new[] { "Steel", "Abutments" },
                targetsAbove: new[] { Up("World/Large Scenery/Ela/Ela Bridge"), Up("World/Large Scenery/Ela/Ela Bridge") }),   // 0
            Object("Ela Bridge", "World/Large Scenery/Ela"),                               // 1
            Object("Bridge Board", "World/Large Scenery/Ela/Ela Bridge"),                  // 2: below the bridge, outside every target
            Object("Ela Station", "World/Large Scenery/Ela"),                              // 3
        };
        Check(Members(Complexes.Of(ela)) == "RailBridgeConstruction:0,1", "a target that is no inventory object brings in the nearest inventory object above it, and no other object below that one (got " + Members(Complexes.Of(ela)) + ")");
        Check(ReferenceEquals(construction[0].Anchor, bridge[0].Node) && construction[0].Root < 0, "the construction is anchored by its controller");
        // Targets inside listed top scene objects: the construction is the outermost group and holds those roots with their parts.
        var targetsAsRoots = new[] {
            new GroupObject("Controller", Up("World/Large Scenery/Somewhere"), constructionController: true, targets: new[] { "Steel", "Abutments" }), // 0
            Object("Steel", "World/Large Scenery/Somewhere", baked: true),                 // 1
            new GroupObject("Girder", Up("World/Large Scenery/Somewhere/Steel"), childObject: true), // 2
            Object("Abutments", "World/Large Scenery/Somewhere", baked: true),             // 3
        };
        Check(Members(Complexes.Of(targetsAsRoots)) == "RailBridgeConstruction:0,1,2,3", "a construction whose targets are top scene objects holds those roots and their parts (got " + Members(Complexes.Of(targetsAsRoots)) + ")");
        // The Almond Bridge as the game holds it in Railroader 0.6.3: the scene object carries the controller and the culler and
        // is the top scene object, its abutments and steel are parts, and the controller is no inventory object of its own. The
        // construction is one scene object with child objects with that scene object as its root.
        var almond = new[] {
            Object("Almond Bridge", "World/Large Scenery", baked: true),                       // 0
            new GroupObject("Steel", Up("World/Large Scenery/Almond Bridge"), childObject: true),     // 1
            new GroupObject("Truss", Up("World/Large Scenery/Almond Bridge/Steel"), childObject: true), // 2
            new GroupObject("Abutments", Up("World/Large Scenery/Almond Bridge"), childObject: true), // 3
            Object("Almond Depot", "World/Large Scenery/Almond", baked: true),                  // 4: another town
        };
        var almondGroups = Complexes.Of(almond);
        Check(Members(almondGroups) == "SceneObject:0,1,2,3" && almondGroups[0].Root == 0, "the Almond Bridge is one group with that scene object as its root and its abutments and steel as parts (got " + Members(almondGroups) + ")");

        // An industry storage display holds the storage piles or log pond that one industry's storage fills. An
        // industry building is its own group and no member of a storage display or another group.
        var industry = new[] {
            new GroupObject("Pile 1", Up("World/Large Scenery/Andrews/Andrews Snowbird Paper"), storage: "Paper storage"),   // 0
            new GroupObject("Pile 2", Up("World/Large Scenery/Andrews/Andrews Snowbird Paper"), storage: "Paper storage"),   // 1
            new GroupObject("Log Pond", Up("World/Large Scenery/Whittier/Whittier Log Pond"), storage: "Mill storage"),       // 2
            new GroupObject("Log Pile", Up("World/Large Scenery/Whittier/Whittier Saw Mill Culled"), storage: "Mill storage"), // 3: fills with the pond, below the mill
            Object("Whittier Saw Mill Culled", "World/Large Scenery/Whittier", baked: true),                                // 4: the industry building
            new GroupObject("Saw Shed", Up("World/Large Scenery/Whittier/Whittier Saw Mill Culled"), childObject: true),        // 5
            Object("Coal & Oil", "World/Large Scenery/Andrews", baked: true),                                               // 6: an industry building without parts
            new GroupObject("Lone Pile", Up("World/Large Scenery/Sylva/Sylva Paperboard"), storage: "Board storage"),        // 7: a display of one pile
        };
        var displays = Complexes.Of(industry);
        Check(Members(displays) == "IndustryStorageDisplay:0,1 | IndustryStorageDisplay:2,3 | SceneObject:4,5",
            "an industry storage display holds the piles or log pond one industry's storage fills; an industry building is its own group (got " + Members(displays) + ")");
        Check(ReferenceEquals(displays[0].Anchor, "Paper storage") && displays[2].Root == 4 && !displays.Any(g => g.Kind != ComplexKind.SceneObject && (g.Members.Contains(4) || g.Members.Contains(6))),
            "a storage display is anchored by the industry's storage, and no industry building is a member of one");
        Check(!displays.Any(g => g.Members.Contains(6) || g.Members.Contains(7)), "an industry building without parts and a display of one pile show as that one object");

        // A storage display that is a scene object with child objects lists its piles as parts: each pile belongs to that group,
        // its outermost group, whichever storage of the industry fills it, so the display is one group with its
        // root standing for it. Connelly has two storages, logs and pulpwood, on one root.
        var connelly = new[] {
            Object("Connelly", "World/Large Scenery/Whittier", baked: true),                                                            // 0
            new GroupObject("Log Pile L1a", Up("World/Large Scenery/Whittier/Connelly"), childObject: true, storage: "Log storage"),     // 1
            new GroupObject("Log Pile L1b", Up("World/Large Scenery/Whittier/Connelly"), childObject: true, storage: "Log storage"),     // 2
            new GroupObject("Pulpwood Pile P1a", Up("World/Large Scenery/Whittier/Connelly"), childObject: true, storage: "Pulpwood storage"), // 3
            Object("Andrews Snowbird Paper", "World/Large Scenery/Andrews", baked: true),                                              // 4
            new GroupObject("Pulpwood Piles", Up("World/Large Scenery/Andrews/Andrews Snowbird Paper"), childObject: true),               // 5
            new GroupObject("Pile", Up("World/Large Scenery/Andrews/Andrews Snowbird Paper/Pulpwood Piles"), childObject: true, storage: "Paper storage"), // 6
            new GroupObject("Pile (1)", Up("World/Large Scenery/Andrews/Andrews Snowbird Paper/Pulpwood Piles"), childObject: true, storage: "Paper storage"), // 7
        };
        var whole = Complexes.Of(connelly);
        Check(Members(whole) == "SceneObject:0,1,2,3 | SceneObject:4,5,6,7",
            "a storage display whose piles are parts of its scene object with child objects is that one group (got " + Members(whole) + ")");
        Check(whole[0].Root == 0 && whole[1].Root == 4 && whole[1].Listed.Contains(6) && whole[1].Listed.Contains(7), "the display's root stands for it, and its choice list holds the piles");

        // Beyond 125 metres only the group shows; at 125 metres or less only its parts: the root, which stands for
        // the group, shows no marker of its own unless it is inspected or the candidate of a choice.
        foreach (var shows in new[] { GroupShows.Objects, GroupShows.MarkerAndFrame, GroupShows.MarkerOnly }) {
            Check(!Complexes.ShowsOwn(shows, false, false, root: true), "the root of a group with parts shows no marker of its own");
            Check(Complexes.ShowsOwn(shows, true, false, root: true) && Complexes.ShowsOwn(shows, false, true, root: true), "an inspected or chosen top scene object shows its own marker and frame");
        }
        Check(Complexes.ShowsOwn(GroupShows.Objects, false, false, root: false), "a part of a near group shows on its own");
        // A choice that offers the root and none of its parts still shows the root near the group: nothing else could be picked.
        Check(Complexes.RootStands(0, new[] { 0, 2 }) && !Complexes.RootStands(0, new[] { 0 }) && !Complexes.RootStands(-1, new[] { 1, 2 }) && !Complexes.RootStands(0, new[] { 1, 2 }),
            "the root stands for the group only while the choice offers the root and another object of the group");
        // A group without parts of its own, such as a station whose objects are sign markers, keeps its root in view.
        Check(Complexes.Of(new[] { Object("Bryson Signs", Town, baked: true), Object("Yard Limit", Town + "/Bryson Signs", marker: true) })[0].Root < 0, "a root whose group has no parts shows among its objects");

        // The group's square marker carries its own symbol instead of a part's "+".
        var plus = ObjectMarkingGeometry.MarkerSymbol(false); var square = ObjectMarkingGeometry.MarkerSymbol(true);
        bool Covers(IReadOnlyList<ScreenRect> bars, double x, double y) => bars.Any(b => x >= b.X && x < b.Right && y >= b.Y && y < b.Bottom);
        Check(plus.Count == 2 && Covers(plus, 0, 0) && Covers(plus, -4, 0) && Covers(plus, 0, 4) && !Covers(plus, -4, -4), "a part's marker carries a plus through its centre");
        Check(!Covers(square, 0, 0) && Covers(square, -4, -4) && Covers(square, 4, 4) && Covers(square, -4, 4) && Covers(square, 0, -5), "a group's marker carries a hollow square, which leaves the centre open");
        Check(plus.Concat(square).All(b => b.X >= -ObjectMarkingGeometry.MarkerSize / 2 && b.Right <= ObjectMarkingGeometry.MarkerSize / 2 && b.Y >= -ObjectMarkingGeometry.MarkerSize / 2 && b.Bottom <= ObjectMarkingGeometry.MarkerSize / 2), "both symbols stay inside the square marker");
    }
    // A road bridge is a group: a road bridge with several inventory objects, here its root and two sign markers on
    // it, shows one square marker and one frame beyond 125 metres and its objects at that distance or less. The base
    // map of Railroader 0.6.3 has no such road bridge: each of its road bridges holds one inventory object.
    static void RoadBridgeGroup() {
        const string Road = "World/Roads Sylva", Bridge = Road + "/Sylva Road Bridge";
        // The rule lists the bridge as a top scene object under Bridge / Road bridges, whatever stands on it.
        var world = new Node("World", new Node("Roads Sylva", new Node("Sylva Road Bridge", new Node("Deck").Solid(),
            new Node("Sign N").With("Track.TrackMarker").Solid().Family(), new Node("Sign S").With("Track.TrackMarker").Solid().Family())));
        var verdict = SceneObjectRules.Classify(world).Single(v => v.Path == Bridge);
        Check(verdict.Disposition == GroupDisposition.Group && verdict.Category == "Bridge" && verdict.Subcategory == "Road bridges", "a road bridge is a top scene object under Bridge / Road bridges");
        var objects = new[] {
            Object("Sylva Road Bridge", Road, baked: true),            // 0
            Object("Sign N", Bridge, marker: true),                    // 1
            Object("Sign S", Bridge, marker: true),                    // 2
            Object("Milepost", Road),                                  // 3: beside the bridge
        };
        var groups = Complexes.Of(objects);
        Check(Members(groups) == "SceneObject:0,1,2" && groups[0].Listed.SequenceEqual(new[] { 0, 1, 2 }) && groups[0].Root < 0, "a road bridge with several inventory objects is one group that lists them all (got " + Members(groups) + ")");
        // The bridge deck from x -30 to 30 and a sign at each end, 1000 m north of the origin.
        var anchors = new[] { new WorldPoint(0, 0, 1000), new WorldPoint(-30, 0, 1000), new WorldPoint(30, 0, 1000) };
        GroupMarking At(double z, out ObjectMarkingFrame[] own) {
            var eye = new WorldPoint(0, 0, z);
            own = new[] {
                ObjectMarkingGeometry.Frame(anchors[0], eye, new[] { Box("deck", 0, 1, 1000, 30, 1, 4) }, Camera(eye), View, null),
                ObjectMarkingGeometry.Frame(anchors[1], eye, new[] { Box("north", -30, 3, 1000, 1, 3, 1) }, Camera(eye), View, null),
                ObjectMarkingGeometry.Frame(anchors[2], eye, new[] { Box("south", 30, 3, 1000, 1, 3, 1) }, Camera(eye), View, null),
            };
            return ObjectMarkingGeometry.GroupFrame(eye, anchors, own, null, null, Camera(eye), View);
        }
        // 196 m from the bounds: one square marker and one frame around the deck and both signs.
        var far = At(800, out var frames);
        Check(far.Shows == GroupShows.MarkerAndFrame && far.Frame.HasValue && Math.Abs(far.Distance - 196) < 1e-9, "beyond 125 metres the road bridge shows one square marker and one frame (got " + far.Shows + " at " + far.Distance + ")");
        Check(frames.All(f => f.Frame.HasValue && far.Frame.Value.X <= f.Frame.Value.X + 1e-6 && far.Frame.Value.Right >= f.Frame.Value.Right - 1e-6 && far.Frame.Value.Y <= f.Frame.Value.Y + 1e-6 && far.Frame.Value.Bottom >= f.Frame.Value.Bottom - 1e-6),
            "the one frame surrounds the frames of all the bridge's objects");
        Check(Enumerable.Range(0, 3).All(i => !Complexes.ShowsOwn(far.Shows, false, false)), "beyond 125 metres no object of the bridge shows a marker of its own");
        // The one marker lists the bridge's objects and not the milepost beside it.
        var picked = WorldPick.Resolve(300, 300, new[] { T(260, 300), T(250, 300), T(350, 300), T(600, 300) }, new[] { new PickGroup(T(300, 300, true), groups[0].Members.ToArray(), new int[0]) });
        Check(Picked(picked) == "0,1,2 g0", "a click on the bridge's square marker lists its objects (got " + Picked(picked) + ")");
        // 96 m and exactly 125 m from the bounds: each object shows on its own.
        foreach (var z in new double[] { 900, 871 }) {
            var near = At(z, out _);
            Check(near.Shows == GroupShows.Objects && Enumerable.Range(0, 3).All(i => Complexes.ShowsOwn(near.Shows, false, false, i == groups[0].Root)), "at 125 metres or less the objects of the road bridge show on their own (got " + near.Shows + " at " + near.Distance + ")");
        }
        // Beyond 500 metres only the square marker remains.
        Check(At(400, out _).Shows == GroupShows.MarkerOnly, "beyond 500 metres the road bridge shows only its square marker");
    }
    static void SkippedGroupLine() =>
        Check(Complexes.SkippedLine("BrysonCoalDealer/World/Large Scenery/Bryson/Bryson Coal Dealer", "none of its objects has a usable position") ==
            "The patcher couldn't finish preparing a group of scene objects at BrysonCoalDealer/World/Large Scenery/Bryson/Bryson Coal Dealer: none of its objects has a usable position", "a skipped group gives one line The patcher couldn't finish preparing a group of scene objects at <top scene object>: <reason>");
    // The family decides its groups again after every scene change; a group whose parts cannot be read keeps its root
    // entry without parts and logs exactly one line, once per root and reason, in the agreed format.
    static void SkippedFamilyOnce() {
        var lines = new List<string>();
        var skips = new GroupSkips(lines.Add);
        var computed = 0; var read = 0;
        for (var pass = 0; pass < 3; pass++) {
            Check(!skips.ReadParts(() => "s/World/A", () => throw new InvalidOperationException("broken")), "a group whose parts cannot be read is skipped");
            Check(skips.ReadParts(() => { computed++; return "s/World/B"; }, () => read++), "a group whose parts are read is not skipped");
        }
        Check(computed == 0 && read == 3, "the root of a group whose parts are read is not computed (computed " + computed + " times), and its parts are read each pass");
        Check(lines.SequenceEqual(new[] { "The patcher couldn't finish preparing a group of scene objects at s/World/A: its parts could not be read: broken" }), "exactly one line for the skipped family: " + String.Join(" | ", lines));
    }
    // World marking decides its groups again after every scene change; a skipped group logs its line once per root
    // and reason.
    static void SkippedGroupOnce() {
        var lines = new List<string>();
        var skips = new GroupSkips(lines.Add);
        var computed = 0;
        for (var pass = 0; pass < 3; pass++) {
            Check(skips.Read<object>(() => "s/World/A", () => throw new InvalidOperationException("broken")) == null, "a group whose objects cannot be read is skipped");
            skips.Skip("s/World/A", "none of its objects has a usable position");
            Check(skips.Read(() => { computed++; return "s/World/B"; }, () => "read") == "read", "a readable group is read");
        }
        Check(computed == 0, "the root of a group that is read is not computed (computed " + computed + " times)");
        // A root that cannot be computed skips only its own group.
        Check(skips.Read<object>(() => throw new InvalidOperationException("no root"), () => throw new InvalidOperationException("broken")) == null && skips.Read(() => "s/World/B", () => "read") == "read",
            "a group whose root cannot be computed is skipped alone");
        Check(lines.SequenceEqual(new[] { "The patcher couldn't finish preparing a group of scene objects at s/World/A: its objects could not be read: broken", "The patcher couldn't finish preparing a group of scene objects at s/World/A: none of its objects has a usable position", "The patcher couldn't finish preparing a group of scene objects at an unknown path: its objects could not be read: broken" }),
            "one line per top scene object and reason: " + String.Join(" | ", lines));
    }

    static void Membership() {
        // The Andrews crossing as the game holds it: the controller on "Setup", below it two masts with a light and
        // a gate each, a bell, a pad marker and two whistle signs; beside it a board that belongs to nothing.
        var andrews = new[] {
            Object("Setup", "World/Roads", controller: true),          // 0
            Object("Light 1", "World/Roads/Setup/Signal 1"),            // 1
            Object("Gate 1", "World/Roads/Setup/Signal 1"),             // 2
            Object("Light 2", "World/Roads/Setup/Signal 2"),            // 3
            Object("Bell", "World/Roads/Setup"),                        // 4
            Object("Pad", "World/Roads/Setup"),                         // 5
            Object("Whistle E", "World/Roads/Setup", marker: true),     // 6
            Object("Whistle W", "World/Roads/Setup", marker: true),     // 7
            Object("Board", "World/Roads"),                             // 8
        };
        var groups = Complexes.Of(andrews);
        Check(Members(groups) == "GradeCrossingSetup:0,1,2,3,4,5", "a grade-crossing setup holds its controller and the inventory objects below the controller's root, without its whistle signs and without what stands beside it (got " + Members(groups) + ")");
        Check(ReferenceEquals(groups[0].Anchor, andrews[0].Node), "the group is anchored by its controller's scene object");
        Check(Complexes.Of(andrews.Reverse().ToArray())[0].Members.SequenceEqual(new[] { 3, 4, 5, 6, 7, 8 }), "membership does not depend on the order of the inventory");

        // The detection spans below the controller's root are members of the setup, but its controller manages them:
        // the setup's choice list and frame leave them out, which the listed members say.
        var spans = new[] {
            Object("Setup", "World/Roads", controller: true),           // 0
            Object("Approach A", "World/Roads/Setup/Spans", span: true), // 1
            Object("Light 1", "World/Roads/Setup/Signal 1"),             // 2
            Object("Island", "World/Roads/Setup/Spans", span: true),     // 3
            Object("Bell", "World/Roads/Setup"),                         // 4
            Object("Whistle E", "World/Roads/Setup", marker: true),      // 5
            Object("Approach B", "World/Roads/Setup", span: true),       // 6
            Object("Loose span", "World/Roads", span: true),             // 7
        };
        var spanned = Complexes.Of(spans);
        Check(Members(spanned) == "GradeCrossingSetup:0,1,2,3,4,6", "the detection spans below the controller's root are members of the setup (got " + Members(spanned) + ")");
        Check(spanned[0].Listed.SequenceEqual(new[] { 0, 2, 4 }), "the setup lists and frames its members other than its detection spans (got " + String.Join(",", spanned[0].Listed) + ")");
        Check(groups[0].Listed.SequenceEqual(groups[0].Members), "a setup without detection spans lists and frames every member");
        // A setup whose only other members are detection spans is still a group of its objects; its list holds the controller.
        var bare = Complexes.Of(new[] { Object("Setup", "World", controller: true), Object("Island", "World/Setup", span: true) });
        Check(Members(bare) == "GradeCrossingSetup:0,1" && bare[0].Listed.SequenceEqual(new[] { 0 }), "a setup of a controller and its spans lists the controller");

        // A whistle sign is its own object at any depth below the controller's root, and a track marker outside a
        // setup is no whistle sign of it.
        var deep = new[] { Object("Setup", "World", controller: true), Object("Light", "World/Setup/Signal"), Object("Whistle", "World/Setup/Signs", marker: true), Object("Speed sign", "World", marker: true) };
        Check(Members(Complexes.Of(deep)) == "GradeCrossingSetup:0,1", "a whistle sign below the controller's root is no member of the setup");

        // An object held by two groups belongs to the outermost one. A controller below another controller's root,
        // with a light of its own: both are objects of the outer setup, and the inner one forms no group.
        var nested = new[] { Object("Inner", "World/Outer/Part", controller: true), Object("Inner light", "World/Outer/Part/Inner"), Object("Outer", "World", controller: true), Object("Outer light", "World/Outer"),
            Object("Inner whistle", "World/Outer/Part/Inner", marker: true) };
        Check(Members(Complexes.Of(nested)) == "GradeCrossingSetup:0,1,2,3", "an object held by two groups belongs to the outermost (got " + Members(Complexes.Of(nested)) + ")");

        // A group with one inventory object shows as that object: it is no group. A controller whose only other
        // inventory objects are its whistle signs stands alone.
        var alone = new[] { Object("Setup", "World", controller: true), Object("Whistle E", "World/Setup", marker: true), Object("Board", "World") };
        Check(Complexes.Of(alone).Count == 0, "a group with one inventory object is that object and no group");
        Check(Complexes.Of(new GroupObject[0]).Count == 0 && Complexes.Of(new[] { Object("Board", "World") }).Count == 0, "without a crossing controller there is no grade-crossing setup");
        // Two inventory entries on the controller's own scene object are two objects of the setup.
        var shared = new[] { Object("Setup", "World", controller: true), Object("Setup", "World") };
        Check(Members(Complexes.Of(shared)) == "GradeCrossingSetup:0,1", "an inventory object on the controller's own scene object belongs to the setup");
    }

    static void Distances() {
        // Distances run from the camera to the bounds of the group, in metres.
        Check(Complexes.GroupDistance == 125 && ObjectMarkingGeometry.FrameDistance == 500, "a group shows as one beyond 125 metres and loses its frame beyond 500");
        Check(Complexes.Shows(0) == GroupShows.Objects && Complexes.Shows(124.9) == GroupShows.Objects && Complexes.Shows(125) == GroupShows.Objects, "at 125 metres or less each object of a group shows on its own");
        Check(Complexes.Shows(125.1) == GroupShows.MarkerAndFrame && Complexes.Shows(499.9) == GroupShows.MarkerAndFrame && Complexes.Shows(500) == GroupShows.MarkerAndFrame, "beyond 125 metres and within 500 a group shows one square marker and one object frame");
        Check(Complexes.Shows(500.1) == GroupShows.MarkerOnly && Complexes.Shows(20000) == GroupShows.MarkerOnly, "beyond 500 metres a group shows only its square marker");
        Check(Complexes.Shows(Double.NaN) == GroupShows.Objects, "a group whose distance is unknown shows its objects");
        // Of a group that shows as one, only a candidate and the inspected object show a marker and a frame of their own.
        foreach (var far in new[] { GroupShows.MarkerAndFrame, GroupShows.MarkerOnly }) {
            Check(!Complexes.ShowsOwn(far, false, false), "an object of a distant group shows no marker or frame of its own");
            Check(Complexes.ShowsOwn(far, true, false) && Complexes.ShowsOwn(far, false, true), "a candidate or the inspected object of a distant group shows its own marker and frame");
        }
        Check(Complexes.ShowsOwn(GroupShows.Objects, false, false), "every object of a near group shows on its own");
        // A candidate of a group within the frame distance has its own frame, wherever its own bounds lie.
        Check(Double.IsPositiveInfinity(Complexes.OwnFrameDistance(GroupShows.MarkerAndFrame)) && Complexes.OwnFrameDistance(GroupShows.MarkerOnly) == 500 && Complexes.OwnFrameDistance(GroupShows.Objects) == 500,
            "a candidate's own frame follows the group's frame distance while the group shows its frame");
    }

    static readonly ScreenRect View = new ScreenRect(0, 0, 2560, 1440);
    const double Near = .3, Far = 20000;
    // A camera at a place looking along +Z with a 90 degree vertical field, in the clip convention of Unity's
    // projection matrix, as the marking-geometry tests use.
    static Func<WorldPoint, ClipPoint> Camera(WorldPoint eye) => p => {
        double depth = p.Z - eye.Z;
        return new ClipPoint((p.X - eye.X) * View.Height / View.Width, p.Y - eye.Y, (Far + Near) / (Far - Near) * depth - 2 * Far * Near / (Far - Near), depth);
    };
    static ObjectMarkingBox Box(string name, double x, double y, double z, double ex, double ey, double ez) =>
        new ObjectMarkingBox(name, new WorldPoint(x, y, z), new WorldPoint(ex, ey, ez));

    // Where a world point shows on screen from a camera of Camera(eye).
    static ScreenPoint Screen(WorldPoint p, WorldPoint eye) {
        var clip = Camera(eye)(p);
        return new ScreenPoint(View.X + (clip.X / clip.W + 1) * View.Width / 2, View.Y + (1 - clip.Y / clip.W) * View.Height / 2);
    }
    static void Marking() {
        // A crossing 20 m wide: a mast 2 m wide and 6 m high west of the track and a post 2 m wide and 2 m high
        // east of it, 1000 m north of the origin, and between them the controller, an object without a part.
        WorldPoint west = new WorldPoint(-10, 0, 1000), east = new WorldPoint(10, 0, 1000), middle = new WorldPoint(0, 0, 1000);
        var anchors = new[] { west, east, middle };
        GroupMarking At(double z, out ObjectMarkingFrame[] own, WorldBasis? orientation = null, Func<double, double, double?> ground = null) {
            var eye = new WorldPoint(0, 0, z);
            own = new[] {
                ObjectMarkingGeometry.Frame(west, eye, new[] { Box("west", -10, 3, 1000, 1, 3, 1) }, Camera(eye), View, null),
                ObjectMarkingGeometry.Frame(east, eye, new[] { Box("east", 10, 1, 1000, 1, 1, 1) }, Camera(eye), View, null),
                null
            };
            return ObjectMarkingGeometry.GroupFrame(eye, anchors, own, orientation, ground, Camera(eye), View);
        }
        // The bounds of the group reach from x -11 to 11, y 0 to 6 and z 999 to 1001: 199 m in front of a camera at z 800.
        var eye800 = new WorldPoint(0, 0, 800);
        var near = At(800, out var frames);
        Check(Math.Abs(near.Distance - 199) < 1e-9 && near.Shows == GroupShows.MarkerAndFrame, "the distance runs from the camera to the bounds of the group (got " + near.Distance + ")");
        Check(Math.Abs(near.Center.X) < 1e-9 && Math.Abs(near.Center.Y - 3) < 1e-9 && Math.Abs(near.Center.Z - 1000) < 1e-9, "the group's square marker stands at the middle of its bounds");
        Check(near.Frame.HasValue && near.Outline != null && near.Outline.Count >= 3, "a group within the frame distance has one object frame");
        ScreenRect a = frames[0].Frame.Value, b = frames[1].Frame.Value, g = near.Frame.Value;
        Check(Math.Abs(g.X - a.X) < 1e-6 && Math.Abs(g.Right - b.Right) < 1e-6 && Math.Abs(g.Y - a.Y) < 1e-6 && Math.Abs(g.Bottom - a.Bottom) < 1e-6,
            "the group's frame surrounds the frames of all its objects");
        foreach (var frame in frames.Where(f => f != null))
            foreach (var point in frame.Outline) Check(ObjectMarkingGeometry.Contains(near.Outline, point.X, point.Y), "the group's frame holds every point of its objects' frames");
        // The detection spans of a grade-crossing setup are members its frame and its bounds leave out: an approach
        // span that stands 300 m along the track changes neither the group's distance, its marker nor its frame.
        var span = new WorldPoint(200, 0, 1300);
        var spanned = ObjectMarkingGeometry.GroupFrame(eye800, anchors.Concat(new[] { span }).ToArray(), frames.Concat(new ObjectMarkingFrame[] { null }).ToArray(), null, null, Camera(eye800), View,
            new[] { true, true, true, false });
        Check(Math.Abs(spanned.Distance - near.Distance) < 1e-9 && Math.Abs(spanned.Center.X - near.Center.X) < 1e-9 && Math.Abs(spanned.Center.Z - near.Center.Z) < 1e-9,
            "a detection span does not count for the group's bounds (got " + spanned.Distance + ")");
        Check(spanned.Frame.HasValue && Math.Abs(spanned.Frame.Value.X - g.X) < 1e-6 && Math.Abs(spanned.Frame.Value.Right - g.Right) < 1e-6 && Math.Abs(spanned.Frame.Value.Y - g.Y) < 1e-6 && Math.Abs(spanned.Frame.Value.Bottom - g.Bottom) < 1e-6,
            "a detection span does not count for the group's frame");
        var counted = ObjectMarkingGeometry.GroupFrame(eye800, anchors.Concat(new[] { span }).ToArray(), frames.Concat(new ObjectMarkingFrame[] { null }).ToArray(), null, null, Camera(eye800), View);
        Check(counted.Spread && !counted.Frame.HasValue, "without the listed members every object counts: the span 300 m off makes the objects stand apart");
        // The frame is one box around the members, not the outline of the members: the place above the low post,
        // at the height of the mast's top, lies inside it, although no member reaches there.
        var above = Screen(new WorldPoint(10, 5.5, 999), eye800);
        Check(ObjectMarkingGeometry.Contains(near.Outline, above.X, above.Y) && !ObjectMarkingGeometry.Contains(frames[1].Outline, above.X, above.Y),
            "the group's frame is one box around all its objects: it holds the corner that no object reaches");
        // Seen level and head-on, a box shows as a rectangle: the outline fills the rectangle around it.
        foreach (var corner in new[] { new ScreenPoint(g.X + 1, g.Y + 1), new ScreenPoint(g.Right - 1, g.Y + 1), new ScreenPoint(g.Right - 1, g.Bottom - 1), new ScreenPoint(g.X + 1, g.Bottom - 1) })
            Check(ObjectMarkingGeometry.Contains(near.Outline, corner.X, corner.Y), "the outline of the group's box, seen head-on, is the rectangle around it");
        Check(!ObjectMarkingGeometry.Contains(near.Outline, g.X - 5, (g.Y + g.Bottom) / 2), "the group's frame is the box around its objects, not more");

        // The box is cut at the surface the group stands in, like the box of an object: with the ground 1 m up,
        // the frame ends higher on screen and keeps its top and its sides.
        var raised = At(800, out _, null, (x, z) => 1).Frame.Value;
        var level = Screen(new WorldPoint(0, 1, 1001), eye800);
        Check(Math.Abs(raised.Y - g.Y) < 1e-6 && Math.Abs(raised.X - g.X) < 1e-6 && Math.Abs(raised.Right - g.Right) < 1e-6 && raised.Bottom < g.Bottom - 2 && Math.Abs(raised.Bottom - (level.Y + ObjectMarkingGeometry.Margin)) < 1e-3,
            "the group's box is cut off at the ground");
        // A group that lies wholly under the surface found from above, as in a tunnel, is framed whole.
        var under = At(800, out _, null, (x, z) => 50).Frame.Value;
        Check(Math.Abs(under.Y - g.Y) < 1e-6 && Math.Abs(under.Bottom - g.Bottom) < 1e-6, "a group wholly under the surface above it is framed whole");

        // The box stands in the orientation of the scene object that anchors the group. Two masts on a diagonal:
        // in the world's directions the box around them is 22 by 22 m on the ground, along the diagonal it is long
        // and narrow.
        var diagonal = new[] { new WorldPoint(-10, 0, -10), new WorldPoint(10, 0, 10), new WorldPoint(-10, 6, -10), new WorldPoint(10, 6, 10) };
        double Edge(WorldPoint[] box, int bit) => Math.Sqrt(Math.Pow(box[bit].X - box[0].X, 2) + Math.Pow(box[bit].Y - box[0].Y, 2) + Math.Pow(box[bit].Z - box[0].Z, 2));
        var straight = ObjectMarkingGeometry.Enclose(diagonal, WorldBasis.World);
        var turned = ObjectMarkingGeometry.Enclose(diagonal, new WorldBasis(new WorldPoint(1, 0, 1), new WorldPoint(0, 2, 0), new WorldPoint(-1, 0, 1)));
        Check(Math.Abs(Edge(straight, 1) - 20) < 1e-9 && Math.Abs(Edge(straight, 2) - 6) < 1e-9 && Math.Abs(Edge(straight, 4) - 20) < 1e-9, "in the world's directions the box around a diagonal is as wide as it is long");
        Check(Math.Abs(Edge(turned, 1) - 20 * Math.Sqrt(2)) < 1e-9 && Math.Abs(Edge(turned, 2) - 6) < 1e-9 && Edge(turned, 4) < 1e-9, "in the anchor's directions the box lies along the diagonal");
        foreach (var point in diagonal) Check(turned.Any(c => Math.Abs(c.X - point.X) < 1e-9 && Math.Abs(c.Y - point.Y) < 1e-9 && Math.Abs(c.Z - point.Z) < 1e-9), "the turned box has the objects at its corners");
        Check(ObjectMarkingGeometry.Enclose(new WorldPoint[0], WorldBasis.World) == null, "no points have no box");
        Check(Edge(ObjectMarkingGeometry.Enclose(diagonal, new WorldBasis(new WorldPoint(0, 0, 0), new WorldPoint(0, 1, 0), new WorldPoint(0, 0, 1))), 1) == 20, "a basis without a direction gives the world's directions");
        // The group's frame lies along its objects: an anchor turned a quarter against the row of masts, whose box
        // would be far larger, gives the same frame as the world's directions, along the row (FittedFrameTests).
        var quarter = At(800, out _, new WorldBasis(new WorldPoint(1, 0, 1), new WorldPoint(0, 1, 0), new WorldPoint(-1, 0, 1))).Frame.Value;
        Check(Math.Abs(quarter.Right - g.Right) < 1e-6 && Math.Abs(quarter.X - g.X) < 1e-6 && Math.Abs(quarter.Y - g.Y) < 1e-6 && Math.Abs(quarter.Bottom - g.Bottom) < 1e-6,
            "the group's box lies along its objects, not along an anchor turned against them");

        // On each side of 125 metres: the bounds begin at z 999.
        Check(At(999 - 125, out _).Shows == GroupShows.Objects && At(999 - 125.5, out _).Shows == GroupShows.MarkerAndFrame, "at 125 metres from its bounds a group shows its objects, beyond it one marker and one frame");
        Check(!At(999 - 125, out _).Frame.HasValue, "a group that shows its objects has no frame of its own");
        // On each side of 500 metres.
        var within = At(999 - 500, out _);
        Check(within.Shows == GroupShows.MarkerAndFrame && within.Frame.HasValue, "at 500 metres a group still has its frame");
        var beyond = At(999 - 500.5, out _);
        Check(beyond.Shows == GroupShows.MarkerOnly && !beyond.Frame.HasValue && beyond.Outline == null, "beyond 500 metres a group shows only its square marker");
        Check(Math.Abs(beyond.Distance - 500.5) < 1e-9, "the distance is known beyond the frame distance too");

        // The frame distance of one object counts from that object's own bounds, the group's from the group's
        // bounds. From z 499.05 the group's bounds, which begin at z 999, are 499.95 m away, and the bounds of each
        // mast, 9 m to the side, a little over 500 m: neither mast has a frame of its own, and the group's box
        // still surrounds both, from the parts of their pieces.
        var edge = new WorldPoint(0, 0, 499.05);
        var mixed = At(499.05, out frames);
        Check(frames[0].Distance > 500 && !frames[0].Frame.HasValue && frames[0].Outline == null && frames[0].Bounds.HasValue && !frames[1].Frame.HasValue, "an object beyond its own frame distance has bounds and no outline");
        Check(Math.Abs(mixed.Distance - 499.95) < 1e-9 && mixed.Shows == GroupShows.MarkerAndFrame && mixed.Frame.HasValue, "the group's frame does not need the frames of its objects");
        ScreenPoint left = Screen(new WorldPoint(-11, 6, 999), edge), right = Screen(new WorldPoint(11, 0, 999), edge);
        Check(Math.Abs(mixed.Frame.Value.X - (left.X - ObjectMarkingGeometry.Margin)) < 1e-3 && Math.Abs(mixed.Frame.Value.Right - (right.X + ObjectMarkingGeometry.Margin)) < 1e-3 && Math.Abs(mixed.Frame.Value.Y - (left.Y - ObjectMarkingGeometry.Margin)) < 1e-3,
            "the group's box surrounds objects with and without a frame of their own");
        // A candidate of that group has its own frame, asked for at the group's frame distance, and the group's
        // box is the same with it.
        var candidate = ObjectMarkingGeometry.Frame(west, edge, new[] { Box("west", -10, 3, 1000, 1, 3, 1) }, Camera(edge), View, null, Complexes.OwnFrameDistance(GroupShows.MarkerAndFrame));
        Check(candidate.Frame.HasValue && candidate.Distance > 500, "a candidate whose own bounds lie beyond 500 metres has its own frame while its group shows one");
        var withCandidate = ObjectMarkingGeometry.GroupFrame(edge, anchors, new[] { candidate, frames[1], null }, null, null, Camera(edge), View);
        Check(Math.Abs(withCandidate.Frame.Value.X - mixed.Frame.Value.X) < 1e-6 && Math.Abs(withCandidate.Frame.Value.Right - mixed.Frame.Value.Right) < 1e-6 && Math.Abs(withCandidate.Center.X) < 1e-9,
            "the group's box and the middle of its bounds stay the same beside a candidate that shows its own frame");

        // A group none of whose objects has a part is bounded and boxed by where its objects stand.
        var bare = ObjectMarkingGeometry.GroupFrame(eye800, anchors, new ObjectMarkingFrame[] { null, null, null }, null, null, Camera(eye800), View);
        Check(Math.Abs(bare.Distance - 200) < 1e-9 && bare.Shows == GroupShows.MarkerAndFrame && bare.Frame.HasValue && bare.Frame.Value.Height <= 2 * ObjectMarkingGeometry.Margin + 1e-3 && Math.Abs(bare.Frame.Value.Width - (72 + 2 * ObjectMarkingGeometry.Margin)) < 1e-3,
            "a group without parts is measured to, and boxed around, where its objects stand");
        // A group whose box is not in view has no frame; the least frame about its marker takes its place.
        var behind = new WorldPoint(0, 0, 1200);
        var unseen = ObjectMarkingGeometry.GroupFrame(behind, anchors, new ObjectMarkingFrame[] { null, null, null }, null, null, Camera(behind), View);
        Check(unseen.Shows == GroupShows.MarkerAndFrame && !unseen.Frame.HasValue, "a group behind the camera has no frame");
        Check(Double.IsNaN(ObjectMarkingGeometry.GroupFrame(new WorldPoint(0, 0, 0), new WorldPoint[0], new ObjectMarkingFrame[0], null, null, Camera(new WorldPoint(0, 0, 0)), View).Distance), "a group without objects has no distance");
    }

    static PickTarget T(double x, double y, bool marked = true, bool area = false) => new PickTarget(x, y, marked, area);
    static string Picked(WorldPickResult result) => String.Join(",", result.Objects) + (result.Groups.Count == 0 ? "" : " g" + String.Join(",", result.Groups));
    static void Picking() {
        // Objects 0 to 3 are a grade-crossing setup beyond 125 metres, which shows one square marker at 300, 300;
        // where its objects would stand on their own no longer counts. Object 4 is a whistle sign and object 5 a
        // board, each on its own.
        PickTarget[] Objects(bool frame4 = false, double boardX = 600) => new[] { T(280, 300), T(290, 300), T(310, 300), T(320, 300), T(400, 300, true, frame4), T(boardX, 300) };
        PickGroup Setup(bool framed = false, params int[] own) => new PickGroup(T(300, 300, true, framed), new[] { 0, 1, 2, 3 }, own);
        string Click(double x, double y, PickTarget[] objects, params PickGroup[] groups) => Picked(WorldPick.Resolve(x, y, objects, groups));

        Check(Click(302, 298, Objects(), Setup()) == "0,1,2,3 g0", "a click on a group's square marker lists the group's objects");
        Check(Click(280, 270, Objects(), Setup()) == "", "a click beside the group's marker and outside its frame picks nothing");
        Check(Click(330, 300, Objects(), Setup()) == "", "an object of a distant group has no marker of its own to click");
        Check(Click(340, 330, Objects(), Setup(true)) == "0,1,2,3 g0", "a click inside the group's frame and on no marker picks the group");
        Check(Click(370, 330, Objects(true), Setup(true)) == "4", "inside two frames the nearest square marker wins, here the whistle sign's");
        Check(Click(330, 330, Objects(true), Setup(true)) == "0,1,2,3 g0", "and the group's where its marker is nearer");
        Check(Click(400, 300, Objects(), Setup()) == "4", "a whistle sign is picked as its own object");
        // The board's marker stands on the group's spot: the list holds the group's objects together with the board.
        Check(Click(300, 300, Objects(boardX: 306), Setup()) == "0,1,2,3,5 g0", "overlapping markers list the group's objects together with the other objects");
        Check(Click(310, 300, Objects(boardX: 306), Setup()) == "5,0,1,2,3 g0", "the object whose marker is nearest leads the list");
        // A candidate or the inspected object of the group shows its own marker: object 3, at 320, 300.
        Check(Click(322, 300, Objects(), Setup(false, 3)) == "3", "a candidate of a distant group is picked on its own marker");
        Check(Click(296, 300, Objects(), Setup(false, 3)) == "0,1,2,3 g0", "and the group's marker still lists every object of the group");
        // Its own marker on the group's spot: listed once.
        Check(Click(300, 300, Objects(), Setup(false, 1)) == "0,1,2,3 g0", "an object that shows on the group's spot is listed once");
        // Two groups on one spot.
        var second = new PickGroup(T(304, 300), new[] { 4, 5 });
        Check(Click(300, 300, Objects(), Setup(), second) == "0,1,2,3,4,5 g0,1", "two groups on one spot list the objects of both");
        // A group whose marker and frame are not in view, such as one behind the camera, takes no click, and
        // neither do its objects; a click still picks what is in view.
        var away = new PickGroup(T(0, 0, false, false), new[] { 0, 1, 2, 3 });
        Check(Click(0, 0, Objects(), away) == "" && Click(300, 300, Objects(), away) == "" && Click(282, 300, Objects(), away) == "", "a group that is not in view takes no click, and neither do its objects");
        Check(Click(400, 300, Objects(), away) == "4" && Click(604, 300, Objects(), away) == "5", "with a group out of view the objects in view are picked as before");
        // Its candidate still shows its own marker when that is in view, and a marker on the unseen group's place
        // does not bring the group with it.
        Check(Click(322, 300, Objects(), new PickGroup(T(0, 0, false, false), new[] { 0, 1, 2, 3 }, new[] { 3 })) == "3", "a candidate of a group out of view is picked on its own marker");
        Check(Click(2, 2, new[] { T(0, 0), T(50, 50), T(60, 60) }, new PickGroup(T(0, 0, false, false), new[] { 1, 2 })) == "0", "a marker where an unseen group would stand is picked alone");
        // The detection spans of a grade-crossing setup take no part on their own while it shows as one, and its
        // choice list leaves them out: objects 1 and 3 are spans here.
        PickGroup Spanned(params int[] own) => new PickGroup(T(300, 300), new[] { 0, 1, 2, 3 }, own, new[] { 1, 3 });
        Check(Click(300, 300, Objects(), Spanned()) == "0,2 g0", "a click on the setup's square marker lists its objects other than its detection spans (got " + Click(300, 300, Objects(), Spanned()) + ")");
        Check(Click(290, 300, Objects(), Spanned()) == "0,2 g0" && Click(330, 300, Objects(), Spanned()) == "", "a detection span of a distant setup has no marker of its own to click");
        Check(Click(300, 300, Objects(boardX: 306), Spanned()) == "0,2,5 g0", "with overlapping markers the list holds the setup's objects without its spans, and the other objects");
        // A detection span that is the candidate or the inspected object, chosen in the outliner, shows its own marker.
        Check(Click(322, 300, Objects(), Spanned(3)) == "3", "a detection span that is the candidate is picked on its own marker");
        // Without a group the rule is the click rule of one object each.
        Check(Click(402, 300, Objects()) == "4" && Click(402, 300, Objects(), new PickGroup[0]) == "4" && WorldPick.Resolve(0, 0, null, null).Objects.Count == 0, "without groups every object is picked by the click rule");
    }
}
