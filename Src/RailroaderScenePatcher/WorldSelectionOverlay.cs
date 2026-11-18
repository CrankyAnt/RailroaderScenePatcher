// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RailroaderScenePatcher.Core;
using UnityEngine;
using UnityEngine.Rendering;
using MarkingPoint = RailroaderScenePatcher.Core.ScreenPoint;

namespace RailroaderScenePatcher {
    // Screen shapes are shared by drawing and picking, including labels and the track-width ribbon.
    internal sealed class WorldSelectionOverlay {
        const float MarkerSize = (float)ObjectMarkingGeometry.MarkerSize;
        const float TrackMinimumRadius = 10f;
        sealed class Shape {
            internal SpatialCandidate Candidate;
            internal Rect Marker;
            // The marking of a group that shows as one: its square marker carries the group symbol, not a plus.
            internal bool Group;
            internal readonly List<Rect> Objects = new List<Rect>();
            // The frame as it is drawn and picked: the convex outline of the object on screen. Objects holds the
            // rectangle around it, at whose centre the marker stands.
            internal IReadOnlyList<MarkingPoint> Outline;
            internal bool InFrame(Vector2 pointer) => Outline != null && ObjectMarkingGeometry.Contains(Outline, pointer.x, pointer.y);
            internal ProjectedWorldRibbon Track;
            internal Rect Label;
            internal string Text;
            internal Color Color;
            internal bool Detailed;
            // A row of the list beside the click, for the candidates of one spot.
            internal bool Listed;
            // The candidate a list row or a panel row points at: drawn last, with a heavy frame and marker.
            internal bool Strong;
            internal bool Contains(Vector2 pointer) =>
                Marker.Contains(pointer) || InFrame(pointer) ||
                (Track != null && Track.Contains(new MarkingPoint(pointer.x, pointer.y)));
        }
        readonly List<Shape> labels = new List<Shape>();
        IReadOnlyList<Rect> covered = Array.Empty<Rect>();
        GUIStyle textStyle;
        float trackRadius;
        Material trackMaterial;
        // The outline kind of each renderer seen, read once: its components, shadow mode and level of detail stay.
        readonly Dictionary<Renderer, ObjectMarkingPartKind> kinds = new Dictionary<Renderer, ObjectMarkingPartKind>();

        // The height of the surface an object stands in at a place (ObjectMarkingGeometry.Surface): the terrain, or the
        // ballast of track that lies on it. One ray from above meets everything on the game's Terrain and Track layers
        // there; track that is carried over the terrain, as on a bridge deck, is no such surface. Read once per half
        // metre of the world and kept until the origin moves, the surface may have changed (ClearSurface) or the
        // overlay is released; null where there is no terrain.
        readonly Dictionary<long, double?> grounds = new Dictionary<long, double?>();
        Vector3 groundOrigin;
        // The layers by number; -1 for one the game does not have, -2 before the first look.
        int terrainLayer = -2, trackLayer = -2;
        // The share of one pass, in milliseconds, for what is read once and then kept: renderer kinds and surface
        // heights (ReadBudget). Only those reads are timed, and each pass, a repaint or a click, starts anew, so the
        // work on what is known already never holds the reads back and they come to an end.
        const double WarmBudgetMs = 4;
        static readonly System.Diagnostics.Stopwatch readClock = System.Diagnostics.Stopwatch.StartNew();
        readonly ReadBudget budget = new ReadBudget(WarmBudgetMs, () => readClock.Elapsed.TotalMilliseconds);
        // Whether an object is an inventory object, which has a marker and a frame of its own; set by the owner.
        internal Func<GameObject, bool> IsInventory;
        // The renderers that make up an object: those under it, and for a construction controller, which has none of
        // its own, those of the objects whose visibility it sets (Game.Mow.TrackConstructionScenery.entries[].targets)
        // that are no inventory objects themselves and lie under none: an inventory object, such as the bridge the
        // controller shows, has its own frame, and a second one around it could not be told from it. A controller
        // all of whose targets are inventory objects, and that has no renderer of its own, is an invisible object: it
        // shows its helper marker on its own pivot, with the frame of its own colliders or the least frame about its
        // marker (HelperMarkers). The controller's targets are read once per object.
        readonly Dictionary<GameObject, GameObject[]> controlled = new Dictionary<GameObject, GameObject[]>();
        // The renderers of an object are listed once and kept for a second or two, each object on its own beat, and
        // listed again after the scene may have changed (ClearSurface): a list per object per pass is garbage of
        // hundreds of arrays a frame, which the game pays for in collection pauses.
        // With each list, which of its renderers outline the object (ObjectMarkingGeometry.Outlines): a renderer the
        // game's culling hides now still counts, so the frame and its marker do not change with camera distance.
        sealed class Listed { internal Renderer[] Renderers; internal bool[] Outlines; internal int Until; }
        readonly Dictionary<GameObject, Listed> listed = new Dictionary<GameObject, Listed>();
        Renderer[] Renderers(GameObject go, out bool[] outlines) {
            if (listed.TryGetValue(go, out var kept) && Time.frameCount < kept.Until) { outlines = kept.Outlines; return kept.Renderers; }
            var list = ListRenderers(go);
            outlines = list.Select(OutlinesObject).ToArray();
            listed[go] = new Listed { Renderers = list, Outlines = outlines, Until = Time.frameCount + 90 + (go.GetInstanceID() & 63) };
            return list;
        }
        // The game's components that switch renderers off by camera distance and hold the renderers they switch: a
        // renderer culler (_cullRenderers) and the builder of a curve mesh such as a crossing pad (_renderers).
        static readonly Type rendererCuller = Native.Type(BakedSceneryRules.RendererCuller), curveBuilder = Native.Type("BezierCurveMesh.CurveMeshBuilderBase");
        static readonly System.Reflection.FieldInfo cullRenderers = rendererCuller?.GetField("_cullRenderers", Native.Flags), curveRenderers = curveBuilder?.GetField("_renderers", Native.Flags);
        static bool HeldByCuller(Renderer renderer) {
            for (var t = renderer.transform; t != null; t = t.parent)
                foreach (var behaviour in t.GetComponents<MonoBehaviour>()) {
                    if (behaviour == null) continue;
                    var type = behaviour.GetType();
                    var field = rendererCuller != null && rendererCuller.IsAssignableFrom(type) ? cullRenderers : curveBuilder != null && curveBuilder.IsAssignableFrom(type) ? curveRenderers : null;
                    if (field != null && field.GetValue(behaviour) is Renderer[] held && Array.IndexOf(held, renderer) >= 0) return true;
                }
            return false;
        }
        // Whether an object that is switched off is off only through a culler (GroupWorld.OffThroughCuller), read once
        // per frame for each object: the renderers below one culled container share the answer.
        static readonly Dictionary<GameObject, bool> throughCuller = new Dictionary<GameObject, bool>();
        static int throughCullerFrame = -1;
        static MarkingLink Link(GameObject go) {
            if (go.activeSelf) return new MarkingLink(true, false);
            if (throughCullerFrame != Time.frameCount) { throughCuller.Clear(); throughCullerFrame = Time.frameCount; }
            if (!throughCuller.TryGetValue(go, out var off)) throughCuller[go] = off = GroupWorld.OffThroughCuller(go);
            return new MarkingLink(false, off);
        }
        static bool OutlinesObject(Renderer renderer) {
            if (renderer == null) return false;
            try {
                var chain = new List<MarkingLink>();
                for (var t = renderer.transform; t != null; t = t.parent) chain.Add(Link(t.gameObject));
                return ObjectMarkingGeometry.Outlines(renderer.enabled, !renderer.enabled && HeldByCuller(renderer), chain);
            } catch { return renderer.enabled && renderer.gameObject.activeInHierarchy; }
        }
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
        // The storage piles at and below an object that outline it: listed with its renderers, and passed over while
        // the scene or a modder switched them off, as a renderer is.
        static readonly Type pileType = Native.Type(BakedSceneryRules.StoragePile);
        static readonly System.Reflection.FieldInfo pileWidth = pileType?.GetField("width", Native.Flags), pileHeight = pileType?.GetField("maxHeight", Native.Flags),
            pileProfile = pileType?.BaseType?.GetField("profile", Native.Flags);
        readonly Dictionary<GameObject, KeyValuePair<int, Component[]>> piles = new Dictionary<GameObject, KeyValuePair<int, Component[]>>();
        IEnumerable<Component> Piles(GameObject go) {
            if (pileType == null) return Array.Empty<Component>();
            if (!piles.TryGetValue(go, out var kept) || Time.frameCount >= kept.Key)
                piles[go] = kept = new KeyValuePair<int, Component[]>(Time.frameCount + 90 + (go.GetInstanceID() & 63), go.GetComponentsInChildren(pileType, true));
            return kept.Value.Where(pile => {
                if (pile == null) return false;
                try {
                    var chain = new List<MarkingLink>();
                    for (var t = pile.transform; t != null; t = t.parent) chain.Add(Link(t.gameObject));
                    return ObjectMarkingGeometry.Outlines(((Behaviour)pile).enabled, false, chain);
                } catch { return pile.gameObject.activeInHierarchy; }
            });
        }
        // The eight corners of the box a pile lays its logs out in (Scenery.Pile's own bounds gizmo): its width across,
        // its height up from the ground plus one log's radius, and the logs' length along it. Null when the pile has no
        // profile or its settings cannot be read.
        static WorldPoint[] PileBox(Component pile) {
            try {
                var profile = pileProfile?.GetValue(pile);
                if (profile == null || pileWidth == null || pileHeight == null) return null;
                float width = (float)pileWidth.GetValue(pile), height = (float)pileHeight.GetValue(pile);
                float radius = Convert.ToSingle(Native.Read(profile, "EffectiveRadius")), length = Convert.ToSingle(Native.Read(profile, "EffectiveLength"));
                var centre = new Vector3(0, height / 2 + radius, 0); var half = new Vector3(width / 2, height / 2, length / 2);
                var matrix = pile.transform.localToWorldMatrix; var corners = new WorldPoint[8];
                for (var i = 0; i < 8; i++) corners[i] = Point(matrix.MultiplyPoint3x4(centre + Vector3.Scale(half, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                return corners;
            } catch { return null; }
        }
        // The bounds of an invisible object's colliders, at and below it, as parts for its helper marker
        // (HelperMarkers.Mark): each collider's own box in the world, from its shape. Which colliders frame the object
        // (HelperMarkers.Framing: on, with every object up to the scene root on or off only through a culler; no
        // terrain collider; triggers only without a solid collider) is decided once and kept on the renderers' beat,
        // as the renderers' outlines are; the boxes of those colliders are read anew in each pass, so the frame
        // follows the object.
        // The terrain's collider type, read by name: its module is not referenced.
        const string TerrainColliderType = "UnityEngine.TerrainCollider";
        sealed class Framed { internal Collider[] Colliders; internal int Until; }
        readonly Dictionary<GameObject, Framed> colliders = new Dictionary<GameObject, Framed>();
        IReadOnlyList<ObjectMarkingPart> Colliders(GameObject go) {
            if (!colliders.TryGetValue(go, out var kept) || Time.frameCount >= kept.Until) {
                var all = go.GetComponentsInChildren<Collider>(true);
                var facts = all.Select(collider => {
                    if (collider == null) return new HelperCollider(false, false, false, null);
                    try {
                        var chain = new List<MarkingLink>();
                        for (var t = collider.transform; t != null; t = t.parent) chain.Add(Link(t.gameObject));
                        return new HelperCollider(collider.enabled, collider.isTrigger, collider.GetType().FullName == TerrainColliderType, chain);
                    } catch { return new HelperCollider(false, false, false, null); }
                }).ToArray();
                colliders[go] = kept = new Framed { Colliders = HelperMarkers.Framing(facts).Select(i => all[i]).ToArray(), Until = Time.frameCount + 90 + (go.GetInstanceID() & 63) };
            }
            var parts = new List<ObjectMarkingPart>();
            for (var index = 0; index < kept.Colliders.Length; index++) {
                var collider = kept.Colliders[index];
                if (collider == null) continue;
                try {
                    var corners = GroupWorld.Corners(collider).Select(Point).ToArray();
                    if (corners.Length != 8) continue;
                    double minX = corners.Min(c => c.X), maxX = corners.Max(c => c.X), minY = corners.Min(c => c.Y), maxY = corners.Max(c => c.Y), minZ = corners.Min(c => c.Z), maxZ = corners.Max(c => c.Z);
                    parts.Add(new ObjectMarkingPart("collider #" + index, new WorldPoint((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2),
                        new WorldPoint((maxX - minX) / 2, (maxY - minY) / 2, (maxZ - minZ) / 2), corners));
                } catch { }
            }
            return parts;
        }
        // Where a candidate stands in the world now: the transform of its scene object, which follows every placement
        // change, gizmo preview and Undo, or its observed position for anything else.
        static Vector3 Live(SpatialCandidate candidate, Vector3 origin) =>
            candidate.Observation != null && candidate.Choice.Handle is GameObject go && go != null ? go.transform.position : candidate.Position + origin;
        Renderer[] ListRenderers(GameObject go) {
            var own = go.GetComponentsInChildren<Renderer>(true);
            if (!controlled.TryGetValue(go, out var targets)) {
                var found = new List<GameObject>();
                try {
                    foreach (var behaviour in go.GetComponents<MonoBehaviour>()) {
                        if (behaviour == null || behaviour.GetType().FullName != "Game.Mow.TrackConstructionScenery") continue;
                        if (!(Native.Read(behaviour, "entries") is System.Collections.IEnumerable entries)) continue;
                        foreach (var entry in entries)
                            if (Native.Read(entry, "targets") is GameObject[] list)
                                // A target under the controller itself is not dropped: its renderers are among the
                                // controller's own, listed above, so it is only not listed twice.
                                foreach (var target in list) if (target != null && !target.transform.IsChildOf(go.transform) && !found.Contains(target)) found.Add(target);
                    }
                } catch { }
                controlled[go] = targets = found.ToArray();
            }
            if (targets.Length == 0) return own;
            var all = new List<Renderer>(own);
            foreach (var target in targets) {
                if (target == null) continue;
                var listed = false;
                if (IsInventory != null)
                    for (var parent = target.transform; parent != null && !listed; parent = parent.parent) listed = IsInventory(parent.gameObject);
                if (!listed) all.AddRange(target.GetComponentsInChildren<Renderer>(true));
            }
            return all.ToArray();
        }
        double? GroundAt(double x, double z) {
            if (terrainLayer == -2) {
                terrainLayer = LayerMask.NameToLayer("Terrain"); trackLayer = LayerMask.NameToLayer("Track");
                // A layer the game does not have gives no mask bit: without the terrain nothing is cut, without the
                // track the cut is at the terrain alone. Said once.
                if (terrainLayer < 0) Debug.LogWarning("Scene Patcher: this game build has no Terrain layer; object frames are not cut off at the ground.");
                else if (trackLayer < 0) Debug.LogWarning("Scene Patcher: this game build has no Track layer; object frames are cut off at the terrain and not at the ballast.");
            }
            if (terrainLayer < 0) return null;
            long key = ((long)Math.Floor(x * 2) << 32) ^ ((long)Math.Floor(z * 2) & 0xFFFFFFFFL);
            if (grounds.TryGetValue(key, out var known)) return known;
            // A height that is not known yet, once the frame's share is used up, is read in a later frame: until
            // then the piece above it is not cut.
            if (budget.Exhausted) return null;
            var height = budget.Read(() => {
                try {
                    var mask = (1 << terrainLayer) | (trackLayer >= 0 ? 1 << trackLayer : 0);
                    double? terrain = null; List<double> track = null;
                    foreach (var hit in Physics.RaycastAll(new Vector3((float)x, 5000f, (float)z), Vector3.down, 10000f, mask)) {
                        var kind = ObjectMarkingGeometry.HitKind(hit.collider.gameObject.layer == terrainLayer, OfInventory(hit.collider.transform));
                        if (kind == ObjectMarkingGeometry.SurfaceHit.Terrain) terrain = terrain.HasValue ? Math.Max(terrain.Value, hit.point.y) : hit.point.y;
                        else if (kind == ObjectMarkingGeometry.SurfaceHit.Ballast) (track ?? (track = new List<double>())).Add(hit.point.y);
                    }
                    return ObjectMarkingGeometry.Surface(terrain, track);
                } catch { return (double?)null; }
            });
            if (grounds.Count > 50000) grounds.Clear();
            grounds[key] = height;
            return height;
        }
        // Whether a collider belongs to an inventory object: the object itself or anything below one.
        bool OfInventory(Transform t) {
            if (IsInventory == null) return false;
            for (; t != null; t = t.parent) if (IsInventory(t.gameObject)) return true;
            return false;
        }
        // The surface may have changed: an edit, a terrain output that completed, or a track change. The heights are
        // read again.
        internal void ClearSurface() { grounds.Clear(); listed.Clear(); colliders.Clear(); controlled.Clear(); piles.Clear(); groupedFrom = null; }
        // What is kept per object and per renderer loses the entries of objects the game destroyed, now and then.
        int prunedAt;
        void Prune() {
            if (Time.frameCount - prunedAt < 600) return;
            prunedAt = Time.frameCount;
            foreach (var gone in listed.Keys.Where(key => key == null).ToArray()) listed.Remove(gone);
            foreach (var gone in colliders.Keys.Where(key => key == null).ToArray()) colliders.Remove(gone);
            foreach (var gone in controlled.Keys.Where(key => key == null).ToArray()) controlled.Remove(gone);
            foreach (var gone in piles.Keys.Where(key => key == null).ToArray()) piles.Remove(gone);
            foreach (var gone in kinds.Keys.Where(key => key == null).ToArray()) kinds.Remove(gone);
            foreach (var gone in keptBoxes.Keys.Where(key => key == null).ToArray()) keptBoxes.Remove(gone);
        }
        // The world box of each static-batched renderer from when it last showed, in its object's own space: the box it
        // outlines with while the game's culling hides it. It is taken again whenever the renderer shows, and it
        // follows its object when the patcher moves it. Kept until the overlay is released.
        readonly Dictionary<Renderer, WorldPoint[]> keptBoxes = new Dictionary<Renderer, WorldPoint[]>();
        static WorldPoint[] BoxCorners(Bounds b) =>
            Enumerable.Range(0, 8).Select(i => Point(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)))).ToArray();

        internal void Release() {
            keptBoxes.Clear();
            if (trackMaterial != null) UnityEngine.Object.Destroy(trackMaterial);
            trackMaterial = null;
            ClearInteraction();
            kinds.Clear();
            grounds.Clear();
            controlled.Clear();
            listed.Clear();
            colliders.Clear();
        }

        internal void ClearInteraction() {
            inspectedMark = null;
            labels.Clear();
        }

        internal bool OverLabel(Vector2 pointer, IReadOnlyList<Rect> uiBounds) =>
            labels.Any(label => label.Label.Contains(pointer)) && !uiBounds.Any(bounds => bounds.Contains(pointer));

        internal SelectionCandidate[] Pick(SpatialCandidate[] candidates, Camera camera, Vector3 origin, Vector2 pointer,
            IReadOnlyList<Rect> uiBounds, SelectionCandidate chosen, out bool listRow, out bool isCrossingSetup) {
            listRow = false;
            isCrossingSetup = false;
            // Native windows can move between the last repaint and this click.
            covered = uiBounds;
            budget.Begin();
            if (covered.Any(r => r.Contains(pointer))) return Array.Empty<SelectionCandidate>();
            lastPick = pointer;
            var labelHits = labels.Where(s => s.Label.Contains(pointer)).ToArray();
            var shapes = ProjectAll(candidates, camera, origin, out var measured);
            var groups = Groups(candidates, measured, camera, origin, chosen, inspectedMark);
            OwnFrames(candidates, shapes, measured, groups, camera, origin);
            // A row of the list beside the click owns its click. Otherwise a click on a shown square marker reaches
            // that marker, also where the label of another candidate lies over it. Any other opaque identity label
            // owns its click; geometry hidden behind it is never selected.
            var rows = labelHits.Where(s => s.Listed).ToArray();
            if (rows.Length != 0) { labelHits = rows; listRow = true; }
            else if (CommandBand.MarkerBeforeLabel(Enumerable.Range(0, shapes.Length).Any(i => shapes[i] != null && !groups.Hidden[i] && Marked(shapes[i], candidates[i], camera, origin) && shapes[i].Marker.Contains(pointer))
                || groups.Shown.Any(group => group.Shape != null && group.Shape.Marker.Contains(pointer)))) labelHits = new Shape[0];
            if (labelHits.Length != 0)
                return candidates.Where(c => labelHits.Any(s => s.Candidate.Choice.Matches(c.Choice))).Select(c => c.Choice).ToArray();
            var result = Resolve(candidates, shapes, groups, camera, origin, pointer);
            isCrossingSetup = result.IsCrossingSetup;
            return result.Objects.Select(i => candidates[i].Choice).ToArray();
        }
        // Where the last click fell: the list of the candidates of one spot opens beside it.
        Vector2 lastPick;
        // The candidates a click at the pointer selects (WorldPick.Resolve): the rule of the click and of the hover.
        // A game-defined group that shows as one takes part with its square marker and frame as one object, and
        // stands for its objects. A candidate that is not in view has no marker and no frame to meet the pointer.
        WorldPickResult Resolve(SpatialCandidate[] candidates, Shape[] shapes, GroupLayout groups, Camera camera, Vector3 origin, Vector2 pointer) {
            PickTarget Target(Shape shape, bool marked) => shape == null ? new PickTarget(0, 0, false, false)
                : new PickTarget(shape.Marker.center.x, shape.Marker.center.y, marked, shape.InFrame(pointer) || (shape.Track != null && shape.Track.Contains(new MarkingPoint(pointer.x, pointer.y))));
            var targets = new PickTarget[shapes.Length];
            // The root of a near group with parts shows nothing of its own, so the pointer meets nothing of it.
            for (var i = 0; i < shapes.Length; i++) targets[i] = groups.Absent[i] ? Target(null, false) : Target(shapes[i], shapes[i] != null && Marked(shapes[i], candidates[i], camera, origin));
            return WorldPick.Resolve(pointer.x, pointer.y, targets, groups.Shown.Select(group => new PickGroup(Target(group.Shape, true), group.Members, group.Own, group.Unlisted, group.Kind)).ToArray());
        }

        // A game-defined group that shows as one square marker: its objects among the candidates, as indices, those
        // of them that show on their own, those its choice list leaves out (its detection spans), what the group
        // shows and its one marking, or null when it is not in view.
        sealed class ShownGroup { internal int[] Members; internal int[] Own; internal int[] Unlisted; internal GroupShows Shows; internal Shape Shape; internal GameDefinedGroupKind Kind; }
        // The groups that show as one from this camera, for each candidate whether its group stands for it, the
        // candidates that show their own frame at their group's frame distance (GameDefinedGroups.OwnFrameDistance).
        // The inspected object has its frame at any distance.
        sealed class GroupLayout {
            internal ShownGroup[] Shown = new ShownGroup[0]; internal bool[] Hidden; internal readonly List<int> OwnAtGroupDistance = new List<int>();
            // The candidates that show nothing and that no shown group stands for: the root of a group with parts
            // at the group distance or less, where only its parts show.
            internal bool[] Absent;
        }
        // What one pass measured of a scene object: its frame, with the bounds and the parts of its framed piece. A
        // group takes its bounds and its one box from these, without reading the object again.
        sealed class Measured { internal ObjectMarkingFrame Frame; }
        // The component facts of a group, read from the family the inventory gave an object: a crossing controller
        // anchors a grade-crossing setup, a track marker that is no crossing pad is, below that controller's root, a
        // whistle sign, and a track span there is a detection span that the controller manages.
        const string ControllerFamily = "Crossing controller", MarkerFamily = "Track marker", SpanFamily = "Track span", ConstructionFamily = "Bridge construction scenery";
        // The inventory snapshot, set by the owner: a group holds inventory objects, whatever a choice offers, so
        // Select in world and a choice for a scene patch file show one crossing the same way.
        internal Func<IReadOnlyList<SceneObservation>> Inventory;
        // One group of the inventory: its objects that have a usable position, for each of them whether the group's
        // choice list offers it and its frame surrounds it (GameDefinedGroup.Listed), and the scene object of the
        // game component that anchors it, whose orientation the group's one box takes.
        // Stands is the index among Objects of the root of a baked scenery group with parts, which stands for the
        // group (GameDefinedGroup.Root), or -1.
        sealed class InventoryGroup { internal SpatialCandidate[] Objects; internal bool[] Listed; internal GameObject Root; internal int Stands = -1; internal GameDefinedGroupKind Kind; }
        // The groups are decided once per inventory snapshot, and again when the snapshot is another one or has
        // another number of entries, and after the scene may have changed (ClearSurface, on every move of the
        // native map editor's history). Without a snapshot they are decided from the candidates of the choice.
        object groupedFrom;
        int groupedCount;
        InventoryGroup[] inventoryGroups = new InventoryGroup[0];
        // For each group, the index among the candidates of each of its objects, or -1 for one the choice does not
        // offer; kept per list of candidates.
        SpatialCandidate[] mappedFor;
        int[][] mapped = new int[0][];
        static IEnumerable<object> Above(GameObject go) {
            for (var parent = go == null ? null : go.transform.parent; parent != null; parent = parent.parent) yield return parent.gameObject;
        }
        InventoryGroup[] GroupsOf(SpatialCandidate[] candidates) {
            var snapshot = Inventory?.Invoke();
            var listed = snapshot != null && snapshot.Count != 0;
            object from = listed ? (object)snapshot : candidates;
            var count = listed ? snapshot.Count : candidates.Length;
            if (ReferenceEquals(from, groupedFrom) && count == groupedCount) return inventoryGroups;
            groupedFrom = from; groupedCount = count; mappedFor = null; inventoryGroups = new InventoryGroup[0];
            try {
                var all = listed ? snapshot.ToArray() : candidates.Where(c => c.Observation != null).Select(c => c.Observation).ToArray();
                // A root of the Baked scenery group family anchors a baked scenery group with the inventory objects
                // below it: its parts, and the objects of other families there.
                bool BakedRoot(SceneObservation o) => o.Family == BakedSceneryRules.Family && o.GroupPath == null && o.Handle is GameObject go && go != null && GroupWorld.IsGroupRoot(go);
                var roots = all.Select(BakedRoot).ToArray();
                // What a game component names: the piles an industry's storage fills, and the scene objects whose
                // visibility a construction controller sets. A fact that cannot be read forms no group.
                var storage = new Dictionary<GameObject, GameObject>();
                try { storage = NativeInventory.StorageDisplays(); } catch (Exception e) { Skipped(null, "the industry storage displays could not be read: " + e.GetBaseException().Message); }
                GameObject[] Targets(SceneObservation o, GameObject go) {
                    if (o.Family != ConstructionFamily) return null;
                    try { return NativeInventory.ConstructionTargets(go); } catch (Exception e) { Skipped(go, "its construction targets could not be read: " + e.GetBaseException().Message); return null; }
                }
                if (all.Any(o => o.Family == ControllerFamily || o.Family == ConstructionFamily) || roots.Any(r => r) || storage.Count != 0) {
                    var found = new List<InventoryGroup>();
                    foreach (var group in GameDefinedGroups.Of(all.Select((o, i) => {
                            if (!(o.Handle is GameObject go) || go == null) return null;
                            var targets = Targets(o, go);
                            return new GroupObject(go, Above(go), o.Family == ControllerFamily, o.Family == MarkerFamily, o.Family == SpanFamily, roots[i], o.GroupPath != null,
                                storage.TryGetValue(go, out var filledBy) ? filledBy : null, targets != null, targets, targets?.Select(Above));
                        }).ToArray())) {
                        var root = group.Anchor as GameObject;
                        var read = skips.Read(() => RootPath(root), () => {
                            var placed = group.Members.Select(i => new { Object = SpatialSelectionScene.Observe(all[i]), Listed = group.Listed.Contains(i), Stands = i == group.Root }).Where(m => m.Object.Points.Length != 0).ToArray();
                            if (placed.Length == 0) { skips.Skip(RootPath(root), "none of its objects has a usable position"); return null; }
                            return new InventoryGroup { Root = root, Objects = placed.Select(m => m.Object).ToArray(), Listed = placed.Select(m => m.Listed).ToArray(), Stands = Array.FindIndex(placed, m => m.Stands), Kind = group.Kind };
                        });
                        if (read != null) found.Add(read);
                    }
                    inventoryGroups = found.ToArray();
                }
            } catch (Exception e) { Debug.LogWarning("Scene Patcher: game-defined groups could not be read; every object shows on its own. " + e.GetBaseException().Message); }
            return inventoryGroups;
        }
        // A group that world marking skips shows its objects on its own; one developer diagnostic line says so, once
        // per group root and reason.
        static readonly GroupSkips skips = new GroupSkips(line => Debug.Log("Scene Patcher: " + line));
        static string RootPath(GameObject root) => root == null ? null : root.scene.name + "/" + BoardScene.PathOf(root.transform);
        internal static void Skipped(GameObject root, string reason) => skips.Skip(RootPath(root), reason);
        int[][] Mapped(SpatialCandidate[] candidates, InventoryGroup[] groups) {
            if (ReferenceEquals(candidates, mappedFor)) return mapped;
            var byHandle = new Dictionary<object, List<int>>();
            for (var i = 0; i < candidates.Length; i++) {
                if (candidates[i].Observation == null) continue;
                if (!byHandle.TryGetValue(candidates[i].Choice.Handle, out var list)) byHandle[candidates[i].Choice.Handle] = list = new List<int>();
                list.Add(i);
            }
            mapped = groups.Select(group => group.Objects.Select(member => {
                if (byHandle.TryGetValue(member.Choice.Handle, out var list)) foreach (var i in list) if (Same(candidates[i], member.Choice)) return i;
                return -1;
            }).ToArray()).ToArray();
            mappedFor = candidates;
            return mapped;
        }
        static bool Same(SpatialCandidate candidate, SelectionCandidate other) =>
            other != null && ReferenceEquals(candidate.Choice.Handle, other.Handle) && candidate.Choice.Identity.Equals(other.Identity);
        // How the groups of the inventory show from this camera (ObjectMarkingGeometry.GroupFrame). A group at 125
        // metres or less from its bounds shows its objects and is not listed here, and neither is a group none of
        // whose objects the choice offers. measured holds what this pass measured of each candidate. chosen is the
        // candidate of the pending choice and inspected the inspected object: each shows its own marker and frame.
        GroupLayout Groups(SpatialCandidate[] candidates, Measured[] measured, Camera camera, Vector3 origin, SelectionCandidate chosen, SpatialCandidate inspected) {
            var layout = new GroupLayout { Hidden = new bool[candidates.Length], Absent = new bool[candidates.Length] };
            var groups = GroupsOf(candidates);
            if (groups.Length == 0) return layout;
            var map = Mapped(candidates, groups);
            var shown = new List<ShownGroup>();
            var eye = Point(camera.transform.position);
            for (var g = 0; g < groups.Length; g++) {
                var objects = groups[g].Objects; var indices = map[g];
                var members = indices.Where(i => i >= 0).ToArray();
                // The group's bounds, its one box and its list hold its listed objects: the invisible detection spans
                // of a grade-crossing setup do not count. A choice that offers none of the listed objects, only
                // detection spans, shows no group: its spans show on their own.
                var listed = groups[g].Listed;
                if (!indices.Where((i, k) => i >= 0 && listed[k]).Any()) continue;
                // A candidate was measured when it was projected. A listed object of the group that the choice does
                // not offer is measured here, once per pass: it bounds the group and outlines it like the others.
                var parts = new Measured[objects.Length];
                for (var k = 0; k < objects.Length; k++) {
                    if (indices[k] >= 0) parts[k] = measured[indices[k]];
                    else if (listed[k]) Project(objects[k], camera, origin, ObjectMarkingGeometry.FrameDistance, out parts[k]);
                }
                var anchors = objects.Select(member => Point(Live(member, origin))).ToArray();
                var frames = parts.Select(part => part?.Frame).ToArray();
                // The group's one box stands in the orientation of the scene object that anchors it, the crossing
                // controller's root, around where its objects stand and around the parts of their pieces, and is cut
                // at the surface like the box of an object.
                var root = groups[g].Root;
                var marking = ObjectMarkingGeometry.GroupFrame(eye, anchors, frames, root != null ? Basis(root.transform) : (WorldBasis?)null, GroundAt, Projection(camera), ViewOf(camera), listed);
                bool Inspected(SpatialCandidate member) => inspected != null && Same(member, inspected.Choice);
                // The root of a group with parts stands for the group: at the group distance or less only the
                // parts show, and the root shows only as the candidate of a choice or as the inspected object.
                var stands = groups[g].Stands < 0 ? -1 : indices[groups[g].Stands];
                // A choice that offers the root and none of its parts shows the root as any object.
                if (!GameDefinedGroups.RootStands(stands, members)) stands = -1;
                if (marking.Shows == GroupShows.Objects) {
                    if (stands >= 0 && !GameDefinedGroups.ShowsOwn(marking.Shows, Same(candidates[stands], chosen), Inspected(candidates[stands]), true)) layout.Hidden[stands] = layout.Absent[stands] = true;
                    continue;
                }
                var own = members.Where(m => Same(candidates[m], chosen) || Inspected(candidates[m])).ToArray();
                foreach (var m in members) layout.Hidden[m] = !GameDefinedGroups.ShowsOwn(marking.Shows, Same(candidates[m], chosen), Inspected(candidates[m]), m == stands);
                var ownDistance = GameDefinedGroups.OwnFrameDistance(marking.Shows);
                if (ownDistance > ObjectMarkingGeometry.FrameDistance) layout.OwnAtGroupDistance.AddRange(own);
                var unlisted = indices.Where((i, k) => i >= 0 && !listed[k]).ToArray();
                shown.Add(new ShownGroup { Members = members, Own = own, Unlisted = unlisted, Shows = marking.Shows, Shape = GroupShape(marking, camera), Kind = groups[g].Kind });
            }
            layout.Shown = shown.ToArray();
            return layout;
        }
        // Every candidate projected once, with what was measured of each.
        Shape[] ProjectAll(SpatialCandidate[] candidates, Camera camera, Vector3 origin, out Measured[] measured) {
            var shapes = new Shape[candidates.Length]; measured = new Measured[candidates.Length];
            for (var i = 0; i < candidates.Length; i++) shapes[i] = Project(candidates[i], camera, origin, ObjectMarkingGeometry.FrameDistance, out measured[i]);
            return shapes;
        }
        // A candidate that shows on its own in a group within the group's frame distance has its own frame, although
        // its own bounds, or without parts its marker, may lie beyond the frame distance: it is projected again at
        // the group's frame distance. That is the candidate of the choice and the inspected object, at most.
        void OwnFrames(SpatialCandidate[] candidates, Shape[] shapes, Measured[] measured, GroupLayout layout, Camera camera, Vector3 origin) {
            foreach (var m in layout.OwnAtGroupDistance) {
                var frame = measured[m]?.Frame;
                if (frame == null || frame.Frame.HasValue) continue;
                var distance = Double.IsNaN(frame.Distance) ? (camera.transform.position - Live(candidates[m], origin)).magnitude : frame.Distance;
                if (distance > ObjectMarkingGeometry.FrameDistance) shapes[m] = Project(candidates[m], camera, origin, Double.PositiveInfinity, out _);
            }
        }
        // The camera's projection of a world point to clip coordinates, and its view in top-left screen pixels.
        static Func<WorldPoint, ClipPoint> Projection(Camera camera) {
            var toClip = camera.projectionMatrix * camera.worldToCameraMatrix;
            return p => {
                var clip = toClip * new Vector4((float)p.X, (float)p.Y, (float)p.Z, 1);
                return new ClipPoint(clip.x, clip.y, clip.z, clip.w);
            };
        }
        static ScreenRect ViewOf(Camera camera) {
            var view = camera.pixelRect;
            return ScreenRect.FromEdges(view.xMin, Screen.height - view.yMax, view.xMax, Screen.height - view.yMin);
        }
        // An object's own right, up and forward: the directions of the one box that frames it.
        static WorldBasis Basis(Transform transform) => new WorldBasis(Point(transform.right), Point(transform.up), Point(transform.forward));
        // The one marking of a group: its square marker at the centre of its frame, or, beyond the frame distance,
        // at the middle of its bounds. Null when neither is in view.
        Shape GroupShape(GroupMarking marking, Camera camera) {
            var shape = new Shape { Group = true };
            // The group's marker stands on the center of its one box, a fixed world point.
            var anchor = ScreenPoint(camera, Vector(marking.Marker), Vector3.zero);
            if (marking.Frame.HasValue) {
                var frame = ObjectMarkingGeometry.VisibleFrame(marking.Frame.Value);
                var rect = Rect.MinMaxRect((float)frame.X, (float)frame.Y, (float)frame.Right, (float)frame.Bottom);
                shape.Objects.Add(rect);
                shape.Outline = ObjectMarkingGeometry.VisibleOutline(marking.Outline);
                anchor = MarkerOn(camera, marking.Marker, rect);
            } else if (!InFront(camera, anchor)) return null;
            else if (marking.Shows == GroupShows.MarkerAndFrame) {
                // None of its objects has an outline in view: the least frame about the group's marker.
                shape.Outline = ObjectMarkingGeometry.MarkerOutline(anchor.x, anchor.y);
                var half = (float)(ObjectMarkingGeometry.MarkerSize / 2 + ObjectMarkingGeometry.MarkerMargin);
                shape.Objects.Add(new Rect(anchor.x - half, anchor.y - half, 2 * half, 2 * half));
            }
            shape.Marker = new Rect(anchor.x - MarkerSize / 2, anchor.y - MarkerSize / 2, MarkerSize, MarkerSize);
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            return screen.Overlaps(shape.Marker) || shape.Objects.Any(screen.Overlaps) ? shape : null;
        }
        // The inspected object of the last repaint: in a group that shows as one it keeps its own marker and frame.
        SpatialCandidate inspectedMark;
        // Whether a candidate carries its "+" without the pointer over it: a track only within a distance of the camera.
        static bool Marked(Shape shape, SpatialCandidate candidate, Camera camera, Vector3 origin) =>
            CommandBand.ShowsMarker(shape.Track != null, shape.Track == null ? 0 : (camera.transform.position - (candidate.Position + origin)).magnitude, false);

        // One colour table with the panel, CommandBand.WorldColor: gold frames the inspected object; a candidate is blue,
        // more saturated under the pointer and plain blue once clicked; a current relation is green, more saturated under the
        // pointer; red marks only a broken item. A candidate that is neither hovered nor clicked shows its neutral
        // marker. Amber is a panel colour and never appears in the world.
        static Color Tone(CommandBand.WorldMark mark) { var c = CommandBand.WorldColor(mark); return new Color(c[0], c[1], c[2]); }
        internal static readonly Color Gold = Tone(CommandBand.WorldMark.Inspected);
        // A current relation of the inspected object: green, more saturated under the pointer.
        void Relation(List<Shape> shapes, SpatialCandidate relation, string label, Camera camera, Vector3 origin, Vector2 pointer) {
            if (relation == null || relation.Points.Length == 0 || shapes.Any(s => s.Text != null && s.Candidate.Choice.Matches(relation.Choice))) return;
            var shape = Project(relation, camera, origin);
            if (shape == null) return;
            shape.Color = Tone(CommandBand.RelationMark(shape.Contains(pointer)));
            shape.Detailed = true;
            shape.Text = ObjectLabel(relation, label);
            shapes.Add(shape);
        }
        internal void Draw(SpatialCandidate[] candidates, PendingSelection pending, SpatialCandidate source, bool hasSource, SpatialCandidate confirmed, SpatialCandidate followed,
            IReadOnlyList<SpatialCandidate> bound, Camera camera, Vector3 origin, IReadOnlyList<Rect> uiBounds, Texture2D backing, SpatialCandidate trackPoint = null) {
            covered = uiBounds;
            inspectedMark = source;
            budget.Begin();
            Prune();
#if ISSUE42_OBSERVATION
            BeginObservation(pending, source, camera);
            try {
#endif
            if (textStyle == null) {
                // Skin labels can carry one-line height/offset constraints. Measure and draw with
                // an unconstrained style so the opaque backing encloses every wrapped identity line.
                textStyle = new GUIStyle {
                    font = NativeFont() ?? GUI.skin.label.font,
                    fontSize = 16, wordWrap = true, richText = false,
                    fixedWidth = 0, fixedHeight = 0, contentOffset = Vector2.zero,
                    clipping = TextClipping.Clip,
                    padding = new RectOffset(8, 8, 5, 5), alignment = TextAnchor.UpperLeft
                };
                textStyle.normal.textColor = new Color(.8156863f, .7950737f, .746353f, 1f);
            }
            var shapes = new List<Shape>();
            // Use the same screen pixels as world clicks, regardless of the current IMGUI event.
            var mouse = Input.mousePosition;
            var pointer = new Vector2(mouse.x, Screen.height - mouse.y);
            // The pointer marks what a click there would select, by the click's own rule: one candidate, or the
            // candidates of one spot. Over a window or a label it marks nothing by that rule; a label marks its own.
            var projected = ProjectAll(candidates, camera, origin, out var measured);
            var overUi = covered.Any(r => r.Contains(pointer));
            var labelHit = overUi ? null : labels.FirstOrDefault(label => label.Label.Contains(pointer));
            labels.Clear();
            var groups = Groups(candidates, measured, camera, origin, pending?.Candidate, source);
            OwnFrames(candidates, projected, measured, groups, camera, origin);
            var pointed = pending == null || overUi || labelHit != null ? null : Resolve(candidates, projected, groups, camera, origin, pointer);
            var under = pointed?.HoveredObjects ?? Array.Empty<int>();
            var several = pending?.ListOpen == true;
            var offerLabels = several ? pending.Offers.Select(o => o.Label).ToArray() : null;
            for (var index = 0; index < candidates.Length; index++) {
                var candidate = candidates[index]; var shape = projected[index];
                bool inOffers = pending != null && pending.Offers.Contains(candidate.Choice);
                bool chosen = pending != null && ReferenceEquals(candidate.Choice, pending.Candidate);
                bool offered = inOffers && (several || chosen);
                bool rowHovered = inOffers && (overUi && ReferenceEquals(candidate.Choice, pending.Highlight) ||
                    ReferenceEquals(candidate.Choice, labelHit?.Candidate.Choice) && (chosen || (several && labelHit.Listed)));
                // Hovering a row reveals that object's own frame, including when its group stands in for it.
                if (groups.Hidden[index]) {
                    if (!offered || !several) continue;
                    shape = rowHovered ? Project(candidate, camera, origin, Double.PositiveInfinity, out _) : null;
                    if (shape == null) {
                        shapes.Add(new Shape { Candidate = candidate, Listed = true, Color = Tone(CommandBand.CandidateMark(false, rowHovered)),
                            Text = ObjectLabel(candidate, pending.Number(candidate.Choice) + "  ", offerLabels) });
                        continue;
                    }
                }
                if (shape == null) continue;
                // A candidate the panel points at, or whose label the pointer is over, is marked as one under the pointer.
                bool hovered = under.Contains(index) || rowHovered;
                // Blue for the candidate a click would select and after it was clicked, until Confirm.
                shape.Color = Tone(CommandBand.CandidateMark(chosen, hovered));
                shape.Detailed = chosen || hovered;
                // The candidates of one spot are the rows of a list beside the click, numbered, each named by what
                // tells it from the others.
                shape.Listed = offered && several;
                shape.Strong = rowHovered;
                if (offered) shape.Text = several ? ObjectLabel(candidate, pending.Number(candidate.Choice) + "  ", offerLabels)
                    : ObjectLabel(candidate, "Candidate: ");
                // Every candidate of the mode carries its "+" marker without the pointer over it; a track within a
                // distance of the camera, so that a wide view is not filled with them.
                if (shape.Listed || shape.Detailed || Marked(shape, candidate, camera, origin)) shapes.Add(shape);
            }
            // A group that shows as one: its square marker, and its frame in blue while the pointer is where a click
            // would pick the group, and plain blue while its choice list is open.
            for (var index = 0; index < groups.Shown.Length; index++) {
                var group = groups.Shown[index];
                if (group.Shape == null) continue;
                bool hovered = pointed != null && pointed.Groups.Contains(index);
                bool open = pending != null && several && group.Members.Any(m => groups.Hidden[m] && pending.Offers.Contains(candidates[m].Choice));
                group.Shape.Color = Tone(CommandBand.CandidateMark(open && !hovered, hovered));
                group.Shape.Detailed = open || hovered;
                shapes.Add(group.Shape);
            }
            if (source != null && source.Points.Length > 0) {
                var marked = shapes.FirstOrDefault(s => s.Text != null && s.Candidate.Choice.Matches(source.Choice));
                // The inspected object keeps its frame and marker at any camera distance and while culling hides it.
                var shape = Project(source, camera, origin, Double.PositiveInfinity, out _);
                if (marked != null) {
                    if (hasSource) marked.Text = ObjectLabel(source, "Inspected: ");
                } else if (shape != null) {
                    // The gold frame and the marker at its center say which object is inspected; a world label would
                    // stand in the gizmo's way, and the panel names the object.
                    shape.Color = Gold;
                    shape.Detailed = true;
                    shapes.Add(shape);
                }
            }
            // The inspected starter placement marker's track point: gold as the inspected object, on the track, where the
            // start setup places the front of the marker's car cut, and the track the cut stands on from there, which shows
            // the direction the cut extends.
            if (trackPoint != null) {
                var point = Project(trackPoint, camera, origin);
                if (point != null) {
                    point.Color = Gold;
                    point.Detailed = true;
                    point.Text = (trackPoint.Path == null ? "Track point: " : "Front of the car cut: ") + Short(trackPoint.Choice.Label);
                    shapes.Add(point);
                }
            }
            // The current relations of the inspected object stay marked in green while it is inspected: a target
            // confirmed in the world, the track it follows, and, while the Connections tab is open, what it is bound to.
            // The offered marking of the same element leads.
            Relation(shapes, confirmed, "Confirmed choice: ", camera, origin, pointer);
            if (CommandBand.ShowsCurrentRelations(confirmed != null)) {
                Relation(shapes, followed, "Follows: ", camera, origin, pointer);
                if (bound != null) foreach (var relation in bound) Relation(shapes, relation, "Bound: ", camera, origin, pointer);
            }
            var zones = VisibleWorld(uiBounds);
            // The list of one spot stands beside the click; every other label under its own marker.
            var listed = shapes.Where(s => s.Listed).OrderBy(s => pending.Number(s.Candidate.Choice)).ToArray();
            if (listed.Length != 0 && zones.Count != 0) {
                var sizes = listed.Select(s => LabelSize(s.Text)).ToArray();
                var rows = WorldPick.ListBeside(lastPick.x, lastPick.y, sizes.Max(size => size.x), sizes.Select(size => (double)size.y).ToArray(), ScreenRect.FromEdges(0, 0, Screen.width, Screen.height));
                for (var i = 0; i < listed.Length; i++) {
                    listed[i].Label = Rect.MinMaxRect((float)rows[i].X, (float)rows[i].Y, (float)rows[i].Right, (float)rows[i].Bottom);
                    labels.Add(listed[i]);
                }
            }
            foreach (var shape in shapes.Where(s => s.Text != null && !s.Listed)) {
                shape.Label = PlaceLabel(shape, shapes, uiBounds, zones);
                if (shape.Label.width > 0) labels.Add(shape);
            }
            var oldColor = GUI.color;
            var oldContent = GUI.contentColor;
            try {
                GUI.contentColor = Color.white;
                DrawTracks(shapes, zones);
                DrawOutlines(shapes, zones);
                foreach (var zone in zones) {
                    GUI.BeginGroup(zone);
                    try {
                        // A candidate that a row points at is drawn over the others on its spot.
                        // Neutral markers first, then hovered, clicked and related ones, then the one a row points at:
                        // on a spot that several share, the candidate shows in its colour, not under a neutral marker.
                        foreach (var shape in shapes.OrderBy(s => CommandBand.DrawRank(s.Detailed, s.Strong))) DrawShape(shape, zone.position);
                        foreach (var label in labels) {
                            if (!zone.Overlaps(label.Label)) continue;
                            var rect = Offset(label.Label, zone.position);
                            GUI.color = Color.white;
                            GUI.DrawTexture(rect, backing);
                            // The row of the list under the pointer is filled, as the native dropdown marks its row.
                            if (label.Listed && label.Label.Contains(pointer)) {
                                var fill = Tone(CommandBand.WorldMark.CandidateHover);
                                GUI.color = new Color(fill.r, fill.g, fill.b, CommandBand.RowFill);
                                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                            }
                            GUI.color = label.Color;
                            GUI.DrawTexture(new Rect(rect.x, rect.y, 3, rect.height), Texture2D.whiteTexture);
                            GUI.color = Color.white;
                            GUI.Label(rect, label.Text, textStyle);
                        }
                    } finally { GUI.EndGroup(); }
                }
            } finally { GUI.color = oldColor; GUI.contentColor = oldContent; }
#if ISSUE42_OBSERVATION
            } finally { observing = false; }
#endif
        }

        Shape Project(SpatialCandidate candidate, Camera camera, Vector3 origin) => Project(candidate, camera, origin, ObjectMarkingGeometry.FrameDistance, out _);
        // frameDistance is the distance beyond which the object has no frame (ObjectMarkingGeometry.Frame); measured
        // is what was measured of a scene object, also when the object is not in view and has no shape.
        Shape Project(SpatialCandidate candidate, Camera camera, Vector3 origin, double frameDistance, out Measured measured) {
            measured = null;
            if (candidate.Points.Length == 0) return null;
            // The surface heights are kept by world place: an origin move makes every place another one.
            if (origin != groundOrigin) { grounds.Clear(); groundOrigin = origin; }
            var shape = new Shape { Candidate = candidate };
            var anchor = ScreenPoint(camera, candidate.Position, origin);
            if (candidate.Observation != null && candidate.Choice.Handle is GameObject go && go != null) {
                var renderers = Renderers(go, out var outlines);
                // A crossing pad lies on its track: the track's ballast and the road it crosses are not ground it
                // stands in, so the pad is framed whole and not cut off where its ends meet the road.
                var ground = PadFamilies.Contains(candidate.Observation.Family) ? null : (Func<double, double, double?>)GroundAt;
                Dictionary<Renderer, KeyValuePair<LODGroup, int>> lods = null;
                var parts = new List<ObjectMarkingPart>();
                // What is read once per renderer and per place is read within a share of the frame: a renderer whose
                // kind is not known yet, once that share is used up, leaves its object without its parts until a
                // later frame, so the first view after a load builds up over some frames instead of in one.
                var waits = false;
                // The mesh renderers that are on but hidden without a known box yet.
                var unseen = 0;
#if ISSUE42_OBSERVATION
                var decisions = observing ? new List<string>() : null;
                var skipped = observing ? new List<string>() : null;
#else
                List<string> decisions = null;
#endif
                for (int index = 0; index < renderers.Length; index++) {
                    var renderer = renderers[index];
                    var name = "#" + index;
                    if (renderer == null) continue;
                    // A renderer switched off by the scene or a modder is passed over; one the game's culling hides
                    // now still outlines the object, from its mesh.
                    if (index >= outlines.Length || !outlines[index]) {
#if ISSUE42_OBSERVATION
                        skipped?.Add(name + ": skipped, switched off");
#endif
                        continue;
                    }
                    var shows = renderer.enabled && renderer.gameObject.activeInHierarchy;
                    // A static batch moves its meshes into one combined mesh, whose bounds are not the renderer's; only
                    // the world-aligned box stays per renderer, and only while it shows. That box is kept, in the
                    // object's own space, for while the game's culling hides the renderer
                    // (ObjectMarkingGeometry.RendererBox).
                    var hasLocal = LocalBox(renderer, out var local, out var matrix);
                    WorldPoint[] own = null;
                    if (hasLocal) {
                        own = new WorldPoint[8];
                        for (int i = 0; i < 8; i++)
                            own[i] = Point(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    }
                    WorldPoint[] visible = shows ? BoxCorners(renderer.bounds) : null;
                    if (shows && own == null) { var toLocal = renderer.transform.worldToLocalMatrix; keptBoxes[renderer] = visible.Select(p => Point(toLocal.MultiplyPoint3x4(Vector(p)))).ToArray(); }
                    keptBoxes.TryGetValue(renderer, out var kept);
                    var toWorld = renderer.transform.localToWorldMatrix;
                    var box = ObjectMarkingGeometry.RendererBox(shows, visible, own, kept, p => Point(toWorld.MultiplyPoint3x4(Vector(p))));
                    // A hidden renderer without a box of its own and never seen since the world loaded adds nothing.
                    if (box == null) {
#if ISSUE42_OBSERVATION
                        skipped?.Add(name + ": skipped, hidden without a known box");
#endif
                        // It still makes its object visible: a mesh renderer outlines the object once it shows, so the
                        // object shows no helper marker (HelperMarkers.Shows). Its kind is read within the pass's share.
                        if (!kinds.TryGetValue(renderer, out var hiddenKind)) {
                            if (budget.Exhausted) { waits = true; break; }
                            hiddenKind = budget.Read(() => Kind(renderer, go, ref lods));
                        }
                        if (hiddenKind == ObjectMarkingPartKind.Mesh) unseen++;
                        continue;
                    }
                    // A mesh renderer that shows outlines with its own oriented box inside its world box; a hidden one
                    // with the box RendererBox gave.
                    var oriented = shows ? (renderer is MeshRenderer ? own : null) : box.ToArray();
                    Bounds bounds;
                    if (shows) bounds = renderer.bounds;
                    else {
                        // A hidden renderer reports no world box: the one around its box stands in.
                        bounds = new Bounds(new Vector3((float)oriented[0].X, (float)oriented[0].Y, (float)oriented[0].Z), Vector3.zero);
                        foreach (var corner in oriented) bounds.Encapsulate(new Vector3((float)corner.X, (float)corner.Y, (float)corner.Z));
                    }
                    if (bounds.size.sqrMagnitude < .000001f) {
#if ISSUE42_OBSERVATION
                        skipped?.Add(name + ": skipped, empty bounds");
#endif
                        continue;
                    }
                    // A renderer that is switched off was passed over above and is never read. The kind of one that
                    // shows is read once, within the pass's share; an object with a kind still to read waits whole.
                    if (!kinds.TryGetValue(renderer, out var kind)) {
                        if (budget.Exhausted) { waits = true; break; }
                        kind = budget.Read(() => Kind(renderer, go, ref lods));
                    }
                    parts.Add(new ObjectMarkingPart(name, Point(bounds.center), Point(bounds.extents), oriented, kind, ground));
                }
                // A storage pile has no renderer of its own: the box it lays its logs out in stands in, from its layout
                // settings, so a pile is framed like any other visible part whether or not the camera has culled it.
                if (!waits)
                    foreach (var pile in Piles(go)) {
                        var oriented = PileBox(pile);
                        if (oriented == null) continue;
                        var bounds = new Bounds(Vector(oriented[0]), Vector3.zero);
                        foreach (var corner in oriented) bounds.Encapsulate(Vector(corner));
                        parts.Add(new ObjectMarkingPart("pile " + pile.name, Point(bounds.center), Point(bounds.extents), oriented, ObjectMarkingPartKind.Mesh, ground));
                    }
                if (waits) parts.Clear();
                var view = camera.pixelRect;
                // One frame per object, around the piece its marker belongs to. Picking uses the same rectangle.
                // The box of an object of several parts stands in the object's own orientation.
                var result = ObjectMarkingGeometry.Frame(Point(candidate.Position + origin), Point(camera.transform.position), parts, Projection(camera), ViewOf(camera), decisions, frameDistance, Basis(go.transform),
                    ObjectMarkingGeometry.FramesWhole(candidate.Observation.Family));
                measured = new Measured { Frame = result };
                if (HelperMarkers.Shows(result, waits, unseen)) {
                    // An invisible object, without a renderer that outlines it, shows its helper marker: the square
                    // marker on its pivot, read from its transform now, so it follows every placement change, gizmo
                    // preview and Undo, and the frame of its colliders' bounds or the least frame about the marker.
                    var pivot = go.transform.position;
                    var helper = HelperMarkers.Mark(Point(pivot), Point(camera.transform.position), Colliders(go), Projection(camera), ViewOf(camera), frameDistance, Basis(go.transform));
                    anchor = ScreenPoint(camera, pivot, Vector3.zero);
                    if (helper.Frame.HasValue) {
                        var frame = ObjectMarkingGeometry.VisibleFrame(helper.Frame.Value);
                        var rect = Rect.MinMaxRect((float)frame.X, (float)frame.Y, (float)frame.Right, (float)frame.Bottom);
                        shape.Objects.Add(rect);
                        shape.Outline = helper.Outline;
                        // A pivot behind the camera with its collider frame in view: the marker stands on the frame.
                        anchor = MarkerOn(camera, Point(pivot), rect);
                    }
                } else if (result.Frame.HasValue) {
                    var frame = result.Frame.Value;
                    // The frame is never smaller than the marker with its margin. The marker stands on the center of
                    // the box the frame outlines, a point of the object, so it stays there as the camera zooms and
                    // turns, also outside the view; only where that point lies behind the camera does it stand on the center of the frame's rectangle.
                    frame = ObjectMarkingGeometry.VisibleFrame(frame);
                    var rect = Rect.MinMaxRect((float)frame.X, (float)frame.Y, (float)frame.Right, (float)frame.Bottom);
                    shape.Objects.Add(rect);
                    shape.Outline = ObjectMarkingGeometry.VisibleOutline(result.Outline);
                    anchor = MarkerOn(camera, result.Marker, rect);
                } else if (result.Marker.HasValue && InFront(camera, ScreenPoint(camera, Vector(result.Marker.Value), Vector3.zero))) {
                    // Beyond the frame distance the marker stays on the middle of the object.
                    anchor = ScreenPoint(camera, Vector(result.Marker.Value), Vector3.zero);
                    if (frameDistance > ObjectMarkingGeometry.FrameDistance || ObjectMarkingGeometry.FramesMarker((camera.transform.position - (candidate.Position + origin)).magnitude)) {
                        shape.Outline = ObjectMarkingGeometry.MarkerOutline(anchor.x, anchor.y);
                        var half = (float)(ObjectMarkingGeometry.MarkerSize / 2 + ObjectMarkingGeometry.MarkerMargin);
                        shape.Objects.Add(new Rect(anchor.x - half, anchor.y - half, 2 * half, 2 * half));
                    }
                } else if (InFront(camera, anchor) && (frameDistance > ObjectMarkingGeometry.FrameDistance || ObjectMarkingGeometry.FramesMarker((camera.transform.position - (candidate.Position + origin)).magnitude))) {
                    // Its parts are still being read (waits), or a hidden renderer's box is not known yet (unseen):
                    // until then the object has a frame within the frame distance, about its marker. An object that is asked for at its group's frame distance has it
                    // wherever its marker stands.
                    shape.Outline = ObjectMarkingGeometry.MarkerOutline(anchor.x, anchor.y);
                    var half = (float)(ObjectMarkingGeometry.MarkerSize / 2 + ObjectMarkingGeometry.MarkerMargin);
                    shape.Objects.Add(new Rect(anchor.x - half, anchor.y - half, 2 * half, 2 * half));
                }
#if ISSUE42_OBSERVATION
                if (observing && !(result.Distance > ObservationRange)) Observe(candidate, renderers, lods ?? Lods(go), decisions, skipped, result, view);
#endif
            }
            if (candidate.ControlPoints != null) {
                if (trackRadius == 0) {
                    trackRadius = (float)Native.Read(Native.Type("Game.MapEditor.TrackClickPicker"), "SnapRadius");
                    if (Single.IsNaN(trackRadius) || Single.IsInfinity(trackRadius) || trackRadius <= 0)
                        throw new InvalidOperationException("Native track pick radius unavailable");
                }
                // Clip the physical corridor and its end geometry before perspective division.
                // An offscreen projected width cannot become a screen-wide stroke or hit area.
                var matrix = camera.projectionMatrix * camera.worldToCameraMatrix;
                var viewport = camera.pixelRect;
                shape.Track = WorldRibbonGeometry.ProjectCurve(candidate.ControlPoints.Select(p => new WorldPoint(p.x, p.y, p.z)).ToArray(),
                    trackRadius, p => {
                        var clip = matrix * new Vector4((float)(p.X + origin.x), (float)(p.Y + origin.y), (float)(p.Z + origin.z), 1);
                        return new ClipPoint(clip.x, clip.y, clip.z, clip.w);
                    }, ScreenRect.FromEdges(viewport.xMin, Screen.height - viewport.yMax, viewport.xMax, Screen.height - viewport.yMin), TrackMinimumRadius);
            }
            // A path, such as the track a car cut stands on, is a ribbon half a car wide.
            if (candidate.Path != null && candidate.Path.Length >= 2) {
                var matrix = camera.projectionMatrix * camera.worldToCameraMatrix;
                var viewport = camera.pixelRect;
                shape.Track = WorldRibbonGeometry.ProjectPath(candidate.Path.Select(p => new WorldPoint(p.x, p.y, p.z)).ToArray(), StarterCutRule.CarHalfWidth, p => {
                    var clip = matrix * new Vector4((float)(p.X + origin.x), (float)(p.Y + origin.y), (float)(p.Z + origin.z), 1);
                    return new ClipPoint(clip.x, clip.y, clip.z, clip.w);
                }, ScreenRect.FromEdges(viewport.xMin, Screen.height - viewport.yMax, viewport.xMax, Screen.height - viewport.yMin), 2);
            }
            if (!InFront(camera, anchor)) {
                if (shape.Track == null || shape.Track.Ribbon.Count == 0) return null;
                var triangle = shape.Track.Ribbon[0];
                anchor = new Vector3((float)((triangle.A.X + triangle.B.X + triangle.C.X) / 3),
                    (float)((triangle.A.Y + triangle.B.Y + triangle.C.Y) / 3), camera.nearClipPlane);
            }
            shape.Marker = new Rect(anchor.x - MarkerSize / 2, anchor.y - MarkerSize / 2, MarkerSize, MarkerSize);
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            if (!screen.Overlaps(shape.Marker) && !shape.Objects.Any(screen.Overlaps) && (shape.Track == null || shape.Track.Ribbon.Count == 0)) return null;
            return shape;
        }

        // The font of the native compact text, when its font asset still carries the source font. IMGUI cannot draw the
        // signed-distance-field asset itself.
        static Font NativeFont() {
            try {
                foreach (var asset in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>())
                    if (asset != null && asset.name == "Montserrat-Medium SDF" && asset.sourceFontFile != null) return asset.sourceFontFile;
            } catch { }
            return null;
        }
        // The name a label shows: the object's own name without its family and path, so a label has one colon. The panel
        // holds the full identity.
        static string Short(string label) {
            var text = label ?? "";
            var colon = text.IndexOf(": ", StringComparison.Ordinal);
            if (colon >= 0) text = text.Substring(colon + 2);
            var slash = text.LastIndexOf('/');
            return slash >= 0 ? text.Substring(slash + 1) : text;
        }
        string ObjectLabel(SpatialCandidate candidate, string prefix, IEnumerable<string> offers = null) {
            var observation = candidate.Observation;
            if (CommandBand.IsAddedFamily(observation?.Family)) {
                var state = offers != null && !observation.Active ? " [inactive]" : "";
                var name = InventoryPresentationState.NarrowName(observation.Family, observation.DisplayName, LabelMaximumWidth,
                    value => textStyle.CalcSize(new GUIContent(prefix + value + state)).x);
                return prefix + name + state;
            }
            return prefix + (offers != null ? CommandBand.ChoiceLabel(candidate.Choice.Label, offers, 60, observation)
                : Short(observation?.DisplayName ?? candidate.Choice.Label));
        }
        static Vector3 ScreenPoint(Camera camera, Vector3 point, Vector3 origin) {
            var projected = camera.WorldToScreenPoint(point + origin);
            projected.y = Screen.height - projected.y;
            return projected;
        }
        static WorldPoint Point(Vector3 v) => new WorldPoint(v.x, v.y, v.z);
        static Vector3 Vector(WorldPoint p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        // The families of a crossing pad, whose frame is not cut off at the ground.
        static readonly HashSet<string> PadFamilies = new HashSet<string>(StringComparer.Ordinal) { "Crossing pad marker", "Crossing pad" };
        // Where a marker with a frame stands on screen: on its world point when that lies in front of the camera,
        // otherwise on the rectangle's center.
        static Vector3 MarkerOn(Camera camera, WorldPoint? marker, Rect rect) {
            var point = marker.HasValue ? ScreenPoint(camera, Vector(marker.Value), Vector3.zero) : Vector3.zero;
            var inFront = marker.HasValue && InFront(camera, point);
            var at = ObjectMarkingGeometry.MarkerScreen(inFront, point.x, point.y, ScreenRect.FromEdges(rect.xMin, rect.yMin, rect.xMax, rect.yMax));
            return new Vector3((float)at.X, (float)at.Y, inFront ? point.z : camera.nearClipPlane);
        }
        // The level of detail each renderer of an object belongs to, by its LOD group.
        static Dictionary<Renderer, KeyValuePair<LODGroup, int>> Lods(GameObject go) {
            var lods = new Dictionary<Renderer, KeyValuePair<LODGroup, int>>();
            foreach (var group in go.GetComponentsInChildren<LODGroup>(true)) {
                var levels = group.GetLODs();
                for (int level = 0; level < levels.Length; level++)
                    foreach (var renderer in levels[level].renderers)
                        if (renderer != null && !lods.ContainsKey(renderer)) lods.Add(renderer, new KeyValuePair<LODGroup, int>(group, level));
            }
            return lods;
        }
        // What the renderer is to the outline (ObjectMarkingGeometry.KindOf), from its type, the component types on its
        // object with their base types, its shadow mode and its level of detail.
        ObjectMarkingPartKind Kind(Renderer renderer, GameObject go, ref Dictionary<Renderer, KeyValuePair<LODGroup, int>> lods) {
            if (kinds.TryGetValue(renderer, out var known)) return known;
            if (lods == null) lods = Lods(go);
            var components = new List<string>();
            foreach (var behaviour in renderer.GetComponents<MonoBehaviour>())
                for (var type = behaviour == null ? null : behaviour.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                    components.Add(type.FullName);
            var kind = ObjectMarkingGeometry.KindOf(renderer.GetType().FullName, components, renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly,
                lods.TryGetValue(renderer, out var lod) ? lod.Value : 0);
            kinds[renderer] = kind;
            return kind;
        }
#if ISSUE42_OBSERVATION
        // Frame diagnostics: one block per candidate within range when a world choice opens and after each camera step.
        const float ObservationStep = 25f;
        const double ObservationRange = 1500;
        object observedChoice;
        Vector3 observedCamera;
        bool observing;
        void BeginObservation(PendingSelection pending, SpatialCandidate source, Camera camera) {
            var choice = (object)pending ?? source;
            observing = choice != null && (!ReferenceEquals(choice, observedChoice) || (camera.transform.position - observedCamera).magnitude >= ObservationStep);
            if (!observing) return;
            observedChoice = choice; observedCamera = camera.transform.position;
            Debug.Log("[Issue42Frames] pass camera=" + Vector(camera.transform.position) + " view=" + camera.pixelRect.width.ToString("0", CultureInfo.InvariantCulture) + "x" +
                camera.pixelRect.height.ToString("0", CultureInfo.InvariantCulture) + " near=" + Number(camera.nearClipPlane) + " far=" + Number(camera.farClipPlane) +
                " frameDistance=" + Number(ObjectMarkingGeometry.FrameDistance));
        }
        static void Observe(SpatialCandidate candidate, Renderer[] renderers, Dictionary<Renderer, KeyValuePair<LODGroup, int>> lods, List<string> decisions,
            List<string> skipped, ObjectMarkingFrame result, Rect view) {
            var text = new System.Text.StringBuilder();
            text.Append("[Issue42Frames] candidate ").Append(candidate.Choice.Label).Append(" distance=").Append(Number(result.Distance))
                .Append(result.Frame.HasValue ? " frame=" + Number(result.Frame.Value.Width / view.width) + "x" + Number(result.Frame.Value.Height / view.height) + " of view" : " marker only");
            var lines = decisions.Concat(skipped).ToList();
            for (int index = 0; index < renderers.Length; index++) {
                var renderer = renderers[index];
                var name = "#" + index;
                var decision = String.Join(" / ", lines.Where(l => l.StartsWith(name + ":", StringComparison.Ordinal)).Select(l => l.Substring(name.Length + 2)));
                var lod = lods.TryGetValue(renderer, out var level) ? BoardScene.PathOf(level.Key.transform) + " LOD" + level.Value : "none";
                text.Append("\n  ").Append(name).Append(' ').Append(BoardScene.PathOf(renderer.transform)).Append(" type=").Append(renderer.GetType().Name)
                    .Append(" size=").Append(Vector(renderer.bounds.size)).Append(" staticBatch=").Append(renderer.isPartOfStaticBatch)
                    .Append(" lod=").Append(lod).Append(" active=").Append(renderer.enabled && renderer.gameObject.activeInHierarchy)
                    .Append(" decision=").Append(decision.Length == 0 ? "none" : decision);
            }
            Debug.Log(text.ToString());
        }
        static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        static string Vector(Vector3 v) => Number(v.x) + "/" + Number(v.y) + "/" + Number(v.z);
#endif
        static bool InFront(Camera camera, Vector3 point) => point.z >= camera.nearClipPlane && point.z <= camera.farClipPlane;
        static Rect Offset(Rect rect, Vector2 origin) => new Rect(rect.position - origin, rect.size);

        static List<Rect> VisibleWorld(IReadOnlyList<Rect> exclusions) {
            var zones = new List<Rect> { new Rect(0, 0, Screen.width, Screen.height) };
            foreach (var block in exclusions) {
                var next = new List<Rect>();
                foreach (var rect in zones) {
                    if (!rect.Overlaps(block)) { next.Add(rect); continue; }
                    var cut = Rect.MinMaxRect(Mathf.Max(rect.xMin, block.xMin), Mathf.Max(rect.yMin, block.yMin),
                        Mathf.Min(rect.xMax, block.xMax), Mathf.Min(rect.yMax, block.yMax));
                    if (cut.yMin > rect.yMin) next.Add(Rect.MinMaxRect(rect.xMin, rect.yMin, rect.xMax, cut.yMin));
                    if (cut.yMax < rect.yMax) next.Add(Rect.MinMaxRect(rect.xMin, cut.yMax, rect.xMax, rect.yMax));
                    if (cut.xMin > rect.xMin) next.Add(Rect.MinMaxRect(rect.xMin, cut.yMin, cut.xMin, cut.yMax));
                    if (cut.xMax < rect.xMax) next.Add(Rect.MinMaxRect(cut.xMax, cut.yMin, rect.xMax, cut.yMax));
                }
                zones = next;
            }
            return zones;
        }

        // A label stands centred under its marker (ObjectMarkingGeometry.LabelUnder), in the order the markings are
        // listed. It is drawn and clicked only where the world shows: a part under a window is cut off.
        // A label covers no native panel and no part of another shown square marker (ObjectMarkingGeometry.LabelUnder):
        // a covered panel would cut the label off, and a covered marker could not be clicked. Only a marker on the
        // label's own spot, which a click cannot tell from its own, does not count.
        Rect PlaceLabel(Shape shape, List<Shape> shapes, IReadOnlyList<Rect> panels, List<Rect> zones) {
            if (zones.Count == 0) return default;
            var marker = shape.Marker;
            var size = LabelSize(shape.Text);
            ScreenRect Area(Rect r) => ScreenRect.FromEdges(r.xMin, r.yMin, r.xMax, r.yMax);
            var placed = labels.Select(s => Area(s.Label)).ToArray();
            var blocked = panels.Select(Area).Concat(shapes.Where(s => !ReferenceEquals(s, shape) && s.Marker.width > 0 && (s.Marker.center - marker.center).sqrMagnitude >= WorldPick.SameSpot * WorldPick.SameSpot).Select(s => Area(s.Marker))).ToArray();
            var rect = ObjectMarkingGeometry.LabelUnder(Area(marker), size.x, size.y, ScreenRect.FromEdges(0, 0, Screen.width, Screen.height), placed, blocked);
            return Rect.MinMaxRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);
        }

        static float LabelMaximumWidth => Mathf.Min(480, Screen.width);
        Vector2 LabelSize(string text) {
            var content = new GUIContent(text);
            // Use the final pixel width for both wrapping and drawing. Rounding a natural width
            // down can introduce a line that was absent during measurement.
            float width = Mathf.Min(LabelMaximumWidth, Mathf.Ceil(textStyle.CalcSize(content).x));
            return new Vector2(width, Mathf.Ceil(textStyle.CalcHeight(content, width)));
        }

        Material StrokeMaterial() {
            if (trackMaterial == null) {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("World track marking shader unavailable");
                trackMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                trackMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                trackMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                trackMaterial.SetInt("_Cull", (int)CullMode.Off);
                trackMaterial.SetInt("_ZWrite", 0);
                trackMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            }
            return trackMaterial;
        }
        // The frames of the hovered, clicked and related objects and of the inspected one: each the line of its
        // outline, clipped to where the world shows. A candidate a row points at is drawn last and heavier.
        void DrawOutlines(List<Shape> shapes, List<Rect> zones) {
            // An outline with a point that is not finite is not drawn: one such point would spoil the batch.
            var framed = shapes.Where(shape => shape.Detailed && shape.Outline != null && shape.Outline.Count >= 3 && shape.Outline.All(q => !Double.IsNaN(q.X) && !Double.IsInfinity(q.X) && !Double.IsNaN(q.Y) && !Double.IsInfinity(q.Y)))
                .OrderBy(shape => shape.Strong ? 1 : 0).ToArray();
            if (zones.Count == 0 || framed.Length == 0) return;
            var material = StrokeMaterial();
            var visible = zones.Select(zone => ScreenRect.FromEdges(zone.xMin, zone.yMin, zone.xMax, zone.yMax)).ToArray();
            GL.PushMatrix();
            try {
                if (!material.SetPass(0)) throw new InvalidOperationException("World marking shader pass unavailable");
                GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                GL.Begin(GL.TRIANGLES);
                try {
                    foreach (var shape in framed)
                        DrawTriangles(WorldStrokeGeometry.ClipTriangles(ObjectMarkingGeometry.OutlineStroke(shape.Outline, shape.Strong ? 5 : 2), visible), shape.Color);
                } finally { GL.End(); }
            } finally { GL.PopMatrix(); GL.InvalidateState(); }
        }

        void DrawTracks(List<Shape> shapes, List<Rect> zones) {
            // A track's ribbon is drawn for a hovered or clicked candidate and for a relation; a candidate that only
            // carries its marker has none.
            if (zones.Count == 0 || !shapes.Any(shape => shape.Detailed && shape.Track != null && shape.Track.Ribbon.Count != 0)) return;
            StrokeMaterial();
            var visible = zones.Select(zone => ScreenRect.FromEdges(zone.xMin, zone.yMin, zone.xMax, zone.yMax)).ToArray();
            GL.PushMatrix();
            try {
                if (!trackMaterial.SetPass(0)) throw new InvalidOperationException("World track marking shader pass unavailable");
                // Geometry is already clipped in top-left screen pixels. No rotated GUI matrix or
                // group scissor is relied upon to keep a wide stroke out of screen UI.
                GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                GL.Begin(GL.TRIANGLES);
                try {
                    foreach (var shape in shapes.Where(s => s.Detailed && s.Track != null)) {
                        // The pieces of a ribbon overlap one another. Drawn one by one they would blend twice where
                        // they do, in patches that move with the camera; drawn as the area they cover, each pixel
                        // once, the ribbon has one tint from every angle.
                        GL.Color(new Color(shape.Color.r, shape.Color.g, shape.Color.b, CommandBand.WorldFill));
                        foreach (var span in WorldStrokeGeometry.Coverage(WorldStrokeGeometry.ClipTriangles(shape.Track.Ribbon, visible))) {
                            float left = (float)span.X, right = (float)span.Right, top = (float)span.Y, bottom = (float)span.Bottom;
                            GL.Vertex3(left, top, 0); GL.Vertex3(right, top, 0); GL.Vertex3(right, bottom, 0);
                            GL.Vertex3(left, top, 0); GL.Vertex3(right, bottom, 0); GL.Vertex3(left, bottom, 0);
                        }
                        DrawTriangles(WorldStrokeGeometry.ClipTriangles(shape.Track.Centerline, visible), shape.Color);
                    }
                } finally { GL.End(); }
            } finally { GL.PopMatrix(); GL.InvalidateState(); }
        }

        static void DrawTriangles(IReadOnlyList<ScreenTriangle> triangles, Color color) {
            GL.Color(color);
            foreach (var triangle in triangles) {
                GL.Vertex3((float)triangle.A.X, (float)triangle.A.Y, 0);
                GL.Vertex3((float)triangle.B.X, (float)triangle.B.Y, 0);
                GL.Vertex3((float)triangle.C.X, (float)triangle.C.Y, 0);
            }
        }

        static void DrawShape(Shape shape, Vector2 origin) {
            var marker = Offset(shape.Marker, origin);
            if (marker.width == 0) return;
            // A pointed-at candidate's marker is larger and filled with its colour, so it stands out on a spot it shares.
            if (shape.Strong) marker = new Rect(marker.x - 6, marker.y - 6, marker.width + 12, marker.height + 12);
            GUI.color = shape.Strong ? shape.Color : new Color(.08f, .08f, .08f, 1f);
            GUI.DrawTexture(marker, Texture2D.whiteTexture);
            Frame(marker, shape.Strong ? Color.white : shape.Color, 2f);
            GUI.color = shape.Strong ? new Color(.08f, .08f, .08f, 1f) : shape.Color;
            foreach (var bar in ObjectMarkingGeometry.MarkerSymbol(shape.Group))
                GUI.DrawTexture(new Rect(marker.center.x + (float)bar.X, marker.center.y + (float)bar.Y, (float)bar.Width, (float)bar.Height), Texture2D.whiteTexture);
        }
        static void Frame(Rect rect, Color color, float weight) {
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, weight), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - weight, rect.width, weight), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, weight, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - weight, rect.y, weight, rect.height), Texture2D.whiteTexture);
        }
    }
}
