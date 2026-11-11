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
    // The native operations of the independent-board capability: board transforms, the renderer culler,
    // the circle map mask and the terrain system. The capability rules live in Core's IndependentBoards.
    internal sealed class BoardScene : IBoardWorld, IDisposable {
        internal const string BoardPath = IndependentBoards.SylvaPath;
        internal const string BoardSceneName = IndependentBoards.SylvaScene;
        internal const string CullerType = IndependentBoards.CullerType;
        internal const string MaskType = "Map.Runtime.MaskComponents.CircleMapMask";
        internal const string Structure = IndependentBoards.Structure;
        readonly Type transformer, culler, mask, maskBase;
        readonly object terrain;
        readonly EventInfo tileEvent;
        readonly Action<Bounds> tileHandler;
        readonly FieldInfo token, keys, spheres, maskStorage, heightStorage, storedModifiers, flatten;
        readonly MethodInfo invalidate, tileOf, held;
        public event Action<double[], double[]> TileBuilt;
        public IEnumerable<string> MapIds => PackDocuments.MapIds();
        internal BoardScene() {
            _ = PackDocuments.MapIds();
            transformer = Native.Type("Helpers.WorldTransformer");
            culler = Native.Type(CullerType); mask = Native.Type(MaskType); maskBase = Native.Type("Map.Runtime.MaskComponents.MaskComponentBase");
            var manager = Native.Type("Map.Runtime.MapManager");
            terrain = manager == null ? null : Native.Read(manager, "Instance");
            if (transformer == null || culler == null || mask == null || maskBase == null || terrain == null) throw new InvalidOperationException("Independent board prerequisites unavailable");
            // Check this capability's required members before any target is resolved.
            foreach (var method in new[] { "WorldToGame", "GameToWorld" })
                if (transformer.GetMethods(Native.Flags).Count(m => m.Name == method && m.GetParameters().Length == 1) != 1) throw new MissingMethodException(method);
            if (culler.GetMethod("RequestUpdateCullingPosition", Native.Flags) == null || mask.GetMethod("Rebuild", Native.Flags) == null) throw new MissingMethodException("Board refresh functions unavailable");
            token = culler.GetField("_cullingToken", Native.Flags);
            keys = maskBase.GetField("_modifierKeys", Native.Flags);
            flatten = mask.GetField("flattenHeightmap", Native.Flags);
            var tokenType = token?.FieldType;
            spheres = Native.Type("Helpers.Culling.CullingManager")?.GetField("_spheres", Native.Flags);
            maskStorage = manager.GetField("_maskModifiers", Native.Flags); heightStorage = manager.GetField("_heightmapModifiers", Native.Flags);
            storedModifiers = maskStorage?.FieldType.GetField("_modifiers", Native.Flags);
            if (tokenType?.GetProperty("Index") == null || tokenType.GetField("Manager") == null || tokenType.GetProperty("IsRemoved") == null || keys == null || flatten == null || spheres == null || maskStorage == null || heightStorage == null || storedModifiers == null)
                throw new MissingMemberException("Board registration members unavailable");
            invalidate = manager.GetMethod("Invalidate", Native.Flags, null, new[] { typeof(Vector2Int) }, null);
            tileOf = manager.GetMethod("TilePositionFromPoint", Native.Flags, null, new[] { typeof(Vector3) }, null);
            held = manager.GetMethod("IsTileHeld", Native.Flags, null, new[] { typeof(Vector2Int) }, null);
            if (invalidate == null || tileOf == null || held == null) throw new MissingMethodException("Terrain tile functions unavailable");
            tileEvent = terrain.GetType().GetEvent("OnTileBuilt", Native.Flags);
            if (tileEvent == null || tileEvent.EventHandlerType != typeof(Action<Bounds>)) throw new MissingMemberException("Terrain completion event unavailable");
            Native.Read(terrain, "PendingInvalidationCount");
            Native.Call(terrain, "IsTerrainWorkIdle");
            tileHandler = OnTileBuilt;
            tileEvent.AddEventHandler(terrain, tileHandler);
        }
        internal static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        static GameObject Board(object board) => board as GameObject ?? throw new InvalidOperationException("The board no longer exists");
        public IReadOnlyList<object> Find(string scene, string path) =>
            Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.scene.IsValid() && g.scene.isLoaded && g.scene.name == scene && PathOf(g.transform) == path && g.GetComponent(culler) != null).Cast<object>().ToArray();
        public BoardFacts Facts(object target) {
            var board = Board(target);
            var cull = board.GetComponent(culler) as Behaviour;
            var masks = board.GetComponents(mask).OfType<Behaviour>().ToArray();
            return new BoardFacts(board.scene.name, PathOf(board.transform), board.scene.IsValid() && board.scene.isLoaded,
                board.transform.parent == null || board.transform.parent.gameObject.activeInHierarchy, cull != null, cull != null && cull.enabled,
                masks.Length, masks.Length != 0 && masks.All(m => m.enabled), masks.Any(m => (bool)flatten.GetValue(m)), Unexpected(board, cull, masks), board.transform.parent);
        }
        // Components in the hierarchy whose dependent state a move would leave stale.
        string Unexpected(GameObject board, Component cull, Behaviour[] masks) {
            foreach (var component in board.GetComponentsInChildren<Component>(true)) {
                if (component == null) return "a missing script";
                if (ReferenceEquals(component, cull) || masks.Contains(component) || component is Transform) continue;
                var type = component.GetType();
                if ((type.FullName ?? "").StartsWith("Track.", StringComparison.Ordinal)) return type.FullName;
                if (maskBase.IsAssignableFrom(type) || culler.IsAssignableFrom(type) || type.GetInterfaces().Any(i => i.Name == "ICullingEventHandler")) return type.FullName;
            }
            return null;
        }
        public string Label(object board) { var go = board as GameObject; return go == null ? "missing board" : go.scene.name + "/" + PathOf(go.transform); }
        public double[] Position(object board) { var p = ConvertPosition("WorldToGame", Board(board).transform.position); return new double[] { p.x, p.y, p.z }; }
        public double[] Rotation(object board) { var r = Board(board).transform.rotation; return new double[] { r.x, r.y, r.z, r.w }; }
        public bool Active(object board) => Board(board).activeSelf;
        public void SetPose(object target, double[] position, double[] rotation) {
            var board = Board(target);
            var game = new Vector3(CheckedFloat(position[0]), CheckedFloat(position[1]), CheckedFloat(position[2]));
            var local = ConvertPosition("GameToWorld", game);
            if ((ConvertPosition("WorldToGame", local) - game).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Map/origin conversion failed its round trip");
            board.transform.SetPositionAndRotation(local, new Quaternion(CheckedFloat(rotation[0]), CheckedFloat(rotation[1]), CheckedFloat(rotation[2]), CheckedFloat(rotation[3])));
        }
        public void SetActive(object board, bool active) => Board(board).SetActive(active);
        public void RefreshCulling(object board) => Native.Call(Board(board).GetComponent(culler), "RequestUpdateCullingPosition");
        public void RebuildMask(object board) => Native.Call(Board(board).GetComponent(mask), "Rebuild");
        public BoardRegistration Registration(object target) {
            var board = Board(target);
            double[] sphere = null;
            var current = token.GetValue(board.GetComponent(culler));
            if (current != null && !(bool)Native.Read(current, "IsRemoved")) {
                var index = (int)Native.Read(current, "Index");
                var all = (BoundingSphere[])spheres.GetValue(Native.Read(current, "Manager"));
                if (index < 0 || index >= all.Length) throw new InvalidOperationException("Culling sphere index out of range");
                var center = ConvertPosition("WorldToGame", all[index].position);
                sphere = new double[] { center.x, center.y, center.z };
            }
            var centers = new List<double[]>(); var missing = 0;
            foreach (var component in board.GetComponents(mask)) {
                foreach (var key in ((IEnumerable<string>)keys.GetValue(component)).ToArray()) {
                    var found = Stored(maskStorage, key) ?? Stored(heightStorage, key);
                    if (found == null) { missing++; continue; }
                    // Modifiers added in world coordinates are stored offset to map coordinates.
                    var bounds = (Bounds)Native.Read(Native.Read(found, "Mask"), "Bounds");
                    centers.Add(new double[] { bounds.center.x, bounds.center.y, bounds.center.z });
                }
            }
            return new BoardRegistration(sphere, centers, missing);
        }
        object Stored(FieldInfo storage, string key) {
            var dictionary = storage.GetValue(terrain) is object store ? store.GetType().GetField("_modifiers", Native.Flags)?.GetValue(store) as IDictionary : null;
            if (dictionary == null) throw new MissingMemberException("Terrain modifier storage unavailable");
            return dictionary.Contains(key) ? dictionary[key] : null;
        }
        object Tile(double[] position) => tileOf.Invoke(terrain, new object[] { new Vector3((float)position[0], (float)position[1], (float)position[2]) });
        public bool TileLoaded(double[] position) => (bool)held.Invoke(terrain, new[] { Tile(position) });
        public void InvalidateTile(double[] position) => invalidate.Invoke(terrain, new[] { Tile(position) });
        public bool TerrainIdle => (bool)Native.Call(terrain, "IsTerrainWorkIdle") && Convert.ToInt32(Native.Read(terrain, "PendingInvalidationCount")) == 0;
        public double Now => Time.realtimeSinceStartup;
        // Native OnTileBuilt bounds use map coordinates, independently of origin.
        void OnTileBuilt(Bounds bounds) => TileBuilt?.Invoke(new double[] { bounds.min.x, bounds.min.y, bounds.min.z }, new double[] { bounds.max.x, bounds.max.y, bounds.max.z });
        internal Vector3 ConvertPosition(string direction, Vector3 position) {
            if (Native.Read(transformer, "Shared") == null) throw new InvalidOperationException("Floating origin unavailable");
            var value = (Vector3)Native.Call(transformer, direction, position);
            if (Single.IsNaN(value.x) || Single.IsNaN(value.y) || Single.IsNaN(value.z) || Single.IsInfinity(value.x) || Single.IsInfinity(value.y) || Single.IsInfinity(value.z)) throw new InvalidOperationException("Invalid origin conversion");
            return value;
        }
        static float CheckedFloat(double value) { var f = (float)value; if (Single.IsNaN(f) || Single.IsInfinity(f)) throw new InvalidOperationException("Coordinate exceeds native precision"); return f; }
        public void Dispose() { tileEvent.RemoveEventHandler(terrain, tileHandler); }
    }
}
