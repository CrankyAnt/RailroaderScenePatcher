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
        // A path of straight pieces in map coordinates that the marking draws as a ribbon, such as the track a car cut
        // stands on; null for every other candidate.
        internal Vector3[] Path { get; }
        internal string Details { get; }
        internal Vector3 Position => Points[Points.Length / 2];

        internal SpatialCandidate(SelectionCandidate choice, Vector3[] points, string details, SceneObservation observation = null, Vector3[] controlPoints = null, Vector3[] path = null) {
            Choice = choice;
            Points = points;
            Details = details;
            Observation = observation;
            ControlPoints = controlPoints;
            Path = path;
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
                var candidate = Of(kind, member);
                if (candidate != null) yield return candidate;
            }
        }

        // The members of the loaded track graph of one kind.
        static IEnumerable Members(SelectionKind kind) =>
            Native.Read(Native.Read(Native.Type("Track.Graph"), "Shared"), kind == SelectionKind.TrackSegment ? "AllSegments" : "Nodes") as IEnumerable;
        // The loaded segment or node with this ID, or null.
        internal static object Member(SelectionIdentity identity) {
            var members = Members(identity.Kind);
            if (members == null) return null;
            foreach (var member in members) if (member != null && Native.Read(member, "id") as string == identity.Identifier) return member;
            return null;
        }
        // One segment or node of the track graph as a candidate with its current geometry, or null without an ID.
        internal static SpatialCandidate Of(SelectionKind kind, object member) {
            if (member == null) return null;
            var id = Native.Read(member, "id") as string;
            if (String.IsNullOrWhiteSpace(id)) return null;
            var identity = SelectionIdentity.Graph(kind, id);
            if (kind == SelectionKind.TrackNode) {
                var point = (Vector3)Native.Read(member, "Position");
                return new SpatialCandidate(new SelectionCandidate(member, identity, identity.ToString(), Coordinates(point)),
                    Finite(point) ? new[] { point } : Array.Empty<Vector3>(), "Map position " + Coordinates(point));
            }

            var a = Native.Read(member, "a");
            var b = Native.Read(member, "b");
            if (a == null || b == null) {
                return new SpatialCandidate(new SelectionCandidate(member, identity, identity.ToString(), "Missing endpoints"),
                    Array.Empty<Vector3>(), "Endpoints unavailable");
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
            return new SpatialCandidate(new SelectionCandidate(member, identity, label, evidence), points, ends,
                controlPoints: new[] { p0, p1, p2, p3 });
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
        // The track element the inspected object follows, and the object. Its marking is rebuilt from the element
        // itself a few times a second, so it moves with the track; the element is looked up once.
        SelectionIdentity followed;
        object followedFor, followedMember;
        SpatialCandidate followedMark;
        int followedAt;
        bool followedMissing;
        // What the inspected object is bound to now, while its Connections tab is open: the track elements of its
        // current binding, native or set by a patch. Their markings are rebuilt from the graph elements a few times a
        // second, as the followed track's is.
        SelectionIdentity[] bound = new SelectionIdentity[0];
        object boundFor;
        object[] boundMembers;
        SpatialCandidate[] boundMarks = new SpatialCandidate[0];
        int boundAt;
        internal void Binds(object owner, SelectionIdentity[] identities) {
            boundFor = owner; bound = identities ?? new SelectionIdentity[0]; boundMembers = null; boundMarks = new SpatialCandidate[0];
        }
        SpatialCandidate[] Bound(SceneObservation inspected) {
            if (bound.Length == 0 || inspected == null || !ReferenceEquals(boundFor, inspected.Handle)) return new SpatialCandidate[0];
            if (boundMembers != null && Time.frameCount < boundAt + 20) return boundMarks;
            try {
                if (boundMembers == null) boundMembers = bound.Select(SpatialSelectionScene.Member).ToArray();
                boundMarks = bound.Select((identity, index) => boundMembers[index] == null ? null : SpatialSelectionScene.Of(identity.Kind, boundMembers[index])).Where(mark => mark != null).ToArray();
            } catch { boundMarks = new SpatialCandidate[0]; boundMembers = new object[bound.Length]; }
            boundAt = Time.frameCount;
            return boundMarks;
        }
        internal void Follows(object owner, SelectionIdentity identity) {
            followedFor = owner; followed = identity; followedMember = null; followedMark = null; followedMissing = false;
        }
        SpatialCandidate Followed(SceneObservation inspected) {
            if (followed == null || inspected == null || !ReferenceEquals(followedFor, inspected.Handle) || followedMissing) return null;
            if (followedMark != null && Time.frameCount < followedAt + 20) return followedMark;
            try {
                if (followedMember == null) followedMember = SpatialSelectionScene.Member(followed);
                followedMark = followedMember == null ? null : SpatialSelectionScene.Of(followed.Kind, followedMember);
            } catch { followedMark = null; }
            // An element that is not in the graph is not searched for again until the relation is read again.
            if (followedMark == null) followedMissing = true;
            followedAt = Time.frameCount;
            return followedMark;
        }
        internal PendingSelection Pending => session.Pending;
        // The last confirmation, until a later choice is cancelled: after Cancel nothing is chosen, so the panel shows no
        // earlier target as confirmed beside fields that were cleared.
        bool withdrawn;
        internal ConfirmedSelection Confirmed => withdrawn ? null : session.Confirmed;
        internal string ConfirmedName => confirmedMark?.Observation?.DisplayName ?? confirmedMark?.Choice.Label;

        internal SpatialSelection(IInspectionScene inventory, Func<IReadOnlyList<SceneObservation>> entries = null) {
            scene = new SpatialSelectionScene(inventory, entries);
            session = new SelectionSession(scene);
            // The overlay decides the game-defined groups from the inventory snapshot.
            overlay.Inventory = entries;
            // The overlay asks which objects are inventory objects; the set follows the inventory snapshot.
            IReadOnlyList<SceneObservation> known = null; HashSet<object> handles = null; var counted = -1;
            overlay.IsInventory = go => {
                var now = entries?.Invoke();
                if (now == null) return false;
                // The set is built again when the snapshot is another one or has another number of entries; that
                // number is kept beside the set, because two entries of one object make the set smaller than the list.
                if (!ReferenceEquals(now, known) || handles == null || counted != now.Count) { known = now; counted = now.Count; handles = new HashSet<object>(now.Select(e => e.Handle)); }
                return handles.Contains(go);
            };
        }

        // The candidates a choice shows and offers: every candidate of its kind with a usable position, less those a
        // patch could not name when the choice is for a scene patch file (SelectionOffers).
        SpatialCandidate[] Offered(SelectionKind kind, bool inspectOnly) {
            var all = scene.Scan(kind).Where(c => c.Points.Length != 0).ToArray();
            if (inspectOnly) return all;
            var offered = new HashSet<SelectionCandidate>(SelectionOffers.For(all.Select(c => c.Choice), false, excluded));
            return all.Where(c => offered.Contains(c.Choice)).ToArray();
        }
        bool inspectOnly;
        SelectionIdentity excluded;
        internal void Begin(SelectionKind kind, SceneObservation origin, bool inspectOnly = false, SelectionIdentity excluded = null, string purpose = null) {
            if (Pending != null) return;
            if (Camera.main == null) throw new InvalidOperationException("The native map editor camera is unavailable");
            WorldOffset();
            this.inspectOnly = inspectOnly;
            this.excluded = excluded;
            var current = Offered(kind, inspectOnly);
            var newSource = origin == null ? null : SpatialSelectionScene.Observe(origin);
            if (current.Length == 0) throw new InvalidOperationException("No candidates with usable world positions were found");
            purpose = purpose ?? (kind == SelectionKind.SceneObject
                ? (origin == null ? "Select scene object" : "Select replacement scene object")
                : (kind == SelectionKind.TrackSegment ? "Select replacement track segment" : "Select replacement track node"));
            session.Begin(kind, purpose, newSource?.Choice, inspectOnly);
            overlay.ClearInteraction();
            lastPointer = null;
            candidates = current;
            source = newSource;
            // A choice for the same object keeps that object's confirmed relation in view, so the track it follows
            // stays green beside the blue candidate. A choice for another object starts without one.
            if (Confirmed?.AppliesTo(origin?.Handle) != true) confirmedMark = null;
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
            var current = Offered(Pending.Kind, inspectOnly);
            var hits = overlay.Pick(current, camera, WorldOffset(), pointer, covered, Pending.Candidate, out var listRow, out var isCrossingSetup);
            if (listRow && hits.Length == 1) {
                var offered = Pending.Offers.FirstOrDefault(offer => offer.Matches(hits[0]));
                if (offered != null) { Pending.Choose(offered); return; }
            }
            candidates = current;
            lastPointer = pointer;
            Pending.Offer(hits, isCrossingSetup);
        }
        // The pointer of the last click that offered candidates, in GUI coordinates.
        Vector2? lastPointer;
        // The point of a confirmed track segment nearest to the click that offered it, in map coordinates: where on the
        // track the click was. Null for a candidate without a curve or without a click.
        internal Vector3? ClickedPoint(SpatialCandidate candidate) {
            var camera = Camera.main;
            if (candidate?.ControlPoints == null || candidate.ControlPoints.Length != 4 || lastPointer == null || camera == null) return null;
            var origin = WorldOffset();
            Vector3 p0 = candidate.ControlPoints[0], p1 = candidate.ControlPoints[1], p2 = candidate.ControlPoints[2], p3 = candidate.ControlPoints[3];
            Vector3? best = null; var nearest = float.PositiveInfinity;
            for (var i = 0; i <= 400; i++) {
                float t = i / 400f, s = 1 - t;
                var point = s * s * s * p0 + 3 * s * s * t * p1 + 3 * s * t * t * p2 + t * t * t * p3;
                var screen = camera.WorldToScreenPoint(point + origin);
                if (screen.z <= 0) continue;
                // GUI coordinates run down from the top of the screen.
                var gui = new Vector2(screen.x, Screen.height - screen.y);
                var distance = (gui - lastPointer.Value).sqrMagnitude;
                if (distance < nearest) { nearest = distance; best = point; }
            }
            return best;
        }

        internal SpatialCandidate Find(SelectionCandidate candidate) => candidates.FirstOrDefault(c => ReferenceEquals(c.Choice, candidate));

        internal SpatialCandidate Confirm() {
            var selected = Find(Pending?.Candidate);
            if (selected == null || !session.Confirm()) return null;
            withdrawn = false;
            ClearMarks();
            confirmedMark = selected;
            return selected;
        }

        internal void Cancel() {
            if (session.Pending != null) { withdrawn = true; confirmedMark = null; }
            session.Cancel();
            ClearMarks();
        }

        internal void Clear() {
            session.Clear();
            confirmedMark = null;
            withdrawn = false;
            lastPointer = null;
            ClearMarks();
        }

        // An edit used the confirmed target: nothing is confirmed any more, and its marking goes.
        internal void Consume() { session.Consume(); confirmedMark = null; }
        internal void Inspect(object handle) { if (session.Inspect(handle)) confirmedMark = null; }

        // The surface under the objects may have changed; their frames are cut at it anew.
        internal void ClearSurface() { overlay.ClearSurface(); }

        internal bool OverLabel(Vector2 pointer, IReadOnlyList<Rect> uiBounds) => overlay.OverLabel(pointer, uiBounds);

        internal void Release() {
            Cancel();
            overlay.Release();
        }

        void ClearMarks() {
            overlay.ClearInteraction();
            source = null;
            candidates = Array.Empty<SpatialCandidate>();
            excluded = null;
        }

        // trackPoint is the inspected starter placement marker's track point in map coordinates, or null; cut is the track
        // its car cut stands on from that point back, in map coordinates, or null.
        internal void Draw(SceneObservation inspected, IReadOnlyList<Rect> covered, Texture2D backing, double[] trackPoint = null, IReadOnlyList<double[]> cut = null) {
            if (Event.current.type != EventType.Repaint || (Pending == null && inspected == null)) return;
            var camera = Camera.main;
            if (camera == null) return;
            var original = source ?? (inspected == null ? null : SpatialSelectionScene.Observe(inspected));
            // The confirmed relation belongs to the object it was chosen for, and is not the inspected object itself.
            var confirmed = Confirmed;
            var relation = confirmed?.Source != null && confirmed.AppliesTo(inspected?.Handle) && confirmedMark != null &&
                (Pending == null || confirmed.AppliesTo(source?.Choice.Handle)) && !ReferenceEquals(confirmedMark.Choice.Handle, inspected.Handle) ? confirmedMark : null;
            // The track point is its own marking, apart from the object's: a car placement reads that point, not the
            // object position. Its evidence tells it from the object's own candidate.
            var point = inspected != null && trackPoint != null && trackPoint.Length == 3
                ? new SpatialCandidate(new SelectionCandidate(inspected.Handle, SelectionIdentity.SceneObject(inspected.Identity), inspected.Family + ": " + inspected.Identity.Path, "track point"),
                    new[] { new Vector3((float)trackPoint[0], (float)trackPoint[1], (float)trackPoint[2]) }, null,
                    path: cut != null && cut.Count >= 2 ? cut.Select(p => new Vector3((float)p[0], (float)p[1], (float)p[2])).ToArray() : null)
                : null;
            overlay.Draw(candidates, Pending, original, source != null, relation, Followed(inspected), Bound(inspected), camera, WorldOffset(), covered, backing, point);
        }

        static Vector3 WorldOffset() =>
            (Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "GameToWorld", Vector3.zero);
    }
}
