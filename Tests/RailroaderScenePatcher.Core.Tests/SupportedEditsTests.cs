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
        IEnumerable<string> dependencies = null, string groupPath = null, string holderPath = null, BridgeKind bridge = BridgeKind.None, bool active = true, string only = null, object handle = null) =>
        new SceneObservation(handle ?? new object(), new TargetIdentity("scene", path, component, structure), family, active, new[] { 0.0, 0, 0 }, new double[] { 0, 0, 0, 1 },
            bindings ?? new string[0], dependencies ?? new string[0], new string[0], only ?? operations, inspectionOnly: only, groupPath: groupPath, holderPath: holderPath, bridge: bridge);

    static string Shown(SceneObservation entry, SupportedEdits.Live live = null) {
        var edits = SupportedEdits.Of(entry, live);
        foreach (var edit in edits.Where(e => e.Reason != null)) {
            Check(edit.Reason.Length <= 45, "reason length: " + edit.Reason);
            Check(CommandBand.Tooltip("Description.", edit.Reason) == edit.Reason + ".", "tooltip sentence: " + edit.Reason);
            foreach (var available in edits.Where(e => e.Reason == null))
                Check(edit.Reason.IndexOf(available.Name, StringComparison.OrdinalIgnoreCase) < 0, "reason repeats available edit: " + edit.Reason);
        }
        return String.Join(" | ", SupportedEdits.Lines(edits));
    }

    public static void Run() {
        CrossingControllerOffersOnlyItsConnections();
        EveryBaseMapObjectListsItsEdits();
        FictionalObjectsListTheirEdits();
        WhatTheWorldAddsChangesTheList();
        EachReasonIsItsControlsTooltip();
        PatternRefusalsUseTheirShortReason();
        AnAbsentSceneBlocksEveryEdit();
        LiveReasonsStayShortAndMatchTheirControls();
        Console.WriteLine("PASS: supported edits");
    }

    static void LiveReasonsStayShortAndMatchTheirControls() {
        var board = Entry("Location sign", LocationSigns.Structure, "Edits");
        foreach (var pair in new[] {
            (GameSwitchActivity.ProgressionMembersRefusal, "Progression data can't be read"),
            (GameSwitchActivity.MapFeatureMembersRefusal, "Map feature data can't be read"),
            (GameSwitchActivity.UnsharedRefusal, "Progression or a map feature switches it"),
        }) Check(Shown(board, new SupportedEdits.Live { DisableProblem = pair.Item1 }) == "Available: Move, Rotate, Follow a track | Unavailable: | • " + pair.Item2 + ": Disable", "live activity reason: " + pair.Item2);
        var bell = Entry("Crossing bell", ExistingEquipmentScene.BellStructure, CommandBand.BellOperations, active: false);
        Check(Shown(bell, new SupportedEdits.Live { ActivitySwitched = "This game build cannot reconfigure the bell when it is enabled again: missing method" })
            == "Available: Move | Unavailable: | • A bell's sound has no direction: Rotate | • Uses fixed placement: Follow a track | • Not supported on this game build: Enable", "bell build limit");
        var marker = Entry("Generic track marker", TrackMarkerScene.Structure, CommandBand.MarkerOperations);
        Check(Shown(marker, new SupportedEdits.Live { MarkerIdReason = CommandBand.MarkerIdUnavailable("Generic track marker", true, null, 0, false) })
            == "Available: Move, Rotate, Follow a track, Disable, Set track binding | Unavailable: | • Its current ID can't be read: Set replacement marker ID", "unreadable marker ID");
        var starter = Entry(StarterPlacements.Family, StarterPlacementScene.Structure, StarterPlacements.Operations,
            bindings: new[] { StarterPlacements.ReaderPrefix + new string('a', 1000), StarterPlacements.ReaderPrefix + new string('b', 1000) });
        Check(Shown(starter) == "Available: Set track location | Unavailable: | • Has no effect on where cars start: " + Placed + " | " + NoReplacementId + " | • Several new-game car placements read it: Set Start derailed", "several readers do not expand the reason");
        Check(CommandBand.Tooltip("Binding.", CommandBand.NoBindingReason(board)) == "Only markers, mileposts and pads have one.", "binding fallback tooltip");
    }

    static void AnAbsentSceneBlocksEveryEdit() {
        foreach (var entry in new[] {
            Entry("Grade crossing controller", CrossingCoverageScene.Structure, "Coverage"),
            Entry(StarterPlacements.Family, StarterPlacementScene.Structure, StarterPlacements.Operations),
            Entry(CommandBand.ConstructionSceneryFamily, ConstructionSceneryScene.Structure, CommandBand.ConstructionSceneryOperations),
        }) {
            var edits = SupportedEdits.Of(entry, new SupportedEdits.Live { Present = false });
            Check(edits.All(e => e.Reason == "Its scene isn't loaded"), "an absent " + entry.Family + " blocks connections too");
            Check(SupportedEdits.Lines(edits).Count == 2, "all absent-object edits share one bullet");
        }
    }

    static void PatternRefusalsUseTheirShortReason() {
        var cases = new Dictionary<string, string> {
            ["The board's scene is not loaded"] = "Its scene isn't loaded",
            ["2 objects share this hierarchy path, so a scene patch file cannot name this one"] = "Another object shares its hierarchy path",
            ["This object has no implemented bridge-placement capability"] = "Its structure isn't supported",
            ["The board's parents must be active"] = "Its parent object must be active",
            ["Editing this board would leave one of its attached objects out of date: " + new string('x', 1000)] = "Its attached object is out of date",
            ["The board's renderer culler must be present and enabled"] = "Its structure isn't supported",
            ["The board must carry exactly one enabled circle map mask"] = "Its structure isn't supported",
            ["the marker reference scan failed: " + new string('x', 1000)] = "Its marker references couldn't be read",
            ["The direct-reference scan is unavailable; other holders of this marker are unknown"] = "Its marker references couldn't be read",
            ["Crossing pad editing is unavailable"] = "Its state couldn't be read",
            ["The game's scene spawn point type is unavailable."] = "Game members it needs are unavailable",
            ["The game's construction controller is unavailable."] = "Game members it needs are unavailable",
            ["The game's ground mask members are unavailable, so you can't move, enable, or disable this group or its parts."] = "Game members it needs are unavailable",
            ["Construction scenery entries unavailable"] = "Game members it needs are unavailable",
            ["Missing script on the pad object"] = "Its structure isn't supported",
            ["The pad builder has no mesh container or mesh profile; native generation cannot produce its geometry"] = "Its structure isn't supported",
            ["Use the milepost's own settings to edit it"] = "Edit it on its milepost",
            ["Edit this marker through its crossing pad"] = "Edit it on its crossing pad",
            ["The pad's activity follows a track group; the crossing-pad operation does not support group-driven activity"] = "Its activity follows a track group",
            ["Only the Ela Bridge Scenery construction controller has construction segments of its own. For another rail bridge, set the construction segments on its group."] = "Edit it on its group",
            [SceneObjectRules.TrackMarkerReason] = "It holds track markers; edit them one by one",
            [SceneObjectRules.SpawnPointReason] = "It holds spawn points; edit them one by one",
            [SceneObjectRules.MapIconReason] = "It holds a map icon; edit it separately",
            [SceneObjectRules.StationAgentReason] = "Station click spots are native map data",
            [SceneObjectRules.ConstructionOutsideBridge] = "It holds a construction controller",
            [SceneObjectRules.TopObjectRefusal] = "Edit the group as a whole instead",
            [SceneObjectRules.CullerOnlyRefusal] = "Edit the parts it holds directly",
            [SceneObjectRules.GroupHolderRefusal] = "Edit the groups it holds instead",
            ["its crossing controller is disabled, so its bell cannot work"] = "Its crossing controller is disabled",
        };
        foreach (var pair in cases) {
            var operations = CommandBand.NotEditable(pair.Key);
            var entry = Entry("Location sign", null, operations, handle: new Box());
            var edits = SupportedEdits.Of(entry);
            Check(Shown(entry) == "Unavailable: | • " + pair.Value + ": Move, Rotate, Follow a track, Disable", "pattern refusal: " + pair.Key);
            Check(CommandBand.InspectorEditBlock(false, operations) == pair.Value, "footer reason equals listed reason");
            foreach (var edit in edits) Check(CommandBand.Tooltip("Description.", edit.Reason) == pair.Value + ".", "disabled tooltip equals short reason");
            var feedback = new EditFeedback();
            feedback.Refuse("Placement", "Move", pair.Key);
            Check(feedback.Messages[0].Text == "Move refused. " + pair.Value + ".", "refusal message uses the same short reason");
            var scene = new Scene { Observations = () => new[] { entry }, BeforeCapture = () => throw new InvalidOperationException(pair.Key) };
            scene.Objects[entry.Identity.Path] = new List<object> { entry.Handle };
            var inventory = new SceneInventory(scene);
            inventory.Refresh();
            const string patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"move\":{\"target\":{\"scene\":\"scene\",\"path\":\"World/X\",\"component\":\"UnityEngine.Transform\"},\"placement\":{\"mode\":\"fixed\",\"position\":[1,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}";
            feedback.Run("Placement", "Move", null, inventory, entry, _ => new PatchEngine(scene).Apply(patch).Outcomes["move"]);
            Check(feedback.Messages[0].Text.StartsWith("Move refused. " + pair.Value + ".", StringComparison.Ordinal), "engine refusal uses the same displayed reason: " + feedback.Messages[0].Text);
            Check(pair.Value.Length <= 45, "reason remains within 45 characters with long source values");
        }
        var noState = Entry("Location sign", null, null);
        Check(Shown(noState) == "Unavailable: | • Its state couldn't be read: Move, Rotate, Follow a track, Disable", "an empty operations list names the unread state");
        var noFamily = Entry("Unrecognised family", null, CommandBand.InspectionOnly("Unrecognised family", false));
        Check(Shown(noFamily) == "Unavailable: | • No edit for this family: Move, Rotate, Follow a track, Disable", "an inspection-only family has no edit");
        var values = new EditFeedback();
        values.Refuse("Placement", "Move", "Enter a finite number.");
        Check(values.Messages[0].Text == "Move refused. Enter a finite number.", "a value correction keeps its own text");
    }

    // The loaded world's facts that the entry does not hold change the edits and their reasons as they change the controls.
    static void WhatTheWorldAddsChangesTheList() {
        var board = Entry("Location sign", LocationSigns.Structure, "Edits of this family");
        Check(Shown(board, new SupportedEdits.Live { Enabled = false }) == "Available: Move, Rotate, Follow a track, Enable", "an inactive object offers Enable");
        var follower = Entry("Generic track marker", TrackMarkerScene.Structure, "Edits of this family", bindings: new[] { "Activity follows track group G1" }, active: false);
        Check(Shown(follower) == "Available: Move, Rotate, Follow a track, Set track binding, Set replacement marker ID | Unavailable: | • Clear activity returns it to its track group: Enable", "an inactive group follower takes Clear activity");
        var mast = Entry(ExistingEquipmentScene.MastFamily, ExistingEquipmentScene.MastStructure, CommandBand.MastOperations);
        Check(Shown(mast, new SupportedEdits.Live { ActivitySwitched = CommandBand.EquipmentSwitched }) == "Available: Move, Rotate | Unavailable: | " + NoFollowEquipment + " | • The game switches it by camera distance: Disable", "camera-switched equipment takes no activity setting");
        Check(Shown(board, new SupportedEdits.Live { DisableProblem = GameSwitchActivity.UnsharedRefusal }) == "Available: Move, Rotate, Follow a track | Unavailable: | • Progression or a map feature switches it: Disable"
            && Shown(board, new SupportedEdits.Live { Enabled = false, DisableProblem = GameSwitchActivity.UnsharedRefusal }) == "Available: Move, Rotate, Follow a track, Enable", "the disable problem holds back Disable only");
        var starter = Entry(StarterPlacements.Family, StarterPlacementScene.Structure, StarterPlacements.Operations, bindings: new[] { StarterPlacements.ReaderPrefix + "Kitchen: 3 cars" });
        Check(Shown(starter) == "Available: Set track location, Set Start derailed | Unavailable: | • Has no effect on where cars start: " + Placed + " | " + NoReplacementId, "one reader offers Start derailed");
        Check(Shown(starter, new SupportedEdits.Live { StartersError = "the setups are missing" }).EndsWith("• The new-game setups can't be read: Set Start derailed", StringComparison.Ordinal), "unreadable setups hold back Start derailed");
        var marker = Entry("Generic track marker", TrackMarkerScene.Structure, CommandBand.MarkerOperations);
        Check(Shown(marker, new SupportedEdits.Live { MarkerIdReason = CommandBand.MarkerIdUnavailable("Generic track marker", true, new string('x', 1000), 0, false) })
            == "Available: Move, Rotate, Follow a track, Disable, Set track binding | Unavailable: | • No other object holds its marker ID: Set replacement marker ID", "a unique marker ID is not a placeholder in the reason");
        Check(Shown(marker, new SupportedEdits.Live { Present = false }) == "Unavailable: | • Its scene isn't loaded: " + Placed + ", Set track binding, Set replacement marker ID", "an absent object has no available edit");
        var added = Entry(SceneFamilies.AddedEquipment, null, CommandBand.AddedEquipmentOperations("Signal 1 (added)"));
        Check(Shown(added) == "Unavailable: | • Edit it on its crossing controller: " + Placed, "added equipment points to its crossing controller");
        var swing = Entry(SceneObjectRules.Family, null, null, only: CommandBand.NotYetSupported("groups that hold a spawn point"));
        Check(Shown(swing) == "Unavailable: | • It holds spawn points; edit them one by one: " + Placed, "spawn points are edited one by one");
        var refusedPad = Entry("Crossing pad marker", null, CommandBand.NotEditable("Missing script on the pad object"));
        Check(Shown(refusedPad) == "Unavailable: | • Its structure isn't supported: " + Placed + ", Set track binding", "a refused pad groups its unavailable edits");
        var part = Entry(SceneObjectRules.ChildFamily, ChildObjects.Structure, CommandBand.PartOperations, groupPath: "World/G");
        Check(Shown(part, new SupportedEdits.Live { Bridge = BridgeKind.Rail }) == Expected["rail bridge"], "a bridge part uses fixed placement");
        var signGroup = Entry(SceneObjectRules.Family, SceneObjects.Structure, CommandBand.GroupOperations, bindings: new[] { "Marker segment: Abc" });
        Check(!Shown(signGroup).Contains(SupportedEdits.Binding), "binding lines do not invent a binding edit");
        var unreadMarker = Entry("Generic track marker", null, CommandBand.NotEditable("the marker reference scan failed: scan error"));
        Check(Shown(unreadMarker) == "Unavailable: | • Its marker references couldn't be read: " + Placed + ", Set track binding", "an unreadable Generic marker keeps its binding edit with its reason");
        foreach (var family in new[] { "Generic track marker", "Milepost", "Crossing pad marker" }) {
            var refused = Entry(family, null, CommandBand.NotEditable("the marker has no track location"));
            Check(SupportedEdits.ReasonOf(SupportedEdits.Of(refused), SupportedEdits.Binding) == "Its structure isn't supported", "a refused binding has its object's reason");
        }
    }

    // Every base-map object's disabled tooltip is its listed short reason with a final full stop.
    static void EachReasonIsItsControlsTooltip() {
        var map = new BaseMapFixture();
        var verdicts = map.Verdicts.GroupBy(v => v.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var o in map.Objects.Where(o => o.Listed)) {
            var (entry, live, _, _) = Listed(map, o, verdicts);
            var edits = SupportedEdits.Of(entry, live);
            var lines = SupportedEdits.Lines(edits);
            Check(lines.Count != 0 && lines.All(line => line.StartsWith("Available: ", StringComparison.Ordinal) || line == "Unavailable:" || line.StartsWith("• ", StringComparison.Ordinal)), o.Path + ": only available edits, heading and bullets");
            Check(edits.Any(e => e.Key == SupportedEdits.Binding) == (o.Family == "Generic track marker" || o.Family == "Milepost" || o.Family == "Crossing pad marker"), o.Path + ": only binding holders offer the binding edit");
            Check(lines.Count(line => line == "Unavailable:") == (edits.Any(e => e.Reason != null) ? 1 : 0), o.Path + ": one unavailable heading");
            Check(lines.Count(line => line.StartsWith("• ", StringComparison.Ordinal)) == edits.Where(e => e.Reason != null).Select(e => e.Reason).Distinct().Count(), o.Path + ": one bullet per distinct reason");
            foreach (var edit in edits.Where(e => e.Reason != null)) {
                var tooltip = CommandBand.Tooltip("What the control does.", edit.Reason);
                Check(tooltip == edit.Reason + "." && edit.Reason.Length <= 45 && !edit.Reason.EndsWith(".", StringComparison.Ordinal), o.Path + ": short reason equals tooltip sentence for " + edit.Name);
                var bullet = lines.Single(line => line.StartsWith("• " + edit.Reason + ": ", StringComparison.Ordinal));
                Check(bullet.Substring(edit.Reason.Length + 4).Split(new[] { ", " }, StringSplitOptions.None).Contains(edit.Name), o.Path + ": bullet covers " + edit.Name);
                foreach (var available in edits.Where(e => e.Reason == null))
                    Check(edit.Reason.IndexOf(available.Name, StringComparison.OrdinalIgnoreCase) < 0, o.Path + ": reason does not repeat available edit " + available.Name);
            }
            foreach (var mode in new[] { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode, CommandBand.FollowMode }) {
                var reason = SupportedEdits.ModeReason(edits, mode);
                var name = mode == CommandBand.FollowMode ? SupportedEdits.Follow : SupportedEdits.Move;
                Check((reason == null) == edits.Any(e => e.Name == name && e.Reason == null), o.Path + ": placement mode follows its listed edit");
            }
        }
    }

    // The explicit expected Supported edits of each kind of object, in the window's words.
    const string SharedBlock = "Another object shares its hierarchy path";
    const string Placed = "Move, Rotate, Follow a track, Disable";
    const string NoReplacementId = "• Generic track markers only: Set replacement marker ID";
    const string NoFollowEquipment = "• Uses fixed placement: Follow a track";
    const string RailNoFollow = "Unavailable: | " + NoFollowEquipment;
    static readonly Dictionary<string, string> Expected = new Dictionary<string, string> {
        ["scenery"] = "Available: " + Placed,
        ["rail bridge"] = "Available: Move, Rotate, Disable | " + RailNoFollow,
        ["road bridge"] = "Available: Move, Rotate, Disable | " + RailNoFollow,
        ["construction root"] = "Available: Move, Rotate, Disable, Set construction segments | " + RailNoFollow,
        ["marker"] = "Available: " + Placed + ", Set track binding, Set replacement marker ID",
        ["pad"] = "Available: Disable, Set track binding | Unavailable: | • Placed by its track binding: Move, Rotate, Follow a track | " + NoReplacementId,
        ["milepost"] = "Available: Move, Rotate, Disable, Set track binding | " + RailNoFollow + " | " + NoReplacementId,
        ["mast"] = "Available: Move, Rotate, Disable | " + RailNoFollow,
        ["bell"] = "Available: Move, Disable | Unavailable: | • A bell's sound has no direction: Rotate | " + NoFollowEquipment,
        ["crossing controller"] = "Available: Edit crossing coverage, Edit crossing equipment | Unavailable: | • Its placement and activity aren't edited: " + Placed,
        ["construction controller"] = "Available: Set construction segments | Unavailable: | • Its placement has no effect in the game: Move, Rotate, Follow a track | • The game needs it to show bridge parts: Disable",
        ["starter"] = "Available: Set track location | Unavailable: | • Has no effect on where cars start: " + Placed
            + " | " + NoReplacementId + " | • No new-game car placement reads it: Set Start derailed",
        ["shared board"] = "Unavailable: | • " + SharedBlock + ": " + Placed,
        ["detection span"] = "Unavailable: | • Edit it on its crossing controller: " + Placed,
        ["sign group"] = "Unavailable: | • It holds track markers; edit them one by one: " + Placed,
        ["refused marker"] = "Unavailable: | • Its structure isn't supported: " + Placed + ", Set track binding",
    };
    static string SharedMarker(int holders) => "Unavailable: | • " + SharedBlock + ": " + Placed + ", Set track binding";

    // A base-map fixture object as the inventory lists it: its family and structure, the refusal of an object that
    // shares its path, a detection span's controller, a group's bridge kind and a rail bridge's construction segments.
    // The class names the expected list it is checked against.
    static (SceneObservation Entry, SupportedEdits.Live Live, string Expected, string Class) Listed(BaseMapFixture map, BaseMapObject o, IReadOnlyDictionary<string, SceneObjectVerdict> verdicts) {
        var parent = o.Path.Substring(0, o.Path.LastIndexOf('/'));
        var holders = map.Objects.Count(other => other.Listed && other.Path == o.Path && other.Family == o.Family);
        var bindings = new List<string>();
        string operations = "Edits of this family", only = null, holder = null, cls;
        var bridge = BridgeKind.None;
        if (o.TopSceneObject || o.ChildObject) {
            var root = o.TopSceneObject ? o.Path : map.Objects.Where(r => r.TopSceneObject && o.Path.StartsWith(r.Path + "/", StringComparison.Ordinal)).OrderByDescending(r => r.Path.Length).FirstOrDefault()?.Path;
            verdicts.TryGetValue(root ?? "", out var verdict);
            bridge = verdict?.Bridge ?? BridgeKind.None;
            if (o.TopSceneObject && o.Structure == null) { only = CommandBand.GroupInspectionOnly(verdict, o.Family); cls = "sign group"; }
            else if (o.TopSceneObject && o.Components.Contains(SceneObjectRules.ConstructionController)) { bindings.Add(CommandBand.SegmentBinding("s1", true)); cls = "construction root"; }
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
            : o.Structure == LocationSigns.Structure || o.Structure == BridgeScene.Structure || o.Structure == StationMapIconRules.Structure || o.Structure == SpawnPoints.Structure ? "scenery" : "unknown " + o.Family + " " + o.Structure;
        var entry = Entry(o.Family, o.Structure, operations, o.Path, bindings: bindings, groupPath: o.ChildObject ? parent : null, holderPath: holder, bridge: o.TopSceneObject ? bridge : BridgeKind.None, only: only);
        var expected = cls == "shared marker" ? SharedMarker(holders) : Expected.TryGetValue(cls, out var text) ? text : null;
        return (entry, o.ChildObject ? new SupportedEdits.Live { Bridge = bridge } : null, expected, cls);
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

    // Every listed object of the base map, the parts of its scene objects with child objects included, against the expected list of its kind.
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
        ["Generic track marker"] = TrackMarkerScene.Structure, ["Location sign"] = LocationSigns.Structure, ["Milepost"] = MilepostScene.Structure, ["Crossing pad marker"] = CrossingPadScene.Structure,
        ["Grade crossing controller"] = CrossingCoverageScene.Structure, ["Crossing mast"] = ExistingEquipmentScene.MastStructure, ["Crossing bell"] = ExistingEquipmentScene.BellStructure,
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
                dependencies: new[] { CommandBand.GroundMaskDependency(true, new[] { o.Name }, true, o.TopSceneObject || o.ChildObject, o.TopSceneObject) });
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
        var controller = Entry("Grade crossing controller", CrossingCoverageScene.Structure, "Set which tracks each circuit detects.", component: CrossingCoverageScene.ComponentType);
        var edits = SupportedEdits.Of(controller);
        const string reason = "Its placement and activity aren't edited";
        Check(Shown(controller) == "Available: Edit crossing coverage, Edit crossing equipment | Unavailable: | • " + reason + ": Move, Rotate, Follow a track, Disable",
            "a crossing controller: " + Shown(controller));
        foreach (var mode in new[] { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode, CommandBand.FollowMode })
            Check(SupportedEdits.ModeReason(edits, mode) == reason, "placement mode " + mode + " of a crossing controller is refused with its reason: " + SupportedEdits.ModeReason(edits, mode));
        Check(CommandBand.Tooltip("Disable this object.", SupportedEdits.ReasonOf(edits, SupportedEdits.Activity)) == reason + ".", "its Disable tooltip gives the same reason");
        // Its lights, gates and bells are an edit of their own, unavailable with the reason the session gives when they cannot be read.
        var unread = new SupportedEdits.Live { EquipmentReason = "the crossing's equipment could not be read" };
        Check(Shown(controller, unread) == "Available: Edit crossing coverage | Unavailable: | • " + reason + ": Move, Rotate, Follow a track, Disable | • Its lights, gates and bells can't be read: Edit crossing equipment",
            "a crossing controller whose equipment cannot be read: " + Shown(controller, unread));
    }
}
// An object's entry for the Supported edits model, for the tests of one family's controls: its family, its structure,
// its refusal text and its group's bridge kind.
static class EditEntries {
    public static SceneObservation Of(string family, string structure, string operations, BridgeKind bridge = BridgeKind.None) =>
        new SceneObservation(new object(), new TargetIdentity("scene", "World/Edit", "UnityEngine.Transform", structure), family, true, new double[] { 0, 0, 0 }, new double[] { 0, 0, 0, 1 },
            new string[0], new string[0], new string[0], operations, bridge: bridge);
}
