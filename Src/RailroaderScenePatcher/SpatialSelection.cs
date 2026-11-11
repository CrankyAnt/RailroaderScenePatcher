// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    internal sealed class SpatialCandidate {
        internal SelectionCandidate Choice { get; }
        internal SceneObservation Observation { get; }
        internal Vector3[] Points { get; }
        internal Vector3[] ControlPoints { get; }
        internal string Details { get; }
        internal Vector3 Position => Points[Points.Length / 2];

        internal SpatialCandidate(SelectionCandidate choice, Vector3[] points, string details, SceneObservation observation = null, Vector3[] controlPoints = null) {
            Choice = choice;
            Points = points;
            Details = details;
            Observation = observation;
            ControlPoints = controlPoints;
        }
    }

    internal sealed class SpatialSelectionScene : ISelectionScene {
        readonly IInspectionScene inventory;
        internal SpatialSelectionScene(IInspectionScene inventory) { this.inventory = inventory; }
        public IEnumerable<string> MapIds => inventory.MapIds;
        public IEnumerable<SelectionCandidate> Discover(SelectionKind kind) => Scan(kind).Select(c => c.Choice);

        internal static SpatialCandidate Observe(SceneObservation entry) {
            var position = entry.Position.Count == 3
                ? new[] { new Vector3((float)entry.Position[0], (float)entry.Position[1], (float)entry.Position[2]) }
                : Array.Empty<Vector3>();
            var label = entry.Family + ": " + entry.Identity.Path + (entry.Active ? "" : " [inactive]");
            return new SpatialCandidate(new SelectionCandidate(entry.Handle, SelectionIdentity.SceneObject(entry.Identity), label),
                position, String.Join("\n", entry.Bindings), entry);
        }

        internal IEnumerable<SpatialCandidate> Scan(SelectionKind kind) {
            if (kind == SelectionKind.SceneObject) {
                foreach (var observation in inventory.Discover()) yield return Observe(observation);
                yield break;
            }

            var graph = Native.Read(Native.Type("Track.Graph"), "Shared");
            var members = Native.Read(graph, kind == SelectionKind.TrackSegment ? "AllSegments" : "Nodes") as IEnumerable;
            if (members == null) throw new InvalidOperationException("The loaded track graph is unavailable");
            foreach (var member in members) {
                if (member == null) continue;
                var id = Native.Read(member, "id") as string;
                if (String.IsNullOrWhiteSpace(id)) continue;
                var identity = SelectionIdentity.Graph(kind, id);
                if (kind == SelectionKind.TrackNode) {
                    var point = (Vector3)Native.Read(member, "Position");
                    yield return new SpatialCandidate(new SelectionCandidate(member, identity, identity.ToString(), Coordinates(point)),
                        Finite(point) ? new[] { point } : Array.Empty<Vector3>(), "Map position " + Coordinates(point));
                    continue;
                }

                var a = Native.Read(member, "a");
                var b = Native.Read(member, "b");
                if (a == null || b == null) {
                    yield return new SpatialCandidate(new SelectionCandidate(member, identity, identity.ToString(), "Missing endpoints"),
                        Array.Empty<Vector3>(), "Endpoints unavailable");
                    continue;
                }
                var endA = Native.Read(a, "id") as string;
                var endB = Native.Read(b, "id") as string;
                var curve = Native.Read(member, "Curve");
                var p0 = (Vector3)Native.Read(curve, "P0");
                var p1 = (Vector3)Native.Read(curve, "P1");
                var p2 = (Vector3)Native.Read(curve, "P2");
                var p3 = (Vector3)Native.Read(curve, "P3");
                var points = Array.Empty<Vector3>();
                bool active = (bool)Native.Read(member, "Available") && (bool)Native.Read(member, "GroupEnabled");
                if (!String.IsNullOrWhiteSpace(endA) && !String.IsNullOrWhiteSpace(endB) &&
                    Finite(p0) && Finite(p1) && Finite(p2) && Finite(p3)) {
                    points = new Vector3[17];
                    for (int i = 0; i < points.Length; i++) {
                        float t = (float)i / (points.Length - 1);
                        float s = 1 - t;
                        points[i] = s * s * s * p0 + 3 * s * s * t * p1 + 3 * s * t * t * p2 + t * t * t * p3;
                    }
                }
                var ends = "Endpoints: " + endA + " / " + endB;
                var evidence = String.Join("|", active.ToString(), endA, endB, Coordinates(p0), Coordinates(p1), Coordinates(p2), Coordinates(p3));
                var label = identity + (active ? "" : " [inactive]");
                yield return new SpatialCandidate(new SelectionCandidate(member, identity, label, evidence), points, ends,
                    controlPoints: new[] { p0, p1, p2, p3 });
            }
        }

        static bool Finite(Vector3 point) => IsFinite(point.x) && IsFinite(point.y) && IsFinite(point.z);
        static bool IsFinite(float n) => !Single.IsNaN(n) && !Single.IsInfinity(n);
        static string Coordinates(Vector3 point) =>
            String.Format(CultureInfo.InvariantCulture, "{0:R}, {1:R}, {2:R}", point.x, point.y, point.z);
    }

    internal sealed class SpatialSelection {
        const float HitRadius = 12f;
        readonly SpatialSelectionScene scene;
        readonly SelectionSession session;
        SpatialCandidate[] candidates = Array.Empty<SpatialCandidate>();
        SpatialCandidate source;
        internal PendingSelection Pending => session.Pending;
        internal ConfirmedSelection Confirmed => session.Confirmed;

        internal SpatialSelection(IInspectionScene inventory) {
            scene = new SpatialSelectionScene(inventory);
            session = new SelectionSession(scene);
        }

        internal void Begin(SelectionKind kind, SceneObservation origin) {
            if (Pending != null) return;
            if (Camera.main == null) throw new InvalidOperationException("The editor camera is unavailable");
            WorldOffset();
            var current = scene.Scan(kind).Where(c => c.Points.Length != 0).ToArray();
            var newSource = origin == null ? null : SpatialSelectionScene.Observe(origin);
            if (current.Length == 0) throw new InvalidOperationException("No candidates with usable world positions were found");
            var purpose = kind == SelectionKind.SceneObject
                ? (origin == null ? "Select scene object" : "Select replacement scene object")
                : (kind == SelectionKind.TrackSegment ? "Select replacement track segment" : "Select replacement track node");
            session.Begin(kind, purpose, newSource?.Choice);
            candidates = current;
            source = newSource;
        }

        internal void Pick(Vector2 pointer) {
            if (Pending == null) return;
            var camera = Camera.main;
            if (camera == null) {
                Pending.Offer(Array.Empty<SelectionCandidate>());
                return;
            }
            var current = scene.Scan(Pending.Kind).Where(c => c.Points.Length != 0).ToArray();
            var offset = WorldOffset();
            var hits = current.Where(c => Hit(c, camera, offset, pointer)).Select(c => c.Choice).ToArray();
            candidates = current;
            Pending.Offer(hits);
        }

        internal SpatialCandidate Find(SelectionCandidate candidate) => candidates.FirstOrDefault(c => ReferenceEquals(c.Choice, candidate));

        internal SpatialCandidate Confirm() {
            var selected = Find(Pending?.Candidate);
            if (selected == null || !session.Confirm()) return null;
            ClearMarks();
            return selected;
        }

        internal void Cancel() {
            session.Cancel();
            ClearMarks();
        }

        void ClearMarks() {
            source = null;
            candidates = Array.Empty<SpatialCandidate>();
        }

        internal void Draw(SceneObservation inspected) {
            if (Event.current.type != EventType.Repaint || (Pending == null && inspected == null)) return;
            var camera = Camera.main;
            if (camera == null) return;
            var offset = WorldOffset();
            var pointer = Event.current.mousePosition;
            if (Pending != null) {
                foreach (var candidate in candidates) {
                    if (ReferenceEquals(candidate.Choice, Pending.Candidate) || Pending.Offers.Contains(candidate.Choice)) continue;
                    if (candidate.Points.Length == 1 || Hit(candidate, camera, offset, pointer))
                        DrawMark(candidate, camera, offset, new Color(.46f, .81f, 1f), null);
                }
                foreach (var offer in Pending.Offers) {
                    var candidate = Find(offer);
                    bool chosen = ReferenceEquals(offer, Pending.Candidate);
                    if (candidate != null)
                        DrawMark(candidate, camera, offset, chosen ? new Color(.4f, 1f, .55f) : new Color(.46f, .81f, 1f),
                            (chosen ? "Candidate: " : "Choice: ") + offer.Label);
                }
            }
            var original = source ?? (inspected == null ? null : SpatialSelectionScene.Observe(inspected));
            if (original != null && original.Points.Length != 0)
                DrawMark(original, camera, offset, new Color(1f, .77f, .25f), (source == null ? "Inspected: " : "Source: ") + original.Choice.Label);
        }

        static Vector3 WorldOffset() =>
            (Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "GameToWorld", Vector3.zero);

        static Vector3 ScreenPoint(Camera camera, Vector3 point, Vector3 offset) {
            var screen = camera.WorldToScreenPoint(point + offset);
            screen.y = Screen.height - screen.y;
            return screen;
        }

        static bool InFront(Camera camera, Vector3 point) => point.z >= camera.nearClipPlane && point.z <= camera.farClipPlane;

        static bool Hit(SpatialCandidate candidate, Camera camera, Vector3 offset, Vector2 pointer) {
            var previous = ScreenPoint(camera, candidate.Points[0], offset);
            if (candidate.Points.Length == 1)
                return InFront(camera, previous) && ((Vector2)previous - pointer).sqrMagnitude <= HitRadius * HitRadius;
            if (!MayHitCurve(candidate.ControlPoints, camera, offset, pointer)) return false;
            for (int i = 1; i < candidate.Points.Length; i++) {
                var next = ScreenPoint(camera, candidate.Points[i], offset);
                if (InFront(camera, previous) && InFront(camera, next) && WithinLine(pointer, previous, next)) return true;
                previous = next;
            }
            return false;
        }

        static bool MayHitCurve(Vector3[] controlPoints, Camera camera, Vector3 offset, Vector2 pointer) {
            // A projected curve stays in its projected control hull when every point is in front of the camera.
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var control in controlPoints) {
                var point = ScreenPoint(camera, control, offset);
                if (!InFront(camera, point)) return true;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return pointer.x >= min.x - HitRadius && pointer.x <= max.x + HitRadius &&
                pointer.y >= min.y - HitRadius && pointer.y <= max.y + HitRadius;
        }

        static bool WithinLine(Vector2 point, Vector2 a, Vector2 b) {
            var line = b - a;
            var along = line.sqrMagnitude == 0 ? 0 : Mathf.Clamp01(Vector2.Dot(point - a, line) / line.sqrMagnitude);
            return (point - (a + along * line)).sqrMagnitude <= HitRadius * HitRadius;
        }

        static void DrawMark(SpatialCandidate candidate, Camera camera, Vector3 offset, Color color, string label) {
            var position = ScreenPoint(camera, candidate.Position, offset);
            var oldColor = GUI.color;
            GUI.color = color;
            try {
                if (candidate.Points.Length > 1) {
                    var a = ScreenPoint(camera, candidate.Points[0], offset);
                    for (int i = 1; i < candidate.Points.Length; i++) {
                        var b = ScreenPoint(camera, candidate.Points[i], offset);
                        if (InFront(camera, a) && InFront(camera, b)) DrawLine(a, b);
                        a = b;
                    }
                }
                if (!InFront(camera, position) || !new Rect(0, 0, Screen.width, Screen.height).Contains(position)) return;
                GUI.DrawTexture(new Rect(position.x - 5, position.y - 1, 10, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(position.x - 1, position.y - 5, 2, 10), Texture2D.whiteTexture);
                if (label != null) {
                    var content = new GUIContent(label);
                    var size = GUI.skin.box.CalcSize(content);
                    var x = Mathf.Clamp(position.x + 8, 0, Mathf.Max(0, Screen.width - size.x));
                    var y = Mathf.Clamp(position.y - size.y - 6, 0, Mathf.Max(0, Screen.height - size.y));
                    GUI.Box(new Rect(x, y, size.x, size.y), content);
                }
            } finally { GUI.color = oldColor; }
        }

        static void DrawLine(Vector2 a, Vector2 b) {
            var delta = b - a;
            var original = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
            try { GUI.DrawTexture(new Rect(a.x, a.y - 1, delta.magnitude, 2), Texture2D.whiteTexture); }
            finally { GUI.matrix = original; }
        }
    }
}
