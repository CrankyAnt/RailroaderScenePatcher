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
        // The one form a track location is shown in: segment, end in lower case as the scene patch file writes it, and the
        // distance to three decimals.
        public string Display => Segment + " / " + (End ?? "").ToLowerInvariant() + " / " + ShownNumber.Text(Distance);
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
        public MarkerRegistration(string bucket, TrackLocation location, bool holdsMarker, object owner = null, string ownerName = null) {
            Bucket = bucket; Location = location; HoldsMarker = holdsMarker; Owner = owner; OwnerName = ownerName;
        }
        // The segment ID under which the graph files the entry.
        public string Bucket { get; }
        public TrackLocation Location { get; }
        // The entry refers to this marker rather than another object registered under the same ID.
        public bool HoldsMarker { get; }
        // The scene marker that holds the entry, as the world's handle of that object, wherever its scene is; null when
        // the entry belongs to an object that is not a scene marker.
        public object Owner { get; }
        // The name of that scene marker, by scene and path; null when it is unknown.
        public string OwnerName { get; }
    }
    // One scene marker that holds a marker ID: the world's handle of the object, its name and whether the native marker
    // lifecycle keeps it registered.
    public sealed class MarkerIdHolder {
        public MarkerIdHolder(object handle, string name, bool registers) { Handle = handle ?? throw new ArgumentNullException(nameof(handle)); Name = name; Registers = registers; }
        public object Handle { get; }
        public string Name { get; }
        public bool Registers { get; }
    }
    // The scene markers that hold one marker ID. The game removes a marker's graph registration by marker ID from every
    // holder, so while the marker ID is a duplicate marker ID, an edit of one holder's activity, binding or
    // track-following placement would remove the registration of the others. Such an edit is refused until the
    // duplicate marker ID is repaired.
    public sealed class MarkerIdHolders {
        readonly MarkerRegistration[] registrations;
        public MarkerIdHolders(string id, IEnumerable<MarkerIdHolder> holders) : this(id, holders, null) { }
        MarkerIdHolders(string id, IEnumerable<MarkerIdHolder> holders, MarkerRegistration[] registrations) {
            Id = id;
            Holders = Array.AsReadOnly((holders ?? Enumerable.Empty<MarkerIdHolder>()).Where(h => h != null).GroupBy(h => h.Handle).Select(g => g.First())
                .OrderBy(h => h.Name ?? "", StringComparer.Ordinal).ToArray());
            this.registrations = registrations ?? new MarkerRegistration[0];
        }
        // The one holder set of the guard and the inspector: these holders, completed by the owner of every registration
        // under the marker ID that is not among them, such as a scene marker in a scene that is not loaded. Such an owner
        // holds a registration, so it counts as registered.
        public MarkerIdHolders With(IEnumerable<MarkerRegistration> entries) {
            var all = (entries ?? Enumerable.Empty<MarkerRegistration>()).Where(e => e != null).ToArray();
            var owners = all.Where(e => e.Owner != null && !Holders.Any(h => ReferenceEquals(h.Handle, e.Owner)))
                .Select(e => new MarkerIdHolder(e.Owner, e.OwnerName ?? "a scene marker that is not loaded", true));
            return new MarkerIdHolders(Id, Holders.Concat(owners), all);
        }
        public string Id { get; }
        // Every scene marker that holds the marker ID, by name.
        public IReadOnlyList<MarkerIdHolder> Holders { get; }
        public bool Duplicate => Holders.Count > 1;
        // The holders as a refusal or the inspector names them: the inspected marker first, then the active holders,
        // whose registration is at stake, and the inactive ones last; by name within each.
        public IReadOnlyList<MarkerIdHolder> Ordered(object self) =>
            Holders.OrderBy(h => ReferenceEquals(h.Handle, self) ? 0 : h.Registers ? 1 : 2).ToArray();
        static string Activity(MarkerIdHolder holder) => holder.Registers ? "active" : "inactive";
        string Names(object self) {
            return Prose.Join(Ordered(self).Select(h => h.Name + " (" + Activity(h) + ")"));
        }
        // The refusal of an edit that changes the activity, binding or track-following placement of a holder: plain
        // sentences that follow the label of the action they refuse.
        public string Refusal(object self) => "Duplicate marker ID " + Id + " is held by " + Holders.Count + " scene markers, " + Names(self) + ". " + Reason;
        const string Reason = "When the game removes a marker's registration by marker ID, it removes the registration of every scene marker with that ID. You can't edit their activity, binding, or track-following placement until the duplicate marker ID is repaired. Reloading or choosing another track doesn't change the duplicate marker ID.";
        // The problem of an active holder that can receive a replacement ID: a duplicate marker ID is then a defect the
        // modder repairs with the patcher. An inactive holder, and a holder that cannot receive a replacement ID, such as a
        // crossing pad, show the explanation without a problem. Null for a marker ID that one scene marker holds.
        public string Problem(object self, bool repairable) {
            var holder = Holders.FirstOrDefault(h => ReferenceEquals(h.Handle, self));
            if (!repairable || !Duplicate || holder == null || !holder.Registers) return null;
            var others = Holders.Count - 1;
            return "Duplicate marker ID " + Id + " is shared with " + (others == 1 ? "1 other scene marker" : others + " other scene markers") + ". " + ReplacementMarkerIds.Offer;
        }
        // The state of a holder that does not match its location and activity, such as its registration or a dependent
        // cache, which only a rewrite of its registration would correct. Loading the scene again registers the holders
        // under the same marker ID, so it gives no advice to reload.
        public string Mismatch(object self, string difference) => "The marker's state does not match its location and activity (" + difference + "). " + Refusal(self);
        // The dependency lines of the inspector for one holder, as plain sentences: the duplicate marker ID first, then
        // every holder with its activity and the registrations it owns under the marker ID, and the registrations that
        // no scene marker owns. Empty for a marker ID that one scene marker holds. offer adds the repair to the first line
        // for a holder that can receive a replacement ID.
        public IReadOnlyList<string> Dependencies(object self, bool offer = false) {
            if (!Duplicate) return Array.Empty<string>();
            var lines = new List<string> { "Duplicate marker ID " + Id + " is held by " + Holders.Count + " scene markers. " + Reason + (offer ? " " + ReplacementMarkerIds.Offer : "") };
            foreach (var holder in Ordered(self)) {
                var own = registrations.Where(e => ReferenceEquals(e.Owner, holder.Handle)).Select(e => e.Location?.Display ?? e.Bucket).ToArray();
                lines.Add((ReferenceEquals(holder.Handle, self) ? "This marker" : holder.Name) + " is " + Activity(holder) + " and "
                    + (own.Length == 0 ? "has no registration under " + Id + "." : "has the registration under " + Id + " at " + String.Join(" and ", own) + "."));
            }
            foreach (var entry in registrations.Where(e => e.Owner == null))
                lines.Add("An object that is not a scene marker has the registration under " + Id + " at " + (entry.Location?.Display ?? entry.Bucket) + ".");
            return lines;
        }
    }
    // One field of a scanned owner. Relevant fields can hold a target, directly or as a list element; Read may throw.
    public sealed class ReferenceField {
        public ReferenceField(string name, bool relevant, Func<object> read, bool authored = true) { Authored = authored; Name = name; Relevant = relevant; Read = read ?? throw new ArgumentNullException(nameof(read)); }
        public string Name { get; }
        public bool Relevant { get; }
        public Func<object> Read { get; }
        // Whether Unity serializes the field, so that a reference in it is authored scene state. A reference in any other
        // field is working state that changes while the game runs.
        public bool Authored { get; }
    }
    // The direct references from scanned owners to targets. A relevant field that cannot be read, or a list that cannot be
    // enumerated, makes the scan incomplete: an unread field may hold any target, so every target is then refused.
    public sealed class ReferenceScan {
        // Unity serializes an instance field that is public or marked SerializeField, and never a static, read-only or
        // NonSerialized one.
        public static bool IsSerialized(System.Reflection.FieldInfo field) =>
            field != null && !field.IsStatic && !field.IsInitOnly && !field.IsLiteral && !field.IsNotSerialized &&
            (field.IsPublic || field.GetCustomAttributes(true).Any(attribute => attribute.GetType().FullName == "UnityEngine.SerializeField"));
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
            if (!Complete) return "The direct-reference scan could not read " + unreadable.Count + " field" + (unreadable.Count == 1 ? "" : "s") + ", first " + unreadable[0] + ". The patcher could not check which other objects use this marker, so its binding cannot be changed";
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
    // The group-driven activity of a marker object: its TrackRelativePosition subscribes to the track graph's group
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
    // IDisableRefusal: why `active: false` cannot hold against a switch of the game that names the marker, or null; the
    // relationship refuses the disable with it before changing anything.
    public interface ITrackMarkerWorld : IDisableRefusal {
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
        // Every scene marker the world can see that holds the marker's ID, the marker itself included. The relationship
        // completes it with the owners of the registrations under the ID.
        MarkerIdHolders IdHolders(object marker);
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
        // Sets the root placement and refreshes dependent culling.
        void SetPlacement(object marker, double[] position, double[] rotation);
        // Activates or deactivates the root; the native marker lifecycle then registers or removes the marker.
        void SetActive(object marker, bool active);
        // The object's group-driven activity, or null when its activity does not follow a track group.
        GroupActivity GroupActivity(object marker);
        // The native unsubscribe and observe steps for the current location's group. The observation sets the object's
        // activity from the group, through the hold when it is installed.
        void ObserveGroup(object marker);
        // Installs or removes the explicit-disable hold in place of the native activation step.
        void HoldDisabled(object marker, bool hold);
    }
    // A native world may reconcile the warning of a deliberately restored broken binding after complete verification.
    public interface ITrackMarkerDiagnostics {
        // Must leave uncertain warning rows intact and must not throw or alter the verified scene state.
        void AfterVerifiedState(object marker);
    }
    // The id of a generic track marker and the ids a replacement ID must not take.
    public interface IMarkerIdWorld {
        string MarkerId(object marker);
        // Writes the marker's id field. The registration is the caller's: remove the marker's own entries first.
        void SetMarkerId(object marker, string id);
        // Removes only the graph entries under the marker's id that this marker registered. The native removal by id
        // would remove the entries of every other holder of the id.
        void UnregisterOwn(object marker);
        // The scene markers other than except that hold the id, by name, followed by the owners of graph entries under
        // the id that no listed scene marker owns, such as a passenger stop; ReplacementMarkerIds.ForeignOwner names one
        // that is not a scene marker.
        IReadOnlyList<string> IdUsers(string id, object except);
        // The map-object ids of the map context: the game gives the track marker of a map object its map-object id. Null
        // when the game has not loaded them, which refuses every replacement ID.
        IEnumerable<string> MapObjectIds { get; }
    }
    // A marker world that tells the mod objects among its markers apart: a mod object that is a scene marker holds a
    // marker ID of its own from its placement, which Set replacement marker ID changes, whatever its family.
    public interface IModObjectMarkers {
        bool IsModObject(object marker);
    }
    // A capability whose objects take a replacement ID: the supported generic track markers, and the mileposts that are
    // mod objects.
    public interface IMarkerIdScene {
        // The scene markers and registrations other than the target that use the id, by name.
        IReadOnlyList<string> MarkerIdUsers(object target, string id);
        // Whether applying this state to the marker rewrites its registration, which a duplicate marker ID refuses:
        // a setting of its activity, binding or track following, or any setting while a dependent cache such as a map
        // label's track group disagrees with its location.
        bool RewritesRegistration(object target, ObjectState state);
        // Why the marker cannot take this replacement ID in the world as it is, or null, as the edit checks it before
        // mutation. The marker's own ID is no change and passes.
        string MarkerIdRefusal(object target, string id);
        // Why a mod object made from the source cannot hold the id as its own marker ID, or null: the id passes the
        // rules of a replacement ID, differs from the source's and no scene marker, registration or map object uses it.
        string NewMarkerIdRefusal(object source, string id);
    }
    // The rules of a replacement ID, which repairs a duplicate marker ID: one holder takes an id that is not empty,
    // does not begin with a prefix the game uses for its own registrations, and that no other scene marker, graph
    // registration or map object of the map context uses. Lua scripts and third-party mods that look a marker up by id
    // are not checked.
    public static class ReplacementMarkerIds {
        public const string Member = "markerId", RequiredVersion = "0.5.0";
        public static readonly IReadOnlyList<string> ReservedPrefixes = Array.AsReadOnly(new[] { "passengerStop:", "flare:" });
        public const string Empty = "The replacement ID is empty. " + WindowGuidance.EnterUnusedId;
        public const string NotAString = "The replacement ID must be a string.";
        public const string NotSupported = "Only objects in the Generic track marker family can have a replacement ID.";
        // The reason the Connections tab gives for an object of another family. A milepost or a starter placement marker
        // is a native track marker of type Generic too, so the reason names the families.
        public static string NotSupportedFor(string family) => NotSupported + (String.IsNullOrWhiteSpace(family) ? "" : " This object is in the " + family + " family.");
        public const string Offer = WindowGuidance.GiveReplacementId;
        public const string ForeignOwner = "an object that is not a scene marker";
        // Why the id cannot be a replacement ID whatever the world holds, or null. A space at either end would make an ID
        // that looks like another one.
        public static string Problem(string id) {
            if (String.IsNullOrWhiteSpace(id)) return Empty;
            if (id.Trim() != id) return "Replacement ID \"" + id + "\" begins or ends with a space. " + WindowGuidance.RemoveSpace;
            var prefix = ReservedPrefixes.FirstOrDefault(p => id.StartsWith(p, StringComparison.Ordinal));
            return prefix == null ? null : "Replacement ID " + id + " begins with " + prefix + ", which the game uses for its own track registrations. " + WindowGuidance.ChooseIdWithoutPrefix;
        }
        public static string UsedBy(string id, string user) => "Replacement ID " + id + " is already used by " + (user == ForeignOwner ? user : "the scene marker " + user) + ". " + WindowGuidance.ChooseIdNoMarkerUses;
        public static string UsedByMapObject(string id) => "Replacement ID " + id + " is already used by map object " + id + " in the map context. " + WindowGuidance.ChooseAnotherId;
        // The map objects of the map context could not be read, so the ID is not known to be free.
        public static string MapObjectsUnknown(string id) => "The game hasn't loaded the map objects in the map context, so replacement ID " + id + " couldn't be checked against them. " + WindowGuidance.TryAfterMapLoaded;
        // The modifications of other holders whose settings need this modification's replacement ID, by key.
        public static string DependentRefusal(IReadOnlyList<string> dependents) => "This marker's replacement ID can't be cleared because " + String.Join(", ", dependents) + (dependents.Count == 1 ? " names" : " name")
            + " its modification in dependsOn. Another holder's activity, binding, or track-following settings need this ID. " + (dependents.Count == 1 ? WindowGuidance.ChangeDependentFirst : WindowGuidance.ChangeDependentsFirst);
        public static string Shared(string id) => "Replacement ID " + id + " is used by more than one modification. A replacement ID must identify one marker in its scene patch file.";
        // Clearing the replacement ID would return a duplicate marker ID while the modification still changes the
        // marker's activity, binding or track-following placement.
        public static string ClearRefusal(string id, string user) => "Marker ID " + id + " is also held by " + user + ", so this marker's activity, binding, and track-following settings need its replacement ID. " + WindowGuidance.ClearSettingsFirst;

        // A generated replacement ID: letters and digits only, so it passes Problem, in GeneratedLength characters. With
        // 62^10 possible IDs a draw that is already used is rare, and the next draw follows; the attempts end the search.
        public const string GeneratedCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        public const int GeneratedLength = 10, GenerationAttempts = 64;
        public const string NoUnusedId = "Couldn't find an unused ID. " + WindowGuidance.GenerateAgain;
        public const string GeneratedMapObjectsUnknown = "The game hasn't loaded the map objects yet, so the patcher can't check whether they use the generated ID. " + WindowGuidance.TryAfterMapLoaded;
        // A random ID that refusal passes, drawn with next, which returns a number from 0 up to but not including its
        // argument; or null with the reason. A refusal that does not name the drawn ID, such as an object that takes no
        // replacement ID or a target that cannot be resolved, and unread map objects end the search at once: another
        // draw would meet the same refusal.
        public static string Generate(Func<int, int> next, Func<string, string> refusal, out string reason) {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (refusal == null) throw new ArgumentNullException(nameof(refusal));
            for (var attempt = 0; attempt < GenerationAttempts; attempt++) {
                var characters = new char[GeneratedLength];
                for (var i = 0; i < characters.Length; i++) characters[i] = GeneratedCharacters[next(GeneratedCharacters.Length)];
                var id = new string(characters);
                var refused = refusal(id);
                if (refused == null) { reason = null; return id; }
                if (refused == MapObjectsUnknown(id)) { reason = GeneratedMapObjectsUnknown; return null; }
                if (!refused.Contains(id)) { reason = refused; return null; }
            }
            reason = NoUnusedId;
            return null;
        }
    }
    // Repairs, replaces and detaches the location relationship of a Generic track marker together with its graph
    // registration, and places or enables the marker object. A relationship edit never moves the object:
    // placement stays a separate, independently set value.
    public sealed class TrackMarkerScene : IScene, IRelationshipScene, IVersionedScene, ITargetConfirmation, IPlacementPreflight, IFollowUpdateScene, IDependentOutputScene, IMarkerIdScene {
        public const string Structure = "track-marker-v1", RequiredVersion = "0.5.0";
        readonly MarkerRelationship relationship;
        // The scene ground masks at and below a marker object, such as the Hemingway station, whose track marker stands
        // on the root of its building: a move of the marker rebuilds them, a mask that flattens the terrain height
        // included.
        readonly MaskFollowing masks;
        public TrackMarkerScene(ITrackMarkerWorld world, Capability capability, GroundMasks masks = null) { relationship = new MarkerRelationship(world, Structure, null, true); Capability = capability; this.masks = new MaskFollowing(masks); }
        // Gives a capability built before the world's ground masks existed, as for a new game's start, those masks. A
        // capability that already has masks keeps them.
        public void UseMasks(GroundMasks value) => masks.Use(value);
        public IReadOnlyList<string> MarkerIdUsers(object target, string id) => relationship.IdUsers(target, id);
        public bool RewritesRegistration(object target, ObjectState state) => relationship.RewritesRegistration(target, state);
        public string MarkerIdRefusal(object target, string id) => relationship.MarkerIdRefusal(target, id);
        public string NewMarkerIdRefusal(object source, string id) => relationship.NewMarkerIdRefusal(source, id);
        public DependentOutput Output(object target) => masks.Output(target);
        public void RetryOutput(object target) => masks.RetryOutput(target);
        public IEnumerable<string> MapIds => relationship.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) => relationship.Resolve(identity);
        public bool StillIdentifies(object target, TargetIdentity identity) => relationship.StillIdentifies(target, identity);
        public void PreflightPlacement(object target) => relationship.PreflightPlacement(target);
        public string MinimumPatcherVersion(TargetIdentity target) => VersionRequirement.For(target, Structure, "Track.TrackMarker", RequiredVersion);
        public void Preflight(object target) => relationship.Preflight(target);
        public void CheckRelationships(object target, ObjectState state) {
            relationship.CheckRelationships(target, state);
        }
        public object Capture(object target) => relationship.Capture(target);
        public ObjectState Read(object target) => relationship.Read(target);
        public void Apply(object target, ObjectState state) => masks.Write(target, () => relationship.Apply(target, state));
        public void ApplyFollowUpdate(object target, ObjectState state) => masks.Write(target, () => relationship.Apply(target, state, true));
        public bool Restore(object target, object snapshot) => masks.Restore(target, () => relationship.Restore(target, snapshot));
    }
    // State that a marker's consumer derives from the marker's location and the object's placement, such as generated geometry.
    internal interface IMarkerConsumer {
        object Capture(object target);
        // Brings the derived state up to date with the current location and placement. A following object's placement follows
        // a track, so its derived state also follows that track's shape; an explicit application, such as an edit or a
        // removal, also brings derived state up to date with a track shape it recorded.
        void Refresh(object target, bool resolvable, bool following, bool explicitApply);
        // Why the derived state disagrees with the current location and placement, or null.
        string Problem(object target, bool resolvable, bool following, bool explicitApply);
        // Why the restored derived state differs from the captured one, or null.
        string Restored(object target, object captured);
    }
    // The shared relationship of a track-marker object: the serialized location, its graph registration, the object's
    // placement and activity, and an optional consumer of the location.
    internal sealed class MarkerRelationship {
        const double PositionTolerance = 0.01, AngleToleranceDegrees = 0.01;
        sealed class Snapshot { internal TrackLocation Location; internal double[] Position, Rotation; internal bool Active, Held; internal object Consumer; internal string Id; }
        readonly ITrackMarkerWorld world;
        readonly string structure;
        readonly IMarkerConsumer consumer;
        // The marker IDs of the world, whether or not its markers take a replacement ID.
        readonly IMarkerIdWorld ids;
        // Whether a marker takes a replacement ID: every marker of a replaceable capability, and a mod object of a
        // capability that takes ids for mod objects, which holds a marker ID of its own from its placement.
        readonly Func<object, bool> replaceable;
        internal MarkerRelationship(ITrackMarkerWorld world, string structure, IMarkerConsumer consumer, bool replaceable = false, bool modObjects = false) {
            this.world = world ?? throw new ArgumentNullException(nameof(world)); this.structure = structure; this.consumer = consumer;
            ids = world as IMarkerIdWorld;
            this.replaceable = replaceable ? (Func<object, bool>)(target => true)
                : modObjects ? (target => world is IModObjectMarkers made && made.IsModObject(target)) : (Func<object, bool>)(target => false);
        }
        // The marker IDs of a marker that takes a replacement ID, or null.
        IMarkerIdWorld Ids(object target) => ids != null && replaceable(target) ? ids : null;
        // Why a new scene marker made from the source cannot hold the id, or null: the id passes the rules of a
        // replacement ID, differs from the source's own, and no scene marker, registration or map object uses it.
        internal string NewMarkerIdRefusal(object source, string id) {
            if (ids == null) return ReplacementMarkerIds.NotSupported;
            var problem = ReplacementMarkerIds.Problem(id);
            if (problem != null) return problem;
            return id == ids.MarkerId(source) ? ReplacementMarkerIds.UsedBy(id, world.IdHolders(source)?.Holders.FirstOrDefault(h => ReferenceEquals(h.Handle, source))?.Name ?? ReplacementMarkerIds.ForeignOwner) : Unavailable(ids, source, id);
        }
        // The refusal for a holder of a duplicate marker ID; a marker that takes a replacement ID offers it.
        string Refused(MarkerIdHolders holders, object target) => Ids(target) == null ? holders.Refusal(target) : holders.Refusal(target) + " " + ReplacementMarkerIds.Offer;
        string Mismatched(MarkerIdHolders holders, object target, string difference) => Ids(target) == null ? holders.Mismatch(target, difference) : holders.Mismatch(target, difference) + " " + ReplacementMarkerIds.Offer;
        internal IReadOnlyList<string> IdUsers(object target, string id) => Ids(target)?.IdUsers(id, target) ?? Array.Empty<string>();
        // The settings CheckRelationships refuses for a holder of a duplicate marker ID, read as a setting rather than a
        // change: a value equal to the current one counts too, so a later load in another order still finds it.
        internal bool RewritesRegistration(object target, ObjectState state) =>
            state != null && (state.Active.HasValue || state.Marker != null || state.Placement?.Following != null || world.DependentProblem(target) != null);
        // Why the marker cannot take this replacement ID, or null; its own ID is no change, as CheckRelationships treats it.
        internal string MarkerIdRefusal(object target, string id) {
            if (Ids(target) == null) return ReplacementMarkerIds.Problem(id) ?? ReplacementMarkerIds.NotSupported;
            return id == Ids(target).MarkerId(target) ? null : Unavailable(Ids(target), target, id);
        }
        // Why the marker cannot take this replacement ID in the world as it is, or null.
        string Unavailable(IMarkerIdWorld ids, object target, string id) {
            var problem = ReplacementMarkerIds.Problem(id);
            if (problem != null) return problem;
            var user = ids.IdUsers(id, target).FirstOrDefault();
            if (user != null) return ReplacementMarkerIds.UsedBy(id, user);
            // Unread map objects fail closed: an ID the check could not cover is not taken.
            var mapObjects = ids.MapObjectIds;
            if (mapObjects == null) return ReplacementMarkerIds.MapObjectsUnknown(id);
            return mapObjects.Contains(id, StringComparer.Ordinal) ? ReplacementMarkerIds.UsedByMapObject(id) : null;
        }
        // Gives the marker another id: its own registration moves to the new id, and no other holder of the old id loses its registration.
        void Rename(object target, string id) {
            Ids(target).UnregisterOwn(target);
            Ids(target).SetMarkerId(target, id);
            if (world.Registers(target) && Resolvable(world.Location(target))) world.Register(target);
        }
        internal IEnumerable<string> MapIds => world.MapIds;
        internal IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Structure != null && identity.Structure != structure) return Array.Empty<object>();
            return world.Resolve(identity) ?? Array.Empty<object>();
        }
        internal bool StillIdentifies(object target, TargetIdentity identity) =>
            (identity.Structure == null || identity.Structure == structure) && world is ITargetConfirmation confirmation && confirmation.StillIdentifies(target, identity);
        // The preflight of a placement-only update: the object's own pattern and its registration, without the consumer scans.
        // A world without the placement check takes the full preflight.
        internal void PreflightPlacement(object target) {
            var problem = world is IPlacementPattern placement ? placement.PlacementProblem(target) : world.PatternProblem(target);
            if (problem != null) throw new InvalidOperationException(problem);
            var difference = Coherence(target, true);
            if (difference != null) throw new InvalidOperationException(Incoherent(target, difference));
        }
        internal void Preflight(object target) {
            var problem = world.PatternProblem(target);
            if (problem != null) throw new InvalidOperationException(problem);
            var difference = Coherence(target, true);
            if (difference != null) throw new InvalidOperationException(Incoherent(target, difference));
        }
        // The refusal of a registration that does not match the location and activity. A holder of a duplicate marker ID
        // gets its explanation instead of advice to reload, which registers the holders under the same marker ID again.
        string Incoherent(object target, string difference) {
            var holders = Holders(target);
            return holders != null && holders.Duplicate ? Mismatched(holders, target, difference)
                : "The game's track information for this marker is out of sync. " + difference.TrimEnd('.') + ". " + WindowGuidance.ReloadMapEditorBeforeEditing;
        }
        internal void CheckRelationships(object target, ObjectState state) {
            if (state?.Coverage != null) throw new InvalidOperationException("This object has no supported crossing-coverage relationship");
            if (state?.Equipment != null) throw new InvalidOperationException("This object has no supported crossing-equipment relationship");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
            // A replacement ID applies first: a marker that leaves a duplicate marker ID holds an id of its own
            // for the rest of the modification.
            var renames = false;
            if (state?.MarkerId != null) {
                if (Ids(target) == null) throw new InvalidPatchDefinitionException(ReplacementMarkerIds.NotSupported);
                var unavailable = MarkerIdRefusal(target, state.MarkerId);
                if (unavailable != null) throw new InvalidPatchDefinitionException(unavailable);
                renames = state.MarkerId != Ids(target).MarkerId(target);
            }
            // Before mutation, what the write would refuse for a holder of a duplicate marker ID: a change of its activity,
            // binding or track-following placement, and a dependent cache the write would correct by re-registering it.
            var dependent = renames || Changes(target, state) ? null : world.DependentProblem(target);
            if (!renames && (Changes(target, state) || dependent != null)) {
                var holders = Holders(target);
                if (holders != null && holders.Duplicate) {
                    if (dependent != null) throw new InvalidOperationException(Mismatched(holders, target, dependent));
                    throw new InvalidPatchDefinitionException(Refused(holders, target));
                }
            }
            if (state?.Active == false && world.DisableRefusal(target) is string reason) throw new InvalidOperationException(reason);
            var group = world.GroupActivity(target);
            if (group != null && state?.Active == true)
                throw new InvalidPatchDefinitionException("The object's activity follows track group " + (String.IsNullOrEmpty(group.LocationGroup) ? "of its bound segment" : group.LocationGroup) + "; only an explicit disable is supported. " + WindowGuidance.OmitActive);
            var location = state?.Marker?.Location;
            if (location == null) return;
            var problem = Unresolved(location);
            if (problem != null) throw new InvalidPatchDefinitionException(problem);
        }
        // Why a location cannot be bound, or null: its segment must exist exactly once and contain the distance.
        internal string Unresolved(TrackLocation location) {
            if (location.End != "a" && location.End != "b") return "Relationship end must be \"a\" or \"b\"";
            var lengths = world.SegmentLengths(location.Segment) ?? Array.Empty<double>();
            if (lengths.Count == 0) return "Relationship target segment " + location.Segment + " is missing";
            if (lengths.Count > 1) return "Relationship target segment " + location.Segment + " is ambiguous";
            if (Double.IsNaN(location.Distance) || location.Distance < 0 || location.Distance > lengths[0] + TrackLocation.DistanceTolerance)
                return "Relationship distance " + ShownNumber.Text(location.Distance) + " lies outside segment " + location.Segment;
            return null;
        }
        bool Resolvable(TrackLocation location) => location != null && Unresolved(location) == null;
        // The scene markers that hold the marker's id, completed by the owners of its registrations wherever they are.
        MarkerIdHolders Holders(object target) => world.IdHolders(target)?.With(world.Registrations(target));
        // Whether one modification's state changes the marker's activity, binding or track-following placement: what
        // removes or adds the registration under the marker's id. A value equal to the current one changes nothing.
        bool Changes(object target, ObjectState state) {
            if (state == null) return false;
            if (state.Placement?.Following != null) return true;
            if (state.Marker != null && !MarkerBinding.Same(state.Marker, MarkerBinding.Of(world.Location(target)))) return true;
            return state.Active.HasValue && ActivityChanges(target, state.Active, world.GroupActivity(target));
        }
        // Whether writing this activity switches the object or its explicit-disable hold. A group follower's null releases an installed hold.
        bool ActivityChanges(object target, bool? active, GroupActivity group) {
            if (group == null) return active.HasValue && active.Value != world.Active(target);
            return active == false ? !group.Held || world.Active(target) : group.Held;
        }
        internal object Capture(object target) => new Snapshot { Location = world.Location(target), Position = world.Position(target), Rotation = world.Rotation(target), Active = world.Active(target), Held = world.GroupActivity(target)?.Held == true, Consumer = consumer?.Capture(target), Id = Ids(target)?.MarkerId(target) };
        // The activity of a group follower is group-driven (null) unless the explicit-disable hold is installed.
        internal ObjectState Read(object target) => new ObjectState(new Placement(world.Position(target), world.Rotation(target)), ReadActive(target), MarkerBinding.Of(world.Location(target))).WithMarkerId(Ids(target)?.MarkerId(target));
        bool? ReadActive(object target) {
            var group = world.GroupActivity(target);
            return group == null || group.Held ? world.Active(target) : (bool?)null;
        }
        internal void Apply(object target, ObjectState state, bool followUpdate = false) {
            var before = world.Location(target);
            var location = state.Marker == null ? before : state.Marker.Location;
            var follower = world.GroupActivity(target) != null;
            if (follower && state.Active == true) CheckRelationships(target, state);
            // A complete state without an explicit disable returns a follower to group-driven activity. One modification's
            // own state without an activity value leaves an installed disable in place.
            var disabled = state.Active == false || (follower && state.Active == null && state.LeavesActivity && world.GroupActivity(target).Held);
            var active = follower ? (disabled ? false : (bool?)null) : state.Active ?? world.Active(target);
            var following = state.Placement?.Following != null;
            var activeBefore = world.Active(target);
            var relationshipWritten = Write(target, location, state.Placement?.Position, state.Placement?.Rotation, active, !TrackLocation.Same(before, location), following, true, state.MarkerId);
            var difference = Verify(target, location, state.Placement, active, following, true, state.MarkerId);
            if (difference != null) throw new InvalidOperationException("Applied marker state did not verify: " + difference);
            // The native warning depends on the binding's validity and the marker's activity. A track-following update that
            // changes neither skips the reconciliation and its search of every marker; an explicit application always runs it.
            if (!followUpdate || relationshipWritten || world.Active(target) != activeBefore) (world as ITrackMarkerDiagnostics)?.AfterVerifiedState(target);
        }
        // Why the object's derived state disagrees with the desired state although its placement may already agree, or null.
        internal string DerivedProblem(object target, ObjectState desired) =>
            consumer?.Problem(target, Resolvable(world.Location(target)), desired?.Placement?.Following != null, false);
        internal bool Restore(object target, object snapshot) {
            var captured = (Snapshot)snapshot;
            try {
                var active = world.GroupActivity(target) == null ? captured.Active : (captured.Held ? false : (bool?)null);
                Write(target, captured.Location, captured.Position, captured.Rotation, active, !TrackLocation.Same(world.Location(target), captured.Location), false, false, captured.Id);
                var verified = Verify(target, captured.Location, new Placement(captured.Position, captured.Rotation), active, false, false, captured.Id) == null
                    && (consumer == null || consumer.Restored(target, captured.Consumer) == null);
                if (verified) (world as ITrackMarkerDiagnostics)?.AfterVerifiedState(target);
                return verified;
            } catch { return false; }
        }
        // The relationship first, so the registration follows the new location; activity then lets the native lifecycle register or remove it.
        // A group follower's hold is installed before anything can activate the object and released only after the
        // relationship; its subscription is observed again for the current location; without the hold the group sets activity.
        // The consumer follows the final location and placement.
        // True when the relationship and its registration were written.
        // A write that would touch the registration of a holder of a duplicate marker ID stops before any mutation: removing it by id
        // would remove the registration of every holder.
        // A marker ID changes first when the marker leaves a duplicate marker ID, so the rest of the write touches only its
        // own registration under the new id; otherwise it changes last, after the activity and binding changed under the
        // id the marker holds alone, which is how a restoration or removal returns a duplicate marker ID.
        bool Write(object target, TrackLocation location, double[] position, double[] rotation, bool? active, bool rebind, bool following, bool explicitApply, string markerId = null) {
            var renaming = Ids(target) != null && markerId != null && markerId != Ids(target).MarkerId(target);
            var renamed = false;
            if (renaming && Holders(target)?.Duplicate == true) { Rename(target, markerId); renaming = false; renamed = true; }
            var group = world.GroupActivity(target);
            if (rebind || ActivityChanges(target, active, group) || Coherence(target) != null || world.DependentProblem(target) != null) {
                var holders = Holders(target);
                if (holders != null && holders.Duplicate) throw new InvalidOperationException(Refused(holders, target));
            }
            if (group != null && active == false) {
                if (!group.Held) world.HoldDisabled(target, true);
                if (world.Active(target)) world.SetActive(target, false);
            }
            var written = rebind || Coherence(target) != null || world.DependentProblem(target) != null;
            if (written) {
                world.Unregister(target);
                world.SetLocation(target, location);
                if (world.Registers(target) && Resolvable(location)) world.Register(target);
                world.RefreshDependents(target);
            }
            if (group != null) {
                var release = active != false && world.GroupActivity(target).Held;
                if (release) world.HoldDisabled(target, false);
                if (rebind || release || GroupProblem(target) != null) world.ObserveGroup(target);
            } else if (active == true && !world.Active(target)) world.SetActive(target, true);
            if (position != null) world.SetPlacement(target, position, rotation);
            if (group == null && active == false && world.Active(target)) world.SetActive(target, false);
            if (renaming) { Rename(target, markerId); renamed = true; }
            consumer?.Refresh(target, Resolvable(world.Location(target)), following, explicitApply);
            return written || renamed;
        }
        string Verify(object target, TrackLocation location, Placement placement, bool? active, bool following, bool explicitApply, string markerId = null) {
            if (Ids(target) != null && markerId != null && Ids(target).MarkerId(target) != markerId) return "the marker ID is " + Ids(target).MarkerId(target) + ", expected " + markerId;
            if (!TrackLocation.Same(world.Location(target), location)) return "the serialized location is " + (world.Location(target)?.ToString() ?? "detached") + ", expected " + (location?.ToString() ?? "detached");
            var group = world.GroupActivity(target);
            if (group == null ? world.Active(target) != active : group.Held != (active == false)) return "activity differs";
            if (placement != null && (!Near(world.Position(target), placement.Position) || !SameRotation(world.Rotation(target), placement.Rotation))) return "placement differs";
            return Coherence(target) ?? world.DependentProblem(target) ?? GroupProblem(target) ?? consumer?.Problem(target, Resolvable(location), following, explicitApply);
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
        // leftBehind: the preflight of an edit leaves out this marker's own entries under a segment the track graph no
        // longer has. A native track edit that removes a segment, such as Dissolve, leaves the entries of the markers
        // on it in the graph until each marker is enabled again, which a Map Editor reload does. The game finds no
        // track for them, and the edit's write removes them with the native unregistration step and registers the
        // marker anew, as that reload would: the edit needs no reload first. Its verification counts every entry.
        string Coherence(object target, bool leftBehind = false) {
            var location = world.Location(target);
            // Another holder's registration under the same marker ID is never a registration of this marker.
            var entries = (world.Registrations(target) ?? Array.Empty<MarkerRegistration>()).Where(e => e.Owner == null || ReferenceEquals(e.Owner, target))
                .Where(e => !(leftBehind && e.HoldsMarker && (world.SegmentLengths(e.Bucket) ?? Array.Empty<double>()).Count == 0)).ToList();
            var expected = world.Registers(target) && Resolvable(location);
            if (!expected) return entries.Count == 0 ? null : entries.Count == 1 ? "The game still lists a track location for this marker." : "The game still lists " + entries.Count + " track locations for this marker.";
            if (entries.Count != 1) return "The game lists " + entries.Count + " track locations for this marker. It should list one.";
            var entry = entries[0];
            if (!entry.HoldsMarker) return "This marker ID points to another object.";
            if (entry.Bucket != location.Segment || !TrackLocation.Same(entry.Location, location)) return "The game lists " + entry.Location + " as the track location for this marker. The marker uses " + location + ".";
            return null;
        }
        internal static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b) {
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
