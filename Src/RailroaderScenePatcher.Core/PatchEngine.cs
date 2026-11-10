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
        public double[] Position { get; internal set; }
        public double[] Rotation { get; internal set; }
    }
    public interface IScene {
        IEnumerable<string> MapIds { get; }
        Capability Capability { get; }
        IReadOnlyList<object> Resolve(TargetIdentity identity);
        void Preflight(object target);
        object Capture(object target);
        void Apply(object target, Placement placement);
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
                foreach (var id in entries.Keys) Execute(id, entries, outcomes, visiting, documentKey);
            } catch (Exception e) { result.Error = "Scene check failed: " + e.Message; }
            return result;
        }
        Outcome Execute(string id, Dictionary<string,JObject> entries, Dictionary<string,Outcome> outcomes, HashSet<string> visiting, string documentKey) {
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
                if (!Only(item, "id", "target", "placement", "dependsOn")) throw new InvalidOperationException("Unsupported modification content");
                var dependencies = item["dependsOn"] as JArray;
                if (dependencies == null || dependencies.Any(x => x.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)x))) throw new InvalidOperationException("Invalid dependencies");
                foreach (var dependency in dependencies) {
                    var dep = (string)dependency;
                    if (!entries.ContainsKey(dep) || !Execute(dep, entries, outcomes, visiting, documentKey).Applied) throw new InvalidOperationException("Dependency failed: " + dep);
                }
                var target = item["target"] as JObject; var placement = item["placement"] as JObject;
                if (target == null || !Only(target, "scene", "path", "component", "structure") || !RequiredString(target,"scene") || !RequiredString(target,"path") || !RequiredString(target,"component") || (target["structure"] != null && target["structure"].Type != JTokenType.String)) throw new InvalidOperationException("Invalid or unsupported target");
                if (placement == null || !Only(placement, "mode", "position", "rotation") || (string)placement["mode"] != "fixed") throw new InvalidOperationException("Unsupported placement");
                var pose = new Placement { Position = Numbers(placement["position"], 3), Rotation = Numbers(placement["rotation"], 4) };
                if (pose.Rotation.All(x => x == 0)) throw new InvalidOperationException("Zero rotation quaternion");
                var identity = new TargetIdentity { Scene=(string)target["scene"], Path=(string)target["path"], Component=(string)target["component"], Structure=(string)target["structure"] };
                Observe("resolve", documentKey, id, identity.Path);
                var matches = scene.Resolve(identity);
                if (matches == null || matches.Count != 1 || matches[0] == null) throw new InvalidOperationException("Target must resolve uniquely");
                var resolved = matches[0];
                outcome.ResolvedTarget = resolved;
                if (unsafeTargets.Contains(resolved)) throw new InvalidOperationException("Target cleanup is unverified");
                Observe("preflight", documentKey, id, identity.Path);
                scene.Preflight(resolved);
                Observe("capture", documentKey, id, identity.Path);
                var snapshot = scene.Capture(resolved);
                try {
                    Observe("mutation", documentKey, id, identity.Path);
                    scene.Apply(resolved, pose);
                    Observe("applied", documentKey, id, identity.Path);
                } catch (Exception e) {
                    bool restored = false;
                    Observe("restore-request", documentKey, id, identity.Path);
                    try { restored = scene.Restore(resolved, snapshot); } catch { }
                    Observe("restore-result", documentKey, id, identity.Path, restored ? "verified" : "unverified");
                    if (!restored) unsafeTargets.Add(resolved);
                    throw new InvalidOperationException(restored ? "Mutation failed and was restored: " + e.Message : "Mutation failed; cleanup is unverified: " + e.Message);
                }
                outcome.Applied = true;
            } catch (Exception e) { outcome.Error = e.Message; failed.Add(key); Observe("failed", documentKey, id, error: e.Message); }
            finally { visiting.Remove(id); }
            outcomes[id] = outcome;
            return outcome;
        }
        static bool RequiredString(JObject obj, string name) { return obj[name]?.Type == JTokenType.String && !String.IsNullOrWhiteSpace((string)obj[name]); }
        static bool Only(JObject obj, params string[] allowed) { return obj.Properties().All(p => allowed.Contains(p.Name, StringComparer.Ordinal)); }
        static double[] Numbers(JToken token, int count) {
            var a = token as JArray;
            if (a == null || a.Count != count || a.Any(x => x.Type != JTokenType.Integer && x.Type != JTokenType.Float)) throw new InvalidOperationException("Invalid placement coordinates");
            var values = a.Select(x => (double)x).ToArray();
            if (values.Any(x => Double.IsNaN(x) || Double.IsInfinity(x))) throw new InvalidOperationException("Nonfinite placement coordinates");
            return values;
        }
    }
}
