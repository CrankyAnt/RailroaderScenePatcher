// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The track a detection span covers: the route between two track locations.
    public sealed class SpanRange {
        public SpanRange(TrackLocation from, TrackLocation to) { From = from ?? throw new ArgumentNullException(nameof(from)); To = to ?? throw new ArgumentNullException(nameof(to)); }
        public TrackLocation From { get; }
        public TrackLocation To { get; }
        internal static bool Same(SpanRange a, SpanRange b) => a == null ? b == null : b != null && TrackLocation.Same(a.From, b.From) && TrackLocation.Same(a.To, b.To);
        public override string ToString() => From.Segment == To.Segment && From.End == To.End
            ? From.Segment + " end " + From.End + " " + Number(From.Distance) + " to " + Number(To.Distance) + " m"
            : From + " to " + To;
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
    public enum CircuitKind { Native, Tracks, Detached }
    // The coverage of one circuit of a crossing: its native spans, explicitly selected tracks, or detached from the crossing.
    public sealed class CircuitCoverage {
        public static readonly string[] Roles = { "approachA", "island", "approachB" };
        public static readonly CircuitCoverage Detached = new CircuitCoverage(CircuitKind.Detached, null, null);
        CircuitCoverage(CircuitKind kind, SpanRange[] ranges, string[] spans) { Kind = kind; this.ranges = ranges; this.spans = spans; }
        readonly SpanRange[] ranges;
        readonly string[] spans;
        public CircuitKind Kind { get; }
        // The explicitly selected tracks in role order: approach A, island, approach B.
        public static CircuitCoverage Tracks(SpanRange approachA, SpanRange island, SpanRange approachB) =>
            new CircuitCoverage(CircuitKind.Tracks, new[] { approachA ?? throw new ArgumentNullException(nameof(approachA)), island ?? throw new ArgumentNullException(nameof(island)), approachB ?? throw new ArgumentNullException(nameof(approachB)) }, null);
        // The circuit's own spans, by ID in role order, as the map defines them.
        public static CircuitCoverage Native(IReadOnlyList<string> spanIds) {
            if (spanIds == null || spanIds.Count != 3) throw new ArgumentException("A circuit has three spans", nameof(spanIds));
            return new CircuitCoverage(CircuitKind.Native, null, spanIds.ToArray());
        }
        // Null unless Kind is Tracks.
        public IReadOnlyList<SpanRange> Ranges => ranges;
        // Null unless Kind is Native.
        public IReadOnlyList<string> SpanIds => spans;
        internal static bool Same(CircuitCoverage a, CircuitCoverage b) {
            if (a == null || b == null) return a == b;
            if (a.Kind != b.Kind) return false;
            if (a.Kind == CircuitKind.Tracks) return a.ranges.Zip(b.ranges, SpanRange.Same).All(x => x);
            if (a.Kind == CircuitKind.Native) return a.spans.SequenceEqual(b.spans, StringComparer.Ordinal);
            return true;
        }
        public override string ToString() {
            switch (Kind) {
                case CircuitKind.Detached: return "detached; it does not trigger the crossing";
                case CircuitKind.Native: return "native spans approach A " + spans[0] + ", island " + spans[1] + ", approach B " + spans[2];
                default: return "selected tracks approach A " + ranges[0] + "; island " + ranges[1] + "; approach B " + ranges[2];
            }
        }
    }
    // The detection coverage of a crossing by circuit. A circuit is named by the ID of its native island span, which identifies
    // it in the map. An authored coverage lists only explicitly selected or detached circuits; an omitted circuit keeps its
    // effective coverage. An observed coverage lists every circuit of the crossing in its native order.
    public sealed class CrossingCoverage {
        readonly List<KeyValuePair<string, CircuitCoverage>> circuits;
        public CrossingCoverage(IEnumerable<KeyValuePair<string, CircuitCoverage>> circuits) : this(circuits, null) { }
        internal CrossingCoverage(IEnumerable<KeyValuePair<string, CircuitCoverage>> circuits, IEnumerable<string> residue) {
            Residue = (residue ?? Enumerable.Empty<string>()).ToArray();
            this.circuits = (circuits ?? throw new ArgumentNullException(nameof(circuits))).ToList();
            if (this.circuits.Any(c => String.IsNullOrWhiteSpace(c.Key) || c.Value == null)) throw new ArgumentException("Every circuit needs a name and a coverage");
            if (this.circuits.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != this.circuits.Count) throw new ArgumentException("Circuit names must be unique");
        }
        public IReadOnlyList<KeyValuePair<string, CircuitCoverage>> Circuits => circuits;
        // IDs of spans the patcher owns that stay registered without a circuit after an incomplete cleanup. Only observed
        // coverage holds them; any application removes them.
        public IReadOnlyList<string> Residue { get; }
        public CircuitCoverage this[string circuit] => circuits.FirstOrDefault(c => c.Key == circuit).Value;
        // The authored circuits laid over the observed ones; an omitted circuit keeps its coverage.
        internal static CrossingCoverage Overlay(CrossingCoverage observed, CrossingCoverage authored) {
            if (authored == null) return observed;
            if (observed == null) return authored;
            var result = observed.circuits.Select(c => new KeyValuePair<string, CircuitCoverage>(c.Key, authored[c.Key] ?? c.Value)).ToList();
            result.AddRange(authored.circuits.Where(c => observed[c.Key] == null));
            return new CrossingCoverage(result);
        }
        // Whether the circuit is the only one that still detects trains, so detaching it would be refused.
        public static bool LastCovered(CrossingCoverage coverage, string circuit) {
            if (coverage == null) return false;
            var covered = coverage.circuits.Where(c => c.Value.Kind != CircuitKind.Detached).ToList();
            return covered.Count == 1 && covered[0].Key == circuit;
        }
        // One line per circuit: what it covers now and what it would cover.
        internal static IReadOnlyList<string> Describe(CrossingCoverage current, CrossingCoverage desired) {
            if (current == null || desired == null) throw new InvalidOperationException("This object has no crossing coverage");
            var lines = new List<string>();
            foreach (var circuit in desired.circuits) {
                var now = current[circuit.Key];
                var next = circuit.Value;
                if (CircuitCoverage.Same(now, next)) { lines.Add("Circuit " + circuit.Key + ": retained, " + next + "."); continue; }
                var line = "Circuit " + circuit.Key + ": " + (now == null ? "unknown" : now.ToString()) + " -> " + next + ".";
                if (now?.Kind == CircuitKind.Native) line += " Its native spans " + String.Join(", ", now.SpanIds) + " stay registered and stop triggering this crossing.";
                if (now?.Kind == CircuitKind.Tracks) line += " The spans the patcher owns for its previous tracks are removed.";
                lines.Add(line);
            }
            return lines;
        }
        internal static bool Same(CrossingCoverage a, CrossingCoverage b) {
            if (a == null || b == null) return a == b;
            return a.circuits.Count == b.circuits.Count && a.circuits.All(c => CircuitCoverage.Same(c.Value, b[c.Key])) && a.Residue.SequenceEqual(b.Residue, StringComparer.Ordinal);
        }
    }
    // The game-facing operations of the crossing-coverage capability.
    public interface ICrossingCoverageWorld {
        IEnumerable<string> MapIds { get; }
        IReadOnlyList<object> Resolve(TargetIdentity identity);
        // Why the controller does not conform to the supported crossing pattern, or null.
        string PatternProblem(object controller);
        // The circuits the controller currently evaluates, in order.
        IReadOnlyList<object> Circuits(object controller);
        // A circuit's three span references in role order.
        IReadOnlyList<object> Spans(object circuit);
        string SpanId(object span);
        SpanRange Range(object span);
        // The graph's span registered under the span's ID is this span.
        bool Registered(object span);
        // Some span is registered under the ID.
        bool IdRegistered(string id);
        // The span resolves both ends and covers track of positive length.
        bool Covers(object span);
        // The lengths of every loaded segment with this ID; one entry for a unique segment.
        IReadOnlyList<double> SegmentLengths(string segment);
        // Creates a span owned by the patcher for the controller, registered under the ID. Throws, leaving nothing behind, when it cannot.
        object CreateSpan(object controller, string id, SpanRange range);
        // Unregisters and destroys an owned span.
        void DestroySpan(object span);
        void SetSpans(object circuit, object approachA, object island, object approachB);
        void SetCircuits(object controller, IReadOnlyList<object> circuits);
        double[] Position(object controller);
        double[] Rotation(object controller);
        bool Active(object controller);
    }
    // Corrects the detection coverage of an existing grade crossing. A circuit keeps its native spans, uses explicitly
    // selected tracks covered by spans the patcher owns, or is detached from the crossing. The native spans stay registered
    // and unchanged, so another consumer keeps them; the crossing simply stops evaluating them. Proximity never assigns
    // coverage, and the crossing object itself is never moved or disabled by this capability.
    public sealed class CrossingCoverageScene : IScene, IRelationshipScene, IVersionedScene {
        public const string Structure = "crossing-coverage-v1", RequiredVersion = "0.5.0", ComponentType = "Track.Crossings.GradeCrossingController";
        const string OwnedPrefix = "CrankyAnt.ScenePatcher.";
        sealed class Circuit { internal string Key; internal object Set; internal object[] Original; }
        // The native circuits of a controller as first observed in this load, and every span the patcher owns for it.
        sealed class Baseline { internal List<Circuit> Circuits = new List<Circuit>(); internal List<object> Owned = new List<object>(); }
        sealed class ByReference : IEqualityComparer<object> {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        readonly ICrossingCoverageWorld world;
        readonly Dictionary<object, Baseline> baselines = new Dictionary<object, Baseline>(new ByReference());
        public CrossingCoverageScene(ICrossingCoverageWorld world, Capability capability) { this.world = world ?? throw new ArgumentNullException(nameof(world)); Capability = capability; }
        public IEnumerable<string> MapIds => world.MapIds;
        public Capability Capability { get; }
        public IReadOnlyList<object> Resolve(TargetIdentity identity) {
            if (identity.Structure != null && identity.Structure != Structure) return Array.Empty<object>();
            return world.Resolve(identity) ?? Array.Empty<object>();
        }
        public string MinimumPatcherVersion(TargetIdentity target) => target.Structure == Structure ? RequiredVersion : null;
        // Spans the patcher owns for the controller in this load, for inspection.
        public IReadOnlyList<object> OwnedSpans(object target) => baselines.TryGetValue(target, out var b) ? b.Owned.ToArray() : Array.Empty<object>();
        Baseline BaselineOf(object target) {
            if (baselines.TryGetValue(target, out var known)) return known;
            var problem = world.PatternProblem(target);
            if (problem != null) throw new InvalidOperationException(problem);
            var baseline = new Baseline();
            foreach (var set in world.Circuits(target) ?? Array.Empty<object>()) {
                var spans = (world.Spans(set) ?? Array.Empty<object>()).ToArray();
                if (set == null || spans.Length != 3 || spans.Any(s => s == null)) throw new InvalidOperationException("A circuit of this crossing lacks one of its three spans");
                if (spans.Any(s => !world.Registered(s))) throw new InvalidOperationException("A span of this crossing is not registered in the track graph under its ID. Reload before editing it");
                var key = world.SpanId(spans[1]);
                if (String.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("The island span of a circuit has no ID; the circuit cannot be identified");
                if (baseline.Circuits.Any(c => c.Key == key || ReferenceEquals(c.Set, set))) throw new InvalidOperationException("Two circuits of this crossing share island span " + key + "; they cannot be told apart");
                baseline.Circuits.Add(new Circuit { Key = key, Set = set, Original = spans });
            }
            if (baseline.Circuits.Count == 0) throw new InvalidOperationException("The crossing has no circuits");
            baselines.Add(target, baseline);
            return baseline;
        }
        public void Preflight(object target) {
            var problem = world.PatternProblem(target);
            if (problem != null) throw new InvalidOperationException(problem);
            var difference = Coherence(target, BaselineOf(target), false);
            if (difference != null) throw new InvalidOperationException("The crossing's circuits differ from their native or patched coverage: " + difference + ". Reload before editing it");
        }
        public void CheckRelationships(object target, ObjectState state) {
            if (state == null) return;
            if (state.Placement != null || state.Active != null) throw new InvalidOperationException("A crossing-coverage target accepts only relationships.crossingCoverage; placement and activity of the crossing are not supported");
            if (state.Marker != null) throw new InvalidOperationException("This object has no supported track-marker relationship");
            if (state.Coverage == null) return;
            Validate(target, BaselineOf(target), state.Coverage, false);
        }
        // Authored coverage names circuits of this crossing with selected tracks or a detach; an applied coverage may also
        // return a circuit to its native spans. At least one circuit must keep detection.
        void Validate(object target, Baseline baseline, CrossingCoverage coverage, bool allowNative) {
            foreach (var circuit in coverage.Circuits) {
                var own = baseline.Circuits.FirstOrDefault(c => c.Key == circuit.Key);
                if (own == null) throw new InvalidOperationException("Circuit " + circuit.Key + " is not a circuit of this crossing; the reference is obsolete or names another map");
                if (circuit.Value.Kind == CircuitKind.Native && (!allowNative || !circuit.Value.SpanIds.SequenceEqual(own.Original.Select(world.SpanId), StringComparer.Ordinal)))
                    throw new InvalidOperationException("Circuit " + circuit.Key + " needs selected tracks or an explicit detach");
                if (circuit.Value.Kind != CircuitKind.Tracks) continue;
                for (var i = 0; i < 3; i++) {
                    var problem = Unresolved(circuit.Value.Ranges[i]);
                    if (problem != null) throw new InvalidOperationException("Circuit " + circuit.Key + " " + CircuitCoverage.Roles[i] + ": " + problem);
                }
            }
            var effective = CrossingCoverage.Overlay(Observed(target, baseline), coverage);
            if (effective.Circuits.All(c => c.Value.Kind == CircuitKind.Detached)) throw new InvalidOperationException("Detaching every circuit would leave the crossing without detection; keep or select coverage for at least one circuit");
        }
        string Unresolved(SpanRange range) {
            foreach (var location in new[] { range.From, range.To }) {
                if (location.End != "a" && location.End != "b") return "end must be \"a\" or \"b\"";
                var lengths = world.SegmentLengths(location.Segment) ?? Array.Empty<double>();
                if (lengths.Count == 0) return "track segment " + location.Segment + " is missing";
                if (lengths.Count > 1) return "track segment " + location.Segment + " is ambiguous";
                if (Double.IsNaN(location.Distance) || location.Distance < 0 || location.Distance > lengths[0] + TrackLocation.DistanceTolerance)
                    return "distance " + location.Distance.ToString("R", CultureInfo.InvariantCulture) + " lies outside segment " + location.Segment;
            }
            if (TrackLocation.Same(range.From, range.To)) return "the selected track has no length";
            return null;
        }
        public object Capture(object target) => Observed(target, BaselineOf(target));
        public ObjectState Read(object target) => new ObjectState(new Placement(world.Position(target), world.Rotation(target)), world.Active(target), null, Observed(target, BaselineOf(target)));
        CrossingCoverage Observed(object target, Baseline baseline) {
            var members = world.Circuits(target) ?? Array.Empty<object>();
            var circuits = baseline.Circuits.Select(c => new KeyValuePair<string, CircuitCoverage>(c.Key, Classify(c, members, baseline))).ToList();
            var inUse = baseline.Circuits.Where(c => members.Any(m => ReferenceEquals(m, c.Set))).SelectMany(c => world.Spans(c.Set) ?? Array.Empty<object>()).ToList();
            return new CrossingCoverage(circuits, baseline.Owned.Where(o => !inUse.Any(u => ReferenceEquals(u, o)) && world.Registered(o)).Select(world.SpanId));
        }
        CircuitCoverage Classify(Circuit circuit, IReadOnlyList<object> members, Baseline baseline) {
            if (!members.Any(m => ReferenceEquals(m, circuit.Set))) return CircuitCoverage.Detached;
            var spans = (world.Spans(circuit.Set) ?? Array.Empty<object>()).ToArray();
            if (spans.Length == 3 && spans.Zip(circuit.Original, ReferenceEquals).All(x => x)) return CircuitCoverage.Native(circuit.Original.Select(world.SpanId).ToArray());
            if (spans.Length == 3 && spans.All(s => baseline.Owned.Any(o => ReferenceEquals(o, s)))) return CircuitCoverage.Tracks(world.Range(spans[0]), world.Range(spans[1]), world.Range(spans[2]));
            throw new InvalidOperationException("Circuit " + circuit.Key + " holds spans that are neither its native spans nor spans this patcher owns");
        }
        public void Apply(object target, ObjectState state) {
            if (state.Marker != null) throw new InvalidOperationException("This object has no supported track-marker relationship");
            if (state.Placement != null && !Placements.Same(state.Placement, new Placement(world.Position(target), world.Rotation(target)))) throw new InvalidOperationException("Crossing coverage does not move the crossing");
            if (state.Active != null && state.Active.Value != world.Active(target)) throw new InvalidOperationException("Crossing coverage does not change the crossing's activity");
            if (state.Coverage == null) return;
            var baseline = BaselineOf(target);
            var desired = CrossingCoverage.Overlay(Observed(target, baseline), state.Coverage);
            Validate(target, baseline, state.Coverage, true);
            Write(target, baseline, desired);
            var difference = Verify(target, baseline, desired);
            if (difference != null) throw new InvalidOperationException("Applied crossing coverage did not verify: " + difference);
        }
        public bool Restore(object target, object snapshot) {
            try {
                var baseline = BaselineOf(target);
                var captured = (CrossingCoverage)snapshot;
                Write(target, baseline, captured);
                return Verify(target, baseline, captured) == null;
            } catch { return false; }
        }
        // Converges the controller on the coverage from any state this adapter can leave: every circuit holds its native spans
        // or owned spans covering exactly its selected tracks, the controller evaluates the circuits that are not detached in
        // their native order, and no other owned span remains registered. Matching owned spans are reused, so repeating an
        // application creates nothing.
        void Write(object target, Baseline baseline, CrossingCoverage coverage) {
            var used = new List<object>();
            var created = new List<object>();
            var assignment = new Dictionary<Circuit, object[]>();
            try {
                foreach (var circuit in baseline.Circuits) {
                    var wanted = coverage[circuit.Key] ?? throw new InvalidOperationException("Circuit " + circuit.Key + " has no coverage to apply");
                    if (wanted.Kind != CircuitKind.Tracks) { assignment[circuit] = circuit.Original; continue; }
                    var current = (world.Spans(circuit.Set) ?? Array.Empty<object>()).ToArray();
                    var spans = new object[3];
                    for (var i = 0; i < 3; i++) {
                        var range = wanted.Ranges[i];
                        bool Fits(object s) => s != null && baseline.Owned.Any(o => ReferenceEquals(o, s)) && !used.Any(u => ReferenceEquals(u, s)) && world.Registered(s) && SpanRange.Same(world.Range(s), range);
                        var reuse = current.Length == 3 && Fits(current[i]) ? current[i] : baseline.Owned.FirstOrDefault(Fits);
                        if (reuse == null) {
                            reuse = world.CreateSpan(target, NewId(circuit.Key, CircuitCoverage.Roles[i]), range) ?? throw new InvalidOperationException("The owned span was not created");
                            created.Add(reuse); baseline.Owned.Add(reuse);
                        }
                        used.Add(reuse); spans[i] = reuse;
                    }
                    assignment[circuit] = spans;
                }
                // New spans must be registered and cover their track before any circuit refers to them.
                foreach (var span in created) if (!world.Registered(span) || !world.Covers(span)) throw new InvalidOperationException("The owned span " + world.SpanId(span) + " did not register or covers no track");
            } catch {
                foreach (var span in created) { try { Retire(baseline, span); } catch { } }
                throw;
            }
            foreach (var circuit in baseline.Circuits) {
                var spans = assignment[circuit];
                var current = (world.Spans(circuit.Set) ?? Array.Empty<object>()).ToArray();
                if (current.Length != 3 || !current.Zip(spans, ReferenceEquals).All(x => x)) world.SetSpans(circuit.Set, spans[0], spans[1], spans[2]);
            }
            var members = baseline.Circuits.Where(c => coverage[c.Key].Kind != CircuitKind.Detached).Select(c => c.Set).ToList();
            var now = world.Circuits(target) ?? Array.Empty<object>();
            if (now.Count != members.Count || !now.Zip(members, ReferenceEquals).All(x => x)) world.SetCircuits(target, members);
            foreach (var span in baseline.Owned.Where(o => !used.Any(u => ReferenceEquals(u, o))).ToList()) {
                Retire(baseline, span);
            }
        }
        // An owned span that stays registered after its destruction remains owned, so the next check reports it as residue.
        void Retire(Baseline baseline, object span) {
            world.DestroySpan(span);
            if (!world.Registered(span)) baseline.Owned.Remove(span);
        }
        string NewId(string key, string role) {
            var stem = OwnedPrefix + key + "." + role;
            var id = stem;
            for (var n = 2; world.IdRegistered(id); n++) id = stem + "." + n;
            return id;
        }
        string Verify(object target, Baseline baseline, CrossingCoverage coverage) {
            CrossingCoverage observed;
            try { observed = Observed(target, baseline); } catch (InvalidOperationException e) { return e.Message; }
            foreach (var circuit in coverage.Circuits)
                if (!CircuitCoverage.Same(observed[circuit.Key], circuit.Value)) return "circuit " + circuit.Key + " is " + (observed[circuit.Key]?.ToString() ?? "unknown") + ", expected " + circuit.Value;
            return Coherence(target, baseline, true);
        }
        // Null when the controller evaluates only its own circuits in native order, each holding its native spans or owned
        // spans, every native span stays registered, and exactly the owned spans in use are registered and cover track.
        // Residue left by an incomplete cleanup does not refuse an edit: the next application removes it, and until then the
        // observed coverage differs from any document so a recheck applies it.
        string Coherence(object target, Baseline baseline, bool residue) {
            var members = world.Circuits(target) ?? Array.Empty<object>();
            if (members.Any(m => !baseline.Circuits.Any(c => ReferenceEquals(c.Set, m)))) return "the crossing evaluates a circuit that is not one of its own";
            if (members.Distinct(new ByReference()).Count() != members.Count) return "the crossing evaluates a circuit twice";
            var order = baseline.Circuits.Where(c => members.Any(m => ReferenceEquals(m, c.Set))).Select(c => c.Set);
            if (!order.SequenceEqual(members, new ByReference())) return "the crossing's circuits are out of their native order";
            var inUse = new List<object>();
            foreach (var circuit in baseline.Circuits) {
                try { Classify(circuit, members, baseline); } catch (InvalidOperationException e) { return e.Message; }
                if (members.Any(m => ReferenceEquals(m, circuit.Set))) inUse.AddRange((world.Spans(circuit.Set) ?? Array.Empty<object>()).Where(s => baseline.Owned.Any(o => ReferenceEquals(o, s))));
                var lost = circuit.Original.FirstOrDefault(s => !world.Registered(s));
                if (lost != null) return "native span " + world.SpanId(lost) + " is no longer registered";
            }
            foreach (var span in baseline.Owned) {
                if (!inUse.Any(u => ReferenceEquals(u, span))) { if (residue) return "owned span " + world.SpanId(span) + " remains without a circuit"; continue; }
                if (!world.Registered(span)) return "owned span " + world.SpanId(span) + " is not registered";
                if (!world.Covers(span)) return "owned span " + world.SpanId(span) + " covers no track";
            }
            return null;
        }
    }
    internal static class Placements {
        internal static bool Same(Placement a, Placement b) {
            if (a == null || b == null || a.Position == null || b.Position == null || a.Rotation == null || b.Rotation == null) return false;
            for (var i = 0; i < 3; i++) if (Math.Abs(a.Position[i] - b.Position[i]) > 0.01) return false;
            double dot = 0, la = 0, lb = 0;
            for (var i = 0; i < 4; i++) { dot += a.Rotation[i] * b.Rotation[i]; la += a.Rotation[i] * a.Rotation[i]; lb += b.Rotation[i] * b.Rotation[i]; }
            return la > 0 && lb > 0 && Math.Abs(dot) / Math.Sqrt(la * lb) > 0.99999;
        }
    }
}
