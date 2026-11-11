// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
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

        internal void Release() {
            if (trackMaterial != null) UnityEngine.Object.Destroy(trackMaterial);
            trackMaterial = null;
            labels.Clear();
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

        internal void Draw(SpatialCandidate[] candidates, PendingSelection pending, SpatialCandidate source, bool hasSource,
            Camera camera, Vector3 origin, IReadOnlyList<Rect> uiBounds, Texture2D backing) {
            covered = uiBounds;
            labels.Clear();
            if (textStyle == null) {
                // Skin labels can carry one-line height/offset constraints. Measure and draw with
                // an unconstrained style so the opaque backing encloses every wrapped identity line.
                textStyle = new GUIStyle {
                    font = GUI.skin.label.font,
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
                shape.Color = chosen ? new Color(.4f, 1f, .55f) : new Color(.46f, .81f, 1f);
                shape.Detailed = offered || shape.Contains(pointer);
                if (offered) shape.Text = (chosen ? "Candidate: " : "Choice: ") + candidate.Choice.Label;
                if (shape.Track == null || shape.Detailed) shapes.Add(shape);
            }
            if (source != null && source.Points.Length > 0) {
                var marked = shapes.FirstOrDefault(s => s.Text != null && s.Candidate.Choice.Matches(source.Choice));
                var shape = Project(source, camera, origin);
                if (marked != null) {
                    if (hasSource) marked.Text = "Source / " + marked.Text;
                } else if (shape != null) {
                    shape.Color = new Color(1f, .77f, .25f);
                    shape.Detailed = true;
                    shape.Text = (hasSource ? "Source: " : "Inspected: ") + source.Choice.Label;
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
                            GUI.color = label.Color;
                            GUI.DrawTexture(new Rect(rect.x, rect.y, 3, rect.height), Texture2D.whiteTexture);
                            GUI.color = Color.white;
                            GUI.Label(rect, label.Text, textStyle);
                        }
                    } finally { GUI.EndGroup(); }
                }
            } finally { GUI.color = oldColor; GUI.contentColor = oldContent; }
        }

        Shape Project(SpatialCandidate candidate, Camera camera, Vector3 origin) {
            if (candidate.Points.Length == 0) return null;
            var shape = new Shape { Candidate = candidate };
            var anchor = ScreenPoint(camera, candidate.Position, origin);
            if (candidate.Observation != null && candidate.Choice.Handle is GameObject go && go != null) {
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true)) {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    var bounds = renderer.bounds;
                    if (bounds.size.sqrMagnitude < .000001f) continue;
                    var points = new List<Vector3>();
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y += 2)
                            for (int z = -1; z <= 1; z += 2)
                                points.Add(camera.WorldToScreenPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))));
                    if (points.Any(p => !InFront(camera, p))) continue;
                    var min = new Vector2(points.Min(p => p.x), Screen.height - points.Max(p => p.y));
                    var max = new Vector2(points.Max(p => p.x), Screen.height - points.Min(p => p.y));
                    shape.Objects.Add(Rect.MinMaxRect(min.x - 4, min.y - 4, max.x + 4, max.y + 4));
                }
                if (shape.Objects.Count != 0) {
                    var rect = shape.Objects.Aggregate(Union);
                    anchor = new Vector3(rect.center.x, rect.center.y, camera.nearClipPlane);
                }
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

        static Vector3 ScreenPoint(Camera camera, Vector3 point, Vector3 origin) {
            var projected = camera.WorldToScreenPoint(point + origin);
            projected.y = Screen.height - projected.y;
            return projected;
        }
        static bool InFront(Camera camera, Vector3 point) => point.z >= camera.nearClipPlane && point.z <= camera.farClipPlane;
        static Rect Offset(Rect rect, Vector2 origin) => new Rect(rect.position - origin, rect.size);
        static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

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
