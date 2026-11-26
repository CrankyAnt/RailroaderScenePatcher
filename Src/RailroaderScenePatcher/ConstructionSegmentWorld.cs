// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The construction segments of a rail bridge's construction controller (Game.Mow.TrackConstructionScenery, Railroader
    // 0.6.3): segmentIds names the track segments; the controller caches them in _cachedSegments at its first use and
    // reads their construction state in UpdateVisibility to switch the objects of its entries. The decompiled game code
    // shows UpdateVisibility dereferencing each segment before it switches anything, so a missing segment stops it there;
    // Railroader logs that at world load as an error during DidRestore. DeferredRestores holds that restore-time call back
    // until the world-load apply has written the repair.
    internal static class ConstructionSegmentNative {
        static readonly Type controller = Native.Type(SceneObjectRules.ConstructionController), graph = Native.Type("Track.Graph");
        static readonly FieldInfo ids = controller?.GetField("segmentIds", Native.Flags), cache = controller?.GetField("_cachedSegments", Native.Flags);
        static readonly MethodInfo update = controller?.GetMethod("UpdateVisibility", Native.Flags, null, Type.EmptyTypes, null);
        // Why the construction segments cannot be read or set on this game build, or null.
        internal static string Problem => controller == null ? "The game's construction controller is unavailable."
            : ids?.FieldType != typeof(string[]) || cache == null || update == null || graph == null ? "The game's track construction members are unavailable, so you can't set construction segments." : null;
        // The segment ids each controller named when the patcher first read it in this load, by controller.
        static readonly Dictionary<Component, string[]> native = new Dictionary<Component, string[]>();
        // The construction controller on the object itself, or null.
        internal static Component On(GameObject go) => controller == null || go == null ? null : go.GetComponent(controller);
        internal static string[] Read(Component c) => c == null ? null : (ids?.GetValue(c) as string[] ?? throw new InvalidOperationException("The construction segment IDs are unavailable."));
        internal static string[] NativeOf(Component c) {
            if (c == null) return null;
            foreach (var gone in native.Keys.Where(k => k == null).ToList()) native.Remove(gone);
            if (!native.TryGetValue(c, out var found)) native[c] = found = (Read(c) ?? new string[0]).ToArray();
            return found;
        }
        // A loaded segment is unique by its id: segment ids are the keys of the graph's segment lookup.
        internal static int Matches(string id) {
            var shared = Native.Read(graph ?? throw new InvalidOperationException("The track graph is unavailable."), "Shared") ?? throw new InvalidOperationException("The track graph is unavailable.");
            return Native.Call(shared, "GetSegment", id) != null ? 1 : 0;
        }
        // Writes the ids, drops the cached segments so the controller reads the new ones, and lets an enabled controller
        // show the construction by their state, as it does when it is enabled. With a missing segment it does not run
        // the controller and leaves the parts as they are.
        internal static void Write(Component c, IReadOnlyList<string> values) {
            if (Problem != null) throw new InvalidOperationException(Problem);
            if (c == null) throw new InvalidOperationException("The construction controller no longer exists.");
            NativeOf(c);
            ids.SetValue(c, values.ToArray());
            cache.SetValue(c, null);
            if (!(c is Behaviour behaviour) || !behaviour.isActiveAndEnabled || values.Any(id => Matches(id) != 1)) return;
            try { update.Invoke(c, null); }
            catch (TargetInvocationException e) { throw new InvalidOperationException("The construction controller couldn't show the construction based on its segments: " + e.GetBaseException().Message); }
        }
    }
    // A construction controller on an object of its own: Ela Bridge Scenery, the one the inventory lists in the Bridge
    // construction scenery family. Any other controller is no target of this capability.
    internal sealed class ConstructionSceneryWorld : IConstructionSceneryWorld {
        internal ConstructionSceneryWorld() { if (ConstructionSegmentNative.Problem != null) throw new InvalidOperationException(ConstructionSegmentNative.Problem); }
        public IEnumerable<string> MapIds => ModScenePatchFiles.MapIds();
        static bool Loaded(GameObject g) => g != null && g.scene.IsValid() && g.scene.isLoaded;
        internal const string ElaPath = "World/Large Scenery/Ela/Ela Bridge Scenery";
        static bool OwnObject(GameObject g) => ConstructionSegmentNative.On(g) != null && BoardScene.PathOf(g.transform) == ElaPath;
        public IReadOnlyList<object> Find(string scene, string path) => BoardScene.LoadedAt(scene, path).Where(g => Loaded(g) && OwnObject(g)).Cast<object>().ToArray();
        public string Refusal(object target) => !(target is GameObject g) || !Loaded(g) ? "The controller's scene isn't loaded." : !OwnObject(g) ? "Only the Ela Bridge Scenery construction controller has construction segments of its own. For another rail bridge, set the construction segments on its group." : null;
        static Component Controller(object target) => ConstructionSegmentNative.On(target as GameObject);
        public Placement Placement(object target) {
            var t = ((GameObject)target).transform; var position = NativeInventory.MapPosition(t); var rotation = t.rotation;
            return new Placement(new double[] { position.x, position.y, position.z }, new double[] { rotation.x, rotation.y, rotation.z, rotation.w });
        }
        public bool Active(object target) => ((GameObject)target).activeSelf;
        public IReadOnlyList<string> ConstructionSegments(object target) => ConstructionSegmentNative.Read(Controller(target));
        public IReadOnlyList<string> NativeConstructionSegments(object target) => ConstructionSegmentNative.NativeOf(Controller(target));
        public int SegmentMatches(string id) => ConstructionSegmentNative.Matches(id);
        public void SetConstructionSegments(object target, IReadOnlyList<string> ids) => ConstructionSegmentNative.Write(Controller(target), ids);
    }
}
