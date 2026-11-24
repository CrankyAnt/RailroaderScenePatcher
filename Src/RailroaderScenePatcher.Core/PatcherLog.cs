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
        // Leaves a known folder out of every line, replaced by a label such as ~; an empty label leaves the relative
        // path. A null root clears the known folders.
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
        // One folder name: any characters a file system allows, with an apostrophe only inside a name, as in O'Neil, so
        // the quote that closes a quoted path is no part of a folder name.
        const string Folder = @"(?:[^\\/\r\n""'<>|:*?]|'(?=[^\s\\/'""]))*";
        // Folders up to a file name: a drive path or a network share on Windows, or a home or system folder elsewhere.
        static readonly Regex drivePath = new Regex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/](?:" + Folder + @"[\\/])*", RegexOptions.CultureInvariant);
        static readonly Regex sharePath = new Regex(@"(?<![\\/\w])\\\\" + Folder + @"\\(?:" + Folder + @"\\)*", RegexOptions.CultureInvariant);
        static readonly Regex homePath = new Regex(@"(?<![\w.~])/(?:home|Users|mnt|media|Volumes|var|tmp|opt|private|usr|run|root)/(?:" + Folder.Replace(@"\\/", "/") + @"/)*", RegexOptions.CultureInvariant);
        public static string WithoutAbsolutePaths(string text) {
            if (String.IsNullOrEmpty(text)) return text;
            KeyValuePair<string, string>[] known;
            lock (gate) known = roots.ToArray();
            foreach (var root in known)
                foreach (var separator in new[] { '\\', '/' })
                    foreach (var form in new[] { root.Key, root.Key.Replace('\\', '/'), root.Key.Replace('/', '\\') }.Distinct()) {
                        var prefix = form + separator;
                        // A label keeps the separator after it, as in ~\AppData; no label leaves the relative path.
                        var replacement = root.Value.Length == 0 ? "" : root.Value + separator;
                        for (var at = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(prefix, at + replacement.Length, StringComparison.OrdinalIgnoreCase))
                            text = text.Substring(0, at) + replacement + text.Substring(at + prefix.Length);
                    }
            return homePath.Replace(sharePath.Replace(drivePath.Replace(text, ""), ""), "");
        }
    }

    // Writes a repeated failure once per key, such as a failure that recurs every frame. Clear starts over, for example
    // at a new load attempt.
    public sealed class LogOnce {
        readonly HashSet<string> written = new HashSet<string>(StringComparer.Ordinal);
        public bool First(string key) { lock (written) return written.Add(key ?? ""); }
        public void Warning(string key, string text) { if (First(key)) PatcherLog.Warning(text); }
        // With the exception: its base message in the player line and the full exception as a debug line.
        public void Warning(string key, string text, Exception failure) { if (First(key)) PatcherLog.Warning(text, failure); }
        public void Error(string key, string text) { if (First(key)) PatcherLog.Error(text); }
        public void Debug(string key, Func<string> text) { if (PatcherLog.DebugEnabled && First(key)) PatcherLog.Debug(text); }
        public void Clear() { lock (written) written.Clear(); }
    }

    // Writes a debug diagnostic that is checked again and again, such as a frame check, the first time per key and again
    // only when its text changes. The text is built only while the debug option is on. Clear starts over, for example at
    // a new load attempt.
    public sealed class LogOnChange {
        readonly Dictionary<string, string> last = new Dictionary<string, string>(StringComparer.Ordinal);
        // The key of one scene object. Objects can share a path and a label, such as the pads of crossings that share one:
        // its identity, such as the game's instance ID, keeps each on its own entry.
        public static string ObjectKey(string path, string label, int identity) =>
            (path ?? "") + "|" + (label ?? "") + "#" + identity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Whether the text differs from the last one recorded for the key; a changed text is recorded.
        public bool Changed(string key, string text) {
            lock (last) {
                key = key ?? "";
                if (last.TryGetValue(key, out var known) && String.Equals(known, text, StringComparison.Ordinal)) return false;
                last[key] = text;
                return true;
            }
        }
        public void Debug(string key, Func<string> text) {
            if (!PatcherLog.DebugEnabled || text == null) return;
            string built;
            try { built = text(); } catch { return; }
            if (built != null && Changed(key, built)) PatcherLog.Debug(built);
        }
        public void Clear() { lock (last) last.Clear(); }
    }
}
