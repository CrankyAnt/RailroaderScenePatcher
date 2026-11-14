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
            context = value; Hooks.Install(); NativeRepairHooks.Install(this);
            if (NativeRepairHooks.Error != null) context.Log.Info(NativeRepairHooks.Error + ". Native marker checks and their warnings remain active.");
        }
        LocalWorldBinding<PatcherBehaviour> Binding() => world ?? (world = new LocalWorldBinding<PatcherBehaviour>(CreateHost, DestroyHost, h => h.BeginAttempt()));
        public void OnWorldLoad() {
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
            if (!state.Initialized) state.Initialize(context); else state.ApplyDocuments();
        }
        // The retained state belongs to the active map and layer identifiers in the editor or in play. It is a scoped
        // implementation key, not a verified world identity; each load attempt checks every object against its identity.
        static object MapContext() => Tuple.Create(String.Join("\n", PackDocuments.MapIds()), PackDocuments.IsEditor);
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
        // state of every object that did not survive. Editor-bound parts end with the world.
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
            Hooks.Remove(); NativeRepairHooks.Remove();
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
        readonly Dictionary<string, string> pendingWarnings = new Dictionary<string, string>();
        // Informational overlap notices by native deduplication key; a newer notice for the same object replaces the pending one.
        readonly Dictionary<string, string> pendingNotices = new Dictionary<string, string>();
        bool presentationErrorLogged;
        TrackMarkerWorld markerWorld;
        internal bool Initialized { get; private set; }
        SceneInventory inventory;
        InspectionInputBlocker inputBlocker;
        SpatialSelection selection;
        SnapController snap;
        SnapRequest preparedSnap;
        bool choosingSnap;
        GizmoController gizmo;
        SceneObservation selected;
        string selectionProblem;
        string documentInspection = "";
        // The patch documents the last refresh read, and whether they changed outside the editor since.
        readonly SuppliedDefinitions definitions = new SuppliedDefinitions();
        bool definitionsStale => definitions.Stale;
        float nextInventoryCheck, nextDefinitionCheck;
        // The share of one frame that the open panel's scene comparison may take. One unit of the scan can exceed it.
        const double InventoryCheckBudgetMs = 2;
        bool urgentCheck;
        readonly System.Diagnostics.Stopwatch inventoryCheckClock = new System.Diagnostics.Stopwatch();
        bool editor;
        string status = "Initializing", filter = "";
        readonly HashSet<string> reported = new HashSet<string>();
        readonly List<string> diagnostics = new List<string>();
        NativeEditorPresentation presentation;
        Texture2D worldLabelBacking;
        object attachedEditor;
        string presentationSnapshot;
        readonly InventoryPresentationState inventoryPresentation = new InventoryPresentationState();
        internal const string Version = "0.5.0";
        Authoring authoring;
        object refusedEditor;
        string authoringStatus = "";
        // The last edit results, shown in the message area above the action slots and written to the log. A place is the
        // action group a message came from; the message area shows it as the message's source.
        readonly EditFeedback feedback = new EditFeedback();
        const string BindingPlace = "Track binding", PlacementPlace = "Placement", GizmoPlace = "Gizmo", SnapPlace = "Snap", ActivityPlace = "Activity",
            ModificationPlace = "Changes", TerrainPlace = "Terrain output", CoveragePlace = "Crossing coverage";
        bool staleLogged = true;
        NativeTrackPoses trackPoses;
        float nextTrackFollow;
        const string FollowPlace = "Follow";
        readonly string[] pose = { "", "", "", "", "", "" };
        readonly string[] binding = { "", "A", "" };
        // Crossing coverage being edited: the circuit, four fields per role (segment, end, from, to) and the previewed change.
        // coverageAddition is the ID of the authored addition being added or edited, or null while a native circuit is edited.
        string coverageCircuit, coverageAddition, newAddition = "";
        readonly string[] coverageFields = new string[12];
        sealed class PreparedCoverage { internal string Circuit, Addition; internal CircuitCoverage Value; internal CoveragePreview Preview; }
        PreparedCoverage preparedCoverage;
        // Scopes native warning deduplication to one load attempt.
        string loadId = Guid.NewGuid().ToString("N");
        internal void Initialize(IModContext value) {
            context = value;
            outlinerWidth = ReadOutlinerWidth();
            Initialized = true;
            feedback.Log = message => {
                var outstanding = "";
                try { if (authoring != null) outstanding = " Outstanding: " + authoring.Outstanding(); } catch { }
                context?.Log.Info(message + outstanding);
            };
#if ISSUE39_OBSERVATION
            JourneyObservation.Sink = message => Debug.Log("[Issue39Attempt] " + message);
#endif
            try {
                editor = PackDocuments.IsEditor;
                try {
                    world = new BoardScene();
                    scene = new IndependentBoards(world, () => native.Discover());
                } catch (Exception e) {
                    world?.Dispose(); world = null;
                    status = "Independent board capability incompatible or unavailable: " + e.GetBaseException().Message;
                    Report("capability", status);
                }
                PrepareRuntime(value);
                if (editor) {
                    inventory = new SceneInventory(native);
                    selection = new SpatialSelection(native, () => inventory?.Entries);
                }
                if (editor) {
                    try { inputBlocker = gameObject.AddComponent<InspectionInputBlocker>(); inputBlocker.Initialize(); inputBlocker.RefreshRegions = SyncInputRegions; }
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
                try { capabilities.Add(new CrossingPadScene(new CrossingPadWorld { KnownReferrers = KnownReferrers }, Capability.Unchecked)); }
                catch (Exception e) {
                    capabilities.AddUnavailable(CrossingPadScene.Structure, e.GetBaseException().Message);
                    context.Log.Info("Scene Patcher " + Version + ": crossing-pad capability incompatible or unavailable: " + e.GetBaseException().Message);
                }
                try { capabilities.Add(new CrossingCoverageScene(new CrossingCoverageWorld(), Capability.Unchecked)); }
                catch (Exception e) {
                    capabilities.AddUnavailable(CrossingCoverageScene.Structure, e.GetBaseException().Message);
                    context.Log.Info("Scene Patcher " + Version + ": crossing-coverage capability incompatible or unavailable: " + e.GetBaseException().Message);
                }
                try { capabilities.Tracks = trackPoses = new NativeTrackPoses(); }
                catch (Exception e) { context.Log.Info("Scene Patcher " + Version + ": track-following placement unavailable: " + e.GetBaseException().Message); }
                context.Log.Info("Scene Patcher " + Version + ": native capabilities checked; game " + Application.version + "; production integration unchecked");
                ApplyDocuments();
            } catch (Exception e) {
                status = "Scene capabilities unavailable: " + e.GetBaseException().Message;
                Report("capability", status);
            }
        }
        internal void PrepareRuntime(IModContext value) {
            if (engine != null) return;
            context = value; editor = PackDocuments.IsEditor;
            native = new NativeInventory(g => scene == null ? "Independent board capability unavailable" : scene.Refusal(g));
            capabilities = new SceneCapabilities(PackDocuments.MapIds, native);
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
                foreach (var pack in PackDocuments.Discover()) {
                    if (!pack.Applicable) continue;
                    var result = runtime.BeforeNativeCheck(pack.Json, pack.Source, identity);
                    if (result.Outcomes.Count > 0) attempted = true;
                    if (result.EarlyRouteUnavailable != null) unavailable = result.EarlyRouteUnavailable;
                }
                return attempted ? null : unavailable ?? "No applicable dependency-free relationship-only repair before TrackMarker.OnEnable";
            });
        }
        internal void ApplyDocuments() {
            if (engine == null) return;
            if (world != null) {
                try { world.Rebind(); }
                catch (Exception e) { Report("terrain-rebind", "Terrain completion for independent boards is unavailable in this load: " + e.GetBaseException().Message + ". Reload the map. Report to the patcher maintainer."); }
            }
            IReadOnlyList<LoadedDocument> documents;
            try { documents = runtime.ApplyLoaded(PackDocuments.Discover(), overlapNotices ?? (overlapNotices = new NativeOverlapNotices(this))); }
            catch (Exception e) { Report("discovery", "Loaded MapMod discovery failed. Report to the patcher maintainer. " + e.GetBaseException().Message); return; }
            foreach (var document in documents) {
                var pack = document.Pack;
                // Follow-up work for one document cannot stop the others; their application has already happened in load order.
                try {
                    if (document.PresentationError != null) context?.Log.Info(Describe(pack, document.PresentationError));
                    if (document.Result == null) {
                        if (pack.Error != null) EditorDiagnostic(Describe(pack, pack.Error));
                        continue;
                    }
                    var result = document.Result;
                    if (result.Error != null) {
                        if (result.EditorOnly) EditorDiagnostic(Describe(pack, result.Error));
                        continue;
                    }
                    foreach (var item in result.Outcomes) {
                        if (!item.Value.Applied) {
                            var error = Describe(pack, item.Key + ": " + item.Value.Error);
                            if (item.Value.EditorOnly) EditorDiagnostic(error);
                        }
                        else ReconcileMarkerWarning(pack, item.Key, item.Value);
                    }
                } catch (Exception e) { context?.Log.Info(Describe(pack, "Follow-up after application failed: " + e.GetBaseException().Message)); }
            }
            status = "Documents processed. " + (scene?.Status ?? "");
        }
        NativeOverlapNotices overlapNotices;
        sealed class NativeOverlapNotices : IOverlapNotices {
            readonly PatcherBehaviour owner;
            internal NativeOverlapNotices(PatcherBehaviour owner) { this.owner = owner; }
            public void Notice(OverlapNotice notice) {
                owner.context?.Log.Info("Scene Patcher overlap notice: " + notice.Message);
                owner.pendingNotices[notice.Key] = notice.Message;
                owner.FlushWarnings();
            }
        }
        void ReconcileMarkerWarning(LoadedPack pack, string operation, Outcome outcome) {
            if (markerWorld == null || !(outcome.ResolvedTarget is GameObject target) || target == null) return;
            var document = JObject.Parse(pack.Json);
            var entry = ((JArray)document["modifications"]).OfType<JObject>().Single(e => (string)e["id"] == operation);
            // Placement or activity alone does not repair a track-marker relationship.
            var binding = entry["relationships"]?["trackMarker"] as JObject;
            if (binding == null || binding["segment"]?.Type != JTokenType.String) return;
            foreach (var attribution in NativeRepairHooks.Warnings.Where(w => ReferenceEquals(w.Target, target))) {
                if (runtime.Reconcile(pack.Source, operation, attribution, nativeDiagnostics, () => {
                    if (target == null) return false;
                    var matches = capabilities.Resolve(outcome.Target);
                    if (matches.Count != 1 || !ReferenceEquals(matches[0], target)) return false;
                    capabilities.Preflight(target);
                    var actual = markerWorld.Location(target);
                    return PackDocuments.MapIds().Contains((string)document["sourceMap"]["mapId"], StringComparer.Ordinal) && actual != null && actual.Segment == (string)binding["segment"] && actual.End == (string)binding["end"] && Math.Abs(actual.Distance - (double)binding["distance"]) <= 0.001 && markerWorld.DependentProblem(target) == null;
                })) context?.Log.Info("Scene Patcher reconciled one exactly attributed current marker warning after verified repair. Earlier route unavailable: " + attribution.EarlyRouteUnavailable);
            }
        }
        static string Describe(LoadedPack loaded, string message) {
            var pack = loaded.Source;
            return (pack.Name ?? pack.Identifier) + " [" + pack.Identifier + " " + pack.Version + "] " + message + ". Patcher " + Version + ". Author: " + (pack.Author ?? "not supplied") + (String.IsNullOrWhiteSpace(pack.Website) ? "" : "; website: " + pack.Website);
        }
        internal void ReportWorld(string message) { Report("world", message); }
        void EditorDiagnostic(string message) { if (editor && !diagnostics.Contains(message)) diagnostics.Add(message); }
        void Report(string key, string message) {
            if (!reported.Add(key)) return;
            diagnostics.Add(message); status = message;
            context?.Log.Info(message);
            pendingWarnings[key] = message;
            FlushWarnings();
        }
        void FlushWarnings() {
            // Before collector initialization MessageLog falls back to Serilog, which is not player presentation.
            if (Game.Diagnostics.MessageCollector.Shared == null) return;
            foreach (var item in pendingWarnings.ToArray()) {
                try {
                    Game.Diagnostics.MessageLog.Warning("Railroader Scene Patcher", item.Value, "Scene patches", loadId + item.Key);
                    pendingWarnings.Remove(item.Key);
                } catch (Exception e) {
                    if (!presentationErrorLogged) { presentationErrorLogged = true; context?.Log.Info("Native structured warning delivery unavailable; delivery remains pending: " + e.GetBaseException().Message); }
                    return;
                }
            }
            // An informational row: it reports possible incompatibility and is never a failure warning.
            foreach (var item in pendingNotices.ToArray()) {
                try {
                    Game.Diagnostics.MessageLog.Info("Railroader Scene Patcher", item.Value, "Scene patches", loadId + item.Key);
                    pendingNotices.Remove(item.Key);
                } catch (Exception e) {
                    if (!presentationErrorLogged) { presentationErrorLogged = true; context?.Log.Info("Native structured notice delivery unavailable; delivery remains pending: " + e.GetBaseException().Message); }
                    return;
                }
            }
        }
        // The map editor appears after world load and is replaced by an editor reload.
        void Attach() {
            if (!editor || engine == null) return;
            var controller = Native.Read(Native.Type("Game.MapEditor.MapEditorController"), "Shared");
            var found = controller as UnityEngine.Object == null ? null : Native.Read(controller, "MapEditor");
            if (ReferenceEquals(found, attachedEditor)) return;
            presentation?.Dispose(); presentation = null;
            selection?.Cancel(); selectionProblem = null; ClearSnap();
            Detach(); attachedEditor = found; refusedEditor = null; presentationSnapshot = null;
            if (found == null) return;
            if (inputBlocker != null && inputBlocker.Available) {
                try {
                    presentation = new NativeEditorPresentation(DrawWindow, () => { CancelGizmo(); selection?.Cancel(); selectionProblem = null; ClearSnap(); });
                    presentation.WindowHelp = ToggleWindowHelp; presentation.WindowHelpOpen = () => bodyView == BodyView.MainHelp;
                    if (presentation.TitleHelpProblem != null) context?.Log.Info("The window help button could not join the title bar (" + presentation.TitleHelpProblem + "); it stands in the command band.");
                }
                catch (Exception e) {
                    Report("inspection-presentation", "Inspection UI unavailable: " + e.GetBaseException().Message);
                    context?.Log.Info("Inspection UI unavailable: " + e);
                }
            }
            if (Hooks.Error != null) { refusedEditor = found; authoringStatus = Hooks.Error; return; }
            try {
                authoring = new Authoring(engine, found); Hooks.Current = authoring;
                authoring.Session.Replayed = identity => replayed.Add(identity);
                // The patcher's own Save writes the patch document: that text is what the definitions on disk now are, so it
                // is no change from outside and the inventory lamp stays Current.
                PackDocuments.Written = text => { try { definitions.Written(PackDocuments.ActivePackId(), text); } catch { } };
                filled = false;
                snap = new SnapController(authoring.Session, capabilities, new NativeSnapScene(native));
                authoring.Log = message => context?.Log.Info(message);
                authoringStatus = authoring.Session.Blocked ?? "Editing ready for " + authoring.PatchPath;
                // The editor built its title before this session could account for the document.
                authoring.RefreshChrome();
            } catch (Exception e) { refusedEditor = found; authoringStatus = "Editing unavailable: " + e.GetBaseException().Message; }
        }
        void Detach() {
            ClearSnap(); snap = null;
            CancelGizmo();
            PackDocuments.Written = null;
            if (authoring == null) return;
            if (ReferenceEquals(Hooks.Current, authoring)) Hooks.Current = null;
            authoring.Dispose(); authoring = null;
        }
        void SyncInputRegions() {
            if (inputBlocker == null) return;
            var open = presentation != null && presentation.IsOpen;
            inputBlocker.SetRegions(open, open ? presentation.PanelBounds : Rect.zero, presentation == null ? Rect.zero : presentation.EntryBounds);
            inputBlocker.WorldPicking = open && selection?.Pending != null;
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
        // The snapshot was compared with the whole scene, by a refresh or a completed pass, while the editor's history
        // read comparedKey; comparedAt is when. An edit or a preview that starts while that still holds, and soon
        // enough, needs no pass of its own: nothing but this patcher changed the scene's authored state since, and the
        // patcher's own changes are observed again as they happen. The edit's guard still observes its target.
        string comparedKey, passKey;
        bool running;
        // The marker reference scan the panel vouches for: the one of the discovery its snapshot was compared with,
        // while a queued request runs or while the snapshot still counts as compared, as when a gizmo is released.
        ReferenceScan KnownReferrers() => editor && inventory != null && !inventory.IsStale && (running || Compared()) ? NativeInventory.LastReferrers : null;
        // Inspects an observation offered by a world choice. The choice offers the snapshot's own entries, so the entry
        // is inspected as it is; an observation that is no entry is looked up by its object, and only when the snapshot
        // does not hold it the inventory is read again.
        void InspectObservation(SceneObservation observation) {
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
        // This patcher changed the editor's history itself, by an edit or by Undo or Redo of its own step. The change is
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
        void Update() {
            FlushWarnings();
            if (!Initialized || engine == null) return;
            if (queuedEdit != null) {
                var ready = inventory == null || inventory.IsStale || queuedCompared || inventory.CompletedPasses > queuedPasses;
                if (!ready) {
                    Step(EditCheckBudgetMs);
                    ready = inventory.IsStale || inventory.CompletedPasses > queuedPasses;
                }
                if (ready) {
                    var run = queuedEdit; queuedEdit = null; queuedPlace = null;
                    // The request runs on a snapshot that was just compared: the marker capability may use that
                    // discovery's reference scan.
                    running = true;
                    try { run(); } finally { running = false; }
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
                ReadPose(); presentation?.RequestRefresh();
            }
            // Undo and Redo of this patcher's own steps changed their objects: observe those again, so they do not read
            // as foreign changes. A step can destroy objects, which the game completes at the end of the frame, so the
            // observation waits two frames, as after an edit.
            if ((replayed.Count != 0 || moved.Count != 0) && replayFrame == 0) { replayFrame = Time.frameCount + 2; OwnChange(); inventory?.CancelFreshnessCheck(); }
            if (replayFrame != 0 && Time.frameCount >= replayFrame) {
                replayFrame = 0;
                if (inventory != null) foreach (var identity in replayed.ToArray()) {
                    var entry = inventory.Entries.FirstOrDefault(e => e.Identity.Scene == identity.Scene && e.Identity.Path == identity.Path && e.Identity.Component == identity.Component);
                    var updated = entry == null ? null : inventory.ReobserveRelated(entry);
                    if (updated != null) { var inspected = ReferenceEquals(selected, entry); KeepSelection(entry, updated); if (inspected) ReadPose(); }
                }
                // Objects a follow update moved: every entry of the object takes its new observation, and the fields of
                // the inspected one take its new pose.
                if (inventory != null) foreach (var handle in moved.ToArray())
                    foreach (var entry in inventory.Entries.Where(e => ReferenceEquals(e.Handle, handle)).ToArray()) {
                        if (!inventory.Entries.Any(e => ReferenceEquals(e, entry))) continue;
                        var updated = inventory.ReobserveRelated(entry);
                        if (updated != null) { var inspected = ReferenceEquals(selected, entry); KeepSelection(entry, updated); if (inspected) ReadPose(); }
                    }
                replayed.Clear(); moved.Clear(); presentation?.RequestRefresh();
            }
            // 1. A panel that is open before the inventory was ever read, as after an automatic open at world load,
            // fills itself once.
            if (!filled && authoring != null && inventory != null && presentation != null && presentation.IsOpen && inventory.Entries.Count == 0 && refreshFrame == 0) {
                filled = true; fullRefresh = true; refreshFrame = Time.frameCount + 30;
            }
            // Escape cancels a pending world choice or an active gizmo and is consumed, as in the native editor modes, so
            // it does not also open the pause menu. The gizmo also ends through the editor's own Escape route, which
            // can run before this handler in the same frame: an Escape in the frame a gizmo ended is consumed as well.
            var escapable = selection?.Pending != null || gizmo?.Active == true || Time.frameCount <= gizmoEnded + 1;
            if (escapable != escapeHeld) try {
                if (escapable) UI.GameInput.RegisterEscapeHandler(UI.GameInput.EscapeHandler.Transient, () => {
                    if (selection?.Pending != null) { selection.Cancel(); selectionProblem = null; ClearSnap(); presentation?.RequestRefresh(); return true; }
                    if (gizmo?.Active == true) { CancelGizmo(); presentation?.RequestRefresh(); return true; }
                    return Time.frameCount <= gizmoEnded + 1;
                });
                else UI.GameInput.UnregisterEscapeHandler(UI.GameInput.EscapeHandler.Transient);
                escapeHeld = escapable;
            } catch (Exception e) { escapeHeld = escapable; context?.Log.Info("Escape handling for the world choice and the gizmo is unavailable: " + e.GetBaseException().Message); }
            try { Attach(); }
            catch (Exception e) { authoringStatus = "Editing unavailable: " + e.GetBaseException().Message; }
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
            if (editor && open && inventory != null) {
                // The idle comparison only informs the band; an edit runs its own pass. It takes a small step on every
                // third frame and pauses five seconds after a pass. A change of the editor's history that is not this
                // patcher's own is the usual cause of a stale inventory, so it starts a pass at once, at the edit's pace.
                var key = ChangeStamp();
                if (key != idleKey) { idleKey = key; urgentCheck = true; nextInventoryCheck = 0; }
                // No pass runs between this patcher's own change and the observation of the changed objects: it would
                // compare them with what they were before.
                // While a gizmo is active the pose of its target is this patcher's own preview: a pass would compare it
                // with the snapshot and mark the inventory stale. The pass stops when the gizmo starts and waits until
                // it ended and its target was observed again, after a release and after a cancellation alike.
                if (gizmo?.Active == true) inventory.CancelFreshnessCheck();
                if (queuedEdit == null && gizmo?.Active != true && refreshFrame == 0 && replayFrame == 0 && (inventory.CheckInProgress || Time.unscaledTime >= nextInventoryCheck) && (urgentCheck || Time.frameCount % 3 == 0)) {
                    Step(urgentCheck ? EditCheckBudgetMs : InventoryCheckBudgetMs);
                    if (!inventory.CheckInProgress) { nextInventoryCheck = Time.unscaledTime + 5f; urgentCheck = false; }
                }
                if (Time.unscaledTime >= nextDefinitionCheck) {
                    nextDefinitionCheck = Time.unscaledTime + 2f;
                    try { definitions.Compare(PackDocuments.Discover()); }
                    catch { definitions.Unreadable(); }
                }
            } else inventory?.CancelFreshnessCheck();
            if (inventory != null && inventory.IsStale != staleLogged) {
                staleLogged = inventory.IsStale;
                if (staleLogged) context?.Log.Info("Scene inventory marked stale: " + inventory.StaleReason);
            }
            FollowTracks();
            if (scene != null) try {
                scene.Poll();
                foreach (var failure in scene.Failures)
                    Report("terrain/" + world.Label(failure.Key) + "/" + failure.Value.Error, "Terrain output for " + world.Label(failure.Key) + " did not complete: " + failure.Value.Error + ". Completed: " + Join(failure.Value.Completed) + ". Outstanding: " + Join(failure.Value.Outstanding) + ". The board keeps its applied placement; reloading the map regenerates the output. Report to the patcher maintainer.");
            } catch (Exception e) { Report("terrain", "Dependent terrain state remains unverified. Reload before retry. Report to the patcher maintainer. " + e.GetBaseException().Message); }
            if (presentation != null) {
                // A closed window shows nothing: it computes no snapshot. Opening builds the content anew.
                if (open) {
                    var snapshot = PresentationSnapshot();
                    feedback.Inspecting(selected?.Handle);
                    var inventoryChanged = inventoryPresentation.Observe(inventory, selected, filter, outlinerFilter, ChangeStamp());
                    if (snapshot != presentationSnapshot || inventoryChanged) { presentationSnapshot = snapshot; presentation.RequestRefresh(); }
                }
                presentation.Tick(); SyncInputRegions();
            }
        }
        // Objects of the edited document that follow a track move with it, a few times a second. No document or history changes.
        void FollowTracks() {
            if (authoring == null || gizmo?.Active == true || Time.unscaledTime < nextTrackFollow) return;
            nextTrackFollow = Time.unscaledTime + 0.25f;
            try {
                if (!authoring.Session.HasTrackFollowing) return;
                foreach (var item in authoring.Session.ReconcileTrackFollowing())
                    if (item.Value.Applied) {
                        context?.Log.Info("Scene Patcher: " + item.Key + " followed its track");
                        // The move is this patcher's own: the object is observed again, as after Undo of its own step,
                        // so the comparison does not read its new pose as a foreign change.
                        // A follow outcome names the object it moved, not an identity.
                        if (item.Value.ResolvedTarget != null && !moved.Any(known => ReferenceEquals(known, item.Value.ResolvedTarget))) moved.Add(item.Value.ResolvedTarget);
                    }
                    else if (reported.Add("follow/" + item.Key + "/" + item.Value.Error)) {
                        diagnostics.Add(item.Key + " could not follow its track: " + item.Value.Error);
                        context?.Log.Info("Scene Patcher: " + item.Key + " could not follow its track: " + item.Value.Error);
                    }
            } catch (Exception e) { if (reported.Add("follow/" + e.GetBaseException().Message)) context?.Log.Info("Scene Patcher: track following stopped: " + e.GetBaseException().Message); }
        }
        // Differs whenever the set of objects the document changes can differ.
        string ChangeStamp() { try { return authoring?.ChangeKey; } catch { return null; } }
        string PresentationSnapshot() {
            var pending = selection?.Pending;
            string outstanding = "";
            try {
                if (authoring != null) {
                    var session = authoring.Session;
                    outstanding = OutstandingSnapshot() + "|" +
                        session.LastSave?.Error + "|" + (session.LastSave == null ? "" : String.Join(", ", session.LastSave.Completed)) + "|" +
                        (session.LastSave == null ? "" : String.Join(", ", session.LastSave.Outstanding)) + "|" +
                        session.IncompleteOperation + "|" + authoring.Fault;
                }
            } catch (Exception e) { outstanding = e.GetBaseException().Message; }
            return String.Join("|", inspectorTab.Value, queuedPlace, status, scene?.Status, inventory?.IsStale, inventory?.StaleReason, definitionsStale, authoringStatus, feedback.Place, feedback.Result,
                documentInspection, String.Join("\n", diagnostics), selectionProblem, inputBlocker?.WorldInputProblem,
                pending?.Purpose, pending?.Source?.Label, pending?.Problem, pending?.Candidate?.Label,
                pending == null ? "" : String.Join("\n", pending.Offers.Select(o => o.Label)),
                selection?.Confirmed?.Identity, preparedSnap?.Identity, choosingSnap, gizmo?.State, outstanding, TerrainSnapshot(),
                coverageCircuit, preparedCoverage == null ? "" : String.Join("\n", preparedCoverage.Preview.Lines) + preparedCoverage.Preview.Error);
        }
        // Computing the outstanding work serializes the document and reads the dependency metadata. Its inputs change
        // with the editor's change counts, a Save attempt or an incomplete operation; one recomputation a second covers
        // a change that passes none of them.
        string outstandingKey, outstandingText;
        object outstandingOwner;
        float outstandingUntil;
        string OutstandingSnapshot() {
            var session = authoring.Session;
            var key = authoring.ChangeKey + "|" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(session.LastSave) + "|" + session.IncompleteOperation + "|" + session.Blocked;
            if (!ReferenceEquals(outstandingOwner, authoring) || key != outstandingKey || Time.unscaledTime >= outstandingUntil) {
                outstandingText = String.Join(", ", session.Outstanding(authoring.NativeDirty));
                outstandingOwner = authoring; outstandingKey = key; outstandingUntil = Time.unscaledTime + 1f;
            }
            return outstandingText;
        }
        readonly InspectedOutput inspectedOutput = new InspectedOutput();
        object terrainFor;
        string terrainState;
        string TerrainSnapshot() {
            if (authoring == null || selected == null || !Editable) { terrainFor = null; return ""; }
            var output = inspectedOutput.Of(authoring.Session, selected, out var problem);
            // A change of the inspected object's terrain output is reported where its Retry control is: the message area.
            var state = problem != null ? "unknown" : output.Error != null ? "stopped" : output.Outstanding.Count != 0 ? "outstanding" : output.Completed.Count != 0 ? "complete" : "none";
            if (!ReferenceEquals(terrainFor, selected.Handle)) { terrainFor = selected.Handle; terrainState = state; }
            else if (state != terrainState) {
                terrainState = state;
                if (state == "outstanding") feedback.Follow(TerrainPlace, "Terrain output outstanding; the game is regenerating it. Retry is on the Changes tab.");
                else if (state == "complete") feedback.Follow(TerrainPlace, "Terrain output complete.");
                else if (state == "stopped") feedback.Follow(TerrainPlace, "Terrain output stopped: " + output.Error + ". Select Retry on the Changes tab.");
            }
            return problem ?? String.Join("|", String.Join(";", output.Completed), String.Join(";", output.Outstanding), output.Error);
        }
        // Starts a new load attempt over the retained world. The engine keeps the captured state and guards of objects that
        // still stand under their identities and releases the rest; diagnostics, overlap notices and editor views start over.
        internal void BeginAttempt() {
            if (engine == null) return;
            EndAttempt();
            var reentry = engine.Reenter();
            runtime = new PatchRuntime(engine, nativeDiagnostics, Version, Application.version);
            overlapNotices = null;
            // Warnings not yet delivered belong to the previous load attempt.
            reported.Clear(); diagnostics.Clear(); pendingWarnings.Clear(); pendingNotices.Clear();
            loadId = Guid.NewGuid().ToString("N"); presentationErrorLogged = false;
            if (inventory != null) { inventory = new SceneInventory(native); staleLogged = true; }
            selected = null; selectionProblem = null; documentInspection = "";
            // The panel's state belongs to the load attempt as well: what it read, compared, reported and had open. The
            // next editor session builds a new window and fills it from the scene as it is then.
            definitions.Reset();
            comparedKey = null; passKey = null; idleKey = null; urgentCheck = false; filled = false; fullRefresh = false;
            terrainFor = null; revealed = null; bodyView = BodyView.Tabs; outlinerHelp = false;
            CloseCoverageForm();
            feedback.Clear();
            status = "Load attempt over the retained world";
            context?.Log.Info("Scene Patcher " + Version + ": load attempt over the retained world; " + reentry.Retained + " objects retained, " + reentry.Released + " released, " + reentry.Failures.Count + " cleanup failures");
            // Local only: no other peer is told, rolled back or disconnected.
            foreach (var failure in reentry.Failures)
                Report("reentry/" + failure, "Retained world cleanup is incomplete on this peer: " + failure + ". The patcher retries at the next load; reload the map to clear it. Report to the patcher maintainer.");
        }
        // Ends the load attempt: parts bound to the editor of this world end with it; the world's patcher state stays.
        // The window, its entry icon and its title-bar help button are disposed here and built again by Attach when the
        // next editor appears. Requests that waited on the scene of this attempt end with it.
        internal void EndAttempt() {
            presentation?.Dispose(); presentation = null; attachedEditor = null; refusedEditor = null; presentationSnapshot = null;
            Detach(); selection?.Cancel();
            queuedEdit = null; queuedPlace = null; refreshFrame = 0; replayFrame = 0; refreshTarget = null;
            replayed.Clear(); moved.Clear();
            inventory?.CancelFreshnessCheck();
            if (escapeHeld) { try { UI.GameInput.UnregisterEscapeHandler(UI.GameInput.EscapeHandler.Transient); } catch { } escapeHeld = false; }
        }
        internal void Release() { ReleaseParts(); }
        void ReleaseParts() {
            if (worldLabelBacking != null) Destroy(worldLabelBacking); worldLabelBacking = null;
            presentation?.Dispose(); presentation = null; attachedEditor = null;
            Detach(); inputBlocker?.Release(); inputBlocker = null; selection?.Release(); selection = null;
            scene?.Dispose(); scene = null; world?.Dispose(); world = null; capabilities = null; engine = null; inventory = null; native = null; selected = null;
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
                selection.Draw(selected, presentation.WorldUiBounds(), worldLabelBacking);
            }
            catch (Exception e) { selectionProblem = "World markings unavailable: " + e.GetBaseException().Message; }
        }
        void BeginSelection(SelectionKind kind, SceneObservation origin) {
            CancelGizmo();
            ClearSnap();
            try { selection.Begin(kind, origin); selectionProblem = null; }
            catch (Exception e) { selectionProblem = "World selection unavailable: " + e.GetBaseException().Message; }
        }

        void ConfirmSelection() {
            try {
                var candidate = selection.Confirm();
                if (candidate == null) return;
                selectionProblem = null;
                var snapping = choosingSnap;
                if (choosingSnap) {
                    // Preparing the snap binds it to the inspected object: queued like an edit, so no scan blocks.
                    var identity = selected.Identity; var confirmed = selection.Confirmed; var choice = candidate.Choice;
                    choosingSnap = false;
                    Queue(SnapPlace, "Snap target", pin => {
                        try { preparedSnap = snap.Prepare(identity, pin(selected), confirmed, choice); }
                        catch (Exception e) { feedback.Refuse(SnapPlace, "Snap target", e.GetBaseException().Message); }
                    });
                }
                if (candidate.Observation != null) {
                    InspectObservation(candidate.Observation);
                    ReadPose();
                }
                // A track confirmed on the Connections tab of a track marker is the track for its binding: the Segment
                // field takes it, so the field and the confirmation name the same track and Set applies what both show.
                // End and distance stay as entered. A crossing's fields take it per role, through Picked for.
                var picked = selection.Confirmed;
                if (!snapping && candidate.Observation == null && picked != null && picked.Identity.Kind == SelectionKind.TrackSegment && inspectorTab.Value == ConnectionsTab &&
                    selected != null && (selected.Identity.Structure == TrackMarkerScene.Structure || selected.Identity.Structure == CrossingPadScene.Structure) && picked.Source != null && picked.Source.Equals(SelectionIdentity.SceneObject(selected.Identity)))
                    binding[0] = picked.Identity.Identifier;
            } catch (Exception e) {
                selection.Pending?.Offer(Array.Empty<SelectionCandidate>());
                selectionProblem = "Identity could not be confirmed: " + e.GetBaseException().Message;
            }
        }
        // Editing needs a mutation adapter; the inventory marks each supported pattern with its structure.
        bool Editable => selected != null && (selected.Identity.Structure == IndependentBoards.Structure || selected.Identity.Structure == BridgeScene.Structure || selected.Identity.Structure == TrackMarkerScene.Structure || selected.Identity.Structure == CrossingCoverageScene.Structure || selected.Identity.Structure == CrossingPadScene.Structure) && selected.Handle is GameObject;
        // Why the inspected object cannot be edited now, or null. The band holds the pending selection and the active gizmo.
        string EditBlock() =>
            authoring == null ? (String.IsNullOrEmpty(authoringStatus) ? "Editing is unavailable." : authoringStatus)
            : selection?.Pending != null ? "Confirm or cancel the world selection to edit."
            : !Editable ? "Inspect an object with a fixed-placement adapter to edit it. Other observations are inspection only."
            : gizmo?.Active == true ? "Complete or cancel the gizmo to edit."
            : (GameObject)selected.Handle == null ? "The inspected object no longer exists. Refresh the inventory." : null;
        void ClearSnap() { preparedSnap = null; choosingSnap = false; preparedCoverage = null; }
        void BeginSnap(SelectionKind kind) {
            BeginSelection(kind, selected);
            choosingSnap = selection?.Pending != null;
        }
        void ReadPose() {
            if (selected == null || selected.Position.Count != 3 || selected.Rotation.Count != 4) return;
            var angles = new Quaternion((float)selected.Rotation[0], (float)selected.Rotation[1], (float)selected.Rotation[2], (float)selected.Rotation[3]).eulerAngles;
            var values = new[] { selected.Position[0], selected.Position[1], selected.Position[2], angles.x, angles.y, angles.z };
            for (var i = 0; i < 6; i++) pose[i] = ((float)values[i]).ToString("R", CultureInfo.InvariantCulture);
            ReadBinding();
        }
        // Fills the binding fields from the inspected marker location.
        void ReadBinding() {
            binding[0] = ""; binding[1] = "A"; binding[2] = "";
            var line = selected?.Bindings.FirstOrDefault(b => b.StartsWith("Marker segment=", StringComparison.Ordinal));
            if (line == null) return;
            foreach (var part in line.Substring("Marker ".Length).Split(new[] { "; " }, StringSplitOptions.None)) {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2) continue;
                if (pair[0] == "segment") binding[0] = pair[1];
                else if (pair[0] == "end") binding[1] = pair[1].ToUpperInvariant();
                else if (pair[0] == "distance") binding[2] = pair[1];
            }
        }
        // Every edit is bound to the inspected observation. A stale inventory refuses the edit instead of redirecting it.
        // An edit that does not run names its reason in the panel and the log, like one that is refused.
        void Edit(string place, string action, Func<AuthoringSession, InspectedTarget, Outcome> change) {
            Queue(place, action, pin => {
                // A completed edit changed the observed values; observe them again so the next edit binds to them.
                if (feedback.Run(place, action, null, inventory, selected, inspection => change(authoring.Session, inspection), pin)) { OwnChange(); RefreshAfterEdit(); }
            });
        }
        // Runs an edit or a preview for the object that is inspected now, once the snapshot is known to agree with the
        // scene. When it was compared since the last foreign change, that is the next frame. Otherwise the comparison
        // of the whole scene runs first as a stepped pass, a few milliseconds a frame, so the game keeps running, and
        // the message area says "Working..." meanwhile. A pass that finds a difference marks the inventory stale, and
        // the request is refused with that reason. Either way the pin it gets observes the target at every validation,
        // so a change of the target up to the mutation is refused.
        void Queue(string place, string action, Action<Func<SceneObservation, InspectedTarget>> run) {
            var blocked = selection?.Pending != null ? "a world selection is pending; confirm or cancel it first"
                : gizmo?.Active == true ? "the gizmo is active; complete or cancel it first"
                : authoring == null ? "editing is unavailable" : queuedEdit != null ? "another edit is still running" : null;
            if (blocked != null) { feedback.Run(place, action, blocked, inventory, selected, null); return; }
            ClearSnap();
            var inspected = selected;
            queuedPlace = place;
            queuedCompared = Compared();
            if (!queuedCompared) inventory?.CancelFreshnessCheck();
            queuedPasses = inventory == null ? 0 : inventory.CompletedPasses;
            queuedKey = ChangeStamp();
            queuedEdit = () => {
                if (!ReferenceEquals(inspected, selected)) { feedback.Refuse(place, action, "another object was inspected before the request ran"); return; }
                if (ChangeStamp() != queuedKey) { feedback.Refuse(place, action, "the editor's history changed while the scene was compared; select the action again"); return; }
                if (inventory == null) { feedback.Refuse(place, action, "map-editor inspection is unavailable"); return; }
                var passes = queuedPasses; var compared = queuedCompared;
                run(entry => compared ? inventory.PinCompared(entry) : inventory.PinAfterPass(entry, passes, true));
            };
        }
        // The preview compares the scene as an edit does: without a blocking scan.
        void PrepareCoverage(AuthoringSession session, TargetIdentity identity, string circuit, CircuitCoverage value, string addition) {
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
        // Fills the fields from the selected tracks of a circuit or addition; a circuit with native spans or detached, and a new
        // addition, start empty.
        void ReadCoverage(CircuitCoverage value) {
            for (var role = 0; role < 3; role++) {
                var range = value?.Ranges?[role];
                coverageFields[role * 4] = range?.From.Segment ?? "";
                coverageFields[role * 4 + 1] = range?.From.End ?? "a";
                coverageFields[role * 4 + 2] = range == null ? "" : range.From.Distance.ToString("R", CultureInfo.InvariantCulture);
                coverageFields[role * 4 + 3] = range == null ? "" : range.To.Distance.ToString("R", CultureInfo.InvariantCulture);
            }
        }
        CircuitCoverage ParseCoverage(out string problem, bool addition) {
            problem = null;
            var ranges = new SpanRange[3];
            for (var role = 0; role < 3; role++) {
                var segment = (coverageFields[role * 4] ?? "").Trim(); var end = (coverageFields[role * 4 + 1] ?? "").Trim().ToLowerInvariant();
                if (segment.Length == 0 || (end != "a" && end != "b") || !Double.TryParse(coverageFields[role * 4 + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var from) || !Double.TryParse(coverageFields[role * 4 + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out var to)) {
                    problem = "enter a segment ID, end a or b, and from and to distances with a decimal point for " + CircuitCoverage.Roles[role];
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
        Action queuedEdit;
        string queuedPlace;
        void CancelGizmo() {
            var pending = gizmo; gizmo = null;
            if (pending != null) pending.Cancel();
        }
        // Starting the gizmo binds it to the inspected object: queued like an edit, so no scan blocks.
        void BeginGizmo(UI.Gizmos.TransformGizmoMode mode) {
            CancelGizmo();
            Queue(GizmoPlace, "Gizmo", pin => StartGizmo(mode, pin));
        }
        void StartGizmo(UI.Gizmos.TransformGizmoMode mode, Func<SceneObservation, InspectedTarget> pin) {
            try {
                if (!Editable || selection?.Pending != null || authoring == null) throw new InvalidOperationException("Inspect a supported target and finish world selection first");
                var inspection = pin(selected);
                var root = new GameObject("CrankyAnt Scene Patcher transform gizmo");
                root.transform.SetParent(transform, false);
                gizmo = root.AddComponent<GizmoController>();
                gizmo.Initialize(engine, capabilities, authoring, selected.Identity, inspection, mode, (outcome, message) => {
                    gizmo = null; gizmoEnded = Time.frameCount;
                    feedback.Report(GizmoPlace, message ?? (outcome?.Applied == true ? "Gizmo placement completed. Use native Undo, Redo and Save." : "Gizmo placement refused: " + outcome?.Error));
                    OwnChange(); RefreshAfterEdit(); authoring?.RefreshChrome();
                });
            } catch (Exception e) { gizmo = null; feedback.Report(GizmoPlace, "Gizmo unavailable: " + e.GetBaseException().Message); }
        }
        void Inspect() {
            ClearSnap();
            try {
                if (inventory == null) throw new InvalidOperationException("Map-editor inspection is unavailable");
                inventory.Refresh();
                // A refresh read the whole scene: the snapshot is compared for the history as it is now.
                comparedKey = idleKey = ChangeStamp(); comparedAt = Time.unscaledTime;
                var inspectedBefore = selected != null;
                if (selected != null) selected = inventoryPresentation.CurrentSelection(inventory, selected);
                var lines = new List<string>();
                var documents = PackDocuments.Discover().ToArray();
                foreach (var pack in documents) {
                    if (pack.Error != null) { lines.Add(pack.Source.Identifier + ": " + pack.Error); continue; }
                    foreach (var item in inventory.InspectDocument(pack.Json))
                        lines.Add(pack.Source.Identifier + "/" + (item.Id ?? "document") + ": " + item.MatchCount + " inspection matches; dependsOn=" + String.Join(", ", item.Dependencies) + "; " + String.Join("; ", item.Problems));
                }
                documentInspection = String.Join("\n", lines);
                definitions.Read(documents);
                status = "Refreshed scene observations and supplied definitions without applying patches." + (inspectedBefore && selected == null ? " The inspected object is no longer present." : "");
            } catch (Exception e) { EditorDiagnostic(e.GetBaseException().Message); }
        }
        string Identity(SceneObservation entry) {
            var identity = new JObject {
                ["sourceMap"] = new JObject { ["mapId"] = PackDocuments.MapIds().First() },
                ["target"] = new JObject { ["scene"] = entry.Identity.Scene, ["path"] = entry.Identity.Path, ["component"] = entry.Identity.Component, ["structure"] = entry.Identity.Structure },
                ["placement"] = new JObject { ["mode"] = "fixed", ["position"] = new JArray(entry.Position), ["rotation"] = new JArray(entry.Rotation) }
            };
            if (entry.Identity.Structure == null) ((JObject)identity["target"]).Remove("structure");
            return identity.ToString();
        }
        // Focus needs the inspected object, not a fresh inventory: it moves the camera to where the object is now. It
        // never jumps to another object of the same path, because it uses the inspected handle.
        void Focus(SceneObservation entry) {
            try {
                if (!(entry.Handle is GameObject target) || target == null) throw new InvalidOperationException("the inspected object no longer exists; select Refresh and inspect it again");
                JumpTo((Vector3)Native.Call(Native.Type("Helpers.WorldTransformer"), "WorldToGame", target.transform.position));
            } catch (Exception e) { feedback.Report("Focus", "Focus refused: " + e.GetBaseException().Message); }
        }
        static void JumpTo(Vector3 position) {
            var type = Native.Type("CameraSelector");
            var selector = Native.Read(type, "shared");
            var strategy = Enum.Parse(type.GetNestedType("CameraIdentifier", Native.Flags), "Strategy");
            Native.Call(selector, "JumpToPoint", position, Quaternion.identity, strategy);
        }
    }
}
