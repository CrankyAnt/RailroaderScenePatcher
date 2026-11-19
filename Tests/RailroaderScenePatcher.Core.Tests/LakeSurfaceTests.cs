// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Lake surfaces: the Whittier Log Pond and Fontana Lake (1) and (2) join the Baked scenery group family with their
// parts, the log pond keeps showing the sawmill's storage, and each lake's culling follows it. Controlled hierarchies
// follow the Railroader 0.6.3 scene: the log pond's root holds its water mesh (a LakePolygon with a mesh renderer) and
// its logs (a Scenery.LogPond without a renderer of its own); a Fontana lake is one water mesh with a renderer culler.
static class LakeSurfaceTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Lake surfaces: " + message); }
    const string LogPond = "World/Large Scenery/Whittier/Whittier Log Pond", Water = LogPond + "/Whittier Saw Mill Log Pond", Logs = LogPond + "/LogPond",
        Fontana1 = "World/Rivers/Fontana Lake (1)", Fontana2 = "World/Rivers/Fontana Lake (2)";
    const string Lake = "LakePolygon", Renderer = "UnityEngine.MeshRenderer", Culler = "Helpers.Culling.RendererCuller";
    internal static Node World() => new Node("World",
        new Node("Large Scenery", new Node("Whittier",
            new Node("Whittier Log Pond", new Node("Whittier Saw Mill Log Pond").With(Lake, Renderer).Solid(), new Node("LogPond").With("Scenery.LogPond")),
            new Node("Whittier Saw Mill Culled", new Node("Saw Shed").Solid()))),
        new Node("Rivers",
            new Node("Fontana Lake (1)").With(Lake, Renderer, Culler).Solid(),
            new Node("Fontana Lake (2)").With(Lake, Renderer, Culler).Solid()));
    static GroupRootVerdict At(IReadOnlyList<GroupRootVerdict> verdicts, string path) => verdicts.SingleOrDefault(v => v.Path == path);

    public static void Run() {
        InventoryListsLakeSurfacesWithTheirParts();
        NoExclusionNamesALakeSurface();
        ObjectTabNamesTheLogPondStorage();
        StaticBatchRefusesAMove();
        LogPondFrameFollowsItsLayout();
        BulkTextsNameTheKind();
        LakeEditsApplyRepeatAndRestore();
        LakeEditsInScenePatchEditing();
        Console.WriteLine("PASS: lake surfaces");
    }

    // The log pond and both Fontana lakes are each listed once as a group: the log pond under Storage display, as the
    // category table names it, and the Fontana lakes under Other, which takes a listed group the table does not name.
    // The log pond's water mesh and its logs are its parts.
    static void InventoryListsLakeSurfacesWithTheirParts() {
        var verdicts = BakedSceneryRules.Classify(World());
        foreach (var path in new[] { LogPond, Fontana1, Fontana2 }) {
            var group = verdicts.Where(v => v.Path == path).ToArray();
            Assert(group.Length == 1 && group[0].Disposition == GroupDisposition.Group, path + " is listed once as a group: " + String.Join(", ", group.Select(g => g.Disposition + " " + g.Reason)));
        }
        Assert(At(verdicts, LogPond).Category == BakedSceneryCategories.StorageDisplay, "The log pond stands under Storage display: " + At(verdicts, LogPond).Category);
        Assert(At(verdicts, Fontana1).Category == BakedSceneryCategories.Other && At(verdicts, Fontana2).Category == BakedSceneryCategories.Other, "The Fontana lakes stand under Other");
        var parts = BakedSceneryRules.Parts(verdicts).Where(p => p.Disposition == PartDisposition.Part).ToArray();
        Assert(parts.Count(p => p.Path == Water) == 1 && parts.Count(p => p.Path == Logs) == 1, "The water mesh and the logs are each one part of the log pond: " + String.Join(", ", parts.Select(p => p.Path)));
        Assert(parts.Where(p => p.Path.StartsWith(LogPond + "/", StringComparison.Ordinal)).All(p => p.GroupPath == LogPond), "The log pond's parts belong to its group");
        Assert(!parts.Any(p => p.GroupPath == Fontana1 || p.GroupPath == Fontana2), "A Fontana lake is one object without parts");
        Assert(BakedSceneryRules.PartName(LogPond, Logs) == "Whittier Log Pond / LogPond", "The logs are named with their group");
        // The logs have no renderer or collider in the scene: they count as physical presence, as a storage pile does,
        // so a log pond root that holds only its logs is still a listed group.
        var bare = At(BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Whittier", new Node("Whittier Log Pond", new Node("LogPond").With("Scenery.LogPond")))))), LogPond);
        Assert(bare?.Disposition == GroupDisposition.Group, "The logs alone give the log pond physical presence: " + bare?.Disposition + " " + bare?.Reason);
    }

    // No exclusion keeps a group that holds a lake surface or a log pond out of the family any more.
    static void NoExclusionNamesALakeSurface() {
        Assert(!BakedSceneryRules.Exclusions.Any(e => e.Component == Lake || e.Component == "Scenery.LogPond" || e.Ticket == "#93"), "No exclusion names a lake surface or a log pond");
        var verdicts = BakedSceneryRules.Classify(World());
        Assert(!verdicts.Any(v => v.Disposition == GroupDisposition.InspectionOnly), "No lake is inspection only: " + String.Join(", ", verdicts.Where(v => v.Disposition == GroupDisposition.InspectionOnly).Select(v => v.Path)));
    }

    // The Object tab's Use in the game text is chosen from the observation's bindings alone. The log pond itself (the
    // LogPond part, a Scenery.LogPond) names the storage it shows and says that moving or disabling it does not change
    // that storage; the group, and a part above the log pond, say the same for themselves. The water mesh is a
    // decorative part of the storage display, and a Fontana lake is decorative. Piles keep their #98 texts.
    static void ObjectTabNamesTheLogPondStorage() {
        var storage = CommandBand.StorageName("Whittier Saw Mill", "Logs", "whittier-sawmill.logs", "logs");
        var ids = CommandBand.StorageBinding("whittier-sawmill.logs", "logs");
        var own = CommandBand.LogPondBindings(true, 0, ids);
        Assert(own.SequenceEqual(new[] { "Log pond=this object", ids }), "The log pond itself carries its own binding and its storage ids: " + String.Join(" | ", own));
        Assert(CommandBand.LogPondBindings(true, 0, null).SequenceEqual(new[] { "Log pond=this object" }),
            "A log pond no synchronizer names carries no storage binding, as a pile does");
        var holder = CommandBand.LogPondBindings(false, 1, ids);
        Assert(holder.SequenceEqual(new[] { "Log ponds below=1" }), "A group or part above a log pond carries only the count below it: " + String.Join(" | ", holder));
        Assert(CommandBand.LogPondBindings(false, 0, null).Count == 0, "An object without a log pond carries no log pond binding");

        var logs = CommandBand.UseInGame(true, BakedSceneryCategories.StorageDisplay, storage, own);
        Assert(logs == "This log pond shows the logs storage of Whittier Saw Mill. Moving or disabling the log pond does not change the storage, and the industry keeps working on its native track spans.",
            "The log pond names the storage and says moving it does not change it: " + logs);
        Assert(CommandBand.UseInGame(true, BakedSceneryCategories.StorageDisplay, null, CommandBand.LogPondBindings(true, 0, null)).StartsWith("This log pond shows the industry's storage.", StringComparison.Ordinal),
            "A log pond no synchronizer names still speaks of the log pond");
        var group = CommandBand.UseInGame(false, BakedSceneryCategories.StorageDisplay, storage, holder);
        Assert(group == "Decorative. Its log pond shows the logs storage of Whittier Saw Mill. Moving or disabling this group does not change the storage, and the industry keeps working on its native track spans.",
            "The log pond group names the storage and says moving or disabling the group does not change it: " + group);
        Assert(CommandBand.UseInGame(true, BakedSceneryCategories.StorageDisplay, null, holder) == "Decorative. Its log pond shows the industry's storage. Moving or disabling this part does not change the storage, and the industry keeps working on its native track spans.",
            "A part above a log pond speaks of itself");
        Assert(CommandBand.UseInGame(true, BakedSceneryCategories.StorageDisplay, null, new string[0]) == "Decorative. The industry works on its native track spans, so moving or disabling this part does not change loading or unloading.",
            "The water mesh is a decorative part of the storage display");
        Assert(CommandBand.UseInGame(false, BakedSceneryCategories.Other, null, new string[0]) == CommandBand.Decorative, "A Fontana lake is decorative");
        // Piles keep the texts of #98, chosen from the same bindings.
        Assert(CommandBand.UseInGame(false, BakedSceneryCategories.StorageDisplay, null, new[] { CommandBand.PilesBinding(2) }) == CommandBand.UseInGame(false, BakedSceneryCategories.StorageDisplay, null, true)
            && CommandBand.UseInGame(true, BakedSceneryCategories.StorageDisplay, "pulpwood storage of Sylva Paperboard", new[] { CommandBand.StorageBinding("a.b", "pulpwood") }) == CommandBand.UseInGame(true, BakedSceneryCategories.StorageDisplay, "pulpwood storage of Sylva Paperboard", false),
            "Piles keep their texts");
        // A lake surface's dependency names what moves with it and what stays native, and whether its water is part of
        // a static batch, which the game run reads from the inspection.
        Assert(CommandBand.LakeSurfaceDependency(false) == "Lake surface: the water surface moves with this object. Native lake masks and rivers shape and color the terrain and stay with the native map editor",
            "A lake surface's dependency: " + CommandBand.LakeSurfaceDependency(false));
        Assert(CommandBand.LakeSurfaceDependency(true).StartsWith("Lake surface: the water surface is part of a static batch, so it cannot be moved.", StringComparison.Ordinal),
            "A batched lake surface's dependency says it cannot be moved: " + CommandBand.LakeSurfaceDependency(true));
    }

    // A lake surface whose water mesh is part of a static batch would stay drawn at its old place while its transform
    // moves, and every transform check would pass. A move is refused before any mutation with the reason; an activity
    // edit still applies.
    static void StaticBatchRefusesAMove() {
        var reason = BakedSceneryRules.StaticBatchRefusal("Fontana Lake (1)");
        Assert(reason == "The water surface Fontana Lake (1) is part of a static batch, so the game would keep drawing it at its old place. This lake surface cannot be moved",
            "The refusal names the water surface and why: " + reason);
        var f = new Lakes(); f.World.PlacementRefused[f.Fontana] = reason;
        var outcome = f.Engine.Apply(Doc(GroupMod("move-fontana", Fontana1, FontanaTarget))).Outcomes["move-fontana"];
        Assert(!outcome.Applied && outcome.Failure == FailureKind.Refused && outcome.Error.Contains("static batch") && f.World.Mutations == 0 && Near(f.At(f.Fontana), FontanaAuthored),
            "A batched lake move is refused before mutation: " + outcome.Failure + " " + outcome.Error);
        var hide = f.Engine.Apply(Doc(GroupMod("hide-fontana", Fontana1, active: false))).Outcomes["hide-fontana"];
        Assert(hide.Applied && !f.Fontana.Active, "Disabling a batched lake still applies: " + hide.Error);
        var part = new Lakes(); part.World.PlacementRefused[part.WaterMesh] = BakedSceneryRules.StaticBatchRefusal("Whittier Saw Mill Log Pond");
        outcome = part.Engine.Apply(Doc(Mod("move-water", Water, BakedSceneryParts.Structure, LogsTarget))).Outcomes["move-water"];
        Assert(!outcome.Applied && outcome.Failure == FailureKind.Refused && part.World.Mutations == 0, "A batched water part move is refused before mutation: " + outcome.Error);
        var session = new Lakes(); session.World.PlacementRefused[session.Fontana] = reason;
        var store = new Store(); var mapEditor = new NativeMapEditor(); var editing = new PatchEditingSession(session.Engine, mapEditor, store); mapEditor.Session = editing;
        var edit = editing.Edit(GroupId(Fontana1), new Placement(FontanaTarget, Upright), null);
        Assert(!edit.Applied && session.World.Mutations == 0 && !mapEditor.IsDirty, "In scene patch editing the move is refused and nothing is outstanding: " + edit.Error);
        // A gizmo drag sets the placement itself; the gesture is refused when it starts, before any preview moves the
        // object. The inspectable group of the part tests stands in for a batched lake surface.
        var g = new BakedPartTests.Fixture(); g.World.PlacementRefused[g.Group] = reason;
        var gStore = new Store(); var gEditor = new NativeMapEditor(); var gSession = new PatchEditingSession(g.Engine, gEditor, gStore); gEditor.Session = gSession;
        var inventory = new SceneInventory(g.Scene); inventory.Refresh();
        string refused = null;
        try { new PlacementGesture(g.Engine, gSession, new TargetIdentity(SceneName, "World/Large Scenery/Whittier/Whittier Saw Mill", BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure),
            inventory.Pin(inventory.Entries.Single(e => ReferenceEquals(e.Handle, g.Group)))); }
        catch (InvalidOperationException e) { refused = e.Message; }
        Assert(refused != null && refused.Contains("static batch") && g.World.Mutations == 0, "A gizmo gesture on a batched lake is refused before it moves anything: " + refused);
    }

    // The log pond's logs have no renderer: the world frames them with the box of the pond they lay out in, its width
    // across and its length plus width along, one log's radius up and down from the water line, as #98 frames a pile.
    static void LogPondFrameFollowsItsLayout() {
        var box = BakedSceneryRules.LogPondBox(4, 8, 0.25);
        Assert(box.Item1.SequenceEqual(new[] { 0.0, 0, 0 }) && box.Item2.SequenceEqual(new[] { 2.0, 0.25, 6 }), "The log pond box: centre " + N(box.Item1) + " half " + N(box.Item2));
        var round = BakedSceneryRules.LogPondBox(10, 0, 0.3);
        Assert(round.Item2.SequenceEqual(new[] { 5.0, 0.3, 5 }), "A pond without a straight section is as long as it is wide: " + N(round.Item2));
    }

    // Refusal and failure texts of the bulk scenery refresh name the object kind: a log pond is no storage pile.
    static void BulkTextsNameTheKind() {
        Assert(BakedSceneryRules.BulkKind("Scenery.LogPond") == "log pond" && BakedSceneryRules.BulkKind("Scenery.Pile") == "storage pile", "The kind of a bulk scenery object");
        Assert(BakedSceneryRules.BulkMembersRefusal(true, false) == "The game's storage pile members are unavailable, so this group and its piles cannot be moved, enabled or disabled", "Piles keep the #98 text");
        Assert(BakedSceneryRules.BulkMembersRefusal(false, true) == "The game's log pond members are unavailable, so this group and its log pond cannot be moved, enabled or disabled",
            "A log pond group names the log pond: " + BakedSceneryRules.BulkMembersRefusal(false, true));
        Assert(BakedSceneryRules.BulkMembersRefusal(true, true) == "The game's storage pile and log pond members are unavailable, so this group, its piles and its log pond cannot be moved, enabled or disabled",
            "Both kinds are named");
    }

    // The log pond and a Fontana lake as the group and part capabilities see them: the log pond with its water mesh
    // and logs below the root, a Fontana lake with a renderer culler on its root and no parts, and the rule's
    // verdicts deciding which paths resolve.
    const string SceneName = "BushnellWhittier-MapManager";
    sealed class Lakes {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeGroup Pond = new FakeGroup { Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakeGroup Fontana = new FakeGroup { Position = new[] { -400.0, 5, 300 }, Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakeCuller FontanaCuller = new FakeCuller { Id = "RendererCuller#7", OnRoot = true, Simple = false, Radius = 150, Authored = 150 };
        public readonly FakePart WaterMesh, LogsPart;
        public readonly PatchEngine Engine;
        public SceneCapabilities Scene;
        public Lakes() {
            WaterMesh = Pond.Add(new FakePart { Id = "Whittier Saw Mill Log Pond#1", Offset = new[] { 284.0, -3, -870 } });
            LogsPart = Pond.Add(new FakePart { Id = "LogPond#2", Offset = new[] { 257.0, -3, -904 } });
            Fontana.Cullers.Add(FontanaCuller);
            var verdicts = BakedSceneryRules.Classify(LakeSurfaceTests.World());
            World.GroupVerdicts = verdicts.ToDictionary(v => v.Path, v => v);
            World.Verdicts = BakedSceneryRules.Parts(verdicts).ToDictionary(v => v.Path, v => v);
            World.Objects[LogPond] = new List<object> { Pond }; World.Objects[Fontana1] = new List<object> { Fontana };
            World.PartsAt[Water] = new List<object> { WaterMesh }; World.PartsAt[Logs] = new List<object> { LogsPart };
            World.SetActive(Pond, true); World.SetActive(Fontana, true); World.Mutations = 0;
            var holds = new PartHolds();
            var scene = Scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new BakedSceneryGroups(World, holds: holds)); scene.Add(new BakedSceneryParts(World, holds));
            Engine = new PatchEngine(scene);
        }
        public double[] At(FakePart part) => World.ObservePart(part).Part.Placement.Position.ToArray();
        public double[] At(FakeGroup group) => World.Observe(group).Root.Placement.Position.ToArray();
        // Where the Fontana lake's renderer culler holds its sphere, in map coordinates, or null while it holds none.
        public double[] Culls() => FontanaCuller.Registered == null ? null : new[] { FontanaCuller.Registered[0] + World.Origin, FontanaCuller.Registered[1], FontanaCuller.Registered[2] };
        public string Where() => "pond=" + N(At(Pond)) + "; water=" + N(At(WaterMesh)) + "; logs=" + N(At(LogsPart)) + "; fontana=" + N(At(Fontana)) + "; culls=" + (Culls() == null ? "none" : N(Culls()));
    }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) => a != null && b != null && a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= 0.001);
    static string Mod(string id, string path, string structure, double[] position = null, bool? active = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", position.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[0,0,0,1]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string GroupMod(string id, string path, double[] position = null, bool? active = null) => Mod(id, path, BakedSceneryGroups.Structure, position, active);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static TargetIdentity GroupId(string path) => new TargetIdentity(SceneName, path, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
    static TargetIdentity PartId(string path) => new TargetIdentity(SceneName, path, BakedSceneryParts.ComponentType, BakedSceneryParts.Structure);
    static readonly double[] Upright = { 0, 0, 0, 1 }, PondTarget = { 1350, 10, 60 }, FontanaTarget = { 700, 5, 420 }, LogsTarget = { 1300, 7, 0 };
    static readonly double[] PondAuthored = { 1100, 10, 20 }, FontanaAuthored = { 600, 5, 300 }, LogsAuthored = { 1357, 7, -884 };

    // The log pond moves with its water mesh and logs, and a Fontana lake moves with its culling sphere; the logs move
    // and disable as a part; a repeated application and a load at another origin reach the same placements; a refusal
    // mutates nothing; a failure after mutation restores placement and activity; an incomplete cleanup blocks the retry.
    static void LakeEditsApplyRepeatAndRestore() {
        var f = new Lakes();
        Assert(Near(f.Culls(), FontanaAuthored), "The unedited lake culls at its authored place: " + f.Where());
        var doc = Doc(GroupMod("move-pond", LogPond, PondTarget), GroupMod("move-fontana", Fontana1, FontanaTarget));
        var result = f.Engine.Apply(doc);
        Assert(result.Outcomes.Values.All(o => o.Applied), "The lake modifications apply: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        Assert(Near(f.At(f.Pond), PondTarget) && Near(f.At(f.WaterMesh), new[] { 1634.0, 7, -810 }) && Near(f.At(f.LogsPart), new[] { 1607.0, 7, -844 }),
            "The water mesh and the logs move with the log pond: " + f.Where());
        Assert(Near(f.At(f.Fontana), FontanaTarget) && Near(f.Culls(), FontanaTarget), "The moved lake culls at its new place: " + f.Where());
        var first = f.Where();
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && f.Where() == first, "A repeated application accumulates no offset: " + f.Where());
        var shifted = new Lakes(); shifted.World.Origin = 1010;
        Assert(shifted.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && N(shifted.At(shifted.Pond)) == N(PondTarget) && N(shifted.At(shifted.Fontana)) == N(FontanaTarget),
            "A load at another origin reaches the same map placements: " + shifted.Where());
        var hidden = new Lakes();
        Assert(hidden.Engine.Apply(Doc(GroupMod("hide-fontana", Fontana1, active: false), Mod("move-logs", Logs, BakedSceneryParts.Structure, LogsTarget, false))).Outcomes.Values.All(o => o.Applied)
            && !hidden.Fontana.Active && hidden.Culls() == null && !hidden.LogsPart.Active && Near(hidden.At(hidden.LogsPart), LogsTarget) && Near(hidden.At(hidden.WaterMesh), new[] { 1384.0, 7, -850 }),
            "A disabled lake holds no culling sphere, and the logs move and disable on their own: " + hidden.Where());
        // The log pond's root and its logs resolve only as what the rule lists them as.
        var wrong = new Lakes().Engine.Apply(Doc(Mod("root-as-part", LogPond, BakedSceneryParts.Structure, PondTarget), Mod("logs-as-group", Logs, BakedSceneryGroups.Structure, PondTarget)));
        Assert(wrong.Outcomes.Values.All(o => !o.Applied), "A lake root does not resolve as a part, nor its logs as a group");

        var refused = new Lakes(); refused.World.Refused = "The group's scene is not loaded";
        var outcome = refused.Engine.Apply(Doc(GroupMod("move-fontana", Fontana1, FontanaTarget))).Outcomes["move-fontana"];
        Assert(!outcome.Applied && refused.World.Mutations == 0 && Near(refused.At(refused.Fontana), FontanaAuthored) && Near(refused.Culls(), FontanaAuthored), "A preflight refusal leaves the lake untouched: " + outcome.Error);

        var failed = new Lakes(); failed.World.ThrowAfterPlacement = true;
        outcome = failed.Engine.Apply(Doc(GroupMod("move-pond", LogPond, PondTarget, false))).Outcomes["move-pond"];
        Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal) && Near(failed.At(failed.Pond), PondAuthored) && failed.Pond.Active
            && Near(failed.At(failed.LogsPart), LogsAuthored), "A failure after mutation restores the log pond's placement and activity: " + outcome.Error + " " + failed.Where());
        var failedPart = new Lakes(); failedPart.World.ThrowAfterPartPlacement = true;
        outcome = failedPart.Engine.Apply(Doc(Mod("move-logs", Logs, BakedSceneryParts.Structure, LogsTarget, false))).Outcomes["move-logs"];
        Assert(!outcome.Applied && Near(failedPart.At(failedPart.LogsPart), LogsAuthored) && failedPart.LogsPart.Active, "A failure after mutation restores the logs' placement and activity: " + outcome.Error);

        var stuck = new Lakes(); stuck.World.LeavePartBehind = stuck.World.KeepPartBehind = true;
        outcome = stuck.Engine.Apply(Doc(GroupMod("move-pond", LogPond, PondTarget))).Outcomes["move-pond"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        stuck.World.KeepPartBehind = false; var mutations = stuck.World.Mutations;
        var retry = stuck.Engine.Apply(Doc(GroupMod("again", LogPond, PondTarget))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && stuck.World.Mutations == mutations, "The retry on the affected log pond is blocked: " + retry.Error);
    }

    // In scene patch editing: Undo and Redo of lake edits with matching dirty state and Save availability, Remove
    // modification returning the effective state from the remaining values, Save at format version 1 with the patcher
    // dependency, a stricter constraint kept, and a reload that reproduces the moves without duplicated state.
    static void LakeEditsInScenePatchEditing() {
        var f = new Lakes(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Assert(session.Edit(PartId(Logs), new Placement(LogsTarget, Upright), null).Applied && session.Edit(GroupId(LogPond), new Placement(PondTarget, Upright), null).Applied
            && session.Edit(GroupId(Fontana1), new Placement(FontanaTarget, Upright), null).Applied, "A logs move, a log pond move and a Fontana lake move apply");
        Persistence(true, all, "After three edits");
        mapEditor.Undo(); Assert(Near(f.At(f.Fontana), FontanaAuthored) && Near(f.Culls(), FontanaAuthored), "Undo returns the lake with its culling: " + f.Where());
        mapEditor.Undo(); Assert(Near(f.At(f.Pond), PondAuthored) && Near(f.At(f.LogsPart), LogsTarget), "Undo returns the log pond; the moved logs stay: " + f.Where());
        mapEditor.Undo(); Assert(Near(f.At(f.LogsPart), LogsAuthored), "Undo returns the logs: " + f.Where()); Persistence(false, new string[0], "After Undo back to the unedited lakes");
        mapEditor.Redo(); mapEditor.Redo(); mapEditor.Redo();
        Assert(Near(f.At(f.LogsPart), LogsTarget) && Near(f.At(f.Pond), PondTarget) && Near(f.At(f.Fontana), FontanaTarget) && Near(f.Culls(), FontanaTarget), "Redo reapplies every edit: " + f.Where());
        Persistence(true, all, "After Redo");
        var removed = session.Remove(PartId(Logs));
        Assert(removed.Applied && Near(f.At(f.LogsPart), new[] { 1607.0, 7, -844 }), "Remove modification returns the logs to their place in the moved log pond: " + removed.Error + " " + f.Where());
        mapEditor.Undo(); Assert(Near(f.At(f.LogsPart), LogsTarget), "Undo of the removal holds the logs again");
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=" + BakedSceneryGroups.RequiredVersion) && (int)JObject.Parse(store.Patch)["formatVersion"] == 1,
            "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        var fresh = new Lakes();
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Count == 3 && reload.Outcomes.Values.All(o => o.Applied) && Near(fresh.At(fresh.LogsPart), LogsTarget) && Near(fresh.At(fresh.Fontana), FontanaTarget)
            && Near(fresh.Culls(), FontanaTarget) && fresh.Pond.Parts.Count == 2 && fresh.Fontana.Cullers.Count == 1, "A reloaded world reproduces the moves without duplicated state: " + fresh.Where());
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = new Lakes(); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(other.Engine, strictEditor, strict); strictEditor.Session = kept;
        Assert(kept.Edit(GroupId(Fontana1), new Placement(FontanaTarget, Upright), null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=" + BakedSceneryGroups.RequiredVersion),
            "A stricter existing constraint is retained: " + strict.Info);
    }
}
