// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public sealed partial class BridgeContactProbe
{
    private string sceneryDependencyBaseline;
    private string trestleBaseline;
    private int trestleBaselineCount;

    // Capture referenced targets as well as descendants of the scenery component.
    // The component can refer to objects outside its own hierarchy.
    private void CaptureSceneryDependencies()
    {
        sceneryDependencyBaseline = null;
        trestleBaseline = null;
        var seen = new HashSet<int>(states.Select(s => s.Node.GetInstanceID()));
        sceneryDependencyBaseline = DescribeSceneryDependencies(t =>
        {
            foreach (var node in t.GetComponentsInChildren<Transform>(true))
                if (seen.Add(node.GetInstanceID())) states.Add(new NodeState(node));
        });
        Write("scenery dependency baseline captured");
    }

    private void SnapshotSceneryDependencies(string phase)
    {
        if (sceneryDependencyBaseline == null)
            throw new InvalidOperationException("Scenery dependencies were not captured.");
        string current = DescribeSceneryDependencies(null);
        bool matches = current == sceneryDependencyBaseline;
        Write(phase + " scenery dependencies equalBaseline=" + matches);
        SnapshotNearbyTrestles(phase);
        if (phase == "restored immediate" && !matches)
            throw new InvalidOperationException("Scenery dependencies differ from baseline. Never save.");
    }

    private void SnapshotNearbyTrestles(string phase)
    {
        const float radius = 250f;
        Vector3 center = states.First(s => s.Node == bridge).GamePosition;
        var candidates = Resources.FindObjectsOfTypeAll(TypeNamed("AutoTrestle.AutoTrestle"))
            .OfType<Component>().Where(c => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded)
            .OrderBy(c => c.GetInstanceID()).ToArray();
        var result = new StringBuilder();
        int selected = 0;
        foreach (var c in candidates)
        {
            var points = Field(c, "controlPoints") as IEnumerable;
            if (points == null) throw new InvalidOperationException("Trestle controlPoints unavailable: " + PathOf(c.transform));
            var localPoints = new List<Vector3>();
            var pointDetails = new StringBuilder();
            foreach (var point in points)
            {
                if (point == null) throw new InvalidOperationException("Null trestle control point: " + PathOf(c.transform));
                var p = (Vector3)Field(point, "position");
                var q = (Quaternion)Field(point, "rotation");
                localPoints.Add(p);
                pointDetails.Append(" local=").Append(V(p)).Append(" rotation=").Append(Q(q));
            }
            var gamePoints = localPoints.Select(p => ConvertPoint("WorldToGame", c.transform.TransformPoint(p))).ToArray();
            bool nearby = Vector3.Distance(ConvertPoint("WorldToGame", c.transform.position), center) <= radius
                || gamePoints.Any(p => Vector3.Distance(p, center) <= radius);
            for (int i = 1; !nearby && i < gamePoints.Length; i++)
            {
                Vector3 a = gamePoints[i - 1], delta = gamePoints[i] - a;
                float fraction = delta.sqrMagnitude == 0f ? 0f : Mathf.Clamp01(Vector3.Dot(center - a, delta) / delta.sqrMagnitude);
                nearby = Vector3.Distance(center, a + fraction * delta) <= radius;
            }
            if (!nearby) continue;
            selected++;
            var matrices = Field(c, "_beamMatrices") as IEnumerable;
            if (matrices == null) throw new InvalidOperationException("Trestle matrices unavailable: " + PathOf(c.transform));
            var matrixText = new StringBuilder();
            int matrixCount = 0;
            foreach (Matrix4x4 matrix in matrices)
            {
                for (int i = 0; i < 16; i++) matrixText.Append(F(matrix[i])).Append(',');
                matrixText.Append(';');
                matrixCount++;
            }
            string digest;
            using (var sha = SHA256.Create())
                digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(matrixText.ToString()))).Replace("-", "");
            object instance = Field(c, "_instanceToken");
            object culling = Field(c, "_cullingToken");
            string cullingState = "null";
            if (culling != null)
            {
                bool removed = (bool)Field(culling, "<IsRemoved>k__BackingField");
                cullingState = "id=" + RuntimeHelpers.GetHashCode(culling) + " removed=" + removed;
                if (!removed)
                {
                    int index = (int)Field(culling, "<Index>k__BackingField");
                    object manager = Field(culling, "Manager");
                    var spheres = (BoundingSphere[])Field(manager, "_spheres");
                    int count = (int)Field(manager, "_sphereCount");
                    if (index < 0 || index >= count || index >= spheres.Length)
                        throw new InvalidOperationException("Invalid trestle culling token index.");
                    cullingState += " sphere=" + V(spheres[index].position) + " radius=" + F(spheres[index].radius);
                }
            }
            string line = "trestle id=" + c.GetInstanceID() + " path=" + PathOf(c.transform)
                + " localPosition=" + V(c.transform.localPosition) + " localRotation=" + Q(c.transform.localRotation)
                + " localScale=" + V(c.transform.localScale) + " gamePosition=" + V(ConvertPoint("WorldToGame", c.transform.position))
                + " worldRotation=" + Q(c.transform.rotation) + " activeSelf=" + c.gameObject.activeSelf
                + " activeHierarchy=" + c.gameObject.activeInHierarchy + " enabled=" + ((Behaviour)c).enabled
                + " controlCount=" + localPoints.Count + pointDetails + " generated=" + Field(c, "_hasGenerated")
                + " beamCount=" + matrixCount + " beamDigestSha256=" + digest
                + " instanceToken=" + (instance == null ? "null" : instance.GetType().FullName + "#" + RuntimeHelpers.GetHashCode(instance))
                + " culling=" + cullingState;
            result.AppendLine(line);
            Write(phase + " " + line);
        }
        if (phase == "baseline") { trestleBaseline = result.ToString(); trestleBaselineCount = selected; }
        bool available = trestleBaseline != null && trestleBaselineCount > 0 && selected > 0;
        Write(phase + " nearby trestles radius=250m candidates=" + candidates.Length + " selected=" + selected
            + " coverage=" + (available ? "sampled" : "unavailable")
            + " equalBaseline=" + (available ? (result.ToString() == trestleBaseline).ToString() : "unavailable")
            + " selection=component-origin-or-control-point-or-straight-control-span");
    }

    private string DescribeSceneryDependencies(Action<Transform> captureTarget)
    {
        if (approach == null) throw new InvalidOperationException("Scenery node unavailable.");
        var components = approach.GetComponents<Component>()
            .Where(c => c != null && c.GetType().FullName == "Game.Mow.TrackConstructionScenery").ToArray();
        if (components.Length != 1)
            throw new InvalidOperationException("Expected exactly one TrackConstructionScenery component.");
        var scenery = components[0];
        var result = new StringBuilder();
        Action<string> record = line => { result.AppendLine(line); Write("scenery dependency " + line); };
        record("component.id=" + scenery.GetInstanceID().ToString(CultureInfo.InvariantCulture));

        var graphType = TypeNamed("Track.Graph");
        var graph = graphType.GetProperty("Shared", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .GetValue(null, null);
        if (graph == null) throw new InvalidOperationException("Track graph unavailable.");
        var getSegment = graphType.GetMethod("GetSegment", Flags, null, new[] { typeof(string) }, null);
        if (getSegment == null) throw new MissingMethodException("Track.Graph", "GetSegment");
        var getUnconstructed = TypeNamed("Game.Mow.TrackConditionController").GetMethod("GetUnconstructedState",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (getUnconstructed == null) throw new MissingMethodException("TrackConditionController", "GetUnconstructedState");
        var ids = Field(scenery, "segmentIds") as string[];
        if (ids == null) throw new InvalidOperationException("Scenery segmentIds is null.");
        record("segment.count=" + ids.Length);
        for (int i = 0; i < ids.Length; i++)
        {
            if (string.IsNullOrEmpty(ids[i])) throw new InvalidOperationException("Empty scenery segment ID at " + i);
            var segment = getSegment.Invoke(graph, new object[] { ids[i] });
            if (segment == null) throw new InvalidOperationException("Unresolved scenery segment " + ids[i]);
            var type = segment.GetType();
            var enabled = type.GetProperty("GroupEnabled", Flags).GetValue(segment, null);
            var construction = type.GetProperty("ConstructionState", Flags).GetValue(segment, null);
            var unconstructed = getUnconstructed.Invoke(null, new[] { segment });
            record("segment[" + i + "] requested=" + ids[i] + " id=" + Field(segment, "id")
                + " groupId=" + (Field(segment, "groupId") ?? "<null>") + " groupEnabled=" + enabled
                + " construction=" + construction + " unconstructed=" + unconstructed);
        }

        var entries = Field(scenery, "entries") as IEnumerable;
        if (entries == null) throw new InvalidOperationException("Scenery entries is null.");
        int entryIndex = 0;
        foreach (var entry in entries)
        {
            if (entry == null) throw new InvalidOperationException("Null scenery entry at " + entryIndex);
            var targets = Field(entry, "targets") as GameObject[];
            if (targets == null) throw new InvalidOperationException("Null scenery targets at " + entryIndex);
            record("entry[" + entryIndex + "] state=" + Field(entry, "trackConstructionState")
                + " condition=" + Field(entry, "condition") + " targetCount=" + targets.Length);
            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (target == null) throw new InvalidOperationException("Null scenery target at " + entryIndex + "/" + i);
                if (!target.scene.IsValid() || !target.scene.isLoaded)
                    throw new InvalidOperationException("Scenery target is outside a loaded scene at " + entryIndex + "/" + i);
                record("target[" + entryIndex + "," + i + "] id=" + target.GetInstanceID()
                    + " path=" + PathOf(target.transform));
                foreach (var node in target.GetComponentsInChildren<Transform>(true))
                    record("target.node id=" + node.GetInstanceID() + " path=" + PathOf(node));
                if (captureTarget != null) captureTarget(target.transform);
            }
            entryIndex++;
        }
        record("entry.count=" + entryIndex);
        return result.ToString();
    }
}
