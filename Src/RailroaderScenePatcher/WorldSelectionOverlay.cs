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
            internal readonly List<Rect> Objects = new List<Rect>();
            // The frame as it is drawn and picked: the convex outline of the object on screen. Objects holds the
            // rectangle around it, at whose centre the marker stands.
            internal IReadOnlyList<MarkingPoint> Outline;
            internal bool InFrame(Vector2 pointer) => Outline != null && ObjectMarkingGeometry.Contains(Outline, pointer.x, pointer.y);
            internal ProjectedWorldRibbon Track;
            internal Rect Label;
            internal string Text;
            internal Color Color;
            internal Color? Rule;
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
        // The labels of the frame before, by candidate: the pointer over a label marks that label's candidate.
        readonly List<KeyValuePair<SelectionCandidate, Rect>> previousLabels = new List<KeyValuePair<SelectionCandidate, Rect>>();
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
        // all of whose targets are inventory objects keeps the least frame about its own marker. The controller's
        // targets are read once per object.
        readonly Dictionary<GameObject, GameObject[]> controlled = new Dictionary<GameObject, GameObject[]>();
        // The renderers of an object are listed once and kept for a second or two, each object on its own beat, and
        // listed again after the scene may have changed (ClearSurface): a list per object per pass is garbage of
        // hundreds of arrays a frame, which the game pays for in collection pauses.
        sealed class Listed { internal Renderer[] Renderers; internal int Until; }
        readonly Dictionary<GameObject, Listed> listed = new Dictionary<GameObject, Listed>();
        Renderer[] Renderers(GameObject go) {
            if (listed.TryGetValue(go, out var kept) && Time.frameCount < kept.Until) return kept.Renderers;
            var list = ListRenderers(go);
            listed[go] = new Listed { Renderers = list, Until = Time.frameCount + 90 + (go.GetInstanceID() & 63) };
            return list;
        }
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
                        if (hit.collider.gameObject.layer == terrainLayer) terrain = terrain.HasValue ? Math.Max(terrain.Value, hit.point.y) : hit.point.y;
                        else (track ?? (track = new List<double>())).Add(hit.point.y);
                    }
                    return ObjectMarkingGeometry.Surface(terrain, track);
                } catch { return (double?)null; }
            });
            if (grounds.Count > 50000) grounds.Clear();
            grounds[key] = height;
            return height;
        }
        // The surface may have changed: an edit, a terrain output that completed, or a track change. The heights are
        // read again.
        internal void ClearSurface() { grounds.Clear(); listed.Clear(); controlled.Clear(); }
        // What is kept per object and per renderer loses the entries of objects the game destroyed, now and then.
        int prunedAt;
        void Prune() {
            if (Time.frameCount - prunedAt < 600) return;
            prunedAt = Time.frameCount;
            foreach (var gone in listed.Keys.Where(key => key == null).ToArray()) listed.Remove(gone);
            foreach (var gone in controlled.Keys.Where(key => key == null).ToArray()) controlled.Remove(gone);
            foreach (var gone in kinds.Keys.Where(key => key == null).ToArray()) kinds.Remove(gone);
        }

        internal void Release() {
            if (trackMaterial != null) UnityEngine.Object.Destroy(trackMaterial);
            trackMaterial = null;
            labels.Clear();
            previousLabels.Clear();
            kinds.Clear();
            grounds.Clear();
            controlled.Clear();
            listed.Clear();
        }

        internal SelectionCandidate[] Pick(SpatialCandidate[] candidates, Camera camera, Vector3 origin, Vector2 pointer,
            IReadOnlyList<Rect> uiBounds) {
            // Native windows can move between the last repaint and this click.
            covered = uiBounds;
            budget.Begin();
            if (covered.Any(r => r.Contains(pointer))) return Array.Empty<SelectionCandidate>();
            lastPick = pointer;
            var labelHits = labels.Where(s => s.Label.Contains(pointer) && !covered.Any(r => r.Overlaps(s.Label))).ToArray();
            var shapes = candidates.Select(c => Project(c, camera, origin)).ToArray();
            // A row of the list beside the click owns its click. Otherwise a click on a shown square marker reaches
            // that marker, also where the label of another candidate lies over it. Any other opaque identity label
            // owns its click; geometry hidden behind it is never selected.
            var rows = labelHits.Where(s => s.Listed).ToArray();
            if (rows.Length != 0) labelHits = rows;
            else if (CommandBand.MarkerBeforeLabel(Enumerable.Range(0, shapes.Length).Any(i => shapes[i] != null && Marked(shapes[i], candidates[i], camera, origin) && shapes[i].Marker.Contains(pointer)))) labelHits = new Shape[0];
            if (labelHits.Length != 0)
                return candidates.Where(c => labelHits.Any(s => s.Candidate.Choice.Matches(c.Choice))).Select(c => c.Choice).ToArray();
            return Resolve(candidates, shapes, camera, origin, pointer).Select(i => candidates[i].Choice).ToArray();
        }
        // Where the last click fell: the list of the candidates of one spot opens beside it.
        Vector2 lastPick;
        // The candidates a click at the pointer selects (WorldPick.Resolve): the rule of the click and of the hover.
        int[] Resolve(SpatialCandidate[] candidates, Shape[] shapes, Camera camera, Vector3 origin, Vector2 pointer) {
            var indices = new List<int>(); var targets = new List<PickTarget>();
            for (var i = 0; i < shapes.Length; i++) {
                var shape = shapes[i];
                if (shape == null) continue;
                var area = shape.InFrame(pointer) || (shape.Track != null && shape.Track.Contains(new MarkingPoint(pointer.x, pointer.y)));
                indices.Add(i);
                targets.Add(new PickTarget(shape.Marker.center.x, shape.Marker.center.y, Marked(shape, candidates[i], camera, origin), area));
            }
            return WorldPick.Resolve(pointer.x, pointer.y, targets).Select(i => indices[i]).ToArray();
        }
        // Whether a candidate carries its "+" without the pointer over it: a track only within a distance of the camera.
        static bool Marked(Shape shape, SpatialCandidate candidate, Camera camera, Vector3 origin) =>
            CommandBand.ShowsMarker(shape.Track != null, shape.Track == null ? 0 : (camera.transform.position - (candidate.Position + origin)).magnitude, false);

        // One colour table with the panel, CommandBand.WorldColor: gold frames the inspected object; a candidate is blue,
        // saturated under the pointer and lighter once clicked; a current relation is green, more saturated under the
        // pointer; red marks only a broken item. A candidate that is neither hovered nor clicked shows its neutral
        // marker. Amber is a panel colour and never appears in the world.
        static Color Tone(CommandBand.WorldMark mark) { var c = CommandBand.WorldColor(mark); return new Color(c[0], c[1], c[2]); }
        internal static readonly Color Gold = Tone(CommandBand.WorldMark.Inspected), Broken = Tone(CommandBand.WorldMark.Broken);
        // A current relation of the inspected object: green, more saturated under the pointer.
        void Relation(List<Shape> shapes, SpatialCandidate relation, string label, Camera camera, Vector3 origin, Vector2 pointer) {
            if (relation == null || relation.Points.Length == 0 || shapes.Any(s => s.Text != null && s.Candidate.Choice.Matches(relation.Choice))) return;
            var shape = Project(relation, camera, origin);
            if (shape == null) return;
            shape.Color = Tone(CommandBand.RelationMark(shape.Contains(pointer)));
            shape.Detailed = true;
            shape.Text = label + Short(relation.Choice.Label);
            shapes.Add(shape);
        }
        internal void Draw(SpatialCandidate[] candidates, PendingSelection pending, SpatialCandidate source, bool hasSource, SpatialCandidate confirmed, SpatialCandidate followed,
            IReadOnlyList<SpatialCandidate> bound, Camera camera, Vector3 origin, IReadOnlyList<Rect> uiBounds, Texture2D backing, SpatialCandidate trackPoint = null) {
            covered = uiBounds;
            labels.Clear();
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
            var pointer = Event.current.mousePosition;
            // The pointer marks what a click there would select, by the click's own rule: one candidate, or the
            // candidates of one spot. Over a window or a label it marks nothing by that rule; a label marks its own.
            var projected = candidates.Select(c => Project(c, camera, origin)).ToArray();
            var overUi = covered.Any(r => r.Contains(pointer));
            var labelled = overUi ? null : previousLabels.FirstOrDefault(l => l.Value.Contains(pointer)).Key;
            var under = pending == null || overUi || labelled != null ? new int[0] : Resolve(candidates, projected, camera, origin, pointer);
            var several = pending != null && pending.Offers.Count > 1;
            var offerLabels = several ? pending.Offers.Select(o => o.Label).ToArray() : null;
            for (var index = 0; index < candidates.Length; index++) {
                var candidate = candidates[index]; var shape = projected[index];
                if (shape == null) continue;
                bool offered = pending != null && pending.Offers.Contains(candidate.Choice);
                bool chosen = pending != null && ReferenceEquals(candidate.Choice, pending.Candidate);
                // A candidate the panel points at, or whose label the pointer is over, is marked as one under the pointer.
                bool hovered = Array.IndexOf(under, index) >= 0 || (pending != null && ReferenceEquals(candidate.Choice, pending.Highlight)) || (labelled != null && ReferenceEquals(labelled, candidate.Choice));
                // Blue for the candidate a click would select and, lighter, for one that was clicked, until Confirm.
                shape.Color = Tone(CommandBand.CandidateMark(offered && !(several && hovered), hovered));
                shape.Detailed = offered || hovered;
                // The candidates of one spot are the rows of a list beside the click, numbered, each named by what
                // tells it from the others.
                shape.Listed = offered && several;
                shape.Strong = pending != null && (ReferenceEquals(candidate.Choice, pending.Highlight) || (labelled != null && ReferenceEquals(labelled, candidate.Choice)));
                if (offered) shape.Text = several ? pending.Number(candidate.Choice) + "  " + CommandBand.ChoiceLabel(candidate.Choice.Label, offerLabels, 60)
                    : (chosen ? "New: " : "Choice: ") + Short(candidate.Choice.Label);
                // Every candidate of the mode carries its "+" marker without the pointer over it; a track within a
                // distance of the camera, so that a wide view is not filled with them.
                if (shape.Detailed || Marked(shape, candidate, camera, origin)) shapes.Add(shape);
            }
            if (source != null && source.Points.Length > 0) {
                var marked = shapes.FirstOrDefault(s => s.Text != null && s.Candidate.Choice.Matches(source.Choice));
                var shape = Project(source, camera, origin);
                var broken = source.Observation != null && source.Observation.Problems.Count != 0;
                if (marked != null) {
                    if (hasSource) marked.Text = "Object / " + marked.Text;
                } else if (shape != null) {
                    shape.Color = Gold;
                    shape.Detailed = true;
                    shape.Text = "Object: " + Short(source.Choice.Label) + (broken ? " (problem)" : "");
                    // Red marks only the broken item: the label's rule, not the frame of the inspected object.
                    if (broken) shape.Rule = Broken;
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
            Relation(shapes, confirmed, "Confirmed: ", camera, origin, pointer);
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
            previousLabels.Clear();
            foreach (var label in labels) previousLabels.Add(new KeyValuePair<SelectionCandidate, Rect>(label.Candidate.Choice, label.Label));
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
                            GUI.color = label.Rule ?? label.Color;
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

        Shape Project(SpatialCandidate candidate, Camera camera, Vector3 origin) {
            if (candidate.Points.Length == 0) return null;
            // The surface heights are kept by world place: an origin move makes every place another one.
            if (origin != groundOrigin) { grounds.Clear(); groundOrigin = origin; }
            var shape = new Shape { Candidate = candidate };
            var anchor = ScreenPoint(camera, candidate.Position, origin);
            if (candidate.Observation != null && candidate.Choice.Handle is GameObject go && go != null) {
                var renderers = Renderers(go);
                Dictionary<Renderer, KeyValuePair<LODGroup, int>> lods = null;
                var parts = new List<ObjectMarkingPart>();
                // What is read once per renderer and per place is read within a share of the frame: a renderer whose
                // kind is not known yet, once that share is used up, leaves its object without its parts until a
                // later frame, so the first view after a load builds up over some frames instead of in one.
                var waits = false;
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
                    var bounds = renderer.bounds;
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || bounds.size.sqrMagnitude < .000001f) {
#if ISSUE42_OBSERVATION
                        skipped?.Add(name + ": skipped, " + (!renderer.enabled || !renderer.gameObject.activeInHierarchy ? "inactive" : "empty bounds"));
#endif
                        continue;
                    }
                    // A static batch moves its meshes into one combined mesh, whose bounds are not the renderer's; only
                    // the world-aligned box stays per renderer.
                    WorldPoint[] oriented = null;
                    if (renderer is MeshRenderer && !renderer.isPartOfStaticBatch) {
                        var local = renderer.localBounds; var matrix = renderer.localToWorldMatrix;
                        oriented = new WorldPoint[8];
                        for (int i = 0; i < 8; i++)
                            oriented[i] = Point(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    }
                    // A renderer that is switched off was passed over above and is never read. The kind of one that
                    // shows is read once, within the pass's share; an object with a kind still to read waits whole.
                    if (!kinds.TryGetValue(renderer, out var kind)) {
                        if (budget.Exhausted) { waits = true; break; }
                        kind = budget.Read(() => Kind(renderer, go, ref lods));
                    }
                    parts.Add(new ObjectMarkingPart(name, Point(bounds.center), Point(bounds.extents), oriented, kind, GroundAt));
                }
                if (waits) parts.Clear();
                var view = camera.pixelRect;
                var toClip = camera.projectionMatrix * camera.worldToCameraMatrix;
                // One frame per object, around the piece its marker belongs to. Picking uses the same rectangle.
                var result = ObjectMarkingGeometry.Frame(Point(candidate.Position + origin), Point(camera.transform.position), parts, p => {
                    var clip = toClip * new Vector4((float)p.X, (float)p.Y, (float)p.Z, 1);
                    return new ClipPoint(clip.x, clip.y, clip.z, clip.w);
                }, ScreenRect.FromEdges(view.xMin, Screen.height - view.yMax, view.xMax, Screen.height - view.yMin), decisions);
                if (result.Frame.HasValue) {
                    var frame = result.Frame.Value;
                    // The marker stands on the frame's centre; the frame is never smaller than the marker with its margin.
                    frame = ObjectMarkingGeometry.VisibleFrame(frame);
                    var rect = Rect.MinMaxRect((float)frame.X, (float)frame.Y, (float)frame.Right, (float)frame.Bottom);
                    shape.Objects.Add(rect);
                    shape.Outline = ObjectMarkingGeometry.VisibleOutline(result.Outline);
                    anchor = new Vector3(rect.center.x, rect.center.y, camera.nearClipPlane);
                } else if (InFront(camera, anchor) && ObjectMarkingGeometry.FramesMarker((camera.transform.position - (candidate.Position + origin)).magnitude)) {
                    // No part to frame: the object still has a frame within the frame distance, about its marker.
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
        static Vector3 ScreenPoint(Camera camera, Vector3 point, Vector3 origin) {
            var projected = camera.WorldToScreenPoint(point + origin);
            projected.y = Screen.height - projected.y;
            return projected;
        }
        static WorldPoint Point(Vector3 v) => new WorldPoint(v.x, v.y, v.z);
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

        Vector2 LabelSize(string text) {
            var content = new GUIContent(text);
            // Use the final pixel width for both wrapping and drawing. Rounding a natural width
            // down can introduce a line that was absent during measurement.
            float width = Mathf.Min(480, Screen.width, Mathf.Ceil(textStyle.CalcSize(content).x));
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
            var framed = shapes.Where(shape => shape.Detailed && shape.Outline != null && shape.Outline.Count >= 3).OrderBy(shape => shape.Strong ? 1 : 0).ToArray();
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
            GUI.DrawTexture(new Rect(marker.center.x - 5, marker.center.y - 1, 10, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(marker.center.x - 1, marker.center.y - 5, 2, 10), Texture2D.whiteTexture);
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
