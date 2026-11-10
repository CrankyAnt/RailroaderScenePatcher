// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The first native capability is the retained independent Sylva board pattern.
    // Other scene families require their own dependent-state adapters.
    internal sealed class BoardScene : IScene, IInspectionScene, IDisposable {
        internal const string BoardPath = "World/Signs/Loc: Sylva Int";
        internal const string BoardSceneName = "BushnellWhittier-MapManager";
        internal const string CullerType = "Helpers.Culling.RendererCuller";
        internal const string MaskType = "Map.Runtime.MaskComponents.CircleMapMask";
        internal const string Structure = "independent-board-v1";
        readonly Type transformer, culler, mask;
        readonly object terrain;
        readonly System.Reflection.EventInfo tileEvent;
        readonly Action<Bounds> tileHandler;
        readonly Dictionary<GameObject, Pending> pending = new Dictionary<GameObject, Pending>();
        internal string Status { get; private set; } = "No dependent terrain work pending";
        internal bool HasPending => pending.Count != 0;
        public Capability Capability { get; private set; }
        public IEnumerable<string> MapIds => PackDocuments.MapIds();
        public IEnumerable<SceneObservation> Discover() => new NativeInventory().Discover();
        sealed class Snapshot {
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Transform Parent;
            internal bool Active;
        }
        sealed class Pending {
            internal readonly HashSet<Vector3> Positions = new HashSet<Vector3>();
        }
        internal BoardScene() {
            _ = PackDocuments.MapIds();
            transformer = Native.Type("Helpers.WorldTransformer");
            culler = Native.Type(CullerType); mask = Native.Type(MaskType);
            terrain = Native.Read(Native.Type("Map.Runtime.MapManager"), "Instance");
            if (transformer == null || culler == null || mask == null || terrain == null) throw new InvalidOperationException("Independent board prerequisites unavailable");
            // Check this capability's required members before any target is resolved.
            foreach (var method in new[] { "WorldToGame", "GameToWorld" })
                if (transformer.GetMethods(Native.Flags).Count(m => m.Name == method && m.GetParameters().Length == 1) != 1) throw new MissingMethodException(method);
            if (culler.GetMethod("RequestUpdateCullingPosition", Native.Flags) == null || mask.GetMethod("Rebuild", Native.Flags) == null) throw new MissingMethodException("Board refresh functions unavailable");
            tileEvent = terrain.GetType().GetEvent("OnTileBuilt", Native.Flags);
            if (tileEvent == null || tileEvent.EventHandlerType != typeof(Action<Bounds>)) throw new MissingMemberException("Terrain completion event unavailable");
            Native.Read(terrain, "PendingInvalidationCount");
            Native.Call(terrain, "IsTerrainWorkIdle");
            tileHandler = TileBuilt;
            tileEvent.AddEventHandler(terrain, tileHandler);
            Capability = Capability.Unchecked;
        }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Scene != BoardSceneName || identity.Component != CullerType || (identity.Structure != null && identity.Structure != Structure)) return Array.Empty<object>();
            return Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.scene.IsValid() && g.scene.isLoaded && g.scene.name == identity.Scene && PathOf(g.transform) == identity.Path && g.GetComponent(culler) != null).Cast<object>().ToArray();
        }
        internal static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        public void Preflight(object target) {
            var board = (GameObject)target;
            if (board == null || !board.scene.isLoaded || board.scene.name != BoardSceneName || PathOf(board.transform) != BoardPath) throw new InvalidOperationException("This object has no implemented independent-placement capability");
            if (!board.activeInHierarchy || !((Behaviour)board.GetComponent(culler)).enabled || !(board.GetComponent(mask) is Behaviour m) || !m.enabled)
                throw new InvalidOperationException("The board, culler and circle mask must be active");
            if (board.GetComponentsInChildren<Component>(true).Any(c => c == null || (c.GetType().FullName ?? "").StartsWith("Track.", StringComparison.Ordinal))) throw new InvalidOperationException("Unexpected board track dependency");
            if (!ReferenceEquals(Native.Read(Native.Type("Map.Runtime.MapManager"), "Instance"), terrain)) throw new InvalidOperationException("Terrain context changed");
            ConvertPosition("WorldToGame", board.transform.position);
        }
        public object Capture(object target) {
            var board = (GameObject)target;
            return new Snapshot { Position = ConvertPosition("WorldToGame", board.transform.position), Rotation = board.transform.rotation, Parent = board.transform.parent, Active = board.activeSelf };
        }
        public void Apply(object target, Placement placement) {
            var board = (GameObject)target;
            var position = Vec(placement.Position);
            var rotation = new Quaternion(CheckedFloat(placement.Rotation[0]), CheckedFloat(placement.Rotation[1]), CheckedFloat(placement.Rotation[2]), CheckedFloat(placement.Rotation[3]));
            if (Math.Abs(Quaternion.Dot(rotation, rotation) - 1f) > 0.001f) throw new InvalidOperationException("Rotation must be a unit quaternion");
            SetPose(board, position, rotation);
        }
        void SetPose(GameObject board, Vector3 position, Quaternion rotation) {
            var old = ConvertPosition("WorldToGame", board.transform.position);
            var local = ConvertPosition("GameToWorld", position);
            if ((ConvertPosition("WorldToGame", local) - position).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Map/origin conversion failed its round trip");
            // Track incomplete work before the first mutation, including interrupted refresh.
            if (!pending.TryGetValue(board, out var work)) pending.Add(board, work = new Pending());
            work.Positions.Add(old); work.Positions.Add(position);
            board.transform.SetPositionAndRotation(local, rotation);
            Native.Call(board.GetComponent(culler), "RequestUpdateCullingPosition");
            Native.Call(board.GetComponent(mask), "Rebuild");
            if ((ConvertPosition("WorldToGame", board.transform.position) - position).sqrMagnitude > 0.0001f || Quaternion.Angle(board.transform.rotation, rotation) > 0.01f)
                throw new InvalidOperationException("Applied pose did not verify");
            Status = "Board pose applied; awaiting native terrain rebuild at old and new positions";
        }
        public bool Restore(object target, object snapshot) {
            var board = (GameObject)target; var state = (Snapshot)snapshot;
            if (board == null || board.transform.parent != state.Parent || board.activeSelf != state.Active) return false;
            SetPose(board, state.Position, state.Rotation);
            // Async output cannot be reported restored on a transform comparison alone.
            return !pending.ContainsKey(board);
        }
        internal void PollCompletion() {
            if (!HasPending) return;
            if (!(bool)Native.Call(terrain, "IsTerrainWorkIdle") || Convert.ToInt32(Native.Read(terrain, "PendingInvalidationCount")) != 0) return;
            foreach (var pair in pending.ToArray()) if (pair.Value.Positions.Count == 0) pending.Remove(pair.Key);
            if (!HasPending) Status = "Native terrain rebuild completed at old and new board positions";
        }
        void TileBuilt(Bounds bounds) {
            foreach (var work in pending.Values) {
                // Native OnTileBuilt bounds use map coordinates, independently of origin.
                // Retain every intermediate location when several packs move this board.
                work.Positions.RemoveWhere(p => p.x >= bounds.min.x && p.x <= bounds.max.x && p.z >= bounds.min.z && p.z <= bounds.max.z);
            }
        }
        internal Vector3 ConvertPosition(string direction, Vector3 position) {
            if (Native.Read(transformer, "Shared") == null) throw new InvalidOperationException("Floating origin unavailable");
            var value = (Vector3)Native.Call(transformer, direction, position);
            if (!Finite(value)) throw new InvalidOperationException("Invalid origin conversion");
            return value;
        }
        static bool Finite(Vector3 v) => !Single.IsNaN(v.x) && !Single.IsNaN(v.y) && !Single.IsNaN(v.z) && !Single.IsInfinity(v.x) && !Single.IsInfinity(v.y) && !Single.IsInfinity(v.z);
        static float CheckedFloat(double value) { var f = (float)value; if (Single.IsNaN(f) || Single.IsInfinity(f)) throw new InvalidOperationException("Coordinate exceeds native precision"); return f; }
        static Vector3 Vec(double[] value) => new Vector3(CheckedFloat(value[0]), CheckedFloat(value[1]), CheckedFloat(value[2]));
        public void Dispose() { tileEvent.RemoveEventHandler(terrain, tileHandler); pending.Clear(); }
    }
}
