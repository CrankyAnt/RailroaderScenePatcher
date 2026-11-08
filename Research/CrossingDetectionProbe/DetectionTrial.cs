// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CrankyAnt.CrossingDetectionResearch
{
    // The caller must guard the disposable world, save and train clearance before every mutation.
    public sealed class DetectionTrial
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        readonly Action<string> log;
        readonly Func<Component> currentController;
        readonly Component controller;
        readonly object graph, trains;
        readonly IList sets;
        readonly object[] originalSets, originalSpans;
        readonly string[] originalEndpoints;
        readonly List<Component> added = new List<Component>();
        GameObject root;
        object addedSet;
        static Type FindType(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null); }
        static object Read(object target, string name) { var t = target as Type ?? target.GetType(); var p = t.GetProperty(name, Flags); return p != null ? p.GetValue(target is Type ? null : target, null) : t.GetField(name, Flags).GetValue(target is Type ? null : target); }
        static void Field(object target, string name, object value) { target.GetType().GetField(name, Flags).SetValue(target, value); }
        static object Invoke(object target, string name, params object[] args) { return target.GetType().GetMethod(name, Flags).Invoke(target, args); }
        static object[] SpanRefs(object set) { return new[] { Read(set, "approachA"), Read(set, "island"), Read(set, "approachB") }; }
        object Lookup(string id) { return Invoke(graph, "SpanForId", id); }
        string Id(object span) { return Convert.ToString(Read(span, "id")); }
        public DetectionTrial(Component existingController, Func<Component> resolveController, Action<string> write)
        {
            controller = existingController ?? throw new ArgumentNullException(nameof(existingController)); currentController = resolveController ?? throw new ArgumentNullException(nameof(resolveController)); log = write ?? throw new ArgumentNullException(nameof(write));
            graph = Read(FindType("Track.Graph"), "Shared") ?? throw new InvalidOperationException("Graph missing");
            trains = Read(FindType("TrainController"), "Shared") ?? throw new InvalidOperationException("TrainController missing");
            sets = (IList)Read(controller, "circuitSets"); originalSets = sets.Cast<object>().ToArray(); originalSpans = originalSets.SelectMany(SpanRefs).ToArray();
            if (originalSets.Length != 2 || originalSpans.Length != 6 || originalSpans.Distinct().Count() != 6) throw new InvalidOperationException("Unexpected baseline topology");
            originalEndpoints = originalSpans.Select(Endpoints).ToArray();
            VerifyOriginals();
        }
        static string Endpoints(object span) { var l = Read(span, "_lower"); var u = Read(span, "_upper"); return Read(l, "segmentId") + "/" + Read(l, "end") + "/" + Read(l, "distance") + "|" + Read(u, "segmentId") + "/" + Read(u, "end") + "/" + Read(u, "distance"); }
        void VerifyOriginals()
        {
            if (controller == null || !controller.gameObject.scene.IsValid() || !controller.gameObject.scene.isLoaded || !ReferenceEquals(currentController(), controller) || !ReferenceEquals(Read(FindType("Track.Graph"), "Shared"), graph) || !ReferenceEquals(Read(FindType("TrainController"), "Shared"), trains) || !ReferenceEquals(Read(controller, "circuitSets"), sets)) throw new InvalidOperationException("World identity changed.");
            if (sets.Count != originalSets.Length + (addedSet == null ? 0 : 1) || !sets.Cast<object>().Take(originalSets.Length).SequenceEqual(originalSets) || !originalSets.SelectMany(SpanRefs).SequenceEqual(originalSpans) || (addedSet != null && !ReferenceEquals(sets[sets.Count - 1], addedSet))) throw new InvalidOperationException("Original membership changed");
            for (int i = 0; i < originalSpans.Length; i++) { var span = originalSpans[i]; if (!ReferenceEquals(Lookup(Id(span)), span) || !(bool)Read(span, "IsValid") || Endpoints(span) != originalEndpoints[i]) throw new InvalidOperationException("Original span changed, missing or invalid: " + Id(span)); }
        }
        void RequireClear(IEnumerable<object> spans) { foreach (var s in spans) if ((bool)Invoke(trains, "AnyCarsOnSpan", s)) throw new InvalidOperationException("Occupied span: " + Id(s)); }
        object Location(Endpoint e)
        {
            var t = FindType("Track.SerializableLocation"); var value = Activator.CreateInstance(t);
            Field(value, "segmentId", e.Segment); Field(value, "distance", e.Distance);
            var end = t.GetField("end", Flags); end.SetValue(value, Enum.Parse(end.FieldType, e.End)); return value;
        }
        public void Add(SpanInput[] inputs)
        {
            if (root != null || inputs == null || inputs.Length != 3) throw new InvalidOperationException("Exactly three new spans required for this trial");
            VerifyOriginals(); RequireClear(originalSpans);
            if (inputs.Any(x => x == null || x.Lower == null || x.Upper == null || string.IsNullOrWhiteSpace(x.Id) || !x.Id.StartsWith("CrankyAntDetection21_", StringComparison.Ordinal)) || inputs.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != 3 || inputs.Any(x => Lookup(x.Id) != null)) throw new InvalidOperationException("New IDs missing, duplicate or registered");
            root = new GameObject("CrankyAnt detection research");
            root.SetActive(false);
            try
            {
                var spanType = FindType("Track.TrackSpan");
                foreach (var input in inputs)
                {
                    var go = new GameObject(input.Id); go.transform.SetParent(root.transform, false);
                    var span = go.AddComponent(spanType); added.Add(span); Field(span, "id", input.Id); Field(span, "_lower", Location(input.Lower)); Field(span, "_upper", Location(input.Upper));
                    if (!(bool)Read(span, "IsValid")) throw new InvalidOperationException("Invalid endpoints: " + input.Id);
                    var points = ((IEnumerable)Invoke(span, "GetPoints")).Cast<object>().ToArray(); var segments = ((IEnumerable)Invoke(span, "GetSegments")).Cast<object>().ToArray();
                    if (points.Length < 2 || segments.Length == 0 || Convert.ToSingle(Read(span, "Length")) <= 0) throw new InvalidOperationException("Empty coverage: " + input.Id);
                    log("coverage id=" + input.Id + " valid=true length=" + Read(span, "Length") + " points=" + string.Join(";", points.Select(p => p.ToString())) + " segments=" + string.Join(",", segments.Select(s => Convert.ToString(Read(s, "id")))));
                }
                RequireClear(added.Cast<object>()); root.SetActive(true);
                foreach (var span in added) if (!ReferenceEquals(Lookup(Id(span)), span)) throw new InvalidOperationException("Registration mismatch: " + Id(span));
                var setType = sets.GetType().GetGenericArguments()[0]; var candidate = Activator.CreateInstance(setType);
                Field(candidate, "approachA", added[0]); Field(candidate, "island", added[1]); Field(candidate, "approachB", added[2]); sets.Add(candidate); addedSet = candidate;
                VerifyOriginals(); log("added membership=" + string.Join(",", added.Select(Id)));
            }
            catch { if (root != null && !root.activeSelf) { UnityEngine.Object.Destroy(root); root = null; added.Clear(); } else Remove(); throw; }
        }
        public void Sample()
        {
            VerifyOriginals();
            foreach (var span in added) if (!ReferenceEquals(Lookup(Id(span)), span)) throw new InvalidOperationException("Registration changed");
            log("sample phase=" + (addedSet == null ? "baseline" : "added") + " " + string.Join(" ", originalSpans.Concat(added.Cast<object>()).Select(s => Id(s) + "=" + Invoke(trains, "AnyCarsOnSpan", s))) + " lights=" + Read(controller, "SignalLights") + " gates=" + Read(controller, "SignalGates") + " bell=" + Read(controller, "SignalBell"));
        }
        public void Remove()
        {
            if (root == null) return;
            VerifyOriginals();
            RequireClear(originalSpans.Concat(added.Cast<object>().Where(s => (bool)Read(s, "IsValid"))));
            if (addedSet != null && !ReferenceEquals(sets[sets.Count - 1], addedSet)) throw new InvalidOperationException("Added membership changed");
            foreach (var span in added) { var current = Lookup(Id(span)); if (!ReferenceEquals(current, span)) throw new InvalidOperationException("ID belongs to another span; refuse disable"); }
            VerifyOriginals();
            if (addedSet != null) { sets.RemoveAt(sets.Count - 1); addedSet = null; }
            root.SetActive(false);
            foreach (var span in added) if (Lookup(Id(span)) != null) throw new InvalidOperationException("Added registration remained");
            UnityEngine.Object.Destroy(root); root = null; added.Clear(); VerifyOriginals(); log("removed; original references and registrations retained");
        }
    }
    [Serializable] public sealed class Endpoint { public string Segment; public string End; public float Distance; }
    [Serializable] public sealed class SpanInput { public string Id; public Endpoint Lower; public Endpoint Upper; }
}
