// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CrankyAnt.CrossingExpansionResearch
{
    // One disposable-world experiment. The host supplies the log sink and drives each step.
    public sealed class EquipmentResearch
    {
        private const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const string Root = "World/Large Scenery/Bryson/Grade Crossing - Bryson Everett";
        private const string AddedName = "CrankyAnt Research Added Signal";
        private readonly Action<string> log;
        private GameObject wrapper, clone;
        private Component controller, originalLamp, originalGate, cloneLamp, cloneGate, culler;
        private Transform originalArm, cloneArm;
        private object[] cullerBefore;
        private Component[] spansBefore;
        private Vector3 sourcePosition;
        private Quaternion sourceRotation;
        private int sourceId;
        private Component sourceKvo;
        private Collider[] sourceColliders;
        private string[] sourceColliderStates;
        private bool cullerEnabled;
        private object sourceClip, sourceTargets;
        private Vector3 sourceScale;
        private string lastFrame;
        private Quaternion lastOriginalRotation, lastCloneRotation;
        public bool Added { get { return wrapper != null && wrapper.activeInHierarchy; } }
        public EquipmentResearch(Action<string> write) { log = write ?? throw new ArgumentNullException(nameof(write)); }
        private static Type TypeNamed(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null); }
        private static object Read(object target, string name) { var t = target as Type ?? target.GetType(); var instance = target is Type ? null : target; for (; t != null; t = t.BaseType) { var p = t.GetProperty(name, F | BindingFlags.DeclaredOnly); if (p != null) return p.GetValue(instance, null); var f = t.GetField(name, F | BindingFlags.DeclaredOnly); if (f != null) return f.GetValue(instance); } throw new MissingMemberException(name); }
        private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
        private static bool Loaded(Component c) { return c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded; }
        private static Component Exactly(GameObject obj, string type) { var matches = obj.GetComponents<Component>().Where(c => c != null && c.GetType().FullName == type).ToArray(); if (matches.Length != 1) throw new InvalidOperationException("Expected one " + type + " on " + PathOf(obj.transform)); return matches[0]; }
        private static object[] Items(object x) { return ((IEnumerable)x).Cast<object>().ToArray(); }
        private static Component[] Spans(Component owner) { return owner.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().FullName == "Track.TrackSpan").ToArray(); }
        private static Component Guard() {
            if (Convert.ToString(Read(TypeNamed("Game.ActiveMapDefinition"), "Identifier")) != "crankyant/crossing-expansion-research") throw new InvalidOperationException("Wrong map.");
            var matches = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => Loaded(c) && c.GetType().FullName == "Track.Crossings.GradeCrossingController" && PathOf(c.transform) == Root).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Controller count " + matches.Length);
            return matches[0];
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static Transform Child(Transform parent, string name) { var found = Enumerable.Range(0, parent.childCount).Select(parent.GetChild).Where(t => t.name == name).ToArray(); Require(found.Length == 1, "Child identity " + name + " count=" + found.Length); return found[0]; }
        private static void Device(Transform outer, out Component lamp, out Component gate, out Transform arm) {
            var inner = Child(outer, "Signal 1"); var model = Child(inner, "Crossing Signal Wishbone A (1)"); arm = Child(model, "Gate");
            lamp = Exactly(inner.gameObject, "Track.KeyValueLampMaterialController"); gate = Exactly(model.gameObject, "RollingStock.Controls.KeyValueBoolAnimator");
            Require(Convert.ToString(Read(lamp, "key")) == "lights" && Convert.ToString(Read(gate, "key")) == "gates", "Unexpected keys.");
            var targets = Items(Read(lamp, "targetObjects")); Require(targets.Length == 1 && ReferenceEquals(targets[0], model.gameObject), "Lamp target is not the model in this device.");
            Require(Read(gate, "animationClip") is UnityEngine.Object clip && clip != null, "Missing gate animation clip.");
            Require(model.GetComponents<Component>().Any(x => x != null && x.GetType().FullName == "UnityEngine.Animator"), "Missing Animator.");
            Require(!outer.GetComponentsInChildren<Component>(true).Any(c => c != null && (c.GetType().FullName == "Track.TrackSpan" || c.GetType().FullName == "Track.Crossings.GradeCrossingController" || c.GetType().FullName == "KeyValue.Runtime.KeyValueObject")), "Unexpected ownership component inside source.");
        }
        private static Component NearestKvo(Transform node) {
            for (var t = node; t != null; t = t.parent) {
                var matches = t.GetComponents<Component>().Where(c => c != null && c.GetType().FullName == "KeyValue.Runtime.KeyValueObject").ToArray();
                Require(matches.Length <= 1, "Multiple KVOs on " + PathOf(t));
                if (matches.Length == 1) return matches[0];
            }
            throw new InvalidOperationException("No parent KVO for " + PathOf(node));
        }
        private static string RelativePath(Transform root, Transform child) {
            if (child == root) return ".";
            Require(child != null && child.parent != null, "Collider outside device.");
            return RelativePath(root, child.parent) + "/" + child.name;
        }
        private static Collider[] Colliders(Transform root) { return root.GetComponentsInChildren<Collider>(true); }
        private static string ColliderState(Collider c) { return c.GetType().FullName + ":" + c.enabled + ":" + c.isTrigger + ":" + (c.sharedMaterial == null ? 0 : c.sharedMaterial.GetInstanceID()); }
        private static void CheckColliders(Transform source, Transform copy) {
            var originals = Colliders(source); var copies = Colliders(copy);
            Require(originals.Length == copies.Length, "Collider count changed.");
            for (var i = 0; i < originals.Length; i++) {
                var a = originals[i]; var b = copies[i];
                Require(!ReferenceEquals(a, b) && a.GetType() == b.GetType() && RelativePath(source, a.transform) == RelativePath(copy, b.transform) && a.enabled == b.enabled && a.isTrigger == b.isTrigger && ReferenceEquals(a.sharedMaterial, b.sharedMaterial), "Collider remapping mismatch at " + i);
            }
        }
        private void OriginalIntact() {
            Require(ReferenceEquals(Guard(), controller), "Controller changed.");
            var source = Child(controller.transform, "Signal 1"); Require(source.GetInstanceID() == sourceId && source.position == sourcePosition && source.rotation == sourceRotation && source.localScale == sourceScale, "Original root changed.");
            Device(source, out var lamp, out var gate, out var arm); Require(ReferenceEquals(lamp, originalLamp) && ReferenceEquals(gate, originalGate) && ReferenceEquals(arm, originalArm), "Original components changed.");
            Require(ReferenceEquals(NearestKvo(source), sourceKvo) && ReferenceEquals(NearestKvo(controller.transform), sourceKvo), "Original KVO changed.");
            Require(ReferenceEquals(Read(gate, "animationClip"), sourceClip) && Items(Read(lamp, "targetObjects")).SequenceEqual((object[])sourceTargets), "Original device references changed.");
            Require(Colliders(source).SequenceEqual(sourceColliders) && Colliders(source).Select(ColliderState).SequenceEqual(sourceColliderStates), "Original colliders changed.");
            Require(((Behaviour)culler).enabled == cullerEnabled, "Culler enabled state changed.");
            Require(Items(Read(culler, "gameObjects")).SequenceEqual(cullerBefore), "Original culler references changed.");
            Require(Spans(controller).SequenceEqual(spansBefore), "Original spans changed.");
        }
        public void Add() {
            Require(wrapper == null, "Remove current addition first."); controller = Guard();
            Require(controller.transform.gameObject.activeInHierarchy, "Controller inactive.");
            Require(!Enumerable.Range(0, controller.transform.childCount).Select(controller.transform.GetChild).Any(t => t.name == AddedName), "Added name already present.");
            var source = Child(controller.transform, "Signal 1"); Require(source.gameObject.activeInHierarchy, "Source inactive.");
            Device(source, out originalLamp, out originalGate, out originalArm); sourceId = source.GetInstanceID(); sourcePosition = source.position; sourceRotation = source.rotation; sourceScale = source.localScale;
            sourceKvo = NearestKvo(source); Require(ReferenceEquals(NearestKvo(controller.transform), sourceKvo), "Source KVO is not controller KVO.");
            sourceColliders = Colliders(source); sourceColliderStates = sourceColliders.Select(ColliderState).ToArray(); sourceClip = Read(originalGate, "animationClip"); sourceTargets = Items(Read(originalLamp, "targetObjects"));
            log("EQUIPMENT source kvo=" + PathOf(sourceKvo.transform) + " clip=" + ((UnityEngine.Object)sourceClip).name + " gateHierarchy=" + PathOf(originalArm) + " colliders=" + sourceColliders.Length);
            culler = Exactly(controller.gameObject, "Helpers.Culling.GameObjectCuller"); cullerBefore = Items(Read(culler, "gameObjects")); cullerEnabled = ((Behaviour)culler).enabled; spansBefore = Spans(controller);
            Require(cullerBefore.Length == 2 && cullerBefore.Contains(source.gameObject) && spansBefore.Length == 6, "Unexpected culler or span baseline.");
            try {
                wrapper = new GameObject(AddedName); wrapper.SetActive(false); wrapper.transform.SetParent(controller.transform, false);
                clone = UnityEngine.Object.Instantiate(source.gameObject, wrapper.transform, false); clone.name = "Signal 1";
                clone.transform.position = source.position + new Vector3(5f, 0f, 0f); clone.transform.rotation = source.rotation;
                Device(clone.transform, out cloneLamp, out cloneGate, out cloneArm);
                Require(!ReferenceEquals(cloneLamp, originalLamp) && !ReferenceEquals(cloneGate, originalGate) && !ReferenceEquals(cloneArm, originalArm), "Clone references original component.");
                Require(ReferenceEquals(Read(cloneGate, "animationClip"), Read(originalGate, "animationClip")), "Animation asset changed.");
                Require(ReferenceEquals(NearestKvo(clone.transform), sourceKvo), "Clone KVO differs before activation.");
                CheckColliders(source, clone.transform); OriginalIntact(); wrapper.SetActive(true);
                Require(ReferenceEquals(NearestKvo(clone.transform), sourceKvo), "Clone KVO differs after activation.");
                CheckColliders(source, clone.transform); OriginalIntact(); lastFrame = null;
                log("EQUIPMENT culler enabled=" + ((Behaviour)culler).enabled + " entries unchanged; cloneGateHierarchy=" + PathOf(cloneArm));
                log("EQUIPMENT added source=" + PathOf(source) + " clone=" + PathOf(clone.transform) + " offsetWorld=(5,0,0); culler unchanged; gate clip shared; targets remapped");
            } catch { if (wrapper != null) { wrapper.SetActive(false); UnityEngine.Object.Destroy(wrapper); } wrapper = null; clone = null; throw; }
        }
        private void LampSample(string label, Component lamp) {
            var maps = (IEnumerable)Read(lamp, "_rendererMaterialMap"); var count = 0;
            foreach (var item in maps) {
                var key = Read(item, "Key"); var renderer = (Renderer)Read(key, "Item1"); var index = Convert.ToInt32(Read(key, "Item2")); var target = Read(item, "Value"); var property = Convert.ToString(Read(target, "materialProperty"));
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, index); log("EQUIPMENT lamp=" + label + " renderer=" + PathOf(renderer.transform) + " materialIndex=" + index + " property=" + property + " color=" + block.GetColor(property)); count++;
            }
            log("EQUIPMENT lamp=" + label + " observer=" + (Read(lamp, "_observer") != null) + " parameter=" + Read(lamp, "_lastColorParameter") + " materialMappings=" + count);
        }
        private string LampPhase(Component lamp) {
            var values = new System.Collections.Generic.List<string>();
            foreach (var item in (IEnumerable)Read(lamp, "_rendererMaterialMap")) {
                var key = Read(item, "Key"); var renderer = (Renderer)Read(key, "Item1"); var index = Convert.ToInt32(Read(key, "Item2"));
                var property = Convert.ToString(Read(Read(item, "Value"), "materialProperty")); var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, index);
                var color = block.GetColor(property);
                values.Add(index + ":" + Mathf.RoundToInt(color.r * 10) + "," + Mathf.RoundToInt(color.g * 10) + "," + Mathf.RoundToInt(color.b * 10) + "," + Mathf.RoundToInt(color.a * 10));
            }
            return "observer=" + (Read(lamp, "_observer") != null) + " colors=" + string.Join(";", values.ToArray());
        }
        public void Frame() {
            if (!Added) return;
            var state = "lights=" + Read(controller, "SignalLights") + " gates=" + Read(controller, "SignalGates") + " originalLamp=" + LampPhase(originalLamp) + " cloneLamp=" + LampPhase(cloneLamp) + " originalGateObserver=" + (Read(originalGate, "_observer") != null) + " cloneGateObserver=" + (Read(cloneGate, "_observer") != null);
            if (lastFrame != state || Quaternion.Angle(lastOriginalRotation, originalArm.localRotation) >= 5f || Quaternion.Angle(lastCloneRotation, cloneArm.localRotation) >= 5f) {
                log("EQUIPMENT frame=" + Time.frameCount + " " + state + " originalGateLocal=" + originalArm.localRotation + " cloneGateLocal=" + cloneArm.localRotation);
                lastFrame = state; lastOriginalRotation = originalArm.localRotation; lastCloneRotation = cloneArm.localRotation;
            }
        }
        public void Sample(string label) {
            Require(wrapper != null && clone != null && wrapper.activeInHierarchy, "Addition not active."); OriginalIntact(); Require(ReferenceEquals(NearestKvo(clone.transform), sourceKvo), "Clone KVO changed."); CheckColliders(originalLamp.transform.parent, cloneLamp.transform.parent);
            log("EQUIPMENT sample=" + label + " lights=" + Read(controller, "SignalLights") + " gates=" + Read(controller, "SignalGates") + " bell=" + Read(controller, "SignalBell") + " originalGateLocal=" + originalArm.localRotation + " cloneGateLocal=" + cloneArm.localRotation + " originalGateObserver=" + (Read(originalGate, "_observer") != null) + " cloneGateObserver=" + (Read(cloneGate, "_observer") != null));
            LampSample("original", originalLamp); LampSample("clone", cloneLamp);
        }
        public void Remove() { Require(wrapper != null, "No addition."); OriginalIntact(); wrapper.SetActive(false); UnityEngine.Object.Destroy(wrapper); log("EQUIPMENT removal requested; verify after a frame."); }
        public void VerifyRemoved() { Require(wrapper == null, "Destroy has not completed."); OriginalIntact(); Require(!Enumerable.Range(0, controller.transform.childCount).Select(controller.transform.GetChild).Any(t => t.name == AddedName), "Added wrapper remains."); clone = null; log("EQUIPMENT removal verified: wrapper absent; source, culler and spans intact."); }
    }
}
