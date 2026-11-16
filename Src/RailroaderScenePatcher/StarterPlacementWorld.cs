// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The starter placement pattern: a scene object whose Generic track marker an active new-game setup's car placement
    // holds. CompanyModeSetup.Setup reads the marker's Location for each placement when a new company game starts and
    // calls TrainController.PlaceTrain there, whose HandlePlaceTrain checks the whole cut with CanPlaceAt and throws
    // when it fails; the setup catches that around all its placements and stops. This adapter writes the marker location
    // with its registration, as the Generic-marker adapter does, and walks the track for the placement rule with the
    // graph's own LocationByMoving, the segments' Available flag and the cars' lengths from ConsistLength.
    internal sealed class StarterPlacementWorld : TrackMarkerWorld, IStarterPlacementWorld {
        const string GraphType = "Track.Graph", SegmentType = "Track.TrackSegment", LocationType = "Track.Location";
        readonly Type graph, location, end, endOfTrack, switchAgainst, stateManager, consistLength;
        readonly MethodInfo moving, position, descriptors, lengthOf, getSegment;
        readonly PropertyInfo available;
        internal StarterPlacementWorld() {
            graph = Native.Type(GraphType); location = Native.Type(LocationType);
            var segment = Native.Type(SegmentType);
            if (graph == null || location == null || segment == null) throw new InvalidOperationException("Starter placement prerequisites unavailable");
            end = segment.GetNestedType("End", Native.Flags) ?? throw new MissingMemberException(SegmentType, "End");
            available = segment.GetProperty("Available", Native.Flags) ?? throw new MissingMemberException(SegmentType, "Available");
            if (graph.GetProperty("Shared", Native.Flags) == null && graph.GetField("Shared", Native.Flags) == null) throw new MissingMemberException(GraphType, "Shared");
            getSegment = graph.GetMethod("GetSegment", Native.Flags, null, new[] { typeof(string) }, null) ?? throw new MissingMethodException(GraphType, "GetSegment(string)");
            if (segment.GetField("id", Native.Flags) == null) throw new MissingFieldException(SegmentType, "id");
            foreach (var field in new[] { "segment", "distance", "end" }) if (location.GetField(field, Native.Flags) == null) throw new MissingFieldException(LocationType, field);
            if (location.GetConstructor(Native.Flags, null, new[] { segment, typeof(float), end }, null) == null) throw new MissingMethodException(LocationType, ".ctor");
            moving = graph.GetMethod("LocationByMoving", Native.Flags, null, new[] { location, typeof(float), typeof(bool), typeof(bool) }, null) ?? throw new MissingMethodException(GraphType, "LocationByMoving");
            position = graph.GetMethod("GetPosition", Native.Flags, null, new[] { location }, null) ?? throw new MissingMethodException(GraphType, "GetPosition");
            endOfTrack = Native.Type("Track.EndOfTrack") ?? throw new InvalidOperationException("Track.EndOfTrack unavailable");
            switchAgainst = Native.Type("Track.SwitchAgainstMovement") ?? throw new InvalidOperationException("Track.SwitchAgainstMovement unavailable");
            stateManager = Native.Type("Game.State.StateManager") ?? throw new InvalidOperationException("Game.State.StateManager unavailable");
            descriptors = stateManager.GetMethods(Native.Flags).SingleOrDefault(m => m.Name == "DescriptorsForIdentifiers" && m.IsStatic && m.GetParameters().Length == 1) ?? throw new MissingMethodException("Game.State.StateManager", "DescriptorsForIdentifiers");
            consistLength = Native.Type("Model.ConsistLength") ?? throw new InvalidOperationException("Model.ConsistLength unavailable");
            lengthOf = consistLength.GetMethods(Native.Flags).SingleOrDefault(m => m.Name == "OfDescriptors" && m.IsStatic && m.GetParameters().Length == 1) ?? throw new MissingMethodException("Model.ConsistLength", "OfDescriptors");
            var setup = Native.Type("Game.Progression.SetupDescriptor") ?? throw new InvalidOperationException("Game.Progression.SetupDescriptor unavailable");
            var placement = setup.GetNestedType("CarPlacement", Native.Flags) ?? throw new MissingMemberException("Game.Progression.SetupDescriptor", "CarPlacement");
            foreach (var field in new[] { "carIdentifier", "marker", "wreck" }) if (placement.GetField(field, Native.Flags) == null) throw new MissingFieldException(placement.FullName, field);
            if (setup.GetField("identifier", Native.Flags) == null) throw new MissingFieldException(setup.FullName, "identifier");
        }
        static object Graph() => Native.Read(Native.Type(GraphType), "Shared") ?? throw new InvalidOperationException("The track graph is unavailable");
        // The setups of the loaded scenes, read once per frame: every check of a frame sees the same placements.
        StarterSetups setups; int setupsFrame = -1;
        public StarterSetups Setups() {
            if (setupsFrame == Time.frameCount && setups != null) return setups;
            try { setups = StarterSetups(); }
            catch (Exception e) { setups = Core.StarterSetups.Unreadable(e.GetBaseException().Message); }
            setupsFrame = Time.frameCount;
            return setups;
        }
        // The starter placement capability owns a marker that an active setup's car placement holds; the Generic-marker
        // capability refuses it for that reason. Crossing pads stay with their own capability.
        protected override bool Belongs(GameObject g) {
            if (Get(g, PadBuilder) != null) return false;
            var m = Get(g, MarkerType);
            return m != null && Setups().Use(m) == StarterUse.Used;
        }
        public object Marker(object target) => MarkerOf(target);
        public TrackLocation PlacementLocation(object marker) => marker is Component c && c != null ? Location(c.gameObject) : null;
        // The marker's own shape and consumers: a Generic marker with an ID and no other consumer of its location than the
        // setups. Its object's activity does not matter to the setup, which reads the location either way.
        public override string PatternProblem(object target) {
            var go = target as GameObject;
            if (!Loaded(go)) return "This object has no implemented starter placement capability";
            var markers = go.GetComponents<Component>().Where(c => c != null && c.GetType().FullName == MarkerType).ToArray();
            var m = markers.Length == 1 ? markers[0] : null;
            var refusal = MarkerPattern.Refusal(new MarkerShape(markers.Length, go.GetComponents<Component>().Any(c => c == null), Get(go, Milepost) != null, Get(go, PadBuilder) != null,
                m == null ? null : Convert.ToString(Field(m, "type"))));
            if (refusal != null) return refusal;
            if (String.IsNullOrWhiteSpace(Field(m, "id") as string)) return "The marker has no ID; its graph registration cannot be identified";
            if (Field(m, "OnLocationChanged") is Delegate listeners && listeners.GetInvocationList().Length != 0) return "Another component consumes this marker's location changes; its dependent state is not supported";
            var relative = Get(go, Relative);
            if (relative != null && Field(relative, "targetActiveFollowsTrackGroup") is bool follows && follows) return "The object's activity follows a track group; a starter placement marker with group-driven activity is not supported";
            if (Setups().Use(m) != StarterUse.Used) return StarterPlacements.NoReader;
            return CurrentReferrers().Refusal(m, (owner, field) => owner is Component c && c.gameObject == go && c.GetType().FullName == Relative && field == "_marker", o => o is Component c && c != null ? c.GetType().FullName + " on " + BoardScene.PathOf(c.transform) : Convert.ToString(o));
        }
        // The cut the game builds: StateManager.DescriptorsForIdentifiers adds each steam locomotive's tender, and
        // ConsistLength.OfDescriptors adds the drawbar gaps.
        public double CutLength(IReadOnlyList<string> cars) {
            object built;
            try { built = descriptors.Invoke(null, new object[] { cars.ToArray() }); }
            catch (TargetInvocationException e) { throw new InvalidOperationException(e.GetBaseException().Message); }
            try { return (float)lengthOf.Invoke(null, new[] { built }); }
            catch (TargetInvocationException e) { throw new InvalidOperationException(e.GetBaseException().Message); }
        }
        // Every native step of the track walk: a failure inside the game is unwrapped and becomes a refusal with its reason,
        // the game's own for the end of the track and a switch lined against the movement.
        T Walk<T>(Func<T> step) {
            try { return step(); }
            catch (StarterTrackException) { throw; }
            catch (Exception e) {
                var cause = e is TargetInvocationException ? e.GetBaseException() : e;
                if (endOfTrack.IsInstanceOfType(cause)) throw new StarterTrackException("Not enough track.");
                if (switchAgainst.IsInstanceOfType(cause)) throw new StarterTrackException("Switch lined against.");
                throw new StarterTrackException("The track could not be walked: " + cause.Message);
            }
        }
        object Segment(string id) => Walk(() => getSegment.Invoke(Graph(), new object[] { id }));
        object NativeLocation(TrackLocation at) {
            var segment = Segment(at.Segment) ?? throw new StarterTrackException("No track there.");
            return Walk(() => Activator.CreateInstance(location, segment, (float)at.Distance, Enum.Parse(end, at.End == "a" ? "A" : "B")));
        }
        TrackLocation Of(object native) => Walk(() => {
            var segment = Field(native, "segment") ?? throw new StarterTrackException("No track there.");
            return new TrackLocation((string)Field(segment, "id"), Convert.ToString(Field(native, "end")) == "A" ? "a" : "b", (float)Field(native, "distance"));
        });
        public TrackLocation Move(TrackLocation from, double distance) {
            var start = NativeLocation(from);
            return Of(Walk(() => moving.Invoke(Graph(), new object[] { start, (float)distance, true, false })));
        }
        // A new company game is no sandbox, so the game refuses unavailable track there.
        public string Unplaceable(TrackLocation at) {
            var segment = Segment(at.Segment);
            if (segment == null) return "No track there.";
            return Walk(() => (bool)available.GetValue(segment)) ? null : "Track not available.";
        }
        public double[] Position(TrackLocation at) {
            var native = NativeLocation(at);
            var p = Walk(() => (Vector3)position.Invoke(Graph(), new[] { native }));
            if (Single.IsNaN(p.x + p.y + p.z) || Single.IsInfinity(p.x + p.y + p.z)) throw new StarterTrackException("The track has no finite position at " + at.Display + ".");
            return new double[] { p.x, p.y, p.z };
        }
    }
}
