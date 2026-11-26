// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // Reads serialized bindings and direct references, never binding/cache getters or TryApply.
    internal sealed class NativeInventory : ISteppedInspectionScene, IRelatedInspectionScene, IOwnedObjectScene, ICheckedObjectsScene {
        // The scene object rule's checks, for the window's reason on a map where it lists nothing. A failed read reports
        // no checks, so the window keeps its list.
        public IEnumerable<string> CheckedObjects() {
            try { return GroupWorld.Checks(); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { PatcherLog.Debug("The patcher couldn't read the scene object checks for the object list: " + e.GetBaseException().Message); return Array.Empty<string>(); }
        }
        // Observes one component with what the last discovery learned about the rest of the scene: who references what,
        // the graph's segments and the setup holders. A discovery sets it when that knowledge is complete.
        static Func<Component, string, SceneObservation> observer;
        // The marker reference scan of the last discovery whose walk over the fields completed, or null.
        internal static ReferenceScan LastReferrers { get; private set; }
        // The object itself is read now: its components, placement, activity, bindings and problems. Without a discovery so
        // far, or when the object or its component is gone, there is no observation.
        public SceneObservation Observe(object handle, TargetIdentity identity) {
            var target = handle as GameObject;
            if (target == null || observer == null || identity == null) return null;
            var component = target.GetComponents<Component>().FirstOrDefault(c => c != null && TypeName(c) == identity.Component);
            var family = component == null ? null : Family(component);
            return family == null ? null : observer(component, family);
        }
        // Added crossing equipment is the patcher's own runtime state, not inventory.
        // The wrapper of an addition is its added object and stays inventory; the copy's parts below it do not.
        public bool IsOwned(object handle) => handle is GameObject go && go != null && go.GetComponent<OwnedEquipment>() == null && OwnedEquipment.Of(go) != null;
        static bool CopyPart(Component c) => !(c is OwnedEquipment) && OwnedEquipment.Of(c.gameObject) != null;
        public bool Present(object handle) => handle is GameObject target && target != null && target.scene.IsValid() && target.scene.isLoaded
            && target.GetComponent<OwnedEquipment>()?.Retired != true;
        // The observed components of the object's hierarchy, which holds the lights, gates and bells of a crossing, and
        // for a crossing the spans this patcher owns in its scene: a coverage edit creates and removes those.
        public IEnumerable<SceneObservation> ObserveRelated(object handle, TargetIdentity identity) {
            var target = handle as GameObject;
            if (target == null || observer == null) return null;
            // An edit of a crossing can have added or removed detection spans in this frame.
            CrossingDeviceWorld.InvalidateHolders();
            var roots = new List<GameObject> { target };
            var found = new List<SceneObservation>();
            // A top scene object or a part is observed through its transform; the families below it through their own components.
            var group = GroupWorld.FamilyOf(target);
            if (group != null) {
                found.Add(observer(target.transform, group));
                // An edit of a top scene object or a part moves or switches every part below it with it: those parts are
                // entries of their own, and they take their new observations too, so the edit's own consequence never
                // reads as a change by another hand.
                foreach (var below in target.GetComponentsInChildren<Transform>(true))
                    if (below != target.transform && GroupWorld.IsPart(below.gameObject)) found.Add(observer(below, SceneObjectRules.ChildFamily));
            }
            if (Get(target, Crossing) != null)
                roots.AddRange(target.scene.GetRootGameObjects().Where(root => root != null && root.name == CrossingCoverageWorld.RootName));
            foreach (var root in roots)
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) {
                    if (!Loaded(component) || CopyPart(component)) continue;
                    var family = Family(component);
                    if (family != null) found.Add(observer(component, family));
                }
            return found;
        }
        // Whether the game shifts the object with its moving origin: it shifts the transforms registered as origin
        // targets, with everything below them. A holder of data outside them, such as an equipment setup with its
        // markers, stays where it is in the scene. When no target is registered at all the registration cannot be read,
        // and every object counts as shifted, as the game's own conversion assumes.
        internal static bool MovesWithOrigin(Transform transform) {
            var targets = Helpers.WorldTransformerTargetList.Targets;
            if (targets == null || targets.Count == 0) return true;
            for (var t = transform; t != null; t = t.parent) if (targets.Contains(t)) return true;
            return false;
        }
        // The object's map position (MapOrigin.Position): the inventory shows it and Focus moves the camera to it.
        internal static Vector3 MapPosition(Transform transform) => MapPosition(transform.position, MovesWithOrigin(transform));
        // The MapOrigin rule between scene and map positions, for an object that does or does not move with the origin.
        // Focus, the inventory, the marker capability and the gizmo use it.
        internal static Vector3 MapPosition(Vector3 scene, bool movesWithOrigin) =>
            Vector(MapOrigin.Position(Doubles(scene), movesWithOrigin ? OriginOffset() : null, movesWithOrigin));
        internal static Vector3 ScenePosition(Vector3 map, bool movesWithOrigin) =>
            Vector(MapOrigin.ScenePosition(Doubles(map), movesWithOrigin ? OriginOffset() : null, movesWithOrigin));
        internal static Vector3 MarkPosition(Vector3 map) => Vector(MapOrigin.MarkPosition(Doubles(map), OriginOffset()));
        static double[] OriginOffset() => Doubles((Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "GameToWorld", Vector3.zero));
        static double[] Doubles(Vector3 v) => new double[] { v.x, v.y, v.z };
        static Vector3 Vector(double[] v) => new Vector3((float)v[0], (float)v[1], (float)v[2]);
        const string GameObjectCuller = CullerTypes.GameObjectCuller;
        // The equipment of a crossing whose activity the game switches by camera distance through a culler.
        static readonly HashSet<string> CulledFamilies = new HashSet<string>(StringComparer.Ordinal) { MastFamily, BellFamily };
        const string MastFamily = ExistingEquipmentScene.MastFamily, BellFamily = ExistingEquipmentScene.BellFamily;
        const string Marker = "Track.TrackMarker", Span = "Track.TrackSpan", Relative = "Track.TrackRelativePosition";
        const string Crossing = "Track.Crossings.GradeCrossingController", Milepost = "Helpers.MilePost";
        const string ConstructionScenery = "Game.Mow.TrackConstructionScenery", Pile = "Scenery.Pile", LogPond = SceneObjectRules.LogPond, LakeSurface = SceneObjectRules.LakeSurface;
        public IEnumerable<string> MapIds => ModScenePatchFiles.MapIds();
        // Why a board candidate cannot be edited, from the board capability; null when that capability is unavailable.
        readonly Func<GameObject, string> boardRefusal;
        internal NativeInventory(Func<GameObject, string> boardRefusal = null) { this.boardRefusal = boardRefusal; }
        static bool Loaded(Component c) => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded;
        static string TypeName(Component c) => c == null ? "missing component" : c.GetType().FullName;
        static Component Get(GameObject go, string type) => TrackMarkerWorld.Get(go, type);
        static Component Parent(Component c, string type) {
            for (var t = c.transform; t != null; t = t.parent) { var found = Get(t.gameObject, type); if (found != null) return found; }
            return null;
        }
        static string Label(Component c) => c == null ? "missing" : c.gameObject.scene.name + "/" + BoardScene.PathOf(c.transform) + " [" + TypeName(c) + "]";
        static IEnumerable<FieldInfo> Fields(Type type) {
            for (; type != null && type != typeof(MonoBehaviour) && type != typeof(Behaviour) && type != typeof(Component); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) yield return field;
        }
        static object Field(object owner, string name) {
            if (owner == null) throw new InvalidOperationException("Missing owner for " + name);
            var field = Fields(owner.GetType()).FirstOrDefault(f => f.Name == name);
            if (field == null) throw new MissingFieldException(owner.GetType().FullName, name);
            return field.GetValue(owner);
        }
        static string Text(object value) => Convert.ToString(value, CultureInfo.InvariantCulture);
        // The scene objects whose visibility a construction controller sets: the targets of its entries.
        internal static GameObject[] ConstructionTargets(GameObject controller) {
            var scenery = controller == null ? null : Get(controller, ConstructionScenery);
            if (scenery == null) return new GameObject[0];
            var found = new List<GameObject>();
            if (Field(scenery, "entries") is IEnumerable entries)
                foreach (var entry in entries) if (entry != null && Field(entry, "targets") is GameObject[] targets) found.AddRange(targets.Where(target => target != null));
            return found.Distinct().ToArray();
        }
        // For each storage pile or log pond that an industry's storage fills, the scene object of that storage: the
        // synchronizer that names it in its bulk scenery.
        const string StorageSynchronizer = "Scenery.BulkIndustryStorageSynchronizer";
        internal static Dictionary<GameObject, GameObject> StorageDisplays() {
            var found = new Dictionary<GameObject, GameObject>();
            var type = Native.Type(StorageSynchronizer);
            if (type == null) return found;
            foreach (var storage in Resources.FindObjectsOfTypeAll(type).OfType<Component>().Where(c => c != null && Loaded(c)).OrderBy(c => c.GetInstanceID()))
                if (Field(storage, "bulkScenery") is IEnumerable filled)
                    foreach (var item in filled) if (item is Component scenery && scenery != null && !found.ContainsKey(scenery.gameObject)) found[scenery.gameObject] = storage.gameObject;
            return found;
        }
        // The game logic a top scene object or part of a building with pickable toggles owns, as bindings in the order the
        // scene lists it: its own object and the objects below it down to the next part. A toggle in a part below is
        // only counted, and so is a key-value object there. A group without a toggle adds nothing.
        static void InteractiveBindings(GameObject go, GameObject root, List<string> bindings, List<string> dependencies) {
            if (!GroupWorld.HoldsToggles(root)) return;
            var owned = new List<GameObject>();
            void Own(Transform t) {
                if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) return;
                owned.Add(t.gameObject);
                foreach (Transform child in t) if (!GroupWorld.IsPart(child.gameObject)) Own(child);
            }
            Own(go.transform);
            int toggles = 0, holders = 0;
            foreach (var at in owned) {
                var components = at.GetComponents<Component>().Where(c => c != null).ToArray();
                var types = components.Select(TypeName).ToArray();
                foreach (var toggle in components.Where(c => TypeName(c) == CommandBand.PickableToggle)) {
                    bindings.Add(CommandBand.ToggleBinding(CommandBand.ToggleKind(types), CommandBand.ToggleKeyOf(toggle)));
                    toggles++;
                }
                if (types.Contains(CommandBand.KeyValueObject)) {
                    var global = components.FirstOrDefault(c => TypeName(c) == CommandBand.GlobalKeyValueObject);
                    bindings.Add(CommandBand.KeyObjectBinding(global == null ? null : CommandBand.GlobalIdOf(global)));
                    holders++;
                }
                foreach (var text in components.Where(c => TypeName(c) == CommandBand.NameTextType)) {
                    var value = text.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.GetValue(text) as string;
                    if (!String.IsNullOrWhiteSpace(value)) bindings.Add(CommandBand.NameTextBinding(String.Join(" ", value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim()));
                }
            }
            if (toggles != 0) dependencies.Add("Pickable toggle: clicks reach it through its collider at its current position. The game reads its state by key from the key-value object above it.");
            // What the parts below hold: disabling this object disables a key-value object there too.
            var saved = go.GetComponentsInChildren<Component>(true).Where(c => c != null && (c.gameObject.hideFlags & HideFlags.DontSave) == 0).Select(TypeName).ToArray();
            var holdersBelow = saved.Count(type => type == CommandBand.KeyValueObject) - holders;
            if (holdersBelow > 0) bindings.Add(CommandBand.KeyObjectsBelowBinding(holdersBelow));
            var below = saved.Count(type => type == CommandBand.PickableToggle) - toggles;
            if (below > 0) bindings.Add(CommandBand.TogglesBelowBinding(below));
        }
        // The storage a pile shows: the binding of the ids the synchronizer naming the pile reads it by, and its name for
        // the Object tab, by the names the game shows or by those ids while the industry or load is not known. Null for
        // a pile that no loaded synchronizer names. A log pond's logs show their storage the same way.
        internal static Tuple<string, string> PileStorage(GameObject pile) {
            var scenery = pile == null ? null : Get(pile, Pile) ?? Get(pile, LogPond);
            var type = Native.Type(StorageSynchronizer);
            if (scenery == null || type == null) return null;
            foreach (var storage in Resources.FindObjectsOfTypeAll(type).OfType<Component>().Where(c => c != null && Loaded(c)).OrderBy(c => c.GetInstanceID())) {
                if (!(Field(storage, "bulkScenery") is IEnumerable filled) || !filled.Cast<object>().Any(item => ReferenceEquals(item, scenery))) continue;
                var componentId = Text(Field(storage, "industryComponentId")); var loadId = Text(Field(storage, "loadId"));
                string industry = null, load = null;
                try {
                    var ops = Native.Read(Native.Type("Model.Ops.OpsController"), "Shared"); var dot = componentId?.IndexOf('.') ?? -1;
                    var found = ops == null || dot <= 0 ? null : Native.Call(ops, "IndustryForId", componentId.Substring(0, dot));
                    industry = found == null ? null : Text(Native.Read(found, "name"));
                } catch { }
                try { var known = Native.Call(Native.Type("Model.Ops.Definition.CarLoadLibrary"), "GetLoad", loadId); load = known == null ? null : Text(Native.Read(known, "description")); } catch { }
                return Tuple.Create(CommandBand.StorageBinding(componentId, loadId), CommandBand.StorageName(industry, load, componentId, loadId));
            }
            return null;
        }
        static IEnumerable<UnityEngine.Object> References(object value) {
            if (value is UnityEngine.Object obj) { if (obj != null) yield return obj; }
            else if (value is IList list) foreach (var item in list) if (item is UnityEngine.Object element && element != null) yield return element;
        }
        // A map object the game spawns carries this component; its track anchor is edited in the native map editor.
        const string MapObject = "Game.MapObjectActivation", PhotoController = "RollingStock.BuilderPhotoController";
        static string MarkerKind(Component marker) { try { return Text(Field(marker, "type")); } catch { return null; } }
        // The markers a builder photo rig holds, read once per discovery.
        static HashSet<object> photoMarkers;
        static HashSet<object> PhotoMarkers(bool again = false) {
            if (photoMarkers != null && !again) return photoMarkers;
            var found = new HashSet<object>();
            try {
                var type = Native.Type(PhotoController);
                if (type != null) foreach (var rig in Resources.FindObjectsOfTypeAll(type)) {
                    var held = rig == null ? null : Field(rig, "marker") as Component;
                    if (held != null) found.Add(held);
                }
            } catch { }
            return photoMarkers = found;
        }
        // The family of a component that is not a transform, for the scene object rule's check of existing families.
        internal static string FamilyOf(Component c) => c is Transform ? null : Family(c);
        // The car placements of the loaded setups as the last discovery read them. A single object observed later is
        // classified by them, as it takes the reference scan of that discovery. Setups that could not be read classify
        // no marker as a starter placement marker; every marker then carries a note that says so.
        static StarterSetups starters = new StarterSetups(null);
        // Why the last discovery could not read the setups, or null: the Refresh reports it.
        internal static string StartersError => starters.Error;
        static void ReadStarters() {
            try { starters = TrackMarkerWorld.StarterSetups(); }
            catch (Exception e) { starters = StarterSetups.Unreadable(e.GetBaseException().Message); }
        }
        internal const string AddedEquipmentFamily = CommandBand.AddedEquipmentFamily;
        static string Family(Component c) {
            // Each light or gate the patcher added is one added object: its wrapper, not the parts of its copy.
            if (c is OwnedEquipment owned) {
                if (owned.Retired) return null;
                return owned.IsBell ? CommandBand.AddedBellFamily : AddedEquipmentFamily;
            }
            var type = TypeName(c); var go = c.gameObject;
            // Every object below a scene root that holds signals, CTC and interlocking, game logic, native operations data or
            // editor helpers stays out; a starter placement marker below Setups keeps its family.
            if (!SceneObjectRules.ListedBelow(c.transform.root.name, type == Marker)) return null;
            // A top scene object of the scene object rule, and a part of a listed group, is listed through its transform.
            if (c is Transform) return GroupWorld.FamilyOf(go);
            if (type == Milepost) return "Milepost";
            if (SpawnPoints.FamilyOf(type, c.transform.root.name) is string spawnFamily) return spawnFamily;
            if (type == Marker) {
                // Kinds the inventory does not list: signals, map objects whose track anchor the native map editor owns, and
                // the builder photo marker. A milepost's marker is part of the Milepost row.
                if (!BindingRules.Listed(MarkerKind(c), Get(go, MapObject) != null, PhotoMarkers().Contains(c)) || Get(go, Milepost) != null) return null;
                // A marker an active setup's car placement holds is a starter placement marker; one only inactive
                // setups hold is never read and stays out of the inventory. A marker no setup holds keeps its kind: a crossing
                // pad's marker or a Generic track marker; a marker of another type is not listed.
                return starters.Family(c, SceneFamilies.OfMarker(MarkerKind(c), Get(go, "Track.TrackCurveMeshBuilder") != null));
            }
            // A span the game builds from the map's operations data is not serialized and stays with the native map
            // editor; the spans this patcher adds for a crossing are listed.
            if (type == Span) return SceneObjectRules.SpanListed(!Transient(c.transform, null), c.transform.root.name == CrossingCoverageWorld.RootName) ? "Track span" : null;
            if (type == Crossing) return "Grade crossing controller";
            // A crossing mast is one entry: its light object. The gate on a child of that object is a part of the
            // entry and gets no row. A gate that stands on no light object stays visible as a mast that does not
            // conform, with its reason.
            if ((type == ExistingEquipmentScene.MastComponent || type == ExistingEquipmentScene.GateComponent || type == ExistingEquipmentScene.BellComponent) && Parent(c, Crossing) != null) {
                if (type == ExistingEquipmentScene.BellComponent) return BellFamily;
                return type == ExistingEquipmentScene.GateComponent && CrossingDeviceWorld.OnLightObject(c.transform) ? null : MastFamily;
            }
            if (type == BoardScene.CullerType && BridgeWorld.SupportedPaths.Contains(BoardScene.PathOf(c.transform))) return "Bridge";
            if (type == BoardScene.CullerType && Get(go, BoardScene.MaskType) != null && Get(go, Marker) == null) return "Location sign";
            if (type == ConstructionScenery && BoardScene.PathOf(c.transform) == ConstructionSceneryWorld.ElaPath) return CommandBand.ConstructionSceneryFamily;
            if (type == StationMapIconRules.Component && StationMapIconRules.Listed(BoardScene.PathOf(c.transform))) return StationMapIconRules.Family;
            return null;
        }
        // The families other than the scene object with child objects family whose capability moves the scene ground masks at and
        // below its object.
        static bool MovesGroundMasks(string family) => family == SceneFamilies.TrackMarker || family == "Bridge" || family == "Milepost" || family == "Crossing pad marker"
            || family == StationMapIconRules.Family || family == SpawnPoints.Family;
        // The discovery in one piece: the stepped walk without its pauses, so both read the scene in the same order.
        public IEnumerable<SceneObservation> Discover() => Steps(boardRefusal, true).Where(observation => observation != null);
        public IEnumerable<SceneObservation> DiscoverInSteps() => Steps(boardRefusal, false);
        // The order and the pauses are DiscoveryWalk's: after the search for loaded components, after each component's
        // loaded check, after each owner's fields, after each component's classification and after the graph lookup. Every
        // observation is one further unit. A caller that pauses reads the scene at different times; a component destroyed
        // since the search, such as a widget of a rebuilt panel, is skipped instead of failing the walk.
        // whole is a discovery in one piece, which a snapshot is read from: single-object observation always follows it.
        // A stepped pass reads the fields over many frames, during which components come and go; when that leaves its
        // reference scan incomplete, every marker would read as unsupported. Such a pass still marks the snapshot stale
        // by its own observations, and single-object observation keeps the last complete scan, so an object observed
        // alone meanwhile is not classified by the incomplete one.
        static IEnumerable<SceneObservation> Steps(Func<GameObject, string> boardRefusal, bool whole) {
            // A whole discovery reads the top scene objects again; a stepped pass reuses them while the hierarchy above them stays.
            if (whole) GroupWorld.Invalidate();
            // A whole discovery measures the crossing detection routes afresh; a changed track node clears them anyway.
            if (whole) CrossingDetectionTrack.Invalidate();
            CrossingDeviceWorld.InvalidateHolders();
            var incoming = new Dictionary<UnityEngine.Object, List<string>>(); var scanErrors = 0;
            // A reference in a field Unity does not serialize is working state of a running manager, such as a culling or
            // audio list: it appears and disappears when the camera moves or an object is inspected. It is runtime state.
            var working = new Dictionary<UnityEngine.Object, List<string>>();
            // One walk over the fields serves the dependency strings and the marker reference scan. Every field of every
            // owner is read once; a failed read or enumeration counts as one unreadable field for the dependencies, and
            // the marker scan applies its own relevance and fail-closed rules to the same reads.
            ReferenceScan markerReferrers = null;
            string referrerError = null;
            var culled = new Dictionary<GameObject, string>();
            // The listed objects of cullers that hide their objects now, or whose state is not known yet.
            var hidden = new HashSet<GameObject>();
            var walk = TrackMarkerWorld.ReferrerWalk((owner, field, value, error) => {
                if (error != null) { scanErrors++; return; }
                // What this patcher's own components hold, such as the object the panel revealed last, is working state
                // of the patcher and says nothing about the scene.
                if (owner.GetType().Assembly == typeof(NativeInventory).Assembly) return;
                // The objects a culler switches on and off by camera distance, read from the same walk over the fields.
                if (field.Name == "gameObjects" && owner.GetType().FullName == GameObjectCuller && value is IList switched)
                {
                    var shows = false;
                    try { shows = Text(Field(owner, "_cullingState")) == "Visible"; } catch { }
                    foreach (var item in switched) if (item is GameObject listed && listed != null) { culled[listed] = Label((Component)owner); if (!shows) hidden.Add(listed); }
                }
                try {
                    foreach (var reference in References(value)) {
                        // A stand-in of the shared activity holds a switch's entries for an object while an explicit
                        // disable holds it: the switch still names that object, so its dependencies stay as they were.
                        var target = (UnityEngine.Object)GroupWorld.HeldThrough(reference) ?? reference;
                        var held = field.Authored ? incoming : working;
                        if (!held.TryGetValue(target, out var refs)) held.Add(target, refs = new List<string>());
                        refs.Add((field.Authored ? "Referenced by " : "Referenced at runtime by ") + Label((Component)owner) + "." + field.Name);
                    }
                } catch { scanErrors++; }
            });
            HolderIndex setups = null;
            // The loaded marker components by id, read once per discovery at the first marker that needs them. A failed
            // read is not repeated: it is logged once and each marker shows it as an inspection note.
            Func<Dictionary<string, List<Component>>> markerIds = null;
            HashSet<string> segmentIds = null;
            PhotoMarkers(true);
            string graphError = null;
            // The components of the scene, and the transforms of the top scene objects and parts, which may carry no behaviour at all.
            var steps = DiscoveryWalk.Steps<Component>(() => Resources.FindObjectsOfTypeAll<MonoBehaviour>().Cast<Component>().Concat(TopObjects()), Loaded,
                owner => {
                    if (!(owner is MonoBehaviour)) return true;
                    try { walk.Add(owner); return true; }
                    catch (Exception e) {
                        // The walk stopped early: the dependency strings are incomplete as well.
                        referrerError = e.GetBaseException().Message; scanErrors++; walk = null;
                        return false;
                    }
                },
                () => {
                    if (walk != null) markerReferrers = walk.Finish();
                    // An object own culling took out of a culler's list for a moved part is still one that culler
                    // switches: the walk no longer finds it in the list, so the registry adds it.
                    foreach (var item in GroupWorld.HandedOver()) { culled[item.Item1] = Label(item.Item2); if (!item.Item3) hidden.Add(item.Item1); }
                    // The setups first, so the classification below never uses those of an earlier discovery.
                    ReadStarters();
                    LastReferrers = referrerError == null ? markerReferrers : null;
                    setups = TrackMarkerWorld.SetupHolders();
                    // The index is the discovery's until the patcher writes a marker ID; an observation after that reads
                    // it again, so the other holders of an edited marker's old and new ID are named by their IDs now.
                    Dictionary<string, List<Component>> ids = null; string idsError = null; var idsAt = -1;
                    markerIds = () => {
                        // TrackMarkerWorld.IdWrites counts every SetMarkerId, by an edit, Undo, Redo or restoration, so
                        // the cached index lives only until the next marker ID write.
                        if (ids != null && idsAt == TrackMarkerWorld.IdWrites) return ids;
                        if (idsError == null) {
                            try { idsAt = TrackMarkerWorld.IdWrites; return ids = TrackMarkerWorld.MarkerIds(); }
                            catch (Exception e) { idsError = "The scene's marker IDs couldn't be read: " + e.GetBaseException().Message; PatcherLog.Warning(idsError + ". Report this to the patcher maintainer."); }
                        }
                        throw new InvalidOperationException(idsError);
                    };
                },
                Family,
                () => {
                    try {
                        var graph = Native.Read(Native.Type("Track.Graph"), "Shared");
                        var lookup = Field(graph, "_segmentsLookup") as IDictionary;
                        if (lookup == null) throw new InvalidOperationException("Track segment lookup unavailable");
                        segmentIds = new HashSet<string>(lookup.Keys.Cast<object>().Select(Text), StringComparer.Ordinal);
                    } catch (Exception e) { graphError = e.GetBaseException().Message; }
                    if (whole || observer == null || (referrerError == null && scanErrors == 0)) observer = (component, family) => Observe(component, family, incoming, working, scanErrors, segmentIds, graphError, boardRefusal, markerReferrers, referrerError, setups, culled, hidden, markerIds);
                },
                (component, family) => Observe(component, family, incoming, working, scanErrors, segmentIds, graphError, boardRefusal, markerReferrers, referrerError, setups, culled, hidden, markerIds),
                // Equipment the patcher added is edited through its crossing controller; its culling, initialization and history
                // steps are the patcher's own changes and must not make the inventory stale.
                component => CopyPart(component));
            foreach (var step in steps) yield return step;
        }
        // The transforms of the top scene objects the scene object rule lists, in the family or as inspection only, and of the
        // parts of the listed groups.
        static IEnumerable<Component> TopObjects() =>
            GroupWorld.Verdicts().Where(pair => pair.Key != null && GroupWorld.FamilyOf(pair.Key) != null).Select(pair => (Component)pair.Key.transform)
                .Concat(GroupWorld.Parts().Select(part => (Component)part.transform)).ToArray();
        static bool Transient(Transform child, Transform root) {
            for (var t = child; t != null && t != root; t = t.parent) if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) return true;
            return false;
        }
        static SceneObservation Observe(Component c, string family, Dictionary<UnityEngine.Object, List<string>> incoming, Dictionary<UnityEngine.Object, List<string>> working, int scanErrors, HashSet<string> segmentIds, string graphError, Func<GameObject, string> boardRefusal,
            ReferenceScan markerReferrers, string referrerError, HolderIndex setups, Dictionary<GameObject, string> culled, HashSet<GameObject> hidden, Func<Dictionary<string, List<Component>>> markerIds) {
            var go = c.gameObject; var problems = new List<string>(); var bindings = new List<string>(); var dependencies = new List<string>(); var duplicate = new List<string>();
            // The segment ids of the construction controller this entry names, for its construction segment problems.
            string[] constructionIds = null;
            // A transform stands for a top scene object or a part of a listed group.
            var part = c is Transform && GroupWorld.IsPart(go);
            var topObject = c is Transform && !part;
            // The object's own active flag. What a culler switches off by camera distance is no disabling; an object
            // that is off while its culler shows its objects was switched off by the scene or by a patch.
            var enabled = AuthoredActivity.Enabled(GroupWorld.OwnActivity(go), culled.ContainsKey(go), !hidden.Contains(go));
            // What the game reads whatever the object's activity: listed as a problem also for a disabled object.
            var anyway = new List<string>();
            // The circuits of the inspected crossing controller itself, by place, key and spans, for its detection checks.
            var detection = new List<Tuple<int, string, DetectionSpan[]>>();
            // The game enables and disables child behaviours while it runs, for example a crossing bell's audio player
            // by distance. Which components exist is a dependency; whether each is enabled is runtime state.
            var runtime = new List<string>();
            var components = go.GetComponentsInChildren<Component>(true);
            var hierarchyComponents = new List<HierarchyComponent>();
            // A crossing pad's builder generates its mesh into a container and destroys whatever is there first.
            Transform container = null;
            if (family == "Crossing pad marker") try { container = Field(Get(go, "Track.TrackCurveMeshBuilder"), "meshContainer") as Transform; } catch { }
            foreach (var child in components) {
                if (child == null) { problems.Add("Missing script in inspected hierarchy"); continue; }
                if (child is Transform) continue;
                // An object the game created while running is not saved with the scene: the audio pool parents its
                // sources under a crossing bell while it is audible and takes them back when it is not, and pad
                // generation replaces a crossing pad's mesh objects. Such a child and everything on it is runtime state.
                var below = container != null && child.transform != container && child.transform.IsChildOf(container);
                if (HierarchyDependencies.IsOutput(Transient(child.transform, go.transform), below)) {
                    runtime.Add((below ? "Generated pad object " : "Runtime object ") + Label(child));
                    continue;
                }
                if (HierarchyDependencies.IsRuntimeComponent(TypeName(child))) { runtime.Add("Runtime component " + Label(child)); continue; }
                // Which components exist, and who references them from an authored field, is a dependency; whether a
                // behaviour is enabled is runtime state, so the component carries no enabled value.
                hierarchyComponents.Add(new HierarchyComponent(Label(child), null, false, incoming.TryGetValue(child, out var refs) ? refs : null));
                if (child is Behaviour behaviour) runtime.Add(Label(child) + " enabled=" + behaviour.enabled);
                if (working.TryGetValue(child, out var held)) runtime.AddRange(held);
            }
            dependencies.AddRange(HierarchyDependencies.Describe(family == "Crossing pad marker", hierarchyComponents));
            if (incoming.TryGetValue(go, out var rootRefs)) dependencies.AddRange(rootRefs);
            if (working.TryGetValue(go, out var rootHeld)) runtime.AddRange(rootHeld);
            if (scanErrors != 0) problems.Add("Direct-reference scan incomplete: " + scanErrors + " unreadable fields");
            void Read(Action action) { try { action(); } catch (Exception e) { problems.Add("Inspection unavailable: " + e.GetBaseException().Message); } }
            var marker = TypeName(c) == Marker ? c : Get(go, Marker);
            // The holders of a duplicate marker ID; their lines and problem follow once the object's capability is known.
            MarkerIdHolders markerHolders = null;
            if (marker != null) Read(() => {
                var kind = Text(Field(marker, "type"));
                bindings.Add(InventoryPresentationState.MarkerIdLine + Text(Field(marker, "id")) + "; type=" + kind);
                // What the game needs from this kind's binding decides whether its state is a problem; the setups say
                // whether a new-game car placement reads it whatever its activity, and which ones do.
                var reading = starters.Reading(marker, kind, family == "Crossing pad marker");
                if (reading.Note != null) problems.Add(reading.Note);
                var follower = Get(go, Relative);
                var follows = follower != null && Field(follower, "targetActiveFollowsTrackGroup") is bool flag && flag;
                var before = problems.Count;
                MarkerLocation(Field(marker, "_location"), reading.Need, enabled, follows, bindings, problems, segmentIds, graphError);
                if (reading.Need == BindingNeed.ReadAnyway) anyway.AddRange(problems.Skip(before));
                // Every setup and car placement that reads a starter placement marker, with its cars and wreck setting.
                bindings.AddRange(reading.Readers);
                Registrations(marker, bindings);
                // A duplicate marker ID: every holder with its activity, and the owner of each registration under the
                // marker ID, with the guard's holder set. Which components hold the marker ID is the discovery's; the rest
                // is read now. These lines lead the dependencies: the first one says why the holders are listed.
                if (markerIds != null) {
                    markerIds().TryGetValue(Text(Field(marker, "id")), out var holders);
                    markerHolders = TrackMarkerWorld.HolderSet(marker, (holders ?? new List<Component>()).Where(Loaded));
                }
            });
            if (TypeName(c) == Span) Read(() => {
                bindings.Add("Span ID=" + Text(Field(c, "id")));
                Location(Field(c, "_lower"), "Lower", bindings, problems, segmentIds, graphError); Location(Field(c, "_upper"), "Upper", bindings, problems, segmentIds, graphError);
                dependencies.Add("Track graph span registration and occupancy consumers.");
            });
            var relative = TypeName(c) == Relative ? c : Get(go, Relative);
            if (relative != null) Read(() => {
                foreach (var name in new[] { "targetTransform", "targetOffset", "targetAngle", "snapToTerrain", "targetActiveFollowsTrackGroup", "_observedGroupId" })
                    bindings.Add("Track-relative " + name + "=" + Describe(Field(relative, name)));
                dependencies.Add("Track-relative placement and track-group subscription.");
            });
            if (TypeName(c) == Milepost) Read(() => {
                foreach (var name in new[] { "prefix", "origin", "mileage" }) bindings.Add("Milepost " + name + "=" + Describe(Field(c, name)));
                dependencies.Add("Milepost origin cache and rendered display.");
            });
            // A spawn point names what teleport offers, its priority and the start setups that name it.
            if (TypeName(c) == SpawnPoints.Component) Read(() => {
                bindings.AddRange(SpawnPointWorld.Bindings(c));
                dependencies.Add(SpawnPoints.Dependency);
            });
            if (TypeName(c) == ConstructionScenery) Read(() => {
                var ids = Field(c, "segmentIds") as string[];
                if (ids == null) throw new InvalidOperationException("Construction segment IDs unavailable");
                bindings.AddRange(CommandBand.ConstructionBindings(ids, segmentIds));
                constructionIds = ids;
                var entries = Field(c, "entries") as IEnumerable;
                if (entries == null) throw new InvalidOperationException("Construction scenery entries unavailable");
                foreach (var entry in entries) {
                    var targets = Field(entry, "targets") as GameObject[];
                    if (targets == null) throw new InvalidOperationException("Construction scenery targets unavailable");
                    var condition = "state=" + Text(Field(entry, "trackConstructionState")) + "; condition=" + Text(Field(entry, "condition"));
                    foreach (var target in targets) {
                        dependencies.Add("Construction target " + Describe(target) + "; " + condition + ".");
                        if (target == null) problems.Add("Missing construction-scenery target");
                    }
                }
                dependencies.Add("Native construction and track-group visibility control. Target placement is separate from controller placement.");
            });
            var controller = Parent(c, Crossing);
            if (controller != null) Read(() => {
                dependencies.Add("Controller " + Label(controller) + ".");
                var sets = Field(controller, "circuitSets") as IEnumerable;
                if (sets == null) throw new InvalidOperationException("Crossing circuit sets unavailable");
                int index = 0;
                // The controller itself reads the spans of its circuits, under the circuit's name on its Connections tab;
                // they are judged below (CircuitSpanChecks), once it is known whether a repair route applies. An object
                // below the controller lists the circuits and their missing roles as before. Without the track graph the
                // controller has one note.
                var own = ReferenceEquals(controller, c);
                if (own && segmentIds == null) problems.Add(CircuitSpanProblems.Unknown(graphError));
                foreach (var set in sets) {
                    string circuit = null;
                    if (own) try {
                        var island = Field(set, "island") as Component;
                        circuit = island == null ? null : CircuitSpanProblems.CircuitKey(Text(Field(island, "id")), CrossingCoverageWorld.AdditionOf(set));
                    } catch (Exception e) { problems.Add("Inspection unavailable: " + e.GetBaseException().Message); }
                    var read = new DetectionSpan[CircuitCoverage.Roles.Length];
                    for (var role = 0; role < read.Length; role++) {
                        var span = Field(set, CircuitCoverage.Roles[role]) as Component;
                        bindings.Add("Circuit " + index + " " + CircuitCoverage.Roles[role] + "=" + Label(span));
                        if (!own) { if (span == null) problems.Add("Missing circuit " + index + " " + CircuitCoverage.Roles[role]); continue; }
                        if (span == null) continue;
                        // A span that cannot be read is a note of its own, so the other spans and circuits stay listed.
                        try { read[role] = new DetectionSpan(Text(Field(span, "id")), StoredLocation(Field(span, "_lower")), StoredLocation(Field(span, "_upper"))); }
                        catch (Exception e) { read[role] = DetectionSpan.Unreadable(null, e.GetBaseException().Message); }
                    }
                    if (own) detection.Add(Tuple.Create(index, circuit, read));
                    index++;
                }
            });
            if (Get(go, BoardScene.CullerType) != null) dependencies.Add("Culling registration and origin-dependent position.");
            if (Get(go, BoardScene.MaskType) != null) dependencies.Add("Terrain mask registration and generated terrain/vegetation output.");
            if (topObject) dependencies.Add("Culling and colliders of every part follow the group.");
            if (part) dependencies.Add(SceneObjectRules.PartCullingDependency);
            // A storage pile shows its industry's storage by the ids its synchronizer reads, wherever the pile stands. The
            // ids are a binding; the storage's name is presentation only.
            string storageName = null;
            if (part && Get(go, Pile) != null) Read(() => {
                var storage = PileStorage(go);
                if (storage != null) { bindings.Add(storage.Item1); storageName = storage.Item2; dependencies.Add("Industry storage display: the pile rebuilds its logs and collider at its current position."); }
            });
            // A log pond's logs show their storage as a pile does; a top scene object or part that holds them names that
            // storage for the Object tab, as presentation only.
            if ((topObject || part) && Get(go, Pile) == null) Read(() => {
                var ponds = components.Where(component => component != null && TypeName(component) == LogPond).ToArray();
                if (ponds.Length == 0) return;
                var self = ponds[0].gameObject == go;
                var storage = PileStorage(ponds[0].gameObject);
                storageName = storage?.Item2;
                bindings.AddRange(CommandBand.LogPondBindings(self, self ? 0 : ponds.Length, storage?.Item1));
                if (self) dependencies.Add("Industry storage display: the log pond lays out its logs at its current position.");
            });
            // The water mesh of a lake surface moves with its object, unless the game draws it from a static batch; the
            // native lake masks and rivers do not move.
            if ((topObject || part) && components.Any(component => component != null && TypeName(component) == LakeSurface))
                dependencies.Add(CommandBand.LakeSurfaceDependency(GroupWorld.BatchedLake(go) != null));
            // A top scene object or part that holds storage piles below it says so, for the Object tab.
            if ((topObject || part) && Get(go, Pile) == null) Read(() => {
                var piles = components.Count(component => component != null && TypeName(component) == Pile);
                if (piles != 0) bindings.Add(CommandBand.PilesBinding(piles));
            });
            // A rail bridge construction shows or hides its parts by the construction state of the track segments its
            // controller names, wherever it stands: each segment is a binding of the top scene object and of every part below the
            // controller. A segment that no longer exists keeps its id with a remark. On the top scene object, whose Connections
            // tab repairs the construction segments, it is also a problem, once the group is known to be editable below.
            if (topObject || part) Read(() => {
                var ids = GroupWorld.ConstructionSegmentsOf(go);
                if (ids == null) return;
                bindings.AddRange(CommandBand.ConstructionBindings(ids, segmentIds));
                dependencies.Add(CommandBand.ConstructionDependency);
                constructionIds = ids;
            });
            // A scene ground mask shapes the terrain where its object stands: the part that holds it says so, and the
            // top scene object, a part above it and a marker object above it name that part. A bridge, milepost, crossing pad,
            // station map icon or spawn point moves its masks too.
            if (topObject || part || MovesGroundMasks(family)) Read(() => {
                var holders = GroundMaskWorld.Holders(go, out var flattens);
                if (holders.Count != 0) dependencies.Add(CommandBand.GroundMaskDependency(holders.Count == 1 && holders[0] == go, holders.Select(holder => holder.name), flattens, topObject || part, topObject));
            });
            // A building with pickable toggles: each entry names the toggles, the key-value object and the name text it
            // owns, its own object and those below it outside its parts, and counts the toggles in its parts. Other
            // groups read none of this, so their bindings stay as they were.
            if (topObject || part) Read(() => InteractiveBindings(go, topObject ? go : GroupWorld.TopObjectOf(go), bindings, dependencies));
            if (family.StartsWith("Bridge", StringComparison.Ordinal)) dependencies.Add("Colliders and culling. Separate construction scenery has its own placement.");
            var icon = family == StationMapIconRules.Family;
            if (icon) dependencies.Add(StationMapIconRules.Dependency);
            // Crossing equipment sits under objects that a culler switches off when the camera is far away, so whether it
            // is active in the game changes with every camera jump. Its activity is what the scene authored: every
            // object from it upward that no culler switches is switched on. What the cullers do now is runtime state.
            var active = go.activeInHierarchy;
            // A simple culler on a top scene object switches the root itself by camera distance: what it switches is runtime
            // state, as for the cullers of crossing equipment.
            if (topObject || part) enabled = GroupWorld.Enabled(go);
            if (CulledFamilies.Contains(family) || topObject || part) {
                var chain = new List<KeyValuePair<bool, bool>>();
                for (var t = go.transform; t != null; t = t.parent) {
                    var switched = culled.TryGetValue(t.gameObject, out var by);
                    if (!switched && GroupWorld.SwitchesItself(t.gameObject)) { switched = true; by = GroupWorld.SimpleCullerType; }
                    // An object that own culling switches for a moved part counts as the scene or the modder set it.
                    chain.Add(new KeyValuePair<bool, bool>(GroupWorld.OwnActivity(t.gameObject), switched));
                    if (switched) runtime.Add("Culled by " + by + ": " + BoardScene.PathOf(t) + " active=" + t.gameObject.activeSelf);
                }
                active = AuthoredActivity.Of(chain);
            }
            var position = go.transform.position;
            var coordinatesAvailable = false;
            Read(() => {
                // The map position is the scene position less the origin's offset. For an object the game does not shift
                // with its origin that difference changes with every origin move although nothing moved the object: its
                // position is the scene position, which is its map position at the first origin.
                position = MapPosition(go.transform);
                if (!MovesWithOrigin(go.transform)) runtime.Add("Position does not move with the world origin; it is the position in the scene.");
                coordinatesAvailable = !Single.IsNaN(position.x) && !Single.IsNaN(position.y) && !Single.IsNaN(position.z) && !Single.IsInfinity(position.x) && !Single.IsInfinity(position.y) && !Single.IsInfinity(position.z);
                if (!coordinatesAvailable) throw new InvalidOperationException("Map coordinates unavailable");
            });
            var rotation = go.transform.rotation;
            var board = family == "Location sign";
            string refusal = board ? "Board editing is unavailable" : null;
            if (board && boardRefusal != null) Read(() => refusal = boardRefusal(go));
            // Why an object of a supported family cannot be edited is no detected problem: it stands under Supported
            // edits.
            var bridge = family == "Bridge";
            string bridgeRefusal = null;
            if (bridge) Read(() => bridgeRefusal = BridgeWorld.PatternProblem(go));
            var marked = family == SceneFamilies.TrackMarker;
            string markerRefusal = marked ? "Generic track marker editing is unavailable" : null;
            if (marked) Read(() => markerRefusal = referrerError != null ? "the marker reference scan failed: " + referrerError
                : TrackMarkerWorld.Problem(go, markerReferrers, setups));
            var markerEditable = marked && markerRefusal == null;
            var pad = family == "Crossing pad marker";
            string padRefusal = pad ? "Crossing pad editing is unavailable" : null;
            if (pad) Read(() => padRefusal = referrerError != null ? "the marker reference scan failed: " + referrerError
                : CrossingPadWorld.PadProblem(go, markerReferrers, setups));
            var padEditable = pad && padRefusal == null;
            var milepost = family == "Milepost";
            string milepostRefusal = milepost ? "Milepost editing is unavailable" : null;
            if (milepost) Read(() => milepostRefusal = referrerError != null ? "the marker reference scan failed: " + referrerError
                : MilepostWorld.MilepostProblem(go, markerReferrers, setups));
            var milepostEditable = milepost && milepostRefusal == null;
            var spawnPoint = family == SpawnPoints.Family;
            string spawnRefusal = spawnPoint ? "Scene spawn point editing is unavailable." : null;
            if (spawnPoint) Read(() => spawnRefusal = SpawnPointWorld.Problem(go));
            string iconRefusal = null;
            if (icon) Read(() => iconRefusal = MapIconWorld.Problem(go));
            string groupRefusal = null; SceneObjectVerdict verdict = null;
            if (topObject) Read(() => { verdict = GroupWorld.VerdictOf(go); groupRefusal = GroupWorld.Problem(go); });
            string partRefusal = null, groupPath = null;
            if (part) Read(() => { partRefusal = GroupWorld.PartProblem(go); groupPath = GroupWorld.PartVerdictOf(go)?.GroupPath; });
            // An existing mast or bell that conforms to its pattern takes placement, rotation and Enabled; one that
            // does not stays listed with its reason under Supported edits.
            var mast = family == MastFamily; var ownBell = family == BellFamily;
            string equipmentRefusal = mast || ownBell ? "Existing crossing equipment editing is unavailable" : null;
            if (mast) Read(() => equipmentRefusal = CrossingDeviceWorld.MastProblem(go));
            if (ownBell) Read(() => equipmentRefusal = CrossingDeviceWorld.BellProblem(go));
            // A gate that stands on no light object is named by another component than a mast's target.
            if (mast && equipmentRefusal == null && TypeName(c) != ExistingEquipmentScene.MastComponent) equipmentRefusal = "this gate does not stand on a light object, so it is not part of a mast";
            if (mast) Read(() => bindings.AddRange(CrossingDeviceWorld.Describe(go)));
            // Which culler shows the equipment is runtime state: it changes with the camera and with a move.
            if (mast || ownBell) Read(() => runtime.AddRange(CrossingDeviceWorld.CullingState(go)));
            // Who names an object above the equipment in an authored field, such as a culler's list or a map feature's
            // or a progression's switch list: those can switch the equipment with the object above it.
            // Every object up to the scene root counts: a progression list on a place's scene object can name the crossing.
            if (mast || ownBell)
                for (var t = go.transform.parent; t != null; t = t.parent)
                    if (incoming.TryGetValue(t.gameObject, out var above)) foreach (var line in above) runtime.Add("Above this object, " + BoardScene.PathOf(t) + ": " + line);
            // A detection span, which a crossing controller holds in a circuit, belongs to that controller: crossing
            // coverage, on the controller's Connections tab, is its supported edit, and it has no placement of its own.
            GameObject spanHolder = null;
            if (family == "Track span") Read(() => spanHolder = CrossingDeviceWorld.HolderOf(c));
            var crossing = family == "Grade crossing controller";
            string coverageRefusal = crossing ? "Crossing coverage editing is unavailable" : null;
            if (crossing) Read(() => coverageRefusal = CrossingCoverageWorld.Problem(go));
            var coverageEditable = crossing && coverageRefusal == null;
            // A construction controller on an object of its own: its construction segments are its only edit.
            var scenery = family == CommandBand.ConstructionSceneryFamily;
            string sceneryRefusal = scenery ? ConstructionSegmentNative.Problem : null;
            // A kind no supported family covers is inspection only, which is another state than an object of a supported
            // family that cannot be edited: a Generic track marker whose type could not be read, and every family without
            // an edit. A starter placement marker is a supported kind with its own workflow.
            var starter = family == StarterPlacements.Family;
            string only = null;
            if (marked) Read(() => {
                var type = MarkerKind(marker);
                if (type != "Generic") only = CommandBand.NotYetSupported(type == null ? "track markers of an unknown type" : "track markers of type " + type);
            });
            else if (part) { }
            else if (topObject) only = CommandBand.GroupInspectionOnly(verdict, family);
            else if (family == "Track span") only = CommandBand.SpanInspectionOnly(spanHolder != null);
            else only = CommandBand.InspectionOnly(family, board || bridge || pad || crossing || starter || milepost || mast || ownBell || icon || spawnPoint);
            if (only != null) markerEditable = false;
            var structure = only != null ? null : part ? (partRefusal == null ? ChildObjects.Structure : null) : topObject ? (groupRefusal == null ? SceneObjects.Structure : null) : starter ? StarterPlacementScene.Structure : board ? BoardScene.Structure : bridge ? BridgeScene.Structure : markerEditable ? TrackMarkerScene.Structure : padEditable ? CrossingPadScene.Structure : milepostEditable ? MilepostScene.Structure : coverageEditable ? CrossingCoverageScene.Structure : mast && equipmentRefusal == null ? ExistingEquipmentScene.MastStructure : ownBell && equipmentRefusal == null ? ExistingEquipmentScene.BellStructure : icon && iconRefusal == null ? StationMapIconRules.Structure : spawnPoint && spawnRefusal == null ? SpawnPoints.Structure : scenery && sceneryRefusal == null ? ConstructionSceneryScene.Structure : null;
            var operations = only != null ? only : part ? (partRefusal == null ? CommandBand.PartOperations : CommandBand.NotEditable(partRefusal)) : topObject ? (groupRefusal == null ? (verdict?.Crossbuck == true ? CommandBand.CrossbuckOperations : CommandBand.GroupOperations) : CommandBand.NotEditable(groupRefusal)) : starter ? StarterPlacements.Operations : board ? (refusal == null ? "Move, rotate, enable, or disable this board." : CommandBand.NotEditable(refusal))
                : bridge ? (bridgeRefusal == null ? "Move, rotate, enable, or disable the bridge. Track scenery stays in place." : CommandBand.NotEditable(bridgeRefusal))
                : markerEditable ? CommandBand.MarkerOperations
                : marked ? CommandBand.NotEditable(markerRefusal)
                : padEditable ? CommandBand.PadOperations
                : pad ? CommandBand.NotEditable(padRefusal)
                : milepostEditable ? CommandBand.MilepostOperations
                : milepost ? CommandBand.NotEditable(milepostRefusal)
                : mast ? (equipmentRefusal == null ? CommandBand.MastOperations : CommandBand.NotEditable(equipmentRefusal))
                : ownBell ? (equipmentRefusal == null ? CommandBand.BellOperations : CommandBand.NotEditable(equipmentRefusal))
                : spawnPoint ? (spawnRefusal == null ? SpawnPoints.Operations : CommandBand.NotEditable(spawnRefusal))
                : icon ? (iconRefusal == null ? StationMapIconRules.Operations : CommandBand.NotEditable(iconRefusal))
                : scenery ? (sceneryRefusal == null ? CommandBand.ConstructionSceneryOperations : CommandBand.NotEditable(sceneryRefusal))
                : spanHolder != null ? CommandBand.DetectionSpanOperations
                : coverageEditable ? "Set which tracks each circuit detects or detach the circuit. You can add detection for extra tracks, or add lights, gates, and bells. The crossing and its original detection spans stay in place."
                : CommandBand.NotEditable(coverageRefusal);
            // An added light, gate or bell is edited on its crossing's Connections tab, not as an object of its own.
            string displayName = null;
            if (c is OwnedEquipment added) {
                displayName = added.DisplayName;
                structure = null;
                operations = CommandBand.AddedEquipmentOperations(displayName);
            }
            // A mast and a bell are named with their crossing, which tells the masts of several crossings apart.
            // A crossbuck is named with its crossing, and its place where crossings share a name, which tells the 52
            // crossbucks of the base map apart.
            string padMarkerId = null;
            if (topObject && verdict?.Crossbuck == true) Read(() => { padMarkerId = GroupWorld.TargetPadMarkerId(go); displayName = SceneObjectRules.CrossbuckName(verdict.Path, padMarkerId, GroupWorld.SharedCrossingNames()); });
            if ((mast || ownBell) && controller != null) Read(() => displayName = go.name + " at " + CrossingEquipment.Place(controller.gameObject.name, Ancestors(controller.transform)));
            string sharedPath = null;
            // A patch names its target by scene, path and component. Where more than one loaded object holds those, an
            // edit would be refused as not resolving uniquely: the object says so under Supported edits and offers none.
            if (only == null && structure != null) Read(() => {
                // A crossbuck that a pad marker ID identifies counts the crossbucks at its path that hold that id.
                var holders = padMarkerId != null ? GroupWorld.Crossbucks(go.scene.name, BoardScene.PathOf(go.transform)).Count(pair => pair.Value == padMarkerId)
                    : BoardScene.LoadedAt(go.scene.name, BoardScene.PathOf(go.transform)).Count(held => held != null && Get(held, TypeName(c)) != null);
                var shared = CommandBand.SharedPath(holders);
                if (shared != null) { structure = null; operations = CommandBand.NotEditable(shared); sharedPath = shared; }
            });
            // What the inspection could not read or check is a note, whatever the object's activity. Of the rest, a
            // disabled object keeps only what the game reads anyway.
            // A holder that can receive a replacement ID, a supported generic track marker, has the duplicate marker ID
            // as a problem while it is active, and its explanation offers the repair. Any other holder, such as a crossing
            // pad, shows the explanation without a problem: the patcher cannot repair it there.
            if (markerHolders != null) {
                var repairable = structure == TrackMarkerScene.Structure;
                duplicate.AddRange(markerHolders.Dependencies(go, repairable));
                if (markerHolders.Problem(go, repairable) is string shared) problems.Add(shared);
            }
            // A missing construction segment is a problem where the patcher can repair it: on the top scene object of an
            // editable rail bridge group and on the editable Ela controller. Elsewhere its remark on the Connections tab
            // stays, without a problem. Like any problem it is hidden while the object is switched off itself: the game's
            // controller (a GameBehaviour) runs only while its object is active, so a disabled construction does not fail.
            problems.AddRange(CommandBand.RepairableConstructionProblems(structure, constructionIds, segmentIds, graphError));
            // A crossing controller's detection findings are problems where Edit circuit or Edit detection addition can
            // repair them: on a controller whose crossing coverage is editable. Any other controller lists them as
            // information with the reason. The checks read the current scene only.
            var information = new List<string>();
            if (detection.Count != 0 && CircuitSpanChecks.Judges(ReferenceEquals(controller, c), structure, coverageRefusal, sharedPath, out var detectionRefusal)) Read(() => {
                IDetectionTrack track = null;
                if (segmentIds != null) try { track = CrossingDetectionTrack.Current(); } catch (Exception e) { problems.Add("Inspection unavailable: " + e.GetBaseException().Message); }
                foreach (var circuit in detection) {
                    var findings = CircuitSpanChecks.Findings(circuit.Item1, circuit.Item2, circuit.Item3, segmentIds, track, detectionRefusal);
                    problems.AddRange(findings.Problems.Concat(findings.Notes));
                    information.AddRange(findings.Information);
                }
            });
            var notes = problems.Where(BindingRules.IsNote).ToArray();
            var defects = BindingRules.Problems(enabled, problems.Where(line => !BindingRules.IsNote(line)), anyway.Where(line => !BindingRules.IsNote(line)));
            return new SceneObservation(go, new TargetIdentity(go.scene.name, BoardScene.PathOf(go.transform), TypeName(c), structure, padMarkerId), family,
                active, coordinatesAvailable ? new double[] { position.x, position.y, position.z } : Array.Empty<double>(), new double[] { rotation.x, rotation.y, rotation.z, rotation.w },
                bindings, duplicate.Concat(dependencies.Distinct().OrderBy(x => x, StringComparer.Ordinal)), defects, operations, runtime.Distinct().OrderBy(x => x, StringComparer.Ordinal), enabled, notes, only, displayName: displayName, groupPath: groupPath,
                category: topObject ? verdict?.Category : null, subcategory: topObject ? verdict?.Subcategory : null, bridge: topObject && verdict != null ? verdict.Bridge : BridgeKind.None,
                holderPath: spanHolder == null ? null : BoardScene.PathOf(spanHolder.transform), storage: storageName, information: information);
        }
        // The names of the objects that enclose a transform, from its parent upwards.
        static IReadOnlyList<string> Ancestors(Transform t) { var names = new List<string>(); for (var p = t.parent; p != null; p = p.parent) names.Add(p.name); return names; }
        // A marker's binding, judged by what the game needs from its kind. A span's two locations keep Location below.
        static void MarkerLocation(object location, BindingNeed need, bool enabled, bool followsGroup, List<string> bindings, List<string> problems, HashSet<string> segmentIds, string graphError) {
            var segment = Text(Field(location, "segmentId"));
            bindings.Add("Marker segment=" + segment + "; end=" + Text(Field(location, "end")) + "; distance=" + Text(Field(location, "distance")));
            var resolves = String.IsNullOrWhiteSpace(segment) ? false : segmentIds == null ? (bool?)null : segmentIds.Contains(segment);
            var problem = BindingRules.Problem(need, enabled, "Marker", segment, resolves) ?? BindingRules.Unknown(need, enabled, "Marker", segment, resolves, graphError);
            if (problem != null) problems.Add(problem);
            var note = BindingRules.Note(need, enabled, segment, resolves, followsGroup);
            if (note != null) bindings.Add("Marker binding note: " + note);
            // Existence is not proof that the binding names the intended track.
        }
        // A span end as the scene stores it: segment, end and distance, without resolving it on the graph.
        static TrackLocation StoredLocation(object location) =>
            new TrackLocation(Text(Field(location, "segmentId")), Text(Field(location, "end")) == "A" ? "a" : "b", Convert.ToDouble(Field(location, "distance"), CultureInfo.InvariantCulture));
        static void Location(object location, string role, List<string> bindings, List<string> problems, HashSet<string> segmentIds, string graphError) {
            var segment = Text(Field(location, "segmentId"));
            bindings.Add(role + " segment=" + segment + "; end=" + Text(Field(location, "end")) + "; distance=" + Text(Field(location, "distance")));
            if (String.IsNullOrWhiteSpace(segment)) problems.Add(role + " has an empty segment binding");
            else if (segmentIds == null) problems.Add(role + " binding validity unknown: " + graphError);
            else if (!segmentIds.Contains(segment)) problems.Add(role + " broken binding: missing segment " + segment);
            // Existence is not proof that the binding names the intended track.
        }
        // The track graph's named-location entries under the marker's ID: the registration that ID lookups find.
        internal const string ListedTrackLocation = CommandBand.ListedTrackLocation;
        static void Registrations(Component marker, List<string> bindings) {
            var id = Text(Field(marker, "id"));
            var graph = Native.Read(Native.Type("Track.Graph"), "Shared") ?? throw new InvalidOperationException("Track graph unavailable");
            var buckets = Field(graph, "_namedLocationsBySegmentId") as IDictionary ?? throw new InvalidOperationException("Named-location registration unavailable");
            var found = 0;
            foreach (DictionaryEntry bucket in buckets) {
                if (!(bucket.Value is IDictionary entries) || !entries.Contains(id)) continue;
                var named = entries[id]; var at = Field(named, "Location"); var segment = Field(at, "segment");
                var location = (segment == null ? "no segment" : Text(Field(segment, "id"))) + " / " + Text(Field(at, "end")) + " / " + Text(Field(at, "distance"));
                bindings.Add(ListedTrackLocation + " " + location + (segment != null && Equals(Field(segment, "id"), bucket.Key) ? "" : " under segment " + Text(bucket.Key)) +
                    (ReferenceEquals(Field(named, "ObjectValue"), marker) ? "." : ". This location belongs to another object."));
                found++;
            }
            if (found == 0) bindings.Add(ListedTrackLocation + " none.");
        }
        static string Describe(object value) => value is Component c ? Label(c) : value is GameObject go ? go.scene.name + "/" + BoardScene.PathOf(go.transform) : Text(value);
    }
}
