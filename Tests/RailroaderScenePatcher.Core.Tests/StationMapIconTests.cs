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
// physical placement and Enabled. Controlled hierarchies follow the Railroader 0.6.3 scene: the Station Icons holder
// holds one object per station with a UI.Map.MapIcon, an image and a box collider; map labels stand beside it.
static class StationMapIconTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Station map icons: " + message); }
    const string SceneName = "level4", IconsPath = StationMapIconRules.IconsPath, Bryson = IconsPath + "/Bryson", Cochran = IconsPath + "/Cochran", Whittier = IconsPath + "/Whittier";
    static Node Icon(string name) => new Node(name, new Node("Text").With("TMPro.TextMeshProUGUI")).With(StationMapIconRules.Component, "UnityEngine.UI.Image", "UnityEngine.BoxCollider").Solid().Family();
    internal static Node World() => new Node("World",
        new Node("Map Labels", new Node("Bryson").With("UI.Map.MapLabel").Solid(), new Node("Station Icons", Icon("Bryson"), Icon("Cochran"), Icon("Whittier"))),
        new Node("Large Scenery", new Node("Bryson", new Node("Bryson House Track", new Node("Platform").Solid()))));

    public static void Run() {
        GroundMaskTests.HeldMaskFollowsItsHolder("Station map icon", masks => { var icons = new Icons(); return (icons.BrysonIcon, new StationMapIconScene(icons.World, Capability.Unchecked, masks)); }, Doc(Mod("move-icon", Bryson, BrysonTarget)));
        InventoryListsEveryStationMapIconOnce();
        OnlyStationIconsAreListed();
        ContainerChildrenWithoutMapIconsStayOut();
        IconEditsApplyRepeatAndRestore();
        IconEditsInScenePatchEditing();
        RefusalInScenePatchEditingLeavesTheFile();
        MinimumHoldsWithoutTheCapability();
        AStationMapIconCanBeDuplicated();
        TheMapIconShowsAtItsPlaceWhileSelected();
        TheMapIconIsTheGhostWhilePlacing();
        Console.WriteLine("PASS: station map icons");
    }

    // The Station Icons holder is no group: its icons keep their own family, each once. The map label
    // beside it stays out, and nothing in the holder is inspection only.
    static void InventoryListsEveryStationMapIconOnce() {
        var verdicts = SceneObjectRules.Classify(World());
        Assert(!verdicts.Any(v => v.Path == IconsPath), "The Station Icons scene object that only holds other groups has no verdict of its own: " + String.Join(", ", verdicts.Where(v => v.Path == IconsPath).Select(v => v.Disposition + " " + v.Reason)));
        foreach (var path in new[] { Bryson, Cochran, Whittier }) {
            var found = verdicts.Where(v => v.Path == path).ToArray();
            Assert(found.Length == 1 && found[0].Disposition == GroupDisposition.ExistingFamily, path + " keeps its family once: " + String.Join(", ", found.Select(v => v.Disposition + " " + v.Reason)));
        }
        Assert(verdicts.Single(v => v.Path == "World/Map Labels/Bryson").Disposition == GroupDisposition.NotListed, "A map label stays out");
        Assert(!verdicts.Any(v => v.Disposition == GroupDisposition.InspectionOnly), "Nothing is inspection only: " + String.Join(", ", verdicts.Where(v => v.Disposition == GroupDisposition.InspectionOnly).Select(v => v.Path)));

    }

    // Only map icons below the Station Icons holder are station map icons; the capability resolves no other target.
    static void OnlyStationIconsAreListed() {
        Assert(StationMapIconRules.Listed(Bryson), "An icon directly in the scene object that only holds other groups is listed");
        Assert(!StationMapIconRules.Listed(IconsPath + "/Group/Deep") && !StationMapIconRules.Listed(IconsPath + "/"), "An icon in a subgroup of the scene object that only holds other groups is not listed");
        Assert(!StationMapIconRules.Listed(IconsPath) && !StationMapIconRules.Listed("World/Map Labels/Bryson") && !StationMapIconRules.Listed("Car/Map Icon") && !StationMapIconRules.Listed(null)
            && !StationMapIconRules.Listed(IconsPath + " (1)/Bryson"), "The scene object that only holds other groups itself, map labels and runtime icons are not listed");
        var f = new Icons();
        Assert(f.Scene.Resolve(Id(Bryson)).Count == 1, "A station map icon resolves");
        Assert(f.Scene.Resolve(new TargetIdentity(SceneName, Bryson, SceneObjects.ComponentType, SceneObjects.Structure)).Count == 0
            && f.Scene.Resolve(new TargetIdentity(SceneName, Bryson, StationMapIconRules.Component, ChildObjects.Structure)).Count == 0, "Another component or structure does not resolve");
        f.World.Objects["World/Map Labels/Bryson"] = new List<object> { new FakeIcon() };
        Assert(f.Scene.Resolve(new TargetIdentity(SceneName, "World/Map Labels/Bryson", StationMapIconRules.Component, StationMapIconRules.Structure)).Count == 0, "A map icon outside the scene object that only holds other groups does not resolve");
        // A group elsewhere that holds a map icon is not moved as a whole, without a ticket.
        var held = SceneObjectRules.Classify(new Node("World", new Node("Large Scenery", new Node("Town", new Node("Depot", new Node("Icon").With(StationMapIconRules.Component), new Node("Shell").Solid())))))
            .Single(v => v.Path == "World/Large Scenery/Town/Depot");
        Assert(held.Disposition == GroupDisposition.InspectionOnly && held.Reason == SceneObjectRules.MapIconReason
            && CommandBand.GroupInspectionOnly(held, SceneObjectRules.Family) == "Not editable as a group: " + SceneObjectRules.MapIconReason, "A group with a map icon elsewhere states why it is left out: " + held.Reason);
        // That reason promises no edit: such a map icon is in no family.
        Assert(SceneObjectRules.MapIconReason.IndexOf("family", StringComparison.OrdinalIgnoreCase) < 0 && SceneObjectRules.MapIconReason.IndexOf("edited", StringComparison.OrdinalIgnoreCase) < 0,
            "The map icon reason promises no edit: " + SceneObjectRules.MapIconReason);
        // A station click spot is native map data in Railroader 0.6.3: a settled rule without a ticket.
        var spot = SceneObjectRules.Classify(new Node("World", new Node("Large Scenery", new Node("Town", new Node("Depot", new Node("Click").With("Model.StationAgent"), new Node("Shell").Solid())))))
            .Single(v => v.Path == "World/Large Scenery/Town/Depot");
        Assert(spot.Reason == SceneObjectRules.StationAgentReason && CommandBand.GroupInspectionOnly(spot, SceneObjectRules.Family) == "Not editable as a group: " + SceneObjectRules.StationAgentReason,
            "A group with a station click spot reads Not editable as a group: " + spot.Reason);
        Assert(CommandBand.InspectionOnly(StationMapIconRules.Family, true) == null, "A station map icon is a supported kind");
        Assert(RecoveryGuidance.Guide("Object", null, StationMapIconRules.Structure).Value == "moving-station-map-icons.md", "A failed icon edit opens the station map icon guide");
    }

    // Only a direct child of the Station Icons holder that carries a map icon is an icon. A child without one is not
    // listed, and neither is a subgroup that holds an icon: no editable group, no inspection-only row, and no row for the
    // icon inside it, which Listed does not accept either.
    static void ContainerChildrenWithoutMapIconsStayOut() {
        var verdicts = SceneObjectRules.Classify(new Node("World", new Node("Map Labels", new Node("Station Icons",
            Icon("Bryson"),
            new Node("Backdrop").With("UnityEngine.MeshRenderer").Solid(),
            new Node("Group", Icon("Nested")).Solid()))));
        Assert(verdicts.Single(v => v.Path == Bryson).Disposition == GroupDisposition.ExistingFamily, "The icon keeps its family");
        foreach (var path in new[] { IconsPath + "/Backdrop", IconsPath + "/Group" }) {
            var verdict = verdicts.SingleOrDefault(v => v.Path == path);
            Assert(verdict?.Disposition == GroupDisposition.NotListed && verdict.Reason == SceneObjectRules.MapLabel, path + " is not listed: " + verdict?.Disposition + " " + verdict?.Reason);
        }
        Assert(!verdicts.Any(v => v.Path == IconsPath + "/Group/Nested"), "The icon in a subgroup gets no verdict");
        Assert(!verdicts.Any(v => v.Disposition == GroupDisposition.Group || v.Disposition == GroupDisposition.InspectionOnly), "Nothing in the scene object that only holds other groups is an editable or inspection-only group");
        Assert(!StationMapIconRules.Listed(IconsPath + "/Group/Nested"), "The nested icon is in no family");
    }

    // A preflight refusal in scene patch editing changes neither the icon nor the scene patch file, and nothing waits to be saved.
    static void RefusalInScenePatchEditingLeavesTheFile() {
        var f = new Icons(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        Assert(session.Edit(Id(Cochran), new Placement(CochranTarget, Upright), null).Applied && mapEditor.Save() == null, "A first edit applies and saves");
        var saved = store.Patch; var info = store.Info;
        f.World.Refused = "The station map icon's scene isn't loaded."; var mutations = f.World.Mutations;
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
            scene.AddUnavailable(StationMapIconRules.Structure, "The map icon types are unavailable.");
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
    // What builds a mod object in the controlled icon world: a new icon in the same holder, which registers with the
    // map as an icon does when it becomes active. No station names it and no switch of the game holds it.
    sealed class IconBuilder : IModObjectScene {
        readonly IconWorld world;
        public IconBuilder(IconWorld world) { this.world = world; }
        public object Build(TargetIdentity identity, object source, Placement placement) {
            if (world.Objects.ContainsKey(identity.Path)) throw new InvalidOperationException(ModObjects.Taken);
            var made = new FakeIcon { Position = new[] { placement.Position[0] - world.Origin, placement.Position[1], placement.Position[2] }, Rotation = (double[])placement.Rotation.Clone(), Parent = ((FakeIcon)source).Parent };
            world.Objects[identity.Path] = new List<object> { made }; world.Registered.Add(made);
            return made;
        }
        public void Remove(object modObject) { foreach (var pair in world.Objects.Where(p => p.Value.Contains(modObject)).ToArray()) world.Objects.Remove(pair.Key); world.Registered.Remove(modObject); }
    }
    // A station map icon is duplicated as a mod object: a load builds it at its placement and the map shows it; it is a
    // station map icon for its edits, names no place and leaves the source icon as it is. Remove modification takes
    // it off the map again.
    static void AStationMapIconCanBeDuplicated() {
        var f = new Icons(); f.Capabilities.ModObjectWorld = new IconBuilder(f.World);
        string Named(string path) => "{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"" + StationMapIconRules.Component + "\",\"structure\":\"" + StationMapIconRules.Structure + "\"}";
        var made = ModObjects.Path(Bryson, "author/pack", "mod-object-bryson");
        var doc = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"mod-object-bryson\":{\"target\":" + Named(made) + ",\"source\":" + Named(Bryson) +
            ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", CochranTarget.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}";
        var result = f.Engine.Apply(doc, "author/pack");
        Assert(result.Outcomes["mod-object-bryson"].Applied, "The duplicated icon applies: " + result.Outcomes["mod-object-bryson"].Error);
        var icon = (FakeIcon)f.World.Objects[made].Single();
        Assert(Near(f.At(icon), CochranTarget) && f.World.Registered.Contains(icon), "The duplicated icon stands at its placement and the map shows it");
        Assert(Near(f.At(f.BrysonIcon), BrysonAuthored) && f.World.Registered.Contains(f.BrysonIcon), "The source icon stays as it is: " + f.Where());
        Assert(StationMapIconRules.Listed(made) && !ScenePlaces.SourcesOf(made, new[] { CochranTarget[0], CochranTarget[2] }).Any(), "It is a station map icon that names no place and no station");
        // It takes the edits of its family, and Remove modification takes it off the map.
        var session = new PatchEditingSession(f.Engine, new NativeMapEditor(), new Store { Patch = doc }, "author/pack", "author/pack");
        Assert(session.Edit(Id(made), null, false).Applied && !icon.Active && !f.World.Registered.Contains(icon), "A duplicated icon is disabled like any station map icon");
        Assert(session.Remove(Id(made)).Applied && !f.World.Objects.ContainsKey(made) && f.World.Registered.Contains(f.BrysonIcon), "Remove modification removes the duplicated icon and leaves the source");
    }
    // The world view of the map icon tests: a 2560 by 1440 screen and a camera at eye looking along +Z with a 90 degree
    // vertical field, in Unity's clip convention.
    static readonly ScreenRect View = new ScreenRect(0, 0, 2560, 1440);
    static Func<IReadOnlyList<double>, ClipPoint> CameraAt(double x, double y, double z) =>
        p => new ClipPoint((p[0] - x) * View.Height / View.Width, p[1] - y, p[2] - z - 0.6, p[2] - z);
    static bool At(ScreenRect? rect, double left, double top, double right, double bottom) =>
        rect.HasValue && Math.Abs(rect.Value.X - left) < .01 && Math.Abs(rect.Value.Y - top) < .01 && Math.Abs(rect.Value.Right - right) < .01 && Math.Abs(rect.Value.Bottom - bottom) < .01;
    static string Show(ScreenRect? rect) => rect.HasValue ? rect.Value.X + "," + rect.Value.Y + "-" + rect.Value.Right + "," + rect.Value.Bottom : "none";
    static SceneObservation Observed(Icons f, FakeIcon icon, string path) {
        var state = f.World.Observe(icon).Root;
        return new SceneObservation(icon, Id(path), StationMapIconRules.Family, state.Active != false, state.Placement.Position, state.Placement.Rotation, new string[0], new string[0], new string[0], StationMapIconRules.Operations);
    }

    // While a station map icon is selected, the world shows the map's icon centred on its place: 40 pixels high and as
    // wide as the icon's image is for that height. It follows a move, shows also for an icon that is switched off,
    // and reading it changes neither the icon's activity nor its registration with the map, so the progression switch
    // stays as it is. Other families show no map icon, and an icon behind the camera is not drawn.
    static void TheMapIconShowsAtItsPlaceWhileSelected() {
        var f = new Icons();
        var bryson = Observed(f, f.BrysonIcon, Bryson);
        Assert(StationMapIconRules.ShowsMapIcon(bryson), "A selected station map icon shows the map's icon in the world");
        var group = new SceneObservation(new object(), new TargetIdentity(SceneName, "World/Large Scenery/Bryson", "UnityEngine.Transform", SceneObjects.Structure), SceneFamilies.SceneObject, true,
            new[] { 1000.0, 80, 400 }, Upright, new string[0], new string[0], new string[0], "Placement");
        Assert(!StationMapIconRules.ShowsMapIcon(group) && !StationMapIconRules.ShowsMapIcon(null), "A scene object shows no map icon");
        var eye = CameraAt(1000, 80, 300);
        var icon = StationMapIconRules.WorldIcon(eye(f.At(f.BrysonIcon)), View, 1);
        Assert(At(icon, 1260, 700, 1300, 740), "The square icon stands centred on Bryson's place: " + Show(icon));
        var wide = StationMapIconRules.WorldIcon(eye(f.At(f.BrysonIcon)), View, 2);
        Assert(At(wide, 1240, 700, 1320, 740), "An icon image twice as wide as high is drawn 80 by 40 pixels: " + Show(wide));
        var session = new PatchEditingSession(f.Engine, new NativeMapEditor(), new Store(), "author/pack", "author/pack");
        Assert(session.Edit(Id(Bryson), new Placement(BrysonTarget, Upright), null).Applied, "The Bryson icon moves");
        var moved = StationMapIconRules.WorldIcon(eye(f.At(f.BrysonIcon)), View, 1);
        Assert(At(moved, 2385, 700, 2425, 740), "The icon follows the move to its new place: " + Show(moved));
        Assert(session.Edit(Id(Bryson), null, false).Applied, "The Bryson icon is switched off");
        var off = Observed(f, f.BrysonIcon, Bryson);
        Assert(StationMapIconRules.ShowsMapIcon(off) && At(StationMapIconRules.WorldIcon(eye(off.Position), View, 1), 2385, 700, 2425, 740),
            "A switched-off icon still shows the map's icon at its place while it is selected");
        Assert(!f.BrysonIcon.Active && !f.World.Registered.Contains(f.BrysonIcon) && f.CochranIcon.Active && f.World.Registered.Contains(f.CochranIcon),
            "Showing the map's icon leaves activity and registration as the edits and the game set them: " + f.Where());
        Assert(StationMapIconRules.WorldIcon(CameraAt(1000, 80, 600)(f.At(f.BrysonIcon)), View, 1) == null, "An icon behind the camera is not drawn");
    }

    // The pointer while a duplicated station map icon is placed: over the world at (1280, 900), where the ground's map
    // point is (1600, 75, 900).
    sealed class GroundPointer : IPlacingPointer {
        public double[] Pointer => new[] { 1280.0, 900 };
        public double[] WorldClick => null;
        public double[] MapPoint(double[] screen) => new[] { 1600.0, 75, 900 };
    }
    // While a duplicated station map icon is being placed, the map's icon is the ghost: it stands centred on the
    // ground under the pointer and moves with it. The ghost of a scene object is its meshes, never a map icon.
    static void TheMapIconIsTheGhostWhilePlacing() {
        var f = new Icons();
        var duplicate = new DuplicateState();
        duplicate.Begin(Observed(f, f.BrysonIcon, Bryson));
        Assert(DuplicateState.Reason(duplicate.Source, new string[0]) == null, "A station map icon can be duplicated");
        duplicate.Place();
        Assert(duplicate.Tick(new GroundPointer(), (from, placement) => throw new InvalidOperationException("nothing is clicked")) == null, "Moving the pointer places nothing");
        Assert(StationMapIconRules.ShowsMapIcon(duplicate.Source) && duplicate.Ghost != null, "The ghost of a duplicated station map icon is the map's icon");
        var ghost = StationMapIconRules.WorldIcon(CameraAt(1600, 75, 800)(duplicate.Ghost), View, 1);
        Assert(At(ghost, 1260, 700, 1300, 740), "The map's icon stands centred on the ground under the pointer: " + Show(ghost));
        Assert(f.World.Objects.Values.Sum(list => list.Count) == 2 && f.BrysonIcon.Active && f.World.Registered.Count == 2, "Nothing is built while the ghost is on the pointer: " + f.Where());
        duplicate.Release();
        Assert(duplicate.Ghost == null, "Release takes the map's icon off the pointer");
    }
    sealed class Icons {
        public readonly IconWorld World = new IconWorld();
        public readonly FakeIcon BrysonIcon = new FakeIcon { Position = new[] { 900.0, 80, 400 } }, CochranIcon = new FakeIcon { Position = new[] { -300.0, 80, 1200 } };
        public readonly StationMapIconScene Scene;
        public readonly SceneCapabilities Capabilities;
        public readonly PatchEngine Engine;
        public Icons() {
            World.Objects[Bryson] = new List<object> { BrysonIcon }; World.Objects[Cochran] = new List<object> { CochranIcon };
            World.Registered.Add(BrysonIcon); World.Registered.Add(CochranIcon);
            var scene = Capabilities = new SceneCapabilities(() => World.MapIds, null);
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

        var refused = new Icons(); refused.World.Refused = "The station map icon's scene isn't loaded.";
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
