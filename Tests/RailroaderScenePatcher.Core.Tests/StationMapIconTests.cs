// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Station map icons: the map icons below World/Map Labels/Station Icons form the Station map icon family, each with
// physical placement and Enabled. Controlled hierarchies follow the Railroader 0.6.3 scene: the Station Icons folder
// holds one object per station with a UI.Map.MapIcon, an image and a box collider; map labels stand beside it.
static class StationMapIconTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Station map icons: " + message); }
    const string SceneName = "level4", Folder = StationMapIconRules.Folder, Bryson = Folder + "/Bryson", Cochran = Folder + "/Cochran", Whittier = Folder + "/Whittier";
    static Node Icon(string name) => new Node(name, new Node("Text").With("TMPro.TextMeshProUGUI")).With(StationMapIconRules.Component, "UnityEngine.UI.Image", "UnityEngine.BoxCollider").Solid().Family();
    internal static Node World() => new Node("World",
        new Node("Map Labels", new Node("Bryson").With("UI.Map.MapLabel").Solid(), new Node("Station Icons", Icon("Bryson"), Icon("Cochran"), Icon("Whittier"))),
        new Node("Large Scenery", new Node("Bryson", new Node("Bryson House Track", new Node("Platform").Solid()))));

    public static void Run() {
        InventoryListsEveryStationMapIconOnce();
        OnlyStationIconsAreListed();
        FolderChildrenThatAreNoIconsStayOut();
        IconEditsApplyRepeatAndRestore();
        IconEditsInScenePatchEditing();
        RefusalInScenePatchEditingLeavesTheFile();
        MinimumHoldsWithoutTheCapability();
        Console.WriteLine("PASS: station map icons");
    }

    // The Station Icons folder is no group and gets no row: its icons keep their own family, each once. The map label
    // beside it stays out, and nothing in the folder is inspection only. In the outliner every icon stands once in the
    // Station map icon family, outside the Inspection only group.
    static void InventoryListsEveryStationMapIconOnce() {
        var verdicts = BakedSceneryRules.Classify(World());
        Assert(!verdicts.Any(v => v.Path == Folder), "The Station Icons folder has no verdict of its own: " + String.Join(", ", verdicts.Where(v => v.Path == Folder).Select(v => v.Disposition + " " + v.Reason)));
        foreach (var path in new[] { Bryson, Cochran, Whittier }) {
            var found = verdicts.Where(v => v.Path == path).ToArray();
            Assert(found.Length == 1 && found[0].Disposition == GroupDisposition.ExistingFamily, path + " keeps its family once: " + String.Join(", ", found.Select(v => v.Disposition + " " + v.Reason)));
        }
        Assert(verdicts.Single(v => v.Path == "World/Map Labels/Bryson").Disposition == GroupDisposition.NotListed, "A map label stays out");
        Assert(!verdicts.Any(v => v.Disposition == GroupDisposition.InspectionOnly), "Nothing is inspection only: " + String.Join(", ", verdicts.Where(v => v.Disposition == GroupDisposition.InspectionOnly).Select(v => v.Path)));
        Assert(!BakedSceneryRules.Exclusions.Any(e => e.Ticket == "#89" && e.Component == StationMapIconRules.Component), "No exclusion of a map icon waits for #89 any more");

        var rows = new[] { Bryson, Cochran, Whittier }.Select(path => Row(path)).ToList();
        rows.Add(new SceneObservation(new object(), new TargetIdentity(SceneName, "World/Large Scenery/Bryson/Bryson House Track", BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure),
            BakedSceneryRules.Family, true, new[] { 0.0, 0, 0 }, new[] { 0.0, 0, 0, 1 }, new string[0], new string[0], new string[0], CommandBand.GroupOperations, category: BakedSceneryCategories.Other));
        var groups = InventoryPresentationState.Groups(rows);
        var icons = groups.Where(g => g.Key == StationMapIconRules.Family).ToArray();
        Assert(icons.Length == 1 && icons[0].Value.Select(r => r.Identity.Path).SequenceEqual(new[] { Bryson, Cochran, Whittier }), "The outliner lists every icon once in the Station map icon family: "
            + String.Join("; ", groups.Select(g => g.Key + "=" + g.Value.Count)));
        Assert(!groups.Any(g => g.Key == InventoryPresentationState.InspectionOnlyGroup), "No icon stands in the Inspection only group");
    }
    static SceneObservation Row(string path) => new SceneObservation(new object(), new TargetIdentity(SceneName, path, StationMapIconRules.Component, StationMapIconRules.Structure),
        StationMapIconRules.Family, true, new[] { 1.0, 2, 3 }, new[] { 0.0, 0, 0, 1 }, new string[0], new[] { StationMapIconRules.Dependency }, new string[0], StationMapIconRules.Operations);

    // Only map icons below the Station Icons folder are station map icons; the capability resolves no other target.
    static void OnlyStationIconsAreListed() {
        Assert(StationMapIconRules.Listed(Bryson), "An icon directly in the folder is listed");
        Assert(!StationMapIconRules.Listed(Folder + "/Group/Deep") && !StationMapIconRules.Listed(Folder + "/"), "An icon in a subgroup of the folder is not listed");
        Assert(!StationMapIconRules.Listed(Folder) && !StationMapIconRules.Listed("World/Map Labels/Bryson") && !StationMapIconRules.Listed("Car/Map Icon") && !StationMapIconRules.Listed(null)
            && !StationMapIconRules.Listed(Folder + " (1)/Bryson"), "The folder itself, map labels and runtime icons are not listed");
        var f = new Icons();
        Assert(f.Scene.Resolve(Id(Bryson)).Count == 1, "A station map icon resolves");
        Assert(f.Scene.Resolve(new TargetIdentity(SceneName, Bryson, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure)).Count == 0
            && f.Scene.Resolve(new TargetIdentity(SceneName, Bryson, StationMapIconRules.Component, BakedSceneryParts.Structure)).Count == 0, "Another component or structure does not resolve");
        f.World.Objects["World/Map Labels/Bryson"] = new List<object> { new FakeIcon() };
        Assert(f.Scene.Resolve(new TargetIdentity(SceneName, "World/Map Labels/Bryson", StationMapIconRules.Component, StationMapIconRules.Structure)).Count == 0, "A map icon outside the folder does not resolve");
        // A group elsewhere that holds a map icon is not moved as a whole, without a ticket.
        var held = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Town", new Node("Depot", new Node("Icon").With(StationMapIconRules.Component), new Node("Shell").Solid())))))
            .Single(v => v.Path == "World/Large Scenery/Town/Depot");
        Assert(held.Disposition == GroupDisposition.InspectionOnly && held.Ticket == null && held.Reason == BakedSceneryRules.MapIconReason
            && CommandBand.GroupInspectionOnly(held, BakedSceneryRules.Family) == "Not editable as a group: " + BakedSceneryRules.MapIconReason, "A group with a map icon elsewhere states why it is left out: " + held.Reason);
        // That reason promises no edit: such a map icon is in no family.
        Assert(BakedSceneryRules.MapIconReason.IndexOf("family", StringComparison.OrdinalIgnoreCase) < 0 && BakedSceneryRules.MapIconReason.IndexOf("edited", StringComparison.OrdinalIgnoreCase) < 0,
            "The map icon reason promises no edit: " + BakedSceneryRules.MapIconReason);
        // A station click spot is native map data in Railroader 0.6.3: a settled rule without a ticket.
        var spot = BakedSceneryRules.Classify(new Node("World", new Node("Large Scenery", new Node("Town", new Node("Depot", new Node("Click").With("Model.StationAgent"), new Node("Shell").Solid())))))
            .Single(v => v.Path == "World/Large Scenery/Town/Depot");
        Assert(spot.Ticket == null && spot.Reason == BakedSceneryRules.StationAgentReason && CommandBand.GroupInspectionOnly(spot, BakedSceneryRules.Family) == "Not editable as a group: " + BakedSceneryRules.StationAgentReason,
            "A group with a station click spot reads Not editable as a group without a ticket: " + spot.Reason + " " + spot.Ticket);
        Assert(!BakedSceneryRules.Exclusions.Any(e => e.Ticket == "#89"), "No exclusion names #89");
        Assert(CommandBand.InspectionOnly(StationMapIconRules.Family, true) == null, "A station map icon is a supported kind");
        Assert(RecoveryGuidance.Guide("Object", null, StationMapIconRules.Structure).Value == "moving-station-map-icons.md", "A failed icon edit opens the station map icon guide");
    }

    // Only a direct child of the Station Icons folder that carries a map icon is an icon. A child without one is not
    // listed, and neither is a subgroup that holds an icon: no editable group, no inspection-only row, and no row for the
    // icon inside it, which Listed does not accept either.
    static void FolderChildrenThatAreNoIconsStayOut() {
        var verdicts = BakedSceneryRules.Classify(new Node("World", new Node("Map Labels", new Node("Station Icons",
            Icon("Bryson"),
            new Node("Backdrop").With("UnityEngine.MeshRenderer").Solid(),
            new Node("Group", Icon("Nested")).Solid()))));
        Assert(verdicts.Single(v => v.Path == Bryson).Disposition == GroupDisposition.ExistingFamily, "The icon keeps its family");
        foreach (var path in new[] { Folder + "/Backdrop", Folder + "/Group" }) {
            var verdict = verdicts.SingleOrDefault(v => v.Path == path);
            Assert(verdict?.Disposition == GroupDisposition.NotListed && verdict.Reason == BakedSceneryRules.MapLabel, path + " is not listed: " + verdict?.Disposition + " " + verdict?.Reason);
        }
        Assert(!verdicts.Any(v => v.Path == Folder + "/Group/Nested"), "The icon in a subgroup gets no verdict");
        Assert(!verdicts.Any(v => v.Disposition == GroupDisposition.Group || v.Disposition == GroupDisposition.InspectionOnly), "Nothing in the folder is an editable or inspection-only group");
        Assert(!StationMapIconRules.Listed(Folder + "/Group/Nested"), "The nested icon is in no family");
    }

    // A preflight refusal in scene patch editing changes neither the icon nor the scene patch file, and nothing waits to be saved.
    static void RefusalInScenePatchEditingLeavesTheFile() {
        var f = new Icons(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        Assert(session.Edit(Id(Cochran), new Placement(CochranTarget, Upright), null).Applied && mapEditor.Save() == null, "A first edit applies and saves");
        var saved = store.Patch; var info = store.Info;
        f.World.Refused = "The station map icon's scene is not loaded"; var mutations = f.World.Mutations;
        var refused = session.Edit(Id(Bryson), new Placement(BrysonTarget, Upright), false);
        Assert(!refused.Applied && refused.Error.Contains(f.World.Refused) && f.World.Mutations == mutations && Near(f.At(f.BrysonIcon), BrysonAuthored) && f.BrysonIcon.Active,
            "The refused edit changes nothing in the scene: " + refused.Error + " " + f.Where());
        Assert(!mapEditor.IsDirty && session.Outstanding(mapEditor.NativeDirty).Count == 0, "Nothing waits to be saved after the refusal");
        Assert(mapEditor.Save() == null && store.Patch == saved && store.Info == info, "The scene patch file is unchanged: " + store.Patch);
    }

    // A scene patch file with a station map icon target needs 0.5.0 also when this load has no station map icon
    // capability: by its structure, and by its component when it has no structure.
    static void MinimumHoldsWithoutTheCapability() {
        foreach (var structure in new[] { StationMapIconRules.Structure, null }) {
            var scene = new SceneCapabilities(() => new[] { "map" }, null);
            scene.AddUnavailable(StationMapIconRules.Structure, "Map icon types unavailable");
            var target = "{\"scene\":\"" + SceneName + "\",\"path\":\"" + Bryson + "\",\"component\":\"" + StationMapIconRules.Component + "\"" + (structure == null ? "" : ",\"structure\":\"" + structure + "\"") + "}";
            var store = new Store { Patch = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"m\":{\"target\":" + target + ",\"placement\":{\"mode\":\"fixed\",\"position\":[1,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}" };
            var mapEditor = new NativeMapEditor(); var session = new PatchEditingSession(new PatchEngine(scene), mapEditor, store); mapEditor.Session = session;
            Assert(session.MinimumPatcherVersion == StationMapIconRules.RequiredVersion, "A station map icon target (structure " + (structure ?? "none") + ") needs 0.5.0 without the capability: " + session.MinimumPatcherVersion);
        }
    }

    // One station map icon in the controlled world: its placement in scene coordinates, its activity, the object it
    // stands in and whether that object is active.
    sealed class FakeIcon { public double[] Position = { 0, 0, 0 }, Rotation = { 0, 0, 0, 1 }; public bool Active = true, ParentActive = true; public object Parent = "Station Icons"; }
    // The game's side: the origin offset between scene and map coordinates, the map's icon registry, and injected faults.
    sealed class IconWorld : IMapIconWorld {
        public double Origin = 100;
        public readonly Dictionary<string, List<object>> Objects = new Dictionary<string, List<object>>();
        public readonly HashSet<object> Registered = new HashSet<object>();
        public bool MapPresent = true, ThrowAfterPlacement, DropRegistration;
        public string Refused;
        public int Mutations;
        public IEnumerable<string> MapIds => new[] { "map" };
        public IReadOnlyList<object> Find(string scene, string path) => scene == SceneName && Objects.TryGetValue(path, out var found) ? found : new List<object>();
        public string Refusal(object icon) => Refused;
        public string DisableRefusal(object icon) => null;
        public MapIconObservation Observe(object target) {
            var icon = (FakeIcon)target;
            return new MapIconObservation(new ObjectState(new Placement(new[] { icon.Position[0] + Origin, icon.Position[1], icon.Position[2] }, (double[])icon.Rotation.Clone()), icon.Active),
                icon.Active && icon.ParentActive, icon.Parent, MapPresent ? Registered.Contains(icon) : (bool?)null);
        }
        public void SetPlacement(object target, Placement placement) {
            Mutations++; var icon = (FakeIcon)target;
            icon.Position = new[] { placement.Position[0] - Origin, placement.Position[1], placement.Position[2] }; icon.Rotation = (double[])placement.Rotation.Clone();
            if (ThrowAfterPlacement) { ThrowAfterPlacement = false; throw new InvalidOperationException("injected failure after the move"); }
        }
        // The icon registers with the map when it becomes active with its parents and leaves it when it is disabled.
        public void SetActive(object target, bool active) {
            Mutations++; var icon = (FakeIcon)target; icon.Active = active;
            if (!MapPresent) return;
            if (active && icon.ParentActive && !DropRegistration) Registered.Add(icon); else Registered.Remove(icon);
        }
    }
    sealed class Icons {
        public readonly IconWorld World = new IconWorld();
        public readonly FakeIcon BrysonIcon = new FakeIcon { Position = new[] { 900.0, 80, 400 } }, CochranIcon = new FakeIcon { Position = new[] { -300.0, 80, 1200 } };
        public readonly StationMapIconScene Scene;
        public readonly PatchEngine Engine;
        public Icons() {
            World.Objects[Bryson] = new List<object> { BrysonIcon }; World.Objects[Cochran] = new List<object> { CochranIcon };
            World.Registered.Add(BrysonIcon); World.Registered.Add(CochranIcon);
            var scene = new SceneCapabilities(() => World.MapIds, null);
            scene.Add(Scene = new StationMapIconScene(World));
            Engine = new PatchEngine(scene);
        }
        public double[] At(FakeIcon icon) => World.Observe(icon).Root.Placement.Position.ToArray();
        public string Where() => "bryson=" + N(At(BrysonIcon)) + " active=" + BrysonIcon.Active + " registered=" + World.Registered.Contains(BrysonIcon) + "; cochran=" + N(At(CochranIcon)) + " active=" + CochranIcon.Active;
    }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) => a != null && b != null && a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= 0.001);
    static string Mod(string id, string path, double[] position = null, bool? active = null, string structure = StationMapIconRules.Structure) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"" + StationMapIconRules.Component + "\",\"structure\":\"" + structure + "\"}" +
        (position == null ? "" : ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", position.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[0,0,0,1]}") +
        (active == null ? "" : ",\"active\":" + (active.Value ? "true" : "false")) + ",\"dependsOn\":[]}";
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static TargetIdentity Id(string path) => new TargetIdentity(SceneName, path, StationMapIconRules.Component, StationMapIconRules.Structure);
    static readonly double[] Upright = { 0, 0, 0, 1 }, BrysonAuthored = { 1000, 80, 400 }, CochranAuthored = { -200, 80, 1200 }, BrysonTarget = { 1250, 80, 460 }, CochranTarget = { -150, 80, 1320 };

    // A moved icon stands at its new map place and stays registered with the map; a repeated application and a load at
    // another origin reach the same placement; a disabled icon leaves the map; a refusal mutates nothing; a failure after
    // mutation restores placement and activity; an incomplete cleanup is reported and blocks the retry.
    static void IconEditsApplyRepeatAndRestore() {
        var f = new Icons();
        var doc = Doc(Mod("move-bryson", Bryson, BrysonTarget), Mod("hide-cochran", Cochran, active: false));
        var result = f.Engine.Apply(doc);
        Assert(result.Outcomes.Values.All(o => o.Applied), "The icon modifications apply: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error)));
        Assert(Near(f.At(f.BrysonIcon), BrysonTarget) && f.World.Registered.Contains(f.BrysonIcon), "The moved icon stands at its new place and stays on the map: " + f.Where());
        Assert(!f.CochranIcon.Active && !f.World.Registered.Contains(f.CochranIcon) && Near(f.At(f.CochranIcon), CochranAuthored), "The disabled icon leaves the map where it stands: " + f.Where());
        var first = f.Where();
        Assert(f.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && f.Where() == first, "A repeated application accumulates no offset: " + f.Where());
        var shifted = new Icons(); shifted.World.Origin = 1010;
        Assert(shifted.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && N(shifted.At(shifted.BrysonIcon)) == N(BrysonTarget), "A load at another origin reaches the same map placement: " + shifted.Where());
        // Without a map to register with, as before the map exists, the icon still moves and switches.
        var early = new Icons(); early.World.MapPresent = false;
        Assert(early.Engine.Apply(doc).Outcomes.Values.All(o => o.Applied) && Near(early.At(early.BrysonIcon), BrysonTarget) && !early.CochranIcon.Active, "Icons apply before the map exists: " + early.Where());
        var enabled = new Icons(); enabled.CochranIcon.Active = false; enabled.World.Registered.Remove(enabled.CochranIcon);
        Assert(enabled.Engine.Apply(Doc(Mod("show-cochran", Cochran, CochranTarget, true))).Outcomes.Values.All(o => o.Applied) && enabled.CochranIcon.Active && enabled.World.Registered.Contains(enabled.CochranIcon)
            && Near(enabled.At(enabled.CochranIcon), CochranTarget), "An enabled icon registers with the map at its new place: " + enabled.Where());

        var refused = new Icons(); refused.World.Refused = "The station map icon's scene is not loaded";
        var outcome = refused.Engine.Apply(Doc(Mod("move-bryson", Bryson, BrysonTarget))).Outcomes["move-bryson"];
        Assert(!outcome.Applied && outcome.Error.Contains(refused.World.Refused) && refused.World.Mutations == 0 && Near(refused.At(refused.BrysonIcon), BrysonAuthored), "A preflight refusal leaves the icon untouched: " + outcome.Error);

        var failed = new Icons(); failed.World.ThrowAfterPlacement = true;
        outcome = failed.Engine.Apply(Doc(Mod("move-bryson", Bryson, BrysonTarget, false))).Outcomes["move-bryson"];
        Assert(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal) && Near(failed.At(failed.BrysonIcon), BrysonAuthored)
            && failed.BrysonIcon.Active && failed.World.Registered.Contains(failed.BrysonIcon), "A failure after mutation restores placement and activity: " + outcome.Error + " " + failed.Where());
        // A map that does not take the enabled icon fails verification after mutation, and the icon is restored.
        var dropped = new Icons(); dropped.CochranIcon.Active = false; dropped.World.Registered.Remove(dropped.CochranIcon); dropped.World.DropRegistration = true;
        outcome = dropped.Engine.Apply(Doc(Mod("show-cochran", Cochran, CochranTarget, true))).Outcomes["show-cochran"];
        Assert(!outcome.Applied && outcome.Error.Contains("map does not have the enabled icon registered") && !dropped.CochranIcon.Active && Near(dropped.At(dropped.CochranIcon), CochranAuthored),
            "An unregistered enabled icon fails and is restored: " + outcome.Error + " " + dropped.Where());

        // The move lands and fails; the restoration then cannot move the icon back.
        var stuckWorld = new Icons().World;
        var stuckScene = new SceneCapabilities(() => stuckWorld.MapIds, null); stuckScene.Add(new StationMapIconScene(new StuckAfterMove(stuckWorld)));
        var stuckEngine = new PatchEngine(stuckScene);
        outcome = stuckEngine.Apply(Doc(Mod("move-bryson", Bryson, BrysonTarget))).Outcomes["move-bryson"];
        Assert(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Incomplete cleanup is reported explicitly: " + outcome.Error);
        var mutations = stuckWorld.Mutations;
        var retry = stuckEngine.Apply(Doc(Mod("again", Bryson, BrysonTarget))).Outcomes["again"];
        Assert(!retry.Applied && retry.Error.Contains("Cleanup") && stuckWorld.Mutations == mutations, "The retry on the affected icon is blocked: " + retry.Error);
        // Another icon is not affected by the blocked one.
        Assert(stuckEngine.Apply(Doc(Mod("move-cochran", Cochran, CochranTarget))).Outcomes["move-cochran"].Applied, "Another icon still applies");
    }
    // The first move lands and then fails; every later move of that icon does nothing, so the restoration cannot verify.
    sealed class StuckAfterMove : IMapIconWorld {
        readonly IconWorld world; bool moved;
        public StuckAfterMove(IconWorld world) { this.world = world; }
        public IEnumerable<string> MapIds => world.MapIds;
        public IReadOnlyList<object> Find(string scene, string path) => world.Find(scene, path);
        public string Refusal(object icon) => world.Refusal(icon);
        public string DisableRefusal(object icon) => null;
        public MapIconObservation Observe(object icon) => world.Observe(icon);
        public void SetActive(object icon, bool active) => world.SetActive(icon, active);
        public void SetPlacement(object icon, Placement placement) {
            if (moved && ReferenceEquals(icon, world.Objects[Bryson][0])) { world.Mutations++; return; }
            world.SetPlacement(icon, placement);
            if (ReferenceEquals(icon, world.Objects[Bryson][0])) { moved = true; throw new InvalidOperationException("injected failure after the move"); }
        }
    }

    // In scene patch editing: Undo and Redo of icon edits with matching dirty state and Save availability, Remove
    // modification and Clear activity returning the effective state from the native context and the remaining values,
    // Save at format version 1 with the patcher dependency, a stricter constraint kept, and a reload that reproduces the
    // edits without duplicated state.
    static void IconEditsInScenePatchEditing() {
        var f = new Icons(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        Assert(session.Edit(Id(Bryson), new Placement(BrysonTarget, Upright), null).Applied && session.Edit(Id(Cochran), null, false).Applied && session.Edit(Id(Cochran), new Placement(CochranTarget, Upright), null).Applied,
            "A Bryson move, a Cochran disable and a Cochran move apply");
        Persistence(true, all, "After three edits");
        mapEditor.Undo(); Assert(Near(f.At(f.CochranIcon), CochranAuthored) && !f.CochranIcon.Active, "Undo returns the Cochran move: " + f.Where());
        mapEditor.Undo(); Assert(f.CochranIcon.Active && f.World.Registered.Contains(f.CochranIcon), "Undo returns the Cochran disable: " + f.Where());
        mapEditor.Undo(); Assert(Near(f.At(f.BrysonIcon), BrysonAuthored), "Undo returns the Bryson move: " + f.Where());
        Persistence(false, new string[0], "After Undo back to the unedited icons");
        mapEditor.Redo(); mapEditor.Redo(); mapEditor.Redo();
        Assert(Near(f.At(f.BrysonIcon), BrysonTarget) && Near(f.At(f.CochranIcon), CochranTarget) && !f.CochranIcon.Active && !f.World.Registered.Contains(f.CochranIcon), "Redo reapplies every edit: " + f.Where());
        Persistence(true, all, "After Redo");
        // Clear activity keeps the move: the icon returns to the activity the scene gives it, at its modified place.
        var cleared = session.ClearActivity(Id(Cochran));
        Assert(cleared.Applied && f.CochranIcon.Active && f.World.Registered.Contains(f.CochranIcon) && Near(f.At(f.CochranIcon), CochranTarget), "Clear activity returns the underlying activity and keeps the move: " + cleared.Error + " " + f.Where());
        var removed = session.Remove(Id(Bryson));
        Assert(removed.Applied && Near(f.At(f.BrysonIcon), BrysonAuthored) && f.BrysonIcon.Active, "Remove modification returns the icon to its place in the scene: " + removed.Error + " " + f.Where());
        mapEditor.Undo(); Assert(Near(f.At(f.BrysonIcon), BrysonTarget), "Undo of the removal moves the icon again: " + f.Where());
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=" + StationMapIconRules.RequiredVersion) && (int)JObject.Parse(store.Patch)["formatVersion"] == 1,
            "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        Assert(session.MinimumPatcherVersion == StationMapIconRules.RequiredVersion, "The scene patch file needs the station map icon capability: " + session.MinimumPatcherVersion);
        Persistence(false, new string[0], "After Save");
        var saved = JObject.Parse(store.Patch)["modifications"].Children<JProperty>().Select(p => p.Value).ToArray();
        Assert(saved.Length == 2 && saved.All(m => (string)m["target"]["structure"] == StationMapIconRules.Structure && (string)m["target"]["component"] == StationMapIconRules.Component),
            "Save writes one modification per icon with the family's identity: " + store.Patch);
        var fresh = new Icons();
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Count == 2 && reload.Outcomes.Values.All(o => o.Applied) && Near(fresh.At(fresh.BrysonIcon), BrysonTarget) && Near(fresh.At(fresh.CochranIcon), CochranTarget) && fresh.CochranIcon.Active
            && fresh.World.Registered.Count == 2 && fresh.World.Objects.Values.Sum(list => list.Count) == 2, "A reloaded world reproduces the edits without duplicated state: " + fresh.Where());
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = new Icons(); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(other.Engine, strictEditor, strict); strictEditor.Session = kept;
        Assert(kept.Edit(Id(Bryson), new Placement(BrysonTarget, Upright), null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=" + StationMapIconRules.RequiredVersion),
            "A stricter existing constraint is retained: " + strict.Info);
    }
}
