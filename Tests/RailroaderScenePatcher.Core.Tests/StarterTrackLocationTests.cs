// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher.Core;

// The starter placement markers of one controlled setup on a controlled track. The segments lie end to end along the
// map x axis, each from its start: a location's map position is its segment's start plus its distance from end a.
// Moving away from a location's end follows the axis away from that end, through the node into the next segment, and
// stops at a gap, which is the end of the track. The setup places its cuts in order, each with its front at its
// marker's track location, extending towards the location's end.
class FakeStarterWorld : FakeMarkerWorld, IStarterPlacementWorld {
    public readonly Dictionary<string, double> Starts = new Dictionary<string, double>();
    public readonly HashSet<string> Unavailable = new HashSet<string>();
    // Nodes, by the map x position where two segments meet, whose switch is lined against a movement through them.
    public readonly HashSet<double> Against = new HashSet<double>();
    public readonly List<StarterCarPlacement> Placements = new List<StarterCarPlacement>();
    public readonly Dictionary<string, double> CarLengths = new Dictionary<string, double> { ["rr/ls-262-g16"] = 18, ["rr/ls-460-t17"] = 14, ["rr/gb-gondola3"] = 12, ["rr/gb-gondola02"] = 12 };
    public bool FailSetups;
    // A map x position at which the track reports no position, as a native failure inside the game would.
    public double? NoPositionAt;
    public void Line(string segment, double start, double length) { Segments[segment] = new List<double> { length }; Starts[segment] = start; }
    public new IReadOnlyList<object> Resolve(TargetIdentity identity) {
        if (identity.Component != "Track.TrackMarker" || identity.Structure != StarterPlacementScene.Structure) return Array.Empty<object>();
        return Objects.TryGetValue(identity.Path, out var found) ? found : new List<object>();
    }
    public StarterSetups Setups() => FailSetups ? StarterSetups.Unreadable("placements unavailable") : new StarterSetups(Placements.ToList());
    public object Marker(object target) => target;
    public TrackLocation PlacementLocation(object marker) => Location(marker);
    // Each car's length with a gap of one metre between two cars.
    public double CutLength(IReadOnlyList<string> cars) {
        if (cars.Count == 0) return 0;
        return cars.Select(c => CarLengths.TryGetValue(c, out var l) ? l : throw new InvalidOperationException("No car for identifier: " + c)).Sum() + (cars.Count - 1);
    }
    double X(TrackLocation at) => Starts[at.Segment] + (at.End == "a" ? at.Distance : Segments[at.Segment][0] - at.Distance);
    string SegmentAt(double x, double direction) =>
        Starts.Where(s => direction >= 0 ? x >= s.Value - 1e-9 && x < s.Value + Segments[s.Key][0] - 1e-9 : x > s.Value + 1e-9 && x <= s.Value + Segments[s.Key][0] + 1e-9).Select(s => s.Key).FirstOrDefault();
    public TrackLocation Move(TrackLocation from, double distance) {
        // The axis direction of a positive movement: away from end a, or away from end b.
        var direction = (from.End == "a" ? 1 : -1) * Math.Sign(distance);
        var x = X(from); var target = x + (from.End == "a" ? distance : -distance);
        var step = direction > 0 ? 0.25 : -0.25;
        var segment = from.Segment;
        for (var at = x; direction > 0 ? at < target : at > target; at = direction > 0 ? Math.Min(at + Math.Abs(step), target) : Math.Max(at + step, target)) {
            var next = direction > 0 ? Math.Min(at + Math.Abs(step), target) : Math.Max(at + step, target);
            var found = SegmentAt(next, direction);
            if (found == null) throw new StarterTrackException("Not enough track.");
            if (found != segment) {
                var node = direction > 0 ? Starts[found] : Starts[found] + Segments[found][0];
                if (Against.Any(n => Math.Abs(n - node) < 1e-6)) throw new StarterTrackException("Switch lined against.");
                segment = found;
            }
        }
        var start = Starts[segment]; var length = Segments[segment][0];
        // The location keeps facing the same way along the axis.
        return from.End == "a" ? new TrackLocation(segment, "a", Math.Round(target - start, 6)) : new TrackLocation(segment, "b", Math.Round(start + length - target, 6));
    }
    public string Unplaceable(TrackLocation at) => !Segments.ContainsKey(at.Segment) ? "No track there." : Unavailable.Contains(at.Segment) ? "Track not available." : null;
    public double[] Position(TrackLocation at) {
        if (NoPositionAt != null && Math.Abs(X(at) - NoPositionAt.Value) < 1e-6) throw new StarterTrackException("The track could not be walked: position injected");
        return new[] { X(at), 0.0, 0.0 };
    }
}
static class StarterTrackLocationTests {
    const string WreckPath = "Setups/Starter Equipment Setup/starter-ewh-wreck", ShedPath = "Setups/Starter Equipment Setup/starter-ewh-shed1", HopperPath = "Setups/Starter Equipment Setup/starter-ewh-int2";
    static readonly TargetIdentity Wreck = new TargetIdentity("level4", WreckPath, "Track.TrackMarker", StarterPlacementScene.Structure),
        Shed = new TargetIdentity("level4", ShedPath, "Track.TrackMarker", StarterPlacementScene.Structure),
        Hopper = new TargetIdentity("level4", HopperPath, "Track.TrackMarker", StarterPlacementScene.Structure);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static string At(string segment, string end, double distance) => "{\"segment\":\"" + segment + "\",\"end\":\"" + end + "\",\"distance\":" + distance.ToString(CultureInfo.InvariantCulture) + "}";
    static string Starter(string id, string path, string track, string setup = "ewh-steam", string extra = "") =>
        "\"" + id + "\":{\"target\":{\"scene\":\"level4\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"starter-placement-v1\"},\"starterPlacement\":{\"setup\":\"" + setup + "\",\"track\":" + track + "}" + extra + ",\"dependsOn\":[]}";
    static string Doc(string items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + items + "}}";
    static string Entry(FakeMarkerWorld world, FakeMarker marker) {
        var entries = world.Registrations(marker);
        if (entries.Count != 1) return entries.Count + " entries";
        var e = entries[0];
        return e.Bucket + "|" + e.Location.End + "|" + e.Location.Distance.ToString(CultureInfo.InvariantCulture) + (e.HoldsMarker ? "" : "|foreign");
    }
    // The base game's ewh-steam setup in miniature: a wreck, a locomotive and a pair of gondolas on a line of three
    // segments, S1 from 0 to 100 m, S2 from 100 to 200 m and the unavailable Sclosed from 200 to 260 m, beside a
    // separate 10 m siding Sshort at 500 m. The cuts extend towards end a, so a cut with its front at distance d from a
    // reaches back to d minus its length. One marker is disabled, as some base-game starter markers are.
    static (FakeStarterWorld world, FakeMarker wreck, FakeMarker shed, FakeMarker hopper, StarterPlacementScene scene) Setup() {
        var world = new FakeStarterWorld();
        world.Segments.Clear();
        world.Line("S1", 0, 100); world.Line("S2", 100, 100); world.Line("Sclosed", 200, 60); world.Line("Sshort", 500, 10);
        world.Unavailable.Add("Sclosed");
        world.Segments["Sdup"] = new List<double> { 40, 40 }; world.Starts["Sdup"] = 900;
        var wreck = new FakeMarker { Id = "Mwreck", Segment = "S1", End = "a", Distance = 50, Position = new double[] { 12450, 560, 4700 } };
        var shed = new FakeMarker { Id = "Mshed", Segment = "S2", End = "a", Distance = 50, Active = false };
        var hopper = new FakeMarker { Id = "Mhop", Segment = "S2", End = "a", Distance = 90 };
        world.Objects[WreckPath] = new List<object> { wreck }; world.Objects[ShedPath] = new List<object> { shed }; world.Objects[HopperPath] = new List<object> { hopper };
        foreach (var m in new[] { wreck, shed, hopper }) world.Load(m);
        world.Placements.Add(new StarterCarPlacement("ewh-steam", true, 0, 3, wreck, new[] { "rr/ls-262-g16" }, true));
        world.Placements.Add(new StarterCarPlacement("ewh-steam", true, 1, 3, shed, new[] { "rr/ls-460-t17" }, false));
        world.Placements.Add(new StarterCarPlacement("ewh-steam", true, 2, 3, hopper, new[] { "rr/gb-gondola3", "rr/gb-gondola02" }, false));
        world.Mutations = 0;
        return (world, wreck, shed, hopper, new StarterPlacementScene(world, Capability.Unchecked));
    }
    internal static void Run() {
        TheFakeTrackMovesLikeTheGame();
        TheCutRuleWalksTheCutAsTheGameDoes();
        OmittedAndSetTrackLocations();
        DetachIsRefused();
        MissingAndAmbiguousSegmentsAreRefused();
        DistanceOutsideTheSegmentIsRefused();
        TheWholeCutMustPassTheGamesRule();
        TheMarkerNeedsOneReader();
        CutsAreJudgedWithTheScenePatchFilesOtherStarterPlacements();
        ANativeFailureOnTheTrackIsARefusal();
        ADuplicateMarkerIdIsRefusedBeforeMutation();
        ObjectSettingsAndOtherRelationshipsAreRefused();
        OtherCapabilitiesRefuseAStarterPlacement();
        RepeatedApplicationConverges();
        FailedAttemptRestoresThePriorLocation();
        UnverifiedCleanupBlocksTheRetry();
        EditingPlacesUndoesAndRedoes();
        EditingRefusesBeforeAnythingChanges();
        RemovalReturnsTheUnderlyingLocation();
        SaveWritesTheCanonicalMemberAndRaisesTheMinimum();
        Console.WriteLine("Starter track location tests passed");
    }
    // The controlled track itself: moving back from a front crosses nodes into the next segment, stops at a gap and at a
    // switch lined against the movement.
    static void TheFakeTrackMovesLikeTheGame() {
        var (world, _, _, _, _) = Setup();
        var back = world.Move(new TrackLocation("S2", "a", 10), -30);
        Check(back.Segment == "S1" && Math.Abs(world.Position(back)[0] - 80) < 1e-6, "Moving back 30 m from S2/a/10 reaches map x 80 on S1: " + back);
        Check(world.Move(new TrackLocation("S1", "b", 10), -5).Segment == "S1" && Math.Abs(world.Position(world.Move(new TrackLocation("S1", "b", 10), -5))[0] - 95) < 1e-6, "End b counts from the far end");
        try { world.Move(new TrackLocation("S1", "a", 5), -10); Check(false, "Moving past the start of S1 must stop"); } catch (StarterTrackException e) { Check(e.Message == "Not enough track.", e.Message); }
        world.Against.Add(100);
        try { world.Move(new TrackLocation("S2", "a", 5), -10); Check(false, "A switch lined against must stop"); } catch (StarterTrackException e) { Check(e.Message == "Switch lined against.", e.Message); }
    }
    // The game checks the front, a location every 1.5 m back and the back end of the cut. The walk follows
    // TrainController.CanPlaceAt(Location, float, ...) of Railroader 0.6.2 build 26335745 (Railroader.dll SHA-256
    // 8E9ACFD5...A27A91E), decompiled from the installed assembly: the remainder is length - Floor(length / 1.5) * 1.5,
    // the loop checks and moves 1.5 m back while done + 1.5 < length, and the last location moves back by the remainder
    // and is checked again. A length of whole steps therefore checks its last step twice and never its back end.
    static void TheCutRuleWalksTheCutAsTheGameDoes() {
        var (world, _, _, _, _) = Setup();
        var checkedAt = StarterCutRule.Checked(world, new TrackLocation("S1", "a", 50), 4).Select(l => world.Position(l)[0]).ToArray();
        Check(checkedAt.SequenceEqual(new[] { 50.0, 48.5, 47, 46 }), "A 4 m cut is checked at 50, 48.5 and 47 and at its back end 46: " + String.Join(",", checkedAt));
        var exact = StarterCutRule.Checked(world, new TrackLocation("S1", "a", 50), 3).Select(l => world.Position(l)[0]).ToArray();
        Check(exact.SequenceEqual(new[] { 50.0, 48.5, 48.5 }), "A cut of a whole number of steps checks its last step again, as the game does: " + String.Join(",", exact));
        var body = StarterCutRule.Body(world, new TrackLocation("S1", "a", 50), 2).Select(p => p[0]).ToArray();
        Check(body.SequenceEqual(new[] { 50.0, 49.5, 49, 48.5, 48 }), "A placed cut stands from its front back over its length: " + String.Join(",", body));
    }
    // An omitted starter placement leaves the marker as the map has it; a set track location becomes the marker's, with
    // its registration, and the object keeps its placement and activity.
    static void OmittedAndSetTrackLocations() {
        var (world, wreck, shed, hopper, scene) = Setup();
        var engine = new PatchEngine(scene);
        var result = engine.Apply(Doc(Starter("move-hopper", HopperPath, At("S2", "a", 80))));
        Check(result.Error == null && result.Outcomes["move-hopper"].Applied, "The starter placement applies: " + result.Outcomes["move-hopper"].Error);
        Check(Entry(world, hopper) == "S2|a|80" && hopper.Position[0] == 20 && hopper.Active, "The marker takes the track location with its registration and keeps its object placement and activity: " + Entry(world, hopper));
        Check(Entry(world, wreck) == "S1|a|50" && shed.Segment == "S2" && shed.Distance == 50 && world.Registrations(shed).Count == 0, "Markers the scene patch file omits keep the map's track location");
        // A disabled marker takes the location without a registration, as the native marker lifecycle keeps it.
        var shedResult = engine.Apply(Doc(Starter("move-shed", ShedPath, At("S2", "b", 60)))).Outcomes["move-shed"];
        Check(shedResult.Applied && shed.Segment == "S2" && shed.End == "b" && shed.Distance == 60 && world.Registrations(shed).Count == 0 && !shed.Active, "A disabled marker takes the location and stays unregistered: " + shedResult.Error);
        var read = scene.Read(hopper);
        Check(read.Starter.Setup == "ewh-steam" && read.Starter.Track.Location.Display == "S2 / a / 80", "The capability reads the setup and track location back");
    }
    static void Refused(string scenePatchFile, string id, string expected, string message) {
        var (world, wreck, shed, hopper, scene) = Setup();
        var outcome = new PatchEngine(scene).Apply(Doc(scenePatchFile)).Outcomes[id];
        Check(!outcome.Applied && outcome.Error.Contains(expected) && outcome.Cause == FailureCause.InvalidDefinition, message + ": " + outcome.Error);
        Check(world.Mutations == 0 && Entry(world, hopper) == "S2|a|90" && Entry(world, wreck) == "S1|a|50", message + ": nothing changed before the refusal");
    }
    static void DetachIsRefused() {
        Refused(Starter("detach", HopperPath, "{\"detach\":true}"), "detach", "cannot be detached", "Detaching a used marker's track location fails at load before mutation");
    }
    static void MissingAndAmbiguousSegmentsAreRefused() {
        Refused(Starter("gone", HopperPath, At("Sgone", "a", 5)), "gone", "segment Sgone is missing from the loaded track; no other segment is used", "A missing segment is named and nothing is substituted");
        Refused(Starter("dup", HopperPath, At("Sdup", "a", 5)), "dup", "segment Sdup is ambiguous", "An ambiguous segment is named");
    }
    static void DistanceOutsideTheSegmentIsRefused() {
        Refused(Starter("far", HopperPath, At("S1", "a", 150)), "far", "distance 150 m lies outside segment S1, which is 100 m long", "A distance beyond the segment gives the reason");
    }
    // The whole cut, with the setup's other cuts, must pass the game's rule: enough track behind the front, available
    // track, a switch lined with the movement and no cut placed before it in the way; and it may not block a later cut.
    static void TheWholeCutMustPassTheGamesRule() {
        Refused(Starter("short", HopperPath, At("S1", "a", 10)), "short", "does not fit with its front at S1 / a / 10: Not enough track.", "A cut that runs off the end of the track is refused");
        Refused(Starter("closed", HopperPath, At("Sclosed", "a", 40)), "closed", "Track not available.", "A cut on unavailable track is refused");
        Refused(Starter("blocked", HopperPath, At("S1", "a", 45)), "blocked", "Blocked by the cut of setup ewh-steam, car placement 1 of 3.", "A cut that runs into the cut placed before it is refused");
        Refused(Starter("blocks", WreckPath, At("S2", "a", 60)), "blocks", "This car cut blocks the cut of setup ewh-steam, car placement 2 of 3", "A cut that a later cut runs into is refused");
        var (world, _, _, hopper, scene) = Setup();
        world.Against.Add(100);
        var against = new PatchEngine(scene).Apply(Doc(Starter("against", HopperPath, At("S2", "a", 10)))).Outcomes["against"];
        Check(!against.Applied && against.Error.EndsWith("Switch lined against.", StringComparison.Ordinal) && Entry(world, hopper) == "S2|a|90", "A cut across a switch lined against it is refused: " + against.Error);
        // Room for the cut beside the others passes; the gondolas need 25 m.
        (world, _, _, hopper, scene) = Setup();
        var fits = new PatchEngine(scene).Apply(Doc(Starter("fits", HopperPath, At("S1", "a", 28)))).Outcomes["fits"];
        Check(fits.Applied && Entry(world, hopper) == "S1|a|28", "A cut that clears the earlier cut's front by more than the check radius fits: " + fits.Error);
        // A car the game does not know leaves the cut unchecked, so the location is refused with that reason.
        (world, _, _, hopper, scene) = Setup();
        world.CarLengths.Remove("rr/gb-gondola02");
        var unknown = new PatchEngine(scene).Apply(Doc(Starter("unknown", HopperPath, At("S1", "a", 80)))).Outcomes["unknown"];
        Check(!unknown.Applied && unknown.Error.Contains("cannot be checked") && unknown.Error.Contains("No car for identifier: rr/gb-gondola02"), "An unknown car refuses the check: " + unknown.Error);
        // Another cut's own failure is that marker's matter: it does not refuse this marker's location.
        (world, _, _, hopper, scene) = Setup();
        var wreck = (FakeMarker)world.Objects[WreckPath][0];
        wreck.Distance = 5;
        var independent = new PatchEngine(scene).Apply(Doc(Starter("independent", HopperPath, At("S2", "a", 80)))).Outcomes["independent"];
        Check(independent.Applied, "A failure of another cut that this cut does not cause does not refuse it: " + independent.Error);
    }
    // A starter placement changes one car placement: a marker that no active car placement reads, or several read, is
    // refused, and so is a scene patch file that names another setup than the one that reads the marker.
    static void TheMarkerNeedsOneReader() {
        var (world, _, _, hopper, scene) = Setup();
        world.Placements.RemoveAt(2);
        var none = new PatchEngine(scene).Apply(Doc(Starter("none", HopperPath, At("S2", "a", 80)))).Outcomes["none"];
        Check(!none.Applied && none.Error == StarterPlacements.NoReader && Entry(world, hopper) == "S2|a|90", "A marker no car placement reads is refused: " + none.Error);
        (world, _, _, hopper, scene) = Setup();
        world.Placements.Add(new StarterCarPlacement("ewh-diesel", true, 0, 1, hopper, new[] { "rr/gb-gondola3" }, false));
        var several = new PatchEngine(scene).Apply(Doc(Starter("several", HopperPath, At("S2", "a", 80)))).Outcomes["several"];
        Check(!several.Applied && several.Error.StartsWith("2 new-game car placements read this marker (setup ewh-steam, car placement 3 of 3; setup ewh-diesel, car placement 1 of 1)", StringComparison.Ordinal) && Entry(world, hopper) == "S2|a|90", "A marker several car placements read is refused: " + several.Error);
        (world, _, _, hopper, scene) = Setup();
        var other = new PatchEngine(scene).Apply(Doc(Starter("other", HopperPath, At("S2", "a", 80), "ewh-diesel"))).Outcomes["other"];
        Check(!other.Applied && other.Error.Contains("names setup ewh-diesel, but the car placement that reads this marker is setup ewh-steam, car placement 3 of 3"), "A scene patch file naming another setup is refused: " + other.Error);
        (world, _, _, hopper, scene) = Setup();
        world.FailSetups = true;
        var unreadable = new PatchEngine(scene).Apply(Doc(Starter("unreadable", HopperPath, At("S2", "a", 80)))).Outcomes["unreadable"];
        Check(!unreadable.Applied && unreadable.Error.Contains("placements unavailable") && Entry(world, hopper) == "S2|a|90", "Setups that cannot be read refuse the placement: " + unreadable.Error);
    }
    // Two cuts that swap places: the gondolas move to the locomotive's old place and the locomotive moves away. Each cut
    // is judged with the scene patch file's other starter placement at its desired location, so the scene patch file applies whichever
    // modification runs first; judged against the current locations, the one that runs first would be blocked.
    static void CutsAreJudgedWithTheScenePatchFilesOtherStarterPlacements() {
        foreach (var keys in new[] { new[] { "a-hopper", "b-shed" }, new[] { "b-hopper", "a-shed" } }) {
            var (world, _, shed, hopper, scene) = Setup();
            var result = new PatchEngine(scene).Apply(Doc(Starter(keys[0], HopperPath, At("S2", "a", 50)) + "," + Starter(keys[1], ShedPath, At("S2", "a", 90))));
            Check(result.Outcomes[keys[0]].Applied && result.Outcomes[keys[1]].Applied, "Both swapped cuts apply in key order " + String.Join(",", keys) + ": " + result.Outcomes[keys[0]].Error + " | " + result.Outcomes[keys[1]].Error);
            Check(Entry(world, hopper) == "S2|a|50" && shed.Distance == 90, "Both markers stand at their new track locations");
        }
        // Scene patch editing moves the locomotive away to S1 first and then the gondolas into its old place, and judges
        // each edit with the scene patch file as it stands; the saved scene patch file then applies at load, where the gondolas' key
        // sorts first and the locomotive still stands in that place.
        var (w, _, s2, h2, sc) = Setup();
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(sc), mapEditor, store); mapEditor.Session = session;
        Check(session.PlaceStarter(Shed, new TrackLocation("S1", "a", 80)).Applied && session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 50)).Applied, "Scene patch editing accepts the two moves one after the other");
        Check(session.Recheck(Shed).Applied && session.Recheck(Hopper).Applied, "Recheck judges each cut with the scene patch file's other starter placement");
        Check(mapEditor.Save() == null, "Save succeeds");
        var keysInFile = Newtonsoft.Json.Linq.JObject.Parse(store.Patch)["modifications"].Cast<Newtonsoft.Json.Linq.JProperty>().Select(p => p.Name).ToArray();
        var (world2, _, shed2, hopper2, scene2) = Setup();
        var reloaded = new PatchEngine(scene2).Apply(store.Patch);
        Check(reloaded.Outcomes.Values.All(o => o.Applied) && Entry(world2, hopper2) == "S2|a|50" && shed2.Segment == "S1" && shed2.Distance == 80, "The saved scene patch file applies at load whatever the order of its keys (" + String.Join(",", keysInFile) + "): " + String.Join(" | ", reloaded.Outcomes.Select(o => o.Key + "=" + o.Value.Error)));
        // A modification that cannot apply leaves no planned location behind for a later scene patch file.
        var (world3, _, _, hopper3, scene3) = Setup();
        var engine3 = new PatchEngine(scene3);
        Check(engine3.Apply(Doc(Starter("away", ShedPath, At("S1", "a", 80)))).Outcomes["away"].Applied, "The first scene patch file moves the locomotive to S1");
        var later = engine3.Apply(Doc(Starter("in", HopperPath, At("S1", "a", 78))));
        Check(!later.Outcomes["in"].Applied && later.Outcomes["in"].Error.Contains("Blocked by the cut of setup ewh-steam, car placement 2 of 3"), "A later scene patch file is judged against the applied locations: " + later.Outcomes["in"].Error);
    }
    // A failure inside the game while the track is walked is a refusal with its reason, and nothing changes.
    static void ANativeFailureOnTheTrackIsARefusal() {
        var (world, wreck, _, hopper, scene) = Setup();
        world.NoPositionAt = 25;
        var outcome = new PatchEngine(scene).Apply(Doc(Starter("fails", HopperPath, At("S1", "a", 28)))).Outcomes["fails"];
        Check(!outcome.Applied && outcome.Cause == FailureCause.InvalidDefinition && outcome.Error.Contains("The track could not be walked: position injected") && Entry(world, hopper) == "S2|a|90", "A native failure on the track refuses with its reason: " + outcome.Error);
        var body = StarterCutRule.Body(world, new TrackLocation("S1", "a", 28), 10, out var error);
        Check(error != null && error.Contains("position injected") && body.Count > 0, "A body that cannot be followed says why: " + error);
    }
    // A starter placement marker whose marker id another scene marker holds keeps its track location: the game removes
    // a registration by marker id from every holder, so the move is refused before anything changes.
    static void ADuplicateMarkerIdIsRefusedBeforeMutation() {
        var (world, _, _, hopper, scene) = Setup();
        var twin = new FakeMarker { Id = "Mhop", Segment = "S1", End = "a", Distance = 5, Active = false };
        world.Objects["World/Twin"] = new List<object> { twin };
        world.Mutations = 0;
        var outcome = new PatchEngine(scene).Apply(Doc(Starter("dup-id", HopperPath, At("S2", "a", 80)))).Outcomes["dup-id"];
        Check(!outcome.Applied && outcome.Error.StartsWith("Duplicate marker id Mhop", StringComparison.Ordinal) && world.Mutations == 0 && Entry(world, hopper) == "S2|a|90",
            "A holder of a duplicate marker id is refused before mutation: " + outcome.Error);
    }
    static void ObjectSettingsAndOtherRelationshipsAreRefused() {
        Refused(Starter("placed", HopperPath, At("S2", "a", 80), extra: ",\"active\":false"), "placed", "the object's placement and activity do not change where its cars start", "Activity on a starter placement marker is refused");
        Refused("\"rel\":{\"target\":{\"scene\":\"level4\",\"path\":\"" + HopperPath + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"starter-placement-v1\"},\"relationships\":{\"trackMarker\":" + At("S2", "a", 80) + "},\"dependsOn\":[]}", "rel", "set through starterPlacement", "A trackMarker relationship on a starter placement marker is refused");
        var (world, _, _, _, scene) = Setup();
        var malformed = new PatchEngine(scene).Apply(Doc("\"bad\":{\"target\":{\"scene\":\"level4\",\"path\":\"" + HopperPath + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"starter-placement-v1\"},\"starterPlacement\":{\"track\":" + At("S2", "a", 80) + "},\"dependsOn\":[]}")).Outcomes["bad"];
        Check(!malformed.Applied && malformed.Error.Contains("setup must name the setup"), "A starter placement without its setup is invalid: " + malformed.Error);
    }
    static void OtherCapabilitiesRefuseAStarterPlacement() {
        var world = new FakeMarkerWorld();
        var sign = new FakeMarker { Id = "Mi4f", Segment = "S9sc", End = "a", Distance = 2 };
        world.Objects["Sign"] = new List<object> { sign }; world.Load(sign);
        var outcome = new PatchEngine(new TrackMarkerScene(world, Capability.Unchecked)).Apply(Doc("\"s\":{\"target\":{\"scene\":\"level4\",\"path\":\"Sign\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"},\"starterPlacement\":{\"setup\":\"ewh-steam\",\"track\":" + At("Nnew", "a", 3) + "},\"dependsOn\":[]}")).Outcomes["s"];
        Check(!outcome.Applied && outcome.Error == StarterPlacements.NotAStarter && sign.Segment == "S9sc", "A Generic marker refuses a starter placement: " + outcome.Error);
    }
    static void RepeatedApplicationConverges() {
        var (world, _, _, hopper, scene) = Setup();
        var engine = new PatchEngine(scene);
        var scenePatchFile = Doc(Starter("move-hopper", HopperPath, At("S2", "a", 80)));
        engine.Apply(scenePatchFile); var mutations = world.Mutations;
        var again = engine.Apply(scenePatchFile).Outcomes["move-hopper"];
        Check(again.Applied && Entry(world, hopper) == "S2|a|80", "A second application converges on the same track location with one registration");
        Check(world.Mutations == mutations, "A converged marker is not written again: " + (world.Mutations - mutations));
        hopper.Distance = 30; foreach (var b in world.Buckets.Values) b.Remove("Mhop"); world.Load(hopper);
        Check(engine.Apply(scenePatchFile).Outcomes["move-hopper"].Applied && Entry(world, hopper) == "S2|a|80", "An application after the marker drifted returns it to the patched location");
    }
    static void FailedAttemptRestoresThePriorLocation() {
        foreach (var inject in new Action<FakeMarkerWorld>[] { w => w.ThrowAfterLocation = true, w => w.SkipRegister = true }) {
            var (world, wreck, _, hopper, scene) = Setup();
            inject(world);
            var engine = new PatchEngine(scene);
            var outcome = engine.Apply(Doc(Starter("move-hopper", HopperPath, At("S2", "a", 80)))).Outcomes["move-hopper"];
            Check(!outcome.Applied && outcome.Error.StartsWith("The change failed. The object is back as it was. Reason:", StringComparison.Ordinal), "A failure after the write is restored: " + outcome.Error);
            Check(hopper.Segment == "S2" && hopper.Distance == 90 && Entry(world, hopper) == "S2|a|90", "The captured track location and registration return");
            Check(!engine.Apply(Doc(Starter("move-hopper", HopperPath, At("S2", "a", 80)))).Outcomes["move-hopper"].Applied, "A failed starter placement is not retried in the same load");
        }
    }
    // The old registration cannot be removed: the new one stands beside it, and the restoration cannot remove that one
    // either. S1/a/80 lies on another segment than the marker's own, so the two registrations stay apart.
    static void UnverifiedCleanupBlocksTheRetry() {
        var (world, _, _, hopper, scene) = Setup();
        world.StickyRegistration = true;
        var engine = new PatchEngine(scene);
        var outcome = engine.Apply(Doc(Starter("move-hopper", HopperPath, At("S1", "a", 80)))).Outcomes["move-hopper"];
        Check(!outcome.Applied && outcome.Error.Contains("could not be verified"), "Unverified cleanup is reported explicitly: " + outcome.Error);
        world.StickyRegistration = false; var mutations = world.Mutations;
        var retry = engine.Apply(Doc(Starter("other", HopperPath, At("S2", "a", 95)))).Outcomes["other"];
        Check(!retry.Applied && retry.Error.Contains("could not be verified") && world.Mutations == mutations, "Another operation on the marker is blocked until cleanup is verified: " + retry.Error);
        // In scene patch editing the retry stays blocked; Recheck refuses while the registration is wrong, and verifies
        // the cleanup and lifts the block once it is right.
        (world, _, _, hopper, scene) = Setup();
        var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(scene), mapEditor, store); mapEditor.Session = session;
        world.StickyRegistration = true;
        var failed = session.PlaceStarter(Hopper, new TrackLocation("S1", "a", 80));
        Check(!failed.Applied && failed.Error.Contains("could not be verified"), "A failed edit with unverified cleanup says so: " + failed.Error);
        world.StickyRegistration = false;
        var blocked = session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 95));
        Check(!blocked.Applied && blocked.Error.Contains("Recheck"), "The retry is blocked until Recheck: " + blocked.Error);
        Check(!session.Recheck(Hopper).Applied, "Recheck refuses while the graph still holds the stray registration");
        world.Buckets["S1"].Remove("Mhop");
        var recheck = session.Recheck(Hopper);
        Check(recheck.Applied && Entry(world, hopper) == "S2|a|90", "Recheck verifies the restored state: " + recheck.Error);
        Check(session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 95)).Applied && Entry(world, hopper) == "S2|a|95", "After Recheck the marker can be placed again");
    }
    static (FakeStarterWorld world, FakeMarker hopper, PatchEditingSession session, NativeMapEditor mapEditor, Store store) Session(string patch = null) {
        var (world, _, _, hopper, scene) = Setup();
        var store = new Store { Patch = patch }; var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(new PatchEngine(scene), mapEditor, store); mapEditor.Session = session;
        return (world, hopper, session, mapEditor, store);
    }
    static JObject Only(PatchEditingSession session) => (JObject)JObject.Parse(session.ScenePatchFileText)["modifications"].Single().First;
    static void EditingPlacesUndoesAndRedoes() {
        var (world, hopper, session, mapEditor, store) = Session();
        Check(!mapEditor.IsDirty, "A fresh session is clean");
        var placed = session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 80));
        Check(placed.Applied && Entry(world, hopper) == "S2|a|80" && hopper.Position[0] == 20, "Placing the marker sets its track location and registration without moving the object: " + placed.Error);
        var item = Only(session);
        Check(((JProperty)item.Parent).Name == "starter-ewh-int2", "A new modification's key names the marker without repeating starter-: " + ((JProperty)item.Parent).Name);
        Check((string)item["starterPlacement"]["setup"] == "ewh-steam" && (string)item["starterPlacement"]["track"]["segment"] == "S2" && (double)item["starterPlacement"]["track"]["distance"] == 80
            && item["placement"] == null && item["active"] == null && item["relationships"] == null && (string)item["target"]["structure"] == StarterPlacementScene.Structure, "The scene patch file records the setup and the track location only: " + item);
        Check(session.HasStarterTrack(Hopper) && session.Changes(Hopper).SequenceEqual(new[] { "Track location: from S2 / a / 90 to S2 / a / 80" }), "The Changes lines name the track location: " + String.Join(" | ", session.Changes(Hopper)));
        Check(mapEditor.IsDirty && session.Outstanding(mapEditor.NativeDirty).Contains(PatchEditingSession.PatchData), "The edit leaves the scene patch file outstanding");
        Check(session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 85)).Applied && Entry(world, hopper) == "S2|a|85", "A second placement replaces the location");
        mapEditor.Undo();
        Check(Entry(world, hopper) == "S2|a|80" && (double)Only(session)["starterPlacement"]["track"]["distance"] == 80, "Undo returns the track location from before the edit");
        mapEditor.Undo();
        Check(Entry(world, hopper) == "S2|a|90" && session.ScenePatchFileText.Contains("\"modifications\": {}") && !mapEditor.IsDirty, "Undo of the first placement returns the map's location and a clean state");
        mapEditor.Redo();
        Check(Entry(world, hopper) == "S2|a|80" && mapEditor.IsDirty, "Redo reapplies the edit and the dirty state follows");
        mapEditor.Redo();
        Check(Entry(world, hopper) == "S2|a|85", "Redo reapplies the replacement");
        Check(mapEditor.Save() == null && !mapEditor.IsDirty && store.Patch.Contains("starterPlacement"), "Save writes the starter placement and leaves a clean state");
        mapEditor.Undo();
        Check(mapEditor.IsDirty && session.Outstanding(mapEditor.NativeDirty).Contains(PatchEditingSession.PatchData) && Entry(world, hopper) == "S2|a|80", "Undo after Save makes the patch outstanding again");
        mapEditor.Redo();
        Check(!mapEditor.IsDirty, "Redo back to the saved state is clean again");
        // Placing the marker back on the map's own location reverts the setting, and the modification leaves the scene patch file.
        Check(session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 90)).Reverted && session.ScenePatchFileText.Contains("\"modifications\": {}") && Entry(world, hopper) == "S2|a|90", "Placing the marker on its underlying location reverts the modification");
    }
    static void EditingRefusesBeforeAnythingChanges() {
        var (world, hopper, session, mapEditor, store) = Session();
        var before = session.ScenePatchFileText;
        foreach (var bad in new[] { new TrackLocation("Sgone", "a", 1), new TrackLocation("S1", "a", 999), new TrackLocation("S1", "a", 10), new TrackLocation("S1", "a", 45) }) {
            var refused = session.PlaceStarter(Hopper, bad);
            Check(!refused.Applied && refused.Error != null && session.ScenePatchFileText == before && Entry(world, hopper) == "S2|a|90" && !mapEditor.CanUndo, "An invalid location is refused before anything changes: " + bad.Display + ": " + refused.Error);
        }
        // A location equal to the map's own changes nothing and records no history step.
        Check(session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 90)).Unchanged && !mapEditor.CanUndo, "The current location changes nothing");
        Check(!session.ClearStarterTrack(Hopper).Applied, "Clearing an absent track location is refused");
        Check(!session.Edit(Hopper, new Placement(new[] { 1.0, 2, 3 }, new[] { 0.0, 0, 0, 1 }), null).Applied && hopper.Position[0] == 20, "The object's placement is not edited");
    }
    static void RemovalReturnsTheUnderlyingLocation() {
        var (world, hopper, session, mapEditor, store) = Session();
        session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 80));
        var cleared = session.ClearStarterTrack(Hopper);
        Check(cleared.Applied && Entry(world, hopper) == "S2|a|90" && session.ScenePatchFileText.Contains("\"modifications\": {}"), "Clearing the track location returns the underlying location and removes the empty modification: " + cleared.Error);
        mapEditor.Undo(); Check(Entry(world, hopper) == "S2|a|80", "Undo of the clearing restores this mod's location");
        var removed = session.Remove(Hopper);
        Check(removed.Applied && Entry(world, hopper) == "S2|a|90" && hopper.Position[0] == 20 && hopper.Active, "Removal returns the effective underlying track location computed from the native context");
        mapEditor.Undo(); Check(Entry(world, hopper) == "S2|a|80", "Undo of the removal restores the patched location");
        // Removal computes the underlying state from the context the engine captured before its first mutation, not from
        // a snapshot of the last edit.
        (world, hopper, session, mapEditor, store) = Session(Doc(Starter("starter-hopper", HopperPath, At("S2", "a", 95))));
        Check(session.Remove(Hopper).Applied && Entry(world, hopper) == "S2|a|90", "Removing a loaded modification returns the map's own location");
    }
    static void SaveWritesTheCanonicalMemberAndRaisesTheMinimum() {
        var (world, hopper, session, mapEditor, store) = Session();
        store.Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"rr/game >=0.6.2\"]}}";
        session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 80));
        Check(session.MinimumPatcherVersion == StarterPlacementScene.RequiredVersion, "A starter placement requires its capability's version: " + session.MinimumPatcherVersion);
        Check(mapEditor.Save() == null, "Save succeeds");
        var saved = JObject.Parse(store.Patch);
        var item = (JObject)saved["modifications"].Single().First;
        Check((int)saved["formatVersion"] == 1, "Save keeps format version 1");
        Check(String.Join(",", item.Properties().Select(p => p.Name)) == "target,starterPlacement,dependsOn", "The starter placement stands at its canonical position: " + String.Join(",", item.Properties().Select(p => p.Name)));
        Check(String.Join(",", ((JObject)item["starterPlacement"]).Properties().Select(p => p.Name)) == "setup,track" && String.Join(",", ((JObject)item["starterPlacement"]["track"]).Properties().Select(p => p.Name)) == "segment,end,distance", "The starter placement's members stand in canonical order");
        Check(store.Info.Contains("crankyant/scene-patcher >=" + StarterPlacementScene.RequiredVersion), "Save raises the minimum patcher dependency: " + store.Info);
        // A stricter existing constraint stays.
        (world, hopper, session, mapEditor, store) = Session();
        store.Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"rr/game >=0.6.2\",\"crankyant/scene-patcher >=0.9.0\"]}}";
        session = new PatchEditingSession(new PatchEngine(new StarterPlacementScene(world, Capability.Unchecked)), mapEditor, store); mapEditor.Session = session;
        session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 80));
        Check(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.9.0") && !store.Info.Contains(">=" + StarterPlacementScene.RequiredVersion + "\""), "A stricter existing constraint is retained: " + store.Info);
        // A scene patch file in another member order reads as the same content and loads unchanged.
        var reordered = "{\"modifications\":{\"starter-int2\":{\"dependsOn\":[],\"starterPlacement\":{\"track\":{\"distance\":80,\"end\":\"a\",\"segment\":\"S2\"},\"setup\":\"ewh-steam\"},\"target\":{\"structure\":\"starter-placement-v1\",\"component\":\"Track.TrackMarker\",\"path\":\"" + HopperPath + "\",\"scene\":\"level4\"}}},\"sourceMap\":{\"mapId\":\"map\"},\"formatVersion\":1}";
        (world, hopper, session, mapEditor, store) = Session(reordered);
        Check(Entry(world, hopper) == "S2|a|90" && !session.Outstanding(mapEditor.NativeDirty).Contains(PatchEditingSession.PatchData), "A reordered scene patch file is read without a patch change");
        Check(session.PlaceStarter(Hopper, new TrackLocation("S2", "a", 85)).Applied && mapEditor.Save() == null, "An edit of the reordered scene patch file saves");
        var written = (JObject)JObject.Parse(store.Patch)["modifications"]["starter-int2"];
        Check(String.Join(",", written.Properties().Select(p => p.Name)) == "target,starterPlacement,dependsOn" && String.Join(",", ((JObject)written["starterPlacement"]).Properties().Select(p => p.Name)) == "setup,track", "Save writes the edited scene patch file in canonical order: " + written);
    }
}
