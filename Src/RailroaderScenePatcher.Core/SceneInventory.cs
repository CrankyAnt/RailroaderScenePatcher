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
    // A scene that can observe one object without discovering all of them. The observation reads the object itself now
    // and takes what other objects say about it, such as the references they hold, from the last discovery that ran.
    // It returns null when the object is gone or is no longer observed.
    public interface ITargetedInspectionScene : IInspectionScene {
        SceneObservation Observe(object handle, TargetIdentity identity);
    }
    // A scene that knows which observations belong with an object: the object itself, the observed parts of its
    // hierarchy and the objects this patcher keeps for it, such as the spans it owns for a crossing. An edit of the
    // object can change, add or remove those and no other observation.
    public interface IRelatedInspectionScene : ITargetedInspectionScene {
        // Every current observation that belongs with the object, the object's own included; null when it is gone.
        IEnumerable<SceneObservation> ObserveRelated(object handle, TargetIdentity identity);
        // Whether the object behind a handle still exists in the loaded scene. It reads nothing else.
        bool Present(object handle);
    }
    // The game keeps its world near a moving origin: it shifts the objects it registered for that by whole tiles and
    // subtracts the shift again to get a map position. An object it did not register stays where it is in the scene,
    // so the same subtraction would move its map position with every shift although nothing moved it.
    public static class MapOrigin {
        // The position that stays the same while the origin moves: the scene position less the origin's offset for an
        // object that moves with the origin, and the scene position itself for one that does not.
        public static double[] Position(IReadOnlyList<double> scenePosition, IReadOnlyList<double> originOffset, bool movesWithOrigin) {
            if (scenePosition == null) throw new ArgumentNullException(nameof(scenePosition));
            if (!movesWithOrigin) return scenePosition.ToArray();
            if (originOffset == null || originOffset.Count != scenePosition.Count) throw new ArgumentException("The origin offset does not fit the position", nameof(originOffset));
            return scenePosition.Select((value, index) => value - originOffset[index]).ToArray();
        }
    }
    // Whether an object is active as the scene was authored. An object is active in the game when it and every object
    // above it is switched on. The game also switches objects on and off while it runs: a culler deactivates the objects
    // on its list when the camera is far away. Such a switch is runtime state and says nothing about the authored scene.
    public static class AuthoredActivity {
        // chain lists the object and every object above it: whether it is switched on, and whether the game switches it
        // at runtime. The object counts as active when every object the game does not switch is switched on.
        public static bool Of(IEnumerable<KeyValuePair<bool, bool>> chain) {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            return chain.All(link => link.Value || link.Key);
        }
    }
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
        // State the game switches while it runs, such as the enabled flag of a culled child behaviour. It is shown, and
        // it is no part of what an inspection pins: a change here alone does not mark a snapshot stale.
        public IReadOnlyList<string> RuntimeState { get; }
        public SceneObservation(object handle, TargetIdentity identity, string family, bool active, IEnumerable<double> position,
            IEnumerable<double> rotation, IEnumerable<string> bindings, IEnumerable<string> dependencies, IEnumerable<string> problems, string operations,
            IEnumerable<string> runtimeState = null) {
            Handle = handle ?? throw new ArgumentNullException(nameof(handle));
            Identity = identity ?? throw new ArgumentNullException(nameof(identity)); Family = family; Active = active;
            Position = Array.AsReadOnly(position.ToArray()); Rotation = Array.AsReadOnly(rotation.ToArray());
            Bindings = Array.AsReadOnly(bindings.ToArray()); Dependencies = Array.AsReadOnly(dependencies.ToArray());
            Problems = Array.AsReadOnly(problems.ToArray()); Operations = operations;
            RuntimeState = Array.AsReadOnly((runtimeState ?? Array.Empty<string>()).ToArray());
        }
        // The game keeps positions relative to a moving origin in single precision, so the same map position reads a few
        // millimetres differently after the origin moved. A position within this distance on every axis is the same.
        public const double PositionTolerance = 0.02;
        bool SamePosition(SceneObservation other) {
            if (Position.Count != other.Position.Count) return false;
            for (var i = 0; i < Position.Count; i++) if (Math.Abs(Position[i] - other.Position[i]) > PositionTolerance) return false;
            return true;
        }
        // What differs from another observation of the same object, for the stale reason: the first differing part, and
        // for a list the first line that only one of the two has.
        public string Difference(SceneObservation other) {
            if (other == null) return "missing";
            if (Active != other.Active) return "activity";
            if (!SamePosition(other)) return "position";
            if (!Rotation.SequenceEqual(other.Rotation)) return "rotation";
            if (Family != other.Family) return "family";
            if (Operations != other.Operations) return "supported edits";
            foreach (var part in new[] { new KeyValuePair<string, IReadOnlyList<string>[]>("bindings", new[] { Bindings, other.Bindings }),
                new KeyValuePair<string, IReadOnlyList<string>[]>("dependencies", new[] { Dependencies, other.Dependencies }),
                new KeyValuePair<string, IReadOnlyList<string>[]>("problems", new[] { Problems, other.Problems }) }) {
                if (part.Value[0].SequenceEqual(part.Value[1])) continue;
                var line = part.Value[0].Except(part.Value[1]).Concat(part.Value[1].Except(part.Value[0])).FirstOrDefault() ?? "order";
                return part.Key + ": " + (line.Length > 160 ? line.Substring(0, 160) + "..." : line);
            }
            return "identity";
        }
        internal string Fingerprint => new JArray(Identity.Scene, Identity.Path, Identity.Component, Identity.Structure, Family, Active,
            new JArray(Rotation), new JArray(Bindings), new JArray(Dependencies), new JArray(Problems), Operations).ToString(Newtonsoft.Json.Formatting.None);
        public bool Matches(SceneObservation current) => current != null && ReferenceEquals(Handle, current.Handle) && Fingerprint == current.Fingerprint && SamePosition(current);
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
        SceneObservation[] checkNear;
        int checkSeen;
        public bool CheckInProgress => check != null;
        // The number of stepped passes that ended without a difference.
        public int CompletedPasses { get; private set; }
        // A pin for an edit whose whole-scene comparison ran as a stepped pass, so that it does not block: it requires a
        // pass that completed without a difference after passesBefore was read. Such a pass read the scene over several
        // frames, so it can refuse and cannot vouch for the target at this moment: the edit's first validation is taken
        // as done, and its guard reads the scene in one piece immediately before the mutation.
        // With validateEvery the first validation is not taken as done: every validation observes the target. That costs
        // a scene that can observe one object next to nothing, and it is what a use with a single validation needs.
        public InspectedTarget PinAfterPass(SceneObservation entry, int passesBefore, bool validateEvery = false) {
            if (IsStale) throw new InvalidOperationException("Inventory is stale (" + StaleReason + "); refresh and inspect again");
            if (CompletedPasses <= passesBefore) throw new InvalidOperationException("The scene comparison for this edit has not completed");
            if (!Entries.Contains(entry)) throw new InvalidOperationException("The inspected observation is not part of the current inventory; refresh and inspect again");
            return new InspectedTarget(maps, entry) { ComparedNow = !validateEvery };
        }
        // A pin without a comparison of the whole scene, for a caller that knows the snapshot was compared after the last
        // change it cannot account for: a refresh or a completed pass since the editor's history last changed by another
        // hand. The snapshot must not be stale, and every validation observes the target now. A scene that cannot
        // observe one object compares the whole scene instead, as Pin does.
        public InspectedTarget PinCompared(SceneObservation entry) {
            if (!(scene is ITargetedInspectionScene)) return Pin(entry);
            if (IsStale) throw new InvalidOperationException("Inventory is stale (" + StaleReason + "); refresh and inspect again");
            if (!Entries.Contains(entry)) throw new InvalidOperationException("The inspected observation is not part of the current inventory; refresh and inspect again");
            return new InspectedTarget(maps, entry);
        }
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
        // Replaces one entry by a new observation of the same object: the object an edit or a history step of this patcher
        // changed. Every other entry stays as it was compared. Returns the new entry, or null when the scene cannot
        // observe one object or the object is gone; the caller then refreshes the whole inventory.
        public SceneObservation Reobserve(SceneObservation entry) {
            if (entry == null || !(scene is ITargetedInspectionScene targeted)) return null;
            var index = -1;
            for (var i = 0; i < Entries.Count; i++) if (ReferenceEquals(Entries[i], entry)) { index = i; break; }
            if (index < 0) return null;
            var observed = targeted.Observe(entry.Handle, entry.Identity);
            if (observed == null) return null;
            // A pass belongs to the snapshot it started against.
            CancelFreshnessCheck();
            var entries = Entries.ToArray(); entries[index] = observed;
            Entries = Array.AsReadOnly(entries);
            return observed;
        }
        // Takes the new observations of everything that belongs with one object: the object an edit or a history step of
        // this patcher changed, the observed parts of its hierarchy and the objects the patcher keeps for it. Those are
        // replaced, the ones the change created are added, and entries whose object no longer exists are dropped. Every
        // other entry stays as it was compared. A scene that does not know what belongs with an object re-observes the
        // object alone. Returns the object's new entry, or null as Reobserve does.
        public SceneObservation ReobserveRelated(SceneObservation entry) {
            if (!(scene is IRelatedInspectionScene related)) return Reobserve(entry);
            if (entry == null || !Entries.Any(e => ReferenceEquals(e, entry))) return null;
            var current = related.ObserveRelated(entry.Handle, entry.Identity)?.Where(o => o != null).ToArray();
            var observed = current?.FirstOrDefault(o => SameObject(entry, o));
            if (observed == null) return null;
            // A pass belongs to the snapshot it started against.
            CancelFreshnessCheck();
            var entries = new List<SceneObservation>(); var taken = new HashSet<SceneObservation>();
            foreach (var old in Entries) {
                var now = current.FirstOrDefault(o => SameObject(old, o));
                if (now != null) { if (taken.Add(now)) entries.Add(now); }
                else if (related.Present(old.Handle)) entries.Add(old);
            }
            entries.AddRange(current.Where(o => !taken.Contains(o)));
            Entries = entries.AsReadOnly();
            return observed;
        }
        public InspectedTarget Pin(SceneObservation entry) {
            CheckFreshness();
            if (IsStale) throw new InvalidOperationException("Inventory is stale (" + StaleReason + "); refresh and inspect again");
            if (!Entries.Contains(entry)) throw new InvalidOperationException("The inspected observation is not part of the current inventory; refresh and inspect again");
            return new InspectedTarget(maps, entry);
        }
        // A pin for an edit that runs in the same call. The pin has just compared the whole scene with the snapshot, so
        // the edit's first validation, at resolution, reuses that comparison. Every later validation, including the guard
        // immediately before the mutation, reads the scene again.
        public InspectedTarget PinForEdit(SceneObservation entry) {
            var pinned = Pin(entry);
            pinned.ComparedNow = true;
            return pinned;
        }
        public void CheckFreshness() {
            if (IsStale) return;
            try {
                var current = scene.Discover().ToArray();
                Conclude(current.Length, Entries.Select(e => current.Count(e.Matches)).ToArray(), Entries.Select(e => current.FirstOrDefault(c => SameObject(e, c))).ToArray());
            } catch (Exception e) { MarkStale("the scene check failed: " + e.GetBaseException().Message); }
            if (IsStale) CancelFreshnessCheck();
        }
        // Stale when the map context changed, the number of observations differs, or an entry does not match exactly one
        // current observation.
        static bool SameObject(SceneObservation a, SceneObservation b) => ReferenceEquals(a.Handle, b.Handle) && a.Identity.Component == b.Identity.Component;
        void Conclude(int seen, int[] matches, SceneObservation[] near) {
            if (!maps.SequenceEqual(scene.MapIds)) { MarkStale("the map context changed"); return; }
            if (seen != Entries.Count) { MarkStale(seen + " observations instead of " + Entries.Count); return; }
            for (var i = 0; i < matches.Length; i++) if (matches[i] != 1) { MarkStale(Name(Entries[i]) + (matches[i] == 0 ? (near != null && near[i] != null ? " changed (" + Entries[i].Difference(near[i]) + ")" : " changed or is missing") : " is observed " + matches[i] + " times")); return; }
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
                    checkMatches = new int[Entries.Count]; checkNear = new SceneObservation[Entries.Count]; checkSeen = 0;
                    check = (scene is ISteppedInspectionScene stepped ? stepped.DiscoverInSteps() : scene.Discover()).GetEnumerator();
                }
                do {
                    if (!check.MoveNext()) {
                        Conclude(checkSeen, checkMatches, checkNear);
                        CancelFreshnessCheck();
                        if (!IsStale) CompletedPasses++;
                        return;
                    }
                    var current = check.Current;
                    if (current == null) continue;
                    // More observations than entries, or a second match of one entry, cannot become equal again.
                    if (++checkSeen > Entries.Count) MarkStale("more than the " + Entries.Count + " observations of the snapshot; first extra " + Name(current));
                    for (var i = 0; i < Entries.Count; i++) if (SameObject(Entries[i], current)) checkNear[i] = current;
                    for (var i = 0; i < Entries.Count; i++) if (Entries[i].Matches(current) && ++checkMatches[i] > 1) MarkStale(Name(Entries[i]) + " is observed " + checkMatches[i] + " times");
                } while (!IsStale && more());
            } catch (Exception e) { MarkStale("the scene check failed: " + e.GetBaseException().Message); }
            if (IsStale) CancelFreshnessCheck();
        }
        // Ends an unfinished pass without a conclusion.
        public void CancelFreshnessCheck() {
            var ended = check; check = null; checkMatches = null; checkNear = null;
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
        // Set by PinForEdit and consumed by the first validation.
        internal bool ComparedNow;
        public void Validate(IScene scene, TargetIdentity identity, object resolved) {
            // A capability that failed its game checks resolves to a placeholder; name that cause instead of a changed context.
            (scene as SceneCapabilities)?.CheckAvailable(resolved);
            if (!maps.SequenceEqual(scene.MapIds) || !ReferenceEquals(resolved, observed.Handle) ||
                identity.Scene != observed.Identity.Scene || identity.Path != observed.Identity.Path ||
                identity.Component != observed.Identity.Component || (identity.Structure != null && identity.Structure != observed.Identity.Structure) || !(scene is IInspectionScene inspection))
                throw new InvalidOperationException("Inspected target context changed; refresh and inspect again");
            if (ComparedNow) { ComparedNow = false; return; }
            // A scene that can observe one object reads the target now and compares it with the inspection. The engine's
            // guard checks beside this that the identity still resolves to this one object.
            if (inspection is ITargetedInspectionScene targeted) {
                if (!observed.Matches(targeted.Observe(observed.Handle, identity))) throw new InvalidOperationException("Inspected target changed; refresh and inspect again");
                return;
            }
            var matches = inspection.Discover().Where(e => e.Identity.Scene == identity.Scene && e.Identity.Path == identity.Path && e.Identity.Component == identity.Component).ToArray();
            if (matches.Length != 1 || !observed.Matches(matches[0])) throw new InvalidOperationException("Inspected target changed; refresh and inspect again");
        }
    }
}
