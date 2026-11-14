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
        GameObject host;
        IModContext context;
        LocalWorldBinding<PatcherBehaviour> world;
        public void OnModLoad(IModContext value) {
            context = value; Hooks.Install(); NativeRepairHooks.Install(this);
            if (NativeRepairHooks.Error != null) context.Log.Info(NativeRepairHooks.Error + ". Native marker checks and their warnings remain active.");
        }
        public void OnWorldLoad() {
            if (world == null) world = new LocalWorldBinding<PatcherBehaviour>(StartWorld, _ => ReleaseHost());
            object key;
            try { key = WorldIdentity(); }
            catch (Exception e) { EnsureHost().ReportWorld("Local world context unavailable: " + e.GetBaseException().Message + ". Report to the patcher maintainer."); return; }
            // An equal key reconciles the retained world; a changed key releases this peer's state and starts a new load attempt.
            if (!world.Enter(key)) world.State?.ApplyDocuments();
        }
        // A host created early by a native marker check during this world load becomes the bound state.
        PatcherBehaviour StartWorld() {
            var behaviour = EnsureHost();
            if (behaviour.Initialized) behaviour.ApplyDocuments(); else behaviour.Initialize(context);
            return behaviour;
        }
        // Scoped implementation key for this peer's local world, not a verified world
        // identity. An equal key keeps the binding; any changed component is treated as a
        // new load attempt. Native retained same-map behavior under this key is unverified.
        static object WorldIdentity() {
            var maps = String.Join("\n", PackDocuments.MapIds());
            var manager = Native.Read(Native.Type("Map.Runtime.MapManager"), "Instance") ?? throw new InvalidOperationException("Map manager unavailable");
            return Tuple.Create(maps, manager, CurrentGraph());
        }
        // Including the track graph is an unverified assumption: a replaced graph
        // reference does not establish a genuinely new world.
        static object CurrentGraph() {
            var graphType = Native.Type("Track.Graph");
            if (graphType == null) return null;
            try { return Native.Read(graphType, "Shared"); } catch (MissingMemberException) { return null; }
        }
        PatcherBehaviour EnsureHost() {
            if (host == null) { host = new GameObject("CrankyAnt Scene Patcher"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<PatcherBehaviour>(); }
            return host.GetComponent<PatcherBehaviour>();
        }
        internal string BeforeMarkerCheck(Track.TrackMarker marker) {
            try { return EnsureHost().BeforeMarkerCheck(context, marker); }
            catch (Exception e) { return "Early capability prerequisites unavailable: " + e.GetBaseException().Message; }
        }
        public void OnWorldUnload() { Cleanup(); }
        public void OnModUnload() { Cleanup(); Hooks.Remove(); NativeRepairHooks.Remove(); }
        void Cleanup() {
            NativeRepairHooks.Reset();
            world?.Release();
            ReleaseHost();
        }
        void ReleaseHost() {
            if (host == null) return;
            host.GetComponent<PatcherBehaviour>().Release();
            UnityEngine.Object.Destroy(host); host = null;
        }
    }
    public sealed class PatcherBehaviour : MonoBehaviour {
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
        string documentStamp;
        bool definitionsStale = true;
        float nextInventoryCheck, nextDefinitionCheck;
        // The share of one frame that the open panel's scene comparison may take. One unit of the scan can exceed it.
        const double InventoryCheckBudgetMs = 3;
        readonly System.Diagnostics.Stopwatch inventoryCheckClock = new System.Diagnostics.Stopwatch();
        bool editor;
        string status = "Initializing", inspected = "", filter = "";
        readonly HashSet<string> reported = new HashSet<string>();
        readonly List<string> diagnostics = new List<string>();
        NativeEditorPresentation presentation;
        Texture2D worldLabelBacking;
        object attachedEditor;
        string presentationSnapshot;
        readonly InventoryPresentationState inventoryPresentation = new InventoryPresentationState();
        readonly UIOutlineViewState inventoryOutline = new UIOutlineViewState();
        internal const string Version = "0.5.0";
        Authoring authoring;
        object refusedEditor;
        string authoringStatus = "";
        // The last edit result, shown in the panel section of its control and written to the log.
        readonly EditFeedback feedback = new EditFeedback();
        const string BindingPlace = "binding", PlacementPlace = "placement", InteractionPlace = "interaction", CoveragePlace = "coverage";
        bool staleLogged = true;
        NativeTrackPoses trackPoses;
        float nextTrackFollow;
        const string FollowPlace = "follow";
        readonly string[] pose = { "", "", "", "", "", "" };
        readonly string[] binding = { "", "a", "" };
        // Crossing coverage being edited: the circuit, four fields per role (segment, end, from, to) and the previewed change.
        // coverageAddition is the ID of the authored addition being added or edited, or null while a native circuit is edited.
        string coverageCircuit, coverageAddition, newAddition = "";
        readonly string[] coverageFields = new string[12];
        sealed class PreparedCoverage { internal string Circuit, Addition; internal CircuitCoverage Value; internal CoveragePreview Preview; }
        PreparedCoverage preparedCoverage;
        readonly string loadId = Guid.NewGuid().ToString("N");
        internal void Initialize(IModContext value) {
            context = value;
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
                    selection = new SpatialSelection(native);
                }
                if (editor) {
                    try { inputBlocker = gameObject.AddComponent<InspectionInputBlocker>(); inputBlocker.Initialize(); inputBlocker.RefreshRegions = SyncInputRegions; }
                    catch (Exception e) {
                        inputBlocker?.Release(); inputBlocker = null;
                        Report("inspection-input", "Inspection UI unavailable: " + e.GetBaseException().Message);
                    }
                }
                // Each capability checks its own game functions; an incompatible one fails only the modifications naming its structure.
                if (scene != null) capabilities.Add(scene);
                else capabilities.AddUnavailable(IndependentBoards.Structure, status);
                try { capabilities.Add(new BridgeScene(new BridgeWorld(), Capability.Unchecked)); }
                catch (Exception e) {
                    capabilities.AddUnavailable(BridgeScene.Structure, e.GetBaseException().Message);
                    context.Log.Info("Scene Patcher " + Version + ": bridge capability incompatible or unavailable: " + e.GetBaseException().Message);
                }
                try { capabilities.Add(new CrossingPadScene(new CrossingPadWorld(), Capability.Unchecked)); }
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
        void PrepareRuntime(IModContext value) {
            if (engine != null) return;
            context = value; editor = PackDocuments.IsEditor;
            native = new NativeInventory(g => scene == null ? "Independent board capability unavailable" : scene.Refusal(g));
            capabilities = new SceneCapabilities(PackDocuments.MapIds, native);
            try { markerWorld = new TrackMarkerWorld(); capabilities.Add(new TrackMarkerScene(markerWorld, Capability.Unchecked)); }
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
                try { presentation = new NativeEditorPresentation(DrawWindow, () => { CancelGizmo(); selection?.Cancel(); selectionProblem = null; ClearSnap(); }); }
                catch (Exception e) { Report("inspection-presentation", "Inspection UI unavailable: " + e.GetBaseException().Message); }
            }
            if (Hooks.Error != null) { refusedEditor = found; authoringStatus = Hooks.Error; return; }
            try {
                authoring = new Authoring(engine, found); Hooks.Current = authoring;
                snap = new SnapController(authoring.Session, capabilities, new NativeSnapScene(native));
                authoring.Log = message => context?.Log.Info(message);
                authoringStatus = authoring.Session.Blocked ?? "Authoring ready for " + authoring.PatchPath;
                // The editor built its title before this session could account for the document.
                authoring.RefreshChrome();
            } catch (Exception e) { refusedEditor = found; authoringStatus = "Authoring unavailable: " + e.GetBaseException().Message; }
        }
        void Detach() {
            ClearSnap(); snap = null;
            CancelGizmo();
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
        void Update() {
            FlushWarnings();
            if (!Initialized || engine == null) return;
            try { Attach(); }
            catch (Exception e) { authoringStatus = "Authoring unavailable: " + e.GetBaseException().Message; }
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
                // The scene comparison runs a few milliseconds per frame; the next pass starts two seconds after one ends.
                if (inventory.CheckInProgress || Time.unscaledTime >= nextInventoryCheck) {
                    inventoryCheckClock.Restart();
                    inventory.AdvanceFreshnessCheck(() => inventoryCheckClock.Elapsed.TotalMilliseconds < InventoryCheckBudgetMs);
                    if (!inventory.CheckInProgress) nextInventoryCheck = Time.unscaledTime + 2f;
                }
                if (Time.unscaledTime >= nextDefinitionCheck) {
                    nextDefinitionCheck = Time.unscaledTime + 2f;
                    try { if (documentStamp != DocumentStamp(PackDocuments.Discover())) definitionsStale = true; }
                    catch { definitionsStale = true; }
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
                var snapshot = PresentationSnapshot();
                var inventoryChanged = inventoryPresentation.Observe(inventory, selected, filter);
                if (snapshot != presentationSnapshot || inventoryChanged) { presentationSnapshot = snapshot; presentation.RequestRefresh(); }
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
                    if (item.Value.Applied) context?.Log.Info("Scene Patcher: " + item.Key + " followed its track");
                    else if (reported.Add("follow/" + item.Key + "/" + item.Value.Error)) {
                        diagnostics.Add(item.Key + " could not follow its track: " + item.Value.Error);
                        context?.Log.Info("Scene Patcher: " + item.Key + " could not follow its track: " + item.Value.Error);
                    }
            } catch (Exception e) { if (reported.Add("follow/" + e.GetBaseException().Message)) context?.Log.Info("Scene Patcher: track following stopped: " + e.GetBaseException().Message); }
        }
        string PresentationSnapshot() {
            var pending = selection?.Pending;
            string outstanding = "";
            try {
                if (authoring != null) {
                    var session = authoring.Session;
                    outstanding = String.Join(", ", session.Outstanding(authoring.NativeDirty)) + "|" +
                        session.LastSave?.Error + "|" + (session.LastSave == null ? "" : String.Join(", ", session.LastSave.Completed)) + "|" +
                        (session.LastSave == null ? "" : String.Join(", ", session.LastSave.Outstanding)) + "|" +
                        session.IncompleteOperation + "|" + authoring.Fault;
                }
            } catch (Exception e) { outstanding = e.GetBaseException().Message; }
            return String.Join("|", status, scene?.Status, inventory?.IsStale, inventory?.StaleReason, definitionsStale, authoringStatus, feedback.Place, feedback.Result,
                documentInspection, String.Join("\n", diagnostics), selectionProblem, inputBlocker?.WorldInputProblem,
                pending?.Purpose, pending?.Source?.Label, pending?.Problem, pending?.Candidate?.Label,
                pending == null ? "" : String.Join("\n", pending.Offers.Select(o => o.Label)),
                selection?.Confirmed?.Identity, preparedSnap?.Identity, choosingSnap, gizmo?.State, outstanding, TerrainSnapshot(),
                coverageCircuit, preparedCoverage == null ? "" : String.Join("\n", preparedCoverage.Preview.Lines) + preparedCoverage.Preview.Error);
        }
        string TerrainSnapshot() {
            if (authoring == null || selected == null || !Editable) return "";
            var output = OutputOf(authoring.Session, selected.Identity, out var problem);
            return problem ?? String.Join("|", String.Join(";", output.Completed), String.Join(";", output.Outstanding), output.Error);
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
                    worldLabelBacking.SetPixel(0, 0, new Color(.11f, .12f, .14f, 1)); worldLabelBacking.Apply();
                }
                selection.Draw(selected, presentation.WorldUiBounds(), worldLabelBacking);
            }
            catch (Exception e) { selectionProblem = "World markings unavailable: " + e.GetBaseException().Message; }
        }
        IConfigurableElement Button(UIPanelBuilder builder, string label, Action action) {
            var button = builder.AddButtonCompact(label, presentation.Action(action));
            foreach (var text in button.RectTransform.GetComponentsInChildren<TMP_Text>(true)) text.richText = false;
            return button;
        }
        void PlainLabel(UIPanelBuilder builder, string value) => builder.AddLabel(value, text => text.richText = false);
        void DrawWindow(UIPanelBuilder builder) {
            PlainLabel(builder, status);
            DrawSelection(builder);
            if (scene != null) PlainLabel(builder, scene.Status);
            builder.AddSection("Inventory");
            PlainLabel(builder, InventoryPresentationState.Notice(inventory, definitionsStale));
            PlainLabel(builder, "Search names, paths, families, binding identifiers, dependencies or problems.");
            UIPanel results = null;
            builder.AddFilterField(filter, value => { filter = value; results?.SetNeedsRebuild(); }, null, true);
            Button(builder, "Refresh inventory and verify stored target identities", Inspect);
            if (inventory != null) {
                PlainLabel(builder, inventory.Entries.Count + " observed entries. Active and inactive loaded objects are included.");
                results = builder.AddRebuildableSection(items => {
                        var indices = inventory.Entries.Select((entry, index) => new { entry, index })
                            .ToDictionary(pair => pair.entry, pair => pair.index.ToString(CultureInfo.InvariantCulture));
                        var rows = new List<UIOutlineItem>();
                        string selectedKey = null;
                        foreach (var item in inventory.Search(filter)) {
                            var key = indices[item];
                            if (ReferenceEquals(selected, item)) selectedKey = key;
                            var targetIdentity = new JObject {
                                ["scene"] = item.Identity.Scene, ["path"] = item.Identity.Path,
                                ["component"] = item.Identity.Component, ["structure"] = item.Identity.Structure
                            };
                            var label = "Inspect " + item.Family + ": " + item.Identity.Path + (item.Active ? "" : " [inactive]");
                            rows.Add(new UIOutlineItem(key, label.Replace("<", "<noparse><</noparse>"), item) {
                                Tooltip = targetIdentity.ToString().Replace("<", "\\u003c").Replace(">", "\\u003e")
                            });
                        }
                        var byKey = rows.ToDictionary(row => row.Identifier);
                        var outline = items.AddOutlineView(rows, inventoryOutline, new UIState<string>(selectedKey), key => {
                            if (!byKey.TryGetValue(key, out var row)) return;
                            presentation.Action(() => {
                                var observation = (SceneObservation)row.Value;
                                if (!inventoryPresentation.CanSelect(inventory, observation)) return;
                                CancelGizmo(); ClearSnap(); coverageCircuit = coverageAddition = null; selected = inventoryPresentation.CurrentSelection(inventory, observation); inspected = selected == null ? "Inspected object no longer present" : DescribeInspection(selected); ReadPose();
                            })();
                        });
                        var listLayout = outline.GetComponent<LayoutElement>();
                        listLayout.minHeight = 180; listLayout.preferredHeight = 180; listLayout.flexibleHeight = 0;
                });
            }
            if (selected != null) {
                PlainLabel(builder, inspected);
                Button(builder, "Focus inspected object", () => Focus(selected));
                Button(builder, "Copy inspected identity", () => GUIUtility.systemCopyBuffer = Identity(selected));
            }
            PlainLabel(builder, documentInspection);
            foreach (var diagnostic in diagnostics) PlainLabel(builder, diagnostic);
            DrawAuthoring(builder);
            builder.AddSection("Guidance");
            foreach (var coverage in NativeInventory.Coverage) PlainLabel(builder, coverage);
            PlainLabel(builder, "Binding validity does not establish correct placement or intended track. Inspect the world before editing.");
            Button(builder, "Inventory and dependency guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/inspect-scene-objects.md"));
            Button(builder, "World selection guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/select-world-targets.md"));
            Button(builder, "Transform gizmo guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/using-transform-gizmos.md"));
            Button(builder, "Fixed-placement guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/authoring-fixed-placement.md"));
            Button(builder, "Board and terrain guidance", () => Application.OpenURL(BoardGuide));
            Button(builder, "Crossing coverage guidance", () => Application.OpenURL(CoverageGuide));
            Button(builder, "Document v1 reference", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/scene-patch-document-v1.md"));
        }
        const string BoardGuide = "https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/moving-boards.md";
        void DrawSelection(UIPanelBuilder builder) {
            if (selection == null) return;
            builder.AddSection("World selection");
            var pending = selection.Pending;
            var confirmed = selection.Confirmed;
            if (pending == null) {
                Button(builder, "Select scene object in world", () => BeginSelection(SelectionKind.SceneObject, null));
                if (selected != null) {
                    Button(builder, "Choose replacement object", () => BeginSelection(SelectionKind.SceneObject, selected));
                    Button(builder, "Choose replacement segment", () => BeginSelection(SelectionKind.TrackSegment, selected));
                    Button(builder, "Choose replacement node", () => BeginSelection(SelectionKind.TrackNode, selected));
                }
            } else {
                var candidate = selection.Find(pending.Candidate);
                PlainLabel(builder, (choosingSnap ? "Select physical snap target" : pending.Purpose) + ". Click a marking or track in the world.");
                if (pending.Source != null) PlainLabel(builder, "Source: " + pending.Source.Label);
                if (pending.Problem != null) PlainLabel(builder, pending.Problem);
                if (pending.Offers.Count > 1) {
                    foreach (var offer in pending.Offers) {
                        if (ReferenceEquals(offer, pending.Candidate)) continue;
                        var choice = offer;
                        Button(builder, "Choose " + choice.Label, () => pending.Choose(choice));
                    }
                }
                if (candidate != null) {
                    PlainLabel(builder, "Candidate: " + candidate.Choice.Label);
                    PlainLabel(builder, candidate.Choice.Identity.ToString());
                    PlainLabel(builder, candidate.Details);
                    Button(builder, "Focus candidate", () => {
                        try { JumpTo(candidate.Position); }
                        catch (Exception e) { selectionProblem = "Camera focus unavailable: " + e.GetBaseException().Message; }
                    });
                    if (candidate.Observation != null) Button(builder, "Inspect candidate", () => {
                        CancelGizmo(); selected = candidate.Observation; inspected = DescribeInspection(selected); Inspect(); ReadPose();
                    });
                    Button(builder, "Confirm identity", ConfirmSelection);
                }
                Button(builder, "Cancel selection", () => { selection.Cancel(); selectionProblem = null; ClearSnap(); });
            }
            if (confirmed != null) PlainLabel(builder, "Confirmed: " + confirmed.Identity + " in " + confirmed.MapIds[0]);
            if (inputBlocker.WorldInputProblem != null && pending != null) PlainLabel(builder, inputBlocker.WorldInputProblem);
            if (selectionProblem != null) PlainLabel(builder, selectionProblem);
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
                if (choosingSnap) {
                    preparedSnap = snap.Prepare(selected.Identity, inventory.Pin(selected), selection.Confirmed, candidate.Choice);
                    choosingSnap = false;
                }
                if (candidate.Observation != null) {
                    selected = candidate.Observation;
                    inspected = DescribeInspection(selected);
                    Inspect();
                    ReadPose();
                }
            } catch (Exception e) {
                selection.Pending?.Offer(Array.Empty<SelectionCandidate>());
                selectionProblem = "Identity could not be confirmed: " + e.GetBaseException().Message;
            }
        }
        // Editing needs a mutation adapter; the inventory marks each supported pattern with its structure.
        bool Editable => selected != null && (selected.Identity.Structure == IndependentBoards.Structure || selected.Identity.Structure == BridgeScene.Structure || selected.Identity.Structure == TrackMarkerScene.Structure || selected.Identity.Structure == CrossingCoverageScene.Structure || selected.Identity.Structure == CrossingPadScene.Structure) && selected.Handle is GameObject;
        void ClearSnap() { preparedSnap = null; choosingSnap = false; preparedCoverage = null; }
        void BeginSnap(SelectionKind kind) {
            BeginSelection(kind, selected);
            choosingSnap = selection?.Pending != null;
        }
        void DrawSnap(UIPanelBuilder builder) {
            if (snap == null) return;
            PlainLabel(builder, "Snap changes position once and keeps rotation and logical binding. Select a track or endpoint, verify its ID, then apply. Save records fixed map coordinates.");
            Button(builder, "Choose snap track", () => BeginSnap(SelectionKind.TrackSegment));
            Button(builder, "Choose snap endpoint", () => BeginSnap(SelectionKind.TrackNode));
            if (preparedSnap != null) {
                PlainLabel(builder, "Physical snap target: " + preparedSnap.Identity + ". Resulting map position: " +
                    String.Join(", ", preparedSnap.Placement.Position.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
                Button(builder, "Apply physical snap", () => {
                    var request = preparedSnap;
                    Edit(PlacementPlace, "Physical snap", (session, inspection) => snap.Apply(request));
                });
                Button(builder, "Cancel prepared snap", ClearSnap);
            }
            Button(builder, "Snapping guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/snapping-to-track.md"));
            DrawFollow(builder);
        }
        // Track-following placement: the object keeps its pose relative to an explicitly selected track or endpoint.
        void DrawFollow(UIPanelBuilder builder) {
            if (authoring == null) return;
            builder.AddSection("Track following");
            var identity = selected.Identity;
            if (trackPoses == null) { PlainLabel(builder, "Track-following placement is unavailable in this game build."); return; }
            var following = authoring.Session.FollowingOf(identity);
            PlainLabel(builder, following == null
                ? "Following keeps this object's current position and rotation relative to a selected track or endpoint, and moves it whenever that track moves. It does not change a logical binding or activity."
                : "Following " + following.Anchor + " at offset " + String.Join(", ", following.Offset.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))) + " in the track's frame. Placement edits keep following it with a new offset.");
            Button(builder, "Choose track to follow", () => BeginSelection(SelectionKind.TrackSegment, selected));
            Button(builder, "Choose endpoint to follow", () => BeginSelection(SelectionKind.TrackNode, selected));
            var confirmed = selection?.Confirmed;
            var usable = confirmed != null && confirmed.Identity.Kind != SelectionKind.SceneObject && (confirmed.Source == null || confirmed.Source.Equals(SelectionIdentity.SceneObject(identity)));
            if (usable) Button(builder, "Follow confirmed " + confirmed.Identity, () => {
                var maps = confirmed.MapIds.ToArray();
                Edit(FollowPlace, "Track following", (current, inspection) => {
                    if (!maps.SequenceEqual(capabilities.MapIds)) throw new InvalidOperationException("Map context changed; select the track again");
                    var anchor = confirmed.Identity.Kind == SelectionKind.TrackNode ? TrackAnchor.Endpoint(confirmed.Identity.Identifier)
                        : TrackAnchor.At(trackPoses.Nearest(confirmed.Identity.Identifier, ((GameObject)selected.Handle).transform.position));
                    return current.Follow(identity, anchor, inspection);
                });
            });
            if (following != null) Button(builder, "Stop following (keep current pose)", () => Edit(FollowPlace, "Stop following", (current, inspection) => current.StopFollowing(identity, inspection)));
            if (feedback.Place == FollowPlace) PlainLabel(builder, feedback.Result);
            Button(builder, "Track following guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/following-track.md"));
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
            binding[0] = ""; binding[1] = "a"; binding[2] = "";
            var line = selected?.Bindings.FirstOrDefault(b => b.StartsWith("Marker segment=", StringComparison.Ordinal));
            if (line == null) return;
            foreach (var part in line.Substring("Marker ".Length).Split(new[] { "; " }, StringSplitOptions.None)) {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2) continue;
                if (pair[0] == "segment") binding[0] = pair[1];
                else if (pair[0] == "end") binding[1] = pair[1].ToLowerInvariant();
                else if (pair[0] == "distance") binding[2] = pair[1];
            }
        }
        // Every edit is bound to the inspected observation. A stale inventory refuses the edit instead of redirecting it.
        // An edit that does not run names its reason in the panel and the log, like one that is refused.
        void Edit(string place, string action, Func<AuthoringSession, InspectedTarget, Outcome> change) {
            var blocked = selection?.Pending != null ? "a world selection is pending; confirm or cancel it first"
                : gizmo?.Active == true ? "the gizmo is active; complete or cancel it first"
                : authoring == null ? "authoring is unavailable" : null;
            if (blocked == null) ClearSnap();
            // A completed edit changed the observed values; refresh so the next edit binds to the new observation.
            if (feedback.Run(blocked == null ? place : InteractionPlace, action, blocked, inventory, selected, inspection => change(authoring.Session, inspection))) { Inspect(); ReadPose(); }
        }
        void DrawAuthoring(UIPanelBuilder builder) {
            builder.AddSection("Authoring");
            PlainLabel(builder, authoringStatus);
            if (authoring == null) return;
            var session = authoring.Session;
            try {
                var outstanding = session.Outstanding(authoring.NativeDirty);
                PlainLabel(builder, outstanding.Count == 0 ? "All required writes are saved." : "Unsaved: " + String.Join(", ", outstanding) + ". Use the editor's Save button.");
            } catch (Exception e) { PlainLabel(builder, "Outstanding work unknown: " + e.GetBaseException().Message); }
            if (session.LastSave != null && session.LastSave.Error != null) PlainLabel(builder, "Last Save failed: " + session.LastSave.Error + " Completed: " + (session.LastSave.Completed.Count == 0 ? "none" : String.Join(", ", session.LastSave.Completed)) + ". Outstanding: " + String.Join(", ", session.LastSave.Outstanding) + ". Edits remain in memory; correct the cause and Save again.");
            if (session.IncompleteOperation != null) PlainLabel(builder, "Incomplete operation: " + session.IncompleteOperation);
            if (authoring.Fault != null) PlainLabel(builder, authoring.Fault);
            if (selection?.Pending != null) {
                PlainLabel(builder, "Confirm or cancel the world selection to edit placement or activity.");
                if (feedback.Place == InteractionPlace) PlainLabel(builder, feedback.Result);
                return;
            }
            if (!Editable) { PlainLabel(builder, "Inspect an object with a fixed-placement adapter to edit it. Other observations are inspection only."); return; }
            if (gizmo?.Active == true) {
                PlainLabel(builder, gizmo.State);
                if (feedback.Place == InteractionPlace) PlainLabel(builder, feedback.Result);
                Button(builder, "Cancel gizmo", CancelGizmo);
                return;
            }
            var target = (GameObject)selected.Handle;
            if (target == null) { PlainLabel(builder, "The inspected object no longer exists. Refresh the inventory."); return; }
            if (selected.Identity.Structure == CrossingCoverageScene.Structure) { DrawCoverage(builder, session, selected.Identity); return; }
            if (selected.Identity.Structure == TrackMarkerScene.Structure || selected.Identity.Structure == CrossingPadScene.Structure) DrawBinding(builder, selected.Identity);
            DrawSnap(builder);
            if (selected.Identity.Structure == BridgeScene.Structure) {
                PlainLabel(builder, "Bridge placement moves the bridge root with its colliders and culling. Generated track trestles, track geometry and construction scenery stay where they are; this is not a track binding.");
                Button(builder, "Bridge placement guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/placing-bridges.md"));
            }
            if (selected.Bindings.Any(b => b.IndexOf("follows track group", StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf("targetActiveFollowsTrackGroup=True", StringComparison.OrdinalIgnoreCase) >= 0))
                PlainLabel(builder, "This object's activity follows its track group. Disable holds it inactive even when the group is enabled later; Clear authored activity returns it to the group. An explicit enable is refused.");
            PlainLabel(builder, "Selected: " + selected.Identity.Path + (target.activeSelf ? "" : " (disabled)") + ". Position in map coordinates; rotation in degrees. After Undo, Redo or a native edit, refresh the inventory before the next edit.");
            Button(builder, "Move with gizmo", () => BeginGizmo(UI.Gizmos.TransformGizmoMode.Move));
            Button(builder, "Rotate with gizmo", () => BeginGizmo(UI.Gizmos.TransformGizmoMode.Rotate));
            var labels = new[] { "X", "Y", "Z", "Rot X", "Rot Y", "Rot Z" };
            for (var row = 0; row < 2; row++) {
                var first = row * 3;
                builder.HStack(fields => {
                    for (var column = 0; column < 3; column++) {
                        var index = first + column;
                        fields.VStack(field => {
                            field.AddLabel(labels[index]);
                            var input = field.AddInputField(pose[index], value => pose[index] = value);
                            input.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => pose[index] = value);
                            var layout = input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
                            layout.minWidth = 70; layout.flexibleWidth = 1;
                        }).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                    }
                });
            }
            var identity = selected.Identity;
            Button(builder, "Apply placement", () => {
                var values = new float[6]; var valid = true;
                for (var i = 0; i < 6; i++) valid &= Single.TryParse(pose[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
                if (!valid) feedback.Refuse(PlacementPlace, "Placement", "enter six numbers with a decimal point");
                else {
                    var rotation = Quaternion.Euler(values[3], values[4], values[5]);
                    var placement = new Placement(new double[] { values[0], values[1], values[2] }, new double[] { rotation.x, rotation.y, rotation.z, rotation.w });
                    Edit(PlacementPlace, "Placement", (current, inspection) => current.Edit(identity, placement, null, inspection));
                }
            });
            Button(builder, "Read inspected values", ReadPose);
            Button(builder, target.activeSelf ? "Disable object" : "Enable object", () => { var enable = !target.activeSelf; Edit(PlacementPlace, "Activity change", (current, inspection) => current.Edit(identity, null, enable, inspection)); });
            Button(builder, "Clear authored activity", () => Edit(PlacementPlace, "Clearing the authored activity", (current, inspection) => current.ClearActivity(identity, inspection)));
            Button(builder, "Remove modification", () => Edit(PlacementPlace, "Removal", (current, inspection) => current.Remove(identity, inspection)));
            Button(builder, "Recheck after failed edit", () => Edit(PlacementPlace, "Recheck", (current, inspection) => current.Recheck(identity, inspection)));
            Button(builder, "Authoring guidance", () => Application.OpenURL("https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/editing-placement-in-the-editor.md"));
            if (feedback.Place == PlacementPlace) PlainLabel(builder, feedback.Result);
            DrawOutput(builder, session, identity);
            PlainLabel(builder, "Removal returns the object to its state without this MapMod's modification. Save writes map data, Data/scene-patches.json and the patcher dependency in Info.json, and never reloads. Reloading the editor ends Undo/Redo history.");
        }
        const string PadGuide = "https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/rebinding-crossing-pads.md";
        const string BindingGuide = "https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/repairing-track-bindings.md";
        // The marker's track relationship. A binding edit registers the marker at the new location and never moves the object.
        void DrawBinding(UIPanelBuilder builder, TargetIdentity identity) {
            var pad = identity.Structure == CrossingPadScene.Structure;
            builder.AddSection(pad ? "Crossing pad binding" : "Track binding");
            PlainLabel(builder, pad
                ? "The binding is the track location this pad's marker is registered at. Setting or replacing it regenerates the pad geometry along the track at that location and moves its culling with it; the pad object does not move. A pad cannot be detached: disable it to hide it."
                : "The binding is the track location this marker is registered at. Setting, replacing or detaching it does not move the object; placement below stays a separate setting. Mileposts and crossings keep their own operations.");
            foreach (var line in selected.Bindings.Where(b => b.StartsWith("Marker", StringComparison.Ordinal) || b.StartsWith("Graph registration", StringComparison.Ordinal))) PlainLabel(builder, "Current: " + line);
            var labels = new[] { "Segment", "End (a or b)", "Distance (m)" };
            builder.HStack(fields => {
                for (var column = 0; column < 3; column++) {
                    var index = column;
                    fields.VStack(field => {
                        field.AddLabel(labels[index]);
                        var input = field.AddInputField(binding[index], value => binding[index] = value);
                        input.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => binding[index] = value);
                        var layout = input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
                        layout.minWidth = 70; layout.flexibleWidth = 1;
                    }).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                }
            });
            var confirmed = selection?.Confirmed?.Identity;
            Button(builder, "Use confirmed segment", () => { if (confirmed != null && confirmed.Kind == SelectionKind.TrackSegment) binding[0] = confirmed.Identifier; presentation.RequestRefresh(); })
                .Disable(confirmed == null || confirmed.Kind != SelectionKind.TrackSegment);
            Button(builder, "Set track binding", () => {
                var end = binding[1].Trim().ToLowerInvariant();
                if (String.IsNullOrWhiteSpace(binding[0]) || (end != "a" && end != "b") || !Double.TryParse(binding[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var distance))
                    feedback.Refuse(BindingPlace, "Track binding", "enter a segment ID, end a or b, and a distance with a decimal point");
                else {
                    var value = MarkerBinding.At(new TrackLocation(binding[0].Trim(), end, distance));
                    Edit(BindingPlace, "Track binding", (current, inspection) => current.Bind(identity, value, inspection));
                }
            });
            if (!pad) Button(builder, "Detach from track", () => Edit(BindingPlace, "Track detachment", (current, inspection) => current.Bind(identity, MarkerBinding.Detached, inspection)));
            Button(builder, "Clear authored binding", () => Edit(BindingPlace, "Clearing the authored binding", (current, inspection) => current.Bind(identity, null, inspection)));
            if (feedback.Place == BindingPlace) PlainLabel(builder, feedback.Result);
            Button(builder, pad ? "Crossing pad guidance" : "Track binding guidance", () => Application.OpenURL(pad ? PadGuide : BindingGuide));
            builder.AddSection("Placement and activity");
        }
        const string CoverageGuide = "https://github.com/CrankyAnt/RailroaderScenePatcher/blob/main/docs/guides/scene-patches/correcting-crossing-coverage.md";
        // The crossing's detection coverage by circuit. Each change is previewed for every circuit before it is applied; the
        // crossing object never moves, and proximity never assigns coverage.
        void DrawCoverage(UIPanelBuilder builder, AuthoringSession session, TargetIdentity identity) {
            builder.AddSection("Crossing coverage");
            PlainLabel(builder, "Each circuit has an approach A, island and approach B span. Select the tracks a circuit covers, or detach it, explicitly; the patcher never assigns coverage by proximity. The native spans stay registered and unchanged, and circuits you do not edit keep their coverage. To make the crossing detect an extra track, add detection for it: the patcher adds a circuit with its own three spans for the tracks you select. Preview a change to see its effect on every circuit, then apply it.");
            CrossingCoverage current = null;
            try { current = session.Coverage(identity); }
            catch (Exception e) { PlainLabel(builder, "Coverage unavailable: " + e.GetBaseException().Message); }
            if (current != null) {
                foreach (var circuit in current.Circuits) {
                    var key = circuit.Key; var value = circuit.Value;
                    var name = DetectionAddition.Name(key);
                    PlainLabel(builder, name + ": " + value + ".");
                    Button(builder, "Edit " + name, () => { coverageCircuit = key; coverageAddition = DetectionAddition.Id(key); preparedCoverage = null; ReadCoverage(value); presentation.RequestRefresh(); });
                }
                if (current.Residue.Count > 0) PlainLabel(builder, "Owned spans left by an incomplete cleanup: " + String.Join(", ", current.Residue) + ". Use Recheck after failed edit.");
                // An addition starts from an ID the modder chooses and tracks the modder selects; nothing is taken from nearby track.
                PlainLabel(builder, "Add detection for extra tracks. Give the addition an ID that is unique in this MapMod's patch document, then select its tracks.");
                var idInput = builder.AddInputField(newAddition ?? "", value => newAddition = value);
                idInput.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => newAddition = value);
                Button(builder, "Add detection with this ID", () => {
                    var id = (newAddition ?? "").Trim();
                    if (id.Length == 0) { feedback.Refuse(CoveragePlace, "Detection addition", "give the addition an ID first"); presentation.RequestRefresh(); return; }
                    var existing = current.Circuits.FirstOrDefault(c => c.Value.Kind == CircuitKind.Added && DetectionAddition.Id(c.Key) == id);
                    coverageAddition = id; coverageCircuit = existing.Key ?? DetectionAddition.Key("", id); preparedCoverage = null; ReadCoverage(existing.Value); presentation.RequestRefresh();
                });
            }
            if (coverageCircuit != null) {
                PlainLabel(builder, "Editing " + (coverageAddition == null ? "circuit " + coverageCircuit : "added detection " + coverageAddition) + ". Distances are metres from the chosen end. From is the end next to the island for each approach and island lower for the island.");
                var confirmed = selection?.Confirmed?.Identity;
                var labels = new[] { "Segment", "End (a or b)", "From (m)", "To (m)" };
                for (var role = 0; role < 3; role++) {
                    var first = role * 4; var roleName = CircuitCoverage.Roles[role];
                    PlainLabel(builder, roleName);
                    builder.HStack(fields => {
                        for (var column = 0; column < 4; column++) {
                            var index = first + column; var label = labels[column];
                            fields.VStack(field => {
                                field.AddLabel(label);
                                var input = field.AddInputField(coverageFields[index] ?? "", value => { coverageFields[index] = value; preparedCoverage = null; });
                                input.GetComponent<TMP_InputField>().onValueChanged.AddListener(value => { coverageFields[index] = value; preparedCoverage = null; });
                                var layout = input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
                                layout.minWidth = 60; layout.flexibleWidth = 1;
                            }).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                        }
                    });
                    Button(builder, "Use confirmed segment for " + roleName, () => { if (confirmed != null && confirmed.Kind == SelectionKind.TrackSegment) { coverageFields[first] = confirmed.Identifier; preparedCoverage = null; } presentation.RequestRefresh(); })
                        .Disable(confirmed == null || confirmed.Kind != SelectionKind.TrackSegment);
                }
                Button(builder, "Choose track in world", () => BeginSelection(SelectionKind.TrackSegment, selected));
                var circuitKey = coverageCircuit; var additionId = coverageAddition;
                Button(builder, "Preview selected tracks", () => {
                    var value = ParseCoverage(out var problem, additionId != null);
                    if (value == null) feedback.Refuse(CoveragePlace, "Coverage preview", problem);
                    else PrepareCoverage(session, identity, circuitKey, value, additionId);
                });
                // A crossing keeps at least one covered circuit; the preview and the edit refuse the last detach or deletion as well.
                var lastCovered = CrossingCoverage.LastCovered(current, circuitKey);
                if (additionId != null) {
                    var authored = current != null && current.Circuits.Any(c => c.Value.Kind == CircuitKind.Added && DetectionAddition.Id(c.Key) == additionId);
                    if (lastCovered) PlainLabel(builder, "Added detection " + additionId + " is the crossing's last detection. Deleting it would leave the crossing without detection, so the patcher refuses it; return a native circuit to its coverage first.");
                    Button(builder, "Preview deleting the addition", () => PrepareCoverage(session, identity, circuitKey, null, additionId)).Disable(!authored || lastCovered);
                } else {
                    if (lastCovered) PlainLabel(builder, "Circuit " + circuitKey + " is the crossing's last covered circuit. Detaching it would leave the crossing without detection, so the patcher refuses it; select tracks for it instead, or keep another circuit covered.");
                    Button(builder, "Preview detach", () => PrepareCoverage(session, identity, circuitKey, CircuitCoverage.Detached, null)).Disable(lastCovered);
                    Button(builder, "Preview clearing the authored circuit", () => PrepareCoverage(session, identity, circuitKey, null, null));
                }
                if (preparedCoverage != null && preparedCoverage.Circuit == coverageCircuit) {
                    if (preparedCoverage.Preview.Error != null) PlainLabel(builder, "Preview refused: " + preparedCoverage.Preview.Error);
                    else {
                        PlainLabel(builder, "Intended crossing relationship:");
                        foreach (var line in preparedCoverage.Preview.Lines) PlainLabel(builder, line);
                        var prepared = preparedCoverage;
                        Button(builder, "Apply previewed coverage", () => Edit(CoveragePlace, "Crossing coverage", (current2, inspection) => prepared.Addition == null ? current2.Cover(identity, prepared.Circuit, prepared.Value, inspection)
                            : prepared.Value == null ? current2.DeleteDetection(identity, prepared.Addition, inspection) : current2.AddDetection(identity, prepared.Addition, prepared.Value, inspection)));
                    }
                    Button(builder, "Cancel preview", () => { preparedCoverage = null; presentation.RequestRefresh(); });
                }
            }
            if (feedback.Place == CoveragePlace) PlainLabel(builder, feedback.Result);
            Button(builder, "Remove modification", () => Edit(CoveragePlace, "Removal", (current2, inspection) => current2.Remove(identity, inspection)));
            Button(builder, "Recheck after failed edit", () => Edit(CoveragePlace, "Recheck", (current2, inspection) => current2.Recheck(identity, inspection)));
            Button(builder, "Crossing coverage guidance", () => Application.OpenURL(CoverageGuide));
            PlainLabel(builder, "Removal returns every circuit to its coverage without this MapMod's modification and deletes its added detection. Save writes Data/scene-patches.json and the patcher dependency; the game rebuilds the selected coverage from the document on every load. Reloading the editor ends Undo/Redo history.");
        }
        void PrepareCoverage(AuthoringSession session, TargetIdentity identity, string circuit, CircuitCoverage value, string addition) {
            CoveragePreview preview;
            try { preview = addition == null ? session.PreviewCoverage(identity, circuit, value, inventory.Pin(selected)) : session.PreviewAddition(identity, addition, value, inventory.Pin(selected)); }
            catch (Exception e) { feedback.Refuse(CoveragePlace, "Coverage preview", e.GetBaseException().Message); preparedCoverage = null; return; }
            preparedCoverage = new PreparedCoverage { Circuit = circuit, Addition = addition, Value = value, Preview = preview };
            presentation.RequestRefresh();
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
        // Terrain output follows a board edit asynchronously; the game regenerates it, and Save does not write it.
        void DrawOutput(UIPanelBuilder builder, AuthoringSession session, TargetIdentity identity) {
            var output = OutputOf(session, identity, out var problem);
            if (problem != null) { PlainLabel(builder, "Terrain output unknown: " + problem); return; }
            if (output.Completed.Count == 0 && output.Settled) return;
            PlainLabel(builder, output.Error != null ? "Terrain output stopped: " + output.Error + ". Completed: " + Join(output.Completed) + ". Outstanding: " + Join(output.Outstanding) + ". The board edit is kept."
                : output.Settled ? "Terrain output complete: " + Join(output.Completed) + "."
                : "Terrain output outstanding; the game is regenerating it: " + Join(output.Outstanding) + ". Completed: " + Join(output.Completed) + ".");
            PlainLabel(builder, "Save does not write the terrain output generated from the board's mask: it writes the board's placement, and the game regenerates that output from it on every load. Native edited terrain tiles are saved separately and are not changed by this edit.");
            Button(builder, "Retry terrain output", () => {
                var outcome = session.RetryOutput(identity);
                feedback.Report(PlacementPlace, outcome.Applied ? "Terrain output requested again." : "Terrain output retry refused: " + outcome.Error);
            }).Disable(output.Outstanding.Count == 0);
            Button(builder, "Board and terrain guidance", () => Application.OpenURL(BoardGuide));
        }
        static DependentOutput OutputOf(AuthoringSession session, TargetIdentity identity, out string problem) {
            problem = null;
            try { return session.Output(identity); }
            catch (Exception e) { problem = e.GetBaseException().Message; return DependentOutput.None; }
        }
        void CancelGizmo() {
            var pending = gizmo; gizmo = null;
            if (pending != null) pending.Cancel();
        }
        void BeginGizmo(UI.Gizmos.TransformGizmoMode mode) {
            CancelGizmo();
            ClearSnap();
            try {
                if (!Editable || selection?.Pending != null || authoring == null) throw new InvalidOperationException("Inspect a supported target and finish world selection first");
                var inspection = inventory.Pin(selected);
                var root = new GameObject("CrankyAnt Scene Patcher transform gizmo");
                root.transform.SetParent(transform, false);
                gizmo = root.AddComponent<GizmoController>();
                gizmo.Initialize(engine, capabilities, authoring, selected.Identity, inspection, mode, (outcome, message) => {
                    gizmo = null;
                    feedback.Report(PlacementPlace, message ?? (outcome?.Applied == true ? "Gizmo placement completed. Use native Undo, Redo and Save." : "Gizmo placement refused: " + outcome?.Error));
                    Inspect(); ReadPose(); authoring?.RefreshChrome();
                });
            } catch (Exception e) { gizmo = null; feedback.Report(PlacementPlace, "Gizmo unavailable: " + e.GetBaseException().Message); }
        }
        void Inspect() {
            ClearSnap();
            try {
                if (inventory == null) throw new InvalidOperationException("Map-editor inspection is unavailable");
                inventory.Refresh();
                if (selected != null) {
                    selected = inventoryPresentation.CurrentSelection(inventory, selected);
                    inspected = selected == null ? "Inspected object no longer present" : DescribeInspection(selected);
                }
                var lines = new List<string>();
                var documents = PackDocuments.Discover().ToArray();
                foreach (var pack in documents) {
                    if (pack.Error != null) { lines.Add(pack.Source.Identifier + ": " + pack.Error); continue; }
                    foreach (var item in inventory.InspectDocument(pack.Json))
                        lines.Add(pack.Source.Identifier + "/" + (item.Id ?? "document") + ": " + item.MatchCount + " inspection matches; dependsOn=" + String.Join(", ", item.Dependencies) + "; " + String.Join("; ", item.Problems));
                }
                documentInspection = String.Join("\n", lines);
                documentStamp = DocumentStamp(documents); definitionsStale = false;
                status = "Refreshed scene observations and supplied definitions without applying patches.";
            } catch (Exception e) { EditorDiagnostic(e.GetBaseException().Message); }
        }
        static string DocumentStamp(IEnumerable<LoadedPack> documents) => new JArray(documents.Select(p => new JArray(p.Source.Identifier, p.Json, p.Error))).ToString(Newtonsoft.Json.Formatting.None);
        string Identity(SceneObservation entry) {
            var identity = new JObject {
                ["sourceMap"] = new JObject { ["mapId"] = PackDocuments.MapIds().First() },
                ["target"] = new JObject { ["scene"] = entry.Identity.Scene, ["path"] = entry.Identity.Path, ["component"] = entry.Identity.Component, ["structure"] = entry.Identity.Structure },
                ["placement"] = new JObject { ["mode"] = "fixed", ["position"] = new JArray(entry.Position), ["rotation"] = new JArray(entry.Rotation) }
            };
            if (entry.Identity.Structure == null) ((JObject)identity["target"]).Remove("structure");
            return identity.ToString();
        }
        string DescribeInspection(SceneObservation entry) => Identity(entry) + "\n" + entry.Family + "; active in hierarchy=" + entry.Active + "\n" + entry.Operations +
            "\nBindings:\n" + String.Join("\n", entry.Bindings) + "\nDependencies:\n" + String.Join("\n", entry.Dependencies) +
            "\nDetected problems:\n" + (entry.Problems.Count == 0 ? "None detected. Placement and intended binding require world inspection." : String.Join("\n", entry.Problems));
        void Focus(SceneObservation entry) {
            try {
                // Never focus a replacement with the same path. Refresh retains the selected handle.
                var current = native.Discover().Where(e => ReferenceEquals(e.Handle, entry.Handle) && e.Identity.Component == entry.Identity.Component).ToArray();
                if (current.Length != 1 || !entry.Matches(current[0])) throw new InvalidOperationException("Inspected object changed; refresh and inspect again");
                if (entry.Position.Count != 3) throw new InvalidOperationException("Map coordinates unavailable for this observation");
                var position = new Vector3((float)entry.Position[0], (float)entry.Position[1], (float)entry.Position[2]);
                JumpTo(position);
            } catch (Exception e) { EditorDiagnostic("Camera focus unavailable: " + e.GetBaseException().Message); }
        }
        static void JumpTo(Vector3 position) {
            var type = Native.Type("CameraSelector");
            var selector = Native.Read(type, "shared");
            var strategy = Enum.Parse(type.GetNestedType("CameraIdentifier", Native.Flags), "Strategy");
            Native.Call(selector, "JumpToPoint", position, Quaternion.identity, strategy);
        }
    }
}
