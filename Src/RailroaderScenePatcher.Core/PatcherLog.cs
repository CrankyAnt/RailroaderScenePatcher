// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RailroaderScenePatcher.Core {
    // Debug lines are written only while the debug option is on. Warnings and errors make up player mode and are always written.
    public enum LogLevel { Debug, Warning, Error }
    // Where the patcher's log lines go: the player log in the game, a recording in tests.
    public interface ILogOutput { void Write(LogLevel level, string text); }

    // The one logging entry point of the patcher. Every log line passes through here, so the debug option decides in one
    // place what a session writes, and no line carries an absolute path, which would name the player's user folder in a
    // log that players post publicly. The game's Log window rows are not written here: the game writes those itself.
    public static class PatcherLog {
        static readonly object gate = new object();
        static readonly List<KeyValuePair<string, string>> roots = new List<KeyValuePair<string, string>>();
        public static ILogOutput Output { get; set; }
        // The debug option: on adds debug lines to the player-mode warnings and errors.
        public static bool DebugEnabled { get; set; }
        public static void Warning(string text) => Write(LogLevel.Warning, text);
        public static void Error(string text) => Write(LogLevel.Error, text);
        // A failure with its exception: player mode gets the base message, debug mode also the full exception.
        public static void Error(string text, Exception failure) {
            Write(LogLevel.Error, failure == null ? text : text + ": " + failure.GetBaseException().Message);
            if (failure != null) Debug(failure.ToString);
        }
        public static void Warning(string text, Exception failure) {
            Write(LogLevel.Warning, failure == null ? text : text + ": " + failure.GetBaseException().Message);
            if (failure != null) Debug(failure.ToString);
        }
        public static void Debug(string text) { if (DebugEnabled) Write(LogLevel.Debug, text); }
        // A debug line that costs work to build is built only while the debug option is on.
        public static void Debug(Func<string> text) {
            if (!DebugEnabled || text == null) return;
            string built;
            try { built = text(); } catch { return; }
            Write(LogLevel.Debug, built);
        }
        // Leaves a known folder out of every line, replaced by a label; an empty label leaves the relative path. A null
        // root clears the known folders.
        public static void HideRoot(string root, string label) {
            lock (gate) {
                if (root == null) { roots.Clear(); return; }
                var trimmed = root.TrimEnd('\\', '/');
                if (trimmed.Length == 0) return;
                roots.Add(new KeyValuePair<string, string>(trimmed, label ?? ""));
                roots.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            }
        }
        static void Write(LogLevel level, string text) {
            var output = Output;
            if (output == null || text == null) return;
            try { output.Write(level, WithoutAbsolutePaths(text)); } catch { /* Logging cannot stop the work it reports. */ }
        }
        // Folders up to a file name: a drive path on Windows, or a home or system folder elsewhere.
        static readonly Regex drivePath = new Regex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/](?:[^\\/\r\n""'<>|:*?]*[\\/])*", RegexOptions.CultureInvariant);
        static readonly Regex homePath = new Regex(@"(?<![\w.~])/(?:home|Users|mnt|media|Volumes|var|tmp|opt|private|usr|run|root)/(?:[^/\r\n""'<>|:*?]*/)*", RegexOptions.CultureInvariant);
        public static string WithoutAbsolutePaths(string text) {
            if (String.IsNullOrEmpty(text)) return text;
            KeyValuePair<string, string>[] known;
            lock (gate) known = roots.ToArray();
            foreach (var root in known)
                foreach (var separator in new[] { '\\', '/' })
                    foreach (var form in new[] { root.Key, root.Key.Replace('\\', '/'), root.Key.Replace('/', '\\') }.Distinct()) {
                        var prefix = form + separator;
                        for (var at = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(prefix, at + root.Value.Length, StringComparison.OrdinalIgnoreCase))
                            text = text.Substring(0, at) + root.Value + text.Substring(at + prefix.Length);
                    }
            return homePath.Replace(drivePath.Replace(text, ""), "");
        }
    }

    // Writes a repeated failure once per key, such as a failure that recurs every frame. Clear starts over, for example
    // at a new load attempt.
    public sealed class LogOnce {
        readonly HashSet<string> written = new HashSet<string>(StringComparer.Ordinal);
        public bool First(string key) { lock (written) return written.Add(key ?? ""); }
        public void Warning(string key, string text) { if (First(key)) PatcherLog.Warning(text); }
        public void Error(string key, string text) { if (First(key)) PatcherLog.Error(text); }
        public void Debug(string key, Func<string> text) { if (PatcherLog.DebugEnabled && First(key)) PatcherLog.Debug(text); }
        public void Clear() { lock (written) written.Clear(); }
    }
}
