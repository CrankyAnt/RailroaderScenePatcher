// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Supported edits on the Object tab, at the inventory presentation seam: the edits an inventory entry's controls offer,
// and for each edit that is not available the reason its control's tooltip gives.
static class SupportedEditsTests {
    static void Check(bool value, string message) { if (!value) throw new Exception("Supported edits: " + message); }

    static SceneObservation Entry(string family, string structure, string operations, string path = "World/X", string component = "UnityEngine.Transform", IEnumerable<string> bindings = null,
        IEnumerable<string> dependencies = null, string groupPath = null, string holderPath = null, BridgeKind bridge = BridgeKind.None, bool active = true, string only = null) =>
        new SceneObservation(new object(), new TargetIdentity("scene", path, component, structure), family, active, new[] { 0.0, 0, 0 }, new double[] { 0, 0, 0, 1 },
            bindings ?? new string[0], dependencies ?? new string[0], new string[0], only ?? operations, inspectionOnly: only, groupPath: groupPath, holderPath: holderPath, bridge: bridge);

    static string Shown(SceneObservation entry, SupportedEdits.Live live = null) => String.Join(" | ", SupportedEdits.Lines(SupportedEdits.Of(entry, live)));

    public static void Run() {
        CrossingControllerOffersOnlyItsConnections();
        EveryBaseMapObjectListsItsEdits();
        FictionalObjectsListTheirEdits();
        WhatTheWorldAddsChangesTheList();
        EachReasonIsItsControlsTooltip();
        Console.WriteLine("PASS: supported edits");
    }

    // The loaded world's facts that the entry does not hold change the edits and their reasons as they change the controls.
    static void WhatTheWorldAddsChangesTheList() {
        var board = Entry("Independent board", IndependentBoards.Structure, "Edits of this family");
        Check(Shown(board, new SupportedEdits.Live { Enabled = false }) == "Available: Move, Rotate, Follow a track, and Enable. | " + NoBinding, "an inactive object offers Enable: " + Shown(board, new SupportedEdits.Live { Enabled = false }));
        var follower = Entry("Track marker", TrackMarkerScene.Structure, "Edits of this family", bindings: new[] { "Activity follows track group G1" }, active: false);
        Check(Shown(follower) == "Available: Move, Rotate, Follow a track, Set track binding, and Set marker ID. | Unavailable: Enable. The track group controls this object's activity. Use Clear activity to let the object follow its group again.",
            "an inactive object that follows its track group cannot be enabled: " + Shown(follower));
        Check(Shown(Entry("Track marker", TrackMarkerScene.Structure, "x", bindings: new[] { "Activity follows track group G1" })).StartsWith("Available: Move, Rotate, Follow a track, Disable,", StringComparison.Ordinal), "it can still be disabled");
        var mast = Entry(ExistingEquipmentScene.MastFamily, ExistingEquipmentScene.MastStructure, CommandBand.MastOperations);
        Check(Shown(mast, new SupportedEdits.Live { ActivitySwitched = CommandBand.EquipmentSwitched }) == "Available: Move and Rotate. | " + NoFollowEquipment
            + " | Unavailable: Disable. The game's culler enables and disables this object based on camera distance. It would override a saved Enabled setting, so the patcher cannot keep that setting applied. | " + NoBinding,
            "equipment that a culler switches takes no Enabled setting: " + Shown(mast, new SupportedEdits.Live { ActivitySwitched = CommandBand.EquipmentSwitched }));
        Check(Shown(board, new SupportedEdits.Live { DisableProblem = GameSwitchActivity.UnsharedRefusal }).Contains("Unavailable: Disable. " + CommandBand.Sentence(GameSwitchActivity.UnsharedRefusal))
            && Shown(board, new SupportedEdits.Live { Enabled = false, DisableProblem = GameSwitchActivity.UnsharedRefusal }).Contains("and Enable."), "a disable that the game would undo holds back Disable only");
        var starter = Entry(StarterPlacements.Family, StarterPlacementScene.Structure, StarterPlacements.Operations, bindings: new[] { StarterPlacements.ReaderPrefix + "Kitchen: 3 cars" });
        Check(Shown(starter).StartsWith("Available: Set track location and Choose Start derailed.", StringComparison.Ordinal), "a marker that one car placement reads offers Start derailed: " + Shown(starter));
        Check(Shown(starter, new SupportedEdits.Live { StartersError = "the setups are missing" }).EndsWith("Unavailable: Choose Start derailed. " + CommandBand.WreckReason("the setups are missing", new string[0]), StringComparison.Ordinal), "unreadable setups hold back Start derailed");
        var marker = Entry("Track marker", TrackMarkerScene.Structure, CommandBand.MarkerOperations);
        Check(Shown(marker, new SupportedEdits.Live { MarkerIdReason = "Only this marker holds marker ID 7, so it doesn't need a replacement ID." })
            == "Available: Move, Rotate, Follow a track, Disable, and Set track binding. | Unavailable: Set marker ID. Only this marker holds marker ID 7, so it doesn't need a replacement ID.", "a marker whose ID no other marker holds takes no replacement ID");
        Check(Shown(marker, new SupportedEdits.Live { Present = false }) == "Available: Set track binding and Set marker ID. | Unavailable: Move, Rotate, Follow a track, and Disable. This object cannot be edited here.",
            "an object that no longer stands takes no placement or Enabled setting: " + Shown(marker, new SupportedEdits.Live { Present = false }));
        var added = Entry(SceneFamilies.AddedEquipment, null, CommandBand.AddedEquipmentOperations("Signal 1 (added)"));
        Check(Shown(added) == Placed + CommandBand.AddedEquipmentReason + " | " + NoBinding, "added equipment names its crossing's Connections tab: " + Shown(added));
        var swing = Entry(BakedSceneryRules.Family, null, null, only: CommandBand.NotYetSupported("baked scenery groups that hold a spawn point"));
        Check(Shown(swing) == Placed + "Baked scenery groups that hold a spawn point cannot be edited. | " + NoBinding, "an inspection-only object offers no edit: " + Shown(swing));
        var refusedPad = Entry("Crossing pad marker", null, CommandBand.NotEditable("the pad is part of a crossing"));
        Check(Shown(refusedPad) == "Unavailable: Move, Rotate, Follow a track, Disable, and Set track binding. The pad is part of a crossing.", "a refused pad names its refusal once: " + Shown(refusedPad));
        var part = Entry(BakedSceneryRules.Family, BakedSceneryParts.Structure, CommandBand.PartOperations, groupPath: "World/G");
        Check(Shown(part, new SupportedEdits.Live { Bridge = BridgeKind.Rail }) == Expected["rail bridge"], "a part of a rail bridge does not follow a track");
        var adapter = Entry("Independent board", IndependentBoards.Structure, "x");
        Check(Shown(adapter, new SupportedEdits.Live { MarkerRefusal = "the marker has no track location" }).EndsWith("Unavailable: Set track binding. The marker has no track location.", StringComparison.Ordinal), "the marker adapter's refusal names why the binding cannot be set");
    }

    // For every edit that is not available, the reason Supported edits names is what its control's tooltip gives after
    // "Unavailable:", for every base-map object.
    static void EachReasonIsItsControlsTooltip() {
        var map = new BaseMapFixture();
        var verdicts = map.Verdicts.GroupBy(v => v.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var o in map.Objects.Where(o => o.Listed)) {
            var (entry, live, _, _) = Listed(map, o, verdicts);
            var edits = SupportedEdits.Of(entry, live);
            var lines = SupportedEdits.Lines(edits);
            foreach (var edit in edits.Where(e => e.Reason != null)) {
                var tooltip = CommandBand.Tooltip("What the control does.", edit.Reason);
                Check(tooltip.StartsWith("Unavailable: ", StringComparison.Ordinal) && lines.Any(line => line.StartsWith("Unavailable: ", StringComparison.Ordinal) && line.Contains(edit.Name)
                    && line.EndsWith(". " + tooltip.Substring("Unavailable: ".Length), StringComparison.Ordinal)), o.Path + ": " + edit.Name + " names its tooltip's reason " + tooltip);
            }
            foreach (var mode in new[] { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode, CommandBand.FollowMode }) {
                var reason = SupportedEdits.ModeReason(edits, mode);
                var name = mode == CommandBand.FollowMode ? SupportedEdits.Follow : SupportedEdits.Move;
                var listed = lines.Any(line => line.StartsWith("Available: ", StringComparison.Ordinal) && line.Contains(name));
                Check((reason == null) == listed, o.Path + ": placement mode " + mode + " is open exactly when its edit is listed");
            }
        }
    }

    // The explicit expected Supported edits of each kind of object, in the window's words.
    const string NoBinding = "Unavailable: Set track binding. Only track markers, mileposts, and crossing pads have editable track bindings.";
    const string SharedBlock = "This object can't be edited while another object has the same hierarchy path.";
    const string Placed = "Unavailable: Move, Rotate, Follow a track, and Disable. ";
    const string NoReplacementId = "Unavailable: Set marker ID. Only objects in the Track marker family can have a replacement ID. This object is in the ";
    const string NoFollowEquipment = "Unavailable: Follow a track. Existing crossing masts and bells use fixed placement. Track-following placement does not apply to them.";
    const string RailNoFollow = "Unavailable: Follow a track. Rail bridges use fixed placement. They don't support track-following placement. Move the track in the native map editor, then move the bridge onto it.";
    static readonly Dictionary<string, string> Expected = new Dictionary<string, string> {
        ["scenery"] = "Available: Move, Rotate, Follow a track, and Disable. | " + NoBinding,
        ["rail bridge"] = "Available: Move, Rotate, and Disable. | " + RailNoFollow + " | " + NoBinding,
        ["road bridge"] = "Available: Move, Rotate, and Disable. | Unavailable: Follow a track. Road bridges use fixed placement. They don't support track-following placement. Move the road in the native map editor, then move the bridge onto it. | " + NoBinding,
        ["construction root"] = "Available: Move, Rotate, Disable, and Set construction segments. | " + RailNoFollow,
        ["marker"] = "Available: Move, Rotate, Follow a track, Disable, Set track binding, and Set marker ID.",
        ["pad"] = "Available: Disable and Set track binding. | Unavailable: Move, Rotate, and Follow a track. A crossing pad follows its track: its geometry is generated along its track binding. Bind it to another track under Connections. | " + NoReplacementId + "Crossing pad marker family.",
        ["milepost"] = "Available: Move, Rotate, Disable, and Set track binding. | Unavailable: Follow a track. Mileposts use fixed placement. Set the track binding under Connections. | " + NoReplacementId + "Milepost family.",
        ["mast"] = "Available: Move, Rotate, and Disable. | " + NoFollowEquipment + " | " + NoBinding,
        ["bell"] = "Available: Move and Disable. | Unavailable: Rotate. A bell's sound has no direction, so rotation does not apply to it. You can still set its position. | " + NoFollowEquipment + " | " + NoBinding,
        ["crossing controller"] = "Available: Edit crossing coverage. | " + Placed + "You cannot move, rotate, enable, or disable a crossing controller. | " + NoBinding,
        ["construction controller"] = "Available: Set construction segments. | Unavailable: Move, Rotate, and Follow a track. A construction controller switches the bridge's parts on or off regardless of its own position. Moving it has no effect in the game. Move the bridge or its parts instead. | Unavailable: Disable. This controller switches the bridge's parts on or off as construction progresses. Disabling it would stop the game from doing this. Disable the bridge or its parts instead.",
        ["starter"] = "Available: Set track location. | " + Placed + "A new-game car placement reads only this marker's track location, so the object's placement and activity do not change where its cars start; place the marker on the track under Connections. | "
            + NoReplacementId + "Starter placement family. | Unavailable: Choose Start derailed. No new-game car placement in an active setup reads this marker, so it is not a starter placement marker.",
        ["shared board"] = Placed + SharedBlock + " | " + NoBinding,
        ["detection span"] = Placed + "A span's track locations determine its position, so you cannot set its placement. To change which tracks the crossing detects, use its controller's Connections tab. | Unavailable: Set track binding. A detection span's tracks come from its controller's crossing coverage. To change which tracks it detects, edit the coverage on its controller's Connections tab.",
        ["sign group"] = Placed + "You can't move this group as a whole. Edit its track markers individually in the Track marker family. | " + NoBinding,
        // A refused marker: the fixture does not record why the adapter refused it, so it stands in for that reason.
        ["refused marker"] = "Unavailable: Move, Rotate, Follow a track, Disable, and Set track binding. The marker adapter refused this marker.",
    };
    // A marker or pad that shares its path: its track binding names the refusal of the shared path.
    static string SharedMarker(int holders) => Placed + SharedBlock + " | Unavailable: Set track binding. " + holders + " objects have this hierarchy path, and a scene patch file names an object by its path.";

    // A base-map fixture object as the inventory lists it: its family and structure, the refusal of an object that
    // shares its path, a detection span's controller, a group's bridge kind and a rail bridge's construction segments.
    // The class names the expected list it is checked against.
    static (SceneObservation Entry, SupportedEdits.Live Live, string Expected, string Class) Listed(BaseMapFixture map, BaseMapObject o, IReadOnlyDictionary<string, GroupRootVerdict> verdicts) {
        var parent = o.Path.Substring(0, o.Path.LastIndexOf('/'));
        var holders = map.Objects.Count(other => other.Listed && other.Path == o.Path && other.Family == o.Family);
        var bindings = new List<string>();
        string operations = "Edits of this family", only = null, holder = null, cls;
        var bridge = BridgeKind.None;
        if (o.BakedRoot || o.BakedPart) {
            var root = o.BakedRoot ? o.Path : map.Objects.Where(r => r.BakedRoot && o.Path.StartsWith(r.Path + "/", StringComparison.Ordinal)).OrderByDescending(r => r.Path.Length).FirstOrDefault()?.Path;
            verdicts.TryGetValue(root ?? "", out var verdict);
            bridge = verdict?.Bridge ?? BridgeKind.None;
            if (o.BakedRoot && o.Structure == null) { only = CommandBand.GroupInspectionOnly(verdict, o.Family); cls = "sign group"; }
            else if (o.BakedRoot && o.Components.Contains(BakedSceneryRules.ConstructionController)) { bindings.Add(CommandBand.SegmentBinding("s1", true)); cls = "construction root"; }
            else cls = bridge == BridgeKind.Rail ? "rail bridge" : bridge == BridgeKind.Road ? "road bridge" : "scenery";
        } else if (o.Family == "Track span") {
            holder = map.Objects.Any(c => c.Listed && c.Path == parent && c.Structure == CrossingCoverageScene.Structure) ? parent : null;
            operations = holder != null ? CommandBand.DetectionSpanOperations : null; only = holder == null ? CommandBand.SpanInspectionOnly(false) : null; cls = holder != null ? "detection span" : "track span";
        } else if (o.Structure == null && holders > 1) {
            operations = CommandBand.NotEditable(CommandBand.SharedPath(holders));
            cls = CommandBand.HoldsMarker(o.Family) ? "shared marker" : "shared board";
        } else if (o.Structure == null && CommandBand.HoldsMarker(o.Family)) {
            operations = CommandBand.NotEditable("the marker adapter refused this marker"); cls = "refused marker";
        } else cls = o.Structure == TrackMarkerScene.Structure ? "marker" : o.Structure == CrossingPadScene.Structure ? "pad" : o.Structure == MilepostScene.Structure ? "milepost"
            : o.Structure == ExistingEquipmentScene.MastStructure ? "mast" : o.Structure == ExistingEquipmentScene.BellStructure ? "bell" : o.Structure == CrossingCoverageScene.Structure ? "crossing controller"
            : o.Structure == ConstructionSceneryScene.Structure ? "construction controller" : o.Structure == StarterPlacementScene.Structure ? "starter"
            : o.Structure == IndependentBoards.Structure || o.Structure == BridgeScene.Structure || o.Structure == StationMapIconRules.Structure || o.Structure == SpawnPoints.Structure ? "scenery" : "unknown " + o.Family + " " + o.Structure;
        var entry = Entry(o.Family, o.Structure, operations, o.Path, bindings: bindings, groupPath: o.BakedPart ? parent : null, holderPath: holder, bridge: o.BakedRoot ? bridge : BridgeKind.None, only: only);
        var expected = cls == "shared marker" ? SharedMarker(holders) : Expected.TryGetValue(cls, out var text) ? text : null;
        return (entry, o.BakedPart ? new SupportedEdits.Live { Bridge = bridge } : null, expected, cls);
    }
    static List<string> Mismatches(BaseMapFixture map, IEnumerable<BaseMapObject> objects, Dictionary<string, int> classes) {
        var verdicts = map.Verdicts.GroupBy(v => v.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var wrong = new List<string>();
        foreach (var o in objects.Where(o => o.Listed)) {
            var (entry, live, expected, cls) = Listed(map, o, verdicts);
            classes[cls] = classes.TryGetValue(cls, out var n) ? n + 1 : 1;
            var shown = Shown(entry, live);
            if (shown != expected) wrong.Add(o.Path + " (" + cls + "): " + shown);
        }
        return wrong;
    }

    // Every listed object of the base map, the parts of its baked scenery groups included, against the expected list of its kind.
    static void EveryBaseMapObjectListsItsEdits() {
        var map = new BaseMapFixture();
        var classes = new Dictionary<string, int>();
        var wrong = Mismatches(map, map.Objects, classes);
        Check(wrong.Count == 0, wrong.Count + " base-map objects list other edits than expected: " + String.Join(" || ", wrong.Take(6)));
        Check(classes.Keys.All(cls => !cls.StartsWith("unknown", StringComparison.Ordinal)), "every base-map object has an expected list: " + String.Join(", ", classes.Keys));
        foreach (var cls in new[] { "scenery", "rail bridge", "road bridge", "marker", "pad", "milepost", "mast", "bell", "crossing controller", "construction controller", "starter", "shared marker", "shared board", "detection span", "sign group", "refused marker" })
            Check(classes.ContainsKey(cls), "the base map holds a " + cls + ": " + String.Join(", ", classes.Select(pair => pair.Key + " " + pair.Value)));
    }

    // The structure the inventory gives an object of a supported family that it accepts, for the fictional objects,
    // which are built the game's way.
    static readonly Dictionary<string, string> Structures = new Dictionary<string, string> {
        ["Track marker"] = TrackMarkerScene.Structure, ["Independent board"] = IndependentBoards.Structure, ["Milepost"] = MilepostScene.Structure, ["Crossing pad marker"] = CrossingPadScene.Structure,
        ["Crossing controller"] = CrossingCoverageScene.Structure, ["Crossing mast"] = ExistingEquipmentScene.MastStructure, ["Crossing bell"] = ExistingEquipmentScene.BellStructure,
        ["Spawn point"] = SpawnPoints.Structure, ["Station map icon"] = StationMapIconRules.Structure, ["Bridge"] = BridgeScene.Structure,
    };
    static BaseMapObject Accepted(BaseMapObject o) { if (o.Structure == null && o.Family != null && Structures.TryGetValue(o.Family, out var structure)) o.Structure = structure; return o; }

    // Each fictional object of the scene classification test, and objects of every family that holds a ground mask that
    // flattens the ground, lists what its controls offer: a mask no longer holds back Move.
    static void FictionalObjectsListTheirEdits() {
        var fiction = SceneClassificationTests.Fictions().Select(Accepted).Concat(new[] {
            new BaseMapObject { Listed = true, Path = "World/Large Scenery/Judson/Judson Bridge", Family = "Bridge", Structure = BridgeScene.Structure, Components = new[] { "UnityEngine.MeshCollider" }, At = new[] { -9210.0, -3210 } },
            new BaseMapObject { Listed = true, Path = "World/Roads Hewitt/Grade Crossing - Main St/Bell", Family = "Crossing bell", Structure = ExistingEquipmentScene.BellStructure, Components = new[] { "Track.Crossings.CrossingBell" }, At = new[] { -13812.0, -8182 } },
        }).ToArray();
        var map = new BaseMapFixture(fiction);
        var classes = new Dictionary<string, int>();
        var wrong = Mismatches(map, fiction, classes);
        Check(wrong.Count == 0, wrong.Count + " fictional objects list other edits than expected: " + String.Join(" || ", wrong.Take(6)));
        foreach (var cls in new[] { "scenery", "road bridge", "marker", "pad", "milepost", "mast", "bell", "crossing controller" })
            Check(classes.ContainsKey(cls), "the fictional objects hold a " + cls + ": " + String.Join(", ", classes.Select(pair => pair.Key + " " + pair.Value)));
        // A ground mask that flattens the ground, held by the object itself or by a part below it.
        var verdicts = map.Verdicts.GroupBy(v => v.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var holders = new[] { "World/Roads Judson/Crossing Unprotected - Mill Road/Sign-Crossing0 (1)", "World/Roads Judson/Crossing Unprotected - Mill Road/Crossing Pad", "World/Mile Posts/M-12",
            "World/Map Labels/Station Icons/Judson Station", "World/POIs/Judson", "World/Large Scenery/Judson/Judson Bridge", "World/Signs/Whistle Post - Judson", "World/Roads Hewitt/Hewitt Road Bridge 30m" };
        foreach (var path in holders) {
            var o = fiction.First(f => f.Path == path);
            var (entry, live, expected, cls) = Listed(map, o, verdicts);
            var masked = Entry(entry.Family, entry.Identity.Structure, entry.Operations, entry.Identity.Path, bindings: entry.Bindings, groupPath: entry.GroupPath, holderPath: entry.HolderPath, bridge: entry.Bridge,
                dependencies: new[] { CommandBand.GroundMaskDependency(true, new[] { o.Name }, true, o.BakedRoot || o.BakedPart, o.BakedRoot) });
            Check(CommandBand.HoldsGroundMask(masked.Dependencies) && Shown(masked, live) == expected, path + " (" + cls + ") with a flattening ground mask lists what its controls offer: " + Shown(masked, live));
        }
        // The crossbuck lists Move.
        var crossbuck = fiction.First(f => f.Path.EndsWith("Sign-Crossing0 (1)", StringComparison.Ordinal));
        Check(verdicts.TryGetValue(crossbuck.Path, out var verdict) && verdict.Crossbuck, "the fictional crossbuck is a crossbuck group");
        Check(Listed(map, crossbuck, verdicts).Expected.StartsWith("Available: Move,", StringComparison.Ordinal), "a crossbuck lists Move");
    }

    // A crossing controller's placement and activity are refused by its capability, so no Placement mode and no Enabled
    // control is offered; its crossing coverage is.
    static void CrossingControllerOffersOnlyItsConnections() {
        var controller = Entry("Crossing controller", CrossingCoverageScene.Structure, "Set which tracks each circuit detects.", component: CrossingCoverageScene.ComponentType);
        var edits = SupportedEdits.Of(controller);
        const string reason = "You cannot move, rotate, enable, or disable a crossing controller.";
        Check(Shown(controller) == "Available: Edit crossing coverage. | Unavailable: Move, Rotate, Follow a track, and Disable. " + reason
            + " | Unavailable: Set track binding. Only track markers, mileposts, and crossing pads have editable track bindings.", "a crossing controller: " + Shown(controller));
        foreach (var mode in new[] { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode, CommandBand.FollowMode })
            Check(SupportedEdits.ModeReason(edits, mode) == reason, "placement mode " + mode + " of a crossing controller is refused with its reason: " + SupportedEdits.ModeReason(edits, mode));
        Check(CommandBand.Tooltip("Disable this object.", SupportedEdits.ReasonOf(edits, SupportedEdits.Activity)) == "Unavailable: " + reason, "its Disable tooltip gives the same reason");
    }
}
// An object's entry for the Supported edits model, for the tests of one family's controls: its family, its structure,
// its refusal text and its group's bridge kind.
static class EditEntries {
    public static SceneObservation Of(string family, string structure, string operations, BridgeKind bridge = BridgeKind.None) =>
        new SceneObservation(new object(), new TargetIdentity("scene", "World/Edit", "UnityEngine.Transform", structure), family, true, new double[] { 0, 0, 0 }, new double[] { 0, 0, 0, 1 },
            new string[0], new string[0], new string[0], operations, bridge: bridge);
}
