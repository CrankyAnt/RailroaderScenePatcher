// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The scene spawn points (Character.SpawnPoint) in the loaded scenes. The game reads a spawn point's transform when
    // it starts, rescues or teleports a player, so a placement writes the transform and nothing else follows.
    internal sealed class SpawnPointWorld : ISpawnPointWorld {
        const string SetupType = "Game.Progression.SetupDescriptor", NativeSpawns = "Game.MapSpawns";
        readonly Type spawn;
        internal SpawnPointWorld() {
            spawn = Native.Type(SpawnPoints.Component) ?? throw new InvalidOperationException("Spawn point type unavailable");
            if (spawn.GetField("priority", Native.Flags) == null) throw new MissingFieldException(SpawnPoints.Component, "priority");
            if (SetupsProblem() is string problem) throw new InvalidOperationException(problem);
        }
        // Why the start setups cannot be read, or null: the setup type and the two fields the Object tab reads.
        internal static string SetupsProblem() {
            var setups = Native.Type(SetupType);
            if (setups == null) return SetupType + " is unavailable";
            foreach (var field in new[] { "spawnPoint", "identifier" })
                if (setups.GetField(field, Native.Flags) == null) return SetupType + "." + field + " is missing";
            return null;
        }
        public IEnumerable<string> MapIds => ModScenePatchFiles.MapIds();
        static bool Loaded(GameObject g) => g != null && g.scene.IsValid() && g.scene.isLoaded;
        public IReadOnlyList<object> Find(string scene, string path) =>
            BoardScene.LoadedAt(scene, path).Where(g => Loaded(g) && g.GetComponent(spawn) != null).Cast<object>().ToArray();
        public string Refusal(object target) => Problem(target as GameObject);
        // Why a spawn point cannot be edited now, or null. Shared with the inventory.
        internal static string Problem(GameObject go) {
            var type = Native.Type(SpawnPoints.Component);
            if (type == null) return "The game's spawn point type is unavailable";
            if (!Loaded(go)) return "The spawn point's scene is not loaded";
            if (go.GetComponent(type) == null) return "This object holds no spawn point";
            return null;
        }
        public ObjectState Observe(object target) {
            var go = (GameObject)target;
            var position = NativeInventory.MapPosition(go.transform); var rotation = go.transform.rotation;
            return new ObjectState(new Placement(new double[] { position.x, position.y, position.z }, new double[] { rotation.x, rotation.y, rotation.z, rotation.w }), go.activeSelf);
        }
        public void SetActive(object target, bool active) => ((GameObject)target).SetActive(active);
        public void SetPlacement(object target, Placement placement) {
            var go = (GameObject)target;
            var map = new Vector3(Checked(placement.Position[0]), Checked(placement.Position[1]), Checked(placement.Position[2]));
            var rotation = new Quaternion(Checked(placement.Rotation[0]), Checked(placement.Rotation[1]), Checked(placement.Rotation[2]), Checked(placement.Rotation[3]));
            var moves = NativeInventory.MovesWithOrigin(go.transform);
            var scene = NativeInventory.ScenePosition(map, moves);
            if ((NativeInventory.MapPosition(scene, moves) - map).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Map/origin conversion failed its round trip");
            go.transform.SetPositionAndRotation(scene, rotation);
        }
        static float Checked(double value) { var f = (float)value; if (Single.IsNaN(f) || Single.IsInfinity(f)) throw new InvalidOperationException("Coordinate exceeds native precision"); return f; }
        // The binding lines of a spawn point for the inventory: its name, its priority and every start setup the game
        // can use that names it. The game finds setups with FindObjectsByType, so only setups active in the hierarchy
        // count, as for the starter placement markers. A setup without an identifier is named by its object.
        internal static IEnumerable<string> Bindings(Component point) {
            var lines = new List<string> { SpawnPoints.NameBinding(point.gameObject.name),
                SpawnPoints.PriorityBinding(Convert.ToInt32(point.GetType().GetField("priority", Native.Flags).GetValue(point))) };
            var problem = SetupsProblem();
            if (problem != null) { lines.Add(SpawnPoints.SetupsUnreadBinding(problem)); return lines; }
            var setups = Native.Type(SetupType);
            var held = setups.GetField("spawnPoint", Native.Flags); var id = setups.GetField("identifier", Native.Flags);
            foreach (var setup in Resources.FindObjectsOfTypeAll(setups).OfType<Component>().Where(c => c != null && Loaded(c.gameObject) && c.gameObject.activeInHierarchy).OrderBy(c => c.GetInstanceID()))
                if (ReferenceEquals(held.GetValue(setup), point)) {
                    var name = id.GetValue(setup) as string;
                    var line = SpawnPoints.SetupBinding(String.IsNullOrWhiteSpace(name) ? setup.gameObject.name : name);
                    if (line != null) lines.Add(line);
                }
            return lines;
        }
        // How many spawns the loaded map's data declares, or null when that cannot be read. The game keeps them in
        // MapSpawns.Shared once the map loads, an empty collection for a map without a spawns section; before that it
        // is null, which is unknown, not zero.
        internal static int? NativeSpawnCount() {
            try {
                var type = Native.Type(NativeSpawns);
                var shared = type == null ? null : Native.Read(type, "Shared");
                if (shared == null) return null;
                var count = shared.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public)?.GetValue(shared);
                return count is int value ? value : (int?)null;
            } catch { return null; }
        }
    }
    // Places the spawn points a new game reads before the mods' OnWorldLoad. GlobalGameManager.LoadMap calls
    // StateManager.ApplyNewGameSetup once the map scenes are loaded; it reads the start setup's spawn point transform for
    // a company with no cars and no balance, and right after it InitializeDefaultSpawnIfNeeded reads the first active
    // spawn point when neither that nor native spawns set the start. The prefix applies the spawn point modifications of
    // the applicable scene patch files first, through the engine's own execution; the world load applies every
    // modification again. It never skips the original, and any internal failure leaves the game's own start.
    internal static class NewGameStartHook {
        const string Id = "crankyant.scene-patcher.new-game-start";
        static Harmony harmony;
        static EntryPoint owner;
        // Why the hook is not installed, or null.
        internal static string Error { get; private set; }
        internal static void Install(EntryPoint value) {
            owner = value;
            try {
                var method = typeof(Game.State.StateManager).GetMethod("ApplyNewGameSetup", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(Game.State.NewGameSetup) }, null);
                if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException("StateManager.ApplyNewGameSetup");
                harmony = new Harmony(Id);
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NewGameStartHook), nameof(Before)));
            } catch (Exception e) { harmony?.UnpatchAll(Id); harmony = null; Error = "New game start hook unavailable: " + e.GetBaseException().Message; }
        }
        internal static void Remove() { harmony?.UnpatchAll(Id); harmony = null; owner = null; }
        static void Before() {
            try { owner?.BeforeNewGameStart(); } catch { /* The game's own start stays. */ }
        }
    }
}
