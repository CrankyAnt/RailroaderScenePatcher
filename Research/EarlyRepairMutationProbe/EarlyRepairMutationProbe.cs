// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Railroader.Modding;
using UnityEngine;

public sealed class EarlyRepairEntry : IModEntryPoint, IWorldLifecycle
{
    private Harmony harmony;
    public void OnModLoad(IModContext context)
    {
        EarlyRepairObserver.LogPath = Path.Combine(context.ModDirectory, "EarlyRepairMutationProbe.log");
        EarlyRepairObserver.Write("OnModLoad");
        var marker = AccessTools.TypeByName("Track.TrackMarker") ?? throw new InvalidOperationException("TrackMarker missing");
        var method = AccessTools.Method(marker, "OnEnable") ?? throw new InvalidOperationException("OnEnable missing");
        harmony = new Harmony("crankyant.early-repair-mutation-probe");
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(EarlyRepairObserver), nameof(EarlyRepairObserver.Before)), postfix: new HarmonyMethod(typeof(EarlyRepairObserver), nameof(EarlyRepairObserver.After)));
        EarlyRepairObserver.Write("patch installed");
    }
    public void OnWorldLoad() { EarlyRepairObserver.Write("OnWorldLoad"); EarlyRepairObserver.EmitControl(); }
    public void OnWorldUnload() { EarlyRepairObserver.Write("OnWorldUnload"); }
    public void OnModUnload() { harmony?.UnpatchAll("crankyant.early-repair-mutation-probe"); EarlyRepairObserver.Write("OnModUnload"); }
}
public static class EarlyRepairObserver
{
    public static string LogPath;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    public static void Write(string text) { try { File.AppendAllText(LogPath, DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine); } catch { } }
    private static object Read(object obj, string name)
    {
        if (obj == null) throw new InvalidOperationException("Missing reflection receiver for " + name);
        var t = obj as Type ?? obj.GetType(); var target = obj is Type ? null : obj;
        var p = t.GetProperty(name, Flags); if (p != null) return p.GetValue(target, null);
        var f = t.GetField(name, Flags); if (f != null) return f.GetValue(target);
        throw new MissingMemberException(t.FullName, name);
    }
    public static void Before(object __instance) { Observe("before", __instance); TryRepair(__instance); }
    public static void After(object __instance) { Observe("after", __instance); VerifyRegistration(__instance); }
    public static void EmitControl()
    {
        if (File.Exists(Path.Combine(Path.GetDirectoryName(LogPath), "SKIP_CONTROL"))) { Write("control skipped"); return; }
        try
        {
            var type = AccessTools.TypeByName("Game.Diagnostics.MessageLog") ?? throw new InvalidOperationException("MessageLog missing");
            var warning = type.GetMethod("Warning", Flags, null, new[] { typeof(string), typeof(string), typeof(string), typeof(string) }, null) ?? throw new MissingMethodException("MessageLog.Warning");
            var message = warning.Invoke(null, new object[] { "Early Repair Mutation Probe", "Research control: simulated patch failure for Scene operations research fixture (crankyant/broken-marker-test-fixture).", "CrankyAnt Research", "issue30-control" });
            Write("control emitted retained=" + (message != null));
        }
        catch (Exception e) { Write("control error=" + e.GetType().Name + " " + e.Message); }
    }
    private static void VerifyRegistration(object marker)
    {
        try
        {
            if (Convert.ToString(Read(marker, "id")) != "M5pp") return;
            var graph = Read(AccessTools.TypeByName("Track.Graph"), "Shared");
            if (graph == null) { Write("registration graph absent"); return; }
            var buckets = Read(graph, "_namedLocationsBySegmentId") as IDictionary ?? throw new InvalidOperationException("graph buckets missing");
            int total = 0, objectMatches = 0, replacementMatches = 0;
            foreach (DictionaryEntry bucket in buckets)
            {
                var entries = bucket.Value as IDictionary ?? throw new InvalidOperationException("graph bucket unsupported");
                foreach (DictionaryEntry entry in entries)
                {
                    if (Convert.ToString(entry.Key) != "M5pp") continue;
                    total++;
                    if (ReferenceEquals(Read(entry.Value, "ObjectValue"), marker)) objectMatches++;
                    if (Convert.ToString(bucket.Key) == "SCrankyAntSceneProbe") replacementMatches++;
                }
            }
            Write("registration total=" + total + " objectMatches=" + objectMatches + " replacementMatches=" + replacementMatches);
        }
        catch (Exception e) { Write("registration error=" + e.GetType().Name + " " + e.Message); }
    }
    private static string ObjectPath(Transform transform) { return transform.parent == null ? transform.name : ObjectPath(transform.parent) + "/" + transform.name; }
    private static void TryRepair(object marker)
    {
        try
        {
            var component = marker as Component;
            if (component == null || Convert.ToString(Read(marker, "id")) != "M5pp") return;
            var data = Path.Combine(Path.GetDirectoryName(LogPath), "repair-case.txt");
            if (!File.Exists(data) || File.ReadAllText(data).Trim() != "M5pp|Shuo|58.69453|A|SCrankyAntSceneProbe") { Write("repair refused: test data absent or invalid"); return; }
            if (Convert.ToString(Read(AccessTools.TypeByName("Game.ActiveMapDefinition"), "Identifier")) != "crankyant/broken-marker-test-map") { Write("repair refused: map mismatch"); return; }
            if (ObjectPath(component.transform) != "World/Roads Sylva/Crossing Unprotected - Allen (1)/Whistle Sign E") { Write("repair refused: path mismatch"); return; }
            var allMarkers = Resources.FindObjectsOfTypeAll(marker.GetType()).OfType<Component>().Where(c => Convert.ToString(Read(c, "id")) == "M5pp").ToArray();
            var markers = allMarkers.Where(c => c.gameObject.scene.IsValid()).ToArray();
            var loadedCount = markers.Count(c => c.gameObject.scene.isLoaded);
            Write("marker candidates all=" + allMarkers.Length + " valid=" + markers.Length + " loaded=" + loadedCount + " targetSceneValid=" + component.gameObject.scene.IsValid() + " targetSceneLoaded=" + component.gameObject.scene.isLoaded + " targetSceneHandle=" + component.gameObject.scene.handle);
            foreach (var candidate in allMarkers) Write("marker candidate instance=" + candidate.GetInstanceID() + " target=" + ReferenceEquals(candidate, marker) + " sceneValid=" + candidate.gameObject.scene.IsValid() + " sceneLoaded=" + candidate.gameObject.scene.isLoaded + " sceneHandle=" + candidate.gameObject.scene.handle + " sceneName=" + candidate.gameObject.scene.name + " path=" + ObjectPath(candidate.transform));
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(LogPath), "ALLOW_MUTATION"))) { Write("repair refused: diagnostic-only mode"); return; }
            if (markers.Length != 1 || !ReferenceEquals(markers[0], marker)) { Write("repair refused: marker not unique"); return; }
            var manager = Read(AccessTools.TypeByName("Model.Database.AssetPackManager"), "Shared");
            var packs = Read(manager, "OrderedPacks") as IEnumerable ?? throw new InvalidOperationException("OrderedPacks unavailable");
            int packCount = 0; foreach (var pack in packs) { var info = Read(pack, "Info"); if (info != null && Convert.ToString(Read(info, "Identifier")) == "crankyant/broken-marker-test-fixture") packCount++; }
            if (packCount != 1) { Write("repair refused: fixture pack not unique"); return; }
            var graphType = AccessTools.TypeByName("Track.Graph") ?? throw new InvalidOperationException("Graph type missing");
            var graph = Read(graphType, "Shared"); if (graph == null) { Write("repair refused: graph absent"); return; }
            var getSegment = graphType.GetMethod("GetSegment", Flags, null, new[] { typeof(string) }, null) ?? throw new MissingMethodException("GetSegment");
            var serial = Read(marker, "_location");
            if (Convert.ToString(Read(serial, "segmentId")) != "Shuo" || Math.Abs(Convert.ToSingle(Read(serial, "distance")) - 58.69453f) > 0.001f || Convert.ToString(Read(serial, "end")) != "A" || getSegment.Invoke(graph, new object[] { "Shuo" }) != null) { Write("repair refused: original mismatch"); return; }
            var destination = getSegment.Invoke(graph, new object[] { "SCrankyAntSceneProbe" });
            if (destination == null || Convert.ToString(Read(Read(destination, "a"), "id")) != "NCrankyAntSceneProbeA" || Convert.ToString(Read(Read(destination, "b"), "id")) != "NCrankyAntSceneProbeB") { Write("repair refused: destination mismatch"); return; }
            var copy = Activator.CreateInstance(serial.GetType());
            foreach (var name in new[] { "segmentId", "distance", "end" }) { var field = serial.GetType().GetField(name, Flags) ?? throw new MissingFieldException(name); field.SetValue(copy, name == "segmentId" ? (object)"SCrankyAntSceneProbe" : field.GetValue(serial)); }
            var resolve = graphType.GetMethod("TryResolveLocation", Flags) ?? throw new MissingMethodException("TryResolveLocation");
            var args = new[] { copy, (object)null };
            if (!(bool)resolve.Invoke(graph, args) || args[1] == null || !(bool)Read(args[1], "IsValid")) { Write("repair refused: destination location invalid"); return; }
            Write("repair preflight original=Shuo/58.69453/A instance=" + component.GetInstanceID() + " testdata=matched");
            var property = marker.GetType().GetProperty("Location", Flags) ?? throw new MissingMemberException("Location");
            property.SetValue(marker, args[1], null);
            Write("repair applied; native check follows");
        }
        catch (Exception e) { Write("repair error=" + e.GetType().Name + " " + e.Message + "; native check follows"); }
    }
    private static void Observe(string phase, object marker)
    {
        try
        {
            if (Convert.ToString(Read(marker, "id")) != "M5pp") return;
            var graphType = AccessTools.TypeByName("Track.Graph") ?? throw new InvalidOperationException("Track.Graph missing");
            var graph = Read(graphType, "Shared");
            var getSegment = graphType.GetMethod("GetSegment", Flags, null, new[] { typeof(string) }, null) ?? throw new MissingMethodException("Track.Graph.GetSegment(string)");
            var destination = graph == null ? null : getSegment.Invoke(graph, new object[] { "SCrankyAntSceneProbe" });
            var map = Read(AccessTools.TypeByName("Game.ActiveMapDefinition"), "Identifier");
            var manager = Read(AccessTools.TypeByName("Model.Database.AssetPackManager"), "Shared");
            string fixture = "manager-absent";
            if (manager != null)
            {
                var packs = Read(manager, "OrderedPacks") as IEnumerable ?? throw new InvalidOperationException("OrderedPacks unavailable");
                int matches = 0;
                foreach (var pack in packs)
                {
                    var info = Read(pack, "Info");
                    if (info != null && Convert.ToString(Read(info, "Identifier")) == "crankyant/broken-marker-test-fixture") matches++;
                }
                fixture = matches.ToString();
            }
            var serial = Read(marker, "_location");
            var component = marker as Component;
            Write(phase + " id=M5pp instance=" + (component == null ? "?" : component.GetInstanceID().ToString()) + " map=" + map + " fixturePackMatches=" + fixture + " graph=" + (graph != null) + " destination=" + (destination != null) + " locationPresent=" + (Read(marker, "Location") != null) + " oldSegment=" + Read(serial, "segmentId"));
        }
        catch (Exception e) { Write(phase + " observer error=" + e.GetType().Name + " " + e.Message); }
    }
}
