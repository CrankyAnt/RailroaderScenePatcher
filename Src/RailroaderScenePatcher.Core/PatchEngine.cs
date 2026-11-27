// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;

namespace RailroaderScenePatcher.Core {
    // A checked supplied value/reference is invalid. Native or unexpected exceptions retain an unknown cause.
    public sealed class InvalidPatchDefinitionException : InvalidOperationException {
        public InvalidPatchDefinitionException(string message) : base(message) { }
    }
    public enum Capability { Checked, Unchecked, Incompatible }
    public sealed class TargetIdentity {
        internal TargetIdentity() { }
        public TargetIdentity(string scene, string path, string component, string structure = null, string padMarkerId = null) {
            Scene = scene; Path = path; Component = component; Structure = structure; PadMarkerId = padMarkerId;
        }
        public string Scene { get; internal set; }
        public string Path { get; internal set; }
        public string Component { get; internal set; }
        public string Structure { get; internal set; }
        // The marker ID of the crossing pad beside a crossbuck whose hierarchy path several crossbucks share, as the
        // crossbucks of the three crossings below World/Roads Bryson/Old River Rd do; null for every other target. It
        // stands beside the path, which stays the raw hierarchy path.
        public string PadMarkerId { get; internal set; }
        public const string PadMarkerMember = "padMarkerId";
        // The key of an object whose hierarchy path and component another loaded object shares (TargetKeys): the
        // game's marker ID where the object has one, else its map x/z as the game placed it. Both are null for a
        // target whose path and component are unique, and for a crossbuck that its pad marker ID names.
        public string MarkerId { get; set; }
        public double[] MapPosition { get; set; }
        public const string MarkerIdMember = "markerId", MapPositionMember = "mapPosition";
        // A map position matches an object whose x and z each lie within this distance, in metres. The game keeps
        // positions in single precision and the key is written to the centimetre; objects that share a path stand
        // metres apart, so no other object falls within it. It is never a search radius: no nearest object is taken.
        public const double MapPositionTolerance = 0.05;
        public bool Keyed => MarkerId != null || MapPosition != null;
        // Why a scene refuses a target with a pad marker ID, when it names no crossbuck at a shared path.
        public const string PadMarkerNotAccepted = "The target doesn't accept padMarkerId because it isn't a crossbuck at an unprotected crossing with a shared path. " + WindowGuidance.RemovePadMarkerId;
        // Whether a target object of a scene patch file names this identity's object: the same scene, path, component,
        // pad marker ID and key. The structure is compared by the caller.
        internal static bool SameObject(JObject target, TargetIdentity identity) {
            if (target == null || identity == null || (string)target["scene"] != identity.Scene || !ModObjects.SamePath((string)target["path"], identity.Path) || (string)target["component"] != identity.Component
                || (target[PadMarkerMember]?.Type == JTokenType.String ? (string)target[PadMarkerMember] : null) != identity.PadMarkerId) return false;
            TargetIdentity other;
            try { other = PatchEngine.ParseTarget(target); } catch (InvalidOperationException) { return false; }
            return SameKey(other, identity);
        }
        // The identity for a message: scene and path, the pad marker ID or key where it has one, and the component.
        public string Label => Scene + "/" + Path + (PadMarkerId == null ? "" : " (pad marker " + PadMarkerId + ")") + (MarkerId == null ? "" : " (marker " + MarkerId + ")")
            + (MapPosition == null ? "" : " (at " + TargetKeys.Number(MapPosition[0]) + ", " + TargetKeys.Number(MapPosition[1]) + ")") + " [" + Component + "]";
        public static bool SameObject(TargetIdentity a, TargetIdentity b) =>
            a != null && b != null && a.Scene == b.Scene && ModObjects.SamePath(a.Path, b.Path) && a.Component == b.Component && a.PadMarkerId == b.PadMarkerId && SameKey(a, b);
        static bool SameKey(TargetIdentity a, TargetIdentity b) =>
            a.MarkerId == b.MarkerId && (a.MapPosition == null) == (b.MapPosition == null) && (a.MapPosition == null || TargetKeys.Matches(a.MapPosition, b.MapPosition));
        // A text that tells identities apart, for a dictionary key: equal identities give the same text.
        internal string Text => Scene + "|" + ModObjects.Canonical(Path) + "|" + Component + "|" + Structure + "|" + PadMarkerId + "|" + MarkerId + "|"
            + (MapPosition == null ? "" : TargetKeys.Number(MapPosition[0]) + "," + TargetKeys.Number(MapPosition[1]));
        // The same identity without its key: the objects at its path and component.
        internal TargetIdentity Unkeyed() => new TargetIdentity(Scene, Path, Component, Structure, PadMarkerId);
        // The same identity at another path, keys included.
        internal TargetIdentity At(string path) => new TargetIdentity(Scene, path, Component, Structure, PadMarkerId) { MarkerId = MarkerId, MapPosition = MapPosition };
        // The target object of a scene patch file for this identity.
        internal JObject ToJson() {
            var value = new JObject { ["scene"] = Scene, ["path"] = Path, ["component"] = Component };
            if (Structure != null) value["structure"] = Structure;
            if (PadMarkerId != null) value[PadMarkerMember] = PadMarkerId;
            if (MarkerId != null) value[MarkerIdMember] = MarkerId;
            if (MapPosition != null) value[MapPositionMember] = new JArray(MapPosition[0], MapPosition[1]);
            return value;
        }
    }
    // An object's key as the game gave it, before this mod changed it: its marker ID, or null, and its map x/z.
    public sealed class TargetKey {
        public TargetKey(string markerId, double[] mapPosition) { MarkerId = markerId; MapPosition = mapPosition; }
        // Matches every key: an object that stands for a refusal of its own rather than for a scene object.
        public static readonly TargetKey Any = new TargetKey(null, null);
        public string MarkerId { get; }
        public double[] MapPosition { get; }
    }
    // A scene that supplies the key of a resolved object. A scene without it matches no keyed target.
    public interface ITargetKeyScene {
        // The object's marker ID and map x/z as the game gave them, unchanged by any modification since; null when
        // they cannot be read.
        TargetKey KeyOf(object target);
    }
    // The key of a target whose hierarchy path and component another loaded object shares.
    public enum TargetKeyKind { MarkerId, PadMarkerId, MapPosition }
    public static class TargetKeys {
        public const string RequiredVersion = "0.5.0";
        public const string MarkerComponent = "Track.TrackMarker";
        // Which key names an object at a shared path: a crossbuck keeps its crossing's pad marker ID, an object with a
        // track marker its marker ID, every other object its map position.
        public static TargetKeyKind KindOf(IEnumerable<string> components, bool crossbuck) =>
            crossbuck ? TargetKeyKind.PadMarkerId : components != null && components.Contains(MarkerComponent) ? TargetKeyKind.MarkerId : TargetKeyKind.MapPosition;
        // The key that editing writes: none when one loaded object holds the path and component, else the game's marker
        // ID where the object has one, else its map x/z rounded to the centimetre.
        public static TargetIdentity For(string scene, string path, string component, string markerId, double[] mapPosition, int holders, string structure = null) {
            var identity = new TargetIdentity(scene, path, component, structure);
            if (holders < 2) return identity;
            if (!String.IsNullOrEmpty(markerId)) identity.MarkerId = markerId;
            else if (mapPosition != null && mapPosition.Length >= 2) identity.MapPosition = new[] { Math.Round(mapPosition[0], 2), Math.Round(mapPosition[1], 2) };
            return identity;
        }
        // Whether two map positions match within the tolerance on x and on z.
        public static bool Matches(double[] a, double[] b) =>
            a != null && b != null && a.Length >= 2 && b.Length >= 2 && Math.Abs(a[0] - b[0]) <= TargetIdentity.MapPositionTolerance && Math.Abs(a[1] - b[1]) <= TargetIdentity.MapPositionTolerance;
        // Whether an object's key matches the identity's key. An object whose key cannot be read matches none.
        public static bool Matches(TargetIdentity identity, TargetKey key) {
            if (key == null) return false;
            if (ReferenceEquals(key, TargetKey.Any)) return true;
            if (identity.MarkerId != null) return key.MarkerId == identity.MarkerId;
            return identity.MapPosition == null || Matches(identity.MapPosition, key.MapPosition);
        }
        internal static string Number(double value) => ShownNumber.Text(value);
    }
    // A scene that accepts a pad marker ID beside a target's path: it says why it refuses the identity, or null.
    public interface IPadMarkerIdentityScene {
        string PadMarkerRefusal(TargetIdentity identity);
    }
    public sealed class Placement {
        internal Placement() { }
        public Placement(double[] position, double[] rotation) { Position = position; Rotation = rotation; }
        public double[] Position { get; internal set; }
        public double[] Rotation { get; internal set; }
        // Set for a track-following placement: Position and Rotation are then its evaluation at the anchor's current pose,
        // or null until it is evaluated. Null for a fixed placement.
        public TrackFollowing Following { get; internal set; }
        // Set on a placement a scene reads for an object that stands in a frame that can move, such as a child object
        // in its top scene object, while no fixed placement holds the object: the map placement of that frame
        // when the object was read. Position and Rotation stay map coordinates. Applied back, such a placement returns
        // the object to its place in the frame. Null for every other placement.
        public Placement Frame { get; internal set; }
        // The placement that keeps this placement's place in its frame, evaluated at the frame's placement now.
        internal Placement InFrame(Placement now) {
            if (Frame == null || now?.Position == null || now.Rotation == null) return this;
            var inverse = Quaternion.Conjugate(Quaternion.Normalize(Frame.Rotation));
            var local = Quaternion.Rotate(inverse, new[] { Position[0] - Frame.Position[0], Position[1] - Frame.Position[1], Position[2] - Frame.Position[2] });
            var turn = Quaternion.Normalize(now.Rotation);
            var moved = Quaternion.Rotate(turn, local);
            return new Placement(new[] { now.Position[0] + moved[0], now.Position[1] + moved[1], now.Position[2] + moved[2] },
                Quaternion.Normalize(Quaternion.Multiply(turn, Quaternion.Multiply(inverse, Rotation)))) { Frame = new Placement((double[])now.Position.Clone(), (double[])now.Rotation.Clone()) };
        }
    }
    // A scene whose objects can stand in a frame that moves, such as the child objects of a scene object in their top
    // scene object. The engine evaluates such an object's underlying placement at the frame's placement now, so a removal returns
    // the object to its place in the frame wherever the frame stands.
    public interface IFramedScene {
        // The map placement of the frame the object stands in now, or null when it stands in none.
        Placement FramePlacement(object target);
    }
    // A scene whose objects record their own placement relative to the object above them (PartHolds): the scene patch file
    // holds where the object stands while the object above it stands at its map placement. The window shows and takes
    // map placements; an edit converts the placement it takes into the recorded one before it writes it.
    public interface IRecordedPlacementScene {
        // The placement the scene patch file records for the target standing at the map placement.
        Placement Recorded(object target, Placement inMap);
        // The map placement of a recorded placement of the target now. A placement read in a frame is already one.
        Placement InMap(object target, Placement recorded);
    }
    // Desired or observed state of one object. A null member is left untouched by an application.
    public sealed class ObjectState {
        public ObjectState(Placement placement, bool? active) : this(placement, active, null) { }
        public ObjectState(Placement placement, bool? active, MarkerBinding marker) : this(placement, active, marker, null) { }
        public ObjectState(Placement placement, bool? active, MarkerBinding marker, CrossingCoverage coverage) : this(placement, active, marker, coverage, null, null) { }
        public ObjectState(Placement placement, bool? active, MarkerBinding marker, CrossingCoverage coverage, CrossingEquipment equipment) : this(placement, active, marker, coverage, equipment, null) { }
        public ObjectState(Placement placement, bool? active, MarkerBinding marker, CrossingCoverage coverage, StarterSetting starter) : this(placement, active, marker, coverage, null, starter) { }
        public ObjectState(Placement placement, bool? active, MarkerBinding marker, CrossingCoverage coverage, CrossingEquipment equipment, StarterSetting starter) { Placement = placement; Active = active; Marker = marker; Coverage = coverage; Equipment = equipment; Starter = starter; }
        public Placement Placement { get; }
        public bool? Active { get; }
        // The track-marker relationship: a location or explicit detachment. Null leaves it untouched, and an object without one reads null.
        public MarkerBinding Marker { get; }
        // The crossing-coverage relationship. Null leaves it untouched, and an object without one reads null. This mod's coverage
        // lists only its explicit circuits; observed coverage lists every circuit of the crossing.
        public CrossingCoverage Coverage { get; }
        // The crossing-equipment relationship: equipment added to a crossing controller. Null leaves it untouched, and an object
        // without one reads null. The mod's equipment lists only its explicit additions; observed equipment is complete.
        public CrossingEquipment Equipment { get; }
        // The starter placement of a starter placement marker: the setup whose new-game car placement reads the marker, and
        // the marker's track location. Null leaves it untouched, and any other object reads null.
        public StarterSetting Starter { get; }
        // The construction-segment relationship of a rail bridge construction: the segments its construction controller
        // reads. Null leaves it untouched, and an object without a construction controller reads null.
        public ConstructionSegments Segments { get; private set; }
        // The same state with this construction-segment setting.
        public ObjectState WithSegments(ConstructionSegments segments) => new ObjectState(Placement, Active, Marker, Coverage, Equipment, Starter) { LeavesActivity = LeavesActivity, Segments = segments, MarkerId = MarkerId };
        // The id of a generic track marker. A modification's replacement ID, or null to leave the id untouched; a
        // generic track marker reads its current id, and any other object reads null.
        public string MarkerId { get; private set; }
        // The same state with this marker ID.
        public ObjectState WithMarkerId(string markerId) => new ObjectState(Placement, Active, Marker, Coverage, Equipment, Starter) { LeavesActivity = LeavesActivity, Segments = Segments, MarkerId = markerId };
        // Tells the two meanings of a null Active apart for an object whose activity follows a track group. True: this is
        // one modification's own state, and the application leaves the activity as it is, including an explicit disable
        // that another modification installed. False: this is the complete desired state, and a null Active returns the
        // object to its group-driven activity, which releases an explicit disable.
        public bool LeavesActivity { get; private set; }
        public ObjectState LeavingActivity() => new ObjectState(Placement, Active, Marker, Coverage, Equipment, Starter) { LeavesActivity = true, Segments = Segments, MarkerId = MarkerId };
    }
    // A map context that holds map patches, as the native map editor's does while it edits a map patch. In a save a map
    // patch is never in the map context.
    public interface IMapPatchContext {
        IEnumerable<string> MapPatchIds { get; }
    }
    // Whether a scene patch file's scene target map lets it apply in the map context, shared by the engine and scene patch
    // editing.
    public static class SceneTargetMapCheck {
        public const string Mismatch = "The scene target map is neither the loaded map nor one of its layers";
        // The map the game loads when Editor is clicked in the main menu, before a map is chosen (MapIdentifiers.EditorDefault,
        // MenuManager.EditorGameSetup). New Game never offers it.
        public const string EditorDefaultMap = "rr/editor-default-map";
        // The maps a scene patch file can name in the scene's map context: its map IDs without the map patches.
        public static string[] Valid(IScene scene) {
            var patches = scene is IMapPatchContext context ? (context.MapPatchIds ?? Enumerable.Empty<string>()).ToArray() : new string[0];
            return scene.MapIds.Where(id => !patches.Contains(id, StringComparer.Ordinal)).ToArray();
        }
        // Why a scene patch file naming mapId does not apply in the scene's map context, or null. A map patch in the native
        // map editor's map context does not count: the game skips a scene patch file that names a map patch in play.
        public static string Problem(IScene scene, string mapId) {
            var maps = scene.MapIds.ToArray();
            if (!maps.Contains(mapId, StringComparer.Ordinal)) return Mismatch;
            if (scene is IMapPatchContext context && (context.MapPatchIds ?? Enumerable.Empty<string>()).Contains(mapId, StringComparer.Ordinal))
                return mapId + " is a map patch. The scene patch file wasn't applied; bottom map layer: "
                    + PatchEditingSession.SceneTargetMapFor(maps) + ". " + WindowGuidance.UseBottomLayer;
            return null;
        }
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
        // What a failure did to the scene: refused before mutation, restored, or left with incomplete cleanup.
        public FailureKind Failure { get; internal set; }
        internal bool WasGuarded { get; set; }
        internal EquipmentCleanupContext EquipmentCleanup { get; set; }
        // For an incomplete cleanup: the remaining or uncertain state, one line per setting.
        public IReadOnlyList<string> Remaining { get; internal set; } = Array.Empty<string>();
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
    public sealed class ScenePatchFileResult {
        public string Error { get; internal set; }
        public IReadOnlyDictionary<string, Outcome> Outcomes { get; internal set; }
        public bool MapEditorOnly { get; internal set; }
        public string EarlyRouteUnavailable { get; internal set; }
        // Why the scene patch file does not apply in this map context, with the map it names and the loaded maps, or null.
        // The file is skipped as a whole; this is no failure of the file or of the patcher.
        public string NotApplicable { get; internal set; }
    }
    public sealed class PatchEngine {
        readonly IScene scene;
        string NotApplicable(string problem, string mapId) {
            if (problem != SceneTargetMapCheck.Mismatch) return problem;
            var maps = scene.MapIds.ToArray();
            // The editor's transient default map, loaded before a map is chosen, is no map a player plays: nothing warns for it.
            if (maps.Length != 0 && maps[0] == SceneTargetMapCheck.EditorDefaultMap) return null;
            return "The scene patch file is for map " + mapId + ", which is neither the loaded map " + (maps.Length == 0 ? "unknown" : maps[0]) +
                " nor one of its layers (" + (maps.Length < 2 ? "none" : String.Join(", ", maps.Skip(1))) + "). Its modifications weren't applied";
        }
        readonly Dictionary<string, Outcome> failed = new Dictionary<string, Outcome>();
        readonly HashSet<object> unsafeTargets = new HashSet<object>(new ByReference());
        // The remaining or uncertain state each unsafe target was left with, for the recovery guidance.
        readonly Dictionary<object, IReadOnlyList<string>> remainingState = new Dictionary<object, IReadOnlyList<string>>(new ByReference());
        // Restored targets whose dependent output, such as regenerated terrain, has not yet verified.
        readonly HashSet<object> awaitingOutput = new HashSet<object>(new ByReference());
        readonly Dictionary<object, ObjectState> underlying = new Dictionary<object, ObjectState>();
        // The identity each object was resolved under, in resolution order, so a later load attempt can tell a retained
        // object from a reconstructed one.
        readonly Dictionary<object, TargetIdentity> identities = new Dictionary<object, TargetIdentity>(new ByReference());
        readonly List<object> resolutionOrder = new List<object>();
        sealed class ByReference : IEqualityComparer<object> {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
        // Addition IDs that more than one modification of the scene patch file being applied uses.
        HashSet<string> sharedAdditions = new HashSet<string>(StringComparer.Ordinal);
        internal IScene Scene { get { return scene; } }
        internal sealed class Modification {
            internal string Id; internal TargetIdentity Identity; internal ObjectState State; internal string[] Dependencies;
            // For a mod object, the scene object it is built from; null for a modification of an existing target.
            internal TargetIdentity Source;
            // For a mod object, the model and build of its source as the placing click recorded it; null when unknown.
            internal string SourceBuild;
            // For a mod object, the marker IDs of the scene markers below it, in hierarchy order; empty when it holds none.
            internal string[] PartMarkerIds = new string[0];
            // For a duplicated spawn point, the object name it has in the game: its teleport name; null for its mark name.
            internal string TeleportName;
        }
        // The start of an attempt trace line: the debug output of one engine stage, as JSON that the native observation
        // and the attempt summary read.
        public const string AttemptTrace = "Attempt trace: ";
        void Observe(string stage, string scenePatchFile, string id = null, string path = null, string error = null) {
            // Observation cannot alter application or failure handling; with debug off it builds nothing.
            PatcherLog.Debug(() => AttemptTrace + new JObject {
                ["stage"] = stage, ["document"] = scenePatchFile, ["id"] = id,
                ["path"] = path, ["error"] = error,
                ["engineRuntimeId"] = RuntimeHelpers.GetHashCode(this)
            }.ToString(Newtonsoft.Json.Formatting.None));
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
                try { present = (Matches(identity) ?? Array.Empty<object>()).Any(m => ReferenceEquals(m, target)) || (scene as IRetainedScene)?.Unsettled(target) == true; }
                catch (Exception e) { failures.Add(Describe(identity) + ": the retained object could not be checked, so its state is kept: " + PatcherLog.Reason(e)); retained++; continue; }
                if (present) { retained++; continue; }
                try { (scene as IRetainedScene)?.Release(target); }
                catch (Exception e) { failures.Add(Describe(identity) + ": owned state of the reconstructed object remains: " + PatcherLog.Reason(e)); continue; }
                identities.Remove(target); resolutionOrder.Remove(target);
                underlying.Remove(target); unsafeTargets.Remove(target); awaitingOutput.Remove(target); remainingState.Remove(target);
                foreach (var built in builtObjects.Values) foreach (var id in built.Where(pair => ReferenceEquals(pair.Value, target)).Select(pair => pair.Key).ToArray()) built.Remove(id);
                released++;
            }
            return new ReentryResult(retained, released, failures);
        }
        static string Describe(TargetIdentity identity) => identity.Label;
        public ScenePatchFileResult Apply(string json) { return Apply(json, ""); }
        public ScenePatchFileResult Apply(string json, string scenePatchFileKey) {
            return Apply(json, scenePatchFileKey, null);
        }
        public ScenePatchFileResult Apply(string json, string scenePatchFileKey, IReadOnlyDictionary<string, InspectedTarget> inspectedTargets) {
            return ApplyScenePatchFile(json, scenePatchFileKey, inspectedTargets, null, false);
        }
        // An explicit file-version choice may retry this scene patch file's failed IDs, subject to the same cleanup guards.
        internal ScenePatchFileResult ApplyFileVersion(string json, string scenePatchFileKey) {
            return ApplyScenePatchFile(json, scenePatchFileKey, null, null, true);
        }
        // The complete envelope is checked before selecting a dependency-free relationship repair for this native check.
        public ScenePatchFileResult BeforeNativeCheck(string json, string scenePatchFileKey, TargetIdentity target) {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return ApplyScenePatchFile(json, scenePatchFileKey, null, (string id, JObject entry, out string unavailable) => EarlyRepair(id, entry, target, out unavailable), false);
        }
        // A new game reads the start setup's spawn point, or the first active spawn point, before the world-load apply
        // (StateManager.ApplyNewGameSetup, GlobalGameManager.InitializeDefaultSpawnIfNeeded). The complete envelope is
        // checked; then only the spawn point modifications that can take effect now apply, through the same execution as
        // the world load, which later applies every modification again.
        public ScenePatchFileResult BeforeNewGameStart(string json, string scenePatchFileKey) {
            return ApplyScenePatchFile(json, scenePatchFileKey, null, EarlySpawn, false);
        }
        // Whether a modification runs in an early route; unavailable names why one that the route is for has to wait.
        delegate bool EarlyRoute(string id, JObject entry, out string unavailable);
        ScenePatchFileResult ApplyScenePatchFile(string json, string scenePatchFileKey, IReadOnlyDictionary<string, InspectedTarget> inspectedTargets, EarlyRoute early, bool retryFailures) {
            if (scenePatchFileKey == null) throw new ArgumentNullException(nameof(scenePatchFileKey));
            Observe("document-dispatch", scenePatchFileKey);
            var result = new ScenePatchFileResult();
            var outcomes = new Dictionary<string, Outcome>(); result.Outcomes = outcomes;
            var scenePatchFile = ScenePatchFile.Load(json);
            if (!scenePatchFile.CanWrite) { result.Error = scenePatchFile.Error; result.MapEditorOnly = true; return result; }
            try {
                var mapProblem = SceneTargetMapCheck.Problem(scene, (string)scenePatchFile.Root["sourceMap"]["mapId"]);
                if (mapProblem != null) { result.Error = mapProblem; result.MapEditorOnly = true; result.NotApplicable = NotApplicable(mapProblem, (string)scenePatchFile.Root["sourceMap"]["mapId"]); return result; }
                if (scene.Capability == Capability.Incompatible) { result.Error = "Placement capability is incompatible"; return result; }
                var entries = Entries(scenePatchFile.Modifications);
                sharedAdditions = SharedAdditionIds(entries.Values);
                Plan(scenePatchFile.Modifications, scenePatchFileKey);
                var visiting = new HashSet<string>();
                // Modifications that dependsOn does not order run in ordinal key order, whatever the order of the text.
                foreach (var id in entries.Keys) {
                    if (early != null && !early(id, entries[id], out var unavailable)) {
                        if (unavailable != null) result.EarlyRouteUnavailable = unavailable;
                        continue;
                    }
                    Execute(id, entries, outcomes, visiting, scenePatchFileKey, inspectedTargets, retryFailures);
                }
                // A mod object exists only through its modification: one that an earlier application of this scene
                // patch file built and the file no longer holds is removed. An early route applies only a part.
                if (early == null) foreach (var gone in BuiltFor(scenePatchFileKey).Where(pair => !entries.TryGetValue(pair.Key, out var entry) || entry[ModObjects.SourceMember] == null).ToArray())
                    try { RemoveModObject(scenePatchFileKey, gone.Key, gone.Value); } catch (Exception e) { PatcherLog.Debug("A mod object that left its scene patch file could not be removed: " + PatcherLog.Reason(e)); }
            } catch (Exception e) { result.Error = "Scene check failed: " + PatcherLog.Reason(e); }
            finally { Unplan(); }
            return result;
        }
        // Gives a scene that judges modifications with their scene patch file the desired state of every object that a starter
        // placement of the scene patch file sets. A modification that cannot be read or resolved is left out.
        internal void Plan(JObject modifications, string owner) {
            if (!(scene is IPlannedStateScene planned)) return;
            var desired = new Dictionary<object, ObjectState>(new ByReference());
            foreach (var entry in Entries(modifications)) {
                if (entry.Value["starterPlacement"] == null) continue;
                try {
                    var parsed = Parse(entry.Value, owner, entry.Key);
                    var target = ResolveUnique(parsed.Identity);
                    desired[target] = desired.TryGetValue(target, out var before) ? Overlay(before, parsed.State) : parsed.State;
                } catch (InvalidOperationException) { }
            }
            planned.Plan(desired);
        }
        internal void Unplan() { (scene as IPlannedStateScene)?.Plan(null); }
        bool EarlyRepair(string id, JObject entry, TargetIdentity target, out string unavailable) {
            unavailable = null;
            try {
                var parsed = Parse(entry, null, id); var identity = parsed.Identity;
                if (identity.Scene != target.Scene || identity.Path != target.Path || identity.Component != target.Component ||
                    identity.Structure != null && identity.Structure != target.Structure || parsed.State.Marker?.Location == null) return false;
                // A replacement ID changes the id the native check registers under, so it waits for the world load.
                if (parsed.Dependencies.Length != 0 || parsed.State.Placement != null || parsed.State.Active != null || parsed.State.MarkerId != null) {
                    unavailable = "Operation " + id + " needs world-load scheduling for dependencies, placement, activity, or a replacement ID.";
                    return false;
                }
                // A prerequisite missing at OnEnable is not yet a failed runtime attempt. The definitive world-load
                // application still owns invalid-definition reporting and the existing guarded-failure policy.
                try {
                    var matches = Matches(identity);
                    if (matches == null || matches.Count != 1 || matches[0] == null) throw new InvalidOperationException("Target is not yet unique and available");
                    scene.Preflight(matches[0]);
                    CheckRelationships(matches[0], parsed.State);
                    return true;
                } catch (Exception e) { unavailable = "Early repair prerequisites unavailable: " + PatcherLog.Reason(e); return false; }
            } catch (InvalidOperationException) { return false; }
        }
        // A spawn point modification runs early when it sets only a fixed placement or activity, has no dependencies, and its
        // target is unique and passes preflight now. Anything else waits for the world load, untouched and without a
        // recorded failure: a prerequisite missing now is not yet a failed attempt.
        bool EarlySpawn(string id, JObject entry, out string unavailable) {
            unavailable = null;
            try {
                var parsed = Parse(entry, null, id); var identity = parsed.Identity;
                // A duplicated spawn point is built at the world load; below every map spawn point, no new game starts there.
                if (parsed.Source != null) return false;
                if (identity.Component != SpawnPoints.Component || identity.Structure != null && identity.Structure != SpawnPoints.Structure) return false;
                var state = parsed.State;
                if (parsed.Dependencies.Length != 0 || state.Placement?.Following != null || state.Marker != null || state.Coverage != null || state.Equipment != null ||
                    state.Starter != null || state.Segments != null || state.MarkerId != null) {
                    unavailable = "Operation " + id + " needs world-load scheduling for dependencies, track-following placement, or relationships.";
                    return false;
                }
                try {
                    var matches = Matches(identity);
                    if (matches == null || matches.Count != 1 || matches[0] == null) throw new InvalidOperationException("The target isn't yet uniquely identified and available.");
                    if (IsUnsafe(matches[0])) throw new InvalidOperationException("Cleanup for this object couldn't be verified.");
                    scene.Preflight(matches[0]);
                    return true;
                } catch (Exception e) { unavailable = "A prerequisite for placing spawn points before the new game starts is unavailable: " + PatcherLog.Reason(e); return false; }
            } catch (InvalidOperationException) { return false; }
        }
        Outcome Execute(string id, SortedDictionary<string,JObject> entries, Dictionary<string,Outcome> outcomes, HashSet<string> visiting, string scenePatchFileKey, IReadOnlyDictionary<string, InspectedTarget> inspectedTargets, bool retryFailures) {
            Outcome prior;
            if (outcomes.TryGetValue(id, out prior)) return prior;
            var outcome = new Outcome();
            if (!visiting.Add(id)) { outcome.Error = "The modification dependencies form a cycle"; outcome.Cause = FailureCause.InvalidDefinition; outcome.Failure = FailureKind.Refused; return outcome; }
            var key = scenePatchFileKey.Length + ":" + scenePatchFileKey + id;
            // The mod object this attempt built: a failure after that removes it again, so nothing half-made stays.
            object builtNow = null;
            try {
                if (failed.ContainsKey(key)) {
                    var priorFailure = failed[key];
                    if (retryFailures && (priorFailure.ResolvedTarget == null || !IsUnsafe(priorFailure.ResolvedTarget))) failed.Remove(key);
                }
                if (failed.ContainsKey(key)) {
                    Observe("retry-blocked", scenePatchFileKey, id);
                    var previous = failed[key];
                    outcome.Error = "Operation already failed during this load: " + previous.Error;
                    outcome.Cause = previous.Cause; outcome.MapEditorOnly = previous.MapEditorOnly; outcome.Target = previous.Target;
                    outcome.Failure = FailureKind.AlreadyFailed; outcome.Remaining = previous.Remaining;
                    outcomes[id] = outcome;
                    return outcome;
                }
                var item = entries[id];
                var unsupported = UnsupportedContent(id, item);
                if (unsupported != null) { outcome.MapEditorOnly = true; throw new InvalidOperationException(unsupported); }
                var shared = AdditionIds(item).FirstOrDefault(sharedAdditions.Contains);
                if (shared != null) throw new InvalidPatchDefinitionException(SharedAddition(shared));
                // A failure of the dependencies names the object the modification is for, when its target reads.
                try { outcome.Target = ParseTarget(item["target"] as JObject); } catch (InvalidOperationException) { }
                string[] dependencies;
                try { dependencies = Dependencies(item); }
                catch (InvalidOperationException e) { throw new InvalidPatchDefinitionException(PatcherLog.Reason(e)); }
                foreach (var dep in dependencies) {
                    if (!entries.ContainsKey(dep)) throw new InvalidPatchDefinitionException("Dependency " + dep + " is missing from the scene patch file");
                    var dependency = Execute(dep, entries, outcomes, visiting, scenePatchFileKey, inspectedTargets, retryFailures);
                    if (!dependency.Applied) { outcome.Cause = dependency.Cause; outcome.MapEditorOnly = dependency.MapEditorOnly; throw new AttemptFailure(FailureKind.DependencyFailed, "A required modification failed: " + dep); }
                }
                Modification parsed;
                try { outcome.Target = ParseTarget(item["target"] as JObject); parsed = Parse(item, scenePatchFileKey, id); }
                catch (InvalidOperationException e) {
                    outcome.Cause = FailureCause.InvalidDefinition;
                    outcome.MapEditorOnly = e.Message.StartsWith("Unsupported", StringComparison.Ordinal) || e.Message == "Invalid or unsupported target";
                    throw;
                }
                var identity = parsed.Identity;
                outcome.Target = identity;
                Observe("resolve", scenePatchFileKey, id, identity.Path);
                IReadOnlyList<object> matches;
                try { matches = Matches(identity); } catch (InvalidOperationException) { outcome.Cause = FailureCause.InvalidDefinition; throw; }
                // A mod object that does not stand yet is built from its source, at its placement, before it resolves.
                if (parsed.Source != null && UnresolvedTarget(matches) == TargetMissing) {
                    try { builtNow = Build(parsed, scenePatchFileKey); } catch (InvalidOperationException) { outcome.Cause = FailureCause.InvalidDefinition; throw; }
                    matches = Matches(identity);
                }
                var unresolved = UnresolvedTarget(matches);
                // Applying a scene patch file tells the modder what to correct; an edit adds its own recovery guidance.
                if (unresolved != null) { outcome.Cause = FailureCause.InvalidDefinition; throw new InvalidOperationException(inspectedTargets == null ? unresolved + " " + (unresolved == TargetMissing ? TargetMissingAction : TargetAmbiguousAction) : unresolved); }
                var resolved = matches[0];
                Track(identity, resolved);
                var mapContext = scene.MapIds.ToArray();
                InspectedTarget expected = null;
                if (inspectedTargets != null && (!inspectedTargets.TryGetValue(id, out expected) || expected == null))
                    throw new InvalidOperationException("An inspected target is required for every edited modification");
                expected?.Validate(scene, identity, resolved);
                outcome.ResolvedTarget = resolved;
                if (IsUnsafe(resolved)) throw Guarded(resolved, "Cleanup for this object could not be verified");
                Observe("preflight", scenePatchFileKey, id, identity.Path);
                scene.Preflight(resolved);
                // One modification's own state: an omitted active leaves the activity another modification set.
                var state = Evaluate(parsed.State.LeavingActivity());
                CheckRelationships(resolved, state);
                Observe("capture", scenePatchFileKey, id, identity.Path);
                Underlying(resolved);
                // The state before this attempt, to name what an unverified restoration leaves behind. A read that fails
                // leaves that comparison uncertain; it does not stop the attempt.
                ObjectState before;
                try { before = scene.Read(resolved); } catch { before = null; }
                var snapshot = scene.Capture(resolved);
                // Capture and preflight may invoke native callbacks. Never redirect to a new match.
                GuardMutation(identity, resolved, mapContext, expected);
                try {
                    Observe("mutation", scenePatchFileKey, id, identity.Path);
                    scene.Apply(resolved, state);
#if SCENE_PATCHER_FAULTS
                    FaultInjection.TripAfterMutation();
#endif
                    Observe("applied", scenePatchFileKey, id, identity.Path);
                } catch (Exception e) {
                    bool restored = false; Exception restoreError = null;
                    Observe("restore-request", scenePatchFileKey, id, identity.Path);
                    try {
#if SCENE_PATCHER_FAULTS
                        FaultInjection.TripRestoration();
#endif
                        restored = scene.Restore(resolved, snapshot);
                    } catch (Exception r) { restoreError = r; }
                    Observe("restore-result", scenePatchFileKey, id, identity.Path, restored ? "verified" : "unverified");
                    if (!restored) {
                        var left = RemainingState.Describe(before, () => scene.Read(resolved), out var settingsMatch, PatcherLog.Reason(restoreError));
                        SetUnsafe(resolved, true, left);
                        var cleanup = e is EmptyEquipmentObjectException && restoreError is EmptyEquipmentObjectException && settingsMatch
                            ? EquipmentCleanupContext.WorldLoad : EquipmentCleanupContext.None;
                        throw new AttemptFailure(FailureKind.IncompleteCleanup, "The change failed. Cleanup could not be verified. Reason: " + PatcherLog.Reason(e), left, equipmentCleanup: cleanup);
                    }
                    if (AwaitOutput(resolved)) throw new AttemptFailure(FailureKind.AwaitingOutput, "The change failed and the object is back as it was. The game is still regenerating its dependent output. Further changes must wait until that output is verified. Reason: " + PatcherLog.Reason(e));
                    throw new AttemptFailure(FailureKind.Restored, "The change failed. The object is back as it was. Reason: " + PatcherLog.Reason(e));
                }
                outcome.Applied = true;
            } catch (Exception e) {
#if SCENE_PATCHER_FAULTS
                FaultInjection.EndAttempt();
#endif
                outcome.Error = PatcherLog.Reason(e); if (e is InvalidPatchDefinitionException) outcome.Cause = FailureCause.InvalidDefinition;
                // An incomplete cleanup keeps its object for Recheck; any other failure leaves no mod object behind.
                if (builtNow != null && !unsafeTargets.Contains(builtNow)) { try { RemoveModObject(scenePatchFileKey, id, builtNow); outcome.ResolvedTarget = null; } catch { } }
                if (outcome.Failure == FailureKind.None) {
                    var attempt = e as AttemptFailure;
                    outcome.Failure = attempt?.Kind ?? FailureKind.Refused;
                    if (attempt != null) { outcome.Remaining = attempt.Remaining; outcome.WasGuarded = attempt.WasGuarded; outcome.EquipmentCleanup = attempt.EquipmentCleanup; }
                }
                failed[key] = outcome; Observe("failed", scenePatchFileKey, id, error: PatcherLog.Reason(e));
            }
            finally { visiting.Remove(id); }
            outcomes[id] = outcome;
            return outcome;
        }
        // The mod objects this engine built, by the key of their scene patch file and their modification ID.
        readonly Dictionary<string, Dictionary<string, object>> builtObjects = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        Dictionary<string, object> BuiltFor(string scenePatchFileKey) {
            if (!builtObjects.TryGetValue(scenePatchFileKey ?? "", out var built)) builtObjects[scenePatchFileKey ?? ""] = built = new Dictionary<string, object>(StringComparer.Ordinal);
            return built;
        }
        // Builds the mod object of a modification from its source. The source resolves exactly, as a target does.
        internal object Build(Modification parsed, string scenePatchFileKey) {
            if (!(scene is IModObjectScene builder)) throw new InvalidOperationException(ModObjects.Unavailable);
            var sources = Matches(parsed.Source);
            var unresolved = UnresolvedTarget(sources);
            var source = unresolved == null ? sources[0] : null;
            SourceInstance instead = null;
            // The one exception to the exactly-one rule (ADR 0008): a missing original gives way to the first loaded
            // instance with the same model and build, in the scene's fixed order. An ambiguous original does not.
            // A duplicated spawn point takes the first loaded map spawn point in that order, whatever its build and
            // with or without the source's build in the file. No mod object stands in for another one's original.
            if (unresolved == TargetMissing && scene is IModObjectSources identical) {
                var candidates = parsed.Identity.Structure == SpawnPoints.Structure ? identical.Instances(parsed.Source)
                    : parsed.SourceBuild != null ? identical.Identical(parsed.Source, parsed.SourceBuild) : null;
                instead = candidates?.FirstOrDefault(i => i?.Handle != null && !ModObjects.Within(i.Path) && !IsBuilt(i.Handle));
            }
            if (source == null && instead == null) throw new InvalidOperationException(unresolved == TargetMissing ? ModObjects.SourceMissing : ModObjects.SourceAmbiguous);
            var placement = Evaluate(parsed.State).Placement;
            // A scene marker never takes its source's marker ID: a second holder would take the source's registration.
            var markerId = parsed.State.MarkerId;
            if (ModObjects.IsSceneMarker(parsed.Identity.Structure)) {
                if (markerId == null) throw new InvalidPatchDefinitionException(ModObjects.NeedsMarkerId);
                var refused = scene is IMarkerIdScene ids ? ids.NewMarkerIdRefusal(source ?? instead.Handle, markerId) : ReplacementMarkerIds.NotSupported;
                if (refused != null) throw new InvalidPatchDefinitionException(refused);
            }
            // The mod object stands beside the object it is built from, under the name its target gives it: beside its
            // original, or beside the identical instance in that instance's parent; a part's copy beside the group of
            // that part. No parent is created.
            // Each scene marker below the mod object, a pad of a duplicated crossing, takes a marker ID of its own too.
            var partScene = scene as IModObjectPartScene;
            var partSources = partScene?.PartMarkers(source ?? instead.Handle) ?? Array.Empty<object>();
            if (partSources.Count != parsed.PartMarkerIds.Length) throw new InvalidPatchDefinitionException(ModObjects.PartMarkerIdCount(partSources.Count, parsed.PartMarkerIds.Length));
            for (var i = 0; i < partSources.Count; i++) {
                var refused = scene is IMarkerIdScene partIds ? partIds.NewMarkerIdRefusal(partSources[i], parsed.PartMarkerIds[i]) : ReplacementMarkerIds.NotSupported;
                if (refused != null) throw new InvalidPatchDefinitionException(refused);
            }
            var from = source != null ? parsed.Source.Path : instead.Path;
            var parent = ModObjects.StandsIn(parsed.Identity.Path, parsed.Source.Path, from);
            var root = (parent == null ? "" : parent + "/") + ModObjects.NameOf(parsed.Identity.Path);
            var built = (partSources.Count == 0 ? builder.Build(parsed.Identity.At(root), source ?? instead.Handle, placement, markerId)
                : partScene.Build(parsed.Identity.At(root), source ?? instead.Handle, placement, markerId, parsed.PartMarkerIds)) ?? throw new InvalidOperationException(ModObjects.Unavailable);
            try { Prepare(parsed, built, source ?? instead.Handle); }
            catch { try { builder.Remove(built); } catch { } throw; }
            if (source == null) PatcherLog.Warning(ModObjects.FallbackWarning(scenePatchFileKey, parsed.Id, parsed.Source.Path, instead.Path));
            standingRoots[ModObjects.MarkOf(root)] = root;
            BuiltFor(scenePatchFileKey)[parsed.Id] = built;
            return built;
        }
        bool IsBuilt(object handle) => builtObjects.Values.Any(built => built.Values.Any(o => ReferenceEquals(o, handle)));
        // What a mod object of its family takes after it is built: a spawn point's copy a priority below every map spawn
        // point, and its teleport name as its object name. Its radius stays its source's.
        void Prepare(Modification parsed, object built, object source) {
            if (parsed.Identity.Structure != SpawnPoints.Structure) return;
            if (!(scene is ISpawnPointPriorities priorities)) throw new InvalidOperationException(SpawnPointCopies.Unavailable);
            var priority = SpawnPointCopies.Priority(priorities.MapPriorities(parsed.Identity.Scene), priorities.Priority(source));
            priorities.SetPriority(built, priority);
            if (priorities.Priority(built) != priority) throw new InvalidOperationException("The priority of the duplicated spawn point did not verify.");
            if (parsed.TeleportName != null) Name(built, parsed.TeleportName);
        }
        // Gives a mod object its object name in the game, or with null its mark name, and checks it.
        internal void Name(object modObject, string name) {
            if (!(scene is IModObjectNames names)) throw new InvalidOperationException(ModObjects.NamesUnavailable);
            names.Name(modObject, name);
            if (name != null && names.NameOf(modObject) != name) throw new InvalidOperationException("The object name of the mod object did not verify.");
        }
        // Where each mod object this engine built stands, by its mark. A target written below a mod object resolves where
        // the mod object stands, whatever parent the written path names.
        readonly Dictionary<string, string> standingRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        TargetIdentity Standing(TargetIdentity identity) {
            var mark = ModObjects.MarkOf(identity.Path);
            if (mark == null || !standingRoots.TryGetValue(mark, out var root)) return identity;
            var path = ModObjects.Moved(identity.Path, root);
            return path == identity.Path ? identity : identity.At(path);
        }
        // Removes a mod object and everything this engine keeps for it and for the objects resolved below it.
        internal void RemoveModObject(string scenePatchFileKey, string id, object modObject) {
            var path = identities.TryGetValue(modObject, out var known) ? ModObjects.Canonical(known.Path) : null;
            (scene as IModObjectScene)?.Remove(modObject);
            BuiltFor(scenePatchFileKey).Remove(id);
            var mark = known == null ? null : ModObjects.MarkOf(known.Path);
            if (mark != null) standingRoots.Remove(mark);
            foreach (var target in resolutionOrder.Where(t => ReferenceEquals(t, modObject) || (path != null && ModObjects.Canonical(identities[t].Path).StartsWith(path + "/", StringComparison.Ordinal))).ToArray()) Forget(target);
        }
        void Forget(object target) {
            try { (scene as IRetainedScene)?.Release(target); } catch { }
            identities.Remove(target); resolutionOrder.Remove(target);
            underlying.Remove(target); unsafeTargets.Remove(target); awaitingOutput.Remove(target); remainingState.Remove(target);
        }
        // The state this object had before any modification of this engine touched it. An object that stood in a frame
        // keeps its place in that frame: its placement follows the frame to where the frame stands now.
        internal ObjectState Underlying(object target) {
            ObjectState state;
            if (!underlying.TryGetValue(target, out state)) underlying.Add(target, state = scene.Read(target));
            if (state?.Placement?.Frame == null || !(scene is IFramedScene framed)) return state;
            var now = framed.FramePlacement(target);
            return now == null ? state : new ObjectState(state.Placement.InFrame(now), state.Active, state.Marker, state.Coverage, state.Equipment, state.Starter).WithSegments(state.Segments).WithMarkerId(state.MarkerId);
        }
        // Set for the duration of a reconciliation that resolves the same targets several times a second.
        internal Func<TargetIdentity, object> ResolveOverride;
        internal object ResolveUnique(TargetIdentity identity) { return ResolveOverride != null ? ResolveOverride(identity) : ResolveFresh(identity); }
        // The objects the identity names. A pad marker ID names a crossbuck at a shared path; every other target refuses
        // it with the reason, before anything is resolved.
        IReadOnlyList<object> Matches(TargetIdentity identity) {
            identity = Standing(identity);
            if (identity.PadMarkerId != null) {
                var refusal = scene is IPadMarkerIdentityScene accepting ? accepting.PadMarkerRefusal(identity) : TargetIdentity.PadMarkerNotAccepted;
                if (refusal != null) throw new InvalidOperationException(refusal);
            }
            if (!identity.Keyed) return scene.Resolve(identity);
            // A key picks among the objects at the path and component: exactly, never the nearest.
            var keys = scene as ITargetKeyScene;
            return (scene.Resolve(identity.Unkeyed()) ?? Array.Empty<object>()).Where(o => o != null && keys != null && TargetKeys.Matches(identity, keys.KeyOf(o))).ToArray();
        }
        // The refusals of a target that does not match exactly one object of the loaded scene.
        public const string TargetMissing = "This target doesn't match any object in the loaded map.";
        public const string TargetMissingAction = WindowGuidance.CompareTargetIdentity;
        public const string TargetAmbiguous = "This target matches more than one object in the loaded map.";
        public const string TargetAmbiguousAction = WindowGuidance.UseSpecificTarget;
        static string UnresolvedTarget(IReadOnlyList<object> matches) =>
            matches == null || matches.Count == 0 || (matches.Count == 1 && matches[0] == null) ? TargetMissing : matches.Count != 1 ? TargetAmbiguous : null;
        internal object ResolveFresh(TargetIdentity identity) {
            var matches = Matches(identity);
            var unresolved = UnresolvedTarget(matches);
            if (unresolved != null) throw new InvalidOperationException(unresolved);
            Track(identity, matches[0]);
            return matches[0];
        }
        // The immediate guard before any mutation: same prerequisites, same unique object, same map context,
        // and for an edit in scene patch editing the same inspected observation.
        internal void GuardMutation(TargetIdentity identity, object resolved, string[] mapContext, InspectedTarget expected) {
            scene.Preflight(resolved);
            var current = Matches(identity);
            if (current == null || current.Count != 1 || !ReferenceEquals(current[0], resolved))
                throw new InvalidOperationException("Target changed before mutation. " + WindowGuidance.RefreshAndInspect);
            if (!mapContext.SequenceEqual(scene.MapIds))
                throw new InvalidOperationException("Map context changed before mutation");
            expected?.Validate(scene, identity, resolved);
        }
        // Refuses a relationship the object does not support and a relationship target that does not resolve uniquely.
        // A relationship scene also checks the settings its relationships make unsupported.
        internal void CheckRelationships(object target, ObjectState state) {
            if (state?.Segments != null && !(scene is IConstructionSegmentScene)) throw new InvalidPatchDefinitionException(ConstructionSegmentRule.NoRelationship);
            if (state?.MarkerId != null && !(scene is IMarkerIdScene)) throw new InvalidPatchDefinitionException(ReplacementMarkerIds.NotSupported);
            if (scene is IRelationshipScene relationships) { relationships.CheckRelationships(target, state); return; }
            if (state?.Marker != null) throw new InvalidOperationException("This object has no supported track-marker relationship");
            if (state?.Coverage != null) throw new InvalidOperationException("This object has no supported crossing-coverage relationship");
            if (state?.Equipment != null) throw new InvalidOperationException("This object has no supported crossing-equipment relationship");
            if (state?.Starter != null) throw new InvalidPatchDefinitionException(StarterPlacements.NotAStarter);
            if (state?.Segments != null) throw new InvalidPatchDefinitionException(ConstructionSegmentRule.NoRelationship);
        }
        // A target is unsafe while its cleanup is unverified, including restored dependent output that has not completed.
        // How the world load or a file version failed the modification id of a scene patch file in this load, or null.
        internal Outcome Failure(string scenePatchFileKey, string id) =>
            failed.TryGetValue((scenePatchFileKey ?? "").Length + ":" + (scenePatchFileKey ?? "") + id, out var outcome) ? outcome : null;
        internal bool IsUnsafe(object target) {
            if (unsafeTargets.Contains(target)) return true;
            if (!awaitingOutput.Contains(target)) return false;
            var output = Output(target);
            if (output.Error != null) { awaitingOutput.Remove(target); unsafeTargets.Add(target); return true; }
            if (!output.Settled) return true;
            awaitingOutput.Remove(target);
            return false;
        }
        internal void SetUnsafe(object target, bool value, IReadOnlyList<string> remaining = null) {
            if (value) { unsafeTargets.Add(target); remainingState[target] = remaining ?? Array.Empty<string>(); }
            else { unsafeTargets.Remove(target); awaitingOutput.Remove(target); remainingState.Remove(target); }
        }
        // The remaining state of every target whose cleanup is unverified, by the identity it was resolved under, without
        // searching the scene. Restored targets that only wait for their dependent output are not listed.
        internal IReadOnlyList<string> UnverifiedCleanup(TargetIdentity identity) {
            if (identity == null) return null;
            foreach (var target in unsafeTargets) {
                if (awaitingOutput.Contains(target) || !ResolvedUnder(target, identity)) continue;
                return Remaining(target);
            }
            return null;
        }
        // The refusal of an operation on the object resolved under this identity while it is guarded, or null. Read from
        // the engine's record without searching the scene, so a caller can check the guard before anything that compares
        // the scene: an incomplete cleanup leaves the object changed, and the guard, not that change, is the reason.
        internal Exception GuardFor(TargetIdentity identity, string blocked) {
            if (identity == null) return null;
            foreach (var target in unsafeTargets.Concat(awaitingOutput).ToArray())
                if (ResolvedUnder(target, identity) && IsUnsafe(target)) return Guarded(target, blocked);
            return null;
        }
        // Whether the object was resolved under this identity. An identity without a structure matches any structure.
        bool ResolvedUnder(object target, TargetIdentity identity) =>
            identities.TryGetValue(target, out var known) && TargetIdentity.SameObject(known, identity)
            && (known.Structure == null || identity.Structure == null || known.Structure == identity.Structure);
        // The refusal of an operation on a guarded target: a restored target that waits for its dependent output is
        // not an incomplete cleanup and needs no Recheck; any other guard is.
        internal Exception Guarded(object target, string blocked) => AwaitingOutput(target)
            ? new AttemptFailure(FailureKind.AwaitingOutput, "The game is still regenerating terrain after an earlier edit.", wasGuarded: true)
            : (Exception)new AttemptFailure(FailureKind.Blocked, blocked, Remaining(target));
        // The remaining or uncertain state an unverified cleanup left on the target, or empty.
        internal IReadOnlyList<string> Remaining(object target) => target != null && unsafeTargets.Contains(target) && remainingState.TryGetValue(target, out var lines) ? lines : Array.Empty<string>();
        internal bool AwaitingOutput(object target) { return awaitingOutput.Contains(target); }
        internal DependentOutput Output(object target) { return target != null && scene is IDependentOutputScene dependent ? dependent.Output(target) ?? DependentOutput.None : DependentOutput.None; }
        // After a verified restoration: keeps the target guarded until its dependent output verifies. True when it waits.
        internal bool AwaitOutput(object target) {
            if (Output(target).Settled) return false;
            awaitingOutput.Add(target);
            return true;
        }
        // The addition IDs a modification sets, and those that more than one modification of a scene patch file uses. A blank key is
        // no ID: its modification fails for the empty key, not for sharing it. An addition ID
        // names one addition of its supplying mod, so every modification that shares one fails, wherever it stands.
        // Equipment IDs are a separate namespace from detection additions and appear as "equipment <id>".
        internal static IEnumerable<string> AdditionIds(JObject item) {
            var relationships = item["relationships"] as JObject;
            var detection = ((relationships?["crossingCoverage"] as JObject)?["additions"] as JObject)?.Properties().Select(p => p.Name).Where(name => !String.IsNullOrWhiteSpace(name)) ?? Enumerable.Empty<string>();
            var equipment = (relationships?["crossingEquipment"] as JObject)?.Properties().Select(p => p.Name).Where(name => !String.IsNullOrWhiteSpace(name)).Select(name => "equipment " + name) ?? Enumerable.Empty<string>();
            // A replacement ID names one marker, so it follows the same rule as "marker ID <id>".
            var markerId = item[ReplacementMarkerIds.Member]?.Type == JTokenType.String && !String.IsNullOrWhiteSpace((string)item[ReplacementMarkerIds.Member]) ? new[] { MarkerIdKey + (string)item[ReplacementMarkerIds.Member] } : new string[0];
            // So does each marker ID of a scene marker below a mod object.
            var parts = (item[ModObjects.PartMarkerIdsMember] as JArray)?.Where(x => x.Type == JTokenType.String && !String.IsNullOrWhiteSpace((string)x)).Select(x => MarkerIdKey + (string)x).Distinct(StringComparer.Ordinal) ?? Enumerable.Empty<string>();
            return detection.Concat(equipment).Concat(markerId).Concat(parts);
        }
        internal static HashSet<string> SharedAdditionIds(IEnumerable<JObject> items) {
            var seen = new HashSet<string>(StringComparer.Ordinal); var shared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items) foreach (var id in AdditionIds(item)) if (!seen.Add(id)) shared.Add(id);
            return shared;
        }
        const string MarkerIdKey = "marker ID ";
        internal static string SharedAddition(string id) => id.StartsWith(MarkerIdKey, StringComparison.Ordinal) ? ReplacementMarkerIds.Shared(id.Substring(MarkerIdKey.Length))
            : "Addition ID " + id + " is used by more than one modification; an addition ID names one addition in its scene patch file";
        // The modifications of this scene patch file that take effect on the object, in the order Desired overlays them: the
        // entries that parse, share no addition ID and whose dependencies take effect.
        internal IReadOnlyList<Modification> EffectiveFor(JObject modifications, object target, string owner = "") {
            var entries = Entries(modifications);
            var effective = new Dictionary<string, Modification>(StringComparer.Ordinal);
            var order = new List<Modification>(); var targets = new Dictionary<Modification, object>();
            var shared = SharedAdditionIds(entries.Values);
            foreach (var id in entries.Keys) Effective(id, entries, effective, order, targets, new HashSet<string>(), owner, shared);
            return order.Where(item => ReferenceEquals(targets[item], target)).ToList();
        }
        // Underlying state overlaid with every effective modification of this scene patch file for the object. The owner is the key
        // of the scene patch file, which scopes the identities of its additions.
        internal ObjectState Desired(JObject modifications, object target, string owner = "") {
            var own = EffectiveFor(modifications, target, owner);
            var state = Underlying(target);
            // The equipment without this mod is what stands now, less this mod's additions: other mods' additions stay as they
            // are, and this mod's additions exist only through its scene patch file, even when an earlier application in this world
            // left them, so an addition the scene patch file no longer lists is removed.
            if (state?.Equipment != null) {
                var standing = scene.Read(target)?.Equipment ?? state.Equipment;
                state = new ObjectState(state.Placement, state.Active, state.Marker, state.Coverage, standing.Without(owner ?? ""), state.Starter).WithSegments(state.Segments).WithMarkerId(state.MarkerId);
            }
            foreach (var item in own) state = Overlay(state, item.State);
            return Evaluate(state);
        }
        // One modification's state laid over a complete state: every value the modification sets replaces the one below.
        internal static ObjectState Overlay(ObjectState under, ObjectState mod) =>
            new ObjectState(mod.Placement ?? under.Placement, mod.Active ?? under.Active, mod.Marker ?? under.Marker, CrossingCoverage.Overlay(under.Coverage, mod.Coverage), CrossingEquipment.Overlay(under.Equipment, mod.Equipment), StarterSetting.Overlay(under.Starter, mod.Starter))
                .WithSegments(ConstructionSegments.Overlay(under.Segments, mod.Segments)).WithMarkerId(mod.MarkerId ?? under.MarkerId);
        // The state with a track-following placement evaluated at its anchor's current native pose. Throws before any
        // mutation when the anchor does not resolve.
        internal ObjectState Evaluate(ObjectState state) {
            var following = state?.Placement?.Following;
            if (following == null) return state;
            if (!(scene is ITrackPoseScene tracks)) throw new InvalidOperationException("Track-following placement is unavailable in this scene");
            var evaluated = new ObjectState(following.Evaluate(tracks.TrackPose(following.Anchor)), state.Active, state.Marker, state.Coverage, state.Equipment, state.Starter).WithSegments(state.Segments).WithMarkerId(state.MarkerId);
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
        // Why the modification under the key would not take effect because of its dependencies, or null: a dependency is
        // missing from the scene patch file, fails itself, or the dependencies form a cycle. The same rules decide which
        // modifications Desired overlays.
        internal string IneffectiveReason(JObject modifications, string key, string owner = "") {
            var entries = Entries(modifications);
            if (!entries.ContainsKey(key)) return null;
            return DependencyProblem(key, entries, SharedAdditionIds(entries.Values), owner, new List<string>());
        }
        string DependencyProblem(string id, SortedDictionary<string, JObject> entries, HashSet<string> shared, string owner, List<string> chain) {
            chain.Add(id);
            try {
                string[] dependencies;
                try { dependencies = Dependencies(entries[id]); } catch (InvalidOperationException) { return "the dependsOn of modification " + id + " is not a list of modification IDs"; }
                foreach (var dependency in dependencies) {
                    var loop = chain.IndexOf(dependency);
                    if (loop >= 0) return "its dependencies form the cycle " + String.Join(" -> ", chain.Skip(loop).Concat(new[] { dependency }));
                    if (!entries.ContainsKey(dependency)) return "dependency " + dependency + " is missing from the scene patch file";
                    var failure = DependencyFailure(dependency, entries, shared, owner);
                    if (failure != null) return "dependency " + dependency + " does not take effect (" + ScenePatchFile.Sentence(failure) + ")";
                    var deeper = DependencyProblem(dependency, entries, shared, owner, chain);
                    if (deeper != null) return deeper;
                }
                return null;
            } finally { chain.RemoveAt(chain.Count - 1); }
        }
        // Why the dependency itself does not take effect, apart from its own dependencies, or null.
        string DependencyFailure(string id, SortedDictionary<string, JObject> entries, HashSet<string> shared, string owner) {
            try {
                var parsed = Parse(entries[id], owner, id);
                var addition = AdditionIds(entries[id]).FirstOrDefault(shared.Contains);
                if (addition != null) return SharedAddition(addition);
                ResolveUnique(parsed.Identity);
                return null;
            } catch (InvalidOperationException e) { return PatcherLog.Reason(e); }
        }
        internal static TargetIdentity ParseTarget(JObject target) {
            if (target == null || !Only(target, "scene", "path", "component", "structure", TargetIdentity.PadMarkerMember, TargetIdentity.MarkerIdMember, TargetIdentity.MapPositionMember) || !RequiredString(target,"scene") || !RequiredString(target,"path") || !RequiredString(target,"component") || (target["structure"] != null && target["structure"].Type != JTokenType.String)
                || (target[TargetIdentity.PadMarkerMember] != null && !RequiredString(target, TargetIdentity.PadMarkerMember))
                || (target[TargetIdentity.MarkerIdMember] != null && !RequiredString(target, TargetIdentity.MarkerIdMember))) throw new InvalidOperationException("Invalid or unsupported target");
            double[] position = null;
            if (target[TargetIdentity.MapPositionMember] != null) {
                var values = target[TargetIdentity.MapPositionMember] as JArray;
                if (values == null || values.Count != 2 || values.Any(v => v.Type != JTokenType.Integer && v.Type != JTokenType.Float)) throw new InvalidOperationException("Invalid or unsupported target");
                position = values.Select(v => (double)v).ToArray();
                if (position.Any(v => Double.IsNaN(v) || Double.IsInfinity(v))) throw new InvalidOperationException("Invalid or unsupported target");
            }
            // One key at most: a marker ID or a map position, and neither beside a crossbuck's pad marker ID.
            var keys = (target[TargetIdentity.PadMarkerMember] != null ? 1 : 0) + (target[TargetIdentity.MarkerIdMember] != null ? 1 : 0) + (position != null ? 1 : 0);
            if (keys > 1) throw new InvalidOperationException("Invalid or unsupported target");
            return new TargetIdentity { Scene=(string)target["scene"], Path=(string)target["path"], Component=(string)target["component"], Structure=(string)target["structure"], PadMarkerId=(string)target[TargetIdentity.PadMarkerMember],
                MarkerId=(string)target[TargetIdentity.MarkerIdMember], MapPosition=position };
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
        internal static readonly string[] Known = { "target", ModObjects.SourceMember, ModObjects.TeleportNameMember, ReplacementMarkerIds.Member, ModObjects.PartMarkerIdsMember, "placement", "active", "relationships", "starterPlacement", "dependsOn" };
        // The modifications of a scene patch file by key, in ordinal key order.
        internal static SortedDictionary<string, JObject> Entries(JObject modifications) {
            var entries = new SortedDictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var property in modifications.Properties()) if (property.Value is JObject item) entries[property.Name] = item;
            return entries;
        }
        // Why the modification holds content this patcher version does not execute, or null. A modification is named by
        // its key, so an id member inside it is content of an older shape.
        static string UnsupportedContent(string id, JObject item) {
            if (item["id"] != null) return "Unsupported modification content: modification " + id + " carries an id member. " + WindowGuidance.RemoveIdMember;
            return Only(item, Known) ? null : "Unsupported modification content";
        }
        // The owner is the key of the scene patch file the modification belongs to; it scopes the identities of its additions.
        // A mod object's mark must hold the owner and the ID, so its identity is scoped to the mod that supplies the file:
        // whatever stands in the scene, no modification resolves to the mod object of another mod or of another key. Null
        // stands for a file whose owner is not known yet, such as one read for its diagnostics: the scope is then empty
        // and the mark is not checked.
        // The ID is the key of the modification; a modification that stands in a scene patch file has it without being told.
        internal static Modification Parse(JObject item, string owner = null, string id = null) {
            id = id ?? ScenePatchFile.Key(item);
            var unsupported = UnsupportedContent(id, item);
            if (unsupported != null) throw new InvalidOperationException(unsupported);
            var dependencies = Dependencies(item);
            var identity = ParseTarget(item["target"] as JObject);
            // A mod object: the target names the object the mod makes, the source the scene object it is made from.
            TargetIdentity source = null; string build = null;
            if (item[ModObjects.SourceMember] != null) {
                var written = item[ModObjects.SourceMember] as JObject;
                if (written?[ModObjects.BuildMember] != null) {
                    if (!RequiredString(written, ModObjects.BuildMember)) throw new InvalidPatchDefinitionException(ModObjects.InvalidBuild);
                    build = (string)written[ModObjects.BuildMember];
                    written = (JObject)written.DeepClone(); written.Remove(ModObjects.BuildMember);
                }
                source = ParseTarget(written);
                if (identity.Keyed || identity.PadMarkerId != null || identity.Scene != source.Scene || identity.Component != source.Component || identity.Structure != ModObjects.StructureOf(source.Structure) || !ModObjects.IsPath(identity.Path))
                    throw new InvalidPatchDefinitionException(ModObjects.TargetShape);
                if (owner != null && ModObjects.MarkOf(identity.Path) != ModObjects.Mark(owner, id)) throw new InvalidPatchDefinitionException(ModObjects.TargetShape);
                if (item["placement"] == null) throw new InvalidPatchDefinitionException(ModObjects.NeedsPlacement);
            }
            string teleportName = null;
            if (item[ModObjects.TeleportNameMember] != null) {
                if (source == null || !ModObjects.TakesTeleportName(identity.Structure)) throw new InvalidPatchDefinitionException(ModObjects.TeleportNameOnlySpawnPoints);
                if (!RequiredString(item, ModObjects.TeleportNameMember)) throw new InvalidPatchDefinitionException(ModObjects.TeleportNameNotAString);
                teleportName = (string)item[ModObjects.TeleportNameMember];
                var problem = ModObjects.TeleportNameProblem(teleportName);
                if (problem != null) throw new InvalidPatchDefinitionException(problem);
            }
            Placement checkedPlacement = null;
            if (item["placement"] != null) {
                var placement = item["placement"] as JObject;
                var mode = placement?["mode"]?.Type == JTokenType.String ? (string)placement["mode"] : null;
                if (mode == "fixed" && Only(placement, "mode", "position", "rotation")) checkedPlacement = CheckPlacement(Numbers(placement["position"], 3), Numbers(placement["rotation"], 4));
                else if (mode == TrackFollowingMode && Only(placement, "mode", "track", "offset", "rotation")) checkedPlacement = new Placement { Following = ParseFollowing(placement) };
                else throw new InvalidOperationException("Unsupported placement");
            }
            if (item["active"] != null && item["active"].Type != JTokenType.Boolean) throw new InvalidOperationException("Invalid activity");
            MarkerBinding marker = null; CrossingCoverage coverage = null; CrossingEquipment equipment = null; ConstructionSegments segments = null;
            if (item["relationships"] != null) {
                var relationships = item["relationships"] as JObject;
                if (relationships == null || !Only(relationships, "trackMarker", "crossingCoverage", "crossingEquipment", "constructionSegments") || !relationships.HasValues) throw new InvalidOperationException("Unsupported relationships");
                if (relationships["trackMarker"] != null) marker = ParseTrackMarker(relationships["trackMarker"]);
                if (relationships["crossingCoverage"] != null) coverage = ParseCoverage(relationships["crossingCoverage"]).OwnedBy(owner ?? "");
                if (relationships["crossingEquipment"] != null) equipment = ParseEquipment(relationships["crossingEquipment"]).In(owner ?? "");
                if (relationships["constructionSegments"] != null) segments = ParseSegments(relationships["constructionSegments"]);
            }
            var starter = item["starterPlacement"] == null ? null : ParseStarter(item["starterPlacement"]);
            var markerId = item[ReplacementMarkerIds.Member] == null ? null : ParseMarkerId(item[ReplacementMarkerIds.Member]);
            var partMarkerIds = new string[0];
            if (item[ModObjects.PartMarkerIdsMember] != null) {
                if (source == null) throw new InvalidPatchDefinitionException(ModObjects.PartMarkerIdsOnlyModObject);
                var list = item[ModObjects.PartMarkerIdsMember] as JArray;
                if (list == null || list.Any(x => x.Type != JTokenType.String)) throw new InvalidPatchDefinitionException(ModObjects.PartMarkerIdsShape);
                partMarkerIds = list.Select(ParseMarkerId).ToArray();
                if (partMarkerIds.Concat(new[] { markerId }).Where(x => x != null).Distinct(StringComparer.Ordinal).Count() != partMarkerIds.Length + (markerId == null ? 0 : 1)) throw new InvalidPatchDefinitionException(ModObjects.PartMarkerIdsShape);
            }
            if (checkedPlacement == null && item["active"] == null && marker == null && coverage == null && equipment == null && starter == null && segments == null && markerId == null) throw new InvalidOperationException("Unsupported placement");
            return new Modification { Id = id, Identity = identity, State = new ObjectState(checkedPlacement, (bool?)item["active"], marker, coverage, equipment, starter).WithSegments(segments).WithMarkerId(markerId), Dependencies = dependencies, Source = source, SourceBuild = build, TeleportName = teleportName, PartMarkerIds = partMarkerIds };
        }
        // A replacement ID: a non-empty string that does not begin with an id prefix the game reserves. Its
        // uniqueness depends on the world and is checked when the modification applies.
        static string ParseMarkerId(JToken token) {
            if (token.Type != JTokenType.String) throw new InvalidPatchDefinitionException(ReplacementMarkerIds.NotAString);
            var id = (string)token;
            var problem = ReplacementMarkerIds.Problem(id);
            if (problem != null) throw new InvalidPatchDefinitionException(problem);
            return id;
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
        // The construction segments of a rail bridge construction: segments lists the ids of the track segments the
        // construction controller reads, each once, in any order. A detach object is read so the capability can refuse it
        // with its reason: a construction without segments has no meaning.
        static ConstructionSegments ParseSegments(JToken token) {
            var value = token as JObject;
            if (value == null) throw new InvalidOperationException("Invalid relationship: constructionSegments must be an object whose segments member lists the IDs of the bridge's track segments.");
            if (value["detach"] != null) {
                if (!Only(value, "detach") || value["detach"].Type != JTokenType.Boolean || !(bool)value["detach"]) throw new InvalidOperationException("Invalid relationship: detach must be true and stand alone");
                return ConstructionSegments.Detached;
            }
            if (!Only(value, "segments")) throw new InvalidOperationException("Unsupported relationship content");
            var ids = value["segments"] as JArray;
            if (ids == null || ids.Count == 0) throw new InvalidOperationException("Invalid relationship: segments must list at least one track segment ID.");
            if (ids.Any(id => id.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)id))) throw new InvalidOperationException("Invalid relationship: each construction segment must be a track segment ID.");
            var duplicate = ids.Select(id => (string)id).GroupBy(id => id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) throw new InvalidOperationException("Invalid relationship: construction segment " + duplicate.Key + " is listed more than once.");
            return ConstructionSegments.Of(ids.Select(id => (string)id));
        }
        // The starter placement of a starter placement marker: setup names the setup whose new-game car placement reads the
        // marker, and track sets the marker's track location. A detach object is read so the capability can refuse it with
        // its reason: the game reads a used marker's track location whatever else holds.
        // wreck sets whether the car placement places its cut derailed; an omitted wreck keeps the native setting.
        static StarterSetting ParseStarter(JToken token) {
            var starter = token as JObject;
            if (starter == null) throw new InvalidOperationException("Invalid starter placement: starterPlacement must be an object with setup, and track, wreck or both");
            if (!Only(starter, "setup", "track", "wreck")) throw new InvalidOperationException("Unsupported starter placement content");
            if (!RequiredString(starter, "setup")) throw new InvalidOperationException("Invalid starter placement: setup must name the setup whose car placement reads the marker");
            bool? wreck = null;
            if (starter.Property("wreck") != null) {
                if (starter["wreck"].Type != JTokenType.Boolean) throw new InvalidOperationException("Invalid starter placement: wreck must be true or false");
                wreck = (bool)starter["wreck"];
            }
            if (starter.Property("track") == null) {
                if (wreck == null) throw new InvalidOperationException(StarterPlacements.NothingSet);
                return new StarterSetting((string)starter["setup"], null, wreck);
            }
            var track = starter["track"] as JObject;
            if (track == null) throw new InvalidOperationException("Invalid starter placement: track must be a track location with segment, end, and distance");
            if (track["detach"] != null) {
                if (!Only(track, "detach") || track["detach"].Type != JTokenType.Boolean || !(bool)track["detach"]) throw new InvalidOperationException("Invalid starter placement: detach must be true and stand alone");
                return new StarterSetting((string)starter["setup"], MarkerBinding.Detached, wreck);
            }
            return new StarterSetting((string)starter["setup"], MarkerBinding.At(ParseLocation(track, "starter placement")), wreck);
        }
        // Explicit crossing coverage: circuits holds each circuit this mod sets, keyed by the ID of the map's own island span, with selected
        // tracks or an explicit detach. An omitted circuit keeps its coverage; an omitted crossingCoverage leaves every
        // circuit untouched. additions holds the detection additions keyed by their IDs: circuits the patcher adds for their
        // selected tracks, which exist only while the scene patch file lists them. Neither the order of the two objects nor the
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
                        if (addition) throw new InvalidOperationException("Invalid relationship: an addition cannot be detached. " + WindowGuidance.OmitDetachedAddition);
                        if (!Only(circuit, "detach") || circuit["detach"].Type != JTokenType.Boolean || !(bool)circuit["detach"]) throw new InvalidOperationException("Invalid relationship: detach must be true and stand alone");
                        parsed.Add(new KeyValuePair<string, CircuitCoverage>(key, CircuitCoverage.Detached));
                        continue;
                    }
                    if (!Only(circuit, "approachA", "island", "approachB")) throw new InvalidOperationException("Unsupported relationship content");
                    var ranges = new SpanRange[3];
                    for (var i = 0; i < 3; i++) {
                        var role = CircuitCoverage.Roles[i];
                        var range = circuit[role] as JObject;
                        if (range == null) throw new InvalidOperationException("Invalid relationship: " + label + " needs approachA, island, and approachB" + (addition ? "" : ", or an explicit detach"));
                        if (!Only(range, "from", "to")) throw new InvalidOperationException("Unsupported relationship content");
                        ranges[i] = new SpanRange(RangeEnd(range["from"]), RangeEnd(range["to"]));
                    }
                    parsed.Add(new KeyValuePair<string, CircuitCoverage>(key, addition ? CircuitCoverage.Added(ranges[0], ranges[1], ranges[2]) : CircuitCoverage.Tracks(ranges[0], ranges[1], ranges[2])));
                }
            }
            return new CrossingCoverage(parsed);
        }
        // Explicit equipment additions to a crossing controller, keyed by a persistent ID unique in this scene patch file: each holds the
        // required pattern, a source and a fixed placement. An addition this scene patch file omits stays untouched.
        static CrossingEquipment ParseEquipment(JToken token) {
            var entries = token as JObject;
            if (entries == null || !entries.HasValues) throw new InvalidOperationException("Invalid relationship: crossingEquipment must be an object with at least one entry, keyed by equipment ID");
            // Content this version does not execute anywhere in the relationship makes it unsupported, whatever else is invalid.
            foreach (var property in entries.Properties()) {
                if (!(property.Value is JObject known)) continue;
                if (!Only(known, "pattern", "source", "placement")) throw new InvalidOperationException("Unsupported relationship content in equipment " + property.Name + (known["id"] != null ? ": an entry carries no id member; its ID is its key, " + property.Name : ""));
                if (known["source"] is JObject source && !Only(source, "scene", "path", "definition")) throw new InvalidOperationException("Unsupported relationship content in the source of equipment " + property.Name);
                if (known["placement"] is JObject placement && !Only(placement, "mode", "position", "track", "offset", "rotation")) throw new InvalidOperationException("Unsupported relationship content in the placement of equipment " + property.Name);
            }
            var parsed = new List<EquipmentAddition>();
            foreach (var property in entries.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)) {
                var id = property.Name;
                if (String.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Invalid relationship: a key of crossingEquipment is empty; equipment is keyed by its ID");
                var problem = EquipmentAddition.IdProblem(id);
                if (problem != null) throw new InvalidOperationException("Invalid relationship: " + problem);
                var addition = property.Value as JObject;
                if (addition == null) throw new InvalidOperationException("Invalid relationship: equipment " + id + " must be an object");
                if (addition["id"] != null) throw new InvalidOperationException("Unsupported relationship content in equipment " + id + ": an entry carries no id member; its ID is its key, " + id);
                if (!Only(addition, "pattern", "source", "placement")) throw new InvalidOperationException("Unsupported relationship content in equipment " + id);
                if (!RequiredString(addition, "pattern")) throw new InvalidOperationException("Invalid relationship: equipment " + id + " needs its required pattern");
                var pattern = (string)addition["pattern"];
                if (!EquipmentAddition.Patterns.Contains(pattern, StringComparer.Ordinal)) throw new InvalidOperationException("Unsupported crossing equipment pattern " + pattern + " in equipment " + id);
                var source = addition["source"] as JObject;
                EquipmentSource from;
                // A bell loads the audio of a bell definition; a light or gate copies a loaded device.
                if (pattern == EquipmentAddition.BellPattern) {
                    if (source == null || !Only(source, "definition") || !RequiredString(source, "definition")) throw new InvalidOperationException("Invalid relationship: equipment " + id + " needs a bell definition as its source in Data/scene-patches.json; the source must contain only definition with the bell definition's identifier");
                    from = EquipmentSource.ForDefinition((string)source["definition"]);
                } else {
                    if (source == null || !Only(source, "scene", "path") || !RequiredString(source, "scene") || !RequiredString(source, "path")) throw new InvalidOperationException("Invalid relationship: equipment " + id + " needs a light or gate in the map as its source in Data/scene-patches.json; the source must contain only the scene and path of the light or gate");
                    from = new EquipmentSource((string)source["scene"], (string)source["path"]);
                }
                var placement = addition["placement"] as JObject;
                if (placement == null || placement["mode"]?.Type != JTokenType.String || (string)placement["mode"] != "fixed" || !Only(placement, "mode", "position", "rotation")) throw new InvalidOperationException("Invalid relationship: equipment " + id + " needs a fixed placement");
                parsed.Add(new EquipmentAddition(id, pattern, from, CheckPlacement(Numbers(placement["position"], 3), Numbers(placement["rotation"], 4))));
            }
            return new CrossingEquipment(parsed, false);
        }
        static TrackLocation RangeEnd(JToken token) {
            if (!(token is JObject location)) throw new InvalidOperationException("Invalid relationship: a track location needs segment, end, and distance");
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
