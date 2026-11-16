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
        // The retained state belongs to the active map and layer identifiers in the native map editor or in play. It is a scoped
        // implementation key, not a verified world identity; each load attempt checks every object against its identity.
        static object MapContext() => Tuple.Create(String.Join("\n", ModDocuments.MapIds()), ModDocuments.IsMapEditor);
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
        // The patch documents the last refresh read, and whether they changed outside scene patch editing since.
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
        object refusedMapEditor;
        string editingStatus = "";
        // The last edit results, shown in the message area above the action slots and written to the log. A place is the
        // action group a message came from; the message area shows it as the message's source.
        readonly EditFeedback feedback = NewFeedback();
        // The footer's actions and the commands of the band and the outliner are the same on every tab: their results
        // stay through a tab switch.
        static EditFeedback NewFeedback() {
            var created = new EditFeedback();
            foreach (var place in new[] { ModificationPlace, "Copy ID", "Fit" }) created.GeneralPlaces.Add(place);
            return created;
        }
        const string BindingPlace = "Track binding", PlacementPlace = "Placement", GizmoPlace = "Gizmo", SnapPlace = "Snap", ActivityPlace = "Activity",
            ModificationPlace = "Modification", TerrainPlace = "Terrain output", CoveragePlace = "Crossing coverage";
        bool staleLogged = true;
        NativeTrackPoses trackPoses;
        float nextTrackFollow;
        const string FollowPlace = "Follow";
        readonly string[] placementFields = { "", "", "", "", "", "" };
        readonly string[] binding = { "", "a", "" };
        // Crossing coverage being edited: the circuit, four fields per role (segment, end, from, to) and the previewed change.
        // coverageAddition is the ID of the detection addition being added or edited, or null while a native circuit is edited.
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
                try { if (attachment != null) outstanding = " Outstanding: " + attachment.Outstanding(); } catch { }
                context?.Log.Info(CommandBand.LogLine(feedback.Place, selected?.Identity.Path, message) + outstanding);
            };
#if ISSUE39_OBSERVATION
            JourneyObservation.Sink = message => Debug.Log("[Issue39Attempt] " + message);
#endif
            try {
                inMapEditor = ModDocuments.IsMapEditor;
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
            context = value; inMapEditor = ModDocuments.IsMapEditor;
            native = new NativeInventory(g => scene == null ? "Independent board capability unavailable" : scene.Refusal(g));
            capabilities = new SceneCapabilities(ModDocuments.MapIds, native);
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
                foreach (var mod in ModDocuments.Discover()) {
                    if (!mod.Applicable) continue;
                    var result = runtime.BeforeNativeCheck(mod.Json, mod.Source, identity);
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
            try { documents = runtime.ApplyLoaded(ModDocuments.Discover(), overlapNotices ?? (overlapNotices = new NativeOverlapNotices(this))); }
            catch (Exception e) { Report("discovery", "Loaded mod discovery failed. Report to the patcher maintainer. " + e.GetBaseException().Message); return; }
            // One line per document per load attempt says what happened to it, also when nothing else is reported: a
            // document for another map is skipped without a failure.
            loadAttempts++;
            string[] maps = null;
            try { maps = ModDocuments.MapIds(); } catch { }
            foreach (var document in documents) {
                var mod = document.Mod;
                try { context?.Log.Info(document.LogLine(Version, loadAttempts, maps)); } catch { }
                // Follow-up work for one document cannot stop the others; their application has already happened in load order.
                try {
                    if (document.PresentationError != null) context?.Log.Info(Describe(mod, document.PresentationError));
                    if (document.Result == null) {
                        if (mod.Error != null) MapEditorDiagnostic(Describe(mod, mod.Error));
                        continue;
                    }
                    var result = document.Result;
                    if (result.Error != null) {
                        if (result.MapEditorOnly) MapEditorDiagnostic(Describe(mod, result.Error));
                        continue;
                    }
                    foreach (var item in result.Outcomes) {
                        if (!item.Value.Applied) {
                            var error = Describe(mod, item.Key + ": " + item.Value.Error);
                            if (item.Value.MapEditorOnly) MapEditorDiagnostic(error);
                        }
                        else ReconcileMarkerWarning(mod, item.Key, item.Value);
                    }
                } catch (Exception e) { context?.Log.Info(Describe(mod, "Follow-up after application failed: " + e.GetBaseException().Message)); }
            }
            status = "Documents processed. " + (scene?.Status ?? "");
        }
        NativeOverlapNotices overlapNotices;
        // The load attempts of this world so far; the log lines of one attempt carry its number.
        int loadAttempts;
        sealed class NativeOverlapNotices : IOverlapNotices {
            readonly PatcherBehaviour owner;
            internal NativeOverlapNotices(PatcherBehaviour owner) { this.owner = owner; }
            public void Notice(OverlapNotice notice) {
                owner.context?.Log.Info("Scene Patcher overlap notice: " + notice.Message);
                owner.pendingNotices[notice.Key] = notice.Message;
                owner.FlushWarnings();
            }
        }
        void ReconcileMarkerWarning(LoadedMod mod, string operation, Outcome outcome) {
            if (markerWorld == null || !(outcome.ResolvedTarget is GameObject target) || target == null) return;
            var document = JObject.Parse(mod.Json);
            var entry = (JObject)document["modifications"][operation];
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
                    return ModDocuments.MapIds().Contains((string)document["sourceMap"]["mapId"], StringComparer.Ordinal) && actual != null && actual.Segment == (string)binding["segment"] && actual.End == (string)binding["end"] && Math.Abs(actual.Distance - (double)binding["distance"]) <= 0.001 && markerWorld.DependentProblem(target) == null;
                })) context?.Log.Info("Scene Patcher reconciled one exactly attributed current marker warning after verified repair. Earlier route unavailable: " + attribution.EarlyRouteUnavailable);
            }
        }
        static string Describe(LoadedMod loaded, string message) {
            var mod = loaded.Source;
            return (mod.Name ?? mod.Identifier) + " [" + mod.Identifier + " " + mod.Version + "] " + message + ". Patcher " + Version + ". Author: " + (mod.Author ?? "not supplied") + (String.IsNullOrWhiteSpace(mod.Website) ? "" : "; website: " + mod.Website);
        }
        internal void ReportWorld(string message) { Report("world", message); }
        void MapEditorDiagnostic(string message) { if (inMapEditor && !diagnostics.Contains(message)) diagnostics.Add(message); }
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
                attachment.Session.Replayed = identity => replayed.Add(identity);
                // The patcher's own Save writes the patch document: that text is what the definitions on disk now are, so it
                // is no change from outside and the inventory lamp stays Current.
                ModDocuments.Written = text => { try { definitions.Written(ModDocuments.EditedModId(), text); } catch { } };
                filled = false;
                snap = new SnapController(attachment.Session, capabilities, new NativeSnapScene(native));
                attachment.Log = message => context?.Log.Info(message);
                editingStatus = attachment.Session.Blocked ?? "Editing ready for " + attachment.PatchPath;
                // The native map editor built its title before this session could account for the document.
                attachment.RefreshChrome();
            } catch (Exception e) { refusedMapEditor = found; editingStatus = "Editing unavailable: " + e.GetBaseException().Message; }
        }
        void Detach() {
            ClearSnap(); snap = null;
            CancelGizmo();
            // A request made in this native map editor does not run in the next one.
            CancelRequest("the native map editor was left before the request ran");
            ModDocuments.Written = null;
            if (attachment == null) return;
            if (ReferenceEquals(Hooks.Current, attachment)) Hooks.Current = null;
            attachment.Dispose(); attachment = null;
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
        void Update() {
            FlushWarnings();
            if (!Initialized || engine == null) return;
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
                    if (updated != null) { var inspected = ReferenceEquals(selected, entry); KeepSelection(entry, updated); if (inspected) ReadPlacement(); }
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
            // Escape cancels a pending world choice or an active gizmo and is consumed, as in the native map editor modes, so
            // it does not also open the pause menu. The gizmo also ends through the native map editor's own Escape route, which
            // can run before this handler in the same frame: an Escape in the frame a gizmo ended is consumed as well.
            // While a field of the panel has the keyboard the patcher takes no Escape at all: the handler is not even
            // registered, so the key reaches the field and the game as without the mod.
            var escapable = !(presentation != null && presentation.FieldFocused) && (selection?.Pending != null || gizmo?.Active == true || Time.frameCount <= gizmoEnded + 1 || ClearsOnEscape());
            if (escapable != escapeHeld) try {
                if (escapable) UI.GameInput.RegisterEscapeHandler(UI.GameInput.EscapeHandler.Transient, () => {
                    // One press does one thing (CommandBand.Escape): the list beside the click closes alone; over the
                    // panel Escape does what Clear does; elsewhere it cancels a pending choice or a gizmo drag.
                    switch (CommandBand.Escape(presentation != null && presentation.FieldFocused, selection?.Pending?.ListOpen == true, PointerOverPanel(), selected != null, selection?.Pending != null, gizmo?.Active == true)) {
                        case CommandBand.EscapeStep.CloseList: selection.Pending.CloseList(); selectionProblem = null; presentation?.RequestRefresh(); return true;
                        case CommandBand.EscapeStep.Clear: ClearInspection(); presentation?.RequestRefresh(); return true;
                        case CommandBand.EscapeStep.CancelChoice: selection.Cancel(); selectionProblem = null; ClearSnap(); presentation?.RequestRefresh(); return true;
                        case CommandBand.EscapeStep.CancelDrag: gizmo.Cancel(); presentation?.RequestRefresh(); return true;
                    }
                    return Time.frameCount <= gizmoEnded + 1;
                });
                else UI.GameInput.UnregisterEscapeHandler(UI.GameInput.EscapeHandler.Transient);
                escapeHeld = escapable;
            } catch (Exception e) { escapeHeld = escapable; context?.Log.Info("Escape handling for the world choice and the gizmo is unavailable: " + e.GetBaseException().Message); }
            try { Attach(); }
            catch (Exception e) { editingStatus = "Editing unavailable: " + e.GetBaseException().Message; }
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
                    try { definitions.Compare(ModDocuments.Discover()); }
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
                    var snapshot = PresentationSnapshot();
                    feedback.Inspecting(selected?.Handle);
                    MarkFollowed();
                    MarkBound();
                    ArmGizmo();
                    ShowLivePlacement();
                    LogChoiceProblem();
                    // A result belongs to the tab and placement mode it was reported in.
                    feedback.View(inspectorTab.Value + "/" + (inspectorTab.Value == PlacementTab ? placementMode : 0));
                    // The help of a tab belongs to that tab: it closes when another tab opens.
                    if (CommandBand.ClosesHelp(presentation.HelpRegion, inspectorTab.Value, TabIds)) presentation.CloseHelp();
                    var inventoryChanged = inventoryPresentation.Observe(inventory, selected, filter, outlinerFilter, ChangeStamp());
                    if (snapshot != presentationSnapshot || inventoryChanged) { presentationSnapshot = snapshot; presentation.RequestRefresh(); }
                }
                presentation.Tick(); SyncInputRegions();
            }
        }
        // Objects of the edited document that follow a track move with it, a few times a second. No document or history changes.
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
        // Differs whenever the set of objects the document changes can differ.
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
            return String.Join("|", inspectorTab.Value, presentation?.HelpRegion, candidateDetails, gizmoTool, Requests.Place, status, scene?.Status, inventory?.IsStale, inventory?.StaleReason, definitionsStale, editingStatus, feedback.Place, feedback.Result,
                documentInspection, String.Join("\n", diagnostics), selectionProblem, inputBlocker?.WorldInputProblem,
                pending?.Purpose, pending?.Source?.Label, pending?.Problem, pending?.Candidate?.Label,
                pending == null ? "" : String.Join("\n", pending.Offers.Select(o => o.Label)),
                selection?.Confirmed?.Identity, preparedSnap?.Identity, choosingSnap, gizmo?.State, outstanding, TerrainSnapshot(),
                coverageCircuit, preparedCoverage == null ? "" : String.Join("\n", preparedCoverage.Preview.Lines) + preparedCoverage.Preview.Error);
        }
        // Computing the outstanding work serializes the document and reads the dependency metadata. Its inputs change
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
            reported.Clear(); diagnostics.Clear(); pendingWarnings.Clear(); pendingNotices.Clear();
            loadId = Guid.NewGuid().ToString("N"); presentationErrorLogged = false;
            if (inventory != null) { inventory = new SceneInventory(native); staleLogged = true; }
            selected = null; selectionProblem = null; documentInspection = "";
            // The panel's state belongs to the load attempt as well: what it read, compared, reported and had open. The
            // next native map editor session builds a new window and fills it from the scene as it is then.
            definitions.Reset();
            comparedKey = null; passKey = null; idleKey = null; urgentCheck = false; filled = false; fullRefresh = false;
            terrainFor = null; revealed = null;
            CloseCoverageForm();
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
            Requests.Cancel("the native map editor was left before the request ran"); refreshFrame = 0; replayFrame = 0; refreshTarget = null;
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
            // A choice without a source object only inspects what it confirms; no patch document names it.
            try {
                selection.Begin(kind, origin, origin == null && kind == SelectionKind.SceneObject); selectionProblem = null;
                // The refusal of an earlier action, such as a Set, does not stand beside the new choice.
                if (selection.Pending != null) feedback.ChoiceStarted();
            }
            catch (Exception e) { selectionProblem = "World selection unavailable: " + e.GetBaseException().Message; }
        }
        // Whether Escape does what Clear does now: the pointer is over the open panel while an object is inspected or
        // a world choice is pending. Escape elsewhere cancels a choice or the gizmo, or opens the pause menu.
        bool ClearsOnEscape() => CommandBand.EscapeClears(PointerOverPanel(), selected != null, selection?.Pending != null);
        bool PointerOverPanel() {
            if (presentation == null || !presentation.IsOpen) return false;
            var pointer = Input.mousePosition;
            return presentation.PanelBounds.Contains(new Vector2(pointer.x, Screen.height - pointer.y));
        }
        // Nothing is inspected any more: what was prepared, open or reported for the object goes with it. A pending
        // world choice is cancelled as Cancel does, so the native map editor takes the world's clicks again. A request that
        // still waited is refused, and that refusal stays to be read.
        void ClearInspection() {
            var waited = Requests.ReleaseInspection(selection?.Pending != null, CancelSelection,
                () => { CancelGizmo(); ClearSnap(); CloseCoverageForm(); selected = null; }, "the inspection was cleared before the request ran");
            if (waited) inventory?.CancelFreshnessCheck();
            presentation?.RequestRefresh();
        }
        // The world marks the track the inspected object follows in green, as a confirmed relation, also while another
        // track is being chosen. The followed track is read from the document when the inspection or the native map editor's
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
        void MarkBound() {
            if (selection == null) return;
            var shown = selected != null && inspectorTab.Value == ConnectionsTab;
            var key = shown ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(selected) + "|" + ChangeStamp() : null;
            if (key == boundKey) return;
            boundKey = key;
            var bound = new List<SelectionIdentity>();
            if (shown) try {
                bound.AddRange(Segments(selected).Select(id => SelectionIdentity.Graph(SelectionKind.TrackSegment, id)));
                if (attachment != null && selected.Identity.Structure == CrossingCoverageScene.Structure)
                    foreach (var circuit in attachment.Session.Coverage(selected.Identity).Circuits) {
                        if (circuit.Value.Ranges != null) foreach (var range in circuit.Value.Ranges) { bound.Add(SelectionIdentity.Graph(SelectionKind.TrackSegment, range.From.Segment)); bound.Add(SelectionIdentity.Graph(SelectionKind.TrackSegment, range.To.Segment)); }
                        // A native circuit names its spans; each span's two track locations name the tracks.
                        if (circuit.Value.SpanIds != null && inventory != null) foreach (var span in circuit.Value.SpanIds) {
                            var entry = inventory.Entries.FirstOrDefault(e => e.Bindings.Contains("Span id=" + span));
                            if (entry != null) bound.AddRange(Segments(entry).Select(id => SelectionIdentity.Graph(SelectionKind.TrackSegment, id)));
                        }
                    }
            } catch { }
            selection.Binds(shown ? selected.Handle : null, bound.Where(id => !String.IsNullOrWhiteSpace(id.Identifier)).Distinct().ToArray());
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
            var wanted = selected != null && inspectorTab.Value == PlacementTab && placementMode == CommandBand.GizmoMode && !IsPad && selection?.Pending == null && Editable && attachment != null && (GameObject)selected.Handle != null;
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
            try {
                var candidate = selection.Confirm();
                // A refused confirmation says why in the footer, beside Confirm, and in the log (LogChoiceProblem).
                if (candidate == null) { presentation?.RequestRefresh(); return; }
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
                    ReadPlacement();
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
            attachment == null ? (String.IsNullOrEmpty(editingStatus) ? "Editing is unavailable." : editingStatus)
            : selection?.Pending != null ? "Confirm or cancel the world selection to edit."
            : !Editable ? CommandBand.EditReason(selected?.Operations)
            : gizmo?.Active == true ? "Complete or cancel the gizmo to edit."
            : (GameObject)selected.Handle == null ? "The inspected object no longer exists. Refresh the inventory." : null;
        void ClearSnap() { preparedSnap = null; choosingSnap = false; preparedCoverage = null; }
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
        void Edit(string place, string action, Func<PatchEditingSession, InspectedTarget, Outcome> change, bool consumes = false) {
            Queue(place, action, pin => {
                // A completed edit changed the observed values; observe them again so the next edit binds to them.
                var truthful = IsPad && (place == PlacementPlace || place == SnapPlace || place == FollowPlace) ? CommandBand.PadPlacement(action) : null;
                if (feedback.Run(place, action, null, inventory, selected, inspection => change(attachment.Session, inspection), pin, truthful)) {
                    if (consumes) selection?.Consume();
                    OwnChange(); RefreshAfterEdit();
                }
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
                : attachment == null ? "editing is unavailable" : Requests.Pending ? "another edit is still running" : null;
            if (blocked != null) { feedback.Run(place, action, blocked, inventory, selected, null); return; }
            ClearSnap();
            var inspected = selected;
            queuedCompared = Compared();
            if (!queuedCompared) inventory?.CancelFreshnessCheck();
            queuedPasses = inventory == null ? 0 : inventory.CompletedPasses;
            queuedKey = ChangeStamp();
            Requests.Enqueue(place, action, () => {
                if (!ReferenceEquals(inspected, selected)) { feedback.Refuse(place, action, "another object was inspected before the request ran"); return; }
                if (ChangeStamp() != queuedKey) { feedback.Refuse(place, action, "the editor's history changed while the scene was compared; select the action again"); return; }
                if (inventory == null) { feedback.Refuse(place, action, "map-editor inspection is unavailable"); return; }
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
            if (!Requests.Cancel(reason)) return;
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
                    feedback.Report(GizmoPlace, message ?? (outcome?.Applied == true ? (outcome.Unchanged ? "Gizmo released without a move; nothing changed." : IsPad ? CommandBand.PadPlacement("Gizmo placement") : "Gizmo placement completed.") : "Gizmo placement refused: " + outcome?.Error));
                    OwnChange(); RefreshAfterEdit(); attachment?.RefreshChrome();
                });
            } catch (Exception e) { gizmo = null; feedback.Report(GizmoPlace, "Gizmo unavailable: " + e.GetBaseException().Message); }
        }
        // A crossing pad's geometry is generated along its track binding: a placement moves the pad object and nothing
        // the player sees, and its result says so.
        bool IsPad => selected != null && selected.Identity.Structure == CrossingPadScene.Structure;
        void Inspect() {
            ClearSnap();
            try {
                if (inventory == null) throw new InvalidOperationException("Inspection is unavailable");
                inventory.Refresh();
                // A refresh read the whole scene: the snapshot is compared for the history as it is now.
                comparedKey = idleKey = ChangeStamp(); comparedAt = Time.unscaledTime;
                var inspectedBefore = selected != null;
                if (selected != null) selected = inventoryPresentation.CurrentSelection(inventory, selected);
                var lines = new List<string>();
                var documents = ModDocuments.Discover().ToArray();
                foreach (var mod in documents) {
                    if (mod.Error != null) { lines.Add(mod.Source.Identifier + ": " + mod.Error); continue; }
                    foreach (var item in inventory.InspectDocument(mod.Json))
                        lines.Add(mod.Source.Identifier + "/" + (item.Id ?? "document") + ": " + item.MatchCount + " inspection matches; dependsOn=" + String.Join(", ", item.Dependencies) + "; " + String.Join("; ", item.Problems));
                }
                documentInspection = String.Join("\n", lines);
                definitions.Read(documents);
                status = "Refreshed scene observations and supplied definitions without applying patches." + (inspectedBefore && selected == null ? " The inspected object is no longer present." : "");
            } catch (Exception e) { MapEditorDiagnostic(e.GetBaseException().Message); }
        }
        string Identity(SceneObservation entry) {
            var identity = new JObject {
                ["sourceMap"] = new JObject { ["mapId"] = ModDocuments.MapIds().First() },
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
                var map = NativeInventory.MapPosition(target.transform);
                var point = FocusTarget.Point(entry.Family, new double[] { map.x, map.y, map.z },
                    entry.Family == FocusTarget.Span && trackPoses != null ? trackPoses.SpanPositions(target) : null, out var refusal);
                if (point == null) throw new InvalidOperationException(refusal);
                JumpTo(new Vector3((float)point[0], (float)point[1], (float)point[2]));
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
