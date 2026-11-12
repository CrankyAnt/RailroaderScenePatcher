// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RailroaderScenePatcher.Core {
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
    }
    // Desired or observed state of one object. A null member is left untouched by an application.
    public sealed class ObjectState {
        public ObjectState(Placement placement, bool? active) : this(placement, active, null) { }
        public ObjectState(Placement placement, bool? active, MarkerBinding marker) { Placement = placement; Active = active; Marker = marker; }
        public Placement Placement { get; }
        public bool? Active { get; }
        // The track-marker relationship: a location or explicit detachment. Null leaves it untouched, and an object without one reads null.
        public MarkerBinding Marker { get; }
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
    }
    public sealed class DocumentResult {
        public string Error { get; internal set; }
        public IReadOnlyDictionary<string, Outcome> Outcomes { get; internal set; }
    }
    public sealed class PatchEngine {
        readonly IScene scene;
        readonly HashSet<string> failed = new HashSet<string>();
        readonly HashSet<object> unsafeTargets = new HashSet<object>();
        // Restored targets whose dependent output, such as regenerated terrain, has not yet verified.
        readonly HashSet<object> awaitingOutput = new HashSet<object>();
        readonly Dictionary<object, ObjectState> underlying = new Dictionary<object, ObjectState>();
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
            if (documentKey == null) throw new ArgumentNullException(nameof(documentKey));
            Observe("document-dispatch", documentKey);
            var result = new DocumentResult();
            var outcomes = new Dictionary<string, Outcome>(); result.Outcomes = outcomes;
            var document = PatchDocument.Load(json);
            if (!document.CanWrite) { result.Error = document.Error; return result; }
            try {
                if (!scene.MapIds.Contains((string)document.Root["sourceMap"]["mapId"], StringComparer.Ordinal)) { result.Error = "Source map does not match loaded map context"; return result; }
                if (scene.Capability == Capability.Incompatible) { result.Error = "Placement capability is incompatible"; return result; }
                var modifications = (JArray)document.Root["modifications"];
                var entries = new Dictionary<string, JObject>(StringComparer.Ordinal);
                foreach (var token in modifications) {
                    var item = token as JObject;
                    if (item == null || item["id"]?.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)item["id"]) || entries.ContainsKey((string)item["id"])) { result.Error = "Modification identities must be unique strings"; return result; }
                    entries.Add((string)item["id"], item);
                }
                var visiting = new HashSet<string>();
                foreach (var id in entries.Keys) Execute(id, entries, outcomes, visiting, documentKey, inspectedTargets);
            } catch (Exception e) { result.Error = "Scene check failed: " + e.Message; }
            return result;
        }
        Outcome Execute(string id, Dictionary<string,JObject> entries, Dictionary<string,Outcome> outcomes, HashSet<string> visiting, string documentKey, IReadOnlyDictionary<string, InspectedTarget> inspectedTargets) {
            Outcome prior;
            if (outcomes.TryGetValue(id, out prior)) return prior;
            var outcome = new Outcome();
            if (!visiting.Add(id)) { outcome.Error = "Dependency cycle"; return outcome; }
            var key = documentKey.Length + ":" + documentKey + id;
            try {
                if (failed.Contains(key)) {
                    Observe("retry-blocked", documentKey, id);
                    throw new InvalidOperationException("Operation already failed during this load");
                }
                var item = entries[id];
                if (!Only(item, Known)) throw new InvalidOperationException("Unsupported modification content");
                foreach (var dep in Dependencies(item))
                    if (!entries.ContainsKey(dep) || !Execute(dep, entries, outcomes, visiting, documentKey, inspectedTargets).Applied) throw new InvalidOperationException("Dependency failed: " + dep);
                var parsed = Parse(item);
                var identity = parsed.Identity;
                Observe("resolve", documentKey, id, identity.Path);
                var matches = scene.Resolve(identity);
                if (matches == null || matches.Count != 1 || matches[0] == null) throw new InvalidOperationException("Target must resolve uniquely");
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
                CheckRelationships(resolved, parsed.State);
                Observe("capture", documentKey, id, identity.Path);
                Underlying(resolved);
                var snapshot = scene.Capture(resolved);
                // Capture and preflight may invoke native callbacks. Never redirect to a new match.
                GuardMutation(identity, resolved, mapContext, expected);
                try {
                    Observe("mutation", documentKey, id, identity.Path);
                    scene.Apply(resolved, parsed.State);
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
            } catch (Exception e) { outcome.Error = e.Message; failed.Add(key); Observe("failed", documentKey, id, error: e.Message); }
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
        internal void CheckRelationships(object target, ObjectState state) {
            if (state?.Marker == null) return;
            if (!(scene is IRelationshipScene relationships)) throw new InvalidOperationException("This object has no supported track-marker relationship");
            relationships.CheckRelationships(target, state);
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
        // Underlying state overlaid with every effective modification of this document for the object.
        internal ObjectState Desired(JArray modifications, object target) {
            var entries = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var item in modifications.OfType<JObject>()) if (item["id"]?.Type == JTokenType.String) entries[(string)item["id"]] = item;
            var effective = new Dictionary<string, Modification>(StringComparer.Ordinal);
            var order = new List<Modification>(); var targets = new Dictionary<Modification, object>();
            foreach (var id in entries.Keys) Effective(id, entries, effective, order, targets, new HashSet<string>());
            var state = Underlying(target);
            foreach (var item in order) {
                if (!ReferenceEquals(targets[item], target)) continue;
                state = new ObjectState(item.State.Placement ?? state.Placement, item.State.Active ?? state.Active, item.State.Marker ?? state.Marker);
            }
            return state;
        }
        Modification Effective(string id, Dictionary<string, JObject> entries, Dictionary<string, Modification> effective, List<Modification> order, Dictionary<Modification, object> targets, HashSet<string> visiting) {
            Modification known;
            if (effective.TryGetValue(id, out known)) return known;
            Modification result = null;
            if (visiting.Add(id)) {
                try {
                    var parsed = Parse(entries[id]);
                    if (parsed.Dependencies.All(d => entries.ContainsKey(d) && Effective(d, entries, effective, order, targets, visiting) != null)) {
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
        internal static Modification Parse(JObject item) {
            if (!Only(item, Known)) throw new InvalidOperationException("Unsupported modification content");
            var dependencies = Dependencies(item);
            var identity = ParseTarget(item["target"] as JObject);
            Placement pose = null;
            if (item["placement"] != null) {
                var placement = item["placement"] as JObject;
                if (placement == null || !Only(placement, "mode", "position", "rotation") || placement["mode"]?.Type != JTokenType.String || (string)placement["mode"] != "fixed") throw new InvalidOperationException("Unsupported placement");
                pose = CheckPlacement(Numbers(placement["position"], 3), Numbers(placement["rotation"], 4));
            }
            if (item["active"] != null && item["active"].Type != JTokenType.Boolean) throw new InvalidOperationException("Invalid activity");
            var marker = item["relationships"] == null ? null : ParseRelationships(item["relationships"]);
            if (pose == null && item["active"] == null && marker == null) throw new InvalidOperationException("Unsupported placement");
            return new Modification { Id = (string)item["id"], Identity = identity, State = new ObjectState(pose, (bool?)item["active"], marker), Dependencies = dependencies };
        }
        // An explicit relationship setting: a reference sets or replaces it, detach clears it. An omitted relationship stays untouched.
        static MarkerBinding ParseRelationships(JToken token) {
            var relationships = token as JObject;
            if (relationships == null || !Only(relationships, "trackMarker")) throw new InvalidOperationException("Unsupported relationships");
            var marker = relationships["trackMarker"] as JObject;
            if (marker == null) throw new InvalidOperationException("Unsupported relationships: trackMarker needs a location or an explicit detach");
            if (marker["detach"] != null) {
                if (!Only(marker, "detach") || marker["detach"].Type != JTokenType.Boolean || !(bool)marker["detach"]) throw new InvalidOperationException("Invalid relationship: detach must be true and stand alone");
                return MarkerBinding.Detached;
            }
            if (!Only(marker, "segment", "end", "distance")) throw new InvalidOperationException("Unsupported relationship content");
            if (!RequiredString(marker, "segment")) throw new InvalidOperationException("Invalid relationship: segment must name a track segment");
            if (marker["end"]?.Type != JTokenType.String || ((string)marker["end"] != "a" && (string)marker["end"] != "b")) throw new InvalidOperationException("Invalid relationship: end must be \"a\" or \"b\"");
            if (marker["distance"] == null || (marker["distance"].Type != JTokenType.Integer && marker["distance"].Type != JTokenType.Float)) throw new InvalidOperationException("Invalid relationship: distance must be a number");
            var distance = (double)marker["distance"];
            if (Double.IsNaN(distance) || Double.IsInfinity(distance) || distance < 0) throw new InvalidOperationException("Invalid relationship: distance must be finite and not negative");
            return MarkerBinding.At(new TrackLocation((string)marker["segment"], (string)marker["end"], distance));
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
