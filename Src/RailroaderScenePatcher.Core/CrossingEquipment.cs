// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // A device already loaded in the selected map, named by its Unity scene and hierarchy path. The patcher never loads
    // another scene to find it and never substitutes a different object.
    public sealed class EquipmentSource {
        public EquipmentSource(string scene, string path) {
            if (String.IsNullOrWhiteSpace(scene) || String.IsNullOrWhiteSpace(path)) throw new ArgumentException("A source needs a scene and a path");
            Scene = scene; Path = path;
        }
        public string Scene { get; }
        public string Path { get; }
        internal static bool Same(EquipmentSource a, EquipmentSource b) => a == null ? b == null : b != null && a.Scene == b.Scene && a.Path == b.Path;
        public override string ToString() => Scene + "/" + Path;
    }
    // One piece of crossing equipment a mod adds to an existing crossing controller: a copy of an eligible source built
    // with the required pattern and addressed by that controller. Its ID is persistent and unique within the supplying
    // mod's scene patch file; the scope is that mod's identifier, so two mods may use the same ID.
    public sealed class EquipmentAddition {
        public const string LightGatePattern = "crossing-light-gate-v1";
        public static readonly string[] Patterns = { LightGatePattern };
        public EquipmentAddition(string id, string pattern, EquipmentSource source, Placement placement) : this(id, pattern, source, placement, "") { }
        public EquipmentAddition(string id, string pattern, EquipmentSource source, Placement placement, string scope) {
            var problem = IdProblem(id);
            if (problem != null) throw new ArgumentException(problem, nameof(id));
            Id = id; Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern)); Source = source ?? throw new ArgumentNullException(nameof(source));
            Placement = placement; Scope = scope ?? "";
        }
        public string Id { get; }
        public string Pattern { get; }
        public EquipmentSource Source { get; }
        // The fixed placement of the copy's root in map coordinates.
        public Placement Placement { get; }
        // The supplying mod's identifier.
        public string Scope { get; }
        internal string Key => Scope + "\n" + Id;
        internal EquipmentAddition In(string scope) => new EquipmentAddition(Id, Pattern, Source, Placement, scope);
        internal EquipmentAddition At(Placement placement) => new EquipmentAddition(Id, Pattern, Source, placement, Scope);
        // Null when the ID can name an addition: 1 to 64 letters, digits, '.', '_' or '-'.
        public static string IdProblem(string id) {
            if (String.IsNullOrEmpty(id) || id.Length > 64 || !id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-'))
                return "An equipment ID needs 1 to 64 letters, digits, '.', '_' or '-'";
            return null;
        }
        internal static bool SameBuild(EquipmentAddition a, EquipmentAddition b) => a.Key == b.Key && a.Pattern == b.Pattern && EquipmentSource.Same(a.Source, b.Source);
        internal static bool Same(EquipmentAddition a, EquipmentAddition b) => SameBuild(a, b) && SamePlacement(a.Placement, b.Placement);
        // The placement tolerance of scene patch editing: half a millimetre on each axis and about a hundredth of a degree,
        // so every move the native map editor stores reaches the scene.
        internal static bool SamePlacement(Placement a, Placement b) =>
            a?.Position != null && b?.Position != null && PatchEditingSession.SamePosition(a.Position, b.Position) && PatchEditingSession.SameRotation(a.Rotation, b.Rotation);
        public override string ToString() {
            var p = Placement?.Position;
            return (Scope.Length == 0 ? "" : Scope + " ") + Id + ": " + Pattern + " from " + Source + (p == null ? "" : " at (" + String.Join(", ", p.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))) + ")");
        }
    }
    // The equipment additions of one crossing. A mod's set lists only its explicit additions; an addition it omits stays
    // untouched. A complete set lists every addition the controller should own, so an addition absent from it is removed.
    public sealed class CrossingEquipment {
        readonly List<EquipmentAddition> additions;
        public CrossingEquipment(IEnumerable<EquipmentAddition> additions, bool complete) {
            this.additions = (additions ?? throw new ArgumentNullException(nameof(additions))).ToList();
            if (this.additions.Any(a => a == null)) throw new ArgumentException("Every addition is required");
            if (this.additions.Select(a => a.Key).Distinct(StringComparer.Ordinal).Count() != this.additions.Count) throw new ArgumentException("Equipment IDs must be unique within their mod");
            Complete = complete;
        }
        public IReadOnlyList<EquipmentAddition> Additions => additions;
        public bool Complete { get; }
        internal CrossingEquipment In(string scope) => new CrossingEquipment(additions.Select(a => a.In(scope)), Complete);
        internal CrossingEquipment Without(string scope) => new CrossingEquipment(additions.Where(a => a.Scope != scope), Complete);
        internal EquipmentAddition Find(string key) => additions.FirstOrDefault(a => a.Key == key);
        // The mod's equipment additions laid over a base set: each mod addition replaces its namesake or is added.
        internal static CrossingEquipment Overlay(CrossingEquipment basis, CrossingEquipment modEquipment) {
            if (modEquipment == null) return basis;
            if (basis == null) return modEquipment;
            var result = basis.additions.Select(a => modEquipment.Find(a.Key) ?? a).ToList();
            result.AddRange(modEquipment.additions.Where(a => basis.Find(a.Key) == null));
            return new CrossingEquipment(result, basis.Complete);
        }
        internal static bool Same(CrossingEquipment a, CrossingEquipment b) {
            if (a == null || b == null) return a == b;
            return a.additions.Count == b.additions.Count && a.additions.All(x => b.Find(x.Key) is EquipmentAddition y && EquipmentAddition.Same(x, y));
        }
        // One line per addition: what changes first, as it is built, rebuilt, moved or removed, then what is kept.
        internal static IReadOnlyList<string> Describe(CrossingEquipment current, CrossingEquipment desired) {
            if (current == null || desired == null) throw new InvalidOperationException("This object has no crossing equipment");
            var changes = new List<string>(); var kept = new List<string>();
            foreach (var want in desired.additions) {
                var now = current.Find(want.Key);
                if (now == null) changes.Add("Add " + want + "; the crossing controller drives it.");
                else if (!EquipmentAddition.SameBuild(now, want)) changes.Add("Rebuild " + want + "; the copy of " + now.Source + " is removed.");
                else if (!EquipmentAddition.SamePlacement(now.Placement, want.Placement)) changes.Add("Move " + want + ".");
                else kept.Add("Keep " + want + ".");
            }
            foreach (var gone in current.additions.Where(a => desired.Find(a.Key) == null)) changes.Add("Remove " + gone + "; its copy is released from the controller and its culling.");
            return changes.Concat(kept).ToList();
        }
        // The place a crossing stands at, for a label: the part after "Grade Crossing - " of its own name, or else the
        // nearest enclosing object that names a place, without a leading "Roads ". ancestors run from the parent upwards.
        public static string Place(string crossing, IReadOnlyList<string> ancestors) {
            const string Lead = "Grade Crossing - ";
            if (crossing != null && crossing.StartsWith(Lead, StringComparison.Ordinal) && crossing.Length > Lead.Length) return crossing.Substring(Lead.Length);
            foreach (var name in ancestors ?? Array.Empty<string>()) {
                if (String.IsNullOrWhiteSpace(name) || name == "World" || name == "Large Scenery" || name == "Roads") continue;
                return name.StartsWith("Roads ", StringComparison.Ordinal) ? name.Substring("Roads ".Length) : name;
            }
            return crossing;
        }
    }
    // What the light/gate pattern reads from one lamp: its key, its targets and its lamp materials.
    public sealed class LampFacts {
        public string Name = "lamp", Key;
        public int Targets, MissingTargets, OutsideTargets, Materials, MaterialsWithoutProperty;
        // Some renderer under the targets uses one of its lamp materials.
        public bool RendererUsesMaterial;
    }
    // What the light/gate pattern reads from one gate: its key, its Animator and its clip.
    public sealed class GateFacts {
        public string Name = "gate", Key;
        public bool Animator, Clip;
    }
    // What the light/gate pattern reads from a candidate source and its destination crossing controller. The native world
    // fills it from the device's components; the rules that judge it are LightGatePattern's.
    public sealed class DeviceFacts {
        public bool AddedByPatcher, MissingScript, ContainsDestination, OtherScene;
        public int Controllers, KeyValueObjects, Spans, Cullers, Bells;
        public int DestinationKeyValueObjects = 1, DestinationCullers = 1;
        public List<LampFacts> Lamps = new List<LampFacts>();
        public List<GateFacts> Gates = new List<GateFacts>();
    }
    // The crossing-light-gate-v1 pattern: a device whose lamps and gates observe the keys the crossing controller publishes
    // on its key-value object, with everything a copy needs to work beneath that controller. Model, material and animation
    // values do not matter; the mechanisms do.
    public static class LightGatePattern {
        // Why a copy of the device beneath the destination controller would not work, or null. keys are the lights and gates
        // keys the controller publishes.
        public static string Problem(DeviceFacts d, IReadOnlyList<string> keys) {
            if (d == null) throw new ArgumentNullException(nameof(d));
            if (keys == null || keys.Count == 0) throw new ArgumentException("The controller's keys are required", nameof(keys));
            if (d.AddedByPatcher) return "it is equipment this patcher added; choose a device of the map";
            if (d.DestinationKeyValueObjects != 1) return "the destination crossing controller has " + (d.DestinationKeyValueObjects == 0 ? "no" : "several") + " key-value objects, so it cannot address the copy";
            if (d.DestinationCullers > 1) return "the destination crossing controller holds several cullers, so the copy's culling would be ambiguous";
            if (d.ContainsDestination) return "it contains the destination crossing";
            if (d.OtherScene) return "it lies in another scene than the destination crossing, which could unload it while the copy still uses its assets";
            if (d.MissingScript) return "a script in its hierarchy is missing";
            if (d.Controllers > 0) return "it holds a crossing controller; a copy would be a second crossing";
            if (d.KeyValueObjects > 0) return "it holds its own key-value object, which would intercept the crossing controller's keys";
            if (d.Spans > 0) return "it holds a detection span";
            if (d.Cullers > 0) return "it holds its own culler";
            if (d.Bells > 0) return "it holds a bell player; bells follow the controller-bound bell pattern";
            if (d.Lamps.Count + d.Gates.Count == 0) return "it holds no lamp and no gate that observes the controller's " + String.Join(" or ", keys) + " key";
            foreach (var l in d.Lamps) {
                if (!keys.Contains(l.Key)) return "lamp " + l.Name + " observes key '" + l.Key + "', which the crossing controller does not publish";
                if (l.Targets == 0 || l.MissingTargets > 0) return "lamp " + l.Name + " has no lamp target";
                if (l.OutsideTargets > 0) return "lamp " + l.Name + " targets an object outside the source, so a copy would drive the original";
                if (l.Materials == 0 || l.MaterialsWithoutProperty > 0) return "lamp " + l.Name + " has no lamp material with a colour property";
                if (!l.RendererUsesMaterial) return "lamp " + l.Name + " has no renderer under its targets that uses its lamp material";
            }
            foreach (var g in d.Gates) {
                if (!keys.Contains(g.Key)) return "gate " + g.Name + " observes key '" + g.Key + "', which the crossing controller does not publish";
                if (!g.Animator) return "gate " + g.Name + " has no Animator";
                if (!g.Clip) return "gate " + g.Name + " has no animation clip";
            }
            return null;
        }
    }
    // One addition of the edited mod's scene patch file for a crossing: built in the scene, or with why it is not, such as a source
    // that is missing or no longer conforms. An addition that was not built can still be edited or removed.
    public sealed class EquipmentEntry {
        public EquipmentEntry(EquipmentAddition addition, EquipmentAddition built, string problem) { Addition = addition; Built = built; Problem = problem; }
        // The addition as the scene patch file holds it.
        public EquipmentAddition Addition { get; }
        // The addition as it stands in the scene, or null when it was not built.
        public EquipmentAddition Built { get; }
        public string Problem { get; }
    }
    // A device scene patch editing offers as a source, with why it does not conform to the pattern, or null when it is eligible.
    public sealed class SourceCandidate {
        public SourceCandidate(EquipmentSource source, string model, string problem, double[] position = null, double[] rotation = null) : this(source, model, problem, position, rotation, null, null, null, null) { }
        public SourceCandidate(EquipmentSource source, string model, string problem, double[] position, double[] rotation, string name, string crossing, double[] crossingPosition, double[] crossingRotation) {
            Source = source; Model = model; Problem = problem; Position = position; Rotation = rotation; Name = name; Crossing = crossing; CrossingPosition = crossingPosition; CrossingRotation = crossingRotation;
        }
        // The source's map placement, or null when it is unknown.
        public double[] Position { get; }
        public double[] Rotation { get; }
        // The device's own name and the crossing it stands at, as scene patch editing tells candidates apart; null when unknown.
        public string Name { get; }
        public string Crossing { get; }
        // The map placement of the crossing controller the device stands at, or null when it is unknown.
        public double[] CrossingPosition { get; }
        public double[] CrossingRotation { get; }
        // "Signal 1 at Bryson Everett": the device and its crossing, which tell otherwise equal models apart.
        public string Label => (Name ?? Model ?? Source.Path) + (String.IsNullOrEmpty(Crossing) ? "" : " at " + Crossing);
        public EquipmentSource Source { get; }
        public string Model { get; }
        public string Problem { get; }
        public bool Eligible => Problem == null;
    }
    // The placement scene patch editing proposes for a copy of a source at another crossing: the source's placement relative to its own
    // crossing, carried over to the destination crossing, then moved aside along the copy's own X axis so it does not
    // stand inside the device it copies. Without the source crossing's placement the copy starts at the destination crossing
    // with the source's rotation.
    public static class EquipmentPlacement {
        public const double Aside = 5;
        public static Placement Propose(double[] sourcePosition, double[] sourceRotation, double[] sourceCrossingPosition, double[] sourceCrossingRotation,
            double[] destinationPosition, double[] destinationRotation) {
            if (destinationPosition == null || destinationRotation == null) throw new ArgumentException("The destination crossing's placement is required");
            double[] position, rotation;
            if (sourcePosition == null || sourceRotation == null) { position = (double[])destinationPosition.Clone(); rotation = Normalize(destinationRotation); }
            else if (sourceCrossingPosition == null || sourceCrossingRotation == null) { position = (double[])destinationPosition.Clone(); rotation = Normalize(sourceRotation); }
            else {
                var inverse = Conjugate(Normalize(sourceCrossingRotation));
                var offset = new[] { sourcePosition[0] - sourceCrossingPosition[0], sourcePosition[1] - sourceCrossingPosition[1], sourcePosition[2] - sourceCrossingPosition[2] };
                var relative = Rotate(inverse, offset);
                var turned = Rotate(Normalize(destinationRotation), relative);
                position = new[] { destinationPosition[0] + turned[0], destinationPosition[1] + turned[1], destinationPosition[2] + turned[2] };
                rotation = Normalize(Multiply(Normalize(destinationRotation), Multiply(inverse, Normalize(sourceRotation))));
            }
            var aside = Rotate(rotation, new[] { Aside, 0, 0 });
            return new Placement(new[] { position[0] + aside[0], position[1] + aside[1], position[2] + aside[2] }, rotation);
        }
        // Quaternions are x, y, z, w, as placements store them.
        static double[] Normalize(double[] q) {
            var length = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
            if (length < 1e-12) throw new ArgumentException("Zero rotation quaternion");
            return new[] { q[0] / length, q[1] / length, q[2] / length, q[3] / length };
        }
        static double[] Conjugate(double[] q) => new[] { -q[0], -q[1], -q[2], q[3] };
        static double[] Multiply(double[] a, double[] b) => new[] {
            a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
            a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
            a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
            a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2] };
        static double[] Rotate(double[] q, double[] v) {
            var r = Multiply(Multiply(q, new[] { v[0], v[1], v[2], 0 }), Conjugate(q));
            return new[] { r[0], r[1], r[2] };
        }
    }
    // The game-facing operations of crossing equipment. An addition is the patcher's own object beneath the controller.
    public interface ICrossingEquipmentWorld {
        // The loaded objects at the source's scene and path, excluding equipment the patcher added. Never loads a scene.
        IReadOnlyList<object> ResolveSource(EquipmentSource source);
        // Why a copy of the source beneath the controller would not work through the pattern, or null.
        string SourceProblem(object source, object controller, string pattern);
        // The loaded devices offered as sources for the controller, eligible or not.
        IReadOnlyList<SourceCandidate> Candidates(object controller);
        // The additions present beneath the controller, including those an earlier application in this world left.
        IReadOnlyList<object> Owned(object controller);
        // The recorded scope, ID, pattern and source of an addition; its placement is read separately.
        EquipmentAddition Describe(object addition);
        // Builds a working copy of the source beneath the controller at the addition's placement. Throws, leaving nothing, when it cannot.
        object Construct(object controller, object source, EquipmentAddition addition);
        // Releases the addition from the controller, its culling and the scene.
        void Destroy(object addition);
        bool Present(object addition);
        Placement Placement(object addition);
        void SetPlacement(object addition, Placement placement);
        // Why a present addition is not addressed, culled and built as the pattern requires, or null.
        string Problem(object addition);
        // Its initialization and culling state, for inspection.
        string Status(object addition);
    }
    // Optional diagnostic sink for a failed equipment verification. It must not affect the edit or its refusal.
    public interface ICrossingEquipmentVerificationDiagnostics {
        void VerificationFailed(object controller, string detail);
    }
    // A scene that can offer equipment sources and report additions, such as the crossing capability.
    public interface ICrossingEquipmentScene {
        IReadOnlyList<SourceCandidate> EquipmentCandidates(object target);
        string EquipmentStatus(object target, string scope, string id);
    }
    // The equipment part of a crossing: converges the additions a controller owns on a complete set.
    internal sealed class CrossingEquipmentPart {
        readonly ICrossingEquipmentWorld world;
        internal CrossingEquipmentPart(ICrossingEquipmentWorld world) { this.world = world; }
        internal CrossingEquipment Observed(object controller, List<object> owned) {
            var present = owned.Where(world.Present).ToList();
            return new CrossingEquipment(present.Select(a => world.Describe(a).At(world.Placement(a))), true);
        }
        internal void Adopt(object controller, List<object> owned) {
            foreach (var addition in world.Owned(controller) ?? Array.Empty<object>()) if (!owned.Any(o => ReferenceEquals(o, addition))) owned.Add(addition);
        }
        // Rechecks each addition's source: unique, loaded and conforming to its pattern for this controller.
        internal object Source(object controller, EquipmentAddition addition) {
            if (!EquipmentAddition.Patterns.Contains(addition.Pattern)) throw new InvalidOperationException("Equipment " + addition.Id + ": pattern " + addition.Pattern + " is not supported");
            var matches = world.ResolveSource(addition.Source) ?? Array.Empty<object>();
            if (matches.Count == 0) throw new InvalidOperationException("Equipment " + addition.Id + ": source " + addition.Source + " is not loaded in this map. The patcher neither substitutes another source nor loads another scene");
            if (matches.Count > 1) throw new InvalidOperationException("Equipment " + addition.Id + ": source " + addition.Source + " is ambiguous; " + matches.Count + " loaded objects share that path");
            var problem = world.SourceProblem(matches[0], controller, addition.Pattern);
            if (problem != null) throw new InvalidOperationException("Equipment " + addition.Id + ": source " + addition.Source + " does not conform to " + addition.Pattern + ": " + problem);
            return matches[0];
        }
        internal void Check(object controller, CrossingEquipment equipment) {
            foreach (var addition in equipment.Additions) {
                if (addition.Placement?.Position == null || addition.Placement.Rotation == null) throw new InvalidOperationException("Equipment " + addition.Id + " needs a fixed placement");
                Source(controller, addition);
            }
        }
        // Null when every present addition is built, addressed and culled as its pattern requires.
        internal string Coherence(List<object> owned) {
            foreach (var addition in owned.Where(world.Present)) {
                var problem = world.Problem(addition);
                if (problem != null) return "added equipment " + world.Describe(addition).Id + ": " + problem;
            }
            return null;
        }
        // Converges the controller's additions on the complete set: a matching addition is kept and moved when needed; one
        // whose source or pattern changed is rebuilt; one absent from the set is removed; a missing one is built.
        internal void Write(object controller, List<object> owned, CrossingEquipment desired) {
            var kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (var addition in owned.Where(world.Present).ToList()) {
                var record = world.Describe(addition);
                var want = desired.Find(record.Key);
                if (want == null || kept.Contains(record.Key) || !EquipmentAddition.SameBuild(record, want)) { Retire(owned, addition); continue; }
                if (!EquipmentAddition.SamePlacement(world.Placement(addition), want.Placement)) world.SetPlacement(addition, want.Placement);
                kept.Add(record.Key);
            }
            foreach (var want in desired.Additions.Where(a => !kept.Contains(a.Key))) {
                var source = Source(controller, want);
                var built = world.Construct(controller, source, want) ?? throw new InvalidOperationException("Equipment " + want.Id + " was not constructed");
                owned.Add(built);
                var problem = world.Problem(built);
                if (problem != null) throw new InvalidOperationException("Equipment " + want.Id + " did not verify: " + problem);
            }
        }
        // Releases every addition; returns the first failure, or null. Additions that stay present remain owned.
        internal string ReleaseAll(List<object> owned) {
            string problem = null;
            foreach (var addition in owned.ToList()) {
                try { Retire(owned, addition); } catch (Exception e) { problem = problem ?? e.Message; }
                if (owned.Contains(addition) && !world.Present(addition)) owned.Remove(addition);
            }
            return problem;
        }
        void Retire(List<object> owned, object addition) {
            world.Destroy(addition);
            if (!world.Present(addition)) owned.Remove(addition);
        }
        internal string Verify(object controller, List<object> owned, CrossingEquipment desired) {
            var observed = Observed(controller, owned);
            foreach (var want in desired.Additions) {
                var now = observed.Find(want.Key);
                if (now == null) { Diagnose(controller, want, null, "missing"); return "equipment " + want.Id + " is missing"; }
                if (!EquipmentAddition.Same(now, want)) { Diagnose(controller, want, now, "mismatch"); return "equipment " + want.Id + " differs from the mod's source or placement"; }
            }
            var extra = observed.Additions.FirstOrDefault(a => desired.Find(a.Key) == null);
            if (extra != null) { Diagnose(controller, null, extra, "extra"); return "equipment " + extra.Id + " remains although it was removed"; }
            var problem = Coherence(owned);
            if (problem != null) Diagnose(controller, null, null, "coherence: " + problem);
            return problem;
        }
        static string Quoted(string value) => value == null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        static string Values(IReadOnlyList<double> values) => values == null ? "null" : "[" + String.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";
        static string Detail(EquipmentAddition a) => a == null ? "null" : "{scope=" + Quoted(a.Scope) + ",id=" + Quoted(a.Id) + ",pattern=" + Quoted(a.Pattern)
            + ",sourceScene=" + Quoted(a.Source.Scene) + ",sourcePath=" + Quoted(a.Source.Path) + ",position=" + Values(a.Placement?.Position) + ",rotation=" + Values(a.Placement?.Rotation) + "}";
        void Diagnose(object controller, EquipmentAddition want, EquipmentAddition now, string reason) {
            if (!(world is ICrossingEquipmentVerificationDiagnostics sink)) return;
            try {
                var comparable = want != null && now != null;
                var comparison = comparable ? " identity=" + EquipmentAddition.SameBuild(now, want).ToString().ToLowerInvariant()
                    + " source=" + EquipmentSource.Same(now.Source, want.Source).ToString().ToLowerInvariant()
                    + " position=" + PatchEditingSession.SamePosition(now.Placement?.Position, want.Placement?.Position).ToString().ToLowerInvariant()
                    + " rotation=" + PatchEditingSession.SameRotation(now.Placement?.Rotation, want.Placement?.Rotation).ToString().ToLowerInvariant() : "";
                sink.VerificationFailed(controller, "reason=" + Quoted(reason) + comparison + " positionTolerance=0.0005-per-axis rotationTolerance=normalized-absolute-dot>=0.99999999 desired=" + Detail(want) + " observed=" + Detail(now));
            } catch { /* Diagnostics must not change verification or restoration. */ }
        }
        internal IReadOnlyList<SourceCandidate> Candidates(object controller) => world.Candidates(controller) ?? Array.Empty<SourceCandidate>();
        internal string Status(List<object> owned, string scope, string id) {
            var addition = owned.Where(world.Present).FirstOrDefault(a => { var r = world.Describe(a); return r.Scope == scope && r.Id == id; });
            return addition == null ? null : world.Status(addition);
        }
    }
}
