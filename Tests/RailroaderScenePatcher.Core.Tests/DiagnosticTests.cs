// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

static class DiagnosticTests {
    sealed class Messages : IPatchDiagnostics {
        public readonly List<PatchFailure> Rows = new List<PatchFailure>();
        public void Warning(PatchFailure failure) { Rows.Add(failure); }
    }
    sealed class Warnings : ICurrentWarnings {
        public readonly List<CurrentWarning> Rows = new List<CurrentWarning>();
        public IEnumerable<CurrentWarning> Current => Rows;
        public void Remove(CurrentWarning row) { Rows.Remove(row); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    const string Document = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"move\",\"target\":{\"scene\":\"scene\",\"path\":\"sign\",\"component\":\"board\"},\"dependsOn\":[],\"active\":false}]}";
    public static void Run() {
        var scene = new Scene(); var box = new Box(); scene.Objects["sign"] = new List<object> { box };
        scene.FailApply = true;
        var messages = new Messages();
        var runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.5.0", "game-build");
        var source = new PatchSource("example/mapmod", "Example MapMod", "2.1", "Example author", "https://example.com/mapmod");
        runtime.Apply(Document, source);
        runtime.Apply(Document, source);
        Check(messages.Rows.Count == 1, "Repeated callbacks must retain one attributed failure per operation within a load");
        var failure = messages.Rows.Single();
        Check(failure.Source == "Railroader Scene Patcher" && failure.Operation == "move" && failure.ObjectPath == "sign", "Diagnostic source, operation and affected object must identify the failure");
        Check(failure.Message.StartsWith("Example MapMod [example/mapmod; version 2.1].", StringComparison.Ordinal), "The description starts with the supplying MapMod; its separate originator supplies the diagnostic source once");
        Check(failure.Cause == FailureCause.Unknown && failure.ReportTo == "patcher maintainer", "An injected native mutation fault must not blame the supplying MapMod");
        Check(failure.Message.Contains("Example MapMod") && failure.Message.Contains("example/mapmod") && failure.Message.Contains("2.1") && failure.Message.Contains("0.5.0") && failure.Message.Contains("game-build"), "Reports must include supplying identity and relevant versions");
        Check(failure.Message.Contains("Example author") && failure.Message.Contains("https://example.com/mapmod"), "Reports retain supplied metadata");
        InvalidDefinitionsAndEditorRecovery();
        EarlyCheckUsesCompleteDocumentProtection();
        ExactFallbackRequiresVerifiedRepair();
        DocumentAndOperationDiagnosticsAreDistinct();
        IsolatedFailuresKeepIndependentWork();
        UnavailableEarlyPrerequisiteDefersWithoutFailingTheLoad();
        GroupActivityRefusalKeepsAccurateDiagnostics();
        RemoveRepairRestoresTheNativeSerializedReference();
        Console.WriteLine("PASS: runtime diagnostics, early repair, exact fallback attribution and within-load deduplication");
    }
    public enum NativeEnd { A, B }
    public sealed class NativeSegment { public string Id; }
    public sealed class NativeLocation {
        public NativeLocation(NativeSegment segment, float distance, NativeEnd end) { Segment = segment; Distance = distance; End = end; }
        public NativeSegment Segment; public float Distance; public NativeEnd End;
    }
    public struct SerializedLocation { public string segmentId; public float distance; public NativeEnd end; }
    public sealed class NativeMarker {
        public SerializedLocation _location;
        public Action OnLocationChanged = null;
        public int Invalidations;
        public void InvalidateCache() { Invalidations++; }
        public NativeLocation Location {
            get { return null; }
            set { _location = value == null ? default(SerializedLocation) : new SerializedLocation { segmentId = value.Segment.Id, distance = value.Distance, end = value.End }; InvalidateCache(); OnLocationChanged?.Invoke(); }
        }
    }
    sealed class SerializedWorld : FakeMarkerWorld, ITrackMarkerWorld, ITrackMarkerDiagnostics {
        public readonly Warnings Messages = new Warnings();
        readonly RailroaderScenePatcher.NativeMarkerWarnings warningHistory = new RailroaderScenePatcher.NativeMarkerWarnings();
        public readonly NativeMarker Native = new NativeMarker { _location = new SerializedLocation { segmentId = "Shuo", distance = 58.69453f, end = NativeEnd.A } };
        void ITrackMarkerWorld.SetLocation(object target, TrackLocation value) {
            RailroaderScenePatcher.NativeMarkerLocation.Write(Native, value, value != null && Segments.ContainsKey(value.Segment) ? new NativeSegment { Id = value.Segment } : null, typeof(NativeLocation), typeof(NativeEnd));
            var stored = Native._location;
            base.SetLocation(target, stored.segmentId == null ? null : new TrackLocation(stored.segmentId, stored.end == NativeEnd.A ? "a" : "b", stored.distance));
        }
        public void AfterVerifiedState(object target) {
            warningHistory.Verified(target, Segments.ContainsKey(Native._location.segmentId ?? ""), ((FakeMarker)target).Active, Messages, () => {
                var row = new CurrentWarning(new object(), "System", "M5pp", "Whistle sign", "Original broken Shuo reference");
                Messages.Rows.RemoveAll(r => r.Source == row.Source && r.Key == row.Key); Messages.Rows.Add(row); return row;
            });
        }
    }
    static void RemoveRepairRestoresTheNativeSerializedReference() {
        var world = new SerializedWorld(); var marker = new FakeMarker { Id = "M5pp", Segment = "Shuo", Distance = 58.69453 };
        world.Objects["sign"] = new List<object> { marker }; world.Segments["replacement"] = new List<double> { 100 };
        var engine = new PatchEngine(new TrackMarkerScene(world, Capability.Checked));
        var json = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"repair\",\"target\":{\"scene\":\"scene\",\"path\":\"sign\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"},\"dependsOn\":[],\"relationships\":{\"trackMarker\":{\"segment\":\"replacement\",\"end\":\"a\",\"distance\":58.69453}}}]}";
        var control = new CurrentWarning(new object(), "Control", "unrelated", "Control", "Keep this unrelated message");
        world.Messages.Rows.Add(control);
        var target = new TargetIdentity("scene", "sign", "Track.TrackMarker", "track-marker-v1");
        Check(engine.BeforeNativeCheck(json, "fixture", target).Outcomes["repair"].Applied, "The complete document repairs the native marker before its native check");
        Check(engine.Apply(json, "fixture").Outcomes["repair"].Applied, "The later world-load callback retains the same original underlying reference");
        var editor = new Editor(); var session = new AuthoringSession(engine, editor, new Store { Patch = json }, "map"); editor.Session = session;
        var removed = session.Remove(target);
        Check(removed.Applied, "Remove must restore the original serialized missing reference instead of refusing it: " + removed.Error);
        Check(world.Native._location.segmentId == "Shuo" && Math.Abs(world.Native._location.distance - 58.69453) < 0.001 && world.Registrations(marker).Count == 0, "Removal restores the original native reference with no graph registration");
        Check(world.Messages.Rows.Count == 2 && world.Messages.Rows.Any(r => r.Key == "M5pp") && world.Native.Invalidations == 2, "Verified removal invalidates the native cache and restores its broken-binding warning alongside the control");
        editor.Undo();
        Check(world.Native._location.segmentId == "replacement" && world.Registrations(marker).Count == 1, "Undo removal restores the repaired native binding and its registration");
        Check(world.Messages.Rows.Count == 1 && ReferenceEquals(world.Messages.Rows[0], control), "Undo removes only the warning emitted for the restored broken binding after repair verifies");
        editor.Redo();
        Check(world.Native._location.segmentId == "Shuo" && world.Registrations(marker).Count == 0, "Redo removal restores the original broken native binding");
        Check(world.Messages.Rows.Count == 2, "Redo removal restores its native warning");
        var row = world.Messages.Rows.Single(r => r.Key == "M5pp");
        world.Messages.Rows.Remove(row); world.Messages.Rows.Add(new CurrentWarning(row.Token, row.Source, row.Key, row.Originator, "Updated by another native check"));
        editor.Undo();
        Check(world.Messages.Rows.Count == 2 && world.Messages.Rows.Any(r => ReferenceEquals(r, control)), "History reconciliation preserves a row changed after attribution and the unrelated control");
        var invalidations = world.Native.Invalidations;
        var invalid = new PatchEngine(new TrackMarkerScene(world, Capability.Checked)).Apply(json.Replace("\"segment\":\"replacement\"", "\"segment\":\"Shuo\""));
        Check(!invalid.Outcomes["repair"].Applied && world.Native.Invalidations == invalidations, "Supporting captured broken references never permits a newly authored missing replacement through preflight");
    }
    static void GroupActivityRefusalKeepsAccurateDiagnostics() {
        var world = new FakeMarkerWorld(); var marker = new FakeMarker { Id = "sign", Segment = "S9sc", Distance = 20 };
        world.Objects["sign"] = new List<object> { marker }; world.Relative(marker); world.LoadFollower(marker);
        var capabilities = new SceneCapabilities(() => world.MapIds); capabilities.Add(new TrackMarkerScene(world, Capability.Checked));
        var messages = new Messages(); var runtime = new PatchRuntime(new PatchEngine(capabilities), messages, "0.5.0", "game-build");
        var source = new PatchSource("example/mapmod", "Example MapMod", "2.1");
        var json = Document.Replace("\"component\":\"board\"", "\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"").Replace("\"active\":false", "\"active\":true");
        var result = runtime.Apply(json, source);
        Check(!result.Outcomes["move"].Applied && world.Mutations == 0 && messages.Rows.Single().Cause == FailureCause.InvalidDefinition && messages.Rows[0].ReportTo == "MapMod author", "A state without a relationship still enforces group activity rules and reports the invalid supplied value accurately");
        runtime.Apply(json, source);
        Check(messages.Rows.Count == 1, "Repeated group-activity refusal does not duplicate its attributed diagnostic");
        runtime = new PatchRuntime(new PatchEngine(capabilities), messages, "0.5.0", "game-build");
        Check(runtime.Apply(json.Replace("\"active\":true", "\"active\":false"), source).Outcomes["move"].Applied, "Explicit disable remains supported through the runtime diagnostic boundary");
        world.SetGroupEnabled("ela", false); world.SetGroupEnabled("ela", true);
        Check(!marker.Active && marker.Held && messages.Rows.Count == 1, "Group activation respects the explicit disable without an invented failure diagnostic");
    }
    static void UnavailableEarlyPrerequisiteDefersWithoutFailingTheLoad() {
        var world = new FakeMarkerWorld(); var marker = new FakeMarker { Id = "sign", Segment = "missing" };
        world.Objects["sign"] = new List<object> { marker };
        var messages = new Messages();
        var runtime = new PatchRuntime(new PatchEngine(new TrackMarkerScene(world, Capability.Checked)), messages, "0.5.0", "game-build");
        var source = new PatchSource("example/mapmod", "Example MapMod", "2.1");
        var json = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"repair\",\"target\":{\"scene\":\"scene\",\"path\":\"sign\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"},\"dependsOn\":[],\"relationships\":{\"trackMarker\":{\"segment\":\"late-track\",\"end\":\"a\",\"distance\":10}}}]}";
        var early = runtime.BeforeNativeCheck(json, source, new TargetIdentity("scene", "sign", "Track.TrackMarker", "track-marker-v1"));
        Check(early.Outcomes.Count == 0 && early.EarlyRouteUnavailable.Contains("late-track") && messages.Rows.Count == 0 && marker.Segment == "missing", "Unavailable early prerequisites defer without consuming the runtime attempt or silencing the native check");
        world.Segments["late-track"] = new List<double> { 100 };
        var later = runtime.Apply(json, source);
        Check(later.Outcomes["repair"].Applied && marker.Segment == "late-track" && messages.Rows.Count == 0, "World-load application can repair after the actual early prerequisite becomes available");
    }
    static void IsolatedFailuresKeepIndependentWork() {
        var scene = new Scene(); var broken = new Box(); var independent = new Box();
        scene.Objects["sign"] = new List<object> { broken }; scene.Objects["other"] = new List<object> { independent };
        scene.FailingTarget = broken;
        var json = JObject.Parse(Document); var entries = (JArray)json["modifications"];
        var second = (JObject)entries[0].DeepClone(); second["id"] = "independent"; second["target"]["path"] = "other"; entries.Add(second);
        var messages = new Messages(); var runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.5.0", "game-build");
        var source = new PatchSource("example/mapmod", "Example MapMod", "2.1");
        var result = runtime.Apply(json.ToString(), source);
        Check(!result.Outcomes["move"].Applied && broken.Active && result.Outcomes["independent"].Applied && !independent.Active, "A failed operation restores its object while independent document work continues");
        runtime.Apply(json.ToString(), source);
        Check(messages.Rows.Count == 1 && messages.Rows[0].Operation == "move", "Only the failed operation gets an attributed row, including after a repeated callback");
    }
    static void DocumentAndOperationDiagnosticsAreDistinct() {
        var scene = new Scene { Capability = Capability.Incompatible };
        var messages = new Messages();
        var runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.5.0", "game-build");
        var source = new PatchSource("example/mapmod", "Example MapMod", "2.1");
        runtime.Apply(Document, source);
        scene.Capability = Capability.Checked;
        runtime.Apply(Document.Replace("\"id\":\"move\"", "\"id\":\"document\""), source);
        Check(messages.Rows.Count == 2, "A modification named document is distinct from a document-level failure");
    }
    static void ExactFallbackRequiresVerifiedRepair() {
        var scene = new Scene(); var box = new Box(); scene.Objects["sign"] = new List<object> { box };
        var runtime = new PatchRuntime(new PatchEngine(scene), new Messages(), "0.5.0", "game-build");
        var source = new PatchSource("example/mapmod", "Example MapMod", "2.1");
        var warnings = new Warnings();
        var row = new CurrentWarning(new object(), "System", "marker-id", "Sign", "Original invalid binding");
        var other = new CurrentWarning(new object(), "System", "other-id", "Other sign", "Unrelated warning");
        warnings.Rows.Add(row); warnings.Rows.Add(other);
        var attribution = new WarningAttribution(row, box, "Graph unavailable at the native check");
        Check(!runtime.Reconcile(source, "move", attribution, warnings, () => true), "Warning removal requires an applied operation");
        runtime.Apply(Document, source);
        Check(!runtime.Reconcile(source, "move", attribution, warnings, () => false), "An applied result alone is insufficient without independent repair verification");
        Check(!runtime.Reconcile(source, "move", new WarningAttribution(row, box, null), warnings, () => true), "An available early route forbids fallback");
        var changed = new CurrentWarning(row.Token, "System", "marker-id", "Sign", "Changed warning");
        warnings.Rows[0] = changed;
        Check(!runtime.Reconcile(source, "move", attribution, warnings, () => true), "A row updated after attribution must remain");
        warnings.Rows[0] = row;
        warnings.Rows.Add(new CurrentWarning(new object(), row.Source, row.Key, row.Originator, row.Description));
        Check(!runtime.Reconcile(source, "move", attribution, warnings, () => true), "Ambiguous warning identity must remain");
        warnings.Rows.RemoveAt(2);
        Check(runtime.Reconcile(source, "move", attribution, warnings, () => !box.Active), "Exact current warning can be reconciled after independent verification");
        Check(warnings.Rows.Count == 1 && ReferenceEquals(warnings.Rows[0], other), "Reconciliation preserves unrelated structured warning rows");
        Check(!runtime.Reconcile(source, "move", attribution, warnings, () => true), "Repeated callback cannot remove anything else");
        warnings.Rows.Insert(0, row);
        runtime.Apply("{broken", source);
        Check(!runtime.Reconcile(source, "move", attribution, warnings, () => true), "A later rejected complete document must invalidate its previous repair permission");
    }
    static void InvalidDefinitionsAndEditorRecovery() {
        var scene = new Scene();
        var messages = new Messages();
        var runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.5.0", "game-build");
        var source = new PatchSource("example/mapmod", "Example MapMod", "2.1");
        runtime.Apply(Document.Replace("\"active\":false", "\"active\":42"), source);
        Check(messages.Rows.Count == 1 && messages.Rows[0].Cause == FailureCause.InvalidDefinition && messages.Rows[0].ReportTo == "MapMod author", "A demonstrably invalid supplied value directs reports to its MapMod author");
        Check(!messages.Rows[0].Message.Contains("Supplied author") && !messages.Rows[0].Message.Contains("Published website"), "Absent optional metadata must not be invented");
        runtime = new PatchRuntime(new PatchEngine(scene), messages, "0.5.0", "game-build");
        runtime.Apply(Document.Replace("\"active\":false", "\"futureOperation\":true"), source);
        runtime.Apply("{broken", source);
        runtime.Apply(Document.Replace("\"formatVersion\":1", "\"formatVersion\":2"), source);
        runtime.Apply(Document.Replace("\"mapId\":\"map\"", "\"mapId\":\"other\""), source);
        Check(messages.Rows.Count == 1 && scene.MutationCalls == 0, "Unknown content, unreadable formats and inapplicable maps remain editor-only and never mutate");
    }
    static void EarlyCheckUsesCompleteDocumentProtection() {
        var world = new FakeMarkerWorld();
        var marker = new FakeMarker { Id = "sign", Segment = "missing" };
        world.Objects["sign"] = new List<object> { marker }; world.Segments["replacement"] = new List<double> { 100 };
        var messages = new Messages();
        var engine = new PatchEngine(new TrackMarkerScene(world, Capability.Checked));
        var runtime = new PatchRuntime(engine, messages, "0.5.0", "game-build");
        var source = new PatchSource("example/mapmod", "Example MapMod", "2.1");
        var json = "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[{\"id\":\"repair\",\"target\":{\"scene\":\"scene\",\"path\":\"sign\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"},\"dependsOn\":[],\"relationships\":{\"trackMarker\":{\"segment\":\"replacement\",\"end\":\"a\",\"distance\":10}}}]}";
        var target = new TargetIdentity("scene", "sign", "Track.TrackMarker", "track-marker-v1");
        runtime.BeforeNativeCheck(json.Substring(0, json.Length - 2) + ",{}]}", source, target);
        Check(marker.Segment == "missing", "An invalid complete document must not repair even a valid early entry");
        var result = runtime.BeforeNativeCheck(json, source, target);
        // The controlled native check runs after the public callback and remains enabled for unresolved cases.
        Check(result.Outcomes["repair"].Applied && marker.Segment == "replacement" && world.Registrations(marker).Count == 1, "The original native check must observe the repaired binding and registration");
        Check(messages.Rows.Count == 0, "A successful early repair must not create a patch failure diagnostic");
        runtime.Apply(json, source);
        Check(world.Registrations(marker).Count == 1, "The later world-load callback must converge without duplicate graph registrations");
        var editor = new Editor(); var session = new AuthoringSession(engine, editor, new Store { Patch = json }, "map"); editor.Session = session;
        Check(session.Remove(target).Applied && marker.Segment == "missing" && world.Registrations(marker).Count == 0, "Editor removal after early repair restores the original underlying binding and registration");
        var unresolved = new FakeMarker { Id = "unpatched", Segment = "missing" };
        world.Objects["unpatched"] = new List<object> { unresolved };
        var skipped = runtime.BeforeNativeCheck(json, source, new TargetIdentity("scene", "unpatched", "Track.TrackMarker", "track-marker-v1"));
        Check(skipped.Outcomes.Count == 0 && unresolved.Segment == "missing" && messages.Rows.Count == 0, "Unpatched broken relationships remain the game's diagnostic responsibility");
        var badRuntime = new PatchRuntime(new PatchEngine(new TrackMarkerScene(world, Capability.Checked)), messages, "0.5.0", "game-build");
        badRuntime.Apply(json.Replace("\"segment\":\"replacement\"", "\"segment\":\"absent\""), source);
        Check(messages.Rows.Count == 1 && messages.Rows[0].Cause == FailureCause.InvalidDefinition, "A supplied missing replacement reference is an invalid definition, with no mutation");
    }
}
