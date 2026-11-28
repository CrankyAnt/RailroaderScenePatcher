// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RailroaderScenePatcher;
using RailroaderScenePatcher.Core;

// Whole scene patch file tests for the whole move of a crossing without signals through its crossing object, against
// a controlled scene of transforms: the crossing object at Alarka Jct with two crossbucks, each with a post below it,
// and one bound crossing pad; and the three crossings that share the path World/Roads Bryson/Old River Rd/Crossing
// Unprotected, told apart by their pads' marker IDs. A placement of the crossing object moves its crossbucks together,
// a crossbuck with its own placement included, and leaves its pads where their binding sets them. The same holds for a
// duplicated crossing. A crossbuck's own placement is recorded relative to its crossing object (PartHolds), so it
// reaches the same place whatever the order of the modifications and scene patch files.
static class CrossingObjectMoveTests {
    const string Scene = "BushnellWhittier";
    const string AlarkaPath = "World/Large Scenery/Alarka Jct/Crossing Unprotected (1)", OldRiverPath = "World/Roads Bryson/Old River Rd/Crossing Unprotected";
    const string Nw = "Sign-Crossing0 (1)", Se = "Sign-Crossing0 (2)";
    static void Assert(bool value, string message) { if (!value) throw new Exception("Crossing object moves: " + message); }
    static string N(IEnumerable<double> values) => String.Join(",", values.Select(v => Math.Round(v, 3).ToString("R", CultureInfo.InvariantCulture)));
    static readonly double[] Identity = { 0, 0, 0, 1 }, Quarter = Turn.Yaw(90);
    // Where Alarka Jct stands, and where the tests move it.
    static readonly double[] Home = { 1620, 9, 1211 }, Shifted = { 1630, 9, 1216 }, AtNewSiding = { 5200, 14, 5900 };
    static bool Near(IReadOnlyList<double> a, IReadOnlyList<double> b, double tolerance = 0.001) => a.Count == b.Count && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d <= tolerance);
    static bool SameTurn(IReadOnlyList<double> a, IReadOnlyList<double> b) => Math.Abs(Math.Abs(a.Zip(b, (x, y) => x * y).Sum()) - 1) < 1e-6;
    static double[] Plus(IReadOnlyList<double> a, IReadOnlyList<double> b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
    static double[] Minus(IReadOnlyList<double> a, IReadOnlyList<double> b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    // Where a point that stood at offset from the crossing stands when the crossing stands at position, turn.
    static double[] At(double[] position, double[] turn, double[] offset) => Plus(position, Turn.Rotate(turn, offset));

    // One scene object: a name, its place in the object above it, or in map coordinates without one.
    internal sealed class Node {
        public string Name; public Node Parent; public readonly List<Node> Kids = new List<Node>();
        public double[] Position = { 0, 0, 0 }, Rotation = { 0, 0, 0, 1 };
        public bool Active = true, Crossbuck, Pad, ModObject;
        // A pad's marker ID and the track binding that sets its place.
        public string PadId, Binding;
        public Node Add(Node kid) { kid.Parent = this; Kids.Add(kid); return kid; }
        public string Path => Parent == null ? Name : Parent.Path + "/" + Name;
        public IEnumerable<Node> Below() { foreach (var kid in Kids) { yield return kid; foreach (var deeper in kid.Below()) yield return deeper; } }
        public bool IsBelow(Node above) { for (var p = Parent; p != null; p = p.Parent) if (p == above) return true; return false; }
        public bool IsCrossing => Kids.Any(k => k.Pad || k.Crossbuck);
        public Node Top => Crossbuck ? this : Parent?.Top;
    }
    // The scene as the group world, the crossing world and the whole move see it.
    internal sealed class Tree : IGroupWorld, IFrameWorld, ICrossbuckWorld {
        public readonly List<Node> Roots = new List<Node>();
        public int Mutations;
        // The next whole move leaves the crossing object moved and then fails, as a game call that throws halfway.
        public bool ThrowAfterMove;
        public IEnumerable<Node> All => Roots.SelectMany(r => new[] { r }.Concat(r.Below()));
        public static Placement World(Node node) {
            if (node.Parent == null) return new Placement((double[])node.Position.Clone(), (double[])node.Rotation.Clone());
            var above = World(node.Parent);
            return new Placement(At(above.Position, above.Rotation, node.Position), Turn.Multiply(above.Rotation, node.Rotation));
        }
        public static void SetWorld(Node node, Placement placement) {
            if (node.Parent == null) { node.Position = (double[])placement.Position.Clone(); node.Rotation = (double[])placement.Rotation.Clone(); return; }
            var above = World(node.Parent); var inverse = Turn.Inverse(above.Rotation);
            node.Position = Turn.Rotate(inverse, Minus(placement.Position, above.Position)); node.Rotation = Turn.Multiply(inverse, placement.Rotation);
        }
        public IEnumerable<string> MapIds => new[] { "map" };
        public IReadOnlyList<object> Find(string scene, string path) => All.Where(n => n.Crossbuck && n.Path == path).Cast<object>().ToList();
        public IReadOnlyList<KeyValuePair<object, string>> CrossbucksAt(string scene, string path) {
            var found = All.Where(n => n.Crossbuck && n.Path == path).ToList();
            return found.Select(n => new KeyValuePair<object, string>(n, n.Parent?.Kids.FirstOrDefault(k => k.Pad)?.PadId)).ToList();
        }
        public string Refusal(object group) => null;
        public string DisableRefusal(object target) => null;
        public GroupObservation Observe(object group) {
            var root = (Node)group; var at = World(root); var inverse = Turn.Inverse(at.Rotation);
            var parts = root.Below().Select((n, i) => { var w = World(n); return new GroupPart(n.Path.Substring(root.Path.Length) + "#" + i, Turn.Rotate(inverse, Minus(w.Position, at.Position)), Turn.Multiply(inverse, w.Rotation), n.Active, false, n, n.Parent == root ? null : n.Parent); });
            return new GroupObservation(new ObjectState(at, root.Active), parts.ToList(), new GroupCuller[0], root.Parent);
        }
        public void SetActive(object group, bool active) { Mutations++; ((Node)group).Active = active; }
        public void SetPlacement(object group, Placement placement) { Mutations++; SetWorld((Node)group, placement); }
        public IReadOnlyList<object> FindParts(string scene, string path) => All.Where(n => !n.Crossbuck && n.Top != null && n.Path == path).Cast<object>().ToList();
        public string PartRefusal(object part) => null;
        public string ActivityRefusal(object part) => null;
        public string FollowingRefusal(object target) => null;
        public string PlacementRefusal(object target) => null;
        public PartObservation ObservePart(object part) {
            var node = (Node)part; var group = node.Top;
            return new PartObservation(new ObjectState(World(node), node.Active), group, Observe(group), node.Parent, World(node.Parent));
        }
        public void SetPartActive(object part, bool active) { Mutations++; ((Node)part).Active = active; }
        public void SetPartPlacement(object part, Placement placement) { Mutations++; SetWorld((Node)part, placement); }
        public void RefreshCulling(object group, CullingPlan plan) { }
        public IReadOnlyList<IReadOnlyList<double>> Presence(object part, IReadOnlyList<object> without) => new IReadOnlyList<double>[0];
        public IReadOnlyList<double> Anchor(object part) => World((Node)part).Position;
        public bool Below(object part, object above) => part is Node n && above is Node a && n.IsBelow(a);
        public bool Alive(object handle) => handle is Node n && All.Contains(n);
        public object FrameAbove(object handle) => handle is Node n && n.Crossbuck && n.Parent != null && n.Parent.IsCrossing ? n.Parent : null;
        public object ModObjectOf(object handle) { for (var n = handle as Node; n != null; n = n.Parent) if (n.ModObject) return n; return null; }
        public Placement PlacementOf(object handle) => World((Node)handle);
    }
    // The whole move of a crossing object in the tree: its pads are put back where they stood.
    sealed class Whole : IWholeMoveWorld, IFrameWorld {
        readonly Tree tree;
        public Whole(Tree tree) { this.tree = tree; }
        public IEnumerable<string> MapIds => tree.MapIds;
        public IReadOnlyList<object> Find(string scene, string path) => new object[0];
        public string Refusal(object group) => null;
        public string DisableRefusal(object target) => null;
        public GroupObservation Observe(object group) => tree.Observe(group);
        public void SetActive(object group, bool active) => throw new InvalidOperationException(CrossingObjectScene.NoActivity);
        public void SetPlacement(object group, Placement placement) => tree.SetPlacement(group, placement);
        public IReadOnlyList<object> Stays(object group) => ((Node)group).Below().Where(n => n.Pad).Cast<object>().ToList();
        public void SetPlacementKeeping(object group, Placement placement, IReadOnlyList<object> kept) {
            var standing = kept.Cast<Node>().Select(n => (n, Tree.World(n))).ToList();
            tree.SetPlacement(group, placement);
            if (tree.ThrowAfterMove) { tree.ThrowAfterMove = false; throw new InvalidOperationException("injected after the crossing moved"); }
            foreach (var (node, at) in standing) Tree.SetWorld(node, at);
        }
        public IReadOnlyList<object> FindParts(string scene, string path) => new object[0];
        public string PartRefusal(object part) => null;
        public string ActivityRefusal(object part) => null;
        public string FollowingRefusal(object target) => CrossingObjectScene.NoFollowing;
        public string PlacementRefusal(object target) => null;
        public PartObservation ObservePart(object part) => tree.ObservePart(part);
        public void SetPartActive(object part, bool active) => tree.SetPartActive(part, active);
        public void SetPartPlacement(object part, Placement placement) => tree.SetPartPlacement(part, placement);
        public void RefreshCulling(object group, CullingPlan plan) { }
        public IReadOnlyList<IReadOnlyList<double>> Presence(object part, IReadOnlyList<object> without) => tree.Presence(part, without);
        public IReadOnlyList<double> Anchor(object part) => tree.Anchor(part);
        public bool Below(object part, object above) => tree.Below(part, above);
        public bool Alive(object handle) => tree.Alive(handle);
        public object FrameAbove(object handle) => tree.FrameAbove(handle);
        public object ModObjectOf(object handle) => tree.ModObjectOf(handle);
        public Placement PlacementOf(object handle) => tree.PlacementOf(handle);
    }
    sealed class Crossings : ICrossingObjectWorld {
        readonly Tree tree;
        public Crossings(Tree tree) { this.tree = tree; }
        public IEnumerable<string> MapIds => tree.MapIds;
        public IReadOnlyList<object> Find(string scene, string path) => tree.All.Where(n => n.IsCrossing && n.Path == path).Cast<object>().ToList();
        public IReadOnlyList<string> PadMarkerIds(object crossing) => ((Node)crossing).Kids.Where(k => k.Pad && k.PadId != null).Select(k => k.PadId).ToList();
        public double[] Position(object crossing) => Tree.World((Node)crossing).Position;
        public double[] Rotation(object crossing) => Tree.World((Node)crossing).Rotation;
        public bool Active(object crossing) => ((Node)crossing).Active;
    }
    // Builds a duplicated crossing as the native world does: a copy of the crossing object with its crossbucks and pads,
    // each pad with the marker ID given for it and no track binding.
    sealed class Builder : IModObjectScene, IModObjectPartScene {
        readonly Tree tree;
        public Builder(Tree tree) { this.tree = tree; }
        public IReadOnlyList<object> PartMarkers(object source) => source is Node n && n.IsCrossing ? n.Kids.Where(k => k.Pad).Cast<object>().ToList() : new List<object>();
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId) => Build(identity, source, placement, markerId, new string[0]);
        public object Build(TargetIdentity identity, object source, Placement placement, string markerId, IReadOnlyList<string> partMarkerIds) {
            var from = (Node)source;
            if (tree.All.Any(n => n.Path == identity.Path)) throw new InvalidOperationException(ModObjects.Taken);
            Node Copy(Node n) {
                var made = new Node { Name = n.Name, Position = (double[])n.Position.Clone(), Rotation = (double[])n.Rotation.Clone(), Active = n.Active, Crossbuck = n.Crossbuck, Pad = n.Pad, PadId = n.PadId, Binding = n.Binding };
                foreach (var kid in n.Kids) made.Add(Copy(kid));
                return made;
            }
            var copy = Copy(from); copy.Name = identity.Path; copy.ModObject = true; copy.Position = (double[])placement.Position.Clone(); copy.Rotation = (double[])placement.Rotation.Clone();
            var pads = copy.Kids.Where(k => k.Pad).ToList();
            for (var i = 0; i < pads.Count; i++) { pads[i].PadId = partMarkerIds[i]; pads[i].Binding = null; }
            tree.Roots.Add(copy);
            return copy;
        }
        public void Remove(object modObject) => tree.Roots.Remove((Node)modObject);
    }
    // The marker IDs a duplicated crossing's pads take: any ID the pads of the tree leave free.
    sealed class PadIds : IScene, IMarkerIdScene {
        readonly Tree tree;
        public PadIds(Tree tree) { this.tree = tree; }
        public IEnumerable<string> MapIds => tree.MapIds;
        public Capability Capability => Capability.Unchecked;
        public IReadOnlyList<object> Resolve(TargetIdentity identity) => new object[0];
        public void Preflight(object target) { }
        public object Capture(object target) => null;
        public ObjectState Read(object target) => new ObjectState(null, null);
        public void Apply(object target, ObjectState state) { }
        public bool Restore(object target, object snapshot) => true;
        public IReadOnlyList<string> MarkerIdUsers(object target, string id) => tree.All.Where(n => n.PadId == id).Select(n => n.Path).ToList();
        public bool RewritesRegistration(object target, ObjectState state) => false;
        public string MarkerIdRefusal(object target, string id) => null;
        public string NewMarkerIdRefusal(object source, string id) => tree.All.Any(n => n.PadId == id) ? "The marker ID is taken" : null;
    }

    // The inventory of the tree as the native inventory lists it: each crossing object, which its outliner node inspects,
    // each crossbuck and each pad. Where crossings share a path, the crossing object and its crossbucks carry their pad's
    // marker ID.
    internal sealed class Inspection : ITargetedInspectionScene {
        readonly Tree tree;
        public Inspection(Tree tree) { this.tree = tree; }
        public IEnumerable<string> MapIds => tree.MapIds;
        string Shared(Node crossing) => tree.All.Count(n => n.IsCrossing && n.Path == crossing.Path) > 1 ? crossing.Kids.FirstOrDefault(k => k.Pad)?.PadId : null;
        public SceneObservation Of(Node n) {
            var at = Tree.World(n);
            if (n.IsCrossing) return new SceneObservation(n, CrossingObjectScene.Identity(Scene, n.Path, Shared(n)), CrossingObjectScene.Family, n.Active, at.Position, at.Rotation,
                new string[0], new string[0], new string[0], CommandBand.CrossingObjectOperations, category: SceneObjectCategories.GradeCrossing);
            if (n.Crossbuck) return new SceneObservation(n, new TargetIdentity(Scene, n.Path, SceneObjects.ComponentType, SceneObjects.Structure, Shared(n.Parent)), SceneFamilies.SceneObject, n.Active, at.Position, at.Rotation,
                new string[0], new string[0], new string[0], CommandBand.CrossbuckOperations, category: SceneObjectCategories.GradeCrossing);
            return new SceneObservation(n, new TargetIdentity(Scene, n.Path, "Track.TrackMarker", CrossingPadScene.Structure), SceneFamilies.CrossingPad, n.Active, at.Position, at.Rotation,
                new[] { SceneObservation.MarkerIdBinding(n.PadId, "Generic"), "Marker segment=" + n.Binding }, new string[0], new string[0], CommandBand.PadOperations);
        }
        public IEnumerable<SceneObservation> Discover() => tree.All.Where(n => n.IsCrossing || n.Crossbuck || n.Pad).Select(Of).ToList();
        public SceneObservation Observe(object handle, TargetIdentity identity) => handle is Node n && tree.All.Contains(n) ? Of(n) : null;
    }
    // Alarka Jct with its two crossbucks NW and SE of its bound pad, each with a post, and the three Old River Rd crossings.
    internal sealed class Fixture {
        public readonly Tree Tree = new Tree();
        public readonly Node Alarka, NwSign, SeSign, NwPost, Pad;
        public readonly Node[] OldRiver;
        public readonly PartHolds Holds = new PartHolds();
        public readonly SceneCapabilities Scene;
        public readonly CrossingObjectScene Crossing;
        public readonly PatchEngine Engine;
        public Fixture(bool wholeMove = true) {
            Alarka = new Node { Name = AlarkaPath, Position = (double[])Home.Clone() };
            NwSign = Alarka.Add(new Node { Name = Nw, Crossbuck = true, Position = new[] { -8.0, 0, 8 }, Rotation = Turn.Yaw(45) });
            NwPost = NwSign.Add(new Node { Name = "Post", Position = new[] { 0.0, 0, 0.5 } });
            SeSign = Alarka.Add(new Node { Name = Se, Crossbuck = true, Position = new[] { 8.0, 0, -8 }, Rotation = Turn.Yaw(225) });
            SeSign.Add(new Node { Name = "Post", Position = new[] { 0.0, 0, 0.5 } });
            Pad = Alarka.Add(new Node { Name = "Crossing Pad", Pad = true, PadId = "Malk", Binding = "Nnew/a/5" });
            Tree.Roots.Add(Alarka);
            OldRiver = Enumerable.Range(0, 3).Select(i => {
                var crossing = new Node { Name = OldRiverPath, Position = new double[] { 5350 + 700 * i, 10, 5747 } };
                crossing.Add(new Node { Name = Nw, Crossbuck = true, Position = new[] { -6.0, 0, 6 } });
                crossing.Add(new Node { Name = Se, Crossbuck = true, Position = new[] { 6.0, 0, -6 } });
                crossing.Add(new Node { Name = "Crossing Pad", Pad = true, PadId = "Mold" + (i + 1), Binding = "Sold/a/" + (20 + 10 * i) });
                Tree.Roots.Add(crossing);
                return crossing;
            }).ToArray();
            Scene = new SceneCapabilities(() => Tree.MapIds, new Inspection(Tree)) { ModObjectWorld = new Builder(Tree) };
            Scene.Add(Crossing = new CrossingObjectScene(new Crossings(Tree), Capability.Unchecked, wholeMove ? new WholeMove(new Whole(Tree), Holds) : null));
            Scene.Add(new SceneObjects(Tree, Capability.Unchecked, Holds));
            Scene.Add(new ChildObjects(Tree, Holds));
            Scene.Add(new PadIds(Tree));
            Engine = new PatchEngine(Scene);
        }
        public PatchEditingSession Session(out Store store, out NativeMapEditor editor) {
            store = new Store(); editor = new NativeMapEditor();
            var session = new PatchEditingSession(Engine, editor, store, LoadedMod.PatcherId, "author/pack"); editor.Session = session;
            return session;
        }
        public static double[] Where(Node n) => Tree.World(n).Position;
        public static double[] Facing(Node n) => Tree.World(n).Rotation;
        public string Layout() => String.Join("; ", new[] { Alarka, NwSign, NwPost, SeSign, Pad }.Select(n => n.Name + "=" + N(Where(n))));
        // Whether every crossbuck and post of the crossing keeps its place relative to the crossing standing at position, turn.
        public bool Together(Node crossing, double[] position, double[] turn) =>
            crossing.Below().Where(n => !n.Pad).All(n => Near(Where(n), At(position, turn, Authored[n].Item1)) && SameTurn(Facing(n), Turn.Multiply(turn, Authored[n].Item2)));
        // Where each object stood relative to its crossing in the unedited scene.
        public readonly Dictionary<Node, Tuple<double[], double[]>> Authored = new Dictionary<Node, Tuple<double[], double[]>>();
        public void Remember() {
            foreach (var crossing in Tree.Roots) {
                var at = Tree.World(crossing); var inverse = Turn.Inverse(at.Rotation);
                foreach (var n in crossing.Below()) { var w = Tree.World(n); Authored[n] = Tuple.Create(Turn.Rotate(inverse, Minus(w.Position, at.Position)), Turn.Multiply(inverse, w.Rotation)); }
            }
        }
    }
    static TargetIdentity CrossingId(string path = AlarkaPath, string padMarkerId = null) => CrossingObjectScene.Identity(Scene, path, padMarkerId);
    static TargetIdentity CrossbuckId(string crossing, string name, string padMarkerId = null) => new TargetIdentity(Scene, crossing + "/" + name, SceneObjects.ComponentType, SceneObjects.Structure, padMarkerId);
    static TargetIdentity PostId(string crossing, string name) => new TargetIdentity(Scene, crossing + "/" + name + "/Post", ChildObjects.ComponentType, ChildObjects.Structure);
    static string Doc(params string[] items) => "{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{" + String.Join(",", items) + "}}";
    static string Placed(double[] position, double[] rotation) =>
        ",\"placement\":{\"mode\":\"fixed\",\"position\":[" + String.Join(",", position.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "],\"rotation\":[" + String.Join(",", (rotation ?? Identity).Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]}";
    static string MoveCrossing(string id, double[] position, double[] rotation = null, string path = AlarkaPath, string padMarkerId = null, string extra = "") =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + path + "\",\"component\":\"" + CrossingObjectScene.ComponentType + "\",\"structure\":\"" + CrossingObjectScene.Structure + "\"" +
        (padMarkerId == null ? "" : ",\"padMarkerId\":\"" + padMarkerId + "\"") + "}" + Placed(position, rotation) + extra + ",\"dependsOn\":[]}";
    static string MoveCrossbuck(string id, string name, double[] position, double[] rotation = null) =>
        "\"" + id + "\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + AlarkaPath + "/" + name + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"" + SceneObjects.Structure + "\"}" + Placed(position, rotation) + ",\"dependsOn\":[]}";

    public static void Run() {
        MovingTheCrossingMovesItsCrossbucksTogether();
        RotatingTheCrossingTurnsItsCrossbucksAboutIt();
        PadsStayAndKeepTheirTrackBinding();
        ACrossbuckWithItsOwnPlacementMovesWithTheCrossing();
        OwnPlacementsReachTheSamePlaceInEitherKeyOrderAndInTwoFiles();
        RemovalReturnsACrossbuckToItsPlaceInTheMovedCrossing();
        CrossingsAtASharedPathMoveOneAtATime();
        ADuplicatedCrossingMovesAndRotatesAsAWhole();
        RefusalsAndAFailureLeaveTheCrossingAsItWas();
        HistorySaveAndReload();
        TheCrossingObjectIsTheRowOfItsNode();
        TheNodeEditIsPinnedToTheInventory();
        TheGizmoMovesTheWholeCrossing();
        ThePlacementTabOffersTheModesOfAWholeMove();
        Console.WriteLine("PASS: crossing object moves");
    }

    static void MovingTheCrossingMovesItsCrossbucksTogether() {
        var f = new Fixture(); f.Remember();
        var outcome = f.Engine.Apply(Doc(MoveCrossing("move-crossing", Shifted))).Outcomes["move-crossing"];
        Assert(outcome.Applied && Near(Fixture.Where(f.Alarka), Shifted), "A move of the crossing through its crossing object applies: " + outcome.Error + " " + f.Layout());
        Assert(f.Together(f.Alarka, Shifted, Identity), "The crossbucks and their posts moved together and keep their placement relative to each other: " + f.Layout());
        Assert(Near(Fixture.Where(f.NwSign), new[] { 1622.0, 9, 1224 }) && Near(Fixture.Where(f.SeSign), new[] { 1638.0, 9, 1208 }), "Each crossbuck moved 10 m east and 5 m north: " + f.Layout());
    }

    static void RotatingTheCrossingTurnsItsCrossbucksAboutIt() {
        var f = new Fixture(); f.Remember();
        var outcome = f.Engine.Apply(Doc(MoveCrossing("turn-crossing", Home, Quarter))).Outcomes["turn-crossing"];
        Assert(outcome.Applied && f.Together(f.Alarka, Home, Quarter), "A rotation turns the crossbucks about the crossing object, each keeping its direction relative to it: " + outcome.Error + " " + f.Layout());
        // 8 m west and 8 m north turns to 8 m east and 8 m north.
        Assert(Near(Fixture.Where(f.NwSign), new[] { 1628.0, 9, 1219 }) && SameTurn(Fixture.Facing(f.NwSign), Turn.Multiply(Quarter, Turn.Yaw(45))), "The NW crossbuck turned a quarter about the crossing: " + f.Layout());
    }

    static void PadsStayAndKeepTheirTrackBinding() {
        var f = new Fixture();
        var pad = Tree.World(f.Pad);
        Assert(f.Engine.Apply(Doc(MoveCrossing("move-crossing", Shifted, Quarter))).Outcomes.Values.Single().Applied, "The crossing moves and turns");
        Assert(Near(Tree.World(f.Pad).Position, pad.Position) && SameTurn(Tree.World(f.Pad).Rotation, pad.Rotation), "The pad is where it was: " + f.Layout());
        Assert(f.Pad.Binding == "Nnew/a/5" && f.Pad.PadId == "Malk", "The pad keeps its track binding and marker ID");
        Assert(f.OldRiver.All(c => Near(Tree.World(c).Position, new double[] { 5350 + 700 * Array.IndexOf(f.OldRiver, c), 10, 5747 })), "No other crossing moved");
    }

    static void ACrossbuckWithItsOwnPlacementMovesWithTheCrossing() {
        var f = new Fixture(); var session = f.Session(out _, out _);
        var held = new[] { 1610.0, 9, 1222 };
        Assert(session.Edit(CrossbuckId(AlarkaPath, Nw), new Placement(held, Identity), null).Applied && Near(Fixture.Where(f.NwSign), held), "A crossbuck takes a placement of its own");
        var post = new[] { 1611.0, 9, 1222 };
        Assert(session.Edit(PostId(AlarkaPath, Se), new Placement(post, Identity), null).Applied, "A post below the other crossbuck takes a placement of its own");
        var moved = session.Edit(CrossingId(), new Placement(Shifted, Quarter), null);
        Assert(moved.Applied, "The crossing moves and turns: " + moved.Error);
        // 10 m west and 11 m north of the crossing turns to 11 m east and 10 m north of the moved crossing.
        Assert(Near(Fixture.Where(f.NwSign), new[] { 1641.0, 9, 1226 }) && SameTurn(Fixture.Facing(f.NwSign), Quarter), "The crossbuck with its own placement moved and turned with the crossing: " + f.Layout());
        Assert(Near(Fixture.Where(f.NwPost), Plus(Fixture.Where(f.NwSign), Turn.Rotate(Quarter, new[] { 0.0, 0, 0.5 }))), "Its post keeps its place on it: " + f.Layout());
        var sePost = f.SeSign.Kids.Single();
        Assert(Near(Fixture.Where(sePost), At(Shifted, Quarter, Minus(post, Home))), "The post with its own placement keeps it relative to its group and moved with the crossing: " + N(Fixture.Where(sePost)));
        var items = JObject.Parse(session.ScenePatchFileText)["modifications"].Children<JProperty>().Select(p => (JObject)p.Value).ToList();
        var crossbuck = items.Single(m => ((string)m["target"]["path"]).EndsWith("/" + Nw, StringComparison.Ordinal));
        Assert(Near(crossbuck["placement"]["position"].Select(v => (double)v).ToArray(), held), "The file keeps the crossbuck's placement relative to the crossing at its map placement: " + crossbuck["placement"]);
        var gizmo = session.Edit(CrossbuckId(AlarkaPath, Nw), new Placement(new[] { 1645.0, 9, 1226 }, Quarter), null);
        Assert(gizmo.Applied && Near(Fixture.Where(f.NwSign), new[] { 1645.0, 9, 1226 }), "A later edit of the crossbuck takes the map placement the window shows: " + gizmo.Error + " " + f.Layout());
    }

    // The final layout of a crossbuck move and a crossing move, for comparing orders.
    static string Final(Fixture f) => f.Layout();
    static void OwnPlacementsReachTheSamePlaceInEitherKeyOrderAndInTwoFiles() {
        var held = new[] { 1610.0, 9, 1222 };
        var orders = new[] {
            Doc(MoveCrossing("a-crossing", Shifted, Quarter), MoveCrossbuck("b-crossbuck", Nw, held)),
            Doc(MoveCrossbuck("a-crossbuck", Nw, held), MoveCrossing("b-crossing", Shifted, Quarter)),
        };
        var finals = orders.Select(doc => { var f = new Fixture(); var result = f.Engine.Apply(doc); Assert(result.Outcomes.Values.All(o => o.Applied), "Both modifications apply: " + String.Join("; ", result.Outcomes.Values.Select(o => o.Error))); return Final(f); }).ToList();
        Assert(finals[0] == finals[1], "A crossbuck and its crossing in one scene patch file reach the same placements in either key order: " + finals[0] + " / " + finals[1]);
        var crossbucks = Doc(MoveCrossbuck("move-crossbuck", Nw, held)); var crossings = Doc(MoveCrossing("move-crossing", Shifted, Quarter));
        string Loaded(params (string id, string text)[] mods) {
            var f = new Fixture();
            var runtime = new PatchRuntime(f.Engine, new Diagnostics(), "0.5.0", "test");
            runtime.ApplyLoaded(mods.Select(m => new LoadedMod(new PatchSource(m.id, m.id, "1.0.0"), m.text, new[] { "crankyant/scene-patcher" })).ToArray(), null);
            return Final(f);
        }
        var first = Loaded(("author/crossbucks", crossbucks), ("author/crossings", crossings)); var second = Loaded(("author/crossings", crossings), ("author/crossbucks", crossbucks));
        Assert(first == second && first == finals[0], "A crossbuck and its crossing in two mods reach the same placements in either load order: " + first + " / " + second);
        Assert(first.Contains(Nw + "=1641,9,1226"), "The crossbuck stands at its own placement relative to the moved crossing: " + first);
    }
    sealed class Diagnostics : IPatchDiagnostics { public void Warning(PatchFailure failure) { } }

    static void RemovalReturnsACrossbuckToItsPlaceInTheMovedCrossing() {
        var f = new Fixture(); f.Remember(); var session = f.Session(out _, out var editor);
        Assert(session.Edit(CrossbuckId(AlarkaPath, Nw), new Placement(new[] { 1610.0, 9, 1222 }, Identity), null).Applied && session.Edit(CrossingId(), new Placement(Shifted, Quarter), null).Applied, "A crossbuck and then its crossing move");
        var removed = session.Remove(CrossbuckId(AlarkaPath, Nw));
        Assert(removed.Applied && f.Together(f.Alarka, Shifted, Quarter), "Remove modification returns the crossbuck to its unedited place in the moved crossing: " + removed.Error + " " + f.Layout());
        editor.Undo();
        Assert(Near(Fixture.Where(f.NwSign), new[] { 1641.0, 9, 1226 }), "Undo of the removal holds the crossbuck at its own placement in the crossing again: " + f.Layout());
    }

    static void CrossingsAtASharedPathMoveOneAtATime() {
        var f = new Fixture(); f.Remember();
        var second = f.OldRiver[1]; var before = f.OldRiver.Select(c => Tree.World(c).Position).ToList();
        var outcome = f.Engine.Apply(Doc(MoveCrossing("move-old-river-2", AtNewSiding, Quarter, OldRiverPath, "Mold2"))).Outcomes.Values.Single();
        Assert(outcome.Applied && Near(Tree.World(second).Position, AtNewSiding) && f.Together(second, AtNewSiding, Quarter), "The crossing whose pad holds the marker ID moves with its crossbucks: " + outcome.Error);
        Assert(Near(Tree.World(f.OldRiver[0]).Position, before[0]) && Near(Tree.World(f.OldRiver[2]).Position, before[2]) && f.Together(f.OldRiver[0], before[0], Identity) && f.Together(f.OldRiver[2], before[2], Identity),
            "The other crossings at the path keep their place with their crossbucks");
        Assert(Near(Tree.World(second.Kids.Single(k => k.Pad)).Position, before[1]) && second.Kids.Single(k => k.Pad).Binding == "Sold/a/30", "Its pad stays where its binding sets it");
        var held = new Fixture();
        var result = held.Engine.Apply(Doc(MoveCrossing("move-old-river", AtNewSiding, null, OldRiverPath)));
        Assert(!result.Outcomes.Values.Single().Applied && held.OldRiver.All(c => Near(Tree.World(c).Position, before[Array.IndexOf(held.OldRiver, c)])), "Without a pad marker ID the shared path names no single crossing, and nothing moves");
    }

    static void ADuplicatedCrossingMovesAndRotatesAsAWhole() {
        var f = new Fixture(); var session = f.Session(out var store, out var editor);
        var placed = session.PlaceModObject(CrossingId(), new Placement(AtNewSiding, Identity), null, null, n => 0);
        Assert(placed.Applied && ModObjects.IsPath(placed.Target.Path), "The crossing is duplicated: " + placed.Error);
        var copy = (Node)placed.ResolvedTarget;
        var copyPad = copy.Kids.Single(k => k.Pad);
        Assert(copyPad.Binding == null && copyPad.PadId != "Malk", "The duplicate's pad starts unbound with its own marker ID");
        var copySign = copy.Kids.Single(k => k.Name == Nw);
        var held = Plus(AtNewSiding, new[] { -10.0, 0, 11 });
        Assert(session.Edit(new TargetIdentity(Scene, placed.Target.Path + "/" + Nw, SceneObjects.ComponentType, SceneObjects.Structure), new Placement(held, Identity), null).Applied, "A crossbuck of the duplicate takes a placement of its own");
        var target = Plus(AtNewSiding, new[] { 30.0, 0, 0 });
        var moved = session.Edit(placed.Target, new Placement(target, Quarter), null);
        Assert(moved.Applied && Near(Tree.World(copy).Position, target), "The duplicate moves and turns as a whole: " + moved.Error);
        Assert(Near(Tree.World(copy.Kids.Single(k => k.Name == Se)).Position, At(target, Quarter, new[] { 8.0, 0, -8 })), "Its other crossbuck moved and turned with it");
        Assert(Near(Tree.World(copySign).Position, At(target, Quarter, new[] { -10.0, 0, 11 })), "Its crossbuck with its own placement keeps it relative to the duplicate: " + N(Tree.World(copySign).Position));
        Assert(Near(Tree.World(copyPad).Position, AtNewSiding) && copyPad.Binding == null, "Its pad stays where it was placed");
        Assert(Near(Tree.World(f.Alarka).Position, Home) && Near(Fixture.Where(f.NwSign), At(Home, Turn.Yaw(0), new[] { -8.0, 0, 8 })), "The source crossing is untouched");
        editor.Undo();
        Assert(Near(Tree.World(copy).Position, AtNewSiding) && Near(Tree.World(copySign).Position, held), "Undo returns the duplicate and its crossbucks");
        editor.Redo();
        Assert(Near(Tree.World(copy).Position, target) && Near(Tree.World(copySign).Position, At(target, Quarter, new[] { -10.0, 0, 11 })), "Redo moves them again");
        Assert(editor.Save() == null, "Save writes the scene patch file");
        var item = (JObject)JObject.Parse(store.Patch)["modifications"].Children<JProperty>().Single(p => (string)p.Value["target"]["path"] == placed.Target.Path).Value;
        Assert(Near(item["placement"]["position"].Select(v => (double)v).ToArray(), target), "The file records where the duplicate stands: " + item["placement"]);
        var fresh = new Fixture();
        var reload = fresh.Engine.Apply(store.Patch, "author/pack");
        Assert(reload.Outcomes.Values.All(o => o.Applied), "A fresh load applies the duplicate and its edits: " + String.Join("; ", reload.Outcomes.Values.Select(o => o.Error)));
        var rebuilt = fresh.Tree.Roots.Single(r => r.ModObject);
        Assert(Near(Tree.World(rebuilt).Position, target) && Near(Tree.World(rebuilt.Kids.Single(k => k.Name == Nw)).Position, At(target, Quarter, new[] { -10.0, 0, 11 })) && Near(Tree.World(rebuilt.Kids.Single(k => k.Name == Se)).Position, At(target, Quarter, new[] { 8.0, 0, -8 })),
            "The rebuilt duplicate stands where it was moved, with its crossbucks in the same places: " + N(Tree.World(rebuilt.Kids.Single(k => k.Name == Nw)).Position));
        Assert(Near(Tree.World(fresh.Alarka).Position, Home), "The source still stands where the map has it");
    }

    static void RefusalsAndAFailureLeaveTheCrossingAsItWas() {
        var f = new Fixture(); f.Remember();
        var refused = f.Engine.Apply(Doc(MoveCrossing("off", Home, null, AlarkaPath, null, ",\"active\":false"))).Outcomes.Values.Single();
        Assert(!refused.Applied && refused.Error.Contains(CrossingObjectScene.NoActivity) && f.Tree.Mutations == 0 && f.Alarka.Active, "Activity of the whole crossing is refused before any change: " + refused.Error);
        var following = f.Engine.Apply("{\"formatVersion\":1,\"sourceMap\":{\"mapId\":\"map\"},\"modifications\":{\"follow\":{\"target\":{\"scene\":\"" + Scene + "\",\"path\":\"" + AlarkaPath + "\",\"component\":\"UnityEngine.Transform\",\"structure\":\"crossing-object-v1\"},\"placement\":{\"mode\":\"trackFollowing\",\"track\":{\"segment\":\"Nnew\",\"end\":\"a\",\"distance\":5},\"offset\":[0,0,0],\"rotation\":[0,0,0,1]},\"dependsOn\":[]}}}").Outcomes.Values.Single();
        Assert(!following.Applied && f.Tree.Mutations == 0, "Track-following placement of the crossing is refused before any change: " + following.Error);
        f.Tree.ThrowAfterMove = true;
        var failed = f.Engine.Apply(Doc(MoveCrossing("move-crossing", Shifted))).Outcomes.Values.Single();
        Assert(!failed.Applied && failed.Error.StartsWith("The change failed. The object is back as it was.", StringComparison.Ordinal), "A failure after the crossing moved is restored: " + failed.Error);
        Assert(Near(Tree.World(f.Alarka).Position, Home) && f.Together(f.Alarka, Home, Identity) && Near(Tree.World(f.Pad).Position, Home), "The crossing, its crossbucks and its pad stand where they stood: " + f.Layout());
        var without = new Fixture(false);
        var old = without.Engine.Apply(Doc(MoveCrossing("move-crossing", Shifted))).Outcomes.Values.Single();
        Assert(!old.Applied && Near(Tree.World(without.Alarka).Position, Home) && !without.Crossing.MovesCrossing && f.Crossing.MovesCrossing, "Without a whole move the capability moves no crossing: " + old.Error);
    }

    static void HistorySaveAndReload() {
        var f = new Fixture(); f.Remember(); var session = f.Session(out var store, out var editor);
        Assert(session.Edit(CrossingId(), new Placement(Shifted, Quarter), null).Applied && f.Together(f.Alarka, Shifted, Quarter), "The crossing moves as a whole in scene patch editing");
        editor.Undo(); Assert(f.Together(f.Alarka, Home, Identity) && Near(Tree.World(f.Alarka).Position, Home), "Undo returns the crossing with its crossbucks: " + f.Layout());
        editor.Redo(); Assert(f.Together(f.Alarka, Shifted, Quarter), "Redo moves it again");
        Assert(editor.Save() == null, "Save writes the scene patch file");
        var item = (JObject)JObject.Parse(store.Patch)["modifications"].Children<JProperty>().Single().Value;
        Assert((string)item["target"]["structure"] == CrossingObjectScene.Structure && item["active"] == null && Near(item["placement"]["position"].Select(v => (double)v).ToArray(), Shifted),
            "The file records the crossing object's placement and nothing else: " + item.ToString(Newtonsoft.Json.Formatting.None));
        var fresh = new Fixture(); fresh.Remember();
        Assert(fresh.Engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && fresh.Together(fresh.Alarka, Shifted, Quarter) && Near(Tree.World(fresh.Pad).Position, Home), "A fresh load reproduces the move with the pad in place");
        Assert(fresh.Engine.Apply(store.Patch).Outcomes.Values.All(o => o.Applied) && fresh.Together(fresh.Alarka, Shifted, Quarter), "Applying the file again adds no offset");
        Assert(session.Remove(CrossingId()).Applied && f.Together(f.Alarka, Home, Identity), "Remove modification returns the crossing with its crossbucks");
    }

    // The node of a crossing without signals has the crossing object's inventory entry as its row: a click on the node
    // inspects the crossing object, and the entry is listed nowhere else. Crossings that share a path each take their own.
    static void TheCrossingObjectIsTheRowOfItsNode() {
        var f = new Fixture();
        var entries = new Inspection(f.Tree).Discover().ToList();
        var tree = OutlinerGrouping.Build(entries, entries, "", false, null);
        IEnumerable<InventoryPresentationState.OutlineNode> All(IEnumerable<InventoryPresentationState.OutlineNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(All(n.Children)));
        var nodes = All(tree.Tops).ToList();
        var crossings = entries.Where(e => e.Family == CrossingObjectScene.Family).ToList();
        foreach (var crossing in crossings) {
            var rows = nodes.Where(n => ReferenceEquals(n.Row, crossing)).ToList();
            Assert(rows.Count == 1 && rows[0].Children.Count == 3 && rows[0].Children.All(c => c.Row != null && ((Node)c.Row.Handle).Parent == crossing.Handle),
                "The crossing object " + crossing.Identity.Path + "/" + crossing.Identity.PadMarkerId + " is the row of its node, which holds its own crossbucks and pad: " + String.Join(", ", rows.SelectMany(r => r.Children).Select(c => c.Label)));
        }
        Assert(!tree.GroupNodes.Values.Any(n => n.Identity != null), "No crossing node is left without a row");
        Assert(crossings.Count(c => c.Identity.Path == OldRiverPath) == 3 && crossings.Where(c => c.Identity.Path == OldRiverPath).Select(c => c.Identity.PadMarkerId).OrderBy(id => id).SequenceEqual(new[] { "Mold1", "Mold2", "Mold3" }),
            "Each crossing object at the shared path is named by its pad's marker ID");
        var alarka = nodes.Single(n => ReferenceEquals(n.Row?.Handle, f.Alarka));
        Assert(alarka.Label == "Crossing Unprotected (1)", "The row keeps the crossing's game name: " + alarka.Label);
    }

    // The edit of a crossing through its node binds to the inspected crossing object as every edit does: a crossing that
    // another hand moved after the inspection refuses the edit, and a stale inventory refuses the pin.
    static void TheNodeEditIsPinnedToTheInventory() {
        var f = new Fixture(); f.Remember(); var session = f.Session(out _, out _);
        var inventory = new SceneInventory(f.Scene); inventory.Refresh();
        var entry = inventory.Entries.Single(e => ReferenceEquals(e.Handle, f.Alarka));
        var pinned = session.Edit(entry.Identity, new Placement(Shifted, Quarter), null, inventory.Pin(entry));
        Assert(pinned.Applied && f.Together(f.Alarka, Shifted, Quarter), "A pinned edit of the crossing object moves the crossing as a whole: " + pinned.Error);
        var g = new Fixture(); var edits = g.Session(out _, out _);
        var current = new SceneInventory(g.Scene); current.Refresh();
        var seen = current.Entries.Single(e => ReferenceEquals(e.Handle, g.Alarka));
        var pin = current.Pin(seen);
        Tree.SetWorld(g.Alarka, new Placement(new[] { 1625.0, 9, 1211 }, Identity));
        var refused = edits.Edit(seen.Identity, new Placement(Shifted, Quarter), null, pin);
        Assert(!refused.Applied && refused.Error.Contains("changed") && Near(Tree.World(g.Alarka).Position, new[] { 1625.0, 9, 1211 }), "An edit of a crossing another hand moved after the inspection is refused: " + refused.Error);
        string stale = null;
        try { current.Pin(seen); } catch (InvalidOperationException e) { stale = e.Message; }
        Assert(stale != null && current.IsStale, "A stale inventory refuses the pin: " + stale);
    }

    // The gizmo on the crossing object moves the crossing as a whole: its previews carry the crossbucks along and leave
    // the pad, Cancel returns everything, and Complete writes one edit.
    static void TheGizmoMovesTheWholeCrossing() {
        foreach (var complete in new[] { true, false }) {
            var f = new Fixture(); f.Remember(); var session = f.Session(out _, out var editor);
            var inventory = new SceneInventory(f.Scene); inventory.Refresh();
            var entry = inventory.Entries.Single(e => ReferenceEquals(e.Handle, f.Alarka));
            var gesture = new PlacementGesture(f.Engine, session, entry.Identity, inventory.Pin(entry));
            Assert(Near(gesture.Initial.Position, Home), "The gizmo starts on the crossing object: " + N(gesture.Initial.Position));
            foreach (var x in new[] { 1625.0, 1630.0 }) {
                var at = new[] { x, 9, 1216 };
                var preview = gesture.Preview(new Placement(at, Quarter));
                Assert(preview.Applied && f.Together(f.Alarka, at, Quarter) && Near(Tree.World(f.Pad).Position, Home), "A preview moves the crossbucks with the crossing and leaves the pad: " + preview.Error + " " + f.Layout());
            }
            var end = complete ? gesture.Complete() : gesture.Cancel();
            Assert(end.Applied, "The gesture ends: " + end.Error);
            Assert(complete ? f.Together(f.Alarka, Shifted, Quarter) : f.Together(f.Alarka, Home, Identity), "The crossing ends where the gesture leaves it: " + f.Layout());
            Assert(editor.CanUndo == complete, "Only a completed gesture records an edit");
        }
    }

    // The crossing object's Placement tab offers what a group's whole move offers: Numeric, Gizmo and Snap move and rotate
    // it. Follow does not apply, since a crossing uses fixed placement, and Enabled does not either.
    static void ThePlacementTabOffersTheModesOfAWholeMove() {
        Assert(new Fixture().Crossing.MovesCrossing && !new Fixture(false).Crossing.MovesCrossing, "The capability says whether it moves the crossing as a whole");
        var entry = new Inspection(new Fixture().Tree).Discover().First(e => e.Family == CrossingObjectScene.Family);
        var edits = SupportedEdits.Of(entry);
        Assert(SupportedEdits.ReasonOf(edits, SupportedEdits.Move) == null && SupportedEdits.ReasonOf(edits, SupportedEdits.Rotate) == null, "Move and Rotate are available");
        Assert(new[] { CommandBand.NumericMode, CommandBand.GizmoMode, CommandBand.SnapMode }.All(mode => SupportedEdits.ModeBlock(edits, mode, true) == null), "Numeric, Gizmo and Snap are open");
        Assert(SupportedEdits.ModeBlock(edits, CommandBand.FollowMode, true) == EditReasons.Short(EditReasons.Fixed), "Follow says the crossing uses fixed placement: " + SupportedEdits.ModeBlock(edits, CommandBand.FollowMode, true));
        Assert(SupportedEdits.ReasonOf(edits, SupportedEdits.Activity) == EditReasons.Short(SupportedEdits.CrossingNoActivity), "Its activity can't be edited");
        Assert(SupportedEdits.Lines(edits).First() == "Available: Move, Rotate", "Supported edits: " + String.Join(" | ", SupportedEdits.Lines(edits)));
        var unsupported = SupportedEdits.Of(entry, new SupportedEdits.Live { WholeMoveReason = EditReasons.GameBuild });
        Assert(SupportedEdits.ReasonOf(unsupported, SupportedEdits.Move) == EditReasons.Short(EditReasons.GameBuild) && SupportedEdits.ModeBlock(unsupported, CommandBand.GizmoMode, true) != null,
            "Without the whole move on this game build the modes say so");
        Assert(entry.Operations.StartsWith("Move or rotate the whole crossing", StringComparison.Ordinal), "The Object tab names the whole move: " + entry.Operations);
    }
}
