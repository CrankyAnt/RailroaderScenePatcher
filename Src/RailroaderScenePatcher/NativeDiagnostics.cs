// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Game.Diagnostics;
using HarmonyLib;
using RailroaderScenePatcher.Core;
using Track;
using UnityEngine;

namespace RailroaderScenePatcher {
    internal sealed class NativePatchDiagnostics : NativeCurrentWarnings, IPatchDiagnostics {
        readonly Action<string, string> report;
        internal NativePatchDiagnostics(Action<string, string> report) { this.report = report; }
        public void Warning(PatchFailure failure) => report(failure.DiagnosticKey, failure.Message);
    }
    internal class NativeCurrentWarnings : ICurrentWarnings {
        public IEnumerable<CurrentWarning> Current => MessageCollector.Shared == null ? Enumerable.Empty<CurrentWarning>() : MessageCollector.Shared.Messages.Where(m => !m.IsRemoved && m.Severity == MessageSeverity.Warning).Select(Snapshot).ToArray();
        internal static CurrentWarning Snapshot(DiagnosticMessage message) => new CurrentWarning(message, message.Source, message.DeduplicationKey, message.Originator, message.Description);
        public void Remove(CurrentWarning row) {
            var message = row.Token as DiagnosticMessage;
            if (message != null && MessageCollector.Shared != null && MessageCollector.Shared.Messages.Contains(message)) MessageCollector.Shared.RemoveMessage(message);
        }
    }
    // This hook never skips the native check. It records only warnings emitted by that invocation of the original method.
    internal static class NativeRepairHooks {
        const string Id = "crankyant.scene-patcher.diagnostics";
        static Harmony harmony;
        static EntryPoint owner;
        static object graph;
        static object restoredMarker;
        // An intentional restoration must reach the game's check without reapplying the still-unsaved disk document.
        internal static void CheckRestoredBinding(object marker, Action nativeCheck) {
            var prior = restoredMarker; restoredMarker = marker;
            try { nativeCheck(); } finally { restoredMarker = prior; }
        }
        static readonly List<WarningAttribution> warnings = new List<WarningAttribution>();
        internal static IEnumerable<WarningAttribution> Warnings => warnings.ToArray();
        internal static string Error { get; private set; }
        internal sealed class CheckState {
            internal TrackMarker Marker;
            internal string Reason, Expected;
            internal MessageCollector Collector;
            internal Action<DiagnosticMessage> Observe;
            internal readonly List<CurrentWarning> Emitted = new List<CurrentWarning>();
        }
        internal static void Install(EntryPoint value) {
            owner = value;
            try {
                var method = typeof(TrackMarker).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException("TrackMarker.OnEnable");
                harmony = new Harmony(Id);
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeRepairHooks), nameof(Before)), postfix: new HarmonyMethod(typeof(NativeRepairHooks), nameof(After)), finalizer: new HarmonyMethod(typeof(NativeRepairHooks), nameof(Finish)));
            } catch (Exception e) { harmony?.UnpatchAll(Id); harmony = null; Error = "Early marker warning hook unavailable: " + e.GetBaseException().Message; }
        }
        internal static void Reset() { warnings.Clear(); graph = null; }
        internal static void Remove() { harmony?.UnpatchAll(Id); harmony = null; owner = null; Reset(); }
        static void Before(TrackMarker __instance, out CheckState __state) {
            __state = null;
            if (ReferenceEquals(restoredMarker, __instance)) return;
            try {
                if (!ReferenceEquals(graph, Graph.Shared)) { warnings.Clear(); graph = Graph.Shared; }
                var marker = __instance;
                if (marker == null || !marker.gameObject.scene.IsValid()) return;
                var location = marker.Location;
                if (location.HasValue && location.Value.IsValid) return;
                // Shared native marker IDs cannot identify one registration or diagnostic safely.
                if (String.IsNullOrEmpty(marker.id) || Resources.FindObjectsOfTypeAll<TrackMarker>().Count(m => m != null && m.gameObject.scene.IsValid() && m.id == marker.id) != 1) return;
                string reason;
                if (Graph.Shared == null || !Graph.Shared.HasPopulatedCollections) reason = "Graph unavailable or not populated before TrackMarker.OnEnable";
                else reason = owner == null ? "Patcher lifecycle unavailable before TrackMarker.OnEnable" : owner.BeforeMarkerCheck(marker);
                if (reason == null) return;
                var serialized = Native.Read(marker, "_location");
                var segment = Native.Read(serialized, "segmentId"); var distance = Convert.ToSingle(Native.Read(serialized, "distance"), CultureInfo.InvariantCulture);
                var state = new CheckState { Marker = marker, Reason = reason, Collector = MessageCollector.Shared,
                    Expected = "Track marker " + marker.id + " is not on the track at " + segment + " + " + distance.ToString("F1", CultureInfo.CurrentCulture) + "m and will have no effect." };
                if (state.Collector == null) return;
                state.Observe = message => {
                    if (message.Severity == MessageSeverity.Warning && message.Source == "System" && message.DeduplicationKey == marker.id && message.Originator == marker.name && message.Description == state.Expected)
                        state.Emitted.Add(NativePatchDiagnostics.Snapshot(message));
                };
                state.Collector.OnMessageAdded += state.Observe; state.Collector.OnMessageUpdated += state.Observe;
                __state = state;
            } catch { /* Readiness or attribution uncertainty leaves the game's diagnostic intact. */ }
        }
        static void After(CheckState __state, bool __runOriginal) {
            if (__state == null) return;
            Detach(__state);
            if (!__runOriginal || __state.Marker == null || __state.Emitted.Count != 1) return;
            warnings.RemoveAll(w => ReferenceEquals(w.Warning.Token, __state.Emitted[0].Token));
            warnings.Add(new WarningAttribution(__state.Emitted[0], __state.Marker.gameObject, __state.Reason));
        }
        static void Detach(CheckState state) {
            if (state?.Collector == null) return;
            state.Collector.OnMessageAdded -= state.Observe; state.Collector.OnMessageUpdated -= state.Observe;
        }
        static Exception Finish(Exception __exception, CheckState __state) { Detach(__state); return __exception; }
    }
}
