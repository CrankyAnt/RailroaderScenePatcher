// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// The construction segments of a rail bridge construction as a relationship: omitted keeps the controller's native
// segments, an explicit reference replaces the set with exactly the selected segments, and an explicit detach is refused.
// Whole scene patch files apply against the controlled rail bridges of RailBridgeTests, whose construction controllers
// name their segments, and a controlled track graph that holds the loaded segment ids. The native split of Bryson
// Bridge 1's segment S-bryson leaves the halves S-bryson-a and S-bryson-b, as GraphEditor.SubdivideSegment does: the old
// id is gone and both halves have new ids.
static class ConstructionSegmentTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception("Construction segments: " + message); }
    const string SceneName = "BushnellWhittier-MapManager", Bryson = "World/Large Scenery/Bryson/Bridge 1", Almond = "World/Large Scenery/Almond Bridge";
    static TargetIdentity GroupId(string path) => new TargetIdentity(SceneName, path, BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
    static string Target(string path) => "\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + path + "\",\"component\":\"" + BakedSceneryGroups.ComponentType + "\",\"structure\":\"" + BakedSceneryGroups.Structure + "\"}";
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static string Segments(string id, string path, string relationship, string extra = "") => "\"" + id + "\":{" + Target(path) + ",\"relationships\":{\"constructionSegments\":" + relationship + "}" + extra + ",\"dependsOn\":[]}";
    static string Ids(IEnumerable<string> ids) => ids == null ? "none" : String.Join(",", ids);

    // A hand-written detach, or a reference with a missing segment, in the scene patch file is refused before mutation on
    // every route that applies the file: Use file version and Import, as at world load. The refused modification shows
    // as a failed modification, and Remove modification of it, with its Undo and Redo, keeps the history consistent.
    static void FileRoutesRefuseADetach() {
        // The footer of a file version without refusals stays as it was. It counts refused modifications as refused and
        // names the other failure kinds apart: an incomplete cleanup never reads as kept but not applied.
        KeyValuePair<string, (FailureKind, string)> Failed(string key, FailureKind kind, string reason) => new KeyValuePair<string, (FailureKind, string)>(key, (kind, reason));
        Assert(FilePanelState.FileVersionApplied(null, 1, new KeyValuePair<string, (FailureKind, string)>[0]) == "File version applied.", "A file version without refusals");
        var mixed = FilePanelState.FileVersionApplied(null, 1, new[] { Failed("b", FailureKind.Refused, "Reason one"), Failed("c", FailureKind.Refused, "Reason two."), Failed("d", FailureKind.IncompleteCleanup, "Reason three"), Failed("e", FailureKind.Restored, "Reason four") });
        Assert(mixed == "File version applied. 1 modification applied, 2 modifications refused, 1 modification failed and restored, 1 modification failed with incomplete cleanup. Kept in the scene patch file but not applied: b (Reason one); c (Reason two). Failed and restored: e (Reason four). Failed with incomplete cleanup: d (Reason three).", "Several failure kinds: " + mixed);
        Assert(FilePanelState.FileVersionApplied("Map mismatch", 0, new KeyValuePair<string, (FailureKind, string)>[0]) == "File version applied. Its modifications weren't applied: Map mismatch.", "A file version that does not apply at all");
        Assert(FilePanelState.FileVersionApplied(null) == "File version applied.", "No application result");
        foreach (var relationship in new[] { "{\"detach\":true}", "{\"segments\":[\"S-gone\"]}" }) {
            var expected = relationship.Contains("detach") ? ConstructionSegmentRule.NoDetach : "S-gone";
            // Use file version.
            var f = Split(); var store = new Store(); var mapEditor = new NativeMapEditor();
            var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
            string Now() => String.Join(",", f.World.SegmentsOf[f.Bridge1].OrderBy(x => x, StringComparer.Ordinal));
            store.Patch = Doc(Segments("bad", Bryson, relationship)); session.CheckFileVersion();
            var used = session.UseFileVersion();
            var outcome = session.LastVersionApplication?.Outcomes?["bad"];
            Assert(used.Applied && outcome != null && !outcome.Applied && outcome.Failure == FailureKind.Refused && outcome.Error.Contains(expected) && f.World.SegmentWrites == 0 && Now() == "S-bryson",
                "Use file version refuses " + relationship + " before mutation with the reason: " + outcome?.Error + " writes " + f.World.SegmentWrites + " now " + Now());
            // The footer names the refused modification with its reason, and the Changes tab marks it as refused.
            var footer = FilePanelState.FileVersionApplied(session.LastVersionApplication);
            Assert(footer == "File version applied. No modifications applied, 1 modification refused. Kept in the scene patch file but not applied: bad (" + outcome.Error.TrimEnd('.') + ").",
                "The footer states the refusal: " + footer);
            Assert(session.Changes(GroupId(Bryson)).SequenceEqual(new[] { "Refused. " + outcome.Error.TrimEnd('.') + "." }), "The Changes tab marks the modification as refused: " + String.Join(" | ", session.Changes(GroupId(Bryson))));
            // Remove modification of the refused modification, then Undo and Redo: nothing incomplete, the object unchanged.
            var afterUse = session.Outstanding(mapEditor.NativeDirty).ToArray(); var dirtyAfterUse = mapEditor.IsDirty;
            var removed = session.Remove(GroupId(Bryson));
            Assert(removed.Applied && Now() == "S-bryson", "Remove modification of the refused modification applies: " + removed.Error);
            Assert(!session.HasConstructionSegments(GroupId(Bryson)) && mapEditor.IsDirty, "After the removal the file no longer holds the refused modification and the title shows unsaved work");
            mapEditor.Undo();
            Assert(session.IncompleteOperation == null && Now() == "S-bryson" && f.World.SegmentWrites == 0, "Undo of the removal is consistent: " + session.IncompleteOperation + " now " + Now());
            Assert(session.HasConstructionSegments(GroupId(Bryson)) && mapEditor.IsDirty == dirtyAfterUse && session.Outstanding(mapEditor.NativeDirty).SequenceEqual(afterUse),
                "Undo puts the refused modification back as the file holds it, with the persistence of the file version: " + String.Join(", ", session.Outstanding(mapEditor.NativeDirty)));
            mapEditor.Redo();
            Assert(session.IncompleteOperation == null && Now() == "S-bryson" && !session.HasConstructionSegments(GroupId(Bryson)) && mapEditor.IsDirty, "Redo of the removal is consistent: " + session.IncompleteOperation);
            // No step is left that fails later: repeated Undo and Redo, also around a later edit of another object, stay complete.
            var moved = session.Edit(GroupId(Almond), new Placement(new[] { 3500.0, 540, 4400 }, new[] { 0.0, 0, 0, 1 }), null);
            Assert(moved.Applied, "A later edit of another object applies: " + moved.Error);
            for (var round = 0; round < 3; round++) {
                mapEditor.Undo(); mapEditor.Undo(); mapEditor.Redo(); mapEditor.Redo();
                Assert(session.IncompleteOperation == null && Now() == "S-bryson" && f.World.SegmentWrites == 0, "Round " + round + " of Undo and Redo stays complete: " + session.IncompleteOperation);
            }
            mapEditor.Undo(); mapEditor.Undo();
            Assert(session.IncompleteOperation == null && mapEditor.IsDirty == dirtyAfterUse && session.Outstanding(mapEditor.NativeDirty).SequenceEqual(afterUse),
                "Back at the file version the title and Save availability match it: " + String.Join(", ", session.Outstanding(mapEditor.NativeDirty)));
            // Import.
            var g = Split(); var importStore = new Store(); var importEditor = new NativeMapEditor();
            var importing = new PatchEditingSession(g.Engine, importEditor, importStore); importEditor.Session = importing;
            importStore.Patch = Doc(Segments("bad", Bryson, relationship)); importing.CheckFileVersion();
            Assert(importing.OpenImport().Applied, "Import opens");
            var imported = importing.ApplyImport();
            Assert(!imported.Applied && imported.Error.Contains(expected) && g.World.SegmentWrites == 0, "Import refuses " + relationship + " before mutation with the reason: " + imported.Error);
        }
    }

    // A refused edit leaves no refusal behind: Undo of a step whose target is gone afterwards stays incomplete.
    static void StaleRefusalLeavesAMissingTargetIncomplete() {
        var f = Split(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var girder = new TargetIdentity(SceneName, "World/Large Scenery/Plate Girder Bridge/Plate Girder Bridge (1)", BakedSceneryGroups.ComponentType, BakedSceneryGroups.Structure);
        // A move of the plate girder bridge and its removal: Undo of the removal restores an entry where the file holds none.
        Assert(session.Edit(girder, new Placement(new[] { 4100.0, 529, 5667 }, new[] { 0.0, 0, 0, 1 }), null).Applied && session.Remove(girder).Applied, "A move of the plate girder bridge and its removal apply");
        // A move the world refuses inside the edit's relationship check, before any mutation.
        f.World.PlacementRefused[f.Bridge1] = "S-gone placement refused";
        var refused = session.Edit(GroupId(Bryson), new Placement(new[] { 3500.0, 540, 4400 }, new[] { 0.0, 0, 0, 1 }), null);
        Assert(!refused.Applied && refused.Error.Contains("S-gone placement refused"), "A move of Bryson Bridge 1 is refused: " + refused.Error);
        f.World.Objects.Remove("World/Large Scenery/Plate Girder Bridge/Plate Girder Bridge (1)");
        mapEditor.Undo();
        Assert(session.IncompleteOperation != null && !session.IncompleteOperation.Contains("S-gone"), "Undo of the removal for a target that is gone stays incomplete with its own reason: " + session.IncompleteOperation);
    }

    // Bryson Bridge 1 after a native split of its construction segment: the controller still names the old id.
    internal static RailBridgeTests.Bridges Split() {
        var f = new RailBridgeTests.Bridges();
        f.World.Graph["S-bryson-a"] = 1; f.World.Graph["S-bryson-b"] = 1; f.World.Graph["S-almond"] = 1; f.World.Graph["S-twin"] = 2;
        f.World.SegmentsOf[f.Bridge1] = new[] { "S-bryson" }; f.World.SegmentsOf[f.AlmondRoot] = new[] { "S-almond" };
        return f;
    }

    public static void Run() {
        ReferenceReplacesTheSet();
        RefusalsComeBeforeMutation();
        FailuresRestoreThePriorSet();
        IncompleteCleanupBlocksTheRetryUntilRecheck();
        AWorldReloadReleasesTheBlockedRetry();
        RepairInScenePatchEditing();
        FileRoutesRefuseADetach();
        StaleRefusalLeavesAMissingTargetIncomplete();
        ElaControllerHoldsOnlyItsSegments();
        ConnectionsTabTexts();
        Console.WriteLine("PASS: construction segments");
    }

    // The separate construction controller at Ela, Ela Bridge Scenery: an object of its own whose only supported edit is its
    // construction segments. Its position has no effect and its activity runs the construction's visibility control, so
    // a placement or Enabled setting is refused before mutation with the reason.
    sealed class Scenery : IConstructionSceneryWorld {
        public string[] Ids = { "S-ela" };
        public readonly object Controller = new object();
        public readonly Dictionary<string, int> Graph = new Dictionary<string, int>(StringComparer.Ordinal) { ["S-ela"] = 1, ["S-ela-a"] = 1, ["S-ela-b"] = 1 };
        public int Writes; public string Refused;
        string[] native;
        public IEnumerable<string> MapIds => new[] { "map" };
        public IReadOnlyList<object> Find(string scene, string path) => scene == SceneName && path == Ela ? new[] { Controller } : new object[0];
        public string Refusal(object controller) => Refused;
        public Placement Placement(object controller) => new Placement(new[] { 10.0, 20, 30 }, new[] { 0.0, 0, 0, 1 });
        public bool Active(object controller) => true;
        public IReadOnlyList<string> ConstructionSegments(object target) => ReferenceEquals(target, Controller) ? Ids.ToArray() : null;
        public IReadOnlyList<string> NativeConstructionSegments(object target) => native ?? (native = Ids.ToArray());
        public int SegmentMatches(string id) => Graph.TryGetValue(id, out var count) ? count : 0;
        public void SetConstructionSegments(object target, IReadOnlyList<string> ids) { NativeConstructionSegments(target); Writes++; Ids = ids.ToArray(); }
    }
    const string Ela = "World/Large Scenery/Ela/Ela Bridge Scenery";
    static void ElaControllerHoldsOnlyItsSegments() {
        string Item(string id, string body) => "\"" + id + "\":{\"target\":{\"scene\":\"" + SceneName + "\",\"path\":\"" + Ela + "\",\"component\":\"" + ConstructionSceneryScene.ComponentType + "\",\"structure\":\"" + ConstructionSceneryScene.Structure + "\"}," + body + ",\"dependsOn\":[]}";
        var world = new Scenery(); var scene = new SceneCapabilities(() => world.MapIds, null); scene.Add(new ConstructionSceneryScene(world)); var engine = new PatchEngine(scene);
        var result = engine.Apply(Doc(Item("segments", "\"relationships\":{\"constructionSegments\":{\"segments\":[\"S-ela-a\",\"S-ela-b\"]}}"),
            Item("move", "\"placement\":{\"mode\":\"fixed\",\"position\":[0,0,0],\"rotation\":[0,0,0,1]}"), Item("off", "\"active\":false")));
        Assert(result.Outcomes["segments"].Applied && String.Join(",", world.Ids) == "S-ela-a,S-ela-b", "The Ela controller takes its construction segments: " + result.Outcomes["segments"].Error);
        Assert(!result.Outcomes["move"].Applied && result.Outcomes["move"].Failure == FailureKind.Refused && result.Outcomes["move"].Error == ConstructionSceneryScene.NoPlacement, "A placement of the controller is refused with the reason: " + result.Outcomes["move"].Error);
        Assert(!result.Outcomes["off"].Applied && result.Outcomes["off"].Failure == FailureKind.Refused && result.Outcomes["off"].Error == ConstructionSceneryScene.NoActivity, "An Enabled setting of the controller is refused with the reason: " + result.Outcomes["off"].Error);
        Assert(world.Writes == 1, "Only the segments were written");
        var detached = new Scenery(); var detachScene = new SceneCapabilities(() => detached.MapIds, null); detachScene.Add(new ConstructionSceneryScene(detached));
        var refused = new PatchEngine(detachScene).Apply(Doc(Item("detach", "\"relationships\":{\"constructionSegments\":{\"detach\":true}}"))).Outcomes["detach"];
        Assert(!refused.Applied && refused.Error == ConstructionSegmentRule.NoDetach && detached.Writes == 0, "The Ela controller refuses a detach before mutation: " + refused.Error);
        var unloaded = new Scenery { Refused = "The controller's scene isn't loaded." }; var unloadedScene = new SceneCapabilities(() => unloaded.MapIds, null); unloadedScene.Add(new ConstructionSceneryScene(unloaded));
        var preflight = new PatchEngine(unloadedScene).Apply(Doc(Item("segments", "\"relationships\":{\"constructionSegments\":{\"segments\":[\"S-ela-a\"]}}"))).Outcomes["segments"];
        Assert(!preflight.Applied && preflight.Failure == FailureKind.Refused && unloaded.Writes == 0 && String.Join(",", unloaded.Ids) == "S-ela", "A preflight refusal of the controller changes nothing: " + preflight.Error);
        // In scene patch editing, Remove modification returns the native segments.
        var edited = new Scenery(); var editedScene = new SceneCapabilities(() => edited.MapIds, null); editedScene.Add(new ConstructionSceneryScene(edited));
        var mapEditor = new NativeMapEditor(); var session = new PatchEditingSession(new PatchEngine(editedScene), mapEditor, new Store()); mapEditor.Session = session;
        var id = new TargetIdentity(SceneName, Ela, ConstructionSceneryScene.ComponentType, ConstructionSceneryScene.Structure);
        Assert(session.SetConstructionSegments(id, new[] { "S-ela-b" }, null).Applied && String.Join(",", edited.Ids) == "S-ela-b", "The Ela controller is repaired in scene patch editing");
        Assert(session.MinimumPatcherVersion == ConstructionSegmentRule.RequiredVersion, "The Ela controller's construction segments need patcher " + ConstructionSegmentRule.RequiredVersion + ": " + session.MinimumPatcherVersion);
        Assert(session.Remove(id).Applied && String.Join(",", edited.Ids) == "S-ela", "Remove modification returns the Ela controller's native segments: " + String.Join(",", edited.Ids));
    }

    // In scene patch editing: the repair after a native split, Undo and Redo with matching dirty state and Save
    // availability, a refusal that leaves file and dirty state alone, Remove modification back to the native set, Save in
    // canonical order at format version 1 with the patcher dependency, a stricter constraint kept, and a reload that
    // reproduces the repair.
    static void RepairInScenePatchEditing() {
        var f = Split(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        var all = new[] { PatchEditingSession.NativeData, PatchEditingSession.PatchData, PatchEditingSession.Metadata };
        void Persistence(bool dirty, string[] outstanding, string step) {
            var now = session.Outstanding(mapEditor.NativeDirty);
            Assert(mapEditor.IsDirty == dirty && now.SequenceEqual(outstanding), step + ": title indication and Save availability read " + mapEditor.IsDirty + " for outstanding " + String.Join(", ", now));
        }
        string Now() => String.Join(",", f.World.SegmentsOf[f.Bridge1].OrderBy(s => s, StringComparer.Ordinal));
        var refused = session.SetConstructionSegments(GroupId(Bryson), new[] { "S-bryson-a", "S-gone" }, null);
        Assert(!refused.Applied && refused.Failure == FailureKind.Refused && refused.Error.Contains("S-gone") && store.Patch == null && !mapEditor.IsDirty && f.World.SegmentWrites == 0,
            "A preflight refusal leaves the scene and the scene patch file unchanged: " + refused.Error);
        var detach = session.SetConstructionSegments(GroupId(Bryson), null, null, detach: true);
        Assert(!detach.Applied && detach.Error.Contains(ConstructionSegmentRule.NoDetach) && !mapEditor.IsDirty, "Explicit detachment is refused before mutation with the reason: " + detach.Error);
        var repair = session.SetConstructionSegments(GroupId(Bryson), new[] { "S-bryson-b", "S-bryson-a" }, null);
        Assert(repair.Applied && Now() == "S-bryson-a,S-bryson-b", "The repair to both halves applies: " + repair.Error + " " + Now());
        Assert(session.ConstructionSegmentsOf(GroupId(Bryson)).Display == "S-bryson-a, S-bryson-b" && session.HasConstructionSegments(GroupId(Bryson)), "The session reads the construction's segments and that this mod sets them");
        Assert(session.Changes(GroupId(Bryson)).Contains("Construction segments: from S-bryson to S-bryson-a, S-bryson-b"), "The Changes tab names the repair: " + String.Join(" | ", session.Changes(GroupId(Bryson))));
        Persistence(true, all, "After the repair");
        mapEditor.Undo(); Assert(Now() == "S-bryson", "Undo returns the segment set from before the edit: " + Now());
        Persistence(false, new string[0], "After Undo");
        mapEditor.Redo(); Assert(Now() == "S-bryson-a,S-bryson-b", "Redo reapplies the repair: " + Now());
        Persistence(true, all, "After Redo");
        var removed = session.Remove(GroupId(Bryson));
        Assert(removed.Applied && Now() == "S-bryson", "Remove modification returns the native segment set: " + removed.Error + " " + Now());
        Assert(!session.HasConstructionSegments(GroupId(Bryson)), "After the removal this mod sets no construction segments");
        mapEditor.Undo(); Assert(Now() == "S-bryson-a,S-bryson-b", "Undo of the removal reapplies the repair");
        // Moving the repaired bridge keeps the relationship and adds placement to the same modification.
        Assert(session.Edit(GroupId(Bryson), new Placement(new[] { 3500.0, 540, 4400 }, new[] { 0.0, 0, 0, 1 }), null).Applied, "A move of the repaired bridge applies");
        Assert(mapEditor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=" + ConstructionSegmentRule.RequiredVersion) && (int)JObject.Parse(store.Patch)["formatVersion"] == 1,
            "Save keeps format version 1 and raises the minimum patcher dependency: " + store.Info);
        var item = (JObject)JObject.Parse(store.Patch)["modifications"].First.First;
        Assert(item.Properties().Select(p => p.Name).SequenceEqual(new[] { "target", "relationships", "placement", "dependsOn" })
            && item["relationships"]["constructionSegments"]["segments"].Select(s => (string)s).SequenceEqual(new[] { "S-bryson-a", "S-bryson-b" }),
            "Save writes the relationship at its place in the canonical order, its segments in ordinal order: " + store.Patch);
        Persistence(false, new string[0], "After Save");
        var fresh = Split();
        var reload = fresh.Engine.Apply(store.Patch);
        Assert(reload.Outcomes.Values.Single().Applied && fresh.World.SegmentsOf[fresh.Bridge1].OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(new[] { "S-bryson-a", "S-bryson-b" }),
            "A reloaded world reproduces the repair: " + reload.Outcomes.Values.Single().Error);
        var strict = new Store { Info = "{\"identifier\":\"author/pack\",\"version\":\"1.0.0\",\"dependencies\":{\"required\":[\"crankyant/scene-patcher >=0.9.0\"]}}" };
        var other = Split(); var strictEditor = new NativeMapEditor(); var kept = new PatchEditingSession(other.Engine, strictEditor, strict); strictEditor.Session = kept;
        Assert(kept.SetConstructionSegments(GroupId(Bryson), new[] { "S-bryson-a" }, null).Applied && strictEditor.Save() == null && strict.Info.Contains(">=0.9.0") && !strict.Info.Contains(">=" + ConstructionSegmentRule.RequiredVersion),
            "A stricter existing constraint is kept: " + strict.Info);
        // Clearing this mod's value returns the native set, and setting the native set again stores nothing.
        Assert(kept.SetConstructionSegments(GroupId(Bryson), null, null).Applied && String.Join(",", other.World.SegmentsOf[other.Bridge1]) == "S-bryson", "Clearing the mod's value returns the native set");
        var same = kept.SetConstructionSegments(GroupId(Bryson), new[] { "S-bryson" }, null);
        Assert(same.Applied && (same.Unchanged || same.Reverted), "Setting the native set stores no modification");
    }

    // A missing or ambiguous segment, a detach, a duplicate id and the relationship on an object without a construction
    // controller are refused before any mutation, with the reason; no other segment is substituted, and the file's other
    // modifications still apply.
    static void RefusalsComeBeforeMutation() {
        void Refused(string relationship, string path, string expected, string what) {
            var f = Split();
            var result = f.Engine.Apply(Doc(Segments("edit", path, relationship), "\"other\":{" + Target(Almond) + ",\"active\":false,\"dependsOn\":[]}"));
            var outcome = result.Outcomes["edit"];
            Assert(!outcome.Applied && outcome.Failure == FailureKind.Refused && (outcome.Error ?? "").Contains(expected), what + " is refused before mutation with its reason: " + outcome.Failure + " " + outcome.Error);
            Assert(f.World.SegmentWrites == 0 && Ids(f.World.SegmentsOf[f.Bridge1]) == "S-bryson" && Ids(f.World.SegmentsOf[f.AlmondRoot]) == "S-almond", what + " leaves every construction's segments as they were");
            Assert(result.Outcomes["other"].Applied && !f.AlmondRoot.Active, what + " leaves the file's other modifications applied");
        }
        Refused("{\"segments\":[\"S-bryson-a\",\"S-bryson-gone\"]}", Bryson, "Construction segment S-bryson-gone is missing on the loaded track. No other segment is used.", "A missing segment");
        Refused("{\"segments\":[\"S-bryson-a\",\"S-twin\"]}", Bryson, "Construction segment S-twin matches more than one segment on the loaded track. No other segment is used.", "An ambiguous segment");
        Refused("{\"detach\":true}", Bryson, ConstructionSegmentRule.NoDetach, "A detach");
        Refused("{\"segments\":[\"S-bryson-a\",\"S-bryson-a\"]}", Bryson, "construction segment S-bryson-a is listed more than once", "A duplicate segment");
        Refused("{\"segments\":[]}", Bryson, "segments must list at least one track segment ID", "An empty set");
        Refused("{\"segments\":[\"S-bryson-a\"]}", "World/Large Scenery/Plate Girder Bridge/Plate Girder Bridge (1)", ConstructionSegmentRule.NoRelationship, "A bridge without a construction controller");
        // The native set itself is no reference: applied back, it stands also with a segment that no longer exists.
        var native = Split();
        var back = native.Engine.Apply(Doc(Segments("native", Bryson, "{\"segments\":[\"S-bryson\"]}")));
        Assert(back.Outcomes["native"].Applied && native.World.SegmentWrites == 0, "The native set is accepted as it stands: " + back.Outcomes["native"].Error);
    }

    // A failure after the segments were written restores the captured prior set; a restoration that cannot be verified
    // is reported with the remaining construction segments and blocks the retry until the world reloads.
    static void FailuresRestoreThePriorSet() {
        var f = Split(); f.World.ThrowAfterPlacement = true;
        var doc = Doc(Segments("repair", Bryson, "{\"segments\":[\"S-bryson-a\",\"S-bryson-b\"]}", ",\"placement\":{\"mode\":\"fixed\",\"position\":[3500,540,4400],\"rotation\":[0,0,0,1]}"));
        var failed = f.Engine.Apply(doc).Outcomes["repair"];
        Assert(!failed.Applied && failed.Failure == FailureKind.Restored && Ids(f.World.SegmentsOf[f.Bridge1]) == "S-bryson" && f.World.SegmentWrites == 2,
            "Failed-attempt restoration returns the captured prior segment set: " + failed.Failure + " " + failed.Error + " " + Ids(f.World.SegmentsOf[f.Bridge1]));
        var sticky = Split(); sticky.World.ThrowAfterPlacement = true; sticky.World.StickySegments = true;
        var left = sticky.Engine.Apply(doc).Outcomes["repair"];
        Assert(!left.Applied && left.Failure == FailureKind.IncompleteCleanup && left.Remaining.Any(line => line == "Construction segments: now S-bryson-a, S-bryson-b, before S-bryson"),
            "An unverified restoration names the remaining construction segments: " + left.Failure + " " + String.Join(" | ", left.Remaining));
        sticky.World.ThrowAfterPlacement = false;
        var blocked = sticky.Engine.Apply(doc).Outcomes["repair"];
        Assert(!blocked.Applied && blocked.Failure == FailureKind.AlreadyFailed && sticky.World.SegmentWrites == 2, "The retry is blocked within the load: " + blocked.Failure + " " + blocked.Error);
    }

    // The repair with a move of Bryson Bridge 1, whose move fails after the segments were written.
    static string RepairWithMove(string id = "repair") => Doc(Segments(id, Bryson, "{\"segments\":[\"S-bryson-a\",\"S-bryson-b\"]}", ",\"placement\":{\"mode\":\"fixed\",\"position\":[3500,540,4400],\"rotation\":[0,0,0,1]}"));

    // In scene patch editing, an incomplete cleanup of the construction segments blocks the next edit of the bridge until
    // Recheck verifies the bridge against the scene patch file; after that, the retry applies.
    static void IncompleteCleanupBlocksTheRetryUntilRecheck() {
        var f = Split(); var store = new Store(); var mapEditor = new NativeMapEditor();
        var session = new PatchEditingSession(f.Engine, mapEditor, store); mapEditor.Session = session;
        string Now() => String.Join(",", f.World.SegmentsOf[f.Bridge1].OrderBy(s => s, StringComparer.Ordinal));
        store.Patch = RepairWithMove(); session.CheckFileVersion();
        f.World.ThrowAfterPlacement = true; f.World.StickySegments = true;
        Assert(session.UseFileVersion().Applied, "Use file version runs");
        var outcome = session.LastVersionApplication?.Outcomes?["repair"];
        Assert(outcome != null && !outcome.Applied && outcome.Failure == FailureKind.IncompleteCleanup && outcome.Remaining.Contains("Construction segments: now S-bryson-a, S-bryson-b, before S-bryson"),
            "The incomplete cleanup is reported with the remaining construction segments: " + outcome?.Failure + " " + outcome?.Error + " | " + String.Join(" | ", outcome?.Remaining ?? new string[0]));
        var blocked = session.SetConstructionSegments(GroupId(Bryson), new[] { "S-bryson-a" }, null);
        Assert(!blocked.Applied && blocked.Failure == FailureKind.Blocked && Now() == "S-bryson-a,S-bryson-b", "The retry on the bridge is blocked until cleanup is verified: " + blocked.Failure + " " + blocked.Error + " now " + Now());
        // The controller accepts writes again; Recheck verifies the bridge against the scene patch file.
        f.World.StickySegments = false;
        var recheck = session.Recheck(GroupId(Bryson));
        Assert(recheck.Applied && Now() == "S-bryson-a,S-bryson-b" && f.At(f.Bridge1).Zip(new[] { 3500.0, 540, 4400 }, (a, b) => Math.Abs(a - b)).All(d => d <= 0.001),
            "Recheck verifies the bridge against the scene patch file: " + recheck.Error + " now " + Now());
        var retry = session.SetConstructionSegments(GroupId(Bryson), new[] { "S-bryson-a" }, null);
        Assert(retry.Applied && Now() == "S-bryson-a", "After Recheck the retry applies: " + retry.Failure + " " + retry.Error + " now " + Now());
    }

    // A world reload loads the bridge from the scene again: the next load attempt releases the bridge whose cleanup stayed
    // unverified, and the retry applies the repair afresh. A new session with a new engine applies it as well.
    static void AWorldReloadReleasesTheBlockedRetry() {
        var f = Split(); f.World.ThrowAfterPlacement = true; f.World.StickySegments = true;
        Assert(f.Engine.Apply(RepairWithMove(), "pack").Outcomes["repair"].Failure == FailureKind.IncompleteCleanup, "The attempt leaves an incomplete cleanup");
        f.Engine.Reenter();
        var held = f.Engine.Apply(RepairWithMove(), "pack").Outcomes["repair"];
        Assert(!held.Applied && held.Failure == FailureKind.Blocked, "Over the retained world the retry stays blocked: " + held.Failure + " " + held.Error);
        // The reload: the game destroys the bridge and loads it from the scene again, with its native segment.
        var reloaded = Split();
        foreach (var path in new[] { Bryson, Bryson + "/Abutments", Bryson + "/Steel", Bryson + "/Steel/Plate Girder E (4)" }) {
            if (reloaded.World.Objects.ContainsKey(path)) f.World.Objects[path] = reloaded.World.Objects[path];
            if (reloaded.World.PartsAt.ContainsKey(path)) f.World.PartsAt[path] = reloaded.World.PartsAt[path];
        }
        f.World.SegmentsOf[reloaded.Bridge1] = new[] { "S-bryson" }; f.World.SetActive(reloaded.Bridge1, true);
        f.World.StickySegments = false; f.World.SegmentWrites = 0;
        var release = f.Engine.Reenter();
        Assert(release.Released > 0 && release.Failures.Count == 0, "The next load attempt releases the reloaded bridge: " + String.Join(" | ", release.Failures));
        var retry = f.Engine.Apply(RepairWithMove(), "pack").Outcomes["repair"];
        Assert(retry.Applied && Ids(f.World.SegmentsOf[reloaded.Bridge1].OrderBy(s => s, StringComparer.Ordinal)) == "S-bryson-a,S-bryson-b",
            "After the reload the retry applies the repair: " + retry.Failure + " " + retry.Error + " " + Ids(f.World.SegmentsOf[reloaded.Bridge1]));
        // A new session over the same scene patch file starts with a new engine.
        var first = Split(); first.World.ThrowAfterPlacement = true; first.World.StickySegments = true;
        Assert(first.Engine.Apply(RepairWithMove(), "pack").Outcomes["repair"].Failure == FailureKind.IncompleteCleanup, "The first session leaves an incomplete cleanup");
        var next = Split();
        var applied = next.Engine.Apply(RepairWithMove(), "pack").Outcomes["repair"];
        Assert(applied.Applied && Ids(next.World.SegmentsOf[next.Bridge1].OrderBy(s => s, StringComparer.Ordinal)) == "S-bryson-a,S-bryson-b", "A new session applies the repair: " + applied.Error);
    }

    // An omitted relationship keeps the native segments; a reference replaces the set with exactly the selected segments,
    // in any order, and repeated application writes nothing more and duplicates nothing.
    static void ReferenceReplacesTheSet() {
        var f = Split();
        var moved = f.Engine.Apply(Doc("\"move\":{" + Target(Bryson) + ",\"placement\":{\"mode\":\"fixed\",\"position\":[3500,540,4400],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}"));
        Assert(moved.Outcomes["move"].Applied && Ids(f.World.SegmentsOf[f.Bridge1]) == "S-bryson" && f.World.SegmentWrites == 0, "With the setting omitted the construction keeps its native segments: " + moved.Outcomes["move"].Error + " " + Ids(f.World.SegmentsOf[f.Bridge1]));
        var repair = Doc(Segments("repair", Bryson, "{\"segments\":[\"S-bryson-b\",\"S-bryson-a\"]}"));
        var applied = f.Engine.Apply(repair);
        Assert(applied.Outcomes["repair"].Applied, "The repair applies: " + applied.Outcomes["repair"].Error);
        Assert(f.World.SegmentsOf[f.Bridge1].OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(new[] { "S-bryson-a", "S-bryson-b" }) && f.World.SegmentWrites == 1,
            "The reference replaces the set with exactly the selected segments: " + Ids(f.World.SegmentsOf[f.Bridge1]));
        Assert(Ids(f.World.SegmentsOf[f.AlmondRoot]) == "S-almond", "Another construction keeps its segments");
        f.Engine.Reenter();
        var again = f.Engine.Apply(repair);
        Assert(again.Outcomes["repair"].Applied && f.World.SegmentsOf[f.Bridge1].Length == 2 && f.World.SegmentWrites == 1, "Repeated application accumulates no duplicate segments and writes nothing: " + Ids(f.World.SegmentsOf[f.Bridge1]) + " writes " + f.World.SegmentWrites);
        Assert(f.Scene.Read(f.Scene.Resolve(GroupId(Bryson)).Single()).Segments.Ids.Count == 2, "The group reads its construction segments");
    }

    // The Connections tab's construction segment row: the segments field, its buttons and their reasons, and the texts
    // of the Ela controller's Supported edits, Placement and Enabled.
    static void ConnectionsTabTexts() {
        // While the Connections tab is open the world marks each current binding green and offers none of them as a
        // candidate: a marker's segment, a span's two segments and each construction segment, of a group root or part
        // (inventory line "Construction segment=") and of the Ela controller ("Construction scenery segment=").
        var bound = CommandBand.BoundSegments(new[] { "Marker segment=S1; end=a; distance=2", "Lower segment=S2", "Upper segment=S3; end=b",
            CommandBand.SegmentBinding("S4", true), CommandBand.SegmentBinding("S5", false), "Construction scenery segment=S6", "Storage piles=0", "Construction target World/Steel" });
        Assert(bound.SequenceEqual(new[] { "S1", "S2", "S3", "S4", "S5", "S6" }), "Every current binding is a bound segment, construction segments included: " + String.Join(",", bound));
        // Segment on a construction's Connections tab offers no current construction segment as a candidate.
        SelectionCandidate Track(string id) => new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackSegment, id), id);
        var tracks = new[] { Track("S4"), Track("S5"), Track("S7") };
        var offered = SelectionOffers.For(tracks, false, new[] { SelectionIdentity.Graph(SelectionKind.TrackSegment, "S4"), SelectionIdentity.Graph(SelectionKind.TrackSegment, "S5") });
        Assert(offered.Select(c => c.Identity.Identifier).SequenceEqual(new[] { "S7" }), "The current construction segments are no candidates: " + String.Join(",", offered.Select(c => c.Identity.Identifier)));
        Assert(CommandBand.SegmentIds(" S-a, S-b  S-c,,S-a ").SequenceEqual(new[] { "S-a", "S-b", "S-c", "S-a" }), "The field reads ids separated by commas or spaces: " + String.Join("|", CommandBand.SegmentIds(" S-a, S-b  S-c,,S-a ")));
        Assert(CommandBand.SegmentText(new[] { "S-b", "S-a" }) == "S-a, S-b", "The field shows a set in ordinal order: " + CommandBand.SegmentText(new[] { "S-b", "S-a" }));
        Assert(CommandBand.AddSegment("S-a", "S-b") == "S-a, S-b" && CommandBand.AddSegment("S-a, S-b", "S-a") == "S-a, S-b" && CommandBand.AddSegment("", "S-a") == "S-a", "Picked adds the confirmed segment once");
        // A segment that no longer exists in the track graph is what the modder replaces: Picked drops it from the field and
        // keeps the existing segments and the picked one.
        var lines = new[] { CommandBand.SegmentBinding("cjwp8g", false), CommandBand.SegmentBinding("S-a", true) };
        Assert(CommandBand.MissingSegments(lines).SequenceEqual(new[] { "cjwp8g" }), "The binding lines name the segment that no longer exists: " + String.Join(",", CommandBand.MissingSegments(lines)));
        var picked = CommandBand.AddSegment("cjwp8g, S-a", "Sg6u0gppt", CommandBand.MissingSegments(lines));
        Assert(picked == "S-a, Sg6u0gppt" && CommandBand.AddSegment(picked, "Sg6u04j9v", CommandBand.MissingSegments(lines)) == "S-a, Sg6u0gppt, Sg6u04j9v",
            "Picked replaces a segment that no longer exists and keeps the others: " + picked);
        Assert(CommandBand.AddSegment("S-a, typed", "S-b", CommandBand.MissingSegments(lines)) == "S-a, typed, S-b", "An ID the observed bindings do not name as missing stays");
        Assert(CommandBand.SegmentsReason(null, "", new[] { "S-a" }) == "Enter at least one segment ID.", "An empty field cannot be set");
        Assert(CommandBand.SegmentsReason(null, "S-b, S-a", new[] { "S-a", "S-b" }) == "The field already lists the construction's current segments.", "The current set cannot be set again, in any order");
        Assert(CommandBand.SegmentsReason(null, "S-a, S-c", new[] { "S-a", "S-b" }) == null && CommandBand.SegmentsReason("Inspect an object first.", "S-c", new[] { "S-a" }) == "Inspect an object first.", "A different set can be set; the general reason comes first");
        Assert(CommandBand.SegmentDetachReason == "The construction needs track segments. Select the segments of the bridge's track instead.", "Detach states why it is unavailable: " + CommandBand.SegmentDetachReason);
        Assert(CommandBand.ClearSegmentsReason(false) == "This mod sets no construction segments for this object, so there is nothing to clear." && CommandBand.ClearSegmentsReason(true) == null, "Clear segments needs this mod's value");
        Assert(CommandBand.ActionRows.Any(row => row.Length == 4 && row[0][0] == "Set" && row[1][0] == "Detach" && row[2][0] == CommandBand.ClearSegments && row[3][0] == "Picked") && CommandBand.LabelWidths.ContainsKey(CommandBand.ClearSegments),
            "The construction segment row is one of the panel's action rows with a known label width");
        // The Ela controller is an editable object (its construction segments), so the edit block does not fall back to
        // the generic reason: placement and activity then give the controller's own reasons.
        Assert(CommandBand.EditableStructure(ConstructionSceneryScene.Structure), "The Ela controller is editable");
        foreach (var structure in new[] { IndependentBoards.Structure, BridgeScene.Structure, TrackMarkerScene.Structure, MilepostScene.Structure, CrossingCoverageScene.Structure, CrossingPadScene.Structure,
            BakedSceneryGroups.Structure, BakedSceneryParts.Structure, StationMapIconRules.Structure, SpawnPoints.Structure, ExistingEquipmentScene.MastStructure, ExistingEquipmentScene.BellStructure })
            Assert(CommandBand.EditableStructure(structure), "Still editable: " + structure);
        Assert(!CommandBand.EditableStructure(null) && !CommandBand.EditableStructure(StarterPlacementScene.Structure), "No structure, or a starter placement marker with its own workflow, is not editable here");
        Assert(CommandBand.PlacementBlock(null, null, CommandBand.ControllerNoPlacement, null) == CommandBand.ControllerNoPlacement
            && CommandBand.ModeBlock(CommandBand.NumericMode, false, true, CommandBand.ConstructionSceneryOperations, controller: true) == null,
            "Numeric stays open for the Ela controller and its fields give the placement reason");
        var ela = new TargetIdentity(SceneName, Ela, ConstructionSceneryScene.ComponentType, ConstructionSceneryScene.Structure);
        Assert(GuidedTaskState.TargetBlock(GuidedTaskKind.Placement, ela) == CommandBand.ControllerNoPlacement && GuidedTaskState.TargetBlock(GuidedTaskKind.Snap, ela) == CommandBand.ControllerNoPlacement
            && GuidedTaskState.TargetBlock(GuidedTaskKind.Activity, ela) == CommandBand.ControllerNoActivity, "The guided tasks of the Ela controller give its placement and activity reasons");
        // The other guided tasks do not fit the controller either: each stays in the list, inactive with the controller's reason.
        foreach (var task in new[] { GuidedTaskKind.Binding, GuidedTaskKind.DetachBinding, GuidedTaskKind.StarterPlacement, GuidedTaskKind.Wreck, GuidedTaskKind.MarkerId }) {
            var reason = GuidedTaskState.TargetBlock(task, ela);
            Assert(reason != null && reason.StartsWith("The construction controller ", StringComparison.Ordinal) && reason.EndsWith(".", StringComparison.Ordinal)
                && CommandBand.Tooltip("Start this task for the inspected object.", reason).Contains("Unavailable: " + reason), "The guided task " + task + " of the Ela controller is unavailable with its reason: " + reason);
        }
        Assert(GuidedTaskState.TargetBlock(GuidedTaskKind.Binding, ela).Contains("Connections tab"), "The binding tasks point to where the controller's segments are set");
        // The controller is bound to track by its construction segments, which the Connections tab shows: the reason
        // says it holds no marker's track location, not that it has no track binding at all.
        Assert(GuidedTaskState.TargetBlock(GuidedTaskKind.Binding, ela) == "The construction controller uses construction segments rather than a marker's track location. Set its construction segments on the Connections tab."
            && GuidedTaskState.TargetBlock(GuidedTaskKind.DetachBinding, ela) == GuidedTaskState.TargetBlock(GuidedTaskKind.Binding, ela),
            "The binding tasks do not deny the construction segments: " + GuidedTaskState.TargetBlock(GuidedTaskKind.Binding, ela));
        Assert(GuidedTaskState.Tasks.Count == 8, "The task list keeps every task visible");
        Assert(GuidedTaskState.TargetBlock(GuidedTaskKind.Placement, GroupId(Bryson)) == null, "A rail bridge group keeps its guided move");
        Assert(CommandBand.EditReason(CommandBand.ConstructionSceneryOperations) == "This object cannot be edited here.", "The generic reason is what the Ela controller showed while it counted as not editable");
        Assert(CommandBand.ConstructionSceneryOperations == "Set this controller's construction segments. Its position has no effect in the game. It stays enabled because it controls which parts of the bridge the game shows.",
            "The Ela controller's Supported edits: " + CommandBand.ConstructionSceneryOperations);
        Assert(CommandBand.InspectionOnly(CommandBand.ConstructionSceneryFamily, false) == null, "No bridge construction controller is inspection only");
        for (var mode = 0; mode < 4; mode++)
            Assert(CommandBand.ModeBlock(mode, false, true, null, controller: true) == (mode == CommandBand.NumericMode ? null : CommandBand.ControllerNoPlacement), "Every placement mode but Numeric is unavailable for the controller, with the reason: mode " + mode);
        Assert(CommandBand.ControllerNoPlacement == ConstructionSceneryScene.NoPlacement && CommandBand.ControllerNoActivity == ConstructionSceneryScene.NoActivity, "Placement and Enabled give the capability's reasons");
    }
}
