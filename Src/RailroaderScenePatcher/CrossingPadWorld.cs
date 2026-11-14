// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // The crossing-pad pattern: a track marker whose colocated Track.TrackCurveMeshBuilder generates the pad mesh from
    // the marker location. The builder generates once, when its culling sphere first becomes visible or near, and
    // never again on its own: a rebinding leaves the old mesh in place until Generate runs. Generate reads the marker's
    // graph curves, replaces the mesh container's children, offsets the mesh by the pad's map position and moves the
    // culling sphere to the new bounds. The new renderer starts enabled; queuing the culling token lets the native
    // culling recompute its visibility on the next update.
    internal sealed class CrossingPadWorld : TrackMarkerWorld, ICrossingPadWorld {
        const string BuilderBase = "BezierCurveMesh.CurveMeshBuilderBase", Manager = "Helpers.Culling.CullingManager";
        readonly FieldInfo renderers, token, boundsCenter, boundsRadius, spheres, needsUpdate;
        readonly MethodInfo generate, curves;
        readonly FieldInfo[] curvePoints;
        // What the adapter generated each builder from, valid while the builder still holds that geometry.
        sealed class Record { internal string Signature, Curves, CurveToken; internal TrackLocation Source; internal double[] Origin; }
        // The signature of the renderer array a builder last held. Generation always assigns a new array, so the same array
        // holds the same meshes and the signature is not computed again.
        sealed class Seen { internal Renderer[] Renderers; internal string Signature; }
        readonly ConditionalWeakTable<Component, Seen> signatures = new ConditionalWeakTable<Component, Seen>();
        readonly MethodInfo connected, otherNode, segmentLength;
        readonly PropertyInfo segmentCurve;
        readonly FieldInfo nodeA, nodeB, nodeId, segmentId, width;
        readonly ConditionalWeakTable<Component, Record> records = new ConditionalWeakTable<Component, Record>();
        internal CrossingPadWorld() {
            var pad = Native.Type(PadBuilder); var builder = Native.Type(BuilderBase); var manager = Native.Type(Manager);
            if (pad == null || builder == null || manager == null || !builder.IsAssignableFrom(pad)) throw new InvalidOperationException("Crossing-pad prerequisites unavailable");
            generate = builder.GetMethod("Generate", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null) ?? throw new MissingMethodException(BuilderBase, "Generate()");
            // The pad builder's own curve query: the graph curves within half the pad width around the marker location, in map coordinates.
            curves = pad.GetMethod("GetCurves", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null) ?? throw new MissingMethodException(PadBuilder, "GetCurves()");
            var curve = curves.ReturnType.IsGenericType ? curves.ReturnType.GetGenericArguments()[0] : null;
            curvePoints = curve == null ? null : new[] { "P0", "P1", "P2", "P3", "_up0", "_up3" }.Select(n => curve.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).ToArray();
            if (curvePoints == null || curvePoints.Any(f => f == null || f.FieldType != typeof(Vector3)) || !typeof(IEnumerable).IsAssignableFrom(curves.ReturnType)) throw new MissingMemberException(PadBuilder, "GetCurves() curve points");
            width = Declared(pad, "width");
            var graph = Native.Type("Track.Graph"); var segment = Native.Type("Track.TrackSegment"); var node = Native.Type("Track.TrackNode");
            if (graph == null || segment == null || node == null) throw new InvalidOperationException("Track curve prerequisites unavailable");
            connected = graph.GetMethod("SegmentsConnectedTo", BindingFlags.Instance | BindingFlags.Public, null, new[] { node }, null) ?? throw new MissingMethodException("Track.Graph", "SegmentsConnectedTo");
            otherNode = segment.GetMethod("GetOtherNode", BindingFlags.Instance | BindingFlags.Public, null, new[] { node }, null) ?? throw new MissingMethodException("Track.TrackSegment", "GetOtherNode");
            segmentLength = segment.GetMethod("GetLength", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null) ?? throw new MissingMethodException("Track.TrackSegment", "GetLength");
            segmentCurve = segment.GetProperty("Curve", BindingFlags.Instance | BindingFlags.Public) ?? throw new MissingMemberException("Track.TrackSegment", "Curve");
            // The raw segment curves of the token have the type of the builder's curves, so they share the point fields.
            if (segmentCurve.PropertyType != curve) throw new MissingMemberException("Track.TrackSegment", "Curve of the builder curve type");
            nodeA = Declared(segment, "a"); nodeB = Declared(segment, "b"); segmentId = Declared(segment, "id"); nodeId = Declared(node, "id");
            renderers = Declared(builder, "_renderers"); token = Declared(builder, "_cullingToken");
            boundsCenter = Declared(builder, "_boundsCenterLocal"); boundsRadius = Declared(builder, "_boundsRadius");
            foreach (var name in new[] { "meshContainer", "meshProfile" }) Declared(builder, name);
            Declared(pad, "_marker");
            if (renderers.FieldType != typeof(Renderer[]) || boundsCenter.FieldType != typeof(Vector3) || boundsRadius.FieldType != typeof(float)) throw new MissingMemberException(BuilderBase, "generated renderer and bounds state");
            spheres = Declared(manager, "_spheres"); needsUpdate = Declared(manager, "_needsUpdate");
            var tokenType = token.FieldType;
            if (tokenType.GetProperty("Index") == null || tokenType.GetField("Manager") == null || tokenType.GetProperty("IsRemoved") == null || spheres.FieldType != typeof(BoundingSphere[]) || needsUpdate.FieldType.GetMethod("Add", new[] { tokenType }) == null)
                throw new MissingMemberException(Manager, "culling token state");
        }
        static FieldInfo Declared(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) ?? throw new MissingFieldException(type.FullName, name);
        protected override bool Belongs(GameObject g) => Get(g, PadBuilder) != null;
        public override string PatternProblem(object target) => PadProblem(target as GameObject, CurrentReferrers(), SetupHolders());
        public override string PlacementProblem(object target) => PadProblem(target as GameObject, null, null, true);
        // Why the object does not conform to the supported crossing-pad pattern, or null. Shared with the inventory.
        internal static string PadProblem(GameObject go, ReferenceScan references, HolderIndex setups, bool placementOnly = false) {
            if (!Loaded(go)) return "This object has no implemented crossing-pad capability";
            var markers = go.GetComponents<Component>().Where(c => c != null && c.GetType().FullName == MarkerType).ToArray();
            if (markers.Length != 1) return markers.Length == 0 ? "The object holds no track marker" : "The object holds several track markers";
            if (go.GetComponents<Component>().Any(c => c == null)) return "Missing script on the pad object";
            var builders = go.GetComponents<Component>().Where(c => c.GetType().FullName == PadBuilder).ToArray();
            if (builders.Length != 1) return builders.Length == 0 ? "The object holds no crossing-pad builder" : "The object holds several crossing-pad builders";
            if (Get(go, Milepost) != null) return "Milepost markers keep their milepost-specific slice; this pad operation does not apply";
            var m = markers[0];
            var type = System.Convert.ToString(Field(m, "type"));
            if (type != "Crossing" && type != "Generic") return "Only Crossing and Generic pad markers are supported; this marker is " + type;
            if (Field(builders[0], "meshContainer") as Transform == null || Field(builders[0], "meshProfile") as UnityEngine.Object == null)
                return "The pad builder has no mesh container or mesh profile; native generation cannot produce its geometry";
            // Group-driven activity is supported for Generic markers only; a pad has no observed group-following case.
            var relative = Get(go, Relative);
            if (relative != null && Field(relative, "targetActiveFollowsTrackGroup") is bool follows && follows)
                return "The pad's activity follows a track group; the crossing-pad operation does not support group-driven activity";
            // The colocated relative position and the pad builder hold their marker; any other holder may cache what it read.
            return Consumers(go, m, references, setups, (owner, field) => owner is Component c && c.gameObject == go &&
                ((c.GetType().FullName == Relative && field == "_marker") || (c.GetType().FullName == PadBuilder && field == "_marker")), placementOnly);
        }
        Component Builder(object target) => Get((GameObject)target, PadBuilder) ?? throw new InvalidOperationException("The object no longer holds a crossing-pad builder");
        public PadGeometry Geometry(object target) {
            var builder = Builder(target);
            var signature = Signature(builder);
            Record record = null;
            if (signature != null && records.TryGetValue(builder, out var known) && known.Signature == signature) record = known;
            return new PadGeometry(signature, record?.Source, record?.Origin, CullingAligned(builder), record?.Curves, record?.CurveToken);
        }
        // A content signature of the generated renderers: vertex count, an FNV-1a hash of the local vertex positions,
        // local bounds and the mesh's map position. Null when the builder holds no generated renderer.
        string Signature(Component builder) {
            var generated = renderers.GetValue(builder) as Renderer[];
            if (generated == null || generated.Length == 0) return null;
            if (signatures.TryGetValue(builder, out var seen) && ReferenceEquals(seen.Renderers, generated) && generated.All(r => r != null)) return seen.Signature;
            var signature = Compute(generated);
            signatures.Remove(builder);
            signatures.Add(builder, new Seen { Renderers = generated, Signature = signature });
            return signature;
        }
        string Compute(Renderer[] generated) {
            var text = new StringBuilder();
            foreach (var renderer in generated) {
                if (renderer == null) throw new InvalidOperationException("A generated pad renderer was destroyed outside generation");
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) throw new InvalidOperationException("A generated pad renderer has no mesh");
                if (!mesh.isReadable) throw new InvalidOperationException("The generated pad mesh is not readable; its geometry cannot be verified");
                ulong hash = 14695981039346656037UL;
                foreach (var v in mesh.vertices)
                    foreach (var f in new[] { v.x, v.y, v.z })
                        foreach (var b in BitConverter.GetBytes(f)) { hash ^= b; hash *= 1099511628211UL; }
                var at = Position(renderer.gameObject);
                var bounds = mesh.bounds;
                text.Append(mesh.vertexCount.ToString(CultureInfo.InvariantCulture)).Append(':').Append(hash.ToString("X16", CultureInfo.InvariantCulture))
                    .Append(':').Append(Round(bounds.center)).Append(':').Append(Round(bounds.extents)).Append('@').Append(Round(new Vector3((float)at[0], (float)at[1], (float)at[2]))).Append(';');
            }
            return text.ToString();
        }
        static string Round(Vector3 v) => String.Join(",", new[] { v.x, v.y, v.z }.Select(x => Math.Round(x, 2).ToString("0.00", CultureInfo.InvariantCulture)));
        static string Millimetres(Vector3 v) => String.Join(",", new[] { v.x, v.y, v.z }.Select(x => Math.Round(x, 3).ToString("0.000", CultureInfo.InvariantCulture)));
        // Native generation sets the sphere to the pad position plus the local bounds center, with the bounds radius.
        // A pad without a live culling registration, such as a disabled pad, has nothing to align.
        bool CullingAligned(Component builder) {
            var current = token.GetValue(builder);
            if (current == null || (bool)Native.Read(current, "IsRemoved")) return true;
            var index = (int)Native.Read(current, "Index");
            var all = (BoundingSphere[])spheres.GetValue(Native.Read(current, "Manager"));
            if (index < 0 || index >= all.Length) return false;
            var expected = builder.transform.position + (Vector3)boundsCenter.GetValue(builder);
            return (all[index].position - expected).magnitude <= 0.01f && Math.Abs(all[index].radius - (float)boundsRadius.GetValue(builder)) <= 0.01f;
        }
        // The control points and end up-vectors of every curve the builder reads, rounded to millimetres. GetCurves logs an
        // error for a location that does not resolve, so it is read only for a resolvable one.
        public string SourceCurves(object target) {
            var location = ResolvedLocation(target);
            if (location == null || Field(location, "segment") == null) return null;
            var text = new StringBuilder();
            var builder = Builder(target);
            foreach (var curve in (IEnumerable)curves.Invoke(builder, null)) {
                foreach (var point in curvePoints) text.Append(Millimetres((Vector3)point.GetValue(curve))).Append(' ');
                text.Append(';');
            }
            var result = text.ToString();
            // Unchanged curves under a changed token: the token confirms them from now on, so they are not built again.
            if (records.TryGetValue(builder, out var record) && record.Curves == result) record.CurveToken = CurveToken(target);
            return result;
        }
        // The raw curves of every segment the builder can reach within half the pad width plus one metre of the marker
        // location, and the segments connected at each node it reaches, which decide the builder's next segment. This
        // reads the cached segment curves and the graph's connection lists; it builds no curve and searches no scene.
        public string CurveToken(object target) {
            var location = ResolvedLocation(target);
            if (location == null) return null;
            // Track segments and nodes are plain graph objects, not Unity objects.
            var start = Field(location, "segment");
            if (start == null) return null;
            var builder = Builder(target);
            var reach = (float)width.GetValue(builder) / 2 + 1f;
            var length = (float)segmentLength.Invoke(start, null);
            var distance = (float)Field(location, "distance");
            var fromA = System.Convert.ToString(Field(location, "end")) == "A" ? distance : length - distance;
            var graph = Native.Read(Native.Type("Track.Graph"), "Shared") ?? throw new InvalidOperationException("The track graph is unavailable");
            var text = new StringBuilder();
            var visited = new HashSet<object>();
            Segment(start, text, visited);
            Reach(graph, nodeA.GetValue(start), reach - fromA, start, text, visited);
            Reach(graph, nodeB.GetValue(start), reach - (length - fromA), start, text, visited);
            return text.ToString();
        }
        void Segment(object segment, StringBuilder text, HashSet<object> visited) {
            visited.Add(segment);
            text.Append(segmentId.GetValue(segment)).Append('=');
            var curve = segmentCurve.GetValue(segment, null);
            foreach (var point in curvePoints) text.Append(Millimetres((Vector3)point.GetValue(curve))).Append(' ');
            text.Append(';');
        }
        void Reach(object graph, object node, float remaining, object from, StringBuilder text, HashSet<object> visited) {
            if (node == null || remaining <= 0) return;
            var segments = ((IEnumerable)connected.Invoke(graph, new[] { node })).Cast<object>().ToList();
            text.Append('[').Append(nodeId.GetValue(node)).Append(':').Append(String.Join(",", segments.Select(s => s == null ? "missing" : (string)segmentId.GetValue(s)))).Append(']');
            foreach (var next in segments) {
                if (next == null || ReferenceEquals(next, from) || visited.Contains(next)) continue;
                Segment(next, text, visited);
                Reach(graph, otherNode.Invoke(next, new[] { node }), remaining - (float)segmentLength.Invoke(next, null), next, text, visited);
            }
        }
        public void Generate(object target) {
            var builder = Builder(target);
            generate.Invoke(builder, null);
            var signature = Signature(builder);
            records.Remove(builder);
            if (signature != null) records.Add(builder, new Record { Signature = signature, Source = Location(target), Origin = Position(target), Curves = SourceCurves(target), CurveToken = CurveToken(target) });
            var current = token.GetValue(builder);
            if (current != null && !(bool)Native.Read(current, "IsRemoved")) {
                var queue = needsUpdate.GetValue(Native.Read(current, "Manager"));
                needsUpdate.FieldType.GetMethod("Add", new[] { token.FieldType }).Invoke(queue, new[] { current });
            }
        }
    }
}
