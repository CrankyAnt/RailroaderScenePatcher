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

    internal sealed class SpatialSelectionScene : ITargetedSelectionScene {
        readonly IInspectionScene inventory;
        // The entries of the inventory snapshot, or null. With them the scene objects offered for a choice are the
        // snapshot's observations and no discovery runs for a choice; confirming reads the chosen object itself.
        readonly Func<IReadOnlyList<SceneObservation>> entries;
        internal SpatialSelectionScene(IInspectionScene inventory, Func<IReadOnlyList<SceneObservation>> entries = null) { this.inventory = inventory; this.entries = entries; }
        // Every current candidate of one identity. A scene object is resolved by its path and observed now, so two
        // objects at one path are both returned and an object that is gone or no longer observed is not. A scene that
        // cannot observe one object, and a track identity, read every candidate of the kind.
        public IEnumerable<SelectionCandidate> Find(SelectionIdentity identity) {
            if (identity.Kind != SelectionKind.SceneObject || !(inventory is ITargetedInspectionScene targeted)) return Discover(identity.Kind);
            var found = new List<SelectionCandidate>();
            foreach (var target in BoardScene.LoadedAt(identity.Target.Scene, identity.Target.Path)) {
                var observation = targeted.Observe(target, identity.Target);
                if (observation != null) found.Add(Observe(observation).Choice);
            }
            return found;
        }
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
                var snapshot = entries?.Invoke();
                foreach (var observation in snapshot != null && snapshot.Count != 0 ? snapshot : inventory.Discover()) yield return Observe(observation);
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
        readonly WorldSelectionOverlay overlay = new WorldSelectionOverlay();
        readonly SpatialSelectionScene scene;
        readonly SelectionSession session;
        SpatialCandidate[] candidates = Array.Empty<SpatialCandidate>();
        SpatialCandidate source;
        // The confirmed target and the object it was confirmed for, kept for its world marking.
        SpatialCandidate confirmedMark;
        object confirmedFor;
        internal PendingSelection Pending => session.Pending;
        // The last confirmation, until a later choice is cancelled: after Cancel nothing is chosen, so the panel shows no
        // earlier target as confirmed beside fields that were cleared.
        bool withdrawn;
        internal ConfirmedSelection Confirmed => withdrawn ? null : session.Confirmed;

        internal SpatialSelection(IInspectionScene inventory, Func<IReadOnlyList<SceneObservation>> entries = null) {
            scene = new SpatialSelectionScene(inventory, entries);
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
            confirmedMark = null; confirmedFor = null;
        }

        internal void Pick(Vector2 pointer, IReadOnlyList<Rect> covered) {
            if (Pending == null) return;
            // A UI click never replaces the pending offers, even if input bounds changed this frame.
            if (covered.Any(rect => rect.Contains(pointer))) return;
            var camera = Camera.main;
            if (camera == null) {
                Pending.Offer(Array.Empty<SelectionCandidate>());
                return;
            }
            var current = scene.Scan(Pending.Kind).Where(c => c.Points.Length != 0).ToArray();
            var hits = overlay.Pick(current, camera, WorldOffset(), pointer, covered);
            candidates = current;
            Pending.Offer(hits);
        }

        internal SpatialCandidate Find(SelectionCandidate candidate) => candidates.FirstOrDefault(c => ReferenceEquals(c.Choice, candidate));

        internal SpatialCandidate Confirm() {
            var selected = Find(Pending?.Candidate);
            if (selected == null || !session.Confirm()) return null;
            withdrawn = false;
            var owner = source?.Choice.Handle;
            ClearMarks();
            confirmedMark = selected; confirmedFor = owner;
            return selected;
        }

        internal void Cancel() {
            if (session.Pending != null) withdrawn = true;
            session.Cancel();
            ClearMarks();
        }

        internal void Release() {
            Cancel();
            overlay.Release();
        }

        void ClearMarks() {
            source = null;
            candidates = Array.Empty<SpatialCandidate>();
        }

        internal void Draw(SceneObservation inspected, IReadOnlyList<Rect> covered, Texture2D backing) {
            if (Event.current.type != EventType.Repaint || (Pending == null && inspected == null)) return;
            var camera = Camera.main;
            if (camera == null) return;
            var original = source ?? (inspected == null ? null : SpatialSelectionScene.Observe(inspected));
            // The confirmed relation belongs to the object it was chosen for, and is not the inspected object itself.
            var relation = Pending == null && session.Confirmed != null && confirmedMark != null && inspected != null &&
                (confirmedFor == null || ReferenceEquals(confirmedFor, inspected.Handle)) && !ReferenceEquals(confirmedMark.Choice.Handle, inspected.Handle) ? confirmedMark : null;
            overlay.Draw(candidates, Pending, original, source != null, relation, camera, WorldOffset(), covered, backing);
        }

        static Vector3 WorldOffset() =>
            (Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "GameToWorld", Vector3.zero);
    }
}
