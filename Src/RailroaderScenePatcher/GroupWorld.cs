// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The native operations of the baked scenery group and part capabilities: the transform and activity of a group root
    // or a part, the game's cullers in the group and the physics state of its colliders. The rules live in Core's
    // BakedSceneryRules, BakedSceneryGroups and BakedSceneryParts.
    internal sealed class GroupWorld : IGroupWorld, IConstructionSegmentWorld, IDisposable {
        internal const string SimpleCullerType = "Helpers.SimpleCuller", GameObjectCullerType = "Helpers.Culling.GameObjectCuller";
        static readonly Type simple = Native.Type(SimpleCullerType), renderer = Native.Type(BoardScene.CullerType), switcher = Native.Type(GameObjectCullerType);
        static readonly FieldInfo cullingGroup = simple?.GetField("_cullingGroup", Native.Flags), cullingSpheres = simple?.GetField("_cullingSpheres", Native.Flags),
            rendererToken = renderer?.GetField("_cullingToken", Native.Flags), switcherToken = switcher?.GetField("_cullingToken", Native.Flags),
            managerSpheres = Native.Type("Helpers.Culling.CullingManager")?.GetField("_spheres", Native.Flags);
        static readonly MethodInfo simpleMoved = simple?.GetMethod("OnTransformDidChange", Native.Flags, null, Type.EmptyTypes, null);
        static readonly FieldInfo simpleRadius = simple?.GetField("radius", Native.Flags), rendererRadius = renderer?.GetField("radius", Native.Flags), switcherRadius = switcher?.GetField("radius", Native.Flags),
            switcherState = switcher?.GetField("_cullingState", Native.Flags);
        // A storage pile (Scenery.Pile, a Scenery.BulkScenery) lays its logs out in map space from where it stands when
        // it builds them, and registers a culling sphere of its own at its transform. Read lazily: a group without a
        // pile needs none of these, and a group with a pile is refused while one is missing (PileProblem). The logs of a
        // log pond (Scenery.LogPond) are a Scenery.BulkScenery too and take the same refresh; "pile" below covers them.
        static readonly Type bulk = Native.Type("Scenery.BulkScenery");
        static readonly FieldInfo bulkVisible = bulk?.GetField("_cullingVisible", Native.Flags), bulkToken = bulk?.GetField("_cullingToken", Native.Flags);
        static readonly MethodInfo bulkRebuild = bulk?.GetMethod("Rebuild", Native.Flags, null, Type.EmptyTypes, null), bulkRefresh = bulk?.GetMethod("RequestUpdateCullingPosition", Native.Flags, null, Type.EmptyTypes, null),
            bulkRadius = bulk?.GetMethod("GetCullingRadius", Native.Flags, null, Type.EmptyTypes, null);
        static string PileProblem(GameObject root) {
            var found = bulk == null ? new Component[0] : root.GetComponentsInChildren(bulk, true);
            if (found.Length == 0) return null;
            if (bulkVisible?.FieldType == typeof(bool) && bulkToken != null && bulkRebuild != null && bulkRefresh != null && bulkRadius != null) return null;
            var ponds = found.Any(c => c != null && c.GetType().FullName == BakedSceneryRules.LogPond);
            return BakedSceneryRules.BulkMembersRefusal(found.Any(c => c != null && c.GetType().FullName != BakedSceneryRules.LogPond), ponds);
        }
        // The lake surface (LakePolygon) at or below an object whose water mesh renderer is part of a static batch, by
        // its name, or null. The game draws a batched mesh where the scene had it, whatever its transform says, so such
        // a water surface would stay at its old place while every transform check passes.
        internal static string BatchedLake(GameObject go) {
            if (go == null) return null;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                if (renderer != null && renderer.isPartOfStaticBatch && renderer.GetComponents<Component>().Any(c => c != null && c.GetType().FullName == BakedSceneryRules.LakeSurface)) return renderer.name;
            return null;
        }
        // A group root or part whose lake surface is drawn from a static batch is not moved; its activity stays editable.
        public string PlacementRefusal(object target) {
            var water = BatchedLake(target as GameObject);
            return water == null ? null : BakedSceneryRules.StaticBatchRefusal(water);
        }
        // The collider a pile builds near the camera: its mesh is a new one at every rebuild, which the game does not
        // destroy; a rebuild the patcher starts destroys the mesh it replaced.
        static readonly FieldInfo pileCollider = Native.Type(BakedSceneryRules.StoragePile)?.GetField("_colliderObject", Native.Flags);
        // A log pond (Scenery.LogPond, also a Scenery.BulkScenery) builds no collider.
        static Mesh ColliderMesh(Component pile) {
            var holder = pileCollider != null && pileCollider.DeclaringType.IsInstanceOfType(pile) ? pileCollider.GetValue(pile) as GameObject : null;
            var collider = holder == null ? null : holder.GetComponent<MeshCollider>();
            return collider == null ? null : collider.sharedMesh;
        }
        // Where each pile stood, in map coordinates, when it last laid out its logs as far as this patcher knows: read
        // when its group is first observed, and after each refresh. Only a pile that stands elsewhere now is laid out
        // again, so a gizmo drag rebuilds the piles it moves and no other.
        static readonly Dictionary<Component, Placement> laidOut = new Dictionary<Component, Placement>();
        static Placement PileAt(Component pile) => new Placement(Coordinates(NativeInventory.MapPosition(pile.transform)), Coordinates(pile.transform.rotation));
        // Within a centimetre and a hundredth of a degree, as the group checks compare placements; a sphere within ten
        // centimetres, as they compare registrations.
        const double SameDistance = 0.01, SameAngle = 0.01, SphereDistance = 0.1;
        static double Distance(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
        static bool SamePile(Placement a, Placement b) =>
            Distance(a.Position, b.Position) <= SameDistance && Quaternion.Angle(new Quaternion((float)a.Rotation[0], (float)a.Rotation[1], (float)a.Rotation[2], (float)a.Rotation[3]),
                new Quaternion((float)b.Rotation[0], (float)b.Rotation[1], (float)b.Rotation[2], (float)b.Rotation[3])) <= SameAngle;
        static void RememberPiles(GameObject root) {
            if (bulk == null) return;
            foreach (var gone in laidOut.Keys.Where(pile => pile == null).ToList()) laidOut.Remove(gone);
            foreach (var pile in root.GetComponentsInChildren(bulk, true)) if (!laidOut.ContainsKey(pile)) laidOut[pile] = PileAt(pile);
        }
        // Own culling of moved parts: one controller for the loaded world, so the readers of the inventory and the
        // inspector see what it holds. A new world replaces it.
        static PartCullingHost host;
        static PartCullingController culling;
        internal GroupWorld() {
            if (simple == null || renderer == null || switcher == null) throw new InvalidOperationException("Culler types unavailable");
            if (cullingGroup?.FieldType != typeof(CullingGroup) || cullingSpheres?.FieldType != typeof(BoundingSphere[]) || simpleMoved == null || simpleRadius?.FieldType != typeof(float)) throw new MissingMemberException("Simple culler members unavailable");
            if (rendererToken == null || switcherToken == null || managerSpheres == null) throw new MissingMemberException("Culling registration members unavailable");
            if (renderer.GetMethod("RequestUpdateCullingPosition", Native.Flags) == null || switcher.GetMethod("RequestUpdateCullingPosition", Native.Flags) == null) throw new MissingMethodException("Culling refresh unavailable");
            if (rendererRadius?.FieldType != typeof(float) || switcherRadius?.FieldType != typeof(float) || switcherState == null) throw new MissingMemberException("Culling radius members unavailable");
            var token = rendererToken.FieldType;
            if (token.GetProperty("Index") == null || token.GetField("Manager") == null || token.GetProperty("IsRemoved") == null) throw new MissingMemberException("Culling token members unavailable");
            if (Native.Type("Helpers.WorldTransformer") == null) throw new InvalidOperationException("Floating origin unavailable");
            // The private culling fields that own culling reads and writes are checked here: a missing one fails the
            // adapter with its name, not a scene edit.
            var next = new PartCullingHost();
            // An earlier world's own culling goes back to the game before this one starts.
            culling?.ReleaseAll(); constructions?.ReleaseAll(); host?.Dispose();
            host = next; culling = new PartCullingController(host);
            constructionHost = new ConstructionHost();
            constructions = new ConstructionActivity(constructionHost, culling);
            own = culling; ownHost = host; ownConstructions = constructions;
            var current = culling; var shared = constructions; var stands = constructionHost;
            host.Unloaded = () => { current.Unload(); shared.Unload(); stands.Unload(); trestles.Clear(); };
            culling.Fault = message => Debug.LogWarning("Scene Patcher: " + message);
        }
        readonly PartCullingController own;
        readonly PartCullingHost ownHost;
        readonly ConstructionActivity ownConstructions;
        // The activity of the objects a rail bridge's construction controller switches, shared with that controller for an
        // explicit disable and for own culling. One for the loaded world, as own culling is.
        static ConstructionActivity constructions;
        static ConstructionHost constructionHost;
        // The patcher's state for this world ends. While the scene stays loaded, every handler, renderer list, object
        // list and activity that own culling took over goes back to the game, and its spheres are released.
        bool disposed;
        public void Dispose() {
            if (disposed) return;
            disposed = true;
            try { own.ReleaseAll(); } catch (Exception e) { Debug.LogWarning("Scene Patcher: own culling release failed: " + e.GetBaseException().Message); }
            try { ownConstructions.ReleaseAll(); } catch (Exception e) { Debug.LogWarning("Scene Patcher: construction activity release failed: " + e.GetBaseException().Message); }
            if (ReferenceEquals(constructions, ownConstructions)) { constructions = null; constructionHost = null; }
            ownHost.Dispose();
            if (ReferenceEquals(culling, own)) { culling = null; host = null; }
        }
        // Every frame: installs own culling that waited for a game culler to start.
        internal static void Tick() { try { culling?.Tick(); } catch (Exception e) { Debug.LogWarning("Scene Patcher: own culling update failed: " + e.GetBaseException().Message); } }
        // A world load started.
        internal static void WorldLoaded() => host?.WorldLoaded();
        // Reports what own culling could not take out again, each once, at information level.
        internal static void ReportCulling(ICullingLog log) => culling?.Report(log);
        // The objects own culling took out of a game-object culler's list, with that culler and whether the object's
        // own sphere shows it now.
        internal static IEnumerable<Tuple<GameObject, Component, bool>> HandedOver() =>
            culling == null ? new Tuple<GameObject, Component, bool>[0] : culling.HandedOver().Where(item => item.Item1 as GameObject != null && item.Item2 as Component != null).Select(item => Tuple.Create((GameObject)item.Item1, (Component)item.Item2, item.Item3)).ToArray();
        // The own culling spheres of the moved objects below a root, as they stand now, for an inspection.
        internal static IReadOnlyList<ObservedRegion> OwnSpheres(GameObject root) => culling == null || root == null ? new ObservedRegion[0] : culling.Regions(root);
        // The object's own activity as the scene or the modder set it: what own culling holds for an object it switches.
        internal static bool OwnActivity(GameObject go) => SharedEnabled(go) ?? CullingActivity.Desired(go.activeSelf, culling?.Desired(go));
        // A group root's or part's Enabled setting as the inventory shows it: for an object of a rail bridge construction
        // that a construction controller switches, whether no explicit disable holds it off; otherwise its authored activity.
        internal static bool Enabled(GameObject go) => SharedEnabled(go) ?? Authored(go);
        // The patch's Enabled setting of a rail bridge construction's root or part that a controller switches, or null
        // for any other object: the objects of other families that a controller names, such as Ela's, keep their readout.
        static bool? SharedEnabled(GameObject go) => go != null && constructions != null && (RailBridgeRoot(go) || RailBridgeRoot(GroupRootOf(go))) ? constructions.Enabled(go) : null;
        // Whether an explicit disable holds the object off against its construction controller, for the amber warning.
        internal static bool HeldAgainstConstruction(GameObject go) => go != null && constructions?.Held(go) == true;
        public IEnumerable<string> MapIds => ModScenePatchFiles.MapIds();

        // The rule's verdicts for every loaded World root. They are read again after Invalidate, which a whole discovery
        // calls, and when the loaded scenes, their World roots or the objects down to the group roots change; a change
        // deeper inside a group waits for the next whole discovery.
        static Dictionary<GameObject, GroupRootVerdict> verdicts;
        // The part rule's verdicts for the scene objects below the listed group roots, read with the group verdicts.
        static Dictionary<GameObject, PartVerdict> partVerdicts = new Dictionary<GameObject, PartVerdict>();
        static string verdictKey;
        static int verdictFrame = -1;
        // The transforms the game animates below the loaded World roots, read with the verdicts.
        static HashSet<Transform> driven = new HashSet<Transform>();
        internal static void Invalidate() { verdicts = null; verdictKey = null; }
        internal static IReadOnlyDictionary<GameObject, GroupRootVerdict> Verdicts() {
            // One check of the hierarchy per frame serves every question asked in it.
            if (verdicts != null && verdictFrame == Time.frameCount) return verdicts;
            verdictFrame = Time.frameCount;
            var worlds = new List<GameObject>();
            var key = new System.Text.StringBuilder();
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                key.Append(scene.handle).Append(':');
                foreach (var root in scene.GetRootGameObjects())
                    if (root != null && root.name == BakedSceneryRules.WorldRoot) { worlds.Add(root); Signature(root.transform, 0, key); }
                key.Append(';');
            }
            var text = key.ToString();
            if (verdicts != null && verdictKey == text) return verdicts;
            var found = new Dictionary<GameObject, GroupRootVerdict>();
            var parts = new Dictionary<GameObject, PartVerdict>();
            var drivenNow = new HashSet<Transform>();
            var togglesNow = new HashSet<GameObject>();
            foreach (var world in worlds) AddDriven(world.transform, drivenNow);
            foreach (var world in worlds) {
                var classified = BakedSceneryRules.Classify(new HierarchyNode(world.transform, drivenNow));
                foreach (var verdict in classified) found[((HierarchyNode)verdict.Node).Transform.gameObject] = verdict;
                // A group whose parts cannot be read keeps its root entry without parts; one developer diagnostic line
                // says so.
                foreach (var group in classified.Where(v => v.Disposition == GroupDisposition.Group)) {
                    var groupRoot = ((HierarchyNode)group.Node).Transform;
                    if (pickableToggle != null && groupRoot.GetComponentsInChildren(pickableToggle, true).Length != 0) togglesNow.Add(groupRoot.gameObject);
                    try { foreach (var part in BakedSceneryRules.Parts(new[] { group })) parts[((HierarchyNode)part.Node).Transform.gameObject] = part; }
                    catch (Exception e) { WorldSelectionOverlay.Skipped(((HierarchyNode)group.Node).Transform.gameObject, "its parts could not be read: " + e.GetBaseException().Message); }
                }
            }
            verdictKey = text; partVerdicts = parts; driven = drivenNow; toggleRoots = togglesNow;
            return verdicts = found;
        }
        // The listed group roots that hold a pickable toggle at any depth, read once with the verdicts: only their entries
        // read the toggles, key-value objects and name texts they own (NativeInventory.InteractiveBindings).
        static readonly Type pickableToggle = Native.Type(CommandBand.PickableToggle);
        static HashSet<GameObject> toggleRoots = new HashSet<GameObject>();
        internal static bool HoldsToggles(GameObject root) { if (root == null) return false; Verdicts(); return toggleRoots.Contains(root); }
        internal static PartVerdict PartVerdictOf(GameObject go) { if (go == null) return null; Verdicts(); return partVerdicts.TryGetValue(go, out var verdict) ? verdict : null; }
        // Whether the object is the root of a baked scenery group for world marking: a listed group root, or an
        // inspection-only one that the category table names. The inventory objects below it are the group's objects.
        internal static bool IsGroupRoot(GameObject go) {
            var verdict = VerdictOf(go);
            return verdict != null && (verdict.Disposition == GroupDisposition.Group || (verdict.Disposition == GroupDisposition.InspectionOnly && verdict.Category != null));
        }
        internal static bool IsPart(GameObject go) => PartVerdictOf(go)?.Disposition == PartDisposition.Part;
        // The listed parts of the listed group roots, for the inventory.
        internal static IEnumerable<GameObject> Parts() { Verdicts(); return partVerdicts.Where(pair => pair.Key != null && pair.Value.Disposition == PartDisposition.Part).Select(pair => pair.Key).ToArray(); }
        // The listed group root a part stands in, or null.
        internal static GameObject GroupRootOf(GameObject part) {
            if (part == null) return null;
            for (var t = part.transform.parent; t != null; t = t.parent) if (VerdictOf(t.gameObject)?.Disposition == GroupDisposition.Group) return t.gameObject;
            return null;
        }
        // The identity of the objects from World down to the group roots: World, its folders, the town folders below
        // World/Large Scenery and the candidates below them.
        // Objects below the candidates, such as the children of a crossing folder, are read again at the next whole discovery.
        static void Signature(Transform t, int depth, System.Text.StringBuilder key, int limit = 2) {
            key.Append(t.GetInstanceID()).Append(',');
            if (depth == 1 && t.name == BakedSceneryRules.LargeScenery) limit = 3;
            if (depth >= limit) return;
            for (var i = 0; i < t.childCount; i++) Signature(t.GetChild(i), depth + 1, key, limit);
            key.Append('/');
        }
        // One line per candidate of the rule and per scene object below a listed group root, for evidence that compares
        // the listed and unlisted group roots and parts.
        internal static IEnumerable<string> Report() =>
            Verdicts().Select(p => p.Key.scene.name + "/" + p.Value.Path + " => " + p.Value.Disposition + (p.Value.Reason == null ? "" : "; " + p.Value.Reason) + (p.Value.Ticket == null ? "" : " (" + p.Value.Ticket + ")") + (p.Value.Category == null ? "" : " [" + p.Value.Category + (p.Value.Subcategory == null ? "" : " / " + p.Value.Subcategory) + "]"))
                .Concat(partVerdicts.Where(p => p.Key != null).Select(p => p.Key.scene.name + "/" + p.Value.Path + " => " + (p.Value.Disposition == PartDisposition.Part ? "Part" : "Part " + p.Value.Disposition) + (p.Value.Reason == null ? "" : "; " + p.Value.Reason)))
                .OrderBy(line => line, StringComparer.Ordinal);
        internal static GroupRootVerdict VerdictOf(GameObject go) => go != null && Verdicts().TryGetValue(go, out var verdict) ? verdict : null;
        // The family an inspection-only candidate of the rule is listed under, or null when the rule lists none. A listed
        // part stands in the family of its group.
        internal static string FamilyOf(GameObject go) {
            var verdict = VerdictOf(go);
            if (verdict == null) return IsPart(go) ? BakedSceneryRules.Family : null;
            if (verdict.Disposition == GroupDisposition.Group) return BakedSceneryRules.Family;
            if (verdict.Disposition != GroupDisposition.InspectionOnly) return null;
            // A group the category table names stands in the family under its category, a crossbuck included.
            return verdict.Category == null && verdict.Ticket == BakedSceneryRules.CrossbuckTicket ? "Crossbuck" : BakedSceneryRules.Family;
        }
        // The rule's view of one loaded object; components and children are read once.
        sealed class HierarchyNode : IHierarchyNode {
            internal readonly Transform Transform;
            readonly HashSet<Transform> driven;
            string[] components; IHierarchyNode[] children; bool physical; bool? family, lod;
            // Read only for the objects below a listed group root.
            public bool LodLevel => lod ?? (lod = IsLodLevel(Transform)).Value;
            // Every renderer at and below the object is one a LOD group on an object above it switches.
            static bool IsLodLevel(Transform t) {
                LODGroup holder = null;
                for (var up = t.parent; up != null && holder == null; up = up.parent) holder = up.GetComponent<LODGroup>();
                if (holder == null) return false;
                var levels = new HashSet<Renderer>(holder.GetLODs().SelectMany(level => level.renderers ?? new Renderer[0]).Where(r => r != null));
                var own = t.GetComponentsInChildren<Renderer>(true).Where(r => r != null && BakedSceneryRules.GivesPresence(r.GetType().FullName, true, false, false)).ToArray();
                return own.Length != 0 && own.All(levels.Contains);
            }
            internal HierarchyNode(Transform transform, HashSet<Transform> driven) { Transform = transform; this.driven = driven; }
            public string Name => Transform.name;
            void Read() {
                if (components != null) return;
                var own = Transform.GetComponents<Component>();
                components = own.Where(c => c != null).Select(c => c.GetType().FullName).ToArray();
                // A visual effect, such as the fireflies under World/Environment Effects, has a renderer but nothing a
                // track change can run into: it gives no physical presence.
                physical = own.Any(c => c != null && BakedSceneryRules.GivesPresence(c.GetType().FullName, c is Renderer, c is Collider, c is Light));
            }
            public IReadOnlyList<string> Components { get { Read(); return components; } }
            public bool Physical { get { Read(); return physical; } }
            // Read only where the rule asks: for candidates and the children of crossings.
            public bool HasFamily => family ?? (family = Transform.GetComponents<Component>().Any(c => c != null && !(c is Transform) && NativeInventory.FamilyOf(c) != null)).Value;
            public bool Serialized => (Transform.gameObject.hideFlags & HideFlags.DontSave) == 0;
            public bool Animated => driven.Contains(Transform);
            public IReadOnlyList<IHierarchyNode> Children {
                get {
                    if (children == null) { children = new IHierarchyNode[Transform.childCount]; for (var i = 0; i < children.Length; i++) children[i] = new HierarchyNode(Transform.GetChild(i), driven); }
                    return children;
                }
            }
        }

        static GameObject Root(object group) => group as GameObject ?? throw new InvalidOperationException("The group no longer exists");
        static GameObject PartObject(object part) => part as GameObject ?? throw new InvalidOperationException("The part no longer exists");
        static bool Loaded(GameObject g) => g != null && g.scene.IsValid() && g.scene.isLoaded;
        public IReadOnlyList<object> Find(string scene, string path) =>
            BoardScene.LoadedAt(scene, path).Where(g => Loaded(g) && BakedSceneryRules.ResolvesGroup(VerdictOf(g), path)).Cast<object>().ToArray();
        public string Refusal(object group) => Problem(group as GameObject);
        // The construction segments of the controller on the group root itself, such as on Bryson Bridge 1 and the
        // Almond Bridge folder; a group without one has no construction-segment relationship.
        static Component OwnController(object group) => group is GameObject root && RailBridgeRoot(root) ? ConstructionSegmentNative.On(root) : null;
        public IReadOnlyList<string> ConstructionSegments(object group) => ConstructionSegmentNative.Read(OwnController(group));
        public IReadOnlyList<string> NativeConstructionSegments(object group) => ConstructionSegmentNative.NativeOf(OwnController(group));
        public int SegmentMatches(string id) => ConstructionSegmentNative.Matches(id);
        public void SetConstructionSegments(object group, IReadOnlyList<string> ids) => ConstructionSegmentNative.Write(OwnController(group), ids);
        // Why a group root cannot be edited, or null. Shared with the inventory.
        internal static string Problem(GameObject root) {
            if (!Loaded(root)) return "The group's scene is not loaded";
            // A folder of the category table, such as Bryson Yard, resolves so that it fails with this reason.
            var folder = VerdictOf(root) == null ? BakedSceneryRules.GroupRefusal(BoardScene.PathOf(root.transform)) : null;
            if (folder != null) return folder;
            if (VerdictOf(root)?.Disposition != GroupDisposition.Group) return "This object is not a group root of the Baked scenery group family";
            var components = root.GetComponentsInChildren<Component>(true);
            if (components.Any(c => c == null)) return "Missing script in the group hierarchy";
            if (components.Any(c => c is Rigidbody)) return "The group holds a rigidbody; only static colliders are supported";
            // The track and what the game builds for it are native map data, which a rail bridge move must leave in place.
            if (RailBridgeRoot(root)) { var owned = BakedSceneryRules.TrackOwnedRefusal(components.Where(c => c != null).Select(c => c.GetType().FullName)); if (owned != null) return owned; }
            return ConstructionProblem(root) ?? PileProblem(root) ?? MaskProblem(root);
        }
        // A group that holds a scene ground mask is not edited while the mask cannot be read and rebuilt: a move would
        // leave the terrain shaping behind.
        static string MaskProblem(GameObject root) =>
            !GroundMaskWorld.Available && GroundMaskWorld.Holds(root) ? "The game's ground mask members are unavailable, so this group and its parts cannot be moved, enabled or disabled" : null;
        // A culling container and a group root resolve too, so their modification fails with the reason (BakedSceneryRules.PartRefusal).
        public IReadOnlyList<object> FindParts(string scene, string path) =>
            BoardScene.LoadedAt(scene, path).Where(g => Loaded(g) && BakedSceneryRules.Resolves(PartVerdictOf(g), VerdictOf(g))).Cast<object>().ToArray();
        public string PartRefusal(object part) => PartProblem(part as GameObject);
        // Why a part cannot be edited, or null: what refuses its group refuses the part. Shared with the inventory.
        internal static string PartProblem(GameObject part) {
            if (!Loaded(part)) return "The part's scene is not loaded";
            var rule = BakedSceneryRules.PartRefusal(PartVerdictOf(part), VerdictOf(part));
            if (rule != null) return rule;
            if (!IsPart(part)) return "This object is not listed as a baked scenery part";
            var root = GroupRootOf(part);
            return root == null ? "The group root for this part is no longer listed" : Problem(root);
        }
        public string ActivityRefusal(object part) => ActivityProblem(part as GameObject);
        // Why the Enabled setting of a group root or part cannot be set, or null: a game-object culler switches the part by
        // camera distance. An object a construction controller switches takes an explicit disable, which holds.
        internal static string ActivityProblem(GameObject go) => go == null ? null : SwitchedPart(go);
        public string FollowingRefusal(object target) => FollowingProblem(target as GameObject);
        // Why the group root or part takes no track-following placement, or null: a rail or road bridge construction and its parts.
        internal static string FollowingProblem(GameObject go) {
            if (go == null) return null;
            var verdict = VerdictOf(go);
            if (verdict?.Disposition != GroupDisposition.Group) verdict = VerdictOf(GroupRootOf(go));
            return verdict?.Disposition == GroupDisposition.Group ? BakedSceneryRules.FollowingRefusal(verdict.Subcategory) : null;
        }
        static bool RailBridgeRoot(GameObject go) { var verdict = VerdictOf(go); return verdict?.Disposition == GroupDisposition.Group && verdict.Subcategory == BakedSceneryCategories.RailBridges; }
        // A rail bridge's construction controller (Game.Mow.TrackConstructionScenery): the track segment ids it reads and
        // the objects of its entries, which it switches on and off by the construction state of those segments. Read
        // lazily: a group without a controller needs none of these, and one with a controller is refused while one is
        // missing (ConstructionProblem).
        static readonly Type construction = Native.Type(BakedSceneryRules.ConstructionController);
        static readonly FieldInfo constructionEntries = construction?.GetField("entries", Native.Flags), constructionSegments = construction?.GetField("segmentIds", Native.Flags);
        static readonly FieldInfo entryTargets = constructionEntries?.FieldType.GetElementType()?.GetField("targets", Native.Flags);
        static bool ConstructionReadable => constructionEntries != null && constructionSegments?.FieldType == typeof(string[]) && entryTargets?.FieldType == typeof(GameObject[]);
        static string ConstructionProblem(GameObject root) {
            if (construction == null || ConstructionReadable) return null;
            var held = root.GetComponentsInChildren(construction, true).Length != 0;
            for (var t = root.transform.parent; t != null && !held; t = t.parent) held = t.GetComponent(construction) != null;
            return held ? "The game's track construction members are unavailable, so this rail bridge cannot be moved, enabled or disabled" : null;
        }
        // The objects every loaded construction controller switches, read once per frame.
        static HashSet<GameObject> constructionTargets;
        static int targetsFrame = -1;
        static HashSet<GameObject> ConstructionTargets() {
            if (constructionTargets != null && targetsFrame == Time.frameCount) return constructionTargets;
            var found = new HashSet<GameObject>();
            if (construction != null && ConstructionReadable)
                foreach (var controller in Resources.FindObjectsOfTypeAll(construction).OfType<Component>().Where(c => c != null && Loaded(c.gameObject)))
                    if (constructionEntries.GetValue(controller) is Array entries)
                        foreach (var entry in entries)
                            if (entry != null && entryTargets.GetValue(entry) is GameObject[] targets) foreach (var target in targets) if (target != null) found.Add(target);
            targetsFrame = Time.frameCount;
            return constructionTargets = found;
        }
        // An object whose controller writes go to a stand-in still counts: its controller still switches it.
        internal static bool ConstructionSwitched(GameObject go) => go != null && construction != null && (ConstructionTargets().Contains(go) || constructionHost?.Holds(go) == true);
        // The objects a construction controller switches at or below the root, diverted or not.
        static IEnumerable<object> ConstructionNodes(GameObject root) =>
            ConstructionTargets().Concat(constructionHost?.Held ?? Enumerable.Empty<GameObject>()).Where(go => go != null && go.transform.IsChildOf(root.transform)).Distinct().Cast<object>().ToArray();
        // The construction controllers' entries, as the stand-ins of ConstructionActivity reach them: every entry that names
        // an object is pointed at a hidden stand-in object, whose activity the controller then sets. Each change of the
        // stand-in's activity reports the controller's choice; the object itself is set by the patcher.
        sealed class ConstructionHost : IConstructionHost {
            sealed class Diversion { internal GameObject StandIn; internal readonly List<KeyValuePair<GameObject[], int>> Slots = new List<KeyValuePair<GameObject[], int>>(); }
            readonly Dictionary<GameObject, Diversion> diversions = new Dictionary<GameObject, Diversion>();
            internal bool Holds(GameObject go) => diversions.ContainsKey(go);
            internal IEnumerable<GameObject> Held => diversions.Keys.ToArray();
            public bool Switched(object node) => node is GameObject go && go != null && ConstructionSwitched(go);
            public bool Active(object node) => ((GameObject)node).activeSelf;
            public void Divert(object node, bool choice, Action<bool> changed) {
                var go = (GameObject)node;
                var diversion = new Diversion { StandIn = new GameObject("Scene Patcher construction stand-in") { hideFlags = HideFlags.HideAndDontSave } };
                diversion.StandIn.SetActive(choice);
                diversion.StandIn.AddComponent<ConstructionStandIn>().Changed = changed;
                foreach (var controller in Resources.FindObjectsOfTypeAll(construction).OfType<Component>().Where(c => c != null && Loaded(c.gameObject)))
                    if (constructionEntries.GetValue(controller) is Array entries)
                        foreach (var entry in entries)
                            if (entry != null && entryTargets.GetValue(entry) is GameObject[] targets)
                                for (var i = 0; i < targets.Length; i++) if (targets[i] == go) { targets[i] = diversion.StandIn; diversion.Slots.Add(new KeyValuePair<GameObject[], int>(targets, i)); }
                diversions[go] = diversion;
                constructionTargets = null;
            }
            public void Restore(object node) {
                if (!(node is GameObject go) || !diversions.TryGetValue(go, out var diversion)) return;
                diversions.Remove(go);
                foreach (var slot in diversion.Slots) if (slot.Key != null && slot.Value < slot.Key.Length && slot.Key[slot.Value] == diversion.StandIn) slot.Key[slot.Value] = go;
                if (diversion.StandIn != null) { var standIn = diversion.StandIn.GetComponent<ConstructionStandIn>(); if (standIn != null) standIn.Changed = null; UnityEngine.Object.Destroy(diversion.StandIn); }
                constructionTargets = null;
            }
            public void SetActive(object node, bool active) { ((GameObject)node).SetActive(active); Physics.SyncTransforms(); }
            public bool Alive(object node) => node is GameObject go && go != null;
            // The map unloads: its controllers, objects and stand-ins go with it.
            internal void Unload() { foreach (var diversion in diversions.Values) if (diversion.StandIn != null) UnityEngine.Object.Destroy(diversion.StandIn); diversions.Clear(); }
        }
        // The track segment ids of the construction controller at or above the object, which decide the visibility of
        // its construction, or null when no controller stands there. A segment that no longer exists stays in the list.
        internal static string[] ConstructionSegmentsOf(GameObject go) {
            if (construction == null || go == null) return null;
            for (var t = go.transform; t != null; t = t.parent) {
                var controller = t.GetComponent(construction);
                if (controller != null) return constructionSegments?.GetValue(controller) as string[] ?? throw new InvalidOperationException("Construction segment ids unavailable");
            }
            return null;
        }
        // Why the part's Enabled setting cannot be set, or null: a game-object culler switches the part on and off by
        // camera distance and would override a stored setting, so the patcher cannot hold it.
        internal static string SwitchedPart(GameObject part) {
            var root = GroupRootOf(part);
            return root != null && ListingCuller(part, root) != null ? "The game's culler enables and disables this part based on camera distance. It would override a saved Enabled setting, so the patcher cannot keep that setting applied" : null;
        }
        // The game-object culler in the part's group that lists the object, or null.
        static Component ListingCuller(GameObject go, GameObject root) {
            if (switcher == null || switchedObjects == null || root == null) return null;
            foreach (var culler in root.GetComponentsInChildren(switcher, true))
                if (Switches(culler).Contains(go)) return culler;
            return null;
        }
        // Whether a game-object culler on the holder switches the object: it lists it, or own culling took it out of its list.
        internal static bool ListedOn(GameObject go, GameObject holder) =>
            switcher != null && switchedObjects != null && go != null && holder != null && holder.GetComponents(switcher).Any(culler => culler != null && Switches(culler).Contains(go));
        // Whether a simple culler on the object switches the object itself by camera distance now.
        internal static bool SwitchesItself(GameObject go) { if (simple == null || cullingGroup == null) return false; var group = GroupOf(go.GetComponent(simple)); return group != null && group.enabled; }
        // A part's own activity is switched by a simple culler on it, or by a game-object culler that lists it.
        static bool Switched(Transform t, HashSet<GameObject> listed) => (simple != null && t.GetComponent(simple) != null) || listed.Contains(t.gameObject);
        static readonly FieldInfo switchedObjects = switcher?.GetField("gameObjects", Native.Flags);
        static HashSet<GameObject> Listed(GameObject root) {
            var listed = new HashSet<GameObject>();
            if (switcher == null || switchedObjects == null) return listed;
            foreach (var culler in root.GetComponentsInChildren(switcher, true))
                foreach (var go in Switches(culler)) listed.Add(go);
            return listed;
        }
        static Component OwnSimple(GameObject root) => simple == null ? null : root.GetComponent(simple);
        static CullingGroup GroupOf(Component culler) => culler == null ? null : cullingGroup.GetValue(culler) as CullingGroup;
        // The object's own activity as the scene authored it. A simple culler on the object, or a game-object culler in
        // its group that lists it, switches it off while the camera is far away; off while that culler hides it, the
        // object counts as on, as AuthoredActivity.Enabled reads it for the inventory.
        // listingRoot names the object whose game-object cullers can list the object, when that is not its baked
        // scenery group: a crossing controller for the objects below it.
        internal static bool Authored(GameObject go, GameObject listingRoot = null) {
            // An object whose construction controller writes go to a stand-in takes the controller's choice, unless an
            // explicit disable holds it off.
            var shared = constructions?.Effective(go);
            if (shared != null) return shared.Value;
            // An object own culling switches reads what the scene or the modder set, whatever the camera made of it.
            var desired = culling?.Desired(go);
            if (desired != null) return desired.Value;
            if (go.activeSelf) return true;
            var group = GroupOf(OwnSimple(go));
            if (AuthoredActivity.Enabled(false, group != null && group.enabled, group != null && group.enabled && group.IsVisible(0))) return true;
            var culler = ListingCuller(go, listingRoot ?? GroupRootOf(go) ?? go);
            return culler != null && AuthoredActivity.Enabled(false, true, Convert.ToString(switcherState.GetValue(culler)) == "Visible");
        }
        // Whether an object that is switched off is off only through a culler (CullingActivity.OffThroughCuller): own
        // culling, a simple culler on it, or a game-object culler on it, on any object above it, such as a crossing
        // root's culler, or in its baked scenery group.
        internal static bool OffThroughCuller(GameObject go) {
            var group = GroupOf(OwnSimple(go));
            var listing = new List<KeyValuePair<bool, bool>>();
            if (switcher != null && switchedObjects != null && switcherState != null) {
                var cullers = new List<Component>();
                for (var t = go.transform; t != null; t = t.parent) cullers.AddRange(t.GetComponents(switcher));
                var root = GroupRootOf(go);
                if (root != null) cullers.AddRange(root.GetComponentsInChildren(switcher, true));
                foreach (var culler in cullers.Distinct())
                    if (culler != null) listing.Add(new KeyValuePair<bool, bool>(Switches(culler).Contains(go), Convert.ToString(switcherState.GetValue(culler)) == "Visible"));
            }
            return CullingActivity.OffThroughCuller(culling?.Desired(go), group != null && group.enabled, group != null && group.enabled && group.IsVisible(0), listing);
        }
        static double[] Coordinates(Vector3 v) => new double[] { v.x, v.y, v.z };
        static double[] Coordinates(Quaternion q) => new double[] { q.x, q.y, q.z, q.w };
        public GroupObservation Observe(object target) => Observe(target, null, null);
        // listingRoot: the object whose game-object cullers list the parts, when the root is no baked scenery group.
        // moves: the parts that the game moves while it runs, beside the transforms the baked scenery rule names.
        internal GroupObservation Observe(object target, GameObject listingRoot, Func<Transform, bool> moves) {
            var root = Root(target);
            Physics.SyncTransforms();
            RememberPiles(root);
            var t = root.transform;
            var position = NativeInventory.MapPosition(t); var rotation = t.rotation;
            var state = new ObjectState(new Placement(Coordinates(position), Coordinates(rotation)), Enabled(root));
            var parts = new List<GroupPart>();
            var listed = Listed(root);
            Verdicts();
            foreach (var part in root.GetComponentsInChildren<Transform>(true)) {
                if (part == t || Transient(part, t)) continue;
                var local = t.InverseTransformPoint(part.position); var turn = Quaternion.Inverse(t.rotation) * part.rotation;
                // A part that a culler or a rail bridge's construction controller switches keeps no activity of its own in the invariant.
                var switched = Switched(part, listed) || ConstructionSwitched(part.gameObject);
                parts.Add(new GroupPart(Relative(t, part) + "#" + part.GetInstanceID(), Coordinates(local), Coordinates(turn), switched ? Authored(part.gameObject, listingRoot) : OwnActivity(part.gameObject), switched, part.gameObject, part.parent == t ? null : part.parent.gameObject, Animated(part, t) || (moves != null && moves(part))));
            }
            var cullers = new List<GroupCuller>();
            var regions = culling == null ? new ObservedRegion[0] : culling.Regions(root);
            foreach (var culler in root.GetComponentsInChildren(simple, true)) {
                var group = GroupOf(culler); var spheres = cullingSpheres.GetValue(culler) as BoundingSphere[];
                var holds = group != null && group.enabled && spheres != null && spheres.Length > 0 && (culler.transform == t || culler.transform.parent.gameObject.activeInHierarchy);
                // Each culler reports the radius of its sphere and the radius the scene gives it.
                var authored = (float)simpleRadius.GetValue(culler);
                var radius = spheres != null && spheres.Length > 0 ? spheres[0].radius : authored;
                cullers.Add(new GroupCuller(CullerId(t, culler), holds ? Map(spheres[0].position, culler.transform) : null, Map(culler.transform.position, culler.transform), radius, authored, culler.gameObject, null, CullerKind.Simple, culler, regions.Where(r => ReferenceEquals(r.Culler, culler))));
            }
            foreach (var type in new[] { renderer, switcher })
                foreach (var culler in root.GetComponentsInChildren(type, true)) {
                    var switches = type == switcher ? Switches(culler) : new GameObject[0];
                    cullers.Add(new GroupCuller(CullerId(t, culler), Registered(culler), Map(culler.transform.position, culler.transform), SphereRadius(culler), (float)(type == switcher ? switcherRadius : rendererRadius).GetValue(culler), culler.gameObject, switches,
                        type == switcher ? CullerKind.GameObject : CullerKind.Renderer, culler, regions.Where(r => ReferenceEquals(r.Culler, culler))));
                }
            var own = GroupOf(OwnSimple(root));
            var verdict = VerdictOf(root);
            // Every edit of a bridge construction verifies its colliders; a rail bridge also the track-owned scenery beside it.
            var bridge = verdict?.Disposition == GroupDisposition.Group && verdict.Category == BakedSceneryCategories.Bridge;
            var rail = bridge && verdict.Subcategory == BakedSceneryCategories.RailBridges;
            return new GroupObservation(state, parts, cullers, t.parent, own == null ? (bool?)null : own.enabled, bridge ? Colliders(t) : null, rail ? TrackScenery(root) : null);
        }
        // The generated trestles near a rail bridge, as the Ela bridge describes them: selected at the first observation
        // of each bridge, so a moved bridge keeps comparing the same ones.
        static readonly Type trestle = Native.Type(BridgeWorld.TrestleType);
        static readonly Dictionary<GameObject, Component[]> trestles = new Dictionary<GameObject, Component[]>();
        static string TrackScenery(GameObject root) {
            if (trestle == null) return "";
            if (!trestles.TryGetValue(root, out var selected)) trestles[root] = selected = BridgeWorld.TrestlesNear(trestle, root.transform.position);
            return BridgeWorld.DescribeTrestles(selected);
        }
        // The colliders the scene saves at and below a rail or road bridge construction's root, with copied values, as the Ela
        // bridge observes its colliders. What the game builds while it runs does not count.
        static IEnumerable<ColliderState> Colliders(Transform root) {
            var found = new List<ColliderState>();
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) {
                if (c == null || Transient(c.transform, root)) continue;
                var chain = true;
                for (var node = c.transform; node != null && node != root; node = node.parent) chain &= node.gameObject.activeSelf;
                var local = root.InverseTransformPoint(c.transform.position); var turn = Quaternion.Inverse(root.rotation) * c.transform.rotation;
                found.Add(new ColliderState(Relative(root, c.transform) + "|" + c.GetType().FullName + "|" + c.GetInstanceID(), BridgeWorld.Shape(c), c.enabled, chain, c.gameObject.activeInHierarchy,
                    Coordinates(local), Coordinates(turn), BridgeWorld.BoundsFollow(c)));
            }
            return found;
        }
        public PartObservation ObservePart(object target) {
            var part = PartObject(target);
            var root = GroupRootOf(part) ?? throw new InvalidOperationException("The group root for this part is no longer listed");
            var group = Observe(root);
            var position = NativeInventory.MapPosition(part.transform);
            // A part without a fixed placement keeps its place in the object directly above it.
            var parent = part.transform.parent;
            var parentPlacement = parent == null ? null : new Placement(Coordinates(NativeInventory.MapPosition(parent)), Coordinates(parent.rotation));
            return new PartObservation(new ObjectState(new Placement(Coordinates(position), Coordinates(part.transform.rotation)), Enabled(part)), root, group, parent == null ? null : parent.gameObject, parentPlacement);
        }
        static string CullerId(Transform root, Component culler) => Relative(root, culler.transform) + "|" + culler.GetType().FullName;
        // The objects a game-object culler switches: those in its list and those own culling took out of it.
        static GameObject[] Switches(Component culler) =>
            ListedObjects.Union(switchedObjects?.GetValue(culler) is System.Collections.IEnumerable objects ? objects.Cast<object>() : null, culling?.HandedOver(culler)).OfType<GameObject>().Where(go => go != null).ToArray();
        // The radius a renderer or game-object culler's sphere holds in its culling manager, or its authored radius while
        // it holds none.
        static double SphereRadius(Component culler) {
            var token = (culler.GetType() == switcher ? switcherToken : rendererToken).GetValue(culler);
            if (token != null && !(bool)Native.Read(token, "IsRemoved")) {
                var index = (int)Native.Read(token, "Index");
                var all = (BoundingSphere[])managerSpheres.GetValue(Native.Read(token, "Manager"));
                if (index >= 0 && index < all.Length) return all[index].radius;
            }
            return (float)(culler.GetType() == switcher ? switcherRadius : rendererRadius).GetValue(culler);
        }
        // The corners of the renderer, collider and light bounds at and below the part. The bounds come from the meshes
        // and collider shapes, which an object that is switched off still has. A visual effect gives no presence, and a
        // light counts only for a part without renderers.
        public IReadOnlyList<IReadOnlyList<double>> Presence(object target, IReadOnlyList<object> without) {
            var part = PartObject(target).transform;
            var skip = (without ?? new object[0]).OfType<GameObject>().Where(go => go != null).Select(go => go.transform).ToArray();
            // What the game builds while it runs, such as the collider a pile builds near the camera, does not count, so
            // the presence does not change with the camera distance.
            bool Counts(Component c) => c != null && !skip.Any(c.transform.IsChildOf) && !Transient(c.transform, part);
            var points = new List<Vector3>();
            foreach (var shape in part.GetComponentsInChildren<Renderer>(true))
                if (Counts(shape) && BakedSceneryRules.GivesPresence(shape.GetType().FullName, true, false, false)) points.AddRange(Corners(shape));
            var shapes = points.Count;
            foreach (var collider in part.GetComponentsInChildren<Collider>(true)) if (Counts(collider)) points.AddRange(Corners(collider));
            // A storage pile has no saved renderer or collider: it counts with the culling sphere it lays its logs out in.
            if (bulk != null && bulkRadius != null)
                foreach (var pile in part.GetComponentsInChildren(bulk, true))
                    if (Counts(pile)) { var r = (float)bulkRadius.Invoke(pile, null); for (var i = 0; i < 8; i++) points.Add(pile.transform.position + Sign(i) * r); shapes++; }
            if (shapes == 0) foreach (var light in part.GetComponentsInChildren<Light>(true)) if (Counts(light)) points.Add(light.transform.position);
            if (points.Count == 0) points.Add(part.position);
            var moves = NativeInventory.MovesWithOrigin(part);
            return points.Select(point => { var map = NativeInventory.MapPosition(point, moves); return (IReadOnlyList<double>)new double[] { map.x, map.y, map.z }; }).ToArray();
        }
        internal static IEnumerable<Vector3> Corners(Collider collider) {
            Bounds local;
            if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
            else if (collider is SphereCollider sphere) local = new Bounds(sphere.center, Vector3.one * sphere.radius * 2);
            else if (collider is CapsuleCollider capsule) { var size = Vector3.one * capsule.radius * 2; size[capsule.direction] = Math.Max(capsule.height, capsule.radius * 2); local = new Bounds(capsule.center, size); }
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
            else {
                // Any other collider reports bounds only while it is on.
                if (collider.enabled && collider.gameObject.activeInHierarchy) { var b = collider.bounds; for (var i = 0; i < 8; i++) yield return b.center + Vector3.Scale(b.extents, Sign(i)); }
                yield break;
            }
            var matrix = collider.transform.localToWorldMatrix;
            for (var i = 0; i < 8; i++) yield return matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, Sign(i)));
        }
        static Vector3 Sign(int i) => new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
        static IEnumerable<Vector3> Corners(Renderer shape) {
            Bounds local; Matrix4x4 matrix;
            if (shape is SkinnedMeshRenderer skinned) { local = skinned.localBounds; matrix = (skinned.rootBone != null ? skinned.rootBone : skinned.transform).localToWorldMatrix; }
            else {
                var filter = shape.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) { local = filter.sharedMesh.bounds; matrix = shape.transform.localToWorldMatrix; }
                else if (shape.gameObject.activeInHierarchy) { local = shape.bounds; matrix = Matrix4x4.identity; }
                else yield break;
            }
            for (var i = 0; i < 8; i++)
                yield return matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
        }
        // The game's components that turn or move a transform of baked scenery while it runs. Railroader 0.6.3: a
        // RollingStock.GaugeNeedle rotates its needle whenever its value changes, and the depot clock's ClockController
        // sets the value of one for each hand every frame. The needle can be any transform, wherever the gauge stands. One
        // detection serves the part rule and the group observation: every gauge below a loaded World root, and the
        // transform its needle field names.
        // A field of BakedSceneryRules.DrivenFields, such as a gauge's needle or a lantern's filament renderer, names a
        // transform the game drives; the field holds that transform or a component on it.
        static readonly Tuple<Type, FieldInfo>[] drivenFields = BakedSceneryRules.DrivenFields
            .Select(d => { var type = Native.Type(d.Component); return Tuple.Create(type, type?.GetField(d.Field, Native.Flags)); })
            .Where(d => d.Item1 != null && d.Item2 != null && typeof(Component).IsAssignableFrom(d.Item2.FieldType)).ToArray();
        // A component of BakedSceneryRules.Animators, such as the KeyValueBoolAnimator of the coal loader's chute,
        // animates the object it stands on.
        static readonly Type[] animators = BakedSceneryRules.Animators.Select(Native.Type).Where(type => type != null).ToArray();
        static void AddDriven(Transform world, HashSet<Transform> found) {
            foreach (var type in animators)
                foreach (var animator in world.GetComponentsInChildren(type, true)) if (animator is Component c && c != null) found.Add(c.transform);
            foreach (var driven in drivenFields)
                foreach (var owner in world.GetComponentsInChildren(driven.Item1, true))
                    if (owner != null && driven.Item2.GetValue(owner) is Component target && target != null) found.Add(target.transform);
        }
        // The part or an object above it, below the root, is a transform the game animates.
        static bool Animated(Transform part, Transform root) {
            if (driven.Count == 0) return false;
            for (var t = part; t != null && t != root; t = t.parent) if (driven.Contains(t)) return true;
            return false;
        }
        static bool Transient(Transform child, Transform root) {
            for (var t = child; t != null && t != root; t = t.parent) if ((t.gameObject.hideFlags & HideFlags.DontSave) != 0) return true;
            return false;
        }
        static string Relative(Transform root, Transform t) => t == root ? "." : Relative(root, t.parent) + "/" + t.name;
        static double[] Map(Vector3 scene, Transform owner) { var p = NativeInventory.MapPosition(scene, NativeInventory.MovesWithOrigin(owner)); return new double[] { p.x, p.y, p.z }; }
        // The centre of the culling sphere a renderer or game-object culler holds in its culling manager, or null.
        static double[] Registered(Component culler) {
            var token = (culler.GetType() == switcher ? switcherToken : rendererToken).GetValue(culler);
            if (token == null || (bool)Native.Read(token, "IsRemoved")) return null;
            var index = (int)Native.Read(token, "Index");
            var all = (BoundingSphere[])managerSpheres.GetValue(Native.Read(token, "Manager"));
            if (index < 0 || index >= all.Length) throw new InvalidOperationException("Culling sphere index out of range");
            return Map(all[index].position, culler.transform);
        }
        public void SetActive(object target, bool active) => Switch(Root(target), active);
        public void SetPartActive(object target, bool active) => Switch(PartObject(target), active);
        // A simple culler on the object switches it on when the camera comes near; while the object is disabled its
        // culling group sends no events, and it resumes when the object is enabled.
        void Switch(GameObject go, bool active) {
            var group = GroupOf(OwnSimple(go));
            // An object a construction controller switches: a disable holds it off against the controller, and Enable
            // returns it to the controller's current choice.
            if (!disposed && ownConstructions.Set(go, active)) { if (group != null) group.enabled = active; Physics.SyncTransforms(); return; }
            // An object own culling switches takes the value as its desired activity and shows by its spheres.
            if (!disposed && own.SetDesired(go, active)) { if (group != null) group.enabled = active; Physics.SyncTransforms(); return; }
            // Enabled again, the object takes the visibility its culling group holds, so a far object does not show until
            // the camera comes near. An object whose culler has not started yet starts it now.
            if (active) { if (group != null) group.enabled = true; go.SetActive(group == null || group.IsVisible(0)); }
            else { if (group != null) group.enabled = false; go.SetActive(false); }
            Physics.SyncTransforms();
        }
        public void SetPlacement(object target, Placement placement) => Place(Root(target), placement);
        public void SetPartPlacement(object target, Placement placement) => Place(PartObject(target), placement);
        static void Place(GameObject go, Placement placement) {
            var map = new Vector3(Checked(placement.Position[0]), Checked(placement.Position[1]), Checked(placement.Position[2]));
            var rotation = new Quaternion(Checked(placement.Rotation[0]), Checked(placement.Rotation[1]), Checked(placement.Rotation[2]), Checked(placement.Rotation[3]));
            var moves = NativeInventory.MovesWithOrigin(go.transform);
            var scene = NativeInventory.ScenePosition(map, moves);
            if ((NativeInventory.MapPosition(scene, moves) - map).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Map/origin conversion failed its round trip");
            go.transform.SetPositionAndRotation(scene, rotation);
            // Physics reads moved static colliders at the next simulation step; synchronise now so queries and checks agree.
            Physics.SyncTransforms();
        }
        public void RefreshCulling(object target, CullingPlan plan) {
            // A world whose patcher state ended gave its own culling back to the game and takes none again.
            if (disposed) return;
            var root = Root(target);
            // A renderer or game-object culler keeps its sphere where it registered it; an inactive one holds none and
            // registers at the current placement when it is enabled.
            foreach (var type in new[] { renderer, switcher })
                foreach (var culler in root.GetComponentsInChildren(type, true))
                    if (culler is Behaviour behaviour && behaviour.isActiveAndEnabled && (culler.GetType() == switcher ? switcherToken : rendererToken).GetValue(culler) != null)
                        Native.Call(culler, "RequestUpdateCullingPosition");
            // A simple culler moves its sphere in its own Update, which does not run while the camera has culled it.
            foreach (var culler in root.GetComponentsInChildren(simple, true))
                if (cullingSpheres.GetValue(culler) != null) simpleMoved.Invoke(culler, null);
            RefreshPiles(root);
            // Every culler keeps the radius the scene gives it. A moved part outside a culler's sphere gets a sphere of
            // its own instead, as the plan names it.
            own.Apply(root, plan);
            // An object a construction controller switches that own culling took over gets the controller's choice from
            // now on; one it let go of goes back to the controller.
            ownConstructions.Settle(ConstructionNodes(root));
            Physics.SyncTransforms();
        }
        // Each enabled storage pile that stands elsewhere than where it last laid out its logs moves its culling sphere
        // to where it stands now, and one whose sphere shows lays its logs out again there, releasing the collider mesh
        // it replaced; a pile the camera has culled lays them out when it shows again. Its sphere must then stand at the
        // pile's new place in map coordinates with the radius the pile gives it; otherwise the edit fails and is
        // restored, so a moved pile never stays culled at its old place.
        static void RefreshPiles(GameObject root) {
            if (bulk == null || PileProblem(root) != null) return;
            foreach (var pile in root.GetComponentsInChildren(bulk, true)) {
                var now = PileAt(pile);
                if (laidOut.TryGetValue(pile, out var was) && SamePile(was, now)) continue;
                var token = pile is Behaviour behaviour && behaviour.isActiveAndEnabled ? bulkToken.GetValue(pile) : null;
                // A disabled pile, or one whose sphere is gone, registers and lays out at its place when it starts again.
                if (token == null || (bool)Native.Read(token, "IsRemoved")) { laidOut[pile] = now; continue; }
                bulkRefresh.Invoke(pile, null);
                if ((bool)bulkVisible.GetValue(pile)) {
                    var replaced = ColliderMesh(pile);
                    bulkRebuild.Invoke(pile, null);
                    if (replaced != null && replaced != ColliderMesh(pile)) UnityEngine.Object.Destroy(replaced);
                }
                var index = (int)Native.Read(token, "Index");
                var all = (BoundingSphere[])managerSpheres.GetValue(Native.Read(token, "Manager"));
                if (index < 0 || index >= all.Length) throw new InvalidOperationException("The culling sphere of the " + BakedSceneryRules.BulkKind(pile.GetType().FullName) + " " + pile.name + " is not registered");
                var centre = Map(all[index].position, pile.transform);
                if (Distance(centre, now.Position) > SphereDistance || Math.Abs(all[index].radius - (float)bulkRadius.Invoke(pile, null)) > SameDistance)
                    throw new InvalidOperationException("The culling sphere of the " + BakedSceneryRules.BulkKind(pile.GetType().FullName) + " " + pile.name + " did not follow it to its new place");
                laidOut[pile] = now;
            }
        }
        public bool Below(object target, object above) =>
            target is GameObject part && part != null && above is GameObject over && over != null && part != over && part.transform.IsChildOf(over.transform);
        public bool Alive(object handle) => handle is GameObject go && go != null;
        static float Checked(double value) { var f = (float)value; if (Single.IsNaN(f) || Single.IsInfinity(f)) throw new InvalidOperationException("Coordinate exceeds native precision"); return f; }
    }
    // The stand-in that receives a construction controller's writes for an object the patcher shares with it: Unity
    // calls OnEnable and OnDisable when the controller sets its activity.
    internal sealed class ConstructionStandIn : MonoBehaviour {
        internal Action<bool> Changed;
        void OnEnable() { try { Changed?.Invoke(true); } catch (Exception e) { Debug.LogWarning("Scene Patcher: construction activity update failed: " + e.GetBaseException().Message); } }
        void OnDisable() { try { Changed?.Invoke(false); } catch (Exception e) { Debug.LogWarning("Scene Patcher: construction activity update failed: " + e.GetBaseException().Message); } }
    }
}
