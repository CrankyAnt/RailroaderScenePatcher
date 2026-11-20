// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The native operations of the station map icon capability: the transform and activity of a UI.Map.MapIcon below
    // World/Map Labels/Station Icons, and the map's icon registry (MapBuilder._mapIcons). An icon registers with the map
    // in its OnEnable and leaves it in its OnDisable; the map draws it where its object stands. The rules live in Core's
    // StationMapIconScene.
    internal sealed class MapIconWorld : IMapIconWorld, IDisableRefusal {
        const string MapBuilderType = "UI.Map.MapBuilder";
        static readonly Type icon = Native.Type(StationMapIconRules.Component), builder = Native.Type(MapBuilderType);
        static readonly FieldInfo registry = builder?.GetField("_mapIcons", Native.Flags);
        static readonly PropertyInfo shared = builder?.GetProperty("Shared", Native.Flags);
        internal MapIconWorld() {
            if (icon == null || builder == null) throw new InvalidOperationException("The map icon types are unavailable.");
            if (registry == null || !typeof(IEnumerable).IsAssignableFrom(registry.FieldType) || shared == null) throw new MissingMemberException(MapBuilderType, "_mapIcons or Shared");
            if (Native.Type("Helpers.WorldTransformer") == null) throw new InvalidOperationException("The floating origin is unavailable.");
        }
        public IEnumerable<string> MapIds => ModScenePatchFiles.MapIds();
        static bool Loaded(GameObject g) => g != null && g.scene.IsValid() && g.scene.isLoaded;
        static GameObject Icon(object target) => target as GameObject ?? throw new InvalidOperationException("The station map icon no longer exists.");
        public IReadOnlyList<object> Find(string scene, string path) =>
            BoardScene.LoadedAt(scene, path).Where(g => Loaded(g) && g.GetComponent(icon) != null).Cast<object>().ToArray();
        public string Refusal(object target) => Problem(target as GameObject);
        // Why a station map icon cannot be edited, or null. Shared with the inventory.
        internal static string Problem(GameObject go) {
            if (!Loaded(go)) return "The station map icon's scene isn't loaded.";
            if (icon == null || go.GetComponent(icon) == null || !StationMapIconRules.Listed(BoardScene.PathOf(go.transform))) return "This object isn't a station map icon.";
            var components = go.GetComponentsInChildren<Component>(true);
            if (components.Any(c => c == null)) return "The station map icon has a missing script.";
            if (components.Any(c => c is Rigidbody)) return "The station map icon has a rigidbody. Only static colliders are supported.";
            // A culler holds a sphere at the place it registered; this capability does not move such a sphere, so a moved
            // icon could be hidden where it stands. Only the icon's own hierarchy counts: an icon without a culler stays
            // editable when another icon carries one.
            var culler = components.FirstOrDefault(c => { var n = c.GetType().FullName ?? ""; return n.StartsWith("Helpers.Culling.", StringComparison.Ordinal) || n == GroupWorld.SimpleCullerType; });
            if (culler != null) return "The station map icon has a culler of type " + culler.GetType().FullName + ". The patcher can't move that culler with it.";
            return null;
        }
        public MapIconObservation Observe(object target) {
            var go = Icon(target);
            var t = go.transform;
            var position = NativeInventory.MapPosition(t); var rotation = t.rotation;
            // An icon a progression controller switches reads the patch's activity: whether no explicit disable holds it off.
            var state = new ObjectState(new Placement(new double[] { position.x, position.y, position.z }, new double[] { rotation.x, rotation.y, rotation.z, rotation.w }), GroupWorld.ReadActivity(go, false));
            return new MapIconObservation(state, go.activeInHierarchy, t.parent == null ? null : t.parent.gameObject, Registered(go));
        }
        // Whether the map has the icon registered, or null while the game has no map.
        static bool? Registered(GameObject go) {
            var map = shared.GetValue(null) as UnityEngine.Object;
            if (map == null) return null;
            var held = go.GetComponent(icon);
            return registry.GetValue(map) is IEnumerable icons && icons.Cast<object>().Any(item => ReferenceEquals(item, held));
        }
        public void SetPlacement(object target, Placement placement) {
            var go = Icon(target);
            var map = new Vector3(Checked(placement.Position[0]), Checked(placement.Position[1]), Checked(placement.Position[2]));
            var rotation = new Quaternion(Checked(placement.Rotation[0]), Checked(placement.Rotation[1]), Checked(placement.Rotation[2]), Checked(placement.Rotation[3]));
            var moves = NativeInventory.MovesWithOrigin(go.transform);
            var scene = NativeInventory.ScenePosition(map, moves);
            if ((NativeInventory.MapPosition(scene, moves) - map).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Converting the position to scene coordinates and back changed the map coordinates.");
            go.transform.SetPositionAndRotation(scene, rotation);
            // The icon's click collider is read by the map's raycasts; synchronise so they find it at its new place.
            Physics.SyncTransforms();
        }
        // The icon's own OnEnable and OnDisable register it with the map and take it out again. An icon a progression
        // controller or a map feature switches: a disable holds it off against them, and Enable returns it to their
        // current choice.
        public void SetActive(object target, bool active) {
            var go = Icon(target);
            if (!GroupWorld.SetActivity(go, active, false)) go.SetActive(active);
            Physics.SyncTransforms();
        }
        // A disable of an icon that progression or a map feature switches is refused while it cannot hold against them.
        public string DisableRefusal(object target) => GroupWorld.DisableProblem(target as GameObject);
        static float Checked(double value) {
            if (Double.IsNaN(value) || Double.IsInfinity(value) || Math.Abs(value) > Single.MaxValue) throw new InvalidOperationException("A coordinate exceeds the game's numeric precision.");
            return (float)value;
        }
    }
}
