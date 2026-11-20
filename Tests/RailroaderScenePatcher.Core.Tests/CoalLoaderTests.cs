// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The Robinson Gap Mine Coal Loader stays out of the inventory as a settled rule without a ticket, under the coverage
// statement's Car loaders rule. In Railroader 0.6.3 the base map loads Robinson Gap coal by teleport between the spans
// of its industry (a TeleportLoadingIndustry in Map.json), not at the loader; a confirming game run left an empty hopper
// under the unedited loader for 24 game hours, and no component references the loader, so moving it would not move
// loading. The controlled hierarchy is the recorded Railroader 0.6.3 scene (Records/InventoryLedger/
// level4_mb_rows.json, Records/InventoryCoverage/renderer_coverage.csv): the car loader
// (RollingStock.CarContentSwapper) and its key-value objects stand on the group root; a simple culler on Model holds
// MineLoader, whose KeyValueBoolAnimator animates it and its 15 renderers, and Spotlight Outdoor.
static class CoalLoaderTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Coal loader: " + message); }
    internal const string Loader = "World/Large Scenery/Robinson Gap Mine/Coal Loader";
    static readonly string[] MineLoaderRenderers = { "Chute", "Cube", "Cube.001", "Cube.002", "Cube.003", "Cube.004", "Cube.005", "Cube.006", "Cube.007", "Cube.008",
        "Post", "Post.001", "Post.002", "Post.003", "Post.004" };
    internal static Node World() => new Node("World", new Node("Large Scenery", new Node("Robinson Gap Mine",
        new Node("Coal Loader",
            new Node("Model",
                new Node("MineLoader", MineLoaderRenderers.Select(name => new Node(name).Solid()).ToArray()).With("UnityEngine.Animator", "RollingStock.Controls.KeyValueBoolAnimator"),
                new Node("Spotlight Outdoor").With("UnityEngine.Light", "Effects.NightLightController", "UnityEngine.Rendering.Universal.UniversalAdditionalLightData").Solid()).With(BakedSceneryRules.SimpleCuller))
            .With("RollingStock.CarContentSwapper", "KeyValue.Runtime.KeyValueObject", "RollingStock.Controls.GlobalKeyValueObject"))));

    public static void Run() {
        TheLoaderGroupStaysOutWithItsReason();
        TheLoaderCannotBeTargeted();
        AnimatorsStayNoParts();
        Console.WriteLine("PASS: coal loader");
    }

    // The loader group is not listed, under the Car loaders rule of the coverage statement: it has no inventory entry, no
    // category and no parts, and it is not in the Inspection only group.
    static void TheLoaderGroupStaysOutWithItsReason() {
        var verdicts = BakedSceneryRules.Classify(World());
        var group = verdicts.SingleOrDefault(v => v.Path == Loader);
        Assert(group?.Disposition == GroupDisposition.NotListed && group.Reason == BakedSceneryRules.CarLoaders && group.Ticket == null && group.Category == null,
            "The loader group is not listed under the Car loaders rule: " + group?.Disposition + " " + group?.Reason + " " + group?.Category);
        Assert(BakedSceneryRules.CarLoaders == "Car loaders", "The rule is named as its coverage statement row: " + BakedSceneryRules.CarLoaders);
        Assert(!verdicts.Any(v => v.Disposition == GroupDisposition.InspectionOnly), "Nothing of the loader stands in the Inspection only group");
        Assert(!BakedSceneryRules.Parts(verdicts).Any(p => p.GroupPath == Loader || p.Path.StartsWith(Loader + "/", StringComparison.Ordinal)), "A group that is not listed has no parts");
        Assert(BakedSceneryRules.CarLoaderRefusal == "This group holds a car loader, so it is not listed. The game loads Robinson Gap coal by teleport between the industry's track spans, not at the loader, so moving the loader would not move loading",
            "The refusal names the rule and why: " + BakedSceneryRules.CarLoaderRefusal);
        Assert(BakedSceneryRules.CarLoaderRefusal.IndexOf('#') < 0 && !BakedSceneryRules.Exclusions.Any(e => e.Component == "RollingStock.CarContentSwapper"), "No ticket and no inspection-only exclusion names the loader");
        // The rule holds the car loader wherever it stands in a group, not only at Robinson Gap Mine.
        var elsewhere = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Town", new Node("Tipple", new Node("Chute").With("RollingStock.CarContentSwapper").Solid())))))
            .Single(v => v.Path == "World/Large Scenery/Town/Tipple");
        Assert(elsewhere.Disposition == GroupDisposition.NotListed && elsewhere.Reason == BakedSceneryRules.CarLoaders, "A car loader deeper in a group keeps the group out: " + elsewhere.Disposition + " " + elsewhere.Reason);
    }

    // A group or part modification that names the loader, or an object below it, fails before any change.
    const string SceneName = "BushnellWhittier-MapManager";
    static void TheLoaderCannotBeTargeted() {
        var world = new FakeGroupWorld();
        var group = new FakeGroup { Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        group.Position = new[] { 317.85, 602.86, 2188.55 };
        group.Add(new FakePart { Id = "Spotlight Outdoor#1", Offset = new[] { 2.0, 8, 0 } });
        var verdicts = BakedSceneryRules.Classify(World());
        world.GroupVerdicts = verdicts.ToDictionary(v => v.Path, v => v);
        world.Verdicts = BakedSceneryRules.Parts(verdicts).ToDictionary(v => v.Path, v => v);
        // The game's adapter resolves a part only through a part verdict (GroupWorld.FindParts); an inspection-only group
        // has none, so no part path is registered.
        world.Objects[Loader] = new List<object> { group };
        world.SetActive(group, true); world.Mutations = 0;
        var holds = new PartHolds();
        var scene = new SceneCapabilities(() => world.Maps, null);
        scene.Add(new BakedSceneryGroups(world, holds: holds)); scene.Add(new BakedSceneryParts(world, holds));
        var engine = new PatchEngine(scene);
        string Mod(string id, string path, string structure) =>
            "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
            ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", new[] { 1337.85, 602.86, 2178.55 }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var result = engine.Apply("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",",
            Mod("group", Loader, BakedSceneryGroups.Structure), Mod("loaderAsPart", Loader, BakedSceneryParts.Structure), Mod("light", Loader + "/Model/Spotlight Outdoor", BakedSceneryParts.Structure),
            Mod("chute", Loader + "/Model/MineLoader/Chute", BakedSceneryParts.Structure)) + "}}");
        // A group or part modification that names the loader fails with the reason; an object below it is no entry, so
        // its modification finds nothing.
        var named = new[] { "group", "loaderAsPart" };
        Assert(result.Outcomes.Count == 4 && named.All(id => !result.Outcomes[id].Applied && (result.Outcomes[id].Error ?? "").Contains(BakedSceneryRules.CarLoaderRefusal))
            && new[] { "light", "chute" }.All(id => !result.Outcomes[id].Applied && result.Outcomes[id].Error.Contains("resolve uniquely")) && world.Mutations == 0,
            "A modification that names the loader is refused with the reason, and none reaches its objects: " + String.Join("; ", result.Outcomes.Select(o => o.Key + " " + o.Value.Applied + " " + o.Value.Error)));
    }

    // The driven-transform rule stays: an object with a KeyValueBoolAnimator is animated, so it and everything below it
    // are no parts of a listed group.
    static void AnimatorsStayNoParts() {
        Assert(BakedSceneryRules.Animates(new[] { "UnityEngine.Animator", "RollingStock.Controls.KeyValueBoolAnimator" }) && !BakedSceneryRules.Animates(new[] { "UnityEngine.Animator" })
            && !BakedSceneryRules.Animates(new[] { "UnityEngine.Light" }) && !BakedSceneryRules.Animates(null),
            "A key-value animator marks its object as animated; the game's adapter reads the same list");
        var verdicts = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Town", new Node("Shed",
            new Node("Doors", new Node("Leaf").Solid()).With("UnityEngine.Animator", "RollingStock.Controls.KeyValueBoolAnimator"), new Node("Walls").Solid())))));
        var parts = BakedSceneryRules.Parts(verdicts).ToDictionary(p => p.Path, p => p);
        const string shed = "World/Large Scenery/Town/Shed";
        Assert(parts[shed + "/Doors"].Reason == BakedSceneryRules.AnimatedParts && parts[shed + "/Doors/Leaf"].Reason == BakedSceneryRules.AnimatedParts
            && parts[shed + "/Walls"].Disposition == PartDisposition.Part, "The animated object and everything below it are no parts; its siblings are");
    }
}
