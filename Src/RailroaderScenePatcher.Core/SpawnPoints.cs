// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The scene spawn points (Character.SpawnPoint), such as those under World/POIs: what the game uses them for, how
    // the inventory lists them and the Object tab names their uses. Spawns in native map data (Map.json) are another
    // kind: the native map editor edits them in its Spawns category, and the inventory does not list them.
    //
    // What the game reads (Railroader 0.6.3): SpawnPoint.All finds the active spawn points and sorts them by priority,
    // highest first. A new game starts at the native spawns when the map declares them, and otherwise at the first
    // of SpawnPoint.All (GlobalGameManager.InitializeDefaultSpawnIfNeeded). An active company setup names a spawn point;
    // when the game applies the setup to a company with no cars and no balance, it reads that spawn point's transform
    // for the start, whatever its activity (StateManager.ApplyNewGameSetup). Whether that start or the native spawns
    // win when both exist is not settled by the source. Both new-game reads run before the mods' OnWorldLoad, so the
    // patcher applies the spawn point modifications first (PatchEngine.BeforeNewGameStart). None of this is observed in
    // Railroader yet, so the Object tab states it as expected behaviour.
    // A stuck player is moved to the closest active spawn point, and to a native spawn only when there is none
    // (PlayerController, SpawnPoint.TryClosestTo). The /tp console command lists the active spawn points by name and
    // jumps to where each stands (TeleportCommand). Quick search lists them too (QuickSearchController.AllItems), but
    // its Show() is empty in Railroader 0.6.3, so it never opens there. Each reads the transform when it runs, so a
    // moved spawn point is used at its new place.
    public static class SpawnPoints {
        public const string Family = "Spawn point", Component = "Character.SpawnPoint", Structure = "spawn-point-v1", RequiredVersion = "0.5.0";
        public const string Operations = "Move, rotate, enable or disable this spawn point. Native spawns in the map data stay with the Map Editor's Spawns category.";
        public const string Dependency = "Player start, rescue and teleport read this spawn point's placement when they run; only an active spawn point is offered, and a start setup reads it whatever its activity";
        // The family of a component below a scene root of this name, or null: every scene spawn point that the inventory
        // can list at all, wherever it stands.
        public static string FamilyOf(string componentType, string sceneRoot) =>
            componentType == Component && BakedSceneryRules.ListedBelow(sceneRoot, false) ? Family : null;
        // The binding lines of a spawn point: the name teleport offers, its priority for the default
        // start, and each start setup that names it, by that setup's identifier. A setup without an identifier has no
        // line here: the adapter names it by its object, as the starter placement markers do. When the setups cannot
        // be read, one line says so instead, so the Object tab never reads that no setup names the spawn point.
        const string NamePrefix = "Spawn point name=", PriorityPrefix = "Spawn point priority=", SetupPrefix = "Start setup=", SetupsUnreadPrefix = "Start setups unread=";
        public static string NameBinding(string name) => NamePrefix + name;
        public static string PriorityBinding(int priority) => PriorityPrefix + priority.ToString(CultureInfo.InvariantCulture);
        public static string SetupBinding(string identifier) => String.IsNullOrWhiteSpace(identifier) ? null : SetupPrefix + identifier;
        public static string SetupsUnreadBinding(string reason) => SetupsUnreadPrefix + reason;
        static IEnumerable<string> After(IEnumerable<string> lines, string prefix) =>
            (lines ?? Enumerable.Empty<string>()).Where(line => line != null && line.StartsWith(prefix, StringComparison.Ordinal)).Select(line => line.Substring(prefix.Length));
        // The priority from the bindings, or null when they hold none.
        public static int? Priority(IEnumerable<string> bindings) =>
            Int32.TryParse(After(bindings, PriorityPrefix).FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (int?)null;
        static string Count(int count, string one) => count.ToString(CultureInfo.InvariantCulture) + " " + one + (count == 1 ? "" : "s");
        // What the Object tab says under Use in the game, one line per use, from the spawn point's bindings: what the game
        // is expected to do with it, from the decompiled game, until a journey has observed it. nativeSpawns is how many
        // spawns the map data declares, or null when they could not be read; active is the spawn point's own activity;
        // samePriority counts the other active spawn points with the same priority, among which the game's sort keeps no
        // fixed order.
        public static IReadOnlyList<string> Uses(IEnumerable<string> bindings, int? nativeSpawns, bool active = true, int samePriority = 0) {
            var lines = bindings?.ToArray() ?? new string[0];
            var name = After(lines, NamePrefix).FirstOrDefault() ?? "its object name";
            var priority = After(lines, PriorityPrefix).FirstOrDefault();
            var uses = new List<string>();
            var choice = "the active spawn point with the highest priority" + (priority == null ? "" : " (this one: " + priority + ")")
                + (samePriority > 0 ? "; " + Count(samePriority, "other active spawn point") + " share this priority, and the choice among them is not fixed" : "");
            var fallback = active ? "a new game without a start setup is expected to start at " + choice + "."
                : "a new game without a start setup is expected to start at the active spawn point with the highest priority; this spawn point is disabled, so it is passed by.";
            uses.Add(nativeSpawns == null ? "New game: the map's native spawns could not be read. Without native spawns, " + fallback
                : nativeSpawns == 0 ? "New game: the map declares no native spawns, so " + fallback
                : "New game: the map declares " + Count(nativeSpawns.Value, "native spawn") + ", so a new game without a start setup is expected to start there. The Map Editor edits them under Spawns.");
            uses.Add(active ? "Rescue: a stuck player is expected to be moved to the closest active spawn point."
                : "Rescue: a stuck player is expected to be moved to the closest active spawn point; this spawn point is disabled, so it is passed by.");
            uses.Add(active ? "Teleport: the /tp console command is expected to offer this spawn point as " + name + " and jump to where it stands. Quick search lists active spawn points too, in a game version that opens it; Railroader 0.6.3 doesn't open quick search."
                : "Teleport: the /tp console command offers only active spawn points; this spawn point is disabled, so it's expected not to offer it.");
            var unread = After(lines, SetupsUnreadPrefix).FirstOrDefault();
            var setups = After(lines, SetupPrefix).ToArray();
            uses.Add(unread != null ? "Start setup: the start setups could not be read: " + unread + "."
                : setups.Length == 0 ? "No start setup names this spawn point."
                : "Start setup: a new company game of " + String.Join(", ", setups) + " is expected to start here when the setup is applied to a company with no cars and no balance yet, also while this spawn point is disabled.");
            return uses;
        }
    }
    // The game-facing operations of the spawn point capability. Positions are map coordinates.
    public interface ISpawnPointWorld {
        IEnumerable<string> MapIds { get; }
        // The loaded spawn points at a scene path.
        IReadOnlyList<object> Find(string scene, string path);
        // Why the spawn point cannot be edited now, or null.
        string Refusal(object spawn);
        // Its map placement and its own active flag.
        ObjectState Observe(object spawn);
        void SetActive(object spawn, bool active);
        void SetPlacement(object spawn, Placement placement);
    }
    // Places and enables or disables a scene spawn point. The game reads its transform each time it starts, rescues or
    // teleports a player, so nothing else needs to follow; it has no renderer, collider or culling of its own.
    public sealed class SpawnPointScene : IScene, IVersionedScene, IRelationshipScene {
        readonly ISpawnPointWorld world;
        public SpawnPointScene(ISpawnPointWorld world, Capability capability) { this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != SpawnPoints.Component || (identity.Structure != null && identity.Structure != SpawnPoints.Structure)) return Array.Empty<object>();
            return world.Find(identity.Scene, identity.Path) ?? Array.Empty<object>();
        }
        public void Preflight(object target) {
            var refusal = world.Refusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            if (!GroupChecks.Finite(world.Observe(target).Placement)) throw new InvalidOperationException("The spawn point's map coordinates are unavailable");
        }
        public void CheckRelationships(object target, ObjectState state) {
            if (state?.Marker != null) throw new InvalidOperationException("Spawn points do not support track-marker relationships");
            if (state?.Coverage != null) throw new InvalidOperationException("Spawn points do not support crossing-coverage relationships");
            if (state?.Equipment != null) throw new InvalidOperationException("Spawn points do not support crossing-equipment relationships");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
        }
        public object Capture(object target) => world.Observe(target);
        public ObjectState Read(object target) => world.Observe(target);
        public void Apply(object target, ObjectState state) {
            var before = world.Observe(target);
            if (state.Placement != null) GroupChecks.CheckPlacement(state.Placement);
            var active = state.Active ?? before.Active ?? true;
            Write(target, state.Placement, active, before.Active);
            var after = world.Observe(target);
            if (!GroupChecks.SamePlacement(after.Placement, state.Placement ?? before.Placement) || after.Active != active) throw new InvalidOperationException("Applied spawn point placement or activity did not verify");
        }
        void Write(object target, Placement placement, bool active, bool? was) {
            if (active && was != true) world.SetActive(target, true);
            if (placement != null) world.SetPlacement(target, placement);
            if (!active && was != false) world.SetActive(target, false);
        }
        public bool Restore(object target, object snapshot) {
            var captured = (ObjectState)snapshot;
            try {
                Write(target, captured.Placement, captured.Active != false, world.Observe(target).Active);
                var current = world.Observe(target);
                return GroupChecks.SamePlacement(current.Placement, captured.Placement) && current.Active == captured.Active;
            } catch { return false; }
        }
        public string MinimumPatcherVersion(TargetIdentity target) =>
            target != null && (target.Structure == SpawnPoints.Structure || (target.Structure == null && target.Component == SpawnPoints.Component)) ? SpawnPoints.RequiredVersion : null;
    }
}
