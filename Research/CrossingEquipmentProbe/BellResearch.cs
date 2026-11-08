// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CrankyAnt.CrossingExpansionResearch
{
    // A disposable second emitter. The controller retains ownership of the loaded asset.
    public sealed class BellResearch
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const string Root = "World/Large Scenery/Bryson/Grade Crossing - Bryson Everett";
        private const string AddedName = "CrankyAnt Research Added Bell";
        private readonly Action<string> log;
        private Component controller, original, added;
        private GameObject wrapper;
        private object assetReference, originalDescriptor, originalClips;
        private object[] originalClipItems;
        private AudioClip asset;
        private string lastState;
        private float nextSample;

        public BellResearch(Action<string> write) { log = write ?? throw new ArgumentNullException(nameof(write)); }
        private static Type TypeNamed(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null); }
        private static object Read(object target, string name) {
            var type = target as Type ?? target.GetType(); var instance = target is Type ? null : target;
            for (; type != null; type = type.BaseType) {
                var property = type.GetProperty(name, Flags | BindingFlags.DeclaredOnly); if (property != null) return property.GetValue(instance, null);
                var field = type.GetField(name, Flags | BindingFlags.DeclaredOnly); if (field != null) return field.GetValue(instance);
            }
            throw new MissingMemberException(name);
        }
        private static void Write(object target, string name, object value) { var field = target.GetType().GetField(name, Flags); if (field == null) throw new MissingFieldException(name); field.SetValue(target, value); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
        private static object[] Items(object value) { return value == null ? new object[0] : ((IEnumerable)value).Cast<object>().ToArray(); }
        private static Component Guard() {
            Require(Convert.ToString(Read(TypeNamed("Game.ActiveMapDefinition"), "Identifier")) == "crankyant/crossing-expansion-research", "Wrong map.");
            var matches = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded && c.GetType().FullName == "Track.Crossings.GradeCrossingController" && PathOf(c.transform) == Root).ToArray();
            Require(matches.Length == 1, "Controller count " + matches.Length); return matches[0];
        }
        private void OriginalIntact() {
            Require(ReferenceEquals(Guard(), controller) && controller.gameObject.activeInHierarchy, "Controller changed or inactive.");
            Require(ReferenceEquals(Read(controller, "_bellPlayer"), original) && original != null && original.gameObject.activeInHierarchy, "Original player changed or inactive.");
            Require(ReferenceEquals(Read(controller, "_audioLoadReference"), assetReference) && ReferenceEquals(Read(assetReference, "Asset"), asset) && asset != null, "Loaded asset changed.");
            Require(ReferenceEquals(Read(original, "indexedClip"), originalDescriptor) && ReferenceEquals(Read(original, "_clips"), originalClips) && Items(originalClips).SequenceEqual(originalClipItems), "Original clip references changed.");
        }
        public void Add() {
            Require(wrapper == null, "Remove current addition first."); controller = Guard();
            Require(controller.gameObject.activeInHierarchy, "Controller inactive.");
            Require(!Enumerable.Range(0, controller.transform.childCount).Select(controller.transform.GetChild).Any(t => t.name == AddedName), "Added name already present.");
            original = Read(controller, "_bellPlayer") as Component; Require(original != null && original.gameObject.activeInHierarchy, "Original player unavailable.");
            assetReference = Read(controller, "_audioLoadReference"); Require(assetReference != null, "Bell asset still loading or unavailable.");
            asset = Read(assetReference, "Asset") as AudioClip; Require(asset != null, "Loaded bell clip unavailable.");
            originalDescriptor = Read(original, "indexedClip");
            var trainController = Read(controller, "TrainController"); Require(trainController != null, "Train controller unavailable.");
            var library = Read(trainController, "ContentLibrary"); Require(library != null, "Content library unavailable.");
            var lookup = TypeNamed("Model.Database.DefinitionProviderExtensions").GetMethods(Flags).Single(m => m.Name == "DefinitionForIdentifier" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3);
            var identifier = Convert.ToString(Read(controller.GetType(), "DefaultCrossingBellIdentifier"));
            Require(identifier == "rr/bell-uss-teardrop-crossing-rh", "Unexpected crossing bell identifier.");
            var definition = lookup.MakeGenericMethod(TypeNamed("Model.Definition.Data.BellDefinition")).Invoke(null, new object[] { library, identifier, null });
            Require(definition != null, "Crossing bell definition unavailable.");
            var audioDefinition = Read(definition, "Audio"); Require(audioDefinition != null, "Crossing bell audio definition unavailable.");
            var indexes = (float[])Read(audioDefinition, "Indexes");
            var volume = Convert.ToSingle(Read(audioDefinition, "Volume"));
            originalClips = Read(original, "_clips"); originalClipItems = Items(originalClips);
            Require(originalClips != null && originalClipItems.Length > 0, "Original player has not been configured.");
            Require(Math.Abs(Convert.ToSingle(Read(original, "volume")) - volume) < 0.0001f, "Original player volume differs from definition."); OriginalIntact();
            try {
                wrapper = new GameObject(AddedName); wrapper.SetActive(false); wrapper.transform.SetParent(controller.transform, false);
                wrapper.transform.position = controller.transform.position + new Vector3(5f, 0f, 0f);
                added = wrapper.AddComponent(TypeNamed("Audio.IntegerLoopingPlayer"));
                foreach (var name in new[] { "mixerGroup", "priority", "audioDistance", "randomize" }) Write(added, name, Read(original, name));
                Write(added, "play", false);
                var configure = added.GetType().GetMethod("Configure", Flags, null, new[] { typeof(AudioClip), typeof(float[]), typeof(float) }, null);
                Require(configure != null, "Configure method unavailable."); configure.Invoke(added, new object[] { asset, indexes, volume });
                Require(!ReferenceEquals(Read(added, "_clips"), originalClips), "Clip array shared with original.");
                OriginalIntact(); wrapper.SetActive(true); OriginalIntact();
                log("BELL added path=" + PathOf(wrapper.transform) + " source=" + PathOf(original.transform) + " indexes=" + (indexes == null ? 0 : indexes.Length) + " volume=" + volume + " mixer=" + Read(added, "mixerGroup") + " priority=" + Read(added, "priority") + " distance=" + Read(added, "audioDistance") + " colliders=" + wrapper.GetComponentsInChildren<Collider>(true).Length);
                Require(wrapper.GetComponentsInChildren<Collider>(true).Length == 0, "Unexpected added collider."); lastState = null; Frame();
            } catch { if (wrapper != null) { wrapper.SetActive(false); UnityEngine.Object.Destroy(wrapper); } wrapper = null; added = null; throw; }
        }
        public void Frame() {
            if (wrapper == null || !wrapper.activeInHierarchy) return;
            OriginalIntact(); var play = Convert.ToBoolean(Read(controller, "SignalBell")); Write(added, "play", play);
            var state = "signal=" + play + " originalPlay=" + Read(original, "play") + " addedPlay=" + Read(added, "play");
            if (state != lastState) { log("BELL frame=" + Time.frameCount + " " + state); lastState = state; }
            if(Time.realtimeSinceStartup >= nextSample) { nextSample = Time.realtimeSinceStartup + 1f; Sample("periodic"); }
        }
        private void Sources(string label, Component player) {
            var sources = Items(Read(player, "_sources")); log("BELL " + label + " pooledSources=" + sources.Length);
            foreach (var source in sources) {
                var component = source as Component;
                var audioType = TypeNamed("Audio.IAudioSource");
                var playing = audioType.GetProperty("isPlaying").GetValue(source, null); var time = audioType.GetProperty("time").GetValue(source, null); var clip = audioType.GetProperty("clip").GetValue(source, null) as AudioClip;
                log("BELL " + label + " source=" + (component == null ? source.GetType().FullName : PathOf(component.transform)) + (" isPlaying=" + playing + " time=" + time + " clip=" + (clip == null ? "null" : clip.name)));
            }
            foreach (var audio in player.GetComponentsInChildren<AudioSource>(true)) log("BELL " + label + " childAudio=" + PathOf(audio.transform) + " isPlaying=" + audio.isPlaying + " time=" + audio.time);
        }
        public void Sample(string label) { Require(wrapper != null && wrapper.activeInHierarchy, "Addition not active."); OriginalIntact(); log("BELL sample=" + label + " signal=" + Read(controller, "SignalBell") + " originalPlay=" + Read(original, "play") + " addedPlay=" + Read(added, "play")); Sources("original", original); Sources("added", added); }
        public void Remove() { Require(wrapper != null, "No addition."); OriginalIntact(); Write(added, "play", false); wrapper.SetActive(false); UnityEngine.Object.Destroy(wrapper); log("BELL removal requested; verify after a frame."); }
        public void VerifyRemoved() { Require(wrapper == null, "Destroy has not completed."); OriginalIntact(); Require(!Enumerable.Range(0, controller.transform.childCount).Select(controller.transform.GetChild).Any(t => t.name == AddedName), "Added bell remains."); added = null; log("BELL removal verified; original player and asset references intact."); }
    }
}
