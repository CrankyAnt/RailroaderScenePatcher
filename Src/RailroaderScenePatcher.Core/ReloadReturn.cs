// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    public sealed class ReloadLocation {
        internal ReloadLocation(JObject value, int controller) {
            RequestId = (string)value["id"];
            Point = ((JArray)value["point"]).Values<double>().ToArray();
            Requested = (int?)value["arriving"] == controller;
        }
        public string RequestId { get; }
        public IReadOnlyList<double> Point { get; }
        public bool Requested { get; }
    }
    // A process-lifetime handoff, independent of mod instances or loaded copies of the core assembly.
    // Only a BCL string lives in the AppDomain. No callbacks, game objects or mod-defined types are retained.
    public sealed class ReloadReturn {
        readonly string key;
        public ReloadReturn(string key = "crankyant/scene-patcher/editor-return") { this.key = key; }
        public string Capture(string mapId, int originController, double[] point, DateTime now) {
            if (String.IsNullOrEmpty(mapId) || !Finite(point)) throw new ArgumentException("A return needs a map and finite map location");
            var id = Guid.NewGuid().ToString("N");
            Write(new JObject { ["id"] = id, ["map"] = mapId, ["origin"] = originController, ["point"] = new JArray(point),
                ["created"] = now.Ticks, ["expires"] = now.AddMinutes(5).Ticks });
            return id;
        }
        JObject Read() {
            var text = AppDomain.CurrentDomain.GetData(key) as string;
            return text == null ? null : JObject.Parse(text);
        }
        void Write(JObject value) { AppDomain.CurrentDomain.SetData(key, value.ToString(Newtonsoft.Json.Formatting.None)); }
        public ReloadLocation Pending(string mapId, int controller, DateTime now) {
            var value = Read();
            if (value == null) return null;
            if ((string)value["map"] != mapId || now.Ticks < (long)value["created"] || now.Ticks >= (long)value["expires"]) { Cancel(); return null; }
            return controller == (int)value["origin"] ? null : new ReloadLocation(value, controller);
        }
        public bool Active(DateTime now) {
            var value = Read();
            if (value == null) return false;
            if (now.Ticks < (long)value["created"] || now.Ticks >= (long)value["expires"]) { Cancel(); return false; }
            return true;
        }
        public bool CanReload(DateTime now, Action<string> feedback) {
            if (!Active(now)) return true;
            feedback?.Invoke("Reload refused because the return to the working location is still pending. " + WindowGuidance.WaitForReturn);
            return false;
        }
        public bool MarkRequested(string requestId, string mapId, int controller, DateTime now) {
            var pending = Pending(mapId, controller, now);
            if (pending == null || pending.RequestId != requestId) return false;
            var value = Read(); value["arriving"] = controller; Write(value); return true;
        }
        public bool Complete(string requestId, string mapId, int controller, double[] measured, DateTime now) {
            var pending = Pending(mapId, controller, now);
            if (pending == null || pending.RequestId != requestId || !pending.Requested || !Finite(measured)) return false;
            // Working area is horizontal map position; native terrain loading determines ground height.
            var dx = measured[0] - pending.Point[0]; var dz = measured[2] - pending.Point[2];
            if (dx * dx + dz * dz > 0.25) return false;
            Cancel(); return true;
        }
        static bool Finite(double[] point) => point != null && point.Length == 3 && point.All(x => !Double.IsNaN(x) && !Double.IsInfinity(x));
        public void Cancel() { AppDomain.CurrentDomain.SetData(key, null); }
    }
}
