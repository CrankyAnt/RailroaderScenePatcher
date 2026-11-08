// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

public sealed partial class MarkerGroups
{
    private object graph, originalLocation, destinationLocation, originalLast;
    private Component marker, relative;
    private Transform target, parent;
    private Vector3 origin, position, localPosition, offset;
    private Quaternion rotation, localRotation;
    private float angle;
    private bool terrain, originalActive, dirty, patched, explicitDisable;
    private string oldGroup, newGroup, markerId;
    private EventInfo groupEvent;
    private Action<string, bool> disableHandler;
    private bool subscribed;
    private int nativeBaseline;
    private int capturedScene;
    private bool autoArmed, autoPending;
    private int worldGeneration;
    public void ToggleAutoArm()
    {
        if (!isolated || dirty || target != null || autoPending) { status = "Restore or release the capture before arming."; return; }
        autoArmed = !autoArmed; Write("auto arm=" + autoArmed + " source=manual-ui");
        status = autoArmed ? "Armed once for the next world load." : "Next-load arm cancelled.";
    }
    public void OnWorldLoaded()
    {
        worldGeneration++;
        if (autoPending) { autoPending = false; autoArmed = false; Write("auto cancelled: another world-load callback arrived"); status = "Automatic trial cancelled; inspect log."; return; }
        if (!autoArmed) return;
        autoArmed = false;
        if (!isolated || dirty || target != null || autoPending) { Write("auto rejected: isolation or prior capture"); status = "Automatic load rejected; inspect log."; return; }
        autoPending = true; Write("auto phase=world-load generation=" + worldGeneration);
        StartCoroutine(AutoWhenReady(worldGeneration));
    }
    private IEnumerator AutoWhenReady(int generation)
    {
        float deadline = Time.realtimeSinceStartup + 30f;
        float stableSince = 0f;
        object stableGraph = null;
        Vector3 stableOrigin = Vector3.zero;
        while (generation == worldGeneration && Time.realtimeSinceStartup < deadline)
        {
            bool ready = false;
            object currentGraph = null;
            Vector3 currentOrigin = Vector3.zero;
            try
            {
                currentGraph = Read(TypeNamed("Track.Graph"), "Shared");
                if (isolated && Convert.ToString(Read(TypeNamed("Game.ActiveMapDefinition"), "Identifier")) == "crankyant/scene-operations-map" && currentGraph != null)
                {
                    var m = At("World/Mile Posts/T-46 Origin", "Track.TrackMarker");
                    var r = At("World/Mile Posts/T-46 Origin", "Track.TrackRelativePosition");
                    var other = At("World/Mile Posts/T-93", "Track.TrackMarker");
                    ready = m != null && r != null && other != null && Read(m, "Location") != null && Read(other, "Location") != null;
                    if (ready) currentOrigin = Origin();
                }
            }
            catch (Exception) { ready = false; }
            if (!ready) { stableGraph = null; stableSince = 0f; }
            else if (stableGraph == null || !ReferenceEquals(currentGraph, stableGraph) || (currentOrigin - stableOrigin).sqrMagnitude >= 0.0001f)
            {
                stableGraph = currentGraph; stableOrigin = currentOrigin; stableSince = Time.realtimeSinceStartup;
            }
            else if (Time.realtimeSinceStartup - stableSince >= 5f)
            {
                Write("auto phase=stable-ready seconds=5 origin=" + currentOrigin.ToString("F4"));
                RunAutomatic(generation);
                yield break;
            }
            yield return null;
        }
        if (generation == worldGeneration) { autoPending = false; Write("auto phase=readiness timeout; no mutation attempted"); status = "Automatic readiness timed out; no mutation."; }
    }
    private void RunAutomatic(int generation)
    {
        try
        {
            Write("auto phase=readiness generation=" + generation + " ready=True; capture once");
            Capture();
            if (target == null || dirty) { Write("auto phase=capture rejected; no mutation attempted"); return; }
            Write("auto phase=baseline source=world-load");
            Snapshot("auto baseline");
            bool ok = ApplyTrial();
            Write("auto phase=apply returned=" + ok + " dirty=" + dirty + " patched=" + patched);
            if (ok) Snapshot("auto applied");
        }
        catch (Exception e) { Write("auto ERROR " + e); status = dirty ? "Automatic trial failed; restore or discard." : "Automatic preflight failed; no mutation."; }
        finally { autoPending = false; }
    }
    public void OnUnload()
    {
        worldGeneration++; autoPending = false;
        if (dirty) autoArmed = false;
        try { if (subscribed) RemoveHandler(); if (dirty) Write("UNLOADED DIRTY: restoration was not verified; discard this session"); }
        catch (Exception e) { Write("unload cleanup ERROR " + e); }
        finally { DropCapture(); status = "World unloaded. Prior trial cannot be restored."; }
    }
    private void DropCapture()
    {
        graph = null; originalLocation = null; destinationLocation = null; originalLast = null;
        marker = null; relative = null; target = null; parent = null; groupEvent = null; disableHandler = null;
        oldGroup = null; newGroup = null; markerId = null; capturedScene = 0;
        dirty = false; patched = false; explicitDisable = false; subscribed = false;
    }
    private static Type TypeNamed(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null); }
    private static object Read(object o, string name)
    {
        var t = o as Type ?? o.GetType(); var instance = o is Type ? null : o;
        const BindingFlags f = Flags | BindingFlags.Static;
        var field = t.GetField(name, f); if (field != null) return field.GetValue(instance);
        var property = t.GetProperty(name, f); if (property != null) return property.GetValue(instance, null);
        throw new MissingMemberException(t.FullName, name);
    }
    private static Vector3 Origin() { return (Vector3)TypeNamed("Helpers.WorldTransformer").GetMethod("WorldToGame", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(Vector3) }, null).Invoke(null, new object[] { Vector3.zero }); }
    private static Component At(string path, string type)
    {
        var matches = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && c.GetType().FullName == type && PathOf(c.transform) == path).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException(path + " " + type + " count=" + matches.Length);
        return matches[0];
    }
    private int NativeSubscriptions()
    {
        var handlers = Read(graph, "GroupEnabledDidChange") as Delegate;
        return handlers == null ? 0 : handlers.GetInvocationList().Count(d => ReferenceEquals(d.Target, relative) && d.Method.Name == "OnGraphGroupEnabledDidChange");
    }
    private static bool SameLocation(object left, object right)
    {
        if (left == null || right == null) return false;
        return ReferenceEquals(Read(left, "segment"), Read(right, "segment")) && Equals(Read(left, "distance"), Read(right, "distance")) && Equals(Read(left, "end"), Read(right, "end"));
    }
    private int Registrations(string phase, object expected)
    {
        if (!SameLocation(Read(marker, "Location"), expected)) throw new InvalidOperationException("Marker location differs from expected binding");
        var segment = Read(expected, "segment") ?? throw new InvalidOperationException("Expected segment missing");
        string expectedBucket = Convert.ToString(Read(segment, "id"));
        var buckets = Read(graph, "_namedLocationsBySegmentId") as IDictionary ?? throw new InvalidOperationException("No graph buckets"); int count = 0;
        foreach (DictionaryEntry bucket in buckets)
        {
            var entries = bucket.Value as IDictionary ?? throw new InvalidOperationException("Unexpected bucket");
            foreach (DictionaryEntry entry in entries) if (Convert.ToString(entry.Key) == markerId)
            {
                count++; var item = entry.Value; bool matches = ReferenceEquals(Read(item, "ObjectValue"), marker);
                bool bucketMatches = string.Equals(Convert.ToString(bucket.Key), expectedBucket, StringComparison.Ordinal);
                bool locationMatches = SameLocation(Read(item, "Location"), expected);
                bool idMatches = string.Equals(Convert.ToString(Read(item, "Id")), markerId, StringComparison.Ordinal);
                Write(phase + " registration bucket=" + bucket.Key + " expectedBucket=" + expectedBucket + " matchingObject=" + matches + " matchingId=" + idMatches + " matchingLocation=" + locationMatches + " location=" + Show(Read(item, "Location")));
                if (!matches || !bucketMatches || !locationMatches || !idMatches) throw new InvalidOperationException("Registration object, id, bucket or location differs from expected binding");
            }
        }
        return count;
    }
    private void Snapshot(string phase)
    {
        int registrationCount = Registrations(phase, patched ? destinationLocation : originalLocation);
        int expectedCount = target.gameObject.activeInHierarchy && ((Behaviour)marker).enabled ? 1 : 0;
        if (registrationCount != expectedCount) throw new InvalidOperationException("Unexpected registration count for marker activity");
        Write(phase + " origin=" + Origin().ToString("F4") + " graph=" + graph.GetHashCode() + " location=" + Show(Read(marker, "Location")) + " observed=" + Read(relative, "_observedGroupId") + " observing=" + Read(relative, "_observing") + " nativeSubscriptions=" + NativeSubscriptions() + " patchSubscribed=" + subscribed + " active=" + target.gameObject.activeSelf + " hierarchy=" + target.gameObject.activeInHierarchy + " markerEnabled=" + ((Behaviour)marker).enabled + " registrations=" + registrationCount);
        var handlers = Read(graph, "GroupEnabledDidChange") as Delegate;
        if (handlers != null) foreach (var handler in handlers.GetInvocationList()) Write(phase + " subscription=" + handler.Method.DeclaringType.FullName + "." + handler.Method.Name + " targetIsRelative=" + ReferenceEquals(handler.Target, relative));
        Write(phase + " parent=" + PathOf(parent) + " position=" + parent.position.ToString("F4") + " rotation=" + parent.rotation.ToString("F4") + " local=" + parent.localPosition.ToString("F4"));
        foreach (var t in target.GetComponentsInChildren<Transform>(true)) Write(phase + " node=" + PathOf(t) + " pos=" + t.position.ToString("F4") + " rot=" + t.rotation.ToString("F4") + " local=" + t.localPosition.ToString("F4") + " active=" + t.gameObject.activeSelf);
        Write(phase + " placement offset=" + Show(Read(relative, "targetOffset")) + " angle=" + Read(relative, "targetAngle") + " terrain=" + Read(relative, "snapToTerrain") + " oldEnabled=" + Enabled(oldGroup) + " newEnabled=" + Enabled(newGroup));
    }
    private bool Enabled(string group) { return ((IList)Read(graph, "enabledGroupIds")).Contains(group); }
    private void Check()
    {
        if (!isolated || target == null || marker == null || relative == null || !target.gameObject.scene.IsValid() || !target.gameObject.scene.isLoaded || target.gameObject.scene.handle != capturedScene || marker.gameObject != target.gameObject || relative.gameObject != target.gameObject || !ReferenceEquals(Read(TypeNamed("Track.Graph"), "Shared"), graph) || target.parent != parent || (Origin() - origin).sqrMagnitude >= 0.0001f || Convert.ToString(Read(TypeNamed("Game.ActiveMapDefinition"), "Identifier")) != "crankyant/scene-operations-map") throw new InvalidOperationException("Isolation, world, graph, parent or origin changed. Abandon without saving.");
    }
    private void Capture()
    {
        try
        {
            if (!isolated) throw new InvalidOperationException("Isolation guard failed");
            if (target != null || dirty) throw new InvalidOperationException("Restore existing trial first");
            if (Convert.ToString(Read(TypeNamed("Game.ActiveMapDefinition"), "Identifier")) != "crankyant/scene-operations-map") throw new InvalidOperationException("Wrong map");
            var m = At("World/Mile Posts/T-46 Origin", "Track.TrackMarker"); var r = At("World/Mile Posts/T-46 Origin", "Track.TrackRelativePosition"); var other = At("World/Mile Posts/T-93", "Track.TrackMarker");
            var g = Read(TypeNamed("Track.Graph"), "Shared") ?? throw new InvalidOperationException("Graph unavailable");
            var original = Read(m, "Location") ?? throw new InvalidOperationException("Original location invalid"); var dest = Read(other, "Location") ?? throw new InvalidOperationException("Destination invalid");
            string oldId = Convert.ToString(Read(Read(original, "segment"), "groupId")); string newId = Convert.ToString(Read(Read(dest, "segment"), "groupId"));
            if (oldId != "sy-db" || newId != "na-an" || !ReferenceEquals(Read(r, "targetTransform"), m.transform) || !(bool)Read(r, "targetActiveFollowsTrackGroup") || !(bool)Read(r, "_observing") || Convert.ToString(Read(r, "_observedGroupId")) != oldId || Read(r, "_setTargetActive") != null || !((Behaviour)m).enabled || !m.gameObject.activeInHierarchy || m.transform.parent == null) throw new InvalidOperationException("Marker/group baseline differs");
            graph = g; marker = m; relative = r; target = m.transform; parent = target.parent; originalLocation = original; destinationLocation = dest; oldGroup = oldId; newGroup = newId; markerId = Convert.ToString(Read(marker, "id"));
            capturedScene = target.gameObject.scene.handle;
            if (markerId != "Mdow" || !Enabled(oldGroup) || !Enabled(newGroup) || NativeSubscriptions() != 1 || Registrations("capture", originalLocation) != 1) throw new InvalidOperationException("Registration or enabled-group baseline differs");
            origin = Origin(); position = target.position; rotation = target.rotation; localPosition = target.localPosition; localRotation = target.localRotation; originalActive = target.gameObject.activeSelf; offset = (Vector3)Read(relative, "targetOffset"); angle = Convert.ToSingle(Read(relative, "targetAngle")); terrain = (bool)Read(relative, "snapToTerrain"); originalLast = Read(relative, "<LastRuntimeApplySucceeded>k__BackingField"); nativeBaseline = NativeSubscriptions();
            groupEvent = graph.GetType().GetEvent("GroupEnabledDidChange", Flags) ?? throw new MissingMemberException("GroupEnabledDidChange"); disableHandler = EnforceDisable;
            Snapshot("baseline"); status = "Baseline captured. Rebind only in disposable unsaved session.";
        }
        catch (Exception e) { Write("capture ERROR " + e); if (!dirty) DropCapture(); status = "Capture failed; see log."; }
    }
    private void SetLocation(object location)
    {
        var b = (Behaviour)marker; if (!b.enabled || !target.gameObject.activeInHierarchy) throw new InvalidOperationException("Marker must be active before binding");
        b.enabled = false; try { marker.GetType().GetProperty("Location", Flags).SetValue(marker, location, null); } finally { b.enabled = true; }
    }
    private bool Configure(Vector3 v, float a, bool snap) { return (bool)relative.GetType().GetMethod("ConfigureAndApply", Flags).Invoke(relative, new object[] { target, v, a, snap }); }
    private void RemoveHandler() { if (subscribed) { groupEvent.RemoveEventHandler(graph, disableHandler); subscribed = false; } }
    private void AddHandler() { if (!subscribed) { groupEvent.AddEventHandler(graph, disableHandler); subscribed = true; } }
    private void EnforceDisable(string group, bool enabled) { if (explicitDisable && group == newGroup && enabled && target != null) { target.gameObject.SetActive(false); Write("explicit disable callback"); } }
    private bool ApplyTrial()
    {
        try
        {
            Check(); if (!Enabled(oldGroup) || !Enabled(newGroup)) throw new InvalidOperationException("Both groups must be enabled");
            bool retainDisable = explicitDisable;
            if (retainDisable) { RemoveHandler(); target.gameObject.SetActive(true); }
            dirty = true; if (!patched) SetLocation(destinationLocation);
            RemoveHandler(); bool ok = Configure(new Vector3(2f, 0f, 1f), 15f, false); patched = true;
            Snapshot("apply result=" + ok); if (!ok || Convert.ToString(Read(relative, "_observedGroupId")) != newGroup || NativeSubscriptions() != 1 || Registrations("apply check", destinationLocation) != 1) throw new InvalidOperationException("Apply postcondition failed");
            if (retainDisable) { AddHandler(); target.gameObject.SetActive(false); Snapshot("repeat explicit disabled"); }
            status = "Applied. Repeat or toggle groups, then restore.";
            return true;
        }
        catch (Exception e) { Write("apply ERROR " + e); status = "Apply failed. Restore or abandon without saving."; return false; }
    }
    private void SetGroup(string group, bool enabled) { graph.GetType().GetMethod("SetGroupEnabled", Flags).Invoke(graph, new object[] { group, enabled }); }
    private void ToggleTrial()
    {
        try
        {
            Check(); if (!patched || explicitDisable || !Enabled(oldGroup) || !Enabled(newGroup)) throw new InvalidOperationException("Apply first with both groups enabled");
            foreach (var group in new[] { oldGroup, newGroup }) { SetGroup(group, false); Snapshot("toggle " + group + " off"); bool expected = group == oldGroup; if (target.gameObject.activeSelf != expected) throw new InvalidOperationException("Unexpected activity after disable " + group); SetGroup(group, true); Snapshot("toggle " + group + " on"); if (!target.gameObject.activeSelf) throw new InvalidOperationException("Target did not activate"); }
            status = "Group toggles recorded. Inspect telemetry and visible placement.";
        }
        catch (Exception e) { Write("toggle ERROR " + e); status = "Toggle failed. Restore or abandon without saving."; }
    }
    private void DisableTrial()
    {
        try
        {
            Check(); if (!patched || !Enabled(oldGroup) || !Enabled(newGroup)) throw new InvalidOperationException("Apply first with both groups enabled");
            explicitDisable = true; AddHandler(); target.gameObject.SetActive(false); Snapshot("explicit disabled"); SetGroup(newGroup, false); Snapshot("explicit group off"); SetGroup(newGroup, true); Snapshot("explicit group on");
            if (target.gameObject.activeSelf) throw new InvalidOperationException("Explicit disable lost"); status = "Explicit disable recorded. Restore before leaving.";
        }
        catch (Exception e) { Write("disable ERROR " + e); status = "Disable failed. Restore or abandon without saving."; }
    }
    private void RestoreTrial()
    {
        try
        {
            Check(); if (!dirty) { DropCapture(); status = "Unused baseline released."; return; }
            explicitDisable = false; RemoveHandler(); SetGroup(oldGroup, true); SetGroup(newGroup, true); target.gameObject.SetActive(true);
            SetLocation(originalLocation); patched = false; bool ok = Configure(offset, angle, terrain);
            target.position = position; target.rotation = rotation; target.gameObject.SetActive(originalActive);
            relative.GetType().GetField("<LastRuntimeApplySucceeded>k__BackingField", Flags).SetValue(relative, originalLast);
            Snapshot("restored apply=" + ok);
            if (!SameLocation(Read(marker, "Location"), originalLocation) || Convert.ToString(Read(relative, "_observedGroupId")) != oldGroup || NativeSubscriptions() != nativeBaseline || Registrations("restore check", originalLocation) != 1 || (target.localPosition - localPosition).sqrMagnitude > 0.0001f || Quaternion.Angle(target.localRotation, localRotation) > 0.01f || target.gameObject.activeSelf != originalActive) throw new InvalidOperationException("Restoration check failed");
            foreach (var group in new[] { oldGroup, newGroup }) { SetGroup(group, false); Snapshot("restored toggle " + group + " off"); bool expected = group == newGroup; if (target.gameObject.activeSelf != expected) throw new InvalidOperationException("Restored group disable behavior differs"); SetGroup(group, true); Snapshot("restored toggle " + group + " on"); if (!target.gameObject.activeSelf) throw new InvalidOperationException("Restored activation failed"); }
            if (NativeSubscriptions() != nativeBaseline || Registrations("final check", originalLocation) != 1 || !SameLocation(Read(marker, "Location"), originalLocation)) throw new InvalidOperationException("Final restored state differs");
            DropCapture(); status = "Restored baseline. Check log and verify groups visually.";
        }
        catch (Exception e) { Write("restore ERROR " + e); status = "Restore failed. Abandon session without saving."; }
    }
}
