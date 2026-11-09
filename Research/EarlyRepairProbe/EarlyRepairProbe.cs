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
        EarlyRepairObserver.LogPath = Path.Combine(context.ModDirectory, "EarlyRepairObserver.log");
        EarlyRepairObserver.Write("OnModLoad");
        var marker = AccessTools.TypeByName("Track.TrackMarker") ?? throw new InvalidOperationException("TrackMarker missing");
        var method = AccessTools.Method(marker, "OnEnable") ?? throw new InvalidOperationException("OnEnable missing");
        harmony = new Harmony("crankyant.early-repair-observer");
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(EarlyRepairObserver), nameof(EarlyRepairObserver.Before)), postfix: new HarmonyMethod(typeof(EarlyRepairObserver), nameof(EarlyRepairObserver.After)));
        EarlyRepairObserver.Write("patch installed");
    }
    public void OnWorldLoad() { EarlyRepairObserver.Write("OnWorldLoad"); }
    public void OnWorldUnload() { EarlyRepairObserver.Write("OnWorldUnload"); }
    public void OnModUnload() { harmony?.UnpatchAll("crankyant.early-repair-observer"); EarlyRepairObserver.Write("OnModUnload"); }
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
    public static void Before(object __instance) { Observe("before", __instance); }
    public static void After(object __instance) { Observe("after", __instance); }
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
