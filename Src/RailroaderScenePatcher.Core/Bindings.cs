// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // A position on one track segment: the segment ID, the end the distance is measured from ("a" or "b") and the distance in metres.
    public sealed class TrackLocation {
        public TrackLocation(string segment, string end, double distance) { Segment = segment; End = end; Distance = distance; }
        public string Segment { get; }
        public string End { get; }
        public double Distance { get; }
        // Native locations store the distance in single precision.
        internal const double DistanceTolerance = 0.001;
        internal static bool Same(TrackLocation a, TrackLocation b) =>
            a == null ? b == null : b != null && a.Segment == b.Segment && a.End == b.End && Math.Abs(a.Distance - b.Distance) <= DistanceTolerance;
        public override string ToString() => Segment + "|" + End + "|" + Distance.ToString("R", CultureInfo.InvariantCulture);
    }
    // The desired or observed relationship of a track marker: a location, or detached from the track.
    public sealed class MarkerBinding {
        public static readonly MarkerBinding Detached = new MarkerBinding(null);
        MarkerBinding(TrackLocation location) { Location = location; }
        public static MarkerBinding At(TrackLocation location) => new MarkerBinding(location ?? throw new ArgumentNullException(nameof(location)));
        internal static MarkerBinding Of(TrackLocation location) => location == null ? Detached : At(location);
        // Null when the marker is detached.
        public TrackLocation Location { get; }
        public bool IsDetached => Location == null;
        internal static bool Same(MarkerBinding a, MarkerBinding b) => a == null ? b == null : b != null && TrackLocation.Same(a.Location, b.Location);
        public override string ToString() => IsDetached ? "detached" : Location.ToString();
    }
    // One entry of the track graph's named-location registration for a marker's ID.
    public sealed class MarkerRegistration {
        public MarkerRegistration(string bucket, TrackLocation location, bool holdsMarker) { Bucket = bucket; Location = location; HoldsMarker = holdsMarker; }
        // The segment ID under which the graph files the entry.
        public string Bucket { get; }
        public TrackLocation Location { get; }
        // The entry refers to this marker rather than another object registered under the same ID.
        public bool HoldsMarker { get; }
    }
    // One field of a scanned owner. Relevant fields can hold a target, directly or as a list element; Read may throw.
    public sealed class ReferenceField {
        public ReferenceField(string name, bool relevant, Func<object> read) { Name = name; Relevant = relevant; Read = read ?? throw new ArgumentNullException(nameof(read)); }
        public string Name { get; }
        public bool Relevant { get; }
        public Func<object> Read { get; }
    }
    // The direct references from scanned owners to targets. A relevant field that cannot be read, or a list that cannot be
    // enumerated, makes the scan incomplete: an unread field may hold any target, so every target is then refused.
    public sealed class ReferenceScan {
        readonly Dictionary<object, List<KeyValuePair<object, string>>> holders = new Dictionary<object, List<KeyValuePair<object, string>>>(new ByReference());
        readonly List<string> unreadable = new List<string>();
        sealed class ByReference : IEqualityComparer<object> {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        ReferenceScan() { }
        // With an observer, the scan reads every field once and reports each read, value or failure, to the observer in owner
        // and field order, so another consumer of the same fields needs no second pass. The observer's own failures are its
        // own. Without an observer, fields that cannot hold a target are not read. A failed read of such a field never makes
        // the scan incomplete.
        public static ReferenceScan Of(IEnumerable<object> owners, Func<object, IEnumerable<ReferenceField>> fields, Func<object, bool> isTarget, Func<object, string> label,
            Action<object, ReferenceField, object, Exception> observe = null) {
            var walk = Begin(fields, isTarget, label, observe);
            foreach (var owner in owners) walk.Add(owner);
            return walk.Finish();
        }
        // The same scan taken one owner at a time, so its caller can pause between owners. Each owner's fields are read
        // when the owner is added. The scan exists once the walk is finished, so an unfinished walk answers no question.
        public static Walk Begin(Func<object, IEnumerable<ReferenceField>> fields, Func<object, bool> isTarget, Func<object, string> label,
            Action<object, ReferenceField, object, Exception> observe = null) => new Walk(fields, isTarget, label, observe);
        public sealed class Walk {
            readonly Func<object, IEnumerable<ReferenceField>> fields;
            readonly Func<object, bool> isTarget;
            readonly Func<object, string> label;
            readonly Action<object, ReferenceField, object, Exception> observe;
            ReferenceScan scan = new ReferenceScan();
            internal Walk(Func<object, IEnumerable<ReferenceField>> fields, Func<object, bool> isTarget, Func<object, string> label, Action<object, ReferenceField, object, Exception> observe) {
                this.fields = fields; this.isTarget = isTarget; this.label = label; this.observe = observe;
            }
            public void Add(object owner) {
                if (scan == null) throw new InvalidOperationException("The reference walk is finished");
                IEnumerable<ReferenceField> ownerFields;
                try { ownerFields = fields(owner).ToList(); }
                catch (Exception e) { scan.unreadable.Add(label(owner) + ": fields unavailable: " + e.GetBaseException().Message); return; }
                foreach (var field in ownerFields) {
                    if (!field.Relevant && observe == null) continue;
                    object value;
                    try { value = field.Read(); }
                    catch (Exception e) {
                        observe?.Invoke(owner, field, null, e);
                        if (field.Relevant) scan.unreadable.Add(label(owner) + "." + field.Name + ": " + e.GetBaseException().Message);
                        continue;
                    }
                    observe?.Invoke(owner, field, value, null);
                    if (!field.Relevant) continue;
                    try {
                        var items = value != null && IsList(value.GetType()) ? ((System.Collections.IEnumerable)value).Cast<object>().ToList() : new List<object> { value };
                        foreach (var item in items)
                            if (item != null && isTarget(item)) {
                                if (!scan.holders.TryGetValue(item, out var refs)) scan.holders.Add(item, refs = new List<KeyValuePair<object, string>>());
                                refs.Add(new KeyValuePair<object, string>(owner, field.Name));
                            }
                    } catch (Exception e) { scan.unreadable.Add(label(owner) + "." + field.Name + ": " + e.GetBaseException().Message); }
                }
            }
            public ReferenceScan Finish() {
                var finished = scan ?? throw new InvalidOperationException("The reference walk is finished");
                scan = null;
                return finished;
            }
        }
        // A list value or declaration: the non-generic IList, or the generic IList<T> on its own.
        static bool IsList(Type type) => typeof(System.Collections.IList).IsAssignableFrom(type) || ListElement(type) != null;
        static Type ListElement(Type type) => type.IsArray ? type.GetElementType()
            : new[] { type }.Concat(type.GetInterfaces()).Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>)).Select(i => i.GetGenericArguments()[0]).FirstOrDefault();
        // Whether a field declared with this type can hold a target of the given type, directly or as a list element.
        public static bool CanHold(Type fieldType, Type targetType) {
            if (fieldType.IsValueType || fieldType == typeof(string)) return false;
            if (targetType.IsAssignableFrom(fieldType) || fieldType.IsAssignableFrom(targetType)) return true;
            if (!IsList(fieldType)) return false;
            var element = ListElement(fieldType);
            return element == null || CanHold(element, targetType);
        }
        public bool Complete => unreadable.Count == 0;
        public IReadOnlyList<string> Unreadable => unreadable;
        public IReadOnlyList<KeyValuePair<object, string>> HoldersOf(object target) =>
            holders.TryGetValue(target, out var refs) ? (IReadOnlyList<KeyValuePair<object, string>>)refs : Array.Empty<KeyValuePair<object, string>>();
        // Why the target cannot be edited because of its holders, or null. An incomplete scan refuses every target.
        public string Refusal(object target, Func<object, string, bool> allowed, Func<object, string> label) {
            if (!Complete) return "The direct-reference scan could not read " + unreadable.Count + " field" + (unreadable.Count == 1 ? "" : "s") + ", first " + unreadable[0] + "; other holders of this marker are unknown, so the binding capability refuses it";
            var holder = HoldersOf(target).FirstOrDefault(h => !allowed(h.Key, h.Value));
            return holder.Key == null ? null : label(holder.Key) + "." + holder.Value + " holds this marker; that consumer is not supported";
        }
    }
    // The objects one ordered walk of placements holds, read once at the first question. A placement found before the
    // walk's first read failure holds its object; any other object receives that failure, as a walk repeated per object
    // that stops at its first match or failure would. Reading stops at the first failure. One index serves one discovery.
    public sealed class HolderIndex {
        readonly Func<IEnumerable<object>> walk;
        HashSet<object> held;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo failure;
        sealed class ByReference : IEqualityComparer<object> {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        HolderIndex(Func<IEnumerable<object>> walk) { this.walk = walk ?? throw new ArgumentNullException(nameof(walk)); }
        public static HolderIndex Lazy(Func<IEnumerable<object>> walk) => new HolderIndex(walk);
        public bool Holds(object target) {
            if (held == null) {
                held = new HashSet<object>(new ByReference());
                try { foreach (var value in walk()) if (value != null) held.Add(value); }
                catch (Exception e) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e); }
            }
            if (target != null && held.Contains(target)) return true;
            failure?.Throw();
            return false;
        }
    }
    // A scene whose objects can hold explicit relationships. Check runs before any mutation of the operation.
    public interface IRelationshipScene {
        // Throws when the state holds a relationship the object does not support, a relationship target that does not
        // resolve uniquely, or a setting the object's relationships make unsupported, such as an explicit enable of an
        // object whose activity follows a track group.
        void CheckRelationships(object target, ObjectState state);
    }
    // The group-following activity of a marker object: its TrackRelativePosition subscribes to the track graph's group
    // event for the bound segment's group and activates or deactivates the object with that group. The explicit-disable
    // hold replaces the native activation step and keeps the object inactive.
    public sealed class GroupActivity {
        public GroupActivity(bool held, bool observing, string observedGroup, int subscriptions, bool locationResolves, string locationGroup, bool groupEnabled) {
            Held = held; Observing = observing; ObservedGroup = observedGroup; Subscriptions = subscriptions;
            LocationResolves = locationResolves; LocationGroup = locationGroup; GroupEnabled = groupEnabled;
        }
        public bool Held { get; }
        public bool Observing { get; }
        public string ObservedGroup { get; }
        // The relative position's handlers on the graph's group event.
        public int Subscriptions { get; }
        // Whether the marker's current location resolves to a loaded segment; the native observation needs one.
        public bool LocationResolves { get; }
        // The group of the current location's segment; null or empty when it has none.
        public string LocationGroup { get; }
        // Whether the graph currently enables that group; true for a segment without a group.
        public bool GroupEnabled { get; }
    }
    // The game-facing operations of the track-marker capability. Positions are map coordinates.
    public interface ITrackMarkerWorld {
        IEnumerable<string> MapIds { get; }
        IReadOnlyList<object> Resolve(TargetIdentity identity);
        // Why the object does not conform to the supported Generic-marker pattern, or null.
        string PatternProblem(object marker);
        // The serialized location, or null when it names no segment.
        TrackLocation Location(object marker);
        // The lengths of every loaded segment with this ID; one entry for a unique segment.
        IReadOnlyList<double> SegmentLengths(string segment);
        // Every graph entry registered under the marker's ID.
        IReadOnlyList<MarkerRegistration> Registrations(object marker);
        // Whether the native marker lifecycle keeps the marker registered: the component is enabled and active.
        bool Registers(object marker);
        // Writes the serialized location and the native dependent caches; does not change the graph registration.
        void SetLocation(object marker, TrackLocation location);
        // Refreshes consumer state that caches the marker's location, such as the track group a map label is dimmed by.
        // A later group-subscription capability extends this step and its check.
        void RefreshDependents(object marker);
        // Why consumer state that caches the location disagrees with the current location, or null.
        string DependentProblem(object marker);
        // The native disable and enable registration steps for the marker's ID.
        void Unregister(object marker);
        void Register(object marker);
        double[] Position(object marker);
        double[] Rotation(object marker);
        bool Active(object marker);
        // Sets the root pose and refreshes dependent culling.
        void SetPose(object marker, double[] position, double[] rotation);
        // Activates or deactivates the root; the native marker lifecycle then registers or removes the marker.
        void SetActive(object marker, bool active);
        // The object's group-following activity, or null when its activity does not follow a track group.
        GroupActivity GroupActivity(object marker);
        // The native unsubscribe and observe steps for the current location's group. The observation sets the object's
        // activity from the group, through the hold when it is installed.
        void ObserveGroup(object marker);
        // Installs or removes the explicit-disable hold in place of the native activation step.
        void HoldDisabled(object marker, bool hold);
    }
    // Repairs, replaces and detaches the location relationship of a Generic track marker together with its graph
    // registration, and places or enables the marker object. A relationship edit never moves the object:
    // placement stays a separate, independently authored setting.
    public sealed class TrackMarkerScene : IScene, IRelationshipScene, IVersionedScene {
        public const string Structure = "track-marker-v1", RequiredVersion = "0.4.0";
        const double PositionTolerance = 0.01, AngleToleranceDegrees = 0.01;
        sealed class Snapshot { internal TrackLocation Location; internal double[] Position, Rotation; internal bool Active, Held; }
        readonly ITrackMarkerWorld world;
        public TrackMarkerScene(ITrackMarkerWorld world, Capability capability) { this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Structure != null && identity.Structure != Structure) return Array.Empty<object>();
            return world.Resolve(identity) ?? Array.Empty<object>();
        }
        public string MinimumPatcherVersion(TargetIdentity target) => target.Structure == Structure ? RequiredVersion : null;
        public void Preflight(object target) {
            var problem = world.PatternProblem(target);
            if (problem != null) throw new InvalidOperationException(problem);
            var difference = Coherence(target);
            if (difference != null) throw new InvalidOperationException("The marker's graph registration does not match its location and activity: " + difference + ". Reload before editing it");
        }
        public void CheckRelationships(object target, ObjectState state) {
            var group = world.GroupActivity(target);
            if (group != null && state?.Active == true)
                throw new InvalidOperationException("The object's activity follows track group " + (String.IsNullOrEmpty(group.LocationGroup) ? "of its bound segment" : group.LocationGroup) + "; only an explicit disable is supported. Omit active to keep group-driven activity");
            var location = state?.Marker?.Location;
            if (location == null) return;
            var problem = Unresolved(location);
            if (problem != null) throw new InvalidOperationException(problem);
        }
        // Why a location cannot be bound, or null: its segment must exist exactly once and contain the distance.
        string Unresolved(TrackLocation location) {
            if (location.End != "a" && location.End != "b") return "Relationship end must be \"a\" or \"b\"";
            var lengths = world.SegmentLengths(location.Segment) ?? Array.Empty<double>();
            if (lengths.Count == 0) return "Relationship target segment " + location.Segment + " is missing";
            if (lengths.Count > 1) return "Relationship target segment " + location.Segment + " is ambiguous";
            if (Double.IsNaN(location.Distance) || location.Distance < 0 || location.Distance > lengths[0] + TrackLocation.DistanceTolerance)
                return "Relationship distance " + location.Distance.ToString("R", CultureInfo.InvariantCulture) + " lies outside segment " + location.Segment;
            return null;
        }
        public object Capture(object target) => new Snapshot { Location = world.Location(target), Position = world.Position(target), Rotation = world.Rotation(target), Active = world.Active(target), Held = world.GroupActivity(target)?.Held == true };
        // The activity of a group follower is group-driven (null) unless the explicit-disable hold is installed.
        public ObjectState Read(object target) => new ObjectState(new Placement(world.Position(target), world.Rotation(target)), ReadActive(target), MarkerBinding.Of(world.Location(target)));
        bool? ReadActive(object target) {
            var group = world.GroupActivity(target);
            return group == null || group.Held ? world.Active(target) : (bool?)null;
        }
        public void Apply(object target, ObjectState state) {
            var before = world.Location(target);
            var location = state.Marker == null ? before : state.Marker.Location;
            var follower = world.GroupActivity(target) != null;
            if (follower && state.Active == true) CheckRelationships(target, state);
            // A follower without an explicit disable returns to group-driven activity.
            var active = follower ? (state.Active == false ? false : (bool?)null) : state.Active ?? world.Active(target);
            Write(target, location, state.Placement?.Position, state.Placement?.Rotation, active, !TrackLocation.Same(before, location));
            var difference = Verify(target, location, state.Placement, active);
            if (difference != null) throw new InvalidOperationException("Applied marker state did not verify: " + difference);
        }
        public bool Restore(object target, object snapshot) {
            var captured = (Snapshot)snapshot;
            try {
                var active = world.GroupActivity(target) == null ? captured.Active : (captured.Held ? false : (bool?)null);
                Write(target, captured.Location, captured.Position, captured.Rotation, active, true);
                return Verify(target, captured.Location, new Placement(captured.Position, captured.Rotation), active) == null;
            } catch { return false; }
        }
        // The relationship first, so the registration follows the new location; activity then lets the native lifecycle register or remove it.
        // A group follower's hold is installed before anything can activate the object and released only after the
        // relationship; its subscription is observed again for the current location; without the hold the group sets activity.
        void Write(object target, TrackLocation location, double[] position, double[] rotation, bool? active, bool rebind) {
            var group = world.GroupActivity(target);
            if (group != null && active == false) {
                if (!group.Held) world.HoldDisabled(target, true);
                if (world.Active(target)) world.SetActive(target, false);
            }
            if (rebind || Coherence(target) != null || world.DependentProblem(target) != null) {
                world.Unregister(target);
                world.SetLocation(target, location);
                if (world.Registers(target) && location != null && Unresolved(location) == null) world.Register(target);
                world.RefreshDependents(target);
            }
            if (group != null) {
                var release = active != false && world.GroupActivity(target).Held;
                if (release) world.HoldDisabled(target, false);
                if (rebind || release || GroupProblem(target) != null) world.ObserveGroup(target);
            } else if (active == true && !world.Active(target)) world.SetActive(target, true);
            if (position != null) world.SetPose(target, position, rotation);
            if (group == null && active == false && world.Active(target)) world.SetActive(target, false);
        }
        string Verify(object target, TrackLocation location, Placement placement, bool? active) {
            if (!TrackLocation.Same(world.Location(target), location)) return "the serialized location is " + (world.Location(target)?.ToString() ?? "detached") + ", expected " + (location?.ToString() ?? "detached");
            var group = world.GroupActivity(target);
            if (group == null ? world.Active(target) != active : group.Held != (active == false)) return "activity differs";
            if (placement != null && (!Near(world.Position(target), placement.Position) || !SameRotation(world.Rotation(target), placement.Rotation))) return "pose differs";
            return Coherence(target) ?? world.DependentProblem(target) ?? GroupProblem(target);
        }
        // The subscription and activity the native observation holds for the current location: one handler for the
        // location's group, none without a group or a resolvable location; activity from the group, or inactive under the
        // hold. Null when it agrees or the object does not follow a group.
        string GroupProblem(object target) {
            var group = world.GroupActivity(target);
            if (group == null) return null;
            if (!group.LocationResolves) return group.Observing || group.Subscriptions != 0 ? "the track-group subscription remains for a location that does not resolve" : null;
            var expected = String.IsNullOrEmpty(group.LocationGroup) ? 0 : 1;
            if (!group.Observing || (group.ObservedGroup ?? "") != (group.LocationGroup ?? "") || group.Subscriptions != expected)
                return "the object observes track group " + (group.Observing ? group.ObservedGroup ?? "none" : "none") + " with " + group.Subscriptions + " subscriptions instead of " + (group.LocationGroup ?? "none") + " with " + expected;
            if (group.Held ? world.Active(target) : world.Active(target) != group.GroupEnabled)
                return group.Held ? "the explicitly disabled object is active" : "the object's activity differs from its track group " + group.LocationGroup;
            return null;
        }
        // The registration the native lifecycle holds for the current location and activity: one owned entry
        // at a resolvable location of an active marker, none otherwise. Null when it agrees.
        string Coherence(object target) {
            var location = world.Location(target);
            var entries = world.Registrations(target) ?? Array.Empty<MarkerRegistration>();
            var expected = world.Registers(target) && location != null && Unresolved(location) == null;
            if (!expected) return entries.Count == 0 ? null : entries.Count + " graph registrations remain for an unregistered marker";
            if (entries.Count != 1) return entries.Count + " graph registrations instead of one";
            var entry = entries[0];
            if (!entry.HoldsMarker) return "the graph registration under this ID belongs to another object";
            if (entry.Bucket != location.Segment || !TrackLocation.Same(entry.Location, location)) return "the graph registers the marker at " + entry.Location + " instead of " + location;
            return null;
        }
        static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++) if (Math.Abs(a[i] - b[i]) > PositionTolerance) return false;
            return true;
        }
        static bool SameRotation(IReadOnlyList<double> a, IReadOnlyList<double> b) {
            if (a == null || b == null || a.Count != 4 || b.Count != 4) return false;
            double dot = 0, la = 0, lb = 0;
            for (var i = 0; i < 4; i++) { dot += a[i] * b[i]; la += a[i] * a[i]; lb += b[i] * b[i]; }
            if (la <= 0 || lb <= 0) return false;
            return 2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(la * lb))) * 180 / Math.PI <= AngleToleranceDegrees;
        }
    }
}
