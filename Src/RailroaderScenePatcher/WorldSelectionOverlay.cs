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
        const float MarkerSize = 24f;
        const float TrackMinimumRadius = 10f;
        sealed class Shape {
            internal SpatialCandidate Candidate;
            internal Rect Marker;
            internal readonly List<Rect> Objects = new List<Rect>();
            internal ProjectedWorldRibbon Track;
            internal Rect Label;
            internal string Text;
            internal Color Color;
            internal Color? Rule;
            internal bool Detailed;
            internal bool Contains(Vector2 pointer) =>
                Marker.Contains(pointer) || Objects.Any(r => r.Contains(pointer)) ||
                (Track != null && Track.Contains(new MarkingPoint(pointer.x, pointer.y)));
        }
        readonly List<Shape> labels = new List<Shape>();
        IReadOnlyList<Rect> covered = Array.Empty<Rect>();
        GUIStyle textStyle;
        float trackRadius;
        Material trackMaterial;
        // The outline kind of each renderer seen, read once: its components, shadow mode and level of detail stay.
        readonly Dictionary<Renderer, ObjectMarkingPartKind> kinds = new Dictionary<Renderer, ObjectMarkingPartKind>();

        internal void Release() {
            if (trackMaterial != null) UnityEngine.Object.Destroy(trackMaterial);
            trackMaterial = null;
            labels.Clear();
            kinds.Clear();
        }

        internal SelectionCandidate[] Pick(SpatialCandidate[] candidates, Camera camera, Vector3 origin, Vector2 pointer,
            IReadOnlyList<Rect> uiBounds) {
            // Native windows can move between the last repaint and this click.
            covered = uiBounds;
            if (covered.Any(r => r.Contains(pointer))) return Array.Empty<SelectionCandidate>();
            var labelHits = labels.Where(s => s.Label.Contains(pointer) && !covered.Any(r => r.Overlaps(s.Label))).ToArray();
            // An opaque identity label owns its click; never select geometry hidden behind it.
            if (labelHits.Length != 0)
                return candidates.Where(c => labelHits.Any(s => s.Candidate.Choice.Matches(c.Choice))).Select(c => c.Choice).ToArray();
            return candidates.Where(c => Project(c, camera, origin)?.Contains(pointer) == true).Select(c => c.Choice).ToArray();
        }

        // One color scheme with the panel: gold frames the inspected object, blue marks the proposed new target, green a
        // confirmed relation, red only a broken item. Other choices are neutral. Amber is a panel color and never appears
        // in the world.
        internal static readonly Color Gold = new Color(1f, .8f, 0f), Proposed = new Color(.3f, .8f, 1f), ConfirmedRelation = new Color(.498f, .722f, .42f),
            Broken = new Color(1f, .502f, .502f), Neutral = new Color(.8156863f, .7950737f, .746353f);
        internal void Draw(SpatialCandidate[] candidates, PendingSelection pending, SpatialCandidate source, bool hasSource, SpatialCandidate confirmed,
            Camera camera, Vector3 origin, IReadOnlyList<Rect> uiBounds, Texture2D backing) {
            covered = uiBounds;
            labels.Clear();
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
            foreach (var candidate in candidates) {
                var shape = Project(candidate, camera, origin);
                if (shape == null) continue;
                bool offered = pending != null && pending.Offers.Contains(candidate.Choice);
                bool chosen = pending != null && ReferenceEquals(candidate.Choice, pending.Candidate);
                shape.Color = chosen ? Proposed : Neutral;
                shape.Detailed = offered || shape.Contains(pointer);
                if (offered) shape.Text = (chosen ? "New: " : "Choice: ") + Short(candidate.Choice.Label);
                if (shape.Track == null || shape.Detailed) shapes.Add(shape);
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
            // A confirmed relation stays marked while its object is inspected.
            if (confirmed != null && confirmed.Points.Length > 0 && !shapes.Any(s => s.Text != null && s.Candidate.Choice.Matches(confirmed.Choice))) {
                var shape = Project(confirmed, camera, origin);
                if (shape != null) {
                    shape.Color = ConfirmedRelation;
                    shape.Detailed = true;
                    shape.Text = "Confirmed: " + Short(confirmed.Choice.Label);
                    shapes.Add(shape);
                }
            }
            var zones = VisibleWorld(uiBounds);
            foreach (var shape in shapes.Where(s => s.Text != null)) {
                shape.Label = PlaceLabel(shape.Marker, shape.Text, zones);
                if (shape.Label.width > 0) labels.Add(shape);
            }
            var oldColor = GUI.color;
            var oldContent = GUI.contentColor;
            try {
                GUI.contentColor = Color.white;
                DrawTracks(shapes, zones);
                foreach (var zone in zones) {
                    GUI.BeginGroup(zone);
                    try {
                        foreach (var shape in shapes) DrawShape(shape, zone.position);
                        foreach (var label in labels) {
                            if (!zone.Overlaps(label.Label)) continue;
                            var rect = Offset(label.Label, zone.position);
                            GUI.color = Color.white;
                            GUI.DrawTexture(rect, backing);
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
            var shape = new Shape { Candidate = candidate };
            var anchor = ScreenPoint(camera, candidate.Position, origin);
            if (candidate.Observation != null && candidate.Choice.Handle is GameObject go && go != null) {
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                Dictionary<Renderer, KeyValuePair<LODGroup, int>> lods = null;
                var parts = new List<ObjectMarkingPart>();
#if ISSUE42_OBSERVATION
                var decisions = observing ? new List<string>() : null;
                var skipped = observing ? new List<string>() : null;
#else
                List<string> decisions = null;
#endif
                for (int index = 0; index < renderers.Length; index++) {
                    var renderer = renderers[index];
                    var name = "#" + index;
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
                    parts.Add(new ObjectMarkingPart(name, Point(bounds.center), Point(bounds.extents), oriented, Kind(renderer, go, ref lods)));
                }
                var view = camera.pixelRect;
                var toClip = camera.projectionMatrix * camera.worldToCameraMatrix;
                // One frame per object, around the piece its marker belongs to. Picking uses the same rectangle.
                var result = ObjectMarkingGeometry.Frame(Point(candidate.Position + origin), Point(camera.transform.position), parts, p => {
                    var clip = toClip * new Vector4((float)p.X, (float)p.Y, (float)p.Z, 1);
                    return new ClipPoint(clip.x, clip.y, clip.z, clip.w);
                }, ScreenRect.FromEdges(view.xMin, Screen.height - view.yMax, view.xMax, Screen.height - view.yMin), decisions);
                if (result.Frame.HasValue) {
                    var frame = result.Frame.Value;
                    var rect = Rect.MinMaxRect((float)frame.X, (float)frame.Y, (float)frame.Right, (float)frame.Bottom);
                    shape.Objects.Add(rect);
                    anchor = new Vector3(rect.center.x, rect.center.y, camera.nearClipPlane);
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

        Rect PlaceLabel(Rect marker, string text, List<Rect> zones) {
            Rect best = default;
            float distance = float.PositiveInfinity;
            foreach (var zone in zones) {
                if (zone.width < Mathf.Min(180, Screen.width)) continue;
                var content = new GUIContent(text);
                // Use the final pixel width for both wrapping and drawing. Rounding a natural width
                // down can introduce a line that was absent during measurement.
                float width = Mathf.Min(480, Mathf.Floor(zone.width), Mathf.Ceil(textStyle.CalcSize(content).x));
                var size = new Vector2(width, Mathf.Ceil(textStyle.CalcHeight(content, width)));
                if (zone.width < size.x || zone.height < size.y) continue;
                foreach (var desired in new[] {
                    new Vector2(marker.xMax + 8, marker.yMin - size.y - 8),
                    new Vector2(marker.xMax + 8, marker.yMax + 8),
                    new Vector2(marker.xMin - size.x - 8, marker.yMin - size.y - 8),
                    new Vector2(marker.xMin - size.x - 8, marker.yMax + 8)
                }) {
                    var position = new Vector2(Mathf.Clamp(desired.x, zone.xMin, zone.xMax - size.x),
                        Mathf.Clamp(desired.y, zone.yMin, zone.yMax - size.y));
                    var rect = new Rect(position, size);
                    if (labels.Any(s => s.Label.Overlaps(rect))) continue;
                    var delta = (position - marker.center).sqrMagnitude;
                    if (delta < distance) { distance = delta; best = rect; }
                }
            }
            // A label lies within the screen, whatever the zone it was placed in.
            if (best.width > 0) {
                best.x = Mathf.Clamp(best.x, 0, Mathf.Max(0, Screen.width - best.width));
                best.y = Mathf.Clamp(best.y, 0, Mathf.Max(0, Screen.height - best.height));
            }
            return best;
        }

        void DrawTracks(List<Shape> shapes, List<Rect> zones) {
            if (zones.Count == 0 || !shapes.Any(shape => shape.Track != null && shape.Track.Ribbon.Count != 0)) return;
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
            var visible = zones.Select(zone => ScreenRect.FromEdges(zone.xMin, zone.yMin, zone.xMax, zone.yMax)).ToArray();
            GL.PushMatrix();
            try {
                if (!trackMaterial.SetPass(0)) throw new InvalidOperationException("World track marking shader pass unavailable");
                // Geometry is already clipped in top-left screen pixels. No rotated GUI matrix or
                // group scissor is relied upon to keep a wide stroke out of screen UI.
                GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                GL.Begin(GL.TRIANGLES);
                try {
                    foreach (var shape in shapes.Where(s => s.Track != null)) {
                        DrawTriangles(WorldStrokeGeometry.ClipTriangles(shape.Track.Ribbon, visible), new Color(shape.Color.r, shape.Color.g, shape.Color.b, .22f));
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
            if (shape.Detailed) foreach (var rect in shape.Objects) Frame(Offset(rect, origin), shape.Color);
            var marker = Offset(shape.Marker, origin);
            if (marker.width == 0) return;
            GUI.color = new Color(.08f, .08f, .08f, 1f);
            GUI.DrawTexture(marker, Texture2D.whiteTexture);
            Frame(marker, shape.Color);
            GUI.color = shape.Color;
            GUI.DrawTexture(new Rect(marker.center.x - 5, marker.center.y - 1, 10, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(marker.center.x - 1, marker.center.y - 5, 2, 10), Texture2D.whiteTexture);
        }
        static void Frame(Rect rect, Color color) {
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2, rect.width, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 2, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 2, rect.y, 2, rect.height), Texture2D.whiteTexture);
        }
    }
}
