// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The Robinson Gap Mine Coal Loader: the group joins the Baked scenery group family with its parts, and the Object tab
// names the object that holds the car loader as the loading point. The controlled hierarchy is the recorded Railroader
// 0.6.3 scene (Records/InventoryLedger/level4_mb_rows.json, Records/InventoryCoverage/renderer_coverage.csv): the car
// loader (RollingStock.CarContentSwapper) and its key-value objects stand on the group root; a simple culler on Model
// holds MineLoader, whose KeyValueBoolAnimator animates it and its 15 renderers, and Spotlight Outdoor.
static class CoalLoaderTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Coal loader: " + message); }
    internal const string Loader = "World/Large Scenery/Robinson Gap Mine/Coal Loader";
    static readonly string[] MineLoaderRenderers = { "Chute", "Cube", "Cube.001", "Cube.002", "Cube.003", "Cube.004", "Cube.005", "Cube.006", "Cube.007", "Cube.008",
        "Post", "Post.001", "Post.002", "Post.003", "Post.004" };
    // Nothing marks a node as animated here: the part rule must find the animator itself, as the game's adapter does.
    internal static Node World() => new Node("World", new Node("Large Scenery", new Node("Robinson Gap Mine",
        new Node("Coal Loader",
            new Node("Model",
                new Node("MineLoader", MineLoaderRenderers.Select(name => new Node(name).Solid()).ToArray()).With("UnityEngine.Animator", "RollingStock.Controls.KeyValueBoolAnimator"),
                new Node("Spotlight Outdoor").With("UnityEngine.Light", "Effects.NightLightController", "UnityEngine.Rendering.Universal.UniversalAdditionalLightData").Solid()).With(BakedSceneryRules.SimpleCuller))
            .With("RollingStock.CarContentSwapper", "KeyValue.Runtime.KeyValueObject", "RollingStock.Controls.GlobalKeyValueObject"))));
    static GroupRootVerdict At(IReadOnlyList<GroupRootVerdict> verdicts, string path) => verdicts.SingleOrDefault(v => v.Path == path);

    public static void Run() {
        InventoryListsTheLoaderGroupWithItsParts();
        ObjectTabNamesTheLoadingPoint();
        LoaderEditsApplyRepeatAndRestore();
        LoaderEditsInScenePatchEditing();
        Console.WriteLine("PASS: coal loader");
    }

    // The loader group is a listed group under Industry building. Spotlight Outdoor is its only part: Model is a
    // culling container, and MineLoader, which the game animates, moves with its group, with every renderer below it.
    static void InventoryListsTheLoaderGroupWithItsParts() {
        var verdicts = BakedSceneryRules.Classify(World());
        var group = At(verdicts, Loader);
        Assert(group?.Disposition == GroupDisposition.Group && group.Category == BakedSceneryCategories.IndustryBuilding && group.Ticket == null,
            "The loader is a listed group under Industry building: " + group?.Disposition + " " + group?.Reason + " " + group?.Category);
        var all = BakedSceneryRules.Parts(verdicts);
        var parts = all.ToDictionary(p => p.Path, p => p);
        var listed = all.Where(p => p.Disposition == PartDisposition.Part).Select(p => p.Path).ToArray();
        Assert(listed.SequenceEqual(new[] { Loader + "/Model/Spotlight Outdoor" }), "Spotlight Outdoor is the only part: " + String.Join(", ", listed));
        Assert(parts[Loader + "/Model"].Reason == BakedSceneryRules.CullingContainers, "Model is a culling container, no part");
        Assert(parts[Loader + "/Model/MineLoader"].Reason == BakedSceneryRules.AnimatedParts, "The animated MineLoader is no part: " + parts[Loader + "/Model/MineLoader"].Reason);
        foreach (var name in MineLoaderRenderers)
            Assert(parts[Loader + "/Model/MineLoader/" + name].Disposition == PartDisposition.NotListed && parts[Loader + "/Model/MineLoader/" + name].Reason == BakedSceneryRules.AnimatedParts,
                name + " below the animated MineLoader is no part");
        Assert(BakedSceneryRules.Animates(new[] { "UnityEngine.Animator", "RollingStock.Controls.KeyValueBoolAnimator" }) && !BakedSceneryRules.Animates(new[] { "UnityEngine.Animator" })
            && !BakedSceneryRules.Animates(new[] { "UnityEngine.Light" }) && !BakedSceneryRules.Animates(null),
            "A key-value animator marks its object as animated; the game's adapter reads the same list");
    }

    // The object that holds the car loader carries the load it fills as a binding, the id the game reads. Its Object tab
    // names it as the loading point; the loader's other parts say that moving them does not change where cars load. A
    // decorative industry building without a loader keeps its own text.
    static void ObjectTabNamesTheLoadingPoint() {
        var binding = CommandBand.LoaderBinding("coal");
        Assert(binding == "Car loader=load coal" && CommandBand.LoaderBinding(null) == "Car loader=no load", "The binding names the load id the loader fills: " + binding);
        Assert(CommandBand.HoldsLoader(new[] { "Other=1", binding }) && !CommandBand.HoldsLoader(new[] { "Other=1", CommandBand.PilesBinding(1) }) && !CommandBand.HoldsLoader(null),
            "Whether an object holds the car loader travels as a binding");
        var root = CommandBand.UseInGame(false, BakedSceneryCategories.IndustryBuilding, null, false, loadingPoint: true);
        Assert(root == "Loading point: cars under it are loaded.", "The object that holds the loader names the loading point: " + root);
        var part = CommandBand.UseInGame(true, BakedSceneryCategories.IndustryBuilding, null, false, groupLoads: true);
        Assert(part == "Decorative. Cars are loaded under the group's loading point, so moving or disabling this part does not change loading.",
            "A part of the loader group says that it does not change loading: " + part);
        Assert(CommandBand.UseInGame(true, BakedSceneryCategories.IndustryBuilding, null, false) == "Decorative. The industry works on its native track spans, so moving or disabling this part does not change loading or unloading.",
            "A part of another industry building keeps its text");
        BindingReadsTheLoadTheLoaderFills();
        ObjectTabReadsTheGroupBinding();
    }

    // The car loader as the game serializes it: the load it fills in outputContent.loadId.
    struct Content { public string loadId; public string description; }
    sealed class Swapper { public Content inputContent; public Content outputContent; public string loadingBoolKey; }
    sealed class NoContent { public string loadingBoolKey = "loading"; }
    // The binding comes from the loader's own serialized output; nothing else is read.
    static void BindingReadsTheLoadTheLoaderFills() {
        var coal = new Swapper { inputContent = new Content { loadId = "ignored" }, outputContent = new Content { loadId = "coal", description = "Coal" }, loadingBoolKey = "loading" };
        Assert(CommandBand.LoaderBindingOf(coal) == "Car loader=load coal", "The binding reads outputContent.loadId: " + CommandBand.LoaderBindingOf(coal));
        Assert(CommandBand.LoaderBindingOf(new Swapper()) == "Car loader=no load", "An empty output reads no load: " + CommandBand.LoaderBindingOf(new Swapper()));
        var missing = false;
        try { CommandBand.LoaderBindingOf(new NoContent()); } catch (MissingFieldException) { missing = true; }
        Assert(missing, "A loader without the output field fails the readout instead of reading no load");
    }
    // The Object tab takes the loading point from the selected object's bindings and, for a part, the loader from its
    // group's bindings, as the inspector passes them.
    static void ObjectTabReadsTheGroupBinding() {
        SceneObservation Entry(string path, string groupPath, string category, params string[] bindings) =>
            new SceneObservation(new object(), new TargetIdentity("s", path, BakedSceneryGroups.ComponentType, groupPath == null ? BakedSceneryGroups.Structure : BakedSceneryParts.Structure),
                BakedSceneryRules.Family, true, new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, bindings, new string[0], new string[0], "edit", groupPath: groupPath, category: category);
        var root = Entry(Loader, null, BakedSceneryCategories.IndustryBuilding, CommandBand.LoaderBinding("coal"));
        var light = Entry(Loader + "/Model/Spotlight Outdoor", Loader, null);
        Assert(CommandBand.UseInGame(root, null) == CommandBand.LoadingPoint, "The loader root reads the loading point: " + CommandBand.UseInGame(root, null));
        Assert(CommandBand.UseInGame(light, root) == "Decorative. Cars are loaded under the group's loading point, so moving or disabling this part does not change loading.",
            "Its part reads the group's loader: " + CommandBand.UseInGame(light, root));
        var dealer = Entry("World/Large Scenery/Bryson/Bryson Coal Dealer", null, BakedSceneryCategories.IndustryBuilding);
        var bin = Entry("World/Large Scenery/Bryson/Bryson Coal Dealer/Coal Bin", "World/Large Scenery/Bryson/Bryson Coal Dealer", null);
        Assert(CommandBand.UseInGame(bin, dealer) == "Decorative. The industry works on its native track spans, so moving or disabling this part does not change loading or unloading.",
            "A part of a group without a loader keeps the industry text: " + CommandBand.UseInGame(bin, dealer));
        Assert(CommandBand.UseInGame(bin, null) == CommandBand.PartDecorative, "A part whose group is not found reads the plain part text");
    }

    // The loader group as the group and part capabilities see it: the loading point is the group root, Spotlight Outdoor
    // is its one part inside the culling container, and the rule's verdicts decide which paths resolve.
    const string SceneName = "BushnellWhittier-MapManager";
    sealed class Mine {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeGroup Group = new FakeGroup { Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakePart Spotlight;
        public readonly PatchEngine Engine;
        public Mine() {
            Group.Position = new[] { 317.85, 602.86, 2188.55 };
            Spotlight = Group.Add(new FakePart { Id = "Spotlight Outdoor#1", Offset = new[] { 2.0, 8, 0 } });
            // MineLoader, which the game animates: observed with its group, never resolved as a part.
            Group.Add(new FakePart { Id = "MineLoader#2", Offset = new[] { 1.7, 4, 3 }, Animated = true });
            var verdicts = BakedSceneryRules.Classify(CoalLoaderTests.World());
            World.GroupVerdicts = verdicts.ToDictionary(v => v.Path, v => v);
            World.Verdicts = BakedSceneryRules.Parts(verdicts).ToDictionary(v => v.Path, v => v);
            World.Objects[Loader] = new List<object> { Group };
            World.PartsAt[Loader + "/Model/Spotlight Outdoor"] = new List<object> { Spotlight };
            World.SetActive(Group, true); World.Mutations = 0;
            var holds = new PartHolds();
            var scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new BakedSceneryGroups(World, holds: holds)); scene.Add(new BakedSceneryParts(World, holds));
            Engine = new PatchEngine(scene);
        }
        // The loading point in map coordinates: where the group root stands.
        public double[] LoadingPoint => World.Observe(Group).Root.Placement.Position.ToArray();
        public double[] At(FakePart part) => World.ObservePart(part).Part.Placement.Position.ToArray();
        public string Where() => "loading point " + N(LoadingPoint) + "; " + String.Join("; ", Group.Parts.Select(p => p.Id + "=" + N(At(p)) + (p.Active ? "" : " off")));
    }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) => a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= 0.001);
    static string Mod(string id, string path, string structure, double[] position = null, bool? active = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", position.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[0,0,0,1]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string LoaderMod(string id, double[] position = null, bool? active = null) => Mod(id, Loader, BakedSceneryGroups.Structure, position, active);
    static string PartMod(string id, string name, double[] position = null, bool? active = null) => Mod(id, Loader + "/Model/" + name, BakedSceneryParts.Structure, position, active);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static readonly TargetIdentity LoaderId = new TargetIdentity(SceneName, Loader, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
    static TargetIdentity PartId(string name) => new TargetIdentity(SceneName, Loader + "/Model/" + name, BakedSceneryParts.ComponentType, BakedSceneryParts.Structure);
    // The authored loading point at Robinson Gap Mine in map coordinates, a place over natively moved track and one
    // off every track.
    static readonly double[] Upright = { 0, 0, 0, 1 }, Authored = { 1317.85, 602.86, 2188.55 }, OverMovedTrack = { 1337.85, 602.86, 2178.55 }, OffTrack = { 1400, 610, 2300 };

    // A loader move carries the loading point and every part; a repeated application and a load at another origin reach
    // the same placements; a part edit leaves the loading point where it is; a refusal mutates nothing; a failure after
    // mutation restores the placement and activity; an incomplete cleanup blocks the retry.
    static void LoaderEditsApplyRepeatAndRestore() {
        var f = new Mine();
        Assert(Near(f.LoadingPoint, Authored), "The controlled loader stands at its authored place: " + f.Where());
        var doc = Doc(LoaderMod("move-loader", OverMovedTrack));
        var result = f.Engine.Apply(doc);
        Assert(result.Outcomes.Values.All(o => o.Applied), "The loader move applies: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        Assert(Near(f.LoadingPoint, OverMovedTrack) && Near(f.At(f.Spotlight), new[] { 1339.85, 610.86, 2178.55 }),
            "The loading point and the spotlight move together: " + f.Where());
        var first = f.Where();
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && f.Where() == first, "A repeated application accumulates no offset: " + f.Where());
        var shifted = new Mine(); shifted.World.Origin = 1010;
        Assert(shifted.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && shifted.Where() == first, "A load at another origin reaches the same map placements: " + shifted.Where());
        // Off every track the move applies as anywhere else: the patcher does not decide where cars stand.
        var off = new Mine();
        Assert(off.Engine.Apply(Doc(LoaderMod("off", OffTrack))).Outcomes["off"].Applied && Near(off.LoadingPoint, OffTrack), "A move off every track applies: " + off.Where());
        // A part moves alone; the loading point stays with the group root.
        var part = new Mine();
        var moved = part.Engine.Apply(Doc(PartMod("move-light", "Spotlight Outdoor", OffTrack, false)));
        Assert(moved.Outcomes.Values.All(o => o.Applied) && Near(part.At(part.Spotlight), OffTrack) && !part.Spotlight.Active && Near(part.LoadingPoint, Authored),
            "A part edit leaves the loading point in place: " + part.Where() + " " + String.Join("; ", moved.Outcomes.Values.Select(o => o.Error)));
        var wrong = new Mine().Engine.Apply(Doc(Mod("root-as-part", Loader, BakedSceneryParts.Structure, OffTrack), Mod("model-as-part", Loader + "/Model", BakedSceneryParts.Structure, OffTrack),
            Mod("mineloader", Loader + "/Model/MineLoader", BakedSceneryParts.Structure, OffTrack), Mod("chute", Loader + "/Model/MineLoader/Chute", BakedSceneryParts.Structure, OffTrack)));
        Assert(wrong.Outcomes.Values.All(o => !o.Applied), "The loader root, its culling container, the animated MineLoader and its chute do not resolve as parts: " +
            String.Join("; ", wrong.Outcomes.Select(o => o.Key + " " + o.Value.Applied)));

        var refused = new Mine(); refused.World.Refused = "The group's scene is not loaded";
        var outcome = refused.Engine.Apply(Doc(LoaderMod("move-loader", OverMovedTrack))).Outcomes["move-loader"];
        Assert(!outcome.Applied && refused.World.Mutations == 0 && Near(refused.LoadingPoint, Authored) && refused.Group.Active, "A preflight refusal leaves the loader untouched: " + outcome.Error);

        foreach (var inject in new Action<FakeGroupWorld>[] { w => w.ThrowAfterPlacement = true, w => w.LeavePartBehind = true }) {
            var failed = new Mine(); inject(failed.World);
            outcome = failed.Engine.Apply(Doc(LoaderMod("move-loader", OverMovedTrack, false))).Outcomes["move-loader"];
            Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal) && Near(failed.LoadingPoint, Authored) && failed.Group.Active
                && Near(failed.At(failed.Spotlight), new[] { 1319.85, 610.86, 2188.55 }), "A failure after mutation restores the loader's placement and activity: " + outcome.Error + " " + failed.Where());
        }

        var stuck = new Mine(); stuck.World.LeavePartBehind = stuck.World.KeepPartBehind = true;
        outcome = stuck.Engine.Apply(Doc(LoaderMod("move-loader", OverMovedTrack))).Outcomes["move-loader"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        stuck.World.LeavePartBehind = stuck.World.KeepPartBehind = false; var mutations = stuck.World.Mutations;
        var retry = stuck.Engine.Apply(Doc(LoaderMod("again", OffTrack))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && stuck.World.Mutations == mutations, "The retry on the affected loader is blocked: " + retry.Error);
    }

    // In scene patch editing: Undo and Redo of a loader move and a part edit with matching dirty state and Save
    // availability, Remove modification returning the loader to its authored place, Save at format version 1 with the
    // patcher dependency, a stricter constraint kept, and a reload that reproduces the move without duplicated state.
    static void LoaderEditsInScenePatchEditing() {
        var f = new Mine(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Assert(session.Edit(LoaderId, new Placement(OverMovedTrack, Upright), null).Applied && session.Edit(PartId("Spotlight Outdoor"), null, false).Applied, "A loader move and a part disable apply");
        Persistence(true, all, "After two edits");
        mapEditor.Undo(); Assert(f.Spotlight.Active && Near(f.LoadingPoint, OverMovedTrack), "Undo enables the spotlight; the loader stays moved: " + f.Where());
        mapEditor.Undo(); Assert(Near(f.LoadingPoint, Authored) && Near(f.At(f.Spotlight), new[] { 1319.85, 610.86, 2188.55 }), "Undo returns the loader and its part: " + f.Where());
        Persistence(false, new string[0], "After Undo back to the unedited loader");
        mapEditor.Redo(); mapEditor.Redo();
        Assert(Near(f.LoadingPoint, OverMovedTrack) && !f.Spotlight.Active && Near(f.At(f.Spotlight), new[] { 1339.85, 610.86, 2178.55 }), "Redo reapplies every edit: " + f.Where()); Persistence(true, all, "After Redo");
        var removed = session.Remove(LoaderId);
        Assert(removed.Applied && Near(f.LoadingPoint, Authored) && !f.Spotlight.Active, "Remove modification returns the loader to its authored place and keeps the part edit: " + removed.Error + " " + f.Where());
        mapEditor.Undo(); Assert(Near(f.LoadingPoint, OverMovedTrack), "Undo of the removal moves the loader again: " + f.Where());
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0") && (int)JObject.Parse(store.Patch)["formatVersion"] == 1, "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Persistence(false, new string[0], "After Save");
        var fresh = new Mine();
        var reload = fresh.Engine.Apply(store.Patch);
        var saved = (JObject)JObject.Parse(store.Patch)["modifications"];
        Assert(saved.Count == 2 && reload.Outcomes.Count == 2 && reload.Outcomes.Values.All(o => o.Applied) && Near(fresh.LoadingPoint, OverMovedTrack) && !fresh.Spotlight.Active,
            "A reloaded world reproduces the move from one modification per target: " + fresh.Where() + " " + saved);
        var once = fresh.Where();
        Assert(fresh.Engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && fresh.Where() == once, "Applying the saved file again adds no offset: " + fresh.Where());
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = new Mine(); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(other.Engine, strictEditor, strict); strictEditor.Session = kept;
        Assert(kept.Edit(LoaderId, new Placement(OverMovedTrack, Upright), null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=0.5.0"), "A stricter existing constraint is retained: " + strict.Info);
    }
}
