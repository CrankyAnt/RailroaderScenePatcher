// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The Robinson Gap Mine Coal Loader is a decorative baked scenery group under Other, like the other Robinson Gap Mine
// groups. In Railroader 0.6.3 the base map loads Robinson Gap coal by teleport between the spans of its industry (a
// TeleportLoadingIndustry in Map.json), not at the loader, so moving the loader does not move loading, and the patcher
// promises no loading function for it. The controlled hierarchy is the recorded Railroader 0.6.3 scene
// (Records/InventoryLedger/level4_mb_rows.json, Records/InventoryCoverage/renderer_coverage.csv): the car loader
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
    // A listed group beside the loader at Robinson Gap Mine.
    internal const string Neighbour = "World/Large Scenery/Robinson Gap Mine/Mine Office";
    const string Light = Loader + "/Model/Spotlight Outdoor", MineLoader = Loader + "/Model/MineLoader", Chute = MineLoader + "/Chute";
    // The word of the removed refusal; no outcome for the loader may carry it.
    const string CarLoaderWord = "car loader";

    public static void Run() {
        TheLoaderGroupIsListedUnderOther();
        TheObjectTabShowsTheDecorativeText();
        TheLoaderAndItsPartsTakeModifications();
        EditingHistorySaveAndRemoval();
        FileVersionsWithLoaderModificationsAreUsed();
        AnimatorsStayNoParts();
        TheMineLoaderGoesWithTheGroup();
        Console.WriteLine("PASS: coal loader");
    }

    // The loader group is listed under Industry building, as a loader that fills cars is, with the Mine Office beside
    // it, and its parts follow the part rule as in any other group.
    static void TheLoaderGroupIsListedUnderOther() {
        var verdicts = BakedSceneryRules.Classify(World());
        var group = verdicts.SingleOrDefault(v => v.Path == Loader);
        var office = verdicts.SingleOrDefault(v => v.Path == Neighbour);
        Assert(group?.Disposition == GroupDisposition.Group && group.Reason == null && group.Ticket == null && group.Category == BakedSceneryCategories.IndustryBuilding && group.Subcategory == null,
            "The loader group is listed under Industry building: " + group?.Disposition + " " + group?.Reason + " " + group?.Category);
        Assert(office?.Disposition == GroupDisposition.Group && office.Category == group.Category, "The loader stands where the other Robinson Gap Mine groups stand: " + office?.Category);
        Assert(!verdicts.Any(v => v.Disposition == GroupDisposition.InspectionOnly), "Nothing of the loader stands in the Inspection only group");
        Assert(!BakedSceneryRules.Exclusions.Any(e => e.Component == "RollingStock.CarContentSwapper"), "No inspection-only exclusion names the car loader");
        var parts = BakedSceneryRules.Parts(verdicts).Where(p => p.GroupPath == Loader).ToDictionary(p => p.Path, p => p);
        Assert(parts[Light].Disposition == PartDisposition.Part && parts[Loader + "/Model"].Reason == BakedSceneryRules.CullingContainers
            && parts[Loader + "/Model/MineLoader"].Reason == BakedSceneryRules.AnimatedParts && parts[Chute].Reason == BakedSceneryRules.AnimatedParts,
            "The part rule decides the loader's objects as in any group: " + String.Join("; ", parts.Values.Select(p => p.Path + " " + p.Disposition + " " + p.Reason)));
        // A car loader elsewhere keeps no group out either.
        var elsewhere = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Town", new Node("Tipple", new Node("Chute").With("RollingStock.CarContentSwapper").Solid())))))
            .Single(v => v.Path == "World/Large Scenery/Town/Tipple");
        Assert(elsewhere.Disposition == GroupDisposition.Group && elsewhere.Reason == null, "A car loader deeper in a group keeps it listed: " + elsewhere.Disposition + " " + elsewhere.Reason);
    }

    // The Object tab shows the decorative Use in the game text of a group without a function of its own, for the group and for its part.
    static void TheObjectTabShowsTheDecorativeText() {
        var group = CommandBand.UseInGame(false, BakedSceneryCategories.Unclassified, null, new string[0]);
        var part = CommandBand.UseInGame(true, BakedSceneryCategories.Unclassified, null, new string[0]);
        Assert(group == CommandBand.Decorative && part == CommandBand.PartDecorative, "The decorative texts: " + group + " | " + part);
        Assert(group.IndexOf(CarLoaderWord, StringComparison.OrdinalIgnoreCase) < 0 && part.IndexOf(CarLoaderWord, StringComparison.OrdinalIgnoreCase) < 0, "No car loader reason");
    }

    // The loader, with its light and its animated chute, and a listed group beside it, as the group and part capabilities
    // see them. The game's adapter finds every loaded object at a path (BoardScene.LoadedAt); the part rule's verdicts
    // decide which of them resolve.
    const string SceneName = "BushnellWhittier-MapManager";
    static readonly double[] Home = { 317.85, 602.86, 2188.55 }, LightOffset = { 2.0, 8, 0 }, MineOffset = { 1.5, 4, -2 }, ChuteOffset = { 0.0, 3, 1 },
        MineRotation = { 0, 0.2588190451, 0, 0.9659258263 };
    sealed class LoaderWorld {
        public readonly FakeGroupWorld World = new FakeGroupWorld();
        public readonly FakeGroup Group = new FakeGroup { Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        public readonly FakeGroup Office = new FakeGroup { Parts = new List<FakePart>(), Cullers = new List<FakeCuller>(), RootCulling = null };
        // Model, the empty container whose simple culler shows it while the camera is near, is left out: the offsets of
        // its children stand for their place relative to the root. MineLoader is empty, and the game animates it and
        // everything below it.
        public readonly FakePart Mine, Spotlight, ChutePart;
        public readonly PatchEngine Engine;
        public LoaderWorld() {
            Group.Position = (double[])Home.Clone();
            Spotlight = Group.Add(new FakePart { Id = "Spotlight Outdoor#1", Offset = (double[])LightOffset.Clone() });
            Mine = Group.Add(new FakePart { Id = "MineLoader#3", Offset = (double[])MineOffset.Clone(), Rotation = (double[])MineRotation.Clone(), Animated = true, Empty = true });
            ChutePart = Group.Add(new FakePart { Id = "Chute#2", Offset = (double[])ChuteOffset.Clone(), Parent = Mine, Animated = true });
            Office.Position = new[] { 340.0, 602.86, 2190.0 };
            var verdicts = BakedSceneryRules.Classify(CoalLoaderTests.World());
            World.GroupVerdicts = verdicts.ToDictionary(v => v.Path, v => v);
            World.Verdicts = BakedSceneryRules.Parts(verdicts).ToDictionary(v => v.Path, v => v);
            World.Objects[Loader] = new List<object> { Group };
            World.Objects[Neighbour] = new List<object> { Office };
            World.PartsAt[Light] = new List<object> { Spotlight };
            World.PartsAt[MineLoader] = new List<object> { Mine };
            World.PartsAt[Chute] = new List<object> { ChutePart };
            World.SetActive(Group, true); World.SetActive(Office, true); World.Mutations = 0;
            var holds = new PartHolds();
            var scene = new SceneCapabilities(() => World.Maps, null);
            scene.Add(new BakedSceneryGroups(World, holds: holds)); scene.Add(new BakedSceneryParts(World, holds));
            Engine = new PatchEngine(scene);
        }
        public bool MineUnmoved => Near(Mine.Offset, MineOffset) && Near(Mine.Rotation, MineRotation) && Near(ChutePart.Offset, ChuteOffset);
        public bool LoaderUnedited => Near(Group.Position, Home) && Near(Spotlight.Offset, LightOffset) && MineUnmoved
            && Group.Active && Spotlight.Active && Mine.Active && ChutePart.Active;
        public string State => "group " + String.Join(",", Group.Position) + " active " + Group.Active + ", light " + String.Join(",", Spotlight.Offset)
            + ", mine loader " + String.Join(",", Mine.Offset) + " rotation " + String.Join(",", Mine.Rotation) + " active " + Mine.Active + " in hierarchy " + InHierarchy(Mine)
            + ", chute " + String.Join(",", ChutePart.Offset);
    }
    static bool Near(double[] a, double[] b) => a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) < 1e-6).All(close => close);
    // Whether the object is active in the hierarchy: it, every part above it and the group root are active.
    static bool InHierarchy(FakePart part) { for (var p = part; p != null; p = p.Parent) if (!p.Active) return false; return part.Group.Active; }
    static string Mod(string id, string path, string structure, double x, string extra = "") =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + structure + "\"}" +
        ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", new[] { x, 602.86, 2178.55 }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[0,0,0,1]}" + extra + ",\"dependsOn\":[]}";
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static string Outcomes(ScenePatchFileResult result) => String.Join("; ", result.Outcomes.Select(o => o.Key + " " + o.Value.Applied + " " + o.Value.Error));
    static bool NoCarLoaderReason(ScenePatchFileResult result) => result.Outcomes.Values.All(o => (o.Error ?? "").IndexOf(CarLoaderWord, StringComparison.OrdinalIgnoreCase) < 0);

    // A group move and a part move of the loader apply, beside the neighbour's move; a group disable switches it off.
    // The loader's animated chute is no part and is refused as any animated object is, not with a car loader reason.
    static void TheLoaderAndItsPartsTakeModifications() {
        var f = new LoaderWorld();
        var result = f.Engine.Apply(Doc(Mod("group", Loader, BakedSceneryGroups.Structure, 1337.85), Mod("light", Light, BakedSceneryParts.Structure, 1339.85),
            Mod("office", Neighbour, BakedSceneryGroups.Structure, 1350.0)));
        Assert(result.Outcomes.Values.All(o => o.Applied), "The loader, its light, and the neighbour move: " + Outcomes(result));
        Assert(Math.Abs(f.Group.Position[0] - 337.85) < 1e-9 && Math.Abs(f.Office.Position[0] - 350.0) < 1e-9, "The loader stands at its new place: " + f.Group.Position[0]);
        var disabled = new LoaderWorld();
        result = disabled.Engine.Apply(Doc(Mod("off", Loader, BakedSceneryGroups.Structure, 1337.85, ",\"active\":false")));
        Assert(result.Outcomes["off"].Applied && !disabled.Group.Active, "A disabled loader is switched off: " + Outcomes(result));
        var refused = new LoaderWorld();
        result = refused.Engine.Apply(Doc(Mod("chute", Chute, BakedSceneryParts.Structure, 1337.85), Mod("loaderAsPart", Loader, BakedSceneryParts.Structure, 1337.85)));
        Assert(!result.Outcomes["chute"].Applied && !result.Outcomes["loaderAsPart"].Applied && result.Outcomes["loaderAsPart"].Error.Contains(BakedSceneryRules.GroupRootRefusal)
            && refused.LoaderUnedited, "An animated object and the group root as a part are refused as in any group: " + Outcomes(result));
        Assert(NoCarLoaderReason(result), "No refusal names a car loader: " + Outcomes(result));
    }

    // Editing the loader in a session: a move and a disable, each reversed by Undo and reapplied by Redo; Save and a
    // reload reproduce both without duplicated state; Remove modification returns the loader to its unedited place.
    static void EditingHistorySaveAndRemoval() {
        var f = new LoaderWorld();
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var identity = new TargetIdentity(SceneName, Loader, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
        var moved = new Placement(new[] { 1337.85, 602.86, 2178.55 }, new[] { 0.0, 0, 0, 1 });
        Assert(session.Edit(identity, moved, null).Applied && Math.Abs(f.Group.Position[0] - 337.85) < 1e-9 && f.Spotlight.Offset.SequenceEqual(LightOffset), "The loader moves with its parts");
        Assert(session.Edit(identity, null, false).Applied && !f.Group.Active, "The loader is disabled");
        mapEditor.Undo(); Assert(f.Group.Active && Math.Abs(f.Group.Position[0] - 337.85) < 1e-9, "Undo re-enables the moved loader");
        mapEditor.Undo(); Assert(f.LoaderUnedited, "Undo returns the loader to its unedited place: " + f.State);
        mapEditor.Redo(); Assert(Math.Abs(f.Group.Position[0] - 337.85) < 1e-9 && f.Group.Active, "Redo reapplies the move");
        mapEditor.Redo(); Assert(!f.Group.Active, "Redo reapplies the disable");
        Assert(mapEditor.Save() == null && !mapEditor.IsDirty, "Save writes the loader's modification");
        var reloaded = new LoaderWorld();
        var reload = reloaded.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Count == 1 && reload.Outcomes.Values.Single().Applied && Math.Abs(reloaded.Group.Position[0] - 337.85) < 1e-9 && !reloaded.Group.Active
            && reloaded.Group.Parts.Count == 3, "A reload reproduces the moved and disabled loader without duplicated state: " + Outcomes(reload));
        Assert(session.Remove(identity).Applied && f.LoaderUnedited, "Remove modification returns the loader to its unedited place: " + String.Join(",", f.Group.Position) + " " + f.Group.Active);
    }

    // Use file version applies the loader modifications of a version like any others, and returns the loader home for a
    // version without them.
    static void FileVersionsWithLoaderModificationsAreUsed() {
        var f = new LoaderWorld();
        var loaderVersion = Doc(Mod("group", Loader, BakedSceneryGroups.Structure, 1337.85), Mod("office", Neighbour, BakedSceneryGroups.Structure, 1350.0));
        var load = f.Engine.Apply(loaderVersion, "author/pack");
        Assert(load.Outcomes.Values.All(o => o.Applied) && Math.Abs(f.Group.Position[0] - 337.85) < 1e-9, "The load applies the loader modification: " + Outcomes(load));
        var store = new Store { Patch = loaderVersion };
        var session = new PatchEditingSession(f.Engine, new NativeMapEditor(), store, scenePatchFileKey: "author/pack");
        store.Patch = Doc(Mod("office", Neighbour, BakedSceneryGroups.Structure, 1360.0)); session.CheckFileVersion();
        var chosen = session.UseFileVersion();
        Assert(chosen.Applied && session.LastVersionApplication.Outcomes.Values.All(o => o.Applied) && Math.Abs(f.Office.Position[0] - 360.0) < 1e-9 && f.LoaderUnedited,
            "A version without loader targets returns the loader home: " + chosen.Error + " " + f.Group.Position[0]);
        store.Patch = loaderVersion; session.CheckFileVersion();
        chosen = session.UseFileVersion();
        Assert(chosen.Applied && session.LastVersionApplication.Outcomes.Values.All(o => o.Applied) && Math.Abs(f.Group.Position[0] - 337.85) < 1e-9,
            "The loader version is used again: " + chosen.Error + " " + f.Group.Position[0]);
        Assert(NoCarLoaderReason(session.LastVersionApplication), "No outcome names a car loader");
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

    // The hidden Model/MineLoader is never listed on its own: it goes with the Coal Loader group. After a move of the
    // group it keeps its unedited place relative to the group root; after Disable of the group it is inactive in the
    // hierarchy, and Undo brings it back. The patcher never writes its own activity, also when the game left it
    // inactive by itself, as Railroader 0.6.3 does.
    static void TheMineLoaderGoesWithTheGroup() {
        bool AtOrBelowMineLoader(string path) => path == MineLoader || path.StartsWith(MineLoader + "/", StringComparison.Ordinal);
        var verdicts = BakedSceneryRules.Classify(World());
        var unlisted = BakedSceneryRules.Parts(verdicts).Where(p => AtOrBelowMineLoader(p.Path)).ToList();
        Assert(unlisted.Count == 1 + MineLoaderRenderers.Length && unlisted.All(p => p.Disposition == PartDisposition.NotListed && p.Reason == BakedSceneryRules.AnimatedParts),
            "MineLoader and its renderers are unlisted animated parts: " + String.Join("; ", unlisted.Select(p => p.Path + " " + p.Disposition + " " + p.Reason)));
        Assert(!verdicts.Any(v => AtOrBelowMineLoader(v.Path)), "MineLoader is no group of its own");

        var f = new LoaderWorld();
        Assert(f.World.FindParts(SceneName, MineLoader).Count == 0, "MineLoader resolves as no part");
        // Each in its own document and world, so that only the part rule can refuse it: the path finds no part.
        foreach (var mod in new[] { Mod("mine", MineLoader, BakedSceneryParts.Structure, 1337.85), Mod("mine", MineLoader, BakedSceneryParts.Structure, 1317.85, ",\"active\":false") }) {
            var alone = new LoaderWorld();
            var refused = alone.Engine.Apply(Doc(mod));
            Assert(refused.Outcomes.Count == 1 && !refused.Outcomes["mine"].Applied && refused.Outcomes["mine"].Error == PatchEngine.TargetMissing + " " + PatchEngine.TargetMissingAction && alone.LoaderUnedited,
                "A move or a disable of MineLoader on its own is refused: " + Outcomes(refused) + " | " + alone.State);
        }

        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var identity = new TargetIdentity(SceneName, Loader, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
        var moved = new Placement(new[] { 1337.85, 604.86, 2178.55 }, Turn.Yaw(90));
        Assert(session.Edit(identity, moved, null).Applied && Math.Abs(f.Group.Position[0] - 337.85) < 1e-9, "The group moves: " + f.State);
        Assert(f.MineUnmoved && InHierarchy(f.Mine) && InHierarchy(f.ChutePart),
            "MineLoader keeps its place relative to the group root: " + f.State);
        Assert(session.Edit(identity, null, false).Applied && !InHierarchy(f.Mine) && !InHierarchy(f.ChutePart) && f.Mine.Active && f.MineUnmoved,
            "After Disable of the group MineLoader is inactive in the hierarchy, its own activity untouched: " + f.State);
        mapEditor.Undo();
        Assert(InHierarchy(f.Mine) && f.MineUnmoved, "Undo of Disable brings MineLoader back with the group: " + f.State);
        Assert(session.Remove(identity).Applied && f.LoaderUnedited, "Remove modification leaves MineLoader at its unedited place: " + f.State);

        var dormant = new LoaderWorld();
        dormant.Mine.Active = false;
        var dormantSession = new PatchEditingSession(dormant.Engine, new NativeMapEditor(), new Store());
        Assert(dormantSession.Edit(identity, moved, null).Applied && dormant.MineUnmoved && !dormant.Mine.Active, "A move of the group leaves an inactive MineLoader inactive, at its place");
        Assert(dormantSession.Edit(identity, null, false).Applied && dormantSession.Edit(identity, null, true).Applied && !dormant.Mine.Active && dormant.MineUnmoved,
            "Disable and enable of the group never switch MineLoader on by itself");
    }
}
