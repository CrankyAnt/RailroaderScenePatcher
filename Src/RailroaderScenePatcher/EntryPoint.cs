// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Railroader.Modding;
using RailroaderScenePatcher.Core;
using UI.Builder;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderScenePatcher {
    public sealed class EntryPoint : IModEntryPoint, IWorldLifecycle {
        // Reports a problem while no world state is bound, such as an unreadable world context.
        GameObject reporter;
        IModContext context;
        LocalWorldBinding<PatcherBehaviour> world;
        public void OnModLoad(IModContext value) {
            context = value; Hooks.Install(); NativeSaveClick.Install(); MapEditorReload.Install(); NativeRepairHooks.Install(this);
            if (NativeSaveClick.Problem != null) context.Log.Info(NativeSaveClick.Problem);
            if (NativeRepairHooks.Error != null) context.Log.Info(NativeRepairHooks.Error + ". Native marker checks and their warnings remain active.");
        }
        LocalWorldBinding<PatcherBehaviour> Binding() => world ?? (world = new LocalWorldBinding<PatcherBehaviour>(CreateHost, DestroyHost, h => h.BeginAttempt()));
        public void OnWorldLoad() {
            GroupWorld.WorldLoaded();
            object map, attempt;
            try { map = MapContext(); attempt = AttemptIdentity(); }
            catch (Exception e) { Unbound("Local world context unavailable: " + e.GetBaseException().Message + ". Report to the patcher maintainer."); return; }
            // The same map context keeps this peer's retained state; a changed attempt key or an earlier world unload starts a
            // new load attempt that first reconciles that state with the actual objects. Another map context replaces it.
            var binding = Binding();
            binding.Enter(map, attempt);
            var state = binding.State;
            if (state == null) { context?.Log.Info("Scene Patcher " + PatcherBehaviour.Version + ": " + binding.Error); return; }
            if (binding.ReentryError != null) state.ReportWorld(binding.ReentryError + ". The patcher started over for this world. Report to the patcher maintainer.");
            if (!state.Initialized) state.Initialize(context); else state.ApplyScenePatchFiles();
        }
        // The retained state belongs to the active map and layer identifiers in the native map editor or in play. It is a scoped
        // implementation key, not a verified world identity; each load attempt checks every object against its identity.
        static object MapContext() => Tuple.Create(String.Join("\n", ModScenePatchFiles.MapIds()), ModScenePatchFiles.IsMapEditor);
        // A replaced map manager or track graph starts a new load attempt within the same map context. Neither establishes
        // that the scene objects are new; the reconciliation decides that per object.
        static object AttemptIdentity() {
            var manager = Native.Read(Native.Type("Map.Runtime.MapManager"), "Instance") ?? throw new InvalidOperationException("Map manager unavailable");
            return Tuple.Create(manager, CurrentGraph());
        }
        static object CurrentGraph() {
            var graphType = Native.Type("Track.Graph");
            if (graphType == null) return null;
            try { return Native.Read(graphType, "Shared"); } catch (MissingMemberException) { return null; }
        }
        PatcherBehaviour CreateHost() {
            var host = new GameObject("CrankyAnt Scene Patcher"); UnityEngine.Object.DontDestroyOnLoad(host);
            var behaviour = host.AddComponent<PatcherBehaviour>();
            try { behaviour.PrepareRuntime(context); }
            catch { DestroyHost(behaviour); throw; }
            return behaviour;
        }
        static void DestroyHost(PatcherBehaviour behaviour) {
            if (behaviour == null) return;
            behaviour.Release();
            UnityEngine.Object.Destroy(behaviour.gameObject);
        }
        void Unbound(string message) {
            var state = world?.State;
            if (state == null) {
                if (reporter == null) { reporter = new GameObject("CrankyAnt Scene Patcher diagnostics"); UnityEngine.Object.DontDestroyOnLoad(reporter); reporter.AddComponent<PatcherBehaviour>(); }
                state = reporter.GetComponent<PatcherBehaviour>();
            }
            state.ReportWorld(message);
        }
        // A marker check can run before the world-load callback. It opens the load attempt for the current map context,
        // which the callback then joins.
        internal string BeforeMarkerCheck(Track.TrackMarker marker) {
            try {
                var binding = Binding();
                var state = binding.Prepare(MapContext());
                if (state == null) return "Early capability prerequisites unavailable: " + binding.Error;
                return state.BeforeMarkerCheck(context, marker);
            }
            catch (Exception e) { return "Early capability prerequisites unavailable: " + e.GetBaseException().Message; }
        }
        // The world's patcher state stays for a later load attempt over the same map context; the next attempt releases the
        // state of every object that did not survive. Parts bound to the native map editor end with the world.
        public void OnWorldUnload() {
            NativeRepairHooks.Reset();
            world?.State?.EndAttempt();
            world?.EndAttempt();
            ReleaseReporter();
        }
        public void OnModUnload() {
            NativeRepairHooks.Reset();
            world?.Release();
            ReleaseReporter();
            MapEditorReload.Remove(); NativeSaveClick.Remove(); Hooks.Remove(); NativeRepairHooks.Remove();
        }
        void ReleaseReporter() {
            if (reporter == null) return;
            reporter.GetComponent<PatcherBehaviour>().Release();
            UnityEngine.Object.Destroy(reporter); reporter = null;
        }
    }
    public sealed partial class PatcherBehaviour : MonoBehaviour {
        IModContext context;
        BoardScene world;
        IndependentBoards scene;
        NativeInventory native;
        SceneCapabilities capabilities;
        PatchEngine engine;
        PatchRuntime runtime;
        NativePatchDiagnostics nativeDiagnostics;
        readonly Dictionary<string, KeyValuePair<string, string>> pendingWarnings = new Dictionary<string, KeyValuePair<string, string>>();
        // Informational overlap notices by native deduplication key; a newer notice for the same object replaces the pending one.
        readonly Dictionary<string, KeyValuePair<string, string>> pendingNotices = new Dictionary<string, KeyValuePair<string, string>>();
        bool presentationErrorLogged;
        TrackMarkerWorld markerWorld;
        internal bool Initialized { get; private set; }
        SceneInventory inventory;
        InspectionInputBlocker inputBlocker;
        SpatialSelection selection;
        SnapController snap;
        SnapRequest preparedSnap;
        bool choosingSnap;
        bool worldEditActionOpen;
        string worldEditActionTab;
        int worldEditActionMode;
        object worldEditActionTarget;
        TMP_InputField numericEditField;
        int numericEditFocusFrame;
        GizmoController gizmo;
        SceneObservation selected;
        string selectionProblem;
        string scenePatchFileInspection = "";
        // The scene patch files the last refresh read, and whether they changed outside scene patch editing since.
        readonly SuppliedDefinitions definitions = new SuppliedDefinitions();
        bool definitionsStale => definitions.Stale;
        float nextInventoryCheck, nextDefinitionCheck;
        // The share of one frame that the open panel's scene comparison may take. One unit of the scan can exceed it.
        const double InventoryCheckBudgetMs = 2;
        bool urgentCheck;
        readonly System.Diagnostics.Stopwatch inventoryCheckClock = new System.Diagnostics.Stopwatch();
        bool inMapEditor;
        string status = "Initializing", filter = "";
        readonly HashSet<string> reported = new HashSet<string>();
        readonly List<string> diagnostics = new List<string>();
        NativePresentation presentation;
        Texture2D worldLabelBacking;
        object attachedMapEditor;
        string presentationSnapshot;
        readonly InventoryPresentationState inventoryPresentation = new InventoryPresentationState();
        internal const string Version = "0.5.0";
        MapEditorAttachment attachment;
        string reloadRequirement;
        float nextReloadCheck;
        object refusedMapEditor;
        string editingStatus = "";
        // The last edit results, shown in the message area above the action slots and written to the log. A place is the
        // action group a message came from; the message area shows it as the message's source.
        readonly EditFeedback feedback = NewFeedback();
        // The footer's actions and the commands of the band and the outliner are the same on every tab: their results
        // stay through a tab switch.
        static EditFeedback NewFeedback() {
            var created = new EditFeedback();
            foreach (var place in new[] { ModificationPlace, SavePlace, EditFeedback.CopyIdPlace, "Fit" }) created.GeneralPlaces.Add(place);
            return created;
        }
        const string BindingPlace = "Track binding", PlacementPlace = "Placement", GizmoPlace = CommandBand.GizmoPlace, SnapPlace = "Snap", ActivityPlace = "Activity",
            ModificationPlace = "Modification", SavePlace = "Save", TerrainPlace = "Terrain output", CoveragePlace = "Crossing coverage", LightGatePlace = "Lights and gates", BellPlace = "Bells", WreckPlace = "Start derailed";
        // The place of the open equipment form's messages: its bells or its lights and gates.
        string EquipmentPlace => equipmentSource?.IsDefinition == true ? BellPlace : LightGatePlace;
        bool staleLogged = true;
        NativeTrackPoses trackPoses;
        CrossingCoverageScene crossingCoverageScene;
        float nextTrackFollow;
        const string FollowPlace = "Follow";
        readonly string[] placementFields = { "", "", "", "", "", "" };
        readonly string[] binding = { "", "a", "" };
        TrackLocation pickedTrackLocation;
        ConfirmedSelection pickedTrackSelection;
        // Crossing coverage being edited: the circuit, four fields per role (segment, end, from, to) and the previewed change.
        // coverageAddition is the ID of the detection addition being added or edited, or null while a native circuit is edited.
        string coverageCircuit, coverageAddition, newAddition = "";
        int? choosingCoverageRole;
        readonly string[] coverageFields = new string[12];
        sealed class PreparedCoverage { internal string Circuit, Addition; internal CircuitCoverage Value; internal CoveragePreview Preview; }
        PreparedCoverage preparedCoverage;
        // A light or gate being added or edited: the chosen source, seven fields (ID, position x/y/z, rotation x/y/z in
        // degrees) and the previewed change, which holds the addition to add or the ID to remove. Null source: no form open.
        EquipmentSource equipmentSource;
        string equipmentLabel;
        readonly string[] equipmentFields = { "", "", "", "", "0", "0", "0" };
        sealed class PreparedEquipment { internal EquipmentAddition Addition; internal string RemoveId; internal EquipmentPreview Preview; }
        PreparedEquipment preparedEquipment;
        // Scopes native warning deduplication to one load attempt.
        string loadId = Guid.NewGuid().ToString("N");
        internal void Initialize(IModContext value) {
            context = value;
            outlinerWidth = ReadOutlinerWidth();
            Initialized = true;
            feedback.Log = message => {
                var outstanding = "";
                try { if (attachment != null) outstanding = " Outstanding: " + attachment.Outstanding(); } catch { }
                context?.Log.Info(CommandBand.LogLine(feedback.Place, selected?.Identity.Path, message) + outstanding);
            };
#if ISSUE39_OBSERVATION
            JourneyObservation.Sink = message => Debug.Log("[Issue39Attempt] " + message);
#endif
            try {
                inMapEditor = ModScenePatchFiles.IsMapEditor;
                try {
                    world = new BoardScene();
                    scene = new IndependentBoards(world, () => native.Discover());
                } catch (Exception e) {
                    world?.Dispose(); world = null;
                    status = "Independent board capability incompatible or unavailable: " + e.GetBaseException().Message;
                    Report("capability", status);
                }
                PrepareRuntime(value);
                if (inMapEditor) {
                    inventory = new SceneInventory(native);
                    selection = new SpatialSelection(native, () => inventory?.Entries);
                }
                if (inMapEditor) {
                    try {
                        inputBlocker = gameObject.AddComponent<InspectionInputBlocker>();
                        inputBlocker.Initialize();
                        inputBlocker.RefreshRegions = SyncInputRegions;
                        inputBlocker.OverWorldLabel = point => presentation?.IsOpen == true &&
                            selection?.OverLabel(point, presentation.WorldUiBounds()) == true;
                    }
                    catch (Exception e) {
                        inputBlocker?.Release(); inputBlocker = null;
                        Report("inspection-input", "Inspection UI unavailable: " + e.GetBaseException().Message);
                    }
                }
                // Each capability checks its own game functions; an incompatible one fails only the modifications naming its structure.
                capabilities.Live = (target, identity) => target is GameObject live && live != null && live.scene.IsValid() && live.scene.isLoaded &&
                    live.scene.name == identity.Scene && BoardScene.PathOf(live.transform) == identity.Path;
                if (scene != null) capabilities.Add(scene);
                else capabilities.AddUnavailable(IndependentBoards.Structure, status);
                try { capabilities.Add(new BridgeScene(new BridgeWorld(), Capability.Unchecked)); }
                catch (Exception e) {
                    capabilities.AddUnavailable(BridgeScene.Structure, e.GetBaseException().Message);
                    context.Log.Info("Scene Patcher " + Version + ": bridge capability incompatible or unavailable: " + e.GetBaseException().Message);
                }
                // Groups and their parts share one world and the fixed placements that hold parts when their group moves.
                try {
                    var groupWorld = new GroupWorld(); var holds = new PartHolds();
                    capabilities.Add(new BakedSceneryGroups(groupWorld, Capability.Unchecked, holds));
                    capabilities.Add(new BakedSceneryParts(groupWorld, holds));
                } catch (Exception e) {
                    capabilities.AddUnavailable(BakedSceneryGroups.Structure, e.GetBaseException().Message);
                    capabilities.AddUnavailable(BakedSceneryParts.Structure, e.GetBaseException().Message);
                    context.Log.Info("Scene Patcher " + Version + ": baked scenery group capability incompatible or unavailable: " + e.GetBaseException().Message);
                }
                try { capabilities.Add(new CrossingPadScene(new CrossingPadWorld { KnownReferrers = KnownReferrers }, Capability.Unchecked)); }
                catch (Exception e) {
                    capabilities.AddUnavailable(CrossingPadScene.Structure, e.GetBaseException().Message);
                    context.Log.Info("Scene Patcher " + Version + ": crossing-pad capability incompatible or unavailable: " + e.GetBaseException().Message);
                }
                try { capabilities.Add(new MilepostScene(new MilepostWorld { KnownReferrers = KnownReferrers }, Capability.Unchecked)); }
                catch (Exception e) {
                    capabilities.AddUnavailable(MilepostScene.Structure, e.GetBaseException().Message);
                    context.Log.Info("Scene Patcher " + Version + ": milepost capability incompatible or unavailable: " + e.GetBaseException().Message);
                }
                try { capabilities.Add(new StarterPlacementScene(new StarterPlacementWorld { KnownReferrers = KnownReferrers }, Capability.Unchecked)); }
                catch (Exception e) {
                    capabilities.AddUnavailable(StarterPlacementScene.Structure, e.GetBaseException().Message);
                    context.Log.Info("Scene Patcher " + Version + ": starter placement capability incompatible or unavailable: " + e.GetBaseException().Message);
                }
                CrossingEquipmentWorld equipmentWorld = null;
                try { equipmentWorld = new CrossingEquipmentWorld { Warn = Report }; }
                catch (Exception e) { context.Log.Info("Scene Patcher " + Version + ": crossing-equipment capability incompatible or unavailable: " + e.GetBaseException().Message); }
                try { capabilities.Add(crossingCoverageScene = new CrossingCoverageScene(new CrossingCoverageWorld(), Capability.Unchecked, equipmentWorld)); }
                catch (Exception e) {
                    capabilities.AddUnavailable(CrossingCoverageScene.Structure, e.GetBaseException().Message);
                    context.Log.Info("Scene Patcher " + Version + ": crossing-coverage capability incompatible or unavailable: " + e.GetBaseException().Message);
                }
                try { capabilities.Tracks = trackPoses = new NativeTrackPoses(); }
                catch (Exception e) { context.Log.Info("Scene Patcher " + Version + ": track-following placement unavailable: " + e.GetBaseException().Message); }
                context.Log.Info("Scene Patcher " + Version + ": native capabilities checked; game " + Application.version + "; production integration unchecked");
                ApplyScenePatchFiles();
            } catch (Exception e) {
                status = "Scene capabilities unavailable: " + e.GetBaseException().Message;
                Report("capability", status);
            }
        }
        internal void PrepareRuntime(IModContext value) {
            if (engine != null) return;
            context = value; inMapEditor = ModScenePatchFiles.IsMapEditor;
            native = new NativeInventory(g => scene == null ? "Independent board editing is unavailable" : scene.Refusal(g));
            ModScenePatchFiles.Log = message => context?.Log.Info(message);
            capabilities = new SceneCapabilities(ModScenePatchFiles.MapIds, native) { MapPatches = ModScenePatchFiles.MapPatchIds };
            try { markerWorld = new TrackMarkerWorld(); markerWorld.KnownReferrers = KnownReferrers; capabilities.Add(new TrackMarkerScene(markerWorld, Capability.Unchecked)); }
            catch (Exception e) { capabilities.AddUnavailable(TrackMarkerScene.Structure, e.GetBaseException().Message); }
            engine = new PatchEngine(capabilities);
            nativeDiagnostics = new NativePatchDiagnostics(Report);
            runtime = new PatchRuntime(engine, nativeDiagnostics, Version, Application.version);
        }
        internal string BeforeMarkerCheck(IModContext value, Track.TrackMarker marker) {
            PrepareRuntime(value);
            if (markerWorld == null) return "Track-marker capability prerequisites unavailable before TrackMarker.OnEnable";
            var identity = new TargetIdentity(marker.gameObject.scene.name, BoardScene.PathOf(marker.transform), TrackMarkerWorld.MarkerType, TrackMarkerScene.Structure);
            return markerWorld.DuringEnable(marker.gameObject, () => {
                bool attempted = false;
                string unavailable = null;
                foreach (var mod in ModScenePatchFiles.Discover()) {
                    if (!mod.Applicable) continue;
                    var result = runtime.BeforeNativeCheck(mod.Json, mod.Source, identity);
                    if (result.Outcomes.Count > 0) attempted = true;
                    if (result.EarlyRouteUnavailable != null) unavailable = result.EarlyRouteUnavailable;
                }
                return attempted ? null : unavailable ?? "No applicable dependency-free relationship-only repair before TrackMarker.OnEnable";
            });
        }
        internal void ApplyScenePatchFiles() {
            if (engine == null) return;
            if (world != null) {
                try { world.Rebind(); }
                catch (Exception e) { Report("terrain-rebind", "Terrain completion for independent boards is unavailable in this load: " + e.GetBaseException().Message + ". Reload the map. Report to the patcher maintainer."); }
            }
            IReadOnlyList<LoadedScenePatchFile> scenePatchFiles;
            try { scenePatchFiles = runtime.ApplyLoaded(ModScenePatchFiles.Discover(), overlapNotices ?? (overlapNotices = new NativeOverlapNotices(this))); }
            catch (Exception e) { Report("discovery", "Loaded mod discovery failed. Report to the patcher maintainer. " + e.GetBaseException().Message); return; }
            // One line per scene patch file per load attempt says what happened to it, also when nothing else is reported: a
            // scene patch file for another map is skipped without a failure.
            loadAttempts++;
            string[] maps = null;
            try { maps = ModScenePatchFiles.MapIds(); } catch { }
            foreach (var scenePatchFile in scenePatchFiles) {
                var mod = scenePatchFile.Mod;
                try { context?.Log.Info(scenePatchFile.LogLine(Version, loadAttempts, maps)); } catch { }
                // Follow-up work for one scene patch file cannot stop the others; their application has already happened in load order.
                try {
                    if (scenePatchFile.PresentationError != null) context?.Log.Info(Describe(mod, scenePatchFile.PresentationError));
                    if (scenePatchFile.Result == null) {
                        if (mod.Error != null) EditedScenePatchFileDiagnostic(mod, Describe(mod, mod.Error));
                        continue;
                    }
                    var result = scenePatchFile.Result;
                    if (result.Error != null) {
                        if (result.MapEditorOnly) EditedScenePatchFileDiagnostic(mod, Describe(mod, result.Error));
                        continue;
                    }
                    foreach (var item in result.Outcomes) {
                        if (!item.Value.Applied) {
                            var error = Describe(mod, item.Key + ": " + item.Value.Error);
                            if (item.Value.MapEditorOnly) EditedScenePatchFileDiagnostic(mod, error);
                        }
                        else ReconcileMarkerWarning(mod, item.Key, item.Value);
                    }
                } catch (Exception e) { context?.Log.Info(Describe(mod, "Follow-up after application failed: " + e.GetBaseException().Message)); }
            }
            status = "Scene patch files processed. " + (scene?.Status ?? "");
        }
        NativeOverlapNotices overlapNotices;
        // The load attempts of this world so far; the log lines of one attempt carry its number.
        int loadAttempts;
        sealed class NativeOverlapNotices : IOverlapNotices {
            readonly PatcherBehaviour owner;
            internal NativeOverlapNotices(PatcherBehaviour owner) { this.owner = owner; }
            public void Notice(OverlapNotice notice) {
                owner.context?.Log.Info("Scene Patcher overlap notice: " + notice.Message);
                owner.pendingNotices[notice.Key] = new KeyValuePair<string, string>(notice.Target.Scene + "/" + notice.Target.Path, notice.Message);
                owner.FlushWarnings();
            }
        }
        void ReconcileMarkerWarning(LoadedMod mod, string operation, Outcome outcome) {
            if (markerWorld == null || !(outcome.ResolvedTarget is GameObject target) || target == null) return;
            var scenePatchFile = JObject.Parse(mod.Json);
            var entry = (JObject)scenePatchFile["modifications"][operation];
            // Placement or activity alone does not repair a track-marker relationship.
            var binding = entry["relationships"]?["trackMarker"] as JObject;
            if (binding == null || binding["segment"]?.Type != JTokenType.String) return;
            foreach (var attribution in NativeRepairHooks.Warnings.Where(w => ReferenceEquals(w.Target, target))) {
                if (runtime.Reconcile(mod.Source, operation, attribution, nativeDiagnostics, () => {
                    if (target == null) return false;
                    var matches = capabilities.Resolve(outcome.Target);
                    if (matches.Count != 1 || !ReferenceEquals(matches[0], target)) return false;
                    capabilities.Preflight(target);
                    var actual = markerWorld.Location(target);
                    return ModScenePatchFiles.MapIds().Contains((string)scenePatchFile["sourceMap"]["mapId"], StringComparer.Ordinal) && actual != null && actual.Segment == (string)binding["segment"] && actual.End == (string)binding["end"] && Math.Abs(actual.Distance - (double)binding["distance"]) <= 0.001 && markerWorld.DependentProblem(target) == null;
                })) context?.Log.Info("Scene Patcher reconciled one exactly attributed current marker warning after verified repair. Earlier route unavailable: " + attribution.EarlyRouteUnavailable);
            }
        }
        static string Describe(LoadedMod loaded, string message) {
            var mod = loaded.Source;
            return (mod.Name ?? mod.Identifier) + " [" + mod.Identifier + " " + mod.Version + "] " + message + ". Patcher " + Version + ". Mod author: " + (mod.Author ?? "not supplied") + (String.IsNullOrWhiteSpace(mod.Website) ? "" : "; website: " + mod.Website);
        }
        internal void ReportWorld(string message) { Report("world", message); }
        void MapEditorDiagnostic(string message) { if (inMapEditor && !diagnostics.Contains(message)) diagnostics.Add(message); }
        // A diagnostic of the edited mod's scene patch file at world load. Recovery replaces that scene patch file in the session,
        // and then replaces these diagnostics too.
        void EditedScenePatchFileDiagnostic(LoadedMod mod, string message) {
            MapEditorDiagnostic(message);
            if (mod.EditTarget && inMapEditor) editedDiagnostics.Add(message);
        }
        // The game's Log window heads a row with its originator, the thing it is about, and its source, the part of the
        // game that reports it, as native rows name a mod file or definition with their subsystem. The source is this
        // patcher's one name; the originator is the supplying mod's scene patch file, the object of an overlap notice, or
        // the patcher's own mod for a state of the patcher.
        const string LogSource = "Railroader Scene Patcher";
        void Report(string key, string message, string originator = LoadedMod.PatcherId) {
            if (!reported.Add(key)) return;
            diagnostics.Add(message); status = message;
            context?.Log.Info(message);
            pendingWarnings[key] = new KeyValuePair<string, string>(originator, message);
            FlushWarnings();
        }
        void FlushWarnings() {
            // Before collector initialization MessageLog falls back to Serilog, which is not player presentation.
            if (Game.Diagnostics.MessageCollector.Shared == null) return;
            foreach (var item in pendingWarnings.ToArray()) {
                try {
                    Game.Diagnostics.MessageLog.Warning(item.Value.Key, item.Value.Value, LogSource, loadId + item.Key);
                    pendingWarnings.Remove(item.Key);
                } catch (Exception e) {
                    if (!presentationErrorLogged) { presentationErrorLogged = true; context?.Log.Info("Native structured warning delivery unavailable; delivery remains pending: " + e.GetBaseException().Message); }
                    return;
                }
            }
            // An informational row: it reports possible incompatibility and is never a failure warning.
            foreach (var item in pendingNotices.ToArray()) {
                try {
                    Game.Diagnostics.MessageLog.Info(item.Value.Key, item.Value.Value, LogSource, loadId + item.Key);
                    pendingNotices.Remove(item.Key);
                } catch (Exception e) {
                    if (!presentationErrorLogged) { presentationErrorLogged = true; context?.Log.Info("Native structured notice delivery unavailable; delivery remains pending: " + e.GetBaseException().Message); }
                    return;
                }
            }
        }
        // The native map editor appears after world load and is replaced by a native map editor reload.
        void Attach() {
            if (!inMapEditor || engine == null) return;
            var controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared");
            var found = controller as UnityEngine.Object == null ? null : Native.Read(controller, "MapEditor");
            if (ReferenceEquals(found, attachedMapEditor)) return;
            presentation?.Dispose(); presentation = null;
            selection?.Cancel(); selectionProblem = null; ClearSnap();
            Detach(); attachedMapEditor = found; refusedMapEditor = null; presentationSnapshot = null;
            if (found == null) return;
            if (inputBlocker != null && inputBlocker.Available) {
                try {
                    presentation = new NativePresentation(DrawWindow, () => {
                        CancelGuided();
                        CancelGizmo(); selection?.Cancel(); selectionProblem = null; ClearSnap();
                        CancelRequest("the panel was closed before the request ran");
                    });
                    presentation.ResetRequested = ResetLayout;
                    presentation.Details = builder => presentation.Scroll(builder, 120, content => { content.ConfigureLayout(4f); DrawSession(content); }, false);
                    presentation.Help = (builder, region) => presentation.Scroll(builder, 80, content => { content.ConfigureLayout(4f); DrawHelp(content, region); }, false);
                    if (presentation.TitleIconProblem != null) context?.Log.Info("A title-bar icon could not join the title bar (" + presentation.TitleIconProblem + "); the window help holds a button for it.");
                    if (presentation.TitleHelpProblem != null) context?.Log.Info("The window help button could not join the title bar (" + presentation.TitleHelpProblem + "); it stands in the command band.");
                }
                catch (Exception e) {
                    Report("inspection-presentation", "Inspection UI unavailable: " + e.GetBaseException().Message);
                    context?.Log.Info("Inspection UI unavailable: " + e);
                }
            }
            if (Hooks.Error != null) { refusedMapEditor = found; editingStatus = Hooks.Error; return; }
            try {
                attachment = new MapEditorAttachment(engine, found); Hooks.Current = attachment;
                attachment.VersionChanged = VersionChanged;
                attachment.SaveRefused = reason => {
                    feedback.Refuse(SavePlace, "Save", reason);
                    var alert = CommandBand.SaveRefusalAlert(presentation != null && presentation.IsOpen, reason, attachment.Session.VersionState == FileVersionState.Changed && reason == attachment.Session.SaveRefusal);
                    if (alert != null) UI.Common.ModalAlertController.PresentOkay(alert.Value.Title, alert.Value.Message);
                };
                attachment.Session.Replayed = identity => replayed.Add(identity);
                attachment.Session.HistoryNotice = feedback.QueueHistoryNotice;
                // The patcher's own Save writes the scene patch file: that text is what the definitions on disk now are, so it
                // is no change from outside and the inventory lamp stays Current.
                ModScenePatchFiles.Written = text => { try { definitions.Written(ModScenePatchFiles.EditedModId(), text); } catch { } };
                filled = false;
                snap = new SnapController(attachment.Session, capabilities, new NativeSnapScene(native));
                attachment.Log = message => context?.Log.Info(message);
                editingStatus = attachment.Session.Blocked ?? "Editing ready for " + attachment.PatchPath;
                // The native map editor built its title before this session could account for the scene patch file.
                attachment.RefreshChrome();
            } catch (Exception e) { refusedMapEditor = found; editingStatus = "Editing unavailable: " + e.GetBaseException().Message; }
        }
        void Detach() {
            reloadRequirement = null;
            presentation?.SetFileLock(null);
            CancelGuided();
            pickedTrackLocation = null; pickedTrackSelection = null;
            ClearSnap(); snap = null;
            CancelGizmo();
            // A request made in this native map editor does not run in the next one.
            CancelRequest("you left the Map Editor before the request ran");
            ModScenePatchFiles.Written = null;
            if (attachment == null) return;
            if (ReferenceEquals(Hooks.Current, attachment)) Hooks.Current = null;
            attachment.Dispose(); attachment = null;
        }
        void SyncInputRegions() {
            if (inputBlocker == null) return;
            var open = presentation != null && presentation.IsOpen;
            inputBlocker.SetRegions(open, open ? presentation.PanelBounds : Rect.zero, presentation == null ? Rect.zero : presentation.EntryBounds);
            var pendingInspectOnly = selection?.Pending?.InspectOnly == true;
            var pendingEdit = selection?.Pending != null && !pendingInspectOnly;
            var gizmoShown = gizmo?.Shown == true;
            if (worldEditActionOpen && (!open || worldEditActionTab != inspectorTab.Value || worldEditActionMode != placementMode || !ReferenceEquals(worldEditActionTarget, selected?.Handle)))
                EndWorldEditAction();
            ReleaseUnchangedNumericEdit();
            inputBlocker.WorldPicking = open && pendingEdit;
            inputBlocker.WorldInspecting = open && pendingInspectOnly;
            inputBlocker.WorldEditing = open && (pendingEdit || worldEditActionOpen || gizmoShown);
            inputBlocker.GizmoShown = open && gizmoShown;
        }
        void BeginWorldEditAction() {
            worldEditActionOpen = true;
            worldEditActionTab = inspectorTab.Value;
            worldEditActionMode = placementMode;
            worldEditActionTarget = selected?.Handle;
        }
        void EndWorldEditAction() {
            worldEditActionOpen = false;
            worldEditActionTab = null;
            worldEditActionTarget = null;
            numericEditField = null;
        }
        void BeginNumericWorldEdit(TMP_InputField field) {
            BeginWorldEditAction();
            numericEditField = field;
            numericEditFocusFrame = Time.frameCount;
        }
        void ReleaseUnchangedNumericEdit() {
            if (ReferenceEquals(numericEditField, null)) return;
            var focused = numericEditField != null && (numericEditField.isFocused || numericEditFocusFrame == Time.frameCount);
            if (!CommandBand.NumericEditBlocksWorld(focused, placementFields, focused ? null : CurrentPlacement())) EndWorldEditAction();
            if (!focused) numericEditField = null;
        }
        // A completed edit changed the observed values, so the inventory is read again before the next edit binds to it.
        // That read scans the whole scene. It runs two frames after the edit, so the edit's result is on screen first;
        // until then the inventory is unchanged and an edit is refused as stale, as it would be without the refresh.
        int refreshFrame, replayFrame;
        bool fullRefresh, filled, escapeHeld;
        string idleKey;
        SceneObservation refreshTarget;
        readonly List<TargetIdentity> replayed = new List<TargetIdentity>();
        readonly List<object> moved = new List<object>();
        void RefreshAfterEdit() { refreshFrame = Time.frameCount + 2; refreshTarget = selected; inventory?.CancelFreshnessCheck(); }
        // The snapshot was compared with the whole scene, by a refresh or a completed pass, while the native map editor's history
        // read comparedKey; comparedAt is when. An edit or a preview that starts while that still holds, and soon
        // enough, needs no pass of its own: nothing but this patcher changed the authored scene state since, and the
        // patcher's own changes are observed again as they happen. The edit's guard still observes its target.
        string comparedKey, passKey;
        bool running;
        // The marker reference scan the panel vouches for: the one of the discovery its snapshot was compared with,
        // while a queued request runs or while the snapshot still counts as compared, as when a gizmo is released.
        ReferenceScan KnownReferrers() => inMapEditor && inventory != null && !inventory.IsStale && (running || Compared()) ? NativeInventory.LastReferrers : null;
        // Inspects an observation offered by a world choice. The choice offers the snapshot's own entries, so the entry
        // is inspected as it is; an observation that is no entry is looked up by its object, and only when the snapshot
        // does not hold it the inventory is read again.
        void InspectObservation(SceneObservation observation) {
            if (!GuidedSelect(observation)) return;
            var entry = inventory == null ? null : inventory.Entries.Any(e => ReferenceEquals(e, observation)) ? observation : inventoryPresentation.CurrentSelection(inventory, observation);
            if (entry != null) { ClearSnap(); selected = entry; return; }
            selected = observation; Inspect();
        }
        float comparedAt;
        const float ComparedSeconds = 120;
        bool Compared() => inventory != null && !inventory.IsStale && comparedKey != null && comparedKey == ChangeStamp() && Time.unscaledTime - comparedAt < ComparedSeconds;
        // One step of the stepped comparison. A pass that completes without a difference counts for the history value
        // it started under.
        void Step(double budget) {
            if (!inventory.CheckInProgress) passKey = ChangeStamp();
            var before = inventory.CompletedPasses;
            inventoryCheckClock.Restart();
            inventory.AdvanceFreshnessCheck(() => inventoryCheckClock.Elapsed.TotalMilliseconds < budget);
            if (inventory.CompletedPasses != before) { comparedKey = passKey; comparedAt = Time.unscaledTime; }
        }
        // This patcher changed the native map editor's history itself, by an edit or by Undo or Redo of its own step. The change is
        // no foreign one: it starts no urgent pass, and a compared snapshot stays compared once the changed objects are
        // observed again.
        void OwnChange() {
            var now = ChangeStamp();
            if (comparedKey != null && comparedKey == idleKey) comparedKey = now;
            idleKey = now;
        }
        // The entry of the inspected object after related observations were replaced.
        void KeepSelection(SceneObservation before, SceneObservation updated) {
            if (ReferenceEquals(selected, before)) selected = updated;
            else if (selected != null && !inventory.Entries.Any(e => ReferenceEquals(e, selected))) selected = inventoryPresentation.CurrentSelection(inventory, selected);
        }
        // After the game's own tooltip update of the frame.
        void LateUpdate() { presentation?.PauseTooltip(); }
        bool applicationFocused, fileCheckAfterFocus;
        float nextFileVersionCheck;
        void OnApplicationFocus(bool focused) {
            applicationFocused = focused;
            if (focused) fileCheckAfterFocus = true;
        }
        void CheckFileVersion() {
            if (attachment == null) return;
            try {
                var session = attachment.Session;
                var changed = feedback.CheckFileVersion(session, RecoveryPlace);
                if (session.ImportOutdated) { session.ImportOutdated = false; feedback.Report(ImportPlace, FilePanelState.ImportOutdated); changed = true; }
                if (changed) VersionChanged();
            } catch (Exception e) { attachment.Fault = "Could not check the scene patch file: " + e.GetBaseException().Message; }
        }
        void VersionChanged() {
            if (attachment != null) feedback.ObserveFileVersion(attachment.Session, RecoveryPlace);
            // While the file panel is open, nothing outside it may continue: a pending world choice, gizmo, snap or
            // request ends.
            var reason = FileLockReason;
            if (reason != null) {
                CancelGizmo(); ClearSnap();
                selection?.Cancel();
                CancelRequest(reason);
                // A typed, unapplied Numeric draft keeps native world clicks blocked; the panel ends it and discards the
                // draft: the fields show the object as it stands, and nothing applies the draft later.
                EndWorldEditAction();
                presentation?.ClearFocusForEdit();
                ReadPlacement();
            }
            presentation?.SetFileLock(reason);
            attachment?.RefreshChrome();
            presentation?.RequestRefresh();
        }
        // Own culling's reports in the mod log: information as information, a failure as a Unity warning.
        sealed class CullingLog : ICullingLog {
            readonly IModContext context;
            internal CullingLog(IModContext context) { this.context = context; }
            public void Information(string message) => context.Log.Info(message);
            public void Warning(string message) => Debug.LogWarning("Scene Patcher: " + message);
        }
        CullingLog cullingLog;
        void Update() {
            FlushWarnings();
            if (!Initialized || engine == null) return;
            // Own culling of moved parts installs what waited for a game culler to start. What it could not take out
            // again is information for the log, not a warning.
            GroupWorld.Tick();
            if (context != null) GroupWorld.ReportCulling(cullingLog ?? (cullingLog = new CullingLog(context)));
            // Focus callbacks can arrive before the editor attachment is ready. Keep the check pending until Update
            // has attached it, and also observe the focus edge when Unity does not deliver the callback to this host.
            var focused = Application.isFocused;
            if (focused && !applicationFocused) fileCheckAfterFocus = true;
            applicationFocused = focused;
            // A request waits only while its panel is open: a closed window does no per-frame work.
            if (Requests.Pending && !(presentation != null && presentation.IsOpen)) CancelRequest("the panel was closed before the request ran");
            if (Requests.Pending) {
                var ready = inventory == null || inventory.IsStale || queuedCompared || inventory.CompletedPasses > queuedPasses;
                if (!ready) {
                    Step(EditCheckBudgetMs);
                    ready = inventory.IsStale || inventory.CompletedPasses > queuedPasses;
                }
                if (ready) {
                    // The request runs on a snapshot that was just compared: the marker capability may use that
                    // discovery's reference scan.
                    running = true;
                    try { Requests.Run(); } finally { running = false; }
                    presentation?.RequestRefresh();
                }
            }
            if (refreshFrame != 0 && Time.frameCount >= refreshFrame && !Input.GetMouseButton(0)) {
                refreshFrame = 0;
                // After an edit the inventory takes the new observations of the edited object and of what belongs with
                // it, such as the parts of a crossing and the spans the edit created or removed; every other entry stays
                // as it was compared. The first fill of the inventory, and a scene that cannot observe one object, read
                // it all.
                var edited = refreshTarget; refreshTarget = null;
                var updated = fullRefresh || inventory == null || edited == null ? null : inventory.ReobserveRelated(edited);
                fullRefresh = false;
                if (updated != null) KeepSelection(edited, updated); else Inspect();
                ReadPlacement(); presentation?.RequestRefresh();
            }
            // Undo and Redo of this patcher's own steps changed their objects: observe those again, so they do not read
            // as foreign changes. A step can destroy objects, which the game completes at the end of the frame, so the
            // observation waits two frames, as after an edit.
            if ((replayed.Count != 0 || moved.Count != 0) && replayFrame == 0) {
                replayFrame = Time.frameCount + 2; OwnChange(); inventory?.CancelFreshnessCheck();
                // Undo or Redo of this patcher's own step: "Enable completed" is no longer true.
                if (replayed.Count != 0) feedback.HistoryMoved();
            }
            if (replayFrame != 0 && Time.frameCount >= replayFrame) {
                replayFrame = 0;
                if (inventory != null) foreach (var identity in replayed.ToArray()) {
                    var entry = inventory.Entries.FirstOrDefault(e => e.Identity.Scene == identity.Scene && e.Identity.Path == identity.Path && e.Identity.Component == identity.Component);
                    var updated = entry == null ? null : inventory.ReobserveRelated(entry);
                    // An Undo or Redo of this patcher's step: the fields show the object, and an open equipment form its addition, as they are now.
                    if (updated != null) { var inspected = ReferenceEquals(selected, entry); KeepSelection(entry, updated); if (inspected) { ReadPlacement(); ReadOpenEquipment(); } }
                }
                // Objects a follow update moved: every entry of the object takes its new observation, and the fields of
                // the inspected one take its new placement.
                if (inventory != null) foreach (var handle in moved.ToArray())
                    foreach (var entry in inventory.Entries.Where(e => ReferenceEquals(e.Handle, handle)).ToArray()) {
                        if (!inventory.Entries.Any(e => ReferenceEquals(e, entry))) continue;
                        var updated = inventory.ReobserveRelated(entry);
                        if (updated != null) { var inspected = ReferenceEquals(selected, entry); KeepSelection(entry, updated); if (inspected) ReadPlacement(); }
                    }
                replayed.Clear(); moved.Clear(); presentation?.RequestRefresh();
            }
            // 1. A panel that is open before the inventory was ever read, as after an automatic open at world load,
            // fills itself once.
            if (!filled && attachment != null && inventory != null && presentation != null && presentation.IsOpen && inventory.Entries.Count == 0 && refreshFrame == 0) {
                filled = true; fullRefresh = true; refreshFrame = Time.frameCount + 30;
            }
            // Escape cancels an armed interaction and is consumed, as in the native map editor modes, so
            // it does not also open the pause menu. The gizmo also ends through the native map editor's own Escape route, which
            // can run before this handler in the same frame: an Escape in the frame a gizmo ended is consumed as well.
            // While a field of the panel has the keyboard the patcher takes no Escape at all: the handler is not even
            // registered, so the key reaches the field and the game as without the mod.
            var escapable = !(presentation != null && presentation.FieldFocused) && (selection?.Pending != null || gizmo?.Active == true || Time.frameCount <= gizmoEnded + 1 || EscapeGizmo() || EscapeSnap());
            if (escapable != escapeHeld) try {
                if (escapable) UI.GameInput.RegisterEscapeHandler(UI.GameInput.EscapeHandler.Transient, () => {
                    // One press does one thing. A list closes before a drag or choice is cancelled; over the panel
                    // a standing gizmo or prepared snap can also be cancelled. Escape never ends the inspection.
                    switch (CommandBand.Escape(presentation != null && presentation.FieldFocused, selection?.Pending?.ListOpen == true, selection?.Pending != null, gizmo?.Active == true, EscapeGizmo(), EscapeSnap())) {
                        case CommandBand.EscapeStep.CloseList: selection.Pending.CloseList(); selectionProblem = null; presentation?.RequestRefresh(); return true;
                        case CommandBand.EscapeStep.CancelDrag: gizmo.Cancel(); presentation?.RequestRefresh(); return true;
                        case CommandBand.EscapeStep.CancelChoice: CancelSelection(); presentation?.RequestRefresh(); return true;
                        case CommandBand.EscapeStep.CancelGizmo:
                            if (Requests.Place == GizmoPlace) CancelRequest("you cancelled the gizmo before the action could run");
                            CancelGizmo(); presentation?.RequestRefresh(); return true;
                        case CommandBand.EscapeStep.CancelSnap:
                            DiscardSnap(); presentation?.RequestRefresh(); return true;
                    }
                    return Time.frameCount <= gizmoEnded + 1;
                });
                else UI.GameInput.UnregisterEscapeHandler(UI.GameInput.EscapeHandler.Transient);
                escapeHeld = escapable;
            } catch (Exception e) { escapeHeld = escapable; context?.Log.Info("Escape handling for the world choice and the gizmo is unavailable: " + e.GetBaseException().Message); }
            try { Attach(); }
            catch (Exception e) { editingStatus = "Editing unavailable: " + e.GetBaseException().Message; }
            if (fileCheckAfterFocus && applicationFocused && attachment != null) {
                fileCheckAfterFocus = false;
                CheckFileVersion();
            }
            if (attachment != null && Time.unscaledTime >= nextFileVersionCheck) {
                nextFileVersionCheck = Time.unscaledTime + 2f;
                CheckFileVersion();
            }
            MapEditorReload.Poll(attachment);
            if (attachment != null && Time.unscaledTime >= nextReloadCheck) {
                reloadRequirement = attachment.Session.ReloadRequirement;
                nextReloadCheck = Time.unscaledTime + 1f;
            }
            presentation?.SetEntryStatus(reloadRequirement == null ? "Scene patches" : "Scene patches: reload",
                reloadRequirement ?? "Open scene-patch inspection and editing.");
            bool open = presentation != null && presentation.IsOpen;
            if (!open) CancelGizmo();
            SyncInputRegions();
            if (open && selection?.Pending != null && inputBlocker?.WorldClick is Vector2 pointer) {
                try { selection.Pick(pointer, presentation.WorldUiBounds()); selectionProblem = null; }
                catch (Exception e) {
                    selection.Pending.Offer(Array.Empty<SelectionCandidate>());
                    selectionProblem = "World selection unavailable: " + e.GetBaseException().Message;
                }
            }
            if (inMapEditor && open && inventory != null) {
                // The idle comparison only informs the band; an edit runs its own pass. It takes a small step on every
                // third frame and pauses five seconds after a pass. A change of the native map editor's history that is not this
                // patcher's own is the usual cause of a stale inventory, so it starts a pass at once, at the edit's pace.
                var key = ChangeStamp();
                // A history that moved without this patcher's own edit is a native edit, Undo or Redo: what the last
                // action completed may no longer hold, so its result goes.
                if (key != idleKey) { idleKey = key; urgentCheck = true; nextInventoryCheck = 0; feedback.HistoryMoved(); }
                feedback.FlushHistoryNotice();
                // No pass runs between this patcher's own change and the observation of the changed objects: it would
                // compare them with what they were before.
                // While a gizmo is active the placement of its target is this patcher's own preview: a pass would compare it
                // with the snapshot and mark the inventory stale. The pass stops when the gizmo starts and waits until
                // it ended and its target was observed again, after a release and after a cancellation alike.
                if (gizmo?.Active == true) inventory.CancelFreshnessCheck();
                if (!Requests.Pending && gizmo?.Active != true && refreshFrame == 0 && replayFrame == 0 && (inventory.CheckInProgress || Time.unscaledTime >= nextInventoryCheck) && (urgentCheck || Time.frameCount % 3 == 0)) {
                    Step(urgentCheck ? EditCheckBudgetMs : InventoryCheckBudgetMs);
                    if (!inventory.CheckInProgress) { nextInventoryCheck = Time.unscaledTime + 5f; urgentCheck = false; }
                }
                if (Time.unscaledTime >= nextDefinitionCheck) {
                    nextDefinitionCheck = Time.unscaledTime + 2f;
                    try { definitions.Compare(ModScenePatchFiles.Discover()); }
                    catch { definitions.Unreadable(); }
                }
            } else inventory?.CancelFreshnessCheck();
            if (inventory != null && inventory.IsStale != staleLogged) {
                staleLogged = inventory.IsStale;
                if (staleLogged) context?.Log.Info("Scene inventory marked stale: " + inventory.StaleReason);
            }
            FollowTracks();
            // Every move of the native map editor's history can change the surface the frames are cut at: a placement edit with
            // its terrain, a native terrain or track edit, Undo and Redo. A terrain output that completes later
            // clears it again where it is reported.
            var surfaceKey = ChangeStamp();
            if (surfaceKey != surfaceStamp) { surfaceStamp = surfaceKey; selection?.ClearSurface(); }
            if (scene != null) try {
                scene.Poll();
                foreach (var failure in scene.Failures)
                    Report("terrain/" + world.Label(failure.Key) + "/" + failure.Value.Error, "Terrain output for " + world.Label(failure.Key) + " did not complete: " + failure.Value.Error + ". Completed: " + Join(failure.Value.Completed) + ". Outstanding: " + Join(failure.Value.Outstanding) + ". The board keeps its applied placement; reloading the map regenerates the output. Report to the patcher maintainer.");
            } catch (Exception e) { Report("terrain", "Dependent terrain state remains unverified. Reload before retry. Report to the patcher maintainer. " + e.GetBaseException().Message); }
            if (presentation != null) {
                // A closed window shows nothing: it computes no snapshot. Opening builds the content anew.
                if (open) {
                    TrackRecovery();
            var snapshot = PresentationSnapshot();
                    feedback.Inspecting(selected?.Handle);
                    MarkFollowed();
                    MarkBound();
                    ArmGizmo();
                    ShowLivePlacement();
                    ShowLiveGizmoDrag();
                    LogChoiceProblem();
                    // A result belongs to the tab and placement mode it was reported in.
                    feedback.View(guided.Enabled ? GuidedFeedbackView() : InspectorFeedbackView());
                    // The help of a tab belongs to that tab: it closes when another tab opens.
                    if (CommandBand.ClosesHelp(presentation.HelpRegion, inspectorTab.Value, TabIds)) presentation.CloseHelp();
                    var inventoryChanged = inventoryPresentation.Observe(inventory, selected, filter, outlinerFilter, ChangeStamp());
                    if (snapshot != presentationSnapshot || inventoryChanged) { presentationSnapshot = snapshot; presentation.RequestRefresh(); }
                }
                presentation.Tick(); SyncInputRegions();
            }
        }
        // Objects of the edited scene patch file that follow a track move with it, a few times a second. No scene patch file or history changes.
        string surfaceStamp;
        void FollowTracks() {
            if (attachment == null || gizmo?.Active == true || Time.unscaledTime < nextTrackFollow) return;
            nextTrackFollow = Time.unscaledTime + 0.25f;
            try {
                if (!attachment.Session.HasTrackFollowing) return;
                foreach (var item in attachment.Session.ReconcileTrackFollowing())
                    if (item.Value.Applied) {
                        context?.Log.Info("Scene Patcher: " + item.Key + " followed its track");
                        // The move is this patcher's own: the object is observed again, as after Undo of its own step,
                        // so the comparison does not read its new placement as a foreign change.
                        // A follow outcome names the object it moved, not an identity.
                        if (item.Value.ResolvedTarget != null && !moved.Any(known => ReferenceEquals(known, item.Value.ResolvedTarget))) moved.Add(item.Value.ResolvedTarget);
                        // A track moved: the ballast is elsewhere.
                        selection?.ClearSurface();
                    }
                    else if (reported.Add("follow/" + item.Key + "/" + item.Value.Error)) {
                        diagnostics.Add(item.Key + " could not follow its track: " + item.Value.Error);
                        context?.Log.Info("Scene Patcher: " + item.Key + " could not follow its track: " + item.Value.Error);
                    }
            } catch (Exception e) { if (reported.Add("follow/" + e.GetBaseException().Message)) context?.Log.Info("Scene Patcher: track following stopped: " + e.GetBaseException().Message); }
        }
        // Differs whenever the set of objects the scene patch file changes can differ.
        string ChangeStamp() { try { return attachment?.ChangeKey; } catch { return null; } }
        string PresentationSnapshot() {
            var pending = selection?.Pending;
            string outstanding = "";
            try {
                if (attachment != null) {
                    var session = attachment.Session;
                    outstanding = OutstandingSnapshot() + "|" +
                        session.LastSave?.Error + "|" + (session.LastSave == null ? "" : String.Join(", ", session.LastSave.Completed)) + "|" +
                        (session.LastSave == null ? "" : String.Join(", ", session.LastSave.Outstanding)) + "|" +
                        session.IncompleteOperation + "|" + attachment.Fault;
                }
            } catch (Exception e) { outstanding = e.GetBaseException().Message; }
            return String.Join("|", guided.Enabled, guided.Step, guided.Kind, guided.Message, guided.Recovery, inspectorTab.Value, presentation?.HelpRegion, candidateDetails, gizmoTool, Requests.Place, status, scene?.Status, inventory?.IsStale, inventory?.StaleReason, definitionsStale, editingStatus, feedback.Place, feedback.Result,
                scenePatchFileInspection, String.Join("\n", diagnostics), selectionProblem, inputBlocker?.WorldInputProblem,
                pending?.Purpose, pending?.Source?.Label, pending?.Problem, pending?.Candidate?.Label,
                pending == null ? "" : String.Join("\n", pending.Offers.Select(o => o.Label)),
                selection?.Confirmed?.Identity, preparedSnap?.Identity, choosingSnap, gizmo?.State, outstanding, reloadRequirement, MapEditorReload.Error, TerrainSnapshot(),
                coverageCircuit, preparedCoverage == null ? "" : String.Join("\n", preparedCoverage.Preview.Lines) + preparedCoverage.Preview.Error, RecoverySnapshot(), attachment?.Session.VersionState, attachment?.Session.VersionMessage, ImportSnapshot(),
                equipmentSource, preparedEquipment == null ? "" : String.Join("\n", preparedEquipment.Preview.Lines) + preparedEquipment.Preview.Error);
        }
        // Computing the outstanding work serializes the scene patch file and reads the dependency metadata. Its inputs change
        // with the native map editor's change counts, a Save attempt or an incomplete operation; one recomputation a second covers
        // a change that passes none of them.
        string outstandingKey, outstandingText;
        object outstandingOwner;
        float outstandingUntil;
        string OutstandingSnapshot() {
            var session = attachment.Session;
            var key = attachment.ChangeKey + "|" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(session.LastSave) + "|" + session.IncompleteOperation + "|" + session.Blocked;
            if (!ReferenceEquals(outstandingOwner, attachment) || key != outstandingKey || Time.unscaledTime >= outstandingUntil) {
                outstandingText = String.Join(", ", session.Outstanding(attachment.NativeDirty));
                outstandingOwner = attachment; outstandingKey = key; outstandingUntil = Time.unscaledTime + 1f;
            }
            return outstandingText;
        }
        readonly InspectedOutput inspectedOutput = new InspectedOutput();
        object terrainFor;
        string terrainState;
        string TerrainSnapshot() {
            if (attachment == null || selected == null || !Editable) { terrainFor = null; return ""; }
            var output = inspectedOutput.Of(attachment.Session, selected, out var problem);
            // A change of the inspected object's terrain output is reported where its Retry control is: the message area.
            var state = problem != null ? "unknown" : output.Error != null ? "stopped" : output.Outstanding.Count != 0 ? "outstanding" : output.Completed.Count != 0 ? "complete" : "none";
            if (!ReferenceEquals(terrainFor, selected.Handle)) { terrainFor = selected.Handle; terrainState = state; }
            else if (state != terrainState) {
                terrainState = state;
                if (state == "outstanding") feedback.Follow(TerrainPlace, "Terrain pending: the game is regenerating it. Retry is on the Changes tab.");
                else if (state == "complete") { feedback.Follow(TerrainPlace, "Terrain updated."); selection?.ClearSurface(); }
                else if (state == "stopped") feedback.Follow(TerrainPlace, "Terrain update stopped: " + output.Error + ". Select Retry on the Changes tab.");
            }
            return problem ?? String.Join("|", String.Join(";", output.Completed), String.Join(";", output.Outstanding), output.Error);
        }
        // Starts a new load attempt over the retained world. The engine keeps the captured state and guards of objects that
        // still stand under their identities and releases the rest; diagnostics, overlap notices and scene patch editing views start over.
        internal void BeginAttempt() {
            if (engine == null) return;
            EndAttempt();
            var reentry = engine.Reenter();
            runtime = new PatchRuntime(engine, nativeDiagnostics, Version, Application.version);
            overlapNotices = null;
            // Warnings not yet delivered belong to the previous load attempt.
            reported.Clear(); diagnostics.Clear(); editedDiagnostics.Clear(); pendingWarnings.Clear(); pendingNotices.Clear();
            loadId = Guid.NewGuid().ToString("N"); presentationErrorLogged = false;
            if (inventory != null) { inventory = new SceneInventory(native); staleLogged = true; }
            selected = null; selectionProblem = null; scenePatchFileInspection = "";
            // The panel's state belongs to the load attempt as well: what it read, compared, reported and had open. The
            // next native map editor session builds a new window and fills it from the scene as it is then.
            definitions.Reset();
            comparedKey = null; passKey = null; idleKey = null; urgentCheck = false; filled = false; fullRefresh = false;
            terrainFor = null; revealed = null;
            CloseCoverageForm(); CloseEquipmentForm();
            feedback.Clear();
            status = "Load attempt over the retained world";
            context?.Log.Info("Scene Patcher " + Version + ": load attempt over the retained world; " + reentry.Retained + " objects retained, " + reentry.Released + " released, " + reentry.Failures.Count + " cleanup failures");
            // Local only: no other peer is told, rolled back or disconnected.
            foreach (var failure in reentry.Failures)
                Report("reentry/" + failure, "Retained world cleanup is incomplete on this peer: " + failure + ". The patcher retries at the next load; reload the map to clear it. Report to the patcher maintainer.");
        }
        // Ends the load attempt: parts bound to the native map editor of this world end with it; the world's patcher state stays.
        // The window, its entry icon and its title-bar help button are disposed here and built again by Attach when the
        // next native map editor appears. Requests that waited on the scene of this attempt end with it.
        internal void EndAttempt() {
            presentation?.Dispose(); presentation = null; attachedMapEditor = null; refusedMapEditor = null; presentationSnapshot = null;
            Detach(); selection?.Cancel();
            Requests.Cancel("you left the Map Editor before the request ran"); refreshFrame = 0; replayFrame = 0; refreshTarget = null;
            replayed.Clear(); moved.Clear();
            inventory?.CancelFreshnessCheck();
            if (escapeHeld) { try { UI.GameInput.UnregisterEscapeHandler(UI.GameInput.EscapeHandler.Transient); } catch { } escapeHeld = false; }
        }
        internal void Release() { ReleaseParts(); }
        void ReleaseParts() {
            if (worldLabelBacking != null) Destroy(worldLabelBacking); worldLabelBacking = null;
            presentation?.Dispose(); presentation = null; attachedMapEditor = null;
            Detach(); inputBlocker?.Release(); inputBlocker = null; selection?.Release(); selection = null;
            // Ending the capabilities returns patched crossings to their native coverage when the scene stays.
            capabilities?.Dispose();
            scene?.Dispose(); scene = null; world?.Dispose(); world = null; capabilities = null; crossingCoverageScene = null; engine = null; inventory = null; native = null; selected = null;
            runtime = null; markerWorld = null; nativeDiagnostics = null; pendingWarnings.Clear(); pendingNotices.Clear(); overlapNotices = null;
        }
        static string Join(IReadOnlyList<string> items) => items.Count == 0 ? "none" : String.Join("; ", items);
        void OnDestroy() { Release(); }
        void OnGUI() {
            if (presentation == null || !presentation.IsOpen || selection == null || Event.current.type != EventType.Repaint) return;
            try {
                if (worldLabelBacking == null) {
                    worldLabelBacking = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    worldLabelBacking.SetPixel(0, 0, new Color(.106f, .098f, .086f, 1)); worldLabelBacking.Apply();
                }
                selection.Draw(selected, presentation.WorldUiBounds(), worldLabelBacking, InspectedTrackPoint(), InspectedCut());
            }
            catch (Exception e) { selectionProblem = "World markings unavailable: " + e.GetBaseException().Message; }
        }
        void BeginSelection(SelectionKind kind, SceneObservation origin, int? coverageRole = null) {
            if (selection?.Pending != null) return;
            CancelGizmo();
            ClearSnap();
            // A choice without a source object only inspects what it confirms; no scene patch file names it.
            try {
                var excluded = coverageRole.HasValue ? crossingCoverageScene?.RoleSegment(origin.Handle, coverageCircuit, coverageRole.Value) : null;
                var purpose = coverageRole.HasValue ? "Select track segment for " + CircuitCoverage.RoleCaption(CircuitCoverage.Roles[coverageRole.Value]) : null;
                selection.Begin(kind, origin, origin == null && kind == SelectionKind.SceneObject, excluded, purpose);
                choosingCoverageRole = coverageRole;
                selectionProblem = null;
                if (selection.Pending?.InspectOnly == false) BeginWorldEditAction();
                // The refusal of an earlier action, such as a Set, does not stand beside the new choice.
                if (selection.Pending != null) feedback.ChoiceStarted();
            }
            catch (Exception e) { selectionProblem = "World selection unavailable: " + e.GetBaseException().Message; }
        }
        // Over the panel Escape can disarm a standing or queued tool as well as cancel a world choice or drag.
        bool EscapeGizmo() => PointerOverPanel() && (gizmo?.Shown == true || Requests.Place == GizmoPlace);
        bool EscapeSnap() => PointerOverPanel() && (preparedSnap != null || Requests.Place == SnapPlace);
        bool PointerOverPanel() {
            if (presentation == null || !presentation.IsOpen) return false;
            var pointer = Input.mousePosition;
            return presentation.PanelBounds.Contains(new Vector2(pointer.x, Screen.height - pointer.y));
        }
        // Nothing is inspected any more: what was prepared, open or reported for the object goes with it. A pending
        // world choice is cancelled as Cancel does, so the native map editor takes the world's clicks again. A request that
        // still waited is refused, and that refusal stays to be read.
        void ClearInspection() {
            if (guided.Enabled && (guided.Step == GuidedTaskStep.Values || guided.Step == GuidedTaskStep.Review) && selection?.Pending != null) {
                CancelSelection();
                presentation?.RequestRefresh();
                return;
            }
            if (!GuidedSelect(null)) return;
            CancelGuided();
            var waited = Requests.ReleaseInspection(selection?.Pending != null, CancelSelection,
                () => { selection?.Clear(); selectionProblem = null; CancelGizmo(); ClearSnap(); CloseCoverageForm(); CloseEquipmentForm(); selected = null; },
                "the inspection was cleared before the request ran");
            if (waited) inventory?.CancelFreshnessCheck();
            presentation?.RequestRefresh();
        }
        // The world marks the track the inspected object follows in green, as a confirmed relation, also while another
        // track is being chosen. The followed track is read from the scene patch file when the inspection or the native map editor's
        // history changes, not every frame.
        object followedOf, modeOf; string followedKey;
        void MarkFollowed() {
            var key = selected == null ? null : ChangeStamp();
            if (ReferenceEquals(followedOf, selected?.Handle) && key == followedKey) return;
            followedOf = selected?.Handle; followedKey = key;
            TrackFollowing following = null;
            try { if (selected != null && attachment != null) following = attachment.Session.FollowingOf(selected.Identity); } catch { }
            // An object opens in its own placement mode: Follow for one that follows a track, Numeric otherwise.
            if (selected != null && !ReferenceEquals(modeOf, selected.Handle)) { modeOf = selected.Handle; placementMode = CommandBand.PlacementMode(following != null); }
            if (selected == null) modeOf = null;
            selection?.Follows(selected?.Handle, CommandBand.Followed(following));
        }
        // While the Connections tab is open the world marks what the inspected object is bound to now in green, native
        // or set by a patch: the segment of its marker binding, and for a crossing the tracks of its circuits. Read
        // when the inspection, the tab or the native map editor's history changes, not every frame.
        string boundKey;
        SelectionIdentity[] BoundIdentities(SceneObservation inspected) {
            try {
                var segments = inspected.Identity.Structure == CrossingCoverageScene.Structure
                    ? crossingCoverageScene?.CoveredSegments(inspected.Handle) ?? Array.Empty<string>()
                    : Segments(inspected);
                return segments.Where(id => !String.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
                    .Select(id => SelectionIdentity.Graph(SelectionKind.TrackSegment, id)).ToArray();
            } catch { }
            return Array.Empty<SelectionIdentity>();
        }
        void MarkBound() {
            if (selection == null) return;
            var shown = selected != null && inspectorTab.Value == ConnectionsTab;
            var key = shown ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(selected) + "|" + ChangeStamp() : null;
            if (key == boundKey) return;
            boundKey = key;
            selection.Binds(shown ? selected.Handle : null, shown ? BoundIdentities(selected) : Array.Empty<SelectionIdentity>());
        }
        // The track segments an observation's binding lines name: a marker's segment, a span's lower and upper.
        static IEnumerable<string> Segments(SceneObservation entry) {
            foreach (var line in entry.Bindings) {
                var at = line.IndexOf(" segment=", StringComparison.Ordinal);
                if (at < 0 || !(line.StartsWith("Marker segment=", StringComparison.Ordinal) || line.StartsWith("Lower segment=", StringComparison.Ordinal) || line.StartsWith("Upper segment=", StringComparison.Ordinal))) continue;
                var value = line.Substring(at + " segment=".Length);
                var end = value.IndexOf(';');
                yield return (end < 0 ? value : value.Substring(0, end)).Trim();
            }
        }
        // The gizmo stands on the inspected object for as long as the Gizmo mode is open: it is shown as soon as the mode
        // is chosen, comes back after every completed or cancelled drag, and goes when the mode, the tab or the
        // inspection ends or a world choice starts. It is bound to the inspection it was shown for; when the object is
        // observed again or the native map editor's history moves, it is shown again for the new inspection. A start that was
        // refused is not tried again until something changes.
        string gizmoArmKey;
        void ArmGizmo() {
            if (gizmo != null && gizmo.Closed) gizmo = null;
            var wanted = !guided.Enabled && selected != null && inspectorTab.Value == PlacementTab && placementMode == CommandBand.GizmoMode && !IsPad && selection?.Pending == null && Editable && attachment != null && (GameObject)selected.Handle != null;
            if (!wanted) {
                if (gizmo != null && !gizmo.Active) CancelGizmo();
                gizmoArmKey = null;
                return;
            }
            var key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(selected) + "|" + ChangeStamp() + "|" + (inventory?.IsStale);
            if (gizmo != null) {
                if (gizmo.Active) return;
                if (key == gizmoArmKey) {
                    // A held gizmo whose request ended without a new inspection, as when it was refused, goes.
                    if (gizmo.Holding && !Requests.Pending) CancelGizmo();
                    return;
                }
                // The same object changed under the gizmo, as by a native Undo or Redo: the gizmo stays shown on it
                // and is bound anew once the object was observed again. Another object gets a gizmo of its own.
                if (ReferenceEquals(gizmoHandle, selected.Handle)) gizmo.Hold(); else CancelGizmo();
            } else if (key == gizmoArmKey) return;
            if (Requests.Pending || refreshFrame != 0 || replayFrame != 0) return;
            gizmoArmKey = key; gizmoHandle = selected.Handle;
            BeginGizmo(gizmoTool);
        }
        // The read-only placement of the Gizmo mode follows the gizmo while it is dragged: the texts are set in place,
        // because the panel does not rebuild while a pointer button is down.
        TMPro.TMP_Text[] gizmoPlacement;
        void ShowLivePlacement() {
            if (gizmoPlacement == null || selected == null || placementMode != CommandBand.GizmoMode) return;
            string[] values = null;
            try {
                if (gizmo != null && gizmo.TryPlacement(out var map, out var rotation)) { var angles = rotation.eulerAngles; values = new[] { map.x, map.y, map.z, angles.x, angles.y, angles.z }.Select(v => CommandBand.Number(v)).ToArray(); }
                else if (selected.Position.Count == 3 && selected.Rotation.Count == 4) {
                    var angles = new Quaternion((float)selected.Rotation[0], (float)selected.Rotation[1], (float)selected.Rotation[2], (float)selected.Rotation[3]).eulerAngles;
                    values = CurrentPlacement();
                }
            } catch { }
            if (values == null) return;
            // One running line for the position and one for the rotation, as every placement that cannot be edited reads.
            for (var i = 0; i < gizmoPlacement.Length && i < 2; i++) {
                var text = CommandBand.RunningLine(CommandBand.Axes, values.Skip(i * 3).Take(3).ToArray(), CommandBand.PlacementUnits[i]);
                if (gizmoPlacement[i] != null && gizmoPlacement[i].text != text) gizmoPlacement[i].text = text;
            }
        }
        // A world choice's refusal or problem goes to the log once, with the object it was for.
        string loggedChoiceProblem;
        void LogChoiceProblem() {
            var pending = selection?.Pending;
            var problem = pending?.Problem ?? selectionProblem;
            // The same problem at another spot is another line: the candidates are part of what is logged.
            var offers = pending == null ? null : pending.Offers.Select(offer => offer.Label).ToArray();
            // The line names the object the choice is for, and none for a choice that only inspects: the object
            // inspected before has nothing to do with the spot that was clicked.
            var forObject = pending?.Source == null ? null : selected?.Identity.Path;
            var line = problem == null ? null : CommandBand.ChoiceLog(forObject, problem, offers);
            if (line == loggedChoiceProblem) return;
            loggedChoiceProblem = line;
            if (line != null) context?.Log.Info(line);
        }
        void ResetLayout() { outlinerWidth = OutlinerDefaultWidth; ForgetOutlinerWidth(); presentation?.ResetLayout(); }

        void ConfirmSelection() {
            var snapping = choosingSnap;
            try {
                var inspectOnly = selection.Pending?.InspectOnly == true;
                var candidate = selection.Confirm();
                // A refused confirmation says why in the footer, beside Confirm, and in the log (LogChoiceProblem).
                if (candidate == null) { presentation?.RequestRefresh(); return; }
                if (!inspectOnly) feedback.ChoiceConfirmed(attachment?.Session);
                pickedTrackLocation = null; pickedTrackSelection = null;
                selectionProblem = null;
                if (snapping) {
                    // Preparing the snap binds it to the inspected object: queued like an edit, so no scan blocks.
                    var identity = selected.Identity; var confirmed = selection.Confirmed; var choice = candidate.Choice;
                    choosingSnap = false;
                    Queue(SnapPlace, "Snap target", pin => {
                        try { preparedSnap = snap.Prepare(identity, pin(selected), confirmed, choice); }
                        catch (Exception e) {
                            EndWorldEditAction();
                            feedback.Refuse(SnapPlace, "Snap target", e.GetBaseException().Message);
                        }
                    }, EndWorldEditAction);
                }
                if (inspectOnly && candidate.Observation != null) {
                    InspectObservation(candidate.Observation);
                    ReadPlacement();
                }
                // Marker, pad and starter placement fields all take the confirmed click's complete track location.
                // A crossing's segment pick fills the role that started the choice.
                var picked = selection.Confirmed;
                if (choosingCoverageRole is int role) {
                    if (coverageCircuit != null && selected != null && picked?.Source?.Equals(SelectionIdentity.SceneObject(selected.Identity)) == true) {
                        coverageFields[role * 4] = picked.Identity.Identifier;
                        preparedCoverage = null;
                    }
                    choosingCoverageRole = null;
                }
                var fillsBinding = !snapping && picked?.Identity.Kind == SelectionKind.TrackSegment && inspectorTab.Value == ConnectionsTab &&
                    selected != null && (IsStarter || selected.Identity.Structure == TrackMarkerScene.Structure || selected.Identity.Structure == CrossingPadScene.Structure || selected.Identity.Structure == MilepostScene.Structure) &&
                    picked.Source != null && picked.Source.Equals(SelectionIdentity.SceneObject(selected.Identity));
                if (fillsBinding) {
                    pickedTrackSelection = picked;
                    binding[0] = picked.Identity.Identifier;
                    PlaceAtClick(candidate, picked);
                }
                if (inspectorTab.Value == ConnectionsTab && !fillsBinding) EndWorldEditAction();
            } catch (Exception e) {
                if (snapping && selection.Pending == null) EndWorldEditAction();
                selection.Pending?.Offer(Array.Empty<SelectionCandidate>());
                selectionProblem = "Identity could not be confirmed: " + e.GetBaseException().Message;
            }
        }
        // Fills a marker's complete track location from where the confirmed segment was clicked. The segment is
        // confirmed either way; a click point that cannot be read leaves end and distance as they were and says why, so
        // they can be entered by hand.
        void PlaceAtClick(SpatialCandidate candidate, ConfirmedSelection picked) {
            const string action = "Track location from the click";
            if (trackPoses == null) { feedback.Refuse(BindingPlace, action, "the track graph is unavailable in this game build, so the click cannot give a distance; enter end and distance by hand"); return; }
            try {
                var point = selection.ClickedPoint(candidate);
                if (point == null) { feedback.Refuse(BindingPlace, action, "the click on the track could not be found on the segment's curve; enter end and distance by hand"); return; }
                var offset = (Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "GameToWorld", Vector3.zero);
                var at = trackPoses.Nearest(picked.Identity.Identifier, point.Value + offset);
                if (!EnteredEditValues.FillTrackLocation(binding, at)) throw new InvalidOperationException("Enter the segment ID, end a or b, and a distance. Use a point for decimals.");
                pickedTrackLocation = at;
            } catch (Exception e) { feedback.Refuse(BindingPlace, action, e.GetBaseException().Message + "; enter end and distance by hand"); }
        }
        // Editing needs a mutation adapter; the inventory marks each supported pattern with its structure.
        bool Editable => selected != null && (selected.Identity.Structure == IndependentBoards.Structure || selected.Identity.Structure == BridgeScene.Structure || selected.Identity.Structure == TrackMarkerScene.Structure || selected.Identity.Structure == MilepostScene.Structure || selected.Identity.Structure == CrossingCoverageScene.Structure || selected.Identity.Structure == CrossingPadScene.Structure || selected.Identity.Structure == BakedSceneryGroups.Structure || selected.Identity.Structure == BakedSceneryParts.Structure) && selected.Handle is GameObject;
        // Why the inspected object cannot be edited now, or null. The band holds the pending selection and the active gizmo.
        // A starter placement marker is edited on the Connections tab only, where it is placed on the track; on every other
        // tab its object's placement and activity stay disabled with the reason.
        string EditBlock() =>
            attachment == null ? (String.IsNullOrEmpty(editingStatus) ? "Editing is unavailable." : editingStatus)
            : FileLockReason != null ? FileLockReason
            : attachment.Session.Blocked != null ? attachment.Session.Blocked + " The recovery choices are in the session details."
            : selection?.Pending != null ? "Confirm or cancel the world selection to edit."
            : IsStarter ? StarterBlock() ?? (inspectorTab.Value == ConnectionsTab ? null : CommandBand.EditReason(CommandBand.NotEditable(StarterPlacements.ObjectSettings)))
            : !Editable ? CommandBand.EditReason(selected?.Operations)
            : gizmo?.Active == true ? "Complete or cancel the gizmo to edit."
            : (GameObject)selected.Handle == null ? "The inspected object no longer exists. Refresh the object list." : null;
        // Why the inspected starter placement marker's modification cannot be changed now, whatever the tab, or null: what
        // Remove modification and Recheck in the footer, and the track location on the Connections tab, wait for.
        string StarterBlock() =>
            attachment == null ? (String.IsNullOrEmpty(editingStatus) ? "Editing is unavailable." : editingStatus)
            : FileLockReason != null ? FileLockReason
            : selection?.Pending != null ? "Confirm or cancel the world selection to edit."
            : gizmo?.Active == true ? "Complete or cancel the gizmo to edit."
            : !(selected?.Handle is GameObject live) || live == null ? "The inspected object no longer exists. Refresh the object list." : null;
        void ResetSnapState() { preparedSnap = null; choosingSnap = false; }
        void ResetConnectionsPreview() { choosingCoverageRole = null; preparedCoverage = null; preparedEquipment = null; }
        void ClearSnap() { ResetSnapState(); ResetConnectionsPreview(); EndWorldEditAction(); }
        void DiscardSnap() {
            if (Requests.Place == SnapPlace) CancelRequest("you cancelled the snap before the action could run");
            if (choosingSnap) { selection?.Cancel(); selectionProblem = null; }
            selection?.Consume();
            ResetSnapState();
            EndWorldEditAction();
        }
        void ChangePlacementMode(int mode) {
            if (placementMode == mode) return;
            // Leaving Snap drops only its own pick and preview; a coverage or equipment preview on Connections stays.
            if (placementMode == CommandBand.SnapMode && (choosingSnap || preparedSnap != null || Requests.Place == SnapPlace)) DiscardSnap();
            placementMode = mode;
        }
        void BeginSnap(SelectionKind kind) {
            BeginSelection(kind, selected);
            choosingSnap = selection?.Pending != null;
        }
        void ReadPlacement() {
            var current = CurrentPlacement();
            if (current == null) return;
            for (var i = 0; i < 6; i++) placementFields[i] = current[i];
            ReadBinding();
        }
        void ReadNumericPlacement() {
            EndWorldEditAction();
            presentation?.ClearFocusForEdit();
            ReadPlacement();
        }
        void CancelPickedBinding() {
            pickedTrackLocation = null;
            pickedTrackSelection = null;
            selection?.Consume();
            ReadBinding();
            EndWorldEditAction();
        }
        bool HasPickedBinding => pickedTrackSelection != null && ReferenceEquals(pickedTrackSelection, selection?.Confirmed);
        // The inspected placement as the numeric fields show it, or null without one.
        string[] CurrentPlacement() {
            var values = PlacementValues();
            return values == null ? null : values.Select(CommandBand.Number).ToArray();
        }
        // The inspected position and Euler angles as numbers, or null without a placement.
        double[] PlacementValues() {
            if (selected == null || selected.Position.Count != 3 || selected.Rotation.Count != 4) return null;
            var angles = new Quaternion((float)selected.Rotation[0], (float)selected.Rotation[1], (float)selected.Rotation[2], (float)selected.Rotation[3]).eulerAngles;
            return new[] { selected.Position[0], selected.Position[1], selected.Position[2], angles.x, angles.y, angles.z };
        }
        // Fills the binding fields from the inspected marker location.
        void ReadBinding() { var current = CurrentBinding(); for (var i = 0; i < 3; i++) binding[i] = current[i]; }
        // The inspected marker location as the binding fields show it; an empty segment for a detached marker.
        string[] CurrentBinding() {
            var fields = new[] { "", "a", "" };
            var line = selected?.Bindings.FirstOrDefault(b => b.StartsWith("Marker segment=", StringComparison.Ordinal));
            if (line == null) return fields;
            foreach (var part in line.Substring("Marker ".Length).Split(new[] { "; " }, StringSplitOptions.None)) {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2) continue;
                if (pair[0] == "segment") fields[0] = pair[1];
                else if (pair[0] == "end") fields[1] = CommandBand.Shown("end", pair[1]);
                else if (pair[0] == "distance") fields[2] = CommandBand.Shown("distance", pair[1]);
            }
            return fields;
        }
        // Every edit is bound to the inspected observation. A stale inventory refuses the edit instead of redirecting it.
        // An edit that does not run names its reason in the panel and the log, like one that is refused.
        // consumes says the edit uses the target confirmed in the world: once it completed, that confirmation is used
        // up, and its "Confirmed" line leaves the message area.
        // guarded says the edit is refused while an earlier failure guards the object; Recheck, which ends that guard, is not.
        void Edit(string place, string action, Func<PatchEditingSession, InspectedTarget, Outcome> change, bool consumes = false, Action<bool> completed = null, bool guarded = true) {
            EndWorldEditAction();
            presentation?.ClearFocusForEdit();
            Queue(place, action, pin => {
                // A completed edit changed the observed values; observe them again so the next edit binds to them.
                var truthful = IsPad && (place == PlacementPlace || place == SnapPlace || place == FollowPlace) ? CommandBand.PadPlacement(action) : null;
                var target = selected?.Identity;
                Func<Outcome> guard = guarded && target != null ? () => attachment.Session.GuardOutcome(target) : (Func<Outcome>)null;
                var applied = feedback.Run(place, action, null, inventory, selected, inspection => change(attachment.Session, inspection), pin, truthful, guard);
                if (applied) {
                    if (consumes) selection?.Consume();
                    OwnChange(); RefreshAfterEdit();
                }
                completed?.Invoke(applied);
            }, () => completed?.Invoke(false));
        }
        // Runs an edit or a preview for the object that is inspected now, once the snapshot is known to agree with the
        // scene. When it was compared since the last foreign change, that is the next frame. Otherwise the comparison
        // of the whole scene runs first as a stepped pass, a few milliseconds a frame, so the game keeps running, and
        // the message area says "Working..." meanwhile. A pass that finds a difference marks the inventory stale, and
        // the request is refused with that reason. Either way the pin it gets observes the target at every validation,
        // so a change of the target up to the mutation is refused.
        void Queue(string place, string action, Action<Func<SceneObservation, InspectedTarget>> run, Action refused = null) {
            void Refuse(string reason) { feedback.Refuse(place, action, reason); refused?.Invoke(); }
            var blocked = selection?.Pending != null ? "a world selection is pending; confirm or cancel it first"
                : gizmo?.Active == true ? "the gizmo is active; complete or cancel it first"
                : attachment == null ? "editing is unavailable" : Requests.Pending ? "another edit is still running" : null;
            if (blocked != null) { feedback.Run(place, action, blocked, inventory, selected, null); refused?.Invoke(); return; }
            ResetSnapState();
            if (CommandBand.ClearsConnectionsPreviewOnQueue(place)) ResetConnectionsPreview();
            var inspected = selected;
            queuedCompared = Compared();
            if (!queuedCompared) inventory?.CancelFreshnessCheck();
            queuedPasses = inventory == null ? 0 : inventory.CompletedPasses;
            queuedKey = ChangeStamp();
            Requests.Enqueue(place, action, () => {
                if (!ReferenceEquals(inspected, selected)) { Refuse("you inspected another object before the action could run"); return; }
                if (ChangeStamp() != queuedKey) { Refuse("the Map Editor's history changed during the scene check. Select the action again"); return; }
                if (inventory == null) { Refuse("Inspection is unavailable"); return; }
                var passes = queuedPasses; var compared = queuedCompared;
                run(entry => compared ? inventory.PinCompared(entry) : inventory.PinAfterPass(entry, passes, true));
            });
        }
        // The preview compares the scene as an edit does: without a blocking scan.
        void PrepareCoverage(PatchEditingSession session, TargetIdentity identity, string circuit, CircuitCoverage value, string addition) {
            Queue(CoveragePlace, "Coverage preview", pin => {
                CoveragePreview preview;
                try { preview = addition == null ? session.PreviewCoverage(identity, circuit, value, pin(selected)) : session.PreviewAddition(identity, addition, value, pin(selected)); }
                catch (Exception e) { feedback.Refuse(CoveragePlace, "Coverage preview", e.GetBaseException().Message); preparedCoverage = null; return; }
                preparedCoverage = new PreparedCoverage { Circuit = circuit, Addition = addition, Value = value, Preview = preview };
                // A preview that the session itself refuses carries its reason; either way the newest result leads.
                if (preview.Error != null) feedback.Refuse(CoveragePlace, "Coverage preview", preview.Error);
                else feedback.Report(CoveragePlace, "Coverage preview completed. Select Apply to apply it.");
                presentation?.RequestRefresh();
            });
        }
        // The preview compares the scene as an edit does: without a blocking scan.
        void PrepareEquipment(PatchEditingSession session, TargetIdentity identity, EquipmentAddition addition, string removeId) {
            var place = EquipmentPlace;
            Queue(place, "Equipment preview", pin => {
                EquipmentPreview preview;
                try {
                    var copies = EquipmentCopies();
                    IReadOnlyList<SourceCandidate> candidates = null;
                    try { candidates = session.EquipmentSources(identity); } catch { }
                    preview = session.PreviewEquipment(identity, addition, removeId, pin(selected), item => EquipmentDisplayName(item, copies, candidates));
                }
                catch (Exception e) { feedback.Refuse(place, "Equipment preview", e.GetBaseException().Message); preparedEquipment = null; return; }
                preparedEquipment = new PreparedEquipment { Addition = addition, RemoveId = removeId, Preview = preview };
                if (preview.Error != null) feedback.Refuse(place, "Equipment preview", preview.Error);
                else feedback.Report(place, "Equipment preview completed. Select Apply to apply it.");
                presentation?.RequestRefresh();
            });
        }
        // Opens the form for a new copy of the source: an ID from the device's name that the scene patch file does not use yet, and
        // the device's placement relative to its own crossing carried over to the inspected crossing, five metres aside.
        // A bell has no placement of its own to carry over: it starts at the inspected crossing, five metres aside, and its ID
        // comes from the definition's name or identifier.
        void UseSource(PatchEditingSession session, SourceCandidate candidate) {
            CloseCoverageForm(); feedback.ClearPlace(LightGatePlace); feedback.ClearPlace(BellPlace);
            equipmentSource = candidate.Source; preparedEquipment = null; equipmentOwn = null; equipmentEdited = null;
            equipmentLabel = InventoryPresentationState.EquipmentSourceName(candidate);
            var path = candidate.Source.Path ?? candidate.Source.Definition;
            var name = candidate.Name ?? path.Substring(path.LastIndexOf('/') + 1);
            equipmentFields[0] = session.NewEquipmentId(name.ToLowerInvariant().Replace(' ', '-') + "-added");
            for (var i = 1; i < 7; i++) equipmentFields[i] = i < 4 ? "" : "0";
            if (selected == null || selected.Position.Count != 3 || selected.Rotation.Count != 4) return;
            FillEquipment(EquipmentPlacement.Propose(candidate.Position, candidate.Rotation, candidate.CrossingPosition, candidate.CrossingRotation, selected.Position.ToArray(), selected.Rotation.ToArray()));
        }
        // Opens the form for an addition of this mod as the scene patch file holds it.
        void ReadEquipment(EquipmentAddition addition, string name) {
            CloseCoverageForm(); feedback.ClearPlace(LightGatePlace); feedback.ClearPlace(BellPlace);
            equipmentSource = addition.Source; preparedEquipment = null; equipmentOwn = null;
            equipmentLabel = name;
            equipmentFields[0] = addition.Id; equipmentEdited = addition.Id;
            if (addition.Placement?.Position != null && addition.Placement.Rotation != null) FillEquipment(addition.Placement);
        }
        // The exact placement the form was filled from: a field the modder leaves as it shows keeps that value, and a
        // rotation whose three angles are unchanged keeps its quaternion, so an untouched Apply changes nothing and a
        // change of one axis rewrites only that axis.
        Placement equipmentOwn;
        double[] equipmentAngles;
        void FillEquipment(Placement placement) {
            equipmentOwn = placement;
            var angles = new Quaternion((float)placement.Rotation[0], (float)placement.Rotation[1], (float)placement.Rotation[2], (float)placement.Rotation[3]).eulerAngles;
            equipmentAngles = new double[] { angles.x, angles.y, angles.z };
            for (var i = 0; i < 3; i++) { equipmentFields[i + 1] = CommandBand.Number(placement.Position[i]); equipmentFields[i + 4] = CommandBand.Number(equipmentAngles[i]); }
        }
        // The ID of the addition the open form edits, or null for a new addition from Use.
        string equipmentEdited;
        // After a native history step the open form for an addition shows that addition as the scene patch file now holds
        // it, as the placement fields show the object's current placement. An addition the step removed leaves the form as
        // it is; a new addition's form keeps the modder's values.
        void ReadOpenEquipment() {
            if (equipmentSource == null || equipmentEdited == null || attachment == null || selected?.Identity.Structure != CrossingCoverageScene.Structure) return;
            try {
                var entry = attachment.Session.EquipmentEntries(selected.Identity).FirstOrDefault(e => e.Addition.Id == equipmentEdited);
                if (entry == null) return;
                equipmentSource = entry.Addition.Source; preparedEquipment = null; equipmentOwn = null; equipmentFields[0] = entry.Addition.Id;
                var copies = EquipmentCopies();
                IReadOnlyList<SourceCandidate> candidates = null;
                if (entry.Built == null) try { candidates = attachment.Session.EquipmentSources(selected.Identity); } catch { }
                equipmentLabel = entry.Built == null ? InventoryPresentationState.UnbuiltEquipmentName(entry.Addition, candidates)
                    : EquipmentDisplayName(entry.Built, copies, candidates);
                if (entry.Addition.Placement?.Position != null && entry.Addition.Placement.Rotation != null) FillEquipment(entry.Addition.Placement);
            } catch (Exception) { }
        }
        void CloseEquipmentForm() { equipmentEdited = null; equipmentSource = null; equipmentLabel = null; preparedEquipment = null; equipmentOwn = null; equipmentAngles = null; for (var i = 0; i < 7; i++) equipmentFields[i] = i < 4 ? "" : "0"; }
        EquipmentAddition ParseEquipment(out string problem) {
            var id = (equipmentFields[0] ?? "").Trim();
            problem = EquipmentAddition.IdProblem(id);
            if (problem != null) return null;
            var position = new double[3]; var angles = new double[3]; var turned = equipmentOwn == null;
            for (var i = 0; i < 3; i++) {
                var ok = equipmentOwn == null ? Double.TryParse(equipmentFields[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out position[i]) : CommandBand.Entered(equipmentFields[i + 1], equipmentOwn.Position[i], out position[i]);
                ok &= equipmentOwn == null ? Double.TryParse(equipmentFields[i + 4], NumberStyles.Float, CultureInfo.InvariantCulture, out angles[i]) : CommandBand.Entered(equipmentFields[i + 4], equipmentAngles[i], out angles[i]);
                if (!ok) { problem = "enter the position and rotation as numbers with a decimal point"; return null; }
                if (equipmentOwn != null && angles[i] != equipmentAngles[i]) turned = true;
            }
            double[] rotation;
            if (!turned) rotation = (double[])equipmentOwn.Rotation.Clone();
            else { var q = Quaternion.Euler((float)angles[0], (float)angles[1], (float)angles[2]); rotation = new double[] { q.x, q.y, q.z, q.w }; }
            return new EquipmentAddition(id, equipmentSource.IsDefinition ? EquipmentAddition.BellPattern : EquipmentAddition.LightGatePattern, equipmentSource, new Placement(position, rotation));
        }
        // Fills the fields from the selected tracks of a circuit or addition; a circuit with native spans or detached, and a new
        // addition, start empty.
        void ReadCoverage(CircuitCoverage value) {
            for (var role = 0; role < 3; role++) {
                var range = value?.Ranges?[role];
                coverageFields[role * 4] = range?.From.Segment ?? "";
                coverageFields[role * 4 + 1] = range?.From.End ?? "a";
                coverageFields[role * 4 + 2] = range == null ? "" : CommandBand.Number(range.From.Distance);
                coverageFields[role * 4 + 3] = range == null ? "" : CommandBand.Number(range.To.Distance);
            }
        }
        CircuitCoverage ParseCoverage(out string problem, bool addition) {
            problem = null;
            var ranges = new SpanRange[3];
            for (var role = 0; role < 3; role++) {
                var segment = (coverageFields[role * 4] ?? "").Trim(); var end = (coverageFields[role * 4 + 1] ?? "").Trim().ToLowerInvariant();
                if (segment.Length == 0 || (end != "a" && end != "b") || !Double.TryParse(coverageFields[role * 4 + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var from) || !Double.TryParse(coverageFields[role * 4 + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out var to)) {
                    problem = "enter a segment ID, end a or b, and from and to distances with a decimal point for " + CommandBand.RoleCaption(CircuitCoverage.Roles[role]);
                    return null;
                }
                ranges[role] = new SpanRange(new TrackLocation(segment, end, from), new TrackLocation(segment, end, to));
            }
            return addition ? CircuitCoverage.Added(ranges[0], ranges[1], ranges[2]) : CircuitCoverage.Tracks(ranges[0], ranges[1], ranges[2]);
        }
        int queuedPasses;
        bool queuedCompared;
        // The frame in which the last gizmo ended.
        int gizmoEnded = -10;
        string queuedKey;
        // The share of one frame the comparison for a requested edit may take: more than the background check, because
        // the user is waiting, and little enough to keep the game moving.
        const double EditCheckBudgetMs = 8;
        // The one edit or preview that waits for the comparison of the scene. Closing the panel, leaving the native map editor and
        // ending the load attempt cancel it with a refusal: Cancel applies to every pending interaction.
        RequestQueue requests;
        RequestQueue Requests => requests ?? (requests = new RequestQueue(feedback));
        void CancelRequest(string reason) {
            var snapPending = Requests.Place == SnapPlace;
            if (!Requests.Cancel(reason)) return;
            if (snapPending) EndWorldEditAction();
            inventory?.CancelFreshnessCheck();
            presentation?.RequestRefresh();
        }
        // Takes the gizmo away. A drag in progress is cancelled and reported; a gizmo that only stands goes silently.
        void CancelGizmo() {
            var pending = gizmo; gizmo = null;
            if (pending != null) pending.Close();
        }
        // Starting the gizmo binds it to the inspected object: queued like an edit, so no scan blocks.
        void BeginGizmo(UI.Gizmos.TransformGizmoMode mode) {
            if (gizmo == null || !gizmo.Holding) CancelGizmo();
            Queue(GizmoPlace, "Gizmo", pin => {
                var standing = gizmo;
                if (standing == null || !standing.Holding) { StartGizmo(mode, pin); return; }
                try { standing.Rebind(pin(selected)); standing.SetMode(mode); }
                catch (Exception e) { CancelGizmo(); feedback.Report(GizmoPlace, "Gizmo unavailable: " + e.GetBaseException().Message); }
            });
        }
        // The object the standing gizmo belongs to.
        object gizmoHandle;
        void StartGizmo(UI.Gizmos.TransformGizmoMode mode, Func<SceneObservation, InspectedTarget> pin) {
            try {
                if (!Editable || selection?.Pending != null || attachment == null) throw new InvalidOperationException("Inspect a supported target and finish world selection first");
                var inspection = pin(selected);
                var root = new GameObject("CrankyAnt Scene Patcher transform gizmo");
                root.transform.SetParent(transform, false);
                gizmo = root.AddComponent<GizmoController>();
                gizmo.Initialize(engine, capabilities, attachment, selected.Identity, inspection, mode, (outcome, message) => {
                    gizmo = null; gizmoEnded = Time.frameCount;
                    // The gizmo comes back for the next drag once the object was observed again.
                    gizmoArmKey = null;
                    if (outcome?.Applied == true && !outcome.Unchanged) ResetConnectionsPreview();
                    feedback.Report(GizmoPlace, message ?? (outcome?.Applied == true ? (outcome.Unchanged ? "Gizmo released without a move; nothing changed." : IsPad ? CommandBand.PadPlacement("Gizmo placement") : "Gizmo placement completed.") : "Gizmo placement refused: " + outcome?.Error));
                    OwnChange(); RefreshAfterEdit(); attachment?.RefreshChrome();
                });
            } catch (Exception e) { gizmo = null; feedback.Report(GizmoPlace, "Gizmo unavailable: " + e.GetBaseException().Message); }
        }
        // A crossing pad's geometry is generated along its track binding: a placement moves the pad object and nothing
        // the player sees, and its result says so.
        bool IsPad => selected != null && selected.Identity.Structure == CrossingPadScene.Structure;
        // The baked scenery rule's verdicts, written to the log once per load attempt for the evidence of the listed and
        // unlisted group roots.
        string loggedVerdicts;
        void LogGroupVerdicts() {
            if (loggedVerdicts == loadId) return;
            loggedVerdicts = loadId;
            try {
                var lines = GroupWorld.Report().ToArray();
                context?.Log.Info("Scene Patcher " + Version + ": baked scenery rule, " + lines.Length + " candidates:" + Environment.NewLine + String.Join(Environment.NewLine, lines));
            } catch (Exception e) { context?.Log.Info("Scene Patcher " + Version + ": baked scenery rule report unavailable: " + e.GetBaseException().Message); }
        }
        void Inspect() {
            feedback.Refreshed();
            ClearSnap();
            try {
                if (inventory == null) throw new InvalidOperationException("Inspection is unavailable");
                inventory.Refresh();
                feedback.InventoryCurrent();
                LogGroupVerdicts();
                // A refresh read the whole scene: the snapshot is compared for the history as it is now.
                comparedKey = idleKey = ChangeStamp(); comparedAt = Time.unscaledTime;
                var inspectedBefore = selected != null;
                if (selected != null) selected = inventoryPresentation.CurrentSelection(inventory, selected);
                var lines = new List<string>();
                var scenePatchFiles = ModScenePatchFiles.Discover().ToArray();
                foreach (var mod in scenePatchFiles) {
                    if (mod.Error != null) { lines.Add(mod.Source.Identifier + ": " + mod.Error); continue; }
                    foreach (var item in inventory.InspectScenePatchFile(mod.Json))
                                                // A field without a value is left out.
                        lines.Add(String.Join("; ", new[] { mod.Source.Identifier + "/" + (item.Id ?? "scene patch file") + ", " + item.MatchCount + " inspection matches",
                            item.Dependencies.Count == 0 ? null : "dependsOn=" + String.Join(", ", item.Dependencies) }.Concat(item.Problems).Where(f => !String.IsNullOrEmpty(f))));
                }
                scenePatchFileInspection = String.Join("\n", lines);
                definitions.Read(scenePatchFiles);
                // Setups that could not be read leave every marker in its own family; each marker notes it, and the
                // diagnostics say it once for the inventory.
                if (NativeInventory.StartersError != null) MapEditorDiagnostic(StarterPlacements.Unreadable(NativeInventory.StartersError));
                status = "Refreshed scene observations and supplied definitions without applying patches." + (inspectedBefore && selected == null ? " The inspected object is no longer present." : "");
            } catch (Exception e) { MapEditorDiagnostic(e.GetBaseException().Message); }
        }
        // Copy ID names the scene target map of the edited mod's scene patch file, or the one scene patch editing would write for it.
        string Identity(SceneObservation entry) =>
            attachment != null ? attachment.Session.CopyId(entry) : PatchEditingSession.CopyId(ModScenePatchFiles.MapIds(), entry);
        // Focus needs the inspected object, not a fresh inventory: it moves the camera to where the object is now. It
        // never jumps to another object of the same path, because it uses the inspected handle.
        void Focus(SceneObservation entry) {
            try {
                if (!(entry.Handle is GameObject target) || target == null) throw new InvalidOperationException("the inspected object no longer exists; select Refresh and inspect it again");
                var map = NativeInventory.MapPosition(target.transform);
                var point = FocusTarget.Point(entry.Family, new double[] { map.x, map.y, map.z }, TrackPoints(entry), out var refusal);
                if (point == null) throw new InvalidOperationException(refusal);
                JumpTo(new Vector3((float)point[0], (float)point[1], (float)point[2]));
            } catch (Exception e) { feedback.Report("Focus", "Focus refused: " + e.GetBaseException().Message); }
        }
        // The track points of an object whose Focus target is on the track, in map coordinates: a track span's locations,
        // a starter placement marker's track point. Null for any other object.
        double[][] TrackPoints(SceneObservation entry) {
            if (trackPoses == null || !(entry?.Handle is GameObject target) || target == null) return null;
            if (entry.Family == FocusTarget.Span) return trackPoses.SpanPositions(target);
            if (entry.Family != StarterPlacements.Family) return null;
            var point = trackPoses.MarkerPoint(target);
            return point == null ? new double[0][] : new[] { point };
        }
        // The starter placement marker's track point while it is inspected, and why there is none, read once per frame:
        // the world marks it in gold, the Placement tab shows it and Focus is disabled without it.
        int trackPointFrame = -1; object trackPointOf; double[] trackPoint; string trackPointMissing;
        internal bool IsStarter => selected != null && selected.Identity.Structure == StarterPlacementScene.Structure;
        // The track the inspected starter placement marker's car cut stands on, from its track point back, in map
        // coordinates, every 3 m: the world draws it so the direction the cut extends shows. Read a few times a second.
        int cutFrame = -100; object cutOf; IReadOnlyList<double[]> cut;
        internal IReadOnlyList<double[]> InspectedCut() {
            if (!IsStarter || attachment == null) return null;
            if (Time.frameCount < cutFrame + 15 && ReferenceEquals(cutOf, selected.Handle)) return cut;
            cutFrame = Time.frameCount; cutOf = selected.Handle;
            try {
                var body = attachment.Session.StarterCut(selected.Identity, null);
                cut = body.Where((p, i) => i % 6 == 0 || i == body.Count - 1).ToArray();
            } catch { cut = null; }
            return cut;
        }
        // The effective wreck setting of the car placement that reads the inspected starter placement marker, or null when
        // not exactly one does: what the Wreck checkbox shows. Read a few times a second.
        int wreckFrame = -100; object wreckOf; bool? wreck;
        internal bool? InspectedWreck() {
            if (!IsStarter || attachment == null) return null;
            if (Time.frameCount < wreckFrame + 10 && ReferenceEquals(wreckOf, selected.Handle)) return wreck;
            wreckFrame = Time.frameCount; wreckOf = selected.Handle;
            try { wreck = attachment.Session.StarterWreck(selected.Identity); } catch { wreck = null; }
            return wreck;
        }
        internal double[] InspectedTrackPoint() {
            if (selected == null || selected.Family != StarterPlacements.Family) return null;
            if (trackPointFrame != Time.frameCount || !ReferenceEquals(trackPointOf, selected.Handle)) {
                trackPointFrame = Time.frameCount; trackPointOf = selected.Handle;
                trackPoint = null;
                if (trackPoses == null) trackPointMissing = "the track graph is unavailable in this game build";
                else if (!(selected.Handle is GameObject target) || target == null) trackPointMissing = "the inspected object no longer exists; select Refresh and inspect it again";
                else {
                    try { trackPoint = TrackPoints(selected)?.FirstOrDefault(); } catch { trackPoint = null; }
                    trackPointMissing = trackPoint == null ? StarterPlacements.NoTrackPoint : null;
                }
            }
            return trackPoint;
        }
        // Why Focus has no target for the inspected object, or null: a starter placement marker without a track point.
        string FocusUnavailable() {
            if (selected == null || selected.Family != StarterPlacements.Family) return null;
            return InspectedTrackPoint() == null ? trackPointMissing : null;
        }
        static void JumpTo(Vector3 position) {
            var type = Native.Type("CameraSelector");
            var selector = Native.Read(type, "shared");
            var strategy = Enum.Parse(type.GetNestedType("CameraIdentifier", Native.Flags), "Strategy");
            Native.Call(selector, "JumpToPoint", position, Quaternion.identity, strategy);
        }
    }
}
