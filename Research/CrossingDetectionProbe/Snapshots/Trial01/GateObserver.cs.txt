// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CrankyAnt.CrossingDetectionResearch
{
    // Read-only, frame-level observations of the two existing gate devices.
    public sealed class GateObserver
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        readonly Component controller;
        readonly Func<Component> guard;
        readonly Action<string> log;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Device[] devices;
        string outputs, previousKey; double nextGuard;
        sealed class Device { public Transform outer, model, arm; public Component consumer, animator; public Quaternion previous; public Vector3 previousPosition, lastAxis; public bool moving; public double lastReport; public string previousPlayable; }
        static object Read(object target, string name) { var t = target.GetType(); for (; t != null; t = t.BaseType) { var p = t.GetProperty(name, F | BindingFlags.DeclaredOnly); if (p != null) return p.GetValue(target, null); var f = t.GetField(name, F | BindingFlags.DeclaredOnly); if (f != null) return f.GetValue(target); } throw new MissingMemberException(name); }
        static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
        static Transform Child(Transform parent, string name) { var matches = Enumerable.Range(0, parent.childCount).Select(parent.GetChild).Where(t => t.name == name).ToArray(); if (matches.Length != 1) throw new InvalidOperationException("Expected one child " + name + " under " + PathOf(parent)); return matches[0]; }
        static string Vec(Vector3 v) { return string.Format(CultureInfo.InvariantCulture, "({0:R},{1:R},{2:R})", v.x, v.y, v.z); }
        static string Quat(Quaternion q) { return string.Format(CultureInfo.InvariantCulture, "({0:R},{1:R},{2:R},{3:R})", q.x, q.y, q.z, q.w); }
        string TimeTag() { return "elapsedSeconds=" + clock.Elapsed.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture) + " ticks=" + clock.ElapsedTicks + " frame=" + Time.frameCount; }
        string Outputs() { return "lights=" + Read(controller, "SignalLights") + " gates=" + Read(controller, "SignalGates") + " bell=" + Read(controller, "SignalBell"); }
        public GateObserver(Component owner, Func<Component> resolve, Action<string> write)
        {
            controller = owner; guard = resolve; log = write; devices = new[] { Capture("Signal 1"), Capture("Signal 2") };
            outputs = Outputs(); previousKey = KeyState(); log("gate-observer begin " + TimeTag() + " stopwatchFrequency=" + Stopwatch.Frequency + " " + outputs);
            foreach (var d in devices) { var state = PlayableState(d); d.previousPlayable = state; Report(d, "initial", 0, previousKey, previousKey, state, state); }
        }
        Device Capture(string name)
        {
            var outer = Child(controller.transform, name);
            var consumers = outer.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().FullName == "RollingStock.Controls.KeyValueBoolAnimator" && Convert.ToString(Read(c, "key")) == "gates").ToArray();
            if (consumers.Length != 1) throw new InvalidOperationException("Gate consumer count for " + name + "=" + consumers.Length);
            var model = consumers[0].transform; var arm = Child(model, "Gate");
            var animators = model.GetComponents<Component>().Where(c => c != null && c.GetType().FullName == "UnityEngine.Animator").ToArray();
            if (animators.Length != 1) throw new InvalidOperationException("Animator count for " + name + "=" + animators.Length);
            log("gate-observer device outer=" + PathOf(outer) + "#" + outer.GetInstanceID() + " model=" + PathOf(model) + "#" + model.GetInstanceID() + " arm=" + PathOf(arm) + "#" + arm.GetInstanceID() + " consumer=" + consumers[0].GetInstanceID() + " animator=" + animators[0].GetInstanceID());
            return new Device { outer = outer, model = model, arm = arm, consumer = consumers[0], animator = animators[0], previous = arm.localRotation, previousPosition = arm.localPosition };
        }
        string AnimatorState(Device d)
        {
            try { var a = d.animator; var layers = Convert.ToInt32(Read(a, "layerCount")); var text = " animatorEnabled=" + ((Behaviour)a).enabled + " animatorActive=" + a.gameObject.activeInHierarchy + " animatorSpeed=" + Read(a, "speed") + " animatorLayers=" + layers; if (layers > 0) { var state = a.GetType().GetMethod("GetCurrentAnimatorStateInfo", F, null, new[] { typeof(int) }, null).Invoke(a, new object[] { 0 }); text += " stateHash=" + Read(state, "fullPathHash") + " stateTime=" + Read(state, "normalizedTime"); } return text; } catch (Exception e) { return " animatorStateUnavailable=" + e.GetType().Name; }
        }
        static string SafeRead(object target, string name) { if (target == null) return "null"; try { return Convert.ToString(Read(target, name), CultureInfo.InvariantCulture) ?? "null"; } catch (Exception e) { return "unavailable:" + e.GetType().Name; } }
        string KeyState()
        {
            try {
                var owner = Read(controller, "_keyValueObject"); if (owner == null) return "owner=null";
                var keys = ((System.Collections.IEnumerable)Read(owner, "Keys")).Cast<object>().Select(Convert.ToString).ToArray();
                bool present = keys.Contains("gates", StringComparer.Ordinal);
                var get = owner.GetType().GetMethod("Get", F, null, new[] { typeof(string) }, null);
                if (get == null) return "present=" + present + ",get=unavailable";
                var value = get.Invoke(owner, new object[] { "gates" });
                if (value == null) return "present=" + present + ",value=null";
                return "present=" + present + ",type=" + SafeRead(value, "Type") + ",isNull=" + SafeRead(value, "IsNull") + ",bool=" + SafeRead(value, "BoolValue");
            } catch (Exception e) { return "unavailable=" + e.GetType().Name + ":" + (e.InnerException?.GetType().Name ?? "none"); }
        }
        string PlayableState(Device d)
        {
            try {
                var clip = Read(d.consumer, "animationClip"); var handle = Read(d.consumer, "_playable");
                return "invert=" + SafeRead(d.consumer, "invert") + ",configuredSpeed=" + SafeRead(d.consumer, "speed") + ",clipLength=" + (clip == null ? "null" : SafeRead(clip, "length")) + ",clipLoop=" + (clip == null ? "null" : SafeRead(clip, "isLooping")) + ",playableTime=" + (handle == null ? "null" : SafeRead(handle, "Time")) + ",playableSpeed=" + (handle == null ? "null" : SafeRead(handle, "Speed")) + ",playState=" + (handle == null ? "null" : SafeRead(handle, "PlayState"));
            } catch (Exception e) { return "unavailable=" + e.GetType().Name + ":" + (e.InnerException?.GetType().Name ?? "none"); }
        }
        static float RotationDelta(Quaternion a, Quaternion b)
        {
            double dot = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w;
            double normA = Math.Sqrt((double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w);
            double normB = Math.Sqrt((double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w);
            if (normA == 0 || normB == 0) throw new InvalidOperationException("Invalid gate rotation.");
            return (float)(2 * Math.Acos(Math.Min(1, Math.Abs(dot / (normA * normB)))) * 180 / Math.PI);
        }
        void Report(Device d, string reason, float delta, string priorKey, string currentKey, string priorPlayable, string currentPlayable) { log("gate-observer " + TimeTag() + " reason=" + reason + " device=" + d.outer.name + " localRotation=" + Quat(d.arm.localRotation) + " localPosition=" + Vec(d.arm.localPosition) + " rotationDeltaDegrees=" + delta.ToString("R", CultureInfo.InvariantCulture) + " active=" + d.arm.gameObject.activeInHierarchy + " consumerEnabled=" + ((Behaviour)d.consumer).enabled + " " + outputs + " previousKey=[" + priorKey + "] currentKey=[" + currentKey + "] previousPlayable=[" + priorPlayable + "] currentPlayable=[" + currentPlayable + "]" + AnimatorState(d)); d.lastReport = clock.Elapsed.TotalSeconds; }
        public void Frame()
        {
            if (controller == null || !controller.gameObject.scene.isLoaded) throw new InvalidOperationException("Gate controller unloaded.");
            if (clock.Elapsed.TotalSeconds >= nextGuard) { if (!ReferenceEquals(guard(), controller)) throw new InvalidOperationException("Gate controller changed."); nextGuard = clock.Elapsed.TotalSeconds + 0.1; }
            var now = Outputs(); var key = KeyState(); var changed = now != outputs || key != previousKey; outputs = now;
            foreach (var d in devices) {
                if (d.outer == null || d.model == null || d.arm == null || d.consumer == null || d.animator == null || !ReferenceEquals(d.outer.parent, controller.transform) || !ReferenceEquals(d.consumer.transform, d.model) || !ReferenceEquals(d.arm.parent, d.model)) throw new InvalidOperationException("Gate device identity changed.");
                var playable = PlayableState(d); var rotation = d.arm.localRotation; var position = d.arm.localPosition; var delta = RotationDelta(d.previous, rotation); var offset = Vector3.Distance(d.previousPosition, position); var moving = delta > 0.01f || offset > 0.00001f;
                var q = rotation * Quaternion.Inverse(d.previous); var axis = new Vector3(q.x, q.y, q.z); if (q.w < 0) axis = -axis;
                var reverse = moving && d.moving && axis.sqrMagnitude > 1e-12f && d.lastAxis.sqrMagnitude > 1e-12f && Vector3.Dot(axis, d.lastAxis) < 0;
                if (changed || moving != d.moving || reverse || (moving && clock.Elapsed.TotalSeconds - d.lastReport >= 0.1)) Report(d, changed ? "output-change" : reverse ? "direction-change" : moving != d.moving ? (moving ? "motion-start" : "motion-stop") : "moving", delta, previousKey, key, d.previousPlayable, playable);
                if (axis.sqrMagnitude > 1e-12f) d.lastAxis = axis; d.previous = rotation; d.previousPosition = position; d.moving = moving; d.previousPlayable = playable;
            }
            previousKey = key;
        }
    }
}
