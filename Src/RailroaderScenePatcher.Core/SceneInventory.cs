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
    // A scene whose discovery can pause. The walk is the one Discover runs: it yields null wherever it can pause between
    // bounded units of work, and each observation as it is produced. Paused over several frames it reads the scene at
    // different times, so its result can show that the scene differs from a snapshot, never what the scene currently is.
    public interface ISteppedInspectionScene : IInspectionScene {
        IEnumerable<SceneObservation> DiscoverInSteps();
    }
    // The order of one discovery over the components of a scene, for a provider that implements DiscoverInSteps: search,
    // loaded check per component, field reads per owner, classification per component, then one observation per candidate.
    // Null is yielded after each of those units. A paused walk meets components that left the scene since the search. It
    // skips them, as a discovery that starts at that moment would not find them: the observations that depended on them
    // then differ, and a component no observation depends on changes nothing.
    public static class DiscoveryWalk {
        // read returns false when the owner walk stops: no further owner is read. family returns null for no candidate.
        public static IEnumerable<SceneObservation> Steps<T>(Func<IEnumerable<T>> find, Func<T, bool> loaded, Func<T, bool> read, Action afterReads,
            Func<T, string> family, Action beforeObservations, Func<T, string, SceneObservation> observe) {
            var found = find();
            yield return null;
            var all = new List<T>();
            foreach (var component in found) { if (loaded(component)) all.Add(component); yield return null; }
            foreach (var owner in all) {
                if (loaded(owner) && !read(owner)) break;
                yield return null;
            }
            afterReads();
            var candidates = new List<KeyValuePair<T, string>>();
            foreach (var component in all) {
                if (loaded(component)) { var name = family(component); if (name != null) candidates.Add(new KeyValuePair<T, string>(component, name)); }
                yield return null;
            }
            beforeObservations();
            yield return null;
            foreach (var candidate in candidates) if (loaded(candidate.Key)) yield return observe(candidate.Key, candidate.Value);
        }
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
        // Why the snapshot is stale: the first difference a check found, or its failure. Null while it is not stale.
        public string StaleReason { get; private set; } = "not yet scanned";
        public SceneInventory(IInspectionScene scene) { this.scene = scene ?? throw new ArgumentNullException(nameof(scene)); }
        // A stepped comparison of the scene with the snapshot, and how many scanned observations match each entry.
        IEnumerator<SceneObservation> check;
        int[] checkMatches;
        int checkSeen;
        public bool CheckInProgress => check != null;
        public void Refresh() {
            // A pass belongs to the snapshot it started against.
            CancelFreshnessCheck();
            var observations = scene.Discover().ToArray();
            maps = scene.MapIds.ToArray(); Entries = Array.AsReadOnly(observations); IsStale = false; StaleReason = null;
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
            if (IsStale) throw new InvalidOperationException("Inventory is stale (" + StaleReason + "); refresh and inspect again");
            if (!Entries.Contains(entry)) throw new InvalidOperationException("The inspected observation is not part of the current inventory; refresh and inspect again");
            return new InspectedTarget(maps, entry);
        }
        public void CheckFreshness() {
            if (IsStale) return;
            try {
                var current = scene.Discover().ToArray();
                Conclude(current.Length, Entries.Select(e => current.Count(e.Matches)).ToArray());
            } catch (Exception e) { MarkStale("the scene check failed: " + e.GetBaseException().Message); }
            if (IsStale) CancelFreshnessCheck();
        }
        // Stale when the map context changed, the number of observations differs, or an entry does not match exactly one
        // current observation.
        void Conclude(int seen, int[] matches) {
            if (!maps.SequenceEqual(scene.MapIds)) { MarkStale("the map context changed"); return; }
            if (seen != Entries.Count) { MarkStale(seen + " observations instead of " + Entries.Count); return; }
            for (var i = 0; i < matches.Length; i++) if (matches[i] != 1) { MarkStale(Name(Entries[i]) + (matches[i] == 0 ? " changed or is missing" : " is observed " + matches[i] + " times")); return; }
        }
        static string Name(SceneObservation entry) => entry.Identity.Scene + "/" + entry.Identity.Path + " [" + entry.Identity.Component + "]";
        void MarkStale(string reason) { if (!IsStale) StaleReason = reason; IsStale = true; }
        // Continues the stepped comparison, or starts one: at least one unit of the scan, then further units while more()
        // holds. A pass marks the snapshot stale on the difference CheckFreshness would find, or on a failure. It replaces
        // no observation, and a pass that finds no difference concludes nothing: the scene was read at different times.
        public void AdvanceFreshnessCheck(Func<bool> more) {
            if (more == null) throw new ArgumentNullException(nameof(more));
            if (IsStale) { CancelFreshnessCheck(); return; }
            try {
                if (check == null) {
                    if (!maps.SequenceEqual(scene.MapIds)) { MarkStale("the map context changed"); return; }
                    checkMatches = new int[Entries.Count]; checkSeen = 0;
                    check = (scene is ISteppedInspectionScene stepped ? stepped.DiscoverInSteps() : scene.Discover()).GetEnumerator();
                }
                do {
                    if (!check.MoveNext()) {
                        Conclude(checkSeen, checkMatches);
                        CancelFreshnessCheck();
                        return;
                    }
                    var current = check.Current;
                    if (current == null) continue;
                    // More observations than entries, or a second match of one entry, cannot become equal again.
                    if (++checkSeen > Entries.Count) MarkStale("more than the " + Entries.Count + " observations of the snapshot; first extra " + Name(current));
                    for (var i = 0; i < Entries.Count; i++) if (Entries[i].Matches(current) && ++checkMatches[i] > 1) MarkStale(Name(Entries[i]) + " is observed " + checkMatches[i] + " times");
                } while (!IsStale && more());
            } catch (Exception e) { MarkStale("the scene check failed: " + e.GetBaseException().Message); }
            if (IsStale) CancelFreshnessCheck();
        }
        // Ends an unfinished pass without a conclusion.
        public void CancelFreshnessCheck() {
            var ended = check; check = null; checkMatches = null;
            try { ended?.Dispose(); } catch { }
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
