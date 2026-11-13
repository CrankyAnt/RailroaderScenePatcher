// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// A controlled scene whose track segment locations and endpoints have native poses in map coordinates.
class TrackScene : Scene, ITrackPoseScene {
    public Dictionary<string, TrackPose> Poses = new Dictionary<string, TrackPose>();
    public int PoseQueries;
    public TrackPose TrackPose(TrackAnchor anchor) {
        PoseQueries++;
        if (!Poses.TryGetValue(anchor.ToString(), out var pose)) throw new InvalidOperationException("Track anchor " + anchor + " does not resolve");
        return pose;
    }
}
static class TrackFollowingTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static readonly double S = Math.Sqrt(0.5);
    // Rotation of 90 degrees about the vertical axis: the track's forward axis turns from +z to +x and its +x offset to -z.
    static readonly double[] Quarter = { 0, S, 0, S };
    static bool Near(double[] a, params double[] b) => a.Length == b.Length && a.Select((v, i) => Math.Abs(v - b[i]) < 1e-6).All(x => x);
    static string Follow(string id, string track, string offset = "[2,0,0]", string rotation = "[0,0,0,1]", string path = "p", string extra = "") =>
        "{\"id\":\"" + id + "\",\"target\":{\"scene\":\"s\",\"path\":\"" + path + "\",\"component\":\"c\"},\"placement\":{\"mode\":\"trackFollowing\",\"track\":" + track +
        ",\"offset\":" + offset + ",\"rotation\":" + rotation + "}" + extra + ",\"dependsOn\":[]}";
    const string SegmentTrack = "{\"segment\":\"S1\",\"end\":\"a\",\"distance\":10}", NodeTrack = "{\"node\":\"N1\"}";
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":[" + items + "]}";
    static (TrackScene scene, Box box) Setup() {
        var scene = new TrackScene(); var box = new Box { Position = new[] { 5.0, 6, 7 }, Rotation = new[] { 0.0, 0, 0, 1 } };
        scene.Objects["p"] = new List<object> { box };
        scene.Poses["S1|a|10"] = new TrackPose(new[] { 200.0, 1, 0 }, Quarter);
        scene.Poses["node:N1"] = new TrackPose(new[] { 300.0, 2, 50 }, new[] { 0.0, 0, 0, 1 });
        return (scene, box);
    }
    public static void Run() {
        SegmentLocationPlacesRelativeToTrackFrame();
        EndpointAnchorAndRepeatDoNotAccumulate();
        UnresolvedAnchorChangesNothing();
        CapabilitiesReportTrackPoses();
        EditorFollowKeepsPoseAndFollowsTrackMoves();
        PlacementEditsKeepFollowingUntilStopped();
        ReconciliationFailureRestoresAndGuards();
        SaveAndReloadReproduceFollowingOnce();
        Console.WriteLine("PASS: track-following placement");
    }
    static void SegmentLocationPlacesRelativeToTrackFrame() {
        var (scene, box) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Follow("follow", SegmentTrack)));
        Assert(result.Error == null && result.Outcomes["follow"].Applied, "A track-following placement applies: " + result.Error + result.Outcomes["follow"].Error);
        // Map position 200 + rotated offset (0,0,-2); the scene stores map x minus its origin 100.
        Assert(Near(box.Position, 100, 1, -2), "The object sits at the track location plus the offset turned with the track: " + String.Join(",", box.Position));
        Assert(Near(box.Rotation, Quarter), "The object's rotation is the track rotation composed with the relative rotation: " + String.Join(",", box.Rotation));
    }
    static void EndpointAnchorAndRepeatDoNotAccumulate() {
        var (scene, box) = Setup();
        var engine = new PatchEngine(scene);
        var doc = Doc(Follow("follow", NodeTrack, "[1,0,4]", "[0," + S.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ",0," + S.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "]"));
        Assert(engine.Apply(doc).Outcomes["follow"].Applied && Near(box.Position, 201, 2, 54) && Near(box.Rotation, Quarter), "An endpoint anchor places the object relative to the node: " + String.Join(",", box.Position));
        for (var i = 0; i < 3; i++) Assert(engine.Apply(doc).Outcomes["follow"].Applied, "Repeated application succeeds");
        Assert(Near(box.Position, 201, 2, 54) && Near(box.Rotation, Quarter), "Repeated application of the same document does not accumulate the offset or rotation");
        // A changed track pose yields the new absolute pose from the same relative values.
        scene.Poses["node:N1"] = new TrackPose(new[] { 400.0, 3, 0 }, Quarter);
        Assert(new PatchEngine(scene).Apply(doc).Outcomes["follow"].Applied && Near(box.Position, 304, 3, -1), "The next load follows the moved endpoint: " + String.Join(",", box.Position));
        Assert(Near(box.Rotation, 0, 1, 0, 0), "The relative rotation composes with the moved endpoint's rotation: " + String.Join(",", box.Rotation));
    }
    static void UnresolvedAnchorChangesNothing() {
        foreach (var track in new[] { "{\"segment\":\"Smissing\",\"end\":\"a\",\"distance\":1}", "{\"node\":\"Nmissing\"}" }) {
            var (scene, box) = Setup();
            var other = new Box { Position = new[] { 1.0, 1, 1 }, Rotation = new[] { 0.0, 0, 0, 1 } }; scene.Objects["q"] = new List<object> { other };
            var result = new PatchEngine(scene).Apply(Doc(Follow("follow", track) + "," + Follow("independent", SegmentTrack, path: "q")));
            Assert(!result.Outcomes["follow"].Applied && result.Outcomes["follow"].Error.Contains("does not resolve") && Near(box.Position, 5, 6, 7), "An unresolved anchor fails before mutation: " + result.Outcomes["follow"].Error);
            Assert(result.Outcomes["independent"].Applied && Near(other.Position, 100, 1, -2), "Independent track-following work still applies");
            Assert(scene.MutationCalls == 1, "Only the independent modification mutated the scene");
        }
        foreach (var bad in new[] {
            Follow("bad", "{\"segment\":\"S1\",\"end\":\"c\",\"distance\":1}"), Follow("bad", "{\"node\":\"N1\",\"segment\":\"S1\"}"), Follow("bad", "{}"),
            Follow("bad", SegmentTrack, "[1,2]"), Follow("bad", SegmentTrack, rotation: "[0,0,0,0]"), Follow("bad", SegmentTrack, extra: "").Replace("\"offset\"", "\"position\"") }) {
            var (scene, box) = Setup();
            var result = new PatchEngine(scene).Apply(Doc(bad));
            Assert(!result.Outcomes["bad"].Applied && scene.MutationCalls == 0 && scene.PoseQueries == 0, "Malformed track-following content is refused before any query or mutation: " + bad);
        }
        // A scene without track poses refuses the mode rather than placing the object somewhere else.
        var plain = new Scene(); var fixedBox = new Box { Position = new[] { 5.0, 6, 7 }, Rotation = new[] { 0.0, 0, 0, 1 } }; plain.Objects["p"] = new List<object> { fixedBox };
        var refused = new PatchEngine(plain).Apply(Doc(Follow("follow", SegmentTrack)));
        Assert(!refused.Outcomes["follow"].Applied && refused.Outcomes["follow"].Error.Contains("unavailable") && plain.MutationCalls == 0, "A scene without track poses refuses track-following placement");
    }
    static void CapabilitiesReportTrackPoses() {
        var (tracks, box) = Setup();
        var capabilities = new SceneCapabilities(() => new[] { "map" }) { Tracks = tracks };
        capabilities.Add(tracks);
        Assert(new PatchEngine(capabilities).Apply(Doc(Follow("follow", SegmentTrack))).Outcomes["follow"].Applied && Near(box.Position, 100, 1, -2), "A composed scene evaluates track-following placement for any placeable capability");
        var without = new SceneCapabilities(() => new[] { "map" }); without.Add(tracks);
        Assert(!new PatchEngine(without).Apply(Doc(Follow("follow", SegmentTrack))).Outcomes["follow"].Applied, "A composed scene without a track source refuses the mode");
    }
    static readonly TargetIdentity P = new TargetIdentity("s", "p", "c");
    static (TrackScene scene, Box box, AuthoringSession session, Editor editor, Store store) Session(string patch = null) {
        var (scene, box) = Setup();
        var store = new Store { Patch = patch }; var editor = new Editor();
        var session = new AuthoringSession(new PatchEngine(scene), editor, store, "map"); editor.Session = session;
        return (scene, box, session, editor, store);
    }
    static Newtonsoft.Json.Linq.JObject Only(AuthoringSession session) => (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(session.DocumentText)["modifications"].Single();
    static double[] Numbers(Newtonsoft.Json.Linq.JToken token) => token.Select(v => (double)v).ToArray();
    static void EditorFollowKeepsPoseAndFollowsTrackMoves() {
        var (scene, box, session, editor, store) = Session();
        var follow = session.Follow(P, TrackAnchor.At(new TrackLocation("S1", "a", 10)));
        Assert(follow.Applied && Near(box.Position, 5, 6, 7) && Near(box.Rotation, 0, 0, 0, 1), "Choosing a followed track keeps the object where it is: " + follow.Error);
        var placement = Only(session)["placement"];
        Assert((string)placement["mode"] == "trackFollowing" && (string)placement["track"]["segment"] == "S1" && (string)placement["track"]["end"] == "a" && (double)placement["track"]["distance"] == 10, "The document records the explicit track: " + placement);
        // Map pose (105,6,7) relative to (200,1,0) turned a quarter: the offset in the track frame is (-7,5,-95).
        Assert(Near(Numbers(placement["offset"]), -7, 5, -95), "The document records the offset in the track frame: " + placement["offset"]);
        Assert(editor.UndoGroups.Count == 1 && session.MinimumPatcherVersion == "0.5.0", "Following is one history entry and needs patcher 0.5.0");
        Assert(session.HasTrackFollowing && session.FollowingOf(P)?.Anchor.ToString() == "S1|a|10", "The session reports the followed track of the object");
        var text = session.DocumentText; var mutations = scene.MutationCalls;
        Assert(session.ReconcileTrackFollowing().Count == 0 && scene.MutationCalls == mutations, "Reconciliation without a track change mutates nothing");
        scene.Poses["S1|a|10"] = new TrackPose(new[] { 210.0, 2, 0 }, Quarter);
        var moved = session.ReconcileTrackFollowing();
        Assert(moved.Count == 1 && moved[0].Value.Applied && Near(box.Position, 15, 7, 7) && Near(box.Rotation, 0, 0, 0, 1), "A moved track carries the object by the same relative placement: " + String.Join(",", box.Position));
        scene.Poses["S1|a|10"] = new TrackPose(new[] { 210.0, 2, 0 }, new[] { 0.0, 0, 0, 1 });
        Assert(session.ReconcileTrackFollowing().Single().Value.Applied && Near(box.Position, 103, 7, -95) && Near(box.Rotation, 0, -S, 0, S), "A turned track turns the object's position and orientation with it: " + String.Join(",", box.Position) + " / " + String.Join(",", box.Rotation));
        Assert(session.DocumentText == text && editor.UndoGroups.Count == 1, "Following the track neither rewrites the stored relative values nor records history");
        editor.Undo();
        Assert(Near(box.Position, 5, 6, 7) && session.DocumentText.Contains("\"modifications\": []"), "Undo removes the following and returns the underlying pose");
        Assert(session.ReconcileTrackFollowing().Count == 0, "An object no longer following is not reconciled");
        Assert(!session.HasTrackFollowing && session.FollowingOf(P) == null, "After Undo nothing follows a track");
        editor.Redo();
        Assert(Near(box.Position, 103, 7, -95), "Redo follows the track's current pose, not the pose at the time of the edit");
        Assert(!session.Follow(P, TrackAnchor.Endpoint("Nmissing")).Applied && editor.UndoGroups.Count == 1, "An unresolved followed track is refused without history");
    }
    static void PlacementEditsKeepFollowingUntilStopped() {
        var (scene, box, session, editor, store) = Session();
        session.Follow(P, TrackAnchor.Endpoint("N1"));
        Assert(session.Edit(P, new Placement(new[] { 310.0, 2, 60 }, new[] { 0.0, 0, 0, 1 }), null).Applied && Near(box.Position, 210, 2, 60), "A placement edit of a following object moves it");
        var placement = Only(session)["placement"];
        Assert((string)placement["mode"] == "trackFollowing" && (string)placement["track"]["node"] == "N1" && Near(Numbers(placement["offset"]), 10, 0, 10), "The placement edit keeps following the same endpoint with a new offset: " + placement);
        scene.Poses["node:N1"] = new TrackPose(new[] { 320.0, 2, 50 }, new[] { 0.0, 0, 0, 1 });
        session.ReconcileTrackFollowing();
        Assert(Near(box.Position, 230, 2, 60), "The edited offset follows the endpoint");
        Assert(session.Edit(P, null, false).Applied && (string)Only(session)["placement"]["mode"] == "trackFollowing" && !box.Active, "An activity edit keeps the following placement");
        var stopped = session.StopFollowing(P);
        Assert(stopped.Applied && Near(box.Position, 230, 2, 60) && (string)Only(session)["placement"]["mode"] == "fixed" && Only(session)["active"] != null, "Stopping keeps the current pose as fixed placement and keeps activity: " + stopped.Error);
        scene.Poses["node:N1"] = new TrackPose(new[] { 400.0, 2, 50 }, new[] { 0.0, 0, 0, 1 });
        Assert(session.ReconcileTrackFollowing().Count == 0 && Near(box.Position, 230, 2, 60), "A fixed placement no longer follows the endpoint");
        Assert(!session.StopFollowing(P).Applied, "Stopping an object that does not follow a track is refused");
        editor.Undo();
        Assert(Near(box.Position, 310, 2, 60) && (string)Only(session)["placement"]["mode"] == "trackFollowing", "Undo of stopping resumes following at the endpoint's current pose: " + String.Join(",", box.Position));
    }
    static void ReconciliationFailureRestoresAndGuards() {
        var (scene, box, session, editor, store) = Session();
        session.Follow(P, TrackAnchor.At(new TrackLocation("S1", "a", 10)));
        scene.Poses["S1|a|10"] = new TrackPose(new[] { 210.0, 1, 0 }, Quarter);
        scene.FailNextApplies = 1;
        var failed = session.ReconcileTrackFollowing().Single().Value;
        Assert(!failed.Applied && failed.Error.Contains("restored") && Near(box.Position, 5, 6, 7), "A failed reconciliation restores the captured placement: " + failed.Error);
        Assert(session.ReconcileTrackFollowing().Single().Value.Applied && Near(box.Position, 15, 6, 7), "A restored object follows on the next reconciliation");
        scene.Poses["S1|a|10"] = new TrackPose(new[] { 220.0, 1, 0 }, Quarter);
        // Restoration cannot verify while every application fails.
        scene.FailApply = true;
        var unverified = session.ReconcileTrackFollowing().Single().Value;
        scene.FailApply = false;
        Assert(!unverified.Applied && unverified.Error.Contains("cleanup is unverified"), "An unverified restoration is reported: " + unverified.Error);
        var blocked = session.ReconcileTrackFollowing().Single().Value;
        Assert(!blocked.Applied && blocked.Error.Contains("unverified") && !session.Edit(P, null, false).Applied, "The guarded object is not retried by reconciliation or edited");
        Assert(session.Recheck(P).Applied && Near(box.Position, 25, 6, 7), "Recheck reconciles the actual state with the followed track and unblocks it");
        scene.Poses.Remove("S1|a|10");
        var lost = session.ReconcileTrackFollowing().Single().Value;
        Assert(!lost.Applied && lost.Error.Contains("does not resolve") && Near(box.Position, 25, 6, 7), "A followed track that no longer resolves leaves the object untouched and is reported");
    }
    static void SaveAndReloadReproduceFollowingOnce() {
        var (scene, box, session, editor, store) = Session();
        session.Follow(P, TrackAnchor.At(new TrackLocation("S1", "a", 10)));
        Assert(editor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.5.0") && store.Patch.Contains("trackFollowing"), "Save writes the following placement and raises the dependency to 0.5.0: " + store.Info);
        var (fresh, freshBox) = Setup();
        fresh.Poses["S1|a|10"] = new TrackPose(new[] { 250.0, 1, 0 }, Quarter);
        var engine = new PatchEngine(fresh);
        Assert(engine.Apply(store.Patch).Outcomes.Values.Single().Applied && Near(freshBox.Position, 55, 6, 7), "A reload places the object relative to the track's pose at load: " + String.Join(",", freshBox.Position));
        engine.Apply(store.Patch);
        Assert(Near(freshBox.Position, 55, 6, 7) && fresh.Objects["p"].Count == 1, "A repeated world-load callback converges without offset accumulation");
        var reopened = Session(store.Patch);
        Assert(reopened.session.Blocked == null && reopened.session.StopFollowing(P).Applied, "A saved following document reopens for editing");
        // The version follows the document content even when the target is not loaded.
        var unloaded = Session(store.Patch.Replace("\"path\": \"p\"", "\"path\": \"absent\""));
        Assert(unloaded.session.MinimumPatcherVersion == "0.5.0", "An unloaded target's following placement still needs 0.5.0");
    }
}
