// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RailroaderScenePatcher.Core;

// One scene object holding a Generic track marker. Its serialized location is the binding; the graph holds its registration.
class FakeMarker {
    public string Id;
    public string Segment, End = "a"; public double Distance;
    public double[] Position = { 20, 1, 2 }, Rotation = { 0, 0, 0, 1 };
    public bool Active = true;
    public string Pattern;
    // A consumer cache derived from the bound segment, such as a map label's cached track group.
    public string CachedGroup;
    // A TrackRelativePosition whose target is this object and whose activity follows the bound track group: the group it
    // observes, whether it observes, its handlers on the graph's group event and the explicit-disable hold in place of
    // the native activation callback.
    public bool Follows, Observing, Held;
    public string ObservedGroup;
    public int Subscriptions;
    // Every activation of the object, so a transient activation cannot hide behind a final inactive snapshot.
    public int Activations;
}
// A loaded component scanned for direct references: its fields, each relevant when it can hold a marker.
class FakeOwner {
    public string Name; public FakeMarker Colocated;
    public List<ReferenceField> Fields = new List<ReferenceField>();
}
// A list whose enumeration fails, as a native list can during a scan.
class BrokenList : System.Collections.ArrayList {
    public override System.Collections.IEnumerator GetEnumerator() => throw new InvalidOperationException("list enumeration injected");
    public override int Count => throw new InvalidOperationException("list enumeration injected");
}
// A list that implements only the generic IList<T>, as a field declared IList<T> can hold.
class GenericOnlyList<T> : IList<T> {
    readonly List<T> items = new List<T>();
    // Makes enumeration fail, as a native list can during a scan.
    public bool FailEnumeration;
    public T this[int index] { get => items[index]; set => items[index] = value; }
    public int Count => items.Count;
    public bool IsReadOnly => false;
    public void Add(T item) => items.Add(item);
    public void Clear() => items.Clear();
    public bool Contains(T item) => items.Contains(item);
    public void CopyTo(T[] array, int index) => items.CopyTo(array, index);
    public IEnumerator<T> GetEnumerator() => FailEnumeration ? throw new InvalidOperationException("generic list enumeration injected") : items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(T item) => items.IndexOf(item);
    public void Insert(int index, T item) => items.Insert(index, item);
    public bool Remove(T item) => items.Remove(item);
    public void RemoveAt(int index) => items.RemoveAt(index);
}
// Inspection of the marker objects with copied values, as the native inventory observes them.
class FakeMarkerInspection : IInspectionScene {
    readonly FakeMarkerWorld world;
    public FakeMarkerInspection(FakeMarkerWorld world) { this.world = world; }
    public IEnumerable<string> MapIds => world.MapIds;
    public IEnumerable<SceneObservation> Discover() => world.Objects.SelectMany(o => o.Value.Select(target => {
        var m = (FakeMarker)target;
        return new SceneObservation(m, new TargetIdentity("BushnellWhittier", o.Key, "Track.TrackMarker", TrackMarkerScene.Structure), "Track marker", m.Active,
            world.Position(m), world.Rotation(m), new[] { "Marker segment=" + m.Segment + "; end=" + m.End + "; distance=" + m.Distance }, Array.Empty<string>(), Array.Empty<string>(), "binding");
    }));
}
// Snap targets with copied geometry, as the native spatial selection offers them.
class FakeSnapTracks : ISnapScene {
    public string[] Maps = { "map" };
    public IEnumerable<string> MapIds => Maps;
    public List<SnapGeometry> Items = new List<SnapGeometry>();
    public IEnumerable<SnapGeometry> DiscoverSnapTargets(SelectionKind kind) => Items.Where(g => g.Candidate.Identity.Kind == kind);
    public IEnumerable<SelectionCandidate> Discover(SelectionKind kind) => DiscoverSnapTargets(kind).Select(g => g.Candidate);
}
// Controlled track graph: segments by ID and named-location buckets by segment ID, as the native graph keeps them.
// Activity follows the native marker lifecycle: enabling registers a valid location, disabling removes every entry.
class FakeMarkerWorld : ITrackMarkerWorld {
    public string[] Maps = { "map" };
    public IEnumerable<string> MapIds => Maps;
    public Dictionary<string, List<object>> Objects = new Dictionary<string, List<object>>();
    public Dictionary<string, List<double>> Segments = new Dictionary<string, List<double>> {
        ["Sjdl"] = new List<double> { 80 }, ["S9sc"] = new List<double> { 300 }, ["Nnew"] = new List<double> { 120 }
    };
    public readonly Dictionary<string, Dictionary<string, (string end, double distance, object owner)>> Buckets = new Dictionary<string, Dictionary<string, (string, double, object)>>();
    public double Origin = 1000;
    public int Mutations;
    // Failure injections.
    public bool ThrowAfterLocation, SkipRegister, StickyRegistration, FailRestoreLocation, SkipDependentRefresh;
    public Dictionary<string, string> Groups = new Dictionary<string, string> { ["Sjdl"] = "alarka", ["S9sc"] = "ela", ["Nnew"] = "extension" };
    string GroupOf(FakeMarker m) => m.Segment != null && Groups.TryGetValue(m.Segment, out var g) ? g : null;
    public void RefreshDependents(object target) { Mutations++; if (SkipDependentRefresh) { SkipDependentRefresh = false; return; } var m = (FakeMarker)target; m.CachedGroup = GroupOf(m); }
    public string DependentProblem(object target) { var m = (FakeMarker)target; return m.CachedGroup == GroupOf(m) ? null : "the map label caches track group " + m.CachedGroup; }
    public IReadOnlyList<object> Resolve(TargetIdentity identity) {
        if (identity.Component != "Track.TrackMarker" || (identity.Structure != null && identity.Structure != TrackMarkerScene.Structure)) return Array.Empty<object>();
        return Objects.TryGetValue(identity.Path, out var found) ? found : new List<object>();
    }
    public List<FakeOwner> Owners = new List<FakeOwner>();
    // The marker's own relative position holds it; the adapter allows only that holder.
    public void Relative(FakeMarker m) {
        Owners.Add(new FakeOwner { Name = "TrackRelativePosition on " + m.Id, Colocated = m, Fields = {
            new ReferenceField("_marker", true, () => m),
            new ReferenceField("targetAngle", false, () => throw new InvalidOperationException("irrelevant value field injected")) } });
    }
    public string PatternProblem(object target) =>
        ((FakeMarker)target).Pattern ?? ReferenceScan.Of(Owners, o => ((FakeOwner)o).Fields, i => i is FakeMarker, o => ((FakeOwner)o).Name)
            .Refusal(target, (o, field) => ((FakeOwner)o).Colocated == target && field == "_marker", o => ((FakeOwner)o).Name);
    public TrackLocation Location(object target) {
        var m = (FakeMarker)target;
        return String.IsNullOrEmpty(m.Segment) ? null : new TrackLocation(m.Segment, m.End, m.Distance);
    }
    public IReadOnlyList<double> SegmentLengths(string segment) => Segments.TryGetValue(segment, out var lengths) ? lengths : new List<double>();
    public IReadOnlyList<MarkerRegistration> Registrations(object target) {
        var m = (FakeMarker)target;
        return Buckets.Where(b => b.Value.ContainsKey(m.Id)).Select(b => new MarkerRegistration(b.Key, new TrackLocation(b.Key, b.Value[m.Id].end, b.Value[m.Id].distance), ReferenceEquals(b.Value[m.Id].owner, m))).ToList();
    }
    public bool Registers(object target) => ((FakeMarker)target).Active;
    public void SetLocation(object target, TrackLocation location) {
        Mutations++;
        var m = (FakeMarker)target;
        if (FailRestoreLocation && location != null && location.Segment == "Sjdl") throw new Exception("injected restore failure");
        m.Segment = location?.Segment; m.End = location?.End ?? "a"; m.Distance = location?.Distance ?? 0;
        if (ThrowAfterLocation) { ThrowAfterLocation = false; throw new Exception("injected after location"); }
    }
    public void Unregister(object target) { Mutations++; if (StickyRegistration) return; foreach (var b in Buckets.Values) b.Remove(((FakeMarker)target).Id); }
    public void Register(object target) {
        Mutations++;
        if (SkipRegister) { SkipRegister = false; return; }
        var m = (FakeMarker)target;
        if (!Valid(m)) throw new ArgumentException("Can't add named location " + m.Id + " with invalid location");
        Add(m);
    }
    bool Valid(FakeMarker m) => !String.IsNullOrEmpty(m.Segment) && Segments.TryGetValue(m.Segment, out var l) && l.Count == 1 && m.Distance >= 0 && m.Distance <= l[0];
    void Add(FakeMarker m) {
        if (!Buckets.TryGetValue(m.Segment, out var bucket)) Buckets.Add(m.Segment, bucket = new Dictionary<string, (string, double, object)>());
        bucket[m.Id] = (m.End, m.Distance, m);
    }
    // The native OnEnable registration of a marker present at load.
    public void Load(FakeMarker m) { if (m.Active && Valid(m)) Add(m); m.CachedGroup = GroupOf(m); }
    public double[] Position(object target) { var m = (FakeMarker)target; return new[] { m.Position[0] + Origin, m.Position[1], m.Position[2] }; }
    public double[] Rotation(object target) => (double[])((FakeMarker)target).Rotation.Clone();
    public bool Active(object target) => ((FakeMarker)target).Active;
    public void SetPose(object target, double[] position, double[] rotation) {
        Mutations++; var m = (FakeMarker)target;
        m.Position = new[] { position[0] - Origin, position[1], position[2] }; m.Rotation = (double[])rotation.Clone();
    }
    public void SetActive(object target, bool active) { Mutations++; Activate((FakeMarker)target, active); }
    void Activate(FakeMarker m, bool active) {
        if (m.Active == active) return;
        m.Active = active;
        if (active) m.Activations++;
        if (!active) { foreach (var b in Buckets.Values) b.Remove(m.Id); } else if (Valid(m)) Add(m);
    }
    // The graph's enabled track groups and its group event, as Graph.SetGroupEnabled raises it.
    public HashSet<string> EnabledGroups = new HashSet<string> { "alarka", "ela", "extension" };
    public bool SkipObserve;
    // Fails the hold step that installs (true) or releases (false) the hold.
    public bool? FailHold;
    public void SetGroupEnabled(string group, bool enabled) {
        if (enabled) EnabledGroups.Add(group); else EnabledGroups.Remove(group);
        foreach (var m in Objects.Values.SelectMany(o => o).Cast<FakeMarker>().ToList())
            if (m.Follows && m.Subscriptions > 0 && m.ObservedGroup == group) SetTargetActive(m, EnabledGroups.Contains(group));
    }
    // The native activation step: the hold, when installed, replaces it and keeps the object inactive.
    void SetTargetActive(FakeMarker m, bool active) { if (m.Held) Activate(m, false); else Activate(m, active); }
    public GroupActivity GroupActivity(object target) {
        var m = (FakeMarker)target;
        if (!m.Follows) return null;
        var group = GroupOf(m);
        return new GroupActivity(m.Held, m.Observing, m.ObservedGroup, m.Subscriptions, Valid(m), group, group == null || EnabledGroups.Contains(group));
    }
    // Native UnsubscribeFromGroupEvents followed by ObserveTrackGroup.
    public void ObserveGroup(object target) {
        Mutations++; var m = (FakeMarker)target;
        m.Observing = false; m.Subscriptions = 0;
        if (SkipObserve) { SkipObserve = false; return; }
        if (!Valid(m)) return;
        var group = GroupOf(m);
        m.ObservedGroup = group; m.Observing = true;
        if (group != null) m.Subscriptions = 1;
        SetTargetActive(m, group == null || EnabledGroups.Contains(group));
    }
    public void HoldDisabled(object target, bool hold) {
        Mutations++;
        if (FailHold == hold) throw new Exception("hold injected");
        ((FakeMarker)target).Held = hold;
    }
    // A group follower present at load, as its Awake observes the group.
    public void LoadFollower(FakeMarker m) { m.Follows = true; ObserveGroup(m); Load(m); Mutations = 0; m.Activations = 0; }
}
static class BindingTests {
    const string SignPath = "World/Large Scenery/Ela/Signs/Station Sign (1)", OtherPath = "World/Large Scenery/Alarka Signs/Yard Limit";
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Target(string path = SignPath) => "\"target\":{\"scene\":\"BushnellWhittier\",\"path\":\"" + path + "\",\"component\":\"Track.TrackMarker\",\"structure\":\"track-marker-v1\"}";
    static string Binding(string id, string marker, string path = SignPath, string deps = "[]", string extra = "") =>
        "{\"id\":\"" + id + "\"," + Target(path) + ",\"relationships\":{\"trackMarker\":" + marker + "}" + extra + ",\"dependsOn\":" + deps + "}";
    static string At(string segment, string end, double distance) => "{\"segment\":\"" + segment + "\",\"end\":\"" + end + "\",\"distance\":" + distance.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
    const string Detach = "{\"detach\":true}";
    static string Doc(string items, string map = "map") => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"" + map + "\"},\"modifications\":[" + items + "]}";
    static (FakeMarkerWorld world, FakeMarker sign, FakeMarker other, TrackMarkerScene scene) Setup() {
        var world = new FakeMarkerWorld();
        var sign = new FakeMarker { Id = "Mi4f", Segment = "S9sc", End = "a", Distance = 225.6681 };
        var other = new FakeMarker { Id = "Mkwd", Segment = "Sjdl", End = "b", Distance = 38.6, Position = new double[] { 70, 3, 4 } };
        world.Objects[SignPath] = new List<object> { sign }; world.Objects[OtherPath] = new List<object> { other };
        world.Load(sign); world.Load(other); world.Relative(sign); world.Relative(other);
        return (world, sign, other, new TrackMarkerScene(world, Capability.Unchecked));
    }
    // The single graph entry for the marker, or a description of why there is not exactly one owned entry.
    static string Entry(FakeMarkerWorld world, FakeMarker marker) {
        var entries = world.Registrations(marker);
        if (entries.Count != 1) return entries.Count + " entries";
        var e = entries[0];
        return e.Bucket + "|" + e.Location.End + "|" + e.Location.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture) + (e.HoldsMarker ? "" : "|foreign");
    }
    public static void Run() {
        SetBindsLocationAndRegistration();
        DependentConsumersFollowTheBinding();
        OmissionLeavesRelationshipUntouched();
        DetachClearsLocationAndRegistration();
        ReplacementAndRepetitionKeepOneRegistration();
        UnresolvedTargetsChangeNothing();
        UnsupportedRelationshipsAreRefusedAndIsolated();
        DependentsFollowRelationshipOutcome();
        FailureAfterMutationRestoresRelationship();
        UnverifiedCleanupBlocksRetry();
        IncompleteReferenceScanRefusesBeforeMutation();
        SharedFieldPassReadsEachFieldOnce();
        ReferenceWalkPausesBetweenOwners();
        SetupPlacementsAreIndexedOncePerDiscovery();
        EditorSetReplaceDetachUndoRedo();
        EditorRemovalRecomputesUnderlyingRelationship();
        EditorRefusalAndFailureRestore();
        SaveAndReloadReproduceBindingOnce();
        GizmoGestureKeepsTheBinding();
        PhysicalSnapKeepsTheBinding();
        Console.WriteLine("PASS: track-marker relationship set, replacement, detachment, omission, refusal, restoration and authoring");
    }
    static void SetBindsLocationAndRegistration() {
        var (world, sign, other, scene) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Binding("rebind-station", At("Nnew", "b", 42.5))));
        Assert(result.Error == null && result.Outcomes["rebind-station"].Applied, "A marker binding applies: " + result.Error + result.Outcomes["rebind-station"].Error);
        Assert(sign.Segment == "Nnew" && sign.End == "b" && sign.Distance == 42.5, "The serialized marker location takes the authored reference");
        Assert(Entry(world, sign) == "Nnew|b|42.5", "The graph holds one registration of this marker at the new location: " + Entry(world, sign));
        Assert(!world.Buckets["S9sc"].ContainsKey("Mi4f"), "The old segment bucket no longer holds the marker");
        Assert(sign.Position.SequenceEqual(new double[] { 20, 1, 2 }) && sign.Rotation.SequenceEqual(new double[] { 0, 0, 0, 1 }), "A binding edit does not move the object");
        Assert(Entry(world, other) == "Sjdl|b|38.6" && other.Segment == "Sjdl", "An unrelated marker keeps its relationship and registration");
    }
    static void DependentConsumersFollowTheBinding() {
        var (world, sign, other, scene) = Setup();
        var engine = new PatchEngine(scene);
        Assert(engine.Apply(Doc(Binding("rebind", At("Nnew", "a", 4)))).Outcomes["rebind"].Applied && sign.CachedGroup == "extension", "A dependent consumer cache follows the new segment");
        Assert(new PatchEngine(scene).Apply(Doc(Binding("rebind", Detach))).Outcomes["rebind"].Applied && sign.CachedGroup == null, "Detach refreshes the dependent consumer cache");
        Assert(other.CachedGroup == "alarka", "The unrelated marker's consumer cache is untouched");
        (world, sign, other, scene) = Setup();
        world.SkipDependentRefresh = true;
        var outcome = new PatchEngine(scene).Apply(Doc(Binding("rebind", At("Nnew", "a", 4)))).Outcomes["rebind"];
        Assert(!outcome.Applied && outcome.Error.StartsWith("Mutation failed and was restored", StringComparison.Ordinal) && outcome.Error.Contains("track group"), "A stale dependent cache fails the binding: " + outcome.Error);
        Assert(sign.Segment == "S9sc" && sign.CachedGroup == "ela" && Entry(world, sign) == "S9sc|a|225.6681", "Restoration returns reference, registration and dependent cache");
    }
    static string Placed(string id, string extra = "") =>
        "{\"id\":\"" + id + "\"," + Target() + ",\"placement\":{\"mode\":\"fixed\",\"position\":[1025,1,2],\"rotation\":[0,1,0,0]}" + extra + ",\"dependsOn\":[]}";
    static void OmissionLeavesRelationshipUntouched() {
        var (world, sign, other, scene) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Placed("move-station")));
        Assert(result.Outcomes["move-station"].Applied && sign.Position[0] == 25 && sign.Rotation[1] == 1, "Placement of a marker object applies through the marker capability: " + result.Outcomes["move-station"].Error);
        Assert(sign.Segment == "S9sc" && sign.Distance == 225.6681 && Entry(world, sign) == "S9sc|a|225.6681", "An omitted relationship keeps the marker's location and registration");
        // A placement and a relationship in one modification are applied independently.
        (world, sign, other, scene) = Setup();
        result = new PatchEngine(scene).Apply(Doc(Binding("both", At("Nnew", "a", 10), extra: ",\"placement\":{\"mode\":\"fixed\",\"position\":[1030,1,2],\"rotation\":[0,0,0,1]}")));
        Assert(result.Outcomes["both"].Applied && sign.Position[0] == 30 && Entry(world, sign) == "Nnew|a|10", "Placement and binding each take their own authored value");
    }
    static void DetachClearsLocationAndRegistration() {
        var (world, sign, other, scene) = Setup();
        var engine = new PatchEngine(scene);
        var result = engine.Apply(Doc(Binding("detach-station", Detach)));
        Assert(result.Outcomes["detach-station"].Applied, "An explicit detach applies: " + result.Outcomes["detach-station"].Error);
        Assert(sign.Segment == null && world.Registrations(sign).Count == 0, "Detach clears the serialized location and removes every graph registration of the marker");
        Assert(sign.Position[0] == 20 && sign.Active, "Detach neither moves nor disables the object");
        Assert(Entry(world, other) == "Sjdl|b|38.6", "Detach leaves the unrelated marker registered");
        Assert(engine.Apply(Doc(Binding("detach-station", Detach))).Outcomes["detach-station"].Applied && world.Registrations(sign).Count == 0, "Repeated detach converges");
        foreach (var invalid in new[] { "{\"detach\":false}", "{\"detach\":true,\"segment\":\"Nnew\",\"end\":\"a\",\"distance\":1}", "null", "{}", "{\"detach\":\"yes\"}" }) {
            (world, sign, other, scene) = Setup();
            var refused = new PatchEngine(scene).Apply(Doc(Binding("bad", invalid)));
            Assert(!refused.Outcomes["bad"].Applied && world.Mutations == 0 && Entry(world, sign) == "S9sc|a|225.6681", "A bare null, empty, false or mixed relationship is not a detach: " + invalid);
        }
    }
    static void ReplacementAndRepetitionKeepOneRegistration() {
        var (world, sign, other, scene) = Setup();
        var engine = new PatchEngine(scene);
        engine.Apply(Doc(Binding("rebind", At("Nnew", "a", 5))));
        var second = engine.Apply(Doc(Binding("rebind", At("Nnew", "a", 5))));
        Assert(second.Outcomes["rebind"].Applied && Entry(world, sign) == "Nnew|a|5", "Repeated application keeps exactly one registration");
        var replaced = new PatchEngine(scene).Apply(Doc(Binding("rebind", At("Sjdl", "a", 70))));
        Assert(replaced.Outcomes["rebind"].Applied && Entry(world, sign) == "Sjdl|a|70" && !world.Buckets["Nnew"].ContainsKey("Mi4f"), "A new reference replaces the earlier binding");
        Assert(Entry(world, other) == "Sjdl|b|38.6", "Another marker in the same segment bucket keeps its entry");
        // A broken binding without registration is repaired by a valid reference.
        (world, sign, other, scene) = Setup();
        foreach (var b in world.Buckets.Values) b.Remove("Mi4f");
        sign.Segment = "Smissing";
        var repaired = new PatchEngine(scene).Apply(Doc(Binding("repair", At("Nnew", "b", 1))));
        Assert(repaired.Outcomes["repair"].Applied && Entry(world, sign) == "Nnew|b|1", "A broken binding is repaired and registered: " + repaired.Outcomes["repair"].Error);
        // An inactive marker takes the binding and registers only once it is enabled.
        (world, sign, other, scene) = Setup();
        world.SetActive(sign, false); world.Mutations = 0;
        var inactive = new PatchEngine(scene).Apply(Doc(Binding("inactive", At("Nnew", "a", 3))));
        Assert(inactive.Outcomes["inactive"].Applied && sign.Segment == "Nnew" && world.Registrations(sign).Count == 0, "An inactive marker holds the binding without registration");
        Assert(new PatchEngine(scene).Apply(Doc(Binding("inactive", At("Nnew", "a", 3), extra: ",\"active\":true"))).Outcomes["inactive"].Applied && Entry(world, sign) == "Nnew|a|3", "Enabling registers the marker at its bound location");
        Assert(new PatchEngine(scene).Apply(Doc(Binding("inactive", At("Nnew", "a", 3), extra: ",\"active\":false"))).Outcomes["inactive"].Applied && world.Registrations(sign).Count == 0, "An explicit disable removes the registration and keeps the binding");
    }
    static void UnresolvedTargetsChangeNothing() {
        var cases = new[] {
            (At("Smissing", "a", 1), "missing"), (At("Sdup", "a", 1), "ambiguous"), (At("Nnew", "a", 500), "outside"),
            (At("Nnew", "c", 1), "end"), (At("Nnew", "a", -1), "negative"), ("{\"segment\":\"\",\"end\":\"a\",\"distance\":1}", "segment")
        };
        foreach (var (marker, expected) in cases) {
            var (world, sign, other, scene) = Setup();
            world.Segments["Sdup"] = new List<double> { 50, 50 };
            var result = new PatchEngine(scene).Apply(Doc(Binding("rebind", marker) + "," + Binding("keep", At("Nnew", "a", 9), OtherPath)));
            var outcome = result.Outcomes["rebind"];
            Assert(!outcome.Applied && outcome.Error.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0, "An unresolved relationship target fails the operation: " + expected + " / " + outcome.Error);
            Assert(sign.Segment == "S9sc" && sign.Distance == 225.6681 && Entry(world, sign) == "S9sc|a|225.6681" && sign.Position[0] == 20, "No substitute target and no partial registration change: " + expected);
            Assert(result.Outcomes["keep"].Applied && Entry(world, other) == "Nnew|a|9", "An independent relationship edit still applies: " + expected);
        }
    }
    static void UnsupportedRelationshipsAreRefusedAndIsolated() {
        var (world, sign, other, markers) = Setup();
        var (bridgeWorld, bridge, bridges) = (new FakeBridgeWorld(), new FakeBridge(), (BridgeScene)null);
        bridgeWorld.Objects["World/Large Scenery/Ela/Ela Bridge"] = new List<object> { bridge };
        bridges = new BridgeScene(bridgeWorld, Capability.Unchecked);
        var capabilities = new SceneCapabilities(() => new[] { "map" });
        capabilities.Add(bridges); capabilities.Add(markers);
        var bridgeBinding = "{\"id\":\"bind-bridge\",\"target\":{\"scene\":\"BushnellWhittier-MapManager\",\"path\":\"World/Large Scenery/Ela/Ela Bridge\",\"component\":\"Helpers.Culling.RendererCuller\",\"structure\":\"bridge-colliders-v1\"},\"relationships\":{\"trackMarker\":" + At("Nnew", "a", 1) + "},\"dependsOn\":[]}";
        var result = new PatchEngine(capabilities).Apply(Doc(bridgeBinding + "," + Binding("rebind", At("Nnew", "a", 2))));
        Assert(!result.Outcomes["bind-bridge"].Applied && result.Outcomes["bind-bridge"].Error.Contains("no supported track-marker relationship") && bridgeWorld.Mutations == 0, "A relationship on an object without one is refused, not ignored: " + result.Outcomes["bind-bridge"].Error);
        Assert(result.Outcomes["rebind"].Applied && Entry(world, sign) == "Nnew|a|2", "The marker capability is composed with others");
        var plain = new Scene(); plain.Objects["p"] = new List<object> { new Box() };
        var refused = new PatchEngine(plain).Apply(Doc("{\"id\":\"plain\",\"target\":{\"scene\":\"s\",\"path\":\"p\",\"component\":\"c\"},\"relationships\":{\"trackMarker\":{\"detach\":true}},\"dependsOn\":[]}"));
        Assert(!refused.Outcomes["plain"].Applied && plain.MutationCalls == 0, "A scene without relationships refuses a relationship");
        (world, sign, other, markers) = Setup();
        sign.Pattern = "Milepost markers belong to the milepost slice";
        var pattern = new PatchEngine(markers).Apply(Doc(Binding("milepost", Detach)));
        Assert(!pattern.Outcomes["milepost"].Applied && pattern.Outcomes["milepost"].Error.Contains("milepost") && world.Mutations == 0, "A marker outside the supported pattern is refused before mutation");
        (world, sign, other, markers) = Setup();
        world.Buckets["Sjdl"]["Mi4f"] = ("a", 1, other);
        var foreign = new PatchEngine(markers).Apply(Doc(Binding("foreign", Detach)));
        Assert(!foreign.Outcomes["foreign"].Applied && foreign.Outcomes["foreign"].Error.Contains("registration") && world.Mutations == 0, "An incoherent registration refuses the edit before mutation: " + foreign.Outcomes["foreign"].Error);
        var unknown = new PatchEngine(Setup().scene).Apply(Doc(Binding("future", "{\"segment\":\"Nnew\",\"end\":\"a\",\"distance\":1,\"offset\":2}")));
        Assert(!unknown.Outcomes["future"].Applied && unknown.Outcomes["future"].Error.StartsWith("Unsupported", StringComparison.Ordinal), "Unknown relationship content is preserved without execution");
        var otherKind = new PatchEngine(Setup().scene).Apply(Doc("{\"id\":\"span\"," + Target() + ",\"relationships\":{\"trackSpan\":{}},\"dependsOn\":[]}"));
        Assert(!otherKind.Outcomes["span"].Applied && otherKind.Outcomes["span"].Error.StartsWith("Unsupported", StringComparison.Ordinal), "A relationship kind this version does not know is not executed");
    }
    static void DependentsFollowRelationshipOutcome() {
        var (world, sign, other, scene) = Setup();
        var result = new PatchEngine(scene).Apply(Doc(Binding("dependent", Detach, OtherPath, "[\"base\"]") + "," + Binding("base", At("Smissing", "a", 1))));
        Assert(!result.Outcomes["dependent"].Applied && !result.Outcomes["base"].Applied && Entry(world, other) == "Sjdl|b|38.6", "A failed relationship isolates its dependents");
        result = new PatchEngine(scene).Apply(Doc(Binding("dependent", Detach, OtherPath, "[\"base\"]") + "," + Binding("base", At("Nnew", "a", 1))));
        Assert(result.Outcomes["dependent"].Applied && result.Outcomes["base"].Applied && world.Registrations(other).Count == 0 && Entry(world, sign) == "Nnew|a|1", "Dependencies run regardless of document order");
    }
    static void FailureAfterMutationRestoresRelationship() {
        foreach (var inject in new Action<FakeMarkerWorld>[] { w => w.ThrowAfterLocation = true, w => w.SkipRegister = true }) {
            var (world, sign, other, scene) = Setup();
            inject(world);
            var engine = new PatchEngine(scene);
            var outcome = engine.Apply(Doc(Binding("rebind", At("Nnew", "a", 5)) + "," + Binding("keep", Detach, OtherPath))).Outcomes["rebind"];
            Assert(!outcome.Applied && outcome.Error.StartsWith("Mutation failed and was restored", StringComparison.Ordinal), "A failure after the relationship write is restored: " + outcome.Error);
            Assert(sign.Segment == "S9sc" && sign.End == "a" && sign.Distance == 225.6681 && Entry(world, sign) == "S9sc|a|225.6681", "The captured reference and registration return");
            Assert(sign.CachedGroup == "ela", "The captured dependent cache returns: " + sign.CachedGroup);
            Assert(world.Registrations(other).Count == 0 && other.CachedGroup == null && other.Segment == null, "Prior independent work survives the restoration with its own dependent cache");
            Assert(!engine.Apply(Doc(Binding("rebind", At("Nnew", "a", 5)))).Outcomes["rebind"].Applied, "A failed relationship operation is not retried in the same load");
        }
    }
    static void UnverifiedCleanupBlocksRetry() {
        var (world, sign, other, scene) = Setup();
        // The old registration cannot be removed: the new one is added beside it and restoration cannot remove that either.
        world.StickyRegistration = true;
        var engine = new PatchEngine(scene);
        var outcome = engine.Apply(Doc(Binding("rebind", At("Nnew", "a", 5)))).Outcomes["rebind"];
        Assert(!outcome.Applied && outcome.Error.Contains("cleanup is unverified"), "Unverified relationship cleanup is reported: " + outcome.Error);
        world.StickyRegistration = false; var mutations = world.Mutations;
        var retry = engine.Apply(Doc(Binding("another", Detach))).Outcomes["another"];
        Assert(!retry.Applied && retry.Error.Contains("cleanup") && world.Mutations == mutations, "Another operation on the unverified marker is blocked");
        (world, sign, other, scene) = Setup();
        world.ThrowAfterLocation = true; world.FailRestoreLocation = true;
        sign.Segment = "Sjdl"; sign.Distance = 1; foreach (var b in world.Buckets.Values) b.Remove("Mi4f"); world.Load(sign);
        outcome = new PatchEngine(scene).Apply(Doc(Binding("rebind", At("Nnew", "a", 5)))).Outcomes["rebind"];
        Assert(!outcome.Applied && outcome.Error.Contains("cleanup is unverified"), "A failed restoration of the reference is reported as unverified: " + outcome.Error);
    }
    // A scanned field whose relevance comes from its declared type, as the native adapter derives it.
    static ReferenceField Declared(string name, Type declared, Func<object> read) => new ReferenceField(name, ReferenceScan.CanHold(declared, typeof(FakeMarker)), read);
    // The inventory's dependency scan and the marker reference scan share one pass: every field is read once,
    // the marker scan keeps its relevance, holders and fail-closed errors, and the other consumer sees every read.
    static void SharedFieldPassReadsEachFieldOnce() {
        var marker = new FakeMarker { Id = "Mx" };
        var reads = new Dictionary<string, int>();
        Func<object> Counted(string name, Func<object> read) => () => { reads[name] = reads.TryGetValue(name, out var n) ? n + 1 : 1; return read(); };
        FakeOwner Owner(string name, params ReferenceField[] fields) { var o = new FakeOwner { Name = name }; o.Fields.AddRange(fields); return o; }
        var owners = new List<object> {
            Owner("A", new ReferenceField("held", true, Counted("A.held", () => marker)), new ReferenceField("count", false, Counted("A.count", () => 3))),
            Owner("B", new ReferenceField("broken", false, Counted("B.broken", () => throw new InvalidOperationException("irrelevant read injected"))),
                new ReferenceField("list", true, Counted("B.list", () => new List<object> { "x", marker }))),
        };
        var seen = new List<string>();
        var shared = ReferenceScan.Of(owners, o => ((FakeOwner)o).Fields, i => i is FakeMarker, o => ((FakeOwner)o).Name,
            (owner, field, value, error) => seen.Add(((FakeOwner)owner).Name + "." + field.Name + (error == null ? "=" + value : "!" + error.Message)));
        Assert(reads.Count == 4 && reads.Values.All(n => n == 1), "One shared pass reads every field exactly once: " + String.Join(",", reads.Select(r => r.Key + "=" + r.Value)));
        Assert(seen.SequenceEqual(new[] { "A.held=FakeMarker", "A.count=3", "B.broken!irrelevant read injected", "B.list=System.Collections.Generic.List`1[System.Object]" }), "The other consumer sees every field in owner and field order with its value or error: " + String.Join(" | ", seen));
        Assert(shared.Complete && shared.HoldersOf(marker).Select(h => ((FakeOwner)h.Key).Name + "." + h.Value).SequenceEqual(new[] { "A.held", "B.list" }), "An unreadable irrelevant field leaves the marker scan complete with the same holders");
        reads.Clear();
        var alone = ReferenceScan.Of(owners, o => ((FakeOwner)o).Fields, i => i is FakeMarker, o => ((FakeOwner)o).Name);
        Assert(!reads.ContainsKey("A.count") && !reads.ContainsKey("B.broken") && alone.Complete && alone.HoldersOf(marker).Count == 2, "Without another consumer the scan still skips irrelevant fields");
        owners.Add(Owner("C", new ReferenceField("cached", true, () => throw new InvalidOperationException("relevant read injected"))));
        seen.Clear();
        var failing = ReferenceScan.Of(owners, o => ((FakeOwner)o).Fields, i => i is FakeMarker, o => ((FakeOwner)o).Name, (owner, field, value, error) => seen.Add(field.Name + (error == null ? "" : "!")));
        Assert(!failing.Complete && failing.Unreadable.Single() == "C.cached: relevant read injected" && seen.Last() == "cached!", "A relevant read failure leaves the shared scan incomplete and is reported to both consumers");
        Assert(failing.Refusal(marker, (o, f) => false, o => ((FakeOwner)o).Name).StartsWith("The direct-reference scan could not read 1 field, first C.cached", StringComparison.Ordinal), "The shared scan refuses with the same first unreadable field");
    }
    // The same pass taken one owner at a time: each owner's fields are read when the owner is added, in the same order
    // and with the same holders and fail-closed errors, so a discovery can pause between owners.
    static void ReferenceWalkPausesBetweenOwners() {
        var marker = new FakeMarker { Id = "Mx" }; var later = new FakeMarker { Id = "My" };
        object held = marker;
        FakeOwner Owner(string name, params ReferenceField[] fields) { var o = new FakeOwner { Name = name }; o.Fields.AddRange(fields); return o; }
        var a = Owner("A", new ReferenceField("held", true, () => marker));
        var b = Owner("B", new ReferenceField("held", true, () => held), new ReferenceField("count", false, () => 3));
        var c = Owner("C", new ReferenceField("cached", true, () => throw new InvalidOperationException("relevant read injected")));
        var seen = new List<string>();
        var walk = ReferenceScan.Begin(o => ((FakeOwner)o).Fields, i => i is FakeMarker, o => ((FakeOwner)o).Name,
            (owner, field, value, error) => seen.Add(((FakeOwner)owner).Name + "." + field.Name + (error == null ? "" : "!")));
        walk.Add(a);
        Assert(seen.SequenceEqual(new[] { "A.held" }), "Adding an owner reads that owner's fields only: " + String.Join(" | ", seen));
        held = later;
        walk.Add(b);
        var scan = walk.Finish();
        Assert(seen.SequenceEqual(new[] { "A.held", "B.held", "B.count" }), "Each owner is read once, when it is added: " + String.Join(" | ", seen));
        Assert(scan.Complete && scan.HoldersOf(marker).Select(h => ((FakeOwner)h.Key).Name + "." + h.Value).SequenceEqual(new[] { "A.held" }) &&
            scan.HoldersOf(later).Select(h => ((FakeOwner)h.Key).Name + "." + h.Value).SequenceEqual(new[] { "B.held" }), "The finished walk holds what each owner referred to when it was read");
        var whole = ReferenceScan.Of(new object[] { a, b, c }, o => ((FakeOwner)o).Fields, i => i is FakeMarker, o => ((FakeOwner)o).Name);
        walk = ReferenceScan.Begin(o => ((FakeOwner)o).Fields, i => i is FakeMarker, o => ((FakeOwner)o).Name);
        foreach (var owner in new[] { a, b, c }) walk.Add(owner);
        scan = walk.Finish();
        Assert(!scan.Complete && scan.Unreadable.SequenceEqual(whole.Unreadable) && scan.HoldersOf(marker).Count == whole.HoldersOf(marker).Count && scan.HoldersOf(later).Count == whole.HoldersOf(later).Count,
            "A walk over the same owners gives the scan of one pass, including its unreadable fields");
        walk = ReferenceScan.Begin(o => ((FakeOwner)o).Fields, i => i is FakeMarker, o => ((FakeOwner)o).Name, (owner, field, value, error) => throw new InvalidOperationException("observer injected"));
        var thrown = false;
        try { walk.Add(a); } catch (InvalidOperationException e) { thrown = e.Message == "observer injected"; }
        Assert(thrown, "The other consumer's failure reaches the caller of the walk, as it reaches the caller of one pass");
        walk = ReferenceScan.Begin(o => ((FakeOwner)o).Fields, i => i is FakeMarker, o => ((FakeOwner)o).Name);
        walk.Finish(); thrown = false;
        try { walk.Add(a); } catch (InvalidOperationException) { thrown = true; }
        Assert(thrown, "A finished walk accepts no further owner, so a scan in use never changes");
    }
    // New-game setup placements are read at most once per discovery, at the first eligible marker, with the
    // per-marker outcome of the original walk: a placement found before any read failure holds its marker.
    static void SetupPlacementsAreIndexedOncePerDiscovery() {
        var m1 = new FakeMarker { Id = "M1" }; var m2 = new FakeMarker { Id = "M2" }; var m3 = new FakeMarker { Id = "M3" };
        var reads = 0;
        IEnumerable<object> Placements(params object[] values) {
            foreach (var value in values) {
                reads++;
                if (value is Exception e) throw e;
                yield return value;
            }
        }
        var index = HolderIndex.Lazy(() => Placements(m1, m2));
        Assert(reads == 0, "Setup placements are not read before an eligible marker asks");
        Assert(index.Holds(m1) && index.Holds(m2) && !index.Holds(m3) && index.Holds(m1), "Placed markers are held and others are not");
        Assert(reads == 2, "Many eligible markers share one read of the setup placements: " + reads);
        var broken = new MissingFieldException("CarPlacement", "marker");
        reads = 0;
        var partial = HolderIndex.Lazy(() => Placements(m1, broken, m2));
        Assert(partial.Holds(m1), "A marker placed before a read failure is held, as the original walk returned on its first match");
        foreach (var later in new[] { m2, m3 }) {
            try { partial.Holds(later); throw new Exception("A marker not found before the read failure must not pass"); }
            catch (MissingFieldException e) { Assert(ReferenceEquals(e, broken) || e.Message == broken.Message, "The original read failure reaches the marker: " + e.Message); }
        }
        Assert(reads == 2, "Reading stops at the first failure: " + reads);
        var unused = HolderIndex.Lazy(() => Placements(broken));
        Assert(reads == 2, "An unreadable setup is not read, and refuses nothing, while no eligible marker asks");
        Assert(!HolderIndex.Lazy(() => Placements()).Holds(m1), "Without setup placements no marker is held");
    }
    static void FieldTypesThatCanHoldAMarkerAreScanned() {
        foreach (var type in new[] { typeof(FakeMarker), typeof(object), typeof(IList<FakeMarker>), typeof(List<FakeMarker>), typeof(FakeMarker[]),
                typeof(IList<object>), typeof(System.Collections.ArrayList), typeof(System.Collections.IList), typeof(GenericOnlyList<FakeMarker>) })
            Assert(ReferenceScan.CanHold(type, typeof(FakeMarker)), "A field declared " + type + " can hold a marker and is scanned");
        foreach (var type in new[] { typeof(int), typeof(string), typeof(IList<int>), typeof(List<string>), typeof(double[]), typeof(FakeOwner), typeof(IList<FakeOwner>) })
            Assert(!ReferenceScan.CanHold(type, typeof(FakeMarker)), "A field declared " + type + " cannot hold a marker and is not scanned");
    }
    static void IncompleteReferenceScanRefusesBeforeMutation() {
        FieldTypesThatCanHoldAMarkerAreScanned();
        Assert(!(new GenericOnlyList<object>() is System.Collections.IList) && !(new GenericOnlyList<FakeMarker>() is System.Collections.IList), "The test containers implement only the generic IList<T>");
        var irrelevant = new FakeOwner { Name = "Counter", Fields = { Declared("counts", typeof(IList<int>), () => throw new InvalidOperationException("irrelevant generic list injected")) } };
        var (cleanWorld, cleanSign, cleanOther, cleanScene) = Setup();
        cleanWorld.Owners.Add(irrelevant);
        Assert(new PatchEngine(cleanScene).Apply(Doc(Binding("rebind", At("Nnew", "a", 4)))).Outcomes["rebind"].Applied && Entry(cleanWorld, cleanSign) == "Nnew|a|4", "An unreadable field that cannot hold a marker does not refuse");
        // An irrelevant unreadable field and the colocated relative position do not refuse; every earlier test ran with both.
        var cases = new (string name, Action<FakeMarkerWorld, FakeMarker> inject, string expected)[] {
            ("unreadable relevant field", (w, m) => w.Owners.Add(new FakeOwner { Name = "Consumer", Fields = { new ReferenceField("cached", true, () => throw new InvalidOperationException("field read injected")) } }), "could not read 1 field, first Consumer.cached"),
            ("unenumerable list", (w, m) => w.Owners.Add(new FakeOwner { Name = "Consumer", Fields = { new ReferenceField("markers", true, () => new BrokenList()) } }), "could not read 1 field, first Consumer.markers"),
            ("unavailable fields", (w, m) => w.Owners.Add(new FakeOwner { Name = "Consumer", Fields = null }), "Consumer: fields unavailable"),
            ("foreign holder", (w, m) => w.Owners.Add(new FakeOwner { Name = "Consumer", Fields = { new ReferenceField("marker", true, () => m) } }), "Consumer.marker holds this marker"),
            ("holder in a list", (w, m) => w.Owners.Add(new FakeOwner { Name = "Consumer", Fields = { new ReferenceField("markers", true, () => new List<object> { m }) } }), "Consumer.markers holds this marker"),
            ("another object's relative position", (w, m) => w.Owners.Add(new FakeOwner { Name = "TrackRelativePosition elsewhere", Fields = { new ReferenceField("_marker", true, () => m) } }), "TrackRelativePosition elsewhere._marker holds this marker"),
            ("holder in a generic-only list", (w, m) => w.Owners.Add(new FakeOwner { Name = "Consumer", Fields = { Declared("markers", typeof(IList<FakeMarker>), () => new GenericOnlyList<FakeMarker> { m }) } }), "Consumer.markers holds this marker"),
            ("holder in a generic-only object list", (w, m) => w.Owners.Add(new FakeOwner { Name = "Consumer", Fields = { Declared("items", typeof(IList<object>), () => new GenericOnlyList<object> { "label", m }) } }), "Consumer.items holds this marker"),
            ("unenumerable generic-only list", (w, m) => w.Owners.Add(new FakeOwner { Name = "Consumer", Fields = { Declared("markers", typeof(IList<FakeMarker>), () => { var list = new GenericOnlyList<FakeMarker> { m }; list.FailEnumeration = true; return list; }) } }), "could not read 1 field, first Consumer.markers"),
            ("unreadable generic list declaration", (w, m) => w.Owners.Add(new FakeOwner { Name = "Consumer", Fields = { Declared("markers", typeof(IList<FakeMarker>), () => throw new InvalidOperationException("generic list read injected")) } }), "could not read 1 field, first Consumer.markers"),
        };
        foreach (var (name, inject, expected) in cases) {
            var (world, sign, other, session, editor, store) = Session();
            inject(world, sign);
            var runtime = new PatchEngine(new TrackMarkerScene(world, Capability.Unchecked)).Apply(Doc(Binding("rebind", At("Nnew", "a", 4)) + "," + Placed("move")));
            Assert(!runtime.Outcomes["rebind"].Applied && runtime.Outcomes["rebind"].Error.Contains(expected), name + ": runtime application refuses: " + runtime.Outcomes["rebind"].Error);
            Assert(!runtime.Outcomes["move"].Applied, name + ": a placement of the same marker is refused too, since its preflight shares the scan");
            var before = session.DocumentText;
            var edit = session.Bind(Sign, MarkerBinding.Detached);
            Assert(!edit.Applied && edit.Error.Contains(expected), name + ": the editor binding refuses: " + edit.Error);
            Assert(world.Mutations == 0 && editor.UndoGroups.Count == 0 && session.DocumentText == before && store.Patch == null, name + ": no mutation, history entry or document change");
            Assert(Entry(world, sign) == "S9sc|a|225.6681" && sign.CachedGroup == "ela" && sign.Position[0] == 20 && Entry(world, other) == "Sjdl|b|38.6", name + ": reference, registration, dependent cache and pose stay");
            var incomplete = new[] { "unreadable relevant field", "unenumerable list", "unavailable fields", "unreadable generic list declaration", "unenumerable generic-only list" };
            Assert(session.Bind(Other, MarkerBinding.Detached).Applied == !incomplete.Contains(name),
                name + ": an incomplete scan refuses every marker, a holder refuses only its marker");
        }
    }
    static readonly TargetIdentity Sign = new TargetIdentity("BushnellWhittier", SignPath, "Track.TrackMarker", TrackMarkerScene.Structure);
    static readonly TargetIdentity Other = new TargetIdentity("BushnellWhittier", OtherPath, "Track.TrackMarker", TrackMarkerScene.Structure);
    static (FakeMarkerWorld world, FakeMarker sign, FakeMarker other, AuthoringSession session, Editor editor, Store store) Session(string patch = null) {
        var (world, sign, other, scene) = Setup();
        var store = new Store { Patch = patch }; var editor = new Editor();
        var session = new AuthoringSession(new PatchEngine(scene), editor, store, "map"); editor.Session = session;
        return (world, sign, other, session, editor, store);
    }
    static Newtonsoft.Json.Linq.JObject Only(AuthoringSession session) => (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(session.DocumentText)["modifications"].Single();
    static void EditorSetReplaceDetachUndoRedo() {
        var (world, sign, other, session, editor, store) = Session();
        var set = session.Bind(Sign, MarkerBinding.At(new TrackLocation("Nnew", "b", 12)));
        Assert(set.Applied && Entry(world, sign) == "Nnew|b|12" && sign.Position[0] == 20, "An editor binding sets the reference and registration without moving the object: " + set.Error);
        var item = Only(session);
        Assert((string)item["relationships"]["trackMarker"]["segment"] == "Nnew" && (string)item["relationships"]["trackMarker"]["end"] == "b" && (double)item["relationships"]["trackMarker"]["distance"] == 12
            && item["placement"] == null && item["active"] == null, "The document records only the binding: " + item);
        Assert(session.MinimumPatcherVersion == "0.4.0", "A relationship requires the patcher version that introduced it");
        var moved = new Placement(new[] { 1040.0, 1, 2 }, new[] { 0.0, 0, 0, 1 });
        Assert(session.Edit(Sign, moved, null).Applied && sign.Position[0] == 40 && Entry(world, sign) == "Nnew|b|12", "A later placement edit keeps the binding");
        Assert(Only(session)["relationships"] != null && Only(session)["placement"] != null, "The placement edit preserves the authored relationship");
        Assert(session.Bind(Sign, MarkerBinding.At(new TrackLocation("Sjdl", "a", 3))).Applied && Entry(world, sign) == "Sjdl|a|3" && sign.Position[0] == 40, "A replacement binding changes only the binding");
        Assert(session.Bind(Sign, MarkerBinding.Detached).Applied && sign.Segment == null && world.Registrations(sign).Count == 0 && sign.Position[0] == 40, "An editor detach clears reference and registration");
        Assert((bool)Only(session)["relationships"]["trackMarker"]["detach"], "The document records an explicit detach");
        Assert(sign.CachedGroup == null, "Detach clears the dependent cache");
        editor.Undo(); Assert(Entry(world, sign) == "Sjdl|a|3" && sign.CachedGroup == "alarka", "Undo of a detach restores the reference, registration and dependent cache");
        editor.Undo(); Assert(Entry(world, sign) == "Nnew|b|12" && sign.Position[0] == 40 && sign.CachedGroup == "extension", "Undo of a replacement restores the prior binding and dependent cache");
        editor.Undo(); Assert(sign.Position[0] == 20 && Entry(world, sign) == "Nnew|b|12" && sign.CachedGroup == "extension", "Undo of the placement keeps the binding and dependent cache");
        editor.Undo(); Assert(Entry(world, sign) == "S9sc|a|225.6681" && sign.CachedGroup == "ela" && session.DocumentText.Contains("\"modifications\": []"), "Undo of the first binding restores the native relationship and dependent cache");
        editor.Redo(); editor.Redo(); editor.Redo(); editor.Redo();
        Assert(sign.Segment == null && world.Registrations(sign).Count == 0 && sign.Position[0] == 40 && sign.CachedGroup == null, "Redo reapplies every relationship and placement edit");
        Assert(Entry(world, other) == "Sjdl|b|38.6" && other.CachedGroup == "alarka", "No editor operation touched the unrelated marker or its dependent cache");
    }
    static void EditorRemovalRecomputesUnderlyingRelationship() {
        var (world, sign, other, session, editor, store) = Session();
        session.Bind(Sign, MarkerBinding.At(new TrackLocation("Nnew", "a", 7)));
        session.Edit(Sign, new Placement(new[] { 1030.0, 1, 2 }, new[] { 0.0, 0, 0, 1 }), null);
        var cleared = session.Bind(Sign, null);
        Assert(cleared.Applied && Entry(world, sign) == "S9sc|a|225.6681" && sign.Position[0] == 30, "Clearing the authored binding returns the underlying relationship and keeps the placement: " + cleared.Error);
        Assert(Only(session)["relationships"] == null && Only(session)["placement"] != null, "The modification keeps its placement without a relationship");
        Assert(!session.Bind(Sign, null).Applied, "Clearing an absent binding is refused");
        editor.Undo(); Assert(Entry(world, sign) == "Nnew|a|7", "Undo of the clearing restores the authored binding");
        Assert(session.Bind(Other, MarkerBinding.Detached).Applied && world.Registrations(other).Count == 0, "A second object's binding is a separate contribution");
        Assert(session.Remove(Sign).Applied && Entry(world, sign) == "S9sc|a|225.6681" && sign.Position[0] == 20 && sign.CachedGroup == "ela", "Removal recomputes the underlying relationship, dependent cache and pose");
        Assert(world.Registrations(other).Count == 0 && other.CachedGroup == null, "Removal keeps the remaining authored contribution of another object and its dependent cache");
        editor.Undo(); Assert(Entry(world, sign) == "Nnew|a|7" && sign.Position[0] == 30 && sign.CachedGroup == "extension", "Undo of the removal restores the authored binding and dependent cache");
        // Clearing the only setting of a modification removes it.
        (world, sign, other, session, editor, store) = Session();
        session.Bind(Sign, MarkerBinding.Detached);
        Assert(session.Bind(Sign, null).Applied && session.DocumentText.Contains("\"modifications\": []") && Entry(world, sign) == "S9sc|a|225.6681", "Clearing a binding-only modification removes it");
    }
    static void EditorRefusalAndFailureRestore() {
        var (world, sign, other, session, editor, store) = Session();
        var before = session.DocumentText;
        foreach (var bad in new[] { new TrackLocation("Smissing", "a", 1), new TrackLocation("Nnew", "a", 999) }) {
            var refused = session.Bind(Sign, MarkerBinding.At(bad));
            Assert(!refused.Applied && session.DocumentText == before && editor.UndoGroups.Count == 0 && Entry(world, sign) == "S9sc|a|225.6681" && world.Mutations == 0, "An unresolved editor binding changes nothing and records no history: " + refused.Error);
        }
        world.SkipRegister = true;
        var failed = session.Bind(Sign, MarkerBinding.At(new TrackLocation("Nnew", "a", 1)));
        Assert(!failed.Applied && failed.Error.StartsWith("Edit failed and the prior state was restored", StringComparison.Ordinal) && Entry(world, sign) == "S9sc|a|225.6681" && session.DocumentText == before, "A failed editor binding restores reference and registration: " + failed.Error);
        Assert(sign.CachedGroup == "ela" && other.CachedGroup == "alarka" && Entry(world, other) == "Sjdl|b|38.6", "A failed editor binding restores the dependent cache and leaves the unrelated marker's cache");
        world.StickyRegistration = true;
        failed = session.Bind(Sign, MarkerBinding.At(new TrackLocation("Nnew", "a", 1)));
        Assert(!failed.Applied && failed.Error.Contains("cleanup is unverified"), "Incomplete editor cleanup is reported: " + failed.Error);
        world.StickyRegistration = false;
        Assert(!session.Bind(Sign, MarkerBinding.Detached).Applied, "The unverified marker refuses another edit");
        foreach (var b in world.Buckets.Values) b.Remove("Mi4f");
        world.Load(sign);
        Assert(session.Recheck(Sign).Applied && Entry(world, sign) == "S9sc|a|225.6681", "Recheck verifies the restored relationship before unblocking");
        Assert(session.Bind(Sign, MarkerBinding.Detached).Applied, "The verified marker accepts edits again");
    }
    // A transform gizmo gesture on a marker object changes its placement only; an authored binding stays in the modification.
    static void GizmoGestureKeepsTheBinding() {
        var (world, sign, other, markers) = Setup();
        var capabilities = new SceneCapabilities(() => world.MapIds, new FakeMarkerInspection(world));
        capabilities.Add(markers);
        var store = new Store(); var editor = new Editor();
        var engine = new PatchEngine(capabilities);
        var session = new AuthoringSession(engine, editor, store, "map"); editor.Session = session;
        var inventory = new SceneInventory(capabilities);
        Assert(session.Bind(Sign, MarkerBinding.At(new TrackLocation("Nnew", "b", 6))).Applied && Entry(world, sign) == "Nnew|b|6" && sign.CachedGroup == "extension", "The marker is bound before the gesture");
        PlacementGesture Begin() { inventory.Refresh(); return new PlacementGesture(engine, session, Sign, inventory.Pin(inventory.Entries.Single(e => ReferenceEquals(e.Handle, sign)))); }
        var cancelled = Begin();
        Assert(cancelled.Preview(new Placement(new[] { 1035.0, 1, 2 }, new[] { 0.0, 0, 0, 1 })).Applied && sign.Position[0] == 35, "A preview moves the marker object: " + cancelled.Current.Position[0]);
        Assert(Entry(world, sign) == "Nnew|b|6" && sign.CachedGroup == "extension", "A preview leaves the binding, registration and dependent cache");
        Assert(cancelled.Cancel().Applied && sign.Position[0] == 20 && Entry(world, sign) == "Nnew|b|6" && sign.CachedGroup == "extension" && editor.UndoGroups.Count == 1, "Cancel restores the pose and keeps the binding without history");
        var completed = Begin();
        Assert(completed.Preview(new Placement(new[] { 1040.0, 1, 2 }, new[] { 0.0, 0, 0, 1 })).Applied, "A second preview applies");
        var outcome = completed.Complete();
        Assert(outcome.Applied && sign.Position[0] == 40 && Entry(world, sign) == "Nnew|b|6" && sign.CachedGroup == "extension" && editor.UndoGroups.Count == 2, "A completed gesture places the marker and keeps the binding: " + outcome.Error);
        var item = Only(session);
        Assert(item["placement"] != null && (string)item["relationships"]["trackMarker"]["segment"] == "Nnew", "The modification keeps the authored binding beside the gesture's placement: " + item);
        editor.Undo(); Assert(sign.Position[0] == 20 && Entry(world, sign) == "Nnew|b|6", "Undo of the gesture restores the pose and keeps the binding");
        editor.Undo(); Assert(Entry(world, sign) == "S9sc|a|225.6681" && sign.CachedGroup == "ela", "Undo of the binding restores the native relationship");
        Assert(Entry(world, other) == "Sjdl|b|38.6" && other.CachedGroup == "alarka" && other.Position[0] == 70, "The unrelated marker is untouched");
    }
    // A physical snap of a marker object changes its position once; the authored binding and registration stay.
    static void PhysicalSnapKeepsTheBinding() {
        var (world, sign, other, markers) = Setup();
        var capabilities = new SceneCapabilities(() => world.MapIds, new FakeMarkerInspection(world));
        capabilities.Add(markers);
        var store = new Store(); var editor = new Editor();
        var session = new AuthoringSession(new PatchEngine(capabilities), editor, store, "map"); editor.Session = session;
        var tracks = new FakeSnapTracks();
        var node = new SelectionCandidate(new object(), SelectionIdentity.Graph(SelectionKind.TrackNode, "Nend"), "Track node Nend", "1050,1,2");
        tracks.Items.Add(new SnapGeometry(node, new[] { new double[] { 1050, 1, 2 } }));
        var snap = new SnapController(session, capabilities, tracks);
        Assert(session.Bind(Sign, MarkerBinding.At(new TrackLocation("Nnew", "a", 9))).Applied && Entry(world, sign) == "Nnew|a|9", "The marker is bound before the snap");
        var inventory = new SceneInventory(capabilities); inventory.Refresh();
        var selection = new PendingSelection(tracks, SelectionKind.TrackNode, "Snap physical position");
        selection.Offer(new[] { node }); Assert(selection.Confirm(), "The snap endpoint confirms");
        var request = snap.Prepare(Sign, inventory.Pin(inventory.Entries.Single(e => ReferenceEquals(e.Handle, sign))), selection.Confirmed, node);
        Assert(sign.Position[0] == 20 && Entry(world, sign) == "Nnew|a|9", "Preparing a snap changes nothing");
        var outcome = snap.Apply(request);
        Assert(outcome.Applied && sign.Position[0] == 50 && sign.Rotation[3] == 1, "The snap moves the marker object to the endpoint and keeps its rotation: " + outcome.Error);
        Assert(Entry(world, sign) == "Nnew|a|9" && sign.Segment == "Nnew" && sign.CachedGroup == "extension", "The snap leaves the binding, registration and dependent cache");
        var item = Only(session);
        Assert((string)item["placement"]["mode"] == "fixed" && (string)item["relationships"]["trackMarker"]["segment"] == "Nnew", "The document holds the fixed snapped placement beside the binding: " + item);
        editor.Undo(); Assert(sign.Position[0] == 20 && Entry(world, sign) == "Nnew|a|9", "Undo of the snap restores the position and keeps the binding");
        Assert(Entry(world, other) == "Sjdl|b|38.6" && other.Position[0] == 70, "The unrelated marker is untouched");
    }
    static void SaveAndReloadReproduceBindingOnce() {
        var (world, sign, other, session, editor, store) = Session();
        session.Bind(Sign, MarkerBinding.At(new TrackLocation("Nnew", "b", 12.25)));
        session.Bind(Other, MarkerBinding.Detached);
        Assert(editor.Save() == null && store.Info.Contains("crankyant/scene-patcher >=0.4.0"), "Save raises the dependency to the relationship version: " + store.Info);
        var (fresh, freshSign, freshOther, freshScene) = Setup();
        var engine = new PatchEngine(freshScene);
        var reload = engine.Apply(store.Patch);
        Assert(reload.Outcomes.Values.All(o => o.Applied) && Entry(fresh, freshSign) == "Nnew|b|12.25" && fresh.Registrations(freshOther).Count == 0, "A reloaded world reproduces the saved binding and detachment");
        engine.Apply(store.Patch);
        Assert(Entry(fresh, freshSign) == "Nnew|b|12.25" && fresh.Buckets.Values.Sum(b => b.Count) == 1, "A repeated world-load callback leaves exactly one owned registration");
        // An existing saved document reopens and edits its relationship.
        var (w2, s2, o2, reopened, e2, st2) = Session(store.Patch);
        Assert(reopened.Blocked == null && reopened.Bind(Sign, MarkerBinding.Detached).Applied && s2.Segment == null, "A saved relationship document reopens for editing");
    }
}
