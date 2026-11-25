// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;

namespace RailroaderScenePatcher.Core {
    // The game's cullers: a simple culler switches its own object, a renderer culler the renderers below its object,
    // and a game-object culler the objects it lists.
    public enum CullerKind { Simple, Renderer, GameObject }

    // The activity rule of own culling, and what a culler's sphere means for what it switches.
    public static class CullingActivity {
        // An object is on while the modder or the scene has it on and one of two holds. Its own culling shows it: a
        // game-object sphere that took it over shows it, and any of the spheres it needs is visible. Or the own sphere
        // of a moved part below it is visible: the object then stays on for that part.
        // always: no simple culler stands at or above the object.
        public static bool Active(bool desired, bool always, IEnumerable<bool> need, bool gate = true, IEnumerable<bool> below = null) =>
            desired && ((gate && (always || (need != null && need.Any(visible => visible)))) || (below != null && below.Any(visible => visible)));
        // The own activity of an object as the modder or the scene set it: for an object own culling switches, the
        // value own culling holds for it, and for every other object its own activity.
        public static bool Desired(bool activeSelf, bool? controlled) => controlled ?? activeSelf;
        // Whether an object that is switched off is off only through a culler, so that it counts as on. ownDesired is
        // the activity own culling holds for an object it switches, null for any other object: such an object is off
        // through own culling exactly while the modder or the scene has it on. Otherwise it is off through its simple
        // culler while that culler switches it and hides it now, or through a game-object culler that lists it and
        // hides its objects now; listing holds, for each game-object culler on the object, above it or in its group,
        // whether it lists the object and whether it shows its objects. Off while its culler shows, the object was
        // switched off by the scene or a modder (AuthoredActivity.Enabled).
        public static bool OffThroughCuller(bool? ownDesired, bool simpleSwitches, bool simpleShows, IEnumerable<KeyValuePair<bool, bool>> listing) =>
            ownDesired ?? ((simpleSwitches && !simpleShows) || (listing != null && listing.Any(culler => culler.Key && !culler.Value)));
        // Whether a sphere shows what it culls, by the rule of the culler it stands in for. A simple culler has no
        // distance band; a game-object culler in distance-band mode ignores visibility.
        public static bool Shows(CullerKind kind, bool distanceBandMode, bool visible, int band, int visibleBand) =>
            kind == CullerKind.Simple ? visible : kind == CullerKind.GameObject && distanceBandMode ? band <= visibleBand : visible && band <= visibleBand;
    }
    // The objects a game-object culler switches: those in its list and those own culling took out of it.
    public static class ListedObjects {
        public static IReadOnlyList<object> Union(IEnumerable<object> listed, IEnumerable<object> handedOver) {
            var all = new List<object>();
            foreach (var item in (listed ?? Enumerable.Empty<object>()).Concat(handedOver ?? Enumerable.Empty<object>()))
                if (item != null && !all.Any(other => ReferenceEquals(other, item))) all.Add(item);
            return all;
        }
    }
    sealed class ByIdentity : IEqualityComparer<object> {
        internal static readonly ByIdentity Instance = new ByIdentity();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

    // An own culling sphere for a moved part: centered on its bounds and enclosing them, in map coordinates.
    public sealed class PlannedRegion {
        internal PlannedRegion(string id, GroupCuller culler, CullerKind kind, object owner, double[] centre, double radius) {
            Id = id; CullerId = culler.Id; Culler = culler.Culler; Kind = kind; Owner = owner; Centre = centre; Radius = radius;
        }
        // The culler's id, an arrow and the part's id; for observation only.
        public string Id { get; }
        // The culler whose sphere the part left, and whose rule the own sphere follows.
        public string CullerId { get; }
        public object Culler { get; }
        public CullerKind Kind { get; }
        public object Owner { get; }
        public IReadOnlyList<double> Centre { get; }
        public double Radius { get; }
    }
    // An object whose own activity own culling sets: on while it is desired and a sphere it needs is visible.
    public sealed class PlannedNode {
        internal PlannedNode(object handle, bool desired, bool always, IEnumerable<string> need, string gate, IEnumerable<string> below) { Handle = handle; Desired = desired; Always = always; Need = need.ToArray(); Gate = gate; Below = below.ToArray(); }
        // The spheres of the moved parts below the object that follow another sphere than the object does. The object
        // stays on while one of them is visible, whatever its own spheres say.
        public IReadOnlyList<string> Below { get; }
        public object Handle { get; }
        public bool Desired { get; }
        public bool Always { get; }
        // Sphere ids: a simple culler's id for the game's sphere, a region id for an own sphere.
        public IReadOnlyList<string> Need { get; }
        // The own sphere that took the object over from a game-object culler, or null.
        public string Gate { get; }
    }
    // A simple culler whose visibility the plan needs. Replace: own culling sets the culler's object in the game's place;
    // otherwise the game keeps switching it and own culling only listens.
    public sealed class PlannedHook {
        internal PlannedHook(GroupCuller culler, bool replace) { CullerId = culler.Id; Culler = culler.Culler; Node = culler.Handle; Replace = replace; }
        public string CullerId { get; }
        public object Culler { get; }
        public object Node { get; }
        public bool Replace { get; }
    }
    // What a renderer or game-object culler hands to own spheres. Follows names, top-down, each moved part below a
    // renderer culler with the region its renderers follow, or null for the culler's own sphere; for a game-object
    // culler each listed object that an own sphere takes over.
    public sealed class PlannedHandOver {
        internal PlannedHandOver(GroupCuller culler, List<KeyValuePair<object, string>> follows) { CullerId = culler.Id; Culler = culler.Culler; Kind = culler.Kind; Follows = follows; }
        public string CullerId { get; }
        public object Culler { get; }
        public CullerKind Kind { get; }
        public IReadOnlyList<KeyValuePair<object, string>> Follows { get; }
    }
    // The own culling of one group: which moved parts get a sphere of their own, and which objects follow which spheres.
    // Every culler keeps the radius the scene gives it.
    public sealed class CullingPlan {
        // A part lies inside a sphere while every corner of its bounds is at most this far outside the radius.
        public const double Tolerance = 0.01;
        readonly List<PlannedRegion> regions = new List<PlannedRegion>();
        readonly List<PlannedNode> nodes = new List<PlannedNode>();
        readonly List<PlannedHook> hooks = new List<PlannedHook>();
        readonly List<PlannedHandOver> handOvers = new List<PlannedHandOver>();
        // A disabled group holds no own sphere.
        public bool Suspended { get; private set; }
        public IReadOnlyList<PlannedRegion> Regions => regions;
        public IReadOnlyList<PlannedNode> Nodes => nodes;
        public IReadOnlyList<PlannedHook> Hooks => hooks;
        public IReadOnlyList<PlannedHandOver> HandOvers => handOvers;
        public bool IsEmpty => regions.Count == 0 && nodes.Count == 0 && hooks.Count == 0 && handOvers.Count == 0;
        public static CullingPlan Empty => new CullingPlan();

        // Game: the game's own sphere of a simple culler.
        sealed class Sphere { internal string Id; internal IReadOnlyList<double> Centre; internal double Radius; internal object Node; internal GroupCuller Culler; internal bool Game; }
        static bool Encloses(Sphere sphere, IReadOnlyList<IReadOnlyList<double>> corners) {
            foreach (var corner in corners) {
                double sum = 0;
                for (var i = 0; i < 3; i++) sum += (corner[i] - sphere.Centre[i]) * (corner[i] - sphere.Centre[i]);
                if (Math.Sqrt(sum) > sphere.Radius + Tolerance) return false;
            }
            return true;
        }
        // moved: the parts whose placement differs from the placement the scene gives them. presence: the corners, in
        // map coordinates, of the renderer, collider and light bounds at and below an object, leaving out the named
        // objects and what stands below them: a moved part below another one has bounds of its own. Empty for an object
        // without presence of its own. anchor: an object's map position, which stands in for the bounds of a moved part
        // without presence. An unedited part is never in moved, so the culling of unedited content stays the game's.
        public static CullingPlan Compute(object group, GroupObservation observed, IEnumerable<object> moved, Func<object, IReadOnlyList<object>, IReadOnlyList<IReadOnlyList<double>>> presence, Func<object, IReadOnlyList<double>> anchor = null) {
            if (observed == null) throw new ArgumentNullException(nameof(observed));
            var plan = new CullingPlan();
            if (observed.Root.Active == false) { plan.Suspended = true; return plan; }
            var parts = new Dictionary<object, GroupPart>(ByIdentity.Instance);
            foreach (var part in observed.Parts) if (part.Handle != null) parts[part.Handle] = part;
            object Up(object node) => ReferenceEquals(node, group) ? null : parts[node].Parent != null && parts.ContainsKey(parts[node].Parent) ? parts[node].Parent : group;
            bool Self(object node) => ReferenceEquals(node, group) || parts[node].ActiveSelf;
            bool On(object node) { for (; node != null; node = Up(node)) if (!Self(node)) return false; return true; }
            int Depth(object node) { var depth = 0; for (; !ReferenceEquals(node, group); node = Up(node)) depth++; return depth; }
            bool AtOrBelow(object node, object above) { for (; node != null; node = Up(node)) if (ReferenceEquals(node, above)) return true; return false; }
            var edited = (moved ?? Enumerable.Empty<object>()).Where(part => part != null && parts.ContainsKey(part)).Distinct(ByIdentity.Instance).OrderBy(Depth).ToList();
            if (edited.Count == 0) return plan;
            // A disabled part, or a part below one, shows nowhere and gets no sphere.
            var held = edited.Where(On).ToList();
            var cache = new Dictionary<object, IReadOnlyList<IReadOnlyList<double>>>(ByIdentity.Instance);
            // What the object shows of its own, outside the moved parts below it; empty when it shows nothing.
            IReadOnlyList<IReadOnlyList<double>> Own(object node) {
                if (!cache.TryGetValue(node, out var corners)) cache[node] = corners = presence?.Invoke(node, edited.Where(part => !ReferenceEquals(part, node) && AtOrBelow(part, node)).ToArray()) ?? new IReadOnlyList<double>[0];
                return corners;
            }
            // The bounds a sphere must enclose: the object's own presence, or its anchor for an object without one.
            IReadOnlyList<IReadOnlyList<double>> Corners(object node) {
                var corners = Own(node);
                if (corners.Count != 0 || anchor == null) return corners;
                var at = anchor(node);
                return at == null ? corners : new[] { at };
            }
            var cullers = observed.Cullers.Where(c => c.Handle != null && c.Culler != null && (c.Authored ?? c.Radius) != null && (ReferenceEquals(c.Handle, group) || parts.ContainsKey(c.Handle))).ToList();
            Sphere Game(GroupCuller culler) => new Sphere { Id = culler.Id, Centre = culler.Expected, Radius = (culler.Authored ?? culler.Radius).Value, Node = culler.Handle, Culler = culler, Game = culler.Kind == CullerKind.Simple };
            Sphere Region(GroupCuller culler, CullerKind kind, object owner) {
                var corners = Corners(owner);
                var centre = new double[3];
                for (var i = 0; i < 3; i++) centre[i] = (corners.Min(c => c[i]) + corners.Max(c => c[i])) / 2;
                var radius = corners.Max(c => Math.Sqrt((c[0] - centre[0]) * (c[0] - centre[0]) + (c[1] - centre[1]) * (c[1] - centre[1]) + (c[2] - centre[2]) * (c[2] - centre[2])));
                var region = new PlannedRegion(culler.Id + "→" + parts[owner].Id, culler, kind, owner, centre, radius);
                plan.regions.Add(region);
                return new Sphere { Id = region.Id, Centre = centre, Radius = radius, Node = owner, Culler = culler };
            }

            Sphere Fixed(GroupCuller culler, object owner) {
                var region = new PlannedRegion(culler.Id + "→" + parts[owner].Id, culler, CullerKind.GameObject, owner, culler.Expected.ToArray(), (culler.Authored ?? culler.Radius).Value);
                plan.regions.Add(region);
                return new Sphere { Id = region.Id, Centre = region.Centre, Radius = region.Radius, Node = owner, Culler = culler };
            }
            var escaped = new HashSet<object>(ByIdentity.Instance);
            var handed = new Dictionary<GroupCuller, List<KeyValuePair<object, string>>>();
            List<KeyValuePair<object, string>> Follows(GroupCuller culler) { if (!handed.TryGetValue(culler, out var list)) handed[culler] = list = new List<KeyValuePair<object, string>>(); return list; }

            // A game-object culler switches the objects it lists. A listed object that a move separates from the
            // culler's sphere, by moving the object or the culler, gets a sphere of its own in the culler's place.
            var gates = new Dictionary<object, string>(ByIdentity.Instance);
            var gateSpheres = new Dictionary<object, Sphere>(ByIdentity.Instance);
            var listedBy = new Dictionary<object, GroupCuller>(ByIdentity.Instance);
            bool Moved(object node) => edited.Any(part => AtOrBelow(node, part));
            foreach (var culler in cullers.Where(c => c.Kind == CullerKind.GameObject)) {
                foreach (var listed in culler.Listed) {
                    if (listed == null || !parts.ContainsKey(listed) || listedBy.ContainsKey(listed)) continue;
                    listedBy[listed] = culler;
                    if (!On(listed)) continue;
                    if (!Moved(listed) && (ReferenceEquals(culler.Handle, group) || !Moved(culler.Handle))) continue;
                    var corners = Corners(listed);
                    if (corners.Count == 0 || Encloses(Game(culler), corners)) continue;
                    var region = Region(culler, CullerKind.GameObject, listed);
                    gates[listed] = region.Id; gateSpheres[listed] = region; escaped.Add(listed);
                    Follows(culler).Add(new KeyValuePair<object, string>(listed, region.Id));
                }
            }
            // The culler's own sphere, as a listed object that stays inside it follows it.
            Sphere StandIn(object listed) { var culler = listedBy[listed]; return new Sphere { Id = culler.Id + "→" + parts[listed].Id, Centre = culler.Expected, Radius = (culler.Authored ?? culler.Radius).Value, Node = listed, Culler = culler }; }

            // Simple cullers switch whole objects, and so does a game-object culler for each object it lists. A moved
            // part follows the first sphere above it that encloses it:
            // the nearest one as the scene has it, a sphere further up through the objects between, or a sphere of its
            // own when none encloses it. A part with a simple culler of its own keeps that culler's sphere.
            var simpleAt = new Dictionary<object, GroupCuller>(ByIdentity.Instance);
            foreach (var culler in cullers.Where(c => c.Kind == CullerKind.Simple)) if (!simpleAt.ContainsKey(culler.Handle)) simpleAt[culler.Handle] = culler;
            var ownSimple = new Dictionary<object, Sphere>(ByIdentity.Instance);
            var driver = new Dictionary<object, string>(ByIdentity.Instance);
            var extra = new Dictionary<object, List<string>>(ByIdentity.Instance);
            foreach (var part in held) {
                var corners = Corners(part);
                if (corners.Count == 0) continue;
                var chain = new List<Sphere>();
                for (var above = Up(part); above != null; above = Up(above)) {
                    if (ownSimple.TryGetValue(above, out var region)) chain.Add(region);
                    if (gateSpheres.TryGetValue(above, out var gate)) chain.Add(gate);
                    else if (listedBy.ContainsKey(above)) chain.Add(StandIn(above));
                    if (simpleAt.TryGetValue(above, out var culler)) chain.Add(Game(culler));
                }
                if (chain.Count == 0) continue;
                var follows = chain.FindIndex(sphere => Encloses(sphere, corners));
                // A sphere that encloses the part does not help while a simple culler further up, whose sphere the part
                // left, hides everything below it: the part then needs a sphere of its own for that culler.
                int first = -1, outer = -1;
                // The own sphere of a moved part above already stands in for the cullers that part left.
                for (var later = follows + 1; follows >= 0 && !ownSimple.ContainsKey(chain[follows].Node) && later < chain.Count; later++)
                    if (chain[later].Game && !Encloses(chain[later], corners)) { if (first < 0) first = later; outer = later; }
                if (follows == 0 && first < 0) continue;
                escaped.Add(part);
                string id;
                if (simpleAt.TryGetValue(part, out var own)) id = own.Id;
                else if (follows < 0 || first >= 0) {
                    var left = chain[follows < 0 ? 0 : first].Culler;
                    var region = Region(left, left.Kind == CullerKind.GameObject ? CullerKind.GameObject : CullerKind.Simple, part); ownSimple[part] = region; id = region.Id;
                }
                else id = chain[follows].Id;
                driver[part] = id;
                // The objects above the part stay on for it: up to the sphere it follows, or up to and with the object
                // of the outermost simple culler it left.
                var home = follows < 0 || first >= 0 ? null : chain[follows].Node;
                var last = first >= 0 ? chain[outer].Node : null;
                for (var above = Up(part); above != null && !ReferenceEquals(above, home); above = Up(above)) {
                    if (!extra.TryGetValue(above, out var list)) extra[above] = list = new List<string>();
                    if (!list.Contains(id)) list.Add(id);
                    if (ReferenceEquals(above, last)) break;
                }
            }

            // A listed object that did not leave its culler's sphere, with a moved part below it that follows another
            // sphere, or that itself follows another simple sphere, is handed over like a container: a stand-in for the
            // culler's sphere takes it, so the object can stay on for the moved part while the culler would hide it.
            // A listed object that shows nothing of its own, such as the outer object of a crossing mast, needs no
            // stand-in while every moved part below it follows an own sphere: the first of those spheres takes it, and
            // it stays on while any of them shows, so no sphere stays in the culler's place. A moved part that follows
            // the game's sphere of a simple culler, or another stand-in, keeps the stand-in, as the object must then
            // leave its culler's list for a sphere of its own.
            // The own spheres of moved parts, read before any stand-in is planned, so the order of the listed objects
            // does not decide whether a stand-in counts as one.
            var owned = new HashSet<string>(plan.regions.Select(r => r.Id), StringComparer.Ordinal);
            var containers = new HashSet<object>(ByIdentity.Instance);
            foreach (var listed in listedBy.Keys.Where(l => !gates.ContainsKey(l) && (extra.ContainsKey(l) || driver.ContainsKey(l))).ToList()) {
                if (!driver.ContainsKey(listed) && Own(listed).Count == 0 && extra[listed].All(owned.Contains)) {
                    gates[listed] = extra[listed][0];
                    Follows(listedBy[listed]).Add(new KeyValuePair<object, string>(listed, gates[listed]));
                    continue;
                }
                var standIn = Fixed(listedBy[listed], listed);
                Follows(listedBy[listed]).Add(new KeyValuePair<object, string>(listed, standIn.Id));
                if (driver.ContainsKey(listed)) gates[listed] = standIn.Id; else containers.Add(listed);
            }

            // A renderer culler switches the renderers below its object. The renderers of a moved part follow the first
            // sphere that encloses the part: an own sphere of a moved part above it, the culler's, or a sphere of its own.
            // A renderer culler on the moved part itself moves with it and keeps its renderers.
            foreach (var culler in cullers.Where(c => c.Kind == CullerKind.Renderer)) {
                var regions = new Dictionary<object, Sphere>(ByIdentity.Instance);
                foreach (var part in held.Where(p => !ReferenceEquals(p, culler.Handle) && AtOrBelow(p, culler.Handle))) {
                    var corners = Corners(part);
                    if (corners.Count == 0) continue;
                    var chain = new List<Sphere>();
                    for (var above = Up(part); !ReferenceEquals(above, culler.Handle); above = Up(above)) if (regions.TryGetValue(above, out var region)) chain.Add(region);
                    chain.Add(Game(culler));
                    var follows = chain.FindIndex(sphere => Encloses(sphere, corners));
                    if (follows < 0) { regions[part] = Region(culler, CullerKind.Renderer, part); escaped.Add(part); Follows(culler).Add(new KeyValuePair<object, string>(part, regions[part].Id)); }
                    else Follows(culler).Add(new KeyValuePair<object, string>(part, follows == chain.Count - 1 ? null : chain[follows].Id));
                }
            }

            foreach (var pair in handed.Where(p => p.Value.Any(f => f.Value != null))) plan.handOvers.Add(new PlannedHandOver(pair.Key, pair.Value));

            // The spheres an object needs: the sphere it follows, or none for an object above every simple culler.
            // Below holds the sphere of every moved part below it that follows another one.
            string Key(object node) => driver.TryGetValue(node, out var id) ? id : simpleAt.TryGetValue(node, out var culler) ? culler.Id : listedBy.ContainsKey(node) && !gates.ContainsKey(node) ? StandIn(node).Id : null;
            List<string> Need(object node) {
                string follows = null;
                for (var at = node; at != null && follows == null; at = Up(at)) follows = Key(at);
                return follows == null ? null : new List<string> { follows };
            }
            List<string> Below(object node) { var need = Need(node); return extra.TryGetValue(node, out var more) ? more.Where(id => need == null || !need.Contains(id)).ToList() : new List<string>(); }
            bool SameNeed(List<string> a, List<string> b) => a == null || b == null ? a == b : a.Count == b.Count && a.All(b.Contains);
            var spine = new HashSet<object>(extra.Keys, ByIdentity.Instance);
            var written = new HashSet<object>(ByIdentity.Instance);
            foreach (var node in new[] { group }.Concat(observed.Parts.Select(p => p.Handle).Where(h => h != null))) {
                var above = Up(node);
                if (!spine.Contains(node) && !driver.ContainsKey(node) && !gates.ContainsKey(node) && (above == null || !spine.Contains(above))) continue;
                var need = Need(node); var below = Below(node);
                gates.TryGetValue(node, out var gate);
                // An object that a game-object culler lists and keeps stays that culler's to switch.
                if (gate == null && listedBy.ContainsKey(node) && !containers.Contains(node)) continue;
                // An object that needs what the object above it needs follows it without being set.
                // So does an object above every simple culler, which nothing hides.
                if (gate == null && (need == null || (SameNeed(need, above == null ? null : Need(above)) && SameNeed(below, above == null ? new List<string>() : Below(above))))) continue;
                // A simple culler that needs only its own sphere stays the game's to switch.
                if (gate == null && below.Count == 0 && simpleAt.TryGetValue(node, out var own) && own.Id == need[0]) continue;
                plan.nodes.Add(new PlannedNode(node, Self(node), need == null, need ?? new List<string>(), gate, below));
                written.Add(node);
            }
            foreach (var culler in simpleAt.Values.Where(c => plan.nodes.Any(n => n.Need.Contains(c.Id) || n.Below.Contains(c.Id)))) plan.hooks.Add(new PlannedHook(culler, written.Contains(culler.Handle)));
            return plan;
        }
    }

    // One own sphere as it stands now.
    public sealed class ObservedRegion {
        public ObservedRegion(string id, object culler, object owner, IEnumerable<double> centre, double radius, bool pending, bool shows) {
            Id = id; Culler = culler; Owner = owner; Centre = centre?.ToArray(); Radius = radius; Pending = pending; Shows = shows;
        }
        public string Id { get; }
        public object Culler { get; }
        public object Owner { get; }
        // The centre in map coordinates, or null while the sphere is not registered.
        public IReadOnlyList<double> Centre { get; }
        public double Radius { get; }
        // Waiting for the game's culler to start; the next update registers it.
        public bool Pending { get; }
        public bool Shows { get; }
    }

    // The game-facing operations of own culling. Positions are map coordinates.
    public interface ICullingHost {
        // False while the map unloads: nothing registers, moves, releases or reads a sphere then.
        bool Available { get; }
        // Whether an object, a culler or a renderer still exists.
        bool Alive(object target);
        // Whether the game's culler has built what it culls with: a simple culler its culling group, a renderer culler
        // its renderer list.
        bool Started(object culler);
        // Whether a simple culler's own sphere is visible.
        bool Visible(object simpleCuller);
        // Listens to a simple culler's sphere. With replace the game's own handler no longer runs; without it the
        // game's handler runs first. changed returns false once own culling no longer owns the hook: the game's
        // handler then runs as if the hook were not there. Returns what Unhook takes.
        object Hook(object simpleCuller, bool replace, Func<bool> changed);
        // Puts the game's handler back. False when another handler took its place, so the hook stays as a pass-through.
        bool Unhook(object simpleCuller, object hook);
        // Registers a sphere with the culling manager of the culler's kind and name; changed reports whether the
        // sphere shows what it culls, by the culler's rule. Null when the manager is not available yet.
        object Register(object culler, CullerKind kind, object owner, IReadOnlyList<double> centre, double radius, Action<bool> changed);
        // Whether the registration still stands in an existing culling manager.
        bool Live(object registration);
        void Move(object registration, IReadOnlyList<double> centre, double radius);
        void Release(object registration);
        // Centre and radius of the registered sphere, as x, y, z, radius.
        IReadOnlyList<double> Sphere(object registration);
        void SetActive(object node, bool active);
        // The renderers a renderer culler switches, and whether its state shows them.
        IReadOnlyList<object> Renderers(object rendererCuller);
        void SetRenderers(object rendererCuller, IReadOnlyList<object> renderers);
        bool Shows(object culler);
        void SetEnabled(object renderer, bool enabled);
        // Whether the renderer stands at or below the object.
        bool Under(object renderer, object node);
        // Takes the object out of a game-object culler's list and returns its index, or -1 when the list does not hold it.
        int Unlist(object gameObjectCuller, object node);
        // Puts the object back at the index, clamped to the list, unless the list already holds it.
        void Relist(object gameObjectCuller, object node, int index);
    }

    // Own culling at run time: registers the planned spheres, sets the planned objects from their visibility, and hands
    // renderers and listed objects over and back. It owns only what it registered or took over, and returns it all.
    public sealed class PartCullingController {
        readonly ICullingHost host;
        sealed class RegionState { internal PlannedRegion Plan; internal object Registration; internal bool Shows = true, Released; internal List<object> Renderers = new List<object>(); }
        sealed class NodeState { internal PlannedNode Plan; internal bool Desired; }
        sealed class HookState { internal PlannedHook Plan; internal object Token; internal bool Removed; }
        sealed class RendererState { internal object Culler; internal List<object> Original; internal Dictionary<object, string> Holder = new Dictionary<object, string>(ByIdentity.Instance); }
        sealed class ListedState { internal object Culler, Node; internal int Index; internal string Region; }
        sealed class GroupState {
            internal object Group; internal CullingPlan Plan = CullingPlan.Empty; internal bool Dirty, AwaitRegister;
            // The game cullers that had not started at the last reconcile; the group waits for one of them.
            internal readonly List<object> Awaiting = new List<object>();
            internal bool Pending => AwaitRegister || Awaiting.Count != 0;
            internal readonly Dictionary<string, RegionState> Regions = new Dictionary<string, RegionState>(StringComparer.Ordinal);
            internal readonly Dictionary<object, NodeState> Nodes = new Dictionary<object, NodeState>(ByIdentity.Instance);
            internal readonly Dictionary<string, HookState> Hooks = new Dictionary<string, HookState>(StringComparer.Ordinal);
            internal readonly Dictionary<object, RendererState> Renderers = new Dictionary<object, RendererState>(ByIdentity.Instance);
            internal readonly List<ListedState> Listed = new List<ListedState>();
            internal bool Empty => Regions.Count == 0 && Nodes.Count == 0 && Hooks.Count == 0 && Renderers.Count == 0 && Listed.Count == 0;
        }
        readonly Dictionary<object, GroupState> groups = new Dictionary<object, GroupState>(ByIdentity.Instance);
        readonly List<string> leftovers = new List<string>();
        readonly HashSet<string> faults = new HashSet<string>(StringComparer.Ordinal);
        public PartCullingController(ICullingHost host) { this.host = host ?? throw new ArgumentNullException(nameof(host)); }
        // Receives each distinct failure of a callback once.
        public Action<string> Fault;
        // The simple cullers whose handler could not be put back at release, so the hook stays as a pass-through. For
        // the inventory diagnostics.
        public IReadOnlyList<string> Leftovers => leftovers;
        int reportedLeftovers;
        // Reports each such handler once as a debug line: it only passes the game's events on, so it needs no action
        // and is no warning.
        public void Report() {
            for (; reportedLeftovers < leftovers.Count; reportedLeftovers++) { var leftover = leftovers[reportedLeftovers]; PatcherLog.Debug(() => BakedSceneryRules.LeftoverWrapper(leftover)); }
        }
        public int RegionCount => groups.Values.Sum(g => g.Regions.Count);
        // Whether a load-order retry or a failed callback waits for the next Tick.
        public bool Waiting => groups.Values.Any(g => g.Pending || g.Dirty);

        // Brings the group's own culling to the plan. Does nothing while the map unloads.
        public void Apply(object group, CullingPlan plan) {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!host.Available) return;
            if (!groups.TryGetValue(group, out var state)) { if (plan.IsEmpty) return; groups[group] = state = new GroupState { Group = group }; }
            state.Plan = plan;
            Reconcile(state);
            if (state.Empty) groups.Remove(group);
        }
        // Installs what waited for a game culler to start, repairs after a failed callback, and lets go of groups that
        // no longer exist. Called every frame.
        public void Tick() {
            if (!host.Available || groups.Count == 0) return;
            foreach (var state in groups.Values.ToList()) {
                try {
                    if (!host.Alive(state.Group)) {
                        foreach (var region in state.Regions.Values) { region.Released = true; if (region.Registration != null && host.Live(region.Registration)) host.Release(region.Registration); }
                        foreach (var hook in state.Hooks.Values) hook.Removed = true;
                        groups.Remove(state.Group);
                        continue;
                    }
                    // A waiting group costs a few checks per frame: it is reconciled only once a culler it waits for has
                    // started, or a sphere that found no culling manager can be registered.
                    if (!state.Dirty && !state.Awaiting.Any(culler => !host.Alive(culler) || host.Started(culler)) && !(state.AwaitRegister && Probe(state))) continue;
                    Reconcile(state);
                    if (state.Empty) groups.Remove(state.Group);
                } catch (Exception e) { state.Dirty = true; UpdateFailed(state.Group, e); }
            }
        }
        // Names a group in a warning, such as its scene and path; without it a warning names no group.
        public Func<object, string> GroupName { get; set; }
        // An update runs every frame: a failure is written once per group and failure, and the other groups still update.
        readonly LogOnce updateFailures = new LogOnce();
        void UpdateFailed(object group, Exception error) {
            string name = null;
            try { name = GroupName?.Invoke(group); } catch { }
            updateFailures.Warning((name ?? "") + "\n" + error.GetBaseException().Message, BakedSceneryRules.CullingUpdateFailure(name), error);
        }
        // The patcher's state ends while the scene stays: every group gives back what own culling took over, as if
        // its plan were empty. While the map unloads there is nothing to give back.
        public void ReleaseAll() {
            if (!host.Available) { Unload(); return; }
            foreach (var state in groups.Values.ToList()) {
                try {
                    state.Plan = CullingPlan.Empty;
                    if (host.Alive(state.Group)) Reconcile(state);
                    else foreach (var region in state.Regions.Values) if (region.Registration != null && host.Live(region.Registration)) host.Release(region.Registration);
                } catch (Exception e) { Failed(state, e); }
                foreach (var region in state.Regions.Values) region.Released = true;
                foreach (var hook in state.Hooks.Values) hook.Removed = true;
            }
            groups.Clear(); leftovers.Clear(); reportedLeftovers = 0;
        }
        // The map unloads: forgets everything without touching a culling manager, a culler or an object.
        public void Unload() {
            foreach (var state in groups.Values) {
                foreach (var region in state.Regions.Values) region.Released = true;
                foreach (var hook in state.Hooks.Values) hook.Removed = true;
            }
            groups.Clear(); leftovers.Clear(); reportedLeftovers = 0;
        }
        NodeState NodeOf(object node) {
            if (node == null) return null;
            foreach (var state in groups.Values) if (state.Nodes.TryGetValue(node, out var found)) return found;
            return null;
        }
        // The own activity the modder or the scene set for an object own culling switches, or null for any other object.
        public bool? Desired(object node) => NodeOf(node)?.Desired;
        // Sets the desired activity of an object own culling switches. False for any other object.
        public bool SetDesired(object node, bool desired) {
            foreach (var state in groups.Values)
                if (state.Nodes.TryGetValue(node, out var found)) { found.Desired = desired; Write(state, found); return true; }
            return false;
        }
        // The objects own culling took out of the game-object culler's list.
        public IReadOnlyList<object> HandedOver(object culler) => groups.Values.SelectMany(g => g.Listed).Where(l => ReferenceEquals(l.Culler, culler)).Select(l => l.Node).ToArray();
        // Every object taken out of a list, with its culler and whether its own sphere shows it now.
        public IReadOnlyList<Tuple<object, object, bool>> HandedOver() =>
            groups.Values.SelectMany(g => g.Listed.Select(l => Tuple.Create(l.Node, l.Culler, !g.Regions.TryGetValue(l.Region, out var region) || region.Shows))).ToArray();
        public IReadOnlyList<ObservedRegion> Regions(object group) {
            if (group == null || !groups.TryGetValue(group, out var state)) return new ObservedRegion[0];
            return state.Regions.Values.Select(region => {
                var sphere = host.Available && region.Registration != null && host.Live(region.Registration) ? host.Sphere(region.Registration) : null;
                return new ObservedRegion(region.Plan.Id, region.Plan.Culler, region.Plan.Owner, sphere?.Take(3), sphere == null ? region.Plan.Radius : sphere[3], region.Registration == null, region.Shows);
            }).ToArray();
        }

        bool Visible(GroupState state, string id) {
            if (state.Regions.TryGetValue(id, out var region)) return region.Shows;
            var hook = state.Plan.Hooks.FirstOrDefault(h => h.CullerId == id);
            // A culler that has not started has culled nothing yet.
            return hook == null || !host.Alive(hook.Culler) || !host.Started(hook.Culler) || host.Visible(hook.Culler);
        }
        void Write(GroupState state, NodeState node) {
            if (!host.Alive(node.Plan.Handle)) return;
            host.SetActive(node.Plan.Handle, CullingActivity.Active(node.Desired, node.Plan.Always, node.Plan.Need.Select(id => Visible(state, id)), node.Plan.Gate == null || Visible(state, node.Plan.Gate), node.Plan.Below.Select(id => Visible(state, id))));
        }
        void Failed(GroupState state, Exception error) {
            state.Dirty = true;
            var message = "Moved scenery parts may appear or disappear at the wrong distance because the patcher couldn't respond to a visibility change: " + error.GetBaseException().Message.TrimEnd('.') + ". Report this to the patcher maintainer.";
            if (faults.Add(message)) Fault?.Invoke(message);
        }
        // A sphere changed. A released registration, a destroyed manager, part or culler writes nothing.
        void Changed(GroupState state, RegionState region, bool shows) {
            try {
                if (region.Released || region.Registration == null || !ReferenceEquals(groups.TryGetValue(state.Group, out var current) ? current : null, state)) return;
                if (!host.Live(region.Registration) || !host.Alive(region.Plan.Owner) || !host.Alive(region.Plan.Culler)) return;
                region.Shows = shows;
                foreach (var renderer in region.Renderers) if (host.Alive(renderer)) host.SetEnabled(renderer, shows);
                foreach (var node in state.Nodes.Values) if (node.Plan.Gate == region.Plan.Id || node.Plan.Need.Contains(region.Plan.Id) || node.Plan.Below.Contains(region.Plan.Id)) Write(state, node);
            } catch (Exception e) { Failed(state, e); }
        }
        // False once the controller no longer owns the hook, so the game's own handler runs.
        bool Hooked(GroupState state, HookState hook) {
            try {
                if (hook.Removed || !ReferenceEquals(groups.TryGetValue(state.Group, out var current) ? current : null, state)) return false;
                if (!host.Alive(hook.Plan.Culler)) return true;
                foreach (var node in state.Nodes.Values) if (node.Plan.Need.Contains(hook.Plan.CullerId) || node.Plan.Below.Contains(hook.Plan.CullerId)) Write(state, node);
            } catch (Exception e) { Failed(state, e); }
            return true;
        }
        // Registers the region's sphere. False when the culling manager takes none yet.
        bool Register(GroupState state, RegionState region) {
            var planned = region.Plan;
            region.Shows = true;
            region.Registration = host.Register(planned.Culler, planned.Kind, planned.Owner, planned.Centre, planned.Radius, shows => Changed(state, region, shows));
            return region.Registration != null;
        }
        // Whether a sphere that found no culling manager can be registered now.
        bool Probe(GroupState state) {
            foreach (var region in state.Regions.Values)
                if (region.Registration == null && host.Alive(region.Plan.Culler) && (region.Plan.Kind != CullerKind.Renderer || host.Started(region.Plan.Culler)) && Register(state, region)) return true;
            return false;
        }

        void Reconcile(GroupState state) {
            state.Awaiting.Clear(); state.AwaitRegister = false; state.Dirty = false;
            var plan = state.Plan;
            void Await(object culler) { if (!state.Awaiting.Any(other => ReferenceEquals(other, culler))) state.Awaiting.Add(culler); }
            // New and moved spheres first, so nothing is handed to a sphere that is not there.
            foreach (var planned in plan.Regions) {
                if (!state.Regions.TryGetValue(planned.Id, out var region)) state.Regions[planned.Id] = region = new RegionState();
                region.Plan = planned;
                if (region.Registration != null && host.Live(region.Registration)) {
                    host.Move(region.Registration, planned.Centre, planned.Radius);
                    // A sphere its manager no longer holds is registered again below.
                    if (host.Live(region.Registration)) continue;
                }
                region.Registration = null; region.Shows = true;
                if (!host.Alive(planned.Culler)) continue;
                // A renderer culler builds its renderer list when it wakes; until then there is nothing to hand over.
                if (planned.Kind == CullerKind.Renderer && !host.Started(planned.Culler)) { Await(planned.Culler); continue; }
                if (!Register(state, region)) state.AwaitRegister = true;
            }
            bool Registered(string id) => id != null && state.Regions.TryGetValue(id, out var region) && region.Registration != null && plan.Regions.Any(r => r.Id == id);

            // Renderers follow their spheres; a culler without hand-over takes all of its renderers back.
            foreach (var handOver in plan.HandOvers.Where(h => h.Kind == CullerKind.Renderer)) {
                if (!host.Alive(handOver.Culler)) continue;
                if (!host.Started(handOver.Culler)) { Await(handOver.Culler); continue; }
                if (!state.Renderers.TryGetValue(handOver.Culler, out var renderers)) state.Renderers[handOver.Culler] = renderers = new RendererState { Culler = handOver.Culler, Original = host.Renderers(handOver.Culler).ToList() };
                Assign(state, renderers, handOver.Follows, Registered);
            }
            foreach (var renderers in state.Renderers.Values.Where(r => !plan.HandOvers.Any(h => ReferenceEquals(h.Culler, r.Culler))).ToList()) {
                if (host.Alive(renderers.Culler)) Assign(state, renderers, new KeyValuePair<object, string>[0], Registered);
                state.Renderers.Remove(renderers.Culler);
            }

            // Listed objects: back into the list of their culler with its current visibility, or out of it. Only an
            // object that this controller took out of a list is ever put back.
            var returned = new HashSet<object>(ByIdentity.Instance);
            foreach (var listed in state.Listed.Where(l => !plan.HandOvers.Any(h => ReferenceEquals(h.Culler, l.Culler) && h.Follows.Any(f => ReferenceEquals(f.Key, l.Node) && f.Value != null))).ToList()) {
                if (host.Alive(listed.Node) && host.Alive(listed.Culler)) {
                    var desired = !state.Nodes.TryGetValue(listed.Node, out var node) || node.Desired;
                    host.SetActive(listed.Node, desired && host.Shows(listed.Culler));
                    host.Relist(listed.Culler, listed.Node, listed.Index);
                }
                returned.Add(listed.Node);
                state.Listed.Remove(listed);
            }
            foreach (var handOver in plan.HandOvers.Where(h => h.Kind == CullerKind.GameObject && host.Alive(h.Culler)))
                foreach (var follow in handOver.Follows.Where(f => f.Value != null && Registered(f.Value))) {
                    // An object already taken out follows the sphere the plan names now, such as the other moved part's
                    // when one of two parts below an empty object returns.
                    var held = state.Listed.FirstOrDefault(l => ReferenceEquals(l.Node, follow.Key));
                    if (held != null) { held.Region = follow.Value; continue; }
                    var index = host.Unlist(handOver.Culler, follow.Key);
                    if (index >= 0) state.Listed.Add(new ListedState { Culler = handOver.Culler, Node = follow.Key, Index = index, Region = follow.Value });
                }

            // Hooks on the simple cullers whose visibility the plan needs.
            var freed = new Dictionary<object, object>(ByIdentity.Instance);
            foreach (var hook in state.Hooks.Values.ToList()) {
                var wanted = plan.Hooks.FirstOrDefault(h => h.CullerId == hook.Plan.CullerId);
                if (wanted != null && wanted.Replace == hook.Plan.Replace && ReferenceEquals(wanted.Culler, hook.Plan.Culler)) { hook.Plan = wanted; continue; }
                hook.Removed = true;
                state.Hooks.Remove(hook.Plan.CullerId);
                if (!host.Alive(hook.Plan.Culler)) continue;
                if (!host.Unhook(hook.Plan.Culler, hook.Token) && !leftovers.Contains(hook.Plan.CullerId)) leftovers.Add(hook.Plan.CullerId);
                if (hook.Plan.Replace) freed[hook.Plan.Node] = hook.Plan.Culler;
            }
            foreach (var wanted in plan.Hooks) {
                if (state.Hooks.ContainsKey(wanted.CullerId) || !host.Alive(wanted.Culler)) continue;
                // Before the game's culler starts there is nothing to hook; the first Tick after it starts hooks it.
                if (!host.Started(wanted.Culler)) { Await(wanted.Culler); continue; }
                var hook = new HookState { Plan = wanted };
                hook.Token = host.Hook(wanted.Culler, wanted.Replace, () => Hooked(state, hook));
                state.Hooks[wanted.CullerId] = hook;
            }

            // Objects that leave the plan return to their desired activity; a simple culler's object to its sphere.
            foreach (var node in state.Nodes.Values.Where(n => !plan.Nodes.Any(p => ReferenceEquals(p.Handle, n.Plan.Handle))).ToList()) {
                state.Nodes.Remove(node.Plan.Handle);
                if (returned.Contains(node.Plan.Handle) || !host.Alive(node.Plan.Handle)) continue;
                var visible = !freed.TryGetValue(node.Plan.Handle, out var culler) || !host.Started(culler) || host.Visible(culler);
                host.SetActive(node.Plan.Handle, node.Desired && visible);
            }
            foreach (var planned in plan.Nodes) {
                if (!state.Nodes.TryGetValue(planned.Handle, out var node)) state.Nodes[planned.Handle] = node = new NodeState();
                node.Plan = planned; node.Desired = planned.Desired;
                Write(state, node);
            }

            // Spheres that left the plan go last, after what they held went back.
            foreach (var region in state.Regions.Values.Where(r => !plan.Regions.Any(p => p.Id == r.Plan.Id)).ToList()) {
                region.Released = true;
                state.Regions.Remove(region.Plan.Id);
                if (region.Registration != null && host.Live(region.Registration)) host.Release(region.Registration);
            }
        }
        // Gives every renderer of the culler to the sphere it follows: the last moved part it stands under names it.
        void Assign(GroupState state, RendererState renderers, IReadOnlyList<KeyValuePair<object, string>> follows, Func<string, bool> registered) {
            var kept = new List<object>();
            var changed = false;
            foreach (var region in state.Regions.Values.Where(r => ReferenceEquals(r.Plan.Culler, renderers.Culler))) region.Renderers.Clear();
            foreach (var renderer in renderers.Original) {
                if (!host.Alive(renderer)) continue;
                string holder = null;
                foreach (var follow in follows) if (host.Under(renderer, follow.Key)) holder = follow.Value;
                if (!registered(holder)) holder = null;
                renderers.Holder.TryGetValue(renderer, out var before);
                if (holder == null) kept.Add(renderer); else state.Regions[holder].Renderers.Add(renderer);
                if (before == holder) continue;
                changed = true;
                host.SetEnabled(renderer, holder == null ? host.Shows(renderers.Culler) : state.Regions[holder].Shows);
                if (holder == null) renderers.Holder.Remove(renderer); else renderers.Holder[renderer] = holder;
            }
            if (changed) host.SetRenderers(renderers.Culler, kept);
        }
    }
}
