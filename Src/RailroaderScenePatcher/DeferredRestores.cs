// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.State;
using HarmonyLib;
using RailroaderScenePatcher.Core;
using UnityEngine;

namespace RailroaderScenePatcher {
    // Defers, never suppresses, the game's restore-time callback of a scene object whose data an applicable scene patch file
    // changes. RestoreNotifier.NotifyDidRestore runs these callbacks before the mods' OnWorldLoad, so such a callback reads
    // the scene's data before the patcher applies its repair. While the notifier is in progress, the prefix skips a callback
    // only when DeferredRestoreRule says it would act on data an applicable file changes. At the end of OnWorldLoad, on
    // unload, and at the latest through DeferredRestoreRelay once the notifier is complete, every skipped callback goes back
    // to the game through RestoreNotifier.RegisterForRestore, so the game runs its own code one frame later, after the
    // apply: with the repair's result, or with the game's own error when the repair did not apply. Any internal failure
    // runs the original. Each hooked type is one row of Hooks; a type is listed only where the decompiled game shows that
    // its restore-time callback reads data a scene patch file changes.
    internal static class DeferredRestores {
        const string Id = "crankyant.scene-patcher.deferred-restore";
        // One restore-time callback the patcher can defer: the component type, its GameBehaviour.OnEnableWithProperties
        // override, and whether its call on the component would act on data the loaded scene patch files change.
        sealed class Hook {
            internal string TypeName;
            internal Func<Component, RestoreTargets, bool> Defer;
            internal Type Type;
            internal MethodInfo Method;
        }
        static readonly Hook[] Hooks = {
            // Game.Mow.TrackConstructionScenery.OnEnableWithProperties calls UpdateVisibility, which dereferences each
            // segment that segmentIds names on the track graph; a saved id a native split removed throws there.
            new Hook { TypeName = BakedSceneryRules.ConstructionController, Defer = DeferConstruction },
        };
        sealed class Skipped { internal Component Instance; internal Hook Hook; }
        static Harmony harmony;
        static readonly List<Skipped> skipped = new List<Skipped>();
        static RestoreTargets targets;
        static object targetsNotifier, targetsGraph;
        static FieldInfo notifierState;
        static PropertyInfo enablePriority;
        // Why the hook is not installed, or null.
        internal static string Error { get; private set; }
        internal static void Install() {
            try {
                notifierState = typeof(RestoreNotifier).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("RestoreNotifier._state");
                enablePriority = typeof(Game.GameBehaviour).GetProperty("EnablePriority", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMemberException("GameBehaviour.EnablePriority");
                harmony = new Harmony(Id);
                foreach (var hook in Hooks) {
                    hook.Type = Native.Type(hook.TypeName) ?? throw new TypeLoadException(hook.TypeName + " is unavailable");
                    hook.Method = hook.Type.GetMethod("OnEnableWithProperties", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (hook.Method == null || hook.Method.ReturnType != typeof(void) || !typeof(Game.GameBehaviour).IsAssignableFrom(hook.Type)) throw new MissingMethodException(hook.TypeName + ".OnEnableWithProperties");
                    harmony.Patch(hook.Method, prefix: new HarmonyMethod(typeof(DeferredRestores), nameof(Before)));
                }
            } catch (Exception e) { harmony?.UnpatchAll(Id); harmony = null; Error = "Deferred restore hook unavailable: " + e.GetBaseException().Message; }
        }
        internal static void Remove() { HandBack(); harmony?.UnpatchAll(Id); harmony = null; }
        static bool InProgress(RestoreNotifier notifier) => notifier != null && notifierState != null && notifierState.GetValue(notifier)?.ToString() == "InProgress";
        // Returns false to skip the original call; true runs it. Every failure runs it.
        static bool Before(Component __instance) {
            try {
                if (__instance == null) return true;
                var notifier = RestoreNotifier.Shared;
                if (!InProgress(notifier)) return true;
                // A component enabled again while the notifier runs keeps its one deferred call.
                if (skipped.Any(s => ReferenceEquals(s.Instance, __instance))) return false;
                var hook = Hooks.FirstOrDefault(h => h.Type != null && h.Type.IsInstanceOfType(__instance));
                if (hook == null || !hook.Defer(__instance, Targets(notifier))) return true;
                // The relay must exist before the call is skipped: without it, the call runs.
                DeferredRestoreRelay.Arm();
                skipped.Add(new Skipped { Instance = __instance, Hook = hook });
                return false;
            } catch { return true; }
        }
        // The loaded scene patch files' modifications, read once per load attempt: per restore notifier and track graph.
        static RestoreTargets Targets(RestoreNotifier notifier) {
            var graph = Track.Graph.Shared;
            if (targets != null && ReferenceEquals(targetsNotifier, notifier) && ReferenceEquals(targetsGraph, graph)) return targets;
            targetsNotifier = notifier; targetsGraph = graph;
            try { targets = RestoreTargets.Read(ModScenePatchFiles.Discover(), ModScenePatchFiles.MapIds(), ModScenePatchFiles.MapPatchIds()); }
            catch { targets = RestoreTargets.Empty; }
            return targets;
        }
        static bool DeferConstruction(Component controller, RestoreTargets loaded) {
            var go = controller.gameObject;
            if (!go.scene.IsValid()) return false;
            var settings = loaded.ConstructionSegments(go.scene.name, BoardScene.PathOf(go.transform), new[] { BakedSceneryGroups.ComponentType, ConstructionSceneryScene.ComponentType });
            if (settings.Count == 0) return false;
            var graph = Track.Graph.Shared;
            if (graph == null) return false;
            return DeferredRestoreRule.DeferConstruction(settings, ConstructionSegmentNative.Read(controller), id => graph.GetSegment(id) != null);
        }
        // Hands every skipped callback back to the game's restore notifier, which runs it in its next LateUpdate. A callback
        // whose component was destroyed or disabled since is dropped. A disabled construction controller is not unregistered
        // (TrackConstructionScenery.OnDisable does not call GameBehaviour.OnDisable), but enabling it again registers it
        // through GameBehaviour.OnEnable, so the game runs its callback then. Nothing is handed back while the notifier is
        // still in progress; the calls wait for the next hand-back, at the latest the relay's once the notifier is complete.
        // Without a notifier, as after the map unloaded, nothing is left to run them.
        internal static bool Pending => skipped.Count > 0;
        internal static bool Complete => RestoreNotifier.Shared == null || notifierState?.GetValue(RestoreNotifier.Shared)?.ToString() == "Complete";
        internal static void HandBack() {
            try {
                var notifier = RestoreNotifier.Shared;
                if (InProgress(notifier)) return;
                var calls = skipped.ToArray();
                skipped.Clear(); targets = null; targetsNotifier = null; targetsGraph = null;
                if (notifier == null) return;
                foreach (var call in calls) {
                    try {
                        if (call.Instance == null || !(call.Instance is Behaviour behaviour) || !behaviour.isActiveAndEnabled) continue;
                        var action = (Action)Delegate.CreateDelegate(typeof(Action), call.Instance, call.Hook.Method);
                        notifier.RegisterForRestore((int)enablePriority.GetValue(call.Instance, null), call.Instance, action);
                    } catch (Exception e) { PatcherLog.Warning(call.Instance + " may not show its saved state, because the patcher couldn't hand its restore back to the game", e); }
                }
            } catch (Exception e) { skipped.Clear(); PatcherLog.Warning("Some objects may not show their saved state, because the patcher couldn't hand their restores back to the game", e); }
        }
    }
    // Hands the skipped callbacks back once, in a frame after the skip and once the restore notifier is complete, so a restore
    // that no OnWorldLoad follows cannot hold a skipped call until unload. After OnWorldLoad has handed them back, it finds
    // nothing left and only removes itself.
    internal sealed class DeferredRestoreRelay : MonoBehaviour {
        static DeferredRestoreRelay armed;
        int frame;
        internal static void Arm() {
            if (armed != null) return;
            var host = new GameObject("CrankyAnt Scene Patcher deferred restore");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            armed = host.AddComponent<DeferredRestoreRelay>();
            armed.frame = Time.frameCount;
        }
        void Update() {
            if (Time.frameCount <= frame) return;
            try {
                if (DeferredRestores.Pending && !DeferredRestores.Complete) return;
                DeferredRestores.HandBack();
            } catch { }
            // A skip later in this frame arms a new relay.
            if (armed == this) armed = null;
            Destroy(gameObject);
        }
        void OnDestroy() { if (armed == this) armed = null; }
    }
}
