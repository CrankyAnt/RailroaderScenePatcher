// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// Starter placement markers: the markers that a new-game car placement of a loaded setup holds. The controlled setup
// mirrors the base game's: an active setup ewh-steam with car placements, an inactive setup, and markers that no setup
// holds.
static class StarterPlacementTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    sealed class Marker { public string Name; public override string ToString() => Name; }
    static readonly Marker Wreck = new Marker { Name = "starter-ewh-wreck" }, Shed = new Marker { Name = "starter-ewh-shed1" },
        Hopper = new Marker { Name = "starter-ewh-int2" }, Retired = new Marker { Name = "starter-old" }, Sign = new Marker { Name = "Whistle Sign" };
    static StarterSetups Setups() => new StarterSetups(new[] {
        new StarterCarPlacement("ewh-steam", true, 0, 3, Wreck, new[] { "rr/ls-262-g16" }, true),
        new StarterCarPlacement("ewh-steam", true, 1, 3, Shed, new[] { "rr/ls-460-t17" }, false),
        new StarterCarPlacement("ewh-steam", true, 2, 3, Hopper, new[] { "rr/gb-gondola3", "rr/gb-gondola02" }, false),
        // An inactive setup is never read at a new game: its own marker is unused, and it adds no reader to a used one.
        new StarterCarPlacement("old-setup", false, 0, 2, Retired, new[] { "rr/ne-caboose01" }, false),
        new StarterCarPlacement("old-setup", false, 1, 2, Shed, new[] { "rr/xm-boxcar05" }, true),
    });

    internal static void Run() {
        FamilyFollowsTheActiveSetups();
        ReadersNameEverySetupAndPlacement();
        UsedBindingsAreReadWhateverTheActivity();
        FocusGoesToTheTrackPoint();
        TheOutlinerListsUsedMarkersOnce();
        EditsOutsideTheWorkflowSayWhy();
        AMarkerOfAnActiveAndAnInactiveSetupIsReadForEveryBinding();
        UnreadableSetupsAreVisible();
    }

    // A marker that an active and an inactive setup both hold is used: the inventory's reading of it gives the need of a
    // marker the game reads whatever its activity, and only the active setup as a reader, for every binding state.
    static void AMarkerOfAnActiveAndAnInactiveSetupIsReadForEveryBinding() {
        var setups = Setups();
        var reading = setups.Reading(Shed, "Generic", false);
        Check(reading.Need == BindingNeed.ReadAnyway && reading.Note == null && reading.Readers.SequenceEqual(new[] { "Read by setup ewh-steam, car placement 2 of 3: cars rr/ls-460-t17; wreck: no" }),
            "The reading of a marker of an active and an inactive setup takes the active one: " + String.Join(" | ", reading.Readers));
        foreach (var enabled in new[] { true, false }) {
            foreach (var state in new[] { new object[] { "", (bool?)false, "Marker has an empty segment binding" }, new object[] { "Sgone", (bool?)false, "Marker broken binding: missing segment Sgone" }, new object[] { "S1", (bool?)true, null } }) {
                var scene = new SetupScene { Setups = setups };
                scene.Markers.Add(new KeyValuePair<Marker, object[]>(Shed, new object[] { enabled, state[0], state[1] }));
                var inventory = new SceneInventory(scene); inventory.Refresh();
                var entry = inventory.Entries.Single();
                var expected = (string)state[2];
                Check(entry.Family == StarterPlacements.Family && (expected == null ? entry.Problems.Count == 0 : entry.Problems.Single().StartsWith(expected, StringComparison.Ordinal)),
                    "Binding '" + state[0] + "', enabled " + enabled + ": " + String.Join(" | ", entry.Problems));
            }
        }
        var other = setups.Reading(Retired, "Generic", false);
        Check(other.Need == BindingNeed.Optional && other.Readers.Count == 0, "A marker only an inactive setup holds is read as an ordinary marker");
    }

    // When the setups cannot be read, no marker can be told to be a starter placement marker. The inventory keeps every
    // marker in its own family, and each marker carries an inspection note that says why; the Refresh reports it once.
    static void UnreadableSetupsAreVisible() {
        var unreadable = StarterSetups.Unreadable("placements unavailable");
        Check(unreadable.Error == "placements unavailable" && unreadable.Use(Wreck) == StarterUse.None && unreadable.Family(Wreck, "Track marker") == "Track marker",
            "Unreadable setups classify no marker as a starter placement marker");
        Check(StarterPlacements.Unreadable("placements unavailable") == "Inspection unavailable: the new-game setups could not be read, so starter placement markers cannot be identified: placements unavailable"
            && BindingRules.IsNote(StarterPlacements.Unreadable("x")), "The note names the starter setups and is an inspection note");
        var scene = new SetupScene { Setups = unreadable };
        scene.Markers.Add(new KeyValuePair<Marker, object[]>(Wreck, new object[] { false, "", (bool?)false }));
        scene.Markers.Add(new KeyValuePair<Marker, object[]>(Sign, new object[] { true, "S2", (bool?)true }));
        var inventory = new SceneInventory(scene); inventory.Refresh();
        Check(inventory.Entries.All(e => e.Family == "Track marker" && e.Problems.Count == 0 && e.Notes.SequenceEqual(new[] { StarterPlacements.Unreadable("placements unavailable") })),
            "Every marker stays listed in its own family with the note and no problem: " + String.Join(" | ", inventory.Entries.Select(e => e.Family + "/" + String.Join(",", e.Notes))));
        Check(new StarterSetups(null).Error == null && new StarterSetups(null).Reading(Wreck, "Generic", false).Note == null, "Readable setups add no note");
    }

    // A discovery over the controlled setup: every marker with its own activity and binding, observed the way the
    // inventory observes a marker, in the family the setups give it.
    sealed class SetupScene : IInspectionScene {
        public IEnumerable<string> MapIds => new[] { "map" };
        public readonly List<KeyValuePair<Marker, object[]>> Markers = new List<KeyValuePair<Marker, object[]>>();
        public StarterSetups Setups;
        public IEnumerable<SceneObservation> Discover() {
            foreach (var pair in Markers) {
                var marker = pair.Key; var enabled = (bool)pair.Value[0]; var segment = (string)pair.Value[1]; var resolves = (bool?)pair.Value[2];
                var family = Setups.Family(marker, "Track marker");
                if (family == null) continue;
                var starter = family == StarterPlacements.Family;
                var bindings = new List<string> { "Marker ID=" + marker.Name + "; type=Generic", "Marker segment=" + segment + "; end=b; distance=45" };
                // The inventory's reading of the marker: what the game needs from its binding, its readers and a note.
                var reading = Setups.Reading(marker, "Generic", false);
                bindings.AddRange(reading.Readers);
                var problem = BindingRules.Problem(reading.Need, enabled, "Marker", segment, resolves);
                var lines = new List<string>(); if (problem != null) lines.Add(problem); if (reading.Note != null) lines.Add(reading.Note);
                var defects = BindingRules.Problems(enabled, lines.Where(l => !BindingRules.IsNote(l)), reading.Need == BindingNeed.ReadAnyway ? lines.Where(l => !BindingRules.IsNote(l)) : new string[0]);
                yield return new SceneObservation(marker, new TargetIdentity("level4", "Setups/Starter Equipment Setup/" + marker.Name, "Track.TrackMarker", starter ? null : "track-marker-v1"), family, enabled,
                    new double[] { 1, 2, 3 }, new double[] { 0, 0, 0, 1 }, bindings, new string[0], defects,
                    starter ? StarterPlacements.Operations : "Track binding", null, enabled, lines.Where(BindingRules.IsNote).ToArray(), null);
            }
        }
    }

    // Every used marker appears once, active or not and whatever its binding; an unused one is absent.
    static void TheOutlinerListsUsedMarkersOnce() {
        var scene = new SetupScene { Setups = Setups() };
        scene.Markers.Add(new KeyValuePair<Marker, object[]>(Wreck, new object[] { true, "Stky030z8", (bool?)true }));
        scene.Markers.Add(new KeyValuePair<Marker, object[]>(Shed, new object[] { false, "", (bool?)false }));
        scene.Markers.Add(new KeyValuePair<Marker, object[]>(Hopper, new object[] { false, "Sgone", (bool?)false }));
        scene.Markers.Add(new KeyValuePair<Marker, object[]>(Retired, new object[] { true, "S1", (bool?)true }));
        scene.Markers.Add(new KeyValuePair<Marker, object[]>(Sign, new object[] { true, "S2", (bool?)true }));
        var inventory = new SceneInventory(scene); inventory.Refresh();
        Check(!inventory.Entries.Any(e => ReferenceEquals(e.Handle, Retired)) && !inventory.Search("starter-old").Any(), "The unused marker is absent from the inventory, also from search");
        var problems = RailroaderScenePatcher.InventoryPresentationState.Rows(inventory, "", RailroaderScenePatcher.OutlinerFilter.Problems, null).Select(RailroaderScenePatcher.InventoryPresentationState.Name).ToArray();
        Check(problems.SequenceEqual(new[] { "starter-ewh-shed1", "starter-ewh-int2" }), "The Problems filter lists the disabled used markers with an empty and a missing binding: " + String.Join(",", problems));
        var wreck = inventory.Entries.Single(e => ReferenceEquals(e.Handle, Wreck));
        Check(wreck.InspectionOnly == null && wreck.Problems.Count == 0 && wreck.Bindings.Any(b => b.StartsWith(StarterPlacements.ReaderPrefix, StringComparison.Ordinal)), "A used marker is a supported kind with its readers, not inspection only");
    }

    // The refusal that named setup consumption as unsupported is gone. Supported edits names the track location as the
    // marker's edit; the object's placement, activity and ordinary marker binding stay disabled, with the reason that a
    // car placement reads only the track location.
    static void EditsOutsideTheWorkflowSayWhy() {
        Check(RailroaderScenePatcher.CommandBand.Refusal(StarterPlacements.Operations) == null && StarterPlacements.Operations.StartsWith("Track location:", StringComparison.Ordinal)
            && StarterPlacements.Operations.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) < 0, "Supported edits names the track location as the edit: " + StarterPlacements.Operations);
        var blocked = RailroaderScenePatcher.CommandBand.NotEditable(StarterPlacements.ObjectSettings);
        Check(RailroaderScenePatcher.CommandBand.ModeBlock(RailroaderScenePatcher.CommandBand.GizmoMode, false, false, blocked) == RailroaderScenePatcher.CommandBand.EditReason(blocked)
            && RailroaderScenePatcher.CommandBand.EditReason(blocked).StartsWith("A new-game car placement reads only", StringComparison.Ordinal), "A disabled placement mode gives that reason: " + RailroaderScenePatcher.CommandBand.EditReason(blocked));
        Check(StarterPlacements.MarkerCapabilityRefusal.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) < 0 && StarterPlacements.MarkerCapabilityRefusal.Contains("starter placement"),
            "The marker capability names the starter placement workflow, not an unsupported consumer: " + StarterPlacements.MarkerCapabilityRefusal);
    }
    // The problems a marker is listed with, as the inventory decides them: what the game needs from its binding,
    // judged against the marker's own activity.
    static IReadOnlyList<string> Problems(StarterSetups setups, object marker, bool enabled, string segment, bool? resolves) {
        var need = BindingRules.Need("Generic", false, setups.Use(marker) == StarterUse.Used);
        var problem = BindingRules.Problem(need, enabled, "Marker", segment, resolves);
        var all = problem == null ? new string[0] : new[] { problem };
        return BindingRules.Problems(enabled, all, need == BindingNeed.ReadAnyway ? all : new string[0]);
    }

    // A used marker needs a usable binding whether it is active or not, because the game reads it either way.
    static void UsedBindingsAreReadWhateverTheActivity() {
        var setups = Setups();
        foreach (var enabled in new[] { true, false }) {
            var empty = Problems(setups, Wreck, enabled, "", false);
            Check(empty.Count == 1 && empty[0].StartsWith("Marker has an empty segment binding", StringComparison.Ordinal), "A used marker with an empty binding shows a problem, enabled " + enabled);
            var missing = Problems(setups, Shed, enabled, "Sgone", false);
            Check(missing.Count == 1 && missing[0].StartsWith("Marker broken binding: missing segment Sgone", StringComparison.Ordinal), "A used marker with a missing segment shows a broken-binding problem, enabled " + enabled);
            Check(Problems(setups, Hopper, enabled, "Stky030z8", true).Count == 0, "A used marker with a usable binding shows no problem, enabled " + enabled);
        }
        // An ordinary marker keeps the rule that an inactive object is never a problem.
        Check(Problems(setups, Sign, false, "Sgone", false).Count == 0 && Problems(setups, Sign, true, "Sgone", false).Count == 1, "A marker no setup holds keeps the ordinary rule");
    }

    // A new-game car placement reads its marker's track location, not its object position: Focus on a starter
    // placement marker goes to the track point, and is disabled with its reason without one. Other objects keep their
    // object position. starter-ewh-wreck: track point 13036.9 / 563.6 / 4402.8, about 660 m from its object.
    static void FocusGoesToTheTrackPoint() {
        var objectPosition = new double[] { 12450.2, 560.5, 4700.1 };
        var trackPoint = new double[] { 13036.9, 563.6, 4402.8 };
        Check(FocusTarget.Point(StarterPlacements.Family, objectPosition, new[] { trackPoint }, out var refusal).SequenceEqual(trackPoint) && refusal == null,
            "Focus on a starter placement marker lands on its track point");
        Check(FocusTarget.Point(StarterPlacements.Family, objectPosition, new double[0][], out refusal) == null && refusal == StarterPlacements.NoTrackPoint,
            "Focus on a starter placement marker without a track point is refused with its reason");
        Check(FocusTarget.Point(StarterPlacements.Family, objectPosition, null, out refusal) == null && refusal == StarterPlacements.NoTrackPoint,
            "An unread track point is no track point");
        Check(FocusTarget.Point("Track marker", objectPosition, new[] { trackPoint }, out refusal).SequenceEqual(objectPosition) && refusal == null,
            "Focus on another object lands on its object position");
        // The command band disables Focus with that reason before it is clicked.
        Check(RailroaderScenePatcher.CommandBand.Focus(true, StarterPlacements.NoTrackPoint) == StarterPlacements.NoTrackPoint
            && RailroaderScenePatcher.CommandBand.Focus(true, null) == null && RailroaderScenePatcher.CommandBand.Focus(false, null) == "Inspect an object first.",
            "Focus is disabled with the reason it has no target");
    }

    // The inspector names every setup and car placement that reads the marker, with each car and its wreck setting.
    // An inactive setup reads nothing.
    static void ReadersNameEverySetupAndPlacement() {
        var setups = Setups();
        Check(setups.Readers(Wreck).SequenceEqual(new[] { "Read by setup ewh-steam, car placement 1 of 3: cars rr/ls-262-g16; wreck: yes" }),
            "A wreck placement names its car and says it is a wreck: " + String.Join(" | ", setups.Readers(Wreck)));
        Check(setups.Readers(Hopper).SequenceEqual(new[] { "Read by setup ewh-steam, car placement 3 of 3: cars rr/gb-gondola3, rr/gb-gondola02; wreck: no" }),
            "A placement of several cars names each car: " + String.Join(" | ", setups.Readers(Hopper)));
        Check(setups.Readers(Shed).SequenceEqual(new[] { "Read by setup ewh-steam, car placement 2 of 3: cars rr/ls-460-t17; wreck: no" }),
            "An inactive setup that holds the same marker is no reader: " + String.Join(" | ", setups.Readers(Shed)));
        var two = new StarterSetups(new[] {
            new StarterCarPlacement("ewh-steam", true, 0, 1, Wreck, new[] { "rr/ls-262-g16" }, true),
            new StarterCarPlacement("ewh-diesel", true, 4, 5, Wreck, new string[0], false),
        });
        Check(two.Readers(Wreck).SequenceEqual(new[] { "Read by setup ewh-steam, car placement 1 of 1: cars rr/ls-262-g16; wreck: yes", "Read by setup ewh-diesel, car placement 5 of 5: cars none; wreck: no" }),
            "Every active setup that reads the marker is named, in the order the setups were read: " + String.Join(" | ", two.Readers(Wreck)));
        Check(setups.Readers(Retired).Count == 0 && setups.Readers(Sign).Count == 0, "An unused marker and an ordinary marker have no readers");
    }

    // A marker is used when an active setup holds it; one only inactive setups hold is unused and stays out of the
    // inventory; one no setup holds is an ordinary marker.
    static void FamilyFollowsTheActiveSetups() {
        var setups = Setups();
        foreach (var used in new[] { Wreck, Shed, Hopper })
            Check(setups.Use(used) == StarterUse.Used && setups.Family(used, "Track marker") == StarterPlacements.Family, "A marker an active setup holds is in the Starter placement family: " + used);
        Check(setups.Use(Retired) == StarterUse.Unused && setups.Family(Retired, "Track marker") == null, "A marker only an inactive setup holds stays out of the inventory");
        Check(setups.Use(Sign) == StarterUse.None && setups.Family(Sign, "Track marker") == "Track marker", "A marker no setup holds keeps its own family");
        Check(StarterPlacements.Family == "Starter placement", "The family is named Starter placement");
    }
}
