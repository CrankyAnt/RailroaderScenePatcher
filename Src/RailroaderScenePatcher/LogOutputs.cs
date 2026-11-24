// SPDX-FileCopyrightText: 2026 CrankyAnt
// SPDX-License-Identifier: MIT
using System;
using Railroader.Modding;
using RailroaderScenePatcher.Core;

namespace RailroaderScenePatcher {
    // The only places that hand log lines to the game. Everything else writes through PatcherLog.

    // Writes PatcherLog's lines to the player log through the game's mod logger, which tags each line with this mod.
    // The game writes its mod logger's debug level only when it starts with -logLevelDebug, so a debug line is written at
    // the information level and marked as debug.
    internal sealed class ModLogOutput : ILogOutput {
        readonly IModLogger log;
        internal ModLogOutput(IModLogger log) { this.log = log ?? throw new ArgumentNullException(nameof(log)); }
        public void Write(LogLevel level, string text) {
            var line = "Scene Patcher " + PatcherBehaviour.Version + (level == LogLevel.Debug ? " [debug]" : "") + ": " + text;
            // The mod logger reads its text as a message template, where braces name values.
            line = line.Replace("{", "{{").Replace("}", "}}");
            switch (level) {
                case LogLevel.Error: log.Error(line); break;
                case LogLevel.Warning: log.Warning(line); break;
                default: log.Info(line); break;
            }
        }
    }

    // The game's Log window. Each row's originator is the thing it is about and its source is this patcher's one name, as
    // native rows name a mod file or definition with their subsystem. The game writes each row to the player log as well.
    internal sealed class NativeRowOutput : INativeRowOutput {
        internal const string Source = "Railroader Scene Patcher";
        readonly Func<string> loadId;
        internal NativeRowOutput(Func<string> loadId) { this.loadId = loadId; }
        // Before collector initialization the message log falls back to the game's file log, which is not player presentation.
        public bool Ready => Game.Diagnostics.MessageCollector.Shared != null;
        public void Warning(string originator, string message, string key) => Game.Diagnostics.MessageLog.Warning(originator, message, Source, loadId() + key);
        // An informational row: it reports possible incompatibility and is never a failure warning.
        public void Notice(string originator, string message, string key) => Game.Diagnostics.MessageLog.Info(originator, message, Source, loadId() + key);
    }
}
