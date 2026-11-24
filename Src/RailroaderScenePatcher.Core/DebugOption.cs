// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RailroaderScenePatcher.Core {
    // The patcher's settings file in its own mod folder. Read returns null while the file does not exist.
    public interface ISettingsStore {
        string Read();
        void Write(string text);
    }

    // Debug logging: off by default, stored in the patcher's settings file, and in effect at once. It is available only
    // while the Preferences hook that shows its checkbox is compatible; otherwise the log stays in player mode.
    public sealed class DebugOption {
        public const string FileName = "settings.json";
        public const string Key = "debugLogging";
        readonly ISettingsStore store;
        public DebugOption(ISettingsStore store, bool available) {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            Available = available;
            string text = null;
            try { text = store.Read(); }
            catch (Exception e) { PatcherLog.Warning("Debug logging is off because the patcher's " + FileName + " can't be read", e); }
            stored = text != null && Read(text, true);
            Apply();
        }
        public bool Available { get; }
        bool stored;
        // The checkbox state; an unavailable option is off whatever the settings file holds.
        public bool On => Available && stored;
        public void Set(bool on) {
            if (!Available) return;
            string text = null;
            try { text = store.Read(); } catch { }
            stored = on;
            Apply();
            // The option takes effect for this session even when it can't be stored.
            try { store.Write(With(text, on)); }
            catch (Exception e) { PatcherLog.Error("Debug logging is " + (on ? "on" : "off") + " for this session only, because the patcher's " + FileName + " can't be saved", e); }
        }
        void Apply() { PatcherLog.DebugEnabled = On; }
        public static bool Read(string text) => Read(text, false);
        static bool Read(string text, bool warn) {
            if (String.IsNullOrWhiteSpace(text)) return false;
            try {
                var value = JObject.Parse(text)[Key];
                if (value == null) return false;
                if (value.Type == JTokenType.Boolean) return (bool)value;
            } catch (JsonException) { }
            if (warn) PatcherLog.Warning("Debug logging is off because the patcher's " + FileName + " has no true or false value for " + Key + ".");
            return false;
        }
        // The settings file text with the debug option set, keeping its other values.
        public static string With(string text, bool on) {
            JObject settings = null;
            try { settings = String.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text); } catch (JsonException) { }
            settings = settings ?? new JObject();
            settings[Key] = on;
            return settings.ToString(Formatting.Indented);
        }
    }
}
