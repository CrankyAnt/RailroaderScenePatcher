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
        readonly Dictionary<Renderer, ObjectMarkingBoxKind> kinds = new Dictionary<Renderer, ObjectMarkingBoxKind>();

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
        // With them, whether the game moves one of them while it runs (GroupWorld.MovesWhileRunning).
        sealed class Listed { internal Renderer[] Renderers; internal bool[] Outlines; internal bool Moves; internal int Until; }
        readonly Dictionary<GameObject, Listed> listed = new Dictionary<GameObject, Listed>();
        Renderer[] Renderers(GameObject go, out bool[] outlines, out bool moves) {
            if (listed.TryGetValue(go, out var kept) && Time.frameCount < kept.Until) { outlines = kept.Outlines; moves = kept.Moves; return kept.Renderers; }
            var list = ListRenderers(go);
            outlines = new bool[list.Length]; moves = false;
            for (var i = 0; i < list.Length; i++) {
                outlines[i] = OutlinesObject(list[i]);
                if (!moves && list[i] != null && GroupWorld.MovesWhileRunning(list[i].transform)) moves = true;
            }
            listed[go] = new Listed { Renderers = list, Outlines = outlines, Moves = moves, Until = Time.frameCount + 90 + (go.GetInstanceID() & 63) };
            return list;
        }
        // The game's components that switch renderers off by camera distance and hold the renderers they switch: a
        // renderer culler (_cullRenderers) and the builder of a curve mesh such as a crossing pad (_renderers).
        static readonly Type rendererCuller = Native.Type(SceneObjectRules.RendererCuller), curveBuilder = Native.Type("BezierCurveMesh.CurveMeshBuilderBase");
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
        // The storage piles and log ponds at and below an object that outline it: listed with its renderers, and passed
        // over while the scene or a modder switched them off, as a renderer is. Both lay out their logs through the
        // game's instancer and have no renderer of their own.
        static readonly Type pileType = Native.Type(SceneObjectRules.StoragePile), pondType = Native.Type(SceneObjectRules.LogPond);
        static readonly System.Reflection.FieldInfo pileWidth = pileType?.GetField("width", Native.Flags), pileHeight = pileType?.GetField("maxHeight", Native.Flags),
            pileProfile = (pileType ?? pondType)?.BaseType?.GetField("profile", Native.Flags), pondWidth = pondType?.GetField("width", Native.Flags), pondLength = pondType?.GetField("length", Native.Flags);
        readonly Dictionary<GameObject, KeyValuePair<int, Component[]>> piles = new Dictionary<GameObject, KeyValuePair<int, Component[]>>();
        IEnumerable<Component> Piles(GameObject go) {
            if (pileType == null && pondType == null) return Array.Empty<Component>();
            if (!piles.TryGetValue(go, out var kept) || Time.frameCount >= kept.Key)
                piles[go] = kept = new KeyValuePair<int, Component[]>(Time.frameCount + 90 + (go.GetInstanceID() & 63),
                    (pileType == null ? new Component[0] : go.GetComponentsInChildren(pileType, true)).Concat(pondType == null ? new Component[0] : go.GetComponentsInChildren(pondType, true)).ToArray());
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
            if (pondType != null && pondType.IsInstanceOfType(pile)) return PondBox(pile);
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
        // The eight corners of the box a log pond lays its logs out in (SceneObjectRules.LogPondBox): its width across,
        // its length plus width along, and one log's radius up and down from the water line. Null when the log pond has
        // no profile or its settings cannot be read.
        static WorldPoint[] PondBox(Component pond) {
            try {
                var profile = pileProfile?.GetValue(pond);
                if (profile == null || pondWidth == null || pondLength == null) return null;
                var box = SceneObjectRules.LogPondBox((float)pondWidth.GetValue(pond), (float)pondLength.GetValue(pond), Convert.ToSingle(Native.Read(profile, "EffectiveRadius")));
                var centre = new Vector3((float)box.Item1[0], (float)box.Item1[1], (float)box.Item1[2]); var half = new Vector3((float)box.Item2[0], (float)box.Item2[1], (float)box.Item2[2]);
                var matrix = pond.transform.localToWorldMatrix; var corners = new WorldPoint[8];
                for (var i = 0; i < 8; i++) corners[i] = Point(matrix.MultiplyPoint3x4(centre + Vector3.Scale(half, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                return corners;
            } catch { return null; }
        }
        // The bounds of an invisible object's colliders, at and below it, as boxes for its helper marker
        // (HelperMarkers.Mark): each collider's own box in the world, from its shape. Which colliders frame the object
        // (HelperMarkers.Framing: on, with every object up to the scene root on or off only through a culler; no
        // terrain collider; triggers only without a solid collider) is decided once and kept on the renderers' beat,
        // as the renderers' outlines are; the boxes of those colliders are read anew in each pass, so the frame
        // follows the object.
        // The terrain's collider type, read by name: its module is not referenced.
        const string TerrainColliderType = "UnityEngine.TerrainCollider";
        sealed class Framed { internal Collider[] All; internal IReadOnlyList<int> Framing; internal int Until; }
        readonly Dictionary<GameObject, Framed> colliders = new Dictionary<GameObject, Framed>();
        IReadOnlyList<ObjectMarkingBox> Colliders(GameObject go) {
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
                colliders[go] = kept = new Framed { All = all, Framing = HelperMarkers.Framing(facts), Until = Time.frameCount + 90 + (go.GetInstanceID() & 63) };
            }
            return HelperMarkers.ColliderBoxes(kept.Framing, i => kept.All[i] == null ? null : GroupWorld.Corners(kept.All[i]).Select(Point).ToArray());
        }
        // Where a candidate stands in the world now: the transform of its scene object, which follows every placement
        // change, gizmo preview and Undo, or its observed position for anything else.
        static Vector3 Live(SpatialCandidate candidate, Vector3 origin) =>
            candidate.Observation != null && candidate.Choice.Handle is GameObject go && go != null ? go.transform.position : candidate.Position + origin;
        // The families of the inventory objects on each scene object, from the inventory snapshot, read again when the
        // snapshot is another one: an object framed whole leaves out the renderers of some objects below it
        // (ObjectMarkingGeometry.WholeCounts).
        object familiesFrom;
        readonly Dictionary<GameObject, List<string>> families = new Dictionary<GameObject, List<string>>();
        List<string> FamiliesOf(GameObject go) {
            var snapshot = Inventory?.Invoke();
            if (!ReferenceEquals(snapshot, familiesFrom)) {
                familiesFrom = snapshot; families.Clear();
                if (snapshot != null)
                    foreach (var o in snapshot)
                        if (o?.Handle is GameObject owner && owner != null && o.Family != null) {
                            if (!families.TryGetValue(owner, out var list)) families[owner] = list = new List<string>();
                            list.Add(o.Family);
                        }
            }
            return go != null && families.TryGetValue(go, out var found) ? found : null;
        }
        // The families of the inventory objects on a renderer's scene object and above it, up to an object, nearest first.
        IEnumerable<string> FamiliesBetween(Renderer renderer, GameObject go) {
            for (var t = renderer.transform; t != null && t != go.transform; t = t.parent) {
                var on = FamiliesOf(t.gameObject);
                if (on != null) foreach (var family in on) yield return family;
            }
        }
        static bool Under(Renderer renderer, IReadOnlyList<GameObject> parts) {
            foreach (var part in parts) if (part != null && renderer.transform.IsChildOf(part.transform)) return true;
            return false;
        }
        // The renderers below the parts, with whether each outlines the object.
        static Renderer[] PartsOnly(Renderer[] renderers, ref bool[] outlines, IReadOnlyList<GameObject> parts) {
            var kept = new List<Renderer>(); var keptOutlines = new List<bool>();
            for (var i = 0; i < renderers.Length; i++) {
                var renderer = renderers[i];
                if (renderer == null || !Under(renderer, parts)) continue;
                kept.Add(renderer); keptOutlines.Add(i < outlines.Length && outlines[i]);
            }
            outlines = keptOutlines.ToArray();
            return kept.ToArray();
        }
        Renderer[] ListRenderers(GameObject go) {
            var own = go.GetComponentsInChildren<Renderer>(true);
            // A crossing controller is framed whole, around its setup, without its whistle signs and detection spans.
            var whole = FamiliesOf(go)?.FirstOrDefault(ObjectMarkingGeometry.FramesWhole);
            if (whole != null) own = own.Where(renderer => renderer != null && ObjectMarkingGeometry.WholeCounts(whole, FamiliesBetween(renderer, go))).ToArray();
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
                if (terrainLayer < 0) PatcherLog.Debug("Object frame clipping: Terrain layer missing; no ground clipping.");
                else if (trackLayer < 0) PatcherLog.Debug("Object frame clipping: Track layer missing; terrain clipping only.");
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
        internal void ClearSurface() { grounds.Clear(); listed.Clear(); colliders.Clear(); controlled.Clear(); piles.Clear(); groupedFrom = null; ForgetGeometry(); }
        // What was measured of the objects, and the last pass, are made anew.
        void ForgetGeometry() { geometry.Clear(); lodsOf.Clear(); reuse.Forget(); keptShapes = null; keptMeasured = null; keptGroups = null; }
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
            foreach (var gone in lodsOf.Keys.Where(key => key == null).ToArray()) lodsOf.Remove(gone);
            geometry.RemoveWhere(handle => !(handle is GameObject go) || go == null);
            rendererBoxes?.Prune();
        }
        // The world box of each static-batched renderer from when it last showed, in its object's own space: the box it
        // outlines with while the game's culling hides it. It is taken again whenever the renderer shows, and it
        // follows its object when the patcher moves it. Kept until the overlay is released.
        readonly Dictionary<Renderer, WorldPoint[]> keptBoxes = new Dictionary<Renderer, WorldPoint[]>();
        // The box that follows each renderer's mesh, read within the pass's share (RendererBoxes).
        RendererBoxes rendererBoxes;
        RendererBoxes Boxes => rendererBoxes ?? (rendererBoxes = new RendererBoxes(budget));
        internal static WorldPoint[] BoxCorners(Bounds b) {
            Vector3 c = b.center, e = b.extents;
            var corners = new WorldPoint[8];
            for (var i = 0; i < 8; i++) corners[i] = Point(c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            return corners;
        }

        internal void Release() {
            frameChecks.Clear(); writtenCameraLine = null;
            keptBoxes.Clear();
            rendererBoxes?.Clear();
            if (trackMaterial != null) UnityEngine.Object.Destroy(trackMaterial);
            trackMaterial = null;
            ClearInteraction();
            kinds.Clear();
            grounds.Clear();
            controlled.Clear();
            listed.Clear();
            colliders.Clear();
            ForgetGeometry();
        }

        internal void ClearInteraction() {
            inspectedMark = null;
            labels.Clear();
        }

        internal bool OverLabel(Vector2 pointer, IReadOnlyList<Rect> uiBounds) =>
            labels.Any(label => label.Candidate != null && label.Label.Contains(pointer)) && !uiBounds.Any(bounds => bounds.Contains(pointer));

        internal SelectionCandidate[] Pick(SpatialCandidate[] candidates, Camera camera, Vector3 origin, Vector2 pointer,
            IReadOnlyList<Rect> uiBounds, SelectionCandidate chosen, out bool listRow, out bool isCrossingSetup) {
            listRow = false;
            isCrossingSetup = false;
            // Native windows can move between the last repaint and this click.
            covered = uiBounds;
            budget.Begin();
            BeginPass(camera);
            if (covered.Any(r => r.Contains(pointer))) return Array.Empty<SelectionCandidate>();
            lastPick = pointer;
            var labelHits = labels.Where(s => s.Candidate != null && s.Label.Contains(pointer)).ToArray();
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
        // A complex that shows as one takes part with its square marker and frame as one object, and
        // stands for its objects. A candidate that is not in view has no marker and no frame to meet the pointer.
        WorldPickResult Resolve(SpatialCandidate[] candidates, Shape[] shapes, GroupLayout groups, Camera camera, Vector3 origin, Vector2 pointer) {
            PickTarget Target(Shape shape, bool marked) => shape == null ? new PickTarget(0, 0, false, false)
                : new PickTarget(shape.Marker.center.x, shape.Marker.center.y, marked, shape.InFrame(pointer) || (shape.Track != null && shape.Track.Contains(new MarkingPoint(pointer.x, pointer.y))));
            var targets = new PickTarget[shapes.Length];
            // The root of a near group with parts shows nothing of its own, so the pointer meets nothing of it.
            for (var i = 0; i < shapes.Length; i++) targets[i] = groups.Absent[i] ? Target(null, false) : Target(shapes[i], shapes[i] != null && Marked(shapes[i], candidates[i], camera, origin));
            return WorldPick.Resolve(pointer.x, pointer.y, targets, groups.Shown.Select(group => new PickGroup(Target(group.Shape, true), group.Members, group.Own, group.Unlisted, group.Kind)).ToArray());
        }

        // A complex that shows as one square marker: its objects among the candidates, as indices, those
        // of them that show on their own, those its choice list leaves out (its detection spans), what the group
        // shows and its one marking, or null when it is not in view.
        // Text is what its square marker says under the pointer: the group's name and that it is a group of scene objects.
        sealed class ShownGroup { internal int[] Members; internal int[] Own; internal int[] Unlisted; internal GroupShows Shows; internal Shape Shape; internal ComplexKind Kind; internal string Text; }
        // The groups that show as one from this camera, for each candidate whether its group stands for it, the
        // candidates that show their own frame at their group's frame distance (Complexes.OwnFrameDistance).
        // The inspected object has its frame at any distance.
        sealed class GroupLayout {
            internal ShownGroup[] Shown = new ShownGroup[0]; internal bool[] Hidden; internal readonly List<int> OwnAtGroupDistance = new List<int>();
            // The candidates that show nothing and that no shown group stands for: the root of a group with parts
            // at the group distance or less, where only its parts show.
            internal bool[] Absent;
        }
        // What one pass measured of a scene object: its frame, with the bounds and the boxes of its framed piece. A
        // group takes its bounds and its one box from these, without reading the object again.
        sealed class Measured { internal ObjectMarkingFrame Frame; }
        // The component facts of a group, read from the family the inventory gave an object: a crossing controller
        // anchors a grade-crossing setup, a track marker that is no crossing pad is, below that controller's root, a
        // whistle sign, and a track span there is a detection span that the controller manages.
        const string ControllerFamily = "Grade crossing controller", MarkerFamily = SceneFamilies.TrackMarker, SpanFamily = "Track span", ConstructionFamily = "Bridge construction scenery", PadFamily = "Crossing pad marker";
        // The inventory snapshot, set by the owner: a group holds inventory objects, whatever a choice offers, so
        // Select in world and a choice for a scene patch file show one crossing the same way.
        internal Func<IReadOnlyList<SceneObservation>> Inventory;
        // One group of the inventory: its objects that have a usable position, for each of them whether the group's
        // choice list offers it and its frame surrounds it (Complex.Listed), and the scene object of the
        // game component that anchors it, whose orientation the group's one box takes.
        // Stands is the index among Objects of a top scene object with parts, which stands for the
        // group (Complex.Root), or -1.
        sealed class InventoryGroup { internal SpatialCandidate[] Objects; internal bool[] Listed; internal GameObject Root; internal int Stands = -1; internal ComplexKind Kind; }
        // The groups are decided once per inventory snapshot, and again when the snapshot is another one or has
        // another number of entries, and after the scene may have changed (ClearSurface, on every move of the
        // native map editor's history). Without a snapshot they are decided from the candidates of the choice.
        object groupedFrom;
        int groupedCount;
        InventoryGroup[] inventoryGroups = new InventoryGroup[0];
        // For each group, the part of its marking that no camera changes (ObjectMarkingGroupPiece), kept while it holds:
        // a camera move then only measures the distance to its box and, between the group and the frame distance,
        // projects that box.
        ObjectMarkingGroupPiece[] groupPieces = new ObjectMarkingGroupPiece[0];
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
            groupedFrom = from; groupedCount = count; mappedFor = null; inventoryGroups = new InventoryGroup[0]; groupPieces = new ObjectMarkingGroupPiece[0];
            try {
                var all = (listed ? snapshot.ToArray() : candidates.Where(c => c.Observation != null).Select(c => c.Observation).ToArray()).Where(o => !ChosenInOutliner(o)).ToArray();
                // A root of the Scene object and Child object families anchors a scene object with child objects with the inventory objects
                // below it: its parts, and the objects of other families there.
                bool TopSceneObject(SceneObservation o) => SceneObjectRules.InFamilies(o.Family) && o.GroupPath == null && o.Handle is GameObject go && go != null && GroupWorld.IsTopObject(go);
                var roots = all.Select(TopSceneObject).ToArray();
                // What a game component names: the piles an industry's storage fills, and the scene objects whose
                // visibility a construction controller sets. A fact that cannot be read forms no group.
                var storage = new Dictionary<GameObject, GameObject>();
                try { storage = NativeInventory.StorageDisplays(); } catch (Exception e) { Skipped(null, "the industry storage displays could not be read: " + e.GetBaseException().Message); }
                GameObject[] Targets(SceneObservation o, GameObject go) {
                    if (o.Family != ConstructionFamily) return null;
                    try { return NativeInventory.ConstructionTargets(go); } catch (Exception e) { Skipped(go, "its construction targets could not be read: " + e.GetBaseException().Message); return null; }
                }
                // A crossing without a controller: its crossbucks and pads stand in one scene object. Where crossings
                // share that object, each crossbuck names its crossing's pad by marker ID, and so does the pad itself.
                bool Crossbuck(SceneObservation o) => SceneObjectRules.InFamilies(o.Family) && o.GroupPath == null && o.Category == SceneObjectCategories.GradeCrossing;
                var sharedPads = new HashSet<Tuple<GameObject, string>>(all.Where(o => Crossbuck(o) && o.Identity.PadMarkerId != null && o.Handle is GameObject go && go != null && go.transform.parent != null)
                    .Select(o => Tuple.Create(((GameObject)o.Handle).transform.parent.gameObject, o.Identity.PadMarkerId)));
                object CrossingOf(SceneObservation o, GameObject go) {
                    var crossbuck = Crossbuck(o);
                    if (!crossbuck && o.Family != PadFamily) return null;
                    var holder = go.transform.parent == null ? null : go.transform.parent.gameObject;
                    if (holder == null) return null;
                    var id = crossbuck ? o.Identity.PadMarkerId : InventoryPresentationState.MarkerIdOf(o);
                    return id != null && sharedPads.Contains(Tuple.Create(holder, id)) ? (object)Tuple.Create(holder, id) : holder;
                }
                if (all.Any(o => o.Family == ControllerFamily || o.Family == ConstructionFamily || Crossbuck(o)) || roots.Any(r => r) || storage.Count != 0) {
                    var found = new List<InventoryGroup>();
                    foreach (var group in Complexes.Of(all.Select((o, i) => {
                            if (!(o.Handle is GameObject go) || go == null) return null;
                            var targets = Targets(o, go);
                            return new GroupObject(go, Above(go), o.Family == ControllerFamily, o.Family == MarkerFamily, o.Family == SpanFamily, roots[i], o.GroupPath != null,
                                storage.TryGetValue(go, out var filledBy) ? filledBy : null, targets != null, targets, targets?.Select(Above), CrossingOf(o, go));
                        }).ToArray())) {
                        var root = group.Anchor as GameObject ?? (group.Anchor as Tuple<GameObject, string>)?.Item1;
                        var read = skips.Read(() => RootPath(root), () => {
                            var placed = group.Members.Select(i => new { Object = SpatialSelectionScene.Observe(all[i]), Listed = group.Listed.Contains(i), Stands = i == group.Root }).Where(m => m.Object.Points.Length != 0).ToArray();
                            if (placed.Length == 0) { skips.Skip(RootPath(root), "none of its objects has a usable position"); return null; }
                            return new InventoryGroup { Root = root, Objects = placed.Select(m => m.Object).ToArray(), Listed = placed.Select(m => m.Listed).ToArray(), Stands = Array.FindIndex(placed, m => m.Stands), Kind = group.Kind };
                        });
                        if (read != null) found.Add(read);
                    }
                    inventoryGroups = found.ToArray();
                }
            } catch (Exception e) { PatcherLog.Warning("The patcher couldn't read groups for scene object selection. Report this to the patcher maintainer.", e); }
            return inventoryGroups;
        }
        // A group that world marking skips shows its objects on its own; one developer diagnostic line says so, once
        // per top scene object and reason.
        static readonly GroupSkips skips = new GroupSkips(PatcherLog.Debug);
        static string RootPath(GameObject root) => root == null ? null : root.scene.name + "/" + BoardScene.PathOf(root.transform);
        internal static void Skipped(GameObject root, string reason) => skips.Skip(RootPath(root), reason);
        // Reads the parts of one family group; a group whose parts cannot be read is skipped with one line.
        internal static void ReadParts(GameObject root, Action read) => skips.ReadParts(() => RootPath(root), read);
        // Whether an inventory object is chosen only through its outliner row, and takes no part in world choices and
        // complexes: the crossing object of a crossing without signals. Inspected, it is framed whole (FramesWhole).
        internal static bool ChosenInOutliner(SceneObservation o) => o?.Family == CrossingObjectScene.Family;
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
                // The group's one box follows its objects (ObjectMarkingGeometry.BoxAlong), around the boxes of their pieces,
                // with the orientation of the scene object that anchors it, the crossing controller's root, as its
                // fallback, and is cut at the surface like the box of an object.
                var root = groups[g].Root;
                var orientation = root != null ? Basis(root.transform) : (WorldBasis?)null;
                if (groupPieces.Length != groups.Length) groupPieces = new ObjectMarkingGroupPiece[groups.Length];
                var piece = groupPieces[g];
                if (piece == null || !piece.Holds(anchors, frames, orientation, listed)) groupPieces[g] = piece = ObjectMarkingGeometry.GroupPieceOf(anchors, frames, orientation, listed);
                var marking = ObjectMarkingGeometry.GroupFrame(piece, eye, GroundAt, passProjection, passView);
                // Objects that stand apart, such as the signs of a container along the track, form no group: each shows
                // on its own.
                if (marking.Spread) continue;
                bool Inspected(SpatialCandidate member) => inspected != null && Same(member, inspected.Choice);
                // The root of a group with parts stands for the group: at the group distance or less only the
                // parts show, and the root shows only as the candidate of a choice or as the inspected object.
                var stands = groups[g].Stands < 0 ? -1 : indices[groups[g].Stands];
                // A choice that offers the root and none of its parts shows the root as any object.
                if (!Complexes.RootStands(stands, members)) stands = -1;
                if (marking.Shows == GroupShows.Objects) {
                    if (stands >= 0 && !Complexes.ShowsOwn(marking.Shows, Same(candidates[stands], chosen), Inspected(candidates[stands]), true)) layout.Hidden[stands] = layout.Absent[stands] = true;
                    continue;
                }
                var own = members.Where(m => Same(candidates[m], chosen) || Inspected(candidates[m])).ToArray();
                foreach (var m in members) layout.Hidden[m] = !Complexes.ShowsOwn(marking.Shows, Same(candidates[m], chosen), Inspected(candidates[m]), m == stands);
                var ownDistance = Complexes.OwnFrameDistance(marking.Shows);
                if (ownDistance > ObjectMarkingGeometry.FrameDistance) layout.OwnAtGroupDistance.AddRange(own);
                var unlisted = indices.Where((i, k) => i >= 0 && !listed[k]).ToArray();
                var top = groups[g].Stands < 0 ? null : objects[groups[g].Stands].Observation;
                var text = CommandBand.GroupMarkerText(top?.DisplayName ?? (root != null ? root.name : null), CommandBand.TrackSide(groups[g].Kind, top?.Category, top?.Bridge ?? BridgeKind.None));
                shown.Add(new ShownGroup { Members = members, Own = own, Unlisted = unlisted, Shows = marking.Shows, Shape = GroupShape(marking, camera), Kind = groups[g].Kind, Text = text });
            }
            layout.Shown = shown.ToArray();
            return layout;
        }
        // Every candidate projected once, with what was measured of each.
        // moving, when given, receives the candidates with a part the game moves while it runs; waiting those still
        // waiting for reads.
        Shape[] ProjectAll(SpatialCandidate[] candidates, Camera camera, Vector3 origin, out Measured[] measured, List<int> moving = null, List<int> waiting = null) {
            var shapes = new Shape[candidates.Length]; measured = new Measured[candidates.Length];
            for (var i = 0; i < candidates.Length; i++) {
                shapes[i] = Project(candidates[i], camera, origin, ObjectMarkingGeometry.FrameDistance, out measured[i]);
                if (lastMoves) moving?.Add(i);
                if (lastWaits) waiting?.Add(i);
            }
            return shapes;
        }
        // A candidate that shows on its own in a group within the group's frame distance has its own frame, although
        // its own bounds, or without boxes its marker, may lie beyond the frame distance: it is projected again at
        // the group's frame distance. That is the candidate of the choice and the inspected object, at most.
        void OwnFrames(SpatialCandidate[] candidates, Shape[] shapes, Measured[] measured, GroupLayout layout, Camera camera, Vector3 origin) {
            foreach (var m in layout.OwnAtGroupDistance) {
                var frame = measured[m]?.Frame;
                if (frame == null || frame.Frame.HasValue) continue;
                var distance = Double.IsNaN(frame.Distance) ? (camera.transform.position - Live(candidates[m], origin)).magnitude : frame.Distance;
                if (distance > ObjectMarkingGeometry.FrameDistance) shapes[m] = Project(candidates[m], camera, origin, Double.PositiveInfinity, out _);
            }
        }
        // The camera of the pass, a repaint or a click: its projection, its view and its position, made once per pass.
        Func<WorldPoint, ClipPoint> passProjection;
        ScreenRect passView;
        WorldPoint passEye;
        void BeginPass(Camera camera) { passProjection = Projection(camera); passView = ViewOf(camera); passEye = Point(camera.transform.position); }
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
        internal static WorldBasis Basis(Transform transform) => new WorldBasis(Point(transform.right), Point(transform.up), Point(transform.forward));
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
            if (relation == null || relation.Points.Length == 0 || shapes.Any(s => s.Text != null && s.Candidate != null && s.Candidate.Choice.Matches(relation.Choice))) return;
            var shape = Project(relation, camera, origin);
            if (shape == null) return;
            shape.Color = Tone(CommandBand.RelationMark(shape.Contains(pointer)));
            shape.Detailed = true;
            shape.Text = ObjectLabel(relation, label);
            shapes.Add(shape);
        }
        // A pass is used again while the camera stands still and nothing it was made from changed (MarkingReuse), for
        // at most ReuseFrames frames: the pointer then changes only what it marks and the labels. A part the game moves
        // while it runs is followed in every pass, and with it the groups it belongs to.
        const int ReuseFrames = 30;
        // A pass that left reads for a later frame is used again for IncompleteFrames frames, while the objects still
        // waiting are measured further in each of them.
        const int IncompleteFrames = 4;
        readonly MarkingReuse reuse = new MarkingReuse(ReuseFrames, IncompleteFrames);
        readonly ObservationPace pace = new ObservationPace();
        // The camera's position, rotation, projection and view, the screen, the world origin and the inventory count.
        readonly double[] viewKey = new double[33];
        // The candidates, the choice's candidate, the inspected object and the inventory snapshot.
        readonly object[] passInputs = new object[4];
        Shape[] keptShapes; Measured[] keptMeasured; GroupLayout keptGroups;
        SpatialCandidate keptSource;
        readonly List<int> moving = new List<int>(), waiting = new List<int>();
        void FillViewKey(Camera camera, Vector3 origin, int inventoryCount) {
            var t = camera.transform; var p = t.position; var r = t.rotation; var m = camera.projectionMatrix; var view = camera.pixelRect;
            var k = 0;
            viewKey[k++] = p.x; viewKey[k++] = p.y; viewKey[k++] = p.z;
            viewKey[k++] = r.x; viewKey[k++] = r.y; viewKey[k++] = r.z; viewKey[k++] = r.w;
            for (var i = 0; i < 16; i++) viewKey[k++] = m[i];
            viewKey[k++] = view.x; viewKey[k++] = view.y; viewKey[k++] = view.width; viewKey[k++] = view.height;
            viewKey[k++] = Screen.width; viewKey[k++] = Screen.height;
            viewKey[k++] = origin.x; viewKey[k++] = origin.y; viewKey[k++] = origin.z;
            viewKey[k] = inventoryCount;
        }
        // What the pointer marked on a kept shape in the last repaint is marked anew.
        static void Unmark(Shape shape) { shape.Text = null; shape.Label = default; shape.Listed = false; shape.Strong = false; shape.Detailed = false; }
        internal void Draw(SpatialCandidate[] candidates, PendingSelection pending, SpatialCandidate source, bool hasSource, SpatialCandidate confirmed, SpatialCandidate followed,
            IReadOnlyList<SpatialCandidate> bound, Camera camera, Vector3 origin, IReadOnlyList<Rect> uiBounds, Texture2D backing, SpatialCandidate trackPoint = null,
            SpatialCandidate entered = null, IReadOnlyList<SpatialCandidate> ends = null) {
            covered = uiBounds;
            inspectedMark = source;
            budget.Begin();
            Prune();
            BeginPass(camera);
            var snapshot = Inventory?.Invoke();
            FillViewKey(camera, origin, snapshot?.Count ?? -1);
            BeginObservation(pending, source, camera, candidates);
            try {
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
            var overUi = covered.Any(r => r.Contains(pointer));
            // A group marker's label is no candidate's: the pointer over it keeps the hover of its group. What it says
            // is read before the kept shapes are marked anew.
            var labelHit = overUi ? null : labels.FirstOrDefault(label => label.Candidate != null && label.Label.Contains(pointer));
            var labelHitChoice = labelHit?.Candidate.Choice;
            var labelHitListed = labelHit != null && labelHit.Listed;
            labels.Clear();
            // The inspected object is observed anew in every repaint: the candidate kept from an earlier repaint stands
            // for it while it is the same object, so a still camera reuses its pass.
            if (source == null || keptSource == null || !source.Choice.Matches(keptSource.Choice)) keptSource = source;
            passInputs[0] = candidates; passInputs[1] = pending?.Candidate; passInputs[2] = keptSource; passInputs[3] = snapshot;
            Shape[] projected; Measured[] measured; GroupLayout groups;
            if (!observing && keptShapes != null && reuse.Reuses(viewKey, passInputs, Time.frameCount)) {
                projected = keptShapes; measured = keptMeasured; groups = keptGroups;
                var own = false;
                // A part the game moves while it runs is followed in every pass where it shows; the frame of a group
                // it belongs to follows with the next pass.
                foreach (var i in moving) {
                    if (projected[i] == null) continue;
                    projected[i] = Project(candidates[i], camera, origin, ObjectMarkingGeometry.FrameDistance, out measured[i]);
                    own |= groups.OwnAtGroupDistance.Contains(i);
                }
                // An object still waiting for reads is measured further in every pass; once the last one is read,
                // the next pass is made anew, with its group frames.
                if (waiting.Count != 0) {
                    for (var w = waiting.Count - 1; w >= 0; w--) {
                        var i = waiting[w];
                        projected[i] = Project(candidates[i], camera, origin, ObjectMarkingGeometry.FrameDistance, out measured[i]);
                        own |= groups.OwnAtGroupDistance.Contains(i);
                        if (!lastWaits) waiting.RemoveAt(w);
                    }
                    if (waiting.Count == 0) reuse.Forget();
                }
                if (own) OwnFrames(candidates, projected, measured, groups, camera, origin);
            } else {
                passIncomplete = false;
                moving.Clear(); waiting.Clear();
                projected = ProjectAll(candidates, camera, origin, out measured, moving, waiting);
                groups = Groups(candidates, measured, camera, origin, pending?.Candidate, source);
                OwnFrames(candidates, projected, measured, groups, camera, origin);
                reuse.Keep(viewKey, passInputs, Time.frameCount, !passIncomplete && !budget.Exhausted);
                keptShapes = projected; keptMeasured = measured; keptGroups = groups;
            }
            foreach (var kept in projected) if (kept != null) Unmark(kept);
            foreach (var group in groups.Shown) if (group.Shape != null) Unmark(group.Shape);
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
                    ReferenceEquals(candidate.Choice, labelHitChoice) && (chosen || (several && labelHitListed)));
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
                // Under the pointer the square marker says what it stands for, as a tooltip does.
                group.Shape.Text = hovered && !open ? group.Text : null;
                shapes.Add(group.Shape);
            }
            if (source != null && source.Points.Length > 0) {
                var marked = shapes.FirstOrDefault(s => s.Text != null && s.Candidate != null && s.Candidate.Choice.Matches(source.Choice));
                // The inspected object keeps its frame and marker at any camera distance and while culling hides it.
                var shape = Project(source, camera, origin, Double.PositiveInfinity, out _, fresh: true);
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
            // While a binding field is edited, the ends of the entered segment, gold as the inspected object, each named
            // by its end, so the End field's a and b can be told apart on the track.
            if (ends != null) foreach (var end in ends) {
                var mark = Project(end, camera, origin, Double.PositiveInfinity, out _);
                if (mark == null) continue;
                mark.Color = Gold;
                mark.Detailed = true;
                mark.Text = end.Choice.Label;
                shapes.Add(mark);
            }
            // The current relations of the inspected object stay marked in green while it is inspected: a target
            // confirmed in the world, the track it follows, and, while the Connections tab is open, what it is bound to.
            // The offered marking of the same element leads.
            Relation(shapes, confirmed, "Confirmed choice: ", camera, origin, pointer);
            if (CommandBand.ShowsCurrentRelations(confirmed != null)) {
                Relation(shapes, followed, "Follows: ", camera, origin, pointer);
                if (bound != null) foreach (var relation in bound) Relation(shapes, relation, "Bound: ", camera, origin, pointer);
            }
            // The segment the binding fields name while one is edited, unless it is marked already, as the bound one.
            Relation(shapes, entered, "Entered segment: ", camera, origin, pointer);
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
                // A candidate that a row points at is drawn over the others on its spot.
                // Neutral markers first, then hovered, clicked and related ones, then the one a row points at:
                // on a spot that several share, the candidate shows in its colour, not under a neutral marker.
                var ordered = new List<Shape>(shapes.Count);
                for (var rank = 0; rank <= 2; rank++) foreach (var shape in shapes) if (CommandBand.DrawRank(shape.Detailed, shape.Strong) == rank) ordered.Add(shape);
                foreach (var zone in zones) {
                    GUI.BeginGroup(zone);
                    try {
                        // Each marker is drawn in the zones it reaches into, not in every zone.
                        foreach (var shape in ordered) if (shape.Marker.width > 0 && zone.Overlaps(MarkerReach(shape))) DrawShape(shape, zone.position);
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
            } finally { observing = false; }
        }

        // What one pass measured of an object that no camera changes: the piece of its boxes the frame outlines
        // (ObjectMarkingGeometry.PieceOf), whether a box was still to be read (Waits) and how many mesh renderers
        // are hidden without a known box (Unseen). It is kept on its renderers' beat while the object stands where it
        // stood, and measured again after the scene may have changed (ClearSurface). An object with a part the game
        // moves while it runs, such as a clock hand, is measured in every pass, and so is one still waiting for reads.
        sealed class Geometry {
            internal ObjectMarkingPiece Piece; internal bool Waits, Moves; internal int Unseen, Until;
            internal Vector3 Position, Scale, Origin, Anchor; internal Quaternion Rotation;
            internal bool Holds(GameObject go, Vector3 origin, Vector3 anchor) {
                if (Time.frameCount >= Until || origin != Origin || anchor != Anchor) return false;
                var t = go.transform;
                return t.position == Position && t.rotation == Rotation && t.lossyScale == Scale;
            }
        }
        // Kept by the object's identity (MarkingKey), not by candidate: every repaint observes the inspected object
        // anew and every click makes new candidates, and each finds the entry of its object. Bounded by GeometryLimit.
        const int GeometryLimit = 20000;
        readonly KeptGeometry<Geometry> geometry = new KeptGeometry<Geometry>(GeometryLimit);
        static MarkingKey KeyOf(SpatialCandidate candidate, GameObject go) => new MarkingKey(go, candidate.Parts, candidate.UncutParts, candidate.Observation.Family);
        // Whether the object the last Project measured has a part the game moves while it runs.
        bool lastMoves;
        // Whether the object the last Project measured still waits for reads.
        bool lastWaits;
        // Whether a pass left reads for a later frame: it is not used again (MarkingReuse).
        bool passIncomplete;
        Geometry Measure(SpatialCandidate candidate, GameObject go, Vector3 origin, List<string> decisions, List<string> skipped, out Renderer[] renderers,
            ref Dictionary<Renderer, KeyValuePair<LODGroup, int>> lods) {
            renderers = Renderers(go, out var outlines, out var moves);
            if (candidate.Parts != null) renderers = PartsOnly(renderers, ref outlines, candidate.Parts);
            // A crossing pad lies on its track: the track's ballast and the road it crosses are not ground it
            // stands in, so the pad is framed whole and not cut off where its ends meet the road.
            // The crossing object of a crossing without signals holds its pads: its frame is not cut at the ground either.
            var ground = PadFamilies.Contains(candidate.Observation.Family) || ChosenInOutliner(candidate.Observation) ? null : (Func<double, double, double?>)GroundAt;
            var boxes = new List<ObjectMarkingBox>();
            // What is read once per renderer and per place is read within a share of the frame: a renderer whose
            // kind is not known yet, once that share is used up, leaves its object without boxes until a
            // later frame, so the first view after a load builds up over some frames instead of in one.
            var waits = false;
            // The mesh renderers that are on but hidden without a known box yet.
            var unseen = 0;
            for (int index = 0; index < renderers.Length; index++) {
                var renderer = renderers[index];
                var name = decisions != null || skipped != null ? "#" + index : "";
                if (renderer == null) continue;
                // A renderer switched off by the scene or a modder is passed over; one the game's culling hides
                // now still outlines the object, from its mesh.
                if (index >= outlines.Length || !outlines[index]) {
                    skipped?.Add(name + ": Not used for the frame because it is switched off.");
                    continue;
                }
                var shows = renderer.enabled && renderer.gameObject.activeInHierarchy;
                // A static batch moves its meshes into one combined mesh, whose bounds are not the renderer's; only
                // the world-aligned box stays per renderer, and only while it shows. That box is kept, in the
                // object's own space, for while the game's culling hides the renderer
                // (ObjectMarkingGeometry.RendererBox).
                if (!Boxes.Own(renderer, out var ownCorners, out var ownSource)) { waits = true; break; }
                decisions?.Add(name + ": The box for this drawing component was " + RendererBoxes.Describe(ownSource) + ".");
                var worldBounds = shows ? renderer.bounds : default;
                WorldPoint[] visible = shows ? BoxCorners(worldBounds) : null;
                if (shows && ownCorners == null) {
                    var toLocal = renderer.transform.worldToLocalMatrix; var local = new WorldPoint[visible.Length];
                    for (var i = 0; i < visible.Length; i++) local[i] = Point(toLocal.MultiplyPoint3x4(Vector(visible[i])));
                    keptBoxes[renderer] = local;
                }
                keptBoxes.TryGetValue(renderer, out var keptBox);
                var toWorld = renderer.transform.localToWorldMatrix;
                var box = ObjectMarkingGeometry.RendererBox(shows, visible, ownCorners, keptBox, p => Point(toWorld.MultiplyPoint3x4(Vector(p))));
                // A hidden renderer without a box of its own and never seen since the world loaded adds nothing.
                if (box == null) {
                    skipped?.Add(name + ": Not used for the frame because it is hidden and its box is unknown.");
                    // It still makes its object visible: a mesh renderer outlines the object once it shows, so the
                    // object shows no helper marker (HelperMarkers.Shows). Its kind is read within the pass's share.
                    if (!kinds.TryGetValue(renderer, out var hiddenKind)) {
                        if (budget.Exhausted) { waits = true; break; }
                        var known = lods;
                        hiddenKind = budget.Read(() => Kind(renderer, go, ref known));
                        lods = known;
                    }
                    if (hiddenKind == ObjectMarkingBoxKind.Mesh) unseen++;
                    continue;
                }
                // A mesh renderer that shows outlines with its own oriented box; a hidden one with the box
                // RendererBox gave.
                var oriented = shows ? (renderer is MeshRenderer ? ownCorners : null) : box.ToArray();
                Bounds bounds;
                if (shows) bounds = worldBounds;
                else {
                    // A hidden renderer reports no world box: the one around its box stands in.
                    bounds = new Bounds(new Vector3((float)oriented[0].X, (float)oriented[0].Y, (float)oriented[0].Z), Vector3.zero);
                    foreach (var corner in oriented) bounds.Encapsulate(new Vector3((float)corner.X, (float)corner.Y, (float)corner.Z));
                }
                if (bounds.size.sqrMagnitude < .000001f) {
                    skipped?.Add(name + ": Not used for the frame because its box is empty.");
                    continue;
                }
                // A renderer that is switched off was passed over above and is never read. The kind of one that
                // shows is read once, within the pass's share; an object with a kind still to read waits whole.
                if (!kinds.TryGetValue(renderer, out var kind)) {
                    if (budget.Exhausted) { waits = true; break; }
                    var known = lods;
                    kind = budget.Read(() => Kind(renderer, go, ref known));
                    lods = known;
                }
                // A part that lies on its track, such as the pad of a crossing node, is framed whole, as a pad is.
                var cut = ground != null && candidate.UncutParts != null && Under(renderer, candidate.UncutParts) ? null : ground;
                boxes.Add(new ObjectMarkingBox("#" + index, Point(bounds.center), Point(bounds.extents), oriented, kind, cut));
            }
            // A storage pile or log pond has no renderer of its own: the box it lays its logs out in stands in, from
            // its layout settings, so it is framed like any other box whether or not the camera has culled it.
            if (!waits)
                foreach (var pile in Piles(go)) {
                    var oriented = PileBox(pile);
                    if (oriented == null) continue;
                    var bounds = new Bounds(Vector(oriented[0]), Vector3.zero);
                    foreach (var corner in oriented) bounds.Encapsulate(Vector(corner));
                    boxes.Add(new ObjectMarkingBox("pile " + pile.name, Point(bounds.center), Point(bounds.extents), oriented, ObjectMarkingBoxKind.Mesh, ground));
                }
            if (waits) boxes.Clear();
            var t = go.transform;
            return new Geometry {
                Piece = ObjectMarkingGeometry.PieceOf(Point(candidate.Position + origin), boxes, Basis(t), ObjectMarkingGeometry.FramesWhole(candidate.Observation.Family)),
                Waits = waits, Moves = moves, Unseen = unseen, Until = Time.frameCount + 90 + (go.GetInstanceID() & 63),
                Position = t.position, Rotation = t.rotation, Scale = t.lossyScale, Origin = origin, Anchor = candidate.Position
            };
        }
        Shape Project(SpatialCandidate candidate, Camera camera, Vector3 origin) => Project(candidate, camera, origin, ObjectMarkingGeometry.FrameDistance, out _);
        // frameDistance is the distance beyond which the object has no frame (ObjectMarkingGeometry.Frame); measured
        // is what was measured of a scene object, also when the object is not in view and has no shape.
        // fresh measures the object's boxes again, as for the inspected object, which the gizmo moves.
        Shape Project(SpatialCandidate candidate, Camera camera, Vector3 origin, double frameDistance, out Measured measured, bool fresh = false) {
            measured = null;
            lastMoves = false; lastWaits = false;
            if (candidate.Points.Length == 0) return null;
            // The surface heights are kept by world place: an origin move makes every place another one.
            if (origin != groundOrigin) { grounds.Clear(); groundOrigin = origin; }
            var shape = new Shape { Candidate = candidate };
            var anchor = ScreenPoint(camera, candidate.Position, origin);
            if (candidate.Observation != null && candidate.Choice.Handle is GameObject go && go != null) {
                var decisions = observing ? new List<string>() : null;
                var skipped = observing ? new List<string>() : null;
                Renderer[] renderers = null;
                Dictionary<Renderer, KeyValuePair<LODGroup, int>> lods = null;
                // What no camera changes is measured once and kept (Geometry); the frame is taken from it for this camera.
                var key = KeyOf(candidate, go);
                if (fresh || observing || !geometry.TryGet(key, out var kept) || !kept.Holds(go, origin, candidate.Position)) {
                    kept = Measure(candidate, go, origin, decisions, skipped, out renderers, ref lods);
                    // What is measured fresh, for the object the gizmo moves, is not kept.
                    if (!fresh) { if (!kept.Waits && !kept.Moves) geometry.Keep(key, kept); else geometry.Remove(key); }
                }
                if (kept.Waits) passIncomplete = true;
                lastWaits = kept.Waits;
                lastMoves = kept.Moves;
                var view = camera.pixelRect;
                // One frame per object, around the piece its marker belongs to. Picking uses the same rectangle.
                // The frame around multiple boxes follows them, with the object's orientation as its fallback.
                var result = ObjectMarkingGeometry.Frame(kept.Piece, passEye, passProjection, passView, decisions, frameDistance);
                measured = new Measured { Frame = result };
                if (HelperMarkers.Shows(result, kept.Waits, kept.Unseen)) {
                    // An invisible object, without a renderer that outlines it, shows its helper marker: the square
                    // marker on its pivot, read from its transform now, so it follows every placement change, gizmo
                    // preview and Undo, and the frame of its colliders' bounds or the least frame about the marker.
                    var pivot = go.transform.position;
                    var helper = HelperMarkers.Mark(Point(pivot), passEye, Colliders(go), passProjection, passView, frameDistance, Basis(go.transform));
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
                    // Its boxes are still being read (waits), or a hidden renderer's box is not known yet (unseen):
                    // until then the object has a frame within the frame distance, about its marker. An object that is asked for at its group's frame distance has it
                    // wherever its marker stands.
                    shape.Outline = ObjectMarkingGeometry.MarkerOutline(anchor.x, anchor.y);
                    var half = (float)(ObjectMarkingGeometry.MarkerSize / 2 + ObjectMarkingGeometry.MarkerMargin);
                    shape.Objects.Add(new Rect(anchor.x - half, anchor.y - half, 2 * half, 2 * half));
                }
                if (observing && !(result.Distance > ObservationRange)) Observe(candidate, renderers, lods ?? LodsOf(go), decisions, skipped, result, view, Basis(go.transform));
            }
            if (candidate.ControlPoints != null) {
                if (trackRadius == 0) {
                    trackRadius = (float)Native.Read(Native.Type("Game.MapEditor.TrackClickPicker"), "SnapRadius");
                    if (Single.IsNaN(trackRadius) || Single.IsInfinity(trackRadius) || trackRadius <= 0)
                        throw new InvalidOperationException("The patcher couldn't prepare track selection. Report this to the patcher maintainer and include the Player log. Details: invalid TrackClickPicker.SnapRadius.");
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
        // The levels of detail of each object, read once and kept with what is measured of it (ForgetGeometry).
        readonly Dictionary<GameObject, Dictionary<Renderer, KeyValuePair<LODGroup, int>>> lodsOf = new Dictionary<GameObject, Dictionary<Renderer, KeyValuePair<LODGroup, int>>>();
        Dictionary<Renderer, KeyValuePair<LODGroup, int>> LodsOf(GameObject go) {
            if (!lodsOf.TryGetValue(go, out var lods)) lodsOf[go] = lods = Lods(go);
            return lods;
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
        ObjectMarkingBoxKind Kind(Renderer renderer, GameObject go, ref Dictionary<Renderer, KeyValuePair<LODGroup, int>> lods) {
            if (kinds.TryGetValue(renderer, out var known)) return known;
            if (lods == null) lods = LodsOf(go);
            var components = new List<string>();
            foreach (var behaviour in renderer.GetComponents<MonoBehaviour>())
                for (var type = behaviour == null ? null : behaviour.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                    components.Add(type.FullName);
            var kind = ObjectMarkingGeometry.KindOf(renderer.GetType().FullName, components, renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly,
                lods.TryGetValue(renderer, out var lod) ? lod.Value : 0);
            kinds[renderer] = kind;
            return kind;
        }
        // Frame diagnostics, debug output: one block per candidate within range, written once per object and again only
        // when the one box its frame outlines changes (LogOnChange, keyed by the object's path, label and instance ID).
        // The camera line goes before the first block written from a camera position, once per position. With debug off
        // nothing is collected or built.
        const double ObservationRange = 1500;
        bool observing;
        // The camera line of the pass, and the last one written.
        string cameraLine, writtenCameraLine;
        // Per object, the box signature of its last written block.
        readonly LogOnChange frameChecks = new LogOnChange();
        // They run once per camera rest and set of candidates (ObservationPace), off the per-frame path.
        void BeginObservation(PendingSelection pending, SpatialCandidate source, Camera camera, SpatialCandidate[] candidates) {
            observing = PatcherLog.DebugEnabled && ((object)pending ?? source) != null && pace.Due(viewKey, candidates);
            if (!observing) return;
            cameraLine = "Frame check. Camera position: " + Vector(camera.transform.position) + ". View size: " + Number(camera.pixelRect.width) + "x" +
                Number(camera.pixelRect.height) + " pixels. Near view limit: " + Number(camera.nearClipPlane) + " m. Far view limit: " + Number(camera.farClipPlane) +
                " m. Maximum frame distance: " + Number(ObjectMarkingGeometry.FrameDistance) + " m.";
        }
        static void Corners(IEnumerable<WorldPoint> corners, System.Text.StringBuilder text) {
            foreach (var corner in corners) text.Append(Math.Round(corner.X, 2).ToString(CultureInfo.InvariantCulture)).Append(',').Append(Math.Round(corner.Y, 2).ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(Math.Round(corner.Z, 2).ToString(CultureInfo.InvariantCulture)).Append(';');
        }
        // The box a block names, to a centimetre, and how many renderers it lists: a block is written again only when
        // that changes. A part the game moves while running, such as a clock hand, changes the frame box every second: an
        // object with such a part is compared by the boxes of its other renderers instead, and the moving ones are left out.
        static string BoxSignature(ObjectMarkingFrame result, Renderer[] renderers) {
            var text = new System.Text.StringBuilder();
            var moving = renderers.Where(r => r != null && GroupWorld.MovesWhileRunning(r.transform)).ToArray();
            if (moving.Length == 0) {
                if (result.Box == null) return "no box/" + renderers.Length;
                Corners(result.Box, text);
                return text.Append(renderers.Length).ToString();
            }
            text.Append("moving parts ").Append(moving.Length).Append('/');
            foreach (var renderer in renderers.Where(r => r != null && !moving.Contains(r))) Corners(BoxCorners(renderer.bounds), text);
            return text.Append(renderers.Length).ToString();
        }
        void Observe(SpatialCandidate candidate, Renderer[] renderers, Dictionary<Renderer, KeyValuePair<LODGroup, int>> lods, List<string> decisions,
            List<string> skipped, ObjectMarkingFrame result, Rect view, WorldBasis objectAxes) {
            // An object that the game moves while running as a whole has no stable box: it is left out of the frame log.
            var go = candidate.Choice.Handle as GameObject;
            if (go != null && GroupWorld.MovesWhileRunning(go.transform)) return;
            // Objects can share a path and a label, such as the pads of crossings that share one: the instance ID keeps each
            // on its own entry.
            var key = LogOnChange.ObjectKey(go != null ? BoardScene.PathOf(go.transform) : "", candidate.Choice.Label,
                go != null ? go.GetInstanceID() : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(candidate.Choice.Handle));
            if (!frameChecks.Changed(key, BoxSignature(result, renderers))) return;
            if (cameraLine != null && cameraLine != writtenCameraLine) { PatcherLog.Debug(cameraLine); writtenCameraLine = cameraLine; }
            var text = new System.Text.StringBuilder();
            text.Append("Frame check for ").Append(candidate.Choice.Label).Append(". Distance: ").Append(Number(result.Distance)).Append(" m")
                .Append(result.Frame.HasValue ? ". Frame width and height as fractions of the view: " + Number(result.Frame.Value.Width / view.width) + "x" + Number(result.Frame.Value.Height / view.height) + "." : ". Only the marker is shown.");
            var lines = decisions.Concat(skipped).ToList();
            // How the one box stands: the object's yaw and the box's, which follows its boxes (BoxAlongBuilder).
            if (result.Box != null)
                text.Append(" The frame combines ").Append(result.Boxes).Append(result.Boxes == 1 ? " box" : " boxes").Append(". Object rotation about the vertical axis: ").Append(ObjectMarkingGeometry.Yaw(objectAxes.X, objectAxes.Z))
                    .Append(". Frame box rotation about the vertical axis: ").Append(ObjectMarkingGeometry.Yaw(result.Box)).Append('.');
            // One entry per line: the frame, then each renderer.
            var entries = new List<string> { text.ToString() };
            // The renderer list can outlive a renderer rebuilt by the game. Keep the original indices for the frame decisions.
            entries.AddRange(WorldMarkingDiagnostics.RendererLines(renderers, renderer => renderer != null, (renderer, index) => {
                var name = "#" + index;
                var decision = String.Join(" / ", lines.Where(l => l.StartsWith(name + ":", StringComparison.Ordinal)).Select(l => l.Substring(name.Length + 2)));
                var lod = lods.TryGetValue(renderer, out var level) && level.Key != null ? BoardScene.PathOf(level.Key.transform) + ", level " + level.Value : "none";
                text.Clear();
                text.Append("Frame check for ").Append(candidate.Choice.Label).Append(", renderer ").Append(name).Append(' ').Append(BoardScene.PathOf(renderer.transform)).Append(". Drawing component: ").Append(renderer.GetType().Name)
                    .Append(". Box width/height/depth in metres: ").Append(Vector(renderer.bounds.size)).Append(". Drawn together with other scenery: ").Append(renderer.isPartOfStaticBatch)
                    .Append(". Distance-based detail group and level: ").Append(lod).Append(". Drawing component switched on: ").Append(renderer.enabled && renderer.gameObject.activeInHierarchy)
                    .Append(". Frame decision: ").Append(decision.Length == 0 ? "No decision recorded." : decision);
                return text.ToString();
            }));
            foreach (var entry in entries) PatcherLog.Debug(entry);
        }
        static string Number(double value) => ShownNumber.Text(value);
        static string Vector(Vector3 v) => Number(v.x) + "/" + Number(v.y) + "/" + Number(v.z);
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

        // The square a marker covers on screen: a pointed-at candidate's marker is larger.
        static Rect MarkerReach(Shape shape) => shape.Strong ? new Rect(shape.Marker.x - 6, shape.Marker.y - 6, shape.Marker.width + 12, shape.Marker.height + 12) : shape.Marker;
        const float MarkerEdge = 2f;
        static void DrawShape(Shape shape, Vector2 origin) {
            var marker = Offset(shape.Marker, origin);
            if (marker.width == 0) return;
            // A pointed-at candidate's marker is larger and filled with its colour, so it stands out on a spot it shares.
            if (shape.Strong) marker = new Rect(marker.x - 6, marker.y - 6, marker.width + 12, marker.height + 12);
            // The edge colour over the whole square, then the fill inside its edge: the pixels of a filled square with
            // four edges over it, in two draws. Both colours are opaque.
            GUI.color = shape.Strong ? Color.white : shape.Color;
            GUI.DrawTexture(marker, Texture2D.whiteTexture);
            GUI.color = shape.Strong ? shape.Color : new Color(.08f, .08f, .08f, 1f);
            GUI.DrawTexture(new Rect(marker.x + MarkerEdge, marker.y + MarkerEdge, marker.width - 2 * MarkerEdge, marker.height - 2 * MarkerEdge), Texture2D.whiteTexture);
            GUI.color = shape.Strong ? new Color(.08f, .08f, .08f, 1f) : shape.Color;
            foreach (var bar in ObjectMarkingGeometry.MarkerSymbol(shape.Group))
                GUI.DrawTexture(new Rect(marker.center.x + (float)bar.X, marker.center.y + (float)bar.Y, (float)bar.Width, (float)bar.Height), Texture2D.whiteTexture);
        }
    }
}
