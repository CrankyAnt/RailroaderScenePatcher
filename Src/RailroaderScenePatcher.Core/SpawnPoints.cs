// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The scene spawn points (Character.SpawnPoint), such as those under World/POIs: what the game uses them for, how
    // the inventory lists them and the Object tab names their uses. Native spawns (Map.json) are another
    // kind: the native map editor edits them in its Spawns category, and the inventory does not list them.
    //
    // What the game reads (Railroader 0.6.3): SpawnPoint.All finds the active spawn points and sorts them by priority,
    // highest first. A new game starts at the native spawns when the map declares them, and otherwise at the first
    // of SpawnPoint.All (GlobalGameManager.InitializeDefaultSpawnIfNeeded). An active company setup names a spawn point;
    // when the game applies the setup to a company with no cars and no balance, it reads that spawn point's transform
    // for the start, whatever its activity (StateManager.ApplyNewGameSetup). Whether that start or the native spawns
    // win when both exist is not settled by the source. Both new-game reads run before the mods' OnWorldLoad, so the
    // patcher applies the spawn point modifications first (PatchEngine.BeforeNewGameStart). Railroader 0.6.3 showed a
    // new company game start at the moved spawn point its setup names on a map without native spawns, and /tp use a
    // moved spawn point. The default start without a setup, the rescue and the precedence of native spawns are not
    // observed, so the Object tab states every use as expected behaviour.
    // A stuck player is moved to the closest active spawn point, and to a native spawn only when there is none
    // (PlayerController, SpawnPoint.TryClosestTo). The /tp console command lists the active spawn points by name and
    // jumps to where each stands (TeleportCommand). Each reads the transform when it runs, so a moved spawn point is
    // used at its new place.
    public static class SpawnPoints {
        public const string Family = "Spawn point", Component = "Character.SpawnPoint", Structure = "spawn-point-v1", RequiredVersion = VersionRequirement.Delivery;
        public const string Operations = "Move, rotate, enable, or disable this spawn point. Edit native spawns in the map data under Spawns in the Map Editor.";
        public const string Dependency = "Player start, rescue, and teleport read this spawn point's placement when they run. Only active spawn points are offered. A start setup reads it even when it's disabled.";
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
        // Where a spawn point stands in the game's order (SpawnPoint.All): its own activity and the other active spawn
        // points, by path, that share or exceed its priority or whose priority could not be read. A spawn point without a
        // priority of its own has nothing to compare, so it counts none.
        public static SpawnRank Rank(SceneObservation selected, IEnumerable<SceneObservation> entries) {
            if (selected == null) throw new ArgumentNullException(nameof(selected));
            if (!selected.Active) return SpawnRank.Inactive;
            var priority = Priority(selected.Bindings);
            if (priority == null || entries == null) return SpawnRank.Alone;
            var others = entries.Where(entry => entry != null && entry.Family == Family && entry.Active && !ReferenceEquals(entry, selected) && entry.Identity.Path != selected.Identity.Path)
                .Select(entry => Priority(entry.Bindings)).ToArray();
            return new SpawnRank(true, others.Count(other => other == priority), others.Count(other => other > priority), others.Count(other => other == null));
        }
        static string Count(int count, string one) => count.ToString(CultureInfo.InvariantCulture) + " " + one + (count == 1 ? "" : "s");
        // What the Object tab says under Use in the game, one line per use, from the spawn point's bindings: what the game
        // is expected to do with it, from the decompiled game, until a journey has observed it. nativeSpawns is how many native
        // spawns the map declares, or null when they could not be read; rank is the spawn point's place in the
        // game's order (Rank), or null for an active spawn point with no others. A new game start is named only where
        // the game starts at this spawn point.
        public static IReadOnlyList<string> Uses(IEnumerable<string> bindings, int? nativeSpawns, SpawnRank rank = null) {
            rank = rank ?? SpawnRank.Alone;
            var active = rank.Active;
            var lines = bindings?.ToArray() ?? new string[0];
            var name = After(lines, NamePrefix).FirstOrDefault() ?? "its object name";
            var priority = After(lines, PriorityPrefix).FirstOrDefault();
            var uses = new List<string>();
            // The default start is the first of SpawnPoint.All, read only when the map declares no native spawns. In a
            // tie the game starts at one of the tied spawn points, so none of them is named as the start; an active spawn
            // point whose priority could not be read might come first, so the start here is hedged.
            if (active && priority != null && rank.HigherPriority == 0 && nativeSpawns.GetValueOrDefault() == 0) {
                var hidden = rank.UnreadPriority;
                var start = rank.SamePriority > 0
                    ? "a new game without a start setup is expected to start at one of the " + Count(rank.SamePriority + 1, "active spawn point") + " with the highest priority (" + priority + "), this one among them. Which one isn't fixed."
                        + (hidden > 0 ? " The priority of " + Count(hidden, "other active spawn point") + " couldn't be read, so the game might start there instead." : "")
                    : "a new game without a start setup is expected to start here, at the active spawn point with the highest priority (" + priority + ")"
                        + (hidden == 0 ? "." : hidden == 1 ? ", unless the other active spawn point whose priority couldn't be read has a higher one."
                            : ", unless one of the " + Count(hidden, "other active spawn point") + " whose priority couldn't be read has a higher one.");
                uses.Add(nativeSpawns == null ? "New game: the map's native spawns couldn't be read. If the map declares none, " + start
                    : "New game: the map declares no native spawns, so " + start);
            }
            uses.Add(active ? "Rescue: a stuck player is expected to be moved to the closest active spawn point."
                : "Rescue: a stuck player is expected to be moved to the closest active spawn point. This spawn point is disabled, so the game is expected to skip it.");
            uses.Add(active ? "Teleport: the /tp console command is expected to list this spawn point as " + name + " and teleport you to its position."
                : "Teleport: the /tp console command lists only active spawn points. This spawn point is disabled, so the command isn't expected to list it.");
            var unread = After(lines, SetupsUnreadPrefix).FirstOrDefault();
            var setups = After(lines, SetupPrefix).ToArray();
            var disabledToo = " This also applies when this spawn point is disabled.";
            var setupStart = "Start setup: a new company game using " + String.Join(", ", setups) + " is expected to start here when the setup is applied to a company that has no cars and no balance yet";
            uses.Add(unread != null ? "Start setup: the start setups couldn't be read. Reason: " + unread + "."
                : setups.Length == 0 ? "No start setup names this spawn point."
                : nativeSpawns == null ? setupStart + ". The map's native spawns couldn't be read; if the map declares any, the game might start there instead." + disabledToo
                : nativeSpawns > 0 ? "Start setup: named by " + String.Join(", ", setups) + ". The map declares " + Count(nativeSpawns.Value, "native spawn")
                    + ", so it isn't known whether a new company game using this setup starts here or at a native spawn."
                : setupStart + "." + disabledToo);
            return uses;
        }
    }
    // A spawn point's place in the game's order of active spawn points; see SpawnPoints.Rank.
    public sealed class SpawnRank {
        public static readonly SpawnRank Alone = new SpawnRank(true, 0, 0, 0), Inactive = new SpawnRank(false, 0, 0, 0);
        public SpawnRank(bool active, int samePriority, int higherPriority, int unreadPriority) {
            Active = active; SamePriority = samePriority; HigherPriority = higherPriority; UnreadPriority = unreadPriority;
        }
        public bool Active { get; }
        // The other active spawn points with the same priority, a higher one, or one that could not be read.
        public int SamePriority { get; }
        public int HigherPriority { get; }
        public int UnreadPriority { get; }
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
    public sealed class SpawnPointScene : IScene, IVersionedScene, IRelationshipScene, IDependentOutputScene {
        readonly ISpawnPointWorld world;
        // The scene ground masks at and below the spawn point: they move with it, a mask that flattens the terrain height
        // included.
        readonly MaskFollowing masks;
        public SpawnPointScene(ISpawnPointWorld world, Capability capability, GroundMasks masks = null) { this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; this.masks = new MaskFollowing(masks); }
        // Gives a capability built before the world's ground masks existed, as for a new game's start, those masks. A
        // capability that already has masks keeps them.
        public void UseMasks(GroundMasks value) => masks.Use(value);
        public DependentOutput Output(object target) => masks.Output(target);
        public void RetryOutput(object target) => masks.RetryOutput(target);
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Component != SpawnPoints.Component || (identity.Structure != null && identity.Structure != SpawnPoints.Structure)) return Array.Empty<object>();
            return world.Find(identity.Scene, identity.Path) ?? Array.Empty<object>();
        }
        public void Preflight(object target) {
            var refusal = world.Refusal(target);
            if (refusal != null) throw new InvalidOperationException(refusal);
            if (!GroupChecks.Finite(world.Observe(target).Placement)) throw new InvalidOperationException("The spawn point's map coordinates are unavailable.");
        }
        public void CheckRelationships(object target, ObjectState state) {
            if (state?.Marker != null) throw new InvalidOperationException("Spawn points do not support track-marker relationships.");
            if (state?.Coverage != null) throw new InvalidOperationException("Spawn points do not support crossing-coverage relationships.");
            if (state?.Equipment != null) throw new InvalidOperationException("Spawn points do not support crossing-equipment relationships.");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
        }
        public object Capture(object target) => world.Observe(target);
        public ObjectState Read(object target) => world.Observe(target);
        public void Apply(object target, ObjectState state) => masks.Write(target, () => Change(target, state));
        void Change(object target, ObjectState state) {
            var before = world.Observe(target);
            if (state.Placement != null) GroupChecks.CheckPlacement(state.Placement);
            var active = state.Active ?? before.Active ?? true;
            Write(target, state.Placement, active, before.Active);
            var after = world.Observe(target);
            if (!GroupChecks.SamePlacement(after.Placement, state.Placement ?? before.Placement) || after.Active != active) throw new InvalidOperationException("Applied spawn point placement or activity did not verify.");
        }
        void Write(object target, Placement placement, bool active, bool? was) {
            if (active && was != true) world.SetActive(target, true);
            if (placement != null) world.SetPlacement(target, placement);
            if (!active && was != false) world.SetActive(target, false);
        }
        public bool Restore(object target, object snapshot) => masks.Restore(target, () => Restored(target, snapshot));
        bool Restored(object target, object snapshot) {
            var captured = (ObjectState)snapshot;
            try {
                Write(target, captured.Placement, captured.Active != false, world.Observe(target).Active);
                var current = world.Observe(target);
                return GroupChecks.SamePlacement(current.Placement, captured.Placement) && current.Active == captured.Active;
            } catch { return false; }
        }
        public string MinimumPatcherVersion(TargetIdentity target) => VersionRequirement.For(target, SpawnPoints.Structure, SpawnPoints.Component, SpawnPoints.RequiredVersion);
    }
}
