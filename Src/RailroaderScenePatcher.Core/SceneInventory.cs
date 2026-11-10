// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    public interface IInspectionScene {
        IEnumerable<string> MapIds { get; }
        IEnumerable<SceneObservation> Discover();
    }
    // Observations contain copied values. The handle binds an observation to this load's object.
    public sealed class SceneObservation {
        public object Handle { get; }
        public TargetIdentity Identity { get; }
        public string Family { get; }
        public bool Active { get; }
        public IReadOnlyList<double> Position { get; }
        public IReadOnlyList<double> Rotation { get; }
        public IReadOnlyList<string> Bindings { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public IReadOnlyList<string> Problems { get; }
        public string Operations { get; }
        public SceneObservation(object handle, TargetIdentity identity, string family, bool active, IEnumerable<double> position,
            IEnumerable<double> rotation, IEnumerable<string> bindings, IEnumerable<string> dependencies, IEnumerable<string> problems, string operations) {
            Handle = handle ?? throw new ArgumentNullException(nameof(handle));
            Identity = identity ?? throw new ArgumentNullException(nameof(identity)); Family = family; Active = active;
            Position = Array.AsReadOnly(position.ToArray()); Rotation = Array.AsReadOnly(rotation.ToArray());
            Bindings = Array.AsReadOnly(bindings.ToArray()); Dependencies = Array.AsReadOnly(dependencies.ToArray());
            Problems = Array.AsReadOnly(problems.ToArray()); Operations = operations;
        }
        internal string Fingerprint => new JArray(Identity.Scene, Identity.Path, Identity.Component, Identity.Structure, Family, Active,
            new JArray(Position), new JArray(Rotation), new JArray(Bindings), new JArray(Dependencies), new JArray(Problems), Operations).ToString(Newtonsoft.Json.Formatting.None);
        public bool Matches(SceneObservation current) => current != null && ReferenceEquals(Handle, current.Handle) && Fingerprint == current.Fingerprint;
        internal string SearchText => String.Join(" ", new[] { Identity.Scene, Identity.Path, Identity.Component, Identity.Structure, Family,
            Active ? "active" : "inactive", Operations }.Concat(Bindings).Concat(Dependencies).Concat(Problems));
    }
    public sealed class SceneInventory {
        readonly IInspectionScene scene;
        string[] maps = Array.Empty<string>();
        public IReadOnlyList<SceneObservation> Entries { get; private set; } = Array.Empty<SceneObservation>();
        public bool IsStale { get; private set; } = true;
        public SceneInventory(IInspectionScene scene) { this.scene = scene ?? throw new ArgumentNullException(nameof(scene)); }
        public void Refresh() {
            var observations = scene.Discover().ToArray();
            maps = scene.MapIds.ToArray(); Entries = Array.AsReadOnly(observations); IsStale = false;
        }
        public IEnumerable<SceneObservation> Search(string text) => Entries.Where(e => e.SearchText.IndexOf(text ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
        public IReadOnlyList<DefinitionInspection> InspectDocument(string json) {
            var document = PatchDocument.Load(json);
            if (!document.CanWrite) return new[] { new DefinitionInspection(null, 0, Array.Empty<string>(), new[] { document.Error }) };
            if (!maps.Contains((string)document.Root["sourceMap"]["mapId"], StringComparer.Ordinal))
                return new[] { new DefinitionInspection(null, 0, Array.Empty<string>(), new[] { "Map context mismatch; targets were not resolved" }) };
            var entries = ((JArray)document.Root["modifications"]).Cast<JObject>().ToDictionary(e => (string)e["id"], StringComparer.Ordinal);
            var results = new List<DefinitionInspection>();
            foreach (var item in entries.Values) {
                var problems = new List<string>(); var matches = 0; var id = (string)item["id"];
                var deps = item["dependsOn"] as JArray;
                if (deps == null || deps.Any(d => d.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)d))) problems.Add("Invalid modification dependencies");
                var dependencies = deps == null ? Array.Empty<string>() : deps.Where(d => d.Type == JTokenType.String).Select(d => (string)d).ToArray();
                foreach (var dependency in dependencies) if (!entries.ContainsKey(dependency)) problems.Add("Missing dependency " + dependency);
                var target = item["target"] as JObject;
                if (target == null || new[] { "scene", "path", "component" }.Any(k => target[k]?.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)target[k])) ||
                    (target["structure"] != null && target["structure"].Type != JTokenType.String)) problems.Add("Invalid target identity");
                else {
                    matches = Entries.Count(e => e.Identity.Scene == (string)target["scene"] && e.Identity.Path == (string)target["path"] &&
                        e.Identity.Component == (string)target["component"] && (target["structure"] == null || e.Identity.Structure == (string)target["structure"]));
                    if (matches != 1) problems.Add(matches == 0 ? "Target is missing or has no inspection adapter" : "Target is ambiguous");
                }
                results.Add(new DefinitionInspection(id, matches, dependencies, problems));
            }
            return results.AsReadOnly();
        }
        public InspectedTarget Pin(SceneObservation entry) {
            CheckFreshness();
            if (IsStale || !Entries.Contains(entry)) throw new InvalidOperationException("Inventory is stale; refresh and inspect again");
            return new InspectedTarget(maps, entry);
        }
        public void CheckFreshness() {
            if (IsStale) return;
            try {
                var current = scene.Discover().ToArray();
                IsStale = !maps.SequenceEqual(scene.MapIds) || current.Length != Entries.Count ||
                    Entries.Any(e => current.Count(e.Matches) != 1);
            } catch { IsStale = true; }
        }
    }
    public sealed class DefinitionInspection {
        public string Id { get; }
        public int MatchCount { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public IReadOnlyList<string> Problems { get; }
        internal DefinitionInspection(string id, int matches, IEnumerable<string> dependencies, IEnumerable<string> problems) {
            Id = id; MatchCount = matches; Dependencies = Array.AsReadOnly(dependencies.ToArray()); Problems = Array.AsReadOnly(problems.ToArray());
        }
    }
    public sealed class InspectedTarget {
        readonly string[] maps;
        readonly SceneObservation observed;
        internal InspectedTarget(string[] maps, SceneObservation observed) { this.maps = (string[])maps.Clone(); this.observed = observed; }
        public void Validate(IScene scene, TargetIdentity identity, object resolved) {
            if (!maps.SequenceEqual(scene.MapIds) || !ReferenceEquals(resolved, observed.Handle) ||
                identity.Scene != observed.Identity.Scene || identity.Path != observed.Identity.Path ||
                identity.Component != observed.Identity.Component || (identity.Structure != null && identity.Structure != observed.Identity.Structure) || !(scene is IInspectionScene inspection))
                throw new InvalidOperationException("Inspected target context changed; refresh and inspect again");
            var matches = inspection.Discover().Where(e => e.Identity.Scene == identity.Scene && e.Identity.Path == identity.Path && e.Identity.Component == identity.Component).ToArray();
            if (matches.Length != 1 || !observed.Matches(matches[0])) throw new InvalidOperationException("Inspected target changed; refresh and inspect again");
        }
    }
}
