// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public sealed partial class TerrainOutputProbe
{
    private object terrainManager;
    private EventInfo tileEvent;
    private Delegate tileHandler;
    private int tileBuilds, buildsAtCapture;
    private readonly Dictionary<string, string> terrainBaselines = new Dictionary<string, string>();
    private Vector3 TreeTarget() { return new Vector3(25772.99f, gamePosition.y, 393.795837f); }

    private void GuardIsolatedHome()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.FindIndex(args, x => string.Equals(x, "-homedir", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length || !string.Equals(System.IO.Path.GetFullPath(args[i + 1]).TrimEnd('\\', '/'),
            @"C:\TMP\RailroaderMapResearch\RuntimeHome", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("First home argument is not isolated RuntimeHome.");
        if (!string.Equals(System.IO.Path.GetFullPath(directory).TrimEnd('\\', '/'),
            @"C:\TMP\RailroaderMapResearch\RuntimeHome\Mods\TerrainOutputProbe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected probe context directory.");
    }

    private void SubscribeTerrain()
    {
        UnsubscribeTerrain();
        terrainManager = TypeNamed("Map.Runtime.MapManager").GetProperty("Instance").GetValue(null, null);
        if (terrainManager == null) throw new InvalidOperationException("Map manager unavailable.");
        tileEvent = terrainManager.GetType().GetEvent("OnTileBuilt");
        if (tileEvent == null) throw new MissingMemberException("MapManager.OnTileBuilt");
        tileHandler = Delegate.CreateDelegate(tileEvent.EventHandlerType, this,
            GetType().GetMethod("TileBuilt", Instance));
        tileEvent.AddEventHandler(terrainManager, tileHandler);
        tileBuilds = 0;
        terrainBaselines.Clear();
        Write("terrain build event subscribed; no automatic mutation");
    }

    private void UnsubscribeTerrain()
    {
        if (tileHandler != null && terrainManager != null)
            tileEvent.RemoveEventHandler(terrainManager, tileHandler);
        tileHandler = null;
        tileEvent = null;
        terrainManager = null;
    }

    private void TileBuilt(Bounds bounds)
    {
        tileBuilds++;
        Write("OnTileBuilt count=" + tileBuilds + " gameBounds.center=" + V(bounds.center)
            + " size=" + V(bounds.size) + " frame=" + Time.frameCount);
    }

    private static string Digest(IEnumerable<string> rows)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                string.Join("\n", rows.OrderBy(x => x, StringComparer.Ordinal))))).Replace("-", "");
    }

    private void SampleTerrain(string phase)
    {
        if (terrainManager == null) throw new InvalidOperationException("Terrain event subscription missing.");
        if (phase == "baseline") { buildsAtCapture = tileBuilds; terrainBaselines.Clear(); }
        Write(phase + " output pending=" + Property(terrainManager, "PendingInvalidationCount")
            + " idle=" + terrainManager.GetType().GetMethod("IsTerrainWorkIdle", Instance).Invoke(terrainManager, null)
            + " tileBuildsSinceCapture=" + (tileBuilds - buildsAtCapture) + " frame=" + Time.frameCount
            + " observation=TerrainData-not-rendered-frame");
        var centers = new[] { gamePosition, gamePosition + new Vector3(5, 0, 0), TreeTarget() };
        Terrain originalTerrain = null;
        for (int site = 0; site < centers.Length; site++)
        {
            var point = ConvertPosition("GameToWorld", centers[site]);
            var terrains = Terrain.activeTerrains.Where(t => t != null && t.terrainData != null
                && point.x >= t.transform.position.x && point.z >= t.transform.position.z
                && point.x < t.transform.position.x + t.terrainData.size.x
                && point.z < t.transform.position.z + t.terrainData.size.z).ToArray();
            Write(phase + " site=" + site + " terrainMatches=" + terrains.Length);
            if (terrains.Length != 1) throw new InvalidOperationException("Unique loaded terrain required at each output site.");
            var terrain = terrains[0];
            if (site == 0) originalTerrain = terrain;
            if (site == 2 && terrain != originalTerrain) throw new InvalidOperationException("Tree target is on a different terrain tile.");
            var data = terrain.terrainData;
            var normalized = point - terrain.transform.position;
            float u = normalized.x / data.size.x, v = normalized.z / data.size.z;
            float radius = Convert.ToSingle(Field(mask, "radius"), CultureInfo.InvariantCulture)
                + Convert.ToSingle(Field(mask, "falloff"), CultureInfo.InvariantCulture) + 2;
            var rows = new List<string>();
            int trees = 0;
            foreach (var tree in data.treeInstances)
            {
                var relative = Vector3.Scale(tree.position, data.size) - normalized;
                if (relative.x * relative.x + relative.z * relative.z > radius * radius) continue;
                trees++;
                rows.Add("tree " + V(tree.position) + " " + tree.prototypeIndex + " "
                    + F(tree.widthScale) + " " + F(tree.heightScale) + " " + F(tree.rotation));
            }
            for (int z = -4; z <= 4; z++)
                for (int x = -4; x <= 4; x++)
                {
                    float su = u + x * radius / 4 / data.size.x;
                    float sv = v + z * radius / 4 / data.size.z;
                    if (su >= 0 && sv >= 0 && su <= 1 && sv <= 1)
                        rows.Add("height " + x + " " + z + " " + F(data.GetInterpolatedHeight(su, sv)));
                }
            string key = site.ToString(CultureInfo.InvariantCulture);
            string digest = Digest(rows);
            if (phase == "baseline") terrainBaselines[key] = digest;
            Write(phase + " site=" + site + " terrain=" + PathOf(terrain.transform)
                + " terrainData.id=" + data.GetInstanceID() + " treesInRadius=" + trees
                + " radius=" + F(radius) + " sampledRows=" + rows.Count + " sha256=" + digest
                + " equalsBaseline=" + (terrainBaselines.TryGetValue(key, out var baseline) && baseline == digest)
                + " detailAndGPUOutput=separate-generated-tree-sample");
            SampleGeneratedTrees(phase, terrain, site, point, radius);
        }
    }
}
