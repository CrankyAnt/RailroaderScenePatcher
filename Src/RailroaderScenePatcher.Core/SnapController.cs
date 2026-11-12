// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    public interface ISnapScene : ISelectionScene {
        IEnumerable<SnapGeometry> DiscoverSnapTargets(SelectionKind kind);
    }

    // Copied map-coordinate geometry and the selection observation from the same scan.
    public sealed class SnapGeometry {
        public SelectionCandidate Candidate { get; }
        public IReadOnlyList<IReadOnlyList<double>> Points { get; }
        public SnapGeometry(SelectionCandidate candidate, IEnumerable<IEnumerable<double>> points) {
            Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
            Points = Array.AsReadOnly(points.Select(p => (IReadOnlyList<double>)Array.AsReadOnly(p.ToArray())).ToArray());
        }
        internal bool Matches(SnapGeometry other) => other != null && Candidate.Matches(other.Candidate) &&
            Points.Count == other.Points.Count && Points.Select((p, i) => p.SequenceEqual(other.Points[i])).All(x => x);
    }

    public sealed class SnapRequest {
        internal SnapController Owner;
        internal TargetIdentity Target;
        internal InspectedTarget Inspected;
        internal SnapGeometry Geometry;
        internal string[] Maps;
        internal Placement Desired;
        public SelectionIdentity Identity => Geometry.Candidate.Identity;
        public Placement Placement => new Placement((double[])Desired.Position.Clone(), (double[])Desired.Rotation.Clone());
    }

    // One explicit physical edit, stored through the same fixed-placement and history route as numeric edits.
    public sealed class SnapController {
        readonly AuthoringSession session;
        readonly IScene scene;
        readonly ISnapScene tracks;
        public SnapController(AuthoringSession session, IScene scene, ISnapScene tracks) {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            this.tracks = tracks ?? throw new ArgumentNullException(nameof(tracks));
        }
        public SnapRequest Prepare(TargetIdentity target, InspectedTarget inspected, ConfirmedSelection selected, SelectionCandidate confirmedCandidate) {
            if (selected == null || selected.Identity.Kind == SelectionKind.SceneObject)
                throw new InvalidOperationException("Explicitly select a track segment or endpoint first");
            if (inspected == null) throw new InvalidOperationException("Inspect the object before snapping");
            var maps = selected.MapIds.ToArray();
            CheckMaps(maps);
            if (selected.Source != null && !selected.Source.Equals(SelectionIdentity.SceneObject(target)))
                throw new InvalidOperationException("The selected snap target belongs to another inspected object");
            var geometry = Resolve(selected.Identity);
            if (confirmedCandidate == null || !confirmedCandidate.Matches(geometry.Candidate))
                throw new InvalidOperationException("Snap target changed after confirmation; select it again");
            var resolved = scene.Resolve(target);
            if (resolved == null || resolved.Count != 1) throw new InvalidOperationException("The object must resolve uniquely");
            inspected.Validate(scene, target, resolved[0]);
            var prior = scene.Read(resolved[0]).Placement;
            PatchEngine.CheckPlacement(prior.Position, prior.Rotation);
            var position = TrackSnapGeometry.Position(geometry, prior.Position);
            PatchEngine.CheckPlacement(position, prior.Rotation);
            CheckMaps(maps);
            return new SnapRequest { Owner = this, Target = target, Inspected = inspected, Geometry = geometry, Maps = maps,
                Desired = new Placement(position, (double[])prior.Rotation.Clone()) };
        }
        public Outcome Apply(SnapRequest request) {
            try {
                if (request == null || !ReferenceEquals(request.Owner, this)) throw new InvalidOperationException("Prepare a snap for this editor session first");
                Validate(request);
                return session.Edit(request.Target, request.Desired, null, request.Inspected, () => Validate(request));
            } catch (Exception e) { return new Outcome { Error = e.Message }; }
        }
        void Validate(SnapRequest request) {
            CheckMaps(request.Maps);
            if (!request.Geometry.Matches(Resolve(request.Identity))) throw new InvalidOperationException("Snap target changed; select it again");
            CheckMaps(request.Maps);
        }
        SnapGeometry Resolve(SelectionIdentity identity) {
            var matches = tracks.DiscoverSnapTargets(identity.Kind).Where(g => g.Candidate.Identity.Equals(identity)).Take(2).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException(matches.Length == 0 ? "Snap target is missing; select it again" : "Snap target is ambiguous; select a unique identity");
            return matches[0];
        }
        void CheckMaps(string[] maps) {
            if (maps.Length == 0 || !maps.SequenceEqual(scene.MapIds) || !maps.SequenceEqual(tracks.MapIds))
                throw new InvalidOperationException("Snap map context changed; select the target again");
        }
    }
}
