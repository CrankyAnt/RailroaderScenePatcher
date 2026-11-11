// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// One bridge root with child colliders, and one separate track-owned trestle beside it.
class FakeCollider {
    public string Id; public double[] Offset; public bool Enabled = true, ChainActive = true;
    public string Shape = "BoxCollider size=1,1,1";
}
class FakeBridge {
    public double[] Position = { 50, 5, 7 }, Rotation = { 0, 0, 0, 1 };
    public bool Active = true, ParentActive = true;
    public List<FakeCollider> Colliders = new List<FakeCollider> {
        new FakeCollider { Id = "Steel#1", Offset = new[] { 0.0, 2.0, 0.0 } },
        new FakeCollider { Id = "Abutments#2", Offset = new[] { 10.0, 0.0, 0.0 }, Shape = "MeshCollider mesh=7" }
    };
}
// Controlled world: map coordinates are world coordinates plus Origin on X.
class FakeBridgeWorld : IBridgeWorld {
    public string[] Maps = { "map" };
    public IEnumerable<string> MapIds => Maps;
    public Dictionary<string, List<object>> Objects = new Dictionary<string, List<object>>();
    public double Origin = 1000;
    public double[] TrestlePosition = { 60, 4, 7 };
    public string PatternError;
    public int Mutations;
    // Failure injections.
    public bool ThrowAfterPose, DesyncOnPose, MoveTrestleOnPose, StaleBoundsOnPose, KeepDesync;
    FakeCollider desynced; double[] desyncOffset;
    bool staleBounds;
    public IReadOnlyList<object> Resolve(TargetIdentity identity) {
        if (identity.Component != "Helpers.Culling.RendererCuller" || (identity.Structure != null && identity.Structure != BridgeScene.Structure)) return Array.Empty<object>();
        return Objects.TryGetValue(identity.Path, out var found) ? found : new List<object>();
    }
    public void CheckPattern(object bridge) { if (PatternError != null) throw new InvalidOperationException(PatternError); }
    public BridgeObservation Observe(object target) {
        var bridge = (FakeBridge)target;
        var colliders = bridge.Colliders.Select(c => new ColliderState(c.Id, c.Shape, c.Enabled, c.ChainActive,
            bridge.ParentActive && bridge.Active && c.ChainActive, c.Offset, new double[] { 0, 0, 0, 1 }, !staleBounds));
        return new BridgeObservation(new ObjectState(new Placement(new[] { bridge.Position[0] + Origin, bridge.Position[1], bridge.Position[2] }, (double[])bridge.Rotation.Clone()), bridge.Active),
            bridge.ParentActive && bridge.Active, colliders, "trestle " + String.Join(",", TrestlePosition));
    }
    public void SetActive(object target, bool active) { Mutations++; ((FakeBridge)target).Active = active; }
    public void SetPose(object target, Placement placement) {
        Mutations++;
        var bridge = (FakeBridge)target;
        bridge.Position = new[] { placement.Position[0] - Origin, placement.Position[1], placement.Position[2] };
        bridge.Rotation = (double[])placement.Rotation.Clone();
        // A pose change always resynchronises an earlier desync unless the injection keeps it.
        if (desynced != null && !KeepDesync) { desynced.Offset = desyncOffset; desynced = null; }
        // Pose injections fire once; a moved trestle keeps moving and cannot be restored.
        staleBounds = StaleBoundsOnPose; StaleBoundsOnPose = false;
        if (DesyncOnPose && desynced == null) { desynced = bridge.Colliders[0]; desyncOffset = desynced.Offset; desynced.Offset = new[] { 3.0, 3.0, 3.0 }; DesyncOnPose = false; }
        if (MoveTrestleOnPose) TrestlePosition = new[] { TrestlePosition[0] + 5, TrestlePosition[1], TrestlePosition[2] };
        if (ThrowAfterPose) { ThrowAfterPose = false; throw new Exception("injected after pose"); }
    }
}
static class BridgeTests {
    const string BridgePath = "World/Large Scenery/Ela/Ela Bridge";
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Bridge(string id, double x = 1055, string rotation = "[0,0.130526,0,0.991445]", string deps = "[]", string path = BridgePath, string extra = "") =>
        "{\"id\":\"" + id + "\",\"target\":{\"scene\":\"BushnellWhittier-MapManager\",\"path\":\"" + path + "\",\"component\":\"Helpers.Culling.RendererCuller\",\"structure\":\"bridge-colliders-v1\"}," +
        "\"placement\":{\"mode\":\"fixed\",\"position\":[" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",5,7],\"rotation\":" + rotation + "}" + extra + ",\"dependsOn\":" + deps + "}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":[" + items + "]}";
    static (FakeBridgeWorld world, FakeBridge bridge, BridgeScene scene) Setup() {
        var world = new FakeBridgeWorld(); var bridge = new FakeBridge();
        world.Objects[BridgePath] = new List<object> { bridge };
        return (world, bridge, new BridgeScene(world, Capability.Unchecked));
    }
    static double[] Offsets(FakeBridge bridge) => bridge.Colliders.SelectMany(c => c.Offset).ToArray();
    public static void Run() {
        FixedPlacement();
        MapMismatchRejectsBeforeResolution();
        TargetResolutionIsExact();
        DependenciesRunAcrossCapabilitiesRegardlessOfOrder();
        RepeatedApplicationConverges();
        ActivityTakesCollidersAlong();
        PreflightRefusalLeavesStateUnchanged();
        FailureAfterMutationRestoresPoseAndColliders();
        UnverifiedCleanupBlocksRetry();
        IncompatibleBridgeCapabilityIsIsolated();
        EditorUndoRedoAndRemovalRestoreBridge();
    }
    static void MapMismatchRejectsBeforeResolution() {
        var (world, bridge, scene) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Bridge("place-ela-bridge"), "other-map"));
        Assert(result.Error != null && result.Outcomes.Count == 0 && world.Mutations == 0 && bridge.Position[0] == 50, "A document for another map is rejected without touching the bridge");
    }
    static void TargetResolutionIsExact() {
        var (world, bridge, scene) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Bridge("missing", path: BridgePath + "/Steel")));
        Assert(!result.Outcomes["missing"].Applied && world.Mutations == 0, "A path without a bridge does not resolve to a nearby object");
        world.Objects["World/Twin"] = new List<object> { new FakeBridge(), new FakeBridge() };
        result = new PatchEngine(scene).Apply(Doc(Bridge("twin", path: "World/Twin")));
        Assert(!result.Outcomes["twin"].Applied && result.Outcomes["twin"].ResolvedTarget == null && world.Mutations == 0, "An ambiguous bridge identity fails");
        var board = Bridge("board-structure").Replace("bridge-colliders-v1", "independent-board-v1");
        result = new PatchEngine(scene).Apply(Doc(board));
        Assert(!result.Outcomes["board-structure"].Applied && world.Mutations == 0, "Another pattern's structure does not resolve as a bridge");
    }
    static void DependenciesRunAcrossCapabilitiesRegardlessOfOrder() {
        var (world, bridge, bridgeScene) = Setup();
        var boardScene = new Scene { Maps = new[] { "map" } }; var box = new Box(); boardScene.Objects["p"] = new List<object> { box };
        var capabilities = new SceneCapabilities(() => new[] { "map" });
        capabilities.Add(boardScene); capabilities.Add(bridgeScene);
        var board = "{\"id\":\"board\",\"target\":{\"scene\":\"s\",\"path\":\"p\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var result = new PatchEngine(capabilities).Apply(Doc(Bridge("bridge", deps: "[\"board\"]") + "," + board));
        Assert(result.Outcomes["bridge"].Applied && result.Outcomes["board"].Applied && box.X == 12 && bridge.Position[0] == 55, "A bridge operation runs after its prerequisite listed later");
        bridge.Position[0] = 50;
        var missing = board.Replace("\"path\":\"p\"", "\"path\":\"absent\"");
        result = new PatchEngine(capabilities).Apply(Doc(Bridge("bridge", deps: "[\"board\"]") + "," + missing));
        Assert(!result.Outcomes["bridge"].Applied && bridge.Position[0] == 50, "A failed prerequisite isolates the dependent bridge operation");
    }
    static void RepeatedApplicationConverges() {
        var (world, bridge, scene) = Setup();
        var offsets = Offsets(bridge); var engine = new PatchEngine(scene);
        engine.Apply(Doc(Bridge("place-ela-bridge")));
        var second = engine.Apply(Doc(Bridge("place-ela-bridge")));
        Assert(second.Outcomes["place-ela-bridge"].Applied && bridge.Position.SequenceEqual(new[] { 55.0, 5, 7 }) && Offsets(bridge).SequenceEqual(offsets), "Repeated application keeps the absolute pose and collider layout");
        world.Origin = 1010;
        Assert(new PatchEngine(scene).Apply(Doc(Bridge("place-ela-bridge"))).Outcomes["place-ela-bridge"].Applied && bridge.Position[0] == 45, "A new load at another origin reaches the same map pose");
        Assert(world.TrestlePosition.SequenceEqual(new[] { 60.0, 4, 7 }), "Repetition never moves the separate trestle");
    }
    static void ActivityTakesCollidersAlong() {
        var (world, bridge, scene) = Setup();
        var engine = new PatchEngine(scene);
        var disable = Doc(Bridge("disable-bridge", extra: ",\"active\":false"));
        Assert(engine.Apply(disable).Outcomes["disable-bridge"].Applied && !bridge.Active, "An explicit disable deactivates the bridge root");
        Assert(world.Observe(bridge).Colliders.All(c => !c.ActiveInHierarchy && c.Enabled), "Disabled bridge colliders stop colliding without changing their own flags");
        bridge.Active = true;
        Assert(engine.Apply(disable).Outcomes["disable-bridge"].Applied && !bridge.Active, "Repeated application keeps the explicit disable after a later activation");
        var enable = Doc(Bridge("enable-bridge", extra: ",\"active\":true"));
        Assert(new PatchEngine(scene).Apply(enable).Outcomes["enable-bridge"].Applied && world.Observe(bridge).Colliders.All(c => c.ActiveInHierarchy), "Enabling restores collision with the root");
    }
    static void PreflightRefusalLeavesStateUnchanged() {
        var (world, bridge, scene) = Setup();
        world.PatternError = "Unexpected track-owned component in the bridge hierarchy";
        var result = new PatchEngine(scene).Apply(Doc(Bridge("place-ela-bridge")));
        Assert(!result.Outcomes["place-ela-bridge"].Applied && result.Outcomes["place-ela-bridge"].Error.Contains("track-owned"), "Preflight reports the pattern refusal");
        Assert(world.Mutations == 0 && bridge.Position[0] == 50 && bridge.Active, "A refused bridge is not touched");
        world.PatternError = null; bridge.Colliders.RemoveAt(1);
        var scene2 = new BridgeScene(world, Capability.Unchecked);
        scene2.Capture(bridge); bridge.Colliders.Add(new FakeCollider { Id = "Added#9", Offset = new[] { 0.0, 0, 0 } });
        result = new PatchEngine(scene2).Apply(Doc(Bridge("place-ela-bridge")));
        Assert(!result.Outcomes["place-ela-bridge"].Applied && world.Mutations == 0, "A collider layout that changed since its first observation refuses mutation");
    }
    static void FailureAfterMutationRestoresPoseAndColliders() {
        foreach (var inject in new Action<FakeBridgeWorld>[] { w => { w.ThrowAfterPose = true; w.DesyncOnPose = true; }, w => w.DesyncOnPose = true, w => w.StaleBoundsOnPose = true }) {
            var (world, bridge, scene) = Setup();
            var offsets = Offsets(bridge); inject(world);
            var engine = new PatchEngine(scene);
            var result = engine.Apply(Doc(Bridge("place-ela-bridge")));
            var outcome = result.Outcomes["place-ela-bridge"];
            Assert(!outcome.Applied && outcome.Error.StartsWith("Mutation failed and was restored", StringComparison.Ordinal), "Injected failure after mutation is restored: " + outcome.Error);
            Assert(bridge.Position.SequenceEqual(new[] { 50.0, 5, 7 }) && bridge.Rotation.SequenceEqual(new[] { 0.0, 0, 0, 1 }) && Offsets(bridge).SequenceEqual(offsets), "Captured pose and collider layout are restored");
            world.ThrowAfterPose = world.DesyncOnPose = world.StaleBoundsOnPose = false;
            Assert(!engine.Apply(Doc(Bridge("place-ela-bridge"))).Outcomes["place-ela-bridge"].Applied, "A failed bridge operation is not retried in the same load");
        }
    }
    static void UnverifiedCleanupBlocksRetry() {
        foreach (var inject in new Action<FakeBridgeWorld>[] { w => { w.DesyncOnPose = true; w.KeepDesync = true; }, w => w.MoveTrestleOnPose = true }) {
            var (world, bridge, scene) = Setup();
            inject(world);
            var engine = new PatchEngine(scene);
            var outcome = engine.Apply(Doc(Bridge("place-ela-bridge"))).Outcomes["place-ela-bridge"];
            Assert(!outcome.Applied && outcome.Error.Contains("cleanup is unverified"), "Incomplete bridge cleanup is reported: " + outcome.Error);
            world.DesyncOnPose = world.KeepDesync = world.MoveTrestleOnPose = false;
            var mutations = world.Mutations;
            var retry = engine.Apply(Doc(Bridge("another-edit", 1060))).Outcomes["another-edit"];
            Assert(!retry.Applied && retry.Error.Contains("cleanup") && world.Mutations == mutations, "Another operation on the unverified bridge is blocked");
        }
    }
    static void IncompatibleBridgeCapabilityIsIsolated() {
        var boardScene = new Scene { Maps = new[] { "map" } }; var box = new Box(); boardScene.Objects["p"] = new List<object> { box };
        var capabilities = new SceneCapabilities(() => new[] { "map" });
        capabilities.Add(boardScene); capabilities.AddUnavailable(BridgeScene.Structure, "RendererCuller.RequestUpdateCullingPosition is missing");
        var board = "{\"id\":\"board\",\"target\":{\"scene\":\"s\",\"path\":\"p\",\"component\":\"c\"},\"placement\":{\"mode\":\"fixed\",\"position\":[112,2,3],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}";
        var result = new PatchEngine(capabilities).Apply(Doc(Bridge("bridge") + "," + board));
        Assert(result.Error == null && result.Outcomes["board"].Applied && box.X == 12, "Unrelated supported work stays available");
        Assert(!result.Outcomes["bridge"].Applied && result.Outcomes["bridge"].Error.Contains("incompatible with this game build"), "The bridge operation reports the incompatible capability: " + result.Outcomes["bridge"].Error);
        Assert(new SceneCapabilities(() => new[] { "map" }).Capability == Capability.Incompatible, "Without any capability nothing can be placed");
    }
    static void EditorUndoRedoAndRemovalRestoreBridge() {
        var (world, bridge, scene) = Setup();
        var offsets = Offsets(bridge);
        var store = new Store(); var editor = new Editor();
        var session = new AuthoringSession(new PatchEngine(scene), editor, store, "map"); editor.Session = session;
        var identity = new TargetIdentity("BushnellWhittier-MapManager", BridgePath, "Helpers.Culling.RendererCuller", BridgeScene.Structure);
        var moved = new Placement(new[] { 1055.0, 5, 7 }, new[] { 0, 0.130526, 0, 0.991445 });
        Assert(session.Edit(identity, moved, null).Applied && bridge.Position[0] == 55 && Offsets(bridge).SequenceEqual(offsets), "An editor bridge placement moves the root with its colliders");
        var item = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(session.DocumentText)["modifications"][0];
        Assert((string)item["target"]["structure"] == BridgeScene.Structure && item["placement"] != null && item["active"] == null, "The document records bridge placement, not a binding or activity");
        Assert(session.MinimumPatcherVersion == "0.3.0", "A bridge modification requires the patcher version that introduced the bridge capability");
        Assert(session.Edit(identity, null, false).Applied && !bridge.Active, "An editor disable deactivates the bridge");
        editor.Undo(); Assert(bridge.Active && bridge.Position[0] == 55, "Undo re-enables the moved bridge");
        editor.Undo(); Assert(bridge.Position.SequenceEqual(new[] { 50.0, 5, 7 }) && bridge.Rotation[3] == 1 && Offsets(bridge).SequenceEqual(offsets), "Undo restores the prior bridge pose and colliders");
        editor.Redo(); Assert(bridge.Position[0] == 55, "Redo reapplies the bridge placement");
        Assert(session.Remove(identity).Applied && bridge.Position[0] == 50 && bridge.Active && Offsets(bridge).SequenceEqual(offsets), "Removal returns the bridge to its effective underlying state");
        Assert(world.TrestlePosition.SequenceEqual(new[] { 60.0, 4, 7 }), "No editor route moves the separate trestle");
        Assert(editor.Save() == null && !store.Info.Contains("crankyant/scene-patcher"), "Save after removal writes no dependency for an empty document");
        editor.Undo(); Assert(bridge.Position[0] == 55, "Undo of the removal restores the authored bridge placement");
        Assert(editor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.3.0"), "Save raises the dependency to the bridge capability version: " + store.Info);
        var fresh = new FakeBridge(); var replay = new FakeBridgeWorld(); replay.Objects[BridgePath] = new List<object> { fresh };
        var reload = new PatchEngine(new BridgeScene(replay, Capability.Unchecked)).Apply(store.Patch);
        Assert(reload.Outcomes.Values.Single().Applied && fresh.Position[0] == 55 && fresh.Colliders.Count == 2, "A reloaded world reproduces the saved bridge placement without duplicated colliders");
    }
    static void FixedPlacement() {
        var (world, bridge, scene) = Setup();
        var offsets = Offsets(bridge);
        var result = new PatchEngine(scene).Apply(Doc(Bridge("place-ela-bridge")));
        Assert(result.Error == null && result.Outcomes["place-ela-bridge"].Applied, "Bridge placement applies: " + result.Error + result.Outcomes["place-ela-bridge"].Error);
        Assert(bridge.Position.SequenceEqual(new[] { 55.0, 5, 7 }) && bridge.Rotation[1] == 0.130526, "Bridge root takes the authored map pose through the current origin");
        Assert(Offsets(bridge).SequenceEqual(offsets) && bridge.Colliders.All(c => c.Enabled), "Colliders keep their identity and follow the root");
        Assert(world.TrestlePosition.SequenceEqual(new[] { 60.0, 4, 7 }), "Separate trestle stays where it was");
    }
}
