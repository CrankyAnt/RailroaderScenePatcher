// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Crossbucks at unprotected crossings: each crossbuck is a group of its own under Grade crossing, identified through its
// crossing's pad marker ID where crossings share one hierarchy path, as the three crossings below Old River Rd do.
static class CrossbuckTests {
    const string Scene = "BushnellWhittier-MapManager";
    const string OldRiver = "World/Roads Bryson/Old River Rd/Crossing Unprotected", Own = "World/Roads Bryson/Crossing Unprotected (1)",
        Nantahala = "World/Large Scenery/Nantahala/Crossing Unprotected";
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run() {
        InventoryListsEachCrossbuckOnce();
        CrossbucksKnowTheirPadMarkerId();
        PadMarkerIdIsRefusedElsewhere();
        PadMarkerIdentityResolvesOneCrossbuck();
        MoveChangesOnlyThatCrossbuck();
        EnabledHoldsAndRepeatsWithoutOffset();
        PreflightRefusalLeavesEverythingUnchanged();
        FailureAfterMutationRestoresTheCrossbuck();
        IncompleteCleanupBlocksRetry();
        EditingHistoryRemovalAndSave();
        ObjectTabAndOutliner();
        Console.WriteLine("PASS: crossbucks at unprotected crossings");
    }

    static Node Pad(string id) => new Node("Crossing Pad").With("Track.TrackMarker", "Track.TrackCurveMeshBuilder").Solid().Family().Id(id);
    static Node Whistle(string side) => new Node("Whistle Sign " + side).With("Track.TrackMarker", "Track.TrackRelativePosition").Solid().Family();
    // A crossbuck as the 0.6.3 scene holds it: its renderer culler on the root, a model object below it, and the two LOD
    // levels of the model below that.
    static Node Buck(string name) => new Node(name, new Node("Sign-Crossing0", new Node("SignCrossing_LOD0").Solid().Lod(), new Node("SignCrossing_LOD1").Solid().Lod())).With("Helpers.Culling.RendererCuller");
    static Node Crossing(string name, string pad) => new Node(name, Pad(pad), Whistle("E"), Whistle("W"), Buck("Sign-Crossing0 (1)"), Buck("Sign-Crossing0 (2)"));
    static Node World(params string[] oldRiverIds) => new Node("World",
        new Node("Roads Bryson",
            new Node("Old River Rd", oldRiverIds.Select(id => Crossing("Crossing Unprotected", id)).ToArray()),
            Crossing("Crossing Unprotected (1)", "Mown")),
        new Node("Large Scenery", new Node("Nantahala", Crossing("Crossing Unprotected", "Mnan"), new Node("Nantahala Station", new Node("Depot").Solid()))));
    static readonly string[] OldRiverIds = { "Mbkd", "M0sh", "Mgdu" };
    static GroupRootVerdict At(IEnumerable<GroupRootVerdict> verdicts, string path) => verdicts.SingleOrDefault(v => v.Path == path);

    // Every crossbuck is listed once, as a group under Grade crossing, wherever its crossing stands below World; its
    // models are no parts, and nothing of it stands in the Inspection only group or under another category.
    static void InventoryListsEachCrossbuckOnce() {
        var verdicts = BakedSceneryRules.Classify(World(OldRiverIds));
        var bucks = verdicts.Where(v => v.Node.Name.StartsWith("Sign-Crossing0 (", StringComparison.Ordinal)).ToArray();
        Check(bucks.Length == 10 && bucks.All(v => v.Disposition == GroupDisposition.Group && v.Crossbuck && v.Category == BakedSceneryCategories.GradeCrossing && v.Subcategory == null && v.Ticket == null),
            "each of the ten crossbucks is a group of its own under Grade crossing (got " + String.Join(", ", bucks.Select(v => v.Path + " " + v.Disposition + " " + v.Category)) + ")");
        Check(bucks.Select(v => v.Path + "|" + v.PadMarkerId).Distinct().Count() == 10 && bucks.Count(v => v.Path.StartsWith(OldRiver + "/", StringComparison.Ordinal)) == 6,
            "each crossbuck is listed once; the six of Old River Rd share their raw paths and differ by their crossing's pad marker ID");
        Check(!verdicts.Any(v => v.Crossbuck && (v.Disposition == GroupDisposition.InspectionOnly || v.Category != BakedSceneryCategories.GradeCrossing)), "no crossbuck is inspection only or under another category");
        Check(At(verdicts, Nantahala + "/Sign-Crossing0 (1)")?.Category == BakedSceneryCategories.GradeCrossing && At(verdicts, Own + "/Sign-Crossing0 (2)")?.Category == BakedSceneryCategories.GradeCrossing,
            "a crossbuck below World/Large Scenery and one below World/Roads both stand under Grade crossing");
        // The crossing pad and whistle signs keep their own families.
        Check(verdicts.Count(v => v.Disposition == GroupDisposition.ExistingFamily && (v.Node.Name == "Crossing Pad" || v.Node.Name.StartsWith("Whistle Sign", StringComparison.Ordinal))) == 15,
            "the pad and whistle signs of each crossing keep their families");
        // A crossbuck is one entry: its model object and LOD levels are no parts.
        var parts = BakedSceneryRules.Parts(verdicts);
        var below = parts.Where(p => bucks.Any(b => b.Path == p.GroupPath)).ToArray();
        Check(below.Length == 30 && below.All(p => p.Disposition == PartDisposition.NotListed && p.Reason == BakedSceneryRules.CrossbuckModels),
            "the model and LOD levels below each crossbuck are not listed, under their coverage rule (got " + below.Length + ")");
        Check(parts.Any(p => p.Disposition == PartDisposition.Part && p.GroupPath == "World/Large Scenery/Nantahala/Nantahala Station"), "another group keeps its parts");
    }
    // Each crossbuck knows its crossing's pad marker ID; its path stays the raw hierarchy path, shared below Old River Rd.
    static void CrossbucksKnowTheirPadMarkerId() {
        var verdicts = BakedSceneryRules.Classify(World(OldRiverIds));
        foreach (var buck in new[] { "/Sign-Crossing0 (1)", "/Sign-Crossing0 (2)" })
            Check(verdicts.Where(v => v.Path == OldRiver + buck).Select(v => v.PadMarkerId).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(new[] { "M0sh", "Mbkd", "Mgdu" }),
                OldRiver + buck + " is the raw path of three crossbucks, one per pad marker ID");
        Check(At(verdicts, Own + "/Sign-Crossing0 (1)").PadMarkerId == "Mown" && At(verdicts, Nantahala + "/Sign-Crossing0 (1)") != null, "a crossbuck with a path of its own keeps its plain path");
        var none = BakedSceneryRules.Classify(World(null, "M0sh"));
        Check(none.Count(v => v.Path == OldRiver + "/Sign-Crossing0 (2)" && v.PadMarkerId == null) == 1, "a crossing without a pad marker ID gives its crossbucks none");
    }

    // The controlled world of the crossbucks: one FakeGroup per crossbuck, at the hierarchy path the scene gives it, with
    // its renderer culler on the root and its model and LOD levels below. The pads and whistle signs stand beside them.
    sealed class Fixture {
        internal readonly FakeGroupWorld World = new FakeGroupWorld();
        // By raw path, and by raw path and pad marker ID for the crossbucks of crossings that share one path.
        internal readonly Dictionary<string, FakeGroup> Bucks = new Dictionary<string, FakeGroup>();
        internal readonly List<FakeGroup> Others = new List<FakeGroup>();
        internal readonly BakedSceneryGroups Scene;
        internal Fixture(params string[] oldRiverIds) {
            var verdicts = BakedSceneryRules.Classify(CrossbuckTests.World(oldRiverIds.Length == 0 ? OldRiverIds : oldRiverIds));
            var x = 0.0;
            var crossbucks = verdicts.Where(v => v.Crossbuck).ToArray();
            foreach (var verdict in crossbucks) {
                var buck = new FakeGroup { Position = new[] { 100 + (x += 10), 0, 20 }, RootCulling = null };
                buck.Parts.Clear();
                var model = buck.Add(new FakePart { Id = "Sign-Crossing0#1", Offset = new[] { 0.0, 0, 0 }, Empty = true });
                buck.Add(new FakePart { Id = "SignCrossing_LOD0#2", Offset = new[] { 0.0, 1, 0 }, Parent = model });
                buck.Add(new FakePart { Id = "SignCrossing_LOD1#3", Offset = new[] { 0.0, 1, 0 }, Parent = model });
                buck.Cullers.Clear(); buck.Cullers.Add(new FakeCuller { Id = "RendererCuller#4", OnRoot = true, Simple = false });
                var shared = crossbucks.Count(v => v.Path == verdict.Path) > 1;
                Bucks.Add(Key(verdict.Path, shared ? verdict.PadMarkerId : null), buck);
                if (!World.Objects.TryGetValue(verdict.Path, out var at)) World.Objects[verdict.Path] = at = new List<object>();
                at.Add(buck);
                if (!World.CrossbuckIds.TryGetValue(verdict.Path, out var ids)) World.CrossbuckIds[verdict.Path] = ids = new List<KeyValuePair<object, string>>();
                ids.Add(new KeyValuePair<object, string>(buck, verdict.PadMarkerId));
            }
            // A pad and whistle signs as objects of their own families: no group path names them.
            foreach (var other in verdicts.Where(v => v.Disposition == GroupDisposition.ExistingFamily)) {
                var held = new FakeGroup { Position = new[] { 500 + (x += 10), 0, 20 } };
                Others.Add(held); World.Objects["other:" + other.Path + "#" + Others.Count] = new List<object> { held };
            }
            foreach (var buck in Bucks.Values.Concat(Others)) World.SetActive(buck, true);
            World.Mutations = 0;
            Scene = new BakedSceneryGroups(World);
        }
        internal string State() => String.Join(";", Bucks.Values.Concat(Others).Select(g => String.Join(",", g.Position) + "/" + String.Join(",", g.Rotation) + "/" + g.Active + "/" + String.Join(",", g.Parts.SelectMany(p => p.Offset))));
    }
    static string Key(string path, string padMarkerId = null) => padMarkerId == null ? path : path + "|" + padMarkerId;
    static string Modification(string id, string path, double x = 1500, string extra = "", bool placement = true, string pad = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"baked-scenery-group-v1\"" +
        (pad == null ? "" : ",\"padMarkerId\":\"" + pad + "\"") + "}" +
        (placement ? ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",0,25],\"rotation\":[0,0.707107,0,0.707107]}" : "") + extra + ",\"dependsOn\":[]}";
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";

    // The shared path with a pad marker ID names that crossing's crossbuck alone; the shared path without one names
    // three and an unknown id none. Both fail that modification before mutation, and the others still apply.
    static void PadMarkerIdentityResolvesOneCrossbuck() {
        var fixture = new Fixture();
        var buck = fixture.Bucks[Key(OldRiver + "/Sign-Crossing0 (1)", "M0sh")];
        TargetIdentity Id(string path, string pad = null) => new TargetIdentity(Scene, path, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure, pad);
        Check(fixture.Scene.Resolve(Id(OldRiver + "/Sign-Crossing0 (1)", "M0sh")).Single() == buck, "the shared path with the pad marker ID resolves to the crossbuck of that crossing");
        Check(fixture.Scene.Resolve(Id(OldRiver + "/Sign-Crossing0 (1)")).Count == 3, "the shared path without a pad marker ID resolves to the three crossbucks");
        Check(fixture.Scene.Resolve(Id(Own + "/Sign-Crossing0 (2)")).Single() == fixture.Bucks[Own + "/Sign-Crossing0 (2)"], "a crossbuck with a path of its own resolves by its path");
        var result = new PatchEngine(fixture.Scene).Apply(Doc(Modification("shared", OldRiver + "/Sign-Crossing0 (1)"), Modification("unknown", OldRiver + "/Sign-Crossing0 (1)", pad: "Mzzz"),
            Modification("m0sh", OldRiver + "/Sign-Crossing0 (1)", pad: "M0sh")));
        Check(!result.Outcomes["shared"].Applied && result.Outcomes["shared"].ResolvedTarget == null && !result.Outcomes["unknown"].Applied && result.Outcomes["unknown"].ResolvedTarget == null,
            "a crossbuck identity that resolves to several crossbucks or to none fails: " + result.Outcomes["shared"].Error + " / " + result.Outcomes["unknown"].Error);
        Check(result.Outcomes["m0sh"].Applied && buck.Position[0] == 500 && fixture.World.Mutations == 1, "the other modification still applies, and the failed ones mutate nothing: " + result.Outcomes["m0sh"].Error);
        // An empty padMarkerId is no identifier.
        var empty = new PatchEngine(fixture.Scene).Apply(Doc(Modification("empty", OldRiver + "/Sign-Crossing0 (1)", pad: ""))).Outcomes["empty"];
        Check(!empty.Applied && empty.Error.Contains("Invalid or unsupported target"), "an empty padMarkerId is refused as an invalid target: " + empty.Error);
    }
    // A pad marker ID names a crossbuck at a shared path only: a crossbuck whose path names it alone, another group and
    // another adapter refuse it with the reason, before anything changes.
    static void PadMarkerIdIsRefusedElsewhere() {
        var fixture = new Fixture();
        var before = fixture.State();
        var result = new PatchEngine(fixture.Scene).Apply(Doc(Modification("own", Own + "/Sign-Crossing0 (1)", pad: "Mown"), Modification("station", "World/Large Scenery/Nantahala/Nantahala Station", pad: "Mbkd")));
        Check(!result.Outcomes["own"].Applied && result.Outcomes["own"].Error.Contains(BakedSceneryRules.PadMarkerUnshared), "a crossbuck on an unshared path refuses padMarkerId: " + result.Outcomes["own"].Error);
        Check(!result.Outcomes["station"].Applied && result.Outcomes["station"].Error.Contains(TargetIdentity.PadMarkerNotAccepted), "a target that names no crossbuck refuses padMarkerId: " + result.Outcomes["station"].Error);
        var parts = new PatchEngine(new BakedSceneryParts(fixture.World, new PartHolds())).Apply(Doc(Modification("part", OldRiver + "/Sign-Crossing0 (1)", pad: "Mbkd").Replace("baked-scenery-group-v1", "baked-scenery-part-v1"))).Outcomes["part"];
        Check(!parts.Applied && parts.Error.Contains(TargetIdentity.PadMarkerNotAccepted), "another adapter refuses padMarkerId: " + parts.Error);
        var composed = new SceneCapabilities(() => fixture.World.Maps); composed.Add(fixture.Scene); composed.Add(new BakedSceneryParts(fixture.World, new PartHolds()));
        var viaParts = new PatchEngine(composed).Apply(Doc(Modification("part", OldRiver + "/Sign-Crossing0 (1)", pad: "Mbkd").Replace("baked-scenery-group-v1", "baked-scenery-part-v1"))).Outcomes["part"];
        Check(!viaParts.Applied && viaParts.Error.Contains(TargetIdentity.PadMarkerNotAccepted), "the composed capabilities refuse padMarkerId for another structure: " + viaParts.Error);
        var viaGroups = new PatchEngine(composed).Apply(Doc(Modification("group", OldRiver + "/Sign-Crossing0 (2)", pad: "Mgdu"))).Outcomes["group"];
        Check(viaGroups.Applied && fixture.Bucks[Key(OldRiver + "/Sign-Crossing0 (2)", "Mgdu")].Position[0] == 500, "the composed capabilities resolve the crossbuck through its pad marker ID: " + viaGroups.Error);
        Check(fixture.World.Mutations == 1, "the refusals mutate nothing");
        // A target with padMarkerId needs the patcher that knows the crossbuck identity, whatever its other content.
        var store = new Store { Patch = Doc(Modification("hand", OldRiver + "/Sign-Crossing0 (1)", pad: "Mbkd").Replace("\"component\":\"UnityEngine.Transform\",\"structure\":\"baked-scenery-group-v1\"", "\"component\":\"Other.Component\"")) };
        var minimum = new PatchEditingSession(new PatchEngine(fixture.Scene), new NativeMapEditor(), store).MinimumPatcherVersion;
        Check(minimum == "0.5.0", "a target with padMarkerId raises the dependency minimum to 0.5.0 (got " + minimum + ")");
    }
    // A crossbuck move changes that crossbuck alone: the pad, the whistle signs and the other crossbucks keep their
    // placement and activity, and the moved crossbuck's renderer culler follows it.
    static void MoveChangesOnlyThatCrossbuck() {
        foreach (var (path, pad) in new[] { (OldRiver + "/Sign-Crossing0 (2)", "Mbkd"), (Own + "/Sign-Crossing0 (1)", (string)null) }) {
            var fixture = new Fixture();
            var key = Key(path, pad); var buck = fixture.Bucks[key];
            var models = buck.Parts.SelectMany(p => p.Offset).ToArray();
            var others = String.Join(";", fixture.Bucks.Where(pair => pair.Key != key).Select(pair => String.Join(",", pair.Value.Position) + pair.Value.Active)) + fixture.Others.Select(o => String.Join(",", o.Position) + o.Active).Aggregate("", (a, b) => a + b);
            var outcome = new PatchEngine(fixture.Scene).Apply(Doc(Modification("move", path, pad: pad))).Outcomes["move"];
            Check(outcome.Applied && buck.Position.SequenceEqual(new[] { 500.0, 0, 25 }) && buck.Rotation[1] == 0.707107, "the crossbuck takes its map placement: " + outcome.Error);
            Check(buck.Parts.SelectMany(p => p.Offset).SequenceEqual(models), "its model and LOD levels move with it");
            Check(buck.Cullers.Single().Registered.SequenceEqual(new[] { 500.0, 0, 25 }), "its renderer culler's sphere follows it");
            var now = String.Join(";", fixture.Bucks.Where(pair => pair.Key != key).Select(pair => String.Join(",", pair.Value.Position) + pair.Value.Active)) + fixture.Others.Select(o => String.Join(",", o.Position) + o.Active).Aggregate("", (a, b) => a + b);
            Check(now == others, "the pad, the whistle signs and the other crossbucks keep their placement and activity");
        }
    }
    // Enabled off deactivates the crossbuck and releases its culling sphere; on brings both back. Repeated application
    // keeps the absolute placement.
    static void EnabledHoldsAndRepeatsWithoutOffset() {
        var fixture = new Fixture();
        var path = OldRiver + "/Sign-Crossing0 (1)"; var buck = fixture.Bucks[Key(path, "Mgdu")];
        var off = new PatchEngine(fixture.Scene).Apply(Doc(Modification("off", path, extra: ",\"active\":false", placement: false, pad: "Mgdu"))).Outcomes["off"];
        Check(off.Applied && !buck.Active && buck.Cullers.Single().Registered == null, "disabling the crossbuck deactivates it and releases its culling sphere: " + off.Error);
        var on = new PatchEngine(fixture.Scene).Apply(Doc(Modification("on", path, extra: ",\"active\":true", placement: false, pad: "Mgdu"))).Outcomes["on"];
        Check(on.Applied && buck.Active && buck.Cullers.Single().Registered != null, "enabling the crossbuck registers its culling sphere again: " + on.Error);
        var engine = new PatchEngine(fixture.Scene);
        engine.Apply(Doc(Modification("move", path, pad: "Mgdu")));
        Check(engine.Apply(Doc(Modification("move", path, pad: "Mgdu"))).Outcomes["move"].Applied && buck.Position.SequenceEqual(new[] { 500.0, 0, 25 }), "repeated application accumulates no offset");
        fixture.World.Origin = 1010;
        Check(new PatchEngine(fixture.Scene).Apply(Doc(Modification("move", path, pad: "Mgdu"))).Outcomes["move"].Applied && buck.Position[0] == 490, "a load at another origin reaches the same map placement");
    }
    static readonly TargetIdentity Identity = new TargetIdentity(Scene, OldRiver + "/Sign-Crossing0 (1)", BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure, "Mbkd");
    static readonly string IdentityKey = Key(OldRiver + "/Sign-Crossing0 (1)", "Mbkd");
    static void PreflightRefusalLeavesEverythingUnchanged() {
        var fixture = new Fixture();
        var before = fixture.State();
        fixture.World.Refused = "The group's scene is not loaded";
        var outcome = new PatchEngine(fixture.Scene).Apply(Doc(Modification("move", Identity.Path, pad: "Mbkd"))).Outcomes["move"];
        Check(!outcome.Applied && outcome.Error.Contains("not loaded") && fixture.World.Mutations == 0 && fixture.State() == before, "a refused crossbuck is not touched: " + outcome.Error);
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(fixture.Scene), mapEditor, store); mapEditor.Session = session;
        var text = session.ScenePatchFileText;
        Check(!session.Edit(Identity, new Placement(new[] { 1500.0, 0, 25 }, new[] { 0.0, 0, 0, 1 }), null).Applied && session.ScenePatchFileText == text && fixture.World.Mutations == 0 && !mapEditor.CanUndo,
            "a scene patch editing refusal leaves the scene, the scene patch file and the history unchanged");
    }
    static void FailureAfterMutationRestoresTheCrossbuck() {
        var cases = new (Action<FakeGroupWorld> inject, string extra)[] { (w => w.ThrowAfterPlacement = true, ",\"active\":false"), (w => w.SkipCullingRefresh = true, ""), (w => w.LeavePartBehind = true, "") };
        foreach (var (inject, extra) in cases) {
            var fixture = new Fixture();
            var buck = fixture.Bucks[IdentityKey]; var position = buck.Position.ToArray(); var before = fixture.State();
            inject(fixture.World);
            var outcome = new PatchEngine(fixture.Scene).Apply(Doc(Modification("move", Identity.Path, extra: extra, pad: "Mbkd"))).Outcomes["move"];
            Check(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal), "a failure after mutation is restored: " + outcome.Error);
            Check(fixture.State() == before && buck.Cullers.Single().Registered.SequenceEqual(position), "the crossbuck's captured placement, activity and culling are restored");
        }
    }
    static void IncompleteCleanupBlocksRetry() {
        var fixture = new Fixture();
        fixture.World.LeavePartBehind = fixture.World.KeepPartBehind = true;
        var engine = new PatchEngine(fixture.Scene);
        var outcome = engine.Apply(Doc(Modification("move", Identity.Path, pad: "Mbkd"))).Outcomes["move"];
        Check(!outcome.Applied && outcome.Error.Contains("could not be verified"), "incomplete cleanup is reported explicitly: " + outcome.Error);
        fixture.World.LeavePartBehind = fixture.World.KeepPartBehind = false;
        var mutations = fixture.World.Mutations;
        var retry = engine.Apply(Doc(Modification("again", Identity.Path, 1520, pad: "Mbkd"))).Outcomes["again"];
        Check(!retry.Applied && retry.Error.Contains("Cleanup") && fixture.World.Mutations == mutations, "a retry on the affected crossbuck is blocked until cleanup is verified or the world reloads");
    }
    // Scene patch editing of an Old River Rd crossbuck: its edits, native Undo and Redo, the persistence they leave,
    // Save, a reload and Remove modification.
    static void EditingHistoryRemovalAndSave() {
        var fixture = new Fixture();
        var buck = fixture.Bucks[IdentityKey]; var position = buck.Position.ToArray();
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(fixture.Scene), mapEditor, store); mapEditor.Session = session;
        var moved = new Placement(new[] { 1500.0, 0, 25 }, new[] { 0, 0.707107, 0, 0.707107 });
        Check(session.Edit(Identity, moved, null).Applied && buck.Position[0] == 500, "a scene patch editing move moves the crossbuck");
        Check(session.Edit(Identity, null, false).Applied && !buck.Active, "a scene patch editing Enabled off disables the crossbuck");
        var item = (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"].Single().First;
        var target = (JObject)item["target"];
        Check((string)target["path"] == OldRiver + "/Sign-Crossing0 (1)" && (string)target["padMarkerId"] == "Mbkd" && (string)target["structure"] == BakedSceneryGroups.Structure && item["placement"] != null && (bool)item["active"] == false,
            "the scene patch file names the crossbuck by its raw path and its crossing's pad marker ID and records its placement and Enabled");
        Check(target.Properties().Select(p => p.Name).SequenceEqual(new[] { "scene", "path", "component", "structure", "padMarkerId" }), "padMarkerId stands after structure in the canonical order");
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Check(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Persistence(true, all, "After a crossbuck move and a disable");
        mapEditor.Undo(); Check(buck.Active && buck.Position[0] == 500, "Undo enables the moved crossbuck again");
        Persistence(true, all, "After Undo of the disable");
        mapEditor.Undo(); Check(buck.Position.SequenceEqual(position), "Undo returns the crossbuck to its prior placement");
        Persistence(false, new string[0], "After Undo back to the unedited crossbuck");
        mapEditor.Redo(); Check(buck.Position[0] == 500, "Redo reapplies the move");
        mapEditor.Redo(); Check(!buck.Active, "Redo reapplies the disable");
        Persistence(true, all, "After Redo of both edits");
        Check(mapEditor.Save() == null && !mapEditor.IsDirty && store.Info.Contains("crankyant/scene-patcher >=0.5.0") && (int)JObject.Parse(store.Patch)["formatVersion"] == 1,
            "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        // A reloaded world reproduces the edits on the same crossbuck alone, without duplicated owned state.
        var reloaded = new Fixture();
        var replay = new PatchEngine(reloaded.Scene).Apply(store.Patch);
        var again = reloaded.Bucks[IdentityKey];
        Check(replay.Outcomes.Values.Single().Applied && again.Position[0] == 500 && !again.Active && again.Parts.Count == 3 && reloaded.Bucks.Count == 10
            && reloaded.Bucks.Where(pair => pair.Key != IdentityKey).All(pair => pair.Value.Active), "a reloaded world reproduces the crossbuck edits without duplicated state");
        Check(session.Remove(Identity).Applied && buck.Position.SequenceEqual(position) && buck.Active, "Remove modification returns the effective underlying state");
        mapEditor.Undo(); Check(buck.Position[0] == 500 && !buck.Active, "Undo of the removal restores this mod's crossbuck edits");
        Check(!mapEditor.IsDirty, "Undo back to the saved scene patch file leaves nothing to save");
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = new Fixture(); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(new PatchEngine(other.Scene), strictEditor, strict); strictEditor.Session = kept;
        Check(kept.Edit(Identity, moved, null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=0.5.0"), "a stricter existing constraint is retained: " + strict.Info);
    }
    sealed class Listing : IInspectionScene {
        public List<SceneObservation> Objects = new List<SceneObservation>();
        public IEnumerable<string> MapIds => new[] { "map" };
        public IEnumerable<SceneObservation> Discover() => Objects;
    }
    // The Object tab names what the game uses a crossbuck for and its supported edits; the outliner lists the crossbucks
    // under Grade crossing and names the crossing, with its pad marker ID where crossings share one path.
    static void ObjectTabAndOutliner() {
        Check(CommandBand.UseInGame(false, BakedSceneryCategories.GradeCrossing, null, false) == CommandBand.CrossbuckUse && CommandBand.CrossbuckUse.Contains("the game doesn't read it")
            && CommandBand.CrossbuckUse.Contains("Moving or disabling the crossbuck doesn't affect game functions"), "the Object tab names the crossbuck's use and that moving or disabling it changes no game function");
        Check(CommandBand.CrossbuckOperations.StartsWith("Move, rotate, enable, or disable this crossbuck.", StringComparison.Ordinal), "the supported edits name the crossbuck");
        Check(InventoryPresentationState.GroupWhere(OldRiver + "/Sign-Crossing0 (1)") == "Roads Bryson / Old River Rd / Crossing Unprotected"
            && InventoryPresentationState.GroupWhere(Own + "/Sign-Crossing0 (1)") == "Roads Bryson / Crossing Unprotected (1)", "a crossbuck row's location is its raw path");
        // The row label names the crossbuck at its crossing, so the crossbucks read apart and a search finds them by
        // crossing name; the full path stays in the tooltip. Where crossings at other paths share the crossing's name,
        // the label adds the place the crossing stands at, as a crossing mast is named.
        const string Andrews = "World/Roads Andrews/Crossing Unprotected - Robbinsville", Topton = "World/Roads Topton/Crossing Unprotected - Robbinsville";
        var paths = new[] { OldRiver + "/Sign-Crossing0 (1)", OldRiver + "/Sign-Crossing0 (1)", Own + "/Sign-Crossing0 (1)", Nantahala + "/Sign-Crossing0 (1)",
            Andrews + "/Sign-Crossing0 (1)", Andrews + "/Sign-Crossing0 (2)", Topton + "/Sign-Crossing0 (1)", Topton + "/Sign-Crossing0 (2)" };
        var shared = BakedSceneryRules.SharedCrossingNames(paths);
        Check(shared.SetEquals(new[] { "Crossing Unprotected", "Crossing Unprotected - Robbinsville" }),
            "a crossing name is shared when crossings at two or more paths hold it; the crossings of one path share no name (got " + String.Join(", ", shared) + ")");
        Check(BakedSceneryRules.CrossbuckName(Own + "/Sign-Crossing0 (1)", null, shared) == "Sign-Crossing0 (1) at Crossing Unprotected (1)"
            && BakedSceneryRules.CrossbuckName(OldRiver + "/Sign-Crossing0 (2)", "Mbkd", shared) == "Sign-Crossing0 (2) at Crossing Unprotected in Old River Rd (pad marker Mbkd)"
            && BakedSceneryRules.CrossbuckName(Nantahala + "/Sign-Crossing0 (1)", null, shared) == "Sign-Crossing0 (1) at Crossing Unprotected in Nantahala"
            && BakedSceneryRules.CrossbuckName(Andrews + "/Sign-Crossing0 (1)", null, shared) == "Sign-Crossing0 (1) at Crossing Unprotected - Robbinsville in Andrews"
            && BakedSceneryRules.CrossbuckName(Topton + "/Sign-Crossing0 (1)", null, shared) == "Sign-Crossing0 (1) at Crossing Unprotected - Robbinsville in Topton"
            && BakedSceneryRules.CrossbuckName(Andrews + "/Sign-Crossing0 (1)") == "Sign-Crossing0 (1) at Crossing Unprotected - Robbinsville",
            "a crossbuck is named with its crossing, with the place where crossings share a name and the pad marker ID where crossings share one path");
        SceneObservation Row(string path, string pad = null) => new SceneObservation(new object(), new TargetIdentity(Scene, path, "UnityEngine.Transform", BakedSceneryGroups.Structure, pad), BakedSceneryRules.Family, true,
            new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, new string[0], new string[0], new string[0], CommandBand.CrossbuckOperations,
            displayName: BakedSceneryRules.CrossbuckName(path, pad, shared), category: BakedSceneryCategories.GradeCrossing);
        var listed = new Listing();
        listed.Objects.AddRange(new[] { Row(OldRiver + "/Sign-Crossing0 (1)", "Mbkd"), Row(OldRiver + "/Sign-Crossing0 (1)", "M0sh"), Row(Andrews + "/Sign-Crossing0 (1)"),
            Row(Andrews + "/Sign-Crossing0 (2)"), Row(Topton + "/Sign-Crossing0 (1)"), Row(Topton + "/Sign-Crossing0 (2)") });
        var inventory = new SceneInventory(listed); inventory.Refresh();
        var tree = InventoryPresentationState.Categories(InventoryPresentationState.Groups(InventoryPresentationState.Rows(inventory, "", OutlinerFilter.All, null))[0].Value, inventory.Entries, "");
        var labels = tree.Single(n => n.Label == BakedSceneryCategories.GradeCrossing).Children.Select(n => n.Label).ToArray();
        Check(labels.Length == 6 && labels.Distinct().Count() == 6 && labels.Contains("Sign-Crossing0 (1) at Crossing Unprotected in Old River Rd (pad marker Mbkd)")
            && labels.Contains("Sign-Crossing0 (2) at Crossing Unprotected - Robbinsville in Andrews") && labels.Contains("Sign-Crossing0 (2) at Crossing Unprotected - Robbinsville in Topton"),
            "the outliner rows under Grade crossing read apart by their crossing (got " + String.Join(", ", labels) + ")");
        var found = InventoryPresentationState.Rows(inventory, "Robbinsville", OutlinerFilter.All, null);
        Check(found.Count() == 4 && found.Select(row => row.DisplayName).Distinct().Count() == 4 && found.Count(row => row.DisplayName.EndsWith("Robbinsville in Andrews", StringComparison.Ordinal)) == 2,
            "a search by crossing name finds the crossbucks of both crossings of that name, each label alike to no other");
        Check(InventoryPresentationState.Rows(inventory, "Robbinsville in Topton", OutlinerFilter.All, null).Count() == 2, "a search by crossing name and place finds that crossing's crossbucks");
        Check(InventoryPresentationState.Rows(inventory, "pad marker M0sh", OutlinerFilter.All, null).Single().Identity.PadMarkerId == "M0sh", "a search by pad marker ID finds the Old River Rd crossbuck");
        var copied = JObject.Parse(PatchEditingSession.CopyId(new[] { "map" }, listed.Objects[0]))["target"];
        Check((string)copied["path"] == OldRiver + "/Sign-Crossing0 (1)" && (string)copied["padMarkerId"] == "Mbkd", "Copy ID shows the pad marker ID beside the raw path: " + copied);
        var tip = InventoryPresentationState.OutlinerTooltip(listed.Objects[0], false);
        Check(tip.StartsWith("Sign-Crossing0 (1) at Crossing Unprotected in Old River Rd (pad marker Mbkd)\n" + OldRiver + "/Sign-Crossing0 (1)", StringComparison.Ordinal), "the tooltip shows the full name and path: " + tip);
    }
}
