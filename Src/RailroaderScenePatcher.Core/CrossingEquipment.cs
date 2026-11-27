// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // Where an addition comes from: a device already loaded in the selected map, named by its Unity scene and hierarchy path,
    // or a definition of the game's content library, named by its identifier, such as the bell definition a bell player
    // loads its audio through. The patcher never loads another scene to find a source and never substitutes another one.
    public sealed class EquipmentSource {
        public EquipmentSource(string scene, string path) {
            if (String.IsNullOrWhiteSpace(scene) || String.IsNullOrWhiteSpace(path)) throw new ArgumentException("A source needs a scene and a path");
            Scene = scene; Path = path;
        }
        EquipmentSource(string definition) { Definition = definition; }
        public static EquipmentSource ForDefinition(string identifier) {
            if (String.IsNullOrWhiteSpace(identifier)) throw new ArgumentException("A definition source needs an identifier");
            return new EquipmentSource(identifier);
        }
        // Null for a definition source.
        public string Scene { get; }
        public string Path { get; }
        // The content-library identifier of a definition source, or null for a loaded device.
        public string Definition { get; }
        public bool IsDefinition => Definition != null;
        internal static bool Same(EquipmentSource a, EquipmentSource b) => a == null ? b == null : b != null && a.Scene == b.Scene && a.Path == b.Path && a.Definition == b.Definition;
        public override string ToString() => IsDefinition ? "definition " + Definition : Scene + "/" + Path;
        // The name an addition keeps from this source: for a loaded device the last element of its path, which is the device's
        // name; for a bell definition its definition name, or the last part of its identifier without one.
        public string StoredName(string definitionName = null) {
            if (!IsDefinition) return Path.Substring(Path.LastIndexOf('/') + 1);
            return String.IsNullOrWhiteSpace(definitionName) ? Definition.Substring(Definition.LastIndexOf('/') + 1) : definitionName;
        }
    }
    // One piece of crossing equipment a mod adds to an existing crossing controller: a copy of an eligible source built
    // with the required pattern and addressed by that controller. Its ID is persistent and unique within the supplying
    // mod's scene patch file; the scope is that mod's identifier, so two mods may use the same ID.
    public sealed class EquipmentAddition {
        public const string LightGatePattern = "crossing-light-gate-v1", BellPattern = "crossing-bell-v1";
        public static readonly string[] Patterns = { LightGatePattern, BellPattern };
        // The audio priority of an added bell's player: the native crossing bell's own, so the game sorts both alike.
        // Railroader 0.6.3, Track.Crossings.GradeCrossingController.CreateBellPlayer.
        public const int BellPriority = 9;
        // The reason a bell source is refused when this game version cannot provide crossing bells. Unlike the
        // other source refusals, it is no problem of the mod author's scene patch file.
        public const string BellsUnavailable = "crossing bells are unavailable in this version of the game";
        public const string BellDefinitionsUnreadable = "the patcher couldn't read the bell definitions from the game's content library, so it can't list bells. Report this to the patcher maintainer and include the Player log";
        internal const string EmptyEquipmentObject = "An added light, gate, or bell left an empty object at this crossing";
        // Why the source kind does not fit the pattern, or null: a light or gate copies a loaded device, and a bell loads the
        // audio of a bell definition.
        public static string SourceKindProblem(string pattern, EquipmentSource source) {
            if (source == null) return "it needs a source";
            if (pattern == BellPattern && !source.IsDefinition) return "a bell needs a bell definition as its source, not a light or gate in the map";
            if (pattern == LightGatePattern && source.IsDefinition) return "a light or gate needs a light or gate in the map as its source, not a bell definition";
            return null;
        }
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
        // The name of the wrapper the game builds the addition in, which the inventory lists as the added object: the
        // supplying mod's identifier, with '/' as ':' since a hierarchy path separates names with '/', and the ID. An ID
        // holds no space (IdProblem), so the last space ends the identifier.
        public const string WrapperPrefix = "CrankyAnt Scene Patcher equipment ";
        public string WrapperName => WrapperPrefix + PathSafe(Scope) + " " + Id;
        // Whether the path names the wrapper of an addition that the mod with the identifier scope supplied. False for a
        // null scope: without an edited mod no addition is its.
        public static bool AddedBy(string path, string scope) {
            if (path == null || scope == null) return false;
            var name = path.Substring(path.LastIndexOf('/') + 1);
            if (!name.StartsWith(WrapperPrefix, StringComparison.Ordinal)) return false;
            var rest = name.Substring(WrapperPrefix.Length);
            var space = rest.LastIndexOf(' ');
            return space >= 0 && String.Equals(rest.Substring(0, space), PathSafe(scope), StringComparison.Ordinal);
        }
        static string PathSafe(string text) => (text ?? "").Replace('/', ':');
        internal string Key => Scope + "\n" + Id;
        internal EquipmentAddition In(string scope) => new EquipmentAddition(Id, Pattern, Source, Placement, scope);
        internal EquipmentAddition At(Placement placement) => new EquipmentAddition(Id, Pattern, Source, placement, Scope);
        // Null when the ID can name an addition: 1 to 64 letters, digits, '.', '_' or '-'.
        public static string IdProblem(string id) {
            if (String.IsNullOrEmpty(id) || id.Length > 64 || !id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-'))
                return "An equipment ID must contain 1 to 64 characters: letters, digits, '.', '_', or '-'";
            return null;
        }
        internal static bool SameBuild(EquipmentAddition a, EquipmentAddition b) => a.Key == b.Key && a.Pattern == b.Pattern && EquipmentSource.Same(a.Source, b.Source);
        internal static bool Same(EquipmentAddition a, EquipmentAddition b) => SameBuild(a, b) && SamePlacement(a.Placement, b.Placement);
        // The game's transforms store float32 coordinates. Beyond the editing tolerance, positions compare equal
        // only when they have the same finite stored coordinate; a different representable position remains a move.
        internal static bool SamePlacement(Placement a, Placement b) =>
            a?.Position != null && b?.Position != null && SamePosition(a.Position, b.Position) && PatchEditingSession.SameRotation(a.Rotation, b.Rotation);
        internal static bool SamePosition(IReadOnlyList<double> a, IReadOnlyList<double> b) {
            if (a == null || b == null || a.Count != 3 || b.Count != 3) return false;
            for (var i = 0; i < 3; i++) {
                var storedA = (float)a[i]; var storedB = (float)b[i];
                if (Single.IsNaN(storedA) || Single.IsNaN(storedB) || Single.IsInfinity(storedA) || Single.IsInfinity(storedB)) return false;
                if (Math.Abs(a[i] - b[i]) > .0005 && storedA != storedB) return false;
            }
            return true;
        }
        public override string ToString() {
            var p = Placement?.Position;
            return (Scope.Length == 0 ? "" : Scope + " ") + Id + ": " + Pattern + " from " + Source + (p == null ? "" : " at (" + String.Join(", ", p.Select(ShownNumber.Text)) + ")");
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
        // The mod whose scene patch file a complete set states, or null. Only its own additions that are not built leave with
        // the set: another mod's addition without its copy is that mod's to build or remove.
        internal string Owner { get; private set; }
        internal CrossingEquipment In(string scope) => new CrossingEquipment(additions.Select(a => a.In(scope)), Complete);
        internal CrossingEquipment Without(string scope) => new CrossingEquipment(additions.Where(a => a.Scope != scope), Complete) { Owner = scope };
        internal EquipmentAddition Find(string key) => additions.FirstOrDefault(a => a.Key == key);
        // The mod's equipment additions laid over a base set: each mod addition replaces its namesake or is added.
        internal static CrossingEquipment Overlay(CrossingEquipment basis, CrossingEquipment modEquipment) {
            if (modEquipment == null) return basis;
            if (basis == null) return modEquipment;
            var result = basis.additions.Select(a => modEquipment.Find(a.Key) ?? a).ToList();
            result.AddRange(modEquipment.additions.Where(a => basis.Find(a.Key) == null));
            return new CrossingEquipment(result, basis.Complete) { Owner = basis.Owner };
        }
        internal static bool Same(CrossingEquipment a, CrossingEquipment b) {
            if (a == null || b == null) return a == b;
            return a.additions.Count == b.additions.Count && a.additions.All(x => b.Find(x.Key) is EquipmentAddition y && EquipmentAddition.Same(x, y));
        }
        // An addition in a preview line: its equipment ID with its display name beside it, so that additions which copy one
        // source read apart, or its full description without a display name.
        internal static string Named(EquipmentAddition addition, Func<EquipmentAddition, string> displayName) {
            var name = displayName?.Invoke(addition);
            return name == null ? addition.ToString() : addition.Id + " (" + name + ")";
        }
        // One line per addition: what changes first, as it is built, rebuilt, moved or removed, then what is kept.
        internal static IReadOnlyList<string> Describe(CrossingEquipment current, CrossingEquipment desired) => Describe(current, desired, null);
        internal static IReadOnlyList<string> Describe(CrossingEquipment current, CrossingEquipment desired, Func<EquipmentAddition, string> displayName) {
            if (current == null || desired == null) throw new InvalidOperationException("This object has no crossing equipment");
            var changes = new List<string>(); var kept = new List<string>();
            string Name(EquipmentAddition addition) => Named(addition, displayName);
            foreach (var want in desired.additions) {
                var now = current.Find(want.Key);
                if (now == null) changes.Add("Would add " + Name(want) + ". " + WindowGuidance.ControllerDrivesCopy);
                else if (!EquipmentAddition.SameBuild(now, want)) changes.Add("Would rebuild " + Name(want) + "; the copy of " + (displayName == null ? now.Source.ToString() : displayName(now)) + " is removed.");
                else if (!EquipmentAddition.SamePlacement(now.Placement, want.Placement)) changes.Add("Would move " + Name(want) + ".");
                else kept.Add("Would keep " + Name(want) + ".");
            }
            foreach (var gone in current.additions.Where(a => desired.Find(a.Key) == null))
                changes.Add("Would remove " + Name(gone) + (displayName == null ? "; its copy is released from the controller and its culling." : "."));
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
            if (d.AddedByPatcher) return "the patcher added this equipment; it isn't an eligible native source. " + WindowGuidance.ChooseMapDevice;
            if (d.DestinationKeyValueObjects != 1) return "the destination crossing controller has " + (d.DestinationKeyValueObjects == 0 ? "no" : "several") + " key-value objects, so it cannot address the copy";
            if (d.DestinationCullers > 1) return "the destination crossing controller has several cullers, so it is unclear which one should control the copy's visibility";
            if (d.ContainsDestination) return "it contains the destination crossing";
            if (d.OtherScene) return "it is in a different Unity scene from the destination crossing and could unload while the copy still uses its assets";
            if (d.MissingScript) return "a script in its hierarchy is missing";
            if (d.Controllers > 0) return "it holds a crossing controller; a copy would be a second crossing";
            if (d.KeyValueObjects > 0) return "it holds its own key-value object, which would intercept the crossing controller's keys";
            if (d.Spans > 0) return "it holds a detection span";
            if (d.Cullers > 0) return "it holds its own culler";
            if (d.Bells > 0) return "it contains a bell player and is not a light or gate source. " + WindowGuidance.AddBellsFromDefinition;
            if (d.Lamps.Count + d.Gates.Count == 0) return "it has no lamp or gate that observes the controller's " + String.Join(" or ", keys) + " key";
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
    // What the bell pattern reads from a candidate definition and its destination crossing controller. The native world
    // fills it from the content library; the rules that judge it are BellPattern's.
    public sealed class BellFacts {
        // The definition's kind, such as Bell or Horn.
        public string Kind = "Bell";
        // The game uses the definition at grade crossings: its crossing controllers load it for their own bell.
        public bool CrossingBell = true;
        // The definition names an audio asset, and that asset resolves in the content library without loading anything.
        public bool AudioNamed = true, AudioResolves = true;
        public string Audio = "";
        public double[] Indexes = new double[0];
        public double Volume = 1;
        public int DestinationKeyValueObjects = 1;
    }
    // The crossing-bell-v1 pattern: a separate bell player beneath the crossing controller that observes the bell key the
    // controller publishes on its key-value object, and plays the audio of a bell definition the content library holds,
    // loaded through the library and configured with the definition's indexes and volume, as the game's own crossing bell and
    // locomotive bells do. The sound does not matter; the definition and loading mechanism do. Its sources are only the bell
    // definitions the game uses at grade crossings; locomotive bells and bells other mods add are none.
    public static class BellPattern {
        public const string NotACrossingBell = "the game doesn't use this bell at grade crossings";
        // Why the list offers no bell when the game's crossing bell definition is missing from the content library.
        public static string CrossingBellMissing(string identifier) => "the content library has no definition for the game's crossing bell " + identifier + ", so you can't add a bell";
        // The bell source list for a crossing controller: the definitions under the identifiers of the game's crossing bells,
        // each eligible or with its reason. Other bell definitions of the library are not listed. A crossing bell missing from
        // the library leaves one rejected row with why, and the list never falls back to other bell definitions.
        public static IReadOnlyList<SourceCandidate> Sources<T>(IEnumerable<string> crossingBells, IEnumerable<T> library, Func<T, string> identifier, Func<T, string> name, Func<T, string> problem) {
            var definitions = library.ToList();
            var result = new List<SourceCandidate>();
            foreach (var bell in crossingBells.Distinct().OrderBy(b => b, StringComparer.Ordinal)) {
                var found = definitions.Where(d => identifier(d) == bell).ToList();
                if (found.Count == 0) result.Add(SourceCandidate.UnavailableBells(CrossingBellMissing(bell)));
                result.AddRange(found.Select(d => SourceCandidate.Bell(bell, name(d), problem(d))));
            }
            if (result.Count == 0) result.Add(SourceCandidate.UnavailableBells(EquipmentAddition.BellsUnavailable));
            return result;
        }
        // Why a bell from the definition beneath the destination controller would not work, or null.
        public static string Problem(BellFacts b) {
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (b.DestinationKeyValueObjects != 1) return "the destination crossing controller has " + (b.DestinationKeyValueObjects == 0 ? "no" : "several") + " key-value objects, so it cannot address the bell";
            if (b.Kind != "Bell") return "it is " + (String.IsNullOrEmpty(b.Kind) ? "a definition of unknown kind" : "a " + b.Kind + " definition") + ", not a bell definition";
            if (!b.CrossingBell) return NotACrossingBell;
            if (!b.AudioNamed) return "its definition does not specify an audio asset";
            if (!b.AudioResolves) return "its audio asset " + b.Audio + " cannot be found in the content library";
            if (Double.IsNaN(b.Volume) || Double.IsInfinity(b.Volume) || b.Volume <= 0) return "its volume " + b.Volume.ToString(CultureInfo.InvariantCulture) + " must be a finite number greater than 0";
            var indexes = b.Indexes ?? new double[0];
            for (var i = 0; i < indexes.Length; i++)
                if (Double.IsNaN(indexes[i]) || Double.IsInfinity(indexes[i]) || indexes[i] <= 0 || (i > 0 && indexes[i] <= indexes[i - 1])) return "its audio indexes must be finite times greater than 0, in strictly increasing order";
            return null;
        }
        // Why the loaded clip does not fit the definition's indexes, or null: the player splits the clip at the sample of each
        // index, so every part between them, the first and the last included, needs at least one sample.
        public static string ClipProblem(int samples, int frequency, double[] indexes) {
            if (samples <= 0 || frequency <= 0) return "its loaded audio clip is empty";
            var previous = 0;
            foreach (var index in indexes ?? new double[0]) {
                var sample = (int)(index * frequency);
                if (sample <= previous || sample >= samples)
                    return "its audio index " + ShownNumber.Text(index) + " s does not split its clip of " + ShownNumber.Text((double)samples / frequency) + " s into parts of at least one sample";
                previous = sample;
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
            Pattern = source != null && source.IsDefinition ? EquipmentAddition.BellPattern : EquipmentAddition.LightGatePattern;
        }
        // A bell definition the content library holds, with its definition name when it has one.
        public static SourceCandidate Bell(string identifier, string name, string problem) =>
            new SourceCandidate(EquipmentSource.ForDefinition(identifier), identifier, problem, null, null, String.IsNullOrWhiteSpace(name) ? null : name, null, null, null);
        // The one rejected row that stands for bell definitions this build cannot offer, with why. It is not an addition and has
        // no recorded source, so it is named Bell.
        public static SourceCandidate UnavailableBells(string problem) => Bell("bell definitions", "Bell", problem);
        // The pattern an addition from this source is built with.
        public string Pattern { get; }
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
        // A bell definition reads as its name and identifier, such as "Crossing Bell (rr/bell-uss-teardrop-crossing-rh)".
        public string Label => Source.IsDefinition ? (Name == null || Name == Source.Definition ? Source.Definition : Name + " (" + Source.Definition + ")")
            : (Name ?? Model ?? Source.Path) + (String.IsNullOrEmpty(Crossing) ? "" : " at " + Crossing);
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
        // Whether a present addition still holds the copy or player it was built with. One without it counts as not built and
        // is rebuilt from its source, as a missing addition is built.
        bool Built(object addition);
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
        // Whether the desired state would remove a wrapper without its copy that the read state does not show, so a
        // Recheck that finds both states equal still applies it.
        bool EquipmentLeftover(object target, ObjectState desired);
    }
    // The equipment part of a crossing: converges the additions a controller owns on a complete set.
    internal sealed class CrossingEquipmentPart {
        readonly ICrossingEquipmentWorld world;
        internal CrossingEquipmentPart(ICrossingEquipmentWorld world) { this.world = world; }
        // A wrapper whose copy or player is gone. It counts as not built, as an addition that was never built does.
        bool Stale(object addition) => world.Present(addition) && !world.Built(addition);
        bool StaleAt(List<object> owned, EquipmentAddition addition) => owned.Any(a => Stale(a) && EquipmentAddition.SameBuild(world.Describe(a), addition));
        // The built additions; a wrapper without its copy is left out.
        internal CrossingEquipment Observed(object controller, List<object> owned) =>
            new CrossingEquipment(owned.Where(a => world.Present(a) && world.Built(a)).Select(a => world.Describe(a).At(world.Placement(a))), true);
        internal void Adopt(object controller, List<object> owned) {
            foreach (var addition in world.Owned(controller) ?? Array.Empty<object>()) if (!owned.Any(o => ReferenceEquals(o, addition))) owned.Add(addition);
        }
        // Rechecks each addition's source: unique, loaded and conforming to its pattern for this controller. A refusal names the
        // source as the addition's row does and ends with what the modder can do. stale says that the addition's wrapper stands
        // without its copy. A refusal is a problem of the mod author's scene patch file (InvalidPatchDefinitionException), so the
        // world load's warning names the mod author; only crossing bells this game build cannot provide stay a patcher problem.
        internal object Source(object controller, EquipmentAddition addition, bool stale = false) {
            var name = addition.Source.StoredName();
            if (!EquipmentAddition.Patterns.Contains(addition.Pattern)) throw new InvalidPatchDefinitionException("Equipment " + addition.Id + ": pattern " + addition.Pattern + " is not supported; supported patterns: " + String.Join(" or ", EquipmentAddition.Patterns) + ". " + WindowGuidance.UseSupportedPattern);
            var kind = EquipmentAddition.SourceKindProblem(addition.Pattern, addition.Source);
            if (kind != null) throw new InvalidPatchDefinitionException("Equipment " + addition.Id + ": " + kind.TrimEnd('.') + ". " + WindowGuidance.ChangeSourceOrRemove);
            var matches = world.ResolveSource(addition.Source) ?? Array.Empty<object>();
            if (matches.Count == 0) {
                var what = "Equipment " + addition.Id + ": " + (stale ? "the added equipment is missing, and " : "");
                var rebuilt = stale ? ", so the equipment cannot be rebuilt" : "";
                throw new InvalidPatchDefinitionException(addition.Source.IsDefinition
                    ? what + "bell definition " + name + " is unavailable in the content library" + rebuilt + ". " + WindowGuidance.OnlyAvailableBells + " " + WindowGuidance.SelectAnotherBell
                    : what + "light or gate " + name + " is not in the map" + rebuilt + ". " + WindowGuidance.OnlyMapLights + " " + WindowGuidance.SelectAnotherLight);
            }
            if (matches.Count > 1) throw new InvalidPatchDefinitionException("Equipment " + addition.Id + ": source " + name + " is ambiguous; " + matches.Count + (addition.Source.IsDefinition ? " bell definitions share its identifier. " + WindowGuidance.SelectAnotherBell : " objects in the map share its scene and path. " + WindowGuidance.SelectAnotherLight));
            var problem = world.SourceProblem(matches[0], controller, addition.Pattern);
            if (problem != null) {
                var message = "Equipment " + addition.Id + ": source " + name + " does not conform to " + addition.Pattern + ": " + problem.TrimEnd('.') + ". " + WindowGuidance.SelectEligibleSource;
                if (problem.StartsWith(EquipmentAddition.BellsUnavailable, StringComparison.Ordinal)) throw new InvalidOperationException(message);
                throw new InvalidPatchDefinitionException(message);
            }
            return matches[0];
        }
        internal void Check(object controller, CrossingEquipment equipment, List<object> owned) {
            foreach (var addition in equipment.Additions) {
                if (addition.Placement?.Position == null || addition.Placement.Rotation == null) throw new InvalidPatchDefinitionException("Equipment " + addition.Id + " has no fixed position and rotation. " + WindowGuidance.GivePlacement);
                Source(controller, addition, StaleAt(owned, addition));
            }
        }
        // Null when every built addition is addressed and culled as its pattern requires; an addition that is not built is
        // listed as such and not judged here. With a desired set, an addition that the change removes or rebuilds is not judged
        // either, so a faulty addition, such as a bell whose audio failed to load, can still be removed or replaced.
        internal string Coherence(List<object> owned, CrossingEquipment desired = null) => Faulty(owned, desired, out _);
        // Why an equipment change is refused before any change, with what the modder can do, or null. A faulty bell, such as
        // one whose audio failed to load, can be removed or replaced; another fault needs a reload.
        internal string Refusal(List<object> owned, CrossingEquipment desired) {
            var difference = Faulty(owned, desired, out var pattern);
            if (difference == null) return null;
            return "The crossing's added equipment does not meet its pattern's requirements: " + difference + (pattern == EquipmentAddition.BellPattern
                ? ". " + WindowGuidance.RemoveOrReplaceBell + " " + WindowGuidance.CoverageStillEditable
                : ". " + WindowGuidance.ReloadBeforeEditingEquipment + " " + WindowGuidance.CoverageStillEditable);
        }
        string Faulty(List<object> owned, CrossingEquipment desired, out string pattern) {
            pattern = null;
            foreach (var addition in owned.Where(a => world.Present(a) && world.Built(a))) {
                var record = world.Describe(addition);
                if (desired != null) { var want = desired.Find(record.Key); if (want == null || !EquipmentAddition.SameBuild(record, want)) continue; }
                var problem = world.Problem(addition);
                if (problem != null) { pattern = record.Pattern; return "added equipment " + record.Id + ": " + problem; }
            }
            return null;
        }
        // Whether the set retires a wrapper without its copy: one it names is built fresh, and its own mod's complete set
        // removes one it leaves out. Another mod's set, a restoration's included, leaves it as it is.
        bool Clears(object addition, CrossingEquipment desired) {
            var record = world.Describe(addition);
            return desired.Find(record.Key) != null || (desired.Complete && desired.Owner == record.Scope);
        }
        internal bool Clears(List<object> owned, CrossingEquipment desired) => owned.Any(a => Stale(a) && Clears(a, desired));
        // Converges the controller's additions on the complete set: a matching addition is kept and moved when needed; one
        // whose source or pattern changed is rebuilt; one absent from the set is removed; a missing one is built. A wrapper
        // without its copy counts as not built: when the set clears it, it is retired and, if named, built fresh.
        internal void Write(object controller, List<object> owned, CrossingEquipment desired) {
            var kept = new HashSet<string>(StringComparer.Ordinal);
            var stale = new HashSet<string>(StringComparer.Ordinal);
            foreach (var addition in owned.Where(world.Present).ToList()) {
                var record = world.Describe(addition);
                if (!world.Built(addition)) {
                    if (Clears(addition, desired)) { stale.Add(record.Key); Retire(owned, addition); }
                    continue;
                }
                var want = desired.Find(record.Key);
                if (want == null || kept.Contains(record.Key) || !EquipmentAddition.SameBuild(record, want)) { Retire(owned, addition); continue; }
                if (!EquipmentAddition.SamePlacement(world.Placement(addition), want.Placement)) world.SetPlacement(addition, want.Placement);
                kept.Add(record.Key);
            }
            foreach (var want in desired.Additions.Where(a => !kept.Contains(a.Key))) {
                var source = Source(controller, want, stale.Contains(want.Key));
                var built = world.Construct(controller, source, want) ?? throw new InvalidOperationException("Equipment " + want.Id + " was not constructed");
                owned.Add(built);
                var problem = world.Problem(built);
                if (problem != null) throw new InvalidOperationException("Equipment " + want.Id + " failed validation: " + problem);
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
            // A stale wrapper the set cleared and that still stands is owned state the change did not remove.
            var left = owned.FirstOrDefault(a => Stale(a) && Clears(a, desired));
            var observed = Observed(controller, owned);
            foreach (var want in desired.Additions) {
                var now = observed.Find(want.Key);
                if (now == null) { Diagnose(controller, want, null, "Requested equipment is missing"); return "equipment " + want.Id + " is missing"; }
                if (!EquipmentAddition.Same(now, want)) { Diagnose(controller, want, now, "Equipment differs from the requested equipment"); return "equipment " + want.Id + " differs from the mod's source or placement"; }
            }
            var extra = observed.Additions.FirstOrDefault(a => desired.Find(a.Key) == null);
            if (extra != null) { Diagnose(controller, null, extra, "Equipment remains although it was removed"); return "equipment " + extra.Id + " remains although it was removed"; }
            var problem = Coherence(owned);
            if (problem != null) { Diagnose(controller, null, null, "Equipment problem: " + problem); return problem; }
            // Report a lone empty wrapper only after the remaining additions have passed their checks.
            if (left != null) { var record = world.Describe(left); Diagnose(controller, null, record, "An empty equipment object remains"); throw new EmptyEquipmentObjectException(); }
            return null;
        }
        static string Quoted(string value) => value == null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        static string Values(IReadOnlyList<double> values) => values == null ? "null" : "[" + String.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";
        static string Detail(EquipmentAddition a) => a == null ? "none" : "{mod=" + Quoted(a.Scope) + ", ID=" + Quoted(a.Id) + ", pattern=" + Quoted(a.Pattern)
            + ", source scene=" + Quoted(a.Source.Scene) + ", source path=" + Quoted(a.Source.Path) + ", position=" + Values(a.Placement?.Position) + ", rotation=" + Values(a.Placement?.Rotation) + "}";
        void Diagnose(object controller, EquipmentAddition want, EquipmentAddition now, string reason) {
            if (!(world is ICrossingEquipmentVerificationDiagnostics sink)) return;
            try {
                var comparable = want != null && now != null;
                var comparison = comparable ? " Mod, equipment ID, pattern, and source match: " + EquipmentAddition.SameBuild(now, want).ToString().ToLowerInvariant()
                    + "; source matches: " + EquipmentSource.Same(now.Source, want.Source).ToString().ToLowerInvariant()
                    + "; position matches: " + EquipmentAddition.SamePosition(now.Placement?.Position, want.Placement?.Position).ToString().ToLowerInvariant()
                    + "; rotation matches: " + PatchEditingSession.SameRotation(now.Placement?.Rotation, want.Placement?.Rotation).ToString().ToLowerInvariant() + "." : "";
                sink.VerificationFailed(controller, reason + "." + comparison + " Positions match when each coordinate has a finite 32-bit floating-point value and either differs by at most 0.0005 or has the same stored 32-bit value. Rotations match when the absolute dot product of the normalized rotations is at least 0.99999999. Requested: " + Detail(want) + ". Found: " + Detail(now) + ".");
            } catch { /* Diagnostics must not change verification or restoration. */ }
        }
        internal IReadOnlyList<SourceCandidate> Candidates(object controller) => world.Candidates(controller) ?? Array.Empty<SourceCandidate>();
        internal string Status(List<object> owned, string scope, string id) {
            // The built addition of the ID, before a stale wrapper of the same ID that a failed cleanup left.
            var addition = owned.Where(world.Present).Where(a => { var r = world.Describe(a); return r.Scope == scope && r.Id == id; })
                .OrderBy(a => world.Built(a) ? 0 : 1).FirstOrDefault();
            return addition == null ? null : world.Status(addition);
        }
    }
}
