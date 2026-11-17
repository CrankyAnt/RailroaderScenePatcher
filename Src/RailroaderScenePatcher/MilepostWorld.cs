// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The milepost pattern: an object holding Helpers.MilePost, its required Generic Track.TrackMarker and its text
    // synchronizer. The marker's location, registration, placement and activity are the Generic-marker relationship. The
    // milepost itself listens to its marker's location changes and keeps its marker in a private property; those two
    // consumers belong to the pattern. The constructor checks that the listener's body is exactly one call to
    // UpdateMileage and that UpdateMileage's body is empty, so a location change recalculates nothing on the running
    // build. The game's origin cache (MilePost._originMilePosts) maps a prefix to the origin milepost object found on
    // the first lookup; it holds the object, not its location, so an edit leaves it alone and the scene checks that it
    // did. The displayed text is set on the text synchronizer and its rendered texts; nothing recalculates it from the
    // binding.
    internal sealed class MilepostWorld : TrackMarkerWorld, IMilepostWorld {
        internal const string SynchronizerType = "Helpers.TextSynchronizer";
        readonly Type post;
        readonly FieldInfo prefix, origin, mileage, originCache, synchronizerText;
        internal MilepostWorld() {
            post = Native.Type(Milepost) ?? throw new InvalidOperationException("The game's milepost type is unavailable");
            var synchronizer = Native.Type(SynchronizerType) ?? throw new InvalidOperationException("The game's milepost text synchronizer is unavailable");
            prefix = Declared(post, "prefix", typeof(string)); origin = Declared(post, "origin", typeof(bool)); mileage = Declared(post, "mileage", typeof(float));
            synchronizerText = Declared(synchronizer, "text", typeof(string));
            originCache = post.GetField("_originMilePosts", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (originCache == null || !typeof(IDictionary).IsAssignableFrom(originCache.FieldType)) throw new MissingFieldException(Milepost, "_originMilePosts");
            // The milepost's location listener calls UpdateMileage. The capability relies on that recalculating nothing; a build
            // whose listener or UpdateMileage does more is a different milepost and this capability is incompatible with it.
            var update = post.GetMethod("UpdateMileage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) ?? throw new MissingMethodException(Milepost, "UpdateMileage");
            if (!EmptyBody(update.GetMethodBody()?.GetILAsByteArray())) throw new InvalidOperationException("The milepost capability expects an empty UpdateMileage method. This game build's implementation does not match");
            var listener = post.GetMethod("TrackMarkerOnOnLocationChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null) ?? throw new MissingMethodException(Milepost, "TrackMarkerOnOnLocationChanged");
            if (!CallsOnly(listener, update)) throw new InvalidOperationException("The milepost capability expects TrackMarkerOnOnLocationChanged to contain only a call to UpdateMileage. This game build's implementation does not match");
        }
        // The IL of an empty method: ret, optionally after one nop.
        internal static bool EmptyBody(byte[] il) => il != null && (il.SequenceEqual(new byte[] { 0x2A }) || il.SequenceEqual(new byte[] { 0x00, 0x2A }));
        // The IL of a method that does nothing but call the target on this instance: ldarg.0, call or callvirt with the
        // target's token, ret, optionally after one nop.
        static bool CallsOnly(MethodInfo method, MethodInfo target) {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            var start = il.Length == 8 && il[0] == 0x00 ? 1 : 0;
            if (il.Length - start != 7 || il[start] != 0x02 || (il[start + 1] != 0x28 && il[start + 1] != 0x6F) || il[start + 6] != 0x2A) return false;
            try { return method.Module.ResolveMethod(BitConverter.ToInt32(il, start + 2)) == target; }
            catch (ArgumentException) { return false; }
        }
        static FieldInfo Declared(Type type, string name, Type fieldType) {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != fieldType) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        protected override bool Belongs(GameObject g) => Get(g, Milepost) != null;
        public override IReadOnlyList<object> Resolve(TargetIdentity identity) => Markers(identity, Belongs, MilepostScene.Component);
        public override bool StillIdentifies(object target, TargetIdentity identity) {
            var g = target as GameObject;
            return g != null && identity.Component == MilepostScene.Component && Loaded(g) && g.scene.name == identity.Scene && g.GetComponent(post) != null
                && BoardScene.PathOf(g.transform) == identity.Path;
        }
        public override string PatternProblem(object target) => MilepostProblem(target as GameObject, CurrentReferrers(), SetupHolders());
        public override string PlacementProblem(object target) => MilepostProblem(target as GameObject, null, null, true);
        // Why the object does not conform to the supported milepost pattern, or null. Shared with the inventory.
        internal static string MilepostProblem(GameObject go, ReferenceScan references, HolderIndex setups, bool placementOnly = false) {
            if (!Loaded(go)) return "This object is not loaded, so its milepost cannot be edited";
            var components = go.GetComponents<Component>();
            if (components.Any(c => c == null)) return "Missing script on the milepost object";
            var posts = components.Where(c => c.GetType().FullName == Milepost).ToArray();
            if (posts.Length != 1) return posts.Length == 0 ? "The object has no milepost component" : "The object has more than one milepost component";
            var markers = components.Where(c => c.GetType().FullName == MarkerType).ToArray();
            if (markers.Length != 1) return markers.Length == 0 ? "The milepost has no track marker" : "The milepost has more than one track marker";
            if (Get(go, PadBuilder) != null) return "The milepost also has a crossing-pad builder. This combination is not supported";
            if (Get(go, SynchronizerType) == null) return "The milepost has no text synchronizer";
            var m = markers[0];
            var type = System.Convert.ToString(Field(m, "type"));
            if (type != "Generic") return "A milepost needs a Generic track marker for editing. This marker is " + type;
            var milepost = posts[0];
            // The milepost's own listener and property belong to the pattern, as the colocated relative position's reference does.
            return Consumers(go, m, references, setups,
                (owner, field) => owner is Component c && c.gameObject == go && ((c.GetType().FullName == Relative && field == "_marker") || ReferenceEquals(c, milepost)),
                placementOnly, listener => ReferenceEquals(listener.Target, milepost) && listener.Method.Name == "TrackMarkerOnOnLocationChanged");
        }
        MilepostState IMilepostWorld.Milepost(object target) {
            var go = (GameObject)target;
            var component = go.GetComponent(post) ?? throw new InvalidOperationException("The object no longer has a milepost component");
            var name = (string)prefix.GetValue(component);
            var cache = (IDictionary)originCache.GetValue(null);
            string cached;
            if (cache == null || name == null || !cache.Contains(name)) cached = MilepostState.NotCached;
            else cached = MilepostState.CachedAs(cache[name] is Component held && held != null ? held.gameObject.scene.name + "/" + BoardScene.PathOf(held.transform) : null);
            var synchronizer = Get(go, SynchronizerType);
            return new MilepostState(name, (bool)origin.GetValue(component), (float)mileage.GetValue(component), synchronizer == null ? null : (string)synchronizerText.GetValue(synchronizer),
                Rendered(go), Model(go), cached, go.activeInHierarchy);
        }
        // Every rendered text below the post, in hierarchy order. A text object that has never been awake shows nothing yet.
        static string Rendered(GameObject go) => String.Join("|", go.GetComponentsInChildren<TMPro.TMP_Text>(true).Select(t => t.text));
        // The meshes and materials below the post, by path and asset identity. Shared meshes and materials are read without
        // instantiating them. A rendered text creates its mesh and material when it wakes and adds sub-mesh children when it
        // generates text, so objects holding a TMP_Text or TMP_SubMesh are left out: the text comparison covers them.
        static string Model(GameObject go) {
            var text = new StringBuilder();
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true).Where(f => !IsText(f.gameObject)))
                text.Append(BoardScene.PathOf(filter.transform)).Append('=').Append(filter.sharedMesh == null ? "none" : filter.sharedMesh.GetInstanceID().ToString(CultureInfo.InvariantCulture)).Append(';');
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true).Where(r => !IsText(r.gameObject)))
                text.Append(BoardScene.PathOf(renderer.transform)).Append(':').Append(String.Join(",", renderer.sharedMaterials.Select(m => m == null ? "none" : m.GetInstanceID().ToString(CultureInfo.InvariantCulture)))).Append(';');
            return text.ToString();
        }
        static bool IsText(GameObject g) => g.GetComponent<TMPro.TMP_Text>() != null || g.GetComponent<TMPro.TMP_SubMesh>() != null;
    }
}
