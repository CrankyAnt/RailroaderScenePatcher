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
        // The crossing-coverage relationship. Null leaves it untouched, and an object without one reads null. Authored coverage
        // lists only its explicit circuits; observed coverage lists every circuit of the crossing.
        public CrossingCoverage Coverage { get; }
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
        public bool EditorOnly { get; internal set; }
        public TargetIdentity Target { get; internal set; }
    }
    public sealed class DocumentResult {
        public string Error { get; internal set; }
        public IReadOnlyDictionary<string, Outcome> Outcomes { get; internal set; }
        public bool EditorOnly { get; internal set; }
        public string EarlyRouteUnavailable { get; internal set; }
    }
    public sealed class PatchEngine {
        readonly IScene scene;
        readonly Dictionary<string, Outcome> failed = new Dictionary<string, Outcome>();
        readonly HashSet<object> unsafeTargets = new HashSet<object>();
        // Restored targets whose dependent output, such as regenerated terrain, has not yet verified.
        readonly HashSet<object> awaitingOutput = new HashSet<object>();
        readonly Dictionary<object, ObjectState> underlying = new Dictionary<object, ObjectState>();
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
            if (!document.CanWrite) { result.Error = document.Error; result.EditorOnly = true; return result; }
            try {
                if (!scene.MapIds.Contains((string)document.Root["sourceMap"]["mapId"], StringComparer.Ordinal)) { result.Error = "Source map does not match loaded map context"; result.EditorOnly = true; return result; }
                if (scene.Capability == Capability.Incompatible) { result.Error = "Placement capability is incompatible"; return result; }
                var modifications = (JArray)document.Root["modifications"];
                var entries = new Dictionary<string, JObject>(StringComparer.Ordinal);
                foreach (var token in modifications) {
                    var item = token as JObject;
                    if (item == null || item["id"]?.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)item["id"]) || entries.ContainsKey((string)item["id"])) { result.Error = "Modification identities must be unique strings"; return result; }
                    entries.Add((string)item["id"], item);
                }
                sharedAdditions = SharedAdditionIds(entries.Values);
                var visiting = new HashSet<string>();
                foreach (var id in entries.Keys) {
                    if (nativeTarget != null && !EarlyRepair(entries[id], nativeTarget, out var unavailable)) {
                        if (unavailable != null) result.EarlyRouteUnavailable = unavailable;
                        continue;
                    }
                    Execute(id, entries, outcomes, visiting, documentKey, inspectedTargets);
                }
            } catch (Exception e) { result.Error = "Scene check failed: " + e.Message; }
            return result;
        }
        bool EarlyRepair(JObject entry, TargetIdentity target, out string unavailable) {
            unavailable = null;
            try {
                var parsed = Parse(entry); var identity = parsed.Identity;
                if (identity.Scene != target.Scene || identity.Path != target.Path || identity.Component != target.Component ||
                    identity.Structure != null && identity.Structure != target.Structure || parsed.State.Marker?.Location == null) return false;
                if (parsed.Dependencies.Length != 0 || parsed.State.Placement != null || parsed.State.Active != null) {
                    unavailable = "Operation " + (string)entry["id"] + " requires world-load scheduling for dependencies, placement or activity";
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
        Outcome Execute(string id, Dictionary<string,JObject> entries, Dictionary<string,Outcome> outcomes, HashSet<string> visiting, string documentKey, IReadOnlyDictionary<string, InspectedTarget> inspectedTargets) {
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
                    outcome.Cause = previous.Cause; outcome.EditorOnly = previous.EditorOnly; outcome.Target = previous.Target;
                    outcomes[id] = outcome;
                    return outcome;
                }
                var item = entries[id];
                if (!Only(item, Known)) { outcome.EditorOnly = true; throw new InvalidOperationException("Unsupported modification content"); }
                var shared = AdditionIds(item).FirstOrDefault(sharedAdditions.Contains);
                if (shared != null) throw new InvalidPatchDefinitionException(SharedAddition(shared));
                string[] dependencies;
                try { dependencies = Dependencies(item); }
                catch (InvalidOperationException e) { throw new InvalidPatchDefinitionException(e.Message); }
                foreach (var dep in dependencies) {
                    if (!entries.ContainsKey(dep)) throw new InvalidPatchDefinitionException("Dependency failed: " + dep);
                    var dependency = Execute(dep, entries, outcomes, visiting, documentKey, inspectedTargets);
                    if (!dependency.Applied) { outcome.Cause = dependency.Cause; outcome.EditorOnly = dependency.EditorOnly; throw new InvalidOperationException("Dependency failed: " + dep); }
                }
                Modification parsed;
                try { outcome.Target = ParseTarget(item["target"] as JObject); parsed = Parse(item, documentKey); }
                catch (InvalidOperationException e) {
                    outcome.Cause = FailureCause.InvalidDefinition;
                    outcome.EditorOnly = e.Message.StartsWith("Unsupported", StringComparison.Ordinal) || e.Message == "Invalid or unsupported target";
                    throw;
                }
                var identity = parsed.Identity;
                outcome.Target = identity;
                Observe("resolve", documentKey, id, identity.Path);
                var matches = scene.Resolve(identity);
                if (matches == null || matches.Count != 1 || matches[0] == null) { outcome.Cause = FailureCause.InvalidDefinition; throw new InvalidOperationException("Target must resolve uniquely"); }
                var resolved = matches[0];
                var mapContext = scene.MapIds.ToArray();
                InspectedTarget expected = null;
                if (inspectedTargets != null && (!inspectedTargets.TryGetValue(id, out expected) || expected == null))
                    throw new InvalidOperationException("An inspected target is required for every edited modification");
                expected?.Validate(scene, identity, resolved);
                outcome.ResolvedTarget = resolved;
                if (IsUnsafe(resolved)) throw new InvalidOperationException("Target cleanup is unverified");
                Observe("preflight", documentKey, id, identity.Path);
                scene.Preflight(resolved);
                var state = Evaluate(parsed.State);
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
        internal object ResolveUnique(TargetIdentity identity) {
            var matches = scene.Resolve(identity);
            if (matches == null || matches.Count != 1 || matches[0] == null) throw new InvalidOperationException("Target must resolve uniquely");
            return matches[0];
        }
        // The immediate guard before any mutation: same prerequisites, same unique object, same map context,
        // and for an editor edit the same inspected observation.
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
        // The addition IDs a modification authors, and those that more than one modification of a document uses. An addition ID
        // names one addition of its supplying MapMod, so every modification that shares one fails, wherever it stands.
        internal static IEnumerable<string> AdditionIds(JObject item) =>
            ((item["relationships"] as JObject)?["crossingCoverage"] as JArray)?.OfType<JObject>().Where(c => c["addition"]?.Type == JTokenType.String).Select(c => (string)c["addition"]).Distinct(StringComparer.Ordinal) ?? Enumerable.Empty<string>();
        internal static HashSet<string> SharedAdditionIds(IEnumerable<JObject> items) {
            var seen = new HashSet<string>(StringComparer.Ordinal); var shared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items) foreach (var id in AdditionIds(item)) if (!seen.Add(id)) shared.Add(id);
            return shared;
        }
        internal static string SharedAddition(string id) => "Addition ID " + id + " is used by more than one modification; an addition ID names one addition in its document";
        // Underlying state overlaid with every effective modification of this document for the object. The owner is the key
        // of the document, which scopes the identities of its additions.
        internal ObjectState Desired(JArray modifications, object target, string owner = "") {
            var entries = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var item in modifications.OfType<JObject>()) if (item["id"]?.Type == JTokenType.String) entries[(string)item["id"]] = item;
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
            return new ObjectState(following.Evaluate(tracks.TrackPose(following.Anchor)), state.Active, state.Marker, state.Coverage);
        }
        Modification Effective(string id, Dictionary<string, JObject> entries, Dictionary<string, Modification> effective, List<Modification> order, Dictionary<Modification, object> targets, HashSet<string> visiting, string owner, HashSet<string> shared) {
            Modification known;
            if (effective.TryGetValue(id, out known)) return known;
            Modification result = null;
            if (visiting.Add(id)) {
                try {
                    var parsed = Parse(entries[id], owner);
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
            return dependencies.Select(x => (string)x).ToArray();
        }
        internal static readonly string[] Known = { "id", "target", "placement", "active", "relationships", "dependsOn" };
        // The owner is the key of the document the modification belongs to; it scopes the identities of its additions.
        internal static Modification Parse(JObject item, string owner = "") {
            if (!Only(item, Known)) throw new InvalidOperationException("Unsupported modification content");
            var dependencies = Dependencies(item);
            var identity = ParseTarget(item["target"] as JObject);
            Placement pose = null;
            if (item["placement"] != null) {
                var placement = item["placement"] as JObject;
                var mode = placement?["mode"]?.Type == JTokenType.String ? (string)placement["mode"] : null;
                if (mode == "fixed" && Only(placement, "mode", "position", "rotation")) pose = CheckPlacement(Numbers(placement["position"], 3), Numbers(placement["rotation"], 4));
                else if (mode == TrackFollowingMode && Only(placement, "mode", "track", "offset", "rotation")) pose = new Placement { Following = ParseFollowing(placement) };
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
            if (pose == null && item["active"] == null && marker == null && coverage == null) throw new InvalidOperationException("Unsupported placement");
            return new Modification { Id = (string)item["id"], Identity = identity, State = new ObjectState(pose, (bool?)item["active"], marker, coverage), Dependencies = dependencies };
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
        // Explicit crossing coverage: each listed circuit, named by its native island span, gets selected tracks or an explicit
        // detach. An omitted circuit keeps its coverage; an omitted crossingCoverage leaves every circuit untouched. An entry
        // named by addition is an authored addition: a circuit the patcher adds for its selected tracks, which exists only while
        // the document lists it.
        static CrossingCoverage ParseCoverage(JToken token) {
            var circuits = token as JArray;
            if (circuits == null || circuits.Count == 0) throw new InvalidOperationException("Invalid relationship: crossingCoverage must list at least one circuit");
            var parsed = new List<KeyValuePair<string, CircuitCoverage>>();
            foreach (var entry in circuits) {
                var circuit = entry as JObject;
                var addition = circuit?["addition"] != null;
                if (addition && (circuit["circuit"] != null || !RequiredString(circuit, "addition"))) throw new InvalidOperationException("Invalid relationship: an addition names its own ID and no circuit");
                if (circuit == null || (!addition && !RequiredString(circuit, "circuit"))) throw new InvalidOperationException("Invalid relationship: each crossingCoverage entry names its circuit by island span ID, or its addition by ID");
                var key = addition ? DetectionAddition.Key("", (string)circuit["addition"]) : (string)circuit["circuit"];
                if (!addition && DetectionAddition.Id(key) != null) throw new InvalidOperationException("Invalid relationship: " + key + " is not the ID of an island span");
                if (parsed.Any(p => p.Key == key)) throw new InvalidOperationException("Invalid relationship: " + (addition ? "addition " + (string)circuit["addition"] : "circuit " + key) + " is listed twice");
                if (addition && circuit["detach"] != null) throw new InvalidOperationException("Invalid relationship: an addition cannot be detached; omit it to delete it");
                if (circuit["detach"] != null) {
                    if (!Only(circuit, "circuit", "detach") || circuit["detach"].Type != JTokenType.Boolean || !(bool)circuit["detach"]) throw new InvalidOperationException("Invalid relationship: detach must be true and stand alone with its circuit");
                    parsed.Add(new KeyValuePair<string, CircuitCoverage>(key, CircuitCoverage.Detached));
                    continue;
                }
                if (!Only(circuit, addition ? "addition" : "circuit", "approachA", "island", "approachB")) throw new InvalidOperationException("Unsupported relationship content");
                var ranges = new SpanRange[3];
                for (var i = 0; i < 3; i++) {
                    var role = CircuitCoverage.Roles[i];
                    var range = circuit[role] as JObject;
                    if (range == null) throw new InvalidOperationException("Invalid relationship: " + (addition ? "addition " + (string)circuit["addition"] + " needs approachA, island and approachB" : "circuit " + key + " needs approachA, island and approachB, or an explicit detach"));
                    if (!Only(range, "from", "to")) throw new InvalidOperationException("Unsupported relationship content");
                    ranges[i] = new SpanRange(RangeEnd(range["from"]), RangeEnd(range["to"]));
                }
                parsed.Add(new KeyValuePair<string, CircuitCoverage>(key, addition ? CircuitCoverage.Added(ranges[0], ranges[1], ranges[2]) : CircuitCoverage.Tracks(ranges[0], ranges[1], ranges[2])));
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
