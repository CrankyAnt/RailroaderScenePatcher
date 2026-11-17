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
    // milepost itself listens to its marker's location changes only to call UpdateMileage, which does nothing on the
    // checked build, and keeps its marker in a private property; those two consumers belong to the pattern. The game's
    // origin cache (MilePost._originMilePosts) maps a prefix to the origin milepost object found on the first lookup; it
    // holds the object, not its location, so an edit leaves it alone and the scene checks that it did. The displayed text
    // is authored on the text synchronizer and its rendered texts; nothing recalculates it from the binding.
    internal sealed class MilepostWorld : TrackMarkerWorld, IMilepostWorld {
        internal const string SynchronizerType = "Helpers.TextSynchronizer";
        readonly Type post;
        readonly FieldInfo prefix, origin, mileage, originCache, synchronizerText;
        internal MilepostWorld() {
            post = Native.Type(Milepost) ?? throw new InvalidOperationException("Milepost prerequisites unavailable");
            var synchronizer = Native.Type(SynchronizerType) ?? throw new InvalidOperationException("Milepost text synchronizer unavailable");
            prefix = Declared(post, "prefix", typeof(string)); origin = Declared(post, "origin", typeof(bool)); mileage = Declared(post, "mileage", typeof(float));
            synchronizerText = Declared(synchronizer, "text", typeof(string));
            originCache = post.GetField("_originMilePosts", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (originCache == null || !typeof(IDictionary).IsAssignableFrom(originCache.FieldType)) throw new MissingFieldException(Milepost, "_originMilePosts");
            // The milepost's location listener calls UpdateMileage. The capability relies on it recalculating nothing; a build
            // whose UpdateMileage has a body is a different milepost and this capability is incompatible with it.
            var update = post.GetMethod("UpdateMileage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) ?? throw new MissingMethodException(Milepost, "UpdateMileage");
            var body = update.GetMethodBody()?.GetILAsByteArray();
            if (body == null || body.Length > 2) throw new InvalidOperationException("Milepost UpdateMileage recalculates on this build; the milepost capability does not support it");
            if (post.GetMethod("TrackMarkerOnOnLocationChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null) == null) throw new MissingMethodException(Milepost, "TrackMarkerOnOnLocationChanged");
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
            if (!Loaded(go)) return "This object has no implemented milepost capability";
            var components = go.GetComponents<Component>();
            if (components.Any(c => c == null)) return "Missing script on the milepost object";
            var posts = components.Where(c => c.GetType().FullName == Milepost).ToArray();
            if (posts.Length != 1) return posts.Length == 0 ? "The object holds no milepost" : "The object holds several mileposts";
            var markers = components.Where(c => c.GetType().FullName == MarkerType).ToArray();
            if (markers.Length != 1) return markers.Length == 0 ? "The milepost holds no track marker" : "The milepost holds several track markers";
            if (Get(go, PadBuilder) != null) return "The milepost holds a crossing-pad builder; that combination is not supported";
            if (Get(go, SynchronizerType) == null) return "The milepost holds no text synchronizer";
            var m = markers[0];
            var type = System.Convert.ToString(Field(m, "type"));
            if (type != "Generic") return "Only mileposts with a Generic track marker are supported; this marker is " + type;
            var milepost = posts[0];
            // The milepost's own listener and property belong to the pattern, as the colocated relative position's reference does.
            return Consumers(go, m, references, setups,
                (owner, field) => owner is Component c && c.gameObject == go && ((c.GetType().FullName == Relative && field == "_marker") || ReferenceEquals(c, milepost)),
                placementOnly, listener => ReferenceEquals(listener.Target, milepost) && listener.Method.Name == "TrackMarkerOnOnLocationChanged");
        }
        MilepostState IMilepostWorld.Milepost(object target) {
            var go = (GameObject)target;
            var component = go.GetComponent(post) ?? throw new InvalidOperationException("The object no longer holds a milepost");
            var name = (string)prefix.GetValue(component);
            var cache = (IDictionary)originCache.GetValue(null);
            string cached;
            if (cache == null || name == null || !cache.Contains(name)) cached = MilepostState.NotCached;
            else cached = MilepostState.CachedAs(cache[name] is Component held && held != null ? held.gameObject.scene.name + "/" + BoardScene.PathOf(held.transform) : null);
            return new MilepostState(name, (bool)origin.GetValue(component), (float)mileage.GetValue(component), Text(go), Model(go), cached);
        }
        // The synchronizer's text and every rendered text below the post, in hierarchy order.
        string Text(GameObject go) {
            var text = new StringBuilder();
            var synchronizer = Get(go, SynchronizerType);
            text.Append(synchronizer == null ? "" : (string)synchronizerText.GetValue(synchronizer));
            foreach (var rendered in go.GetComponentsInChildren<TMPro.TMP_Text>(true)) text.Append('|').Append(rendered.text);
            return text.ToString();
        }
        // The meshes and materials below the post, by path and asset identity. Shared meshes and materials are read without
        // instantiating them.
        static string Model(GameObject go) {
            var text = new StringBuilder();
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
                text.Append(BoardScene.PathOf(filter.transform)).Append('=').Append(filter.sharedMesh == null ? "none" : filter.sharedMesh.GetInstanceID().ToString(CultureInfo.InvariantCulture)).Append(';');
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                text.Append(BoardScene.PathOf(renderer.transform)).Append(':').Append(String.Join(",", renderer.sharedMaterials.Select(m => m == null ? "none" : m.GetInstanceID().ToString(CultureInfo.InvariantCulture)))).Append(';');
            return text.ToString();
        }
    }
}
