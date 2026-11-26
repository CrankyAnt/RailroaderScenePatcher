// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The wreck setting of a starter placement: whether the new-game car placement that reads the marker places its cut
// derailed. The controlled setup of the track location tests: the wreck marker's placement is a wreck, the shed's and
// the hopper's are not.
static class StarterWreckTests {
    const string WreckPath = "Setups/Starter Equipment Setup/starter-ewh-wreck", HopperPath = "Setups/Starter Equipment Setup/starter-ewh-int2";
    static readonly TargetIdentity Wreck = new TargetIdentity("level4", WreckPath, "Track.TrackMarker", StarterPlacementScene.Structure),
        Hopper = new TargetIdentity("level4", HopperPath, "Track.TrackMarker", StarterPlacementScene.Structure);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static string At(string segment, string end, double distance) => "{\"segment\":\"" + segment + "\",\"end\":\"" + end + "\",\"distance\":" + distance.ToString(CultureInfo.InvariantCulture) + "}";
    // A starter placement with the members given as JSON text, such as "\"wreck\":true".
    static string Starter(string id, string path, string members, string setup = "ewh-steam") =>
        "\"" + id + "\":{\"target\":{\"scene\":\"level4\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"starter-placement-v1\"},\"starterPlacement\":{\"setup\":\"" + setup + "\"" + (members.Length == 0 ? "" : "," + members) + "},\"dependsOn\":[]}";
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + items + "}}";
    static string Entry(FakeStarterWorld world, FakeMarker marker) {
        var entries = world.Registrations(marker);
        if (entries.Count != 1) return entries.Count + " entries";
        var e = entries[0];
        return e.Bucket + "|" + e.Location.End + "|" + e.Location.Distance.ToString(CultureInfo.InvariantCulture);
    }
    internal static void Run() {
        OmittedOnAndOffValues();
        AWreckWithATrackLocationSetsBoth();
        InvalidWreckValuesAreRefused();
        AWreckNeedsExactlyOneReader();
        RepeatedApplicationConverges();
        FailedAttemptRestoresThePriorWreck();
        UnverifiedWreckRestorationBlocksTheRetry();
        ACaptureWithoutReadableSetupsKeepsTheMarkerSnapshot();
        EditingShowsSetsUndoesAndRedoes();
        EditingRefusesBeforeAnythingChanges();
        RemovalReturnsTheUnderlyingWreck();
        ARevertedWreckLeavesTheSessionClean();
        SaveWritesTheCanonicalMemberAndRaisesTheMinimum();
        ASavedWreckReappliesAtReload();
        Console.WriteLine("Starter wreck tests passed");
    }
    // An omitted wreck keeps the native setting; an explicit value sets it on or off. The marker's track location, its
    // registration and the object stay as they are.
    static void OmittedOnAndOffValues() {
        var (world, wreck, shed, hopper, scene) = StarterTrackLocationTests.Setup();
        var engine = new PatchEngine(scene);
        var result = engine.Apply(Doc(Starter("hopper-wreck", HopperPath, "\"wreck\":true") + "," + Starter("no-wreck", WreckPath, "\"wreck\":false")));
        Check(result.Error == null && result.Outcomes.Values.All(o => o.Applied), "Both wreck settings apply: " + String.Join(" | ", result.Outcomes.Select(o => o.Key + "=" + o.Value.Error)));
        Check(world.WreckOf(hopper) && !world.WreckOf(wreck), "On sets the wreck and off clears it");
        Check(!world.WreckOf(shed), "A placement the scene patch file omits keeps its native setting");
        Check(Entry(world, hopper) == "S2|a|90" && Entry(world, wreck) == "S1|a|50" && hopper.Position[0] == 20 && hopper.Active, "The markers keep their track locations, registrations and objects");
        var read = scene.Read(hopper);
        Check(read.Starter.Wreck == true && read.Starter.Setup == "ewh-steam", "The capability reads the effective wreck setting back");
        Check(scene.Read(shed).Starter.Wreck == false, "The capability reads an untouched placement's native setting");
    }
    static void AWreckWithATrackLocationSetsBoth() {
        var (world, _, _, hopper, scene) = StarterTrackLocationTests.Setup();
        var outcome = new PatchEngine(scene).Apply(Doc(Starter("both", HopperPath, "\"track\":" + At("S2", "a", 80) + ",\"wreck\":true"))).Outcomes["both"];
        Check(outcome.Applied && Entry(world, hopper) == "S2|a|80" && world.WreckOf(hopper), "A starter placement sets the track location and the wreck together: " + outcome.Error);
    }
    static void Refused(string scenePatchFile, string id, string expected, string message, Action<FakeStarterWorld> prepare = null) {
        var (world, wreck, _, hopper, scene) = StarterTrackLocationTests.Setup();
        prepare?.Invoke(world);
        world.Mutations = 0;
        var outcome = new PatchEngine(scene).Apply(Doc(scenePatchFile)).Outcomes[id];
        Check(!outcome.Applied && outcome.Error != null && outcome.Error.Contains(expected) && outcome.Cause == FailureCause.InvalidDefinition, message + ": " + outcome.Error);
        Check(world.Mutations == 0 && world.WreckWrites == 0 && Entry(world, hopper) == "S2|a|90", message + ": nothing changed before the refusal");
        if (world.Placements.Any(p => ReferenceEquals(p.Marker, wreck))) Check(world.Placements.First(p => ReferenceEquals(p.Marker, wreck)).Wreck, message + ": the native wreck stays");
    }
    static void InvalidWreckValuesAreRefused() {
        Refused(Starter("text", HopperPath, "\"wreck\":\"yes\""), "text", "wreck must be true or false", "A wreck that is no boolean is invalid");
        Refused(Starter("null", HopperPath, "\"wreck\":null"), "null", "wreck must be true or false", "A null wreck is invalid");
        Refused(Starter("bare", HopperPath, ""), "bare", "starterPlacement contains neither track nor wreck. Set track, wreck or both in starterPlacement.", "A starter placement that sets nothing is invalid");
    }
    // A wreck setting changes the one car placement that reads the marker: a marker that no active car placement or
    // several read is refused before mutation, with the reason, and so is a setup other than the reader's.
    static void AWreckNeedsExactlyOneReader() {
        Refused(Starter("none", HopperPath, "\"wreck\":true"), "none", StarterPlacements.NoReader, "A marker no car placement reads is refused", w => w.Placements.RemoveAt(2));
        Refused(Starter("several", HopperPath, "\"wreck\":true"), "several", "2 new-game car placements read this marker (setup ewh-steam, car placement 3 of 3; setup ewh-diesel, car placement 1 of 1)",
            "A marker several car placements read is refused", w => w.Placements.Add(new StarterCarPlacement("ewh-diesel", true, 0, 1, w.Objects[HopperPath][0], new[] { "rr/gb-gondola3" }, false)));
        Refused(Starter("other", HopperPath, "\"wreck\":true", "ewh-diesel"), "other", "names setup ewh-diesel, but the car placement that reads this marker is setup ewh-steam, car placement 3 of 3", "Another setup is refused");
        Refused(Starter("unreadable", HopperPath, "\"wreck\":true"), "unreadable", "placements unavailable", "Unreadable setups refuse the wreck", w => w.FailSetups = true);
    }
    static void RepeatedApplicationConverges() {
        var (world, _, _, hopper, scene) = StarterTrackLocationTests.Setup();
        var engine = new PatchEngine(scene);
        var scenePatchFile = Doc(Starter("hopper-wreck", HopperPath, "\"wreck\":true"));
        Check(engine.Apply(scenePatchFile).Outcomes["hopper-wreck"].Applied && world.WreckWrites == 1, "The first application writes the wreck once");
        var mutations = world.Mutations;
        Check(engine.Apply(scenePatchFile).Outcomes["hopper-wreck"].Applied && world.WreckOf(hopper) && world.Mutations == mutations, "A converged wreck is not written again: " + (world.Mutations - mutations));
        world.SetWreck(world.Placements[2], false);
        Check(engine.Apply(scenePatchFile).Outcomes["hopper-wreck"].Applied && world.WreckOf(hopper), "An application after the setting drifted returns the patched wreck");
    }
    // A failure after the wreck write, or after the track location write of the same starter placement, restores the
    // captured wreck setting and track location.
    static void FailedAttemptRestoresThePriorWreck() {
        var (world, _, _, hopper, scene) = StarterTrackLocationTests.Setup();
        world.ThrowAfterWreck = true;
        var engine = new PatchEngine(scene);
        var outcome = engine.Apply(Doc(Starter("hopper-wreck", HopperPath, "\"wreck\":true"))).Outcomes["hopper-wreck"];
        Check(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal) && !world.WreckOf(hopper), "A failure after the wreck write restores the prior wreck: " + outcome.Error);
        Check(!engine.Apply(Doc(Starter("hopper-wreck", HopperPath, "\"wreck\":true"))).Outcomes["hopper-wreck"].Applied && !world.WreckOf(hopper), "A failed wreck setting is not retried in the same load");
        (world, _, _, hopper, scene) = StarterTrackLocationTests.Setup();
        world.ThrowAfterLocation = true;
        outcome = new PatchEngine(scene).Apply(Doc(Starter("both", HopperPath, "\"track\":" + At("S2", "a", 80) + ",\"wreck\":true"))).Outcomes["both"];
        Check(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal) && !world.WreckOf(hopper) && Entry(world, hopper) == "S2|a|90",
            "A failure of the track location write restores the wreck and the location: " + outcome.Error);
        // In scene patch editing the failed edit leaves the scene patch file and the wreck as they were.
        var (w2, h2, session, mapEditor, _) = Session();
        w2.ThrowAfterWreck = true;
        var failed = session.SetStarterWreck(Hopper, true);
        Check(!failed.Applied && !w2.WreckOf(h2) && session.ScenePatchFileText.Contains("\"modifications\": {}") && !mapEditor.CanUndo, "A failed edit restores the prior wreck and records nothing: " + failed.Error);
    }
    // The restoration's wreck write fails as well: the engine cannot verify the cleanup and refuses the marker until
    // Recheck brings it to the state the scene patch file asks for and verifies it.
    static void UnverifiedWreckRestorationBlocksTheRetry() {
        var (world, _, _, hopper, scene) = StarterTrackLocationTests.Setup();
        world.AfterWreckWrite = () => { world.AfterWreckWrite = null; world.FailWreckWrites = true; throw new InvalidOperationException("injected after the wreck write"); };
        var engine = new PatchEngine(scene);
        var outcome = engine.Apply(Doc(Starter("hopper-wreck", HopperPath, "\"wreck\":true"))).Outcomes["hopper-wreck"];
        Check(!outcome.Applied && outcome.Error.StartsWith("The change failed. Cleanup could not be verified. Reason:", StringComparison.Ordinal) && world.WreckOf(hopper), "A wreck restoration that cannot be written is unverified: " + outcome.Error);
        world.FailWreckWrites = false; var writes = world.WreckWrites;
        var retry = engine.Apply(Doc(Starter("other", HopperPath, "\"wreck\":false"))).Outcomes["other"];
        Check(!retry.Applied && retry.Error.Contains("could not be verified") && world.WreckWrites == writes, "Another operation on the marker is blocked until cleanup is verified: " + retry.Error);
        // In scene patch editing the retry stays blocked until Recheck verifies the restored state.
        var (w2, h2, session, mapEditor, _) = Session();
        w2.AfterWreckWrite = () => { w2.AfterWreckWrite = null; w2.FailWreckWrites = true; throw new InvalidOperationException("injected after the wreck write"); };
        var failed = session.SetStarterWreck(Hopper, true);
        Check(!failed.Applied && failed.Error.Contains("could not be verified") && w2.WreckOf(h2) && !mapEditor.CanUndo, "A failed edit with unverified cleanup says so: " + failed.Error);
        var blocked = session.SetStarterWreck(Hopper, false);
        Check(!blocked.Applied && blocked.Error.Contains("Recheck"), "The retry is blocked until Recheck: " + blocked.Error);
        Check(!session.Recheck(Hopper).Applied && w2.WreckOf(h2), "Recheck refuses while the stray wreck cannot be written back");
        w2.FailWreckWrites = false;
        var recheck = session.Recheck(Hopper);
        Check(recheck.Applied && !w2.WreckOf(h2), "Recheck returns the placement to the scene patch file's state and verifies it: " + recheck.Error);
        Check(session.SetStarterWreck(Hopper, true).Applied && w2.WreckOf(h2), "After Recheck the wreck can be set again");
    }
    // Setups or a marker that cannot be read leave the capture's wreck setting out; the marker's own snapshot still
    // restores its track location as before.
    static void ACaptureWithoutReadableSetupsKeepsTheMarkerSnapshot() {
        var (world, _, _, hopper, scene) = StarterTrackLocationTests.Setup();
        world.FailMarker = true;
        var snapshot = scene.Capture(hopper);
        world.FailMarker = false;
        hopper.Distance = 30;
        Check(scene.Restore(hopper, snapshot) && hopper.Distance == 90 && world.WreckWrites == 0, "The capture restores the track location without a wreck setting");
        world.FailSetups = true;
        snapshot = scene.Capture(hopper);
        world.FailSetups = false;
        Check(scene.Restore(hopper, snapshot) && hopper.Distance == 90 && world.WreckWrites == 0, "Unreadable setups leave the wreck setting out of the capture");
    }
    static (FakeStarterWorld world, FakeMarker hopper, PatchEditingSession session, NativeMapEditor mapEditor, Store store) Session(string patch = null) {
        var (world, _, _, hopper, scene) = StarterTrackLocationTests.Setup();
        var store = new Store { Patch = patch }; var mapEditor = new NativeMapEditor();
        // A loaded scene patch file has applied at world load before scene patch editing starts.
        var engine = new PatchEngine(scene);
        if (patch != null) engine.Apply(patch, "author/pack");
        var session = new PatchEditingSession(engine, mapEditor, store); mapEditor.Session = session;
        return (world, hopper, session, mapEditor, store);
    }
    static JObject Only(PatchEditingSession session) => (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"].Single().First;
    // The checkbox shows the effective wreck setting; setting it stores the mod value, and native Undo and Redo return
    // and reapply it with the dirty state following the outstanding persistence.
    static void EditingShowsSetsUndoesAndRedoes() {
        var (world, hopper, session, mapEditor, store) = Session();
        Check(session.StarterWreck(Hopper) == false && session.StarterWreck(Wreck) == true && !session.HasStarterWreck(Hopper), "The checkbox shows each placement's native wreck setting");
        var set = session.SetStarterWreck(Hopper, true);
        Check(set.Applied && !set.Reverted && world.WreckOf(hopper) && session.StarterWreck(Hopper) == true, "Setting the wreck derails the placement's cut at a new game: " + set.Error);
        var item = Only(session);
        Check(((JProperty)item.Parent).Name == "starter-ewh-int2" && (string)item["starterPlacement"]["setup"] == "ewh-steam" && item["starterPlacement"]["wreck"]?.Type == JTokenType.Boolean && (bool)item["starterPlacement"]["wreck"]
            && item["starterPlacement"]["track"] == null && item["placement"] == null && item["active"] == null && item["relationships"] == null, "The scene patch file records the setup and the wreck only: " + item);
        Check(session.HasStarterWreck(Hopper) && session.Changes(Hopper).SequenceEqual(new[] { "Start derailed: from No to Yes" }), "The Changes lines name the wreck: " + String.Join(" | ", session.Changes(Hopper)));
        Check(mapEditor.IsDirty && session.Outstanding(mapEditor.NativeDirty).Contains(PatchEditingSession.PatchData), "The edit leaves the scene patch file outstanding");
        Check(Entry(world, hopper) == "S2|a|90", "The wreck edit leaves the track location alone");
        // A track location placed next keeps the wreck in the same starter placement.
        Check(session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 80)).Applied && (bool)Only(session)["starterPlacement"]["wreck"] && session.Changes(Hopper).Count == 2, "Placing the marker keeps the wreck");
        mapEditor.Undo();
        Check(Entry(world, hopper) == "S2|a|90" && world.WreckOf(hopper), "Undo of the placement keeps the wreck");
        mapEditor.Undo();
        Check(!world.WreckOf(hopper) && session.ScenePatchFileText.Contains("\"modifications\": {}") && !mapEditor.IsDirty && session.StarterWreck(Hopper) == false, "Undo returns the wreck setting from before the edit and a clean state");
        mapEditor.Redo();
        Check(world.WreckOf(hopper) && mapEditor.IsDirty && session.Outstanding(mapEditor.NativeDirty).Contains(PatchEditingSession.PatchData), "Redo reapplies the edit and the dirty state follows");
        Check(mapEditor.Save() == null && !mapEditor.IsDirty && store.Patch.Contains("\"wreck\": true"), "Save writes the wreck and leaves a clean state");
        mapEditor.Undo();
        Check(!world.WreckOf(hopper) && mapEditor.IsDirty && session.Outstanding(mapEditor.NativeDirty).Contains(PatchEditingSession.PatchData), "Undo after Save makes the patch outstanding again");
        mapEditor.Redo();
        Check(world.WreckOf(hopper) && !mapEditor.IsDirty, "Redo back to the saved state is clean again");
        // Clearing the wreck of the wreck marker's placement sets it off as a mod value.
        var off = session.SetStarterWreck(Wreck, false);
        Check(off.Applied && !world.WreckOf(world.Objects[WreckPath][0]) && session.StarterWreck(Wreck) == false && session.Changes(Wreck).SequenceEqual(new[] { "Start derailed: from Yes to No" }), "Clearing the checkbox sets the wreck off: " + off.Error);
        // A setting equal to the current one changes nothing and records no history step.
        var steps = mapEditor.UndoGroups.Count;
        Check(session.SetStarterWreck(Wreck, false).Unchanged && mapEditor.UndoGroups.Count == steps, "The current wreck setting changes nothing");
    }
    static void EditingRefusesBeforeAnythingChanges() {
        var (world, hopper, session, mapEditor, _) = Session();
        world.Placements.RemoveAt(2);
        var before = session.ScenePatchFileText;
        var refused = session.SetStarterWreck(Hopper, true);
        Check(!refused.Applied && refused.Error == StarterPlacements.NoReader && session.ScenePatchFileText == before && world.WreckWrites == 0 && !mapEditor.CanUndo, "A marker that no car placement reads is refused before anything changes: " + refused.Error);
        Check(session.StarterWreck(Hopper) == null, "The checkbox has no effective setting without exactly one reader");
        (world, hopper, session, mapEditor, _) = Session();
        world.Placements.Add(new StarterCarPlacement("ewh-diesel", true, 0, 1, hopper, new[] { "rr/gb-gondola3" }, false));
        refused = session.SetStarterWreck(Hopper, true);
        Check(!refused.Applied && refused.Error.StartsWith("2 new-game car placements read this marker", StringComparison.Ordinal) && refused.Error.EndsWith("so it cannot be used for this marker", StringComparison.Ordinal)
            && world.WreckWrites == 0 && !mapEditor.CanUndo, "A marker that several car placements read is refused: " + refused.Error);
        // The greyed checkbox gives the short reason for the engine's refusal.
        var lines = world.Setups().Readers(hopper);
        Check(CommandBand.WreckReason(null, lines) == "Several new-game car placements read it", "The checkbox names the multiple readers: " + CommandBand.WreckReason(null, lines));
        world.Placements.RemoveAt(world.Placements.Count - 1); world.Placements.RemoveAt(2);
        refused = session.SetStarterWreck(Hopper, true);
        Check(CommandBand.WreckReason(null, world.Setups().Readers(hopper)) == "No new-game car placement reads it", "Without a reader the checkbox names that reason: " + refused.Error);
        world.FailSetups = true;
        refused = session.SetStarterWreck(Hopper, true);
        Check(!refused.Applied && refused.Error == StarterPlacements.Unreadable("placements unavailable")
            && CommandBand.WreckReason("placements unavailable", lines) == "The new-game setups can't be read",
            "Unreadable setups come first in the checkbox's reason, as in the refusal: " + refused.Error + " | " + CommandBand.WreckReason("placements unavailable", lines));
    }
    // Setting the native value again, or removing the modification, returns the effective underlying setting computed
    // from the native context and the mod values that remain.
    static void RemovalReturnsTheUnderlyingWreck() {
        var (world, hopper, session, mapEditor, _) = Session();
        session.SetStarterWreck(Hopper, true);
        var back = session.SetStarterWreck(Hopper, false);
        Check(back.Applied && back.Reverted && !world.WreckOf(hopper) && session.ScenePatchFileText.Contains("\"modifications\": {}") && !session.HasStarterWreck(Hopper), "Setting the native value removes the wreck setting and the empty modification: " + back.Error);
        mapEditor.Undo(); Check(world.WreckOf(hopper), "Undo of the removal restores this mod's wreck");
        // With a track location, the wreck setting leaves and the track location stays.
        Check(session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 80)).Applied, "The marker is placed");
        var cleared = session.SetStarterWreck(Hopper, false);
        Check(cleared.Reverted && !world.WreckOf(hopper) && Entry(world, hopper) == "S2|a|80" && Only(session)["starterPlacement"]["wreck"] == null && Only(session)["starterPlacement"]["track"] != null, "The remaining track location stays: " + Only(session));
        mapEditor.Undo();
        Check(session.ClearStarterTrack(Hopper).Applied && world.WreckOf(hopper) && Entry(world, hopper) == "S2|a|90" && (bool)Only(session)["starterPlacement"]["wreck"], "Clearing the track location keeps the wreck");
        var removed = session.Remove(Hopper);
        Check(removed.Applied && !world.WreckOf(hopper) && Entry(world, hopper) == "S2|a|90", "Removal returns the native wreck setting: " + removed.Error);
        mapEditor.Undo(); Check(world.WreckOf(hopper), "Undo of the removal restores the patched wreck");
        // A loaded modification: removal computes the underlying setting from the context captured before the first mutation.
        (world, hopper, session, mapEditor, _) = Session(Doc(Starter("starter-ewh-int2", HopperPath, "\"wreck\":true")));
        Check(world.WreckOf(hopper) && session.StarterWreck(Hopper) == true, "The loaded wreck applies");
        Check(session.Remove(Hopper).Applied && !world.WreckOf(hopper), "Removing a loaded wreck returns the map's own setting");
        // An explicit value equal to the native one, written by hand, is a mod value that stays until it is removed.
        (world, hopper, session, mapEditor, _) = Session(Doc(Starter("starter-ewh-int2", HopperPath, "\"wreck\":false")));
        Check(session.HasStarterWreck(Hopper) && session.StarterWreck(Hopper) == false, "An explicit off equal to the native setting is kept");
    }
    // The native map editor as the attachment wires it: the history with its group actions, the session and the native
    // dirty tracker that tells native work from patch-only history.
    sealed class TrackedHistory : NativeMapEditor, INativeHistoryActions { }
    sealed class Editor {
        public FakeStarterWorld World; public FakeMarker Hopper; public PatchEditingSession Session; public TrackedHistory History; public Store Store;
        NativeDirtyTracker tracker;
        public Editor(string patch = null, string info = null) {
            var (world, _, _, hopper, scene) = StarterTrackLocationTests.Setup();
            World = world; Hopper = hopper;
            var engine = new PatchEngine(scene);
            if (patch != null) engine.Apply(patch, "author/pack");
            Store = new Store { Patch = patch }; if (info != null) Store.Info = info;
            History = new TrackedHistory();
            Session = new PatchEditingSession(engine, History, Store, scenePatchFileKey: "author/pack"); History.Session = Session;
            tracker = new NativeDirtyTracker(History, History.ChangeCount, false, History.CleanCount);
        }
        public bool NativeDirty => tracker.Observe(History.ChangeCount, false);
        public IReadOnlyList<string> Outstanding => Session.Outstanding(NativeDirty);
        // Clean: no outstanding write, no native dirty contribution and the band reads Saved.
        public bool Clean => !NativeDirty && Outstanding.Count == 0 && CommandBand.Save(Session.IsDirty(NativeDirty)).Word == "Saved";
        public string State => "native dirty " + NativeDirty + ", outstanding " + String.Join(", ", Outstanding);
        public string Save() { var message = History.Save(); if (message == null) tracker.Saved(History.ChangeCount); return message; }
    }
    // Ticking and unticking Start derailed, or removing and undoing back to the saved content, ends where the session
    // started: no outstanding scene patch file write, no native dirty contribution, and the band reads Saved. A native
    // edit among the steps stays native work.
    static void ARevertedWreckLeavesTheSessionClean() {
        var e = new Editor();
        Check(e.Clean, "A session without a wreck setting starts clean: " + e.State);
        Check(e.Session.SetStarterWreck(Hopper, true).Applied && !e.Clean && e.Outstanding.Contains(PatchEditingSession.PatchData), "Ticking leaves the scene patch file outstanding: " + e.State);
        var back = e.Session.SetStarterWreck(Hopper, false);
        Check(back.Applied && back.Reverted && e.Session.ScenePatchFileText.Contains("\"modifications\": {}"), "Unticking removes the wreck setting: " + e.Session.ScenePatchFileText);
        Check(e.Clean, "Tick and untick leave the session clean: " + e.State);
        e.History.Undo();
        Check(e.World.WreckOf(e.Hopper) && !e.Clean, "Undo of the untick makes it dirty again: " + e.State);
        e.History.Undo();
        Check(!e.World.WreckOf(e.Hopper) && e.Clean, "Undo of the tick is clean: " + e.State);
        e.History.Redo(); e.History.Redo();
        Check(!e.World.WreckOf(e.Hopper) && e.Clean, "Redo of both steps is clean again: " + e.State);
        // A native edit among the steps is native work that Save has to write.
        e.Session.SetStarterWreck(Hopper, true);
        e.History.NativeEdit(5);
        e.Session.SetStarterWreck(Hopper, false);
        Check(e.Outstanding.SequenceEqual(new[] { PatchEditingSession.NativeData }), "A native edit among them keeps the native map data outstanding: " + e.State);
        Check(e.Save() == null && e.Clean, "Save writes it: " + e.State);
        // Remove modification and Undo back to the saved content.
        Check(e.Session.SetStarterWreck(Hopper, true).Applied && e.Save() == null && e.Clean, "Save writes the wreck: " + e.State);
        Check(e.Session.Remove(Hopper).Applied && !e.World.WreckOf(e.Hopper) && !e.Clean, "Removal leaves the scene patch file outstanding: " + e.State);
        e.History.Undo();
        Check(e.World.WreckOf(e.Hopper) && e.Clean, "Remove modification and Undo back to the saved content are clean: " + e.State);
        Check(e.Session.SetStarterWreck(Hopper, false).Applied && e.Session.SetStarterWreck(Hopper, true).Applied && e.Clean, "Untick and tick back to the saved setting are clean: " + e.State);
        // A reopened Map Editor on the saved scene patch file: Remove modification and Undo.
        var reopened = new Editor(e.Store.Patch, e.Store.Info);
        Check(reopened.World.WreckOf(reopened.Hopper) && reopened.Clean, "The reopened session starts clean: " + reopened.State);
        Check(reopened.Session.Remove(Hopper).Applied && !reopened.World.WreckOf(reopened.Hopper) && !reopened.Clean, "Removal in the reopened session is outstanding: " + reopened.State);
        reopened.History.Undo();
        Check(reopened.World.WreckOf(reopened.Hopper) && reopened.Clean, "Remove modification and Undo in the reopened session are clean: " + reopened.State);
    }
    static void SaveWritesTheCanonicalMemberAndRaisesTheMinimum() {
        var (world, hopper, session, mapEditor, store) = Session();
        store.Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"rr/game >=0.6.2\"]}}";
        session.SetStarterWreck(Hopper, true);
        session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 80));
        Check(session.MinimumPatcherVersion == StarterPlacementScene.RequiredVersion, "A wreck setting requires the starter placement capability's version: " + session.MinimumPatcherVersion);
        Check(mapEditor.Save() == null, "Save succeeds");
        var saved = JObject.Parse(store.Patch);
        var item = (JObject)saved["modifications"].Single().First;
        Check((int)saved["formatVersion"] == 1, "Save keeps format version 1");
        Check(String.Join(",", item.Properties().Select(p => p.Name)) == "target,starterPlacement,dependsOn", "The starter placement stands at its canonical position");
        Check(String.Join(",", ((JObject)item["starterPlacement"]).Properties().Select(p => p.Name)) == "setup,track,wreck", "The wreck stands after the track location: " + item["starterPlacement"]);
        Check(store.Info.Contains("crankyant/scene-patcher >=" + StarterPlacementScene.RequiredVersion), "Save raises the minimum patcher dependency: " + store.Info);
        // A wreck-only scene patch file raises the minimum too.
        (world, hopper, session, mapEditor, store) = Session();
        store.Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"rr/game >=0.6.2\"]}}";
        session.SetStarterWreck(Hopper, true);
        Check(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=" + StarterPlacementScene.RequiredVersion), "A wreck-only scene patch file raises the minimum: " + store.Info);
        // A stricter existing constraint stays.
        (world, hopper, session, mapEditor, store) = Session();
        store.Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"rr/game >=0.6.2\",\"crankyant/scene-patcher >=0.9.0\"]}}";
        session = new PatchEditingSession(new PatchEngine(new StarterPlacementScene(world, Capability.Unchecked)), mapEditor, store); mapEditor.Session = session;
        session.SetStarterWreck(Hopper, true);
        Check(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.9.0") && !store.Info.Contains(">=" + StarterPlacementScene.RequiredVersion + "\""), "A stricter existing constraint is retained: " + store.Info);
        // A scene patch file in another member order reads as the same content and saves in canonical order after an edit.
        var reordered = "{\"modifications\":{\"starter-int2\":{\"dependsOn\":[],\"starterPlacement\":{\"wreck\":true,\"track\":{\"distance\":80,\"end\":\"a\",\"segment\":\"S2\"},\"setup\":\"ewh-steam\"},\"target\":{\"structure\":\"starter-placement-v1\",\"component\":\"Track.TrackMarker\",\"path\":\"" + HopperPath + "\",\"scene\":\"level4\"}}},\"sourceMap\":{\"mapId\":\"map\"},\"formatVersion\":1}";
        (world, hopper, session, mapEditor, store) = Session(reordered);
        Check(!session.Outstanding(mapEditor.NativeDirty).Contains(PatchEditingSession.PatchData), "A reordered scene patch file is read without a patch change");
        Check(session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 85)).Applied && mapEditor.Save() == null, "An edit of the reordered scene patch file saves");
        var written = (JObject)JObject.Parse(store.Patch)["modifications"]["starter-int2"];
        Check(String.Join(",", ((JObject)written["starterPlacement"]).Properties().Select(p => p.Name)) == "setup,track,wreck", "Save writes the starter placement's members in canonical order: " + written);
    }
    // Save followed by a world reload: the saved scene patch file sets the wreck again on a fresh world.
    static void ASavedWreckReappliesAtReload() {
        var (_, _, session, mapEditor, store) = Session();
        session.SetStarterWreck(Hopper, true);
        session.SetStarterWreck(Wreck, false);
        Check(mapEditor.Save() == null, "Save succeeds");
        var (world, wreck, _, hopper, scene) = StarterTrackLocationTests.Setup();
        var reloaded = new PatchEngine(scene).Apply(store.Patch);
        Check(reloaded.Outcomes.Values.All(o => o.Applied) && world.WreckOf(hopper) && !world.WreckOf(wreck), "The reloaded world has the saved wreck settings: " + String.Join(" | ", reloaded.Outcomes.Select(o => o.Key + "=" + o.Value.Error)));
    }
}
