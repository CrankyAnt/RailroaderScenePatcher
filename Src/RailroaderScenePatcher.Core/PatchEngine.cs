// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RailroaderScenePatcher.Core {
    // A checked supplied value/reference is invalid. Native or unexpected exceptions retain an unknown cause.
    public sealed class InvalidPatchDefinitionException : InvalidOperationException {
        public InvalidPatchDefinitionException(string message) : base(message) { }
    }
#if ISSUE39_OBSERVATION
    // Only the explicitly instrumented integration-test build exposes this sink.
    public static class JourneyObservation {
        public static Action<string> Sink;
    }
#endif
    public enum Capability { Checked, Unchecked, Incompatible }
    public sealed class TargetIdentity {
        internal TargetIdentity() { }
        public TargetIdentity(string scene, string path, string component, string structure = null) {
            Scene = scene; Path = path; Component = component; Structure = structure;
        }
        public string Scene { get; internal set; }
        public string Path { get; internal set; }
        public string Component { get; internal set; }
        public string Structure { get; internal set; }
    }
    public sealed class Placement {
        internal Placement() { }
        public Placement(double[] position, double[] rotation) { Position = position; Rotation = rotation; }
        public double[] Position { get; internal set; }
        public double[] Rotation { get; internal set; }
        // Set for a track-following placement: Position and Rotation are then its evaluation at the anchor's current pose,
        // or null until it is evaluated. Null for a fixed placement.
        public TrackFollowing Following { get; internal set; }
    }
    // Desired or observed state of one object. A null member is left untouched by an application.
    public sealed class ObjectState {
        public ObjectState(Placement placement, bool? active) : this(placement, active, null) { }
        public ObjectState(Placement placement, bool? active, MarkerBinding marker) : this(placement, active, marker, null) { }
        public ObjectState(Placement placement, bool? active, MarkerBinding marker, CrossingCoverage coverage) { Placement = placement; Active = active; Marker = marker; Coverage = coverage; }
        public Placement Placement { get; }
        public bool? Active { get; }
        // The track-marker relationship: a location or explicit detachment. Null leaves it untouched, and an object without one reads null.
        public MarkerBinding Marker { get; }
        // The crossing-coverage relationship. Null leaves it untouched, and an object without one reads null. This mod's coverage
        // lists only its explicit circuits; observed coverage lists every circuit of the crossing.
        public CrossingCoverage Coverage { get; }
        // Tells the two meanings of a null Active apart for an object whose activity follows a track group. True: this is
        // one modification's own state, and the application leaves the activity as it is, including an explicit disable
        // that another modification installed. False: this is the complete desired state, and a null Active returns the
        // object to its group-driven activity, which releases an explicit disable.
        public bool LeavesActivity { get; private set; }
        public ObjectState LeavingActivity() => new ObjectState(Placement, Active, Marker, Coverage) { LeavesActivity = true };
    }
    public interface IScene {
        IEnumerable<string> MapIds { get; }
        Capability Capability { get; }
        IReadOnlyList<object> Resolve(TargetIdentity identity);
        void Preflight(object target);
        object Capture(object target);
        // The complete current state in map coordinates, independent of the current origin.
        ObjectState Read(object target);
        void Apply(object target, ObjectState state);
        bool Restore(object target, object snapshot);
    }
    public sealed class Outcome {
        public bool Applied { get; internal set; }
        public string Error { get; internal set; }
        public object ResolvedTarget { get; internal set; }
        public FailureCause Cause { get; internal set; }
        public bool MapEditorOnly { get; internal set; }
        public TargetIdentity Target { get; internal set; }
        // Applied without a change: the operation found nothing to do and recorded nothing.
        public bool Unchanged { get; internal set; }
        // An edit in scene patch editing whose value equals the object's value without this mod: the modification no longer holds
        // that setting, so the edit acted as a revert of it.
        public bool Reverted { get; internal set; }
    }
    // A capability that keeps state for the objects it resolved. Release drops that state for an object that no longer
    // stands under its identity in a later load attempt, and removes only what the patcher owns for it; native state is
    // never changed. It throws when owned state remains, and keeps what remains so a later load attempt retries.
    // Unsettled tells an object that still exists but cannot be checked against its identity yet, such as an object whose
    // scene is not reported as loaded during a native check that runs inside a scene load, from an object that is gone.
    public interface IRetainedScene {
        void Release(object target);
        bool Unsettled(object target);
    }
    // The outcome of reconciling a retained world at the start of a new load attempt.
    public sealed class ReentryResult {
        internal ReentryResult(int retained, int released, IReadOnlyList<string> failures) { Retained = retained; Released = released; Failures = failures; }
        // Targets that still stand under their identities, or still exist but cannot be checked yet; their captured state
        // and guards carry over.
        public int Retained { get; }
        // Targets whose state was released because they no longer stand under their identities.
        public int Released { get; }
        // Local cleanup failures. The affected target stays recorded so the next load attempt retries its release.
        public IReadOnlyList<string> Failures { get; }
    }
    public sealed class DocumentResult {
        public string Error { get; internal set; }
        public IReadOnlyDictionary<string, Outcome> Outcomes { get; internal set; }
        public bool MapEditorOnly { get; internal set; }
        public string EarlyRouteUnavailable { get; internal set; }
    }
    public sealed class PatchEngine {
        readonly IScene scene;
        readonly Dictionary<string, Outcome> failed = new Dictionary<string, Outcome>();
        readonly HashSet<object> unsafeTargets = new HashSet<object>();
        // Restored targets whose dependent output, such as regenerated terrain, has not yet verified.
        readonly HashSet<object> awaitingOutput = new HashSet<object>();
        readonly Dictionary<object, ObjectState> underlying = new Dictionary<object, ObjectState>();
        // The identity each object was resolved under, in resolution order, so a later load attempt can tell a retained
        // object from a reconstructed one.
        readonly Dictionary<object, TargetIdentity> identities = new Dictionary<object, TargetIdentity>(new ByReference());
        readonly List<object> resolutionOrder = new List<object>();
        sealed class ByReference : IEqualityComparer<object> {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
        // Addition IDs that more than one modification of the document being applied uses.
        HashSet<string> sharedAdditions = new HashSet<string>(StringComparer.Ordinal);
        internal IScene Scene { get { return scene; } }
        internal sealed class Modification {
            internal string Id; internal TargetIdentity Identity; internal ObjectState State; internal string[] Dependencies;
        }
        [Conditional("ISSUE39_OBSERVATION")]
        void Observe(string stage, string document, string id = null, string path = null, string error = null) {
#if ISSUE39_OBSERVATION
            try {
                JourneyObservation.Sink?.Invoke(new JObject {
                    ["stage"] = stage, ["document"] = document, ["id"] = id,
                    ["path"] = path, ["error"] = error,
                    ["engineRuntimeId"] = RuntimeHelpers.GetHashCode(this)
                }.ToString(Newtonsoft.Json.Formatting.None));
            } catch { /* Observation cannot alter application or failure handling. */ }
#endif
        }
        public PatchEngine(IScene scene) { this.scene = scene ?? throw new ArgumentNullException(nameof(scene)); }
        void Track(TargetIdentity identity, object target) {
            if (target == null || identities.ContainsKey(target)) return;
            identities.Add(target, identity); resolutionOrder.Add(target);
        }
        // Starts a new load attempt over the retained world. An object that still resolves under its recorded identity
        // keeps its captured underlying state and its unverified-cleanup guard, and so does an object an IRetainedScene reports
        // as unsettled: it still exists, so releasing it would strip the patcher's state from a live object. Any other object is
        // released: the engine forgets it and an IRetainedScene removes what the patcher owns for it. Failures recorded in the previous attempt
        // are retried in this one. A failed release is reported and retried by the next load attempt.
        public ReentryResult Reenter() {
            failed.Clear();
            int retained = 0, released = 0;
            var failures = new List<string>();
            foreach (var target in resolutionOrder.ToArray()) {
                var identity = identities[target];
                bool present;
                try { present = (scene.Resolve(identity) ?? Array.Empty<object>()).Any(m => ReferenceEquals(m, target)) || (scene as IRetainedScene)?.Unsettled(target) == true; }
                catch (Exception e) { failures.Add(Describe(identity) + ": the retained object could not be checked, so its state is kept: " + e.Message); retained++; continue; }
                if (present) { retained++; continue; }
                try { (scene as IRetainedScene)?.Release(target); }
                catch (Exception e) { failures.Add(Describe(identity) + ": owned state of the reconstructed object remains: " + e.Message); continue; }
                identities.Remove(target); resolutionOrder.Remove(target);
                underlying.Remove(target); unsafeTargets.Remove(target); awaitingOutput.Remove(target);
                released++;
            }
            return new ReentryResult(retained, released, failures);
        }
        static string Describe(TargetIdentity identity) => identity.Scene + "/" + identity.Path + " [" + identity.Component + "]";
        public DocumentResult Apply(string json) { return Apply(json, ""); }
        public DocumentResult Apply(string json, string documentKey) {
            return Apply(json, documentKey, null);
        }
        public DocumentResult Apply(string json, string documentKey, IReadOnlyDictionary<string, InspectedTarget> inspectedTargets) {
            return ApplyDocument(json, documentKey, inspectedTargets, null);
        }
        // The complete envelope is checked before selecting a dependency-free relationship repair for this native check.
        public DocumentResult BeforeNativeCheck(string json, string documentKey, TargetIdentity target) {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return ApplyDocument(json, documentKey, null, target);
        }
        DocumentResult ApplyDocument(string json, string documentKey, IReadOnlyDictionary<string, InspectedTarget> inspectedTargets, TargetIdentity nativeTarget) {
            if (documentKey == null) throw new ArgumentNullException(nameof(documentKey));
            Observe("document-dispatch", documentKey);
            var result = new DocumentResult();
            var outcomes = new Dictionary<string, Outcome>(); result.Outcomes = outcomes;
            var document = PatchDocument.Load(json);
            if (!document.CanWrite) { result.Error = document.Error; result.MapEditorOnly = true; return result; }
            try {
                if (!scene.MapIds.Contains((string)document.Root["sourceMap"]["mapId"], StringComparer.Ordinal)) { result.Error = "Target map does not match the loaded map context"; result.MapEditorOnly = true; return result; }
                if (scene.Capability == Capability.Incompatible) { result.Error = "Placement capability is incompatible"; return result; }
                var entries = Entries(document.Modifications);
                sharedAdditions = SharedAdditionIds(entries.Values);
                var visiting = new HashSet<string>();
                // Modifications that dependsOn does not order run in ordinal key order, whatever the order of the text.
                foreach (var id in entries.Keys) {
                    if (nativeTarget != null && !EarlyRepair(id, entries[id], nativeTarget, out var unavailable)) {
                        if (unavailable != null) result.EarlyRouteUnavailable = unavailable;
                        continue;
                    }
                    Execute(id, entries, outcomes, visiting, documentKey, inspectedTargets);
                }
            } catch (Exception e) { result.Error = "Scene check failed: " + e.Message; }
            return result;
        }
        bool EarlyRepair(string id, JObject entry, TargetIdentity target, out string unavailable) {
            unavailable = null;
            try {
                var parsed = Parse(entry, "", id); var identity = parsed.Identity;
                if (identity.Scene != target.Scene || identity.Path != target.Path || identity.Component != target.Component ||
                    identity.Structure != null && identity.Structure != target.Structure || parsed.State.Marker?.Location == null) return false;
                if (parsed.Dependencies.Length != 0 || parsed.State.Placement != null || parsed.State.Active != null) {
                    unavailable = "Operation " + id + " requires world-load scheduling for dependencies, placement or activity";
                    return false;
                }
                // A prerequisite missing at OnEnable is not yet a failed runtime attempt. The definitive world-load
                // application still owns invalid-definition reporting and the existing guarded-failure policy.
                try {
                    var matches = scene.Resolve(identity);
                    if (matches == null || matches.Count != 1 || matches[0] == null) throw new InvalidOperationException("Target is not yet unique and available");
                    scene.Preflight(matches[0]);
                    CheckRelationships(matches[0], parsed.State);
                    return true;
                } catch (Exception e) { unavailable = "Early repair prerequisites unavailable: " + e.Message; return false; }
            } catch (InvalidOperationException) { return false; }
        }
        Outcome Execute(string id, SortedDictionary<string,JObject> entries, Dictionary<string,Outcome> outcomes, HashSet<string> visiting, string documentKey, IReadOnlyDictionary<string, InspectedTarget> inspectedTargets) {
            Outcome prior;
            if (outcomes.TryGetValue(id, out prior)) return prior;
            var outcome = new Outcome();
            if (!visiting.Add(id)) { outcome.Error = "Dependency cycle"; outcome.Cause = FailureCause.InvalidDefinition; return outcome; }
            var key = documentKey.Length + ":" + documentKey + id;
            try {
                if (failed.ContainsKey(key)) {
                    Observe("retry-blocked", documentKey, id);
                    var previous = failed[key];
                    outcome.Error = "Operation already failed during this load: " + previous.Error;
                    outcome.Cause = previous.Cause; outcome.MapEditorOnly = previous.MapEditorOnly; outcome.Target = previous.Target;
                    outcomes[id] = outcome;
                    return outcome;
                }
                var item = entries[id];
                var unsupported = UnsupportedContent(id, item);
                if (unsupported != null) { outcome.MapEditorOnly = true; throw new InvalidOperationException(unsupported); }
                var shared = AdditionIds(item).FirstOrDefault(sharedAdditions.Contains);
                if (shared != null) throw new InvalidPatchDefinitionException(SharedAddition(shared));
                string[] dependencies;
                try { dependencies = Dependencies(item); }
                catch (InvalidOperationException e) { throw new InvalidPatchDefinitionException(e.Message); }
                foreach (var dep in dependencies) {
                    if (!entries.ContainsKey(dep)) throw new InvalidPatchDefinitionException("Dependency failed: " + dep);
                    var dependency = Execute(dep, entries, outcomes, visiting, documentKey, inspectedTargets);
                    if (!dependency.Applied) { outcome.Cause = dependency.Cause; outcome.MapEditorOnly = dependency.MapEditorOnly; throw new InvalidOperationException("Dependency failed: " + dep); }
                }
                Modification parsed;
                try { outcome.Target = ParseTarget(item["target"] as JObject); parsed = Parse(item, documentKey, id); }
                catch (InvalidOperationException e) {
                    outcome.Cause = FailureCause.InvalidDefinition;
                    outcome.MapEditorOnly = e.Message.StartsWith("Unsupported", StringComparison.Ordinal) || e.Message == "Invalid or unsupported target";
                    throw;
                }
                var identity = parsed.Identity;
                outcome.Target = identity;
                Observe("resolve", documentKey, id, identity.Path);
                var matches = scene.Resolve(identity);
                if (matches == null || matches.Count != 1 || matches[0] == null) { outcome.Cause = FailureCause.InvalidDefinition; throw new InvalidOperationException("Target must resolve uniquely"); }
                var resolved = matches[0];
                Track(identity, resolved);
                var mapContext = scene.MapIds.ToArray();
                InspectedTarget expected = null;
                if (inspectedTargets != null && (!inspectedTargets.TryGetValue(id, out expected) || expected == null))
                    throw new InvalidOperationException("An inspected target is required for every edited modification");
                expected?.Validate(scene, identity, resolved);
                outcome.ResolvedTarget = resolved;
                if (IsUnsafe(resolved)) throw new InvalidOperationException("Target cleanup is unverified");
                Observe("preflight", documentKey, id, identity.Path);
                scene.Preflight(resolved);
                // One modification's own state: an omitted active leaves the activity another modification set.
                var state = Evaluate(parsed.State.LeavingActivity());
                CheckRelationships(resolved, state);
                Observe("capture", documentKey, id, identity.Path);
                Underlying(resolved);
                var snapshot = scene.Capture(resolved);
                // Capture and preflight may invoke native callbacks. Never redirect to a new match.
                GuardMutation(identity, resolved, mapContext, expected);
                try {
                    Observe("mutation", documentKey, id, identity.Path);
                    scene.Apply(resolved, state);
                    Observe("applied", documentKey, id, identity.Path);
                } catch (Exception e) {
                    bool restored = false;
                    Observe("restore-request", documentKey, id, identity.Path);
                    try { restored = scene.Restore(resolved, snapshot); } catch { }
                    Observe("restore-result", documentKey, id, identity.Path, restored ? "verified" : "unverified");
                    if (!restored) unsafeTargets.Add(resolved);
                    else if (AwaitOutput(resolved)) throw new InvalidOperationException("Mutation failed and was restored; the restored dependent output is still being regenerated, and this target waits until it verifies: " + e.Message);
                    throw new InvalidOperationException(restored ? "Mutation failed and was restored: " + e.Message : "Mutation failed; cleanup is unverified: " + e.Message);
                }
                outcome.Applied = true;
            } catch (Exception e) { outcome.Error = e.Message; if (e is InvalidPatchDefinitionException) outcome.Cause = FailureCause.InvalidDefinition; failed[key] = outcome; Observe("failed", documentKey, id, error: e.Message); }
            finally { visiting.Remove(id); }
            outcomes[id] = outcome;
            return outcome;
        }
        // The state this object had before any modification of this engine touched it.
        internal ObjectState Underlying(object target) {
            ObjectState state;
            if (!underlying.TryGetValue(target, out state)) underlying.Add(target, state = scene.Read(target));
            return state;
        }
        // Set for the duration of a reconciliation that resolves the same targets several times a second.
        internal Func<TargetIdentity, object> ResolveOverride;
        internal object ResolveUnique(TargetIdentity identity) { return ResolveOverride != null ? ResolveOverride(identity) : ResolveFresh(identity); }
        internal object ResolveFresh(TargetIdentity identity) {
            var matches = scene.Resolve(identity);
            if (matches == null || matches.Count != 1 || matches[0] == null) throw new InvalidOperationException("Target must resolve uniquely");
            Track(identity, matches[0]);
            return matches[0];
        }
        // The immediate guard before any mutation: same prerequisites, same unique object, same map context,
        // and for an edit in scene patch editing the same inspected observation.
        internal void GuardMutation(TargetIdentity identity, object resolved, string[] mapContext, InspectedTarget expected) {
            scene.Preflight(resolved);
            var current = scene.Resolve(identity);
            if (current == null || current.Count != 1 || !ReferenceEquals(current[0], resolved))
                throw new InvalidOperationException("Target changed before mutation; refresh and inspect again");
            if (!mapContext.SequenceEqual(scene.MapIds))
                throw new InvalidOperationException("Map context changed before mutation");
            expected?.Validate(scene, identity, resolved);
        }
        // Refuses a relationship the object does not support and a relationship target that does not resolve uniquely.
        // A relationship scene also checks the settings its relationships make unsupported.
        internal void CheckRelationships(object target, ObjectState state) {
            if (scene is IRelationshipScene relationships) { relationships.CheckRelationships(target, state); return; }
            if (state?.Marker != null) throw new InvalidOperationException("This object has no supported track-marker relationship");
            if (state?.Coverage != null) throw new InvalidOperationException("This object has no supported crossing-coverage relationship");
        }
        // A target is unsafe while its cleanup is unverified, including restored dependent output that has not completed.
        internal bool IsUnsafe(object target) {
            if (unsafeTargets.Contains(target)) return true;
            if (!awaitingOutput.Contains(target)) return false;
            var output = Output(target);
            if (output.Error != null) { awaitingOutput.Remove(target); unsafeTargets.Add(target); return true; }
            if (!output.Settled) return true;
            awaitingOutput.Remove(target);
            return false;
        }
        internal void SetUnsafe(object target, bool value) { if (value) unsafeTargets.Add(target); else { unsafeTargets.Remove(target); awaitingOutput.Remove(target); } }
        internal bool AwaitingOutput(object target) { return awaitingOutput.Contains(target); }
        internal DependentOutput Output(object target) { return target != null && scene is IDependentOutputScene dependent ? dependent.Output(target) ?? DependentOutput.None : DependentOutput.None; }
        // After a verified restoration: keeps the target guarded until its dependent output verifies. True when it waits.
        internal bool AwaitOutput(object target) {
            if (Output(target).Settled) return false;
            awaitingOutput.Add(target);
            return true;
        }
        // The addition IDs a modification sets, and those that more than one modification of a document uses. A blank key is
        // no ID: its modification fails for the empty key, not for sharing it. An addition ID
        // names one addition of its supplying mod, so every modification that shares one fails, wherever it stands.
        internal static IEnumerable<string> AdditionIds(JObject item) =>
            (((item["relationships"] as JObject)?["crossingCoverage"] as JObject)?["additions"] as JObject)?.Properties().Select(p => p.Name).Where(name => !String.IsNullOrWhiteSpace(name)) ?? Enumerable.Empty<string>();
        internal static HashSet<string> SharedAdditionIds(IEnumerable<JObject> items) {
            var seen = new HashSet<string>(StringComparer.Ordinal); var shared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items) foreach (var id in AdditionIds(item)) if (!seen.Add(id)) shared.Add(id);
            return shared;
        }
        internal static string SharedAddition(string id) => "Addition ID " + id + " is used by more than one modification; an addition ID names one addition in its document";
        // Underlying state overlaid with every effective modification of this document for the object. The owner is the key
        // of the document, which scopes the identities of its additions.
        internal ObjectState Desired(JObject modifications, object target, string owner = "") {
            var entries = Entries(modifications);
            var effective = new Dictionary<string, Modification>(StringComparer.Ordinal);
            var order = new List<Modification>(); var targets = new Dictionary<Modification, object>();
            var shared = SharedAdditionIds(entries.Values);
            foreach (var id in entries.Keys) Effective(id, entries, effective, order, targets, new HashSet<string>(), owner, shared);
            var state = Underlying(target);
            foreach (var item in order) {
                if (!ReferenceEquals(targets[item], target)) continue;
                state = new ObjectState(item.State.Placement ?? state.Placement, item.State.Active ?? state.Active, item.State.Marker ?? state.Marker, CrossingCoverage.Overlay(state.Coverage, item.State.Coverage));
            }
            return Evaluate(state);
        }
        // The state with a track-following placement evaluated at its anchor's current native pose. Throws before any
        // mutation when the anchor does not resolve.
        internal ObjectState Evaluate(ObjectState state) {
            var following = state?.Placement?.Following;
            if (following == null) return state;
            if (!(scene is ITrackPoseScene tracks)) throw new InvalidOperationException("Track-following placement is unavailable in this scene");
            var evaluated = new ObjectState(following.Evaluate(tracks.TrackPose(following.Anchor)), state.Active, state.Marker, state.Coverage);
            return state.LeavesActivity ? evaluated.LeavingActivity() : evaluated;
        }
        Modification Effective(string id, SortedDictionary<string, JObject> entries, Dictionary<string, Modification> effective, List<Modification> order, Dictionary<Modification, object> targets, HashSet<string> visiting, string owner, HashSet<string> shared) {
            Modification known;
            if (effective.TryGetValue(id, out known)) return known;
            Modification result = null;
            if (visiting.Add(id)) {
                try {
                    var parsed = Parse(entries[id], owner, id);
                    if (!AdditionIds(entries[id]).Any(shared.Contains) && parsed.Dependencies.All(d => entries.ContainsKey(d) && Effective(d, entries, effective, order, targets, visiting, owner, shared) != null)) {
                        targets[parsed] = ResolveUnique(parsed.Identity);
                        result = parsed; order.Add(parsed);
                    }
                } catch (InvalidOperationException) { }
                visiting.Remove(id);
            }
            effective[id] = result;
            return result;
        }
        internal static TargetIdentity ParseTarget(JObject target) {
            if (target == null || !Only(target, "scene", "path", "component", "structure") || !RequiredString(target,"scene") || !RequiredString(target,"path") || !RequiredString(target,"component") || (target["structure"] != null && target["structure"].Type != JTokenType.String)) throw new InvalidOperationException("Invalid or unsupported target");
            return new TargetIdentity { Scene=(string)target["scene"], Path=(string)target["path"], Component=(string)target["component"], Structure=(string)target["structure"] };
        }
        internal static Placement CheckPlacement(double[] position, double[] rotation) {
            if (position == null || rotation == null || position.Length != 3 || rotation.Length != 4) throw new InvalidOperationException("Invalid placement coordinates");
            if (position.Concat(rotation).Any(x => Double.IsNaN(x) || Double.IsInfinity(x))) throw new InvalidOperationException("Nonfinite placement coordinates");
            if (rotation.All(x => x == 0)) throw new InvalidOperationException("Zero rotation quaternion");
            return new Placement { Position = position, Rotation = rotation };
        }
        static string[] Dependencies(JObject item) {
            var dependencies = item["dependsOn"] as JArray;
            if (dependencies == null || dependencies.Any(x => x.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)x))) throw new InvalidOperationException("Invalid dependencies");
            return dependencies.Select(x => (string)x).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }
        internal static readonly string[] Known = { "target", "placement", "active", "relationships", "dependsOn" };
        // The modifications of a document by key, in ordinal key order.
        internal static SortedDictionary<string, JObject> Entries(JObject modifications) {
            var entries = new SortedDictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var property in modifications.Properties()) if (property.Value is JObject item) entries[property.Name] = item;
            return entries;
        }
        // Why the modification holds content this patcher version does not execute, or null. A modification is named by
        // its key, so an id member inside it is content of an older shape.
        static string UnsupportedContent(string id, JObject item) {
            if (item["id"] != null) return "Unsupported modification content: modification " + id + " carries an id member. The ID of a modification is its key, " + id + "; remove the id member";
            return Only(item, Known) ? null : "Unsupported modification content";
        }
        // The owner is the key of the document the modification belongs to; it scopes the identities of its additions.
        // The ID is the key of the modification; a modification that stands in a document has it without being told.
        internal static Modification Parse(JObject item, string owner = "", string id = null) {
            id = id ?? PatchDocument.Key(item);
            var unsupported = UnsupportedContent(id, item);
            if (unsupported != null) throw new InvalidOperationException(unsupported);
            var dependencies = Dependencies(item);
            var identity = ParseTarget(item["target"] as JObject);
            Placement checkedPlacement = null;
            if (item["placement"] != null) {
                var placement = item["placement"] as JObject;
                var mode = placement?["mode"]?.Type == JTokenType.String ? (string)placement["mode"] : null;
                if (mode == "fixed" && Only(placement, "mode", "position", "rotation")) checkedPlacement = CheckPlacement(Numbers(placement["position"], 3), Numbers(placement["rotation"], 4));
                else if (mode == TrackFollowingMode && Only(placement, "mode", "track", "offset", "rotation")) checkedPlacement = new Placement { Following = ParseFollowing(placement) };
                else throw new InvalidOperationException("Unsupported placement");
            }
            if (item["active"] != null && item["active"].Type != JTokenType.Boolean) throw new InvalidOperationException("Invalid activity");
            MarkerBinding marker = null; CrossingCoverage coverage = null;
            if (item["relationships"] != null) {
                var relationships = item["relationships"] as JObject;
                if (relationships == null || !Only(relationships, "trackMarker", "crossingCoverage") || !relationships.HasValues) throw new InvalidOperationException("Unsupported relationships");
                if (relationships["trackMarker"] != null) marker = ParseTrackMarker(relationships["trackMarker"]);
                if (relationships["crossingCoverage"] != null) coverage = ParseCoverage(relationships["crossingCoverage"]).OwnedBy(owner);
            }
            if (checkedPlacement == null && item["active"] == null && marker == null && coverage == null) throw new InvalidOperationException("Unsupported placement");
            return new Modification { Id = id, Identity = identity, State = new ObjectState(checkedPlacement, (bool?)item["active"], marker, coverage), Dependencies = dependencies };
        }
        public const string TrackFollowingMode = "trackFollowing";
        // A placement relative to an explicitly selected segment location or endpoint node.
        static TrackFollowing ParseFollowing(JObject placement) {
            var track = placement["track"] as JObject;
            if (track == null) throw new InvalidOperationException("Invalid track-following placement: track must name a segment location or a node");
            TrackAnchor anchor;
            if (track["node"] != null) {
                if (!Only(track, "node") || !RequiredString(track, "node")) throw new InvalidOperationException("Invalid track-following placement: node must name a track node and stand alone");
                anchor = TrackAnchor.Endpoint((string)track["node"]);
            } else anchor = TrackAnchor.At(ParseLocation(track, "track-following placement"));
            var offset = Numbers(placement["offset"], 3); var rotation = Numbers(placement["rotation"], 4);
            CheckPlacement(offset, rotation);
            return new TrackFollowing(anchor, offset, rotation);
        }
        static TrackLocation ParseLocation(JObject marker, string what) {
            if (!Only(marker, "segment", "end", "distance")) throw new InvalidOperationException("Unsupported " + (what == "relationship" ? "relationship content" : what + " track content"));
            if (!RequiredString(marker, "segment")) throw new InvalidOperationException("Invalid " + what + ": segment must name a track segment");
            if (marker["end"]?.Type != JTokenType.String || ((string)marker["end"] != "a" && (string)marker["end"] != "b")) throw new InvalidOperationException("Invalid " + what + ": end must be \"a\" or \"b\"");
            if (marker["distance"] == null || (marker["distance"].Type != JTokenType.Integer && marker["distance"].Type != JTokenType.Float)) throw new InvalidOperationException("Invalid " + what + ": distance must be a number");
            var distance = (double)marker["distance"];
            if (Double.IsNaN(distance) || Double.IsInfinity(distance) || distance < 0) throw new InvalidOperationException("Invalid " + what + ": distance must be finite and not negative");
            return new TrackLocation((string)marker["segment"], (string)marker["end"], distance);
        }
        // An explicit relationship setting: a reference sets or replaces it, detach clears it. An omitted relationship stays untouched.
        static MarkerBinding ParseTrackMarker(JToken token) {
            var marker = token as JObject;
            if (marker == null) throw new InvalidOperationException("Unsupported relationships: trackMarker needs a location or an explicit detach");
            if (marker["detach"] != null) {
                if (!Only(marker, "detach") || marker["detach"].Type != JTokenType.Boolean || !(bool)marker["detach"]) throw new InvalidOperationException("Invalid relationship: detach must be true and stand alone");
                return MarkerBinding.Detached;
            }
            return MarkerBinding.At(ParseLocation(marker, "relationship"));
        }
        // Explicit crossing coverage: circuits holds each circuit this mod sets, keyed by its native island span, with selected
        // tracks or an explicit detach. An omitted circuit keeps its coverage; an omitted crossingCoverage leaves every
        // circuit untouched. additions holds the detection additions keyed by their IDs: circuits the patcher adds for their
        // selected tracks, which exist only while the document lists them. Neither the order of the two objects nor the
        // order of their keys has a meaning.
        static CrossingCoverage ParseCoverage(JToken token) {
            var coverage = token as JObject;
            if (coverage == null || !coverage.HasValues) throw new InvalidOperationException("Invalid relationship: crossingCoverage must be an object that holds circuits, additions or both");
            if (!Only(coverage, "circuits", "additions")) throw new InvalidOperationException("Unsupported relationship content");
            var parsed = new List<KeyValuePair<string, CircuitCoverage>>();
            foreach (var name in new[] { "circuits", "additions" }) {
                if (coverage[name] == null) continue;
                var addition = name == "additions";
                var entries = coverage[name] as JObject;
                if (entries == null || !entries.HasValues) throw new InvalidOperationException("Invalid relationship: " + name + " must be an object with at least one entry, keyed by " + (addition ? "addition ID" : "the island span ID of the circuit"));
                foreach (var property in entries.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)) {
                    if (String.IsNullOrWhiteSpace(property.Name)) throw new InvalidOperationException("Invalid relationship: a key of " + name + " is empty; " + (addition ? "an addition is keyed by its ID" : "a circuit is keyed by its island span ID"));
                    var label = (addition ? "addition " : "circuit ") + property.Name;
                    var circuit = property.Value as JObject;
                    if (circuit == null) throw new InvalidOperationException("Invalid relationship: " + label + " must be an object");
                    var key = addition ? DetectionAddition.Key("", property.Name) : property.Name;
                    if (!addition && DetectionAddition.Id(key) != null) throw new InvalidOperationException("Invalid relationship: " + key + " is not the ID of an island span");
                    if (circuit["detach"] != null) {
                        if (addition) throw new InvalidOperationException("Invalid relationship: an addition cannot be detached; omit it to delete it");
                        if (!Only(circuit, "detach") || circuit["detach"].Type != JTokenType.Boolean || !(bool)circuit["detach"]) throw new InvalidOperationException("Invalid relationship: detach must be true and stand alone");
                        parsed.Add(new KeyValuePair<string, CircuitCoverage>(key, CircuitCoverage.Detached));
                        continue;
                    }
                    if (!Only(circuit, "approachA", "island", "approachB")) throw new InvalidOperationException("Unsupported relationship content");
                    var ranges = new SpanRange[3];
                    for (var i = 0; i < 3; i++) {
                        var role = CircuitCoverage.Roles[i];
                        var range = circuit[role] as JObject;
                        if (range == null) throw new InvalidOperationException("Invalid relationship: " + label + " needs approachA, island and approachB" + (addition ? "" : ", or an explicit detach"));
                        if (!Only(range, "from", "to")) throw new InvalidOperationException("Unsupported relationship content");
                        ranges[i] = new SpanRange(RangeEnd(range["from"]), RangeEnd(range["to"]));
                    }
                    parsed.Add(new KeyValuePair<string, CircuitCoverage>(key, addition ? CircuitCoverage.Added(ranges[0], ranges[1], ranges[2]) : CircuitCoverage.Tracks(ranges[0], ranges[1], ranges[2])));
                }
            }
            return new CrossingCoverage(parsed);
        }
        static TrackLocation RangeEnd(JToken token) {
            if (!(token is JObject location)) throw new InvalidOperationException("Invalid relationship: a track location needs segment, end and distance");
            return ParseLocation(location, "relationship");
        }
        static bool RequiredString(JObject obj, string name) { return obj[name]?.Type == JTokenType.String && !String.IsNullOrWhiteSpace((string)obj[name]); }
        static bool Only(JObject obj, params string[] allowed) { return obj.Properties().All(p => allowed.Contains(p.Name, StringComparer.Ordinal)); }
        static double[] Numbers(JToken token, int count) {
            var a = token as JArray;
            if (a == null || a.Count != count || a.Any(x => x.Type != JTokenType.Integer && x.Type != JTokenType.Float)) throw new InvalidOperationException("Invalid placement coordinates");
            return a.Select(x => (double)x).ToArray();
        }
    }
}
