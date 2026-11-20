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
            .With("RollingStock.CarContentSwapper", "KeyValue.Runtime.KeyValueObject", "RollingStock.Controls.GlobalKeyValueObject"),
        new Node("Mine Office").Solid())));
    // A listed group beside the loader, whose modification in the same file still applies.
    internal const string Neighbour = "World/Large Scenery/Robinson Gap Mine/Mine Office";

    public static void Run() {
        TheLoaderGroupStaysOutWithItsReason();
        TheLoaderCannotBeTargeted();
        AnotherFileVersionCanBeUsedAfterARefusedLoaderModification();
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
        Assert(BakedSceneryRules.CarLoaderRefusal == "This group isn't listed because it contains a car loader. The game loads Robinson Gap coal by teleport between the industry's track spans. It doesn't use the loader, so moving the loader wouldn't change where loading happens.",
            "The refusal names the rule and why: " + BakedSceneryRules.CarLoaderRefusal);
        Assert(BakedSceneryRules.CarLoaderRefusal.IndexOf('#') < 0 && !BakedSceneryRules.Exclusions.Any(e => e.Component == "RollingStock.CarContentSwapper"), "No ticket and no inspection-only exclusion names the loader");
        // The rule holds the car loader wherever it stands in a group, not only at Robinson Gap Mine.
        var elsewhere = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Town", new Node("Tipple", new Node("Chute").With("RollingStock.CarContentSwapper").Solid())))))
            .Single(v => v.Path == "World/Large Scenery/Town/Tipple");
        Assert(elsewhere.Disposition == GroupDisposition.NotListed && elsewhere.Reason == BakedSceneryRules.CarLoaders, "A car loader deeper in a group keeps the group out: " + elsewhere.Disposition + " " + elsewhere.Reason);
    }

    // The loader, with a light and a chute below it, and a listed group beside it, as the group and part capabilities see
    // them. The game's adapter finds every loaded object at a path (BoardScene.LoadedAt). The loader has no part
    // verdicts, so an object below it resolves only through the group with the car loader above it.
    const string SceneName = "BushnellWhittier-MapManager";
    sealed class LoaderWorld {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeGroup Group = new FakeGroup { Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakeGroup Office = new FakeGroup { Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakePart Light, Chute;
        public readonly PatchEngine Engine;
        public LoaderWorld() {
            Group.Position = new[] { 317.85, 602.86, 2188.55 };
            Light = Group.Add(new FakePart { Id = "Spotlight Outdoor#1", Offset = new[] { 2.0, 8, 0 } });
            Chute = Group.Add(new FakePart { Id = "Chute#2", Offset = new[] { 0.0, 3, 1 } });
            Office.Position = new[] { 340.0, 602.86, 2190.0 };
            var verdicts = BakedSceneryRules.Classify(CoalLoaderTests.World());
            World.GroupVerdicts = verdicts.ToDictionary(v => v.Path, v => v);
            World.Verdicts = BakedSceneryRules.Parts(verdicts).ToDictionary(v => v.Path, v => v);
            World.Objects[Loader] = new List<object> { Group };
            World.Objects[Neighbour] = new List<object> { Office };
            World.PartsAt[Loader + "/Model/Spotlight Outdoor"] = new List<object> { Light };
            World.PartsAt[Loader + "/Model/MineLoader/Chute"] = new List<object> { Chute };
            World.SetActive(Group, true); World.SetActive(Office, true); World.Mutations = 0;
            var holds = new PartHolds();
            var scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new BakedSceneryGroups(World, holds: holds)); scene.Add(new BakedSceneryParts(World, holds));
            Engine = new PatchEngine(scene);
        }
        // Nothing of the loader moved or switched off.
        public bool LoaderUntouched => Group.Position.SequenceEqual(new[] { 317.85, 602.86, 2188.55 }) && Light.Offset.SequenceEqual(new[] { 2.0, 8, 0 })
            && Chute.Offset.SequenceEqual(new[] { 0.0, 3, 1 }) && Group.Active && Light.Active && Chute.Active;
    }
    static string Mod(string id, string path, string structure, double x) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", new[] { x, 602.86, 2178.55 }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";

    // A group or part modification that names the loader, or an object below it, fails with the Car loaders reason
    // before any change, and the file's other modifications still apply.
    static void TheLoaderCannotBeTargeted() {
        var f = new LoaderWorld();
        var result = f.Engine.Apply(Doc(
            Mod("group", Loader, BakedSceneryGroups.Structure, 1337.85), Mod("loaderAsPart", Loader, BakedSceneryParts.Structure, 1337.85),
            Mod("light", Loader + "/Model/Spotlight Outdoor", BakedSceneryParts.Structure, 1337.85), Mod("chute", Loader + "/Model/MineLoader/Chute", BakedSceneryParts.Structure, 1337.85),
            Mod("office", Neighbour, BakedSceneryGroups.Structure, 1350.0)));
        var refused = new[] { "group", "loaderAsPart", "light", "chute" };
        Assert(result.Outcomes.Count == 5 && refused.All(id => !result.Outcomes[id].Applied && (result.Outcomes[id].Error ?? "").Contains(BakedSceneryRules.CarLoaderRefusal)),
            "A modification that names the loader or an object below it is refused with the Car loaders reason: " + String.Join("; ", result.Outcomes.Select(o => o.Key + " " + o.Value.Applied + " " + o.Value.Error)));
        Assert(f.LoaderUntouched, "No refused modification reaches the loader's objects");
        Assert(result.Outcomes["office"].Applied && Math.Abs(f.Office.Position[0] - 350.0) < 1e-9, "The file's other modification still applies: " + result.Outcomes["office"].Error + " " + f.Office.Position[0]);
    }

    // A refused modification refuses only itself: with a file whose loader modifications were refused in use, Use file
    // version applies another version, which holds no loader target, by that version's own modifications. Choosing the
    // loader version again refuses only its loader modifications, and an empty version is used after it.
    static void AnotherFileVersionCanBeUsedAfterARefusedLoaderModification() {
        var f = new LoaderWorld();
        var loaderVersion = Doc(Mod("light", Loader + "/Model/Spotlight Outdoor", BakedSceneryParts.Structure, 1337.85),
            Mod("chute", Loader + "/Model/MineLoader/Chute", BakedSceneryParts.Structure, 1337.85), Mod("office", Neighbour, BakedSceneryGroups.Structure, 1350.0));
        var load = f.Engine.Apply(loaderVersion, "author/pack");
        Assert(!load.Outcomes["light"].Applied && !load.Outcomes["chute"].Applied && load.Outcomes["office"].Applied, "The load refuses the loader modifications and applies the other one");
        var store = new Store { Patch = loaderVersion };
        var session = new PatchEditingSession(f.Engine, new NativeMapEditor(), store, scenePatchFileKey: "author/pack");
        store.Patch = Doc(Mod("office", Neighbour, BakedSceneryGroups.Structure, 1360.0)); session.CheckFileVersion();
        var chosen = session.UseFileVersion();
        Assert(chosen.Applied && session.LastVersionApplication.Outcomes.Values.All(o => o.Applied) && Math.Abs(f.Office.Position[0] - 360.0) < 1e-9 && f.LoaderUntouched,
            "A version without loader targets is used after the refused loader modifications: " + chosen.Error + " " + f.Office.Position[0]);
        store.Patch = loaderVersion; session.CheckFileVersion();
        chosen = session.UseFileVersion();
        var outcomes = session.LastVersionApplication?.Outcomes;
        Assert(chosen.Applied && !outcomes["light"].Applied && outcomes["light"].Error.Contains(BakedSceneryRules.CarLoaderRefusal) && outcomes["office"].Applied
            && Math.Abs(f.Office.Position[0] - 350.0) < 1e-9 && f.LoaderUntouched, "The loader version is used again and refuses only its loader modifications: " + chosen.Error);
        store.Patch = Doc(); session.CheckFileVersion();
        chosen = session.UseFileVersion();
        Assert(chosen.Applied && Math.Abs(f.Office.Position[0] - 340.0) < 1e-9 && f.LoaderUntouched, "An empty version is used after the loader version again: " + chosen.Error + " " + f.Office.Position[0]);
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
