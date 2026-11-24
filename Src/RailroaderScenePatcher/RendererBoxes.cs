// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // Where a renderer's own box came from, as the frame check names it.
    enum OwnBoxSource { None, MeshPoints, MeshBounds, BatchPoints, BatchNotReadable, BatchUnavailable }

    // The own box of each renderer in the world: the box that follows its mesh's points (BoxAlongBuilder), so a mesh
    // built along a curve in axes of its own, such as a crossing pad, or a static-batched part, is framed along what it
    // shows; the box of its mesh's bounds where the points cannot be read. Points are read within the overlay's share
    // of each frame: a mesh's points are taken in parts of PartSize, and the one bulk read of a mesh's point list only
    // as the first read of a frame, so the first view of an area builds up over some frames instead of stuttering.
    sealed class RendererBoxes {
        // Points per part of a read.
        const int PartSize = 4096;
        // Points of combined static-batch meshes kept at most: the oldest are let go beyond it.
        const int KeptBatchPoints = 2000000;
        readonly ReadBudget budget;
        public RendererBoxes(ReadBudget budget) { this.budget = budget ?? throw new ArgumentNullException(nameof(budget)); }

        // What a mesh's box was read for: a mesh built again, such as a crossing pad's along a changed curve, is read
        // again when its point count or bounds differ. A rebuild that keeps both is not seen.
        readonly struct MeshStamp : IEquatable<MeshStamp> {
            public MeshStamp(Mesh mesh) { Points = mesh.vertexCount; Bounds = mesh.bounds; }
            public int Points { get; }
            public Bounds Bounds { get; }
            public bool Equals(MeshStamp other) => Points == other.Points && Bounds == other.Bounds;
        }
        // The box that follows a mesh's or a part's points, in its own space, or the read still under way; null
        // corners where the points cannot be read.
        sealed class FittedBox {
            public MeshStamp Stamp;
            public WorldPoint[] LocalCorners;
            public OwnBoxSource Source;
            public PointsRead Pending;
        }
        // A read of points part by part: the ranges of a point list, each point carried by a matrix, into a builder.
        sealed class PointsRead {
            public Vector3[] Points;
            public List<KeyValuePair<int, int>> Ranges;
            public Matrix4x4 ToSpace;
            public BoxAlongBuilder Builder;
            public Bounds? Reach;
            public int Range, Next;
            public bool Done => Range >= Ranges.Count;
            public void Step(int count) {
                var part = new List<WorldPoint>(count);
                while (count > 0 && !Done) {
                    var range = Ranges[Range];
                    if (Next < range.Key) Next = range.Key;
                    var end = Math.Min(range.Value, Next + count);
                    for (; Next < end; Next++) {
                        var p = ToSpace.MultiplyPoint3x4(Points[Next]);
                        part.Add(new WorldPoint(p.x, p.y, p.z));
                        if (Reach.HasValue) { var b = Reach.Value; b.Encapsulate(p); Reach = b; } else Reach = new Bounds(p, Vector3.zero);
                        count--;
                    }
                    if (Next >= range.Value) { Range++; Next = 0; }
                }
                Builder.Add(part);
            }
        }
        readonly Dictionary<Mesh, FittedBox> meshes = new Dictionary<Mesh, FittedBox>();
        readonly Dictionary<Renderer, FittedBox> parts = new Dictionary<Renderer, FittedBox>();
        // The point list of each combined static-batch mesh, read once for all its parts, oldest first.
        readonly Dictionary<Mesh, Vector3[]> batchPoints = new Dictionary<Mesh, Vector3[]>();
        readonly List<Mesh> batchOrder = new List<Mesh>();

        // Where a static-batched renderer's part of the combined mesh begins and the transform that places the batch:
        // the engine keeps both internal. Without them static-batched parts are framed by their world boxes.
        static readonly PropertyInfo batchRoot = typeof(Renderer).GetProperty("staticBatchRootTransform", Native.Flags),
            batchStart = typeof(Renderer).GetProperty("subMeshStartIndex", Native.Flags);
        static bool unavailableSaid, notReadableSaid;
        static void Unavailable(string reason) {
            if (unavailableSaid) return;
            unavailableSaid = true;
            PatcherLog.Debug("The patcher uses the game's world-aligned boxes to frame scenery parts drawn together. This game build doesn't provide each part's location in the combined drawing data: " + reason + ".");
        }
        static void NotReadable(Mesh mesh) {
            if (notReadableSaid) return;
            notReadableSaid = true;
            PatcherLog.Debug("The patcher uses the game's world-aligned boxes to frame scenery parts whose combined drawing data can't be read. One example is " + mesh.name + ".");
        }
        static bool Expected(Exception e) => e is UnityException || e is MissingReferenceException || e is TargetInvocationException || e is ArgumentException || e is InvalidCastException;

        // A renderer's own box in the world and where it came from. False while its points are still being read; the
        // object then waits, as for a renderer whose kind is not read yet.
        public bool Own(Renderer renderer, out WorldPoint[] corners, out OwnBoxSource source) {
            corners = null; source = OwnBoxSource.None;
            if (renderer is MeshRenderer) {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (renderer.isPartOfStaticBatch) {
                    if (mesh == null) return true;
                    if (!parts.TryGetValue(renderer, out var part)) {
                        // The part is placed in the world by the renderer's world box, which only a renderer that
                        // shows has: one that does not show yet is read once it shows.
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) return true;
                        part = StartPart(renderer, mesh);
                        if (part == null) return false;
                        parts[renderer] = part;
                    }
                    if (!Finish(part, renderer)) return false;
                    source = part.Source;
                    if (part.LocalCorners == null) return true;
                    corners = ToWorld(part.LocalCorners, renderer.transform.localToWorldMatrix);
                    return true;
                }
                if (mesh != null) {
                    var stamp = new MeshStamp(mesh);
                    if (!meshes.TryGetValue(mesh, out var entry) || !entry.Stamp.Equals(stamp)) {
                        entry = StartMesh(mesh, stamp);
                        if (entry == null) return false;
                        meshes[mesh] = entry;
                    }
                    if (!Finish(entry, null)) return false;
                    if (entry.LocalCorners != null) {
                        corners = ToWorld(entry.LocalCorners, renderer.localToWorldMatrix);
                        source = OwnBoxSource.MeshPoints;
                        return true;
                    }
                }
            }
            if (LocalBox(renderer, out var local, out var matrix)) {
                corners = ToWorld(WorldSelectionOverlay.BoxCorners(local), matrix);
                source = OwnBoxSource.MeshBounds;
            }
            return true;
        }
        // A read of a mesh's points in its own space, where its own axes stand in for the object's. Null when the bulk
        // read of the point list has to wait for the next frame.
        FittedBox StartMesh(Mesh mesh, MeshStamp stamp) {
            var entry = new FittedBox { Stamp = stamp };
            try {
                if (!mesh.isReadable || mesh.vertexCount == 0) return entry;
                if (budget.Spent > 0) return null;
                var points = budget.Read(() => mesh.vertices);
                entry.Pending = new PointsRead { Points = points, Ranges = new List<KeyValuePair<int, int>> { new KeyValuePair<int, int>(0, points.Length) },
                    ToSpace = Matrix4x4.identity, Builder = new BoxAlongBuilder(WorldBasis.World) };
            } catch (Exception e) when (Expected(e)) { entry.Pending = null; }
            return entry;
        }
        // A read of a static-batched renderer's own submeshes in the combined mesh, carried into the world by the
        // batch root where there is one. Null when the bulk read of the combined mesh's points has to wait.
        FittedBox StartPart(Renderer renderer, Mesh combined) {
            var part = new FittedBox();
            if (batchStart == null || batchRoot == null) {
                Unavailable((batchStart == null ? "Renderer.subMeshStartIndex" : "Renderer.staticBatchRootTransform") + " is missing");
                part.Source = OwnBoxSource.BatchUnavailable;
                return part;
            }
            try {
                if (!combined.isReadable) { NotReadable(combined); part.Source = OwnBoxSource.BatchNotReadable; return part; }
                if (!batchPoints.TryGetValue(combined, out var points)) {
                    if (budget.Spent > 0) return null;
                    batchPoints[combined] = points = budget.Read(() => combined.vertices);
                    batchOrder.Add(combined);
                    LetGoOfOldBatches();
                }
                var first = (int)batchStart.GetValue(renderer, null);
                var root = batchRoot.GetValue(renderer, null) as Transform;
                var ranges = new List<KeyValuePair<int, int>>();
                for (int sub = first; sub < first + renderer.sharedMaterials.Length && sub < combined.subMeshCount; sub++) {
                    var range = combined.GetSubMesh(sub);
                    ranges.Add(new KeyValuePair<int, int>(range.firstVertex, Math.Min(points.Length, range.firstVertex + range.vertexCount)));
                }
                part.Source = OwnBoxSource.BatchPoints;
                part.Pending = new PointsRead { Points = points, Ranges = ranges, ToSpace = root != null ? root.localToWorldMatrix : Matrix4x4.identity,
                    Builder = new BoxAlongBuilder(WorldSelectionOverlay.Basis(renderer.transform)) };
            } catch (Exception e) when (e is TargetInvocationException || e is InvalidCastException) {
                Unavailable(e.GetBaseException().Message);
                part.Source = OwnBoxSource.BatchUnavailable; part.Pending = null;
            } catch (Exception e) when (Expected(e)) { part.Source = OwnBoxSource.BatchUnavailable; part.Pending = null; }
            return part;
        }
        // Reads the next parts of a pending read within the frame's share. True once the box is known.
        bool Finish(FittedBox entry, Renderer part) {
            var read = entry.Pending;
            if (read == null) return true;
            try {
                while (!read.Done) {
                    if (budget.Exhausted) return false;
                    budget.Read(() => { read.Step(PartSize); return true; });
                }
                var box = read.Builder.Box;
                entry.Pending = null;
                if (box == null) { entry.LocalCorners = null; return true; }
                if (part == null) { entry.LocalCorners = box; return true; }
                // The part's points are moved so that their world-aligned box stands where the renderer's world box
                // stands now, and kept in its object's own space, so the box follows a patcher move.
                var shift = read.Reach.HasValue ? part.bounds.center - read.Reach.Value.center : Vector3.zero;
                entry.LocalCorners = ToWorld(box.Select(p => new WorldPoint(p.X + shift.x, p.Y + shift.y, p.Z + shift.z)).ToArray(), part.transform.worldToLocalMatrix);
            } catch (Exception e) when (Expected(e)) {
                entry.Pending = null; entry.LocalCorners = null;
                if (part != null) entry.Source = OwnBoxSource.BatchUnavailable;
            }
            return true;
        }
        void LetGoOfOldBatches() {
            var total = batchPoints.Values.Sum(p => (long)p.Length);
            while (total > KeptBatchPoints && batchOrder.Count > 1) {
                var oldest = batchOrder[0]; batchOrder.RemoveAt(0);
                if (batchPoints.TryGetValue(oldest, out var points)) { total -= points.Length; batchPoints.Remove(oldest); }
            }
        }
        static WorldPoint[] ToWorld(IReadOnlyList<WorldPoint> corners, Matrix4x4 matrix) =>
            corners.Select(p => { var q = matrix.MultiplyPoint3x4(new Vector3((float)p.X, (float)p.Y, (float)p.Z)); return new WorldPoint(q.x, q.y, q.z); }).ToArray();
        // A renderer's box in its own space and the matrix to the world. The mesh gives it whether or not the renderer
        // shows now; a static batch has no own mesh any more. False when neither is known.
        static bool LocalBox(Renderer renderer, out Bounds local, out Matrix4x4 matrix) {
            local = default; matrix = renderer.localToWorldMatrix;
            if (renderer is SkinnedMeshRenderer skinned) { local = skinned.localBounds; matrix = (skinned.rootBone != null ? skinned.rootBone : skinned.transform).localToWorldMatrix; return true; }
            if (renderer.isPartOfStaticBatch) return false;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) { local = filter.sharedMesh.bounds; return true; }
            if (renderer.enabled && renderer.gameObject.activeInHierarchy) { local = renderer.localBounds; return true; }
            return false;
        }
        public static string Describe(OwnBoxSource source) {
            switch (source) {
                case OwnBoxSource.MeshPoints: return "fitted to the points that define this part's shape";
                case OwnBoxSource.MeshBounds: return "taken from this part's shape bounds";
                case OwnBoxSource.BatchPoints: return "fitted to this part's points in the combined drawing data";
                case OwnBoxSource.BatchNotReadable: return "taken from the game's world-aligned box because the combined drawing data can't be read";
                case OwnBoxSource.BatchUnavailable: return "taken from the game's world-aligned box because this part's location in the combined drawing data is unavailable";
                default: return "not available";
            }
        }
        // Lets go of what is kept for destroyed meshes and renderers.
        public void Prune() {
            foreach (var gone in meshes.Keys.Where(key => key == null).ToArray()) meshes.Remove(gone);
            foreach (var gone in parts.Keys.Where(key => key == null).ToArray()) parts.Remove(gone);
            foreach (var gone in batchPoints.Keys.Where(key => key == null).ToArray()) { batchPoints.Remove(gone); batchOrder.Remove(gone); }
        }
        public void Clear() { meshes.Clear(); parts.Clear(); batchPoints.Clear(); batchOrder.Clear(); }
    }
}
